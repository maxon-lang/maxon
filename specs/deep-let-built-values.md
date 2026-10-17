---
feature: deep-let-built-values
status: experimental
keywords: [let, read-only, field, record, String, var, fresh value]
category: type-system
---

# A value built from what a `let` field holds is a new value

## Documentation

A `let` field is read-only, and so is everything reachable through it. A record or a `String` that a function
builds from the text it reads out of a `let` field is a value of its own: the builder's result is not a view
of the field, so a `var` may take it and write it. What stays refused is the `let` field's own record: binding
it to a `var` would let the `var` write what the field holds.

A `var` field's record can be moved out instead: `let old = self.log` followed by `self.log = Log.create()` leaves
`old` as the record's only holder, so a `let` field may take it. Statements between the two that neither read nor
write the field or the record are fine; a read of the field, a call that reaches it, or a record some other holder
also names is refused.

## Tests

<!-- test: a-record-built-from-strings-read-out-of-a-let-field-is-writable -->
A `let` field's `String` is copied into a request that a call hands back, inside an exchange, inside a union. The
caller binds the exchange to a `var` and writes it.

```maxon
typealias Tally = int(0 to 1000000)

type Arrived
	export let body as String
	export let flag as bool

	static function create(body String, flag bool) returns Arrived
		return Arrived{body: body, flag: flag}
	end 'create'
end 'Arrived'

type Request
	var body as String
	let path as String

	static function create(path String) returns Request
		return Request{body: "", path: path}
	end 'create'

	function setBody(text String)
		self.body = text
	end 'setBody'
end 'Request'

type Exchange
	export var stage as Tally
	let parsed as Request

	static function create(parsed Request) returns Exchange
		return Exchange{stage: 0, parsed: parsed}
	end 'create'

	function bump()
		self.stage = self.stage + 1
	end 'bump'
end 'Exchange'

union Arrival
	forExchange(exchange Exchange)
	forNothing(flag bool)
end 'Arrival'

function exchangeFor(arrived Arrived) returns Exchange
	var request = Request.create("p")
	request.setBody(arrived.body)

	return Exchange.create(request)
end 'exchangeFor'

function arrive(arrived Arrived) returns Arrival
	if arrived.flag 'nothing'
		return Arrival.forNothing(true)
	end 'nothing'

	return Arrival.forExchange(exchangeFor(arrived))
end 'arrive'

function next(arrived Arrived) returns Exchange
	let arrival = arrive(arrived)

	match arrival 'which'
		forExchange(exchange) then return exchange
		forNothing then print("none\n")
	end 'which'

	panic("unreachable")
end 'next'

function main() returns ExitCode
	let arrived = Arrived.create("hi", flag: false)
	var exchange = next(arrived)
	exchange.bump()
	exchange.stage = exchange.stage + 1

	return exchange.stage as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: error.a-let-fields-own-record-is-not-writable-through-a-var -->
The twin: the record a `let` field holds is the field's own, so a `var` bound to it is refused.

```maxon
typealias Tally = int(0 to 1000000)

type Settings
	export var level as Tally

	static function create(level Tally) returns Settings
		return Settings{level: level}
	end 'create'
end 'Settings'

type Owner
	export let settings as Settings

	static function create(settings Settings) returns Owner
		return Owner{settings: settings}
	end 'create'
end 'Owner'

function main() returns ExitCode
	let owner = Owner.create(Settings.create(1))
	var copy = owner.settings
	copy.level = 2

	return copy.level as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/deep-let-built-values/error.a-let-fields-own-record-is-not-writable-through-a-var.maxon:22:6: cannot assign a value read out of a `let` field to mutable binding 'copy'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.a-local-bound-to-a-let-fields-record-is-not-assigned-to-a-var -->
The `let` local `held` names the `let` field's own record. Assigning it to an existing `var` binds the record to a
writer exactly as declaring the `var` from it would.

```maxon
typealias Tally = int(0 to 1000000)

type Settings
	export var level as Tally

	static function create(level Tally) returns Settings
		return Settings{level: level}
	end 'create'
end 'Settings'

type Owner
	export let settings as Settings

	static function create(settings Settings) returns Owner
		return Owner{settings: settings}
	end 'create'
end 'Owner'

function main() returns ExitCode
	let owner = Owner.create(Settings.create(1))
	let held = owner.settings
	var copy = Settings.create(5)
	copy = held
	copy.level = 2

	return copy.level as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/deep-let-built-values/error.a-local-bound-to-a-let-fields-record-is-not-assigned-to-a-var.maxon:24:2: cannot assign a value read out of a `let` field to mutable binding 'copy'; use 'let' instead of 'var', or use clone()
```

<!-- test: a-fresh-record-is-assigned-to-a-var -->
A record a function builds is the `var`'s own, so assigning it is accepted.

```maxon
typealias Tally = int(0 to 1000000)

type Settings
	export var level as Tally

	static function create(level Tally) returns Settings
		return Settings{level: level}
	end 'create'
end 'Settings'

function main() returns ExitCode
	var copy = Settings.create(5)
	copy = Settings.create(6)
	copy.level = 2

	return copy.level as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-field-record-swapped-out-is-stored-into-a-let-field -->
`let old = self.log` followed by `self.log = Log.create()` moves the record out of the field: the replacement is a
fresh value, nothing else holds the old record, so `old` is its only holder and a `let` field may take it.

```maxon
typealias NameList = Array with ByteArray

type Told
	export var names as NameList

	static function create() returns Self
		return Self{names: NameList.create()}
	end 'create'

	function note(name ByteArray)
		self.names.push(name)
	end 'note'
end 'Told'

type Log
	export var told as Told

	static function create() returns Self
		return Self{told: Told.create()}
	end 'create'
end 'Log'

type KeepLog
	export let log as Log

	static function create(log Log) returns Self
		return Self{log: log}
	end 'create'
end 'KeepLog'

type Holder
	var log as Log

	static function create() returns Self
		return Self{log: Log.create()}
	end 'create'

	function note(name ByteArray)
		self.log.told.note(name)
	end 'note'

	function closeWhole() returns KeepLog
		var old = self.log
		self.log = Log.create()
		return KeepLog.create(old)
	end 'closeWhole'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	holder.note(b"a")
	let kept = holder.closeWhole()

	return kept.log.told.names.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: unrelated-work-between-the-swap-load-and-store-is-accepted -->
Statements that neither read nor write the field, the old record, or anything that reaches them may sit between the
load and the store: the old record is still the only holder when the replacement lands.

```maxon
typealias Count = int(0 to 1000)

typealias NameList = Array with ByteArray

type Told
	export var names as NameList

	static function create() returns Self
		return Self{names: NameList.create()}
	end 'create'

	function note(name ByteArray)
		self.names.push(name)
	end 'note'
end 'Told'

type Log
	export var told as Told

	static function create() returns Self
		return Self{told: Told.create()}
	end 'create'
end 'Log'

type KeepLog
	export let log as Log

	static function create(log Log) returns Self
		return Self{log: log}
	end 'create'
end 'KeepLog'

type Holder
	var log as Log
	var generation as Count

	static function create() returns Self
		return Self{log: Log.create(), generation: 0}
	end 'create'

	function note(name ByteArray)
		self.log.told.note(name)
	end 'note'

	function closeWhole() returns KeepLog
		var old = self.log
		let next = self.generation + 1
		self.generation = next
		self.log = Log.create()
		return KeepLog.create(old)
	end 'closeWhole'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	holder.note(b"a")
	let kept = holder.closeWhole()

	return kept.log.told.names.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-read-of-the-field-between-the-swap-load-and-store-is-refused -->
A read of `self.log` between the load and the store may hand the record to a second holder, so the swap no longer
proves `old` is the only one and the `let` field refuses it.

```maxon
typealias Count = int(0 to 1000)

typealias NameList = Array with ByteArray

type Told
	export var names as NameList

	static function create() returns Self
		return Self{names: NameList.create()}
	end 'create'

	function note(name ByteArray)
		self.names.push(name)
	end 'note'
end 'Told'

type Log
	export var told as Told

	static function create() returns Self
		return Self{told: Told.create()}
	end 'create'
end 'Log'

type KeepLog
	export let log as Log

	static function create(log Log) returns Self
		return Self{log: log}
	end 'create'
end 'KeepLog'

type Holder
	var log as Log
	var total as Count

	static function create() returns Self
		return Self{log: Log.create(), total: 0}
	end 'create'

	function note(name ByteArray)
		self.log.told.note(name)
	end 'note'

	function closeWhole() returns KeepLog
		var old = self.log
		let seen = self.log.told.names.count()
		self.total = seen as Count
		self.log = Log.create()
		return KeepLog.create(old)
	end 'closeWhole'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	holder.note(b"a")
	let kept = holder.closeWhole()

	return kept.log.told.names.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/deep-let-built-values/error.a-read-of-the-field-between-the-swap-load-and-store-is-refused.maxon:51:18: cannot pass a value other names may still write to parameter 'log' of 'KeepLog.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: error.a-call-that-reaches-the-field-between-the-swap-load-and-store-is-refused -->
A call that may read or write `self.log` between the load and the store is judged by what it does to the field, and
`self.note(...)` reaches it, so the swap is not proven and the `let` field refuses the record.

```maxon
typealias NameList = Array with ByteArray

type Told
	export var names as NameList

	static function create() returns Self
		return Self{names: NameList.create()}
	end 'create'

	function note(name ByteArray)
		self.names.push(name)
	end 'note'
end 'Told'

type Log
	export var told as Told

	static function create() returns Self
		return Self{told: Told.create()}
	end 'create'
end 'Log'

type KeepLog
	export let log as Log

	static function create(log Log) returns Self
		return Self{log: log}
	end 'create'
end 'KeepLog'

type Holder
	var log as Log

	static function create() returns Self
		return Self{log: Log.create()}
	end 'create'

	function note(name ByteArray)
		self.log.told.note(name)
	end 'note'

	function closeWhole() returns KeepLog
		var old = self.log
		self.note(b"peek")
		self.log = Log.create()
		return KeepLog.create(old)
	end 'closeWhole'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	holder.note(b"a")
	let kept = holder.closeWhole()

	return kept.log.told.names.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/deep-let-built-values/error.a-call-that-reaches-the-field-between-the-swap-load-and-store-is-refused.maxon:47:18: cannot pass a value other names may still write to parameter 'log' of 'KeepLog.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: error.a-record-another-holder-also-names-is-not-swapped-out -->
The `Holder` was built around a `Log` that `main` still names. Swapping the field leaves that second holder with the
old record, so the `let` field refuses it.

```maxon
typealias NameList = Array with ByteArray

type Told
	export var names as NameList

	static function create() returns Self
		return Self{names: NameList.create()}
	end 'create'

	function note(name ByteArray)
		self.names.push(name)
	end 'note'
end 'Told'

type Log
	export var told as Told

	static function create() returns Self
		return Self{told: Told.create()}
	end 'create'
end 'Log'

type KeepLog
	export let log as Log

	static function create(log Log) returns Self
		return Self{log: log}
	end 'create'
end 'KeepLog'

type Holder
	var log as Log

	static function create(log Log) returns Self
		return Self{log: log}
	end 'create'

	function note(name ByteArray)
		self.log.told.note(name)
	end 'note'

	function closeWhole() returns KeepLog
		var old = self.log
		self.log = Log.create()
		return KeepLog.create(old)
	end 'closeWhole'
end 'Holder'

function main() returns ExitCode
	var shared = Log.create()
	var holder = Holder.create(shared)
	holder.note(b"a")
	let kept = holder.closeWhole()
	shared.told.note(b"b")

	return kept.log.told.names.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/deep-let-built-values/error.a-record-another-holder-also-names-is-not-swapped-out.maxon:46:18: cannot pass a value other names may still write to parameter 'log' of 'KeepLog.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```
