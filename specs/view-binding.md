---
feature: view-binding
keywords: [view, binding, read-only, alias, await, E3159, E3078, E3138]
category: semantics
---

# `view`: a Read-Only Name Over Data Others May Write

## Documentation

`view x = e` binds any value read-only through `x`. Unlike a `let`, a `view` makes no promise that nothing else
writes the data: a write made through another name, including by another coroutine across an `await`, is seen
through the `view`.

Writing through a `view` is `E3159`. A `view` lives only inside its function: storing it, handing it to a parameter
that is kept, returning it (or a call's result that is still the `view`) and capturing it in a closure are `E3078`,
handing it to a parameter the callee writes is `E3159`, and sending it to a service is `E3138`. Use a clone() for any
of them. A `view` of a number or another value with no capability is copied where it goes, so a closure may capture
it.

A `view` holds its own reference to the record its value names. A record something may write is refused where it is
sent to a service (`E3138`), published (`E3174`) or handed to a `sole` parameter (`E3078`) when it may be reached from
the value of any `view` declared earlier in the function, whether or not the view is read again and whatever the
view's initializer is: a name, a field or element of it, a ternary, an `otherwise`, a `match` or a call that may hand
the record back. Reassigning the `var` a fresh record makes it sole again. A record nothing can write (every field a
`let`) is shared instead: it is sent, published or handed to a `sole` parameter as it is, and the view stays valid. Returning the `var` is unaffected, since every view of it ends with the function.

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

var holder = Holder.create(Box.create(1))

function bump() returns Count
	sleep(1)
	holder.box.n = 5
	return 1
end 'bump'

function main() returns ExitCode
	view v = holder.box
	let task = async bump()
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

<!-- test: view-binding.error.a-write-through-a-view-is-refused -->
A `view` is read-only through its own name.

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
```maxoncstderr
error E3159: <fragment>:24:2: cannot write through 'v': 'v' is read-only (a `view`) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: view-binding.error.a-view-stored-into-a-field-or-pushed-is-refused -->
A `var` field and an array element are places something may write, so neither may hold a `view`.

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
	var boxes = Boxes.create()
	h.box.n = 3
	view v = h.box
	other.box = v
	boxes.push(v)
	print("{other.box.n} {boxes.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:27:8: cannot store view 'v' into mutable place 'Holder.box'; use clone()
error E3078: <fragment>:28:8: cannot store view 'v' into mutable place 'an element of the container push writes'; use clone()
```

<!-- test: view-binding.error.a-view-passed-to-a-parameter-that-is-kept-is-refused -->
`stash` keeps its parameter in a module array, which would outlive the `view`.

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

var kept = Boxes.create()

function stash(b Box)
	kept.push(b)
end 'stash'

function main() returns ExitCode
	var h = Holder.create(Box.create(1))
	h.box.n = 3
	view v = h.box
	stash(v)
	print("{kept.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:31:2: cannot pass 'v', which is read-only (a `view`), to parameter 'b' of 'stash', which keeps it where something may write it; pass a clone()
```

<!-- test: view-binding.error.a-returned-view-is-refused -->
A `view` cannot outlive the function that binds it.

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
```maxoncstderr
error E3078: <fragment>:22:9: cannot return view 'v': a `view` cannot outlive its function; return a clone()
```

<!-- test: view-binding.error.a-view-returned-through-a-call-that-hands-it-back-is-refused -->
`pick` returns its argument, so its result is still the `view`, and returning it is refused as returning `v` is.

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
```maxoncstderr
error E3078: <fragment>:26:9: cannot return view 'v': a `view` cannot outlive its function; return a clone()
```

<!-- test: view-binding.error.a-view-handed-to-a-function-that-writes-its-parameter-is-refused -->
`bump` writes its parameter, and nothing may be written through a `view`.

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
```maxoncstderr
error E3159: <fragment>:28:2: cannot pass 'v', which is read-only (a `view`), to a function that writes parameter 'b' (in main); pass a clone()
```

<!-- test: view-binding.error.a-view-stored-into-a-let-field-is-refused -->
A `let` field keeps its record for the holder's whole life, which a `view` cannot outlast.

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
```maxoncstderr
error E3078: <fragment>:25:15: cannot store view 'v' into a `let` field 'Pin.box': a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: view-binding.error.a-view-captured-in-a-closure-is-refused -->
A closure may outlive the function, so it cannot capture a `view`.

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
```maxoncstderr
error E3078: <fragment>:24:27: cannot capture view 'v' in a closure; capture a clone()
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
A `view` names data its own green thread may write, so it can be neither moved nor shared with a service.

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

	export function keep(p Box) returns Integer
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
error E3138: <fragment>:36:27: argument `v` of the message `Store.keep` is a `view`: it names data something else may write, so it can be neither moved nor shared with the service. Send a `.clone()`
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

<!-- test: view-binding.a-var-returned-after-it-has-been-viewed-is-accepted -->
Every view of `b` ends with the function, so returning the `var` after it has been viewed is unaffected by the view.

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
```stdout
2
2
```
```exitcode
0
```

<!-- test: view-binding.every-arm-of-a-chained-ternary-view-is-shared -->
Every arm of the chained ternary may become `w`'s value, so each is shared with the view rather than moved, and `a`, `b` and `e` are all read afterwards.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = Box.create(1)
	var b = Box.create(2)
	var e = Box.create(3)
	a.n = 4
	b.n = 5
	e.n = 6
	let c = a.n > 0
	let d = b.n > 0
	view w = a if c else b if d else e
	print("{w.n} {a.n} {b.n} {e.n}\n")
	return 0
end 'main'
```
```stdout
4 4 5 6
```
```exitcode
0
```
