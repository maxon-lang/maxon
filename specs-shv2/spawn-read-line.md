---
feature: spawn-read-line
status: experimental
keywords: [spawnReadLine, async, await, green-threads, scheduler, iocp, overlapped, pipe, subprocess, stdio, yield, concurrency]
category: concurrency
---

# spawnReadLine — the IOCP overlapped-read substrate (P1.5 dogfood slice 1a)

## Documentation

`spawnReadLine(cmd)` spawns the Windows child named by the command String with its **stdout redirected to a
fresh overlapped pipe**, issues a **yielding** overlapped `ReadFile` of that pipe, and returns the number of
bytes read once the read completes. It is the risky core of async-subprocess-stdio: the read PARKS the green
thread and is resumed — by a dedicated **IOCP completion thread** — when the read finishes, so the M is free
to run other green threads while the read is in flight.

```text
function main() returns ExitCode
	let n = spawnReadLine("cmd /c echo hello")
	return n as ExitCode
end 'main'
```

`spawnReadLine` is a **temporary probe surface** for the substrate — the full async-subprocess-stdio API and
its stdlib arrive in a later slice. It requires exactly one `String` argument (the command line; a non-String
is refused) and returns an `int` (the byte count), so its result is usable in value position. It is
**x64-windows only** (the whole IOCP substrate is x64-windows-gated at this rung).

Mechanically: an overlapped named pipe is registered with a process-wide I/O completion port; a completion
thread created at scheduler init drains the port with `GetQueuedCompletionStatus` and re-readies the parked
reading thread — under a `CRITICAL_SECTION` that guards the run queue against the scheduler's own dequeue
(the first cross-thread run-queue mutation) — then signals an auto-reset event the netpoll waits on. A
publish-after-park handshake (the reading thread sets a `parked` flag only AFTER committing `waiting`; the
completion thread spins on that flag before re-readying) closes the lost-wakeup / torn-status race a
completion arriving before the park would otherwise cause.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** Reading a line from a spawned child parks the calling green thread on the driver,
and the interleaving cases additionally reach `__gt_sleep` — both x64-windows only at this rung.

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
bytes and answers the count, from GT0 and from a spawned green thread; the yielding half waits on a
kqueue this lane does not have.

## Tests

<!-- test: spawn-read-line.top-level -->
<!-- targets: x64-windows -->
The main thread (GT0) reads a child's stdout. The read yields (GT0 self-drives its own scheduler loop until
the completion thread signals the read is done), then GT0 resumes and returns the byte count. `cmd /c echo
hello` writes `hello\r\n` = 7 bytes.
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
<!-- targets: arm64-macos -->
`top-level`'s subject on the POSIX lane. GT0 reads a child's stdout through the one-shot probe and returns
the byte count; `echo hello` writes `hello\n` = SIX bytes, an LF where `cmd /c echo` writes CRLF.

⚠ **THE READ COMPLETES IN ITS CALLER HERE RATHER THAN ON A COMPLETION THREAD.** There is no IOCP and no
port to drain on this lane — `GtRuntime.IoCompletionShape` is why the port, the wake event and the drain
thread are not built at all — so what this case pins is that the probe reads the child's bytes and answers
the count, not the publish-after-park handshake its Windows sibling additionally exercises. The command
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
<!-- targets: x64-windows -->
The read runs inside a SPAWNED green thread (`stackBase != 0`), so its resume goes through the CROSS-THREAD
path: the completion thread re-enqueues the reading thread onto the run queue under the run-queue lock, and
the driver dequeues and switches back into it. Exercises the lock + cross-thread `__gt_enqueue`.
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
<!-- targets: arm64-macos -->
The read runs inside a SPAWNED green thread (`stackBase != 0`) rather than on GT0, so the reader's stack is
a scheduler-allocated one and its result travels back through the promise rather than through a plain
return. `main` awaits it and returns the byte count.

⚠ On this lane the resume is NOT cross-thread — the read completes in the reading GT itself — so what this
case adds over `posix-top-level` is the spawned-stack path, not the run-queue lock its Windows sibling
exercises. It is worth its own case for the reason `async-subprocess.posix-multi-concurrent` is: a probe
that only ever ran on GT0 would not notice a reader that answered correctly there and clobbered a spawned
GT's frame.
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

<!-- test: spawn-read-line.interleave-with-sleep -->
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
An `async` reader promise is DROPPED (un-awaited) while its overlapped read is still in flight — the IOCP
completion thread still holds the reader's OVERLAPPED. Dropping it must NOT free the green thread out from under
the completion thread (a cross-thread use-after-free). The drop path cancels the read (`CancelIoEx`), drains the
completion through the `ioParked` abandon/drain handshake so the completion thread is provably done with the GT,
closes the read handle, and only then frees. Five in-flight drops, then one clean read (7 bytes) — a recurrence
of the bug crashes with 0xC0000005 instead of returning 7.
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
