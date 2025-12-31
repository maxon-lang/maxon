---
feature: interface-conformance
status: stable
keywords: [interface, conformance, is, implements, type-checking]
category: type-system
---

# Interface Conformance Checking

## Developer Notes

When a type declares conformance to an interface using `is InterfaceName`, the compiler must verify that all required interface methods are implemented by the type.

**Conformance Rules:**
1. Type must implement all methods declared in the interface
2. Method signatures must match: name, parameter count, parameter types, return type
3. Default implementations in interfaces are optional to override
4. Interfaces can extend other interfaces - all inherited methods must also be implemented

**Implementation:**
1. Store interface declarations in `interface_map` during registration
2. After registering each type, iterate through its conformances
3. For each interface, verify all required methods exist in the type
4. Report `E015` error with details if a required method is missing

**Error Format:**
```
error E015: type 'TypeName' does not implement interface 'InterfaceName': missing method 'methodName'
```

## Documentation

### Declaring Interface Conformance

Types declare interface conformance using the `is` keyword:

```text
interface Printable
    function toString() returns int
end 'Printable'

type MyType is Printable
    function toString() returns int
        return 42
    end 'toString'
end 'MyType'
```

### Multiple Interface Conformance

Types can conform to multiple interfaces:

```text
type MyType is Interface1, Interface2
    // must implement all methods from both interfaces
end 'MyType'
```

### Conformance Errors

If a type declares conformance but doesn't implement all required methods, a compile error is reported:

```text
interface Counter
    function get() returns int
    function increment()
end 'Counter'

type BadCounter is Counter
    function get() returns int
        return 0
    end 'get'
    // ERROR: missing 'increment' method
end 'BadCounter'
```

## Tests

<!-- test: conformance-basic -->
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

<!-- test: conformance-multiple-interfaces -->
```maxon
interface Readable
    function read() returns int
end 'Readable'

interface Writable
    function write(value int)
end 'Writable'

type Buffer is Readable, Writable
    var data int

    function read() returns int
        return data
    end 'read'

    function write(value int)
        data = value
    end 'write'
end 'Buffer'

function main() returns int
    var buf = Buffer{data: 0}
    buf.write(42)
    return buf.read()
end 'main'
```
```exitcode
42
```

<!-- test: conformance-missing-method -->
```maxon
interface Counter
    function get() returns int
    function increment()
end 'Counter'

type BadCounter is Counter
    var value int

    function get() returns int
        return value
    end 'get'
end 'BadCounter'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
error E015: specs/fragments/interface-conformance.conformance-missing-method.1.test:1:1: Partial interface implementation: type 'BadCounter' is missing 1 method(s):
  - increment() returns void
```

<!-- test: conformance-extra-methods-ok -->
```maxon
interface Simple
    function getValue() returns int
end 'Simple'

type Extended is Simple
    var value int

    function getValue() returns int
        return value
    end 'getValue'

    function extraMethod() returns int
        return 100
    end 'extraMethod'
end 'Extended'

function main() returns int
    var e = Extended{value: 42}
    return e.getValue()
end 'main'
```
```exitcode
42
```

<!-- test: conformance-no-interface -->
```maxon
type Standalone
    var value int

    function get() returns int
        return value
    end 'get'
end 'Standalone'

function main() returns int
    var s = Standalone{value: 42}
    return s.get()
end 'main'
```
```exitcode
42
```
