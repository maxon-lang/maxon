---
feature: regions-holds
status: experimental
keywords: [hold, pending operand, constructor, union, send, spawn, async, match subject]
category: semantics
---

# Pending operands are held

## Documentation

A value already evaluated for one operand survives the operands after it: a field read used as a constructor,
union-literal, send, `spawn` or `async` argument is held across every later operand that may run user code, and
a `match` subject is held across arms that write back.

## Tests

<!-- test: a-field-read-constructor-argument-survives-a-later-operand-that-replaces-the-field -->
`Pair.build` reads `shelf.items` for the first field and then runs `replace`, which gives the shelf a new array. The array the first field holds is still alive afterwards.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Shelf
	export var items as TallyArray

	static function create() returns Self
		let items = TallyArray.create(0)
		items.push(11)
		items.push(22)

		return Self{items: items}
	end 'create'
end 'Shelf'

type Pair
	export var first as TallyArray
	export let second as Tally

	static function build(shelf Shelf) returns Self
		return Self{first: shelf.items, second: replace(shelf)}
	end 'build'
end 'Pair'

function replace(shelf Shelf) returns Tally
	let fresh = TallyArray.create(0)
	fresh.push(99)
	shelf.items = fresh

	return 1
end 'replace'

function main() returns ExitCode
	let shelf = Shelf.create()
	let pair = Pair.build(shelf)
	print("{pair.first.count()} {try pair.first.get(0) otherwise 0} {pair.second}\n")
	return 0
end 'main'
```
```stdout
2 11 1
```
```exitcode
0
```

<!-- test: a-field-read-union-argument-survives-a-later-operand-that-replaces-the-field -->
`Parcel.filled` reads `shelf.items` and then runs `replace`; the array in the parcel is still alive.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Shelf
	export var items as TallyArray

	static function create() returns Self
		let items = TallyArray.create(0)
		items.push(11)
		items.push(22)

		return Self{items: items}
	end 'create'
end 'Shelf'

union Parcel
	empty
	filled(items TallyArray, tag Tally)
end 'Parcel'

function replace(shelf Shelf) returns Tally
	let fresh = TallyArray.create(0)
	fresh.push(99)
	shelf.items = fresh

	return 1
end 'replace'

function pack(shelf Shelf) returns Parcel
	return Parcel.filled(shelf.items, tag: replace(shelf))
end 'pack'

function main() returns ExitCode
	let shelf = Shelf.create()
	let parcel = pack(shelf)

	match parcel 'parcel'
		empty then print("empty\n")
		filled(items, tag) then print("{items.count()} {try items.get(0) otherwise 0} {tag}\n")
	end 'parcel'

	return 0
end 'main'
```
```stdout
2 11 1
```
```exitcode
0
```

<!-- test: a-field-read-send-argument-survives-a-later-operand-that-replaces-the-field -->
`sink.take` reads `shelf.label` for its first argument and then runs `replace`, which gives the shelf a new label. The text the message carries is still alive when the service reads it.

```maxon
typealias Tally = int(0 to u64.max)

type Shelf
	export var label as String

	static function create(n Tally) returns Self
		return Self{label: "label-{n}"}
	end 'create'
end 'Shelf'

type Sink
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(label String, tag Tally) returns Tally
		self.seen = self.seen + 1
		return (label.count() as Tally) + tag
	end 'take'
end 'Sink'

function replace(shelf Shelf) returns Tally
	shelf.label = "x"

	return 1
end 'replace'

function main() returns ExitCode
	let shelf = Shelf.create(1)
	let sink = spawn Sink.create()
	let n = try await sink.take(shelf.label, tag: replace(shelf)) otherwise 0
	print("{n}\n")
	return 0
end 'main'
```
```stdout
8
```
```exitcode
0
```

<!-- test: a-field-read-spawn-argument-survives-a-later-operand-that-replaces-the-field -->
`spawn Holder.create` reads `shelf.label` for the factory and then runs `replace`; the text the factory keeps is still alive.

```maxon
typealias Tally = int(0 to u64.max)

type Shelf
	export var label as String

	static function create(n Tally) returns Self
		return Self{label: "label-{n}"}
	end 'create'
end 'Shelf'

type Holder
	var label as String
	var tag as Tally

	static function create(label String, tag Tally) returns Self
		return Self{label: label, tag: tag}
	end 'create'

	export function total() returns Tally
		return (self.label.count() as Tally) + self.tag
	end 'total'
end 'Holder'

function replace(shelf Shelf) returns Tally
	shelf.label = "x"

	return 1
end 'replace'

function main() returns ExitCode
	let shelf = Shelf.create(1)
	let holder = spawn Holder.create(shelf.label, tag: replace(shelf))
	let n = try await holder.total() otherwise 0
	print("{n}\n")
	return 0
end 'main'
```
```stdout
8
```
```exitcode
0
```

<!-- test: a-field-read-async-argument-survives-a-later-operand-that-replaces-the-field -->
`async total` reads `shelf.items` for its first argument and then runs `replace`; the array the coroutine receives is still alive.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Shelf
	export var items as TallyArray

	static function create() returns Self
		let items = TallyArray.create(0)
		items.push(11)
		items.push(22)

		return Self{items: items}
	end 'create'
end 'Shelf'

function replace(shelf Shelf) returns Tally
	let fresh = TallyArray.create(0)
	fresh.push(99)
	shelf.items = fresh

	return 1
end 'replace'

function total(items TallyArray, tag Tally) returns Tally
	sleep(1)
	return (items.count() as Tally) + tag
end 'total'

function main() returns ExitCode
	let shelf = Shelf.create()
	let later = async total(shelf.items, tag: replace(shelf))
	let n = await later
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

<!-- test: a-match-subject-survives-an-arm-that-replaces-the-field-it-was-read-from -->
The arm writes `holder.slot` back while it still reads the payload it bound, so the subject is held for the whole match.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

union Slot
	empty
	filled(items TallyArray)
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		let items = TallyArray.create(0)
		items.push(11)
		items.push(22)

		return Self{slot: Slot.filled(items)}
	end 'create'
end 'Holder'

function readAfterReplacing(holder Holder, items TallyArray)
	holder.slot = Slot.empty

	let other = TallyArray.create(0)
	other.push(7)
	other.push(8)
	other.push(9)
	print("{items.count()} {try items.get(0) otherwise 0}\n")
end 'readAfterReplacing'

function main() returns ExitCode
	let holder = Holder.create()

	match holder.slot 'slot'
		empty then print("empty\n")
		filled(items) then readAfterReplacing(holder, items: items)
	end 'slot'

	return 0
end 'main'
```
```stdout
2 11
```
```exitcode
0
```

<!-- test: a-local-read-by-one-operand-and-written-by-a-reference-cell-in-the-same-statement-is-refused -->
`label` is read for the first operand and passed to a `mutable` parameter for the second, so a hold could not protect it from the cell write.

```maxon
typealias Tally = int(0 to u64.max)

type Pair
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'Pair'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var label = "original"
	let pair = Pair.build(label, second: put(label))
	print("{pair.first} {pair.second}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:21:43: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: a-local-read-by-a-later-operand-after-a-reference-cell-write-is-accepted -->
The second operand is evaluated after `put` has written `label`, so it reads the new value and nothing was held.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var label = "original"
	print("{put(label)} {label}\n")
	return 0
end 'main'
```
```stdout
1 replaced
```
```exitcode
0
```

<!-- test: a-ternary-condition-reads-the-local-before-the-reference-cell-arm-writes-it -->
The condition of `a if c else b` is evaluated before `a`, so `label` is read for `c` and then passed by reference in `a`, which a hold could not protect.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function longer(text String) returns bool
	return text.byteLength() > 3
end 'longer'

function main() returns ExitCode
	var label = "original"
	let result = put(label) if longer(label) else 0
	print("{result} {label}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:19: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: a-field-path-handed-by-reference-and-read-by-another-operand-is-refused -->
`h.f` is read for the first operand and passed to a `mutable` parameter for the second, so a hold could not protect it from the cell write.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String

	static function create() returns Self
		return Self{f: "original"}
	end 'create'
end 'Holder'

type Pair
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'Pair'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	let pair = Pair.build(h.f, second: put(h.f))
	print("{pair.first} {pair.second}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:29:41: argument 'h.f' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'h.f' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: a-name-in-the-else-arm-of-a-ternary-is-not-read-before-the-call-arm -->
`put(label) if flag else label.byteLength()`: the condition is `flag`, and the `else` arm is not evaluated with the call arm, so `label` is read by nothing pending when `put` writes it.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var label = "original"
	let flag = label.byteLength() > 100
	let result = put(label) if not flag else label.byteLength() as Tally
	print("{result} {label}\n")
	return 0
end 'main'
```
```stdout
1 replaced
```
```exitcode
0
```

<!-- test: a-let-alias-of-the-holder-moves-it-so-no-operand-reads-the-cell-under-two-names -->
`let g = h` moves `h` into `g`, so the cell cannot be read under another name: the first operand's read of `h` is a use after the move.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String

	static function create() returns Self
		return Self{f: "original"}
	end 'create'
end 'Holder'

type Pair
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'Pair'

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var h = Holder.create()
	let g = h
	let pair = Pair.build(h.f, second: put(g.f))
	print("{pair.first} {pair.second}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:24: use of moved value 'h': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-nested-ternary-in-the-else-arm-is-not-read-before-the-call-arm -->
The `else` arm, nested ternary and all, is not evaluated with the call arm, so the condition inside it does not read `label` while `put` writes it.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function main() returns ExitCode
	var label = "original"
	let flag = label.byteLength() > 100
	let result = put(label) if flag else (7 as Tally if label.byteLength() > 3 else 8 as Tally)
	print("{result} {label}\n")
	return 0
end 'main'
```
```stdout
7 original
```
```exitcode
0
```

<!-- test: a-closure-parameter-spelled-like-the-cell-is-not-the-cell -->
The closure's own `label` is its parameter, not the local `label` that `put` writes, so no other operand reads the cell.

```maxon
typealias Tally = int(0 to u64.max)
typealias Sizer = function(String) returns Tally

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function apply(sizer Sizer, text String) returns Tally
	return sizer(text)
end 'apply'

function both(first Tally, second Tally) returns Tally
	return first + second
end 'both'

function main() returns ExitCode
	var label = "original"
	let total = both(apply(function(label String) gives label.byteLength() as Tally, text: "ab"), second: put(label))
	print("{total} {label}\n")
	return 0
end 'main'
```
```stdout
3 replaced
```
```exitcode
0
```

<!-- test: a-ternary-in-a-later-operand-is-not-read-before-the-call -->
The ternary is the second operand, evaluated after `put(label)` has run, so its condition reads `label` after the write and not under it.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

type Pair
	export var first as Tally
	export var second as Tally

	static function build(first Tally, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'Pair'

function main() returns ExitCode
	var label = "original"
	let pair = Pair.build(put(label), second: (7 as Tally if label.byteLength() > 3 else 8 as Tally))
	print("{pair.first} {pair.second}\n")
	return 0
end 'main'
```
```stdout
1 7
```
```exitcode
0
```

<!-- test: a-let-alias-of-a-field-of-the-holder-is-not-another-name-for-its-cell -->
`g` names the same record as `h.a`, and a field of a `let` is refused at a `mutable` parameter, so no second spelling of the cell reaches the statement.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

type Inner
	export var b as String

	static function create() returns Self
		return Self{b: "original"}
	end 'create'
end 'Inner'

type Outer
	export var a as Inner

	static function create() returns Self
		return Self{a: Inner.create()}
	end 'create'
end 'Outer'

type Pair
	export var first as String
	export var second as Tally

	static function build(first String, second Tally) returns Self
		return Self{first: first, second: second}
	end 'build'
end 'Pair'

function main() returns ExitCode
	let h = Outer.create()
	let g = h.a
	let pair = Pair.build(h.a.b, second: put(g.b))
	print("{pair.first} {pair.second}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:38:39: 'g' is a let and cannot be passed to the `mutable` parameter 'text'; bind it to a `var`
```

<!-- test: a-ternary-condition-wrapping-a-group-that-holds-the-call-reads-the-local-first -->
`sum(put(label), b: 1) if label.byteLength() > 3 else 0` evaluates the condition first, so `label` is read before `put` writes it, whatever group the call sits in.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	let result = sum(put(label), b: 1) if label.byteLength() > 3 else 0
	print("{result} {label}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:23: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: a-ternary-condition-wrapping-two-groups-that-hold-the-call-reads-the-local-first -->
The ternary wraps the outer `sum`, two groups above `put(label)`, and its condition still reads `label` before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	let result = sum(1, b: sum(put(label), b: 2)) if label.byteLength() > 3 else 0
	print("{result} {label}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:33: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: a-ternary-condition-wrapping-an-array-literal-that-holds-the-call-reads-the-local-first -->
The ternary wraps the array literal that holds `put(label)`, and its condition reads `label` before the write.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	let result = [put(label), 1 as Tally] if label.byteLength() > 3 else [0 as Tally, 1 as Tally]
	print("{result.count()} {label}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:16:20: argument 'label' is read by another operand of this statement and passed by reference to 'put' (parameter 'text') — a hold cannot protect a value its by-reference cell overwrites. Copy 'label' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: a-ternary-that-is-a-later-operand-of-the-group-holding-the-call-reads-after-the-write -->
The ternary is the second operand of `sum`, so its condition reads `label` after `put(label)` has run, and it is accepted.

```maxon
typealias Tally = int(0 to u64.max)

function put(text mutable String) returns Tally
	text = "replaced"

	return 1
end 'put'

function sum(a Tally, b Tally) returns Tally
	return a + b
end 'sum'

function main() returns ExitCode
	var label = "original"
	let result = sum(put(label), b: 7 if label.byteLength() > 3 else 8)
	print("{result} {label}\n")
	return 0
end 'main'
```
```stdout
8 replaced
```
```exitcode
0
```
