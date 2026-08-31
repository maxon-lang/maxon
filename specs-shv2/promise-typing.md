---
feature: promise-typing
status: stable
keywords: [async, await, promise, green-threads, type, ownership, E3005, E2004, E2015]
category: concurrency
---

# Promises are TYPED at birth

## Documentation

`async f()` mints a promise. That promise is **typed** — `Promise with T`, or `Promise with (T, E)` when
`f` throws — from the moment it is minted, exactly as `stdlib/Builtins.maxon` declares it: an *opaque
handle*, typed by both the value its thunk returns and the error its thunk throws.

It used to be minted as a bare machine word (`int`). The handle IS a machine word, but that is a
representation, not a type, and a value carrying its representation as its type is accepted everywhere
that word is accepted. So `return async work()` satisfied a `returns Integer`, and printed a raw green-
thread pointer; `p + 1` was pointer arithmetic; `p.clone()` handed out a second copy of a handle exactly
one owner may reclaim; and an `Integer` parameter took a promise. None of those was diagnosed, because
by the compiler's own account nothing was wrong.

Typing the promise at birth is what refuses all of them, and it does so through the checks that already
exist rather than a new roster: a promise is not an `Integer`, so every position that wants an `Integer`
already knows how to say no. **The roster of refusals is DERIVED, not enumerated** — which is the point.
There is no list to keep in step with the language.

⚠ **THESE REFUSALS ARE PINNED TO `x64-windows`, AND THE REASON IS THE THUNK RATHER THAN THE RULE.** The
rules themselves are target-neutral — nothing about "a promise is not an `Integer`" depends on a backend.
But an `async` thunk must have a yield point or **E3073** refuses the spawn outright (*"function never
yields"*), and the yield points available at this rung lower to runtime entries `wasm32-wasi` does not
have: `File.exists` reaches `__mf_exists` and raises **E3104** there. Either way the case's own subject is
MASKED by an error about the thunk. Dropping the marker to win the lane simply trades E3104 for E3073 —
measured, both ways — so the case is pinned instead of quietly testing something else.

⚠ **ON THE SPELLINGS IN THESE DIAGNOSTICS.** Two are the compiler's existing renderings rather than
anything this rule chose, and both are worth knowing. A refusal taken at the TAG arm prints the tag word
(`struct`), because at that point the check has compared classes and not names. And an instance no
`typealias` spells renders by its type ARGUMENT's range — `Promise with int(…)` and not `Promise with
Integer` — which is what `rangeRenderedInstanceName` does for every alias-less instance, on the ground that
the alias is exactly the thing that was ambiguous. Declare `typealias IntPromise = Promise with Integer` and
the diagnostics say `IntPromise`.

The one sanctioned promise → `int` conversion is **`p.inner`** (see `promise-peek.md`): a non-blocking
peek at the handle word, which reads the promise without consuming it.

## Tests

<!-- test: promise-typing.error.return-a-promise-as-its-result-type -->
<!-- targets: x64-windows -->
A promise is not its result. `grab` is declared `returns Integer` and returns `async plain()`, which is a
`Promise with Integer` — the value that will eventually produce an `Integer`, not an `Integer`. Before
promises were typed this compiled and printed the green thread's raw address (a different number on every
run), which is the wrong answer this whole slice exists to stop.
```maxon
typealias Integer = int(i64.min to i64.max)

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function grab() returns Integer
		return async plain()
end 'grab'

function main() returns ExitCode
		print("grabbed {grab()}")
		return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:3: Cannot return 'struct' from function declared to return 'int'
```

<!-- test: promise-typing.error.arithmetic-on-a-promise -->
<!-- targets: x64-windows -->
A promise is not a number, so it has no arithmetic. `p + 1` used to be pointer arithmetic on a green-
thread address that happened to compile.
```maxon
typealias Integer = int(i64.min to i64.max)

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		let p = async plain()
		let bumped = p + 1
		print("bumped {bumped}")
		return (await p) as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:11:18: Cannot operate on struct and int
```

<!-- test: promise-typing.error.a-promise-in-an-integer-parameter -->
<!-- targets: x64-windows -->
An `Integer` parameter does not take a promise. The cure is to `await` it and pass the RESULT — which is
also the only spelling that keeps the thread's one owner intact.
```maxon
typealias Integer = int(i64.min to i64.max)

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function takesInt(n Integer) returns Integer
		return n
end 'takesInt'

function main() returns ExitCode
		let p = async plain()
		return takesInt(p) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:10: argument type mismatch for 'n': expected 'Integer', got 'Promise with int(-9223372036854775808 to 9223372036854775807)'
```

<!-- test: promise-typing.error.clone-a-promise -->
<!-- targets: x64-windows -->
⭐ The one that was a latent double-reclaim rather than merely a wrong type. A promise owns a green
thread that exactly one owner may reclaim; `p.clone()` used to hand back a second copy of the handle,
with nothing to say which of the two owned the thread. `Promise` declares no `clone`, and synthesizing
one is refused at the receiver.
```maxon
typealias Integer = int(i64.min to i64.max)

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		let p = async plain()
		let copy = p.clone()
		print("copied {copy.inner > 0}")
		return (await p) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:11:16: Unsupported: `clone` on `Promise`, which is a GENERIC type — a clone must be minted per INSTANCE (a `Promise with String` and a `Promise with int` copy different things), and this compiler mints one per declared type only, so the copy would alias the type parameter's value instead of cloning it. Write a `clone` method on `Promise` that rebuilds it.
```

<!-- test: promise-typing.inner-is-the-one-unwrap -->
<!-- targets: x64-windows -->
The sanctioned promise → `int` conversion. `.inner` peeks at the handle word without consuming the
promise, so the `await` that follows still reclaims the thread and the program still balances to zero.
```maxon
typealias Integer = int(i64.min to i64.max)

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		let p = async plain()
		let named = p.inner > 0
		print("names a thread {named}")
		return (await p) as ExitCode
end 'main'
```
```stdout
names a thread true
```
```exitcode
7
```
