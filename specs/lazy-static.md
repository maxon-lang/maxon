---
feature: lazy-static
status: experimental
keywords: [static, var, let, lazy, initializer, cache]
category: language
---

# Lazy Static Initializers

## Documentation

Static fields can be initialized with complex expressions including function calls, struct literals, and array literals. These initializers are evaluated lazily on first access.

### Syntax

```text
type MyType
  static var cached = SomeType.create()
  static let DEFAULTS = [1, 2, 3]
end 'MyType'
```

### Semantics

- The initializer expression is evaluated the first time the static field is accessed
- After initialization, subsequent accesses return the cached value
- `static var` fields can be reassigned after initialization
- `static let` fields are immutable after initialization
- Constant initializers (integer, float, bool literals) continue to be evaluated at compile time

### Use Cases

Caching expensive computations:

```text
type CharacterSet
  static var cachedWhitespace = CharacterSet.buildWhitespace()

  export static function whitespace() returns CharacterSet
    return CharacterSet.cachedWhitespace
  end 'whitespace'
end 'CharacterSet'
```

## Tests

<!-- test: lazy-static.basic-function-call -->
### Basic lazy static with function call

```maxon
typealias Count = int(0 to u64.max)

type Config
	static var value = Config.makeValue()
	export var n as Count

	static function makeValue() returns Config
		return Config{n: 42}
	end 'makeValue'

	export static function getValue() returns Config
		return Config.value
	end 'getValue'
end 'Config'

function main() returns ExitCode
	let c = Config.getValue()
	return c.n
end 'main'
```
```exitcode
42
```

<!-- test: lazy-static.initialized-once -->
### Lazy static initialized only once

```maxon
typealias Count = int(0 to u64.max)

type Counter
	static var initCount = 0
	static var cached = Counter.createInstance()
	export var id as Count

	static function createInstance() returns Counter
		Counter.initCount = Counter.initCount + 1
		return Counter{id: Counter.initCount}
	end 'createInstance'

	export static function getInstance() returns Counter
		return Counter.cached
	end 'getInstance'

	export static function getInitCount() returns Count
		return Counter.initCount
	end 'getInitCount'
end 'Counter'

function main() returns ExitCode
	let a = Counter.getInstance()
	let b = Counter.getInstance()
	let c = Counter.getInstance()
	print("{a.id} {b.id} {c.id} {Counter.getInitCount()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1 1 1
```

<!-- test: lazy-static.factory-call-with-labelled-argument -->
### Lazy static built by a static factory call with a labelled argument

```maxon
typealias Count = int(0 to u64.max)

type Point
	export var x as Count
	export var y as Count

	static function create(x Count, y Count) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Defaults
	static var origin = Point.create(0, y: 0)

	export static function getOrigin() returns Point
		return Defaults.origin
	end 'getOrigin'
end 'Defaults'

function main() returns ExitCode
	let p = Defaults.getOrigin()
	print("{p.x} {p.y}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 0
```

<!-- test: lazy-static.mutable-reassign -->
### Lazy static var can be reassigned

```maxon
typealias Count = int(0 to u64.max)

type State
	static var current = State.makeDefault()
	export var value as Count

	static function makeDefault() returns State
		return State{value: 0}
	end 'makeDefault'

	export static function get() returns State
		return State.current
	end 'get'

	export static function set(s State)
		State.current = s
	end 'set'

	static function create(value Count) returns Self
		return Self{value: value}
	end 'create'
end 'State'

function main() returns ExitCode
	let a = State.get()
	print("{a.value} ")
	State.set(State.create(99))
	let b = State.get()
	print("{b.value}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 99
```

<!-- test: lazy-static.multiple-fields -->
### Multiple lazy statics in same type

```maxon
typealias Count = int(0 to u64.max)

type Cache
	static var a = Cache.buildA()
	static var b = Cache.buildB()
	export var n as Count

	static function buildA() returns Cache
		return Cache{n: 10}
	end 'buildA'

	static function buildB() returns Cache
		return Cache{n: 20}
	end 'buildB'

	export static function sum() returns Count
		return Cache.a.n + Cache.b.n
	end 'sum'
end 'Cache'

function main() returns ExitCode
	return Cache.sum()
end 'main'
```
```exitcode
30
```

<!-- test: lazy-static.array-literal -->
### Lazy static with array literal

```maxon
typealias Integer = int(i64.min to i64.max)

type Lookup
	static var values = [10, 20, 30]

	export static function get(index Integer) returns Integer
		return try Lookup.values.get(index) otherwise -1
	end 'get'
end 'Lookup'

function main() returns ExitCode
	print("{Lookup.get(0)} {Lookup.get(1)} {Lookup.get(2)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10 20 30
```

<!-- test: lazy-static.charset-cache -->
### CharacterSet caching pattern

```maxon
type WSCache
	static var ws = CharacterSet.whitespacesAndNewlines()

	export static function isWhitespace(c Character) returns bool
		return WSCache.ws.contains(c)
	end 'isWhitespace'
end 'WSCache'

function main() returns ExitCode
	if WSCache.isWhitespace(' ') 'c1'
		print("space ")
	end 'c1'
	if WSCache.isWhitespace('a') 'c2'
		print("FAIL")
	end 'c2'
	if WSCache.isWhitespace('\t') 'c3'
		print("tab")
	end 'c3'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
space tab
```

<!-- test: lazy-static.collection-initializer -->
### Lazy static with collection initializer

```maxon
typealias CharSet = Set with Character

type Vowels
	static let vowelSet = CharSet from ['a', 'e', 'i', 'o', 'u']

	export static function contains(c Character) returns bool
		return Vowels.vowelSet.contains(c)
	end 'contains'
end 'Vowels'

function main() returns ExitCode
	print("{Vowels.contains('a')} {Vowels.contains('b')} {Vowels.contains('u')}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true false true
```

<!-- test: lazy-static.cross-type-return -->
### Lazy static with function returning a different type

```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var x as Count
	export var y as Count

	static function create(x Count, y Count) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Pair'

type Registry
	static let cachedPair = buildPair()

	export static function getX() returns Count
		return Registry.cachedPair.x
	end 'getX'

	export static function getY() returns Count
		return Registry.cachedPair.y
	end 'getY'
end 'Registry'

function buildPair() returns Pair
	return Pair.create(11, y: 22)
end 'buildPair'

function main() returns ExitCode
	print("{Registry.getX()} {Registry.getY()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11 22
```

<!-- test: lazy-static.two-loads-in-one-function -->
### Two loads of one lazy static in the same function

Each load emits a guard whose "already initialized" edge is a **fall-through** to its own merge
block, so nothing may sit between the two. The init block is therefore emitted at the end of the
function rather than next to its merge block: placed next to it, the second guard — which is
emitted *into* the first merge block — would fall through into the first init block, which ends by
branching back to the first merge block. That is an endless loop. Every other test in this file
reads its static through a one-load accessor, which is why one load was enough to look correct.

```maxon
type Vocab
	static let indent = "  "

	export static function describe(a String, b String) returns String
		var s = "{Vocab.indent}{a}\n"
		s.append("{Vocab.indent}{b}\n")
		return s
	end 'describe'
end 'Vocab'

function main() returns ExitCode
	let text = Vocab.describe("x", b: "y")
	print("{text}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
  x
  y
```

<!-- test: lazy-static.repeated-loads-across-a-branch -->
### Several lazy statics loaded repeatedly, including inside a branch

Three loads of two different statics in one function, one of them on a conditional path, so the
deferred init blocks are a chain rather than a single pair and one guard sits inside a branch.

```maxon
type Vocab
	static let first = "A"
	static let second = "B"

	export static function mix(flag bool) returns String
		var s = "{Vocab.first}1"

		if flag 'withSecond'
			s.append("{Vocab.second}2")
		end 'withSecond'

		s.append("{Vocab.first}3")
		return s
	end 'mix'
end 'Vocab'

function main() returns ExitCode
	let on = Vocab.mix(true)
	let off = Vocab.mix(false)
	print("{on} {off}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
A1B2A3 A1A3
```

## A struct literal as a static field's initializer

A struct construction is a CALL whose callee is a TYPE rather than a function, and whose labelled
arguments are its FIELDS. Everything the cases below assert follows from that one sentence: a field's
value is admitted from exactly the set a factory ARGUMENT is (a constant, a String or array literal, an
empty container, another call — or another construction), a field the literal omits takes the DEFAULT its
declaration supplies, and the record is built on first access by the same deferred machinery a
`static var x = T.create()` already uses.

The construction is legal **inside the type's own body and nowhere else**, which is the ordinary E3076
restriction rather than a rule of its own — a `static` member's initializer is written inside the `type`
body that declares it, so `Pair{…}` is admitted there for the same reason `Self{…}` is admitted in a
method, and the same literal at file scope is refused.

<!-- test: lazy-static.struct-literal-initializer -->
### A static field initialized with a struct literal

```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count

	static var origin = Pair{a: 1, b: 2}

	export static function get() returns Pair
		return Pair.origin
	end 'get'
end 'Pair'

function main() returns ExitCode
	let p = Pair.get()
	print("{p.a} {p.b}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: lazy-static.struct-literal-managed-field -->
### A struct literal whose field is a String

The box becomes the field's sole owner, so the literal's immortal record is CLONED in and released with
the box after `main` — this case is leak-gated (a missed drop, or a doubled one, is exit 101).

```maxon
type Holder
	export var name as String

	static var greeting = Holder{name: "hi"}

	export static function get() returns Holder
		return Holder.greeting
	end 'get'
end 'Holder'

function main() returns ExitCode
	let h = Holder.get()
	print("{h.name}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi
```

<!-- test: lazy-static.struct-literal-field-from-constant -->
### A struct literal whose field is a top-level constant

```maxon
typealias Count = int(0 to u64.max)

let SEED = 5

type Pair
	export var a as Count
	export var b as Count

	static var origin = Pair{a: SEED, b: 2}

	export static function get() returns Pair
		return Pair.origin
	end 'get'
end 'Pair'

function main() returns ExitCode
	let p = Pair.get()
	print("{p.a} {p.b}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 2
```

<!-- test: lazy-static.struct-literal-array-field -->
### A struct literal whose field is an array literal

```maxon
typealias Count = int(0 to u64.max)
typealias CountArray = Array with Count

type Holder
	export var xs as CountArray

	static var seeded = Holder{xs: [1, 2, 3]}

	export static function get() returns Holder
		return Holder.seeded
	end 'get'
end 'Holder'

function main() returns ExitCode
	let h = Holder.get()
	print("{try h.xs.get(1) otherwise 0}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: lazy-static.struct-literal-field-is-a-call -->
### A struct literal whose field is a static factory call

The case that says a field is an ARGUMENT: its value is produced by running the program's own code, and
the result is moved into the box exactly as a materialized constant is.

```maxon
typealias Count = int(0 to u64.max)

type Inner
	export var n as Count

	export static function create(n Count) returns Inner
		return Inner{n: n}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static var base = Outer{inner: Inner.create(7)}

	export static function get() returns Outer
		return Outer.base
	end 'get'
end 'Outer'

function main() returns ExitCode
	let o = Outer.get()
	print("{o.inner.n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: lazy-static.struct-literal-field-default -->
### A struct literal that omits a field with a declared default

```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count = 7

	static var origin = Pair{a: 1}

	export static function get() returns Pair
		return Pair.origin
	end 'get'
end 'Pair'

function main() returns ExitCode
	let p = Pair.get()
	print("{p.a} {p.b}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 7
```

<!-- test: lazy-static.struct-literal-reassigned -->
### A struct-literal static var reassigned to another literal

```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count

	static var origin = Pair{a: 1, b: 2}

	export static function bump()
		Pair.origin = Pair{a: 9, b: 9}
	end 'bump'

	export static function get() returns Pair
		return Pair.origin
	end 'get'
end 'Pair'

function main() returns ExitCode
	let p = Pair.get()
	Pair.bump()
	let q = Pair.get()
	print("{p.a} {q.a}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 9
```

<!-- test: lazy-static.struct-literal-in-two-types -->
### Two types, each with its own struct-literal static

```maxon
typealias Count = int(0 to u64.max)

type A
	export var n as Count

	static var one = A{n: 1}

	export static function get() returns A
		return A.one
	end 'get'
end 'A'

type B
	export var n as Count

	static var two = B{n: 2}

	export static function get() returns B
		return B.two
	end 'get'
end 'B'

function main() returns ExitCode
	print("{A.get().n} {B.get().n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: lazy-static.struct-literal-never-read -->
### A struct-literal static nothing ever reads

The initializer runs on first access, so a static nothing accesses builds nothing — and the program still
exits cleanly, with no record left behind for the cleanup to trip over.

```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count

	static var origin = Pair{a: 1, b: 2}
end 'Pair'

function main() returns ExitCode
	print("no read")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
no read
```

### Error: a struct literal names a type other than the one whose body it is written in

E3076 is the ordinary constructor restriction, reached from a static field's initializer exactly as it is
from a method body — the type name is what decides, not the position.

<!-- test: error.struct-literal-initializer-nested-other-type -->
```maxon
typealias Count = int(0 to u64.max)

type Inner
	export var n as Count
end 'Inner'

type Outer
	export var inner as Inner

	static var base = Outer{inner: Inner{n: 7}}

	export static function get() returns Outer
		return Outer.base
	end 'get'
end 'Outer'

function main() returns ExitCode
	let o = Outer.get()
	print("{o.inner.n}")
	return 0
end 'main'
```
```maxoncstderr
error E3076: specs/fragments/lazy-static/error.struct-literal-initializer-nested-other-type.test:11:39: type 'Inner' can only be constructed from within its own methods; use a static factory method instead
```

### Error: a struct literal at FILE scope is refused, including for the type's own name

A file-scope binding is written inside no type body, so there is no body the restriction could admit it
in — which is what makes the static-member spelling the only one this feature adds.

<!-- test: error.struct-literal-initializer-at-file-scope -->
```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count
end 'Pair'

let origin = Pair{a: 1, b: 2}

function main() returns ExitCode
	print("{origin.a}")
	return 0
end 'main'
```
```maxoncstderr
error E3076: specs/fragments/lazy-static/error.struct-literal-initializer-at-file-scope.test:9:19: type 'Pair' can only be constructed from within its own methods; use a static factory method instead
```

### Error: a struct literal's fields are LABELLED, in a static field's initializer too

A struct literal names slots outright, so every field carries its `name:` — including the first, which is
the opposite of a call's rule. The refusal is the grammar's own, in the same words a literal written in a
method body gets.

<!-- test: error.struct-literal-initializer-positional-fields -->
```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b as Count

	static var origin = Pair{1, 2}

	export static function get() returns Pair
		return Pair.origin
	end 'get'
end 'Pair'

function main() returns ExitCode
	let p = Pair.get()
	print("{p.a} {p.b}")
	return 0
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/lazy-static/error.struct-literal-initializer-positional-fields.test:8:27: Expected identifier but got '1'
```

### Error: A lazy static field's initializer must consume everything up to the end of its line

A static field with a non-constant initializer is deferred to its first access and re-parsed from a
stored token region — the same door a top-level `var` uses, and it dropped its leftovers the same way:
`static var b = Box.create() zzz` initialized `b` and said nothing about `zzz`.

<!-- test: error.static-field-init-trailing-tokens -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var v as Integer

	export static function create() returns Self
		return Self{v: 3}
	end 'create'
end 'Box'

type Holder
	static var b = Box.create() zzz

	export static function get() returns Integer
		return Holder.b.v
	end 'get'
end 'Holder'

function main() returns ExitCode
	return Holder.get()
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/lazy-static/error.static-field-init-trailing-tokens.test:13:30: Expected 'end of global initializer' but got 'zzz'
```
