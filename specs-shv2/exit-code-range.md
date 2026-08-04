---
feature: exit-code-range
status: experimental
keywords: [exitcode, range, typealias, panic, runtime, bounds check]
category: runtime
---

# ExitCode Is A Ranged Type

## Documentation

`ExitCode` is a **narrow declared type** — the compiler gives it a 32-bit unsigned representation, so
its range is `int(0 to 4294967295)`. It is a builtin rather than a `typealias` the program writes, and
that is the only thing that distinguishes it from `typealias Percent = int(0 to 100)`. **It is not a
distinction the range rule may see.**

So every obligation `range-check-panic.md` states for a ranged typealias is `ExitCode`'s too, at every
position a value meets one — a `return`, an explicit `as`, a call argument, a struct field, an array
element:

- **A value known at compile time** that does not fit is a **compile error (E3005)**, naming the value,
  the type and its bounds. `return -1` from a `returns ExitCode` function never builds.
- **A value the compiler cannot fold** gets a **runtime range check** where it lands. Out of range, the
  program panics naming `ExitCode`, exactly as it would name any other alias.

### ⚠ Why this needed saying at all

Before X6 the builtin recorded no range anywhere: `int(0 to u32.max)` was a *sentence in a comment*
beside the shift rule, and the width `u32` was a `StdType` chosen at the Maxon→Std boundary. Nothing
connected the two, and nothing checked either. A `returns ExitCode` function returning `-1` therefore
compiled clean and **gave a different answer on each target** — `-1` on x64, `4294967295` on wasm —
because the two disagree about how wide a register holding a `u32` is, and neither was asked to hold a
value the type admits.

**The disagreement was the symptom; the missing check was the defect.** Widening the wasm
representation until the two agreed would have made the declared type a lie about representation and
left every out-of-range value silently accepted on both. The range is the type's promise, so the range
is what is enforced.

## Tests

<!-- test: error.negative-literal-return -->
The reproducer. `-1` is a literal, `ExitCode` starts at 0, and the two facts are both in hand at the
`return`.
```maxon
function negOne() returns ExitCode
  return -1
end 'negOne'

function main() returns ExitCode
  let v = negOne()
  print("v={v}\n")
  return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:3: Value -1 is outside the range of 'ExitCode' (int(0 to 4294967295))
```

<!-- test: error.negative-literal-cast -->
The same rule at an explicit `as`. A cast to `ExitCode` used to be the canonical example of a cast that
names no ranged alias and therefore records nothing; it names one now.
```maxon
function main() returns ExitCode
  let v = -1 as ExitCode
  print("v={v}\n")
  return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:14: Value -1 is outside the range of 'ExitCode' (int(0 to 4294967295))
```

<!-- test: error.literal-past-the-upper-bound -->
And at the top of the range, which is the bound the 32-bit representation actually sets. `4294967296` is
`u32.max + 1`.
```maxon
function tooBig() returns ExitCode
  return 4294967296
end 'tooBig'

function main() returns ExitCode
  let v = tooBig()
  print("v={v}\n")
  return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:3: Value 4294967296 is outside the range of 'ExitCode' (int(0 to 4294967295))
```

<!-- test: error.literal-argument -->
A call ARGUMENT owes the compile-time half at its own site, exactly as it does for a user alias — the
runtime half of that door lives at the callee's entry.
```maxon
function takes(code ExitCode) returns ExitCode
  return code
end 'takes'

function main() returns ExitCode
  return takes(-3)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:10: Value -3 is outside the range of 'ExitCode' (int(0 to 4294967295))
```

<!-- test: in-range-literal-is-unchanged -->
The negative control for every case above: an in-range literal is not a violation, and `7` still comes
out of the process.

⚠ **AND ITS FRAGMENT RECORDS THE PRICE, WHICH SINCE A4f IS ZERO — for a reason that is still the one this
spec opens with.** `code`'s `return 7` is folded and needs no runtime check at all; `main`'s `return
code()` returns a CALL RESULT, which no constant fold can see, so it is decided by the ordinary rule
every ranged alias gets — and that rule (`range-check-panic.md`, *"a value the destination PROVABLY
admits"*) answers **contained**, because the source is an `ExitCode` and the destination is an `ExitCode`.
Between X6 and A4f this fragment carried a full `0 ≤ x ≤ 4294967295` cascade over a value that had just
passed the identical cascade one frame down. `ExitCode` being a builtin is not a distinction the range
rule may see — in EITHER direction.
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

<!-- test: both-bounds-are-reachable-values -->
The two ends of the range are ordinary values, on every target — this is the band where a signed and an
unsigned reading of the same 32 bits disagree, and both readings must print the unsigned one.
```maxon
function main() returns ExitCode
  let low = 0 as ExitCode
  let high = 4294967295 as ExitCode
  print("low={low} high={high}\n")
  return 0
end 'main'
```
```stdout
low=0 high=4294967295
```

<!-- test: computed-negative-return-panics -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
⭐ **THE RUNTIME HALF, AND THE CASE THE WHOLE RUNG IS ABOUT.** The value is computed, so no literal
check can see it; the `return` is where it meets `ExitCode` and where the guard stands. It printed `-1`
on x64 and `4294967295` on wasm before this rung, with no diagnostic on either — a wrong answer that was
not even the SAME wrong answer.
```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function negOne() returns ExitCode
  return opaque(0) - 1
end 'negOne'

function main() returns ExitCode
  let v = negOne()
  print("v={v}\n")
  return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at computed-negative-return-panics.test:9: Range check failed: value outside typealias 'ExitCode'
Stack trace:
  in negOne
  in main
  in mrt_start
```

<!-- test: computed-in-range-return-is-unchanged -->
The negative control for the runtime half: an in-range computed value passes the guard and returns
normally, on every target. Nothing inside the range moves.
```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function code() returns ExitCode
  return opaque(3) + 4
end 'code'

function main() returns ExitCode
  return code()
end 'main'
```
```exitcode
7
```

### ⛔ ONE DOOR IS NOT CLOSED, AND IT IS THE ONE WHERE THE ABI NARROWS BEFORE THE GUARD RUNS

Every door above guards the value while it is still a full machine word: a `return`'s guard runs before
the `ret` narrows to the u32 return slot, a cast's before the value is used, a struct field's before the
store (a field declared `ExitCode` takes `alias.underlying`, an 8-byte slot, and an env slot is widened
outright by `envSlotStorageType`). **The call ARGUMENT is the single exception**, and the reason is
structural rather than an oversight:

`A1f` moved the argument door's runtime half to the **callee's entry** — one guard per narrowed parameter,
standing in front of every caller including the indirect ones. That rests on a premise nothing had ever
falsified: *the value the callee sees is the value the caller passed.* An `ExitCode` parameter is the
first parameter whose **Std type is narrower than a machine word** (`u32`), so on a target whose ABI
narrows at the boundary the premise is false — and it is false in the one way no callee-side check can
repair. **MEASURED:** `takes(opaque(0) - 5)` arrives inside `takes` as `4294967291` on wasm, which is
byte-for-byte the *legitimate* `takes(opaque(4294967291))`. At `u32` width `int(0 to u32.max)` is the FULL
range: every bit pattern is in range, so there is no predicate that separates the two. x64 refuses `-5`
only because its registers never narrowed it.

⇒ **The guard has to move to the caller for this one door** — the check belongs where the lossy
conversion is decided, which is the call site. That is not a line here: it needs an argument position in
the Std IR for every argument (the side table records only CONSTANT ones today, `recordConstantArgRangeChecks`),
and it has to answer the indirect-call question A1f chose the entry to solve. It is its own rung. The
compile-time half of this door is unaffected and closed on every target — `takes(-3)` above never builds.

<!-- test: computed-argument-is-guarded-at-the-callee-entry -->
<!-- targets: x64-windows, x64-linux -->
The half that DOES hold: where the ABI passes the argument at full width, the entry guard refuses it, and
the panic names the parameter's own declaration line rather than the caller's — one guard serves every
call site, so the caller's line is not a fact it holds.
```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function takes(code ExitCode) returns ExitCode
  return code
end 'takes'

function main() returns ExitCode
  return takes(opaque(0) - 5)
end 'main'
```
```exitcode
1
```
```stderr
panic at computed-argument-is-guarded-at-the-callee-entry.test:8: Range check failed: value outside typealias 'ExitCode'
Stack trace:
  in takes
  in main
  in mrt_start
```

<!-- disabled-test: computed-argument-is-guarded-on-a-narrowing-abi -->
<!-- a rung that moves the call-argument door's RUNTIME half back to the CALL SITE -->
<!-- targets: wasm32-wasi -->
The wasm twin of the case above, and the exact shape of the missing mechanism. It is the identical
program; only the lane differs. It is shelved rather than deleted because a gap nothing states is a gap
nobody finds again — this case going green is what will say the door is closed.

⚠ It is DISABLED, not marked with a `targets:` line that quietly omits wasm, because the two say
different things: a `targets:` omission reads as "this lane cannot express the program", and this lane
expresses it perfectly and gets the wrong answer. Today it exits **251** — `-5` truncated to `u32` and
then masked to a byte by WASI — with no diagnostic.
```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function takes(code ExitCode) returns ExitCode
  return code
end 'takes'

function main() returns ExitCode
  return takes(opaque(0) - 5)
end 'main'
```
```exitcode
1
```
```stderr
panic at computed-argument-is-guarded-on-a-narrowing-abi.test:8: Range check failed: value outside typealias 'ExitCode'
Stack trace:
  in takes
  in main
  in mrt_start
```

### Joins, and the return sites a guard never reached

⭐⭐ **THESE ARE REGRESSION CASES FOR A MEASURED WRONG ANSWER, NOT NEW SURFACE.** X6 built the
guard and its elision path; the elision is correct wherever the value's own range is already inside the
declared one. **It is NOT correct at a JOIN whose incoming value is wider than the declared type** — the
premise the guard stands on is lost at the phi — nor at a `return` in a `throws` function, which never
got a guard at all. Found 2026-08-03 by the `G14` review while auditing 604 guards the re-mint would
otherwise have deleted from the record.

⚠⚠ **TWO SENTENCES THAT STOOD HERE WERE WRONG, AND BOTH MADE THE DEFECT LOOK SMALLER THAN IT IS (G14
implementation).** They are corrected rather than deleted, because each one names a probe that was run
and a conclusion that did not survive a second probe:

  * *"a `return` inside a `throws` function's **`match` arm**"* — the `match` is not the trigger. The
    terminator is: a throwing function leaves through `errorReturn` rather than `ret`, and the pass
    resolving a return site recognised only `ret`. **Every ranged `return` in every `throws` function
    was unguarded**, `match` or no `match`, which
    `throws-plain-return-is-guarded` below pins in its general form.
  * *"**`try/otherwise` does NOT lose it** (measured), which is why `trycont` is excluded"* — measured
    with a LITERAL fallback, which is the one shape that is separately checked at compile time
    (`requireOtherwiseInRangedReturn` refuses an out-of-range literal and is documented as checking
    nothing else). Swap the literal for a variable and the merge loses the premise exactly as the other
    joins do — `try-otherwise-nonliteral-fallback-is-guarded` below. The asymmetry was an artifact of
    the probe, not a property of `trycont`; `otherwise <literal>` still elides, and that is a fact about
    the LITERAL rather than about `try`.

<!-- test: join-in-a-gives-arm-is-guarded -->
<!-- targets: x64-windows -->
```maxon
typealias Num = int(0 to 1000)

union Kw
	alpha(n Num)
	end(a Num, b Num)
end 'Kw'

function tagOf(k Kw) returns Num
	return match k 'm'
		alpha(n) gives n
		end(a, b) gives a + b
	end 'm'
end 'tagOf'

function main() returns ExitCode
	let t = tagOf(Kw.end(900, b: 900))
	print("t={t}")
	return 7
end 'main'
```
```exitcode
1
```
```stderr
panic at join-in-a-gives-arm-is-guarded.test:10: Range check failed: value outside typealias 'Num'
Stack trace:
  in tagOf
  in main
  in mrt_start
```

<!-- test: throws-match-arm-return-is-guarded -->
<!-- targets: x64-windows -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Result
	success(value Integer)
	pending
end 'Result'

enum MatchError
	unmatched
end 'MatchError'

function getValue(r Result) returns ExitCode throws MatchError
	match r 'check'
		success(v) then return v
		default throws MatchError.unmatched
	end 'check'
end 'getValue'

function main() returns ExitCode
	let r = Result.success(-5)
	let result = try getValue(r) otherwise 0
	print("result={result}")
	return 7
end 'main'
```
```exitcode
1
```
```stderr
panic at throws-match-arm-return-is-guarded.test:15: Range check failed: value outside typealias 'ExitCode'
Stack trace:
  in getValue
  in main
  in mrt_start
```

<!-- test: in-range-join-is-guarded-and-passes -->
<!-- targets: x64-windows -->
⛔ **THIS CASE WAS CALLED `in-range-join-still-elides-and-returns`, AND IT DOES NOT ELIDE (G14 review).**
Its own committed fragment carries the `__rc_panic` block, and the emitted `matchcont` runs the full
`0 ≤ x ≤ 1000` cascade — because `a + b` denotes no alias, so the arm proves nothing and the merge keeps
its withheld claim. That is CORRECT and is exactly why the sibling above panics on `900 + 900`; what was
wrong was the name, which promised an observation this case structurally cannot make. **A runtime case
cannot see an elision at all** — an emitted guard and an elided one are the same PASS on an in-range
value — so what it pins is the other half, and the honest half: the withheld claim does not turn a
program the range admits into a panic. The elision control is the FRAGMENT, and the `otherwise 0` corpus.
```maxon
typealias Num = int(0 to 1000)

union Kw
	alpha(n Num)
	end(a Num, b Num)
end 'Kw'

function tagOf(k Kw) returns Num
	return match k 'm'
		alpha(n) gives n
		end(a, b) gives a + b
	end 'm'
end 'tagOf'

function main() returns ExitCode
	return tagOf(Kw.end(20, b: 22))
end 'main'
```
```exitcode
42
```

<!-- test: throws-plain-return-is-guarded -->
<!-- targets: x64-windows -->
⭐ **THE GENERAL FORM OF THE CASE ABOVE, AND THE ONE THAT NAMES THE REAL TRIGGER.** No `match`, no
join, no branch — one `return v` in a function whose only distinguishing feature is a `throws` clause.
It is worth more than its `match` sibling because the sibling could be read as a statement about match
arms, and this cannot be read as anything but a statement about the terminator: `errorReturn` is what a
throwing function returns through, and a site resolver that knows only `ret` finds nothing to guard.
```maxon
typealias Integer = int(i64.min to i64.max)

enum MatchError
	unmatched
end 'MatchError'

function getValue(v Integer) returns ExitCode throws MatchError
	return v
end 'getValue'

function main() returns ExitCode
	let result = try getValue(-5) otherwise 0
	print("result={result}")
	return 7
end 'main'
```
```exitcode
1
```
```stderr
panic at throws-plain-return-is-guarded.test:9: Range check failed: value outside typealias 'ExitCode'
Stack trace:
  in getValue
  in main
  in mrt_start
```

<!-- test: try-otherwise-nonliteral-fallback-is-guarded -->
<!-- targets: x64-windows -->
⭐ **THE `try` MERGE IS A MERGE LIKE THE OTHERS — the case that retired this section's "`try/otherwise`
does NOT lose it".** The continuation phi is named off the callee's return type, so it claims `Num`;
the ok edge earns that claim (the callee guards its own `return`), but a VARIABLE fallback earns
nothing — only a LITERAL fallback is checked, and only at compile time. `5000` therefore reached a
`returns Num` caller's `return` wearing `Num`, and the guard was elided on it.

⚠ Its negative control is the whole `otherwise 0` corpus and its FRAGMENTS: the fix withholds the claim
per EDGE, not per construct, so a literal fallback still proves and still elides. Both halves are needed
— a fix that guarded every `trycont` would pass this case and be wrong. ⛔ `in-range-join-is-guarded-and-passes`
is NOT the elision half and was named as though it were; a runtime case cannot distinguish an emitted
guard from an elided one on a value the range admits (see its own header). Only a fragment can.
```maxon
typealias Num = int(0 to 1000)
typealias Integer = int(i64.min to i64.max)

enum E
	oops
end 'E'

function mk(b bool) returns Num throws E
	if b 'bad'
		throw E.oops
	end 'bad'
	return 5
end 'mk'

function wide(n Integer) returns Integer
	return n
end 'wide'

function pick(b bool) returns Num
	let w = wide(5000)
	return try mk(b) otherwise w
end 'pick'

function main() returns ExitCode
	let v = pick(true)
	print("v={v}")
	return 7
end 'main'
```
```exitcode
1
```
```stderr
panic at try-otherwise-nonliteral-fallback-is-guarded.test:22: Range check failed: value outside typealias 'Num'
Stack trace:
  in pick
  in main
  in mrt_start
```
