#!/usr/bin/env python3
"""bench-aes.py - time the stdlib AES and AES-GCM against Go, on one lane.

The instrument of the AES parity work (bench/aes/README.md). It builds bench/aes/aes.maxon with one
or more Maxon compilers and bench/aes/go for the same lane, verifies that every arm prints the same
iteration counts and output digests on every workload, runs the arms interleaved, and reports each
workload's best time as MB/s with every Maxon/Go ratio. With --note it appends rows to
docs/aes-benchmark-log.md.

It is an INSTRUMENT: it exits 0 whatever the ratio is. Non-zero means the run itself is invalid - a
digest that differs between arms, a leak (exit 101), a crash, or a build that failed.

Usage:
  python scripts/bench-aes.py [--lane x64|wasm|arm64] [--runs 5] [--profile] [--note "..."]
  python scripts/bench-aes.py --ref <pre-change-sha>             # A/B: slot vs a control compiler
  python scripts/bench-aes.py --check-go                         # only locate Go and print its version

Lanes (default: the host's own native lane):
  x64    arms: maxon, go-bitsliced, go-purego (-tags purego, std), go-aesni (std)
  arm64  the same arms with go-armv8 for the hardware one; on a host that is not arm64 the run is
         shipped to the shared Mac through scripts/mac-host.sh (the commit must be pushed)
  wasm   maxon built with --target=wasm32-wasi, go built with GOOS=wasip1 GOARCH=wasm, both run under
         vendor/wasmtime; arms: maxon, go-bitsliced, go-std (Go's wasm std IS its portable code)

Go is found by scripts/bench_common.locate_go (MAXON_GO, PATH, then the usual install directory).
Every Maxon arm compiles its own checkout's copy of bench/aes/aes.maxon; --ref builds a compiler from
a git ref the way bench-fannkuch.py does.

Exit codes: 0 ok; 1 a verification failure (no log row is written); 2 a build failure; 3 usage.
"""
import argparse
import dataclasses
import json
import os
import pathlib
import platform
import shlex
import subprocess
import sys
import tempfile
import time

import bench_common

BENCH_DIR = os.path.join(bench_common.REPO, "bench", "aes")
GO_DIR_NAME = "go"
GO_DIR = os.path.join(BENCH_DIR, GO_DIR_NAME)
DEFAULT_OUT_DIR = os.path.join(BENCH_DIR, "out")
MAXON_SOURCE = os.path.join(BENCH_DIR, "aes.maxon")
MAXON_STEM = "aes"
LOG_RELATIVE_PATH = "docs/aes-benchmark-log.md"
LOG_PATH = os.path.join(bench_common.REPO, *LOG_RELATIVE_PATH.split("/"))
MAC_HOST_SCRIPT = "scripts/mac-host.sh"

LANE_X64 = "x64"
LANE_ARM64 = "arm64"
LANE_WASM = "wasm"
LANES = (LANE_X64, LANE_ARM64, LANE_WASM)
X64_MACHINES = ("amd64", "x86_64")
ARM64_MACHINES = ("arm64", "aarch64")

ROLE_BITSLICED = "bitsliced"
ROLE_PORTABLE = "portable"
ROLE_HARDWARE = "hardware"
ROLES = (ROLE_BITSLICED, ROLE_PORTABLE, ROLE_HARDWARE)

GO_MODULE_FILE = "go.mod"
GO_SOURCE_SUFFIX = ".go"
GO_PACKAGE = "."
GO_VARIANT_DEFAULT = "default"
GO_VARIANT_PUREGO = "purego"
GO_VARIANT_TAGS = {GO_VARIANT_DEFAULT: [], GO_VARIANT_PUREGO: ["purego"]}
GO_ARM_STD = "std"
GO_ARM_BITSLICED = "bitsliced"
GO_WASM_ENV = {"GOOS": "wasip1", "GOARCH": "wasm"}
GO_WASM_SUFFIX = ".wasm"

MAXON_WASM_TARGET = "--target=wasm32-wasi"
MAXON_WASM_SUFFIX = ".wasm"
WASMTIME_DIR = "wasmtime"
WASMTIME_EXIT_CODE_FLAGS = ["-S", "cli-exit-with-code=y"]

# REPEATS and WASM_DIVISOR equal `defaultRepeats` and `wasmDivisor` in bench/aes/aes.maxon, which
# reads no arguments on wasm.
REPEATS = 5
NATIVE_DIVISOR = 1
WASM_DIVISOR = 10
PROFILE_REPEATS = 1
PROFILE_DIVISOR = 10
PROFILE_RATE_HZ = 1000
OUTPUT_FIELDS = 6
OUTPUT_LAYOUT = "workload bytesPerOp iterations repeats bestNanos digest"
KIND_MAXON = "maxon"
KIND_GO = "go"
BYTES_PER_MB_PER_NS = 1e3
NANOS_PER_MICRO = 1e3
NO_CELL = "-"

MAC_SESSION_PREFIX = "bench-aes-"
MAC_LOCK_WAIT_SECONDS = 1800
MAC_POLL_SECONDS = 30
MAC_SKIPPED_MARKER = "mac-host.sh: skipped"
MAC_JOB_PREFIX = "JOB "
MAC_JOB_FINISHED = "finished (exit "
MAC_JOB_ALIVE = ": alive (pid "
MAC_ROWS_FILE = "bench/aes/out/arm64-rows.json"
ROWS_STAGING_SUFFIX = ".partial"

LOG_COLUMNS = ("date", "head", "tree", "lane", "host", "runs", "arm", "workload", "Maxon", "Go bitsliced", "Go portable",
               "Go hardware", "vs bitsliced", "vs portable", "vs hardware", "note")
COLUMN_ARM = LOG_COLUMNS.index("arm")
COLUMN_WORKLOAD = LOG_COLUMNS.index("workload")

LOG_PREAMBLE = """# AES benchmark log

Best-of time of the stdlib AES and AES-GCM (`bench/aes/aes.maxon`) against Go, per workload and lane,
by `scripts/bench-aes.py`. **Read the tables downwards**: each row is one measurement of one workload,
dated, and the ratio columns are the numbers the AES parity work drives to 1.0 or below.

- **Rows are appended by the tool, never by hand.** A run without `--note` records nothing; a run with
  one records unconditionally. The note says WHY the number moved - the instrument cannot.
- **This is an instrument, not a gate.** There is nothing to pass; a row that did not move is a
  datapoint, and a row that moved the wrong way is a reading to explain.
- **Rows rot.** Re-measure before planning from one; an A/B is two arms in ONE session, interleaved,
  never a before/after across a source change.
- **Only rows from one lane and host compare.** Each row names both.

Columns: `Maxon` and the Go columns are the best time of `runs` interleaved process runs, each of which
already took the best of 5 repetitions: MB/s, or microseconds per operation for key setup. `vs ...` is the
Maxon time over that Go arm's time, so below 1.0 Maxon is faster. The Go arms are `bitsliced` (a
function-for-function Go port of the stdlib algorithm, `bench/aes/go/bitsliced.go`), `portable` (`-tags purego`
on x64 and arm64, Go's own wasm build on wasm: the table AES with constant-time GHASH) and `hardware`
(Go's default build: AES-NI and PCLMULQDQ on x64, ARMv8 Crypto on arm64; absent on wasm). `arm` names the
Maxon compiler (`slot`, `ref@<sha>`, or a `--label`); `tree` is whether the harness's checkout was clean.

"""
LOG_TABLE_HEADER = "| " + " | ".join(LOG_COLUMNS) + " |\n|" + "---|" * len(LOG_COLUMNS) + "\n"
LOG_HEADER = LOG_PREAMBLE + LOG_TABLE_HEADER


@dataclasses.dataclass(frozen=True)
class GoArm:
    name: str
    role: str
    variant: str
    program_arm: str


@dataclasses.dataclass(frozen=True)
class Lane:
    name: str
    maxon_args: tuple
    maxon_suffix: str
    go_env: dict
    go_suffix: str
    divisor: int
    go_arms: tuple


def hardware_arm_name(lane):
    return {LANE_X64: "go-aesni", LANE_ARM64: "go-armv8"}[lane]


def native_go_arms(lane):
    return (GoArm("go-bitsliced", ROLE_BITSLICED, GO_VARIANT_DEFAULT, GO_ARM_BITSLICED),
            GoArm("go-purego", ROLE_PORTABLE, GO_VARIANT_PUREGO, GO_ARM_STD),
            GoArm(hardware_arm_name(lane), ROLE_HARDWARE, GO_VARIANT_DEFAULT, GO_ARM_STD))


LANE_TABLE = {
    LANE_X64: Lane(LANE_X64, (), bench_common.EXE, {}, bench_common.EXE, NATIVE_DIVISOR, native_go_arms(LANE_X64)),
    LANE_ARM64: Lane(LANE_ARM64, (), bench_common.EXE, {}, bench_common.EXE, NATIVE_DIVISOR, native_go_arms(LANE_ARM64)),
    LANE_WASM: Lane(LANE_WASM, (MAXON_WASM_TARGET,), MAXON_WASM_SUFFIX, GO_WASM_ENV, GO_WASM_SUFFIX, WASM_DIVISOR,
                    (GoArm("go-bitsliced", ROLE_BITSLICED, GO_VARIANT_DEFAULT, GO_ARM_BITSLICED),
                     GoArm("go-std", ROLE_PORTABLE, GO_VARIANT_DEFAULT, GO_ARM_STD))),
}


def host_lane():
    machine = platform.machine().lower()
    if machine in X64_MACHINES:
        return LANE_X64
    if machine in ARM64_MACHINES:
        return LANE_ARM64
    raise bench_common.UsageFailure(f"this host is {platform.machine()}, which is neither x64 nor arm64; only --lane wasm runs here")


def native_lane_here(lane):
    return lane != LANE_WASM and host_lane() == lane


def locate_wasmtime():
    path = os.path.join(bench_common.REPO, "vendor", WASMTIME_DIR, "wasmtime" + bench_common.EXE)
    if not os.path.isfile(path):
        raise bench_common.BuildFailure(f"no wasmtime at {path}; run scripts/fetch-vendor.sh")
    return path


def wasmtime_version(wasmtime):
    answer = bench_common.run([wasmtime, "--version"])
    if answer.returncode != 0:
        raise bench_common.BuildFailure(f"{wasmtime} --version failed:\n{answer.stderr}")
    return answer.stdout.strip()


def host_description(lane, wasmtime):
    description = f"{platform.system()} {platform.machine()}"
    return f"{description}, {wasmtime_version(wasmtime)}" if lane == LANE_WASM else description


def go_source_files():
    return [os.path.join(GO_DIR, name) for name in os.listdir(GO_DIR)
            if name == GO_MODULE_FILE or name.endswith(GO_SOURCE_SUFFIX)]


def build_go_arms(go, lane, out_dir):
    env = bench_common.go_build_environment(lane.go_env)
    version = bench_common.go_query(go, env, "version")
    stamp = {"sources": bench_common.file_digests(go_source_files(), GO_DIR), "version": version,
             "settings": bench_common.go_target_settings(go, env)}
    go_out = os.path.join(out_dir, lane.name, GO_DIR_NAME)
    executables = {}
    for variant in sorted({arm.variant for arm in lane.go_arms}):
        tags = GO_VARIANT_TAGS[variant]
        flags = ["-tags", ",".join(tags)] if tags else []
        executables[variant] = bench_common.build_go_cached(go, env, flags, GO_PACKAGE, GO_DIR,
                                                            os.path.join(go_out, variant + lane.go_suffix), stamp,
                                                            f"the {variant} variant on {lane.name}")
    return executables, version


def program_arguments(lane, kind, go_arm):
    settings = [str(REPEATS), str(lane.divisor)]
    if kind == KIND_GO:
        return [go_arm.program_arm] + settings
    return [] if lane.name == LANE_WASM else settings


def runnable_command(lane, wasmtime, kind, exe, go_arm=None):
    arguments = program_arguments(lane, kind, go_arm)
    if lane.name != LANE_WASM:
        return [exe] + arguments
    flags = WASMTIME_EXIT_CODE_FLAGS if kind == KIND_MAXON else []
    return [wasmtime, "run"] + flags + [exe] + arguments


def parse_output(name, stdout):
    results = {}
    for line in stdout.replace("\r\n", "\n").splitlines():
        fields = line.split()
        if len(fields) != OUTPUT_FIELDS:
            raise bench_common.VerificationFailure(f"{name} printed {line!r}, not '{OUTPUT_LAYOUT}'")
        workload, per_op, iterations, repeats, nanos, digest = fields
        if workload in results:
            raise bench_common.VerificationFailure(f"{name} printed {workload} twice")
        try:
            results[workload] = {"bytes": int(per_op), "iterations": int(iterations), "repeats": int(repeats),
                                 "nanos": int(nanos), "digest": digest}
        except ValueError:
            raise bench_common.VerificationFailure(f"{name} printed {line!r}, whose numeric fields are not integers") from None
    if not results:
        raise bench_common.VerificationFailure(f"{name} printed nothing")
    return results


def run_once(runnable):
    try:
        result = bench_common.run(runnable["command"], cwd=runnable["dir"], timeout=bench_common.RUN_TIMEOUT_S)
    except subprocess.TimeoutExpired:
        raise bench_common.VerificationFailure(f"{runnable['name']} timed out after {bench_common.RUN_TIMEOUT_S} s") from None
    bench_common.verify_exit(runnable["name"], "a full run", result, leak_checked=runnable["kind"] == KIND_MAXON)
    return parse_output(runnable["name"], result.stdout)


def collect(runnables, runs, warmup):
    measured = bench_common.interleave(runnables, lambda r: r["name"], warmup, runs, run_once)
    best = {}
    seen = {}
    order = []
    for name, rounds in measured.items():
        for results in rounds:
            for workload, cell in results.items():
                if workload not in order:
                    order.append(workload)
                key = (workload, name)
                best[key] = min(best.get(key, cell["nanos"]), cell["nanos"])
                shape = (cell["bytes"], cell["iterations"], cell["repeats"], cell["digest"])
                seen.setdefault(workload, {}).setdefault(shape, set()).add(name)
    check_agreement(seen, runnables, order)
    return best, seen, order


def check_agreement(seen, runnables, order):
    expected = {r["name"] for r in runnables}
    for workload in order:
        shapes = seen[workload]
        if len(shapes) != 1:
            detail = "; ".join(f"{sorted(names)} printed bytes/iterations/repeats/digest {shape}" for shape, names in sorted(shapes.items()))
            raise bench_common.VerificationFailure(f"the arms disagree on {workload}: {detail}")
        (_, names), = shapes.items()
        if names != expected:
            raise bench_common.VerificationFailure(f"{sorted(expected - names)} printed no {workload} line")


def shape_of(seen, workload):
    (bytes_per_op, iterations, _, _), = seen[workload]
    return bytes_per_op, iterations


def throughput_cell(best, seen, workload, name):
    nanos = best[(workload, name)]
    bytes_per_op, iterations = shape_of(seen, workload)
    if nanos <= 0:
        return NO_CELL
    per_op = nanos / iterations
    if bytes_per_op == 0:
        return f"{per_op / NANOS_PER_MICRO:.2f} us"
    return f"{bytes_per_op * BYTES_PER_MB_PER_NS / per_op:.1f} MB/s"


def ratio_cell(best, workload, numerator, denominator):
    over = best[(workload, denominator)]
    if over <= 0:
        return NO_CELL
    return f"{best[(workload, numerator)] / over:.2f}"


def print_tables(order, best, seen, maxon_names, go_arms):
    names = maxon_names + [arm.name for arm in go_arms]
    print()
    print(f"  {'workload':<18}" + "".join(f"{name:>16}" for name in names))
    for workload in order:
        print(f"  {workload:<18}" + "".join(f"{throughput_cell(best, seen, workload, name):>16}" for name in names))
    for maxon_name in maxon_names:
        print()
        print(f"  ratios, {maxon_name} time / Go time (below 1.00 Maxon is faster)")
        print(f"  {'workload':<18}" + "".join(f"{'vs ' + arm.name:>18}" for arm in go_arms))
        for workload in order:
            print(f"  {workload:<18}" + "".join(f"{ratio_cell(best, workload, maxon_name, arm.name):>18}" for arm in go_arms))


def log_rows(context, order, best, seen, maxon_names, go_arms, note):
    by_role = {arm.role: arm for arm in go_arms}
    rows = []
    for maxon_name in maxon_names:
        for workload in order:
            cells = [throughput_cell(best, seen, workload, maxon_name)]
            ratios = []
            for role in ROLES:
                arm = by_role.get(role)
                cells.append(throughput_cell(best, seen, workload, arm.name) if arm else NO_CELL)
                ratios.append(ratio_cell(best, workload, maxon_name, arm.name) if arm else NO_CELL)
            rows.append([context["today"], context["head"], bench_common.tree_state(context["dirty"]), context["lane"],
                         context["host"], str(context["runs"]), maxon_name, workload] + cells + ratios + [" ".join(note.replace("|", "/").split())])
    return rows


def run_lane(args, lane_name):
    lane = LANE_TABLE[lane_name]
    out_dir = os.path.abspath(args.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    go = bench_common.locate_go()
    wasmtime = locate_wasmtime() if lane_name == LANE_WASM else None
    plan = bench_common.plan_arms(args, reserved_labels=[arm.name for arm in lane.go_arms])

    go_executables, go_version = build_go_arms(go, lane, out_dir)
    maxon_arms = [bench_common.build_arm(planned, out_dir, MAXON_SOURCE, MAXON_STEM, build_args=lane.maxon_args,
                                         exe_suffix=lane.maxon_suffix, arms_dir=os.path.join(out_dir, lane.name))
                  for planned in plan]
    context = dict(bench_common.session_context(), lane=lane_name, host=host_description(lane_name, wasmtime), runs=args.runs)

    print(f"bench-aes  {context['today']}  head {context['head']}{'*' if context['dirty'] else ''}  lane {lane_name}  runs={args.runs}  warmup={args.warmup}")
    print(f"  host: {context['host']}")
    print(f"  {go_version}")
    for arm in maxon_arms:
        print(f"  {arm['label']}: {bench_common.describe_identity(arm['identity'])}  exe {arm['exe_bytes']:,} B")
    print(f"  program: {bench_common.describe_sources({arm['label']: {MAXON_STEM: arm['source_sha256']} for arm in maxon_arms})}")

    runnables = []
    for arm in maxon_arms:
        runnables.append({"name": arm["label"], "kind": KIND_MAXON, "dir": arm["dir"],
                          "command": runnable_command(lane, wasmtime, KIND_MAXON, arm["exe"])})
    for go_arm in lane.go_arms:
        exe = go_executables[go_arm.variant]
        runnables.append({"name": go_arm.name, "kind": KIND_GO, "dir": os.path.dirname(exe),
                          "command": runnable_command(lane, wasmtime, KIND_GO, exe, go_arm)})

    best, seen, order = collect(runnables, args.runs, args.warmup)
    maxon_names = [arm["label"] for arm in maxon_arms]
    print_tables(order, best, seen, maxon_names, lane.go_arms)

    if args.profile:
        profile_maxon_arms(maxon_arms)

    if args.note:
        rows = log_rows(context, order, best, seen, maxon_names, lane.go_arms, args.note)
        print()
        if args.rows_file:
            write_rows_file(args.rows_file, rows)
            print(f"  {len(rows)} row(s) written to {args.rows_file}")
        else:
            bench_common.append_log_rows(LOG_PATH, LOG_HEADER, rows)
            print(f"  {len(rows)} row(s) appended to {LOG_RELATIVE_PATH}")
    return bench_common.EXIT_OK


def write_rows_file(path, rows):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    staging = path + ROWS_STAGING_SUFFIX
    with open(staging, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(rows, fh)
    os.replace(staging, path)


def profile_maxon_arms(maxon_arms):
    for arm in maxon_arms:
        path, report = bench_common.profile_arm(arm, [str(PROFILE_REPEATS), str(PROFILE_DIVISOR)], PROFILE_RATE_HZ, "profile.txt")
        print()
        print(f"  profile of {arm['label']} (repeats {PROFILE_REPEATS}, divisor {PROFILE_DIVISOR}) -> {path}")
        for line in bench_common.hot_functions_section(report).splitlines():
            print("  " + line)


def mac_script(*arguments, timeout=bench_common.BUILD_TIMEOUT_S):
    answer = bench_common.run([bench_common.posix_shell(), MAC_HOST_SCRIPT] + list(arguments), cwd=bench_common.REPO,
                              timeout=timeout)
    if answer.returncode != 0:
        raise bench_common.BuildFailure(f"mac-host.sh {' '.join(arguments)} failed (exit {answer.returncode}):\n{answer.stdout}\n{answer.stderr}")
    if MAC_SKIPPED_MARKER in answer.stdout + answer.stderr:
        raise bench_common.UsageFailure("mac-host.sh skipped: no Mac is configured (set MAXON_MAC_HOST, see .env.example), so the arm64 lane cannot run")
    return answer.stdout


def marked_value(output, key):
    prefix = key + "="
    values = [line[len(prefix):].strip() for line in output.splitlines() if line.startswith(prefix)]
    if len(values) != 1 or not values[0]:
        raise bench_common.BuildFailure(f"mac-host.sh printed no single {prefix} line:\n{output}")
    return values[0]


def require_shippable_tree():
    head = bench_common.git(bench_common.REPO, "rev-parse", "HEAD")
    if not bench_common.git(bench_common.REPO, "branch", "-r", "--contains", head):
        raise bench_common.UsageFailure("the Mac clones a pushed commit and HEAD is on no remote branch; push it first")
    changed = bench_common.git(bench_common.REPO, "status", "--porcelain", "--", ".", f":!{LOG_RELATIVE_PATH}")
    if changed:
        raise bench_common.UsageFailure("the working tree has uncommitted changes, which the Mac would not measure; commit and push them:\n" + changed)
    return head


def mac_job_line(args, shas):
    forwarded = ["--lane", LANE_ARM64, "--runs", str(args.runs), "--warmup", str(args.warmup)]
    for sha in shas:
        forwarded += ["--ref", sha]
    if args.no_slot:
        forwarded.append("--no-slot")
    if args.note:
        forwarded += ["--note", args.note, "--rows-file", MAC_ROWS_FILE]
    quoted = " ".join(shlex.quote(part) for part in forwarded)
    return (f"python3 scripts/bench-aes.py --check-go && bash {bench_common.SEED_BUILD_SCRIPT} "
            f"&& python3 scripts/bench-aes.py {quoted}")


def await_mac_job(session):
    while True:
        report = mac_script("job", session)
        state = next((line for line in report.splitlines() if line.startswith(MAC_JOB_PREFIX)), "")
        print(f"  {state}", flush=True)
        if MAC_JOB_FINISHED in state:
            code = state.split(MAC_JOB_FINISHED, 1)[1].rstrip(")")
            if not code.isdigit():
                raise bench_common.BuildFailure(f"the Mac job finished with no readable exit code: {state!r}\n{report}")
            return report, int(code)
        if MAC_JOB_ALIVE not in state:
            raise bench_common.BuildFailure(f"the Mac job is neither alive nor finished: {state!r}\n{report}")
        time.sleep(MAC_POLL_SECONDS)


def check_rows(rows, expected_arms):
    if not isinstance(rows, list) or not rows:
        raise bench_common.VerificationFailure(f"the Mac's rows file {MAC_ROWS_FILE} holds no rows")
    for row in rows:
        if not isinstance(row, list) or len(row) != len(LOG_COLUMNS) or not all(isinstance(cell, str) for cell in row):
            raise bench_common.VerificationFailure(f"the Mac's rows file {MAC_ROWS_FILE} holds a row that is not {len(LOG_COLUMNS)} text cells: {row!r}")
    arms = {row[COLUMN_ARM] for row in rows}
    workloads = {row[COLUMN_WORKLOAD] for row in rows}
    pairs = {(row[COLUMN_ARM], row[COLUMN_WORKLOAD]) for row in rows}
    if len(arms) != expected_arms or len(pairs) != len(rows) or len(rows) != len(arms) * len(workloads):
        raise bench_common.VerificationFailure(
            f"the Mac's rows file {MAC_ROWS_FILE} holds {len(rows)} rows over {len(arms)} arm(s) and {len(workloads)} workload(s); "
            f"expected {expected_arms} arm(s), each with every workload exactly once")


def fetch_mac_rows(session, expected_arms):
    with tempfile.TemporaryDirectory(prefix=MAC_SESSION_PREFIX) as scratch:
        destination = pathlib.Path(scratch, pathlib.PurePosixPath(MAC_ROWS_FILE).name)
        mac_script("fetch", session, MAC_ROWS_FILE, destination.as_posix())
        try:
            with open(destination, encoding="utf-8") as fh:
                rows = json.load(fh)
        except (OSError, ValueError) as e:
            raise bench_common.VerificationFailure(f"the Mac's rows file {MAC_ROWS_FILE} is unreadable: {e}") from None
    check_rows(rows, expected_arms)
    return rows


def run_on_mac(args):
    if args.compiler:
        raise bench_common.UsageFailure("--compiler names a binary on this host; the Mac cannot run it. Use --ref")
    head = require_shippable_tree()
    plan = bench_common.plan_arms(args, allow_no_arms=True)
    shas = [arm["sha"] for arm in plan if arm["sha"]]
    session = MAC_SESSION_PREFIX + head[:12]
    key = marked_value(mac_script("clone", session, head, "--seed"), "session-key")
    rows = []
    try:
        token = marked_value(mac_script("lock", session, f"--wait={MAC_LOCK_WAIT_SECONDS}", "bench-aes timing run",
                                        timeout=MAC_LOCK_WAIT_SECONDS + bench_common.BUILD_TIMEOUT_S), "token")
        mac_script("run", session, token, "bash", "-c", mac_job_line(args, shas))
        report, exit_code = await_mac_job(session)
        if exit_code == bench_common.EXIT_OK and args.note:
            rows = fetch_mac_rows(session, len(plan))
    finally:
        mac_script("drop", session, key)
    print(report)
    if exit_code != bench_common.EXIT_OK:
        return exit_code
    if args.note:
        bench_common.append_log_rows(LOG_PATH, LOG_HEADER, rows)
        print(f"  {len(rows)} row(s) appended to {LOG_RELATIVE_PATH}")
    return bench_common.EXIT_OK


def parse_arguments():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    bench_common.add_arm_arguments(ap, warmup_default=0)
    ap.add_argument("--lane", choices=LANES, default=None, help="the lane to measure (default: this host's native lane)")
    ap.add_argument("--profile", action="store_true", help="profile each Maxon arm with `maxon profile run` (native lanes)")
    ap.add_argument("--note", default="", help="append log rows to docs/aes-benchmark-log.md with this note")
    ap.add_argument("--out-dir", default=DEFAULT_OUT_DIR)
    ap.add_argument("--check-go", action="store_true", help="only locate Go and print its version")
    ap.add_argument("--rows-file", default="", help=argparse.SUPPRESS)
    return ap.parse_args()


def check_go():
    go = bench_common.locate_go()
    print(f"{go}: {bench_common.go_query(go, bench_common.go_build_environment(), 'version')}")
    return bench_common.EXIT_OK


def main():
    args = parse_arguments()
    try:
        if args.check_go:
            return check_go()
        bench_common.check_arm_arguments(args)
        if args.rows_file and not args.note:
            raise bench_common.UsageFailure("--rows-file carries the rows --note records")
        lane = args.lane or host_lane()
        if args.profile and lane == LANE_WASM:
            raise bench_common.UsageFailure("--profile profiles a native binary; use --lane x64 or arm64")
        if args.profile and not native_lane_here(lane):
            raise bench_common.UsageFailure(f"--profile needs a host that runs the {lane} lane natively; "
                                            "start the run on the Mac with scripts/mac-host.sh run")
        if lane == LANE_ARM64 and not native_lane_here(lane):
            return run_on_mac(args)
        if lane == LANE_X64 and not native_lane_here(lane):
            raise bench_common.UsageFailure(f"this host is {platform.machine()}; the x64 lane runs on an x64 host")
        return run_lane(args, lane)
    except (bench_common.UsageFailure, bench_common.BuildFailure, bench_common.VerificationFailure) as e:
        return bench_common.report_failure(e)
    except subprocess.TimeoutExpired as e:
        return bench_common.report_failure(bench_common.BuildFailure(f"a step timed out: {e}"))


if __name__ == "__main__":
    sys.exit(main())
