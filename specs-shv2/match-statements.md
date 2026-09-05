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
	let x = 2
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
	let input = 3
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
	let x = 2
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
	let role = 1
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

## Exhaustiveness for Enums and Enums

When matching on enum or enum values, all cases must be covered. Plain `default` is not allowed — use `default throws ErrorType.case` if you want a catch-all that throws an error.

```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'

function main() returns ExitCode
	let dir = Direction.north
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
	let name = "alice"
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
The comparison is lexicographic (byte-by-byte): the first differing byte decides, and where one
cluster's bytes are a prefix of the other's, the shorter cluster orders first.

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
	let temp = 25
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
	let x = 50
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
- For enums, all cases must be covered explicitly — plain `default` is forbidden (use `default throws`)
- `default` matches any value not matched by previous patterns (non-enum/enum types only)
- Overlapping patterns are reported as errors
- `default` must be the last case if present

## Tests

<!-- test: match-statements.simple -->
```maxon
function main() returns ExitCode
	let x = 2
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
	let x = 99
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
	let x = 1
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
	let x = 3
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
	let x = 1
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
	let x = 2
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
	let x = 2
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
	let x = 4
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
	let x = 99
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
	let x = 1
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
	let x = 1
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
	let x = 3
	var result = 0
	match x 'check'
		1 then result = 10
		2 then result = 20
		3 then result = result + 30 and fallthrough
		default then result = result + 90
	end 'check'
	return result
end 'main'
```
```exitcode
120
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
	let x = 2
	var result = 0
	match x 'process'
		1 then result = 60
		2 then result = 120
		default then result = 0
	end 'process'
	return result
end 'main'
```
```exitcode
120
```

<!-- test: match-statements.function-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(n Integer) returns Integer
	return n * 2
end 'double'

function main() returns ExitCode
	let x = 2
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

<!-- test: error.match-enum-default -->
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
error E2046: specs/fragments/match-statements/error.match-enum-default.test:12:3: 'default' in a match on enum 'Color' must be followed by 'throws <error>' or 'panic("message")'
```

<!-- test: match-enum.expression -->
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

<!-- test: match-enum.bare-case-names -->
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

<!-- test: match-expression.used-in-expression -->
```maxon
function main() returns ExitCode
	let x = 2
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
	let x = 1
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
	let x = 1
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
error E2026: specs/fragments/match-statements/error.match-enum-not-exhaustive.test:13:2: match on enum 'Color' is not exhaustive, missing: blue
```

<!-- test: error.match-duplicate-pattern -->
```maxon
function main() returns ExitCode
	let x = 1
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
	let x = 1
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
	let x = 1
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
	let x = 1
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
	let x = 1
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

<!-- test: error.match-block-statement -->
```maxon
function main() returns ExitCode
	let x = 1
	match x 'check'
		1 then if true 'inner'
			return 10
		end 'inner'
		default then return 0
	end 'check'
end 'main'
```
```maxoncstderr
error E2049: specs/fragments/match-statements/error.match-block-statement.test:5:10: block-opening statement 'if' is not allowed in a match arm; use a function call instead
```

<!-- test: error.match-not-exhaustive -->
```maxon
function main() returns ExitCode
	let x = 1
	match x 'check'
		1 then return 10
		2 then return 20
	end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/match-statements/error.match-not-exhaustive.test:7:2: match is not exhaustive: add a 'default' arm
```

### Single-Statement `try` Forms in Match Arms

A `match` arm body must be a single statement; the multi-line block-opening keywords `if`, `while`, `for`, `match` are rejected with E2049 (see above). All single-statement forms of `try` are permitted: bare propagation (`try call()`), and the four single-statement `otherwise` shapes (`panic`, `ignore`, `return/break/continue/throw`, default-value expression). The two multi-line block forms — `try 'label' ... end 'label'` and `try call() otherwise 'label' ... end 'label'` (with or without binding) — are rejected because they would allocate persistent error-flag slots in the enclosing scope and leak them on every error path through the match.

<!-- test: match-statements.try-propagate -->
```maxon
typealias Tally = int(0 to 100)

enum Err implements Error
	bad
end 'Err'

function leaf() throws Err
	throw Err.bad
end 'leaf'

function dispatch(kind Tally) throws Err
	match kind 'k'
		1 then try leaf()
		default throws Err.bad
	end 'k'
end 'dispatch'

function main() returns ExitCode
	try dispatch(1) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: match-statements.try-otherwise-panic -->
```maxon
enum Err implements Error
	bad
end 'Err'

function ok() throws Err
	return
end 'ok'

function dispatch(kind Tally)
	match kind 'k'
		1 then try ok() otherwise panic("unreachable")
		default panic("unreachable")
	end 'k'
end 'dispatch'

typealias Tally = int(0 to 100)

function main() returns ExitCode
	dispatch(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: match-statements.try-otherwise-ignore -->
```maxon
enum Err implements Error
	bad
end 'Err'

function leaf() throws Err
	throw Err.bad
end 'leaf'

function dispatch(kind Tally)
	match kind 'k'
		1 then try leaf() otherwise ignore
		default panic("unreachable")
	end 'k'
end 'dispatch'

typealias Tally = int(0 to 100)

function main() returns ExitCode
	dispatch(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: match-statements.try-otherwise-stmt -->
```maxon
enum Err implements Error
	bad
end 'Err'

function leaf() throws Err
	throw Err.bad
end 'leaf'

function dispatch(kind Tally) returns Score
	match kind 'k'
		1 then try leaf() otherwise return 7
		default panic("unreachable")
	end 'k'
	return 99
end 'dispatch'

typealias Tally = int(0 to 100)
typealias Score = int(0 to 100)

function main() returns ExitCode
	return dispatch(1)
end 'main'
```
```exitcode
7
```

<!-- test: match-statements.try-otherwise-default -->
```maxon
enum Err implements Error
	bad
end 'Err'

typealias Tally = int(0 to 100)
typealias Score = int(0 to 100)

function leaf() returns Score throws Err
	throw Err.bad
end 'leaf'

function dispatch(kind Tally) returns Score
	match kind 'k'
		1 then return try leaf() otherwise 42
		default panic("unreachable")
	end 'k'
end 'dispatch'

function main() returns ExitCode
	return dispatch(1)
end 'main'
```
```exitcode
42
```

<!-- test: error.match-try-block-form -->
```maxon
enum Err implements Error
	bad
end 'Err'

function leaf() throws Err
	throw Err.bad
end 'leaf'

function dispatch(kind Tally)
	match kind 'k'
		1 then try 'inner'
			leaf()
		end 'inner' otherwise (e) 'h'
			match e 'eh'
				bad then return
			end 'eh'
		end 'h'
		default panic("unreachable")
	end 'k'
end 'dispatch'

typealias Tally = int(0 to 100)

function main() returns ExitCode
	dispatch(1)
	return 0
end 'main'
```
```maxoncstderr
error E2049: specs/fragments/match-statements/error.match-try-block-form.test:12:10: block-form 'try ... end' is not allowed in a match arm; use a single-statement try form (e.g. 'try call()' or 'try call() otherwise panic(...)') instead
```

<!-- test: error.match-otherwise-block-form -->
```maxon
enum Err implements Error
	bad
end 'Err'

function leaf() throws Err
	throw Err.bad
end 'leaf'

function dispatch(kind Tally)
	match kind 'k'
		1 then try leaf() otherwise (e) 'h'
			match e 'eh'
				bad then return
			end 'eh'
		end 'h'
		default panic("unreachable")
	end 'k'
end 'dispatch'

typealias Tally = int(0 to 100)

function main() returns ExitCode
	dispatch(1)
	return 0
end 'main'
```
```maxoncstderr
error E2049: specs/fragments/match-statements/error.match-otherwise-block-form.test:12:35: block-form 'try ... otherwise 'label' ... end' is not allowed in a match arm; use 'otherwise panic("...")', 'otherwise ignore', or 'otherwise return/throw/...' instead
```

<!-- test: match-string.simple -->
```maxon
function main() returns ExitCode
	let name = "alice"
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
	let name = "bob"
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
	let name = "charlie"
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

<!-- test: match-string.pattern-decodes-escapes -->
### A string pattern is read like every other string literal
A pattern becomes the `.rdata` literal its arm compares against, and the scrutinee's own literal was
decoded on the way in — so both sides have to be read the SAME way. MEASURED 2026-08-26: the
bootstrap kept the raw token slice for the pattern only, so it compared the FOUR bytes `a\nb`
against a three-byte string, missed every arm and fell silently through to `default` — this program
exited **2** there and **6** under shv2. A wrong answer, not a diagnostic. `\{` is the literal brace
(see string-interpolation.md) and reads the same in a pattern as in the value matched against it.
```maxon
typealias Choice = int(0 to 9)

function pick(s String) returns Choice
	match s 'pick'
		"a\nb" then return 4
		"\{" then return 2
		default then return 1
	end 'pick'
end 'pick'

function main() returns ExitCode
	let a = pick("a\nb")
	let b = pick("\{")
	return a + b
end 'main'
```
```exitcode
6
```

<!-- test: match-string.or-patterns -->
```maxon
function main() returns ExitCode
	let name = "carol"
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
	let name = "bob"
	let code = match name 'lookup'
		"alice" gives 60
		"bob" gives 120
		default gives 0
	end 'lookup'
	return code
end 'main'
```
```exitcode
120
```

<!-- test: match-range.inclusive -->
```maxon
function main() returns ExitCode
	let x = 5
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
	let x = 1
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
	let x = 4
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
	let x = 5
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
	let x = 100
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
	let x = 5
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
	let x = 5
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
	let score = 85
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
	let x = -5
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
	let temp = 22
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

<!-- test: match-character-literal -->
```maxon
function main() returns ExitCode
	let c = 'ñ'
	match c 'check'
		'ñ' then print("eq\n")
		default then print("ne\n")
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
eq
```

<!-- test: match-character-range -->
```maxon
function main() returns ExitCode
	let c = 'ñ'
	match c 'check'
		'é' to 'ü' then print("inrange\n")
		default then print("out\n")
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
inrange
```

<!-- test: match-character-range-outside -->
A range test that only ever matches is not a range test. `Ω` is `CE A9`, whose FIRST byte is above `ü`'s `C3`, so it falls outside the range and the arm must not be taken.

```maxon
function main() returns ExitCode
	let c = 'Ω'
	match c 'check'
		'é' to 'ü' then print("inrange\n")
		default then print("out\n")
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
out
```

<!-- test: match-character-range-over-an-owned-scrutinee -->
The scrutinee is an OWNED `Character` the loop mints, while both range bounds are immortal `.rdata`
literals — so nothing the arm compares against is droppable and the loop's own temporary is dropped
exactly once. `x` (one ASCII byte, `0x78`) falls below `'é'` (`C3 A9`) and takes the default.

```maxon
function main() returns ExitCode
	let s = "éxü"
	var hits = 0
	for c in s 'each'
		match c 'classify'
			'é' to 'ü' then hits = hits + 1
			default then hits = hits + 100
		end 'classify'
	end 'each'
	print("{hits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
102
```

<!-- test: error.match-character-pattern-on-int-scrutinee -->
A `Character` pattern against an `int` scrutinee is an ordinary pattern/scrutinee type mismatch — the two
do not compare, exactly as a `String` pattern against an `int` one does not.

The pattern is a ZWJ family emoji, and that is the whole point of the case since A5m-ab: a match pattern is
an integer-expecting position, so a SINGLE-codepoint literal there converts to its codepoint (see
`match-character-literal-pattern-is-its-codepoint` below). A cluster is a SEQUENCE of codepoints and has no
integer reading at all, so it is the pattern that still cannot meet an `int`.

```maxon
function main() returns ExitCode
	let n = 5
	match n 'check'
		'👨‍👩‍👧‍👦' then return 1
		default then return 0
	end 'check'
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/match-statements/error.match-character-pattern-on-int-scrutinee.test:5:3: pattern type 'Character' does not match scrutinee type 'int'
```

<!-- test: match-character-literal-pattern-is-its-codepoint -->
### A character-literal pattern over an `int` scrutinee is its codepoint

A `match` pattern is an integer-expecting position when the scrutinee is integral, so the literal converts
exactly as `cp == '-'`'s operand does — one rule, one door (`Parser.integerizedOperand`). The multi-byte arm
is what makes it a CODEPOINT rule rather than a byte one: `'é'` is 233, which the oracle also compares
against.

```maxon
function main() returns ExitCode
	var hits = 0
	let dash = 45
	match dash 'ascii'
		'-' then hits = hits + 1
		default then hits = hits + 100
	end 'ascii'
	let accent = 233
	match accent 'wide'
		'é' then hits = hits + 1
		default then hits = hits + 100
	end 'wide'
	print("{hits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: match-range.character -->
```maxon
function main() returns ExitCode
	let c = 'G'
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
	let c = 'm'
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
	let c = '7'
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
	let x = 95
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
	let x = 5
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
	let x = 2.5
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

A `break` that leaves a match must release what the arm still owns, and nothing the
match does not own. Here a managed `String` is declared by the LOOP body, so it sits
BELOW the match's drop floor: the break jumps to the match's merge, which is still
inside the loop body, and the String must survive to be read on the next iteration
and released by the loop body's own `end`.

<!-- test: match-break.managed-string-live-across -->
```maxon
function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 3 'loop'
		let s = "abc"
		match i 'check'
			1 then break
			default then total = total + s.byteLength()
		end 'check'
		total = total + 1
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```exitcode
9
```

A boxed union scrutinee whose arm `break`s still drops its box exactly once — the
break leaves the match, not the function, so the scrutinee's own scope exit is what
releases it. The leak gate (exit 101) is the assertion that matters here.

<!-- test: match-break.managed-union-payload-arm -->
```maxon
union Note
	text(s String)
	none
end 'Note'

typealias Count = int(0 to 1000)

function weigh(n Note) returns Count
	var r = 0
	match n 'check'
		text then break
		none then r = 5
	end 'check'
	return r + 1
end 'weigh'

function main() returns ExitCode
	let a = weigh(Note.text("hello"))
	let b = weigh(Note.none)
	return a + b
end 'main'
```
```exitcode
7
```

Every arm `break`s, so no arm falls through — and the merge is still REACHABLE,
because a break jumps to it. A parser that counts only fall-through arms seals the
merge dead, drops the implicit return, and lets the merge fall into whatever block
the layout placed next.

<!-- test: match-break.every-arm-breaks -->
```maxon
typealias Count = int(0 to 1000)

function pick(k Count) returns Count
	let r = 7
	match k 'check'
		1 then break
		2 then break
		default then break
	end 'check'
	return r + 1
end 'pick'

function main() returns ExitCode
	return pick(1) + pick(2) + pick(5) - 16
end 'main'
```
```exitcode
8
```

Naming a MATCH's own label is never redundant-label (E2048): that diagnostic is about
loop labels, and `break 'check'` inside `match … 'check'` is exactly how both
reference compilers spell an explicit match exit.

<!-- test: match-break.match-own-label -->
```maxon
function main() returns ExitCode
	var r = 0
	var i = 0
	while i < 3 'loop'
		match i 'check'
			1 then break 'check'
			default then r = r + 10
		end 'check'
		r = r + 1
		i = i + 1
	end 'loop'
	return r
end 'main'
```
```exitcode
23
```

A labelled `break` still reaches PAST a match to an outer loop.

<!-- test: match-break.crosses-match-to-outer-loop -->
```maxon
function main() returns ExitCode
	var r = 0
	var i = 0
	while i < 3 'outer'
		var j = 0
		while j < 3 'inner'
			match j 'check'
				1 then break 'outer'
				default then r = r + 1
			end 'check'
			j = j + 1
		end 'inner'
		i = i + 1
	end 'outer'
	return r
end 'main'
```
```exitcode
1
```

A single-statement `otherwise break` inherits match targeting through the ordinary
break parser — it is not special-cased anywhere. With a BOXED error type the handler
owns a caught box that sits above the match's drop floor, so the break must release
it exactly once: the break's own scope drop, and NOT a second one from the handler's
live-exit drop. Both a leak and a double-free would show here.

<!-- test: match-break.otherwise-break-boxed-error -->
```maxon
typealias Count = int(0 to 1000)

union Err
	bad(s String)
end 'Err'

function risky(k Count) returns Count throws Err
	if k == 1 'fail'
		throw Err.bad("boom")
	end 'fail'
	return 10
end 'risky'

function pick(k Count) returns Count
	var r = 0
	match k 'check'
		1 then r = try risky(k) otherwise break
		default then r = 50
	end 'check'
	return r + 1
end 'pick'

function main() returns ExitCode
	return pick(1) + pick(2)
end 'main'
```
```exitcode
52
```

CONTROL. `continue` targets loops ONLY — a match is not an iteration — so an
intervening match does NOT make the innermost loop's label meaningful for it. The
redundant-label rule is unchanged for `continue`.

<!-- test: match-break.continue-across-match-still-redundant -->
```maxon
function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 3 'loop'
		match i 'check'
			1 then continue 'loop'
			default then total = total + 1
		end 'check'
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```maxoncstderr
error E2048: <fragment>:7:20: 'continue' with label 'loop' targets its own loop; use 'continue' without a label, or 'continue' with the label of an outer loop
```

CONTROL. The E2048 exemption keys on an intervening MATCH, not on "any intervening
construct": an `if` is not a break target, so a `break 'loop'` written inside one
still names the innermost loop redundantly.

<!-- test: match-break.loop-label-across-if-still-redundant -->
```maxon
function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 3 'loop'
		if i == 1 'check'
			break 'loop'
		end 'check'
		total = total + 1
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```maxoncstderr
error E2048: <fragment>:7:10: 'break' with label 'loop' targets its own loop; use 'break' without a label, or 'break' with the label of an outer loop
```

A `break` reached by an `and fallthrough` edge must carry the FALLTHROUGH path's
carried-variable values to the merge, not the match's entry values. The breaking
arm's body block has two predecessors — its own match edge and the previous arm's
fallthrough edge — so the values a break snapshots there are the fallthrough merge
phis. A break that snapshotted the pre-match values instead would silently discard
every assignment the fell-through arm made, and no existing case reaches a break
through a fallthrough edge.

<!-- test: match-break.fallthrough-into-break-arm -->
```maxon
function main() returns ExitCode
	var r = 0
	var n = 1
	match n 'check'
		1 then r = 5 and fallthrough
		2 then break
		default then r = 9
	end 'check'
	return r
end 'main'
```
```exitcode
5
```

⭐ **THE SEARCH ORDER IS MATCHES-BEFORE-LOOPS, AND IT IS OBSERVABLE.** When a match
statement and an enclosing loop carry the SAME label, a labelled `break 'dup'` names
the MATCH — the innermost construct wearing that label. Nothing else in the suite
distinguishes the two search loops in `resolveControlTarget`, so reversing them would
stay green everywhere else while changing this program from 32 to 10. Oracle-verified
against the bootstrap, which resolves it the same way.

<!-- test: match-break.match-label-shadows-loop-label -->
```maxon
function main() returns ExitCode
	var n = 0
	var trips = 0
	while n < 3 'dup'
		n = n + 1
		match n 'dup'
			1 then break 'dup'
			default then trips = trips + 1
		end 'dup'
		trips = trips + 10
	end 'dup'
	return trips
end 'main'
```
```exitcode
32
```

CONTROL, and the RUNTIME half of the `continue` rule the E2048 case above only
checks as a diagnostic. An UNLABELLED `continue` inside a match arm targets the
`for`'s STEP block, so the counter still advances — routing it at the match instead
does not merely pick the wrong destination, it spins forever (see `LoopContext`,
which both reference compilers carry the same warning on). This is the one shape that
fails if `resolveControlTarget` ever stops gating its match search on the keyword.

<!-- test: match-break.continue-in-for-reaches-step -->
```maxon
function main() returns ExitCode
	var seen = 0
	var skipped = 0
	for i in 0 upto 6 'each'
		match i 'check'
			2 then continue
			4 then continue
			default then seen = seen + 1
		end 'check'
		skipped = skipped + 10
	end 'each'
	return seen + skipped
end 'main'
```
```exitcode
44
```

A `String` scrutinee takes the per-arm compare CHAIN, not the interval-plan dispatch
that every other break case here exercises — a structurally different lowering, with
real `matchnext` test blocks the plan never mints. The break enrols its block as a
reaching arm the same way on both paths, and nothing else pins that.

<!-- test: match-break.string-scrutinee-chain-dispatch -->
```maxon
function main() returns ExitCode
	var total = 0
	let words = ["red", "green", "blue"]
	for w in words 'each'
		match w 'check'
			"green" then break
			default then total = total + 1
		end 'check'
		total = total + 10
	end 'each'
	return total
end 'main'
```
```exitcode
32
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

### Break-Only Arms Keep the Merge Reachable

A match statement whose every arm terminates — some with `panic`, the rest
with `break` — must still treat its merge block as reachable: the `break`
arms jump to it. A parser that only counts fall-through arms marks the merge
dead, skips the function's implicit return, and lets the merge fall through
into whatever block the layout places next (historically the panic arm's
body — the self-hosted `IrBlock.assertTerminated` panicked on every
well-terminated block in self-compiled builds).

<!-- test: match-statements.break-only-arms-with-panic-arm -->
```maxon
typealias Payload = int(0 to u64.max)

union Slot
	vacant
	reserved
	filled(value Payload)
end 'Slot'

type Holder
	export var slot as Slot

	export static function create() returns Holder
		return Self{slot: Slot.reserved}
	end 'create'

	export function assertUsable()
		match self.slot 'check'
			vacant then panic("slot is vacant")
			reserved then break 'check'
			filled then break 'check'
		end 'check'
	end 'assertUsable'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.assertUsable()
	h.slot = Slot.filled(7)
	h.assertUsable()
	print("usable\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
usable
```

