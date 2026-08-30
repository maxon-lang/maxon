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

<!-- test: basic-associated-type -->
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


<!-- test: associated-type-in-param -->
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


<!-- test: multiple-associated-types -->
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


<!-- test: character-element-type -->
BOTH of the blockers this case was disabled for are gone, and it was disabled for one of them long after
that one had been closed. MEASURED at W60 on the untouched compiler: it compiles, runs, and exits **65**.
The `Character` half (`E2015 … member 'codepoints'`) closed with the `Character` slice; the
"substitution rung" half was never a blocker at all — `CharSource` declares no `extends`, so
`implements CharSource with Character` is the ordinary root-entry binding every case above it already used.
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


<!-- test: byte-element-type -->
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


<!-- test: missing-type-binding-error -->
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


<!-- test: partial-implementation-error -->
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


<!-- test: wrong-return-type-error -->
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


<!-- test: wrong-param-type-error -->
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


### shv2 regression cases

(A `###` and not a `##`: the active-test region runs from `## Tests` to the NEXT `## ` heading, so a
second-level heading here would shelve every case below it — see `Testing/SpecParser.maxon`.)

The cases above are the canonical `/specs/associated-types.md` corpus, byte-identical. The four below are
shv2's own, found by probing the substitution rung (R5) for false rejects and false accepts. Each names the
mechanism it pins.

### A conformance may bind an associated type to the CONFORMER'S OWN type parameter

`type ArrayIterator uses Element implements BidirectionalIterator with Element` is how every generic
container in `stdlib/` declares its conformance (`Array.maxon:490`, `List.maxon:158`, `Map.maxon:343`,
`Set.maxon:356`), so this is the shape the feature exists for and not a corner.

It was a wrong REJECTION until R5, on the IMPL side rather than the interface side: an interface requirement
already spelled a type parameter by its declared name, but the impl method's types went through
`maxonTypeName`, whose fallback for a `typeParameter` is the bare tag word — so the comparison read
`held() returns type parameter` against `held() returns T` and could never agree, whatever the program said.
Measured before the fix: `E3016 … held() returns type parameter (expected held() returns T)`; the bootstrap
compiles and runs the same program (exit 42).

<!-- test: conformance-argument-is-the-conformers-own-type-parameter -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Holder uses Element
	function held() returns Element
end 'Holder'

type Box uses T implements Holder with T
	let value as T

	function held() returns T
		return value
	end 'held'

	static function create(value T) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	let b = IntBox.create(42)
	return b.held()
end 'main'
```
```exitcode
42
```


### …and the WRONG type parameter is still a mismatch, named

The other half of the same rendering fix, and the reason it is a fix and not a relaxation: with every
parameter rendered as one string, a diagnostic could not tell `T` from `U`. Here the conformance binds
`Element := T` and the method returns `U`.

<!-- test: conformance-argument-type-parameter-mismatch -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Holder uses Element
	function held() returns Element
end 'Holder'

type Pair uses T, U implements Holder with T
	let first as T
	let second as U

	function held() returns U
		return second
	end 'held'

	static function create(first T, second U) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'Pair'

typealias IntPair = Pair with (Integer, Integer)

function main() returns ExitCode
	let p = IntPair.create(1, second: 2)
	return p.held()
end 'main'
```
```maxoncstderr
error E3016: <fragment>:9:6: Partial interface implementation: type 'Pair' has 1 method(s) with wrong signature:
  - held() returns U (expected held() returns T)
```


### An `implements` list mixes bound and unbound interfaces, and the comma belongs to whichever takes it

R5 made an unparenthesized `with` read as many arguments as the interface has `uses` names, so the comma
after an argument is ambiguous between "another argument" and "another interface" and the ARITY is what
settles it. `Alpha` takes one, so the comma after `Integer` opens a sibling conformance;
`stdlib/List.maxon:12` is written in exactly this shape. A greedy reader eats `Beta` as an argument.

<!-- test: mixed-bound-and-unbound-interfaces -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Alpha uses A
	function one() returns A
end 'Alpha'

interface Beta uses B
	function two() returns B
end 'Beta'

interface Plain
	function three() returns Integer
end 'Plain'

type Multi implements Alpha with Integer, Beta with Float, Plain
	let n as Integer

	function one() returns Integer
		return n
	end 'one'

	function two() returns Float
		return 1.0
	end 'two'

	function three() returns Integer
		return 42
	end 'three'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Multi'

function main() returns ExitCode
	let m = Multi.create(7)
	return m.one() + m.three()
end 'main'
```
```exitcode
49
```


### The arity is a WHOLE-PROGRAM fact, so an interface declared BELOW its conformance still supplies it

R5 read the arity off the file's own `artifact.interfaces`, which the linear parse fills as it REACHES each
declaration — so an interface written below the type that conforms to it was invisible, the arity defaulted
to 1, and `implements Duo with Score, Weight` truncated after `Score`. MEASURED before R7:
`E3016 … type 'Both' does not define required associated type 'Q'` plus
`E3015 … implements unknown interface 'Weight'`, on a program the bootstrap compiles and runs (exit 42).
The arity now comes from the whole-program declaration sweep, which visits every file's `interface`
declarations before any file is parsed — the same guarantee `type` and `enum` have had since P1.1.

<!-- test: interface-declared-below-its-conformance -->
```maxon

typealias Score = int(i64.min to i64.max)
typealias Weight = float(f64.min to f64.max)

type Both implements Duo with Score, Weight
	let a as Score
	let b as Weight

	function getFirst() returns Score
		return a
	end 'getFirst'

	function getSecond() returns Weight
		return b
	end 'getSecond'

	static function create(a Score, b Weight) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Both'

interface Duo uses P, Q
	function getFirst() returns P
	function getSecond() returns Q
end 'Duo'

function main() returns ExitCode
	let v = Both.create(42, b: 1.5)
	return v.getFirst()
end 'main'
```
```exitcode
42
```


### …and in ANOTHER file, which is the half a same-file rule could never reach

The cross-file case is the one the old per-file lookup could not answer even in principle: a file's parse
sees only its own declarations. It is the same defect and the same fix, and it is spelled separately
because "declared lower in this file" and "declared in a file swept later" are two different reasons the
old lookup missed, and only one of them a re-ordering of declarations could have hidden.

<!-- test: interface-declared-in-a-later-file -->
```maxon
// --- file: a.maxon
typealias Score = int(i64.min to i64.max)
typealias Weight = float(f64.min to f64.max)

type Both implements Duo with Score, Weight
	let a as Score
	let b as Weight

	function getFirst() returns Score
		return a
	end 'getFirst'

	function getSecond() returns Weight
		return b
	end 'getSecond'

	static function create(a Score, b Weight) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Both'

function main() returns ExitCode
	let v = Both.create(42, b: 1.5)
	return v.getFirst()
end 'main'

// --- file: b.maxon
export interface Duo uses P, Q
	function getFirst() returns P
	function getSecond() returns Q
end 'Duo'
```
```exitcode
42
```


### ⚠ THE OVER-FIX GUARD: a FORWARD interface's arity must STOP the comma loop, not merely start it

`mixed-bound-and-unbound-interfaces` with every interface moved BELOW the type. `Alpha` takes ONE
associated type, so the comma after `Integer` opens a SIBLING conformance — and now that the arity is
resolvable for a forward interface, a reader that consulted it and then over-consumed would eat `Beta` as
`Alpha`'s second argument. This case was green before R7 for the wrong reason (the unresolvable default
happened to be 1) and must stay green for the right one.

<!-- test: forward-interface-comma-still-opens-a-sibling-conformance -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

type Multi implements Alpha with Integer, Beta with Float, Plain
	let n as Integer

	function one() returns Integer
		return n
	end 'one'

	function two() returns Float
		return 1.0
	end 'two'

	function three() returns Integer
		return 42
	end 'three'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Multi'

interface Alpha uses A
	function one() returns A
end 'Alpha'

interface Beta uses B
	function two() returns B
end 'Beta'

interface Plain
	function three() returns Integer
end 'Plain'

function main() returns ExitCode
	let m = Multi.create(7)
	return m.one() + m.three()
end 'main'
```
```exitcode
49
```


### A forward interface with NO `uses` clause does not swallow the conformance that follows it

`Plain` declares no associated type, so its surplus `with Score` binds nothing (the rule the case below
states) and the comma after it opens the SIBLING `Duo` — whose own two `uses` names then take BOTH of the
remaining arguments. Every interface here is declared below the type, so the arity of each comes from the
whole-program sweep. It fails before R7 the way the two cases above do — `Duo` truncates at `Score` and
`Weight` is read as an interface — so what it pins is that a FORWARD `Plain` still stops its own comma
loop while a FORWARD `Duo` continues one, in a single `implements` list.

⚠ **IT DOES NOT PIN ARITY 0 AS DISTINCT FROM THE UNRESOLVABLE DEFAULT OF 1, AND NO CASE CAN — MEASURED
(R7 review).** `parseConformanceWithArgs` consults the arity only once a first argument is already read
and its loop is `while args.count() < arity`, so 0 and 1 stop at exactly the same token through the sole
reader that exists. Collapsing `DeclaredInterfaceArity.declared(0)` into that default and rebuilding
leaves the WHOLE suite green at 2812/0 — so the 0-vs-undeclared split in `SignatureIndex.maxon` is
correct modelling that today's corpus cannot exercise, and a future edit that breaks it will be caught by
nothing here. Do not read this case as its guard.

<!-- test: forward-interface-with-no-uses-clause -->
```maxon

typealias Score = int(i64.min to i64.max)
typealias Weight = float(f64.min to f64.max)

type Both implements Plain with Score, Duo with Score, Weight
	let a as Score
	let b as Weight

	function getFirst() returns Score
		return a
	end 'getFirst'

	function getSecond() returns Weight
		return b
	end 'getSecond'

	function plain() returns Score
		return a
	end 'plain'

	static function create(a Score, b Weight) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Both'

interface Plain
	function plain() returns Score
end 'Plain'

interface Duo uses P, Q
	function getFirst() returns P
	function getSecond() returns Q
end 'Duo'

function main() returns ExitCode
	let v = Both.create(42, b: 1.5)
	return v.getFirst()
end 'main'
```
```exitcode
42
```


### An UNPARENTHESIZED `with` argument an interface declares no `uses` name for is IGNORED, not rejected

The two reference compilers disagree here — the bootstrap binds `min(names, args)` and drops the rest, v1
rejects the arity — and shv2 takes the bootstrap's answer *for this spelling*. See
`ConformanceCheck.checkOneInterfaceConformance` for the argument in full; the short form is that without
parentheses the list's LENGTH is not something the author asserted — it is decided by the interface's `uses`
arity, and a trailing comma legitimately belongs to the outer `implements` list.

⚖ The PARENTHESIZED spelling of the same surplus is refused (R6) — see the case below, which is this exact
program with two characters added.

<!-- test: surplus-conformance-argument-is-ignored -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface NoUses
	function value() returns Integer
end 'NoUses'

type Odd implements NoUses with Integer
	let n as Integer

	function value() returns Integer
		return n
	end 'value'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Odd'

function main() returns ExitCode
	let o = Odd.create(42)
	return o.value()
end 'main'
```
```exitcode
42
```


### A requirement typed by a GENERIC INSTANCE is checked by the instance's identity, not by its kind

The third arm of the shared renderer (`IrInterface.renderDeclaredTypeName`), and the one that was a false
ACCEPT rather than a false reject. `maxonTypeName` spells a `genericInstance` with the bare kind word
`struct`, so `Array with Integer` and `Array with String` rendered to the same string and satisfied each
other. MEASURED before the fix: shv2 COMPILED AND RAN this program (exit 42) where the bootstrap reports
E3016 on it. Its identity is `ProgramSignatures.canonicalInstanceName` — the compiler's own answer to
"are these two the same type?", which every other comparison site already asks.

<!-- test: conformance-requirement-typed-by-a-generic-instance -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias StrArray = Array with String

interface Bag
	function take(xs IntArray) returns Integer
end 'Bag'

type Sack implements Bag
	let n as Integer

	function take(xs StrArray) returns Integer
		return n + xs.count()
	end 'take'

	static function create() returns Self
		return Self{n: 41}
	end 'create'
end 'Sack'

function main() returns ExitCode
	let s = Sack.create()
	var f = StrArray.create()
	f.push("a")
	return s.take(f)
end 'main'
```
```maxoncstderr
error E3016: <fragment>:11:6: Partial interface implementation: type 'Sack' has 1 method(s) with wrong signature:
  - take(xs Array_String) returns Integer (expected take(xs Array_Integer) returns Integer)
```


### …and TWO ALIAS SPELLINGS of ONE instance still conform, which is why the identity is the canonical name

The negative control for the case above, and the reason the fix is the canonical instance name rather than
a refusal to compare instances at all: `IntArray` and `AlsoIntArray` are two names for one type, and a
comparison that read either alias's own spelling would REJECT this program. `canonicalInstanceName` collapses
both to one string, exactly as it does for every other comparison site in the compiler.

<!-- test: two-alias-spellings-of-one-generic-instance-conform -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias AlsoIntArray = Array with Integer

interface Bag
	function take(xs IntArray) returns Integer
end 'Bag'

type Sack implements Bag
	let n as Integer

	function take(xs AlsoIntArray) returns Integer
		return n + xs.count()
	end 'take'

	static function create() returns Self
		return Self{n: 41}
	end 'create'
end 'Sack'

function main() returns ExitCode
	let s = Sack.create()
	var f = IntArray.create()
	f.push(7)
	return s.take(f)
end 'main'
```
```exitcode
42
```


### ⚖ …but a surplus INSIDE PARENTHESES is REFUSED, and the asymmetry is the whole rule (R6)

The two spellings are not the same claim. **Parentheses make the list explicit, so its LENGTH is
something the author asserted and can be wrong about.** Without them the length is decided by the
interface's `uses` arity and a trailing comma legitimately belongs to the outer `implements` list —
which is why both references consume exactly `arity` items there, and why the case above pins the
unparenthesized surplus as ignored. So this refusal diverges from nothing that case pinned.

Before it, `implements One with (Integer, Float)` against `interface One uses A` bound `A := Integer`,
dropped `Float` and COMPILED (measured, exit 42): a typo nothing reported.

### ⭐⭐ AT ARITY ONE THE REFUSAL IS GONE, AND `with (A, B)` IS A TUPLE ARGUMENT (W43)

R6's own text below named the day this would come: *"It is also the spelling
`stdlib/helpers/itertools/withIterator.maxon` needs the day it is loadable: that file writes
`implements Iterator with (Source, Element)` against a one-`uses` `Iterator`, which under shv2's settled
LIST reading is exactly the surplus this rule refuses."* That file also declares
`current() returns (Source, Element)`, so **no list reading can make it conform** — the LIST reading was
not a stricter shv2 rule here, it was a rejection of a program the corpus writes and both references
compile. **BOTH references arity-discriminate**: the bootstrap outright (*"when expecting a single type
arg, let ParseTypeRef handle it — (A, B) is a tuple type, not two separate type arguments"*,
`2-Parser.cs:3129-3137`), v1 by recording `parenForm` so `TypeResolution` can *"collapse a parenthesized
multi-arg list against a single-`uses` interface into one `__TupleN`"* (`Parser.maxon:1944-1949`).

⚠ **THE TUPLE READING NEEDS A TOP-LEVEL COMMA, WHICH IS WHAT KEEPS THE TWO SPELLINGS R6 PINNED ALIVE** —
`with (T)` holds none and stays one ordinary binding (the case further down pins it), and `with ((A, B))`
holds its comma one level DOWN and stays one binding whose single item is the tuple (pinned too). Only
`with (A, B)` moves, and only at arity one. **E2066 still fires at every other arity** — the three cases
below it are unchanged and green.

⚠ **AND THE TYPO R6 EXISTED TO CATCH IS STILL CAUGHT**, by the check that was always going to have the
last word about it: `A` is now the tuple, so a `get()` that returns `Integer` disagrees with the
requirement. The verdict is unchanged and the sentence is better — it names both types rather than a
count.

<!-- test: error.surplus-parenthesized-conformance-argument -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface One uses A
	function get() returns A
end 'One'

type Holder implements One with (Integer, Float)
	let v as Integer

	function get() returns Integer
		return v
	end 'get'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(42)
	return h.get()
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/error.surplus-parenthesized-conformance-argument.test:10:6: Partial interface implementation: type 'Holder' has 1 method(s) with wrong signature:
  - get() returns Integer (expected get() returns __Tuple2.int.float)
```


### The SAME program as `surplus-conformance-argument-is-ignored`, written with parentheses

The sharpest statement of the asymmetry: an interface with NO `uses` clause at all. `with Integer` is
ignored (the case above, unchanged); `with (Integer)` is refused. Nothing but the parentheses differs,
and the parentheses are exactly what makes the count an assertion.

<!-- test: error.surplus-parenthesized-argument-against-no-uses -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface NoUses
	function value() returns Integer
end 'NoUses'

type Odd implements NoUses with (Integer)
	let n as Integer

	function value() returns Integer
		return n
	end 'value'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Odd'

function main() returns ExitCode
	let o = Odd.create(42)
	return o.value()
end 'main'
```
```maxoncstderr
error E2066: specs/fragments/associated-types/error.surplus-parenthesized-argument-against-no-uses.test:9:33: interface 'NoUses' declares 0 associated type(s), but this parenthesized 'with' clause binds 1
```


### ⚠ THE FALSE-REJECT GUARD: an interface name that resolves to NOTHING has no arity to be surplus of

The hazard a new refusal carries is that it fires one nesting level below where it was tested. An
interface no file declares is the sharpest instance: the arity door answers `unresolvable`, and a
count-shaped default (there is one, for the unparenthesized arm — `UnresolvableInterfaceUsesArity`)
would turn every misspelled interface with a two-argument `with (…)` into an arity complaint about an
interface the compiler never found. The program's real error is that the name resolves to nothing, and
that is the only sentence it earns.

<!-- test: error.surplus-parenthesized-argument-on-unresolvable-interface -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface One uses A
	function get() returns A
end 'One'

type Holder implements Onee with (Integer, Float)
	let v as Integer

	function get() returns Integer
		return v
	end 'get'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(42)
	return h.get()
end 'main'
```
```maxoncstderr
error E3015: specs/fragments/associated-types/error.surplus-parenthesized-argument-on-unresolvable-interface.test:10:6: type 'Holder' implements unknown interface 'Onee'
```


### The three shapes a `with` clause gets RIGHT, one case each — the guards the refusal must not touch

A rejection rule is worth nothing if it also rejects the programs it was supposed to leave alone, so
each legal shape is pinned on its own rather than left to be inferred from a suite that happens to be
green. `with (A)` ≡ `with A` is prose elsewhere (`specs-shv2/interfaces.md`); here it is two cases that
differ in nothing but the parentheses.

<!-- test: conformance-argument-in-parentheses-at-arity-one -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface One uses A
	function get() returns A
end 'One'

type Holder implements One with (Integer)
	let v as Integer

	function get() returns Integer
		return v
	end 'get'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(42)
	return h.get()
end 'main'
```
```exitcode
42
```


<!-- test: conformance-argument-unparenthesized-at-arity-one -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface One uses A
	function get() returns A
end 'One'

type Holder implements One with Integer
	let v as Integer

	function get() returns Integer
		return v
	end 'get'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(42)
	return h.get()
end 'main'
```
```exitcode
42
```


<!-- test: conformance-arguments-in-parentheses-at-arity-two -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Two uses A, B
	function first() returns A
	function second() returns B
end 'Two'

type Pair implements Two with (Integer, Float)
	let a as Integer
	let b as Float

	function first() returns Integer
		return a
	end 'first'

	function second() returns Float
		return b
	end 'second'

	static function create(a Integer, b Float) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(42, b: 1.5)
	return p.first()
end 'main'
```
```exitcode
42
```


### ⚠ THE NESTING LEVEL BELOW: the refusal must not touch a GENERIC conformer's own type parameter

`conformance-argument-is-the-conformers-own-type-parameter`, written with parentheses. It is spelled
separately because the two arms read their arguments through the same `readConformanceWithArg` but only
one of them now COUNTS them, and a rejection rule's false rejects hide one nesting level below where it
was tested — a bare `Integer` inside the parentheses exercises the count against a concrete type, and a
`T` exercises it against a name that only exists inside this declaration's own `uses` list.

<!-- test: conformance-argument-in-parentheses-is-the-conformers-own-type-parameter -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Holder uses Element
	function held() returns Element
end 'Holder'

type Box uses T implements Holder with (T)
	let value as T

	function held() returns T
		return value
	end 'held'

	static function create(value T) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	let b = IntBox.create(42)
	return b.held()
end 'main'
```
```exitcode
42
```


### ⭐ A TUPLE is still bindable at arity ONE, and this case is what keeps the refusal a TYPO CHECK

The outer parentheses open the LIST; a `(…)` INSIDE one of its items is an ordinary tuple TYPE
reference, so `with ((Integer, Float))` binds ONE argument and is accepted where `with (Integer, Float)`
is refused. That distinction is the entire reason E2066 removes no capability: an author who genuinely
means "bind this one associated type to a pair" has a spelling, and the refused program is the one that
meant two bindings and had one to give.

⚠ It is pinned because nothing else in the suite would notice it going away. The count comes off
`args.count()`, so a reader that flattened the parenthesized arm — or that counted commas instead of
items — would turn this legal program into an arity complaint while every other case stayed green.
It is also the spelling `stdlib/helpers/itertools/withIterator.maxon` needs the day it is loadable:
that file writes `implements Iterator with (Source, Element)` against a one-`uses` `Iterator`, which
under shv2's settled LIST reading is exactly the surplus this rule refuses.

<!-- test: conformance-argument-in-parentheses-is-a-tuple-type -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface One uses A
	function get() returns A
end 'One'

type Holder implements One with ((Integer, Float))
	let v as Integer
	let f as Float

	function get() returns (Integer, Float)
		return (v, f)
	end 'get'

	static function create(v Integer, f Float) returns Self
		return Self{v: v, f: f}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(42, f: 1.5)
	let t = h.get()
	return t.0
end 'main'
```
```exitcode
42
```


### ⚖ An `extends`-INHERITED `uses` name does NOT count toward the arity, and the sentence changes here

`conformanceUsesArity` counts the interface's OWN `uses` names and not its `extends`-inherited ones.
That used to be an incrementality constraint as well as a scope one — `associatedTypeNames.count()` rides
the signature index's hash while `extendsInterfaces` did not, so walking the chain would have made a
parse's answer depend on an unhashed fact. **R10c made `extendsInterfaces` ride that hash** (a witness
dispatch now numbers its slot against the interface's transitive requirement list), so the incrementality
half is discharged and what is left is that shv2 does not inherit associated-type BINDINGS at all:
widening the count without also inheriting the binding would accept an argument nothing then substitutes
(the function's header carries the argument in full).

⚠ **This case is here to record that R6 changed the SENTENCE and not the VERDICT, so that the day
inherited bindings land it turns red and forces the decision rather than quietly widening.** shv2 does
not inherit the binding either, so `interface Sub extends Base` — where `Base uses Element` — rejects
`implements Sub with (Integer)` both before and after R6, and the control below is the same program
without the parentheses, still reported by `ConformanceCheck` as a wrong SIGNATURE. Measured: the
bootstrap refuses the parenthesized spelling too (`E2003 Single-element parenthesised type is not
allowed`, its TUPLE reading of `(…)`), and agrees with the control character for character.

<!-- test: error.surplus-parenthesized-argument-against-an-inherited-uses-name -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Base uses Element
	function get() returns Element
end 'Base'

interface Sub extends Base
	function extra() returns Integer
end 'Sub'

type Impl implements Sub with (Integer)
	let v as Integer

	function get() returns Integer
		return v
	end 'get'

	function extra() returns Integer
		return 1
	end 'extra'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Impl'

function main() returns ExitCode
	let i = Impl.create(42)
	return i.get()
end 'main'
```
```maxoncstderr
error E2066: specs/fragments/associated-types/error.surplus-parenthesized-argument-against-an-inherited-uses-name.test:13:31: interface 'Sub' declares 0 associated type(s), but this parenthesized 'with' clause binds 1
```


### …and the control: unparenthesized, the same program is still the SIGNATURE error it always was

The case above with two characters removed. It is what proves R6 turned an existing rejection into a
different rejection rather than an acceptance into a rejection — this sentence is what shv2 said for
BOTH spellings before the rung, and it is what the bootstrap still says for this one.

<!-- test: error.inherited-uses-name-unparenthesized-is-still-a-signature-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Base uses Element
	function get() returns Element
end 'Base'

interface Sub extends Base
	function extra() returns Integer
end 'Sub'

type Impl implements Sub with Integer
	let v as Integer

	function get() returns Integer
		return v
	end 'get'

	function extra() returns Integer
		return 1
	end 'extra'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Impl'

function main() returns ExitCode
	let i = Impl.create(42)
	return i.get()
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/error.inherited-uses-name-unparenthesized-is-still-a-signature-error.test:13:6: Partial interface implementation: type 'Impl' has 1 method(s) with wrong signature:
  - get() returns Integer (expected get() returns Element)
```

<!-- test: associated-types.uses-name-shadowing-a-builtin -->
⭐⭐ **A `uses` PARAMETER IS AN IDENTIFIER, AND IDENTIFIERS CAN SPELL BUILTINS.** `float`, `bool` and
`int` are KEYWORDS a `uses` list cannot hold (E2010), but `ExitCode` and `String` are not — so
`interface Taker uses ExitCode` is a legal program in which `ExitCode` is a TYPE PARAMETER denoting
whatever each conformer binds, here `Integer`. A witness call's argument ABI must therefore ask
"is this an associated type?" BEFORE it asks "is this a builtin type name?".
**MEASURED RED: asking the builtin table first read the formal as the `u32` builtin and declared the
argument a wasm i32 against an i64 callee — x64 answered 31 and wasm trapped `indirect call type
mismatch`.** `TypeResolution.builtinTypeNameTag`'s own header states the rule that violated: it is
valid only at a door holding a RENDERED type name, never at one asking what a `named` reference
DENOTES. Returns `31`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses ExitCode
	function take(e ExitCode) returns Integer
end 'Taker'

type Runner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

function useIt(x Taker) returns Integer
	return x.take(11)
end 'useIt'

function main() returns ExitCode
	return useIt(Runner.create(20)) as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: associated-types.assoc-bound-to-exitcode-through-type-parameter -->
An associated type bound to `ExitCode` — the one narrow (`u32`) type a Maxon signature can name — as a
requirement's PARAMETER, dispatched through a constrained type parameter. The formal's own spelling
(`Element`) says nothing about its width; `implements Taker with ExitCode` is what makes the argument a
wasm `i32`, so the call site has to resolve the BINDING to declare the same functype the impl does.
**MEASURED RED: `wasm trap: indirect call type mismatch`, x64 `31`.** Returns `31`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type Runner implements Taker with ExitCode
	let base as Integer

	function take(e ExitCode) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T where T is Taker
	let item as T

	export function run(c ExitCode) returns Integer
		return self.item.take(c)
	end 'run'

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias RunnerBox = Box with Runner

function main() returns ExitCode
	return RunnerBox.create(Runner.create(20)).run(11 as ExitCode) as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: associated-types.assoc-bound-to-exitcode-through-existential -->
The EXISTENTIAL twin of the case above: one dispatch mechanism, two receiver kinds, and the binding has
to be resolved on both. **MEASURED RED: wasm trapped, x64 `31`.** Returns `31`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type Runner implements Taker with ExitCode
	let base as Integer

	function take(e ExitCode) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

function useIt(t Taker, c ExitCode) returns Integer
	return t.take(c)
end 'useIt'

function main() returns ExitCode
	return useIt(Runner.create(20), c: 11 as ExitCode) as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: associated-types.assoc-bound-to-float-takes-an-int-actual -->
An associated type bound to `float`, given an INTEGER actual. The requirement spells `Element`, so the
parser's own float widening — which keys on a formal SPELLED `float` — cannot see that this argument
must become an f64; the lowering resolves the binding and widens there, through the same
`widenIntArgsToFloatParams` a function-value call uses. **MEASURED RED, and note WHICH half was worse:
x64-windows compiled clean and answered `20` where the program computes `40` — a SILENT WRONG ANSWER,
the integer `20` handed to a callee that compares `e > 19.0` — while wasm trapped.** Returns `40`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type Runner implements Taker with float
	let base as Integer

	function take(e Real) returns Integer
		return base + (20 if e > 19.0 else 0)
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T where T is Taker
	let item as T

	export function run() returns Integer
		return self.item.take(20)
	end 'run'

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias RunnerBox = Box with Runner

function main() returns ExitCode
	return RunnerBox.create(Runner.create(20)).run() as ExitCode
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
40
```

<!-- test: associated-types.assoc-bound-to-float-takes-a-float-actual -->
The FALSE-REJECT CONTROL for the two refusals below, and for the widening above: an associated type
bound to `float` in a PARAMETER position, given a float actual, is a correct program and must keep
compiling. A rule that refused associated types in ABI positions outright would take this with it.
Returns `40`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type Runner implements Taker with float
	let base as Integer

	function take(e Real) returns Integer
		return base + (20 if e > 19.0 else 0)
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T where T is Taker
	let item as T

	export function run() returns Integer
		return self.item.take(20.0)
	end 'run'

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias RunnerBox = Box with Runner

function main() returns ExitCode
	return RunnerBox.create(Runner.create(20)).run() as ExitCode
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
40
```

<!-- test: error.two-conformers-binding-one-associated-type-differently -->
⭐⭐ **THIS CASE WAS PINNED AS LEGAL ONE ROUND AGO, AND IT IS A LIVE WRONG ANSWER.** It was the
false-reject control for a rule that compared the ABI CLASS: `Integer` and `Score` are different ranged
aliases and the same machine word, so the two conformers were held to agree. They do not.
**MEASURED with `t.take(5000)` at a call site both conformers reach: `A` answers 5000 and `B`
RANGE-PANICS at run time**, because `Score`'s `int(0 to 100)` is `B`'s declared parameter and the shared
body was compiled against `A`'s. Its sibling is worse — `String` beside `Integer`, also one ABI class,
SEGFAULTED on x64 (exit 139) and silently answered 20 on wasm.
⇒ The line is "two conformers bind a dispatched associated type to different TYPES", not "to different
ABI classes". Under dictionary passing there is no per-conformer specialization at all: the body is
compiled ONCE, against one binding, and every other conformer reinterprets those bits. The narrower
rule was strictly inside where the wrong answers fall.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Score = int(0 to 100)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type A implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'A'

type B implements Taker with Score
	let base as Integer

	function take(e Score) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'B'

function useIt(t Taker) returns Integer
	return t.take(5000)
end 'useIt'

function main() returns ExitCode
	return (useIt(A.create(0)) + useIt(B.create(0))) as ExitCode
end 'main'
```
```maxoncstderr
error E3119: specs/fragments/associated-types/error.two-conformers-binding-one-associated-type-differently.test:21:6: 'B' binds 'Taker's associated type 'Element' to 'Score', but 'A' binds it to 'Integer' — and 'Element' is written as a parameter or return type of a requirement this program DISPATCHES THROUGH A RECEIVER THAT DOES NOT SAY WHICH BINDING IT HOLDS. A witness dispatch is compiled ONCE for every conformer, with no per-conformer specialization, so the shared body would be compiled against one of those two types and hand the other conformer's impl bits it reads as something else. Declare that receiver at 'Taker with <binding>', which settles it for that dispatch; or bind the associated type to the same type in both conformances; or give the two conformers different interfaces
```

<!-- test: associated-types.two-conformers-binding-the-same-type-still-compile -->
The FALSE-REJECT CONTROL for E3119, re-cut after the rule moved: TWO conformers binding one associated
type to the SAME type is exactly what the refusal above must NOT take with it, and it is the only
two-conformer shape a shared body can be compiled for. `(5 + 10) + (6 + 10)`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type A implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'A'

type B implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'B'

function useIt(t Taker) returns Integer
	return t.take(10)
end 'useIt'

function main() returns ExitCode
	return (useIt(A.create(5)) + useIt(B.create(6))) as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: associated-types.conformers-disagree-but-nothing-dispatches -->
⭐⭐ **THE DISPATCH GATE, AND WITHOUT IT E3119 REFUSES A CORRECT PROGRAM.** Two conformers bind
`Element` to `float` and to `Integer` — the disagreement the case two above refuses — but every call
here is statically resolved, so no shared body is compiled against both and no witness table exists to
be wrong about. **MEASURED: this program builds and answers 51 correctly on both targets, and a rule
keyed only on "a requirement WRITES the name in an ABI position" refused it.** shv2 is whole-program,
so which interfaces are actually dispatched is knowable exactly, and the check reads it off the
`witnessDispatch` ops the parser emitted. `(0 + 20) + (11 + 20)`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type FloatRunner implements Taker with float
	let base as Integer

	function take(e Real) returns Integer
		return base + (20 if e > 19.0 else 0)
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'FloatRunner'

type IntRunner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'IntRunner'

function main() returns ExitCode
	return (FloatRunner.create(0).take(20.0) + IntRunner.create(11).take(20)) as ExitCode
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
51
```

<!-- test: error.dispatched-associated-type-bound-to-a-string-beside-an-int -->
⭐⭐ **E3119's SECOND RECORDED FAILURE, PINNED BY A CASE RATHER THAN BY ITS OWN HEADER.** `String`
beside `Integer` is the pair the entry calls the worst of the three: both are one ABI class and one machine
word, so no width rule and no register-file rule can separate them, and the shared body compiled against
`Integer` hands `TextRunner.take` the literal `20` to read as a String pointer. **RE-MEASURED with the
refusal lifted, x64-windows: SEGFAULT, exit 139.** The refusal is the only thing between this program and
that fault, and the case that would have caught the refusal being lifted did not exist.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type IntRunner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'IntRunner'

type TextRunner implements Taker with String
	let base as Integer

	function take(e String) returns Integer
		return base + e.byteLength()
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'TextRunner'

function useIt(t Taker) returns Integer
	return t.take(20)
end 'useIt'

function main() returns ExitCode
	return (useIt(IntRunner.create(0)) + useIt(TextRunner.create(11))) as ExitCode
end 'main'
```
```maxoncstderr
error E3119: specs/fragments/associated-types/error.dispatched-associated-type-bound-to-a-string-beside-an-int.test:20:6: 'TextRunner' binds 'Taker's associated type 'Element' to 'String', but 'IntRunner' binds it to 'Integer' — and 'Element' is written as a parameter or return type of a requirement this program DISPATCHES THROUGH A RECEIVER THAT DOES NOT SAY WHICH BINDING IT HOLDS. A witness dispatch is compiled ONCE for every conformer, with no per-conformer specialization, so the shared body would be compiled against one of those two types and hand the other conformer's impl bits it reads as something else. Declare that receiver at 'Taker with <binding>', which settles it for that dispatch; or bind the associated type to the same type in both conformances; or give the two conformers different interfaces
```

<!-- test: error.dispatched-associated-type-bound-to-a-float-beside-an-int -->
⭐⭐ **E3119's FIRST RECORDED FAILURE, IN THE PLAIN SHAPE THE ENTRY DESCRIBES.** The `float`-beside-
`Integer` pair was pinned only through an `extends` projection, which is a case about the TRANSITIVE slot
list; the direct disagreement it was measured on had no case at all. **RE-MEASURED with the refusal lifted,
x64-windows: the program compiles clean and answers 31 where it computes 51** — `FloatRunner.take` reads
its `e` out of xmm0 while the shared body, compiled against `Integer`, passed it in a general-purpose
register. It is the twin of `associated-types.conformers-disagree-but-nothing-dispatches` two cases up,
differing in exactly the fact that gates the rule: there every call is direct, here one is a dispatch.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type IntRunner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'IntRunner'

type FloatRunner implements Taker with float
	let base as Integer

	function take(e Real) returns Integer
		return base + (20 if e > 19.0 else 0)
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'FloatRunner'

function useIt(t Taker) returns Integer
	return t.take(20)
end 'useIt'

function main() returns ExitCode
	return (useIt(IntRunner.create(11)) + useIt(FloatRunner.create(0))) as ExitCode
end 'main'
typealias Real = float(f64.min to f64.max)
```
```maxoncstderr
error E3119: specs/fragments/associated-types/error.dispatched-associated-type-bound-to-a-float-beside-an-int.test:20:6: 'FloatRunner' binds 'Taker's associated type 'Element' to 'float', but 'IntRunner' binds it to 'Integer' — and 'Element' is written as a parameter or return type of a requirement this program DISPATCHES THROUGH A RECEIVER THAT DOES NOT SAY WHICH BINDING IT HOLDS. A witness dispatch is compiled ONCE for every conformer, with no per-conformer specialization, so the shared body would be compiled against one of those two types and hand the other conformer's impl bits it reads as something else. Declare that receiver at 'Taker with <binding>', which settles it for that dispatch; or bind the associated type to the same type in both conformances; or give the two conformers different interfaces
```

<!-- test: associated-types.disagreement-in-a-requirement-no-dispatch-reaches -->
⭐⭐ **THE GATE IS THE DISPATCHED SLOT, NOT THE DISPATCHED INTERFACE NAME.** `Tally` declares two
requirements: `take`, which writes `Element` in a parameter, and `label`, which writes nothing associated.
The only dispatch in the program jumps through `label`. There is therefore no shared body compiled against
either binding of `Element` — the two `take`s are reached by DIRECT calls, each against its own conformer's
declared parameter type — and the E3119 hazard has no site to occur at. Read off the interface NAME the rule
refused this program anyway, for a requirement no witness call reaches. **MEASURED: it runs and answers 51**
(`20 + 11 + 20`). Both consumers of the whole-program fold are keyed on `(interfaceDeclIndex, methodIndex)` —
`LowerMaxonToStd.witnessFormalType` resolves the formals of the requirement a `witnessCall` names, and the
parser types that same requirement's result — so a slot nothing dispatches has no consumer to be wrong.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Score = int(0 to 100)

interface Tally uses Element
	function take(e Element) returns Integer
	function label() returns Integer
end 'Tally'

type Wide implements Tally with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	function label() returns Integer
		return base
	end 'label'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Wide'

type Narrow implements Tally with Score
	let base as Integer

	function take(e Score) returns Integer
		return base + e
	end 'take'

	function label() returns Integer
		return base
	end 'label'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Narrow'

function useIt(t Tally) returns Integer
	return t.label()
end 'useIt'

function main() returns ExitCode
	return (useIt(Wide.create(20)) + useIt(Narrow.create(11)) + Wide.create(0).take(20)) as ExitCode
end 'main'
```
```exitcode
51
```

<!-- test: error.associated-return-bound-to-a-managed-type -->
⭐⭐ **"MACHINE WORD" WAS THE WRONG PREDICATE FOR E3120, AND WHAT IT LET THROUGH LEAKED.** A `String`
binding IS a machine word — a pointer — so the first cut of this rule admitted it. The parser types
`m.make()` off the interface's spelling (`Element` → `named` → `int`), so nothing takes OWNERSHIP of the
returned refcounted value and nothing releases it.
**MEASURED: `with String` and `with Point` both ran to completion and exited 101 — the leak gate — on
x64-windows AND wasm32-wasi. The same interface with the return SPELLED `String` instead of associated
is clean, which attributes the leak exactly to the associated-return path.**
⇒ The conjunct is an UNMANAGED machine word, and what "managed" means is
`ProgramSignatures.declaredNameIsManaged` — the same answer a struct field's drop routing takes.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Maker uses Element
	function make() returns Element
end 'Maker'

type Runner implements Maker with String
	let base as String

	function make() returns String
		return base
	end 'make'

	static function create(base String) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

function useIt(m Maker) returns Integer
	return m.make() + 11
end 'useIt'

function main() returns ExitCode
	return useIt(Runner.create("hello")) as ExitCode
end 'main'
```
```maxoncstderr
error E3120: specs/fragments/associated-types/error.associated-return-bound-to-a-managed-type.test:8:6: 'Runner' binds 'Maker's associated type 'Element' to 'String', and 'Element' is the RETURN type of one of 'Maker's requirements. A dispatch's result type flows on into the code around it — which instruction the arithmetic picks, and whether the value is OWNED and released — and no dispatch through it holds a receiver that says which binding it is, so that is chosen while the interface is still only a NAME. Declare such a receiver at 'Maker with <binding>', which settles the result type at the site; or bind it to an `int`, a ranged typealias or a payload-free enum; or take the value as a PARAMETER instead, where the binding IS resolved
```

<!-- test: associated-types.assoc-return-bound-to-a-machine-word-still-compiles -->
The FALSE-REJECT CONTROL for E3120: an associated type in a RETURN position bound to a ranged alias is
a machine word, so the front end's `named` reading of `Element` and the impl's own return agree, and
the program compiles and answers correctly. Refusing associated returns outright would take this with
it. `20 + 11`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Maker uses Element
	function make() returns Element
end 'Maker'

type Runner implements Maker with Integer
	let base as Integer

	function make() returns Integer
		return base
	end 'make'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

function useIt(m Maker) returns Integer
	return m.make() + 11
end 'useIt'

function main() returns ExitCode
	return useIt(Runner.create(20)) as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: error.extends-projected-associated-type-disagreement -->
⭐⭐ **E3119 WAS ESCAPABLE THROUGH `extends`, AND THE ESCAPE WAS A SILENT WRONG ANSWER.** `Base` declares
`take(e Element)`; `Derived extends Base` redeclares `uses Element`; two conformers of `Derived` bind it
to `Integer` and to `float`; the dispatch goes through a `Derived` existential.
**MEASURED before the fix: compiled clean and answered `31` on x64-windows where the program computes
`51`, while wasm trapped.** Three owners of one fact disagreed — the check scanned only `Derived`'s OWN
requirements (which write nothing), and the lowering resolved the formal against the DECLARING interface
`Base`, which no conformance names (`implements Base with X` is unconstructible, E3016), so it answered
"nothing conforms" and fell back to the machine word for every inherited requirement.
⇒ Both now use the TRANSITIVE list a witness table actually holds (`interfaceWitnessSlots`) and key on
the DISPATCHED interface. The same disagreement declared on the CHILD always fired, which is what
isolated the gap to the projected requirements.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Base uses Element
	function take(e Element) returns Integer
end 'Base'

interface Derived extends Base uses Element
	function extra() returns Integer
end 'Derived'

type A implements Derived with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	function extra() returns Integer
		return 1
	end 'extra'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'A'

type B implements Derived with float
	let base as Integer

	function take(e Real) returns Integer
		return base + (20 if e > 19.0 else 0)
	end 'take'

	function extra() returns Integer
		return 2
	end 'extra'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'B'

function useIt(t Derived) returns Integer
	return t.take(20) + t.extra()
end 'useIt'

function main() returns ExitCode
	return (useIt(A.create(10)) + useIt(B.create(9))) as ExitCode
end 'main'
typealias Real = float(f64.min to f64.max)
```
```maxoncstderr
error E3119: specs/fragments/associated-types/error.extends-projected-associated-type-disagreement.test:28:6: 'B' binds 'Derived's associated type 'Element' to 'float', but 'A' binds it to 'Integer' — and 'Element' is written as a parameter or return type of a requirement this program DISPATCHES THROUGH A RECEIVER THAT DOES NOT SAY WHICH BINDING IT HOLDS. A witness dispatch is compiled ONCE for every conformer, with no per-conformer specialization, so the shared body would be compiled against one of those two types and hand the other conformer's impl bits it reads as something else. Declare that receiver at 'Derived with <binding>', which settles it for that dispatch; or bind the associated type to the same type in both conformances; or give the two conformers different interfaces
```

<!-- test: associated-types.extends-projected-associated-type-single-conformer -->
The FALSE-REJECT CONTROL for the case above, and it is also the second half of a REGRESSION this rung
introduced and then closed. ONE conformer, binding `Derived`'s `Element` to `float`, with `Base`'s
`take` projected in through `extends`.
**MEASURED: the control compiles and answers 31; this rung's previous commit PANICKED the x64 emitter —
`a register-to-register move from xmm0 to rdx crosses register files`, with no source position, on all
three targets** — because resolving the formal against the DECLARING interface found no conformance and
fell back to the machine word while the impl declares an f64. Resolving against the DISPATCHED interface
is what makes the single-conformer case right and the two-conformer case refusable. `(0 + 20) + 11`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Base uses Element
	function take(e Element) returns Integer
end 'Base'

interface Derived extends Base uses Element
	function extra() returns Integer
end 'Derived'

type FloatRunner implements Derived with float
	let base as Integer

	function take(e Real) returns Integer
		return self.base + (20 if e > 19.0 else 0)
	end 'take'

	function extra() returns Integer
		return 11
	end 'extra'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'FloatRunner'

function useIt(t Derived) returns Integer
	return t.take(20.0) + t.extra()
end 'useIt'

function main() returns ExitCode
	return useIt(FloatRunner.create(0)) as ExitCode
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
31
```

<!-- test: error.associated-return-bound-to-a-generic-instance -->
⭐⭐ **E3120 ADMITTED A GENERIC INSTANCE AND THE PROGRAM LEAKED.** A `Box with Integer` IS a machine word
— a pointer — and it is managed. The predicate asked `declaredNameIsManaged`, which answered `false`,
and the reason is a rendering: a conformance's `with` argument is recorded as the compiler's own
CANONICAL name (`Box_Integer`), while `genericAliases` is keyed by the ALIAS (`IntBox`) — so
`declaredFormOf` matched no registry at all and the walk fell through its `otherwise return false`.
**MEASURED: `with IntBox` and `with IntArr` (an `Array with Integer`) both ran to completion and exited
101, the leak gate, on x64 and wasm alike.**
⇒ That door answers for canonical instance names now. It also falsified the claim that an undeclared
name is safe here because E3011 would have fired — `Box_Integer` is the compiler's own spelling of a
declared type and no diagnostic fires on it.

⚠ **THE SENTENCE ITSELF USED TO SHOW `Box_Integer`, AND A DIAGNOSTIC NAMES A TYPE THE WAY THE AUTHOR WROTE
IT (W58).** The canonical mint is the right key for the ownership question above and is not a type name: the
author wrote `IntBox`, and no source line in this program holds the other spelling. Every message in
`ConformanceCheck` that prints a conformance's recorded binding now goes through
`displaySpellingOfDeclaredName`, the name-keyed twin of `instanceDisplayName`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	let v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

typealias IntBox = Box with Integer

interface Maker uses Element
	function make() returns Element
end 'Maker'

type Runner implements Maker with IntBox
	let base as IntBox

	function make() returns IntBox
		return self.base
	end 'make'

	static function create(base IntBox) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

function useIt(m Maker) returns Integer
	return m.make() + 11
end 'useIt'

function main() returns ExitCode
	return useIt(Runner.create(IntBox.create(20))) as ExitCode
end 'main'
```
```maxoncstderr
error E3120: specs/fragments/associated-types/error.associated-return-bound-to-a-generic-instance.test:18:6: 'Runner' binds 'Maker's associated type 'Element' to 'IntBox', and 'Element' is the RETURN type of one of 'Maker's requirements. A dispatch's result type flows on into the code around it — which instruction the arithmetic picks, and whether the value is OWNED and released — and no dispatch through it holds a receiver that says which binding it is, so that is chosen while the interface is still only a NAME. Declare such a receiver at 'Maker with <binding>', which settles the result type at the site; or bind it to an `int`, a ranged typealias or a payload-free enum; or take the value as a PARAMETER instead, where the binding IS resolved
```

<!-- test: error.associated-type-bound-to-an-interface -->
⭐⭐ **AN ASSOCIATED TYPE BOUND TO AN INTERFACE NAME SEGFAULTED.** A value held at an interface is a
two-word fat pointer `(value, witness)`, and a witness call carries ONE machine word per argument — so
the second word is dropped and the impl reads a witness that was never passed.
**MEASURED: exit 139 on x64-windows, and a trap on wasm.** It reached the ABI because the width question
was being read off the OWNERSHIP door: `declaredNameIsManaged`'s `interfaceType` arm answers `false`,
which is true (an existential owns no record) and is not the question. That arm's own comment said
*"existentials are unbuilt. When they land, a fat pointer's ownership is that rung's answer to give, and
this arm is where it says it"* — this is that rung, and the arm had been silently promoted to
load-bearing by giving it this caller.
⚠ Refused wherever the associated type reaches the calling convention, PARAMETER as well as return —
this case's `Element` is a parameter, and the return-only rule could not see it.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type Runner implements Taker with Shape
	let base as Integer

	function take(e Shape) returns Integer
		return self.base + e.area()
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

function useIt(t Taker) returns Integer
	return t.take(Sq.create(11))
end 'useIt'

function main() returns ExitCode
	return useIt(Runner.create(20)) as ExitCode
end 'main'
```
```maxoncstderr
error E3120: specs/fragments/associated-types/error.associated-type-bound-to-an-interface.test:24:6: 'Runner' binds 'Taker's associated type 'Element' to the interface type 'Shape', and 'Element' reaches the calling convention of a requirement this program DISPATCHES through a receiver that does not say which binding it holds — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a witness call carries one machine word per argument and one per result — so the second word is dropped and the impl reads a witness that was never passed. Bind the associated type to a concrete type
```

### ⭐⭐ A PARAMETERIZED EXISTENTIAL CARRIES ITS ARGUMENTS INTO THE SIGNATURE, AND THE WIDENING DISCHARGES THE CLAIM (W58)

`ProgramSignatures.instanceDenotedType` collapses `Base with Args` to a bare `interfaceRef(Base)`, so the
arguments a use site wrote stopped existing on the type and E3125 was the whole of their enforcement — a
WHOLE-PROGRAM comparison against the one binding the conformances settled. That is exactly right while the
written argument is a CONCRETE type, and it is wrong when the argument is the enclosing generic's own
TYPE PARAMETER: `typealias TakerOfT = Taker with T` inside `type Box uses T` states nothing about the
program, it states something about each INSTANTIATION, and comparing `T` against `Integer` refused a
correct program.

So the arguments now ride the SIGNATURE (`FuncSignature.paramExistentialSites` / `returnExistentialSite`),
are substituted through the call's own instance, and the claim is discharged where a concrete value
actually becomes an existential — the WIDENING, which is the one door both positions already share
(`SemanticCheck.existentialWideningVerdict`). E3125 keeps every concrete claim; only an OPAQUE one is
deferred to that door.

⚠ **A CLAIM THAT CANNOT BE RESOLVED IS REFUSED, NOT ADMITTED.** A `return` position is checked in the
DECLARATION view, where the enclosing generic's parameter is still opaque and there is no instance to
substitute it through — so an opaque claim there has no answer and E3127 says so, exactly as E3120 refuses
an associated RETURN whose binding the parser cannot resolve and points at the parameter position instead.

<!-- test: associated-types.existential-parameter-bound-to-the-enclosing-generics-own-parameter -->
⭐⭐ **THE CASE E3125 REFUSED AND THE PROGRAM COMPUTES.** A shared generic body binds an existential's
associated position to its OWN type parameter and dispatches through it. `Box with Integer` makes the
claim `Taker with Integer`, which is what `Runner` binds — so the widening is sound and the dispatch is
the ordinary one. **MEASURED RED: `E3125 … binds its associated type 'Element' to 'T', but 'Runner' binds
it to 'Integer'`.** Returns `31`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type Runner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T
	typealias TakerOfT = Taker with T

	let seed as Integer

	export function run(t TakerOfT, c T) returns Integer
		return t.take(c) + self.seed
	end 'run'

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	return IntBox.create(0).run(Runner.create(20), c: 11) as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: error.existential-parameter-claim-disagrees-with-the-widened-conformer -->
⭐⭐ **THE SAFETY CONDITION THE DEFERRAL OWES, AND WITHOUT IT THE PROGRAM IS E3119's FIRST RECORDED FAILURE
ONE INDIRECTION LATER.** The same shared body, instantiated at `String`: the site's claim is now
`Taker with String` and the value widened into it is a `Runner`, which binds `Element` to `Integer`. Under
the per-site rule the conformances no longer settle the question, so the WIDENING is the only place the two
bindings meet, and it refuses. Nothing here is a whole-program disagreement — there is exactly one
conformer — so neither E3119 nor E3125 can see it.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
	function label() returns Integer
end 'Taker'

type Runner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	function label() returns Integer
		return base
	end 'label'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T
	typealias TakerOfT = Taker with T

	let seed as Integer

	export function run(t TakerOfT) returns Integer
		return t.label() + self.seed
	end 'run'

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	return StrBox.create(0).run(Runner.create(20)) as ExitCode
end 'main'
```
```maxoncstderr
error E3127: specs/fragments/associated-types/error.existential-parameter-claim-disagrees-with-the-widened-conformer.test:42:26: cannot widen 'Runner' into 't', which is declared at the existential type 'Taker' with its associated type 'Element' bound to 'String' — 'Runner' binds 'Element' to 'Integer'. A dispatch through this value is emitted against the binding the site claims and would reach an impl written for the other one. Write the binding the conformer declares, or widen a conformer that binds 'Element' to 'String'
```

<!-- test: associated-types.existential-parameter-with-a-concrete-claim-still-runs -->
The FALSE-REJECT CONTROL for the new door in its ordinary shape: a CONCRETE claim that restates what the
one conformer binds is what E3125 has always admitted, and the widening check must agree with it rather
than acquire an opinion of its own. Returns `31`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type Runner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

typealias IntTaker = Taker with Integer

function useIt(t IntTaker, c Integer) returns Integer
	return t.take(c)
end 'useIt'

function main() returns ExitCode
	return useIt(Runner.create(20), c: 11) as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: error.existential-return-claim-cannot-be-resolved-in-the-declaration-view -->
⭐ **THE HOLE THE DEFERRAL WOULD OTHERWISE OPEN, CLOSED WHERE IT OPENS.** A `return` widens too, and it is
checked once against the shared body's DECLARATION view — where `T` is still opaque and no call instance
exists to substitute it through. Admitting the claim there would let a `Box with String` hand back a
`Runner` that binds `Integer`, which is the previous case with no site left to report it at. Refused, and
the sentence names the position that CAN be resolved.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
	function label() returns Integer
end 'Taker'

type Runner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	function label() returns Integer
		return base
	end 'label'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T
	typealias TakerOfT = Taker with T

	let seed as Integer

	export function make() returns TakerOfT
		return Runner.create(20 + self.seed)
	end 'make'

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	return IntBox.create(0).make().label() as ExitCode
end 'main'
```
```maxoncstderr
error E3127: specs/fragments/associated-types/error.existential-return-claim-cannot-be-resolved-in-the-declaration-view.test:31:3: cannot return a value from 'Box.make', which is declared to return the existential type 'Taker' with its associated type 'Element' bound to 'T' — 'T' is a type parameter of the declaring type, and a `return` is checked once against the shared body, where no instantiation has fixed it. Write the binding the conformers declare, or take the value as a PARAMETER instead, where the call's own instance resolves it
```

<!-- test: associated-types.existential-field-at-an-opaque-claim-is-filled-through-a-checked-parameter -->
⭐ **THE FIELD POSITION, WHICH IS THE THIRD PLACE A VALUE COULD BECOME AN EXISTENTIAL.** The deferral admits
`Taker with T` as a FIELD type as well as a parameter one, and a field store is checked by nobody — so this
case and the one below it are the pair that says the deferral opened no hole. This half is the legal route:
the field is filled from a PARAMETER, which the widening check reaches, and the call goes through a STATIC
constructor rather than an instance method — a different subject for `callInstanceSubject` to resolve the
instantiation from, and the one `stdlib` shapes are built out of. Returns `31`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
	function label() returns Integer
end 'Taker'

type Runner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	function label() returns Integer
		return base
	end 'label'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T
	typealias TakerOfT = Taker with T

	let t as TakerOfT

	export function run() returns Integer
		return self.t.label()
	end 'run'

	static function create(t TakerOfT) returns Self
		return Self{t: t}
	end 'create'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	return IntBox.create(Runner.create(31)).run() as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: error.existential-field-at-an-opaque-claim-cannot-be-stored-into-directly -->
⭐⭐ **AND THE OTHER HALF: THE ROUTE THAT WOULD BYPASS THE WIDENING CHECK DOES NOT EXIST.** The same field,
filled by a struct literal inside the shared body — no callee, no parameter, nothing for E3127 to be asked at.
It is refused, and it was refused before this rung existed: E2015 already required every widening to name a
SITE, for the fat pointer's own reason. **That is the argument the E3125 deferral rests on** — the widening
positions E3127 covers (a call argument and a `return`) are ALL of them, so a claim deferred out of the
whole-program check cannot slip through a third door. Pinned as a case rather than left as reasoning, because
the day a direct field store becomes legal is the day the deferral needs a third discharge.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
	function label() returns Integer
end 'Taker'

type Runner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	function label() returns Integer
		return base
	end 'label'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T
	typealias TakerOfT = Taker with T

	let t as TakerOfT

	export function run() returns Integer
		return self.t.label()
	end 'run'

	static function make() returns Self
		return Self{t: Runner.create(31)}
	end 'make'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	return StrBox.make().run() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/associated-types/error.existential-field-at-an-opaque-claim-cannot-be-stored-into-directly.test:35:15: Unsupported: storing a value of a CONCRETE type into the interface-typed field 'Box.t' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and the second word is the address of the conformer's witness table, which only a widening SITE can name. A field store has no callee whose declared parameter types could name it, unlike a call argument. Pass the value to a named function taking the interface as a PARAMETER and store THAT parameter (which arrives already widened), or declare the field at a concrete type
```
<!-- test: associated-types.a-generic-conformers-binding-is-resolved-through-its-own-instance -->
⭐⭐ **THE CONFORMER'S SIDE IS A SOURCE SPELLING TOO, AND A GENERIC CONFORMER'S NAMES ITS OWN PARAMETER.**
`type Holder uses E implements Taker with E` records the binding as the STRING `E` — not a type, and not
anything a site's `Integer` could equal. Compared raw that is a false disagreement, and it is exactly the shape
`stdlib/Array.maxon` is made of (`type Array uses Element implements Iterable with Element`). So both sides are
reduced before they meet: the SITE's claim through the call's instance (`Box with Integer` makes `Taker with T`
into `Taker with Integer`) and the CONFORMER's binding through its own (`Holder with Integer` makes `E` into
`Integer`). Returns `31`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
	function label() returns Integer
end 'Taker'

type Holder uses E implements Taker with E
	let base as Integer
	let seed as E

	function take(e E) returns Integer
		return self.base
	end 'take'

	function label() returns Integer
		return self.base
	end 'label'

	static function create(base Integer, seed E) returns Self
		return Self{base: base, seed: seed}
	end 'create'
end 'Holder'

type Box uses T
	typealias TakerOfT = Taker with T

	let extra as Integer

	export function run(t TakerOfT) returns Integer
		return t.label() + self.extra
	end 'run'

	static function create(extra Integer) returns Self
		return Self{extra: extra}
	end 'create'
end 'Box'

typealias IntHolder = Holder with Integer
typealias IntBox = Box with Integer

function main() returns ExitCode
	return IntBox.create(11).run(IntHolder.create(20, seed: 0)) as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: error.a-generic-conformers-resolved-binding-still-has-to-match -->
⭐ **AND THE CONTROL THAT KEEPS THE REDUCTION HONEST.** The identical program at `Box with String`: the site
now claims `Taker with String` and `IntHolder`'s `E` still resolves to `Integer`. A reduction that had quietly
answered "agrees" for every generic conformer would pass this, so the two cases are a pair — one proves the
resolution happens, the other that it still compares.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
	function label() returns Integer
end 'Taker'

type Holder uses E implements Taker with E
	let base as Integer
	let seed as E

	function take(e E) returns Integer
		return self.base
	end 'take'

	function label() returns Integer
		return self.base
	end 'label'

	static function create(base Integer, seed E) returns Self
		return Self{base: base, seed: seed}
	end 'create'
end 'Holder'

type Box uses T
	typealias TakerOfT = Taker with T

	let extra as Integer

	export function run(t TakerOfT) returns Integer
		return t.label() + self.extra
	end 'run'

	static function create(extra Integer) returns Self
		return Self{extra: extra}
	end 'create'
end 'Box'

typealias IntHolder = Holder with Integer
typealias StrBox = Box with String

function main() returns ExitCode
	return StrBox.create(11).run(IntHolder.create(20, seed: 0)) as ExitCode
end 'main'
```
```maxoncstderr
error E3127: specs/fragments/associated-types/error.a-generic-conformers-resolved-binding-still-has-to-match.test:44:27: cannot widen 'IntHolder' into 't', which is declared at the existential type 'Taker' with its associated type 'Element' bound to 'String' — 'IntHolder' binds 'Element' to 'Integer'. A dispatch through this value is emitted against the binding the site claims and would reach an impl written for the other one. Write the binding the conformer declares, or widen a conformer that binds 'Element' to 'String'
```
<!-- test: error.a-deferred-claim-cannot-be-filled-from-a-value-already-held-at-the-interface -->
⛔⛔ **THE HOLE THE DEFERRAL OPENED, FOUND BY RUNNING IT, AND THE REASON E3127 IS NOT ONLY ABOUT CONCRETE
CONFORMERS.** Every check above asks what the ARRIVING TYPE binds. A value already held at `Taker` answers
nothing — which conformer is inside a fat pointer is exactly what it exists to not say — so forwarding a bare
`Taker` into a `Box with String`'s `Taker with T` slipped past the widening check entirely.
**MEASURED on this tree with the deferral landed and this refusal not yet written: it COMPILED**, `Runner.take`
read a `String` pointer as its `Integer` argument, and the program died
`panic … Range check failed: value outside typealias 'ExitCode'`. That is E3119's own recorded failure class
one indirection later, which is precisely what E3127 exists to stop.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type Runner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T
	typealias TakerOfT = Taker with T

	let seed as Integer

	export function run(t TakerOfT, c T) returns Integer
		return t.take(c) + self.seed
	end 'run'

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'
end 'Box'

typealias StrBox = Box with String

function relay(t Taker) returns Integer
	return StrBox.create(0).run(t, c: "hello")
end 'relay'

function main() returns ExitCode
	return relay(Runner.create(20)) as ExitCode
end 'main'
```
```maxoncstderr
error E3127: specs/fragments/associated-types/error.a-deferred-claim-cannot-be-filled-from-a-value-already-held-at-the-interface.test:37:26: cannot pass a value already held at the interface type 'Taker' as 't', which is declared at the existential type 'Taker' with its associated type 'Element' bound to 'T' — 'T' is a type parameter of the declaring type, so this site's binding was never settled against the conformances, and a value held at an interface does not say which conformer is inside it for the call to settle it against. Pass the concrete conformer, or declare 't' at 'Taker' with no `with` clause
```

<!-- test: associated-types.a-concrete-claim-may-still-be-filled-from-a-value-already-held-at-the-interface -->
⚖ **AND THE REASON THE REFUSAL ABOVE IS NARROW RATHER THAN BLANKET.** The identical forwarding into a
CONCRETE claim is legal and must stay so: E3125 has already checked `Taker with Integer` against the one
binding the conformances settled, and E3119 makes that binding unique — so whatever conformer is inside the fat
pointer binds `Element` to `Integer` too, and the site has nothing left to verify. Only a DEFERRED claim has
had neither check, which is the one case the refusal names. Returns `31`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type Runner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

typealias IntTaker = Taker with Integer

function useIt(t IntTaker, c Integer) returns Integer
	return t.take(c)
end 'useIt'

function relay(t Taker, c Integer) returns Integer
	return useIt(t, c: c)
end 'relay'

function main() returns ExitCode
	return relay(Runner.create(20), c: 11) as ExitCode
end 'main'
```
```exitcode
31
```
<!-- test: error.a-deferred-claim-cannot-be-laundered-through-a-return -->
⭐ **THE ESCAPE ATTEMPT, RUN RATHER THAN REASONED ABOUT.** The `return` position lets an ALREADY-existential
value through — `function id(t TakerOfT) returns TakerOfT` restates its own claim and widens nothing — so the
obvious way around the refusal above is to launder a bare `Taker` into a claimed one through a return and hand
THAT to the claimed parameter. It does not work, and the reason is that a claim is only ever consumed at a
DECLARED slot: the laundered result carries no claim of its own, and the moment it reaches one it is an
already-existential argument again. Refused at the consumer, with the same sentence and the call's position.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type Runner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T
	typealias TakerOfT = Taker with T

	let seed as Integer

	export function pass(t Taker) returns TakerOfT
		return t
	end 'pass'

	export function use(t TakerOfT, c T) returns Integer
		return t.take(c) + self.seed
	end 'use'

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	let b = StrBox.create(0)
	return b.use(b.pass(Runner.create(20)), c: "hi") as ExitCode
end 'main'
```
```maxoncstderr
error E3127: specs/fragments/associated-types/error.a-deferred-claim-cannot-be-laundered-through-a-return.test:42:11: cannot pass a value already held at the interface type 'Taker' as 't', which is declared at the existential type 'Taker' with its associated type 'Element' bound to 'T' — 'T' is a type parameter of the declaring type, so this site's binding was never settled against the conformances, and a value held at an interface does not say which conformer is inside it for the call to settle it against. Pass the concrete conformer, or declare 't' at 'Taker' with no `with` clause
```

<!-- test: associated-types.two-conformers-disagree-but-every-dispatch-site-names-its-binding -->
⭐⭐ **THE PER-SITE RULE, AND IT IS THE EXACT PROGRAM E3119's SECOND RECORDED FAILURE REFUSES, ONE `with`
CLAUSE APART (W59).** `String` beside `Integer` is the pair that SEGFAULTED when a single shared body was
compiled against one of them — and the body is only shared because the receiver was written at the bare
interface. Here each dispatch is reached through a receiver that names its own binding, so there are TWO
bodies and each is compiled against the binding only its own conformers can arrive at (E3127 refuses the
rest). **Measured: refused E3119 by the compiler this rung started from; compiles and answers 61 now.**
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntTaker = Taker with Integer
typealias TextTaker = Taker with String

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type IntRunner implements Taker with Integer
	let base as Integer

	function take(e Integer) returns Integer
		return base + e
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'IntRunner'

type TextRunner implements Taker with String
	let base as Integer

	function take(e String) returns Integer
		return base + e.byteLength()
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'TextRunner'

function useInt(t IntTaker) returns Integer
	return t.take(20)
end 'useInt'

function useText(t TextTaker) returns Integer
	return t.take("abcdefghij")
end 'useText'

function main() returns ExitCode
	return (useInt(IntRunner.create(20)) + useText(TextRunner.create(11))) as ExitCode
end 'main'
```
```exitcode
61
```

<!-- test: associated-types.a-held-position-carries-its-own-with-arguments-through-the-dispatch -->
⭐⭐ **THE SITE'S CLAIM SURVIVES ONE INDIRECTION — through a HELD position, which is the shape
`stdlib/Array.maxon` is made of.** `Bag with (Integer, Cursor with Integer)` says `Iter` is held at `Cursor`
AND that whatever cursor arrives yields `Integer`, so `b.cursor().item()` is typed from the site rather than
from a fold over every `Cursor` conformer — and `TextCur`, which binds `Item` to `String`, is beside it in the
same program. **Measured: refused E3119 before W59; answers 11 now** (7 from the bag, 4 from `"nope"`'s
length through a DIRECT call on the other cursor, which is what keeps both conformers live).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntCursor = Cursor with Integer
typealias IntBag = Bag with (Integer, IntCursor)

interface Cursor uses Item
	function item() returns Item
end 'Cursor'

interface Bag uses Element, Iter
	function cursor() returns Iter
end 'Bag'

type IntCur implements Cursor with Integer
	let v as Integer

	function item() returns Integer
		return v
	end 'item'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'IntCur'

type TextCur implements Cursor with String
	let s as String

	function item() returns String
		return s
	end 'item'

	static function create(s String) returns Self
		return Self{s: s}
	end 'create'
end 'TextCur'

type GoodBag implements Bag with (Integer, IntCur)
	let v as Integer

	function cursor() returns IntCur
		return IntCur.create(v)
	end 'cursor'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'GoodBag'

type BadBag implements Bag with (Integer, TextCur)
	let v as Integer

	function cursor() returns TextCur
		return TextCur.create("nope")
	end 'cursor'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'BadBag'

function firstOf(b IntBag) returns Integer
	return b.cursor().item()
end 'firstOf'

function main() returns ExitCode
	return (firstOf(GoodBag.create(7)) + BadBag.create(1).cursor().item().byteLength()) as ExitCode
end 'main'
```
```exitcode
11
```

<!-- test: error.held-position-nested-binding-disagreement -->
⭐⭐ **THE OTHER HALF OF THE CASE ABOVE, AND WITHOUT IT W59 WOULD BE A SILENT WRONG ANSWER.** The site claims
`Cursor with Integer` at a HELD position; `BadBag` binds `Iter` to a cursor yielding `String`. E3125 DEFERS a
nested claim (it is opaque inside a generic declaration) and E3127's held-position exemption used to skip it,
so nothing checked it — which cost nothing only while E3119 forbade `IntCur` and `TextCur` from coexisting.
**MEASURED with the claim unchecked: the program compiled, `item()` handed back a `String` pointer through a
slot typed `Integer`, and the leak gate reported exit 101.** The refusal names the level the fault is at:
'Cursor', not 'Bag', and 'TextCur', not 'BadBag'.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntCursor = Cursor with Integer
typealias IntBag = Bag with (Integer, IntCursor)

interface Cursor uses Item
	function item() returns Item
end 'Cursor'

interface Bag uses Element, Iter
	function cursor() returns Iter
end 'Bag'

type IntCur implements Cursor with Integer
	let v as Integer

	function item() returns Integer
		return v
	end 'item'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'IntCur'

type TextCur implements Cursor with String
	let s as String

	function item() returns String
		return s
	end 'item'

	static function create(s String) returns Self
		return Self{s: s}
	end 'create'
end 'TextCur'

type GoodBag implements Bag with (Integer, IntCur)
	let v as Integer

	function cursor() returns IntCur
		return IntCur.create(v)
	end 'cursor'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'GoodBag'

type BadBag implements Bag with (Integer, TextCur)
	let v as Integer

	function cursor() returns TextCur
		return TextCur.create("nope")
	end 'cursor'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'BadBag'

function firstOf(b IntBag) returns Integer
	return b.cursor().item()
end 'firstOf'

function main() returns ExitCode
	return (firstOf(GoodBag.create(7)) + firstOf(BadBag.create(1))) as ExitCode
end 'main'
```
```maxoncstderr
error E3127: specs/fragments/associated-types/error.held-position-nested-binding-disagreement.test:67:39: cannot widen 'BadBag' into 'b', which is declared at the existential type 'Bag' with its associated type 'Iter' held at 'Cursor', and that holding's own 'Item' bound to 'Integer' — 'BadBag' binds 'Iter' to 'TextCur', which binds 'Item' to 'String'. A dispatch through this value is emitted against the binding the site claims and would reach an impl written for the other one. Write the binding the conformer declares, or widen a conformer that binds 'Item' to 'Integer'
```


### An INHERITED requirement is substituted by the conformance to the interface that DECLARED it (W60)

⭐⭐ **CONFORMING TO A DESCENDANT MUST NOT DESTROY THE ANCESTOR'S BINDING.** `Element` is *Cursor's*
`uses` name, so a requirement that mentions it means whatever this type's conformance to **Cursor**
bound it to — including when the requirement arrives in `BiCursor`'s slot list through `extends`.
It did not: `collectInterfaceMethods` substituted every slot under the ROOT entry's bindings, and
`BiCursor` declares no `uses` of its own, so the inherited `current() returns Element` was compared
raw. MEASURED before the fix: `E3016 … current() returns Pos (expected current() returns Element)` —
against a program the bootstrap compiles and runs.

<!-- test: associated-types.extends.inherited-requirement-substituted-by-a-sibling-conformance -->
```maxon
interface Cursor uses Element
	function current() returns Element
end 'Cursor'

interface BiCursor extends Cursor
	function retreat()
end 'BiCursor'

typealias Pos = int(0 to 100)

type Walker implements Cursor with Pos, BiCursor
	var pos as Pos

	export static function create(p Pos) returns Self
		return Self{pos: p}
	end 'create'

	export function current() returns Pos
		return pos
	end 'current'

	export function retreat()
		pos = pos - 1
	end 'retreat'
end 'Walker'

function main() returns ExitCode
	var w = Walker.create(8)
	w.retreat()
	print("{w.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```


### …and the clause may be written in either order

The lookup is a SEARCH of the whole `implements` clause and not a look-behind, because an author may
write the descendant first. The bootstrap accepts this spelling too (measured: exit 0, `7`), so an
order-dependent answer here would be shv2's alone.

<!-- test: associated-types.extends.the-sibling-conformance-may-be-written-after-the-descendant -->
```maxon
interface Cursor uses Element
	function current() returns Element
end 'Cursor'

interface BiCursor extends Cursor
	function retreat()
end 'BiCursor'

typealias Pos = int(0 to 100)

type Walker implements BiCursor, Cursor with Pos
	var pos as Pos

	export static function create(p Pos) returns Self
		return Self{pos: p}
	end 'create'

	export function current() returns Pos
		return pos
	end 'current'

	export function retreat()
		pos = pos - 1
	end 'retreat'
end 'Walker'

function main() returns ExitCode
	var w = Walker.create(8)
	w.retreat()
	print("{w.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```


### …and the NEGATIVE CONTROL: the ancestor conformance alone, which already worked

The case above with `, BiCursor` struck — the `BiCursor` interface still declared and `retreat()` still
present, so the only thing removed is a CONFORMANCE. It passed before W60 and passes after, which is what
attributes a future regression to the `extends` half rather than to conformance generally.

<!-- test: associated-types.extends.the-ancestor-conformance-alone-still-answers -->
```maxon
interface Cursor uses Element
	function current() returns Element
end 'Cursor'

interface BiCursor extends Cursor
	function retreat()
end 'BiCursor'

typealias Pos = int(0 to 100)

type Walker implements Cursor with Pos
	var pos as Pos

	export static function create(p Pos) returns Self
		return Self{pos: p}
	end 'create'

	export function current() returns Pos
		return pos
	end 'current'

	export function retreat()
		pos = pos - 1
	end 'retreat'
end 'Walker'

function main() returns ExitCode
	var w = Walker.create(8)
	w.retreat()
	print("{w.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```


### …and a name NO entry of the clause binds still denotes itself

⚖ The binding comes from a conformance, never from an inheritance of ARITY: `implements BiCursor` writes
no conformance to `Cursor` at all, so `Element` is bound by nothing and the requirement is compared as
written. MEASURED — the bootstrap rejects this program with the same sentence, as it does the
`implements BiCursor with Pos` spelling one file over
(`error.inherited-uses-name-unparenthesized-is-still-a-signature-error`). v1 is the outlier here
(`findInheritedBindings`); shv2 follows the bootstrap, and `Parser.conformanceUsesArity` records why.

<!-- test: error.extends.no-sibling-conformance-leaves-the-inherited-name-unbound -->
```maxon
interface Cursor uses Element
	function current() returns Element
end 'Cursor'

interface BiCursor extends Cursor
	function retreat()
end 'BiCursor'

typealias Pos = int(0 to 100)

type Walker implements BiCursor
	var pos as Pos

	export static function create(p Pos) returns Self
		return Self{pos: p}
	end 'create'

	export function current() returns Pos
		return pos
	end 'current'

	export function retreat()
		pos = pos - 1
	end 'retreat'
end 'Walker'

function main() returns ExitCode
	var w = Walker.create(8)
	w.retreat()
	print("{w.current()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/error.extends.no-sibling-conformance-leaves-the-inherited-name-unbound.test:12:6: Partial interface implementation: type 'Walker' has 1 method(s) with wrong signature:
  - current() returns Pos (expected current() returns Element)
```


### …and a GENUINELY wrong signature still says so — naming the SUBSTITUTED type

⛔ **THE CASE THAT PINS "CORRECT VERDICT, WRONG SENTENCE".** The substitution is made ONCE, at collection,
so the string `signatureMatches` decides on and the string `formatInterfaceMethodSignature` prints are the
same string (`collectInterfaceMethods`' header). A fix that reached the comparison and missed the message
would leave this program rejected — correctly — while telling its author the requirement is
`returns Element`, a name their program cannot satisfy and no positive test can see. So the assertion here
is the SENTENCE, not the code.

⚠ **BOTH LINES ARE THE MEASURED OUTPUT AND THE DUPLICATION IS PRE-EXISTING.** `Cursor`'s slot is reached
by two entries of one clause, and each entry reports its own verdict — before W60 it reported the same
defect twice with two DIFFERENT expected types (`Pos` from the `Cursor` entry, `Element` from the
`BiCursor` one). What this rung changes is that the two now agree; de-duplicating a rejection the way
`recordWitnessSlotImpl` de-duplicates an ACCEPTANCE is a separate question, and it needs a home on
`Project` beside that map.

<!-- test: error.extends.wrong-signature-under-an-inherited-binding-names-the-substituted-type -->
```maxon
interface Cursor uses Element
	function current() returns Element
end 'Cursor'

interface BiCursor extends Cursor
	function retreat()
end 'BiCursor'

typealias Pos = int(0 to 100)

type Walker implements Cursor with Pos, BiCursor
	var pos as Pos

	export static function create(p Pos) returns Self
		return Self{pos: p}
	end 'create'

	export function current() returns String
		return "no"
	end 'current'

	export function retreat()
		pos = pos - 1
	end 'retreat'
end 'Walker'

function main() returns ExitCode
	var w = Walker.create(8)
	w.retreat()
	print("{w.current()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/error.extends.wrong-signature-under-an-inherited-binding-names-the-substituted-type.test:12:6: Partial interface implementation: type 'Walker' has 1 method(s) with wrong signature:
  - current() returns String (expected current() returns Pos)
error E3016: specs/fragments/associated-types/error.extends.wrong-signature-under-an-inherited-binding-names-the-substituted-type.test:12:6: Partial interface implementation: type 'Walker' has 1 method(s) with wrong signature:
  - current() returns String (expected current() returns Pos)
```


### An interface-scoped `typealias` in a requirement is EXPANDED, not name-matched (W61)

⭐⭐ **AN INTERFACE'S `typealias` IS LOCAL SHORTHAND, NOT PART OF ITS REQUIREMENT SURFACE.**
`interface Holder uses Element` declaring `typealias ElementArray = Array with Element` requires, of
`absorb`, a parameter of the type that alias's RIGHT-HAND SIDE names under this conformance's bindings —
`Array with <whatever Element was bound to>` — and NOT a parameter spelled `ElementArray`. Before W61 the
requirement rendered the unresolved name `Holder.ElementArray` while every conformer's own alias rendered a
resolved instance, so the two sides of one requirement were compared as a name against a type and EVERY
conformer was refused:

```
E3016 … - absorb(value Array_T779624e88745be2b) returns void (expected absorb(value Holder.ElementArray) returns void)
```

⛔ **BOTH REFERENCE COMPILERS MATCH THE ALIAS'S SPELLING INSTEAD, AND CANONICAL `/specs` HAS NO CASE FOR THE
SHAPE AT ALL.** Measured on the bootstrap: the first case below compiles, and the second — the conformer
spelling its own alias `NumStore` over the identical right-hand side — is REFUSED with
`absorb(value NumStore) returns void (expected absorb(value ElementArray) returns void)`. v1 discards the
alias line outright (`Parser.maxon:6578-6585`). So `stdlib/Set.maxon` and `stdlib/List.maxon` conform
through `InitableFromArrayLiteral` only because two files independently wrote the member name
`ElementArray`. And a conformer cannot avoid inventing a name: `value Array with Element` in a parameter
list is **E2010**, and `var items as Array with Element` in a field is **E3005 … Define a typealias
first** — so under the references the language forces every conformer to guess the interface's private
spelling, and refuses it with a diagnostic naming a type its program never wrote.

⚖ **USER RULING (BATCH36): conformance is decided on the UNDERLYING TYPE. shv2 therefore accepts programs
both references refuse, deliberately.** A reader who finds an oracle disagreement here has found the ruling,
not a bug.

<!-- test: w61.interface-scoped-alias-in-a-requirement -->
```maxon
typealias Num = int(0 to 1000)

interface Holder uses Element
	typealias ElementArray = Array with Element
	function absorb(value ElementArray)
end 'Holder'

interface Maker
	static function spawn() returns Self
end 'Maker'

type Bag uses Element implements Holder with Element, Maker
	typealias ElementArray = Array with Element
	var items as ElementArray = ElementArray.create()

	export static function spawn() returns Self
		return Self{}
	end 'spawn'

	export function absorb(value ElementArray)
		items = value.clone()
	end 'absorb'

	export function count() returns Num
		return items.count()
	end 'count'
end 'Bag'

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```


### …and the case the ruling turns on: the conformer names its alias something else

⭐⭐ **`stdlib/Set.maxon`'s SHAPE WITH THE COINCIDENCE REMOVED.** The conformer writes
`typealias NumStore = Array with Num` and binds `Holder with Num`; nothing but the underlying type is
shared with the interface's own `ElementArray`. This is the ONE program that separates name-matching from
right-hand-side expansion — it is what both references refuse and what the ruling requires — so it is also
the case a future regression toward the reference behaviour would land on first.

<!-- test: w61.conformer-names-the-alias-differently -->
```maxon
typealias Num = int(0 to 1000)

interface Holder uses Element
	typealias ElementArray = Array with Element
	function absorb(value ElementArray)
end 'Holder'

typealias NumStore = Array with Num

type Crate implements Holder with Num
	var items as NumStore = NumStore.create()

	export static function create() returns Self
		return Self{}
	end 'create'

	export function absorb(value NumStore)
		items = value.clone()
	end 'absorb'

	export function count() returns Num
		return items.count()
	end 'count'
end 'Crate'

function main() returns ExitCode
	var c = Crate.create()
	var v = NumStore.create()
	v.push(4)
	v.push(9)
	c.absorb(v)
	print("{c.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```


### …and an alias over a CONCRETE type, in an interface with no `uses` at all

The half that says this is about ALIASES and not about associated types. `interface Sink` binds nothing, so
the substitution has nothing to do and the EXPANSION is the whole of the work — which is why the check's
fast path asks two questions rather than one (`collectInterfaceMethods`). It was equally broken:
`expected write(data Sink.Bytes)` against the conformer's `Array_Byte`.

<!-- test: w61.interface-alias-over-a-concrete-type -->
```maxon
typealias Byte = int(0 to 255)

interface Sink
	typealias Bytes = Array with Byte
	function write(data Bytes)
end 'Sink'

typealias Buf = Array with Byte

type Log implements Sink
	var kept as Buf = Buf.create()

	export static function create() returns Self
		return Self{}
	end 'create'

	export function write(data Buf)
		kept = data.clone()
	end 'write'

	export function count() returns Byte
		return kept.count()
	end 'count'
end 'Log'

function main() returns ExitCode
	var l = Log.create()
	var b = Buf.create()
	b.push(7)
	l.write(b)
	print("{l.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```


### …and the expansion descends into a NESTED instance argument

`typealias Nested = Array with (Array with Element)` expands as far as it is written — the substitution
recurses through the inner instance rather than stopping at the outer one. It is the case that says the
expansion works on the alias's TYPE and not on a one-level spelling.

<!-- test: w61.expansion-descends-into-a-nested-instance-argument -->
```maxon
typealias Num = int(0 to 1000)

interface Holder uses Element
	typealias Nested = Array with (Array with Element)
	function absorb(value Nested)
end 'Holder'

typealias NumStore = Array with Num
typealias NumStoreStore = Array with NumStore

type Crate implements Holder with Num
	var items as NumStoreStore = NumStoreStore.create()

	export static function create() returns Self
		return Self{}
	end 'create'

	export function absorb(value NumStoreStore)
		items = value.clone()
	end 'absorb'

	export function count() returns Num
		return items.count()
	end 'count'
end 'Crate'

function main() returns ExitCode
	var c = Crate.create()
	var outer = NumStoreStore.create()
	var inner = NumStore.create()
	inner.push(3)
	outer.push(inner)
	c.absorb(outer)
	print("{c.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```


### …and an alias naming ANOTHER alias of the same interface is still refused, at the parse

⚖ **MEASURED, and it is why this rung needed no fixpoint.** An interface alias's arguments are read by the
same `readGenericInstanceAlias` a file-scope one is, and a bare member name is not resolvable there — so the
expansion can only ever descend into instances spelled INLINE, which are strictly smaller than their parent.
The refusal below is pre-existing and unchanged by W61; it is committed so that a later rung which makes
this shape legal has to decide the fixpoint deliberately rather than discover it as a hang.

<!-- test: error.w61.interface-alias-naming-another-interface-alias -->
```maxon
typealias Num = int(0 to 1000)

interface Holder uses Element
	typealias ElementArray = Array with Element
	typealias Nested = Array with ElementArray
	function absorb(value Nested)
end 'Holder'

type Crate implements Holder with Num
	export static function create() returns Self
		return Self{}
	end 'create'

	export function absorb(value Num)
		print("{value}\n")
	end 'absorb'
end 'Crate'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: specs/fragments/associated-types/error.w61.interface-alias-naming-another-interface-alias.test:6:32: Unknown type 'Holder.ElementArray'
```


### …and a genuinely wrong signature under an EXPANDED alias names the EXPANDED type

⛔ **W61's twin of the W60 message case, and it pins the same hazard.** The expansion arrives at the ONE
substitution site (`collectInterfaceMethods`), so the string the verdict compares and the string the
diagnostic prints are one string. A fix that expanded for the comparison and not for the message would
leave this program correctly rejected while telling its author the requirement is `Holder.ElementArray` — a
type their program cannot write, and one no positive case can see.

<!-- test: error.w61.wrong-signature-under-an-expanded-alias-names-the-expanded-type -->
```maxon
typealias Num = int(0 to 1000)
typealias Other = int(0 to 7)

interface Holder uses Element
	typealias ElementArray = Array with Element
	function absorb(value ElementArray)
end 'Holder'

typealias OtherStore = Array with Other

type Crate implements Holder with Num
	var items as OtherStore = OtherStore.create()

	export static function create() returns Self
		return Self{}
	end 'create'

	export function absorb(value OtherStore)
		items = value.clone()
	end 'absorb'
end 'Crate'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/error.w61.wrong-signature-under-an-expanded-alias-names-the-expanded-type.test:12:6: Partial interface implementation: type 'Crate' has 1 method(s) with wrong signature:
  - absorb(value Array_Other) returns void (expected absorb(value Array_Num) returns void)
```
