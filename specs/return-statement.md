---
feature: return-statement
status: selfhosted
keywords: [return, control-flow]
category: statements
---

# Return Statement

## Documentation

`return <expression>` exits the current function, handing the value back to the
caller. A `return` also terminates its block, and each branch that returns emits
its own `ret`.

### Syntax

```maxon
return expression
```

## Tests

A bare value return, an expression return, a return inside an `if`, a `return` of a
variable as the tail statement, a return inside a called function's `if`
(`return-in-if-then-reachable`), and both sides of E3071: code after a `return` is refused
(`dead-code-after-return`) and code after an `if` that returns is not
(`return-in-if-then-statement-after`).

<!-- test: simple-return -->
```maxon
function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: expression-return -->
```maxon
function main() returns ExitCode
	return 2 + 3 * 4
end 'main'
```
```exitcode
14
```

<!-- test: conditional-return -->
A function with two exits must have BOTH of them executed. Written as `let x = 5; if x > 3 then
return 1; return 0`, the condition is a constant the run always takes, so the tail `return 0` is
compiled and never entered — its return lowering (the R8 move, the epilogue) is emitted
but never checked by an actual execution. Putting the branch in a helper and calling it on both
sides of the condition makes each exit an executed path, and each is checked alone so neither can
mask the other.
```maxon
function firstOver(x Integer) returns Integer
	if x > 3 'check'
		return 1
	end 'check'
	return 0
end 'firstOver'

function main() returns ExitCode
	if firstOver(5) != 1 'branchTaken'
		return 2
	end 'branchTaken'

	if firstOver(1) != 0 'branchNotTaken'
		return 3
	end 'branchNotTaken'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: tail-return-is-last -->
```maxon
function main() returns ExitCode
	let x = 7
	return x
end 'main'
```
```exitcode
7
```

<!-- test: return-in-if-then-reachable -->
```maxon
function classify(x ExitCode) returns ExitCode
	if x > 0 'positive'
		return 1
	end 'positive'
	return 0
end 'classify'

function main() returns ExitCode
	return classify(5)
end 'main'
```
```exitcode
1
```

<!-- test: dead-code-after-return -->
```maxon
function pick() returns ExitCode
	return 1
	return 2
end 'pick'

function main() returns ExitCode
	return pick()
end 'main'
```
```maxoncstderr
error E3071: <fragment>:4:2: unreachable code after 'return'
```

<!-- test: return-in-if-then-statement-after -->
The OTHER side of E3071, and the one that must keep compiling: a `return` inside an `if`
is NOT followed by dead code, because the statements after the `end` are reachable on the
false path. Written with an ordinary statement — not a second `return` — after the `if`,
so the case fails if the refusal is widened from "the same straight-line block" to "any
block that has already returned".

Both exits are executed, and the false path's answer pins that an early `return` is never
DISCARDED by `setTerminator` overwriting it. Here the `return` is in its own block, so nothing
overwrites it — and the trailing statement proves the continuation runs.
```maxon
function pick(x ExitCode) returns ExitCode
	if x > 0 'positive'
		return 1
	end 'positive'
	let fallback = 4
	return fallback
end 'pick'

function main() returns ExitCode
	if pick(9) != 1 'tookFalsePath'
		return 2
	end 'tookFalsePath'

	return pick(0)
end 'main'
```
```exitcode
4
```

<!-- test: return-one-of-many-live-owners -->
Ten owned `String` bindings are live when each early `return` hands one of them back, so the exit
owes a drop of the nine it does not return. Every index is driven once, and a final call returns a
fresh value that no binding owns: each path drops exactly the owners it should, and the leak gate
(exit 101) reddens any path that drops one twice or not at all.
```maxon
typealias Index = int(0 to 255)

function build(i Index) returns String
	return "owner {i} padded out long enough to heap allocate"
end 'build'

function pick(k Index) returns String
	let s0 = build(0)
	let s1 = build(1)
	let s2 = build(2)
	let s3 = build(3)
	let s4 = build(4)
	let s5 = build(5)
	let s6 = build(6)
	let s7 = build(7)
	let s8 = build(8)
	let s9 = build(9)

	if k == 0 'r0'
		return s0
	end 'r0'

	if k == 1 'r1'
		return s1
	end 'r1'

	if k == 2 'r2'
		return s2
	end 'r2'

	if k == 3 'r3'
		return s3
	end 'r3'

	if k == 4 'r4'
		return s4
	end 'r4'

	if k == 5 'r5'
		return s5
	end 'r5'

	if k == 6 'r6'
		return s6
	end 'r6'

	if k == 7 'r7'
		return s7
	end 'r7'

	if k == 8 'r8'
		return s8
	end 'r8'

	if k == 9 'r9'
		return s9
	end 'r9'

	return build(99)
end 'pick'

function main() returns ExitCode
	for k in 0 to 10 'each'
		let got = pick(k)
		print("{k}: {got}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0: owner 0 padded out long enough to heap allocate
1: owner 1 padded out long enough to heap allocate
2: owner 2 padded out long enough to heap allocate
3: owner 3 padded out long enough to heap allocate
4: owner 4 padded out long enough to heap allocate
5: owner 5 padded out long enough to heap allocate
6: owner 6 padded out long enough to heap allocate
7: owner 7 padded out long enough to heap allocate
8: owner 8 padded out long enough to heap allocate
9: owner 9 padded out long enough to heap allocate
10: owner 99 padded out long enough to heap allocate
```

<!-- test: return-an-older-binding-past-newer-owners -->
A `return` of an older binding while newer owned bindings are live must release the newer ones and
not the one it hands back: the binding returned lies one, two, three, four and five owners below the
innermost, and the last exits return a value built from two owners that are dropped.
```maxon
typealias Index = int(0 to 255)

function build(i Index) returns String
	return "owner {i} padded out long enough to heap allocate"
end 'build'

function pick(k Index) returns String
	let a = build(1)

	if k == 1 'r1'
		return a
	end 'r1'

	let b = build(2)

	if k == 2 'r2'
		return b
	end 'r2'

	let c = build(3)

	if k == 3 'r3'
		return a
	end 'r3'

	let d = build(4)

	if k == 4 'r4'
		return a
	end 'r4'

	let e = build(5)

	if k == 5 'r5'
		return b
	end 'r5'

	let f = build(6)

	if k == 6 'r6'
		return b
	end 'r6'

	if k == 7 'r7'
		return c
	end 'r7'

	if k == 8 'r8'
		return f
	end 'r8'

	if k == 9 'r9'
		return "{d.byteLength()}{e.byteLength()}"
	end 'r9'

	return build(99)
end 'pick'

function main() returns ExitCode
	for k in 0 to 10 'each'
		let got = pick(k)
		print("{k}: {got}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0: owner 99 padded out long enough to heap allocate
1: owner 1 padded out long enough to heap allocate
2: owner 2 padded out long enough to heap allocate
3: owner 1 padded out long enough to heap allocate
4: owner 1 padded out long enough to heap allocate
5: owner 2 padded out long enough to heap allocate
6: owner 2 padded out long enough to heap allocate
7: owner 3 padded out long enough to heap allocate
8: owner 6 padded out long enough to heap allocate
9: 4747
10: owner 99 padded out long enough to heap allocate
```

<!-- test: return-from-inside-a-loop-past-outer-owners -->
The same exits taken from inside a `for` loop, where the owners the exit releases are the loop body's
own binding above five declared outside the loop, and the returned binding is the innermost, an
outer one, or neither.
```maxon
typealias Index = int(0 to 255)

function build(i Index) returns String
	return "owner {i} padded out long enough to heap allocate"
end 'build'

function find(k Index) returns String
	let a = build(1)
	let b = build(2)
	let c = build(3)
	let d = build(4)
	let e = build(5)

	for i in 0 to 4 'scan'
		let t = build(10 + i)

		if i == k 'hit'
			return t
		end 'hit'

		if i + 1 == k 'older'
			return a
		end 'older'

		if i + 2 == k 'oldest'
			return "{c.byteLength()}{d.byteLength()}{e.byteLength()}"
		end 'oldest'
	end 'scan'

	return b
end 'find'

function main() returns ExitCode
	for k in 0 to 7 'each'
		print("{k}: {find(k)}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0: owner 10 padded out long enough to heap allocate
1: owner 1 padded out long enough to heap allocate
2: 474747
3: 474747
4: 474747
5: 474747
6: 474747
7: owner 2 padded out long enough to heap allocate
```

<!-- test: return-a-record-among-many-record-owners -->
Six records that each own a `String` are live at every exit, and the one handed back is the first,
the mutated one, a later one or the last: the records not returned are released with their strings, the
one returned reaches the caller intact.
```maxon
typealias Index = int(0 to 255)

type Box
	export var name as String
	export var weight as Index

	static function create(name String, weight Index) returns Box
		return Self{name: name, weight: weight}
	end 'create'
end 'Box'

function build(i Index) returns String
	return "owner {i} padded out long enough to heap allocate"
end 'build'

function pick(k Index) returns Box
	let a = Box.create(build(1), weight: 1)
	let b = Box.create(build(2), weight: 2)
	let c = Box.create(build(3), weight: 3)
	var d = Box.create(build(4), weight: 4)
	let e = Box.create(build(5), weight: 5)
	let f = Box.create(build(6), weight: 6)
	d.weight = d.weight + k + c.weight

	if k == 0 'zero'
		return a
	end 'zero'

	if k == 1 'one'
		return d
	end 'one'

	if k == 2 'two'
		return e
	end 'two'

	if k == 3 'three'
		return b
	end 'three'

	return f
end 'pick'

function main() returns ExitCode
	for k in 0 to 5 'each'
		let got = pick(k)
		print("{k}: {got.name} {got.weight}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0: owner 1 padded out long enough to heap allocate 1
1: owner 4 padded out long enough to heap allocate 8
2: owner 5 padded out long enough to heap allocate 5
3: owner 2 padded out long enough to heap allocate 2
4: owner 6 padded out long enough to heap allocate 6
5: owner 6 padded out long enough to heap allocate 6
```

<!-- test: return-a-tuple-among-many-owners -->
Tuples are returned from among owned strings, a tuple bound earlier, one bound later and a fresh
one, with enough owners live that the exits share one chain of drops.
```maxon
typealias Index = int(0 to 255)

function build(i Index) returns String
	return "owner {i} padded out long enough to heap allocate"
end 'build'

function makePair(i Index) returns (Index, Index)
	return (i, i + 1)
end 'makePair'

function pick(k Index) returns (Index, Index)
	let a = build(1)
	let b = build(2)
	let c = build(3)
	let d = build(4)
	let e = build(5)
	let first = makePair(k)
	let second = makePair(k + 10)
	let f = build(6)
	let g = build(7)

	if k == 0 'zero'
		return first
	end 'zero'

	if k == 1 'one'
		return second
	end 'one'

	if k == 2 'two'
		return (a.byteLength() as Index, b.byteLength() as Index)
	end 'two'

	return (c.byteLength() as Index, d.byteLength() as Index + e.byteLength() as Index + f.byteLength() as Index + g.byteLength() as Index)
end 'pick'

function main() returns ExitCode
	for k in 0 to 4 'each'
		let got = pick(k)
		print("{k}: {got.0} {got.1}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0: 0 1
1: 11 12
2: 47 47
3: 47 188
4: 47 188
```
