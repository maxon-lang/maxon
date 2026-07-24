---
feature: stdlib-whitelist
status: stable
keywords: [stdlib, whitelist, Clock, WallClock, prepend, dead-function-elimination, runtime-floor]
category: system
---

# The stdlib whitelist

## Documentation

shv2 does not load all of `stdlib/` the way v1 and the C# bootstrap do. It loads an EXPLICIT
WHITELIST — a listed subset of `stdlib/*.maxon` prepended to every compile — so stdlib support can
grow one module at a time, each module gated on the language features it needs. The list is stated
in exactly one place, `Compiler/StdlibWhitelist.maxon`'s `whitelistedStdlibRelativePaths()`; at this
rung its only entry is `stdlib/Clock.maxon`.

A whitelisted module is registered into the query database exactly like a user source, so it flows
through the same tokenize → signature-index → parse → merge spine. Its declarations therefore
populate the signature registry, and a user program can call `Clock.nowMs()` /
`WallClock.nowUnixSeconds()` though the program itself contains no `Clock`.

`stdlib/` is located by walking UP from the COMPILER's own executable directory, not from the
current working directory: the spec runner and `run_program` compile in a throwaway temp dir, so the
working directory has no `stdlib/` above it, while the compiler binary always lives inside the
checkout. A missing `stdlib/`, or a whitelisted path that does not exist under it, is a loud, hard
error — never a silent skip.

### An unused whitelisted module changes NOTHING

The whitelist prepends Clock to EVERY compile, so a program that never reads a clock must still
compile to the exact bytes it did before Clock was whitelisted — same x64 goldens, same wasm/arm64
output. Two mechanisms compose to guarantee that:

1. Dead-function elimination drops every whitelisted function no reachable code calls, before the
   back end — so an unused Clock never reaches instruction selection, register allocation or
   encoding, and never lands in a golden, on any target.

2. The runtime-floor decision (`scanRuntimeUsage`: does this program carry the heap / GT scheduler /
   wall clock?) runs at the Maxon tier, BEFORE that elimination. Clock.nowMs's body calls
   `__gt_now_ns` and WallClock.nowUnixSeconds's calls `__clock_now_unix_s`, so a naive load would
   install the scheduler and a `.data` slot for a program that reads no clock — slots the later
   elimination cannot prune. The whitelist therefore SKIPS its own unreachable functions in that
   scan (`stdlibWhitelistSkipSet`): a whitelisted function no user code reaches feeds the floor
   decision nothing. User functions are never skipped, so nothing about a program that does not use
   the whitelist changes.

The whole existing `specs-shv2` corpus — every program that never touches Clock — is the standing
proof of this: not one committed fragment moves when Clock is added to the whitelist.
`no-clock-is-byte-neutral` below is the same guard stated directly.

### The collision rule

A whitelisted module must declare no name a user program declares and no builtin type/name.

- FUNCTION-name collisions — a user `Clock.nowMs` against the whitelisted one, or two whitelisted
  modules — are caught loudly by the whole-program duplicate-function check, `E3006`, because a
  whitelisted file merges through the identical path a user file does. A user program that declares
  its own `type Clock` with a `nowMs()` is rejected with

  ```text
  error E3006: <path>/stdlib/Clock.maxon:18:25: duplicate definition of function 'Clock.nowMs'
  ```

  naming the whitelisted definition it collided with. (That path is the real `stdlib/Clock.maxon`,
  not the test fragment, so this diagnostic is documented here rather than pinned as a golden — the
  runner only rewrites the fragment's own path to `<fragment>`.)

- TYPE-name-vs-builtin collisions are the maintainer's responsibility. shv2 has no
  builtin-type-redeclaration diagnostic at all — a user `type String` compiles today as a distinct
  nominal — so enforcing one is a general language matter, not a whitelist one. Clock declares
  `Clock`/`WallClock` and time typealiases, none of them builtin, so it cannot hit this. Do not
  whitelist a module that redeclares a builtin.

## Tests

<!-- test: stdlib-whitelist.clock-from-whitelist -->
<!-- targets: x64-windows -->
A program that calls `Clock.nowMs()` and `WallClock.nowUnixSeconds()` but declares NEITHER type
compiles and runs — the declarations came from the whitelist, not the program. A monotonic reading
and a calendar reading are both positive on any real host.
```maxon
function main() returns ExitCode
	let ms = Clock.nowMs()
	let secs = WallClock.nowUnixSeconds()
	var score = 0
	if ms > 0 'monotonicPositive'
		score = score + 1
	end 'monotonicPositive'
	if secs > 0 'calendarPositive'
		score = score + 1
	end 'calendarPositive'
	return score as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: stdlib-whitelist.elapsed-through-a-sibling -->
<!-- targets: x64-windows -->
`Clock.elapsedMs(since:)` is a whitelisted function that itself calls another whitelisted function
(`Clock.nowMs`), so reaching it must keep BOTH alive through the call graph — the reachability the
runtime-floor skip is computed against. Elapsed time since a reading taken moments earlier is
non-negative.
```maxon
function main() returns ExitCode
	let start = Clock.nowMs()
	var spins = 0
	while spins < 50000 'burn'
		spins = spins + 1
	end 'burn'
	let elapsed = Clock.elapsedMs(start)
	if elapsed >= 0 'nonNegative'
		return 7 as ExitCode
	end 'nonNegative'
	return 1 as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: stdlib-whitelist.nanos-from-whitelist -->
<!-- targets: x64-windows -->
`Clock.nowNanos()` reaches the same monotonic counter as `nowMs` but through the nanosecond entry —
another whitelisted function pulled in only because the program names it.
```maxon
function main() returns ExitCode
	let a = Clock.nowNanos()
	let b = Clock.nowNanos()
	if b >= a 'nonDecreasing'
		return 4 as ExitCode
	end 'nonDecreasing'
	return 1 as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: stdlib-whitelist.no-clock-is-byte-neutral -->
A program that never mentions Clock must compile to exactly what it did before Clock was whitelisted:
the whitelist adds Clock to this compile too, and every one of its functions is pruned back out with
no runtime floor installed. This case carries NO target restriction, so its byte-neutrality is
checked on wasm as well — the whitelist must not drag the x64-only clock substrate into a
non-x64 target for a program that reads no clock.
```maxon
function main() returns ExitCode
	let answer = 42
	return answer as ExitCode
end 'main'
```
```exitcode
42
```
