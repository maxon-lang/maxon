---
title: Memory Model
description: "Reference counting, read-only values, moves and sharing, threads, raw memory, stacks, cloning, heap statistics, and code generation."
sidebar:
  order: 16
---

## Memory Model

**`let` means the name is never reassigned; `var` means it may be.** Neither keyword says whether the data
behind the name can be written: a record held by a `let` can be written through it, and a number, `bool`,
enum or `String` held by a `let` cannot change, because changing one is reassigning it. Data that nothing may
write is declared with `immutable`, or is a constant (see [Regions and Capabilities](/docs/language/regions/)).

Maxon manages memory with **reference counting**. There is no garbage collector and no manual `free`: a
record is released when its last reference goes away. Programs cannot:

- use freed memory, or free memory twice;
- access out of bounds;
- read null or uninitialized memory;
- invalidate an iterator;
- race on data.

Three mechanisms carry this:

- **Reference counting is the safety mechanism.** Every read that could outlive its owner is counted, so a
  record is freed only when nothing can reach it.
- **Mutable state belongs to one thread; read-only values may be shared.** `immutable` data and constants can
  be written by nobody, so any number of holders and threads share them without copying.
- **Raw memory is reachable only from the library tier** (`stdlib/` and `runtime/`). Programs reach it through
  safe, bounds-checked standard-library APIs.

### Safety Guarantees

| Hazard | What Maxon does |
|--------|-----------------|
| Use after free, double free | Reference counting; a read that could outlive its owner takes its own reference, including across the rest of its statement |
| Iterator invalidation | A `for` loop or cursor holds a counted reference to its source |
| Out-of-range collection index | `get`/`set` throw `ArrayError.indexOutOfBounds`, or the compiler proves a `FixedArray` index inside the size |
| Raw memory | Reachable only from the library tier (**E3185**) |
| Forged values | No integer-to-enum or integer-to-union cast, and no interface-to-interface cast (**E3009**) |
| Null | There is no null: every variable, field and element holds a value |
| Uninitialized fields | Every field must be definitely assigned before its struct literal (**E3086**) |
| Writing through read-only data | Compile error (**E3197**, **E3207**) |
| Use after move | Compile error (**E3102**) |
| Data races | Mutable state is owned by one thread, and only read-only values are shared |
| Stack overflow | Checked function prologues; **E5004** for the unchecked runtime chains; guard pages on OS stacks |
| Value outside a typealias's range | Compile error for a known value, **panic** at run time otherwise |
| Division by zero | Compile error for a constant zero; otherwise the division throws `DivisionByZero` |
| Negative shift count | Compile error for a known count, **panic** at run time otherwise |
| Integer overflow | Wraps (two's complement); a range check catches it where the value reaches a checked place |
| Leaks | Exit code **101** when `main` returns with memory still allocated (see [The Leak Checker](#the-leak-checker)) |

### Values and Records

- **Scalars** — integers, floats, `bool`, payload-free enum values, and an
  [inline packed record](/docs/language/composite-types/#inline-packed-records) — are plain values. Assigning one copies it.
- **Records** — values of a `type`, tuples, unions with payloads, strings, arrays and other collections —
  live on the heap. A variable holds a **reference** to its record. A two-element tuple of register-wide
  elements returned from a function is the one exception: it comes back in the two return registers, and
  a call site builds a record only when it needs one (see [Memory Semantics](/docs/language/composite-types/#memory-semantics) under
  Tuples).

### Reference-by-Default Assignment

Storing a record somewhere else shares it rather than copying it, so a field write through one holder is
visible through every other:

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord
	export var y as Coord

	static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	var a = Point.create(1, y: 2)
	let points = PointArray.create(0)
	points.push(a)                                        // the array shares a's record
	var b = try points.get(0) otherwise panic("pushed")
	b.x = 99
	print("{a.x}\n")                                      // 99: a and b are one record

	b = Point.create(5, y: 6)                             // rebinding b leaves a alone
	a = Point.create(7, y: 8)
	points.push(a)
	print("{points.count()}\n")                           // 2
	return 0
end 'main'
```

Reassigning a variable rebinds it to another record without touching the old one. Use
[`clone()`](#explicit-cloning) when you need an independent copy.

### Ownership and Lifetime

Every record carries a reference count and is freed when the count reaches zero. The compiler decides where
counts are needed. A mistake in that analysis can only add a count, so it costs speed, never safety.

- **Counted reads.** A value read out of a collection (`get`, `first`, a cursor) or out of a field takes its own
  reference whenever something before its last use could release the record it came from. That includes
  whatever its own statement evaluates after it: a later operand, argument or literal element, the right-hand
  side of its own write (`x.a.b = x.dropA()`), a default argument filled after the last written one, and the
  arguments of a method called on it (`x.items.push(x.reset())`). Mutating a collection while an element read
  from it is still in use is therefore safe.
- **Loops hold their source.** A `for` loop or cursor holds a counted reference to the collection it walks, so a
  write to that collection inside the loop cannot free the elements being visited.
- **Moves.** A move hands the reference over without changing the count, and consumes the source binding.
  Reading the source afterwards is **E3102**. Assigning the binding a new value makes it usable again.
- **Three kinds of count.** A record in the current region has a plain count, stepped without synchronization
  by the one thread that owns the region. `immutable` data that threads share, such as a `freeze` result, has an
  atomic count. Constant data, which includes every module-level `let`, is immortal: it sits in read-only memory
  and no count is stepped. A `String` buffer and the box of an OS handle start with a plain count and turn atomic
  at the first alias, at a `freeze`, or at a send (see [Threads and Shared Data](#threads-and-shared-data)).

### Read-Only Values

Read-only is a property of the data, whatever keyword names it. Data that can never be written is shared by any
number of holders and threads without copying, which is what makes sharing by reference safe.

| Read-only data | Read-only through |
|----------------|-------------------|
| [constant data](/docs/language/regions/#constant-data): a constant `let`, every module-level `let` and `static let`, a `b"…"` literal | itself, for the program's whole life |
| [`immutable` data](/docs/language/regions/#frozen-data-freeze-and-immutable): a `freeze` result, a read of an `immutable` slot | everything it reaches |
| a value read from a `SharedValue` or a registry | everything it reaches |
| a stored Map key or Set element | itself, while stored |
| an element of an `immutable` element slot (`Array with immutable T`) | itself, while stored |
| a `for` variable, `if let` binding or `match` binding | the same as its source: read-only from a read-only source, writable from a writable one |

The rules:

- **No write through read-only data.** A field store or a method that writes its receiver is **E3197** (**E3207**
  for constant and `immutable` data, whose message names the use that needs it writable); handing it to a
  parameter the callee writes is **E3207**. The diagnostic names the read-only root. A `for` over a read-only
  `List` (frozen, or a module `let`), or over a collection with `immutable` elements, hands out read-only
  elements, so writing one is **E3197**; iterate a `.clone()`. The element an iterator hands out of a frozen
  collection parameter is read-only too, and handing it to a callee that writes it is **E3207**.
- **No mutable place takes read-only data.** Storing it into a plain field, a mutable container element or a
  union payload is **E3196**, and passing it to a parameter the callee stores into a plain place is **E3207**.
  Binding it to a `var` or a `let` is allowed: the name sees the same data and is refused the writes.
  [`clone()`](#explicit-cloning) makes an independent mutable copy.
- **A tuple carries the mark of what it holds.** A tuple literal may hold read-only data, and the tuple is
  read-only wherever it goes, one a call returns included; the rules above apply to its elements.
- **A `let` names writable data.** A `let` binding, a `let` field and a union payload hold records that stay
  writable through every name, so a write through a `let` field reaches the record it holds. An `immutable`
  field takes `immutable` data only.
- **`let b = a`, where `a` is a `var`, moves `a`.** `b` is then the record's only holder. Where `a` is a `let`,
  `b` names the same record and `a` stays usable.
- **Keys are read-only.** A stored Map key or Set element cannot be written through its container, and a key
  read back out is read-only. Both are `immutable` slots: a key is immutable data or is frozen where it is
  inserted. Map values and Array elements follow their container.
- **Immutable element types.** `Array with immutable T` and `Map with K, immutable V` hold frozen values
  without copying:
  - a frozen value can be stored directly, and a mutable one is **E3196** until it is frozen;
  - an element read out is read-only;
  - the container itself stays mutable: elements can still be pushed, replaced and removed.
- **Value types are exempt.** Scalars, `String`, `Character` and records that are immutable aggregates cannot
  give a stored value a second writer, so none of these rules apply to them. A record is an immutable aggregate
  when every field is a `let` and every field's type is a value type or an immutable aggregate; a record with
  a `let` array field is not. A `String` or `Character` a name does not own is copied when it is bound to a
  `var` or stored.

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord

	static function create(x Coord) returns Point
		return Point{x: x}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	let frozen = freeze Point.create(2)  // frozen data
	// frozen.x = 3                      // E3197: a write through frozen data
	let b = frozen.clone()               // an independent, mutable record
	b.x = 5

	let points = PointArray.create(0)
	points.push(b)                       // the array shares b's record
	let first = try points.get(0) otherwise panic("pushed")
	first.x = 9                          // an element follows its container
	print("{frozen.x} {b.x} {first.x}\n")   // 2 9 9
	return 0
end 'main'
```

### Moves and Sharing

| Where a value goes | What happens |
|--------------------|--------------|
| `let b = a` or `var b = a`, `a` a `var` | `a` moves into `b` |
| `let b = a` or `var b = a`, `a` a `let` | `b` names the same record, and `a` stays usable |
| `let b = e`, `e` state something may still write | `b` names it, and the writes are seen through `b` |
| `let b = a`, `a` read-only data (a stored key, a `SharedValue` read, constant or `immutable` data) | `b` names the same data and is refused writes: a write through it is **E3197** or **E3207**; `a.clone()` is the mutable copy |
| a `unique` value (a promise, an OS handle) bound with `let`, pushed into a container or handed to a `spawn` | moves; reading the name afterwards is **E3102**, and `clone()` is **E3214** |
| an ordinary call argument | shared with the callee for the call, counted where needed; a `mutable` parameter is the caller's own storage, so the callee's writes reach it |
| an argument the callee keeps where something may write it | the holder shares the record; constant or `immutable` data is refused (**E3196** into a plain field, element or payload, **E3207** at a parameter the callee stores into a plain place) |
| an argument the callee keeps in an `immutable` field | must be `immutable` data (**E3207** otherwise) |
| an argument to an [`isolated`](/docs/language/regions/#separate-regions-isolated) parameter | moves: a fresh region, or an `isolated` local; reading it afterwards is **E3102** |
| an [`async`](/docs/language/async/#starting-a-coroutine) call argument | shared like an ordinary call argument: the coroutine runs on the caller's green thread, and its promise is [tied](/docs/language/async/#tied-and-must-use-promises) when the argument is the caller's mutable data; a variable at a `mutable` parameter is lent to the coroutine until its promise is awaited or cancelled; a promise moves to the coroutine |
| a [closure](/docs/language/functions/#closures) capture | the captured local moves into the closure; reading it afterwards is **E3102** |
| a [service](/docs/language/async/#services--spawn) message | an `isolated` value moves to the service; an `immutable` value is shared; a tuple of heap-free elements the sender does not solely own is copied |
| a `spawn` factory argument | moves into the service, on a message argument's terms: `isolated` or `immutable` (**E3217** without a word), and a value still held elsewhere is not `isolated` (**E3200**) |
| `SharedValue.publish`, `Key.register` | the value must be solely owned (**E3174**) or a tuple of heap-free elements, which is copied; every reader sees it read-only |

### Aliasing Between Arguments

Reads are counted, so two arguments that reach the same storage are safe in most calls: `s.append(s)` is
legal. The refused shape is a **by-reference cell**: a local handed to a `mutable` parameter the callee writes
is overwritten right after the call's own operands run, so a value an earlier operand already read from that
local would be stale, and a hold cannot protect a value its own cell overwrites. **E3186** refuses a read of
that local by an operand evaluated **before** the write, anywhere in the same statement:

- an earlier operand, an argument of the call itself, or the receiver;
- a ternary condition that wraps the call, at any group depth, because the condition runs first, and the
  arms of a ternary in the order they run;
- a closure literal that captures the local.

```maxon
var label = "original"
let k = two(label, k: put(label))      // E3186: `label` is read by `two`'s first operand, before `put` writes it
let j = two2(put(label), text: label)  // accepted: the read runs after the write and sees "replaced"
```

`specs/regions-byref-order.md` pins the two orders (`call-argument-before`, `call-argument-after`) and the
operand kinds: constructor, union-literal, tuple-literal, call, method, `async`, `match` subject and ternary
operands, `return` and `if` conditions. An operand that does not read the local is accepted beside the cell
(`bump(h.n, h: Holder.create(7))`), and `bump(h.n, h: h.copy())` is refused because `h.copy()` reads `h`.

The message names the other operand and the cure for the kind of cell:

```text
error E3186: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

Where the by-reference cell itself is the operand to move, the cure copies the local into a `var` in an earlier
statement, passes the copy at that position and stores it back after the call (a `let` there is **E2013**).

The refusal is judged per **root binding**. It counts reads of the cell's own root variable and of that
variable's field paths, and all the fields of `self` share one root. Another name for the same record, such as a `var` that took the record by move or a `let`
alias of a parameter, is a pending operand of its own, and the runtime hold a read of it takes keeps the value
alive across the write.

**Only a parameter the callee writes lends its cell.** A callee writes a `mutable` parameter when its body
does, or when it forwards the parameter to a `mutable` parameter that is written (one fact per parameter,
computed over the call graph). `look(s, b: s)` is accepted when `look`'s `a mutable String` is never written.
A call through an interface requirement or a function value counts every `mutable` position as written, and a
generic function writes a parameter if any instance does. Another call's cell counts only while it is lent,
from its argument to its call: two sibling calls that rewrite one local do not overlap, and a nested call does
while the call around it holds the cell. A `mutable` parameter of a value type that the callee never writes
takes a sibling read of the same local.

A promise, or a value of an opaque type parameter, passed at two arguments whose parameters take it moves at
the first, so the second is **E3102**.

### Explicit Cloning

`clone()` returns an independent deep copy:

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord

	static function create(x Coord) returns Point
		return Point{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1)
	let b = a.clone()
	b.x = 99
	print("{a.x} {b.x} {a is b}\n")    // 1 99 false
	return 0
end 'main'
```

`clone()` comes from the `Cloneable` interface (`function clone() returns Self`). The compiler generates it
for any type whose fields are all cloneable; primitives, `String`, and collections of cloneable elements are
cloneable. A collection's `clone()` clones each element it holds, so no record of the copy is a record of the
original, and the copy is solely owned. A generic type's instance clones per instance — a `Map`, a `Set`, a declared `Box with String` —
copying what its type arguments hold. Declare `clone()` yourself for custom behaviour, or for a type holding
an uncloneable field.

A copy of anything that is or holds a promise is **E3141**: a `clone()`, or an array's `slice` or `append`,
reaching a promise through a field, a union payload, a generic instance or an array element. A generic type
whose body copies an `Array with T` field is refused where the program instantiates it with a promise. A
promise owns a green thread exactly one owner may reclaim; `await` it and copy a value holding its result.

A field declared at an [interface](/docs/language/composite-types/#interfaces) type is cloneable when **every** conformer of that interface
in the program is — the copy runs the conformer the value actually holds, which is not known until the
program runs, so the whole program's conformers are what the compiler checks. A conformer that owns an OS
handle, or a generic type, makes the field uncloneable and the `.clone()` is refused with **E2015**, naming
the field, the interface and the conformer. A container ELEMENT held at an interface type is a separate
matter and is never cloneable: an element slot is one machine word and a value at an interface type is two.

A type that both conforms to an interface and holds a value at that interface type cannot be declared at
all — it is a reference cycle, reported as **E4014** (see the ownership rules in `specs/ownership.md`), so no
decorator of that shape reaches the clone check.

### Threads and Shared Data

- **A green thread and its coroutines run one at a time.** The coroutines a green thread starts with `async` never
  run in parallel with it or with each other. They switch only at `await`, `sleep`, `Scheduler.yield()`, I/O
  and a service send given a `sendTimeout:` to a full mailbox, so their reference counts are plain and sharing between them needs no synchronization.
- **Services own their state.** State that more than one thread uses lives in a [service](/docs/language/async/#services--spawn) and is
  reached by message. Messages and replies move their mutable arguments.
- **There is no module-level mutable state.** `var` is a local, a field of a `type`, or a parameter's target; a
  file-scope or `static` `var` is a syntax error. State more than one thread uses is a service's fields or a
  [`SharedValue`](/docs/language/async/#sharedvalue--a-live-value-made-at-run-time).
- **Read-only values are shared.** A module `let` is image data that every thread reads. A `freeze` result, a
  value published through `SharedValue` or a registry `default`, and an `immutable` argument sent in a message
  are marked shared before a second thread can see them. A shared value uses atomic counts, and nothing can
  write it, because it is read-only.
- **A shared graph must be walkable.** A `SharedValue` or registry value that holds an OS handle, a function value
  or a promise is **E3171**. A module `let` can hold none of them.
- **A moved value has one owner.** A mutable value that crosses to another thread is `isolated`, so the sender
  holds no other name for it (**E3200**, **E3102**); the compiler decides this, and a send checks nothing at run
  time. The share walk a `freeze` or a publish runs stops the program with exit code **96** only at a value held
  at an interface type whose conformer no walk can be built for.

### Raw Memory and the Library Tier

Raw memory is reached only through `__Builtins` and `.managed` / `__ManagedMemory`, and both are available only
to the library tier: source files under `stdlib/` and `runtime/`. In any other code these are **E3185**:

- the whole `__Builtins` namespace;
- `.managed`, `__ManagedMemory` and `__ManagedMemoryCursor`, and the methods of a value of either type;
- `cstr()` and `cstring`;
- `Promise.inner`;
- the raw handle types `__ManagedFile`, `__ManagedDirectory`, `__ManagedSocket`, `__ManagedPages`,
  `__ManagedHandle` and `__ManagedPin`.

The diagnostic names the safe API that replaces the member, where there is one (`Process.id()` for
`__Builtins.currentProcessId`, `HeapStats.slab()` for the slab counters).

`__Raw`, the runtime's machine-level floor, is reachable only from `runtime/` (**E3152**).

Programs use the safe standard-library APIs built on top of these, and each is handle-based and bounds-checked:
`File`, `Directory`, the sockets, `Process`, `Scheduler`, `Clock`, `Console`, `Math`, `HeapStats`, `Debuggee` and
`Sampler`.

The rules that rule out forged and missing values:

- **Casts.** An integer cannot be cast to an enum or a union, and an interface cannot be cast to another
  interface; each is **E3009**. A plain enum value comes from `Enum.fromRawValue(raw)`, which throws
  `noSuchRawValue` for a value no case carries, and a union value from its case constructor.
- **Null.** There is none. A value that may be absent is reached with `try … otherwise`, and an operation that can
  fail `throws`.
- **Initialization.** A struct literal must give every field, and a field a factory assigns must be definitely
  assigned on every path to the literal (**E3086**).

### Stacks

- **Green-thread stacks** are pooled. Each starts small and grows on demand up to 1 GiB; past that the program stops
  with `panic: stack overflow`. Every ordinary function checks its stack in its prologue.
- **Unchecked runtime functions.** A few runtime functions have no prologue check. The build proves that every
  chain of them fits the 928-byte margin below the stack limit, and refuses recursion among them (**E5004**).
- **OS stacks** — each machine's system stack, OS threads, the main stack and the signal stack — have guard pages.
- **On arm64**, a frame large enough to skip past a guard is probed page by page.

### Stack Promotion

A record that never escapes its function — never returned, stored in a field or container, captured by a
closure, or passed to a callee that keeps it — and whose fields are all scalars is allocated on the stack
instead of the heap, with no reference counting. This is automatic and does not change behaviour.
`@heap let p = Point.create(1)` forces heap allocation for one declaration.

### Scope Cleanup

When a reference goes out of scope the record's count drops, and a record whose count reaches zero is freed
along with everything it holds — a container releases each element, a record each field. A returned record
is handed to the caller rather than freed.

```maxon
typealias TokenId = int(0 to 1000)

type Token
	export let id as TokenId

	static function create(id TokenId) returns Token
		return Token{id: id}
	end 'create'
end 'Token'

typealias TokenList = List with Token

function countTokens() returns TokenId
	let list = TokenList.create()
	list.append(Token.create(1))
	list.append(Token.create(2))
	return list.count()             // list, then both tokens, are freed on return
end 'countTokens'
```

A type cannot contain itself (**E4014**), so reference cycles between records cannot be built and reference
counting reclaims everything.

### Returning Memory to the Operating System

Freeing a record returns its slot to the allocator, not to the operating system. The allocator hands the
slot back to the size class it came from, so the next record of that size reuses it — but a program's
resident set otherwise stays at its high-water mark for the life of the process.

`HeapStats.scavenge()` is the one call that changes that. It returns the number of bytes whose physical backing
was handed back, and it does two things no other road does:

- **Chunks go back to the page layer, where any size class can take them.** A size class the program has
  finished with is holding memory nothing else can use until this call releases it.
- **Whole 64 KiB granules that nothing is using are decommitted.**

```maxon
// after a burst has been allocated and released
_ = HeapStats.scavenge()            // chunks return to the page layer; nothing is decommitted yet
let released = HeapStats.scavenge() // granules still free are decommitted; `released` is the bytes
```

⚠ **The first call after a population is dropped returns 0.** A granule is released only if it was
already entirely free when the previous call looked at it, so a program that reuses its memory between two
calls never pays a decommit and a re-commit for having been busy. **A caller that wants memory back after a
burst therefore calls it twice.**

The address space is not returned and the reservation is not shrunk — only the physical backing goes. What
the operating system then reports as the process's resident set is its own answer, which is why the figure
here is bytes the allocator asked it to drop rather than bytes observed to leave.

`HeapStats.slab()` reports what the heap holds at any moment as a `SlabUsage`: live, free, cached-free,
parked and committed bytes, and the count of frees made from another thread. Its free and cached-free
figures include the backlog `scavenge()` would return.

### Attributing the Live Heap

`HeapStats.slab()` says how much is out; `HeapStats.census(mode)` says what it is. It walks every live slot once
and returns a `SlabCensus` of 2048 buckets, read back one at a time with `bucketCount(i)` and `bucketBytes(i)`.

| `mode` | Bucket `i` holds |
|--------|------------------|
| `HeapCensusMode.byTag` | the slots whose allocation tag is `i` — available only in a program built with `--debugstream`, which is what puts a tag on a box |
| `HeapCensusMode.bySizeClass` | the slots of size class `i` |

The census's `tallyBytes` is the live bytes the walk itself counted, which is an independent cross-check: the
buckets' sum travels through the table and the tally travels through nothing, so the two figures beside
`slab().live` tell a table fault from a walk fault. Bucket 2046 is the slots whose first word is not a tag — a
string's bytes, an element buffer, a green thread's stack — and bucket 2047 is a tag past the end of the table.
A heap the walk finds inconsistent ends the program with exit code **119** rather than answering.

The compiler's own build uses this through [`--census-by-tag`](/docs/cli/#logging).

### Attributing Allocation Churn

A census says what is still held; it cannot see an allocation that was freed before it looked.
`HeapStats.allocationsTagged(i)` and `HeapStats.bytesTagged(i)` answer the other question — how many
allocations tag `i` has made since the process started, and how many bytes they asked for. Nothing subtracts a
free, so both figures only rise and a difference of two readings is the churn between them.

The table is 2048 buckets, like the census's. Bucket 0 is the allocations the compiler had no declared type to
tag, and bucket 2047 collects every tag past the end of the table.

Both are maintained only in a program built with `--debugstream`, which is what puts a tag on a box; in any other
build they answer 0. Summed over every bucket they equal `HeapStats.allocationsMade()`, which is the cross-check
to reach for. The compiler's own build uses them through
[`--allocations-by-tag`](/docs/cli/#logging).

### The Leak Checker

Every program that uses the heap checks, when `main` returns, that every allocation was released. If any
was not, the program's exit code becomes **101** (nothing else is printed). Reference counting is managed by
the compiler, so a leak is not something ordinary code is expected to cause; `maxon test` reports a leaking
test as leaked. A green thread that was never
awaited or dropped exits with **75** instead (see [Concurrency](/docs/language/async/#exit-codes)).

### Memory Diagnostics

| Code | Meaning |
|------|---------|
| [E3009](/docs/cli/error-codes/#e3009--semanticunsafecast) | a cast that could forge a value: integer to an enum or a union, or interface to interface |
| [E3086](/docs/cli/error-codes/#e3086--semanticfieldnotinitialized) | a field not definitely assigned before its struct literal |
| [E3102](/docs/cli/error-codes/#e3102--useaftermove) | use of a moved value |
| [E3152](/docs/cli/error-codes/#e3152--rawintrinsicoutsideruntimetier) | `__Raw` used outside `runtime/` |
| [E3171](/docs/cli/error-codes/#e3171--registryvaluenotshareable) | a shared value whose graph holds an OS handle, a function value or a promise |
| [E3174](/docs/cli/error-codes/#e3174--registrypublishedvaluenotsolelyowned) | a published value that is not solely owned |
| [E3185](/docs/cli/error-codes/#e3185--rawsurfaceoutsidelibrarytier) | raw memory or a raw handle used outside the library tier |
| [E3186](/docs/cli/error-codes/#e3186--argumentoverlapswrittenparameter) | a local read by an operand evaluated before a call writes it through a `mutable` parameter |
| [E3196](/docs/cli/error-codes/#e3196--semanticcapabilitymismatchatstore)–[E3217](/docs/cli/error-codes/#e3217--semanticmessageneedsacapability) and [E3221](/docs/cli/error-codes/#e3221--semanticholdermaywait)–[E3224](/docs/cli/error-codes/#e3224--semanticlinearrefused) | the capability, window, tied-promise, unique, service-state, holder-wait, reply and linear checks (see [Capability Errors](/docs/language/regions/#capability-errors)) |
| [E5004](/docs/cli/error-codes/#e5004--unguardedstackchain) | a chain of unchecked runtime functions that can exceed the stack margin |
| [E3220](/docs/cli/error-codes/#e3220--semanticmoduleletcallsafunction) | a call in the initializer of a module-level `let` or a `static let` |
| exit 96 | a share walk reached a value held at an interface type whose conformer no walk can be built for |
| exit 101 | memory still allocated when `main` returns |

## Code Generation

### Native Backend

- Maxon has its own code generator: no LLVM, no external assembler and no linker step.
- It writes each target's executable format directly, producing a standalone executable.
- The supported targets are listed under `--target` in the [CLI reference](/docs/cli/).

### Optimizations

The compiler's optimization passes include:

- constant folding and simplification of constant operands
- inlining of small functions and of functions called once
- common subexpression elimination and loop-invariant code motion
- loop unswitching and jump threading
- value-range analysis, which removes range and bounds checks that provably cannot fail
- strength reduction of division and `mod` by constants
- stack promotion of records that do not escape
- dead function and dead global elimination
- hot/cold code layout and jump tables for `match`
- register allocation

Optimizations never change a program's behaviour, including its panics and range checks.

### Runtime Library

- The runtime — memory management, the scheduler, string and arithmetic helpers — is written in Maxon and
  compiled into each program.
- There is no C runtime dependency and nothing to ship beside the executable.
