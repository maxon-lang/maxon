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
register immediately after the call's result captures, inside the cold block. The hot path keeps
its registers; the cold path pays a store and a load per value, once, when it runs. A full-pool
overflow whose peak lies in a cold block is relieved by a spill placed in that block, at any loop
depth, for the same reason.

Why the rule matters: a versioned hot loop over two arrays holds two lengths, two buffers and its
counters live across every slow arm, and confined to five registers what did not fit was stored and
reloaded inside the loop on every iteration, for the sake of calls that never ran.

`specs/register-pressure.md`'s E5001 cases are the pin that a hot call's confinement is what it
was; the splitter's own spill code (store at the def, reload before each run of uses) relieves a
FULL pool and is a different mechanism.

⚠ A green case here proves nothing on its own — the bracket and the confinement compute the same
thing. The evidence is the committed fragment of the first case (no store or reload in the loop
body; a save/reload pair inside each slow arm) and the CONTROLS, which make a cold arm actually run
under values live across it and read those values back afterwards.

## Tests

<!-- test: a-versioned-loop-keeps-its-registers -->
The shape the row was opened for: two arrays, four accesses and an accumulator in one loop. In
`rotateAll`'s versioned fast copy the loop body holds no `storeSlotReg` and no `loadRegSlot`; each
`__im_slow` arm saves the live caller-saved registers before its call and reloads them after.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function rotateAll(current WordArray, count WordArray, n Word) returns Word
	var t = 0
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
	var current = WordArray.create()
	var count = WordArray.create()
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
	var t = 0
	var w = 1
	for i in 0 upto n 'each'
		w = w * 3 + i
		t = t + (try a.get(i) otherwise 99) + w
	end 'each'
	return t
end 'walk'

function main() returns ExitCode
	var a = WordArray.create()
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

function halves(a WordArray, n Word) returns Real
	var f = 0.5
	for i in 0 upto n 'each'
		let v = try a.get(i) otherwise 8
		f = f + (v as Real) * 0.5
	end 'each'
	return f
end 'halves'

function main() returns ExitCode
	var a = WordArray.create()
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

function pairs(a WordArray, n Word) returns Word
	var t = 0
	var w = 2
	for i in 0 upto n 'each'
		let first = try a.get(i) otherwise 100
		let second = try a.get(i + 1) otherwise 1000
		w = w + 1
		t = t + first + second + w
	end 'each'
	return t
end 'pairs'

function main() returns ExitCode
	var a = WordArray.create()
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
arm is the common path and is `normal`: its fragment shows no save/reload bracket, and the values
across it are confined. Every occupant is destroyed (no leak) and every new value is read back.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Names = Array with String

function relabel(a Names, n Word) returns Word
	var bytes = 0
	for i in 0 upto n 'each'
		try a.set(i, value: "name-{i}") otherwise panic("relabel: i < n = a.count()")
		let s = try a.get(i) otherwise panic("relabel: i < n = a.count()")
		bytes = bytes + s.byteLength()
	end 'each'
	return bytes
end 'relabel'

function main() returns ExitCode
	var a = Names.create()
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
	let v = try a.get(i) otherwise 0
	if v > 2 'heavy'
		print("")
	end 'heavy'
	return v * 2
end 'weigh'

function total(a WordArray, n Word) returns Word
	var t = 0
	var w = 5
	for i in 0 upto n 'each'
		w = w + i
		t = t + weigh(a, i: i) + w
	end 'each'
	return t
end 'total'

function main() returns ExitCode
	var a = WordArray.create()
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
