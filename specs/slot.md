---
feature: slot
status: experimental
keywords: [slot, optional, holder, fill, take, held, unique, isolated, generic]
category: collections
---

# `Slot with T` holds at most one value

## Documentation

`Slot with T` is a named place that holds one `T` or none. `Slot.create(name)` makes an empty slot; `fill(item)` puts a value in,
`held()` reads the value, `take()` moves it out, `replace(item)` fills it whether or not it was full, and `clear()` empties it.
`isEmpty()` and `isFull()` answer which. `fill` on a full slot and `held` or `take` on an empty one stop the program with a
panic that names the slot. `T` is any type, a `unique` one included: `fill` and `take` move it, and `held` lends it: a unique value is read through the call, and binding it is E3214.

## Tests

<!-- test: a-slot-is-filled-read-and-taken -->
A slot starts empty, `fill` fills it, `held` reads without emptying, and `take` moves the value out and leaves it empty.
```maxon
typealias Tally = int(0 to 1024)

type Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

typealias JobSlot = Slot with Job

function main() returns ExitCode
	let slot = JobSlot.create("jobs")
	print("{slot.isEmpty()} {slot.isFull()}\n")
	slot.fill(Job.create(3))
	print("{slot.isEmpty()} {slot.isFull()} {slot.held().n}\n")
	let job = slot.take()
	print("{job.n} {slot.isEmpty()}\n")
	return 0
end 'main'
```
```stdout
true false
false true 3
3 true
```
```exitcode
0
```

<!-- test: replace-and-clear-empty-or-refill-a-slot -->
`replace` fills a full slot with the new value and fills an empty one, and `clear` empties a slot that is full or already empty.
```maxon
typealias Tally = int(0 to 1024)

typealias TallySlot = Slot with Tally

function main() returns ExitCode
	let slot = TallySlot.create("tally")
	slot.replace(1)
	slot.replace(2)
	print("{slot.held()}\n")
	slot.clear()
	slot.clear()
	print("{slot.isEmpty()}\n")
	return 0
end 'main'
```
```stdout
2
true
```
```exitcode
0
```

<!-- test: a-slot-holds-a-unique-value-and-moves-it-out -->
A `unique` value is moved into the slot by `fill` and out of it by `take`.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenSlot = Slot with Token

function main() returns ExitCode
	let slot = TokenSlot.create("tokens")
	slot.fill(Token.create(5))
	print("{slot.held().n}\n")
	let token = slot.take()
	print("{token.n} {slot.isEmpty()}\n")
	return 0
end 'main'
```
```stdout
5
5 true
```
```exitcode
0
```

<!-- test: a-slot-is-a-field-of-an-isolated-record -->
A slot is an ordinary record, so it lives in an `isolated` region and is filled and emptied in a `modify` block.
```maxon
typealias Tally = int(0 to 1024)

typealias TallySlot = Slot with Tally

type Mailbox
	export let letter as TallySlot

	static function create() returns Self
		return Self{letter: TallySlot.create("Mailbox.letter")}
	end 'create'
end 'Mailbox'

function main() returns ExitCode
	let box = Mailbox.create() as isolated Mailbox

	modify box 'fill'
		box.letter.fill(9)
	end 'fill'

	let moved = merge box
	print("{moved.letter.take()} {moved.letter.isEmpty()}\n")
	return 0
end 'main'
```
```stdout
9 true
```
```exitcode
0
```

<!-- test: filling-a-full-slot-panics-naming-the-slot -->
`fill` does not replace: a second `fill` stops the program with a panic that names the slot.
```maxon
typealias Tally = int(0 to 1024)

typealias TallySlot = Slot with Tally

function main() returns ExitCode
	let slot = TallySlot.create("totals")
	slot.fill(1)
	slot.fill(2)
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at Slot.maxon:28: Slot 'totals' is already full
Stack trace:
  in Slot.fill
  in main
  in mrt_start
```

<!-- test: taking-from-an-empty-slot-panics-naming-the-slot -->
`take` on an empty slot stops the program with a panic that names the slot.
```maxon
typealias Tally = int(0 to 1024)

typealias TallySlot = Slot with Tally

function main() returns ExitCode
	let slot = TallySlot.create("totals")
	print("{slot.take()}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at Slot.maxon:52: Slot 'totals' is empty
Stack trace:
  in Slot.take
  in main
  in mrt_start
```

<!-- test: reading-an-empty-slot-panics-naming-the-slot -->
`held` on an empty slot stops the program with a panic that names the slot.
```maxon
typealias Tally = int(0 to 1024)

typealias TallySlot = Slot with Tally

function main() returns ExitCode
	let slot = TallySlot.create("totals")
	print("{slot.held()}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at Slot.maxon:47: Slot 'totals' is empty
Stack trace:
  in Slot.held
  in main
  in mrt_start
```

<!-- test: a-container-lends-a-unique-element-it-keeps-and-the-element-is-never-bound -->
`Slot.held`, `List.first` and `Map.get` hand back an element the container goes on owning, so a unique one is read through the call and never bound. `take` and `removeFirst` move the element out and the caller owns it.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenSlot = Slot with Token
typealias TokenList = List with Token
typealias TokenMap = Map with (Tally, Token)

function fromSlot(slot TokenSlot) returns Tally
	let held = slot.held()
	return held.n
end 'fromSlot'

function fromList(list TokenList) returns Tally
	let head = try list.first() otherwise panic("empty")
	return head.n
end 'fromList'

function fromMap(map TokenMap) returns Tally
	let found = try map.get(1) otherwise panic("absent")
	return found.n
end 'fromMap'

function main() returns ExitCode
	let slot = TokenSlot.create("tokens")
	slot.fill(Token.create(5))
	let list = TokenList.create()
	list.append(Token.create(6))
	let map = TokenMap.create()
	map.upsert(1, value: Token.create(7))
	print("{slot.held().n} {fromSlot(slot)} {fromList(list)} {fromMap(map)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:17:6: cannot bind 'held' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
error E3214: <fragment>:22:6: cannot bind 'head' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
error E3214: <fragment>:27:6: cannot bind 'found' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: a-unique-element-read-through-a-container-call-and-a-moved-out-one-both-run -->
`held().n` reads the slot's token in place, and `take` and `removeFirst` hand the caller the token itself, so it is bound and the container is empty afterwards.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenSlot = Slot with Token
typealias TokenList = List with Token

function main() returns ExitCode
	let slot = TokenSlot.create("tokens")
	slot.fill(Token.create(5))
	print("{slot.held().n}\n")
	let taken = slot.take()
	print("{taken.n} {slot.isEmpty()}\n")
	let list = TokenList.create()
	list.append(Token.create(6))
	let head = try list.removeFirst() otherwise panic("empty")
	print("{head.n}\n")
	return 0
end 'main'
```
```stdout
5
5 true
6
```
```exitcode
0
```
