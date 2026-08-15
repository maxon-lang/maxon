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
loadable. The **MEMBERS** are served too, as of the cases at the end of this file: a name the roster
carries is the synthesized arm's in both spellings, and a name only the declaration carries is an
ordinary call to an ordinary declared function.

⛔ **AND THAT WAS *NOT* ONE CHANGE WITH `ProgramSignatures.genericInstanceHasBaseLayout`, WHICH THIS
SECTION USED TO SAY IT WAS.** The reasoning was that the gate also decides the FIELD WALK the drop
cascade reads, so the two had to move together. **MEASURED on this tree by widening exactly that gate
to admit a declared `Array` base: 551 `--filter=array` cases, 0 failures — INERT.** The walk it opens
enumerates one field, the buffer, whose drop callee is `__managed_decref`, which is already the callee
`genericInstanceBoxDropCallee` gives the record; both cascade readers filter a non-`__destruct_` leaf
out. The door that was actually refusing the member is `Parser.structLayoutOfType`, which reads that
gate as one of its inputs, so the admission is spent there — a layout to NAME a member is not a layout
to ENUMERATE fields.

What still blocks the listing is three mechanisms this spec does not exercise, each named by its own
wall in `stdlib/Array.maxon`: `for … in` over an interface EXISTENTIAL (`from`, line 132), an opaque
type-parameter value moved into a slot from a BORROW (`appendMemory`, line 281 — the first wall left
once the member walls fell, and reached identically by the `self.push(borrowed)` spelling the roster
has always served), and an extension method OWNING an opaque type-parameter parameter with no
descriptor reserved (`contains`).

### ⭐⭐ TWO `Array` DECLARATIONS IN ONE COMPILE — what is scoped, and what deliberately is not

`stdlib/Array.maxon` is now listed, so declaring a `type Array` does not replace the corpus's: it
CONTESTS it. `SignatureIndex.contestStdlibTypeName` moves the library's declaration into the reserved
space, and the compile then holds **two** `Array`s at once — the program's under the bare name, the
library's under a name this compile minted and no author may write. ⚖ **The rule is module-scoped
resolution: a stdlib body resolves `Array` to the STDLIB declaration, and the program's `Array` wins in
the program's own code.**

The two consequences point in opposite directions, and neither is an accident:

- **MEMBER RESOLUTION IS SCOPED — to the RECEIVER, not to whoever holds the six bytes `Array`.**
  `Parser.memberBelongsToTheCorpus` reads the declaration off the receiver's own instance, so a member
  only the program declares is not served on a value the library produced, and a member only the
  library declares is not served on the program's own. Both are refused, and the refusal says which
  side of the split the value is on rather than reciting the synthesized roster at a reader whose own
  declaration plainly has the member (`Parser.refuseArrayMemberTheOtherDeclarationCarries`).
- **A VALUE IS *NOT* SCOPED, AND THAT IS THE `Array`-IS-ITS-BUFFER THESIS RATHER THAN A HOLE.**
  `declarationIsTheManagedRecord` admits a declaration only in the SOLE-FIELD-AND-THAT-FIELD-THE-BUFFER
  shape, so any two admitted `Array` declarations over one element denote ONE record — same bytes, same
  stride, same drop, same clone, because there is nothing else in either of them. So an `Array with T`
  is accepted at the other declaration's `Array with T` position, which is what lets a program with its
  own container go on calling `String.from`, `String.split` and `toByteArray` at all.

⚠ **The two do not contradict each other: the RECORD is shared and the SURFACE is not.** Each side
reaches the value through its own declaration's members, and there is nothing IN the value for the
other side's members to be missing from. A member is a compile-time question about a declaration; a
value is a run-time record, and here the record is one thing under two names.

## Tests

<!-- test: the-empty-literal-is-the-empty-container -->
`Self{}` inside `type Array` builds the EMPTY container, which is the answer `<InnerAlias>{}` already
gets: the sole field IS the record, so an empty literal leaves no slot unwritten. Before this it was
`E3086 field 'managed' … is not initialized by this literal, and it has no default value`.

⚠ **THE EMPTINESS IS ASKED THROUGH `count()` AND WAS ASKED THROUGH `isEmpty()` UNTIL ARR4 RETIRED THAT
NAME.** The two say the same thing about this program and only one of them is still on the synthesized
surface, so the edit costs the case nothing — which is exactly what makes it the INCIDENTAL half of the
declared-own question (`Parser.arraySurfaceMemberNames` carries the ruling). A declaration of its own
would have worked too and would have tested the declaration rather than the literal.
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
	if a.count() != 0 'notEmpty'
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
error E2015: <fragment>:8:10: Unsupported: `Array` implements `BuiltinArrayLiteral`, and a record whose first field IS its `__ManagedMemory` is that buffer — one record under two names — so its literal is an identity on the buffer that field names. This declaration has 2 field(s), and every one past the buffer would be silently dropped at construction and then read back past the record's end when the value is cloned or destroyed. Declare the buffer alone, or drop the literal marker so the declaration keeps the box its fields describe
```

<!-- test: error.a-sole-field-that-is-not-the-buffer -->
⛔ **THE OTHER HALF OF THE SAME HAZARD, AND THE ONE THE NAME-KEYED ADMISSION LET THROUGH.** The
admission used to be `isArrayBaseName(name)` plus a sole field the layout calls inline-managed — and
that index is a question about the field's NAME and the declaration's MARKER, never about its TYPE. So
this declaration was ADMITTED and the identity handed the raw `Num` back as the container; the only
thing that refused it was a later type compare, whose sentence is about a return
(`E3005 … Cannot return 'int' from function declared to return 'struct'`). The admission is the SHAPE
now, so the refusal is positioned at the literal and names the field.
```maxon
typealias Num = int(0 to 1000)

type Array uses Element implements BuiltinArrayLiteral
	export var managed as Num

	export static function init(managed Num) returns Self
		return Self{managed: managed}
	end 'init'
end 'Array'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:10: Unsupported: `Array` implements `BuiltinArrayLiteral` over the parameter `Element` it declares, so its record IS a `__ManagedMemory with Element` and its literal is an identity on that buffer — but its first field is not one. A value whose bytes are the buffer's and whose declared fields say otherwise has those fields discarded at construction and read back past the record's end when it is cloned or destroyed. Declare exactly one field, `managed as __ManagedMemory with Element`, or drop the marker so the declaration keeps the box its fields describe
```

<!-- test: a-generic-conformer-of-another-name-is-the-record-too -->
⭐⭐ **THE BOUNDARY THE STRUCTURAL ADMISSION USED TO STOP AT, AND THE NAME IS NOT WHERE IT LIES.** This
declaration is generic over one parameter and its sole field IS a `__ManagedMemory` over that parameter
— the whole shape — so it IS the record, and it is admitted although nothing about it is spelled
`Array`. What used to refuse it was the fold: `canonicalGenericBaseName` mapped a written
`__ManagedMemory` onto a CONSTANT, so `Bag`'s own field resolved to the record declaration's instance
rather than to `Bag`'s, and the identity would have handed back a value of a type `Self` is not. The
fold now learns its target from the conformance — inside a `BuiltinArrayLiteral` conformer's body a
written `__ManagedMemory` is that conformer — so the field's instance IS `Bag`'s and the shape test is
the whole test.
```maxon
typealias Num = int(0 to 1000)

type Bag uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element

	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Bag'

typealias NumBag = Bag with Num

function main() returns ExitCode
	var b = NumBag.create()
	b.push(4 as Num)
	b.push(5 as Num)
	return (b.count() + try b.get(1) otherwise panic("get: index 1 was just pushed")) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-conformer-of-another-name-owns-its-managed-elements -->
⭐ **AND ITS OWNERSHIP CASCADE IS ITS OWN.** The record identity decides the drop callee and the element
descriptor, so a conformer admitted by SHAPE has to carry managed elements as correctly as the corpus's
does — `__destruct_Bag_String` is a distinct symbol from the corpus array's and is built from `Bag`'s own
instance. A leak or a double free here would exit 101 rather than fail an expectation, which is why the
case pushes owned strings and reads one back.
```maxon
type Bag uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element

	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Bag'

typealias StringBag = Bag with String

function main() returns ExitCode
	var b = StringBag.create()
	b.push("alpha")
	b.push("beta")
	let second = try b.get(1) otherwise panic("get: index 1 was just pushed")
	print(second)
	return (b.count() + second.byteLength()) as ExitCode
end 'main'
```
```stdout
beta
```
```exitcode
6
```

<!-- test: a-conformer-of-another-name-and-the-library-array-are-two-records-in-one-program -->
⭐⭐ **TWO RECORDS, TWO GIDS, ONE PROGRAM — which is what the fold being per-DECLARATION rather than
per-compile buys.** `Bag with String` is `Bag`'s own instance and `"a,bb,ccc".split(",")` is the corpus
array's, and both carry the compiler's synthesized surface because both declarations carry the marker. A
single canonical fold target would have merged them, which is the merge `ARR5` measured and refused.
```maxon
type Bag uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element

	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Bag'

typealias StringBag = Bag with String

function main() returns ExitCode
	var mine = StringBag.create()
	mine.push("xy")

	var total = mine.count()
	for part in "a,bb,ccc".split(",") 'librarysLoop'
		total = total + part.byteLength()
	end 'librarysLoop'

	return total as ExitCode
end 'main'
```
```exitcode
7
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

<!-- test: a-declared-member-off-the-roster-is-served-from-the-corpus -->
⭐ **A MEMBER THE DECLARATION CARRIES AND THE ROSTER DOES NOT IS AN ORDINARY CALL TO AN ORDINARY DECLARED
FUNCTION.** `at` is not one of the members shv2 synthesizes, so before this the call was
`E2015 … 'Array' member 'at' — P1.7 provides managed/get/set/…; that list IS the surface` — about a
method the program plainly has, sitting in the very declaration whose record the compiler owns.
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

	export function at(i Num) returns Element
		return try managed.get(i) otherwise panic("at: the caller checked the bound")
	end 'at'
end 'Array'

typealias NumArray = Array with Num

function main() returns ExitCode
	var a = NumArray.create()
	a.push(17 as Num)
	a.push(25 as Num)
	return (a.at(0) + a.at(1)) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: the-self-spelling-of-a-corpus-served-member-reaches-the-same-body -->
The `self.` spelling of the call above, made from inside the declaration's own body. It is the spelling
that reaches `dispatchMethodOnReceiver`, and it was refused by the same roster sentence — so the two
spellings of one member disagreed about whether the member existed at all.
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

	export function at(i Num) returns Num
		return try managed.get(i) otherwise panic("at: the caller checked the bound")
	end 'at'

	export function headPlusTail() returns Num
		return self.at(0) + self.at(1)
	end 'headPlusTail'
end 'Array'

typealias NumArray = Array with Num

function main() returns ExitCode
	var a = NumArray.create()
	a.push(30 as Num)
	a.push(12 as Num)
	return a.headPlusTail()
end 'main'
```
```exitcode
42
```

<!-- test: a-bare-call-to-a-roster-member-takes-the-roster -->
⭐⭐ **THE ROSTER-WINS ORDER APPLIES TO THE BARE SPELLING TOO — W17's RULE, SECOND SURFACE.** `self.get(i)`
has always reached the synthesized arm; a BARE `get(i)` in the same body reached the declaration instead,
so one member had two meanings depending on how it was written. The two bodies are made to DISAGREE on
purpose — the declared `count` answers 99 and the buffer holds 3 — so the case reports WHICH one the bare
call took rather than merely that it compiled. `count` is the discriminator and `get` is not, because the
roster's `get` hands back the ELEMENT, which in the shared generic body is an opaque `Element` no concrete
return type can be compared against.
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

	export function count() returns Num
		return 99 as Num
	end 'count'

	export function bareCount() returns Num
		return count()
	end 'bareCount'
end 'Array'

typealias NumArray = Array with Num

function main() returns ExitCode
	var a = NumArray.create()
	a.push(41 as Num)
	a.push(42 as Num)
	a.push(43 as Num)
	return a.bareCount()
end 'main'
```
```exitcode
3
```

<!-- test: a-bare-roster-member-inside-an-extension-body-resolves -->
An `extension` body's bare `count()` / `get(i)` is the same rule one declaration over, and it is what
`stdlib/Array.maxon`'s `contains(sequence)` is written in. The sibling walk cannot see them — they belong
to the type's OWN body — so the call was `E3004 call to undefined function 'get'`, a diagnostic no reader
ever saw because the `E3005` the untyped result then caused is THROWN and discards the file's recorded
diagnostics. The extension member is not called: shv2 compiles every declared body, so the compile IS the
assertion, and `main` reports through the roster.
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

export extension Array
	export function total() returns Num
		let slots = count()
		var sum = 0 as Num
		for i in 0 upto slots 'eachSlot'
			sum = sum + (try get(i) otherwise 0 as Num)
		end 'eachSlot'
		return sum
	end 'total'
end 'Array'

typealias NumArray = Array with Num

function main() returns ExitCode
	var a = NumArray.create()
	a.push(19 as Num)
	a.push(23 as Num)
	return a.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-managed-element-through-a-corpus-served-member-balances -->
⭐⭐ **THE OWNERSHIP HALF, AND IT PINS THE EXIT CODE BECAUSE THE PRINT ALONE COULD NOT SEE THE BUG.** The
corpus-served member hands back a MANAGED element, which is a `+1` the CALLEE owes
(`Parser.emitOwnedValueReturn`'s opaque arm) and takes through the enclosing instance's descriptor
`retainFunc@64`. That word was stamped ZERO for an `Array with String`, because the gate deciding whether a
descriptor carries ownership words asked `genericInstanceHasBaseLayout` — i.e. *"is there a shared body that
reads this block?"* — and answered no for every `Array`. A record-collapsed `type Array` is the case where
the record is the compiler's AND the methods are a shared body, so the caller was handed a BORROW it then
released: **this program printed `alpha beta 2` and SEGFAULTED at teardown.**

⛔ **AND THE FIRST VERSION OF THIS CASE PINNED ONLY `stdout`, SO IT PASSED WHILE DOING EXACTLY THAT.** An
unpinned exit code asserts nothing (`SpecTestRunner.runAssertions` says so deliberately, for the ported
`/specs` cases that pin only what a program prints) — so a fault or a **101** AFTER the last `print` is
invisible to a stdout-only case. Any authored case whose subject is OWNERSHIP must pin the exit code too.
```maxon
typealias Slot = int(0 to 1000)

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element

	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function at(i Slot) returns Element
		return try managed.get(i) otherwise panic("at: the caller checked the bound")
	end 'at'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha")
	a.push("beta")
	let head = a.at(0)
	let tail = a.at(1)
	print("{head} {tail} {a.count()}")
	return 0 as ExitCode
end 'main'
```
```stdout
alpha beta 2
```
```exitcode
0
```

<!-- test: error.a-member-no-declaration-carries-is-still-refused -->
⛔ **THE FALL-THROUGH MAY NOT BECOME A SILENT NO-OP.** The corpus door admits only a name the declaration
actually carries; anything else meets the roster sentence unchanged, which is the answer for a member that
exists nowhere.
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
	return a.nosuch() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:22:11: Unsupported: `Array` member 'nosuch' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: a-corpus-served-managed-element-balances-across-a-loop -->
⭐ **THE SAME ARITHMETIC, FIFTY TIMES OVER, WITH A CLONE IN THE MIDDLE.** One call can be one reference
wrong and still look right; a loop cannot. Every round takes an element out through the corpus door in
BOTH spellings — `b.at(0)` and, inside `lastOne`, `self.at(…)` over a BARE `count()` — and drops a clone,
so a miscount is 50 chances to fault or to exit **101**. The exit code is pinned for the reason the case
above it records.
```maxon
typealias Slot = int(0 to 1000)

// The record-collapsed `Array`, exercised through a CORPUS-SERVED member that hands back a managed
// element, in a loop, alongside a clone. Any reference taken one too few or one too many times exits 101.
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

	// Off the roster, so this is the corpus door's own path.
	export function at(i Slot) returns Element
		return try managed.get(i) otherwise panic("at: the caller checked the bound")
	end 'at'

	// The `self.` spelling of the same, plus a BARE roster call in the same body.
	export function lastOne() returns Element
		return self.at((count() - 1) as Slot)
	end 'lastOne'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha")
	a.push("beta")
	a.push("gamma")

	var seen = 0 as Slot
	for _ in 0 upto 50 'rounds'
		var b = a.clone()
		b.push("delta")
		let head = b.at(0)
		let tail = b.lastOne()
		if head.byteLength() + tail.byteLength() > 0 'nonEmpty'
			seen = (seen + 1) as Slot
		end 'nonEmpty'
	end 'rounds'

	print("{seen} {a.count()} {a.lastOne()} {a.at(0)}")
	return 0 as ExitCode
end 'main'
```
```stdout
50 3 gamma alpha
```
```exitcode
0
```

<!-- test: a-buffer-member-keeps-its-meaning-when-the-record-declares-the-name -->
⭐⭐ **`managed.<m>()` MEANS THE BUFFER'S `<m>`, EVEN WHEN THE RECORD AROUND IT DECLARES ONE TOO.**
`Array with T` and `__ManagedMemory with T` are ONE record with TWO surfaces, so nothing about the
receiver's TYPE tells them apart — only the buffer mark does. The corpus door read the type alone and
so asked the **`Array`** roster about a receiver denoting the **buffer**: `elementSize` is off the
`Array` roster and the record declares one, so the call was handed to the record's own method. It
compiled, it linked and it RAN, returning **99** where the buffer's answer is **8** — a silent wrong
answer with no diagnostic anywhere, which is why this case pins a VALUE rather than a refusal.

**Both directions are pinned, because the cure must not swap the collision round.** The three
buffer-denoting spellings (bare `managed.`, `self.managed.`, and `a.managed.` from outside) all mean
the BUFFER; the three record-denoting spellings (bare `elementSize()`, `self.elementSize()` and
`a.elementSize()`) all mean the RECORD. Declaring the name on the record changes neither: this
program prints exactly what the same program prints with the record's method renamed.
```maxon
typealias Slot = int(0 to u64.max)

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	// A name the BUFFER surface also has. It is off the `Array` roster, so it is the corpus door's own.
	export function elementSize() returns Slot
		return 99
	end 'elementSize'

	// The buffer, reached bare — the spelling `stdlib/Array.maxon` writes throughout.
	export function bufferBare() returns Slot
		return managed.elementSize()
	end 'bufferBare'

	// The buffer, reached through the explicit `self.` — the same receiver, one token longer.
	export function bufferViaSelf() returns Slot
		return self.managed.elementSize()
	end 'bufferViaSelf'

	// THE REVERSE DIRECTION: a bare sibling call still means the RECORD's own method.
	export function ownBare() returns Slot
		return elementSize()
	end 'ownBare'

	// And so does the `self.` spelling of it.
	export function ownViaSelf() returns Slot
		return self.elementSize()
	end 'ownViaSelf'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha")

	// The third buffer spelling, from OUTSIDE the declaration, and the third record one beside it.
	let outsideBuffer = a.managed.elementSize()
	let outsideOwn = a.elementSize()

	print("{a.bufferBare()} {a.bufferViaSelf()} {outsideBuffer} {a.ownBare()} {a.ownViaSelf()} {outsideOwn}")
	return 0 as ExitCode
end 'main'
```
```stdout
8 8 8 99 99 99
```
```exitcode
0
```

<!-- test: a-managed-swap-through-the-buffer-is-not-the-records-own -->
⭐⭐ **THE SAME COLLISION ON A THROWING MEMBER, WHICH IS WHERE IT WAS FOUND.**
`stdlib/helpers/sort/smallSort.maxon:32` is `try managed.swap(i, j: j)` inside an `export extension
Array` that declares a `swap` of its own — so the bare call resolved to the extension's own method and
the build stopped at **`E3055 … 'Array.swap' does not throw`**, about a call whose real target throws.
`swap` is off the `Array` roster and on the buffer's, exactly as `elementSize` is; the only thing the
throws-mismatch added was a diagnostic where the case above got silence.

**The element is a `String`, so the ownership is exercised and not merely the routing.** A buffer
`swap` MOVES ownership between slots with refcounts unchanged — the reason it may not be built from
`get`+`set` — so a swap that took one reference too many or too few faults or exits **101** rather
than printing. Fifty-one reversals run it 102 times over four managed elements, and the exit code is
pinned because a leak lands after the last `print`.
```maxon
typealias Slot = int(0 to u64.max)

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	// `smallSort.maxon`'s own body: the record declares `swap`, and its `swap` calls the BUFFER's.
	// Resolving the inner call to THIS method would be unbounded recursion, which is what the
	// throws-mismatch was really reporting.
	export function swap(i Slot, j Slot)
		try managed.swap(i, j: j) otherwise panic("swap: the caller pre-bounded i and j")
	end 'swap'

	// THE REVERSE DIRECTION, through `self.`: the record's own `swap` above, not the buffer's.
	export function reverseRange(lo Slot, hi Slot)
		if hi <= lo + 1 'trivial'
			return
		end 'trivial'
		var i = lo
		var j = hi - 1
		while i < j 'loop'
			self.swap(i, j: j)
			i = i + 1
			j = j - 1
		end 'loop'
	end 'reverseRange'

	// THE REVERSE DIRECTION, bare: a sibling call to a name the buffer also has still means the record.
	export function swapEnds()
		swap(0 as Slot, j: (count() - 1) as Slot)
	end 'swapEnds'

	export function reverseAll()
		reverseRange(0 as Slot, hi: count() as Slot)
	end 'reverseAll'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha")
	a.push("beta")
	a.push("gamma")
	a.push("delta")

	for _ in 0 upto 51 'rounds'
		a.reverseAll()
	end 'rounds'

	a.swapEnds()

	let w = try a.get(0) otherwise panic("0 is in range")
	let x = try a.get(1) otherwise panic("1 is in range")
	let y = try a.get(2) otherwise panic("2 is in range")
	let z = try a.get(3) otherwise panic("3 is in range")
	print("{w} {x} {y} {z} {a.count()}")
	return 0 as ExitCode
end 'main'
```
```stdout
alpha gamma beta delta 4
```
```exitcode
0
```

<!-- test: a-library-array-value-reaches-a-declared-own-array-parameter -->
⭐⭐ **A VALUE CROSSES THE SPLIT, AND THE ELEMENT IS MANAGED SO THE CROSSING IS AN OWNERSHIP CLAIM AND
NOT ONLY A TYPE ONE.** `String.split` hands back the LIBRARY's `Array with String`; `takesMine` declares
the PROGRAM's. They are two declarations, and the argument is admitted because both are the
sole-field-buffer shape over one element and therefore one record — see the section above. The exit code
is the count, and the three `String` records are freed by the same `__managed_decref` walk whichever
declaration named the array (a leak exits 101).
```maxon
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

typealias MyStrings = Array with String

function takesMine(parts MyStrings) returns int
	return parts.count()
end 'takesMine'

function main() returns ExitCode
	let s = "a,b,c"
	return takesMine(s.split(",")) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-declared-own-array-value-reaches-a-library-parameter -->
⭐ **THE SAME CROSSING THE OTHER WAY, INTO `stdlib/` ITSELF.** `String.from(bytes ByteArray)` is
declared over the library's `Array with Byte`; the argument is the program's own. It is the direction
that matters most, because the library body then goes on to call `reserve`, `count` and `appendMemory`
on it — members served from the library's declaration, on a record the program built.
```maxon
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

typealias MyBytes = Array with Byte

function main() returns ExitCode
	var mine = MyBytes.create()
	mine.push(72 as Byte)
	mine.push(105 as Byte)
	mine.push(33 as Byte)
	let s = String.from(mine)
	print(s)
	return s.byteLength() as ExitCode
end 'main'
```
```stdout
Hi!
```
```exitcode
3
```

<!-- test: for-in-walks-both-arrays-in-one-program -->
⭐ **`for … in` ADMITS BOTH, AND BY DIFFERENT ROUTES.** The library's array declares `createIterator()`,
so it is rewritten to its `ArrayIterator` and walked as a cursor; the program's own declares none, so it
takes the COUNTED form off `count`/`get` (`Parser.requireIterableSource`'s array arm exists for exactly
this declaration). One program, one loop keyword, two lowerings.
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
	var mine = NumArray.create()
	mine.push(4 as Num)
	mine.push(5 as Num)

	var total = 0
	for n in mine 'ownLoop'
		total = total + n
	end 'ownLoop'

	for part in "a,bb,ccc".split(",") 'librarysLoop'
		total = total + part.byteLength()
	end 'librarysLoop'

	return total as ExitCode
end 'main'
```
```exitcode
15
```

<!-- test: error.a-library-only-member-is-not-served-on-the-programs-own-array -->
⛔ **THE SCOPING, SEEN FROM THE PROGRAM'S SIDE.** `truncate` is declared on `stdlib/Array.maxon`'s
`Array` and on nothing else in this program, so it is not served on a value of the program's own
container. The refusal names the split rather than reciting the synthesized roster at a reader who can
see a `truncate` in the library source: *"that list IS the surface"* is true of the surface and useless
here.
```maxon
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

typealias MyBytes = Array with Byte

function main() returns ExitCode
	var mine = MyBytes.create()
	mine.push(72 as Byte)
	mine.truncate(0)
	return mine.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:7: Unsupported: `Array` member 'truncate' — this value's type is the `type Array` this program declares, and 'truncate' is declared on the `Array` the standard library declares. Declaring an `Array` of your own does not replace the library's: yours answers for the bare name in YOUR files, the library's goes on answering inside `stdlib/`, and they are two different types — so a member declared on one is not served on a value of the other. What both share is the compiler's synthesized surface, which provides managed/get/set/first/count/push/resize/append/appendMemory
```

<!-- test: error.a-program-only-member-is-not-served-on-a-library-array -->
⛔ **AND FROM THE LIBRARY'S SIDE, WHICH IS THE HALF A READER MEETS BY SURPRISE.** `at` is declared two
lines up, in this program's own `type Array`, and `s.split(",")` is not a value of it. Before the
receiver-scoped lookup this program was refused by the roster sentence alone, about a method the reader
was looking straight at.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element

	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function at(i int) returns Element
		return try managed.get(i) otherwise panic("at: the caller checked the bound")
	end 'at'
end 'Array'

function main() returns ExitCode
	let parts = "a,b,c".split(",")
	print(parts.at(1))
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:22:14: Unsupported: `Array` member 'at' — this value's type is the `Array` the standard library declares, and 'at' is declared on the `type Array` this program declares. Declaring an `Array` of your own does not replace the library's: yours answers for the bare name in YOUR files, the library's goes on answering inside `stdlib/`, and they are two different types — so a member declared on one is not served on a value of the other. What both share is the compiler's synthesized surface, which provides managed/get/set/first/count/push/resize/append/appendMemory
```

<!-- test: a-compiler-minted-literal-is-the-library-array -->
⭐⭐ **A LITERAL THE COMPILER MINTS IS THE LIBRARY'S RECORD, NOT THE PROGRAM'S — and the program never
wrote a type for it to be read from.** `b"Hi!"` and `[4, 5]` name no declaration: the sweep interns their
`Array with Byte` / `Array with int` instances itself. Under a contest the corpus's own declaration has
MOVED (`SignatureIndex.contestStdlibTypeName`), so a mint filed under the bare name lands on the
PROGRAM's container and every library-only member is refused on a value the reader never typed. `isEmpty`
is declared on `stdlib/Array.maxon` and nowhere else here, so it is the whole question. The bootstrap
oracle prints `false false` and exits 5.
```maxon
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

function main() returns ExitCode
	let bytes = b"Hi!"
	let nums = [4, 5]
	print("{bytes.isEmpty()} {nums.isEmpty()}")
	return (bytes.count() + nums.count()) as ExitCode
end 'main'
```
```stdout
false false
```
```exitcode
5
```

<!-- test: a-minted-managed-element-literal-is-the-library-array-and-balances -->
⭐ **THE SAME MINT WITH A MANAGED ELEMENT, WHERE A WRONG RECORD IDENTITY IS A LEAK OR A DOUBLE FREE
RATHER THAN AN EXPECTATION MISMATCH.** `["a", "bb"]` interns `Array with String`; `contains` is served
only from the library's declaration. The two `String` records are freed by one `__managed_decref` walk,
so a record identity that picked the wrong declaration exits 101 rather than 2.
```maxon
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

function main() returns ExitCode
	let words = ["a", "bb"]
	print("{words.contains("bb")}")
	return words.count() as ExitCode
end 'main'
```
```stdout
true
```
```exitcode
2
```

<!-- test: error.a-conformer-of-another-name-under-a-contest-is-still-refused-under-its-own-name -->
⛔⛔ **THE TWO-DECLARATIONS SENTENCE IS ABOUT TWO `Array`s, SO A RECEIVER THAT IS NEITHER MAY NOT BE
HANDED IT.** Found in review of ARRO, and it is the cure one door up applied everywhere EXCEPT its own
twin: `refuseArrayMemberTheOtherDeclarationCarries` gated only on whether a CONTEST exists, never on
whether the receiver is one of the two `Array`s. Since ARR5b the array surface serves every
`BuiltinArrayLiteral` conformer, so this program — a contested `type Array` AND a `Bag`, asking a `Bag`
for a member the OTHER declaration carries — met *"this value's type is the `Array` the standard library
declares"*. `receiverIsTheProgramsOwn` was `false` for the WRONG REASON, so it took the library arm and
**the whole sentence frame was false, not merely its noun** — which is why naming the receiver would have
fixed the first word and left the claim standing.

A conformer of another name now falls through to the roster refusal, which states what the SURFACE
serves — and the surface is exactly what such a receiver has.

⚠ The three conditions are all load-bearing and no earlier case has them together: the sibling above
declares no `Array`, so the contest gate returns before the defect; every other two-declaration case has
an `Array` receiver.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element

	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function at(index int) returns Element throws ArrayError
		return try managed.get(index)
	end 'at'
end 'Array'

type Bag uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element

	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Bag'

typealias IntBag = Bag with int

function main() returns ExitCode
	var b = IntBag.create()
	b.push(7)
	return b.at(0) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:39:11: Unsupported: `Bag` member 'at' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: error.a-conformer-of-another-name-is-refused-under-its-own-name -->
⛔ **THE ROSTER REFUSAL NAMES THE TYPE THE VALUE ACTUALLY HAS.** Since a conformer of any name IS the
record, the array surface serves more than one declaration — so the refusal cannot name a constant. This
program declares no `Array` at all and `Bag` is the only container in it; a sentence about `Array` would
send the reader to a type their program does not have, which is the mistake `surfaceRosterProvider`'s
own rule forbids. The roster itself is unchanged: it is the surface's, and the surface is shared.
```maxon
type Bag uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element

	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Bag'

typealias IntBag = Bag with int

function main() returns ExitCode
	var b = IntBag.create()
	b.push(1)
	return b.nosuchmember() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:11: Unsupported: `Bag` member 'nosuchmember' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: error.the-librarys-array-is-refused-under-the-name-its-file-writes -->
⛔⛔ **AND THE OTHER SIDE OF THAT NOUN — THE ONE THAT CAN LEAK A NAME NO AUTHOR MAY WRITE.** Under a
contest the corpus's declaration is keyed under this compile's MINT, so a refusal reading the receiver's
instance straight off says **`__Array`** — a spelling `Parser.requireUnreservedName` forbids the reader
from typing, about a type they cannot name. `nosuchmember` is on neither declaration, so this falls past
the two-declarations refusal to the roster one, which is the door that has to say `Array`.
```maxon
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

function main() returns ExitCode
	let parts = "a,b,c".split(",")
	return parts.nosuchmember() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:18:15: Unsupported: `Array` member 'nosuchmember' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: error.a-conformer-named-for-a-container-the-compiler-serves-is-refused -->
⛔⛔ **THE MARKER IS OPEN TO ANY NAME EXCEPT ONE SHV2 ALREADY SERVES A CONTAINER UNDER, AND THAT
EXCEPTION WAS MISSING — A FIXED-SIZE `Vector with 3 Num` GREW.** `Bag` above is admitted because the
compiler owns no `Bag`; `Vector` is different in kind, because shv2 synthesizes that container itself and
a `Vector with N T` states its SIZE as a second coordinate of its instance identity
(`GenericInstanceRegistry.fixedSizes`) where a `__ManagedMemory with T` states none.

**MEASURED on the tree before this case existed, with nothing but the declaration below added to an
ordinary program**: `noteArrayLiteralConformer` filed `Vector` as a conformer, `isArrayBaseName("Vector")`
went TRUE through the non-corpus widening, and `Parser.dispatchMethodOnReceiver` — which asks the ARRAY
arm before the vector one — served the growable surface on a fixed-size receiver. `Vec3.create()` reported
**count 0**, four `push`es took it to **4**, and `resize(9)` to **9**. Not a refusal and not a diagnostic:
the size that is part of the type was simply gone.

⇒ **A name shv2 serves a container under is not open to the marker.** The conformance REQUIRES a
`static init(value __ManagedMemory) returns Self` (E3016 without one), and that body's identity is
precisely what cannot be honoured under such a name — so the declaration is refused where it constructs,
with the container's own reason rather than the `String`/`Character` sentence a `Holder` gets.
```maxon
typealias Num = int(0 to 100)

type Vector uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element

	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'
end 'Vector'

typealias Vec3 = Vector with 3 Num

function main() returns ExitCode
	var v = Vec3.create()
	v.push(5 as Num)
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:10: Unsupported: `Vector` is a generic container shv2 serves from its own runtime rather than from a declaration, so a `Vector with …` is already this compiler's own instance and not the `__ManagedMemory` record a `BuiltinArrayLiteral` conformer's literal is an identity on. Honouring the marker would put every `Vector` on the growable array's surface — `push`, `resize` — under the container's own name, which is a wrong answer rather than a refusal. Rename the declaration, or drop the marker
```

<!-- test: error.a-conformer-named-for-the-list-is-refused-for-the-same-reason -->
⭐ **THE SAME REFUSAL AT THE SECOND NAME, BECAUSE THE RULE IS ABOUT THE CLASS AND NOT ABOUT `Vector`.**
`List` is the other container shv2 still synthesizes under a name a user may write, and its record is a
chain of 24-byte nodes dropped through `__list_decref` — nothing like the 48-byte managed record. **MEASURED
before the rule existed:** the identical declaration named `List` compiled, and `l.resize(9)` — a member
the list roster does not carry, and cannot (`shv2 provides create/append/prepend/get/first/removeFirst/count`)
— answered **count 9**. Pinning one name would have left the other open, which is the shape a
`covered-by` roster written twice always takes.
```maxon
typealias Num = int(0 to 100)

type List uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element

	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'
end 'List'

typealias NumList = List with Num

function main() returns ExitCode
	var l = NumList.create()
	l.resize(9)
	return l.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:10: Unsupported: `List` is a generic container shv2 serves from its own runtime rather than from a declaration, so a `List with …` is already this compiler's own instance and not the `__ManagedMemory` record a `BuiltinArrayLiteral` conformer's literal is an identity on. Honouring the marker would put every `List` on the growable array's surface — `push`, `resize` — under the container's own name, which is a wrong answer rather than a refusal. Rename the declaration, or drop the marker
```

<!-- test: error.a-conformer-named-for-a-container-that-constructs-nothing-still-does-not-take-its-surface -->
⭐⭐ **THE HALF THE TWO CASES ABOVE CANNOT SEE: A CONFORMER THAT CONSTRUCTS NOTHING.** Its `init` satisfies
the conformance by DELEGATING, so it never reaches a fused-wrapper literal and the declaration stands. What
must still hold is that the marker did not make `Vector` an ARRAY declaration, and the only thing holding
that is `ProgramSignatures.noteArrayLiteralConformer`'s skip. The assertion is the ROSTER in the sentence:
`count/get/set` is the VECTOR surface, and a hijacked receiver would be reciting the growable array's
`managed/get/set/first/count/push/resize/append/appendMemory` instead — or, as it did before this rung,
not being refused at all.

⭐ **BOTH SABOTAGES WERE RUN, AND THE FIRST ONE REFUTED THE PARAGRAPH THAT USED TO STAND HERE.** It said the
skip covers the surface, the refusal covers the declaration, and neither substitutes for the other — a tidy
split, and wrong about the direction of the dependency:

| sabotage | this case | the two above |
|---|---|---|
| **skip off**, refusal on | `E3005 … Cannot return 'Vector' from function declared to return 'Vector.ElementMemory'` | **compilation SUCCEEDS** — no refusal at all |
| skip on, **refusal off** | **PASS** | `requireFusedWrapperTag`'s `String`/`Character` sentence |

⇒ **The skip is the load-bearing rule and it is what makes the refusal REACHABLE.** Without it
`isArrayBaseName` answers TRUE for the name, `firstFieldIsItsOwnBuffer` follows, and
`Parser.emitFusedWrapperLiteral` returns down its IDENTITY arm before any of the three sentences is asked —
which is precisely how a fixed-size vector came to be served `push`. The refusal is not a second half; it is
the NOUN the declaration gets once the skip has taken it off the array surface.
```maxon
typealias Num = int(0 to 100)

type Vector uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element

	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Vector.init(managed)
	end 'init'
end 'Vector'

typealias Vec3 = Vector with 3 Num

function main() returns ExitCode
	var v = Vec3.create()
	v.push(5 as Num)
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:18:4: Unsupported: `Vector` member 'push' — shv2 provides count/get/set; that list IS the surface, so nothing else is served here
```

<!-- test: error.a-string-marker-conformer-named-for-a-container-is-not-this-doors-subject -->
⛔ **THE NEW REFUSAL'S NOUN WAS WRONG FOR EVERY MARKER BUT ITS OWN, AND THE HEADER SAID IT COULD NOT BE
(found in review).** `Parser.requireNameIsNotABuiltinContainerBase` RENDERS the marker into its sentence
rather than spelling it, and its header read that as proof a `BuiltinStringLiteral` conformer would "meet
its own marker in the sentence rather than the array one". Rendering carries the marker's NAME and not the
REASON under it, which stayed hardcoded to the growable surface. **MEASURED before the gate**, on the
declaration below — `interface-conformance.error.literal-marker-conformer-would-be-discarded-at-construction`
with only the type's name changed:

> ``…not the `__ManagedMemory` record a `BuiltinStringLiteral` conformer's literal is an identity on.
> Honouring the marker would put every `Vector` on the growable array's surface — `push`, `resize`…``

Three clauses, none of them true of it: the declaration is **not generic**, so the `Vector with …` the
sentence quotes does not exist; `ProgramSignatures.noteArrayLiteralConformer` is called for the ARRAY marker
alone, so no array surface is at stake and nothing was skipped; and it **preempted**
`requireFusedWrapperTag`, whose sentence is the right one here and is what the byte-for-byte identical
declaration named `Wrapped` still gets.

⇒ **The door is gated on the array marker, the same opening clause its sibling
`requireArrayMarkerDeclaresTheBuffer` already had.** The assertion below is that the name `Vector` buys this
declaration NOTHING — it meets the byte-record sentence a `Wrapped` meets, because the byte record is what
its marker is about. A container name is disqualifying for the ARRAY marker specifically, and that is the
whole of the rule.
```maxon
type Vector implements BuiltinStringLiteral
	var managed as __ManagedMemory
	var flag as bool

	export static function init(value __ManagedMemory) returns Self
		return Self{managed: value, flag: false}
	end 'init'
end 'Vector'

function main() returns ExitCode
	let mm = try __ManagedMemory.create(64, elementSize: 1) otherwise return 1
	try mm.setLength(40) otherwise return 2
	let w = Vector.init(mm)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:10: Unsupported: `Vector` implements `BuiltinStringLiteral`, one of `stdlib/Builtins.maxon`'s literal markers, so its record would be the compiler's own fused byte record rather than the fields it declares. shv2 mints that record only for the two names it owns the record FOR — `String` and `Character` — because a conformer of any other name gets a VALUE whose bytes are a byte record's and whose IDENTITY is a struct's: every declared field the fused record has no slot for is discarded at construction, and the struct cascade that later drops or clones it reads past the record's end
```
