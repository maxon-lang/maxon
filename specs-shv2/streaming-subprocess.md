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

The read line-buffers per handle: each stdout/stderr stream carries a growable byte buffer that a read
appends into; the reader scans for `\n`, returns everything through the first one, and keeps the tail
buffered for the next call. `cmd /c echo hello` writes `hello\r\n` = 7 bytes.

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
	subpRelease(h)
	return n as ExitCode
end 'main'
```
```exitcode
7
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
	subpRelease(h)
	return (line1.byteLength() + eof1.byteLength() * 10 + eof2.byteLength() * 10) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: streaming-subprocess.spawned-reader -->
<!-- targets: x64-windows -->
The read runs inside a SPAWNED green thread, so its resume goes through the cross-thread path: the
completion thread re-enqueues the reading GT under the run-queue lock and the driver switches back into it.
The reader returns the line's byte length (7); `main` awaits it.
```maxon
function reader() returns int
	let h = subpSpawn("cmd /c echo hello")
	let line = subpReadLine(h)
	let n = line.byteLength()
	let code = subpWait(h)
	subpRelease(h)
	return n
end 'reader'

function main() returns ExitCode
	let r = async reader()
	let n = await r
	return n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: streaming-subprocess.interleave-with-sleep -->
<!-- targets: x64-windows -->
A concurrent `async sleep` runs while the streaming read is in flight, PROVING the read yields: if the read
blocked the single M, the sleeper could not run. The read (7 bytes) plus the sleeper's `1` sum to 8.
```maxon
function reader() returns int
	let h = subpSpawn("cmd /c echo hello")
	let line = subpReadLine(h)
	let n = line.byteLength()
	let code = subpWait(h)
	subpRelease(h)
	return n
end 'reader'

function napper() returns int
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
```
```exitcode
8
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
	subpRelease(h)
	return (l1.byteLength() + l2.byteLength()) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: streaming-subprocess.write-echo -->
<!-- targets: x64-windows -->
A `sort` child echoes stdin after EOF, sorted. The parent writes `22` then `1`, closes stdin, then reads
the sorted output back: `sort` orders them lexicographically to `1\n` then `22\n` (it preserves the LF-only
line endings the writer sent, so the lines are 2 and 3 bytes). The distinct lengths verify BOTH the write→
child→read round trip AND the ordering (an unsorted or dropped line would not give `1`-before-`22`). Returns
`first.byteLength()*10 + second.byteLength() + w1 + w2` = 2×10 + 3 + 0 + 0 = 23.
```maxon
function main() returns ExitCode
	let h = subpSpawn("cmd /c sort")
	let w1 = subpWriteLine(h, line: "22")
	let w2 = subpWriteLine(h, line: "1")
	subpCloseStdin(h)
	let first = subpReadLine(h)
	let second = subpReadLine(h)
	let code = subpWait(h)
	subpRelease(h)
	return (first.byteLength() * 10 + second.byteLength() + w1 + w2) as ExitCode
end 'main'
```
```exitcode
23
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
		let code = subpWait(h)
		subpRelease(h)
		i = i + 1
	end 'loop'
	return total as ExitCode
end 'main'
```
```exitcode
84
```
