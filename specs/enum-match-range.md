---
feature: enum-match-range
status: experimental
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
error E2026: specs/fragments/enum-match-range/error.enum-match-range.not-exhaustive.test:18:2: match on enum 'Op' is not exhaustive, missing: mul
```
