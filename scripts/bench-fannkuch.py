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
                                bench/fannkuch/out/ref-<sha>/tree, seeded from .bootstrap/maxon.exe.
                                A control arm for an A/B is `--ref <pre-change-sha>`.

Every arm compiles the SAME source with its own compiler (and its own checkout's stdlib), into its
own directory, so two arms never share a .ir or a cache.

Exit codes: 0 ok; 1 a verification failure (no log row is written); 2 a build failure; 3 usage.
"""
import argparse
import datetime
import hashlib
import importlib.util
import json
import os
import shutil
import statistics
import subprocess
import sys
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IS_WINDOWS = os.name == "nt"
EXE = ".exe" if IS_WINDOWS else ""
SLOT_COMPILER = os.path.join(REPO, "maxon-bin", ".maxon", "maxon" + EXE)
SEED_COMPILER = os.path.join(REPO, ".bootstrap", "maxon" + EXE)
DEFAULT_SOURCE = os.path.join(REPO, "examples", "fannkuch-redux.maxon")
DEFAULT_C_SOURCE = os.path.join(REPO, "bench", "fannkuch", "fannkuchredux.gcc-5.c")
DEFAULT_OUT_DIR = os.path.join(REPO, "bench", "fannkuch", "out")
LOG_PATH = os.path.join(REPO, "docs", "fannkuch-benchmark-log.md")
CENSUS_SCRIPT = os.path.join(REPO, "scripts", "emitted-code-count.py")
WINDOWS_CLANG = r"C:\Program Files\LLVM\bin\clang.exe"
# Single-threaded on purpose: without -fopenmp the pragma is a comment and the blocks run in sequence.
CLANG_FLAGS = ["-O3", "-march=native", "-Wall", "-Wno-unknown-pragmas"]
LEAK_EXIT_CODE = 101
BUILD_TIMEOUT_S = 1200
RUN_TIMEOUT_S = 1800

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


class BuildFailure(Exception):
    pass


class VerificationFailure(Exception):
    pass


def run(cmd, cwd=None, timeout=BUILD_TIMEOUT_S):
    return subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, timeout=timeout)


def git(root, *args):
    r = run(["git", "-C", root] + list(args))
    if r.returncode != 0:
        raise BuildFailure(f"git {' '.join(args)} in {root} failed:\n{r.stderr}")
    return r.stdout.strip()


def checkout_root_of(path):
    """The git checkout containing `path`, or None. A worktree's .git is a file, not a directory."""
    d = os.path.dirname(os.path.abspath(path))
    while True:
        if os.path.exists(os.path.join(d, ".git")):
            return d
        parent = os.path.dirname(d)
        if parent == d:
            return None
        d = parent


def tree_identity(root):
    head = git(root, "rev-parse", "--short", "HEAD")
    dirty = git(root, "status", "--porcelain") != ""
    return head, dirty


def load_census():
    spec = importlib.util.spec_from_file_location("emitted_code_count", CENSUS_SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.count


def sha256_of(path):
    with open(path, "rb") as fh:
        return hashlib.sha256(fh.read()).hexdigest()


def find_clang(override):
    if override:
        return override
    if IS_WINDOWS and os.path.exists(WINDOWS_CLANG):
        return WINDOWS_CLANG
    found = shutil.which("clang")
    if not found:
        raise BuildFailure("no clang found; pass --clang <path>")
    return found


def build_c_reference(clang, c_source, out_dir):
    """Build the C arm, cached on the source bytes, the clang version and the flags."""
    c_dir = os.path.join(out_dir, "c")
    os.makedirs(c_dir, exist_ok=True)
    exe = os.path.join(c_dir, "fannkuchredux" + EXE)
    stamp_path = os.path.join(c_dir, "fannkuchredux.stamp.json")
    version = run([clang, "--version"])
    if version.returncode != 0:
        raise BuildFailure(f"{clang} --version failed:\n{version.stderr}")
    clang_line = version.stdout.splitlines()[0].strip()
    stamp = {"source_sha256": sha256_of(c_source), "clang": clang_line, "flags": CLANG_FLAGS}
    if os.path.exists(exe) and os.path.exists(stamp_path):
        with open(stamp_path, encoding="utf-8") as fh:
            if json.load(fh) == stamp:
                return exe, clang_line, True
    r = run([clang] + CLANG_FLAGS + ["-o", exe, c_source], cwd=c_dir)
    if r.returncode != 0 or not os.path.exists(exe):
        raise BuildFailure(f"clang failed (exit {r.returncode}):\n{r.stdout}\n{r.stderr}")
    with open(stamp_path, "w", encoding="utf-8") as fh:
        json.dump(stamp, fh, indent=2)
    return exe, clang_line, False


def build_compiler_from_ref(ref, out_dir, build_args):
    """A compiler built from a git ref in its own worktree, seeded from .bootstrap. Cached by sha."""
    sha = git(REPO, "rev-parse", "--short", ref)
    tree = os.path.join(out_dir, f"ref-{sha}", "tree")
    compiler = os.path.join(tree, "maxon-bin", ".maxon", "maxon" + EXE)
    if os.path.exists(compiler):
        return sha, compiler
    if not os.path.exists(SEED_COMPILER):
        raise BuildFailure(f"no seed compiler at {SEED_COMPILER}; a ref build needs one")
    if os.path.exists(tree):
        # A tree without a compiler is a build that failed halfway; start it over.
        run(["git", "-C", REPO, "worktree", "remove", "--force", tree])
        shutil.rmtree(tree, ignore_errors=True)
    os.makedirs(os.path.dirname(tree), exist_ok=True)
    git(REPO, "worktree", "add", "--detach", tree, sha)
    seed = os.path.join(tree, ".bootstrap", "maxon" + EXE)
    os.makedirs(os.path.dirname(seed), exist_ok=True)
    shutil.copy2(SEED_COMPILER, seed)
    print(f"building a compiler from {ref} ({sha}) in {tree} ...", flush=True)
    r = run([seed] + build_args, cwd=tree)
    if r.returncode != 0 or not os.path.exists(compiler):
        raise BuildFailure(f"building {ref} failed (exit {r.returncode}):\n{r.stdout}\n{r.stderr}")
    return sha, compiler


def compiler_identity(compiler):
    root = checkout_root_of(compiler)
    identity = {"compiler": os.path.abspath(compiler),
                "bytes": os.path.getsize(compiler),
                "mtime": datetime.datetime.fromtimestamp(os.path.getmtime(compiler)).isoformat(timespec="seconds")}
    if root:
        head, dirty = tree_identity(root)
        identity["checkout"] = root
        identity["head"] = head
        identity["dirty"] = dirty
    return identity


def build_maxon_arm(label, compiler, source, out_dir):
    """Compile the example with `compiler` into out/<label>/ and return the exe, the .ir and sizes."""
    arm_dir = os.path.join(out_dir, label)
    os.makedirs(arm_dir, exist_ok=True)
    src = os.path.join(arm_dir, "fannkuch-redux.maxon")
    shutil.copyfile(source, src)
    r = run([compiler, "build", "fannkuch-redux.maxon", "--emit-ir"], cwd=arm_dir)
    exe = os.path.join(arm_dir, "fannkuch-redux" + EXE)
    ir = os.path.join(arm_dir, "fannkuch-redux.ir")
    if r.returncode != 0 or not os.path.exists(exe) or not os.path.exists(ir):
        raise BuildFailure(f"{compiler} build failed for arm {label} (exit {r.returncode}):\n{r.stdout}\n{r.stderr}")
    return {"label": label, "dir": arm_dir, "exe": exe, "ir": ir,
            "exe_bytes": os.path.getsize(exe), "identity": compiler_identity(compiler),
            "compiler": compiler}


def expected_output(n, checksum, flips):
    return f"{checksum}\nPfannkuchen({n}) = {flips}\n"


def verify(arm_name, n, result, expect, is_c):
    """Refuse a run whose output or exit code is wrong. `expect` is (checksum, flips)."""
    stdout = result.stdout.replace("\r\n", "\n")
    checksum, flips = expect
    if stdout != expected_output(n, checksum, flips):
        raise VerificationFailure(f"{arm_name}: wrong output at n={n}:\n{stdout!r}\nexpected {expected_output(n, checksum, flips)!r}\nstderr: {result.stderr[:2000]}")
    wanted_exit = 0 if is_c else flips
    if result.returncode == LEAK_EXIT_CODE and not is_c:
        raise VerificationFailure(f"{arm_name}: LEAK - exit {LEAK_EXIT_CODE} at n={n}")
    if result.returncode != wanted_exit:
        raise VerificationFailure(f"{arm_name}: exit {result.returncode} at n={n}, expected {wanted_exit}\nstderr: {result.stderr[:2000]}")


def parse_output(stdout):
    lines = stdout.replace("\r\n", "\n").split("\n")
    checksum = int(lines[0])
    flips = int(lines[1].rsplit("=", 1)[1])
    return checksum, flips


def timed_run(exe, n, cwd):
    start = time.perf_counter()
    result = run([exe, str(n)], cwd=cwd, timeout=RUN_TIMEOUT_S)
    return (time.perf_counter() - start) * 1000.0, result


def profile_arm(arm, n, hz):
    """`maxon profile run` with the arm's own compiler; the report is saved beside the binary."""
    r = run([arm["compiler"], "profile", "run", arm["exe"], str(n)], cwd=arm["dir"], timeout=RUN_TIMEOUT_S)
    path = os.path.join(arm["dir"], f"profile-n{n}.txt")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(r.stdout)
        fh.write(r.stderr)
    return path, r.stdout


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
    with open(LOG_PATH, "a", encoding="utf-8", newline="\n") as fh:
        fh.write("| " + " | ".join(row) + " |\n")


def fmt_ms(value):
    return f"{value:,.0f}"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--n", type=int, default=11)
    ap.add_argument("--runs", type=int, default=5, help="timed runs per arm (default 5)")
    ap.add_argument("--warmup", type=int, default=1, help="untimed runs per binary first (default 1)")
    ap.add_argument("--compiler", action="append", default=[], help="a compiler binary as an arm (repeatable)")
    ap.add_argument("--label", action="append", default=[], help="label for the matching --compiler")
    ap.add_argument("--ref", action="append", default=[], help="a git ref to build a compiler from, as an arm")
    ap.add_argument("--ref-build-args", default="build maxon-bin",
                    help="what the seed runs in a ref's worktree (refs before dc2404d2 need 'build')")
    ap.add_argument("--no-slot", action="store_true", help="do not add the slot compiler as an arm")
    ap.add_argument("--no-c", action="store_true", help="skip the C reference")
    ap.add_argument("--profile", action="store_true", help="profile each Maxon arm with `maxon profile run`")
    ap.add_argument("--profile-n", type=int, default=None, help="n for the profile run (default --n)")
    ap.add_argument("--profile-hz", type=int, default=1000)
    ap.add_argument("--note", default="", help="append a log row per Maxon arm with this note")
    ap.add_argument("--out-dir", default=DEFAULT_OUT_DIR)
    ap.add_argument("--source", default=DEFAULT_SOURCE)
    ap.add_argument("--c-source", default=DEFAULT_C_SOURCE)
    ap.add_argument("--clang", default=None)
    ap.add_argument("--json", action="store_true", help="also write <out-dir>/last-run.json")
    args = ap.parse_args()

    if args.label and len(args.label) != len(args.compiler):
        print("usage: --label must be given once per --compiler", file=sys.stderr)
        return 3
    if args.runs < 1:
        print("usage: --runs must be at least 1", file=sys.stderr)
        return 3
    if args.no_c and args.n not in EXPECTED:
        print(f"usage: n={args.n} has no expected output and --no-c removes the oracle", file=sys.stderr)
        return 3
    if args.runs < 3:
        print(f"warning: {args.runs} run(s) per arm cannot show the spread; use 3 or more for a row", file=sys.stderr)

    out_dir = os.path.abspath(args.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    census = load_census()
    head, dirty = tree_identity(REPO)
    today = datetime.date.today().isoformat()

    try:
        arms = []
        if not args.no_slot:
            if not os.path.exists(SLOT_COMPILER):
                raise BuildFailure(f"the slot compiler is missing at {SLOT_COMPILER}; build it or pass --no-slot")
            arms.append(("slot", SLOT_COMPILER))
        for i, compiler in enumerate(args.compiler):
            label = args.label[i] if args.label else f"compiler{i + 1}"
            arms.append((label, os.path.abspath(compiler)))
        for ref in args.ref:
            sha, compiler = build_compiler_from_ref(ref, out_dir, args.ref_build_args.split())
            arms.append((f"ref@{sha}", compiler))
        if not arms and args.no_c:
            print("usage: nothing to run", file=sys.stderr)
            return 3

        c_exe = clang_line = None
        if not args.no_c:
            clang = find_clang(args.clang)
            c_exe, clang_line, cached = build_c_reference(clang, args.c_source, out_dir)

        maxon_arms = [build_maxon_arm(label, compiler, args.source, out_dir) for label, compiler in arms]
    except BuildFailure as e:
        print(f"BUILD FAILURE: {e}", file=sys.stderr)
        return 2

    print(f"bench-fannkuch  {today}  head {head}{'*' if dirty else ''}  n={args.n}  runs={args.runs}  warmup={args.warmup}")
    if c_exe:
        print(f"  C: {clang_line}  {' '.join(CLANG_FLAGS)}  (no -fopenmp)")
    for arm in maxon_arms:
        ident = arm["identity"]
        tree = f"{ident['head']}{'*' if ident['dirty'] else ''}" if "head" in ident else "no checkout"
        print(f"  {arm['label']}: {ident['compiler']}  [{tree}, {ident['bytes']:,} B, {ident['mtime']}]  exe {arm['exe_bytes']:,} B")

    # Runnable arms: the C reference first, then every Maxon arm. Each round runs every arm once,
    # alternating direction so neither drift nor a fixed order can manufacture a sign.
    runnables = []
    if c_exe:
        runnables.append({"name": "C", "exe": c_exe, "dir": os.path.dirname(c_exe), "is_c": True})
    for arm in maxon_arms:
        runnables.append({"name": arm["label"], "exe": arm["exe"], "dir": arm["dir"], "is_c": False, "arm": arm})

    expect = EXPECTED.get(args.n)
    times = {r["name"]: [] for r in runnables}
    try:
        for r in runnables:
            for _ in range(args.warmup):
                _, result = timed_run(r["exe"], args.n, r["dir"])
                if expect is None:
                    if not r["is_c"]:
                        raise VerificationFailure(f"n={args.n} is not in the expected table and the C run has not produced its oracle yet")
                    expect = parse_output(result.stdout)
                verify(r["name"], args.n, result, expect, r["is_c"])
        for round_index in range(args.runs):
            order = runnables if round_index % 2 == 0 else list(reversed(runnables))
            for r in order:
                ms, result = timed_run(r["exe"], args.n, r["dir"])
                verify(r["name"], args.n, result, expect, r["is_c"])
                times[r["name"]].append(ms)
    except VerificationFailure as e:
        print(f"VERIFICATION FAILURE: {e}", file=sys.stderr)
        return 1
    except subprocess.TimeoutExpired as e:
        print(f"VERIFICATION FAILURE: {e}", file=sys.stderr)
        return 1

    stats = {}
    for name, ms in times.items():
        stats[name] = {"min": min(ms), "median": statistics.median(ms), "max": max(ms), "runs": ms}
    c_stats = stats.get("C")

    print()
    print(f"  {'arm':<16} {'min ms':>10} {'median ms':>10} {'max ms':>10} {'ratio(med)':>11} {'ratio(min)':>11}  verified")
    for r in runnables:
        s = stats[r["name"]]
        if c_stats and not r["is_c"]:
            ratio_med = f"{s['median'] / c_stats['median']:.2f}"
            ratio_min = f"{s['min'] / c_stats['min']:.2f}"
        else:
            ratio_med = ratio_min = "-"
        print(f"  {r['name']:<16} {fmt_ms(s['min']):>10} {fmt_ms(s['median']):>10} {fmt_ms(s['max']):>10} {ratio_med:>11} {ratio_min:>11}  yes")

    censuses = {}
    print()
    print("  emitted-code census (scripts/emitted-code-count.py) per Maxon arm:")
    print("  " + f"{'arm':<16}" + "".join(f"{c:>15}" for c in CENSUS_COLUMNS))
    for arm in maxon_arms:
        with open(arm["ir"], encoding="utf-8") as fh:
            c = census(fh.read())
        censuses[arm["label"]] = c
        print("  " + f"{arm['label']:<16}" + "".join(f"{c[k]:>15}" for k in CENSUS_COLUMNS))

    profiles = {}
    if args.profile:
        profile_n = args.profile_n if args.profile_n is not None else args.n
        for arm in maxon_arms:
            path, report = profile_arm(arm, profile_n, args.profile_hz)
            profiles[arm["label"]] = path
            print()
            print(f"  profile of {arm['label']} at n={profile_n} -> {path}")
            for line in hot_functions_section(report).splitlines():
                print("  " + line)

    if args.n == 12 and c_stats:
        for arm in maxon_arms:
            s = stats[arm["label"]]
            print()
            print(f"VERDICT n=12 [{arm['label']}]: Maxon median {fmt_ms(s['median'])} ms vs C median {fmt_ms(c_stats['median'])} ms - ratio {s['median'] / c_stats['median']:.2f}")

    if args.note:
        for arm in maxon_arms:
            s = stats[arm["label"]]
            c_cell = f"{fmt_ms(c_stats['min'])} / {fmt_ms(c_stats['median'])}" if c_stats else "-"
            ratio = f"{s['median'] / c_stats['median']:.2f}" if c_stats else "-"
            append_log_row([today, head, "dirty" if dirty else "clean", str(args.n), str(args.runs), c_cell,
                            f"{fmt_ms(s['min'])} / {fmt_ms(s['median'])}", ratio, f"{arm['exe_bytes']:,}",
                            str(censuses[arm["label"]]["ops"]), arm["label"], args.note.replace("|", "/")])
        print()
        print(f"  {len(maxon_arms)} row(s) appended to {os.path.relpath(LOG_PATH, REPO)}")

    if args.json:
        payload = {"date": today, "head": head, "dirty": dirty, "n": args.n, "runs": args.runs,
                   "clang": clang_line, "clang_flags": CLANG_FLAGS, "stats": stats,
                   "arms": [{k: v for k, v in arm.items()} for arm in maxon_arms],
                   "census": censuses, "profiles": profiles, "note": args.note}
        with open(os.path.join(out_dir, "last-run.json"), "w", encoding="utf-8") as fh:
            json.dump(payload, fh, indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
