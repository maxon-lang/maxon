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

<!-- test: character-literal-adopts-the-character-type -->
### A character literal in a Character-expecting position IS a Character

shv2 types a character literal by its BYTE WIDTH — one byte is an `int`, wider is a `Character` — which
is what keeps `cp == '-'` and `cp - '0'` meaning what they say. The gap that leaves is the other
direction: `c == 'a'` inside `for c in s`, the very next thing anyone writes after the loop. A literal
in a position that unambiguously expects a Character now adopts that type, on both sides of a
comparison, on an assignment's right-hand side, and at a `return`.

```maxon
function firstAscii() returns Character
	return 'a'
end 'firstAscii'

function main() returns ExitCode
	var n = 0
	for c in "banana" 'scan'
		if c == 'a' 'hit'
			n = n + 1
		end 'hit'
	end 'scan'

	var m = 0
	for c in "banana" 'scan2'
		if c != 'a' 'miss'
			m = m + 1
		end 'miss'
	end 'scan2'

	var w = 0
	for c in "héllö théré" 'scan3'
		if 'é' == c 'lhs'
			w = w + 1
		end 'lhs'
	end 'scan3'

	var found = 'é'
	found = 'z'
	print("{n} {m} {w} {found} {firstAscii()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3 3 z a
```

<!-- test: an-int-position-keeps-its-integer-literal -->
### A character literal meeting an `int` is still an int

The rule is keyed on the position expecting a Character, never on the two tags — so nothing about
`char-literal-to-int.md`'s codepoint arithmetic moves.

```maxon
function main() returns ExitCode
	let cp = 45
	var hits = 0
	if cp == '-' 'dash'
		hits = hits + 1
	end 'dash'
	if 'A' == 'A' 'bothLiterals'
		hits = hits + 1
	end 'bothLiterals'
	let digit = 53 - '0'
	print("{hits} {digit}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 5
```

<!-- disabled-test: character-literal-at-a-call-argument -->
<!-- A CHARACTER LITERAL AT A `Character` PARAMETER. The other three Character-expecting positions (comparison, assignment, `return`) adopt the literal's type; a call ARGUMENT cannot yet, and the blocker is measured: the parse-time whole-program index records parameter NAMES but no parameter TYPES (`mixDeclaration` hashes a declaration's name and RETURN type only), so at the moment an argument is parsed there is nothing to ask what the parameter expects. Measured: `isTarget(c, target: 'a')` is `E3005: argument type mismatch for 'target': expected 'Character', got 'int'`, while the same call with a multi-byte literal (`target: 'ö'`) compiles — see `character-parameter-and-equality`. Closing it means giving `ProgramSignatures` a per-parameter fact, which is a whole-program query sitting under a per-file one (cost is O(functions x params) x files) whose hash keys every parse memo -->
### Character literal at a call argument

```maxon
function isTarget(c Character, target Character) returns bool
	return c == target
end 'isTarget'

function main() returns ExitCode
	var hits = 0
	for c in "banana" 'each'
		if isTarget(c, target: 'a') 'match'
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
3
```

<!-- test: iterating-a-string-locks-its-source -->
### `for c in s` locks `s`, exactly as the array form locks its array

The loop evaluates its source ONCE into the preheader and re-reads `length@8` / `buffer@0` off that
record every trip. Rebinding the variable inside the body decrefs the record the loop is still
walking — it compiled clean and faulted with **0xC0000005** before the String form took the same
`lockIterationSource` the array form has always taken. The runnable oracle reports E2013 here too.

```maxon
function main() returns ExitCode
	var s = "abcdefgh"
	var n = 0
	for c in s 'each'
		n = n + 1 if c == c else n
		if n == 2 'swap'
			s = "z"
		end 'swap'
	end 'each'
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/character-ownership/iterating-a-string-locks-its-source.test:8:4: cannot assign to immutable variable: 's'
```

<!-- test: mutating-a-string-being-iterated-is-refused -->
### A method that mutates the iterated String is refused too

The other half of the same lock, and it is the half the missing lock was believed to buy. The oracle
refuses this program with **E3019**, so keeping it legal was a divergence, not a feature.

```maxon
function main() returns ExitCode
	var s = "ab"
	let t = "c"
	var n = 0
	for c in s 'each'
		n = n + 1 if c == c else n
		s.append(t)
	end 'each'
	s = "done"
	print("{n} {s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3019: specs/fragments/character-ownership/mutating-a-string-being-iterated-is-refused.test:8:5: cannot pass 's' to function that mutates parameter 'self' (in main)
```

<!-- test: a-character-array-takes-a-borrowed-literal -->
### An `Array with Character` accepts a literal, exactly as an `Array with String` does

A move into durable storage picks its protocol off the element's RECORD, not off its type: a borrowed
byte record is COPIED into a fresh owned one. Asked as `tagIsText`, a Character element took the
aggregate arm and `a.push('é')` was refused as *"a struct/union has no `clone`"* — on a value whose
clone is `__str_clone`, and while the identical literal into a struct FIELD was accepted.

```maxon
typealias CharArray = Array with Character

function build(s String) returns int
	var a = CharArray.create()
	a.push('é')
	for c in s 'each'
		a.push(c)
	end 'each'
	return a.count()
end 'build'

function main() returns ExitCode
	var i = 0
	var t = 0
	while i < 200 'rep'
		t = t + build("héllö")
		i = i + 1
	end 'rep'
	print("{t}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1200
```

<!-- test: a-character-array-takes-a-one-byte-literal -->
### A ONE-BYTE character literal adopts `Character` at an array element too

`push` / `set` / `insert` are Character-expecting positions like any other, so the width rule that
makes `'e'` an `int` and `'é'` a `Character` must not decide what an `Array with Character` accepts.
It nearly did: when the element-argument type check landed, `a.push('e')` was refused as
*"cannot assign 'int' … of type 'Character'"* on a program the oracle runs — the adoption door
(`characterizedOperand`) has to be asked before the slot's type is compared, exactly as at a
comparison, an assignment and a `return`.

```maxon
typealias Count = int(0 to 1000)
typealias CharArray = Array with Character

function build() returns Count
	var a = CharArray.create()
	a.push('e')
	a.insert(0, value: 'z')
	try a.set(1, value: 'q') otherwise panic("test invariant: set OOB")
	return a.count()
end 'build'

function main() returns ExitCode
	print("{build()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: panic-takes-a-string-not-a-character -->
### `panic` takes a string literal, and a Character is not one

The near-twin of `print-takes-a-string-not-a-character`. ⚠ **The two doors ask DIFFERENT questions and
this case no longer pins `tagIsText`** — `print` does (and that case still carries the whole
thirteen-site sabotage argument), but `panic`'s argument must be a string **literal**, not merely a
value of string type, because its message is baked into `.rdata` at parse time along with the file and
line the runtime prints. A `'中'` is a character-literal token, so it is refused at the literal door
before any type tag is consulted — which is why the message names the rule that actually rejected it.

```maxon
function main() returns ExitCode
	let c = '中'
	panic(c)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/character-ownership/panic-takes-a-string-not-a-character.test:4:8: 'panic' requires a string literal, but its argument is Character
```

<!-- test: an-array-literal-element-is-not-a-character -->
### An array literal infers no element type from a Character

The third type-side site sabotage left green, and the one whose wrong answer is SILENT: with
`tagIsText` answering for a Character, `['é']` infers `Array with String` and stores Characters in it.

```maxon
function main() returns ExitCode
	let a = ['é', 'ö']
	print("{a.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/character-ownership/an-array-literal-element-is-not-a-character.test:3:11: Unsupported: an array literal element of type 'Character' — a literal's elements are an integer, a float, a bool, a String, a struct, or a boxed union (a bare `[…]` infers the type from the first element)
```

<!-- test: a-single-byte-literal-adopts-on-the-left-too -->
### The LEFT operand's literal adopts the type as well

`character-literal-adopts-the-character-type`'s `'é' == c` case cannot pin this: a MULTI-byte literal is
already a `Character`, so it takes `characterizedOperand`'s early return and never reaches the
materialization. Only a SINGLE-byte literal on the left does — and with the left operand asking its own
tag instead of the other side's (a one-word transposition), the whole suite stayed **2113/0**.

```maxon
function main() returns ExitCode
	var n = 0
	for c in "banana" 'scan'
		if 'a' == c 'hit'
			n = n + 1
		end 'hit'
	end 'scan'
	var m = 0
	for c in "banana" 'scan2'
		if 'a' != c 'miss'
			m = m + 1
		end 'miss'
	end 'scan2'
	print("{n} {m}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3
```
