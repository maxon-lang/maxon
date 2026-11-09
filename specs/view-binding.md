---
feature: view-binding
keywords: [view, binding, let, alias, await, E2013, E3102, E3207]
category: semantics
---

# `view`: Another Spelling of `let`

## Documentation

`view x = e` is `let x = e`. The name is never reassigned (`E2013`), and data written through another name, including
by another coroutine across an `await`, is seen through it. Writing through a `view`, storing it, handing it to a
parameter, returning it and capturing it in a closure are as legal as they are for a `let`.

A `view` of a record local binds it as `let` does, moving it (`E3102` where the local is returned afterwards). Sending a `view`
to a service is `E3207` where the message takes `immutable` data and `E3200` where it takes `isolated` data. `view` binds only a
local name: a field, a module-level binding, an extension member or a `@heap` binding spelled `view` is `E2077`.

`view` is a contextual word: it begins a binding only where a name follows it, so it stays usable as an ordinary
variable or parameter name.

## Tests

<!-- test: view-binding.a-view-sees-a-write-through-the-var-it-views -->
`v` names the record `h.box` holds, so the write through `h` is read back through `v`.

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

function main() returns ExitCode
	var h = Holder.create(Box.create(1))
	view v = h.box
	h.box.n = 5
	print("{v.n}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: view-binding.view-is-an-ordinary-name-elsewhere -->
`view` followed by `=` or used as a parameter name is an identifier, not a binding.

```maxon
typealias Count = int(0 to 1000)

function next(view Count) returns Count
	return view + 1
end 'next'

function main() returns ExitCode
	var view = 3 as Count
	view = view + 1
	print("{next(view)}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: view-binding.a-view-live-across-an-await-sees-the-coroutines-write -->
The coroutine `bump` writes the viewed record while `main` waits, and the read after the `await` sees it.

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

function bump(box Box) returns Count
	sleep(1)
	box.n = 5
	return 1
end 'bump'

function main() returns ExitCode
	var holder = Holder.create(Box.create(1))
	view v = holder.box
	let task = async bump(holder.box)
	let r = await task
	print("{v.n} {r}\n")
	return 0
end 'main'
```
```stdout
5 1
```
```exitcode
0
```

<!-- test: view-binding.a-write-through-a-view-is-legal -->
A `view` is a `let`, so it may be written through; the write lands in the record `h.box` names.

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

function main() returns ExitCode
	var h = Holder.create(Box.create(1))
	h.box.n = 3
	view v = h.box
	v.n = 2
	print("{h.box.n}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: view-binding.a-view-stored-into-a-field-or-pushed-is-legal -->
A `view` may be stored into a `var` field and pushed onto an array.

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

function main() returns ExitCode
	var h = Holder.create(Box.create(1))
	var other = Holder.create(Box.create(2))
	var boxes = Boxes.create(0)
	h.box.n = 3
	view v = h.box
	other.box = v
	boxes.push(v)
	print("{other.box.n} {boxes.count()}\n")
	return 0
end 'main'
```
```stdout
3 1
```
```exitcode
0
```

<!-- test: view-binding.a-view-passed-to-a-parameter-that-is-kept-is-legal -->
`stash` keeps its parameter in an array, and a `view` may be handed to it.

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

function stash(b Box, kept mutable Boxes)
	kept.push(b)
end 'stash'

function main() returns ExitCode
	var kept = Boxes.create(0)
	var h = Holder.create(Box.create(1))
	h.box.n = 3
	view v = h.box
	stash(v, kept: kept)
	print("{kept.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: view-binding.a-returned-view-is-legal -->
A `view` may be returned.

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

function first(h Holder) returns Box
	view v = h.box
	return v
end 'first'

function main() returns ExitCode
	var h = Holder.create(Box.create(1))
	h.box.n = 3
	print("{first(h).n}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: view-binding.a-view-returned-through-a-call-that-hands-it-back-is-legal -->
`pick` returns its argument, and the result may be returned.

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

function pick(b Box) returns Box
	return b
end 'pick'

function first(h Holder) returns Box
	view v = h.box
	return pick(v)
end 'first'

function main() returns ExitCode
	var h = Holder.create(Box.create(1))
	h.box.n = 3
	print("{first(h).n}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: view-binding.a-view-handed-to-a-function-that-writes-its-parameter-is-legal -->
`bump` writes its parameter, and a `view` may be handed to it.

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

function bump(b Box)
	b.n = 9
end 'bump'

function main() returns ExitCode
	var h = Holder.create(Box.create(1))
	h.box.n = 3
	view v = h.box
	bump(v)
	print("{h.box.n}\n")
	return 0
end 'main'
```
```stdout
9
```
```exitcode
0
```

<!-- test: view-binding.a-view-stored-into-a-let-field-is-legal -->
A `view` may be stored into a `let` field.

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

type Pin
	let box as Box

	static function over(h Holder) returns Self
		view v = h.box
		return Self{box: v}
	end 'over'

	function value() returns Count
		return self.box.n
	end 'value'
end 'Pin'

function main() returns ExitCode
	var h = Holder.create(Box.create(1))
	h.box.n = 3
	let pin = Pin.over(h)
	print("{pin.value()}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: view-binding.a-view-captured-in-a-closure-is-legal -->
A closure may capture a `view`.

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

function main() returns ExitCode
	var h = Holder.create(Box.create(1))
	h.box.n = 3
	view v = h.box
	let f = function() gives v.n
	print("{f()}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: view-binding.a-view-of-a-number-captured-in-a-closure-is-accepted -->
A number has no capability: the closure captures a copy of it, so a `view` of one may be captured.

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

function main() returns ExitCode
	var h = Holder.create(Box.create(1))
	h.box.n = 3
	view n = h.box.n
	let g = function() gives n + 1
	print("{g()}\n")
	return 0
end 'main'
```
```stdout
4
```
```exitcode
0
```

<!-- test: view-binding.error.a-view-sent-to-a-service-is-refused -->
A `view` names data its own green thread may write, so it is mutable data of its region: it cannot be frozen for a
message that takes `immutable` data, and it cannot be given away to one that takes `isolated` data.

```maxon
type Box
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create(box Box) returns Self
		return Self{box: box}
	end 'create'
end 'Holder'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p immutable Box) returns Integer
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var holder = Holder.create(Box.create(7))
	holder.box.n = 8
	view v = holder.box
	let r = try await h.keep(v) otherwise 0
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3207: <fragment>:36:27: 'keep' takes `immutable` 'p'; 'v' is mutable. Freeze it first
```

<!-- test: view-binding.error.a-heap-annotated-view-is-refused -->
`@heap` places a binding's own record on the heap, and a `view` names data something else already owns.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	@heap view b = Box.create(1)
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2077: <fragment>:13:2: `view` binds a local name, so it cannot declare a field, a module-level binding, an extension member or a `@heap` binding; use `let` or `var`
```

<!-- test: view-binding.error.a-view-field-is-refused -->
A field is part of its record, not a local name, so it cannot be a `view`.

```maxon
typealias Count = int(0 to 1000)

type Box
	view n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let b = Box.create(1)
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2077: <fragment>:5:2: `view` binds a local name, so it cannot declare a field, a module-level binding, an extension member or a `@heap` binding; use `let` or `var`
```

<!-- test: view-binding.error.a-module-level-view-is-refused -->
A module-level binding outlives every function, so it cannot be a `view`.

```maxon
typealias Count = int(0 to 1000)

view limit = 3 as Count

function main() returns ExitCode
	print("{limit}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2077: <fragment>:4:1: `view` binds a local name, so it cannot declare a field, a module-level binding, an extension member or a `@heap` binding; use `let` or `var`
```

<!-- test: view-binding.error.a-view-in-an-extension-body-is-refused -->
An extension body declares members, and a `view` is not one.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

extension Box
	view k = 1 as Count
end 'Box'

function main() returns ExitCode
	let b = Box.create(1)
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2077: <fragment>:13:2: `view` binds a local name, so it cannot declare a field, a module-level binding, an extension member or a `@heap` binding; use `let` or `var`
```

<!-- test: view-binding.error.a-view-of-a-record-local-moves-it-like-a-let -->
`view w = b` is `let w = b`: it moves the record local `b`, so returning `b` afterwards is refused.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function relay() returns Box
	var b = Box.create(1)
	b.n = 2
	view w = b
	print("{w.n}\n")
	return b
end 'relay'

function main() returns ExitCode
	print("{relay().n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:9: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: view-binding.error.reassigning-a-view-is-refused -->
A `view` is a `let`: its name is never reassigned.

```maxon
typealias Count = int(0 to 1000)

function main() returns ExitCode
	view n = 1 as Count
	n = 2
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:6:2: 'n' is a let and cannot be reassigned
```
