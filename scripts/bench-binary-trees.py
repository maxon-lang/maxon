#!/usr/bin/env python3
"""bench-binary-trees.py - time examples/binary-trees.maxon, the allocation-bound benchmark.

The sibling of scripts/bench-fannkuch.py for the Benchmarks Game's binary-trees: it builds the Maxon
example with one or more compilers, runs every binary interleaved, verifies each run's output and
exit code, and reports wall time per arm. There is no C reference arm: vendor/ holds no C
binary-trees, so the ratio column is between Maxon arms only, against the FIRST arm.

It is an INSTRUMENT: it exits 0 whatever the ratio is. Non-zero means the run itself is invalid -
a wrong answer, a leak (exit 101), a crash, or a build that failed.

Usage:
  python scripts/bench-binary-trees.py [--n 16] [--runs 5]
  python scripts/bench-binary-trees.py --ref <pre-change-sha>          # A/B: slot vs a control compiler
  python scripts/bench-binary-trees.py --compiler <path> --label x     # any compiler binary as an arm

Arms:
  --compiler PATH [--label L]   a compiler binary; default arm is the slot compiler, labelled "slot"
  --ref REF                     a compiler BUILT FROM a git ref, in a cached worktree under
                                <out-dir>/ref-<sha>/tree, seeded from .bootstrap/maxon.exe.

A label names an arm's output directory AND its timing list, so two arms may not share one, compared
case-insensitively - a duplicate is refused before anything is built, because two arms writing one
list report a ratio of 1.00 and call it verified. Every arm compiles the SAME source with its own compiler (and its own
checkout's stdlib), into its own directory, so two arms never share a .ir or a cache. The expected
output is computed from the depth, so any n the example accepts can be verified.

Exit codes: 0 ok; 1 a verification failure; 2 a build failure; 3 usage.
"""
import argparse
import datetime
import json
import os
import re
import sys

import bench_common

SOURCE_STEM = "binary-trees"

BuildFailure = bench_common.BuildFailure
VerificationFailure = bench_common.VerificationFailure
UsageFailure = bench_common.UsageFailure

DEFAULT_SOURCE = os.path.join(bench_common.REPO, "examples", SOURCE_STEM + ".maxon")
DEFAULT_OUT_DIR = os.path.join(bench_common.REPO, "temp", "bench-binary-trees")

# The names the example declares its own depth bounds under. They are read out of the source this run
# times rather than restated here, so a bound that moves there cannot leave this bench validating an n
# the program refuses - or computing an expected output the program does not print.
BOUND_SMALLEST_DEPTH = "smallestDepth"
BOUND_LARGEST_N = "largestN"
BOUND_DEFAULT_N = "defaultN"


def example_bounds(source):
    """The three depth bounds the example declares, by name."""
    with open(source, encoding="utf-8") as fh:
        text = fh.read()
    bounds = {}
    for name in (BOUND_SMALLEST_DEPTH, BOUND_LARGEST_N, BOUND_DEFAULT_N):
        found = re.search(rf"^let {name} = (\d+)$", text, re.MULTILINE)
        if found is None:
            raise UsageFailure(f"{source} declares no `let {name} = <n>`; this bench reads the example's own bounds")
        bounds[name] = int(found.group(1))
    return bounds


def tree_check(depth):
    """The node count of a complete binary tree, which is what the benchmark's check sums."""
    return (1 << (depth + 1)) - 1


def expected_output(n, smallest_depth):
    lines = [f"stretch tree of depth {n + 1}\t check: {tree_check(n + 1)}"]
    for depth in range(smallest_depth, n + 1, 2):
        iterations = 1 << (n - depth + smallest_depth)
        lines.append(f"{iterations}\t trees of depth {depth}\t check: {iterations * tree_check(depth)}")
    lines.append(f"long lived tree of depth {n}\t check: {tree_check(n)}")
    return "\n".join(lines) + "\n"


def verify(arm_name, n, result, expect):
    """Refuse a run whose output or exit code is wrong; a leak is the runtime's exit 101."""
    where = f"n={n}"
    bench_common.verify_exit(arm_name, where, result)
    bench_common.verify_output(arm_name, where, result, expect)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--n", type=int, default=None, help="problem size (default: the example's own)")
    bench_common.add_arm_arguments(ap)
    ap.add_argument("--out-dir", default=DEFAULT_OUT_DIR)
    ap.add_argument("--source", default=DEFAULT_SOURCE)
    ap.add_argument("--json", action="store_true", help="also write <out-dir>/last-run.json")
    args = ap.parse_args()

    # One block for everything that can refuse the run: resolving a ref, reading the example's bounds
    # and building an arm all reach git or the compiler, so each failure has one report and one code.
    try:
        bench_common.check_arm_arguments(args)
        bounds = example_bounds(args.source)
        smallest_depth = bounds[BOUND_SMALLEST_DEPTH]
        n = args.n if args.n is not None else bounds[BOUND_DEFAULT_N]
        if not smallest_depth <= n <= bounds[BOUND_LARGEST_N]:
            raise UsageFailure(f"n={n} is outside the example's {smallest_depth}..{bounds[BOUND_LARGEST_N]}")
        plan = bench_common.plan_arms(args)

        out_dir = os.path.abspath(args.out_dir)
        os.makedirs(out_dir, exist_ok=True)
        head, dirty = bench_common.tree_identity(bench_common.REPO)
        today = datetime.date.today().isoformat()
        maxon_arms = [bench_common.build_arm(arm["label"], bench_common.arm_compiler(arm, out_dir, args.ref_build_args.split()),
                                             out_dir, args.source, SOURCE_STEM)
                      for arm in plan]
    except (UsageFailure, BuildFailure) as e:
        return bench_common.report_failure(e)

    print(f"bench-binary-trees  {today}  head {head}{'*' if dirty else ''}  n={n}  runs={args.runs}  warmup={args.warmup}  (no C arm)")
    for arm in maxon_arms:
        print(f"  {arm['label']}: {bench_common.describe_identity(arm['identity'])}  exe {arm['exe_bytes']:,} B")

    # Each round runs every arm once, alternating direction so neither drift nor a fixed order can
    # manufacture a sign.
    expect = expected_output(n, smallest_depth)

    def measure_one(arm):
        ms, result = bench_common.timed_run(arm["exe"], n, arm["dir"])
        verify(arm["label"], n, result, expect)
        return ms

    try:
        times = bench_common.interleave(maxon_arms, lambda arm: arm["label"], args.warmup, args.runs, measure_one)
    except VerificationFailure as e:
        return bench_common.report_failure(e)

    stats = {name: bench_common.summarize(ms) for name, ms in times.items()}
    control = stats[maxon_arms[0]["label"]]

    bench_common.print_timing_table([(arm["label"], stats[arm["label"]], control) for arm in maxon_arms])

    if args.json:
        payload = {"date": today, "head": head, "dirty": dirty, "n": n, "runs": args.runs,
                   "stats": stats, "arms": maxon_arms}
        with open(os.path.join(out_dir, "last-run.json"), "w", encoding="utf-8") as fh:
            json.dump(payload, fh, indent=2)
    return bench_common.EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
