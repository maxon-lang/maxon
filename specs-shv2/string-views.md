---
feature: string-views
status: experimental
keywords: [string, bytes, toByteArray, codepoints, utf16, clone, isEmpty, replaceFirst, from]
category: types
---

# String views, copies and the one `String` static

## Documentation

Three `String` methods hand back a LAZY VIEW of what the receiver holds, three hand back a COPY, and one
`String` static builds a `String` out of one:

```text
bytes()       returns ByteView
codepoints()  returns CodepointView
utf16()       returns UTF16View

toByteArray() returns Array with Byte
clone()       returns String
isEmpty()     returns bool
replaceFirst(old String, with String) returns String

String.from(bytes Array with Byte) returns String
```

Four properties are what these tests pin, and each is a decision rather than an accident:

- **⚠⚠ THE THREE VIEWS ARE LAZY, AND UNTIL W49 WAVE 6 THEY WERE NOT.** This section read *"A VIEW
  MATERIALIZES … shv2 has no `Iterable`, no associated types and no cursor protocol, so each view copies
  into the one collection shv2 does have"*, and every one of `bytes()`/`codepoints()`/`utf16()` handed
  back an `Array`. **That premise was MEASURED FALSE before a line was deleted**: `for b in
  ByteView.create(s)` already walked the corpus's own `createIterator`/`current`/`advance` and printed
  `97,98,99`. The three retired onto `stdlib/helpers/string/views.maxon`, whose views hold the `String`
  itself and read one unit per `advance()`. `.count()` and `for u in <view>` answer exactly what they
  answered before — which is what the cases below pin — but a view is **not** an `Array`, so it is
  refused at a declared `Array with Byte` position (`bytearray-element-size` holds that shut).
- **⚠ `bytes()` AND `toByteArray()` ARE NO LONGER ONE ANSWER, AND THE REFERENCE'S DISTINCTION IS THE
  REASON.** This section used to say they were, because shv2 had no lazy view and both spellings reached
  one emitter. The reference distinguishes them by laziness and by nothing else: `bytes()` is a view over
  the receiver's buffer and `toByteArray()` must COPY, because a plain view onto an OWNED buffer is a
  read-after-free the moment the owner appends (`stdlib/String.maxon:143-161`, whose `managed.slice` is
  that copy). Both are now the corpus's, each with the body its own doc describes.
- **A copy is INDEPENDENT, and that is the CONTRACT.** `clone()` may not be `return self`: the receiver's
  record would gain a second owner and the caller's drop would take the receiver's bytes with it.
  `replaceFirst`'s two no-op cases — an empty needle, and a needle that is absent — answer with a clone
  for exactly that reason. `toByteArray()` makes the same promise about its array, and
  `clone-outlives-a-growing-source` is the shape that would catch either breaking it.
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

⭐ **THE SENTENCE IS THE ORDINARY PARAMETER CHECK'S SINCE W55, AND THAT IS THE POINT.** `String.from`
had a bespoke parse door with a bespoke argument test; retiring it (`Parser.parseStringStaticCall`) left
`stdlib/String.maxon`'s own `from(bytes ByteArray)` to be met by `SemanticCheck.checkArgTypes`, the check
every other call's argument already answers to. What moved is the wording and the column — the anchor is
now the CALL rather than the argument — and what did not move is the refusal.
```maxon
function main() returns ExitCode
	let s = String.from([1, 2, 3])
	return s.byteLength()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:17: argument type mismatch for 'bytes': expected 'ByteArray', got 'Array_int'
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
error E3005: <fragment>:3:17: argument type mismatch for 'bytes': expected 'ByteArray', got 'String'
```

<!-- test: error.string-unknown-static-is-named-where-it-is-written -->
### An unknown `String` static is named where it is written
`fromOwnedBytes` is deliberately not exported by the reference ("take these bytes and trust me about
them" is not a promise the stdlib can let arbitrary code make), and there is no `String.from(codepoints)`.

⚠ **This case was named `error.string-has-exactly-one-static` until R4.2, and the rename is the point.**
The exported set became TWO when R4.2 added `init(managed)` (`stdlib/String.maxon:114`) beside
`from(bytes)` (`:119`), so a name asserting the COUNT was made false by the very rung that changed it —
while what the case actually pins never moved: an unknown static is refused **at its own span** rather
than mangled into a `String.create` callee no file declares. A test whose NAME claims one fact and whose
BODY pins another is this project's signature bug wearing a filename, and the count is the half that is
not load-bearing. ⇒ the name now says what the body checks, and it will survive the next static too.

⭐⭐ **THE SENTENCE NO LONGER NAMES A ROSTER, BECAUSE THERE IS NO LONGER A ROSTER TO NAME (W55).** It read
*"the reference provides from/init; that list IS the surface"*, rendered from the two constants the
parser's own static arms matched on — and W55 retired those arms, so `String`'s statics are now whatever
`stdlib/String.maxon` declares plus whatever an `extension String` adds. A message quoting a two-name list
would have been a copy of a surface the compiler stopped holding. What the case pins is unchanged and is
the whole of why it exists: the refusal lands on `create`, **where the author wrote it**, and names both
the type and the member.

⚠ **AND IT WENT RED WHEN THE DOOR WENT, WHICH IS WHAT MADE IT WORTH KEEPING.** With no static door the
callee mangles to `String.create`, whose result is typed `unknown` — and the parser then refuses the USE
one line later (`E2015 :4:11: a member access 'byteLength' on a 'unknown' value`), ending the file before
`SemanticCheck`'s E3004 could name the call at all. `Parser.requireCompilerOwnedStaticIsResolvable` is
the door that replaced the roster: it speaks for a base no file may declare and a member no file does,
and leaves a USER type's unknown static to SemanticCheck exactly as before.
```maxon
function main() returns ExitCode
	let s = String.create()
	return s.byteLength()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:17: Unsupported: 'String' has no static method named 'create' — the compiler owns that type name, so a static of its own can only come from the module that declares it or from an `extension` of it, and no declaration of that name exists
```

<!-- test: error.codepoint-is-a-compiler-owned-type-name -->
### A declaration may not bind `Codepoint` to a nominal identity
`Codepoint` is the compiler's own synthesized ranged int alias — `stdlib/Character.maxon` declares it
and shv2 cannot load that module — so a user `type Codepoint` would mean one thing to the parser and
another to type resolution, which is the disagreement `HashValue` was measured to cause.
```maxon
type Codepoint
	export var value as Integer
end 'Codepoint'

function main() returns ExitCode
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:2:6: Unsupported: a declaration of the type name 'Codepoint', which the compiler owns — its one meaning comes from the compiler itself or from the stdlib module that declares it, and shv2 has no namespace to tell a user declaration of the name apart from that one
```

⚠ **THE TWO RANGE-GUARD CASES BELOW WERE FOUR, IN PAIRS THAT PARTITIONED THE TARGETS, AND THE PARTITION
IS GONE BECAUSE ITS CAUSE IS (rung W3).** A range violation EXITS 1 on every target — that is the guard
FIRING — but the message and the backtrace existed only where there was a panic runtime to print them.
So each guard was pinned TWICE: once on `wasm32-wasi`, where the assertion was the exit code and stderr
had to be silent, and once on `x64-windows, x64-linux`, where the message was pinned in full. Two cases
over one program, differing in nothing but which half of the answer they could see.

⚠ **A TARGET CROSSED THAT LINE TWICE, AND BOTH TIMES THE PARTITION WAS THE THING THAT WAS WRONG.**
x64-linux moved to the message side in rung A1j (2026-07-31): it was measured silent when these cases
were written, and the reason was never the ELF lane — `mrt_panic` simply was not appended to it, while
every primitive it assembles was. wasm32-wasi moved in rung W3 for the same shape of reason: it had no
`mrt_panic` at all, and *"wasm cannot print a backtrace"* had been read as a property of the target when
it was a property of the backend. It cannot walk a frame chain — there is none — so its panic runtime
writes the message and TRAPS, and the frames come from wasmtime's own unwind, symbolized from the
module's "name" section (`StdToWasm.appendPanicRuntime`, `SpecTestRunner.wasmPanicReport`).

⇒ **The silent halves are DELETED rather than given a `stderr` block of their own**, which would have
made them byte-for-byte duplicates of their twins under a different `targets:` line. Nothing they
asserted is lost: each surviving case runs the same program, pins the same `exitcode 1` — `7` is what an
UNGUARDED build returns in both, so the exit code still separates a fired guard from a silent wrong
answer — and now pins the message everywhere as well.

⚠ **A `stderr` BLOCK STILL CANNOT BE OMITTED**: an absent block asserts the program printed NOTHING
(`SpecTestRunner.checkRunStderr`, whose `unpinned` arm says so in as many words). That is exactly how
this rung found these two cases — they went red the moment the wasm lane started speaking, which is a
gate reporting a stale premise rather than a regression.

MEASURED, and it is why these carried a target marker at all: the two guards were first written as
message-only cases with no marker, and the cross-target gate went red on x64-linux and wasm with an
EMPTY `actual` — which READS exactly like "the guard is missing on this target" and was not. The
identical program over a USER-declared `typealias Percent = int(0 to 100)`, touching neither the
whitelist nor `Codepoint`, exited 1 with empty stderr on wasm and 1 with the full message on x64.
`targets: x64-windows, x64-linux, wasm32-wasi` is now the list of lanes with a panic runtime AND a
runner on this host's gate; arm64 is synced separately by hand and is not named here for that reason.

<!-- test: whitelisted-stdlib-range-panic-names-the-alias -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
### A REACHABLE whitelisted stdlib function still gets its range guard, and the panic names the alias
`insertRangeChecks` skips a stdlib function no path from `main` reaches — that skip is what keeps the
whitelist from renumbering `.rdata` for functions nobody calls. It must not be one word wider than
that: a function the program DOES call keeps every guard it declared. `utf16LeadSurrogate` returns
through the ranged `CodeUnit16`, and a codepoint far past the supplementary plane overflows it.

`7` is what an UNGUARDED build returns — the out-of-range lead surrogate, 1031794, is comfortably past
`u16.max` — so the exit code tells a fired guard from a silent wrong answer, and the message says which
guard fired. `targets:` for the reason stated at the head of this group.

⚠ **THE CODEPOINT ARRIVES THROUGH `widen`, AND IT HAS TO.** `utf16LeadSurrogate(1000000000)` — which is
what this case read while a call argument's declared range went unenforced — is now the compile-time
E3005 `range-check-panic.md` promises at every position ("a literal argument never reaches a runtime
check because it never builds"), measured identically on the bootstrap. The subject here is the RUNTIME
guard on the whitelisted stdlib function's ranged `return`, so the codepoint must be a value no constant
folds to; `widen` is the smallest way to say that.

⭐⭐ **A1f CHANGED WHICH ALIAS THIS NAMES, AND THE NEW ANSWER IS THE VIOLATION THAT ACTUALLY HAPPENED.**
`utf16LeadSurrogate(codepoint Codepoint) returns CodeUnit16` is handed `1,000,000,000`. That is outside
`Codepoint` (`int(0 to 1114111)`) on the way IN — but the argument door owed only the compile-time half,
and the argument is a call result nothing folds, so nothing checked it. The value ran the whole body and
was caught on the way OUT by the `return`'s guard, which named `CodeUnit16` at `utf16.maxon:51`.
**The old message named the SECOND violation because the first was never checked.** The entry guard now
refuses it at `utf16.maxon:49`, naming `Codepoint` — the premise the caller actually broke, one door
earlier, before `codepoint - 65536` underflows on a value the type said could not occur. Nothing about
the program changed; the compiler simply stopped reporting the consequence in place of the cause.
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
panic at utf16.maxon:49: Range check failed: value outside typealias 'Codepoint'
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
leaves the buffer. The last row is the well-formed control: it must be untouched.

⚠⚠ **THIS PARAGRAPH SAID THE REFERENCE "produces nothing at all to match here — its `utf8DecodeAt` is
bounds-checked and `panic`s", AND THAT HAS BEEN FALSE SINCE THE CORPUS ADOPTED THIS VERY RULE.** Measured
2026-08-08 on the bootstrap oracle, same tree: it prints these four lines byte for byte.
`stdlib/helpers/string/utf8.maxon`'s `utf8WidthFits` carries the rule now and its own header cites THIS
CASE as why — *"maxon-shv2's segmenter had this rule while this file did not"* — so what was a shv2-only
memory-safety guarantee is a shared one, and the two compilers agree rather than one having no answer.
⇒ **W49 wave 6 moved `codepoints()`/`utf16()` onto that corpus code and the pinned output did not move**,
which is the strongest evidence available that the two implementations of this rule agree to the byte.
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

<!-- test: codepoint-range-panic-names-the-alias -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
### `Codepoint` is a RANGE and not merely an erasure, and the panic names it
`Codepoint` is `int(0 to 1114111)` in `stdlib/Character.maxon`, a module shv2 cannot load — so the
compiler declares the alias for itself rather than answering only "it erases to `integer`". Without the
range, this sibling of the case above was silent where that one panics: `utf16DecodeSurrogates` on two
non-surrogates computed **-56613888** and handed it back as a `Codepoint`, whose declared range starts
at 0. One stdlib module, one kind of precondition violation, and it must not matter which alias the
compiler happened to get from a registry and which it synthesized.

`7` is what an UNGUARDED build returns — the answer it computes is negative — so the exit code separates
a fired guard from the silent wrong answer, and the message shows the alias reaching the diagnostic
under its own name, which is what a bare erasure to `integer` could never have produced.
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
`[x, y]` builds an eight-byte-strided `Array with integer` instead of an `OctetArray`.

⭐⭐ **THE ASSERTION IS THE PARAMETER TYPE, AND IT USED TO BE `String.from` — WHICH WAS PINNING A WRONG
ANSWER (W55).** This case read `print("{String.from([x, y])}\n")` and claimed stdout `Hi` as *"a program
both references accept"*. **MEASURED against the runnable oracle: it prints `H`.** The bootstrap erases
the literal's element, strides eight, and hands `String.from` every eighth byte of the buffer — the exact
silent wrong answer the case above exists to forbid, committed as an expectation, and is why this one had
to stop asking `String.from` the question.

⭐⭐ **THIS PARAGRAPH ONCE ENDED "shv2 REFUSES THAT PROGRAM INSTEAD, WHICH IS THE BETTER ANSWER". SINCE R-1
IT DOES NOT REFUSE, AND ITS ANSWER IS BETTER STILL: it prints `Hi`.** Two ranged aliases over one range are
one type, so `Octet` IS `Byte` and `OctetArray` is a `ByteArray` whose element provably strides 1 — there is
nothing to refuse and nothing to truncate. See `from-admits-a-user-ranged-byte-alias` below, which pins BOTH
routes and records that the bootstrap still answers `H ` on the literal one.
⇒ **What the case pins is unchanged and is now asserted where it belongs: at the RECORD.**
`aggregatesConflict` is nominal over containers, so a `readBack(bytes OctetArray)` parameter admits an
argument whose element kept the name `Octet` and refuses an `Array_int`; reading the two elements back
THROUGH that parameter is what proves the STRIDE as well as the name. Both compilers print `177`.
```maxon
typealias Octet = int(0 to u8.max)
typealias OctetArray = Array with Octet
typealias OctetSum = int(0 to 510)

function readBack(bytes OctetArray) returns OctetSum
	let p = try bytes.get(0) otherwise 0
	let q = try bytes.get(1) otherwise 0
	return p + q
end 'readBack'

function main() returns ExitCode
	var a = OctetArray.create()
	a.push(72)
	a.push(105)
	let x = try a.get(0) otherwise 0
	let y = try a.get(1) otherwise 0
	print("{readBack([x, y])}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
177
```

<!-- test: from-admits-a-user-ranged-byte-alias -->
### `String.from` takes any element whose RANGE is a byte's
⛔⛔ **THIS CASE PINNED A REFUSAL UNTIL R-1, AND THE REASON IT GAVE WAS MEASURED FALSE.** It read *"an
`OctetArray` holds one byte per element exactly as a `ByteArray` does, and is still a DIFFERENT type …
refused on its NAME rather than admitted on its layout"*, and justified that with *"the bootstrap accepts
this program and prints a truncated string"*. **MEASURED, both compilers, this exact program: both print
`Hi`.** The truncation is real but it belongs to the array-LITERAL form in the case above, which is a
different program reaching `String.from` by a different route — the prose had imported one program's defect
into a claim about another, which is how a refusal came to be justified by a wrong answer it does not
produce.

⭐⭐ **R-1 (user ruling, 2026-08-22) SETTLES IT THE OTHER WAY: two ranged aliases over one range are ONE
type.** `Octet` is declared `int(0 to u8.max)`, which is `Byte`'s range, so `OctetArray` *is* `ByteArray` —
not a lookalike admitted on its layout, the same instance. Nominal typing over containers is untouched and
is still what refuses an `Array_int` at the `readBack` parameter above; what changed is which names denote
the same element.

⭐ **BOTH ROUTES ARE PINNED, AND THE SECOND IS ONE WHERE shv2 IS NOW RIGHT AND THE ORACLE IS WRONG.** The
pushed array and the array literal reach `String.from` differently, and the literal is the one W55 measured:
the bootstrap erases its element, strides EIGHT over a stride-1 buffer and prints `H `. shv2 prints `Hi` for
both. A case that pinned only the pushed form would not have caught that.
```maxon
typealias Octet = int(0 to u8.max)
typealias OctetArray = Array with Octet

function main() returns ExitCode
	var a = OctetArray.create()
	a.push(72)
	a.push(105)
	let x = try a.get(0) otherwise 0
	let y = try a.get(1) otherwise 0
	let pushed = String.from(a)
	let literal = String.from([x, y])
	print("pushed={pushed} literal={literal} len={pushed.byteLength()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
pushed=Hi literal=Hi len=2
```
