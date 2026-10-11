---
feature: argument-overlap
status: stable
keywords: [argument, overlap, alias, exclusivity, parameter, reassign, by-reference, clone, E3186]
category: memory
---
# Argument overlap (E3186) — a parameter passed by reference may not overlap another argument

## Documentation

A function REASSIGNS a parameter when its body assigns to the parameter itself (`h = Holder.create(5)`),
and a reassigned parameter is passed by reference: it names the caller's storage, not a value of its
own. A call may not pass, as one argument, storage the callee reassigns and, as another, a view of that
same storage. Two arguments overlap when one names the other's storage or storage inside it: the same
variable, or a variable and a field reached through it. The check compares the arguments as written — a
root binding, then its field steps — and does not see through a call result or a `match`, `try` or
conditional merge, each of which is a fresh counted value. A receiver is never passed by reference (a
method cannot assign to `self`), so it is never the reassigned argument; it is still a view, and a receiver that
overlaps an argument the method reassigns is refused like any other. Each overload of a
name is its own callee: a call is judged against the declaration it binds to.

The refusal is E3186, reported at the argument the callee does NOT reassign:

```text
error E3186: argument 'h.inner' overlaps argument 'h', which 'rebindThenRead' reassigns (parameter 'h') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'h.inner.clone()'
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

<!-- test: error.a-field-handed-beside-its-owner-to-a-callee-that-reassigns-the-owner -->
`rebindThenRead` assigns to `h`, so `h` is passed by reference and the call replaces the record `x`
was read out of.
```maxon
typealias Coord = int(i64.min to i64.max)

type Inner
	export var v as Coord

	static function create(v Coord) returns Inner
		return Inner{v: v}
	end 'create'
end 'Inner'

type Holder
	export let inner as Inner

	static function create(v Coord) returns Holder
		return Holder{inner: Inner.create(v)}
	end 'create'
end 'Holder'

function rebindThenRead(x Inner, h Holder) returns Coord
	h = Holder.create(5)
	let filler = Inner.create(77)
	return x.v + filler.v - 77 + h.inner.v - 5
end 'rebindThenRead'

function main() returns ExitCode
	var h = Holder.create(1234)
	let got = rebindThenRead(h.inner, h: h)
	print("{got} {h.inner.v}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:28:27: argument 'h.inner' overlaps argument 'h', which 'rebindThenRead' reassigns (parameter 'h') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'h.inner.clone()'
```

<!-- test: error.a-reassigned-parameter-first-and-a-field-of-it-second -->
The same overlap with the reassigned parameter written first: the refusal is still at the argument the
callee does not reassign, which is now the second.
```maxon
typealias Coord = int(i64.min to i64.max)

type Inner
	export var v as Coord

	static function create(v Coord) returns Inner
		return Inner{v: v}
	end 'create'
end 'Inner'

type Holder
	export let inner as Inner

	static function create(v Coord) returns Holder
		return Holder{inner: Inner.create(v)}
	end 'create'
end 'Holder'

function rebindThenRead(h Holder, x Inner) returns Coord
	h = Holder.create(5)
	let filler = Inner.create(77)
	return x.v + filler.v - 77 + h.inner.v - 5
end 'rebindThenRead'

function main() returns ExitCode
	var h = Holder.create(1234)
	let got = rebindThenRead(h, x: h.inner)
	print("{got} {h.inner.v}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:28:33: argument 'h.inner' overlaps argument 'h', which 'rebindThenRead' reassigns (parameter 'h') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'h.inner.clone()'
```

<!-- test: error.only-the-overload-that-reassigns-refuses-the-overlap -->
Two overloads of `settle` take the same arguments; the three-parameter one only reads `h`, the
two-parameter one reassigns it. The call bound to the reader is legal, and only the call bound to the
reassigning overload is refused.
```maxon
typealias Coord = int(i64.min to i64.max)

type Inner
	export var v as Coord

	static function create(v Coord) returns Inner
		return Inner{v: v}
	end 'create'
end 'Inner'

type Holder
	export let inner as Inner

	static function create(v Coord) returns Holder
		return Holder{inner: Inner.create(v)}
	end 'create'
end 'Holder'

function settle(x Inner, h Holder, scale Coord) returns Coord
	return (x.v + h.inner.v) * scale
end 'settle'

function settle(x Inner, h Holder) returns Coord
	h = Holder.create(5)
	let filler = Inner.create(77)
	return x.v + filler.v - 77 + h.inner.v - 5
end 'settle'

function main() returns ExitCode
	var h = Holder.create(1234)
	let doubled = settle(h.inner, h: h, scale: 2)
	let got = settle(h.inner, h: h)
	print("{doubled} {got} {h.inner.v}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:33:19: argument 'h.inner' overlaps argument 'h', which 'settle' reassigns (parameter 'h') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'h.inner.clone()'
```

<!-- test: the-overload-that-only-reads-accepts-the-overlap -->
The call with `scale` binds the reading overload and runs; the call without it binds the reassigning overload and
passes a clone.
```maxon
typealias Coord = int(i64.min to i64.max)

type Inner
	export var v as Coord

	static function create(v Coord) returns Inner
		return Inner{v: v}
	end 'create'
end 'Inner'

type Holder
	export let inner as Inner

	static function create(v Coord) returns Holder
		return Holder{inner: Inner.create(v)}
	end 'create'
end 'Holder'

function settle(x Inner, h Holder, scale Coord) returns Coord
	return (x.v + h.inner.v) * scale
end 'settle'

function settle(x Inner, h Holder) returns Coord
	h = Holder.create(5)
	let filler = Inner.create(77)
	return x.v + filler.v - 77 + h.inner.v - 5
end 'settle'

function main() returns ExitCode
	var h = Holder.create(1234)
	let doubled = settle(h.inner, h: h, scale: 2)
	let got = settle(h.inner.clone(), h: h)
	print("{doubled} {got} {h.inner.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4936 1234 5
```

<!-- test: error.an-array-handed-twice-to-a-callee-that-reassigns-one-parameter -->
`look` reads an element out of `a` and assigns a fresh array to `b`, and both are `x`.
```maxon
typealias StringArray = Array with String

function look(a StringArray, b StringArray)
	let s = try a.get(0) otherwise ""
	b = StringArray.create()
	print("[{s}] {b.count()}\n")
end 'look'

function main() returns ExitCode
	var x = StringArray.create()
	x.push("hello {40 + 2} world, long enough for the heap")
	look(x, b: x)
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:13:7: argument 'x' overlaps argument 'x', which 'look' reassigns (parameter 'b') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'x.clone()'
```

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

<!-- test: a-clone-of-the-array-survives-the-parameter-being-reassigned -->
`x.clone()` is a second array, so `look` reassigning `b` leaves the element it read out of the copy
intact, and the caller's `x` is the fresh array the callee assigned.
```maxon
typealias StringArray = Array with String

function look(a StringArray, b StringArray)
	let s = try a.get(0) otherwise ""
	b = StringArray.create()
	print("[{s}] {a.count()} {b.count()}\n")
end 'look'

function main() returns ExitCode
	var x = StringArray.create()
	x.push("hello {40 + 2} world, long enough for the heap")
	look(x.clone(), b: x)
	print("{x.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[hello 42 world, long enough for the heap] 1 0
0
```

<!-- test: a-clone-of-the-field-survives-the-owner-being-reassigned -->
`h.inner.clone()` is a record of its own, so `rebindThenRead` replacing `h` leaves `x` reading 1234, and
the caller's `h` is the record the callee assigned.
```maxon
typealias Coord = int(i64.min to i64.max)

type Inner
	export var v as Coord

	static function create(v Coord) returns Inner
		return Inner{v: v}
	end 'create'
end 'Inner'

type Holder
	export let inner as Inner

	static function create(v Coord) returns Holder
		return Holder{inner: Inner.create(v)}
	end 'create'
end 'Holder'

function rebindThenRead(x Inner, h Holder) returns Coord
	h = Holder.create(5)
	let filler = Inner.create(77)
	return x.v + filler.v - 77 + h.inner.v - 5
end 'rebindThenRead'

function main() returns ExitCode
	var h = Holder.create(1234)
	let got = rebindThenRead(h.inner.clone(), h: h)
	print("{got} {h.inner.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1234 5
```

<!-- test: error.a-receiver-handed-beside-an-argument-the-method-reassigns -->
A receiver is never the reassigned side, but it is a view like any other argument: `settle` assigns to `other`, so
handing it `h` while `h` is also the receiver is the same overlap.
```maxon
typealias Coord = int(i64.min to i64.max)

type Inner
	export var v as Coord

	static function create(v Coord) returns Inner
		return Inner{v: v}
	end 'create'
end 'Inner'

type Holder
	export let inner as Inner

	static function create(v Coord) returns Holder
		return Holder{inner: Inner.create(v)}
	end 'create'

	function settle(other Holder) returns Coord
		other = Holder.create(5)
		let filler = Inner.create(77)
		return self.inner.v + filler.v - 77 + other.inner.v - 5
	end 'settle'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(1234)
	let got = h.settle(h)
	print("{got} {h.inner.v}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:28:12: argument 'h' overlaps argument 'h', which 'settle' reassigns (parameter 'other') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'h.clone()'
```

<!-- test: a-call-result-and-a-merge-are-fresh-counted-values-beside-a-reassigned-parameter -->
The check compares arguments as written, so a field handed back by a call and a field chosen by a conditional are
each a counted value of their own: the callee reassigning `h` leaves the record `x` names alive.
```maxon
typealias Coord = int(i64.min to i64.max)

type Inner
	export var v as Coord

	static function create(v Coord) returns Inner
		return Inner{v: v}
	end 'create'
end 'Inner'

type Holder
	export let inner as Inner

	static function create(v Coord) returns Holder
		return Holder{inner: Inner.create(v)}
	end 'create'
end 'Holder'

function rebindThenRead(x Inner, h Holder) returns Coord
	h = Holder.create(5)
	let filler = Inner.create(77)
	return x.v + filler.v - 77 + h.inner.v - 5
end 'rebindThenRead'

function same(inner Inner) returns Inner
	return inner
end 'same'

function main() returns ExitCode
	var h = Holder.create(1234)
	let viaCall = rebindThenRead(same(h.inner), h: h)
	print("{viaCall} {h.inner.v}\n")
	var g = Holder.create(99)
	let viaMerge = rebindThenRead(g.inner if viaCall > 0 else Inner.create(1), h: g)
	print("{viaMerge} {g.inner.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1234 5
99 5
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
