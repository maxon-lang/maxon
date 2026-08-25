#!/usr/bin/env python3
"""Sampling profiler for Maxon-compiled executables (Windows x64).

Launches the target process, samples every thread's RIP at a fixed rate, and
resolves samples against the `__symtable` function table the Maxon backends
embed in every binary (format: [u32 count][count x (u32 codeOffset,
u32 nameOffset)][NUL-terminated names], offsets relative to `.text`).

The two compilers put that table in different places, and this reads both:
the C# bootstrap writes it into a `.symtab` section; shv2 has no `.symtab`
and CLOSES `.text` with it (`X64Backend.maxon`, `buildSymbolTableBytes`).
So a binary shv2 emitted — the stage-2 compiler of a self-host chain, which
carries no `.mxdbg` and which `maxon profile` therefore refuses — profiles
here exactly like a bootstrap-emitted one. That is the reason this script
exists beside `maxon profile`: it is the only profiler that can see the
code shv2's own codegen produced.

Usage:
  python sample_profile.py --duration 40 --hz 200 [--top 40] -- <exe> [args...]

Exits with the target's exit code, or 124 if the duration cap killed it
(mirroring `timeout`).
"""

import argparse
import bisect
import ctypes
import ctypes.wintypes as wt
import struct
import subprocess
import sys
import time
from collections import Counter

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)

kernel32.OpenThread.restype = wt.HANDLE
kernel32.OpenThread.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.SuspendThread.restype = wt.DWORD
kernel32.SuspendThread.argtypes = [wt.HANDLE]
kernel32.ResumeThread.restype = wt.DWORD
kernel32.ResumeThread.argtypes = [wt.HANDLE]
kernel32.GetThreadContext.restype = wt.BOOL
kernel32.GetThreadContext.argtypes = [wt.HANDLE, ctypes.c_void_p]
kernel32.CloseHandle.argtypes = [wt.HANDLE]
kernel32.CreateToolhelp32Snapshot.restype = wt.HANDLE
kernel32.CreateToolhelp32Snapshot.argtypes = [wt.DWORD, wt.DWORD]
kernel32.Thread32First.argtypes = [wt.HANDLE, ctypes.c_void_p]
kernel32.Thread32Next.argtypes = [wt.HANDLE, ctypes.c_void_p]
psapi.EnumProcessModules.argtypes = [
    wt.HANDLE,
    ctypes.c_void_p,
    wt.DWORD,
    ctypes.c_void_p,
]
psapi.GetModuleInformation.argtypes = [
    wt.HANDLE,
    wt.HMODULE,
    ctypes.c_void_p,
    wt.DWORD,
]
kernel32.ReadProcessMemory.restype = wt.BOOL
kernel32.ReadProcessMemory.argtypes = [
    wt.HANDLE,
    ctypes.c_void_p,
    ctypes.c_void_p,
    ctypes.c_size_t,
    ctypes.c_void_p,
]

THREAD_SUSPEND_RESUME = 0x0002
THREAD_GET_CONTEXT = 0x0008
THREAD_QUERY_INFORMATION = 0x0040
TH32CS_SNAPTHREAD = 0x00000004
CONTEXT_AMD64 = 0x00100000
# CONTROL captures Rip/Rsp; INTEGER adds Rbp, needed for the caller walk.
CONTEXT_CONTROL_INTEGER = CONTEXT_AMD64 | 0x0003


class THREADENTRY32(ctypes.Structure):
    _fields_ = [
        ("dwSize", wt.DWORD),
        ("cntUsage", wt.DWORD),
        ("th32ThreadID", wt.DWORD),
        ("th32OwnerProcessID", wt.DWORD),
        ("tpBasePri", wt.LONG),
        ("tpDeltaPri", wt.LONG),
        ("dwFlags", wt.DWORD),
    ]


class MODULEINFO(ctypes.Structure):
    _fields_ = [
        ("lpBaseOfDll", ctypes.c_void_p),
        ("SizeOfImage", wt.DWORD),
        ("EntryPoint", ctypes.c_void_p),
    ]


def parse_pe_sections(path):
    """Return list of (name, rva, raw_offset, raw_size)."""
    with open(path, "rb") as f:
        data = f.read()
    e_lfanew = struct.unpack_from("<I", data, 0x3C)[0]
    num_sections = struct.unpack_from("<H", data, e_lfanew + 6)[0]
    opt_size = struct.unpack_from("<H", data, e_lfanew + 20)[0]
    sect_off = e_lfanew + 24 + opt_size
    sections = []
    for i in range(num_sections):
        off = sect_off + 40 * i
        name = data[off : off + 8].rstrip(b"\0").decode("ascii", "replace")
        virt_size, rva, raw_size, raw_ptr = struct.unpack_from("<IIII", data, off + 8)
        sections.append((name, rva, raw_ptr, raw_size))
    return data, sections


def load_symbols(path):
    """Return (text_rva, sorted list of code offsets, parallel list of names)."""
    data, sections = parse_pe_sections(path)
    text_rva = None
    text_raw = None
    sym_raw = None
    for name, rva, raw_ptr, raw_size in sections:
        if name == ".text":
            text_rva = rva
            text_raw = (raw_ptr, raw_size)
        elif name == ".symtab":
            sym_raw = (raw_ptr, raw_size)
    if text_rva is None:
        raise SystemExit("no .text section — not a Maxon binary?")
    # A bootstrap-emitted binary carries the table in `.symtab`; an shv2-emitted
    # one has no such section and the table is the tail of `.text` itself.
    raw_ptr, raw_size = sym_raw if sym_raw is not None else text_raw
    sect = data[raw_ptr : raw_ptr + raw_size]
    blob = find_symtable_blob(sect)
    count = struct.unpack_from("<I", blob, 0)[0]
    offsets, names = [], []
    for i in range(count):
        code_off, name_off = struct.unpack_from("<II", blob, 4 + 8 * i)
        end = blob.index(b"\0", name_off)
        offsets.append(code_off)
        names.append(blob[name_off:end].decode("utf-8", "replace"))
    return text_rva, offsets, names


# How many leading entries must ascend before a candidate header is believed.
# Two sufficed inside `.symtab`, whose other contents are text; a scan over
# `.text` sees machine code, where a 4-byte-aligned word that happens to read
# as a plausible count followed by one ascending pair is not rare.
SYMTABLE_PROBE_ENTRIES = 64


def find_symtable_blob(sect):
    """Locate the function table (`__symtable`) inside a section that holds
    other things too — `.symtab` concatenates it with every symdata blob
    (panic-message strings, ...), and shv2's `.text` precedes it with all the
    code. Its header is self-identifying: entry 0's nameOffset == 4 + 8*count,
    and codeOffsets ascend. Scan 4-byte-aligned starts for that signature."""
    for start in range(0, len(sect) - 12, 4):
        count = struct.unpack_from("<I", sect, start)[0]
        header = 4 + 8 * count
        if count < 100 or start + header >= len(sect):
            continue
        first_code, first_name = struct.unpack_from("<II", sect, start + 4)
        if first_name != header or first_code > 0x10000:
            continue
        prev_code = -1
        ascending = True
        for entry in range(min(count, SYMTABLE_PROBE_ENTRIES)):
            code_off, name_off = struct.unpack_from("<II", sect, start + 4 + 8 * entry)
            if code_off < prev_code or start + name_off >= len(sect):
                ascending = False
                break
            prev_code = code_off
        if not ascending:
            continue
        return sect[start:]
    raise SystemExit("no __symtable blob found in .symtab or at the tail of .text")


def thread_ids_of(pid):
    snap = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0)
    if snap == wt.HANDLE(-1).value:
        return []
    entry = THREADENTRY32()
    entry.dwSize = ctypes.sizeof(THREADENTRY32)
    tids = []
    if kernel32.Thread32First(snap, ctypes.byref(entry)):
        while True:
            if entry.th32OwnerProcessID == pid:
                tids.append(entry.th32ThreadID)
            if not kernel32.Thread32Next(snap, ctypes.byref(entry)):
                break
    kernel32.CloseHandle(snap)
    return tids


def module_base(proc_handle):
    module = wt.HMODULE()
    needed = wt.DWORD()
    if not psapi.EnumProcessModules(
        proc_handle, ctypes.byref(module), ctypes.sizeof(module), ctypes.byref(needed)
    ):
        return None
    info = MODULEINFO()
    if not psapi.GetModuleInformation(
        proc_handle, module, ctypes.byref(info), ctypes.sizeof(info)
    ):
        return None
    return info.lpBaseOfDll, info.SizeOfImage


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--duration", type=float, default=40.0)
    ap.add_argument("--hz", type=float, default=200.0)
    ap.add_argument("--top", type=int, default=40)
    ap.add_argument(
        "--start-after",
        type=float,
        default=0.0,
        help="seconds to let the target run before sampling begins — "
        "focuses the histogram on one compile phase",
    )
    ap.add_argument("cmd", nargs=argparse.REMAINDER)
    args = ap.parse_args()
    cmd = args.cmd
    if cmd and cmd[0] == "--":
        cmd = cmd[1:]
    if not cmd:
        ap.error("no target command given (use: ... -- exe args)")

    text_rva, sym_offsets, sym_names = load_symbols(cmd[0])

    proc = subprocess.Popen(cmd)
    try:
        run_sampler(proc, args, text_rva, sym_offsets, sym_names)
    finally:
        if proc.poll() is None:
            proc.kill()
            proc.wait()


def read_remote_u64(handle, addr):
    buf = ctypes.c_uint64()
    got = ctypes.c_size_t()
    ok = kernel32.ReadProcessMemory(
        handle, ctypes.c_void_p(addr), ctypes.byref(buf), 8, ctypes.byref(got)
    )
    if not ok or got.value != 8:
        return None
    return buf.value


def run_sampler(proc, args, text_rva, sym_offsets, sym_names):
    # Give the loader a moment, then resolve the image base once (it never moves).
    time.sleep(0.05)
    base_info = module_base(int(proc._handle))
    if base_info is None:
        raise SystemExit("could not resolve module base")
    base, image_size = base_info

    # A CONTEXT capture buffer. CONTEXT must be 16-byte aligned; over-allocate
    # and slide. Rip lives at offset 0xF8 in AMD64 CONTEXT.
    raw = ctypes.create_string_buffer(ctypes.sizeof(ctypes.c_byte) * (1232 + 16))
    addr = ctypes.addressof(raw)
    aligned = (addr + 15) & ~15
    ctx = (ctypes.c_byte * 1232).from_address(aligned)

    if args.start_after > 0:
        settle = time.monotonic() + args.start_after
        while proc.poll() is None and time.monotonic() < settle:
            time.sleep(0.05)

    hist = Counter()
    edge_hist = Counter()
    outside = 0
    total = 0
    interval = 1.0 / args.hz
    deadline = time.monotonic() + args.duration

    def resolve(rip):
        off = rip - base - text_rva
        if 0 <= rip - base < image_size and off >= 0:
            idx = bisect.bisect_right(sym_offsets, off) - 1
            if idx >= 0:
                return sym_names[idx]
        return None

    handles = {}

    def thread_handle(tid):
        if tid not in handles:
            handles[tid] = kernel32.OpenThread(
                THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION,
                False,
                tid,
            )
        return handles[tid]

    tids = thread_ids_of(proc.pid)
    last_tid_refresh = time.monotonic()

    while proc.poll() is None and time.monotonic() < deadline:
        now = time.monotonic()
        if now - last_tid_refresh > 1.0:
            tids = thread_ids_of(proc.pid)
            last_tid_refresh = now
        for tid in tids:
            h = thread_handle(tid)
            if not h:
                continue
            if kernel32.SuspendThread(h) == wt.DWORD(-1).value:
                continue
            try:
                ctypes.memset(aligned, 0, 1232)
                struct.pack_into("<I", ctx, 0x30, CONTEXT_CONTROL_INTEGER)  # ContextFlags
                if kernel32.GetThreadContext(h, aligned):
                    rip = struct.unpack_from("<Q", ctx, 0xF8)[0]
                    total += 1
                    leaf = resolve(rip)
                    if leaf is None:
                        outside += 1
                    else:
                        hist[leaf] += 1
                        # Walk the rbp chain for caller attribution. Maxon
                        # frames keep a standard [rbp]=prev / [rbp+8]=ret
                        # layout. Stop at the first unresolvable frame.
                        rbp = struct.unpack_from("<Q", ctx, 0xA0)[0]
                        chain = [leaf]
                        ph = int(proc._handle)
                        for _ in range(2):
                            if rbp < 0x10000:
                                break
                            ret = read_remote_u64(ph, rbp + 8)
                            prev = read_remote_u64(ph, rbp)
                            if ret is None or prev is None:
                                break
                            caller = resolve(ret)
                            if caller is None:
                                break
                            chain.append(caller)
                            rbp = prev
                        if len(chain) > 1:
                            edge_hist["  <-  ".join(chain)] += 1
            finally:
                kernel32.ResumeThread(h)
        time.sleep(interval)

    timed_out = proc.poll() is None
    if timed_out:
        proc.kill()
        proc.wait()

    for h in handles.values():
        if h:
            kernel32.CloseHandle(h)

    # Percentages are relative to samples that landed in our .text — samples
    # from parked helper threads (ntdll waits) and syscalls carry no signal
    # about where compiler CPU time goes.
    in_text = total - outside
    print(f"\n=== sample profile: {in_text} in-text samples ({total} total) ===")
    for name, n in hist.most_common(args.top):
        pct = 100.0 * n / in_text if in_text else 0.0
        print(f"{pct:6.2f}%  {n:6d}  {name}")

    print(f"\n=== hottest call edges (leaf  <-  caller) ===")
    for name, n in edge_hist.most_common(args.top):
        pct = 100.0 * n / in_text if in_text else 0.0
        print(f"{pct:6.2f}%  {n:6d}  {name}")

    sys.exit(124 if timed_out else proc.returncode)


if __name__ == "__main__":
    main()
