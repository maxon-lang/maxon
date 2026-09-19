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

⭐ **THAT IS THE WHOLE REASON `bits(n)` EXISTS.** Before it, one spelling meant both things and the
unguarded reading won, so a quantity typed `int(0 to u64.max)` silently absorbed an underflow. The
spellings are now separate and each says which it is.

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

<!-- test: an-unsigned-quantity-refuses-its-own-bit-63-values-at-a-door -->
### ⚠⚠ THE SHARP EDGE — an unsigned QUANTITY cannot carry a bit-63 value through a door
`Count`'s declared value set reaches `u64.max`, and `u64.max as Count` is accepted, because a written
`u64.max` carries no sign. But a DOOR has only the 64 bits to look at, and the bit-63 patterns a
quantity must refuse — a laundered negative — are the SAME bits as the values above `i64.max` it
would like to admit. There is no runtime information that separates them, so the door refuses both.
⇒ **A type that genuinely needs the whole word is a PATTERN, and `bits(64)` is how it says so.** This
is the one thing `int(0 to u64.max)` gives up in exchange for catching an underflow, and it is why
this file exists.
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
1
```
```stderr
panic at an-unsigned-quantity-refuses-its-own-bit-63-values-at-a-door.test:4: Range check failed: value outside typealias 'Count'
Stack trace:
  in take
  in main
  in mrt_start
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
error E3005: specs/fragments/raw-bit-patterns/error.a-written-negative-into-a-pattern-is-refused.test:5:13: Value -1 is outside the range of 'Word' (bits(64))
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
error E3148: specs/fragments/raw-bit-patterns/error.an-illegal-width-is-refused.test:2:22: 'bits(5)' is not a legal width: bits(n) takes 1, 2, 4, 8, 16, 32 or 64 — the widths a slot or a sub-byte packed field can hold
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
error E3148: specs/fragments/raw-bit-patterns/error.a-zero-width-is-refused.test:2:26: 'bits(0)' is not a legal width: bits(n) takes 1, 2, 4, 8, 16, 32 or 64 — the widths a slot or a sub-byte packed field can hold
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
error E3148: specs/fragments/raw-bit-patterns/error.a-width-past-the-machine-word-is-refused.test:2:23: 'bits(128)' is not a legal width: bits(n) takes 1, 2, 4, 8, 16, 32 or 64 — the widths a slot or a sub-byte packed field can hold
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
error E3011: specs/fragments/raw-bit-patterns/error.bare-bits-is-not-a-type.test:3:12: Unknown type 'bits'
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
error E3010: specs/fragments/raw-bit-patterns/error.a-cast-to-a-values-own-pattern-alias-is-unneeded.test:6:12: unneeded cast: 'Word' already fits in 'Word'
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
PARSES and LOWERS — that the cast is accepted at all — and it passes both before the guard is removed and
after. It is not the evidence for the guard's absence. **The committed golden fragment is**: the emitted
sequence for this probe is where a sign-bit test and an `__rc_panic` are either present or gone, and a
diff of that fragment is what reports the change.
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
value at the remaining door, and it panics.
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

<!-- test: a-word-sourced-value-still-refuses-an-unsigned-quantity-parameter -->
### ⚠⚠ …AND THE ARGUMENT DOOR REFUSES IT ANYWAY
The sharp edge above, asked of the route a `bits(64)` value takes. `Word` is where a bit-63 pattern is
legitimately at home, and `Word as Count` is free — but neither fact travels with the value. A PARAMETER
door has only the 64 bits to look at, exactly as it does for a value that never met a `Word`, so it
cannot separate this laundered `-3` from the quantity 18446744073709551613 it just printed. It refuses
both, and this program dies on the argument the one above returned and bound.
⇒ **THE SOURCE OF A VALUE NEVER RELAXES A DOOR.** A type that needs the whole word through a CALL is a
`bits(64)` parameter; converting to `int(0 to u64.max)` at the call site converts nothing away.
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
1
```
```stderr
panic at a-word-sourced-value-still-refuses-an-unsigned-quantity-parameter.test:10: Range check failed: value outside typealias 'Count'
Stack trace:
  in show
  in main
  in mrt_start
```
