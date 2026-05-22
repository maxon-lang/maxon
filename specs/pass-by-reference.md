---
feature: pass-by-reference
status: experimental
keywords: [reference, pass-by-reference, mutation, ref, closure, capture]
category: core
---

# Pass by Reference

## Documentation

In Maxon, all parameters are passed by reference. When you pass a variable to a function, the function receives a reference to the original value, not a copy.

### Reading Referenced Values

A function can read a parameter that was passed by reference:

```text
function double(x Integer) returns Integer
  return x * 2
end 'double'

var n = 21
var result = double(x: n)  // result is 42
```

### Mutating Referenced Values

A function can assign to its parameters, and the caller will see the change:

```text
function increment(x Integer)
  x = x + 1
end 'increment'

var n = 10
increment(x: n)
// n is now 11
```

### Immutability Enforcement

If a `let` variable is passed to a function that assigns to that parameter, the compiler reports an error. This ensures immutable bindings cannot be modified indirectly.

### Temporaries from Literals and Expressions

When a literal or expression result is passed to a function, a temporary is created. The function can read it normally:

```text
var result = double(x: 42)       // literal creates a temporary
var result2 = double(x: a + b)   // expression result creates a temporary
```

### Closure Capture

Closures capture variables by reference. Changes to the original variable are visible inside the closure, and assignments inside the closure are visible to the outer scope.

## Tests

<!-- test: pass-by-reference.basic-primitive-ref -->
```maxon

typealias Integer = int(i64.min to i64.max)

function readVal(x Integer) returns Integer
	return x
end 'readVal'

function main() returns ExitCode
	let n = 42
	return readVal(x: n)
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.mutate-primitive-ref -->
```maxon

typealias Integer = int(i64.min to i64.max)

function setTo99(x Integer)
	x = 99
end 'setTo99'

function main() returns ExitCode
	var n = 0
	setTo99(x: n)
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: pass-by-reference.immutable-primitive-ref -->
```maxon

typealias Integer = int(i64.min to i64.max)

function readVal(x Integer) returns Integer
	return x
end 'readVal'

function main() returns ExitCode
	let n = 37
	return readVal(x: n)
end 'main'
```
```exitcode
37
```

<!-- test: pass-by-reference.literal-creates-temporary -->
```maxon

typealias Integer = int(i64.min to i64.max)

function readVal(x Integer) returns Integer
	return x
end 'readVal'

function main() returns ExitCode
	return readVal(x: 42)
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.expression-creates-temporary -->
```maxon

typealias Integer = int(i64.min to i64.max)

function readVal(x Integer) returns Integer
	return x
end 'readVal'

function main() returns ExitCode
	let a = 20
	let b = 22
	return readVal(x: a + b)
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.struct-ref-field-mutation -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function setX(p Point)
	p.x = 99
end 'setX'

function main() returns ExitCode
	let p = Point.create(x: 1, y: 2)
	setX(p: p)
	print("{p.x}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: pass-by-reference.struct-ref-reassignment -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function replacePoint(p Point)
	p = Point.create(x: 99, y: 99)
end 'replacePoint'

function main() returns ExitCode
	var p = Point.create(x: 1, y: 2)
	replacePoint(p: p)
	print("{p.x}\n")
	print("{p.y}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
99

```

<!-- test: pass-by-reference.let-to-mutating-param-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

function setTo99(x Integer)
	x = 99
end 'setTo99'

function main() returns ExitCode
	let n = 5
	setTo99(x: n)
	return 0
end 'main'
```
```maxoncstderr
error E3019: specs/fragments/pass-by-reference/pass-by-reference.let-to-mutating-param-error.test:11:2: cannot pass 'n' to function that mutates parameter 'x' (in main)
```

<!-- test: pass-by-reference.nested-calls -->
```maxon

typealias Integer = int(i64.min to i64.max)

function inner(x Integer)
	x = 77
end 'inner'

function outer(x Integer)
	inner(x: x)
end 'outer'

function main() returns ExitCode
	var n = 0
	outer(x: n)
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
77
```

<!-- test: pass-by-reference.multiple-params-mixed -->
```maxon

typealias Integer = int(i64.min to i64.max)

function process(a Integer, b Integer, c Integer)
	b = a + c + 90
end 'process'

function main() returns ExitCode
	let x = 1
	var y = 2
	let z = 3
	process(a: x, b: y, c: z)
	print("{x}\n")
	print("{y}\n")
	print("{z}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
94
3

```

<!-- test: pass-by-reference.enum-ref -->
```maxon

enum Color
	red
	blue
	green
end 'Color'

function switchColor(c Color)
	c = Color.green
end 'switchColor'

function main() returns ExitCode
	var c = Color.red
	switchColor(c: c)
	return c.rawValue
end 'main'
```
```exitcode
2
```

<!-- test: pass-by-reference.default-param-value -->
```maxon

typealias Integer = int(i64.min to i64.max)

function addOffset(x Integer, offset Integer = 10) returns Integer
	return x + offset
end 'addOffset'

function main() returns ExitCode
	let result = addOffset(x: 32)
	return result
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.closure-capture-by-ref -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function() returns Integer
function apply(f FnTypeAlias1) returns Integer
	return f()
end 'apply'

function main() returns ExitCode
	let x = 42
	let result = apply(f: function() gives x)
	return result
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.closure-capture-after-mutation -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function() returns Integer
function apply(f FnTypeAlias1) returns Integer
	return f()
end 'apply'

function main() returns ExitCode
	var x = 10
	let f = function() gives x
	x = 99
	let result = apply(f: f)
	return result
end 'main'
```
```exitcode
99
```
