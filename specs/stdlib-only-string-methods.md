---
feature: stdlib-only-string-methods
status: experimental
keywords: [string, stdlib, visibility, module, addressableBytes, byteAtOrPanic, setByte]
category: language
---

# Stdlib-only String methods

## Documentation

FIVE of `String`'s methods are `module`-visible in the corpus (`stdlib/String.maxon`) rather than
exported: `addressableBytes()`, which hands out a live view of the string's own UTF-8 bytes;
`byteAt(index)` and `byteAtOrPanic(index)`, which read one of those bytes — the first throwing
`__ManagedMemoryError`, the second with no catchable failure at all; `setByte(index, value:)`, the only
one that WRITES; and `hasSingleByteGraphemes()`, which reports a private field of the record. All five
exist for the stdlib's own byte walkers — `stdlib/URL.maxon` and `stdlib/helpers/url/urlHelpers.maxon`
forced the first two, `stdlib/helpers/string/{utf8,hash,grapheme}.maxon` forced two more, and
`String.mapAsciiCase` is `setByte`'s one caller — and none is part of the language a user program may
write.

⭐⭐ **ALL FIVE ARE REFUSED BY ONE MECHANISM.** Every one of them is `E3088`, raised by `SemanticCheck`
off the corpus's own `module` keyword: each is an ordinary call to an ordinary declared function, and
`SemanticCheck.calleeVisibleFrom` -> `ProgramSignatures.isVisibleFrom` applies the visibility the
corpus WROTE. No second statement of it exists to disagree, and no refusal sentence enumerates the
members, so none can omit one.

A member the compiler SYNTHESIZES would be answered by an arm that runs *ahead* of the corpus
(`memberBelongsToTheCorpus` is consulted only for names the roster does NOT carry), so the declaration
whose `module` keyword could answer would never be reached. Such a member needs a location gate or it
has no gate at all. None of the five is synthesized.

`setByte` WRITES its receiver, and `Parser.receiverOwnerMask` derives that bit from the ENVELOPE
COLLAPSE (a fused wrapper's inline `managed` IS the receiver's record, so writing it writes the
caller's own) — so a `let` receiver is still refused: `let s = "hello"; s.append(" world")` is E3019.
Its one caller, `mapAsciiCase`, names the second argument (`work.setByte(i, value: …)`):
`specs/parameter-labels.md` rules that every argument after the first must be named, and the compiler
enforces it (E2053), so a positional one would make `stdlib/` refuse itself.

⇒ The corpus body's `managed.setByte(…)` on a fused record reaches `__managed_set_byte` with the
receiver's own RECORD — a String record and an Array record agree on all five slots that entry reads,
and it touches `@40` nowhere.

⚠ Some case names below say "pair". They are IDs, and an id is renamed at the cost of orphaning a
golden in every target directory; the count that matters is what the refusals themselves render.

A user call to any of the five is refused as module-scoped and not visible from this directory
(`E3088`).

⚠ The expected stderr below must keep
it that way. A 4-digit code written outside `ErrorCodeRegistry.maxon` is a copy of the number space,
and this one cannot even be a checked copy: the compiler does not claim that code, so the `ErrorCode`
enum has no member to derive the spelling from.

User code that wants a string's bytes uses `toByteArray()`, which COPIES, so nothing it is handed can
alias the string.

⚠⚠ **THE `String` MEMBER ROSTER NAMES NO MEMBER THE COMPILER SYNTHESIZES.** What is on it is
`hash`/`equals` — the builtin CONFORMANCES, whose impls the corpus defines and which the dispatch
reaches by SYMBOL rather than by re-synthesizing — so the unknown-member refusal below renders `the
compiler provides hash/equals`. The roster lists only what the synthesized dispatch serves, never what
a `String` HAS; every other member is served by `stdlib/String.maxon` through the corpus door.

⚠ The location gate (`livesUnderStdlibDirectory`, deliberately not `isStdlibSource`) is reached only
by the two `__ManagedMemory` members that keep synthesized arms. Gated on `isStdlibSource` instead,
`maxon build stdlib/URL.maxon` — the command that checks whether a module compiles standalone
— would be told `stdlib\URL.maxon` "is not stdlib source". The spec suite cannot reach that case
(it stages every test's sources under `specs/.spec-tmp/`, and the multi-file marker deliberately
refuses the `..` that would escape), so the cases below pin the USER half only; the stdlib half is
pinned by every `url` case, which compiles `stdlib/URL.maxon` through the loader.

## Tests

<!-- test: error.addressable-bytes-is-stdlib-only -->
```maxon
function main() returns ExitCode
	let b = "abc".addressableBytes()
	return b.length()
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:16: function 'String.addressableBytes' is module-scoped and not visible from this directory
```

<!-- test: error.byte-at-or-panic-is-stdlib-only -->
```maxon
function main() returns ExitCode
	return "abc".byteAtOrPanic(0)
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:15: function 'String.byteAtOrPanic' is module-scoped and not visible from this directory
```

<!-- test: error.byte-at-is-stdlib-only -->
```maxon
function main() returns ExitCode
	return try "abc".byteAt(0) otherwise 1
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:19: function 'String.byteAt' is module-scoped and not visible from this directory
```

<!-- test: error.has-single-byte-graphemes-is-stdlib-only -->
```maxon
function main() returns ExitCode
	if "abc".hasSingleByteGraphemes() 'flagged'
		return 1
	end 'flagged'
	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:11: function 'String.hasSingleByteGraphemes' is module-scoped and not visible from this directory
```

<!-- test: error.set-byte-is-stdlib-only -->
The one of the five that WRITES, and the last one to be refused by its own declaration rather than by a
parser arm. Its refusal matters most: reading a private byte is a privacy question, writing one is a
MUTATION through a receiver the caller may only have borrowed. Note the RECEIVER here is a `let` — so
this case would still be refused after the visibility gate, by E3019, which is what makes it worth
keeping the same shape it had when a parser arm answered it.
```maxon
function main() returns ExitCode
	let s = "abc"
	try s.setByte(0, value: 65) otherwise 'oob'
		return 1
	end 'oob'
	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:4:8: function 'String.setByte' is module-scoped and not visible from this directory
```

<!-- test: error.addressable-bytes-on-a-string-variable-is-stdlib-only -->
```maxon
function main() returns ExitCode
	let s = "abc"
	let t = s.trim()
	let b = t.addressableBytes()
	return b.length()
end 'main'
```
```maxoncstderr
error E3088: <fragment>:5:12: function 'String.addressableBytes' is module-scoped and not visible from this directory
```

A NEAR MISS of a served member — `addressableByte` for `addressableBytes` — is a typo and must be
answered as one. Both spellings are off the roster, so both fall through to the corpus door, which
does not find either; the roster refusal is what a reader gets, and nobody is told they lack permission
for a method nobody has.

⚠ **THE ROSTER DOES NOT NAME `startsWith`, `endsWith`, `count`, `toLower`, `toUpper`, `replace`,
`replaceFirst`, `contains`, `slice`, `split`, `bytes`, `toByteArray`, `codepoints`, `utf16`,
`byteLength`, `isEmpty`, `clone`, `addressableBytes`, `byteAt`, `byteAtOrPanic` OR
`hasSingleByteGraphemes`, AND THAT IS NOT AN OMISSION.** Every one is served by `stdlib/String.maxon`:
off the roster, each falls through to the corpus door that is consulted for exactly the names the
roster does not carry, and is an ordinary call to the ordinary declared function the corpus has. The
list says *what the synthesized surface serves*, never what a `String` has. `startsWith("x")`,
`toLower()`, `byteLength()` and `isEmpty()` all work; `string-methods-ascii`, `string-type` and
`string-type-2` pin their answers.

⚠⚠ **`bytes()`/`codepoints()`/`utf16()` RETURN THE CORPUS'S LAZY `ByteView`/`CodepointView`/`UTF16View`**
(`stdlib/helpers/string/views.maxon`), which hold the `String` rather than a copy of its bytes.
`.count()` and `for x in <view>` answer as a materialized `Array` would, but a view is not assignable at
an `Array with Byte` position, and `bytearray-element-size` is where that is pinned rather than here.

⚠ **`mapAsciiCase`, the corpus body behind `toLower`/`toUpper`, is the only body in `stdlib/` that
writes a byte**, and it writes through `setByte` on the owning `String`, so the write lands.

<!-- test: unknown-string-method-still-gets-the-roster -->
```maxon
function main() returns ExitCode
	return "abc".addressableByte()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:15: Unsupported: `String` member 'addressableByte' — the compiler provides hash/equals/compare; that list IS the surface, so nothing else is served here
```

And the roster a user program is handed NAMES the stdlib-only method it still serves. This is the case
the derivation exists for: before it, the sentence listed twenty-six members by hand and omitted exactly
the two that existed then, so a user searching it for a way at a String's bytes was told — by silence —
that neither existed.

<!-- test: error.unknown-string-method-names-the-stdlib-only-pair -->
```maxon
function main() returns ExitCode
	return "abc".frobnicate()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:15: Unsupported: `String` member 'frobnicate' — the compiler provides hash/equals/compare; that list IS the surface, so nothing else is served here
```
