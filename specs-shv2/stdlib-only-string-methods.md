---
feature: stdlib-only-string-methods
status: experimental
keywords: [string, stdlib, visibility, module, addressableBytes, byteAtOrPanic]
category: language
---

# Stdlib-only String methods

## Documentation

FOUR of `String`'s methods are `module`-visible in the corpus (`stdlib/String.maxon`) rather than
exported: `addressableBytes()`, which hands out a live view of the string's own UTF-8 bytes;
`byteAt(index)` and `byteAtOrPanic(index)`, which read one of those bytes — the first throwing
`__ManagedMemoryError`, the second with no catchable failure at all; and `hasSingleByteGraphemes()`,
which reports a private field of the record. All four exist for the stdlib's own byte walkers —
`stdlib/URL.maxon` and `stdlib/helpers/url/urlHelpers.maxon` forced the first two, and
`stdlib/helpers/string/{utf8,hash,grapheme}.maxon` forced the other two — and none is part of the
language a user program may write.

⚠ The case names below say "pair", which is what the set was when they were written. They are IDs, and
an id is renamed at the cost of orphaning a golden in every target directory; the count that matters is
the one the refusal itself renders, from the arms' own constants.

The reference compiler refuses a user call to either with its not-exported diagnostic. shv2 enforces the
same visibility on whether the calling file is physically **under `stdlib/`**. The refusal names the reason
rather than falling through to the unknown-method roster — the method exists, it is simply not the
caller's to reach.

⚠ This paragraph used to give the reason as *"shv2 has no `module` keyword and never parses
`stdlib/String.maxon`"*, and **both halves have been false since W17**: `module` is a contextual modifier
shv2's parser recognizes and records, and `stdlib/String.maxon` is a listed module whose declarations it
reads. The location gate is still right, for a different reason — these four members are served by shv2's
own synthesized arms, and the member roster is consulted *ahead* of the corpus, so the call never reaches
the declaration whose `module` visibility could have answered. A member RETIRED onto the corpus takes its
visibility from its declaration instead, because from then on the declaration is the only thing serving it.

⚠ It names the oracle's ANSWER and not the oracle's CODE NUMBER, and the expected stderr below must keep
it that way. A 4-digit code written outside `docs/error-codes.txt` is a copy of the number space, and
this one cannot even be a checked copy: shv2 does not claim that code, so its generated
`ErrorCodeRegistry` has no member to derive the spelling from. Written out, a renumber would leave the
sentence standing and false in four places at once — the message, this file, and three goldens.

⚠ The gate asks about the file's LOCATION and deliberately not about `isStdlibSource`, which answers a
visibility question and hands a project rooted under `stdlib/` its own files back as the user's. Gated
on that instead, `maxon-shv2 build stdlib/URL.maxon` — the command that checks whether a module is
ready to be whitelisted — was told `stdlib\URL.maxon` "is not stdlib source". The spec suite cannot
reach that case (it stages every test's sources under `specs-shv2/.spec-tmp/`, and the multi-file
marker deliberately refuses the `..` that would escape), so the cases below pin the USER half only; the
stdlib half is pinned by every `url` case, which compiles `stdlib/URL.maxon` through the loader.

User code that wants a string's bytes uses `toByteArray()`, which COPIES, so nothing it is handed can
alias the string.

⚠ **EVERY ONE OF THE NAMES IS ON THE `String` MEMBER ROSTER, and the unknown-member refusal names them
(A2q).** The roster describes what the dispatch SERVES; being stdlib-only is a property of those arms,
stated by the refusal above. Left off the roster, the only sentence a user has to go on denied real
methods by silence — which it did, measured, for as long as that sentence was a hand-written second copy
of the arm list. The roster is now derived from the very constants the arms match on, so it cannot deny
one again; the two cases below pin both halves of that.

## Tests

<!-- test: error.addressable-bytes-is-stdlib-only -->
```maxon
function main() returns ExitCode
	let b = "abc".addressableBytes()
	return b.length()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:16: Unsupported: String method 'addressableBytes' — it is STDLIB-ONLY (`module function` in `stdlib/String.maxon`, which the reference compiler refuses to user code as a not-exported error) and this file is not under `stdlib/`. `addressableBytes` hands out a live view of a String's own bytes, and `byteAt`/`byteAtOrPanic` each read one of them — the first through a catchable failure, the second through none — and `hasSingleByteGraphemes` reports a private flag of the record; user code reaches the bytes through `toByteArray()`, which copies
```

<!-- test: error.byte-at-or-panic-is-stdlib-only -->
```maxon
function main() returns ExitCode
	return "abc".byteAtOrPanic(0)
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:15: Unsupported: String method 'byteAtOrPanic' — it is STDLIB-ONLY (`module function` in `stdlib/String.maxon`, which the reference compiler refuses to user code as a not-exported error) and this file is not under `stdlib/`. `addressableBytes` hands out a live view of a String's own bytes, and `byteAt`/`byteAtOrPanic` each read one of them — the first through a catchable failure, the second through none — and `hasSingleByteGraphemes` reports a private flag of the record; user code reaches the bytes through `toByteArray()`, which copies
```

<!-- test: error.byte-at-is-stdlib-only -->
```maxon
function main() returns ExitCode
	return try "abc".byteAt(0) otherwise 1
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:19: Unsupported: String method 'byteAt' — it is STDLIB-ONLY (`module function` in `stdlib/String.maxon`, which the reference compiler refuses to user code as a not-exported error) and this file is not under `stdlib/`. `addressableBytes` hands out a live view of a String's own bytes, and `byteAt`/`byteAtOrPanic` each read one of them — the first through a catchable failure, the second through none — and `hasSingleByteGraphemes` reports a private flag of the record; user code reaches the bytes through `toByteArray()`, which copies
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
error E2015: <fragment>:3:11: Unsupported: String method 'hasSingleByteGraphemes' — it is STDLIB-ONLY (`module function` in `stdlib/String.maxon`, which the reference compiler refuses to user code as a not-exported error) and this file is not under `stdlib/`. `addressableBytes` hands out a live view of a String's own bytes, and `byteAt`/`byteAtOrPanic` each read one of them — the first through a catchable failure, the second through none — and `hasSingleByteGraphemes` reports a private flag of the record; user code reaches the bytes through `toByteArray()`, which copies
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
error E2015: <fragment>:5:12: Unsupported: String method 'addressableBytes' — it is STDLIB-ONLY (`module function` in `stdlib/String.maxon`, which the reference compiler refuses to user code as a not-exported error) and this file is not under `stdlib/`. `addressableBytes` hands out a live view of a String's own bytes, and `byteAt`/`byteAtOrPanic` each read one of them — the first through a catchable failure, the second through none — and `hasSingleByteGraphemes` reports a private flag of the record; user code reaches the bytes through `toByteArray()`, which copies
```

A NEAR MISS of one of the two — `addressableByte` for `addressableBytes` — is a typo and must be answered
as one. The visibility refusal is reachable only once a real method NAME has matched, so the roster
answers here and nobody is told they lack permission for a method nobody has.

⚠ **THE ROSTER NO LONGER NAMES `startsWith`, `endsWith` OR `count`, AND THAT IS NOT AN OMISSION — IT IS
THE RETIREMENT.** Those three are served by `stdlib/String.maxon` now: struck from the roster, each falls
through to the corpus door that is consulted for exactly the names the roster does not carry, and becomes
an ordinary call to the ordinary declared function the corpus already had. The list below therefore says
what it has always said — *what the synthesized surface serves* — and a name leaving it means the corpus
took the member over, never that the member vanished. `startsWith("x")` still works; `string-methods-ascii`
and `string-type` pin that it answers the same.

<!-- test: unknown-string-method-still-gets-the-roster -->
```maxon
function main() returns ExitCode
	return "abc".addressableByte()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:15: Unsupported: `String` member 'addressableByte' — shv2 provides append/byteLength/contains/toLower/toUpper/replace/split/bytes/toByteArray/codepoints/utf16/isEmpty/clone/replaceFirst/startIndex/endIndex/findFirst/findLast/indexAfter/slice/trim/trimStart/trimEnd/addressableBytes/byteAt/byteAtOrPanic/hasSingleByteGraphemes/setByte/hash/equals; that list IS the surface, so nothing else is served here
```

And the roster a user program is handed NAMES both stdlib-only methods. This is the case the derivation
exists for: before it, the sentence listed twenty-six members by hand and omitted exactly these two, so a
user searching it for a way at a String's bytes was told — by silence — that neither existed.

<!-- test: error.unknown-string-method-names-the-stdlib-only-pair -->
```maxon
function main() returns ExitCode
	return "abc".frobnicate()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:15: Unsupported: `String` member 'frobnicate' — shv2 provides append/byteLength/contains/toLower/toUpper/replace/split/bytes/toByteArray/codepoints/utf16/isEmpty/clone/replaceFirst/startIndex/endIndex/findFirst/findLast/indexAfter/slice/trim/trimStart/trimEnd/addressableBytes/byteAt/byteAtOrPanic/hasSingleByteGraphemes/setByte/hash/equals; that list IS the surface, so nothing else is served here
```
