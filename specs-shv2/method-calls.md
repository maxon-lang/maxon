---
feature: method-calls
status: stable
keywords: [method, call, type, struct, instance]
category: type-system
---

# Method Calls

## Documentation

### Calling Methods

Methods are called using dot notation on an instance:

```text
type Counter
  var count as int

  function increment()
    count = count + 1
  end 'increment'

  function get() returns int
    return count
  end 'get'
end 'Counter'

function main() returns int
  var c = Counter{count: 0}
  c.increment()
  return c.get()
end 'main'
```

### Methods with Parameters

Methods can take parameters in addition to the implicit self:

```text
type Adder
  var value as int

  function add(n int)
    value = value + n
  end 'add'
end 'Adder'
```

### Methods Returning Values

Methods can return values that can be used in expressions:

```text
type Box
  var value as int

  function getValue() returns int
    return value
  end 'getValue'
end 'Box'

function main() returns int
  var b = Box{value: 42}
  return b.getValue() + 1  // 43
end 'main'
```

### shv2 note on the four expected-error blocks

The four `error-*` cases below carry shv2's own diagnostics rather than the ones the C# bootstrap
reports. Each difference is ratified by `docs/error-codes.txt` — the single registry shared by all
three compilers — and by specs already ported, not decided here:

- **The two unnamed-argument cases are `E2053`, not `E3005`.** The registry defines **E2053**
  (`callArgMissingLabel`) for exactly this rule and claims it for **shv2 alone**, while the bootstrap
  reports it through its general type-mismatch E3005. shv2 also anchors on the OFFENDING ARGUMENT
  rather than on the call, which `consumeArgLabel`'s per-argument anchor buys. Already pinned by
  `parameter-labels.md`, `functions.md`, `where-clauses.md`, `union-managed-payload.md` and
  `enum-full.md`.
- **The too-many-arguments case keeps `E3036`** and differs only in wording, and in counting the
  receiver as an argument — which is what the same call shape already reports in
  `implicit-self-methods.md` (`'Maker.bump' expects 1 argument(s) but 0 were provided`, for a `bump()`
  that declares none) and in `where-clauses.md`.
- **The unknown-method case is `E3004`, not `E4006`.** shv2 resolves a method call at PARSE time, so an
  unknown name is refused before lowering ever runs — one stage EARLIER than the bootstrap, at the same
  position, under E3004 `callUnknownFunction`, the code the registry defines for "a call names a
  function that does not exist -- a typo" and which `functions.md` already states as the rule. The
  difference is WHEN shv2 refuses, and that is what ratifies it.

  ⚠ **This bullet used to say "the registry claims E4006 for `csharp` and `selfhosted` only — shv2
  cannot emit it", and that was FALSE when written.** `docs/error-codes.txt` claims E4006 for all three
  (shv2 spells it `invalidFieldAccess`), and shv2 emits it from `Queries.maxon` — for `hiddenTypeName`
  before this note existed, and since 2026-08-04 for `conditionalExtensionWithheld` too
  (`conditional-extensions.md` pins that one). Corrected 2026-08-04. **The lesson is the standing one:
  derive what IS supplied and never assert what is ABSENT — the absence half rots first, the moment
  anyone builds the thing.** A retraction needs the reason it is actually true for, not the nearest
  reason that sounds structural.

## Tests

<!-- test: method-call-void -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	var count as Integer

	function increment()
		count = count + 1
	end 'increment'

	function get() returns Integer
		return count
	end 'get'

	static function create(count Integer) returns Self
		return Self{count: count}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create(0)
	c.increment()
	c.increment()
	c.increment()
	return c.get()
end 'main'
```
```exitcode
3
```

<!-- test: method-call-with-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Adder
	var total as Integer

	function add(n Integer)
		total = total + n
	end 'add'

	function get() returns Integer
		return total
	end 'get'

	static function create(total Integer) returns Self
		return Self{total: total}
	end 'create'
end 'Adder'

function main() returns ExitCode
	var a = Adder.create(0)
	a.add(10)
	a.add(20)
	a.add(12)
	return a.get()
end 'main'
```
```exitcode
42
```

<!-- test: method-return-in-expr -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Box
	var value as Integer

	function getValue() returns Integer
		return value
	end 'getValue'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	let b = Box.create(40)
	return b.getValue() + 2
end 'main'
```
```exitcode
42
```

<!-- test: method-multiple-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Calculator
	var result as Integer

	function addTwo(a Integer, b Integer)
		result = result + a + b
	end 'addTwo'

	function get() returns Integer
		return result
	end 'get'

	static function create(result Integer) returns Self
		return Self{result: result}
	end 'create'
end 'Calculator'

function main() returns ExitCode
	var calc = Calculator.create(0)
	calc.addTwo(20, b: 22)
	return calc.get()
end 'main'
```
```exitcode
42
```

<!-- test: method-call-on-field-access -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Inner
	var value as Integer

	function get() returns Integer
		return value
	end 'get'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	var inner as Inner

	function getInnerValue() returns Integer
		return inner.get()
	end 'getInnerValue'

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create(Inner.create(42))
	return o.getInnerValue()
end 'main'
```
```exitcode
42
```

<!-- test: method-modify-multiple-fields -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	var x as Integer
	var y as Integer

	function moveBy(dx Integer, dy Integer)
		x = x + dx
		y = y + dy
	end 'moveBy'

	function sum() returns Integer
		return x + y
	end 'sum'

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(10, y: 10)
	p.moveBy(10, dy: 12)
	return p.sum()
end 'main'
```
```exitcode
42
```

<!-- test: method-return-comparison -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Value
	var n as Integer

	function isPositive() returns Integer
		if n > 0 'positive'
			return 1
		end 'positive'
		return 0
	end 'isPositive'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Value'

function main() returns ExitCode
	let v = Value.create(42)
	return v.isPositive()
end 'main'
```
```exitcode
1
```

<!-- test: error-method-unnamed-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Adder
	var total as Integer

	function addTwo(a Integer, b Integer)
		total = total + a + b
	end 'addTwo'

	static function create(total Integer) returns Self
		return Self{total: total}
	end 'create'
end 'Adder'

function main() returns ExitCode
	var x = Adder.create(0)
	x.addTwo(10, 20)
	return 0
end 'main'
```
```maxoncstderr
error E2053: specs/fragments/method-calls/error-method-unnamed-args.test:19:15: the second and later arguments must be named ('name: value')
```

<!-- test: method-named-args-reorder -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Calculator
	var result as Integer

	function compute(a Integer, b Integer, c Integer)
		result = a + b * c
	end 'compute'

	function get() returns Integer
		return result
	end 'get'

	static function create(result Integer) returns Self
		return Self{result: result}
	end 'create'
end 'Calculator'

function main() returns ExitCode
	var calc = Calculator.create(0)
	calc.compute(10, c: 4, b: 8)
	return calc.get()
end 'main'
```
```exitcode
42
```

<!-- test: static-method-named-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Factory
	static function create(x Integer, y Integer) returns Integer
		return x * 10 + y
	end 'create'
end 'Factory'

function main() returns ExitCode
	return Factory.create(4, y: 2)
end 'main'
```
```exitcode
42
```

<!-- test: error-static-method-unnamed-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Factory
	static function create(x Integer, y Integer) returns Integer
		return x * 10 + y
	end 'create'
end 'Factory'

function main() returns ExitCode
	return Factory.create(4, 2)
end 'main'
```
```maxoncstderr
error E2053: specs/fragments/method-calls/error-static-method-unnamed-args.test:12:27: the second and later arguments must be named ('name: value')
```

<!-- test: error-instance-method-too-many-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	var count as Integer

	function increment()
		count = count + 1
	end 'increment'

	static function create(count Integer) returns Self
		return Self{count: count}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create(0)
	c.increment(5)
	return 0
end 'main'
```
```maxoncstderr
error E3036: specs/fragments/method-calls/error-instance-method-too-many-args.test:19:4: 'Counter.increment' expects 1 argument(s) but 2 were provided
```

### Calling a method that does not exist

A call to a method the receiver's type does not declare is a compile error. This holds for
stdlib generic types (`Map`, `Array`, …) exactly as it does for user types — before, an
unknown method on a stdlib generic resolved to a bare `Map.set` callee that nothing had a
signature for, and the backend panicked in `lookupFuncParamTypes` instead of reporting the
typo.

<!-- test: error-no-such-method-on-user-type -->
```maxon

typealias ExitCode = int(0 to 255)

type Counter
	var count as ExitCode

	export static function create() returns Counter
		return Counter{count: 0}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.incrementt()
	return 0
end 'main'
```
```maxoncstderr
error E3004: specs/fragments/method-calls/error-no-such-method-on-user-type.test:15:4: call to undefined function 'Counter.incrementt'
```

<!-- disabled-test: error-no-such-method-on-stdlib-generic -->
<!-- MEASURED 2026-09-04: `E3004: call to undefined function 'Map.set'` where the pin is `E4006: Type 'Map' has no
     method named 'set'`. A method miss on a stdlib generic falls through to the free-function noun, which names a
     symbol the author did not write. -->
```maxon

typealias ExitCode = int(0 to 255)
typealias Count = int(0 to 1000)
typealias CountMap = Map with String, Count

function main() returns ExitCode
	var m = CountMap.create()
	m.set("a", value: 1 as Count)
	return 0
end 'main'
```
```maxoncstderr
error E4006: specs/fragments/method-calls/error-no-such-method-on-stdlib-generic.test:9:2: Type 'Map' has no method named 'set'
```
