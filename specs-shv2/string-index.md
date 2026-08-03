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
slice(start StringIndex, length GraphemeCount) returns String
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
		total = total + part.byteLength()
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
`end` before `start` would ask for a negative number of bytes. The reference panics on the same program;
shv2 aborts with the exit code its runtime reserves for it.
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
77
```

<!-- test: an-index-from-a-longer-string-aborts -->
### An index of ANOTHER string is out of range here
An index is an ordinary value and nothing ties it to the string it came from, so this is reachable from
source rather than hypothetical: byte 20 of a 24-byte string is past the end of a 2-byte one, and slicing
to it would read out of the buffer.
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
77
```

<!-- test: an-index-inside-a-type-method -->
### The whole family works inside a `type`'s METHOD BODY
The declaration sweep that registers the compiler-owned `StringIndex` layout walks TOKENS, and the
declaration walk it used to ride hands a whole `type` — method bodies included — to
`recordScannedType` and resumes past its `end`. So the four producers were invisible in exactly the
place a String helper type puts them, and the layout went unregistered: `head` took the compiler down
at `stringIndexLayoutOrPanic` (the field read) and `width` took it down at `managedNameDropCallee`
(the scope-exit drop of a bound index). Both crash sites are here, because they are two different
readers of the one missing entry.
```maxon
type Label
	var text as String

	export static function create(t String) returns Label
		return Label{text: t}
	end 'create'

	export function head() returns String
		return text.slice(text.startIndex(), length: 2)
	end 'head'

	export function width() returns int
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
```
```exitcode
0
```
```stdout
aé 6 aéb
```

<!-- test: error.slice-needs-an-index-not-an-integer -->
### `slice`'s start must be a `StringIndex`
```maxon
function main() returns ExitCode
	let s = "hello"
	let sub = s.slice(0, endIndex: s.endIndex())
	print("{sub}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:20: 'slice' requires a __StringIndex, but its argument is int
```

<!-- test: error.slice-rejects-an-unknown-argument-label -->
### `slice`'s second argument is `endIndex:` or `length:`
```maxon
function main() returns ExitCode
	let s = "hello"
	let sub = s.slice(s.startIndex(), ending: s.endIndex())
	print("{sub}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:36: Unsupported: argument label 'ending' on `slice` — a `String`'s slice takes `endIndex:` (a second `StringIndex`, exclusive) or `length:` (a grapheme count)
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
error E2015: <fragment>:5:12: Unsupported: `__StringIndex` member 'offset' — shv2 provides charIndex/bytePos; that list IS the surface, so nothing else is served here
```

### A throwing search written without `try` names the search

<!-- test: error.find-first-without-try -->

⭐ **THE SAME E3057 RULE, THE `StringIndex` FAMILY'S NOUN (D12).** `findFirst` throws `StringError.notFound`
through the dual-register `errorReturn` ABI, so a bare call reads only the value register and takes a miss
for an answer. The author wrote `findFirst`, not `__strix_first`, and the sentence says so — before D12 it
called their search a "throwing array accessor".
```maxon
function main() returns ExitCode
	let s = "a b"
	_ = s.findFirst(" ")
	return 0
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/string-index/error.find-first-without-try.test:4:6: throwing function requires try: 'findFirst'
```
