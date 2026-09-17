---
title: Composite Types
description: Structs, methods, interfaces, extensions, conditional conformance, and tuples.
sidebar:
  order: 4
---

## Composite Types

A `type` declares a record with named fields and methods. Every value of a type is a reference to a
heap record (see [Memory Model](/docs/language/memory-model/#memory-model)).

### Declaration

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord
	export var y as Coord

	export static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let p = Point.create(10, y: 20)
	print("{p.x}, {p.y}\n")
	return 0
end 'main'
```

- `var` fields can be written after construction; `let` fields cannot.
- A field's type is written after `as` and is an alias, `bool`, a type, or another named type — never a bare
  `int` or `float`.
- **Fields are private to the type** unless marked `export`, `module` or `public`. The boundary is the type,
  not the file: another type in the same file reading a private field is **E3014** (`cannot access
  unexported field`). A type's own methods may read the private fields of any instance of that type.
- A type cannot contain itself, directly or through other types: `var next as Node` inside `Node` is
  **E4014** (`recursive type references are not allowed`). Use a container such as `Array with Node` or an
  index.

### Construction

A type literal `Point{x: 1, y: 2}` — or `Self{...}` — is legal only inside the type's own body. Code elsewhere
constructs through a `static` factory, which is where invariants are established. A literal outside the
type is **E3076** (`type 'Point' can only be constructed from within its own methods; use a static factory
method instead`).

### Required Field Initialization

Every field must have a value when a record is constructed. A field is initialized when:

1. **The declaration supplies a default.** `var count = 0` infers the type from an integer, float or
   `true`/`false` literal. `var items as IntArray = IntArray.create()` gives the type explicitly and may use
   any expression — an enum case, a factory call — evaluated each time a literal omits the field.
2. **The literal supplies it**: `Counter{value: 5}`. A supplied value always wins over the default.
3. **A static factory assigns it** with `self.field = expr` on every path before `return Self{}` (or the
   type's own literal).

A literal that leaves a field uninitialized is **E3086**, listing the fields.

```maxon
typealias Tally = int(0 to u64.max)

type Counter
	export var value as Tally
	export var version = 0

	export static function create(initial Tally) returns Self
		self.value = initial     // rule 3
		return Self{}            // version comes from its default
	end 'create'
end 'Counter'
```

Rule 3 requires **definite assignment**: a write on only one branch of an `if`, or only inside a loop body,
does not count.

### Methods

Methods are declared inside the type body. Inside a method, fields and sibling methods are reachable by
bare name; `self` names the receiver explicitly.

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord
	export var y as Coord

	export static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'

	export function add(other Point) returns Point
		return Point.create(x + other.x, y: y + other.y)
	end 'add'

	export function manhattan() returns Coord
		return magnitudeOf(x) + magnitudeOf(y)    // sibling call: self.magnitudeOf(x)
	end 'manhattan'

	function magnitudeOf(v Coord) returns Coord
		return v if v >= 0 else -v
	end 'magnitudeOf'
end 'Point'

function main() returns ExitCode
	let p = Point.create(3, y: -4).add(Point.create(1, y: 1))
	print("{p.manhattan()}\n")     // 7
	return 0
end 'main'
```

- A method is private to its file unless marked `export`, `module` or `public`; its visibility does not
  inherit from the type's. Calling a non-exported method from another file is **E3008**.
- A local declaration inside a method (a `let`/`var`, parameter, pattern binding or loop variable) may not
  reuse a field's name (**E3006**).
- `self` cannot be bound by any declaration (**E2051** / **E2010**).

### Static Methods

A `static function` belongs to the type rather than an instance. It has no `self` and is called as
`Type.method()`. Factories are static methods.

| | Instance method | Static method |
|---|---|---|
| Declaration | `function name()` | `static function name()` |
| Receiver | `self` (implicit) | none |
| Call | `value.name()` | `Type.name()` |

A type may declare a static and an instance method with the same name; `Type.name()` calls the static one
and `value.name()` the instance one.

### Static Fields

`static var` and `static let` declare one value per type, read and written as `Type.name`:

```maxon
typealias Byte = int(0 to u8.max)

type Limits
	static let MAX_COUNT = 1000
	static let SEPARATOR = 61 as Byte
	static var used = 2
end 'Limits'

function main() returns ExitCode
	Limits.used = Limits.used + 1
	print("{Limits.MAX_COUNT} {Limits.SEPARATOR} {Limits.used}\n")   // 1000 61 3
	return 0
end 'main'
```

- A static field needs an initializer. Assigning to a `static let` is **E2013**; naming a static the type
  does not declare is **E2073**.
- An initializer may be a literal, a factory call (`static var shared = Cache.create()`), a literal of the
  enclosing type, or an array literal. A literal of *another* type is **E3076** — call its factory.
- **Initializers run before `main`, exactly once**, whether or not the field is ever read, in dependency
  order rather than declaration order. Initializers that depend on each other in a cycle are **E2012**.
- A `let` without `static` in a type body is an ordinary field with a default.

### Equality and Copying

- `a == b` and `a != b` on two records call the type's own `equals(other)` method, which must return `bool`.
  Declaring `implements Equatable` is not required. A type with no `equals` cannot be compared: **E3005**
  (`cannot compare struct with struct`). Ordering (`<`) on records is refused the same way.
- `a is b` asks whether two names refer to the **same record**
  (see [Reference Identity](/docs/language/expressions/#reference-identity-operators)).
- A type whose fields are all cloneable gets `clone()` automatically, producing an independent deep copy
  (see [Memory Model](/docs/language/memory-model/#explicit-cloning)).

### Interfaces

An interface lists method signatures a type promises to provide:

```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Square implements Shape
	export let side as Tally

	export static function create(side Tally) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Tally
		return side * side
	end 'area'
end 'Square'

function describe(item Shape) returns String
	return "area {item.area()}"
end 'describe'

function main() returns ExitCode
	print("{describe(Square.create(3))}\n")    // area 9
	return 0
end 'main'
```

- A type lists every interface it conforms to: `type Foo implements A, B`. A missing or mis-typed method is
  **E3016**.
- `Self` in a requirement means the conforming type.
- A requirement may be `static function`; the conforming type provides it as a static method.
- `interface Derived extends Base` inherits `Base`'s requirements; a type implementing `Derived` provides
  both, and satisfies parameters typed `Base`.

**Interface-typed parameters, returns and fields.** An interface can be used as a type, as `describe`
does above: a parameter typed `Shape` accepts any conforming type, a function may return an interface,
and a field may hold one. A struct literal (`Self{shape: Square.create(3)}`) or an assignment
(`self.shape = Strip.create(5)`) stores a conforming value straight into such a field, exactly as passing it
to a `Shape` parameter would; assigning a different conformer releases the one the field held. A value that
does not conform is **E3005**, and a conformer whose associated-type binding contradicts the field's
`with` clause is **E3127**. Where the compiler can see the concrete type, calls dispatch statically;
otherwise they dispatch through a witness table at run time.

**The standard interfaces** (declared in the standard library):

| Interface | Requirement |
|-----------|-------------|
| `Equatable` | `function equals(other Self) returns bool` |
| `Hashable` | `function hash() returns HashValue` |
| `Comparable` | `function compare(other Self) returns Ordering` |
| `Cloneable` | `function clone() returns Self` |
| `Stringable` | `function toString() returns String` |
| `FormattedStringable` | `function toString(format String) returns String` |
| `Error` | none — a marker for thrown values |
| `Iterable uses Element, Iter` | `function createIterator() returns Iter throws IterationError` |
| `Parsable` | `static function fromString(input String) returns Self throws Error` |

A type used in string interpolation needs a `toString()` method; one used with a format specifier
(`{value:spec}`) needs `toString(format String)`.

### Hashable and Hasher

A type used as a `Map` key or `Set` element implements `Hashable` and `Equatable`:

```maxon
typealias Score = int(i64.min to i64.max)

type Cell implements Hashable, Equatable
	export let row as Score
	export let col as Score

	static function create(row Score, col Score) returns Self
		return Self{row: row, col: col}
	end 'create'

	function hash() returns HashValue
		return (row * 31 + col) and 0xFFFFFFFF
	end 'hash'

	function equals(other Self) returns bool
		return row == other.row and col == other.col
	end 'equals'
end 'Cell'

typealias CellNames = Map with (Cell, String)

function main() returns ExitCode
	var names = CellNames.create()
	names.upsert(Cell.create(1, col: 2), value: "b1")
	let found = try names.get(Cell.create(1, col: 2)) otherwise "missing"
	print("{found}\n")     // b1
	return 0
end 'main'
```

Payload-free enums are `Hashable` and `Equatable` automatically; unions are not (see
[Unions](/docs/language/enums-unions/#comparing-union-values)).

For a digest over bytes, the standard library's `Hasher` is an incremental 64-bit FNV-1a fold that
returns a `HashDigest` (`bits(64)`):

```maxon
function main() returns ExitCode
	var hasher = Hasher.create()
	hasher.combine("abc".toByteArray())
	print("{hasher.finalize():016x}\n")     // e71fa2190541574b
	return 0
end 'main'
```

The fold carries no length tag — combining `"ab"` then `"c"` equals combining `"abc"` — so combine each
part's length yourself when the boundaries matter. `Hasher.empty()` and `Hasher.combined(state, bytes:)`
are the same fold as pure functions.

### Parsable

`Parsable` is the interface for types that parse themselves from text. `int`, `float` and `bool` provide
`fromString`, which throws `ParseError.invalidFormat`:

```maxon
function main() returns ExitCode
	let n = try int.fromString("42") otherwise 0
	let f = try float.fromString("2.5") otherwise 0.0
	print("{n} {f}\n")     // 42 2.5
	return 0
end 'main'
```

A type conforms with a static `fromString` that throws an error type:

```maxon
typealias Cents = int(0 to i64.max)

enum MoneyError implements Error
	negative
end 'MoneyError'

type Money implements Parsable
	export let cents as Cents

	static function fromString(input String) returns Self throws MoneyError
		let value = try int.fromString(input) otherwise throw MoneyError.negative
		if value < 0 'negative'
			throw MoneyError.negative
		end 'negative'

		return Self{cents: value as Cents}
	end 'fromString'
end 'Money'
```

A `fromString` that does not throw, or throws a type that is not an error, is **E3016**.

### Initializing From Literals

A type can be written from a string or character literal with `Type from "…"`, which calls its
`static function init(value String) returns Self`. The type must declare the conformance:

| Interface | Literal | Requirement |
|-----------|---------|-------------|
| `InitableFromStringLiteral` | `T from "text"` | `static function init(value String) returns Self` |
| `InitableFromCharLiteral` | `T from 'c'` | `static function init(value Character) returns Self` |

```maxon
type Wrapper implements InitableFromStringLiteral
	export let value as String

	static function init(value String) returns Wrapper
		return Wrapper{value: value}
	end 'init'
end 'Wrapper'

let greeting = Wrapper from "hello"

function main() returns ExitCode
	print("{greeting.value}\n")
	return 0
end 'main'
```

`Type from "…"` is also allowed as a module-level initializer. Using it on a type without the conformance is
**E3005** (`Type 'Wrapper' does not conform to InitableFromStringLiteral`).

The standard collections accept bracketed literals: a plain `[1, 2, 3]` is an `Array`, `["a": 1]` is a
`Map`, and `Array`, `Set`, `List` and `Vector` aliases take `Alias from [ ... ]`, for example
`CharSet from ['a', 'e']`; the alias states the element type, and a `Vector` alias's literal must write
exactly as many elements as the alias holds. A type that declares `implements InitableFromArrayLiteral with
Element` takes `Type from [ ... ]`, which is `Type.init(value ElementArray)` over the literal:

```maxon
typealias Digit = int(0 to 9)
typealias DigitArray = Array with Digit
typealias Number = int(0 to i64.max)

type Digits implements InitableFromArrayLiteral with Digit
	export var value as Number

	static function init(digits DigitArray) returns Self
		var total = 0 as Number

		for d in digits 'each'
			total = total * 10 + d
		end 'each'

		return Self{value: total}
	end 'init'
end 'Digits'

function main() returns ExitCode
	let n = Digits from [4, 0, 7]
	print("{n.value}\n")
	return 0
end 'main'
```

### Generic Types

`uses` declares type parameters. A generic type is instantiated with `with`, through a typealias:

```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var item as T

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function get() returns T
		return item
	end 'get'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	let b = IntBox.create(42)
	let inferred = Box.create("hi")        // Box with String, from the argument
	print("{b.get()} {inferred.get()}\n")  // 42 hi
	return 0
end 'main'
```

- Several parameters: `type Pair uses A, B`, instantiated `Pair with (Integer, String)`.
- A static factory called on the bare generic name infers the arguments from its parameters.
- One compiled body serves every instantiation.
- Wrong argument count is **E2056**; a bare `int` argument is **E2061**; a `float` type argument is not
  yet supported (**E2062**); a type argument to a non-generic type is **E2055**.

### Where Clauses

`where` constrains a type parameter to conform to interfaces, which lets the body call their methods:

```maxon
typealias Code = int(i64.min to i64.max)

interface Digest
	function digest() returns Code
end 'Digest'

type Tagged uses T where T is Digest
	export var item as T

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function itemDigest() returns Code
		return item.digest()
	end 'itemDigest'
end 'Tagged'
```

- Several interfaces on one parameter: `where T is Hashable and Equatable`.
- Several parameters: `where K is Hashable, V is Cloneable`.
- An instantiation whose argument does not conform is **E3017** (`Type 'X' does not satisfy constraint
  'Digest' required by type parameter 'T' of 'Tagged'`).
- A constraint can bind an associated type: `where S is Cursor with E`.

### Associated Types

An interface can declare associated types with `uses`; a conforming type binds them with `with`:

```maxon
typealias Slot = int(0 to u64.max)
typealias Score = int(i64.min to i64.max)

interface Container uses Element
	function at(index Slot) returns Element
end 'Container'

type Scores implements Container with Score
	export var base as Score

	export static function create(base Score) returns Self
		return Self{base: base}
	end 'create'

	function at(index Slot) returns Score
		return base + (index as Score)
	end 'at'
end 'Scores'
```

Several associated types bind positionally: `implements Iterable with (Entry, MapIterator)`. A missing
binding or a method whose types disagree with the binding is **E3016**.

### Parameterized Existentials

An alias over an interface with its associated types bound — `typealias IntegerSeq = Seq with Integer` —
is a value type that holds **any** conforming value and dispatches at run time:

```maxon
typealias Integer = int(i64.min to i64.max)

interface Seq uses Element
	function current() returns Element
	function advance() throws IterationError
end 'Seq'

type Upto implements Seq with Integer
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{pos: 1, limit: limit}
	end 'create'

	export function current() returns Integer
		return self.pos
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'

		self.pos = self.pos + 1
	end 'advance'
end 'Upto'

typealias IntegerSeq = Seq with Integer

function total(source IntegerSeq) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'

	return sum
end 'total'

function main() returns ExitCode
	print("{total(Upto.create(4))}\n")     // 10
	return 0
end 'main'
```

The `with` arguments are a claim checked against every conformer that reaches the existential; a conformer
binding the associated type differently is **E3125**.

### The `Self` Type

Inside a type, interface or extension body, `Self` names the enclosing type. It is legal in signatures
(`returns Self`), in literals (`Self{...}`), and as the base of a member expression: `Self.create(…)`,
`Self.someCase`, `Self.MAX` mean exactly what the type's own name would. `Self` outside a type declaration
is **E2015**.

### Interface Extensions

`extension InterfaceName` adds methods to **every** type that conforms to the interface, with one
implementation:

```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

extension Shape
	function doubledArea() returns Tally
		return self.area() * 2
	end 'doubledArea'
end 'Shape'
```

- `self` is the conforming value; the extension may call any requirement of the interface.
- Associated types resolve to each conformer's binding.
- Extensions of a parent interface apply to types conforming to a derived one.

### Conditional Extensions

A `where` clause restricts an extension to conformers whose associated types satisfy it. With the `Seq`
interface above:

```text
extension Seq where Element is Equatable
	function has(target Element) returns bool
		for item in self 'loop'
			if item == target 'found'
				return true
			end 'found'
		end 'loop'

		return false
	end 'has'
end 'Seq'
```

`Upto.create(4).has(3)` is available because `Integer` is `Equatable`. Several constraints on one
parameter join with `and`: `where Key is Hashable and Equatable`.

Conformers that do not satisfy the clause simply do not get the method; calling it on one is **E4006**,
which names the unmet constraint.

### Conditional Interface Conformance

An extension can also add a conformance under a condition:

```text
extension Array implements Hashable, Equatable where Element is Hashable and Equatable
	function hash() returns HashValue
		// ...
	end 'hash'

	function equals(other Self) returns bool
		// ...
	end 'equals'
end 'Array'
```

The constraint is checked wherever an instantiation is created: an explicit typealias
(`typealias IntArr = Array with Integer`), a bracketed literal (`[1, 2]`, `["a": 1]`), or a static factory
call on the bare generic name (`Box.create("hi")`). If `Integer` is `Hashable` and `Equatable`, `Array with
Integer` is too and can be a `Map` key; if not, an instantiation used where the conformance is required is
**E3017**.

### Extensions Over Primitives

`extension int`, `extension float` and `extension bool` add methods to a primitive, and may declare
conformances. Inside, `self` is the value and `Self` the primitive:

```maxon
typealias Integer = int(i64.min to i64.max)

extension int
	export function doubled() returns Integer
		return self * 2
	end 'doubled'
end 'int'

function main() returns ExitCode
	let n = 21
	print("{n.doubled()}\n")     // 42
	return 0
end 'main'
```

A `hash`, `equals`, `compare`, `toString` or `clone` you declare in such an extension replaces the built-in
one. An extension body cannot declare stored `var`/`let` members (**E2015**).

## Tuples

A tuple is a fixed-size, ordered group of values that may have different types. Tuples are written with
parentheses, both as values and as types.

```maxon
typealias Amount = int(i64.min to i64.max)

function sum(t (Amount, Amount)) returns Amount
	return t.0 + t.1
end 'sum'

function makePair(a Amount, b Amount) returns (Amount, Amount)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	var t = (0, 0)
	t.0 = 20
	t.1 = 22
	print("{sum(t)}\n")                      // 42

	let mixed = (42, "hello")
	print("{mixed.0} {mixed.1}\n")           // 42 hello

	let (x, y) = makePair(10, b: 32)
	print("{x} {y}\n")                       // 10 32
	return 0
end 'main'
```

### Literals and Element Access

- `(a, b)` builds a tuple; a tuple has at least two elements. `(expr)` is just a parenthesized expression.
- Elements are read and written by position: `t.0`, `t.1`, `t.2`.
- A tuple type in a signature is a parenthesized type list: `(Amount, String)`. It can also be named:
  `typealias Span = (Amount, Amount)`.

### Destructuring Declarations

`let (a, b) = expr` and `var (a, b) = expr` bind each element to a new name. `_` discards an element:

```maxon
let (first, _) = makePair(1, b: 2)
```

### Tuple Assignment

A tuple pattern on the left of `=` assigns to existing `var`s, and may declare new names in the same pattern:

```maxon
var p = 0
var q = 0
(p, q) = makePair(1, b: 2)       // both existing
(p, let r) = makePair(3, b: 4)   // p existing, r newly declared
(q, _) = makePair(5, b: 6)       // discard the second element
```

- A name without `var`/`let` must already be a `var`; a `let` target is **E2013**.
- The number of names must match the element count (**E3005**).
- Discarding every element of a pure function's result is **E3064**.

### Destructuring in For Loops

A `for` loop over tuples can destructure each one — a `Map` yields `(key, value)` pairs:

```maxon
let ages = ["ada": 36, "alan": 41]
var total = 0
for (_, age) in ages 'loop'
	total = total + age
end 'loop'
```

### Memory Semantics

A tuple is a reference-counted record, like a `type`. Assigning a tuple shares it; a tuple holding managed
values (strings, records) releases them when the last reference goes away.
