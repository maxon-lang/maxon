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

⭐⭐ **THE FIVE ARE NO LONGER REFUSED BY ONE MECHANISM, AND THAT IS W49 WAVE 7 — THE SPLIT IS THE
SUBJECT OF THIS FILE NOW.** Four of them are `E3088`, raised by `SemanticCheck` off the corpus's own
`module` keyword; `setByte` alone is still `E2015` from a parser arm reading the calling file's
LOCATION. The reason is not that four were tidied and one was missed:

* A member shv2 SYNTHESIZES is answered by an arm that runs *ahead* of the corpus (`memberBelongsToTheCorpus`
  is consulted only for names the roster does NOT carry), so the declaration whose `module` keyword
  could have answered is never reached. Such a member needs a location gate or it has no gate at all.
* A member RETIRED onto the corpus is an ordinary call to an ordinary declared function, and
  `SemanticCheck.calleeVisibleFrom` -> `ProgramSignatures.isVisibleFrom` applies the visibility the
  corpus WROTE. No second statement of it exists to disagree.

Wave 7 retired the four READ doors, so they moved from the first case to the second. `setByte` cannot
follow, and not for want of a declaration: it WRITES its receiver, and shv2 derives no
`writtenParamMask` for a method writing its own receiver's data (`ARCHITECTURE.md:1121-1132` rules that
this is not a parameter mutation). Routed to the corpus today it would admit
`let s = "abc"; s.setByte(0, value: 65)` — a write through a receiver the caller may only have
borrowed. `append` is held back at the same door for the same reason.

⚠ **THE COUNT SAID "FOUR" UNTIL W49 WAVE 2, IN THIS FILE AND IN THE REFUSAL ITSELF, WHILE FIVE ARMS TOOK
THE GATE.** `setByte` was the missing one — and it had no case here either, so a user who wrote it was
refused by a sentence enumerating four OTHER members and never their own. Wave 2 is what surfaced it:
retiring `toLower`/`toUpper` onto the corpus gave `setByte` its first caller, and the member nobody had
called turned out to be the member nobody had described.

⭐ **AND WAVE 7 DISCHARGED THAT OBLIGATION BY CONSTRUCTION RATHER THAN BY MAINTAINING IT.** The shared
gate is gone: with one arm left, its sentence sits INSIDE that arm, so it cannot enumerate a member it
is not about and cannot omit the member it is. A rule that says "keep this list in step" is a rule that
gets broken; a sentence with one possible subject is not.

⚠ The case names below say "pair", which is what the set was when they were written. They are IDs, and
an id is renamed at the cost of orphaning a golden in every target directory; the count that matters is
what the refusals themselves render.

The reference compiler refuses a user call to any of the five with its not-exported diagnostic. shv2
now answers four of them with a code of its own that means the same thing (`E3088`, module-scoped and
not visible from this directory) and the fifth with the location sentence.

⚠ It names the oracle's ANSWER and not the oracle's CODE NUMBER, and the expected stderr below must keep
it that way. A 4-digit code written outside `docs/error-codes.txt` is a copy of the number space, and
this one cannot even be a checked copy: shv2 does not claim that code, so its generated
`ErrorCodeRegistry` has no member to derive the spelling from.

⚠ The `setByte` gate asks about the file's LOCATION and deliberately not about `isStdlibSource`, which
answers a visibility question and hands a project rooted under `stdlib/` its own files back as the
user's. Gated on that instead, `maxon-shv2 build stdlib/URL.maxon` — the command that checks whether a
module is ready to be whitelisted — was told `stdlib\URL.maxon` "is not stdlib source". The spec suite
cannot reach that case (it stages every test's sources under `specs-shv2/.spec-tmp/`, and the
multi-file marker deliberately refuses the `..` that would escape), so the cases below pin the USER
half only; the stdlib half is pinned by every `url` case, which compiles `stdlib/URL.maxon` through the
loader.

User code that wants a string's bytes uses `toByteArray()`, which COPIES, so nothing it is handed can
alias the string.

⚠ **`setByte` IS ON THE `String` MEMBER ROSTER, and the unknown-member refusal names it (A2q).** The
roster describes what the dispatch SERVES; being stdlib-only is a property of that arm, stated by the
refusal above. Left off the roster, the only sentence a user has to go on would deny a real method by
silence — which it did, measured, for as long as that sentence was a hand-written second copy of the
arm list. **The four retired members are correctly ABSENT from it**: the roster has never claimed to
list what a `String` HAS, only what the synthesized dispatch serves, and a member the corpus serves is
found by the corpus door instead.

## Tests

<!-- test: error.addressable-bytes-is-stdlib-only -->
```maxon
function main() returns ExitCode
	let b = "abc".addressableBytes()
	return b.length()
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:15: function 'String.addressableBytes' is module-scoped and not visible from this directory
```

<!-- test: error.byte-at-or-panic-is-stdlib-only -->
```maxon
function main() returns ExitCode
	return "abc".byteAtOrPanic(0)
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:14: function 'String.byteAtOrPanic' is module-scoped and not visible from this directory
```

<!-- test: error.byte-at-is-stdlib-only -->
```maxon
function main() returns ExitCode
	return try "abc".byteAt(0) otherwise 1
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:18: function 'String.byteAt' is module-scoped and not visible from this directory
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
error E3088: <fragment>:3:10: function 'String.hasSingleByteGraphemes' is module-scoped and not visible from this directory
```

<!-- test: error.set-byte-is-stdlib-only -->
The one of the five that WRITES, and now the only one refused by the parser rather than by its own
declaration. Its refusal matters most: reading a private byte is a privacy question, writing one is a
MUTATION through a receiver the caller may only have borrowed — which is also exactly why it could not
retire with the other four.
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
error E2015: <fragment>:4:8: Unsupported: String method 'setByte' — it is STDLIB-ONLY (`module function` in `stdlib/String.maxon`, which the reference compiler refuses to user code as a not-exported error) and this file is not under `stdlib/`. It WRITES one of a String's own bytes, which would mutate a String the caller may only have borrowed; every other `module` member of `String` is refused by its own declaration, and this one is served here instead because a synthesized arm is what carries the receiver-write refusal
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
error E3088: <fragment>:5:10: function 'String.addressableBytes' is module-scoped and not visible from this directory
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
error E2015: <fragment>:3:15: Unsupported: `String` member 'addressableByte' — shv2 provides append/setByte/hash/equals; that list IS the surface, so nothing else is served here
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
error E2015: <fragment>:3:15: Unsupported: `String` member 'frobnicate' — shv2 provides append/setByte/hash/equals; that list IS the surface, so nothing else is served here
```
