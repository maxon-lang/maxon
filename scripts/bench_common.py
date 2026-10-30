import datetime
import hashlib
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
SEED_FETCH_SCRIPT = "scripts/fetch-seed.sh"
SEED_BUILD_SCRIPT = "scripts/build-from-seed.sh"
SEED_TAG_FILE = "scripts/seed-tag"
SLOT_LABEL = "slot"
LEAK_EXIT_CODE = 101
EXIT_OK = 0
EXIT_VERIFICATION = 1
EXIT_BUILD = 2
EXIT_USAGE = 3
SPREAD_PERCENT = 100.0
MILLIS = 1000.0
BUILD_TIMEOUT_S = 1200
REF_BUILD_TIMEOUT_S = 3600
RUN_TIMEOUT_S = 1800
GIT_EXEC_PATH_DEPTH = 3
GO_PATH_VARIABLE = "MAXON_GO"
GO_WINDOWS_INSTALL = r"C:\Program Files\Go\bin\go.exe"
GO_HOME_INSTALL = os.path.join(".local", "go", "bin", "go")
GO_BUILD_ENV = {"GOTOOLCHAIN": "local", "GOFLAGS": ""}
GO_TARGET_SETTINGS = ("GOOS", "GOARCH", "GOAMD64", "GOARM64", "GOEXPERIMENT", "CGO_ENABLED")
STAMP_SUFFIX = ".stamp.json"
TREE_DIRTY = "dirty"
TREE_CLEAN = "clean"


class BuildFailure(Exception):
    pass


class VerificationFailure(Exception):
    pass


class UsageFailure(Exception):
    """An argument, or the example itself, cannot support the run that was asked for."""


def locate_go():
    configured = os.environ.get(GO_PATH_VARIABLE)
    if configured:
        if os.path.isfile(configured) and os.access(configured, os.X_OK):
            return configured
        raise BuildFailure(f"{GO_PATH_VARIABLE}={configured} is not an executable file; unset it to search PATH, "
                           "or point it at the go binary")
    found = shutil.which("go")
    if found:
        return found
    fallback = GO_WINDOWS_INSTALL if IS_WINDOWS else os.path.join(os.path.expanduser("~"), GO_HOME_INSTALL)
    if os.path.isfile(fallback):
        return fallback
    raise BuildFailure(f"no Go toolchain found: {GO_PATH_VARIABLE} is unset, `go` is not on PATH and {fallback} does not exist. "
                       f"Install Go (go.dev/dl) or set {GO_PATH_VARIABLE} to the go binary")


def go_build_environment(target_env=None):
    return dict(os.environ, **GO_BUILD_ENV, **(target_env or {}))


def go_query(go, env, *arguments):
    answer = run([go] + list(arguments), env=env)
    if answer.returncode != 0:
        raise BuildFailure(f"{go} {' '.join(arguments)} failed:\n{answer.stderr}")
    return answer.stdout.strip()


def go_target_settings(go, env):
    return json.loads(go_query(go, env, "env", "-json", *GO_TARGET_SETTINGS))


def file_digests(paths, root):
    return {os.path.relpath(path, root).replace(os.sep, "/"): sha256_of(path) for path in sorted(paths)}


def build_cached(command, cwd, exe, stamp, what, env=None):
    """Run `command` to write `exe` unless it was already built from a stamp equal to `stamp`; True when cached."""
    stamp_path = os.path.splitext(exe)[0] + STAMP_SUFFIX
    if os.path.exists(exe) and os.path.exists(stamp_path):
        with open(stamp_path, encoding="utf-8") as fh:
            if json.load(fh) == stamp:
                return True
    os.makedirs(os.path.dirname(exe), exist_ok=True)
    built = run(command, cwd=cwd, env=env)
    if built.returncode != 0 or not os.path.exists(exe):
        raise BuildFailure(f"{what} failed (exit {built.returncode}):\n{built.stdout}\n{built.stderr}")
    with open(stamp_path, "w", encoding="utf-8") as fh:
        json.dump(stamp, fh, indent=2)
    return False


def build_go_cached(go, env, flags, package, cwd, exe, stamp, what):
    build_cached([go, "build"] + list(flags) + ["-o", exe, package], cwd, exe,
                 dict(stamp, env=GO_BUILD_ENV, flags=list(flags), package=package), f"go build for {what}", env=env)
    return exe


def add_arm_arguments(ap, warmup_default=1):
    ap.add_argument("--runs", type=int, default=5, help="timed runs per arm (default 5)")
    ap.add_argument("--warmup", type=int, default=warmup_default, help=f"untimed runs per binary first (default {warmup_default})")
    ap.add_argument("--compiler", action="append", default=[], help="a compiler binary as an arm (repeatable)")
    ap.add_argument("--label", action="append", default=[], help="label for the matching --compiler")
    ap.add_argument("--ref", action="append", default=[],
                    help=f"a git ref to build a compiler from, as an arm: its worktree's own {SEED_BUILD_SCRIPT}, "
                         f"as CI builds it, from the seed its {SEED_TAG_FILE} pins (this tree's .bootstrap seed when "
                         f"the pins agree, else its own {SEED_FETCH_SCRIPT}, which needs gh)")
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


def arm_compiler(planned, out_dir):
    if planned["compiler"] is None:
        return build_compiler_from_ref(planned["sha"], out_dir)
    if not os.path.exists(planned["compiler"]):
        raise BuildFailure(f"arm {planned['label']} has no compiler at {planned['compiler']}; build it or drop the arm")
    return planned["compiler"]


def arm_source_root(planned, out_dir):
    return REPO if planned["sha"] is None else ref_tree_of(planned["sha"], out_dir)


def repo_relative(path):
    try:
        relative = os.path.relpath(os.path.abspath(path), REPO)
    except ValueError:
        return None
    return None if relative == os.pardir or relative.startswith(os.pardir + os.sep) else relative


def arm_source(planned, out_dir, source):
    relative = repo_relative(source)
    if relative is None:
        return os.path.abspath(source)
    root = arm_source_root(planned, out_dir)
    path = os.path.join(root, relative)
    if not os.path.isfile(path):
        raise BuildFailure(f"arm {planned['label']} has no {relative.replace(os.sep, '/')} in its checkout {root}; "
                           "a ref arm compiles its own commit's program, never this tree's")
    return path


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


def build_arm(planned, out_dir, source, stem, build_args=(), exe_suffix=EXE, arms_dir=None):
    label = planned["label"]
    compiler = arm_compiler(planned, out_dir)
    program = arm_source(planned, out_dir, source)
    arm_dir = os.path.join(arms_dir or out_dir, label)
    exe = compile_in_arm(compiler, label, arm_dir, program, stem, build_args=build_args, exe_suffix=exe_suffix)
    return {"label": label, "dir": arm_dir, "exe": exe, "exe_bytes": os.path.getsize(exe),
            "identity": compiler_identity(compiler), "compiler": compiler,
            "source": program, "source_sha256": sha256_of(program)}


def describe_sources(source_digests_by_arm):
    labels = list(source_digests_by_arm)
    names = list(source_digests_by_arm[labels[0]]) if labels else []
    differing = [name for name in names if len({source_digests_by_arm[label][name] for label in labels}) > 1]
    if not differing:
        return "every arm compiles the same program"
    return f"the arms compile DIFFERENT programs, each from its own checkout, for: {', '.join(differing)}"


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


def append_log_rows(path, header, rows):
    if not os.path.exists(path):
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(header)
    for row in rows:
        append_table_row(path, row)


def session_context():
    head, dirty = tree_identity(REPO)
    return {"today": datetime.date.today().isoformat(), "head": head, "dirty": dirty}


def tree_state(dirty):
    return TREE_DIRTY if dirty else TREE_CLEAN


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


def posix_shell():
    """Git for Windows' bash on Windows, where `bash` on PATH may be System32's WSL launcher."""
    if not IS_WINDOWS:
        shell = shutil.which("bash")
        if shell is None:
            raise BuildFailure(f"no bash on PATH; a ref build runs the ref's {SEED_BUILD_SCRIPT}")
        return shell
    git_root = os.path.normpath(git(REPO, "--exec-path"))
    for _ in range(GIT_EXEC_PATH_DEPTH):
        git_root = os.path.dirname(git_root)
    shell = os.path.join(git_root, "bin", "bash.exe")
    if not os.path.exists(shell):
        raise BuildFailure(f"no Git for Windows bash at {shell}; a ref build runs the ref's {SEED_BUILD_SCRIPT}")
    return shell


def seed_tag_of(root):
    path = os.path.join(root, SEED_TAG_FILE)
    if not os.path.exists(path):
        raise BuildFailure(f"{root} has no {SEED_TAG_FILE}; it names the seed release that builds the tree")
    with open(path, encoding="utf-8") as fh:
        tag = "".join(fh.read().split())
    if not tag:
        raise BuildFailure(f"{path} is empty")
    return tag


def seed_path_of(root):
    return os.path.join(root, ".bootstrap", "maxon" + EXE)


def place_ref_seed(shell, tree):
    ref_tag = seed_tag_of(tree)
    own_tag = seed_tag_of(REPO)
    own_seed = seed_path_of(REPO)
    if ref_tag == own_tag and os.path.exists(own_seed):
        ref_seed = seed_path_of(tree)
        os.makedirs(os.path.dirname(ref_seed), exist_ok=True)
        staging = ref_seed + ".partial"
        shutil.copy2(own_seed, staging)
        os.replace(staging, ref_seed)
        print(f"{ref_seed} is {ref_tag}, copied from {own_seed}", flush=True)
        return
    if shutil.which("gh") is None:
        held = f"{own_seed} holds {own_tag}" if os.path.exists(own_seed) else f"there is no seed at {own_seed}"
        raise BuildFailure(f"the ref pins {ref_tag} and {held}, so {SEED_FETCH_SCRIPT} must download it, "
                           f"and it needs gh, which is not on PATH")
    run_tree_script(shell, tree, SEED_FETCH_SCRIPT)


def run_tree_script(shell, tree, script):
    if not os.path.exists(os.path.join(tree, script)):
        raise BuildFailure(f"{tree} has no {script}; a ref is built the way CI builds it, by its own scripts")
    r = run([shell, script], cwd=tree, timeout=REF_BUILD_TIMEOUT_S)
    if r.returncode != 0:
        raise BuildFailure(f"{script} failed in {tree} (exit {r.returncode}):\n{r.stdout}\n{r.stderr}")


def ref_tree_of(sha, out_dir):
    return os.path.join(out_dir, f"ref-{sha}", "tree")


def build_compiler_from_ref(ref, out_dir):
    """A compiler built from a git ref in its own worktree by that ref's own seed scripts. Cached by sha."""
    sha = git(REPO, "rev-parse", "--short", ref)
    tree = ref_tree_of(sha, out_dir)
    compiler = os.path.join(tree, "maxon-bin", ".maxon", "maxon" + EXE)
    if os.path.exists(compiler):
        return compiler
    shell = posix_shell()
    if os.path.exists(tree):
        # A tree without a compiler is a build that failed halfway; start it over.
        run(["git", "-C", REPO, "worktree", "remove", "--force", tree])
        shutil.rmtree(tree, ignore_errors=True)
    os.makedirs(os.path.dirname(tree), exist_ok=True)
    git(REPO, "worktree", "add", "--detach", tree, sha)
    print(f"building a compiler from {ref} ({sha}) in {tree} ...", flush=True)
    place_ref_seed(shell, tree)
    run_tree_script(shell, tree, SEED_BUILD_SCRIPT)
    if not os.path.exists(compiler):
        raise BuildFailure(f"{SEED_BUILD_SCRIPT} in {tree} exited 0 and left no compiler at {compiler}")
    return compiler


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


def profile_arm(arm, program_arguments, hz, report_name):
    """`maxon profile run` with the arm's own compiler; the report is saved beside the binary."""
    cmd = [arm["compiler"], "profile", "run", f"--rate={hz}", arm["exe"]] + list(program_arguments)
    where = f"profile of {arm['label']} with arguments {list(program_arguments)}"
    try:
        r = run(cmd, cwd=arm["dir"], timeout=RUN_TIMEOUT_S)
    except subprocess.TimeoutExpired:
        raise VerificationFailure(f"{where} timed out after {RUN_TIMEOUT_S} s") from None
    if r.returncode != 0:
        raise VerificationFailure(f"{where} failed (exit {r.returncode}):\n{r.stdout[-3000:]}\n{r.stderr[-3000:]}")
    path = os.path.join(arm["dir"], report_name)
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


def timed_run(exe, n, cwd):
    start = time.perf_counter()
    try:
        result = run([exe, str(n)], cwd=cwd, timeout=RUN_TIMEOUT_S)
    except subprocess.TimeoutExpired:
        raise VerificationFailure(f"{exe} {n} timed out after {RUN_TIMEOUT_S} s") from None
    return (time.perf_counter() - start) * MILLIS, result


def fmt_ms(value):
    return f"{value:,.0f}"
