---
feature: interfaces
status: stable
keywords: [interface, is, self, method, conformance]
category: type-system
---

# Interfaces

## Developer Notes

Interfaces define interfaces that structs can conform to. Key implementation:

- `interface` keyword parsed in `Parser::parseInterface()` (parser_decl.cpp)
- Interface AST node `InterfaceDefAST` contains method signatures
- Struct conformance declared with `is` keyword: `struct Foo is Interface1, Interface2`
- **Methods defined inside struct body** with explicit `self` parameter
- Methods registered with qualified name `ReceiverType.methodName` in semantic analyzer
- `Self` type in interface signatures replaced with conforming type during checking
- Conformance validation in `SemanticAnalyzer::checkInterfaceConformance()`
- Methods called with explicit type: `Type.method(instance, args)`

The `Self` type is a placeholder that gets resolved to the concrete type during conformance checking. This enables interfaces to define methods that return or accept the conforming type.

## Documentation

Interfaces define a set of methods that types can implement. They provide a form of interface-based polymorphism.

### Interface Declaration

Interfaces are declared with the `interface` keyword and contain method signatures:

```maxon
interface Hashable
    function hash(self Self) int
end 'Hashable'
```

Method signatures in interfaces use `Self` to refer to the conforming type.

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

Methods are defined **inside the struct body** using the `function` keyword with an explicit `self` parameter:

```maxon
struct Point is Hashable
    x int
    y int

    function hash(self Point) int
        return self.x + self.y * 31
    end 'hash'
end 'Point'
```

The first parameter must be named `self` and have the struct's type. Methods can be exported with the `export` keyword:

```maxon
struct Point
    x int
    y int

    export function getX(self Point) int
        return self.x
    end 'getX'
end 'Point'
```

### Calling Methods

Methods are called with explicit type qualification:

```maxon
var p = Point{x: 10, y: 20}
var h = Point.hash(p)
```

### Self Type

The `Self` type in interface signatures represents the conforming type:

```maxon
interface Cloneable
    function clone(self Self) Self
end 'Cloneable'

struct Point is Cloneable
    x int
    y int

    function clone(self Point) Point
        return Point{x: self.x, y: self.y}
    end 'clone'
end 'Point'
```

### Standard Library Interfaces

The standard library (`stdlib/interfaces.maxon`) defines commonly used interfaces that are automatically imported when referenced:

- **Hashable** - Types that can be hashed to an integer: `function hash(self Self) int`
- **Equatable** - Types that can be compared for equality: `function equals(self Self, other Self) bool`
- **Comparable** - Types that can be ordered: `function compare(self Self, other Self) int`
- **Cloneable** - Types that can be copied: `function clone(self Self) Self`
- **Iterable** - Types that can be iterated over (used by for-in loops):
  - `function hasNext(self Self) int`
  - `function getCurrent(self Self) int`
  - `function next(self Self) Self`

The `Iterator` struct in `stdlib/iter/iterator.maxon` conforms to `Iterable` and is used by `for` loops with `range()`.

### Example

```maxon
interface Hashable
    function hash(self Self) int
end 'Hashable'

struct Point is Hashable
    x int
    y int

    function hash(self Point) int
        return self.x + self.y * 31
    end 'hash'
end 'Point'

function main() int
    var p = Point{x: 10, y: 20}
    var h = Point.hash(p)
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
    function hash(self Self) int
end 'Hashable'

struct Point is Hashable
    x int
    y int

    function hash(self Point) int
        return self.x + self.y * 31
    end 'hash'
end 'Point'

function main() int
    var p = Point{x: 10, y: 20}
    return Point.hash(p)
end 'main'
```
```exitcode
630
```


<!-- test: multiple-methods -->
```maxon
interface Describable
    function describe(self Self) int
    function value(self Self) int
end 'Describable'

struct Counter is Describable
    count int

    function describe(self Counter) int
        return 100 + self.count
    end 'describe'

    function value(self Counter) int
        return self.count
    end 'value'
end 'Counter'

function main() int
    var c = Counter{count: 42}
    return Counter.describe(c) + Counter.value(c)
end 'main'
```
```exitcode
184
```


<!-- test: method-with-params -->
```maxon
interface Calculator
    function add(self Self, n int) int
end 'Calculator'

struct Accumulator is Calculator
    total int

    function add(self Accumulator, n int) int
        return self.total + n
    end 'add'
end 'Accumulator'

function main() int
    var acc = Accumulator{total: 10}
    return Accumulator.add(acc, 5)
end 'main'
```
```exitcode
15
```


<!-- test: multiple-interfaces -->
```maxon
interface Hashable
    function hash(self Self) int
end 'Hashable'

interface Equatable
    function equals(self Self, other Self) int
end 'Equatable'

struct Point is Hashable, Equatable
    x int
    y int

    function hash(self Point) int
        return self.x + self.y
    end 'hash'

    function equals(self Point, other Point) int
        if self.x == other.x and self.y == other.y then return 1
        return 0
    end 'equals'
end 'Point'

function main() int
    var p1 = Point{x: 3, y: 4}
    var p2 = Point{x: 3, y: 4}
    return Point.hash(p1) + Point.equals(p1, p2)
end 'main'
```
```exitcode
8
```


<!-- test: self-return-type -->
```maxon
interface Movable
    function move(self Self, dx int, dy int) Self
end 'Movable'

struct Point is Movable
    x int
    y int

    function move(self Point, dx int, dy int) Point
        return Point{x: self.x + dx, y: self.y + dy}
    end 'move'
end 'Point'

function main() int
    var p = Point{x: 10, y: 20}
    var p2 = Point.move(p, 5, 10)
    return p2.x + p2.y
end 'main'
```
```exitcode
45
```


