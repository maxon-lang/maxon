---
feature: argument-overlap
status: stable
keywords: [argument, overlap, alias, exclusivity, parameter, mutable, by-reference, clone, E3186]
category: memory
---
# Argument overlap (E3186) — a `mutable` parameter may not overlap another argument

## Documentation

A parameter declared `mutable` (`function bump(n mutable Coord, h Holder)`) is passed by reference: it
names the caller's storage, not a value of its own. A local handed to a `mutable` parameter the callee
writes is lent to the call as a by-reference cell, and the call overwrites it right after its own operands
have run. E3186 refuses a statement in which another operand reads that local BEFORE the write: another
argument of the call, an earlier operand, the receiver, a ternary condition that wraps the call, or a
closure literal that captures it. A read that runs after the write sees the new value and is accepted.

The refusal is judged per root binding. It counts reads of the local the cell is rooted in and of that
local's field paths, so `bump(h.n, h: h)` and `bump(h, n: h.n)` are refused whichever order the two
arguments are written in. The fields of `self` share one root, and a module `var` or a `static var` is one
root. A receiver is never a `mutable` argument (a method cannot assign to `self`); it is still a read of
its record, and a receiver beside an argument the method takes as `mutable` is refused like any other. A
call result is a fresh counted value that reads nothing of the local, so `bump(h.n, h: Holder.create(7))`
is accepted. Another name for the same record, such as a `view`, a `var` that took the record by move or a
`let` alias of a parameter, is not that local, and the hold a read of it takes keeps the value alive across
the write. A call is judged against the declaration of the function it names.

The refusal is E3186, reported at the by-reference argument:

```text
error E3186: argument 'h.n' is read by another operand of this statement and passed by reference to 'bump' (parameter 'n') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

A hold cannot protect a value its own cell overwrites, so the other operand must not read the local. Copy
it into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy; where the
other operand is itself a by-reference cell, copy the local into a `var`, pass the copy for that operand
and store it back after the call.

Only a parameter the callee writes lends its cell. A callee writes a `mutable` parameter when its body
does, or when it forwards the parameter to a `mutable` parameter that is written. A call through an
interface requirement or a function value counts every `mutable` position as written. A `mutable`
parameter the callee never writes reads like any other operand, and a parameter that is not `mutable` is a
counted value taken before the call, so a write through one parameter releases only the storage's own
reference and a value read out of the other still names what it read. Handing `x` twice to a `look` that
clears its second parameter, `readThenClear(r, log: r.ids)`, `assign(a, src: a)`, `a.absorb(a)`,
`s.append(s)` and `peek(h.n, h: h)` are legal, and a write is visible through both parameters because
they name one record.

## Tests

<!-- test: an-array-handed-twice-to-a-callee-that-clears-one-parameter -->
`look` reads an element out of `a` and clears `b`, and both are `x`. The element is counted, so it
survives the clear, and both parameters and the caller see the one array emptied.
```maxon
typealias StringArray = Array with String

function look(a StringArray, b StringArray)
	let s = try a.get(0) otherwise ""
	b.clear()
	print("[{s}] {a.count()} {b.count()}\n")
end 'look'

function main() returns ExitCode
	var x = StringArray.create(0)
	x.push("hello {40 + 2} world, long enough for the heap")
	look(x, b: x)
	print("{x.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello 42 world, long enough for the heap] 0 0
0
```

<!-- test: a-record-handed-beside-a-field-the-callee-clears -->
`readThenClear` reads `r.ids` through `r` and clears `log`, which is `r.ids`. The element it read keeps
its value, and the caller's `r.ids` is the array the callee cleared.
```maxon
typealias Coord = int(i64.min to i64.max)

type Inner
	export var v as Coord

	static function create(v Coord) returns Inner
		return Inner{v: v}
	end 'create'
end 'Inner'

typealias InnerArray = Array with Inner

type Rec
	export var ids as InnerArray
	export var log as InnerArray

	static function create() returns Rec
		return Rec{ids: InnerArray.create(0), log: InnerArray.create(0)}
	end 'create'
end 'Rec'

function readThenClear(r Rec, log InnerArray) returns Coord
	let s = try r.ids.get(0) otherwise panic("present")
	log.clear()
	let filler = Inner.create(77)
	return s.v + filler.v - 77
end 'readThenClear'

function main() returns ExitCode
	var r = Rec.create()
	r.ids.push(Inner.create(1234))
	let got = readThenClear(r, log: r.ids)
	print("{got} {r.ids.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1234 0
```

<!-- test: one-record-handed-to-a-callee-that-stores-a-field-of-one-parameter -->
`assign` stores a field of `dst` and reads `src`, and both are `a`: the store puts the array back where
it was read from, element intact.
```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Blk
	export var args as NumArray

	static function create() returns Blk
		return Self{args: NumArray.create(0)}
	end 'create'
end 'Blk'

function assign(dst Blk, src Blk)
	dst.args = src.args
end 'assign'

function main() returns ExitCode
	var a = Blk.create()
	a.args.push(7)
	assign(a, src: a)
	let v = try a.args.get(0) otherwise 0
	print("{a.args.count()} {v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 7
```

<!-- test: a-receiver-handed-to-its-own-method-that-writes-its-fields -->
`absorb` stores a field of its receiver and reads `other`, and both are `a`.
```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Blk
	export var args as NumArray

	static function create() returns Blk
		return Self{args: NumArray.create(0)}
	end 'create'

	function absorb(other Blk)
		self.args = other.args
	end 'absorb'
end 'Blk'

function main() returns ExitCode
	var a = Blk.create()
	a.args.push(7)
	a.absorb(a)
	let v = try a.args.get(0) otherwise 0
	print("{a.args.count()} {v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 7
```

<!-- test: one-array-handed-twice-to-a-callee-that-only-reads-both -->
`areSame` writes neither parameter, so handing it `x` twice is legal.
```maxon
typealias StringArray = Array with String

function areSame(a StringArray, b StringArray) returns bool
	return a is b and a.count() == b.count()
end 'areSame'

function main() returns ExitCode
	var x = StringArray.create(0)
	x.push("hello {40 + 2} world, long enough for the heap")
	print("{areSame(x, b: x)} {x.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true 1
```

<!-- test: a-string-appended-to-itself -->
`append` writes its receiver, which is also its argument, and a callee that only writes may overlap: the argument is
a counted value taken before the call.
```maxon
function main() returns ExitCode
	var s = "ab"
	s.append(s)
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
abab
```

<!-- test: error.a-field-handed-beside-its-owner-to-a-callee-that-takes-the-field-mutable -->
`bump` takes `n` as `mutable`, so `n` is the caller's `h.n`, and the record `h` handed beside it is a view of the
storage `bump` reassigns.
```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var n as Coord

	static function create(n Coord) returns Holder
		return Holder{n: n}
	end 'create'

	function copy() returns Holder
		return Holder.create(self.n)
	end 'copy'
end 'Holder'

function bump(n mutable Coord, h Holder) returns Coord
	n = n + 1
	return h.n
end 'bump'

function main() returns ExitCode
	var h = Holder.create(5)
	print("{bump(h.n, h: h)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:23:15: argument 'h.n' is read by another operand of this statement and passed by reference to 'bump' (parameter 'n') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: error.a-record-handed-first-and-a-field-of-it-to-a-mutable-parameter-second -->
The overlap is the same whichever order the two arguments are written in.
```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var n as Coord

	static function create(n Coord) returns Holder
		return Holder{n: n}
	end 'create'
end 'Holder'

function bump(h Holder, n mutable Coord) returns Coord
	n = n + 1
	return h.n
end 'bump'

function main() returns ExitCode
	var h = Holder.create(5)
	print("{bump(h, n: h.n)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:19:21: argument 'h.n' is read by another operand of this statement and passed by reference to 'bump' (parameter 'n') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: error.only-the-function-that-takes-the-parameter-mutable-refuses-the-overlap -->
`peek` takes `n` by value, so the call to it is legal; `settle` takes `n` as `mutable`, and only the call to
`settle` is refused.
```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var n as Coord

	static function create(n Coord) returns Holder
		return Holder{n: n}
	end 'create'
end 'Holder'

function settle(n mutable Coord, h Holder) returns Coord
	n = n + 1
	return h.n
end 'settle'

function peek(n Coord, h Holder) returns Coord
	return n + h.n
end 'peek'

function main() returns ExitCode
	var h = Holder.create(5)
	print("{peek(h.n, h: h)}\n")
	print("{settle(h.n, h: h)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:24:17: argument 'h.n' is read by another operand of this statement and passed by reference to 'settle' (parameter 'n') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: the-function-that-takes-the-parameter-by-value-accepts-the-overlap -->
`peek` reads `n` and `h` and reassigns nothing, so handing it a record and a field of that record is legal.
```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var n as Coord

	static function create(n Coord) returns Holder
		return Holder{n: n}
	end 'create'
end 'Holder'

function peek(n Coord, h Holder) returns Coord
	return n + h.n
end 'peek'

function main() returns ExitCode
	let h = Holder.create(5)
	print("{peek(h.n, h: h)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: error.a-receiver-handed-beside-a-field-the-method-takes-mutable -->
A receiver is a view of its record, so a method that takes a field of the receiver as `mutable` is refused at the
receiver.
```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var n as Coord

	static function create(n Coord) returns Holder
		return Holder{n: n}
	end 'create'

	function settle(m mutable Coord) returns Coord
		m = m + 1
		return self.n
	end 'settle'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(5)
	print("{h.settle(h.n)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:19:19: argument 'h.n' is read by another operand of this statement and passed by reference to 'settle' (parameter 'm') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: a-call-result-beside-a-mutable-parameter-is-a-fresh-counted-value -->
`Holder.create(7)` is a fresh record, not a view of `h.n`'s storage, so it may travel beside the `mutable` field.
```maxon
typealias Coord = int(i64.min to i64.max)

type Holder
	export var n as Coord

	static function create(n Coord) returns Holder
		return Holder{n: n}
	end 'create'
end 'Holder'

function bump(n mutable Coord, h Holder) returns Coord
	n = n + 1
	return h.n
end 'bump'

function main() returns ExitCode
	var h = Holder.create(5)
	let seen = bump(h.n, h: Holder.create(7))
	print("{seen} {h.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 6
```
