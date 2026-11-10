---
feature: deep-let-let-held-contents
status: experimental
keywords: [let, immutable, let field, keep, mutable container, payload-free union, module let, value type]
category: memory
---

# What a record holds through its `let` fields

## Documentation

A `let` field is a name that is never reassigned, and the record it holds is written like any other: a parameter a
function stores into a `let` field of a record is kept where something may write it, and a `var` bound to the same
record writes what the field holds. A parameter declared `immutable` takes immutable data, and a `var` field
refuses it. A record whose fields are all `let` values is a value type and goes anywhere, and so is a union whose
every payload is a `let` value.

A payload-free case of a union held in a module-level `let` has no storage to write, so it passes every door that
immutable data is refused at, directly, through a call result, or through a merge.

## Tests

<!-- test: a-parameter-kept-only-in-a-let-field-of-a-pushed-record-takes-an-immutable-argument -->
`Entry` keeps `name` in a `let` field and `weight` in a `var` field. The entry goes into a mutable array, and `source.title`, itself `immutable`, is accepted as the argument for `name`, a parameter declared `immutable`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias EntryArray = Array with Entry

type Entry
	export let name as immutable ByteArray
	export var weight as Integer

	static function create(name immutable ByteArray) returns Self
		return Self{name: name, weight: 0}
	end 'create'
end 'Entry'

type Source
	export let title as immutable ByteArray

	static function create() returns Self
		return Self{title: b"title"}
	end 'create'
end 'Source'

type Registry
	var entries as EntryArray

	static function create() returns Self
		return Self{entries: EntryArray.create(0)}
	end 'create'

	function add(name immutable ByteArray)
		self.entries.push(Entry.create(name))
	end 'add'

	function total() returns Integer
		return self.entries.count() as Integer
	end 'total'
end 'Registry'

function main() returns ExitCode
	var registry = Registry.create()
	let source = Source.create()
	registry.add(source.title)

	return registry.total() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-parameter-kept-in-a-var-field-of-a-pushed-record-refuses-an-immutable-argument -->
`name` is a `var` field here, so the pushed entry holds a mutable `ByteArray`, and the `immutable` `source.title` is refused for it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias EntryArray = Array with Entry

type Entry
	export var name as ByteArray
	export var weight as Integer

	static function create(name ByteArray) returns Self
		return Self{name: name, weight: 0}
	end 'create'
end 'Entry'

type Source
	export let title as immutable ByteArray

	static function create() returns Self
		return Self{title: b"title"}
	end 'create'
end 'Source'

type Registry
	var entries as EntryArray

	static function create() returns Self
		return Self{entries: EntryArray.create(0)}
	end 'create'

	function add(name ByteArray)
		self.entries.push(Entry.create(name))
	end 'add'

	function total() returns Integer
		return self.entries.count() as Integer
	end 'total'
end 'Registry'

function main() returns ExitCode
	var registry = Registry.create()
	let source = Source.create()
	registry.add(source.title)

	return registry.total() as ExitCode
end 'main'
```
```maxoncstderr
error E3207: <fragment>:41:15: 'add' writes 'name' (line 30: a result read from it is stored in a writable slot (an element of the container push writes)); 'source' is immutable
```

<!-- test: a-record-whose-fields-are-all-let-is-a-value-type-pushed-anywhere -->
`Entry` has only `let` fields of value types, so it is a value type: the entry is pushed into a mutable array and `source.size` is accepted for `id`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias EntryArray = Array with Entry

type Entry
	export let id as Integer
	export let weight as Integer

	static function create(id Integer) returns Self
		return Self{id: id, weight: 0}
	end 'create'
end 'Entry'

type Source
	export let size as Integer

	static function create() returns Self
		return Self{size: 3}
	end 'create'
end 'Source'

type Registry
	var entries as EntryArray

	static function create() returns Self
		return Self{entries: EntryArray.create(0)}
	end 'create'

	function add(id Integer)
		self.entries.push(Entry.create(id))
	end 'add'

	function total() returns ElementIndex
		return self.entries.count()
	end 'total'
end 'Registry'

function main() returns ExitCode
	var registry = Registry.create()
	let source = Source.create()
	registry.add(source.size)

	return registry.total() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-payload-free-union-case-in-a-module-let-passes-every-door -->
`NoTarget` is a payload-free case in a module-level `let`. It is stored into a `var` field directly, through a call
result and through a merge, and none of those is refused.

```maxon
union Target
	none
	named(label String)
end 'Target'

let NoTarget = Target.none

type Walker
	var target as Target

	static function create() returns Self
		return Self{target: Target.none}
	end 'create'

	function aim(next Target)
		self.target = next
	end 'aim'

	function aimed() returns bool
		return match self.target 'shape'
			none gives false
			named gives true
		end 'shape'
	end 'aimed'
end 'Walker'

function fallback() returns Target
	return NoTarget
end 'fallback'

function main() returns ExitCode
	var walker = Walker.create()
	walker.aim(NoTarget)
	walker.aim(fallback())
	let chosen = Target.named("x") if walker.aimed() else NoTarget
	walker.aim(chosen)

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-union-case-with-a-payload-in-a-module-let-is-refused-at-every-door -->
The same three doors as the case above with a module-level `let` that holds a case with a payload. `Target` has a `ByteArray` payload, so the value is `immutable` image data, and a `var` field that keeps it is a second holder that could write it. The direct pass, the call result and the merge are each refused.

```maxon
union Target
	none
	named(label ByteArray)
end 'Target'

let Named = Target.named("x".toByteArray())

type Walker
	var target as Target

	static function create() returns Self
		return Self{target: Target.none}
	end 'create'

	function aim(next Target)
		self.target = next
	end 'aim'

	function aimed() returns bool
		return match self.target 'shape'
			none gives false
			named gives true
		end 'shape'
	end 'aimed'
end 'Walker'

function fallback() returns immutable Target
	return Named
end 'fallback'

function main() returns ExitCode
	var walker = Walker.create()
	walker.aim(Named)
	walker.aim(fallback())
	let chosen = Target.none if walker.aimed() else Named
	walker.aim(chosen)

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:34:13: 'aim' writes 'next' (line 17: `self.target = next`); 'Named' is immutable
error E3207: <fragment>:35:13: 'aim' writes 'next' (line 17: `self.target = next`); 'fallback()' is immutable
error E3207: <fragment>:37:13: 'aim' writes 'next' (line 17: `self.target = next`); 'chosen' is immutable
```

<!-- test: error.a-payload-written-through-a-match-in-a-callee-refuses-a-module-let-union -->
A `match` binding is a payload of its scrutinee's box, so the callee that pushes onto `label` writes the box it was handed, and the module-level `let` that box is cannot be handed to it.

```maxon
union Target
	none
	named(label ByteArray)
end 'Target'

let Named = Target.named("x".toByteArray())

function poke(t Target)
	match t 'm'
		none then break
		named(label) then label.push(65)
	end 'm'
end 'poke'

function main() returns ExitCode
	poke(Named)

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:17:7: 'poke' writes 't' (line 12: `label.push(65)`); 'Named' is immutable
```

<!-- test: an-all-let-union-is-a-value-type-pushed-anywhere -->
Every payload of `Step` is `let`, so a `Step` is a value type exactly as an all-`let` record is: `route.first`
is pushed into a mutable array and nothing can write it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias StepArray = Array with Step

union Step
	move(distance Integer)
	stop
end 'Step'

type Route
	export let first as Step

	static function create() returns Self
		return Self{first: Step.move(3)}
	end 'create'
end 'Route'

function main() returns ExitCode
	let route = Route.create()
	var steps = StepArray.create(0)
	steps.push(route.first)

	return steps.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-union-with-a-var-payload-is-pushed-anywhere -->
The same union with one payload that is not `let` is not a value type, and the `route.first` is pushed into the mutable array all the same: the `let` field only fixes the name, and the array shares the union.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias StepArray = Array with Step

union Step
	move(distance Integer)
	stop
end 'Step'

type Route
	export let first as Step

	static function create() returns Self
		return Self{first: Step.move(3)}
	end 'create'
end 'Route'

function main() returns ExitCode
	let route = Route.create()
	var steps = StepArray.create(0)
	steps.push(route.first)

	return steps.count() as ExitCode
end 'main'
```
```exitcode
1
```
