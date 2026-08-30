---
feature: streaming-subprocess
status: experimental
keywords: [subprocess, streaming, stdio, pipe, readLine, writeLine, handle-table, async, await, yield, iocp, overlapped, green-threads, concurrency]
category: concurrency
---

# Streaming subprocess — the long-lived child with caller-driven stdio (P1.5 dogfood slice 1b)

## Documentation

The streaming subprocess builtins expose a long-lived Windows child whose stdin / stdout / stderr the
caller drives by hand, one line at a time. They generalize the one-shot `spawnReadLine` probe (slice 1a)
into a real handle-table API: a spawn creates THREE pipes (a synchronous outbound stdin pipe the parent
writes, and two overlapped IOCP-registered pipes the parent reads for stdout and stderr), hands back a
non-negative integer handle indexing a runtime table, and every later call names that handle.

- `subpSpawn(cmd)` spawns the child named by the command `String` with all three std streams redirected to
  pipes, and returns the handle (or `-1` on spawn failure). x64-windows only.
- `subpReadLine(h)` reads one line from the child's stdout, INCLUDING the trailing `\n` (so a caller can
  distinguish a blank line from EOF by length). It YIELDS the green thread while the read is in flight
  (parking on the calling GT's OVERLAPPED, resumed by the IOCP completion thread), so other green threads
  make progress. Returns an empty `String` on EOF, and the EOF is latched. `subpReadErrLine(h)` is the
  stderr twin (post-exit use only in the harness).
- `subpWriteLine(h, line: s)` writes `s + "\n"` to the child's stdin (a synchronous `WriteFile`), returning
  `0` on success or non-zero on a broken pipe.
- `subpCloseStdin(h)` closes the parent's write end of stdin, so the child sees EOF.
- `subpWait(h)` blocks until the child exits and returns its exit code.
- `subpRelease(h)` closes every OS handle the child owns and marks the table slot free for reuse. The
  slot's reusable line buffers persist across spawn/release cycles, so a spawn+read+release loop is
  memory-bounded rather than leaking a fresh buffer per iteration (slice 1a's measured debt).

**Releasing a handle out from under a parked reader is SAFE.** Each table slot carries a GENERATION that
`subpSpawn` bumps when it claims the slot, so a `(slot, generation)` pair names one handle for all time. A reader
captures the generation at park time and re-reads it on resume: if a `subpRelease(h)` freed the slot and a later
`subpSpawn` reused that index while the reader slept, the generations differ, so the stale reader returns its own
empty/EOF result (correct — its handle is gone) and writes NO slot state back, leaving the new handle's stream
untouched. Without it the resumed reader stamped its EOF into whatever handle now owned the slot, silently making
the NEW handle read EOF — memory-safe, but a cross-handle wrong answer.

The read line-buffers per handle: each stdout/stderr stream carries a growable byte buffer that a read
appends into; the reader scans for `\n`, returns everything through the first one, and keeps the tail
buffered for the next call. `cmd /c echo hello` writes `hello\r\n` = 7 bytes.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** The streaming reader parks its green thread on the driver, and several cases reach
`__gt_sleep` directly — both x64-windows only at this rung.

## Tests

<!-- test: streaming-subprocess.echo-read -->
<!-- targets: x64-windows -->
The main thread (GT0) spawns `cmd /c echo hello`, reads its one stdout line (`hello\r\n`, 7 bytes), waits,
and releases. The read yields on GT0's own scheduler loop until the completion thread signals it done. The
line's byte length (7) is returned.
```maxon
function main() returns ExitCode
	let h = subpSpawn("cmd /c echo hello")
	let line = subpReadLine(h)
	let n = line.byteLength()
	let code = subpWait(h)
	print("{code}")
	subpRelease(h)
	return n as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
0

```

<!-- test: streaming-subprocess.eof-latched -->
<!-- targets: x64-windows -->
After the single line, a second read returns EOF (empty string, length 0), and a third read stays EOF
(latched). Returns line1 length (7) plus 10× the EOF length (0) — so any non-empty EOF line would show.
```maxon
function main() returns ExitCode
	let h = subpSpawn("cmd /c echo hello")
	let line1 = subpReadLine(h)
	let eof1 = subpReadLine(h)
	let eof2 = subpReadLine(h)
	let code = subpWait(h)
	print("{code}")
	subpRelease(h)
	return (line1.byteLength() + eof1.byteLength() * 10 + eof2.byteLength() * 10) as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
0

```

<!-- test: streaming-subprocess.spawned-reader -->
<!-- targets: x64-windows -->
The read runs inside a SPAWNED green thread, so its resume goes through the cross-thread path: the
completion thread re-enqueues the reading GT under the run-queue lock and the driver switches back into it.
The reader returns the line's byte length (7); `main` awaits it.
```maxon
function reader() returns Integer
	let h = subpSpawn("cmd /c echo hello")
	let line = subpReadLine(h)
	let n = line.byteLength()
	let code = subpWait(h)
	print("{code}")
	subpRelease(h)
	return n
end 'reader'

function main() returns ExitCode
	let r = async reader()
	let n = await r
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```
```stdout
0

```

<!-- test: streaming-subprocess.interleave-with-sleep -->
<!-- targets: x64-windows -->
A concurrent `async sleep` runs while the streaming read is in flight, PROVING the read yields: if the read
blocked the single M, the sleeper could not run. The read (7 bytes) plus the sleeper's `1` sum to 8.
```maxon
function reader() returns Integer
	let h = subpSpawn("cmd /c echo hello")
	let line = subpReadLine(h)
	let n = line.byteLength()
	let code = subpWait(h)
	print("{code}")
	subpRelease(h)
	return n
end 'reader'

function napper() returns Integer
	sleep(50)
	return 1
end 'napper'

function main() returns ExitCode
	let s = async napper()
	let r = async reader()
	let a = await s
	let n = await r
	return (n + a) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
8
```
```stdout
0

```

<!-- test: streaming-subprocess.two-lines -->
<!-- targets: x64-windows -->
A child prints two lines (`a\r\n` then `b\r\n`, 3 bytes each). Both are read across the buffered line reader
(the second line arriving in the same or a later chunk than the first). Returns the summed byte length (6).
```maxon
function main() returns ExitCode
	let h = subpSpawn("cmd /c (echo a& echo b)")
	let l1 = subpReadLine(h)
	let l2 = subpReadLine(h)
	let code = subpWait(h)
	print("{code}")
	subpRelease(h)
	return (l1.byteLength() + l2.byteLength()) as ExitCode
end 'main'
```
```exitcode
6
```
```stdout
0

```

<!-- test: streaming-subprocess.write-echo -->
<!-- targets: x64-windows -->
A `sort` child echoes stdin after EOF, sorted. The parent writes `22` then `1`, closes stdin, then reads
the sorted output back: `sort` orders them lexicographically, so the shorter `1` line returns before the longer
`22` line. The test asserts the line-terminator-INVARIANT round-trip facts — both writes succeeded (`w1==w2==0`),
the round trip delivered a non-empty first line, and it is SHORTER than the second (`1`-before-`22`) — not exact
byte counts, because the line ending `sort` emits is environment-dependent (LF under some consoles, CRLF under
others: `1\n`/`22\n` are 2/3 bytes, `1\r\n`/`22\r\n` are 3/4 — `first < second` holds either way). An unsorted,
dropped, or failed-write outcome breaks at least one clause. Returns `23` on a good round trip.
```maxon
function main() returns ExitCode
	let h = subpSpawn("cmd /c sort")
	let w1 = subpWriteLine(h, line: "22")
	let w2 = subpWriteLine(h, line: "1")
	subpCloseStdin(h)
	let first = subpReadLine(h)
	let second = subpReadLine(h)
	let code = subpWait(h)
	print("{code}")
	subpRelease(h)
	var result = 0
	if w1 == 0 and w2 == 0 and first.byteLength() > 0 and first.byteLength() < second.byteLength() 'roundtrip'
		result = 23
	end 'roundtrip'
	return result as ExitCode
end 'main'
```
```exitcode
23
```
```stdout
0

```

<!-- test: streaming-subprocess.drop-reader-then-reread -->
<!-- targets: x64-windows -->
A streaming reader is DROPPED mid-read, then the SAME handle is re-read and must still work. `reader(h)` runs
`subpReadLine(h)` in an `async` GT; the child (`ping -n 3` then `echo hi`) delays ~2 s, so the reader parks on
the overlapped read with no data. `dropIt` sleeps 200 ms (the reader is parked) then returns, DROPPING the
un-awaited promise. Because the read pipe is TABLE-owned (not the GT's), the drop's cancel arm CancelIoEx +
drains but must NOT close it — so the follow-up `subpReadLine(h)` on the same handle re-issues a fresh read and
gets `hi\r\n` (4 bytes), and `subpRelease` is the sole pipe-closer (no double-close). Before the ownership
marker, the drop closed the shared pipe and this returned 0 (EOF forever).
```maxon
function reader(h Integer) returns Integer
	let line = subpReadLine(h)
	return line.byteLength()
end 'reader'

function dropIt(h Integer) returns Integer
	_ = async reader(h)
	sleep(200)
	return 0
end 'dropIt'

function main() returns ExitCode
	let h = subpSpawn("cmd /c ping -n 3 127.0.0.1 >nul & echo hi")
	_ = dropIt(h)
	let line = subpReadLine(h)
	let n = line.byteLength()
	_ = subpWait(h)
	subpRelease(h)
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
4
```

<!-- test: streaming-subprocess.release-while-parked-then-reuse-slot -->
<!-- targets: x64-windows -->
`subpRelease(h)` runs while an `async` reader is still PARKED on `h`, and the freed slot is then immediately
reused by a new `subpSpawn` — the new handle must read correctly. The reader parks on the slow child
(`ping -n 3` then `echo hi`); `subpRelease(h)` frees the slot out from under it; `subpSpawn` reuses that index
for `h2` and BUMPS the slot generation. When the stale reader resumes it sees the generation changed, returns an
empty line (0) and writes NOTHING back — so `h2` still reads `second\r\n` (8 bytes). Returns `n2*10 + rn` =
8×10 + 0 = 80. Without the generation guard the stale reader stamped its EOF into `h2`'s stream and this
returned 0.
```maxon
function reader(h Integer) returns Integer
	let line = subpReadLine(h)
	return line.byteLength()
end 'reader'

function main() returns ExitCode
	let h = subpSpawn("cmd /c ping -n 3 127.0.0.1 >nul & echo hi")
	let r = async reader(h)
	sleep(300)
	subpRelease(h)
	let h2 = subpSpawn("cmd /c echo second")
	let rn = await r
	let l2 = subpReadLine(h2)
	let n2 = l2.byteLength()
	let c2 = subpWait(h2)
	print("{c2}")
	subpRelease(h2)
	return (n2 * 10 + rn) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
80
```
```stdout
0

```

<!-- test: streaming-subprocess.spawn-release-loop -->
<!-- targets: x64-windows -->
Twelve spawn+read+release cycles reuse the same table slot (and its line buffer) each iteration, proving no
OS-handle leak and no per-iteration buffer leak across a many-iteration loop. Each iteration reads `hello\r\n`
(7 bytes); the accumulator returns 12×7 mod 256 = 84.
```maxon
function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 12 'loop'
		let h = subpSpawn("cmd /c echo hello")
		let line = subpReadLine(h)
		total = total + line.byteLength()
		_ = subpWait(h)
		subpRelease(h)
		i = i + 1
	end 'loop'
	return total as ExitCode
end 'main'
```
```exitcode
84
```
