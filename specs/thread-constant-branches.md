---
feature: thread-constant-branches
status: experimental
keywords: [optimizer, codegen, branch, phi, block-args, jump-threading, try, otherwise, control-flow]
category: codegen
---
# A branch decided by the edge that reaches it

## Documentation

`threadConstantBranches` is a Std-tier pass that runs right after `foldConstOperands`. It looks for
a join block `C` whose body is exactly one compare of one of its own block arguments against an
immediate, and whose terminator branches on that compare. When a predecessor passes a **constant**
for that argument, the compare is decided at the moment the edge is taken — so for every such
predecessor the test is dead, and for every other predecessor it can be asked one block earlier.

The rewrite keeps `C` as the join (so no phi has to be rebuilt anywhere): every constant-passing
predecessor must agree on one target `X`; each non-constant predecessor gets the compare on its own
incoming value and branches to the other target `Y` directly or on into `C`; `C` loses the compare,
its edge to `Y`, and — once nothing reads it — the argument slot the compare tested, and ends in an
unconditional branch to `X`. A block whose only content is that branch is then removed by
`BranchCleanup`'s jump threading at the Target tier, so a constant-passing predecessor reaches `X`
with no test, no copy of the constant, and no jump.

### The shape it exists for

Every `try f(…) otherwise …` whose callee `InlineManagedPrimitives` expanded ends in a continuation
block `__im_cont` with block args `(value, flag)`: the fast arm passes `const 0` for the flag, the
slow arm the call's real flag, and the continuation's body is the `try`'s own `flag != 0` test. Until
this pass the fast path — every checked array read and write in every program — paid a constant
materialisation, a phi copy, a compare and a branch for an answer that was known where the edge is.
The same shape arises from any user-level phi of constants feeding a branch, and the rule is stated in
those terms rather than in `try`'s.

### What the rule refuses, and why

- **The tested argument is read anywhere but the compare.** The `otherwise (e)` binding forms, a bare
  `try` inside a throwing function (which rethrows the flag through `errorReturn`) and a `test`-body
  absorption all read the flag in `tryerr`; after the rewrite `tryerr` is reached from the slow
  predecessor directly, where `C`'s block argument is not defined. Those sites are left exactly as
  they are — `a-binding-handler-reads-the-flag-and-is-left-alone` and
  `a-rethrowing-try-propagates-the-error` pin them.
- **Two constants that decide different targets.** `C` would need two exits; `a-user-phi-whose-
  constants-disagree-is-left-alone` pins the refusal.
- **A predecessor that reaches `C` through a `condBranch` or `switch`**, and **a `C` whose body holds
  more than the compare** (the parser sometimes places a drop between a call and its test). Both are
  simply not the shape; a later widening owes its own cases.
- **Another block argument of `C` read outside blocks `X` dominates.** `C` keeps `X` and only `X`, so
  a value phi of `C` is defined on every path through `X` and on no path that skips `C` to `Y`.
- **An `X` with a second predecessor, or `X` the entry block.** "Every read of a value phi is
  dominated by `X`" is stable across the rewrite only when `C` dominates `X` afterwards, which one
  predecessor guarantees for every block but the entry.

When every predecessor passes a constant, `Y` loses its last way in and is dropped with the region only
it reaches — after every rewritten candidate has had its tested slot dropped, because that region can
hold one (`a-candidate-inside-an-orphaned-region`).

⚠ A green case here proves nothing on its own — behaviour does not change. The evidence is the
committed fragment of the first case (the fast arm reaches `tryok` with no `cmp` on the flag) and the
controls below, each of which exercises a path the rewrite touches: the value taken from the slow
arm, the handler reached from the duplicated test, the phi of the loaded value in the continuation.
Measured under sabotage — the "tested argument read elsewhere" refusal deleted — every case in this
file fails at COMPILE time: the pass admits a rethrowing bare `try` inside a stdlib body every program
links, the flag is then undefined on the new path into `tryerr`, and the register allocator refuses
the function ("value … is live-in to block … but was never colored — a use dominates its def").

## Tests

<!-- test: a-known-flag-skips-the-test -->
The shape the pass was opened for. In `pick`'s fragment the fast arm loads the element and reaches
`tryok` directly — no `movRegImm32 …, 0` for the flag, no `cmpRegImm32 r10, 0`, no `critsplit` copy
of the flag — while `__im_slow`'s call is followed by the `cmp`/`jcc` to `tryerr`.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max)
typealias WordArray = Array with Word

function pick(a WordArray, i Idx) returns Word
	return try a.get(i) otherwise panic("pick: index out of range")
end 'pick'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 6 'seed'
		a.push(i * 2)
	end 'seed'
	var t = 0
	for i in 0 upto 6 'sum'
		t = t + pick(a, i: i)
	end 'sum'
	if t != 30 'total'
		return 1
	end 'total'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-fallback-value-is-taken-on-a-real-failure -->
The slow path after the rewrite: the duplicated test sits in the slow arm and must still reach the
handler on a genuine failure, while the fast arm's value phi still reaches the continuation. Five
elements read at nine indices: 0+1+2+3+4 from the array and 99 four times from the handler.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max)
typealias WordArray = Array with Word

function pickOr(a WordArray, i Idx) returns Word
	return try a.get(i) otherwise 99
end 'pickOr'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 5 'seed'
		a.push(i)
	end 'seed'
	var t = 0
	for i in 0 upto 9 'sum'
		t = t + pickOr(a, i: i)
	end 'sum'
	if t != 406 'total'
		return 1
	end 'total'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-block-handler-runs-on-a-real-failure -->
The block-form handler is a block of its own reached from the duplicated test; it returns through the
function's exit, so a test that fell into the continuation instead would print 5 where 7 is right.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max)
typealias WordArray = Array with Word

function probe(a WordArray, i Idx) returns Word
	let v = try a.get(i) otherwise 'missing'
		return 7
	end 'missing'
	return v
end 'probe'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(5)
	if probe(a, i: 0) != 5 'present'
		return 1
	end 'present'
	if probe(a, i: 3) != 7 'absent'
		return 2
	end 'absent'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-write-in-a-loop-keeps-its-slow-arm -->
The store shape, with the store's own continuation carrying `(noError, noError)` from the fast arm.
Every slot is written and read back, and the one write past the end must still take the slow arm
and land in the handler.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max)
typealias WordArray = Array with Word

function put(a WordArray, i Idx, v Word) returns bool
	try a.set(i, value: v) otherwise return false
	return true
end 'put'

function main() returns ExitCode
	var a = WordArray.create()
	a.resize(4)
	var stored = 0
	for i in 0 upto 5 'each'
		if put(a, i: i, v: i * 10) 'ok'
			stored = stored + 1
		end 'ok'
	end 'each'
	if stored != 4 'count'
		return 1
	end 'count'
	var seen = 0
	for v in a 'check'
		seen = seen + v
	end 'check'
	if seen != 60 'sum'
		return 2
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-condition-form-in-a-while-header -->
The `try` inside a `while` condition — the continuation's compare feeds the loop's own branch a
block later, so the loop header holds two tests and the pass must leave the second one alone.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function main() returns ExitCode
	var a = WordArray.create()
	a.push(3)
	a.push(2)
	a.push(1)
	a.push(0)
	var i = 0
	var steps = 0
	while (try a.get(i) otherwise panic("main: i stays below count()")) > 0 'walk'
		steps = steps + 1
		i = i + 1
	end 'walk'
	if steps != 3 'count'
		return 1
	end 'count'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-binding-handler-reads-the-flag-and-is-left-alone -->
The flag is read in the handler to decode `e`, so the rule refuses the site and the site behaves as
it always did: `indexOutOfBounds` is what an index past the end reports.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max)
typealias WordArray = Array with Word

function classify(a WordArray, i Idx) returns Word
	let v = try a.get(i) otherwise (e) 'handler'
		match e 'which'
			indexOutOfBounds then return 3
			emptySlot then return 4
		end 'which'
	end 'handler'
	return v
end 'classify'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(8)
	if classify(a, i: 0) != 8 'present'
		return 1
	end 'present'
	if classify(a, i: 2) != 3 'absent'
		return 2
	end 'absent'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-rethrowing-try-propagates-the-error -->
A bare `try` in a throwing function rethrows the flag it received, so the flag is read past the
compare and the site is refused. The caller's own `otherwise` is the ordinary shape and may be
rewritten; the two must compose.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max)
typealias WordArray = Array with Word

function twice(a WordArray, i Idx) returns Word throws ArrayError
	let v = try a.get(i)
	return v * 2
end 'twice'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(21)
	let present = try twice(a, i: 0) otherwise 0
	let absent = try twice(a, i: 5) otherwise 100
	if present != 42 'first'
		return 1
	end 'first'
	if absent != 100 'second'
		return 2
	end 'second'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-user-phi-of-constants-agreeing-on-one-target -->
The general shape, without a `try`: three arms of an `if` chain merge a flag, two of them pass the
constant 0 and one a value only known at run time. In `classify`'s own body the two constant arms
decide the same target, so the test is duplicated into the third arm only. (The copies `inlineLeaves`
splices into `main` are a different shape: `foldConstants` has decided the outer `if` there and the
inner merge's argument reaches the join through an empty forwarding block, which the rule does not
look through — those copies are left alone.) The answer depends on which arm ran.
```maxon
typealias Word = int(i64.min to i64.max)

function classify(p bool, q bool, n Word) returns Word
	var flag = 0
	if p 'first'
		flag = 0
	end 'first' else if q 'second'
		flag = n - 5
	end 'second' else 'third'
		flag = 0
	end 'third'
	if flag != 0 'nonzero'
		return 1
	end 'nonzero'
	return 2
end 'classify'

function main() returns ExitCode
	if classify(true, q: true, n: 9) != 2 'a'
		return 1
	end 'a'
	if classify(false, q: true, n: 9) != 1 'b'
		return 2
	end 'b'
	if classify(false, q: true, n: 5) != 2 'c'
		return 3
	end 'c'
	if classify(false, q: false, n: 9) != 2 'd'
		return 4
	end 'd'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-user-phi-whose-constants-disagree-is-left-alone -->
Two constants that decide different targets: `C` would need two exits, so the site is refused and
the branch stays where it was.
```maxon
typealias Word = int(i64.min to i64.max)

function pick(p bool) returns Word
	var tag = 0
	if p 'yes'
		tag = 1
	end 'yes' else 'no'
		tag = 0
	end 'no'
	if tag != 0 'tagged'
		return 10
	end 'tagged'
	return 20
end 'pick'

function main() returns ExitCode
	if pick(true) != 10 'a'
		return 1
	end 'a'
	if pick(false) != 20 'b'
		return 2
	end 'b'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: the-other-target-carries-a-phi-of-its-own -->
When `Y` — the target the non-constant predecessor now reaches directly — has block arguments,
the new edge carries `C`'s edge arguments to `Y` with `C`'s own block arguments substituted by the
predecessor's incoming values. Here `C` merges `(flag, acc)`, `X` is the `nz` arm and `Y` is the
merge after it, which takes `acc` as a block argument; the `no` arm's new edge to `Y` must carry its
own `7`, not `C`'s phi. All four combinations are read back.
```maxon
typealias Word = int(i64.min to i64.max)

function bump(p bool, n Word) returns Word
	var flag = 0
	var acc = 5
	if p 'yes'
		flag = 1
		acc = n
	end 'yes' else 'no'
		flag = n
		acc = 7
	end 'no'
	if flag != 0 'nz'
		acc = acc + 1
	end 'nz'
	return acc
end 'bump'

function main() returns ExitCode
	if bump(true, n: 9) != 10 'a'
		return 1
	end 'a'
	if bump(true, n: 0) != 1 'b'
		return 2
	end 'b'
	if bump(false, n: 9) != 8 'c'
		return 3
	end 'c'
	if bump(false, n: 0) != 7 'd'
		return 4
	end 'd'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: every-predecessor-constant-orphans-the-other-target -->
When every predecessor passes a constant — here both arms of the `if` assign `0`, as two distinct
`const` ops so the phi survives `elimTrivialBlockArgs` — `C` has no non-constant predecessor, so `Y`
(the `nz` arm) loses its last way in and is removed with everything only it reaches. `f` answers 2 on
every call.
```maxon
typealias Word = int(i64.min to i64.max)

function f(p bool, n Word) returns Word
	var flag = 0
	if p 'a'
		flag = 0
	end 'a' else 'b'
		flag = 0
	end 'b'
	if flag != 0 'nz'
		return n + 1
	end 'nz'
	return 2
end 'f'

function main() returns ExitCode
	var t = 0
	for i in 0 upto 4 'each'
		t = t + f(i mod 2 == 0, n: i)
	end 'each'
	if t != 8 'total'
		return 1
	end 'total'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-candidate-inside-an-orphaned-region -->
The orphaned region can itself hold a second admitted candidate — the `g` join inside the `nz` arm,
with one runtime arm. Both are rewritten in one batch, so the second candidate's slot must be dropped
while its block is still in the function, and only then may the region go. This program used to
panic the compiler ("tested arg 13 is outside the block-arg id space").
```maxon
typealias Word = int(i64.min to i64.max)

function f(p bool, q bool, n Word) returns Word
	var flag = 0
	if p 'a'
		flag = 0
	end 'a' else 'b'
		flag = 0
	end 'b'
	if flag != 0 'nz'
		var g = 0
		if q 'c'
			g = 0
		end 'c' else 'd'
			g = n
		end 'd'
		if g != 0 'gz'
			return 1
		end 'gz'
		return 3
	end 'nz'
	return 2
end 'f'

function main() returns ExitCode
	var t = 0
	for i in 0 upto 4 'each'
		t = t + f(i mod 2 == 0, q: i > 1, n: i)
	end 'each'
	if t != 8 'total'
		return 1
	end 'total'
	return 0
end 'main'
```
```exitcode
0
```
