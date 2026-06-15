---
feature: enum-ordinal
status: experimental
keywords: [enum, ordinal, position]
category: type-system
---

## Documentation

# Enum Ordinal

All enum have an `.ordinal` property that returns the zero-based position of the case in its declaration order, always as an `int`.

This is different from `.rawValue` for backed enum — `.ordinal` always returns the declaration position:

```text
enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

var s = HttpStatus.notFound
var pos = s.ordinal    // 1 (second case declared)
var code = s.rawValue  // 404 (the backing value)
```

For simple enum (no explicit values), `.ordinal` and `.rawValue` are identical:

```text
enum Color
  red       // ordinal 0, rawValue 0
  green     // ordinal 1, rawValue 1
  blue      // ordinal 2, rawValue 2
end 'Color'
```

## Tests

### Simple Enum

<!-- test: enum-ordinal.simple -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	let c = Color.green
	if c.ordinal == 1 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### All Cases

<!-- test: enum-ordinal.all-cases -->
```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'

function main() returns ExitCode
	let n = Direction.north
	let s = Direction.south
	let e = Direction.east
	let w = Direction.west
	if n.ordinal == 0 and s.ordinal == 1 and e.ordinal == 2 and w.ordinal == 3 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Int-Backed Enum

<!-- test: enum-ordinal.int-backed -->
```maxon
enum HttpStatus
	ok = 200
	notFound = 404
	serverError = 500
end 'HttpStatus'

function main() returns ExitCode
	let s = HttpStatus.serverError
	// ordinal is 2 (third case), not 500 (the raw value)
	if s.ordinal == 2 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Float-Backed Enum

<!-- test: enum-ordinal.float-backed -->
```maxon
enum Threshold
	low = 0.1
	medium = 0.5
	high = 0.9
end 'Threshold'

function main() returns ExitCode
	let t = Threshold.high
	if t.ordinal == 2 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### String-Backed Enum

<!-- test: enum-ordinal.string-backed -->
```maxon
enum ContentType
	json = "application/json"
	html = "text/html"
	plain = "text/plain"
end 'ContentType'

function main() returns ExitCode
	let ct = ContentType.html
	if ct.ordinal == 1 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Char-Backed Enum

<!-- test: enum-ordinal.char-backed -->
```maxon
enum Grade
	a = 'A'
	b = 'B'
	c = 'C'
end 'Grade'

function main() returns ExitCode
	let g = Grade.c
	if g.ordinal == 2 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Ordinal in Arithmetic

<!-- test: enum-ordinal.arithmetic -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	let c = Color.blue
	let result = c.ordinal + 10
	if result == 12 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Bare Enum Case in Arithmetic Yields int

An enum case used directly as an arithmetic operand (no `.ordinal`) is its
ordinal — an integer — so the `*` / `+` result is `int`, NOT the enum type. The
result must flow into an int-typed parameter (here a table index): a row-major
table lookup `state * COUNT + col` is the canonical DFA-transition-table idiom.
If the binop result kept the operand's enum type, the `lookup(index Idx)` call
would reject it.

<!-- test: enum-ordinal.bare-case-arithmetic-index -->
```maxon
typealias Idx = int(0 to u64.max)
typealias IdxArray = Array with Idx

enum Col
	a
	b
	c
	COUNT
end 'Col'

enum Row
	x
	y
	COUNT
end 'Row'

function lookup(table IdxArray, index Idx) returns Idx
	return try table.get(index) otherwise 0
end 'lookup'

function main() returns ExitCode
	var table = IdxArray.create()
	var i = 0
	while i < 100 'fill'
		table.push(i)
		i = i + 1
	end 'fill'
	let col = Col.b
	let idx = Row.y * Col.COUNT + col
	return lookup(table, index: idx)
end 'main'
```
```exitcode
4
```

### Ordinal from Function

<!-- test: enum-ordinal.from-function -->
```maxon
enum Priority
	low
	medium
	high
end 'Priority'

typealias OrdinalValue = int(0 to 100)

function getOrdinal(p Priority) returns OrdinalValue
	return p.ordinal
end 'getOrdinal'

function main() returns ExitCode
	let p = Priority.high
	if getOrdinal(p) == 2 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Ordinal on Simple Enum

`.ordinal` is available on all enums.

<!-- test: enum-ordinal.error-enum-ordinal -->
```maxon
enum Shape
	circle
	square
end 'Shape'

function main() returns ExitCode
	let s = Shape.square
	return s.ordinal
end 'main'
```
```exitcode
1
```
