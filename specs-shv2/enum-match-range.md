---
feature: enum-match-range
status: selfhosted
status-reason: all 10 cases PASS here, but 8 of its 10 committed goldens were minted by another compiler and disagree with what this one emits - un-suspending it therefore re-mints them, overwriting the only record of what v1 emitted for these programs (measured 2026-08-06, BATCH29/A3a, by un-suspending the file and running the full suite: 2 of 10). shv2 runs 9 of the 10 and is where a port belongs.
keywords: [enum, match, range, to, upto, bare case, exhaustive]
category: control-flow
---

# Enum Match Range Patterns

## Documentation

Enum match expressions support range patterns using `to` (inclusive) and `upto` (exclusive upper bound) on bare case names. This allows matching multiple consecutive cases in a single arm without listing each one individually.

```text
match op 'dispatch'
    maxon(hlOp) then lowerMaxonOp(hlOp, dstBlock: dstBlock)
    arith to arm64 then dstBlock.ops.push(op)
end 'dispatch'
```

Ranges use the enum's ordinal order (the order cases are declared). A range arm cannot extract bindings — it matches the cases without binding their payloads. Cases with associated values can be covered by a range, but their payloads are inaccessible in that arm.

Range patterns participate in exhaustiveness checking. Every case must be covered by exactly one arm, and overlapping patterns are rejected.

## Tests

<!-- test: enum-match-range.basic -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Action
	noop
	skip
	run(code Integer)
end 'Action'

function main() returns ExitCode
	let a = Action.skip
	let result = match a 'check'
		noop to skip gives 1
		run(code) gives code
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-match-range.first-in-range -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Action
	noop
	skip
	run(code Integer)
end 'Action'

function main() returns ExitCode
	let a = Action.noop
	let result = match a 'check'
		noop to skip gives 1
		run(code) gives code
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-match-range.binding-arm -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Action
	noop
	skip
	run(code Integer)
end 'Action'

function main() returns ExitCode
	let a = Action.run(42)
	match a 'dispatch'
		noop to skip then return 0
		run(code) then return code
	end 'dispatch'
end 'main'
```
```exitcode
42
```

<!-- test: enum-match-range.upto -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
	add
	sub
	mul
	exec(code Integer)
end 'Op'

function main() returns ExitCode
	let op = Op.mul
	let result = match op 'check'
		add upto mul gives 1
		mul gives 2
		exec(code) gives code
	end 'check'
	return result
end 'main'
```
```exitcode
2
```

<!-- test: enum-match-range.multiple-ranges -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
	add
	sub
	mul
	div
	exec(code Integer)
end 'Op'

function main() returns ExitCode
	let op = Op.div
	let result = match op 'check'
		add to sub gives 1
		mul to div gives 2
		exec(code) gives code
	end 'check'
	return result
end 'main'
```
```exitcode
2
```

<!-- test: enum-match-range.statement-form -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
	add
	sub
	mul
	exec(code Integer)
end 'Op'

function main() returns ExitCode
	let op = Op.sub
	match op 'check'
		add to mul then return 1
		exec(code) then return code
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-match-range.range-covers-all-associated -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Mixed
	a(x Integer)
	b(y Integer)
	c(z Integer)
end 'Mixed'

function main() returns ExitCode
	let m = Mixed.b(42)
	match m 'check'
		a to c then return 1
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-match-range.endpoint-name-collision -->
```maxon
// A range arm's endpoint case names (`lt`/`ge`) also exist on a DIFFERENT
// enum (`Cond`) at other ordinals — here `ge` precedes `lt`, both high, the
// way `Arm64CondCode` shadows `CmpPredicate` inside the compiler. The range
// endpoints must resolve against the SCRUTINEE's enum (`Pred`: lt=2..ge=5),
// not whichever enum a type-blind name search happens to reach first. Binding
// to `Cond` would yield the empty ordinal span [7,6], so the exhaustive match
// would match no arm for `Pred.gt` and fall through to a nil result.
union Cond
	eqc
	nec
	mi
	pl
	hic
	ls
	ge
	lt
	gtc
	lec
end 'Cond'

union Pred
	eq
	ne
	lt
	le
	gt
	ge
end 'Pred'

function classify(p Pred) returns String
	return match p 'm'
		eq gives "eq"
		ne gives "ne"
		lt to ge gives "ord"
	end 'm'
end 'classify'

function main() returns ExitCode
	let s = classify(Pred.gt)
	if s.isEmpty() 'empty'
		return 2
	end 'empty'
	return 0 if s == "ord" else 1
end 'main'
```
```exitcode
0
```

<!-- test: error.enum-match-range.overlap -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
	add
	sub
	mul
	exec(code Integer)
end 'Op'

function main() returns ExitCode
	let op = Op.add
	match op 'check'
		add to sub then return 1
		sub to mul then return 2
		exec(code) then return code
	end 'check'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/enum-match-range/error.enum-match-range.overlap.test:15:3: overlapping pattern in match: 'sub' is already covered
```

<!-- test: error.enum-match-range.not-exhaustive -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
	add
	sub
	mul
	div
	exec(code Integer)
end 'Op'

function main() returns ExitCode
	let op = Op.mul
	match op 'check'
		add to sub then return 1
		div then return 2
		exec(code) then return code
	end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/enum-match-range/error.enum-match-range.not-exhaustive.test:18:2: match on union 'Op' is not exhaustive, missing: mul
```
