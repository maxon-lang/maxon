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

<!-- test: string-append-self -->
### Append a String to Itself
`s.append(s)` ALIASES the argument to the receiver's own bytes, so the source of the blit is the buffer the
grow has just replaced. The result must still be the doubled text, and the run must stay leak-free: the old
allocation may only be released once BOTH the grow's copy and the blit have finished reading it. shv2-authored;
the expected output is the bootstrap oracle's, which produces `abcabc` for the same program.

⚠ The parenthetical here read *"the corpus has no self-append case"* until BATCH32, and it was wrong:
`specs/ownership-edge-cases.md` carries `rc-repeated-self-append`, which is now ported beside it.

⛔ **THE REASON BATCH32 GAVE FOR KEEPING BOTH WAS ALSO WRONG, AND THE REVIEW MEASURED IT (BATCH32 review).**
It said the ported case's SECOND round *"frees the block it copies from"* where this one-round case cannot.
It does not: growth is `2 * requiredLen` against a `capacity < requiredLen` test, so round 1 detaches the
`.rdata` literal with no owed allocation to free (`len 6, cap 12`) and round 2 fits exactly and appends IN
PLACE. **Neither this case nor a two-round one can fail on the read-after-free both describe** — verified by
moving `emitReleaseOwedBase` ahead of the blit, which left rounds 1 and 2 clean and corrupted round 3.
⇒ `rc-repeated-self-append` now runs THREE rounds and is the case that guards the hazard. This one stays as
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
than to the length — and it is the one shape that still reaches a second consecutive detach now that growth
is geometric. shv2-authored (the corpus has no empty-append case); the expected output is the bootstrap
oracle's.
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

### String literals are COPY-ON-WRITE

⭐⭐ **A STRING LITERAL'S BYTES ARE SHARED; A WRITE THROUGH ONE USE IS NOT.** Every use of `"hello"` in a
program reads ONE immortal `.rdata` blob — the bytes are interned, so a literal written a hundred times
costs one copy of its text. Each use describes those bytes through its own 48-byte record (`buffer@0`,
`length@8`, `capacity@16 = RdataBufferCapacity`), and the first write through one detaches it onto a private
buffer (`BufferOwnership.emitBufferCannotHold` is unconditionally true against a negative capacity), leaving
the blob untouched for every other use.

⚠ **THE DETACH NEEDS SOMEWHERE WRITABLE TO PUBLISH ITSELF, AND THAT IS THE HALF THAT WAS MISSING.**
`__str_append` already detached the BUFFER; what it had nowhere to write back was the new
`buffer@0`/`capacity@16`/`length@8`, because a literal's record is emitted into `.rdata` beside its blob.
The symptom split by target, and BOTH readings below are measured at `4e2b0b2b38`:

- **x64/arm64** — the record is in a read-only image section, so the store faulted. `grow("hello")` was an
  ACCESS VIOLATION (`0xC0000005`, exit 3221225477) with an empty stdout.
- **wasm32-wasi** — linear memory has no read-only segment, so the identical store SUCCEEDED and the program
  ran to completion. **MEASURED: exit 101 with stdout CORRECT** — `grow("hello")` then `print("hello")`
  printed `hello`, and doubling the pair printed `hello` twice. The leak gate's orphaned detached buffer is
  the whole signal. It does NOT print `helloXY`: `GlobalDataTable` dedupes identical BLOBS but mints a
  record per literal OCCURRENCE (`__str_rec_1`, `__str_rec_3`, … in any golden), so the repointed record is
  the writing use's own and no reader can see it.

⇒ the cases below run on EVERY target deliberately. A check that only watched for the fault would call the
wasm lane green while it leaked, and a check that only watched stdout would call the x64 lane green while it
crashed.

⭐ **WHAT MAKES THEM PASS: the literal at a WRITTEN parameter position is given a heap record of its own**
(`LiteralArgPromotion` — `__str_clone` before the call, `__str_decref` after). A literal that no callee
writes still lowers to its immortal `.rdata` record and costs nothing.

⭐⭐ **AND A LITERAL REACHING SUCH A POSITION THROUGH A MERGE IS THE SAME ONE SUBSTITUTION, ASKED OF THE
RECORD INSTEAD OF THE COMPILER** (`grow("a" if c else "b")`, `grow(try arr.get(5) otherwise "lit")`,
`grow(s if c else "lit")`). The argument is a block-arg whose edges have DIFFERENT provenances — one an
immortal `.rdata` record that must be COPIED, one a live heap record whose write MUST reach its owner — so
no *statically chosen* substitution can be right on both. The substitution is `__str_retain`, which reads
`capacity@16` and clones or increfs accordingly; `__str_decref` balances either arm, because it frees only
at the last owner. ⚠ The provenance is genuinely not a compile-time fact even for a single edge: a borrowed
`String` PARAMETER is a heap record when its caller owned one and an immortal record when its caller wrote a
literal, and one `pick(s String, c bool)` sees both.

⚠ **THE FAILURE MODES ARE OPPOSITE AND BOTH WERE MEASURED**, which is why the cases below pin the
write-through as hard as they pin the fault: increfing an immortal record writes a refcount into a read-only
image section (`0xC0000005`), and cloning a heap record hands the callee a COPY, so the append lands
somewhere nobody reads (`v=ab` where the oracle prints `v=abXY` — silent, exit 0, and invisible to a check
that only watched for the crash).

<!-- test: string-literal-through-a-mutating-parameter -->
### A Literal Passed to a Mutating Parameter
The minimal shape. `grow` writes through a borrowed `String` parameter, and the argument is a bare literal —
so nothing about the CALL SITE is unusual and nothing about the callee is: the record it is handed simply
has to be one a write may land in.
```maxon
function grow(s String)
	s.append("XY")
end 'grow'

function main() returns ExitCode
	grow("hello")
	print("survived\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
survived
```

<!-- test: string-literal-bytes-outlive-a-write-through-another-use -->
### A Write Through One Use Leaves Every Other Use Alone
⭐ **THIS IS THE COW PROPERTY ITSELF, and it is the case the x64 fault could never have shown.** The two
`"hello"`s share one interned blob; the first is written through and must detach onto a private buffer,
which leaves the second reading the original bytes. On wasm this case printed `helloXY` before the record
became per-use — the shared record had been repointed at the grown buffer, so a use that never wrote saw
the write anyway.
```maxon
function grow(s String)
	s.append("XY")
end 'grow'

function main() returns ExitCode
	grow("hello")
	print("hello\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: string-literal-through-two-call-levels -->
### A Literal Through Two Call Levels
The borrow is transitive: `outer` passes its own borrowed parameter on to `grow`, so the record that is
finally written is two frames away from the literal that produced it. Nothing along the way copies it, which
is the point — a fix that worked by promoting at the immediate call site would still fault here.
```maxon
function grow(s String)
	s.append("XY")
end 'grow'

function outer(t String)
	grow(t)
end 'outer'

function main() returns ExitCode
	outer("hello")
	print("hello\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: string-literal-through-a-merge-of-two-literals -->
### A Literal Through a Ternary Merge
Both edges are immortal `.rdata` records, so both need a writable one — but the argument the callee is
handed is the MERGE, not either literal, and the merge is what has to be substituted.
```maxon
function grow(s String)
	s.append("XY")
end 'grow'

function main() returns ExitCode
	let c = true
	grow("a" if c else "b")
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
done
```

<!-- test: string-literal-through-a-try-otherwise-fallback -->
### A Literal on a `try … otherwise` Fallback Edge
⭐ **THE TWO EDGES DISAGREE, AND BOTH ARE EXERCISED HERE.** The `ok` edge is a real heap element the array
owns and whose append MUST reach it (`t=abXY`); the `otherwise` edge is an immortal literal that must get a
record of its own. The first `grow` takes the ok edge, the second takes the fallback — so a fix that copied
the element would print `t=ab`, and one that increfed the literal would fault on the second call.
```maxon
typealias StringArray = Array with String

function grow(s String)
	s.append("XY")
end 'grow'

function main() returns ExitCode
	var arr = StringArray.create()
	arr.push("ab")
	grow(try arr.get(0) otherwise "lit")
	let t = try arr.get(0) otherwise panic("get")
	print("t={t}\n")
	grow(try arr.get(5) otherwise "lit")
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
t=abXY
done
```

<!-- test: string-literal-merged-with-a-borrowed-parameter -->
### A Literal Merged With a Borrowed Parameter
One `pick` sees a caller-owned heap record on the first call and takes the literal edge on the second, so a
SINGLE compiled merge must be right about both. `t=abXY` is the borrowed edge writing through to `main`'s
own `v`; `done` is the literal edge not faulting.
```maxon
function grow(s String)
	s.append("XY")
end 'grow'

function pick(s String, c bool)
	grow(s if c else "lit")
end 'pick'

function main() returns ExitCode
	var v = "ab"
	pick(v, c: true)
	print("t={v}\n")
	pick(v, c: false)
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
t=abXY
done
```

<!-- test: a-merged-borrow-still-writes-through-to-the-caller -->
### The Non-Literal Edge Still Writes Through
⭐⭐ **THE PROPERTY A NAIVE FIX DESTROYS, PINNED RATHER THAN ASSUMED.** Here the merge already has an OWNED
edge (`make()`), so the borrowed `s` edge is promoted to match it — and promoting it by COPYING is exactly
the wrong answer: `grow` would append to the copy and `main`'s `v` would never see it. This printed `v=ab`
before the promotion became a co-ownership, silently and with exit 0.
```maxon
function grow(s String)
	s.append("XY")
end 'grow'

function make() returns String
	var m = "zz"
	m.append("!")
	return m
end 'make'

function pick(s String, c bool)
	grow(make() if c else s)
end 'pick'

function main() returns ExitCode
	var v = "ab"
	pick(v, c: false)
	print("v={v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=abXY
```

<!-- test: a-returned-borrow-still-writes-through-to-the-caller -->
### The Return Door Hands Back the Caller's Own Record
⭐⭐ **THE `returned` DOOR'S HALF OF THE SAME RULING, AND NOTHING PINNED IT FOR `String` UNTIL NOW**
(`ca5169e231`, *"borrowed strings are no longer copied"*). `return s` over a borrowed `String` promotes
through `retainBorrowedByteRecord` (`__str_retain`) where it used to promote through
`promoteToOwnedString` (`__mm_alloc` + `__str_copy`), so what the caller adopts is a SECOND REFERENCE to
`main`'s record rather than a copy of its bytes. Maxon is single-ownership with reference semantics —
*"everything is a reference; if you want a copy you do it explicitly with `clone()`"* — so a `return s`
handing back a different record than the `s` it was given would be a copy the author never wrote, and the
caller's value would silently stop being the callee's. There is ONE record here, so both names print the
appended text; the shared record is co-owned (`markValueCoOwnsHeap`) and never destructively moved.
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
v=abXY out=abXY
```

<!-- test: a-returned-literal-is-cloned-not-shared -->
### A Returned Literal Is Cloned, and the `.rdata` Record Is Untouched
⚠ **THE OTHER ARM OF `__str_retain`, WHICH IS WHAT LETS THE RETURN DOOR RETAIN AT ALL.** A `String`
LITERAL is a wholly immortal `.rdata` record and shv2 has no immortal-refcount sentinel, so an incref of
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

<!-- test: a-literal-reaching-a-merge-through-a-parameter -->
### A Literal Reaching a Merge Through a Parameter
⚠ **THIS IS WHY THE CHOICE CANNOT BE MADE AT COMPILE TIME.** `pick`'s `s` is the same declared `String`
parameter as in the case two above, and here it holds an IMMORTAL record because `main` wrote a literal —
nothing in `pick`'s frame distinguishes the two, and the merge is what has to ask.
```maxon
function grow(s String)
	s.append("XY")
end 'grow'

function pick(s String, c bool)
	grow(s if c else "lit")
end 'pick'

function main() returns ExitCode
	pick("ab", c: true)
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
done
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
