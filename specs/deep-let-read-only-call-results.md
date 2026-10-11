---
feature: deep-let-read-only-call-results
status: experimental
keywords: [let, read-only, call result, let field, readonly, module let, keeper]
category: memory
---

# Read-only values handed back by a call

## Documentation

A function that returns a value read out of a `let` field, a module-level `let` or an element of a `readonly`
container returns a value nothing can ever write. The caller sees that result as read-only, so a `let` field or a
`readonly` container keeps it without a copy. A result that may also be a record some `var` can write is not
read-only, and a `let` field refuses it. A result that is read-only on some paths and freshly built on the others
is accepted by a `let` field, since neither alternative has another writer.

## Tests

<!-- test: a-let-field-read-through-a-method-is-kept-by-a-let-field -->
`fixedName` returns the `let` field `fixed`, so the label keeps it although `table` is a `var` its owner still
writes.

```maxon
typealias Integer = int(i64.min to i64.max)

type Label
	export let text as ByteArray

	static function create(text ByteArray) returns Self
		return Self{text: text}
	end 'create'
end 'Label'

type Table
	var visits as Integer
	let fixed as ByteArray

	static function create() returns Self
		return Self{visits: 0, fixed: b"beta"}
	end 'create'

	function touch()
		self.visits = self.visits + 1
	end 'touch'

	function fixedName() returns ByteArray
		return self.fixed
	end 'fixedName'
end 'Table'

function main() returns ExitCode
	var table = Table.create()
	table.touch()
	let label = Label.create(table.fixedName())

	return label.text.count() as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: a-readonly-element-read-through-a-method-is-kept-by-a-let-field -->
`nameOf` returns an element of a `readonly` array, which nothing can write.

```maxon
typealias Names = Array with readonly ByteArray

type Label
	export let text as ByteArray

	static function create(text ByteArray) returns Self
		return Self{text: text}
	end 'create'
end 'Label'

type Table
	var names as Names

	static function create() returns Self
		var names = Names.create()
		names.push(b"alpha")
		return Self{names: names}
	end 'create'

	function register(name ByteArray)
		self.names.push(name)
	end 'register'

	function nameOf(index ElementIndex) returns ByteArray
		return try self.names.get(index) otherwise panic("nameOf: no such name")
	end 'nameOf'
end 'Table'

function main() returns ExitCode
	var table = Table.create()
	table.register(b"gamma")
	let label = Label.create(table.nameOf(1))

	return label.text.count() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-readonly-element-read-through-a-method-is-pushed-into-a-readonly-container -->
The same result is stored into another `readonly` container.

```maxon
typealias Names = Array with readonly ByteArray

type Table
	var names as Names

	static function create() returns Self
		var names = Names.create()
		names.push(b"alpha")
		return Self{names: names}
	end 'create'

	function register(name ByteArray)
		self.names.push(name)
	end 'register'

	function nameOf(index ElementIndex) returns ByteArray
		return try self.names.get(index) otherwise panic("nameOf: no such name")
	end 'nameOf'
end 'Table'

function main() returns ExitCode
	var table = Table.create()
	table.register(b"gamma")
	var roster = Names.create()
	roster.push(table.nameOf(1))

	return roster.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-writable-element-read-through-a-method-is-refused-by-a-readonly-container -->
The twin of the case above with elements that are not `readonly`: `Table.nameOf` hands back an element its `var`
field still holds, so storing the result into a `readonly` container would leave two holders with one of them able
to write.

```maxon
typealias Names = Array with ByteArray
typealias Roster = Array with readonly ByteArray

type Table
	var names as Names

	static function create() returns Self
		var names = Names.create()
		names.push(b"alpha")
		return Self{names: names}
	end 'create'

	function register(name ByteArray)
		self.names.push(name)
	end 'register'

	function nameOf(index ElementIndex) returns ByteArray
		return try self.names.get(index) otherwise panic("nameOf: no such name")
	end 'nameOf'
end 'Table'

function main() returns ExitCode
	var table = Table.create()
	table.register(b"gamma")
	var roster = Roster.create()
	roster.push(table.nameOf(1))

	return roster.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:27:9: cannot store a value other names may still write into a `readonly` element of the container written by 'push': a `readonly` element has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: a-module-let-returned-by-a-function-is-kept-by-a-let-field -->
`banner` returns a module-level `let`, which is read-only everywhere.

```maxon
let Banner = b"banner"

type Label
	export let text as ByteArray

	static function create(text ByteArray) returns Self
		return Self{text: text}
	end 'create'
end 'Label'

function banner() returns ByteArray
	return Banner
end 'banner'

function main() returns ExitCode
	let label = Label.create(banner())

	return label.text.count() as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: error.a-module-var-returned-by-a-function-is-refused-by-a-let-field -->
The twin of the case above with a module-level `var`: `banner` hands back a value some function may still write,
so a `let` field cannot keep it.

```maxon
var Banner = b"banner"

type Label
	export let text as ByteArray

	static function create(text ByteArray) returns Self
		return Self{text: text}
	end 'create'
end 'Label'

function banner() returns ByteArray
	return Banner
end 'banner'

function main() returns ExitCode
	let label = Label.create(banner())

	return label.text.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:17:20: cannot pass a value other names may still write to parameter 'text' of 'Label.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: a-merge-of-a-let-field-and-a-fresh-value-is-kept-by-a-let-field -->
`pick` returns the `let` field on one path and a fresh array on the other. Neither has another writer, so the
`let` field of the label keeps either.

```maxon
typealias Integer = int(i64.min to i64.max)

type Label
	export let text as ByteArray

	static function create(text ByteArray) returns Self
		return Self{text: text}
	end 'create'
end 'Label'

type Table
	var visits as Integer
	let fixed as ByteArray

	static function create() returns Self
		return Self{visits: 0, fixed: b"beta"}
	end 'create'

	function touch()
		self.visits = self.visits + 1
	end 'touch'

	function pick(useFixed bool) returns ByteArray
		if useFixed 'fixed'
			return self.fixed
		end 'fixed'

		return ByteArray.create()
	end 'pick'
end 'Table'

function main() returns ExitCode
	var table = Table.create()
	table.touch()
	let label = Label.create(table.pick(true))

	return label.text.count() as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: error.a-var-fields-element-returned-by-a-method-is-refused-by-a-let-field -->
`nameOf` returns an element of a plain array its `var` field holds, which `table` may still write.

```maxon
typealias NameList = Array with ByteArray

type Label
	export let text as ByteArray

	static function create(text ByteArray) returns Self
		return Self{text: text}
	end 'create'
end 'Label'

type Table
	var names as NameList

	static function create() returns Self
		var names = NameList.create()
		names.push(b"alpha")
		return Self{names: names}
	end 'create'

	function register(name ByteArray)
		self.names.push(name)
	end 'register'

	function nameOf(index ElementIndex) returns ByteArray
		return try self.names.get(index) otherwise panic("nameOf: no such name")
	end 'nameOf'
end 'Table'

function main() returns ExitCode
	var table = Table.create()
	table.register(b"gamma")
	let label = Label.create(table.nameOf(1))

	return label.text.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/deep-let-read-only-call-results/error.a-var-fields-element-returned-by-a-method-is-refused-by-a-let-field.maxon:33:20: cannot pass a value other names may still write to parameter 'text' of 'Label.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: error.a-merge-of-a-let-field-and-a-var-fields-element-is-refused-by-a-let-field -->
`pick` returns the `let` field on one path and an element of the plain array a `var` field holds on the other. One
alternative may still be written, so the `let` field of the label refuses the result.

```maxon
typealias NameList = Array with ByteArray

type Label
	export let text as ByteArray

	static function create(text ByteArray) returns Self
		return Self{text: text}
	end 'create'
end 'Label'

type Table
	var names as NameList
	let fixed as ByteArray

	static function create() returns Self
		var names = NameList.create()
		names.push(b"alpha")
		return Self{names: names, fixed: b"beta"}
	end 'create'

	function register(name ByteArray)
		self.names.push(name)
	end 'register'

	function pick(useFixed bool) returns ByteArray
		if useFixed 'fixed'
			return self.fixed
		end 'fixed'

		return try self.names.get(0) otherwise panic("pick: no names")
	end 'pick'
end 'Table'

function main() returns ExitCode
	var table = Table.create()
	table.register(b"gamma")
	let label = Label.create(table.pick(true))

	return label.text.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/deep-let-read-only-call-results/error.a-merge-of-a-let-field-and-a-var-fields-element-is-refused-by-a-let-field.maxon:38:20: cannot pass a value other names may still write to parameter 'text' of 'Label.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: a-lookup-or-the-argument-is-read-only-when-the-argument-is -->
`spell` returns a read-only lookup on one path and its own parameter on the other. Its result is read-only when
the argument is, here an element of a `readonly` array, so the `let` field of the label keeps it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias RenameMap = Map with (ByteArray, readonly ByteArray)
typealias Roster = Array with readonly ByteArray

type Labelled
	export let label as ByteArray

	static function create(label ByteArray) returns Self
		return Self{label: label}
	end 'create'

	function size() returns Integer
		return self.label.count() as Integer
	end 'size'
end 'Labelled'

type Names
	var renames as RenameMap

	static function create() returns Self
		return Self{renames: RenameMap.create()}
	end 'create'

	function spell(name ByteArray) returns ByteArray
		return try self.renames.get(name) otherwise name
	end 'spell'
end 'Names'

function main() returns ExitCode
	let names = Names.create()
	var roster = Roster.create()
	roster.push(b"first")
	let labelled = Labelled.create(names.spell(try roster.get(0) otherwise panic("the roster holds one name")))

	return labelled.size() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.a-lookup-or-the-argument-is-refused-when-the-argument-may-be-written -->
The same `spell` handed a `var` that is read again returns that `var`'s record on one path, which something may
still write, so the `let` field of the label refuses the result.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias RenameMap = Map with (ByteArray, readonly ByteArray)

type Labelled
	export let label as ByteArray

	static function create(label ByteArray) returns Self
		return Self{label: label}
	end 'create'

	function size() returns Integer
		return self.label.count() as Integer
	end 'size'
end 'Labelled'

type Names
	var renames as RenameMap

	static function create() returns Self
		return Self{renames: RenameMap.create()}
	end 'create'

	function spell(name ByteArray) returns ByteArray
		return try self.renames.get(name) otherwise name
	end 'spell'
end 'Names'

function main() returns ExitCode
	let names = Names.create()
	var scratch = ByteArray.create()
	scratch.push(1)
	let labelled = Labelled.create(names.spell(scratch))

	return (labelled.size() + scratch.count() as Integer) as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/deep-let-read-only-call-results/error.a-lookup-or-the-argument-is-refused-when-the-argument-may-be-written.maxon:33:26: cannot pass a value other names may still write to parameter 'label' of 'Labelled.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```
