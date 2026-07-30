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


### A forward interface with NO `uses` clause has arity ZERO, and zero is an ANSWER

`Plain` declares no associated type, so its surplus `with Score` binds nothing (the rule the case below
states) and the comma after it opens the SIBLING `Duo` — whose own two `uses` names then take BOTH of the
remaining arguments. Every interface here is declared below the type, so the arity of each comes from the
whole-program sweep. It fails before R7 the way the two cases above do — `Duo` truncates at `Score` and
`Weight` is read as an interface — and it pins the arity-0 half: an interface the sweep recorded with NO
`uses` clause must not be handed the unresolvable default of 1, which is the number a genuinely undeclared
name gets.

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


### A `with` argument an interface declares no `uses` name for is IGNORED, not rejected

The two reference compilers disagree here — the bootstrap binds `min(names, args)` and drops the rest, v1
rejects the arity — and shv2 takes the bootstrap's answer. See `ConformanceCheck.checkOneInterfaceConformance`
for the argument in full; the short form is that a surplus rejection would also refuse
`implements Sub with Score` where `Sub extends` an interface whose `uses` name it inherits, a shape v1
supports and shv2 does not yet, so refusing it here would be preemptive.

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

