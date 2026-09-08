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

<!-- test: error.enum-lt -->
An ORDERING operator on a union is refused exactly as `==`/`!=` are: a union carries no comparison of any kind, and `<` would compare two box addresses. The message names the operator the author actually wrote.
```maxon
typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let c = Container.empty
	if c < Container.empty 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3066: specs/fragments/enum-match-only/error.enum-lt.test:11:7: cannot compare union values using '<', use 'match' instead
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

<!-- test: union-return -->
A payload-bearing union RETURNED across a call (OPEN #44, closed at P1.4a). The callee builds an owned heap box and MOVES it out; the caller ADOPTS it (`let c = make()`) — owned, dropped exactly once at scope exit, no leak (a leak is exit 101). The result is recognized as owned via a `named` + `isBoxed` LAYOUT lookup, not the tag: a boxed union and a bare enum both carry the `named` tag, but only the boxed one owns a box the caller must free.
```maxon
typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function make() returns Container
	return Container.value(5)
end 'make'

function main() returns ExitCode
	let c = make()
	match c 'k'
		empty then return 0
		value(n) then return n
	end 'k'
end 'main'
```
```exitcode
5
```

<!-- test: error.return-wrong-union -->
A boxed union carries its NAME (`named`) at the parser, so the same aggregate identity check that
rejects a wrong struct rejects a wrong union: returning a `Palette` where a `Shape` is declared would
hand back the wrong boxed layout to be dropped under `Shape`'s destructor (OPEN #54). Union identity
is the interned name, exact — the `resolveTypes` → integer erasure that a scalar tag would see is a
later concern, and this is caught before it.
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(r Integer)
	square(s Integer)
end 'Shape'

union Palette
	red
	blue
end 'Palette'

function bad() returns Shape
	return Palette.red
end 'bad'

function main() returns ExitCode
	let s = bad()
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/enum-match-only/error.return-wrong-union.test:15:2: Cannot return 'Palette' from function declared to return 'Shape'
```

<!-- test: error.callarg-wrong-union-borrowed -->
The aggregate-identity check reaches CALL ARGUMENTS, not just returns (OPEN #54 Slice B2). Its subtlety
for a union is that a `union`/`enum` PARAMETER loses its name before the check runs — `resolveTypes`
erases the parameter's `named` tag to bare `integer` — so the check reads the name from a pre-erasure
carrier the parser stashes on the signature (`FuncSignature.paramAggregateNames`), the same carrier the
struct case does not need because a `structRef` survives resolution. Here `BoxA` carries a managed
`String` and `BoxB` a scalar, and `readA` merely BORROWS its argument, so nothing crashes — but handing
a `BoxB` where a `BoxA` is declared is a type error the callee would read through the wrong layout, and
it is rejected at the argument (this returned a garbage `1` before the check).
```maxon
typealias Integer = int(0 to i64.max)

union BoxA
	msg(text String)
end 'BoxA'

union BoxB
	code(n Integer)
end 'BoxB'

function readA(e BoxA) returns Integer
	match e 'k'
		msg then return 1
	end 'k'
end 'readA'

function main() returns ExitCode
	let bb = BoxB.code(5)
	return readA(bb) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/enum-match-only/error.callarg-wrong-union-borrowed.test:20:9: argument type mismatch for 'e': expected 'BoxA', got 'BoxB'
```

<!-- test: error.callarg-wrong-union-consumed -->
The union identity check is a memory-safety fix, not merely a wrong-answer one, exactly as for structs
(OPEN #54 Slice B2). At a CONSUMING argument — `WrapA.create` moves its `BoxA` argument into a managed
field — passing a `BoxB` would store the wrong box and later drop it under `BoxA`'s destructor, which
expects a `String`-carrying case and would free `BoxB`'s scalar as a heap pointer: a wild free the
scalar tag check cannot see (both unions erase to `integer`). Union identity is the interned name, EXACT
— a union is never a subtype of another — and the check is at the call argument, so it is caught
regardless of ownership.
```maxon
typealias Integer = int(0 to i64.max)

union BoxA
	msg(text String)
end 'BoxA'

union BoxB
	code(n Integer)
end 'BoxB'

type WrapA
	export var inner as BoxA

	static function create(inner BoxA) returns Self
		return Self{inner: inner}
	end 'create'
end 'WrapA'

function main() returns ExitCode
	let b = BoxB.code(7)
	let w = WrapA.create(b)
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/enum-match-only/error.callarg-wrong-union-consumed.test:22:16: argument type mismatch for 'inner': expected 'BoxA', got 'BoxB'
```

<!-- test: error.callarg-int-to-union -->
An aggregate meets a SCALAR: passing a bare `int` where a union is declared is a conflict too, because
`ValueTypeTag.named` is overloaded — a boxed union and a ranged-int alias share it — so the erased
`integer` parameter would agree with an integer argument on the tag alone. The identity check names the
scalar the argument actually is (`got 'int'`), not the union it was supposed to be.
```maxon
typealias Integer = int(0 to i64.max)

union BoxA
	msg(text String)
end 'BoxA'

function takesBoxA(e BoxA) returns Integer
	match e 'k'
		msg then return 1
	end 'k'
end 'takesBoxA'

function main() returns ExitCode
	return takesBoxA(7) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/enum-match-only/error.callarg-int-to-union.test:15:9: argument type mismatch for 'e': expected 'BoxA', got 'int'
```

<!-- test: callarg-union-same-type -->
The mirror of the rejections above: passing the SAME union the parameter declares COMPILES and runs, so
the identity check does not over-reject a correct call. `readA(BoxA.num(42))` returns 42.
```maxon
typealias Integer = int(0 to i64.max)

union BoxA
	msg(text String)
	num(n Integer)
end 'BoxA'

function readA(e BoxA) returns Integer
	match e 'k'
		msg then return 1
		num(n) then return n
	end 'k'
end 'readA'

function main() returns ExitCode
	return readA(BoxA.num(42)) as ExitCode
end 'main'
```
```exitcode
42
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
