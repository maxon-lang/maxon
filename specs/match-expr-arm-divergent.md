---
feature: match-expr-arm-divergent
status: experimental
keywords: [match, expression, panic, throws, divergent]
category: control-flow
---

# Match Expression Per-Arm `panic` / `throws`

## Documentation

In a match *expression* (the `gives` form), individual arms may use `panic("message")` or `throws ErrorType.case` in place of `gives <expr>`. Such an arm terminates control flow rather than producing a value, so the match expression's result type is inferred only from the `gives` arms. This mirrors the existing `default panic` / `default throws` catch-all forms applied to a specific pattern.

```text
let n = match c 'check'
    red panic("red not allowed here")
    green throws ColorError.unsupported
    blue gives 42
    default gives 0
end 'check'
```

A `throws` arm requires the enclosing function to declare `throws ErrorType`. A `panic` arm requires no declaration. Diverging arms participate in exhaustiveness checking exactly like `gives` arms — they cover their pattern.

Match *statements* (the `then` form) already permit `panic` and `throw` because every statement is allowed in an arm body; this feature extends only to the expression form.

## Tests

<!-- test: arm-panic.expression-not-hit -->
```maxon
function main() returns ExitCode
	let x = 3
	let result = match x 'eval'
		1 panic("not one")
		2 gives 20
		3 gives 30
		default gives 0
	end 'eval'
	return result
end 'main'
```
```exitcode
30
```

<!-- test: arm-panic.expression-hit -->
```maxon
function main() returns ExitCode
	let x = 1
	let result = match x 'eval'
		1 panic("got the bad one")
		2 gives 20
		3 gives 30
		default gives 0
	end 'eval'
	return result
end 'main'
```
```exitcode
1
```
```stderr
panic at arm-panic.expression-hit.test:5: got the bad one
Stack trace:
  in main
  in mrt_start
```

<!-- test: arm-throws.expression-not-hit -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum E
	bad
end 'E'

function pick(x Integer) returns ExitCode throws E
	let result = match x 'eval'
		1 throws E.bad
		2 gives 20
		3 gives 30
		default gives 0
	end 'eval'
	return result
end 'pick'

function main() returns ExitCode
	let r = try pick(2) otherwise 99
	return r
end 'main'
```
```exitcode
20
```

<!-- test: arm-throws.expression-hit -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum E
	bad
end 'E'

function pick(x Integer) returns ExitCode throws E
	let result = match x 'eval'
		1 throws E.bad
		2 gives 20
		3 gives 30
		default gives 0
	end 'eval'
	return result
end 'pick'

function main() returns ExitCode
	let r = try pick(1) otherwise 77
	return r
end 'main'
```
```exitcode
77
```

<!-- test: arm-panic.enum-exhaustive -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	let c = Color.blue
	let result = match c 'eval'
		red panic("no red")
		green gives 1
		blue gives 2
	end 'eval'
	return result
end 'main'
```
```exitcode
2
```

<!-- test: arm-throws.enum-exhaustive -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

enum E
	rejected
end 'E'

function classify(c Color) returns ExitCode throws E
	let result = match c 'eval'
		red throws E.rejected
		green gives 1
		blue gives 2
	end 'eval'
	return result
end 'classify'

function main() returns ExitCode
	let r = try classify(Color.green) otherwise 99
	return r
end 'main'
```
```exitcode
1
```

<!-- test: arm-panic.interpolated-message -->
```maxon
function main() returns ExitCode
	let x = 7
	let result = match x 'eval'
		7 panic("got value {x}")
		default gives 0
	end 'eval'
	return result
end 'main'
```
```exitcode
1
```
```stderr
panic at arm-panic.interpolated-message.test:5: got value 7
Stack trace:
  in main
  in mrt_start
```
