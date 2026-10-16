#!/usr/bin/env python3
"""bench-fannkuch.py - time examples/fannkuch-redux.maxon against the C reference.

The instrument of the fannkuch-redux optimization loop (bench/fannkuch/README.md). It builds the
vendored C gcc #5 program single-threaded with clang, builds the Maxon example with one or more
compilers, runs every binary interleaved, verifies each run's output and exit code, and reports
wall time per arm with the ratio against C. With --note it appends one row per Maxon arm to
docs/fannkuch-benchmark-log.md.

It is an INSTRUMENT: it exits 0 whatever the ratio is. Non-zero means the run itself is invalid -
a wrong answer, a leak (exit 101), a crash, or a build that failed.

Usage:
  python scripts/bench-fannkuch.py [--n 11] [--runs 5] [--profile] [--note "..."]
  python scripts/bench-fannkuch.py --ref <pre-change-sha>          # A/B: slot vs a control compiler
  python scripts/bench-fannkuch.py --compiler <path> --label x     # any compiler binary as an arm

Arms:
  --compiler PATH [--label L]   a compiler binary; default arm is the slot compiler, labelled "slot"
  --ref REF                     a compiler BUILT FROM a git ref, in a cached worktree under
                                bench/fannkuch/out/ref-<sha>/tree, by that tree's own
                                scripts/fetch-seed.sh and scripts/build-from-seed.sh.
                                A control arm for an A/B is `--ref <pre-change-sha>`.

A label names an arm's output directory AND its timing list, so labels are compared
case-insensitively, a duplicate is refused before anything is built, and `C` is reserved for the C
arm. Every arm compiles the SAME source with its own compiler (and its own checkout's stdlib), into
its own directory, so two arms never share a .ir or a cache.

Exit codes: 0 ok; 1 a verification failure (no log row is written); 2 a build failure; 3 usage.
"""
import argparse
import datetime
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import time

import bench_common

SIDECAR_SUFFIX = ".mxdbg"
DEFAULT_SOURCE = os.path.join(bench_common.REPO, "examples", "fannkuch-redux.maxon")
DEFAULT_C_SOURCE = os.path.join(bench_common.REPO, "bench", "fannkuch", "fannkuchredux.gcc-5.c")
DEFAULT_OUT_DIR = os.path.join(bench_common.REPO, "bench", "fannkuch", "out")
LOG_PATH = os.path.join(bench_common.REPO, "docs", "fannkuch-benchmark-log.md")
CENSUS_SCRIPT = os.path.join(bench_common.REPO, "scripts", "emitted-code-count.py")
WINDOWS_CLANG = r"C:\Program Files\LLVM\bin\clang.exe"
# Single-threaded on purpose: without -fopenmp the pragma is a comment and the blocks run in sequence.
CLANG_FLAGS = ["-O3", "-march=native", "-Wall", "-Wno-unknown-pragmas"]
C_LABEL = "C"
SOURCE_STEM = "fannkuch-redux"
# The self-compile band: a change that makes the compiler compile ITSELF this much slower than the
# control does is a HALT for the user (ruling 2026-09-08), whatever it bought the benchmark. The
# threshold sits above the ±3% build-to-build layout noise docs/emitted-code-roadmap.md records.
SELF_COMPILE_HALT_FRACTION = 0.05
SELF_COMPILE_SOURCE = "maxon-bin"

# (checksum, pancake count) per n. The pancake count is the Maxon program's exit code.
EXPECTED = {7: (228, 16), 10: (73196, 38), 11: (556355, 51), 12: (3968050, 65)}

CENSUS_COLUMNS = ("ops", "jmp", "jmp->next", "jmponly-blocks", "imul-imm", "imul-pow2", "idiv",
                  "call-direct", "mgd-call", "im-blocks", "mov")

LOG_HEADER = """# fannkuch-redux benchmark log

Wall time of `examples/fannkuch-redux.maxon` against the C gcc #5 reference
(`bench/fannkuch/fannkuchredux.gcc-5.c`, built single-threaded with clang), both run on this
machine by `scripts/bench-fannkuch.py`. **Read the tables downwards**: each row is one measurement,
dated, and the ratio column is the number the loop drives at 1.0.

- **Rows are appended by the tool, never by hand.** A run without `--note` records nothing; a run
  with one records unconditionally. The note says WHY the number moved - the instrument cannot.
- **This is an instrument, not a gate.** There is nothing to pass; a row that did not move is a
  datapoint, and a row that moved the wrong way is a reading to explain.
- **Rows rot.** Re-measure before planning from one; an A/B is two arms in ONE session, interleaved,
  never a before/after across a source change.
- **Only rows from one host compare.** `-march=native` makes the C figure machine-specific.
- `docs/optimization-log.md` is the compile-time axis and is never touched by this tool.

Host: Intel Core i7-8700 (6C/12T, 3.2 GHz), Windows 11, x64. C reference: clang, single-threaded.

Columns: `C min/med ms` and `Maxon min/med ms` are the minimum and median of `runs` interleaved
timed runs after one warm-up each; `ratio` is Maxon median / C median; `ops` is the emitted-code
census of the Maxon binary (`scripts/emitted-code-count.py`); `arm` names the compiler
(`slot`, `ref@<sha>`, or a `--label`); `tree` is whether the harness's checkout was clean.

| date | head | tree | n | runs | C min/med ms | Maxon min/med ms | ratio (med) | exe bytes | ops | arm | note |
|---|---|---|---|---|---|---|---|---|---|---|---|
"""


def load_census():
    spec = importlib.util.spec_from_file_location("emitted_code_count", CENSUS_SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.count


def find_clang(override):
    if override:
        return override
    if bench_common.IS_WINDOWS and os.path.exists(WINDOWS_CLANG):
        return WINDOWS_CLANG
    found = shutil.which("clang")
    if not found:
        raise bench_common.BuildFailure("no clang found; pass --clang <path>")
    return found


def build_c_reference(clang, c_source, out_dir):
    """Build the C arm, cached on the source bytes, the clang version and the flags."""
    c_dir = os.path.join(out_dir, "c")
    os.makedirs(c_dir, exist_ok=True)
    exe = os.path.join(c_dir, "fannkuchredux" + bench_common.EXE)
    stamp_path = os.path.join(c_dir, "fannkuchredux.stamp.json")
    version = bench_common.run([clang, "--version"])
    if version.returncode != 0:
        raise bench_common.BuildFailure(f"{clang} --version failed:\n{version.stderr}")
    clang_line = version.stdout.splitlines()[0].strip()
    stamp = {"source_sha256": bench_common.sha256_of(c_source), "clang": clang_line, "flags": CLANG_FLAGS}
    if os.path.exists(exe) and os.path.exists(stamp_path):
        with open(stamp_path, encoding="utf-8") as fh:
            if json.load(fh) == stamp:
                return exe, clang_line, True
    r = bench_common.run([clang] + CLANG_FLAGS + ["-o", exe, c_source], cwd=c_dir)
    if r.returncode != 0 or not os.path.exists(exe):
        raise bench_common.BuildFailure(f"clang failed (exit {r.returncode}):\n{r.stdout}\n{r.stderr}")
    with open(stamp_path, "w", encoding="utf-8") as fh:
        json.dump(stamp, fh, indent=2)
    return exe, clang_line, False


def build_maxon_arm(label, compiler, source, out_dir):
    """Compile the example with `compiler` into out/<label>/ and return the exe, the .ir and sizes."""
    arm = bench_common.build_arm(label, compiler, out_dir, source, SOURCE_STEM, build_args=["--emit-ir"])
    ir = os.path.join(arm["dir"], SOURCE_STEM + ".ir")
    if not os.path.exists(ir):
        raise bench_common.BuildFailure(f"{compiler} wrote no {os.path.basename(ir)} for arm {label} although --emit-ir was given")
    arm["ir"] = ir
    return arm


def expected_output(n, checksum, flips):
    return f"{checksum}\nPfannkuchen({n}) = {flips}\n"


def verify(arm_name, n, result, expect, is_c):
    """Refuse a run whose output or exit code is wrong. `expect` is (checksum, flips)."""
    checksum, flips = expect
    where = f"n={n}"
    bench_common.verify_exit(arm_name, where, result, expected_exit=0 if is_c else flips, leak_checked=not is_c)
    bench_common.verify_output(arm_name, where, result, expected_output(n, checksum, flips))


def parse_output(stdout):
    lines = stdout.replace("\r\n", "\n").split("\n")
    try:
        checksum = int(lines[0])
        flips = int(lines[1].rsplit("=", 1)[1])
    except (ValueError, IndexError):
        raise bench_common.VerificationFailure(f"{C_LABEL} printed {stdout!r}, not a checksum line and a Pfannkuchen line") from None
    return checksum, flips


def profile_arm(arm, n, hz):
    """`maxon profile run` with the arm's own compiler; the report is saved beside the binary."""
    cmd = [arm["compiler"], "profile", "run", f"--rate={hz}", arm["exe"], str(n)]
    try:
        r = bench_common.run(cmd, cwd=arm["dir"], timeout=bench_common.RUN_TIMEOUT_S)
    except subprocess.TimeoutExpired:
        raise bench_common.VerificationFailure(f"profile of {arm['label']} at n={n} timed out after {bench_common.RUN_TIMEOUT_S} s") from None
    if r.returncode != 0:
        raise bench_common.VerificationFailure(f"profile of {arm['label']} at n={n} failed (exit {r.returncode}):\n{r.stdout[-3000:]}\n{r.stderr[-3000:]}")
    path = os.path.join(arm["dir"], f"profile-n{n}.txt")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(r.stdout)
        fh.write(r.stderr)
    return path, r.stdout


def time_self_compile(arm):
    """Wall time of this arm's compiler compiling the tree's own compiler sources into a scratch stem."""
    stem = os.path.join(arm["dir"], "selfcompile", "maxon")
    os.makedirs(os.path.dirname(stem), exist_ok=True)
    # A build that finds its output up to date compiles nothing, and the timing would measure that.
    for leftover in (stem + bench_common.EXE, stem + bench_common.EXE + SIDECAR_SUFFIX):
        if os.path.exists(leftover):
            os.remove(leftover)
    start = time.perf_counter()
    try:
        r = bench_common.run([arm["compiler"], "build", SELF_COMPILE_SOURCE, f"--output={stem}"], cwd=bench_common.REPO)
    except subprocess.TimeoutExpired:
        raise bench_common.BuildFailure(f"self-compile with {arm['label']} timed out after {bench_common.BUILD_TIMEOUT_S} s") from None
    ms = (time.perf_counter() - start) * bench_common.MILLIS
    if r.returncode != 0 or not os.path.exists(stem + bench_common.EXE):
        raise bench_common.BuildFailure(f"self-compile with {arm['label']} failed (exit {r.returncode}):\n{r.stdout[-3000:]}\n{r.stderr[-3000:]}")
    return ms, os.path.getsize(stem + bench_common.EXE)


def hot_functions_section(report):
    lines = report.splitlines()
    out = []
    keep = False
    for line in lines:
        if line.startswith("=== hot functions"):
            keep = True
        elif line.startswith("=== ") and keep:
            break
        if keep:
            out.append(line)
    return "\n".join(out)


def append_log_row(row):
    if not os.path.exists(LOG_PATH):
        with open(LOG_PATH, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(LOG_HEADER)
    bench_common.append_table_row(LOG_PATH, row)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--n", type=int, default=11)
    bench_common.add_arm_arguments(ap)
    ap.add_argument("--no-c", action="store_true", help="skip the C reference")
    ap.add_argument("--profile", action="store_true", help="profile each Maxon arm with `maxon profile run`")
    ap.add_argument("--profile-n", type=int, default=None, help="n for the profile run (default --n)")
    ap.add_argument("--profile-hz", type=int, default=1000)
    ap.add_argument("--self-compile", action="store_true",
                    help="also time each arm's compiler compiling maxon-bin; the first arm is the control and any other arm slower by more than the halt band prints HALT")
    ap.add_argument("--note", default="", help="append a log row per Maxon arm with this note")
    ap.add_argument("--out-dir", default=DEFAULT_OUT_DIR)
    ap.add_argument("--source", default=DEFAULT_SOURCE)
    ap.add_argument("--c-source", default=DEFAULT_C_SOURCE)
    ap.add_argument("--clang", default=None)
    ap.add_argument("--json", action="store_true", help="also write <out-dir>/last-run.json")
    args = ap.parse_args()

    out_dir = os.path.abspath(args.out_dir)
    try:
        bench_common.check_arm_arguments(args)
        if args.no_c and args.n not in EXPECTED:
            raise bench_common.UsageFailure(f"n={args.n} has no expected output and --no-c removes the oracle")

        os.makedirs(out_dir, exist_ok=True)
        census = load_census()
        head, dirty = bench_common.tree_identity(bench_common.REPO)
        today = datetime.date.today().isoformat()

        # Ref arms first: an A/B's control is the pre-change ref, and the self-compile band is
        # measured against the FIRST arm.
        plan = bench_common.plan_arms(args, allow_no_arms=not args.no_c, reserved_labels=(C_LABEL,))

        c_exe = clang_line = None
        if not args.no_c:
            clang = find_clang(args.clang)
            c_exe, clang_line, cached = build_c_reference(clang, args.c_source, out_dir)

        maxon_arms = [build_maxon_arm(arm["label"], bench_common.arm_compiler(arm, out_dir), args.source, out_dir)
                      for arm in plan]
    except (bench_common.UsageFailure, bench_common.BuildFailure) as e:
        return bench_common.report_failure(e)

    print(f"bench-fannkuch  {today}  head {head}{'*' if dirty else ''}  n={args.n}  runs={args.runs}  warmup={args.warmup}")
    if c_exe:
        print(f"  C: {clang_line}  {' '.join(CLANG_FLAGS)}  (no -fopenmp)")
    for arm in maxon_arms:
        print(f"  {arm['label']}: {bench_common.describe_identity(arm['identity'])}  exe {arm['exe_bytes']:,} B")

    # Runnable arms: the C reference first, then every Maxon arm. Each round runs every arm once,
    # alternating direction so neither drift nor a fixed order can manufacture a sign.
    runnables = []
    if c_exe:
        runnables.append({"name": C_LABEL, "exe": c_exe, "dir": os.path.dirname(c_exe), "is_c": True})
    for arm in maxon_arms:
        runnables.append({"name": arm["label"], "exe": arm["exe"], "dir": arm["dir"], "is_c": False, "arm": arm})

    expect = EXPECTED.get(args.n)

    def measure_one(r):
        nonlocal expect
        ms, result = bench_common.timed_run(r["exe"], args.n, r["dir"])
        if expect is None:
            if not r["is_c"]:
                raise bench_common.VerificationFailure(f"n={args.n} is not in the expected table and the C run has not produced its oracle yet")
            expect = parse_output(result.stdout)
        verify(r["name"], args.n, result, expect, r["is_c"])
        return ms

    try:
        times = bench_common.interleave(runnables, lambda r: r["name"], args.warmup, args.runs, measure_one)
    except bench_common.VerificationFailure as e:
        return bench_common.report_failure(e)

    stats = {name: bench_common.summarize(ms) for name, ms in times.items()}
    c_stats = stats.get(C_LABEL)

    bench_common.print_timing_table([(r["name"], stats[r["name"]], None if r["is_c"] else c_stats) for r in runnables])

    censuses = {}
    print()
    print("  emitted-code census (scripts/emitted-code-count.py) per Maxon arm:")
    print("  " + f"{'arm':<16}" + "".join(f"{c:>15}" for c in CENSUS_COLUMNS))
    for arm in maxon_arms:
        with open(arm["ir"], encoding="utf-8") as fh:
            c = census(fh.read())
        censuses[arm["label"]] = c
        print("  " + f"{arm['label']:<16}" + "".join(f"{c[k]:>15}" for k in CENSUS_COLUMNS))

    self_compiles = {}
    halted = False
    if args.self_compile:
        print()
        print(f"  self-compile ({SELF_COMPILE_SOURCE}) per arm, control first:")
        control_ms = None
        for arm in maxon_arms:
            try:
                ms, size = time_self_compile(arm)
            except bench_common.BuildFailure as e:
                return bench_common.report_failure(e)
            self_compiles[arm["label"]] = {"ms": ms, "bytes": size}
            if control_ms is None:
                control_ms = ms
                print(f"  {arm['label']:<16} {bench_common.fmt_ms(ms):>10} ms   {size:,} B   (control)")
            else:
                delta = ms / control_ms - 1.0
                verdict = ""
                if delta > SELF_COMPILE_HALT_FRACTION:
                    verdict = f"   HALT: +{delta * 100:.1f}% over the control's self-compile (band {SELF_COMPILE_HALT_FRACTION * 100:.0f}%)"
                    halted = True
                print(f"  {arm['label']:<16} {bench_common.fmt_ms(ms):>10} ms   {size:,} B   {delta * 100:+.1f}%{verdict}")

    profiles = {}
    if args.profile:
        profile_n = args.profile_n if args.profile_n is not None else args.n
        for arm in maxon_arms:
            try:
                path, report = profile_arm(arm, profile_n, args.profile_hz)
            except bench_common.VerificationFailure as e:
                return bench_common.report_failure(e)
            profiles[arm["label"]] = path
            print()
            print(f"  profile of {arm['label']} at n={profile_n} -> {path}")
            for line in hot_functions_section(report).splitlines():
                print("  " + line)

    if args.n == 12 and c_stats:
        for arm in maxon_arms:
            s = stats[arm["label"]]
            print()
            print(f"VERDICT n=12 [{arm['label']}]: Maxon median {bench_common.fmt_ms(s['median'])} ms vs C median {bench_common.fmt_ms(c_stats['median'])} ms - ratio {s['median'] / c_stats['median']:.2f}")

    if args.note:
        for arm in maxon_arms:
            s = stats[arm["label"]]
            c_cell = f"{bench_common.fmt_ms(c_stats['min'])} / {bench_common.fmt_ms(c_stats['median'])}" if c_stats else "-"
            ratio = f"{s['median'] / c_stats['median']:.2f}" if c_stats else "-"
            note = args.note.replace("|", "/")
            if arm["label"] in self_compiles:
                note = f"{note}; self-compile {bench_common.fmt_ms(self_compiles[arm['label']]['ms'])} ms"
            append_log_row([today, head, "dirty" if dirty else "clean", str(args.n), str(args.runs), c_cell,
                            f"{bench_common.fmt_ms(s['min'])} / {bench_common.fmt_ms(s['median'])}", ratio, f"{arm['exe_bytes']:,}",
                            str(censuses[arm["label"]]["ops"]), arm["label"], note])
        print()
        print(f"  {len(maxon_arms)} row(s) appended to {os.path.relpath(LOG_PATH, bench_common.REPO)}")

    if args.json:
        payload = {"date": today, "head": head, "dirty": dirty, "n": args.n, "runs": args.runs,
                   "clang": clang_line, "clang_flags": CLANG_FLAGS, "stats": stats,
                   "arms": [{k: v for k, v in arm.items()} for arm in maxon_arms],
                   "census": censuses, "profiles": profiles, "self_compile": self_compiles,
                   "halted": halted, "note": args.note}
        with open(os.path.join(out_dir, "last-run.json"), "w", encoding="utf-8") as fh:
            json.dump(payload, fh, indent=2)
    if halted:
        print()
        print("HALT: the change slows the compiler's own self-compile beyond the band; stop and ask before landing it.")
    return bench_common.EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
