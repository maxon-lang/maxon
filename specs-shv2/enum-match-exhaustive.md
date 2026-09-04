---
feature: enum-match-exhaustive
status: experimental
keywords: [enum, match, exhaustive, range, to, upto]
category: control-flow
---

# Enum Match Exhaustiveness

## Documentation

Enum match expressions require exhaustive case coverage, just like enum matches. Every enum case must be matched by either an explicit case pattern or a range pattern. Plain `default` is not allowed — use `default throws` if you want a catch-all that throws an error.

Range patterns use enum case references as bounds:

```text
match priority 'check'
    low to medium then print("not urgent")
    high to critical then print("urgent")
end 'check'
```

Ranges use the enum's ordinal values. `to` is inclusive on both ends, `upto` excludes the upper bound.

Overlapping patterns are not allowed — each enum case must be covered by exactly one arm.

## Tests

<!-- test: enum-exhaustive.all-explicit -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	let c = Color.green
	match c 'check'
		red then return 1
		green then return 2
		blue then return 3
	end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: enum-exhaustive.single-range -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	let c = Color.green
	match c 'check'
		red to blue then return 1
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.multiple-ranges -->
```maxon
enum Priority
		low
		medium
		high
		critical
end 'Priority'

function main() returns ExitCode
	let p = Priority.high
	match p 'check'
		low to medium then return 1
		high to critical then return 2
	end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: enum-exhaustive.mix-explicit-and-range -->
```maxon
enum Priority
		low
		medium
		high
		critical
end 'Priority'

function main() returns ExitCode
	let p = Priority.low
	match p 'check'
		low then return 1
		medium to critical then return 2
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.int-backed-range-covers-upper-bound -->
```maxon
enum Level
	low = 10
	medium = 20
	high = 30
end 'Level'

function classify(l Level) returns ExitCode
	let r = match l 'm'
		low to medium gives 41
		high gives 42
	end 'm'
	return r
end 'classify'

function main() returns ExitCode
	return classify(Level.medium)
end 'main'
```
```exitcode
41
```

<!-- test: enum-exhaustive.int-backed-range-covers-lower-bound -->
```maxon
enum Level
	low = 10
	medium = 20
	high = 30
end 'Level'

function classify(l Level) returns ExitCode
	let r = match l 'm'
		low to medium gives 41
		high gives 42
	end 'm'
	return r
end 'classify'

function main() returns ExitCode
	return classify(Level.low)
end 'main'
```
```exitcode
41
```

<!-- test: enum-exhaustive.int-backed-range-excludes-outside-case -->
```maxon
enum Level
	low = 10
	medium = 20
	high = 30
end 'Level'

function classify(l Level) returns ExitCode
	let r = match l 'm'
		low to medium gives 41
		high gives 42
	end 'm'
	return r
end 'classify'

function main() returns ExitCode
	return classify(Level.high)
end 'main'
```
```exitcode
42
```

<!-- test: enum-exhaustive.int-backed-range-is-declaration-order-not-raw-order -->
```maxon
enum Code
	ok = 500
	notFound = 200
	serverError = 404
end 'Code'

function classify(c Code) returns ExitCode
	let r = match c 'm'
		ok to notFound gives 41
		serverError gives 42
	end 'm'
	return r
end 'classify'

function main() returns ExitCode
	return classify(Code.serverError)
end 'main'
```
```exitcode
42
```

<!-- test: enum-exhaustive.int-backed-range-declaration-order-matches-second-case -->
```maxon
enum Code
	ok = 500
	notFound = 200
	serverError = 404
end 'Code'

function classify(c Code) returns ExitCode
	let r = match c 'm'
		ok to notFound gives 41
		serverError gives 42
	end 'm'
	return r
end 'classify'

function main() returns ExitCode
	return classify(Code.notFound)
end 'main'
```
```exitcode
41
```

<!-- test: enum-exhaustive.expression -->
```maxon
enum Status
		pending
		approved
		rejected
end 'Status'

function main() returns ExitCode
	let s = Status.approved
	let code = match s 'eval'
		pending gives 0
		approved to rejected gives 1
	end 'eval'
	return code
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.upto-range -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	let c = Color.red
	match c 'check'
		red upto blue then return 1
		blue then return 2
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.trailing-fallthrough -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	var x = 0
	let c = Color.blue
	match c 'check'
		red then x = 1
		green then x = 2
		blue then x = 3 and fallthrough
	end 'check'
	return x
end 'main'
```
```exitcode
3
```

<!-- test: enum-exhaustive.default-throws -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

enum AppError
		unmatched
end 'AppError'

typealias ColorCode = int(0 to 10)

function checkColor(c Color) returns ColorCode throws AppError
	match c 'check'
		red then return 1
		default throws AppError.unmatched
	end 'check'
end 'checkColor'

function main() returns ExitCode
	let result = try checkColor(Color.red) otherwise 'err'
		return 99
	end 'err'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-exhaustive.float-backed-range -->
```maxon
enum Threshold
		low = 0.1
		medium = 0.5
		high = 0.9
end 'Threshold'

function main() returns ExitCode
	let t = Threshold.medium
	match t 'check'
		low to high then return 1
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: error.enum-not-exhaustive -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	let c = Color.green
	match c 'check'
		red then return 1
		green then return 2
	end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/enum-match-exhaustive/error.enum-not-exhaustive.test:13:2: match on enum 'Color' is not exhaustive, missing: blue
```

<!-- test: error.union-not-exhaustive -->
```maxon
union Shape
	circle
	square
	triangle
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle
	match s 'check'
		circle then return 1
		square then return 2
	end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/enum-match-exhaustive/error.union-not-exhaustive.test:13:2: match on union 'Shape' is not exhaustive, missing: triangle
```

<!-- test: error.enum-not-exhaustive-lists-every-missing-case -->
```maxon
enum Color
		red
		green
		blue
		amber
		violet
end 'Color'

function main() returns ExitCode
	let c = Color.green
	match c 'check'
		red then return 1
		green then return 2
	end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/enum-match-exhaustive/error.enum-not-exhaustive-lists-every-missing-case.test:15:2: match on enum 'Color' is not exhaustive, missing: blue, amber, violet
```

<!-- test: error.enum-default-without-throws -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	let c = Color.blue
	match c 'check'
		red then return 1
		default then return 0
	end 'check'
end 'main'
```
```maxoncstderr
error E2046: specs/fragments/enum-match-exhaustive/error.enum-default-without-throws.test:12:3: 'default' in a match on enum 'Color' must be followed by 'throws <error>' or 'panic("message")'
```

<!-- test: error.enum-gap-in-ranges -->
```maxon
enum Priority
		low
		medium
		high
		critical
end 'Priority'

function main() returns ExitCode
	let p = Priority.high
	match p 'check'
		low to medium then return 1
		critical then return 2
	end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/enum-match-exhaustive/error.enum-gap-in-ranges.test:14:2: match on enum 'Priority' is not exhaustive, missing: high
```

<!-- test: error.enum-overlapping-ranges -->
```maxon
enum Priority
		low
		medium
		high
		critical
end 'Priority'

function main() returns ExitCode
	let p = Priority.high
	match p 'check'
		low to high then return 1
		medium to critical then return 2
	end 'check'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/enum-match-exhaustive/error.enum-overlapping-ranges.test:13:3: overlapping pattern in match: 'medium' is already covered
```

<!-- test: error.enum-explicit-overlaps-range -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	let c = Color.green
	match c 'check'
		red to blue then return 1
		green then return 2
	end 'check'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/enum-match-exhaustive/error.enum-explicit-overlaps-range.test:12:3: overlapping pattern in match: 'green' is already covered
```

<!-- test: enum-exhaustive.bare-case-names -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	let c = Color.green
	match c 'check'
		red then return 1
		green then return 2
		blue then return 3
	end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: enum-exhaustive.bare-case-range -->
```maxon
enum Priority
		low
		medium
		high
		critical
end 'Priority'

function main() returns ExitCode
	let p = Priority.high
	match p 'check'
		low to medium then return 1
		high to critical then return 2
	end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: enum-exhaustive.bare-case-expression -->
```maxon
enum Status
		pending
		approved
		rejected
end 'Status'

function main() returns ExitCode
	let s = Status.approved
	let code = match s 'eval'
		pending gives 0
		approved gives 1
		rejected gives 2
	end 'eval'
	return code
end 'main'
```
```exitcode
1
```

<!-- test: error.enum-qualified-case-name -->
```maxon
enum Color
		red
		green
		blue
end 'Color'

function main() returns ExitCode
	let c = Color.green
	match c 'check'
		Color.red then return 1
		Color.green then return 2
		Color.blue then return 3
	end 'check'
end 'main'
```
```maxoncstderr
error E3075: specs/fragments/enum-match-exhaustive/error.enum-qualified-case-name.test:11:3: use 'red' instead of 'Color.red' in match
```

### Cross-file scrutinee shadowed by a larger same-case-name union

A function parameter typed as a union declared in another file has an
unresolved scrutinee type at parse time. The parser must NOT guess the enum by
searching the registry for a type that declares the arms' case names — a larger
union sharing those case names (here `BigOp`, which also has `condBr` and `br`)
would shadow the intended `SmallOp` and emit a spurious E2026. Exhaustiveness
is deferred to TypeResolution, where the scrutinee's real type is known, so this
exhaustive match on `SmallOp` compiles cleanly.

<!-- test: cross-file-shadowed-union -->
```maxon
// --- file: ops.maxon
export union BigOp
	condBr
	br
	cmp
	call
	ret
end 'BigOp'

export union SmallOp
	condBr
	br
end 'SmallOp'

// --- file: main.maxon
typealias Code = int(0 to 125)

function classify(op SmallOp) returns Code
	return match op 'check'
		condBr gives 1
		br gives 2
	end 'check'
end 'classify'

function describeBig(op BigOp) returns Code
	return match op 'check'
		condBr gives 10
		br gives 20
		cmp gives 30
		call gives 40
		ret gives 50
	end 'check'
end 'describeBig'

function main() returns ExitCode
	let small = classify(SmallOp.br)
	let big = describeBig(BigOp.cmp)
	if big == 30 'bigOk'
		return small
	end 'bigOk'
	return 99
end 'main'
```
```exitcode
2
```

### Cross-file scrutinee with a range arm shadowed by a larger union

Same shadowing hazard, but the small union is covered by a single enum-case
range (`condBr to br`). The parser resolves range ordinals best-effort and can
misroute them to `BigSpan` (which also contains `condBr` and `br` but with extra
cases between them). The deferred check records the raw endpoint case names and
expands the range against `SpanOp`'s OWN ordinals, so the match is recognized as
exhaustive.

<!-- test: cross-file-shadowed-union-range -->
```maxon
// --- file: spans.maxon
export union BigSpan
	condBr
	cmp
	call
	br
	ret
end 'BigSpan'

export union SpanOp
	condBr
	br
end 'SpanOp'

// --- file: main.maxon
typealias Code = int(0 to 125)

function classify(op SpanOp) returns Code
	return match op 'check'
		condBr to br gives 7
	end 'check'
end 'classify'

function describeBig(op BigSpan) returns Code
	return match op 'check'
		condBr to ret gives 3
	end 'check'
end 'describeBig'

function main() returns ExitCode
	let span = classify(SpanOp.br)
	let big = describeBig(BigSpan.call)
	if big == 3 'bigOk'
		return span
	end 'bigOk'
	return 99
end 'main'
```
```exitcode
7
```

### Cross-file range arm where a misrouted enum would falsely report E2027

The sharpest form of the shadowing hazard: the small union (`NarrowOp`) covers a
bare case plus a range arm that do NOT overlap in `NarrowOp`'s OWN ordinals, but
a larger union declared earlier (`WideOp`) shares all the case names with a
DIFFERENT ordering in which the bare case falls INSIDE the range. Here `ret` is
ordinal 0 in `NarrowOp` (outside `call`..`param`), but ordinal 1 in `WideOp`
(between `call` and `param`). If the parser expanded the range `call to param`
against `WideOp` (the misroute), it would see `ret` as covered and emit a
spurious E2027 against the prior `ret` arm. Overlap detection is deferred to
TypeResolution, which expands the range against `NarrowOp`'s real ordinals, so
this exhaustive non-overlapping match compiles cleanly.

<!-- test: cross-file-range-no-false-overlap -->
```maxon
// --- file: ops.maxon
export union WideOp
	call
	ret
	param
	extra
end 'WideOp'

export union NarrowOp
	ret
	call
	param
end 'NarrowOp'

// --- file: main.maxon
typealias Code = int(0 to 125)

function classify(op NarrowOp) returns Code
	return match op 'check'
		ret gives 1
		call to param gives 2
	end 'check'
end 'classify'

function describeWide(op WideOp) returns Code
	return match op 'check'
		call to extra gives 9
	end 'check'
end 'describeWide'

function main() returns ExitCode
	let narrow = classify(NarrowOp.call)
	let wide = describeWide(WideOp.ret)
	if wide == 9 'wideOk'
		return narrow
	end 'wideOk'
	return 99
end 'main'
```
```exitcode
2
```

### Cross-file range arm with a GENUINE overlap still reports E2027

The deferral must not suppress real overlaps. Here the range `call to param`
genuinely covers `ret` (ordinal 1) in the scrutinee's OWN union, so the trailing
bare `ret` arm is a real overlap. TypeResolution detects it against the resolved
enum and reports E2027 at the offending arm's line/column.

<!-- test: error.cross-file-range-genuine-overlap -->
```maxon
// --- file: ops.maxon
export union WideOp
	call
	ret
	param
end 'WideOp'

// --- file: main.maxon
typealias Code = int(0 to 125)

function classify(op WideOp) returns Code
	return match op 'check'
		call to param gives 1
		ret gives 2
	end 'check'
end 'classify'

function main() returns ExitCode
	return classify(WideOp.call)
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/enum-match-exhaustive/error.cross-file-range-genuine-overlap.test:15:3: overlapping pattern in match: 'ret' is already covered
```
