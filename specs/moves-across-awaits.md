---
feature: moves-across-awaits
status: experimental
keywords: [await, async, unique, move, immutable, borrowed result, send error, map key, set element, E3102, E3197]
category: semantics
---

# Moves, awaits and borrowed results

## Documentation

An awaited `async` call keeps the capability its function declares for the result: awaiting a call to a function
that returns `immutable T` yields frozen data, and a write through it is E3197. A `unique` value read out of a
`match` payload and handed to a parameter the callee keeps leaves its union unreadable (E3102). A `let` bound to a
call result that is borrowed from a parameter is judged exactly as a `var` bound to it would be. A `Map` key and a
`Set` element are `immutable` slots without a spelling. A send error rethrown from a function that only borrowed it
is counted, so it is dropped once.

## Tests

<!-- test: an-awaited-async-result-keeps-the-immutable-return -->
`fetch` returns `immutable Box`, so the awaited result is frozen and a write through it is refused.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function fetch(frozen immutable Box) returns immutable Box
	return frozen
end 'fetch'

function main() returns ExitCode
	let fr = freeze Box.create(1)
	let b = await async fetch(fr)
	b.n = 9
	print("{b.n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:19:2: cannot write 'n' through 'b', which is immutable
```

<!-- test: an-awaited-async-result-of-an-immutable-return-is-read -->
Reading the frozen result is fine.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function fetch(frozen immutable Box) returns immutable Box
	return frozen
end 'fetch'

function main() returns ExitCode
	let fr = freeze Box.create(1)
	let b = await async fetch(fr)
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

<!-- test: a-unique-payload-handed-to-a-consuming-constructor-ends-its-union -->
`Box.wrap` stores the `token` payload, which moves it out of the `var` union `slot`, so `tally(slot)` reads a union whose payload has left.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token, n Tally)
end 'Slot'

type Box
	export var token as Token

	static function wrap(token Token) returns Self
		return Self{token: token}
	end 'wrap'

	function size() returns Tally
		return self.token.n
	end 'size'
end 'Box'

function tally(slot Slot) returns Tally
	match slot 'which'
		empty then return 0
		filled(token, n) then return n + token.n
	end 'which'
end 'tally'

function main() returns ExitCode
	var slot = Slot.empty
	slot = Slot.filled(Token.create(1), n: 2)

	match slot 'which'
		empty then print("empty\n")
		filled(token, n) then print("{Box.wrap(token).size()} {n} {tally(slot)}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:42:68: use of moved value 'slot': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-let-bound-to-a-borrowed-call-result-is-written-like-a-var -->
`memo` hands back a field of its parameter; the `let` takes the record and writes through it, and the parameter stays writable.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Index
	export var items as TallyArray

	static function create() returns Self
		return Self{items: TallyArray.create(0)}
	end 'create'
end 'Index'

type Db
	export var cache as Index
	export var hits as Tally

	static function create() returns Self
		return Self{cache: Index.create(), hits: 0}
	end 'create'
end 'Db'

function memo(db Db) returns Index
	db.hits = db.hits + 1

	return db.cache
end 'memo'

function fill(db Db) returns Tally
	let index = memo(db)
	index.items.push(7)
	index.items.push(8)

	return index.items.count()
end 'fill'

function main() returns ExitCode
	let db = Db.create()
	print("{fill(db)} {db.cache.items.count()} {db.hits}\n")

	return 0
end 'main'
```
```stdout
2 2 1
```
```exitcode
0
```

<!-- test: a-borrowed-send-error-is-rethrown-without-a-double-drop -->
`again` only borrows the send error it throws back; the catcher and the original owner each drop their own reference once.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var kept as Tally

	static function create() returns Self
		return Self{kept: 0}
	end 'create'

	export function keep(box isolated Box)
		let n = box.n
		self.kept = self.kept + n
	end 'keep'
end 'Store'

function again(e Store.keep.sendError) throws Store.keep.sendError
	throw e
end 'again'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	let first = Box.create(1) as isolated Box
	let second = Box.create(2) as isolated Box
	try h.keep(first) otherwise panic("a capacity of one takes the first send")

	try h.keep(second) otherwise (e) 'refused'
		try again(e) otherwise ignore
	end 'refused'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-set-element-and-a-map-key-are-immutable-slots -->
A frozen name is stored as a `Set` element and as a `Map` key with no `immutable` spelled on the alias.

```maxon
typealias Names = Set with ByteArray
typealias Index = Map with (ByteArray, ByteArray)

type Tok
	export let value as immutable ByteArray

	static function create(value immutable ByteArray) returns Self
		return Self{value: value}
	end 'create'
end 'Tok'

function record(names Names, tok Tok)
	names.insert(tok.value)
end 'record'

function index(into Index, tok Tok)
	into.upsert(tok.value, value: tok.value.clone())
end 'index'

function main() returns ExitCode
	let names = Names.create()
	let tok = Tok.create(freeze "abc".toByteArray())
	record(names, tok: tok)
	let into = Index.create()
	index(into, tok: tok)
	print("{names.count()} {into.count()}\n")

	return 0
end 'main'
```
```stdout
1 1
```
```exitcode
0
```

<!-- test: a-number-read-from-a-written-parameter-may-key-a-map -->
The key is a copy of the number, so reaching it through `rec` does not make `rec` frozen.

```maxon
typealias Count64 = int(0 to u64.max)
typealias Seen = Map with (Count64, Count64)

type Rec
	export var id as Count64

	static function create() returns Self
		return Self{id: 1}
	end 'create'
end 'Rec'

function note(seen Seen, rec Rec)
	seen.upsert(rec.id, value: 1)
end 'note'

function work(seen Seen, rec Rec) returns Count64
	rec.id = rec.id + 1
	note(seen, rec: rec)

	return seen.count() as Count64
end 'work'

function main() returns ExitCode
	let seen = Seen.create()
	let rec = Rec.create()
	print("{work(seen, rec: rec)}\n")

	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: a-field-frozen-by-its-own-slot-reaches-a-frozen-parameter-while-its-holder-is-written -->
`name` is an `immutable` field, so handing `h.corpus.name` to `takes` needs nothing of `h`, which `bump` writes.

```maxon
typealias Count64 = int(0 to u64.max)

type RecordName
	export let name as immutable ByteArray

	static function of(name immutable ByteArray) returns RecordName
		return Self{name: name}
	end 'of'
end 'RecordName'

function takes(name immutable ByteArray) returns Count64
	return name.count() as Count64
end 'takes'

type Holder
	export var corpus as RecordName
	var hits as Count64

	static function create() returns Self
		return Self{corpus: RecordName.of(freeze "Array".toByteArray()), hits: 0}
	end 'create'

	function bump()
		self.hits = self.hits + 1
	end 'bump'

	function direct() returns Count64
		return takes(self.corpus.name)
	end 'direct'
end 'Holder'

function viaParam(r RecordName, h Holder) returns Count64
	h.bump()

	return takes(r.name)
end 'viaParam'

function viaHolder(h Holder) returns Count64
	h.bump()

	return h.direct()
end 'viaHolder'

function viaHolderField(h Holder) returns Count64
	h.bump()

	return takes(h.corpus.name)
end 'viaHolderField'

function main() returns ExitCode
	let h = Holder.create()
	print("{viaParam(h.corpus, h: h)} {viaHolder(h)} {viaHolderField(h)}\n")

	return 0
end 'main'
```
```stdout
5 5 5
```
```exitcode
0
```

<!-- test: a-literal-construction-is-not-a-call-in-a-module-let -->
`Names from [...]` and `[...]` build data, not a call, so a module `let` may hold them.

```maxon
typealias Names = Set with String

let known = Names from ["alpha", "beta"]
let listed = [1, 2, 3]

function main() returns ExitCode
	print("{known.count()} {listed.count()}\n")

	return 0
end 'main'
```
```stdout
2 3
```
```exitcode
0
```
