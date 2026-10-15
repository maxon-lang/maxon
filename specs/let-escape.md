---
feature: let-escape
keywords: [let, immutable, escape, store, write, alias, E3159, E3078, container, global, holder, await]
category: semantics
---

# A `let` Record Stored Elsewhere Is Accepted While Nothing Writes It

## Documentation

A `let` names one record for its whole life. `E3159` refuses it at a call: where the `let` is handed to a
parameter the callee writes, including through a method the callee calls on it (`parameter-mutation.md`), or to a
parameter the callee stores into a struct field or a tuple slot that is then written
(`write-through-a-holder.md`). One call that writes the records of two `let`s passed directly to it refuses both.
A `let` holder is read-only all the way down: a method that writes it is `E3159`, and binding the record one of its
methods hands back to a `var` is `E3078`. Storing a local `let`'s record into a container, a field, a tuple slot
or a module variable, or handing it to a callee that keeps it, is not such a call, and is accepted.

A module `let` is image data. Pushing its record into a container, local or module-level, or storing it into a
field of a module variable, puts it where something may write it, and is `E3078`; store a `clone()`. Handed to a
function that stores its parameter in a container or a field, it is stored as a copy, so a write through the
copy leaves the `let` as it was. A `var` binding of a record read out of a holder is mutable, so `E3077` does not
report it.

## Tests

<!-- test: let-escape.a-global-container-written-before-the-let-is-stored-is-accepted -->
`register` stores the `let` record into a module container and `clobber` writes the container's first element. Neither call hands the `let` to a parameter the callee writes, so the program is accepted.

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

type Outer
	export var inner as Reg

	static function create() returns Self
		return Self{inner: Reg.create()}
	end 'create'
end 'Outer'

type Top
	export var outer as Outer

	static function create() returns Self
		return Self{outer: Outer.create()}
	end 'create'
end 'Top'

function zeroBox(w Box)
	w.n = 0
end 'zeroBox'

var registry = Boxes.create()

function register(b Box)
	registry.push(b)
end 'register'

function clobber()
	var w = try registry.get(0) otherwise return
	w.n = 99
end 'clobber'

function main() returns ExitCode
	let a = Box.create(2)
	register(a)
	clobber()
	return 0
end 'main'
```
```stdout
```
```exitcode
0
```

<!-- test: let-escape.a-global-container-of-another-type-leaves-the-let-alone -->
The container written later holds records of a different type than the `let`, so the write cannot reach it.

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

type Outer
	export var inner as Reg

	static function create() returns Self
		return Self{inner: Reg.create()}
	end 'create'
end 'Outer'

type Top
	export var outer as Outer

	static function create() returns Self
		return Self{outer: Outer.create()}
	end 'create'
end 'Top'

function zeroBox(w Box)
	w.n = 0
end 'zeroBox'

type Tag
	export var t as Count

	static function create(t Count) returns Self
		return Self{t: t}
	end 'create'
end 'Tag'

typealias Tags = Array with Tag

var registry = Boxes.create()
var tags = Tags.create()

function register(b Box)
	registry.push(b)
end 'register'

function retag()
	tags.push(Tag.create(1))
	var w = try tags.get(0) otherwise return
	w.t = 9
end 'retag'

function main() returns ExitCode
	let a = Box.create(2)
	register(a)
	retag()
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

<!-- test: let-escape.a-sibling-container-of-the-holder-leaves-the-let-alone -->
The record is stored into one container of the holder and a different container of the same holder is written, so the `let` is accepted.

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

type Outer
	export var inner as Reg

	static function create() returns Self
		return Self{inner: Reg.create()}
	end 'create'
end 'Outer'

type Top
	export var outer as Outer

	static function create() returns Self
		return Self{outer: Outer.create()}
	end 'create'
end 'Top'

function zeroBox(w Box)
	w.n = 0
end 'zeroBox'

type Pair
	export var left as Boxes
	export var right as Boxes

	static function create() returns Self
		return Self{left: Boxes.create(), right: Boxes.create()}
	end 'create'
end 'Pair'

function stashLeft(p Pair, b Box)
	p.left.push(b)
end 'stashLeft'

function resetRight(p Pair)
	p.right.clear()
end 'resetRight'

function main() returns ExitCode
	var p = Pair.create()
	let a = Box.create(2)
	stashLeft(p, b: a)
	resetRight(p)
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

<!-- test: let-escape.a-sibling-record-method-that-never-runs-after-the-store-is-accepted -->
The write by the sibling record's method happens before the store, so the `let` is accepted.

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

type Outer
	export var inner as Reg

	static function create() returns Self
		return Self{inner: Reg.create()}
	end 'create'
end 'Outer'

type Top
	export var outer as Outer

	static function create() returns Self
		return Self{outer: Outer.create()}
	end 'create'
end 'Top'

function zeroBox(w Box)
	w.n = 0
end 'zeroBox'

type Cursor
	export var current as Box
	export var steps as Count

	static function create(b Box) returns Self
		return Self{current: b, steps: 0}
	end 'create'

	function attach(b Box)
		current = b
	end 'attach'

	function wipe()
		current.n = 0
	end 'wipe'
end 'Cursor'

function main() returns ExitCode
	var c = Cursor.create(Box.create(1))
	let a = Box.create(2)
	c.attach(a)
	c.wipe()
	return 0
end 'main'
```
```stdout
```
```exitcode
0
```

<!-- test: let-escape.a-record-pushed-then-returned-without-a-write-is-accepted -->
The record is stored in a container and never written afterwards.

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

type Outer
	export var inner as Reg

	static function create() returns Self
		return Self{inner: Reg.create()}
	end 'create'
end 'Outer'

type Top
	export var outer as Outer

	static function create() returns Self
		return Self{outer: Outer.create()}
	end 'create'
end 'Top'

function zeroBox(w Box)
	w.n = 0
end 'zeroBox'

function build() returns Reg
	var reg = Reg.create()
	let a = Box.create(2)
	reg.items.push(a)
	return reg
end 'build'

function main() returns ExitCode
	let r = build()
	print("{r.items.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-builder-that-writes-before-it-stores-is-accepted -->
The builder writes its own field of the record before the record is stored anywhere, so the `let` is accepted.

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

type Outer
	export var inner as Reg

	static function create() returns Self
		return Self{inner: Reg.create()}
	end 'create'
end 'Outer'

type Top
	export var outer as Outer

	static function create() returns Self
		return Self{outer: Outer.create()}
	end 'create'
end 'Top'

function zeroBox(w Box)
	w.n = 0
end 'zeroBox'

function build() returns Reg
	var reg = Reg.create()
	var a = Box.create(2)
	zeroBox(a)
	reg.items.push(a)
	return reg
end 'build'

function main() returns ExitCode
	let r = build()
	print("{r.items.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-record-with-sixty-five-fields-is-accepted -->
A holder wider than the analysis keeps apart stays accepted when nothing writes it.

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

type Outer
	export var inner as Reg

	static function create() returns Self
		return Self{inner: Reg.create()}
	end 'create'
end 'Outer'

type Top
	export var outer as Outer

	static function create() returns Self
		return Self{outer: Outer.create()}
	end 'create'
end 'Top'

function zeroBox(w Box)
	w.n = 0
end 'zeroBox'

type Wide
	export var f0 as Count
	export var f1 as Count
	export var f2 as Count
	export var f3 as Count
	export var f4 as Count
	export var f5 as Count
	export var f6 as Count
	export var f7 as Count
	export var f8 as Count
	export var f9 as Count
	export var f10 as Count
	export var f11 as Count
	export var f12 as Count
	export var f13 as Count
	export var f14 as Count
	export var f15 as Count
	export var f16 as Count
	export var f17 as Count
	export var f18 as Count
	export var f19 as Count
	export var f20 as Count
	export var f21 as Count
	export var f22 as Count
	export var f23 as Count
	export var f24 as Count
	export var f25 as Count
	export var f26 as Count
	export var f27 as Count
	export var f28 as Count
	export var f29 as Count
	export var f30 as Count
	export var f31 as Count
	export var f32 as Count
	export var f33 as Count
	export var f34 as Count
	export var f35 as Count
	export var f36 as Count
	export var f37 as Count
	export var f38 as Count
	export var f39 as Count
	export var f40 as Count
	export var f41 as Count
	export var f42 as Count
	export var f43 as Count
	export var f44 as Count
	export var f45 as Count
	export var f46 as Count
	export var f47 as Count
	export var f48 as Count
	export var f49 as Count
	export var f50 as Count
	export var f51 as Count
	export var f52 as Count
	export var f53 as Count
	export var f54 as Count
	export var f55 as Count
	export var f56 as Count
	export var f57 as Count
	export var f58 as Count
	export var f59 as Count
	export var f60 as Count
	export var f61 as Count
	export var f62 as Count
	export var f63 as Count
	export var f64 as Count
	export var f65 as Count
	export var f66 as Count
	export var f67 as Count
	export var f68 as Count
	export var f69 as Count

	static function create() returns Self
		return Self{f0: 0, f1: 0, f2: 0, f3: 0, f4: 0, f5: 0, f6: 0, f7: 0, f8: 0, f9: 0, f10: 0, f11: 0, f12: 0, f13: 0, f14: 0, f15: 0, f16: 0, f17: 0, f18: 0, f19: 0, f20: 0, f21: 0, f22: 0, f23: 0, f24: 0, f25: 0, f26: 0, f27: 0, f28: 0, f29: 0, f30: 0, f31: 0, f32: 0, f33: 0, f34: 0, f35: 0, f36: 0, f37: 0, f38: 0, f39: 0, f40: 0, f41: 0, f42: 0, f43: 0, f44: 0, f45: 0, f46: 0, f47: 0, f48: 0, f49: 0, f50: 0, f51: 0, f52: 0, f53: 0, f54: 0, f55: 0, f56: 0, f57: 0, f58: 0, f59: 0, f60: 0, f61: 0, f62: 0, f63: 0, f64: 0, f65: 0, f66: 0, f67: 0, f68: 0, f69: 0}
	end 'create'
end 'Wide'

type WideHold
	export var wide as Wide
	export var box as Box

	static function create(wide Wide, box Box) returns Self
		return Self{wide: wide, box: box}
	end 'create'
end 'WideHold'

function main() returns ExitCode
	let w = Wide.create()
	let a = Box.create(2)
	var h = WideHold.create(w, box: a)
	h.wide.f69 = 3
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

<!-- test: let-escape.an-awaited-task-that-writes-nothing-is-accepted -->
The task awaited after the store writes nothing, so the `let` is accepted.

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

type Outer
	export var inner as Reg

	static function create() returns Self
		return Self{inner: Reg.create()}
	end 'create'
end 'Outer'

type Top
	export var outer as Outer

	static function create() returns Self
		return Self{outer: Outer.create()}
	end 'create'
end 'Top'

function zeroBox(w Box)
	w.n = 0
end 'zeroBox'

var registry = Boxes.create()

function register(b Box)
	registry.push(b)
end 'register'

function quiet() returns Count
	sleep(1)
	return 1
end 'quiet'

function main() returns ExitCode
	let a = Box.create(2)
	let handle = async quiet()
	register(a)
	let r = await handle
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

<!-- test: let-escape.a-let-read-for-the-last-time-before-the-write-is-accepted -->
`b` is not read after `touch` writes the container, so nothing observes the change through `b`.

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

	function add(n Count) returns Box
		let made = Box.create(n)
		items.push(made)
		return made
	end 'add'
end 'Reg'

function touch(reg Reg)
	var w = try reg.items.get(0) otherwise return
	w.n = 1
end 'touch'

function main() returns ExitCode
	var reg = Reg.create()
	let b = reg.add(7)
	print("{b.n}\n")
	touch(reg)
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: let-escape.error.a-module-lets-record-pushed-into-a-global-container-is-refused -->
A module `let` is read-only, and an element of a `var` container is a place something may write, so pushing the `let`'s record into one is refused; push a `clone()`.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

let shared = Box.create(7)
var registry = Boxes.create()

function register()
	registry.push(shared)
end 'register'

function clobber()
	var w = try registry.get(0) otherwise return
	w.n = 99
end 'clobber'

function main() returns ExitCode
	register()
	clobber()
	print("{shared.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:17:11: cannot store a value read out of a module-level `let` into mutable place 'an element of the container push writes'; use clone()
```

<!-- test: let-escape.a-module-lets-record-stored-by-a-callee-is-a-copy -->
The same holds when the `let` is handed to a function that stores its parameter in a container and in a field.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create(box Box) returns Self
		return Self{box: box}
	end 'create'
end 'Holder'

let shared = Box.create(7)
var registry = Boxes.create()
var holder = Holder.create(Box.create(0))

function stash(b Box)
	registry.push(b)
	holder.box = b
end 'stash'

function clobber()
	var w = try registry.get(0) otherwise return
	w.n = 99
	holder.box.n = 55
end 'clobber'

function main() returns ExitCode
	stash(shared)
	clobber()
	print("{shared.n} {holder.box.n}\n")
	return 0
end 'main'
```
```stdout
7 55
```
```exitcode
0
```

<!-- test: let-escape.a-module-lets-record-stored-in-a-field-is-a-copy -->
A record stored in a field of a holder is a copy too.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create(box Box) returns Self
		return Self{box: box}
	end 'create'
end 'Holder'

let shared = Box.create(7)

function main() returns ExitCode
	var h = Holder.create(shared)
	h.box.n = 99
	print("{shared.n} {h.box.n}\n")
	return 0
end 'main'
```
```stdout
7 99
```
```exitcode
0
```

<!-- test: let-escape.error.a-module-lets-record-pushed-into-a-local-array-is-refused -->
A local array's element is a mutable place, so pushing a module `let`'s record into it is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

let shared = Box.create(7)

function main() returns ExitCode
	var local = Boxes.create()
	local.push(shared)
	var w = try local.get(0) otherwise return 1
	w.n = 99
	print("{shared.n} {w.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:17:8: cannot store a value read out of a module-level `let` into mutable place 'an element of the container push writes'; use clone()
```

<!-- test: let-escape.error.a-module-lets-record-in-a-literal-a-map-and-a-global-field-is-refused -->
A field of a module variable is a mutable place, so storing a module `let`'s record into it is refused.

```maxon
typealias Count = int(0 to 1000)
typealias BoxMap = Map with (Count, Box)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create(box Box) returns Self
		return Self{box: box}
	end 'create'
end 'Holder'

let shared = Box.create(7)
var registry = Holder.create(Box.create(0))

function remember()
	registry.box = shared
end 'remember'

function bump()
	registry.box.n = 99
end 'bump'

function main() returns ExitCode
	var lit = [shared]
	var w = try lit.get(0) otherwise return 1
	w.n = 11

	var m = BoxMap.create()
	m.upsert(1, value: shared)
	var mv = try m.get(1) otherwise return 2
	mv.n = 22

	remember()
	bump()
	print("{shared.n} {w.n} {mv.n} {registry.box.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:25:11: cannot store a value read out of a module-level `let` into mutable place 'Holder.box'; use clone()
```

<!-- test: let-escape.a-module-lets-record-in-a-literal-and-a-map-value-is-a-copy -->
An array literal's element and a value handed to `upsert` are a copy of the module `let`'s record, so writing either leaves the `let` as it was.

```maxon
typealias Count = int(0 to 1000)
typealias BoxMap = Map with (Count, Box)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

let shared = Box.create(7)

function main() returns ExitCode
	var lit = [shared]
	var w = try lit.get(0) otherwise return 1
	w.n = 11

	var m = BoxMap.create()
	m.upsert(1, value: shared)
	var mv = try m.get(1) otherwise return 2
	mv.n = 22

	print("{shared.n} {w.n} {mv.n}\n")
	return 0
end 'main'
```
```stdout
7 11 22
```
```exitcode
0
```

<!-- test: let-escape.a-captured-let-of-a-type-nothing-writes-is-accepted -->
The same program with no write to any `Box` anywhere: the closure outliving `install` can only read the record, so the `let` is accepted.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Show = function() returns Count

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Reg
	export var items as Boxes
	export var show as Show

	static function create() returns Self
		return Self{items: Boxes.create(), show: function() gives 0}
	end 'create'
end 'Reg'

function install(reg Reg)
	let b = Box.create(1)
	reg.items.push(b)
	reg.show = function() gives b.n
end 'install'

function main() returns ExitCode
	var reg = Reg.create()
	install(reg)
	let w = try reg.items.get(0) otherwise return 1
	let f = reg.show
	print("{f()} {w.n}\n")
	return 0
end 'main'
```
```stdout
1 1
```
```exitcode
0
```

<!-- test: let-escape.a-string-let-stored-as-a-clone-is-accepted -->
The field holds a clone of the string, so the append leaves the `let` as it was.

```maxon
typealias Count = int(0 to 1000)

type H
	export var s as String

	static function create(s String) returns Self
		return Self{s: s}
	end 'create'
end 'H'

function grow(h H)
	h.s.append("x")
end 'grow'

function runtimeText(n Count) returns String
	return "{n}"
end 'runtimeText'

function main() returns ExitCode
	let s = "a{runtimeText(7)}"
	var h = H.create("")
	h.s = s.clone()
	grow(h)
	print("{s}\n")
	return 0
end 'main'
```
```stdout
a7
```
```exitcode
0
```

<!-- test: let-escape.a-task-that-writes-records-of-another-type-is-accepted -->
The spawned task writes a container of another record type, so no write at the yield can reach the `let`'s record.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias OtherBoxes = Array with Other

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Other
	export var m as Count

	static function create(m Count) returns Self
		return Self{m: m}
	end 'create'
end 'Other'

var others = OtherBoxes.create()

var registry = Boxes.create()

function register(b Box)
	registry.push(b)
end 'register'

function wipeFirst() returns Count
	var w = try others.get(0) otherwise return 1
	w.m = 0
	return 0
end 'wipeFirst'

function main() returns ExitCode
	let task = async wipeFirst()
	others.push(Other.create(1))
	let b = Box.create(2)
	register(b)
	Scheduler.yield()
	print("{b.n}\n")
	let r = await task
	return r as ExitCode
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: let-escape.a-module-lets-record-a-call-hands-back-is-stored-as-a-clone -->
Storing `pick().clone()` stores an independent record, so the write through the field leaves the `let` as it was.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create(box Box) returns Self
		return Self{box: box}
	end 'create'
end 'Holder'

let shared = Box.create(7)

function pick() returns Box
	return shared
end 'pick'

function adopt(h Holder)
	h.box = pick().clone()
end 'adopt'

function bump(h Holder)
	h.box.n = 99
end 'bump'

function main() returns ExitCode
	var h = Holder.create(Box.create(0))
	adopt(h)
	bump(h)
	print("{shared.n} {h.box.n}\n")
	return 0
end 'main'
```
```stdout
7 99
```
```exitcode
0
```

<!-- test: let-escape.a-let-record-holding-a-set-survives-a-write-to-another-container -->
A `let` record whose field is a `Set` is taken from a holder, and the holder then mutates a `Map` of its own. The write reaches the map's slot table, a record of another container, so it cannot reach the `Set` the `let` holds.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Set with Count
typealias Seen = Map with (Count, Count)
typealias Layouts = Array with Layout

type Layout
	export var names as Names

	static function create() returns Self
		return Self{names: Names.create()}
	end 'create'
end 'Layout'

type Holder
	var items as Layouts
	var seen as Seen

	static function create() returns Holder
		return Holder{items: Layouts.create(), seen: Seen.create()}
	end 'create'

	function add(layout Layout)
		self.items.push(layout)
	end 'add'

	function first() returns Layout
		return try self.items.get(0) otherwise panic("empty")
	end 'first'

	function note(id Count)
		self.seen.upsert(id, value: id)
	end 'note'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	holder.add(Layout.create())
	let layout = holder.first()
	holder.note(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-let-record-holding-a-set-survives-a-write-to-another-set-of-its-type -->
A `let` record whose field is a `Set` is taken from a holder, and the holder then inserts into a second `Set` of the very same type that it keeps beside the records. The write reaches the second set's slot table, which is reached through another field of the holder than the one the `let` record was stored under, so it cannot reach the `Set` the `let` holds.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Set with Count
typealias Seen = Set with Count
typealias Layouts = Array with Layout

type Layout
	export var names as Names

	static function create() returns Self
		return Self{names: Names.create()}
	end 'create'
end 'Layout'

type Holder
	var items as Layouts
	var seen as Seen

	static function create() returns Holder
		return Holder{items: Layouts.create(), seen: Seen.create()}
	end 'create'

	function add(layout Layout)
		self.items.push(layout)
	end 'add'

	function first() returns Layout
		return try self.items.get(0) otherwise panic("empty")
	end 'first'

	function note(id Count)
		self.seen.insert(id)
	end 'note'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	holder.add(Layout.create())
	let layout = holder.first()
	holder.note(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-let-record-stored-in-a-module-holder-survives-a-write-to-another-set-of-its-type -->
A `let` record is stored in a module variable's container, and a function writes a second `Set` of the same type that the module variable keeps in another field. The write is reached through the module variable's other field, so it cannot reach the record the `let` names.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Set with Count
typealias Seen = Set with Count
typealias Layouts = Array with Layout

type Layout
	export var names as Names

	static function create() returns Self
		return Self{names: Names.create()}
	end 'create'
end 'Layout'

type Holder
	export var items as Layouts
	export var seen as Seen

	static function create() returns Holder
		return Holder{items: Layouts.create(), seen: Seen.create()}
	end 'create'
end 'Holder'

var holder = Holder.create()

function note(id Count)
	holder.seen.insert(id)
end 'note'

function main() returns ExitCode
	let layout = Layout.create()
	holder.items.push(layout)
	note(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-getter-that-hands-back-a-clone-of-what-the-factory-minted-is-accepted -->
`grab` returns a clone of the record the holder keeps, so the `let` names a record of its own and the write in `grow` cannot reach it.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Box'

type Holder
	var box as Box

	static function create() returns Holder
		return Holder{box: Box.create()}
	end 'create'

	function grab() returns Box
		return self.box.clone()
	end 'grab'

	function grow()
		self.box.n = 5
	end 'grow'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	let box = holder.grab()
	holder.grow()
	print("{box.n}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-getter-that-hands-back-a-clone-of-a-set-the-factory-minted-is-accepted -->
The same for a `Set`: the clone the getter returns is not the set the holder inserts into.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Set with Count

type Holder
	var names as Names

	static function create() returns Holder
		return Holder{names: Names.create()}
	end 'create'

	function grab() returns Names
		return self.names.clone()
	end 'grab'

	function grow(id Count)
		self.names.insert(id)
	end 'grow'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	let names = holder.grab()
	holder.grow(7)
	print("{names.count()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-getter-of-one-field-survives-a-write-to-another-field-of-the-factory-holder -->
The holder keeps two `Array` fields of one type. The getter hands back one and `grow` pushes onto the other, so the write cannot reach the `let`.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Array with Count

type Holder
	var names as Names
	var other as Names

	static function create() returns Holder
		return Holder{names: Names.create(), other: Names.create()}
	end 'create'

	function grab() returns Names
		return self.names
	end 'grab'

	function grow()
		self.other.push(5)
	end 'grow'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	let names = holder.grab()
	holder.grow()
	print("{names.count()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-field-record-no-other-holder-shares-survives-a-write-through-the-other-holder -->
The same holder with no store that could share the `Set`: the second `Layout` holds a set of its own, so the write through it cannot reach the `let` record.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Set with Count
typealias Layouts = Array with Layout

type Layout
	export var names as Names

	static function create() returns Self
		return Self{names: Names.create()}
	end 'create'
end 'Layout'

type Holder
	var items as Layouts
	var extra as Layout

	static function create(extra Layout) returns Holder
		return Holder{items: Layouts.create(), extra: extra}
	end 'create'

	function add(layout Layout)
		self.items.push(layout)
	end 'add'

	function touch(id Count)
		self.extra.names.insert(id)
	end 'touch'
end 'Holder'

function main() returns ExitCode
	let layout = Layout.create()
	var other = Layout.create()
	var holder = Holder.create(other)
	holder.add(layout)
	holder.touch(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-let-handed-to-a-factory-that-stores-a-clone-is-accepted -->
The same factory storing `first.clone()`: the holder owns a record of its own, so the write through it cannot reach the `let` record.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Set with Count

type Layout
	export var names as Names

	static function create() returns Self
		return Self{names: Names.create()}
	end 'create'
end 'Layout'

type Holder
	var first as Layout

	static function create(first Layout) returns Holder
		return Holder{first: first.clone()}
	end 'create'

	function touch(id Count)
		self.first.names.insert(id)
	end 'touch'
end 'Holder'

function main() returns ExitCode
	let layout = Layout.create()
	var holder = Holder.create(layout)
	holder.touch(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-let-stored-two-factories-down-survives-a-write-to-the-spare-beside-it -->
The nested holder keeps a second record beside the `let` one, and `touch` writes only that spare, so the `let` record is untouched.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Set with Count

type Layout
	export var names as Names

	static function create() returns Self
		return Self{names: Names.create()}
	end 'create'
end 'Layout'

type Inner
	export var layout as Layout
	export var spare as Layout

	static function create(layout Layout, spare Layout) returns Inner
		return Inner{layout: layout, spare: spare}
	end 'create'
end 'Inner'

type Holder
	export var inner as Inner

	static function create(layout Layout, spare Layout) returns Holder
		return Holder{inner: Inner.create(layout, spare: spare)}
	end 'create'

	function touch(id Count)
		self.inner.spare.names.insert(id)
	end 'touch'
end 'Holder'

function main() returns ExitCode
	let layout = Layout.create()
	var spare = Layout.create()
	var holder = Holder.create(layout, spare: spare)
	holder.touch(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-record-stored-after-the-write-into-a-holder-an-earlier-call-escaped-is-accepted -->
`world.open()` stores the new block in the world and hands it back, so the block escapes at that call. `attach` later stores the `let` record in that block, after `world.scribble()` has written the array `wire` stored in another block. At the write the `let` record is in no block, so it cannot be the array written, and the `let` is accepted.

```maxon
typealias Count = int(0 to 1000)
typealias Args = Array with Count
typealias Edges = Array with Edge
typealias Blocks = Array with Block

type Edge
	export var args as Args

	static function create(args Args) returns Self
		return Self{args: args}
	end 'create'
end 'Edge'

type Block
	export var edges as Edges

	static function create() returns Self
		return Self{edges: Edges.create()}
	end 'create'
end 'Block'

type Book
	export var blocks as Blocks

	static function create() returns Self
		return Self{blocks: Blocks.create()}
	end 'create'
end 'Book'

type Routed
	export var args as Args

	static function create() returns Self
		return Self{args: Args.create()}
	end 'create'
end 'Routed'

type World
	export var book as Book
	export var routed as Routed

	static function create() returns Self
		return Self{book: Book.create(), routed: Routed.create()}
	end 'create'

	function open() returns Block
		var block = Block.create()
		self.book.blocks.push(block)
		return block
	end 'open'

	function scribble()
		self.routed.args.push(1)
	end 'scribble'
end 'World'

function attach(block Block, args Args)
	block.edges.push(Edge.create(args))
end 'attach'

function wire(world World, block Block)
	attach(block, args: world.routed.args)
end 'wire'

function run(world World) returns Count
	let carried = Args.create()
	var block = world.open()
	world.scribble()
	attach(block, args: carried)
	return carried.count()
end 'run'

function main() returns ExitCode
	var world = World.create()
	var other = world.open()
	wire(world, block: other)
	print("{run(world)}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.error.a-var-bound-to-a-record-read-out-of-a-let-holder-is-refused -->
`holder` is a `let`, so it is read-only all the way down: `holder.grow()` writes through it, and binding the record `holder.grab()` hands back to a `var` would give that record a second writer. Both are refused.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Box'

type Holder
	var box as Box

	static function create() returns Holder
		return Holder{box: Box.create()}
	end 'create'

	function grab() returns Box
		return self.box
	end 'grab'

	function grow()
		self.box.n = 5
	end 'grow'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	var box = holder.grab()
	holder.grow()
	print("{box.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:31:9: cannot pass 'holder', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
error E3078: <fragment>:30:6: cannot assign the result of 'grab', which may share storage with immutable variable 'holder', to mutable binding 'box'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-escape.a-let-bound-to-a-record-minted-in-the-loop-is-not-written-on-a-later-trip -->
The record is minted inside the loop, so trip two's `clobber` writes trip one's record and never the one `r` names.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

var registry = Boxes.create()

function register(b Box)
	registry.push(b)
end 'register'

function clobber()
	var w = try registry.get(0) otherwise return
	w.n = 99
end 'clobber'

function main() returns ExitCode
	var i = 0 as Count

	while i < 2 'trips'
		let r = Box.create(2)
		clobber()
		register(r)
		print("{r.n}\n")
		i = i + 1
	end 'trips'

	return 0
end 'main'
```
```stdout
2
2
```
```exitcode
0
```

<!-- test: let-escape.error.a-callee-writing-a-let-through-its-holders-own-method-is-refused -->
`bumpIt` writes the `let` it is handed through a method called on it. A method that writes its receiver writes the record, so the `let` is refused at the call.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		n = n + 1
	end 'bump'
end 'Box'

type Reg
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'
end 'Reg'

function stash(reg Reg, b Box)
	reg.items.push(b)
end 'stash'

function bumpIt(reg Reg, b Box)
	if reg.items.count() > 100 'tooMany'
		return
	end 'tooMany'

	b.bump()
end 'bumpIt'

function main() returns ExitCode
	var reg = Reg.create()
	let a = Box.create(2)
	stash(reg, b: a)
	bumpIt(reg, b: a)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:41:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: let-escape.a-write-through-slots-only-their-own-record-holds-is-accepted-though-the-let-holds-a-map-of-the-same-instance -->
`fill` writes the map in `world.cache`, a slot only the `Cache` record holds. The `let` record holds a map of the same instance in a field of its own, but the written map is not stored in it, so the write cannot reach the `let`.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Map with (String, Count)

type Sigs
	export var names as Names

	static function create() returns Self
		return Self{names: Names.create()}
	end 'create'
end 'Sigs'

type Memo
	export var sigs as Sigs

	static function create() returns Self
		return Self{sigs: Sigs.create()}
	end 'create'
end 'Memo'

type Cache
	export var names as Names

	static function create() returns Self
		return Self{names: Names.create()}
	end 'create'
end 'Cache'

type World
	export var memo as Memo
	export var cache as Cache

	static function create() returns Self
		return Self{memo: Memo.create(), cache: Cache.create()}
	end 'create'
end 'World'

function sigsOf(world World) returns Sigs
	let made = Sigs.create()
	world.memo.sigs = made
	return made
end 'sigsOf'

function fill(world World)
	world.cache.names.upsert("k", value: 1)
end 'fill'

function main() returns ExitCode
	var world = World.create()
	let sigs = sigsOf(world)
	fill(world)
	print("{sigs.names.count()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-write-through-a-long-chain-of-single-parent-slots-is-accepted -->
The written set sits five records down a chain no `let` record is part of. Its slots each hold one record, so it is not the set the `let` holds.

```maxon
typealias Count = int(0 to 1000)
typealias Flags = Set with Count

type Sigs
	export var flags as Flags

	static function create() returns Self
		return Self{flags: Flags.create()}
	end 'create'
end 'Sigs'

type Memo
	export var sigs as Sigs

	static function create() returns Self
		return Self{sigs: Sigs.create()}
	end 'create'
end 'Memo'

type Leaf
	export var members as Flags

	static function create() returns Self
		return Self{members: Flags.create()}
	end 'create'
end 'Leaf'

type Level4
	export var leaf as Leaf

	static function create() returns Self
		return Self{leaf: Leaf.create()}
	end 'create'
end 'Level4'

type Level3
	export var next as Level4

	static function create() returns Self
		return Self{next: Level4.create()}
	end 'create'
end 'Level3'

type Level2
	export var next as Level3

	static function create() returns Self
		return Self{next: Level3.create()}
	end 'create'
end 'Level2'

type Level1
	export var next as Level2

	static function create() returns Self
		return Self{next: Level2.create()}
	end 'create'
end 'Level1'

type World
	export var memo as Memo
	export var deep as Level1

	static function create() returns Self
		return Self{memo: Memo.create(), deep: Level1.create()}
	end 'create'
end 'World'

function sigsOf(world World) returns Sigs
	let made = Sigs.create()
	world.memo.sigs = made
	return made
end 'sigsOf'

function mark(world World)
	world.deep.next.next.next.leaf.members.insert(3)
end 'mark'

function main() returns ExitCode
	var world = World.create()
	let sigs = sigsOf(world)
	mark(world)
	print("{sigs.flags.count()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-write-to-a-record-the-callee-copied-rather-than-held-is-accepted -->
`sigsOf` builds each declaration around a fresh layout that copies the size of the one in `world.artifact`, so the layout `measure` writes is not one the `let` holds.

```maxon
typealias Count = int(0 to 1000)
typealias Layouts = Array with Layout
typealias Decls = Array with Decl

type Detail
	export var size as Count

	static function create() returns Self
		return Self{size: 0}
	end 'create'
end 'Detail'

type Layout
	export var detail as Detail

	static function create() returns Self
		return Self{detail: Detail.create()}
	end 'create'
end 'Layout'

type Decl
	export var layout as Layout

	static function create(layout Layout) returns Self
		return Self{layout: layout}
	end 'create'
end 'Decl'

type Artifact
	export var layouts as Layouts

	static function create() returns Self
		var made = Layouts.create()
		made.push(Layout.create())
		return Self{layouts: made}
	end 'create'
end 'Artifact'

type Sigs
	export var decls as Decls

	static function create() returns Self
		return Self{decls: Decls.create()}
	end 'create'
end 'Sigs'

type Memo
	export var sigs as Sigs

	static function create() returns Self
		return Self{sigs: Sigs.create()}
	end 'create'
end 'Memo'

type World
	export var memo as Memo
	export var artifact as Artifact

	static function create() returns Self
		return Self{memo: Memo.create(), artifact: Artifact.create()}
	end 'create'
end 'World'

function sigsOf(world World) returns Sigs
	var made = Sigs.create()

	for layout in world.artifact.layouts 'each'
		var copy = Layout.create()
		copy.detail.size = layout.detail.size
		made.decls.push(Decl.create(copy))
	end 'each'

	world.memo.sigs = made
	return made
end 'sigsOf'

function measure(world World)
	var layout = try world.artifact.layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	var world = World.create()
	let sigs = sigsOf(world)
	measure(world)
	print("{sigs.decls.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.error.a-call-writing-two-lets-passed-directly-refuses-both -->
`bump` writes the records of both of its parameters, and the call passes two `let` records, so both are named.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function bump(a Box, b Box)
	a.n = 1
	b.n = 2
end 'bump'

function main() returns ExitCode
	let a = Box.create(0)
	let b = Box.create(0)
	bump(a, b: b)
	print("{a.n} {b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:20:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'a' (in main); pass a clone()
error E3159: <fragment>:20:2: cannot pass 'b', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: let-escape.a-let-held-by-a-local-holder-that-holds-copies-of-the-worlds-layouts-is-accepted -->
The same program with `made` holding copies of the layouts, so `measure` writes only records `made` does not hold.

```maxon
typealias Count = int(0 to 1000)
typealias Layouts = Array with Layout
typealias Decls = Array with Decl

type Detail
	export var size as Count

	static function create() returns Self
		return Self{size: 0}
	end 'create'
end 'Detail'

type Layout
	export var detail as Detail

	static function create() returns Self
		return Self{detail: Detail.create()}
	end 'create'
end 'Layout'

type Decl
	export var layout as Layout

	static function create(layout Layout) returns Self
		return Self{layout: layout}
	end 'create'
end 'Decl'

type Artifact
	export var layouts as Layouts

	static function create() returns Self
		var made = Layouts.create()
		made.push(Layout.create())
		return Self{layouts: made}
	end 'create'
end 'Artifact'

type Sigs
	export var decls as Decls

	static function create() returns Self
		return Self{decls: Decls.create()}
	end 'create'
end 'Sigs'

type Memo
	export var sigs as Sigs

	static function create() returns Self
		return Self{sigs: Sigs.create()}
	end 'create'
end 'Memo'

type World
	export var memo as Memo
	export var artifact as Artifact

	static function create() returns Self
		return Self{memo: Memo.create(), artifact: Artifact.create()}
	end 'create'
end 'World'

function measure(world World)
	var layout = try world.artifact.layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	var world = World.create()
	var made = Sigs.create()

	for layout in world.artifact.layouts 'each'
		var copy = Layout.create()
		copy.detail.size = layout.detail.size
		made.decls.push(Decl.create(copy))
	end 'each'

	world.memo.sigs = made
	measure(world)
	print("{made.decls.count()}\n")
	return 0
end 'main'
```

```stdout
1
```
```exitcode
0
```
