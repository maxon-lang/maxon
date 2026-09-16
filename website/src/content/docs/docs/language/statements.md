---
title: Statements & Control Flow
description: Control flow, loops, match statements and expressions, and named blocks with labels.
sidebar:
  order: 9
---

Maxon statements are newline-delimited: one statement per line, no semicolons. Every block — `if`,
`else`, `while`, `for`, `match`, `try` — opens with a quoted **block label** and closes with `end` and the
same label. A missing label is **E2042**, and a match whose `end` names a different label is **E2043**.

## Expression Statements

A call on its own line is a statement. Its result must be used or explicitly discarded (see
[Function Purity and Discarded Results](/docs/language/functions/#function-purity-and-discarded-results)):

```maxon
print("hello\n")
_ = incrementAndGet()
```

## Return

```text
return <expression>
return
```

A function with a `returns` clause must return a value on every path; a path that falls off the end is
**E3013** (`missing return statement`). A function without `returns` uses a bare `return`, or none.

## Variable Declaration and Assignment

```maxon
var count = 10
let limit = 20
count = count + limit
```

Assigning to a `let` is **E2013**. Assigning a variable to itself (`x = x`, `p.x = p.x`) has no effect
and is **E3067**. See [Variables](/docs/language/variables/) for the full rules.

## Tuple Assignment

Assign the elements of a tuple to existing `var`s, declare new names in the same pattern, or discard
elements with `_`:

```maxon
var x = 0
var y = 0
(x, y) = makePair(10, b: 32)      // x = 10, y = 32
(x, let z) = makePair(3, b: 4)    // x existing, z newly declared
(y, _) = makePair(5, b: 6)        // discard the second element
```

- Every name without `var`/`let` must already be a `var`; a `let` target is **E2013**.
- The number of names must match the tuple's element count (**E3005**).
- Discarding every element of a pure function's result is **E3064**.

## If Statement

```text
if <condition> 'label'
	<statements>
end 'label'
```

`else` follows the closing `end` on the same line and opens its own labelled block. An else-if chain
nests another `if` in that position:

```maxon
if size == 0 'zero'
	print("empty\n")
end 'zero' else if size < 10 'small'
	print("small\n")
end 'small' else 'large'
	print("large\n")
end 'large'
```

- The condition must be `bool`; there is no implicit truthiness.
- An empty block is **E3082**. A block holding only a comment is empty too, since a comment is not a
  statement. This applies to every `if`, `else`, `while`, `for` and `try … otherwise` block.

## While Loop

```maxon
var i = 0
while i < 10 'loop'
	print("{i}\n")
	i = i + 1
end 'loop'
```

## For Loop

`for … in` iterates anything that implements `Iterable`: arrays, strings, maps, sets, lists, ranges and
your own types.

```maxon
let numbers = [1, 2, 3, 4, 5]
for num in numbers 'loop'
	print("{num}\n")
end 'loop'
```

**Ranges.** `a to b` is inclusive and `a upto b` excludes `b`. Integer and `Character` ranges are both
supported:

```maxon
for i in 1 to 5 'inclusive'      // 1, 2, 3, 4, 5
	print("{i}")
end 'inclusive'

for i in 1 upto 5 'exclusive'    // 1, 2, 3, 4
	print("{i}")
end 'exclusive'

for c in 'a' to 'z' 'letters'
	print("{c}")
end 'letters'
```

In a `for` header a range compiles to a counted loop with no allocation. Anywhere else an integer range is
a value: `a to b` is a `Range` and `a upto b` an `OpenRange`, both `Iterable`, so a range can be stored,
passed and iterated later:

```maxon
let r = 1 upto 4
for x in r 'loop'
	print("{x}")                 // 1, 2, 3
end 'loop'
```

**Destructuring.** When the elements are tuples — a map yields `(key, value)` — the loop variable can be
a tuple pattern:

```maxon
let ages = ["ada": 36, "alan": 41]
for (name, age) in ages 'loop'
	print("{name}: {age}\n")
end 'loop'
```

**Positions.** `.withIterator()` pairs each element with the iterator that produced it, which exposes
`index()` and navigation methods such as `advance()`, `retreat()`, `seek(index)` and `peek(ahead)`:

```maxon
let names = ["Alice", "Bob", "Charlie"]
for (iter, name) in names.withIterator() 'loop'
	print("{iter.index()}: {name}\n")
end 'loop'
```

**Notes:**
- The loop variable is immutable.
- Each loop obtains a fresh iterator (`createIterator()`), so a collection can be re-iterated and nested
  loops over one collection are safe.
- Loop variables must be used (**E3012**). Write `_` for one you do not need: `for _ in items 'loop'`,
  `for (key, _) in pairs 'loop'`.

## Break and Continue

```text
break              // leave the innermost loop
break 'label'      // leave the labelled enclosing loop
continue           // next iteration of the innermost loop
continue 'label'   // next iteration of the labelled enclosing loop
```

```maxon
var total = 0
for i in 0 upto 3 'rows'
	for j in 0 upto 3 'cols'
		if j == 2 'skip'
			continue 'rows'
		end 'skip'

		total = total + i + j
	end 'cols'
end 'rows'
```

A label that names the innermost loop is redundant and is **E2048**; use a label only to reach an outer
loop, or to reach a loop from inside a `match` arm (where a bare `break` leaves the `match`).

## Match Statement

`match` compares a value against patterns, one arm per line. Each arm is `pattern then <statement>` — a
single statement.

```maxon
typealias Score = int(i64.min to i64.max)
typealias Category = int(0 to 3)

function classify(n Score) returns Category
	match n 'check'
		0 then return 1
		1 to 5 then return 2
		6 upto 10 then return 3
		default then return 0
	end 'check'
end 'classify'
```

**Patterns:**

| Pattern | Matches |
|---------|---------|
| `42`, `"text"`, `'c'` | a single value |
| `a to b` | an inclusive range: `1 to 5` is 1 through 5 |
| `a upto b` | a range excluding `b`: `1 upto 5` is 1 through 4 |
| `min upto b`, `min to b`, `a to max` | an open-ended range: `min upto 0` is every negative value |
| `caseName` | an enum or union case (bare name, never `Type.case`) |
| `caseName(x, y)` | a union case, binding its associated values |
| `p1 or` ⏎ `p2` | any of several patterns, one per line |
| `default` | anything not matched above; must be the last arm |

Range patterns work on integers, floats and any `Comparable` type such as `Character`. Each bound is a
literal, or `min`/`max` for an open end. A range covering exactly one value (`5 to 5`, `'a' upto 'b'`) is **E2027** — write the
value. Covering a value or case twice is also **E2027**.

**Alternatives.** An arm covering several patterns joins them with `or`, **one alternative per line**; the
last alternative carries `then` and the body. Two alternatives on one line are **E3147**.

```maxon
match score 'grade'
	90 to 100 or
		85 to 89 then print("A\n")
	70 to 84 then print("B\n")
	default then print("C\n")
end 'grade'
```

**Exhaustiveness.** A match on an enum or union must name every case (**E2026** lists the missing ones).
A plain `default` arm on an enum or union is **E2046**: when a case is added later, a silent default would
absorb it. To ignore some cases, name them in an `or`-chain ending in `break`; to treat them as a bug or an
error, use `default panic("…")` or `default throws` (below). Matches on other types (integers, floats,
strings, characters) need a `default` arm unless the patterns cover every value.

```maxon
enum Level
	trace
	info
	warning
	error
end 'Level'

function report(level Level)
	match level 'filter'
		error then print("error!\n")
		trace or
			info or
			warning then break
	end 'filter'
end 'report'
```

There is no range over enum or union cases (`trace to warning` is **E3146**): a case declared inside the
span later would be absorbed without anyone deciding about it.

**Break and fallthrough.** `break` in an arm leaves the `match`; `break 'label'` leaves an enclosing loop.
`<statement> and fallthrough` runs the arm and then the next arm's body without testing its pattern:

```maxon
var result = 0
match x 'cascade'
	1 then result = result + 10 and fallthrough
	2 then result = result + 20
	default then result = 100
end 'cascade'
// x == 1 gives 30, x == 2 gives 20
```

**Union payloads.** `caseName(a, b)` binds a union case's associated values for that arm:

```maxon
typealias Amount = int(i64.min to i64.max)

union Outcome
	success(value Amount)
	failure(code Amount, message String)
	pending
end 'Outcome'

function show(r Outcome)
	match r 'handle'
		success(v) then print("ok {v}\n")
		failure(_, message) then print("failed: {message}\n")
		pending then print("waiting\n")
	end 'handle'
end 'show'
```

**Notes:**
- An arm body is one statement. Block-opening statements (`if`, `while`, `for`, a nested `match`, a
  multi-line `try`) are **E2049** — call a function instead. Every single-line `try` form is allowed.
- Arms use bare case names; `Level.trace` in an arm is **E3075**.
- Bindings must be used (**E3012**); discard one with `_`: `pair(_, second)`. To ignore all of a case's
  payload, omit the parentheses (`success then …`); `success(_)` with every binding discarded is **E3081**.
- `and fallthrough` cannot follow `return`.

## Match Expression

A match that produces a value uses `gives` instead of `then`:

```maxon
let points = match letterGrade 'convert'
	"A" gives 4
	"B" gives 3
	"C" gives 2
	default gives 0
end 'convert'
```

Every `gives` arm must produce the same type. `break` and `and fallthrough` are not allowed in a match
expression, since every arm must yield a value or leave.

**Diverging arms.** An arm may leave instead of producing a value, with `panic("…")` or
`throws ErrorType.case`. The result type comes from the `gives` arms; a diverging arm still counts toward
exhaustiveness. `throws` requires the enclosing function to declare that error type.

```maxon
function weight(c Color) returns Amount
	return match c 'weigh'
		red panic("red has no weight")
		green gives 1
		blue gives 2
	end 'weigh'
end 'weight'
```

## Default Throws and Default Panic

`default throws ErrorType.case` and `default panic("message")` are the two `default` forms allowed on an
enum or union, in both match statements and match expressions:

- **`default throws`** throws the error when no arm matches. The enclosing function must declare
  `throws ErrorType`, and the caller handles it with `try`.
- **`default panic("…")`** stops the program with that message (see
  [Panic](/docs/language/error-handling/#panic)). Use it for cases that indicate a bug.

```maxon
typealias Amount = int(i64.min to i64.max)

union Shape
	circle(radius Amount)
	square(side Amount)
	triangle(base Amount, height Amount)
end 'Shape'

union ShapeError implements Error
	unsupported(name String)
end 'ShapeError'

function area(shape Shape) returns Amount throws ShapeError
	return match shape 'calc'
		circle(r) gives 3 * r * r
		square(s) gives s * s
		default throws ShapeError.unsupported("triangle")
	end 'calc'
end 'area'

function main() returns ExitCode
	let a = try area(Shape.square(4)) otherwise 0
	print("{a}\n")                                    // 16
	return 0
end 'main'
```
