---
feature: where-clauses
status: stable
keywords: [where, constraints, type-parameters, generics, interfaces]
category: type-system
---

# Where Clauses

## Documentation

### Type Parameter Constraints

The `where` clause constrains type parameters to require specific interface conformance. This enables the compiler to verify that method calls on type parameters are valid, and that concrete types bound to constrained parameters actually implement the required interfaces.

### Syntax

```text
type Container uses Element implements SomeInterface where Element is Hashable
    // Element is guaranteed to have hash() method
end 'Container'
```

Multiple interfaces on the same parameter use `and`:

```text
type Container uses Element where Element is Hashable and Equatable
end 'Container'
```

Multiple constrained parameters use comma separation:

```text
type Pair uses A, B where A is Hashable, B is Cloneable
end 'Pair'
```

### Enforcement

When creating a type alias that binds concrete types to constrained parameters, the compiler checks that each concrete type conforms to all required interfaces:

```text
// OK: String implements Hashable
typealias StringMap = Map with (String, int)

// Error: a type that doesn't implement Hashable cannot be used as Key
```

### Method Resolution

Inside a type with `where` constraints, method calls on constrained type parameters are resolved through the constrained interfaces. For example, if `where Key is Hashable`, then `key.hash()` resolves to the `Hashable.hash()` method.

## Tests

### Basic where clause with Map

Map requires `Key is Hashable`. String implements Hashable, so this should work:

<!-- test: where-clauses.map-basic -->
```maxon
function main() returns ExitCode
    var m = ["hello": 42]
    return try m.get("hello") otherwise 0
end 'main'
```
```exitcode
42
```

### Custom Hashable type as Map key

A user-defined type that implements Hashable can be used as a Map key:

<!-- test: where-clauses.custom-hashable-key -->
```maxon
type MyKey implements Hashable, Equatable
    var value Integer

    function hash() returns Integer
        return self.value * 31
    end 'hash'

    function equals(other MyKey) returns bool
        return self.value == other.value
    end 'equals'
end 'MyKey'

typealias MyKeyMap = Map with (MyKey, int)

function main() returns ExitCode
    return 1
end 'main'
```
```exitcode
1
```

### Where clause constraint violation

Using a type that doesn't implement Hashable as a Map key should produce a compile error:

<!-- test: where-clauses.constraint-violation -->
```maxon
type NotHashable
    var x Integer
end 'NotHashable'

typealias BadMap = Map with (NotHashable, int)

function main() returns ExitCode
    return 0
end 'main'
```
```maxoncstderr
error E3017: specs/fragments/where-clauses/where-clauses.constraint-violation.test:6:11: Type 'NotHashable' does not satisfy constraint 'Hashable' required by type parameter 'Key' of 'Map'
```

### User-defined type with where clause

A user-defined generic type can use where clauses:

<!-- test: where-clauses.user-defined -->
```maxon
interface Valuable
    function value() returns Integer
end 'Valuable'

type Wrapper implements Valuable
    var n Integer

    function value() returns Integer
        return self.n
    end 'value'
end 'Wrapper'

type Holder uses T where T is Valuable
    export var item T
end 'Holder'

typealias WrapperHolder = Holder with Wrapper

function main() returns ExitCode
    var w = Wrapper{n: 10}
    var h = WrapperHolder{item: w}
    return h.item.value()
end 'main'
```
```exitcode
10
```

### Where clause with multiple interfaces using and

A type parameter can require multiple interface conformance:

<!-- test: where-clauses.multiple-interfaces -->
```maxon
interface HasName
    function name() returns Integer
end 'HasName'

interface HasAge
    function age() returns Integer
end 'HasAge'

type Person implements HasName, HasAge
    var _age Integer

    function name() returns Integer
        return 1
    end 'name'

    function age() returns Integer
        return self._age
    end 'age'
end 'Person'

type Registry uses T where T is HasName and HasAge
    export var item T
end 'Registry'

typealias PersonRegistry = Registry with Person

function main() returns ExitCode
    var p = Person{_age: 30}
    var r = PersonRegistry{item: p}
    return r.item.age()
end 'main'
```
```exitcode
30
```

### Where clause violation with and - missing one interface

<!-- test: where-clauses.and-violation -->
```maxon
interface Foo
    function foo() returns Integer
end 'Foo'

interface Bar
    function bar() returns Integer
end 'Bar'

type OnlyFoo implements Foo
    function foo() returns Integer
        return 1
    end 'foo'
end 'OnlyFoo'

type NeedsBoth uses T where T is Foo and Bar
    var item T
end 'NeedsBoth'

typealias Bad = NeedsBoth with OnlyFoo

function main() returns ExitCode
    return 0
end 'main'
```
```maxoncstderr
error E3017: specs/fragments/where-clauses/where-clauses.and-violation.test:20:11: Type 'OnlyFoo' does not satisfy constraint 'Bar' required by type parameter 'T' of 'NeedsBoth'
```

### Equality on unconstrained type parameter requires Equatable

Using `==` or `!=` on a type parameter that isn't constrained with `where T is Equatable` should produce a compile error:

<!-- test: where-clauses.eq-requires-equatable -->
```maxon
type Box uses T
    var item T

    export function eq(other T) returns bool
        return item == other
    end 'eq'
end 'Box'

function main() returns ExitCode
    return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/where-clauses/where-clauses.eq-requires-equatable.test:6:21: Operator '==' requires type parameter 'T' to be constrained with 'where T is Equatable'
```

### Equality on Equatable-constrained type parameter compiles

When the type parameter is properly constrained, `==` should work:

<!-- test: where-clauses.eq-with-equatable -->
```maxon
type Box uses T where T is Equatable
    var item T

    export function eq(other T) returns bool
        return item == other
    end 'eq'
end 'Box'

typealias IntBox = Box with int

function main() returns ExitCode
    var b = IntBox{item: 42}
    if b.eq(other: 42) 'yes'
        return 1
    end 'yes'
    return 0
end 'main'
```
```exitcode
1
```
