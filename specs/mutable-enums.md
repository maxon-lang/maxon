---
feature: mutable-enums
status: experimental
keywords: [enum, mutable, associated values, match, write-back]
category: type-system
---

# Mutable Enum Associated Values

## Documentation

### Mutable Match Bindings

When an enum variable is mutable (`var`), match bindings on associated values are also mutable. Assigning to a match binding writes the new value back to the enum's heap block in-place.

For scalar bindings (integers, booleans, etc.), use direct assignment:

```text
var b = Box.full(10)
match b 'update'
  full(value) then value = 42
  empty then return
end 'update'
```

For heap-pointer bindings (structs, enums with associated values, strings), use direct assignment:

```text
var b = Named.named("hello")
match b 'update'
  named(name) then name = "world"
  anonymous then return
end 'update'
```

When the enum variable is immutable (`let`), match bindings are read-only copies, preserving the existing behavior.

## Tests

<!-- test: basic-mutable -->
Mutate an associated value through a match binding on a `var` enum.
```maxon
typealias Integer = int(i64.min to i64.max)

union Box
	empty
	full(value Integer)
end 'Box'

function main() returns ExitCode
	var b = Box.full(10)
	match b 'update'
		full(value) then value = 42
		empty then return 0
	end 'update'
	match b 'read'
		full(value) then return value
		empty then return 0
	end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: write-back-verify -->
Verify that mutation through match binding persists after the match.
```maxon
typealias Integer = int(i64.min to i64.max)

union Wrapper
	none
	some(n Integer)
end 'Wrapper'

function readValue(w Wrapper) returns Integer
	match w 'r'
		some(n) then return n
		none then return 0
	end 'r'
end 'readValue'

function main() returns ExitCode
	var w = Wrapper.some(5)
	match w 'mutate'
		some(n) then n = 99
		none then return 0
	end 'mutate'
	return readValue(w)
end 'main'
```
```exitcode
99
```

<!-- test: multiple-mutable-fields -->
Multiple mutable fields in one case, mutate one at a time.
```maxon
typealias Integer = int(i64.min to i64.max)

union Pair
	empty
	values(a Integer, b Integer)
end 'Pair'

function main() returns ExitCode
	var p = Pair.values(1, b: 2)
	match p 'update-a'
		values(a, _) then a = 10
		empty then return 0
	end 'update-a'
	match p 'update-b'
		values(_, b) then b = 32
		empty then return 0
	end 'update-b'
	match p 'read'
		values(a, b) then return a + b
		empty then return 0
	end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: mixed-readonly-and-mutable -->
Only assign to some bindings, leave others unchanged.
```maxon
typealias Integer = int(i64.min to i64.max)

union Record
	blank
	data(id Integer, count Integer)
end 'Record'

function main() returns ExitCode
	var e = Record.data(1, count: 0)
	match e 'inc'
		data(id, count) then count = count + id
		blank then return 0
	end 'inc'
	match e 'read'
		data(id, count) then return id + count
		blank then return 0
	end 'read'
end 'main'
```
```exitcode
2
```

<!-- test: mutate-twice -->
Mutate the same associated value twice in sequence.
```maxon
typealias Integer = int(i64.min to i64.max)

union Cell
	empty
	holding(value Integer)
end 'Cell'

function main() returns ExitCode
	var c = Cell.holding(10)
	match c 'first'
		holding(value) then value = 20
		empty then return 0
	end 'first'
	match c 'second'
		holding(value) then value = value + 22
		empty then return 0
	end 'second'
	match c 'read'
		holding(value) then return value
		empty then return 0
	end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: error.assign-to-let-enum-binding -->
Error: assigning to a match binding when the enum variable is `let`.
```maxon
typealias Integer = int(i64.min to i64.max)

union Box
	empty
	full(value Integer)
end 'Box'

function main() returns ExitCode
	let b = Box.full(10)
	match b 'bad'
		full(value) then value = 42
		empty then return 0
	end 'bad'
	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/mutable-enums/error.assign-to-let-enum-binding.test:12:20: cannot assign to immutable variable: 'value'
```

<!-- test: mutate-string-payload -->
Mutate a String-typed associated value.
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function main() returns ExitCode
	var n = Named.named("hello")
	match n 'update'
		named(name) then name = "world"
		anonymous then return 0
	end 'update'
	match n 'read'
		named(name) then return name.byteLength()
		anonymous then return 0
	end 'read'
end 'main'
```
```exitcode
5
```

<!-- test: mutate-struct-payload -->
Mutate a struct-typed associated value.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

union Shape
	nothing
	located(pos Point)
end 'Shape'

function main() returns ExitCode
	var s = Shape.located(Point.create(1, y: 2))
	match s 'update'
		located(pos) then pos = Point.create(10, y: 32)
		nothing then return 0
	end 'update'
	match s 'read'
		located(pos) then return pos.x + pos.y
		nothing then return 0
	end 'read'
end 'main'
```
```exitcode
42
```
