---
feature: associated-types
status: experimental
keywords: [uses, with, interface, associated, element, iterable]
category: type-system
---

# Associated Types

## Documentation

Associated types allow interfaces to declare type placeholders that implementing types must define. This enables generic interfaces where the concrete types vary by implementation.

### Declaring Associated Types in Interfaces

Use the `uses` keyword after the interface name to declare associated types:

```maxon
interface Container uses Element
	function get(index Index) returns Element
	function set(index Index, value Element) returns Self
end 'Container'
```

Associated types can be used in:
- Return types (`Element`)
- Parameter types (`value Element`)
- Combined with `Self` in the same signature

### Implementing Associated Types

Types bind concrete types to associated types using `with` after the interface name. Interface methods use `function methodName(params)` syntax:

```maxon
typealias Score = int(i64.min to i64.max)

type ScoreArray implements Container with Score
	var data as array of 100 Score
	var len as Score

	function get(index Index) returns Score
		return data[index]
	end 'get'

	function set(index Index, value Score) returns ScoreArray
		data[index] = value
		return ScoreArray{data: data, len: len}
	end 'set'
end 'ScoreArray'
```

The `with` types map positionally to the interface's `uses` types. Method signatures use the concrete type (`int`) that was bound.

### Multiple Associated Types

For interfaces with multiple associated types, list them in order:

```maxon
typealias ID = int(i64.min to i64.max)
typealias Weight = float(f64.min to f64.max)

interface Pair uses First, Second
	function getFirst() returns First
	function getSecond() returns Second
end 'Pair'

type PersonRecord implements Pair with ID, Weight
	let a as ID
	let b as Weight

	function getFirst() returns ID
		return a
	end 'getFirst'

	function getSecond() returns Weight
		return b
	end 'getSecond'
end 'PersonRecord'
```

### The Iterable Interface

The standard library `Iterable` interface uses associated types:

```maxon
interface Iterable uses Element
	function next() returns Element throws IterationError
end 'Iterable'
```

Different iterators define different element types:

- `Iterator` (for `range()`): `implements Iterable with int`
- `string`: `implements Iterable with character` (grapheme cluster)
- `ByteView`: `implements Iterable with byte` (byte value)
- `UTF16View`: `implements Iterable with int` (UTF-16 code unit)
- `CodepointView`: `implements Iterable with int` (Unicode codepoint)

### For-Loop Type Inference

When iterating with `for`, the loop variable's type is inferred from the iterator's `Element` type:

```maxon
function main() returns ExitCode
	let s = "Hi"
	for ch in s 'chars'
		// ch has type 'character' (inferred from string's Element type - grapheme cluster)
		print("{ch}\n")
	end 'chars'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
H
i
```

### Conformance Requirements

A type conforming to an interface with associated types must:

1. Bind all associated types with `with Type1, Type2` (positional order matches `uses`)
2. Implement **all** methods - partial implementation is an error
3. Use exact type matches in method signatures (no implicit conversions)

```maxon
typealias Score = int(i64.min to i64.max)

interface Summable uses Element
	function sum() returns Element
end 'Summable'

type ScorePair implements Summable with Score
	let a as Score
	let b as Score

	function sum() returns Score
		return a + b
	end 'sum'

	static function create(a Score, b Score) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'ScorePair'

function main() returns ExitCode
	let p = ScorePair.create(10, b: 32)
	return p.sum()
end 'main'
```
```exitcode
42
```

### Calling Methods

Methods are called using the method call syntax:

```maxon
var p = IntPair.create(10, b: 32)
var result = p.sum()    // Call sum() method on instance p
```

### Error: Missing Type Binding

If a type doesn't bind required associated types:

```maxon
typealias Score = int(i64.min to i64.max)

interface HasElement uses Element
	function get() returns Element
end 'HasElement'

type Broken implements HasElement
	let value as Score

	function get() returns Score
		return value
	end 'get'
end 'Broken'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/docs-example-3.test:8:6: Type 'Broken' does not define required associated type 'Element' from interface 'HasElement'
```

### Error: Partial Implementation

If a type doesn't implement all interface methods:

```maxon
typealias Score = int(i64.min to i64.max)

interface TwoMethods uses Element
	function first() returns Element
	function second() returns Element
end 'TwoMethods'

type Partial implements TwoMethods with Score
	let value as Score

	function first() returns Score
		return value
	end 'first'
end 'Partial'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/docs-example-4.test:9:6: Partial interface implementation: type 'Partial' is missing 1 method(s):
  - second() returns Score
```

### Error: Type Mismatch in Method

If a method's signature doesn't match the resolved associated type:

```maxon
typealias ID = int(i64.min to i64.max)
typealias Weight = float(f64.min to f64.max)

interface Producer uses Output
	function produce() returns Output
end 'Producer'

type WrongReturn implements Producer with Weight
	let value as ID

	function produce() returns ID
		return value
	end 'produce'
end 'WrongReturn'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/docs-example-5.test:9:6: Partial interface implementation: type 'WrongReturn' has 1 method(s) with wrong signature:
  - produce() returns ID (expected produce() returns Weight)
```


## Tests

<!-- disabled-test: basic-associated-type -->
<!-- ASSOCIATED-TYPE SUBSTITUTION AT CONFORMANCE — a rung of its own, and the half D6 was told not to build. shv2 parses `uses` and records the names on `IrInterface.associatedTypeNames`, and D6 made an interface's `uses` its TYPE-PARAMETER SCOPE so a `typealias ElementArray = Array with Element` inside one resolves. What nothing does is BIND them: `implements I with Integer` must substitute the associated type for its argument BEFORE `checkConformance` compares a requirement's rendered signature against the impl's, so today the comparison reads the associated type's own NAME. `associatedTypeNames` has no reader at all — that reader is the rung. MEASURED: `E3016 … 'IntBox' has 1 method(s) with wrong signature: unwrap() returns Integer (expected unwrap() returns Inner)` — `Inner` unbound where `implements Boxed with Integer` binds it. -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Wrapper uses Inner
	function unwrap() returns Inner
end 'Wrapper'

type IntBox implements Wrapper with Integer
	let value as Integer

	function unwrap() returns Integer
		return value
	end 'unwrap'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'IntBox'

function main() returns ExitCode
	let box = IntBox.create(42)
	return box.unwrap()
end 'main'
```
```exitcode
42
```


<!-- disabled-test: associated-type-in-param -->
<!-- ASSOCIATED-TYPE SUBSTITUTION AT CONFORMANCE — a rung of its own, and the half D6 was told not to build. shv2 parses `uses` and records the names on `IrInterface.associatedTypeNames`, and D6 made an interface's `uses` its TYPE-PARAMETER SCOPE so a `typealias ElementArray = Array with Element` inside one resolves. What nothing does is BIND them: `implements I with Integer` must substitute the associated type for its argument BEFORE `checkConformance` compares a requirement's rendered signature against the impl's, so today the comparison reads the associated type's own NAME. `associatedTypeNames` has no reader at all — that reader is the rung. MEASURED: `E3016 … 'IntSum' has 1 method(s) with wrong signature: add(item Integer) returns IntSum (expected add(item Item) returns Self)` — the PARAMETER position, same unbound name. -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Accumulator uses Item
	function add(item Item) returns Self
	function total() returns Integer
end 'Accumulator'

type IntSum implements Accumulator with Integer
	let sum as Integer

	function add(item Integer) returns IntSum
		return IntSum{sum: sum + item}
	end 'add'

	function total() returns Integer
		return sum
	end 'total'

	static function create(sum Integer) returns Self
		return Self{sum: sum}
	end 'create'
end 'IntSum'

function main() returns ExitCode
	var acc = IntSum.create(0)
	acc = acc.add(10)
	acc = acc.add(32)
	return acc.total()
end 'main'
```
```exitcode
42
```


<!-- disabled-test: multiple-associated-types -->
<!-- ASSOCIATED-TYPE SUBSTITUTION AT CONFORMANCE — a rung of its own, and the half D6 was told not to build. shv2 parses `uses` and records the names on `IrInterface.associatedTypeNames`, and D6 made an interface's `uses` its TYPE-PARAMETER SCOPE so a `typealias ElementArray = Array with Element` inside one resolves. What nothing does is BIND them: `implements I with Integer` must substitute the associated type for its argument BEFORE `checkConformance` compares a requirement's rendered signature against the impl's, so today the comparison reads the associated type's own NAME. `associatedTypeNames` has no reader at all — that reader is the rung. MEASURED: `E3016 … 'IntFloat' has 2 method(s) with wrong signature: getFirst() returns Integer (expected getFirst() returns First)` and the same for `Second`. ⚠ AND A SECOND, INDEPENDENT DIVERGENCE IN THE SAME CASE, which the substitution rung must fix too: this is the only case here whose `with` clause carries TWO arguments UNPARENTHESIZED (`implements Pair with Integer, Float`), and `skipOptionalWithClause`'s unparenthesized arm consumes exactly ONE type reference — so `parseTypeImplementsClause`'s comma loop reads `, Float` as a SECOND INTERFACE and adds a spurious `E3015: type 'IntFloat' implements unknown interface 'Float'`. How many arguments the clause takes is the interface's `uses` ARITY, which is precisely the fact binding needs. The oracle accepts the program (build 0, run 42 — measured). -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Pair uses First, Second
	function getFirst() returns First
	function getSecond() returns Second
end 'Pair'

type IntFloat implements Pair with Integer, Float
	let a as Integer
	let b as Float

	function getFirst() returns Integer
		return a
	end 'getFirst'

	function getSecond() returns Float
		return b
	end 'getSecond'

	static function create(a Integer, b Float) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'IntFloat'

function main() returns ExitCode
	let p = IntFloat.create(40, b: 2.5)
	let x = p.getFirst()
	let y = trunc(p.getSecond())
	return x + y
end 'main'
```
```exitcode
42
```


<!-- disabled-test: character-element-type -->
<!-- TWO blockers, and the FIRST one is NOT the associated-type gap. MEASURED: `E2015 … Character method 'codepoints' — shv2 provides `bytes` and `byteLength` …`, raised in `main` before conformance is ever checked. Past that it needs the substitution rung as well, its `implements CharSource with Character` binding `Element := Character` exactly as the cases above do. Both, in that order. -->
```maxon
// character is a grapheme cluster type, use codepoints() to access codepoint values
interface CharSource uses Element
	function getChar() returns Element
end 'CharSource'

type SingleChar implements CharSource with Character
	let ch as Character

	function getChar() returns Character
		return ch
	end 'getChar'

	static function create(ch Character) returns Self
		return Self{ch: ch}
	end 'create'
end 'SingleChar'

function main() returns ExitCode
	let s = SingleChar.create('A')
	let c = s.getChar()
	for cp in c.codepoints() 'loop'
		return cp
	end 'loop'
	return 0
end 'main'
```
```exitcode
65
```


<!-- disabled-test: byte-element-type -->
<!-- ASSOCIATED-TYPE SUBSTITUTION AT CONFORMANCE — a rung of its own, and the half D6 was told not to build. shv2 parses `uses` and records the names on `IrInterface.associatedTypeNames`, and D6 made an interface's `uses` its TYPE-PARAMETER SCOPE so a `typealias ElementArray = Array with Element` inside one resolves. What nothing does is BIND them: `implements I with Integer` must substitute the associated type for its argument BEFORE `checkConformance` compares a requirement's rendered signature against the impl's, so today the comparison reads the associated type's own NAME. `associatedTypeNames` has no reader at all — that reader is the rung. MEASURED: `E3016 … 'SingleByte' has 1 method(s) with wrong signature: getByte() returns Byte (expected getByte() returns Element)` — the argument here is a RANGED alias, which changes nothing: the requirement still reads `Element`. -->
```maxon

typealias Byte = int(0 to u8.max)

interface ByteSource uses Element
	function getByte() returns Element
end 'ByteSource'

type SingleByte implements ByteSource with Byte
	let b as Byte

	function getByte() returns Byte
		return b
	end 'getByte'

	static function create(b Byte) returns Self
		return Self{b: b}
	end 'create'
end 'SingleByte'

function main() returns ExitCode
	let s = SingleByte.create(42 as Byte)
	let b = s.getByte()
	return b
end 'main'
```
```exitcode
42
```


<!-- disabled-test: missing-type-binding-error -->
<!-- ASSOCIATED-TYPE SUBSTITUTION AT CONFORMANCE — a rung of its own, and the half D6 was told not to build. shv2 parses `uses` and records the names on `IrInterface.associatedTypeNames`, and D6 made an interface's `uses` its TYPE-PARAMETER SCOPE so a `typealias ElementArray = Array with Element` inside one resolves. What nothing does is BIND them: `implements I with Integer` must substitute the associated type for its argument BEFORE `checkConformance` compares a requirement's rendered signature against the impl's, so today the comparison reads the associated type's own NAME. `associatedTypeNames` has no reader at all — that reader is the rung. This case wants the substitution rung's OWN DIAGNOSTIC, not just a substituted message: expected `E3016 … Type 'Missing' does not define required associated type 'Element' from interface 'NeedsElement'`, a check for an `implements` that binds nothing, which shv2 does not have. MEASURED instead: `E3016 … 'Missing' has 1 method(s) with wrong signature: get() returns Integer (expected get() returns Element)` — the same fact reported as a signature mismatch, which is the consequence rather than the diagnosis. -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface NeedsElement uses Element
	function get() returns Element
end 'NeedsElement'

type Missing implements NeedsElement
	let value as Integer

	function get() returns Integer
		return value
	end 'get'
end 'Missing'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/missing-type-binding-error.test:9:6: Type 'Missing' does not define required associated type 'Element' from interface 'NeedsElement'
```


<!-- disabled-test: partial-implementation-error -->
<!-- ASSOCIATED-TYPE SUBSTITUTION AT CONFORMANCE — a rung of its own, and the half D6 was told not to build. shv2 parses `uses` and records the names on `IrInterface.associatedTypeNames`, and D6 made an interface's `uses` its TYPE-PARAMETER SCOPE so a `typealias ElementArray = Array with Element` inside one resolves. What nothing does is BIND them: `implements I with Integer` must substitute the associated type for its argument BEFORE `checkConformance` compares a requirement's rendered signature against the impl's, so today the comparison reads the associated type's own NAME. `associatedTypeNames` has no reader at all — that reader is the rung. The missing-method LIST is right and only its TEXT is unsubstituted — expected `- second() returns Integer`, MEASURED `- second() returns Element`. The case is red on the message alone, so it is the cheapest proof that substitution reaches the DIAGNOSTIC and not only the comparison. -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface TwoMethods uses Element
	function first() returns Element
	function second() returns Element
end 'TwoMethods'

type Partial implements TwoMethods with Integer
	let value as Integer

	function first() returns Integer
		return value
	end 'first'
end 'Partial'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/partial-implementation-error.test:10:6: Partial interface implementation: type 'Partial' is missing 1 method(s):
  - second() returns Integer
```


<!-- disabled-test: wrong-return-type-error -->
<!-- ASSOCIATED-TYPE SUBSTITUTION AT CONFORMANCE — a rung of its own, and the half D6 was told not to build. shv2 parses `uses` and records the names on `IrInterface.associatedTypeNames`, and D6 made an interface's `uses` its TYPE-PARAMETER SCOPE so a `typealias ElementArray = Array with Element` inside one resolves. What nothing does is BIND them: `implements I with Integer` must substitute the associated type for its argument BEFORE `checkConformance` compares a requirement's rendered signature against the impl's, so today the comparison reads the associated type's own NAME. `associatedTypeNames` has no reader at all — that reader is the rung. Expected `(expected make() returns Float)`, MEASURED `(expected make() returns Output)`. The REJECTION is correct today and only the name it prints is unbound — so substitution must not be mistaken for something that merely relaxes the check. -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Typed uses Output
	function make() returns Output
end 'Typed'

type WrongType implements Typed with Float
	let value as Integer

	function make() returns Integer
		return value
	end 'make'
end 'WrongType'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/wrong-return-type-error.test:10:6: Partial interface implementation: type 'WrongType' has 1 method(s) with wrong signature:
  - make() returns Integer (expected make() returns Float)
```


<!-- disabled-test: wrong-param-type-error -->
<!-- ASSOCIATED-TYPE SUBSTITUTION AT CONFORMANCE — a rung of its own, and the half D6 was told not to build. shv2 parses `uses` and records the names on `IrInterface.associatedTypeNames`, and D6 made an interface's `uses` its TYPE-PARAMETER SCOPE so a `typealias ElementArray = Array with Element` inside one resolves. What nothing does is BIND them: `implements I with Integer` must substitute the associated type for its argument BEFORE `checkConformance` compares a requirement's rendered signature against the impl's, so today the comparison reads the associated type's own NAME. `associatedTypeNames` has no reader at all — that reader is the rung. Expected `(expected accept(val Float) returns Integer)`, MEASURED `(expected accept(val Input) returns Integer)`. The parameter-position twin of the case above. -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Acceptor uses Input
	function accept(val Input) returns Integer
end 'Acceptor'

type WrongParam implements Acceptor with Float
	let value as Integer

	function accept(val Integer) returns Integer
		return value + val
	end 'accept'
end 'WrongParam'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/wrong-param-type-error.test:10:6: Partial interface implementation: type 'WrongParam' has 1 method(s) with wrong signature:
  - accept(val Integer) returns Integer (expected accept(val Float) returns Integer)
```


<!-- test: implicit-self-field-access -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Countable
	function getCount() returns Integer
end 'Countable'

type Counter implements Countable
	let count as Integer

	function getCount() returns Integer
		return count
	end 'getCount'

	static function create(count Integer) returns Self
		return Self{count: count}
	end 'create'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(42)
	return c.getCount()
end 'main'
```
```exitcode
42
```


<!-- test: method-call-syntax -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Addable
	function addOne() returns Integer
end 'Addable'

type Number implements Addable
	let value as Integer

	function addOne() returns Integer
		return value + 1
	end 'addOne'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Number'

function main() returns ExitCode
	let n = Number.create(41)
	return n.addOne()
end 'main'
```
```exitcode
42
```


