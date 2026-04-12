---
feature: ternary-expression
status: experimental
keywords: [ternary, conditional, if, else, inline]
category: expressions
---

# Ternary Expression

## Documentation

Ternary expressions provide a concise way to choose between two values based on a condition. The syntax places the true value first, followed by the condition, then the false value:

```text
<true_value> if <condition> else <false_value>
```

The condition must be a `bool` expression, and both arms must produce the same type.

**Basic usage:**

```maxon
function main() returns ExitCode
	let x = 10 if true else 20
	return x
end 'main'
```
```exitcode
10
```

**With comparisons:**

```maxon
function main() returns ExitCode
	let a = 5
	let b = 3
	let max = a if a > b else b
	return max
end 'main'
```
```exitcode
5
```

**With strings:**

```maxon
function main() returns ExitCode
	let flag = true
	let status = "Active" if flag else "Offline"
	print(status)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Active
```

**In string interpolation:**

```text
let msg = "Status: {"Active" if is_online else "Offline"}"
```

**Chaining:**

Ternary expressions can be chained. The else branch is parsed as a full expression, so chained ternaries associate to the right:

```text
let x = 1 if a else 2 if b else 3
// equivalent to: 1 if a else (2 if b else 3)
```

### Precedence

The ternary operator binds **looser** than all binary operators:

```text
a + b if cond else c * d
// equivalent to: (a + b) if cond else (c * d)
```

### Rules

- The condition must be a `bool` expression
- Both arms must produce the same type
- Binds looser than all binary operators
- Chainable via right-association of the else branch

## Tests

<!-- test: ternary-expression.basic-true -->
```maxon
function main() returns ExitCode
	let x = 10 if true else 20
	return x
end 'main'
```
```exitcode
10
```

<!-- test: ternary-expression.basic-false -->
```maxon
function main() returns ExitCode
	let x = 10 if false else 20
	return x
end 'main'
```
```exitcode
20
```

<!-- test: ternary-expression.with-variable-condition -->
```maxon
function main() returns ExitCode
	let flag = true
	let x = 1 if flag else 0
	return x
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-comparison -->
```maxon
function main() returns ExitCode
	let a = 5
	let b = 3
	let x = 1 if a > b else 0
	return x
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-strings -->
```maxon
function main() returns ExitCode
	let flag = true
	let s = "yes" if flag else "no"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
yes
```

<!-- test: ternary-expression.string-interp-expression -->
```maxon
function main() returns ExitCode
	let x = 5
	let msg = "value: {x if x > 0 else 0}"
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
value: 5
```

<!-- test: ternary-expression.string-interp-nested-strings -->
```maxon
function main() returns ExitCode
	let flag = true
	let msg = "status: {"on" if flag else "off"}"
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
status: on
```

<!-- test: ternary-expression.complex-expressions -->
```maxon
function main() returns ExitCode
	let a = 3
	let b = 2
	let cond = true
	let x = a + b if cond else a * b
	return x
end 'main'
```
```exitcode
5
```

<!-- test: ternary-expression.chained -->
```maxon
function main() returns ExitCode
	let a = false
	let b = true
	let x = 1 if a else 2 if b else 3
	return x
end 'main'
```
```exitcode
2
```

<!-- test: ternary-expression.in-return -->
```maxon
function main() returns ExitCode
	let done = true
	return 1 if done else 0
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-floats -->
```maxon
function main() returns ExitCode
	let x = 1.5 if true else 2.5
	return trunc(x)
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-bools -->
```maxon
function main() returns ExitCode
	let x = true if true else false
	let result = 1 if x else 0
	return result
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.false-path-strings -->
```maxon
function main() returns ExitCode
	let flag = false
	let s = "yes" if flag else "no"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
no
```

<!-- test: ternary-expression.error.type-mismatch -->
```maxon
function main() returns ExitCode
	let x = 10 if true else "hello"
	return 0
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/ternary-expression/ternary-expression.error.type-mismatch.test:3:13: ternary expression type mismatch: true branch is 'Integer' but false branch is 'Struct'
```

<!-- test: ternary-expression.error.non-bool-condition -->
```maxon
function main() returns ExitCode
	let x = 10 if 42 else 20
	return 0
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/ternary-expression/ternary-expression.error.non-bool-condition.test:3:13: ternary expression requires a bool condition, got 'Integer'
```
