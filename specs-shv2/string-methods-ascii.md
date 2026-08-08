---
feature: string-methods-ascii
status: experimental
keywords: [string, ascii, bytes, startsWith, endsWith, contains, toLower, toUpper, replace, replaceFirst, split]
category: types
---

# Byte / ASCII String methods

## Documentation

Seven `String` methods operate on BYTES and on the ASCII case ranges alone — they consult no Unicode
character database and perform no grapheme segmentation:

```text
startsWith(prefix String) returns bool
endsWith(suffix String) returns bool
contains(needle String) returns bool
toLower() returns String
toUpper() returns String
replace(old String, with String) returns String
split(delimiter String) returns Array with String
```

Two consequences follow from "bytes and ASCII only", and they are what these tests pin:

- **A byte outside `A`–`Z` / `a`–`z` is left alone by a case conversion.** `toLower`/`toUpper` gate on a
  byte RANGE, and every byte of a multi-byte UTF-8 sequence has its high bit set, so non-ASCII text passes
  through a case conversion unchanged rather than being corrupted.
- **Search, replace and split match BYTE sequences.** A multi-byte character is matched by its bytes, so
  they work on non-ASCII text without knowing anything about it.

`toLower`/`toUpper`/`replace` return a NEW `String` and `split` a NEW `Array with String` whose elements
are new `String`s; the receiver is never modified and never aliased by a result.

⚠ **ALL SEVEN ARE SERVED BY `stdlib/String.maxon`, NONE OF THEM IS SHV2's OWN ANY MORE, AND EVERY ANSWER
BELOW IS THE SAME AS IT WAS ON THE OTHER SIDE OF THAT LINE.** `startsWith`/`endsWith` went first (W49
wave 1), then `toLower`/`toUpper`/`replace`/`replaceFirst` (wave 2), then `contains` (wave 4), and `split`
last (wave 5). Each was struck from `Parser.stringSurfaceMemberNames`, which is the whole of what moved it:
an unrostered member of a byte record is put to the corpus, and the corpus already declared every one. This
file is where that is checked to have changed NOTHING a program can observe — which is the only claim a
retirement is allowed to make.

⚠ **`split` WAS HELD BACK THREE WAVES FOR ONE MEASUREMENT, AND THE CASES BELOW ARE WHERE IT WAS TAKEN.** It
is the only member of the seven that CONSTRUCTS a container: its synthesized arm decided an
`Array with String`'s element size and its `element_destroy@40` stamp through the compiler's own
array-creation door, and the open question was whether the corpus's `typealias StringArray = Array with
String` decides them the same way. It does — measured on the emitted x64, the same element size and the
same `__str_decref` stamp, from one interned instance rather than two — so `split-many-segments` (which
grows the result well past its initial capacity) and `unbound-results-do-not-leak` (which drops one
unnamed) are the two cases that would have caught a wrong stamp, in the two directions a wrong one fails:
a leak and a double free.

The one thing it genuinely does change is the OWNERSHIP ROUTE. A synthesized arm emits an inline runtime
call and borrows its argument; a corpus call goes through the ordinary call door, which is where a result
is minted, an owned temporary is enrolled and a consumed parameter is applied. Both end up borrowing
here — the caller drops the temporary after the call returns — but they arrive there by different code,
so the last four cases pin the shapes that could tell them apart: an argument that is an owned TEMPORARY
with nobody else to free it, an argument that is a live BINDING the caller uses again afterwards, and —
for the two-argument replacements, whose corpus bodies hold a live view of the receiver's bytes across an
append of the replacement — an argument that IS the receiver.

⚠⚠ **WAVE 2 IS ALSO WHERE A RETIREMENT FIRST REACHED A DOOR NOTHING HAD EVER WALKED THROUGH.** The corpus
`toLower`/`toUpper` are `mapAsciiCase`, which maps a private copy through `String.byteAt` and
`String.setByte`, and `setByte` had no other caller in the whole corpus. shv2 served it by writing through
`__str_bytes_view(self)` — a NON-OWNING view record — so the write copy-on-write-detached into a private
buffer that died with the temporary, and every case conversion in this file returned its receiver verbatim
the moment the retirement landed. The arm now hands the write the String's OWN record. That is the reason
`case-conversion-does-not-mutate-receiver` and `case-conversion-on-let-binding` (in `string-type-2.md`)
are not redundant with the cases here: they ask about the RECEIVER, and these ask about the RESULT.

## Tests

<!-- test: case-conversion-leaves-non-ascii-bytes-untouched -->
### ASCII case ranges only — a non-ASCII byte passes through unchanged
`É` and `ß` are outside `A`–`Z` and `a`–`z`, so neither conversion touches them, and neither result
changes length. Uppercasing `ß` to `SS` would need the Unicode character database, which these methods
deliberately do not consult.
```maxon
function main() returns ExitCode
	let s = "CAFÉ Straße"
	let lowered = s.toLower()
	let uppered = s.toUpper()
	print("{lowered}\n")
	print("{uppered}\n")
	print("{s}\n")
	print("{s.byteLength()}\n")
	print("{lowered.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
cafÉ straße
CAFÉ STRAßE
CAFÉ Straße
13
13
```

<!-- test: search-and-split-match-byte-sequences -->
### Search, replace and split match a multi-byte character by its bytes
```maxon
function main() returns ExitCode
	let s = "CAFÉ Straße"
	if s.contains("É") 'c1'
		print("contains\n")
	end 'c1'
	if s.startsWith("CAFÉ") 'c2'
		print("starts\n")
	end 'c2'
	if s.endsWith("ße") 'c3'
		print("ends\n")
	end 'c3'
	print("{s.replace("É", with: "E")}\n")
	let parts = s.split("É")
	print("{parts.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
contains
starts
ends
CAFE Straße
2
```

<!-- test: predicate-argument-longer-than-the-receiver -->
### An argument longer than the receiver is false, not an out-of-bounds read
The length check comes first in all three predicates; an empty argument matches everywhere.
```maxon
function main() returns ExitCode
	let s = "hi"
	if s.startsWith("hello") 'a'
		print("badStart\n")
	end 'a'
	if s.endsWith("hello") 'b'
		print("badEnd\n")
	end 'b'
	if s.contains("hello") 'c'
		print("badContains\n")
	end 'c'
	if s.startsWith("") 'd'
		print("emptyPrefix\n")
	end 'd'
	if s.endsWith("") 'e'
		print("emptySuffix\n")
	end 'e'
	if s.contains("") 'f'
		print("emptyNeedle\n")
	end 'f'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
emptyPrefix
emptySuffix
emptyNeedle
```

<!-- test: empty-receiver-for-every-byte-method -->
### Every byte method on an empty receiver
```maxon
function main() returns ExitCode
	let e = ""
	if e.startsWith("x") 'a'
		print("bad1\n")
	end 'a'
	if e.endsWith("x") 'b'
		print("bad2\n")
	end 'b'
	if e.contains("x") 'c'
		print("bad3\n")
	end 'c'
	if e.startsWith("") 'd'
		print("ok1\n")
	end 'd'
	print("[{e.toLower()}][{e.toUpper()}][{e.replace("x", with: "y")}]\n")
	let parts = e.split("x")
	print("{parts.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok1
[][][]
1
```

<!-- test: replace-shorter-longer-and-whole -->
### A replacement shorter than, longer than, or equal to the receiver
The result is sized from the match count before a byte is written, so growth and shrinkage are the same
path; the receiver is unchanged by any of them.
```maxon
function main() returns ExitCode
	let s = "a.b.c"
	print("{s.replace(".", with: "-->")}\n")
	print("{s.replace(".", with: "")}\n")
	print("{s.replace("a.b.c", with: "z")}\n")
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a-->b-->c
abc
z
a.b.c
```

<!-- test: adjacent-and-overlapping-matches-count-once -->
### A match is consumed WHOLE, and the sizing pass must agree with the building pass
`replace` runs two passes over the same matches: one COUNTS them to size a single exactly-fitting
allocation, the other re-walks them and writes. Both advance the cursor PAST the whole match, so
`"aaaa".replace("aa", …)` is two matches and not three. ⚠ **Nothing but this agreement bounds the write** —
if the counting pass ever stepped differently from the building pass the result would be sized for one
number of matches and filled for another, which is a heap overrun or an under-filled buffer, not a crash.
MEASURED: stepping the count pass by 1 instead of by the needle's length wrote 2 bytes into a 1-byte
allocation and surfaced only as a one-character stdout diff. A replacement LONGER than the needle is what
makes the divergence unmistakable here, because the two passes then disagree about the SIZE and not only
about the count. `split` walks the same matches by the same rule, so `"aaa".split("aa")` is two parts.
Expected output is the bootstrap oracle's.
```maxon
function main() returns ExitCode
	let a = "aaaa"
	print("[{a.replace("aa", with: "LONG")}]\n")
	let b = "aaaaa"
	print("[{b.replace("aa", with: "xyz")}]\n")
	let c = "aaaaaa"
	print("[{c.replace("aaa", with: "-")}]\n")
	let d = "abababa"
	print("[{d.replace("aba", with: "Q")}]\n")
	let e = "aaa"
	let p = e.split("aa")
	print("{p.count()}\n")
	for z in p 'l'
		print("[{z}]")
	end 'l'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[LONGLONG]
[xyzxyza]
[--]
[QbQ]
2
[][a]
```

<!-- test: split-many-segments -->
### A split that grows the result array well past its initial capacity
```maxon
function main() returns ExitCode
	let s = "a,b,c,d,e,f,g,h,i,j,k,l,m,n,o,p,q,r,s,t,u,v,w,x,y,z"
	let parts = s.split(",")
	print("{parts.count()}\n")
	for p in parts 'loop'
		print(p)
	end 'loop'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
26
abcdefghijklmnopqrstuvwxyz
```

<!-- test: split-of-a-grown-receiver -->
### A receiver whose bytes have moved to a detached buffer still splits
`append` detaches a String's bytes into a separate allocation once they outgrow the record; the split
reads the current buffer, not the one the String was born with.
```maxon
function main() returns ExitCode
	let a = "one"
	let b = "two"
	let t = "{a},{b},{a}"
	let parts = t.split(",")
	print("{parts.count()}\n")
	for p in parts 'loop'
		print("[{p}]")
	end 'loop'
	print("\n")
	var acc = ""
	acc.append("x,y")
	let grown = acc.split(",")
	print("{grown.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
[one][two][one]
2
```

<!-- test: unbound-results-do-not-leak -->
### Results nothing binds are dropped at the end of their statement
Every String-returning method mints a fresh owned allocation, so a result used straight inside an
interpolation — or several in one statement, or thousands around a loop — must be freed without ever
having been bound to a name. The leak check runs at exit, so a missed drop is a non-zero exit code.
```maxon
function shout(text String) returns String
	return text.toUpper()
end 'shout'

function firstWord(line String) returns String
	let parts = line.split(" ")
	return try parts.first() otherwise "?"
end 'firstWord'

function main() returns ExitCode
	print("{shout("quiet")}\n")
	print("{firstWord("alpha beta gamma")}\n")
	let s = "Hello"
	print("{s.replace("l", with: "L")}{s.toUpper()}\n")
	var i = 0
	var total = 0
	while i < 200 'loop'
		let line = "k{i},v{i}"
		let parts = line.split(",")
		total = total + parts.count()
		let r = line.replace(",", with: ";")
		if r.contains(";") 'has'
			total = total + 1
		end 'has'
		i = i + 1
	end 'loop'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
QUIET
alpha
HeLLoHELLO
600
```

<!-- test: byte-methods-on-a-top-level-managed-binding -->
### A module-scope `let` / `var` String is a receiver like any other
```maxon
let Greeting = "Hello, World"
var Mutable = "a-b-c"

function main() returns ExitCode
	print("{Greeting.toLower()}\n")
	let parts = Greeting.split(", ")
	print("{parts.count()}\n")
	print("{Mutable.replace("-", with: "+")}\n")
	if Greeting.startsWith("Hello") 'a'
		print("ok\n")
	end 'a'
	for p in Greeting.split(", ") 'loop'
		print("[{p}]")
	end 'loop'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello, world
2
a+b+c
ok
[Hello][World]
```

<!-- test: affix-predicate-argument-is-an-owned-temporary -->
### An affix argument nobody else owns is freed exactly once
`makeAffix` returns a freshly built `String`, so the value handed to `startsWith` / `endsWith` is an
owned temporary with no binding behind it. Whoever the predicate is served by, the temporary must be
released once — a second owner leaks it and a double drop frees the caller's bytes underneath it. The
suite fails the case outright if either happens, because a leak is an exit code and not a printed
difference.
```maxon
function makeAffix(head bool) returns String
	var s = "af"
	s.append("fix" if head else "ter")
	return s
end 'makeAffix'

function main() returns ExitCode
	let s = "affix-in-the-middle-after"
	if s.startsWith(makeAffix(true)) 'a'
		print("start\n")
	end 'a'
	if s.endsWith(makeAffix(false)) 'b'
		print("end\n")
	end 'b'
	if s.startsWith(makeAffix(false)) 'c'
		print("badStart\n")
	end 'c'
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
start
end
25
```

<!-- test: affix-predicate-argument-is-a-live-binding -->
### An affix argument that is a live binding survives the call
The mirror of the case above: the argument is NAMED, so the predicate must borrow it and leave the
caller's ownership alone. Every use after the call is what proves it — the binding is read again, passed
again, and finally printed.
```maxon
function main() returns ExitCode
	let s = "prefix-body-suffix"
	let p = "prefix"
	if s.startsWith(p) 'a'
		print("first\n")
	end 'a'
	if s.startsWith(p) 'b'
		print("second\n")
	end 'b'
	print("{p.byteLength()}\n")
	print("[{p}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first
second
6
[prefix]
```

<!-- test: replacement-arguments-in-both-ownership-shapes -->
### A replacement's two arguments, once as owned temporaries and once as live bindings
`replace`/`replaceFirst` are the two-argument members, so a retirement puts TWO values through the call
door per call instead of one. `piece` returns a freshly built `String`, so the first pair are owned
temporaries with no binding behind them — a second owner leaks one (an exit code, not a printed
difference) and a double drop frees bytes the builder is still copying. The second pair are live
bindings, read and printed after both calls, which is what proves the corpus body BORROWED them: the
receiver is a `let` throughout, so nothing here may write through anything either.

The receiver is read again between the calls, because the corpus `replace` holds a live view of the
receiver's own bytes (`addressableBytes()`) across every append into its builder.
```maxon
typealias Tag = int(0 to 9)

function piece(n Tag) returns String
	var s = "p"
	s.append("{n}")
	return s
end 'piece'

function main() returns ExitCode
	let s = "p1-p2-p1"
	print("[{s.replace(piece(1), with: piece(9))}]\n")
	print("[{s.replaceFirst(piece(2), with: piece(8))}]\n")
	print("[{s}]\n")
	let needle = "p1"
	let wide = "WIDE"
	print("[{s.replace(needle, with: wide)}]\n")
	print("[{s.replaceFirst(needle, with: wide)}]\n")
	print("{needle.byteLength()}{wide.byteLength()}\n")
	print("[{needle}][{wide}]\n")
	var grown = "MiXeD"
	grown.append("-CaSe-TAIL")
	print("[{grown.toLower()}][{grown.toUpper()}][{grown}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[p9-p2-p9]
[p1-p8-p1]
[p1-p2-p1]
[WIDE-p2-WIDE]
[WIDE-p2-p1]
24
[p1][WIDE]
[mixed-case-tail][MIXED-CASE-TAIL][MiXeD-CaSe-TAIL]
```

<!-- test: a-replacement-argument-that-is-the-receiver -->
### A needle or a replacement that IS the receiver
The corpus `replace` takes `addressableBytes()` of the receiver ONCE, before its loop, and then appends
`with` into a builder inside it. When `with` is the receiver, that append reads the very bytes the live
view is over while the builder reallocates around them — so this is the one argument shape where a
borrow that was really a move, or a view that outlived its String, shows up as wrong OUTPUT rather than
only as a leak. All four spellings are here: needle-is-receiver (a whole-string match), replacement-is-
receiver, both, and the two degenerate answers (`clone`) an empty or absent needle takes.
```maxon
function main() returns ExitCode
	let s = "aXbXc"
	print("[{s.replace(s, with: s)}]\n")
	print("[{s.replace("X", with: s)}]\n")
	print("[{s.replaceFirst("X", with: s)}]\n")
	print("[{s.replace("", with: "Q")}]\n")
	print("[{s.replaceFirst("zz", with: "Q")}]\n")
	print("[{s.replace("aXbXc", with: "")}]\n")
	print("[{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[aXbXc]
[aaXbXcbaXbXcc]
[aaXbXcbXc]
[aXbXc]
[aXbXc]
[]
[aXbXc]
```
