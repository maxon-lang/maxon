---
feature: spawn-read-line
status: experimental
keywords: [spawnReadLine, async, await, green-threads, scheduler, iocp, overlapped, pipe, subprocess, stdio, yield, concurrency]
category: concurrency
---

# spawnReadLine — the overlapped-read substrate (P1.5 dogfood slice 1a)

## Documentation

`spawnReadLine(cmd)` spawns the Windows child named by the command String with its **stdout redirected to a
fresh overlapped pipe**, issues a **yielding** overlapped `ReadFile` of that pipe, and returns the number of
bytes read once the read completes. It is the risky core of async-subprocess-stdio: the read PARKS the green
thread and is resumed — by the scheduler's own **poller**, which is what the completion is delivered to —
when the read finishes, so the M is free to run other green threads while the read is in flight.

```text
function main() returns ExitCode
	let n = spawnReadLine("cmd /c echo hello")
	return n as ExitCode
end 'main'
```

`spawnReadLine` is a **temporary probe surface** for the substrate — the full async-subprocess-stdio API and
its stdlib arrive in a later slice. It requires exactly one `String` argument (the command line; a non-String
is refused) and returns an `int` (the byte count), so its result is usable in value position. It is
**x64-windows only** (the whole overlapped-read substrate is x64-windows-gated at this rung).

Mechanically: an overlapped named pipe joins the scheduler's ONE poller, which on this lane is a completion
port. The read gets a poll source of its own — the source is the OPERATION rather than the handle, because a
completion key is fixed per handle and two reads may be in flight on one pipe — and the green thread parks on
it exactly as a socket reader parks on a descriptor. Whichever machine drains the poller recovers the reading
thread from the packet's `lpOverlapped`, stores the transfer and the status onto it, and readies it through
`__np_pd_wake`, under `__sched_lock`. A readiness that arrives before the thread has published itself is
recorded on the poll descriptor and taken by the wait without parking at all, which is what closes the
lost-wakeup race.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** Reading a line from a spawned child parks the calling green thread,
and the interleaving cases additionally reach `__gt_sleep` — neither of which wasm32-wasi lowers.

### ⭐ arm64-macOS RUNS THE PROBE, BUT NOT THE PART OF IT THAT PARKS

`TargetFacilities` answers `subprocess gives true` for arm64-macOS, so `spawnReadLine` compiles and
runs there; the command line reaches the child through `/bin/sh -c`. The two cases this lane can
express carry `posix-…` siblings marked `arm64-macos`, following `process-background-priority.md`'s
pattern — the programs could not be widened, because they spawn `cmd`.

⛔ **`interleave-with-sleep` AND `drop-in-flight` HAVE NO SIBLING, AND BOTH ARE ABOUT A READ THAT IS
IN FLIGHT.** This lane builds no completion port, no wake event and no drain thread at all
(`GtRuntime.IoCompletionShape`), and the read completes in its caller — so there is no overlapped
operation to run a sleeper against and none to cancel. MEASURED with `interleave-with-sleep`'s own
shape, recording completion ORDER rather than a sum: this lane answers **12** (the reader finishes
first) where Windows answers **21**. What the siblings below pin is that the probe reads a child's
bytes and answers the count, from `main` and from an `async` coroutine; the yielding half waits on a
kqueue this lane does not have.

## Tests

<!-- test: spawn-read-line.top-level -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
`main` reads a child's stdout. The read parks `main` until the poller readies it, then `main`
resumes and returns the byte count. `cmd /c echo hello` writes `hello\r\n` = 7 bytes.
```maxon
function main() returns ExitCode
	let n = spawnReadLine("cmd /c echo hello")
	return n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: spawn-read-line.posix-top-level -->
<!-- unsupported-targets: x64-windows -->
`top-level`'s subject on the POSIX lane. `main` reads a child's stdout through the one-shot probe and returns
the byte count; `echo hello` writes `hello\n` = SIX bytes, an LF where `cmd /c echo` writes CRLF.

⚠ **THE READ COMPLETES IN ITS CALLER HERE RATHER THAN AS A PACKET.** No read is ever in flight across a park
on this lane — `GtRuntime.IoCompletionShape` is why — so what this case pins is that the probe reads the
child's bytes and answers the count, not the per-operation poll source its Windows sibling additionally
exercises. The command
line reaches the child through `/bin/sh -c`, exactly as `__Builtins.runProcess`'s does.
```maxon
function main() returns ExitCode
	let n = spawnReadLine("echo hello")
	return n as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: spawn-read-line.spawned-reader -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
The read runs inside an `async` coroutine rather than in `main`, so the poller readies a member
that is not its strand's owner: `__gt_ready_locked` appends it to the back of `main`'s strand queue under
`__sched_lock` and — `main` being parked on its await, so no machine holds the strand — publishes the
strand's token, and the machine that pops it switches back into the reader. `top-level` takes the owner's
arm of the same door; this case takes the coroutine's.
```maxon
function reader() returns Integer
	return spawnReadLine("cmd /c echo hello")
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

<!-- test: spawn-read-line.posix-spawned-reader -->
<!-- unsupported-targets: x64-windows -->
The read runs inside an `async` coroutine rather than in `main`, so its result travels back through the
promise rather than through a plain return. `main` awaits it and returns the byte count.

⚠ On this lane the resume is NOT cross-thread — the read completes in the reading GT itself — so what this
case adds over `posix-top-level` is the coroutine path, not the cross-thread ready its Windows sibling
exercises. It is worth its own case for the reason `async-subprocess.posix-multi-concurrent` is: a probe
that only ever ran in `main` would not notice a reader that answered correctly there and clobbered a
coroutine's frame.
```maxon
function reader() returns Integer
	return spawnReadLine("echo hello")
end 'reader'

function main() returns ExitCode
	let r = async reader()
	let n = await r
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
6
```

<!-- test: spawn-read-line.posix-a-parked-line-read-yields-to-a-sleeper -->
<!-- unsupported-targets: x64-windows -->
<!-- procs: 1 -->
⭐ **THE READ YIELDS ON THIS LANE TOO.** A slow child (a one-second delay before its line) and a 50 ms
sleeper run at ONE processor. The pipe's read end is non-blocking and carries a poll descriptor, so the
reader PARKS and the sleeper's timer fires while the line is still unwritten. Each records its completion
order into a global (`order = order * 10 + tag`), so `21` says the sleeper (tag 2) finished first. A read
that holds the only machine inside a blocking `read(2)` answers `12` — which is what
`async-subprocess.posix-interleave-with-sleep`'s own prose records for the streaming reader on this lane,
and this case is that `12` turned into a `21`. The exit code IS the assertion.

⚠ **THE ORDER IS THE ONLY THING THIS SURFACE CAN PIN, BECAUSE `spawnReadLine` SPAWNS AND READS IN ONE
CALL.** A retake witness would say where the thread went rather than only that something else ran, but
there is nowhere to open a bracket around the read alone here, and a bracket around the whole program
counts the SPAWN: `osProcessSpawn` is `SyscallClass.blocking`, and `__sched_retake` spares a bracketed call
only while `(nmspinning + npidle) > 0`. At ONE processor the machine inside `clone`+`execve` holds the only
P, so that term is zero and the retake always fires — MEASURED as exactly one retake per spawn, and that
retake is CORRECT: it is what lets the sleeper run at all while a child is being spawned.
`streaming-subprocess.posix-a-parked-line-read-needs-no-rescue` is the case that pins the retake property,
because its two-call surface (`subpSpawn` then `subpReadLine`) CAN bracket the read by itself.
```maxon
var order = 0

function slow() returns Integer
	_ = spawnReadLine("sleep 1; echo hello")
	order = order * 10 + 1
	return 1
end 'slow'

function fast() returns Integer
	sleep(50)
	order = order * 10 + 2
	return 2
end 'fast'

function main() returns ExitCode
	let p1 = async slow()
	let p2 = async fast()
	_ = await p1
	_ = await p2
	return order as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
21
```

<!-- test: spawn-read-line.interleave-with-sleep -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
A concurrent `async sleep(50)` runs while the read is in flight, PROVING the read yields: if the read blocked
the single M, the sleeper could not run. Both a parked timer and an in-flight overlapped read are pending at
once, so the netpoll blocks on the wake event bounded by the timer delta. The read (7 bytes) plus the
sleeper's `1` sum to 8.
```maxon
function reader() returns Integer
	return spawnReadLine("cmd /c echo hello")
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

<!-- test: spawn-read-line.drop-in-flight -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
An `async` reader promise is DROPPED (un-awaited) while its overlapped read may still be in flight — the
KERNEL still holds the reader's OVERLAPPED, which is embedded in its green thread. Dropping it must NOT free
that thread out from under the kernel. The drop therefore leaves the park standing and cancels the read
(`CancelIoEx`): the cancellation's own packet readies the thread, which answers a failed read, unwinds its
frame, closes its handle and frees its buffer. Five in-flight drops, then one clean read (7 bytes) — a
recurrence of the bug crashes with 0xC0000005 instead of returning 7.
```maxon
function reader() returns Integer
	return spawnReadLine("cmd /c echo hello")
end 'reader'

function dropInFlight() returns Integer
	_ = async reader()
	sleep(1)
	return 0
end 'dropInFlight'

function main() returns ExitCode
	var i = 0
	while i < 5 'loop'
		_ = dropInFlight()
		i = i + 1
	end 'loop'
	let n = spawnReadLine("cmd /c echo hello")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```
