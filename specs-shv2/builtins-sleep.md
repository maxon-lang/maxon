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
fourth member, and the one `stdlib/Sleep.maxon` is written against — a module the compiler can now
compile unmodified but which the whitelist is deliberately NOT grown to, because the bare-name
`sleep` builtin already claims the name (`stdlib-whitelist.md` states the rule and the measurement):

```text
export function sleep(milliseconds Milliseconds)
	__Builtins.sleep(milliseconds)
end 'sleep'
```

`__Builtins.sleep(ms)` suspends the current green thread for `ms` milliseconds, exactly as the bare
`sleep(ms)` builtin does — the two spellings share one emit, so they cannot come to disagree about
the argument's type, the absent result, or which scheduler entry parks the thread. It takes exactly
one INTEGER argument and returns VOID.

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

### The substrate is x64-windows only at this rung

The green-thread timer computes its deadline from `osReadClock`'s QueryPerformanceCounter reading and
waits with `osSleepMs`, neither of which `StdToArm64`/`StdToWasm` will fake. A program that reaches
`sleep` on another target is refused with `E3104` at the call site, naming the runtime entry
(`__gt_sleep`) that has no lowering there — never a panic from inside the backend, which is what it
used to be:

```text
panic at StdToWasm.maxon:1108: emitBodyOp: `osReadClock` is x64-windows only — the green-thread sleep substrate is x64-windows-gated at this rung
```

The refusal is a SEMANTIC CHECK, so it does not care whether the call is reachable: a `sleep` written in
a function `main` never calls is refused for wasm just as a type error in an unreached function is
reported. That is a narrowing against the previous rung, where the same program compiled because
dead-function elimination — which runs two tiers later — removed the call before any backend saw it. It is
pinned below so that the reverse change is a deliberate one.

## Tests

<!-- test: builtins-sleep.statement-position -->
<!-- targets: x64-windows -->
A void wrapper shaped exactly like `stdlib/Sleep.maxon` — one `__Builtins.sleep(…)` statement and
nothing else — compiles, and the sleep is OBSERVABLE: the elapsed time measured across it with the
whitelisted `Clock` is at least most of the requested duration. Two whitelisted-era mechanisms in one
program: a `Clock` reading from the whitelist and a void intrinsic in statement position.
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
<!-- targets: x64-windows -->
`sleep` returns nothing, so reading its result is reading a value that is not there — the same
rejection a void user-call gets, quoting the QUALIFIED name the user wrote.
```maxon
function main() returns ExitCode
	let ignored = __Builtins.sleep(5)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:16: Function '__Builtins.sleep' does not return a value
```

<!-- test: builtins-sleep.arity-rejected -->
<!-- targets: x64-windows -->
An intrinsic has no signature registry entry for the ordinary arity check to consult, so the arity is
enforced at the emit — and the diagnostic quotes what the user wrote, not the bare spelling.
```maxon
function main() returns ExitCode
	__Builtins.sleep()
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:2: '__Builtins.sleep' takes exactly 1 argument, but 0 were given
```

<!-- test: builtins-sleep.float-arg-rejected -->
<!-- targets: x64-windows -->
The duration is an integer count of milliseconds; a float is refused, by the SAME operand rule the
bare `sleep(1.5)` is refused by (`async-sleep.float-arg-rejected`) — one emit, so the two spellings
cannot drift apart on what they accept.
```maxon
function main() returns ExitCode
	__Builtins.sleep(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:2: '__Builtins.sleep' requires a integer, but its argument is float
```

<!-- test: builtins-sleep.unknown-member-in-statement-position -->
<!-- targets: x64-windows -->
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
error E3004: <fragment>:3:2: call to undefined function '__Builtins.nope': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
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
error E3104: <fragment>:3:2: this construct is x64-windows only at this rung: it lowers to the runtime entry '__gt_sleep', which has no wasm32-wasi implementation
```

<!-- test: builtins-sleep.rejected-on-wasm-when-unreached -->
<!-- targets: wasm32-wasi -->
The gate is REACHABILITY-BLIND for user code: `napper` is never called, yet its `sleep` is still refused.
`SemanticCheck` visits every function and dead-function elimination runs two tiers later, so this is the
same rule that reports a type error in an unreached function. Pinned because the whitelist's own exemption
(`checkCalls` skips an unreached whitelisted stdlib body) points the other way, and the rung that retires
the bare-name `sleep` builtin moves this call INTO such a body — which would silently flip this program
back to compiling.
```maxon
function napper()
	sleep(1)
end 'napper'

function main() returns ExitCode
	return 4
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:2: this construct is x64-windows only at this rung: it lowers to the runtime entry '__gt_sleep', which has no wasm32-wasi implementation
```
