---
feature: duplicate-functions
status: stable
keywords: [duplicate, function, main, semantic, validation]
category: basics
---

## Documentation

### Duplicate Function Detection

The compiler detects duplicate function definitions and reports an error.

#### Same-file duplicates

If the same function name is defined more than once in a single file:

```text
error E3006: file.maxon:5:10: duplicate definition of 'helper' — already declared as `function helper`
```

#### Multiple main functions across files

In a multi-file build, if more than one file defines a `main` function:

```text
error E3006: b.maxon:1:10: duplicate definition of 'main' — already declared as `function main`
```

#### One declaration per name

Maxon has no overloading. A function name is declared once per scope, a method name once per type, and a
requirement name once per interface. A `static` method and an instance method of one name are two
declarations of that name, and so are a method of a type and a method an `extension` of that type adds. A
field and a method of one type never share a name. The second declaration, in source order, is the one
refused, at its name:

```text
error E3006: file.maxon:9:10: duplicate definition of 'helper' — already declared as `function helper`
error E3006: file.maxon:13:11: Duplicate method 'add' in type 'Counter'
error E3006: file.maxon:9:11: 'total' names both a field and a method of type 'Counter'
error E3006: file.maxon:4:11: Duplicate requirement 'area' in interface 'Shape'
```

Declarations in mutually exclusive `#if` branches are one declaration, and so are not refused.

#### No main function

Every program must have a `main` function. This is tested in the `basics` spec.

## Tests

<!-- test: error.same-file-duplicate -->
```maxon

typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 1
end 'helper'

function helper() returns Integer
	return 2
end 'helper'

function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3006: specs/duplicate-functions/error.same-file-duplicate.maxon:9:10: duplicate definition of 'helper' — already declared as `function helper`
```

<!-- test: error.same-file-duplicate-main -->
```maxon
function main() returns ExitCode
	return 0
end 'main'

function main() returns ExitCode
	return 1
end 'main'
```
```maxoncstderr
error E3006: specs/duplicate-functions/error.same-file-duplicate-main.maxon:6:10: duplicate definition of 'main' — already declared as `function main`
```

<!-- test: error.multi-file-duplicate-main -->
```maxon
// --- file: a.maxon
function main() returns ExitCode
	return 0
end 'main'

// --- file: b.maxon
function main() returns ExitCode
	return 1
end 'main'
```
```maxoncstderr
error E3006: specs/duplicate-functions/error.multi-file-duplicate-main.maxon:8:10: duplicate definition of 'main' — already declared as `function main`
```

<!-- test: error.a-duplicate-function-beside-an-undefined-call-in-another-function-reports-both -->
A duplicate declaration is refused where it is declared, and the rest of the program is still checked: the
undefined call in `broken` is independent of the duplicate.
```maxon
function helper() returns ExitCode
	return 1
end 'helper'

function helper() returns ExitCode
	return 2
end 'helper'

function broken() returns ExitCode
	return nothere(7)
end 'broken'

function main() returns ExitCode
	return broken()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:6:10: duplicate definition of 'helper' — already declared as `function helper`
error E3004: <fragment>:11:9: call to undefined function 'nothere'
```

<!-- test: error.two-free-functions-of-one-name-with-different-parameters -->
Maxon has no overloading: a second function of the name is a duplicate whatever its parameters.
```maxon
function helper() returns ExitCode
	return 1
end 'helper'

function helper(extra ExitCode) returns ExitCode
	return extra
end 'helper'

function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:6:10: duplicate definition of 'helper' — already declared as `function helper`
```

<!-- test: error.two-instance-methods-of-one-name-with-different-parameters -->
A method name is declared once per type.
```maxon
type Counter
	let total as ExitCode

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function add() returns ExitCode
		return self.total
	end 'add'

	function add(amount ExitCode) returns ExitCode
		return amount
	end 'add'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	return c.add()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:13:11: Duplicate method 'add' in type 'Counter'
```

<!-- test: error.a-static-and-an-instance-method-of-one-name -->
A `static` method and an instance method of one name are duplicates: the name is declared once per type.
```maxon
type Counter
	let total as ExitCode

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function size() returns ExitCode
		return self.total
	end 'size'

	static function size(seed ExitCode) returns ExitCode
		return seed
	end 'size'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	return c.size()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:13:18: Duplicate method 'size' in type 'Counter'
```

<!-- test: error.a-field-and-a-method-of-one-name -->
A field and a method of one type never share a name.
```maxon
type Counter
	let total as ExitCode

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function total() returns ExitCode
		return 0
	end 'total'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	return c.total()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:9:11: 'total' names both a field and a method of type 'Counter'
```

<!-- test: error.two-fields-of-one-name-in-a-type -->
A field name is declared once per type, as a method name is. The second `payload` is refused where it is declared, and
the literal that supplies `payload` once is not a missing field.
```maxon
type Holder
	export let payload as ExitCode
	export let payload as ExitCode

	static function create() returns Self
		return Self{payload: 1}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	return h.payload
end 'main'
```
```maxoncstderr
error E3006: <fragment>:4:13: Duplicate field 'payload' in type 'Holder'
```

<!-- test: error.two-fields-of-one-name-with-expression-defaults -->
A repeated field with an expression default is refused at the second declaration and nowhere else: the second default
supplies no helper of its own.
```maxon
type Inner
	export let value as ExitCode

	static function create() returns Self
		return Self{value: 3}
	end 'create'
end 'Inner'

type Holder
	export let payload = Inner.create()
	export let payload = Inner.create()

	static function create() returns Self
		return Self{}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	return h.payload.value
end 'main'
```
```maxoncstderr
error E3006: <fragment>:12:13: Duplicate field 'payload' in type 'Holder'
```

<!-- test: error.two-fields-of-one-name-with-expression-defaults-of-different-types -->
The two defaults may have different types; the refusal is still the second declaration alone.
```maxon
type Inner
	export let value as ExitCode

	static function create() returns Self
		return Self{value: 3}
	end 'create'
end 'Inner'

type Other
	export let value as ExitCode

	static function create() returns Self
		return Self{value: 4}
	end 'create'
end 'Other'

type Holder
	export let payload = Inner.create()
	export let payload = Other.create()

	static function create() returns Self
		return Self{}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	return h.payload.value
end 'main'
```
```maxoncstderr
error E3006: <fragment>:20:13: Duplicate field 'payload' in type 'Holder'
```

<!-- test: error.two-free-functions-of-one-name-in-two-files-of-one-directory -->
Two files of one directory declare `module function helper`, which every file of the directory sees: the
second declaration, in `b.maxon`, is refused.
```maxon
// --- file: a.maxon
module function helper() returns ExitCode
	return 1
end 'helper'

// --- file: b.maxon
module function helper() returns ExitCode
	return 2
end 'helper'

// --- file: main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:8:17: duplicate definition of 'helper' — already declared as `function helper`
```

<!-- test: error.two-file-private-functions-of-one-name-and-different-arity-in-one-directory -->
A directory is one scope for free functions whatever their visibility, and a refused duplicate is reported
without being resolved against the declaration that was kept, whose parameter count differs.
```maxon
// --- file: a.maxon
function helper(extra ExitCode) returns ExitCode
	return extra
end 'helper'

export function fromA() returns ExitCode
	return helper(1)
end 'fromA'

// --- file: b.maxon
function helper() returns ExitCode
	return 2
end 'helper'

export function fromB() returns ExitCode
	return helper()
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	print("{fromA()} {fromB()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:12:10: duplicate definition of 'helper' — already declared as `function helper`
```

<!-- test: error.an-extension-method-of-a-name-the-type-already-declares -->
A method an `extension` adds to a type is declared in that type's scope, so a name the type already
declares cannot be added again, whatever the parameters.
```maxon
type Counter
	let total as ExitCode

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function add() returns ExitCode
		return self.total
	end 'add'
end 'Counter'

extension Counter
	function add(amount ExitCode) returns ExitCode
		return amount
	end 'add'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	return c.add()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:15:11: Duplicate method 'add' in type 'Counter'
```

<!-- test: error.two-requirements-of-one-name-in-one-interface -->
```maxon
interface Shape
	function area() returns ExitCode
	function area(scale ExitCode) returns ExitCode
end 'Shape'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:4:11: Duplicate requirement 'area' in interface 'Shape'
```

<!-- test: a-function-and-a-method-of-one-name-in-mutually-exclusive-if-branches-is-one-declaration -->
**THE CONTROL FOR THE CASES ABOVE.** `testing(false)` holds in every build, so each `#if` keeps its first
declaration and drops the second: `pick` and `size` are each declared once, and nothing is refused.
```maxon
#if testing(false)
	function pick() returns ExitCode
		return 21
	end 'pick'
#else
	function pick() returns ExitCode
		return 99
	end 'pick'
#endif

type Counter
	let total as ExitCode

	static function create() returns Self
		return Self{total: 7}
	end 'create'

	#if testing(false)
		function size() returns ExitCode
			return self.total
		end 'size'
	#else
		function size(scale ExitCode) returns ExitCode
			return scale
		end 'size'
	#endif
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	print("{pick()} {c.size()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
21 7
```
