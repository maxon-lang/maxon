---
title: Memory Model
description: "Reference counting, read-only values, moves and sharing, threads, raw memory, stacks, cloning, heap statistics, and code generation."
sidebar:
  order: 15
---

## Memory Model

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
- **Mutable state belongs to one thread; read-only values may be shared.** `let` is deeply read-only, so a
  read-only value can be shared by any number of holders and threads without copying.
- **Raw memory is reachable only from the library tier** (`stdlib/` and `runtime/`). Programs reach it through
  safe, bounds-checked standard-library APIs.

### Safety Guarantees

| Hazard | What Maxon does |
|--------|-----------------|
| Use after free, double free | Reference counting; a read that could outlive its owner takes its own reference |
| Iterator invalidation | A `for` loop or cursor holds a counted reference to its source |
| Out-of-range collection index | `get`/`set` throw `ArrayError.indexOutOfBounds`; there is no unchecked access |
| Raw memory | Reachable only from the library tier (**E3185**) |
| Forged values | No integer-to-enum or integer-to-union cast, and no interface-to-interface cast (**E3009**) |
| Null | There is no null: every variable, field and element holds a value |
| Uninitialized fields | Every field must be definitely assigned before its struct literal (**E3086**) |
| Writing through read-only data | Compile error (**E3159**, **E3078**) |
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
  elements returned from a function is the one exception: it comes back in the two return registers and no
  record is built (see [Memory Semantics](/docs/language/composite-types/#memory-semantics) under Tuples).

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
	var points = PointArray.create()
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
  reference whenever something before its last use could release the record it came from. Mutating a
  collection while an element read from it is still in use is therefore safe.
- **Loops hold their source.** A `for` loop or cursor holds a counted reference to the collection it walks, so a
  write to that collection inside the loop cannot free the elements being visited.
- **Moves.** A move hands the reference over without changing the count, and consumes the source binding.
  Reading the source afterwards is **E3102**. Assigning the binding a new value makes it usable again.
- **Plain and atomic counts.** Counts are plain within one thread, and atomic only on values shared between
  threads (see [Threads and Shared Data](#threads-and-shared-data)).

### Read-Only Values

`let` makes a value **read-only all the way down**: nothing reachable through it may be written, by any holder,
for as long as it is read-only. That is what makes sharing by reference safe.

| Read-only root | Read-only through |
|----------------|-------------------|
| a `let` binding | everything it reaches |
| a `let` field | everything it reaches, for the record's whole life |
| a module-level `let` | everything it reaches, for the program's whole life |
| a value read from a `SharedValue` or a registry | everything it reaches |
| a stored Map key or Set element | itself, while stored |
| an element of a `readonly` element type | itself, while stored |
| a `for` variable, `if let` binding or `match` binding | the same as its source: read-only from a read-only source, writable from a writable one |

The rules:

- **No write through a read-only value.** A field store, a method that writes its receiver, or an argument the
  callee writes is **E3159**. The diagnostic names the read-only root.
- **No mutable alias of a read-only value.** Binding a read-only value to a `var` is **E3078**, and so is storing
  it into a `var` field, a mutable container element, a parameter that keeps it mutably, or a module `var`
  initializer. [`clone()`](#explicit-cloning) makes an independent mutable copy.
- **A `let` field has no other writer.** A value stored into a `let` field must be:
  - already read-only;
  - a value type;
  - or moved in, either as a sole-owned value or as a `var` at its last use.

  A mutable value that is still shared elsewhere is **E3078**. The same rule applies to a parameter whose callee
  stores it into a `let` field; the compiler reads that from the callee's body.
- **`let b = a`, where `a` is a `var`, moves `a`.** `b` is then the record's only holder, and read-only.
- **Keys are read-only.** A stored Map key or Set element cannot be written through its container, and a key read
  back out is read-only. Storing a key follows the `let`-field rule. Map values and Array elements follow their
  container.
- **Read-only element types.** `Array with readonly T` and `Map with K, readonly V` hold read-only values
  without copying:
  - a read-only value can be stored directly;
  - an element read out is read-only;
  - the container itself stays mutable: elements can still be pushed, replaced and removed.

  `readonly` appears only on a container's element or value type; bindings and fields use `let`.
- **Value types are exempt.** Scalars, `String`, `Character` and records whose fields are all `let` cannot give a
  stored value a second writer, so none of these rules apply to them. A `String` or `Character` a name does not
  own is copied when it is bound to a `var` or stored.

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
	var a = Point.create(1)
	a.x = 2
	let frozen = a                       // a moves into frozen, which is read-only
	let view = frozen                    // view shares frozen's record, read-only
	// frozen.x = 3                      // E3159: 'frozen' is read-only
	// var copy = frozen                 // E3078: 'frozen' is read-only
	var b = frozen.clone()               // an independent, mutable record
	b.x = 5

	var points = PointArray.create()
	points.push(b)                       // the array shares b's record
	var first = try points.get(0) otherwise panic("pushed")
	first.x = 9                          // an element follows its container
	print("{view.x} {b.x} {first.x}\n")     // 2 9 9
	return 0
end 'main'
```

### Moves and Sharing

| Where a value goes | What happens |
|--------------------|--------------|
| `var b = a`, `a` a `var` | `a` moves into `b` |
| `let b = a`, `a` a `var` | `a` moves into `b`, and `b` is read-only |
| `let b = a`, `a` read-only | `b` shares the record, read-only |
| `var b = a`, `a` read-only (a `let` included) | **E3078**; `a.clone()` is the mutable copy |
| an ordinary call argument | shared with the callee for the call, counted where needed; a parameter the callee reassigns gets its own storage |
| an argument the callee keeps in a `let` field | must be read-only, a value type, or moved (a `var` at its last use) |
| an [`async`](/docs/language/async/#starting-a-coroutine) call argument | shared like an ordinary call argument: the coroutine runs on the caller's green thread; a parameter the coroutine reassigns gets storage the coroutine owns; a promise moves to the coroutine |
| a [closure](/docs/language/functions/#closures) capture | the captured local moves into the closure; reading it afterwards is **E3102** |
| a [service](/docs/language/async/#services--spawn) message or `spawn` factory argument | a mutable value moves to the service; a read-only value the sender does not own is shared |
| `SharedValue.publish`, `Key.register` | the value must be solely owned (**E3174**), and every reader sees it read-only |

### Aliasing Between Arguments

Reads are counted, so two arguments that reach the same storage are safe in most calls: `s.append(s)` is
legal. The one refused overlap is a **by-reference parameter** whose storage another argument of the same call
also reaches. A by-reference parameter is one the callee reassigns. The refusal is **E3186**; pass a `clone()` as
one of the two. A promise, or a value of an opaque type parameter, passed at two arguments whose parameters
take it moves at the first, so the second is **E3102**.

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
	var b = a.clone()
	b.x = 99
	print("{a.x} {b.x} {a is b}\n")    // 1 99 false
	return 0
end 'main'
```

`clone()` comes from the `Cloneable` interface (`function clone() returns Self`). The compiler generates it
for any type whose fields are all cloneable; primitives, `String`, and collections of cloneable elements are
cloneable. A generic type's instance clones per instance — a `Map`, a `Set`, a declared `Box with String` —
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
  run in parallel with it or with each other. They switch only at `await`, `sleep`, `Scheduler.yield()` and I/O,
  so their reference counts are plain and sharing between them needs no synchronization.
- **Services own their state.** State that more than one thread uses lives in a [service](/docs/language/async/#services--spawn) and is
  reached by message. Messages and replies move their mutable arguments.
- **Module `var`s stay on one thread.** A module or static `var` that code running on another thread (a service,
  or anything it calls) can reach is **E3143**.
- **Read-only values are shared.** A module `let` reachable from another thread, a value published through
  `SharedValue` or a registry `default`, and a read-only parameter sent in a message are marked shared before a
  second thread can see them. A shared value uses atomic counts, and nothing can write it, because it is read-only.
- **A shared graph must be walkable.** A `SharedValue` or registry value that holds an OS handle, a function value
  or a promise is **E3171**. A module `let` read by a message that the share walk cannot mark is **E3163**.
- **The send check.** At run time, a mutable value moved to another thread must be solely owned; a value that is
  not stops the program with exit code **96**.

### Raw Memory and the Library Tier

Raw memory is reached only through `__Builtins` and `.managed` / `__ManagedMemory`, and both are available only
to the library tier: source files under `stdlib/` and `runtime/`. In any other code these are **E3185**:

- the whole `__Builtins` namespace;
- `.managed`, `__ManagedMemory` and `__ManagedMemoryCursor`, and the methods of a value of either type;
- `cstr()` and `cstring`;
- `Promise.inner`;
- the raw handle types `__ManagedFile`, `__ManagedDirectory`, `__ManagedSocket`, `__ManagedPages` and
  `__ManagedHandle`.

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
	var list = TokenList.create()
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
| [E3078](/docs/cli/error-codes/#e3078--semanticvarfromimmutable) | a read-only value bound or stored where it could be written, or kept by a `let` field while another writer exists |
| [E3086](/docs/cli/error-codes/#e3086--semanticfieldnotinitialized) | a field not definitely assigned before its struct literal |
| [E3102](/docs/cli/error-codes/#e3102--useaftermove) | use of a moved value |
| [E3143](/docs/cli/error-codes/#e3143--semanticmodulevarinserviceprogram) | a module `var` reachable from another thread |
| [E3152](/docs/cli/error-codes/#e3152--rawintrinsicoutsideruntimetier) | `__Raw` used outside `runtime/` |
| [E3159](/docs/cli/error-codes/#e3159--semanticwritethroughimmutablerecord) | a write through a read-only value |
| [E3163](/docs/cli/error-codes/#e3163--semanticunmarkableletreadbyamessage) | a module `let` read by a message that the share walk cannot mark |
| [E3171](/docs/cli/error-codes/#e3171--registryvaluenotshareable) | a shared value whose graph holds an OS handle, a function value or a promise |
| [E3174](/docs/cli/error-codes/#e3174--registrypublishedvaluenotsolelyowned) | a published value that is not solely owned |
| [E3185](/docs/cli/error-codes/#e3185--rawsurfaceoutsidelibrarytier) | raw memory or a raw handle used outside the library tier |
| [E3186](/docs/cli/error-codes/#e3186--argumentoverlapswrittenparameter) | a by-reference parameter that overlaps another argument |
| [E5004](/docs/cli/error-codes/#e5004--unguardedstackchain) | a chain of unchecked runtime functions that can exceed the stack margin |
| exit 96 | a mutable value sent to another thread while not solely owned |
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
