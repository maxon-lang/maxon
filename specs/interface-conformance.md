---
feature: interface-conformance
status: stable
keywords: [interface, conformance, is, implements, type-checking]
category: type-system
---

# Interface Conformance Checking

## Documentation

### Declaring Interface Conformance

Types declare interface conformance using the `is` keyword. The type must implement all methods declared by the interface:

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
error E3016: specs/fragments/interface-conformance/conformance-missing-method.test:7:6: Partial interface implementation: type 'BadCounter' is missing 1 method(s):
  - increment() returns void
```

<!-- test: conformance-wrong-param-type -->
```maxon
interface Processor
  function process(value int) returns int
end 'Processor'

type BadProcessor is Processor
  function process(value float) returns int
    return 0
  end 'process'
end 'BadProcessor'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/interface-conformance/conformance-wrong-param-type.test:6:6: Partial interface implementation: type 'BadProcessor' has 1 method(s) with wrong signature:
  - process(value float) returns int (expected process(value int) returns int)
```

<!-- test: conformance-wrong-return-type -->
```maxon
interface Provider
  function provide() returns int
end 'Provider'

type BadProvider is Provider
  function provide() returns float
    return 0.0
  end 'provide'
end 'BadProvider'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/interface-conformance/conformance-wrong-return-type.test:6:6: Partial interface implementation: type 'BadProvider' has 1 method(s) with wrong signature:
  - provide() returns float (expected provide() returns int)
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

<!-- test: error.builtin-interface-stdlib-only -->
```maxon
type MyCollection uses Element is BuiltinArrayLiteral
  var managed __ManagedMemory

  static function init(managed __ManagedMemory) returns Self
    return {managed: managed}
  end 'init'
end 'MyCollection'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/interface-conformance/error.builtin-interface-stdlib-only.test:2:6: Interface 'BuiltinArrayLiteral' can only be implemented by stdlib types
```
