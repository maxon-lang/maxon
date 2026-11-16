#!/usr/bin/env python3
"""bench-hash.py - time the stdlib's seeded hash and SHA-256 against Go, on this host.

The instrument of the hash work (bench/hash/README.md). It builds bench/hash/hash.maxon with one or
more Maxon compilers and bench/hash/go once, verifies that every arm prints the same iteration counts
and digests on every workload, runs the arms interleaved, and reports each workload's best time with
the Maxon/Go ratios. Each Maxon binary runs once per hash tier, forced with MAXON_HASH_TIER, so a
Maxon arm in the tables is a compiler and a tier: `slot-aes`, `slot-software`.

Maxon's tiers are compared with the Go arm that runs the same kind of hash: `slot-aes` with `go`,
the Go program as it runs, and `slot-software` with `go-software`, the same Go binary run with
GODEBUG=cpu.aes=off, which makes Go's runtime hash (maps, hash/maphash) use its portable code.

Every arm prints a `clock <resolutionNanos>` line before its rows: the smallest step its timing clock
makes. A row whose best time is under 1000 times its arm's resolution is refused, because the clock
cannot resolve it. A Maxon control built before the line existed prints none and is not gated.

It is an INSTRUMENT: it exits 0 whatever the ratio is. Non-zero means the run itself is invalid - a
digest that differs between arms, a row too short for its arm's clock, a leak (exit 101), a crash, a
tier this host lacks, or a build that failed.

Usage:
  python scripts/bench-hash.py [--runs 5] [--tiers aes,software] [--note "..."]
  python scripts/bench-hash.py --ref <pre-change-sha>            # A/B: slot vs a control compiler
  python scripts/bench-hash.py --check-go                        # only locate Go and print its version

Every Maxon arm compiles its own checkout's copy of bench/hash/hash.maxon together with
bench/common/BenchProgram.maxon; --ref builds a compiler from a git ref the way bench-fannkuch.py does.
Go is found by scripts/bench_common.locate_go.

Exit codes: 0 ok; 1 a verification failure (no log row is written); 2 a build failure; 3 usage.
"""
import argparse
import os
import platform
import sys

import bench_common

BENCH_DIR = os.path.join(bench_common.REPO, "bench", "hash")
SPEC = bench_common.BenchSpec("bench-hash", os.path.join(BENCH_DIR, "go"), os.path.join(BENCH_DIR, "hash.maxon"), "hash", bench_common.NANOS_PER_OP,
                             "in bench/hash/hash.maxon (baseIterationsOf) and bench/hash/go/main.go together")
DEFAULT_OUT_DIR = os.path.join(BENCH_DIR, "out")
LOG_RELATIVE_PATH = "docs/hash-benchmark-log.md"
LOG_PATH = os.path.join(bench_common.REPO, *LOG_RELATIVE_PATH.split("/"))

GO_VARIANT = "default"
GO_VARIANT_TAGS = {GO_VARIANT: []}
GODEBUG_VARIABLE = "GODEBUG"
GO_AES_KNOB_MACHINES = bench_common.X64_MACHINES + bench_common.ARM64_MACHINES
GO_ARM_NAME = "go"
GO_SOFTWARE_ARM_NAME = "go-software"
# Go's x86 and arm64 runtimes both list `aes` among the cpu options that GODEBUG=cpu.aes=off clears, and the
# runtime selects its AES hash only while the CPU reports AES.
GO_SOFTWARE_ENVIRONMENT = {GODEBUG_VARIABLE: "cpu.aes=off"}
GO_DEFAULT_ENVIRONMENT = {GODEBUG_VARIABLE: ""}

TIER_VARIABLE = "MAXON_HASH_TIER"
TIER_AES = "aes"
TIER_SOFTWARE = "software"
TIERS = (TIER_AES, TIER_SOFTWARE)
TIER_SEPARATOR = ","
ARM_TIER_SEPARATOR = "-"
GO_ARM_OF_TIER = {TIER_AES: (GO_ARM_NAME, GO_DEFAULT_ENVIRONMENT), TIER_SOFTWARE: (GO_SOFTWARE_ARM_NAME, GO_SOFTWARE_ENVIRONMENT)}

REPEATS = 5
DIVISOR = 1

LOG_COLUMNS = ("date", "head", "tree", "host", "runs", "arm", "tier", "workload", "Maxon", "Go arm", "Go", "vs Go", "note")

LOG_HEADER = bench_common.log_header(
    "Hash benchmark log",
    """Best-of time of the stdlib's seeded hash and SHA-256 (`bench/hash/hash.maxon`) against Go
(`hash/maphash`, `map`, `crypto/sha256`), per workload and hash tier, by `scripts/bench-hash.py`.
**Read the tables downwards**: each row is one measurement of one workload on one tier, dated, and
`vs Go` is the number the hash work drives to 1.0 or below.""",
    ["**Only rows from one host compare.** Each row names it: the OS, the machine, the CPU model and the Go version it ran. "
     "The hash tiers a host has depend on its CPU."],
    """Columns: `Maxon` and `Go` are the best time of `runs` interleaved process runs, each of which already
took the best of 5 repetitions: MB/s for a workload that hashes bytes, or nanoseconds per operation for
one that does not. `vs Go` is the Maxon time over the time of the Go arm named in `Go arm`, so below 1.0
Maxon is faster. `tier` is the value `MAXON_HASH_TIER` forced for the row's Maxon run, and each tier is
compared with the Go arm that runs the same kind of hash: `aes` with `go`, Go as it runs, and `software`
with `go-software`, the same Go binary run with `GODEBUG=cpu.aes=off`, so Go's runtime hash (maps,
`hash/maphash`) is its portable code. `MAXON_HASH_TIER` changes Maxon's seeded hash only, and `cpu.aes=off`
does not touch SHA-256, so the two Maxon cells of `sha256-1m` and its two Go cells each differ by noise
alone. Maxon's `int` hash is not seeded and has no tier, so the two Maxon cells of `set-int-*` differ by
noise too, but Go's `map[int]` hashes with the runtime's `memhash64`, which follows `cpu.aes`: its two Go
cells, and so the two ratios, differ. `arm` names the Maxon compiler (`slot`, `ref@<sha>`, or a
`--label`); `tree` is whether the harness's checkout was clean.""",
    LOG_COLUMNS)


def parse_tiers(text):
    tiers = [tier for tier in text.split(TIER_SEPARATOR) if tier]
    unknown = [tier for tier in tiers if tier not in TIERS]
    if not tiers or unknown or len(set(tiers)) != len(tiers):
        raise bench_common.UsageFailure(f"--tiers is a list of distinct tiers from {TIER_SEPARATOR.join(TIERS)}, not {text!r}")
    if TIER_SOFTWARE in tiers and platform.machine().lower() not in GO_AES_KNOB_MACHINES:
        raise bench_common.UsageFailure(f"the {TIER_SOFTWARE} tier is paired with {GO_SOFTWARE_ARM_NAME}, which needs Go's "
                                        f"{GO_SOFTWARE_ENVIRONMENT[GODEBUG_VARIABLE]} knob, known on {', '.join(GO_AES_KNOB_MACHINES)} and "
                                        f"not on this {platform.machine()} host")
    return tiers


def program_arguments():
    return [str(REPEATS), str(DIVISOR)]


def maxon_runnable(arm, tier):
    entry = bench_common.runnable(arm["label"] + ARM_TIER_SEPARATOR + tier, bench_common.KIND_MAXON, arm["dir"],
                                  [arm["exe"]] + program_arguments(), environment={TIER_VARIABLE: tier}, compiler=arm["label"])
    return dict(entry, arm=arm["label"], tier=tier)


def go_runnable(name, environment, exe):
    return bench_common.runnable(name, bench_common.KIND_GO, os.path.dirname(exe), [exe] + program_arguments(), environment=environment)


def log_rows(context, order, best, seen, maxon_runnables, note):
    rows = []
    for runnable in maxon_runnables:
        go_name = GO_ARM_OF_TIER[runnable["tier"]][0]
        for workload in order:
            if (workload, runnable["name"]) not in best:
                continue

            rows.append(bench_common.row_prefix(context) + [runnable["arm"], runnable["tier"], workload,
                                                            bench_common.throughput_cell(best, seen, workload, runnable["name"], SPEC.per_op_unit),
                                                            go_name,
                                                            bench_common.throughput_cell(best, seen, workload, go_name, SPEC.per_op_unit),
                                                            bench_common.ratio_cell(best, workload, runnable["name"], go_name),
                                                            bench_common.note_cell(note)])
    return rows


def run_bench(args):
    tiers = parse_tiers(args.tiers)
    go_arms = [GO_ARM_OF_TIER[tier] for tier in tiers]
    built = bench_common.build_programs(SPEC, args, reserved_labels=[GO_ARM_NAME, GO_SOFTWARE_ARM_NAME], variant_tags=GO_VARIANT_TAGS)
    context = bench_common.run_context(bench_common.host_description(), built, args)
    bench_common.print_run_header(SPEC, context, args, built, details=[f"tiers {TIER_SEPARATOR.join(tiers)}"])

    maxon_runnables = [maxon_runnable(arm, tier) for arm in built.maxon_arms for tier in tiers]
    runnables = maxon_runnables + [go_runnable(name, environment, built.go_executables[GO_VARIANT]) for name, environment in go_arms]
    pairs = [(runnable["name"], GO_ARM_OF_TIER[runnable["tier"]][0]) for runnable in maxon_runnables]
    table_names = [runnable["name"] for runnable in maxon_runnables] + [name for name, _ in go_arms]
    best, seen, order = bench_common.run_and_report(SPEC, args, runnables, table_names, bench_common.ratio_table_of_pairs(pairs))

    if args.note:
        rows = log_rows(context, order, best, seen, maxon_runnables, args.note)
        bench_common.append_log_rows(LOG_PATH, LOG_HEADER, rows)
        print()
        print(f"  {len(rows)} row(s) appended to {LOG_RELATIVE_PATH}")
    return bench_common.EXIT_OK


def parse_arguments():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    bench_common.add_arm_arguments(ap, warmup_default=0)
    ap.add_argument("--tiers", default=TIER_SEPARATOR.join(TIERS),
                    help=f"the hash tiers to force on each Maxon arm, comma separated (default {TIER_SEPARATOR.join(TIERS)})")
    bench_common.add_log_arguments(ap, LOG_RELATIVE_PATH, DEFAULT_OUT_DIR)
    return ap.parse_args()


def run_harness(args):
    if args.check_go:
        return bench_common.check_go()
    bench_common.check_arm_arguments(args)
    return run_bench(args)


def main():
    args = parse_arguments()
    return bench_common.guarded_main(lambda: run_harness(args))


if __name__ == "__main__":
    sys.exit(main())
