---
feature: interfaces
status: stable
keywords: [interface, extends, method, signature, contract]
category: type-system
---

# Interfaces

## Developer Notes

Interfaces define contracts that types can conform to. They declare method signatures that implementing types must provide.

**Syntax:**
```
export? interface Name uses TypeParam, ... extends ParentInterface, ...
    function methodName(params) returns ReturnType

    function methodWithDefault(params) returns ReturnType
        // default implementation
    end 'methodWithDefault'
end 'Name'
```

**AST Changes:**
- Add `InterfaceDecl` struct with name, export flag, generic params, extends list, and methods
- Add `InterfaceMethod` struct with name, params, return type, and optional default body
- Add `interfaces` field to `Program` struct

**Parser Changes:**
- Add `parseInterfaceDecl()` function
- Handle `interface` keyword in main parse loop
- Parse method signatures (no body) or methods with default implementations

**Method Signatures vs Default Implementations:**
- Method signature only: `function name(params) returns Type` followed by newline
- Method with default: has a body and ends with `end 'label'`

**Interface Inheritance:**
- `extends Interface1, Interface2` allows interface composition
- Child interfaces inherit all method requirements from parents

## Documentation

### Defining Interfaces

Interfaces declare method signatures that types must implement:

```text
interface Printable
    function toString() returns int
end 'Printable'
```

### Generic Interfaces

Interfaces can have generic type parameters:

```text
interface Collection uses Element
    function count() returns int
    function isEmpty() returns bool
end 'Collection'
```

### Interface Inheritance

Interfaces can extend other interfaces:

```text
interface List uses Element extends Collection
    function get(index int) returns Element
    function set(index int, value Element)
end 'List'
```

### Default Implementations

Methods can have default implementations:

```text
interface Comparable
    function compare(other Self) returns int

    function lessThan(other Self) returns bool
        return self.compare(other) < 0
    end 'lessThan'
end 'Comparable'
```

## Tests

<!-- test: interface-basic -->
```maxon
interface Counter
    function get() returns int
    function increment()
end 'Counter'

type SimpleCounter is Counter
    var value int

    function get() returns int
        return value
    end 'get'

    function increment()
        value = value + 1
    end 'increment'
end 'SimpleCounter'

function main() returns int
    var c = SimpleCounter{value: 40}
    c.increment()
    c.increment()
    return c.get()
end 'main'
```
```exitcode
42
```

<!-- test: interface-parse-only -->
```maxon
interface Addable
    function add(other int) returns int
end 'Addable'

function main() returns int
    return 42
end 'main'
```
```exitcode
42
```

<!-- test: interface-generic -->
```maxon
interface Container uses Element
    function count() returns int
end 'Container'

function main() returns int
    return 42
end 'main'
```
```exitcode
42
```

<!-- test: interface-extends -->
```maxon
interface Base
    function getValue() returns int
end 'Base'

interface Extended extends Base
    function getDouble() returns int
end 'Extended'

function main() returns int
    return 42
end 'main'
```
```exitcode
42
```

<!-- test: interface-default-impl -->
```maxon
interface WithDefault
    function required() returns int

    function optional() returns int
        return 10
    end 'optional'
end 'WithDefault'

function main() returns int
    return 42
end 'main'
```
```exitcode
42
```
