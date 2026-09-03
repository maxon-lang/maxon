---
feature: builtins-sleep
status: stable
keywords: [builtins, __Builtins, sleep, intrinsics, statement, green-threads, timer]
category: system
---

# The `__Builtins.sleep` intrinsic

## Documentation

`__Builtins` is the compiler's builtin TYPE, whose static methods are INTRINSICS rather than
functions any file declares (see `builtins-clock.md` for the three clock members). `sleep` is the
fourth member, and the one `stdlib/Sleep.maxon` is written against — the module the stdlib loader
now brings into every compile, unmodified, so that a source-level `sleep(ms)` is a call to ITS
declaration rather than to a name the compiler claims (`async-sleep.md`):

```text
export function sleep(milliseconds Milliseconds)
	__Builtins.sleep(milliseconds)
end 'sleep'
```

`__Builtins.sleep(ms)` suspends the current green thread for `ms` milliseconds. It is STDLIB's OWN
FLOOR — the one spelling that is still recognized by name, because the module's body has to bottom
out somewhere — and it takes exactly one INTEGER argument and returns VOID. The bare `sleep(ms)`
builtin that used to share this emit is gone; the stdlib declaration replaced it, and the argument
and result rules a source call meets are now that declaration's.

### A VOID intrinsic needs a STATEMENT position, and that is what the other three never asked for

`currentTimeNanos`, `currentTimeMs` and `currentUnixTimeSeconds` all RETURN a value, so every call to
one arrives in expression position and the `__Builtins` recognizer was only ever reached from there.
`sleep` returns nothing, so it is written on a line of its own — and a qualified call was not a
statement:

```text
error E2015: <file>:4:2: Unsupported: identifier statement
```

A STATIC call is otherwise deliberately not a statement (`Point.create()` on a line of its own
discards a box nothing would then free), but that reason does not reach an intrinsic: it allocates
nothing, and the `__Builtins` table is what decides whether its result exists at all. So the
statement door recognizes `__Builtins.<member>(…)` and nothing else of that shape.

Widening the door widens no NAME. A member the table does not recognize falls through to the same
reserved-callee rejection expression position already gives it — `E3004`, at the call's own span.

### The substrate exists on the lanes that have written it, and nowhere else

The green-thread timer computes its deadline from `osReadClock` and waits with `osSleepMs`, and neither
backend will fake either. Two lanes provide both — x64-windows through
QueryPerformanceCounter/`Sleep`, and arm64-macOS through `clock_gettime_nsec_np(CLOCK_UPTIME_RAW)` and
`nanosleep`. A program that reaches `sleep` on any other is refused with `E3104` at the call site, naming
the runtime entry (`__gt_sleep`) that has no lowering there — never a panic from inside the backend, which
is what it used to be:

```text
panic at StdToWasm.maxon:1108: emitBodyOp: `osReadClock` is x64-windows only — the green-thread sleep substrate is x64-windows-gated at this rung
```

The refusal is a SEMANTIC CHECK, so it does not care whether the call is reachable: an
`__Builtins.sleep` written in a function `main` never calls is refused for wasm just as a type error in an
unreached function is reported. That is a narrowing against the rung before it, where the same program
compiled because dead-function elimination — which runs two tiers later — removed the call before any
backend saw it. It is pinned below so that the reverse change is a deliberate one.

⚠ It is pinned HERE, at the INTRINSIC, because that is the spelling it is still true of. The bare
`sleep(1)` it used to be written with is now a call to `stdlib/Sleep.maxon`'s declaration, which moves the
`__gt_sleep` out of user code and into stdlib source — where the gate is reachability-AWARE, so an
unreached one COMPILES (`async-sleep.unreached-compiles-on-wasm` pins the other side). Reachability-blind
for user code and reachability-aware for stdlib source is one rule with two halves, and the two cases now
pin one half each.

⚠ **Which cases that gates, exactly**: only the ones that REACH the emit. Arity, operand type, value
position and unknown member are decided in the front end, are target-neutral, and carry NO marker — all
four wore one until the 2026-07-28 targets audit measured them green on x64-linux and wasm32-wasi. See
`async-scheduler.md`'s *Targets* section for the one statement of the substrate gate.

## Tests

<!-- test: builtins-sleep.statement-position -->
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
A void wrapper shaped exactly like `stdlib/Sleep.maxon` — one `__Builtins.sleep(…)` statement and
nothing else — compiles, and the sleep is OBSERVABLE: the elapsed time measured across it with
stdlib's `Clock` is at least most of the requested duration. Two stdlib-loading mechanisms in one
program: a `Clock` reading out of stdlib and a void intrinsic in statement position.
```maxon
typealias Millis = int(0 to u64.max)

function nap(duration Millis)
	__Builtins.sleep(duration)
end 'nap'

function main() returns ExitCode
	let start = Clock.nowMs()
	nap(60)
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

<!-- test: builtins-sleep.value-position-rejected -->
`sleep` returns nothing, so reading its result is reading a value that is not there — the same
rejection a void user-call gets, quoting the QUALIFIED name the user wrote.
```maxon
function main() returns ExitCode
	let ignored = __Builtins.sleep(5)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:27: Function '__Builtins.sleep' does not return a value
```

<!-- test: builtins-sleep.arity-rejected -->
An intrinsic has no signature registry entry for the ordinary arity check to consult, so the arity is
enforced at the emit — and the diagnostic quotes what the user wrote, not the bare spelling.
```maxon
function main() returns ExitCode
	__Builtins.sleep()
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:13: '__Builtins.sleep' takes exactly 1 argument, but 0 were given
```

<!-- test: builtins-sleep.float-arg-rejected -->
The duration is an integer count of milliseconds; a float is refused, by the SAME operand rule the
bare `sleep(1.5)` is refused by (`async-sleep.float-arg-rejected`) — one emit, so the two spellings
cannot drift apart on what they accept. ⚠ The two differ in WHICH refusal arrives FIRST off
x64-windows, which is why only this one is un-gated: the qualified `__Builtins.sleep` form reaches the
operand check, while the bare `sleep` name is a stdlib call the E3104 target gate refuses
before the operand is ever typed. Measured 2026-07-28 on x64-linux and wasm32-wasi.
```maxon
function main() returns ExitCode
	__Builtins.sleep(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:13: '__Builtins.sleep' requires a integer, but its argument is float
```

<!-- test: builtins-sleep.unknown-member-in-statement-position -->
The statement door widened a SHAPE, not a name list: an unrecognized `__Builtins` member written on a
line of its own reaches the same reserved-callee rejection it gets in expression position, rather than
the shape-level `E2015` it used to die on.
```maxon
function main() returns ExitCode
	__Builtins.nope()
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:13: call to undefined function '__Builtins.nope': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: builtins-sleep.rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
The green-thread sleep substrate is x64-windows only at this rung. On any other target the call is
refused at its source span with `E3104`, naming the runtime entry that has no lowering there — never
a panic from inside the wasm backend.
```maxon
function main() returns ExitCode
	__Builtins.sleep(1)
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:13: this construct is x64-windows only at this rung: it lowers to the runtime entry '__gt_sleep', which has no wasm32-wasi implementation
```

<!-- test: builtins-sleep.rejected-on-wasm-when-unreached -->
<!-- targets: wasm32-wasi -->
The gate is REACHABILITY-BLIND for user code: `napper` is never called, yet its intrinsic is still refused.
`SemanticCheck` visits every function and dead-function elimination runs two tiers later, so this is the
same rule that reports a type error in an unreached function. Pinned because the stdlib loader's own
exemption (`checkCalls` skips an unreached stdlib body) points the other way, and it is the INTRINSIC that
keeps this property: the bare `sleep(1)` this case was written with reaches the same entry through
`stdlib/Sleep.maxon` now, so it takes the exemption instead.
```maxon
function napper()
	__Builtins.sleep(1)
end 'napper'

function main() returns ExitCode
	return 4
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:13: this construct is x64-windows only at this rung: it lowers to the runtime entry '__gt_sleep', which has no wasm32-wasi implementation
```
