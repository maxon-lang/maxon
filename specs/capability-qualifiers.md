---
feature: capability-qualifiers
keywords: [keep, sole, returns readonly, function type, interface, capability, E3189, E2076, E3078]
category: semantics
---

# `keep`, `sole` and `returns readonly` at Boundaries With No Body

## Documentation

A call through a function value or an interface method has no body the checker can read, so the boundary declares
what it does with its values. The qualifier is written before the type:

- `keep` on a parameter: the callee keeps the argument where something may write it.
- `sole` on a parameter: the callee consumes the argument and owns it, so it may keep it anywhere. The caller
  passes a fresh value or a name at its last use. `returns sole T`: the result is fresh, reached by no other name.
- `returns readonly T`: the result is read-only.

They are written only on a parameter or result of a function-type alias, of an interface method requirement, or on
the result of a generic function. Anywhere else a qualifier is `E2076`.

A caller trusts the declaration: an argument to a `keep` parameter follows the same rule as one handed to a
function that keeps it, so a `let` read again afterwards is `E3078`, and so is one handed to a `sole` parameter. A
function value converted to the alias, or a conformer's method, that does more than its boundary declares (keeps a
parameter not declared `keep` or `sole`, or returns state something may write where `returns sole` or
`returns readonly` is declared) is `E3189`. A function value typed by one alias converts to another alias, by a
`return`, a store or an argument, only when it declares at least the result capability the target declares and
keeps no parameter the target does not declare `keep`; otherwise the conversion is `E3189`.

## Tests

<!-- test: capability-qualifiers.a-function-value-that-keeps-its-parameter-converts-to-a-keep-alias -->
`Sink` declares that its parameter is kept, so `stash` converts to it; the `var` box handed to it stays writable and the array sees the write.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(keep Box)

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

function feed(sink Sink) returns Count
	var box = Box.create(3)
	sink(box)
	box.n = 4
	return box.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	view first = try kept.get(0) otherwise return 1
	print("{n} {first.n}\n")
	return 0
end 'main'
```
```stdout
4 4
```
```exitcode
0
```

<!-- test: capability-qualifiers.error.a-function-value-that-keeps-a-parameter-its-alias-does-not-declare-is-refused -->
`Look` does not declare its parameter `keep`, so a caller could hand it a `let`; `stash` keeps it, and is refused at the conversion.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Look = function(Box)

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

function feed(sink Look) returns Count
	var box = Box.create(3)
	sink(box)
	box.n = 4
	return box.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	view first = try kept.get(0) otherwise return 1
	print("{n} {first.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3189: <fragment>:28:15: function value for 'Look' keeps its parameter 'b', but 'Look' does not declare it `keep`
```

<!-- test: capability-qualifiers.error.a-conformer-that-keeps-a-parameter-the-requirement-does-not-declare-is-refused -->
`Taker.take` does not declare its parameter `keep`, and `Shelf.take` pushes it into a field.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Taker
	function take(b Box)
end 'Taker'

type Shelf implements Taker
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'

	function take(b Box)
		self.items.push(b)
	end 'take'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create()
	var box = Box.create(1)
	shelf.take(box)
	box.n = 2
	view first = try shelf.items.get(0) otherwise return 1
	print("{first.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3189: <fragment>:24:11: 'Shelf.take' keeps its parameter 'b', but 'Taker.take' does not declare it `keep`
```

<!-- test: capability-qualifiers.a-conformer-that-keeps-a-parameter-the-requirement-declares-keep-is-accepted -->
The same conformer under a requirement that declares `keep`: the `var` box it keeps stays shared with the shelf.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Taker
	function take(b keep Box)
end 'Taker'

type Shelf implements Taker
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'

	function take(b Box)
		self.items.push(b)
	end 'take'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create()
	var box = Box.create(1)
	shelf.take(box)
	box.n = 2
	view first = try shelf.items.get(0) otherwise return 1
	print("{first.n}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: capability-qualifiers.error.a-function-value-returning-stored-state-for-a-returns-sole-alias-is-refused -->
`Make` promises a fresh result, but `current` hands back the record a module holder stores.

```maxon
typealias Count = int(0 to 1000)
typealias Make = function() returns sole Box

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

function current() returns Box
	return holder.box
end 'current'

function build(make Make) returns Count
	var b = make()
	b.n = b.n + 1
	return b.n
end 'build'

function main() returns ExitCode
	holder.box.n = 2
	let n = build(current)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3189: <fragment>:35:16: 'current' returns a value something may still write, but 'Make' declares `returns sole`
```

<!-- test: capability-qualifiers.error.keep-on-a-cast-type-is-refused -->
The type a cast names is not a boundary, so it takes no capability qualifier.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let b = Box.create(1) as keep Box
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2076: <fragment>:13:27: 'keep' qualifies a parameter or result of a function type, an interface method or a generic function only
```

<!-- test: capability-qualifiers.error.a-let-handed-to-a-keep-parameter-and-read-again-is-refused -->
`sink` declares its parameter `keep`, so handing it the `let` `box` and reading `box` afterwards is refused as a call to a function that keeps it would be.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b keep Box)

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

function feed(sink Sink) returns Count
	let box = Box.create(3)
	sink(box)
	return box.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:22:2: cannot pass 'box', which is read-only (a `let` binding), to parameter 'b' of 'sink', which keeps it where something may write it; pass a clone()
```

<!-- test: capability-qualifiers.a-sole-result-of-a-function-value-is-consumed-into-a-writable-field -->
`Maker` promises a fresh result, so the `let` of `make()` is sole, and storing it into the holder at its last use consumes it; the holder then writes its own record.

```maxon
typealias Count = int(0 to 1000)
typealias Maker = function() returns sole Box

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

function fresh() returns Box
	return Box.create(2)
end 'fresh'

function build(make Maker) returns Count
	let b = make()
	var h = Holder.create(b)
	h.box.n = h.box.n + 1
	return h.box.n
end 'build'

function main() returns ExitCode
	let n = build(fresh)
	print("{n}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: capability-qualifiers.error.a-sole-result-stored-into-a-module-holder-and-read-again-is-refused -->
The sole `let` is stored into a module holder that is written next, and `b` is read after the store, so the store does not consume it.

```maxon
typealias Count = int(0 to 1000)
typealias Maker = function() returns sole Box

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

function fresh() returns Box
	return Box.create(3)
end 'fresh'

function build(make Maker) returns Count
	let b = make()
	holder.box = b
	holder.box.n = 9
	return b.n
end 'build'

function main() returns ExitCode
	let n = build(fresh)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:29:9: cannot store `let` 'b' into mutable place 'Holder.box'; use clone()
```

<!-- test: capability-qualifiers.a-let-field-returned-for-a-returns-readonly-requirement-is-accepted -->
`Source.current` declares a read-only result, and `Fixed.current` returns a `let` field, so a `let` of the result through the interface is accepted.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Source
	function current() returns readonly Box
end 'Source'

type Fixed implements Source
	let box as Box

	static function create() returns Self
		return Self{box: Box.create(5)}
	end 'create'

	function current() returns Box
		return self.box
	end 'current'
end 'Fixed'

function show(src Source) returns Count
	let c = src.current()
	return c.n
end 'show'

function main() returns ExitCode
	let fixed = Fixed.create()
	print("{show(fixed)}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: capability-qualifiers.error.a-var-field-returned-for-a-returns-readonly-requirement-is-refused -->
`Live.current` returns a `var` field of its receiver, which something may still write, so it breaks the read-only result `Source.current` declares.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Source
	function current() returns readonly Box
end 'Source'

type Live implements Source
	var box as Box

	static function create() returns Self
		return Self{box: Box.create(5)}
	end 'create'

	function current() returns Box
		return self.box
	end 'current'
end 'Live'

function show(src Source) returns Count
	let c = src.current()
	return c.n
end 'show'

function main() returns ExitCode
	let live = Live.create()
	print("{show(live)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3189: <fragment>:23:11: 'Live.current' returns a value something may still write, but 'Source.current' declares `returns readonly`
```

<!-- test: capability-qualifiers.error.an-alias-value-returned-as-an-alias-declaring-returns-sole-is-refused -->
`Make` promises nothing about its result, so returning an `m` of that type as a `MakeSole` would let a caller trust stored state as fresh.

```maxon
typealias Count = int(0 to 1000)
typealias Make = function() returns Box
typealias MakeSole = function() returns sole Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

var stored = Box.create(1)

function current() returns Box
	return stored
end 'current'

function promote(m Make) returns MakeSole
	return m
end 'promote'

function main() returns ExitCode
	let f = promote(current)
	let b = f()
	stored.n = 9
	print("{b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3189: <fragment>:21:9: function value of type 'Make' does not declare `returns sole`, but 'MakeSole' does
```

<!-- test: capability-qualifiers.an-alias-value-returned-as-an-alias-it-matches-is-accepted -->
The same conversion from an alias that declares `returns sole` itself.

```maxon
typealias Count = int(0 to 1000)
typealias Fresh = function() returns sole Box
typealias MakeSole = function() returns sole Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function fresh() returns Box
	return Box.create(1)
end 'fresh'

function promote(m Fresh) returns MakeSole
	return m
end 'promote'

function main() returns ExitCode
	let f = promote(fresh)
	let b = f()
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

<!-- test: capability-qualifiers.error.a-keep-alias-value-passed-as-an-alias-without-keep-is-refused -->
`Sink` keeps its parameter and `Look` does not declare it, so `look` would hand a kept value its `let`.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b keep Box)
typealias Look = function(b Box)

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

function look(sink Look) returns Count
	let box = Box.create(3)
	sink(box)
	return box.n
end 'look'

function relay(sink Sink) returns Count
	return look(sink)
end 'relay'

function main() returns ExitCode
	let n = relay(stash)
	print("{n} {kept.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3189: <fragment>:28:14: function value of type 'Sink' keeps its parameter 'b', but 'Look' does not declare it `keep`
```

<!-- test: capability-qualifiers.a-sole-parameter-takes-a-fresh-value-and-a-let-at-its-last-use -->
`Sink` consumes its argument, so the value behind it may keep it; a fresh record and a `let` at its last use are both consumed.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	sink(Box.create(1))
	let b = Box.create(2)
	sink(b)
	return kept.count()
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	view last = try kept.get(1) otherwise return 1
	print("{n} {last.n}\n")
	return 0
end 'main'
```
```stdout
2 2
```
```exitcode
0
```

<!-- test: capability-qualifiers.error.a-let-handed-to-a-sole-parameter-and-read-again-is-refused -->
A `sole` parameter consumes its argument, so `b` cannot be read after the call.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	let b = Box.create(2)
	sink(b)
	return b.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:22:2: cannot pass 'b', which is read-only (a `let` binding), to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.a-conformer-that-keeps-a-parameter-the-requirement-declares-sole-is-accepted -->
A requirement that declares its parameter `sole` lets the conformer own it, so `Shelf.take` may keep it.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Taker
	function take(b sole Box)
end 'Taker'

type Shelf implements Taker
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'

	function take(b Box)
		self.items.push(b)
	end 'take'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create()
	shelf.take(Box.create(1))
	let b = Box.create(2)
	shelf.take(b)
	view last = try shelf.items.get(1) otherwise return 1
	print("{shelf.items.count()} {last.n}\n")
	return 0
end 'main'
```
```stdout
2 2
```
```exitcode
0
```

<!-- test: capability-qualifiers.error.keep-on-a-parameter-of-a-function-with-a-body-is-refused -->
A function with a body states what it does with its parameter by doing it, so the parameter takes no qualifier.

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

function stash(b keep Box)
	kept.push(b)
end 'stash'

function main() returns ExitCode
	stash(Box.create(1))
	print("{kept.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2076: <fragment>:15:18: 'keep' qualifies a parameter or result of a function type, an interface method or a generic function only
```

<!-- test: capability-qualifiers.error.a-var-handed-to-a-sole-parameter-is-moved -->
A `sole` parameter consumes its argument, so after `sink(b)` the name `b` no longer owns the record and writing through it is refused.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	var b = Box.create(1)
	sink(b)
	b.n = 5
	return b.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:23:2: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: capability-qualifiers.error.a-var-handed-to-a-sole-parameter-after-it-has-been-viewed-and-read-afterwards-is-refused -->
`w` has viewed `b` and reads the record after `sink(b)`, so `b` is not the record's only owner and the parameter cannot consume it.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	var b = Box.create(1)
	b.n = 2
	view w = b
	sink(b)
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:24:2: cannot pass the `var` 'b', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-var-handed-to-a-sole-parameter-after-it-has-been-viewed-is-refused -->
`w` is not read after `n` is taken, but once it has viewed `b` its reference is a second owner for the rest of the function, so `b` is not the record's only owner at `sink(b)`.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	var b = Box.create(1)
	b.n = 2
	view w = b
	let n = w.n
	sink(b)
	return n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:25:2: cannot pass the `var` 'b', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-field-of-a-var-handed-to-a-sole-parameter-is-refused -->
`h.box` stays reachable through `h` after the call, so the field's record is not the argument's alone to give up.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	var h = Holder.create(Box.create(1))
	sink(h.box)
	h.box.n = 5
	return h.box.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:30:2: cannot pass a value other names may still write to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-parameter-handed-on-to-a-sole-parameter-is-refused -->
`b` is lent for the call, so the caller still owns the record and `feed` cannot give it away.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink, b Box) returns Count
	sink(b)
	b.n = 5
	return b.n
end 'feed'

function main() returns ExitCode
	var b = Box.create(1)
	let n = feed(stash, b: b)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:21:2: cannot pass a value other names may still write to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-var-handed-to-a-sole-requirement-through-the-interface-is-moved -->
The same consumption through a witness dispatch: `Taker.take` declares its parameter `sole`, so `b` is moved by `t.take(b)`.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Taker
	function take(b sole Box)
end 'Taker'

type Shelf implements Taker
	export var items as Boxes

	static function create() returns Self
		return Self{items: Boxes.create()}
	end 'create'

	function take(b Box)
		self.items.push(b)
	end 'take'
end 'Shelf'

function feed(t Taker) returns Count
	var b = Box.create(1)
	t.take(b)
	b.n = 5
	return b.n
end 'feed'

function main() returns ExitCode
	var shelf = Shelf.create()
	print("{feed(shelf)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:2: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: capability-qualifiers.error.a-var-viewed-then-reassigned-on-one-path-handed-to-a-sole-parameter-is-refused -->
When `fresh` is false, `b` still names the record `w` viewed, so the `b` that reaches `sink(b)` is not sole on every path.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink, fresh bool) returns Count
	var b = Box.create(1)
	b.n = 2
	view w = b

	if fresh 'swap'
		b = Box.create(3)
	end 'swap'

	sink(b)
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash, fresh: false)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:29:2: cannot pass the `var` 'b', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-var-viewed-by-an-outer-and-an-inner-view-handed-to-a-sole-parameter-is-refused -->
The inner `view u` has ended by `sink(b)`, but `w` has viewed `b` too, and a view counts for the rest of the function; the first view of `b` is named.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	var b = Box.create(1)
	b.n = 2
	view w = b

	if w.n > 0 'peek'
		view u = b
		print("{u.n}\n")
	end 'peek'

	sink(b)
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:30:2: cannot pass the `var` 'b', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-var-whose-field-has-been-viewed-handed-to-a-sole-parameter-is-refused -->
`w` views a field of `b`, so its reference co-owns part of `b`'s graph, and `b` is not the graph's only owner at `sink(b)`.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

type Inner
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Inner'

type Box
	export var child as Inner

	static function create(n Count) returns Self
		return Self{child: Inner.create(n)}
	end 'create'
end 'Box'

var kept = Boxes.create()

function stash(b Box)
	kept.push(b)
end 'stash'

function feed(sink Sink) returns Count
	var b = Box.create(1)
	b.child.n = 2
	view w = b.child
	sink(b)
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:32:2: cannot pass the `var` 'b', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.a-viewed-var-reassigned-a-fresh-record-is-sole-again -->
`b = Box.create(3)` gives `b` a record no view has seen, so `sink(b)` consumes it; `w` keeps the first record.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	var b = Box.create(1)
	b.n = 2
	view w = b
	let n = w.n
	b = Box.create(3)
	sink(b)
	return n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	view first = try kept.get(0) otherwise return 1
	print("{n} {first.n}\n")
	return 0
end 'main'
```
```stdout
2 3
```
```exitcode
0
```

<!-- test: capability-qualifiers.error.a-var-a-ternary-view-may-name-handed-to-a-sole-parameter-is-refused -->
`w` names `b` or `c` depending on the comparison, so `c` may not be the record's only owner at `sink(c)`.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	var b = Box.create(1)
	var c = Box.create(2)
	b.n = 3
	c.n = 4
	view w = b if b.n > c.n else c
	sink(c)
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:26:2: cannot pass the `var` 'c', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-var-an-otherwise-view-may-name-handed-to-a-sole-parameter-is-refused -->
`w` names `spare` when the array has no element 0, so `spare` may not be the record's only owner at `sink(spare)`.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	var boxes = Boxes.create()
	boxes.push(Box.create(1))
	var spare = Box.create(0)
	spare.n = 2
	view w = try boxes.get(0) otherwise spare
	sink(spare)
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:26:2: cannot pass the `var` 'spare', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-var-a-view-of-a-calls-result-may-name-handed-to-a-sole-parameter-is-refused -->
`pick` hands back its argument, so the record `w` views is `b`'s.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function pick(x Box) returns Box
	return x
end 'pick'

function feed(sink Sink) returns Count
	var b = Box.create(1)
	b.n = 2
	view w = pick(b)
	sink(b)
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:28:2: cannot pass the `var` 'b', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-var-a-view-of-a-methods-result-may-name-handed-to-a-sole-parameter-is-refused -->
The same through a method: `c.pick(b)` hands back `b`, not something of `c`'s.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Picker
	let tag as Count

	static function create() returns Self
		return Self{tag: 0}
	end 'create'

	function pick(x Box) returns Box
		return x
	end 'pick'
end 'Picker'

var kept = Boxes.create()

function stash(b Box)
	kept.push(b)
end 'stash'

function feed(sink Sink) returns Count
	let c = Picker.create()
	var b = Box.create(1)
	b.n = 2
	view w = c.pick(b)
	sink(b)
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:37:2: cannot pass the `var` 'b', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-var-a-match-view-may-name-handed-to-a-sole-parameter-is-refused -->
One arm of the `match` gives `b`, so the record `w` views may be `b`'s.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

enum Side
	left
	right
end 'Side'

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

function feed(sink Sink, side Side) returns Count
	var b = Box.create(1)
	var c = Box.create(2)
	b.n = 3
	c.n = 4

	view w = match side 'which'
		left gives b
		right gives c
	end 'which'

	sink(b)
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash, side: Side.left)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:36:2: cannot pass the `var` 'b', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-var-a-parenthesized-view-names-handed-to-a-sole-parameter-is-refused -->
Parentheses around the initializer change nothing: `w` views `b`'s record.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	var b = Box.create(1)
	b.n = 2
	view w = (b)
	sink(b)
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:24:2: cannot pass the `var` 'b', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-viewed-var-handed-in-parentheses-to-a-sole-parameter-is-refused -->
Parentheses around the argument change nothing: the argument is `b`, which `w` has viewed.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

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

function feed(sink Sink) returns Count
	var b = Box.create(1)
	b.n = 2
	view w = b
	sink((b))
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:24:2: cannot pass the `var` 'b', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.error.a-viewed-sole-parameter-forwarded-to-a-sole-parameter-is-refused -->
`Taker.take` declares its parameter `sole`, so `Relay.take` owns `b`; once `w` has viewed it, `b` is no longer the record's only owner and cannot be handed on to another `sole` parameter.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Taker
	function take(b sole Box) returns Count
end 'Taker'

type Relay implements Taker
	let sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'

	function take(b Box) returns Count
		view w = b
		self.sink(b)
		return w.n
	end 'take'
end 'Relay'

var kept = Boxes.create()

function stash(b Box)
	kept.push(b)
end 'stash'

function main() returns ExitCode
	let relay = Relay.create(stash)
	print("{relay.take(Box.create(1))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:27:8: cannot pass the parameter 'b', which has been viewed by 'w', to parameter 'b' of 'sink', which consumes it; pass a clone()
```

<!-- test: capability-qualifiers.a-viewed-var-of-an-immutable-record-is-accepted-by-a-sole-parameter -->
`Box` has only `let` fields, so nothing can write its record: it is shared rather than owned, a `sole` parameter accepts it although `w` has viewed it, and `w` stays valid.

```maxon
typealias Count = int(i64.min to i64.max)
typealias Boxes = Array with Box
typealias Sink = function(b sole Box)

type Box
	export let n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

var kept = Boxes.create()

function stash(b Box)
	kept.push(b)
end 'stash'

function feed(sink Sink) returns Count
	var b = Box.create(1)
	b = Box.create(2)
	view w = b
	sink(b)
	return w.n
end 'feed'

function main() returns ExitCode
	let n = feed(stash)
	print("{n}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```
