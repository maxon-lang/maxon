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

- FUNCTION-name-vs-BARE-BUILTIN collisions are SILENT — and this is why `stdlib/Sleep.maxon` is
  **not** the second entry, though the compiler can now compile it unmodified. The parser
  recognizes a handful of BARE names (`print`, `sleep`, `trunc`, `runProcess`, the math intrinsics)
  before any registry is consulted, so a CALL to one never reaches a declaration of that name.
  Whitelisting Sleep would load a module no ordinary call site can reach — `sleep(5)` still emits
  the builtin's `__gt_sleep` — while charging the whitelist's per-compile cost (measured: +677
  allocations and ~30 KB on **every** compile, flat across a 32x scale ladder) for zero delivered
  capability. And the name would have two routes: `let f = sleep` is not a call site, so it takes
  the address of the whitelisted `stdlib.sleep`, whose body reaches the same entry the long way
  round.

  The shadowing is not the whitelist's doing and predates it: a user file declaring its own
  `function sleep` already compiles with the declaration silently unlinked, no diagnostic. The
  repair is to delete the bare-name builtin and let the whitelisted declaration be authoritative —
  its own rung, because it moves every committed golden that calls `sleep` (counted: 15 goldens
  across 7 specs — `async-sleep`, `async-promise-drop`, `async-stack-growth`, `async-subprocess`,
  `builtins-clock`, `spawn-read-line`, `streaming-subprocess`), changes
  `async-sleep.float-arg-rejected`'s pinned stderr, and must first answer the gap below.
  **The rule: do not whitelist a module whose function name a bare builtin already claims. Retire
  the builtin first.**

### The open mechanism gap: a diagnostic that originates INSIDE whitelisted stdlib source

A whitelisted module is a real compilation unit, so a rejection raised in ITS body is positioned at
ITS path — an absolute `…/stdlib/Foo.maxon:L:C` naming a file the user never opened, for a mistake
they made at a call site somewhere else. Nothing renders it in terms of the caller, and no spec can
pin it (the runner rewrites only the fragment's own path).

This is GENERAL to the whitelist, not a quirk of any one module. It fires wherever a whitelisted
body bottoms out in something target-gated or otherwise refusable — which is exactly what a stdlib
leaf is for; every module in the queue behind Clock ends in a `__Builtins.*` intrinsic. Clock is
only spared today because its callers are `Clock.nowMs()`-shaped, so `E3104`'s span already lands
inside `stdlib/Clock.maxon` on a non-x64 target and no test compiles a Clock program for one.

Which route makes the gap user-visible was measured rather than assumed. With the Sleep entry
temporarily added, a plain `sleep(1)` compiled for wasm is still anchored at the **user's** span —
the bare builtin claims the call site, so the whitelisted body is never entered. What reaches the
gap is the second route above: `let f = sleep` takes the whitelisted declaration's address, and
`f(5)` for wasm reports `E3104` at `stdlib/Sleep.maxon:6:2`, a file the user never opened. Retiring
the bare-name builtin sends the plain call down that route too, turning the gap from exotic into the
common case. A diagnostic raised in whitelisted source needs a caller-side anchor before the
whitelist grows a module users reach through a target-gated leaf.

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
