---
feature: default-values
status: experimental
keywords: [default, default-values, parameters, string-default, array-default, enum-default, bool-default]
category: core
---

# Default Parameter Values

## Documentation

Function parameters can have default values that are used when the argument is omitted at the call site. Default values can be any literal: integers, floats, booleans, strings, arrays, and enum cases.

### Integer Defaults

```text
typealias Count = int(0 to 1000)

function repeat(value Count, times Count = 1) returns Count
  return value * times
end 'repeat'

var result = repeat(7)  // times defaults to 1, result is 7
```

### String Defaults

```text
function greet(name String = "World") returns ExitCode
  print("Hello, {name}!")
  return 0
end 'greet'

greet()                    // prints "Hello, World!"
greet("Maxon")       // prints "Hello, Maxon!"
```

### Array Defaults

```text
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function sum(items IntArray = [1, 2, 3]) returns Integer
  var total = 0
  for item in items 'loop'
    total = total + item
  end 'loop'
  return total
end 'sum'

var result = sum()  // uses default [1, 2, 3], result is 6
```

### Enum Defaults

```text
enum Color
  red
  green
  blue
end 'Color'

function paint(color Color = Color.blue) returns ExitCode
  return color.rawValue
end 'paint'

var result = paint()  // uses default Color.blue, result is 2
```

## Tests

<!-- test: default-values.string-default-omitted -->
```maxon
function greet(name String = "World") returns ExitCode
	print("Hello, {name}!")
	return 0
end 'greet'

function main() returns ExitCode
	return greet()
end 'main'
```
```stdout
Hello, World!
```
```exitcode
0
```

<!-- test: default-values.string-default-provided -->
```maxon
function greet(name String = "World") returns ExitCode
	print("Hello, {name}!")
	return 0
end 'greet'

function main() returns ExitCode
	return greet("Maxon")
end 'main'
```
```stdout
Hello, Maxon!
```
```exitcode
0
```

<!-- test: default-values.string-default-empty -->
```maxon
function show(label String = "") returns ExitCode
	print("[{label}]")
	return 0
end 'show'

function main() returns ExitCode
	return show()
end 'main'
```
```stdout
[]
```
```exitcode
0
```

<!-- test: default-values.array-default-omitted -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function sum(items IntArray = [10, 20, 12]) returns Integer
	var total = 0
	for item in items 'loop'
		total = total + item
	end 'loop'
	return total
end 'sum'

function main() returns ExitCode
	return sum()
end 'main'
```
```exitcode
42
```

<!-- test: default-values.array-default-provided -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function sum(items IntArray = [10, 20, 12]) returns Integer
	var total = 0
	for item in items 'loop'
		total = total + item
	end 'loop'
	return total
end 'sum'

function main() returns ExitCode
	return sum([1, 2, 3])
end 'main'
```
```exitcode
6
```

<!-- test: default-values.bool-default-omitted -->
```maxon
typealias Integer = int(i64.min to i64.max)

function choose(flag bool = true, a Integer = 40, b Integer = 10) returns Integer
	if flag 'check'
		return a + 2
	end 'check' else 'other'
		return b
	end 'other'
end 'choose'

function main() returns ExitCode
	return choose()
end 'main'
```
```exitcode
42
```

<!-- test: default-values.enum-default-omitted -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function paint(color Color = Color.blue) returns ExitCode
	return color.rawValue
end 'paint'

function main() returns ExitCode
	return paint()
end 'main'
```
```exitcode
2
```

<!-- test: default-values.mixed-defaults -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(label String = "result", value Integer = 42) returns ExitCode
	print("{label}: {value}")
	return 0
end 'process'

function main() returns ExitCode
	return process()
end 'main'
```
```stdout
result: 42
```
```exitcode
0
```

<!-- test: default-values.string-default-with-escape -->
```maxon
function show(msg String = "line1\nline2") returns ExitCode
	print(msg)
	return 0
end 'show'

function main() returns ExitCode
	return show()
end 'main'
```
```stdout
line1
line2
```
```exitcode
0
```

<!-- test: default-values.float-default-omitted -->
```maxon
typealias Number = float(f64.min to f64.max)

function scale(factor Number = 2.5) returns ExitCode
	print("{factor}")
	return 0
end 'scale'

function main() returns ExitCode
	return scale()
end 'main'
```
```stdout
2.5
```
```exitcode
0
```

<!-- test: default-values.struct-default-omitted -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function sum(p Point = Point.create(10, y: 32)) returns Integer
	return p.x + p.y
end 'sum'

function main() returns ExitCode
	return sum()
end 'main'
```
```exitcode
42
```

<!-- test: default-values.struct-default-provided -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function sum(p Point = Point.create(10, y: 32)) returns Integer
	return p.x + p.y
end 'sum'

function main() returns ExitCode
	return sum(Point.create(1, y: 2))
end 'main'
```
```exitcode
3
```

<!-- test: default-values.char-default-omitted -->
```maxon
function initial(c Character = 'A') returns ExitCode
	print("{c}")
	return 0
end 'initial'

function main() returns ExitCode
	return initial()
end 'main'
```
```stdout
A
```
```exitcode
0
```

<!-- test: default-values.bytestring-default-omitted -->
```maxon
typealias Integer = int(i64.min to i64.max)

function checkLen(data ByteArray = b"hello") returns Integer
	return data.count()
end 'checkLen'

function main() returns ExitCode
	return checkLen()
end 'main'
```
```exitcode
5
```
