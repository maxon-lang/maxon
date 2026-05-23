---
feature: interface-conformance
status: stable
keywords: [interface, conformance, implements, type-checking]
category: type-system
---

# Interface Conformance Checking

## Documentation

### Declaring Interface Conformance

Types declare interface conformance using the `implements` keyword. The type must implement all methods declared by the interface:

```text
interface Printable
  function toString() returns int
end 'Printable'

type MyType implements Printable
  function toString() returns int
    return 42
  end 'toString'
end 'MyType'
```

### Multiple Interface Conformance

Types can conform to multiple interfaces:

```text
type MyType implements Interface1, Interface2
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

type BadCounter implements Counter
  function get() returns int
    return 0
  end 'get'
  // ERROR: missing 'increment' method
end 'BadCounter'
```

## Tests

<!-- test: conformance-basic -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Counter
	function get() returns Integer
	function increment()
end 'Counter'

type SimpleCounter implements Counter
	var value Integer

	function get() returns Integer
		return value
	end 'get'

	function increment()
		value = value + 1
	end 'increment'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'SimpleCounter'

function main() returns ExitCode
	var c = SimpleCounter.create(40)
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

typealias Integer = int(i64.min to i64.max)

interface Readable
	function read() returns Integer
end 'Readable'

interface Writable
	function write(value Integer)
end 'Writable'

type Buffer implements Readable, Writable
	var data Integer

	function read() returns Integer
		return data
	end 'read'

	function write(value Integer)
		data = value
	end 'write'

	static function create(data Integer) returns Self
		return Self{data: data}
	end 'create'
end 'Buffer'

function main() returns ExitCode
	var buf = Buffer.create(0)
	buf.write(42)
	return buf.read()
end 'main'
```
```exitcode
42
```

<!-- test: conformance-missing-method -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Counter
	function get() returns Integer
	function increment()
end 'Counter'

type BadCounter implements Counter
	let value Integer

	function get() returns Integer
		return value
	end 'get'
end 'BadCounter'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/interface-conformance/conformance-missing-method.test:10:6: Partial interface implementation: type 'BadCounter' is missing 1 method(s):
  - increment() returns void
```

<!-- test: conformance-wrong-param-type -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Processor
	function process(value Integer) returns Integer
end 'Processor'

type BadProcessor implements Processor
	function process(value Float) returns Integer
		return 0
	end 'process'
end 'BadProcessor'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/interface-conformance/conformance-wrong-param-type.test:10:6: Partial interface implementation: type 'BadProcessor' has 1 method(s) with wrong signature:
  - process(value Float) returns Integer (expected process(value Integer) returns Integer)
```

<!-- test: conformance-wrong-return-type -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Provider
	function provide() returns Integer
end 'Provider'

type BadProvider implements Provider
	function provide() returns Float
		return 0.0
	end 'provide'
end 'BadProvider'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/interface-conformance/conformance-wrong-return-type.test:10:6: Partial interface implementation: type 'BadProvider' has 1 method(s) with wrong signature:
  - provide() returns Float (expected provide() returns Integer)
```

<!-- test: conformance-extra-methods-ok -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Simple
	function getValue() returns Integer
end 'Simple'

type Extended implements Simple
	let value Integer

	function getValue() returns Integer
		return value
	end 'getValue'

	function extraMethod() returns Integer
		return 100
	end 'extraMethod'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Extended'

function main() returns ExitCode
	let e = Extended.create(42)
	return e.getValue()
end 'main'
```
```exitcode
42
```

<!-- test: conformance-no-interface -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Standalone
	var value Integer

	function get() returns Integer
		return value
	end 'get'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Standalone'

function main() returns ExitCode
	let s = Standalone.create(42)
	return s.get()
end 'main'
```
```exitcode
42
```

<!-- test: builtin-interface-user-code -->
```maxon
type MyCollection uses Element implements BuiltinArrayLiteral
	var managed __ManagedMemory

	static function init(managed __ManagedMemory) returns Self
		return MyCollection{managed: managed}
	end 'init'
end 'MyCollection'

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: interface-method-unused-param-allowed -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet(volume Integer) returns Integer
end 'Greeter'

type Silent implements Greeter
	let value Integer

	function greet(volume Integer) returns Integer
		return value
	end 'greet'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Silent'

function callGreet(g Greeter) returns Integer
	return g.greet(99)
end 'callGreet'

function main() returns ExitCode
	let s = Silent.create(42)
	return callGreet(s)
end 'main'
```
```exitcode
42
```


<!-- test: interface-method-via-extended-interface -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Base
	function ping(payload Integer) returns Integer
end 'Base'

interface Extended extends Base
	function other() returns Integer
end 'Extended'

type Impl implements Extended
	let n Integer

	function ping(payload Integer) returns Integer
		return n
	end 'ping'

	function other() returns Integer
		return n + 1
	end 'other'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Impl'

function callPing(b Base) returns Integer
	return b.ping(7)
end 'callPing'

function main() returns ExitCode
	let i = Impl.create(5)
	return callPing(i)
end 'main'
```
```exitcode
5
```


<!-- test: non-interface-method-on-conforming-type-still-errors -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet() returns Integer
end 'Greeter'

type Hello implements Greeter
	let value Integer

	function greet() returns Integer
		return value
	end 'greet'

	function helper(unused Integer) returns Integer
		return value
	end 'helper'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Hello'

function main() returns ExitCode
	let h = Hello.create(1)
	return h.helper(5)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/interface-conformance/non-interface-method-on-conforming-type-still-errors.test:16:18: unused variable: 'unused'
```


<!-- test: interface-method-local-var-still-errors -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet(volume Integer) returns Integer
end 'Greeter'

type Silent implements Greeter
	let value Integer

	function greet(volume Integer) returns Integer
		let unusedLocal = 99
		return value
	end 'greet'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Silent'

function main() returns ExitCode
	let s = Silent.create(1)
	return s.greet(0)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/interface-conformance/interface-method-local-var-still-errors.test:13:7: unused variable: 'unusedLocal'
```
