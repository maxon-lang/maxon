---
feature: if-statements
status: selfhosted
keywords: [if, else, conditional, branching, control flow]
category: control-flow
---

# If Statements

## Documentation

Execute code conditionally on a boolean expression. Each block carries a string
label after its condition and a matching label on its closing `end`:

**Simple if (no else):**

```maxon
if <condition> 'identifier'
	<statements>
end 'identifier'
```

**If-else** — `else` comes after the closing `end`, on the same line:

```maxon
if <condition> 'if_id'
	<statements>
end 'if_id' else 'else_id'
	<statements>
end 'else_id'
```

**Else-if chain** — the `else` branch is itself a nested `if`:

```maxon
if <cond1> 'case1'
	<statements>
end 'case1' else if <cond2> 'case2'
	<statements>
end 'case2' else 'default'
	<statements>
end 'default'
```

Lowering: the entry block evaluates the condition (a comparison, fused with
the branch — see `specs/comparison-operators.md`) and takes a two-way branch
to the then block or the false target (the else block, or the continuation when
there is no else). A branch that returns terminates itself; one that falls through
branches to the continuation. When both branches return, the continuation is
unreachable and emits no code.

## Tests

The first five cases — simple if, if-else (taken and not taken), the else-if chain, and a
nested if — each run with a single `x`, so only ONE arm of a chain is ever entered.
`else-if-chain-every-arm` closes that: it puts the chain in a helper and calls it once per arm,
so every arm executes.

<!-- test: if-statements.simple -->
```maxon
function main() returns ExitCode
	let x = 10
	if x > 5 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: if-statements.else -->
```maxon
function main() returns ExitCode
	let x = 5
	if x == 5 'is5'
		return 1
	end 'is5' else 'not5'
		return 0
	end 'not5'
end 'main'
```
```exitcode
1
```

<!-- test: if-statements.else-false -->
```maxon
function main() returns ExitCode
	let x = 3
	if x > 5 'gt5'
		return 1
	end 'gt5' else 'not_gt5'
		return 0
	end 'not_gt5'
end 'main'
```
```exitcode
0
```

<!-- test: if-statements.else-if-chain -->
```maxon
function main() returns ExitCode
	let x = 2
	if x == 1 'case1'
		return 1
	end 'case1' else if x == 2 'case2'
		return 2
	end 'case2' else 'default'
		return 0
	end 'default'
end 'main'
```
```exitcode
2
```

<!-- test: if-statements.nested -->
```maxon
function main() returns ExitCode
	let x = 3
	if x == 1 'outer'
		return 1
	end 'outer' else 'else_outer'
		if x == 2 'inner'
			return 2
		end 'inner' else 'else_inner'
			return 3
		end 'else_inner'
	end 'else_outer'
end 'main'
```
```exitcode
3
```

<!-- test: else-if-chain-every-arm -->
`if-statements.else-if-chain` and `if-statements.nested` each run with ONE `x`, so exactly
one arm of the chain is ever entered — the other arms are compiled and never executed, and a
miscompiled arm among them (a wrong constant, a branch to the wrong block) is invisible to the
exit code. Here the chain sits in a helper called once per arm, so EVERY arm returns on some
executed path.

Each arm is checked **on its own exit code**, not folded into one number. A weighted sum is
tempting and it is a trap: with results 0..3 and weights 1/3/9/27 the digits OVERFLOW the base,
so the encoding is not injective — `classify(0)` wrongly returning 3 and `classify(5)` wrongly
returning 0 gives `3·1 + 0·3 + 2·9 + 3·27 = 102`, the expected total, and the test reports PASS
on two miscompiled arms. Any combining arithmetic has some cancellation; a separate check has
none, and the failing exit code names the arm that broke.
```maxon
function classify(x Integer) returns Integer
	if x == 0 'zero'
		return 0
	end 'zero' else if x < 10 'small'
		return 1
	end 'small' else if x < 100 'medium'
		return 2
	end 'medium' else 'large'
		return 3
	end 'large'
end 'classify'

function main() returns ExitCode
	if classify(0) != 0 'zeroArm'
		return 1
	end 'zeroArm'

	if classify(5) != 1 'smallArm'
		return 2
	end 'smallArm'

	if classify(50) != 2 'mediumArm'
		return 3
	end 'mediumArm'

	if classify(200) != 3 'largeArm'
		return 4
	end 'largeArm'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: if-statements.else-if-in-helper -->
```maxon

typealias Integer = int(i64.min to i64.max)

function classify(x Integer) returns Integer
	if x == 0 'zero'
		return 0
	end 'zero' else if x < 10 'small'
		return 1
	end 'small' else if x < 100 'medium'
		return 2
	end 'medium' else 'large'
		return 3
	end 'large'
end 'classify'

function main() returns ExitCode
	let a = classify(0)
	let b = classify(5)
	let c = classify(50)
	let d = classify(200)
	return a + b * 3 + c * 9 + d * 27
end 'main'
```
```exitcode
102
```


<!-- test: if-statements.nested-if-with-multiple-returns -->
```maxon

typealias Integer = int(i64.min to i64.max)

function test(c Integer, next Integer) returns Integer
	if c == 0 'maybePrefix'
		if next == 1 'isHex'
			return 1
		end 'isHex'
		if next == 2 'isBinary'
			return 2
		end 'isBinary'
	end 'maybePrefix'
	return 42
end 'test'

function main() returns ExitCode
	return test(5, next: 0)
end 'main'
```
```exitcode
42
```


<!-- test: if-statements.nested-if-with-scoped-string -->
Variables declared inside if blocks go out of scope at the end of the block.
Return after the if should not attempt to clean up those variables.
```maxon

typealias Integer = int(i64.min to i64.max)

function test(x Integer) returns Integer
	if x == 0 'outer'
		let inner = "hello"
		if inner == "hello" 'checkInner'
			return 1
		end 'checkInner'
	end 'outer'
	return 42
end 'test'

function main() returns ExitCode
	return test(5)
end 'main'
```
```exitcode
42
```

<!-- test: if-statements.single-line-block-rejected -->
```maxon
function main() returns ExitCode
	if true 'x' return 1 end 'x'
	return 0
end 'main'
```
```maxoncstderr
error E2001: specs/if-statements/if-statements.single-line-block-rejected.maxon:3:14: Expected newline after block label, got 'return'
```

<!-- test: if-statements.statement-and-end-on-one-line-rejected -->
A statement ends its line, so the `end` closing its block cannot follow it there.
```maxon
function main() returns ExitCode
	if true 'x'
		return 1	end 'x'
	return 0
end 'main'
```
```maxoncstderr
error E2001: specs/if-statements/if-statements.statement-and-end-on-one-line-rejected.maxon:4:12: Expected newline after statement, got 'end'
```

<!-- test: if-statements.statement-after-return-on-one-line-is-unreachable -->
A second statement after a `return` on the same line is unreachable code, which is the more specific error.
```maxon
typealias Small = int(0 to 9)

function main() returns ExitCode
	var x = 1 as Small
	return x	x = 2
end 'main'
```
```maxoncstderr
error E3071: specs/if-statements/if-statements.statement-after-return-on-one-line-is-unreachable.maxon:6:11: unreachable code after 'return'
```

<!-- test: if-statements.self-assignment-after-return-on-one-line-is-unreachable -->
A statement's position is judged before its content: a self-assignment after a `return` on its line is unreachable code first.
```maxon
typealias Small = int(0 to 9)

function main() returns ExitCode
	var x = 1 as Small
	return x	x = x
end 'main'
```
```maxoncstderr
error E3071: specs/if-statements/if-statements.self-assignment-after-return-on-one-line-is-unreachable.maxon:6:11: unreachable code after 'return'
```

<!-- test: if-statements.block-if-sharing-a-line-with-a-statement -->
A block `if` written after a statement on the same line is refused at the `if`, before its block is read.
```maxon
typealias Small = int(0 to 9)

function main() returns ExitCode
	var x = 1 as Small
	print("one\n")	if x == 1 'a'
		x = 3
	end 'a'
	return x
end 'main'
```
```maxoncstderr
error E2001: specs/if-statements/if-statements.block-if-sharing-a-line-with-a-statement.maxon:6:17: Expected newline after statement, got 'if'
```

<!-- test: if-statements.shared-line-is-refused-before-the-block-it-opens -->
The shared line is the error even when the block it opens holds a later one.
```maxon
typealias Small = int(0 to 9)

function main() returns ExitCode
	var x = 1 as Small
	print("one\n")	while x < 3 'w'
		x = nope
	end 'w'
	return x
end 'main'
```
```maxoncstderr
error E2001: specs/if-statements/if-statements.shared-line-is-refused-before-the-block-it-opens.maxon:6:17: Expected newline after statement, got 'while'
```

<!-- test: if-statements.shared-line-names-nothing-past-its-first-token -->
The shared line is refused at its first token, before a qualified name after it is resolved.
```maxon
function main() returns ExitCode
	print("one\n")	nowhere.inner.shout()
	return 0
end 'main'
```
```maxoncstderr
error E2001: specs/if-statements/if-statements.shared-line-names-nothing-past-its-first-token.maxon:3:17: Expected newline after statement, got 'nowhere'
```

<!-- test: if-statements.self-assignment-sharing-a-line-is-the-self-assignment -->
A self-assignment written after a statement on its line is refused as the self-assignment, the more specific error.
```maxon
typealias Small = int(0 to 9)

function main() returns ExitCode
	var x = 1 as Small
	print("one\n")	x = x
	return x
end 'main'
```
```maxoncstderr
error E3067: specs/if-statements/if-statements.self-assignment-sharing-a-line-is-the-self-assignment.maxon:6:17: self-assignment has no effect: 'x = x'
```

<!-- test: if-statements.two-statements-on-one-line-rejected -->
One statement per line: a second statement after the first on the same line is refused.
```maxon
typealias Small = int(0 to 9)

function main() returns ExitCode
	var x = 1 as Small
	var y = 2 as Small
	x = 3	y = 4
	return x + y
end 'main'
```
```maxoncstderr
error E2001: specs/if-statements/if-statements.two-statements-on-one-line-rejected.maxon:7:8: Expected newline after statement, got 'y'
```

<!-- test: error.mismatched-if-end-label -->
A label written after `end` must repeat the block's opening label.
```maxon
function main() returns ExitCode
	let x = 1
	if x > 0 'positive'
		return 1
	end 'negative'
	return 0
end 'main'
```
```maxoncstderr
error E2008: <fragment>:6:2: Mismatched end label: expected 'positive', got 'negative'
```

<!-- test: error.mismatched-else-end-label -->
The `else` branch opens its own block, and its `end` repeats the `else` label, not the `if` label.
```maxon
function main() returns ExitCode
	let x = 0
	if x > 0 'positive'
		return 1
	end 'positive' else 'nonPositive'
		return 2
	end 'negative'
end 'main'
```
```maxoncstderr
error E2008: <fragment>:8:2: Mismatched end label: expected 'nonPositive', got 'negative'
```
