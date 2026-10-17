---
feature: write-through-a-holder
keywords: [mutation, parameter, let, immutable, holder, struct, tuple, field, keep, E3078, E3159, E3187]
category: semantics
---

# A Record Stored Into a Holder Is Kept by the Holder

## Documentation

A record stored into a struct field or a tuple slot is held by that field for as long as the holder lives. A
function that stores one of its parameters there, directly or through a factory function or a method that does,
therefore keeps that parameter, and so does a function that pushes it into a container. A `let` handed to a
parameter the callee keeps where something may write it, and read afterwards, is `E3078`, whether or not anything
later writes through the holder. A `let` field holds only a read-only value, so a parameter kept only in a `let`
field takes a `let`.

A method that writes its own receiver, called directly on a parameter, writes that parameter
(`parameter-mutation.md`), and a `let` handed to it is `E3159`. A method behind a comparison operator may neither
write nor keep its operand or receiver (`E3187`).

## Tests

<!-- test: write-through-a-holder.error.a-parameter-stored-in-a-struct-is-written-by-the-holders-method -->
`bump` stores its parameter into a `var` field of a struct, so it keeps the parameter, and a `let` handed to it and read afterwards is refused.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function clear() returns Count
		n = 0
		return 0
	end 'clear'
end 'Box'

function zero(b Box) returns Count
	b.n = 0
	return 0
end 'zero'

type Env
	export var b as Box
	export var hits as Count

	static function create(b Box) returns Self
		return Self{b: b, hits: 0}
	end 'create'

	function run() returns Count
		return self.b.clear()
	end 'run'

	function bumpHits()
		hits = hits + 1
	end 'bumpHits'
end 'Env'

function bump(b Box) returns Count
	var e = Env.create(b)
	return e.run()
end 'bump'

function main() returns ExitCode
	let a = Box.create(2)
	let r = bump(a)
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:46:10: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'bump', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-parameter-stored-in-a-struct-is-written-through-its-field-by-a-callee -->
The holder reaches a callee whole; `bump` keeps its parameter in the holder all the same.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function clear() returns Count
		n = 0
		return 0
	end 'clear'
end 'Box'

function zero(b Box) returns Count
	b.n = 0
	return 0
end 'zero'

type Env
	export var b as Box
	export var hits as Count

	static function create(b Box) returns Self
		return Self{b: b, hits: 0}
	end 'create'

	function run() returns Count
		return self.b.clear()
	end 'run'

	function bumpHits()
		hits = hits + 1
	end 'bumpHits'
end 'Env'

function zeroHeld(e Env) returns Count
	return zero(e.b)
end 'zeroHeld'

function bump(b Box) returns Count
	var e = Env.create(b)
	return zeroHeld(e)
end 'bump'

function main() returns ExitCode
	let a = Box.create(2)
	let r = bump(a)
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:50:10: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'bump', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-parameter-stored-in-a-tuple-is-written-by-a-callee -->
A tuple holds what it was built from, so `bump` keeps its parameter in the tuple.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function clear() returns Count
		n = 0
		return 0
	end 'clear'
end 'Box'

function zero(b Box) returns Count
	b.n = 0
	return 0
end 'zero'

function zeroFirst(t (Box, Count)) returns Count
	return zero(t.0)
end 'zeroFirst'

function bump(b Box) returns Count
	var t = (b, 1 as Count)
	return zeroFirst(t)
end 'bump'

function main() returns ExitCode
	let a = Box.create(2)
	let r = bump(a)
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:33:10: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'bump', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-parameter-held-by-a-record-another-function-returned-is-written -->
The holder comes back from a factory function that keeps its parameter, so `bump` keeps its own.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function clear() returns Count
		n = 0
		return 0
	end 'clear'
end 'Box'

function zero(b Box) returns Count
	b.n = 0
	return 0
end 'zero'

type Env
	export var b as Box
	export var hits as Count

	static function create(b Box) returns Self
		return Self{b: b, hits: 0}
	end 'create'

	function run() returns Count
		return self.b.clear()
	end 'run'

	function bumpHits()
		hits = hits + 1
	end 'bumpHits'
end 'Env'

function make(b Box) returns Env
	return Env.create(b)
end 'make'

function bump(b Box) returns Count
	var e = make(b)
	return zero(e.b)
end 'bump'

function main() returns ExitCode
	let a = Box.create(2)
	let r = bump(a)
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:50:10: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'bump', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-parameter-attached-to-a-holder-by-a-method-is-written -->
A method that stores its argument into the holder's field keeps it as a constructor does, so `bump` keeps its parameter.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function clear() returns Count
		n = 0
		return 0
	end 'clear'
end 'Box'

function zero(b Box) returns Count
	b.n = 0
	return 0
end 'zero'

type Slot
	export var held as Box

	static function create() returns Self
		return Self{held: Box.create(0)}
	end 'create'

	function attach(b Box)
		held = b
	end 'attach'

	function run() returns Count
		return self.held.clear()
	end 'run'
end 'Slot'

function bump(b Box) returns Count
	var s = Slot.create()
	s.attach(b)
	return s.run()
end 'bump'

function main() returns ExitCode
	let a = Box.create(2)
	let r = bump(a)
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:46:10: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'bump', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-parameter-held-two-frames-down-is-written -->
The holder passes through an intermediate function; `bump` keeps its parameter in the holder all the same.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function clear() returns Count
		n = 0
		return 0
	end 'clear'
end 'Box'

function zero(b Box) returns Count
	b.n = 0
	return 0
end 'zero'

type Env
	export var b as Box
	export var hits as Count

	static function create(b Box) returns Self
		return Self{b: b, hits: 0}
	end 'create'

	function run() returns Count
		return self.b.clear()
	end 'run'

	function bumpHits()
		hits = hits + 1
	end 'bumpHits'
end 'Env'

function relay(e Env) returns Count
	return e.run()
end 'relay'

function bump(b Box) returns Count
	var e = Env.create(b)
	return relay(e)
end 'bump'

function main() returns ExitCode
	let a = Box.create(2)
	let r = bump(a)
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:50:10: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'bump', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-local-let-stored-in-a-struct-is-written-by-the-holders-method -->
`Env.create` keeps its parameter in a `var` field, and `a` is read afterwards, so the refusal names the `let` at the factory call.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function clear() returns Count
		n = 0
		return 0
	end 'clear'
end 'Box'

function zero(b Box) returns Count
	b.n = 0
	return 0
end 'zero'

type Env
	export var b as Box
	export var hits as Count

	static function create(b Box) returns Self
		return Self{b: b, hits: 0}
	end 'create'

	function run() returns Count
		return self.b.clear()
	end 'run'

	function bumpHits()
		hits = hits + 1
	end 'bumpHits'
end 'Env'


function main() returns ExitCode
	let a = Box.create(2)
	var e = Env.create(a)
	let r = e.run()
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:42:14: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'Env.create', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-local-let-stored-in-a-var-field-is-written-by-a-method-of-the-holder -->
The holder is a `var`, so it may be written, but `Env.create` keeps the `let`'s record in a `var` field while `a` is read afterwards.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Env
	export var b as Box

	static function create(b Box) returns Self
		return Self{b: b}
	end 'create'

	function bump()
		self.b.n = 5
	end 'bump'
end 'Env'

function main() returns ExitCode
	let a = Box.create(2)
	var e = Env.create(a)
	e.bump()
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:26:14: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'Env.create', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-let-kept-by-a-holder-is-refused-though-its-method-writes-another-field -->
`Env.create` keeps the `let`'s record in a `var` field and `a` is read afterwards, so the call is refused, whichever field the method writes.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Env
	export var b as Box
	export var c as Box

	static function create(b Box) returns Self
		return Self{b: b, c: Box.create(9)}
	end 'create'

	function bump()
		self.c.n = 5
	end 'bump'
end 'Env'

function main() returns ExitCode
	let a = Box.create(2)
	var e = Env.create(a)
	e.bump()
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:27:14: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'Env.create', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-let-handed-to-a-parameter-kept-in-a-holder-is-refused-though-the-holder-writes-only-its-own-field -->
`bump` keeps its parameter in a `var` field of the holder, so a `let` handed to it and read afterwards is refused, though the method writes only the holder's own field.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function clear() returns Count
		n = 0
		return 0
	end 'clear'
end 'Box'

function zero(b Box) returns Count
	b.n = 0
	return 0
end 'zero'

type Env
	export var b as Box
	export var hits as Count

	static function create(b Box) returns Self
		return Self{b: b, hits: 0}
	end 'create'

	function run() returns Count
		return self.b.clear()
	end 'run'

	function bumpHits()
		hits = hits + 1
	end 'bumpHits'
end 'Env'

function bump(b Box) returns Count
	var e = Env.create(b)
	e.bumpHits()
	return e.hits
end 'bump'

function main() returns ExitCode
	let a = Box.create(2)
	let r = bump(a)
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:47:10: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'bump', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-let-handed-to-a-parameter-kept-in-one-of-two-fields-of-the-same-type-is-refused -->
`bump` keeps its parameter in the first field of the pair, so a `let` handed to it and read afterwards is refused, though the method writes only the second.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function clear() returns Count
		n = 0
		return 0
	end 'clear'
end 'Box'

function zero(b Box) returns Count
	b.n = 0
	return 0
end 'zero'

type Pair
	export var first as Box
	export var second as Box

	static function create(first Box, second Box) returns Self
		return Self{first: first, second: second}
	end 'create'

	function clearSecond() returns Count
		return self.second.clear()
	end 'clearSecond'
end 'Pair'

function bump(b Box) returns Count
	let other = Box.create(5)
	var p = Pair.create(b, second: other)
	return p.clearSecond()
end 'bump'

function main() returns ExitCode
	let a = Box.create(2)
	let r = bump(a)
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:43:10: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'bump', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-method-writing-its-own-field-on-a-let-parameter-is-refused -->
Calling a self-writing method on the parameter writes the record handed in, so a `let` handed to that parameter is refused.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function clear() returns Count
		n = 0
		return 0
	end 'clear'
end 'Box'

function zero(b Box) returns Count
	b.n = 0
	return 0
end 'zero'

function bump(b Box) returns Count
	return b.clear()
end 'bump'

function main() returns ExitCode
	let a = Box.create(2)
	let r = bump(a)
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:28:10: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: write-through-a-holder.a-read-only-method-on-a-holder-leaves-the-parameter-unwritten -->
Reading through the holder writes nothing.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function clear() returns Count
		n = 0
		return 0
	end 'clear'
end 'Box'

function zero(b Box) returns Count
	b.n = 0
	return 0
end 'zero'

type Env
	export let b as Box
	export var hits as Count

	static function create(b Box) returns Self
		return Self{b: b, hits: 0}
	end 'create'

	function run() returns Count
		return self.b.n
	end 'run'

	function bumpHits()
		hits = hits + 1
	end 'bumpHits'
end 'Env'

function bump(b Box) returns Count
	let e = Env.create(b)
	return e.run()
end 'bump'

function main() returns ExitCode
	let a = Box.create(2)
	let r = bump(a)
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```stdout
22
```
```exitcode
0
```

<!-- test: write-through-a-holder.error.a-let-handed-to-a-parameter-pushed-after-the-holder-is-written-is-refused -->
`stashAfterZero` pushes `b` into the holder's array, so it keeps `b`, and a `let` handed to it and read afterwards is refused, wherever the write sits.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Reg
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'
end 'Reg'

function zeroFirst(reg Reg)
	var w = try reg.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'

function stashAfterZero(reg Reg, b Box)
	zeroFirst(reg)
	reg.items.push(b)
end 'stashAfterZero'

function main() returns ExitCode
	var reg = Reg.create()
	let a = Box.create(2)
	stashAfterZero(reg, b: a)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:34:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stashAfterZero', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-let-handed-to-a-parameter-pushed-in-one-arm-is-refused -->
`stashArms` pushes `b` into the holder's array in one arm, so it keeps `b`, and a `let` handed to it and read afterwards is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Reg
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'
end 'Reg'

function zeroFirst(reg Reg)
	var w = try reg.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'


function stashArms(reg Reg, b Box, first bool)
	if first 'zeroes'
		zeroFirst(reg)
	end 'zeroes' else 'stores'
		reg.items.push(b)
	end 'stores'
end 'stashArms'

function main() returns ExitCode
	var reg = Reg.create()
	let a = Box.create(2)
	stashArms(reg, b: a, first: true)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:38:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stashArms', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-let-parked-in-a-holders-field-is-refused-beside-a-record-the-callee-creates-and-stores -->
`add` pushes `made` and then returns it, so the push is refused, and `parkAndGrow` keeps `b` in `reg.last`, so the `let` handed to it and read afterwards is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Reg
	export var items as Boxes
	export var last as Box

	static function create(seed Box) returns Self
		return Self{items: Boxes.create(), last: seed}
	end 'create'

	function add(n Count) returns Box
		let made = Box.create(n)
		items.push(made)
		return made
	end 'add'
end 'Reg'

function growAndKeep(reg Reg)
	let kept = reg.add(7)
	reg.last = kept
end 'growAndKeep'

function growAndBump(reg Reg)
	var fresh = reg.add(1)
	fresh.n = 2
end 'growAndBump'

function parkAndGrow(reg Reg, b Box)
	reg.last = b
	growAndBump(reg)
end 'parkAndGrow'

function main() returns ExitCode
	var reg = Reg.create(Box.create(0))
	growAndKeep(reg)
	let a = Box.create(5)
	parkAndGrow(reg, b: a)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:23:9: cannot store `let` 'made' into mutable place 'an element of the container push writes'; use clone()
error E3078: <fragment>:47:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'parkAndGrow', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-parameter-stored-in-a-holders-container-and-then-written-by-the-same-function-is-written -->
`stashAndZero` stores `b` into the container its holder parameter holds, so it keeps `b`, and a `let` handed to it and read afterwards is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Reg
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'
end 'Reg'

function zeroFirst(reg Reg)
	var w = try reg.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'

function stashAndZero(reg Reg, b Box)
	reg.items.push(b)
	zeroFirst(reg)
end 'stashAndZero'

function main() returns ExitCode
	var reg = Reg.create()
	let a = Box.create(2)
	stashAndZero(reg, b: a)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:34:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stashAndZero', which keeps it where something may write it; pass a clone()
```

<!-- test: write-through-a-holder.error.a-write-earlier-in-a-loop-body-follows-the-store-of-the-next-trip -->
`stashLoop` pushes `b` into the holder's container on every trip, so it keeps `b`, and a `let` handed to it and read afterwards is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Reg
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'
end 'Reg'

function zeroFirst(reg Reg)
	var w = try reg.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'


function stashLoop(reg Reg, b Box, times Count)
	var left = times

	while left > 0 'each'
		zeroFirst(reg)
		reg.items.push(b)
		left = left - 1
	end 'each'
end 'stashLoop'

function main() returns ExitCode
	var reg = Reg.create()
	let a = Box.create(2)
	stashLoop(reg, b: a, times: 3)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:40:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stashLoop', which keeps it where something may write it; pass a clone()
```
