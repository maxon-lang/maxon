---
feature: let-escape
keywords: [let, immutable, escape, store, keep, write, alias, E3159, E3078, E3188, container, global, holder, await]
category: semantics
---

# A `let` Record Is Kept Only Where Nothing Can Write It

## Documentation

A `let` names a value nothing may write. It binds a value that is already read-only (another `let`, a `let` field,
a `readonly` element, a module `let`), a fresh value nothing else holds, or a parameter or a part of one. A `let`
bound to a value something may still write, such as an element of a `var` container, a record a callee also stored
elsewhere, or what a getter hands back from a `var` holder, is `E3188`.

A `let`'s record is stored only where nothing can write it. Storing it into a `var` field, a mutable container
element or a module variable, or handing it to a parameter the callee keeps in such a place, is `E3078` while the
`let` is still read afterwards; at its last use a fresh `let` is moved there instead. A write through a `let`, or
handing it to a parameter the callee writes, is `E3159`.

A module `let` is image data: storing its record anywhere writable, directly, in an array literal or a map value,
or through a callee that keeps its parameter, is `E3078`; store a `clone()`. A `var` binding of a record a holder
still holds is writable, so `E3077` does not report it.

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

<!-- test: let-escape.error.a-let-kept-in-a-global-container-of-another-type-and-read-again-is-refused -->
`register` keeps its parameter in a module container, and `a` is read after the call, so handing `a` to it is refused, whatever type the later write reaches.

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
```maxoncstderr
error E3078: <fragment>:66:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'register', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-let-kept-in-one-container-of-a-holder-and-read-again-is-refused -->
`stashLeft` keeps its parameter in one container of the holder, and `a` is read after the call, so it is refused, whichever container is written later.

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
```maxoncstderr
error E3078: <fragment>:61:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stashLeft', which keeps it where something may write it; pass a clone()
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

<!-- test: let-escape.error.a-let-kept-in-a-var-field-of-a-record-with-sixty-five-fields-is-refused -->
`WideHold.create` keeps `box` in a `var` field, and `a` is read after the call, so it is refused however wide the holder is; `w` is not read again, so it moves into the holder.

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
```maxoncstderr
error E3078: <fragment>:130:19: cannot pass 'a', which is read-only (a `let` binding), to parameter 'box' of 'WideHold.create', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-let-kept-by-a-callee-while-a-task-runs-and-read-again-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
On wasm32-wasi this case's expected diagnostic is displaced by E3104 ('sleep' lowers to the runtime entry '__gt_sleep', which has no wasm32-wasi implementation), so the lane is excluded.

`register` keeps its parameter in a module container and `a` is read afterwards, so the call is refused whatever the task does.

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
```maxoncstderr
error E3078: <fragment>:55:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'register', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-let-the-callee-pushes-and-returns-is-refused-at-the-push-though-the-caller-reads-it-before-the-write -->
`add` pushes `made` and then returns it, so the push is not its last use and is refused, before any write; what `add` returns is its `let`, which is read-only, so `b` may bind it.

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
```maxoncstderr
error E3078: <fragment>:22:9: cannot store `let` 'made' into mutable place 'an element of the container push writes'; use clone()
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

<!-- test: let-escape.error.a-module-lets-record-handed-to-a-callee-that-keeps-it-is-refused -->
`stash` keeps its parameter in a container and in a field, so handing it a module `let` is refused; pass a `clone()`.

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
```maxoncstderr
error E3078: <fragment>:37:2: cannot pass 'shared', which is read-only (a module-level `let`), to parameter 'b' of 'stash', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-module-lets-record-handed-to-a-factory-that-keeps-it-in-a-field-is-refused -->
`Holder.create` keeps its parameter in a `var` field, so handing it a module `let` is refused.

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
```maxoncstderr
error E3078: <fragment>:23:17: cannot pass 'shared', which is read-only (a module-level `let`), to parameter 'box' of 'Holder.create', which keeps it where something may write it; pass a clone()
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
An array literal's element, a map value and a field of a module variable are each a mutable place, so storing a module `let`'s record into any of them is refused.

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
error E3078: <fragment>:33:13: cannot store a value read out of a module-level `let` into mutable place 'an element of the array literal'; use clone()
error E3078: <fragment>:38:4: cannot store a value read out of a module-level `let` into mutable place 'an element of the container upsert writes'; use clone()
```

<!-- test: let-escape.error.a-module-lets-record-in-a-literal-and-a-map-value-is-refused -->
The same stores without the module variable: an array literal's element and a value handed to `upsert` are mutable places, so both are refused.

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
```maxoncstderr
error E3078: <fragment>:16:13: cannot store a value read out of a module-level `let` into mutable place 'an element of the array literal'; use clone()
error E3078: <fragment>:21:4: cannot store a value read out of a module-level `let` into mutable place 'an element of the container upsert writes'; use clone()
```

<!-- test: let-escape.error.a-captured-let-pushed-into-a-holder-is-refused-though-nothing-writes-its-type -->
`b` is captured after the push, so the push is not its last use and is refused; the element `main` reads back is held by the container, so binding it to a `let` is refused too.

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
```maxoncstderr
error E3078: <fragment>:25:12: cannot store `let` 'b' into mutable place 'an element of the container push writes'; use clone()
error E3188: <fragment>:32:6: cannot bind `let` 'w' to a value reached through 'reg', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
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

<!-- test: let-escape.error.a-let-kept-in-a-module-container-across-a-yield-and-read-again-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
On wasm32-wasi the expected diagnostic is followed by an extra E3104 ('Scheduler.yield' lowers to the runtime entry '__gt_resched', which has no wasm32-wasi implementation) that the pinned output does not list, so the lane is excluded.

`register` keeps its parameter in a module container and `b` is read afterwards, so the call is refused, whatever type the spawned task writes.

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
```maxoncstderr
error E3078: <fragment>:40:2: cannot pass 'b', which is read-only (a `let` binding), to parameter 'b' of 'register', which keeps it where something may write it; pass a clone()
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

<!-- test: let-escape.error.a-let-bound-to-a-record-a-var-holder-hands-back-is-refused-though-another-container-is-written -->
`first` hands back an element of the holder's container, and `holder` is a `var`, so the record is one something may still write, and the `let` is refused whichever container is written later.

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
```maxoncstderr
error E3188: <fragment>:39:6: cannot bind `let` 'layout' to a value reached through 'holder', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-let-bound-to-a-record-a-var-holder-hands-back-is-refused-though-another-set-of-its-type-is-written -->
The same with a second `Set` of the very same type beside the records: the `let` of what `first` hands back from a `var` holder is refused.

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
```maxoncstderr
error E3188: <fragment>:39:6: cannot bind `let` 'layout' to a value reached through 'holder', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-let-pushed-into-a-module-holder-and-read-again-is-refused -->
The `let` record is pushed into a module variable's container and read afterwards, so the push is refused, whichever field of the module variable is written later.

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
```maxoncstderr
error E3078: <fragment>:32:15: cannot store `let` 'layout' into mutable place 'an element of the container push writes'; use clone()
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

<!-- test: let-escape.error.a-let-bound-to-a-field-a-getter-hands-back-from-a-var-holder-is-refused -->
`grab` hands back the holder's own field, and `holder` is a `var`, so the `let` is refused whichever field `grow` writes.

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
```maxoncstderr
error E3188: <fragment>:24:6: cannot bind `let` 'names' to a value reached through 'holder', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-let-kept-by-a-holders-method-and-read-again-is-refused -->
`Holder.add` keeps its parameter in the holder's container and `layout` is read afterwards, so the call is refused, whatever `touch` writes.

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
```maxoncstderr
error E3078: <fragment>:35:9: cannot pass 'layout', which is read-only (a `let` binding), to parameter 'layout' of 'Holder.add', which keeps it where something may write it; pass a clone()
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

<!-- test: let-escape.error.a-let-kept-two-factories-down-and-read-again-is-refused -->
`Holder.create` hands `layout` to `Inner.create`, which keeps it in a `var` field, so `Holder.create` keeps it too, and `layout` is read afterwards: the call is refused, whichever record `touch` writes.

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
```maxoncstderr
error E3078: <fragment>:37:22: cannot pass 'layout', which is read-only (a `let` binding), to parameter 'layout' of 'Holder.create', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-let-kept-after-the-write-into-a-holder-an-earlier-call-escaped-is-refused -->
`attach` keeps its `args` in an edge it pushes into the block, and `carried` is read afterwards, so the call is refused, before or after any write.

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
```maxoncstderr
error E3078: <fragment>:70:2: cannot pass 'carried', which is read-only (a `let` binding), to parameter 'args' of 'attach', which keeps it where something may write it; pass a clone()
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

<!-- test: let-escape.error.a-let-minted-in-the-loop-kept-by-a-callee-and-read-again-is-refused -->
`register` keeps its parameter in a module container and `r` is read after the call, so it is refused on every trip.

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
```maxoncstderr
error E3078: <fragment>:30:3: cannot pass 'r', which is read-only (a `let` binding), to parameter 'b' of 'register', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-callee-writing-a-let-through-its-holders-own-method-is-refused -->
`stash` keeps `a` in the container and `a` is read afterwards, and `bumpIt` writes the `let` it is handed through a method called on it. Both calls are refused.

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
error E3078: <fragment>:40:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stash', which keeps it where something may write it; pass a clone()
error E3159: <fragment>:41:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: let-escape.error.a-let-stored-into-a-var-field-and-returned-is-refused-though-only-another-map-is-written -->
`sigsOf` stores `made` into `world.memo` and then returns it, so the store is not its last use and is refused, whatever `fill` writes.

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
```maxoncstderr
error E3078: <fragment>:40:13: cannot store `let` 'made' into mutable place 'Memo.sigs'; use clone()
```

<!-- test: let-escape.error.a-let-stored-into-a-var-field-and-returned-is-refused-though-only-a-long-chain-is-written -->
The same store of `made` before it is returned, with the written set five records down a chain: the store is refused.

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
```maxoncstderr
error E3078: <fragment>:72:13: cannot store `let` 'made' into mutable place 'Memo.sigs'; use clone()
```

<!-- test: let-escape.error.a-let-bound-to-a-record-the-callee-also-stored-in-the-world-is-refused-though-it-holds-copies -->
`sigsOf` stores the `var` it returns into `world.memo`, so the record it hands back is one something may still write, and the `let` of it is refused, whatever the record holds.

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
```maxoncstderr
error E3188: <fragment>:85:6: cannot bind `let` 'sigs' to a value reached through 'world', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
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

<!-- test: let-escape.error.a-record-stored-in-a-global-container-is-written-through-the-global -->
`register` keeps its parameter in a module container, and `a` is read after the call, so handing `a` to it is refused.

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
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:54:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'register', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-record-stored-into-a-parameters-container-is-written-through-the-parameter -->
`stash` keeps its parameter in a container its first parameter holds, and `a` is read afterwards, so the call is refused.

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

function stash(reg Reg, b Box)
	reg.items.push(b)
end 'stash'

function zeroFirst(reg Reg)
	var w = try reg.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'

function main() returns ExitCode
	var reg = Reg.create()
	let a = Box.create(2)
	stash(reg, b: a)
	zeroFirst(reg)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:53:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stash', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-record-stored-two-fields-down-is-written-through-the-holder -->
`stash` keeps its parameter two fields below its first parameter, and `a` is read afterwards, so the call is refused.

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

function stash(o Outer, b Box)
	o.inner.items.push(b)
end 'stash'

function zeroFirst(o Outer)
	var w = try o.inner.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'

function main() returns ExitCode
	var o = Outer.create()
	let a = Box.create(2)
	stash(o, b: a)
	zeroFirst(o)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:53:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stash', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-record-stored-three-fields-down-through-a-loaded-record-is-written -->
`stash` keeps its parameter in a container reached through a record it loads from its first parameter, and `a` is read afterwards, so the call is refused.

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

function stash(t Top, b Box)
	var o = t.outer
	o.inner.items.push(b)
end 'stash'

function zeroFirst(t Top)
	var w = try t.outer.inner.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'

function main() returns ExitCode
	var t = Top.create()
	let a = Box.create(2)
	stash(t, b: a)
	zeroFirst(t)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:54:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stash', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-record-stored-by-a-callee-into-a-field-container-is-written -->
`stash` hands its parameter to `put`, which keeps it in a container, so `stash` keeps it too, and `a` is read afterwards: the call is refused.

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

function put(items Boxes, b Box)
	items.push(b)
end 'put'

function stash(reg Reg, b Box)
	put(reg.items, b: b)
end 'stash'

function zeroFirst(reg Reg)
	var w = try reg.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'

function main() returns ExitCode
	var reg = Reg.create()
	let a = Box.create(2)
	stash(reg, b: a)
	zeroFirst(reg)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:57:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stash', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-record-stored-by-a-callback-is-written -->
The function value keeps its parameter `x` in the holder's container, but `Taker` does not declare that parameter `keep`, so converting the function value to `Taker` is refused.

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

typealias Taker = function(Reg, Box) returns Count

function noteIn(reg Reg, x Box) returns Count
	reg.items.push(x)
	return 1
end 'noteIn'

function give(take Taker, reg Reg, b Box) returns Count
	return take(reg, b)
end 'give'

function zeroFirst(reg Reg)
	var w = try reg.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'

function main() returns ExitCode
	var reg = Reg.create()
	let a = Box.create(2)
	let seen = give(function(r Reg, x Box) gives noteIn(r, x: x), reg: reg, b: a)
	zeroFirst(reg)
	print("{a.n}{seen}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3189: <fragment>:60:18: function value for 'Taker' keeps its parameter 'x', but 'Taker' does not declare it `keep`
```

<!-- test: let-escape.error.a-record-stored-through-a-witness-method-is-written -->
`ListSink.take` keeps its parameter in the sink's container, but the requirement `Sink.take` does not declare it `keep`, so the conformer is refused.

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

interface Sink
	function take(b Box)
end 'Sink'

type ListSink implements Sink
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'

	function take(b Box)
		items.push(b)
	end 'take'

	function zeroFirst()
		var w = try items.get(0) otherwise return
		w.n = 0
	end 'zeroFirst'
end 'ListSink'

function feed(s Sink, b Box)
	s.take(b)
end 'feed'

function main() returns ExitCode
	var sink = ListSink.create()
	let a = Box.create(2)
	feed(sink, b: a)
	sink.zeroFirst()
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3189: <fragment>:52:11: 'ListSink.take' keeps its parameter 'b', but 'Sink.take' does not declare it `keep`
```

<!-- test: let-escape.error.a-record-stored-in-a-sibling-record-is-written-by-the-siblings-method -->
`attach` keeps its parameter in the cursor's `var` field, and `a` is read afterwards, so the call is refused.

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
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:61:4: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'Cursor.attach', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.an-awaited-task-may-write-a-record-stored-while-it-ran -->
`register` keeps its parameter in a module container, and `a` is read afterwards, so the call is refused whatever the awaited task writes.

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

function clobber() returns Count
	var w = try registry.get(0) otherwise return 0
	w.n = 99
	return 1
end 'clobber'

function main() returns ExitCode
	let a = Box.create(2)
	let handle = async clobber()
	register(a)
	let r = await handle
	print("{a.n}{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:56:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'register', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-record-the-callee-creates-and-stores-is-written-through-its-holder -->
`add` pushes `made` and then returns it, so the push is not its last use and is refused; what `add` returns is its `let`, which is read-only, so `b` may bind it.

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
	touch(reg)
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:22:9: cannot store `let` 'made' into mutable place 'an element of the container push writes'; use clone()
```

<!-- test: let-escape.a-var-binding-of-the-record-the-callee-created-may-be-written-through-its-holder -->
The same program with `var` bindings compiles: the record is written through another route, so the bindings are writable, and neither `var` is reported as one that could be a `let`.

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
		var made = Box.create(n)
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
	var b = reg.add(7)
	touch(reg)
	print("{b.n}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.error.a-record-the-callee-creates-and-makes-current-is-written-through-the-cursor -->
`open` stores `made` in the cursor's `current` field and then returns it, so the store is refused; what `open` returns is its `let`, which is read-only, so `b` may bind it.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Cursor
	export var current as Box

	static function create(seed Box) returns Self
		return Self{current: seed}
	end 'create'

	function open(n Count) returns Box
		let made = Box.create(n)
		current = made
		return made
	end 'open'

	function touch()
		current.n = current.n + 1
	end 'touch'
end 'Cursor'

function main() returns ExitCode
	var c = Cursor.create(Box.create(0))
	let b = c.open(5)
	c.touch()
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:21:3: cannot store `let` 'made' into mutable place 'Cursor.current'; use clone()
```

<!-- test: let-escape.error.a-let-stored-in-a-local-map-is-written-through-the-map -->
A value handed to `upsert` is stored in a mutable place, and `a` is read afterwards, so the store is refused.

```maxon
typealias Count = int(0 to 1000)
typealias BoxMap = Map with (Count, Box)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var m = BoxMap.create()
	let a = Box.create(2)
	m.upsert(1, value: a)
	var w = try m.get(1) otherwise return 1
	w.n = 9
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:16:4: cannot store `let` 'a' into mutable place 'an element of the container upsert writes'; use clone()
```

<!-- test: let-escape.error.a-let-stored-in-an-array-inside-an-array-is-written-through-the-holder -->
`fill` keeps its parameter in an array it pushes into the holder's array of arrays, and `a` is read afterwards, so the call is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Rows = Array with Boxes

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function fill(rows Rows, b Box)
	var inner = Boxes.create()
	inner.push(b)
	rows.push(inner)
end 'fill'

function wipe(rows Rows)
	var r = try rows.get(0) otherwise return
	var w = try r.get(0) otherwise return
	w.n = 0
end 'wipe'

function main() returns ExitCode
	var rows = Rows.create()
	let a = Box.create(2)
	fill(rows, b: a)
	wipe(rows)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:29:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'fill', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-let-stored-in-an-array-inside-a-map-is-written-through-the-holder -->
`fill` keeps its parameter in an array it stores as a value of the holder's map, and `a` is read afterwards, so the call is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias RowMap = Map with (Count, Boxes)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Table
	export var rows as RowMap

	static function create() returns Self
		return Self{rows: RowMap.create()}
	end 'create'
end 'Table'

function fill(t Table, b Box)
	var inner = Boxes.create()
	inner.push(b)
	t.rows.upsert(7, value: inner)
end 'fill'

function wipe(t Table)
	var r = try t.rows.get(7) otherwise return
	var w = try r.get(0) otherwise return
	w.n = 0
end 'wipe'

function main() returns ExitCode
	var t = Table.create()
	let a = Box.create(2)
	fill(t, b: a)
	wipe(t)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:37:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'fill', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-let-stored-three-containers-down-is-written-through-the-holder -->
`place` keeps its parameter three containers below the holder, and `a` is read afterwards, so the call is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Grid = Array with Rows
typealias Rows = Array with Boxes

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Board
	export var grid as Grid

	static function create() returns Self
		return Self{grid: Grid.create()}
	end 'create'
end 'Board'

function place(board Board, b Box)
	var inner = Boxes.create()
	inner.push(b)
	var rows = Rows.create()
	rows.push(inner)
	board.grid.push(rows)
end 'place'

function wipe(board Board)
	var rows = try board.grid.get(0) otherwise return
	var inner = try rows.get(0) otherwise return
	var w = try inner.get(0) otherwise return
	w.n = 0
end 'wipe'

function main() returns ExitCode
	var board = Board.create()
	let a = Box.create(2)
	place(board, b: a)
	wipe(board)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:41:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'place', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-record-stored-in-a-function-typed-field-record-is-written-deep-by-a-callee -->
`stash` keeps its parameter in a `var` field of the holder, and `s` is read afterwards, so the call is refused; a function-typed field beside it changes nothing.

```maxon
typealias Count = int(0 to 1000)
typealias Hook = function() returns Count

type Point
	export var x as Count

	static function create(x Count) returns Self
		return Self{x: x}
	end 'create'

	function move()
		x = x + 1
	end 'move'
end 'Point'

type Mid
	export var leaf as Point

	static function create(leaf Point) returns Self
		return Self{leaf: leaf}
	end 'create'
end 'Mid'

type Outer
	export var hook as Hook
	export var mid as Mid

	static function create(hook Hook, mid Mid) returns Self
		return Self{hook: hook, mid: mid}
	end 'create'
end 'Outer'

type Holder
	export var slot as Outer

	static function create(slot Outer) returns Self
		return Self{slot: slot}
	end 'create'
end 'Holder'

function stash(p Holder, s Outer)
	p.slot = s
end 'stash'

function nudge(p Holder)
	p.slot.mid.leaf.move()
end 'nudge'

function main() returns ExitCode
	var p = Holder.create(Outer.create(function() gives 0, mid: Mid.create(Point.create(0))))
	let s = Outer.create(function() gives 1, mid: Mid.create(Point.create(1)))
	stash(p, s: s)
	nudge(p)
	print("{s.mid.leaf.x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:53:2: cannot pass 's', which is read-only (a `let` binding), to parameter 's' of 'stash', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-captured-let-stored-in-a-container-is-written-after-the-closure-outlives-the-function -->
`install` pushes `b` into the holder's container and then captures it in a closure, so the push is not its last use and is refused.

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
	var w = try reg.items.get(0) otherwise return 1
	w.n = 5
	let f = reg.show
	print("{f()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:25:12: cannot store `let` 'b' into mutable place 'an element of the container push writes'; use clone()
```

<!-- test: let-escape.error.a-record-stashed-two-fields-down-is-written-by-a-callee-handed-the-middle-record -->
`stash` keeps its parameter two fields below its first parameter, and `a` is read afterwards, so the call is refused, whichever callee writes later.

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

function stash(o Outer, b Box)
	o.inner.items.push(b)
end 'stash'

function wipeFirst(r Reg)
	var w = try r.items.get(0) otherwise return
	w.n = 0
end 'wipeFirst'

function main() returns ExitCode
	var o = Outer.create()
	let a = Box.create(2)
	stash(o, b: a)
	wipeFirst(o.inner)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:41:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stash', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.a-string-let-stored-in-a-field-is-a-value-the-append-does-not-reach -->
A `String` is a value type, so storing the `let` in a `var` field stores a value: the append extends the field's copy and the `let` still reads `a7`.

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
	h.s = s
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

<!-- test: let-escape.error.a-yielding-call-lets-a-spawned-task-write-a-record-stored-in-a-module-container -->
<!-- unsupported-targets: wasm32-wasi -->
On wasm32-wasi the expected diagnostic is followed by an extra E3104 ('Scheduler.yield' lowers to the runtime entry '__gt_resched', which has no wasm32-wasi implementation) that the pinned output does not list, so the lane is excluded.

`register` keeps its parameter in a module container, and `b` is read after the yield, so the call is refused.

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

function wipeFirst() returns Count
	var w = try registry.get(0) otherwise return 1
	w.n = 0
	return 0
end 'wipeFirst'

function main() returns ExitCode
	let task = async wipeFirst()
	let b = Box.create(2)
	register(b)
	Scheduler.yield()
	print("{b.n}\n")
	let r = await task
	return r as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:28:2: cannot pass 'b', which is read-only (a `let` binding), to parameter 'b' of 'register', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-module-lets-record-a-call-hands-back-cannot-be-stored-where-it-is-written -->
`pick` returns a module `let`, which is image data, so storing the call's result into a `var` field is refused and asks for `clone()`.

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
	h.box = pick()
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
```maxoncstderr
error E3078: <fragment>:27:4: cannot store a value read out of a module-level `let` into mutable place 'Holder.box'; use clone()
```

<!-- test: let-escape.error.a-callee-two-calls-down-writes-the-module-holder -->
The `let` record is pushed into a module variable's container and read afterwards, so the push is refused, however deep the later write is.

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
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'
end 'Holder'

var holder = Holder.create()

function g()
	var w = try holder.items.get(0) otherwise return
	w.n = 5
end 'g'

function f()
	g()
end 'f'

function main() returns ExitCode
	let b = Box.create(1)
	holder.items.push(b)
	f()
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:34:15: cannot store `let` 'b' into mutable place 'an element of the container push writes'; use clone()
```

<!-- test: let-escape.error.a-record-handed-through-a-function-typed-by-an-alias-of-its-instance -->
`keep` hands back the `let` it is given, so `k` is another name for `b`'s record, and pushing it is refused while `b` is read afterwards; the alias spelling changes nothing.

```maxon
typealias Count = int(0 to 1000)

type S0
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'S0'

type Box uses T
	export var item as T

	static function create(v T) returns Self
		return Self{item: v}
	end 'create'
end 'Box'

typealias N0 = Box with S0
typealias ViaAlias = Box with N0
typealias Holder = Array with ViaAlias

var holder = Holder.create()

function keep(b ViaAlias) returns ViaAlias
	return b
end 'keep'

function clobber()
	var w = try holder.get(0) otherwise return
	w.item.item.n = 5
end 'clobber'

function main() returns ExitCode
	let b = Box.create(Box.create(S0.create(1)))
	let k = keep(b)
	holder.push(k)
	clobber()
	print("{b.item.item.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:38:9: cannot store `let` 'k' into mutable place 'an element of the container push writes'; use clone()
```

<!-- test: let-escape.error.a-record-cast-to-an-alias-of-its-instance -->
`k` is `b` cast to an alias of its instance, another name for the same record, so pushing it is refused while `b` is read afterwards.

```maxon
typealias Count = int(0 to 1000)

type S0
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'S0'

type Box uses T
	export var item as T

	static function create(v T) returns Self
		return Self{item: v}
	end 'create'
end 'Box'

typealias N0 = Box with S0
typealias ViaAlias = Box with N0
typealias Holder = Array with ViaAlias

var holder = Holder.create()

function clobber()
	var w = try holder.get(0) otherwise return
	w.item.item.n = 5
end 'clobber'

function main() returns ExitCode
	let b = Box.create(Box.create(S0.create(1)))
	let k = b as ViaAlias
	holder.push(k)
	clobber()
	print("{b.item.item.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:34:9: cannot store `let` 'k' into mutable place 'an element of the container push writes'; use clone()
```

<!-- test: let-escape.error.a-write-to-the-set-a-let-record-holds-through-its-holder -->
`first` hands back an element of a container the `var` holder keeps, so the `let` of it is refused.

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

	function grow(id Count)
		var target = try self.items.get(0) otherwise return
		target.names.insert(id)
	end 'grow'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	holder.add(Layout.create())
	let layout = holder.first()
	holder.grow(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:44:6: cannot bind `let` 'layout' to a value reached through 'holder', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-write-to-the-set-a-let-record-holds-through-a-nested-holder -->
`first` hands back an element of a container the `var` holder keeps, so the `let` of it is refused.

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

	function grow(id Count)
		var target = try self.items.get(0) otherwise return
		target.names.insert(id)
	end 'grow'
end 'Holder'

type Outer
	var inner as Holder

	static function create() returns Outer
		return Outer{inner: Holder.create()}
	end 'create'

	function first() returns Layout
		return self.inner.first()
	end 'first'

	function add(layout Layout)
		self.inner.add(layout)
	end 'add'

	function grow(id Count)
		self.inner.grow(id)
	end 'grow'
end 'Outer'

function main() returns ExitCode
	var holder = Outer.create()
	holder.add(Layout.create())
	let layout = holder.first()
	holder.grow(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:64:6: cannot bind `let` 'layout' to a value reached through 'holder', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-write-to-the-set-of-its-type-a-let-record-holds-through-its-holder -->
`first` hands back an element of a container the `var` holder keeps, so the `let` of it is refused.

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

	function grow(id Count)
		var target = try self.items.get(0) otherwise return
		target.names.insert(id)
	end 'grow'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	holder.add(Layout.create())
	let layout = holder.first()
	holder.grow(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:40:6: cannot bind `let` 'layout' to a value reached through 'holder', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-record-stored-in-a-module-holder-is-written-through-the-module-holder-beside-a-set-of-its-type -->
The `let` record is pushed into a module variable's container and read afterwards, so the push is refused.

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

function grow(id Count)
	var target = try holder.items.get(0) otherwise return
	target.names.insert(id)
end 'grow'

function main() returns ExitCode
	let layout = Layout.create()
	holder.items.push(layout)
	grow(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:33:15: cannot store `let` 'layout' into mutable place 'an element of the container push writes'; use clone()
```

<!-- test: let-escape.error.a-record-a-factory-minted-into-its-holder-is-written-through-the-holder-after-a-getter-hands-it-back -->
The getter hands back the field the `var` holder keeps, so the `let` of it is refused.

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
	var holder = Holder.create()
	let box = holder.grab()
	holder.grow()
	print("{box.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:30:6: cannot bind `let` 'box' to a value reached through 'holder', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.an-array-a-factory-minted-into-its-holder-is-written-through-the-holder-after-a-getter-hands-it-back -->
The getter hands back the field the `var` holder keeps, so the `let` of it is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Array with Count

type Holder
	var names as Names

	static function create() returns Holder
		return Holder{names: Names.create()}
	end 'create'

	function grab() returns Names
		return self.names
	end 'grab'

	function grow()
		self.names.push(5)
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
```maxoncstderr
error E3188: <fragment>:23:6: cannot bind `let` 'names' to a value reached through 'holder', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-set-a-factory-minted-into-its-holder-is-written-through-the-holder-after-a-getter-hands-it-back -->
The getter hands back the field the `var` holder keeps, so the `let` of it is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Set with Count
typealias Seen = Set with Count

type Holder
	var names as Names
	var seen as Seen

	static function create() returns Holder
		return Holder{names: Names.create(), seen: Seen.create()}
	end 'create'

	function grabNames() returns Names
		return self.names
	end 'grabNames'

	function grow(id Count)
		self.names.insert(id)
	end 'grow'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	let names = holder.grabNames()
	holder.grow(7)
	print("{names.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:25:6: cannot bind `let` 'names' to a value reached through 'holder', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-field-record-shared-with-another-holders-field-is-written-through-that-holder -->
`share` keeps the set its `source` holds in another record's field, and `Holder.add` keeps its parameter in the holder's container; `layout` is read after both calls, so both are refused.

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

function share(source Layout, target Layout)
	target.names = source.names
end 'share'

function main() returns ExitCode
	let layout = Layout.create()
	var other = Layout.create()
	share(layout, target: other)
	var holder = Holder.create(other)
	holder.add(layout)
	holder.touch(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:38:2: cannot pass 'layout', which is read-only (a `let` binding), to parameter 'source' of 'share', which keeps it where something may write it; pass a clone()
error E3078: <fragment>:40:9: cannot pass 'layout', which is read-only (a `let` binding), to parameter 'layout' of 'Holder.add', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-let-handed-to-a-factory-that-stores-it-is-written-through-the-holder -->
`Holder.create` keeps its argument in a `var` field of the record it returns, and `layout` is read afterwards, so the call is refused.

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
		return Holder{first: first}
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
```maxoncstderr
error E3078: <fragment>:27:22: cannot pass 'layout', which is read-only (a `let` binding), to parameter 'first' of 'Holder.create', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-let-stored-two-factories-down-is-written-through-the-outer-holder -->
`Holder.create` hands `layout` to `Inner.create`, which keeps it in a `var` field, so `Holder.create` keeps it too, and `layout` is read afterwards: the call is refused.

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

	static function create(layout Layout) returns Inner
		return Inner{layout: layout}
	end 'create'
end 'Inner'

type Holder
	export var inner as Inner

	static function create(layout Layout) returns Holder
		return Holder{inner: Inner.create(layout)}
	end 'create'

	function touch(id Count)
		self.inner.layout.names.insert(id)
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
```maxoncstderr
error E3078: <fragment>:35:22: cannot pass 'layout', which is read-only (a `let` binding), to parameter 'layout' of 'Holder.create', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-let-and-another-record-sharing-a-set-are-both-held-by-a-factory-and-written-through-the-holder -->
`share` keeps the set its `source` holds in another record's field, and `Holder.create` keeps both its arguments in `var` fields; `layout` is read after both calls, so both are refused.

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
	var second as Layout

	static function create(first Layout, second Layout) returns Holder
		return Holder{first: first, second: second}
	end 'create'

	function touch(id Count)
		self.second.names.insert(id)
	end 'touch'
end 'Holder'

function share(source Layout, target Layout)
	target.names = source.names
end 'share'

function main() returns ExitCode
	let layout = Layout.create()
	var other = Layout.create()
	share(layout, target: other)
	var holder = Holder.create(layout, second: other)
	holder.touch(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:33:2: cannot pass 'layout', which is read-only (a `let` binding), to parameter 'source' of 'share', which keeps it where something may write it; pass a clone()
error E3078: <fragment>:34:22: cannot pass 'layout', which is read-only (a `let` binding), to parameter 'first' of 'Holder.create', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-record-stored-before-the-write-into-a-holder-an-earlier-call-escaped-is-written -->
`attach` keeps its `args` in an edge it pushes into the block, and `carried` is read afterwards, so the call is refused.

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
	attach(block, args: carried)
	world.scribble()
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
```maxoncstderr
error E3078: <fragment>:69:2: cannot pass 'carried', which is read-only (a `let` binding), to parameter 'args' of 'attach', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-let-bound-to-a-record-read-out-of-a-holder-is-written-by-another-name -->
`holder` is a `let`, so what `grab` hands back is read-only and `box` may bind it; `holder.grow()` writes through `holder`, and that is refused.

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
	let box = holder.grab()
	holder.grow()
	print("{box.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:31:9: cannot pass 'holder', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: let-escape.error.one-call-writing-the-records-of-two-live-lets-refuses-both -->
`register` keeps its parameter in a module container, and `a` and `b` are both read afterwards, so both calls are refused, each naming its own binding.

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

function clobberAll()
	for i in 0 upto registry.count() 'each'
		var w = try registry.get(i) otherwise return
		w.n = 99
	end 'each'
end 'clobberAll'

function main() returns ExitCode
	let a = Box.create(1)
	let b = Box.create(2)
	register(a)
	register(b)
	clobberAll()
	print("{a.n} {b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:29:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'register', which keeps it where something may write it; pass a clone()
error E3078: <fragment>:30:2: cannot pass 'b', which is read-only (a `let` binding), to parameter 'b' of 'register', which keeps it where something may write it; pass a clone()
```

<!-- test: let-escape.error.a-let-lent-a-record-minted-before-the-loop-is-written-on-a-later-trip-through-storage-an-earlier-trip-filled -->
`same` hands back the record `outer` names, and `outer` is a `var`, so `r` would name a record something may still write, and the `let` is refused.

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

function same(b Box) returns Box
	return b
end 'same'

function clobber()
	var w = try registry.get(0) otherwise return
	w.n = 99
end 'clobber'

function main() returns ExitCode
	var outer = Box.create(2)
	var i = 0 as Count

	while i < 2 'trips'
		let r = same(outer)
		clobber()
		register(r)
		print("{r.n}\n")
		i = i + 1
	end 'trips'

	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:33:7: cannot bind `let` 'r' to a value reached through 'outer', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-callee-handed-the-let-and-its-holder-writes-the-let-through-the-holder -->
`stash` keeps `a` in the container and `a` is read afterwards, and `touchBoth` writes the `let` it is handed through a method called on it. Both calls are refused.

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

function touchBoth(reg Reg, b Box)
	var w = try reg.items.get(0) otherwise return
	w.n = 5
	b.bump()
end 'touchBoth'

function main() returns ExitCode
	var reg = Reg.create()
	let a = Box.create(2)
	stash(reg, b: a)
	touchBoth(reg, b: a)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:38:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stash', which keeps it where something may write it; pass a clone()
error E3159: <fragment>:39:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: let-escape.error.a-write-through-a-slot-the-let-record-also-holds-is-refused -->
`Sigs.create` keeps the map `world.cache` holds, so its result is not fresh and `made` is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Map with (String, Count)

type Sigs
	export var names as Names

	static function create(names Names) returns Self
		return Self{names: names}
	end 'create'
end 'Sigs'

type Memo
	export var sigs as Sigs

	static function create() returns Self
		return Self{sigs: Sigs.create(Names.create())}
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
	let made = Sigs.create(world.cache.names)
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
```maxoncstderr
error E3188: <fragment>:39:6: cannot bind `let` 'made' to a value reached through 'world', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-write-to-a-record-another-holder-shares-with-the-let-is-refused -->
`sigsOf` builds a record around layouts something may still write and stores it into `world`, so the record it hands back is one something may still write, and the `let` of it is refused.

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

	static function create() returns Self
		return Self{layout: Layout.create()}
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
		var decl = Decl.create()
		decl.layout = layout
		made.decls.push(decl)
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
```maxoncstderr
error E3188: <fragment>:85:6: cannot bind `let` 'sigs' to a value reached through 'world', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-write-to-a-record-a-callee-built-into-the-let-from-its-argument-is-refused -->
`sigsOf` builds a record around layouts something may still write and stores it into `world`, so the record it hands back is one something may still write, and the `let` of it is refused.

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
		made.decls.push(Decl.create(layout))
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
```maxoncstderr
error E3188: <fragment>:83:6: cannot bind `let` 'sigs' to a value reached through 'world', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-write-through-a-path-longer-than-the-recorded-hops-reaches-a-record-the-let-holds -->
`sigsOf` builds a record around layouts something may still write and stores it into `world`, so the record it hands back is one something may still write, and the `let` of it is refused.

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

type L1
	export var next as Artifact

	static function create() returns Self
		return Self{next: Artifact.create()}
	end 'create'
end 'L1'

type L2
	export var next as L1

	static function create() returns Self
		return Self{next: L1.create()}
	end 'create'
end 'L2'

type L3
	export var next as L2

	static function create() returns Self
		return Self{next: L2.create()}
	end 'create'
end 'L3'

type L4
	export var next as L3

	static function create() returns Self
		return Self{next: L3.create()}
	end 'create'
end 'L4'

type L5
	export var next as L4

	static function create() returns Self
		return Self{next: L4.create()}
	end 'create'
end 'L5'

type L6
	export var next as L5

	static function create() returns Self
		return Self{next: L5.create()}
	end 'create'
end 'L6'

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
	export var deep as L6

	static function create() returns Self
		return Self{memo: Memo.create(), deep: L6.create()}
	end 'create'
end 'World'

function sigsOf(world World) returns Sigs
	var made = Sigs.create()

	for layout in world.deep.next.next.next.next.next.next.layouts 'each'
		made.decls.push(Decl.create(layout))
	end 'each'

	world.memo.sigs = made
	return made
end 'sigsOf'

function measure(world World)
	var layout = try world.deep.next.next.next.next.next.next.layouts.get(0) otherwise return
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
```maxoncstderr
error E3188: <fragment>:131:6: cannot bind `let` 'sigs' to a value reached through 'world', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-write-through-a-global-reaches-a-record-the-let-holds-though-the-let-is-stored-in-a-local -->
`sigsOf` builds a record around layouts something may still write and stores it into `memo`, so the record it hands back is one something may still write, and the `let` of it is refused.

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

var layouts = Layouts.create()

function sigsOf(memo Memo) returns Sigs
	var made = Sigs.create()

	for layout in layouts 'each'
		made.decls.push(Decl.create(layout))
	end 'each'

	memo.sigs = made
	return made
end 'sigsOf'

function measure()
	var layout = try layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	layouts.push(Layout.create())
	var memo = Memo.create()
	let sigs = sigsOf(memo)
	measure()
	print("{sigs.decls.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:67:6: cannot bind `let` 'sigs' to a value reached through 'memo', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-field-assigned-a-global-record-in-the-let-is-written-through-the-global-is-refused -->
`sigsOf` builds a record around layouts something may still write and stores it into `memo`, so the record it hands back is one something may still write, and the `let` of it is refused.

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

var layouts = Layouts.create()

function sigsOf(memo Memo) returns Sigs
	var made = Sigs.create()

	for layout in layouts 'each'
		var decl = Decl.create(Layout.create())
		decl.layout = layout
		made.decls.push(decl)
	end 'each'

	memo.sigs = made
	return made
end 'sigsOf'

function measure()
	var layout = try layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	layouts.push(Layout.create())
	var memo = Memo.create()
	let sigs = sigsOf(memo)
	measure()
	print("{sigs.decls.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:69:6: cannot bind `let` 'sigs' to a value reached through 'memo', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-let-held-by-a-local-holder-that-a-local-world-also-holds-is-written-through-the-world-after-both-are-stored -->
`made` is a `let`, so pushing into its declarations writes through it, and storing it into the world while it is read afterwards is refused too.

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
	let made = Sigs.create()

	for layout in world.artifact.layouts 'each'
		made.decls.push(Decl.create(layout))
	end 'each'

	world.memo.sigs = made
	measure(world)
	print("{made.decls.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:75:14: cannot write through 'made.decls': 'made' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
error E3078: <fragment>:78:13: cannot store `let` 'made' into mutable place 'Memo.sigs'; use clone()
```

<!-- test: let-escape.error.an-interface-typed-let-is-written-through-a-record-it-holds -->
`Holder.create` keeps the layout `world` holds, so its result is not fresh and `made` is refused.

```maxon
typealias Count = int(0 to 1000)

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

interface Sized
	function size() returns Count
end 'Sized'

type Holder implements Sized
	export var layout as Layout

	static function create(layout Layout) returns Self
		return Self{layout: layout}
	end 'create'

	function size() returns Count
		return self.layout.detail.size
	end 'size'
end 'Holder'

type Memo
	export var sized as Sized

	static function create() returns Self
		return Self{sized: Holder.create(Layout.create())}
	end 'create'
end 'Memo'

type World
	export var memo as Memo
	export var layout as Layout

	static function create() returns Self
		return Self{memo: Memo.create(), layout: Layout.create()}
	end 'create'
end 'World'

function sizedOf(world World) returns Sized
	let made = Holder.create(world.layout)
	world.memo.sized = made
	return made
end 'sizedOf'

function measure(world World)
	world.layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	var world = World.create()
	let sized = sizedOf(world)
	measure(world)
	print("{sized.size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:54:6: cannot bind `let` 'made' to a value reached through 'world', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-named-let-holding-a-record-read-from-a-world-is-written-through-the-world -->
`Holder.create` keeps the layout `world` holds, so its result is not fresh and `made` is refused.

```maxon
typealias Count = int(0 to 1000)

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

interface Sized
	function size() returns Count
end 'Sized'

type Holder implements Sized
	export var layout as Layout

	static function create(layout Layout) returns Self
		return Self{layout: layout}
	end 'create'

	function size() returns Count
		return self.layout.detail.size
	end 'size'
end 'Holder'

type Memo
	export var sized as Holder

	static function create() returns Self
		return Self{sized: Holder.create(Layout.create())}
	end 'create'
end 'Memo'

type World
	export var memo as Memo
	export var layout as Layout

	static function create() returns Self
		return Self{memo: Memo.create(), layout: Layout.create()}
	end 'create'
end 'World'

function sizedOf(world World) returns Holder
	let made = Holder.create(world.layout)
	world.memo.sized = made
	return made
end 'sizedOf'

function measure(world World)
	world.layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	var world = World.create()
	let sized = sizedOf(world)
	measure(world)
	print("{sized.size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:54:6: cannot bind `let` 'made' to a value reached through 'world', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.an-interface-typed-let-is-written-through-a-deep-path-to-a-record-it-holds -->
`sigsOf` builds a record around layouts something may still write and stores it into `world`, so the record it hands back is one something may still write, and the `let` of it is refused.

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

interface Counted
	function count() returns Count
end 'Counted'

type Sigs implements Counted
	export var decls as Decls

	function count() returns Count
		return self.decls.count() as Count
	end 'count'

	static function create() returns Self
		return Self{decls: Decls.create()}
	end 'create'
end 'Sigs'

type Memo
	export var sigs as Counted

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

function sigsOf(world World) returns Counted
	var made = Sigs.create()

	for layout in world.artifact.layouts 'each'
		made.decls.push(Decl.create(layout))
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
	print("{sigs.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:91:6: cannot bind `let` 'sigs' to a value reached through 'world', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-escape.error.a-record-handed-to-a-generic-method-is-written-through-the-world-in-the-same-call -->
`world` is a `var`, so its layout is a record something may still write, and the `let` of it is refused.

```maxon
typealias Count = int(0 to 1000)

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

type Artifact
	export var layout as Layout

	static function create() returns Self
		return Self{layout: Layout.create()}
	end 'create'
end 'Artifact'

type World
	export var artifact as Artifact

	static function create() returns Self
		return Self{artifact: Artifact.create()}
	end 'create'
end 'World'

type Keeper uses T
	export typealias Items = Array with T
	export var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function keep(item T, world World)
		let kept = item
		self.items.push(kept)
		world.artifact.layout.detail.size = 8
	end 'keep'
end 'Keeper'

typealias LayoutKeeper = Keeper with Layout

function main() returns ExitCode
	var world = World.create()
	var keeper = LayoutKeeper.create()
	let layout = world.artifact.layout
	keeper.keep(layout, world: world)
	print("{layout.detail.size}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:56:6: cannot bind `let` 'layout' to a value reached through 'world', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```
