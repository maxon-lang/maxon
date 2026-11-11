# Memory Management

This document describes how Maxon's memory manager allocates, tracks, and frees heap memory. Maxon uses **pure reference counting** -- no garbage collector, no scope frames, no tracing. Every heap object has an inline header with a refcount and a destructor function pointer. When the refcount reaches zero, the destructor is called and the object is freed immediately.

## Overview

```
User code
  var p = Point.create(1, y: 2)   // mm_alloc -> rc=0, mm_incref -> rc=1
  let q = p                       // mm_incref -> rc=2
  p = Point.create(3, y: 4)       // decref old (rc->1), alloc new (rc=0), incref (rc=1)
end of scope                   // decref q -> rc=0
                               // -> destructor -> free
```

The memory manager provides three core operations:

| Operation | What it does |
|-----------|-------------|
| `mm_alloc(size, destructor, tag)` | Allocate a tracked heap object with rc=0 |
| `mm_incref(ptr)` | Increment reference count |
| `mm_decref(ptr)` | Decrement reference count; if rc reaches 0, call destructor then free |

## Header Layout

Every managed heap object has a **16-byte inline header** immediately before the user-visible pointer. The header stores the destructor function pointer and the reference count. A box's total size (prefix plus payload) is rounded up to a multiple of 16, so every heap object's user pointer is 16-byte aligned.

```
                       +--- user_ptr (what Maxon code sees)
                       |
     +-----------+---------+-------------------------+
     | destructor| refcount|  user data              |
     +-----------+---------+-------------------------+
     [ptr-16]     [ptr-8]   [ptr]
```

| Offset | Field | Type | Description |
|--------|-------|------|-------------|
| `ptr - 16` | `destructor` | `fn_ptr` | Generated destructor function (or NULL for no managed fields) |
| `ptr - 8` | `refcount` | `u64` | Reference count |
| `ptr` | user data | varies | The object's fields |

A build passed `--debugstream` prepends a 16-byte prefix *below* the header; its first word, at `ptr - 32`,
holds `(alloc_id << 16) | tag_index` — the allocation's id and the interned index of its type's name, which is
what a trace event carries — and the second word pads the prefix to 16 so the user pointer stays aligned. The
prefix is present only under that flag, so a normal build's box is 16 bytes of header exactly and a program
that is not being traced pays nothing for the instrument.

## API

### `mm_alloc(size, destructor, tag)` -> user_ptr

Allocates a new heap object:

1. Asks the slab allocator for `size + 16` bytes (16 for the inline header), or `size + 32` under `--debugstream`, rounded up to a multiple of 16.
2. Initializes the header: `destructor = destructor`, `refcount = 0`. Under `--debugstream` it also writes the packed id below the header; `tag` is read nowhere else.
3. Increments the global `__mm_alloc_count`.
4. Returns the pointer to user data (past the header).

The refcount starts at **0**. The caller's assignment generates an `mm_incref` to set it to 1.

### `mm_incref(ptr)`

Increments the refcount by 1:

```
refcount = [ptr - 8]
[ptr - 8] = refcount + 1
```

Panics if `ptr` is NULL.

### `mm_decref(ptr)`

Decrements the refcount by 1. If refcount reaches zero, invokes the destructor and frees:

```
refcount = [ptr - 8]
refcount -= 1
[ptr - 8] = refcount
if refcount == 0:
    destructor = [ptr - 16]
    if destructor != NULL:
        destructor(ptr)        // decrefs managed fields
    HeapFree(ptr - 16)         // free the entire block
    __mm_alloc_count -= 1
```

Panics on NULL or refcount underflow (already zero).

### Growth

An allocation keeps its size for its whole life. A buffer that outgrows its capacity is replaced: the runtime
allocates a larger one, copies the live elements, and releases the old buffer with `mm_decref`.

## Destructors

The compiler generates a destructor function for each concrete type that has managed fields. Each destructor decrefs the type's managed fields, triggering a recursive cleanup cascade.

### Struct Destructor

For structs with managed fields (fields that are heap-allocated structs, strings, arrays, etc.):

```
destructor_Point(ptr):
    // For each managed field at known offset:
    field_ptr = load [ptr + offset]
    if field_ptr != NULL:
        mm_decref(field_ptr)     // may trigger field's own destructor
```

### Enum Destructor (Associated Values)

For enums with associated values that contain managed types:

```
destructor_Result(ptr):
    tag = load [ptr + 0]
    switch tag:
        case 0: // success
            field_ptr = load [ptr + 8]
            if field_ptr != NULL: mm_decref(field_ptr)
        case 1: // error
            field_ptr = load [ptr + 8]
            if field_ptr != NULL: mm_decref(field_ptr)
```

A union stored as one tagged word (see [Unboxed Unions](#unboxed-unions)) takes the word as its argument and switches on the key. A case with no payload, and the empty word, match no case and release nothing:

```
destructor_Result(word):
    field_ptr = word & -8
    switch word & 7:
        case 1: // success
            if field_ptr != NULL: mm_decref(field_ptr)
        case 2: // error
            if field_ptr != NULL: mm_decref(field_ptr)
```

### ManagedList Destructor

For managed lists (`__ManagedList with T`) holding managed elements:

```
destructor_ManagedList(ptr):
    node = load head
    while node != NULL:
        next = load [node + next_offset]
        value = load [node + value_offset]
        if value != NULL: mm_decref(value)
        HeapFree(node)
        node = next
```

### `mm_decref_managed_elements`

For arrays (`Array with T`) holding managed struct elements, this function iterates the `__ManagedMemory` buffer and decrefs each element pointer. Called by the array's destructor before the buffer is freed.

### Types With No Managed Fields

Types that contain only primitives (e.g., `Point{x: int, y: int}`) have a NULL destructor. When `mm_decref` drops their refcount to zero, the object is freed directly without calling any destructor.

## Unboxed Unions

A `union` is stored as one 8-byte word, with no box of its own, when all of these hold:

- it has at most seven cases that carry a payload and at most 32767 cases in all;
- it declares no `implements` clause;
- every case carries no payload, or exactly one managed record: a `String`, a `Character`, a struct, or an array, map or other generic-instance record;
- it is declared in source (the compiler's own request and reply unions are boxes).

Every other union with a payload is a heap box.

The word holds the payload pointer with the case's key in its low three bits. Payload records are 8-aligned, so those bits are free:

```
word    = payload_ptr | key
key     = word & 7           // 1 to 7
payload = word & -8
```

A union of at most seven cases gives each case the key `declaration index + 1`. A larger union gives its payload cases the keys 1 to 7 in declaration order, then its payload-free cases the keys after them in declaration order.

- **A payload-free case** is a word with no pointer bits and allocates nothing, so there is nothing to free. Its word is its key when the key is at most 7, and `key << 48` otherwise. Every real pointer is below 2^48 on every target, and a key of 8 or more shifted by 48 is at least 2^51, so no payload word equals a payload-free case's word.
- **Key 0 is the empty word.** A zeroed slot (a fresh array element, an unwritten field) is a union that holds nothing: the destructor, retain, clone and share walk read it as vacant and touch nothing. Keys run from 1, so the three bits hold seven payload cases, and the largest key is 32767, which keeps the shifted word positive.
- **Construction** allocates only the payload; the word owns the reference the payload arrived with.
- **Drop and retain** go through `__destruct_<U>(word)` and `__uretain_<U>(word)`, synthesized per union. Both switch on the tag, mask the pointer out and skip a null payload.
- **`match`** dispatches on the key: `word & 7` for a union of at most seven cases, and `(word & 7) | (word >> 48)` (logical shift) for a larger one. An arm that binds a payload covers exactly one case, so it takes the payload as `word - key` with that case's key. `.ordinal` answers the declaration index of the case with that key. A payload binding is a hold when the arm cannot write it or displace its source (the compiler lowers the hold to a reference only where an operation in its window may release the payload), and a retained reference when it can.

## Buffers (`__ManagedMemory`)

`__ManagedMemory` is the internal backing store used by `String`, `Array`, and other buffer-based types. It is heap-allocated via `mm_alloc` with its own refcount. The parent struct's destructor calls `mm_decref` on it.

```
__ManagedMemory layout (48 bytes):
+-----------------------------------------------------+
| +0   buffer          (ptr) -> heap data             |
| +8   length          (i64) -> element count         |
| +16  capacity        (i64) -> ownership mode        |
| +24  element_size    (i64) -> stamp; stride + flag  |
| +32  parent          (ptr) -> owed allocation       |
| +40  element_destroy (ptr) -> per-element destructor |
+-----------------------------------------------------+
```

The `element_size` field at +24 is a 64-bit stamp. Its low 32 bits are the
per-element stride, normally a byte stride (1, 2, 4, or 8). A **negative** stride marks
a *sub-byte-packed* element: the magnitude is the element's bit width. `Array with bool`
stores `element_size = -1` (1 bit per element, 8 per byte); a non-negative ranged-int
typealias packs the same way — `int(0 to 3)` is `-2` (4 per byte) and a nibble
`int(0 to 15)` is `-4` (2 per byte). Buffer sizing, get/set, and the shift/copy paths
all route through shared packed-aware helpers
(`__managed_mem_is_packed` / `__managed_mem_buffer_byte_len` /
`__managed_mem_read_packed` / `__managed_mem_write_packed`), so one code path serves
every width — the widths are restricted to those dividing 8 (1/2/4) so a field never
straddles a byte boundary. `element_size == 0` is reserved for an unset / non-container
record. See [bool-bit-packing](../specs/bool-bit-packing.md) and
[ranged-int-bit-packing](../specs/ranged-int-bit-packing.md).

Bit 32 of the stamp is set for an element stored in a signed 1-, 2- or 4-byte slot (an
enum with a negative raw value). Code compiled once for every element type, such as the
body of a generic type, learns that the slot is signed from this bit, so it sign-extends
what it reads from a record whose stamp carries it. `arr.managed.elementSize()` returns the
stride alone. An array created inside a shared generic body is stamped with the stride
of the concrete element its instantiation binds, which the body reads from the
instantiation's layout descriptor, so it is the same size as the same array created in
a concrete body. An element access compares the whole stamp against each stride it
serves — a machine word, 1, 2 and 4 bytes, 1, 2 and 4 packed bits, and the three signed
slots — and a stamp none of them names ends the program with
`fatal error: an array record carries an element size no element access serves`.

The `element_destroy` field at +40 holds a per-element teardown function pointer
(or `0` for raw/unmanaged elements). When a buffer is a destructor ROOT, the
`__destruct___ManagedMemory` walk reads this slot and calls it on each element
before freeing the buffer. The record is a full 48 bytes precisely so this slot
is in-bounds: a short 40-byte allocation would leave the walk reading adjacent
heap garbage and calling it as a function pointer. Every allocation site zeroes
the slot when the elements are raw, so the walk's `== 0` gate no-ops.

The `capacity` field says whether the record may write its buffer and release it:
- **capacity >= 0** (owned): the record owns the buffer and writes it in place up to `capacity` elements. The buffer is a separate allocation, which the record releases when it dies or detaches, or it sits inside the record's own allocation ([co-allocated](#co-allocated-buffers)). When the length reaches the capacity, the runtime allocates a larger buffer (the capacity doubles up to 256 elements, then eases toward 1.25x), copies the live elements into it, and releases the old buffer with `mm_decref`.
- **capacity == -1** (view): the buffer is an interior pointer into another record's allocation, and `parent` names that allocation, on which the view holds a count.
- **capacity == -2** (read-only data): the buffer is a static blob, such as a literal's bytes, and the record is an ordinary heap box.
- **capacity == -3** (inline String): the bytes sit in the String record's own allocation, right after the record.
- **capacity == -4** (immortal record): the record itself is read-only image data, left alone by every count and free.

A negative capacity means the record copies its elements into a private buffer before any in-place write; the specific value only records where the buffer came from. `capacity()` returns it to user code.

The `parent` field is the address of the separate allocation the record owes a release to, or `0` when it owes none. A view stores the base of the allocation its bytes are in, and a view of a view stores that same base, so every view names the original allocation. An owner whose buffer is a separate allocation stores `0`: its debt is its own `buffer`, which the runtime reads where it would otherwise store the address twice. A [co-allocated](#co-allocated-buffers) owner stores `1`, which marks its buffer as part of its own allocation.

### Co-allocated buffers

An array created with a known capacity holds its elements in the same allocation as its record, so creating it
is one allocation and dropping it is one free. A `FixedArray` made by `create()` or `create(n)` is co-allocated
at every size, because its length is fixed. The deep copy of an array of managed elements and a
`__ManagedMemory` created with a capacity are co-allocated up to 256 bytes of elements, the bound for arrays
that can grow; see [co-allocated-arrays](../specs/co-allocated-arrays.md).

```
[48-byte record][8 bytes unused][8-byte refcount word][element bytes ...]
                                                     ^ buffer = record + 64
```

The refcount word is a box refcount slot, so the checks that ask whether a buffer is shared or viewed read it as
they read any buffer's. The buffer starts at a 16-byte boundary, as a separate allocation's payload does. The
record marks its own bytes with `parent = 1`; growing
past the capacity or a copy-on-write detach clears the mark when the record takes a buffer of its own.

- Dropping the record frees the elements with it; a separate buffer is released with `mm_decref`.
- Growing past the capacity allocates a buffer of its own, copies the elements, and leaves the inline bytes
  unused until the record dies. The 256-byte limit bounds that unused space.
- A `FixedArray` built from a literal (`from [a, b, c]`), and the result of `clone`, `slice` or a copy-on-write
  detach of one, follow the 256-byte limit.
- `clone` and `slice` of a co-allocated array copy the window into a new array, because the bytes end with their
  record and a view can outlive it. The copy owns its buffer, so `capacity()` reports the larger of its length and
  4 (0 for an empty copy) where a view reports -1.
- A record frozen for sharing with other threads has its refcount word marked like any other buffer's, so it
  detaches to a buffer of its own before any write. A large `FixedArray` detached this way leaves its inline
  bytes unused until the record dies.

## What Gets Heap-Allocated

| Type | Heap-allocated? | Notes |
|------|----------------|-------|
| Primitives (`int`, `bool`, `float`, `byte`) | No | Stored inline in stack slots or struct fields |
| Structs | Yes | Every struct instance lives on the heap |
| Strings | Yes | Struct (pointer to `__ManagedMemory`) with refcount |
| Chars | Yes | Heap-allocated with refcounting, like a small string |
| Arrays (`Array with T`) | Yes | A 48-byte `__ManagedMemory` record with refcount; its element buffer is a separate allocation, or part of the record's own ([co-allocated](#co-allocated-buffers)) |
| ManagedLists (`__ManagedList with T`) | Yes | Doubly-linked list, each node is a separate allocation |
| Tuples | Yes | Structs with `_0`, `_1`, etc. fields |
| Enums (with associated values) | Yes | Tagged enum with payload; a union of single-record cases is a [tagged word](#unboxed-unions) whose only allocation is its payload |
| Enums (no associated values) | No | Stored as integers |
| Closures | Yes | Environment block is heap-allocated |

## Ownership Rules

### Construction

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord
	export var y as Coord

	export static function create(x Coord, y Coord) returns Self
		return Self{x: x, y: y}   // mm_alloc -> rc=0, then mm_incref -> rc=1
	end 'create'
end 'Point'
```

The compiler emits `mm_alloc` (rc=0), stores field values, then calls `mm_incref` when the pointer is assigned to the variable (rc=1). A struct literal is legal only inside its own type (E3076), so the examples below construct through `Point.create`.

### Aliasing

```maxon
let p = Point.create(1, y: 2)
let q = p                    // mm_incref -> rc=2
```

Assigning a struct to another variable copies the pointer and increments the refcount. Both variables point to the same heap object.

### Reassignment

```maxon
var p = Point.create(1, y: 2)
p = Point.create(3, y: 4)    // mm_decref old (destructor handles fields), alloc new (rc=1)
```

The old value is decremented via `mm_decref`. If its refcount reaches zero, the destructor is called (which decrefs all managed fields), then the object is freed. The new value is allocated with rc=1.

### Scope Exit

When a variable goes out of scope, its refcount is decremented:

```maxon
function test()
	let p = Point.create(1, y: 2)   // rc=1
	print("{p.x}\n")
end 'test'                         // mm_decref p -> rc=0 -> destructor -> free
```

All managed variables in scope are decremented. The order is: user variables first, then temp variables.

### Return

Returning a struct **passes lifetime responsibility** to the caller. The returned variable is skipped during scope cleanup — it is one of the values the scope-end cleanup is told to keep:

```maxon
function makePoint() returns Point
	let p = Point.create(1, y: 2)   // rc=1
	return p                        // p is NOT decref'd; caller is responsible
end 'makePoint'

function main() returns ExitCode
	let p = makePoint()             // caller now owns it, rc=1
	print("{p.x}\n")
	return 0                        // mm_decref p -> rc=0 -> freed
end 'main'
```

The compiler tracks call-return temps as such. When the caller assigns the return value to a named variable, that origin is what makes the incref unnecessary -- the callee already allocated at rc=1, so the reference passes directly without an extra retain.

A function whose every return hands back the same parameter returns that parameter **borrowed**. The callee emits no retain, and each direct call site retains the result, so the caller owns what it receives exactly as it does for any other return. The compiler returns a parameter borrowed when the function releases and consumes nothing of it and every use of the function is a direct call that passes it; a `throws` function and a function reached through a function value keep returning a counted reference. A `var` rebound through such a call (`last = step(last)`) then retains and releases the record it already holds, and the compiler cancels that pair.

### Function Parameters

Function parameters are **not owned** by the callee. The caller retains ownership and is responsible for the parameter's lifetime, so a parameter is skipped during scope-end cleanup:

```maxon
typealias Integer = int(i64.min to i64.max)

function readLevel(c Config) returns Integer
	// c is not owned; no incref on entry, no decref on exit
	return c.level
end 'readLevel'
```

### Container Operations

| Operation | Effect |
|-----------|--------|
| `arr.push(item)` | incref `item`; container holds a reference |
| `arr.get(index)` | incref the element; caller gets a reference |
| `arr.remove(index)` | transfers the container's reference to caller (no extra incref) |
| `arr.set(index, value)` | decref old element, incref new element |
| `arr.clear()` | decref all elements |
| Container freed | destructor decrefs all elements via `mm_decref_managed_elements` |

### Clone

```maxon
let b = a.clone()   // allocates a new, independent copy with rc=1
```

The compiler auto-generates `Cloneable` conformance for structs whose fields are all cloneable. Clone creates a deep copy -- each managed field is also cloned.

A field held at an INTERFACE type is deep-copied too: the copy goes through a clone entry in the witness table the value carries, so the conformer it actually holds is what does the copying. Because that conformer is a run-time fact, the compiler admits the clone only when EVERY conformer of that interface in the program can be deep-cloned; otherwise it refuses the `.clone()` and names the field, the interface and the conformer that cannot be copied.

## Scope Cleanup Mechanism

### Scope-end cleanup

The compiler inserts scope-end cleanup at every scope exit point (block end, break, continue, return, throw). What it carries is:

- the managed variables declared in this scope, which are decref'd
- the variables to skip (the return value, and parameters, which the caller owns; a returned parameter is [returned borrowed](#return))

A value moved in a `match` arm that ends `and fallthrough` is released on the fallen-into arm's own
dispatch edge, where the move never ran.

### Cleanup on All Exit Paths

Scope cleanup runs on every possible exit path:

- **Normal block exit** -- `end 'label'`
- **Break** -- cleans up the loop body scope before jumping to the loop exit
- **Labeled break** -- cleans up all intermediate scopes between the break and the target loop
- **Continue** -- cleans up the loop body scope before jumping back to the loop header
- **Return from nested block** -- cleans up all enclosing scopes up to the function level
- **Error propagation** -- `try` that throws cleans up the function scope before propagating

## Cycle Prevention

Reference cycles are a **compile-time error** (`E4014`). The compiler statically rejects any type that references itself directly or indirectly:

```maxon
// ERROR: type 'Node' contains a reference cycle (via Node -> next: Node)
type Node
	export var next as Node
end 'Node'

// ERROR: mutual recursion A -> B -> A
type A
	export var b as B
end 'A'
type B
	export var a as A
end 'B'

// ERROR: indirect via container
typealias FolderArray = Array with Folder
type Folder
	export var children as FolderArray
end 'Folder'
```

This eliminates the need for cycle-breaking mechanisms like weak references or tracing collectors.

## Debug Modes

### The memory trace

Every memory operation can be traced, but not by a flag that prints to stderr. A build passed
`--debugstream` writes its events into a shared-memory ring, and `maxon monitor --filter=mm <exe>` runs the
program and decodes them:

```text
mm_alloc String #1 size=16
mm_incref String #1 rc=1
mm_decref String #1 rc=0
mm_free String #1
```

The type name comes from the interned-name table the compiler embedded in the executable, reached through
the packed id below each box's header; the `#id` correlates the events of one allocation. That is also what
a spec's ` ```mm-trace ` block asserts — see `docs/SPECS.md`. The ring is one shared buffer and a memory
event carries no thread id, so with more than one green thread producing events the decoded order is the
order they took the lock.

### Detecting a use-after-free

`maxon build --poison-freed` (also accepted by `maxon run`) overwrites a box freed into a slab span, from
the payload start to the end of its slot, with the poison byte `0x3F`: read back as a small integer it is a
conspicuous 63, where a freshly zeroed slot would hold 0, and a word of it is the non-canonical address
`0x3F3F3F3F3F3F3F3F`, so dereferencing a poisoned pointer faults. A use-after-free becomes loud at the
read. A box above 32 KiB goes back to the operating system whole and is not filled. The spec suite builds
every run case with the option, except a case that pins Target IR.

### Leak Check

At program exit the runtime checks the allocator's live count of tracked (header-carrying) allocations. The
test is `!= 0` rather than `> 0`: an over-release drives the count negative, and a gate that caught only
under-release would call a clean run for a compiler emitting too many drops — the more dangerous of the two.
A non-zero count makes the program's exit code **101**; nothing is printed. A program using green threads is
gated a second, independent time on its live green-thread count, which exits **75**.

The exit code says that something leaked, not what. **To attribute it, census the live heap by tag** —
`__Builtins.slabCensusTally` and its two bucket readers, described under
[Memory Management](LANGUAGE_REFERENCE.md#attributing-the-live-heap) — which walks every live slot and
buckets it by the tag in its packed id. The compiler runs exactly this on itself through
[`maxon build --census-by-tag`](CLI_REFERENCE.md#logging).

A slot that carries no box header — a string's bytes, an element buffer, a green thread's stack — has no tag
to be attributed by, and lands in the census's `(unattributable)` bucket.
`__Builtins.mmRawAllocLive()` is how many such slots are live, which bounds how much of a table can be wrong
for that reason.

### Counting allocations by tag

A census is blind to an allocation that was freed before it looked, which is most of them: it answers what
the heap holds, not what the program asked for. `__Builtins.mmAllocTotalByTag(i)` and
`__Builtins.mmAllocBytesByTag(i)` answer the second question, described under
[Attributing Allocation Churn](LANGUAGE_REFERENCE.md#attributing-allocation-churn) — the allocations and
bytes tag `i` has asked for since the process started, which only rise. The compiler runs this on itself
through [`maxon build --allocations-by-tag`](CLI_REFERENCE.md#logging).

Their table is a 32 KiB run in `.data` that a `--debugstream` binary always carries, stepped by every
allocation such a build makes. A binary built without the flag has neither the table nor the step, and
both readers answer 0.

## Copy-on-Write (COW)

Strings and arrays use copy-on-write semantics for efficient sharing:

- A `slice` or `clone` of a separately allocated buffer is a view: it holds a count on the allocation its bytes are in, so that allocation's refcount is non-zero while the view lives.
- A mutator (`append`, `set`, `push`, and the rest) first detaches the record with `__managed_cow_detach` when `capacity` is negative (a view or read-only data) or when the buffer's refcount is non-zero (another record views it).
- A detach allocates a buffer of exactly `length` elements, copies the live elements into it, and then releases the count on the allocation it left; the record becomes an owner with `parent` at `0`. A view and the owner it views detach independently, so either side's write leaves the other's elements as they were.
- A co-allocated buffer is copied by `clone` and `slice` ([Co-allocated buffers](#co-allocated-buffers)), so every view is over a separate allocation.
- String literals and array slices share memory this way until a write occurs.

## Managed Variable Initialization

All managed (heap-allocated) variables are **zero-initialized** at function entry. This includes both named variables and compiler-generated temps. Zero-initialization ensures that scope cleanup can safely call `mm_decref` (with null guard) on variables that may not have been assigned yet (e.g., in conditional branches where only one path allocates).

## Summary of Invariants

1. Every heap object has a 16-byte inline header at `[ptr-16]` containing destructor and refcount.
2. Refcount starts at 0 after `mm_alloc`. The caller's assignment emits `mm_incref` to set it to 1.
3. Refcount reaches 0 -> destructor called (decrefs managed fields) -> object freed.
4. Reference cycles are impossible -- they are compile-time errors.
5. Every scope exit path decrefs all in-scope managed variables (except returned/kept values and parameters).
6. The global `__mm_alloc_count` tracks live allocations and is checked at exit for leaks.
7. A function that returns one of its parameters returns it borrowed; every call site retains the result, so the caller always owns what a call returns.
