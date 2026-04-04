---
feature: lazy-static
status: experimental
keywords: [static, var, let, lazy, initializer, cache]
category: language
---

# Lazy Static Initializers

## Documentation

Static fields can be initialized with complex expressions including function calls, struct literals, and array literals. These initializers are evaluated lazily on first access.

### Syntax

```text
type MyType
  static var cached = SomeType.create()
  static let DEFAULTS = [1, 2, 3]
end 'MyType'
```

### Semantics

- The initializer expression is evaluated the first time the static field is accessed
- After initialization, subsequent accesses return the cached value
- `static var` fields can be reassigned after initialization
- `static let` fields are immutable after initialization
- Constant initializers (integer, float, bool literals) continue to be evaluated at compile time

### Use Cases

Caching expensive computations:

```text
type CharacterSet
  static var cachedWhitespace = CharacterSet.buildWhitespace()

  export static function whitespace() returns CharacterSet
    return CharacterSet.cachedWhitespace
  end 'whitespace'
end 'CharacterSet'
```

## Tests

<!-- test: lazy-static.basic-function-call -->
### Basic lazy static with function call

```maxon
type Config
	static var value = Config.makeValue()
	export var n Count

	static function makeValue() returns Config
		return Config{n: 42}
	end 'makeValue'

	export static function getValue() returns Config
		return Config.value
	end 'getValue'
end 'Config'

function main() returns ExitCode
	let c = Config.getValue()
	return c.n
end 'main'
```
```exitcode
42
```

<!-- test: lazy-static.initialized-once -->
### Lazy static initialized only once

```maxon
type Counter
	static var initCount = 0
	static var cached = Counter.createInstance()
	export var id Count

	static function createInstance() returns Counter
		Counter.initCount = Counter.initCount + 1
		return Counter{id: Counter.initCount}
	end 'createInstance'

	export static function getInstance() returns Counter
		return Counter.cached
	end 'getInstance'

	export static function getInitCount() returns Count
		return Counter.initCount
	end 'getInitCount'
end 'Counter'

function main() returns ExitCode
	let a = Counter.getInstance()
	let b = Counter.getInstance()
	let c = Counter.getInstance()
	print("{a.id} {b.id} {c.id} {Counter.getInitCount()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1 1 1
```

<!-- test: lazy-static.struct-literal -->
### Lazy static with struct literal

```maxon
type Point
	export var x Count
	export var y Count

	static function create(x Count, y Count) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Defaults
	static var origin = Point.create(x: 0, y: 0)

	export static function getOrigin() returns Point
		return Defaults.origin
	end 'getOrigin'
end 'Defaults'

function main() returns ExitCode
	let p = Defaults.getOrigin()
	print("{p.x} {p.y}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 0
```

<!-- test: lazy-static.mutable-reassign -->
### Lazy static var can be reassigned

```maxon
type State
	static var current = State.makeDefault()
	export var value Count

	static function makeDefault() returns State
		return State{value: 0}
	end 'makeDefault'

	export static function get() returns State
		return State.current
	end 'get'

	export static function set(s State)
		State.current = s
	end 'set'

	static function create(value Count) returns Self
		return Self{value: value}
	end 'create'
end 'State'

function main() returns ExitCode
	let a = State.get()
	print("{a.value} ")
	State.set(State.create(value: 99))
	let b = State.get()
	print("{b.value}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 99
```

<!-- test: lazy-static.multiple-fields -->
### Multiple lazy statics in same type

```maxon
type Cache
	static var a = Cache.buildA()
	static var b = Cache.buildB()
	export var n Count

	static function buildA() returns Cache
		return Cache{n: 10}
	end 'buildA'

	static function buildB() returns Cache
		return Cache{n: 20}
	end 'buildB'

	export static function sum() returns Count
		return Cache.a.n + Cache.b.n
	end 'sum'
end 'Cache'

function main() returns ExitCode
	return Cache.sum()
end 'main'
```
```exitcode
30
```

<!-- test: lazy-static.array-literal -->
### Lazy static with array literal

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Lookup
	static var values = [10, 20, 30]

	export static function get(index Integer) returns Integer
		return try Lookup.values.get(index) otherwise -1
	end 'get'
end 'Lookup'

function main() returns ExitCode
	print("{Lookup.get(0)} {Lookup.get(1)} {Lookup.get(2)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10 20 30
```

<!-- test: lazy-static.charset-cache -->
### CharacterSet caching pattern

```maxon
type WSCache
	static var ws = CharacterSet.whitespacesAndNewlines()

	export static function isWhitespace(c Character) returns bool
		return WSCache.ws.contains(c)
	end 'isWhitespace'
end 'WSCache'

function main() returns ExitCode
	if WSCache.isWhitespace(' ') 'c1'
		print("space ")
	end 'c1'
	if WSCache.isWhitespace('a') 'c2'
		print("FAIL")
	end 'c2'
	if WSCache.isWhitespace('\t') 'c3'
		print("tab")
	end 'c3'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
space tab
```

<!-- test: lazy-static.collection-initializer -->
### Lazy static with collection initializer

```maxon
typealias CharSet = Set with Character

type Vowels
	static let vowelSet = CharSet from ['a', 'e', 'i', 'o', 'u']

	export static function contains(c Character) returns bool
		return Vowels.vowelSet.contains(c)
	end 'contains'
end 'Vowels'

function main() returns ExitCode
	print("{Vowels.contains('a')} {Vowels.contains('b')} {Vowels.contains('u')}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true false true
```

<!-- test: lazy-static.cross-type-return -->
### Lazy static with function returning a different type

```maxon
type Pair
	export var x Count
	export var y Count

	static function create(x Count, y Count) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Pair'

type Registry
	static let cachedPair = buildPair()

	export static function getX() returns Count
		return Registry.cachedPair.x
	end 'getX'

	export static function getY() returns Count
		return Registry.cachedPair.y
	end 'getY'
end 'Registry'

function buildPair() returns Pair
	return Pair.create(x: 11, y: 22)
end 'buildPair'

function main() returns ExitCode
	print("{Registry.getX()} {Registry.getY()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11 22
```
