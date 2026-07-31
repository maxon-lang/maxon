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
reaches the runtime check on the `return`:

```text
Range check failed: value outside typealias 'Percent'
Stack trace:
  in clamp
  in main
  in mrt_start
```

### ⚠ Where the RUNTIME check is emitted, and the one gap

The runtime half is emitted at a `return`, a struct-literal field, a field store, an array-literal
element and an explicit `as`. It is **not** emitted at a call argument: a runtime check needs a
branch, which splits the current block, and doing that part-way through building an argument list
breaks the compiler's argument-pinning pass (an `E9001 … not in valueMap`, see PLAN.md's
bootstrap-oracle-bugs list). So an unfoldable argument is not guarded at the boundary it crosses —
it is guarded wherever it comes to rest, which is what the traces below show.

**The compile-time half at a call argument is unaffected and is the half that was missing**: before
it existed, `takePercent(500)` ran the callee with `p = 500` and the body observed `p > 100` as true.
A declared range was simply not enforced at a call.

## Tests

<!-- test: range-check-panic.upper-bound -->
<!-- targets: x64-windows, x64-linux -->
Above the maximum, and not foldable — so the runtime check on `clamp`'s `return` is what fires, and
the trace names `clamp`.
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
panic at range-check-panic.upper-bound.test:6: Range check failed: value outside typealias 'Percent'
Stack trace:
  in clamp
  in main
  in mrt_start
```

<!-- test: range-check-panic.lower-bound -->
<!-- targets: x64-windows, x64-linux -->
Below the minimum. `Natural`'s lower bound is 0, so a negative value is out of range even though it
is a perfectly ordinary `int`.
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
panic at range-check-panic.lower-bound.test:6: Range check failed: value outside typealias 'Natural'
Stack trace:
  in check
  in main
  in mrt_start
```

<!-- test: range-check-panic.in-range -->
The half that must keep working: an in-range argument costs nothing and returns normally.
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
<!-- targets: x64-windows, x64-linux -->
The stack trace goes as deep as the value does: `process` receives an in-range `Score`, computes one
that is not, and `validate` is where it comes to rest.
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
panic at range-check-panic.nested-call.test:5: Range check failed: value outside typealias 'Score'
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

<!-- test: range-check-panic.runtime-argument -->
<!-- targets: x64-windows, x64-linux -->
A runtime out-of-range argument is refused AT THE PARAMETER, before the callee's body runs. `by`
excludes 0, so `a / by` is a bare divide with no `try` spellable -- the proof the elision rests on is
the parameter's declared range, and this is the check that makes that proof true. The panic names the
line the premise is DECLARED on (`divide`'s parameter list), not the line that wrote the argument: one
guard stands in front of every call, including a call through a function value that has no argument
list to blame.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NonZero = int(1 to i64.max)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function divide(a Integer, by NonZero) returns Integer
  return a / by
end 'divide'

function main() returns ExitCode
  return divide(10, by: opaque(0))
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.runtime-argument.test:9: Range check failed: value outside typealias 'NonZero'
Stack trace:
  in divide
  in main
  in mrt_start
```
