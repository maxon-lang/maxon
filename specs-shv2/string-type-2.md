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

<!-- disabled-test: heap-string-data-access -->
<!-- P1.7: heap-string data access -->
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

<!-- disabled-test: reassigned-var-equality-not-const-folded -->
<!-- P1.2 wave C: var reassignment of an owned String -->
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

<!-- disabled-test: heap-string-iteration -->
<!-- P1.7: string iteration -->
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

<!-- disabled-test: string-double-iteration -->
<!-- P1.7: string iteration -->
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

<!-- disabled-test: heap-string-byteview -->
<!-- P1.8: byte view / .bytes() -->
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

<!-- disabled-test: memory-tracking-simple-interp -->
<!-- P1.2 wave B: string interpolation -->
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

<!-- disabled-test: memory-tracking-chained-interp -->
<!-- P1.2 wave B: string interpolation -->
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

<!-- disabled-test: memory-tracking-loop-interp -->
<!-- P1.2 wave B: string interpolation -->
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

<!-- disabled-test: memory-tracking-no-leak-scope-exit -->
<!-- P1.2 wave B: string interpolation -->
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

<!-- disabled-test: toLower -->
<!-- P1.8: string methods -->
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

<!-- disabled-test: case-conversion-does-not-mutate-receiver -->
<!-- P1.8: string methods -->
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

<!-- disabled-test: case-conversion-on-let-binding -->
<!-- P1.8: string methods -->
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

<!-- disabled-test: bytes-count-method -->
<!-- P1.8: byte view / .bytes() -->
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

<!-- disabled-test: bytes-count-multibyte -->
<!-- P1.8: byte view / .bytes() -->
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

<!-- disabled-test: count-graphemes -->
<!-- P1.7: string iteration -->
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

<!-- disabled-test: count-vs-bytes-count -->
<!-- P1.8: byte view / .bytes() -->
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

<!-- disabled-test: grapheme-iteration-emoji -->
<!-- P1.7: string iteration -->
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

<!-- disabled-test: grapheme-iteration-flag -->
<!-- P1.7: string iteration -->
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

<!-- disabled-test: grapheme-iteration-zwj -->
<!-- P1.7: string iteration -->
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

<!-- disabled-test: codepoints-view -->
<!-- P1.7: string iteration -->
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

<!-- disabled-test: string-reassignment -->
<!-- P1.8: string methods (.count()) -->
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

<!-- disabled-test: slice-basic -->
<!-- P1.8: string methods -->
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

<!-- disabled-test: slice-full -->
<!-- P1.8: string methods -->
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

<!-- disabled-test: slice-empty -->
<!-- P1.8: string methods -->
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

<!-- disabled-test: slice-iteration -->
<!-- P1.7: string iteration -->
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

<!-- disabled-test: clone-isolates-string-mutation -->
<!-- P1.8: string methods -->
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

<!-- disabled-test: clone-preserves-original -->
<!-- P1.8: string methods -->
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

<!-- disabled-test: cow-slice-independent -->
<!-- P1.8: string methods -->
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

### String.append

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

<!-- disabled-test: string-append-implicit-loop -->
<!-- P1.8: string methods -->
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

<!-- test: string-append-self -->
### Append a String to Itself
`s.append(s)` ALIASES the argument to the receiver's own bytes, so the source of the blit is the buffer the
grow has just replaced. The result must still be the doubled text, and the run must stay leak-free: the old
allocation may only be released once BOTH the grow's copy and the blit have finished reading it. shv2-authored
(the corpus has no self-append case); the expected output is the bootstrap oracle's, which produces `abcabc`
for the same program.
```maxon
function main() returns ExitCode
	var s = "abc"
	s.append(s)
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
abcabc
```

<!-- test: string-append-empty -->
### Append an Empty String
An empty append needs no room, so the receiver's buffer already fits and nothing is reallocated — including
for a String whose bytes are still INLINE in its own record, whose capacity is its exact length. The
observable contract is only that the text and the byte length are unchanged, and that a later non-empty
append still grows correctly off that untouched buffer. shv2-authored (the corpus has no empty-append case);
the expected output is the bootstrap oracle's.
```maxon
function main() returns ExitCode
	var t = "xy"
	t.append("")
	print("{t}|{t.byteLength()}\n")
	var u = ""
	u.append("")
	u.append("q")
	print("{u}|{u.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
xy|2
q|1
```

<!-- disabled-test: tobytearray-is-independent-of-an-owned-source -->
<!-- P1.8: string methods -->
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

<!-- disabled-test: tobytearray-is-independent-of-a-literal-source -->
<!-- P1.8: string methods -->
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

<!-- disabled-test: tobytearray-is-independent-through-the-raw-managed-door -->
<!-- P1.8: string methods -->
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

<!-- disabled-test: tobytearray-survives-the-source-growing -->
<!-- P1.8: string methods -->
The independence runs both ways: growing the STRING after taking the bytes must not disturb them.
`append` detaches the string to a fresh buffer, and the array keeps the bytes it was given.
```maxon
function main() returns ExitCode
	let who = "ello"
	var s = "H{who}"
	let arr = s.toByteArray()
	s.append("!!!")
	let b0 = try arr.get(0) otherwise panic("get")
	print("s={s} arr.count={arr.count()} arr[0]={b0}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
s=Hello!!! arr.count=5 arr[0]=72
```
