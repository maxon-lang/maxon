---
feature: builtin-member-rosters
status: experimental
keywords: [character, roster, diagnostics, unsupported, members]
category: language
---

# A builtin type's member roster is DERIVED from its dispatch

## Documentation

Every builtin type shv2 carries methods for — `String`, `Character`, `__StringIndex`, `Array`,
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
`__StringIndex`'s is pinned by `string-index.md`'s `error.a-string-index-has-two-methods`.

`Character`'s is here, because it had none at all and its own spec file (`specs-shv2/character-type.md`)
is ported byte-identical from `/specs` and may not gain an shv2-authored case.

## Tests

<!-- test: error.unknown-character-method-gets-the-roster -->
### An unknown `Character` member is answered with the derived roster
```maxon
function main() returns ExitCode
	let s = "hi"
	var n = 0
	for c in s 'eachCharacter'
		n = n + c.codepoint()
	end 'eachCharacter'
	return n
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:13: Unsupported: `Character` member 'codepoint' — shv2 provides bytes/byteLength/asciiValue; `codepoint`/`codepoints`/`compare`/`hash`/`clone` and the static `fromCodepoint` have no consumer in the corpus
```
