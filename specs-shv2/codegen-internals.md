---
feature: codegen-internals
status: selfhosted
status-reason: 11 of its 32 cases fail here on whole-module RequiredIR blocks written in v1's dump format, which this runner's section comparer cannot read; the other 21 pass (measured 2026-08-06, BATCH29/A3a). shv2 runs 19 of the 32 as authored and needs ByteArray, a `.rdata` typealias surface and per-stage IR pins for the rest, so porting it is a rung of its own.
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

<!-- test: rdata-constant-array-uses-rdata -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```
```RequiredRdata
i64[] 10, 20, 30
```

<!-- test: rdata-bool-array-bit-packed -->
```maxon
function main() returns ExitCode
	let arr = [true, false, true, false]
	let v0 = try arr.get(0) otherwise false
	let v1 = try arr.get(1) otherwise true
	let v2 = try arr.get(2) otherwise false
	let v3 = try arr.get(3) otherwise true
	var sum = 0
	if v0 'c0'
		sum = sum + 1
	end 'c0'
	if v1 'c1'
		sum = sum + 1
	end 'c1'
	if v2 'c2'
		sum = sum + 1
	end 'c2'
	if v3 'c3'
		sum = sum + 1
	end 'c3'
	return sum
end 'main'
```
```exitcode
2
```
```RequiredRdata
i8[] 5
```

<!-- test: rdata-byte-array-uses-i8 -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let arr = [10 as Byte, 20 as Byte, 30 as Byte]
	let v0 = try arr.get(0) otherwise 0 as Byte
	let v1 = try arr.get(1) otherwise 0 as Byte
	let v2 = try arr.get(2) otherwise 0 as Byte
	return v0 + v1 + v2
end 'main'
```
```exitcode
60
```
```RequiredRdata
i8[] 10, 20, 30
```

<!-- test: rdata-typealias-byte-array-uses-i8 -->
```maxon

typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let arr = ByteArray from [10, 20, 30]
	let v0 = try arr.get(0) otherwise 0 as Byte
	let v1 = try arr.get(1) otherwise 0 as Byte
	let v2 = try arr.get(2) otherwise 0 as Byte
	return v0 + v1 + v2
end 'main'
```
```exitcode
60
```
```RequiredRdata
i8[] 10, 20, 30
```

<!-- test: rdata-cow-mutation-copies-to-heap -->
```maxon
function main() returns ExitCode
	var arr = [42]
	try arr.set(0, value: 77) otherwise panic("test invariant: set OOB")
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
77
```
```RequiredRdata
i64 42
```

<!-- test: rdata-cow-multiple-mutations -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3]
	try arr.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try arr.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try arr.set(2, value: 30) otherwise panic("test invariant: set OOB")
	var sum = 0
	sum = sum + (try arr.get(0) otherwise 0)
	sum = sum + (try arr.get(1) otherwise 0)
	sum = sum + (try arr.get(2) otherwise 0)
	return sum
end 'main'
```
```exitcode
60
```
```RequiredRdata
i64[] 1, 2, 3
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

<!-- test: rdata-global-let-array-uses-rdata -->
```maxon
let globalArr = [10, 20, 30]

function main() returns ExitCode
	return try globalArr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```
```RequiredRdata
i64[] 10, 20, 30
```

<!-- test: rdata-global-var-array-cow -->
```maxon
var globalArr = [1, 2, 3]

function main() returns ExitCode
	try globalArr.set(0, value: 42) otherwise panic("test invariant: set OOB")
	return try globalArr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```
```RequiredRdata
i64[] 1, 2, 3
```

<!-- test: rdata-global-var-array-cow-preserves-original -->
```maxon
typealias Integer = int(i64.min to i64.max)

var globalArr = [10, 20, 30]

function readFirst() returns Integer
	return try globalArr.get(0) otherwise 0
end 'readFirst'

function main() returns ExitCode
	try globalArr.set(0, value: 99) otherwise panic("test invariant: set OOB")
	return readFirst()
end 'main'
```
```exitcode
99
```
```RequiredRdata
i64[] 10, 20, 30
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

<!-- test: managed-string-heap-string-generates-cleanup -->
```maxon
function main() returns ExitCode
	let s = "this is a heap allocated string!"
	return s.byteLength()
end 'main'
```
```exitcode
32
```
```RequiredRdata
utf8 "this is a heap allocated string!\0"
```

<!-- test: managed-string-reassignment-handles-old-value -->
```maxon
function main() returns ExitCode
	var s = "first heap allocated value!!"
	s = "second heap allocated here!!"
	return s.byteLength()
end 'main'
```
```exitcode
28
```
```RequiredRdata
utf8 "first heap allocated value!!\0"
utf8 "second heap allocated here!!\0"
```

<!-- test: managed-string-print-heap-string -->
```maxon
function main() returns ExitCode
	let s = "heap allocated string here!!"
	return s.byteLength()
end 'main'
```
```exitcode
28
```
```RequiredRdata
utf8 "heap allocated string here!!\0"
```

<!-- test: managed-string-short-string-sso -->
```maxon
function main() returns ExitCode
	let s = "short"
	return s.byteLength()
end 'main'
```
```exitcode
5
```
```RequiredRdata
utf8 "short\0"
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

<!-- test: managed-string-literal-deduplication -->
```maxon
function main() returns ExitCode
	let a = "hello world"
	let b = "hello world"
	let c = "hello world"
	return a.byteLength() + b.byteLength() + c.byteLength()
end 'main'
```
```exitcode
33
```
```RequiredRdata
utf8 "hello world\0"
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

