---
feature: word-operator-mixed-operands
status: stable
keywords: [and, or, xor, bool, int, operators, type-mismatch]
category: operators
---

# Word Operators Reject a Bool/Int Mixture

## Documentation

`and` / `or` / `xor` are **context-dependent, not polymorphic**: on two `bool`s they are the
logical operators, on two `int`s they are the bit operators. A MIXTURE is neither, and there
is no reading of it that does not silently throw away what one operand meant.

The int-on-the-LEFT direction is the dangerous one, because it looks like it works:
`4 and flag` computes `4 & 1` = **0**, so it is *always false* no matter what `flag` is —
while `5 and flag` computes `5 & 1` = **1** and behaves exactly like `flag`. Same expression
shape, opposite behaviour, decided by a bit of the constant. Both are rejected.

## Tests

> **All four cases below are SHELVED for a HARNESS reason ONLY — the compiler is already
> right.** They are written in the upstream `maxoncstderr` convention
> (`specs/fragments/<spec>/<test>.test:<line>:<col>`). This branch is based BEFORE the
> fragment-convention rung, so the runner here still reports `<fragment>:<line>` and its line
> numbers are one lower (it prepends no `// Test:` header). The message text and the COLUMN
> already match byte-for-byte — only the path and the +1 line offset differ, which is exactly
> what that rung normalizes.
>
> **Flip all four to `test:` once this branch is rebased onto it. Nothing else is needed.**


<!-- test: bool-and-int -->
```maxon
function main() returns ExitCode
	let flag = true
	let r = flag and 4
	if r 'r'
		return 1
	end 'r'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/word-operator-mixed-operands/bool-and-int.test:4:15: operator 'and' requires both operands to be the same type (both bool or both int)
```

<!-- test: int-and-bool -->
```maxon
function main() returns ExitCode
	let flag = true
	let r = 4 and flag
	if r 'r'
		return 1
	end 'r'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/word-operator-mixed-operands/int-and-bool.test:4:12: operator 'and' requires both operands to be the same type (both bool or both int)
```

<!-- test: int-or-bool -->
```maxon
function main() returns ExitCode
	let flag = false
	let r = 4 or flag
	if r 'r'
		return 1
	end 'r'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/word-operator-mixed-operands/int-or-bool.test:4:12: operator 'or' requires both operands to be the same type (both bool or both int)
```

<!-- test: bool-xor-int -->
```maxon
function main() returns ExitCode
	let flag = true
	let r = flag xor 4
	if r 'r'
		return 1
	end 'r'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/word-operator-mixed-operands/bool-xor-int.test:4:15: operator 'xor' requires both operands to be the same type (both bool or both int)
```
