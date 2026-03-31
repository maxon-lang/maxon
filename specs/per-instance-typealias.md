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

Use dot-syntax to construct a per-instance value explicitly:

```text
var idx = FooContainer.Idx{0}
```

## Tests

### Basic per-instance typealias: return type is tracked

<!-- test: basic-return-type -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value T
	export var tag Idx

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

	export var value T
	export var tag Idx

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
	var a = WrapperA{value: 1, tag: 5}
	var b = WrapperB{value: 2, tag: 0}
	let aTag = a.getTag()
	b.setTag(aTag)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/per-instance-typealias/wrong-instance-error.test:26:4: argument type mismatch for 't': expected 'WrapperB.Idx', got 'WrapperA.Idx'
```

### Literal in range is accepted

<!-- test: literal-accepted -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value T
	export var tag Idx

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer

function main() returns ExitCode
	var w = IntWrapper{value: 42, tag: 5}
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

	export var value T
	export var tag Idx
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer
typealias StrWrapper = Wrapper with String

function takeStrTag(t StrWrapper.Idx) returns StrWrapper.Idx
	return t
end 'takeStrTag'

function main() returns ExitCode
	var iw = IntWrapper{value: 1, tag: 7}
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

	export var value T
	export var tag Idx

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer

function main() returns ExitCode
	let idx = IntWrapper.Idx{42}
	var w = IntWrapper{value: 99, tag: idx}
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
