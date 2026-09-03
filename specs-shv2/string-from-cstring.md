---
feature: string-from-cstring
status: stable
keywords: [String, fromCString, cstring, ManagedMemory, cstr, NUL, copy, ownership, round-trip]
category: strings
---

# `String.fromCString()` — a NUL-terminated pointer back into an owned `String`

## Documentation

`stdlib/String.maxon`'s `fromCString(cs)` is `cstr()`'s inverse, and the two are the language's only
crossing of the boundary between text and a raw byte pointer. It is how an answer from a `__Builtins.*`
intrinsic **declared to return `cstring`** becomes something a program can read.

It forwards to `__ManagedMemory.fromCString(cstr)` — the surface's second static, beside `create` — which
the compiler lowers to `__mm_from_cstring` (`StringRuntime.buildMmFromCString`).

### ⭐ IT COPIES, AND IT HAS TO

A `cstring` is a bare address carrying **no length, no capacity and no ownership**. There is nothing to
take a view of and no lifetime this call can see, so the bytes are measured with a `strlen` walk and
blitted into a buffer the returned `String` owns. ⇒ The caller may free, mutate or reallocate the source
the moment this returns and the `String` is unaffected — which is the whole reason to convert rather than
to keep the pointer.

The conversion itself is not new code: `emitCStringToManaged` has done exactly this since R4.3, for
`__ManagedDirectory.filename()` and `currentPath()`. What this feature adds is a **name a Maxon program
can reach it by**; `__mm_from_cstring` is a third caller of that builder, not a third copy of the loop.

### ⛔ IT STOPS AT THE FIRST `\0`, BECAUSE THAT IS WHAT NUL-TERMINATED MEANS

A cstring cannot carry an embedded zero. `"a\0b"` is three bytes as a `String`, and one byte after a
round trip through a pointer. That is not a defect to route around — it is the representation, and it is
why bytes that may hold a zero travel as a `ByteArray` through `String.from(bytes)` instead.

### ⚠ THE BLOCKER THIS FEATURE CARRIED FOR THREE RUNGS WAS STALE, IN BOTH HALVES

`Parser.parseManagedMemoryStaticCall` refused the name with *"`fromCString` and `createCursor` are not
built: each needs a `cstring` / cursor type shv2 has no producer for"*, and `SignatureIndex` repeated it.
Both halves were false when written: `parseTypeReference` resolves `cstring`, the lexer has carried the
keyword since P1.2, and `String.cstr()` **produces** one. `createCursor` was separately built as what it
actually is, an instance method. ⇒ **The gap was never a missing type — it was a conversion the runtime
already performed with no Maxon-reachable name on it.** Re-measure a blocker before repeating it.

## Tests

<!-- test: round-trips-a-literal -->
The simplest crossing there is: bytes out to a pointer, bytes back into a `String`. A String LITERAL's
blob is NUL-terminated in `.rdata` by construction, so `cstr()` hands back the blob untouched and this
case exercises the read side alone.
```maxon
function main() returns ExitCode
	let source = "hello, world"
	let round = String.fromCString(source.cstr())
	print("{round} len={round.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello, world len=12
```

<!-- test: round-trips-a-packed-receiver -->
⭐ The other `cstr()` path, so this feature is measured against BOTH of them. A first `append` onto `""`
leaves `capacity == length`, the shape whose terminator slot the record cannot vouch for — so the source
is grown and terminated in place before its pointer is taken (`specs-shv2/string-cstr.md` owns that half).
The exit code is the other half of the assertion: a stranded buffer on either side of the crossing is
**101**, not a wrong string.
```maxon
function main() returns ExitCode
	var packed = ""
	packed.append("packed tight")
	let round = String.fromCString(packed.cstr())
	print("{round} len={round.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
packed tight len=12
```

<!-- test: the-result-is-independent-of-its-source -->
⭐⭐ **THE CASE THAT SAYS IT COPIED.** A view over the source's buffer would pass both cases above and fail
this one: the source is mutated hard enough to REALLOCATE after the conversion, and a `String` still
reading the old block would print rubbish or fault. It prints what it was built from.
```maxon
function main() returns ExitCode
	var owner = ""
	owner.append("original")
	let copy = String.fromCString(owner.cstr())
	owner.append("-and-then-a-great-deal-more-so-the-buffer-moves")
	print("copy={copy}\n")
	print("owner={owner}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
copy=original
owner=original-and-then-a-great-deal-more-so-the-buffer-moves
```

<!-- test: an-empty-source-round-trips-to-an-empty-string -->
The degenerate length. `strlen` finds its terminator at offset 0, so the walk never runs and the record is
built with a zero-byte buffer — the shape most likely to be an off-by-one somewhere, and the reason it has
a case rather than being assumed to follow from the others.
```maxon
function main() returns ExitCode
	let round = String.fromCString("".cstr())
	print("[{round}] len={round.byteLength()} empty={round.isEmpty()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[] len=0 empty=true
```

<!-- test: it-stops-at-an-embedded-nul -->
⛔ **THE TRUNCATION, PINNED AS THE RULE IT IS.** `"a\0b"` is three bytes of `String`; through a pointer it
is one. Pinning it here is what stops a later reader from treating the loss as a bug and "fixing" it by
carrying a length a `cstring` does not have.
```maxon
function main() returns ExitCode
	let embedded = "a\0b"
	let round = String.fromCString(embedded.cstr())
	print("source={embedded.byteLength()} round={round.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
source=3 round=1
```

<!-- test: the-managed-memory-static-is-reachable-directly -->
The layer underneath, asked without the `String` wrapper — because that is where the mechanism actually
lives, and a case that only ever reaches it through `stdlib/String.maxon` could not tell a broken static
from a broken forwarder.
```maxon
function main() returns ExitCode
	let buffer = __ManagedMemory.fromCString("through the static".cstr())
	print("{String.init(buffer)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
through the static
```

<!-- test: error-unknown-managed-memory-static -->
The refusal that names the surface. It renders both statics from the very constants their arms are parsed
with, so the sentence cannot come to disagree with the dispatch — and it tells a reader that
`createCursor` is not unbuilt but MISFILED, which is the cure they can actually apply. Both references
register that member under a `// static methods` heading, which is where the confusion came from.
```maxon
function main() returns ExitCode
	let mm = __ManagedMemory.createCursor()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:27: Unsupported: `__ManagedMemory` static 'createCursor' — the statics are `create(count, elementSize)` and `fromCString(cstr)`; `createCursor` is an INSTANCE method, so write it on a receiver
```
