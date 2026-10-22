---
feature: sizeof
status: stable
keywords: sizeof, type size, memory, intrinsic
category: intrinsic
---
# sizeof

## Documentation

Returns the size of a type in bytes as a compile-time integer constant.

## Tests

<!-- test: sizeof.type-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Sizer uses T
	export var dummy as T

	static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	function typeSize() returns Integer
		return sizeof(T)
	end 'typeSize'
end 'Sizer'

typealias BoolSizer = Sizer with bool

function main() returns ExitCode
	let s = BoolSizer.create(false)
	return s.typeSize()
end 'main'
```
```exitcode
1
```

<!-- test: sizeof.type-parameter-struct -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var a as Integer
	export var b as Integer

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

type Sizer uses T
	export var dummy as T

	static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	function typeSize() returns Integer
		return sizeof(T)
	end 'typeSize'
end 'Sizer'

typealias PairSizer = Sizer with Pair

function main() returns ExitCode
	let s = PairSizer.create(Pair.create(0, b: 0))
	return s.typeSize()
end 'main'
```
```exitcode
16
```

<!-- test: sizeof.concrete -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var a as Integer
	export var b as Integer

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

function main() returns ExitCode
	return sizeof(bool) + sizeof(Integer) + sizeof(Pair)
end 'main'
```
```exitcode
25
```

<!-- test: sizeof.self-forward -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Sizer uses T
	export var dummy as T

	static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	function directSize() returns Integer
		return sizeof(T)
	end 'directSize'

	function indirectSize() returns Integer
		return self.directSize()
	end 'indirectSize'
end 'Sizer'

typealias BoolSizer = Sizer with bool

function main() returns ExitCode
	let s = BoolSizer.create(false)
	return s.indirectSize()
end 'main'
```
```exitcode
1
```

<!-- test: sizeof.transitive-two-hop -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var a as Integer
	export var b as Integer

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

type Sizer uses T
	export var dummy as T

	static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	function level0() returns Integer
		return sizeof(T)
	end 'level0'

	function level1() returns Integer
		return self.level0()
	end 'level1'

	function level2() returns Integer
		return self.level1()
	end 'level2'
end 'Sizer'

typealias PairSizer = Sizer with Pair

function main() returns ExitCode
	let s = PairSizer.create(Pair.create(0, b: 0))
	return s.level2()
end 'main'
```
```exitcode
16
```

<!-- test: sizeof.int -->
```maxon
function main() returns ExitCode
	return sizeof(int)
end 'main'
```
```exitcode
8
```

<!-- test: sizeof.float -->
```maxon
function main() returns ExitCode
	return sizeof(float)
end 'main'
```
```exitcode
8
```

<!-- test: sizeof.bool -->
```maxon
function main() returns ExitCode
	return sizeof(bool)
end 'main'
```
```exitcode
1
```

<!-- test: sizeof.byte -->
```maxon
function main() returns ExitCode
	return sizeof(byte)
end 'main'
```
```exitcode
1
```

<!-- test: sizeof.struct -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer
end 'Point'

function main() returns ExitCode
	return sizeof(Point)
end 'main'
```
```exitcode
16
```

<!-- test: sizeof.struct-three-fields -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Vec3
	export var x as Integer
	export var y as Integer
	export var z as Integer
end 'Vec3'

function main() returns ExitCode
	return sizeof(Vec3)
end 'main'
```
```exitcode
24
```

<!-- test: sizeof.enum -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	return sizeof(Color)
end 'main'
```
```exitcode
8
```

<!-- test: sizeof.arithmetic -->
```maxon
function main() returns ExitCode
	return sizeof(int) + sizeof(bool)
end 'main'
```
```exitcode
9
```

<!-- test: sizeof.let-binding -->
```maxon
function main() returns ExitCode
	let size = sizeof(int)
	return size
end 'main'
```
```exitcode
8
```

<!-- test: sizeof.the-corpus-byte-record-embeds-the-buffer-record -->
### A `managed` field of a builtin-literal conformer is embedded whole

THE ENVELOPE COLLAPSE. A type whose `implements` clause names one of `stdlib/Builtins.maxon`'s three
literal markers holds its `managed` `__ManagedMemory` INLINE — a whole 48-byte buffer record at offset 0,
six machine words — rather than as an 8-byte pointer to one. That is what makes the corpus's two-field
`type String` lay out as the 56-byte record the runtime reads: `managed` occupies 0..48 (`buffer@0`,
`length@8`, `capacity@16`, `element_size@24`, `parent@32`, `element_destroy@40`) and the flag lands at 48.

⚠ **THE SIXTH SLOT IS THE POINT, AND IT IS WHY THIS NUMBER IS 56 RATHER THAN 48.** An embedding that
stopped at `parent@32` would let the flag take `@40` — the slot a buffer record keeps `element_destroy` in — so
a String record and a buffer record would disagree at exactly one offset and a String's bytes could only be
handed to buffer code through a freshly minted view, one heap record per `addressableBytes()` call.
Embedding the buffer record WHOLE (a String's `element_destroy` is always 0: its elements are `Byte`s and
own nothing) makes a String record a valid buffer record, and handing its bytes over is an incref.

⚠ **THE SUBJECT IS THE CORPUS'S OWN `String`, AND IT HAS TO BE.** A user conformer would be the more
direct probe, but a marker conformer of any name the compiler
does not own the byte record for is refused at its `Self{…}`
(`Parser.requireFusedWrapperTag`), because the value it would produce carries a byte record's bytes under a
struct's identity and its drop, its clone and its own field reads each disagree with it. See
`interface-conformance/error.literal-marker-conformer-*` for the three programs. So the marker
conformers that remain are the two the corpus declares, and this reads one of them.

```maxon
function main() returns ExitCode
	return sizeof(String) as ExitCode
end 'main'
```
```exitcode
56
```

<!-- test: sizeof.a-plain-managed-field-is-still-a-pointer -->
<!-- tier: stdlib -->
### Without the marker, `managed` is an ordinary pointer field

The negative control, and it is the reason the rule is keyed on the CONFORMANCE and not on the field
name: pointer-valued `managed` fields exist (`StringIterator`'s, an `ArrayIter`'s cursor source), and
keying on the name alone would silently widen every one of them by 40 bytes.

```maxon
type Plain
	var managed as __ManagedMemory
	var flag as bool
end 'Plain'

function main() returns ExitCode
	return sizeof(Plain) as ExitCode
end 'main'
```
```exitcode
16
```

<!-- test: sizeof.through-a-Self-typed-parameter -->
### A `Self`-typed PARAMETER is a value of the enclosing type, and the descriptor forwards to it

`merge(other Self)` binds `other` to the same instance the receiver is, so `other.slotSize()`
reaches the same shared body `self.slotSize()` reaches and the caller forwards the descriptor
it already carries. The descriptor-need fixpoint records a parameter DECLARED `Self`, as it
does a local bound to a `Self{…}`, as a value of the enclosing type, so `pairWith` is reserved a
descriptor to forward; without that edge the call is refused — *"calling 'slotSize' … from a
function that carries no layout descriptor of its own to forward"*.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntBag = Bag with Int

type Bag uses Element
	typealias Items = Array with Element

	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function slotSize() returns Int
		return sizeof(Element)
	end 'slotSize'

	function pairWith(other Self) returns Int
		return other.slotSize()
	end 'pairWith'
end 'Bag'

function main() returns ExitCode
	var a = IntBag.create()
	var b = IntBag.create()
	return a.pairWith(b)
end 'main'
```
```exitcode
8
```

<!-- test: sizeof-in-a-static-reads-the-descriptor-of-the-instance-the-call-names -->
`sizeof(T)` in statics, one through an alias (`ByteCell`, `WideCell`) and one through a bare generic base whose argument fixes the instance (`Cell.widthOf(true)`). `width` is reached directly, through `Self.width` from a sibling static, and each answers the size of the instance its call names.
```maxon
typealias Byte = int(0 to 255)
typealias Wide = int(i64.min to i64.max)

type Cell uses T where T is Equatable
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	static function width(n Count) returns Count
		return sizeof(T) + n
	end 'width'

	static function widthOf(x T) returns Count
		let c = Self{v: x}
		return Self.width(0) + sizeof(T) + (0 if c.v == c.v else 0)
	end 'widthOf'
end 'Cell'

typealias ByteCell = Cell with bool
typealias WideCell = Cell with Wide

function main() returns ExitCode
	print("{sizeof(Byte)} {sizeof(Wide)} {sizeof(bool)} {sizeof(String)}\n")
	print("{ByteCell.width(0)} {WideCell.width(0)} {ByteCell.widthOf(true)} {WideCell.widthOf(1)} {Cell.widthOf(true)} {Cell.widthOf(1 as Wide)} {ByteCell.make(true).v} {WideCell.make(1).v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 8 1 56
1 8 2 16 2 16 true 1
```

<!-- test: sizeof-in-a-closure-in-a-static-reads-the-call-sites-descriptor -->
`sizeof` of a type parameter in a closure written in a static reads the descriptor the static took from its call site.
```maxon
typealias Int = int(i64.min to i64.max)

type Cell uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function sized(v T, n Int) returns Self
		let f = function() gives (sizeof(T) as Int) + n
		if f() != 9 'theSizeOfAnIntegerPlusOne'
			panic("the closure read the wrong size")
		end 'theSizeOfAnIntegerPlusOne'

		return Self{v: v}
	end 'sized'
end 'Cell'

typealias IntCell = Cell with Int

function main() returns ExitCode
	let c = IntCell.create(4)
	let d = IntCell.sized(2, n: 1)
	return (d.v + c.v) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: error.sizeof-is-a-count -->
`sizeof` produces a `Count`, which a parameter of another alias refuses.
```maxon
typealias Score = int(0 to u64.max)

function show(v Score)
	print("{v}\n")
end 'show'

function main() returns ExitCode
	show(sizeof(int))
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:2: argument type mismatch for 'v': expected 'Score', got 'Count'
```

### `sizeof` of each parameter of a two-parameter generic

Each parameter's block of the descriptor holds that parameter's own size.

<!-- test: sizeof-of-each-parameter-of-a-two-parameter-generic -->
```maxon
typealias Small = int(0 to 255)

type Holder uses T, U
	var tag as T
	var held as U

	static function create(t T, u U) returns Self
		return Self{tag: t, held: u}
	end 'create'

	function sizeOfFirst() returns Count
		return sizeof(T)
	end 'sizeOfFirst'

	function sizeOfSecond() returns Count
		return sizeof(U)
	end 'sizeOfSecond'

	function bothSizes() returns Count
		return sizeof(T) * 1000 + sizeof(U)
	end 'bothSizes'
end 'Holder'

typealias SmallStrHolder = Holder with (Small, String)

function main() returns ExitCode
	let h = SmallStrHolder.create(3, u: "alpha {40 + 2} value long enough for a heap record")
	print("{h.sizeOfFirst()} {h.sizeOfSecond()} {h.bothSizes()} {sizeof(Small)} {sizeof(String)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 56 8056 8 56
```

### `sizeof` of a two-parameter generic's parameter reached through forwarded descriptors

A sibling call forwards the whole descriptor, and a nested instance over a run of the caller's parameters forwards the block the run begins at.

<!-- test: sizeof-of-a-two-parameter-generic-through-forwarded-descriptors -->
```maxon
typealias Small = int(0 to 255)

type Probe uses A, B
	var a as A
	var b as B

	static function create(a A, b B) returns Self
		return Self{a: a, b: b}
	end 'create'

	function sizes() returns Count
		return sizeof(A) * 1000 + sizeof(B)
	end 'sizes'
end 'Probe'

type Holder uses T, U
	typealias Straight = Probe with (T, U)
	var straight as Straight

	static function create(t T, u U) returns Self
		return Self{straight: Straight.create(t, b: u)}
	end 'create'

	function sizeOfSecond() returns Count
		return sizeof(U)
	end 'sizeOfSecond'

	function viaSibling() returns Count
		return self.sizeOfSecond()
	end 'viaSibling'

	function viaNested() returns Count
		return self.straight.sizes()
	end 'viaNested'
end 'Holder'

typealias SmallStrHolder = Holder with (Small, String)

function main() returns ExitCode
	let h = SmallStrHolder.create(3, u: "alpha {40 + 2} value long enough for a heap record")
	print("{h.viaSibling()} {h.viaNested()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
56 8056
```

### `sizeof` of a two-parameter generic's parameter through a permuted instance, a closure and a static

<!-- test: sizeof-of-a-two-parameter-generic-through-a-permutation-a-closure-and-a-static -->
```maxon
typealias Small = int(0 to 255)

type Probe uses A, B
	var a as A
	var b as B

	static function create(a A, b B) returns Self
		return Self{a: a, b: b}
	end 'create'

	function sizes() returns Count
		return sizeof(A) * 1000 + sizeof(B)
	end 'sizes'
end 'Probe'

type Holder uses T, U
	typealias Swapped = Probe with (U, T)
	var swapped as Swapped

	static function create(t T, u U) returns Self
		return Self{swapped: Swapped.create(u, b: t)}
	end 'create'

	static function widthOfSecond(n Count) returns Count
		return sizeof(U) + n
	end 'widthOfSecond'

	function viaSwapped() returns Count
		return self.swapped.sizes()
	end 'viaSwapped'

	function viaClosure() returns Count
		let f = function() gives sizeof(U)
		return f()
	end 'viaClosure'
end 'Holder'

typealias SmallStrHolder = Holder with (Small, String)

function main() returns ExitCode
	let h = SmallStrHolder.create(3, u: "alpha {40 + 2} value long enough for a heap record")
	print("{h.viaSwapped()} {h.viaClosure()} {SmallStrHolder.widthOfSecond(1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
56008 56 57
```

### `sizeof` read by a call chained on a method's result and on an overloaded method's result

<!-- test: sizeof-read-through-a-call-chained-on-a-result -->
```maxon
typealias Small = int(0 to 65535)

type Inner uses T
	var v as T

	static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	function size() returns Count
		return sizeof(T)
	end 'size'
end 'Inner'

type Outer uses T
	typealias Held = Inner with T
	var held as Held

	static function create(x T) returns Self
		return Self{held: Held.create(x)}
	end 'create'

	function make() returns Held
		return held
	end 'make'

	function probe() returns Count
		return self.make().size()
	end 'probe'
end 'Outer'

typealias SmallOuter = Outer with Small
typealias BoolOuter = Outer with bool

function main() returns ExitCode
	let a = SmallOuter.create(7)
	let b = BoolOuter.create(true)
	print("{a.probe()} {b.probe()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 1
```

<!-- test: sizeof-read-through-an-overloaded-methods-result -->
```maxon
type Left uses A
	var n as Count

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function size() returns Count
		return sizeof(A)
	end 'size'
end 'Left'

type Right uses B
	var n as Count

	static function create() returns Self
		return Self{n: 2}
	end 'create'

	function size() returns Count
		return sizeof(B)
	end 'size'
end 'Right'

type Inner uses T
	typealias L = Left with T
	typealias R = Right with T
	var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function pick(_ Count) returns L
		return L.create()
	end 'pick'

	function pick(_ bool) returns R
		return R.create()
	end 'pick'
end 'Inner'

type Outer uses T
	typealias Held = Inner with T
	var inner as Held

	static function create() returns Self
		return Self{inner: Held.create()}
	end 'create'

	function probe() returns Count
		let c = self.inner.pick(1)
		return c.size()
	end 'probe'

	function probeOther() returns Count
		let c = self.inner.pick(true)
		return c.size()
	end 'probeOther'
end 'Outer'

typealias BoolOuter = Outer with bool

function main() returns ExitCode
	let o = BoolOuter.create()
	print("{o.probe()} {o.probeOther()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

### `sizeof` behind a method taken as a value, and read by a field default built from outside

<!-- test: sizeof.error.a-static-of-a-generic-type-is-not-a-function-value -->
A method of a generic type is not a function value, so no thunk is ever asked to call one without the descriptor it reads.
```maxon
type Box uses T
	var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	static function width() returns Count
		return sizeof(T)
	end 'width'

	function viaValue() returns Count
		let f = Self.width
		return f()
	end 'viaValue'
end 'Box'

typealias Small = int(0 to 65535)
typealias SmallBox = Box with Small

function main() returns ExitCode
	let a = SmallBox.create()
	print("{a.viaValue()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2078: <fragment>:14:16: 'Self.width' is a method, and a method is not a function value: wrap the call in a closure, such as 'function(...) gives Self.width(...)'
```

<!-- test: sizeof-read-by-a-field-default-built-through-a-typealias -->
```maxon
type Box uses T
	export var n = sizeof(T) as Count

	static function create() returns Self
		return Self{}
	end 'create'
end 'Box'

typealias Small = int(0 to 65535)
typealias SmallBox = Box with Small
typealias FlagBox = Box with bool

function main() returns ExitCode
	let a = SmallBox.create()
	let b = FlagBox.create()
	print("{a.n} {b.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 1
```

<!-- test: sizeof.error.a-static-of-a-generic-type-is-not-a-function-value-from-main -->
```maxon
type Box uses T
	var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	static function width() returns Count
		return sizeof(T)
	end 'width'
end 'Box'

typealias Small = int(0 to 65535)
typealias SmallBox = Box with Small

function main() returns ExitCode
	let f = SmallBox.width
	print("{f()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2078: <fragment>:18:19: 'SmallBox.width' is a method, and a method is not a function value: wrap the call in a closure, such as 'function(...) gives SmallBox.width(...)'
```
