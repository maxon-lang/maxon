---
feature: interfaces
status: stable
keywords: [interface, is, with, uses, self, method, conformance]
category: type-system
---

# Interfaces

## Developer Notes

Interfaces define contracts that structs can conform to. Key implementation:

- `interface` keyword parsed in `Parser::parseInterface()` (parser_decl.cpp)
- Interface AST node `InterfaceDefAST` contains method signatures
- Associated types declared with `uses`: `interface Foo uses Element`
- Type conformance with `is` and type binding with `with`: `type Bar is Foo with int`
- **Methods have implicit `self` parameter** - not declared in signatures
- **Interface methods use `function InterfaceName.methodName(params)` syntax** to explicitly declare which interface a method implements
- Non-interface methods use simple `function methodName(params)` syntax
- Methods registered with qualified name `ReceiverType.methodName` in semantic analyzer
- `Self` type in interface signatures replaced with conforming type during checking
- Conformance validation in `SemanticAnalyzer::checkInterfaceConformance()`
- **Partial implementation is an error** - all interface methods must be implemented
- Methods called with method syntax `instance.method(args)`
- Inside method bodies, bare identifiers resolve to type fields (implicit `self.`)

The `Self` type is a placeholder that gets resolved to the concrete type during conformance checking. This enables interfaces to define methods that return or accept the conforming type.

## Documentation

Interfaces define a set of methods that types can implement. They provide a form of interface-based polymorphism.

### Interface Declaration

Interfaces are declared with the `interface` keyword and contain method signatures:

```maxon
interface Hashable
    function hash() returns int
end 'Hashable'
```

Method signatures in interfaces have an implicit `self` parameter of type `Self` (the conforming type). You don't declare `self` - it's automatic.

### Type Conformance

Types declare conformance to interfaces using the `is` keyword:

```maxon
type Point is Hashable
    var x int
    var y int
end 'Point'
```

A type can conform to multiple interfaces:

```maxon
type Point is Hashable, Equatable
    var x int
    var y int
end 'Point'
```

### Method Implementation

Methods implementing interface requirements are defined **inside the type body** using `function InterfaceName.methodName(params)` syntax. The interface prefix explicitly declares which interface the method implements. The `self` parameter is implicit:

```maxon
type Point is Hashable
    var x int
    var y int

    function Hashable.hash() returns int
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
type Point
    var x int
    var y int

    export function getX() returns int
        return x
    end 'getX'
end 'Point'
```

### Calling Methods

Methods are called using the method call syntax - `instance.method()`:

```maxon
var p = Point{x: 10, y: 20}
var h = p.hash()
```

The method receives the instance as an implicit first parameter. Fields are accessed directly by name within the method body.

### Self Type

The `Self` type in interface signatures represents the conforming type:

```maxon
interface Cloneable
    function clone() returns Self
end 'Cloneable'

type Point is Cloneable
    var x int
    var y int

    function Cloneable.clone() returns Point
        return Point{x: x, y: y}
    end 'clone'
end 'Point'
```

### Associated Types

Interfaces can declare associated types with `uses`. Structs bind concrete types with `with`:

```maxon
interface Container uses Element
    function get(index int) returns Element
end 'Container'

type IntArray is Container with int
    var data Array of 10 int

    function Container.get(index int) returns int
        return data[index]
    end 'get'
end 'IntArray'
```

See the [Associated Types](associated-types.md) spec for full documentation.

### Partial Implementation Error

A type must implement **all** methods from interfaces it conforms to. Partial implementation is an error:

```maxon
interface TwoMethods
    function first() returns int
    function second() returns int
end 'TwoMethods'

type Incomplete is TwoMethods
    function TwoMethods.first() returns int
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
- **Comparable** - Types that can be ordered: `returns int`
- **Cloneable** - Types that can be copied: `returns Self`
- **Iterable** - Types that can be iterated over (used by for-in loops):
  - Uses `Element` associated type
  - `returns Element or nil`

The `Iterator` type in `stdlib/iter/iterator.maxon` conforms to `Iterable` and is used by `for` loops with `range()`.

### Example

```maxon
type Point is Hashable
    var x int
    var y int

    function Hashable.hash() returns int
        return x + y * 31
    end 'hash'
end 'Point'

function main() returns int
    var p = Point{x: 10, y: 20}
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
type Point is Hashable
    var x int
    var y int

    function Hashable.hash() returns int
        return x + y * 31
    end 'hash'
end 'Point'

function main() returns int
    var p = Point{x: 10, y: 20}
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
interface Describable
    function describe() returns int
    function value() returns int
end 'Describable'

type Counter is Describable
    var count int

    function Describable.describe() returns int
        return 100 + count
    end 'describe'

    function Describable.value() returns int
        return count
    end 'value'
end 'Counter'

function main() returns int
    var c = Counter{count: 42}
    return c.describe() + c.value()
end 'main'
```
```exitcode
184
```


<!-- test: method-with-params -->
```maxon
interface Calculator
    function add(n int) returns int
end 'Calculator'

type Accumulator is Calculator
    var total int

    function Calculator.add(n int) returns int
        return total + n
    end 'add'
end 'Accumulator'

function main() returns int
    var acc = Accumulator{total: 10}
    return acc.add(5)
end 'main'
```
```exitcode
15
```


<!-- test: multiple-interfaces -->
```maxon
type Point is Hashable, Equatable
    var x int
    var y int

    function Hashable.hash() returns int
        return x + y
    end 'hash'

    function Equatable.equals(other Point) returns int
        if x == other.x and y == other.y 'c1'
            return 1
        end 'c1'
        return 0
    end 'equals'
end 'Point'

function main() returns int
    var p1 = Point{x: 3, y: 4}
    var p2 = Point{x: 3, y: 4}
    return p1.hash() + p1.equals(p2)
end 'main'
```
```exitcode
8
```


<!-- test: self-return-type -->
```maxon
interface Movable
    function move(dx int, dy int) returns Self
end 'Movable'

type Point is Movable
    var x int
    var y int

    function Movable.move(dx int, dy int) returns Point
        return Point{x: x + dx, y: y + dy}
    end 'move'
end 'Point'

function main() returns int
    var p = Point{x: 10, y: 20}
    var p2 = p.move(5, 10)
    return p2.x + p2.y
end 'main'
```
```exitcode
45
```


<!-- test: method-call-syntax -->
```maxon
interface Incrementable
    function inc() returns int
end 'Incrementable'

type Value is Incrementable
    var n int

    function Incrementable.inc() returns int
        return n + 1
    end 'inc'
end 'Value'

function main() returns int
    var v = Value{n: 41}
    return v.inc()
end 'main'
```
```exitcode
42
```


<!-- test: partial-implementation-error -->
```maxon
interface ThreeMethods
    function one() returns int
    function two() returns int
    function three() returns int
end 'ThreeMethods'

type Incomplete is ThreeMethods
    var value int

    function ThreeMethods.one() returns int
        return 1
    end 'one'
end 'Incomplete'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:8:1
Partial interface implementation: type 'Incomplete' is missing 2 method(s):
  - two() returns int
  - three() returns int

  8 | type Incomplete is ThreeMethods
    | ^
```


<!-- test: non-interface-method -->
```maxon
type Calculator
    var value int

    function double() returns int
        return value * 2
    end 'double'

    function triple() returns int
        return value * 3
    end 'triple'
end 'Calculator'

function main() returns int
    var c = Calculator{value: 7}
    return c.double() + c.triple()
end 'main'
```
```exitcode
35
```


<!-- test: transitive-interface-missing-method-error -->
// Test that generic templates implementing Collection require Iterable.next() since Collection extends Iterable
```maxon
type IncompleteCollection uses Element is Collection with Element
    var data Element

    function Collection.count() returns int
        return 1
    end 'count'

    function Collection.get(index int) returns Element or nil
        return data
    end 'get'

    function Collection.set(index int, value Element) returns Self
        data = value
        return self
    end 'set'
end 'IncompleteCollection'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:2:1
Partial interface implementation: type 'IncompleteCollection' is missing 1 method(s):
  - next() returns Element or nil

  2 | type IncompleteCollection uses Element is Collection with Element
    | ^
```


