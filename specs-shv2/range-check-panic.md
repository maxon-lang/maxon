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

The runtime half is now enforced for **every** position: a `return`, a struct-literal field, a field
store, a field's declared default, an explicit `as`, an **array element** (since `A1f-arrayelem`) and a
**call argument** (since A1f).

⚠ **"Enforced for every position" is not "emitted at every position", and the CALL ARGUMENT is the one
that keeps them apart.** Its guard is emitted at the callee's ENTRY, not where the argument is written,
so a call argument still owes the compile-time half *alone at its own site* — that is what
`RangeCheckGuard.compileTimeOnly` names, it is still passed (by `checkArgAgainstParamRange` and by
nothing else), and `checkArgAgainstParamRange` panics if the shared decider ever answers `guard` there.
Read this paragraph before concluding the enum has no live case.

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

An element travels as `__managed_push`/`__managed_set`/`__managed_insert`'s third argument into a shared `Array`
body whose parameter is the OPAQUE element type, so A1f's callee-entry cure has no narrowed parameter to
stand behind. But the argument obstacle above never applied to it either: an element's alias is right
there in the instance's declared element type, at parse time, so it records an ordinary positioned site
and its guard goes at the **store**, immediately in front of the `__managed_*` call.

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

### ⭐⭐ A value the destination PROVABLY ADMITS gets no check (A4f)

A `Byte = int(0 to u8.max)` cast to `ExitCode` cannot be outside `ExitCode`'s range on **any** target —
`int(0 to u32.max)` on Windows *strictly* contains `0 to 255`, and `int(0 to 255)` on Linux, macOS and
WASI *is* `0 to 255`, which is contained in itself. The cast is REQUIRED — a `Byte` is not an `ExitCode`,
and `as` is the one door between two aliases (`nominal-typealias.md`) — and **no range check is emitted
for it**: the containment is a **proof** that the source range lies inside the destination, and a runtime
bounds cascade behind that proof tests a value that cannot fail it. E3010 is a different question, asked
of the NAMES: it refuses a cast to the value's own alias, and never one between two aliases whatever
their ranges.

**The rule is the containment relation and nothing else** — *emit nothing when the source's declared range
is inside the destination's* — asked through one predicate (`TypeRules.rangeCoversRange`). It is not a
rule about `ExitCode`, about builtins, or about which alias is "wide":

- **Every door asks it**, because every door holds both ends: a `return`, an explicit `as`, a
  struct-literal field, a field store, a field's declared default, an array element.
- **A source that names no alias proves nothing**, and that is the direction that keeps the check: a bare
  `int` local, a folded literal, a `trunc` result. So the **compile-time E3005 half is untouched** —
  `InsertRangeChecks` reports it only for a value it can fold, and a folded literal denotes no alias.
- **Equal ranges are contained.** `returns ExitCode` returning an `ExitCode` call result emits nothing.
- **Anything else still guards, and still panics** — that is `A1f`'s whole mechanism, and this elision
  removes none of it.

⚠ **WHAT THE ELISION RESTS ON, stated rather than assumed** (the same discipline the four clauses above
are written in): *a value denoting alias `A` is in `A`'s range*. Every door in this spec maintains it — a
cast guards its result, a parameter is guarded at the callee's entry, a `return` before its `ret`, a field
or element at its store. The one producer that does not is **`Array.resize`**, which *exposes*
zero-initialized slots crossing no door at all (see the array-element section above, and `safety.md`'s
`divide-by-zero-fault-through-a-resized-array-slot`). A wider door downstream used to catch such a slot
**by coincidence** — it fires only when the exposed `0` happens to fall outside the *second* range too —
and a coincidence is not a guarantee an elision may be written against. **That hole is `resize`'s to
close, and closing it is what makes this premise total.**

### ⛔⛔ A guard runs on the PATH ITS SITE IS ON — and a ternary's TRUE ARM moves after it is parsed

The runtime half of every door is emitted **in the block the value is checked in**, so a cast in a branch
that is not taken does not fire. A guard site is therefore a POSITION — a block plus an ordinal — and not
just a value: a narrowing `int`→`int` cast emits no op of its own, so there is nothing else to anchor to.

**One construct invalidates such a position after it is recorded, and it is the inline conditional.** The
true arm of `<t> if <c> else <f>` is written *before* the `if` announces the form, so the parser emits it
onto the unconditional path and then **relocates** it into a `ternarytrue` block only one edge reaches. A
guard site recorded while that arm parsed names a block the value has left. Both halves of the resulting
mistake are pinned below:

- **The value is merely READ in the arm** (a parameter, anything from a dominating block): nothing is out
  of order, so the cascade simply runs on **both** edges. `(i as Small) if flag else 7` panicked for an
  out-of-range `i` with `flag` FALSE, where the bootstrap prints `7`.
- **The value is COMPUTED in the arm**: the cascade is emitted in a block that DOMINATES the definition it
  reads, which is a use above its def. Liveness carries the operand out of the entry block and the
  register allocator refuses the function — *"value N is live-in to block 0 but was never colored"*. This
  is what stopped shv2 compiling ITSELF the day `stdlib/Array.maxon`'s `ElementIndex` narrowed and put a
  guard on every computed array index.

**The cure is that the arm's SITES move with the arm's OPS** (`Parser.rehomeArmRangeSites`, called from
`parseTernaryExpression` where `detachOpRefsAbove`'s ops are re-attached), which is the relocation's own
arithmetic and no new decision: an op that stood at position `p` of the start block stands at `p - mark`
of the continuation. It is not a rule about ternaries in `InsertRangeChecks` — that pass never learns the
arm existed, and could not: the only frame that knows which ops moved is the one that moved them.

## Tests

<!-- test: range-check-panic.upper-bound -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
  return clamp(big as Percent) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.upper-bound.test:15: Range check failed: value outside typealias 'Percent'
Stack trace:
  in main
  in mrt_start
```

<!-- test: range-check-panic.lower-bound -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
  return -n
end 'neg'

function main() returns ExitCode
  let below = neg(1)
  return check(below as Natural) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.lower-bound.test:15: Range check failed: value outside typealias 'Natural'
Stack trace:
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
  return ((a as NonZero) / by) as Integer
end 'divide'

function main() returns ExitCode
  return divide(10, by: opaque(0) as NonZero) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.runtime-argument.test:14: Range check failed: value outside typealias 'NonZero'
Stack trace:
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⭐ A narrowed FLOAT parameter is guarded on exactly the same terms as an integer one. Shipping the
integer half alone would have replaced *five doors of which four guard* with *parameters of which only
the int ones guard* — a fresh instance of the very asymmetry the entry guard exists to delete. The
cascade is the f64 one `emitGuardAt` already forks for the `as` and `return` doors; nothing about a
parameter is special.
```maxon
typealias Ratio = float(0.0 to 1.0)

function widen(x Real) returns Real
  return x * 4.0
end 'widen'

function scale(r Ratio) returns Real
  return r * 100.0
end 'scale'

function main() returns ExitCode
  let big = widen(0.5)
  return trunc(scale(big))
end 'main'
typealias Real = float(f64.min to f64.max)
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
  return ((a as NonZero) / by) as Integer
end 'divide'

function main() returns ExitCode
  return divide(10, by: 0)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/range-check-panic/range-check-panic.error.literal-argument-into-a-divisor.test:10:10: Value 0 is outside the range of 'NonZero' (int(1 to 9223372036854775807))
```

<!-- test: range-check-panic.entry-guard-on-a-parameter-no-body-reads -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⭐⭐ **THE GUARD IS NOT DEAD CODE, EVEN WHEN NOTHING BUT THE PREMISE CONSUMES IT.** `unused`'s body never
mentions its parameter — it is spelled `_`, the ignore name, because a parameter no body reads is exactly
what this case is about and E3012 refuses any other spelling of it — so the only thing the entry guard
establishes is *the premise itself* — and a guard whose
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

function unused(_ NonZero) returns Integer
  return 7
end 'unused'

function main() returns ExitCode
  return unused(opaque(0) as NonZero) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.entry-guard-on-a-parameter-no-body-reads.test:14: Range check failed: value outside typealias 'NonZero'
Stack trace:
  in main
  in mrt_start
```

<!-- test: range-check-panic.argument-through-a-function-value-is-guarded -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
  return ((a as NonZero) / b) as Integer
end 'divide'

function apply(f DivFn, a Integer, b Integer) returns Integer
  return f(a, b as NonZero)
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
panic at range-check-panic.argument-through-a-function-value-is-guarded.test:15: Range check failed: value outside typealias 'NonZero'
Stack trace:
  in apply
  in main
  in mrt_start
```


<!-- test: range-check-panic.return-of-a-reassigned-shadow-is-still-guarded -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
  y = opaque(99) as SmallInt
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
panic at range-check-panic.return-of-a-reassigned-shadow-is-still-guarded.test:11: Range check failed: value outside typealias 'SmallInt'
Stack trace:
  in check
  in main
  in mrt_start
```


<!-- test: range-check-panic.return-through-a-narrower-alias-is-still-guarded -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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

function pick(_ SmallInt, b SmallInt) returns SmallInt
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
  return pick(opaque(99) as SmallInt, flag: 1) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.entry-guard-covers-a-return-inside-a-branch.test:17: Range check failed: value outside typealias 'SmallInt'
Stack trace:
  in main
  in mrt_start
```


<!-- test: range-check-panic.a-contained-return-emits-no-guard -->
⭐ **THE A4f REPRODUCER.** `pick` returns a `Byte`, `main` returns an `ExitCode`, and `int(0 to 255)` is
inside `ExitCode`'s range on every target — strictly, under Windows' `int(0 to u32.max)`; as an equal
range, under the `int(0 to 255)` Linux, macOS and WASI carry — so the `as ExitCode` that `main`'s
`return` owes gets nothing on any of them. Its FRAGMENT is the evidence: `pick` keeps its own cascade
(the `Integer` it computes from is NOT inside `Byte`, so that one is earned), and `main` is a `bl` and a
`ret`.
```maxon
typealias Byte = int(0 to u8.max)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function pick() returns Byte
  return opaque(7)
end 'pick'

function main() returns ExitCode
  return pick()
end 'main'
```
```exitcode
7
```


<!-- test: range-check-panic.an-identical-range-return-emits-no-guard -->
The boundary of the rule: the two ranges are the SAME range, which is contained in itself. Every `main`
in the corpus that returns an `ExitCode`-returning call is this program, and every one of them used to
carry a full bounds cascade against a value that had just passed the identical cascade one frame down.
The rule is containment, so it does not care *which* range `ExitCode` carries on the target — only that
the two ends of the `return` name the same one.
```maxon
function code() returns ExitCode
  return 7
end 'code'

function main() returns ExitCode
  return code()
end 'main'
```
```exitcode
7
```


<!-- test: range-check-panic.an-uncontained-return-is-still-guarded -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⚠ **THE NEGATIVE CONTROL, and the one that matters most.** `Wide` is not inside `Narrow`, so nothing is
proved and the guard stands exactly where A1f put it. Off by one at the top: `101` is a legal `Wide` and
not a legal `Narrow`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Wide = int(0 to 1000)
typealias Narrow = int(0 to 100)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function widen() returns Wide
  return opaque(101)
end 'widen'

function narrow() returns Narrow
  return widen()
end 'narrow'

function main() returns ExitCode
  return narrow()
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.an-uncontained-return-is-still-guarded.test:15: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in narrow
  in main
  in mrt_start
```


<!-- test: range-check-panic.the-source-alias-keeps-its-own-guard -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⚠ **THE ELISION MOVES NOTHING UPSTREAM.** It is the DOWNSTREAM door that proves nothing to check; the
door the value actually entered through is untouched, and it is the one that fires. `300` is outside
`Byte`, so `pick` panics naming `Byte` — `main`'s elided `ExitCode` check never gets a value at all.
```maxon
typealias Byte = int(0 to u8.max)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function pick() returns Byte
  return opaque(300)
end 'pick'

function main() returns ExitCode
  return pick()
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.the-source-alias-keeps-its-own-guard.test:10: Range check failed: value outside typealias 'Byte'
Stack trace:
  in pick
  in main
  in mrt_start
```


<!-- test: range-check-panic.a-contained-field-store-and-element-store-emit-no-guard -->
⭐ **THE OTHER DOORS ASK THE SAME QUESTION, and here the answer IS observable** — a field store and an
array-element store take no `as`, so E3010 never stands in front of them. `Small` is inside `Wide` at
both, and the two `return`s that hand a `Wide` back through a `returns Wide` are contained as well: the
fragment holds no `__rc_panic` block anywhere.
```maxon
typealias Small = int(0 to 100)
typealias Wide = int(0 to 1000)
typealias Wides = Array with Wide

type Box
  export var v as Wide

  static function create() returns Box
    return Self{v: 1}
  end 'create'
end 'Box'

function small() returns Small
  return 5
end 'small'

function fieldStore() returns Wide
  var b = Box.create()
  b.v = small() as Wide
  return b.v
end 'fieldStore'

function elementStore() returns Wide
  var a = Wides.create()
  a.push(small() as Wide)
  return try a.get(0) otherwise panic("no slot")
end 'elementStore'

function main() returns ExitCode
  print("f={fieldStore()} e={elementStore()}\n")
  return 0
end 'main'
```
```stdout
f=5 e=5
```


<!-- test: range-check-panic.an-uncontained-field-store-is-still-guarded -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⚠ The negative control for the field door, one value past the bound the elision would need. `Loose` is
not inside `Wide`, so the store guards, and `1001` is what the guard is for.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Loose = int(0 to 2000)
typealias Wide = int(0 to 1000)

type Box
  export var v as Wide

  static function create() returns Box
    return Self{v: 1}
  end 'create'
end 'Box'

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function loose() returns Loose
  return opaque(1001)
end 'loose'

function main() returns ExitCode
  var b = Box.create()
  b.v = loose() as Wide
  print("v={b.v}\n")
  return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.an-uncontained-field-store-is-still-guarded.test:24: Range check failed: value outside typealias 'Wide'
Stack trace:
  in main
  in mrt_start
```


<!-- test: range-check-panic.an-uncontained-element-store-is-still-guarded -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⚠ The negative control for the array-element door, which A1f-arrayelem put at the STORE rather than at
the callee's entry — so the elision has to leave that one standing too.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Loose = int(0 to 2000)
typealias Wide = int(0 to 1000)
typealias Wides = Array with Wide

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function loose() returns Loose
  return opaque(1001)
end 'loose'

function main() returns ExitCode
  var a = Wides.create()
  a.push(loose())
  let v = try a.get(0) otherwise panic("no slot")
  print("v={v}\n")
  return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.an-uncontained-element-store-is-still-guarded.test:17: Range check failed: value outside typealias 'Wide'
Stack trace:
  in main
  in mrt_start
```


### A COUNTED LOOP'S COUNTER CARRIES A RANGE, AND A GUARD IT COVERS IS DEAD (EC7)

`for r in 0 upto 64` steps its counter through `0 … 63` and nothing else. That is a fact about every
value the body ever sees, so a range check the counter reaches — an `as` cast, a `return`, a field or
element store, or the entry guard of a callee that gets inlined here — **cannot fail**, and the
compiler does not emit it.

⚠ **IT IS AN INTERVAL, NOT A TYPE.** The counter's written type is still the bare `int` its bounds were
written at: nothing NAMES `0 … 63`, so `r as RegNum` is **not** an *unneeded cast* (E3010 quotes two
declared aliases and there is only one here), and a counter that happens to exclude zero does **not**
make `n / r` an infallible divide. Only the questions about whether a runtime GUARD can fail read it.

**Both bounds must be constants the compiler can fold** — a literal, or an immutable top-level `let`.
`for r in 0 upto xs.count()` proves nothing and every guard under it stands.

**The interval is the one the BODY runs on**, which is `[a, b-1]` for `upto` and `[a, b]` for `to`. A
loop that runs no trip states no interval, and an inclusive loop whose top is `i64.max` states none
either: its step wraps to `i64.min` rather than ever failing the test.


<!-- test: range-check-panic.a-counted-loop-counter-proves-a-cast -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The counter of `0 upto 64` is in `0 … 63`, which is exactly `RegNum` — so the cast's cascade is not
emitted, and the cast is still not an E3010 (an interval names no alias to have repeated).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RegNum = int(0 to 63)

function main() returns ExitCode
  var total = 0 as Integer
  for r in 0 upto 64 'scan'
    let g = r as RegNum
    total = total + g
  end 'scan'
  print("total={total}\n")
  return 0
end 'main'
```
```stdout
total=2016
```


<!-- test: range-check-panic.a-counted-loop-one-past-the-alias-still-panics -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⚠ **THE DISCRIMINATING CASE FOR THE EXCLUSIVE ARITHMETIC.** One more trip and the counter reaches 64,
which `RegNum` does not admit — so the guard stays and fires. A rule that took the interval as
`[0, b-2]`, or that compared it the other way round, would print `2080` here instead.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RegNum = int(0 to 63)

function main() returns ExitCode
  var total = 0 as Integer
  for r in 0 upto 65 'scan'
    let g = r as RegNum
    total = total + g
  end 'scan'
  print("total={total}\n")
  return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.a-counted-loop-one-past-the-alias-still-panics.test:8: Range check failed: value outside typealias 'RegNum'
Stack trace:
  in main
  in mrt_start
```


<!-- test: range-check-panic.an-inclusive-counted-loop-at-its-top-bound-is-proven -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
`to` includes its bound, so `0 to 63` is the same interval `0 upto 64` is — the same 2016, and the
same elision.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RegNum = int(0 to 63)

function main() returns ExitCode
  var total = 0 as Integer
  for r in 0 to 63 'scan'
    let g = r as RegNum
    total = total + g
  end 'scan'
  print("total={total}\n")
  return 0
end 'main'
```
```stdout
total=2016
```


<!-- test: range-check-panic.an-inclusive-counted-loop-one-past-its-top-bound-still-panics -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⚠ **THE DISCRIMINATING CASE FOR THE INCLUSIVE ARITHMETIC**, and it is the one that would go silently
wrong if `to` were read as `upto`: under `[0, 63]` the guard would be elided and this would print
`2080` rather than panicking at 64.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RegNum = int(0 to 63)

function main() returns ExitCode
  var total = 0 as Integer
  for r in 0 to 64 'scan'
    let g = r as RegNum
    total = total + g
  end 'scan'
  print("total={total}\n")
  return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.an-inclusive-counted-loop-one-past-its-top-bound-still-panics.test:8: Range check failed: value outside typealias 'RegNum'
Stack trace:
  in main
  in mrt_start
```


<!-- test: range-check-panic.a-runtime-loop-bound-proves-nothing -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A bound the compiler cannot fold states no interval, however obvious the value is at run time — so
the guard stands and fires at 64.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RegNum = int(0 to 63)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function main() returns ExitCode
  var total = 0 as Integer
  for r in 0 upto opaque(100) 'scan'
    let g = r as RegNum
    total = total + g
  end 'scan'
  print("total={total}\n")
  return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.a-runtime-loop-bound-proves-nothing.test:12: Range check failed: value outside typealias 'RegNum'
Stack trace:
  in main
  in mrt_start
```


<!-- test: range-check-panic.an-empty-counted-loop-proves-nothing -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A loop that runs no trip has no interval to state. Nothing is elided and nothing runs; the committed
fragment carries the guard that survives.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RegNum = int(0 to 63)

function main() returns ExitCode
  var total = 0 as Integer
  for r in 5 upto 5 'none'
    let g = r as RegNum
    total = total + g
  end 'none'
  print("total={total}\n")
  return 0
end 'main'
```
```stdout
total=0
```


<!-- test: range-check-panic.an-inclusive-counted-loop-to-i64-max-keeps-its-guard -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⚠ An inclusive loop ending at `i64.max` never fails its own test — the step WRAPS to `i64.min`, which
`NonNeg` does not admit — so no interval is stated and the guard stays. No answer can show that (the
wrap is 2^63 trips away): what records it is the committed fragment, which still carries the cascade.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NonNeg = int(0 to i64.max)

function main() returns ExitCode
  var total = 0 as Integer
  for r in 0 to i64.max 'forever'
    let g = r as NonNeg
    total = total + g
    break
  end 'forever'
  print("total={total}\n")
  return 0
end 'main'
```
```stdout
total=0
```


<!-- test: range-check-panic.a-counted-loop-counter-proves-a-callees-parameter -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A narrowed PARAMETER's guard stands at the callee's entry, in front of every caller — until the callee
is inlined here, where there is exactly one caller and it already knows the argument is in range. The
spliced cascade is then not copied at all.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RegNum = int(0 to 63)

function weigh(table Integer, regNum RegNum) returns Integer
  return table + (regNum as Integer)
end 'weigh'

function main() returns ExitCode
  var total = 0 as Integer
  for r in 0 upto 64 'scan'
    total = total + weigh(1, regNum: r)
  end 'scan'
  print("total={total}\n")
  return 0
end 'main'
```
```stdout
total=2080
```


<!-- test: range-check-panic.a-counted-loop-past-a-callees-parameter-still-panics -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⚠ The negative control for the inlined cascade, and it pins the FRAME as well as the panic: the
counter is not inside `RegNum`, the cascade is copied as it always was, and the slow arm re-issues the
real call — so the trace still names `weigh`, at `weigh`'s own parameter line.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RegNum = int(0 to 63)

function weigh(table Integer, regNum RegNum) returns Integer
  return table + (regNum as Integer)
end 'weigh'

function main() returns ExitCode
  var total = 0 as Integer
  for r in 0 upto 100 'scan'
    total = total + weigh(1, regNum: r)
  end 'scan'
  print("total={total}\n")
  return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.a-counted-loop-past-a-callees-parameter-still-panics.test:5: Range check failed: value outside typealias 'RegNum'
Stack trace:
  in weigh
  in main
  in mrt_start
```


<!-- test: range-check-panic.a-ranged-parameter-proves-a-callees-parameter -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The second proof a call site can hold: the argument is one of THIS function's own narrowed
parameters, whose entry cascade dominates every call it makes. `twice` keeps its own guard — it is
still called from anywhere — and both inlined copies of `weigh`'s lose theirs.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RegNum = int(0 to 63)

function weigh(table Integer, regNum RegNum) returns Integer
  return table + (regNum as Integer)
end 'weigh'

function twice(table Integer, reg RegNum) returns Integer
  return weigh(table, regNum: reg) + weigh(table, regNum: reg)
end 'twice'

function main() returns ExitCode
  print("t={twice(1, reg: 7)}\n")
  return 0
end 'main'
```
```stdout
t=16
```


<!-- test: range-check-panic.a-wider-parameter-does-not-prove-a-narrower-one -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⚠ The negative control for that second proof: `Wide` admits values `RegNum` does not, so the caller's
own guard proves nothing about the callee's and the spliced cascade stays. Both frames are in the
trace, which is what says the panic is still the callee's.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RegNum = int(0 to 63)
typealias Wide = int(0 to 1000)

function weigh(table Integer, regNum RegNum) returns Integer
  return table + (regNum as Integer)
end 'weigh'

function twice(table Integer, reg Wide) returns Integer
  return weigh(table, regNum: reg as RegNum) + weigh(table, regNum: reg as RegNum)
end 'twice'

function main() returns ExitCode
  print("t={twice(1, reg: 200)}\n")
  return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.a-wider-parameter-does-not-prove-a-narrower-one.test:11: Range check failed: value outside typealias 'RegNum'
Stack trace:
  in twice
  in main
  in mrt_start
```


<!-- test: range-check-panic.an-inclusive-counted-loop-past-the-unsigned-top-still-panics -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⚠⚠ **THE DISCRIMINATING CASE FOR THE TOP BOUND, AND IT WAS A MEASURED WRONG ANSWER.** A `to` loop runs the
body AT its bound, steps past it and tests again — so at the top of the domain the test READS, the step wraps
to that domain's bottom and the loop never leaves. There are two tops, because `emitCompare` picks a
signedness valid for both operands: this counter is declared over `int(0 to u64.max)`, so the test compiles
UNSIGNED, walks past `-1` into `0, 1, …`, and the interval `[-5, -1]` stops being true on the sixth trip.
With only the SIGNED top refused this printed `n=7 last=1` and exited 0; the guard belongs here and fires.
⚠ `(0 - 5) as Big` is spelled as a SUBTRACTION on purpose: a written `-5` is refused at a declared lower
bound of 0 before the program runs (E3005), and this case pins the RUNTIME guard — the counter has to
arrive negative through arithmetic, not through a literal the parser marks.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Big = int(0 to u64.max)
typealias Neg = int(-5 to -1)

function main() returns ExitCode
  var n = 0 as Integer
  var last = 0 as Integer
  for i in ((0 - 5) as Big) to u64.max 'wrap'
    let g = i as Neg
    last = g
    n = n + 1
    if n > 6 'enough'
      break
    end 'enough'
  end 'wrap'
  print("n={n} last={last}\n")
  return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.an-inclusive-counted-loop-past-the-unsigned-top-still-panics.test:10: Range check failed: value outside typealias 'Neg'
Stack trace:
  in main
  in mrt_start
```


<!-- test: range-check-panic.a-counted-divisor-is-still-a-throwing-divide -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⛔ **THE RED-GATE CONTROL FOR ONE OF THE THREE WITHHOLDS.** `safety.md`'s escape hatch is stated in terms of
*a ranged TYPE whose range excludes 0*, and a counted loop's interval is not one — so `100 / i` still throws
and still needs its `try`. Fold the interval into `divisorProof` and the divide becomes a bare `binOp`, at
which point this `try` has no call to apply to and the program stops compiling (E2015). Verified by making
that edit and watching this case go red.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
  var t = 0 as Integer
  for i in 1 upto 10 'l'
    t = t + try (100 / i) otherwise 0
  end 'l'
  print("t={t}\n")
  return 0
end 'main'
```
```stdout
t=281
```


<!-- test: range-check-panic.a-guarded-float-cast-proves-a-divisor-a-merge-cannot -->
⭐⭐ **THE POSITIVE COUNTERPART OF THE WITHHOLD ABOVE, AND THE LINE BETWEEN THEM IS *NAMED AND GUARDED*.**
`d` is a reassigned `var`, so it denotes nothing a merge or an interval could vouch for — but `as Positive`
is a DECLARED alias, and the cast emits the runtime guard the next case fires. Past that guard the value
satisfies `Positive` whatever it was before, so `8.0 / s` is `safety.md`'s escape hatch and needs no `try`.
⚠ The proof cannot ride the SOURCE value: `d` is a bare local, one value under two names, and stamping it
would vouch for `d` on paths the guard never ran on. The cast mints its own value to carry it.
```maxon
typealias Positive = float(2.2250738585072014e-308 to f64.max)

function main() returns ExitCode
  var d = 0.0
  d = d + 4.0
  let s = d as Positive
  print("q={8.0 / s}\n")
  return 0
end 'main'
```
```stdout
q=2.0
```


<!-- test: range-check-panic.a-guarded-float-cast-that-fails-still-panics -->
⛔ **WHAT MAKES THE PROOF ABOVE LEGITIMATE.** The same shape with `d` landing on `0.0`: the guard the cast
emits is what stands between a bare `divsd` and a division by zero, so it must still fire — and it must fire
at the CAST, before the divide the proof exempted. An elided guard here would print `q=inf`.
```maxon
typealias Positive = float(2.2250738585072014e-308 to f64.max)

function main() returns ExitCode
  var d = 1.0
  d = d - 1.0
  let s = d as Positive
  print("q={8.0 / s}\n")
  return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.a-guarded-float-cast-that-fails-still-panics.test:7: Range check failed: value outside typealias 'Positive'
Stack trace:
  in main
  in mrt_start
```


<!-- test: range-check-panic.a-merge-fed-by-a-counted-loop-denotes-no-alias -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⛔ **THE RED-GATE CONTROL FOR THE THIRD WITHHOLD.** `x` is an `if`-continuation merge — one of the four that
MAY lift a phi's withheld alias claim — and one of its edges is a counted counter whose interval lies inside
`Small`. Letting `edgeProvesAlias` read the interval would lift that claim, putting the merge's DECLARED name
back into circulation, and `x as Small` would become E3010: an interval that names nothing would have refused
a program every other compiler accepts. Verified by making that edit and watching this case go red.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Small = int(0 to 10)

function main() returns ExitCode
  var t = 0 as Integer
  for r in 0 upto 5 'l'
    var x = 1 as Small
    if r > 2 'high'
      x = r
    end 'high' else 'low'
      x = 1
    end 'low'
    t = t + (x as Small)
  end 'l'
  print("t={t}\n")
  return 0
end 'main'
```
```stdout
t=10
```
```


<!-- test: range-check-panic.a-guard-in-an-unselected-ternary-arm-does-not-fire -->
⛔⛔ **A GUARD BELONGS TO ITS ARM, AND A CONDITIONAL EXPRESSION'S TRUE ARM IS *RELOCATED* AFTER IT IS
PARSED.** A site records the block control was in and the ordinal that block stood at, because an
int->int cast emits no op to anchor to — and `parseTernaryExpression` then lifts the true arm's ops off
the unconditional path into `ternarytrue`, leaving the site describing a position that no longer holds
its value. The cascade was emitted in the CONDITION's block and ran on both edges: this program panicked
`value outside typealias 'Small'` where the bootstrap prints `7`. The arm's sites now travel with its
ops (`Parser.rehomeArmRangeSites`).
```maxon
typealias Wide = int(0 to u64.max)
typealias Small = int(0 to 100)

function widen(n Wide) returns Wide
  return n * 100
end 'widen'

function pick(i Wide, flag bool) returns Small
  return (i as Small) if flag else 7
end 'pick'

function main() returns ExitCode
  return pick(widen(5), flag: false)
end 'main'
```
```exitcode
7
```

<!-- test: range-check-panic.a-guard-in-the-selected-ternary-arm-still-fires -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The positive control for the case above, and the half that says the guard was MOVED rather than lost:
the identical program with the condition TRUE panics, at the arm's own line.
```maxon
typealias Wide = int(0 to u64.max)
typealias Small = int(0 to 100)

function widen(n Wide) returns Wide
  return n * 100
end 'widen'

function pick(i Wide, flag bool) returns Small
  return (i as Small) if flag else 7
end 'pick'

function main() returns ExitCode
  return pick(widen(5), flag: true)
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.a-guard-in-the-selected-ternary-arm-still-fires.test:10: Range check failed: value outside typealias 'Small'
Stack trace:
  in pick
  in main
  in mrt_start
```

<!-- test: range-check-panic.a-value-computed-inside-a-ternary-arm-is-guarded-there -->
⛔ The same defect's louder half: when the guarded value is COMPUTED in the arm rather than merely read
there, the misplaced cascade READS a value defined in a block it dominates, and the register allocator
refuses the function outright — *"seedInUse: value N is live-in to block 0 but was never colored"*, so
the program does not build at all. It is what stopped shv2 compiling ITSELF once `stdlib/Array.maxon`'s
`ElementIndex` narrowed and put a guard on every computed array index: `Project.slotCallArgs` indexes
`argDefaultSlots` by a subtraction inside a conditional expression's true arm. The second call also
carries an OUT-OF-RANGE value down the arm that is NOT selected, so a guard that merely survived in the
wrong block would be caught here too and not only by the compile.
```maxon
typealias Wide = int(0 to u64.max)
typealias Small = int(0 to 100)

function widen(n Wide) returns Wide
  return n * 100
end 'widen'

function pick(i Wide, flag bool) returns Small
  return ((i + 1) as Small) if flag else 7
end 'pick'

function main() returns ExitCode
  print("{pick(widen(0), flag: true)}\n")
  print("{pick(widen(5), flag: false)}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1
7
```

<!-- test: range-check-panic.a-computed-value-in-the-selected-ternary-arm-still-panics -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The fourth corner, and the one that says the computed shape's guard was MOVED rather than dropped: the
same program with the arm SELECTED and the computation out of range panics, from the arm's own line.
```maxon
typealias Wide = int(0 to u64.max)
typealias Small = int(0 to 100)

function widen(n Wide) returns Wide
  return n * 100
end 'widen'

function pick(i Wide, flag bool) returns Small
  return ((i + 1) as Small) if flag else 7
end 'pick'

function main() returns ExitCode
  return pick(widen(5), flag: true)
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.a-computed-value-in-the-selected-ternary-arm-still-panics.test:10: Range check failed: value outside typealias 'Small'
Stack trace:
  in pick
  in main
  in mrt_start
```
