---
feature: static-trivial-element
status: experimental
keywords: [optimizer, codegen, array, element-destroy, inline, managed-memory, ownership, generics]
category: codegen
---
# The element ownership the compiler already knows

## Documentation

`InlineManagedPrimitives` (EC1) puts the fast path of `__managed_get` and `__managed_set` inline at
every array read and write. Both arms ask `element_destroy@40` — a **runtime** field of the array
record — whether the element owns anything: the write arm refuses a managed element outright (the
occupant it would overwrite is the runtime's to destroy), and the read arm sends a loaded `0` through
an extra `__im_empty` block to distinguish an empty managed slot from a genuine zero. Until this row an
`Array with Integer` element access paid, per access:

```
mov rdx, [rbx + 40]      ; element_destroy@40
cmp rdx, 0
jne __im_slow            ; (write)  — or —  je __im_empty ... cmp rax, 0 / jne ... (read)
```

`Array with Integer` stamps that field with `TrivialDestructor` (0) in every program that ever runs.
The stamp is decided by the ELEMENT TYPE — `Parser.arrayElementDestroyValue` writes it from
`ProgramSignatures.containerElementOwesDrop` — and the compiler has it at the moment it emits the call.

### How the fact reaches the pass that needs it

The same way the stride does (`static-stride-specialization`): the Std tier is type-free, so
`LowerMaxonToStd.recordStaticElementFacts` — the one door every `__managed_*` call passes through —
writes the answer down against the **Std op index** of the call it is appending
(`Project.stdOpTrivialElementSites`) and `InlineManagedPrimitives` reads it back. The gate is the
stride's gate, verbatim: a typed array receiver (`isArrayInstanceAt`) whose element is not an opaque
type parameter (`containerElementIsOpaque`). The key survives for the stride's reason — the one pass in
between, `insertRangeChecks`, splits blocks and never clones or replaces a call.

### WHY THE STAMP IS ALWAYS THE RECORD'S — unlike the stride

`element_destroy@40` has two writers — `__managed_create`'s second argument, and
`BufferOwnership.immortalRecordBytes` for a record that is image data (a managed image record's slot is
then filled by the `stampOwnershipWord` relocation, which an empty callee leaves at 0) — and every
producer stamps it from the element type of the record's own instance: a concrete creation site passes
`arrayElementDestroyValue(giid)`; a creation site inside a SHARED GENERIC BODY reads `destroyFunc@40` of
the threaded layout descriptor, which is `0` for a trivial instance (`LayoutDescriptor.maxon`); an
immortal empty container stamps `columnDestroyStamp`, decided by `slotTypeIsManaged`; a view (`slice`,
`clone`) copies its source record's field. Those are three deciders (`typeOwesDrop` at a concrete site,
the descriptor's ownership protocol, `slotTypeIsManaged`), not one, and they agree in the direction that
matters: where `typeOwesDrop` is false the other two answer nothing. So a record typed over an element
that owes no drop carries `0` wherever it was born. The W57 trap that makes a BYTE stride untrustworthy — a record
created at the machine-word slot of a shared body and read back under a substituted type — does not
arise here: the slot's WIDTH is the body's, but the element's OWNERSHIP is the instantiation's, and the
descriptor carries that per instantiation.

The refusal direction is the same as the stride's: an opaque `T` inside the shared body records
nothing, and such a site keeps both guards exactly as before.

### What the two arms drop

- **Write** (`emitStoreArm`): the `@40` load, the compare and the branch to the slow arm. The four
  remaining guards — ownership, the bound, the buffer, the sharing test — are untouched.
- **Read** (`emitCheckedGetTail`): the `@40` load, the compare, the `__im_empty` block and its
  `cmp value, 0` — the continuation is reached by one unconditional edge carrying `(value, noError)`.

⚠ A green case here proves nothing on its own — the change removes a guard the fast arm never took on
these programs. The evidence is the committed fragment (no `[<rec> + 40]` in the two first cases) and
the three CONTROLS below, measured under sabotage (the stamp applied to every typed array site,
ignoring `containerElementOwesDrop`): the write control and the substituted-field control exit **101**
(a leak), the read control exits **0** where 42 is the answer.

## Tests

<!-- test: an-integer-element-read-asks-no-destructor-question -->
The shape the row was opened for. A checked `get` over `Array with Integer`: the fast arm is the
bound, the buffer load, the element load and the edge to the continuation. No `[<rec> + 40]` load, no
`__im_empty` block, no `cmpRegImm32 …, 0` on the loaded element.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max)
typealias WordArray = Array with Word

function pick(a WordArray, i Idx) returns Word
	return try a.get(i) otherwise panic("pick: index out of range")
end 'pick'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 6 'seed'
		a.push(i * i)
	end 'seed'
	var t = 0
	for i in 0 upto 6 'sum'
		t = t + pick(a, i: i)
	end 'sum'
	if t != 55 'total'
		return 1
	end 'total'
	if pick(a, i: 0) != 0 'zeroIsAValue'
		return 2
	end 'zeroIsAValue'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-integer-element-write-asks-no-destructor-question -->
The store mirror. `__managed_set`'s fast arm still proves the buffer is this record's, that the index
is in range, that the buffer exists and that nobody is viewing it; what is gone is the `[<rec> + 40]`
load and its branch. Every slot is written and read back, so a store that went to the wrong place is a
wrong exit code.
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
	a.resize(5)
	fill(a, n: 5)
	var seen = 0
	for v in a 'check'
		seen = seen + v
	end 'check'
	if seen != 35 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-managed-element-write-still-destroys-the-occupant -->
The control for the write arm. A `set` over a managed element DESTROYS the occupant it overwrites, and
that is the runtime's job: the stamp says the element owes a drop, so the guard stays and the write
takes the slow arm. Sabotage — stamping this site trivial — stores over four Strings nobody releases,
and the leak gate answers exit 101.
```maxon
typealias Idx = int(0 to u64.max)
typealias Names = Array with String

function relabel(a Names, i Idx)
	try a.set(i, value: "renamed-{i}") otherwise panic("set: index out of range")
end 'relabel'

function main() returns ExitCode
	var a = Names.create()
	for i in 0 upto 4 'seed'
		a.push("original-{i}")
	end 'seed'
	for i in 0 upto 4 'rewrite'
		relabel(a, i: i)
	end 'rewrite'
	var bytes = 0
	for s in a 'measure'
		bytes = bytes + s.byteLength()
	end 'measure'
	if bytes != 36 'total'
		return 1
	end 'total'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-managed-element-read-still-reports-an-empty-slot -->
The control for the read arm. A slot opened through the raw buffer holds nothing; read back through
`Array.get` over a managed element it is `emptySlot`, which only the `__im_empty` arm can say. The
stamp says the element owes a drop, so that arm stays. Sabotage — stamping this site trivial — hands
the null slot back as a value: the program exits 0 where 42 is the answer.
```maxon
typealias Names = Array with String

function main() returns ExitCode
	var a = Names.create()
	a.reserve(2)
	try a.managed.setLength(2) otherwise panic("setLength: capacity just reserved for 2")
	try a.get(0) otherwise (e) 'handler'
		match e 'check'
			emptySlot then return 42
			indexOutOfBounds then return 99
		end 'check'
	end 'handler'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: a-substituted-container-field-keeps-its-element-answer -->
A shared generic body records nothing, so `Bag.put`/`Bag.at` keep both guards for every
instantiation. The same record reached through a CONCRETE receiver — `w.items` typed `Array with Word`,
`n.items` typed `Array with String` — is stamped per instantiation: the word field drops the guard, the
String field keeps it, and the Strings the direct `set` overwrites are still destroyed (exit 0, not
101).
```maxon
typealias Word = int(i64.min to i64.max)
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
typealias NameBag = Bag with String

function main() returns ExitCode
	var w = WordBag.create()
	w.add(10)
	w.add(20)
	w.put(1, value: 30)
	try w.items.set(0, value: 40) otherwise return 1
	if (try w.at(0) otherwise return 1) + (try w.items.get(1) otherwise return 1) != 70 'words'
		return 2
	end 'words'

	var n = NameBag.create()
	n.add("alpha")
	n.add("beta")
	n.put(0, value: "gamma")
	try n.items.set(1, value: "delta") otherwise return 1
	let first = try n.at(0) otherwise return 1
	let second = try n.items.get(1) otherwise return 1
	if first.byteLength() + second.byteLength() != 10 'names'
		return 3
	end 'names'
	return 0
end 'main'
```
```exitcode
0
```
