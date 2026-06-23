---
feature: vector
status: experimental
keywords: [vector, fixed size, stack, collection, generic]
category: stdlib
---

# Vector

## Documentation

### Overview

`Vector` is a generic fixed-size collection. 

### Creating Vectors

Create a concrete vector type using `typealias` with element type and size:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int
var v = Vec3.create()  // zero-initialized, 3 elements on the stack
```

The size is part of the type. A `Vector with 3 Int` is a different type from `Vector with 4 Int`.

### Creating from Array Literals

Vectors implement `BuiltinArrayLiteral`, so you can initialize them from an array literal using `from`. The element type and size are inferred from the literal:

```text
var v = Vector from [10, 20, 30]  // inferred as Vector with 3 Int
```

The inferred type is compatible with a typealias of the same element type and size, so a `Vector from [...]` can be passed to a function expecting the typealias:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function process(v Vec3) returns Int
  return try v.get(0) otherwise 0
end 'process'

var v = Vector from [10, 20, 30]
process(v)  // works — inferred type matches Vec3
```

### Element Access

Access elements with `.get()`:

```text
var value = try v.get(0) otherwise 0
```

Modify elements with `.set()`:

```text
v.set(0, value: 42)
```

### Size and Count

The `.count()` method always returns the fixed size of the vector:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int
var v = Vec4.create()
var n = v.count()  // always 4
```

### Stack vs Heap

Vectors are designed for small, fixed-size data. The compiler places the storage on the stack when the total byte size (element size x count) is 8192 bytes or less. Larger vectors are automatically heap-allocated.

```text
typealias Int = int(i64.min to i64.max)
typealias SmallVec = Vector with 100 Int    // 800 bytes → stack
typealias LargeVec = Vector with 2000 Int   // 16000 bytes → heap
```

### Use Cases

Vectors are ideal for:
- Small fixed-size collections (coordinates, colors, matrices)
- Performance-sensitive code where heap allocation is undesirable
- Types with a known compile-time size

```text
typealias Float = float(f64.min to f64.max)
typealias Byte = int(0 to u8.max)
typealias Point3D = Vector with 3 Float
typealias Color = Vector with 4 Byte      // RGBA
typealias Mat2x2 = Vector with 4 Float    // 2x2 matrix stored flat
```

### Iteration

Vectors support `for-in` loops:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int
var v = Vec3.create()
v.set(0, value: 10)
v.set(1, value: 20)
v.set(2, value: 30)

for elem in v 'loop'
  print("{elem}")
end 'loop'
```

## Tests

<!-- test: create-zero-initialized -->
<!-- SelfhostedOnly -->
This test pins a `RequiredIR:x64-windows` block in the self-hosted compiler's single-section format (its own instruction selection for the `try/otherwise` default value and inlined `Vector.get`). The C# bootstrap emits structurally different multi-section IR for the same source, so this test is owned by the self-hosted suite.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	let v = Vec3.create()
	return try v.get(0) otherwise -1
end 'main'
```
```exitcode
0
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.lea rcx, [rip+__layout_Vector_N3_Int]
    x64.call Vector.create
    x64.mov r12, r8
    x64.mov r13, [r12+0] (8b)
    x64.mov r8d, 3
    x64.mov [r13+16], r8 (8b)
    x64.mov r8d, 8
    x64.mov [r13+24], r8 (8b)
    x64.mov ecx, 24
    x64.call mrt_alloc
    x64.mov [r13+0], r8 (8b)
    x64.mov r8d, 3
    x64.mov [r13+8], r8 (8b)
    x64.xor edx, edx
  inlined_Vector.get_0_0:
    x64.mov rcx, [r12+0] (8b)
    x64.call stdlib.__managed_mem_get
    x64.mov r13, r8
    x64.test rdx, rdx
    x64.je inlined_Vector.get_3_0
  inlined_Vector.get_1_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov edx, 1
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_0
  inlined_Vector.get_3_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.xor edx, edx
    x64.mov r8, r13
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.mov r8, -1
  try_0.merge:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_28da985da273e42e]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=32
    arm64.adrp_add_rdata x0, __layout_Vector_N3_Int
    arm64.ldr x0, [x29, #16]
    arm64.bl Vector.create
    arm64.mov x19, x0
    arm64.ldr x20, [x19, #0] (8b)
    arm64.mov x0, #3
    arm64.str x0, [x20, #16] (8b)
    arm64.mov x0, #8
    arm64.str x0, [x20, #24] (8b)
    arm64.mov x0, #24
    arm64.bl mrt_alloc
    arm64.str x0, [x20, #0] (8b)
    arm64.mov x0, #3
    arm64.str x0, [x20, #8] (8b)
    arm64.mov x1, #0
  inlined_Vector.get_0_0:
    arm64.ldr x0, [x19, #0] (8b)
    arm64.bl stdlib.__managed_mem_get
    arm64.mov x20, x0
    arm64.mov x0, x1
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x0, ne
    arm64.b.eq inlined_Vector.get_3_0
  inlined_Vector.get_1_0:
    arm64.mov x0, x19
    arm64.bl __mm_decref_maybenull_helper
    arm64.mov x1, #1
    arm64.mov x0, #0
    arm64.b inline_cont_main_0
  inlined_Vector.get_3_0:
    arm64.mov x0, x19
    arm64.bl __mm_decref_maybenull_helper
    arm64.mov x1, #0
    arm64.mov x0, x20
  inline_cont_main_0:
    arm64.mov x2, #0
    arm64.cmp x1, x2
    arm64.cset x1, ne
    arm64.b.eq try_0.merge
  try_0.otherwise:
    arm64.mov x0, #-1
  try_0.merge:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_28da985da273e42e
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
    arm64.ret
  }
}

```

<!-- test: count -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	return v.count()
end 'main'
```
```exitcode
4
```

<!-- test: set-and-get -->
<!-- SelfhostedOnly -->
This test pins a `RequiredIR:x64-windows` block in the self-hosted compiler's single-section format (its own instruction selection for the inlined `Vector.set`/`Vector.get` and `try/otherwise`). The C# bootstrap emits structurally different multi-section IR for the same source, so this test is owned by the self-hosted suite.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(0, value: 42) otherwise panic("test invariant: set OOB")
	return try v.get(0) otherwise 0
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.lea rcx, [rip+__layout_Vector_N3_Int]
    x64.call Vector.create
    x64.mov r12, r8
    x64.mov r13, [r12+0] (8b)
    x64.mov r8d, 3
    x64.mov [r13+16], r8 (8b)
    x64.mov r8d, 8
    x64.mov [r13+24], r8 (8b)
    x64.mov ecx, 24
    x64.call mrt_alloc
    x64.mov [r13+0], r8 (8b)
    x64.mov r8d, 3
    x64.mov [r13+8], r8 (8b)
    x64.mov eax, 42
    x64.xor r13d, r13d
  inlined_Vector.set_0_0:
    x64.mov rcx, [r12+0] (8b)
    x64.mov rdx, r13
    x64.call stdlib.__managed_mem_set
    x64.test rdx, rdx
    x64.je inlined_Vector.set_3_0
  inlined_Vector.set_1_0:
    x64.mov edx, 1
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_0
  inlined_Vector.set_3_0:
    x64.xor edx, edx
    x64.xor r8d, r8d
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je inlined_Vector.get_0_0
  try_0.otherwise:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.lea r12, [rip+__panic_msg_8ba9c6a39785d9d9]
    x64.mov rcx, r12
    x64.call mrt_panic
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  inlined_Vector.get_0_0:
    x64.mov rcx, [r12+0] (8b)
    x64.mov rdx, r13
    x64.call stdlib.__managed_mem_get
    x64.mov r14, r8
    x64.test rdx, rdx
    x64.je inlined_Vector.get_3_0
  inlined_Vector.get_1_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov edx, 1
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_1
  inlined_Vector.get_3_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.xor edx, edx
    x64.mov r8, r14
  inline_cont_main_1:
    x64.test rdx, rdx
    x64.je try_1.ok
    x64.mov r8, r13
    x64.jmp try_1.merge
  try_1.ok:
  try_1.merge:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_1a2166fd7fc0a172]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=48
    arm64.adrp_add_rdata x0, __layout_Vector_N3_Int
    arm64.ldr x0, [x29, #16]
    arm64.bl Vector.create
    arm64.mov x19, x0
    arm64.ldr x20, [x19, #0] (8b)
    arm64.mov x0, #3
    arm64.str x0, [x20, #16] (8b)
    arm64.mov x0, #8
    arm64.str x0, [x20, #24] (8b)
    arm64.mov x0, #24
    arm64.bl mrt_alloc
    arm64.str x0, [x20, #0] (8b)
    arm64.mov x0, #3
    arm64.str x0, [x20, #8] (8b)
    arm64.mov x2, #42
    arm64.mov x20, #0
  inlined_Vector.set_0_0:
    arm64.ldr x0, [x19, #0] (8b)
    arm64.mov x1, x20
    arm64.bl stdlib.__managed_mem_set
    arm64.mov x0, x1
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x0, ne
    arm64.b.eq inlined_Vector.set_3_0
  inlined_Vector.set_1_0:
    arm64.mov x1, #1
    arm64.mov x0, #0
    arm64.b inline_cont_main_0
  inlined_Vector.set_3_0:
    arm64.mov x1, #0
    arm64.mov x0, #0
  inline_cont_main_0:
    arm64.cmp x1, x20
    arm64.cset x0, ne
    arm64.b.eq inlined_Vector.get_0_0
  try_0.otherwise:
    arm64.mov x0, x19
    arm64.bl __mm_decref_maybenull_helper
    arm64.adrp_add_rdata x0, __panic_msg_8ba9c6a39785d9d9
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
    arm64.mov x0, x20
    arm64.epilogue
    arm64.ret
  inlined_Vector.get_0_0:
    arm64.ldr x0, [x19, #0] (8b)
    arm64.mov x1, x20
    arm64.bl stdlib.__managed_mem_get
    arm64.mov x21, x0
    arm64.mov x0, x1
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x0, ne
    arm64.b.eq inlined_Vector.get_3_0
  inlined_Vector.get_1_0:
    arm64.mov x0, x19
    arm64.bl __mm_decref_maybenull_helper
    arm64.mov x1, #1
    arm64.mov x0, #0
    arm64.b inline_cont_main_1
  inlined_Vector.get_3_0:
    arm64.mov x0, x19
    arm64.bl __mm_decref_maybenull_helper
    arm64.mov x1, #0
    arm64.mov x0, x21
  inline_cont_main_1:
    arm64.cmp x1, x20
    arm64.cset x1, ne
    arm64.b.eq try_1.ok
    arm64.mov x0, x20
    arm64.b try_1.merge
  try_1.ok:
  try_1.merge:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_1a2166fd7fc0a172
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
    arm64.ret
  }
}

```

<!-- test: set-all-elements -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 30) otherwise panic("test invariant: set OOB")
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	let c = try v.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: get-out-of-bounds -->
Accessing an index beyond the fixed size throws ArrayError.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec2 = Vector with 2 Int

function main() returns ExitCode
	var v = Vec2.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	let result = try v.get(5) otherwise -1
	print("{result}\n")
	return 0
end 'main'
```
```stdout
-1
```

<!-- test: set-out-of-bounds-throws -->
Setting an out-of-bounds index throws ArrayError.indexOutOfBounds.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec2 = Vector with 2 Int

function main() returns ExitCode
	var v = Vec2.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(5, value: 99) otherwise 'oob'
		return 7
	end 'oob'
	return try v.get(0) otherwise 0
end 'main'
```
```exitcode
7
```

<!-- test: single-element -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec1 = Vector with 1 Int

function main() returns ExitCode
	var v = Vec1.create()
	try v.set(0, value: 77) otherwise panic("test invariant: set OOB")
	return try v.get(0) otherwise 0
end 'main'
```
```exitcode
77
```

<!-- test: larger-vector -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec10 = Vector with 10 Int

function main() returns ExitCode
	var v = Vec10.create()
	var i = 0
	while i < 10 'fill'
		try v.set(i, value: i * 10) otherwise panic("test invariant: set OOB")
		i = i + 1
	end 'fill'
	let first = try v.get(0) otherwise -1
	let last = try v.get(9) otherwise -1
	return first + last
end 'main'
```
```exitcode
90
```

<!-- test: count-single -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec1 = Vector with 1 Int

function main() returns ExitCode
	var v = Vec1.create()
	return v.count()
end 'main'
```
```exitcode
1
```

<!-- test: overwrite-element -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(1, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 42) otherwise panic("test invariant: set OOB")
	return try v.get(1) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: float-vector -->
```maxon
typealias Float = float(f64.min to f64.max)
typealias Vec2F = Vector with 2 Float

function main() returns ExitCode
	var v = Vec2F.create()
	try v.set(0, value: 2.5) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 3.5) otherwise panic("test invariant: set OOB")
	let a = try v.get(0) otherwise 0.0
	let b = try v.get(1) otherwise 0.0
	return trunc(a + b)
end 'main'
```
```exitcode
6
```

<!-- test: byte-vector -->
```maxon

typealias Byte = int(0 to u8.max)
typealias ByteVec4 = Vector with 4 Byte

function main() returns ExitCode
	var v = ByteVec4.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 30) otherwise panic("test invariant: set OOB")
	try v.set(3, value: 40) otherwise panic("test invariant: set OOB")
	let a = try v.get(0) otherwise 0
	let b = try v.get(3) otherwise 0
	return a + b
end 'main'
```
```exitcode
50
```

<!-- test: pass-to-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec3 = Vector with 3 Integer

function sum(v Vec3) returns Integer
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	let c = try v.get(2) otherwise 0
	return a + b + c
end 'sum'

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 12) otherwise panic("test invariant: set OOB")
	return sum(v)
end 'main'
```
```exitcode
42
```

<!-- test: return-from-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec2 = Vector with 2 Integer

function makeVec(a Integer, b Integer) returns Vec2
	var v = Vec2.create()
	try v.set(0, value: a) otherwise panic("test invariant: set OOB")
	try v.set(1, value: b) otherwise panic("test invariant: set OOB")
	return v
end 'makeVec'

function main() returns ExitCode
	let v = makeVec(30, b: 12)
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	return a + b
end 'main'
```
```exitcode
42
```

<!-- test: iterate -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	try v.set(0, value: 1) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 2) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 3) otherwise panic("test invariant: set OOB")
	try v.set(3, value: 4) otherwise panic("test invariant: set OOB")
	var sum = 0
	for elem in v 'loop'
		sum = sum + elem
	end 'loop'
	return sum
end 'main'
```
```exitcode
10
```

<!-- test: let-vector-read -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function makeVec() returns Vec3
	var v = Vec3.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 12) otherwise panic("test invariant: set OOB")
	return v
end 'makeVec'

function main() returns ExitCode
	let v = makeVec()
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	let c = try v.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
42
```

<!-- test: from-array-literal -->
```maxon
function main() returns ExitCode
	let v = Vector from [10, 20, 30]
	return try v.get(0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: from-array-literal-sum -->
```maxon
function main() returns ExitCode
	let v = Vector from [10, 20, 30]
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	let c = try v.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: from-array-literal-float -->
```maxon
function main() returns ExitCode
	let v = Vector from [1.5, 2.5]
	let a = try v.get(0) otherwise 0.0
	let b = try v.get(1) otherwise 0.0
	return trunc(a + b)
end 'main'
```
```exitcode
4
```

<!-- test: from-array-literal-iterate -->
```maxon
function main() returns ExitCode
	let v = Vector from [1, 2, 3, 4]
	var sum = 0
	for elem in v 'loop'
		sum = sum + elem
	end 'loop'
	return sum
end 'main'
```
```exitcode
10
```

<!-- test: from-array-literal-single -->
```maxon
function main() returns ExitCode
	let v = Vector from [99]
	return try v.get(0) otherwise 0
end 'main'
```
```exitcode
99
```

<!-- test: from-literal-typealias-compatible -->
The inferred type from a literal is compatible with a typealias of the same element type and size.
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec3 = Vector with 3 Integer

function sum(v Vec3) returns Integer
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	let c = try v.get(2) otherwise 0
	return a + b + c
end 'sum'

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 12) otherwise panic("test invariant: set OOB")
	return sum(v)
end 'main'
```
```exitcode
42
```

<!-- test: accumulate-sum -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec5 = Vector with 5 Int

function main() returns ExitCode
	var v = Vec5.create()
	try v.set(0, value: 1) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 2) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 3) otherwise panic("test invariant: set OOB")
	try v.set(3, value: 4) otherwise panic("test invariant: set OOB")
	try v.set(4, value: 5) otherwise panic("test invariant: set OOB")
	var sum = 0
	var i = 0
	while i < v.count() 'loop'
		sum = sum + (try v.get(i) otherwise 0)
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
15
```
