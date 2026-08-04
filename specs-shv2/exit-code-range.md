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

⭐⭐ **THESE THREE ARE REGRESSION CASES FOR A MEASURED WRONG ANSWER, NOT NEW SURFACE.** X6 built the
guard and its elision path; the elision is correct wherever the value's own range is already inside the
declared one. **It is NOT correct at a JOIN whose incoming value is wider than the declared type** — the
premise the guard stands on is lost at the phi — nor at a `return` inside a `throws` function's `match`
arm, which never got a guard at all. Found 2026-08-03 by the `G14` review while auditing 604 guards the
re-mint would otherwise have deleted from the record. ⚠ **`try/otherwise` does NOT lose it** (measured),
which is why `trycont` is excluded below: the asymmetry is the clue to where the fix belongs.

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

<!-- test: in-range-join-still-elides-and-returns -->
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
	return tagOf(Kw.end(20, b: 22))
end 'main'
```
```exitcode
42
```
