---
feature: stdlib-whitelist
status: stable
keywords: [stdlib, whitelist, Clock, WallClock, prepend, dead-function-elimination, runtime-floor]
category: system
---

# The stdlib whitelist

## Documentation

shv2's stdlib loader enumerates every `.maxon` file under the checkout's `stdlib/`, top level and
subdirectories alike, exactly as v1 and the C# bootstrap do — and then a TEMPORARY WHITELIST filters
which of them are actually loaded, so stdlib support can grow one module at a time, each module gated
on the language features it needs. The filter is stated in exactly one place,
`Compiler/StdlibLoader.maxon`; at this rung its only entry is `stdlib/Clock.maxon`.

The whitelist is scaffolding, not a feature: it is a filter INSIDE the real loader rather than a list
the loader walks, so removing it is a deletion rather than a rewrite — of ONE file, which owns every
mention of it including the types and the error cases it needs. Everything downstream of the loader
deals in two durable facts instead — "is this function stdlib source or user source?" and "is it
reachable from `main`?" — so no pass has to be rewritten on the day the filter goes.

A whitelisted module is registered into the query database exactly like a user source, so it flows
through the same tokenize → signature-index → parse → merge spine. Its declarations therefore
populate the signature registry, and a user program can call `Clock.nowMs()` /
`WallClock.nowUnixSeconds()` though the program itself contains no `Clock`.

That sameness runs one level deeper: which files under a directory are Maxon sources is decided by
ONE enumerator (`Compiler.collectMaxonSources`), which walks the user's own root and `stdlib/` alike,
so the extension, the excluded build manifest and every ignore rule either walk grows are stated once.
The loader also skips a file the project has ALREADY registered — the one case being a user root that
lies under `stdlib/` — because a source registered under two spellings of its path is parsed twice and
every function in it then collides with itself. (Not pinned by a test below: the harness stages every
fragment in a temp directory and cannot place one inside the checkout's `stdlib/`.)

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
   elimination cannot prune. That scan therefore SKIPS the stdlib functions no path from `main`
   reaches (`StdlibFacts.unreachable`): code the program does not contain feeds the floor decision
   nothing. User functions are never skipped — an unreached user body still speaks for the program —
   so nothing about a program that touches no stdlib changes.

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

### A diagnostic raised inside stdlib source is attributed to the crossing call

Stdlib source is compiled as part of the program, so a rejection raised in one of its bodies would be
positioned at ITS path — an absolute `…/stdlib/Foo.maxon:L:C` naming a file the user never opened, at
a line they cannot change, for a choice they made at a call site somewhere else.

That is not a quirk of one module: it fires wherever a stdlib body bottoms out in something
target-gated, which is exactly what a stdlib leaf is FOR — every module in the queue behind Clock ends
in a `__Builtins.*` intrinsic — and it gets more common, not less, as stdlib grows.

The refusal that reaches this today is the TARGET gate, `E3104`. It is reported at the FIRST call
crossing from user code INTO stdlib, and it names the stdlib function the user actually wrote:

```text
error E3104: <fragment>:3:10: this construct is x64-windows only at this rung: 'Clock.nowMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

The requirement is TRANSITIVE through the stdlib call graph: `Clock.elapsedMs` names no runtime entry
itself — it calls `Clock.nowMs`, which does — so a program calling `elapsedMs` is refused at ITS call,
naming `Clock.elapsedMs` and still naming the entry that has no lowering. A user's own helper is user
code, so `main → myHelper → Clock.nowMs` is blamed inside `myHelper`. And a stdlib function no path
from `main` reaches is refused nowhere at all, which is what keeps an unused module byte-neutral.

The gate is therefore reachability-BLIND for user code and reachability-AWARE for stdlib source: a
`sleep(1)` in a function `main` never calls is still refused for wasm
(`builtins-sleep.rejected-on-wasm-when-unreached`), while a `Clock.nowMs()` in one is not. The day a
bare-name builtin is retired in favour of the stdlib declaration that shadows it, that program moves
from the first case to the second — a behaviour change to decide on deliberately, not to discover.

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

<!-- test: stdlib-whitelist.target-refusal-blames-the-crossing-call -->
<!-- targets: wasm32-wasi -->
A whitelisted body that bottoms out in an x64-only runtime entry is refused at the USER's call, and
names the whitelisted function they wrote — never at `stdlib/Clock.maxon`, a file they never opened
and cannot change.
```maxon
function main() returns ExitCode
	let t = Clock.nowMs()
	if t > 0 'chk'
		return 4
	end 'chk'
	return 5
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:10: this construct is x64-windows only at this rung: 'Clock.nowMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

<!-- test: stdlib-whitelist.target-refusal-blames-the-crossing-call-arm64 -->
<!-- targets: arm64-macos -->
The attribution is a property of the whitelist mechanism, not of one backend: the same program
compiled for arm64 is refused at the same user span, naming the same whitelisted function and the
same missing runtime entry.
```maxon
function main() returns ExitCode
	let t = Clock.nowMs()
	if t > 0 'chk'
		return 4
	end 'chk'
	return 5
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:10: this construct is x64-windows only at this rung: 'Clock.nowMs' lowers to the runtime entry '__gt_now_ns', which has no arm64-macos implementation
```

<!-- test: stdlib-whitelist.target-refusal-is-transitive -->
<!-- targets: wasm32-wasi -->
`Clock.elapsedMs` reaches no runtime entry itself — it calls `Clock.nowMs`, which does. The
requirement propagates through the whitelisted call graph, so the caller is blamed at the function
THEY named, while the entry named is still the one that has no lowering.
```maxon
function main() returns ExitCode
	let e = Clock.elapsedMs(0)
	if e >= 0 'chk'
		return 4
	end 'chk'
	return 5
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:10: this construct is x64-windows only at this rung: 'Clock.elapsedMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

<!-- test: stdlib-whitelist.target-refusal-blames-the-users-own-helper -->
<!-- targets: wasm32-wasi -->
A user's own function is not whitelisted, so the crossing is the call INSIDE it — code the user can
actually change — rather than `main`'s call to the helper or anything in `stdlib/`.
```maxon
function reader() returns int
	return Clock.nowMs()
end 'reader'

function main() returns ExitCode
	let t = reader()
	if t > 0 'chk'
		return 4
	end 'chk'
	return 5
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:9: this construct is x64-windows only at this rung: 'Clock.nowMs' lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

<!-- test: stdlib-whitelist.unreached-clock-still-compiles-on-wasm -->
<!-- targets: wasm32-wasi -->
The crossing gate is reachability-AWARE, exactly as the runtime-floor skip is: `reader` is never
called, so no path from `main` crosses into the whitelist and the program compiles for wasm
unchanged. This is the byte-neutrality guarantee stated as a run — attributing the refusal to the
caller must not turn an untaken crossing into a refusal.
```maxon
function reader() returns int
	return Clock.nowMs()
end 'reader'

function main() returns ExitCode
	return 4
end 'main'
```
```exitcode
4
```
