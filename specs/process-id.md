---
feature: process-id
status: stable
keywords: [process, Process.id, currentProcessId, pid, introspection]
category: system
---

# `Process.id()` — the running process's own id

## Documentation

| Function | Meaning |
|---|---|
| `Process.id() returns Pid` | the OS-assigned id of the RUNNING process |

It takes no arguments and cannot fail. Its body is the library-tier intrinsic
`__Builtins.currentProcessId()`, which only a source file under `stdlib/` or `runtime/` may call; a program
calls `Process.id()`. The compiler's `TreeLock` stamps the id into a lock file so a later reader can tell a
live holder from an abandoned one.

### The id is MACHINE- AND RUN-SPECIFIC, so every case here asserts a PROPERTY

There is no literal to compare against — the value differs on every run of every case — so the
properties asserted below are all that cannot vary:

- it is POSITIVE. Zero is not a process id on any host, and a call that never happened would
  leave whatever the result register held;
- it FITS IN A DWORD. `GetCurrentProcessId` answers a 32-bit `DWORD` and `getpid()` a 32-bit
  `pid_t`, so a sign-extension or a stale high half shows up here as a value past `2^32` rather than
  as a plausible-looking id nobody would question;
- it is STABLE. Every call in one process answers the same id, which is what makes it usable as a
  lock token at all;
- it DIFFERS FROM A CHILD'S. A spawned child has an id of its own, which `Subprocess.run` reports in
  `CollectedOutput.pid` — so a constant, or a lowering that answered some other process's id, cannot
  pass.

### The child comparison is the only in-language ORACLE, and it is a discriminator rather than an equality

Nothing a program can reach reports "the id the OS has for this process" independently of
`Process.id()` itself. What IS reachable is a SECOND process whose id the OS reports through a different
entry point entirely (the spawn's own process record), and a running process's id is never handed to
another process. That falsifies every wrong answer of the shape "a constant", "the same id for
everybody" and "the child's id".

### Where it is served

The intrinsic lowers to `__proc_pid`, one of the two IDENTITY reads behind the `processInfo` host
facility, gated by the `__proc_` PREFIX; a call reaching it on a target that does not provide the facility
is refused with `E3104` at the call's own span. WASI is refused because a component has no process
identity to report.

⚠ **`fits-in-a-dword` IS FREE ON ONE LANE AND BOUGHT ON THE OTHER.** Writing `EAX` zeroes the upper
half of `RAX` architecturally, so `GetCurrentProcessId`'s `DWORD` arrives clean; AAPCS64 leaves the
upper half of a register holding an `int` return UNSPECIFIED, so the arm64 lowering must widen
`getpid`'s `pid_t` itself.

The `arity-checked` case pins the intrinsic's own arity check, so it compiles as a library-tier source
(`<!-- tier: stdlib -->`).

## Tests

<!-- test: process-id.is-positive -->
The weakest property, and the one a missing lowering fails first: the call answers a number above
zero rather than whatever the result register happened to hold.
```maxon
function main() returns ExitCode
	let pid = Process.id()
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
`GetCurrentProcessId` answers a 32-bit `DWORD` and `getpid()` a 32-bit `pid_t`. The id must therefore
land in `[1, 2^32)` — which is what a plain 32-bit write to `EAX` gives for free and what a stale
high half, on either lane, would break. A wrong answer of that shape is still positive and still
stable, so this is the case that separates it from a real id. ⚠ On arm64 it is the WIDENING at the
call site that keeps it true, not the ABI; see the section above.
```maxon
function main() returns ExitCode
	let pid = Process.id()
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
Three independent calls in one process agree. This is the property `TreeLock` actually leans on — a
token written at `takeLock` and re-read at the release has to name the same process — and it is what
a lowering that read a THREAD id, or that answered a fresh counter, would fail.
```maxon
function main() returns ExitCode
	let first = Process.id()
	let second = Process.id()
	let third = Process.id()
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
<!-- unsupported-targets: wasm32-wasi -->
The only oracle a program can reach: run a child and read ITS id off the spawn (`CollectedOutput.pid`).
This process is alive for the whole case, so its id belongs to no other process — a constant, an answer
that was the child's id, or the same id for every process cannot pass. wasm32-wasi cannot host a child process
and answers `E3074`, which no skip counts, so the case is pinned on the other lanes.
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("exit")
	argv.push("0")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sh") otherwise return 20)
	var argv = StringArray.create()
	argv.push("-c")
	argv.push("exit 0")
	#endif
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 21
	let childPid = result.pid
	let selfPid = Process.id()
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
<!-- tier: stdlib -->
The intrinsic takes no arguments. An intrinsic has no signature for the ordinary arity check to read, so
it is refused by the same `builtinArity` check `executablePath`/`commandLineCount` use.
```maxon
function main() returns ExitCode
	return __Builtins.currentProcessId(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.currentProcessId' takes exactly 0 argument, but 1 were given
```

<!-- test: process-id.rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
wasm32-wasi has no process-introspection substrate, so the call is refused at the span the program wrote
with `E3104`, naming the library function and the runtime entry that has no lowering there — never a
panic from inside the wasm backend.
```maxon
function main() returns ExitCode
	let pid = Process.id()
	return pid as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: 'Process.id' lowers to the runtime entry '__proc_pid', which has no wasm32-wasi implementation
```
