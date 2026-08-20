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
bottoms out in one of twenty compiler intrinsics. `Parser.BuiltinsSubprocessSpawnName` lists all
twenty; they fall into three families.

| Family | Intrinsics |
|---|---|
| the ATTACHED run | `subprocessSpawn` / `subprocessDetach` (fourteen arguments), `subprocessGetPid`, `subprocessWaitCollect`, `subprocessResultStatusKind` / `StatusCode` / `Stdout` / `Stderr` / `DurationMs` / `Release`, `subprocessReleaseHandle` |
| the STREAMING child | `subprocessSpawnStreaming`, `subprocessWriteStdinAll`, `subprocessReadStdoutLine` / `ReadStderrLine`, `subprocessCloseStdin`, `subprocessWaitExit` |
| the helpers | `subprocessResolveOnPath`, `subprocessLastErrorMessage`, and `managedIsNull` (a `__ManagedMemory` predicate whose only corpus caller is the executable lookup) |

The bootstrap declares exactly these twenty with exactly these shapes
(`maxon-sharp/Compiler/2-Parser.cs:11936-12022`), so the SURFACE is the reference's and only the
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
- **env / envInherit**: only `1` (inherit) is servable. There is no producer for a caller-built
  environment block anywhere in the corpus — `requireInheritEnv` refuses `Environment.custom` and
  `inheritUpdating` before any spawn call is made — so `envInherit = 0` answers `-1` with a recorded
  error rather than silently serving the parent's environment under another name.
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
The guard sits at the ENTRY POINT, never at a wrapper, so both families — the twenty
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

function runEcho(word String, outKind int) returns int
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
what "streaming" MEANS — there are no stdio triples to pass because the caller drives all three.
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
	let h = __Builtins.subprocessSpawnStreaming(argv, 3, empty.cstr(), 0)
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

<!-- test: subprocess-builtins.custom-environment-is-refused -->
<!-- targets: x64-windows -->
`envInherit = 0` asks for a caller-BUILT environment block, which nothing in the corpus can produce
— `stdlib/Subprocess.maxon`'s `requireInheritEnv` refuses `Environment.custom` before any spawn call
is made. The runtime answers a failure rather than silently serving the parent's environment under
another name.
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
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 1, empty.cstr(), env, 0, 0, empty.cstr(), 0, empty.cstr(), 0, 0, empty.cstr(), 0, 0)
	print("spawn={h}")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
spawn=-1

```

<!-- test: subprocess-builtins.rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
The subprocess substrate is x64-windows only. On any other target the call is refused at its source
span with `E3104`, naming the runtime entry that has no lowering there — never a panic from inside
the wasm backend, which is what this family did before this rung.
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

<!-- test: subprocess-builtins.streaming-rejected-on-arm64 -->
<!-- targets: arm64-macos -->
The bare-name streaming builtin is gated by the same band and names its own entry. ⚠ It was outside
the gate until this rung, and `SemanticCheck.calleeNeedsWin32Substrate`'s header recorded the
consequence: *"on another target they still die as a BACKEND PANIC rather than a diagnostic —
MEASURED"*.
```maxon
function main() returns ExitCode
	let h = subpSpawn("cmd /c echo hi")
	return h as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:10: this construct is x64-windows only at this rung: it lowers to the runtime entry '__gt_subp_spawn', which has no arm64-macos implementation
```
