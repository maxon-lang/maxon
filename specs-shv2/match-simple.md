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


## Rules

- Block identifier required after `match <expression>` and on `end`
- Each case is a single line with exactly one statement
- All patterns in a case must be type-compatible with the scrutinee
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
		3 then result = result + 20 and fallthrough
		default then result = result + 100
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
		1 then result = 100
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
error E2046: specs/fragments/match-simple/error.match-enum-default.test:12:3: 'default' in a match on enum 'Color' must be followed by 'throws <error>' or 'panic("message")'
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
error E2001: specs/fragments/match-simple/error.match-expression-fallthrough.test:5:14: unexpected token: 'and'
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
error E2025: specs/fragments/match-simple/error.match-fallthrough-with-return.test:5:20: match fallthrough with return: 'cannot combine 'fallthrough' with 'return''
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
error E2026: specs/fragments/match-simple/error.match-enum-not-exhaustive.test:13:2: match on enum 'Color' is not exhaustive, missing: blue
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
error E2027: specs/fragments/match-simple/error.match-duplicate-pattern.test:6:3: duplicate pattern in match: '1'
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
error E2042: specs/fragments/match-simple/error.match-missing-block-id.test:4:9: missing block identifier
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
error E2043: specs/fragments/match-simple/error.match-mismatched-block-id.test:7:2: block identifier mismatch: expected 'check', got 'wrong'
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
error E2029: specs/fragments/match-simple/error.match-default-not-last.test:6:3: 'default' case must be the last case in match
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
error E2026: specs/fragments/match-simple/error.match-not-exhaustive.test:7:2: match is not exhaustive: add a 'default' arm
```

<!-- test: error.match-duplicate-bool-pattern -->

⚠ **shv2-authored — not a `/specs` case.** Added with sub-byte bit-packing, which widened the
duplicate-pattern recorder to bools as a side effect: a repeated `true` arm was **silently dead code**
before and is now refused, which is the existing overlap rule finally reaching the one pattern kind it
had been skipping.

⭐ **The point of the case is the SPELLING, not the refusal.** A bool arm folds to `BoolTrueValue`/
`BoolFalseValue`, so the first version of this diagnostic named `'1'` for a pattern the source writes as
`true` — a message quoting a number that appears nowhere in the program. Its siblings do not do that
(`enum-match-exhaustive` pins *"overlapping pattern in match: `'medium'` is already covered"*), and this
case is what stops it regressing to the folded value. Keyed on the TOKEN rather than the value's type,
because the question is about spelling: `-1` is two tokens, so a token-text rule has to leave every other
pattern kind on the folded constant, and this case pins only the arm that changed.
```maxon
typealias Integer = int(i64.min to i64.max)

function pick(b bool) returns Integer
	return match b 'p'
		true gives 1
		false gives 2
		true gives 3
	end 'p'
end 'pick'

function main() returns ExitCode
	return pick(true) as ExitCode
end 'main'
```
```maxoncstderr
error E2027: specs/fragments/match-simple/error.match-duplicate-bool-pattern.test:8:3: duplicate pattern in match: 'true'
```

<!-- test: match-arm-field-assignment -->
A match arm body may be a dotted field assignment (`state.flag = true`), not
just a bare `x = value`. The arm-body parser routes an identifier-started
statement through the shared statement parser so every assignment shape — plain,
dotted field-store, discard — is accepted. This mirrors the compiler's own
`scanArm64InstrForFrame`, whose arms set `state.hasCalls = true`.
```maxon
enum Op
	noop
	mark
	done
end 'Op'

type State
	export var flag = false

	export static function make() returns State
		return State{}
	end 'make'
end 'State'

function scan(op Op, state State)
	match op 'm'
		noop then break 'm'
		mark then state.flag = true
		done then break 'm'
	end 'm'
end 'scan'

function main() returns ExitCode
	var s = State.make()
	scan(Op.mark, state: s)
	return 1 if s.flag else 0
end 'main'
```
```exitcode
1
```


<!-- test: match-expr-result-from-method-call-arm -->
A `match` expression bound to a `let` whose value-producing arm is a METHOD or
SIBLING call (not a literal, not a free-function call) must still type the
binding from the call's return type, so a consumer of the bound value resolves
its receiver. The parser can't resolve a call's return type at parse time, so it
seeds the merge slot with an int-zero placeholder; TypeResolution upgrades the
slot from the arm's resolved producer type and must propagate that upgrade to the
binding AND its downstream method-call/convert consumers. Here `name` is bound to
`match t { 0 gives self.getName(0); default panic }` (a String method-call arm),
and `name.byteLength()` must dispatch on the String receiver — otherwise the
binding stays the int seed and the unresolved chain trips the lowering guard.
```maxon
typealias Idx = int(0 to 100)

type Lookup
	var names as StringArray

	export static function make() returns Lookup
		var a = StringArray.create()
		a.push("alpha")
		return Lookup{names: a}
	end 'make'

	export function getName(i Idx) returns String
		return try self.names.get(i) otherwise "?"
	end 'getName'

	export function nameLen(t Idx) returns Idx
		let name = match t 'pick'
			0 gives self.getName(0)
			default panic("empty")
		end 'pick'
		return name.byteLength() as Idx
	end 'nameLen'
end 'Lookup'

function main() returns ExitCode
	var l = Lookup.make()
	return l.nameLen(0)
end 'main'
```
```exitcode
5
```


<!-- test: match-expr-result-from-payload-binding-arm -->
A `match` expression bound to a `let` whose value arm gives a UNION PAYLOAD
BINDING must type the binding from the payload field's type, so a consumer of the
bound value resolves correctly. The match-merge block is created before the arm
blocks, so the producer-type walk records the merge load, the `let`, and the
consumer from the int-zero seed before the arm's payload-read store upgrades the
merge slot — the type then flows seed -> merge slot -> binding -> load -> call-arg
over successive TypeResolution convergence cycles, and a call argument whose type
an earlier cycle pinned to the seed must be unlocked once the real type arrives.
Here `n` is bound to `match op { alloc(name) gives name; default panic }` where
`name` is a `ByteArray` payload field, and `firstByte(n)` must accept it.
```maxon
typealias Idx = int(0 to 100)

union Op
	none
	alloc(name ByteArray)
end 'Op'

function firstByte(b ByteArray) returns Idx
	return try b.get(0) otherwise 0 as Idx
end 'firstByte'

function nameOf(op Op) returns Idx
	let n = match op 'pick'
		alloc(name) gives name
		default panic("none")
	end 'pick'
	return firstByte(n)
end 'nameOf'

function main() returns ExitCode
	var b = ByteArray.create()
	b.push(65)
	return nameOf(Op.alloc(b))
end 'main'
```
```exitcode
65
```
