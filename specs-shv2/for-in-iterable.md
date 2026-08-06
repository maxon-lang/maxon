---
feature: for-in-iterable
status: experimental
keywords: [for, iteration, Iterable, createIterator, cursor, IterationError, empty, extension]
category: control-flow
---

## Documentation

# `for … in` over an `Iterable`

`stdlib/Interfaces.maxon` states the traversal protocol in two halves. **`Iterator`** is
`current()` + a throwing `advance()`, and shv2 has walked that directly since conditional extensions.
**`Iterable`** is `createIterator()`, *"used by for-in loops"* — a factory the loop calls ONCE, in the
preheader, to obtain the cursor it then walks. This file is the second half.

The source is replaced by its cursor and the loop is the cursor loop, unchanged
(`Parser.materializeIterableIterationSource`, the `Map`-becomes-its-entries rewrite one interface over).
Two rules decide what happens:

- **A source that already IS a cursor is left alone.** An iterator may itself declare `createIterator`
  (the identity passthrough both reference compilers implement), and `for x in iter` over a
  half-consumed iterator must resume from where it stands rather than restarting. So the cursor
  protocol is tested first and wins.
- **A THROWING `createIterator()` reports EMPTINESS by throwing**, which is exactly what the protocol
  says (*"the constructor throws `IterationError.exhausted` on empty"*). That path ran no trips and owns
  no cursor, so it leaves the loop by an entry edge of its own that rejoins PAST the loop's exit drops.

⚠ **Seeding the loop's error flag with the factory's is not enough, and the difference is a SIGSEGV.**
The first cut did exactly that: the header's `flag == 0` test then correctly ran zero trips, and the
exit block went on to call `__destruct_<Cursor>` on the throw path's leftover return register. Control
flow and OWNERSHIP are two answers, and shv2's ownership is static — one owner, dropped at one point,
with no conditional-drop form — so the path that owns nothing may not reach the point that drops.

`createIterator()` may also be INFALLIBLE, in which case the loop cannot learn the collection is empty
and the protocol's positioning invariant is the conformer's to keep.

A factory throwing anything but `IterationError` is refused: the loop absorbs what it throws without
binding it, which is sound for a payload-free enum and would leak for one carrying a managed payload —
the same rule `advance()`'s clause is pinned by.

## Tests

<!-- test: for-in-iterable.walks-the-cursor-the-factory-returns -->
The factory is called once and the loop walks what it returns. `IntSeq` declares no `current`/`advance`
of its own — only `createIterator` — so nothing but the `Iterable` half is available here.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type IntCursor
	var items as IntArray
	var pos as Integer

	export static function create(items IntArray) returns Self throws IterationError
		if items.count() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{items: items, pos: 0}
	end 'create'

	export function current() returns Integer
		return try self.items.get(self.pos) otherwise panic("oob")
	end 'current'

	export function advance() throws IterationError
		if self.pos + 1 >= self.items.count() 'atEnd'
			throw IterationError.exhausted
		end 'atEnd'
		self.pos = self.pos + 1
	end 'advance'
end 'IntCursor'

type IntSeq
	var items as IntArray

	export static function create(items IntArray) returns Self
		return Self{items: items}
	end 'create'

	export function createIterator() returns IntCursor throws IterationError
		return try IntCursor.create(self.items)
	end 'createIterator'
end 'IntSeq'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(10)
	a.push(20)
	a.push(30)
	let s = IntSeq.create(a)
	var sum = 0
	for v in s 'each'
		sum = sum + v
	end 'each'
	return sum
end 'main'
```
```exitcode
60
```

<!-- test: for-in-iterable.an-empty-collection-runs-zero-trips-and-drops-nothing -->
⭐ **THE CASE THE SEEDED-FLAG LOWERING SEGFAULTED ON.** The factory throws `exhausted`, the loop runs
zero trips, and the cursor it did NOT return must not be destructed. Exit 0 with no leak is the whole
assertion; the carried `sum` reading its pre-loop value at the join is the other half.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type IntCursor
	var items as IntArray
	var pos as Integer

	export static function create(items IntArray) returns Self throws IterationError
		if items.count() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{items: items, pos: 0}
	end 'create'

	export function current() returns Integer
		return try self.items.get(self.pos) otherwise panic("oob")
	end 'current'

	export function advance() throws IterationError
		if self.pos + 1 >= self.items.count() 'atEnd'
			throw IterationError.exhausted
		end 'atEnd'
		self.pos = self.pos + 1
	end 'advance'
end 'IntCursor'

type IntSeq
	var items as IntArray

	export static function create(items IntArray) returns Self
		return Self{items: items}
	end 'create'

	export function createIterator() returns IntCursor throws IterationError
		return try IntCursor.create(self.items)
	end 'createIterator'
end 'IntSeq'

function total(s IntSeq) returns Integer
	var sum = 7
	for v in s 'each'
		sum = sum + v
	end 'each'
	return sum
end 'total'

function main() returns ExitCode
	let empty = IntSeq.create(IntArray.create())
	print("empty={total(empty)}\n")

	var a = IntArray.create()
	a.push(1)
	a.push(2)
	print("filled={total(IntSeq.create(a))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
empty=7
filled=10
```

<!-- test: for-in-iterable.break-and-return-leave-an-iterable-loop -->
Every exit a cursor loop already had works over an `Iterable` source, on the empty collection too — the
join the empty entry edge forces must merge the carried bindings, not bypass them.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type IntCursor
	var items as IntArray
	var pos as Integer

	export static function create(items IntArray) returns Self throws IterationError
		if items.count() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{items: items, pos: 0}
	end 'create'

	export function current() returns Integer
		return try self.items.get(self.pos) otherwise panic("oob")
	end 'current'

	export function advance() throws IterationError
		if self.pos + 1 >= self.items.count() 'atEnd'
			throw IterationError.exhausted
		end 'atEnd'
		self.pos = self.pos + 1
	end 'advance'
end 'IntCursor'

type IntSeq
	var items as IntArray

	export static function create(items IntArray) returns Self
		return Self{items: items}
	end 'create'

	export function createIterator() returns IntCursor throws IterationError
		return try IntCursor.create(self.items)
	end 'createIterator'
end 'IntSeq'

function firstOver(s IntSeq, floor Integer) returns Integer
	var found = 0
	for v in s 'each'
		if v > floor 'hit'
			found = v
			break
		end 'hit'
	end 'each'
	return found
end 'firstOver'

function earlyReturn(s IntSeq) returns Integer
	for v in s 'each'
		return v
	end 'each'
	return 0
end 'earlyReturn'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(10)
	a.push(20)
	a.push(30)
	let filled = IntSeq.create(a)
	let empty = IntSeq.create(IntArray.create())
	print("break={firstOver(filled, floor: 15)} breakEmpty={firstOver(empty, floor: 15)}\n")
	print("return={earlyReturn(filled)} returnEmpty={earlyReturn(empty)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
break=20 breakEmpty=0
return=10 returnEmpty=0
```

<!-- test: for-in-iterable.an-infallible-factory-needs-no-entry-edge -->
A `createIterator()` that cannot throw seeds the header at the first element exactly as a bare cursor
source does, and the loop grows no second entry edge.
```maxon
typealias Integer = int(i64.min to i64.max)

type Countdown
	var n as Integer

	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function current() returns Integer
		return self.n
	end 'current'

	export function advance() throws IterationError
		if self.n <= 1 'done'
			throw IterationError.exhausted
		end 'done'
		self.n = self.n - 1
	end 'advance'
end 'Countdown'

type Countable
	var start as Integer

	export static function create(start Integer) returns Self
		return Self{start: start}
	end 'create'

	export function createIterator() returns Countdown
		return Countdown.create(self.start)
	end 'createIterator'
end 'Countable'

function main() returns ExitCode
	let c = Countable.create(4)
	var sum = 0
	for v in c 'each'
		sum = sum + v
	end 'each'
	return sum
end 'main'
```
```exitcode
10
```

<!-- test: for-in-iterable.a-cursor-that-is-also-a-factory-is-walked-as-a-cursor -->
⭐ The precedence rule. `Resumable` supplies BOTH halves; iterating it must resume from where it stands
rather than calling its own factory and restarting. `main` advances twice before the loop, so a restart
would sum 100 and resuming sums 70.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Resumable
	var items as IntArray
	var pos as Integer

	export static function create(items IntArray) returns Self
		return Self{items: items, pos: 0}
	end 'create'

	export function current() returns Integer
		return try self.items.get(self.pos) otherwise panic("oob")
	end 'current'

	export function advance() throws IterationError
		if self.pos + 1 >= self.items.count() 'atEnd'
			throw IterationError.exhausted
		end 'atEnd'
		self.pos = self.pos + 1
	end 'advance'

	export function createIterator() returns Resumable
		return Resumable.create(self.items)
	end 'createIterator'
end 'Resumable'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(10)
	a.push(20)
	a.push(30)
	a.push(40)
	var it = Resumable.create(a)
	try it.advance() otherwise panic("advance")
	try it.advance() otherwise panic("advance")
	var sum = 0
	for v in it 'each'
		sum = sum + v
	end 'each'
	return sum
end 'main'
```
```exitcode
70
```

<!-- test: error.a-factory-throwing-another-error-is-refused -->
The loop absorbs what the traversal protocol throws without binding it, so the error type is pinned to
the payload-free `IterationError` — `advance()`'s rule, applied to the factory.
```maxon
typealias Integer = int(i64.min to i64.max)

enum BuildError implements Error
	broken
end 'BuildError'

type Countdown
	var n as Integer

	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function current() returns Integer
		return self.n
	end 'current'

	export function advance() throws IterationError
		if self.n <= 1 'done'
			throw IterationError.exhausted
		end 'done'
		self.n = self.n - 1
	end 'advance'
end 'Countdown'

type Countable
	var start as Integer

	export static function create(start Integer) returns Self
		return Self{start: start}
	end 'create'

	export function createIterator() returns Countdown throws BuildError
		if self.start <= 0 'bad'
			throw BuildError.broken
		end 'bad'
		return Countdown.create(self.start)
	end 'createIterator'
end 'Countable'

function main() returns ExitCode
	let c = Countable.create(4)
	var sum = 0
	for v in c 'each'
		sum = sum + v
	end 'each'
	return sum
end 'main'
```
```maxoncstderr
error E2015: <fragment>:45:2: Unsupported: `for … in` over a `Countable`, whose `createIterator()` throws `BuildError` — the loop absorbs what the traversal protocol throws without binding it, which is sound only for the payload-free `IterationError`; a factory throwing anything else must be called with an explicit `try` and its result iterated
```
