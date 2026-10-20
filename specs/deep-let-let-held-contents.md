---
feature: deep-let-let-held-contents
status: experimental
keywords: [let, read-only, let field, keep, mutable container, payload-free union, module let, value type]
category: memory
---

# What a record holds through its `let` fields

## Documentation

A `let` field has no other writer, so a value a record keeps only in its `let` fields cannot be written through
that record, whatever the record is later pushed into. A parameter that a function stores only into `let` fields
of a record is therefore kept read-only: a read-only argument is accepted for it even when the record goes into a
mutable container. A record whose fields are all `let` is a value type and goes anywhere, and so is a union whose every payload is `let`. A parameter stored
into a `var` field is kept where something may write it, and a read-only argument is refused for it.

A payload-free case of a union held in a module-level `let` has no storage to write, so it passes every door
that a read-only value is refused at, directly, through a call result, or through a merge.

## Tests

<!-- test: a-parameter-kept-only-in-a-let-field-of-a-pushed-record-takes-a-read-only-argument -->
`Entry` keeps `name` in a `let` field and `weight` in a `var` field. The entry goes into a mutable array, yet
`source.title`, itself read-only, is accepted as the argument for `name`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias EntryArray = Array with Entry

type Entry
	export let name as ByteArray
	export var weight as Integer

	static function create(name ByteArray) returns Self
		return Self{name: name, weight: 0}
	end 'create'
end 'Entry'

type Source
	export let title as ByteArray

	static function create() returns Self
		return Self{title: b"title"}
	end 'create'
end 'Source'

type Registry
	var entries as EntryArray

	static function create() returns Self
		return Self{entries: EntryArray.create()}
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
```exitcode
1
```

<!-- test: error.a-parameter-kept-in-a-var-field-of-a-pushed-record-refuses-a-read-only-argument -->
`name` is a `var` field here, so the pushed entry is a place something may write, and the read-only `source.title`
is refused for it.

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
	export let title as ByteArray

	static function create() returns Self
		return Self{title: b"title"}
	end 'create'
end 'Source'

type Registry
	var entries as EntryArray

	static function create() returns Self
		return Self{entries: EntryArray.create()}
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
error E3078: <fragment>:41:11: cannot pass a `let` field of 'source', which is read-only, to parameter 'name' of 'Registry.add', which keeps it where something may write it; pass a clone()
```

<!-- test: a-record-whose-fields-are-all-let-is-a-value-type-pushed-anywhere -->
`Entry` has only `let` fields, so it is a value type: the entry is pushed into a mutable array and the read-only
`source.title` is accepted for `name`.

```maxon
typealias EntryArray = Array with Entry

type Entry
	export let name as ByteArray
	export let weight as ByteArray

	static function create(name ByteArray) returns Self
		return Self{name: name, weight: b"0"}
	end 'create'
end 'Entry'

type Source
	export let title as ByteArray

	static function create() returns Self
		return Self{title: b"title"}
	end 'create'
end 'Source'

type Registry
	var entries as EntryArray

	static function create() returns Self
		return Self{entries: EntryArray.create()}
	end 'create'

	function add(name ByteArray)
		self.entries.push(Entry.create(name))
	end 'add'

	function total() returns ElementIndex
		return self.entries.count()
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
The same three doors as the case above with a module-level `let` that holds a case with a payload. `Target` has a
payload that is not `let`, so the value is not inert: it is read-only, and a `var` field that keeps it is a second
holder that could write it. The direct pass, the call result and the merge are each refused.

```maxon
union Target
	none
	named(label String)
end 'Target'

let Named = Target.named("x")

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
error E3078: <fragment>:34:9: cannot pass 'Named', which is read-only (a module-level `let`), to parameter 'next' of 'Walker.aim', which keeps it where something may write it; pass a clone()
error E3078: <fragment>:35:9: cannot pass the result of 'fallback', which may share storage with a module-level `let`, to parameter 'next' of 'Walker.aim', which keeps it where something may write it; pass a clone()
error E3078: <fragment>:37:9: cannot pass 'chosen', which is read-only (a module-level `let`), to parameter 'next' of 'Walker.aim', which keeps it where something may write it; pass a clone()
```

<!-- test: an-all-let-union-is-a-value-type-pushed-anywhere -->
Every payload of `Step` is `let`, so a `Step` is a value type exactly as an all-`let` record is: the read-only
`route.first` is pushed into a mutable array and nothing else can write it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias StepArray = Array with Step

union Step
	move(let distance Integer)
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
	var steps = StepArray.create()
	steps.push(route.first)

	return steps.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-union-with-a-var-payload-is-not-a-value-type -->
The same union with one payload that is not `let` can have a second writer, so the read-only `route.first` is
refused when it is pushed into the mutable array.

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
	var steps = StepArray.create()
	steps.push(route.first)

	return steps.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/deep-let-let-held-contents/error.a-union-with-a-var-payload-is-not-a-value-type.maxon:21:8: cannot store a value read out of a `let` field into mutable place 'an element of the container push writes'; use clone()
```
