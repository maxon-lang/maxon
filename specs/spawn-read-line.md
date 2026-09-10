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
thread created at scheduler init drains the port with `GetQueuedCompletionStatus` and readies the parked
reading thread through `__gt_ready_locked`, under `__sched_lock` — the lock every run-queue and strand-queue
access takes — then, when that ready published a strand token, pays the wake it owes
(`__sched_wake_or_spawn`) after releasing the lock. A publish-after-park handshake (the park's registration
sets a `parked` flag only AFTER committing `waiting`; the completion thread spins on that flag before
readying) closes the lost-wakeup / torn-status race a completion arriving before the park would otherwise
cause.

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
`main` reads a child's stdout. The read parks `main` until the completion thread readies it, then `main`
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
The read runs inside an `async` coroutine rather than in `main`, so the completion thread readies a member
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
