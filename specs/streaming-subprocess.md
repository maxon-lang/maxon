---
feature: streaming-subprocess
status: experimental
keywords: [subprocess, streaming, stdio, pipe, readLine, writeLine, handle-table, async, await, yield, iocp, overlapped, green-threads, concurrency]
category: concurrency
---

# Streaming subprocess — the long-lived child with caller-driven stdio (P1.5 dogfood slice 1b)

## Documentation

The streaming subprocess builtins expose a long-lived child whose stdin / stdout / stderr the caller
drives by hand, one line at a time. They generalize the one-shot `spawnReadLine` probe (slice 1a) into a
real handle-table API: a spawn creates THREE pipes — one outbound for stdin that the parent writes, two
inbound that the parent reads — hands back a non-negative integer handle that packs a runtime table slot
with the slot's GENERATION (the slot index in the low part, the generation above it; `-1` is the
failure answer), and every later call names that handle. A handle therefore names a CHILD, not a slot:
once released it answers as a released handle for all time, and the slot's next occupant is unreachable
through it (`a-released-handle-never-reaches-the-slots-next-child`).

⚠ **WHICH LANES RUN THESE, AND HOW EACH PARKS A READER, IS THE *Targets* SECTION BELOW.** The mechanism
differs — Windows hands the two inbound pipes to the scheduler's poller and parks the reader on the READ
itself; the POSIX lane makes the read end non-blocking and parks it on the DESCRIPTOR — and that difference
is a measured property with cases pinning it, not an aside.

- `subpSpawn(cmd)` spawns the child named by the command `String` with all three std streams redirected to
  pipes, and returns the handle (or `-1` on spawn failure). The command reaches `CreateProcessA` on
  Windows and `/bin/sh -c` on the POSIX lane, which is why each subject carries a `posix-…` sibling.
- `subpReadLine(h)` reads one line from the child's stdout, INCLUDING the trailing `\n` (so a caller can
  distinguish a blank line from EOF by length). ⛔ **IT YIELDS ON EVERY LANE AND ONLY THE MECHANISM DIFFERS**,
  which is the lane difference above: on Windows the read is in flight and the green thread parks on its
  own poll source; on the POSIX lane the read end is non-blocking and the green thread parks on its poll
  descriptor. Both are resumed by the poller. Other green threads make progress either
  way. Returns an empty `String` on EOF, and the EOF is latched. `subpReadErrLine(h)` is the
  stderr twin (post-exit use only in the harness).
- `subpWriteLine(h, line: s)` writes `s + "\n"` to the child's stdin, synchronously, returning
  `0` on success or non-zero on a broken pipe.
- `subpCloseStdin(h)` closes the parent's write end of stdin, so the child sees EOF.
- `subpWait(h)` blocks until the child exits and returns its exit code.
- `subpRelease(h)` closes every OS handle the child owns and marks the table slot free for reuse. The
  slot's reusable line buffers persist across spawn/release cycles, so a spawn+read+release loop is
  memory-bounded rather than leaking a fresh buffer per iteration (slice 1a's measured debt).

**Releasing a handle out from under a parked reader is SAFE.** Each table slot carries a GENERATION that
`subpSpawn` bumps when it claims the slot, and the handle it returns packs that generation above the slot
index, so a `(slot, generation)` pair names one handle for all time. Every entry that takes a handle
checks "in range, live, AND the same generation" before touching the slot, which is what makes a handle
kept past its `subpRelease` — or a second copy of one — answer exactly as a dead handle does (`-1` from
the integer entries, an empty string from the readers, a void no-op from release and closeStdin) rather
than reaching whatever child now owns the slot. The one window that check cannot cover is a park: a
reader passes the entry check, sleeps, and the slot may be released and reused while it sleeps. So a
reader also captures the generation at park time and re-reads it on resume: if a `subpRelease(h)` freed
the slot and a later `subpSpawn` reused that index while the reader slept, the generations differ, so
the stale reader returns its own empty/EOF result (correct — its handle is gone) and writes NO slot
state back, leaving the new handle's stream untouched. Without it the resumed reader stamped its EOF
into whatever handle now owned the slot, silently making the NEW handle read EOF — memory-safe, but a
cross-handle wrong answer.

The read line-buffers per handle: each stdout/stderr stream carries a growable byte buffer that a read
appends into; the reader scans for `\n`, returns everything through the first one, and keeps the tail
buffered for the next call. `cmd /c echo hello` writes `hello\r\n` = 7 bytes.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** The streaming reader parks its green thread, and several cases reach
`__gt_sleep` directly — neither of which wasm32-wasi lowers.

### ⭐ arm64-macOS RUNS THESE BUILTINS, AND THE READ IS WHERE THE TWO LANES GENUINELY PART

`TargetFacilities` answers `subprocess gives true` for arm64-macOS, so all seven bare-name `subp*`
builtins compile and run there. The programs above cannot be widened — they spawn `cmd`, and
`subpSpawn(cmd)` reaches `/bin/sh -c` on that lane rather than `CreateProcessA` — so each subject
this lane can express carries a `posix-…` sibling marked `arm64-macos`, exactly as
`process-background-priority.md` pairs its two units.

⚠ **A READ PARKS HERE TOO, ON A POLL DESCRIPTOR RATHER THAN AN OVERLAPPED.** The pipe's read end is
non-blocking and carries a poll source, so `subpReadLine` gives its machine up until the poller
reports the descriptor ready. `posix-a-parked-line-read-needs-no-rescue` measures exactly that: the
retake delta across one `subpReadLine` whose child delays a second is ZERO, so the machine was
released deliberately rather than rescued out of a blocking call.

⛔ **THREE OF THE NINE CASES ABOVE STILL HAVE NO SIBLING, AND THAT IS NOW A GAP RATHER THAN A LANE
FACT.** `interleave-with-sleep`, `drop-reader-then-reread` and `release-while-parked-then-reuse-slot`
each assert something about a reader that is PARKED — a concurrent sleeper making progress, a drop
cancelling a read in flight, a generation guard catching a resume after the slot was reused. Their
subject exists on these lanes now; the ports are simply unwritten.

## Tests

<!-- test: streaming-subprocess.echo-read -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
`main` spawns `cmd /c echo hello`, reads its one stdout line (`hello\r\n`, 7 bytes), waits, and releases.
The read parks `main` until the poller readies it. The line's byte length (7) is returned.
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

<!-- test: streaming-subprocess.posix-echo-read -->
<!-- unsupported-targets: x64-windows -->
`echo-read`'s subject on the POSIX lane, and the case that proves the seven bare-name `subp*` builtins run
here at all. `main` spawns `echo hello`, reads its one stdout line, waits, and releases; the line's byte
length (`hello\n`, SIX — an LF where `cmd /c echo` writes CRLF) is returned.

⛔⛔ **THIS ENTIRE FAMILY SEGFAULTED ON THIS LANE UNTIL THE CASE EXISTED, AND NOTHING REFUSED IT.**
`subpSpawn(cmd)` is the third door that takes a whole command LINE, and it was the one that did not ask
`SubprocessCommandShape`: it stored the raw bytes into the spawn request's command field and handed them to
a core that, under `argumentVector`, reads that field as a `char *const argv[]`. The first eight ASCII
bytes of `"echo hello"` were dereferenced as `argv[0]`. MEASURED: **exit 139 (SIGSEGV) at the spawn
itself**, in a program that compiled clean — the facility table already said `subprocess gives true` here,
so the E3104 gate that covers a target with no substrate correctly did not fire. Its two siblings
`__gt_process_run` and `__gt_io_read` had routed their line through `emitSubpCommandFromLine` since MAC8;
this one now does too.

⚠ **THE READ YIELDS ON THIS LANE, ON A POLL DESCRIPTOR RATHER THAN AN OVERLAPPED — BUT NOT MEASURABLY
HERE.** `echo hello` races the parent to the pipe, so whether this read finds its line already waiting or
parks for it is not decidable from the answer. `posix-a-parked-line-read-needs-no-rescue` puts a
one-second delay under the child so the park is certain, and measures where the machine went.
```maxon
function main() returns ExitCode
	let h = subpSpawn("echo hello")
	let line = subpReadLine(h)
	let n = line.byteLength()
	let code = subpWait(h)
	print("{code}")
	subpRelease(h)
	return n as ExitCode
end 'main'
```
```exitcode
6
```
```stdout
0

```

<!-- test: streaming-subprocess.posix-a-parked-line-read-needs-no-rescue -->
<!-- unsupported-targets: x64-windows -->
<!-- procs: 1 -->
⭐ **THE READ PARKS, AND THE SYSTEM MONITOR NEVER HAS TO TAKE THE PROCESSOR BACK FOR IT.** This is the
witness `spawn-read-line.posix-a-parked-line-read-yields-to-a-sleeper` cannot carry: `spawnReadLine` spawns
AND reads in one call, so a bracket around it counts the SPAWN — `osProcessSpawn` is
`SyscallClass.blocking`, and at ONE processor the machine inside `clone`+`execve` holds the only P, so
`__sched_retake`'s `(nmspinning + npidle) > 0` term is zero and the retake always fires. That retake is
correct; it is what lets anything else run while a child is being spawned. Here the two are SEPARATE calls,
so `beforeRetake` … `schedRetakeCount() - beforeRetake` brackets `subpReadLine` ALONE and the spawn's
retake falls outside it. The idiom is `netpoll-socket`'s verbatim.

The child delays a second before its line, so the read is certainly outstanding when the scheduler runs out
of other work. `rescued=false` then says the reader went onto its poll descriptor and gave the machine up
deliberately, rather than sitting inside `read(2)` for `__sysmon` to rescue. The line's byte length is the
exit code (`hello\n`, SIX), so a read that returned nothing cannot pass the case.

⚠ **BEFORE THE PIPE CARRIED A POLL DESCRIPTOR THE DELTA WAS AT LEAST ONE.** `subpReadLine` blocked its
machine in the read, `__sysmon` observed the same bracketed call twice and took the processor back. The
witness is EXACTLY ZERO for the reason `netpoll-socket`'s `stuck=` witness is: a tolerance would admit
precisely the arrangement the case exists to refuse.
```maxon
function main() returns ExitCode
	let h = subpSpawn("sleep 1; echo hello")
	let beforeRetake = __Builtins.schedRetakeCount()
	let line = subpReadLine(h)
	let delta = __Builtins.schedRetakeCount() - beforeRetake
	let rescued = delta > 0
	let n = line.byteLength()
	print("rescued={rescued}\n")
	_ = subpWait(h)
	subpRelease(h)
	return n as ExitCode
end 'main'
```
```exitcode
6
```
```stdout
rescued=false
```

<!-- test: streaming-subprocess.posix-a-drained-child-costs-no-timer-scan -->
<!-- unsupported-targets: x64-windows -->
<!-- procs: 1 -->
⭐ **DRAINING A CHILD MUST NOT COST ANYTHING PER LIVE SLEEPER, AND `schedTimerScanStepCount()` IS THE ONLY
READING THAT CAN SAY SO.** Disarming a deadline is a LINEAR walk of the live timer heap under `__sched_lock`
(`GtRuntime.emitGtTimerStoreRemove`), and a `(gt, tag)` that is not on the heap walks to its END. A child
exit and a pipe-read readiness arm NO deadline, and between them they are the highest-frequency wakes the
runtime has — one per child and one per chunk boundary of everything a child writes. So the cancel side asks
exactly what the arm side asked: `emitNetpollDisarmDeadline` branches on the same `deadline != 0` word that
`__np_pd_commit`'s publish arm branches on before it adds an entry, and a cancel with nothing to find never
starts the walk.

The counter reports entries INSPECTED rather than cancels attempted, which is the whole point — a count of
CALLS reads the same whether a cancel walked one entry or a thousand. Eight sleepers hold eight entries on
the heap (`Scheduler.yield()` puts them all there before the bracket opens), then twenty `echo`s are drained
line by line inside it. Every readiness in that loop, and the child's exit, is a turned-away cancel over a
NON-EMPTY heap, so the guard is under load rather than under a heap that is trivially short. The twenty
lines are one length each (`line-a\n`, 7 bytes), so the 140 in the exit code cannot be reached by a short
read, and the sleepers are awaited afterwards — their own deadlines fire and are popped from the root,
which is not a scan.

⚠ **`cancelWalked=true` IS THE CONTROL, AND WITHOUT IT `scanned=0` WOULD BE A GATE THAT CANNOT FAIL.** A
counter that never moved at all would satisfy the first witness perfectly. So the same program then performs
the ONE cancel in it that really does have an entry to find: `dropAParkedSleeper` drops a promise parked on
a 2000 ms deadline, in `parked-timer-drop-cancel`'s exact shape — bound to a name, and `await`ing a `fast`
sibling first so the victim has run and armed its timer (the `gtIsComplete` peek adds `0`, which is that
thread saying it is still parked). That cancel walks a heap of nine, so the counter is live, and the `0`
above is a measurement rather than a silence.

⛔ **THIS CASE WAS WRITTEN GREEN AND IS A GUARD, NOT A REPRODUCTION.** The pre-guard number was never
observed and is not claimed here: seeing it would need a compiler carrying this counter and NOT the guard,
which is a build made to fail, and no such build was made. What it pins is the shape — with a guarded cancel
the delta is 0 for any heap length and any number of wakes, so it is a regression re-routing undeadlined
traffic back through the scan that this catches, at a cost of one heap length per wake.
```maxon
function sleeper() returns Integer
	sleep(2000)
	return 1
end 'sleeper'

function fast() returns Integer
	Scheduler.yield()
	return 42
end 'fast'

function dropAParkedSleeper() returns Integer
	let victim = async sleeper()
	let q = async fast()
	let r = await q
	return r + __Builtins.gtIsComplete(victim.inner)
end 'dropAParkedSleeper'

function main() returns ExitCode
	let s1 = async sleeper()
	let s2 = async sleeper()
	let s3 = async sleeper()
	let s4 = async sleeper()
	let s5 = async sleeper()
	let s6 = async sleeper()
	let s7 = async sleeper()
	let s8 = async sleeper()
	Scheduler.yield()

	let h = subpSpawn("for i in a b c d e f g h i j k l m n o p q r s t; do echo line-$i; done")
	let before = __Builtins.schedTimerScanStepCount()
	var total = 0
	var line = subpReadLine(h)
	while line.byteLength() > 0 'drain'
		total = total + line.byteLength()
		line = subpReadLine(h)
	end 'drain'
	let scanned = __Builtins.schedTimerScanStepCount() - before
	_ = subpWait(h)
	subpRelease(h)

	let beforeCancel = __Builtins.schedTimerScanStepCount()
	_ = dropAParkedSleeper()
	let walked = __Builtins.schedTimerScanStepCount() - beforeCancel

	_ = await s1
	_ = await s2
	_ = await s3
	_ = await s4
	_ = await s5
	_ = await s6
	_ = await s7
	_ = await s8

	print("scanned={scanned} cancelWalked={walked > 0}\n")
	return total as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
140
```
```stdout
scanned=0 cancelWalked=true
```

<!-- test: streaming-subprocess.eof-latched -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
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

<!-- test: streaming-subprocess.posix-read-to-eof-latched -->
<!-- unsupported-targets: x64-windows -->
⭐ **THE EOF EDGE, WHICH IS THE ONE MOST LIKELY TO HANG ON THIS LANE.** `poll` on a pipe whose writer has
closed reports `POLLIN|POLLHUP` and SUCCEEDS, so the peek says "readable", the read answers 0, and the
reader must latch EOF from that zero rather than from the poll. A reader that took the successful poll as
"there are bytes" and looped would spin here for ever, and one that never latched would re-issue a read on
a closed pipe on every call. After the single line, a second read returns EOF (an empty String, length 0)
and a third stays EOF.

The return is `line1 + 10 * eof1 + 10 * eof2`, so a non-empty "EOF" line of any length shows up as a
different number rather than as a near miss.
```maxon
function main() returns ExitCode
	let h = subpSpawn("echo hello")
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
6
```
```stdout
0

```

<!-- test: streaming-subprocess.spawned-reader -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
The read runs inside an `async` coroutine rather than in `main`, so its resume is a cross-thread ready of a
coroutine: the machine draining the poller readies the reader onto `main`'s strand queue under
`__sched_lock`, and the machine that takes the strand switches back into it.
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
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

<!-- test: streaming-subprocess.posix-write-echo -->
<!-- unsupported-targets: x64-windows -->
`write-echo`'s round trip on this lane: the parent WRITES two lines into the child's stdin, closes the
write end so the child sees EOF, and reads the sorted output back. `sort` orders `1` before `22`
lexicographically, so the first line back is the SHORTER one. The three clauses — both writes succeeded, a
non-empty first line came back, and it is shorter than the second — are the same line-terminator-invariant
facts the Windows sibling asserts, for the same reason: `1\n` / `22\n` here and `1\r\n` / `22\r\n`
there both satisfy `first < second`.

⚠ **`subpCloseStdin` IS LOAD-BEARING AND ITS ABSENCE IS A HANG, NOT A WRONG ANSWER.** `sort` cannot emit
its first byte until it has read EOF, and this lane's read blocks its M — so a close that closed the wrong
descriptor, or closed nothing, parks this program for ever rather than failing it.
```maxon
function main() returns ExitCode
	let h = subpSpawn("sort")
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
A streaming reader is DROPPED mid-read, then the SAME handle is re-read and must still work. `reader(h)` runs
`subpReadLine(h)` in an `async` GT; the child (`ping -n 3` then `echo hi`) delays ~2 s, so the reader parks on
the overlapped read with no data. `dropIt` sleeps 200 ms (the reader is parked) then returns, DROPPING the
un-awaited promise. Because the read pipe is TABLE-owned (not the GT's), the drop cancels the READ and must
NOT close the pipe — so the follow-up `subpReadLine(h)` on the same handle re-issues a fresh read and gets
`hi\r\n` (4 bytes), and `subpRelease` is the sole pipe-closer (no double-close). Before the ownership marker,
the drop closed the shared pipe and this returned 0 (EOF forever).
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
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
	return ((n2 as Integer) * 10 + rn) as ExitCode
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
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

<!-- test: streaming-subprocess.posix-spawn-release-loop -->
<!-- unsupported-targets: x64-windows -->
`spawn-release-loop` on this lane: twelve spawn+read+release cycles reuse the same table slot (and its line
buffer) each iteration, proving no descriptor leak and no per-iteration buffer leak across a many-iteration
loop. ⚠ It is a sharper check here than on Windows, because every cycle also allocates the
`/bin/sh -c <line>` argument vector and the two `posix_spawn_file_actions` this lane's spawn needs; twelve
of those going unreleased is what the leak gate (exit **101**) would catch. Each iteration reads `hello\n`
(6 bytes), so the accumulator returns 12x6 = 72.
```maxon
function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 12 'loop'
		let h = subpSpawn("echo hello")
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
72
```


<!-- test: streaming-subprocess.a-released-handle-never-reaches-the-slots-next-child -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⭐ **A HANDLE NAMES A CHILD, NOT A SLOT.** The `(slot, generation)` pair IS the handle from the spawn
on — the generation sits above the index in the integer `subpSpawn` returns — so a released handle
answers as a released handle for all time, and the slot's next occupant is unreachable through it. The
runtime's per-entry check ("in range, live, AND the same generation") is what makes a copied-and-released
handle safe; the per-copy `requireLive()` bool in the stdlib wrapper cannot be, because a second copy of
the handle never sees the first copy's release.

Child A (`echo first&exit 3`) is waited to completion — nothing is parked, so the parked reader's
generation re-read never enters — and released; child B (`echo second&exit 7`) then takes the freed
slot. The STALE `h1` is driven first: `subpWait(h1)` answers `-1` and `subpReadLine(h1)` an empty
string, exactly the dead-handle answers `subprocess-builtins.handle-guards-streaming` pins. Only then
does the fresh `h2` collect B's `second\r\n` (8 bytes) and its exit code 7 — read BEFORE the wait, as
`spawn-release-loop` does. Before this change the handle was the bare slot index, so the stale wait
reaped B and answered 7 and the stale read stole `second`, leaving the fresh handle with nothing.
```maxon
function main() returns ExitCode
	let h1 = subpSpawn("cmd /c echo first&exit 3")
	let firstLine = subpReadLine(h1)
	let first = subpWait(h1)
	subpRelease(h1)
	let h2 = subpSpawn("cmd /c echo second&exit 7")
	let stale = subpWait(h1)
	let staleLine = subpReadLine(h1).byteLength()
	let freshLine = subpReadLine(h2).byteLength()
	let fresh = subpWait(h2)
	subpRelease(h2)
	print("first={first} firstLine={firstLine.byteLength()} stale={stale} staleLine={staleLine} fresh={fresh} freshLine={freshLine}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
first=3 firstLine=7 stale=-1 staleLine=0 fresh=7 freshLine=8
```
```exitcode
0
```

<!-- test: streaming-subprocess.posix-a-released-handle-never-reaches-the-slots-next-child -->
<!-- unsupported-targets: x64-windows -->
`a-released-handle-never-reaches-the-slots-next-child` on this lane, where the command reaches
`/bin/sh -c` and the lines end in a bare LF (`first\n` = 6 bytes, `second\n` = 7). The handle is the
same packed `(slot, generation)` integer on every lane, so a released `h1` answers `-1` from the wait
and an empty string from the read no matter that child B now owns its slot, and B's line and exit code
7 reach only the fresh `h2`. Before this change the stale wait reaped B and answered 7 and the stale read
stole `second`.
```maxon
function main() returns ExitCode
	let h1 = subpSpawn("echo first; exit 3")
	let firstLine = subpReadLine(h1)
	let first = subpWait(h1)
	subpRelease(h1)
	let h2 = subpSpawn("echo second; exit 7")
	let stale = subpWait(h1)
	let staleLine = subpReadLine(h1).byteLength()
	let freshLine = subpReadLine(h2).byteLength()
	let fresh = subpWait(h2)
	subpRelease(h2)
	print("first={first} firstLine={firstLine.byteLength()} stale={stale} staleLine={staleLine} fresh={fresh} freshLine={freshLine}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
first=3 firstLine=6 stale=-1 staleLine=0 fresh=7 freshLine=7
```
```exitcode
0
```

<!-- test: streaming-subprocess.windows-a-parked-line-read-is-on-the-poller -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
<!-- procs: 1 -->
⭐ **A PIPE READ THAT WAITS MUST WAIT ON THE SCHEDULER'S POLLER, AND `schedNetpollBlockCount()` IS WHAT
SAYS SO.** A read parked anywhere else is a wait the scheduler cannot see: it does not count toward
`__np_waiters`, the deadlock census cannot reason about it, and a second completion port needs a thread of
its own to drain it. The counter is stepped by `__np_pd_commit` alone, so a non-zero delta across the read
means the green thread published itself on a poll descriptor and nothing else can produce it.

⚠ **THE WINDOW BRACKETS THE READ AND NOT THE SPAWN.** `subpSpawn` is `SyscallClass.blocking` and has
parks of its own that have nothing to do with this subject, so the reading opens after the child exists.
The child sleeps about a second before writing, which is what guarantees the read finds nothing ready and
has to wait — a read satisfied inline completes without ever reaching the poller, correctly, and would
witness nothing.
```maxon
function main() returns ExitCode
	let h = subpSpawn("cmd /c ping -n 3 127.0.0.1 >nul & echo hi")
	let before = __Builtins.schedNetpollBlockCount()
	let line = subpReadLine(h)
	let parks = __Builtins.schedNetpollBlockCount() - before
	let n = line.byteLength()
	_ = subpWait(h)
	subpRelease(h)
	print("n={n} polled={parks >= 1}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
n=4 polled=true
```
```exitcode
0
```
