---
feature: extension-over-enum
status: stable
keywords: [extension, enum, union, method, implements, self, match, static, visibility]
category: type-system
---

# An `extension` whose target is an `enum` or a `union`

## Documentation

An `extension` of an `enum` or a `union` adds methods exactly as an extension of a `type` does. The methods
are the enum's own at every call site, `self` in the body is the case value, and `implements` in the header
adds a conformance that is checked and dispatched as a type extension's is.

```maxon
typealias Width = int(0 to 9)

enum Suite
	hearts
	spades
end 'Suite'

extension Suite
	function width() returns Width
		return 3
	end 'width'
end 'Suite'
```

An extension adds behaviour and never storage, and an enum or union body has no `static function`, so an
extension of one refuses `static` as the body does. A name that is not a declared type, enum, union,
interface or primitive is not an extension target.

## Tests

<!-- test: an-extension-adds-a-method-to-an-enum -->
The headline case: the declaration, the extension and the call share a file.
```maxon
typealias Width = int(0 to 9)

enum Suite
	a
	b
end 'Suite'

extension Suite
	function width() returns Width
		return 3
	end 'width'
end 'Suite'

function main() returns ExitCode
	return Suite.a.width() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: an-extension-adds-a-method-to-a-union -->
A union with payloads: the method matches on `self` and binds each case's payload.
```maxon
typealias Measure = int(0 to 1000)

union Shape
	circle(radius Measure)
	square(side Measure)
	empty
end 'Shape'

extension Shape
	function span() returns Measure
		return match self 'which'
			circle(radius) gives radius + radius
			square(side) gives side
			empty gives 0
		end 'which'
	end 'span'
end 'Shape'

function main() returns ExitCode
	let c = Shape.circle(20)
	let s = Shape.square(2)
	let e = Shape.empty
	return (c.span() + s.span() + e.span()) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: an-enum-extension-method-calls-the-enums-own-method-and-matches-self -->
The extension body reaches the enum's own method by `self.` and by a bare sibling call, and inspects `self`
with `match`.
```maxon
enum Direction
	north
	south

	function opposite() returns Direction
		return match self 'flip'
			north gives Self.south
			south gives Self.north
		end 'flip'
	end 'opposite'
end 'Direction'

extension Direction
	function code() returns ExitCode
		return match self 'which'
			north gives 10
			south gives 20
		end 'which'
	end 'code'

	function oppositeCode() returns ExitCode
		return self.opposite().code()
	end 'oppositeCode'

	function bareOppositeCode() returns ExitCode
		return opposite().code() + 1
	end 'bareOppositeCode'
end 'Direction'

function main() returns ExitCode
	return Direction.north.oppositeCode() + Direction.south.bareOppositeCode() - 10
end 'main'
```
```exitcode
21
```

<!-- test: an-enum-extension-names-the-enum-and-its-cases-through-self-type -->
`Self` in an extension body is the enum, in a return type and as the qualifier of a case, for an enum and for
a union that builds a value.
```maxon
enum Direction
	north
	south
end 'Direction'

extension Direction
	function other() returns Self
		return match self 'flip'
			north gives Self.south
			south gives Self.north
		end 'flip'
	end 'other'
end 'Direction'

union Shape
	dot
	line(length ExitCode)
end 'Shape'

extension Shape
	function longer() returns Self
		return match self 'which'
			dot gives Self.line(1)
			line(length) gives Self.line(length + 1)
		end 'which'
	end 'longer'

	function size() returns ExitCode
		return match self 'which'
			dot gives 0
			line(length) gives length
		end 'which'
	end 'size'
end 'Shape'

function main() returns ExitCode
	let flipped = match Direction.north.other() 'which'
		north gives 100
		south gives 40
	end 'which'

	return (flipped + Shape.dot.longer().longer().size()) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: an-enum-extension-method-reads-the-case-name-and-ordinal -->
`self.name` and `self.ordinal` read the case in an extension body as they do in the enum's own.
```maxon
enum Planet
	mercury
	venus
	earth
end 'Planet'

extension Planet
	function label() returns String
		return "{self.name}#{self.ordinal}"
	end 'label'
end 'Planet'

function main() returns ExitCode
	print(Planet.earth.label())
	return 0
end 'main'
```
```stdout
earth#2
```

<!-- test: a-method-of-an-enum-extension-is-a-method-of-the-enum-for-every-case-of-a-loop -->
Every case of `allCases` has the extension's method.
```maxon
enum Level
	low = 1
	mid = 5
	high = 9
end 'Level'

extension Level
	function twice() returns ExitCode
		return (self.rawValue * 2) as ExitCode
	end 'twice'
end 'Level'

function main() returns ExitCode
	var total = 0 as ExitCode

	for level in Level.allCases 'eachLevel'
		total = total + level.twice()
	end 'eachLevel'

	return total
end 'main'
```
```exitcode
30
```

<!-- test: an-enum-extension-in-another-file-of-the-directory-adds-a-method -->
The extension sits in a file of its own, and the method is `export` because the call is in a third file.
```maxon
// --- file: suite.maxon
export enum Suite
	a
	b
end 'Suite'

// --- file: widths.maxon
extension Suite
	export function width() returns ExitCode
		return match self 'which'
			a gives 4
			b gives 7
		end 'which'
	end 'width'
end 'Suite'

// --- file: main.maxon
function main() returns ExitCode
	return Suite.a.width() + Suite.b.width()
end 'main'
```
```exitcode
11
```

<!-- test: a-union-extension-in-another-file-of-the-directory-adds-a-method -->
```maxon
// --- file: shape.maxon
export union Shape
	dot
	line(length ExitCode)
end 'Shape'

// --- file: lengths.maxon
extension Shape
	export function length() returns ExitCode
		return match self 'which'
			dot gives 1
			line(length) gives length
		end 'which'
	end 'length'
end 'Shape'

// --- file: main.maxon
function main() returns ExitCode
	return Shape.dot.length() + Shape.line(40).length()
end 'main'
```
```exitcode
41
```

<!-- test: an-enum-extension-in-another-directory-adds-a-method -->
`extension sub.Suite` extends the enum `sub/` declares, and the call goes through the same chain.
```maxon
// --- file: sub/suite.maxon
export enum Suite
	a
	b
end 'Suite'

// --- file: main.maxon
extension sub.Suite
	function width() returns ExitCode
		return match self 'which'
			a gives 20
			b gives 22
		end 'which'
	end 'width'
end 'Suite'

function main() returns ExitCode
	return sub.Suite.a.width() + sub.Suite.b.width() - 20
end 'main'
```
```exitcode
22
```

<!-- test: an-enum-extension-method-of-another-directory-is-called-from-a-third-file -->
The method is `export` and the call is in a file beside neither the enum nor the extension.
```maxon
// --- file: sub/suite.maxon
export enum Suite
	a
	b
end 'Suite'

// --- file: sub/widths.maxon
extension Suite
	export function width() returns ExitCode
		return match self 'which'
			a gives 20
			b gives 22
		end 'which'
	end 'width'
end 'Suite'

// --- file: main.maxon
function main() returns ExitCode
	return sub.Suite.b.width()
end 'main'
```
```exitcode
22
```

<!-- test: error.a-private-enum-extension-method-is-not-callable-from-another-file -->
An extension method is file-private without a visibility word, as a type's is.
```maxon
// --- file: suite.maxon
export enum Suite
	a
end 'Suite'

// --- file: widths.maxon
extension Suite
	function width() returns ExitCode
		return 3
	end 'width'
end 'Suite'

// --- file: main.maxon
function main() returns ExitCode
	return Suite.a.width()
end 'main'
```
```maxoncstderr
error E3008: <fragment>:16:17: function 'Suite.width' is not exported
```

<!-- test: an-enum-extension-declares-a-conformance-dispatched-through-an-interface -->
`extension Suite implements Describable` makes `Suite` a conformer. The same value is reached through a
generic function bound `T is Describable` and through an interface-typed parameter.
```maxon
interface Describable
	function describe() returns ExitCode
end 'Describable'

enum Suite
	hearts
	spades
end 'Suite'

extension Suite implements Describable
	function describe() returns ExitCode
		return match self 'which'
			hearts gives 10
			spades gives 30
		end 'which'
	end 'describe'
end 'Suite'

function viaGeneric(item T) uses T returns ExitCode where T is Describable
	return item.describe()
end 'viaGeneric'

function viaInterface(item Describable) returns ExitCode
	return item.describe() + 1
end 'viaInterface'

function main() returns ExitCode
	return viaGeneric(Suite.hearts) + viaInterface(Suite.spades)
end 'main'
```
```exitcode
41
```

<!-- test: a-union-extension-declares-a-conformance-dispatched-through-an-interface -->
```maxon
interface Describable
	function describe() returns ExitCode
end 'Describable'

union Reading
	absent
	value(amount ExitCode)
end 'Reading'

extension Reading implements Describable
	function describe() returns ExitCode
		return match self 'which'
			absent gives 0
			value(amount) gives amount
		end 'which'
	end 'describe'
end 'Reading'

function viaGeneric(item T) uses T returns ExitCode where T is Describable
	return item.describe()
end 'viaGeneric'

function viaInterface(item Describable) returns ExitCode
	return item.describe()
end 'viaInterface'

function main() returns ExitCode
	return viaGeneric(Reading.value(30)) + viaInterface(Reading.value(12)) + viaInterface(Reading.absent)
end 'main'
```
```exitcode
42
```

<!-- test: an-enum-extension-conformance-joins-the-enums-own -->
The enum's own `implements` and the extension's are a union, whichever is written first.
```maxon
interface Shower
	function show() returns ExitCode
end 'Shower'

interface Sizer
	function size() returns ExitCode
end 'Sizer'

extension Mode implements Sizer
	function size() returns ExitCode
		return 2
	end 'size'
end 'Mode'

enum Mode implements Shower
	fast
	slow

	function show() returns ExitCode
		return 20
	end 'show'
end 'Mode'

function display(s Shower) returns ExitCode
	return s.show()
end 'display'

function measure(s Sizer) returns ExitCode
	return s.size()
end 'measure'

function main() returns ExitCode
	return display(Mode.fast) + measure(Mode.slow)
end 'main'
```
```exitcode
22
```

<!-- test: error.an-enum-extension-that-leaves-a-requirement-unmet-is-refused -->
The conformance an extension declares is checked against the interface's requirements.
```maxon
interface Describable
	function describe() returns ExitCode
end 'Describable'

enum Suite
	hearts
end 'Suite'

extension Suite implements Describable
	function other() returns ExitCode
		return 1
	end 'other'
end 'Suite'

function main() returns ExitCode
	return Suite.hearts.other()
end 'main'
```
```maxoncstderr
error E3016: <fragment>:10:11: Partial interface implementation: type 'Suite' is missing 1 method(s):
  - describe() returns ExitCode
```

<!-- test: error.static-function-in-an-enum-extension -->
An enum has no `static function`, and an extension of one refuses it with the sentence the body gives.
```maxon
enum Color
	red
end 'Color'

extension Color
	static function best() returns ExitCode
		return 1
	end 'best'
end 'Color'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:2: Unsupported: a `static function` on `enum Color` (an INSTANCE method is supported — a static one has no receiver to name the enum through, and no `enum`/`union` in the corpus declares one)
```

<!-- test: error.static-function-in-a-union-extension -->
```maxon
union Shape
	circle(radius ExitCode)
end 'Shape'

extension Shape
	export static function unit() returns ExitCode
		return 1
	end 'unit'
end 'Shape'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:9: Unsupported: a `static function` on `union Shape` (an INSTANCE method is supported — a static one has no receiver to name the enum through, and no `enum`/`union` in the corpus declares one)
```

<!-- test: error.a-field-read-through-self-in-an-enum-extension -->
`self` in an enum extension is the case value, so a field read is the refusal the enum's own body gives.
```maxon
enum Color
	red
end 'Color'

extension Color
	function peek() returns ExitCode
		return self.x
	end 'peek'
end 'Color'

function main() returns ExitCode
	return Color.red.peek()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:10: Unsupported: a field access through `self` in a method of `enum`/`union` `Color` — an enum/union declares no fields; a case's PAYLOAD is bound by a pattern (`match self 'k' … fail(reason) then …`), never read through the receiver
```

<!-- test: error.an-enum-extension-method-repeats-an-enum-method -->
```maxon
enum Direction
	north

	function opposite() returns ExitCode
		return 1
	end 'opposite'
end 'Direction'

extension Direction
	function opposite() returns ExitCode
		return 2
	end 'opposite'
end 'Direction'

function main() returns ExitCode
	return Direction.north.opposite()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:11:11: Duplicate method 'opposite' in type 'Direction'
```

<!-- test: error.two-extensions-of-an-enum-declare-one-method -->
```maxon
enum Direction
	north
end 'Direction'

extension Direction
	function code() returns ExitCode
		return 1
	end 'code'
end 'Direction'

extension Direction
	function code() returns ExitCode
		return 2
	end 'code'
end 'Direction'

function main() returns ExitCode
	return Direction.north.code()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:13:11: Duplicate method 'code' in type 'Direction'
```

<!-- test: error.an-enum-extension-may-not-declare-storage -->
```maxon
enum Color
	red
end 'Color'

extension Color
	var extra as ExitCode
end 'Color'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:2: Unsupported: a `var` declaration in an `extension` body — an extension adds methods to types other declarations named and declares no storage of its own, so there is no type for this binding to belong to; declare it as a field of the `type` body
```

<!-- test: error.an-enum-extension-takes-no-where-clause -->
An enum declares no `uses` list, so there is no parameter for a `where` clause to constrain.
```maxon
enum Color
	red
end 'Color'

extension Color where T is Hashable
	function code() returns ExitCode
		return 1
	end 'code'
end 'Color'

function main() returns ExitCode
	return Color.red.code()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:23: Unsupported: a `where` clause constrains 'T', which is not a type parameter of this type — a constraint must name one of its `uses` parameters
```

<!-- test: error.an-extension-of-an-undeclared-name-is-refused -->
A name that no declaration holds is not an extension target, and the refusal names it at the `extension` line.
```maxon
extension Nonesuch
	function code() returns ExitCode
		return 1
	end 'code'
end 'Nonesuch'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:2:11: Unknown type 'Nonesuch'
```

<!-- test: error.an-extension-of-a-typealias-is-refused -->
A typealias names a range, not a declaration with methods of its own, so it is no extension target either.
```maxon
typealias Score = int(0 to 100)

extension Score
	function code() returns ExitCode
		return 1
	end 'code'
end 'Score'

function best(s Score) returns Score
	return s
end 'best'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:11: Unsupported: `extension Score` — 'Score' is a typealias, which declares no methods of its own; extend the `type`, `enum`, `union`, `interface` or primitive it names
```

<!-- test: a-union-with-a-managed-payload-conforms-through-an-extension-and-is-dropped -->
A union with a `String` payload and no `implements` clause of its own is a word, and a conformer is never a
word. The extension's clause is the conformance, so the union is widened through a witness and dropped as a
box (a mismatch is a leak, exit 101, or a crash).
```maxon
interface Describable
	function describe() returns ExitCode
end 'Describable'

union Note
	blank
	text(body String)
end 'Note'

extension Note implements Describable
	function describe() returns ExitCode
		return match self 'which'
			blank gives 0
			text(body) gives body.byteLength() as ExitCode
		end 'which'
	end 'describe'
end 'Note'

function measure(item Describable) returns ExitCode
	return item.describe()
end 'measure'

function main() returns ExitCode
	return measure(Note.text("hello")) + measure(Note.blank)
end 'main'
```
```exitcode
5
```

<!-- test: a-union-extension-conformance-in-another-file-keeps-the-union-a-box -->
The same program with the union, the extension and the caller in three files.
```maxon
// --- file: note.maxon
export interface Describable
	function describe() returns ExitCode
end 'Describable'

export union Note
	blank
	text(body String)
end 'Note'

// --- file: notes.maxon
extension Note implements Describable
	function describe() returns ExitCode
		return match self 'which'
			blank gives 0
			text(body) gives body.byteLength() as ExitCode
		end 'which'
	end 'describe'
end 'Note'

// --- file: main.maxon
function measure(item Describable) returns ExitCode
	return item.describe()
end 'measure'

function main() returns ExitCode
	return measure(Note.text("hello")) + measure(Note.blank)
end 'main'
```
```exitcode
5
```

<!-- test: an-enum-extension-may-declare-the-error-conformance -->
`extension Failure implements Error` is the enum's `Error` conformance, so an implementation of an abstract
`throws Error` requirement may narrow to it, and the error is thrown and caught.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Failure
	empty
end 'Failure'

extension Failure implements Error
	function code() returns Integer
		return 42
	end 'code'
end 'Failure'

type Value implements Parsable
	export var n as Integer

	static function fromString(input String) returns Self throws Failure
		if input.byteLength() == 0 'check'
			throw Failure.empty
		end 'check'

		return Value{n: input.byteLength() as Integer}
	end 'fromString'
end 'Value'

function main() returns ExitCode
	let v = try Value.fromString("") otherwise 'err'
		return Failure.empty.code() as ExitCode
	end 'err'

	return v.n as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: an-enum-conformer-through-an-extension-writing-a-let-argument-is-written -->
The requirement's `mutable` reaches an enum conformer that an extension declares, exactly as it reaches one
its own clause declares (`enum-interface-conformance.md`). A `let` forbids only reassigning `a`, so the push
lands in the array it names and the read finds `9`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

interface Grower
	function grow(dest mutable IntArray)
end 'Grower'

enum Step
	one
	two
end 'Step'

extension Step implements Grower
	function grow(dest mutable IntArray)
		dest.push(9)
	end 'grow'
end 'Step'

function through(g Grower, dest IntArray)
	g.grow(dest)
end 'through'

function main() returns ExitCode
	let a = IntArray.create(0)
	through(Step.one, dest: a)
	return try a.get(0) otherwise 55
end 'main'
```
```exitcode
9
```

<!-- test: an-enum-conformer-through-an-extension-writing-a-var-argument-is-allowed -->
The twin of the case above: the same program with `var a` answers 9.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

interface Grower
	function grow(dest mutable IntArray)
end 'Grower'

enum Step
	one
	two
end 'Step'

extension Step implements Grower
	function grow(dest mutable IntArray)
		dest.push(9)
	end 'grow'
end 'Step'

function through(g Grower, dest IntArray)
	g.grow(dest)
end 'through'

function main() returns ExitCode
	let a = IntArray.create(0)
	through(Step.one, dest: a)
	return try a.get(0) otherwise 55
end 'main'
```
```exitcode
9
```

<!-- test: error.an-enum-conformer-through-an-extension-writing-an-unmarked-parameter-is-refused -->
An implementation that writes a parameter its requirement does not declare `mutable` is refused for an
extension-declared enum conformer as it is for one the enum's own clause declares.
```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var v as Integer

	static function create() returns Self
		return Self{v: 0}
	end 'create'
end 'Cell'

interface Taker
	function take(item Cell)
end 'Taker'

enum Slot
	one
end 'Slot'

extension Slot implements Taker
	function take(item Cell)
		item.v = 5
	end 'take'
end 'Slot'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3210: <fragment>:21:11: 'Slot.take' writes its parameter 'item', but 'Taker.take' does not declare it `mutable`
```

<!-- test: an-enum-extension-conformance-satisfies-a-conditional-type-extension -->
`extension Container where Element is Sizer` is available on `Container with Suite` because
`extension Suite implements Sizer` makes `Suite` a `Sizer`.
```maxon
interface Sizer
	function size() returns ExitCode
end 'Sizer'

enum Suite
	hearts
	spades
end 'Suite'

extension Suite implements Sizer
	function size() returns ExitCode
		return match self 'which'
			hearts gives 10
			spades gives 30
		end 'which'
	end 'size'
end 'Suite'

type Container uses Element
	var item as Element

	static function create(item Element) returns Self
		return Self{item: item}
	end 'create'
end 'Container'

extension Container where Element is Sizer
	function total() returns ExitCode
		return self.item.size() + 2
	end 'total'
end 'Container'

typealias SuiteContainer = Container with Suite

function main() returns ExitCode
	let c = SuiteContainer.create(Suite.spades)
	return c.total()
end 'main'
```
```exitcode
32
```

<!-- test: an-enum-extension-conformance-satisfies-a-conditional-interface-extension -->
`extension HasItems where Element is Sizer` is available on a conformer that binds `Element` to `Suite`, an
enum whose `Sizer` conformance an extension declares.
```maxon
interface Sizer
	function size() returns ExitCode
end 'Sizer'

enum Suite
	hearts
	spades
end 'Suite'

extension Suite implements Sizer
	function size() returns ExitCode
		return match self 'which'
			hearts gives 10
			spades gives 30
		end 'which'
	end 'size'
end 'Suite'

interface HasItems uses Element
	function current() returns Element
end 'HasItems'

extension HasItems where Element is Sizer
	function currentSize() returns ExitCode
		return self.current().size()
	end 'currentSize'
end 'HasItems'

type SuiteList implements HasItems with Suite
	let first as Suite

	static function create(first Suite) returns Self
		return Self{first: first}
	end 'create'

	function current() returns Suite
		return self.first
	end 'current'
end 'SuiteList'

function main() returns ExitCode
	let list = SuiteList.create(Suite.spades)
	return list.currentSize()
end 'main'
```
```exitcode
30
```

<!-- test: error.an-enum-extension-method-repeats-an-enum-method-declared-after-it -->
The refusal does not depend on which of the two declarations is written first.
```maxon
extension Direction
	function opposite() returns ExitCode
		return 2
	end 'opposite'
end 'Direction'

enum Direction
	north

	function opposite() returns ExitCode
		return 1
	end 'opposite'
end 'Direction'

function main() returns ExitCode
	return Direction.north.opposite()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:3:11: Duplicate method 'opposite' in type 'Direction'
```

<!-- test: error.an-enum-extension-method-repeats-a-method-of-an-enum-in-another-file -->
```maxon
// --- file: direction.maxon
export enum Direction
	north

	export function opposite() returns ExitCode
		return 1
	end 'opposite'
end 'Direction'

// --- file: extension.maxon
extension Direction
	export function opposite() returns ExitCode
		return 2
	end 'opposite'
end 'Direction'

// --- file: main.maxon
function main() returns ExitCode
	return Direction.north.opposite()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:13:18: Duplicate method 'opposite' in type 'Direction'
```

<!-- test: error.a-nested-typealias-in-an-enum-extension-is-refused -->
An enum body declares cases and methods and no nested `typealias`, so the extension of one declares no more.
```maxon
enum Color
	red
end 'Color'

extension Color
	typealias Tint = int(0 to 3)

	function tinted(tint Tint) returns ExitCode
		return 1
	end 'tinted'
end 'Color'

function main() returns ExitCode
	return Color.red.tinted(2)
end 'main'
```
```maxoncstderr
error E2010: <fragment>:7:2: Expected 'function' but got 'typealias'
```
