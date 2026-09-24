---
feature: same-name-methods
status: experimental
keywords: [static, instance, method, overload, same name]
category: type-system
---

# Same-Name Static and Instance Methods

## Documentation

### Overview

A type can define both a static method and an instance method with the same name. The compiler disambiguates based on call syntax:

- `Type.method()` calls the **static** method
- `instance.method()` calls the **instance** method

```text
type Counter
  var count as int

  static function reset() returns Counter
    return Counter{count: 0}
  end 'reset'

  function reset()
    count = 0
  end 'reset'
end 'Counter'

var c = Counter.reset()   // static: creates a new Counter
c.reset()                 // instance: resets the existing Counter
```

This is useful when the same verb makes sense in both contexts, e.g., a static factory `create` alongside an instance `create` that reinitializes.

## Tests

<!-- test: same-name-methods.basic -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(v Integer) returns Box
		return Box{value: v}
	end 'create'

	static function getValue() returns Integer
		return 9
	end 'getValue'

	function getValue() returns Integer
		return value
	end 'getValue'
end 'Box'

function main() returns ExitCode
	let b = Box.create(42)
	let instanceResult = b.getValue()
	let staticResult = Box.getValue()
	return instanceResult + staticResult
end 'main'
```
```exitcode
51
```

<!-- test: same-name-methods.instance-in-an-extension -->
The instance method may be declared in an extension of the type, beside the type body's static of the
same name.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(v Integer) returns Box
		return Box{value: v}
	end 'create'

	static function getValue() returns Integer
		return 9
	end 'getValue'
end 'Box'

extension Box
	function getValue() returns Integer
		return self.value
	end 'getValue'
end 'Box'

function main() returns ExitCode
	let b = Box.create(42)
	return b.getValue() + Box.getValue()
end 'main'
```
```exitcode
51
```

<!-- test: same-name-methods.instance-in-another-file-does-not-inherit-the-statics-facts -->
The static and the instance method are two declarations, and a fact about one — here that the static never
returns — is not a fact about the other, whichever file each is in.
```maxon
// --- file: box.maxon
module type Box
	var tag = 0

	module static function create() returns Box
		return Box{}
	end 'create'

	static function ping()
		panic("the static never returns")
	end 'ping'
end 'Box'

// --- file: ext.maxon
extension Box
	module function ping()
		print("instance ping\n")
	end 'ping'
end 'Box'

// --- file: main.maxon
function main() returns ExitCode
	let b = Box.create()
	b.ping()
	print("after\n")
	return 0
end 'main'
```
```stdout
instance ping
after
```
```exitcode
0
```

<!-- test: same-name-methods.an-extension-static-beside-the-types-own-static-is-withheld -->
The type's own static withholds an extension's static of the same name, whatever another extension
publishes under that name for the instance.
```maxon
typealias Num = int(i64.min to i64.max)

type T
	export var v as Num

	static function make(v Num) returns T
		return Self{v: v}
	end 'make'

	static function m() returns Num
		return 7
	end 'm'
end 'T'

extension T
	function m() returns Num
		return self.v
	end 'm'
end 'T'

extension T
	static function m() returns Num
		return 99
	end 'm'
end 'T'

function main() returns ExitCode
	return (T.m() + T.make(1).m()) as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: same-name-methods.with-params -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Calc
	export var base as Integer

	static function create(base Integer) returns Calc
		return Calc{base: base}
	end 'create'

	static function add(a Integer, b Integer) returns Integer
		return a + b
	end 'add'

	function add(x Integer) returns Integer
		return base + x
	end 'add'
end 'Calc'

function main() returns ExitCode
	let c = Calc.create(30)
	let instanceResult = c.add(10)
	let staticResult = Calc.add(1, b: 1)
	return instanceResult + staticResult
end 'main'
```
```exitcode
42
```

<!-- test: same-name-methods.returns-self -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var count as Integer

	static function create(count Integer) returns Counter
		return Counter{count: count}
	end 'create'

	static function reset() returns Counter
		return Counter{count: 0}
	end 'reset'

	function reset()
		count = 0
	end 'reset'
end 'Counter'

function main() returns ExitCode
	let c = Counter.reset()
	var c2 = Counter.create(42)
	c2.reset()
	return c.count + c2.count
end 'main'
```
```exitcode
0
```

<!-- test: same-name-methods.export -->
```maxon
// --- file: lib.maxon
export typealias Integer = int(i64.min to i64.max)

export type Pair
	export var a as Integer
	export var b as Integer

	export static function create(a Integer, b Integer) returns Pair
		return Pair{a: a, b: b}
	end 'create'

	export static function sum(x Integer, y Integer) returns Integer
		return x + y
	end 'sum'

	export function sum() returns Integer
		return a + b
	end 'sum'
end 'Pair'

// --- file: main.maxon
function main() returns ExitCode
	let p = Pair.create(10, b: 20)
	let instanceResult = p.sum()
	let staticResult = Pair.sum(5, y: 7)
	return instanceResult + staticResult
end 'main'
```
```exitcode
42
```

<!-- test: same-name-methods.same-params -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Converter
	export var factor as Integer

	static function create(factor Integer) returns Converter
		return Converter{factor: factor}
	end 'create'

	static function convert(x Integer) returns Integer
		return x * 2
	end 'convert'

	function convert(x Integer) returns Integer
		return x * factor
	end 'convert'
end 'Converter'

function main() returns ExitCode
	let c = Converter.create(7)
	let instanceResult = c.convert(5)
	let staticResult = Converter.convert(3)
	return instanceResult + staticResult
end 'main'
```
```exitcode
41
```

<!-- test: error.same-name-methods.throwing-static-names-its-callee -->
A static that shares its name with an instance method is registered under a key of its own, but a
diagnostic names it the way the source does: `Box.getValue`.
```maxon
typealias Integer = int(i64.min to i64.max)

enum BoxError implements Error
	empty
end 'BoxError'

type Box
	export var value as Integer

	static function create(v Integer) returns Box
		return Box{value: v}
	end 'create'

	static function getValue() returns Integer throws BoxError
		throw BoxError.empty
	end 'getValue'

	function getValue() returns Integer
		return value
	end 'getValue'
end 'Box'

function read() returns Integer
	return Box.getValue()
end 'read'

function main() returns ExitCode
	let b = Box.create(4)
	return b.getValue() + read()
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/same-name-methods/error.same-name-methods.throwing-static-names-its-callee.test:25:13: throwing function requires try: 'Box.getValue'
```
