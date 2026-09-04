---
feature: exit-code-range
status: experimental
keywords: [exitcode, range, typealias, panic, runtime, bounds check]
category: runtime
---

# ExitCode Is A Ranged Type

## Documentation

`ExitCode` is a **narrow declared type**, and its range is **the compile TARGET's**: `int(0 to u32.max)`
on Windows, `int(0 to 255)` on Linux, macOS and WASI — the platform's own exit-status domain, which is
what `stdlib/Process.maxon` has always said and what `docs/LANGUAGE_REFERENCE.md` states at the entry
point. It is a builtin rather than a `typealias` the program writes, and that is the only thing that
distinguishes it from `typealias Percent = int(0 to 100)`. **It is not a distinction the range rule may
see.**

⭐⭐ **THE RANGE IS NOT THE WIDTH, AND THE SENTENCE THAT STOOD HERE DERIVED ONE FROM THE OTHER.** It read
*"the compiler gives it a 32-bit unsigned representation, **so** its range is `int(0 to 4294967295)`"* —
which is the whole defect BATCH27 fixed, written down as a definition. The two are separate facts and
only one of them is platform-shaped:

- the **WIDTH is `u32` on every target** (`valueTagToStdType`), and deliberately stays there;
- the **RANGE is the platform's**, and on three of four targets it is far narrower than the width.

⛔ **THE RANGE BEING NARROWER THAN THE SLOT IS WHAT MAKES THE ENTRY GUARD WORK, so the width may not
follow the range down.** The runtime half of the call-argument door is a guard at the CALLEE'S ENTRY
(`A1f`), which reads the value out of the parameter slot *after* the ABI has narrowed it. A guard can
only see a truncation while the slot is WIDER than the range: give `ExitCode` a `u8` slot on a POSIX
lane and `takes(opaque(0) - 5)` arrives as `251`, which is inside `int(0 to 255)`, and the guard passes
a value the program never wrote. `Project.maxon`'s `ExitCode` seed and
`Targets/Wasm/StdToWasm.maxon`'s `coerceOnStack` both carry that argument at the two places someone
would reach for the narrower slot.

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

⚠ **AND X6 THEN WROTE THE WIDTH'S SPAN DOWN AS THE RANGE, WHICH IS WHERE BATCH27 CAME IN.** X6's story
above is unchanged and still true — the missing check was the defect, and X6 built the check. What X6
also did was answer *"what range?"* with *"whatever the `u32` slot spans"*, so the builtin's declared
range became `int(0 to u32.max)` on every target. That number was never the language's: it was the
representation's, read off the slot the lowering happened to pick, and it disagreed with
`stdlib/Process.maxon`, with `docs/LANGUAGE_REFERENCE.md` and with the oracle — all three of which key
the exit-status domain by the OS, because the OS is what defines it.

⭐ **AND ONE SPEC IN THIS CORPUS GOT THERE FIRST, WHICH IS WHY THIS WAS FINDABLE AT ALL.**
`function-overloads.md`'s `enum-raw-value-argument` already states the platform rule correctly — *"`ExitCode`
is `int(0 to u32.max)` on Windows but `int(0 to 255)` on Linux, macOS and wasi (`stdlib/Process.maxon`)"* —
and it says so because a case there was RED on every non-Windows target until its addends were made small.
It predates this rung and is the one `ExitCode`-range sentence in the corpus that needed no correction; the
rest of the corpus was quoting shv2's own answer back at itself.

**Deriving the range from the width did not merely quote a wrong number; it made two doors unable to
refuse anything.** At `u32` width `int(0 to u32.max)` **is** the full span of the slot, so *every bit
pattern is in range*: there is no predicate that separates a wrapped `-5` from an honest `4294967291`,
and no check standing anywhere on that value can fire. That is why this spec spent a rung asserting the
call-argument door could not be closed without moving the guard to the call site (see below) — the
missing mechanism was never a Std-IR argument position. It was a range that said something.

## ⭐⭐ WHAT THE NARROWING COSTS THE OTHER LANES, AND WHY NOTHING BUYS IT BACK

`ExitCode` was carrying a second job that had nothing to do with exit codes. **`valueTagToStdType` maps
exactly ONE tag — `exitCode` — to `StdType.u32`; every other tag it can return is `i1`, `f64` or `i64`.**
So `ExitCode` is the only INTEGER type in the language whose *values* are narrower than a machine word —
`bool`'s `i1` is the other narrow one and can hold nothing above 1 — and it became the vehicle for six
cases that pin the behaviour of a narrow value slot on `wasm32-wasi`, where such a value lives in an
`i32` local and has to be widened back:

| case | property |
|---|---|
| `first-class-functions/first-class-function.exitcode-return-through-alias-high` | the widen at a DIRECT call result and at a FUNCTION-TYPEALIAS call result |
| `where-clauses/where-clauses.witness-exitcode-return-high` | the widen at a WITNESS dispatch result |
| `first-class-functions/first-class-function.exitcode-through-alias-computes-at-machine-width` | `*`, `not`, `shl` and `>` on an alias-tagged narrow value, immediate and register forms |
| `comparison-operators/compare-against-a-literal-keeps-the-operand-width` | a folded immediate compare taking its width from the left operand |
| `division/divide-a-value-whose-declared-type-is-narrower-than-a-machine-word` | `div`/`mod`, which carry no operand type |
| `closure-capture/closure-capture.capture-exitcode-wide-value` | the closure ENV slot's width |

All six carry a value the platform range no longer admits (`4000000000`, or `100000` for the env slot),
so all six are now **`<!-- targets: x64-windows -->`** — the honest marker, because those programs are
*illegal* on POSIX rather than untested there.

⛔⛔ **AND THE READING THEY CARRIED IS NOT MERELY UNTESTED ON WASM NOW — IT IS UNREACHABLE, WHICH IS A
STRONGER THING AND A MORE FRAGILE ONE. IT IS WRITTEN DOWN HERE BECAUSE IT IS WHAT A FUTURE RUNG WOULD
BREAK.** The argument is two lines and both are premises, not observations:

1. **`exitCode` is the sole producer of a narrow VALUE type.** `StdType.u32` has two construction sites:
   `valueTagToStdType`'s `exitCode` arm, and `stdTypeOfAbiClass`, which re-derives it from the narrow BIT
   that arm put there. A user `typealias U32 = int(0 to u32.max)` is a `named` tag and lowers to
   `StdType.i64`.
2. **On Linux, macOS and WASI `ExitCode` admits `[0, 255]`**, and a signed widen first disagrees with an
   unsigned one at `2^31`. The band is empty.

⇒ **no legal program on those lanes can put a value in the disagreeing band.** The defect class is closed
by construction there rather than by a case, and it stays closed only while BOTH premises hold: if P1.9
gives some other value a sub-64 Std type, or a later rung widens the POSIX range past `2^31`, these six
readings need a new vehicle and the wasm lane has no guard until they get one.

⚠ **AN ARRAY ELEMENT LOOKS LIKE THAT VEHICLE AND IS NOT — MEASURED, and the measurement is the point.** A
`u32`-ranged element genuinely gets a **4-byte STORAGE slot** (`rangedAliasStorageBytes` = 4, visible as
the `i64.const 4` handed to `__managed_create`), and a `4000000000` pushed through one reads back identically
on the host and on wasm. That is a true measurement of a *different* property. **The STORAGE width is not
the VALUE width:** `__managed_get` is `(param i64 i64) (result i64 i64)`, the loaded element never occupies an
`i32` local, and the emitted `main` for such a program contains **zero** `extend_i32` instructions of
either signedness — `coerceOnStack` is never asked. A case built on that route would pass with the
sign-extension defect fully present, which makes it a positive control and not a guard. It is not added,
and this paragraph is here so the next reader does not re-derive the attractive wrong answer.

✅ **`Targets/Wasm/StdToWasm.maxon`'s `coerceOnStack` header BRIEFLY CONTRADICTED the paragraph above, and
was CORRECTED IN THIS RUNG (`bd24ce8c1`) — the two now say one thing.** It had stated that the widen
"stays reachable on wasm by a route that is not platform-shaped: a `u32`-RANGED ARRAY ELEMENT … The widen
did not become untestable here; only `ExitCode` stopped being able to spell it." Its measurement — the
4-byte slot, and the value agreeing across targets — was real; its conclusion did not follow from it, for
the reason above, and the disassembly was the arbiter. ⚠ **This note is kept rather than deleted because
the wrong reading is the ATTRACTIVE one and was reached twice** — once by the coordinator's measurement
and once by the comment that recorded it — so a reader arriving at either file should find the refutation
rather than re-derive the appeal. Do not read it as a live disagreement: `coerceOnStack`'s header carries
the same two premises this section does.

## Tests

<!-- test: error.negative-literal-return -->
The reproducer. `-1` is a literal, `ExitCode` starts at 0 on every platform, and the two facts are both
in hand at the `return`. The **lower** bound is the one part of the range no target argues about, so this
case would read identically on every lane were the diagnostic not obliged to print the whole range — which
is why it, and the three below it, carry a `` ```Maxoncstderr:x64-windows `` block beside the portable one.
The portable block is the POSIX text; the qualified block wins on Windows.
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
error E3005: <fragment>:3:3: Value -1 is outside the range of 'ExitCode' (int(0 to 255))
```
```Maxoncstderr:x64-windows
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
error E3005: <fragment>:3:14: Value -1 is outside the range of 'ExitCode' (int(0 to 255))
```
```Maxoncstderr:x64-windows
error E3005: <fragment>:3:14: Value -1 is outside the range of 'ExitCode' (int(0 to 4294967295))
```

<!-- test: error.literal-past-the-upper-bound -->
And at the top of the range — the bound that is **platform-shaped**, unlike the `0` the two cases above
meet. `4294967296`
is chosen because it is past the upper bound on *every* target and is therefore one case rather than
two: it is `u32.max + 1` on Windows, and on Linux, macOS and WASI it is far past the meaningful
boundary, which there is `256`. Only the range the diagnostic PRINTS differs, which is what the
qualified block carries.
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
error E3005: <fragment>:3:3: Value 4294967296 is outside the range of 'ExitCode' (int(0 to 255))
```
```Maxoncstderr:x64-windows
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
error E3005: <fragment>:7:10: Value -3 is outside the range of 'ExitCode' (int(0 to 255))
```
```Maxoncstderr:x64-windows
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
<!-- targets: x64-windows -->
⭐ **THE CLAIM IS "BOTH ENDS ARE ORDINARY VALUES", AND SINCE BATCH27 THE TOP END IS A DIFFERENT NUMBER PER
PLATFORM — so this is a TWIN PAIR, not one case with a marker.** A single case would have to pick one
platform's top bound and would then be asserting the claim on one lane only; two cases assert it on all
four. The bound is a `targets:` restriction rather than a `` ```Stdout: `` fence because `4294967295` is
not merely *printed* differently on POSIX — it is **not a legal `ExitCode` there at all**, so the program
does not compile and there is no stdout to name. That is exactly what a `targets:` omission is for.

This is the Windows half: `int(0 to u32.max)`, whose top bound sits in the band where a signed and an
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

<!-- test: both-bounds-are-reachable-values-on-posix -->
<!-- targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
The POSIX half of the pair above: the same claim against `int(0 to 255)`. `255` is the top of the
platform's exit-status domain and an ordinary value — the range is narrower than the `u32` slot it rides
in, and a bound being far from its slot's edge is not a reason for it to be unreachable.
```maxon
function main() returns ExitCode
  let low = 0 as ExitCode
  let high = 255 as ExitCode
  print("low={low} high={high}\n")
  return 0
end 'main'
```
```stdout
low=0 high=255
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

### ⭐⭐ THE DOOR WHERE THE ABI NARROWS BEFORE THE GUARD RUNS — CLOSED, AND NOT BY THE CURE THIS SECTION USED TO NAME

Every door here guards the value while it is still a full machine word: a `return`'s guard runs before
the `ret` narrows to the u32 return slot, a cast's before the value is used, a struct field's before the
store (a field declared `ExitCode` takes `alias.underlying`, an 8-byte slot, and an env slot is widened
outright by `envSlotStorageType`). **The call ARGUMENT is the one door whose guard reads the value AFTER
a narrowing** — `A1f` put the runtime half at the **callee's entry**, one guard per narrowed parameter,
standing in front of every caller including the indirect ones — and that rests on a premise a narrowing
ABI falsifies: *the value the callee sees is the value the caller passed.* **MEASURED:**
`takes(opaque(0) - 5)` arrives inside `takes` as `4294967291` on wasm, byte-for-byte the *legitimate*
`takes(opaque(4294967291))`. x64 refuses `-5` only because its registers never narrowed it.

⛔⛔ **THIS SECTION USED TO CONCLUDE "⇒ THE GUARD HAS TO MOVE TO THE CALLER", AND THAT WAS WRONG — the
diagnosis, not the observation.** The observation above is exact and stands. What did not survive is the
cure it was read as needing: an argument position in the Std IR for every argument (the side table records
only CONSTANT ones today, `recordConstantArgRangeChecks`) plus an answer to the indirect-call question
`A1f` chose the entry to solve. **None of that was built and none of it was needed.**

Read the sentence the old text argued from: *"at `u32` width `int(0 to u32.max)` **is** the FULL range,
so every bit pattern is in range and there is no predicate that separates the two."* That is true — and
it is a statement about **the range**, not about where the guard stands. The identity existed only
because X6 derived the RANGE from the WIDTH. Give `ExitCode` its true platform range and the two values
stop being the same value: `4294967291` is outside `int(0 to 255)`, and the entry guard X6 already built
separates them at the entry, where it already stood. **BATCH27 closed this door by narrowing the range
and changing nothing about the guard's position.**

⚠ **AND HERE IS WHAT WOULD RE-BREAK IT, because the cure is a RELATION between two numbers rather than a
number.** A guard at the callee's entry can only see a truncation while the **slot is WIDER than the
range**. `ExitCode`'s slot is deliberately still `u32` on every target: put a `u8` slot under it on a
POSIX lane — the width that "obviously" matches an `int(0 to 255)` — and `-5` arrives as `251`, which is
*inside* the range, and the guard passes it. The wrong answer returns wearing a check.
`Targets/Wasm/StdToWasm.maxon`'s `coerceOnStack` carries the same warning at the place someone would
reach for that `u8`.

⚠ The compile-time half of this door was never affected and is closed on every target — `takes(-3)`
above never builds.

<!-- test: computed-argument-is-guarded-at-the-callee-entry -->
<!-- targets: x64-windows, x64-linux -->
The half that held even before the range was narrowed: where the ABI passes the argument at full width,
the entry guard refuses it, and the panic names the parameter's own declaration line rather than the
caller's — one guard serves every call site, so the caller's line is not a fact it holds.
```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function takes(code ExitCode) returns ExitCode
  return code
end 'takes'

function main() returns ExitCode
  base = opaque(0)
  return takes(base - 5)
end 'main'
var base = 0
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

<!-- test: computed-argument-is-guarded-on-a-narrowing-abi -->
<!-- targets: wasm32-wasi -->
⭐⭐ **THE CASE THIS RUNG EXISTED TO TURN GREEN, AND IT WENT GREEN WITHOUT A LINE OF NEW MECHANISM.** The
wasm twin of the case above — the identical program; only the lane differs, and on this lane the ABI
narrows the argument to `i32` before the callee's guard ever sees it. It was DISABLED rather than given a
`targets:` line that omits wasm, because the two say different things: a `targets:` omission reads as
"this lane cannot express the program", and this lane expressed it perfectly and got the wrong answer.
It used to exit **251** — `-5` truncated to `u32` and then masked to a byte by WASI — with no diagnostic.

It is kept as a SEPARATE case from its x64 sibling rather than merged into one portable case because the
two pin different facts: the sibling says the guard catches a value the ABI *did not* touch, this one
says it still catches a value the ABI *did* narrow. A single portable case would assert the weaker of the
two on every lane and the stronger one nowhere.

⚠ **WHAT MADE IT PASS WAS THE RANGE, NOT THE GUARD'S POSITION** — see the section above. The truncated
`-5` still arrives as `4294967291`; what changed is that `4294967291` is no longer a value `ExitCode`
admits on this lane, so the guard that was already standing at the entry has something to refuse. The
shelving note said the fix was "a rung that moves the call-argument door's RUNTIME half back to the CALL
SITE"; that was a diagnosis written from the symptom, and it named a mechanism that never had to exist.
```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
  return n
end 'opaque'

function takes(code ExitCode) returns ExitCode
  return code
end 'takes'

function main() returns ExitCode
  base = opaque(0)
  return takes(base - 5)
end 'main'
var base = 0
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
	w = wide(5000)
	return try mk(b) otherwise w
end 'pick'

function main() returns ExitCode
	let v = pick(true)
	print("v={v}")
	return 7
end 'main'
var w = 0
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
