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
brings into every compile, unmodified, so that a source-level `sleep(ms)` is a call to ITS
declaration rather than to a name the compiler claims (`async-sleep.md`):

```text
export function sleep(milliseconds Milliseconds)
	__Builtins.sleep(milliseconds)
end 'sleep'
```

`__Builtins.sleep(ms)` suspends the current green thread for `ms` milliseconds. It is STDLIB's OWN
FLOOR — the one spelling that is recognized by name, because the module's body has to bottom
out somewhere — and it takes exactly one INTEGER argument and returns VOID. A bare `sleep(ms)` is a
call to the stdlib declaration, and the argument and result rules a source call meets are that
declaration's.

### A VOID intrinsic needs a STATEMENT position, and that is what the other three never asked for

`currentTimeNanos`, `currentTimeMs` and `currentUnixTimeSeconds` all RETURN a value, so every call to
one arrives in expression position. `sleep` returns nothing, so it is written on a line of its own —
and a qualified call is otherwise not a statement:

```text
error E2015: <file>:4:2: Unsupported: identifier statement
```

A STATIC call is otherwise deliberately not a statement (`Point.create()` on a line of its own
discards a box nothing would then free), but that reason does not reach an intrinsic: it allocates
nothing, and the `__Builtins` table is what decides whether its result exists at all. So the
statement door recognizes `__Builtins.<member>(…)` and nothing else of that shape.

The door admits a shape and no NAME. A member the table does not recognize falls through to the same
reserved-callee rejection expression position already gives it — `E3004`, at the call's own span.

### Every lane serves it

`sleep` lowers to the runtime entry `__gt_sleep`, which every lane implements. On a native lane the
green-thread timer computes its deadline from the host's monotonic counter and parks the green thread
until it passes. wasm32-wasi has no green threads, so there a sleep blocks the program on a WASI
`monotonic-clock` pollable for the requested duration (`runs-on-wasm` below).

A target refusal of a runtime entry is a SEMANTIC CHECK on the program's own source, so it does not care
whether the call is reachable; `builtins-clock.md`'s `thread-cpu-ticks-rejected-on-wasm-when-unreached`
pins that with an entry wasm still refuses.

Arity, operand type, value position and unknown member are decided in the front end, are target-neutral,
and carry NO marker.

Every `__Builtins` member is library-tier: only a source file under `stdlib/` or `runtime/` may call one,
and a call from anywhere else is E3185. The cases below call the intrinsics themselves, so they compile in
the library tier (`<!-- tier: stdlib -->`). A program calls `sleep(ms)`.

## Tests

<!-- test: builtins-sleep.statement-position -->
<!-- tier: stdlib -->
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
		return 7
	end 'slept'
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: builtins-sleep.value-position-rejected -->
<!-- tier: stdlib -->
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
<!-- tier: stdlib -->
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
<!-- tier: stdlib -->
The duration is an integer count of milliseconds; a float is refused, by the SAME operand rule the
bare `sleep(1.5)` is refused by (`async-sleep.float-arg-rejected`) — one emit, so the two spellings
cannot drift apart on what they accept. ⚠ The two differ in WHICH refusal arrives FIRST off
x64-windows, which is why only this one is un-gated: the qualified `__Builtins.sleep` form reaches the
operand check, while the bare `sleep` name is a stdlib call the E3104 target gate refuses
before the operand is ever typed.
```maxon
function main() returns ExitCode
	__Builtins.sleep(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:13: '__Builtins.sleep' requires an integer, but its argument is float
```

<!-- test: builtins-sleep.unknown-member-in-statement-position -->
<!-- tier: stdlib -->
The statement door admits a SHAPE, not a name list: an unrecognized `__Builtins` member written on a
line of its own reaches the same reserved-callee rejection it gets in expression position, rather than
the shape-level `E2015`.
```maxon
function main() returns ExitCode
	__Builtins.nope()
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:13: call to undefined function '__Builtins.nope': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: builtins-sleep.runs-on-wasm -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
On wasm32-wasi a sleep blocks on a WASI `monotonic-clock` pollable, so the monotonic clock has moved at
least the requested 20 ms when it returns.
```maxon
function main() returns ExitCode
	let start = __Builtins.currentTimeNanos()
	__Builtins.sleep(20)
	if __Builtins.currentTimeNanos() - start >= 20000000 'sleptTheWholeTime'
		return 4
	end 'sleptTheWholeTime'
	return 1
end 'main'
```
```exitcode
4
```
