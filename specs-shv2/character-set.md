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

⚠ **WHERE TO PERFORM THE TWO UCD SABOTAGES HAS MOVED, AND THEIR RED COUNTS ARE DATED (W129).** Both were
measured against the compiler's own `__ucd_cat` — a synthesized second transcription of the lookup, with its
own binary search and its own `UcdUnassignedCategory` constant. `W115` listed `stdlib/CharacterSet.maxon` and
`W129` deleted that transcription, so the ONE implementation is now
`stdlib/helpers/string/unicodeCategory.maxon:53-75`, reached through the two surviving raw table loads
(`__ucd_bmp_at`/`__ucd_supp_at`). Sabotage it THERE; the counts below have not been re-measured at the new
site.

| Sabotage | Reds in the ported corpus alone | With this file | Covered by |
|---|---|---|---|
| `__str_trim`'s all-matched arm reopened to the byte length | 2 — both `trim()` | 4 | `trim-start-all-match`, `trim-end-all-match` |
| a `Set` key argument no longer adopts the set's key type | **0** | 2 | `character-key-insert-contains`, `character-key-remove` |
| `Character` withdrawn from the `Set` key-type gate | **0** | 3 | `character-set-create` (+ the two above) |
| the supplementary-plane binary search removed | **0** | 2 | `supplementary-plane-category`, `supplementary-plane-trim` |
| a bare-local member set not CO-OWNED when `from` consumes it | **0** | 2 | `member-set-co-owned-by-from`, `one-member-set-fills-two-character-sets` |
| `availableUnicodeEscapeText`'s UNTRUNCATED window (`available = whole`) | **0** | 1 | `malformed-escape-full-window` |
| the `Cn` fall-out for a codepoint in NO supplementary range reading any other category | **0** | 1 | `supplementary-plane-table-bounds` |
| `CharSet`/`CharacterSet` withdrawn from `isCompilerOwnedTypeName` | **0** | 2 | `charset-alias-is-compiler-owned`, `characterset-name-is-compiler-owned` |
| the const evaluator's `CharacterSet.<name>(` arm restored ahead of `atConstInitializerCall` (W115 review) | **0** | 2 | `error.characterset-undeclared-static-at-a-top-level-initializer`, `error.characterset-from-at-a-top-level-initializer` |

Two sabotages the ported corpus already covered well, recorded so the next reader does not re-run them:
a member set never enrolled as an owned temporary is **31** red, and explicit membership never winning
over the category mask is **3** red (`trim-tabs-and-newlines`, `trim-mixed-whitespace`,
`unicode-category/custom-set-unchanged` — exactly the seeds no category bit covers).

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

<!-- test: supplementary-plane-trim -->
### A trim driven by a supplementary-plane category
The direct `contains` above and this reach the sorted table by different routes — one from the parser's
own `__cs_contains` call, one from inside the trim's cluster walk — so a search broken in either
direction is caught twice rather than once.
```maxon
function main() returns ExitCode
	let s = "🎉🎉ok🎉"
	let r = s.trim(CharacterSet.symbols())
	print("[{r}]")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[ok]
```

<!-- test: supplementary-plane-table-bounds -->
### The sorted table's TOP, and the `Cn` a codepoint in no range falls out to
`supplementary-plane-category` finds both its codepoints in the table's MIDDLE, so nothing above
reaches the last entry and nothing at all reaches the fall-out — a lookup that answered any
other category for an unmatched supplementary codepoint left the suite at 2230/0 (measured against the
since-deleted `__ucd_cat`, whose `UcdUnassignedCategory` was moved 0 ⇒ 2; the live implementation is
`stdlib/helpers/string/unicodeCategory.maxon:53-75`). The three here are, in order: inside the highest range
any preset's mask covers (U+E0100, `Mn`, entry 803 of 806); one byte past that range's end, so the search
falls out between two entries; and above EVERY range (U+10FFFF), which is the only input that drives `lo` to
the table's last index and is therefore what an off-by-one in the search's initial high bound would read past.
```maxon
function main() returns ExitCode
	let letters = CharacterSet.letters()
	print("{letters.contains('󠄀')}\n")
	print("{letters.contains('󠇰')}\n")
	print("{letters.contains('􏿿')}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
false
false
```

<!-- test: member-set-co-owned-by-from -->
### `CharacterSet.from` CO-OWNS its member set
`from` consumes its argument, and a consuming position is a DURABLE SINK: the set takes its own reference
to the member box (⚖ 2026-08-12) rather than stealing the caller's. `members` therefore stays readable and
releases its own reference at scope exit, so the box is freed once — exactly the two references, exactly
two drops. (It used to be a MOVE, and `members.count()` here was E3102.)
```maxon
function main() returns ExitCode
	let members = CharSet from ['x']
	_ = CharacterSet.from(members)
	print("{members.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: one-member-set-fills-two-character-sets -->
### One member set can fill two CharacterSets
The refcount consequence of the case above, and the reason it is not merely a relaxation: EACH `from`
takes its own reference, so the box ends at three (two sets plus `members`) and is released three times.
Under the old move rule the second `from` was E3102, because a single transferred `+1` could not answer
for two owners.
```maxon
function main() returns ExitCode
	let members = CharSet from ['x']
	let first = CharacterSet.from(members)
	let second = CharacterSet.from(members)
	print("{first.contains('x')}{second.contains('x')}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
truetrue
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
	_ = CharacterSet.punctuation()
	_ = CharacterSet.whitespacesAndNewlines()
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

<!-- test: malformed-escape-full-window -->
### A malformed `\uNNNN` quotes SIX bytes when six are there
`availableUnicodeEscapeText` has two arms — the escape runs off the end of the literal body, or it does
not — and every other malformed-escape case in the corpus takes the TRUNCATED one (`'\u00'` and
`"ab\uZZ"` each have fewer than six bytes left at the backslash), so the arm that quotes the full
`\u` + four is unreached. Sabotaging it alone (`available = whole` ⇒ `available = 0`) left the whole
suite at 2230/0. Here the escape has a byte after it, so the window is complete and the truncation
never fires.
```maxon
function main() returns ExitCode
	let x = "z\uZZZZ!"
	return 0
end 'main'
```
```maxoncstderr
error E1004: <fragment>:3:11: Invalid unicode escape '\uZZZZ': expected 4 hex digits in string interpolation
```

<!-- test: charset-alias-is-compiler-owned -->
### A user declaration of `CharSet` is refused AT THE DECLARATION
This slice introduced two user-visible compiler-owned type names, and
`registerCharacterSetType`'s `genericAliases.upsert` is unconditional — so before `CharSet` joined
`TypeResolution.isCompilerOwnedTypeName`, a user declaration of the name was not refused, it was
silently OVERWRITTEN, and the author was blamed at the first USE of the name the compiler had taken:
*"`Set with Character` requires a Character key — got a 'String' value"*, reported on line 5 for a
declaration written on line 1. The rule the author broke is stated on line 1.
```maxon
typealias CharSet = Set with String

function main() returns ExitCode
	var s = CharSet.create()
	s.insert("a")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:11: Unsupported: a declaration of the type name 'CharSet', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and shv2 has no namespace to tell a user declaration of the name apart from that one
```

<!-- test: characterset-name-is-compiler-owned -->
### And so is one of `CharacterSet`, through the SAME derivation
The two names are not two checks: both are rows of `isCompilerOwnedTypeName`, which
`requireTypeNameNotCompilerOwned` asks at the four declaration forms that mint a nominal identity
(`type`, `enum`/`union`, a function typealias, a generic-instance typealias). Withdrawing the two rows
turns exactly these two cases red, which is what says they share one derivation rather than agreeing by
coincidence. A RANGED `typealias CharSet = int(0 to 5)` stays legal, as it does for `Ordering` and
`ExitCode` — it mints no identity, and a ranged reference and a `CharSet from […]` read different
registries.
```maxon
type CharacterSet
	export var x as int
end 'CharacterSet'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:6: Unsupported: a declaration of the type name 'CharacterSet', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and shv2 has no namespace to tell a user declaration of the name apart from that one
```

### A `CharacterSet` an aggregate OWNS is dropped by that aggregate's cascade

<!-- test: characterset-at-a-struct-field -->
### A `CharacterSet` at a struct field

⭐⭐ **WHAT A DESTRUCTOR CASCADE CALLS IS INVISIBLE TO THE MODULE SCAN.** `scanRuntimeUsage` walks the Maxon
module and turns on a runtime bit per callee it SEES; a `__destruct_<T>` body is synthesized later, so every
callee inside one has to be DECLARED instead (`MmRuntime.declareDestructorCascadeNeeds`).

⚠ **THE DEFECT THIS CASE WAS WRITTEN AGAINST IS HISTORY AND THE CLASS IS NOT (W129).** `CharacterSet` used
to be the one managed type whose drop had its own install bit (`usesCharSetDecref`) rather than riding a
family bit its own construction already turned on — `CharacterSet.whitespaces()` set `usesCharSetMake` and
nothing else — so a set reached ONLY through a cascade linked against a `__cs_decref` nothing installed:
`panic at X64Backend.maxon: resolveCallFixups: call to unknown function '__cs_decref'`, on a program with no
diagnostic to its name. `W115` made the type corpus-declared and `W129` deleted the `__cs_*` runtime with all
three of its bits, so the drop here is now an ordinary synthesized cascade. **The case still earns its place:
it is the struct-field shape the class needs a witness at, and the next per-type install bit reopens it.**
(The source-side statement of the same fact lives at `MmRuntime.noteDestructorUsage`'s header.)
```maxon
type Trimmer
	var chars as CharacterSet

	export static function init(cs CharacterSet) returns Self
		return Self{chars: cs}
	end 'init'
end 'Trimmer'

function main() returns ExitCode
	_ = Trimmer.init(CharacterSet.whitespaces())
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: characterset-at-a-union-payload -->
### A `CharacterSet` at a union payload

The union arm of the same declaration, and it was missing for the same reason: the arm asked
`unionHasManagedField(wantStringOnly: true)`, which is a question about `String` and about nothing else, so
a payload of any other managed type contributed no bit at all. Both arms now read the drop CALLEE the
cascade will emit and route it through the one callee-to-bit map, so neither can answer for a type the
other knows about.
```maxon
union Slot
	empty
	filled(chars CharacterSet)
end 'Slot'

function main() returns ExitCode
	let s = Slot.filled(CharacterSet.whitespaces())
	print("{s}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
filled

```

<!-- test: characterset-at-a-generic-instance-field -->
### A `CharacterSet` substituted into a generic instance's field

The third arm, and the one that shows the three were one defect rather than three: an instance's cascade is
synthesized by the same machinery and its needs were declared from `genericInstanceHasStringField`, the
`String`-only question again.
```maxon
type Box uses T
	var v as T

	export static function init(x T) returns Self
		return Self{v: x}
	end 'init'
end 'Box'

typealias CharacterSetBox = Box with CharacterSet

function main() returns ExitCode
	_ = CharacterSetBox.init(CharacterSet.whitespaces())
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.characterset-undeclared-static-at-a-top-level-initializer -->
### Error: an undeclared `CharacterSet` static at a TOP-LEVEL initializer

⭐ **THE TOP-LEVEL DOOR MUST NOT KEEP A ROSTER OF ITS OWN (W115 review).** Until this case, the constant
evaluator claimed `CharacterSet.<name>(` before `atConstInitializerCall` could and folded the eleven presets
out of `characterSetPresets`, in the since-deleted `Runtime/CharacterSetRuntime.maxon` (W129) — the
compiler's own transcription of what
`stdlib/CharacterSet.maxon` declares. Listing that module made the fold unreachable (the declaration wins at
`atConstInitializerCall`, which is asked first) and left only its refusal, which went on asserting *"a global
`CharacterSet` is one of the eleven predefined sets, whose members the compiler holds as data"* — a claim the
retirement had deleted, and a more confident sentence than the true one the same name gets inside a function.

⇒ what a retired builtin's undeclared static earns here is the sentence EVERY declared type's undeclared
static earns. Restore the arm and this case reads the old roster claim instead.
```maxon
let bogus = CharacterSet.inverted()

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:13: Unsupported: `CharacterSet.inverted` in a constant initializer — a constant is folded before any code runs, so it can name another top-level `let`, a literal, an empty container, a `create()`-style factory at the TOP of an initializer, or a sized type's `.min`/`.max`, and nothing else
```

<!-- test: error.characterset-from-at-a-top-level-initializer -->
### Error: `CharacterSet.from` — a KEYWORD-named static — at a TOP-LEVEL initializer

⚠ **THIS PINS A PROPERTY OF `atConstInitializerCall`, NOT OF THIS TYPE.** That predicate tests the member
position for `TokenKind.identifier`, and `from` lexes as `TokenKind.from` — so a declared static whose name is
a keyword is invisible to it and falls to the scalar walk, however ordinary its declaration. `CharacterSet.from`
is the one such static the corpus ships, which makes it the witness. Widening the member test to
`tokenCanBeAName` (D8: after a `.`, a keyword IS a name) would admit this form and move this case; that is a
decision to take deliberately, and this case is what makes it visible rather than silent.
```maxon
let custom = CharacterSet.from(CharSet from ['a', 'e'])

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:14: Unsupported: `CharacterSet.from` in a constant initializer — a constant is folded before any code runs, so it can name another top-level `let`, a literal, an empty container, a `create()`-style factory at the TOP of an initializer, or a sized type's `.min`/`.max`, and nothing else
```
