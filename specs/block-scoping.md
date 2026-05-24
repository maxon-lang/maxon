---
feature: block-scoping
status: stable
keywords: [scope, scoping, variable, block, lifetime]
category: semantics
---

## Documentation

### Overview

Variables declared inside a block are scoped to that block. They are not accessible after the block ends. This applies to all block constructs: `if`, `while`, `for`, and `match`.

Loop iterator variables in `for` loops are also scoped to the loop body and are immutable.

### If Blocks

```text
if condition 'check'
  var x = 42
end 'check'
// x is not accessible here
```

### For Loops

The iterator variable is immutable and scoped to the loop body:

```text
var arr = [1, 2, 3]
for item in arr 'loop'
  // item is immutable
  // item = 99  // error: not mutable
end 'loop'
// item is not accessible here
```

### While Loops

```text
while condition 'loop'
  var x = 42
end 'loop'
// x is not accessible here
```

## Tests

<!-- test: for-iterator-immutable -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	for item in arr 'loop'
		item = 99
	end 'loop'
	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/block-scoping/for-iterator-immutable.test:5:3: cannot assign to immutable variable: 'item'
```

<!-- test: for-iterator-not-accessible-after -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	for x in arr 'loop'
		let y = x
	end 'loop'
	return x
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/block-scoping/for-iterator-not-accessible-after.test:7:9: Undefined variable 'x'
```

<!-- test: for-body-var-not-accessible-after -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	for item in arr 'loop'
		let inside = item
	end 'loop'
	return inside
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/block-scoping/for-body-var-not-accessible-after.test:7:9: Undefined variable 'inside'
```

<!-- test: for-destructured-immutable -->
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 32]
	for (key, value) in m 'loop'
		value = 99
	end 'loop'
	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/block-scoping/for-destructured-immutable.test:5:3: cannot assign to immutable variable: 'value'
```

<!-- test: for-destructured-not-accessible-after -->
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 32]
	for (key, value) in m 'loop'
		let sum = key + value
	end 'loop'
	return key
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/block-scoping/for-destructured-not-accessible-after.test:7:9: Undefined variable 'key'
```

<!-- test: if-body-var-not-accessible-after -->
```maxon
function main() returns ExitCode
	if true 'check'
		let x = 42
	end 'check'
	return x
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/block-scoping/if-body-var-not-accessible-after.test:6:9: Undefined variable 'x'
```

<!-- test: if-else-body-var-not-accessible-after -->
```maxon
function main() returns ExitCode
	if false 'check'
		let x = 10
	end 'check' else 'other'
		let y = 20
	end 'other'
	return y
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/block-scoping/if-else-body-var-not-accessible-after.test:8:9: Undefined variable 'y'
```

<!-- test: while-body-var-not-accessible-after -->
```maxon
function main() returns ExitCode
	var i = 0
	while i < 3 'loop'
		let x = i
		i = i + 1
	end 'loop'
	return x
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/block-scoping/while-body-var-not-accessible-after.test:8:9: Undefined variable 'x'
```

<!-- test: outer-var-accessible-in-block -->
```maxon
function main() returns ExitCode
	var sum = 0
	let arr = [10, 20, 12]
	for item in arr 'loop'
		sum = sum + item
	end 'loop'
	return sum
end 'main'
```
```exitcode
42
```

<!-- test: if-scope-cleanup -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var outer = IntArray.create()
	outer.resize(3)
	try outer.set(0, value: 10) otherwise panic("test invariant: set OOB")
	if true 'block'
		var inner = IntArray.create()
		inner.resize(5)
		try inner.set(0, value: 20) otherwise panic("test invariant: set OOB")
	end 'block'
	return try outer.get(0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: var-redeclare-after-scope -->
```maxon
function main() returns ExitCode
	if true 'check'
		let x = 10
	end 'check'
	let x = 42
	return x
end 'main'
```
```exitcode
42
```

<!-- test: local-shadows-prior-field-access -->
### Local declared after a non-self field access on the same name
A field access like `foo.name` in an outer loop must not shadow a later
local declaration `let name = ...` in a sibling block. Regression: the
Maxon→Standard pass installed `varNameToStructPrefix["name"]` from the
field access and the snapshot/restore semantics preserved that mapping
across blocks, so subsequent references to the local `name` resolved to
the field-access tempvar holding stale data from the outer loop. Hits
when the local is declared in a `try` success path and used in a sibling
sub-block — the shape that fired in MirToWasm.emitWasmModule.
```maxon
typealias Kind = int(0 to 100)
typealias StringArray = Array with String

type Foo
	export var name as String
	export var kind as Kind

	export static function create(s String, k Kind) returns Foo
		return Foo{name: s, kind: k}
	end 'create'
end 'Foo'

typealias FooArray = Array with Foo

function useTwo(s String, k Kind)
	print("two: '{s}' k={k}\n")
end 'useTwo'

function useOne(s String)
	print("one: '{s}'\n")
end 'useOne'

function repro(foos FooArray, names StringArray)
	for foo in foos 'fooLoop'
		useTwo(foo.name, k: foo.kind)
	end 'fooLoop'

	for i in 0 upto names.count() 'nameLoop'
		let name = try names.get(i) otherwise panic("get failed at {i}")
		let s2 = try names.get(i) otherwise panic("get2 failed at {i}")
		useOne(name)
		useOne(s2)
	end 'nameLoop'
end 'repro'

function main() returns ExitCode
	var foos = FooArray.create()
	foos.push(Foo.create("outer1", k: 1))
	foos.push(Foo.create("outer2", k: 2))
	var names = StringArray.create()
	names.push("inner1")
	names.push("inner2")
	repro(foos, names: names)
	return 0
end 'main'
```
```stdout
two: 'outer1' k=1
two: 'outer2' k=2
one: 'inner1'
one: 'inner1'
one: 'inner2'
one: 'inner2'
```
```exitcode
0
```
