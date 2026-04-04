---
name: maxon-coder
description: Write or modify Maxon (.maxon) code. Use this skill whenever you need to create, edit, or review Maxon source files. Ensures correct syntax and avoids common mistakes.
---

Read `docs/WRITING_MAXON_CODE.md` before writing any Maxon code. It contains mandatory syntax rules, common mistakes, and the correct patterns. Refer to `docs/LANGUAGE_REFERENCE.md` for full specification and `docs/QUICK_REFERENCE.md` for API reference.

## Critical rules (most common mistakes)

- NEVER use bare `int`, `float`, or `byte` as types — ALWAYS use a typealias with range. `bool` is the exception.
- NEVER use `:` for type annotations on variables — `let x = 5` not `let x: int = 5`
- NEVER use `++`, `+=`, `%`, `&&`, `||`, `!`, `&`, `|`, `^`, `<<`, `>>` — use `x = x + 1`, `mod`, `and`, `or`, `not`, `xor`, `shl`, `shr`
- NEVER use curly braces for blocks — use `'label'` ... `end 'label'`
- NEVER use semicolons
- NEVER use string concatenation (`+`) — use interpolation: `"hello {name}"`
- NEVER use `null`/`nil`/`None` — use `try...otherwise`
- EVERY block (if, else, while, for, match, try...otherwise, function, type, enum, interface, extension) MUST have a label and matching `end 'label'`
- `else` MUST appear on the same line as its `end`: `end 'check' else 'other'`
- `main` MUST return `ExitCode` and MUST NOT throw
- First argument is positional, all subsequent arguments MUST be named: `func(first, name: second)`
- Collection `.get()` ALWAYS requires `try...otherwise`
- Throwing functions MUST be called with `try`
- Match arms MUST use bare case names (`red` not `Color.red`)
- Enum match MUST be exhaustive; `default` on enum MUST use `throws` or `panic`
- Enum values with associated values CANNOT be compared with `==` — use `match`
- Indentation uses tabs (not spaces)
- Comments use `//`
- Add blank lines to improve code readability, especially around control flow statements and between logical sections of code.

## Stdlib type aliases to use

`ExitCode`, `Count`, `Index`, `Offset`, `HashValue`, `Codepoint`, `MathValue`, `Byte`, `ByteArray`

## Quick syntax reference

```maxon
// Function
function name(param1 Type1, param2 Type2) returns ReturnType
	// body
end 'name'

// Variables (type inferred, NEVER annotated)
let x = 42
var y = 10

// Struct
type Point
	export var x MathValue
	export var y MathValue
end 'Point'
var p = Point{x: 1.0, y: 2.0}

// Enum
enum Color
	red
	green
	blue
end 'Color'

// Enum with associated values
enum Result
	success(value Offset)
	failure(message String)
end 'Result'

// Error enum
enum MyError implements Error
	notFound
	invalid
end 'MyError'

// Typealias
typealias Port = int(0 to 65535)
typealias ScoreArray = Array with Score

// If/else
if condition 'label'
	// ...
end 'label' else 'other'
	// ...
end 'other'

// While
while condition 'loop'
	// ...
end 'loop'

// For
for i in 0 upto n 'loop'
	// ...
end 'loop'

// Match statement
match value 'label'
	1 then doOne()
	2 or 3 then doOther()
	default then doDefault()
end 'label'

// Match expression
let result = match value 'label'
	1 gives "one"
	default gives "other"
end 'label'

// Error handling
let val = try mayFail() otherwise defaultValue
try mayFail() otherwise 'err'
	// handle error
end 'err'

// Closure
let fn = (x Offset) gives x * 2
```
