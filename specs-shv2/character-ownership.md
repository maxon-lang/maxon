---
feature: character-ownership
status: experimental
keywords: [character, grapheme, for-in, ownership, drop, uax29]
category: types
---

## Documentation

# A `Character` is MINTED per trip, and dropped per trip

`for c in s` walks a `String`'s extended grapheme clusters (UAX #29). Unlike `for x in <Array>`,
where the element is a **borrow** the array still owns, a String's clusters are **stored nowhere** —
the record holds bytes, and a `Character` is a record — so each trip **allocates** one and this loop
is its only owner.

```text
for c in "café" 'each'      // 4 trips, 4 Characters minted, 4 dropped
    print("{c}")
end 'each'
```

Every path out of a trip drops it: the body's fall-through, `continue`, `break`, and a `return` out
of the loop. A leak on any of them fails the run with exit code 101.

A `Character` shares its 48-byte record with a `String`, but it is a **distinct type**: `s == c` is a
type error, `print(c)` is refused (it takes a `String`), and interpolation — `"{c}"` — is how a
Character reaches stdout.

## Tests

<!-- test: for-in-string-drops-every-cluster -->
### Every exit path out of a trip drops its cluster

Five loop shapes over a mixed-width string, repeated 200 times — 5,200 minted Characters. Any path
that fails to drop one fails the leak gate with exit 101, so a clean exit IS the assertion; the
`total` check keeps the loops from being trivially right for the wrong reason. Nothing is printed:
the point is the allocation ledger, not the text.

```maxon
function fallthroughTrips(s String) returns int
	var n = 0
	for c in s 'a'
		n = n + 1 if c == c else n
	end 'a'
	return n
end 'fallthroughTrips'

function continueTrips(s String) returns int
	var n = 0
	for c in s 'b'
		n = n + 1 if c == c else n
		if n > 0 'always'
			continue
		end 'always'
	end 'b'
	return n
end 'continueTrips'

function breakTrips(s String) returns int
	var n = 0
	for c in s 'd'
		n = n + 1 if c == c else n
		if n == 2 'stop'
			break
		end 'stop'
	end 'd'
	return n
end 'breakTrips'

function returnFromLoop(s String) returns int
	var n = 0
	for c in s 'e'
		n = n + 1 if c == c else n
		if n == 3 'out'
			return n
		end 'out'
	end 'e'
	return n
end 'returnFromLoop'

function discardTrips(s String) returns int
	var n = 0
	for _ in s 'f'
		n = n + 1
	end 'f'
	return n
end 'discardTrips'

function main() returns ExitCode
	let s = "aé中!xéy"
	var total = 0
	var i = 0
	while i < 200 'rep'
		total = total + fallthroughTrips(s) + continueTrips(s) + breakTrips(s) + returnFromLoop(s) + discardTrips(s)
		i = i + 1
	end 'rep'
	if total != 5200 'wrongTotal'
		return 1
	end 'wrongTotal'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: crlf-iterates-once -->
### CR LF is ONE cluster when iterated, as well as when counted

GB3 joins CR and LF. The reference compilers disagree with themselves here — `count()` answers 1 and
`for c in s` answers 2, because their iterator takes an ASCII shortcut that never consults GB3 — so
shv2 routes both through the real scan and this case pins that they agree.

```maxon
function main() returns ExitCode
	let s = "\r\n"
	var n = 0
	for _ in s 'each'
		n = n + 1
	end 'each'
	print("{s.count()} {n}\n")
	let mid = "ab\r\ncd"
	var m = 0
	for _ in mid 'more'
		m = m + 1
	end 'more'
	print("{mid.count()} {m}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
5 5
```

<!-- test: empty-string-iterates-zero-times -->
### An empty string has no clusters

```maxon
function main() returns ExitCode
	let s = ""
	var n = 0
	for _ in s 'each'
		n = n + 1
	end 'each'
	print("{s.count()} {n} {s.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 0 0
```

<!-- test: lone-combining-mark-is-one-cluster -->
### A string that is only a combining mark is one cluster

An `Extend` with no base never breaks from what precedes it — and at the start of a string there is
nothing to attach to, so it stands alone.

```maxon
function main() returns ExitCode
	let s = "́"
	print("{s.count()} {s.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: lone-zero-width-joiner-is-one-cluster -->
### A lone ZWJ is one cluster

```maxon
function main() returns ExitCode
	let s = "‍"
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: unpaired-regional-indicator-trails -->
### Regional indicators pair left to right, and a fifth stands alone

Five regional indicators are two flags and one unpaired indicator.

```maxon
function main() returns ExitCode
	let s = "🇺🇸🇬🇧🇫"
	var n = 0
	for _ in s 'each'
		n = n + 1
	end 'each'
	print("{s.count()} {n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3
```

<!-- test: four-byte-cluster-at-end -->
### A four-byte codepoint as the last cluster

The scan must not read past the buffer when the widest sequence sits at the very end.

```maxon
function main() returns ExitCode
	let s = "ab🎉"
	var n = 0
	for _ in s 'each'
		n = n + 1
	end 'each'
	print("{s.count()} {n} {s.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3 6
```

<!-- test: string-and-character-do-not-compare -->
### A String and a Character are different types

They share a record; they are not the same type. Comparing them is a type error, not a byte compare
that happens to succeed for a one-cluster String.

```maxon
function main() returns ExitCode
	let s = "é"
	let c = 'é'
	if s == c 'same'
		return 1
	end 'same'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/character-ownership/string-and-character-do-not-compare.test:5:7: type mismatch: 'cannot compare String with Character'
```

<!-- test: print-takes-a-string-not-a-character -->
### `print` takes a String; a Character reaches stdout through interpolation

```maxon
function main() returns ExitCode
	let c = '中'
	print(c)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/character-ownership/print-takes-a-string-not-a-character.test:4:2: 'print' requires a String, but its argument is Character
```

<!-- test: character-parameter-and-equality -->
### A `Character` is nameable as a declared type

```maxon
function isTarget(c Character, target Character) returns bool
	return c == target
end 'isTarget'

function main() returns ExitCode
	var hits = 0
	for c in "héllo wörld" 'each'
		if isTarget(c, target: 'ö') 'match'
			hits = hits + 1
		end 'match'
	end 'each'
	print("{hits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: grapheme-count-of-ascii-matches-byte-length -->
### Pure ASCII counts one cluster per byte

The ASCII fast path inside the segmenter must agree with the general scan.

```maxon
function main() returns ExitCode
	let s = "the quick brown fox"
	print("{s.count()} {s.byteLength()} {s.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
19 19 19
```

<!-- test: returned-character-is-dropped-by-the-caller -->
### A `returns Character` hands ownership to the caller, which drops it

A Character crossing a return is a heap record the callee moves out, so the CALLER becomes its sole
owner. Nothing in the corpus reached that path before this case: sabotaging the adoption test
(`valueIsManagedHeap`) left the whole suite green, which is what identified the hole.

```maxon
function litOnly() returns Character
	return 'é'
end 'litOnly'

function main() returns ExitCode
	var i = 0
	var n = 0
	while i < 50 'rep'
		let c = litOnly()
		n = n + 1 if c == c else n
		i = i + 1
	end 'rep'
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
50
```

<!-- test: loop-element-moves-into-an-outer-var -->
### Assigning the trip's cluster to an outer `var` MOVES it

`found = c` must move the minted cluster into `found` — dropping whatever `found` held — not copy it.
Before the element carried its owned-heap provenance, the reassignment saw a borrow, promoted a COPY
(a second allocation per trip) and left the original with no owner: exit 101.

```maxon
function reassignInLoop(s String) returns int
	var n = 0
	var found = 'é'
	for c in s 'each'
		found = c
		n = n + 1
	end 'each'
	n = n + 1 if found == found else n
	return n
end 'reassignInLoop'

function lastOf(s String) returns Character
	var found = 'é'
	for c in s 'each'
		found = c
	end 'each'
	return found
end 'lastOf'

function main() returns ExitCode
	var i = 0
	var t = 0
	let word = "héllö"
	while i < 50 'rep'
		t = t + reassignInLoop(word)
		print("{lastOf(word)}")
		i = i + 1
	end 'rep'
	print("\nt={t}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
öööööööööööööööööööööööööööööööööööööööööööööööööö
t=300
```

<!-- test: promoting-a-character-keeps-its-type -->
### A `var` Character stays a Character

A `var`'s managed initializer is promoted to an owned copy at its declaration, and the copy is built
by the interpolation encoder — whose result is a String. Keeping the source's type is what makes the
next line legal; before it did, `found` was typed `String` and the assignment was E3005.

```maxon
function main() returns ExitCode
	var found = 'é'
	for c in "ab中" 'each'
		found = c
	end 'each'
	print("{found} {found.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
中 3
```
