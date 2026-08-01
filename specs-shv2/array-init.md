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
  `__mm_retain` raises the same refcount word `__arr_decref` lowers (both go through
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

### ⚠ Every adoption reachable TODAY is a BYTE one, and the reason is not this door

MEASURED, and it is worth writing down because the door looks byte-agnostic and is:

| receiver alias | buffer | verdict |
|---|---|---|
| `Array with Byte` | `__ManagedMemory.create(n, elementSize: 1)` | adopts |
| `Array with Integer` | `__ManagedMemory.create(n, elementSize: 8)` | refused — `Array_int` |
| `Array with Small` (`int(0 to 200)`) | `__ManagedMemory.create(n, elementSize: 1)` | refused — `Array_Byte` |

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
error E3005: <fragment>:7:19: argument type mismatch for 'managed': expected '__ManagedMemory with Integer', got 'Array_Byte'
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
