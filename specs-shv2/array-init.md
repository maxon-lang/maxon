---
feature: array-init
status: stable
keywords: [array, __ManagedMemory, init, adoption, buffer, surface, ownership, refcount]
category: memory-safety
---

# `Array.init(managed)` — the buffer's second surface, named

## Documentation

`stdlib/Array.maxon:113-115` declares the static every `Array` has:

```
export static function init(managed ElementMemory) returns Self
	return Self{managed: managed}
end 'init'
```

where `ElementMemory = __ManagedMemory with Element`. It is the only way back from a raw
buffer to the container around it — the inverse of `arr.managed`, which `array-slots.md`
already uses to reach the buffer under an array.

### In shv2 it is an IDENTITY on the record, not a copy

`Array with T` and `__ManagedMemory with T` are ONE RECORD with TWO SURFACES:
`SignatureIndex.ManagedMemoryTypeName` is registered as a generic ALIAS of the very
`Array with Byte` instance a `b"…"` literal has. So the reference's `Self{managed: managed}`
allocates nothing here — there is no second record for the buffer to be a field of, and the
bytes the two names reach are the same bytes.

⚠ **It is therefore NOT `String.init(managed)`, whose same-named, same-signature arm COPIES**
(`__str_from_bytes`). A String record genuinely differs from an Array record at slot `@40`, so
that one has to build a new one. Here a copy would be both wasteful and wrong: the whole point
of the static is that the caller keeps addressing the buffer it just filled.

### What the call actually does: a retain, and a FRESH name

Two facts, and each is load-bearing on its own:

- **The result is a FRESH `ValueId`.** The two surfaces are told apart by PROVENANCE — a value
  minted by `__ManagedMemory.create` carries the buffer mark, and everything else is an
  ordinary array (`Parser.markBufferSurface`). The array surface is the ABSENCE of that mark,
  and there is no un-mark, so a pure identity returning the argument's own `ValueId` would hand
  back a value that still dispatched on the BUFFER roster: `init(…).push(…)` would be refused as
  an unknown `Array` method. `the-surface-flips` below is the case that says so.
- **The record is RETAINED** (`__mm_retain`). The source buffer is still live and still drops at
  its own scope exit, so the adopted array must be a second owner rather than a stolen one.
  `__mm_retain` raises the same refcount word `__managed_decref` lowers (both go through
  `MmRuntime.emitRefcountCheckToLastOwner`), so `let a = ByteArray.init(mm)` traces
  rc 0 → retain → 1 → `a` drops → 0 → `mm` drops → freed. Every case here would exit **101**
  if that did not balance.

### The argument rule is SAME-INSTANCE, not "is a byte buffer"

`String.init`'s door (`requireManagedMemoryValue`) is BYTE-ONLY — it asks
`arrayElementSize == 1` — which is right for a String and wrong here, in the direction that
matters: it would ACCEPT `IntArray.init(<a byte buffer>)`, an `Array with Integer` striding by 8
through a byte-packed allocation, reading whichever bytes fell on the boundaries. A silent wrong
answer, not an error. The rule is instead that the argument's generic instance must EQUAL the
receiver alias's, which is element-agnostic and strictly stronger.
`error.a-buffer-of-the-wrong-element` is that refusal.

It is not "carries the buffer mark" either, and that is the second half of the choice. The mark
(`bufferSurfaceValues`) is a per-function fact about a `ValueId`, so it is GONE after a closure capture
while the record and its stride are unchanged — `a-captured-buffer-adopts-inside-a-closure` is measured
proof. A door keyed on the mark would refuse `stdlib/File.maxon:135`'s shape for a reason having nothing
to do with the stride. So an ORDINARY array of the instance is admitted too
(`an-ordinary-array-of-the-instance-adopts`), and adopting one is an ordinary co-owning retain.

### ⚠ Every adoption reachable TODAY is a BYTE one, and the reason is not this door

MEASURED, and it is worth writing down because the door looks byte-agnostic and is:

| receiver alias | buffer | verdict |
|---|---|---|
| `Array with Byte` | `__ManagedMemory.create(n, elementSize: 1)` | adopts |
| `Array with Integer` | `__ManagedMemory.create(n, elementSize: 8)` | refused — `Array_int` |
| `Array with Small` (`int(0 to 200)`) | `__ManagedMemory.create(n, elementSize: 1)` | refused — `Array_Byte` |

The verdict column names the INSTANCE the buffer minted. The DIAGNOSTIC spells that instance by the
`typealias` the program declares for it where one exists (user ruling, 2026-08-04), which is why the byte
row's refusal below reads `got 'ByteArray'` — `stdlib/File.maxon`'s own
`export typealias ByteArray = Array with Byte`, the same instance under a name a person wrote. The word
row has no such name, which is the next paragraph's whole subject, so it still reads `Array_int`.

`managedMemoryInstanceForElementSize` mints exactly two instances — `Array with Byte` for width 1
and `Array with int` for width 8 — and **`Array with int` is a type no program can name**:
`typealias IntArray = Array with int` is `E2061: Cannot use bare type 'int' as a type argument; use
a ranged typealias instead`, and every ranged alias over it (`Integer`, `Small`) interns a DIFFERENT instance, exactly
as `default-values.md` and `string-views.md` already pin for array literals (`Array_int` vs
`Array_Integer`). So the only nameable alias a `__ManagedMemory` value can be adopted into is
`Array with Byte`. That is the standing `Array_int`-unnameability gap, observed here rather than
caused here — a door that keyed on element WIDTH instead would close it only by giving up the
soundness the row above depends on.

`stdlib/File.maxon:135`'s `return ByteArray.init(managed)` is the byte row, which is why this
rung unblocks it.

## Tests

<!-- test: count-agrees-with-the-buffers-length -->
The adopted array's `count()` is the buffer's `length()`, because it is the same `length@8`. The
value is pinned as well as the agreement — two readers of one field agree even when both are
wrong.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(3) otherwise return 2
	let a = ByteArray.init(mm)
	if a.count() != mm.length() 'disagree'
		return 4
	end 'disagree'
	return a.count() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: written-through-the-buffer-read-through-the-array -->
Bytes stored through the BUFFER surface (`setByte`) read back through the ARRAY surface (`get`).
This is the whole purpose of the static: fill a raw buffer, then name it as the container.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(3) otherwise return 2
	try mm.setByte(0, value: 7) otherwise return 3
	try mm.setByte(1, value: 9) otherwise return 3
	try mm.setByte(2, value: 11) otherwise return 3
	let a = ByteArray.init(mm)
	let v0 = try a.get(0) otherwise return 4
	let v1 = try a.get(1) otherwise return 4
	let v2 = try a.get(2) otherwise return 4
	return (v0 + v1 + v2) as ExitCode
end 'main'
```
```exitcode
27
```

<!-- test: the-surface-flips -->
⭐ **THE CASE A PURE IDENTITY FAILS.** `push` is an `Array` member the buffer surface does not
have. If `init` handed back the argument's own `ValueId`, that value would still carry the buffer
mark and this call would be refused as an unknown `Array` method — the roster, exactly inverted.
It compiles because the retain mints a fresh, unmarked name.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	var a = ByteArray.init(mm)
	a.push(5)
	a.push(6)
	return a.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: the-buffer-keeps-its-own-surface -->
The other half of the flip, and it needs no error message to say it: the SOURCE is untouched by
the adoption, so it still answers on the buffer roster (`setByte`, `length`) — while the result
answers on the array roster (`push`, `get`). Both surfaces are live at once over one record, and
a write through either shows through the other.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	var a = ByteArray.init(mm)
	a.push(4)
	try mm.setByte(0, value: 8) otherwise return 3
	if mm.length() != 3 'lengthNotShared'
		return 4
	end 'lengthNotShared'
	let v0 = try a.get(0) otherwise return 5
	let v2 = try a.get(2) otherwise return 5
	return (v0 + v2) as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: a-differently-named-byte-alias-still-adopts -->
The receiver is matched by INSTANCE, never by alias NAME: a second alias over `Array with Byte`
adopts the same buffer, and the two names denote one type.
```maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	try mm.setByte(0, value: 20) otherwise return 3
	try mm.setByte(1, value: 22) otherwise return 3
	let a = Bytes.init(mm)
	let v0 = try a.get(0) otherwise return 4
	let v1 = try a.get(1) otherwise return 4
	return (v0 + v1) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: both-owners-reach-scope-exit -->
⭐ **THE REFCOUNT CASE.** The buffer and the array it was adopted into are two owners of one
record, and both die at the same scope exit. A missing retain frees it twice; a retain with no
matching drop leaks and the run exits **101**. Neither is expressible as a wrong exit code, which
is why this case pins 0 rather than a computed value — the leak checker is the assertion.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let mm = try __ManagedMemory.create(8, elementSize: 1) otherwise return 1
	try mm.setLength(4) otherwise return 2
	let a = ByteArray.init(mm)
	let b = ByteArray.init(mm)
	if a.count() != 4 'badA'
		return 3
	end 'badA'
	if b.count() != 4 'badB'
		return 4
	end 'badB'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-unbound-result-is-dropped-at-the-statement -->
The result of `init` bound to no name is a statement-scoped owned temporary, exactly as a
`create()`'s is. The buffer is still readable afterwards — the temporary's drop lowered the count,
it did not free the record — and the program must not exit 101.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	if ByteArray.init(mm).count() != 2 'badTempCount'
		return 3
	end 'badTempCount'
	return mm.length() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: the-array-outlives-the-buffer-binding -->
⭐ **THE HAND-OFF.** The buffer local dies at the end of `build`, lowering the count by one; the
array it was adopted into is moved OUT and becomes the caller's sole owner. This is the shape
`stdlib/File.maxon:135`'s `return ByteArray.init(managed)` has, and the one an unbalanced retain
turns into a use-after-free rather than a leak.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function build() returns ByteArray
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise panic("create failed")
	try mm.setLength(2) otherwise panic("setLength failed")
	try mm.setByte(0, value: 6) otherwise panic("setByte failed")
	try mm.setByte(1, value: 7) otherwise panic("setByte failed")
	return ByteArray.init(mm)
end 'build'

function main() returns ExitCode
	let a = build()
	let v0 = try a.get(0) otherwise return 1
	let v1 = try a.get(1) otherwise return 1
	return (v0 + v1) as ExitCode
end 'main'
```
```exitcode
13
```

<!-- test: a-declared-buffer-parameter-adopts -->
⭐ **THE `stdlib/File.maxon:135` SHAPE, EXACTLY.** The buffer arrives as a parameter DECLARED
`__ManagedMemory` — which is where the surface mark comes from at a function boundary
(`markDeclaredBufferSurface`) rather than from a producing call — and the array is handed straight
back out. The parameter is BORROWED, so the retain is what makes the returned array an owner at
all; without it the caller would receive a record whose only owner is `main`'s `mm`.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function wrap(managed __ManagedMemory) returns ByteArray
	return ByteArray.init(managed)
end 'wrap'

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	try mm.setByte(0, value: 20) otherwise return 3
	try mm.setByte(1, value: 22) otherwise return 3
	let a = wrap(mm)
	let v0 = try a.get(0) otherwise return 4
	let v1 = try a.get(1) otherwise return 4
	return (v0 + v1) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: sixty-four-adoptions-of-one-buffer -->
The retain/drop pair has to balance every time round, not once: 64 adoptions of one record, each
dropped at the loop body's scope exit. An off-by-one either way is exit 101 or a use-after-free on
the next iteration.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(3) otherwise return 2
	var i = 0
	var total = 0
	while i < 64 'spin'
		let a = ByteArray.init(mm)
		total = total + a.count()
		i = i + 1
	end 'spin'
	if total != 192 'badTotal'
		return 3
	end 'badTotal'
	return mm.length() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-reallocating-grow-is-visible-through-every-adoption -->
⭐ **THE ADOPTION IS OF THE RECORD, NOT OF THE BYTES.** Two adoptions of one buffer, then 200
`push`es through the first — enough to reallocate `buffer@0` several times. The second adoption
must see all 200, because both names address the same record and the record is what holds the
pointer. An implementation that copied anything at `init` would read a stale buffer here.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let mm = try __ManagedMemory.create(2, elementSize: 1) otherwise return 1
	var a = ByteArray.init(mm)
	var b = ByteArray.init(mm)
	var i = 0
	while i < 200 'fill'
		a.push(1)
		i = i + 1
	end 'fill'
	if b.count() != 200 'notShared'
		return 2
	end 'notShared'
	let last = try b.get(199) otherwise return 3
	return (last + mm.length() - 200) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: the-adopted-array-moves-into-a-struct-field -->
The adopted array is consumed by a static factory and stored in a field, while the buffer local
dies at `build`'s exit. The record then has exactly one owner — the struct — and reaches `main`
alive.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Holder
	export var bytes as ByteArray

	export static function create(bytes ByteArray) returns Self
		return Self{bytes: bytes}
	end 'create'
end 'Holder'

function build() returns Holder
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise panic("create failed")
	try mm.setLength(2) otherwise panic("setLength failed")
	try mm.setByte(0, value: 30) otherwise panic("setByte failed")
	try mm.setByte(1, value: 12) otherwise panic("setByte failed")
	return Holder.create(ByteArray.init(mm))
end 'build'

function main() returns ExitCode
	let h = build()
	let v0 = try h.bytes.get(0) otherwise return 1
	let v1 = try h.bytes.get(1) otherwise return 1
	return (v0 + v1) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: an-ordinary-array-of-the-instance-adopts -->
⭐ **WHAT THE DOOR ACTUALLY ASKS, PINNED (review).** The rule is SAME-INSTANCE, and that admits an
ORDINARY array of the instance as well as a buffer — `requireSameArrayInstance` never consults the
buffer mark. Adopting one is an ordinary co-owning retain: `src` and `b` are two owners of one record,
so a `push` through either shows through the other and both drop at the same scope exit. The stricter
reading (require `valueIsBufferSurface`) is not merely narrower, it is wrong — see
`a-captured-buffer-adopts-inside-a-closure` below, where the mark is already gone and the adoption must
still work.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var src = ByteArray.create()
	src.push(1)
	src.push(2)
	var b = ByteArray.init(src)
	b.push(3)
	return (src.count() * 10 + b.count()) as ExitCode
end 'main'
```
```exitcode
33
```

<!-- test: a-captured-buffer-adopts-inside-a-closure -->
⭐ **THE MARK DOES NOT CROSS A CLOSURE BOUNDARY AND THE ADOPTION MUST.** `bufferSurfaceValues` is a
per-function fact about a `ValueId`, so a captured `mm` has lost it inside the closure body — measured:
`mm.length()` there is already refused. `ByteArray.init(mm)` nonetheless adopts, because the door asks
the INSTANCE, which erasure cannot take away. This is the case that makes the door's choice of test a
correctness property rather than a preference: keyed on the mark, `stdlib/File.maxon:135`'s shape would
stop compiling the day it moved inside a closure.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Integer = int(i64.min to i64.max)
typealias Counter = function(Integer) returns Integer

function apply(f Counter, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	let r = apply(function(n Integer) gives ByteArray.init(mm).count() + n, x: 5)
	return (r * 10 + mm.length()) as ExitCode
end 'main'
```
```exitcode
72
```

<!-- test: an-opaque-element-array-adopts-and-becomes-the-last-owner -->
⭐⭐ **THE OPAQUE INSTANCE, AND THE ADOPTION AS SOLE OWNER.** `parseArrayStaticCall`'s header claims the
adoption serves an OPAQUE instance (`Array with Element` inside a generic body) exactly as a concrete
one, "element-agnostic by construction" — a claim nothing ran until this case. It is reachable through
a nested `typealias ElementArray = Array with Element` whose own FIELD supplies the same instance. The
element is a `String`, so the record's elements are managed, and `drain` then drops the container's
reference — leaving the adopted array the LAST owner, whose drop must free the record AND destroy both
Strings. A wrong drop callee for the retained value is exit **101** or a double free; neither is
expressible as a wrong count.
```maxon
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{items: ElementArray.create()}
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function drain() returns Count
		let t = ElementArray.init(self.items)
		self.items = ElementArray.create()
		return t.count()
	end 'drain'
end 'Container'

typealias StrContainer = Container with String

function main() returns ExitCode
	var c = StrContainer.create()
	c.push("ab")
	c.push("cd")
	let n = c.drain()
	return (n * 10 + c.items.count()) as ExitCode
end 'main'
```
```exitcode
20
```

<!-- test: an-inner-alias-adopts -->
`parseArrayStaticCall` has TWO call sites and this rung edited both: a top-level generic alias, and an
alias declared INSIDE a type body (`enclosingInnerAliases` → `innerArrayStatic`). Every case above
reaches only the first. This one reaches the second, in the shape it will actually be written — a
static factory adopting its `__ManagedMemory` parameter straight into the type's own field.
```maxon
typealias Byte = int(0 to u8.max)

type Holder
	typealias Inner = Array with Byte

	export var bytes as Inner

	export static function wrap(mm __ManagedMemory) returns Self
		return Self{bytes: Inner.init(mm)}
	end 'wrap'
end 'Holder'

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	try mm.setByte(0, value: 20) otherwise return 3
	try mm.setByte(1, value: 22) otherwise return 3
	let h = Holder.wrap(mm)
	let v0 = try h.bytes.get(0) otherwise return 4
	let v1 = try h.bytes.get(1) otherwise return 4
	return (v0 + v1) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: the-adopted-array-drops-on-the-throw-edge -->
⭐ **RELEASE ON EVERY PATH, INCLUDING THE ONE THAT DOES NOT RETURN.** The adopted array is a BOUND owner
when the function throws past it. Its drop rides the unwind edge or it does not ride at all, and a
missing one is invisible in a single call — so the pair is run 8 times over both edges, thrown and
returned, and the leak checker is the assertion.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

enum Boom implements Error
	bad
end 'Boom'

function adoptThenThrow(fail bool) returns Integer throws Boom
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise panic("create failed")
	try mm.setLength(2) otherwise panic("setLength failed")
	let a = ByteArray.init(mm)
	if fail 'boom'
		throw Boom.bad
	end 'boom'
	return a.count()
end 'adoptThenThrow'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 8 'spin'
		total = total + (try adoptThenThrow(true) otherwise 1)
		total = total + (try adoptThenThrow(false) otherwise 9)
		i = i + 1
	end 'spin'
	return total as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
24
```

<!-- test: an-unbound-adoption-is-dropped-when-the-callee-throws -->
The other half of the same question, one owner-kind over: the adoption is UNBOUND — a statement-scoped
pending temporary handed straight to a callee — and the callee throws before it ever reads it. The
temporary's drop is owed by the statement, and the error edge leaves the statement early. 8 rounds of
both outcomes; the buffer's own length is returned afterwards to prove the record survived all 16.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

enum Boom implements Error
	bad
end 'Boom'

function eat(xs ByteArray, fail bool) returns Integer throws Boom
	if fail 'boom'
		throw Boom.bad
	end 'boom'
	return xs.count()
end 'eat'

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	var total = 0
	var i = 0
	while i < 8 'spin'
		total = total + (try eat(ByteArray.init(mm), fail: true) otherwise 1)
		total = total + (try eat(ByteArray.init(mm), fail: false) otherwise 9)
		i = i + 1
	end 'spin'
	return (total * 10 + mm.length()) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
242
```

<!-- test: error.a-non-buffer-argument -->
An argument that is not a buffer at all is refused at the call, naming the type the SIGNATURE
declares (`__ManagedMemory with <element>`) rather than the `Array` spelling of the same instance.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let a = ByteArray.init(5)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:20: argument type mismatch for 'managed': expected '__ManagedMemory with Byte', got 'int'
```

<!-- test: error.a-buffer-of-the-wrong-element -->
⭐ **THE SILENT WRONG ANSWER THIS DOOR EXISTS TO STOP.** An `Array with Integer` over a BYTE
buffer would stride by 8 through a byte-packed allocation and read whichever bytes fell on the
boundaries. The instances differ, so the call is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let mm = try __ManagedMemory.create(16, elementSize: 1) otherwise return 1
	let a = IntArray.init(mm)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:19: argument type mismatch for 'managed': expected '__ManagedMemory with Integer', got 'ByteArray'
```

<!-- test: error.a-word-buffer-has-no-nameable-array -->
⚠ **THE ROW THIS DOOR OBSERVES RATHER THAN CAUSES.** `__ManagedMemory.create(n, elementSize: 8)`
mints `Array with int` — and `Array with int` is a type no program can spell (E2061 refuses a bare
`int` as a type argument), while every ranged alias over it interns a different instance. So there
is no nameable receiver a WORD buffer can be adopted into, and the diagnostic below names an
instance the author cannot write. It is pinned so the day `Array_int` becomes nameable is a RED
case rather than a silent widening.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let mm = try __ManagedMemory.create(3, elementSize: 8) otherwise return 1
	let a = IntArray.init(mm)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:19: argument type mismatch for 'managed': expected '__ManagedMemory with Integer', got 'Array_int'
```

<!-- test: error.an-unknown-array-static -->
The unknown-static refusal names what the type ACTUALLY provides. It said "`create()` only" until
this rung, which stopped being true the moment `init` landed — a refusal's noun is the authority a
reader trusts, so a stale one sends them looking for a mechanism that exists.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let a = ByteArray.nosuch()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:20: Unsupported: `Array` static method 'nosuch' — shv2 provides two: `create()` and `init(managed)`
```

<!-- test: a-synthesized-byte-buffer-adopts -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐⭐ **THE BYTE-BUFFER BOUNDARY AT THIS DOOR (W5).** A COMPILER-SYNTHESIZED buffer wears the
reserved element `__ManagedByte`, deliberately a DIFFERENT instance from the user-visible `Byte` —
a user may declare `Byte`, and a compiler-minted buffer's stride may not follow. Raw instance
equality therefore refused it, and this is the NINTH door of the class
`ProgramSignatures.byteBufferBoundaryAdmits`'s header enumerates — and the only one that does not
ride `aggregatesConflict`, which is why the other eight did not cover it: `stdlib/Console.maxon:68` is
`ByteArray.init(__Builtins.readStdin(n))`, and it got `E3005 … expected '__ManagedMemory with
Byte', got 'Array___ManagedByte'` on a module the bootstrap compiles.

The buffer here is `__ManagedDirectory.currentPath()` rather than a stdin read, deliberately: the
subject is the DOOR and not the intrinsic, and this producer is the same four-line shape that
header uses for the other six. It is restricted to x64-windows by the marker line above, for that
producer's substrate, which is the only reason this case is not target-neutral like the rest of the
file. (Quoting the marker's TEXT in prose would be a second marker: `SpecParser` reads the directive
with `line.contains`, so a sentence inside a test's region sets it too — see
`bytearray-element-size.md`'s exit-code case, where for one revision the sentence was the only one.)
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	let a = ByteArray.init(cwd)
	if a.count() > 0 'adopted'
		return 7
	end 'adopted'
	return 2
end 'main'
```
```exitcode
7
```

<!-- test: error.a-synthesized-buffer-is-refused-at-a-narrowed-byte -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⛔ **AND THE ADMISSION IS THREE QUESTIONS, NOT ONE — THIS IS THE ONE THAT STOPS THE MEASURED 223.**
The element is still NAMED `Byte` and the record still strides one byte, so a door that asked only
those two would adopt a buffer of raw OS bytes as an array of `int(0 to 200)` and hand back
whichever bytes fell outside it, with no diagnostic anywhere. The third question — does the
declared element hold EVERY byte value, in every file that declares the name — is what refuses it.
```maxon
typealias Byte = int(0 to 200)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	let a = ByteArray.init(cwd)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:20: argument type mismatch for 'managed': expected '__ManagedMemory with Byte$0_200', got 'Array___ManagedByte'
```
