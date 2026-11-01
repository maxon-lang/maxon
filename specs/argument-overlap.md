---
feature: argument-overlap
status: stable
keywords: [argument, overlap, alias, exclusivity, parameter, mutable, by-reference, clone, E3186]
category: memory
---
# Argument overlap (E3186) — a `mutable` parameter may not overlap another argument

## Documentation

A parameter declared `mutable` (`function bump(n mutable Coord, h Holder)`) is passed by reference: it
names the caller's storage, not a value of its own, and the function may reassign it. A call may not pass,
as one argument, storage the callee takes as a `mutable` parameter and, as another, a view of that same
storage. Two arguments overlap when one names the other's storage or storage inside it: the same variable,
or a variable and a field reached through it. The check compares the arguments as written — a root
binding, then its field steps — and does not see through a call result or a `match`, `try` or conditional
merge, each of which is a fresh counted value. A receiver is never a `mutable` argument (a method cannot
assign to `self`); it is still a view, and a receiver that overlaps an argument the method takes as
`mutable` is refused like any other. A call is judged against the declaration of the function it names.

The refusal is E3186, reported at the argument the callee does NOT take as `mutable`:

```text
error E3186: argument 'h' overlaps argument 'h.n', which 'bump' reassigns (parameter 'n') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'h.clone()'
```

A by-reference parameter points into the caller's storage without a count of its own, so reassigning
it replaces what the caller's storage held while the other argument is still a view of it. The remedy
is `.clone()`: a copy is storage of its own, so the reassignment cannot reach it.

A parameter the callee only WRITES — `clear`, `push`, a field store, a method that writes its
receiver's fields — may overlap another argument. Each such argument is a counted value taken before
the call, so a write through one parameter releases only the storage's own reference, and a value read
out of the other still names what it read. Handing `x` twice to a `look` that only clears its second
parameter, `readThenClear(r, log: r.ids)`, `assign(a, src: a)` and `a.absorb(a)` are legal, and the write
is visible through both parameters because they name one record.

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
	var x = StringArray.create()
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
		return Rec{ids: InnerArray.create(), log: InnerArray.create()}
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
		return Self{args: NumArray.create()}
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
		return Self{args: NumArray.create()}
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
	var x = StringArray.create()
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
error E3186: <fragment>:23:23: argument 'h' overlaps argument 'h.n', which 'bump' reassigns (parameter 'n') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'h.clone()'
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
error E3186: <fragment>:19:15: argument 'h' overlaps argument 'h.n', which 'bump' reassigns (parameter 'n') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'h.clone()'
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
error E3186: <fragment>:24:25: argument 'h' overlaps argument 'h.n', which 'settle' reassigns (parameter 'n') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'h.clone()'
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
error E3186: <fragment>:19:10: argument 'h' overlaps argument 'h.n', which 'settle' reassigns (parameter 'm') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'h.clone()'
```

<!-- test: a-call-result-beside-a-mutable-parameter-is-a-fresh-counted-value -->
`h.copy()` is a fresh record, not a view of `h.n`'s storage, so it may travel beside the `mutable` field, and the
copy keeps the value it was built from.
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
	let seen = bump(h.n, h: h.copy())
	print("{seen} {h.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 6
```
