---
feature: builtin-member-rosters
status: experimental
keywords: [character, roster, diagnostics, unsupported, members]
category: language
---

# A builtin type's member roster is DERIVED from its dispatch

## Documentation

Every builtin type the compiler carries methods for — `String`, `Character`, `StringIndex`, `Array`,
`__ManagedMemory` — refuses an unknown member with a sentence that names the members it *does* serve.
One copy of that fact is the whole of what this file is about: the list is built by pushing **the very
constants the arms match on**, in arm order, and the dispatch **gates** on it before any arm runs.

Two directions of drift are closed by the pair, and neither is closed by the other:

- **arm renamed, message not** — impossible, because the message is joined from the constants.
- **arm ADDED, message not** — closed by the gate (a name with no
  roster entry never reaches its arm, so an unadvertised arm is unreachable rather than secretly served)
  plus a **panic** at the fall-through (a roster name that reaches it names the drift, loudly).

`String`'s half is pinned in `specs/stdlib-only-string-methods.md`: its roster names `addressableBytes`
and `byteAtOrPanic`, two real dispatched methods, so the case that names them lives beside the visibility
rule they are refused under.
`StringIndex`'s is pinned by `string-index.md`'s `error.a-string-index-has-two-methods`. That type is
declared by `stdlib/String.maxon`, not the compiler, but its two accessors keep their arms under the
roster-wins rule, so the roster keeps its obligation.

`Character`'s is pinned here.

⚠ The member the case names has to be one **NEITHER SIDE SUPPLIES**, and that is not a free choice: with
`stdlib/Character.maxon` listed, the corpus fall-through serves every member the corpus declares, so a name
like `codepoint` (`stdlib/Character.maxon:123`) is RESOLVED rather than refused — which is the listing
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
error E2015: <fragment>:6:13: Unsupported: `Character` member 'isUpperCase' — the compiler provides bytes/byteLength/asciiValue; that list IS the surface, so nothing else is served here
```
