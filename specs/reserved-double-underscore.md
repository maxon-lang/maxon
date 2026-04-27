---
feature: reserved-double-underscore
status: stable
keywords: [parse-error, reserved-identifier, naming, compiler-internals]
category: diagnostics
---

# Reserved Double-Underscore Identifiers

## Documentation

Identifiers starting with two underscores (`__`) are reserved for compiler-internal
symbols — runtime intrinsics (`__gt_spawn`, `__chkstk`), built-in types
(`__ManagedMemory`, `__Builtins`), synthetic destructor names (`__destruct_String`),
and parser-generated temporaries (`__discard_*`, `__try_result_*`).

User code MAY still reference these existing internal names (the stdlib does so
extensively to define `String`, `Array`, `Map`, etc.). What user code MAY NOT do is
**declare** a new identifier with that prefix. Any binding site that introduces a
fresh `__`-prefixed name — function, type, typealias, field, parameter, local
variable, enum case, match binding — is a compile-time error.

The check fires at the parser, before any later stage runs, so the error message
points at the offending name's source location.

### Error Example

```maxon
function main() returns ExitCode
	let __value = 5
	return __value
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/docs-example-1.test:3:6: identifier '__value' is reserved: declarations starting with '__' are reserved for compiler internals
```

### Why This Matters

Without the check, user code could collide with runtime symbols (`__gt_spawn`)
or shadow builtin types (`__ManagedMemory`), producing confusing link-time
failures or silently breaking memory management invariants. Reserving the prefix
makes the boundary between user identifiers and compiler internals explicit.

## Tests

<!-- test: let-declaration -->
```maxon
function main() returns ExitCode
	let __foo = 5
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/let-declaration.test:3:6: identifier '__foo' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: var-declaration -->
```maxon
function main() returns ExitCode
	var __counter = 0
	__counter = __counter + 1
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/var-declaration.test:3:6: identifier '__counter' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: function-declaration -->
```maxon
function __helper() returns ExitCode
	return 0
end '__helper'

function main() returns ExitCode
	return __helper()
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/function-declaration.test:2:10: identifier '__helper' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: function-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)

function id(__x Integer) returns Integer
	return __x
end 'id'

function main() returns ExitCode
	_ = id(0)
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/function-parameter.test:4:13: identifier '__x' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: type-declaration -->
```maxon
type __Hidden
	export var x int
end '__Hidden'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/type-declaration.test:2:6: identifier '__Hidden' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: type-field -->
```maxon
type Point
	export var __x int
	export var y int
end 'Point'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/type-field.test:3:13: identifier '__x' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: typealias-declaration -->
```maxon
typealias __Score = int(0 to 100)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/typealias-declaration.test:2:11: identifier '__Score' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: enum-case -->
```maxon
enum Color
	red
	__green
	blue
end 'Color'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/enum-case.test:4:2: identifier '__green' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: closure-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)

function apply(f Integer, n Integer) returns Integer
	return f + n
end 'apply'

function main() returns ExitCode
	let f = (__n Integer) gives __n + 1
	_ = f(0)
	return 0
end 'main'
```
```maxoncstderr
error E2051: specs/fragments/reserved-double-underscore/closure-parameter.test:9:11: identifier '__n' is reserved: declarations starting with '__' are reserved for compiler internals
```
