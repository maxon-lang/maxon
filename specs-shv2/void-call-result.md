---
feature: void-call-result
status: stable
keywords: [void, functions, call, statement, type-mismatch]
category: diagnostics
---

# Using the Result of a Void Call

## Documentation

A function that declares no return type returns **nothing**. There is no value, so there is nothing
to use:

```text
function noop()
	return
end 'noop'

let x = noop()      // error: Function 'noop' does not return a value
```

The grammar has exactly two positions a call can appear in, and they differ by precisely this:

- a **bare-call statement** — `noop()` on a line of its own. The call is evaluated for its effect
  and its result discarded. A void callee is what this position is *for*.
- a call inside an **expression** — `let x = f()`, `f() + 1`, `if f()`, `g(f())`. The result *is*
  the expression. A void callee has nothing to give here, and the program is rejected.

### Why this was a wrong answer, and what it has to do with cross-file calls

It is the same defect as the cross-file one, wearing different clothes: **one sentinel, two
meanings.** The parser reported *both* "I could not see this callee" *and* "this callee returns
nothing" as `unresolved` — and `unresolved` is the tag that **agrees with everything**, because
deferring is the right thing to do about a type you cannot know. So "there is no value" was
classified as "I cannot judge this value", every type rule correctly deferred on it, and

```text
let x = noop()
return x + 4
```

compiled — returning whatever happened to be in the return register.

The deferral was never the defect. Giving "no value" its own tag is the fix, and the two meanings
can never be confused again.

## Tests

<!-- test: void-result-in-binding -->
```maxon
function noop()
	return
end 'noop'

function main() returns ExitCode
	let x = noop()
	return x + 4
end 'main'
```
```maxoncstderr
error E2004: <fragment>:7:10: Function 'noop' does not return a value
```

<!-- test: void-result-in-arithmetic -->
```maxon
function noop()
	return
end 'noop'

function main() returns ExitCode
	return noop() + 4
end 'main'
```
```maxoncstderr
error E2004: <fragment>:7:9: Function 'noop' does not return a value
```

<!-- test: void-result-in-condition -->
```maxon
function noop()
	return
end 'noop'

function main() returns ExitCode
	if noop() 'branch'
		return 1
	end 'branch'
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:7:5: Function 'noop' does not return a value
```

<!-- test: void-result-as-argument -->
```maxon
typealias Integer = int(i64.min to i64.max)

function noop()
	return
end 'noop'

function takeInt(n Integer) returns Integer
	return n
end 'takeInt'

function main() returns ExitCode
	return takeInt(noop())
end 'main'
```
```maxoncstderr
error E2004: <fragment>:13:17: Function 'noop' does not return a value
```

<!-- test: void-result-returned -->
```maxon
function noop()
	return
end 'noop'

function main() returns ExitCode
	return noop()
end 'main'
```
```maxoncstderr
error E2004: <fragment>:7:9: Function 'noop' does not return a value
```

<!-- test: cross-file-void-result -->
A void callee in ANOTHER file is the same error, and it is the case the two-meaning sentinel hid
best: the parser could see neither the callee nor its voidness, and reported the same `unresolved`
for both.
```maxon
// --- file: a.maxon
export function noop()
	return
end 'noop'

// --- file: b.maxon
function main() returns ExitCode
	let x = noop()
	return x + 4
end 'main'
```
```maxoncstderr
error E2004: <fragment>:9:10: Function 'noop' does not return a value
```

<!-- test: void-call-statement-is-legal -->
⚠ THE OVER-REJECTION GUARD. A void call in STATEMENT position is exactly what that position is for,
and it must keep compiling — the check is about the RESULT being used, not about the call.
```maxon
function noop()
	return
end 'noop'

function main() returns ExitCode
	noop()
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: value-call-result-may-still-be-discarded -->
A value-returning call in statement position keeps its existing behaviour: the result is discarded,
and that is not this error.
```maxon
typealias Integer = int(i64.min to i64.max)

function answer() returns Integer
	return 42
end 'answer'

function main() returns ExitCode
	answer()
	return 42
end 'main'
```
```exitcode
42
```
