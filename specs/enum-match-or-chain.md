---
feature: enum-match-or-chain
status: experimental
keywords: [enum, match, or, alternative, bare case, exhaustive]
category: control-flow
---

# Enum Match Or-Chains

## Documentation

An enum match arm covering several cases names each of them with `or`, one alternative per line, the chain continuing after a trailing `or`.

```text
match op 'dispatch'
    maxon(hlOp) then lowerMaxonOp(hlOp, dstBlock: dstBlock)
    arith or
        cf or
        arm64 then dstBlock.ops.push(op)
end 'dispatch'
```

A bare alternative binds nothing. Cases with associated values may appear as bare alternatives, but their payloads are inaccessible in that arm.

Every alternative counts toward exhaustiveness. Each case must be covered by exactly one arm; covering one twice is E2027. A RANGE of cases is E3146.

## Tests

<!-- test: or-chain.basic -->
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
		noop or
			skip gives 1
		run(code) gives code
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: or-chain.first-alternative -->
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
		noop or
			skip gives 1
		run(code) gives code
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: or-chain.binding-arm -->
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
		noop or
			skip then return 0
		run(code) then return code
	end 'dispatch'
end 'main'
```
```exitcode
42
```

<!-- test: or-chain.two-alternatives -->
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
		add or
			sub gives 1
		mul gives 2
		exec(code) gives code
	end 'check'
	return result
end 'main'
```
```exitcode
2
```

<!-- test: or-chain.multiple-chains -->
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
		add or
			sub gives 1
		mul or
			div gives 2
		exec(code) gives code
	end 'check'
	return result
end 'main'
```
```exitcode
2
```

<!-- test: or-chain.statement-form -->
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
		add or
			sub or
			mul then return 1
		exec(code) then return code
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: or-chain.chain-covers-all-associated -->
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
		a or
			b or
			c then return 1
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: or-chain.case-name-collision -->
```maxon
// The alternatives' case names (`lt`/`ge`) also exist on a DIFFERENT
// enum (`Cond`) at other ordinals — here `ge` precedes `lt`, both high, the
// way `Arm64CondCode` shadows `CmpPredicate` inside the compiler. The chain
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
		lt or
			le or
			gt or
			ge gives "ord"
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

<!-- test: error.or-chain.overlap -->
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
		add or
			sub then return 1
		sub or
			mul then return 2
		exec(code) then return code
	end 'check'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/enum-match-or-chain/error.or-chain.overlap.test:16:3: overlapping pattern in match: 'sub' is already covered
```

<!-- test: error.or-chain.not-exhaustive -->
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
		add or
			sub then return 1
		div then return 2
		exec(code) then return code
	end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/enum-match-or-chain/error.or-chain.not-exhaustive.test:19:2: match on union 'Op' is not exhaustive, missing: mul
```
