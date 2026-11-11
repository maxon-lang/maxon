---
feature: parameter-write-seeds
status: experimental
keywords: [mutation, parameter, alias, merge, field chain, call result, raw resource, function value, witness, E3207]
category: semantics
---

# What Makes a Function Write Its Parameter

## Documentation

A function writes a parameter when the record it writes may be that parameter's record, or may lie within it. The write need not name the parameter: a `let` alias of it, a call that hands it back, a variable that holds it on one path and a fresh record on another, a field or array reached through it, a payload bound by a `match` on it, and an argument within it that is handed to a callee that writes (directly, through a function value, or through an interface requirement declared `mutable self`) all write it. Releasing a raw resource the parameter owns (`close()`) is a write of the parameter too.

A parameter the function writes takes a mutable argument: a `freeze` result, a constant, and a read of a registry value are refused at the call (`E3207`), and the same function accepts a `var` the caller owns. A function that writes only a record it creates, even one that stores a parameter's array, writes no parameter.

## Tests

<!-- test: error.a-receiver-write-through-a-local-alias-writes-the-parameter -->
`alias` names the record `d` names, so the push writes `d`, and a constant array cannot be handed to a function that writes its parameter.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d IntArray)
	let alias = d
	alias.push(9)
end 'grow'

function main() returns ExitCode
	let a = [1 as Integer]
	grow(a)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3207: <fragment>:12:7: 'grow' writes 'd' (line 7: `alias.push(9)`); 'a' is immutable
```

<!-- test: error.a-receiver-write-through-a-call-result-writes-the-parameter -->
`pick` may hand back its parameter, so the record `c` names may be `b`, and the field store writes it.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, flag bool) returns Box
	if flag 'useParameter'
		return p
	end 'useParameter'

	return Box.create(0)
end 'pick'

function edit(b Box)
	let c = pick(b, flag: true)
	c.n = 5
end 'edit'

function main() returns ExitCode
	let frozen = freeze Box.create(7)
	edit(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:27:7: 'edit' writes 'b'; 'frozen' is immutable
```

<!-- test: error.a-receiver-write-through-a-merge-writes-the-parameter -->
`target` is the parameter on one path and a fresh array on the other, so what the push writes may be `d`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d IntArray, flag bool)
	var target = IntArray.create(0)

	if flag 'useParameter'
		target = d
	end 'useParameter'

	target.push(9)
end 'grow'

function main() returns ExitCode
	let a = [1 as Integer]
	grow(a, flag: true)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3207: <fragment>:17:7: 'grow' writes 'd' (line 12: `target.push(9)`); 'a' is immutable
```

<!-- test: error.a-write-through-a-field-chain-of-the-parameter-writes-it -->
The array `s.items` lies within `s`, so pushing onto it writes the shelf `s` names.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Shelf
	export var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create(0)}
	end 'create'
end 'Shelf'

function stock(s Shelf)
	s.items.push(9)
end 'stock'

function main() returns ExitCode
	let frozen = freeze Shelf.create()
	stock(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:19:8: 'stock' writes 's' (line 14: `s.items.push(9)`); 'frozen' is immutable
```

<!-- test: error.a-field-store-through-an-alias-writes-the-parameter -->
`alias` names the record `b` names, so assigning its field writes `b`.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function bump(b Box)
	let alias = b
	alias.n = 5
end 'bump'

function main() returns ExitCode
	let frozen = freeze Box.create(7)
	bump(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:19:7: 'bump' writes 'b' (line 14: `alias.n = 5`); 'frozen' is immutable
```

<!-- test: error.an-argument-within-a-parameter-handed-to-a-writing-callee-writes-the-parameter -->
`grow` writes the array it is handed, and `s.items` lies within `s`, so `feed` writes the shelf it was handed.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Shelf
	export var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create(0)}
	end 'create'
end 'Shelf'

function grow(d IntArray)
	d.push(9)
end 'grow'

function feed(s Shelf)
	grow(s.items)
end 'feed'

function main() returns ExitCode
	let frozen = freeze Shelf.create()
	feed(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:23:7: 'feed' writes 's' (it calls 'grow', which writes 'd'); 'frozen' is immutable
```

<!-- test: error.an-argument-within-a-parameter-handed-to-a-function-value-that-writes-writes-the-parameter -->
The function value `f` is `grow`, which writes the array it is handed, so `feed` writes the shelf whose array it passes.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Shelf
	export var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create(0)}
	end 'create'
end 'Shelf'

function grow(d IntArray)
	d.push(9)
end 'grow'

function feed(s Shelf)
	let f = grow
	f(s.items)
end 'feed'

function main() returns ExitCode
	let frozen = freeze Shelf.create()
	feed(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:24:7: 'feed' writes 's' (it calls 'grow', which writes 'd'); 'frozen' is immutable
```

<!-- test: error.a-method-the-requirement-declares-mutable-writes-the-parameter-it-is-called-on -->
`Grows.grow` declares `mutable self`, so `stretch` writes the conformer it was handed through the interface, and a frozen bag cannot be handed to it.

```maxon
typealias Tally = int(0 to u64.max)

interface Grows
	function grow(mutable self, by Tally)
end 'Grows'

type Bag implements Grows
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function grow(by Tally)
		self.total = self.total + by
	end 'grow'
end 'Bag'

function stretch(g Grows)
	g.grow(5)
end 'stretch'

function main() returns ExitCode
	let frozen = freeze Bag.create()
	stretch(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:26:10: 'stretch' writes 'g' (line 21: it is passed to a `mutable` parameter of 'Grows.grow'); 'frozen' is immutable
```

<!-- test: error.releasing-a-raw-resource-writes-the-parameter-that-owns-it -->
`close()` writes the mapping the segment owns, so `retire` writes `s`, and a registry read, which every thread shares, cannot be handed to it.

```maxon
default SharedSegment = try SharedSegment.create("maxon-spec-write-seeds", bytes: 4096) otherwise panic("no segment")

function retire(s SharedSegment)
	s.close()
end 'retire'

function main() returns ExitCode
	retire(SharedSegment.current())
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:9:9: 'retire' writes 's' (it calls 'close', which writes its receiver); 'SharedSegment.current()' is immutable
```

<!-- test: error.a-write-through-a-match-binding-of-a-payload-of-the-parameter-writes-it -->
`label` is the array the union `t` holds, so the push writes the union `t` names, and a frozen union cannot be handed to `poke`.

```maxon
union Target
	none
	named(label ByteArray)
end 'Target'

function poke(t Target)
	match t 'm'
		none then break
		named(label) then label.push(65)
	end 'm'
end 'poke'

function main() returns ExitCode
	let frozen = freeze Target.named("x".toByteArray())
	poke(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:16:7: 'poke' writes 't' (line 10: `label.push(65)`); 'frozen' is immutable
```

<!-- test: error.a-write-through-an-array-that-is-the-parameters-on-one-path-and-fresh-on-the-other-writes-the-parameter -->
`list` holds the array inside `t` on one path and a fresh array on the other, so what the push writes may lie within `t`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Shelf
	export var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create(0)}
	end 'create'
end 'Shelf'

type Holder
	export var inner as Shelf

	static function create() returns Self
		return Self{inner: Shelf.create()}
	end 'create'
end 'Holder'

function poke(t Holder, deep bool)
	var list = IntArray.create(0)

	if deep 'fromInner'
		list = t.inner.items
	end 'fromInner'

	list.push(1)
end 'poke'

function main() returns ExitCode
	let frozen = freeze Holder.create()
	poke(frozen, deep: true)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:33:7: 'poke' writes 't' (line 28: `list.push(1)`); 'frozen' is immutable
```

<!-- test: a-callee-that-pushes-into-a-fresh-array-leaves-the-parameter-alone -->
The negative control: `grow` pushes into an array it creates and only reads `d`, so no parameter is written and a constant array is accepted.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d IntArray, flag bool) returns Integer
	let target = IntArray.create(0)

	if flag 'copyOne'
		target.push(try d.get(0) otherwise 0)
	end 'copyOne'

	target.push(9)
	return target.count() as Integer
end 'grow'

function main() returns ExitCode
	let a = [1 as Integer]
	let total = grow(a, flag: true)
	print("{total} {a.count()}\n")
	return 0
end 'main'
```
```stdout
2 1
```
```exitcode
0
```

<!-- test: a-callee-that-writes-an-array-held-by-a-fresh-record-leaves-the-parameter-alone -->
The control for the within seed: `held` stores the parameter's array in a record it builds but pushes only into the record's own fresh array, which lies within nothing the parameter holds.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Shelf
	export var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create(0)}
	end 'create'
end 'Shelf'

function build(s Shelf) returns Shelf
	let made = Shelf.create()
	made.items.push(try s.items.get(0) otherwise 0)
	return made
end 'build'

function main() returns ExitCode
	let source = Shelf.create()
	source.items.push(4)
	let made = build(source)
	made.items.push(5)
	print("{made.items.count()} {source.items.count()}\n")
	return 0
end 'main'
```
```stdout
2 1
```
```exitcode
0
```

<!-- test: a-writing-callee-accepts-a-variable-for-each-alias-shape -->
A write the callee makes through an alias, a call result or a merge is a write of the parameter, so it is accepted for a `var` the caller owns, and the caller sees every push.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function viaAlias(d IntArray)
	let alias = d
	alias.push(1)
end 'viaAlias'

function pass(d IntArray) returns IntArray
	return d
end 'pass'

function viaResult(d IntArray)
	let r = pass(d)
	r.push(2)
end 'viaResult'

function viaMerge(d IntArray, flag bool)
	var target = IntArray.create(0)

	if flag 'useParameter'
		target = d
	end 'useParameter'

	target.push(3)
end 'viaMerge'

function main() returns ExitCode
	let items = IntArray.create(0)
	viaAlias(items)
	viaResult(items)
	viaMerge(items, flag: true)
	print("{items.count()}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: error.a-caller-of-a-keeper-reached-through-a-chain-of-keepers-writes-the-parameter -->
`inner` stores `v` into the box `b` names, `middle` forwards its `v` to `inner`, and `outer` hands `middle` the item `src.item` holds, so `outer` keeps a value read from `src` in a writable slot and takes a mutable `src`.

```maxon
typealias Tally = int(0 to u64.max)

type Item
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Item'

type Box
	export var item as Item

	static function create(item Item) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Holder
	export var item as Item

	static function create(item Item) returns Self
		return Self{item: item}
	end 'create'
end 'Holder'

function inner(b mutable Box, v Item)
	b.item = v
end 'inner'

function middle(b mutable Box, v Item)
	inner(b, v: v)
end 'middle'

function outer(b mutable Box, src Holder)
	middle(b, v: src.item)
end 'outer'

function main() returns ExitCode
	var box = Box.create(Item.create(0))
	let frozen = freeze Holder.create(Item.create(1))
	outer(box, src: frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:43:13: 'outer' writes 'src' (it calls 'middle', which calls 'inner', which writes 'v'); 'frozen' is immutable
```
