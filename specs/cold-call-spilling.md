---
feature: cold-call-spilling
status: experimental
keywords: [regalloc, register-allocation, spill, call, caller-saved, callee-saved, cold, slow-arm, loop]
category: codegen
---
# A call the loop never makes pays for its own registers

## Documentation

A call clobbers the caller-saved registers, so a value live across it must survive in a callee-saved
register or in memory. Which of the two is decided by the HEAT of the call's block.

Every block carries a heat, `normal` or `cold`, set by the pass that mints it: an inlined access's
slow arm is cold when the fast arm serves the common case (a store over a trivially owned element,
every read — a `set` over a managed element takes its slow arm on every write, so that arm is
`normal`), a range check's panic block is cold, a leaf inliner's panic redirect is cold, and a
`try … otherwise panic` handler is cold. A copy, clone or split of a block keeps its heat.

A call in a `normal` block CONFINES the values live across it: they are coloured into callee-saved
registers, and x64 has five of them for integers. A call in a `cold` block confines nothing; after
colouring, every value live across it that sits in a caller-saved register — integer or float, in
its own class — is saved to a spill slot immediately before the call and reloaded into the same
register immediately after the call's result captures, inside the cold block. A value that is an
integer constant has its register rebuilt by the constant's own instruction after the captures
(`specs/cold-run-rematerialization.md`). The hot path keeps its registers; the cold path
pays a store and a load per other value, once, when it runs.

The unit of the bracket is a RUN, not the call: the argument pre-moves, the call and its result
captures are one region, saved before its first op and reloaded (or rebuilt) after its last, so a pre-move's own
write is paid exactly as the call's is and no run op confines. A signed division is a run of the same
shape without a call: the pre-moves into RAX and RDX, the divide (or, for a constant divisor, the high
multiply) and the capture of the quotient or remainder, which reads a register that divide wrote. A cold block's runs are computed once,
by one forward walk (`ColdBlockRuns`), and every consumer reads that table. Two things a run does not
cover stay confining: a value the run itself reads after an earlier op of the run has written its
register, which cannot sit in that register; and an op that defines a virtual of its own while writing
scratch (a float negation through an xmm, an arm64 atomic through x16/x17), which is no run op at all.
A call's pre-moves, the call and its captures are one GROUP: the splitter reloads a pre-move's read
before the group's first pre-move, never between two pre-moves, and the one op of its own it lands
between two captures — the spill store of what the first captured, right after that capture, so the
value holds no register across the second — is a run op, so the bracket's reloads still follow the
last capture. A value whose store or reload would span a peak is no victim at that peak. The
splitter's COLD placement follows the heat
rule too: a spill store or reload may land at loop depth 0 or in a cold block at any depth
(`blockIsOffTheHotPath`), so a value whose def and every use are off the hot path — the record a loop
reads only in its slow arm, the arm's own re-issued result — is split with its store outside the loop
or inside the arm and its reload inside the arm, at no cost to the hot path.

Where the hot working set itself is over the pool, a loop with a cold run is not refused: the run's
call confines those values and forces the bracket, so the region rule never turns such a loop into
E5001. Such a loop pays what the confinement charges — a store at the def and a reload before each use,
on the hot path — and E5001 is the answer for a loop with no cold run at all (`ir-specs/register-pressure.md`). The unswitcher asks the same question before it hoists: a loop's
live-across set plus the record fields it would hoist must fit the target's allocatable GPRs less a
margin for in-loop temporaries (`hoistPressureBudgetFor`), or the loop is left as it is.

Why the rule matters: a versioned hot loop over two arrays holds two lengths, two buffers and its
counters live across every slow arm, and confined to five registers what did not fit would be stored
and reloaded inside the loop on every iteration, for the sake of calls that never run.

`ir-specs/register-pressure.md`'s E5001 cases are the pin on a hot call's confinement; the splitter's own spill code (store at the def, reload before each run of uses) relieves a
FULL pool and is a different mechanism.

⚠ A green case here proves nothing on its own — the bracket and the confinement compute the same
thing. The shape shows in the first case's emitted code (`--emit-ir`: no store or reload in the loop
body; a save/reload pair inside each slow arm for each live register that holds no constant), which nothing in the suite pins; what the suite runs are
the CONTROLS, which make a cold arm actually run under values live across it and read those values back
afterwards. With the reload point re-storing instead of restoring, the cases whose cold arm runs at
their inputs answer wrong; the ones whose arm never runs (`rotateAll`, the scratch-clobbering and the
dividing panic handlers) stay green, and their bracket's shape shows only in their emitted code; the managed-element and
hot-call controls stay green because nothing is bracketed there.

## Tests

<!-- test: a-versioned-loop-keeps-its-registers -->
The shape the row was opened for: two arrays, four accesses and an accumulator in one loop. In
`rotateAll`'s versioned fast copy the loop body holds no `storeSlotReg` and no `loadRegSlot`; each
`__im_slow` arm saves the live caller-saved registers that hold no constant before its call and
reloads them after.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function rotateAll(current WordArray, count WordArray, n Count) returns Word
	var t = 0 as Word
	for i in 1 upto n 'each'
		let c = try count.get(i) otherwise panic("rotateAll: i < n <= count.count()")
		try count.set(i, value: c + 1) otherwise panic("rotateAll: i < n <= count.count()")
		let v = try current.get(i - 1) otherwise panic("rotateAll: i - 1 < n <= current.count()")
		try current.set(i, value: v) otherwise panic("rotateAll: i < n <= current.count()")
		t = t + v + c
	end 'each'
	return t
end 'rotateAll'

function main() returns ExitCode
	let current = WordArray.create(0)
	let count = WordArray.create(0)
	for i in 0 upto 6 'seed'
		current.push(i + 10)
		count.push(i)
	end 'seed'
	if rotateAll(current, count: count, n: 6) != 65 'sum'
		return 1
	end 'sum'
	if (try current.get(5) otherwise -1) != 10 'rotated'
		return 2
	end 'rotated'
	if (try count.get(5) otherwise -1) != 6 'counted'
		return 3
	end 'counted'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-cold-arm-that-runs-reloads-what-it-saved -->
Control. Four of nine indices are past the end, so the slow arm runs four times with `t`, `w` and
the counter live across its call; each is saved and reloaded, and the answer reads them back.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function walk(a WordArray, n Word) returns Word
	var t = 0 as Word
	var w = 1 as Word
	for i in 0 upto n 'each'
		w = w * 3 + i
		t = t + (try a.get(i as ElementIndex) otherwise 99) + w
	end 'each'
	return t
end 'walk'

function main() returns ExitCode
	let a = WordArray.create(0)
	for i in 0 upto 5 'seed'
		a.push(i)
	end 'seed'
	if walk(a, n: 9) != 37285 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-float-survives-a-cold-call -->
Control for the floating-point half. `f` accumulates across a slow arm that runs on the last four
indices; a float live across the call is saved and reloaded through its XMM register exactly as an
integer is, and must come back intact.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)
typealias WordArray = Array with Word

function halves(a WordArray, n Count) returns Real
	var f = 0.5 as Real
	for i in 0 upto n 'each'
		let v = try a.get(i) otherwise 8
		f = f + (v as Real) * 0.5
	end 'each'
	return f
end 'halves'

function main() returns ExitCode
	let a = WordArray.create(0)
	for i in 0 upto 4 'seed'
		a.push(i)
	end 'seed'
	if halves(a, n: 8) != 19.5 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: two-cold-calls-in-one-body -->
Control for a body with two slow arms in sequence, each reached on the last iterations, with the
same values live across both.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function pairs(a WordArray, n Count) returns Word
	var t = 0 as Word
	var w = 2 as Word
	for i in 0 upto n 'each'
		let first = try a.get(i) otherwise 100
		let second = try a.get(i + 1) otherwise 1000
		w = w + 1
		t = t + first + second + w
	end 'each'
	return t
end 'pairs'

function main() returns ExitCode
	let a = WordArray.create(0)
	for i in 0 upto 4 'seed'
		a.push(i)
	end 'seed'
	if pairs(a, n: 6) != 3245 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-managed-element-store-arm-stays-hot -->
Control for the heat rule. A `set` over `String` elements takes its slow arm on every write, so that
arm is the common path and is `normal`: its emitted code (`--emit-ir`) shows no save/reload bracket, and the values
across it are confined. Every occupant is destroyed (no leak) and every new value is read back.
```maxon
typealias Names = Array with String

function relabel(a Names, n Count) returns Count
	var bytes = 0 as Count
	for i in 0 upto n 'each'
		try a.set(i, value: "name-{i}") otherwise panic("relabel: i < n = a.count()")
		let s = try a.get(i) otherwise panic("relabel: i < n = a.count()")
		bytes = bytes + s.byteLength()
	end 'each'
	return bytes
end 'relabel'

function main() returns ExitCode
	let a = Names.create(0)
	for i in 0 upto 4 'seed'
		a.push("original-{i}")
	end 'seed'
	if relabel(a, n: 4) != 24 'bytes'
		return 1
	end 'bytes'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-hot-call-in-a-loop-confines-as-before -->
Control for the other half of the rule. `weigh` is a real call on every iteration — its block is
`normal` — so the values live across it are confined to callee-saved registers, with no bracket.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function weigh(a WordArray, i Word) returns Word
	let v = try a.get(i as ElementIndex) otherwise 0
	if v > 2 'heavy'
		print("")
	end 'heavy'
	return v * 2
end 'weigh'

function total(a WordArray, n Word) returns Word
	var t = 0 as Word
	var w = 5 as Word
	for i in 0 upto n 'each'
		w = w + i
		t = t + weigh(a, i: i) + w
	end 'each'
	return t
end 'total'

function main() returns ExitCode
	let a = WordArray.create(0)
	for i in 0 upto 5 'seed'
		a.push(i)
	end 'seed'
	if total(a, n: 5) != 65 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-loop-at-the-pools-edge-keeps-its-registers -->
The largest loop of this shape that fits: `i`, `t`, nine accumulators, `n` and `a` — thirteen values
live around the loop — with the slow arm's two results on top inside the arm. The unswitcher leaves
the loop alone (thirteen plus the two fields it would hoist is over its budget), so no hoisted value
joins the set; `a` is read by the range check and the fast load on the hot path, so it is bracketed
around the arm's call, never split; the arm's own result is stored right after its capture — before
the flag's — and reloaded in the cold split block on its edge, so at the flag's capture the loop's
thirteen and the flag are all that is live. The emitted code (`--emit-ir`) holds no `storeSlotReg` or `loadRegSlot` in
the loop's hot blocks
(`forhdr`, `each`, `__rc_ok`, `__im_load`, `trycont`, `forstep`); every one is inside `__im_slow` or
`critsplit`. `reference` is the same loop without the call.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function viaArray(a WordArray, n Word) returns Word
	var a0 = 1 as Word
	var a1 = 2 as Word
	var a2 = 3 as Word
	var a3 = 4 as Word
	var a4 = 5 as Word
	var a5 = 6 as Word
	var a6 = 7 as Word
	var a7 = 8 as Word
	var a8 = 9 as Word
	var t = 0 as Word
	for i in 0 upto n 'each'
		a0 = a0 + i
		a1 = a1 xor a0
		a2 = a2 + a1
		a3 = a3 xor a2
		a4 = a4 + a3
		a5 = a5 xor a4
		a6 = a6 + a5
		a7 = a7 xor a6
		a8 = a8 + a7
		let e = try a.get(i as ElementIndex) otherwise 99
		t = t + e + a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8
	end 'each'
	return t
end 'viaArray'

function reference(n Word) returns Word
	var a0 = 1 as Word
	var a1 = 2 as Word
	var a2 = 3 as Word
	var a3 = 4 as Word
	var a4 = 5 as Word
	var a5 = 6 as Word
	var a6 = 7 as Word
	var a7 = 8 as Word
	var a8 = 9 as Word
	var t = 0 as Word
	for i in 0 upto n 'each'
		a0 = a0 + i
		a1 = a1 xor a0
		a2 = a2 + a1
		a3 = a3 xor a2
		a4 = a4 + a3
		a5 = a5 xor a4
		a6 = a6 + a5
		a7 = a7 xor a6
		a8 = a8 + a7
		let e = i if i < 6 else 99
		t = t + e + a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8
	end 'each'
	return t
end 'reference'

function main() returns ExitCode
	let a = WordArray.create(0)
	for i in 0 upto 6 'seed'
		a.push(i)
	end 'seed'
	return 0 if viaArray(a, n: 12) == reference(12) else 1
end 'main'
```
```exitcode
0
```

<!-- test: a-loop-past-the-pools-edge-pays-the-bracket -->
One accumulator more — fourteen values live around the loop, plus the guard's transient bound load —
and the hot working set is over the pool. The loop is not refused: its values cross the arm's run,
so the overflow takes the forced bracket the arm's call would have forced when it confined, and the
emitted code (`--emit-ir`) shows that cost on the hot path — a store at the loop header and one in the body, with their
reloads in `trycont` and `forstep` — beside the arm's own bracket. The answer is read back through
every one of them.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function viaArray(a WordArray, n Word) returns Word
	var a0 = 1 as Word
	var a1 = 2 as Word
	var a2 = 3 as Word
	var a3 = 4 as Word
	var a4 = 5 as Word
	var a5 = 6 as Word
	var a6 = 7 as Word
	var a7 = 8 as Word
	var a8 = 9 as Word
	var a9 = 10 as Word
	var t = 0 as Word
	for i in 0 upto n 'each'
		a0 = a0 + i
		a1 = a1 xor a0
		a2 = a2 + a1
		a3 = a3 xor a2
		a4 = a4 + a3
		a5 = a5 xor a4
		a6 = a6 + a5
		a7 = a7 xor a6
		a8 = a8 + a7
		a9 = a9 xor a8
		let e = try a.get(i as ElementIndex) otherwise 99
		t = t + e + a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9
	end 'each'
	return t
end 'viaArray'

function reference(n Word) returns Word
	var a0 = 1 as Word
	var a1 = 2 as Word
	var a2 = 3 as Word
	var a3 = 4 as Word
	var a4 = 5 as Word
	var a5 = 6 as Word
	var a6 = 7 as Word
	var a7 = 8 as Word
	var a8 = 9 as Word
	var a9 = 10 as Word
	var t = 0 as Word
	for i in 0 upto n 'each'
		a0 = a0 + i
		a1 = a1 xor a0
		a2 = a2 + a1
		a3 = a3 xor a2
		a4 = a4 + a3
		a5 = a5 xor a4
		a6 = a6 + a5
		a7 = a7 xor a6
		a8 = a8 + a7
		a9 = a9 xor a8
		let e = i if i < 6 else 99
		t = t + e + a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9
	end 'each'
	return t
end 'reference'

function main() returns ExitCode
	let a = WordArray.create(0)
	for i in 0 upto 6 'seed'
		a.push(i)
	end 'seed'
	return 0 if viaArray(a, n: 12) == reference(12) else 1
end 'main'
```
```exitcode
0
```

<!-- test: a-flag-capture-after-a-cold-store -->
A `tryCall`'s answer arrives as TWO adjacent captures, `result ← r8` then `flag ← r10`. Here the arm's
result is split, so its store sits between the two, and the cold run must carry it: a run closed at
the store would put the bracket's reloads there, restore r10, and the flag's capture would read the
restored value — a wrong answer that survives every iteration whose restored r10 is merely nonzero.
`z` zeroes the accumulators from the seventh iteration on, so the checksum can only come out right if
every slow-arm result and flag were the call's own.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function viaArray(a WordArray, n Word) returns Word
	var a0 = 1 as Word
	var a1 = 2 as Word
	var a2 = 3 as Word
	var a3 = 4 as Word
	var a4 = 5 as Word
	var a5 = 6 as Word
	var a6 = 7 as Word
	var a7 = 8 as Word
	var a8 = 9 as Word
	var t = 0 as Word
	for i in 0 upto n 'each'
		let z = 1 if i < 6 else 0
		a0 = (a0 + i) * z
		a1 = (a1 xor a0) * z
		a2 = (a2 + a1) * z
		a3 = (a3 xor a2) * z
		a4 = (a4 + a3) * z
		a5 = (a5 xor a4) * z
		a6 = (a6 + a5) * z
		a7 = (a7 xor a6) * z
		a8 = (a8 + a7) * z
		let e = try a.get(i as ElementIndex) otherwise 99
		t = t + e + a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8
	end 'each'
	return t
end 'viaArray'

function reference(n Word) returns Word
	var a0 = 1 as Word
	var a1 = 2 as Word
	var a2 = 3 as Word
	var a3 = 4 as Word
	var a4 = 5 as Word
	var a5 = 6 as Word
	var a6 = 7 as Word
	var a7 = 8 as Word
	var a8 = 9 as Word
	var t = 0 as Word
	for i in 0 upto n 'each'
		let z = 1 if i < 6 else 0
		a0 = (a0 + i) * z
		a1 = (a1 xor a0) * z
		a2 = (a2 + a1) * z
		a3 = (a3 xor a2) * z
		a4 = (a4 + a3) * z
		a5 = (a5 xor a4) * z
		a6 = (a6 + a5) * z
		a7 = (a7 xor a6) * z
		a8 = (a8 + a7) * z
		let e = i if i < 6 else 99
		t = t + e + a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8
	end 'each'
	return t
end 'reference'

function main() returns ExitCode
	let a = WordArray.create(0)
	for i in 0 upto 6 'seed'
		a.push(i)
	end 'seed'
	print("{viaArray(a, n: 12)} {reference(12)}\n")
	return 0 if viaArray(a, n: 12) == reference(12) else 1
end 'main'
```
```stdout
2857 2857
```
```exitcode
0
```

<!-- test: a-scratch-clobbering-op-in-a-cold-arm -->
A panic handler is a cold block, and the float negation in its message writes an xmm scratch while
defining a virtual of its own. Such an op is no run op — a value born inside a run would have no save
point — so its scratch keeps confining exactly as in a normal block, and the block's runs are the
handler's calls alone. Every index is in range, so the handler never runs and the answer is the sum.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)
typealias WordArray = Array with Word

function walk(a WordArray, n Count, f Real) returns Word
	var t = 0 as Word
	for i in 0 upto n 'each'
		let v = try a.get(i) otherwise panic("index {i} is past the end; scale was {-f}")
		t = t + v
	end 'each'
	return t
end 'walk'

function main() returns ExitCode
	let a = WordArray.create(0)
	for i in 0 upto 5 'seed'
		a.push(i)
	end 'seed'
	return 0 if walk(a, n: 5, f: 1.5) == 10 else 1
end 'main'
```
```exitcode
0
```

<!-- test: a-division-in-a-cold-arm -->
A panic handler is a cold block, and a signed division in its message is a run of its own: the dividend
and the sign extension are pre-moves into RAX and RDX, the divide writes both, and the capture of the
quotient or the remainder reads the register the divide answers through. Every index is in range, so
the handler never runs and the answer is the sum.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Scale = int(1 to 1000)
typealias WordArray = Array with Word

function walk(a WordArray, n Count, m Scale, d Scale) returns Word
	var t = 0 as Word
	for i in 0 upto n 'each'
		let v = try a.get(i) otherwise panic("index {i} is past the end; ratio was {m / d} remainder {m mod d}")
		t = t + v
	end 'each'
	return t
end 'walk'

function main() returns ExitCode
	let a = WordArray.create(0)
	for i in 0 upto 5 'seed'
		a.push(i + 3)
	end 'seed'
	let dividend = try a.get(4) otherwise 1
	let divisor = try a.get(2) otherwise 1
	return 0 if walk(a, n: 5, m: dividend as Scale, d: divisor as Scale) == 25 else 1
end 'main'
```
```exitcode
0
```

<!-- test: a-cold-arm-that-divides-reads-its-live-values-after-the-divide -->
Control for the division above with the handler running. The counter, the running sum and the
multiplier are live across both divides of the message and are read after them, so each is saved
before the run and reloaded after the capture of the remainder; the message reads them back.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Scale = int(1 to 1000)
typealias WordArray = Array with Word

function walk(a WordArray, n Count, m Scale, d Scale) returns Word
	var t = 0 as Word
	var w = 1 as Word
	for i in 0 upto n 'each'
		w = w * 3 + (i as Word)
		let v = try a.get(i) otherwise panic("ratio {m / d} remainder {m mod d} sum {t} multiplier {w} index {i}")
		t = t + v
	end 'each'
	return t
end 'walk'

function main() returns ExitCode
	let a = WordArray.create(0)
	for i in 0 upto 5 'seed'
		a.push(i + 3)
	end 'seed'
	let dividend = try a.get(4) otherwise 1
	let divisor = try a.get(2) otherwise 1
	return 0 if walk(a, n: 8, m: dividend as Scale, d: divisor as Scale) == 25 else 1
end 'main'
```
```exitcode
1
```
```stderr
panic at a-cold-arm-that-divides-reads-its-live-values-after-the-divide.test:11: ratio 1 remainder 2 sum 25 multiplier 908 index 5
Stack trace:
  in walk
  in main
  in mrt_start
```

<!-- test: a-cold-arm-that-divides-by-a-constant-reads-its-live-values-after-the-divide -->
Control for a divisor known at compile time. The division becomes a multiply that keeps only the high
half of the product, whose answer is read from RDX, and the remainder is taken from that quotient. The
run is the pre-moves into RAX, the high multiply and the capture of RDX, and the sum, multiplier and
index live across it are saved before the run and reloaded after the capture.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function walk(a WordArray, n Count, m Word) returns Word
	var t = 0 as Word
	var w = 1 as Word
	for i in 0 upto n 'each'
		w = w * 3 + (i as Word)
		let v = try a.get(i) otherwise panic("ratio {m / 7} remainder {m mod 7} sum {t} multiplier {w} index {i}")
		t = t + v
	end 'each'
	return t
end 'walk'

function main() returns ExitCode
	let a = WordArray.create(0)
	for i in 0 upto 5 'seed'
		a.push(i + 3)
	end 'seed'
	let seed = try a.get(4) otherwise 1
	return 0 if walk(a, n: 8, m: seed * 31 + 3) == 25 else 1
end 'main'
```
```exitcode
1
```
```stderr
panic at a-cold-arm-that-divides-by-a-constant-reads-its-live-values-after-the-divide.test:10: ratio 31 remainder 3 sum 25 multiplier 908 index 5
Stack trace:
  in walk
  in main
  in mrt_start
```
