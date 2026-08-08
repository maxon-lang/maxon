---
feature: per-instance-typealias
status: experimental
keywords: [typealias, generics, type-safety, nominal-types, per-instance]
category: type-system
---

# Per-Instance Ranged Typealiases in Generics

## Documentation

### Overview

When a ranged typealias is declared inside a generic type, each concrete instantiation gets a nominally distinct copy. This prevents accidentally mixing values from different instances (e.g., passing an index from one pool to a different pool).

### Syntax

Declare a ranged typealias inside a generic type body:

```text
type Container uses T
	export typealias Idx = int(0 to u64.max)

	export function push(item T) returns Idx
		// ...
	end 'push'

	export function get(index Idx) returns T
		// ...
	end 'get'
end 'Container'
```

When instantiated:

```text
typealias FooContainer = Container with Foo
typealias BarContainer = Container with Bar

// FooContainer.Idx and BarContainer.Idx are distinct types
var fooIdx = fooContainer.push(myFoo)   // returns FooContainer.Idx
fooContainer.get(fooIdx)                // OK
barContainer.get(fooIdx)                // ERROR: type mismatch
```

### Explicit Conversion

Use `as` to convert between compatible per-instance aliases (same base type and range):

```text
var barIdx = fooIdx as BarContainer.Idx
barContainer.get(barIdx)  // OK after explicit conversion
```

### Construction

Cast a value into the per-instance type with `as`:

```text
var idx = 0 as FooContainer.Idx
```

## Tests

### Basic per-instance typealias: return type is tracked

<!-- test: basic-return-type -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T) returns Self
		return Self{value: value, tag: 0}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'

	export function withTag(t Idx) returns Self
		return Self{value: self.value, tag: t}
	end 'withTag'
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer

function main() returns ExitCode
	var w = IntWrapper.create(42)
	w = w.withTag(7)
	let t = w.getTag()
	if t == 7 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Wrong instance tag is rejected

<!-- test: wrong-instance-error -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'

	export function setTag(t Idx)
		self.tag = t
	end 'setTag'
end 'Wrapper'

typealias WrapperA = Wrapper with Integer
typealias WrapperB = Wrapper with Integer

function main() returns ExitCode
	let a = WrapperA.create(1, tag: 5)
	let b = WrapperB.create(2, tag: 0)
	let aTag = a.getTag()
	b.setTag(aTag)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/per-instance-typealias/wrong-instance-error.test:30:4: argument type mismatch for 't': expected 'WrapperB.Idx', got 'WrapperA.Idx'
```

### Literal in range is accepted

<!-- test: literal-accepted -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer

function main() returns ExitCode
	let w = IntWrapper.create(42, tag: 5)
	let t = w.getTag()
	if t == 5 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Explicit conversion with 'as'

<!-- test: as-conversion -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer
typealias StrWrapper = Wrapper with String

function takeStrTag(t StrWrapper.Idx) returns StrWrapper.Idx
	return t
end 'takeStrTag'

function main() returns ExitCode
	let iw = IntWrapper.create(1, tag: 7)
	let intTag = iw.tag
	let strTag = intTag as StrWrapper.Idx
	let result = takeStrTag(strTag)
	if result == 7 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Dot-syntax construction

<!-- test: dot-construction -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer

function main() returns ExitCode
	let w = IntWrapper.create(99, tag: 42)
	let t = w.getTag()
	if t == 42 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Cast preserves the source's identity

An `as` between per-instance aliases is a CONVERSION: it yields a distinct value of the target
type and leaves the SOURCE unchanged. So after `let bTag = aTag as WB.Idx`, `aTag` is still
`WA.Idx` and remains usable everywhere `WA.Idx` is expected — the cast did not mutate it.

<!-- test: cast-preserves-source -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'

	export function setTag(t Idx)
		self.tag = t
	end 'setTag'
end 'Wrapper'

typealias WA = Wrapper with Integer
typealias WB = Wrapper with Integer

function main() returns ExitCode
	let a = WA.create(1, tag: 5)
	let aTag = a.getTag()
	let bTag = aTag as WB.Idx
	var a2 = WA.create(9, tag: 0)
	a2.setTag(aTag)
	let check = a2.getTag()
	if bTag == 5 'converted'
		if check == 5 'preserved'
			return 0
		end 'preserved'
	end 'converted'
	return 1
end 'main'
```
```exitcode
0
```

### Cast does not launder the source's instance

The `as` produces `bTag` (a `WB.Idx`), but `aTag` is STILL a `WA.Idx` — the cast is not an in-place
retag. So `bTag` is accepted where `WB.Idx` is expected, while the SOURCE `aTag` passed to the same
slot is the genuine cross-instance mismatch.

<!-- test: error.cast-does-not-launder-source -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'

	export function setTag(t Idx)
		self.tag = t
	end 'setTag'
end 'Wrapper'

typealias WA = Wrapper with Integer
typealias WB = Wrapper with Integer

function main() returns ExitCode
	let a = WA.create(1, tag: 5)
	let aTag = a.getTag()
	let bTag = aTag as WB.Idx
	var b = WB.create(2, tag: 0)
	b.setTag(bTag)
	b.setTag(aTag)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/per-instance-typealias/error.cast-does-not-launder-source.test:32:4: argument type mismatch for 't': expected 'WB.Idx', got 'WA.Idx'
```

### Per-instance Idx decays to plain int on return

A per-instance `Idx` is a nominal wrapper over a SCALAR int, so it DECAYS to plain int wherever a
non-per-instance numeric is expected — a `return` included. `getTag()` returns `IW.Idx`, and returning
it from an `ExitCode` function is accepted (no narrowing: the range fits), yielding the value.

<!-- test: return-decays-to-plain -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to 200)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

typealias IW = Wrapper with Integer

function main() returns ExitCode
	let w = IW.create(1, tag: 42)
	return w.getTag()
end 'main'
```
```exitcode
42
```

### Per-instance Idx decays when reassigned into a plain int var

The decay is not special to `return` — a per-instance `Idx` assigned into a plain `int` variable decays
just the same, as it does when passed to a plain-int parameter.

<!-- test: reassign-decays-to-plain -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

typealias IW = Wrapper with Integer

function main() returns ExitCode
	let w = IW.create(1, tag: 7)
	var n = 0
	n = w.getTag()
	if n == 7 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```
