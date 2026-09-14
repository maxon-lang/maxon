---
feature: memory-destination-add
status: stable
keywords: var, global, counter, add, memory operand, fusion, flags, isel, x64
category: language
---
# A Counter Step Is One Instruction, And Only Where That Is The Same Program

## Documentation

`slot = slot + n` reads a word, adds to it and writes it back. On x64 that is one instruction,
`add [mem], n`, and instruction selection emits it when the three operations are one step: the
read feeds only the add, the add feeds only the write, both name the same address at the same
width, and nothing between them can write memory. The same holds for `-`, `and`, `or` and `xor`,
and for an operand in a register as well as a constant.

Each of those conditions is a way for the one instruction to compute something the three did not:

- **An intervening write** — a store or a call — changes the word between the read and the write.
  The three operations add to the value read BEFORE it; one instruction would add to the value
  written after it.
- **A second reader of the loaded value or of the sum** needs a register that one instruction does
  not produce.
- **The flags.** `add [mem]` sets them, where the three-instruction form computes its sum with a
  `lea`, which does not. A compare whose branch sits after the step must not read the step's flags.

Every case below computes an answer that a fold breaking one of those conditions gets wrong.
The first is the positive control: its rendered code is where the one instruction is visible.

Each of the five operations then runs once with a constant and once with a register, on `12` and
`10`, where every operation of the group gives a different answer — `+` 22, `-` 2, `and` 8, `or`
14, `xor` 6 — and an instruction that did not write the word leaves 12. An encoding that named
the wrong operation or the wrong direction therefore returns a different exit code.

## Tests

<!-- test: a-counter-step-on-a-global -->
The loop's step on `hits` is one read, one add and one write of the same word, with nothing in
between. `7` steps of one.

```maxon
var hits = 0

function main() returns ExitCode
	var n = 0

	while n < 7 'loop'
		hits = hits + 1
		n = n + 1
	end 'loop'

	return hits
end 'main'
```
```exitcode
7
```

<!-- test: a-compare-branches-on-its-own-flags-across-a-counter-step -->
The sum is computed, then the compare, then the write, then the branch. The add writes the flags
before the compare does, so the branch reads the compare's; the write does not touch them. `lo <
hi` is true, so the answer is `40 + 1`. One instruction at the write would set the flags between
the compare and its branch, describing `0 + 1` — not less — and the program would return 7.

```maxon
var lo = 0
var hi = 0
var hits = 0

function main() returns ExitCode
	lo = 1
	hi = 2
	let next = hits + 1
	let less = lo < hi
	hits = next

	if less 'less'
		return 40 + hits
	end 'less'

	return 7
end 'main'
```
```exitcode
41
```

<!-- test: a-read-between-the-sum-and-the-write-sees-the-old-value -->
The word is read again after the sum is computed and before it is written back, so that read sees
`3`: `3 * 10 + 4`. One instruction placed at the first read rather than at the write would make
the second read see `4` and return 44.

```maxon
var hits = 0

function main() returns ExitCode
	hits = 3
	let next = hits + 1
	let seen = hits
	hits = next
	return seen * 10 + hits
end 'main'
```
```exitcode
34
```

<!-- test: a-store-between-the-read-and-the-write-keeps-them-apart -->
`slot` is read, overwritten, and then written with the value read FIRST plus one: `5 + 1`. Adding
in place at the final write would add to the `100` and return 101.

```maxon
var slot = 5

function main() returns ExitCode
	let seen = slot
	slot = 100
	slot = seen + 1
	return slot
end 'main'
```
```exitcode
6
```

<!-- test: a-call-between-the-read-and-the-write-keeps-them-apart -->
The intervening write is inside a call. `reset` recurses, so it stays a call rather than being
spliced into `main` as a plain store. `5 + 1`, not `100 + 1`.

```maxon
typealias Depth = int(0 to 3)

var slot = 5

function reset(depth Depth)
	if depth > 0 'deeper'
		reset(depth - 1)
		return
	end 'deeper'

	slot = 100
end 'reset'

function main() returns ExitCode
	let seen = slot
	reset(3)
	slot = seen + 1
	return slot
end 'main'
```
```exitcode
6
```

<!-- test: the-value-read-is-used-again -->
The value read is needed after the write, so it must be in a register: `20 + 21`.

```maxon
var total = 20

function main() returns ExitCode
	let before = total
	total = before + 1
	return before + total
end 'main'
```
```exitcode
41
```

<!-- test: the-sum-is-used-again -->
The sum is needed after the write, so it must be in a register: `21 * 2 - 21`.

```maxon
var total = 20

function main() returns ExitCode
	let after = total + 1
	total = after
	return after * 2 - total
end 'main'
```
```exitcode
21
```

<!-- test: add-a-constant-to-the-word -->
`12 + 10` in one instruction. Any other operation of the group returns 2, 8, 14 or 6.

```maxon
var slot = 12

function main() returns ExitCode
	slot = slot + 10
	return slot
end 'main'
```
```exitcode
22
```

<!-- test: add-a-register-to-the-word -->
`12 + 10` with the operand in a register, read from another global. Any other operation returns 2,
8, 14 or 6; an instruction written in the load direction leaves the word at 12.

```maxon
var slot = 12
var operand = 0

function main() returns ExitCode
	operand = 10
	slot = slot + operand
	return slot
end 'main'
```
```exitcode
22
```

<!-- test: subtract-a-constant-from-the-word -->
`12 - 10`. Any other operation of the group returns 22, 8, 14 or 6.

```maxon
var slot = 12

function main() returns ExitCode
	slot = slot - 10
	return slot
end 'main'
```
```exitcode
2
```

<!-- test: subtract-a-register-from-the-word -->
`12 - 10` with the operand in a register. Any other operation returns 22, 8, 14 or 6; the load
direction leaves 12.

```maxon
var slot = 12
var operand = 0

function main() returns ExitCode
	operand = 10
	slot = slot - operand
	return slot
end 'main'
```
```exitcode
2
```

<!-- test: and-a-constant-into-the-word -->
`12 and 10`. Any other operation of the group returns 22, 2, 14 or 6.

```maxon
var slot = 12

function main() returns ExitCode
	slot = slot and 10
	return slot
end 'main'
```
```exitcode
8
```

<!-- test: and-a-register-into-the-word -->
`12 and 10` with the operand in a register. Any other operation returns 22, 2, 14 or 6; the load
direction leaves 12.

```maxon
var slot = 12
var operand = 0

function main() returns ExitCode
	operand = 10
	slot = slot and operand
	return slot
end 'main'
```
```exitcode
8
```

<!-- test: or-a-constant-into-the-word -->
`12 or 10`. Any other operation of the group returns 22, 2, 8 or 6.

```maxon
var slot = 12

function main() returns ExitCode
	slot = slot or 10
	return slot
end 'main'
```
```exitcode
14
```

<!-- test: or-a-register-into-the-word -->
`12 or 10` with the operand in a register. Any other operation returns 22, 2, 8 or 6; the load
direction leaves 12.

```maxon
var slot = 12
var operand = 0

function main() returns ExitCode
	operand = 10
	slot = slot or operand
	return slot
end 'main'
```
```exitcode
14
```

<!-- test: xor-a-constant-into-the-word -->
`12 xor 10`. Any other operation of the group returns 22, 2, 8 or 14.

```maxon
var slot = 12

function main() returns ExitCode
	slot = slot xor 10
	return slot
end 'main'
```
```exitcode
6
```

<!-- test: xor-a-register-into-the-word -->
`12 xor 10` with the operand in a register. Any other operation returns 22, 2, 8 or 14; the load
direction leaves 12.

```maxon
var slot = 12
var operand = 0

function main() returns ExitCode
	operand = 10
	slot = slot xor operand
	return slot
end 'main'
```
```exitcode
6
```

<!-- test: the-word-read-on-the-right-of-a-sum-is-the-same-step -->
`+` commutes, so a sum that reads the word as its RIGHT operand is the same step, in one
instruction: `10 + 4`. Adding the word to itself instead of the operand would return 8.

```maxon
var hits = 4
var operand = 0

function main() returns ExitCode
	operand = 10
	hits = operand + hits
	return hits
end 'main'
```
```exitcode
14
```

<!-- test: a-constant-on-the-left-of-a-sum-is-the-same-step -->
`1 + hits` is `hits + 1`, one instruction: `4 + 1`.

```maxon
var hits = 4

function main() returns ExitCode
	hits = 1 + hits
	return hits
end 'main'
```
```exitcode
5
```

<!-- test: the-word-read-on-the-right-of-a-difference-keeps-them-apart -->
`-` does not commute: `10 - 3` is 7. Subtracting in place would compute `3 - 10`, which is no exit
code, and the program would fail its range check instead.

```maxon
var slot = 3

function main() returns ExitCode
	slot = 10 - slot
	return slot
end 'main'
```
```exitcode
7
```

<!-- test: two-globals-are-two-words -->
`a` is written with `b + 1`, a read of a DIFFERENT global. `20 + 1`; adding in place to `a` would
return `5 + 1`.

```maxon
var a = 5
var b = 0

function main() returns ExitCode
	b = 20
	a = b + 1
	return a
end 'main'
```
```exitcode
21
```

<!-- test: a-sum-an-address-fold-already-claimed-keeps-them-apart -->
`operand * 8` added to `slot` is the `base + index*scale` shape an element address has, so the
address fold takes the sum as one `lea` and emits no multiply of its own. A step folded into the
write as well would add a product nothing computed. `5 + 3 * 8`.

```maxon
var slot = 5
var operand = 0

function main() returns ExitCode
	operand = 3
	slot = slot + operand * 8
	return slot
end 'main'
```
```exitcode
29
```
