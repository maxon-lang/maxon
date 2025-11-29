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
- Struct conformance with `is` and type binding with `with`: `struct Bar is Foo with int`
- **Methods have implicit `self` parameter** - not declared in signatures
- Methods defined inside struct body with simple `function methodName(params)` format
- Methods registered with qualified name `ReceiverType.methodName` in semantic analyzer
- `Self` type in interface signatures replaced with conforming type during checking
- Conformance validation in `SemanticAnalyzer::checkInterfaceConformance()`
- **Partial implementation is an error** - all interface methods must be implemented
- Methods called with method syntax `instance.method(args)`
- Inside method bodies, bare identifiers resolve to struct fields (implicit `self.`)

The `Self` type is a placeholder that gets resolved to the concrete type during conformance checking. This enables interfaces to define methods that return or accept the conforming type.

## Documentation

Interfaces define a set of methods that types can implement. They provide a form of interface-based polymorphism.

### Interface Declaration

Interfaces are declared with the `interface` keyword and contain method signatures:

```maxon
interface Hashable
    function hash() int
end 'Hashable'
```

Method signatures in interfaces have an implicit `self` parameter of type `Self` (the conforming type). You don't declare `self` - it's automatic.

### Struct Conformance

Structs declare conformance to interfaces using the `is` keyword:

```maxon
struct Point is Hashable
    x int
    y int
end 'Point'
```

A struct can conform to multiple interfaces:

```maxon
struct Point is Hashable, Equatable
    x int
    y int
end 'Point'
```

### Method Implementation

Methods are defined **inside the struct body** using simple `function methodName(params)` syntax. The `self` parameter is implicit:

```maxon
struct Point is Hashable
    x int
    y int

    function hash() int
        return x + y * 31
    end 'hash'
end 'Point'
```

Inside method bodies, you can access struct fields directly without `self.` prefix:
- `x` resolves to `self.x`
- `y` resolves to `self.y`

You can still use `self.field` explicitly if needed, especially when a parameter shadows a field name.

Methods can be exported with the `export` keyword:

```maxon
struct Point
    x int
    y int

    export function getX() int
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
    function clone() Self
end 'Cloneable'

struct Point is Cloneable
    x int
    y int

    function clone() Point
        return Point{x: x, y: y}
    end 'clone'
end 'Point'
```

### Associated Types

Interfaces can declare associated types with `uses`. Structs bind concrete types with `with`:

```maxon
interface Container uses Element
    function get(index int) Element
end 'Container'

struct IntArray is Container with int
    data [10]int
    
    function get(index int) int
        return data[index]
    end 'get'
end 'IntArray'
```

See the [Associated Types](associated-types.md) spec for full documentation.

### Partial Implementation Error

A struct must implement **all** methods from interfaces it conforms to. Partial implementation is an error:

```maxon
interface TwoMethods
    function first() int
    function second() int
end 'TwoMethods'

struct Incomplete is TwoMethods
    function first() int
        return 1
    end 'first'
    // Missing: second()
end 'Incomplete'
```

This produces a compiler error listing all missing methods.

### Standard Library Interfaces

The standard library (`stdlib/interfaces.maxon`) defines commonly used interfaces that are automatically imported when referenced:

- **Hashable** - Types that can be hashed to an integer: `function hash() int`
- **Equatable** - Types that can be compared for equality: `function equals(other Self) bool`
- **Comparable** - Types that can be ordered: `function compare(other Self) int`
- **Cloneable** - Types that can be copied: `function clone() Self`
- **Iterable** - Types that can be iterated over (used by for-in loops):
  - Uses `Element` associated type
  - `function hasNext() int`
  - `function getCurrent() Element`
  - `function next() Self`

The `Iterator` struct in `stdlib/iter/iterator.maxon` conforms to `Iterable` and is used by `for` loops with `range()`.

### Example

```maxon
interface Hashable
    function hash() int
end 'Hashable'

struct Point is Hashable
    x int
    y int

    function hash() int
        return x + y * 31
    end 'hash'
end 'Point'

function main() int
    var p = Point{x: 10, y: 20}
    var h = p.hash()
    print(h)
    return 0
end 'main'
```
```output
ExitCode: 0
Stdout: 630
```


## Tests

<!-- test: basic-interface -->
```maxon
interface Hashable
    function hash() int
end 'Hashable'

struct Point is Hashable
    x int
    y int

    function hash() int
        return x + y * 31
    end 'hash'
end 'Point'

function main() int
    var p = Point{x: 10, y: 20}
    return p.hash()
end 'main'
```
```exitcode
630
```


<!-- test: multiple-methods -->
```maxon
interface Describable
    function describe() int
    function value() int
end 'Describable'

struct Counter is Describable
    count int

    function describe() int
        return 100 + count
    end 'describe'

    function value() int
        return count
    end 'value'
end 'Counter'

function main() int
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
    function add(n int) int
end 'Calculator'

struct Accumulator is Calculator
    total int

    function add(n int) int
        return total + n
    end 'add'
end 'Accumulator'

function main() int
    var acc = Accumulator{total: 10}
    return acc.add(5)
end 'main'
```
```exitcode
15
```


<!-- test: multiple-interfaces -->
```maxon
interface Hashable
    function hash() int
end 'Hashable'

interface Equatable
    function equals(other Self) int
end 'Equatable'

struct Point is Hashable, Equatable
    x int
    y int

    function hash() int
        return x + y
    end 'hash'

    function equals(other Point) int
        if x == other.x and y == other.y then return 1
        return 0
    end 'equals'
end 'Point'

function main() int
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
    function move(dx int, dy int) Self
end 'Movable'

struct Point is Movable
    x int
    y int

    function move(dx int, dy int) Point
        return Point{x: x + dx, y: y + dy}
    end 'move'
end 'Point'

function main() int
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
    function inc() int
end 'Incrementable'

struct Value is Incrementable
    n int

    function inc() int
        return n + 1
    end 'inc'
end 'Value'

function main() int
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
    function one() int
    function two() int
    function three() int
end 'ThreeMethods'

struct Incomplete is ThreeMethods
    value int

    function one() int
        return 1
    end 'one'
end 'Incomplete'

function main() int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 8, column 1
Partial interface implementation: struct 'Incomplete' is missing 2 method(s):
  - two() int
  - three() int

  8 | struct Incomplete is ThreeMethods
    | ^
```


<!-- test: non-interface-method -->
```maxon
struct Calculator
    value int

    function double() int
        return value * 2
    end 'double'
    
    function triple() int
        return value * 3
    end 'triple'
end 'Calculator'

function main() int
    var c = Calculator{value: 7}
    return c.double() + c.triple()
end 'main'
```
```exitcode
35
```


