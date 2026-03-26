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
  static var _cachedWhitespace = CharacterSet._buildWhitespace()

  export static function whitespace() returns CharacterSet
    return CharacterSet._cachedWhitespace
  end 'whitespace'
end 'CharacterSet'
```

## Tests

<!-- test: lazy-static.basic-function-call -->
### Basic lazy static with function call

```maxon
type Config
	static var _value = Config._makeValue()
	export var n Count

	static function _makeValue() returns Config
		return Config{n: 42}
	end '_makeValue'

	export static function getValue() returns Config
		return Config._value
	end 'getValue'
end 'Config'

function main() returns ExitCode
	var c = Config.getValue()
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
	static var _callCount = 0
	static var _instance = Counter._create()
	export var id Count

	static function _create() returns Counter
		Counter._callCount = Counter._callCount + 1
		return Counter{id: Counter._callCount}
	end '_create'

	export static function instance() returns Counter
		return Counter._instance
	end 'instance'

	export static function callCount() returns Count
		return Counter._callCount
	end 'callCount'
end 'Counter'

function main() returns ExitCode
	var a = Counter.instance()
	var b = Counter.instance()
	var c = Counter.instance()
	print("{a.id} {b.id} {c.id} {Counter.callCount()}")
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
end 'Point'

type Defaults
	static var origin = Point{x: 0, y: 0}

	export static function getOrigin() returns Point
		return Defaults.origin
	end 'getOrigin'
end 'Defaults'

function main() returns ExitCode
	var p = Defaults.getOrigin()
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
	static var _current = State._default()
	export var value Count

	static function _default() returns State
		return State{value: 0}
	end '_default'

	export static function get() returns State
		return State._current
	end 'get'

	export static function set(s State)
		State._current = s
	end 'set'
end 'State'

function main() returns ExitCode
	var a = State.get()
	print("{a.value} ")
	State.set(State{value: 99})
	var b = State.get()
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
	static var _a = Cache._buildA()
	static var _b = Cache._buildB()
	export var n Count

	static function _buildA() returns Cache
		return Cache{n: 10}
	end '_buildA'

	static function _buildB() returns Cache
		return Cache{n: 20}
	end '_buildB'

	export static function sum() returns Count
		return Cache._a.n + Cache._b.n
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
	static var _values = [10, 20, 30]

	export static function get(index Integer) returns Integer
		return try Lookup._values.get(index) otherwise -1
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
	static var _ws = CharacterSet.whitespacesAndNewlines()

	export static function isWhitespace(c Character) returns bool
		return WSCache._ws.contains(c)
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
	static let _set = CharSet from ['a', 'e', 'i', 'o', 'u']

	export static function contains(c Character) returns bool
		return Vowels._set.contains(c)
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
end 'Pair'

type Registry
	static let _pair = _buildPair()

	export static function getX() returns Count
		return Registry._pair.x
	end 'getX'

	export static function getY() returns Count
		return Registry._pair.y
	end 'getY'
end 'Registry'

function _buildPair() returns Pair
	return Pair{x: 11, y: 22}
end '_buildPair'

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
