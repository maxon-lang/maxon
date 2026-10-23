---
feature: let-binding-capabilities
keywords: [let, immutable, capability, sole, lent, val, ref, store, keep, E3188, E3078]
category: semantics
---

# What a `let` May Bind, and Where Its Record May Be Stored

## Documentation

A `let` is immutable at every depth unless it is bound from a parameter, where it is as immutable as the caller's
argument. So a `let` binds only a value nothing can write while it lives:

- a fresh record no other name reaches (`let x = Box.create(1)`), which is frozen at birth;
- a read-only value, such as a module `let`'s record;
- a part of a parameter (`let z = p.child`), which is as immutable as the argument the caller passed.

A `let` bound to state something may still write, such as a field of a writable `self`, an element of a `var`
container, a getter's result on a `var` holder, or a `var` record a container also holds, is `E3188`. Bind a
`view` to read it, a `var` to write it, or `let` a clone().

A fresh `let` record may be stored where something may write it only at its last use: the store consumes the
name. Stored there and read again afterwards, it is `E3078`, because the write that the holder allows would
change a `let`.

## Tests

<!-- test: let-binding-capabilities.error.a-let-of-a-field-of-a-writable-receiver-is-refused -->
`bump` writes `self`, so `self.box` is writable state, and the `let` would see the write made one line later.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	var box as Box

	static function create() returns Self
		return Self{box: Box.create(1)}
	end 'create'

	function bump() returns Count
		let b = self.box
		self.box.n = self.box.n + 1
		return b.n
	end 'bump'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	print("{holder.bump()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:20:7: cannot bind `let` 'b' to a value reached through 'self', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-binding-capabilities.error.a-let-of-an-element-of-a-var-array-is-refused -->
An element of a `var` array is writable state: `again` names the same record and writes it.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var boxes = Boxes.create()
	boxes.push(Box.create(1))
	let w = try boxes.get(0) otherwise return 1
	var again = try boxes.get(0) otherwise return 2
	again.n = 5
	print("{w.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:16:6: cannot bind `let` 'w' to a value reached through 'boxes', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-binding-capabilities.error.a-let-of-a-getter-result-on-a-var-is-refused -->
`grab` hands back the record `holder` stores, and `holder` is a `var`, so `grow` writes the record the `let` names.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	var box as Box

	static function create() returns Self
		return Self{box: Box.create(1)}
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
	let b = holder.grab()
	holder.grow()
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:30:6: cannot bind `let` 'b' to a value reached through 'holder', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-binding-capabilities.a-let-of-a-fresh-record-is-accepted -->
A record the factory has just made is reached by no other name, so the `let` freezes it at birth.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let x = Box.create(1)
	print("{x.n}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: let-binding-capabilities.a-let-of-a-module-let-is-accepted -->
A module `let`'s record is read-only everywhere, so a local `let` may name it.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

let shared = Box.create(7)

function main() returns ExitCode
	let y = shared
	print("{y.n}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: let-binding-capabilities.a-let-of-a-part-of-a-parameter-is-accepted -->
`p` is lent for the call, so a `let` of its field is as immutable as the argument, and `peek` only reads it.

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

function peek(p Holder) returns Count
	let z = p.box
	return z.n
end 'peek'

function main() returns ExitCode
	var h = Holder.create(Box.create(4))
	h.box.n = 6
	print("{peek(h)}\n")
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: let-binding-capabilities.error.a-fresh-let-kept-by-a-callee-and-read-again-is-refused -->
`register` keeps its parameter in a module holder and `clobber` writes it there, so `a` would read 99 after the call; the store is refused because `a` is read after it.

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

var holder = Holder.create(Box.create(0))

function register(b Box)
	holder.box = b
end 'register'

function clobber()
	holder.box.n = 99
end 'clobber'

function main() returns ExitCode
	let a = Box.create(1)
	register(a)
	clobber()
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:32:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'register', which keeps it where something may write it; pass a clone()
```

<!-- test: let-binding-capabilities.a-fresh-let-kept-by-a-callee-at-its-last-use-is-consumed -->
The same program with `register(a)` as `a`'s last use: the store consumes the fresh record, so no `let` sees the write.

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

var holder = Holder.create(Box.create(0))

function register(b Box)
	holder.box = b
end 'register'

function clobber()
	holder.box.n = 99
end 'clobber'

function main() returns ExitCode
	let a = Box.create(1)
	register(a)
	clobber()
	print("{holder.box.n}\n")
	return 0
end 'main'
```
```stdout
99
```
```exitcode
0
```

<!-- test: let-binding-capabilities.error.a-fresh-let-pushed-by-a-callee-and-read-again-is-refused -->
`stash` pushes its parameter into a module array and `bump` writes the element, so the push is refused because `a` is read after it.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

var kept = Boxes.create()

function stash(b Box)
	kept.push(b)
end 'stash'

function bump()
	var w = try kept.get(0) otherwise return
	w.n = 99
end 'bump'

function main() returns ExitCode
	let a = Box.create(1)
	stash(a)
	bump()
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:26:2: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stash', which keeps it where something may write it; pass a clone()
```

<!-- test: let-binding-capabilities.error.a-let-of-a-var-a-container-holds-is-refused -->
`a` is a `var` the array also holds, so it is no longer fresh, and the write through the element reaches the record `b` names.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var boxes = Boxes.create()
	var a = Box.create(1)
	a.n = 2
	boxes.push(a)
	let b = a
	var w = try boxes.get(0) otherwise return 1
	w.n = 9
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:18:6: cannot bind `let` 'b' to a value reached through 'a', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-binding-capabilities.error.a-let-of-a-callees-result-that-is-its-written-parameter-is-refused -->
`touch` writes its parameter and returns it, so its result is `a`'s writable record and a `let` may not bind it.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function touch(b Box) returns Box
	b.n = 1
	return b
end 'touch'

function main() returns ExitCode
	var a = Box.create(3)
	let x = touch(a)
	a.n = 5
	print("{x.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:19:6: cannot bind `let` 'x' to a value reached through 'a', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-binding-capabilities.error.a-let-of-a-parameter-a-later-call-writes-is-refused -->
`id` hands back `p` itself and `bump` writes `p`, so the parameter is writable here and `x` would read the write.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function id(b Box) returns Box
	return b
end 'id'

function bump(b Box)
	b.n = 9
end 'bump'

function f(p Box) returns Count
	let x = id(p)
	bump(p)
	return x.n
end 'f'

function main() returns ExitCode
	var a = Box.create(1)
	print("{f(a)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:21:6: cannot bind `let` 'x' to a value reached through 'p', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-binding-capabilities.error.a-let-of-a-var-a-later-closure-captures-is-refused -->
`let x = b` moves `b`, so the closure that captures `b` afterwards uses a moved value: no closure can keep writing a record a `let` holds.

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

var holder = Holder.create(Box.create(0))

function register(b Box) returns Count
	holder.box = b
	return 0
end 'register'

function clobber()
	holder.box.n = 99
end 'clobber'

function main() returns ExitCode
	var b = Box.create(1)
	b.n = 2
	let x = b
	let f = function() gives register(b)
	_ = f()
	clobber()
	print("{x.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:35:36: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: let-binding-capabilities.error.an-element-of-a-lent-parameter-kept-in-a-let-field-is-refused -->
`ms` is lent for the call, so its elements are the caller's writable records; a `let` field keeps one past the call, where `main`'s write through `w` would change it.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Hs = Array with H

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type H
	export let m as Box

	static function create(m Box) returns Self
		return Self{m: m}
	end 'create'
end 'H'

function wrap(ms Boxes) returns Hs
	var hs = Hs.create()

	for m in ms 'each'
		hs.push(H.create(m))
	end 'each'

	return hs
end 'wrap'

function main() returns ExitCode
	var ms = Boxes.create()
	ms.push(Box.create(1))
	let hs = wrap(ms)
	var w = try ms.get(0) otherwise return 1
	w.n = 7
	let h = try hs.get(0) otherwise return 2
	print("{h.m.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:26:13: cannot pass a value other names may still write to parameter 'm' of 'H.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: let-binding-capabilities.a-fresh-let-kept-on-one-arm-of-a-ternary-is-consumed-on-that-path -->
Each arm of the ternary is a different path, and `s` is used once on each, so handing it to `keepAndReturn` on one arm is its last use there.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

var kept = Boxes.create()

function keepAndReturn(b Box) returns Box
	kept.push(b)
	return b
end 'keepAndReturn'

function printOne(b Box)
	print("{b.n}\n")
end 'printOne'

function main() returns ExitCode
	let s = Box.create(3)
	let flag = kept.count() == 0
	printOne(keepAndReturn(s) if flag else s)
	print("{kept.count()}\n")
	return 0
end 'main'
```
```stdout
3
1
```
```exitcode
0
```

<!-- test: let-binding-capabilities.a-fresh-let-kept-on-a-returning-branch-is-read-on-the-other-path -->
The branch that hands `doc` to `take` returns at once, so the keep is `doc`'s last use on that path; the read after the `if` is on the path where nothing kept it.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

var kept = Boxes.create()

function matches(b Box) returns bool
	return b.n > 1
end 'matches'

function take(b Box) returns Count
	kept.push(b)
	return b.n
end 'take'

function scan() returns Count
	var i = 0 as Count

	while i < 3 'trips'
		let doc = Box.create(i)

		if matches(doc) 'ours'
			return take(doc)
		end 'ours'

		print("{doc.n}\n")
		i = i + 1
	end 'trips'

	return 0
end 'scan'

function main() returns ExitCode
	print("{scan()}\n")
	return 0
end 'main'
```
```stdout
0
1
2
```
```exitcode
0
```

<!-- test: let-binding-capabilities.a-string-field-of-a-let-pushed-into-a-local-array-keeps-nothing-of-the-record -->
A `String` is a value, so pushing `p.name` into a local array keeps nothing of `p`'s record, and the `let` is read after the call.

```maxon
typealias Count = int(0 to 1000)
typealias Names = Array with String

type Person
	export var name as String
	export var age as Count

	static function create(name String) returns Self
		return Self{name: name, age: 1}
	end 'create'
end 'Person'

function countName(p Person) returns Count
	var names = Names.create()
	names.push(p.name)
	return names.count() as Count
end 'countName'

function main() returns ExitCode
	let p = Person.create("ada")
	let n = countName(p)
	print("{n} {p.name} {p.age}\n")
	return 0
end 'main'
```
```stdout
1 ada 1
```
```exitcode
0
```

<!-- test: let-binding-capabilities.let-fields-of-a-let-returned-and-bound-with-let-are-accepted -->
`ends` returns two `let` fields of its parameter, which are read-only wherever they go, so binding them with `let` keeps nothing writable and the argument is read afterwards.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function ends(p Pair) returns (Box, Box)
	return (p.left, p.right)
end 'ends'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	let (a, b) = ends(p)
	print("{a.n} {b.n} {p.left.n}\n")
	return 0
end 'main'
```
```stdout
1 2 1
```
```exitcode
0
```

<!-- test: let-binding-capabilities.error.a-fresh-let-pushed-on-an-else-arm-that-falls-through-and-read-again-is-refused -->
The `then` arm returns, but the `else` arm falls through to the read after the `if`, so `stash(a)` is not `a`'s last use on that path and the push is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

var kept = Boxes.create()

function stash(b Box)
	kept.push(b)
end 'stash'

function bump()
	var w = try kept.get(0) otherwise return
	w.n = 99
end 'bump'

function main() returns ExitCode
	let a = Box.create(1)

	if kept.count() > 5 'never'
		return 1
	end 'never' else 'always'
		stash(a)
	end 'always'

	bump()
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:30:3: cannot pass 'a', which is read-only (a `let` binding), to parameter 'b' of 'stash', which keeps it where something may write it; pass a clone()
```

<!-- test: let-binding-capabilities.a-let-destructure-that-names-only-a-scalar-of-a-writable-result-is-accepted -->
The only element the `let` names is a `Count`; the record element is discarded, so no `let` is bound to the record `a` goes on to write.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function touchPair(b Box) returns (Box, Count)
	b.n = 1
	return (b, 0)
end 'touchPair'

function main() returns ExitCode
	var a = Box.create(3)
	let (_, k) = touchPair(a)
	a.n = 5
	print("{k}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-binding-capabilities.error.a-let-destructure-that-names-a-writable-record-is-refused-at-that-name -->
The same call with the record element named `x`: `x` is bound to `a`'s record, which `a` goes on to write, so the refusal names `x`, the binding the program wrote.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function touchPair(b Box) returns (Box, Count)
	b.n = 1
	return (b, 0)
end 'touchPair'

function main() returns ExitCode
	var a = Box.create(3)
	let (x, k) = touchPair(a)
	a.n = 5
	print("{x.n} {k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:19:7: cannot bind `let` 'x' to a value reached through 'a', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-binding-capabilities.a-tuple-of-let-fields-is-read-after-it-is-bound-with-let-and-returned-on -->
The tuple a call returns keeps the read-only mark of the `let` fields it holds: `let (a, b)` and a `let` tuple read it, and a function may hand it on, because nothing in any of those lets a writer reach the fields.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function ends(p Pair) returns (Box, Box)
	return (p.left, p.right)
end 'ends'

function relay(p Pair) returns (Box, Box)
	return ends(p)
end 'relay'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	let (a, b) = relay(p)
	let t = (p.left, p.right)
	let u = ends(p)
	print("{a.n} {b.n} {t.0.n} {t.1.n} {u.0.n} {p.left.n}\n")
	return 0
end 'main'
```
```stdout
1 2 1 2 1 1
```
```exitcode
0
```

<!-- test: let-binding-capabilities.error.a-tuple-of-let-fields-destructured-into-a-var-is-refused -->
`ends` returns a tuple holding two `let` fields. Destructuring it with `var` would make the first name a writable alias of `p.left`, so it is refused at the name.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function ends(p Pair) returns (Box, Box)
	return (p.left, p.right)
end 'ends'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	var (a, _) = ends(p)
	a.n = 9
	print("{a.n} {p.left.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:27:7: cannot assign a value, which may share storage with a `let` field, to mutable binding 'a'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-binding-capabilities.error.a-tuple-of-let-fields-returned-by-a-call-and-bound-to-a-var-is-refused -->
The whole tuple a call returns is bound to a `var`, so its elements would become writable.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function ends(p Pair) returns (Box, Box)
	return (p.left, p.right)
end 'ends'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	var t = ends(p)
	t.0.n = 9
	print("{t.0.n} {p.left.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:27:6: cannot assign a value, which may share storage with a `let` field, to mutable binding 't'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-binding-capabilities.error.a-tuple-literal-of-let-fields-bound-to-a-var-is-refused -->
The tuple literal is accepted, and binding it to a `var` is where it becomes writable.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	var t = (p.left, p.right)
	t.0.n = 9
	print("{t.0.n} {p.left.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:23:6: cannot assign a value, which may share storage with a `let` field, to mutable binding 't'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-binding-capabilities.error.a-tuple-of-let-fields-assigned-to-existing-vars-is-refused -->
A tuple assignment to existing `var` names is a mutable door like a `var` binding.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function ends(p Pair) returns (Box, Box)
	return (p.left, p.right)
end 'ends'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	var a = Box.create(5)
	(a, _) = ends(p)
	a.n = 9
	print("{a.n} {p.left.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:28:3: cannot assign a value, which may share storage with a `let` field, to mutable binding 'a'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-binding-capabilities.error.a-tuple-of-let-fields-stored-into-a-var-field-is-refused -->
Storing the tuple into a `var` field makes its elements writable through that field.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

type Holder
	export var held as (Box, Box)

	static function create(h (Box, Box)) returns Self
		return Self{held: h}
	end 'create'
end 'Holder'

function ends(p Pair) returns (Box, Box)
	return (p.left, p.right)
end 'ends'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	var h = Holder.create((Box.create(3), Box.create(4)))
	h.held = ends(p)
	print("{h.held.0.n} {p.left.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:36:2: cannot store a value, which may share storage with a `let` field, into writable field 'h.held'; use clone()
```

<!-- test: let-binding-capabilities.error.a-tuple-of-let-fields-passed-to-a-function-that-writes-it-is-refused -->
`bump` writes through its tuple parameter, so the tuple holding `p`'s `let` fields may not be passed to it.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function ends(p Pair) returns (Box, Box)
	return (p.left, p.right)
end 'ends'

function bump(t (Box, Box))
	t.0.n = 9
end 'bump'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	bump(ends(p))
	print("{p.left.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:31:2: cannot pass a value read out of a `let` field, which is read-only, to a function that writes parameter 't' (in main); pass a clone()
```

<!-- test: let-binding-capabilities.error.a-tuple-of-let-fields-pushed-into-an-array-is-refused -->
An array element is a mutable place, so the tuple may not be pushed into one.

```maxon
typealias Count = int(0 to 1000)
typealias BoxPair = (Box, Box)
typealias Ends = Array with BoxPair

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function ends(p Pair) returns (Box, Box)
	return (p.left, p.right)
end 'ends'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	var all = Ends.create()
	all.push(ends(p))
	print("{all.count()} {p.left.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:30:6: cannot store a `let` field into mutable place 'an element of the container this call writes'; use clone()
```

<!-- test: let-binding-capabilities.error.a-let-tuple-with-a-read-only-column-and-a-writable-one-is-refused -->
Only the first column of the tuple is read-only. The second is the `var` `p`'s record, which `bump` writes, so the `let` that holds the tuple would see the write and is refused.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function id(b Box) returns Box
	return b
end 'id'

function bump(b Box)
	b.n = b.n + 1
end 'bump'

function main() returns ExitCode
	let q = Pair.create(Box.create(1), right: Box.create(2))
	var p = Box.create(5)
	let t = (q.left, id(p))
	bump(p)
	print("{t.1.n} {q.left.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:32:6: cannot bind `let` 't' to a value reached through 'p', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-binding-capabilities.error.a-let-destructure-of-a-tuple-with-a-writable-column-is-refused -->
The same tuple destructured with `let`: the name bound to the writable column is refused.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function id(b Box) returns Box
	return b
end 'id'

function bump(b Box)
	b.n = b.n + 1
end 'bump'

function main() returns ExitCode
	let q = Pair.create(Box.create(1), right: Box.create(2))
	var p = Box.create(5)
	let (_, y) = (q.left, id(p))
	bump(p)
	print("{y.n} {q.left.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:32:10: cannot bind `let` 'y' to a value reached through 'p', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-binding-capabilities.error.a-let-destructure-of-a-tuple-a-call-returns-with-a-writable-column-is-refused -->
`ends2` returns a tuple of a `let` field and its second argument. The argument is the `var` `p`'s record, so binding the second column with `let` is refused.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function id(b Box) returns Box
	return b
end 'id'

function bump(b Box)
	b.n = b.n + 1
end 'bump'

function ends2(a Pair, b Box) returns (Box, Box)
	return (a.left, b)
end 'ends2'

function main() returns ExitCode
	let q = Pair.create(Box.create(1), right: Box.create(2))
	var p = Box.create(5)
	let (_, y) = ends2(q, b: id(p))
	bump(p)
	print("{y.n} {q.left.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:36:10: cannot bind `let` 'y' to a value reached through 'p', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-binding-capabilities.a-tuple-of-let-fields-stored-into-a-let-field-is-accepted -->
Both columns of the tuple are read-only, so a `let` field holding it keeps nothing writable.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

type Frozen
	export let held as (Box, Box)

	static function create(h (Box, Box)) returns Self
		return Self{held: h}
	end 'create'
end 'Frozen'

function main() returns ExitCode
	let q = Pair.create(Box.create(1), right: Box.create(2))
	let fr = Frozen.create((q.left, q.right))
	print("{fr.held.0.n} {fr.held.1.n} {q.left.n}\n")
	return 0
end 'main'
```
```stdout
1 2 1
```
```exitcode
0
```

<!-- test: let-binding-capabilities.a-tuple-built-from-let-field-arguments-is-bound-with-let -->
`pairUp` returns a tuple of its two parameters. Passing it two `let` fields gives a tuple of read-only columns, which `let (x, y)` binds and reads.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function pairUp(a Box, b Box) returns (Box, Box)
	return (a, b)
end 'pairUp'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	let (x, y) = pairUp(p.left, b: p.right)
	print("{x.n} {y.n} {p.left.n}\n")
	return 0
end 'main'
```
```stdout
1 2 1
```
```exitcode
0
```

<!-- test: let-binding-capabilities.a-writable-column-of-a-local-tuple-with-a-read-only-column-is-destructured-into-a-var -->
Only the first column of the tuple literal is a `let` field's record. The second column is a fresh box, so `var (_, c)` takes a writable record.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function fresh(p Pair) returns Count
	var (_, c) = (p.left, Box.create(3))
	c.n = 4
	return c.n
end 'fresh'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	print("{fresh(p)} {p.left.n}\n")
	return 0
end 'main'
```
```stdout
4 1
```
```exitcode
0
```

<!-- test: let-binding-capabilities.a-writable-column-of-a-tuple-a-call-returns-is-destructured-into-a-var -->
`mixed` returns a `let` field in its first column and a fresh box in its second. Destructuring only the second column with `var` takes a writable record.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function mixed(p Pair) returns (Box, Box)
	return (p.left, Box.create(3))
end 'mixed'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	var (_, b) = mixed(p)
	b.n = 4
	print("{b.n} {p.left.n}\n")
	return 0
end 'main'
```
```stdout
4 1
```
```exitcode
0
```

<!-- test: let-binding-capabilities.a-writable-column-a-function-returns-out-of-a-local-tuple-is-bound-to-a-var -->
`second` builds a tuple with a read-only first column and returns its fresh second column, which the caller binds to a `var` and writes.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function second(p Pair) returns Box
	let t = (p.left, Box.create(3))
	return t.1
end 'second'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	var b = second(p)
	b.n = 4
	print("{b.n} {p.left.n}\n")
	return 0
end 'main'
```
```stdout
4 1
```
```exitcode
0
```

<!-- test: let-binding-capabilities.error.a-read-only-column-of-a-tuple-a-call-returns-is-destructured-into-a-var-beside-a-writable-one -->
The same `mixed` result destructured with `var` takes the first column, which is the `let` field's record, so it is refused at that name while the writable second column is not.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export let left as Box
	export let right as Box

	static function create(left Box, right Box) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function mixed(p Pair) returns (Box, Box)
	return (p.left, Box.create(3))
end 'mixed'

function main() returns ExitCode
	let p = Pair.create(Box.create(1), right: Box.create(2))
	var (a, _) = mixed(p)
	a.n = 4
	print("{a.n} {p.left.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:27:7: cannot assign a value, which may share storage with a `let` field, to mutable binding 'a'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-binding-capabilities.error.a-record-below-a-let-field-stored-into-a-tuple-column-is-refused-when-the-column-is-read-back-into-a-var -->
`m.x.y` lies within the `let` field `x`, so the tuple column that holds it is read-only; destructuring that column into a `var` and writing it would write through `m`.

```maxon
typealias Count = int(0 to 1000)

type Leaf
	export var z as Count

	static function create(z Count) returns Self
		return Self{z: z}
	end 'create'
end 'Leaf'

type Inner
	export var y as Leaf

	static function create(y Leaf) returns Self
		return Self{y: y}
	end 'create'
end 'Inner'

type Outer
	export let x as Inner

	static function create(x Inner) returns Self
		return Self{x: x}
	end 'create'
end 'Outer'

function run(m Outer) returns Count
	var (r, _) = (m.x.y, 1)
	r.z = 5
	return r.z
end 'run'

function main() returns ExitCode
	let m = Outer.create(Inner.create(Leaf.create(1)))
	print("{run(m)} {m.x.y.z}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:30:2: cannot write through 'r', which may be a record read out of a `let` field that a call or a merge handed back; nothing reachable through a `let` field may be written; use clone()
```

<!-- test: let-binding-capabilities.error.a-result-within-a-tuple-argument-keeps-the-read-only-mark -->
`first` returns the first column of the tuple it is passed. The tuple holds a `let` field's record, so the result may be that record and writing it is refused.

```maxon
typealias Count = int(0 to 1000)

type Rec
	export var v as Count

	static function create(v Count) returns Self
		return Self{v: v}
	end 'create'
end 'Rec'

type Holder
	export let r as Rec

	static function create(r Rec) returns Self
		return Self{r: r}
	end 'create'
end 'Holder'

function first(t (Rec, Count)) returns Rec
	return t.0
end 'first'

function run(h Holder) returns Count
	var r = first((h.r, 1))
	r.v = 5
	return r.v
end 'run'

function main() returns ExitCode
	let h = Holder.create(Rec.create(1))
	print("{run(h)} {h.r.v}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:26:2: cannot write through 'r', which may be a record read out of a `let` field that a call or a merge handed back; nothing reachable through a `let` field may be written; use clone()
```

<!-- test: let-binding-capabilities.error.a-fresh-tuple-holding-a-writable-record-is-refused-by-a-let-field -->
The tuple is fresh, but its second column is the record `id(p)` hands back, which is `p`'s, and `bump` writes `p` after the `let` field has been given the tuple.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Frozen
	export let held as (Box, Box)

	static function create(h (Box, Box)) returns Self
		return Self{held: h}
	end 'create'
end 'Frozen'

function id(b Box) returns Box
	return b
end 'id'

function bump(b Box)
	b.n = b.n + 1
end 'bump'

function main() returns ExitCode
	var p = Box.create(5)
	let fr = Frozen.create((Box.create(1), id(p)))
	bump(p)
	print("{fr.held.1.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:30:18: cannot pass a value that reaches 'p', which something may still write, to parameter 'h' of 'Frozen.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: let-binding-capabilities.error.a-fresh-record-holding-a-writable-record-is-refused-by-a-let-field -->
The `Wrap` is fresh, but its `b` field is the record `id(p)` hands back, which is `p`'s, and `bump` writes `p` after the `let` field has been given the `Wrap`.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Wrap
	export var a as Box
	export var b as Box

	static function create(a Box, b Box) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Wrap'

type Frozen
	export let held as Wrap

	static function create(h Wrap) returns Self
		return Self{held: h}
	end 'create'
end 'Frozen'

function id(b Box) returns Box
	return b
end 'id'

function bump(b Box)
	b.n = b.n + 1
end 'bump'

function main() returns ExitCode
	var p = Box.create(5)
	let fr = Frozen.create(Wrap.create(Box.create(1), b: id(p)))
	bump(p)
	print("{fr.held.b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:39:18: cannot pass a value that reaches 'p', which something may still write, to parameter 'h' of 'Frozen.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: let-binding-capabilities.a-fresh-tuple-of-fresh-records-is-accepted-by-a-let-field -->
Both columns are records the tuple alone holds, so the `let` field keeps nothing another name can write.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Frozen
	export let held as (Box, Box)

	static function create(h (Box, Box)) returns Self
		return Self{held: h}
	end 'create'
end 'Frozen'

function main() returns ExitCode
	let fr = Frozen.create((Box.create(1), Box.create(2)))
	print("{fr.held.0.n} {fr.held.1.n}\n")
	return 0
end 'main'
```
```stdout
1 2
```
```exitcode
0
```

<!-- test: let-binding-capabilities.error.a-fresh-tuple-holding-a-writable-record-is-refused-by-a-let-field-literal -->
The factory stores the fresh tuple into its own `let` field directly, and `bump` writes `p` after the store.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Frozen
	export let held as (Box, Box)

	static function build() returns Self
		var p = Box.create(5)
		let f = Self{held: (Box.create(1), id(p))}
		bump(p)
		return f
	end 'build'
end 'Frozen'

function id(b Box) returns Box
	return b
end 'id'

function bump(b Box)
	b.n = b.n + 1
end 'bump'

function main() returns ExitCode
	let fr = Frozen.build()
	print("{fr.held.1.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:17:16: cannot store a value that reaches 'p', which something may still write, into a `let` field 'Frozen.held': a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: let-binding-capabilities.a-fresh-generic-container-kept-by-a-let-field-is-accepted-for-a-writable-item -->
The `let` field holds an empty array of its own, so a `Bag` of a type with a `var` field keeps nothing anything else can write.

```maxon
typealias Count = int(0 to 1000)
typealias CounterBag = Bag with Counter

type Counter
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Bag uses Item
	typealias ItemArray = Array with Item

	let kept as ItemArray

	static function create() returns Self
		return Self{kept: ItemArray.create()}
	end 'create'

	function size() returns Count
		return self.kept.count() as Count
	end 'size'
end 'Bag'

function main() returns ExitCode
	let bag = CounterBag.create()
	print("{bag.size()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: let-binding-capabilities.a-var-never-used-again-is-moved-into-a-fresh-record-kept-by-a-let-field -->
`q` is written before it is handed over and never named again, so nothing can write the record the `let` field ends up holding.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Wrap
	export var a as Box
	export var b as Box

	static function create(a Box, b Box) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Wrap'

type Frozen
	export let held as Wrap

	static function create(h Wrap) returns Self
		return Self{held: h}
	end 'create'
end 'Frozen'

function bump(b Box)
	b.n = b.n + 1
end 'bump'

function main() returns ExitCode
	var q = Box.create(2)
	bump(q)
	let fr = Frozen.create(Wrap.create(Box.create(1), b: q))
	print("{fr.held.b.n}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: let-binding-capabilities.a-var-never-used-again-is-moved-into-a-fresh-record-bound-by-a-let -->
The same hand-over into a `let` binding: `q` is dead after the `Wrap` takes it.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Wrap
	export var a as Box
	export var b as Box

	static function create(a Box, b Box) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Wrap'

function bump(b Box)
	b.n = b.n + 1
end 'bump'

function main() returns ExitCode
	var q = Box.create(2)
	bump(q)
	let w = Wrap.create(Box.create(1), b: q)
	print("{w.b.n}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: let-binding-capabilities.error.a-fresh-tuple-holding-a-module-var-is-refused-by-a-let-field -->
`gBox` is module storage that `main` writes after the `let` field has been given the tuple.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Frozen
	export let held as (Box, Box)

	static function create() returns Self
		let f = Self{held: (Box.create(1), gBox)}
		gBox.n = 9
		return f
	end 'create'
end 'Frozen'

var gBox = Box.create(5)

function main() returns ExitCode
	let fr = Frozen.create()
	print("{fr.held.1.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:16:16: cannot store a value that reaches 'a module variable', which something may still write, into a `let` field 'Frozen.held': a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: let-binding-capabilities.error.a-let-tuple-holding-a-module-var-is-refused -->
The tuple is bound with `let`, and `gBox` is written after it.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

var gBox = Box.create(5)

function main() returns ExitCode
	let t = (Box.create(1), gBox)
	gBox.n = 9
	print("{t.1.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3188: <fragment>:15:6: cannot bind `let` 't' to a value reached through 'a module variable', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: let-binding-capabilities.a-let-bound-to-the-awaited-result-of-a-promise-removed-from-a-var-array-is-accepted -->
`remove` moves the promise out of `replies`, so the record `await` hands back no longer lies inside the array that `dispatch` keeps pushing to.

```maxon
typealias Count = int(0 to 1000)

type Reply
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Reply'

typealias ReplyPromise = Promise with Reply
typealias ReplyPromises = Array with ReplyPromise

function answer(n Count) returns Reply
	return Reply.create(n)
end 'answer'

function dispatch(n Count, replies ReplyPromises)
	replies.push(async answer(n))
end 'dispatch'

function drain() returns Count
	var replies = ReplyPromises.create()
	var total = 0 as Count

	for n in 1 to 3 'each'
		dispatch(n as Count, replies: replies)
	end 'each'

	while replies.count() > 0 'serveWhoeverFinishedFirst'
		let ready = replies.awaitAny()
		let reply = try replies.remove(ready) otherwise panic("awaitAny answered a slot of this very array")
		let result = await reply
		total = total + result.n
	end 'serveWhoeverFinishedFirst'

	return total
end 'drain'

function main() returns ExitCode
	print("{drain()}\n")
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: let-binding-capabilities.a-var-moved-into-a-fresh-record-bound-by-a-let-is-accepted-beside-an-unrelated-module-var -->
`shelf` is a module `var` of the same type as `box`, and `holder` holds `box`, a local `var` that is dead after `wrap` takes it: nothing `holder` holds is `shelf`'s record.

```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	function read() returns Count
		return self.n
	end 'read'

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

var shelf = Box.create(7)

type Holder
	var box as Box

	static function wrap(box Box) returns Self
		return Self{box: box}
	end 'wrap'
end 'Holder'

function keep() returns Count
	var box = Box.create(1)
	let holder = Holder.wrap(box)
	_ = holder
	return 1
end 'keep'

function main() returns ExitCode
	let kept = keep()
	return (shelf.read() + kept) as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: let-binding-capabilities.a-let-of-a-fresh-record-literal-holding-a-parameter-is-accepted-beside-an-unrelated-module-var -->
The literal `built` holds the parameter `box`, which is the caller's record and no module `var`'s; `shelf` is only another `Box`.

```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	function read() returns Count
		return self.n
	end 'read'

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

var shelf = Box.create(7)

type Holder
	var box as Box

	static function wrap(box Box) returns Self
		let built = Self{box: box}
		return built
	end 'wrap'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.wrap(Box.create(1))
	_ = holder
	return shelf.read() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: let-binding-capabilities.error.a-var-a-function-stored-in-a-module-var-is-refused-when-a-fresh-record-takes-it-into-a-let -->
`register` stores `box` in the module `var` `shelf`, so the `holder` that takes `box` still reaches a record `shelf` writes.

```maxon
typealias Count = int(0 to 1000)
typealias BoxArray = Array with Box

type Box
	var n as Count

	function read() returns Count
		return self.n
	end 'read'

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

var shelf = BoxArray.create()

function register(b Box)
	shelf.push(b)
end 'register'

type Holder
	var box as Box

	function peek() returns Count
		return self.box.read()
	end 'peek'

	static function wrap(box Box) returns Self
		return Self{box: box}
	end 'wrap'
end 'Holder'

function keep() returns Count
	var box = Box.create(1)
	register(box)
	let holder = Holder.wrap(box)
	return holder.peek()
end 'keep'

function main() returns ExitCode
	return keep() as ExitCode
end 'main'
```
```maxoncstderr
error E3188: <fragment>:38:6: cannot bind `let` 'holder' to a value reached through 'box', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```
