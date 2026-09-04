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
function fallthroughTrips(s String) returns Integer
	var n = 0
	for c in s 'a'
		n = n + 1 if c == c else n
	end 'a'
	return n
end 'fallthroughTrips'

function continueTrips(s String) returns Integer
	var n = 0
	for c in s 'b'
		n = n + 1 if c == c else n
		if n > 0 'always'
			continue
		end 'always'
	end 'b'
	return n
end 'continueTrips'

function breakTrips(s String) returns Integer
	var n = 0
	for c in s 'd'
		n = n + 1 if c == c else n
		if n == 2 'stop'
			break
		end 'stop'
	end 'd'
	return n
end 'breakTrips'

function returnFromLoop(s String) returns Integer
	var n = 0
	for c in s 'e'
		n = n + 1 if c == c else n
		if n == 3 'out'
			return n
		end 'out'
	end 'e'
	return n
end 'returnFromLoop'

function discardTrips(s String) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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
error E3005: specs/fragments/character-ownership/print-takes-a-string-not-a-character.test:4:2: argument type mismatch for 'value': expected 'String', got 'Character'
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
function reassignInLoop(s String) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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

Every character literal IS a `Character` (A5m-ab), so no position has to ask for one: `c == 'a'` inside
`for c in s`, `found = 'z'` into a `Character` var and `return 'a'` from `returns Character` are all the
literal's own type meeting itself. The case predates that ruling — it was written when a one-byte literal
was an `int` and each of these positions had to make the literal ADOPT the type — and it is kept exactly
as it was, because what it measures is the behaviour and not the route to it.

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

Its title survives the flip and its behaviour is unchanged, but the direction of the rule is now the
opposite one: the literal is a `Character` and the INTEGER position converts it to its codepoint
(`Parser.integerizedOperand`), where before the position had to be a Character one for anything to happen
at all. What must not move is the ANSWER, and `char-literal-to-int.md`'s codepoint arithmetic pins the
rest of it.

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

<!-- test: character-literal-at-a-call-argument -->
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

function build(s String) returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
1200
```

<!-- test: a-character-array-takes-a-one-byte-literal -->
### A ONE-BYTE character literal adopts `Character` at an array element too

`push` / `set` / `insert` take whatever the element slot declares, and a one-byte literal must not be a
different KIND of thing from a wider one. It nearly was: under the width rule that made `'e'` an `int` and
`'é'` a `Character`, `a.push('e')` was refused as *"cannot assign 'int' … of type 'Character'"* on a
program the oracle runs, and the cure was an adoption door asked before the slot's type was compared. Since
A5m-ab there is no width rule and no adoption to do — but the case is what would catch its return.

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
### The LEFT operand's literal is judged against the RIGHT one

A one-word transposition — the left operand asking its own tag instead of the other side's — left the whole
suite at **2113/0** when this was written, and the asymmetry it hides is still exactly as reachable after
A5m-ab: `integerizedOperand` is asked of both operands, each against what the OTHER is, and a left operand
compared with itself would answer about nothing.

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

<!-- test: a-character-literal-receiver-is-not-a-try-block-label -->
### `try 'A'.asciiValue()` is a call, not a block-form `try`

A block LABEL and a character literal are the same `TokenKind.charLiteral`; only grammatical position tells
them apart, and `tryOpensBlockAt` used to claim the two forms were disjoint on the token alone because *"no
call can begin with a charLiteral"*. A character literal with methods is exactly such a call, and the claim
became false: measured, this program was **E3059**, *"a block-form `try 'label' … end` groups statements and
yields no value"* — a diagnostic about a construct it does not contain. The disambiguator is the `.`: a block
label is always followed by a statement, so the lexer emits a `newline` behind it and never a dot.

```maxon
function main() returns ExitCode
	let val = try 'A'.asciiValue() otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
65
```

<!-- test: a-character-literal-fills-a-character-struct-field -->
### A character literal at a struct-literal field

`Self{ch: 'e'}` was `E3005 … cannot assign 'int' to variable 'Cell.ch' of type 'Character'` while the oracle
compiled and ran it (`PLAN.md`'s roster entry). It needs no roster entry now: the literal IS a `Character`,
so the field and the value are the same type without anything being asked.

```maxon
type Cell
	export var ch as Character

	static function create() returns Cell
		return Self{ch: 'e'}
	end 'create'
end 'Cell'

function main() returns ExitCode
	let c = Cell.create()
	print("{c.ch}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
e
```

<!-- test: a-character-binding-is-never-retyped-by-an-int -->
### Only a LONE LITERAL converts — a `Character` binding meeting an `int` is a type error

The coercion keys on the operand being written as a character literal, never on the two tags. Without that
guard `let c = 'a'` followed by `c == 45` would silently re-type the BINDING to 97 — a wrong answer with no
diagnostic anywhere — because a `let` binds the literal's own SSA value.

```maxon
function main() returns ExitCode
	let c = 'a'
	if c == 45 'oops'
		return 1
	end 'oops'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/character-ownership/a-character-binding-is-never-retyped-by-an-int.test:4:7: type mismatch: 'cannot compare Character with int'
```

<!-- test: a-module-level-character-initializer-is-not-a-constant -->
### A top-level `let` cannot hold a character

A module-scope binding folds to a compile-time constant and a `Character` is a heap-shaped record, so there
is nothing for the constant evaluator to fold — whatever the character's byte width. The one-byte case used
to fold to its byte, which it could only do while the literal was an `int`. Both reference compilers refuse
it under the same code and the same sentence.

```maxon
let DASH = '-'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/character-ownership/a-module-level-character-initializer-is-not-a-constant.test:2:12: Expected constant expression, got '-'
```

<!-- test: a-character-range-needs-both-bounds -->
### A range is a `Character` range only when BOTH bounds are character literals

`for c in 'a' to 'e'` iterates Characters; `for i in 'a' to n` is an INTEGER range starting at 97, because
one bound is a number and the range is therefore over numbers — the same rule that makes `cp == '-'` a
comparison of numbers. The element type follows the pair, never either half.

```maxon
function main() returns ExitCode
	for c in 'a' to 'e' 'chars'
		print("{c}")
	end 'chars'
	let stop = 99
	var total = 0
	for i in 'a' to stop 'codepoints'
		total = total + i
	end 'codepoints'
	print(" {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
abcde 294
```

<!-- test: a-wide-character-literal-converts-to-its-codepoint -->
### The conversion is to the CODEPOINT, not to a byte

`'é'` is two bytes and one codepoint, and an integer position wants the codepoint: 233, which is what the
oracle compares against too. It is what makes the rule a statement about characters rather than about the
one-byte case that happened to be an `int` before A5m-ab.

```maxon
function main() returns ExitCode
	var hits = 0
	let cp = 233
	if cp == 'é' 'accent'
		hits = hits + 1
	end 'accent'
	if '中' == 20013 'han'
		hits = hits + 1
	end 'han'
	let shifted = 'é' + 1
	print("{hits} {shifted}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 234
```

<!-- test: a-cluster-has-no-integer-reading -->
### A multi-codepoint cluster stays a `Character` in an integer position

A ZWJ family emoji is a SEQUENCE of codepoints, so there is no single number to convert it to — answering
with the FIRST would be a wrong number wearing a conversion's name. The literal stays a `Character` and the
operator raises its own type error.

```maxon
function main() returns ExitCode
	let cp = 128104
	if cp == '👨‍👩‍👧‍👦' 'family'
		return 1
	end 'family'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/character-ownership/a-cluster-has-no-integer-reading.test:4:8: type mismatch: 'cannot compare int with Character'
```

<!-- test: an-ascii-array-literal-is-a-character-array-too -->
### `['a', 'b']` infers the same element type `['é', 'ö']` does

The width rule made these two literals different kinds of array; they are one kind now, and shv2 refuses a
`Character` array in both. The case exists because the refusal moved: `['a', 'b']` used to be an
`Array with integer` and compiled.

```maxon
function main() returns ExitCode
	let a = ['a', 'b']
	print("{a.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/character-ownership/an-ascii-array-literal-is-a-character-array-too.test:3:11: Unsupported: an array literal element of type 'Character' — a literal's elements are an integer, a float, a bool, a String, a struct, or a boxed union (a bare `[…]` infers the type from the first element)
```
