---
feature: string-type-2
status: experimental
keywords: [string, sso, utf8, cow]
category: types
---

# String Type (Part 2)

Continuation of [string-type](string-type.md): heap-string access, memory-tracking,
grapheme/codepoint iteration, slicing, clone/COW, and `String.append`. Split from the
original 77-fragment spec so each batch stays under the per-worker test timeout.

## Tests

<!-- test: heap-string-data-access -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	// Verify heap-allocated string data is accessible via bytes()
	let s = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
	// Read first byte ('A' = 65)
	var first_printed = false
	for b in s.bytes() 'read_first'
		if not first_printed 'print_first'
			print("{b}\n")
			first_printed = true
		end 'print_first'
	end 'read_first'
	// Read last byte ('Z' = 90)
	var last_byte = 0 as Byte
	for b in s.bytes() 'read_all'
		last_byte = b
	end 'read_all'
	print("{last_byte}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
65
90
```

<!-- test: heap-string-equality -->
```maxon
function main() returns ExitCode
	let a = "This string is definitely longer than fifteen bytes"
	let b = "This string is definitely longer than fifteen bytes"
	if a == b 'check'
		print("1\n")
	end 'check' else 'not_equal'
		print("0\n")
	end 'not_equal'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: heap-string-inequality -->
```maxon
function main() returns ExitCode
	let a = "This string is definitely longer than fifteen bytes"
	let b = "This string is definitely longer than fifteen chars"
	if a != b 'check'
		print("1\n")
	end 'check' else 'are_equal'
		print("0\n")
	end 'are_equal'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: reassigned-var-equality-not-const-folded -->
A `var` declared with a compile-time string constant (`""`) and then reassigned
to a runtime value must compare by content, not against its stale declaration-
time constant. Regression: the TypeResolution string-const specializer aliased
the slot to its declaration id and never invalidated it on reassignment, so
`v == "_"` folded to `"" == "_"` (false) at compile time. A self-compiled
compiler inherited the miscompile in `parseForStatement` (`var iterName = "";
iterName = nameToken.value; iterName == "_"`), taking the non-discard branch for
every `for _` loop and spuriously firing E3012 on the stdlib's discard loops.
```maxon
function pick(useUnderscore bool) returns String
	if useUnderscore 'u'
		return "_"
	end 'u'
	return "x"
end 'pick'

function main() returns ExitCode
	var v = ""
	v = pick(true)
	if v == "_" 'match'
		print("1\n")
	end 'match' else 'no'
		print("0\n")
	end 'no'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: heap-string-iteration -->
```maxon
function main() returns ExitCode
	let s = "ABCDEFGHIJKLMNOP"  // 16 bytes, triggers heap
	var sum = 0
	// Iterate over bytes directly to test heap string iteration
	for b in s.bytes() 'loop'
		sum = sum + b
	end 'loop'
	print("{sum}\n")  // 65+66+...+80 = 1160
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1160
```

<!-- test: string-double-iteration -->
Iterating the same string twice yields the same count both times.
```maxon
function main() returns ExitCode
	let s = "Hello"
	var count1 = 0
	for _ in s 'loop1'
		count1 = count1 + 1
	end 'loop1'
	var count2 = 0
	for _ in s 'loop2'
		count2 = count2 + 1
	end 'loop2'
	if count1 == 5 and count2 == 5 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: heap-string-byteview -->
```maxon
function main() returns ExitCode
	let s = "ABCDEFGHIJKLMNOPQR"  // 18 bytes, heap allocated
	var count = 0
	for b in s.bytes() 'loop'
		// Use b to avoid unused variable warning
		if b > 0 'use'
			count = count + 1
		end 'use'
	end 'loop'
	print("{count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18
```

<!-- test: memory-tracking-simple-interp -->
```maxon
function main() returns ExitCode
	let a = "hello"
	let b = "world"
	let s = "{a} {b}"
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11
```

<!-- test: memory-tracking-chained-interp -->
String interpolation with multiple parts creates a single allocation with O(n) copy.
All intermediate buffers use stack allocation for primitives.
```maxon
function main() returns ExitCode
	let a = "a"
	let b = "b"
	let c = "c"
	let d = "d"
	let s = "{a}{b}{c}{d}"
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: memory-tracking-loop-interp -->
String accumulation in loop properly releases old values on reassignment.
The final value is released at scope exit. Uses efficient O(n) interpolation.
```maxon
function main() returns ExitCode
	var s = ""
	let x = "x"
	var i = 0
	while i < 3 'loop'
		s = "{s}{x}"
		i = i + 1
	end 'loop'
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: memory-tracking-no-leak-scope-exit -->
```maxon
function main() returns ExitCode
	if true 'scope'
		let temp = "heap allocated string here!"
		print("{temp.count()}\n")
	end 'scope'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
27
```

<!-- test: toLower -->
```maxon
function main() returns ExitCode
	var s = "HELLO"
	print(s.toLower())
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: case-conversion-does-not-mutate-receiver -->
### toLower / toUpper return a new string and leave the receiver unchanged
Regression guard. `toLower`/`toUpper` used to rewrite the receiver's bytes in place and
return the SAME buffer, so `let b = a.toLower()` silently lowercased `a` too. They now
transform an independent copy: the receiver reads back unchanged even after both calls,
and — because they no longer mutate `self` — they are callable on a `let` binding.
```maxon
function main() returns ExitCode
	var a = "Hello World"
	let lower = a.toLower()
	let upper = a.toUpper()
	print("{a}\n")
	print("{lower}\n")
	print("{upper}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Hello World
hello world
HELLO WORLD
```

<!-- test: case-conversion-on-let-binding -->
### toLower / toUpper are non-mutating, so they work on an immutable binding
```maxon
function main() returns ExitCode
	let a = "MixedCase"
	print("{a.toLower()}\n")
	print("{a.toUpper()}\n")
	print("{a}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
mixedcase
MIXEDCASE
MixedCase
```

<!-- test: bytes-count-method -->
### bytes().count() Method
```maxon
function main() returns ExitCode
	let s = "hello"
	print("{s.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: bytes-count-multibyte -->
### bytes().count() with Multi-byte Characters
```maxon
function main() returns ExitCode
	let s = "café"
	print("{s.bytes().count()}\n")  // 5 bytes (c=1, a=1, f=1, é=2)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: count-graphemes -->
### count Returns Grapheme Count
```maxon
function main() returns ExitCode
	let s = "café"
	print("{s.count()}\n")  // 4 graphemes
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: crlf-is-one-grapheme-when-iterating -->
### CR+LF Is One Grapheme When Iterating, Not Just When Counting
UAX #29 GB3 joins CR and LF into a single grapheme cluster, so an all-ASCII string is
NOT "one byte per character". `count()` and `for c in s` must agree: three clusters, of
which the middle one is two bytes wide.

The same four bytes are then assembled at RUNTIME. A compiler classifies a literal's bytes
itself, while a string built from a `ByteArray` is classified by the stdlib scanning them —
one rule with an implementation on each side of the compiler boundary, and nothing but this
case to make them agree. Both must report three.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let s = "a\r\nb"
	for c in s 'each'
		print("[{c.toString().byteLength()}]")
	end 'each'
	print("\ncount={s.count()}\n")

	var raw = ByteArray.create()
	raw.push(97)
	raw.push(13)
	raw.push(10)
	raw.push(98)
	print("scanned={String.from(raw).count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[1][2][1]
count=3
scanned=3
```

<!-- test: trim-character-set-splitting-crlf -->
### Trimming Never Splits a CR+LF Cluster
A set holding CR but not LF (or LF but not CR) does not match the two-byte `"\r\n"`
cluster, so nothing is trimmed. A set holding the cluster itself does match it. Trimming
whitespace hides this: CR and LF are independently whitespace, so a byte-at-a-time trim
reaches the same answer by coincidence. These sets remove the coincidence.
```maxon
function main() returns ExitCode
	let crOnly = CharacterSet.from(CharSet from ['\r'])
	let lfOnly = CharacterSet.from(CharSet from ['\n'])
	let crlf = CharacterSet.from(CharSet from ['\r\n'])
	let s = "\r\nx\r\n"
	print("{s.trimStart(crOnly).byteLength()}\n")
	print("{s.trimEnd(lfOnly).byteLength()}\n")
	print("{s.trim(crOnly).byteLength()}\n")
	print("{s.trim(crlf).byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
5
5
1
```

<!-- test: crlf-index-and-search-arithmetic -->
### Index and Search Arithmetic Never Assumes One Byte Per Character
`charAt`, `indexAfter`, `indexBefore`, `findFirst`, `findLast` and `slice(start, length:)` each
have a byte offset and a grapheme index to relate. Six of them once did it by ARITHMETIC whenever
the string's ASCII flag was set, instead of asking where the cluster ends. This string is
all-ASCII, so that flag WAS set — and it holds a CR+LF, which is two bytes and one cluster
(UAX #29 GB3), so every one of the six answered differently from `count()`.

`x` `\r\n` `y` `z` — five bytes, four clusters, with the two-byte one in the middle.
```maxon
function main() returns ExitCode
	let s = "x\r\nyz"
	print("clusters={s.count()} bytes={s.byteLength()}\n")

	// indexAfter steps OVER the cluster, and charAt reports its full width.
	var i = s.startIndex()
	i = try s.indexAfter(i) otherwise panic("indexAfter past x")
	print("afterX bytePos={i.bytePos()} width={s.charAt(i).toString().byteLength()}\n")
	i = try s.indexAfter(i) otherwise panic("indexAfter past the cluster")
	print("afterCluster bytePos={i.bytePos()} charIndex={i.charIndex()}\n")

	// The searches report a GRAPHEME index beside the byte offset, and past a cluster
	// those two numbers differ.
	let f = try s.findFirst("z") otherwise panic("findFirst")
	print("findFirst charIndex={f.charIndex()} bytePos={f.bytePos()}\n")
	let l = try s.findLast("y") otherwise panic("findLast")
	print("findLast charIndex={l.charIndex()} bytePos={l.bytePos()}\n")

	// indexBefore lands on the START of the cluster, never on its LF half. It steps back from
	// the SEARCH result, not from the walk above: a walk that splits the cluster arrives at a
	// different byte, and then `indexBefore` is being asked a different question rather than
	// answering the same one wrongly. `findFirst`'s byte offset is the same number either way.
	let back = try s.indexBefore(l) otherwise panic("indexBefore")
	print("back bytePos={back.bytePos()} charIndex={back.charIndex()}\n")

	// Slicing by grapheme LENGTH counts the cluster once and takes both its bytes.
	let two = s.slice(s.startIndex(), length: 2)
	print("slice2 bytes={two.byteLength()} clusters={two.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
clusters=4 bytes=5
afterX bytePos=1 width=2
afterCluster bytePos=3 charIndex=2
findFirst charIndex=3 bytePos=4
findLast charIndex=2 bytePos=3
back bytePos=1 charIndex=1
slice2 bytes=3 clusters=2
```

<!-- test: count-vs-bytes-count -->
### count vs bytes().count()
```maxon
function main() returns ExitCode
	let s = "🇺🇸"  // Flag emoji (1 grapheme, 8 bytes)
	print("{s.count()}\n")
	print("{s.bytes().count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
8
```

<!-- test: grapheme-iteration-emoji -->
### Grapheme Iteration with Emoji
```maxon
function main() returns ExitCode
	let s = "a🎉b"
	var count = 0
	for c in s 'loop'
		print("{c}")  // Use c to avoid unused warning
		count = count + 1
	end 'loop'
	print("\n{count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a🎉b
3
```

<!-- test: grapheme-iteration-flag -->
### Grapheme Iteration with Flag Emoji
```maxon
function main() returns ExitCode
	let s = "🇺🇸🇬🇧"  // Two flag emojis
	var count = 0
	for c in s 'loop'
		print("{c}")  // Use c to avoid unused warning
		count = count + 1
	end 'loop'
	print("\n{count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
🇺🇸🇬🇧
2
```

<!-- test: grapheme-iteration-zwj -->
### Grapheme Iteration with ZWJ Sequence
```maxon
function main() returns ExitCode
	let s = "👨‍👩‍👧"  // Family emoji (1 grapheme)
	var count = 0
	for c in s 'loop'
		print("{c}")  // Use c to avoid unused warning
		count = count + 1
	end 'loop'
	print("\n{count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
👨‍👩‍👧
1
```

<!-- test: codepoints-view -->
### Codepoints View
```maxon
function main() returns ExitCode
	let s = "Aé"  // A (1 codepoint) + é (1 codepoint if precomposed)
	for cp in s.codepoints() 'loop'
		print("{cp}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
65
233
```

<!-- test: string-reassignment -->
```maxon
function main() returns ExitCode
	let s = "hello"
	print("{s.count()}\n")

	var u = "abc"
	u = "testing"
	print("{u.count()}\n")

	var v = ""
	v = "world"
	print("{v.count()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
7
5
```

<!-- test: slice-basic -->
### Basic String Slicing
```maxon
function main() returns ExitCode
	let s = "hello world"
	let start = s.startIndex()
	let spaceIdx = try s.findFirst(" ") otherwise s.endIndex()
	let sub = s.slice(start, endIndex: spaceIdx)
	print(sub)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: slice-full -->
### Slice Entire String
```maxon
function main() returns ExitCode
	let s = "hello"
	let start = s.startIndex()
	let endIdx = s.endIndex()
	let sub = s.slice(start, endIndex: endIdx)
	print(sub)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: slice-empty -->
### Empty Slice
```maxon
function main() returns ExitCode
	let s = "hello"
	let start = s.startIndex()
	let sub = s.slice(start, endIndex: start)
	print("{sub.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: slice-iteration -->
### Iterate Over Sliced String
```maxon
function main() returns ExitCode
	let s = "abcdef"
	let start = s.startIndex()
	let idx = try s.findFirst("d") otherwise s.endIndex()
	let sub = s.slice(start, endIndex: idx)
	for c in sub 'loop'
		print("{c}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a
b
c
```

<!-- test: clone-isolates-string-mutation -->
### Clone Isolates String Mutation
```maxon
function main() returns ExitCode
	let original = "HELLO"
	var copy = original.clone()
	copy = copy.toLower()
	print("{original}\n")
	print("{copy}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
HELLO
hello
```

<!-- test: clone-preserves-original -->
### Clone Preserves Original
```maxon
function main() returns ExitCode
	let a = "TEST STRING"
	var b = a.clone()
	let c = a.clone()
	b = b.toLower()
	print("{a}\n")
	print("{b}\n")
	print("{c}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
TEST STRING
test string
TEST STRING
```

<!-- test: cow-slice-independent -->
### Slice Is Independent After Parent Goes Out of Scope
Demonstrates that sliced strings work correctly.
```maxon
function main() returns ExitCode
	let s = "hello world"
	let start = s.startIndex()
	let spaceIdx = try s.findFirst(" ") otherwise s.endIndex()
	let sub = s.slice(start, endIndex: spaceIdx)
	print("{sub}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

## String.append

<!-- test: string-append-basic -->
### Basic Append
Append a string literal to an existing string.
```maxon
function main() returns ExitCode
	var s = "Hello"
	s.append(" World")
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Hello World
```

<!-- test: string-append-interp -->
### Append with Interpolation
Append an interpolated string directly into the target buffer without materializing a temporary.
```maxon
function main() returns ExitCode
	var s = "Hello"
	let name = "World"
	s.append(" {name}!")
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Hello World!
```

<!-- test: string-append-loop -->
### Append in Loop
Append in a loop builds the string efficiently with amortized O(1) per append.
```maxon
function main() returns ExitCode
	var s = ""
	var i = 0
	while i < 5 'loop'
		s.append("{i}")
		i = i + 1
	end 'loop'
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
01234
```

<!-- test: string-append-variable -->
### Append Variable
Append another string variable.
```maxon
function main() returns ExitCode
	var s = "abc"
	let other = "def"
	s.append(other)
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
abcdef
```

<!-- test: string-append-implicit-loop -->
### Implicit Append Optimization
The pattern `s = "{s}..."` is automatically optimized to in-place buffer growth,
equivalent to `s.append("...")`.
```maxon
function main() returns ExitCode
	var s = ""
	var i = 0
	while i < 5 'loop'
		s = "{s}{i},"
		i = i + 1
	end 'loop'
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0,1,2,3,4,
```

<!-- test: string-append-multi-parts -->
### Append Multiple Interpolation Parts
Append with multiple interpolated expressions written directly into buffer.
```maxon
function main() returns ExitCode
	var s = "["
	let a = 1
	let b = 2
	s.append("{a}+{b}")
	s.append("]")
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[1+2]
```

<!-- test: tobytearray-is-independent-of-an-owned-source -->
`toByteArray()` returns a NEW INDEPENDENT `ByteArray`. Writing to it must not touch the string.
The copy-on-write view behind it is an optimisation, not the contract.
```maxon
function main() returns ExitCode
	let who = "ello"
	let s = "H{who}"
	var arr = s.toByteArray()
	try arr.set(0, value: 74) otherwise panic("set")
	let b0 = try arr.get(0) otherwise panic("get")
	print("s={s} arr[0]={b0}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
s=Hello arr[0]=74
```

<!-- test: tobytearray-is-independent-of-a-literal-source -->
The source can be ANYTHING and the rule does not change — including a static literal, where sharing
for writes is not merely undesirable but impossible: literals are interned, so `a` and `b` here are
ONE immortal object in read-only `.rodata`. A shared write would rewrite `b` too, and fault first.
```maxon
function main() returns ExitCode
	let a = "Hello"
	let b = "Hello"
	var arr = a.toByteArray()
	try arr.set(0, value: 74) otherwise panic("set")
	let b0 = try arr.get(0) otherwise panic("get")
	print("a={a} b={b} arr[0]={b0}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=Hello b=Hello arr[0]=74
```

<!-- test: tobytearray-is-independent-through-the-raw-managed-door -->
The independence holds through the RAW door too, and that is what makes `s.toByteArray().managed` the
sanctioned way for code outside the stdlib to hand a string's bytes to an intrinsic that wants a
`__ManagedMemory`. `Array` still exports `managed`; `String` stopped exporting it in Stage 4c of the
SSO plan. So this is the one remaining route from a string to a writable buffer — and it detaches,
where reaching through the string's own field did not.

This is pinned separately from the tests above because they go through `Array.set`, which is
COW-aware by construction. `arr.managed.set` bypasses that and writes the buffer directly, so it is
the case that would actually alias if the view had not already detached.
```maxon
function main() returns ExitCode
	let who = "ello"
	let s = "H{who}"
	let arr = s.toByteArray()
	try arr.managed.set(0, 74) otherwise panic("set")
	let b0 = try arr.get(0) otherwise panic("get")
	print("s={s} arr[0]={b0}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
s=Hello arr[0]=74
```

<!-- test: tobytearray-survives-the-source-growing -->
The independence runs both ways: growing the STRING after taking the bytes must not disturb them.
`append` detaches the string to a fresh buffer, and the array keeps the bytes it was given.

⚠ THE SECOND ROUND IS THE ONE THAT BITES, AND THE FIRST ONE ON ITS OWN DID NOT. Five bytes is
short enough that the string's grow does not free a block whose head the allocator then writes
through, so this case passed for a long time while the contract it states was false: an array
that merely VIEWED the string's buffer read the freed block after a real reallocation, and its
first eight bytes came back zeroed. The second round owns its buffer before the array is taken
(a literal is read-only data, which is never freed) and is long enough to be reallocated rather
than carried inline. A pinned rule is only as strong as the shape it is pinned on.
```maxon
function main() returns ExitCode
	let who = "ello"
	var s = "H{who}"
	let arr = s.toByteArray()
	s.append("!!!")
	let b0 = try arr.get(0) otherwise panic("get")
	print("s={s} arr.count={arr.count()} arr[0]={b0}\n")

	var big = "0123456789abcdefghijABCDEFGHIJ"
	big.append("+")
	let bigArr = big.toByteArray()
	big.append("TAIL")
	let f0 = try bigArr.get(0) otherwise panic("get 0")
	let f8 = try bigArr.get(8) otherwise panic("get 8")
	print("big.count={bigArr.count()} big[0]={f0} big[8]={f8}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
s=Hello!!! arr.count=5 arr[0]=72
big.count=31 big[0]=48 big[8]=56
```
