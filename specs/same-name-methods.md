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
		return 99
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
141
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
typealias Integer = int(i64.min to i64.max)

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
