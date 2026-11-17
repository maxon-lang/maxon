---
feature: regions-recover
status: experimental
keywords: [isolated, recover, fresh, payload, match, merge, window, hand-back, E3200]
category: semantics
---

# A fresh local or an owned payload goes where an `isolated` value is wanted

## Documentation

A local initialised from a fresh value (`let fresh = Box.create(3)`) goes into an `isolated` position the first time
the program reads it, with no `as isolated` on its declaration: an `isolated` parameter, an `isolated` slot, an
`isolated` declaration's initializer, `freeze` and `merge`. The read moves the local, so a later read is E3102.
A local read before that, a local an enclosing loop re-reads, and a local with a second name stay E3200.

A `match` over a linear union held in an `isolated` local, an `isolated` parameter or a behaviour's message parameter
consumes the union and hands its payloads over as fresh owned values. The lone plain payload of its case is recovered
the same way as a fresh local, and inside an open `modify` window it belongs to the window's region, so the arm may
write it or store it into the window's data.

## Tests

<!-- test: a-fresh-local-moves-into-an-isolated-parameter-on-its-first-read -->
`fresh` is read once, as the argument of an `isolated` parameter.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let fresh = Box.create(3)
	consume(fresh)
	return 0
end 'main'
```
```stdout
consumed 3
```
```exitcode
0
```

<!-- test: a-fresh-local-is-declared-isolated-from-its-first-read -->
`fresh` becomes the `isolated` local `kept`.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let fresh = Box.create(4)
	let kept = fresh as isolated Box
	consume(kept)
	return 0
end 'main'
```
```stdout
consumed 4
```
```exitcode
0
```

<!-- test: a-fresh-local-inside-an-isolated-initializer-moves-with-it -->
The union literal that holds `fresh` is the initializer of an `isolated` local, so `fresh` is read once, inside it.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	empty
	holding(box Box)
end 'Slot'

function drain(s isolated Slot)
	match s 'which'
		empty then print("empty\n")
		holding then print("holding\n")
	end 'which'
end 'drain'

function main() returns ExitCode
	let fresh = Box.create(5)
	let slot = Slot.holding(fresh) as isolated Slot
	drain(slot)
	return 0
end 'main'
```
```stdout
holding
```
```exitcode
0
```

<!-- test: a-fresh-local-is-frozen-and-merged-on-its-first-read -->
The first read of `fresh` is the operand of `freeze`.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let fresh = Box.create(6)
	let frozen = freeze fresh
	print("frozen {frozen.n}\n")
	return 0
end 'main'
```
```stdout
frozen 6
```
```exitcode
0
```

<!-- test: a-local-read-before-the-isolated-position-is-not-fresh -->
The `print` already read the local, so it is ordinary mutable data when `consume` wants an `isolated` value.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let fresh = Box.create(3)
	print("{fresh.n}\n")
	consume(fresh)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:19:10: 'fresh' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: a-fresh-local-is-moved-once-it-is-recovered -->
`consume(fresh)` moved `fresh`, so the next read is a use after the move.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let fresh = Box.create(3)
	consume(fresh)
	consume(fresh)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:19:10: use of moved value 'fresh': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-local-an-enclosing-loop-reads-again-is-not-fresh -->
The loop reads `fresh` on every lap, so the one textual read is not the first read of a value nobody else names.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let fresh = Box.create(3)
	var lap = 0 as Count

	while lap < 2 'laps'
		consume(fresh)
		lap = lap + 1
	end 'laps'

	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:21:11: 'fresh' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: a-local-with-a-second-name-is-not-fresh -->
`alias` is a second read of `fresh`, so the read that wants an `isolated` value is not the first.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let fresh = Box.create(3)
	let alias = fresh
	consume(fresh)
	print("{alias.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:19:10: 'fresh' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: a-local-each-match-arm-leaves-fresh-is-fresh-after-the-match -->
One arm gives `fresh` a new `Box` and the other leaves the one it was declared with; neither reads it, so it is fresh after the `match` and moves into the `isolated` parameter.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

enum Side
	left
	right
end 'Side'

function side() returns Side
	return Side.left
end 'side'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	var fresh = Box.create(1)

	match side() 'which'
		left then fresh = Box.create(2)
		right then print("right\n")
	end 'which'

	consume(fresh)
	return 0
end 'main'
```
```stdout
consumed 2
```
```exitcode
0
```

<!-- test: a-local-a-match-arm-reads-is-not-fresh-after-the-match -->
The `right` arm reads `fresh`, so after the `match` it may have been read before the `isolated` parameter reads it.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

enum Side
	left
	right
end 'Side'

function side() returns Side
	return Side.left
end 'side'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	var fresh = Box.create(1)

	match side() 'which'
		left then fresh = Box.create(2)
		right then print("right {fresh.n}\n")
	end 'which'

	consume(fresh)
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:33:10: 'fresh' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: a-local-assigned-a-try-of-a-fresh-call-with-a-fresh-fallback-is-fresh -->
`fresh` is moved, then given either `build`'s new `Box` or the fallback's, so it is fresh again and moves into the `isolated` parameter.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

enum BuildError implements Error
	refused
end 'BuildError'

function build(n Count) returns Box throws BuildError
	if n == 0 'refuse'
		throw BuildError.refused
	end 'refuse'

	return Box.create(n)
end 'build'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	var fresh = Box.create(1)
	consume(fresh)
	fresh = try build(0) otherwise Box.create(3)
	consume(fresh)
	return 0
end 'main'
```
```stdout
consumed 1
consumed 3
```
```exitcode
0
```

<!-- test: the-plain-payload-of-an-owned-union-goes-where-an-isolated-value-does -->
`u` is an `isolated` local holding a linear union, and the `match` consumes it. `y` is the lone plain payload of its case, so it is recovered into `consume`'s `isolated` parameter.
```maxon
typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function consume(b isolated Box)
	print("consumed {b.n}\n")
end 'consume'

function main() returns ExitCode
	let parcel = Parcel.box(Box.create(7)) as isolated Parcel

	match parcel 'unpack'
		pad(p) then p.fin()
		box(b) then consume(b)
	end 'unpack'

	return 0
end 'main'
```
```stdout
consumed 7
```
```exitcode
0
```

<!-- test: an-owned-payload-stored-into-an-open-window-merges-its-region -->
The `match` inside the `modify` window consumes the `isolated` message parameter `p`, and storing the payload `b` into the window's `label` merges the payload's region into the window's.
```maxon
typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Shelf
	var label as Box

	static function create(label Box) returns Self
		return Self{label: label}
	end 'create'
end 'Shelf'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function settle(p Parcel)
	match p 'which'
		pad(x) then x.fin()
		box then print("box\n")
	end 'which'
end 'settle'

type Bay
	var loads as Count

	static function create() returns Self
		return Self{loads: 0}
	end 'create'

	export function load(p isolated Parcel)
		self.loads = 1
		let shelf = Shelf.create(Box.create(0)) as isolated Shelf

		modify shelf 'open'
			match p 'unpack'
				pad(x) then x.fin()
				box(b) then shelf.label = b
			end 'unpack'

			print("label {shelf.label.n}\n")
		end 'open'
	end 'load'
end 'Bay'

function main() returns ExitCode
	let bay = spawn Bay.create()

	try bay.load(Parcel.box(Box.create(8))) otherwise (failure) 'refused'
		match failure 'why'
			mailboxFull(back) then settle(back)
			serviceStopped(back) then settle(back)
		end 'why'
	end 'refused'

	return 0
end 'main'
```
```stdout
label 8
```
```exitcode
0
```

<!-- test: the-plain-payload-of-an-owned-union-is-frozen-inside-a-window -->
`b` is consumed out of `parcel` inside the window, so freezing it is the first read of a fresh value.
```maxon
typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Shelf
	var label as Box

	static function create(label Box) returns Self
		return Self{label: label}
	end 'create'
end 'Shelf'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function main() returns ExitCode
	let shelf = Shelf.create(Box.create(0)) as isolated Shelf
	let parcel = Parcel.box(Box.create(9)) as isolated Parcel

	modify shelf 'open'
		match parcel 'unpack'
			pad(p) then p.fin()
			box(b) then let _ = freeze b
		end 'unpack'
	end 'open'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-behaviour-stores-the-plain-payload-of-its-message-in-an-isolated-slot -->
The behaviour owns its `isolated` message parameter `p`, matches it, and stores the payload in the `isolated` field `item`.
```maxon
typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Parcel
	pad(p Pad)
	box(b Box)
end 'Parcel'

function settle(p Parcel)
	match p 'which'
		pad(x) then x.fin()
		box then print("box\n")
	end 'which'
end 'settle'

type Shed
	var item as isolated Box

	static function create(item isolated Box) returns Self
		return Self{item: item}
	end 'create'

	export function store(p isolated Parcel)
		match p 'unpack'
			pad(x) then x.fin()
			box(b) then self.item = b
		end 'unpack'
	end 'store'
end 'Shed'

function main() returns ExitCode
	let shed = spawn Shed.create(Box.create(0))

	try shed.store(Parcel.box(Box.create(1))) otherwise (failure) 'refused'
		match failure 'why'
			mailboxFull(back) then settle(back)
			serviceStopped(back) then settle(back)
		end 'why'
	end 'refused'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-handed-back-linear-argument-goes-to-an-isolated-parameter -->
The failed send hands `back` over as an owned value, so the handler passes it to a function that takes an `isolated` union.
```maxon
typealias Count = int(0 to 1000)

type Pad
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'

	function fin(consuming self)
		print("fin {self.n}\n")
		destruct self
	end 'fin'
end 'Pad'

union Parcel
	pad(p Pad)
	none
end 'Parcel'

function settle(p isolated Parcel)
	match p 'which'
		pad(x) then x.fin()
		none then print("none\n")
	end 'which'
end 'settle'

type Counter
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(p Parcel)
		self.seen = 1

		match p 'which'
			pad(x) then x.fin()
			none then print("none\n")
		end 'which'
	end 'take'
end 'Counter'

function main() returns ExitCode
	let counter = spawn Counter.create()

	try counter.take(Parcel.pad(Pad.create(2))) otherwise (failure) 'refused'
		match failure 'why'
			mailboxFull(back) then settle(back)
			serviceStopped(back) then settle(back)
		end 'why'
	end 'refused'

	return 0
end 'main'
```
```stdout
fin 2
```
```exitcode
0
```

<!-- test: a-linear-union-with-a-plain-payload-does-not-cross-from-a-literal-over-a-live-local -->
`Mp.m1(b)` is no fresh value, because `b` is a local the sender writes after the send, and `Mp` is not closed: its payload `y` is a plain `Bx`.
```maxon
typealias Num = int(i64.min to i64.max)
type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Rl'
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'
union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

function drain(m Mp)
	match m 'drained'
		m0(x) then x.fin()
		m1(y) then print("drained {y.n}\n")
	end 'drained'
end 'drain'
type Taker
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	export function take(m Mp)
		match m 'taken'
			m0(x) then x.fin()
			m1(y) then y.n = 2
		end 'taken'
	end 'take'
end 'Taker'

function main() returns ExitCode
	let s = spawn Taker.create(0)
	let b = Bx.create(1)

	try s.take(Mp.m1(b)) otherwise (failure) 'refused'
		match failure 'why'
			mailboxFull(back) then drain(back)
			serviceStopped(back) then drain(back)
		end 'why'
	end 'refused'

	b.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:52:13: 'Mp.m1(b)' crosses to the service it is sent to only as the whole of its region, and 'Mp' is not closed: 'Mp.y' is a plain 'Bx' that other code may still name. Declare 'y' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-linear-union-local-with-a-plain-payload-does-not-cross -->
`m` is an owned linear value, but `Mp` holds the plain part `y`, which `b` still names, so `m` is not the whole of its region.
```maxon
typealias Num = int(i64.min to i64.max)
type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Rl'
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'
union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

function drain(m Mp)
	match m 'drained'
		m0(x) then x.fin()
		m1(y) then print("drained {y.n}\n")
	end 'drained'
end 'drain'
type Taker
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	export function take(m Mp)
		match m 'taken'
			m0(x) then x.fin()
			m1(y) then y.n = 2
		end 'taken'
	end 'take'
end 'Taker'

function main() returns ExitCode
	let s = spawn Taker.create(0)
	let b = Bx.create(1)
	let m = Mp.m1(b)

	try s.take(m) otherwise (failure) 'refused'
		match failure 'why'
			mailboxFull(back) then drain(back)
			serviceStopped(back) then drain(back)
		end 'why'
	end 'refused'

	b.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:53:13: 'm' crosses to the service it is sent to only as the whole of its region, and 'Mp' is not closed: 'Mp.y' is a plain 'Bx' that other code may still name. Declare 'y' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-unique-local-whose-type-holds-a-plain-part-does-not-cross -->
`Tp` is `unique` and holds the plain `Bx` field `b`; `h.k` keeps a second name for it, so `t` is not the whole of its region.
```maxon
typealias Num = int(i64.min to i64.max)
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'
type Tp is unique
	var b as Bx

	static function create(n Num) returns Self
		return Self{b: Bx.create(n)}
	end 'create'
end 'Tp'

type Holder
	var k as Bx

	static function create() returns Self
		return Self{k: Bx.create(0)}
	end 'create'
end 'Holder'

type TpTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(t Tp)
		self.seen = t.b.n
	end 'take'
end 'TpTaker'

function main() returns ExitCode
	let s = spawn TpTaker.create(0)
	let h = Holder.create()
	let t = Tp.create(1)
	h.k = t.b

	try s.take(t) otherwise ignore

	h.k.n = 4
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:44:13: 't' crosses to the service it is sent to only as the whole of its region, and 'Tp' is not closed: 'Tp.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-unique-parameter-whose-type-holds-a-plain-part-does-not-cross -->
A parameter is no fresh value, so `t` crosses only if `Tp` is closed, and it is not.
```maxon
typealias Num = int(i64.min to i64.max)
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'
type Tp is unique
	var b as Bx

	static function create(n Num) returns Self
		return Self{b: Bx.create(n)}
	end 'create'
end 'Tp'

type Holder
	var k as Bx

	static function create() returns Self
		return Self{k: Bx.create(0)}
	end 'create'
end 'Holder'

type TpTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(t Tp)
		self.seen = t.b.n
	end 'take'
end 'TpTaker'

function relay(s TpTaker.handle, t Tp)
	try s.take(t) otherwise ignore
end 'relay'

function main() returns ExitCode
	let s = spawn TpTaker.create(0)
	let h = Holder.create()
	let t = Tp.create(1)
	h.k = t.b
	relay(s, t: t)
	h.k.n = 4
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:39:13: 't' crosses to the service it is sent to only as the whole of its region, and 'Tp' is not closed: 'Tp.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-unique-message-parameter-whose-type-holds-a-plain-part-is-not-sent-on -->
The behaviour keeps `t.b` in its own state and then sends `t` on: the message parameter is owned, but `Tp` is not closed.
```maxon
typealias Num = int(i64.min to i64.max)
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'
type Tp is unique
	var b as Bx

	static function create(n Num) returns Self
		return Self{b: Bx.create(n)}
	end 'create'
end 'Tp'

type Holder
	var k as Bx

	static function create() returns Self
		return Self{k: Bx.create(0)}
	end 'create'
end 'Holder'

type TpTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(t Tp)
		self.seen = t.b.n
	end 'take'
end 'TpTaker'

type Forwarder
	var keep as Bx
	var out as TpTaker.handle

	static function create(out TpTaker.handle) returns Self
		return Self{keep: Bx.create(0), out: out}
	end 'create'

	export function put(t Tp)
		self.keep = t.b
		try self.out.take(t) otherwise ignore
		self.keep.n = 7
	end 'put'
end 'Forwarder'

function main() returns ExitCode
	let h = Holder.create()
	h.k.n = 1
	let s = spawn TpTaker.create(0)
	let f = spawn Forwarder.create(s)
	try f.put(Tp.create(1)) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:48:21: 't' crosses to the service it is sent to only as the whole of its region, and 'Tp' is not closed: 'Tp.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-unique-spawn-factory-argument-whose-type-holds-a-plain-part-does-not-cross -->
A factory argument crosses to the service's thread under the same rule as a message argument; a fresh `Tp` crosses.
```maxon
typealias Num = int(i64.min to i64.max)
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'
type Tp is unique
	var b as Bx

	static function create(n Num) returns Self
		return Self{b: Bx.create(n)}
	end 'create'
end 'Tp'

type Holder
	var k as Bx

	static function create() returns Self
		return Self{k: Bx.create(0)}
	end 'create'
end 'Holder'

type TpTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(t Tp)
		self.seen = t.b.n
	end 'take'
end 'TpTaker'

type Keeper
	var t as Tp

	static function create(t Tp) returns Self
		return Self{t: t}
	end 'create'

	export function poke()
		self.t.b.n = 3
	end 'poke'
end 'Keeper'

function main() returns ExitCode
	let h = Holder.create()
	let t = Tp.create(1)
	h.k = t.b
	let s = spawn Keeper.create(t)
	try s.poke() otherwise ignore
	h.k.n = 4
	let z = spawn TpTaker.create(0)
	try z.take(Tp.create(2)) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:54:30: 't' crosses to the service the `spawn` starts only as the whole of its region, and 'Tp' is not closed: 'Tp.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-unique-reply-whose-type-holds-a-plain-part-does-not-cross -->
The behaviour keeps `t.b` and replies with `t`, which is then not the whole of its region.
```maxon
typealias Num = int(i64.min to i64.max)
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'
type Tp is unique
	var b as Bx

	static function create(n Num) returns Self
		return Self{b: Bx.create(n)}
	end 'create'
end 'Tp'

type Holder
	var k as Bx

	static function create() returns Self
		return Self{k: Bx.create(0)}
	end 'create'
end 'Holder'

type TpTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(t Tp)
		self.seen = t.b.n
	end 'take'
end 'TpTaker'

type Maker
	var keep as Bx

	static function create() returns Self
		return Self{keep: Bx.create(0)}
	end 'create'

	export function make() returns Tp
		let t = Tp.create(1)
		self.keep = t.b
		return t
	end 'make'
end 'Maker'

function main() returns ExitCode
	let h = Holder.create()
	h.k.n = 1
	let s = spawn Maker.create()
	let t = try await s.make() otherwise return 1
	print("{t.b.n}\n")
	let z = spawn TpTaker.create(0)
	try z.take(Tp.create(2)) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:48:10: 't' crosses to the sender awaiting the reply only as the whole of its region, and 'Tp' is not closed: 'Tp.b' is a plain 'Bx' that other code may still name. Declare 'b' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-unique-type-in-a-cycle-through-a-plain-payload-does-not-cross -->
`U` and `Link` hold each other, and `Link` holds `U` as a plain payload, so `U` is not closed. A type cycle is also E4014.
```maxon
typealias Num = int(i64.min to i64.max)

type U is unique
	var n as Num
	var link as Link

	static function create(n Num) returns Self
		return Self{n: n, link: Link.stop}
	end 'create'
end 'U'

union Link
	stop
	next(u U)
end 'Link'

type UTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(u U)
		self.seen = u.n
	end 'take'
end 'UTaker'

function relay(s UTaker.handle, u U)
	try s.take(u) otherwise ignore
end 'relay'

function main() returns ExitCode
	let s = spawn UTaker.create(0)
	relay(s, u: U.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:4:6: type 'U' contains a reference cycle (via U → link: Link → next.u: U); recursive type references are not allowed
error E3200: <fragment>:31:13: 'u' crosses to the service it is sent to only as the whole of its region, and 'U' is not closed: 'U.link' is a plain 'Link' that other code may still name. Declare 'link' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-unique-type-in-a-cycle-through-a-plain-payload-does-not-cross-when-the-union-is-declared-first -->
The cycle above with `Link` declared first: the answer does not depend on the order the types are declared in.
```maxon
typealias Num = int(i64.min to i64.max)

union Link
	stop
	next(u U)
end 'Link'

type U is unique
	var n as Num
	var link as Link

	static function create(n Num) returns Self
		return Self{n: n, link: Link.stop}
	end 'create'
end 'U'

type UTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(u U)
		self.seen = u.n
	end 'take'
end 'UTaker'

function relay(s UTaker.handle, u U)
	try s.take(u) otherwise ignore
end 'relay'

function main() returns ExitCode
	let s = spawn UTaker.create(0)
	relay(s, u: U.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:4:7: type 'Link' contains a reference cycle (via Link → next.u: U → link: Link); recursive type references are not allowed
error E3200: <fragment>:31:13: 'u' crosses to the service it is sent to only as the whole of its region, and 'U' is not closed: 'U.link' is a plain 'Link' that other code may still name. Declare 'link' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-unique-type-in-a-cycle-through-an-isolated-payload-does-not-cross -->
`Link` holds `U` as an `isolated` payload. Where the walk meets `U` again it counts `U` as holding nothing, so `U.link` is a plain `Link` and `U` is not closed.
```maxon
typealias Num = int(i64.min to i64.max)

type U is unique
	var n as Num
	var link as Link

	static function create(n Num) returns Self
		return Self{n: n, link: Link.stop}
	end 'create'
end 'U'

union Link
	stop
	next(u isolated U)
end 'Link'

type UTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(u U)
		self.seen = u.n
	end 'take'
end 'UTaker'

function relay(s UTaker.handle, u U)
	try s.take(u) otherwise ignore
end 'relay'

function main() returns ExitCode
	let s = spawn UTaker.create(0)
	relay(s, u: U.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:4:6: type 'U' contains a reference cycle (via U → link: Link → next.u: U); recursive type references are not allowed
error E3200: <fragment>:31:13: 'u' crosses to the service it is sent to only as the whole of its region, and 'U' is not closed: 'U.link' is a plain 'Link' that other code may still name. Declare 'link' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-unique-type-in-a-cycle-through-an-isolated-payload-does-not-cross-when-the-union-is-declared-first -->
The cycle above with `Link` declared first.
```maxon
typealias Num = int(i64.min to i64.max)

union Link
	stop
	next(u isolated U)
end 'Link'

type U is unique
	var n as Num
	var link as Link

	static function create(n Num) returns Self
		return Self{n: n, link: Link.stop}
	end 'create'
end 'U'

type UTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(u U)
		self.seen = u.n
	end 'take'
end 'UTaker'

function relay(s UTaker.handle, u U)
	try s.take(u) otherwise ignore
end 'relay'

function main() returns ExitCode
	let s = spawn UTaker.create(0)
	relay(s, u: U.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:4:7: type 'Link' contains a reference cycle (via Link → next.u: U → link: Link); recursive type references are not allowed
error E3200: <fragment>:31:13: 'u' crosses to the service it is sent to only as the whole of its region, and 'U' is not closed: 'U.link' is a plain 'Link' that other code may still name. Declare 'link' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-unique-type-in-a-cycle-through-a-generic-instance-does-not-cross -->
`U` reaches `Link` through the generic instance `LinkArray`, and `Link` holds `U` as a plain payload, so `U` is not closed.
```maxon
typealias Num = int(i64.min to i64.max)

type U is unique
	var n as Num
	var links as LinkArray

	static function create(n Num) returns Self
		return Self{n: n, links: LinkArray.create(0)}
	end 'create'
end 'U'

typealias LinkArray = Array with Link

union Link
	stop
	next(u U)
end 'Link'

type UTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(u U)
		self.seen = u.n
	end 'take'
end 'UTaker'

function relay(s UTaker.handle, u U)
	try s.take(u) otherwise ignore
end 'relay'

function main() returns ExitCode
	let s = spawn UTaker.create(0)
	relay(s, u: U.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:4:6: type 'U' contains a reference cycle (via U → links: LinkArray → Link → next.u: U); recursive type references are not allowed
error E3200: <fragment>:33:13: 'u' crosses to the service it is sent to only as the whole of its region, and 'U' is not closed: 'U.links' is a plain 'LinkArray' that other code may still name. Declare 'links' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-unique-type-in-a-cycle-through-a-generic-instance-does-not-cross-when-the-union-is-declared-first -->
The cycle above with `Link` declared first.
```maxon
typealias Num = int(i64.min to i64.max)

union Link
	stop
	next(u U)
end 'Link'

type U is unique
	var n as Num
	var links as LinkArray

	static function create(n Num) returns Self
		return Self{n: n, links: LinkArray.create(0)}
	end 'create'
end 'U'

typealias LinkArray = Array with Link

type UTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(u U)
		self.seen = u.n
	end 'take'
end 'UTaker'

function relay(s UTaker.handle, u U)
	try s.take(u) otherwise ignore
end 'relay'

function main() returns ExitCode
	let s = spawn UTaker.create(0)
	relay(s, u: U.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:4:7: type 'Link' contains a reference cycle (via Link → next.u: U → links: LinkArray → Link); recursive type references are not allowed
error E3200: <fragment>:33:13: 'u' crosses to the service it is sent to only as the whole of its region, and 'U' is not closed: 'U.links' is a plain 'LinkArray' that other code may still name. Declare 'links' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: the-payload-of-a-message-parameter-whose-union-is-not-closed-is-not-isolated -->
`Parcel` holds the plain payload `b`, so the behaviour's unmarked message parameter `p` is owned plain data and its payload goes to no `isolated` slot.
```maxon
typealias Num = int(i64.min to i64.max)
type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Rl'
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

union Parcel
	pad(p Rl)
	box(b Bx)
end 'Parcel'

function settle(p Parcel)
	match p 'which'
		pad(x) then x.fin()
		box then print("box\n")
	end 'which'
end 'settle'

type Shed
	var item as isolated Bx

	static function create(item isolated Bx) returns Self
		return Self{item: item}
	end 'create'

	export function store(p Parcel)
		match p 'unpack'
			pad(x) then x.fin()
			box(b) then self.item = b
		end 'unpack'
	end 'store'
end 'Shed'

function main() returns ExitCode
	let shed = spawn Shed.create(Bx.create(0))

	try shed.store(Parcel.box(Bx.create(1))) otherwise (failure) 'refused'
		match failure 'why'
			mailboxFull(back) then settle(back)
			serviceStopped(back) then settle(back)
		end 'why'
	end 'refused'

	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:45:28: 'b' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Bx`, or store a copy
```

<!-- test: a-linear-local-of-a-closed-type-crosses-in-a-message -->
`Rl` holds only a value, so the owned linear local `r` is the whole of its region and crosses.
```maxon
typealias Num = int(i64.min to i64.max)
type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Rl'

type RlTaker
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	export function take(r Rl)
		r.fin()
	end 'take'
end 'RlTaker'

function main() returns ExitCode
	let s = spawn RlTaker.create(0)
	let r = Rl.create(1)
	print("peek {r.code}\n")

	try s.take(r) otherwise (failure) 'refused'
		match failure 'why'
			mailboxFull(back) then back.fin()
			serviceStopped(back) then back.fin()
		end 'why'
	end 'refused'

	return 0
end 'main'
```
```stdout
peek 1
fin 1
```
```exitcode
0
```

<!-- test: a-linear-union-whose-payloads-are-values-or-isolated-crosses-in-a-message -->
Every payload of `Mq` is linear and closed or declared `isolated`, so `Mq` is closed.
```maxon
typealias Num = int(i64.min to i64.max)
type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Rl'
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

union Mq
	q0(x Rl)
	q1(y isolated Bx)
end 'Mq'

function drainQ(m Mq)
	match m 'drained'
		q0(x) then x.fin()
		q1 then print("drained\n")
	end 'drained'
end 'drainQ'

type QTaker
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	export function take(m Mq)
		match m 'taken'
			q0(x) then x.fin()
			q1 then self.k = 1
		end 'taken'
	end 'take'
end 'QTaker'

function main() returns ExitCode
	let s = spawn QTaker.create(0)
	let m = Mq.q1(Bx.create(1))
	drainQ(Mq.q0(Rl.create(2)))

	try s.take(m) otherwise (failure) 'refused'
		match failure 'why'
			mailboxFull(back) then drainQ(back)
			serviceStopped(back) then drainQ(back)
		end 'why'
	end 'refused'

	return 0
end 'main'
```
```stdout
fin 2
```
```exitcode
0
```

<!-- test: a-linear-union-declared-isolated-crosses-though-its-type-is-not-closed -->
`m` is declared `isolated`, so it is the whole of its region whatever `Mp` holds.
```maxon
typealias Num = int(i64.min to i64.max)
type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Rl'
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'
union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

function drain(m Mp)
	match m 'drained'
		m0(x) then x.fin()
		m1(y) then print("drained {y.n}\n")
	end 'drained'
end 'drain'
type Taker
	var k as Num

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'

	export function take(m Mp)
		match m 'taken'
			m0(x) then x.fin()
			m1(y) then y.n = 2
		end 'taken'
	end 'take'
end 'Taker'

function main() returns ExitCode
	let s = spawn Taker.create(0)
	let m = Mp.m1(Bx.create(1)) as isolated Mp
	drain(Mp.m0(Rl.create(3)))

	try s.take(m) otherwise (failure) 'refused'
		match failure 'why'
			mailboxFull(back) then drain(back)
			serviceStopped(back) then drain(back)
		end 'why'
	end 'refused'

	return 0
end 'main'
```
```stdout
fin 3
```
```exitcode
0
```

<!-- test: a-fresh-unique-local-crosses-though-its-type-is-not-closed -->
`t` holds a fresh value nothing has read, so it is the whole of its region.
```maxon
typealias Num = int(i64.min to i64.max)
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'
type Tp is unique
	var b as Bx

	static function create(n Num) returns Self
		return Self{b: Bx.create(n)}
	end 'create'
end 'Tp'

type Holder
	var k as Bx

	static function create() returns Self
		return Self{k: Bx.create(0)}
	end 'create'
end 'Holder'

type TpTaker
	var seen as Num

	static function create(seen Num) returns Self
		return Self{seen: seen}
	end 'create'

	export function take(t Tp)
		self.seen = t.b.n
	end 'take'
end 'TpTaker'

function main() returns ExitCode
	let h = Holder.create()
	h.k.n = 1
	let s = spawn TpTaker.create(0)
	let t = Tp.create(1)

	try s.take(t) otherwise ignore

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-owned-linear-parameter-of-a-closed-type-goes-to-an-isolated-parameter -->
A linear parameter is owned by the function, and `Mq` is closed, so `m` goes where an `isolated` value goes.
```maxon
typealias Num = int(i64.min to i64.max)
type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Rl'
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

union Mq
	q0(x Rl)
	q1(y isolated Bx)
end 'Mq'

function takeIso(m isolated Mq)
	match m 'taken'
		q0(x) then x.fin()
		q1(y) then print("took {y.n}\n")
	end 'taken'
end 'takeIso'

function relay(m Mq)
	takeIso(m)
end 'relay'

function main() returns ExitCode
	relay(Mq.q0(Rl.create(1)))
	relay(Mq.q1(Bx.create(2)))
	return 0
end 'main'
```
```stdout
fin 1
took 2
```
```exitcode
0
```

<!-- test: an-owned-linear-parameter-of-a-type-that-is-not-closed-stays-plain -->
`Mp` holds the plain payload `y`, which the caller's `b` still names, so `m` is ordinary data of its region.
```maxon
typealias Num = int(i64.min to i64.max)
type Rl
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Rl'
type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'
union Mp
	m0(x Rl)
	m1(y Bx)
end 'Mp'

function drain(m Mp)
	match m 'drained'
		m0(x) then x.fin()
		m1(y) then print("drained {y.n}\n")
	end 'drained'
end 'drain'

function takeIso(m isolated Mp)
	drain(m)
end 'takeIso'

function relay(m Mp)
	takeIso(m)
end 'relay'

function main() returns ExitCode
	let b = Bx.create(1)
	relay(Mp.m1(b))
	b.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:39:10: 'm' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: a-consuming-receiver-of-a-closed-type-returns-as-isolated -->
`pass` owns its `consuming self`, and `Lc` holds only a value, so `self` fits `returns isolated Lc`.
```maxon
typealias Num = int(i64.min to i64.max)

type Lc
	var code as Num

	static function create(code Num) returns Self
		return Self{code: code}
	end 'create'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'

	function pass(consuming self) returns isolated Lc
		return self
	end 'pass'
end 'Lc'

function takeIso(l isolated Lc)
	l.fin()
end 'takeIso'

function main() returns ExitCode
	let l = Lc.create(4)
	takeIso(l.pass())
	return 0
end 'main'
```
```stdout
fin 4
```
```exitcode
0
```

<!-- test: an-outer-isolated-value-moved-into-a-local-inside-a-window-is-not-opened -->
`p` is declared inside 'mh', but its region is `g`'s, which is older than the window, so `modify p` opens an outer region.
```maxon
typealias Num = int(i64.min to i64.max)

union Iq6
	i0
	i1(w isolated Wo6)
end 'Iq6'

union Oq6
	o0
	o1(q isolated Iq6)
end 'Oq6'

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'

	function touch(k Num)
		self.keep.n = k
	end 'touch'
end 'Wo6'

function mk() returns isolated Wo6
	return Wo6.create(Bx6.create(0), item: Bx6.create(1))
end 'mk'

function pass(x isolated Wo6) returns isolated Wo6
	return x
end 'pass'

function openO(o isolated Wo6)
	modify o 'mo'
		o.keep.n = 3
	end 'mo'
end 'openO'

function scn()
	let h = mk()
	let g = mk()
	modify h 'mh'
		let p = g as isolated Wo6
		modify p 'mp'
			p.keep.n = 4
		end 'mp'
	end 'mh'
end 'scn'

function cover()
	let z = Oq6.o1(Iq6.i0) as isolated Oq6
	match z 'cz'
		o0 then break
		o1(q) then take(q)
	end 'cz'
	let w = pass(mk())
	openO(w)
	let t = Wo6.create(Bx6.create(0), item: Bx6.create(1))
	t.touch(1)
end 'cover'

function take(q isolated Iq6)
	let _ = q
end 'take'

function main() returns ExitCode
	scn()
	cover()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:54:10: 'p' cannot be opened: the value that holds it is read-only while 'mh' is open
```

<!-- test: the-payload-matched-out-of-an-outer-isolated-union-inside-a-window-is-not-opened -->
`o` is bound inside 'mh' by a consuming match of `u`, which is declared outside it, so a one-statement open of `o` opens an outer region.
```maxon
typealias Num = int(i64.min to i64.max)

union Iq6
	i0
	i1(w isolated Wo6)
end 'Iq6'

union Oq6
	o0
	o1(q isolated Iq6)
end 'Oq6'

type Bx6
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx6'

type Wo6
	var keep as Bx6
	var item as isolated Bx6

	static function create(keep Bx6, item isolated Bx6) returns Self
		return Self{keep: keep, item: item}
	end 'create'

	function touch(k Num)
		self.keep.n = k
	end 'touch'
end 'Wo6'

function mk() returns isolated Wo6
	return Wo6.create(Bx6.create(0), item: Bx6.create(1))
end 'mk'

function pass(x isolated Wo6) returns isolated Wo6
	return x
end 'pass'

function openO(o isolated Wo6)
	modify o 'mo'
		o.keep.n = 3
	end 'mo'
end 'openO'

function scn()
	let h = mk()
	let u = Iq6.i1(mk()) as isolated Iq6
	modify h 'mh'
		match u 'x1'
			i0 then break
			i1(o) then o.keep.n = 4
		end 'x1'
	end 'mh'
end 'scn'

function cover()
	let z = Oq6.o1(Iq6.i0) as isolated Oq6
	match z 'cz'
		o0 then break
		o1(q) then take(q)
	end 'cz'
	let w = pass(mk())
	openO(w)
	let t = Wo6.create(Bx6.create(0), item: Bx6.create(1))
	t.touch(1)
end 'cover'

function take(q isolated Iq6)
	let _ = q
end 'take'

function main() returns ExitCode
	scn()
	cover()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:55:15: 'o' cannot be opened: the value that holds it is read-only while 'mh' is open
```

<!-- test: an-outer-isolated-linear-value-renamed-inside-a-window-is-not-opened-for-a-statement -->
A linear `isolated` value keeps its birth through the rename, like any other `isolated` value.
```maxon
typealias Num = int(i64.min to i64.max)

union Iq7
	i0
	i1(w isolated Lw7)
end 'Iq7'

type Bx7
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx7'

type Lw7
	var keep as Bx7

	static function create(keep Bx7) returns Self
		return Self{keep: keep}
	end 'create'

	function touch(k Num)
		self.keep.n = k
	end 'touch'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Lw7'

function mk() returns isolated Lw7
	return Lw7.create(Bx7.create(0))
end 'mk'

function sink(o isolated Lw7)
	o.fin()
end 'sink'

function scn()
	let h = Bx7.create(0) as isolated Bx7
	let g = mk()
	modify h 'mh'
		let p = g
		p.keep.n = 4
		sink(p)
	end 'mh'
	let _ = h
end 'scn'

function cover()
	let z = Iq7.i1(mk()) as isolated Iq7
	match z 'cz'
		i0 then break
		i1(q) then sink(q)
	end 'cz'
end 'cover'

function main() returns ExitCode
	scn()
	cover()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:46:3: 'p' cannot be opened: the value that holds it is read-only while 'mh' is open
```

<!-- test: an-outer-isolated-linear-value-renamed-inside-a-window-is-not-modified -->
`modify p` inside 'mh' opens the region of `g`, which is older than the window.
```maxon
typealias Num = int(i64.min to i64.max)

union Iq7
	i0
	i1(w isolated Lw7)
end 'Iq7'

type Bx7
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx7'

type Lw7
	var keep as Bx7

	static function create(keep Bx7) returns Self
		return Self{keep: keep}
	end 'create'

	function touch(k Num)
		self.keep.n = k
	end 'touch'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Lw7'

function mk() returns isolated Lw7
	return Lw7.create(Bx7.create(0))
end 'mk'

function sink(o isolated Lw7)
	o.fin()
end 'sink'

function scn()
	let h = Bx7.create(0) as isolated Bx7
	let g = mk()
	modify h 'mh'
		let p = g
		modify p 'mp'
			p.keep.n = 4
		end 'mp'
		sink(p)
	end 'mh'
	let _ = h
end 'scn'

function cover()
	let z = Iq7.i1(mk()) as isolated Iq7
	match z 'cz'
		i0 then break
		i1(q) then sink(q)
	end 'cz'
end 'cover'

function main() returns ExitCode
	scn()
	cover()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:46:10: 'p' cannot be opened: the value that holds it is read-only while 'mh' is open
```

<!-- test: an-outer-isolated-linear-value-renamed-inside-a-window-is-not-opened-for-a-call -->
`touch` lends its receiver, so `p.touch(4)` opens `p` for the call.
```maxon
typealias Num = int(i64.min to i64.max)

union Iq7
	i0
	i1(w isolated Lw7)
end 'Iq7'

type Bx7
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx7'

type Lw7
	var keep as Bx7

	static function create(keep Bx7) returns Self
		return Self{keep: keep}
	end 'create'

	function touch(k Num)
		self.keep.n = k
	end 'touch'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Lw7'

function mk() returns isolated Lw7
	return Lw7.create(Bx7.create(0))
end 'mk'

function sink(o isolated Lw7)
	o.fin()
end 'sink'

function scn()
	let h = Bx7.create(0) as isolated Bx7
	let g = mk()
	modify h 'mh'
		let p = g
		p.touch(4)
		sink(p)
	end 'mh'
	let _ = h
end 'scn'

function cover()
	let z = Iq7.i1(mk()) as isolated Iq7
	match z 'cz'
		i0 then break
		i1(q) then sink(q)
	end 'cz'
end 'cover'

function main() returns ExitCode
	scn()
	cover()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:46:3: 'p' cannot be opened: the value that holds it is read-only while 'mh' is open
```

<!-- test: an-outer-isolated-linear-value-is-not-opened-inside-a-window -->
`g` is declared outside 'mh'.
```maxon
typealias Num = int(i64.min to i64.max)

union Iq7
	i0
	i1(w isolated Lw7)
end 'Iq7'

type Bx7
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx7'

type Lw7
	var keep as Bx7

	static function create(keep Bx7) returns Self
		return Self{keep: keep}
	end 'create'

	function touch(k Num)
		self.keep.n = k
	end 'touch'

	function fin(consuming self)
		destruct self
	end 'fin'
end 'Lw7'

function mk() returns isolated Lw7
	return Lw7.create(Bx7.create(0))
end 'mk'

function sink(o isolated Lw7)
	o.fin()
end 'sink'

function scn()
	let h = Bx7.create(0) as isolated Bx7
	let g = mk()
	modify h 'mh'
		g.keep.n = 4
	end 'mh'
	sink(g)
	let _ = h
end 'scn'

function cover()
	let z = Iq7.i1(mk()) as isolated Iq7
	match z 'cz'
		i0 then break
		i1(q) then sink(q)
	end 'cz'
end 'cover'

function main() returns ExitCode
	scn()
	cover()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:45:3: 'g' cannot be opened: the value that holds it is read-only while 'mh' is open
```

<!-- test: a-plain-part-read-out-of-a-lent-unique-parameter-is-not-a-keep -->
`stash` stores `t.b`, a part read out of `t`, and never `t` itself, so `q` is only lent to the call and stays the caller's to write.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Tq'

type Ko
	var keep as Bx

	static function create(keep Bx) returns Self
		return Self{keep: keep}
	end 'create'
end 'Ko'

function stash(t Tq) returns Ko
	let o = Ko.create(Bx.create(0))
	o.keep = t.b
	return o
end 'stash'

function main() returns ExitCode
	let q = Tq.create(Bx.create(2))
	let o = stash(q)
	q.b.n = 3
	print("{q.b.n} {o.keep.n}\n")
	return 0
end 'main'
```
```stdout
3 3
```
```exitcode
0
```

<!-- test: a-call-lent-a-unique-local-whose-type-holds-a-plain-part-is-not-a-clean-call -->
`Tq` is not closed, so the lent `q` is no clean input even though nothing read it before, and the result, which holds `q.b`, cannot become isolated.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Tq'

type Ko
	var keep as Bx

	static function create(keep Bx) returns Self
		return Self{keep: keep}
	end 'create'
end 'Ko'

type Taker
	static function create() returns Self
		return Self{}
	end 'create'

	export function takeK(k isolated Ko)
		k.keep.n = 7
	end 'takeK'
end 'Taker'

function stash(t Tq) returns Ko
	let o = Ko.create(Bx.create(0))
	o.keep = t.b
	return o
end 'stash'

function main() returns ExitCode
	let s = spawn Taker.create()
	let q = Tq.create(Bx.create(2))
	let k = stash(q) as isolated Ko
	try s.takeK(k) otherwise ignore
	q.b.n = 3
	print("{q.b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:47:10: 'stash(q) as isolated Ko' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: a-call-lent-a-unique-local-whose-type-holds-a-plain-part-is-not-sent -->
The same call handed straight to a message's `isolated` parameter is refused for the same reason.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Tq'

type Ko
	var keep as Bx

	static function create(keep Bx) returns Self
		return Self{keep: keep}
	end 'create'
end 'Ko'

type Taker
	static function create() returns Self
		return Self{}
	end 'create'

	export function takeK(k isolated Ko)
		k.keep.n = 7
	end 'takeK'
end 'Taker'

function stash(t Tq) returns Ko
	let o = Ko.create(Bx.create(0))
	o.keep = t.b
	return o
end 'stash'

function main() returns ExitCode
	let s = spawn Taker.create()
	let q = Tq.create(Bx.create(2))
	try s.takeK(stash(q)) otherwise ignore
	q.b.n = 3
	print("{q.b.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:47:14: 'stash(q)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: a-call-lent-a-unique-local-of-a-closed-type-is-a-clean-call -->
`Tc` is closed, so lending `q` keeps `make(q)` a clean call: its result becomes isolated and `q` stays the caller's.
```maxon
typealias Num = int(i64.min to i64.max)

type Tc is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tc'

type Ko
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Ko'

function make(t Tc) returns Ko
	return Ko.create(t.n + 1)
end 'make'

function main() returns ExitCode
	let q = Tc.create(2)
	print("{q.n}\n")
	let k = make(q) as isolated Ko

	modify k 'k'
		k.n = k.n + q.n
	end 'k'

	q.n = 5
	print("{q.n}\n")
	return 0
end 'main'
```
```stdout
2
5
```
```exitcode
0
```

<!-- test: a-call-that-keeps-a-unique-local-whose-type-holds-a-plain-part-is-not-a-clean-call -->
`wrap` keeps `q`, whose `Tq` holds the plain `shared`, so the result is not a whole region.
```maxon
typealias Num = int(i64.min to i64.max)

type Bx
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Bx'

type Tq is unique
	var b as Bx

	static function create(b Bx) returns Self
		return Self{b: b}
	end 'create'
end 'Tq'

type Wrap is unique
	var t as Tq

	static function create(t Tq) returns Self
		return Self{t: t}
	end 'create'
end 'Wrap'

function wrap(t Tq) returns Wrap
	return Wrap.create(t)
end 'wrap'

function main() returns ExitCode
	let shared = Bx.create(2)
	let q = Tq.create(shared)
	print("{q.b.n}\n")
	let w = wrap(q) as isolated Wrap

	modify w 'w'
		w.t.b.n = 7
	end 'w'

	print("{shared.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:36:10: 'wrap(q) as isolated Wrap' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: a-field-write-of-a-value-keeps-a-local-fresh-for-a-send -->
Writing a number into `b` takes no reference out of it, so the send still finds `b` fresh.
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Keeper
	var total as Count

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function take(b isolated Box) returns Count
		self.total = self.total + b.n
		return self.total
	end 'take'
end 'Keeper'

function main() returns ExitCode
	let k = spawn Keeper.create()
	let b = Box.create(1)
	b.n = 4
	let got = try await k.take(b) otherwise 0
	print("{got}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```
