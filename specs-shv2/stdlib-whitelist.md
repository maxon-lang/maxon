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
rung it holds `stdlib/Clock.maxon` and `stdlib/Sleep.maxon`, in that order.

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
  `Clock`/`WallClock` and time typealiases and Sleep declares `Milliseconds`, none of them builtin,
  so neither can hit this. Do not whitelist a module that redeclares a builtin.

- FUNCTION-name-vs-BARE-BUILTIN collisions are SILENT, and `stdlib/Sleep.maxon` is standing in one.
  The parser recognizes a handful of BARE names (`print`, `sleep`, `trunc`, `runProcess`, the math
  intrinsics) before any registry is consulted, so a CALL to one never reaches a declaration of that
  name — `sleep(5)` emits the builtin's `__gt_sleep` directly, while `let f = sleep` (not a call
  site) takes the address of the whitelisted `stdlib.sleep`, whose body reaches the same entry
  through `__Builtins.sleep`. Both suspend the green thread for the same duration, so a program is
  correct either way; what is unsound is that one name has two routes. It predates the whitelist —
  a user file declaring its own `function sleep` already compiled with the declaration silently
  unlinked — and repairing it means deleting the bare-name builtin, which moves every committed
  golden that calls `sleep` and hands `E3104` a span inside `stdlib/Sleep.maxon` instead of the
  user's own call. Until then, do not whitelist a module whose function name a bare builtin already
  claims unless — as here — the two lower to the same runtime entry.

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

<!-- test: stdlib-whitelist.sleep-module-runs -->
<!-- targets: x64-windows -->
`stdlib/Sleep.maxon` — whitelist entry #2 — compiles UNMODIFIED as part of this build, and the code
it contributes RUNS. `sleep` names the whitelisted declaration (a bare `sleep(…)` call site would be
claimed by the bare-name builtin first — see the collision rule above — so the function VALUE is what
reaches it), and calling through it suspends the green thread observably: the elapsed time measured
across it with `Clock` is at least most of the requested duration. Both whitelisted modules are live
in one program, which is also the proof that two of them coexist.
```maxon
function main() returns ExitCode
	let start = Clock.nowMs()
	let napper = sleep
	napper(60)
	let elapsed = Clock.elapsedMs(start)
	if elapsed >= 40 'slept'
		return 7 as ExitCode
	end 'slept'
	return 1 as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: stdlib-whitelist.no-clock-is-byte-neutral -->
A program that never mentions Clock or Sleep must compile to exactly what it did before either was
whitelisted: the whitelist adds both to this compile too, and every one of their functions is pruned
back out with no runtime floor installed. This case carries NO target restriction, so its
byte-neutrality is checked on wasm as well — the whitelist must not drag the x64-only clock and timer
substrate into a non-x64 target for a program that reads no clock and never sleeps.
```maxon
function main() returns ExitCode
	let answer = 42
	return answer as ExitCode
end 'main'
```
```exitcode
42
```
