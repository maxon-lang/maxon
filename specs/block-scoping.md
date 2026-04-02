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
	var arr = [10, 20, 30]
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
	var arr = [10, 20, 30]
	for x in arr 'loop'
		var y = x
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
	var arr = [10, 20, 30]
	for item in arr 'loop'
		var inside = item
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
	var m = [1: 10, 2: 32]
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
	var m = [1: 10, 2: 32]
	for (key, value) in m 'loop'
		var sum = key + value
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
		var x = 42
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
		var x = 10
	end 'check' else 'other'
		var y = 20
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
		var x = i
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
	var arr = [10, 20, 12]
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
	outer.set(0, value: 10)
	if true 'block'
		var inner = IntArray.create()
		inner.resize(5)
		inner.set(0, value: 20)
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
		var x = 10
	end 'check'
	var x = 42
	return x
end 'main'
```
```exitcode
42
```
