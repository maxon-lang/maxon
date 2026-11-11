---
feature: lazy-static
status: experimental
keywords: [static, let, initializer, before-main, struct-literal]
category: language
---

# Static Initializers — the VALUE half

## Documentation

A `static let` is initialized by a constant, an array literal, a collection conversion, or a struct literal of
the type whose body declares it. An initializer calls no function: a call is E3220, so write a literal, or build
the value where it is used. The cases here pin the VALUE every such initializer leaves in its slot;
`specs/lazy-static-observable.md` pins what a value cannot show, and the one exception to a slot being kept: a
standard-library static that nothing reads is not emitted at all, initializer included.

### Syntax

```text
type MyType
	static let cached = MyType{n: 1}
	static let DEFAULTS = [1, 2, 3]
end 'MyType'
```

### Semantics

- An initializer calls nothing (E3220), so it is a function of the program text alone, in dependency order
  whether or not anything reads the field, a standard-library static aside (`specs/lazy-static-observable.md`)
- Every read answers with the value the initializer left
- Assigning to a `static let` is E2013 (`specs/static-variables.md`)
- A `static let` holds immutable data, so a function that hands it back declares `returns immutable`
- A constant initializer is folded at compile time

### Use Cases

Sharing one computed value:

```text
type CharacterSet
	static let cachedWhitespaces = CharacterSet{chars: CharSet from ['\t'], categoryMask: 8388608}

	public static function whitespaces() returns immutable CharacterSet
		return CharacterSet.cachedWhitespaces
	end 'whitespaces'
end 'CharacterSet'
```

## Tests

<!-- test: error.a-static-initialized-by-a-function-call-is-refused -->
### A static initialized by a function call is refused

```maxon
typealias Count = int(0 to u64.max)

type Config
	static let value = Config.makeValue()
	export var n as Count

	static function makeValue() returns Config
		return Config{n: 42}
	end 'makeValue'

	static function getValue() returns Count
		return Config.value.n
	end 'getValue'
end 'Config'

function main() returns ExitCode
	return Config.getValue()
end 'main'
```
```maxoncstderr
error E3220: <fragment>:5:21: the initializer of 'Config.value' calls 'Config.makeValue': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: lazy-static.multiple-fields -->
### Several struct-literal statics in one type

```maxon
typealias Count = int(0 to u64.max)

type Cache
	static let a = Cache{n: 10}
	static let b = Cache{n: 20}
	export var n as Count

	static function sum() returns Count
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
### A static initialized by an array literal

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Lookup
	static let values = IntArray from [10, 20, 30]

	static function get(index Integer) returns Integer
		return try Lookup.values.get(index as ElementIndex) otherwise -1
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
	static let ws = CharacterSet.whitespacesAndNewlines

	static function isWhitespace(c Character) returns bool
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
### A static initialized by a collection conversion

```maxon
typealias CharSet = Set with Character

type Vowels
	static let vowelSet = CharSet from ['a', 'e', 'i', 'o', 'u']

	static function contains(c Character) returns bool
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

<!-- test: error.a-static-initialized-by-a-free-function-is-refused -->
### A static initialized by a free function is refused

A bare call inside a type body names the free function, and a free function is a call like any other.

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

	static function getX() returns Count
		return Registry.cachedPair.x
	end 'getX'
end 'Registry'

function buildPair() returns Pair
	return Pair.create(11, y: 22)
end 'buildPair'

function main() returns ExitCode
	print("{Registry.getX()}")
	return 0
end 'main'
```
```maxoncstderr
error E3220: <fragment>:14:26: the initializer of 'Registry.cachedPair' calls 'buildPair': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: lazy-static.two-loads-in-one-function -->
### Two loads of one static in the same function

Every other case in this file reads its static through a one-load accessor. Here one function loads the same
static twice, and both loads answer with the same value.

```maxon
type Vocab
	static let indent = "  "

	static function describe(a String, b String) returns String
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
### Several statics loaded repeatedly, including inside a branch

Three loads of two different statics in one function, one of them on a conditional path: a load that runs on
only some paths answers with the same value as the loads that run on every path.

```maxon
type Vocab
	static let first = "A"
	static let second = "B"

	static function mix(flag bool) returns String
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

A struct construction names a TYPE rather than a function, and its labelled arguments are its FIELDS. It is
a literal, not a call: a field's value is a constant, a String or array literal, an empty container or
another construction, a call among the fields is E3220, and a field the literal omits takes the DEFAULT its
declaration supplies.

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

	static let origin = Pair{a: 1, b: 2}

	static function get() returns immutable Pair
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

	static let greeting = Holder{name: "hi"}

	static function get() returns immutable Holder
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

	static let origin = Pair{a: SEED, b: 2}

	static function get() returns immutable Pair
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

	static let seeded = Holder{xs: [1, 2, 3]}

	static function get() returns immutable Holder
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

<!-- test: error.struct-literal-field-is-a-call -->
### A struct literal whose field is a static factory call is refused

A field is an ARGUMENT, and a call among the arguments is a call in the initializer.

```maxon
typealias Count = int(0 to u64.max)

type Inner
	export var n as Count

	static function create(n Count) returns Inner
		return Inner{n: n}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static let base = Outer{inner: Inner.create(7)}

	static function get() returns Count
		return Outer.base.inner.n
	end 'get'
end 'Outer'

function main() returns ExitCode
	print("{Outer.get()}")
	return 0
end 'main'
```
```maxoncstderr
error E3220: <fragment>:15:20: the initializer of 'Outer.base' calls 'Inner.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: lazy-static.struct-literal-field-default -->
### A struct literal that omits a field with a declared default

```maxon
typealias Count = int(0 to u64.max)

type Pair
	export var a as Count
	export var b = 7 as Count

	static let origin = Pair{a: 1}

	static function get() returns immutable Pair
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

<!-- test: lazy-static.struct-literal-in-two-types -->
### Two types, each with its own struct-literal static

```maxon
typealias Count = int(0 to u64.max)

type A
	export var n as Count

	static let one = A{n: 1}

	static function get() returns immutable A
		return A.one
	end 'get'
end 'A'

type B
	export var n as Count

	static let two = B{n: 2}

	static function get() returns immutable B
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

Nothing ever loads `Pair.origin`, and the program still runs to a clean exit.

```maxon
public typealias Count = int(0 to u64.max)

public type Pair
	export var a as Count
	export var b as Count

	static let origin = Pair{a: 1, b: 2}
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

	static let base = Outer{inner: Inner{n: 7}}

	static function get() returns Outer
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
error E3076: specs/lazy-static/error.struct-literal-initializer-nested-other-type.maxon:11:39: type 'Inner' can only be constructed from within its own methods; use a static factory method instead
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
error E3076: specs/lazy-static/error.struct-literal-initializer-at-file-scope.maxon:9:19: type 'Pair' can only be constructed from within its own methods; use a static factory method instead
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

	static let origin = Pair{1, 2}

	static function get() returns Pair
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
error E2010: specs/lazy-static/error.struct-literal-initializer-positional-fields.maxon:8:27: Expected identifier but got '1'
```

### Error: a static field's initializer must consume everything up to the end of its line

An initializer is its whole token range, so a token left over after the expression is refused where it stands
rather than ignored, in the words a body's binding gets (`specs/top-level-let.md`).

<!-- test: error.static-field-init-trailing-tokens -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var v as Integer

	static function create() returns Self
		return Self{v: 3}
	end 'create'
end 'Box'

type Holder
	static let b = Box.create() zzz

	static function get() returns Integer
		return Holder.b.v
	end 'get'
end 'Holder'

function main() returns ExitCode
	return Holder.get()
end 'main'
```
```maxoncstderr
error E2001: specs/lazy-static/error.static-field-init-trailing-tokens.maxon:13:30: unexpected token: 'zzz'
```
