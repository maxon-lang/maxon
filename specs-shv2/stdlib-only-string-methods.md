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

⭐⭐ **ALL FIVE ARE REFUSED BY ONE MECHANISM AGAIN, AND THAT IS W49 WAVE 9 — THE SPLIT THIS FILE
DESCRIBED IS CLOSED.** Every one of them is `E3088`, raised by `SemanticCheck` off the corpus's own
`module` keyword. Between waves 7 and 9 there were two mechanisms: four members had retired onto the
corpus while `setByte` was still `E2015` from a parser arm reading the calling file's LOCATION. The
reason for that split was never that four were tidied and one was missed:

* A member shv2 SYNTHESIZES is answered by an arm that runs *ahead* of the corpus (`memberBelongsToTheCorpus`
  is consulted only for names the roster does NOT carry), so the declaration whose `module` keyword
  could have answered is never reached. Such a member needs a location gate or it has no gate at all.
* A member RETIRED onto the corpus is an ordinary call to an ordinary declared function, and
  `SemanticCheck.calleeVisibleFrom` -> `ProgramSignatures.isVisibleFrom` applies the visibility the
  corpus WROTE. No second statement of it exists to disagree.

Wave 7 retired the four READ doors, so they moved from the first case to the second. **Wave 9 moved
`setByte` after them, and the interesting part is how long a WRONG reason kept it back.**

The reason this file first gave was: *"it WRITES its receiver, and shv2 derives no `writtenParamMask`
for a method writing its own receiver's data, so routed to the corpus today it would admit
`let s = "abc"; s.setByte(0, value: 65)`"* — with `append` held back on the same sentence. Wave 8
discharged it: `Parser.receiverOwnerMask` derives the bit from the ENVELOPE COLLAPSE (a fused wrapper's
inline `managed` IS the receiver's record, so writing it writes the caller's own), `append` retired on
it, and `let s = "hello"; s.append(" world")` is still E3019.

What ACTUALLY held `setByte` back was one token in the corpus, and it was not about ownership or
lowering at all: `stdlib/String.maxon:674`, where `mapAsciiCase` wrote `try work.setByte(i, b + delta)`
with the second argument POSITIONAL. `specs/parameter-labels.md` rules that every argument after the
first must be named; shv2 enforces it (E2053) and the bootstrap does not — so the declared
`String.setByte` could not serve its own only caller, and `stdlib/` would have refused itself. Writing
`value:` closed it, and the parser arm went in the same edit.

⇒ **The lowering did not change; it moved one frame down.** The arm handed the receiver's own RECORD to
`__managed_set_byte`, and the corpus body's `managed.setByte(…)` on a fused record reaches the identical
callee with the identical bound — a String record and an Array record agree on all five slots that
entry reads, and it touches `@40` nowhere.

⚠ **THE COUNT SAID "FOUR" UNTIL W49 WAVE 2, IN THIS FILE AND IN THE REFUSAL ITSELF, WHILE FIVE ARMS TOOK
THE GATE.** `setByte` was the missing one — and it had no case here either, so a user who wrote it was
refused by a sentence enumerating four OTHER members and never their own. Wave 2 is what surfaced it:
retiring `toLower`/`toUpper` onto the corpus gave `setByte` its first caller, and the member nobody had
called turned out to be the member nobody had described.

⭐ **AND WAVE 7 DISCHARGED THAT OBLIGATION BY CONSTRUCTION RATHER THAN BY MAINTAINING IT.** The shared
gate went first: with one arm left, its sentence sat INSIDE that arm, so it could not enumerate a member
it was not about and could not omit the member it was. A rule that says "keep this list in step" is a
rule that gets broken; a sentence with one possible subject is not. Wave 9 then deleted the arm and the
sentence together, which is the same cure taken one step further: with no arm at all, there is nothing
left to keep in step with anything.

⚠ The case names below say "pair", which is what the set was when they were written. They are IDs, and
an id is renamed at the cost of orphaning a golden in every target directory; the count that matters is
what the refusals themselves render.

The reference compiler refuses a user call to any of the five with its not-exported diagnostic. shv2
answers all five with a code of its own that means the same thing (`E3088`, module-scoped and not
visible from this directory).

⚠ It names the oracle's ANSWER and not the oracle's CODE NUMBER, and the expected stderr below must keep
it that way. A 4-digit code written outside `docs/error-codes.txt` is a copy of the number space, and
this one cannot even be a checked copy: shv2 does not claim that code, so its generated
`ErrorCodeRegistry` has no member to derive the spelling from.

User code that wants a string's bytes uses `toByteArray()`, which COPIES, so nothing it is handed can
alias the string.

⚠⚠ **THE `String` MEMBER ROSTER NO LONGER NAMES A SINGLE MEMBER SHV2 SYNTHESIZES, AND `setByte` WAS THE
LAST ONE (W49 wave 9).** What remains on it is `hash`/`equals` — the builtin CONFORMANCES, whose impls
the corpus already defines and which the dispatch reaches by SYMBOL rather than by re-synthesizing —
so the unknown-member refusal below renders `shv2 provides hash/equals`. The roster has never claimed
to list what a `String` HAS, only what the synthesized dispatch serves; every one of the twenty-two
names that have left it is served by `stdlib/String.maxon` through the corpus door instead.

⚠ The location gate this file used to describe (`livesUnderStdlibDirectory`, deliberately not
`isStdlibSource`) is now reached only by the two `__ManagedMemory` members that keep synthesized arms.
Its argument is unchanged and lives with them: gated on `isStdlibSource` instead,
`maxon-shv2 build stdlib/URL.maxon` — the command that checks whether a module compiles standalone
— was told `stdlib\URL.maxon` "is not stdlib source". The spec suite cannot reach that case
(it stages every test's sources under `specs-shv2/.spec-tmp/`, and the multi-file marker deliberately
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
answered as one, and wave 7 changed WHICH sentence answers it without changing that it is a typo. Both
spellings are off the roster now, so both fall through to the corpus door, which does not find either;
the roster refusal is what a reader gets, and nobody is told they lack permission for a method nobody
has.

⚠ **THE ROSTER NO LONGER NAMES `startsWith`, `endsWith`, `count`, `toLower`, `toUpper`, `replace`,
`replaceFirst`, `contains`, `slice`, `split`, `bytes`, `toByteArray`, `codepoints`, `utf16`,
`byteLength`, `isEmpty`, `clone`, `addressableBytes`, `byteAt`, `byteAtOrPanic` OR
`hasSingleByteGraphemes`, AND THAT IS NOT AN OMISSION — IT IS THE RETIREMENT.** Every one is served by
`stdlib/String.maxon` now (W49 waves 1 through 7): struck from the roster, each falls through to the
corpus door that is consulted for exactly the names the roster does not carry, and becomes an ordinary
call to the ordinary declared function the corpus already had. The list therefore says what it has
always said — *what the synthesized surface serves* — and a name leaving it means the corpus took the
member over, never that the member vanished. `startsWith("x")`, `toLower()`, `byteLength()` and
`isEmpty()` all still work; `string-methods-ascii`, `string-type` and `string-type-2` pin that they
answer the same.

⚠⚠ **WAVE 6 IS THE ONE THAT CHANGED A RETURN TYPE, SO "ANSWERS THE SAME" IS NARROWER THERE AND THE
DIFFERENCE IS DELIBERATE.** `bytes()`/`codepoints()`/`utf16()` were MATERIALIZED `Array`s and are now the
corpus's LAZY `ByteView`/`CodepointView`/`UTF16View` (`stdlib/helpers/string/views.maxon`), which hold the
`String` rather than a copy of its bytes. `.count()` and `for x in <view>` answer identically — that is
what the ported cases pin — but a view is not assignable at an `Array with Byte` position, and
`bytearray-element-size` is where that is pinned rather than here.

⚠ **THE LIST IS WHAT A RETIREMENT MOVES, SO IT IS ALSO WHAT A RETIREMENT MUST RE-READ.** `setByte` is on
it and stays there, and wave 2 is what first gave it a caller: the corpus `mapAsciiCase` behind
`toLower`/`toUpper` is the only body in `stdlib/` that writes a byte. It had been served by a lowering
that wrote through a non-owning view and lost the write, which nothing could observe while the member
above it was synthesized.

<!-- test: unknown-string-method-still-gets-the-roster -->
```maxon
function main() returns ExitCode
	return "abc".addressableByte()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:15: Unsupported: `String` member 'addressableByte' — shv2 provides hash/equals; that list IS the surface, so nothing else is served here
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
error E2015: <fragment>:3:15: Unsupported: `String` member 'frobnicate' — shv2 provides hash/equals; that list IS the surface, so nothing else is served here
```
