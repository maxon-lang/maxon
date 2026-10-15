import datetime
import hashlib
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
# The seed predates the manifest's named targets, so it compiles `maxon-bin/` as a plain program and
# has to be told the output stem — the same command `.github/workflows/ci.yml` runs on a fresh checkout.
SEED_BUILD_ARGS = "build maxon-bin --output=maxon-bin/.maxon/maxon"
SLOT_LABEL = "slot"
LEAK_EXIT_CODE = 101
EXIT_OK = 0
EXIT_VERIFICATION = 1
EXIT_BUILD = 2
EXIT_USAGE = 3
SPREAD_PERCENT = 100.0
MILLIS = 1000.0
BUILD_TIMEOUT_S = 1200
RUN_TIMEOUT_S = 1800


class BuildFailure(Exception):
    pass


class VerificationFailure(Exception):
    pass


class UsageFailure(Exception):
    """An argument, or the example itself, cannot support the run that was asked for."""


def add_arm_arguments(ap):
    ap.add_argument("--runs", type=int, default=5, help="timed runs per arm (default 5)")
    ap.add_argument("--warmup", type=int, default=1, help="untimed runs per binary first (default 1)")
    ap.add_argument("--compiler", action="append", default=[], help="a compiler binary as an arm (repeatable)")
    ap.add_argument("--label", action="append", default=[], help="label for the matching --compiler")
    ap.add_argument("--ref", action="append", default=[], help="a git ref to build a compiler from, as an arm")
    ap.add_argument("--ref-build-args", default=SEED_BUILD_ARGS,
                    help="what the seed runs in a ref's worktree (default: the CI seed build command)")
    ap.add_argument("--no-slot", action="store_true", help="do not add the slot compiler as an arm")


def check_arm_arguments(args):
    if args.label and len(args.label) != len(args.compiler):
        raise UsageFailure("--label must be given once per --compiler")
    if args.runs < 1:
        raise UsageFailure("--runs must be at least 1")
    if args.warmup < 0:
        raise UsageFailure("--warmup must not be negative")
    if args.runs < 3:
        print(f"warning: {args.runs} run(s) per arm cannot show the spread; use 3 or more for a row", file=sys.stderr)


def plan_arms(args, allow_no_arms=False, reserved_labels=()):
    """Every arm in run order with its final label - ref arms FIRST, so an A/B's control is the first one.

    A ref is resolved to its sha HERE and built from that sha later, so every label is known before a
    compiler is built and a duplicate costs nothing; it also keeps the label and the binary in
    agreement if the ref moves mid-run.
    """
    plan = []
    for ref in args.ref:
        sha = git(REPO, "rev-parse", "--short", ref)
        plan.append({"label": f"ref@{sha}", "sha": sha, "compiler": None})
    for i, compiler in enumerate(args.compiler):
        label = args.label[i] if args.label else f"compiler{i + 1}"
        plan.append({"label": label, "sha": None, "compiler": os.path.abspath(compiler)})
    if not args.no_slot:
        plan.append({"label": SLOT_LABEL, "sha": None, "compiler": SLOT_COMPILER})
    if not plan and not allow_no_arms:
        raise UsageFailure("nothing to run")

    reserved = {label.casefold(): label for label in reserved_labels}
    seen = {}
    for arm in plan:
        label = arm["label"]
        key = label.casefold()
        if key in reserved:
            raise UsageFailure(f"an arm may not be labelled {label!r}; {reserved[key]!r} names this bench's own reference arm")
        if key in seen:
            raise UsageFailure(f"two arms are labelled {seen[key]!r} and {label!r}; a label names an arm's output directory "
                               "(case-insensitively on Windows) and its timing list, so they would share both and report "
                               "a ratio of 1.00 as verified")
        seen[key] = label
    return plan


def arm_compiler(planned, out_dir, build_args):
    if planned["compiler"] is None:
        _, compiler = build_compiler_from_ref(planned["sha"], out_dir, build_args)
        return compiler
    if not os.path.exists(planned["compiler"]):
        raise BuildFailure(f"arm {planned['label']} has no compiler at {planned['compiler']}; build it or drop the arm")
    return planned["compiler"]


def describe_identity(identity):
    tree = f"{identity['head']}{'*' if identity['dirty'] else ''}" if "head" in identity else "no checkout"
    return f"{identity['compiler']}  [{tree}, {identity['bytes']:,} B, {identity['mtime']}]"


def verify_exit(arm_name, where, result, expected_exit=0, leak_checked=True):
    if leak_checked and result.returncode == LEAK_EXIT_CODE:
        raise VerificationFailure(f"{arm_name}: LEAK - exit {LEAK_EXIT_CODE} at {where}")
    if result.returncode != expected_exit:
        raise VerificationFailure(f"{arm_name}: exit {result.returncode} at {where}, expected {expected_exit}\n"
                                  f"stderr: {result.stderr[:2000]}")


def verify_output(arm_name, where, result, expected_stdout):
    stdout = result.stdout.replace("\r\n", "\n")
    if stdout != expected_stdout:
        raise VerificationFailure(f"{arm_name}: wrong output at {where}:\n{stdout!r}\nexpected {expected_stdout!r}\n"
                                  f"stderr: {result.stderr[:2000]}")


def round_order(runnables, round_index):
    return runnables if round_index % 2 == 0 else list(reversed(runnables))


def interleave(runnables, name_of, warmup, runs, measure_one):
    for runnable in runnables:
        for _ in range(warmup):
            measure_one(runnable)
    measured = {name_of(runnable): [] for runnable in runnables}
    for round_index in range(runs):
        for runnable in round_order(runnables, round_index):
            measured[name_of(runnable)].append(measure_one(runnable))
    return measured


def compile_in_arm(compiler, label, arm_dir, source, stem, build_args=(), exe_suffix=EXE):
    os.makedirs(arm_dir, exist_ok=True)
    staged_name = stem + ".maxon"
    shutil.copyfile(source, os.path.join(arm_dir, staged_name))
    r = run([compiler, "build", staged_name] + list(build_args), cwd=arm_dir)
    exe = os.path.join(arm_dir, stem + exe_suffix)
    if r.returncode != 0 or not os.path.exists(exe):
        raise BuildFailure(f"{compiler} build failed for {stem} in arm {label} (exit {r.returncode}):\n{r.stdout}\n{r.stderr}")
    return exe


def build_arm(label, compiler, out_dir, source, stem, build_args=()):
    arm_dir = os.path.join(out_dir, label)
    exe = compile_in_arm(compiler, label, arm_dir, source, stem, build_args=build_args)
    return {"label": label, "dir": arm_dir, "exe": exe, "exe_bytes": os.path.getsize(exe),
            "identity": compiler_identity(compiler), "compiler": compiler}


def print_timing_table(rows):
    print()
    print(f"  {'arm':<16} {'min ms':>10} {'median ms':>10} {'max ms':>10} {'ratio(med)':>11} {'ratio(min)':>11}  verified")
    for name, stats, control in rows:
        if control is None:
            ratio_med = ratio_min = "-"
        else:
            ratio_med = f"{stats['median'] / control['median']:.2f}"
            ratio_min = f"{stats['min'] / control['min']:.2f}"
        print(f"  {name:<16} {fmt_ms(stats['min']):>10} {fmt_ms(stats['median']):>10} {fmt_ms(stats['max']):>10} {ratio_med:>11} {ratio_min:>11}  yes")


def summarize(values):
    median = statistics.median(values)
    spread = (max(values) - min(values)) / median * SPREAD_PERCENT if median > 0 else 0.0
    return {"median": median, "min": min(values), "max": max(values), "spread_pct": spread, "runs": list(values)}


def append_table_row(path, row):
    with open(path, "a", encoding="utf-8", newline="\n") as fh:
        fh.write("| " + " | ".join(row) + " |\n")


def report_failure(error):
    if isinstance(error, UsageFailure):
        print(f"usage: {error}", file=sys.stderr)
        return EXIT_USAGE
    if isinstance(error, BuildFailure):
        print(f"BUILD FAILURE: {error}", file=sys.stderr)
        return EXIT_BUILD
    if isinstance(error, VerificationFailure):
        print(f"VERIFICATION FAILURE: {error}", file=sys.stderr)
        return EXIT_VERIFICATION
    raise error


def run(cmd, cwd=None, timeout=BUILD_TIMEOUT_S, env=None):
    return subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, timeout=timeout, env=env)


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


def sha256_of(path):
    with open(path, "rb") as fh:
        return hashlib.sha256(fh.read()).hexdigest()


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


def timed_run(exe, n, cwd):
    start = time.perf_counter()
    try:
        result = run([exe, str(n)], cwd=cwd, timeout=RUN_TIMEOUT_S)
    except subprocess.TimeoutExpired:
        raise VerificationFailure(f"{exe} {n} timed out after {RUN_TIMEOUT_S} s") from None
    return (time.perf_counter() - start) * MILLIS, result


def fmt_ms(value):
    return f"{value:,.0f}"
