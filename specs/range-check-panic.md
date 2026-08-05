---
feature: range-check-panic
status: experimental
keywords: [range, typealias, panic, runtime, bounds check]
category: runtime
---

# Range Check Panic

## Documentation

A ranged typealias binds every value that meets it. Wherever a value reaches a place declared with
one — a **call argument**, a `return`, a **struct-literal field**, a **field store**, an array-literal
element, an explicit `as` — the declared bounds are enforced.

**How they are enforced depends on what the compiler can know, and the two halves are one rule:**

- **The value is known at compile time** — it traces back to a literal, or to a constants-enum case's
  raw value — and it is a **compile error (E3005)** naming the value, the type and its bounds. This
  applies at **every** position above. A program that provably violates a range never gets built.
- **The value is not known at compile time** — the compiler emits a **runtime range check** where the
  value lands. If it is out of range the program panics, naming the type, with a stack trace.

### Example

```text
typealias Percent = int(0 to 100)

function clamp(x Percent) returns Percent
    return x
end 'clamp'
```

`clamp(101)` is refused at compile time — the 101 is right there. A value the compiler cannot fold
reaches the runtime check at `clamp`'s entry:

```text
Range check failed: value outside typealias 'Percent'
Stack trace:
  in clamp
  in main
  in mrt_start
```

### ⚠ Where the RUNTIME check for a PARAMETER is emitted: at the CALLEE'S ENTRY, not at the call

The runtime half is emitted at a `return`, a struct-literal field, a field store, an array-literal
element and an explicit `as` — at each of those the value comes to rest in the place that declared
the range, so the guard goes where the store goes.

**A call ARGUMENT is enforced at the other end: the callee's own entry**, past the incoming
parameters and before a single body op runs. Two reasons, and neither is available at the call site:

- **Cost** — one guard per parameter per *function*, not one per *call*.
- **Coverage** — an indirect call through a function value has no callee NAME at the call site to
  look a range up by, so only the entry can cover every caller.

The panic therefore names the **parameter's own declaration line**, not the caller's.

**The compile-time half stays at the call site**, where the offending literal actually is: a value
the compiler can fold is refused as an E3005 naming the value and its bounds, and never gets built.
That is why the two halves are *disjoint* rather than duplicated.

### ⚠ A parameter's range is a PREMISE THE BODY IS COMPILED AGAINST, which is why this is a PANIC

`function divide(n Integer, d NonZero)` may write `n / d` with **no `try` and no guard**: the
declared type excludes 0, so the compiler proves the divide safe and emits the bare instruction.
That proof is only as good as the caller's promise. A caller that breaks it is not producing a
recoverable *condition* — it is violating a contract the code it called was compiled against — so a
range violation **panics**, and does not throw. (An unproven divisor is the other case entirely:
that is `E3057`, the author writes `try`, and gets a catchable `DivisionByZero`.)

## Tests

<!-- test: range-check-panic.upper-bound -->
Above the maximum, and not foldable — so `clamp`'s ENTRY guard is what fires, on the line that
declares `x Percent`, and the trace names `clamp`. The `return` never runs.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Percent = int(0 to 100)

function clamp(x Percent) returns Percent
  return x
end 'clamp'

function grow(n Integer) returns Integer
  return n * 101
end 'grow'

function main() returns ExitCode
  let big = grow(1)
  return clamp(big)
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.upper-bound.test:5: Range check failed: value outside typealias 'Percent'
Stack trace:
  in clamp
  in main
  in mrt_start
```

<!-- test: range-check-panic.lower-bound -->
Below the minimum, caught at `check`'s ENTRY. `Natural`'s lower bound is 0, so a negative value is
out of range even though it is a perfectly ordinary `int`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Natural = int(0 to i64.max)

function check(n Natural) returns Natural
  return n
end 'check'

function neg(n Integer) returns Integer
  return 0 - n
end 'neg'

function main() returns ExitCode
  let below = neg(1)
  return check(below)
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.lower-bound.test:5: Range check failed: value outside typealias 'Natural'
Stack trace:
  in check
  in main
  in mrt_start
```

<!-- test: range-check-panic.in-range -->
The half that must keep working: an in-range argument passes the entry guard and returns normally.
```maxon
typealias SmallInt = int(0 to 10)

function check(x SmallInt) returns SmallInt
  return x
end 'check'

function main() returns ExitCode
  return check(5)
end 'main'
```
```exitcode
5
```

<!-- test: range-check-panic.nested-call -->
The stack trace goes as deep as the value does: `process`'s entry guard passes on an in-range
`Score`, `process` computes one that is not, and `validate`'s entry guard is where it stops.
```maxon
typealias Score = int(0 to 100)

function validate(s Score) returns Score
  return s
end 'validate'

function process(x Score) returns Score
  return validate(x * 3)
end 'process'

function main() returns ExitCode
  return process(50)
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.nested-call.test:4: Range check failed: value outside typealias 'Score'
Stack trace:
  in validate
  in process
  in main
  in mrt_start
```

<!-- test: range-check-panic.error.literal-argument -->
The compile-time half at a call argument -- the position that used to let the value through in
silence. `clamp(101)` never reaches a runtime check because it never builds.
```maxon
typealias Percent = int(0 to 100)

function clamp(x Percent) returns Percent
  return x
end 'clamp'

function main() returns ExitCode
  return clamp(101)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/range-check-panic/range-check-panic.error.literal-argument.test:9:10: Value 101 is outside the range of 'Percent' (int(0 to 100))
```

<!-- test: range-check-panic.error.literal-struct-field -->
And at a struct-literal field, which is the same rule at a different place.
```maxon
typealias Percent = int(0 to 100)

type Reading
  export var pct as Percent

  static function create() returns Self
    return Self{pct: 101}
  end 'create'
end 'Reading'

function main() returns ExitCode
  let r = Reading.create()
  return r.pct
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/range-check-panic/range-check-panic.error.literal-struct-field.test:8:17: Value 101 is outside the range of 'Percent' (int(0 to 100))
```

### The premise the callee's body is compiled against

<!-- test: divide-by-zero-premise-enforced-at-the-callee-entry -->
#### ⭐ A runtime zero is refused AT `divide`'S PARAMETER — the divide it would have reached is still bare
`d` excludes 0, so the divide is proven safe and compiles unguarded — no throw, no `try`. The proof
is the CALLER's to keep, and the entry guard is what holds it to it: it fires at line 9, the
parameter's own declaration, before a single body op runs. Without it this program died
`panic: integer divide by zero` — an uncatchable CPU fault where the language promises a catchable
`DivisionByZero`, reported against the divide instead of against the broken promise.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NonZero = int(1 to i64.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function divide(n Integer, d NonZero) returns Integer
	return n / d
end 'divide'

function main() returns ExitCode
	let z = ident(0)
	return divide(10, d: z)
end 'main'
```
```exitcode
1
```
```stderr
panic at divide-by-zero-premise-enforced-at-the-callee-entry.test:9: Range check failed: value outside typealias 'NonZero'
Stack trace:
  in divide
  in main
  in mrt_start
```

<!-- test: mod-overflow-premise-enforced-at-the-callee-entry -->
<!-- targets: x64-windows -->
#### ⭐ A runtime `-1` is refused at `remainder`'S PARAMETER — the twin hazard, one cure

The twin of the case above, through the same door. `BelowMinusOne` rules out BOTH of the integer
remainder's hazardous divisors, so `n mod d` compiles to the bare sequence with no guard and no
`try` spellable — correctly, given the declared type. A caller that breaks the type it declared used
to get the `i64.min mod -1` overflow fault the type was standing in front of; it now gets the range
panic, naming `BelowMinusOne` at the parameter that declared it. **One cure, two hazards** — the
divide-by-zero premise and the `i64.min mod -1` premise were never two defects.

Restricted to `x64-windows` for its shv2 twin's measured reason: the fault this case used to raise
is reported differently per host kernel, and the exclusion is kept rather than re-derived —
re-admitting a target is a measurement, not a guess.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias BelowMinusOne = int(i64.min to -2)

function ident(v Integer) returns Integer
	return v
end 'ident'

function remainder(n Integer, d BelowMinusOne) returns Integer
	return n mod d
end 'remainder'

function main() returns ExitCode
	let bad = ident(0 - 1)
	return remainder(i64.min, d: bad)
end 'main'
```
```exitcode
1
```
```stderr
panic at mod-overflow-premise-enforced-at-the-callee-entry.test:9: Range check failed: value outside typealias 'BelowMinusOne'
Stack trace:
  in remainder
  in main
  in mrt_start
```

<!-- test: runtime-argument-in-range-passes-the-entry-guard -->
The control that keeps the guard honest: the same shape with a divisor the compiler cannot fold and
that is IN range. The entry guard passes, the bare divide runs, and 10 / 5 is 2. A guard that
refused this would be worse than no guard at all.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NonZero = int(1 to i64.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function divide(n Integer, d NonZero) returns Integer
	return n / d
end 'divide'

function main() returns ExitCode
	let ok = ident(5)
	return divide(10, d: ok)
end 'main'
```
```exitcode
2
```
