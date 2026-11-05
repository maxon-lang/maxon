---
feature: static-stride-specialization
status: experimental
keywords: [optimizer, codegen, array, element-size, stride, inline, managed-memory, generics]
category: codegen
---
# The element stride the compiler already knows

## Documentation

`ManagedMemoryRuntime.emitStrideChain` forks an element access on `element_size@24` — a **runtime**
field of the array record — into one arm per stride a target moves in a single memory operation: a machine
word, a byte, 16 and 32 bits, and a 1-, 2- or 4-bit packed field.
`InlineManagedPrimitives` puts that fork inline at every array read and write in every program,
so without a known stride an `Array with Integer` element access pays, per access:

```
mov rax, [rbx + 24]      ; element_size@24
cmp rax, 8
jne __im_stride          ; ... which compares against the other widths in turn
```

`Array with Integer` stamps that field with 8 in every program that ever runs. The stamp is
`ProgramSignatures.arrayElementSize`'s, it is decided by the ELEMENT TYPE, and the compiler has it at
the moment it emits the call.

### How the stamp reaches the pass that needs it

The Std tier is deliberately type-free: `StdOp.call` carries a callee name and argument values and no
types at all. The tier that still HAS the type is `LowerMaxonToStd`, so it writes the stamp down
against the **Std op index** of the call it is appending (`Project.stdOpElementStrides`) and
`InlineManagedPrimitives` reads it back two passes later.

The key survives because `IrModule.ops` allocates indices by `push` and the one pass in between neither
clones nor replaces a call: `insertRangeChecks` appends guards and splits blocks, which moves `opRefs`
and never ops.

⭐ **`inlineLeaves` IS NOT ON THAT LIST.** It runs AFTER `inlineManagedPrimitives` — after this table has
been read and spent — so a splice cannot reach a live key at all. A leaf may carry a call in a cold block, which a splice
clones with the body, so this key relies on that order alone.

### THE STATIC STAMP IS THE RECORD'S STAMP

A container declared over a TYPE PARAMETER is created inside a SHARED GENERIC BODY, and it stamps the
concrete element's stride all the same: the stride is a word of the instantiation's layout descriptor
(`elementStride@72`), which the shared body's create reads at run time
(`opaqueCreateReadsStrideFromDescriptor`). Read back through a concrete receiver, that same record wears a
SUBSTITUTED type (`Parser.slotTypeThroughReceiver`) — a `Bag with Byte`'s `Array with Element` field is typed
`Array with Byte`, stride 1, and its record stamps 1.

So what is provable is **`actual` = `static stamp`**: every producer of `element_size@24` stamps
`ProgramSignatures.arrayElementSize` of the value's own instance, or the descriptor's word for that same
element type (`__ManagedMemory.create` is CHECKED against its instance by `requireManagedMemoryElementSize`;
clone and slice copy their source). An element stored in a SIGNED narrow slot (an enum with a negative raw
value) strides its slot like any other; the signed flag the stamp carries above its stride tells a shared body the slot is
signed, and the body sign-extends what it reads.

### FOUR ANSWERS, and each of them is a case below

- **The word stride is known** — the record IS the word. Exactly ONE arm is emitted, into the site's own
  block: no `element_size@24` load, no compare, and no branch into the arm, because with one successor the arm IS
  the block.
- **The BYTE stride is known** — the record IS one byte per element, so exactly ONE arm is emitted, the byte
  arm, with no fork and no call.
- **A 16-bit, a 32-bit or a PACKED stride is known** — `element_size@24` is signed, and a packed element
  reads `-1`/`-2`/`-4`. Exactly ONE arm is emitted as well: a `u16` or `u32` access, or the byte load, shift
  and mask (a read-modify-write for a store) of a field that shares its byte with its neighbours.
- **A stamp none of those names is known** — an unset stride. Every fast arm is dead, so nothing is expanded
  at all: the CALL is right, and expanding would pay the load and the compares to reach the very call the
  site makes anyway.
- **Nothing is known** — an opaque `T` element inside the shared body itself, an array reached through
  an interface or an existential. The full runtime fork is emitted: the stamp is compared against 8, 1,
  -1, 2, 4, -2 and -4 in turn, and each hit is one access.

### For `__managed_get_unchecked` a known stride removes the CALL as well

The stride fork is that expansion's ONLY precondition — there is no bounds check and no empty-slot
check, because `__managed_get_unchecked`'s own body has neither. So once the stamp answers the fork
there is nothing left for a slow arm to serve, and the call is dropped rather than parked in an
unreachable block. The body of a `for v in a` over a concrete array is then genuinely **call-free**,
which is what a later hoisting pass needs: a live range crossing a call is confined to the
callee-saved registers, and this compiler refuses rather than spills.

### A SHARED GENERIC BODY HAS NO SINGLE STRIDE

`Array with T` and `__ManagedMemory with T` are ONE `GenericInstanceId`, and a generic body is compiled
**once** for every instantiation — so an opaque element has no stride to specialize on, and
`ProgramSignatures.arrayElementSize` would answer for it anyway (off the machine-word slot a type
parameter occupies). Recording that answer would specialize an `Array with Byte`'s body onto an 8-byte
access. `containerElementIsOpaque` is the refusal, and `a-shared-generic-body-keeps-the-runtime-fork`
is its control. Its dual — a record BORN in such a body and read back under a substituted concrete type
— is what the equality of the two stamps above rests on, and
`a-substituted-container-field-is-byte-strided-as-it-is-typed` is the program that pins it.

## Tests

<!-- test: a-known-word-stride-reads-with-no-dispatch -->
The shape this specialization exists for. `for v in a` over an `Array with` an 8-byte element: the loop body is
the buffer load and the element load and nothing else — no `[<rec> + 24]`, no `cmpRegImm32 …, 8`, no
`__im_stride` / `__im_byte` block, and no `callDirect __managed_get_unchecked` anywhere in `total`.

The emitted code (`--emit-ir`) is the reading: `@total` is eight instructions per element across four blocks
with no call at all — which is also why its prologue saves none of `rbx`/`r12`/`r13`.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function total(a WordArray) returns Word
	var t = 0 as Word
	for v in a 'loop'
		t = t + v
	end 'loop'
	return t
end 'total'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 6 'seed'
		a.push(i * i)
	end 'seed'
	if total(a) != 55 'sum'
		return 1
	end 'sum'
	if a.count() != 6 'count'
		return 2
	end 'count'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-known-word-stride-write-keeps-every-other-guard -->
The store mirror, and the case that says the stride specialization removes the STRIDE FORK and nothing
else. `__managed_set`'s fast arm still proves that the buffer is this record's, that the index is in
range, that the buffer exists and that nobody is viewing it — all four survive, and their refusals still
reach the slow arm's `callDirect __managed_set`. What is gone here is the `element_size@24` load, the two
compares and the byte arm; the destructor question is answered statically for a `Word` element
(`static-trivial-element`), so no `[<rec> + 40]` load appears either.

Every slot is written and read back, so a wrong width or a wrong address is a wrong exit code.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function fill(a WordArray, n Word)
	for i in 0 upto n 'each'
		try a.set(i as ElementIndex, value: i * 3 + 1) otherwise panic("set: index out of range")
	end 'each'
end 'fill'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 5 'seed'
		a.push(0)
	end 'seed'
	fill(a, n: 5)
	var seen = 0 as Word
	for i in 0 upto 5 'check'
		seen = seen + (try a.get(i) otherwise panic("get: index out of range"))
	end 'check'
	if seen != 35 'sum'
		return 1
	end 'sum'
	if (try a.get(4) otherwise panic("get: index out of range")) != 13 'last'
		return 2
	end 'last'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-byte-stamp-reads-with-the-byte-arm-alone -->
A `ByteArray` stamps `element_size@24` with 1, and the record IS one byte per element wherever it was created,
a shared generic body included. The site therefore emits the byte arm alone: the emitted code (`--emit-ir`) shows
a `loadRegBaseDisp.byte` with no `[<rec> + 24]` load, no `cmpRegImm32` and no word arm.

⚠ The values here are all genuinely byte-strided, so this case cannot go RED on its own — it is a
CODEGEN pin, and the program that makes the rule load-bearing is
`a-substituted-container-field-is-byte-strided-as-it-is-typed` below.
```maxon
typealias Total = int(0 to u64.max)

function sumBytes(b ByteArray) returns Total
	var t = 0 as Total
	for v in b 'loop'
		t = t + v as Total
	end 'loop'
	return t
end 'sumBytes'

function main() returns ExitCode
	var b = ByteArray.create()
	for i in 0 upto 10 'seed'
		b.push(i as Byte)
	end 'seed'
	if sumBytes(b) != 45 'sum'
		return 1
	end 'sum'
	try b.set(3, value: 200) otherwise panic("set: index out of range")
	if (try b.get(3) otherwise panic("get: index out of range")) != 200 'written'
		return 2
	end 'written'
	if b.count() != 10 'count'
		return 3
	end 'count'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-packed-element-is-read-and-written-through-its-field-arm -->
<!-- tier: stdlib -->
A NEGATIVE `element_size@24` IS A WIDTH, NOT AN ERROR. `int(0 to 3)` packs four elements to the byte
and stamps `-2`, which selects the two-bit arm — so the pass emits that one arm and nothing else. The
emitted code (`--emit-ir`) shows the byte load, the shift and the mask of a `get`, and the read-modify-write
of a `set`, with no `callDirect __managed_get` on the fast path and no stride fork.

The values round-trip across a byte boundary (five elements at four per byte) and the raw stamp is
asserted, so a specialization that treated `-2` as a byte stride would read one element's two bits as a
whole byte and answer wrong rather than merely slower.
```maxon
typealias Q = int(0 to 3)
typealias QArray = Array with Q

function main() returns ExitCode
	var a = QArray.create()
	a.push(0)
	a.push(1)
	a.push(2)
	a.push(3)
	a.push(2)
	if a.managed.elementSize() != -2 'stamp'
		return 1
	end 'stamp'
	try a.set(4, value: 1) otherwise panic("set: index out of range")
	var seen = 0 as Count
	for i in 0 upto 5 'read'
		seen = seen + ((try a.get(i) otherwise panic("get: index out of range")) as Count)
	end 'read'
	if seen != 7 'sum'
		return 2
	end 'sum'
	if (try a.get(3) otherwise panic("get: index out of range")) != 3 'acrossTheByte'
		return 3
	end 'acrossTheByte'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-shared-generic-body-keeps-the-runtime-fork -->
THE SHARED-BODY CONTROL. `Bag uses Element` is compiled ONCE and reached here at two different element WIDTHS —
an 8-byte `Word` and a 1-byte `Byte`. There is no stride for its body to be specialized on, and every
value below is wrong by a factor of eight in one direction or the other if it is specialized anyway: an
8-byte access into a byte-strided buffer reads seven bytes of its neighbours, and a `u8` access into a
word-strided one keeps one byte of every element.

So `Bag.at` and `Bag.put` keep the stride fork — the `element_size@24` load and the compares against each
stride — and that is the shape the emitted code (`--emit-ir`) shows. `main`'s own direct accesses on the same two
element types are specialized as usual: the difference between the two is the whole rule.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Byte = int(0 to 255)
typealias Idx = int(0 to u64.max) implements ElementIndex

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(value Element)
		items.push(value)
	end 'add'

	function put(i Idx, value Element)
		try items.set(i, value: value) otherwise panic("Bag.put: index out of range")
	end 'put'

	function at(i Idx) returns Element throws ArrayError
		return try items.get(i)
	end 'at'
end 'Bag'

typealias WordBag = Bag with Word
typealias ByteBag = Bag with Byte
typealias WordArray = Array with Word

function main() returns ExitCode
	var w = WordBag.create()
	w.add(1000000)
	w.add(2000000)
	w.put(1, value: 3000000)
	if (try w.at(0) otherwise return 1) != 1000000 'wordFirst'
		return 2
	end 'wordFirst'
	if (try w.at(1) otherwise return 1) != 3000000 'wordSecond'
		return 3
	end 'wordSecond'

	var b = ByteBag.create()
	b.add(200)
	b.add(7)
	b.put(1, value: 255)
	if (try b.at(0) otherwise return 1) != 200 'byteFirst'
		return 4
	end 'byteFirst'
	if (try b.at(1) otherwise return 1) != 255 'byteSecond'
		return 5
	end 'byteSecond'

	// The same element width reached DIRECTLY, where the stride is known and the fork is gone.
	var direct = WordArray.create()
	direct.push(9)
	if (try direct.get(0) otherwise return 1) != 9 'directWord'
		return 6
	end 'directWord'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-substituted-container-field-is-byte-strided-as-it-is-typed -->
<!-- tier: stdlib -->
⛔⛔ **THE RED-GATE CONTROL, AND THE PROGRAM THAT PINS THE RULE.** `Bag with Byte`'s `items` is
declared `Array with Element` inside a SHARED generic body, and its record stamps `element_size@24` with the
stride of the instantiation's element, read from the layout descriptor: **1**. Read from OUTSIDE that body it
wears the SUBSTITUTED type `Array with Byte`, whose static stride is **1** as well. The two agree, and that is
what lets the byte site emit its one arm and nothing else.

`elementSize()` asserts the record's own stamp, so the agreement is stated rather than assumed;
`items.get(1)` then reads element 1 through the one byte arm. A shared body that stamped the machine word
again would make this case answer **0** where 7 is correct — one byte out of the middle of an eight-byte
slot — and `elementSize()` would answer 8 first, exit code 1.
```maxon
typealias Byte = int(0 to 255)

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(value Element)
		items.push(value)
	end 'add'
end 'Bag'

typealias ByteBag = Bag with Byte

function main() returns ExitCode
	var b = ByteBag.create()
	b.add(200)
	b.add(7)
	if b.items.managed.elementSize() != 1 'theRecordIsByteStrided'
		return 1
	end 'theRecordIsByteStrided'
	if (try b.items.get(0) otherwise return 2) != 200 'first'
		return 2
	end 'first'
	if (try b.items.get(1) otherwise return 3) != 7 'second'
		return 3
	end 'second'
	return 0
end 'main'
```
```exitcode
0
```
