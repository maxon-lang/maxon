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

The memory manager provides four core operations:

| Operation | What it does |
|-----------|-------------|
| `mm_alloc(size, destructor, tag)` | Allocate a tracked heap object with rc=0 |
| `mm_incref(ptr)` | Increment reference count |
| `mm_decref(ptr)` | Decrement reference count; if rc reaches 0, call destructor then free |
| `mm_realloc(ptr, size)` | Reallocate a raw buffer (for `__ManagedMemory` growth) |

## Header Layout

Every managed heap object has a **24-byte inline header** immediately before the user-visible pointer. The header stores the destructor function pointer, the payload size, and the reference count.

```
                       +--- user_ptr (what Maxon code sees)
                       |
     +-----------+---------+---------+-------------------------+
     | destructor|  size   | refcount|  user data (size bytes) |
     +-----------+---------+---------+-------------------------+
     [ptr-24]     [ptr-16]  [ptr-8]   [ptr]
```

| Offset | Field | Type | Description |
|--------|-------|------|-------------|
| `ptr - 24` | `destructor` | `fn_ptr` | Generated destructor function (or NULL for no managed fields) |
| `ptr - 16` | `size` | `u64` | The payload's byte count |
| `ptr - 8` | `refcount` | `u64` | Reference count |
| `ptr` | user data | varies | The object's fields |

**There is no type tag in the header.** A build passed `--debugstream` prepends one more word *below* the
header, at `ptr - 32`, holding `(alloc_id << 16) | tag_index` — the allocation's id and the interned index
of its type's name, which is what a trace event carries. It is present only under that flag: eight bytes on
every allocation in the language is not a price a debug instrument may charge a program that is not being
traced, so a normal build's box is 24 bytes of header exactly.

## API

### `mm_alloc(size, destructor, tag)` -> user_ptr

Allocates a new heap object:

1. Asks the slab allocator for `size + 24` bytes (24 for the inline header), or `size + 32` under `--debugstream`.
2. Initializes the header: `destructor = destructor`, `size = size`, `refcount = 0`. Under `--debugstream` it also writes the packed id below the header; `tag` is read nowhere else.
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
    HeapFree(ptr - 24)         // free the entire block
    __mm_alloc_count -= 1
```

Panics on NULL or refcount underflow (already zero).

### `mm_realloc(ptr, new_size)` -> new_ptr

Reallocates an existing allocation (used for raw buffer growth in `__ManagedMemory`):

1. If `ptr` is NULL, allocates a fresh buffer via `HeapAlloc`.
2. Otherwise, calls `HeapReAlloc` on the underlying block.
3. Returns the new pointer.

Note: `mm_realloc` is for raw buffers only (no inline header). Managed objects are never reallocated -- they are freed and a new one is allocated.

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

## Buffers (`__ManagedMemory`)

`__ManagedMemory` is the internal backing store used by `String`, `Array`, and other buffer-based types. It is heap-allocated via `mm_alloc` with its own refcount. The parent struct's destructor calls `mm_decref` on it.

```
__ManagedMemory layout (48 bytes):
+-----------------------------------------------------+
| +0   buffer          (ptr) -> heap data             |
| +8   length          (i64) -> element count         |
| +16  capacity        (i64) -> ownership mode        |
| +24  element_size    (i64) -> stride; <0=bits/elem  |
| +32  parent_ptr      (ptr) -> parent struct         |
| +40  element_destroy (ptr) -> per-element destructor |
+-----------------------------------------------------+
```

The `element_size` field at +24 is normally the per-element byte stride (1, 2, 4,
or 8). A **negative** value marks a *sub-byte-packed* element: the magnitude is the
element's bit width. `Array with bool` stores `element_size = -1` (1 bit per element,
8 per byte); a non-negative ranged-int typealias packs the same way — `int(0 to 3)`
is `-2` (4 per byte) and a nibble `int(0 to 15)` is `-4` (2 per byte). Buffer sizing,
get/set, and the shift/copy paths all route through shared packed-aware helpers
(`__managed_mem_is_packed` / `__managed_mem_buffer_byte_len` /
`__managed_mem_read_packed` / `__managed_mem_write_packed`), so one code path serves
every width — the widths are restricted to those dividing 8 (1/2/4) so a field never
straddles a byte boundary. `element_size == 0` is reserved for an unset / non-container
record. See [bool-bit-packing](../specs/bool-bit-packing.md) and
[ranged-int-bit-packing](../specs/ranged-int-bit-packing.md).

The `element_destroy` field at +40 holds a per-element teardown function pointer
(or `0` for raw/unmanaged elements). When a buffer is a destructor ROOT, the
`__destruct___ManagedMemory` walk reads this slot and calls it on each element
before freeing the buffer. The record is a full 48 bytes precisely so this slot
is in-bounds: a short 40-byte allocation would leave the walk reading adjacent
heap garbage and calling it as a function pointer. Every allocation site zeroes
the slot when the elements are raw, so the walk's `== 0` gate no-ops.

The `capacity` field encodes the buffer's ownership mode:
- **capacity > 0** (owned): buffer is a writable heap allocation. When length reaches capacity, it is reallocated (typically doubled) via `mm_realloc`.
- **capacity == 0** (rdata): buffer points to read-only static data (e.g., a string literal). Any mutation triggers a copy to a new writable buffer (copy-on-write).
- **capacity == -1** (slice): buffer is a zero-copy view into another `__ManagedMemory`'s data. The `parent_ptr` field holds a pointer to the parent struct (incref'd). Mutations trigger a copy-on-write, after which the slice becomes owned and `parent_ptr` is decref'd and zeroed.

The `parent_ptr` field is `0` for owned and rdata buffers. For slices, it holds the parent struct pointer to keep the parent alive while the slice references its data.

### `parent_ptr` sentinels and small-array inline storage

The runtime (`stdlib/Internals.maxon`) keys teardown on the
`parent_ptr` field (not `capacity`, which always holds the real slot count) via a
small set of sentinels: `-1` root (owns an external buffer), `-2` rdata-backed,
`-3` inline (see below), `0` uninitialized, and any other value a live slice
parent pointer.

As a single-allocation optimization, a small array or string whose element bytes
fit within `MM_INLINE_CAP_BYTES` (64) holds its elements **inline** in the same
slab slot as the 48-byte record, right after the record fields — `buffer@0` points
into the record's own allocation. The slab rounds the record+inline request up to
a size class, so **all** of that class's slack is reclaimed as spare element
capacity (a few pushes past the requested count stay single-allocation at zero
extra cost). Such a record is tagged `parent_ptr == -3`; its destructor still runs
the managed-element walk but skips the buffer decref (the inline bytes are freed
with the record's own slot). The first grow past the inline capacity detaches to a
normal external buffer via copy-on-write, after which the record is an ordinary
root array. This halves the allocation count for small arrays (previously one
allocation for the record plus a second for the element buffer).

## What Gets Heap-Allocated

| Type | Heap-allocated? | Notes |
|------|----------------|-------|
| Primitives (`int`, `bool`, `float`, `byte`) | No | Stored inline in stack slots or struct fields |
| Structs | Yes | Every struct instance lives on the heap |
| Strings | Yes | Struct (pointer to `__ManagedMemory`) with refcount |
| Chars | Yes | Heap-allocated with refcounting, like a small string |
| Arrays (`Array with T`) | Yes | Struct (count + `__ManagedMemory` pointer) with refcount |
| ManagedLists (`__ManagedList with T`) | Yes | Doubly-linked list, each node is a separate allocation |
| Tuples | Yes | Structs with `_0`, `_1`, etc. fields |
| Enums (with associated values) | Yes | Tagged enum with payload |
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
var b = a.clone()   // allocates a new, independent copy with rc=1
```

The compiler auto-generates `Cloneable` conformance for structs whose fields are all cloneable. Clone creates a deep copy -- each managed field is also cloned.

## Scope Cleanup Mechanism

### Scope-end cleanup

The compiler inserts scope-end cleanup at every scope exit point (block end, break, continue, return, throw). What it carries is:

- the managed variables declared in this scope, which are decref'd
- the variables to skip (the return value, and parameters, which the caller owns)

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
a spec's ` ```mm-trace ` block asserts — see `docs/SPECS.md`. The ring is one shared buffer with no thread
id, so with more than one green thread producing events the decoded order is the order they took the lock.

### Corruption detection is always on

There is no opt-in debug allocator. `__mm_free` overwrites every freed payload with the poison byte `0x3F`
on every build: read back as a small integer it is a conspicuous 63 rather than the 0 a freshly zeroed slot
would hold, and a word of it is the non-canonical address `0x3F3F3F3F3F3F3F3F`, so dereferencing a poisoned
pointer faults instead of quietly reading. Either way a use-after-free becomes loud at the read.

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

- When a string/array is aliased (incref'd), both references share the same `__ManagedMemory` buffer.
- On mutation (append, set, etc.), if `capacity <= 0` (read-only or slice), the `maxon_cow_check` runtime function allocates a new writable buffer and copies the data.
- For slices (`capacity == -1`), COW also decrefs the parent pointer and zeros it, transitioning the slice to an owned buffer.
- Struct-level COW (`maxon_cow_struct_detach`) handles the case where a parent `__ManagedMemory` has refcount > 1 due to slice references. It allocates a new struct and buffer, copies data, and decrefs the old struct.
- This allows string literals and array slices to share memory without copying until a write occurs.

## Managed Variable Initialization

All managed (heap-allocated) variables are **zero-initialized** at function entry. This includes both named variables and compiler-generated temps. Zero-initialization ensures that scope cleanup can safely call `mm_decref` (with null guard) on variables that may not have been assigned yet (e.g., in conditional branches where only one path allocates).

## Summary of Invariants

1. Every heap object has a 24-byte inline header at `[ptr-24]` containing tag, destructor, and refcount.
2. Refcount starts at 0 after `mm_alloc`. The caller's assignment emits `mm_incref` to set it to 1.
3. Refcount reaches 0 -> destructor called (decrefs managed fields) -> object freed.
4. Reference cycles are impossible -- they are compile-time errors.
5. Every scope exit path decrefs all in-scope managed variables (except returned/kept values and parameters).
6. The global `__mm_alloc_count` tracks live allocations and is checked at exit for leaks.
