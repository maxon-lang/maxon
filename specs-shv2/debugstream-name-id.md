---
feature: debugstream-name-id
status: stable
keywords: [__DebugStream, nameId, debugstream, intrinsics, interning, zero-alloc, trace]
category: system
---

# The `__DebugStream.nameId` intrinsic

## Documentation

`__DebugStream` is a compiler builtin TYPE with no instances and no state — only static methods,
dispatched through the same builtin-static door `__Builtins`' members use. `nameId` is the member
that turns a NAME into a number:

```text
let event = __DebugStream.nameId("regalloc.functionAllocated")
```

⚠ **IT IS THE ONE MEMBER THIS COMPILER RECOGNIZES SO FAR.** The five that EMIT an event —
`enabled`, `phaseBegin`, `phaseEnd`, `event`, `text` — arrive with the producer that CONSUMES a name
id; until then each earns the same reserved-callee refusal `error.unknown-member` pins below. `nameId`
comes first because it is the only one that answers a VALUE, and because a name id costs nothing to
mint whether or not anything ever transmits it.

### Its argument is a compile-time NAME, not a runtime value

Every other member of the family takes ordinary Maxon values. `nameId` takes a string LITERAL, and it
is read off its own token rather than resolved as an expression. That is the whole reason the
structured trace tier can sit on a hot path at all: the call becomes a small integer, the text exists
only in the executable's `MXDS_STRS` blob, and **no String is ever built at run time** — so a pass can
emit a trace event from inside the register allocator without allocating into the very `mm` stream the
trace exists to read.

⇒ A non-literal argument is REFUSED rather than silently degraded to something that would allocate.
It is the same rule, and the same wording, `panic(…)` states for its message: *"requires a string
literal"*.

⚠ An INTERPOLATED literal is refused too, and by the ordinary `)` expectation rather than by a rule of
its own — a hole is exactly the thing that cannot be known while the call is parsed. Both reference
compilers answer this shape the same way (the bootstrap's `Expect(TokenType.RightParen)`, and
`__Builtins.ucdByteAt`'s label one door over).

### The ids are DENSE, deduped, and start at 1

Index 0 is reserved for "no name", so a zeroed wire field never resolves to a real one. A fresh name
takes the next index; a name already seen answers the one it was given. The numbering is therefore a
property of the PROGRAM, not of a file, and two spellings of one name in different functions are one
id.

⚠ **THEY ARE MINTED WHETHER OR NOT `--debugstream` IS ON.** The value a program computes may not
depend on a compiler flag, so the interning is unconditional and only the BLOB is gated. A traced
build and a plain one differ in the events they emit and in nothing else.

## Tests

<!-- test: debugstream-name-id.dense-deduped-and-one-based -->
Two distinct names take 1 and 2 in the order they are written, and the second mention of the first
answers 1 again. This is the whole observable contract, and it is the bootstrap's numbering to the
digit (measured: `a=1 b=2 c=1`).
```maxon
function main() returns ExitCode
	let a = __DebugStream.nameId("alpha")
	let b = __DebugStream.nameId("beta")
	let c = __DebugStream.nameId("alpha")
	print("a={a} b={b} c={c}")
	return 0
end 'main'
```
```stdout
a=1 b=2 c=1
```
```exitcode
0
```

<!-- test: debugstream-name-id.dedup-is-whole-program -->
The table is the PROGRAM's, not the call site's: one name written in two different functions is one
id, and a third name declared between them still takes the next free index rather than a per-function
one. The numbering follows DECLARATION order, which is the order the one pass that sees the whole
merged module walks it in.
```maxon
typealias NameId = int(0 to 65535)

function first() returns NameId
	return __DebugStream.nameId("shared") as NameId
end 'first'

function middle() returns NameId
	return __DebugStream.nameId("other") as NameId
end 'middle'

function second() returns NameId
	return __DebugStream.nameId("shared") as NameId
end 'second'

function main() returns ExitCode
	print("{first()} {middle()} {second()}")
	return 0
end 'main'
```
```stdout
1 2 1
```
```exitcode
0
```

<!-- test: debugstream-name-id.the-empty-name-is-no-name -->
⚠ **A DELIBERATE DIVERGENCE FROM THE ORACLE, ON A DEGENERATE INPUT, AND IT IS PINNED SO IT STAYS
DELIBERATE.** Index 0 is the monitor's *"no name"*, and the seed that reserves it is INDEXED — so
`nameId("")` answers the reservation rather than minting a second entry that means the same thing. The
bootstrap's map starts empty, so it mints 1 there and then renders it as `name=1` anyway, because its own
resolver rejects an empty entry (`DebugStreamDecode.ResolveInternedName`). Both compilers agree the name is
unresolvable; they disagree only about the number, and an empty name IS no name.
```maxon
function main() returns ExitCode
	print("{__DebugStream.nameId("")} {__DebugStream.nameId("real")}")
	return 0
end 'main'
```
```stdout
0 1
```
```exitcode
0
```

<!-- test: debugstream-name-id.error.non-literal-argument -->
A `String` binding is not a compile-time name. Refused with the wording `panic(…)` uses for the
identical requirement — the requirement is shared, so the two cannot come to describe it differently.
```maxon
function main() returns ExitCode
	let name = "alpha"
	let id = __DebugStream.nameId(name)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:32: '__DebugStream.nameId' requires a string literal, but its argument is String
```

<!-- test: debugstream-name-id.error.interpolated-argument -->
A hole cannot be known while the call is parsed, so an interpolated literal is refused at the `)` the
call did not have — the same answer `__Builtins.ucdByteAt`'s label gets for the same shape, and the
bootstrap's.
```maxon
function main() returns ExitCode
	let n = 1
	let id = __DebugStream.nameId("alpha{n}")
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:4:38: Expected ')' but got 'interpolation start'
```

<!-- test: debugstream-name-id.error.unknown-member -->
`nameId` is the only `__DebugStream` member this compiler recognizes. An unrecognized one reaches the
same reserved-callee rejection every other unknown `__` callee gets, at the call's own span.
```maxon
function main() returns ExitCode
	let id = __DebugStream.nope("alpha")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:25: call to undefined function '__DebugStream.nope': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```
