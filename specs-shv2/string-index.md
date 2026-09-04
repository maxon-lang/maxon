---
feature: string-index
status: experimental
keywords: [string, stringindex, findFirst, findLast, slice, grapheme, startIndex, endIndex]
category: types
---

# StringIndex — positions, searches and slicing

## Documentation

Five `String` methods traffic in a `StringIndex`, a position that carries BOTH a grapheme index and a
byte position:

```text
startIndex() returns StringIndex
endIndex() returns StringIndex
findFirst(needle String) returns StringIndex throws StringError
findLast(needle String) returns StringIndex throws StringError
slice(start StringIndex, endIndex StringIndex) returns String
slice(start StringIndex, length GraphemeIndex) returns String
```

and the index itself answers two accessors, `charIndex()` and `bytePos()`.

Four properties are what these tests pin, and every one of them was measured against the runnable
reference compiler before it was written down:

- **`charIndex()` is a GRAPHEME index, `bytePos()` is a byte offset, and they are different numbers.**
  A search is a BYTE search; the grapheme index is derived from the byte position it lands on by walking
  the UAX #29 segmenter from the start of the string. For ASCII text the two numbers coincide, which is
  exactly why a non-ASCII case is needed to tell a correct conversion from an omitted one.
- **`slice` is END-EXCLUSIVE.** `s.slice(a, endIndex: b)` contains the bytes `[a.bytePos(), b.bytePos())`,
  so `s.slice(i, endIndex: i)` is empty.
- **`slice(start, length: n)` counts GRAPHEME CLUSTERS, not bytes** — the second overload is chosen by its
  argument LABEL, not by the argument's type.
- **A slice OWNS its bytes.** It is a copy, not a view into the receiver, so it outlives its parent and
  neither one's drop can reach the other's buffer.

A search that finds nothing THROWS `StringError.notFound`. An EMPTY needle is not a miss: the forward
search finds it at position 0 and the backward search at the end of the string, since the empty string
occurs at every position.

A `slice` whose byte range is not a range of the receiver — inverted, negative, or past its byte length —
ABORTS the process with exit code 77. It is reachable from source (an index taken from a LONGER string is
a perfectly good index of that string), and the reference dies on the same input, through a `panic` in
`String.sliceBytes`.

## Tests

<!-- test: grapheme-index-is-not-a-byte-index -->
### `charIndex()` counts CLUSTERS where `bytePos()` counts BYTES
`"aéb👨‍👩‍👧cd"` is six clusters — the ZWJ family is ONE — spanning 24 bytes. `cd` starts at cluster 4 and
byte 22, and a conversion that returned the byte position would answer 22 for both.
```maxon
function main() returns ExitCode
	let uni = "aéb👨‍👩‍👧cd"
	let cd = try uni.findFirst("cd") otherwise uni.endIndex()
	print("{cd.charIndex()} {cd.bytePos()} {uni.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 22 6
```

<!-- test: ascii-index-and-byte-position-coincide -->
### On ASCII text the two numbers are the same
Which is why every other case here uses non-ASCII text to distinguish them.
```maxon
function main() returns ExitCode
	let alpha = "abcdefghijklm"
	let ghij = try alpha.findFirst("ghij") otherwise alpha.endIndex()
	print("{ghij.charIndex()} {ghij.bytePos()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6 6
```

<!-- test: find-last-on-non-ascii-text -->
### The BACKWARD search derives its grapheme index the same way
`findFirst` and `findLast` share one conversion, and this is the case that says so: sabotaging that
conversion to return the byte position turned exactly ONE test red before this case existed — the
forward one — because every ported `find-last-*` case is pure ASCII, where the two numbers coincide. The
slice between the two hits is the third reader of the same pair.
```maxon
function main() returns ExitCode
	let uni = "aéb👨‍👩‍👧cdéb"
	let lastB = try uni.findLast("b") otherwise uni.startIndex()
	print("{lastB.charIndex()} {lastB.bytePos()}\n")
	let firstB = try uni.findFirst("b") otherwise uni.endIndex()
	print("{firstB.charIndex()} {firstB.bytePos()}\n")
	let tail = uni.slice(firstB, endIndex: lastB)
	print("{tail} {tail.byteLength()} {tail.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 26
2 3
b👨‍👩‍👧cdé 23 5
```

<!-- test: find-first-empty-needle-does-not-throw -->
### An empty needle is found at position 0
Every string contains the empty string at every position, so the FIRST such position is the start. It is
not a miss and it does not throw.
```maxon
function main() returns ExitCode
	let alpha = "abcdefghijklm"
	let hit = try alpha.findFirst("") otherwise alpha.endIndex()
	print("{hit.charIndex()} {hit.bytePos()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 0
```

<!-- test: find-last-empty-needle-is-the-end -->
### An empty needle's LAST occurrence is the end of the string
The mirror of the case above, and the reference's own answer: `findLastIn` returns the byte length for an
empty needle.
```maxon
function main() returns ExitCode
	let s = "abc"
	let hit = try s.findLast("") otherwise s.startIndex()
	print("{hit.charIndex()} {hit.bytePos()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3
```

<!-- test: find-first-whole-string -->
### A needle equal to the whole string is found at 0
```maxon
function main() returns ExitCode
	let alpha = "abcdefghijklm"
	let hit = try alpha.findFirst("abcdefghijklm") otherwise alpha.endIndex()
	print("{hit.charIndex()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: search-in-an-empty-string -->
### An empty haystack: the empty needle hits, anything else throws
```maxon
function main() returns ExitCode
	let e = ""
	let hit = try e.findFirst("") otherwise e.endIndex()
	print("{hit.charIndex()} {hit.bytePos()}\n")
	let miss = try e.findFirst("x") otherwise e.endIndex()
	print("{miss.charIndex()} {miss.bytePos()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 0
0 0
```

<!-- test: slice-is-end-exclusive -->
### `slice(a, endIndex: b)` stops one byte before `b`
`world` starts at byte 6 and `require` at byte 35, so the slice between them is 29 bytes — 35 − 6, not
36 − 6.
```maxon
function main() returns ExitCode
	let long = "hello world that is long enough to require heap allocation"
	let at = try long.findFirst("world") otherwise long.endIndex()
	let upTo = try long.findFirst("require") otherwise long.endIndex()
	print("{at.bytePos()}..{upTo.bytePos()}\n")
	let sub = long.slice(at, endIndex: upTo)
	print("{sub.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6..35
29
```

<!-- test: slice-by-length-counts-clusters -->
### `slice(start, length: n)` advances by CLUSTERS
Three clusters of `"aéb👨‍👩‍👧cd"` is `aéb` (4 bytes, because `é` is two); four is that plus the whole
18-byte family. A byte count would have cut the family in half.
```maxon
function main() returns ExitCode
	let uni = "aéb👨‍👩‍👧cd"
	let start = uni.startIndex()
	let three = uni.slice(start, length: 3)
	print("{three} {three.byteLength()}\n")
	let four = uni.slice(start, length: 4)
	print("{four} {four.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
aéb 4
aéb👨‍👩‍👧 22
```

<!-- test: slice-by-length-clamps-past-the-end -->
### A `length:` past the end of the string stops at the end
The walk runs out of string before it runs out of count, which is the reference's own bound
(`graphemeOffsetToBytePos` tests both).
```maxon
function main() returns ExitCode
	let s = "abc"
	let whole = s.slice(s.startIndex(), length: 99)
	print("{whole} {whole.byteLength()}\n")
	let none = s.slice(s.startIndex(), length: 0)
	print("[{none}] {none.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
abc 3
[] 0
```

<!-- test: index-equality-and-ordering-read-the-grapheme-index -->
### `==` and `<` compare the grapheme index
The two operators the reference's `Equatable`/`Comparable` conformances give a `StringIndex`, both over
`charIdx` alone.
```maxon
function main() returns ExitCode
	let s = "abcdefghijklm"
	let start = s.startIndex()
	let g = try s.findFirst("ghij") otherwise s.endIndex()
	if start < g 'ordered'
		print("lt\n")
	end 'ordered' else 'notOrdered'
		print("NOT lt\n")
	end 'notOrdered'
	if start == s.startIndex() 'same'
		print("eq\n")
	end 'same' else 'different'
		print("NOT eq\n")
	end 'different'
	if g == start 'wrong'
		print("BAD eq\n")
	end 'wrong' else 'right'
		print("ne\n")
	end 'right'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
lt
eq
ne
```

<!-- test: a-missed-search-takes-the-otherwise-value -->
### A miss throws, so `otherwise` supplies the index
```maxon
function main() returns ExitCode
	let s = "hello world"
	let missed = try s.findFirst("zzz") otherwise s.endIndex()
	if missed == s.endIndex() 'threw'
		print("threw\n")
	end 'threw' else 'found'
		print("DID NOT throw\n")
	end 'found'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
threw
```

<!-- test: a-slice-is-an-owned-copy -->
### A slice feeds `append` and slices again, and nothing is aliased
The first slice is an unnamed owned temporary consumed by `append`; the second is sliced again. If a slice
were a view of the receiver rather than a copy, one of the three drops here would reach another's bytes.
```maxon
function main() returns ExitCode
	let s = "hello world"
	let idx = try s.findFirst(" ") otherwise s.endIndex()
	var acc = "["
	acc.append(s.slice(s.startIndex(), endIndex: idx))
	acc.append("]")
	print("{acc}\n")
	let inner = s.slice(s.startIndex(), endIndex: idx)
	let innerEnd = try inner.findFirst("llo") otherwise inner.endIndex()
	let nested = inner.slice(inner.startIndex(), endIndex: innerEnd)
	print("{nested}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello]
he
```

<!-- test: indices-and-slices-in-a-loop-do-not-leak -->
### 500 trips, each minting two index boxes and a slice
Every trip allocates and every allocation must be reclaimed; a leak is exit 101 rather than a wrong
answer, so only a loop with enough trips to be unmistakable can pin it.
```maxon
function main() returns ExitCode
	var total = 0
	var trips = 0
	while trips < 500 'loop'
		let s = "hello world"
		let idx = try s.findFirst("world") otherwise s.endIndex()
		total = total + idx.charIndex()
		let part = s.slice(s.startIndex(), endIndex: idx)
		total = total + (part.byteLength() as GraphemeIndex)
		trips = trips + 1
	end 'loop'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6000
```

<!-- test: an-inverted-slice-range-aborts -->
### An inverted range ends the process
`end` before `start` would ask for a negative number of bytes.

⚠ **THIS CASE PINNED A DIVERGENCE FROM THE REFERENCE UNTIL W49 WAVE 4, AND THE DIVERGENCE IS GONE.** It
read *"the reference panics on the same program; shv2 aborts with the exit code its runtime reserves for
it"* — exit **77**, from the SYNTHESIZED `slice` arm's own bounds check. `slice` is `stdlib/String.maxon`'s
now, so the guard that answers is the corpus's own `sliceBytes` precondition (`:336`), which is the guard
BOTH reference compilers execute. Verified against the bootstrap on this exact program: same exit code,
same message, same stack. The expectation below is therefore the ORACLE's, not a new shv2 behaviour.
```maxon
function main() returns ExitCode
	let s = "hello world"
	let a = try s.findFirst("world") otherwise s.endIndex()
	let bad = s.slice(a, endIndex: s.startIndex())
	print("{bad}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at String.maxon:378: String.sliceBytes: caller guarantees 0 <= start <= end <= byteLength()
Stack trace:
  in String.sliceBytes
  in String.slice
  in main
  in mrt_start
```

<!-- test: an-index-from-a-longer-string-aborts -->
### An index of ANOTHER string is out of range here
An index is an ordinary value and nothing ties it to the string it came from, so this is reachable from
source rather than hypothetical: byte 20 of a 24-byte string is past the end of a 2-byte one, and slicing
to it would read out of the buffer. Its twin above records why the guard that answers is now the corpus's,
and that this is the ORACLE's exit code rather than a new shv2 behaviour.
```maxon
function main() returns ExitCode
	let long = "hello world that is long"
	let short = "hi"
	let far = try long.findFirst("long") otherwise long.endIndex()
	let oob = short.slice(short.startIndex(), endIndex: far)
	print("{oob}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at String.maxon:378: String.sliceBytes: caller guarantees 0 <= start <= end <= byteLength()
Stack trace:
  in String.sliceBytes
  in String.slice
  in main
  in mrt_start
```

<!-- test: an-index-inside-a-type-method -->
### The whole family works inside a `type`'s METHOD BODY
⚠ **THIS CASE OUTLIVED THE DEFECT IT WAS WRITTEN FOR, AND IT IS KEPT.** While shv2 registered its own
`StringIndex` layout, a token SWEEP decided per file whether to register it — and the declaration walk it
rode hands a whole `type` (method bodies included) to `recordScannedType` and resumes past its `end`. So
the four producers were invisible in exactly the place a String helper type puts them, the layout went
unregistered, and `head` took the compiler down at `stringIndexLayoutOrPanic` while `width` took it down
at `managedNameDropCallee`. **W49 wave 3 removed the per-file decision rather than fixing it**: the type is
`stdlib/String.maxon`'s declaration, folded in every program, so there is nothing left to miss. The case
still pins that the family composes inside a method, which is a different question and still worth asking.
```maxon
type Label
	var text as String

	export static function create(t String) returns Label
		return Label{text: t}
	end 'create'

	export function head() returns String
		return text.slice(text.startIndex(), length: 2)
	end 'head'

	export function width() returns Integer
		let last = text.endIndex()
		return last.charIndex()
	end 'width'

	export function upTo(needle String) returns String
		let hit = try text.findFirst(needle) otherwise text.endIndex()
		return text.slice(text.startIndex(), endIndex: hit)
	end 'upTo'
end 'Label'

function main() returns ExitCode
	var l = Label.create("aébcdé")
	let needle = "cd"
	print("{l.head()} {l.width()} {l.upTo(needle)}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
aé 6 aéb
```

<!-- test: char-at-and-index-after-walk-the-clusters -->
### `charAt` and `indexAfter` walk a string one CLUSTER at a time
⭐ **`charAt` IS THE CORPUS'S AND SO IS EVERY PRODUCER IT IS FED FROM (W49 wave 3).** shv2 never had a
`charAt` arm — it needs `makeCharFromBytes`, which the `__ManagedMemory` surface does not serve — and the
one it could not have was the one the corpus already declares (`stdlib/String.maxon:396-402`). What kept
it out of reach was not the missing arm: `startIndex()` minted a box of the compiler's OWN
`__StringIndex` type while the corpus's `charAt` declares its parameter `StringIndex`, so the call was
`E3005 argument type mismatch for 'idx': expected 'StringIndex', got '__StringIndex'` — two declarations
of one type, and no program could hold a value satisfying both. Retiring the five producers onto the
corpus leaves ONE declaration, and `charAt` resolves against it with nothing added anywhere.
⚠ The three interpolations are three `print`s rather than one, and that is E5001 rather than style: this
loop's working set does not fit the register file as one statement (`needs 5 more register(s)`), and it
does not at the merge base either — MEASURED both ways on the identical program, so the deficit is the
allocator's standing limit and not something this retirement moved.
```maxon
function main() returns ExitCode
	let uni = "aéb👨‍👩‍👧cd"
	var i = uni.startIndex()
	var n = 0
	while n < 6 'walk'
		print(uni.charAt(i).toString())
		print(" {i.charIndex()}")
		print(",{i.bytePos()}\n")
		i = try uni.indexAfter(i) otherwise uni.endIndex()
		n = n + 1
	end 'walk'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a 0,0
é 1,1
b 2,3
👨‍👩‍👧 3,4
c 4,22
d 5,23
```

<!-- test: index-before-steps-back-one-cluster -->
### `indexBefore` steps BACKWARD, and it is the corpus's too
`indexBefore` is the one member of this family that needs a BACKWARD segmenter (`findGraphemeStart`), and
the roster's own header spent three revisions calling that the reason it was absent. It never was:
`stdlib/helpers/string/grapheme.maxon` has had `findGraphemeStart` all along, and what actually blocked
the call was the same two-declaration mismatch `charAt` hit. One index at a time from the END, over the
same six clusters, so a backward step that answered a byte position where a grapheme index belongs would
disagree with the forward walk above at the ZWJ family.
```maxon
function main() returns ExitCode
	let uni = "aéb👨‍👩‍👧cd"
	var back = uni.endIndex()
	var m = 0
	while m < 3 'walkBack'
		back = try uni.indexBefore(back) otherwise uni.startIndex()
		print("{back.charIndex()}")
		print(",{back.bytePos()}\n")
		m = m + 1
	end 'walkBack'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5,23
4,22
3,4
```

<!-- test: error.a-user-may-not-declare-the-string-index-type -->
### `StringIndex` is a name the compiler owns
⭐ **THE RESERVATION MOVED WITH THE TYPE, AND WITHOUT IT THE RETIREMENT WOULD HAVE OPENED A HOLE.** While
the layout was the compiler's own, it was registered under the RESERVED spelling `__StringIndex`
precisely so that a user's `type StringIndex` could not land in the same bucket and have `slice` read a
box through the user's field offsets. The corpus's declaration is the only one now, under the BARE name —
so the reservation has to be the ordinary one every other corpus-declared builtin name carries
(`String`, `Character`, `Ordering`): admitted from `stdlib/`, refused from a user file. MEASURED on the
tree before this rung: this program COMPILED.
```maxon
type StringIndex
	var a as Integer
end 'StringIndex'

function main() returns ExitCode
	print("{"hi".byteLength()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:2:6: Unsupported: a declaration of the type name 'StringIndex', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and shv2 has no namespace to tell a user declaration of the name apart from that one
```

<!-- test: error.slice-needs-an-index-not-an-integer -->
### `slice`'s start must be a `StringIndex`
⚠ **THE SENTENCE IS THE ORACLE'S SINCE W49 WAVE 4.** It was `'slice' requires a StringIndex, but its
argument is int` — the SYNTHESIZED arm's bespoke wording, from a hand-written type test. The member is
`stdlib/String.maxon:486`'s now, so the refusal is the ordinary declared-parameter check, which is the
sentence the bootstrap prints for this program verbatim.
```maxon
function main() returns ExitCode
	let s = "hello"
	let sub = s.slice(0, endIndex: s.endIndex())
	print("{sub}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:14: argument type mismatch for 'start': expected 'StringIndex', got 'int'
```

<!-- test: error.slice-rejects-an-unknown-argument-label -->
### `slice`'s second argument is `endIndex:` or `length:`
⚠ **W49 WAVE 4 MOVED THIS FROM A BESPOKE REFUSAL TO THE ORDINARY ONE.** The synthesized arm read the
label off a token and named both spellings itself. With `slice` retired the call matches NEITHER corpus
overload, so `resolveOverloadedCalls` leaves the op alone (its documented step 5) and `checkCalls` reports
against the first-declared member's parameter list. The bootstrap answers `E3007 No overload of
'stdlib.String.slice' matches the named arguments`; shv2's E3037 names the offending label instead. Both
refuse the program at the same token — the code and the noun differ, which is the same latitude
`overloadTypeSuffix` records for a declaration-site-vs-call-site split.
```maxon
function main() returns ExitCode
	let s = "hello"
	let sub = s.slice(s.startIndex(), ending: s.endIndex())
	print("{sub}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3037: <fragment>:4:36: 'String.slice' has no parameter named 'ending'
```

<!-- test: error.a-string-index-has-two-methods -->
### A `StringIndex` answers `charIndex` and `bytePos`
```maxon
function main() returns ExitCode
	let s = "hello"
	let i = s.startIndex()
	print("{i.offset()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:12: Unsupported: `StringIndex` member 'offset' — shv2 provides charIndex/bytePos; that list IS the surface, so nothing else is served here
```

### A throwing search written without `try` names the search

<!-- test: error.find-first-without-try -->

⭐ **THE SAME E3057 RULE, AND THE NOUN NOW COMES FROM THE DECLARATION ITSELF.** `findFirst` throws
`StringError.notFound`, so a bare call reads only the value register and takes a miss for an answer.

⚠ **THE SENTENCE SAID `'findFirst'` UNTIL W49 WAVE 3 AND SAYS `'String.findFirst'` NOW, WHICH IS THE SAME
FIX ARRIVING FOR FREE.** D12 had to build a map (`stringIndexSourceMethodName`) translating the runtime
callee `__strix_first` back into the spelling the author wrote — without it this read *"throwing function
requires try: 'throwing array accessor'"*. `findFirst` is `stdlib/String.maxon:476`'s ordinary declared
method now, so the diagnostic reads the callee's REAL name and the map is gone: one fewer list to drift,
and the qualified form matches `int.fromString`'s in `parsable-interface.md`.
```maxon
function main() returns ExitCode
	let s = "a b"
	_ = s.findFirst(" ")
	return 0
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/string-index/error.find-first-without-try.test:4:8: throwing function requires try: 'stdlib.String.findFirst'
```
