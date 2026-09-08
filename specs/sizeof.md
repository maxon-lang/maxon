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

	export static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	export function typeSize() returns Integer
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

	export static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	export function typeSize() returns Integer
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

	export static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	export function directSize() returns Integer
		return sizeof(T)
	end 'directSize'

	export function indirectSize() returns Integer
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

	export static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	export function level0() returns Integer
		return sizeof(T)
	end 'level0'

	export function level1() returns Integer
		return self.level0()
	end 'level1'

	export function level2() returns Integer
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

⚠ **THE SIXTH SLOT IS THE POINT, AND IT IS WHY THIS NUMBER IS 56 RATHER THAN 48.** The embedding used to
stop at `parent@32` and let the flag take `@40` — the slot a buffer record keeps `element_destroy` in — so
a String record and a buffer record disagreed at exactly one offset and a String's bytes could only be
handed to buffer code through a freshly minted view, one heap record per `addressableBytes()` call.
Embedding the buffer record WHOLE (a String's `element_destroy` is always 0: its elements are `Byte`s and
own nothing) makes a String record a valid buffer record, and the mint becomes an incref.

⚠ **THE SUBJECT IS THE CORPUS'S OWN `String`, AND IT HAS TO BE.** A user conformer would be the more
direct probe, and it is the one this case first used — but a marker conformer of any name the compiler
does not own the byte record for is now refused at its `Self{…}`
(`Parser.requireFusedWrapperTag`), because the value it produced carried a byte record's bytes under a
struct's identity and its drop, its clone and its own field reads each disagreed with it. See
`interface-conformance/error.literal-marker-conformer-*` for the three measured programs. So the marker
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
it already carries. What had to change for this to compile is the descriptor-need fixpoint: it
recorded a local bound to a `Self{…}` as a value of the enclosing type but not a parameter
DECLARED `Self`, so nothing reserved `pairWith` a descriptor to forward and the call was
refused — *"calling 'slotSize' … from a function that carries no layout descriptor of its own
to forward"*. That refusal was a clean stand-in for an edge that had not been drawn, and its
own header said so; the edge is drawn now.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntBag = Bag with Int

type Bag uses Element
	typealias Items = Array with Element

	var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function slotSize() returns Int
		return sizeof(Element)
	end 'slotSize'

	export function pairWith(other Self) returns Int
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
