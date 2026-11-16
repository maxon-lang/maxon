#!/usr/bin/env python3
"""bench-aes.py - time the stdlib AES and AES-GCM against Go, on one lane.

The instrument of the AES parity work (bench/aes/README.md). It builds bench/aes/aes.maxon with one
or more Maxon compilers and bench/aes/go for the same lane, verifies that every arm prints the same
iteration counts and output digests on every workload, runs the arms interleaved, and reports each
workload's best time as MB/s with every Maxon/Go ratio. With --note it appends rows to
docs/aes-benchmark-log.md.

Every arm prints a `clock <resolutionNanos>` line before its rows: the smallest step its timing clock
makes. A row whose best time is under 1000 times its arm's resolution is refused, because the clock
cannot resolve it. A Maxon control built before the line existed prints none and is not gated.

It is an INSTRUMENT: it exits 0 whatever the ratio is. Non-zero means the run itself is invalid - a
digest that differs between arms, a row too short for its arm's clock, a leak (exit 101), a crash, or
a build that failed.

Usage:
  python scripts/bench-aes.py [--lane x64|wasm|arm64] [--runs 5] [--profile] [--note "..."]
  python scripts/bench-aes.py --ref <pre-change-sha>             # A/B: slot vs a control compiler
  python scripts/bench-aes.py --check-go                         # only locate Go and print its version

Lanes (default: the host's own native lane):
  x64    arms: maxon, go-purego (-tags purego, std), go-aesni (std)
  arm64  the same arms with go-armv8 for the hardware one; on a host that is not arm64 the run is
         shipped to the shared Mac through scripts/mac-host.sh (the commit must be pushed)
  wasm   maxon built with --target=wasm32-wasi, go built with GOOS=wasip1 GOARCH=wasm, both run under
         vendor/wasmtime; arms: maxon, go-std (Go's wasm std IS its portable code)

Go is found by scripts/bench_common.locate_go (MAXON_GO, PATH, then the usual install directory).
Every Maxon arm compiles its own checkout's copy of bench/aes/aes.maxon together with
bench/common/BenchProgram.maxon; --ref builds a compiler from
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
import sys
import time

import bench_common

BENCH_DIR = os.path.join(bench_common.REPO, "bench", "aes")
SPEC = bench_common.BenchSpec("bench-aes", os.path.join(BENCH_DIR, "go"), os.path.join(BENCH_DIR, "aes.maxon"), "aes", bench_common.MICROS_PER_OP,
                             "in bench/aes/aes.maxon (baseIterationsOf) and bench/aes/go/main.go together")
DEFAULT_OUT_DIR = os.path.join(BENCH_DIR, "out")
LOG_RELATIVE_PATH = "docs/aes-benchmark-log.md"
LOG_PATH = os.path.join(bench_common.REPO, *LOG_RELATIVE_PATH.split("/"))
MAC_HOST_SCRIPT = "scripts/mac-host.sh"

LANE_X64 = "x64"
LANE_ARM64 = "arm64"
LANE_WASM = "wasm"
LANES = (LANE_X64, LANE_ARM64, LANE_WASM)

ROLE_PORTABLE = "portable"
ROLE_HARDWARE = "hardware"
ROLES = (ROLE_PORTABLE, ROLE_HARDWARE)

GO_VARIANT_DEFAULT = "default"
GO_VARIANT_PUREGO = "purego"
GO_VARIANT_TAGS = {GO_VARIANT_DEFAULT: [], GO_VARIANT_PUREGO: ["purego"]}
GO_WASM_ENV = {"GOOS": "wasip1", "GOARCH": "wasm"}
GO_WASM_SUFFIX = ".wasm"

MAXON_WASM_TARGET = "--target=wasm32-wasi"
MAXON_WASM_SUFFIX = ".wasm"
WASMTIME_DIR = "wasmtime"
WASMTIME_EXIT_CODE_FLAGS = ["-S", "cli-exit-with-code=y"]

# REPEATS and WASM_DIVISOR equal `defaultRepeats` and `wasmDivisor` in bench/common/BenchProgram.maxon, which
# reads no arguments on wasm.
REPEATS = 5
NATIVE_DIVISOR = 1
WASM_DIVISOR = 10
PROFILE_REPEATS = 1
PROFILE_DIVISOR = 10
PROFILE_RATE_HZ = 1000

MAC_SESSION_PREFIX = "bench-aes-"
MAC_LOCK_WAIT_SECONDS = 1800
MAC_POLL_SECONDS = 30
MAC_SKIPPED_MARKER = "mac-host.sh: skipped"
MAC_JOB_PREFIX = "JOB "
MAC_JOB_STATE_KEY = "state="
MAC_JOB_EXIT_KEY = "exit="
MAC_JOB_FINISHED = "finished"
MAC_JOB_ALIVE = "alive"
MAC_ROWS_FILE = "bench/aes/out/arm64-rows.json"
ROWS_STAGING_SUFFIX = ".partial"

LOG_COLUMNS = ("date", "head", "tree", "lane", "host", "runs", "arm", "workload", "Maxon", "Go portable", "Go hardware",
               "vs portable", "vs hardware", "note")
COLUMN_ARM = LOG_COLUMNS.index("arm")
COLUMN_WORKLOAD = LOG_COLUMNS.index("workload")

LOG_HEADER = bench_common.log_header(
    "AES benchmark log",
    """Best-of time of the stdlib AES and AES-GCM (`bench/aes/aes.maxon`) against Go, per workload and lane,
by `scripts/bench-aes.py`. **Read the tables downwards**: each row is one measurement of one workload,
dated, and the ratio columns are the numbers the AES parity work drives to 1.0 or below.""",
    ["**Only rows from one lane and host compare.** Each row names both: the host cell holds the OS, the machine, the CPU model "
     "and the Go version the row ran."],
    """Columns: `Maxon` and the Go columns are the best time of `runs` interleaved process runs, each of which
already took the best of 5 repetitions: MB/s, or microseconds per operation for key setup. `vs ...` is the
Maxon time over that Go arm's time, so below 1.0 Maxon is faster. The Go arms are `portable` (`-tags purego`
on x64 and arm64, Go's own wasm build on wasm: the table AES with constant-time GHASH) and `hardware`
(Go's default build: AES-NI and PCLMULQDQ on x64, ARMv8 Crypto on arm64; absent on wasm). `arm` names the
Maxon compiler (`slot`, `ref@<sha>`, or a `--label`); `tree` is whether the harness's checkout was clean.""",
    LOG_COLUMNS)


@dataclasses.dataclass(frozen=True)
class GoArm:
    name: str
    role: str
    variant: str


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
    return (GoArm("go-purego", ROLE_PORTABLE, GO_VARIANT_PUREGO),
            GoArm(hardware_arm_name(lane), ROLE_HARDWARE, GO_VARIANT_DEFAULT))


LANE_TABLE = {
    LANE_X64: Lane(LANE_X64, (), bench_common.EXE, {}, bench_common.EXE, NATIVE_DIVISOR, native_go_arms(LANE_X64)),
    LANE_ARM64: Lane(LANE_ARM64, (), bench_common.EXE, {}, bench_common.EXE, NATIVE_DIVISOR, native_go_arms(LANE_ARM64)),
    LANE_WASM: Lane(LANE_WASM, (MAXON_WASM_TARGET,), MAXON_WASM_SUFFIX, GO_WASM_ENV, GO_WASM_SUFFIX, WASM_DIVISOR,
                    (GoArm("go-std", ROLE_PORTABLE, GO_VARIANT_DEFAULT),)),
}


def host_lane():
    machine = platform.machine().lower()
    if machine in bench_common.X64_MACHINES:
        return LANE_X64
    if machine in bench_common.ARM64_MACHINES:
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
    description = bench_common.host_description()
    return f"{description}, {wasmtime_version(wasmtime)}" if lane == LANE_WASM else description


def program_arguments(lane, kind):
    if kind == bench_common.KIND_MAXON and lane.name == LANE_WASM:
        return []
    return [str(REPEATS), str(lane.divisor)]


def runnable_command(lane, wasmtime, kind, exe):
    arguments = program_arguments(lane, kind)
    if lane.name != LANE_WASM:
        return [exe] + arguments
    flags = WASMTIME_EXIT_CODE_FLAGS if kind == bench_common.KIND_MAXON else []
    return [wasmtime, "run"] + flags + [exe] + arguments


def log_rows(context, order, best, seen, maxon_names, go_arms, note):
    by_role = {arm.role: arm for arm in go_arms}
    rows = []
    for maxon_name in maxon_names:
        for workload in order:
            if (workload, maxon_name) not in best:
                continue

            cells = [bench_common.throughput_cell(best, seen, workload, maxon_name, SPEC.per_op_unit)]
            ratios = []
            for role in ROLES:
                arm = by_role.get(role)
                cells.append(bench_common.throughput_cell(best, seen, workload, arm.name, SPEC.per_op_unit) if arm else bench_common.NO_CELL)
                ratios.append(bench_common.ratio_cell(best, workload, maxon_name, arm.name) if arm else bench_common.NO_CELL)
            rows.append(bench_common.row_prefix(context, lane=context["lane"]) + [maxon_name, workload] + cells + ratios
                        + [bench_common.note_cell(note)])
    return rows


def run_lane(args, lane_name):
    lane = LANE_TABLE[lane_name]
    wasmtime = locate_wasmtime() if lane_name == LANE_WASM else None
    built = bench_common.build_programs(SPEC, args, reserved_labels=[arm.name for arm in lane.go_arms],
                                        variant_tags={arm.variant: GO_VARIANT_TAGS[arm.variant] for arm in lane.go_arms},
                                        subdir=lane.name, go_env=lane.go_env, go_suffix=lane.go_suffix, maxon_args=lane.maxon_args,
                                        maxon_suffix=lane.maxon_suffix, target_label=f" on {lane.name}")
    context = bench_common.run_context(host_description(lane_name, wasmtime), built, args, lane=lane_name)
    bench_common.print_run_header(SPEC, context, args, built, details=[f"lane {lane_name}"])

    maxon_names = [arm["label"] for arm in built.maxon_arms]
    go_names = [arm.name for arm in lane.go_arms]
    runnables = [bench_common.runnable(arm["label"], bench_common.KIND_MAXON, arm["dir"],
                                       runnable_command(lane, wasmtime, bench_common.KIND_MAXON, arm["exe"])) for arm in built.maxon_arms]
    for go_arm in lane.go_arms:
        exe = built.go_executables[go_arm.variant]
        runnables.append(bench_common.runnable(go_arm.name, bench_common.KIND_GO, os.path.dirname(exe),
                                               runnable_command(lane, wasmtime, bench_common.KIND_GO, exe)))
    best, seen, order = bench_common.run_and_report(SPEC, args, runnables, maxon_names + go_names,
                                                    bench_common.ratio_tables_per_maxon_arm(maxon_names, go_names))

    if args.profile:
        profile_maxon_arms(built.maxon_arms)

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


# The first match, not the only one: `mac-host.sh job` prints its log's tail after the header, and the
# job's own output may repeat a marker.
def first_marked_value(output, prefix):
    return next((line[len(prefix):].strip() for line in output.splitlines() if line.startswith(prefix)), "")


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
    forwarded = ["--lane", LANE_ARM64, "--runs", str(args.runs), "--warmup", str(args.warmup), "--keep-refs", str(args.keep_refs)]
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
        print(f"  {first_marked_value(report, MAC_JOB_PREFIX)}", flush=True)
        state = first_marked_value(report, MAC_JOB_STATE_KEY)
        if state == MAC_JOB_FINISHED:
            code = first_marked_value(report, MAC_JOB_EXIT_KEY)
            if not code.isdigit():
                raise bench_common.BuildFailure(f"the Mac job finished with no readable exit code: {code!r}\n{report}")
            return report, int(code)
        if state != MAC_JOB_ALIVE:
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
    if len(arms) != expected_arms or len(pairs) != len(rows):
        raise bench_common.VerificationFailure(
            f"the Mac's rows file {MAC_ROWS_FILE} holds {len(rows)} rows over {len(arms)} arm(s) and {len(workloads)} workload(s); "
            f"expected {expected_arms} arm(s), each workload at most once per arm")


def fetch_mac_rows(session, expected_arms):
    with bench_common.temp_directory("bench-aes-mac-rows") as scratch:
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
    bench_common.add_log_arguments(ap, LOG_RELATIVE_PATH, DEFAULT_OUT_DIR)
    ap.add_argument("--rows-file", default="", help=argparse.SUPPRESS)
    return ap.parse_args()


def run_harness(args):
    if args.check_go:
        return bench_common.check_go()
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


def main():
    args = parse_arguments()
    return bench_common.guarded_main(lambda: run_harness(args))


if __name__ == "__main__":
    sys.exit(main())
