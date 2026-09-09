---
title: Statements & Control Flow
description: Control flow, loops, match statements and expressions, and named blocks with labels.
sidebar:
  order: 9
---

### Expression Statement
Any expression followed by newline:
```maxon
print(x)           // Single param is positional
add(3, b: 4)       // First positional, rest named
x = x + 1
```

### Return Statement
```maxon
return expression
```
Must appear in every code path of non-void functions.

### Variable Declaration
```maxon
var x = 10
let y = 20
```

### Assignment
```maxon
variable = expression
```

**Note:** Cannot assign to `let` variables (immutable).

Assigning a variable to itself is a compile error (E3067), since it has no effect:
```maxon
x = x         // ERROR: self-assignment has no effect
p.x = p.x     // ERROR: self-assignment has no effect
```

### Tuple Assignment

Assign multiple values from a tuple expression (or function returning a tuple) to existing mutable variables in a single statement:

```maxon
(variable1, variable2) = expression
```

**Notes:**
- All named variables must already be declared with `var`
- Immutable (`let`) variables cannot be targets (error E2013)
- The number of names must exactly match the tuple's element count (error E3005 on mismatch)
- Use `_` to discard individual elements
- If all elements are discarded (`(_, _) = ...`) and the function is pure, error E3064 is raised

**Example:**
```maxon
var x = 0
var y = 0
(x, y) = makePair(10, b: 32)  // x = 10, y = 32
```

**Discard individual elements:**
```maxon
(result, _) = compute()   // discard second element
(_, status) = fetch()     // discard first element
```

### If Statement

**Syntax**
```maxon
if condition 'label'
		statements
end 'label'
```

**With Else**
```maxon
if condition 'then'
		statements
end 'then' else 'else'
		statements
end 'else'
```

**Notes:**
- Block identifier required after `if` condition
- Block identifier must match on `else` and `end` keywords
- Condition must be `bool` type
- Can nest arbitrarily
- Empty blocks are a compile error (E3082) — every `if`, `else`, `while`, `for`, and `try...otherwise` block must contain at least one statement

### While Loop
```maxon
while condition 'label'
		statements
end 'label'
```

**Example:**
```maxon
var i = 0
while i < 10 'loop'
		print("{i}")
		i = i + 1
end 'loop'
```

### For Loop
```maxon
for variable in iterable 'label'
		statements
end 'label'
```

**Iterating over collections:**
```maxon
var numbers = [1, 2, 3, 4, 5]
for num in numbers 'loop'
		print("{num}")
end 'loop'
```

**Iterating over ranges:**

Ranges are created using `to` (inclusive) or `upto` (exclusive) expressions:

```maxon
// Inclusive range: 1, 2, 3, 4, 5
for i in 1 to 5 'loop'
		print("{i}")
end 'loop'

// Exclusive range: 1, 2, 3, 4
for i in 1 upto 5 'loop'
		print("{i}")
end 'loop'

// Character ranges
for c in 'a' to 'z' 'loop'
		print("{c}")
end 'loop'
```

Range expressions are supported for `int` and `Character`.

**Ranges as first-class values:**

Outside a `for-in` header, an integer range expression evaluates to a `Range` (`to`, inclusive) or `OpenRange` (`upto`, exclusive) value from the standard library. Both implement `Iterable`, so they can be bound to a variable, passed as an argument, or chained with `.createIterator()` / `.withIterator()`. Inside a `for-in` header the same syntax desugars directly to a counted loop with no allocation.

```maxon
let r = 1 upto 5                           // OpenRange value
for x in r 'loop' ... end 'loop'           // iterates 1, 2, 3, 4

let it = try (1 to 4).createIterator() otherwise return 0
for v in it 'loop' ... end 'loop'          // iterates 1, 2, 3, 4
```

Character ranges and ranges over user-defined types remain `for-in`-only.

**Iterating with the underlying iterator:**

Append `.withIterator()` to any iterable to get an `(Iterator, Element)` tuple — the iterator exposes navigation methods like `index()`, `advance()`, `retreat()`, `advanceBy(n)`, `retreatBy(n)`, `seek(index)`, and `peek(ahead)`:

```maxon
var names = ["Alice", "Bob", "Charlie"]
for (iter, name) in names.withIterator() 'loop'
		print("{iter.index()}: {name}\n")
end 'loop'
// 0: Alice
// 1: Bob
// 2: Charlie
```

This works on all iterable types (Array, String, Map, Set, List, etc.). The `WithIterIterator` is a lazy wrapper — no intermediate collection is created.

**Notes:**
- Loop variable is immutable (like `let`)
- Ranges use `to` for inclusive end and `upto` for exclusive end
- Desugars to a loop over the `Iterator` protocol: `advance(1)` (throws `IterationError.exhausted` at end) followed by `current()` (infallible read of the element in view)
- The compiler calls `createIterator()` before each loop to obtain a fresh iterator, enabling safe re-iteration and nested loops over the same collection
- Loop variables are checked for unused (E3012). Use `_` as the loop variable when the value is not needed: `for _ in array 'loop'`. In tuple destructuring, each element can be discarded independently: `for (key, _) in pairs 'loop'`

### Match Statement

Match statements provide pattern matching on values, executing different code based on the matched pattern. Each case is a single line with exactly one statement.

**Syntax**
```maxon
match expression 'label'
		pattern then statement
		pattern1 or pattern2 then statement
		pattern then statement and fallthrough
		pattern then break
		default then statement
end 'label'
```

**Example:**
```maxon
var x = 2
match x 'check'
		1 then return 10
		2 or 3 then return 20
		default then return 0
end 'check'
```

**With Fallthrough:**
```maxon
var result = 0
match x 'cascade'
		1 then result = result + 10 and fallthrough
		2 then result = result + 20
		default then result = 100
end 'cascade'
```

When `x = 1`, the first case matches, adds 10, then falls through to case 2 (adds 20), giving a total of 30.

**With Break:**

Use `break` in a match arm to exit the match without executing any code for that arm. An unlabeled `break` exits the innermost match. A labeled `break` can target any enclosing match or loop:

```maxon
while running 'loop'
		match state 'check'
				0 then break              // exits match, continues loop
				1 then break 'loop'       // exits loop
				default then process()
		end 'check'
end 'loop'
```

`break` is not allowed in match expressions (with `gives`), since every arm must produce a value.

**Enum Case Pattern Matching (Associated Values):**

For enums with associated values, use `CaseName(bindings)` syntax to extract values:

```maxon
enum Result
		success(value int)
		failure(code int)
end 'Result'

var r = Result.success(42)
match r 'handle'
		success(v) then return v      // v binds to 42
		failure(c) then return c
end 'handle'
```

**Notes:**
- Block identifier required after `match expression` and on `end`
- Each case is a single line with one statement
- Block-opening statements (`if`, `while`, `for`, nested `match`, and the multi-line `try ... end` / `try ... otherwise 'label' ... end` block forms) are rejected in match arms with **E2049**. All single-statement `try` forms are allowed: bare propagation (`try call()`), `try call() otherwise panic("...")`, `try call() otherwise ignore`, `try call() otherwise return/break/continue/throw ...`, and `try call() otherwise <expr>`.
- Multiple patterns can be combined with `or`
- `break` exits the match statement (or a labeled enclosing loop/match)
- `and fallthrough` continues to the next case (skipping its pattern check)
- `and fallthrough` cannot be combined with `return`
- For enums, all cases must be covered (error E2026) — plain `default` is not allowed (error E2046). This is a deliberate design choice: when a new case is added to an enum, a plain `default` arm would silently swallow it, hiding bugs that can be subtle and difficult to track down. By requiring exhaustive coverage, the compiler forces every match site to be reviewed when cases change, ensuring new variants are handled intentionally. To cover cases you don't need to handle individually, use range patterns with `break` (see [Enum Match Range Patterns](#enum-match-range-patterns) below), or use `default throws` / `default panic("message")` to signal that unhandled cases are errors (see [Default Throws / Default Panic in Match](#default-throws--default-panic-in-match) below). Enums use bare case names in match arms — qualified `Type.case` syntax is a compile error (E3075). Range patterns use bare case names as bounds (`case1 to case2`).
- Overlapping patterns are reported as errors (error E2027).
- All matches must be exhaustive. For non-enum matches (int, float, string, char), a `default` arm is required.
- `default` matches any non-enum value not matched by previous patterns
- `default` must be the last case if present
- Enum case patterns: `CaseName(binding1, binding2)` extracts associated values
- Pattern bindings are checked for unused (E3012). Use `_` to discard individual bindings: `pair(_, second)`
- To discard all associated values, omit the parentheses entirely: `success then ...` — using `success(_)` when all bindings are discarded is an error (E3081)

#### Enum Match Range Patterns

Enums with associated values support range patterns on bare case names using `to` (inclusive) and `upto` (exclusive upper bound). This allows matching a contiguous range of cases by their ordinal (declaration order) without listing each one individually.

```maxon
enum IrOp
		maxon(op MaxonOp)
		arith(op ArithOp)
		cf(op CfOp)
		func(op FuncOp)
end 'IrOp'

match op 'dispatch'
		maxon(hlOp) then lowerMaxonOp(hlOp, dstBlock: dstBlock)
		arith to func then dstBlock.ops.push(op)
end 'dispatch'
```

In this example, `arith to func` matches `arith`, `cf`, and `func` (inclusive). Using `arith upto func` would match `arith` and `cf` but not `func`. Cases with associated values can be covered by a range — their payloads are simply inaccessible in that arm.

**Rules:**
- A range arm cannot extract bindings. To extract associated values from a specific case, match it individually with binding syntax.
- Range bounds are based on ordinal order (the order cases are declared in the enum).
- Range patterns participate in exhaustiveness checking — they count toward full case coverage.
- Overlapping patterns (a range that covers a case also matched explicitly, or two overlapping ranges) are reported as errors (E2027).
- A range pattern that covers exactly one value is also rejected as E2027 — `red to red` and `red upto green` (when `green` is the case immediately after `red`) are mistakes; use the bare case name `red` instead.
- Range patterns can be combined with `or` and with explicit case patterns in the same match.

**Range Patterns:**

Range patterns match numeric values within a range using Rust-style syntax:

| Syntax | Meaning | Example |
|--------|---------|---------|
| `a..=b` | Inclusive range (a ≤ x ≤ b) | `1..=5` matches 1, 2, 3, 4, 5 |
| `a..<b` | Exclusive upper (a ≤ x < b) | `1..<5` matches 1, 2, 3, 4 |
| `a..` | Open upper bound (x ≥ a) | `100..` matches 100 and above |
| `..=b` | Open lower, inclusive (x ≤ b) | `..=0` matches 0 and below |
| `..<b` | Open lower, exclusive (x < b) | `..<0` matches negative numbers |
| `..` | Wildcard (matches any value) | `..` equivalent to `default` |

```maxon
function classify(n int) returns int
		match n 'check'
				1..=5 then return 1      // 1 to 5 inclusive
				6..<10 then return 2     // 6 to 9 (exclusive of 10)
				10.. then return 3       // 10 and above
				default then return 0    // negative numbers
		end 'check'
end 'classify'
```

Range patterns work with integers, floats, and any type implementing the `Comparable` interface (like `Character`):

```maxon
function charType(c Character) returns int
		match c 'classify'
				'a'..='z' then return 1  // lowercase letters
				'A'..='Z' then return 2  // uppercase letters
				'0'..='9' then return 3  // digits
				default then return 0    // other
		end 'classify'
end 'charType'
```

A range pattern that covers exactly one value is rejected as E2027 — `5 to 5`, `5 upto 6`, `'a' to 'a'`, and `'a' upto 'b'` (adjacent codepoints) are mistakes; use the bare value (`5`, `'a'`) instead.

Range patterns can be combined with `or`:

```maxon
match score 'grade'
		90..=100 or 85..=89 then return "A"
		70..=84 then return "B"
		default then return "C"
end 'grade'
```

### Match Expression

Match expressions return a value and can be assigned to variables. Use `gives` instead of `then`:

**Syntax**
```maxon
let result = match expression 'label'
		pattern1 gives value1
		pattern2 or pattern3 gives value2
		default gives defaultValue
end 'label'
```

**Example:**
```maxon
var grade = "B"
let points = match grade 'convert'
		"A" gives 4
		"B" gives 3
		"C" gives 2
		default gives 0
end 'convert'
```

**Enum Case Extraction:**
```maxon
enum Container
		empty
		value(n int)
end 'Container'

var c = Container.value(10)
var result = match c 'get'
		empty gives 0
		value(n) gives n * 2    // result = 20
end 'get'
```

**Notes:**
- All cases must return the same type
- `and fallthrough` is NOT allowed in match expressions
- Block identifier required
- Enum bindings work the same as in match statements

#### Per-Arm `panic` and `throws`

Because match expressions don't allow arbitrary statements in arm bodies, but you may still need a specific case to signal an unrecoverable error or throw a recoverable one, individual arms may use `panic("message")` or `throws ErrorType.case` in place of `gives <expr>`. The arm terminates instead of producing a value, so the match expression's result type is inferred only from the `gives` arms.

```maxon
let n = match c 'check'
		red panic("red not allowed here")
		green throws ColorError.unsupported
		blue gives 42
		default gives 0
end 'check'
```

- `panic("...")` arms accept either a string literal or an interpolated string, just like the `panic` statement and `default panic`.
- `throws ErrorType.case` arms require the enclosing function to declare `throws ErrorType` (the same rule as the `throw` statement and `default throws`).
- A diverging arm covers its pattern for exhaustiveness purposes exactly like a `gives` arm.
- This applies only to match *expressions* — match statements already accept `panic`/`throw` via the normal `then <statement>` form.

### Default Throws / Default Panic in Match

When matching on an enum, all cases must normally be covered explicitly (exhaustive matching). Plain `default` is forbidden because it defeats the purpose of exhaustiveness: if a new case is added to the enum later, the `default` arm would silently handle it, often with incorrect behavior. This class of bug — adding a new variant and forgetting to update match sites — is a common source of subtle, hard-to-diagnose errors in languages that allow catch-all defaults on sum types.

To handle only a subset of cases, you have two options:

1. **Range patterns with `break`**: When the unhandled cases are not errors — you simply don't need to act on them — cover them with a range pattern and `break`. This still participates in exhaustiveness checking, so new cases outside the range will be flagged by the compiler.

```maxon
match level 'filter'
		error then handleError()
		fatal then handleFatal()
		trace to warning then break
end 'filter'
```

2. **`default throws` or `default panic("message")`**: When unhandled cases represent genuine errors that should not occur silently:

- **`default throws`** throws the specified error when no other case matches. The enclosing function must declare `throws ErrorType` to use this feature. The error is catchable by the caller.
- **`default panic("message")`** terminates the program with an error message when no other case matches. This is not catchable and should be used for cases that represent programming errors.

Both forms work in all match types (enum and primitive types).

**Statement Form:**

```maxon
function handleShape(shape Shape) throws ShapeError
		match shape 'draw'
				circle(r) then drawCircle(r)
				square(s) then drawSquare(s)
				default throws ShapeError.unsupported
		end 'draw'
end 'handleShape'
```

If `shape` is `triangle`, the function throws `ShapeError.unsupported`, which the caller must handle with `try`.

**Statement Form (panic):**

```maxon
function handleShape(shape Shape)
		match shape 'draw'
				circle(r) then drawCircle(r)
				square(s) then drawSquare(s)
				default panic("unsupported shape")
		end 'draw'
end 'handleShape'
```

If `shape` is `triangle`, the program terminates with the message "unsupported shape".

**Expression Form:**

```maxon
function describeShape(shape Shape) returns String throws ShapeError
		let desc = match shape 'describe'
				circle(r) gives "circle with radius {r}"
				square(s) gives "square with side {s}"
				default throws ShapeError.unsupported
		end 'describe'
		return desc
end 'describeShape'
```

**Example:**

```maxon
enum Shape
		circle(radius float)
		square(side float)
		triangle(base float, height float)
end 'Shape'

enum ShapeError implements Error
		unsupported
end 'ShapeError'

function getArea(shape Shape) returns float throws ShapeError
		return match shape 'calc'
				circle(r) gives 3.14159 * r * r
				square(s) gives s * s
				default throws ShapeError.unsupported
		end 'calc'
end 'getArea'

function main() returns ExitCode
		var shape = Shape.circle(5.0)
		let area = try getArea(shape) otherwise 0.0
		print("{area}")
		return 0
end 'main'
```

**Notes:**
- `default throws` and `default panic("message")` are the only forms of `default` allowed in enum matches -- `default` with arbitrary code on enums is forbidden (error E2046)
- For `default throws`: the error value must be a valid enum case of an `Error`-conforming type, the enclosing function must declare `throws` with a matching error type, and callers must handle the thrown error using `try ... otherwise` or `try` propagation
- For `default panic("message")`: the program terminates immediately with the given message. No `throws` declaration is required.
- Supports all the same features as regular match: associated value extraction, `and fallthrough`, `break`, etc.
- For non-enum matches (int, float, string, char), `default` with arbitrary code remains valid as before

### Break Statement
```maxon
break           // Break from innermost loop
break 'label'   // Break from loop with specified label
```
Exits the innermost loop (while or for), or breaks to a specific labeled loop.

**Example:**
```maxon
while true 'outer'
		while true 'inner'
				break 'outer'  // Breaks out of outer loop
		end 'inner'
end 'outer'
```

Labeling a `break` with the innermost enclosing loop's own label is
redundant — unlabeled `break` already targets that loop — and is rejected
as E2048. The label is meaningful only when an outer loop must be exited,
or when a `match` arm sits between the `break` and its target loop.

### Continue Statement
```maxon
continue           // Continue innermost loop
continue 'label'   // Continue loop with specified label
```
Skips to next iteration of the innermost loop, or continues to a specific labeled loop.

Like `break`, `continue 'label'` is rejected as E2048 when `label` names
the innermost enclosing loop. Use bare `continue` for the innermost loop,
or a label only when an outer loop is the actual target.

---
