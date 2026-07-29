---
feature: string-views
status: experimental
keywords: [string, bytes, toByteArray, codepoints, utf16, clone, isEmpty, replaceFirst, from]
category: types
---

# String views, copies and the one `String` static

## Documentation

Five `String` methods hand back a COPY of what the receiver holds, and one `String` static builds a
`String` out of one:

```text
toByteArray() returns Array with Byte
codepoints()  returns Array with integer
utf16()       returns Array with integer
clone()       returns String
isEmpty()     returns bool
replaceFirst(old String, with String) returns String

String.from(bytes Array with Byte) returns String
```

Four properties are what these tests pin, and each is a decision rather than an accident:

- **⚠ A VIEW MATERIALIZES.** The reference returns a LAZY `ByteView` / `CodepointView` / `Utf16View` — an
  `Iterable` with a cursor over the receiver's own buffer. shv2 has no `Iterable`, no associated types
  and no cursor protocol (the same absence that makes `for x in <array>` an index counter rather than a
  heap iterator), so each view copies into the one collection shv2 does have. `.count()` and
  `for u in <view>` then work through machinery that already exists.
- **`bytes()` and `toByteArray()` are ONE answer here**, and that follows from the point above rather
  than being a shortcut: the reference distinguishes them by laziness alone — its `toByteArray()` must
  copy, because a plain view onto an OWNED buffer is a read-after-free the moment the owner appends —
  and shv2's `bytes()` already copies. Two spellings, one emitter.
- **A copy is INDEPENDENT, and that is the CONTRACT.** `clone()` may not be `return self`: the receiver's
  record would gain a second owner and the caller's drop would take the receiver's bytes with it.
  `replaceFirst`'s two no-op cases — an empty needle, and a needle that is absent — answer with a clone
  for exactly that reason.
- **`String.from` COPIES the array's bytes** where the reference shares the array's `__ManagedMemory`.
  shv2 has no shared-ownership relationship between an `Array` record and a `String` record: each box's
  drop reclaims its own allocation, so a view would be a second reclaimer of one block.

The nine `utf16*` FREE functions (`utf16Width`, `utf16IsLeadSurrogate`, `utf16IsTrailSurrogate`,
`utf16IsSurrogate`, `utf16IsBmp`, `utf16LeadSurrogate`, `utf16TrailSurrogate`, `utf16DecodeSurrogates`,
`utf16IsValidSurrogatePair`) are NOT compiler builtins: they are `stdlib/helpers/string/utf16.maxon`,
reached through the stdlib whitelist as ordinary declarations. Their parameter type `Codepoint` is
declared in `stdlib/Character.maxon`, which shv2 cannot load (`Character` is a name the compiler owns),
so `Codepoint` is a COMPILER-SYNTHESIZED ranged int alias exactly as `HashValue` is — and, like every
compiler-owned type name, a user declaration may not bind it to a nominal identity.

## Tests

<!-- test: codepoints-and-utf16-are-different-lengths -->
### A supplementary codepoint is ONE codepoint and TWO UTF-16 code units
The one string where all four counts differ, which is what tells a real UTF-16 encode from a
codepoint list wearing its name: `A😀B` is 3 graphemes, 3 codepoints, 4 code units and 6 bytes.
```maxon
function main() returns ExitCode
	let s = "A😀B"
	print("{s.count()} {s.codepoints().count()} {s.utf16().count()} {s.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3 4 6
```

<!-- test: utf16-encodes-a-surrogate-pair-in-order -->
### The lead surrogate precedes the trail, and the walk resumes past the whole sequence
A four-byte UTF-8 sequence pushes TWO code units and then advances the source cursor by four — so a
character after the emoji still lands in the right place.
```maxon
function main() returns ExitCode
	let s = "😀A"
	for u in s.utf16() 'each'
		print("{u},")
	end 'each'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
55357,56832,65,
```

<!-- test: empty-string-views-are-empty -->
### Every view of an empty string is empty, and every copy of one is empty
The zero-length walk of each of the three materializers, plus the two copies, in one program: a loop
whose bound is `0` must push nothing, and a fresh record of zero bytes must still be a valid String.
```maxon
function main() returns ExitCode
	let e = ""
	let copy = e.clone()
	let replaced = e.replaceFirst("x", with: "y")
	print("{e.isEmpty()} {e.toByteArray().count()} {e.codepoints().count()} {e.utf16().count()} {copy.byteLength()} {replaced.byteLength()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true 0 0 0 0 0
```

<!-- test: replacefirst-replaces-only-the-first -->
### `replaceFirst` leaves every later occurrence alone
```maxon
function main() returns ExitCode
	let s = "aXbXc"
	print("{s.replaceFirst("X", with: "--")}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a--bXc
```

<!-- test: replacefirst-no-match-and-empty-needle-clone -->
### Both no-op cases answer with an independent copy, not with the receiver
An empty needle matches at every position and an absent needle at none; both mean "nothing to do", and
both must hand back a String the caller may own and drop without touching the receiver's bytes.
```maxon
function main() returns ExitCode
	var s = "aXbXc"
	let empty = s.replaceFirst("", with: "Q")
	let missing = s.replaceFirst("zz", with: "Q")
	s.append("!")
	print("{empty} {missing} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
aXbXc aXbXc aXbXc!
```

<!-- test: from-round-trips-non-ascii-bytes -->
### `String.from` rebuilds a multi-byte string from its own bytes
The bytes go out through `toByteArray()` and back in through `String.from`, and the result must be
EQUAL to the source and carry its grapheme count — which is the ASCII classification being computed
from the bytes rather than assumed.
```maxon
function main() returns ExitCode
	let src = "中文字"
	let back = String.from(src.toByteArray())
	print("{back} {back.count()} {back.byteLength()} {back == src}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
中文字 3 9 true
```

<!-- test: clone-outlives-a-growing-source -->
### A clone of a heap String survives its source growing
`append` may reallocate the receiver's buffer, so a clone that VIEWED it would read freed memory. The
string is long enough to be heap-backed, which is the only case where the difference is observable.
```maxon
function main() returns ExitCode
	var a = "HELLOWORLDLONGENOUGH"
	let c = a.clone()
	a.append("TAIL")
	print("{a} {c}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
HELLOWORLDLONGENOUGHTAIL HELLOWORLDLONGENOUGH
```

<!-- test: error.from-rejects-a-non-byte-array -->
### `String.from`'s argument must be an `Array with Byte`
An `Array with integer` strides EIGHT bytes per element, so reading it as bytes would hand back every
eighth byte of a slot's worth of padding — a silent wrong answer, refused at the argument instead.
```maxon
function main() returns ExitCode
	let s = String.from([1, 2, 3])
	return s.byteLength()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:22: 'String.from' requires a Array with Byte, but its argument is Array_int
```

<!-- test: error.from-rejects-a-string -->
### …and it is the ELEMENT TYPE that is checked, not merely "some container"
```maxon
function main() returns ExitCode
	let s = String.from("abc")
	return s.byteLength()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:22: 'String.from' requires a Array with Byte, but its argument is String
```

<!-- test: error.string-has-exactly-one-static -->
### `String` has ONE static, and an unknown one is named where it is written
`fromOwnedBytes` is deliberately not exported by the reference ("take these bytes and trust me about
them" is not a promise the stdlib can let arbitrary code make), and there is no `String.from(codepoints)`.
```maxon
function main() returns ExitCode
	let s = String.create()
	return s.byteLength()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:17: Unsupported: `String` static 'create' — the reference exports one, `from(bytes)`, and `fromOwnedBytes` is deliberately not exported
```

<!-- test: error.codepoint-is-a-compiler-owned-type-name -->
### A declaration may not bind `Codepoint` to a nominal identity
`Codepoint` is the compiler's own synthesized ranged int alias — `stdlib/Character.maxon` declares it
and shv2 cannot load that module — so a user `type Codepoint` would mean one thing to the parser and
another to type resolution, which is the disagreement `HashValue` was measured to cause.
```maxon
type Codepoint
	export var value as int
end 'Codepoint'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:6: Unsupported: a declaration of the type name 'Codepoint', which the compiler owns — shv2 synthesizes that declaration rather than reading it from the stdlib, and has no namespace to tell a user declaration of the name apart from the builtin one
```

⚠ **THE FOUR RANGE-GUARD CASES BELOW COME IN PAIRS THAT PARTITION THE TARGETS, AND THE SPLIT IS A
TARGET FACT RATHER THAN A CONVENIENCE.** A range violation EXITS 1 on every target — that is the guard
FIRING. The message and the backtrace exist only where there is a panic runtime to print them: wasm's
`emitRangePanic` is `i32.const 1; call $exit; unreachable`, its constant named `RangePanicExitCode` and
documented *"No message/backtrace (wasm has no panic runtime)"*, and x64-linux is measured the same.
So each guard is pinned TWICE — once on `x64-linux, wasm32-wasi`, where the assertion is the exit code
and stderr must be silent, and once on `x64-windows`, where the message is pinned in full.

⚠ **A `stderr` BLOCK CANNOT BE OMITTED TO PAPER OVER THIS**: an absent block asserts the program
printed NOTHING (`SpecTestRunner.checkRunStderr`, whose `unpinned` arm says so in as many words), which
is TRUE on the two silent lanes and FALSE on Windows. Partitioning the targets is what lets both facts
be stated; a single case cannot state either without lying about the other.

MEASURED, and this is why the pairing exists rather than a `targets: x64-windows` line on the lot:
these two guards were first written as message-only cases with no target marker, and the cross-target
gate went red on x64-linux and wasm with an EMPTY `actual` — which READS exactly like "the guard is
missing on this target" and is not. The identical program over a USER-declared
`typealias Percent = int(0 to 100)`, touching neither the whitelist nor `Codepoint`, exits 1 with empty
stderr on wasm and exits 1 with the full message on x64. So the guard fires everywhere, and asserting
the EXIT CODE on the silent lanes is what proves it — which no case in this suite had done for a range
guard before.

<!-- test: whitelisted-stdlib-keeps-its-range-check -->
<!-- targets: x64-linux, wasm32-wasi -->
### A REACHABLE whitelisted stdlib function still gets its range guard
`insertRangeChecks` skips a stdlib function no path from `main` reaches — that skip is what keeps the
whitelist from renumbering `.rdata` for functions nobody calls. It must not be one word wider than
that: a function the program DOES call keeps every guard it declared. `utf16LeadSurrogate` returns
through the ranged `CodeUnit16`, and a codepoint far past the supplementary plane overflows it.

The exit code is the assertion because it is the part every target shares. `7` is what an UNGUARDED
build returns — the out-of-range lead surrogate, 1031794, is comfortably past `u16.max` — so this case
tells a fired guard from a silent wrong answer on every lane, which a message-only case cannot.

⚠ **THE CODEPOINT ARRIVES THROUGH `widen`, AND IT HAS TO.** `utf16LeadSurrogate(1000000000)` — which is
what this case read while a call argument's declared range went unenforced — is now the compile-time
E3005 `range-check-panic.md` promises at every position ("a literal argument never reaches a runtime
check because it never builds"), measured identically on the bootstrap. The subject here is the RUNTIME
guard on the whitelisted stdlib function's ranged `return`, so the codepoint must be a value no constant
folds to; `widen` is the smallest way to say that.
```maxon
typealias Wide = int(0 to u32.max)

function widen(n Wide) returns Wide
	return n * 1000000
end 'widen'

function main() returns ExitCode
	let lead = utf16LeadSurrogate(widen(1000))
	return 7 if lead > 65535 else 9
end 'main'
```
```exitcode
1
```

<!-- test: whitelisted-stdlib-range-panic-names-the-alias -->
<!-- targets: x64-windows -->
### …and on the target that has a panic runtime, it names the alias and the function
The message half of the case above — same program, same reason for `widen`. `targets: x64-windows` for
the reason stated at the head of this group, and the same reason every other panic-text case in the
suite carries it.
```maxon
typealias Wide = int(0 to u32.max)

function widen(n Wide) returns Wide
	return n * 1000000
end 'widen'

function main() returns ExitCode
	let lead = utf16LeadSurrogate(widen(1000))
	return 7 if lead > 65535 else 9
end 'main'
```
```exitcode
1
```
```stderr
panic at utf16.maxon:51: Range check failed: value outside typealias 'CodeUnit16'
Stack trace:
  in utf16LeadSurrogate
  in main
  in mrt_start
```

<!-- test: truncated-sequences-stop-at-the-buffer-end -->
### A lead byte may not promise more bytes than the buffer holds
`String.from(bytes)` is the FIRST door arbitrary bytes reach the UTF-8 decoders through: every earlier
producer of a `String` is well-formed by construction (a literal is lexer-validated, a slice is
grapheme-aligned, `replace`/`split` rebuild from valid pieces). A truncated lead therefore used to make
`__utf8_cp_at` read the byte PAST the allocation — measured, and it answered rather than faulting, which
is the worst version: at the end of a slab the same read crosses a page.

**A sequence the buffer cannot complete now decodes as its own lead byte and advances ONE**, so no read
leaves the buffer. The reference produces nothing at all to match here — its `utf8DecodeAt` is
bounds-checked and `panic`s (`stdlib/helpers/string/utf8.maxon:126`, *"2-byte seq continuation out of
bounds"*) — so what is pinned is the memory-safety guarantee, not an answer copied from it. The last row
is the well-formed control: it must be untouched.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteSeq = Array with Byte

function show(label String, bytes ByteSeq)
	let s = String.from(bytes)
	var cps = ""
	for c in s.codepoints() 'eachCp'
		cps = "{cps}{c},"
	end 'eachCp'
	var units = ""
	for u in s.utf16() 'eachUnit'
		units = "{units}{u},"
	end 'eachUnit'
	print("{label} len={s.byteLength()} count={s.count()} cps=[{cps}] utf16=[{units}]\n")
end 'show'

function main() returns ExitCode
	show("lead2-alone", bytes: b"\xC3")
	show("lead3-one-continuation", bytes: b"\xE4\xB8")
	show("lead4-two-continuations", bytes: b"\xF0\x9F\x98")
	show("well-formed-3byte", bytes: b"\xE4\xB8\xAD")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
lead2-alone len=1 count=1 cps=[195,] utf16=[195,]
lead3-one-continuation len=2 count=2 cps=[228,184,] utf16=[228,184,]
lead4-two-continuations len=3 count=2 cps=[240,2008,] utf16=[240,2008,]
well-formed-3byte len=3 count=1 cps=[20013,] utf16=[20013,]
```

<!-- test: codepoint-return-is-range-checked -->
<!-- targets: x64-linux, wasm32-wasi -->
### `Codepoint` is a RANGE and not merely an erasure
`Codepoint` is `int(0 to 1114111)` in `stdlib/Character.maxon`, a module shv2 cannot load — so the
compiler declares the alias for itself rather than answering only "it erases to `integer`". Without the
range, this sibling of the case above was silent where that one panics: `utf16DecodeSurrogates` on two
non-surrogates computed **-56613888** and handed it back as a `Codepoint`, whose declared range starts
at 0. One stdlib module, one kind of precondition violation, and it must not matter which alias the
compiler happened to get from a registry and which it synthesized.
`7` is what an UNGUARDED build returns — the answer it computes is negative — so the exit code alone
separates a fired guard from the silent wrong answer, on every target.
```maxon
function main() returns ExitCode
	let cp = utf16DecodeSurrogates(0, low: 0)
	return 7 if cp < 0 else 9
end 'main'
```
```exitcode
1
```

<!-- test: codepoint-range-panic-names-the-alias -->
<!-- targets: x64-windows -->
### …and the panic names `Codepoint`, the alias the compiler declared for itself
The message half of the case above — and the one that shows the alias reaching the diagnostic under
its own name, which is what a bare erasure to `integer` could never have produced.
```maxon
function main() returns ExitCode
	let cp = utf16DecodeSurrogates(0, low: 0)
	return 7 if cp < 0 else 9
end 'main'
```
```exitcode
1
```
```stderr
panic at utf16.maxon:67: Range check failed: value outside typealias 'Codepoint'
Stack trace:
  in utf16DecodeSurrogates
  in main
  in mrt_start
```

<!-- test: ranged-codepoint-alias-stays-legal -->
### A RANGED `typealias Codepoint` is still legal, exactly as one over `ExitCode` is
The carve-out is the same one every compiler-owned name gets: a ranged alias mints no nominal identity,
it erases to the same `integer` the builtin does, so the two answers agree and there is nothing to refuse.
```maxon
typealias Codepoint = int(0 to 1114111)

function main() returns ExitCode
	let c = 128512 as Codepoint
	return c - 128512
end 'main'
```
```exitcode
0
```

<!-- test: a-byte-read-back-through-a-literal-is-still-a-byte -->
### An element read out of an `Array with Byte` rebuilds an `Array with Byte`
An array literal infers its instance from its first element's TYPE, so a `get` that erased its element
to a bare `int` made `[b.get(0), …]` an eight-byte-strided `Array with integer` — and the byte array
could not be rebuilt from its own elements. `String.from` is what makes the stride observable.
```maxon
function main() returns ExitCode
	let src = b"Hi!"
	let x = try src.get(0) otherwise 0
	let y = try src.get(1) otherwise 0
	let z = try src.get(2) otherwise 0
	print("{String.from([x, y, z])}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Hi!
```

<!-- test: a-user-ranged-alias-element-keeps-its-name -->
### The same rule for a USER-declared ranged alias, which is the arm the byte case never reaches
The case above rides `emitArrayElementAccessor`'s trivial arm; the element type of a compiler-minted
`Array with Byte` is not a `named` type, so it never reaches `arrayElementValueType`'s ranged-alias arm.
A `typealias` the AUTHOR wrote does — and without that arm the element erases to a bare `int`, so
`[x, y]` builds an eight-byte-strided `Array with integer` and `String.from` FALSE-REJECTS a program
both references accept (measured: `E3005 requires a Array with Byte, but its argument is Array_int`).
```maxon
typealias Octet = int(0 to u8.max)
typealias OctetArray = Array with Octet

function main() returns ExitCode
	var a = OctetArray.create()
	a.push(72)
	a.push(105)
	let x = try a.get(0) otherwise 0
	let y = try a.get(1) otherwise 0
	print("{String.from([x, y])}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Hi
```
