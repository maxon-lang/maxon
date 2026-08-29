---
feature: static-stride-specialization
status: experimental
keywords: [optimizer, codegen, array, element-size, stride, inline, managed-memory, generics]
category: codegen
---
# The element stride the compiler already knows

## Documentation

`ManagedMemoryRuntime.emitStrideDispatch` forks an element access on `element_size@24` — a **runtime**
field of the array record — into a machine-word arm, a single-byte arm, and everything else.
`InlineManagedPrimitives` (EC1) puts that fork inline at every array read and write in every program,
so until EC15 an `Array with Integer` element access paid, per access:

```
mov rax, [rbx + 24]      ; element_size@24
cmp rax, 8
jne __im_stride          ; ... which compares against 1 and falls through to a runtime CALL
```

`Array with Integer` stamps that field with 8 in every program that ever runs. The stamp is
`ProgramSignatures.arrayElementSize`'s, it is decided by the ELEMENT TYPE, and the compiler has it at
the moment it emits the call.

### How the stamp reaches the pass that needs it

The Std tier is deliberately type-free: `StdOp.call` carries a callee name and argument values and no
types at all. The tier that still HAS the type is `LowerMaxonToStd`, so it writes the stamp down
against the **Std op index** of the call it is appending (`Project.stdOpElementStrides`) and
`InlineManagedPrimitives` reads it back two passes later.

The key survives because `IrModule.ops` allocates indices by `push` and the two passes in between
neither clone nor replace a call: `insertRangeChecks` appends guards and splits blocks, and
`inlineLeaves` copies only LEAF bodies — and a leaf holds no call by rule, so it can neither copy nor
re-issue a managed primitive.

### THE STATIC STAMP IS NOT ALWAYS THE RECORD'S STAMP

A container declared over a TYPE PARAMETER is created inside a SHARED GENERIC BODY, where every `T`
occupies one machine word: `Parser.emitOpaqueArrayCreateOp` stamps `LAYOUT_TYPE_PARAM_SLOT_BYTES` and
not the concrete element's width, because the one compiled body moves elements as words whatever the
instantiation. Read back through a concrete receiver, that same record wears a SUBSTITUTED type
(`Parser.slotTypeThroughReceiver`) — a `Bag with Byte`'s `Array with Element` field is typed
`Array with Byte`, stride 1, while its record stamps 8.

So what is provable is **`actual` ∈ {`static stamp`, `MachineWordBytes`}**: every other producer of
`element_size@24` stamps `ProgramSignatures.arrayElementSize` of the value's own instance
(`__ManagedMemory.create` is CHECKED against its instance by `requireManagedMemoryElementSize`;
clone and slice copy their source), and the one that does not stamps the machine word.

### THREE ANSWERS, and each of them is a case below

- **The word stride is known** — both possibilities ARE the word, so the word arm is right either way.
  Exactly ONE arm is emitted, into the site's own block: no `element_size@24` load, no compare, and no
  branch into the arm, because with one successor the arm IS the block.
- **The BYTE stride is known** — the record is 1 or 8, which are two DIFFERENT single-op arms, so the
  site still has to ask. The runtime fork stands, and the fork is exactly the question that
  distinguishes them. (Left open: a byte-stamped site could keep both arms and route the fork's third
  edge to the WORD arm rather than to the call, which is sound by the same reading and would make a
  `for v in b` over a `ByteArray` call-free the way the word case now is.)
- **A stride is known that takes NEITHER arm** — a 2- or 4-byte element, an unset stride, or a sub-byte
  PACKED one (`element_size@24` is signed: a packed element reads `-1`/`-2`/`-4`). The record is that
  stride or the word, and the CALL is right for both, so nothing is expanded at all. That is strictly
  better than expanding: such a site used to pay the load and both compares to reach the very call it
  was always going to make.
- **Nothing is known** — an opaque `T` element inside the shared body itself, an array reached through
  an interface or an existential. The runtime fork is emitted exactly as before.

### For `__managed_get_unchecked` a known stride removes the CALL as well

The stride fork is that expansion's ONLY precondition — there is no bounds check and no empty-slot
check, because `__managed_get_unchecked`'s own body has neither. So once the stamp answers the fork
there is nothing left for a slow arm to serve, and the call is dropped rather than parked in an
unreachable block. The body of a `for v in a` over a concrete array is then genuinely **call-free**,
which is what a later hoisting pass needs: a live range crossing a call is confined to the
callee-saved registers, and this compiler refuses rather than spills.

### A SHARED GENERIC BODY HAS NO SINGLE STRIDE (W57)

`Array with T` and `__ManagedMemory with T` are ONE `GenericInstanceId`, and a generic body is compiled
**once** for every instantiation — so an opaque element has no stride to specialize on, and
`ProgramSignatures.arrayElementSize` would answer for it anyway (off the machine-word slot a type
parameter occupies). Recording that answer would specialize an `Array with Byte`'s body onto an 8-byte
access. `containerElementIsOpaque` is the refusal, and `a-shared-generic-body-keeps-the-runtime-fork`
is its control. Its dual — a record BORN in such a body and read back under a substituted concrete type
— is what the byte rule above exists for, and
`a-substituted-container-field-is-word-strided-however-it-is-typed` is the program that measured it.

## Tests

<!-- test: a-known-word-stride-reads-with-no-dispatch -->
The shape the row was opened for. `for v in a` over an `Array with` an 8-byte element: the loop body is
the buffer load and the element load and nothing else — no `[<rec> + 24]`, no `cmpRegImm32 …, 8`, no
`__im_stride` / `__im_byte` block, and no `callDirect __managed_get_unchecked` anywhere in `total`.

The committed fragment is the reading: `@total` used to be twelve instructions per element across five
blocks with a call on one of them, and is now eight across four with no call at all — which is also why
its prologue no longer saves `rbx`/`r12`/`r13`.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function total(a WordArray) returns Word
	var t = 0
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
The store mirror, and the case that says the specialization removes the STRIDE FORK and nothing else.
`__managed_set`'s fast arm still proves the element has no destructor, that the index is in range, that
the buffer is this record's, that it exists and that nobody is viewing it — all five survive, and their
refusals still reach the slow arm's `callDirect __managed_set`. What is gone is the `element_size@24`
load, the two compares and the byte arm.

Every slot is written and read back, so a wrong width or a wrong address is a wrong exit code.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function fill(a WordArray, n Word)
	for i in 0 upto n 'each'
		try a.set(i, value: i * 3 + 1) otherwise panic("set: index out of range")
	end 'each'
end 'fill'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 5 'seed'
		a.push(0)
	end 'seed'
	fill(a, n: 5)
	var seen = 0
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

<!-- test: a-byte-stamp-keeps-the-runtime-fork -->
A `ByteArray` stamps `element_size@24` with 1 — and that is the one known stride the compiler may NOT
act on, because a record wearing a byte-strided type may still be the word-strided one a shared generic
body created. 1 and 8 are two DIFFERENT single-op arms, so the site keeps `emitStrideDispatch`: the
committed fragment still shows the `[<rec> + 24]` load, the `cmpRegImm32 …, 8` and the `cmpRegImm32
…, 1` around a `loadRegBaseDisp.byte` and a `loadRegBaseIndexScale.word64`.

⚠ The values here are all genuinely byte-strided, so this case cannot go RED on its own — it is a
CODEGEN pin, and the program that makes the rule load-bearing is
`a-substituted-container-field-is-word-strided-however-it-is-typed` below.
```maxon
typealias Byte = int(0 to 255)
typealias Total = int(0 to u64.max)

function sumBytes(b ByteArray) returns Total
	var t = 0
	for v in b 'loop'
		t = t + v
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

<!-- test: a-packed-element-is-not-inlined-at-all -->
A NEGATIVE `element_size@24` IS A WIDTH, NOT AN ERROR. `int(0 to 3)` packs four elements to the byte
and stamps `-2`, which selects NEITHER single-op arm — so the compiler knows, at compile time, that
every fast arm this pass could emit is dead, and emits none of them. The fragment therefore shows a
plain `callDirect __managed_get` / `__managed_set` with no `__im_` block anywhere around it, where
before it showed the guard chain leading to that same call.

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
	var seen = 0
	for i in 0 upto 5 'read'
		seen = seen + (try a.get(i) otherwise panic("get: index out of range"))
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
THE W57 CONTROL. `Bag uses Element` is compiled ONCE and reached here at two different element WIDTHS —
an 8-byte `Word` and a 1-byte `Byte`. There is no stride for its body to be specialized on, and every
value below is wrong by a factor of eight in one direction or the other if it is specialized anyway: an
8-byte access into a byte-strided buffer reads seven bytes of its neighbours, and a `u8` access into a
word-strided one keeps one byte of every element.

So `Bag.at` and `Bag.put` keep `emitStrideDispatch` — the `element_size@24` load and both compares —
and that is the shape the committed fragment shows. `main`'s own direct accesses on the same two
element types are specialized as usual: the difference between the two is the whole rule.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Byte = int(0 to 255)
typealias Idx = int(0 to u64.max)

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function add(value Element)
		items.push(value)
	end 'add'

	export function put(i Idx, value Element)
		try items.set(i, value: value) otherwise panic("Bag.put: index out of range")
	end 'put'

	export function at(i Idx) returns Element throws ArrayError
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

<!-- test: a-substituted-container-field-is-word-strided-however-it-is-typed -->
⛔⛔ **THE RED-GATE CONTROL, AND THE PROGRAM THAT MEASURED THE RULE.** `Bag with Byte`'s `items` is
declared `Array with Element` inside a SHARED generic body, so its record was created at the opaque
machine-word slot and stamps `element_size@24` with **8**. Read from OUTSIDE that body it wears the
SUBSTITUTED type `Array with Byte`, whose static stride is **1**. The two disagree, and the record is
the one that is true.

`elementSize()` asserts the record's own stamp, so the disagreement is stated rather than assumed;
`items.get(1)` then reads element 1 through it. Change `strideDispatchPlanForStamp`'s byte arm from
`runtimeFork` back to `singleArm(SingleOpStride.byte)` and this case answers **0** where 7 is correct —
one byte out of the middle of an eight-byte slot, exit code 3. MEASURED: with that arm restored this is
the ONLY case in this file that goes red, `a-byte-stamp-keeps-the-runtime-fork` included, which is why a
codegen pin could not have stood in for it.
```maxon
typealias Byte = int(0 to 255)

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function add(value Element)
		items.push(value)
	end 'add'
end 'Bag'

typealias ByteBag = Bag with Byte

function main() returns ExitCode
	var b = ByteBag.create()
	b.add(200)
	b.add(7)
	// The record was born in the shared body at the machine-word slot, whatever the field's type says.
	if b.items.managed.elementSize() != 8 'theRecordIsWordStrided'
		return 1
	end 'theRecordIsWordStrided'
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
