---
title: Debug Stream
description: The shared-memory event ring a --debugstream build writes and maxon monitor reads — its wire format, producer and consumer.
sidebar:
  order: 5
---

A program built with `--debugstream` writes events into a shared-memory ring that `maxon monitor` reads while
the program runs. What the flags and the command do for a user is under
[Debugging and Profiling](../CLI_REFERENCE.md#debugging-and-profiling).

## The wire format

The format is frozen: the producer and the consumer are compiled separately and must agree byte for byte.
Its one definition is the constant registry in `maxon-bin/Compiler/Runtime/DebugStreamRuntime.maxon`, which
the producer is built from and the decoder (`maxon-bin/Debug/DebugStreamDecoder.maxon`) imports rather than
restating a number. The registry declares codes this producer never emits, because the decoder needs them
all. The schema version is 2.

**Segment.** A 128-byte header, a 2 MiB ring and a 64 KiB reserve that nothing writes, kept so both sides map
the same length. An entry at absolute cursor position `c` lives at `base + 128 + (c & (ringSize - 1))`.

| Offset | Header field |
| --- | --- |
| 0 | magic, `0x4D58444253545200` |
| 8 | schema version the monitor expects |
| 16 | ring size |
| 24 | write cursor (absolute; never wraps) |
| 32 | read cursor |
| 40 | flags: bit 0 is "producer alive" |
| 56 | start timestamp, in the tick-count milliseconds the producer's clock uses |
| 64, 72 | total and dropped events |
| 96 | peak ring use |
| 104 | schema version the producer announces |

**Entry header**, one 64-bit word: bits 0–7 the event type, 8–15 flags (bit 8 of the word is *committed*),
16–31 the entry's total size in bytes, 8-aligned, and 32–63 milliseconds since the start timestamp. An entry
never straddles the ring's end; the producer fills the tail with a padding entry (type 255), born committed.

| Family | Codes | Payload after the header |
| --- | --- | --- |
| Memory manager | 1–9 (alloc, free, incref, decref, …) | allocation id; then tag index (16 bits), scope length (16) and value (32) |
| Scheduler | `0x20`–`0x2C` (spawn, await, yield, resume, I/O) | trace id |
| Log | `0x60`–`0x63` (phase begin and end, event, text) | green thread and processor id; category, level, a 16-bit name and a 32-bit unit; then two arguments or UTF-8 text |

Every 16-bit name or tag field bounds the tables below to 65,535 entries.

## Names

Names travel in the executable, not the ring. `__DebugStream.nameId("…")` is interned at compile time and
lowered to a constant; two tables are written into the executable's symbol data — `MXDS_TAGS` for allocation
tags and `MXDS_STRS` for names, each a magic, a 16-bit count and length-prefixed strings. Index 0 means "no
name". The monitor reads both tables from the program it launched (`InternedNameBlob.maxon`); the
`--census-by-tag` and `--allocations-by-tag` reports read the same tag table from the compiler's own
executable.

## The producer

A build carries the producer only with `--debugstream` and only if the program uses the heap. It stays dark
until `__ds_init` finds a segment named by `MAXON_DEBUGSTREAM`, checks the magic and version, and only then
arms `__ds_base`.

- **Without the flag**, emit sites compile to nothing: no load, no call.
- **With the flag but no monitor**, a memory-manager or scheduler site costs a load of `__ds_base`, a compare
  and a branch before any call; a log site always makes one call and tests inside it, because lowering
  cannot split a block.

`__ds_reserve` claims space under a lock, writes the header uncommitted and advances the write cursor; the
emitter writes the payload; `__ds_commit` sets the committed bit. The producer never overwrites unread data:
with no room it drops the event and counts it. Log entries carry the current green thread and processor id,
0 and 0 outside the scheduler.

The write cursor and the commit bit are published with release stores, so the monitor sees a header before
the cursor that reaches it and a payload before the bit that commits it. The producer reads the monitor's read
cursor with an acquire load before it reuses the space behind it. The monitor reads and writes these words
through `SharedSegment.readWord` (an acquire) and `writeWord` (a release). The lock orders producers against
each other only. On x64 the ordered accesses are plain loads and stores.

## The monitor

`maxon monitor` creates and seeds the segment — magic, version, ring size, start timestamp — before it
spawns the program with `MAXON_DEBUGSTREAM` naming it (`maxon_ds_<pid>_<nonce>`), because a program that
attaches to a half-written header traces nothing. It refuses a producer announcing another schema version.

Each drain reads the committed prefix of the ring, stops at the first uncommitted entry, copies it out in at
most two pieces across the wrap, and publishes the read cursor. An uncommitted entry left by a producer that
exited is stepped over and counted as abandoned. The summary reports events, drops, abandoned entries and
peak ring use.

The interactive debugger's control segment (`MAXON_DEBUG`, [Debugger](debugger.md)) is separate. Its only
link to this ring is the write cursor's offset, which the debug agent reads at a stop so `maxon debug --trace`
can show the events leading up to it.
