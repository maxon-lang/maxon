---
feature: builtin-member-rosters
status: experimental
keywords: [character, roster, diagnostics, unsupported, members]
category: language
---

# A builtin type's member roster is DERIVED from its dispatch

## Documentation

Every builtin type shv2 carries methods for — `String`, `Character`, `StringIndex`, `Array`,
`__ManagedMemory` — refuses an unknown member with a sentence that names the members it *does* serve.
That sentence used to be a hand-written second copy of the dispatch's own arm list, and one copy of one
fact is the whole of what this file is about: the list is now built by pushing **the very constants the
arms match on**, in arm order, and the dispatch **gates** on it before any arm runs.

Two directions of drift are closed by the pair, and neither is closed by the other:

- **arm renamed, message not** — impossible, because the message is joined from the constants.
- **arm ADDED, message not** — the direction that actually bit. It is closed by the gate (a name with no
  roster entry never reaches its arm, so an unadvertised arm is unreachable rather than secretly served)
  plus a **panic** at the fall-through (a roster name that reaches it names the drift, loudly).

`String`'s half is pinned in `specs-shv2/stdlib-only-string-methods.md` — the defect the derivation was
built for was that its roster omitted `addressableBytes` and `byteAtOrPanic`, two real dispatched
methods, so the case that names them lives beside the visibility rule they are refused under.
`StringIndex`'s is pinned by `string-index.md`'s `error.a-string-index-has-two-methods`. That type is
no longer the COMPILER's — `stdlib/String.maxon` declares it and W49 wave 3 struck shv2's second layout —
but its two accessors keep their arms under the roster-wins rule, so the roster keeps its obligation.

`Character`'s is here, because it had none at all and its own spec file (`specs-shv2/character-type.md`)
is ported byte-identical from `/specs` and may not gain an shv2-authored case.

⚠ The member the case names has to be one **NEITHER SIDE SUPPLIES**, and that is not a free choice: with
`stdlib/Character.maxon` listed, the corpus fall-through serves every member the corpus declares, so a name
like `codepoint` (`stdlib/Character.maxon:123`) is now RESOLVED rather than refused — which is the listing
working, not the roster failing. `isUpperCase` is declared by neither the roster nor the corpus, so it still
reaches the refusal this case is about.

## Tests

<!-- test: error.unknown-character-method-gets-the-roster -->
### An unknown `Character` member is answered with the derived roster
```maxon
function main() returns ExitCode
	let s = "hi"
	var n = 0
	for c in s 'eachCharacter'
		n = n + c.isUpperCase()
	end 'eachCharacter'
	return n
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:13: Unsupported: `Character` member 'isUpperCase' — shv2 provides bytes/byteLength/asciiValue; that list IS the surface, so nothing else is served here
```
