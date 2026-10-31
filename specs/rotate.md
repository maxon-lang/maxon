---
feature: rotate
status: implemented
keywords: [rotate, shift, bits, shl, shr, or, xor, bitwise]
category: operators
---

# Rotate

## Documentation

Maxon has no rotate operator. A rotate is written as the two shifts it is made of, joined by `or`:

```maxon
typealias Word = bits(64)

function rotateLeftByEight(word Word) returns Word
	return (word shl 8) or (word shr 56)
end 'rotateLeftByEight'
```

The pair is a rotate when both shifts read the same `bits(64)` value and their counts sum to 64, and the compiler then emits a single rotate instruction. The answer is the same either way: every case below pins what the two shifts compute, so it holds whether or not the pair was recognised.

Only the zero-filling right shift of an unsigned value completes a rotate. A signed value's `shr` fills with the sign, so `(x shl 8) or (x shr 56)` over a signed `x` is not a rotate and stays two shifts and an `or`.

A count that is a variable is a rotate only when its declared type or recorded interval keeps both it and `64 - count` inside `1..63`. A count that could be 0 or 64 keeps the saturating shifts of [Shift Count](bitwise-operators.md#shift-count), which differ from a rotate there.

## Tests

<!-- test: a-constant-rotate-moves-the-top-bits-round -->
Rotates by 1, 8, 32 and 63 in both directions over `0x0123456789ABCDEF`. A rotate left by `k` is a rotate right by `64 - k`, so the two directions meet at 1/63 and 32/32.

`halves` compares the answer's two 32-bit halves, so no expected value has its top bit set.
```maxon
typealias Word = bits(64)

function rotateLeftBy1(word Word) returns Word
	return (word shl 1) or (word shr 63)
end 'rotateLeftBy1'

function rotateLeftBy8(word Word) returns Word
	return (word shl 8) or (word shr 56)
end 'rotateLeftBy8'

function rotateLeftBy32(word Word) returns Word
	return (word shl 32) or (word shr 32)
end 'rotateLeftBy32'

function rotateLeftBy63(word Word) returns Word
	return (word shl 63) or (word shr 1)
end 'rotateLeftBy63'

function rotateRightBy1(word Word) returns Word
	return (word shr 1) or (word shl 63)
end 'rotateRightBy1'

function rotateRightBy8(word Word) returns Word
	return (word shr 8) or (word shl 56)
end 'rotateRightBy8'

function rotateRightBy32(word Word) returns Word
	return (word shr 32) or (word shl 32)
end 'rotateRightBy32'

function rotateRightBy63(word Word) returns Word
	return (word shr 63) or (word shl 1)
end 'rotateRightBy63'

function halves(word Word, high Word, low Word) returns bool
	return (word shr 32) == high and (word and 0xFFFFFFFF) == low
end 'halves'

function main() returns ExitCode
	let value = 0x0123456789ABCDEF

	if not halves(rotateLeftBy1(value), high: 0x02468ACF, low: 0x13579BDE) 'left1'
		return 1
	end 'left1'
	if not halves(rotateLeftBy8(value), high: 0x23456789, low: 0xABCDEF01) 'left8'
		return 2
	end 'left8'
	if not halves(rotateLeftBy32(value), high: 0x89ABCDEF, low: 0x01234567) 'left32'
		return 3
	end 'left32'
	if not halves(rotateLeftBy63(value), high: 0x8091A2B3, low: 0xC4D5E6F7) 'left63'
		return 4
	end 'left63'
	if not halves(rotateRightBy1(value), high: 0x8091A2B3, low: 0xC4D5E6F7) 'right1'
		return 5
	end 'right1'
	if not halves(rotateRightBy8(value), high: 0xEF012345, low: 0x6789ABCD) 'right8'
		return 6
	end 'right8'
	if not halves(rotateRightBy32(value), high: 0x89ABCDEF, low: 0x01234567) 'right32'
		return 7
	end 'right32'
	if not halves(rotateRightBy63(value), high: 0x02468ACF, low: 0x13579BDE) 'right63'
		return 8
	end 'right63'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: a-rotate-fills-from-the-top-bit-with-zeros -->
The same rotates over `0x8000000000000001`, whose top bit is set. A right shift that filled with the sign would smear that bit across the half the rotate vacates, and these answers would carry it.
```maxon
typealias Word = bits(64)

function rotateLeftBy1(word Word) returns Word
	return (word shl 1) or (word shr 63)
end 'rotateLeftBy1'

function rotateLeftBy8(word Word) returns Word
	return (word shl 8) or (word shr 56)
end 'rotateLeftBy8'

function rotateRightBy1(word Word) returns Word
	return (word shr 1) or (word shl 63)
end 'rotateRightBy1'

function rotateRightBy8(word Word) returns Word
	return (word shr 8) or (word shl 56)
end 'rotateRightBy8'

function halves(word Word, high Word, low Word) returns bool
	return (word shr 32) == high and (word and 0xFFFFFFFF) == low
end 'halves'

function main() returns ExitCode
	let value = 0x8000000000000001

	if not halves(rotateLeftBy1(value), high: 0, low: 3) 'left1'
		return 1
	end 'left1'
	if not halves(rotateLeftBy8(value), high: 0, low: 0x180) 'left8'
		return 2
	end 'left8'
	if not halves(rotateRightBy1(value), high: 0xC0000000, low: 0) 'right1'
		return 3
	end 'right1'
	if not halves(rotateRightBy8(value), high: 0x01800000, low: 0) 'right8'
		return 4
	end 'right8'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: a-count-declared-inside-one-to-sixty-three-rotates -->
Rotates by a variable count whose type is `int(1 to 63)`, at both ends of the range and in between. The count reaches the rotate as an argument chosen at run time, so nothing about it is folded.
```maxon
typealias Word = bits(64)
typealias Step = int(0 to 3)
typealias RotateCount = int(1 to 63)

function rotateLeftBy(word Word, n RotateCount) returns Word
	return (word shl n) or (word shr (64 - n))
end 'rotateLeftBy'

function rotateRightBy(word Word, n RotateCount) returns Word
	return (word shr n) or (word shl (64 - n))
end 'rotateRightBy'

function countFor(step Step) returns RotateCount
	if step == 0 'one'
		return 1
	end 'one'
	if step == 1 'eight'
		return 8
	end 'eight'
	if step == 2 'thirtyTwo'
		return 32
	end 'thirtyTwo'

	return 63
end 'countFor'

function halves(word Word, high Word, low Word) returns bool
	return (word shr 32) == high and (word and 0xFFFFFFFF) == low
end 'halves'

function main() returns ExitCode
	let value = 0x0123456789ABCDEF

	if not halves(rotateLeftBy(value, n: countFor(0)), high: 0x02468ACF, low: 0x13579BDE) 'left1'
		return 1
	end 'left1'
	if not halves(rotateLeftBy(value, n: countFor(1)), high: 0x23456789, low: 0xABCDEF01) 'left8'
		return 2
	end 'left8'
	if not halves(rotateLeftBy(value, n: countFor(2)), high: 0x89ABCDEF, low: 0x01234567) 'left32'
		return 3
	end 'left32'
	if not halves(rotateLeftBy(value, n: countFor(3)), high: 0x8091A2B3, low: 0xC4D5E6F7) 'left63'
		return 4
	end 'left63'
	if not halves(rotateRightBy(value, n: countFor(0)), high: 0x8091A2B3, low: 0xC4D5E6F7) 'right1'
		return 5
	end 'right1'
	if not halves(rotateRightBy(value, n: countFor(1)), high: 0xEF012345, low: 0x6789ABCD) 'right8'
		return 6
	end 'right8'
	if not halves(rotateRightBy(value, n: countFor(2)), high: 0x89ABCDEF, low: 0x01234567) 'right32'
		return 7
	end 'right32'
	if not halves(rotateRightBy(value, n: countFor(3)), high: 0x02468ACF, low: 0x13579BDE) 'right63'
		return 8
	end 'right63'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: xor-of-the-two-shifts-is-the-same-rotate -->
For a count in `1..63` the two shifted halves share no bit, so joining them with `xor` gives the answer `or` gives.
```maxon
typealias Word = bits(64)

function rotateLeftByEight(word Word) returns Word
	return (word shl 8) xor (word shr 56)
end 'rotateLeftByEight'

function rotateRightByThree(word Word) returns Word
	return (word shr 3) xor (word shl 61)
end 'rotateRightByThree'

function halves(word Word, high Word, low Word) returns bool
	return (word shr 32) == high and (word and 0xFFFFFFFF) == low
end 'halves'

function main() returns ExitCode
	let value = 0x0123456789ABCDEF

	if not halves(rotateLeftByEight(value), high: 0x23456789, low: 0xABCDEF01) 'left'
		return 1
	end 'left'
	if not halves(rotateRightByThree(value), high: 0xE02468AC, low: 0xF13579BD) 'right'
		return 2
	end 'right'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shifts-of-two-different-values-are-not-a-rotate -->
`(a shl 8) or (b shr 56)` reads two values, so it takes the high bytes of `a` and the top byte of `b`: `0x23456789ABCDEF00 or 0x11`.
```maxon
typealias Word = bits(64)

function joined(a Word, b Word) returns Word
	return (a shl 8) or (b shr 56)
end 'joined'

function halves(word Word, high Word, low Word) returns bool
	return (word shr 32) == high and (word and 0xFFFFFFFF) == low
end 'halves'

function main() returns ExitCode
	if not halves(joined(0x0123456789ABCDEF, b: 0x1122334455667788), high: 0x23456789, low: 0xABCDEF11) 'joined'
		return 1
	end 'joined'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: counts-that-do-not-sum-to-64-are-not-a-rotate -->
`(word shl 8) or (word shr 55)` brings down nine bits where a rotate by 8 brings down eight, so its low byte is `0x02` where a rotate by 8 has `0x01`.
```maxon
typealias Word = bits(64)

function lookalike(word Word) returns Word
	return (word shl 8) or (word shr 55)
end 'lookalike'

function halves(word Word, high Word, low Word) returns bool
	return (word shr 32) == high and (word and 0xFFFFFFFF) == low
end 'halves'

function main() returns ExitCode
	if not halves(lookalike(0x0123456789ABCDEF), high: 0x23456789, low: 0xABCDEF02) 'lookalike'
		return 1
	end 'lookalike'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: a-signed-right-shift-fills-with-the-sign-so-it-is-not-a-rotate -->
`shr` on a signed value is arithmetic. For -2 the pair is `-512 or -1`, which is -1; a rotate left by 8 of the same 64 bits is -257.
```maxon
typealias Num = int(i64.min to i64.max)

function lookalike(x Num) returns Num
	return (x shl 8) or (x shr 56)
end 'lookalike'

function main() returns ExitCode
	if lookalike(-2) != -1 'smeared'
		return 1
	end 'smeared'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: a-count-that-can-be-zero-keeps-the-saturating-shifts -->
With `n` declared `int(0 to 63)`, `64 - n` reaches 64 and the guarded right shift saturates to 0 there. So at `n = 0` the pair is `word xor 0`, which is `word`; a rotate-by-zero of both halves under `xor` would be 0.
```maxon
typealias Word = bits(64)
typealias Reach = int(0 to 63)

function lookalike(word Word, n Reach) returns Word
	return (word shl n) xor (word shr (64 - n))
end 'lookalike'

function orLookalike(word Word, n Reach) returns Word
	return (word shl n) or (word shr (64 - n))
end 'orLookalike'

function reachFor(step Reach) returns Reach
	return (step * 8) as Reach
end 'reachFor'

function halves(word Word, high Word, low Word) returns bool
	return (word shr 32) == high and (word and 0xFFFFFFFF) == low
end 'halves'

function main() returns ExitCode
	let value = 0x0123456789ABCDEF

	if not halves(lookalike(value, n: reachFor(0)), high: 0x01234567, low: 0x89ABCDEF) 'xorZero'
		return 1
	end 'xorZero'
	if not halves(orLookalike(value, n: reachFor(0)), high: 0x01234567, low: 0x89ABCDEF) 'orZero'
		return 2
	end 'orZero'
	if not halves(lookalike(value, n: reachFor(1)), high: 0x23456789, low: 0xABCDEF01) 'xorEight'
		return 3
	end 'xorEight'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: a-shift-also-used-elsewhere-survives-the-rotate -->
`high` is the left shift and is also an operand of the rotate. Both the rotate (`0x23456789ABCDEF01`) and `high` itself (`0x23456789ABCDEF00`) are read, so the shift stays whatever the rotate becomes.
```maxon
typealias Word = bits(64)

function halves(word Word, high Word, low Word) returns bool
	return (word shr 32) == high and (word and 0xFFFFFFFF) == low
end 'halves'

function rotateKeepingShift(word Word) returns bool
	let high = word shl 8
	let rotated = high or (word shr 56)

	if not halves(rotated, high: 0x23456789, low: 0xABCDEF01) 'rotated'
		return false
	end 'rotated'

	return halves(high, high: 0x23456789, low: 0xABCDEF00)
end 'rotateKeepingShift'

function rotateRightKeepingShift(word Word) returns bool
	let low = word shl 56
	let rotated = (word shr 8) or low

	if not halves(rotated, high: 0xEF012345, low: 0x6789ABCD) 'rotated'
		return false
	end 'rotated'

	return halves(low, high: 0xEF000000, low: 0)
end 'rotateRightKeepingShift'

function main() returns ExitCode
	let value = 0x0123456789ABCDEF

	if not rotateKeepingShift(value) 'left'
		return 1
	end 'left'
	if not rotateRightKeepingShift(value) 'right'
		return 2
	end 'right'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shifts-computed-before-a-branch-feed-an-or-inside-it -->
Both shifts are computed before the `if`; the `or` is inside it. Taken, the answer is the rotate `0x23456789ABCDEF01`; not taken, it is the left shift alone, `0x23456789ABCDEF00`.
```maxon
typealias Word = bits(64)

function halves(word Word, high Word, low Word) returns bool
	return (word shr 32) == high and (word and 0xFFFFFFFF) == low
end 'halves'

function rotateWhen(word Word, joined bool) returns Word
	let left = word shl 8
	let right = word shr 56

	if joined 'joined'
		return left or right
	end 'joined'

	return left
end 'rotateWhen'

function main() returns ExitCode
	let value = 0x0123456789ABCDEF

	if not halves(rotateWhen(value, joined: true), high: 0x23456789, low: 0xABCDEF01) 'taken'
		return 1
	end 'taken'
	if not halves(rotateWhen(value, joined: false), high: 0x23456789, low: 0xABCDEF00) 'skipped'
		return 2
	end 'skipped'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: a-variable-count-rotates-in-either-operand-order -->
With `n` declared `int(1 to 63)`, the right rotate is written `(x shr n) or (x shl (64 - n))` and `(x shl (64 - n)) or (x shr n)`, the left rotate `(x shl n) or (x shr (64 - n))` and `(x shr (64 - n)) or (x shl n)`. Each spelling gives the one answer for its direction: over `0x0123456789ABCDEF`, right by 8 is `0xEF012345 6789ABCD` and left by 8 is `0x23456789ABCDEF01`.
```maxon
typealias Word = bits(64)
typealias Step = int(0 to 1)
typealias RotateCount = int(1 to 63)

function rotateRightShiftFirst(word Word, n RotateCount) returns Word
	return (word shr n) or (word shl (64 - n))
end 'rotateRightShiftFirst'

function rotateRightShiftLast(word Word, n RotateCount) returns Word
	return (word shl (64 - n)) or (word shr n)
end 'rotateRightShiftLast'

function rotateLeftShiftFirst(word Word, n RotateCount) returns Word
	return (word shl n) or (word shr (64 - n))
end 'rotateLeftShiftFirst'

function rotateLeftShiftLast(word Word, n RotateCount) returns Word
	return (word shr (64 - n)) or (word shl n)
end 'rotateLeftShiftLast'

function countFor(step Step) returns RotateCount
	if step == 0 'eight'
		return 8
	end 'eight'

	return 63
end 'countFor'

function halves(word Word, high Word, low Word) returns bool
	return (word shr 32) == high and (word and 0xFFFFFFFF) == low
end 'halves'

function main() returns ExitCode
	let value = 0x0123456789ABCDEF

	if not halves(rotateRightShiftFirst(value, n: countFor(0)), high: 0xEF012345, low: 0x6789ABCD) 'rightFirst8'
		return 1
	end 'rightFirst8'
	if not halves(rotateRightShiftLast(value, n: countFor(0)), high: 0xEF012345, low: 0x6789ABCD) 'rightLast8'
		return 2
	end 'rightLast8'
	if not halves(rotateRightShiftFirst(value, n: countFor(1)), high: 0x02468ACF, low: 0x13579BDE) 'rightFirst63'
		return 3
	end 'rightFirst63'
	if not halves(rotateRightShiftLast(value, n: countFor(1)), high: 0x02468ACF, low: 0x13579BDE) 'rightLast63'
		return 4
	end 'rightLast63'
	if not halves(rotateLeftShiftFirst(value, n: countFor(0)), high: 0x23456789, low: 0xABCDEF01) 'leftFirst8'
		return 5
	end 'leftFirst8'
	if not halves(rotateLeftShiftLast(value, n: countFor(0)), high: 0x23456789, low: 0xABCDEF01) 'leftLast8'
		return 6
	end 'leftLast8'
	if not halves(rotateLeftShiftLast(value, n: countFor(1)), high: 0x8091A2B3, low: 0xC4D5E6F7) 'leftLast63'
		return 7
	end 'leftLast63'

	return 42
end 'main'
```
```exitcode
42
```
