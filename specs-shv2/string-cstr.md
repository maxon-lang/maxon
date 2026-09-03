---
feature: string-cstr
status: stable
keywords: [String, cstr, cstring, toCString, terminator, NUL, capacity, view, rdata, ownership, leak]
category: strings
---

# `String.cstr()` — a NUL-terminated pointer to the receiver's OWN bytes

## Documentation

`stdlib/String.maxon`'s `cstr()` is the language's one producer of a `cstring`: a raw byte pointer with
no length, no capacity and no refcount, whose purpose is to hand a `__Builtins.*` intrinsic something to
`strlen`. It forwards to `managed.toCString()`, which the compiler lowers to `__mm_to_cstring`
(`StringRuntime.buildMmToCString`).

The answer is **always the receiver's own buffer**, and there are exactly two ways to get there:

| The receiver | What happens | Cost |
|---|---|---|
| its `buffer[length]` is a byte it OWNS, and that byte is already `\0` | the buffer is handed straight back | **nothing** |
| anything else | the record is grown by one byte and terminated IN PLACE | **one reallocation, once** |

⇒ The pointer is owned by the RECEIVER. Nothing is allocated for the caller to free, and — this is the
half that used to be false — nothing is left unowned either. The caller's whole obligation is to keep the
receiver **alive** and **unmutated** for as long as it holds the pointer: a later `append` may reallocate
the buffer out from under it.

### ⛔⛔ THE BYTE AT `buffer[length]` MAY NOT BE READ BY EVERY RECORD, AND THAT IS A SEPARATE FACT FROM WHICH ONES ANSWER `\0`

`buffer[length]` sits one PAST the content. Four shapes can vouch for it and are allowed the probe: an
owned buffer with a SPARE SLOT (`capacity > length`), a byte-string literal's `.rdata` blob, a fused
inline String's payload, and a String LITERAL's immortal record. Two cannot, and neither is a corner case:

- a **VIEW**, whose bytes are a window into someone else's buffer;
- an **EXACTLY-FULL owned buffer** (`capacity == length`), which is the ORDINARY state — a first `append`
  onto `""` leaves exactly that, because `__managed_grown_cap` takes an ask that outruns doubling at face
  value.

For those two the byte belongs to whatever the allocator hands out next, and **a zero found there is one
nothing keeps zero**. `BufferOwnership.emitRouteOnVouchedTerminatorSlot` is the admit-list, and the C#
bootstrap carries the same rule in its own IR (`MaxonToStandardConversion.LowerManagedToCString`), where
its absence was MEASURED as `Directory.exists` answering **false** for a directory plainly present.

### ⛔ THE COPY PATH IS GONE, AND IT WAS AN EXIT-101 LEAK ON FIVE LINES OF ORDINARY MAXON

An unvouched receiver used to be blitted into a fresh `__mm_alloc` block — a block a `cstring` has no
owner to record against and no drop to reclaim it with. `var s = ""` + `s.append("hello world")` +
`s.cstr()` therefore left the process leaking and exiting **101**. The cure is the shape both reference
compilers already use: COW, grow to `length + 1`, terminate in place, hand back the record's own buffer
(the bootstrap through `maxon_string_ensure_cap`, v1 through `__managed_mem_grow`, shv2 through
`__managed_reserve`).

### ⚠ WHAT THESE CASES PIN, AND WHAT THEY DO NOT

Both halves were SABOTAGE-MEASURED on this tree, and they do not catch the same thing:

- **The leak half is pinned squarely.** With the copy path restored (guard intact),
  `terminates-a-packed-receiver-without-leaking` exits **101** instead of 0, at length 8 and at 11 alike.
- **The out-of-bounds READ half is caught only by an accident of the allocator, and this spec says so
  rather than claiming a pin it does not have.** With the admit-list removed so every record probes,
  `first=1` in `packed-receiver-pays-once-and-only-once` becomes `first=0`: the probe read the byte it
  does not own, found a zero there, and handed back a pointer that is not terminated by anything. That the
  case goes red today is real but incidental — the discriminating byte is precisely the byte NOTHING KEEPS
  ZERO, so a different allocator state would let the sabotage pass. ⛔ **A case here can never pin the
  read directly, because no user-reachable consumer of a `cstring` exists in this language** — the
  bootstrap's half has one (`Directory.exists`, pinned by `specs/directory.md`) and shv2's does not.

## Tests

<!-- test: terminates-a-packed-receiver-without-leaking -->
⭐⭐ **THE FIVE LINES THAT EXITED 101.** A first `append` onto `""` leaves `capacity == length`, which is
exactly the shape whose terminator slot the record cannot vouch for — so this receiver takes the second
path on every run, deterministically, and is not relying on any allocator coincidence to do so. The
receiver is still readable afterwards, and the printed text comes back THROUGH the pointer — which is what
says the in-place grow moved the bytes rather than losing them, and that the terminator landed where the
receiver's bytes end.
```maxon
function main() returns ExitCode
	var s = ""
	s.append("hello world")
	print("{String.fromCString(s.cstr())} len={s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world len=11
```

<!-- test: terminates-an-exactly-full-receiver-without-leaking -->
The same shape at the length MEASURED landing exactly full in its slab slot: `HeaderBytes(24) + 8` is 32, a
whole size class, so `buffer[8]` is past the end of the record's allocation entirely and belongs to the
allocator. Its own case beside the eleven-byte one because the two differ in WHOSE byte the old, unguarded
probe would have read — at length 11 the ask rounds up to a 48-byte slot, leaving 13 bytes of the record's
own private zeroed slack, so that read was harmless; at length 8 there is no slack and the read is out of
bounds. The answer this spec requires is the same for both, and that is the point: the guard cannot see the
allocator's rounding, so it refuses the whole shape rather than the half that is actually unsafe.
```maxon
function main() returns ExitCode
	var s = ""
	s.append("xxxxxxxx")
	print("{String.fromCString(s.cstr())} len={s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
xxxxxxxx len=8
```

<!-- test: packed-receiver-pays-once-and-only-once -->
⭐ **THE COST, PINNED AS A NUMBER — and the case that says the cure did not make every receiver pay.** A
String LITERAL's blob is NUL-terminated in `.rdata` by construction, so it vouches, probes, and costs
**nothing**. A packed receiver pays **one** reallocation. And asking the SAME receiver again costs
nothing, because the first ask left it with `capacity > length` and a real `\0` in the slot it now owns —
which is what distinguishes a grow from a copy: the copy path paid on every call forever.
```maxon
function main() returns ExitCode
	let lit = "hello world"
	let beforeLit = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	var p = lit.cstr()
	let afterLit = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()

	var s = ""
	s.append("xxxxxxxx")
	let beforeFirst = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	p = s.cstr()
	let afterFirst = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()

	let beforeSecond = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()
	p = s.cstr()
	let afterSecond = __Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()

	print("literal={afterLit - beforeLit} first={afterFirst - beforeFirst} second={afterSecond - beforeSecond}\n")
	// The round trip is the only user-reachable consumer of a `cstring`, and it is kept OUT of every
	// measured window because it allocates. It is what says the pointer is terminated where the
	// receiver's bytes end rather than merely handed back.
	if String.fromCString(p).byteLength() != 8 'roundTripIsNotTheReceiversBytes'
		return 1
	end 'roundTripIsNotTheReceiversBytes'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
literal=0 first=1 second=0
```
