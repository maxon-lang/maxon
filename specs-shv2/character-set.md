---
feature: character-set
status: experimental
keywords: [character-set, set, trim, unicode, general-category, ownership]
category: types
---

# CharacterSet and Character-keyed Sets — the shapes the ported corpus does not reach

## Documentation

`specs-shv2/string-trim.md`, `specs-shv2/unicode-category.md` and `specs-shv2/unicode-escape.md` are
ported byte-identical from `/specs` and are the acceptance test for P1.8 Slice D. This file is **not** a
second copy of them: every case below exists because **breaking the guard it covers turned ZERO or ONE of
those tests red**, which is a coverage hole rather than a passing grade.

Each case names the sabotage that found it.

| Sabotage | Reds in the ported corpus | Covered here by |
|---|---|---|
| `__str_trim`'s all-matched arm reopened to the byte length | 2 — both `trim()` | `trim-start-all-match`, `trim-end-all-match` |
| a `Set` key argument no longer adopts the set's key type | 0 | `character-key-insert-contains`, `character-key-remove` |
| `Character` withdrawn from the `Set` key-type gate | 0 | `character-set-create` |
| `__ucd_cat`'s supplementary-plane search removed | 0 | `supplementary-plane-category` |
| a bare-local member set not poisoned when `from` consumes it | 0 | `member-set-moved-into-from` |

## Tests

<!-- test: trim-start-all-match -->
### trimStart on a string that is entirely trimmable
The ported corpus has `trim()` on an all-whitespace string (`string-trim/trim-all-whitespace`) and
nothing for the one-ended forms — but the all-matched path CANNOT consult the end flags (with
`keptStart`/`keptEnd` collapsed to 0, an untrimmed END would reopen the range to the whole string), so
that arm is exactly what these two pin.
```maxon
function main() returns ExitCode
	let s = "   "
	let r = s.trimStart()
	print("[{r}]")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[]
```

<!-- test: trim-end-all-match -->
### trimEnd on a string that is entirely trimmable
```maxon
function main() returns ExitCode
	let s = "\t\n\r"
	let r = s.trimEnd()
	print("[{r}]")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[]
```

<!-- test: character-set-create -->
### A Character-keyed Set is constructible
`CharSet` is the builtin alias for `Set with Character` — the reference's own
`typealias CharSet = Set with Character`. The ported corpus only ever reaches a member set through
`CharSet from [...]`, which does not consult the key-type gate at all.
```maxon
function main() returns ExitCode
	var cs = CharSet.create()
	print("{cs.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: character-key-insert-contains -->
### A single-byte character literal is a Character key, not an int
shv2 types a character literal by its byte WIDTH, so `'x'` is an `int` unless the position expects a
`Character`. Every `insert`/`contains`/`remove` on a `Set with Character` therefore has to make the
literal adopt the key type, and no ported case exercises that door.
```maxon
function main() returns ExitCode
	var cs = CharSet.create()
	cs.insert('x')
	cs.insert('é')
	cs.insert('中')
	cs.insert('x')
	print("{cs.count()}\n")
	print("{cs.contains('x')}\n")
	print("{cs.contains('é')}\n")
	print("{cs.contains('y')}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
true
true
false
```

<!-- test: character-key-remove -->
### Removing a Character key drops it exactly once
```maxon
function main() returns ExitCode
	var cs = CharSet.create()
	cs.insert('a')
	cs.insert('🎉')
	print("{cs.remove('a')}\n")
	print("{cs.remove('a')}\n")
	print("{cs.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
false
1
```

<!-- test: supplementary-plane-category -->
### A codepoint above the BMP is classified through the sorted range table
The BMP table is indexed directly; everything above U+FFFF takes a binary search. No ported case
distinguishes the two — `string-trim/trim-end-emoji` only needs an emoji NOT to be whitespace, which a
search that always answered `Cn` would also give.
```maxon
function main() returns ExitCode
	let syms = CharacterSet.symbols()
	let letters = CharacterSet.letters()
	print("{syms.contains('🎉')}\n")
	print("{letters.contains('🎉')}\n")
	print("{letters.contains('𝐀')}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
false
true
```

<!-- test: member-set-moved-into-from -->
### `CharacterSet.from` CONSUMES its member set
The box becomes the set's sole owner, so a bare local that reaches `from` is moved out and may not be
read again. Without the poison the set would be freed twice.
```maxon
function main() returns ExitCode
	let members = CharSet from ['x']
	let cs = CharacterSet.from(members)
	print("{members.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:5:10: use of moved value 'members': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: member-set-dropped-unused -->
### A member set that never reaches a box is still dropped
```maxon
function main() returns ExitCode
	let orphan = CharSet from ['q', 'r']
	print("{orphan.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: character-set-dropped-unused -->
### A CharacterSet that is built and never used is still dropped
Both the box and the member set it owns; a leak here exits 101.
```maxon
function main() returns ExitCode
	let unused = CharacterSet.punctuation()
	let alsoUnused = CharacterSet.whitespacesAndNewlines()
	print("ok")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok
```

<!-- test: trim-result-discarded -->
### A discarded trim result is dropped at statement end
```maxon
function main() returns ExitCode
	let s = "  hi  "
	_ = s.trim()
	print("ok")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok
```

<!-- test: trim-in-a-loop -->
### A trim in a loop mints and drops a set and a Character per trip
Fifty trips, each building a fresh `CharacterSet` of seven members and minting one `Character` per
grapheme scanned. Anything held past its trip shows up as a leak.
```maxon
function main() returns ExitCode
	var i = 0
	var total = 0
	while i < 50 'loop'
		let padded = "  x  "
		let t = padded.trim()
		total = total + t.byteLength()
		i = i + 1
	end 'loop'
	print("{total}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
50
```

<!-- test: one-set-two-trims -->
### One CharacterSet serves two trims
The set is BORROWED by the scan, so trimming twice against one set neither frees it early nor twice.
```maxon
function main() returns ExitCode
	let d = CharacterSet.decimalDigits()
	let mixed = "12a34"
	let head = mixed.trimStart(d)
	let tail = mixed.trimEnd(d)
	print("[{head}][{tail}]")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[a34][12a]
```

<!-- test: trim-crlf-is-one-cluster -->
### CR+LF is ONE grapheme, so a set holding CR but not LF trims neither
UAX#29 GB3 joins CR+LF into a single cluster, which is why the trim walks clusters rather than bytes:
the cluster `"\r\n"` is not a member of a set seeded with a bare CR, so nothing is cut.
```maxon
function main() returns ExitCode
	let s = "\r\nx\r\n"
	let cr = CharacterSet.from(CharSet from ['\r'])
	let r = s.trim(cr)
	print("{r.byteLength()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: malformed-unicode-escape-in-a-string -->
### A malformed `\uNNNN` in a string is blamed at the escape
The ported `unicode-escape.invalid-too-few-digits` puts its escape at offset 0 of a character literal,
so it cannot tell a column that tracks the escape from one that always names the opening quote.
```maxon
function main() returns ExitCode
	let x = "ab\uZZ"
	return 0
end 'main'
```
```maxoncstderr
error E1004: <fragment>:3:12: Invalid unicode escape '\uZZ': expected 4 hex digits in string interpolation
```
