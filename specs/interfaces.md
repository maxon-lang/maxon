---
feature: interfaces
status: stable
keywords: [interface, implements, with, uses, self, method, conformance]
category: type-system
---

# Interfaces

## Documentation

Interfaces define a set of methods that types can implement. They provide a form of interface-based polymorphism.

### Interface Declaration

Interfaces are declared with the `interface` keyword and contain method signatures:

```maxon
interface Hashable
	function hash() returns HashValue
end 'Hashable'
```

Method signatures in interfaces have an implicit `self` parameter of type `Self` (the conforming type). You don't declare `self` - it's automatic.

### Type Conformance

Types declare conformance to interfaces using the `implements` keyword:

```maxon
typealias Score = int(i64.min to i64.max)

type Point implements Hashable
	var x Score
	var y Score
end 'Point'
```

A type can conform to multiple interfaces:

```maxon
typealias Score = int(i64.min to i64.max)

type Point implements Hashable, Equatable
	var x Score
	var y Score
end 'Point'
```

### Method Implementation

Methods implementing interface requirements are defined **inside the type body** using `function methodName(params)` syntax. The interface prefix explicitly declares which interface the method implements. The `self` parameter is implicit:

```maxon
typealias Score = int(i64.min to i64.max)

type Point implements Hashable
	var x Score
	var y Score

	function hash() returns HashValue
		return x + y * 31
	end 'hash'
end 'Point'
```

Inside method bodies, you can access type fields directly without `self.` prefix:
- `x` resolves to `self.x`
- `y` resolves to `self.y`

You can still use `self.field` explicitly if needed, especially when a parameter shadows a field name.

Non-interface methods (methods that don't implement any interface) use simple `function methodName(params)` syntax without a prefix:

```maxon
typealias Score = int(i64.min to i64.max)

type Point
	var x Score
	var y Score

	export function getX() returns Score
		return x
	end 'getX'
end 'Point'
```

### Calling Methods

Methods are called using the method call syntax - `instance.method()`:

```maxon
var p = Point.create(x: 10, y: 20)
var h = p.hash()
```

The method receives the instance as an implicit first parameter. Fields are accessed directly by name within the method body.

### Self Type

The `Self` type in interface signatures represents the conforming type:

```maxon
typealias Score = int(i64.min to i64.max)

interface Cloneable
	function clone() returns Self
end 'Cloneable'

type Point implements Cloneable
	var x Score
	var y Score

	function clone() returns Point
		return Point{x: x, y: y}
	end 'clone'
end 'Point'
```

### Associated Types

Interfaces can declare associated types with `uses`. Structs bind concrete types with `with`:

```maxon
typealias Score = int(i64.min to i64.max)

interface Container uses Element
	function get(index Score) returns Element
end 'Container'

typealias Int = int(i64.min to i64.max)
typealias InternalIntArray = Array with Int

type IntArray implements Container with Score
	var data InternalIntArray

	function get(index Score) returns Score
		return try data.get(index) otherwise 0
	end 'get'
end 'IntArray'
```

See the [Associated Types](associated-types.md) spec for full documentation.

### Partial Implementation Error

A type must implement **all** methods from interfaces it conforms to. Partial implementation is an error:

```maxon
typealias Score = int(i64.min to i64.max)

interface TwoMethods
	function first() returns Score
	function second() returns Score
end 'TwoMethods'

type Incomplete implements TwoMethods
	function first() returns Score
		return 1
	end 'first'
	// Missing: TwoMethods.second()
end 'Incomplete'
```

This produces a compiler error listing all missing methods.

### Standard Library Interfaces

The standard library (`stdlib/interfaces.maxon`) defines commonly used interfaces that are automatically imported when referenced:

- **Hashable** - Types that can be hashed to an integer: `returns int`
- **Equatable** - Types that can be compared for equality: `returns bool`
- **Comparable** - Types that can be ordered: `returns Ordering`
- **Cloneable** - Types that can be copied: `returns Self`
- **Iterable** - Types that can be iterated over (used by for-in loops):
  - Uses `Element` associated type
  - `returns Element throws IterationError`

The `Iterator` type in `stdlib/iter/iterator.maxon` conforms to `Iterable` and is used by `for` loops with `range()`.

### Example

```maxon
typealias Score = int(i64.min to i64.max)

type Point implements Hashable
	var x Score
	var y Score

	function hash() returns HashValue
		return x + y * 31
	end 'hash'

	static function create(x Score, y Score) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(x: 10, y: 20)
	var h = p.hash()
	print("{h}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
630
```


## Tests

<!-- test: basic-interface -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point implements Hashable
	var x Integer
	var y Integer

	function hash() returns HashValue
		return x + y * 31
	end 'hash'

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(x: 10, y: 20)
	print("{p.hash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
630
```


<!-- test: multiple-methods -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Describable
	function describe() returns Integer
	function value() returns Integer
end 'Describable'

type Counter implements Describable
	var count Integer

	function describe() returns Integer
		return 100 + count
	end 'describe'

	function value() returns Integer
		return count
	end 'value'

	static function create(count Integer) returns Self
		return Self{count: count}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create(count: 42)
	return c.describe() + c.value()
end 'main'
```
```exitcode
184
```


<!-- test: method-with-params -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Calculator
	function add(n Integer) returns Integer
end 'Calculator'

type Accumulator implements Calculator
	var total Integer

	function add(n Integer) returns Integer
		return total + n
	end 'add'

	static function create(total Integer) returns Self
		return Self{total: total}
	end 'create'
end 'Accumulator'

function main() returns ExitCode
	var acc = Accumulator.create(total: 10)
	return acc.add(5)
end 'main'
```
```exitcode
15
```


<!-- test: multiple-interfaces -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point implements Hashable, Equatable
	var x Integer
	var y Integer

	function hash() returns HashValue
		return x + y
	end 'hash'

	function equals(other Point) returns bool
		if x == other.x and y == other.y 'c1'
			return true
		end 'c1'
		return false
	end 'equals'

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p1 = Point.create(x: 3, y: 4)
	var p2 = Point.create(x: 3, y: 4)
	print("{p1.hash()} {p1.equals(p2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 true
```

<!-- test: self-return-type -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Movable
	function move(dx Integer, dy Integer) returns Self
end 'Movable'

type Point implements Movable
	export var x Integer
	export var y Integer

	function move(dx Integer, dy Integer) returns Point
		return Point{x: x + dx, y: y + dy}
	end 'move'

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(x: 10, y: 20)
	var p2 = p.move(5, dy: 10)
	return p2.x + p2.y
end 'main'
```
```exitcode
45
```


<!-- test: method-call-syntax -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Incrementable
	function inc() returns Integer
end 'Incrementable'

type Value implements Incrementable
	var n Integer

	function inc() returns Integer
		return n + 1
	end 'inc'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Value'

function main() returns ExitCode
	var v = Value.create(n: 41)
	return v.inc()
end 'main'
```
```exitcode
42
```


<!-- test: partial-implementation-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface ThreeMethods
	function one() returns Integer
	function two() returns Integer
	function three() returns Integer
end 'ThreeMethods'

type Incomplete implements ThreeMethods
	var value Integer

	function one() returns Integer
		return 1
	end 'one'
end 'Incomplete'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/interfaces/partial-implementation-error.test:11:6: Partial interface implementation: type 'Incomplete' is missing 2 method(s):
  - two() returns Integer
  - three() returns Integer
```


<!-- test: non-interface-method -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Calculator
	var value Integer

	function double() returns Integer
		return value * 2
	end 'double'

	function triple() returns Integer
		return value * 3
	end 'triple'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Calculator'

function main() returns ExitCode
	var c = Calculator.create(value: 7)
	return c.double() + c.triple()
end 'main'
```
```exitcode
35
```


<!-- test: transitive-interface-validation -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface BaseInterface
	function baseMethod() returns Integer
end 'BaseInterface'

interface DerivedInterface extends BaseInterface
	function derivedMethod() returns Integer
end 'DerivedInterface'

// IncompleteType is missing baseMethod from BaseInterface
type IncompleteType implements DerivedInterface
	var value Integer

	function derivedMethod() returns Integer
		return value
	end 'derivedMethod'
end 'IncompleteType'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/interfaces/transitive-interface-validation.test:14:6: Partial interface implementation: type 'IncompleteType' is missing 1 method(s):
  - baseMethod() returns Integer (from BaseInterface)
```


<!-- test: transitive-interface-complete -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface BaseInterface
	function baseMethod() returns Integer
end 'BaseInterface'

interface DerivedInterface extends BaseInterface
	function derivedMethod() returns Integer
end 'DerivedInterface'

// CompleteType implements all methods from both interfaces
type CompleteType implements DerivedInterface
	var value Integer

	function baseMethod() returns Integer
		return value
	end 'baseMethod'

	function derivedMethod() returns Integer
		return value * 2
	end 'derivedMethod'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'CompleteType'

function main() returns ExitCode
	var t = CompleteType.create(value: 21)
	return t.baseMethod() + t.derivedMethod()
end 'main'
```
```exitcode
63
```
