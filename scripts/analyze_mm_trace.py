#!/usr/bin/env python3
"""Analyze `--mm-trace` output from a Maxon-compiled binary.

Reads an mm-trace log from stdin (or a file path as argv[1]) in one streaming
pass and emits summary statistics relevant to the refcount-optimization
roadmap: per-tag op counts, per-scope hot spots, per-allocation lifecycles,
matched-pair candidates, traffic ratios.

Trace line shapes recognized (leading whitespace permitted — the runtime
emits `mm_free` and a realloc's `mm_raw_alloc`/`mm_raw_free` as nested
events indented under their parent `mm_decref rc=0` / `mm_realloc`. Only
`sl_*` / `os_alloc` lines are skipped):

    mm_alloc       <tag> #N size=K [scope]
    mm_incref      <tag> #N rc=K [scope]
    mm_decref      <tag> #N rc=K [scope]
    mm_transfer    <tag> #N rc=K [scope]
    mm_free        <tag> #N
    mm_raw_alloc   #RN size=K [scope]
    mm_raw_free    #RN
    mm_realloc     <tag> #N size=K

All output is human-readable plain text. Pass `--json` for machine-readable
JSON instead. Pass `--top=N` to change the number of entries shown in each
"top list" (default 20).
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from typing import TextIO


# ---- Trace line parsing ----

# Leading whitespace is tolerated: `mm_free` is emitted as a nested event
# indented under `mm_decref rc=0`, and `mm_raw_alloc`/`mm_raw_free` appear
# nested under `mm_realloc`. The `[scope]` suffix is optional — missing when
# the scope was unregistered (the trace prints neither `[` nor `]` on that
# branch, per EmitTraceScopeAndNewline) and empty (`[]`) when the scope was
# registered but resolves to an empty cstring. `sl_*` / `os_*` are allocator
# plumbing and are skipped via `RE_SUBEVENT` below.
RE_ALLOC = re.compile(
    r"^\s*mm_alloc\s+(?P<tag>\S+)\s+#(?P<id>\d+)\s+size=(?P<size>\d+)(?:\s+\[(?P<scope>.*)\])?\s*$"
)
RE_REF = re.compile(
    r"^\s*mm_(?P<op>incref|decref|transfer)\s+(?P<tag>\S+)\s+#(?P<id>\d+)\s+rc=(?P<rc>\d+)(?:\s+\[(?P<scope>.*)\])?\s*$"
)
RE_FREE = re.compile(r"^\s*mm_free\s+(?P<tag>\S+)\s+#(?P<id>\d+)\s*$")
RE_RAW_ALLOC = re.compile(
    r"^\s*mm_raw_alloc\s+#R(?P<id>\d+)\s+size=(?P<size>\d+)(?:\s+\[(?P<scope>.*)\])?\s*$"
)
RE_RAW_FREE = re.compile(
    r"^\s*mm_raw_free\s+#R(?P<id>\d+)(?:\s+\[(?P<scope>.*)\])?\s*$"
)
RE_REALLOC = re.compile(
    r"^\s*mm_realloc\s+(?P<tag>\S+)\s+#(?P<id>\d+)\s+size=(?P<size>\d+)\s*$"
)
# Sub-events to skip explicitly (allocator plumbing, not refcount traffic).
RE_SUBEVENT = re.compile(r"^\s+(sl_alloc|sl_free|os_alloc)\b")
# Compiler stdout/stderr status noise (e.g. "[CMP] INFO: ...") that the
# compiler prints while a trace is being captured. Not trace data.
RE_COMPILER_STATUS = re.compile(r"^\[[A-Z]{3,}\]\s")


@dataclass
class AllocLifecycle:
    tag: str
    size: int
    create_scope: str
    peak_rc: int = 0
    final_rc: int = 0  # Last observed rc (before free, or 0 if never freed).
    incref_count: int = 0
    decref_count: int = 0
    transfer_count: int = 0
    freed: bool = False
    # (op, rc, scope) — kept compact; we only need it for a later "pointless
    # pair" analysis. Dropped when the object is freed to bound memory.
    events: list[tuple[str, int, str]] = field(default_factory=list)


@dataclass
class ScopeStats:
    increfs: int = 0
    decrefs: int = 0
    transfers: int = 0
    allocs: int = 0


@dataclass
class TagStats:
    allocs: int = 0
    frees: int = 0
    increfs: int = 0
    decrefs: int = 0
    transfers: int = 0
    total_size_bytes: int = 0
    # Distribution of peak rc reached by each allocation of this tag.
    peak_rc_hist: Counter[int] = field(default_factory=Counter)
    # Number of allocations whose *peak rc* was 1 — every bump past 0 had
    # a matching drop. These are "incref/decref traffic without shared
    # ownership" and are the cleanest elimination targets.
    allocs_with_peak_rc_1: int = 0


# ---- Main analysis ----


def analyze(stream: TextIO) -> dict:
    """Stream the trace once and return a results dict."""
    per_tag: defaultdict[str, TagStats] = defaultdict(TagStats)
    per_scope: defaultdict[str, ScopeStats] = defaultdict(ScopeStats)
    # Cross: (scope → tag) counts for hot-spot identification.
    per_scope_tag_ref: defaultdict[tuple[str, str], int] = defaultdict(int)
    # Matched +/- pair detection: an incref+decref on the same (#N, scope)
    # with no intervening scope change on that allocation is the cleanest
    # "pointless bump" shape. Tracked live per allocation.
    # Key: (alloc_id, tag) → list of (scope, op) in order.
    live_alloc_scope_events: defaultdict[tuple[int, str], list[tuple[str, str]]] = \
        defaultdict(list)
    # Matched-pair candidate counts, keyed by (scope, tag).
    pointless_pair_counts: Counter[tuple[str, str]] = Counter()

    live: dict[tuple[int, str], AllocLifecycle] = {}
    raw_live: dict[int, tuple[int, str | None]] = {}  # id → (size, scope)

    totals = Counter()
    total_raw_alloc_bytes = 0
    total_managed_alloc_bytes = 0

    # Peak live tracking.
    current_live_managed = 0
    peak_live_managed = 0
    current_live_raw = 0
    peak_live_raw = 0

    # Freed-lifecycle accumulator: we emit summaries *after* each allocation
    # completes, so we don't need to keep the events list after free.
    # Per-tag "peak_rc_hist" is updated on free.

    line_count = 0
    unrecognized_count = 0

    for raw_line in stream:
        line_count += 1
        line = raw_line.rstrip("\n").rstrip("\r")
        if not line:
            continue
        if RE_SUBEVENT.match(line):
            # Allocator plumbing (sl_alloc/sl_free/os_alloc) — not refcount
            # traffic. The patterns below tolerate leading whitespace so
            # `mm_free` (nested under `mm_decref rc=0`) and a realloc's
            # inner `mm_raw_alloc`/`mm_raw_free` still match.
            continue
        if RE_COMPILER_STATUS.match(line):
            # "[CMP] INFO: ..." and friends — compiler stdout noise mixed
            # into the stderr capture. Not refcount data.
            continue
        if line == "sl_init":
            continue

        m = RE_ALLOC.match(line)
        if m:
            tag = m.group("tag")
            alloc_id = int(m.group("id"))
            size = int(m.group("size"))
            scope = m.group("scope")
            key = (alloc_id, tag)
            lc = AllocLifecycle(
                tag=tag, size=size, create_scope=scope
            )
            live[key] = lc
            per_tag[tag].allocs += 1
            per_tag[tag].total_size_bytes += size
            per_scope[scope].allocs += 1
            totals["alloc"] += 1
            total_managed_alloc_bytes += size
            current_live_managed += 1
            peak_live_managed = max(peak_live_managed, current_live_managed)
            continue

        m = RE_REF.match(line)
        if m:
            op = m.group("op")
            tag = m.group("tag")
            alloc_id = int(m.group("id"))
            rc = int(m.group("rc"))
            scope = m.group("scope")
            key = (alloc_id, tag)
            lc = live.get(key)
            if lc is None:
                # Ref op on an alloc we didn't see (possibly pre-scoped, or
                # from a prior region we trimmed). Still count it.
                lc = AllocLifecycle(
                    tag=tag, size=0, create_scope="<unknown>"
                )
                live[key] = lc
            ts = per_tag[tag]
            ss = per_scope[scope]
            if op == "incref":
                lc.incref_count += 1
                ts.increfs += 1
                ss.increfs += 1
                totals["incref"] += 1
            elif op == "decref":
                lc.decref_count += 1
                ts.decrefs += 1
                ss.decrefs += 1
                totals["decref"] += 1
            else:  # transfer
                lc.transfer_count += 1
                ts.transfers += 1
                ss.transfers += 1
                totals["transfer"] += 1
            lc.peak_rc = max(lc.peak_rc, rc)
            lc.final_rc = rc
            per_scope_tag_ref[(scope, tag)] += 1

            # Matched-pair tracking: record (scope, op) on live alloc;
            # when we see +1/−1 on the same scope back-to-back with no
            # other ops in between on this allocation, count it as a
            # candidate pointless pair.
            events = live_alloc_scope_events[key]
            if events and events[-1] == (scope, "incref") and op == "decref":
                pointless_pair_counts[(scope, tag)] += 1
                events.pop()  # pair consumed
            else:
                events.append((scope, op))
            continue

        m = RE_FREE.match(line)
        if m:
            tag = m.group("tag")
            alloc_id = int(m.group("id"))
            key = (alloc_id, tag)
            lc = live.pop(key, None)
            if lc is not None:
                per_tag[tag].peak_rc_hist[lc.peak_rc] += 1
                if lc.peak_rc == 1:
                    per_tag[tag].allocs_with_peak_rc_1 += 1
                per_tag[tag].frees += 1
                current_live_managed = max(0, current_live_managed - 1)
            live_alloc_scope_events.pop(key, None)
            totals["free"] += 1
            continue

        m = RE_RAW_ALLOC.match(line)
        if m:
            rid = int(m.group("id"))
            size = int(m.group("size"))
            scope = m.group("scope")
            raw_live[rid] = (size, scope)
            totals["raw_alloc"] += 1
            total_raw_alloc_bytes += size
            current_live_raw += 1
            peak_live_raw = max(peak_live_raw, current_live_raw)
            continue

        m = RE_RAW_FREE.match(line)
        if m:
            rid = int(m.group("id"))
            raw_live.pop(rid, None)
            totals["raw_free"] += 1
            current_live_raw = max(0, current_live_raw - 1)
            continue

        m = RE_REALLOC.match(line)
        if m:
            totals["realloc"] += 1
            continue

        unrecognized_count += 1

    # Final accounting: any allocs still live at end never freed — record
    # their peak rc into the histogram too (otherwise the histogram is
    # incomplete for the leak analysis).
    leaked_by_tag: Counter[str] = Counter()
    for (alloc_id, tag), lc in live.items():
        per_tag[tag].peak_rc_hist[lc.peak_rc] += 1
        leaked_by_tag[tag] += 1

    return {
        "totals": dict(totals),
        "line_count": line_count,
        "unrecognized_count": unrecognized_count,
        "per_tag": {
            tag: {
                "allocs": ts.allocs,
                "frees": ts.frees,
                "increfs": ts.increfs,
                "decrefs": ts.decrefs,
                "transfers": ts.transfers,
                "total_size_bytes": ts.total_size_bytes,
                "peak_rc_hist": dict(ts.peak_rc_hist),
                "allocs_with_peak_rc_1": ts.allocs_with_peak_rc_1,
            }
            for tag, ts in per_tag.items()
        },
        "per_scope": {
            scope: {
                "increfs": ss.increfs,
                "decrefs": ss.decrefs,
                "transfers": ss.transfers,
                "allocs": ss.allocs,
            }
            for scope, ss in per_scope.items()
        },
        "per_scope_tag_ref": {
            f"{scope}|||{tag}": count
            for (scope, tag), count in per_scope_tag_ref.items()
        },
        "pointless_pair_candidates": {
            f"{scope}|||{tag}": count
            for (scope, tag), count in pointless_pair_counts.items()
        },
        "peak_live_managed": peak_live_managed,
        "peak_live_raw": peak_live_raw,
        "total_managed_alloc_bytes": total_managed_alloc_bytes,
        "total_raw_alloc_bytes": total_raw_alloc_bytes,
        "leaked_by_tag": dict(leaked_by_tag),
    }


# ---- Reporting ----


def fmt_int(n: int) -> str:
    return f"{n:,}"


def print_report(results: dict, top: int, out: TextIO) -> None:
    w = out.write
    tot = results["totals"]
    lines = results["line_count"]
    w(f"=== mm-trace analysis ===\n")
    w(f"Trace lines read:        {fmt_int(lines)}\n")
    w(f"Unrecognized lines:      {fmt_int(results['unrecognized_count'])}\n")
    w(f"\n")
    w(f"Totals by op:\n")
    for op in ("alloc", "free", "incref", "decref", "transfer",
               "raw_alloc", "raw_free", "realloc"):
        w(f"  mm_{op:10s} {fmt_int(tot.get(op, 0))}\n")
    w(f"\n")

    managed_allocs = tot.get("alloc", 0) or 1
    refops = tot.get("incref", 0) + tot.get("decref", 0)
    w(f"Refcount traffic:\n")
    w(f"  Total incref+decref:   {fmt_int(refops)}\n")
    w(f"  Per allocation:        {refops / managed_allocs:.2f}\n")
    w(f"  Incref:decref ratio:   "
      f"{tot.get('incref', 0)}:{tot.get('decref', 0)}\n")
    w(f"\n")

    w(f"Peak live allocations:   "
      f"managed={fmt_int(results['peak_live_managed'])} "
      f"raw={fmt_int(results['peak_live_raw'])}\n")
    w(f"Total bytes allocated:   "
      f"managed={fmt_int(results['total_managed_alloc_bytes'])} "
      f"raw={fmt_int(results['total_raw_alloc_bytes'])}\n")
    w(f"\n")

    leaked = results["leaked_by_tag"]
    if leaked:
        w(f"LEAKED (mm_alloc without matching mm_free):\n")
        for tag, n in sorted(leaked.items(), key=lambda p: -p[1]):
            w(f"  {tag:40s} {fmt_int(n)}\n")
        w(f"\n")

    # -- Per-tag breakdown, sorted by total refcount traffic --
    per_tag = results["per_tag"]
    tag_items = sorted(
        per_tag.items(),
        key=lambda p: -(p[1]["increfs"] + p[1]["decrefs"])
    )
    w(f"Top {top} tags by refcount traffic (incref+decref):\n")
    w(f"  {'tag':45s} {'allocs':>8s} {'incref':>8s} {'decref':>8s} "
      f"{'ref/alloc':>9s} {'peakrc1%':>9s}\n")
    for tag, ts in tag_items[:top]:
        refs = ts["increfs"] + ts["decrefs"]
        allocs = ts["allocs"] or 1
        ratio = refs / allocs
        peak1_pct = (
            100.0 * ts["allocs_with_peak_rc_1"] / allocs
            if ts["allocs"] > 0 else 0.0
        )
        w(f"  {tag[:45]:45s} {fmt_int(ts['allocs']):>8s} "
          f"{fmt_int(ts['increfs']):>8s} {fmt_int(ts['decrefs']):>8s} "
          f"{ratio:>9.2f} {peak1_pct:>8.1f}%\n")
    w(f"\n")

    # -- Per-scope breakdown --
    per_scope = results["per_scope"]
    scope_items = sorted(
        per_scope.items(),
        key=lambda p: -(p[1]["increfs"] + p[1]["decrefs"])
    )
    w(f"Top {top} scopes by refcount traffic (incref+decref):\n")
    w(f"  {'scope':60s} {'incref':>8s} {'decref':>8s} "
      f"{'xfer':>6s} {'alloc':>6s}\n")
    for scope, ss in scope_items[:top]:
        w(f"  {scope[:60]:60s} {fmt_int(ss['increfs']):>8s} "
          f"{fmt_int(ss['decrefs']):>8s} {fmt_int(ss['transfers']):>6s} "
          f"{fmt_int(ss['allocs']):>6s}\n")
    w(f"\n")

    # -- Pointless-pair candidates --
    pp = results["pointless_pair_candidates"]
    pp_items = sorted(pp.items(), key=lambda p: -p[1])
    pp_total = sum(pp.values())
    w(f"Pointless-pair candidates: {fmt_int(pp_total)} "
      f"(incref+decref on same allocation, same scope, nothing between)\n")
    w(f"  These are the cleanest 'emitter-local' eliminations — both sides\n")
    w(f"  of the pair come from the same scope and aren't separated by\n")
    w(f"  any ownership-affecting op on the allocation.\n")
    w(f"\n")
    w(f"Top {top} (scope, tag) pointless-pair buckets:\n")
    w(f"  {'scope':40s} {'tag':25s} {'pairs':>7s}\n")
    for key, count in pp_items[:top]:
        scope, tag = key.split("|||", 1)
        w(f"  {scope[:40]:40s} {tag[:25]:25s} {fmt_int(count):>7s}\n")
    w(f"\n")

    # -- Cross: scope × tag hot spots --
    pst = results["per_scope_tag_ref"]
    pst_items = sorted(pst.items(), key=lambda p: -p[1])
    w(f"Top {top} (scope, tag) refcount hot spots:\n")
    w(f"  {'scope':40s} {'tag':25s} {'refops':>7s}\n")
    for key, count in pst_items[:top]:
        scope, tag = key.split("|||", 1)
        w(f"  {scope[:40]:40s} {tag[:25]:25s} {fmt_int(count):>7s}\n")
    w(f"\n")

    # -- Peak-rc-1 analysis: refcount traffic whose peak rc never
    # exceeded 1 is entirely elision-eligible (no actual shared ownership
    # ever existed on these allocations). --
    peak1_allocs = sum(ts["allocs_with_peak_rc_1"] for ts in per_tag.values())
    peak1_pct = (
        100.0 * peak1_allocs / (tot.get("alloc", 0) or 1)
    )
    w(f"Allocations with peak rc = 1: {fmt_int(peak1_allocs)} "
      f"({peak1_pct:.1f}% of allocations)\n")
    w(f"  Every incref on these allocations had a matching decref at\n")
    w(f"  rc=1 (no ownership was ever truly shared). The refcount traffic\n")
    w(f"  on these is, in principle, 100% eliminable — the only reason\n")
    w(f"  it's there is conservative emission.\n")


# ---- Entry point ----


def main(argv: list[str]) -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("path", nargs="?", help="Trace file path (default: stdin)")
    p.add_argument("--json", action="store_true",
                   help="Emit machine-readable JSON instead of a report")
    p.add_argument("--top", type=int, default=20,
                   help="Top N entries to show in each list (default 20)")
    args = p.parse_args(argv[1:])

    if args.path:
        with open(args.path, "r", encoding="utf-8", errors="replace") as f:
            results = analyze(f)
    else:
        results = analyze(sys.stdin)

    if args.json:
        json.dump(results, sys.stdout, indent=2, default=str)
        sys.stdout.write("\n")
    else:
        print_report(results, top=args.top, out=sys.stdout)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
