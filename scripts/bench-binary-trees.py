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

A label names an arm's output directory AND its timing list, so two arms may not share one - a
duplicate is refused before anything is built, because two arms writing one list report a ratio of
1.00 and call it verified. Every arm compiles the SAME source with its own compiler (and its own
checkout's stdlib), into its own directory, so two arms never share a .ir or a cache. The expected
output is computed from the depth, so any n the example accepts can be verified.

Exit codes: 0 ok; 1 a verification failure; 2 a build failure; 3 usage.
"""
import argparse
import datetime
import importlib.util
import json
import os
import re
import shutil
import statistics
import subprocess
import sys
import time

SOURCE_STEM = "binary-trees"
FANNKUCH_BENCH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bench-fannkuch.py")


def load_shared():
    """The build, git and identity helpers live in bench-fannkuch.py; its filename is not importable."""
    spec = importlib.util.spec_from_file_location("bench_fannkuch", FANNKUCH_BENCH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


shared = load_shared()
BuildFailure = shared.BuildFailure
VerificationFailure = shared.VerificationFailure

DEFAULT_SOURCE = os.path.join(shared.REPO, "examples", SOURCE_STEM + ".maxon")
DEFAULT_OUT_DIR = os.path.join(shared.REPO, "temp", "bench-binary-trees")
SLOT_LABEL = "slot"

# The names the example declares its own depth bounds under. They are read out of the source this run
# times rather than restated here, so a bound that moves there cannot leave this bench validating an n
# the program refuses - or computing an expected output the program does not print.
BOUND_SMALLEST_DEPTH = "smallestDepth"
BOUND_LARGEST_N = "largestN"
BOUND_DEFAULT_N = "defaultN"


class UsageFailure(Exception):
    """An argument, or the example itself, cannot support the run that was asked for."""


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


def plan_arms(args):
    """Every arm in run order with its final label - ref arms FIRST, so an A/B's control is the first one.

    A ref is resolved to its sha HERE and built from that sha below, so every label is known before a
    compiler is built and a duplicate costs nothing; it also keeps the label and the binary in
    agreement if the ref moves mid-run.
    """
    plan = []
    for ref in args.ref:
        sha = shared.git(shared.REPO, "rev-parse", "--short", ref)
        plan.append({"label": f"ref@{sha}", "sha": sha, "compiler": None})
    for i, compiler in enumerate(args.compiler):
        label = args.label[i] if args.label else f"compiler{i + 1}"
        plan.append({"label": label, "sha": None, "compiler": os.path.abspath(compiler)})
    if not args.no_slot:
        plan.append({"label": SLOT_LABEL, "sha": None, "compiler": shared.SLOT_COMPILER})
    if not plan:
        raise UsageFailure("nothing to run")

    labels = [arm["label"] for arm in plan]
    for label in labels:
        if labels.count(label) > 1:
            raise UsageFailure(f"two arms are labelled {label!r}; a label names an arm's output directory and its "
                               "timing list, so they would share both and report a ratio of 1.00 as verified")
    return plan


def build_maxon_arm(planned, source, out_dir, ref_build_args):
    """Compile the example with the arm's compiler into out/<label>/ and return the exe and its size."""
    label = planned["label"]
    compiler = planned["compiler"]
    if compiler is None:
        _, compiler = shared.build_compiler_from_ref(planned["sha"], out_dir, ref_build_args)
    elif not os.path.exists(compiler):
        raise BuildFailure(f"arm {label} has no compiler at {compiler}; build it or drop the arm")

    arm_dir = os.path.join(out_dir, label)
    os.makedirs(arm_dir, exist_ok=True)
    src = os.path.join(arm_dir, SOURCE_STEM + ".maxon")
    shutil.copyfile(source, src)
    r = shared.run([compiler, "build", SOURCE_STEM + ".maxon"], cwd=arm_dir)
    exe = os.path.join(arm_dir, SOURCE_STEM + shared.EXE)
    if r.returncode != 0 or not os.path.exists(exe):
        raise BuildFailure(f"{compiler} build failed for arm {label} (exit {r.returncode}):\n{r.stdout}\n{r.stderr}")
    return {"label": label, "dir": arm_dir, "exe": exe, "exe_bytes": os.path.getsize(exe),
            "identity": shared.compiler_identity(compiler), "compiler": compiler}


def verify(arm_name, n, result, expect):
    """Refuse a run whose output or exit code is wrong; a leak is the runtime's exit 101."""
    stdout = result.stdout.replace("\r\n", "\n")
    if stdout != expect:
        raise VerificationFailure(f"{arm_name}: wrong output at n={n}:\n{stdout!r}\nexpected {expect!r}\nstderr: {result.stderr[:2000]}")
    if result.returncode == shared.LEAK_EXIT_CODE:
        raise VerificationFailure(f"{arm_name}: LEAK - exit {shared.LEAK_EXIT_CODE} at n={n}")
    if result.returncode != 0:
        raise VerificationFailure(f"{arm_name}: exit {result.returncode} at n={n}, expected 0\nstderr: {result.stderr[:2000]}")


def timed_run(exe, n, cwd):
    start = time.perf_counter()
    result = shared.run([exe, str(n)], cwd=cwd, timeout=shared.RUN_TIMEOUT_S)
    return (time.perf_counter() - start) * 1000.0, result


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--n", type=int, default=None, help="problem size (default: the example's own)")
    ap.add_argument("--runs", type=int, default=5, help="timed runs per arm (default 5)")
    ap.add_argument("--warmup", type=int, default=1, help="untimed runs per binary first (default 1)")
    ap.add_argument("--compiler", action="append", default=[], help="a compiler binary as an arm (repeatable)")
    ap.add_argument("--label", action="append", default=[], help="label for the matching --compiler")
    ap.add_argument("--ref", action="append", default=[], help="a git ref to build a compiler from, as an arm")
    ap.add_argument("--ref-build-args", default=shared.SEED_BUILD_ARGS,
                    help="what the seed runs in a ref's worktree (default: the CI seed build command)")
    ap.add_argument("--no-slot", action="store_true", help="do not add the slot compiler as an arm")
    ap.add_argument("--out-dir", default=DEFAULT_OUT_DIR)
    ap.add_argument("--source", default=DEFAULT_SOURCE)
    ap.add_argument("--json", action="store_true", help="also write <out-dir>/last-run.json")
    args = ap.parse_args()

    # One block for everything that can refuse the run: resolving a ref, reading the example's bounds
    # and building an arm all reach git or the compiler, so each failure has one report and one code.
    try:
        if args.label and len(args.label) != len(args.compiler):
            raise UsageFailure("--label must be given once per --compiler")
        if args.runs < 1:
            raise UsageFailure("--runs must be at least 1")
        bounds = example_bounds(args.source)
        smallest_depth = bounds[BOUND_SMALLEST_DEPTH]
        n = args.n if args.n is not None else bounds[BOUND_DEFAULT_N]
        if not smallest_depth <= n <= bounds[BOUND_LARGEST_N]:
            raise UsageFailure(f"n={n} is outside the example's {smallest_depth}..{bounds[BOUND_LARGEST_N]}")
        plan = plan_arms(args)

        if args.runs < 3:
            print(f"warning: {args.runs} run(s) per arm cannot show the spread; use 3 or more", file=sys.stderr)

        out_dir = os.path.abspath(args.out_dir)
        os.makedirs(out_dir, exist_ok=True)
        head, dirty = shared.tree_identity(shared.REPO)
        today = datetime.date.today().isoformat()
        maxon_arms = [build_maxon_arm(arm, args.source, out_dir, args.ref_build_args.split()) for arm in plan]
    except UsageFailure as e:
        print(f"usage: {e}", file=sys.stderr)
        return 3
    except BuildFailure as e:
        print(f"BUILD FAILURE: {e}", file=sys.stderr)
        return 2

    print(f"bench-binary-trees  {today}  head {head}{'*' if dirty else ''}  n={n}  runs={args.runs}  warmup={args.warmup}  (no C arm)")
    for arm in maxon_arms:
        ident = arm["identity"]
        tree = f"{ident['head']}{'*' if ident['dirty'] else ''}" if "head" in ident else "no checkout"
        print(f"  {arm['label']}: {ident['compiler']}  [{tree}, {ident['bytes']:,} B, {ident['mtime']}]  exe {arm['exe_bytes']:,} B")

    # Each round runs every arm once, alternating direction so neither drift nor a fixed order can
    # manufacture a sign.
    expect = expected_output(n, smallest_depth)
    times = {arm["label"]: [] for arm in maxon_arms}
    try:
        for arm in maxon_arms:
            for _ in range(args.warmup):
                _, result = timed_run(arm["exe"], n, arm["dir"])
                verify(arm["label"], n, result, expect)
        for round_index in range(args.runs):
            order = maxon_arms if round_index % 2 == 0 else list(reversed(maxon_arms))
            for arm in order:
                ms, result = timed_run(arm["exe"], n, arm["dir"])
                verify(arm["label"], n, result, expect)
                times[arm["label"]].append(ms)
    except (VerificationFailure, subprocess.TimeoutExpired) as e:
        print(f"VERIFICATION FAILURE: {e}", file=sys.stderr)
        return 1

    stats = {}
    for name, ms in times.items():
        stats[name] = {"min": min(ms), "median": statistics.median(ms), "max": max(ms), "runs": ms}
    control = stats[maxon_arms[0]["label"]]

    print()
    print(f"  {'arm':<16} {'min ms':>10} {'median ms':>10} {'max ms':>10} {'ratio(med)':>11} {'ratio(min)':>11}  verified")
    for arm in maxon_arms:
        s = stats[arm["label"]]
        ratio_med = f"{s['median'] / control['median']:.2f}"
        ratio_min = f"{s['min'] / control['min']:.2f}"
        print(f"  {arm['label']:<16} {shared.fmt_ms(s['min']):>10} {shared.fmt_ms(s['median']):>10} {shared.fmt_ms(s['max']):>10} {ratio_med:>11} {ratio_min:>11}  yes")

    if args.json:
        payload = {"date": today, "head": head, "dirty": dirty, "n": n, "runs": args.runs,
                   "stats": stats, "arms": maxon_arms}
        with open(os.path.join(out_dir, "last-run.json"), "w", encoding="utf-8") as fh:
            json.dump(payload, fh, indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
