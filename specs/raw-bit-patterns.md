---
feature: raw-bit-patterns
status: stable
keywords: [bits, typealias, range-check, cast, unsigned, pattern, quantity]
category: types
---
# `bits(n)` — a raw bit pattern, and what separates it from a quantity

## Documentation

`typealias Hash = bits(64)` declares a **PATTERN**: a value is nothing but its bits, so every value
`n` bits can hold is one of its values. `typealias Count = int(0 to u64.max)` declares a **QUANTITY**
that happens to order unsigned. The two are the same VALUE SET and the same eight bytes, and they
differ in exactly one thing — **what a door does with a value that arrived from a SIGNED domain.**

- A quantity's door **refuses** it. A negative is not a quantity, whatever its bits would be read as,
  so `opaque(-1) as Count` panics rather than becoming 18446744073709551615.
- A pattern's door **admits** it, because at 64 bits there is nothing to test. An address, a hash, a
  mask and a wrapped relocation displacement all legitimately set bit 63.

⭐ **THAT IS THE WHOLE REASON `bits(n)` EXISTS.** Without it, one spelling would mean both things and the
unguarded reading would win, so a quantity typed `int(0 to u64.max)` would silently absorb an underflow. The
two spellings are separate and each says which it is.

### The legal widths are 1, 2, 4, 8, 16, 32 and 64 — and they are not an arbitrary list

They are exactly the widths a slot or a sub-byte packed field can hold: 8/16/32/64 are the 1/2/4/8-byte
slots the storage ladder produces, and 1/2/4 are the packed widths that DIVIDE a byte, so a field never
straddles one. A width outside that set has nowhere to be stored, and is **E3148**.

### `bits` is an ordinary identifier, not a keyword

The lexer is untouched — `bits` and `word` stay usable as variable names throughout a corpus. What
makes a declaration a raw pattern is the name followed by `(`, so a nominal `typealias X = bits` is
read as a name and refused as one.

### ⚠ The two casts that are deliberately silent

`bits(64) as Integer` reinterprets: a bit-63 pattern READS as a negative number and no guard fires.
This is a decision, not a gap — the bits do not move, every 64-bit pattern is a legal signed number,
and a guard would have nothing to reject. The same holds in reverse for `Count as Integer`. Both are
pinned below so the decision is on the record.

## Tests

<!-- test: a-whole-word-pattern-cast-to-a-signed-alias-reinterprets -->
### `bits(64) as Integer` keeps the pattern and reads negative
The deliberate reinterpretation. `u64.max` and `-1` are one 64-bit pattern; naming it under a signed
alias changes what it is CALLED and not one bit of what it IS.
⚠ The claim is asked as a COMPARISON rather than as a printed digit string on purpose: how a value is
SPELLED is `string-interpolation.md`'s subject, and this case is about what the bits are.
```maxon
typealias Word = bits(64)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
	return n
end 'opaque'

function main() returns ExitCode
	let w = u64.max as Word
	let minusOne = opaque(0) - 1
	return 7 if (w as Integer) == minusOne else 1
end 'main'
```
```exitcode
7
```

<!-- test: an-unsigned-quantity-cast-to-a-signed-alias-reinterprets -->
### `int(0 to u64.max) as Integer` reinterprets too
Unsigned → signed crosses no guard in either spelling: the target admits every pattern, so there is
nothing a check could refuse.
```maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
	return n
end 'opaque'

function main() returns ExitCode
	let c = u64.max as Count
	let minusOne = opaque(0) - 1
	return 7 if (c as Integer) == minusOne else 1
end 'main'
```
```exitcode
7
```

<!-- test: a-whole-word-pattern-reaches-an-unsigned-quantity-unguarded -->
### `bits(64) as int(0 to u64.max)` is free
Pattern → quantity crosses no signed domain and no width, so the quantity's door has nothing to test
even though it is a guarded door in general.
```maxon
typealias Word = bits(64)
typealias Count = int(0 to u64.max)

function main() returns ExitCode
	let w = u64.max as Word
	print("{w as Count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551615
```

<!-- test: a-signed-negative-reaches-a-whole-word-pattern-unguarded -->
### `Integer as bits(64)` is free, and keeps the pattern
The direction that separates a pattern from a quantity most sharply: the SAME `-1` this file's next
case panics on is admitted here, because every pattern is one of `bits(64)`'s values.
```maxon
typealias Word = bits(64)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
	return n
end 'opaque'

function main() returns ExitCode
	let laundered = opaque(0) - 1
	let extreme = u64.max as Word
	return 7 if (laundered as Word) == extreme else 1
end 'main'
```
```exitcode
7
```

<!-- test: a-signed-negative-into-an-unsigned-quantity-panics -->
### ⭐ THE THESIS — a signed negative into an unsigned QUANTITY panics
The same value, the same value set, the same eight bytes as the case above, and the opposite verdict —
because `Count` says it holds a QUANTITY. `opaque` launders the `-1` past the compile-time refusal so
the RUNTIME door is what answers.
```maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
	return n
end 'opaque'

function main() returns ExitCode
	let c = opaque(-1) as Count
	print("{c}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-signed-negative-into-an-unsigned-quantity-panics.test:10: Range check failed: value outside typealias 'Count'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-narrow-pattern-widens-with-no-check -->
### `bits(32) as bits(64)` is a widening
```maxon
typealias Half = bits(32)
typealias Word = bits(64)

function main() returns ExitCode
	let h = 0xDEADBEEF as Half
	print("{h as Word}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3735928559
```

<!-- test: a-whole-word-pattern-narrowed-to-32-bits-panics -->
### `bits(64) as bits(32)` panics — the WIDTH is the door
Truncation is never implicit. A caller who wants the low 32 bits writes the mask.
⚠ The pattern is laundered through `opaque` so the value arrives at RUN TIME: a folded one is refused
before the program runs, which is a different rule (E3005) reported by a different case.
```maxon
typealias Word = bits(64)
typealias Half = bits(32)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
	return n
end 'opaque'

function main() returns ExitCode
	let w = (opaque(0) - 1) as Word
	print("{w as Half}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-whole-word-pattern-narrowed-to-32-bits-panics.test:12: Range check failed: value outside typealias 'Half'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-signed-negative-into-a-narrow-pattern-panics -->
### `Integer as bits(32)` panics — `-1` does not fit 32 bits
A pattern narrower than a word is guarded like any other narrow range, so the unguardedness of
`bits(64)` is a property of the WIDTH and not of the spelling.
```maxon
typealias Half = bits(32)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
	return n
end 'opaque'

function main() returns ExitCode
	let h = opaque(-1) as Half
	print("{h}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-signed-negative-into-a-narrow-pattern-panics.test:10: Range check failed: value outside typealias 'Half'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-whole-word-pattern-into-a-signed-nonnegative-alias-panics -->
### `bits(64)` into a range that stops at `i64.max` panics on bit 63
The control that a pattern is not waved into every target: `Small` genuinely excludes the value, and
the ordinary containment check is what refuses it.
```maxon
typealias Word = bits(64)
typealias Small = int(0 to i64.max)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
	return n
end 'opaque'

function main() returns ExitCode
	let w = (opaque(0) - 1) as Word
	print("{w as Small}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-whole-word-pattern-into-a-signed-nonnegative-alias-panics.test:12: Range check failed: value outside typealias 'Small'
Stack trace:
  in main
  in mrt_start
```

<!-- test: an-unsigned-quantity-parameter-admits-an-unsigned-bit-63-value -->
### An unsigned QUANTITY carries a bit-63 value through a door
`Count`'s declared value set reaches `u64.max`, and `u64.max as Count` is accepted, because a written
`u64.max` carries no sign. A door cannot tell a laundered negative from a value above `i64.max` by
its 64 bits, so it decides by the value's STATIC type instead: a source whose type is signed is
checked for a negative, and a source whose type is already unsigned — `Count` itself here — is
admitted with nothing to test. A parameter's check therefore stands at the CALL, where the argument's
type is known, and not at the callee's entry.
```maxon
typealias Count = int(0 to u64.max)

function take(c Count) returns Count
	return c
end 'take'

function main() returns ExitCode
	let extreme = u64.max as Count
	print("{take(extreme)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551615
```

<!-- test: error.a-written-negative-into-a-pattern-is-refused -->
### A WRITTEN `-1` is still E3005, even where the pattern would hold it
The compile-time rule is about what the author wrote, not about what the bits would do: a declared
lower bound of 0 refuses a negative literal. `u64.max` and `0xFFFFFFFFFFFFFFFF` are how the same
pattern is spelled without writing a sign, and both are admitted.
⚠ The message names the alias's OWN spelling: `bits(64)`, not the `int(0 to 18446744073709551615)`
its stored bounds render as — a declaration the program does not contain.
```maxon
typealias Word = bits(64)

function main() returns ExitCode
	let w = -1 as Word
	print("{w}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/raw-bit-patterns/error.a-written-negative-into-a-pattern-is-refused.maxon:5:13: Value -1 is outside the range of 'Word' (bits(64))
```

<!-- test: error.an-illegal-width-is-refused -->
### E3148 — a width the language does not have
```maxon
typealias Odd = bits(5)

function main() returns ExitCode
	let x = 1 as Odd
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3148: specs/raw-bit-patterns/error.an-illegal-width-is-refused.maxon:2:22: 'bits(5)' is not a legal width: bits(n) takes 1, 2, 4, 8, 16, 32 or 64 — the widths a slot or a sub-byte packed field can hold
```

<!-- test: error.a-zero-width-is-refused -->
### E3148 — `bits(0)` holds no values at all
```maxon
typealias Nothing = bits(0)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3148: specs/raw-bit-patterns/error.a-zero-width-is-refused.maxon:2:26: 'bits(0)' is not a legal width: bits(n) takes 1, 2, 4, 8, 16, 32 or 64 — the widths a slot or a sub-byte packed field can hold
```

<!-- test: error.a-width-past-the-machine-word-is-refused -->
### E3148 — `bits(128)` is wider than any slot
```maxon
typealias Huge = bits(128)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3148: specs/raw-bit-patterns/error.a-width-past-the-machine-word-is-refused.maxon:2:23: 'bits(128)' is not a legal width: bits(n) takes 1, 2, 4, 8, 16, 32 or 64 — the widths a slot or a sub-byte packed field can hold
```

<!-- test: error.bare-bits-is-not-a-type -->
### `bits` alone denotes nothing
The half of "an ordinary identifier, not a keyword" that has to be pinned: without a width there is
no declaration here, so the name reaches the ordinary unknown-type path.
```maxon
function main() returns ExitCode
	let x = 1 as bits
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3011: specs/raw-bit-patterns/error.bare-bits-is-not-a-type.maxon:3:12: Unknown type 'bits'
```

<!-- test: error.a-cast-to-a-values-own-pattern-alias-is-unneeded -->
### E3010 is unchanged — a pattern is an alias like any other
```maxon
typealias Word = bits(64)

function main() returns ExitCode
	let w = 1 as Word
	print("{w as Word}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3010: specs/raw-bit-patterns/error.a-cast-to-a-values-own-pattern-alias-is-unneeded.maxon:6:12: unneeded cast: 'Word' already fits in 'Word'
```

<!-- test: every-width-rides-the-storage-ladder -->
### Storage is the existing ladder — 8/4/2/1 bytes, then 4/2/1 packed bits
`bits(n)` adds no storage rule of its own: it lands on the same ladder a ranged `int` of the same
value set does. A negative `elementSize` is the ladder's spelling for a SUB-BYTE packed element,
and it is why the legal widths divide a byte.
```maxon
typealias W64 = bits(64)
typealias W32 = bits(32)
typealias W16 = bits(16)
typealias W8 = bits(8)
typealias W4 = bits(4)
typealias W2 = bits(2)
typealias W1 = bits(1)
typealias A64 = Array with W64
typealias A32 = Array with W32
typealias A16 = Array with W16
typealias A8 = Array with W8
typealias A4 = Array with W4
typealias A2 = Array with W2
typealias A1 = Array with W1

function main() returns ExitCode
	let a64 = A64.create()
	let a32 = A32.create()
	let a16 = A16.create()
	let a8 = A8.create()
	let a4 = A4.create()
	let a2 = A2.create()
	let a1 = A1.create()
	print("{a64.managed.elementSize()} {a32.managed.elementSize()} {a16.managed.elementSize()} {a8.managed.elementSize()}\n")
	print("{a4.managed.elementSize()} {a2.managed.elementSize()} {a1.managed.elementSize()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 4 2 1
-4 -2 -1
```

<!-- test: a-pattern-and-the-range-with-its-value-set-share-a-slot -->
### `bits(4)` and `int(0 to 15)` are one slot
The control for the case above: the ladder is asked about the VALUE SET, so the two spellings cannot
drift apart in layout. What separates them is the door, and nothing else.
```maxon
typealias W4 = bits(4)
typealias N15 = int(0 to 15)
typealias A4 = Array with W4
typealias A15 = Array with N15

function main() returns ExitCode
	let a = A4.create()
	let b = A15.create()
	print("{a.managed.elementSize()} {b.managed.elementSize()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-4 -4
```

<!-- test: a-compiler-minted-machine-word-reaches-an-unsigned-quantity-unguarded -->
### A word the COMPILER minted crosses the same door as one the SOURCE named
`bits(64) as int(0 to u64.max)` is free, and the case above pins it for a word a program declared. This
case asks it of the other kind of word: one no source line gave a type to, minted by the compiler as the
result of a `__Raw` machine-word operation. Pattern → quantity crosses no signed domain and no width
whichever end minted the value, so the quantity's door has nothing to test in either case, and a sign-bit
test plus an `__rc_panic` on this one would be a guard on an address- or bitmap-shaped word that can set
bit 63 in the ordinary course of its work — in a tier body that has no caller to report a panic to.

`__Raw` may only be spelled inside a runtime-tier file, so the probe is staged as one; `main` is an
ordinary program and does not call it.

⚠ **WHAT THIS CASE CAN AND CANNOT SAY.** Nothing reaches `probeLoadedCount`, so dead-function elimination
drops it and no instruction of it survives into the binary. The case therefore measures that the program
PARSES and LOWERS — that the cast is accepted at all — and it passes whether or not the guard is
emitted. It is not the evidence for the guard's absence. **The emitted code is**: the emitted
sequence for this probe is where a sign-bit test and an `__rc_panic` are either present or gone.
```maxon
// --- runtime-file: Probe.maxon
typealias ProbeCount = int(0 to u64.max)

let ProbeScratchBytes = 8

function probeLoadedCount() returns ProbeCount
	let addr = __Raw.scratch(ProbeScratchBytes)
	__Raw.storeWord(addr, offset: 0, value: 7 as MachineWord)

	return __Raw.loadWord(addr, offset: 0) as ProbeCount
end 'probeLoadedCount'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-word-sourced-value-is-free-at-a-return-door-and-at-a-binding -->
### The `bits(64)`-SOURCED route, one door at a time — the two that are FREE
`bits(64) as int(0 to u64.max)` is free, so a value that arrived through a `Word` reaches a `Count`
RETURN and a `Count` BINDING with nothing to test, bit 63 set and all. Both doors read the pattern the
`Word` already held and print it as the unsigned quantity it now denotes. The case below is the same
value at the remaining door.
```maxon
typealias Word = bits(64)
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
	return n
end 'opaque'

function throughReturn(n Integer) returns Count
	return n as Word as Count
end 'throughReturn'

function throughBinding(n Integer) returns Count
	let c = n as Word as Count
	return c
end 'throughBinding'

function main() returns ExitCode
	let laundered = opaque(0) - 3
	print("{throughReturn(laundered)}\n")
	print("{throughBinding(laundered)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551613
18446744073709551613
```

<!-- test: a-word-sourced-value-is-free-at-an-unsigned-quantity-parameter -->
### …and the ARGUMENT door is free too
The same route at a parameter. The argument's static type is `Count`, which is unsigned, so the call
has no sign to test and the value arrives as the quantity 18446744073709551613.
```maxon
typealias Word = bits(64)
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
	return n
end 'opaque'

function show(c Count) returns Count
	return c
end 'show'

function main() returns ExitCode
	let laundered = opaque(0) - 3
	print("{show(laundered as Word as Count)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551613
```

<!-- test: a-full-unsigned-quantity-parameter-admits-u64-max -->
### An unsigned source reaches an unsigned-quantity PARAMETER with nothing to test
```maxon
typealias WidePid = int(0 to u64.max)

function show(p WidePid) returns WidePid
	return p
end 'show'

function main() returns ExitCode
	print("{show(u64.max as WidePid)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551615
```

<!-- test: a-full-unsigned-quantity-return-admits-a-shifted-top-bit -->
### An unsigned shift result reaches an unsigned-quantity RETURN with nothing to test
```maxon
typealias Count = int(0 to u64.max)

function topBit(one Count) returns Count
	return one shl 63
end 'topBit'

function main() returns ExitCode
	print("{topBit(1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9223372036854775808
```

<!-- test: a-full-unsigned-quantity-store-admits-a-shifted-top-bit -->
### An unsigned shift result reaches an unsigned-quantity FIELD with nothing to test
```maxon
typealias Count = int(0 to u64.max)

type Tally
	export var total as Count

	static function create(one Count) returns Tally
		var t = Self{total: one shl 62}
		t.total = one shl 63
		return t
	end 'create'
end 'Tally'

function main() returns ExitCode
	print("{Tally.create(1).total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9223372036854775808
```

<!-- test: a-dynamic-shift-count-carries-an-unsigned-quantity-into-bit-63 -->
### A shift by a count the compiler cannot fold still lands an unsigned quantity in bit 63
The count is a parameter, so the shift is emitted behind the saturating mask — and the result's domain
is the LEFT operand's, which the count cannot change.
```maxon
typealias Word = int(0 to u64.max)

function shifted(value Word, by Word) returns Word
	return value shl by
end 'shifted'

function main() returns ExitCode
	print("{shifted(0xAB, by: 56):x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ab00000000000000
```

<!-- test: a-product-and-an-or-reach-the-same-bit-63-unsigned-value -->
### A product and an `or` reach the same bit-63 value with nothing to test
The two controls for the shift above: the identical pattern, produced by two other operations closed
over the unsigned domain.
```maxon
typealias Word = int(0 to u64.max)

function multiplied(value Word, by Word) returns Word
	return value * by
end 'multiplied'

function orred(low Word, high Word) returns Word
	return low or high
end 'orred'

function main() returns ExitCode
	print("{multiplied(0xAB, by: 0x100000000000000):x}\n")
	print("{orred(1, high: 0x8000000000000000):x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ab00000000000000
8000000000000001
```

<!-- test: an-accumulation-in-a-loop-carries-a-bit-63-unsigned-value -->
### An accumulation inside a loop carries a bit-63 unsigned value out of it
```maxon
typealias Word = int(0 to u64.max)
typealias Lap = int(0 to 8)

function looped(seed Word, high Word, laps Lap) returns Word
	var word = seed

	for _ in 0 upto laps 'eachLap'
		word = word or high
	end 'eachLap'

	return word
end 'looped'

function main() returns ExitCode
	print("{looped(1, high: 0x8000000000000000, laps: 2):x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8000000000000001
```

<!-- test: bytes-assembled-in-a-loop-fill-an-unsigned-quantity-word -->
### Bytes assembled in a loop fill an unsigned-quantity word, top byte included
The shape any code building a machine word out of bytes has: a loop-carried accumulator, a shift by a
computed count, and a last byte at or above 0x80.
```maxon
typealias Word = int(0 to u64.max)
typealias Fill = int(1 to 7)

function packed(bytes ByteArray) returns Word
	var word = 0 as Word

	for (iter, value) in bytes.withIterator() 'eachByte'
		word = word or ((value as Word) shl ((iter.index() as Word) * 8))
	end 'eachByte'

	return word
end 'packed'

function main() returns ExitCode
	var bytes = ByteArray.create()

	for i in 1 upto 8 'eachLowByte'
		bytes.push((i as Fill) as Byte)
	end 'eachLowByte'

	bytes.push(0xCC as Byte)

	print("{packed(bytes):x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
cc07060504030201
```

<!-- test: a-shift-past-a-narrower-unsigned-ceiling-is-still-refused -->
### A shift past a narrower unsigned ceiling is still refused
The admission is the FULL-unsigned range's alone. An alias with a real upper bound keeps both of its
checks, whichever operation produced the value.
```maxon
typealias Mask = int(0 to 65535)

function shifted(value Mask, by Mask) returns Mask
	return value shl by
end 'shifted'

function main() returns ExitCode
	print("{shifted(1, by: 20)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-shift-past-a-narrower-unsigned-ceiling-is-still-refused.test:5: Range check failed: value outside typealias 'Mask'
Stack trace:
  in shifted
  in main
  in mrt_start
```

<!-- test: a-loop-carried-signed-accumulator-is-still-refused-at-an-unsigned-quantity-return -->
### A loop-carried SIGNED accumulator is still refused at an unsigned-quantity return
What the merge admits is decided by the domain it was DECLARED over, so a signed accumulator keeps its
underflow check however many times the loop turns.
```maxon
typealias Word = int(0 to u64.max)
typealias Counter = int(i64.min to i64.max)
typealias Lap = int(0 to 4)

function drained(seed Counter, laps Lap) returns Word
	var total = seed

	for _ in 0 upto laps 'eachLap'
		total = total - 1
	end 'eachLap'

	return total
end 'drained'

function main() returns ExitCode
	print("{drained(0, laps: 2)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-loop-carried-signed-accumulator-is-still-refused-at-an-unsigned-quantity-return.test:13: Range check failed: value outside typealias 'Word'
Stack trace:
  in drained
  in main
  in mrt_start
```

<!-- test: a-signed-counter-argument-to-an-unsigned-quantity-parameter-panics -->
### A signed source still meets the underflow check, at the CALL
A counter over a signed interval is a signed value with no alias, so it reaches a `Count` parameter
without a cast, and the call is where its sign is known.
```maxon
typealias Count = int(0 to u64.max)

function show(c Count) returns Count
	return c
end 'show'

function main() returns ExitCode
	for i in -2 upto 1 'each'
		print("{show(i)}\n")
	end 'each'
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-signed-counter-argument-to-an-unsigned-quantity-parameter-panics.test:10: Range check failed: value outside typealias 'Count'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-signed-counter-through-a-function-value-to-an-unsigned-quantity-parameter-panics -->
### …and at a call through a FUNCTION VALUE, where the callee keeps its entry guard
A function taken as a value can be called from anywhere, so no set of call sites is known to have
checked its argument, and the callee guards itself.
```maxon
typealias Count = int(0 to u64.max)

function show(c Count) returns Count
	return c
end 'show'

function main() returns ExitCode
	let f = show

	for i in -2 upto 1 'each'
		print("{f(i)}\n")
	end 'each'
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-signed-counter-through-a-function-value-to-an-unsigned-quantity-parameter-panics.test:4: Range check failed: value outside typealias 'Count'
Stack trace:
  in show
  in main
  in mrt_start
```

<!-- test: a-narrower-unsigned-topped-parameter-still-refuses-below-its-floor -->
### A narrower unsigned-topped range keeps its entry guard
```maxon
typealias AboveTen = int(10 to u64.max)

function show(c AboveTen) returns AboveTen
	return c
end 'show'

function main() returns ExitCode
	print("{show(u64.max as AboveTen)}\n")

	for i in 5 upto 6 'each'
		print("{show(i)}\n")
	end 'each'
	return 0
end 'main'
```
```exitcode
1
```
```stdout
18446744073709551615
```
```stderr
panic at a-narrower-unsigned-topped-parameter-still-refuses-below-its-floor.test:4: Range check failed: value outside typealias 'AboveTen'
Stack trace:
  in show
  in main
  in mrt_start
```

<!-- test: a-subtraction-on-an-unsigned-quantity-argument-panics-at-the-call -->
### A subtraction on an unsigned quantity can go below 0, so it is checked at the call
`c - 5` wears `Count`, but a subtraction is not a source that cannot go negative. Only an unsigned value
itself, or a shift, `and`, `or`, `+` or `*` of such values, reaches a full-unsigned quantity unchecked.
```maxon
typealias Count = int(0 to u64.max)

function show(c Count) returns Count
	return c
end 'show'

function lessFive(c Count) returns Count
	return show(c - 5)
end 'lessFive'

function main() returns ExitCode
	print("{lessFive(3)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-subtraction-on-an-unsigned-quantity-argument-panics-at-the-call.test:9: Range check failed: value outside typealias 'Count'
Stack trace:
  in lessFive
  in main
  in mrt_start
```

<!-- test: a-mixed-overload-set-guards-at-the-call-against-the-resolved-member -->
### An overload set whose members disagree at the slot is checked against the member the call resolves to
```maxon
typealias Count = int(0 to u64.max)

function show(c Count) returns Count
	return c
end 'show'

function show(s String) returns Count
	print(s)
	return 0
end 'show'

function main() returns ExitCode
	for i in -2 upto 1 'each'
		print("{show(i)}\n")
	end 'each'
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-mixed-overload-set-guards-at-the-call-against-the-resolved-member.test:15: Range check failed: value outside typealias 'Count'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-signed-counter-through-an-interface-to-an-unsigned-quantity-parameter-panics -->
### …and at a call through an INTERFACE, where the conformer keeps its entry guard
```maxon
typealias Count = int(0 to u64.max)

interface Shower
	function show(c Count) returns Count
end 'Shower'

type Plain
	export var tag as Count

	static function create() returns Plain
		return Self{tag: 0}
	end 'create'
end 'Plain'

extension Plain implements Shower
	function show(c Count) returns Count
		return c + self.tag
	end 'show'
end 'Plain'

function drive(s Shower) returns ExitCode
	for i in -2 upto 1 'each'
		print("{s.show(i)}\n")
	end 'each'
	return 0
end 'drive'

function main() returns ExitCode
	return drive(Plain.create())
end 'main'
```
```exitcode
1
```
```stderr
panic at a-signed-counter-through-an-interface-to-an-unsigned-quantity-parameter-panics.test:17: Range check failed: value outside typealias 'Count'
Stack trace:
  in Plain.show
  in drive
  in main
  in mrt_start
```

<!-- test: a-signed-counter-into-an-async-unsigned-quantity-parameter-panics -->
### …and at an `async` call, where the callee keeps its entry guard
```maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

function half(n Count) returns Integer
	sleep(1)
	return (n shr 1) as Integer
end 'half'

function main() returns ExitCode
	for i in -2 upto 1 'each'
		let p = async half(i)
		print("{await p}\n")
	end 'each'
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-signed-counter-into-an-async-unsigned-quantity-parameter-panics.test:5: Range check failed: value outside typealias 'Count'
Stack trace:
  in half
  in __gt_trampoline
```

<!-- test: a-signed-counter-through-an-associated-type-to-an-unsigned-quantity-parameter-panics -->
### …and at a call through an interface whose formal is an ASSOCIATED TYPE, where the conformer keeps its entry guard
```maxon
typealias Count = int(0 to u64.max)

interface Shower uses Shown
	function show(c Shown) returns Shown
end 'Shower'

type Plain implements Shower with Count
	export var tag as Count

	static function create() returns Plain
		return Self{tag: 0}
	end 'create'

	function show(c Count) returns Count
		return c + self.tag
	end 'show'
end 'Plain'

function drive(s Shower) returns ExitCode
	for i in -2 upto 1 'each'
		print("{s.show(i)}\n")
	end 'each'
	return 0
end 'drive'

function main() returns ExitCode
	return drive(Plain.create())
end 'main'
```
```exitcode
1
```
```stderr
panic at a-signed-counter-through-an-associated-type-to-an-unsigned-quantity-parameter-panics.test:15: Range check failed: value outside typealias 'Count'
Stack trace:
  in Plain.show
  in drive
  in main
  in mrt_start
```

<!-- test: a-folded-negative-argument-to-an-unsigned-quantity-parameter-panics-at-the-call -->
### A folded negative is a signed source, refused at the call
`0 - 1` folds to a constant, and a subtraction can go below 0, so the constant is the negative
number it denotes rather than the pattern `u64.max`.
```maxon
typealias Count = int(0 to u64.max)

function show(c Count) returns Count
	return c
end 'show'

function main() returns ExitCode
	print("{show(0 - 1)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-folded-negative-argument-to-an-unsigned-quantity-parameter-panics-at-the-call.test:9: Range check failed: value outside typealias 'Count'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-folded-negative-from-a-local-to-an-unsigned-quantity-parameter-panics-at-the-call -->
### …and so is one folded through a local
```maxon
typealias Count = int(0 to u64.max)

function show(c Count) returns Count
	return c
end 'show'

function main() returns ExitCode
	let d = 2
	print("{show(d - 3)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-folded-negative-from-a-local-to-an-unsigned-quantity-parameter-panics-at-the-call.test:10: Range check failed: value outside typealias 'Count'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-folded-negative-returned-as-an-unsigned-quantity-panics -->
### …and at a return
```maxon
typealias Count = int(0 to u64.max)

function below() returns Count
	let d = 2
	return d - 3
end 'below'

function main() returns ExitCode
	print("{below()}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-folded-negative-returned-as-an-unsigned-quantity-panics.test:6: Range check failed: value outside typealias 'Count'
Stack trace:
  in below
  in main
  in mrt_start
```

<!-- test: a-folded-negative-stored-as-an-unsigned-quantity-panics -->
### …and at a store
```maxon
typealias Count = int(0 to u64.max)

type Tally
	export var total as Count

	static function create() returns Tally
		let d = 2
		return Self{total: d - 3}
	end 'create'
end 'Tally'

function main() returns ExitCode
	print("{Tally.create().total}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-folded-negative-stored-as-an-unsigned-quantity-panics.test:9: Range check failed: value outside typealias 'Count'
Stack trace:
  in Tally.create
  in main
  in mrt_start
```

<!-- test: a-folded-negative-into-an-async-unsigned-quantity-parameter-panics-at-the-entry -->
### …and at an `async` call, where the callee's entry guard refuses it
```maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

function half(n Count) returns Integer
	sleep(1)
	return (n shr 1) as Integer
end 'half'

function main() returns ExitCode
	let p = async half(0 - 1)
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-folded-negative-into-an-async-unsigned-quantity-parameter-panics-at-the-entry.test:5: Range check failed: value outside typealias 'Count'
Stack trace:
  in half
  in __gt_trampoline
```

<!-- test: folded-unsigned-constants-reach-an-unsigned-quantity-parameter -->
### A constant folded from unsigned operands is the unsigned value it names, at a PARAMETER
A folded constant takes its sign from how it was produced: unsigned operands give the exact unsigned
value, whatever its bit pattern, and a literal written above `i64.max` is unsigned too.
```maxon
typealias Count = int(0 to u64.max)

function take(c Count) returns Count
	return c
end 'take'

function main() returns ExitCode
	print("{take(u64.max - 1)}\n")
	print("{take(u64.max xor 1)}\n")
	print("{take((u64.max as Count) / 1)}\n")
	print("{take(0xcbf29ce484222325 * 1)}\n")
	print("{take(0x8000000000000000 or 1)}\n")
	print("{take(0xcbf29ce484222325 xor 5)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551614
18446744073709551614
18446744073709551615
14695981039346656037
9223372036854775809
14695981039346656032
```

<!-- test: folded-unsigned-constants-reach-an-unsigned-quantity-binding -->
### …at a BINDING
```maxon
typealias Count = int(0 to u64.max)

function main() returns ExitCode
	let a = (u64.max - 1) as Count
	let b = (u64.max xor 1) as Count
	let c = ((u64.max as Count) / 1) as Count
	let d = (0xcbf29ce484222325 * 1) as Count
	let e = (0x8000000000000000 or 1) as Count
	let h = (0xcbf29ce484222325 xor 5) as Count
	print("{a} {b} {c}\n")
	print("{d} {e} {h}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551614 18446744073709551614 18446744073709551615
14695981039346656037 9223372036854775809 14695981039346656032
```

<!-- test: folded-unsigned-constants-reach-an-unsigned-quantity-field -->
### …at a STORE
```maxon
typealias Count = int(0 to u64.max)

type Tally
	export var a as Count
	export var b as Count
	export var c as Count
	export var d as Count
	export var e as Count
	export var h as Count

	static function create() returns Tally
		var t = Self{a: u64.max - 1, b: 0, c: 0, d: 0, e: 0, h: 0}
		t.b = u64.max xor 1
		t.c = (u64.max as Count) / 1
		t.d = 0xcbf29ce484222325 * 1
		t.e = 0x8000000000000000 or 1
		t.h = 0xcbf29ce484222325 xor 5
		return t
	end 'create'
end 'Tally'

function main() returns ExitCode
	let t = Tally.create()
	print("{t.a} {t.b} {t.c}\n")
	print("{t.d} {t.e} {t.h}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551614 18446744073709551614 18446744073709551615
14695981039346656037 9223372036854775809 14695981039346656032
```

<!-- test: unsigned-division-over-a-top-bit-variable-reaches-an-unsigned-quantity-parameter -->
### `/` over an unsigned value stays unsigned, at run time as at a fold
```maxon
typealias Count = int(0 to u64.max)

function take(c Count) returns Count
	return c
end 'take'

function top(one Count) returns Count
	return one shl 63
end 'top'

function main() returns ExitCode
	let a = top(1)
	print("{take(a / 1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9223372036854775808
```

<!-- test: xor-over-a-top-bit-variable-reaches-an-unsigned-quantity-parameter -->
### …and so does `xor`
```maxon
typealias Count = int(0 to u64.max)

function take(c Count) returns Count
	return c
end 'take'

function top(one Count) returns Count
	return one shl 63
end 'top'

function main() returns ExitCode
	let a = top(1)
	print("{take(a xor 1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9223372036854775809
```

<!-- test: wrapping-unsigned-folds-reach-an-unsigned-quantity-parameter -->
### Arithmetic wraps, so a fold over unsigned operands admits the wrapped value
`+`, `*` and `-` wrap at run time (two's complement), and a fold agrees with what the running program
computes: the wrapped value is what reaches the parameter.
```maxon
typealias Count = int(0 to u64.max)

function take(c Count) returns Count
	return c
end 'take'

function main() returns ExitCode
	print("{take(0xFFFFFFFFFFFFFFFF + 1)}\n")
	print("{take(0x100000000 * 0x100000000)}\n")
	print("{take(0 - 0xFFFFFFFFFFFFFFFF)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
0
1
```

<!-- test: a-signed-overflow-fold-is-refused-at-an-unsigned-quantity-parameter -->
### Two signed operands overflow into a negative, and the fold refuses it
`i64.max + 1` is signed arithmetic that wraps to `i64.min`, a negative number, not the unsigned 2^63.
```maxon
typealias Count = int(0 to u64.max)

function take(c Count) returns Count
	return c
end 'take'

function main() returns ExitCode
	print("{take(i64.max + 1)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-signed-overflow-fold-is-refused-at-an-unsigned-quantity-parameter.test:9: Range check failed: value outside typealias 'Count'
Stack trace:
  in main
  in mrt_start
```

<!-- test: signed-division-over-a-top-bit-literal-folds-as-signed -->
### `/` over a top-bit literal with no unsigned type is signed, folded or not
A literal carries no unsigned type, so `/` over it is the signed division at run time, and the fold
computes the same signed answer.
```maxon
typealias Count = int(0 to u64.max)

function take(c Count) returns Count
	return c
end 'take'

function main() returns ExitCode
	print("{take(0xFFFFFFFFFFFFFFFF / 2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: an-unsigned-subtraction-without-a-borrow-reaches-every-unsigned-quantity-door -->
### An unsigned subtraction is refused only on a real borrow, and `u64.max - 1` has none
Both operands are unsigned, so `c - 1` is the unsigned difference: it is checked for a borrow
(`c < 1`, compared unsigned), never for the sign bit of its result.
```maxon
typealias Count = int(0 to u64.max)

type Tally
	export var total as Count

	static function create(c Count) returns Tally
		var t = Self{total: 0}
		t.total = c - 1
		return t
	end 'create'
end 'Tally'

function take(c Count) returns Count
	return c
end 'take'

function viaParameter(c Count) returns Count
	return take(c - 1)
end 'viaParameter'

function viaReturn(c Count) returns Count
	return c - 1
end 'viaReturn'

function main() returns ExitCode
	print("{viaParameter(u64.max)}\n")
	print("{viaReturn(u64.max)}\n")
	print("{Tally.create(u64.max).total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551614
18446744073709551614
18446744073709551614
```

<!-- test: an-unsigned-subtraction-that-borrows-is-refused-at-a-return -->
### …and a real borrow is refused, at a return
```maxon
typealias Count = int(0 to u64.max)

function lessFive(c Count) returns Count
	return c - 5
end 'lessFive'

function main() returns ExitCode
	print("{lessFive(3)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at an-unsigned-subtraction-that-borrows-is-refused-at-a-return.test:5: Range check failed: value outside typealias 'Count'
Stack trace:
  in lessFive
  in main
  in mrt_start
```

<!-- test: an-unsigned-subtraction-that-borrows-is-refused-at-a-store -->
### …and at a store
```maxon
typealias Count = int(0 to u64.max)

type Tally
	export var total as Count

	static function create(c Count) returns Tally
		var t = Self{total: 0}
		t.total = c - 5
		return t
	end 'create'
end 'Tally'

function main() returns ExitCode
	print("{Tally.create(3).total}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at an-unsigned-subtraction-that-borrows-is-refused-at-a-store.test:9: Range check failed: value outside typealias 'Count'
Stack trace:
  in Tally.create
  in main
  in mrt_start
```

<!-- test: an-unsigned-difference-without-a-borrow-is-admitted-folded-and-at-run-time -->
### A fold and the running program give an unsigned difference one verdict: no borrow, admitted
```maxon
typealias Count = int(0 to u64.max)

function take(c Count) returns Count
	return c
end 'take'

function difference(a Count, b Count) returns Count
	return take(a - b)
end 'difference'

function main() returns ExitCode
	print("{take((u64.max as Count) - (1 as Count))}\n")
	print("{difference(u64.max, b: 1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551614
18446744073709551614
```

<!-- test: an-unsigned-difference-that-borrows-is-refused-when-folded -->
### …and a borrow is refused, folded
```maxon
typealias Count = int(0 to u64.max)

function take(c Count) returns Count
	return c
end 'take'

function main() returns ExitCode
	print("{take((0 as Count) - (1 as Count))}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at an-unsigned-difference-that-borrows-is-refused-when-folded.test:9: Range check failed: value outside typealias 'Count'
Stack trace:
  in main
  in mrt_start
```

<!-- test: an-unsigned-difference-that-borrows-is-refused-at-run-time -->
### …and at run time
```maxon
typealias Count = int(0 to u64.max)

function take(c Count) returns Count
	return c
end 'take'

function difference(a Count, b Count) returns Count
	return take(a - b)
end 'difference'

function main() returns ExitCode
	print("{difference(0, b: 1)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at an-unsigned-difference-that-borrows-is-refused-at-run-time.test:9: Range check failed: value outside typealias 'Count'
Stack trace:
  in difference
  in main
  in mrt_start
```
