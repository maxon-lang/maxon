---
feature: init-from-literal-rules
status: experimental
keywords: [InitableFromStringLiteral, from, conformance, equatable, equals, operator]
category: language
---

# The rules around `<Type> from "…"` and struct equality

## Documentation

`specs/init-from-literal.md` pins what the `InitableFromStringLiteral` sugar DOES. This file pins the
four decisions around it that the canonical corpus has no case for, and every one of them was taken by
running the reference compiler on the program below rather than by reading it.

**1. The conformance is required, and it is checked after the merge.** `<Type> from "…"` is sugar for
`<Type>.init(<the literal>)`, but the oracle refuses it for a type that does not declare
`implements InitableFromStringLiteral` — even when that type has a perfectly good `static function
init(value String)`. shv2 cannot decide that where the construction is parsed (the `implements` clause is
recorded when the type's OWN file is parsed, and shv2 orders files only by source path), so the parser
records the site and `ConformanceCheck.checkLiteralInitConformance` reports it. The wording is the
oracle's, word for word, so a `/specs` case pinning it would be portable.

**2. An UNDECLARED name keeps its `Undefined variable`.** The new parser arm claims the
`<identifier> from <string literal>` shape only when the identifier names a declared `type`. That bound
is not tidiness: the oracle answers `E2004 Undefined variable 'Bogus'` for `Bogus from "hello"`, so a
wider claim would have replaced a correct diagnostic with a bespoke one.

**3. Struct `==` asks for the METHOD, not for the conformance — the opposite rule to (1).** `a == b` on
two values of one struct dispatches that struct's own `equals`, and the oracle accepts it with no
`implements Equatable` clause at all. It is the same rule string interpolation already follows for
`toString`. The two constructs therefore genuinely differ, which is why each is pinned here: guessing that
they agreed would have made one of them wrong.

Only `==` and `!=` are served. Ordering (`<`, `>`, `<=`, `>=`) means `Comparable.compare` returning an
`Ordering`, which is a different method and a different verdict shape; a struct pair still earns the
comparison type mismatch there.

**4. The dispatch target must return `bool` — a struct that merely HAS the name is not `Equatable`.**
Rule (3) matches by METHOD NAME, and a name proves nothing about a result. `primitive-conformance.md`
already records the measurement for the WITNESS form of this same dispatch — an interface declaring
`function equals(other Self) returns Integer` made `a == b` *"evaluate to the raw `7` that `equals`
returned"* — and `Parser.witnessTargetIsProtocol` refuses it there. The DIRECT form did not ask, and both
operators went silently wrong: `a == b` evaluated to whatever `equals` returned (a `String`, measured),
and `a != b` applied `logicalNot` to that heap POINTER while stamping the result `boolean` — a fabricated
truth value, always `true`. Both are now E3005 at the operator, naming the method and its actual result.

⚠ The reference COMPILES the `==` half of that (it prints the `String`) and fails an internal cast
(`E9001`, a .NET type-cast trace naming no source position) on the `!=` half. This is therefore a
deliberate shv2 divergence, taken for `ParseError.nonBoolCondition`'s stated reason: an internal error
leaking to a user is a bootstrap bug and is not a diagnostic to copy, and shv2 rejects where the type is
known — in the parser. The two cases below pin it as a divergence, not as agreement.

⚠ **TWO FURTHER refusals below are shv2 DIVERGENCES from the reference, and the
`struct-equality-without-an-equals-method` / `struct-ordering-is-not-an-equals-dispatch` cases pin them as
such — not as agreement.** Both were measured on the oracle, one program each, at the same time as the
rules above:

* **A struct with NO `equals` at all.** The oracle COMPILES `a == b` and answers TRUE for two separately
  constructed boxes with equal fields — a synthesized structural equality shv2 does not have. shv2
  refuses with `E3005 cannot compare struct with struct`, which is where it stood before this rung and is
  a clean, positioned refusal rather than a wrong answer. The case below pins the refusal so that the day
  structural equality lands, it goes red at exactly the right line.
* **Ordering on a struct.** Both compilers refuse and both spell it `E3005`, but the reference says
  `operator '<' is not defined for type 'Box'` where shv2 says `cannot compare struct with struct`. Same
  verdict, different sentence; the shv2 wording predates this rung.

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

	export static function make(n Count) returns Box
		return Box{n: n}
	end 'make'

	export function equals(other Box) returns bool
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

	export static function make(n Count) returns Box
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

	export static function make(n Count) returns Box
		return Box{n: n}
	end 'make'

	export function equals(other Box) returns bool
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

	export static function make(n Count) returns Box
		return Box{n: n}
	end 'make'

	export function equals(other Box) returns String
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

	export static function make(n Count) returns Box
		return Box{n: n}
	end 'make'

	export function equals(other Box) returns Count
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
