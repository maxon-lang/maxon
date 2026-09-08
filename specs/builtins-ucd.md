---
feature: builtins-ucd
status: stable
keywords: [builtins, __Builtins, unicode, ucd, general-category, intrinsics, parameter-labels]
category: system
---

# `__Builtins` UCD table loads

## Documentation

`__Builtins` is the compiler's builtin TYPE: a name reserved for the compiler, whose static
methods are INTRINSICS rather than functions any file declares. Two of them read the checked-in
Unicode Character Database tables that `stdlib/helpers/string/unicodeCategory.maxon` is written
against, and they are what this spec pins:

| Intrinsic | Table it reads | Meaning |
|---|---|---|
| `__Builtins.ucdByteAt("__ucd_bmp", offset: cp)` | `ucd_bmp.bin` | the `General_Category` byte of BMP codepoint `cp`, indexed DIRECTLY |
| `__Builtins.ucdI64At("__ucd_supp", index: i)` | `ucd_supp.bin` | packed supplementary-range entry `i` of a sorted array |

A supplementary entry packs three fields into one machine word: bits 0..20 are `rangeStart`,
bits 21..41 are `rangeEnd`, bits 42..46 are the category. 21 bits covers U+10FFFF exactly.

### Each intrinsic HAS A DECLARATION, and that is why the label rule applies to it

Maxon's call rule is universal: the first argument is positional and every later argument carries
its `name:` label (`E2052`/`E2053`). A label names a PARAMETER OF A DECLARATION — which is exactly
why the bare compiler builtins (`min`, `abs`, ...) are exempt from it (`E2067`): they have no
declaration, so there is no parameter for a label to name.

These two are NOT in that class. Each has a real declaration — two parameters, `label` and
`offset`/`index` — so the ordinary rule binds and the second argument MUST be labelled. The
declared name is the authority: `offset` for `ucdByteAt`, `index` for `ucdI64At`.

The first argument is the table LABEL and stays positional, which needs no exception: the first
argument of every call in the language is positional.

### The label is a COMPILE-TIME LITERAL naming ONE table, and that is a guard

The label selects a blob the compiler links into the program, so it is resolved while the call is
being parsed and never becomes a runtime value. Two rules follow, and both are refusals rather
than conventions:

- **It must be a string LITERAL.** A computed label could not be resolved at all.
- **It must name the table its intrinsic reads.** `ucdByteAt` reads `__ucd_bmp`, whose entries are
  one byte each; `ucdI64At` reads `__ucd_supp`, whose entries are eight. Reading either with the
  other's stride cannot produce an answer, only a wrong one — so the pairing is checked, not
  assumed (`E2070`).

An unchecked label was a PATH TRAVERSAL, not merely a wrong answer: the label used to be turned
into a file name and joined onto the stdlib directory, so `"__ucd_../../../x"` reached for a file
outside it.

## Tests

<!-- test: builtins-ucd.bmp-byte-load -->
`ucdByteAt` indexes the BMP table directly by codepoint and answers that codepoint's
`General_Category`: `A` is `Lu` (1), `z` is `Ll` (2), `5` is `Nd` (9) and a plain space is
`Zs` (23).
```maxon
function main() returns ExitCode
	var score = 0
	if __Builtins.ucdByteAt("__ucd_bmp", offset: 65) == 1 'upper'
		score = score + 1
	end 'upper'
	if __Builtins.ucdByteAt("__ucd_bmp", offset: 122) == 2 'lower'
		score = score + 1
	end 'lower'
	if __Builtins.ucdByteAt("__ucd_bmp", offset: 53) == 9 'digit'
		score = score + 1
	end 'digit'
	if __Builtins.ucdByteAt("__ucd_bmp", offset: 32) == 23 'space'
		score = score + 1
	end 'space'
	return score as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: builtins-ucd.bmp-byte-load-is-zero-extended -->
A table byte is UNSIGNED. A sign-extending load would answer a negative number for every category
byte above 127, and the whole BMP table is scanned here rather than one entry because which byte
carries a high bit is a property of the checked-in data, not of the program.
```maxon
function main() returns ExitCode
	var i = 0
	var negatives = 0
	while i < 65536 'scan'
		if __Builtins.ucdByteAt("__ucd_bmp", offset: i) < 0 'negative'
			negatives = negatives + 1
		end 'negative'
		i = i + 1
	end 'scan'
	if negatives == 0 'allUnsigned'
		return 5
	end 'allUnsigned'
	return 1
end 'main'
```
```exitcode
5
```

<!-- test: builtins-ucd.supp-i64-load -->
`ucdI64At` loads one packed supplementary entry, striding by eight bytes. Entry 0 is the first
supplementary range in the checked-in table: U+10000..U+1000B, category `Lo` (5).

⚠ **ENTRY 1 IS CHECKED TOO, AND THAT IS THE ONLY THING THAT PINS THE STRIDE.** Entry 0 sits at
offset 0 of the table whatever the stride is, so an assertion about it alone passes under a stride
of one byte — MEASURED, by sabotaging the stride to 1 and watching this case stay green. Entry 1 is
where a wrong stride reads a word straddling two entries, and the table's own sortedness
(`entry1.rangeStart > entry0.rangeEnd`) is asserted beside the exact fields so the case cannot pass
on a coincidence in either direction.
```maxon
function main() returns ExitCode
	let entry = __Builtins.ucdI64At("__ucd_supp", index: 0)
	let next = __Builtins.ucdI64At("__ucd_supp", index: 1)
	var score = 0
	if (entry and 2097151) == 65536 'rangeStart'
		score = score + 1
	end 'rangeStart'
	if ((entry shr 21) and 2097151) == 65547 'rangeEnd'
		score = score + 1
	end 'rangeEnd'
	if ((entry shr 42) and 31) == 5 'category'
		score = score + 1
	end 'category'
	if (next and 2097151) == 65549 'nextRangeStart'
		score = score + 1
	end 'nextRangeStart'
	if ((next shr 21) and 2097151) == 65574 'nextRangeEnd'
		score = score + 1
	end 'nextRangeEnd'
	if (next and 2097151) > ((entry shr 21) and 2097151) 'sortedAndDisjoint'
		score = score + 1
	end 'sortedAndDisjoint'
	return score as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: builtins-ucd.supp-binary-search -->
The two intrinsics together are the whole of `unicodeGeneralCategory`: a BMP codepoint is one
indexed byte, and a supplementary one is a binary search over the sorted range table. U+10400
(DESERET CAPITAL LETTER LONG I) is `Lu` (1); U+1F600 (GRINNING FACE) is `So` (22).

The index is spelled with a RANGED ALIAS, exactly as `stdlib/helpers/string/unicodeCategory.maxon`
spells it, because that is the shape a corpus caller actually writes — and a value typed by an
alias carries a different tag from a bare `int` until type resolution collapses it.
```maxon
typealias Codepoint = int(0 to 1114111)
typealias GeneralCategory = int(0 to 31)

function categoryOf(cp Codepoint) returns GeneralCategory
	if cp < 65536 'bmp'
		return __Builtins.ucdByteAt("__ucd_bmp", offset: cp)
	end 'bmp'
	var lo = 0
	var hi = 805
	while lo <= hi 'bsearch'
		let mid = (lo + hi) shr 1
		let entry = __Builtins.ucdI64At("__ucd_supp", index: mid)
		let rangeStart = entry and 2097151
		let rangeEnd = (entry shr 21) and 2097151
		if cp < rangeStart 'left'
			hi = mid - 1
		end 'left' else 'notLeft'
			if cp > rangeEnd 'right'
				lo = mid + 1
			end 'right' else 'found'
				return (entry shr 42) and 31
			end 'found'
		end 'notLeft'
	end 'bsearch'
	return 0
end 'categoryOf'

function main() returns ExitCode
	var score = 0
	if categoryOf(66560) == 1 'deseretIsUppercase'
		score = score + 1
	end 'deseretIsUppercase'
	if categoryOf(128512) == 22 'emojiIsOtherSymbol'
		score = score + 1
	end 'emojiIsOtherSymbol'
	if categoryOf(65) == 1 'bmpStillWorks'
		score = score + 1
	end 'bmpStillWorks'
	return score as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: builtins-ucd.offset-must-be-labelled -->
**The case this whole family exists for.** `ucdByteAt` has a declaration, so its second argument
is bound by the ordinary label rule and an unlabelled one is refused — the same `E2053` any
user-declared call gets, from the same door. It is NOT `E2067`: that code says a callee has no
parameters at all, which is false here.
```maxon
function main() returns ExitCode
	return __Builtins.ucdByteAt("__ucd_bmp", 65) as ExitCode
end 'main'
```
```maxoncstderr
error E2053: <fragment>:3:43: the second and later arguments must be named ('name: value')
```

<!-- test: builtins-ucd.index-must-be-labelled -->
The sibling door, refused identically. `ucdI64At` is the same shape one name over, and a rule
enforced at only the name that forced the question would leave the defect class alive inside its
own fix.
```maxon
function main() returns ExitCode
	return __Builtins.ucdI64At("__ucd_supp", 0) as ExitCode
end 'main'
```
```maxoncstderr
error E2053: <fragment>:3:43: the second and later arguments must be named ('name: value')
```

<!-- test: builtins-ucd.label-cannot-be-named -->
The FIRST argument stays positional, which is not an exception granted to these two: it is the
language's rule for the first argument of every call (`E2052`).
```maxon
function main() returns ExitCode
	return __Builtins.ucdByteAt(label: "__ucd_bmp", offset: 65) as ExitCode
end 'main'
```
```maxoncstderr
error E2052: <fragment>:3:30: the first argument cannot be named; only the second and later arguments take 'name:' labels
```

<!-- test: builtins-ucd.offset-label-must-be-the-declared-name -->
A label that matches no parameter of the declaration is refused with `E3037`, the same code a
user-declared call's unknown label gets. The declaration is the authority for what the name is.
```maxon
function main() returns ExitCode
	return __Builtins.ucdByteAt("__ucd_bmp", index: 65) as ExitCode
end 'main'
```
```maxoncstderr
error E3037: <fragment>:3:43: '__Builtins.ucdByteAt' has no parameter named 'index'
```

<!-- test: builtins-ucd.label-must-be-a-string-literal -->
The table label is resolved while the call is parsed, so it must be written as a literal. A
computed one is refused at the token, not accepted and mis-resolved later.
```maxon
function main() returns ExitCode
	let name = "__ucd_bmp"
	return __Builtins.ucdByteAt(name, offset: 65) as ExitCode
end 'main'
```
```maxoncstderr
error E2010: <fragment>:4:30: Expected 'string literal' but got 'name'
```

<!-- test: builtins-ucd.label-must-name-a-readable-table -->
A literal that names no table this intrinsic reads is refused with `E2070`. The label used to be
turned into a FILE NAME, so an unchecked one reached outside the stdlib directory entirely.
```maxon
function main() returns ExitCode
	return __Builtins.ucdByteAt("__ucd_../../../evil", offset: 65) as ExitCode
end 'main'
```
```maxoncstderr
error E2070: <fragment>:3:30: '__Builtins.ucdByteAt' reads the compiler-owned table '__ucd_bmp'; the label '__ucd_../../../evil' names no table it can read
```

<!-- test: builtins-ucd.tables-are-not-interchangeable -->
The pairing is checked in both directions. `ucdByteAt` strides by one byte and `ucdI64At` by
eight, so reading a table with the other's stride cannot produce an answer — only a wrong one.
```maxon
function main() returns ExitCode
	return __Builtins.ucdI64At("__ucd_bmp", index: 0) as ExitCode
end 'main'
```
```maxoncstderr
error E2070: <fragment>:3:29: '__Builtins.ucdI64At' reads the compiler-owned table '__ucd_supp'; the label '__ucd_bmp' names no table it can read
```

<!-- test: builtins-ucd.arity-is-exactly-two -->
Both take exactly two arguments. A call that stops after the label is refused at the `)` it found,
before anything tries to read a table — and that is the BOOTSTRAP's answer too, which is why it is
not an arity diagnostic: neither compiler has one for a builtin whose arguments it parses itself.
```maxon
function main() returns ExitCode
	return __Builtins.ucdByteAt("__ucd_bmp") as ExitCode
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:41: Expected ',' but got ')'
```
