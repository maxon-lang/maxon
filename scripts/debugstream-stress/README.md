# DebugStream commit-bit validation harness

The acceptance test for the ring buffer's **commit bit**. It exists because a fix for a race is
worth nothing without a test that **fails before it and passes after it** — and this one does.

## Run it

```
bash scripts/debugstream-stress/validate.sh
```

Exits `0` iff every check passes. Takes ~1 minute. Override the compiler with
`MAXON=/path/to/maxon.exe` and the producer-pacing band with `PACINGS="80000 120000"`.

## The race

`__ds_reserve` publishes an entry — header written, `write_cursor` advanced, ring lock released —
and the **caller** writes the payload afterwards, outside the lock. So there is a window in which
the monitor (a separate process, polling) can see an entry below `write_cursor` whose payload has
not been written, copy it, and decode **stale ring bytes as event data**.

It affected every event family — `mm`, `sched`, `dbg` and `log` alike — and it had always been
there. The commit bit closes it: an entry is born UNCOMMITTED, `__ds_commit` sets the bit with a
release store once the payload is in, and the monitor decodes nothing — and advances past nothing —
that does not carry it.

## What the stress does

`ds-race.maxon` runs 12 green threads on real OS workers, each emitting two interleaved streams
whose payloads are **checksums of themselves**:

| Stream | Payload | What it proves |
|---|---|---|
| `LOG_EVENT` | `arg0 = 1 + idx*seqBase + seq`, `arg1 = arg0*3 + 11`, `unit = idx+1` | Nothing is LOST or DUPLICATED. Its payload is five stores, so its window is nanoseconds wide — **it does not catch the race on its own** (measured: 14400 events, zero tears). |
| `LOG_TEXT` | the message `t<idx>s<seq>-AAAA…` (16 KB tail) | **The tearer.** Its payload ends in a byte-copy loop, so the window between "entry visible" and "entry written" is tens of MICROSECONDS — a window a polling reader can land in. |

A torn read can only produce one of two things, and `verify.awk` checks for both:

* **fresh ring memory** (all zeros) — `cat`/`lvl` are 0, `gt` is null, `arg0` is 0 (out of range by
  construction), the checksum fails, and a text tail is not the message its own header claims.
* **a previous generation's payload** at the same ring offset — internally consistent, but its
  `(unit, seq)` was already consumed, so it is a **duplicate**.

## The operating point — the part that is easy to get wrong

The monitor copies its pending region **from the bottom up**, and sleeps only when the ring is
**empty**. Both extremes therefore *hide* the bug:

* **Producer too fast** ⇒ the monitor is permanently backlogged, its pending region grows to
  megabytes, and its memcpy reaches an in-flight entry only after copying everything below it — by
  which time the producer has long finished. A flat-out producer, even a *single* one, never tears.
  This is the easiest way to write a stress that proves nothing.
* **Producer too slow** ⇒ the ring keeps going empty, the monitor sleeps ~1-15 ms between polls, and
  never looks during the microseconds an entry is in flight.

The bug lives in between: the ring **never empty** (so the monitor polls continuously, at its decode
rate) but **shallow** (so its copy reaches an in-flight entry at once). Where that sits depends on
how fast the machine is relative to the monitor, which is why the pacer is a **command-line
argument** and `validate.sh` sweeps a *band* of it rather than betting on one number.

## The two verdicts, kept separate

`verify.awk` answers two different questions, and conflating them is what would force the sweep to
stay in the narrow drop-free zone:

* **INTEGRITY** (exit 1) — every decoded entry satisfied its own payload invariant. This is the race
  gate, and it is meaningful whatever else happened: a torn payload is a torn payload even in a run
  whose ring overflowed.
* **COMPLETENESS** (exit 2 = inconclusive) — nothing lost or duplicated, decoded count == emitted
  count. Only *answerable* when the ring did not overflow: a DROPPED event never reached the ring at
  all, which is a capacity fact about the test, not a correctness fact about the compiler.

`validate.sh` requires zero integrity violations at **every** pacing, and at least one pacing that
ran drop-free so the exact-count check was actually answered.

## Results

Against the **unfixed** compiler, 5 of the 6 swept pacings caught torn payloads:

```
  --- pacing=220000 ---
  VIOLATION: torn-tail(len=16390)  <<[+0000.078] log_text cat=7 lvl=5 gt=0x22a8524a000 P1 unit=1                        >>
```

— an entry the monitor decoded and printed whose 16 KB tail had not been written at all.

Against the **fixed** compiler: zero violations across all six pacings, with 180000 events + 7200
texts decoded exactly, none lost, none duplicated.

## Check 2 — the monitor must not hang

A producer killed *between* `__ds_reserve` and `__ds_commit` leaves an entry that will never be
committed. The old drain loop (`while readCursor < writeCursor`) would spin on it **forever**. The
monitor now recognises that its producer is gone, steps over the abandoned entry, and **reports it**:

```
[debugstream] 103532 events, 0 dropped, 2 abandoned (producer died mid-entry), peak buffer: 1.8 MB / 2.0 MB (89%)
```

Whether a kill lands inside an entry or between two of them is a race, so a zero count is not a
failure — but the monitor terminating is not optional, and that is what the check asserts.

## Known unrelated bug found here

At a **32 KB** tail, twelve green threads each building a fresh 32 KB `String` per iteration
**segfault the runtime** — reproducible with no `__DebugStream` call anywhere in the program, so it
is a separate pre-existing bug and not this one. The tail is capped at 16 KB, which is comfortably
inside the working range and already a wide enough window.
