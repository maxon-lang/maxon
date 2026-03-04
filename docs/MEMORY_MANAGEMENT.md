# Memory Management

This document describes how Maxon's memory manager allocates, tracks, and frees heap memory. Maxon uses **reference counting with hierarchical ownership** — no garbage collector, no scope frames, no tracing. Every heap object has a refcount; when it reaches zero, the object and all its owned children are freed immediately.

## Overview

```
┌─────────────────────────────────────────────────────┐
│ User code                                           │
│   var p = Point{x: 1, y: 2}   // allocate, rc=1    │
│   let q = p                    // alias, rc=2       │
│   p = Point{x: 3, y: 4}       // decref old, rc=1  │
│ end of scope                   // decref q → rc=0   │
│                                // → free             │
└─────────────────────────────────────────────────────┘
```

The memory manager provides five core operations:

| Operation | What it does |
|-----------|-------------|
| `mm_alloc` | Allocate a tracked heap object |
| `mm_alloc_in` | Allocate a child object owned by a parent |
| `mm_incref` | Increment reference count |
| `mm_decref` | Decrement reference count; free if it reaches zero |
| `mm_free` | Immediately free an object and recursively free its children |

## Allocation Layout

Every managed heap object has an **8-byte backpointer header** immediately before the user-visible pointer. The backpointer points to an `AllocEntry` metadata node that tracks the allocation.

```
                       ┌─── user_ptr (what Maxon code sees)
                       │
     ┌────────┐  ┌─────▼──────────────────┐
     │ entry* │  │ user data (size bytes)  │
     └───┬────┘  └────────────────────────┘
         │        [ptr-8]    [ptr]
         │
         ▼
    AllocEntry (80 bytes, 88 in debug mode)
    ┌────────────────────────────────────────┐
    │ +0   user_ptr       → points back     │
    │ +8   size           (u64)             │
    │ +16  next           → sibling link    │
    │ +24  prev           → sibling link    │
    │ +32  child_head     → first child     │
    │ +40  child_tail     → last child      │
    │ +48  owner_entry    → parent entry    │
    │ +56  tag_cstr       → type name       │
    │ +64  refcount       (u64)             │
    │ +72  alloc_id       (u64)             │
    │ +80  magic          (debug only)      │
    └────────────────────────────────────────┘
```

### `mm_alloc(size, tag)` → user_ptr

Allocates a new independent heap object:

1. Calls `HeapAlloc` to allocate `size + 8` bytes for the user data (the extra 8 bytes hold the backpointer).
2. Calls `HeapAlloc` to allocate an 80-byte `AllocEntry`.
3. Initializes the entry: `user_ptr` → payload, `refcount` = 0, all child/sibling pointers = NULL.
4. Writes `[user_ptr - 8] = entry` (the backpointer).
5. Increments the global `__mm_alloc_count` and assigns a unique `alloc_id`.
6. Returns `user_ptr`.

The refcount starts at **zero**. The caller is expected to immediately call `mm_incref` to set it to 1.

### `mm_alloc_in(size, parent_ptr, tag)` → user_ptr

Allocates a **child object** owned by `parent_ptr`. Same as `mm_alloc`, but additionally:

1. Loads the parent's `AllocEntry` from `[parent_ptr - 8]`.
2. Links the new entry into the parent's child list (`child_head`/`child_tail`).
3. Sets `owner_entry` to point to the parent's entry.

Child objects are freed automatically when the parent is freed, unless they have their own refcount > 0, in which case they are **detached** (made independent) instead of freed.

### `mm_realloc(ptr, new_size, tag)` → new_ptr

Reallocates an existing managed object:

1. If `ptr` is NULL, delegates to `mm_alloc(new_size, tag)`.
2. Otherwise, calls `HeapReAlloc` on the payload.
3. Updates the `AllocEntry.user_ptr` and the backpointer at `[new_ptr - 8]`.

## Reference Counting

### `mm_incref(ptr)`

Increments the refcount by 1:

```
entry = [ptr - 8]         // load AllocEntry
entry.refcount += 1       // at offset +64
```

Panics if `ptr` is NULL or unmanaged (no backpointer).

### `mm_decref(ptr)`

Decrements the refcount by 1. If refcount reaches zero, calls `mm_free`:

```
entry = [ptr - 8]
entry.refcount -= 1
if entry.refcount == 0:
    mm_free(entry.user_ptr)
```

Panics on NULL, unmanaged pointers, or refcount underflow (already zero).

### `mm_decref_check(ptr)` → new_refcount

Like `mm_decref`, but returns the new refcount instead of auto-freeing. The caller uses the return value to decide whether to run an inline destructor before calling `mm_free`. This is the mechanism used for structs with managed fields — the caller needs to decref the fields before freeing the parent.

## Freeing

### `mm_free(ptr)`

Frees a managed object:

1. Loads the `AllocEntry` from `[ptr - 8]`.
2. If parent-owned, unlinks from the parent's child list.
3. Calls `mm_free_entry(entry)`.

### `mm_free_entry(entry)`

Recursively frees an entry and its children:

1. Walks the child list (`child_head`):
   - If a child's refcount is 0: recursively calls `mm_free_entry(child)`.
   - If a child's refcount > 0: **detaches** the child (clears `owner_entry`, `next`, `prev`) so it becomes an independent allocation.
2. Decrements `__mm_alloc_count`.
3. Calls `HeapFree` on the `AllocEntry`.
4. Calls `HeapFree` on the payload (the `user_ptr - 8` block).

This hierarchical free ensures that child allocations (like a String's `__ManagedMemory` buffer) are automatically cleaned up when the parent struct is freed.

## What Gets Heap-Allocated

| Type | Heap-allocated? | Notes |
|------|----------------|-------|
| Primitives (`int`, `bool`, `float`, `byte`) | No | Stored inline in stack slots or struct fields |
| Structs | Yes | Every struct instance lives on the heap |
| Strings | Yes | Outer struct (8 bytes: pointer to `__ManagedMemory`) + child `__ManagedMemory` |
| Chars | Yes | Heap-allocated with refcounting, like a small string |
| Arrays (`Array with T`) | Yes | Outer struct (16 bytes: count + `__ManagedMemory` pointer) + child `__ManagedMemory` |
| Chains (`__Chain with T`) | Yes | Doubly-linked list, each node is a 32-byte child allocation |
| Tuples | Yes | Structs with `_0`, `_1`, etc. fields |
| Unions (with associated values) | Yes | Tagged union with payload |
| Enums (no associated values) | No | Stored as integers |
| Closures | Yes | Environment block is heap-allocated |

## Ownership Rules

### Construction

```maxon
var p = Point{x: 1, y: 2}   // mm_alloc → mm_incref → rc=1
```

The compiler emits `mm_alloc` for the struct size, stores the field values, then calls `mm_incref` to set the refcount to 1.

### Aliasing

```maxon
var q = p                    // mm_incref → rc=2
```

Assigning a struct to another variable copies the pointer and increments the refcount. Both variables point to the same heap object.

### Reassignment

```maxon
p = Point{x: 3, y: 4}       // decref old (rc→1), alloc new (rc=1)
```

The old value is decref'd. If its refcount reaches zero, it is freed. The new value is allocated with rc=1.

### Scope Exit

When a variable goes out of scope, its refcount is decremented:

```maxon
function test()
  var p = Point{x: 1, y: 2}   // rc=1
  // ... use p ...
end 'test'                     // decref p → rc=0 → freed
```

### Return

Returning a struct **transfers ownership** to the caller. The returned variable is skipped during scope cleanup (it appears in the `KeepVars` set of `MaxonScopeEndOp`):

```maxon
function makePoint() returns Point
  var p = Point{x: 1, y: 2}   // rc=1
  return p                     // p is NOT decref'd; ownership transfers to caller
end 'makePoint'

function main() returns ExitCode
  var p = makePoint()          // caller now owns it, rc=1
  return 0                     // decref p → rc=0 → freed
end 'main'
```

The compiler detects call-return patterns (`__callret_` prefix) to avoid an unnecessary incref+decref cycle when the caller receives the return value.

### Function Parameters

Struct parameters are incref'd on entry and decref'd on exit:

```maxon
function readLevel(c Config) returns Integer
  // c was incref'd by caller before the call
  return c.level
  // c is decref'd at scope exit
end 'readLevel'
```

### Struct Field Assignment

Assigning to a struct field decrefs the old field value and increfs the new:

```maxon
outer.inner = newInner
// 1. decref old outer.inner
// 2. store newInner pointer in outer.inner field
// 3. incref newInner
```

### Container Operations

| Operation | Effect |
|-----------|--------|
| `arr.push(item)` | incref `item`; container holds a reference |
| `arr.get(index)` | incref the element; caller gets a reference |
| `arr.remove(index)` | transfers the container's reference to caller (no extra incref) |
| `arr.set(index, value)` | decref old element, incref new element |
| `arr.clear()` | decref all elements |
| Container freed | decref all elements via `mm_decref_managed_elements` |

### Clone

```maxon
var b = a.clone()   // allocates a new, independent copy with rc=1
```

The compiler auto-generates `Cloneable` conformance for structs whose fields are all cloneable. Clone creates a deep copy — each managed field is also cloned.

## Scope Cleanup Mechanism

### `MaxonScopeEndOp`

The compiler inserts `MaxonScopeEndOp` at every scope exit point (block end, break, continue, return, throw). This op carries:

- **`VarsToClean`**: list of managed variables declared in this scope
- **`KeepVars`**: variables to skip (e.g., the return value)

During lowering (Maxon → Standard dialect), each variable in `VarsToClean` that isn't in `KeepVars` gets a decref (or destructor call if the type has managed fields).

### Cleanup on All Exit Paths

Scope cleanup runs on every possible exit path:

- **Normal block exit** — `end 'label'`
- **Break** — cleans up the loop body scope before jumping to the loop exit
- **Labeled break** — cleans up all intermediate scopes between the break and the target loop
- **Continue** — cleans up the loop body scope before jumping back to the loop header
- **Return from nested block** — cleans up all enclosing scopes up to the function level
- **Error propagation** — `try` that throws cleans up the function scope before propagating

## Destructors

Maxon generates inline destructors for types with managed fields. There are three destructor patterns:

### Struct Destructor (`StdDestructStructOp`)

For structs with managed fields (fields that are themselves heap-allocated structs, strings, arrays, etc.):

1. `mm_decref_check` the struct — get the new refcount.
2. If refcount > 0, skip destruction (someone else still references it).
3. If refcount == 0:
   - For each managed field at known offset: recursively decref/destruct the field.
   - Call `mm_free` on the struct itself.

```
// Example: Outer { inner: Inner, name: String }
mm_decref_check(outer)    → new_rc
if new_rc != 0: skip
  load field at +0 (inner) → mm_decref
  load field at +8 (name)  → destruct_struct (it has a __ManagedMemory child)
  mm_free(outer)
```

The destructor tree is built recursively via `BuildFieldDestructors`, which walks the type's managed fields and generates a `FieldDestructorInfo` tree. This tree is then emitted as inline x86 code.

### Union Destructor (`StdDestructUnionOp`)

For unions with associated values that contain managed types:

1. `mm_decref_check` the union.
2. If refcount == 0, examine the tag field to determine which case is active.
3. Destruct only the fields of the active case.
4. `mm_free` the union.

### Chain Destructor (`StdDestructChainOp`)

For chains (`__Chain with T`) holding managed elements:

1. `mm_decref_check` the chain header.
2. If refcount == 0, walk all chain nodes and decref their values.
3. Free all nodes and the chain header.

### `mm_decref_managed_elements`

For arrays (`Array with T`) holding managed struct elements, this function iterates the `__ManagedMemory` buffer and decrefs each element pointer. Called when the array is being freed, before freeing the buffer itself.

## `__ManagedMemory`

`__ManagedMemory` is the internal backing store used by `String`, `Array`, and other buffer-based types. It is always allocated as a **child** of its owning struct via `mm_alloc_in`.

```
__ManagedMemory layout (32 bytes):
┌────────────────────────────────────────────┐
│ +0   buffer        (ptr) → heap data      │
│ +8   length        (i64) → element count   │
│ +16  capacity      (i64) → allocated slots │
│ +24  element_size   (i64) → bytes per elem  │
└────────────────────────────────────────────┘
```

- **Copy-on-write**: if `capacity == 0`, the buffer is read-only (e.g., a string literal pointing to static data). Any mutation triggers a copy to a new writable buffer.
- **Growth**: when length reaches capacity, the buffer is reallocated (typically doubled) via `mm_realloc`.
- **Ownership**: the `__ManagedMemory` struct is a child of the parent struct. When the parent is freed, the `__ManagedMemory` is freed along with it (which in turn frees the buffer via its own child list).

## Hierarchical Ownership

The parent-child relationship enables automatic cleanup of composite objects. When a parent is freed, all its children with refcount 0 are recursively freed. Children with refcount > 0 are detached and become independent.

Example: A `String` has a child `__ManagedMemory`, which has a child buffer allocation:

```
String (parent)
  └─ __ManagedMemory (child, allocated via mm_alloc_in)
       └─ buffer (child, allocated via mm_alloc_in)
```

Freeing the String automatically frees the `__ManagedMemory` and the buffer, provided neither has external references.

## Cycle Prevention

Reference cycles are a **compile-time error** (`E4014`). The compiler statically rejects any type that references itself directly or indirectly:

```maxon
// ERROR: type 'Node' contains a reference cycle (via Node → next: Node)
type Node
  export var next Node
end 'Node'

// ERROR: mutual recursion A → B → A
type A
  export var b B
end 'A'
type B
  export var a A
end 'B'

// ERROR: indirect via container
type Folder
  export var children Array with Folder
end 'Folder'
```

This eliminates the need for cycle-breaking mechanisms like weak references or tracing collectors.

## Debug Modes

### `--mm-trace`

Emits trace output to stderr for every memory operation:

```
alloc Point #1 rc=0 [module.function]
incref Point #1 rc=1 [module.function]
decref Point #1 rc=0 [module.function]
  free Point #1
```

Trace output is scope-aware: destructor operations are indented to show nesting. Each allocation has a unique `#id` for correlation.

### `--mm-debug`

Enables runtime corruption detection:

- **Magic value** (`0xA10CDEADA10CDEAD`) at offset +80 in every `AllocEntry`. Validated on every operation to detect use-after-free and heap corruption.
- **Canary value** (`0xCAFEBABEDEADC0DE`) written immediately after each allocation's payload. Checked on free to detect buffer overruns.
- **Double-free detection**: magic is cleared on free; freeing an entry with cleared magic panics.
- **Self-consistency check**: validates that `[entry.user_ptr - 8] == entry` on free.
- **NULL return check**: panics if `HeapAlloc` or `HeapReAlloc` return NULL.

### Leak Check

At program exit, the runtime checks `__mm_alloc_count`. If it is non-zero, it prints a leak diagnostic to stderr. This catches any allocations that were not properly freed.

## Managed Variable Initialization

All managed (heap-allocated) variables are **zero-initialized** at function entry. This ensures that scope cleanup can safely call `mm_decref_if_nonnull` on variables that may not have been assigned yet (e.g., in conditional branches where only one path allocates).

## Summary of Invariants

1. Every heap object has a backpointer at `[ptr-8]` → `AllocEntry`.
2. Refcount starts at 0 after `mm_alloc`; the immediate `mm_incref` sets it to 1.
3. Refcount reaches 0 → object is freed (either by `mm_decref` or by `mm_decref_check` + inline destructor + `mm_free`).
4. Parent-owned children (refcount 0) are freed with the parent. Children with refcount > 0 are detached.
5. Reference cycles are impossible — they are compile-time errors.
6. Every scope exit path decrefs all in-scope managed variables (except returned/kept values).
7. The global `__mm_alloc_count` tracks live allocations and is checked at exit for leaks.
