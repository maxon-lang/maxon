---
feature: string-type-2
status: experimental
keywords: [string, sso, utf8, cow]
category: types
---

# String Type (Part 2)

Continuation of [string-type](string-type.md): heap-string access, memory-tracking,
grapheme/codepoint iteration, slicing, clone/COW, and `String.append`.

## Tests

<!-- test: heap-string-data-access -->
```maxon
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
time constant. A string-const specializer that aliased the slot to its declaration
without invalidating it on reassignment would fold `v == "_"` to `"" == "_"` (false)
at compile time.
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
	var sum = 0 as Count
	// Iterate over bytes directly to test heap string iteration
	for b in s.bytes() 'loop'
		sum = sum + b as Count
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
	var count1 = 0 as FullRangeInt
	for _ in s 'loop1'
		count1 = count1 + 1
	end 'loop1'
	var count2 = 0 as FullRangeInt
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
	var count = 0 as FullRangeInt
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
	var i = 0 as FullRangeInt
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
Regression guard. `toLower`/`toUpper` transform an independent copy rather than rewriting
the receiver's bytes in place and returning the SAME buffer (which would make
`let b = a.toLower()` silently lowercase `a` too): the receiver reads back unchanged even
after both calls, and — because they do not mutate `self` — they are callable on a `let` binding.
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
	var count = 0 as FullRangeInt
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
	var count = 0 as FullRangeInt
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
	var count = 0 as FullRangeInt
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
	var i = 0 as FullRangeInt
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
	var i = 0 as FullRangeInt
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
allocation may only be released once BOTH the grow's copy and the blit have finished reading it.
The expected output is `abcabc`.

⚠ `specs/ownership-edge-cases.md` carries `rc-repeated-self-append` beside this one.

⛔ **ONLY THE THIRD ROUND OF A SELF-APPEND FREES THE BLOCK IT COPIES FROM.** Growth is `2 * requiredLen`
against a `capacity < requiredLen` test, so round 1 detaches the `.rdata` literal with no owed allocation
to free (`len 6, cap 12`) and round 2 fits exactly and appends IN PLACE. **Neither this case nor a
two-round one can fail on the read-after-free** — `emitReleaseOwedBase` moved ahead of the blit leaves
rounds 1 and 2 clean and corrupts round 3.
⇒ `rc-repeated-self-append` runs THREE rounds and is the case that guards the hazard. This one is
the ONE-round boundary — the detach off an unowned literal, which the three-round case passes through
without ever asserting on its own.
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
Appending nothing must change nothing observable, and must leave the receiver in a state a LATER append can
still grow correctly. Both halves are load-bearing and neither is obvious: a String whose bytes are INLINE
in its own record cannot be written in place at all (its buffer is part of its box), so even an empty append
detaches it onto a private buffer — and `u`, which detaches at length ZERO, gets a buffer of zero bytes
(`__str_append` asks for `2 * requiredLen`, and twice nothing is nothing), so its next append has no slack
and must grow again. A `capacity` test that only asked whether the length increased would let that second
append blit past the buffer it just sized, so this case is what pins the growth test to the capacity rather
than to the length — and under geometric growth it is the one shape that reaches a second consecutive
detach.
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

<!-- test: a-returned-borrow-bound-to-a-var-is-copied-not-written-through -->
### The Return Door Hands Back the Caller's Record, and the `var` Binding Copies It
⭐⭐ **A `String` A NAME DOES NOT OWN IS COPIED WHEN IT IS BOUND TO A `var`.** `return s` over a borrowed `String`
promotes through `retainBorrowedByteRecord` (`__str_retain`), so what the caller receives is a second reference to
`main`'s record. Binding that result to a `var` is the point where a writable name would share storage with `v`, so
the binding copies it at run time (`__str_unshare`): `out.append("XY")` appends to the copy and `v` is unchanged.
The record is shared only while the result is not written.
```maxon
function relay(s String) returns String
	return s
end 'relay'

function main() returns ExitCode
	var v = "ab"
	var out = relay(v)
	out.append("XY")
	print("v={v} out={out}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=ab out=abXY
```

<!-- test: a-returned-borrow-assigned-to-a-var-is-copied-not-written-through -->
### The Reassignment Door Copies What The Binding Door Copies
⭐⭐ **THE SIBLING ROUTE OF THE CASE ABOVE.** `s = relay(v)` hands an existing `var` the same shared record `var out =
relay(v)` is handed, so the reassignment copies it too (`__str_unshare`): `s.append("XY")` grows the copy, and `v` and
the string `s` held before are both untouched.
```maxon
function make() returns String
	var m = "a"
	m.append("b")
	return m
end 'make'

function relay(s String) returns String
	return s
end 'relay'

function main() returns ExitCode
	let v = make()
	var s = "zz"
	s = relay(v)
	s.append("XY")
	print("v={v} s={s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=ab s=abXY
```

<!-- test: a-returned-borrow-assigned-to-a-var-in-a-loop-is-copied-each-trip -->
### A Reassignment In A Loop Copies On Every Trip
Each trip hands `s` a fresh share of `v`'s record, and each trip writes the copy it was given, so `v` is never
written and the leak gate stays quiet.
```maxon
function make() returns String
	var m = "a"
	m.append("b")
	return m
end 'make'

function relay(s String) returns String
	return s
end 'relay'

function main() returns ExitCode
	let v = make()
	var s = "zz"

	for i in 0 upto 3 'trips'
		s = relay(v)
		s.append("{i}")
	end 'trips'

	print("v={v} s={s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=ab s=ab2
```

<!-- test: a-returned-borrow-stored-in-a-var-field-is-copied-not-written-through -->
### A Field Store Copies What A Binding Copies
The `var` field is a writable name too: `self.text = relay(src)` must not leave the field sharing the caller's record,
so growing the field leaves `v` as it was.
```maxon
function make() returns String
	var m = "a"
	m.append("b")
	return m
end 'make'

function relay(s String) returns String
	return s
end 'relay'

type Holder
	var text as String

	static function create(text String) returns Holder
		return Holder{text: text}
	end 'create'

	function adopt(src String)
		self.text = relay(src)
	end 'adopt'

	function grow()
		self.text.append("XY")
	end 'grow'

	function show(v String)
		print("v={v} text={self.text}\n")
	end 'show'
end 'Holder'

function main() returns ExitCode
	let v = make()
	var h = Holder.create("zz")
	h.adopt(v)
	h.grow()
	h.show(v)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=ab text=abXY
```

<!-- test: a-returned-borrow-stored-through-a-field-assignment-is-copied-not-written-through -->
### A Struct Literal Field Copies Too
`Holder{text: relay(src)}` stores the call's result into a `var` field through the literal door, so the same copy is
owed there and growing the field leaves `v` as it was.
```maxon
function make() returns String
	var m = "a"
	m.append("b")
	return m
end 'make'

function relay(s String) returns String
	return s
end 'relay'

type Holder
	var text as String

	static function adopting(src String) returns Holder
		return Holder{text: relay(src)}
	end 'adopting'

	function grow()
		self.text.append("XY")
	end 'grow'

	function show(v String)
		print("v={v} text={self.text}\n")
	end 'show'
end 'Holder'

function main() returns ExitCode
	let v = make()
	var h = Holder.adopting(v)
	h.grow()
	h.show(v)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=ab text=abXY
```

<!-- test: a-borrowed-string-stored-in-a-var-field-is-copied-not-written-through -->
### A Borrowed `String` Stored In A `var` Field Is Copied, Through Assignment And Through A Literal
The field is a writable name for what it was handed, so the store copies the record the caller still owns: growing
the field through `adopt` or through a record built by `create` leaves `v` as it was.
```maxon
function make() returns String
	var m = "a"
	m.append("b")
	return m
end 'make'

type Holder
	var text as String

	static function create(text String) returns Holder
		return Holder{text: text}
	end 'create'

	function adopt(src String)
		self.text = src
	end 'adopt'

	function grow()
		self.text.append("XY")
	end 'grow'

	function show(v String)
		print("v={v} text={self.text}\n")
	end 'show'
end 'Holder'

function main() returns ExitCode
	let v = make()
	var h = Holder.create("zz")
	h.adopt(v)
	h.grow()
	h.show(v)
	var g = Holder.create(v)
	g.grow()
	g.show(v)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=ab text=abXY
v=ab text=abXY
```

<!-- test: a-returned-literal-is-cloned-not-shared -->
### A Returned Literal Is Cloned, and the `.rdata` Record Is Untouched
⚠ **THE OTHER ARM OF `__str_retain`, WHICH IS WHAT LETS THE RETURN DOOR RETAIN AT ALL.** A `String`
LITERAL is a wholly immortal `.rdata` record and the compiler has no immortal-refcount sentinel, so an incref of
one would be a read-modify-write of a read-only image section. Nothing in `lit`'s frame can tell an
immortal record from a heap one — which is why the decision is made at RUN TIME, off the record's own
`capacity@16` against `ImmortalRecordCapacity`: that arm CLONES. So the returned value is an
independently-droppable heap record, appending through it touches nothing else, and a second call hands
back a fresh clone of the pristine literal.
```maxon
function lit() returns String
	return "ab"
end 'lit'

function main() returns ExitCode
	var first = lit()
	first.append("XY")
	let second = lit()
	print("first={first} second={second}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first=abXY second=ab
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
<!-- tier: stdlib -->
The independence holds through the RAW door too: `s.toByteArray().managed` is how library-tier code hands
a string's bytes to an intrinsic that wants a `__ManagedMemory`. `Array` exports `managed`; `String` does
not. So this is the one route from a string
to a writable buffer — and it detaches.

This is pinned separately from the tests above because they go through `Array.set`, which is
COW-aware by construction. `arr.managed.set` bypasses that and writes the buffer directly, so it is
the case that would actually alias if the view had not already detached.
```maxon
function main() returns ExitCode
	let who = "ello"
	let s = "H{who}"
	var arr = s.toByteArray()
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
