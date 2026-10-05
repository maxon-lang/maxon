---
feature: codegen-internals
status: selfhosted
keywords: [rdata, cow, managed-memory, strings, stack-probing, signedness, width, i32, f32]
category: dev
---

## Documentation

### Stack Probing

On Windows x64, functions with stack allocations exceeding 4KB (one page) require stack probing via `__chkstk`. Without it, a large `sub rsp, N` can skip multiple guard pages and crash.

**Note:** The `stack-probing-large-struct-recursive` test is a runtime-execution test that requires allocating a struct with 2000 fields (16KB) and calling it recursively. This test verifies the program does not crash — it cannot be expressed as an IR or rdata check. It is documented here but should be tested programmatically.

### Managed Memory

Heap-allocated arrays require automatic cleanup (`maxon_free`) when they go out of scope. The compiler inserts heap management operations:

- `maxon_free` — cleanup at scope exit
- `maxon_realloc` — array growth (e.g., in loops)
- `maxon_alloc` — heap allocation for mutable arrays

### Rdata and Copy-on-Write

Constant array literals (declared with `let`) are stored in the `.rdata` section and accessed via `lea_rdata`. When a mutable copy is needed (e.g., `var` + mutation), copy-on-write allocates a heap copy. Non-constant arrays (containing variables) go directly to heap.

### Managed Strings

String literals are stored in `.rdata` with null termination. The compiler handles:

- Heap string cleanup at scope exit
- Reassignment (old value cleanup)
- Substring slicing (retains parent reference)
- SSO (small string optimization) for short strings
- Loop concatenation with intermediate cleanup
- Literal deduplication (identical strings share `.rdata` entries)

## Tests

<!-- test: stack-probing-large-struct -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias BigVec = Vector with 2048 Integer
typealias Depth = int(-1 to 50)

function recurse(n Depth) returns Depth
	var v = BigVec.create()
	try v.set(2047, value: n as Integer) otherwise panic("test invariant: set OOB")
	if n <= 0 'base'
		return 0
	end 'base'
	return recurse(n - 1)
end 'recurse'

function main() returns ExitCode
	return recurse(50)
end 'main'
```
```exitcode
0
```

<!-- test: managed-memory-heap-array-generates-free -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	return arr.count()
end 'main'
```
```exitcode
2
```

<!-- test: managed-memory-scope-cleanup-generates-free -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	if true 'outer'
		var outer_arr = IntArray.create()
		outer_arr.push(100)
		if true 'inner'
			var inner_arr = IntArray.create()
			inner_arr.push(200)
		end 'inner'
	end 'outer'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: managed-memory-loop-growth-generates-realloc -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	var i = 0
	while i < 10 'loop'
		arr.push(i)
		i = i + 1
	end 'loop'
	return arr.count()
end 'main'
```
```exitcode
10
```

<!-- test: managed-memory-fixed-size-array-literal-cleanup -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: rdata-non-constant-array-uses-heap -->
```maxon
function main() returns ExitCode
	let x = 5
	let arr = [1, x, 3]
	return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
5
```

<!-- test: rdata-dead-global-array-no-init-code -->
```maxon
let unusedTable = [100, 200, 300, 400]

function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: managed-string-loop-concatenation-cleanup -->
```maxon
function main() returns ExitCode
	var s = ""
	let a = "a"
	var i = 0
	while i < 5 'loop'
		s.append(a)
		i = i + 1
	end 'loop'
	return s.byteLength()
end 'main'
```
```exitcode
5
```

<!-- test: i32-unsigned-add -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
	let a = 10 as SmallInt
	let b = 3 as SmallInt
	return a + b
end 'main'
```
```exitcode
13
```

<!-- test: i32-unsigned-div -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
	let a = 20 as SmallInt
	let b = 3 as SmallInt
	return a / b
end 'main'
```
```exitcode
6
```

<!-- test: i32-signed-div -->
```maxon
typealias Temp = int(-100000 to 100000)

function main() returns ExitCode
	let a = 20 as Temp
	let b = 3 as Temp
	return a / b
end 'main'
```
```exitcode
6
```

<!-- test: i32-unsigned-cmp -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
	let a = 10 as SmallInt
	let b = 3 as SmallInt
	if a > b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: i32-unsigned-mod -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
	let a = 20 as SmallInt
	let b = 3 as SmallInt
	return a mod b
end 'main'
```
```exitcode
2
```

<!-- test: i64-signed-no-narrowing -->
```maxon
typealias BigInt = int(-1000000000000 to 1000000000000)

function main() returns ExitCode
	let a = 20 as BigInt
	let b = 3 as BigInt
	return a / b
end 'main'
```
```exitcode
6
```

<!-- test: i8-range-uses-i32-arithmetic -->
```maxon
typealias Tiny = int(0 to 100)

function main() returns ExitCode
	let a = 21 as Tiny
	let b = 3 as Tiny
	return a / b
end 'main'
```
```exitcode
7
```

<!-- test: f32-arithmetic-uses-ss-instructions -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let a = 10.0 as F
	let b = 3.0 as F
	return trunc(a + b)
end 'main'
```
```exitcode
13
```

<!-- test: f32-comparison-uses-ucomiss -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let a = 3.0 as F
	let b = 5.0 as F
	if a < b 'less'
		return 1
	end 'less'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: f32-truncation-uses-cvttss2si -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let a = 42.9 as F
	return trunc(a)
end 'main'
```
```exitcode
42
```

