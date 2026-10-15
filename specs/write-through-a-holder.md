---
feature: write-through-a-holder
keywords: [mutation, parameter, let, immutable, holder, struct, tuple, field, E3159, E3187]
category: semantics
---

# A Write Through a Record That Holds a Parameter Writes the Parameter

## Documentation

A record stored into a struct field or a tuple slot is that field's record for as long as the holder lives, so a
write through the field is a write of the record that was stored. A function that builds such a holder from one of
its parameters — directly, or through a factory function that returns it — therefore writes the parameter whenever
the holder's methods, or a function the holder is handed to, write that field. `E3159` refuses a `let` handed to
that parameter, and a method behind a comparison operator may not do it to its operand or receiver (`E3187`).

The holder's field is the parameter wherever the write sits in the function: a write through it that comes before
the store, or in the other arm of an `if`, writes the parameter too.

The write is attributed to the field it goes through. A holder whose method writes ITS OWN field, or a sibling
field the parameter was not stored into, leaves the parameter unwritten. A method that writes its own receiver,
called directly on a parameter, writes that parameter (`parameter-mutation.md`). Only a struct field or
a tuple slot holds the parameter: a parameter pushed into an array is no field of the holder, so a write through
an element the array hands back leaves the parameter unwritten.

## Tests

<!-- test: write-through-a-holder.error.a-parameter-stored-in-a-struct-is-written-by-the-holders-method -->
A parameter stored into a struct is the struct's field for as long as it lives, so a method of the struct that writes the field writes the parameter, and a `let` handed to `bump` is refused.

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
error E3159: <fragment>:46:10: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: write-through-a-holder.error.a-parameter-stored-in-a-struct-is-written-through-its-field-by-a-callee -->
The holder reaches a callee whole, and the callee writes the field.

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
error E3159: <fragment>:50:10: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: write-through-a-holder.error.a-parameter-stored-in-a-tuple-is-written-by-a-callee -->
A tuple holds what it was built from.

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
error E3159: <fragment>:33:10: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: write-through-a-holder.error.a-parameter-held-by-a-record-another-function-returned-is-written -->
The holder comes back from a factory function and the caller writes through it.

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
error E3159: <fragment>:50:10: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: write-through-a-holder.error.a-parameter-attached-to-a-holder-by-a-method-is-written -->
A method that stores its argument into the holder's field puts the record there as a constructor does.

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
error E3159: <fragment>:46:10: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: write-through-a-holder.error.a-parameter-held-two-frames-down-is-written -->
The holder passes through an intermediate function before the write.

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
error E3159: <fragment>:50:10: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: write-through-a-holder.error.a-local-let-stored-in-a-struct-is-written-by-the-holders-method -->
The refusal names the `let` whose record the holder's method writes.

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
error E3159: <fragment>:43:12: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: write-through-a-holder.error.a-local-let-stored-in-a-var-field-is-written-by-a-method-of-the-holder -->
The holder is a `var`, so it may be written, but the field its method writes through holds the `let`'s record.

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
error E3159: <fragment>:27:4: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: write-through-a-holder.a-holders-method-writing-another-field-leaves-the-let-alone -->
The method writes through a field that does not hold the `let`'s record, so the call is accepted.

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
```stdout
2
```
```exitcode
0
```

<!-- test: write-through-a-holder.a-holders-own-field-write-leaves-the-parameter-unwritten -->
The holder's method writes its own field, not the record it holds, so `bump` leaves its parameter alone and a `let` is accepted.

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
```stdout
21
```
```exitcode
0
```

<!-- test: write-through-a-holder.a-sibling-field-of-the-same-type-leaves-the-parameter-unwritten -->
Two fields of one type are two places: the method writes the one the parameter was not stored into.

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
```stdout
20
```
```exitcode
0
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

<!-- test: write-through-a-holder.a-parameter-stored-after-the-holder-is-written-is-unwritten -->
`stashAfterZero` pushes `b` into the holder's array, and `zeroFirst` writes an element the array hands back, so `b` is not written through a field and a `let` is accepted.

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
```stdout
2
```
```exitcode
0
```

<!-- test: write-through-a-holder.a-write-in-the-other-arm-does-not-follow-the-store -->
The write and the store sit in the two arms of one `if`; the store pushes `b` into the holder's array, and a write through an element the array hands back does not write `b`.

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
```stdout
2
```
```exitcode
0
```

<!-- test: write-through-a-holder.a-record-the-callee-creates-and-stores-is-not-the-parameter-stored-beside-it -->
`parkAndGrow` stores `b` in `reg.last` and then calls `growAndBump`, which writes only the record `reg.add` creates and stores in the container, so a `let` handed to `parkAndGrow` is accepted.

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
```stdout
5
```
```exitcode
0
```
