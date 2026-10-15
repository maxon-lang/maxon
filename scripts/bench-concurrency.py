#!/usr/bin/env python3
import argparse
import dataclasses
import datetime
import json
import os
import platform
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import threading
import time

import bench_common

BuildFailure = bench_common.BuildFailure
VerificationFailure = bench_common.VerificationFailure
UsageFailure = bench_common.UsageFailure

IS_WINDOWS = bench_common.IS_WINDOWS

BENCH_DIR = os.path.join(bench_common.REPO, "bench", "concurrency")
DEFAULT_OUT_DIR = os.path.join(BENCH_DIR, "out")
LOG_PATH = os.path.join(bench_common.REPO, "docs", "concurrency-benchmark-log.md")

HOST_WINDOWS = "windows"
HOST_WSL = "wsl"
WSL_DISTRO = "Ubuntu"
WSL_TARGET = "x64-linux"
WSL_STAGE_DIR = os.path.join(".cache", "maxon-bench-concurrency")
CONTEXT_FILE = "context.json"

GNU_TIME = "/usr/bin/time"
WSL_LAUNCHER = "wsl.exe"
GO_ARM = "go"
GO_PATH_VARIABLE = "MAXON_GO"
GO_PROCESSORS_VARIABLE = "GOMAXPROCS"
MAXON_PROCESSORS_VARIABLE = "MAXON_MAX_PROCS"
GO_WINDOWS_INSTALL = r"C:\Program Files\Go\bin\go.exe"
GO_HOME_INSTALL = os.path.join(".local", "go", "bin", "go")
GO_BUILD_ENV = {"GOTOOLCHAIN": "local", "GOFLAGS": ""}
GO_BUILD_ARGS = ["build"]
GO_TARGET_SETTINGS = ("GOOS", "GOARCH", "GOAMD64", "GOEXPERIMENT", "CGO_ENABLED")
GO_SOURCE_DIRS = ("internal",)
GO_MODULE_FILE = "go.mod"
GO_MAIN_FILE = "main.go"
SINGLE_PROCESSOR = 1

BYTES_PER_MIB = 1024 * 1024
KIB = 1024
WINDOWS_FILETIME_TICKS_PER_MS = 10000.0


@dataclasses.dataclass(frozen=True)
class Workload:
    name: str
    arguments: tuple
    go_processors: int | None = None

    def go_processors_at(self, processors):
        return processors if self.go_processors is None else self.go_processors


WORKLOADS = (
    Workload("thread-ring", ("3000000",)),
    Workload("ping-pong", ("1000000",)),
    Workload("fan-in", ("100000",)),
    Workload("worker-pool", ("6000", "250000")),
    Workload("spawn-many", ("200000",)),
    # Maxon coroutines share one strand and never run in parallel, so Go is held to one processor.
    Workload("coroutine-churn", ("200000",), go_processors=SINGLE_PROCESSOR),
    Workload("shared-counter", ("200000",)),
)

METRICS = (
    ("wall", "WALL TIME (ms)", "wall_ms", 0),
    ("cpu", "CPU TIME, user+kernel (ms)", "cpu_ms", 0),
    ("mem", "PEAK WORKING SET (MiB)", "peak_mib", 1),
)

@dataclasses.dataclass
class Sample:
    wall_ms: float
    cpu_ms: float
    peak_mib: float
    returncode: int
    stdout: str
    stderr: str
    timed_out: bool = False


@dataclasses.dataclass
class Contender:
    name: str
    processors_variable: str
    executables: dict
    honours_pins: bool = False


if IS_WINDOWS:
    import ctypes
    from ctypes import wintypes

    class FileTime(ctypes.Structure):
        _fields_ = [("low", wintypes.DWORD), ("high", wintypes.DWORD)]

    class ProcessMemoryCounters(ctypes.Structure):
        _fields_ = [("cb", wintypes.DWORD), ("page_faults", wintypes.DWORD),
                    ("peak_working_set", ctypes.c_size_t), ("working_set", ctypes.c_size_t),
                    ("peak_paged_pool", ctypes.c_size_t), ("paged_pool", ctypes.c_size_t),
                    ("peak_non_paged_pool", ctypes.c_size_t), ("non_paged_pool", ctypes.c_size_t),
                    ("pagefile", ctypes.c_size_t), ("peak_pagefile", ctypes.c_size_t)]

    KERNEL32 = ctypes.WinDLL("kernel32", use_last_error=True)
    PSAPI = ctypes.WinDLL("psapi", use_last_error=True)
    KERNEL32.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(FileTime)] * 4
    PSAPI.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(ProcessMemoryCounters), wintypes.DWORD]


def filetime_ticks(value):
    return (value.high << 32) | value.low


def windows_cpu_ms(handle):
    created, exited, kernel, user = FileTime(), FileTime(), FileTime(), FileTime()
    ok = KERNEL32.GetProcessTimes(handle, ctypes.byref(created), ctypes.byref(exited), ctypes.byref(kernel), ctypes.byref(user))
    if not ok:
        raise BuildFailure(f"GetProcessTimes failed (error {ctypes.get_last_error()})")
    return (filetime_ticks(kernel) + filetime_ticks(user)) / WINDOWS_FILETIME_TICKS_PER_MS


def windows_peak_bytes(handle):
    counters = ProcessMemoryCounters()
    counters.cb = ctypes.sizeof(ProcessMemoryCounters)
    if not PSAPI.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb):
        raise BuildFailure(f"GetProcessMemoryInfo failed (error {ctypes.get_last_error()})")
    return counters.peak_working_set


def decode(raw):
    return raw.decode("utf-8", errors="replace").replace("\r\n", "\n")


def measure_windows(command, env, cwd):
    start = time.perf_counter()
    proc = subprocess.Popen(command, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    timed_out = False
    try:
        stdout, stderr = proc.communicate(timeout=bench_common.RUN_TIMEOUT_S)
    except subprocess.TimeoutExpired:
        proc.kill()
        stdout, stderr = proc.communicate()
        timed_out = True
    wall_ms = (time.perf_counter() - start) * bench_common.MILLIS
    handle = int(proc._handle)
    return Sample(wall_ms, windows_cpu_ms(handle), windows_peak_bytes(handle) / BYTES_PER_MIB,
                  proc.returncode, decode(stdout), decode(stderr), timed_out)


def read_peak_kib(path):
    with open(path, encoding="utf-8") as fh:
        lines = fh.read().split()
    if not lines or not lines[-1].isdigit():
        raise BuildFailure(f"{GNU_TIME} wrote no peak resident size to {path}")
    return int(lines[-1])


def require_instruments():
    if IS_WINDOWS:
        return
    if sys.platform != "linux":
        raise UsageFailure(f"peak memory is read from {GNU_TIME} on Linux and from the process counters on Windows; "
                           f"{sys.platform} has neither")
    if not os.access(GNU_TIME, os.X_OK):
        raise BuildFailure(f"{GNU_TIME} is missing; it reads each run's peak resident size. Install GNU time "
                           "(`sudo apt install time` on Ubuntu)")


def measure_posix(command, env, cwd):
    settled = threading.Lock()
    exited = threading.Event()
    killed = threading.Event()
    with tempfile.TemporaryFile() as out, tempfile.TemporaryFile() as err, tempfile.TemporaryDirectory() as scratch:
        peak_path = os.path.join(scratch, "peak")
        start = time.perf_counter()
        # The peak comes from GNU time: wait4's ru_maxrss would carry this Python parent's resident
        # size across the exec as a floor.
        proc = subprocess.Popen([GNU_TIME, "-f", "%M", "-o", peak_path] + command, cwd=cwd, env=env,
                                stdout=out, stderr=err, start_new_session=True)

        def expire():
            with settled:
                if exited.is_set():
                    return
                os.killpg(proc.pid, signal.SIGKILL)
                killed.set()

        timer = threading.Timer(bench_common.RUN_TIMEOUT_S, expire)
        timer.start()
        try:
            # WNOWAIT leaves the child unreaped until the timer is settled, so `expire` can never
            # signal a reused pid or process group.
            os.waitid(os.P_PID, proc.pid, os.WEXITED | os.WNOWAIT)
            wall_ms = (time.perf_counter() - start) * bench_common.MILLIS
        except BaseException:
            # The child has its own session, so Ctrl-C never reaches it.
            os.killpg(proc.pid, signal.SIGKILL)
            raise
        finally:
            with settled:
                exited.set()
            timer.cancel()
            _, status, usage = os.wait4(proc.pid, 0)
        timed_out = killed.is_set() and os.WIFSIGNALED(status) and os.WTERMSIG(status) == signal.SIGKILL
        proc.returncode = os.waitstatus_to_exitcode(status)
        out.seek(0)
        err.seek(0)
        stdout, stderr = out.read(), err.read()
        peak_kib = 0 if timed_out else read_peak_kib(peak_path)
    cpu_ms = (usage.ru_utime + usage.ru_stime) * bench_common.MILLIS
    return Sample(wall_ms, cpu_ms, peak_kib * KIB / BYTES_PER_MIB, proc.returncode,
                  decode(stdout), decode(stderr), timed_out)


def measure(command, env, cwd):
    return measure_windows(command, env, cwd) if IS_WINDOWS else measure_posix(command, env, cwd)


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


def go_source_files(workload):
    files = [os.path.join(BENCH_DIR, GO_MODULE_FILE), os.path.join(BENCH_DIR, workload.name, GO_MAIN_FILE)]
    for directory in GO_SOURCE_DIRS:
        for root, _, names in os.walk(os.path.join(BENCH_DIR, directory)):
            files.extend(os.path.join(root, name) for name in names if name.endswith(".go"))
    return sorted(files)


def go_source_digest(workload):
    return {os.path.relpath(path, BENCH_DIR).replace("\\", "/"): bench_common.sha256_of(path) for path in go_source_files(workload)}


def go_query(go, env, *arguments):
    answer = bench_common.run([go] + list(arguments), env=env)
    if answer.returncode != 0:
        raise BuildFailure(f"{go} {' '.join(arguments)} failed:\n{answer.stderr}")
    return answer.stdout.strip()


def build_go_programs(go, workloads, host_out_dir):
    env = dict(os.environ, **GO_BUILD_ENV)
    version_line = go_query(go, env, "version")
    settings = json.loads(go_query(go, env, "env", "-json", *GO_TARGET_SETTINGS))
    go_dir = os.path.join(host_out_dir, GO_ARM)
    os.makedirs(go_dir, exist_ok=True)
    executables = {}
    for workload in workloads:
        exe = os.path.join(go_dir, workload.name + bench_common.EXE)
        stamp_path = os.path.join(go_dir, workload.name + ".stamp.json")
        stamp = {"sources": go_source_digest(workload), "version": version_line, "settings": settings,
                 "args": GO_BUILD_ARGS, "env": GO_BUILD_ENV}
        cached = False
        if os.path.exists(exe) and os.path.exists(stamp_path):
            with open(stamp_path, encoding="utf-8") as fh:
                cached = json.load(fh) == stamp
        if not cached:
            built = bench_common.run([go] + GO_BUILD_ARGS + ["-o", exe, f"./{workload.name}"], cwd=BENCH_DIR, env=env)
            if built.returncode != 0 or not os.path.exists(exe):
                raise BuildFailure(f"go build failed for {workload.name} (exit {built.returncode}):\n{built.stdout}\n{built.stderr}")
            with open(stamp_path, "w", encoding="utf-8") as fh:
                json.dump(stamp, fh, indent=2)
        executables[workload.name] = exe
    return version_line, executables


def maxon_executable_suffix(host):
    return bench_common.EXE if host == HOST_WINDOWS else ""


def build_maxon_arm(planned, workloads, host_out_dir, ref_build_args, host, ref_out_dir):
    label = planned["label"]
    compiler = bench_common.arm_compiler(planned, ref_out_dir, ref_build_args)

    arm_dir = maxon_arm_dir(host_out_dir, label)
    target_args = [f"--target={WSL_TARGET}"] if host == HOST_WSL else []
    executables = {workload.name: bench_common.compile_in_arm(compiler, label, arm_dir, os.path.join(BENCH_DIR, f"{workload.name}.maxon"),
                                                        workload.name, build_args=target_args,
                                                        exe_suffix=maxon_executable_suffix(host))
                   for workload in workloads}
    return {"label": label, "identity": bench_common.compiler_identity(compiler),
            "exe_bytes": {name: os.path.getsize(path) for name, path in executables.items()}}


def maxon_arm_dir(host_out_dir, label):
    return os.path.join(host_out_dir, "maxon", label)


def arm_executables(arm, workloads, host, host_out_dir):
    suffix = maxon_executable_suffix(host)
    return {w.name: os.path.join(maxon_arm_dir(host_out_dir, arm["label"]), w.name + suffix) for w in workloads}


def cpu_count():
    return len(os.sched_getaffinity(0)) if hasattr(os, "sched_getaffinity") else os.cpu_count()


def processor_counts():
    return sorted({SINGLE_PROCESSOR, cpu_count()})


def stage_executable(path, stage_root, group):
    stage_dir = os.path.join(stage_root, group)
    os.makedirs(stage_dir, exist_ok=True)
    staged = os.path.join(stage_dir, os.path.basename(path))
    if not os.path.exists(staged) or bench_common.sha256_of(staged) != bench_common.sha256_of(path):
        shutil.copyfile(path, staged)
    os.chmod(staged, 0o755)
    return staged


def stage_all(executables, stage_root, group):
    return {name: stage_executable(path, stage_root, group) for name, path in executables.items()}


def run_environment(variable, processors):
    return dict(os.environ, **{variable: str(processors)})


def run_contender(contender, workload, processors):
    count = workload.go_processors_at(processors) if contender.honours_pins else processors
    return measure([contender.executables[workload.name]] + list(workload.arguments),
                   run_environment(contender.processors_variable, count), BENCH_DIR)


def run_location(workload, processors):
    return f"{workload.name} P={processors}"


def verify_completed(name, workload, processors, sample):
    where = run_location(workload, processors)
    if sample.timed_out:
        raise VerificationFailure(f"{name}: {where} timed out after {bench_common.RUN_TIMEOUT_S} s")
    bench_common.verify_exit(name, where, sample)


def verify(name, workload, processors, sample, expected):
    verify_completed(name, workload, processors, sample)
    bench_common.verify_output(name, run_location(workload, processors), sample, expected)


def derive_expected(go_contender, workload):
    sample = run_contender(go_contender, workload, SINGLE_PROCESSOR)
    verify_completed(go_contender.name, workload, SINGLE_PROCESSOR, sample)
    if re.fullmatch(rf"{re.escape(workload.name)} \d+\n", sample.stdout) is None:
        raise VerificationFailure(f"{go_contender.name}: {workload.name} printed {sample.stdout!r}, not '{workload.name} <value>'")
    return sample.stdout


def measure_cell(contenders, workload, processors, runs, warmup, expected):
    def measure_one(contender):
        sample = run_contender(contender, workload, processors)
        verify(contender.name, workload, processors, sample, expected)
        return sample

    return bench_common.interleave(contenders, lambda contender: contender.name, warmup, runs, measure_one)


def summarize_cell(samples):
    return {name: {key: bench_common.summarize([getattr(s, attribute) for s in runs]) for key, _, attribute, _ in METRICS}
            for name, runs in samples.items()}


def ratio_of(maxon_stats, go_stats):
    return maxon_stats["median"] / go_stats["median"] if go_stats["median"] > 0 else None


def fmt_ratio(value):
    return "-" if value is None else f"{value:.2f}"


def fmt_cell(stats, decimals):
    return f"{stats['median']:,.{decimals}f} +/-{stats['spread_pct']:.0f}%"


def pin_marker(cell):
    return "*" if cell["go_processors"] != cell["processors"] else ""


def print_metric_table(title, key, decimals, cells, maxon_labels):
    print()
    print(f"  {title}   median +/-spread (max-min over median); ratio = maxon median / go median")
    header = f"  {'workload':<16}{'P':>4}  {GO_ARM:>16}"
    for label in maxon_labels:
        header += f"  {label:>16} {'ratio':>7}"
    print(header)
    for cell in cells:
        line = f"  {cell['workload']:<16}{str(cell['processors']) + pin_marker(cell):>4}  {fmt_cell(cell['stats'][GO_ARM][key], decimals):>16}"
        for label in maxon_labels:
            maxon = cell["stats"][label][key]
            line += f"  {fmt_cell(maxon, decimals):>16} {fmt_ratio(ratio_of(maxon, cell['stats'][GO_ARM][key])):>7}"
        print(line)


def write_log_rows(cells, maxon_labels, context, args, host):
    note = args.note.replace("|", "/")
    for cell in cells:
        for label in maxon_labels:
            stats = cell["stats"]
            ratios = [fmt_ratio(ratio_of(stats[label][key], stats[GO_ARM][key])) for key, _, _, _ in METRICS]
            bench_common.append_table_row(LOG_PATH, [context["today"], context["head"], "dirty" if context["dirty"] else "clean", host, cell["workload"],
                            " ".join(cell["arguments"]), f"{cell['processors']}{pin_marker(cell)}",
                            str(args.runs), f"{stats[GO_ARM]['wall']['median']:,.0f}", f"{stats[label]['wall']['median']:,.0f}"] + ratios + [label, note])


def check_log_file():
    if not os.path.exists(LOG_PATH):
        raise UsageFailure(f"{os.path.relpath(LOG_PATH, bench_common.REPO)} is missing; --note appends to it and never creates it")


def describe_host(host):
    if host == HOST_WSL:
        return f"wsl ({WSL_DISTRO}) {platform.release()}, {cpu_count()} cpus, maxon built for {WSL_TARGET}"
    return f"{host} {platform.platform()}, {cpu_count()} cpus"


def print_session(context, host, go, go_version, workloads, arms, args):
    print(f"bench-concurrency  {context['today']}  head {context['head']}{'*' if context['dirty'] else ''}  host {describe_host(host)}  "
          f"runs={args.runs}  warmup={args.warmup}")
    print(f"  go: {go}  [{go_version}]")
    for arm in arms:
        print(f"  {arm['label']}: {bench_common.describe_identity(arm['identity'])}")
    print("  sizes: " + "; ".join(f"{w.name} {' '.join(w.arguments)}" for w in workloads))
    print(f"  P in {processor_counts()}: MAXON_MAX_PROCS=P for maxon, GOMAXPROCS=P for go; * marks a go arm held at GOMAXPROCS=1")


def select_workloads(filters):
    if not filters:
        return list(WORKLOADS)
    chosen = [w for w in WORKLOADS if any(f in w.name for f in filters)]
    unmatched = [f for f in filters if not any(f in w.name for w in WORKLOADS)]
    if unmatched:
        raise UsageFailure(f"--filter {unmatched} matches no workload; the workloads are {[w.name for w in WORKLOADS]}")
    return chosen


def run_matrix(contenders, workloads, args):
    maxon_contenders = [c for c in contenders if c.name != GO_ARM]
    go_contender = next(c for c in contenders if c.name == GO_ARM)
    cells = []
    for workload in workloads:
        expected = derive_expected(go_contender, workload)
        for processors in processor_counts():
            samples = measure_cell(contenders, workload, processors, args.runs, args.warmup, expected)
            cells.append({"workload": workload.name, "arguments": list(workload.arguments), "processors": processors,
                          "go_processors": workload.go_processors_at(processors), "stats": summarize_cell(samples)})
    return cells, [c.name for c in maxon_contenders]


def report(cells, maxon_labels):
    print()
    for key, title, _, decimals in METRICS:
        print_metric_table(title, key, decimals, cells, maxon_labels)


def make_contenders(go_executables, arms, workloads, host, host_out_dir, stage_root):
    go_map = stage_all(go_executables, stage_root, GO_ARM) if stage_root else go_executables
    contenders = [Contender(GO_ARM, GO_PROCESSORS_VARIABLE, go_map, honours_pins=True)]
    for arm in arms:
        maxon_map = arm_executables(arm, workloads, host, host_out_dir)
        if stage_root:
            maxon_map = stage_all(maxon_map, stage_root, arm["label"])
        contenders.append(Contender(arm["label"], MAXON_PROCESSORS_VARIABLE, maxon_map))
    return contenders


def host_out_dir_of(out_dir, host):
    return os.path.join(out_dir, host)


def run_session(args, workloads, host, context, arms, go):
    out_dir = os.path.abspath(args.out_dir)
    host_out_dir = host_out_dir_of(out_dir, host)
    os.makedirs(host_out_dir, exist_ok=True)
    go_version, go_executables = build_go_programs(go, workloads, host_out_dir)
    stage_root = os.path.join(os.path.expanduser("~"), WSL_STAGE_DIR) if host == HOST_WSL else None
    contenders = make_contenders(go_executables, arms, workloads, host, host_out_dir, stage_root)

    print_session(context, host, go, go_version, workloads, arms, args)
    cells, maxon_labels = run_matrix(contenders, workloads, args)
    report(cells, maxon_labels)

    if args.note:
        write_log_rows(cells, maxon_labels, context, args, host)
        print()
        print(f"  {len(cells) * len(maxon_labels)} row(s) appended to {os.path.relpath(LOG_PATH, bench_common.REPO)}")
    if args.json:
        payload = {"date": context["today"], "head": context["head"],
                   "dirty": context["dirty"], "host": host, "go": go, "go_version": go_version, "runs": args.runs,
                   "arms": arms, "cells": cells, "note": args.note}
        json_path = os.path.join(host_out_dir, "last-run.json")
        with open(json_path, "w", encoding="utf-8") as fh:
            json.dump(payload, fh, indent=2)
        print(f"  {os.path.relpath(json_path, bench_common.REPO)} written")


def read_context(host_out_dir):
    with open(os.path.join(host_out_dir, CONTEXT_FILE), encoding="utf-8") as fh:
        return json.load(fh)


def wsl_path(windows_path):
    translated = bench_common.run([WSL_LAUNCHER, "-d", WSL_DISTRO, "--", "wslpath", "-a", windows_path.replace("\\", "/")])
    if translated.returncode != 0:
        raise BuildFailure(f"wslpath failed for {windows_path}:\n{translated.stderr}")
    return translated.stdout.strip()


def forwarded_arguments(args):
    forwarded = ["--runs", str(args.runs), "--warmup", str(args.warmup)]
    for name in args.filter:
        forwarded += ["--filter", name]
    if args.json:
        forwarded.append("--json")
    if args.note:
        forwarded += ["--note", args.note]
    return forwarded


def run_outer_wsl(args, workloads, context, planned_arms):
    if shutil.which(WSL_LAUNCHER) is None:
        raise BuildFailure(f"--host wsl needs {WSL_LAUNCHER} on PATH and the {WSL_DISTRO} distribution installed")
    out_dir = os.path.abspath(args.out_dir)
    host_out_dir = host_out_dir_of(out_dir, HOST_WSL)
    os.makedirs(host_out_dir, exist_ok=True)
    arms = [build_maxon_arm(planned, workloads, host_out_dir, args.ref_build_args.split(), HOST_WSL, out_dir)
            for planned in planned_arms]
    with open(os.path.join(host_out_dir, CONTEXT_FILE), "w", encoding="utf-8") as fh:
        json.dump({"context": context, "arms": arms}, fh, indent=2)
    environment = [f"{GO_PATH_VARIABLE}={os.environ[GO_PATH_VARIABLE]}"] if GO_PATH_VARIABLE in os.environ else []
    command = [WSL_LAUNCHER, "-d", WSL_DISTRO, "--", "env"] + environment + [
        "python3", wsl_path(os.path.abspath(__file__)), "--inside-wsl", "--out-dir", wsl_path(out_dir)] + forwarded_arguments(args)
    return subprocess.run(command).returncode


def run_native(args, workloads, context, planned_arms):
    out_dir = os.path.abspath(args.out_dir)
    host = HOST_WINDOWS if IS_WINDOWS else sys.platform
    go = locate_go()
    host_out_dir = host_out_dir_of(out_dir, host)
    os.makedirs(host_out_dir, exist_ok=True)
    arms = [build_maxon_arm(planned, workloads, host_out_dir, args.ref_build_args.split(), host, out_dir)
            for planned in planned_arms]
    run_session(args, workloads, host, context, arms, go)
    return bench_common.EXIT_OK


def run_inside_wsl(args, workloads):
    stored = read_context(host_out_dir_of(os.path.abspath(args.out_dir), HOST_WSL))
    run_session(args, workloads, HOST_WSL, stored["context"], stored["arms"], locate_go())
    return bench_common.EXIT_OK


def build_context():
    head, dirty = bench_common.tree_identity(bench_common.REPO)
    return {"today": datetime.date.today().isoformat(), "head": head, "dirty": dirty}


def parse_arguments():
    ap = argparse.ArgumentParser(description="Maxon against Go on seven concurrency workloads: builds both, verifies every run's "
                                             "output, runs them interleaved at matched processor counts and reports wall, CPU "
                                             "and peak working set per arm with the Maxon/Go ratio. An instrument: it exits 0 "
                                             "whatever the ratio is.")
    bench_common.add_arm_arguments(ap)
    ap.add_argument("--filter", action="append", default=[], help="only workloads whose name contains this (repeatable)")
    ap.add_argument("--host", choices=("native", "wsl"), default="native",
                    help="wsl: build maxon for x64-linux, build go and run both inside WSL (Windows only)")
    ap.add_argument("--out-dir", default=DEFAULT_OUT_DIR)
    ap.add_argument("--json", action="store_true", help="also write <out-dir>/<host>/last-run.json")
    ap.add_argument("--note", default="", help="append one log row per workload and processor count with this note")
    ap.add_argument("--inside-wsl", action="store_true", help=argparse.SUPPRESS)
    return ap.parse_args()


def validate(args):
    if args.host == HOST_WSL and not IS_WINDOWS and not args.inside_wsl:
        raise UsageFailure("--host wsl is for a Windows host; run the script directly on Linux")
    if args.note:
        check_log_file()
    if not args.inside_wsl:
        bench_common.check_arm_arguments(args)


def main():
    args = parse_arguments()
    try:
        validate(args)
        require_instruments()
        workloads = select_workloads(args.filter)
        if args.inside_wsl:
            return run_inside_wsl(args, workloads)
        context = build_context()
        planned_arms = bench_common.plan_arms(args, reserved_labels=(GO_ARM,))
        if args.host == HOST_WSL:
            return run_outer_wsl(args, workloads, context, planned_arms)
        return run_native(args, workloads, context, planned_arms)
    except (UsageFailure, BuildFailure, VerificationFailure) as e:
        return bench_common.report_failure(e)
    except subprocess.TimeoutExpired as e:
        return bench_common.report_failure(BuildFailure(f"a build step timed out: {e}"))


if __name__ == "__main__":
    sys.exit(main())
