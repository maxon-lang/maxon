---
feature: init-from-literal-rules
status: experimental
keywords: [InitableFromStringLiteral, from, conformance, equatable, equals, operator]
category: language
---

# The rules around `<Type> from "…"` and struct equality

## Documentation

`specs/init-from-literal.md` pins what the `InitableFromStringLiteral` sugar DOES. This file pins the
four decisions around it that the canonical corpus has no case for.

**1. The conformance is required, and it is checked after the merge.** `<Type> from "…"` is sugar for
`<Type>.init(<the literal>)`, and it is refused for a type that does not declare
`implements InitableFromStringLiteral` — even when that type has a perfectly good `static function
init(value String)`. The compiler cannot decide that where the construction is parsed (the `implements` clause is
recorded when the type's OWN file is parsed, and the compiler orders files only by source path), so the parser
records the site and `ConformanceCheck.checkLiteralInitConformance` reports it.

**2. An UNDECLARED name keeps its `Undefined variable`.** The parser arm claims the
`<identifier> from <string literal>` shape only when the identifier names a declared `type`. That bound
is not tidiness: `Bogus from "hello"` is correctly answered `E2004 Undefined variable 'Bogus'`, and a
wider claim would replace that diagnostic with a bespoke one.

**3. Struct `==` asks for the METHOD, not for the conformance — the opposite rule to (1).** `a == b` on
two values of one struct dispatches that struct's own `equals`, and it is accepted with no
`implements Equatable` clause at all. It is the same rule string interpolation already follows for
`toString`. The two constructs therefore genuinely differ, which is why each is pinned here: guessing that
they agreed would make one of them wrong.

Only `==` and `!=` are served. Ordering (`<`, `>`, `<=`, `>=`) means `Comparable.compare` returning an
`Ordering`, which is a different method and a different verdict shape; a struct pair still earns the
comparison type mismatch there.

**4. The dispatch target must return `bool` — a struct that merely HAS the name is not `Equatable`.**
Rule (3) matches by METHOD NAME, and a name proves nothing about a result. `primitive-conformance.md`
pins the WITNESS form of this same dispatch — unchecked, an interface declaring
`function equals(other Self) returns Integer` would make `a == b` evaluate to the raw `7` that `equals`
returned — and `Parser.witnessTargetIsProtocol` refuses it there. The DIRECT form asks too; without
it both operators would go silently wrong: `a == b` would evaluate to whatever `equals` returned (a `String`),
and `a != b` would apply `logicalNot` to that heap POINTER while stamping the result `boolean` — a fabricated
truth value, always `true`. Both are E3005 at the operator, naming the method and its actual result,
reported by the parser, where the type is known.

**Two further refusals** are pinned by the `struct-equality-without-an-equals-method` /
`struct-ordering-is-not-an-equals-dispatch` cases:

* **A struct with NO `equals` at all.** There is no synthesized structural equality, so `a == b` is refused
  with `E3005 cannot compare struct with struct` — a clean, positioned refusal rather than a wrong answer. If
  structural equality is ever added, the case goes red at exactly the right line.
* **Ordering on a struct.** Refused with the same `E3005 cannot compare struct with struct`.

## Tests

<!-- test: error.from-literal-requires-the-conformance -->
```maxon
type Wrapper
	export let value as String

	static function init(value String) returns Wrapper
		return Wrapper{value: value}
	end 'init'
end 'Wrapper'

function main() returns ExitCode
	let w = Wrapper from "hello"
	return w.value.byteLength()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: Type 'Wrapper' does not conform to InitableFromStringLiteral
```

<!-- test: error.from-literal-on-an-undeclared-name -->
```maxon
function main() returns ExitCode
	let w = Bogus from "hello"
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:10: Undefined variable 'Bogus'
```

<!-- test: struct-equality-needs-no-conformance -->
```maxon
typealias Count = int(0 to 100)

type Box
	export let n as Count

	static function make(n Count) returns Box
		return Box{n: n}
	end 'make'

	function equals(other Box) returns bool
		return n == other.n
	end 'equals'
end 'Box'

function main() returns ExitCode
	let a = Box.make(1)
	let b = Box.make(1)
	let c = Box.make(2)
	if a == b 'eq'
		print("equal\n")
	end 'eq'
	if a != c 'neq'
		print("not equal\n")
	end 'neq'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
equal
not equal
```

<!-- test: error.struct-equality-without-an-equals-method -->
```maxon
typealias Count = int(0 to 100)

type Box
	export let n as Count

	static function make(n Count) returns Box
		return Box{n: n}
	end 'make'
end 'Box'

function main() returns ExitCode
	let a = Box.make(1)
	let b = Box.make(1)
	if a == b 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:7: type mismatch: 'cannot compare struct with struct'
```

<!-- test: error.struct-ordering-is-not-an-equals-dispatch -->
```maxon
typealias Count = int(0 to 100)

type Box
	export let n as Count

	static function make(n Count) returns Box
		return Box{n: n}
	end 'make'

	function equals(other Box) returns bool
		return n == other.n
	end 'equals'
end 'Box'

function main() returns ExitCode
	let a = Box.make(1)
	let b = Box.make(2)
	if a < b 'lt'
		return 1
	end 'lt'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:7: type mismatch: 'cannot compare struct with struct'
```

<!-- test: error.struct-equality-requires-a-bool-equals -->
```maxon
typealias Count = int(0 to 100)

type Box
	export let n as Count

	static function make(n Count) returns Box
		return Box{n: n}
	end 'make'

	function equals(other Box) returns String
		return "not-a-bool"
	end 'equals'
end 'Box'

function main() returns ExitCode
	let a = Box.make(1)
	let b = Box.make(1)
	let r = a == b
	print("{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:12: Operator '==' dispatches 'Box.equals', which returns 'String' — an equality operator requires the 'bool' result 'Equatable' declares
```

<!-- test: error.struct-inequality-requires-a-bool-equals -->
```maxon
typealias Count = int(0 to 100)

type Box
	export let n as Count

	static function make(n Count) returns Box
		return Box{n: n}
	end 'make'

	function equals(other Box) returns Count
		return 7
	end 'equals'
end 'Box'

function main() returns ExitCode
	let a = Box.make(1)
	let b = Box.make(1)
	if a != b 'ne'
		return 1
	end 'ne'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:7: Operator '!=' dispatches 'Box.equals', which returns 'int' — an equality operator requires the 'bool' result 'Equatable' declares
```

### `from` reaches a type through its alias

`<Name> from [...]` takes the same construction whether `<Name>` is the type or an alias of it, and
naming an alias there is a use of that alias. Each case below names its alias ONLY in the `from`, so an
alias the construction failed to record would be refused as unused.

<!-- test: a-set-alias-takes-from-an-array-literal -->
```maxon
typealias Letters = Set with Character

function main() returns ExitCode
	let vowels = Letters from ['a', 'e', 'i']
	print("{vowels.count()} {vowels.contains('a')} {vowels.contains('e')} {vowels.contains('i')} {vowels.contains('o')}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 true true true false
```

<!-- test: a-list-alias-takes-from-an-array-literal -->
```maxon
typealias Reading = int(0 to 1000)
typealias Readings = List with Reading

function main() returns ExitCode
	let readings = Readings from [30, 10, 20]

	for r in readings 'each'
		print("{r} ")
	end 'each'

	print("count={readings.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
30 10 20 count=3
```

<!-- test: a-vector-alias-takes-from-an-array-literal -->
```maxon
typealias Reading = int(0 to 1000)
typealias ReadingTriple = Vector with 3 Reading

function main() returns ExitCode
	let readings = ReadingTriple from [30, 10, 20]

	for r in readings 'each'
		print("{r} ")
	end 'each'

	print("count={readings.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
30 10 20 count=3
```

<!-- test: a-user-type-takes-from-an-array-literal -->
A user type that implements `InitableFromArrayLiteral` takes `from [...]` exactly as the standard
collections do: the literal becomes the `Array` its `init` receives, in order.
```maxon
typealias Digit = int(0 to 9)
typealias DigitArray = Array with Digit
typealias Number = int(0 to i64.max)

type Digits implements InitableFromArrayLiteral with Digit
	export var value as Number
	export var length as Number

	static function init(digits DigitArray) returns Self
		var total = 0 as Number

		for d in digits 'each'
			total = total * 10 + d
		end 'each'

		return Self{value: total, length: digits.count() as Number}
	end 'init'
end 'Digits'

function main() returns ExitCode
	let n = Digits from [4, 0, 7]
	print("{n.value} {n.length}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
407 3
```

<!-- test: a-user-type-takes-from-an-array-literal-at-module-scope -->
The same construction as a module-scope initializer: like `FilePath from ""` there, it is the call to the
type's `init` that the module initializer makes before `main`.
```maxon
typealias Digit = int(0 to 9)
typealias DigitArray = Array with Digit
typealias Number = int(0 to i64.max)

type Digits implements InitableFromArrayLiteral with Digit
	export var value as Number
	export var length as Number

	static function init(digits DigitArray) returns Self
		var total = 0 as Number

		for d in digits 'each'
			total = total * 10 + d
		end 'each'

		return Self{value: total, length: digits.count() as Number}
	end 'init'
end 'Digits'

let lucky = Digits from [4, 0, 7]

function main() returns ExitCode
	print("{lucky.value} {lucky.length}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
407 3
```

<!-- test: error.a-module-scope-array-literal-init-element-outside-its-range -->
At module scope the literal is built at the array its `init` declares, so an element is held to that array's
element range exactly as a struct field's constant is held to the field's.
```maxon
typealias Digit = int(0 to 9)
typealias DigitArray = Array with Digit
typealias Number = int(0 to i64.max)

type Digits implements InitableFromArrayLiteral with Digit
	export var value as Number
	export var length as Number

	static function init(digits DigitArray) returns Self
		var total = 0 as Number

		for d in digits 'each'
			total = total * 10 + d
		end 'each'

		return Self{value: total, length: digits.count() as Number}
	end 'init'
end 'Digits'

let unlucky = Digits from [4, 12]

function main() returns ExitCode
	print("{unlucky.value}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:21:31: Value 12 is outside the range of 'Digit' (int(0 to 9))
```

<!-- test: an-array-alias-reaches-its-static-from -->
`from` is a keyword and also the name of `Array`'s static constructor over any iterable; after a `.` it
is the member name, so an `Array` alias reaches it. The alias's element is `RangeBound`, the element a
`Range` iterates: the iterable must bind `Element` to the array's own element (E3127).
```maxon
typealias ReadingArray = Array with RangeBound

function main() returns ExitCode
	let span = 3 to 6
	let readings = ReadingArray.from(span)

	for r in readings 'each'
		print("{r} ")
	end 'each'

	print("count={readings.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 4 5 6 count=4
```
