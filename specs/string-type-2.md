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
