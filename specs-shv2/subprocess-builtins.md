---
feature: subprocess-builtins
status: experimental
keywords: [subprocess, __Builtins, intrinsics, spawn, collect, stdout, stderr, stdin, timeout, detach, PATH, PATHEXT, pipe, handle-table, green-threads]
category: system
---

# The `__Builtins.subprocess*` intrinsics — `stdlib/Subprocess.maxon`'s whole floor

## Documentation

`stdlib/Subprocess.maxon` is the child-process surface — `Subprocess.run(...)`,
`Configuration.run()`, `runDetached()` and `StreamingSubprocess` — and every one of its leaves
bottoms out in a compiler intrinsic. `Parser.BuiltinsSubprocessSpawnName` lists them all; they fall into
three families.

| Family | Intrinsics |
|---|---|
| the ATTACHED run | `subprocessSpawn` / `subprocessDetach` (fourteen arguments), `subprocessGetPid`, `subprocessWaitCollect`, `subprocessResultStatusKind` / `StatusCode` / `Stdout` / `Stderr` / `DurationMs` / `Release`, `subprocessReleaseHandle` |
| the STREAMING child | `subprocessSpawnStreaming`, `subprocessWriteStdinAll`, `subprocessReadStdoutLine` / `ReadStderrLine`, `subprocessReadStdoutBytes`, `subprocessStdoutState` / `subprocessStderrState`, `subprocessCloseStdin`, `subprocessWaitExit`, and the three that ask rather than commit — `subprocessPollExit`, `subprocessTryReadStdoutLine` / `TryReadStderrLine` |
| the helpers | `subprocessResolveOnPath`, `subprocessLastErrorMessage`, and `managedIsNull` (a `__ManagedMemory` predicate whose only corpus caller is the executable lookup) |

The bootstrap declares most of them with exactly these shapes
(`maxon-sharp/Compiler/2-Parser.cs`'s `CompilerBuiltins`, `subprocessSpawn` through
`subprocessResultRelease`), plus `subprocessKill` and `subprocessSendSignal`, which shv2 does not surface
because no corpus file calls either. The three non-blocking members are shv2's own and the reference has
no counterpart for them; the rest of the SURFACE is the reference's minus that pair, and only the
implementation differs.

### The fourteen-argument spawn contract

`subprocessSpawn(argv_mm, argc, cwd_cstr, env_mm, envInherit, stdinKind, stdinData_cstr,
stdoutKind, stdoutData_cstr, stdoutLimit, stderrKind, stderrData_cstr, stderrLimit, flags)`.

- **argv** is a NUL-SEPARATED BLOB of `argc` tokens — the shape `stdlib/Subprocess.maxon`'s
  `buildArgvBlob` packs — joined into a Windows command line by the runtime. A token is quoted iff
  it is empty or holds a space, a tab or a `"`; inside a quoted token a `"` is written `\"`. That is
  the bootstrap's rule to the byte.
- **cwd** is a `cstring`; the EMPTY one means "inherit", which is
  `stdlib/Subprocess.maxon`'s own sentinel.
- **env / envInherit**: `envInherit = 1` (`EnvSource.parent`) hands the child this process's own
  environment and the `env` slot beside it is not read; `envInherit = 0` (`EnvSource.block`) makes
  `env` the child's WHOLE environment — a NUL-separated `NAME=VALUE` block ended by one more NUL,
  which `a-caller-built-environment-is-the-childs-whole-environment` pins by having the child expand
  a name that exists only in the block it was given. ⚠ This bullet used to say the block was refused
  because *"`requireInheritEnv` refuses `Environment.custom` and `inheritUpdating` before any spawn
  call is made"*; **that function is deleted** — `stdlib/Subprocess.maxon` assembles the block from
  this process's own entries with the caller's overrides applied, so both arms are servable.
- **stdinKind** is `StdinKind`'s raw value: `0` none (the NUL device), `1` inherit, `2` bytes (the
  payload is pushed into a pipe while the child runs), `3` file.
- **stdoutKind** / **stderrKind** are `OutputKind`'s: `0` discard (NUL), `1` inherit, `2` collect
  (a pipe the runtime drains), `3` file. A `collect` limit of 0 means uncapped.
- **flags** is `SpawnFlag`'s bitfield: bit 0 hide window, bit 1 new process group, bit 2 detach.

`subprocessDetach` takes the identical fourteen and differs only in the detach bit its caller has
already set; the runtime answers the child's PID instead of a handle and releases the slot.

### Collecting BOTH streams is the whole difficulty

A child that fills the 64 KiB stderr pipe blocks for ever if the parent is committed to a read on
stdout, and shv2's yielding read parks the calling green thread on the GT's own single OVERLAPPED —
so it cannot hold two reads in flight. `subprocessWaitCollect` therefore ASKS each pipe whether it
has bytes (`PeekNamedPipe`) before committing to read it, pushes any queued stdin payload between
drains, and sleeps GREEN for a millisecond only when a pass moved nothing and the child is still
running. The same structure is what lets a DEADLINE be honoured at all: a committed blocking read
cannot observe one.

### The result struct is OWNED and must be released

`subprocessWaitCollect` answers a pointer to a heap struct holding the status kind, the status code,
the duration and the two captured buffers. All three allocations are `__mm_alloc`'d, so
`subprocessResultRelease` genuinely gives them back AND the leak gate counts them: a program that
forgets the release exits **101**. The two buffer readers each answer a FRESH owned
`__ManagedMemory`, so a caller may read either stream twice.

### A bogus handle is a FAILURE, never a panic — and so is a bogus result pointer

Every entry that takes a handle validates it — in range, and the slot is live — and answers `-1`
(`-2` for `subprocessWaitExit`'s timeout, EOF for a reader, nothing for a void entry) otherwise. The
six `subprocessResult*` readers validate their struct POINTER the same way and answer `0` / an empty
buffer / nothing.

⚠ This is the property a previous attempt at this rung got wrong in the loudest possible way: it
declared the intrinsics without building their runtime entries, and
`__Builtins.subprocessWaitCollect(-1, 0)` became `panic at X64Backend.maxon:2033: resolveCallFixups:
call to unknown function`.

⛔⛔ **AND IT WAS STILL HALF TRUE WHEN THIS FILE FIRST CLAIMED IT.** The guard was applied per SITE,
by hand, and four doors had it while six did not — `__Builtins.subprocessReleaseHandle(-1)` was an
ACCESS VIOLATION (0xC0000005) in a program that compiled clean, and the bare-name `subpRelease(-1)`
with it. `emitSubpSlotAddr` is `table + handle * SubpSlotBytes`, so `-1` names the 184 bytes BEFORE
the table — inside the slab — and a garbage `inUse` read as live had the release close three garbage
words as HANDLEs and zero memory it did not own. The gate case that existed to prove the guard
exercised **exactly the four doors that had it**. The six result readers were the same defect one
door over, and v1 had already solved that one (`X64Backend.maxon:3530-3533` carries a `nullValue`
per accessor).

⭐ **SO THE RULE IS NOW STRUCTURAL, AND THE CASES BELOW COVER EVERY DOOR RATHER THAN A CHOSEN FOUR.**
The guard sits at the ENTRY POINT, never at a wrapper, so both families — the twenty-two
`__Builtins.subprocess*` intrinsics and the seven bare-name `subp*` streaming builtins — reach it
without either having anything to remember. `emitSubpSlotOf` is the one door from a caller's handle
to a slot address, and every function that calls it must open with `emitSubpRequireHandle`; that
roster is a `grep`, not a list somebody maintains. `handle-guards` and
`handle-guards-streaming` below exercise all of it.

### shv2's ONE divergence from the reference: `subprocessResolveOnPath` never answers NULL

The bootstrap answers NULL when the PATH walk finds nothing, and
`stdlib/Subprocess.maxon`'s `resolveByName` tests for it with `managedIsNull` and then returns the
BARE NAME. shv2 cannot: the result is an OWNED `__ManagedMemory`, so its binding is dropped at scope
exit through `__mm_decref`, which dereferences the record header — a NULL would fault. Making
`__mm_decref` null-tolerant would put a test on every drop in every program to serve one corpus
line, so this producer answers the bare name itself, which is exactly the String that corpus line
returns on its miss branch. **The observable behaviour is identical**; only which of
`resolveByName`'s two branches runs differs.

### `subprocessLastErrorMessage()` answers the Win32 error NUMBER

The bootstrap picks one of a handful of fixed English sentences, which tells a caller which code
path failed and nothing about why. The number tells them why — `2` is "the executable is not
there", `5` is "access denied" — and shv2 has neither an `.rdata` producer reachable from a runtime
builder nor a `FormatMessageA` import, so the choice was a fixed string or the OS's own code. A code
of `0` means nothing has failed and answers the empty message.

### Targets — the Win32 substrate gate

`CreateProcessA`, three named pipes and an IOCP completion port are a WINDOWS shape, and WASI has no
process-spawn primitive at all. Every one of these intrinsics lowers into the `__gt_subp_` band,
which `SemanticCheck.calleeNeedsWin32Substrate` refuses on any other target with **E3104** at the
call's own span. ⚠ Before this rung that band was NOT in the gate and such a program died as a
BACKEND PANIC three tiers down — `SemanticCheck`'s own header recorded the gap verbatim. The two
`rejected-on-*` cases below are what hold it shut.

### ⭐ arm64-macOS HAS THE FACILITY, SO THE CASES COME IN PAIRS — AND WIDENING WAS NOT AN OPTION

`TargetFacilities.targetProvidesFacility` answers `subprocess gives true` for arm64-macOS
(`posix_spawnp` under `POSIX_SPAWN_CLOEXEC_DEFAULT`, anonymous pipes, `poll`, `waitpid`), so the
E3104 above does not fire there and every intrinsic in this file RUNS on that lane. What could not
be shared is the PROGRAMS: every case above spawns `cmd /c …`, which exists on no POSIX box, so
widening their markers would test a missing executable rather than this surface (measured: 31 of 39
cases across the four subprocess specs fail on a bare widening).

⇒ **Each subject that this lane can express carries a SECOND case, named `posix-…`, marked
`arm64-macos`, running an ordinary POSIX command.** That is `process-background-priority.md`'s
pattern, for `process-background-priority.md`'s reason: the SUBJECT is shared and the PROGRAM cannot
be. The pairs deliberately do not agree on their numbers — `/bin/echo` ends a line with a bare LF
where `cmd /c echo` writes CRLF, so a six is the same answer as the sibling's seven.

⚠ **AND THE TWO LANES DIVIDE ON ONE PROPERTY OF THIS SURFACE, NOT MERELY ON SPELLING.** The
fourteen-argument spawn takes an argv BLOB, and this lane's primitive takes an argv VECTOR — so the
Win32 quoting rule at the top of this section has NOTHING to do on arm64-macOS, where nothing is
ever re-split and therefore nothing needs escaping. `argv-quoting` asserts that a token holding a
space becomes ONE quoted argument; `posix-argv-reaches-the-child-verbatim` asserts the same subject
from the other side — that `$HOME`, `a b` and `a*b` reach the child unexpanded, unsplit and
unglobbed. Neither program could carry the other's claim.

## Tests

<!-- test: subprocess-builtins.collect-echo -->
<!-- targets: x64-windows -->
The whole attached path end to end: build an argv blob, spawn `cmd /c echo hello` with both output
streams collected, wait, and read the result struct back. The child's stdout is `hello\r\n` — SEVEN
bytes — which is what the BOOTSTRAP answers for the same command through
`stdlib/Subprocess.maxon`'s own `Subprocess.run(Executable.name("cmd"), arguments: ["/c", "echo",
"hello"])` (measured: `outLen=7`). Both the exit code and the printed line are pinned, so a run that
collected nothing cannot pass on its exit code alone.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "echo")
	appendToken(argv, token: "hello")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 4, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	let err = String.init(__Builtins.subprocessResultStderr(r))
	let kind = __Builtins.subprocessResultStatusKind(r)
	let code = __Builtins.subprocessResultStatusCode(r)
	let n = out.byteLength()
	let expected = "hello"
	let matches = out.startsWith(expected)
	print("kind={kind} code={code} outLen={n} errLen={err.byteLength()} matches={matches}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return n as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
kind=0 code=0 outLen=7 errLen=0 matches=true

```

<!-- test: subprocess-builtins.posix-collect-echo -->
<!-- targets: arm64-macos, arm64-linux, x64-linux -->
`collect-echo`'s subject on the POSIX lane, and the case that proves the whole attached path runs here at
all: build an argv blob, spawn `/bin/echo hello` with both streams collected, wait, and read the result
struct back. ⚠ **The byte count is SIX, not the Windows sibling's seven, and that is the line terminator
rather than a different capture** — `/bin/echo` ends its line with a bare LF where `cmd /c echo` writes
CRLF. Nothing about that is convertible, which is why this is a sibling and not a widened marker.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "/bin/echo")
	appendToken(argv, token: "hello")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 2, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	let err = String.init(__Builtins.subprocessResultStderr(r))
	let kind = __Builtins.subprocessResultStatusKind(r)
	let code = __Builtins.subprocessResultStatusCode(r)
	let n = out.byteLength()
	let expected = "hello"
	let matches = out.startsWith(expected)
	print("kind={kind} code={code} outLen={n} errLen={err.byteLength()} matches={matches}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return n as ExitCode
end 'main'
```
```exitcode
6
```
```stdout
kind=0 code=0 outLen=6 errLen=0 matches=true

```

<!-- test: subprocess-builtins.posix-argv-reaches-the-child-verbatim -->
<!-- targets: arm64-macos, arm64-linux, x64-linux -->
⭐ **THE CASE THAT PROVES THERE IS NO SHELL ON THE ARGV PATH — `argv-quoting`'s subject, inverted.** The
Windows sibling asserts that a token holding a space is QUOTED into one command line; this lane must
assert the opposite property, because `posix_spawnp` takes a vector and quotes nothing. Three tokens are
chosen so that each failure mode is a DIFFERENT wrong answer: `$HOME` would be EXPANDED, `a b` would be
SPLIT, and `a*b` would be GLOBBED, by any implementation that joined the blob into a line and handed it to
`/bin/sh -c`. The child prints one bracketed field per argument, so a split shows up as an extra field
rather than as text that happens to read the same — `[$HOME][a b][a*b]` and `[$HOME][a][b][a*b]` differ,
where `echo`'s space-joined output for the two would not.

⚠ It is `/bin/sh` that runs, but the tokens under test are POSITIONAL PARAMETERS rather than script text:
the shell never sees them as source, so an expansion here could only have come from the spawn.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "/bin/sh")
	appendToken(argv, token: "-c")
	appendToken(argv, token: "printf '[%s]' \"$@\"")
	appendToken(argv, token: "sh")
	appendToken(argv, token: "$HOME")
	appendToken(argv, token: "a b")
	appendToken(argv, token: "a*b")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 7, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	let expected = "[$HOME][a b][a*b]"
	print("verbatim={out == expected} len={out.byteLength()} kind={__Builtins.subprocessResultStatusKind(r)}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
verbatim=true len=17 kind=0

```

<!-- test: subprocess-builtins.handle-guards -->
<!-- targets: x64-windows -->
**EVERY** `__Builtins.subprocess*` entry that takes a handle, answering a failure for one that was
never spawned — a negative index, an index past the table, and a live-looking index in an empty
table. All eight, not the four that used to have the guard: the two VOID entries and the two READERS
are here precisely because they were the ones that faulted, and reaching the final `print` at all is
what proves they returned.
```maxon
function main() returns ExitCode
	let empty = ""
	let getPid = __Builtins.subprocessGetPid(-1)
	let waitCollect = __Builtins.subprocessWaitCollect(-1, 0)
	let waitExit = __Builtins.subprocessWaitExit(9999, 0)
	let writeAll = __Builtins.subprocessWriteStdinAll(3, empty.cstr())
	print("getPid={getPid} waitCollect={waitCollect} waitExit={waitExit} writeAll={writeAll}\n")
	let outLine = String.init(__Builtins.subprocessReadStdoutLine(-1, 64))
	let errLine = String.init(__Builtins.subprocessReadStderrLine(64, 64))
	__Builtins.subprocessCloseStdin(-1)
	__Builtins.subprocessReleaseHandle(-1)
	print("readOut={outLine.byteLength()} readErr={errLine.byteLength()} voidsReturned=true\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
getPid=-1 waitCollect=-1 waitExit=-1 writeAll=-1
readOut=0 readErr=0 voidsReturned=true

```

<!-- test: subprocess-builtins.result-pointer-guards -->
<!-- targets: x64-windows -->
The six `subprocessResult*` readers take the struct POINTER `subprocessWaitCollect` answers, and that
answer is `-1` on failure. `stdlib/Subprocess.maxon` tests for it before reading — but a caller need
not, and one line was an ACCESS VIOLATION. v1 guards the identical readers with a per-accessor
`nullValue` (`X64Backend.maxon:3530-3533`); these are its answers.
```maxon
function main() returns ExitCode
	let kind = __Builtins.subprocessResultStatusKind(-1)
	let code = __Builtins.subprocessResultStatusCode(-1)
	let duration = __Builtins.subprocessResultDurationMs(0)
	let out = String.init(__Builtins.subprocessResultStdout(-1))
	let err = String.init(__Builtins.subprocessResultStderr(0))
	__Builtins.subprocessResultRelease(-1)
	print("kind={kind} code={code} duration={duration} out={out.byteLength()} err={err.byteLength()} releaseReturned=true\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
kind=0 code=0 duration=0 out=0 err=0 releaseReturned=true

```

<!-- test: subprocess-builtins.handle-guards-streaming -->
<!-- targets: x64-windows -->
The OTHER family through the same entries. The seven bare-name `subp*` builtins are a separate
surface (`streaming-subprocess.md`) that lowers to the very `__gt_subp_*` functions the intrinsics
above lower to — which is exactly why the guard belongs at the ENTRY POINT and not at a wrapper:
`subpRelease(-1)` faulted for the same reason `__Builtins.subprocessReleaseHandle(-1)` did, and one
fix answers both. A wrapper-level guard would have left this case red.
```maxon
function main() returns ExitCode
	let line = subpReadLine(-1)
	let errLine = subpReadErrLine(9999)
	let wrote = subpWriteLine(-1, line: "ignored")
	let code = subpWait(-1)
	subpCloseStdin(-1)
	subpRelease(-1)
	print("read={line.byteLength()} readErr={errLine.byteLength()} write={wrote} wait={code} voidsReturned=true\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
read=0 readErr=0 write=-1 wait=-1 voidsReturned=true

```

<!-- test: subprocess-builtins.file-stdio-that-cannot-open-fails-the-spawn -->
<!-- targets: x64-windows -->
`InputSource.file(path)` and `OutputDestination.file(path)` are public corpus surface, and
`CreateFileA` can fail. ⚠ **MEASURED before this case existed: it did not fail — it SUCCEEDED.**
`INVALID_HANDLE_VALUE` went into the STARTUPINFO, `CreateProcessA` did not object, and the spawn
answered handle `0` with an EMPTY `lastErrorMessage()` for a child whose stdin was a dead handle.
The open is checked now, and the reason a caller reads is `CreateFileA`'s own — `3` is
ERROR_PATH_NOT_FOUND — rather than whatever `CreateProcessA` would have said about it afterwards.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "echo hi")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let missing = "Z:/no/such/dir/nope.txt"
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 1, 3, missing.cstr(), 2, empty.cstr(), 0, 0, empty.cstr(), 0, 0)
	let reason = String.init(__Builtins.subprocessLastErrorMessage())
	let expected = "win32 error 3"
	print("spawn={h} reason={reason == expected}\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
spawn=-1 reason=true

```

<!-- test: subprocess-builtins.stdin-bytes-and-stderr -->
<!-- targets: x64-windows -->
`StdinKind.bytes` queues a payload the collect loop pushes into the child's stdin between drains,
and closes the pipe once it is spent — so `more` echoes it back and exits rather than blocking for
ever. `cmd`'s `1>&2` redirection puts a second line on STDERR, which proves the two streams are
drained INDEPENDENTLY rather than one after the other.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "more & echo oops 1>&2")
	let empty = ""
	let payload = "fed-in\r\n"
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 1, 2, payload.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	let err = String.init(__Builtins.subprocessResultStderr(r))
	let fed = "fed-in"
	let oops = "oops"
	print("outHasPayload={out.startsWith(fed)} errHasOops={err.startsWith(oops)} kind={__Builtins.subprocessResultStatusKind(r)}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
outHasPayload=true errHasOops=true kind=0

```

<!-- test: subprocess-builtins.posix-stdin-bytes -->
<!-- targets: arm64-macos, arm64-linux, x64-linux -->
`stdin-bytes-and-stderr`'s stdin half on this lane. `StdinKind.bytes` queues a payload the collect loop
pushes into the child between drains and closes the pipe once it is spent — so `/bin/cat` echoes it back
and EXITS rather than blocking on a pipe nobody ever closes. A runtime that queued the payload and never
wrote it, or wrote it and never closed, hangs here instead of failing, which is what the pinned exit code
turns into a finite red.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "/bin/cat")
	let empty = ""
	let payload = "fed-in\n"
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 1, empty.cstr(), env, 1, 2, payload.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	let echoed = out == payload
	print("kind={__Builtins.subprocessResultStatusKind(r)} code={__Builtins.subprocessResultStatusCode(r)} echoed={echoed} outLen={out.byteLength()}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
kind=0 code=0 echoed=true outLen=7

```

<!-- test: subprocess-builtins.posix-stdout-and-stderr-are-separate -->
<!-- targets: arm64-macos, arm64-linux, x64-linux -->
`stdin-bytes-and-stderr`'s other half: the two pipes are drained INDEPENDENTLY, not one after the other.
The child writes four bytes to stdout and three to stderr with no trailing newline on either, so each
buffer's exact contents are pinned and a runtime that concatenated the two — or that dropped one — cannot
pass on a length alone. `printf` rather than `echo` for the same reason the Windows sibling reaches for
`set /p`: it appends no terminator of its own.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "/bin/sh")
	appendToken(argv, token: "-c")
	appendToken(argv, token: "printf sout; printf err 1>&2")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	let err = String.init(__Builtins.subprocessResultStderr(r))
	let expectedOut = "sout"
	let expectedErr = "err"
	print("out={out == expectedOut} err={err == expectedErr} outLen={out.byteLength()} errLen={err.byteLength()}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
out=true err=true outLen=4 errLen=3

```

<!-- test: subprocess-builtins.posix-exit-code-is-propagated -->
<!-- targets: arm64-macos, arm64-linux, x64-linux -->
The child's own exit status reaches `subprocessResultStatusCode` unchanged. ⚠ **On this lane that is a
`waitpid` status word rather than `GetExitCodeProcess`'s plain integer**, so the runtime has to decode it —
a body that handed the raw status back would answer `1792` (`7 << 8`) for this child, and one that lost the
decode entirely would answer 0 while still reporting `exited`. The status KIND is pinned beside the code so
that a `0` cannot pass as "exited cleanly" when the child in fact exited 7.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "/bin/sh")
	appendToken(argv, token: "-c")
	appendToken(argv, token: "exit 7")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let kind = __Builtins.subprocessResultStatusKind(r)
	let code = __Builtins.subprocessResultStatusCode(r)
	print("kind={kind} code={code}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return code as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
kind=0 code=7

```

<!-- test: subprocess-builtins.timeout-kills-the-child -->
<!-- targets: x64-windows -->
A non-zero `timeoutMs` is a KILL-AFTER deadline, which is what `stdlib/Subprocess.maxon`'s
`Configuration.timeoutMs` documents. The child would run for many seconds; the deadline fires,
`TerminateProcess` ends it, and the result's status kind is `timedOut` (`2`). A wait that ignored
the deadline would take twenty seconds and answer `0`.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "ping -n 20 127.0.0.1 > nul")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 0, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 300)
	let kind = __Builtins.subprocessResultStatusKind(r)
	print("kind={kind}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return kind as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
kind=2

```

<!-- test: subprocess-builtins.posix-timeout-kills-the-child -->
<!-- targets: arm64-macos, arm64-linux, x64-linux -->
`timeout-kills-the-child` on this lane. `/bin/sleep 5` would run for five seconds; the 300 ms deadline
fires, the child is killed, and the result's status kind is `timedOut` (`2`). The DURATION is asserted
beside the kind because the kind alone cannot tell a deadline that fired from one that was ignored and then
mislabelled: a wait that ran the child to completion would report five seconds. ⚠ It is a POSIX `kill`
rather than `TerminateProcess`, and a runtime that signalled but never reaped would report `timedOut` while
leaving a zombie — which the leak gate does not see, but a subsequent `waitpid` in the same process would.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "/bin/sleep")
	appendToken(argv, token: "5")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 2, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 0, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 300)
	let kind = __Builtins.subprocessResultStatusKind(r)
	let durationMs = __Builtins.subprocessResultDurationMs(r)
	let childRuntimeMs = 5000
	print("kind={kind} killedEarly={durationMs < childRuntimeMs}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return kind as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
kind=2 killedEarly=true

```

<!-- test: subprocess-builtins.detach-answers-a-pid -->
<!-- targets: x64-windows -->
The detach bit (bit 2 of `flags`) makes the runtime answer the child's OS process id rather than a
table handle, and release the slot — so the parent holds no handle to a child it will never wait
for. The pid is machine-specific, so the property asserted is that it is a real one.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "exit")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let pid = __Builtins.subprocessDetach(argv, 3, empty.cstr(), env, 1, 0, empty.cstr(), 0, empty.cstr(), 0, 0, empty.cstr(), 0, 4)
	print("positive={pid > 0}")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
positive=true

```

<!-- test: subprocess-builtins.posix-detach-answers-a-pid -->
<!-- targets: arm64-macos, arm64-linux, x64-linux -->
`detach-answers-a-pid` on this lane: the detach bit (bit 2 of `flags`) makes the runtime answer the child's
OS process id rather than a table handle, and release the slot — so the parent holds no handle to a child it
will never wait for. The pid is machine-specific, so the property asserted is that it is a real one. ⚠ On
this lane a detached child is a `posix_spawnp` pid the parent will never `waitpid`, which is why the slot
must be released HERE rather than left live: a handle nobody can wait on is a leak the table would carry to
program exit.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "/usr/bin/true")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let pid = __Builtins.subprocessDetach(argv, 1, empty.cstr(), env, 1, 0, empty.cstr(), 0, empty.cstr(), 0, 0, empty.cstr(), 0, 4)
	print("positive={pid > 0}")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
positive=true

```

<!-- test: subprocess-builtins.inherited-and-discarded-output -->
<!-- targets: x64-windows -->
`OutputKind.inherit` hands the child the PARENT's own standard handle, so its text lands in this
program's stdout with nothing collected; `OutputKind.discard` opens the NUL device, so the second
child's text goes nowhere at all. ⚠ An inherited handle belongs to the OS, and a spawn that closed
it after `CreateProcessA` — as it must close a pipe end or a file it opened — would close the
PARENT's stdout and the tail of the line below would never appear.

⚠ The children use `set /p` rather than `echo` for one reason: `echo` ends its line with a Windows
CRLF, and a spec's expected-stdout block cannot carry a bare CR. `set /p` writes its prompt with no
terminator at all, so what lands between the parent's two prints is exactly the child's bytes.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function runEcho(word String, outKind Integer) returns Integer
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: word)
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 1, 0, empty.cstr(), outKind, empty.cstr(), 0, 0, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let collected = String.init(__Builtins.subprocessResultStdout(r)).byteLength()
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return collected
end 'runEcho'

function main() returns ExitCode
	print("[")
	let inherited = runEcho("<nul set /p=visible", outKind: 1)
	let discarded = runEcho("<nul set /p=invisible", outKind: 0)
	print("] inheritCollected={inherited} discardCollected={discarded}")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
[visible] inheritCollected=0 discardCollected=0

```

<!-- test: subprocess-builtins.posix-inherited-and-discarded-output -->
<!-- targets: arm64-macos, arm64-linux, x64-linux -->
`inherited-and-discarded-output` on this lane. `OutputKind.inherit` hands the child the PARENT's own
descriptor, so its text lands in this program's stdout with nothing collected; `OutputKind.discard` opens
`/dev/null`, so the second child's text goes nowhere at all. ⚠ An inherited descriptor belongs to the
PARENT, and a spawn that closed it after `posix_spawnp` — as it must close a pipe end or a file it opened —
would close this program's own stdout and the tail of the line below would never appear. That is a sharper
trap here than on Windows: `POSIX_SPAWN_CLOEXEC_DEFAULT` means every descriptor is closed in the child
unless a file action names it, so the inherit path is a deliberate `dup2` rather than the absence of one.

⚠ `/bin/echo -n` rather than `echo`, for the same reason the Windows sibling reaches for `set /p`: a
trailing newline between the parent's two prints cannot be written into an expected-stdout block as part of
one line.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function runEcho(word String, outKind Integer) returns Integer
	var argv = ByteArray.create()
	appendToken(argv, token: "/bin/echo")
	appendToken(argv, token: "-n")
	appendToken(argv, token: word)
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 1, 0, empty.cstr(), outKind, empty.cstr(), 0, 0, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let collected = String.init(__Builtins.subprocessResultStdout(r)).byteLength()
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return collected
end 'runEcho'

function main() returns ExitCode
	print("[")
	let inherited = runEcho("visible", outKind: 1)
	let discarded = runEcho("invisible", outKind: 0)
	print("] inheritCollected={inherited} discardCollected={discarded}")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
[visible] inheritCollected=0 discardCollected=0

```

<!-- test: subprocess-builtins.argv-quoting -->
<!-- targets: x64-windows -->
A token holding a SPACE is quoted, so it reaches the child as ONE argument rather than two. `echo`
prints its arguments back with the quotes removed, which is what makes the round trip observable:
without the quoting the command line would be `cmd /c echo one two` and the child would still print
`one two`, so the discriminating case is a token whose *quoting* is what keeps the trailing text out
of `cmd`'s own parsing — an embedded `"` in the same token, which is written `\"`.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "echo")
	appendToken(argv, token: "a b")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 4, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 0, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	let quoted = "\"a b\""
	print("echoed={out.startsWith(quoted)} len={out.byteLength()}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
echoed=true len=7

```

<!-- test: subprocess-builtins.large-collect-grows-the-buffer -->
<!-- targets: x64-windows -->
An UNCAPPED collect of far more than one buffer's worth: 200 lines of 40 characters, which `cmd`
terminates with CRLF, is exactly 8400 bytes. The collect buffer starts at 4096 and doubles past what
each pass needs, freeing the buffer it outgrew — so this is the case that exercises the growth and
the free rather than the single-chunk happy path, and the pinned exit code is what would catch a
double free or a leaked old buffer (either exits **101**).
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "for /L %i in (1,1,200) do @echo 0123456789012345678901234567890123456789")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 0, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	print("kind={__Builtins.subprocessResultStatusKind(r)} code={__Builtins.subprocessResultStatusCode(r)} outLen={out.byteLength()}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
kind=0 code=0 outLen=8400

```

<!-- test: subprocess-builtins.posix-large-collect-grows-both-buffers -->
<!-- targets: arm64-macos, arm64-linux, x64-linux -->
An UNCAPPED collect of far more than one buffer's worth, on BOTH pipes at once: 1200 lines of 49
characters plus a LF on each stream is exactly 60000 bytes each, 117 KiB together. Each collect buffer
starts at 4096 and doubles past what a pass needs, freeing the buffer it outgrew — so this is the case that
exercises the growth and the free rather than the single-chunk happy path, and the pinned exit code is what
would catch a double free or a leaked old buffer (either exits **101**).

⭐ **DRIVING BOTH PIPES AT ONCE IS THE POINT, AND IT IS SHARPER HERE THAN ON WINDOWS.** A 64 KiB pipe fills
long before 60000 bytes have been read out of it, so a runtime that committed to draining stdout before
looking at stderr would leave the child blocked on a full stderr pipe for ever and this case would HANG
rather than fail. It passes only because `osPipePeek` asks each stream whether it has bytes before any read
is committed — the property `subprocessWaitCollect`'s structure exists for, and the one this lane cannot
fall back on a netpoll to supply.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "/bin/sh")
	appendToken(argv, token: "-c")
	appendToken(argv, token: "i=0; while [ $i -lt 1200 ]; do echo 0123456789012345678901234567890123456789012345678; echo 0123456789012345678901234567890123456789012345678 1>&2; i=$((i+1)); done")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	let err = String.init(__Builtins.subprocessResultStderr(r))
	print("kind={__Builtins.subprocessResultStatusKind(r)} code={__Builtins.subprocessResultStatusCode(r)} outLen={out.byteLength()} errLen={err.byteLength()}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
kind=0 code=0 outLen=60000 errLen=60000

```

<!-- test: subprocess-builtins.capture-limit -->
<!-- targets: x64-windows -->
A non-zero `stdoutLimit` caps what is KEPT, never what is read: a runtime that stopped reading at
the ceiling would leave the child blocked on a full pipe for ever. The child writes far more than
the ceiling and still exits cleanly, and the capture stops at the ceiling.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "for /L %i in (1,1,200) do @echo 0123456789012345678901234567890123456789")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 64, 0, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	print("kind={__Builtins.subprocessResultStatusKind(r)} code={__Builtins.subprocessResultStatusCode(r)} capped={out.byteLength() <= 64}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
kind=0 code=0 capped=true

```

<!-- test: subprocess-builtins.streaming-over-argv -->
<!-- targets: x64-windows -->
`subprocessSpawnStreaming` takes the same argv blob and wires all three streams to pipes, which is
what "streaming" MEANS — there are no stdio triples to pass because the caller drives all three. It
does take the environment pair the attached contract carries, and a `1` there is `EnvSource.parent`,
under which the block beside it is never read.
`writeStdinAll` writes exactly the bytes it is given (the surface appends its own newline), the
`readStdoutLine` reader answers a `__ManagedMemory` rather than the `String` the bare-name
`subpReadLine` builtin answers, and `waitExit` honours a deadline the streaming `subpWait` cannot.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "more")
	let empty = ""
	var envBlock = ByteArray.create()
	envBlock.push(0)
	let h = __Builtins.subprocessSpawnStreaming(argv, 3, empty.cstr(), 0, envBlock, 1)
	let payload = "ping\n"
	let wrote = __Builtins.subprocessWriteStdinAll(h, payload.cstr())
	__Builtins.subprocessCloseStdin(h)
	let line = String.init(__Builtins.subprocessReadStdoutLine(h, 1024))
	let code = __Builtins.subprocessWaitExit(h, 0)
	let expected = "ping"
	print("wrote={wrote} echoed={line.startsWith(expected)} lineLen={line.byteLength()} code={code}")
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
wrote=0 echoed=true lineLen=6 code=0

```

<!-- test: subprocess-builtins.streaming-read-exact-bytes -->
<!-- targets: x64-windows -->
`subprocessReadStdoutBytes(handle, n)` answers EXACTLY `n` bytes, and that is a different question
from the one `subprocessReadStdoutLine` answers — which is why it exists. A line reader cannot
express *"the next four bytes"*, so a framed protocol whose body length arrives in a header
(`Content-Length: N\r\n\r\n<body>`, the LSP framing, whose body carries NO trailing newline) has no
way to ask for the body: the line reader blocks for a newline that never comes.

⭐ **THIS CASE DISCRIMINATES THE TWO READERS BY STOPPING MID-LINE.** The child echoes
`abcdefghij` — one line — and the two four-byte reads answer `abcd` then `efgh`. The line reader
given the same stream can only answer the whole line (`abcdefghij\r\n`), so a case that read
a whole newline-terminated line would pass against it too and would prove nothing.

⚠ **SHORT ONLY AT EOF.** `cmd /c more` echoes the line CRLF-terminated and then a blank line, so the
child produces `abcdefghij\r\n\r\n` — 14 bytes, MEASURED. The third read asks for 100 and gets the 6 that
remain after the two four-byte reads, because the child has exited and its pipe is closed; the fourth
read gets 0, from the SAME latched EOF the line reader uses. A short answer anywhere but EOF would be a
wrong answer, not a limitation.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "more")
	let empty = ""
	var envBlock = ByteArray.create()
	envBlock.push(0)
	let h = __Builtins.subprocessSpawnStreaming(argv, 3, empty.cstr(), 0, envBlock, 1)
	let payload = "abcdefghij\n"
	let wrote = __Builtins.subprocessWriteStdinAll(h, payload.cstr())
	__Builtins.subprocessCloseStdin(h)
	let first = String.init(__Builtins.subprocessReadStdoutBytes(h, 4))
	let second = String.init(__Builtins.subprocessReadStdoutBytes(h, 4))
	let rest = String.init(__Builtins.subprocessReadStdoutBytes(h, 100))
	let afterEof = String.init(__Builtins.subprocessReadStdoutBytes(h, 4))
	let code = __Builtins.subprocessWaitExit(h, 0)
	print("wrote={wrote} first={first} second={second} restLen={rest.byteLength()} eofLen={afterEof.byteLength()} code={code}\n")
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```stdout
wrote=0 first=abcd second=efgh restLen=6 eofLen=0 code=0
```
```exitcode
0
```

<!-- test: subprocess-builtins.streaming-read-bytes-after-line -->
<!-- targets: x64-windows -->
⭐⭐ **THE TWO READERS SHARE ONE PER-HANDLE BUFFER, AND THIS IS THE CASE THAT SAYS SO.** A framed
client reads its headers a LINE at a time and its body by exact COUNT, so the two readers alternate
on one stream. The line reader pulls a whole 4 KiB chunk off the pipe to find its newline; if the
byte reader kept a buffer of its own, every byte already pulled would be invisible to it — the read
below would block on a pipe with nothing left to deliver, and the bytes would be silently lost.
That is a wrong answer, not a limitation, which is why it gets a case rather than a note.

The child echoes two lines. `subprocessReadStdoutLine` takes `first\r\n` (7 bytes — the runtime's
line result carries its terminator), and the four bytes immediately after it are `abcd`.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "more")
	let empty = ""
	var envBlock = ByteArray.create()
	envBlock.push(0)
	let h = __Builtins.subprocessSpawnStreaming(argv, 3, empty.cstr(), 0, envBlock, 1)
	let payload = "first\nabcdefghij\n"
	let wrote = __Builtins.subprocessWriteStdinAll(h, payload.cstr())
	__Builtins.subprocessCloseStdin(h)
	let line = String.init(__Builtins.subprocessReadStdoutLine(h, 1024))
	let after = String.init(__Builtins.subprocessReadStdoutBytes(h, 4))
	let code = __Builtins.subprocessWaitExit(h, 0)
	print("wrote={wrote} lineLen={line.byteLength()} after={after} code={code}\n")
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```stdout
wrote=0 lineLen=7 after=abcd code=0
```
```exitcode
0
```

<!-- test: subprocess-builtins.streaming-read-bytes-refuses-a-negative-count -->
<!-- targets: x64-windows -->
⛔⛔ **A NEGATIVE COUNT IS REFUSED, AND THE STREAM IS LEFT EXACTLY AS IT WAS.** The refusal has to happen
BEFORE the "is enough buffered?" test, because `bufferedBytes >= count` is VACUOUSLY TRUE for a negative
count — so the request falls straight through into the consume with a negative length. That produced a
`String` record claiming a negative length AND, because the consume publishes `buffered - taken`, a stream
buffer whose recorded length had been driven UP past what it holds: the corruption outlives the call and
the NEXT reader walks it. MEASURED before the guard existed: `panic at String.maxon:281: Range check
failed: value outside typealias 'BytePos'`, exit 1.

⚠ **`still=abcd` IS THE HALF THAT MAKES THIS A TEST.** An empty answer alone would also come from a
reader that had quietly eaten the stream; reading four real bytes afterwards is what says the refusal
touched nothing.

⭐ **`state=open` IS WHAT SEPARATES THIS REFUSAL FROM END OF STREAM.** The reader's empty answer is the
same bytes a clean EOF answers; `subprocessStdoutState` is the per-stream fact the reader cannot carry,
and a refusal leaves it `open` where a 0-byte read would have latched `atEof`. That is the whole of what
lets `stdlib/Subprocess.maxon` throw for one and return for the other.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "more")
	let empty = ""
	var envBlock = ByteArray.create()
	envBlock.push(0)
	let h = __Builtins.subprocessSpawnStreaming(argv, 3, empty.cstr(), 0, envBlock, 1)
	let payload = "abcdefghij\n"
	let wrote = __Builtins.subprocessWriteStdinAll(h, payload.cstr())
	__Builtins.subprocessCloseStdin(h)
	let refused = String.init(__Builtins.subprocessReadStdoutBytes(h, -1))
	let state = __Builtins.subprocessStdoutState(h)
	let still = String.init(__Builtins.subprocessReadStdoutBytes(h, 4))
	let code = __Builtins.subprocessWaitExit(h, 0)
	print("wrote={wrote} negLen={refused.byteLength()} state={state.name} still={still} code={code}\n")
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```stdout
wrote=0 negLen=0 state=open still=abcd code=0
```
```exitcode
0
```

<!-- test: subprocess-builtins.streaming-stream-state-follows-the-reader -->
<!-- targets: x64-windows -->
`subprocessStdoutState(handle)` / `subprocessStderrState(handle)` answer `__SubprocessStreamState` — the
per-stream fact a reader's bytes cannot carry. A reader answers `""` for a clean end of stream AND for
every refusal (a handle naming no live child, a negative count, a failed OS read), so the bytes alone
cannot say which; the state can. It is an ENUM, not a number: `.name` below is what an `int` could not
answer, and it is the one place the four outcomes are spelled.

⭐ **THE STATE FOLLOWS THE READER, NOT THE CHILD.** `before` is `open` even though the child has already
been handed EOF on its stdin and may have exited: nothing latches until a READ sees the 0-byte answer.
`after` is `atEof` because the 100-byte request ran into it. A released slot and an invented handle both
answer `noSuchChild`, from the handle guard rather than from any slot.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "more")
	let empty = ""
	var envBlock = ByteArray.create()
	envBlock.push(0)
	let h = __Builtins.subprocessSpawnStreaming(argv, 3, empty.cstr(), 0, envBlock, 1)
	let payload = "abc\n"
	let wrote = __Builtins.subprocessWriteStdinAll(h, payload.cstr())
	__Builtins.subprocessCloseStdin(h)
	let before = __Builtins.subprocessStdoutState(h)
	let errBefore = __Builtins.subprocessStderrState(h)
	let all = String.init(__Builtins.subprocessReadStdoutBytes(h, 100))
	let after = __Builtins.subprocessStdoutState(h)
	let errLine = String.init(__Builtins.subprocessReadStderrLine(h, 64))
	let errAfter = __Builtins.subprocessStderrState(h)
	let code = __Builtins.subprocessWaitExit(h, 0)
	__Builtins.subprocessReleaseHandle(h)
	let released = __Builtins.subprocessStdoutState(h)
	let invented = __Builtins.subprocessStderrState(-1)
	print("wrote={wrote} before={before.name} errBefore={errBefore.name} outLen={all.byteLength()} after={after.name} errLen={errLine.byteLength()} errAfter={errAfter.name} code={code} released={released.name} invented={invented.name}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
wrote=0 before=open errBefore=open outLen=7 after=atEof errLen=0 errAfter=atEof code=0 released=noSuchChild invented=noSuchChild
```
```exitcode
0
```

<!-- test: subprocess-builtins.streaming-read-after-release-throws -->
<!-- targets: x64-windows -->
The stdlib half of the case above. `StreamingSubprocess`'s three readers answer `""` ONLY for end of
stream: a short `readStdoutBytes` at EOF and an empty `readStdoutLine` at EOF both return, and nothing
else does. A refusal throws `SubprocessError.ioFailed` — here the one every caller can reach, a handle
used after `release()`, which the stdlib refuses itself before the runtime is asked (the bootstrap's
handle is a raw pointer, so the runtime could not refuse it safely). `writeStdinLine` and `wait` are
held to the same rule. `closeStdin` and `release` are NOT: they are idempotent, and a released child
already satisfies their postcondition, so they answer as no-ops rather than throwing — and neither
reaches the freed handle.
```maxon
typealias StringArray = Array with String

function readBytesAfterRelease(child StreamingSubprocess) returns String
	let text = try child.readStdoutBytes(4) otherwise (e) 'refused'
		return e.displayReason()
	end 'refused'
	return "answered {text.byteLength()} bytes"
end 'readBytesAfterRelease'

function readLineAfterRelease(child StreamingSubprocess) returns String
	let text = try child.readStdoutLine() otherwise (e) 'refused'
		return e.displayReason()
	end 'refused'
	return "answered {text.byteLength()} bytes"
end 'readLineAfterRelease'

function readErrLineAfterRelease(child StreamingSubprocess) returns String
	let text = try child.readStderrLine() otherwise (e) 'refused'
		return e.displayReason()
	end 'refused'
	return "answered {text.byteLength()} bytes"
end 'readErrLineAfterRelease'

function writeAfterRelease(child StreamingSubprocess) returns String
	try child.writeStdinLine("late") otherwise (e) 'refused'
		return e.displayReason()
	end 'refused'
	return "wrote"
end 'writeAfterRelease'

function waitAfterRelease(child StreamingSubprocess) returns String
	let code = try child.wait() otherwise (e) 'refused'
		return e.displayReason()
	end 'refused'
	return "answered {code}"
end 'waitAfterRelease'

function main() returns ExitCode
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("more")
	var child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: argv) otherwise return 3
	try child.writeStdinLine("abc") otherwise return 4
	child.closeStdin()
	let line = try child.readStdoutLine() otherwise return 5
	let tail = try child.readStdoutBytes(100) otherwise return 6
	let atEnd = try child.readStdoutLine() otherwise return 7
	let code = try child.wait() otherwise return 8
	child.release()
	print("line={line} tailLen={tail.byteLength()} atEndLen={atEnd.byteLength()} code={code}\n")
	print("bytes: {readBytesAfterRelease(child)}\n")
	print("line: {readLineAfterRelease(child)}\n")
	print("errLine: {readErrLineAfterRelease(child)}\n")
	print("write: {writeAfterRelease(child)}\n")
	print("wait: {waitAfterRelease(child)}\n")
	child.closeStdin()
	print("closeStdin: returned\n")
	return 0 as ExitCode
end 'main'
```
```stdout
line=abc tailLen=2 atEndLen=0 code=0
bytes: I/O failed: used after release()
line: I/O failed: used after release()
errLine: I/O failed: used after release()
write: I/O failed: used after release()
wait: I/O failed: used after release()
closeStdin: returned
```
```exitcode
0
```

<!-- test: subprocess-builtins.resolve-on-path -->
<!-- targets: x64-windows -->
The `PATH` + `PATHEXT` walk. `cmd` is on the PATH of every Windows host and has no extension as
written, so a resolver that only tried the name verbatim would miss it and one that only tried the
PATH directories without the extension list would too. The answer is machine-specific, so the
properties asserted are that it is absolute, that it ends in the name, and that it is longer than
what was handed in. A name nothing can resolve comes back UNCHANGED rather than as NULL — see the
divergence note above — which is what `managedIsNull` then reports as "not null".
```maxon
function main() returns ExitCode
	let name = "cmd"
	let resolved = String.init(__Builtins.subprocessResolveOnPath(name.cstr()))
	let missing = "definitely-not-a-real-executable-xyz"
	let unresolved = __Builtins.subprocessResolveOnPath(missing.cstr())
	let unresolvedIsNull = __Builtins.managedIsNull(unresolved)
	let unresolvedText = String.init(unresolved)
	let colon = ":"
	print("grew={resolved.byteLength() > 3} absolute={resolved.contains(colon)} missIsNull={unresolvedIsNull} missEchoed={unresolvedText == missing}")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
grew=true absolute=true missIsNull=0 missEchoed=true

```

<!-- test: subprocess-builtins.last-error-is-empty-when-clean -->
<!-- targets: x64-windows -->
`subprocessLastErrorMessage()` answers the EMPTY buffer while nothing has failed, and the Win32
error NUMBER once something has — here a spawn of an executable that does not exist, which
`CreateProcessA` refuses with `ERROR_FILE_NOT_FOUND` (2).
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	let clean = String.init(__Builtins.subprocessLastErrorMessage())
	var argv = ByteArray.create()
	appendToken(argv, token: "definitely-not-a-real-executable-xyz.exe")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 1, empty.cstr(), env, 1, 0, empty.cstr(), 0, empty.cstr(), 0, 0, empty.cstr(), 0, 0)
	let failed = String.init(__Builtins.subprocessLastErrorMessage())
	let expected = "win32 error 2"
	print("cleanLen={clean.byteLength()} spawn={h} message={failed == expected}")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
cleanLen=0 spawn=-1 message=true

```

<!-- test: subprocess-builtins.a-caller-built-environment-is-the-childs-whole-environment -->
<!-- targets: x64-windows -->
⭐⭐ **`envInherit = 0` HANDS THE CHILD THE CALLER'S OWN BLOCK, AND THIS CASE USED TO ASSERT THE
OPPOSITE.** It was `custom-environment-is-refused`, and its prose said *"a caller-BUILT environment
block … nothing in the corpus can produce"* — true exactly while `stdlib/Subprocess.maxon` refused
`Environment.custom` and `Environment.inheritUpdating` before any spawn call was made. That refusal is
gone: the stdlib assembles a block from this process's own entries (`__Builtins.osEnvironmentEntry`)
with the caller's overrides applied, and this is the contract underneath it.

⚠ **THE ASSERTION IS THE CHILD'S OWN READING, NOT THE SPAWN'S RETURN.** A spawn that merely SUCCEEDS
would be satisfied by a runtime that accepted the block and then passed NULL — which is precisely the
silent wrong answer the old refusal existed to prevent — so the child expands a name that exists ONLY
in the block it was given and echoes it back.

The block is the platform's own shape: NUL-terminated `NAME=VALUE` entries back to back, then one more
NUL. `appendToken` writes the first NUL of each entry, and the lone `push(0)` after the last one ends
the block.

⚠ `cmd` still resolves although the child's environment carries no `PATH`: `CreateProcessA` searches
with the CALLING process's PATH, not the one it is about to hand over.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "echo")
	appendToken(argv, token: "%MAXON_ENV_PROBE%")

	var env = ByteArray.create()
	appendToken(env, token: "MAXON_ENV_PROBE=seen")
	env.push(0)

	let empty = ""
	let h = __Builtins.subprocessSpawn(argv, 4, empty.cstr(), env, 0, 0, empty.cstr(), 2, empty.cstr(), 0, 0, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	print("spawned={h >= 0} echoed={out.startsWith("seen")} len={out.byteLength()}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
spawned=true echoed=true len=6

```
<!-- test: subprocess-builtins.posix-a-caller-built-environment-is-the-childs-whole-environment -->
<!-- targets: arm64-macos, arm64-linux, x64-linux -->
The Windows twin's subject on the POSIX lane. ⚠ **It is a sibling rather than a widened marker for
`posix-collect-echo`'s reason: the CHILD is platform-specific.** `cmd /c echo %VAR%` has no meaning
here, so the probe is `/bin/sh -c`, whose `echo` is a shell builtin and therefore needs no `PATH` in
the environment it is handed.

⭐ **BOTH HALVES OF "WHOLE ENVIRONMENT" ARE ASSERTED, AND THE SECOND IS THE ONE A PASSING SPAWN CAN
STILL GET WRONG.** The child echoes `$HOME` beside the probe: the probe proves the caller's block
REACHED it, and the EMPTY `$HOME` proves the parent's own environment did not — a runtime that
accepted the block and then passed the parent's vector would satisfy the first and fail the second,
which is exactly the silent wrong answer the contract exists to prevent.

⛔ **`$PATH` CANNOT BE THAT WITNESS AND `$HOME` CAN, WHICH IS A FACT ABOUT `sh` RATHER THAN ABOUT THE
SPAWN.** POSIX has a shell SYNTHESIZE a default `PATH` when the environment it is handed carries
none, so an inherited and a caller-built environment both leave `$PATH` non-empty — MEASURED at 60
bytes on macOS and 77 on Linux, a witness that is neither empty nor even the same on two lanes.
`$HOME` is set in every real parent environment and is synthesized by nothing.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "/bin/sh")
	appendToken(argv, token: "-c")
	appendToken(argv, token: "echo probe=$MAXON_ENV_PROBE home=$HOME")

	var env = ByteArray.create()
	appendToken(env, token: "MAXON_ENV_PROBE=seen")
	env.push(0)

	let empty = ""
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 0, 0, empty.cstr(), 2, empty.cstr(), 0, 0, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	print("spawned={h >= 0} echoed={out.startsWith("probe=seen home=")} len={out.byteLength()}")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
spawned=true echoed=true len=17

```

<!-- test: subprocess-builtins.rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
The subprocess substrate is gated on `HostFacility.subprocess`, which x64-windows and arm64-macOS
both provide and wasm does not. On a target that does not, the call is refused at its source span
with `E3104`, naming the runtime entry that has no lowering there — never a panic from inside the
wasm backend, which is what this family did before this rung.

⚠ **THIS LINE READ "the subprocess substrate is x64-windows only", AND THAT PREMISE OUTLIVED ITS
TRUTH BY A WHOLE LANE.** Three compiler comments and one isel panic rested on the same sentence after
arm64-macOS grew the substrate, and the panic is what a program hit: any `--target=arm64-macos`
program touching `Subprocess` — with the DEFAULT `Environment.inherit`, which reads no environment at
all — died in `StdToArm64Conversion` on the environment-block ops, because the reader was built for a
lane whose lowering had been left out on the strength of this claim. The gate is the facility table,
and it always was.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let argv = ByteArray.create()
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 0, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	return h as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:9:21: this construct is x64-windows only at this rung: it lowers to the runtime entry '__gt_subp_attached_spawn', which has no wasm32-wasi implementation
```

<!-- test: subprocess-builtins.streaming-rejected-off-its-substrate -->
<!-- targets: wasm32-wasi -->
The bare-name streaming builtin is gated by the same band and names its own entry. ⚠ It was outside
the gate until this rung, and `SemanticCheck.calleeNeedsWin32Substrate`'s header recorded the
consequence: *"on another target they still die as a BACKEND PANIC rather than a diagnostic —
MEASURED"*.

⚠ **THE LANE IS `wasm32-wasi`, AND IT IS THE LAST ONE THAT CAN CARRY THIS RULE.** The case moved across
the native lanes as each grew a child-process substrate — arm64-macOS at MAC8 (`posix_spawnp`, a
file-actions builder, anonymous pipes, `waitpid` behind a handle object); arm64-Linux at L5 by hand,
because Linux has no `posix_spawn` syscall (`clone(SIGCHLD)`, `dup3`, `execve`, and a close-on-exec status
pipe reporting a failed exec back to the parent); then x64-Linux on the same POSIX shape. All of them
COMPILE AND RUN it, so there is no fourth native lane to move to. A WASI component has no process-spawn
primitive at all, so its refusal is PERMANENT where every native one was "not yet" — a difference in KIND
carried by `SemanticCheck.targetCanHostSubprocess`'s E3074, not by this case, which pins only that the
band is gated and names its own entry.

⇒ **THE NAME DOES NOT CARRY A TARGET, AND THAT IS DELIBERATE.** A target in the name would be a third
copy beside the `targets:` marker and the `maxoncstderr` text, forcing a rename at every re-point. The
marker and the text are the pair the runner checks against each other; a name is the copy nothing
verifies.
⚠ **THE LANE IS `wasm32-wasi`, AND IT IS THE LAST ONE THAT CAN CARRY THIS.** The case moved across the
native lanes as each grew a child-process substrate — arm64-macOS at MAC8, arm64-Linux at L5, x64-Linux
with the green-thread floor — and there is no fourth native lane left. A WASI component has no
process-spawn primitive at all, so its refusal is PERMANENT where every native one was "not yet"; that
difference in KIND is carried by `SemanticCheck.targetCanHostSubprocess`'s E3074, not by this case, which
pins only that the band is gated and names its own entry.

⚠ **IT IS NOT A DUPLICATE OF `subprocess-builtins.rejected-on-wasm`, AND THE ENTRY NAME SEPARATES THEM.**
That case calls the raw fourteen-argument `__Builtins.subprocessSpawn` and names
`__gt_subp_attached_spawn`; this one calls the BARE-NAME `subpSpawn` and names `__gt_subp_spawn`. Two
doors, two entries, two gates — MEASURED: compiled for `wasm32-wasi`, this program emits exactly the
diagnostic below.

```maxon
function main() returns ExitCode
	let h = subpSpawn("cmd /c echo hi")
	return h as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:10: this construct is x64-windows only at this rung: it lowers to the runtime entry '__gt_subp_spawn', which has no wasm32-wasi implementation
```

<!-- test: subprocess-builtins.the-stdlib-api-compiles-on-arm64 -->
<!-- targets: arm64-macos -->
⛔⛔ **A `Subprocess` PROGRAM CROSS-COMPILED TO arm64-macOS, AND THE COMPILER USED TO PANIC ON THIS
EXACT SIX LINES.** `--target=arm64-macos` on a `Configuration.create` + `runConfiguration` with the
DEFAULT `Environment.inherit` died with *"panic at StdToArm64Conversion.maxon:948: the
environment-block ops are x64-windows only"*, exit 1, while `--target=x64-windows` on the same source
exited 0. The claim in that panic was FALSE — `TargetFacilities` answers `subprocess gives true` for
arm64-macOS, so the whole `__gt_subp_` family is built there, `__gt_subp_env_entry` with it, and dead
code elimination cannot drop what the stdlib's spawn path calls.

⭐ **WHY IT NEEDED A CASE OF ITS OWN, WITH EVERY OTHER arm64 SUBPROCESS CASE IN THIS FILE GREEN.** The
nine `posix-*` cases above drive `__Builtins.subprocessSpawn` DIRECTLY, so none of them reaches
`stdlib/Subprocess.maxon`'s `spawnEnvironmentFor` — and `spawnEnvironmentFor` is what keeps the
environment reader alive, for `Environment.inherit` as much as for the other two arms. This is the
first case on this lane that goes through the stdlib's own API, which is the door the compiler was
panicking at. ⚠ `maxon-shv2/Testing/SpecTestRunner.maxon` calls `runConfiguration`, so the shv2
binary itself is inside this blast radius.

⚠ **THE ANSWER IS THE CHILD'S, NOT THE COMPILE'S**, so this is a run case rather than a compile-only
one: `/bin/sh -c 'printf ok'` is echoed back through a collected stdout. On a host that cannot execute
arm64-macOS the harness reports it COMPILED but NOT RUN, which is still the whole of what the panic
made impossible.
```maxon
function main() returns ExitCode
	var config = Configuration.create(Executable.name("/bin/sh"))
	config.arguments = ["-c", "printf ok"]
	config.workingDirectory = try FilePath.from("") otherwise return 1
	let r = try Subprocess.runConfiguration(config) otherwise return 2
	print("exit={r.exitCode()} out={r.stdout}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
exit=0 out=ok
```
```exitcode
0
```

<!-- test: subprocess-builtins.the-stdlib-api-runs-on-windows -->
<!-- targets: x64-windows -->
The host-lane twin of `the-stdlib-api-compiles-on-arm64`, and the reason both exist: the arm64 case is
the one that pins the compile that used to panic, and this one is the same door with its ANSWER
checked on a lane this box can execute. Between them the stdlib's `Configuration` +
`runConfiguration` path — which nothing else in `specs-shv2` exercised — is covered on both lanes the
`subprocess` facility serves.
```maxon
function main() returns ExitCode
	var config = Configuration.create(Executable.name("cmd"))
	config.arguments = ["/c", "echo ok"]
	config.workingDirectory = try FilePath.from("") otherwise return 1
	let r = try Subprocess.runConfiguration(config) otherwise return 2
	print("exit={r.exitCode()} out={r.stdout.trim()}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
exit=0 out=ok
```
```exitcode
0
```
