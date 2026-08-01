---
feature: range-check-panic
status: experimental
keywords: [range, typealias, panic, runtime, bounds check]
category: runtime
---

# Range Check Panic

## Documentation

A ranged typealias binds every value that meets it. Wherever a value reaches a place declared with
one — a **call argument**, a `return`, a **struct-literal field**, a **field store**, a field's
**declared default**, an **array element**, an explicit `as` — the declared bounds are enforced.

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
reaches a runtime check — since A1f, `clamp`'s ENTRY guard on the parameter, before its body runs:

```text
Range check failed: value outside typealias 'Percent'
Stack trace:
  in clamp
  in main
  in mrt_start
```

### ⚠ Where the RUNTIME check is emitted — and WHERE a call argument's lands (A1f)

The runtime half is emitted at **every** position: a `return`, a struct-literal field, a field store, a
field's declared default, an explicit `as`, an **array element** (since `A1f-arrayelem`) and a **call
argument** (since A1f). There is no position left owing the compile-time half alone.

**A call argument's runtime check is not emitted where the argument is written**, and the obstacle is
mechanical: a check is a BRANCH, so it splits the block it lands in, and an argument is evaluated
part-way through building an argument list — a guard placed there lands PAST the call, with the callee
already run on the value the range forbids. A1f did not defeat that obstacle; it moved the guard. The
runtime half of the argument door belongs to the **callee's entry**: one guard per narrowed parameter
per function, emitted once, standing in front of every caller.

Three consequences follow, and all three are pinned below:

- **The panic names the PARAMETER's declaration line, not the caller's.** One guard serves every call
  site, so a caller's line is not a fact it holds. The parameter list is where the premise is declared.
- **It covers callers a call-site rule structurally could not** — a call through a function value has
  no callee name at the call site to look a parameter's range up by.
- **A guarded leaf function stops being a leaf**, because the panic block calls `mrt_panic`. That is a
  per-FUNCTION cost on the in-range path, not an instruction count on the failing one.

**The compile-time half at a call argument is unchanged and still fires first**: `clamp(101)` is
refused by E3005 and never builds, so no entry guard ever runs for it. A parameter whose range is
**full** promises nothing and gains nothing — no guard, no frame, byte-identical codegen.

### An ARRAY ELEMENT goes the other way, and the difference is where the ALIAS is known (A1f-arrayelem)

An element travels as `__arr_push`/`__arr_set`/`__arr_insert`'s third argument into a shared `Array`
body whose parameter is the OPAQUE element type, so A1f's callee-entry cure has no narrowed parameter to
stand behind. But the argument obstacle above never applied to it either: an element's alias is right
there in the instance's declared element type, at parse time, so it records an ordinary positioned site
and its guard goes at the **store**, immediately in front of the `__arr_*` call.

⚠ **What is left unenforced is not a position at all** — `Array.resize` GROWS an array by *exposing*
zero-initialized slots, so an `Array with NonZero` can acquire a `0` with no value crossing any door.
`specs-shv2/safety.md`'s `divide-by-zero-fault-through-a-resized-array-slot` pins it.

### ⭐ A `return` of a guarded parameter is ALREADY guarded — and the elision has to earn that (A1f-dupguard)

`function f(x T) returns T … return x` used to emit the entry cascade and an identical return cascade
over the same value against the same alias. The second is now elided, and because this whole mechanism
exists *because* an elision rested on a premise nothing enforced, the premise is stated rather than
assumed. **All four clauses are required, and each one is what a case below breaks:**

1. **The same VALUE.** SSA numbers a value once, and a parameter's `ValueId` is defined by its `param`
   op. A `var` shadow reassigned before the `return` is a *different* id, and a parameter cannot be
   mutated at all (E2013) — so "this `return` returns that parameter" is an identity, not an inference.
2. **The same ALIAS.** The parameter's alias name and the function's return alias name are looked up
   through the *same* file (`FunctionRangeChecks.filePath`), so equal names are the same declaration and
   therefore the same bounds.
3. **DOMINANCE.** The entry guard splits the entry block at the end of its `param` ops — before a single
   body op — so every path to every `return` passes through it. A `return` inside a branch is covered
   for free, and there is no path that reaches a `return` without it.
4. **THE ENTRY GUARD ACTUALLY EXISTS.** Both sites ask one decision function (`needsRuntimeGuard`) with
   the same value and the same alias, and the return site asks it *first*: it reaches the elision only
   on the answer that made the entry guard fire. A full-range alias, or a range whose bounds both elide,
   emits nothing at either site.

Break any of 1, 2 or 4 and the return guard is emitted exactly as before. That is what
`return-of-a-reassigned-shadow-is-still-guarded`, `return-through-a-narrower-alias-is-still-guarded` and
`return-of-a-computed-value-is-still-guarded` pin; `return-of-a-second-ranged-parameter-is-covered-by-its-entry-guard`
and `entry-guard-covers-a-return-inside-a-branch` pin the elision itself, the first by keying on the
VALUE (not on "is there a ranged parameter") and the second on clause 3.

## Tests

<!-- test: range-check-panic.upper-bound -->
<!-- targets: x64-windows, x64-linux -->
Above the maximum, and not foldable — so a runtime check is what fires, and the trace names `clamp`.
⭐ Since A1f the guard is `clamp`'s ENTRY guard, at the parameter's own line (5), not the one on its
`return` (6): the value was already outside `Percent` when it crossed the boundary, and the parameter
list is where that premise is declared.
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
<!-- targets: x64-windows, x64-linux -->
Below the minimum. `Natural`'s lower bound is 0, so a negative value is out of range even though it
is a perfectly ordinary `int` — and, as above, `check`'s entry guard is what refuses it, at the
parameter's line.
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
The half that must keep working: an in-range argument passes every guard and returns normally.
⭐ Its fragment is also where the `A1f-dupguard` elision is visible — `check`'s entry guard is now the
ONLY cascade in the function, where it used to be followed by an identical `return` cascade over the
same `ValueId` against the same alias, with a second panic block and a second `.rdata` blob. See the
Documentation above for the four clauses that elision rests on.
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
that is not, and `validate` is where it is refused — at `validate`'s parameter (line 4), the boundary
the bad value crosses first.
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

<!-- test: range-check-panic.full-range-argument-gains-no-guard -->
⭐ **THE CONTROL THAT KEEPS THE COST HONEST.** `Integer` spans the whole of `i64`, so it forbids
nothing — and a parameter that promises nothing must gain nothing. `rangeIsFull` discards it before a
cascade is ever built, so `passthrough` stays a LEAF: no `__rc_ok`, no `__rc_panic`, no `mrt_panic`
call, and therefore no frame. Its committed fragment is the proof, and it is the reason the entry
guard is a cost only where a range is genuinely narrowed.
```maxon
typealias Integer = int(i64.min to i64.max)

function passthrough(n Integer) returns Integer
  return n + 1
end 'passthrough'

function main() returns ExitCode
  return passthrough(6)
end 'main'
```
```exitcode
7
```

<!-- test: range-check-panic.float-argument -->
<!-- targets: x64-windows, x64-linux -->
⭐ A narrowed FLOAT parameter is guarded on exactly the same terms as an integer one. Shipping the
integer half alone would have replaced *five doors of which four guard* with *parameters of which only
the int ones guard* — a fresh instance of the very asymmetry the entry guard exists to delete. The
cascade is the f64 one `emitGuardAt` already forks for the `as` and `return` doors; nothing about a
parameter is special.
```maxon
typealias Ratio = float(0.0 to 1.0)

function widen(x float) returns float
  return x * 4.0
end 'widen'

function scale(r Ratio) returns float
  return r * 100.0
end 'scale'

function main() returns ExitCode
  let big = widen(0.5)
  return scale(big) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.float-argument.test:8: Range check failed: value outside typealias 'Ratio'
Stack trace:
  in scale
  in main
  in mrt_start
```

<!-- test: range-check-panic.error.literal-argument-into-a-divisor -->
⭐ **THE COMPILE-TIME HALF STILL FIRES FIRST, AND IT IS STRICTLY BETTER THAN THE RUNTIME ONE.** `divide`
now carries an entry guard, but a literal argument never reaches it: E3005 refuses the program at the
line that wrote the `0`, naming the value, the type and its bounds — a caller-anchored diagnostic the
one shared entry guard structurally cannot give. The two halves coexist; adding the runtime one did not
displace the compile-time one.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NonZero = int(1 to i64.max)

function divide(a Integer, by NonZero) returns Integer
  return a / by
end 'divide'

function main() returns ExitCode
  return divide(10, by: 0)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/range-check-panic/range-check-panic.error.literal-argument-into-a-divisor.test:10:10: Value 0 is outside the range of 'NonZero' (int(1 to 9223372036854775807))
```

<!-- test: range-check-panic.entry-guard-on-a-parameter-no-body-reads -->
<!-- targets: x64-windows, x64-linux -->
⭐⭐ **THE GUARD IS NOT DEAD CODE, EVEN WHEN NOTHING BUT THE PREMISE CONSUMES IT.** `unused`'s body never
mentions `d`, so the only thing the entry guard establishes is *the premise itself* — and a guard whose
sole consumer is a premise is exactly what a naive dead-value pass deletes. That is not a hypothetical:
the whole point of this mechanism is that the divide prover TRUSTS a `NonZero` parameter, so a pass that
elided the guard on the grounds that nothing reads the value would silently restore the bug A1f closed,
with every existing case still green. Pinned so `pruneDeadBlockArgs` / `elimTrivialBlockArgs` /
`foldConstOperands` — and anything later that walks uses — has a test standing in front of it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NonZero = int(1 to i64.max)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function unused(d NonZero) returns Integer
  return 7
end 'unused'

function main() returns ExitCode
  return unused(opaque(0)) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.entry-guard-on-a-parameter-no-body-reads.test:9: Range check failed: value outside typealias 'NonZero'
Stack trace:
  in unused
  in main
  in mrt_start
```

<!-- test: range-check-panic.argument-through-a-function-value-is-guarded -->
<!-- targets: x64-windows, x64-linux -->
⭐⭐ **THE CLAIM THE CALLEE-ENTRY DESIGN RESTS ON, AND THE ONLY CASE THAT CAN TEST IT.** The reason the
guard belongs to the callee rather than the call is that a call-site rule structurally cannot reach every
caller: a call through a function VALUE has no callee name at the call site to look a parameter's range
up by, so `callIndirect` / `witnessCall` would have stayed open for ever. Here the out-of-range `0`
crosses the boundary through `fn`, and the trace shows the whole route — `main` → `apply` →
`__fnref_divide` → `divide` — with the guard firing inside `divide`, where the range is declared. Nothing
at any of those three call sites knows the word `NonZero`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NonZero = int(1 to i64.max)
typealias DivFn = function(Integer, NonZero) returns Integer

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function divide(a Integer, b NonZero) returns Integer
  return a / b
end 'divide'

function apply(f DivFn, a Integer, b Integer) returns Integer
  return f(a, b)
end 'apply'

function main() returns ExitCode
  let fn = divide
  return apply(fn, a: 10, b: opaque(0)) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.argument-through-a-function-value-is-guarded.test:10: Range check failed: value outside typealias 'NonZero'
Stack trace:
  in divide
  in __fnref_divide
  in apply
  in main
  in mrt_start
```


<!-- test: range-check-panic.return-of-a-reassigned-shadow-is-still-guarded -->
<!-- targets: x64-windows, x64-linux -->
⚠ **CLAUSE 1 BROKEN: the returned value is not the parameter any more.** `y` starts as `x` — which mints
no op, so at that point it IS the parameter's `ValueId` — and is then REASSIGNED, which rebinds it to the
call's result. A `return` guard elided on "the function has a ranged parameter" rather than on the
VALUE would let `99` out through a `SmallInt`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias SmallInt = int(0 to 10)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function check(x SmallInt) returns SmallInt
  var y = x
  y = opaque(99)
  return y
end 'check'

function main() returns ExitCode
  return check(5)
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.return-of-a-reassigned-shadow-is-still-guarded.test:12: Range check failed: value outside typealias 'SmallInt'
Stack trace:
  in check
  in main
  in mrt_start
```


<!-- test: range-check-panic.return-through-a-narrower-alias-is-still-guarded -->
<!-- targets: x64-windows, x64-linux -->
⚠ **CLAUSE 2 BROKEN: same value, DIFFERENT alias.** `x` is a `Wide` the entry guard admits and a `Narrow`
the return must refuse. An elision keyed on "the returned value is a ranged parameter" — without
comparing the alias NAMES — would return `50` through a type declared `int(0 to 10)`.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10)

function narrow(x Wide) returns Narrow
  return x
end 'narrow'

function main() returns ExitCode
  return narrow(50)
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.return-through-a-narrower-alias-is-still-guarded.test:6: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in narrow
  in main
  in mrt_start
```


<!-- test: range-check-panic.return-of-a-computed-value-is-still-guarded -->
<!-- targets: x64-windows, x64-linux -->
⚠ **CLAUSE 1 BROKEN THE OTHER WAY: the value never was the parameter.** `x * 3` is a `binOp` result, so
the entry guard says nothing about it — the parameter it was computed FROM was in range, and the result
is not.
```maxon
typealias SmallInt = int(0 to 10)

function grow(x SmallInt) returns SmallInt
  return x * 3
end 'grow'

function main() returns ExitCode
  return grow(5)
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.return-of-a-computed-value-is-still-guarded.test:5: Range check failed: value outside typealias 'SmallInt'
Stack trace:
  in grow
  in main
  in mrt_start
```


<!-- test: range-check-panic.return-of-a-second-ranged-parameter-is-covered-by-its-entry-guard -->
⭐ **THE ELISION ITSELF, and the case that says it keys on the VALUE.** `pick` returns its SECOND ranged
parameter, so an elision that asked "does this function have a ranged parameter" and an elision that
asked "is this value parameter 0" would both answer differently from the right one. Its FRAGMENT is the
evidence: two entry cascades, one per parameter, and NONE at the `return`.
```maxon
typealias SmallInt = int(0 to 10)

function pick(a SmallInt, b SmallInt) returns SmallInt
  return b
end 'pick'

function main() returns ExitCode
  return pick(1, b: 7)
end 'main'
```
```exitcode
7
```


<!-- test: range-check-panic.entry-guard-covers-a-return-inside-a-branch -->
<!-- targets: x64-windows, x64-linux -->
⭐ **CLAUSE 3, which is the one that cannot be broken by writing a program.** The entry guard splits the
entry block before any body op, so it DOMINATES every `return` — including one inside a branch, whose own
guard is elided. `99` is refused at `pick`'s parameter line before the `if` is even evaluated, which is
what a dominating guard means: there is no path to that `return` that skips it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias SmallInt = int(0 to 10)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function pick(x SmallInt, flag Integer) returns SmallInt
  if flag > 0 'yes'
    return x
  end 'yes'
  return 3
end 'pick'

function main() returns ExitCode
  return pick(opaque(99), flag: 1)
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.entry-guard-covers-a-return-inside-a-branch.test:9: Range check failed: value outside typealias 'SmallInt'
Stack trace:
  in pick
  in main
  in mrt_start
```
