---
feature: export-var-fields
status: in-progress
keywords: [export, var, field, visibility, encapsulation]
category: type-system
---

# Export Var Fields

## Documentation

### Field Visibility

Type fields can be marked with `export` to make them accessible from every file. By default a field is visible to the files of its type's declaring directory and that directory's subdirectories, and refused (E3014) everywhere else.

```text
typealias Score = int(i64.min to i64.max)

type Value
	export var n as Score      // accessible from every file
	var private as Score       // accessible from the type's directory
end 'Value'
```

### Accessing Exported Fields

Exported fields can be read and written (if mutable) from any code:

```text
function demo()
	var v = Value.create(42, private: 100)
	print(v.n)           // OK - n is exported
	v.n = 50             // OK - n is exported and mutable
end 'demo'
```

### Unexported Fields

An unexported field is reachable from the type's own methods and from any other code in the type's directory:

```text
typealias Score = int(i64.min to i64.max)

type Counter
	var count as Score              // the type's directory
	export var name as Score     // every file

	function increment()
		count = count + 1      // OK - accessing within method
	end 'increment'

	export function getCount() returns Score
		return count           // OK - accessing within method
	end 'getCount'

	static function create(count Score, name Score) returns Self
		return Self{count: count, name: name}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create(0, name: 5)
	c.increment()
	return c.getCount()
end 'main'
```
```exitcode
1
```

### Initialization

All fields (exported or not) can be initialized in struct literals, since the type itself determines what fields exist. The visibility only affects access after construction.

## Tests

<!-- test: export-var-basic -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Value
	export var n as Integer
	var private as Integer

	static function create() returns Self
		return Value{n: 42, private: 100}
	end 'create'
end 'Value'

function main() returns ExitCode
	let v = Value.create()
	return v.n
end 'main'
```
```exitcode
42
```

<!-- test: export-var-write -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Value
	export var n as Integer

	static function create() returns Self
		return Value{n: 10}
	end 'create'
end 'Value'

function main() returns ExitCode
	let v = Value.create()
	v.n = 42
	return v.n
end 'main'
```
```exitcode
42
```

<!-- test: export-let-readonly -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Config
	export let version as Integer

	static function create() returns Self
		return Config{version: 42}
	end 'create'
end 'Config'

function main() returns ExitCode
	let c = Config.create()
	return c.version
end 'main'
```
```exitcode
42
```

<!-- test: private-field-in-method -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	var count as Integer

	function increment()
		count = count + 1
	end 'increment'

	function getCount() returns Integer
		return count
	end 'getCount'

	static function create(count Integer) returns Self
		return Self{count: count}
	end 'create'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(40)
	c.increment()
	c.increment()
	return c.getCount()
end 'main'
```
```exitcode
42
```

<!-- test: mixed-export-fields -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer
	var secret as Integer

	static function create(v Integer, s Integer) returns Self
		return Box{value: v, secret: s}
	end 'create'

	function getSecret() returns Integer
		return secret
	end 'getSecret'
end 'Box'

function main() returns ExitCode
	let b = Box.create(20, s: 22)
	return b.value + b.getSecret()
end 'main'
```
```exitcode
42
```

<!-- test: unexported-field-read-in-its-directory -->
A free function in the type's own file reads the unexported field.
```maxon

typealias Integer = int(i64.min to i64.max)

type Value
	var private as Integer

	static function create() returns Self
		return Value{private: 42}
	end 'create'
end 'Value'

function main() returns ExitCode
	let v = Value.create()
	return v.private
end 'main'
```
```exitcode
42
```

<!-- test: error.unexported-field-write-from-another-directory -->
A write from a file outside the type's directory is refused at the field token.
```maxon
// --- file: lib/value.maxon
typealias Integer = int(i64.min to i64.max)

export type Value
	var private as Integer

	export static function create() returns Self
		return Value{private: 0}
	end 'create'
end 'Value'

// --- file: main.maxon
function main() returns ExitCode
	var v = Value.create()
	v.private = 42
	return 0
end 'main'
```
```maxoncstderr
error E3014: <fragment>:16:4: field 'Value.private' is not exported
```

<!-- test: all-fields-private-by-default -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Simple
	var x as Integer
	var y as Integer

	static function make(a Integer, b Integer) returns Self
		return Simple{x: a, y: b}
	end 'make'

	function sum() returns Integer
		return x + y
	end 'sum'
end 'Simple'

function main() returns ExitCode
	let s = Simple.make(20, b: 22)
	return s.sum()
end 'main'
```
```exitcode
42
```
