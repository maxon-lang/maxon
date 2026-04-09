---
feature: enum-match-only
status: experimental
keywords: [enum, match, default throws, exhaustive, comparison]
category: type-system
---

# Enum Match-Only

## Documentation

### Enum Comparison Restriction

Enum values cannot be compared using `==` or `!=` operators. The only way to inspect an enum value is through `match` statements or expressions. This prevents a class of bugs where a new case is added to an enum but existing code silently falls through or uses a wrong default value -- the compiler forces every match site to handle all cases explicitly.

```text
// This is a compile error:
if dir == Direction.north 'check'  // ERROR: cannot compare union values using '=='
  ...
end 'check'

// Use match instead:
match dir 'check'
  north then ...
  south then ...
  east then ...
  west then ...
end 'check'
```

### Non-Exhaustive Match with `default throws`

When you intentionally want to handle only a subset of enum cases, use `default throws` as the last case. Unlike `default` with arbitrary code (which is forbidden on enums), `default throws` explicitly declares that unmatched cases throw an error that callers must handle.

**Statement form:**

```text
match value 'label'
  Case1 then statement
  Case2 then statement
  default throws MyError.unmatched
end 'label'
```

**Expression form:**

```text
let result = match value 'label'
  Case1 gives expr1
  Case2 gives expr2
  default throws MyError.unmatched
end 'label'
```

The enclosing function must declare `throws MyError`. Callers must use `try`/`otherwise` to handle the error.

## Tests

<!-- test: error.enum-eq -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let c = Container.empty
	if c == Container.empty 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3066: specs/fragments/enum-match-only/error.enum-eq.test:11:7: cannot compare union values using '==', use 'match' instead
```

<!-- test: error.enum-ne -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let c = Container.empty
	if c != Container.empty 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3066: specs/fragments/enum-match-only/error.enum-ne.test:11:7: cannot compare union values using '!=', use 'match' instead
```

<!-- test: error.enum-eq-method -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)

	function isEmpty() returns bool
		if self == Container.empty 'check'
			return true
		end 'check'
		return false
	end 'isEmpty'
end 'Container'

function main() returns ExitCode
	let c = Container.empty
	if c.isEmpty() 'test'
		return 1
	end 'test'
	return 0
end 'main'
```
```maxoncstderr
error E3066: specs/fragments/enum-match-only/error.enum-eq-method.test:9:11: cannot compare union values using '==', use 'match' instead
```

<!-- test: error.enum-eq-associated -->
```maxon

typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let a = Container.empty
	let b = Container.empty
	if a == b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3066: specs/fragments/enum-match-only/error.enum-eq-associated.test:13:7: cannot compare union values using '==', use 'match' instead
```

<!-- test: error.default-without-throws -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	let c = Color.green
	match c 'check'
		green then return 1
		default then return 0
	end 'check'
end 'main'
```
```maxoncstderr
error E2046: specs/fragments/enum-match-only/error.default-without-throws.test:12:3: 'default' in a match on enum 'Color' must be followed by 'throws <error>' or 'panic("message")'
```

<!-- test: default-throws-statement -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

enum MatchError
	unmatched
end 'MatchError'

function checkColor(c Color) returns ExitCode throws MatchError
	match c 'check'
		green then return 1
		default throws MatchError.unmatched
	end 'check'
end 'checkColor'

function main() returns ExitCode
	let c = Color.green
	let result = try checkColor(c) otherwise 0
	return result
end 'main'
```
```exitcode
1
```

<!-- test: default-throws-no-match -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

enum MatchError
	unmatched
end 'MatchError'

function checkColor(c Color) returns ExitCode throws MatchError
	match c 'check'
		red then return 1
		green then return 2
		default throws MatchError.unmatched
	end 'check'
end 'checkColor'

function main() returns ExitCode
	let c = Color.blue
	let result = try checkColor(c) otherwise 99
	return result
end 'main'
```
```exitcode
99
```

<!-- test: default-throws-expression -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

enum MatchError
	unmatched
end 'MatchError'

function colorValue(c Color) returns ExitCode throws MatchError
	let result = match c 'check'
		red gives 10
		green gives 20
		default throws MatchError.unmatched
	end 'check'
	return result
end 'colorValue'

function main() returns ExitCode
	let c = Color.green
	let result = try colorValue(c) otherwise 0
	return result
end 'main'
```
```exitcode
20
```

<!-- test: default-throws-associated-value -->
```maxon

typealias Integer = int(i64.min to i64.max)

union Result
	success(value Integer)
	failure(code Integer)
	pending
end 'Result'

enum MatchError
	unmatched
end 'MatchError'

function getValue(r Result) returns ExitCode throws MatchError
	match r 'check'
		success(v) then return v
		failure(c) then return c
		default throws MatchError.unmatched
	end 'check'
end 'getValue'

function main() returns ExitCode
	let r = Result.success(42)
	let result = try getValue(r) otherwise 0
	return result
end 'main'
```
```exitcode
42
```

<!-- test: enum-map-key-still-works -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

typealias Int = int(i64.min to i64.max)
typealias ColorMap = Map with (Color, Int)

function main() returns ExitCode
	var m = ColorMap.create()
	try m.insert(Color.red, value: 10) otherwise ignore
	try m.insert(Color.green, value: 20) otherwise ignore
	let result = try m.get(Color.green) otherwise 0
	return result
end 'main'
```
```exitcode
20
```
