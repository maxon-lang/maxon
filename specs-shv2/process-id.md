---
feature: process-id
status: stable
keywords: [process, currentProcessId, pid, introspection, __Builtins, intrinsics]
category: system
---

# `__Builtins.currentProcessId()` — the running process's own id

## Documentation

`__Builtins` is the compiler's builtin TYPE: a name reserved for the compiler, whose static methods
are INTRINSICS rather than functions any file declares. This spec pins one of them:

| Intrinsic | Meaning |
|---|---|
| `__Builtins.currentProcessId()` | the OS-assigned id of the RUNNING process, as an `int` |

It takes no arguments, and `/specs/builtins-type.md` documents it as *"Pid of the current process"*.
Unlike `__Builtins.executablePath` it has no `stdlib/` wrapper: the intrinsic IS the surface, and its
one caller in this tree is `maxon-shv2/Compiler/TreeLock.maxon`, which stamps the id into a lock file
so a later reader can tell a live holder from an abandoned one.

### The id is MACHINE- AND RUN-SPECIFIC, so every case here asserts a PROPERTY

There is no literal to compare against — the value differs on every run of every case — so the
properties asserted below are all that cannot vary:

- it is POSITIVE. Zero is not a Windows process id, and a call that never happened would leave
  whatever the result register held;
- it FITS IN A DWORD. `GetCurrentProcessId` answers a 32-bit `DWORD` in `EAX`, so a sign-extension
  or a stale high half shows up here as a value past `2^32` (or as a negative one) rather than as a
  plausible-looking id nobody would question;
- it is STABLE. Every call in one process answers the same id, which is what makes it usable as a
  lock token at all;
- it DIFFERS FROM A CHILD'S. A spawned child has an id of its own, and `__Builtins.subprocessGetPid`
  reports it — so a constant, or a lowering that answered some other process's id, cannot pass.

### The child comparison is the only in-language ORACLE, and it is a discriminator rather than an equality

Nothing a Maxon program can reach reports "the id Windows has for this process" independently of the
intrinsic itself, so there is no equality to assert against an outside authority. What IS reachable
is a SECOND live process whose id the OS reports through a different entry point entirely
(`subprocessGetPid`, off the `CreateProcessA` `PROCESS_INFORMATION`), and two live processes never
share an id. That falsifies every wrong answer of the shape "a constant", "the same id for
everybody" and "the child's id" — which is the whole class a stable, positive, DWORD-sized number
could otherwise hide in.

### The substrate is x64-windows only at this rung, and the reason is NOT `executablePath`'s

`__Builtins.currentProcessId` lowers to `__proc_pid`, which joins the file, directory, command-line
and executable-path families under `SemanticCheck.calleeNeedsWin32Substrate` — by the `__proc_`
PREFIX, so it is gated by construction rather than by memory — and a program that reaches it on
another target is refused with `E3104` at the call's own span.

⚠ `process-executable-path.md` argues that its own intrinsic is host-only because the three
platforms genuinely DISAGREE about the API's shape (a fill-and-count, an in-out size, a symlink).
That argument does not reach this one and is not borrowed: `getpid()` and `GetCurrentProcessId()`
are the same call with two spellings, and neither can fail. What gates this intrinsic is narrower
and honest — shv2's arm64-macos and wasm32-wasi lanes have no OS-primitive substrate at all at this
rung, so there is nowhere for the one instruction to go. WASI is the one target where the refusal is
more than "not yet": a component has no process identity to report.

## Tests

<!-- test: process-id.is-positive -->
<!-- targets: x64-windows -->
The weakest property, and the one a missing lowering fails first: the call answers a number above
zero rather than whatever the result register happened to hold.
```maxon
function main() returns ExitCode
	let pid = __Builtins.currentProcessId()
	if pid > 0 'positive'
		return 3
	end 'positive'
	return 1
end 'main'
```
```exitcode
3
```

<!-- test: process-id.fits-in-a-dword -->
<!-- targets: x64-windows -->
`GetCurrentProcessId` answers a 32-bit `DWORD`. The id must therefore land in `[1, 2^32)` — which is
what a plain 32-bit write to `EAX` gives for free and what a sign-extension, or a high half left
over from a previous call, would break. A wrong answer of that shape is still positive and still
stable, so this is the case that separates it from a real id.
```maxon
function main() returns ExitCode
	let pid = __Builtins.currentProcessId()
	let dwordLimit = 4294967296
	var score = 0
	if pid > 0 'aboveZero'
		score = score + 1
	end 'aboveZero'
	if pid < dwordLimit 'belowTheDwordCeiling'
		score = score + 2
	end 'belowTheDwordCeiling'
	return score as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: process-id.is-stable-across-calls -->
<!-- targets: x64-windows -->
Three independent calls in one process agree. This is the property `TreeLock` actually leans on — a
token written at `takeLock` and re-read at the release has to name the same process — and it is what
a lowering that read a THREAD id, or that answered a fresh counter, would fail.
```maxon
function main() returns ExitCode
	let first = __Builtins.currentProcessId()
	let second = __Builtins.currentProcessId()
	let third = __Builtins.currentProcessId()
	var score = 0
	if first == second 'firstPair'
		score = score + 1
	end 'firstPair'
	if second == third 'secondPair'
		score = score + 3
	end 'secondPair'
	return score as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: process-id.differs-from-a-child -->
<!-- targets: x64-windows -->
The only oracle a program can reach: spawn a child and ask a DIFFERENT entry point
(`__Builtins.subprocessGetPid`, off `CreateProcessA`'s `PROCESS_INFORMATION`) for ITS id. Two live
processes never share one, so a constant — or an intrinsic that answered the child's id, or the same
id for every process — cannot pass. The child is waited on and both structs released, so the case is
leak-clean.
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
	appendToken(argv, token: "child")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 4, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	let childPid = __Builtins.subprocessGetPid(h)
	let selfPid = __Builtins.currentProcessId()
	let r = __Builtins.subprocessWaitCollect(h, 0)
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	var score = 0
	if childPid > 0 'childHasAnId'
		score = score + 1
	end 'childHasAnId'
	if selfPid > 0 'selfHasAnId'
		score = score + 2
	end 'selfHasAnId'
	if selfPid != childPid 'twoDistinctProcesses'
		score = score + 4
	end 'twoDistinctProcesses'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: process-id.arity-checked -->
`currentProcessId` takes no arguments. An intrinsic has no signature for the ordinary arity check to
read, so it is refused by the same `builtinArity` check `executablePath`/`commandLineCount` use.
This case is front-end only and target-neutral, so it carries no marker.
```maxon
function main() returns ExitCode
	return __Builtins.currentProcessId(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.currentProcessId' takes exactly 0 argument, but 1 were given
```

<!-- test: process-id.rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
The introspection substrate is x64-windows only at this rung. On any other target the call is
refused at its source span with `E3104`, naming the runtime entry that has no lowering there — never
a panic from inside the wasm backend.
```maxon
function main() returns ExitCode
	let pid = __Builtins.currentProcessId()
	return pid as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:23: this construct is x64-windows only at this rung: it lowers to the runtime entry '__proc_pid', which has no wasm32-wasi implementation
```
