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

Every managed heap object has a **24-byte inline header** immediately before the user-visible pointer. The header stores the type tag, destructor function pointer, and reference count.

```
                       +--- user_ptr (what Maxon code sees)
                       |
     +---------+-----------+---------+-------------------------+
     |  tag    | destructor| refcount|  user data (size bytes) |
     +---------+-----------+---------+-------------------------+
     [ptr-24]   [ptr-16]    [ptr-8]   [ptr]
```

| Offset | Field | Type | Description |
|--------|-------|------|-------------|
| `ptr - 24` | `packed_id` | `u64` | `(alloc_id << 16) \| tag_index` — alloc ID and type tag packed for trace |
| `ptr - 16` | `destructor` | `fn_ptr` | Generated destructor function (or NULL for no managed fields) |
| `ptr - 8` | `refcount` | `u64` | Reference count |
| `ptr` | user data | varies | The object's fields |

## API

### `mm_alloc(size, destructor, tag)` -> user_ptr

Allocates a new heap object:

1. Calls `HeapAlloc` to allocate `size + 24` bytes (24 for the inline header).
2. Initializes the header: `refcount = 0`, `destructor = destructor`, `tag = tag`.
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

### Self-hosted runtime: `parent_ptr` sentinels and small-array inline storage

The self-hosted runtime (`stdlib/Internals.maxon`) keys teardown on the
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
allocation for the record plus a second for the element buffer). It is a
self-hosted-only optimization; the C# bootstrap uses the capacity-keyed model
described above.

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

Returning a struct **passes lifetime responsibility** to the caller. The returned variable is skipped during scope cleanup (it appears in the `KeepVars` set of `MaxonScopeEndOp`):

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

The compiler marks call-return temps with `OwnershipFlags.CallReturn`. When the caller assigns the return value to a named variable, the registry detects the `CallReturn` flag and skips the incref -- the callee already allocated at rc=1, so the reference passes directly without an extra retain.

### Function Parameters

Function parameters are **not owned** by the callee. The caller retains ownership and is responsible for the parameter's lifetime. Parameters are marked with `OwnershipFlags.IsParam` and skipped during scope-end cleanup:

```maxon
typealias Integer = int(i64.min to i64.max)

function readLevel(c Config) returns Integer
	// c is not owned (IsParam flag); no incref on entry, no decref on exit
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

### `MaxonScopeEndOp`

The compiler inserts `MaxonScopeEndOp` at every scope exit point (block end, break, continue, return, throw). This op carries:

- **`VarsToClean`**: list of managed variables declared in this scope
- **`KeepVars`**: variables to skip (e.g., the return value)
- **`VarMetadata`** (optional): `IReadOnlyDictionary<string, (OwnershipFlags Flags, string? StructTypeName)>` -- propagates ownership flags and type information from the parser to the lowering layer

During lowering, the lowering layer reads the flags from `VarMetadata` to determine cleanup behavior -- variables with `IsParam` are skipped, etc.

### Cleanup on All Exit Paths

Scope cleanup runs on every possible exit path:

- **Normal block exit** -- `end 'label'`
- **Break** -- cleans up the loop body scope before jumping to the loop exit
- **Labeled break** -- cleans up all intermediate scopes between the break and the target loop
- **Continue** -- cleans up the loop body scope before jumping back to the loop header
- **Return from nested block** -- cleans up all enclosing scopes up to the function level
- **Error propagation** -- `try` that throws cleans up the function scope before propagating

## Variable Registry

The `VarRegistry` is the unified system for creating and tracking all managed variables across both the parser and lowering layers.

### Design Principles

- **Factory pattern**: The only way to create a variable is through the registry (`Declare()` or `CreateTemp()`), which automatically registers it with ownership metadata.
- **Flag-based classification**: Variables carry an `OwnershipFlags` enum instead of being classified by name prefix.
- **Cross-layer propagation**: The parser attaches ownership metadata to IR ops, so the lowering layer reads flags directly.

### `OwnershipFlags`

```csharp
[Flags]
enum OwnershipFlags {
    None       = 0,
    CallReturn = 1 << 0,  // Callee allocated at rc=1; skip incref on first assign
    Orphan     = 1 << 2,  // Not consumed by parser-tracked var; needs scope-end cleanup
    SelfReturn = 1 << 3,  // Returned from self-returning method; alias, not fresh alloc
    IsTemp     = 1 << 4,  // Internal temp variable (cleaned after user vars at scope end)
    IsParam    = 1 << 5,  // Function parameter (not owned, skip decref at scope end)
}
```

### Two Registration Modes

| Mode | Method | Layer | Produces | Use case |
|------|--------|-------|----------|----------|
| Parser-level | `Declare()` | Parser | `VarInfo` | User variables, parser temps |
| Lowering-level | `CreateTemp()` | Lowering | `TempVarInfo` | Lowering temps (`__callret_`, `__field_`, `__struct_`, etc.) |

Parser-level variables participate in scope management (`PushScope`/`PopScope`) and appear in `MaxonScopeEndOp.VarsToClean`. Lowering-level temps are tracked separately and cleaned up by the lowering layer itself.

### Scope Management

- **`PushScope()`** -- snapshots current variable keys before entering a nested scope.
- **`PopScope()`** -- removes variables declared since the last `PushScope`, restoring the parent scope.
- **`SnapshotKeys()` / `KeysSince(snapshot)`** -- manual scope tracking for loops and match expressions.

### Scope-End Ordering

`GetScopeEndVars()` returns variables in cleanup order: **user variables first, then temps**. This ordering is determined by the `IsTemp` flag. User variables are cleaned before temps because a user variable may alias a temp.

### Ownership Transfer

When a parser-tracked variable takes ownership of a value held by a temp, `TransferTempOwnership()` handles the handoff:

- **`CallReturn` temps**: Removed from tracking entirely. The named variable now owns the value.
- **Other temps**: Re-inserted at the end of the variable map for correct cleanup ordering.

### Orphan Temps

A lowering-level temp is marked `Orphan` when it holds a managed value that was never consumed by a parser-tracked variable (e.g., a struct literal passed directly to a function argument). Orphan temps are tracked via `VarRegistry.OrphanTemps` and cleaned up at scope end.

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

### `--mm-trace`

Emits trace output to stderr for every memory operation:

```
alloc Point #1 rc=1 [module.function]
incref Point #1 rc=2 [module.function]
decref Point #1 rc=1 [module.function]
decref Point #1 rc=0 [module.function]
  destruct Point #1
  free Point #1
```

Trace output is scope-aware: destructor operations are indented to show nesting. Each allocation has a unique `#id` for correlation.

### `--mm-debug`

Enables runtime corruption detection:

- **Canary value** written after each allocation's payload. Checked on free to detect buffer overruns.
- **Double-free detection**: header is cleared on free; freeing a cleared header panics.
- **NULL return check**: panics if `HeapAlloc` or `HeapReAlloc` return NULL.
- **Per-tag leak breakdown** at exit: `mm_leak_check` prints a per-type tally of live allocations in addition to the total, so leaks can be attributed to a specific type without re-running under `--mm-trace`.

### Leak Check

At program exit, the runtime checks `__mm_alloc_count`. If it is non-zero, it prints a leak diagnostic to stderr.

Under `--mm-debug`, the runtime also maintains `__mm_alloc_count_by_tag`, an array indexed by tag_index. `mm_alloc` atomically bumps the slot for its tag; `mm_free` reads the tag back out of the packed_id header and atomically decrements it. The leak check walks this array and prints one line per non-zero slot:

```text
MM leak: 8 allocation(s) remain
  3 String
  3 __ManagedMemory
  1 __ManagedMemory_Integer
  1 IntArray
  5 (raw)
```

Untagged raw allocations (green-thread stacks, pipe buffers, etc.) are grouped under a trailing `(raw)` line sourced from `__mm_raw_alloc_count`. Per-tag tracking is only compiled in when `--mm-debug` is passed; release builds skip the extra atomics.

## Copy-on-Write (COW)

Strings and arrays use copy-on-write semantics for efficient sharing:

- When a string/array is aliased (incref'd), both references share the same `__ManagedMemory` buffer.
- On mutation (append, set, etc.), if `capacity <= 0` (read-only or slice), the `maxon_cow_check` runtime function allocates a new writable buffer and copies the data.
- For slices (`capacity == -1`), COW also decrefs the parent pointer and zeros it, transitioning the slice to an owned buffer.
- Struct-level COW (`maxon_cow_struct_detach`) handles the case where a parent `__ManagedMemory` has refcount > 1 due to slice references. It allocates a new struct and buffer, copies data, and decrefs the old struct.
- This allows string literals and array slices to share memory without copying until a write occurs.

## Managed Variable Initialization

All managed (heap-allocated) variables are **zero-initialized** at function entry. This includes both parser-level variables and orphan temps from the `VarRegistry`. Zero-initialization ensures that scope cleanup can safely call `mm_decref` (with null guard) on variables that may not have been assigned yet (e.g., in conditional branches where only one path allocates).

## Summary of Invariants

1. Every heap object has a 24-byte inline header at `[ptr-24]` containing tag, destructor, and refcount.
2. Refcount starts at 0 after `mm_alloc`. The caller's assignment emits `mm_incref` to set it to 1.
3. Refcount reaches 0 -> destructor called (decrefs managed fields) -> object freed.
4. Reference cycles are impossible -- they are compile-time errors.
5. Every scope exit path decrefs all in-scope managed variables (except returned/kept values and parameters).
6. The global `__mm_alloc_count` tracks live allocations and is checked at exit for leaks.
7. All managed variables must be created through the `VarRegistry`. Direct variable creation outside the registry is not permitted.
