---
feature: match-statements
status: experimental
keywords: match, then, gives, fallthrough, default
category: control-flow
---
# Match Statements

## Documentation

Match statements provide pattern matching on values, allowing you to execute different code based on the value of an expression. Each case is a single line with exactly one statement.

## Match Statement Syntax

```maxon
match <expression> 'identifier'
	<pattern> then <statement>
	<pattern1> or <pattern2> then <statement>
	default <statement>
end 'identifier'
```

**Example (simple match):**

```maxon
function main() returns ExitCode
	var x = 2
	match x 'check'
		1 then return 10
		2 then return 20
		default then return 0
	end 'check'
end 'main'
```
```exitcode
20
```

## Multiple Patterns with `or`

You can match multiple patterns in a single case using the `or` keyword:

```maxon
function main() returns ExitCode
	var input = 3
	match input 'eval'
		1 or 2 then return 10
		3 or 4 or 5 then return 20
		default then return 0
	end 'eval'
end 'main'
```
```exitcode
20
```

## Match Expressions

Match expressions return a value and can be used in variable assignments. Use `gives` instead of `then`:

```maxon
function main() returns ExitCode
	var x = 2
	let result = match x 'convert'
		1 gives 10
		2 gives 20
		3 gives 30
		default gives 0
	end 'convert'
	return result
end 'main'
```
```exitcode
20
```

## Fallthrough

By default, only the matching case executes. Use `and fallthrough` to continue to the next case's statement:

```maxon
function main() returns ExitCode
	var role = 1
	var permissions = 0
	match role 'auth'
		1 then permissions = permissions + 100 and fallthrough
		2 then permissions = permissions + 10 and fallthrough
		3 then permissions = permissions + 1
		default then permissions = 0
	end 'auth'
	return permissions
end 'main'
```
```exitcode
111
```

When `role = 1`, the first case matches (adds 100), falls through to case 2 (adds 10), falls through to case 3 (adds 1), giving a total of 111.

**Note:** Fallthrough is NOT allowed in match expressions since they must return a single value.

## Exhaustiveness for Enums and Unions

When matching on enum or union values, all cases must be covered. Plain `default` is not allowed — use `default throws ErrorType.case` if you want a catch-all that throws an error.

```maxon
union Direction
	north
	south
	east
	west
end 'Direction'

function main() returns ExitCode
	var dir = Direction.north
	match dir 'navigate'
		north then return 1
		south then return 2
		east then return 3
		west then return 4
	end 'navigate'
end 'main'
```
```exitcode
1
```

Enum matches also support range patterns using `to` (inclusive) and `upto` (exclusive upper bound), based on ordinal values. Overlapping patterns are reported as errors. See the `enum-match-exhaustive` spec for details.

If any case is missing, the compiler reports an error listing the uncovered cases.

## String Matching

You can match on string values using string literals as patterns:

```maxon
function main() returns ExitCode
	var name = "alice"
	match name 'greet'
		"alice" then return 1
		"bob" then return 2
		default then return 0
	end 'greet'
end 'main'
```
```exitcode
1
```

String matching uses the `equals` method from the `Equatable` interface, so any type that implements `Equatable` can be used as a match scrutinee.

## Range Patterns

Range patterns allow matching values within an interval. This is useful for numeric ranges, character classification, and grading systems.

**Syntax:**
- `1 to 5` - inclusive range (matches 1, 2, 3, 4, 5)
- `1 upto 5` - exclusive upper bound (matches 1, 2, 3, 4)
- `1 to max` - from 1 to infinity (open-ended upper)
- `min to 5` - from negative infinity to 5 inclusive (open-ended lower)
- `min upto 5` - from negative infinity to 5 exclusive (open-ended lower)

min/max are only valid for numeric ranges

**Integer ranges:**

```maxon
typealias Score = int(i64.min to i64.max)

function grade(score Score) returns Score
	match score 'grade'
		90 to 100 then return 65  // 'A'
		80 upto 90 then return 66   // 'B'
		70 upto 80 then return 67   // 'C'
		60 upto 70 then return 68   // 'D'
		0 upto 60 then return 70    // 'F'
		default then return 63   // '?'
	end 'grade'
end 'grade'

function main() returns ExitCode
	return grade(85)
end 'main'
```
```exitcode
66
```

**Character ranges:**

Characters implement the `Comparable` interface, so they can be used in range patterns.
The comparison is lexicographic (byte-by-byte).

```maxon
typealias Score = int(i64.min to i64.max)

function charType(c Character) returns Score
	match c 'classify'
		'a' to 'z' then return 1  // lowercase
		'A' to 'Z' then return 2  // uppercase
		'0' to '9' then return 3  // digit
		default then return 0    // other
	end 'classify'
end 'charType'

function main() returns ExitCode
	return charType('G')
end 'main'
```
```exitcode
2
```

**Open-ended ranges:**

```maxon
typealias Score = int(i64.min to i64.max)

function classify(age Score) returns Score
	match age 'category'
		min upto 0 then return 0       // invalid (negative)
		0 upto 18 then return 1     // minor
		18 to max then return 2       // adult
		default then return 0
	end 'category'
end 'classify'

function main() returns ExitCode
	return classify(25)
end 'main'
```
```exitcode
2
```

**Range patterns in match expressions:**

```maxon
function main() returns ExitCode
	var temp = 25
	let category = match temp 'weather'
		min upto 0 gives 1      // freezing
		0 upto 15 gives 2    // cold
		15 upto 25 gives 3   // mild
		25 to max gives 4      // warm
		default gives 0
	end 'weather'
	return category
end 'main'
```
```exitcode
4
```

**Combining ranges with `or`:**

```maxon
function main() returns ExitCode
	var x = 50
	match x 'check'
		1 to 10 or 90 to 100 then return 1   // extreme values
		default then return 0
	end 'check'
end 'main'
```
```exitcode
0
```

## Break

Use `break` in a match arm to exit the match early without executing any further code in the arm:

```text
match value 'label'
  1 then break                  // exits the match
  2 then break 'label'          // labeled break (same effect)
  default then doSomething()
end 'label'
```

When a match is inside a loop, an unlabeled `break` exits the match. Use a labeled break to exit the loop instead:

```text
while condition 'loop'
  match value 'check'
    1 then break              // exits match, continues loop
    2 then break 'loop'       // exits loop
    default then process()
  end 'check'
end 'loop'
```

`break` is not allowed in match expressions (with `gives`), since every arm must produce a value.

## Rules

- Block identifier required after `match <expression>` and on `end`
- Each case is a single line with exactly one statement
- All patterns in a case must be type-compatible with the scrutinee
- `break` exits the match statement (or a labeled enclosing loop/match)
- `and fallthrough` continues to the next case's statement
- `and fallthrough` not allowed in match expressions
- `and fallthrough` cannot be combined with `return`
- For enums, all cases must be covered by explicit or range patterns — plain `default` is forbidden (use `default throws`)
- For unions, all cases must be covered explicitly — plain `default` is forbidden (use `default throws`)
- `default` matches any value not matched by previous patterns (non-enum/union types only)
- Overlapping patterns are reported as errors
- `default` must be the last case if present

## Tests

<!-- test: match-statements.simple -->
```maxon
function main() returns ExitCode
	var x = 2
	match x 'check'
		1 then return 10
		2 then return 20
		default then return 0
	end 'check'
end 'main'
```
```exitcode
20
```

<!-- test: match-statements.default -->
```maxon
function main() returns ExitCode
	var x = 99
	match x 'check'
		1 then return 10
		2 then return 20
		default then return 0
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: match-statements.first-case -->
```maxon
function main() returns ExitCode
	var x = 1
	match x 'check'
		1 then return 10
		2 then return 20
		default then return 0
	end 'check'
end 'main'
```
```exitcode
10
```

<!-- test: match-statements.or-patterns -->
```maxon
function main() returns ExitCode
	var x = 3
	match x 'check'
		1 or 2 then return 10
		3 or 4 or 5 then return 20
		default then return 0
	end 'check'
end 'main'
```
```exitcode
20
```

<!-- test: match-statements.or-patterns-first -->
```maxon
function main() returns ExitCode
	var x = 1
	match x 'check'
		1 or 2 then return 10
		3 or 4 then return 20
		default then return 0
	end 'check'
end 'main'
```
```exitcode
10
```

<!-- test: match-statements.or-patterns-second -->
```maxon
function main() returns ExitCode
	var x = 2
	match x 'check'
		1 or 2 then return 10
		3 or 4 then return 20
		default then return 0
	end 'check'
end 'main'
```
```exitcode
10
```


<!-- test: match-expression.basic -->
```maxon
function main() returns ExitCode
	var x = 2
	let result = match x 'eval'
		1 gives 10
		2 gives 20
		default gives 0
	end 'eval'
	return result
end 'main'
```
```exitcode
20
```

<!-- test: match-expression.or-patterns -->
```maxon
function main() returns ExitCode
	var x = 4
	let result = match x 'eval'
		1 or 2 gives 10
		3 or 4 gives 20
		default gives 0
	end 'eval'
	return result
end 'main'
```
```exitcode
20
```

<!-- test: match-expression.default -->
```maxon
function main() returns ExitCode
	var x = 99
	let result = match x 'eval'
		1 gives 10
		2 gives 20
		default gives 0
	end 'eval'
	return result
end 'main'
```
```exitcode
0
```


<!-- test: match-statements.fallthrough -->
```maxon
function main() returns ExitCode
	var x = 1
	var result = 0
	match x 'check'
		1 then result = result + 10 and fallthrough
		2 then result = result + 20
		default then result = result + 100
	end 'check'
	return result
end 'main'
```
```exitcode
30
```

<!-- test: match-statements.fallthrough-chain -->
```maxon
function main() returns ExitCode
	var x = 1
	var result = 0
	match x 'cascade'
		1 then result = result + 10 and fallthrough
		2 then result = result + 20 and fallthrough
		3 then result = result + 30
		default then result = 100
	end 'cascade'
	return result
end 'main'
```
```exitcode
60
```

<!-- test: match-statements.fallthrough-to-default -->
```maxon
function main() returns ExitCode
	var x = 3
	var result = 0
	match x 'check'
		1 then result = 10
		2 then result = 20
		3 then result = result + 30 and fallthrough
		default then result = result + 100
	end 'check'
	return result
end 'main'
```
```exitcode
130
```

<!-- test: match-statements.nested-in-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

function categorize(n Integer) returns Integer
	match n 'cat'
		1 or 2 or 3 then return 1
		4 or 5 or 6 then return 2
		default then return 0
	end 'cat'
end 'categorize'

function main() returns ExitCode
	return categorize(5)
end 'main'
```
```exitcode
2
```

<!-- test: match-statements.assignment -->
```maxon
function main() returns ExitCode
	var x = 2
	var result = 0
	match x 'process'
		1 then result = 100
		2 then result = 200
		default then result = 0
	end 'process'
	return result
end 'main'
```
```exitcode
200
```

<!-- test: match-statements.function-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(n Integer) returns Integer
	return n * 2
end 'double'

function main() returns ExitCode
	var x = 2
	var result = 0
	match x 'process'
		1 then result = double(10)
		2 then result = double(20)
		default then result = 0
	end 'process'
	return result
end 'main'
```
```exitcode
40
```

<!-- test: match-enum.exhaustive -->
```maxon
union Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	var c = Color.green
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

<!-- test: error.match-enum-default -->
```maxon
union Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	var c = Color.blue
	match c 'check'
		red then return 1
		default then return 0
	end 'check'
end 'main'
```
```maxoncstderr
error E2046: specs/fragments/match-statements/error.match-enum-default.test:12:3: 'default' in a match on union 'Color' must be followed by 'throws <error>' or 'panic("message")'
```

<!-- test: match-enum.expression -->
```maxon
union Status
	pending
	approved
	rejected
end 'Status'

function main() returns ExitCode
	var s = Status.approved
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

<!-- test: match-enum.bare-case-names -->
```maxon
union Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	var c = Color.green
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

<!-- test: match-expression.used-in-expression -->
```maxon
function main() returns ExitCode
	var x = 2
	let doubled = match x 'eval'
		1 gives 10
		2 gives 20
		default gives 0
	end 'eval' * 2
	return doubled
end 'main'
```
```exitcode
40
```

<!-- test: error.match-expression-fallthrough -->
```maxon
function main() returns ExitCode
	var x = 1
	let result = match x 'eval'
		1 gives 10 and fallthrough
		default gives 0
	end 'eval'
	return result
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/match-statements/error.match-expression-fallthrough.test:5:14: unexpected token: 'and'
```

<!-- test: error.match-fallthrough-with-return -->
```maxon
function main() returns ExitCode
	var x = 1
	match x 'check'
		1 then return 10 and fallthrough
		default then return 0
	end 'check'
end 'main'
```
```maxoncstderr
error E2025: specs/fragments/match-statements/error.match-fallthrough-with-return.test:5:20: match fallthrough with return: 'cannot combine 'fallthrough' with 'return''
```

<!-- test: error.match-enum-not-exhaustive -->
```maxon
union Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	var c = Color.green
	match c 'check'
		red then return 1
		green then return 2
	end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/match-statements/error.match-enum-not-exhaustive.test:13:2: match on union 'Color' is not exhaustive, missing: blue
```

<!-- test: error.match-duplicate-pattern -->
```maxon
function main() returns ExitCode
	var x = 1
	match x 'check'
		1 then return 10
		1 then return 20
		default then return 0
	end 'check'
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/match-statements/error.match-duplicate-pattern.test:6:3: duplicate pattern in match: '1'
```

<!-- test: error.match-type-mismatch -->
```maxon
function main() returns ExitCode
	var x = 1
	match x 'check'
		"one" then return 10
		default then return 0
	end 'check'
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/match-statements/error.match-type-mismatch.test:5:3: pattern type 'String' does not match scrutinee type 'int'
```

<!-- test: error.match-missing-block-id -->
```maxon
function main() returns ExitCode
	var x = 1
	match x
		1 then return 10
		default then return 0
	end
end 'main'
```
```maxoncstderr
error E2042: specs/fragments/match-statements/error.match-missing-block-id.test:4:9: missing block identifier
```

<!-- test: error.match-mismatched-block-id -->
```maxon
function main() returns ExitCode
	var x = 1
	match x 'check'
		1 then return 10
		default then return 0
	end 'wrong'
end 'main'
```
```maxoncstderr
error E2043: specs/fragments/match-statements/error.match-mismatched-block-id.test:7:2: block identifier mismatch: expected 'check', got 'wrong'
```

<!-- test: error.match-default-not-last -->
```maxon
function main() returns ExitCode
	var x = 1
	match x 'check'
		default then return 0
		1 then return 10
		2 then return 20
	end 'check'
end 'main'
```
```maxoncstderr
error E2029: specs/fragments/match-statements/error.match-default-not-last.test:6:3: 'default' case must be the last case in match
```

<!-- test: match-string.simple -->
```maxon
function main() returns ExitCode
	var name = "alice"
	match name 'greet'
		"alice" then return 1
		"bob" then return 2
		default then return 0
	end 'greet'
end 'main'
```
```exitcode
1
```

<!-- test: match-string.second-case -->
```maxon
function main() returns ExitCode
	var name = "bob"
	match name 'greet'
		"alice" then return 1
		"bob" then return 2
		default then return 0
	end 'greet'
end 'main'
```
```exitcode
2
```

<!-- test: match-string.default -->
```maxon
function main() returns ExitCode
	var name = "charlie"
	match name 'greet'
		"alice" then return 1
		"bob" then return 2
		default then return 0
	end 'greet'
end 'main'
```
```exitcode
0
```

<!-- test: match-string.or-patterns -->
```maxon
function main() returns ExitCode
	var name = "carol"
	match name 'greet'
		"alice" or "bob" then return 1
		"carol" or "dave" then return 2
		default then return 0
	end 'greet'
end 'main'
```
```exitcode
2
```

<!-- test: match-string.expression -->
```maxon
function main() returns ExitCode
	var name = "bob"
	let code = match name 'lookup'
		"alice" gives 100
		"bob" gives 200
		default gives 0
	end 'lookup'
	return code
end 'main'
```
```exitcode
200
```

<!-- test: match-range.inclusive -->
```maxon
function main() returns ExitCode
	var x = 5
	match x 'check'
		1 to 5 then return 1
		default then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.inclusive-boundary -->
```maxon
function main() returns ExitCode
	var x = 1
	match x 'check'
		1 to 5 then return 1
		default then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.exclusive -->
```maxon
function main() returns ExitCode
	var x = 4
	match x 'check'
		1 upto 5 then return 1
		default then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.exclusive-boundary -->
```maxon
function main() returns ExitCode
	var x = 5
	match x 'check'
		1 upto 5 then return 1
		default then return 0
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: match-range.open-upper -->
```maxon
function main() returns ExitCode
	var x = 100
	match x 'check'
		10 to max then return 1
		default then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.open-lower-inclusive -->
```maxon
function main() returns ExitCode
	var x = 5
	match x 'check'
		min to 5 then return 1
		default then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.open-lower-exclusive -->
```maxon
function main() returns ExitCode
	var x = 5
	match x 'check'
		min upto 5 then return 1
		default then return 0
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: match-range.multiple-ranges -->
```maxon
function main() returns ExitCode
	var score = 85
	match score 'grade'
		90 to 100 then return 65
		80 upto 90 then return 66
		70 upto 80 then return 67
		default then return 70
	end 'grade'
end 'main'
```
```exitcode
66
```

<!-- test: match-range.negative -->
```maxon
function main() returns ExitCode
	var x = -5
	match x 'check'
		-10 to -1 then return 1
		default then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.expression -->
```maxon
function main() returns ExitCode
	var temp = 22
	let category = match temp 'weather'
		min upto 0 gives 1
		0 upto 15 gives 2
		15 upto 25 gives 3
		25 to max gives 4
		default gives 0
	end 'weather'
	return category
end 'main'
```
```exitcode
3
```

<!-- test: match-range.character -->
```maxon
function main() returns ExitCode
	var c = 'G'
	match c 'classify'
		'a' to 'z' then return 1
		'A' to 'Z' then return 2
		'0' to '9' then return 3
		default then return 0
	end 'classify'
end 'main'
```
```exitcode
2
```

<!-- test: match-range.character-lowercase -->
```maxon
function main() returns ExitCode
	var c = 'm'
	match c 'classify'
		'a' to 'z' then return 1
		'A' to 'Z' then return 2
		default then return 0
	end 'classify'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.character-digit -->
```maxon
function main() returns ExitCode
	var c = '7'
	match c 'classify'
		'a' to 'z' then return 1
		'A' to 'Z' then return 2
		'0' to '9' then return 3
		default then return 0
	end 'classify'
end 'main'
```
```exitcode
3
```

<!-- test: match-range.with-or -->
```maxon
function main() returns ExitCode
	var x = 95
	match x 'check'
		1 to 10 or 90 to 100 then return 1
		default then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.with-or-second -->
```maxon
function main() returns ExitCode
	var x = 5
	match x 'check'
		1 to 10 or 90 to 100 then return 1
		default then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-range.float -->
```maxon
function main() returns ExitCode
	var x = 2.5
	match x 'check'
		0.0 to 5.0 then return 1
		default then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: match-break.basic -->
```maxon
function main() returns ExitCode
	var result = 0
	match 2 'check'
		1 then result = 10
		2 then break
		default then result = 99
	end 'check'
	return result
end 'main'
```
```exitcode
0
```

<!-- test: match-break.labeled -->
```maxon
function main() returns ExitCode
	var result = 0
	match 3 'outer'
		1 then result = 10
		2 then result = 20
		3 then break 'outer'
		default then result = 99
	end 'outer'
	return result
end 'main'
```
```exitcode
0
```

<!-- test: match-break.inside-loop -->
```maxon
function main() returns ExitCode
	var result = 0
	var i = 0
	while i < 5 'loop'
		match i 'check'
			3 then break 'loop'
			default then result = result + 1
		end 'check'
		i = i + 1
	end 'loop'
	return result
end 'main'
```
```exitcode
3
```

<!-- test: match-break.exits-match-not-loop -->
```maxon
function main() returns ExitCode
	var result = 0
	var i = 0
	while i < 3 'loop'
		match i 'check'
			1 then break
			default then result = result + 10
		end 'check'
		result = result + 1
		i = i + 1
	end 'loop'
	return result
end 'main'
```
```exitcode
23
```

### Default Throws on Non-Enum Match

<!-- test: match-statements.default-throws-non-enum -->
```maxon
typealias Integer = int(0 to 100)

enum StringError
	notFound
end 'StringError'

function classify(s String) returns Integer throws StringError
	return match s 'check'
		"a" gives 1
		"b" gives 2
		default throws StringError.notFound
	end 'check'
end 'classify'

function main() returns ExitCode
	let a = try classify("a") otherwise 0
	let c = try classify("c") otherwise 99
	if a == 1 and c == 99 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Default Panic on Non-Enum Match

<!-- test: match-statements.default-panic-non-enum -->
```maxon
typealias Integer = int(0 to 100)

function classify(x Integer) returns Integer
	return match x 'check'
		1 gives 10
		2 gives 20
		default panic("unexpected value")
	end 'check'
end 'classify'

function main() returns ExitCode
	let result = classify(2)
	if result == 20 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Break in Exhaustive Enum Match

<!-- test: match-statements.break-exhaustive-enum -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function process(c Container) returns Integer
	var result = 0
	match c 'check'
		empty then break 'check'
		value(n) then result = n
	end 'check'
	return result
end 'process'

function main() returns ExitCode
	let a = process(Container.empty)
	let b = process(Container.value(42))
	if a == 0 and b == 42 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

