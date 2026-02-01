---
feature: interface-conformance
status: stable
keywords: [interface, conformance, is, implements, type-checking]
category: type-system
---

# Interface Conformance Checking

## Documentation

### Declaring Interface Conformance

Types declare interface conformance using the `is` keyword. Methods implementing interface requirements must use the qualified name `InterfaceName.methodName`:

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

If a type declares conformance but doesn't implement all required methods (or doesn't use qualified names), a compile error is reported:

```text
interface Counter
  function get() returns int
  function increment()
end 'Counter'

type BadCounter is Counter
  function get() returns int
    return 0
  end 'get'
  // ERROR: missing 'Counter.increment' method
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

<!-- test: conformance-unqualified-method-error -->
```maxon
interface Counter
  function get() returns int
end 'Counter'

type BadCounter is Counter
  var value int

  // ERROR: method must be declared as Counter.get
  function get() returns int
    return value
  end 'get'
end 'BadCounter'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E015: specs/fragments/interface-conformance.conformance-unqualified-method-error.1.test:1:1: Partial interface implementation: type 'BadCounter' is missing 1 method(s):
  - get() returns int
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
