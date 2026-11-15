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
`fresh.n = 1` already read the local, so it is ordinary mutable data when `consume` wants an `isolated` value.
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
	fresh.n = 1
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
The `match` inside the `modify` window consumes `parcel`, and storing the payload `b` into the window's `label` merges the payload's region into the window's.
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

	export function load(p Parcel)
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
The behaviour owns its message parameter `p`, matches it, and stores the payload in the `isolated` field `item`.
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

	export function store(p Parcel)
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
