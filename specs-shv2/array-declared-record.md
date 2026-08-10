---
feature: array-declared-record
status: stable
keywords: [array, __ManagedMemory, BuiltinArrayLiteral, Self, generic, envelope-collapse, corpus]
category: types
---

# A declared `type Array` IS the record the compiler already owns

## Documentation

`stdlib/Array.maxon` opens

```
export type Array uses Element implements BuiltinArrayLiteral, Iterable with (Element, ArrayIter), Cloneable
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory
```

and shv2 could not admit that declaration at all: **every construction in it was `E2015 'Array'
implements 'BuiltinArrayLiteral' … shv2 mints that record only for the two names it owns the record
FOR — String and Character`.** That refusal is about a real hazard and it stays (see the two
`error.` cases below) — it simply never applied to this one shape, and the whole of why is one
sentence: **an `Array` IS its `__ManagedMemory`.**

`SignatureIndex.canonicalGenericBaseName` maps `__ManagedMemory with T` onto `Array with T`, one
`GenericInstanceId`, told apart only by a per-value PROVENANCE mark (`Parser.markBufferSurface`).
So the sole field this declaration has is not a slot inside a box — it is the box. There is nothing
for a construction to discard, which is precisely what the refusal was protecting against.

### What that costs the compiler: `Self` is the INSTANCE, not the base

An ordinary generic type's body is compiled ONCE over its base, and `Self` there is
`structRef(Base)`; the concrete instance is put back at the CALL SITE
(`retypeGenericAliasConstructorResult`). That is right for `stdlib/Map.maxon`, whose record really is
a box built from declared fields.

It is wrong here, and wrong in the direction the sibling refusal names: a `structRef(Array)` value
whose bytes are the buffer record is *bytes of one record, identity of another*, so its drop would
be a struct cascade over a `__ManagedMemory`. A non-generic byte record already avoids this by
answering `Self` with its TAG (`String`, `Character`); `Array` is that same fact with a type
parameter on it, so `Parser.enclosingSelfType` answers with the instance over the declaration's own
parameters (`ProgramSignatures.ownRecordSelfInstance`).

Four readers had to learn the second spelling, and each was a hard failure rather than a wrong
answer, which is why they are all pinned here:

| reader | what it did before | measured symptom |
|---|---|---|
| `Parser.returnsEnclosingType` | tested `structRef` only | `E2015 … a static that needs the type parameter's layout descriptor must return `Self`` — on a static that returns `Self` |
| `IrInterface.renderDeclaredTypeName` | rendered the mangled instance | three `E3016`s at once, against `Cloneable`, `Equatable` and `BuiltinArrayLiteral` |
| the descriptor-need pre-scan | saw `<Alias>{}`, not `Self{}` | `panic … appendOpaqueArrayCreate: … the parser's descriptor-need seed and this lowering disagree` |
| `Parser.emitFieldLoad`'s inline-`managed` arm | minted a BYTE view | `panic … opaqueTypeParamPosition: asked for the `uses`-list position of a `int`` |

The last of those is the one worth reading twice, because the byte view is also a **stride lie**: it
stamps `element_size@24 = 1` on a record whose slots are 8 bytes wide. `String` and `Character` are
byte buffers so the view is correct for them; an `Array with Element` is not, and its `managed` read
is simply the surface flip — the same answer `arr.managed` gets from `Parser.bufferSurfaceOf`, and
it allocates nothing where the String path allocates a record.

### The two halves of the refusal that remain

The hazard `requireFusedWrapperTag` documents is that construction DISCARDS every declared field the
fused record has no slot for, and the drop and clone cascades then read past the record's end. Both
`error.` cases below are that hazard still being true:

- **another NAME** — the compiler owns no record for it, so there is no identity to perform.
- **a SECOND FIELD under the name `Array`** — the identity would drop it silently. `Array` is
  deliberately absent from `TypeResolution.isCompilerOwnedTypeName` (exactly as `Map` is), so a user
  program may declare its own container and this is the shape that must not be admitted. The
  admission is therefore STRUCTURAL and not a name whitelist: sole field, and that field the buffer.

### ⚠ This does NOT list `stdlib/Array.maxon`, and the boundary is exact

The declaration is admitted and its record, its `Self`, its literals, its statics and its
conformances are correct — measured here on a user-written one, because the corpus module is not yet
loadable. What still blocks the listing is four mechanisms this spec does not exercise, each named
by its own wall in `stdlib/Array.maxon`: `for … in` over an interface EXISTENTIAL (`from`), an
opaque type-parameter value written into N slots inside a loop (`growFilled`, `refill`), an
extension method OWNING an opaque type-parameter parameter with no descriptor reserved (`contains`),
and the corpus fall-through for a declared member, which is one change with
`ProgramSignatures.genericInstanceHasBaseLayout` because that gate also decides the FIELD WALK the
drop cascade reads.

## Tests

<!-- test: the-empty-literal-is-the-empty-container -->
`Self{}` inside `type Array` builds the EMPTY container, which is the answer `<InnerAlias>{}` already
gets: the sole field IS the record, so an empty literal leaves no slot unwritten. Before this it was
`E3086 field 'managed' … is not initialized by this literal, and it has no default value`.
```maxon
typealias Num = int(0 to 1000)

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Array'

typealias NumArray = Array with Num

function main() returns ExitCode
	var a = NumArray.create()
	if not a.isEmpty() 'notEmpty'
		return 1
	end 'notEmpty'
	a.push(7 as Num)
	a.push(9 as Num)
	return a.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: the-literal-is-an-identity-on-the-buffer -->
`Self{managed: m}` allocates nothing — it is the buffer, renamed. The adopted array and the buffer
therefore agree on `length@8` because it is the same word, and the refcount balances: a run that
took one owner too few or too many would exit **101**, not print.
```maxon
typealias Num = int(0 to 1000)

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Array'

typealias NumArray = Array with Num

function main() returns ExitCode
	var a = NumArray.create()
	a.push(4 as Num)
	a.push(5 as Num)
	a.push(6 as Num)
	let b = NumArray.init(a.managed)
	if b.count() != a.count() 'disagree'
		return 1
	end 'disagree'
	return b.count() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-static-returning-self-is-the-instance -->
⭐ **A `static function … returns Self` that builds the container needs the enclosing instance's
layout descriptor, and it may only source one by returning `Self`.** With `Self` resolving to the
base `structRef` the static was told it returned something else —
`E2015 … must return 'Self' so the caller can source the descriptor from the instance the static
builds` — about a declaration whose return clause says exactly that. Both statics are exercised, so
the answer is a real one and not merely a compile.
```maxon
typealias Num = int(0 to 1000)

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Array'

typealias NumArray = Array with Num

function main() returns ExitCode
	var built = NumArray.create()
	built.push(20 as Num)
	built.push(22 as Num)
	let adopted = NumArray.init(built.managed)
	let head = try adopted.get(0) otherwise return 1
	let tail = try adopted.get(1) otherwise return 2
	return (head + tail) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: clone-through-the-inline-buffer-gives-independent-storage -->
⭐ **THE STRIDE.** `clone()`'s body reads `managed` and slices it, and the inline-`managed` read used
to mint a BYTE view of an 8-byte-wide buffer. The copy is exercised at a width where that lie is
observable — the elements read back, and the two arrays grow independently.
```maxon
typealias Num = int(0 to 1000)

type Array uses Element implements BuiltinArrayLiteral, Cloneable
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function clone() returns Self
		let len = managed.length()
		let copy = try managed.slice(0, len) otherwise panic("clone: 0..len is always in range")
		return Self{managed: copy}
	end 'clone'
end 'Array'

typealias NumArray = Array with Num

function main() returns ExitCode
	var a = NumArray.create()
	a.push(300 as Num)
	a.push(301 as Num)
	var b = a.clone()
	b.push(302 as Num)
	let borrowed = try b.get(1) otherwise return 1
	if borrowed != 301 'stride'
		return 2
	end 'stride'
	if a.count() != 2 'sourceGrew'
		return 3
	end 'sourceGrew'
	return b.count() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-managed-element-clones-and-balances -->
The same clone over a MANAGED element. Every element is a refcounted pointer, so a copy that took
the wrong number of references exits **101** rather than answering — which is what makes the printed
answer the leak check as well as the value check.
```maxon
type Array uses Element implements BuiltinArrayLiteral, Cloneable
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function clone() returns Self
		let len = managed.length()
		let copy = try managed.slice(0, len) otherwise panic("clone: 0..len is always in range")
		return Self{managed: copy}
	end 'clone'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha")
	a.push("beta")
	var b = a.clone()
	b.push("gamma")
	let first = try b.get(0) otherwise "?"
	let last = try b.get(2) otherwise "?"
	print("{a.count()} {b.count()} {first} {last}")
	return 0 as ExitCode
end 'main'
```
```stdout
2 3 alpha gamma
```

<!-- test: self-in-an-interface-requirement-matches-the-declaration -->
`Cloneable.clone() returns Self` and `BuiltinArrayLiteral.init(…) returns Self` are matched against a
declaration whose `Self` is now an INSTANCE. The comparison and the diagnostic both spell it as the
declaration — three simultaneous `E3016`s said `Array_Te3315404e8d3fd14`, a type no author can write.
```maxon
typealias Num = int(0 to 1000)

type Array uses Element implements BuiltinArrayLiteral, Cloneable
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function clone() returns Self
		let len = managed.length()
		let copy = try managed.slice(0, len) otherwise panic("clone: 0..len is always in range")
		return Self{managed: copy}
	end 'clone'
end 'Array'

typealias NumArray = Array with Num

function main() returns ExitCode
	var a = NumArray.create()
	a.push(9 as Num)
	return a.clone().count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-second-field-under-the-name-array -->
⛔ **THE HAZARD THE ADMISSION IS STRUCTURAL FOR.** The identity hands back the buffer, so a second
declared field would be dropped at construction and then read past the record's end by the clone and
drop cascades. The refusal names the SHAPE, because the name is not what is wrong here.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory
	export var frozen as bool

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed, frozen: false}
	end 'init'
end 'Array'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:10: Unsupported: `Array` implements `BuiltinArrayLiteral`, and an `Array` IS its `__ManagedMemory` — one record under two names — so its literal is an identity on the buffer its sole field names. This declaration has 2 field(s), and every one past the buffer would be silently dropped at construction and then read back past the record's end when the value is cloned or destroyed. Declare the buffer alone, or give the type a name the compiler owns no record for
```

<!-- test: error.a-conformer-of-another-name-is-still-refused -->
The unchanged refusal. The identity rests on `Array with T` and `__ManagedMemory with T` being one
instance, which is a fact about that base name and no other — so a conformer of any other name still
gets the sentence about the two names the compiler owns a record for.
```maxon
type Holder implements BuiltinArrayLiteral
	export var managed as __ManagedMemory

	export static function init(managed __ManagedMemory) returns Self
		return Self{managed: managed}
	end 'init'
end 'Holder'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:10: Unsupported: `Holder` implements `BuiltinArrayLiteral`, one of `stdlib/Builtins.maxon`'s literal markers, so its record would be the compiler's own fused byte record rather than the fields it declares. shv2 mints that record only for the two names it owns the record FOR — `String` and `Character` — because a conformer of any other name gets a VALUE whose bytes are a byte record's and whose IDENTITY is a struct's: every declared field the fused record has no slot for is discarded at construction, and the struct cascade that later drops or clones it reads past the record's end
```
