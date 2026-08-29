---
feature: emitted-runtime-body
status: experimental
keywords: [runtime, golden, fragment, codegen, string, view]
category: codegen
---
# Emitted Runtime Bodies

## Documentation

A compiled program CONTAINS more code than its author declares. The entry stub `mrt_start`, the
`__mm_*`/`__slab_*` allocator, the `__str_*`/`__managed_*` families and the `__destruct_*`
thunks are all synthesized by the backend, and a golden fragment shows NONE of them: the
fragment renders only the functions parsed from the PROGRAM's own source, because the shared
scaffolding is identical in every test and would be pure noise in all of them.

The **library** is withheld by the same rule and for the same reason. shv2 compiles `stdlib/`
from source into the same module as the program, so `String.trim`, `Array.reserve` and every
grapheme helper the cone reaches are ordinary module functions the printer could render — and
until 2026-08-29 it did. That made `abs/abs.rt-float.test`, a five-line program, a **5,846-line**
golden of which 120 lines were `main`; it is now 135. What the noise cost was not disk: an edit
anywhere in `stdlib/` moved thousands of goldens at once, and the drift signal the fragments exist
to carry was inside that diff.

That default is right for the scaffolding and wrong for a body a rung has just written.
`__str_bytes_view` publishes a zero-copy `Array with Byte` over a String's own bytes and
counts a reference on the allocation those bytes live in; getting that count wrong is a
use-after-free that BALANCES — the matching release disappears with it — so neither the
exit code, nor the leak gate, nor any golden can see it.

### Naming a body to render: ```RequiredRuntime

A test opts one or more withheld functions INTO its own golden with a
`RequiredRuntime` block, one name per line:

```
__str_bytes_view
```

A **stdlib** body is named the same way and by the same list — `String.trim`, `Array.reserve` —
which is how a case whose subject IS a library body pins the one body it is about. The name has to
be one the program REACHES: an unreached stdlib function is eliminated before the back end, so
naming it renders nothing and is refused exactly as a misspelling is.

Only the named functions are added, and only to that test's fragment; every test without
the block renders byte-identically to a test written before the block existed. A name
that matches no emitted function — or one that names a function the fragment already
shows — is refused by the compiler, so a misspelling cannot silently pin nothing. An
EMPTY block is refused by the spec parser for the same reason: it would render no body at
all, leaving the golden identical to a test that never asked.

### What it pins, and what it does not

⚠ **This block pins the compiler's Target IR — what the backend DECIDED — not the bytes the
linker wrote.** It is the `printDataSection` half of the pair, not the ```RequiredData
half: the text is the printer's own rendering of a lowered, register-allocated body, so it
moves on any change to lowering, allocation or block structure, and is BLIND to anything
below it. An instruction encoder that emits different bytes for the same mnemonic and
operands leaves this golden identical. ```RequiredData / ```RequiredRdata are the gates
that read the linked image back; ```RequiredRuntime is not one of them.

### A hand-assembled chunk: the same block, rendered as BYTES

Some of what the compiler emits is not IR at all. The panic runtime (`mrt_panic` and its
backtrace walker), the green-thread pieces (`__gt_context_switch`, `__gt_trampoline`,
`__gt_morestack`) and arm64's entry stub are assembled as raw machine bytes, so they never
enter the module's function list and there is no body to print for them.

They are still named, and the same ```RequiredRuntime block reaches them: a name no IR
function answers is offered to the emitted chunks, and the one bearing it renders a hex dump
instead of a body.

```
chunk @mrt_panic {
    0000: 55 48 89 e5 …
}
```

⚠ **The bytes are the chunk's own, BEFORE linking** — every intra-module call still shows a
zero displacement (`e8 00 00 00 00`) and every imported call a zero IAT slot. That is
deliberate and it is what makes the golden usable: the linked image resolves those against
where everything landed, so a dump cut from it would move whenever anything ELSE in the
program changed size. A chunk's own buffer depends on nothing outside the chunk — the same
reason a printed IR body shows a branch LABEL rather than a displacement. What this golden
pins is therefore the ASSEMBLER's output, exactly as the IR form pins the lowering's.

## Tests

<!-- test: string-bytes-view-body -->
The byte-view runtime entry, reached through `stdlib/FilePath.maxon`'s own cone.
`__str_bytes_view`'s `bvcoown` block carries the incref that keeps the receiver alive for as long
as the buffer value exists; `bvret` is the arm an IMMORTAL record takes, which increfs nothing
because a `.rdata` record can neither be freed nor written.

⚠ **THE BODY WAS FOUR TIMES THIS SIZE UNTIL W157, AND WHAT IT LOST WAS AN ALLOCATION.** It used to
MINT a fresh 48-byte `Array with Byte` view over the receiver's bytes — `__managed_create`, six
field stores, and a branchy derivation of which allocation to count — because a String record and a
buffer record disagreed at `@40` (`singleByteGraphemes` against `element_destroy`). A String record
now embeds the buffer record whole and keeps its flag at `@48`, so `return managed` is served by
handing back the receiver: one capacity test and one `__mm_incref`. This golden is where that shows
as instructions rather than as a trace.

⚠ **THIS CASE NAMED A PAIR UNTIL W49 WAVE 7, AND ITS SECOND HALF NO LONGER EXISTS AS A RUNTIME
BODY.** `__str_byte_at_or_panic` was `String.byteAtOrPanic`'s synthesized body; that member retired
onto `stdlib/String.maxon:281`, whose own body is `try byteAt(index) otherwise panic(…)` — ordinary
Maxon, compiled as an ordinary function, with no runtime chunk to render. The surviving name is not
an accident of the same retirement: `String.addressableBytes()` retired too, but its corpus body is
`return managed`, and reading a fused wrapper's inline `managed` is exactly what mints this entry
(`Parser.emitFieldLoad`). So the producer moved one call frame down and the body is unchanged.

```maxon
function main() returns ExitCode
	let p = FilePath from "a/b.txt"
	if p.fileExtension() == ".txt" 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredRuntime
__str_bytes_view
```

<!-- test: error-ordinal-in-an-emitted-body -->
An emitted body transcribes an error enum's ORDINAL as a literal — `__managed_set`'s `rejcont` block
returns ordinal 0 with the error flag set, which is the wire format an `otherwise` arm decodes. The
ordinal is a positional fact about a case LIST, and nothing else in the suite can see the literal
the runtime actually carries: the run only sees that *some* error came back.

```maxon
function main() returns ExitCode
	var names = ["a", "b"]
	try names.set(1, value: "c") otherwise panic("set rejected a valid index")
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
__managed_set
```

<!-- test: entry-stub-body -->
The `mrt_` band, from the other side of `isRuntimeFunction`'s two prefixes: the entry
stub every program has and no fragment has ever shown.

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
mrt_start
```

<!-- test: panic-runtime-chunk -->
<!-- targets: x64-windows, x64-linux -->
The panic runtime is HAND-ASSEMBLED bytes, not `TargetOp`s — so it has no IR body, is skipped
by every fragment, and until this case had no gate of any kind: not an exit code (a program
that never panics never enters it), not the leak gate, not a section pin, not a golden. It is
installed in EVERY x64 program, so the smallest possible one pins it. The zero
displacements are the unresolved call fixups the linker fills in; see the note above.

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
mrt_panic
```

<!-- test: stack-growth-chunk -->
<!-- targets: x64-windows -->
`__gt_morestack` — the relocating stack grower every green thread's prologue calls. The
directive's own documentation used to name it as the example of a piece that *could not* be
pinned at any spelling; this case is that claim's retirement. It is installed on demand, so
the program has to actually run a green thread. Gated to x64-windows for
`async-stack-growth.md`'s reason: the grower is hand-written x64 assembly over a
`VirtualAlloc`ed stack.

```maxon
function deepRecurse(n int) returns int
	Runtime.yield()
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function main() returns ExitCode
	let p = async deepRecurse(200)
	let r = await p
	return r as ExitCode
end 'main'
```
```exitcode
200
```
```RequiredRuntime
__gt_morestack
```

<!-- test: a-library-body-named-into-the-golden -->
The LIBRARY half of the same block. `String.trim` is `stdlib/String.maxon`'s own code — withheld
from every fragment by default, exactly as `mrt_start` is — and this is the route that puts one
body back. The case is this door's only gate: withdraw the stdlib half of
`TargetPrinter.isSuppliedFunction` and the block stops pinning anything new (the fragment would
render `String.trim` anyway, and the compiler refuses a name it already shows); withdraw the
NAMING half and the compiler refuses the block outright. Either way this case goes red, which is
what a case that would otherwise only assert an exit code cannot do.

⚠ Its zero-argument sibling `String.trim#` — the overload that supplies
`CharacterSet.whitespacesAndNewlines()` — is what `main` actually calls, and it is NOT named here:
one body per name, and the golden shows the two-argument scan this pins rather than the forwarder.

```maxon
function main() returns ExitCode
	let s = "  abc  "
	return s.trim().byteLength() as ExitCode
end 'main'
```
```exitcode
3
```
```RequiredRuntime
String.trim
```

<!-- test: string-byte-at-body -->
`__managed_byte_at` — the THROWING, bounds-checked byte read behind `String.byteAt`. It leaves through the
dual-register `errorReturn` ABI carrying `__ManagedMemoryError.invalidByteRange`, which is exactly what
`stdlib/String.maxon:266` declares. The corpus's `hashString` is what installs it here, and this block is
its only gate.

⚠ **IT WAS `__str_byte_at` UNTIL W49 WAVE 7, AND THE SUBJECT SURVIVED THE SYMBOL.** `String.byteAt` was a
synthesized arm with its own runtime body; retiring it onto the corpus made the declaration's own
`try managed.byteAt(index)` the real call, so the read now happens where the BUFFER's `byteAt` always
happened — one entry point with one bound, rather than two graphs to keep in step. The
`RequiredRuntime` guard is what forced this edit rather than letting the case quietly pin nothing: a
name no program emits is a loud panic (`TargetPrinter.requireEveryNameRendered`), which is the whole
reason that guard exists.

```maxon
function main() returns ExitCode
	return hashString("a") mod 7
end 'main'
```
```exitcode
3
```
```RequiredRuntime
__managed_byte_at
```
