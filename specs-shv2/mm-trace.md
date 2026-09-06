---
feature: mm-trace
status: stable
keywords: mm-trace, memory manager, debug stream, monitor, alloc, refcount, incref, decref, free
category: tooling
---
# mm-trace

## Documentation

`mm-trace` is a spec-test capture mode that verifies the runtime's memory
management behavior — heap allocations, reference-count transitions, and frees —
by decoding the binary debug-event stream the compiled program emits.

A test enters mm-trace mode when it carries either a `<!-- MmTrace -->` directive
or an ` ```mm-trace ` block. In this mode the harness compiles the program with
the shared-memory debug stream enabled, runs it under `maxon monitor --filter=mm`,
and compares the decoded, normalized event trace against the ` ```mm-trace `
golden block.

⚠ It is ONE capture mode with a FAMILY, not a mode of its own. Its sibling is `log-trace`
(`<!-- LogTrace -->` and ` ```log-trace `), which captures the events the PROGRAM authors
through `__DebugStream` rather than the ones its memory manager emits — see
`specs-shv2/debugstream-log-events.md`. The build flag, the target restriction, the
normalizer, the `--update-required` mint and the splice are the same for both; only the
marker, the fence, the `--filter=` and the decoded line's prefix differ.

The golden is normalized so it is stable across runs and machines:

- Timestamps (`[+SSSS.mmm]`) and depth indentation are stripped, leaving the
  bare `mm_<verb> ...` payload.
- Allocation ids (`#<id>`) are densely renumbered `1, 2, 3, …` by first
  appearance, so the runtime's monotonic counter never leaks into the golden.

Regenerate the golden with `--update-required`.

## Tests

<!-- test: string-builder-append-allocates-no-record-per-append -->
A `StringBuilder` must not allocate a record **per `append`**. It holds one growable buffer, and
`build()` hands that buffer over to the finished `String` rather than copying it — so the whole build
costs the builder's buffer, the builder, the `String`, and the builder's fresh reset buffer. Nothing
scales with the number of appends.

This golden exists because it did not: through the envelope-collapse series (Stages 1–3) each
`sb.append(...)` quietly paid a 40-byte `ByteArray` record, because `appendBytes` wrapped its piece in
`ByteArray.init(piece)` purely to hand it to `Array.append`, which only ever read `.managed` back out
again. Since the collapse an `Array` **is** its `__ManagedMemory`, so that wrapper stopped being free —
it became an allocation per append, and no test could see it. `Array.appendMemory` takes the memory
directly. **If a `ByteArray` line appears here once per `append`, that regression is back.**
<!-- MmTrace -->
```maxon
function main() returns ExitCode
	var sb = StringBuilder.create()
	sb.append("ab")
	sb.append("cd")
	let s = sb.build()
	return s.byteLength() as ExitCode
end 'main'
```
```exitcode
4
```

```mm-trace
mm_alloc ArrayRecord #1 size=48
mm_alloc StringBuilder #2 size=16
mm_alloc ElementBuffer #3 size=4
mm_incref ArrayRecord #1 rc=2
mm_alloc StringRecord #4 size=61
mm_decref ArrayRecord #1 rc=1
mm_alloc ArrayRecord #5 size=48
mm_decref ArrayRecord #1 rc=0
mm_decref ElementBuffer #3 rc=0
mm_free ElementBuffer #3
mm_free ArrayRecord #1
mm_decref StringRecord #4 rc=0
mm_free StringRecord #4
mm_decref StringBuilder #2 rc=0
mm_decref ArrayRecord #5 rc=0
mm_free ArrayRecord #5
mm_free StringBuilder #2
```

<!-- test: heap-alloc-free -->
An interpolated `String` costs ONE record: one `mm_alloc`, one `mm_free`, and a balanced refcount
column in between. That is what this pins — the allocation count, not the retain count.

The middle `incref`/`decref` pair belongs to `print`. Since Stage 4c of the SSO plan `print` reaches
the bytes through `value.addressableBytes()` rather than the raw `.managed` field, and a callee that
returns a value hands its caller an OWNED reference — so it increfs on the way out and `print`
releases at scope end, where a field read was a borrow and did neither. It buys the thing Stage 4b
needs: one named call site per materialization, which is where a short string's allocation will
appear once its bytes live inline in a register. **If an `mm_alloc` line appears here per `print`,
that is the regression to chase — not this pair.**
<!-- MmTrace -->
```maxon
function main() returns ExitCode
	let n = 42
	let s = "value {n}"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```

```mm-trace
mm_alloc InterpolationScratch #1 size=21
mm_alloc StringRecord #2 size=65
mm_decref InterpolationScratch #1 rc=0
mm_free InterpolationScratch #1
mm_decref StringRecord #2 rc=0
mm_free StringRecord #2
```

<!-- test: module-let-byte-string-read-allocates-no-record-per-read -->
A module-scope `let` holding a byte string literal is ONE array for the whole program, so READING it must
cost nothing. Three reads of the same global appear here and the golden is EMPTY: not one memory-manager
event may attend them. The read is a single `.rdata` address, and an address is not an allocation.

What makes that a claim worth pinning is a global with no storage. With `hasStorage == false` the binding
is INLINED at each read, and every inlined read is a fresh `constArrayLiteral` — a 48-byte managed record
allocated, four fields stamped into it, then decref'd and freed at the end of the statement. One heap
record per dynamic read of a constant, which on a hot path is the dominant allocation in the program.
These same three reads cost exactly this per read while the binding had no storage:

    mm_alloc ArrayRecord #1 size=48
    mm_decref ArrayRecord #1 rc=0
    mm_free ArrayRecord #1
    mm_alloc ArrayRecord #2 size=48
    mm_decref ArrayRecord #2 rc=0
    mm_free ArrayRecord #2
    mm_alloc ArrayRecord #3 size=48
    mm_decref ArrayRecord #3 rc=0
    mm_free ArrayRecord #3

**Any line at all below is that regression**, and three `size=48` records for three reads is the exact
shape of it.

<!-- MmTrace -->
```maxon
let Keyword = b"critsplit"

function main() returns ExitCode
	let a = try Keyword.get(0) otherwise 0
	let b = try Keyword.get(1) otherwise 0
	let c = try Keyword.get(2) otherwise 0
	return 0 if a + b + c == 318 else 1
end 'main'
```
```exitcode
0
```
```mm-trace

```

<!-- test: module-var-byte-string-read-allocates-no-record-per-read -->
The negative control for its `let` sibling above: a module-scope `var` byte string, read the same three
times. A `var` global has storage — the array is materialized ONCE at startup and every reference loads
that one record — so its whole trace is that single startup allocation, whatever the read count. It must
STAY that way.

**If this golden grows a line, the change reached the storage-backed global path**, which is not what a
fix to the inlined `let` path is allowed to do.
<!-- MmTrace -->
```maxon
var Buffer = b"critsplit"

function main() returns ExitCode
	let a = try Buffer.get(0) otherwise 0
	let b = try Buffer.get(1) otherwise 0
	let c = try Buffer.get(2) otherwise 0
	return 0 if a + b + c == 318 else 1
end 'main'
```
```exitcode
0
```
```mm-trace
mm_alloc ArrayRecord #1 size=48
mm_decref ArrayRecord #1 rc=0
mm_free ArrayRecord #1
```

<!-- test: module-let-array-globals-cost-no-allocation-at-all -->
A module-scope `let` holding an array LITERAL, and one holding an empty container, are both constants: their
bytes are decided when the program is compiled and nothing about them can change while it runs. So neither
may cost a memory-manager event, and the golden is EMPTY.

⭐ **NEITHER RESERVES A `.data` SLOT, AND THAT IS THE FACT THE EMPTY GOLDEN PINS.** Each is one `.rdata`
record every read addresses directly, so there is no slot for `__module_init` to fill and nothing for
`__maxon_global_cleanup` to release — the same storage model the byte-string sibling above already has. A
slot would put a `__managed_create` back in `__module_init` and its `__managed_decref` back in the cleanup:
one allocation and one free per global, per process, whether or not the program ever looks at it, which is
exactly the six events this golden's emptiness refuses.

**A per-process cost is not a hot path, and that is not why this is pinned.** It is pinned because a global
the compiler builds at run time is a heap record with a reference count, and a reference count is a word two
worker threads can step at once. A constant that lives in the image has no such word. The allocation is the
observable; the shared mutable word is the reason.

Both shapes appear because they reach the image by different routes — a literal's elements are its own bytes,
an empty container's are the absence of any — and a change that images one and forgets the other leaves a
global that still allocates while the file that permits it says none do.

<!-- MmTrace -->
```maxon
typealias Num = int(0 to 1000)
typealias NumArray = Array with Num

let Literal = [7, 9, 11]
let Empty = NumArray.create()

function main() returns ExitCode
	let a = try Literal.get(0) otherwise 0
	let n = Empty.count()
	return 0 if a == 7 and n == 0 else 1
end 'main'
```
```exitcode
0
```
```mm-trace
```

<!-- test: module-let-scalar-struct-costs-no-allocation -->
A module-scope `let` holding a struct of scalars is a constant like any other: every field is decided when
the program is compiled, nothing about it can change while the program runs, and so it must cost no
memory-manager event. The golden is EMPTY.

⭐ **THIS SHAPE CANNOT BE MARKED THE WAY THE ARRAYS ABOVE ARE, AND THAT IS WHY IT IS PINNED SEPARATELY.**
An `Array`'s immortality is a sentinel in its own `capacity@16`, so `emitRecordIsImmortal` reads a slot the
record already has. A user struct has no such slot — offset 16 is whatever the author declared there, and a
test that read it would be comparing a field against a sentinel. The mark for a record of arbitrary shape
has to live OUTSIDE the record, in the allocation header every managed box is addressed through, which for
image data costs bytes in `.rdata` and nothing at run time because there is no allocator on that path.

Measured, before the record moved into the image, for the single global below:

    mm_alloc Minter #1 size=8
    mm_decref Minter #1 rc=0
    mm_free Minter #1

Eight bytes and a free, per process, for a value that never changes and that most programs holding it will
never read.

⚠ **The field is declared `var` deliberately, and reaching it for a write is already refused.** A mutable
field on an immortal record is the shape that would write to a read-only page — and `var mine = Seed;
mine.next = 9` does not compile: **E2015**, *"an aggregate has no owning COPY in shv2, so the write would
reach the global's own record"*. That refusal predates imaging and is what makes a `var` field on this
record safe to place in `.rdata` rather than merely lucky.
<!-- MmTrace -->
```maxon
typealias Count = int(0 to 1000)

type Minter
	export var next as Count

	static function create(next Count) returns Self
		return Self{next: next}
	end 'create'
end 'Minter'

let Seed = Minter.create(7)

function main() returns ExitCode
	return (Seed.next + 9) as ExitCode
end 'main'
```
```exitcode
16
```
```mm-trace
```

<!-- test: module-let-string-array-costs-no-allocation -->
A module-scope `let` holding an array of string LITERALS is a constant twice over: the array never changes,
and neither does any element. So it must cost nothing, and the golden is EMPTY.

⭐ **THIS IS THE SHAPE THAT NEEDS A POINTER BETWEEN TWO IMAGE OBJECTS, WHICH IS WHY IT IS PINNED APART FROM
THE ARRAYS ABOVE.** A byte string's element bytes live in its own blob and a scalar array's live inline, so
each is ONE object and the only address it needs is its own. An array of strings is a table of ADDRESSES:
the element buffer has to name the `.rdata` String records, and naming one image object from inside another
is a relocation the writers did not have. `GlobalDataTable`'s data-to-data channel deliberately carries a
DISTANCE rather than an address, because a distance is the same number in every container format; an address
is a base relocation on PE, an `R_X86_64_RELATIVE` on ELF, a chained rebase on Mach-O and nothing at all on
wasm.

Measured before this global reached the image — **four** allocations for two headings:

    mm_alloc ArrayRecord #1 size=48
    mm_alloc StringRecord #2 size=68
    mm_alloc ElementBuffer #3 size=32
    mm_alloc StringRecord #4 size=65

⚠ **Two of those four are the surprise, and they are the reason a reader should not assume "the literals
were already immortal, so only the array cost anything".** `"## Deferred"` has an immortal `.rdata` record
of its own — and storing it into the array `__str_clone`d it into a fresh heap String anyway, because a
durable store of a constant takes a private copy (`reference-identity.a-durable-store-of-a-constant-copies-it`).
Imaging the table removes the copies with it: the slots address the literals' own records, and a constant
addressed from a constant needs no copy because neither can be written.
<!-- MmTrace -->
```maxon
let Headings = ["## Deferred", "## Notes"]

function main() returns ExitCode
	let first = try Headings.get(0) otherwise ""
	let second = try Headings.get(1) otherwise ""
	return (first.byteLength() + second.byteLength() + Headings.count()) as ExitCode
end 'main'
```
```exitcode
21
```
```mm-trace
```

<!-- test: module-let-empty-map-and-set-cost-no-allocation -->
An empty `Map` and an empty `Set` at module scope are constants, and must cost nothing. The golden is EMPTY.

⭐ **THIS IS THE SHAPE WHOSE COST IS NOT ITS OWN RECORD.** `Map.create()` is `return Self{}` — as trivial as
a factory gets — but a `Map` is a struct whose four container fields carry DEFAULTS, so constructing one
builds four empty column arrays and then the struct that points at them. A `Set` builds three and its own.
Measured, for the two globals below:

    mm_alloc ArrayRecord #1 size=48      keys
    mm_alloc ArrayRecord #2 size=48      values
    mm_alloc ArrayRecord #3 size=48      states
    mm_alloc ArrayRecord #4 size=48      hashes
    mm_alloc Map #5 size=48
    mm_alloc ArrayRecord #6 size=48
    mm_alloc ArrayRecord #7 size=48
    mm_alloc ArrayRecord #8 size=48
    mm_alloc Set #9 size=40

**Nine records, freed nine times, in a program that reads two counts.** The compiler's own source declares
22 such maps and 18 such sets, so this shape alone is 182 allocations before `main` and 182 frees after it.

⚠ **Nothing here is a new mechanism; it is the first shape that needs three of them at once.** The mark
cannot live in the record (a `Map`'s offset 16 is a field, not a capacity) so it needs the header. The
struct's four slots address other image objects, so it needs the absolute data-to-data relocation. And the
bytes are not in the source the way a literal's are — `Self{}` names four factory calls — so the compiler
has to EVALUATE the construction rather than transcribe it. The first two shipped; this pins the third.
<!-- MmTrace -->
```maxon
typealias Key = int(0 to 1000)
typealias KeyMap = Map with (Key, Key)
typealias KeySet = Set with Key

let Empty = KeyMap.create()
let Seen = KeySet.create()

function main() returns ExitCode
	return (Empty.count() as ExitCode) + (Seen.count() as ExitCode)
end 'main'
```
```exitcode
0
```
```mm-trace
```
