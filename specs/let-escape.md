---
feature: let-escape
keywords: [let, escape, store, keep, write, alias, container, holder, module let, immutable]
category: semantics
---

# A `let` Names A Record It Never Rebinds

## Documentation

A `let` means the name is never reassigned. The record it names is an ordinary record of the current region: a
callee may keep it in a container or a `var` field, and any holder, or the `let` itself, may write it. Handing a
`let` to a parameter the callee writes or keeps, storing it into a `var` field or a container element, and
binding a `let` to a value something else may write are all accepted, and every name reads the same record, so a
write through one is read through the others. `clone()` gives the writer a record of its own.

A module `let` is `immutable` image data: nothing may write it, and nothing may store it where something may
write it. Storing its record into a plain `Array`, `Map`, field or array literal is `E3196` (declare the holder
`immutable Box`), handing it to a parameter the callee writes or keeps there is `E3207`, and a result that
returns it is `E3209` until the result is declared `immutable`. Store a `clone()`.

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

function register(b Box, registry mutable Boxes)
	registry.push(b)
end 'register'

function clobber(registry mutable Boxes)
	let w = try registry.get(0) otherwise return
	w.n = 99
end 'clobber'

function main() returns ExitCode
	let registry = Boxes.create(0)
	let a = Box.create(2)
	register(a, registry: registry)
	clobber(registry)
	return 0
end 'main'
```
```stdout
```
```exitcode
0
```

<!-- test: let-escape.a-let-kept-in-a-global-container-of-another-type-and-read-again-is-accepted -->
`register` keeps its parameter in a container and `a` is read after the call: the call is accepted, whatever type the later write reaches.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Tag
	export var t as Count

	static function create(t Count) returns Self
		return Self{t: t}
	end 'create'
end 'Tag'

typealias Tags = Array with Tag

function register(b Box, registry mutable Boxes)
	registry.push(b)
end 'register'

function retag(tags mutable Tags)
	tags.push(Tag.create(1))
	let w = try tags.get(0) otherwise return
	w.t = 9
end 'retag'

function main() returns ExitCode
	let registry = Boxes.create(0)
	let tags = Tags.create(0)
	let a = Box.create(2)
	register(a, registry: registry)
	retag(tags)
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

<!-- test: let-escape.a-let-kept-in-one-container-of-a-holder-and-read-again-is-accepted -->
`stashLeft` keeps its parameter in one container of the holder and `a` is read after the call: the call is accepted, whichever container is written later.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export var left as Boxes
	export var right as Boxes

	static function create() returns Self
		return Self{left: Boxes.create(0), right: Boxes.create(0)}
	end 'create'
end 'Pair'

function stashLeft(p Pair, b Box)
	p.left.push(b)
end 'stashLeft'

function resetRight(p Pair)
	p.right.clear()
end 'resetRight'

function main() returns ExitCode
	let p = Pair.create()
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

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

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
	let c = Cursor.create(Box.create(1))
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
		return Self{items: Boxes.create(0)}
	end 'create'
end 'Reg'

function build() returns Reg
	let reg = Reg.create()
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
		return Self{items: Boxes.create(0)}
	end 'create'
end 'Reg'

function zeroBox(w Box)
	w.n = 0
end 'zeroBox'

function build() returns Reg
	let reg = Reg.create()
	let a = Box.create(2)
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

<!-- test: let-escape.a-let-kept-in-a-var-field-of-a-record-with-sixty-five-fields-is-accepted -->
`WideHold.create` keeps `box` in a `var` field and `a` is read after the call: the call is accepted however wide the holder is.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

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
	let h = WideHold.create(w, box: a)
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

<!-- test: let-escape.a-let-kept-by-a-callee-while-a-task-runs-and-read-again-is-accepted -->
`register` keeps its parameter in a container while a task runs and `a` is read afterwards: the call is accepted, whatever the task does.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function register(b Box, registry mutable Boxes)
	registry.push(b)
end 'register'

function quiet() returns Count
	sleep(1)
	return 1
end 'quiet'

function main() returns ExitCode
	let registry = Boxes.create(0)
	let a = Box.create(2)
	let handle = async quiet()
	register(a, registry: registry)
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

<!-- test: let-escape.a-let-the-callee-pushes-and-returns-is-accepted-at-the-push-though-the-caller-reads-it-before-the-write -->
`add` pushes `made` and then returns it: the push is accepted, and the `let` that binds the result names the record the container holds.

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
		return Self{items: Boxes.create(0)}
	end 'create'

	function add(n Count) returns Box
		let made = Box.create(n)
		items.push(made)
		return made
	end 'add'
end 'Reg'

function touch(reg Reg)
	let w = try reg.items.get(0) otherwise return
	w.n = 1
end 'touch'

function main() returns ExitCode
	let reg = Reg.create()
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

<!-- test: let-escape.error.a-static-lets-record-pushed-into-a-global-container-is-refused -->
A `static let` is `immutable` image data, and an element of a plain `Array with Box` is a place something may write, so pushing the `let`'s record into one is refused; declare the element `immutable Box`, or push a `clone()`.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static let shared = Self{n: 7}

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function register(registry mutable Boxes)
	registry.push(Box.shared)
end 'register'

function clobber(registry mutable Boxes)
	var w = try registry.get(0) otherwise return
	w.n = 99
end 'clobber'

function main() returns ExitCode
	var registry = Boxes.create(0)
	register(registry)
	clobber(registry)
	print("{Box.shared.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:16: cannot store an immutable 'Box' into 'an element of the container push writes', which holds a mutable 'Box'; declare the element `immutable Box`
```

<!-- test: let-escape.error.a-static-lets-record-handed-to-a-callee-that-keeps-it-is-refused -->
`stash` keeps its parameter in a container and in a field, so handing it a `static let` is refused; pass a `clone()`.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static let shared = Self{n: 7}

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

function stash(b Box, registry mutable Boxes, holder mutable Holder)
	registry.push(b)
	holder.box = b
end 'stash'

function clobber(registry mutable Boxes, holder mutable Holder)
	var w = try registry.get(0) otherwise return
	w.n = 99
	holder.box.n = 55
end 'clobber'

function main() returns ExitCode
	var registry = Boxes.create(0)
	var holder = Holder.create(Box.create(0))
	stash(Box.shared, registry: registry, holder: holder)
	clobber(registry, holder: holder)
	print("{Box.shared.n} {holder.box.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:37:8: 'stash' writes 'b' (line 24: `registry.push(b)`); 'Box' is immutable
```

<!-- test: let-escape.error.a-static-lets-record-handed-to-a-factory-that-keeps-it-in-a-field-is-refused -->
`Holder.create` keeps its parameter in a `var` field, so handing it a `static let` is refused; pass a `clone()`.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static let shared = Self{n: 7}

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


function main() returns ExitCode
	var h = Holder.create(Box.shared)
	h.box.n = 99
	print("{Box.shared.n} {h.box.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:25:2: cannot write 'n' through 'h.box', which is read-only
```

<!-- test: let-escape.error.a-static-lets-record-pushed-into-a-local-array-is-refused -->
A local array's element is a mutable place, so pushing a `static let`'s record into it is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static let shared = Self{n: 7}

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'


function main() returns ExitCode
	var local = Boxes.create(0)
	local.push(Box.shared)
	var w = try local.get(0) otherwise return 1
	w.n = 99
	print("{Box.shared.n} {w.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:18:13: cannot store an immutable 'Box' into 'an element of the container push writes', which holds a mutable 'Box'; declare the element `immutable Box`
```

<!-- test: let-escape.error.a-static-lets-record-in-a-literal-a-map-and-a-global-field-is-refused -->
An array literal's element and a field of a `var` holder are each a mutable place, so storing a `static let`'s record into either is refused.

```maxon
typealias Count = int(0 to 1000)
typealias BoxMap = Map with (Count, Box)

type Box
	export var n as Count

	static let shared = Self{n: 7}

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

function remember(registry mutable Holder)
	registry.box = Box.shared
end 'remember'

function bump(registry mutable Holder)
	registry.box.n = 99
end 'bump'

function main() returns ExitCode
	var registry = Holder.create(Box.create(0))
	var lit = [Box.shared]
	var w = try lit.get(0) otherwise return 1
	w.n = 11

	var m = BoxMap.create()
	m.upsert(1, value: Box.shared)
	var mv = try m.get(1) otherwise return 2
	mv.n = 22

	remember(registry)
	bump(registry)
	print("{Box.shared.n} {w.n} {mv.n} {registry.box.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:24:17: cannot store an immutable 'Box' into 'box', which holds a mutable 'Box'; declare the field `immutable Box`
error E3196: <fragment>:33:13: cannot store an immutable 'Box' into 'an element of the array literal', which holds a mutable 'Box'; declare the element `immutable Box`
```

<!-- test: let-escape.error.a-static-lets-record-in-a-literal-and-a-map-value-is-refused -->
An array literal's element is a mutable place, so storing a `static let`'s record into it is refused.

```maxon
typealias Count = int(0 to 1000)
typealias BoxMap = Map with (Count, Box)

type Box
	export var n as Count

	static let shared = Self{n: 7}

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'


function main() returns ExitCode
	var lit = [Box.shared]
	var w = try lit.get(0) otherwise return 1
	w.n = 11

	var m = BoxMap.create()
	m.upsert(1, value: Box.shared)
	var mv = try m.get(1) otherwise return 2
	mv.n = 22

	print("{Box.shared.n} {w.n} {mv.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:17:13: cannot store an immutable 'Box' into 'an element of the array literal', which holds a mutable 'Box'; declare the element `immutable Box`
```

<!-- test: let-escape.error.a-static-lets-record-in-a-map-value-is-refused -->
A value handed to `upsert` is stored in a mutable place, so storing a `static let`'s record into a map value is refused.

```maxon
typealias Count = int(0 to 1000)
typealias BoxMap = Map with (Count, Box)

type Box
	export var n as Count

	static let shared = Self{n: 7}

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'


function main() returns ExitCode
	var m = BoxMap.create()
	m.upsert(1, value: Box.shared)
	var mv = try m.get(1) otherwise return 1
	mv.n = 22
	print("{Box.shared.n} {mv.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:18:21: cannot store an immutable 'Box' into 'Map.upsert.value', which holds a mutable 'Box'; declare the element `immutable Box`
```

<!-- test: let-escape.a-captured-let-pushed-into-a-holder-is-accepted-though-nothing-writes-its-type -->
`install` pushes `b` into the holder's container and then captures it in a closure, and `main` binds the element it reads back to a `let`: all accepted.

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
		return Self{items: Boxes.create(0), show: function() gives 0}
	end 'create'
end 'Reg'

function install(reg Reg)
	let b = Box.create(1)
	reg.items.push(b)
	reg.show = function() gives b.n
end 'install'

function main() returns ExitCode
	let reg = Reg.create()
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
	let h = H.create("")
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

<!-- test: let-escape.a-let-kept-in-a-module-container-across-a-yield-and-read-again-is-accepted -->
`register` keeps its parameter in a container and `b` is read afterwards: the call is accepted, whatever type the spawned task writes.

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

function register(b Box, registry mutable Boxes)
	registry.push(b)
end 'register'

function wipeFirst(others mutable OtherBoxes) returns Count
	let w = try others.get(0) otherwise return 1
	w.m = 0
	return 0
end 'wipeFirst'

function main() returns ExitCode
	let others = OtherBoxes.create(0)
	let registry = Boxes.create(0)
	others.push(Other.create(1))
	let task = async wipeFirst(others)
	let b = Box.create(2)
	register(b, registry: registry)
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

<!-- test: let-escape.a-static-lets-record-a-call-hands-back-is-stored-as-a-clone -->
`pick` returns a `static let`, which is `immutable`, so its result is declared `immutable`. Storing `pick().clone()` stores an independent record, so the write through the field leaves the `let` as it was.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static let shared = Self{n: 7}

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


function pick() returns immutable Box
	return Box.shared
end 'pick'

function adopt(h Holder)
	h.box = pick().clone()
end 'adopt'

function bump(h Holder)
	h.box.n = 99
end 'bump'

function main() returns ExitCode
	let h = Holder.create(Box.create(0))
	adopt(h)
	bump(h)
	print("{Box.shared.n} {h.box.n}\n")
	return 0
end 'main'
```
```stdout
7 99
```
```exitcode
0
```

<!-- test: let-escape.a-let-bound-to-a-record-a-var-holder-hands-back-is-accepted-though-another-container-is-written -->
`first` hands back an element of the holder's container, and the `let` bound to it is accepted, whichever container is written later.

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
		return Holder{items: Layouts.create(0), seen: Seen.create()}
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
	let holder = Holder.create()
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

<!-- test: let-escape.a-let-bound-to-a-record-a-var-holder-hands-back-is-accepted-though-another-set-of-its-type-is-written -->
The same with a second `Set` of the very same type beside the records: the `let` of what `first` hands back from a `var` holder is accepted.

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
		return Holder{items: Layouts.create(0), seen: Seen.create()}
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
	let holder = Holder.create()
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

<!-- test: let-escape.a-let-pushed-into-a-module-holder-and-read-again-is-accepted -->
The `let` record is pushed into a container of the holder and read afterwards: the push is accepted, whichever field of the holder is written later.

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
		return Holder{items: Layouts.create(0), seen: Seen.create()}
	end 'create'
end 'Holder'

function note(holder mutable Holder, id Count)
	holder.seen.insert(id)
end 'note'

function main() returns ExitCode
	let holder = Holder.create()
	let layout = Layout.create()
	holder.items.push(layout)
	note(holder, id: 7)
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
	let holder = Holder.create()
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
	let holder = Holder.create()
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

<!-- test: let-escape.a-let-bound-to-a-field-a-getter-hands-back-from-a-var-holder-is-accepted -->
`grab` hands back the holder's own field, and the `let` bound to it is accepted, whichever field `grow` writes.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Array with Count

type Holder
	var names as Names
	var other as Names

	static function create() returns Holder
		return Holder{names: Names.create(0), other: Names.create(0)}
	end 'create'

	function grab() returns Names
		return self.names
	end 'grab'

	function grow()
		self.other.push(5)
	end 'grow'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
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

<!-- test: let-escape.a-let-kept-by-a-holders-method-and-read-again-is-accepted -->
`Holder.add` keeps its parameter in the holder's container and `layout` is read afterwards: the call is accepted, whatever `touch` writes.

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
		return Holder{items: Layouts.create(0), extra: extra}
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
	let other = Layout.create()
	let holder = Holder.create(other)
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
	let holder = Holder.create(layout)
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

<!-- test: let-escape.a-let-kept-two-factories-down-and-read-again-is-accepted -->
`Holder.create` hands `layout` to `Inner.create`, which keeps it in a `var` field, and `layout` is read afterwards: the call is accepted, whichever record `touch` writes.

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
	let spare = Layout.create()
	let holder = Holder.create(layout, spare: spare)
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

<!-- test: let-escape.a-let-kept-after-the-write-into-a-holder-an-earlier-call-escaped-is-accepted -->
`attach` keeps its `args` in an edge it pushes into the block, and `carried` is read afterwards: the call is accepted, before or after any write.

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
		return Self{edges: Edges.create(0)}
	end 'create'
end 'Block'

type Book
	export var blocks as Blocks

	static function create() returns Self
		return Self{blocks: Blocks.create(0)}
	end 'create'
end 'Book'

type Routed
	export var args as Args

	static function create() returns Self
		return Self{args: Args.create(0)}
	end 'create'
end 'Routed'

type World
	export var book as Book
	export var routed as Routed

	static function create() returns Self
		return Self{book: Book.create(), routed: Routed.create()}
	end 'create'

	function open() returns Block
		let block = Block.create()
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
	let carried = Args.create(0)
	let block = world.open()
	world.scribble()
	attach(block, args: carried)
	return carried.count()
end 'run'

function main() returns ExitCode
	let world = World.create()
	let other = world.open()
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

<!-- test: let-escape.a-let-minted-in-the-loop-kept-by-a-callee-and-read-again-is-accepted -->
`register` keeps its parameter in a container and `r` is read after the call: the call is accepted on every trip.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function register(b Box, registry mutable Boxes)
	registry.push(b)
end 'register'

function clobber(registry mutable Boxes)
	let w = try registry.get(0) otherwise return
	w.n = 99
end 'clobber'

function main() returns ExitCode
	let registry = Boxes.create(0)
	var i = 0 as Count

	while i < 2 'trips'
		let r = Box.create(2)
		clobber(registry)
		register(r, registry: registry)
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

<!-- test: let-escape.a-callee-writing-a-let-through-its-holders-own-method-is-accepted -->
`stash` keeps `a` in the container and `a` is read afterwards, and `bumpIt` writes the `let` it is handed through a method called on it: both calls are accepted.

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
		return Self{items: Boxes.create(0)}
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
	let reg = Reg.create()
	let a = Box.create(2)
	stash(reg, b: a)
	bumpIt(reg, b: a)
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: let-escape.a-let-stored-into-a-var-field-and-returned-is-accepted-though-only-another-map-is-written -->
`sigsOf` stores `made` into `world.memo` and then returns it: the store is accepted, whatever `fill` writes.

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
	let world = World.create()
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

<!-- test: let-escape.a-let-stored-into-a-var-field-and-returned-is-accepted-though-only-a-long-chain-is-written -->
The same store of `made` before it is returned, with the written set five records down a chain: the store is accepted.

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
	let world = World.create()
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

<!-- test: let-escape.a-let-bound-to-a-record-the-callee-also-stored-in-the-world-is-accepted-though-it-holds-copies -->
`sigsOf` stores the `var` it returns into `world.memo`, and the `let` that binds the record it hands back is accepted, whatever the record holds.

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
		let made = Layouts.create(0)
		made.push(Layout.create())
		return Self{layouts: made}
	end 'create'
end 'Artifact'

type Sigs
	export var decls as Decls

	static function create() returns Self
		return Self{decls: Decls.create(0)}
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
	let made = Sigs.create()

	for layout in world.artifact.layouts 'each'
		let copy = Layout.create()
		copy.detail.size = layout.detail.size
		made.decls.push(Decl.create(copy))
	end 'each'

	world.memo.sigs = made
	return made
end 'sigsOf'

function measure(world World)
	let layout = try world.artifact.layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	let world = World.create()
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

<!-- test: let-escape.a-call-writing-two-lets-passed-directly-writes-both -->
`bump` writes the records of both of its parameters, and the call passes two `let` records: both are written.

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
```stdout
1 2
```
```exitcode
0
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
		let made = Layouts.create(0)
		made.push(Layout.create())
		return Self{layouts: made}
	end 'create'
end 'Artifact'

type Sigs
	export var decls as Decls

	static function create() returns Self
		return Self{decls: Decls.create(0)}
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
	let layout = try world.artifact.layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	let world = World.create()
	let made = Sigs.create()

	for layout in world.artifact.layouts 'each'
		let copy = Layout.create()
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

<!-- test: let-escape.a-record-stored-in-a-global-container-is-written-through-the-global -->
`register` keeps its parameter in a container and `a` is read after the call: the call is accepted, and `a` reads the write made through the container.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function register(b Box, registry mutable Boxes)
	registry.push(b)
end 'register'

function clobber(registry mutable Boxes)
	let w = try registry.get(0) otherwise return
	w.n = 99
end 'clobber'

function main() returns ExitCode
	let registry = Boxes.create(0)
	let a = Box.create(2)
	register(a, registry: registry)
	clobber(registry)
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
99
```
```exitcode
0
```

<!-- test: let-escape.a-record-stored-into-a-parameters-container-is-written-through-the-parameter -->
`stash` keeps its parameter in a container its first parameter holds, and `a` is read afterwards: the call is accepted, and `a` reads the write.

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
		return Self{items: Boxes.create(0)}
	end 'create'
end 'Reg'

function stash(reg Reg, b Box)
	reg.items.push(b)
end 'stash'

function zeroFirst(reg Reg)
	let w = try reg.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'

function main() returns ExitCode
	let reg = Reg.create()
	let a = Box.create(2)
	stash(reg, b: a)
	zeroFirst(reg)
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-record-stored-two-fields-down-is-written-through-the-holder -->
`stash` keeps its parameter two fields below its first parameter, and `a` is read afterwards: the call is accepted, and `a` reads the write.

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
		return Self{items: Boxes.create(0)}
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

function zeroFirst(o Outer)
	let w = try o.inner.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'

function main() returns ExitCode
	let o = Outer.create()
	let a = Box.create(2)
	stash(o, b: a)
	zeroFirst(o)
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-record-stored-three-fields-down-through-a-loaded-record-is-written -->
`stash` keeps its parameter in a container reached through a record it loads from its first parameter, and `a` is read afterwards: the call is accepted, and `a` reads the write.

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
		return Self{items: Boxes.create(0)}
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

function stash(t Top, b Box)
	let o = t.outer
	o.inner.items.push(b)
end 'stash'

function zeroFirst(t Top)
	let w = try t.outer.inner.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'

function main() returns ExitCode
	let t = Top.create()
	let a = Box.create(2)
	stash(t, b: a)
	zeroFirst(t)
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-record-stored-by-a-callee-into-a-field-container-is-written -->
`stash` hands its parameter to `put`, which keeps it in a container, so `stash` keeps it too, and `a` is read afterwards: the call is accepted, and `a` reads the write.

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
		return Self{items: Boxes.create(0)}
	end 'create'
end 'Reg'

function put(items Boxes, b Box)
	items.push(b)
end 'put'

function stash(reg Reg, b Box)
	put(reg.items, b: b)
end 'stash'

function zeroFirst(reg Reg)
	let w = try reg.items.get(0) otherwise return
	w.n = 0
end 'zeroFirst'

function main() returns ExitCode
	let reg = Reg.create()
	let a = Box.create(2)
	stash(reg, b: a)
	zeroFirst(reg)
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.error.a-record-stored-by-a-callback-is-written -->
The function value writes its parameter `r` by pushing the record into it, but `Taker` does not declare that parameter `mutable`, so converting the function value to `Taker` is refused.

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
		return Self{items: Boxes.create(0)}
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
error E3210: <fragment>:60:18: function value for 'Taker' writes its parameter 'r', but 'Taker' does not declare it `mutable`
```

<!-- test: let-escape.error.a-record-stored-through-a-witness-method-is-written -->
`ListSink.take` writes its receiver by pushing the record into the sink's container, but the requirement `Sink.take` does not declare `mutable self`, so the conformer is refused.

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
		return Self{items: Boxes.create(0)}
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
		return Self{items: Boxes.create(0)}
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
error E3210: <fragment>:52:11: 'ListSink.take' writes its receiver, but 'Sink.take' does not declare `mutable self`
```

<!-- test: let-escape.a-record-stored-in-a-sibling-record-is-written-by-the-siblings-method -->
`attach` keeps its parameter in the cursor's `var` field and `a` is read afterwards: the call is accepted, and the cursor's `wipe` writes the record `a` names.

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
	let c = Cursor.create(Box.create(1))
	let a = Box.create(2)
	c.attach(a)
	c.wipe()
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-record-the-callee-creates-and-stores-is-written-through-its-holder -->
`add` pushes `made` and then returns it: the `let` that binds the result names the record the container holds, and `touch` writes it.

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
		return Self{items: Boxes.create(0)}
	end 'create'

	function add(n Count) returns Box
		let made = Box.create(n)
		items.push(made)
		return made
	end 'add'
end 'Reg'

function touch(reg Reg)
	let w = try reg.items.get(0) otherwise return
	w.n = 1
end 'touch'

function main() returns ExitCode
	let reg = Reg.create()
	let b = reg.add(7)
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

<!-- test: let-escape.a-var-the-callee-pushed-and-returned-is-written-through-its-holder -->
`add` pushes its `var` `made` and returns it, and `touch` writes the pushed element: the record the caller binds with a `let` reads the write.

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
		return Self{items: Boxes.create(0)}
	end 'create'

	function add(n Count) returns Box
		let made = Box.create(n)
		items.push(made)
		return made
	end 'add'
end 'Reg'

function touch(reg Reg)
	let w = try reg.items.get(0) otherwise return
	w.n = 1
end 'touch'

function main() returns ExitCode
	let reg = Reg.create()
	let b = reg.add(7)
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

<!-- test: let-escape.a-record-the-callee-creates-and-makes-current-is-written-through-the-cursor -->
`open` stores `made` in the cursor's `current` field and then returns it: the `let` that binds the result names the record the cursor holds, and `touch` writes it.

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
	let c = Cursor.create(Box.create(0))
	let b = c.open(5)
	c.touch()
	print("{b.n}\n")
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: let-escape.a-let-stored-in-a-local-map-is-written-through-the-map -->
A value handed to `upsert` is stored in a mutable place, and `a` is read afterwards: the store is accepted, and `a` reads the write made through the map.

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
	let m = BoxMap.create()
	let a = Box.create(2)
	m.upsert(1, value: a)
	let w = try m.get(1) otherwise return 1
	w.n = 9
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
9
```
```exitcode
0
```

<!-- test: let-escape.a-let-stored-in-an-array-inside-an-array-is-written-through-the-holder -->
`fill` keeps its parameter in an array it pushes into the holder's array of arrays, and `a` is read afterwards: the call is accepted, and `a` reads the write.

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
	let inner = Boxes.create(0)
	inner.push(b)
	rows.push(inner)
end 'fill'

function wipe(rows Rows)
	let r = try rows.get(0) otherwise return
	let w = try r.get(0) otherwise return
	w.n = 0
end 'wipe'

function main() returns ExitCode
	let rows = Rows.create(0)
	let a = Box.create(2)
	fill(rows, b: a)
	wipe(rows)
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-let-stored-in-an-array-inside-a-map-is-written-through-the-holder -->
`fill` keeps its parameter in an array it stores as a value of the holder's map, and `a` is read afterwards: the call is accepted, and `a` reads the write.

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
	let inner = Boxes.create(0)
	inner.push(b)
	t.rows.upsert(7, value: inner)
end 'fill'

function wipe(t Table)
	let r = try t.rows.get(7) otherwise return
	let w = try r.get(0) otherwise return
	w.n = 0
end 'wipe'

function main() returns ExitCode
	let t = Table.create()
	let a = Box.create(2)
	fill(t, b: a)
	wipe(t)
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-let-stored-three-containers-down-is-written-through-the-holder -->
`place` keeps its parameter three containers below the holder, and `a` is read afterwards: the call is accepted, and `a` reads the write.

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
		return Self{grid: Grid.create(0)}
	end 'create'
end 'Board'

function place(board Board, b Box)
	let inner = Boxes.create(0)
	inner.push(b)
	let rows = Rows.create(0)
	rows.push(inner)
	board.grid.push(rows)
end 'place'

function wipe(board Board)
	let rows = try board.grid.get(0) otherwise return
	let inner = try rows.get(0) otherwise return
	let w = try inner.get(0) otherwise return
	w.n = 0
end 'wipe'

function main() returns ExitCode
	let board = Board.create()
	let a = Box.create(2)
	place(board, b: a)
	wipe(board)
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-escape.a-record-stored-in-a-function-typed-field-record-is-written-deep-by-a-callee -->
`stash` keeps its parameter in a `var` field of the holder, and `s` is read afterwards: the call is accepted, a function-typed field beside it changes nothing, and `s` reads the write.

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
	let p = Holder.create(Outer.create(function() gives 0, mid: Mid.create(Point.create(0))))
	let s = Outer.create(function() gives 1, mid: Mid.create(Point.create(1)))
	stash(p, s: s)
	nudge(p)
	print("{s.mid.leaf.x}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: let-escape.a-captured-let-stored-in-a-container-is-written-after-the-closure-outlives-the-function -->
`install` pushes `b` into the holder's container and then captures it in a closure: both are accepted, and the closure reads the write `main` makes through the container.

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
		return Self{items: Boxes.create(0), show: function() gives 0}
	end 'create'
end 'Reg'

function install(reg Reg)
	let b = Box.create(1)
	reg.items.push(b)
	reg.show = function() gives b.n
end 'install'

function main() returns ExitCode
	let reg = Reg.create()
	install(reg)
	let w = try reg.items.get(0) otherwise return 1
	w.n = 5
	let f = reg.show
	print("{f()}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: let-escape.a-record-stashed-two-fields-down-is-written-by-a-callee-handed-the-middle-record -->
`stash` keeps its parameter two fields below its first parameter, and `a` is read afterwards: the call is accepted, whichever callee writes later.

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
		return Self{items: Boxes.create(0)}
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
	let w = try r.items.get(0) otherwise return
	w.n = 0
end 'wipeFirst'

function main() returns ExitCode
	let o = Outer.create()
	let a = Box.create(2)
	stash(o, b: a)
	wipeFirst(o.inner)
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
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
	let h = H.create("")
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

<!-- test: let-escape.error.a-static-lets-record-a-call-hands-back-cannot-be-stored-where-it-is-written -->
`pick` returns a `static let`, which is `immutable`, so its result is declared `immutable`; storing the call's result into a `var` field is refused and asks for a `clone()`.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static let shared = Self{n: 7}

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


function pick() returns immutable Box
	return Box.shared
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
	print("{Box.shared.n} {h.box.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:28:10: cannot store an immutable 'Box' into 'box', which holds a mutable 'Box'; declare the field `immutable Box`
```

<!-- test: let-escape.a-callee-two-calls-down-writes-the-module-holder -->
The `let` record is pushed into a container and read afterwards, and the write comes two calls down: the push is accepted.

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
		return Self{items: Boxes.create(0)}
	end 'create'
end 'Holder'

function g(holder mutable Holder)
	let w = try holder.items.get(0) otherwise return
	w.n = 5
end 'g'

function f(holder mutable Holder)
	g(holder)
end 'f'

function main() returns ExitCode
	let holder = Holder.create()
	let b = Box.create(1)
	holder.items.push(b)
	f(holder)
	print("{b.n}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: let-escape.a-record-handed-through-a-function-typed-by-an-alias-of-its-instance -->
`keep` hands back the `let` it is given, so `k` is another name for `b`'s record; pushing it is accepted, and the alias spelling changes nothing.

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

function keep(b ViaAlias) returns ViaAlias
	return b
end 'keep'

function clobber(holder mutable Holder)
	let w = try holder.get(0) otherwise return
	w.item.item.n = 5
end 'clobber'

function main() returns ExitCode
	let holder = Holder.create(0)
	let b = Box.create(Box.create(S0.create(1)))
	let k = keep(b)
	holder.push(k)
	clobber(holder)
	print("{b.item.item.n}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: let-escape.a-record-cast-to-an-alias-of-its-instance -->
`k` is `b` cast to an alias of its instance, another name for the same record; pushing it is accepted.

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

function clobber(holder mutable Holder)
	let w = try holder.get(0) otherwise return
	w.item.item.n = 5
end 'clobber'

function main() returns ExitCode
	let holder = Holder.create(0)
	let b = Box.create(Box.create(S0.create(1)))
	let k = b as ViaAlias
	holder.push(k)
	clobber(holder)
	print("{b.item.item.n}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: let-escape.a-write-to-the-set-a-let-record-holds-through-its-holder -->
`first` hands back an element of a container the `var` holder keeps, the `let` of it is accepted, and `grow` writes the set that record holds.

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
		return Holder{items: Layouts.create(0), seen: Seen.create()}
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
		let target = try self.items.get(0) otherwise return
		target.names.insert(id)
	end 'grow'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	holder.add(Layout.create())
	let layout = holder.first()
	holder.grow(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-write-to-the-set-a-let-record-holds-through-a-nested-holder -->
The same through a holder that reaches the inner holder: the `let` of what `first` hands back is accepted, and `grow` writes the set that record holds.

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
		return Holder{items: Layouts.create(0), seen: Seen.create()}
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
		let target = try self.items.get(0) otherwise return
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
	let holder = Outer.create()
	holder.add(Layout.create())
	let layout = holder.first()
	holder.grow(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-write-to-the-set-of-its-type-a-let-record-holds-through-its-holder -->
`first` hands back an element of a container the `var` holder keeps, with a second `Set` of the same type beside it: the `let` of the element is accepted, and `grow` writes the set that record holds.

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
		return Holder{items: Layouts.create(0), seen: Seen.create()}
	end 'create'

	function add(layout Layout)
		self.items.push(layout)
	end 'add'

	function first() returns Layout
		return try self.items.get(0) otherwise panic("empty")
	end 'first'

	function grow(id Count)
		let target = try self.items.get(0) otherwise return
		target.names.insert(id)
	end 'grow'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	holder.add(Layout.create())
	let layout = holder.first()
	holder.grow(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-record-stored-in-a-module-holder-is-written-through-the-module-holder-beside-a-set-of-its-type -->
The `let` record is pushed into a container of the holder and read afterwards: the push is accepted.

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
		return Holder{items: Layouts.create(0), seen: Seen.create()}
	end 'create'
end 'Holder'

function grow(holder mutable Holder, id Count)
	let target = try holder.items.get(0) otherwise return
	target.names.insert(id)
end 'grow'

function main() returns ExitCode
	let holder = Holder.create()
	let layout = Layout.create()
	holder.items.push(layout)
	grow(holder, id: 7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-record-a-factory-minted-into-its-holder-is-written-through-the-holder-after-a-getter-hands-it-back -->
The getter hands back the field the `var` holder keeps, the `let` of it is accepted, and `grow` writes it.

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
```stdout
5
```
```exitcode
0
```

<!-- test: let-escape.an-array-a-factory-minted-into-its-holder-is-written-through-the-holder-after-a-getter-hands-it-back -->
The getter hands back the array the `var` holder keeps, the `let` of it is accepted, and `grow` writes it.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Array with Count

type Holder
	var names as Names

	static function create() returns Holder
		return Holder{names: Names.create(0)}
	end 'create'

	function grab() returns Names
		return self.names
	end 'grab'

	function grow()
		self.names.push(5)
	end 'grow'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	let names = holder.grab()
	holder.grow()
	print("{names.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-set-a-factory-minted-into-its-holder-is-written-through-the-holder-after-a-getter-hands-it-back -->
The getter hands back the set the `var` holder keeps, the `let` of it is accepted, and `grow` writes it.

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
	let holder = Holder.create()
	let names = holder.grabNames()
	holder.grow(7)
	print("{names.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-field-record-shared-with-another-holders-field-is-written-through-that-holder -->
`share` keeps the set its `source` holds in another record's field, and `Holder.add` keeps its parameter in the holder's container; `layout` is read after both calls, and both calls are accepted.

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
		return Holder{items: Layouts.create(0), extra: extra}
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
	let other = Layout.create()
	share(layout, target: other)
	let holder = Holder.create(other)
	holder.add(layout)
	holder.touch(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-let-handed-to-a-factory-that-stores-it-is-written-through-the-holder -->
`Holder.create` keeps its argument in a `var` field of the record it returns, and `layout` is read afterwards: the call is accepted, and `touch` writes the record `layout` names.

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
	let holder = Holder.create(layout)
	holder.touch(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-let-stored-two-factories-down-is-written-through-the-outer-holder -->
`Holder.create` hands `layout` to `Inner.create`, which keeps it in a `var` field, and `layout` is read afterwards: the call is accepted.

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
	let holder = Holder.create(layout)
	holder.touch(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-let-and-another-record-sharing-a-set-are-both-held-by-a-factory-and-written-through-the-holder -->
`share` keeps the set its `source` holds in another record's field, and `Holder.create` keeps both its arguments in `var` fields; `layout` is read after both calls, and both calls are accepted.

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
	let other = Layout.create()
	share(layout, target: other)
	let holder = Holder.create(layout, second: other)
	holder.touch(7)
	print("{layout.names.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-record-stored-before-the-write-into-a-holder-an-earlier-call-escaped-is-written -->
`attach` keeps its `args` in an edge it pushes into the block, and `carried` is read afterwards: the call is accepted.

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
		return Self{edges: Edges.create(0)}
	end 'create'
end 'Block'

type Book
	export var blocks as Blocks

	static function create() returns Self
		return Self{blocks: Blocks.create(0)}
	end 'create'
end 'Book'

type Routed
	export var args as Args

	static function create() returns Self
		return Self{args: Args.create(0)}
	end 'create'
end 'Routed'

type World
	export var book as Book
	export var routed as Routed

	static function create() returns Self
		return Self{book: Book.create(), routed: Routed.create()}
	end 'create'

	function open() returns Block
		let block = Block.create()
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
	let carried = Args.create(0)
	let block = world.open()
	attach(block, args: carried)
	world.scribble()
	return carried.count()
end 'run'

function main() returns ExitCode
	let world = World.create()
	let other = world.open()
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

<!-- test: let-escape.a-let-bound-to-a-record-read-out-of-a-holder-is-written-by-another-name -->
`holder` is a `let`, and `holder.grow()` writes through it: `box`, bound to the record `holder.grab()` hands back, reads the write.

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
```stdout
5
```
```exitcode
0
```

<!-- test: let-escape.one-call-writing-the-records-of-two-live-lets-writes-both -->
`register` keeps its parameter in a container, and `a` and `b` are both read afterwards: both calls are accepted, and `clobberAll` writes both records.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function register(b Box, registry mutable Boxes)
	registry.push(b)
end 'register'

function clobberAll(registry mutable Boxes)
	for i in 0 upto registry.count() 'each'
		let w = try registry.get(i) otherwise return
		w.n = 99
	end 'each'
end 'clobberAll'

function main() returns ExitCode
	let registry = Boxes.create(0)
	let a = Box.create(1)
	let b = Box.create(2)
	register(a, registry: registry)
	register(b, registry: registry)
	clobberAll(registry)
	print("{a.n} {b.n}\n")
	return 0
end 'main'
```
```stdout
99 99
```
```exitcode
0
```

<!-- test: let-escape.a-let-lent-a-record-minted-before-the-loop-is-written-on-a-later-trip-through-storage-an-earlier-trip-filled -->
`same` hands back the record `outer` names, `r` binds it with a `let`, and a later trip writes the record an earlier trip stored: the program is accepted.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function register(b Box, registry mutable Boxes)
	registry.push(b)
end 'register'

function same(b Box) returns Box
	return b
end 'same'

function clobber(registry mutable Boxes)
	let w = try registry.get(0) otherwise return
	w.n = 99
end 'clobber'

function main() returns ExitCode
	let registry = Boxes.create(0)
	let outer = Box.create(2)
	var i = 0 as Count

	while i < 2 'trips'
		let r = same(outer)
		clobber(registry)
		register(r, registry: registry)
		print("{r.n}\n")
		i = i + 1
	end 'trips'

	return 0
end 'main'
```
```stdout
2
99
```
```exitcode
0
```

<!-- test: let-escape.a-callee-handed-the-let-and-its-holder-writes-the-let-through-the-holder -->
`stash` keeps `a` in the container and `a` is read afterwards, and `touchBoth` writes the `let` it is handed through the container and through a method called on it: both calls are accepted.

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
		return Self{items: Boxes.create(0)}
	end 'create'
end 'Reg'

function stash(reg Reg, b Box)
	reg.items.push(b)
end 'stash'

function touchBoth(reg Reg, b Box)
	let w = try reg.items.get(0) otherwise return
	w.n = 5
	b.bump()
end 'touchBoth'

function main() returns ExitCode
	let reg = Reg.create()
	let a = Box.create(2)
	stash(reg, b: a)
	touchBoth(reg, b: a)
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: let-escape.a-write-through-a-slot-the-let-record-also-holds-is-accepted -->
`Sigs.create` keeps the map `world.cache` holds, `made` binds the result with a `let`, and `fill` writes the map through the cache: the program is accepted.

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
	let world = World.create()
	let sigs = sigsOf(world)
	fill(world)
	print("{sigs.names.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-write-to-a-record-another-holder-shares-with-the-let-is-accepted -->
`sigsOf` builds a record around layouts something else may write and stores it into `world`: the `let` that binds the record it hands back is accepted.

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
		let made = Layouts.create(0)
		made.push(Layout.create())
		return Self{layouts: made}
	end 'create'
end 'Artifact'

type Sigs
	export var decls as Decls

	static function create() returns Self
		return Self{decls: Decls.create(0)}
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
	let made = Sigs.create()

	for layout in world.artifact.layouts 'each'
		let decl = Decl.create()
		decl.layout = layout
		made.decls.push(decl)
	end 'each'

	world.memo.sigs = made
	return made
end 'sigsOf'

function measure(world World)
	let layout = try world.artifact.layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	let world = World.create()
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

<!-- test: let-escape.a-write-to-a-record-a-callee-built-into-the-let-from-its-argument-is-accepted -->
`sigsOf` builds a record around layouts something else may write and stores it into `world`: the `let` that binds the record it hands back is accepted.

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
		let made = Layouts.create(0)
		made.push(Layout.create())
		return Self{layouts: made}
	end 'create'
end 'Artifact'

type Sigs
	export var decls as Decls

	static function create() returns Self
		return Self{decls: Decls.create(0)}
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
	let made = Sigs.create()

	for layout in world.artifact.layouts 'each'
		made.decls.push(Decl.create(layout))
	end 'each'

	world.memo.sigs = made
	return made
end 'sigsOf'

function measure(world World)
	let layout = try world.artifact.layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	let world = World.create()
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

<!-- test: let-escape.a-write-through-a-path-longer-than-the-recorded-hops-reaches-a-record-the-let-holds -->
`sigsOf` builds a record around layouts reached through a long path and stores it into `world`: the `let` that binds the record it hands back is accepted.

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
		let made = Layouts.create(0)
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
		return Self{decls: Decls.create(0)}
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
	let made = Sigs.create()

	for layout in world.deep.next.next.next.next.next.next.layouts 'each'
		made.decls.push(Decl.create(layout))
	end 'each'

	world.memo.sigs = made
	return made
end 'sigsOf'

function measure(world World)
	let layout = try world.deep.next.next.next.next.next.next.layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	let world = World.create()
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

<!-- test: let-escape.a-write-through-a-global-reaches-a-record-the-let-holds-though-the-let-is-stored-in-a-local -->
`sigsOf` builds a record around layouts something else may write and stores it into `memo`: the `let` that binds the record it hands back is accepted.

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
		return Self{decls: Decls.create(0)}
	end 'create'
end 'Sigs'

type Memo
	export var sigs as Sigs

	static function create() returns Self
		return Self{sigs: Sigs.create()}
	end 'create'
end 'Memo'

function sigsOf(memo Memo, layouts Layouts) returns Sigs
	let made = Sigs.create()

	for layout in layouts 'each'
		made.decls.push(Decl.create(layout))
	end 'each'

	memo.sigs = made
	return made
end 'sigsOf'

function measure(layouts mutable Layouts)
	let layout = try layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	let layouts = Layouts.create(0)
	layouts.push(Layout.create())
	let memo = Memo.create()
	let sigs = sigsOf(memo, layouts: layouts)
	measure(layouts)
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

<!-- test: let-escape.a-field-assigned-a-global-record-in-the-let-is-written-through-the-global-is-accepted -->
`sigsOf` assigns a layout into a field of a record it builds and stores that record into `memo`: the `let` that binds the record it hands back is accepted.

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
		return Self{decls: Decls.create(0)}
	end 'create'
end 'Sigs'

type Memo
	export var sigs as Sigs

	static function create() returns Self
		return Self{sigs: Sigs.create()}
	end 'create'
end 'Memo'

function sigsOf(memo Memo, layouts Layouts) returns Sigs
	let made = Sigs.create()

	for layout in layouts 'each'
		let decl = Decl.create(Layout.create())
		decl.layout = layout
		made.decls.push(decl)
	end 'each'

	memo.sigs = made
	return made
end 'sigsOf'

function measure(layouts mutable Layouts)
	let layout = try layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	let layouts = Layouts.create(0)
	layouts.push(Layout.create())
	let memo = Memo.create()
	let sigs = sigsOf(memo, layouts: layouts)
	measure(layouts)
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

<!-- test: let-escape.a-let-held-by-a-local-holder-that-a-local-world-also-holds-is-written-through-the-world-after-both-are-stored -->
`made` is a `let`: pushing into its declarations writes through it, and storing it into the world while it is read afterwards is accepted too.

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
		let made = Layouts.create(0)
		made.push(Layout.create())
		return Self{layouts: made}
	end 'create'
end 'Artifact'

type Sigs
	export var decls as Decls

	static function create() returns Self
		return Self{decls: Decls.create(0)}
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
	let layout = try world.artifact.layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	let world = World.create()
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
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.an-interface-typed-let-is-written-through-a-record-it-holds -->
`Holder.create` keeps the layout `world` holds, and `made` binds the result with a `let` at an interface type: the program is accepted, and `sized` reads the write.

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
	let world = World.create()
	let sized = sizedOf(world)
	measure(world)
	print("{sized.size()}\n")
	return 0
end 'main'
```
```stdout
8
```
```exitcode
0
```

<!-- test: let-escape.a-named-let-holding-a-record-read-from-a-world-is-written-through-the-world -->
`Holder.create` keeps the layout `world` holds, and `made` binds the result with a `let`: the program is accepted, and `sized` reads the write.

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
	let world = World.create()
	let sized = sizedOf(world)
	measure(world)
	print("{sized.size()}\n")
	return 0
end 'main'
```
```stdout
8
```
```exitcode
0
```

<!-- test: let-escape.an-interface-typed-let-is-written-through-a-deep-path-to-a-record-it-holds -->
`sigsOf` builds a record around layouts something else may write and stores it into `world` at an interface type: the `let` that binds it is accepted.

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
		let made = Layouts.create(0)
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
		return Self{decls: Decls.create(0)}
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
	let made = Sigs.create()

	for layout in world.artifact.layouts 'each'
		made.decls.push(Decl.create(layout))
	end 'each'

	world.memo.sigs = made
	return made
end 'sigsOf'

function measure(world World)
	let layout = try world.artifact.layouts.get(0) otherwise return
	layout.detail.size = 8
end 'measure'

function main() returns ExitCode
	let world = World.create()
	let sigs = sigsOf(world)
	measure(world)
	print("{sigs.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-escape.a-record-handed-to-a-generic-method-is-written-through-the-world-in-the-same-call -->
`keep` stores its argument in the keeper's container and writes the world in the same call: the program is accepted, and `layout` reads the write.

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
		return Self{items: Items.create(0)}
	end 'create'

	function keep(item T, world World)
		let kept = item
		self.items.push(kept)
		world.artifact.layout.detail.size = 8
	end 'keep'
end 'Keeper'

typealias LayoutKeeper = Keeper with Layout

function main() returns ExitCode
	let world = World.create()
	let keeper = LayoutKeeper.create()
	let layout = world.artifact.layout
	keeper.keep(layout, world: world)
	print("{layout.detail.size}\n")
	return 0
end 'main'
```
```stdout
8
```
```exitcode
0
```

<!-- test: let-escape.a-let-bound-to-an-element-moved-out-of-a-field-of-a-generic-type-is-accepted-for-an-integer -->
`pop` moves the element out of `self.items`, so the `let` holds a value nothing else holds and the later `push` cannot reach it. The generic body is accepted and returns the element.

```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to 1000)

type Stack uses T
	typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function push(v T)
		self.items.push(v)
	end 'push'

	function pop() returns T
		let v = try self.items.pop() otherwise panic("empty")
		return v
	end 'pop'

	function popThenPush(extra T) returns T
		let v = try self.items.pop() otherwise panic("empty")
		self.items.push(extra)
		return v
	end 'popThenPush'
end 'Stack'

typealias CountStack = Stack with Count

function main() returns ExitCode
	let s = CountStack.create()
	s.push(7)
	s.push(9)
	let a = s.popThenPush(3)
	let b = s.pop()
	print("{a} {b}\n")
	return 0
end 'main'
```
```stdout
9 3
```
```exitcode
0
```

<!-- test: let-escape.a-let-bound-to-an-element-moved-out-of-a-field-of-a-generic-type-is-accepted-for-a-string -->
The same generic body instantiated with a `String`: the moved-out string is owned by the `let`, handed to the caller by `return`, and released exactly once, so the run exits 0 rather than 101.

```maxon
typealias ExitCode = int(0 to 125)

type Stack uses T
	typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function push(v T)
		self.items.push(v)
	end 'push'

	function pop() returns T
		let v = try self.items.pop() otherwise panic("empty")
		return v
	end 'pop'

	function popThenPush(extra T) returns T
		let v = try self.items.pop() otherwise panic("empty")
		self.items.push(extra)
		return v
	end 'popThenPush'
end 'Stack'

typealias StringStack = Stack with String

function main() returns ExitCode
	let s = StringStack.create()
	s.push("first string long enough to force a heap allocation")
	s.push("second string long enough to force a heap allocation")
	let a = s.popThenPush("third string long enough to force a heap allocation")
	let b = s.pop()
	print("{a}\n{b}\n")
	return 0
end 'main'
```
```stdout
second string long enough to force a heap allocation
third string long enough to force a heap allocation
```
```exitcode
0
```

<!-- test: let-escape.a-let-bound-to-an-element-moved-out-of-a-field-of-a-generic-type-is-accepted-for-a-record -->
The same generic body instantiated with a record type, whose element is a heap record rather than a scalar or a string.

```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Stack uses T
	typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function push(v T)
		self.items.push(v)
	end 'push'

	function popThenPush(extra T) returns T
		let v = try self.items.pop() otherwise panic("empty")
		self.items.push(extra)
		return v
	end 'popThenPush'
end 'Stack'

typealias BoxStack = Stack with Box

function main() returns ExitCode
	let s = BoxStack.create()
	s.push(Box.create(7))
	let a = s.popThenPush(Box.create(9))
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: let-escape.a-let-bound-to-an-element-moved-out-of-a-field-of-a-plain-type-is-accepted -->
The control with no type parameter: `pop` and `remove` on a record field, bound to a `let`, leave nothing in the field that the `let` could still reach, so the body is accepted.

```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias Boxes = Array with Box

type Stack
	var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create(0)}
	end 'create'

	function push(v Box)
		self.items.push(v)
	end 'push'

	function popThenPush(extra Box) returns Box
		let v = try self.items.pop() otherwise panic("empty")
		self.items.push(extra)
		return v
	end 'popThenPush'

	function removeFirstOne() returns Box
		let v = try self.items.remove(0) otherwise panic("empty")
		return v
	end 'removeFirstOne'
end 'Stack'

function main() returns ExitCode
	let s = Stack.create()
	s.push(Box.create(7))
	s.push(Box.create(8))
	let a = s.popThenPush(Box.create(9))
	let b = s.removeFirstOne()
	print("{a.n} {b.n}\n")
	return 0
end 'main'
```
```stdout
8 7
```
```exitcode
0
```

<!-- test: let-escape.a-let-bound-to-a-borrowed-element-of-a-field-of-a-generic-type-is-accepted -->
`last` borrows the element and leaves it in `self.items`, and `push` writes that storage while the `let` is read: the `let` is accepted and names the borrowed element.

```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to 1000)

type Stack uses T
	typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function push(v T)
		self.items.push(v)
	end 'push'

	function peekThenPush(extra T) returns T
		let v = try self.items.last() otherwise panic("empty")
		self.items.push(extra)
		return v
	end 'peekThenPush'
end 'Stack'

typealias CountStack = Stack with Count

function main() returns ExitCode
	let s = CountStack.create()
	s.push(7)
	let a = s.peekThenPush(9)
	print("{a}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: let-escape.a-let-bound-to-a-borrowed-element-of-a-field-of-a-plain-type-is-accepted -->
The control with no type parameter: a record borrowed by `last` and read after a `push` is accepted.

```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias Boxes = Array with Box

type Stack
	var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create(0)}
	end 'create'

	function push(v Box)
		self.items.push(v)
	end 'push'

	function peekThenPush(extra Box) returns Box
		let v = try self.items.last() otherwise panic("empty")
		self.items.push(extra)
		return v
	end 'peekThenPush'
end 'Stack'

function main() returns ExitCode
	let s = Stack.create()
	s.push(Box.create(7))
	let a = s.peekThenPush(Box.create(9))
	print("{a.n}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```
