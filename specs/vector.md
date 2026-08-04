---
feature: vector
status: experimental
keywords: [vector, fixed size, stack, collection, generic]
category: stdlib
---

# Vector

## Documentation

### Overview

`Vector` is a generic fixed-size collection. A `Vector with N bool` is bit-packed
(8 elements per byte, `element_size = -1`) exactly like `Array with bool` — a
`Vector with 16 bool` allocates a 2-byte buffer, not 16 — and the packing is
transparent to the `get`/`set`/iterate API. See [bool-bit-packing](bool-bit-packing.md).

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
    x64.mov rbx, r8
    x64.mov r12, [rbx+0] (8b)
    x64.mov r8d, 3
    x64.mov [r12+16], r8 (8b)
    x64.mov r8d, 8
    x64.mov [r12+24], r8 (8b)
    x64.mov ecx, 24
    x64.call mrt_alloc
    x64.mov [r12+0], r8 (8b)
    x64.mov r8d, 3
    x64.mov [r12+8], r8 (8b)
    x64.xor edx, edx
  inlined_Vector.get_0_0:
    x64.mov rcx, [rbx+0] (8b)
    x64.call stdlib.__managed_mem_get
    x64.mov r12, r8
    x64.test rdx, rdx
    x64.je inlined_Vector.get_3_0
  inlined_Vector.get_1_0:
    x64.mov rcx, rbx
    x64.call __mm_decref_maybenull_helper
    x64.mov edx, 1
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_0
  inlined_Vector.get_3_0:
    x64.mov rcx, rbx
    x64.call __mm_decref_maybenull_helper
    x64.xor edx, edx
    x64.mov r8, r12
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
    x64.lea rcx, [rip+__panic_msg_28da985da273e42e]
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

```RequiredIR:wasm32-wasi
module {
  func @main() -> u8 {
  entry:
    %1 = mir.global_addr @__layout_Vector_N3_Int
    %2 = mir.call @Vector.create(%1)
    %3 = mir.load %2, 0 width: qword
    %4 = mir.mov_imm 3 : i64
    mir.store %4, %3, 16 width: qword
    %5 = mir.mov_imm 8 : i64
    mir.store %5, %3, 24 width: qword
    %6 = mir.mov_imm 24 : i64
    %7 = mir.call @mrt_alloc(%6)
    mir.store %7, %3, 0 width: qword
    %8 = mir.mov_imm 3 : i64
    mir.store %8, %3, 8 width: qword
    %10 = mir.mov_imm 0 : i64
    mir.br inlined_Vector.get_0_0()
  inlined_Vector.get_0_0:
    %20 = mir.load %2, 0 width: qword
    %21, %22 = mir.try_call @stdlib.__managed_mem_get(%20, %10)
    %23 = mir.mov_imm 0 : i64
    %24 = mir.cmp ne, %22, %23
    mir.cond_br %24 [then: inlined_Vector.get_1_0(), else: inlined_Vector.get_3_0()]
  inlined_Vector.get_1_0:
    %31 = mir.call @__mm_decref_maybenull_helper(%2)
    %25 = mir.mov_imm 0 : i64
    %26 = mir.mov_imm 1 : i64
    mir.br inline_cont_main_0(%25, %26)
  inlined_Vector.get_3_0:
    %27 = mir.mov_imm 0 : i64
    %30 = mir.call @__mm_decref_maybenull_helper(%2)
    mir.br inline_cont_main_0(%21, %27)
  inline_cont_main_0(%28: i64, %29: i64):
    %14 = mir.mov_imm 0 : i64
    %15 = mir.cmp ne, %29, %14
    mir.cond_br %15 [then: try_0.otherwise(), else: try_0.merge(%28)]
  try_0.otherwise:
    %17 = mir.mov_imm -1 : i64
    mir.br try_0.merge(%17)
  try_0.merge(%19: i64):
    %32 = mir.mov_imm 255 : i64
    %33 = mir.cmp ugt, %19, %32
    mir.cond_br %33 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %34 = mir.global_addr @__panic_msg_28da985da273e42e
    %35 = mir.call @mrt_panic(%34)
  __range_ok_0:
    mir.ret %19
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
    x64.mov rbx, r8
    x64.mov r12, [rbx+0] (8b)
    x64.mov r8d, 3
    x64.mov [r12+16], r8 (8b)
    x64.mov r8d, 8
    x64.mov [r12+24], r8 (8b)
    x64.mov ecx, 24
    x64.call mrt_alloc
    x64.mov [r12+0], r8 (8b)
    x64.mov r8d, 3
    x64.mov [r12+8], r8 (8b)
    x64.mov eax, 42
    x64.xor r12d, r12d
  inlined_Vector.set_0_0:
    x64.mov rcx, [rbx+0] (8b)
    x64.mov rdx, r12
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
    x64.mov rcx, rbx
    x64.call __mm_decref_maybenull_helper
    x64.lea rcx, [rip+__panic_msg_8ba9c6a39785d9d9]
    x64.call mrt_panic
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  inlined_Vector.get_0_0:
    x64.mov rcx, [rbx+0] (8b)
    x64.mov rdx, r12
    x64.call stdlib.__managed_mem_get
    x64.mov r13, r8
    x64.test rdx, rdx
    x64.je inlined_Vector.get_3_0
  inlined_Vector.get_1_0:
    x64.mov rcx, rbx
    x64.call __mm_decref_maybenull_helper
    x64.mov edx, 1
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_1
  inlined_Vector.get_3_0:
    x64.mov rcx, rbx
    x64.call __mm_decref_maybenull_helper
    x64.xor edx, edx
    x64.mov r8, r13
  inline_cont_main_1:
    x64.test rdx, rdx
    x64.je try_1.ok
    x64.mov r8, r12
    x64.jmp try_1.merge
  try_1.ok:
  try_1.merge:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea rcx, [rip+__panic_msg_1a2166fd7fc0a172]
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

```RequiredIR:wasm32-wasi
module {
  func @main() -> u8 {
  entry:
    %0 = mir.mov_imm 0 : i64
    %1 = mir.global_addr @__layout_Vector_N3_Int
    %2 = mir.call @Vector.create(%1)
    %3 = mir.load %2, 0 width: qword
    %4 = mir.mov_imm 3 : i64
    mir.store %4, %3, 16 width: qword
    %5 = mir.mov_imm 8 : i64
    mir.store %5, %3, 24 width: qword
    %6 = mir.mov_imm 24 : i64
    %7 = mir.call @mrt_alloc(%6)
    mir.store %7, %3, 0 width: qword
    %8 = mir.mov_imm 3 : i64
    mir.store %8, %3, 8 width: qword
    %11 = mir.mov_imm 42 : i64
    mir.br inlined_Vector.set_0_0()
  inlined_Vector.set_0_0:
    %33 = mir.load %2, 0 width: qword
    %34, %35 = mir.try_call @stdlib.__managed_mem_set(%33, %0, %11)
    %36 = mir.mov_imm 0 : i64
    %37 = mir.cmp ne, %35, %36
    mir.cond_br %37 [then: inlined_Vector.set_1_0(), else: inlined_Vector.set_3_0()]
  inlined_Vector.set_1_0:
    %38 = mir.mov_imm 0 : i64
    %39 = mir.mov_imm 1 : i64
    mir.br inline_cont_main_0(%38, %39)
  inlined_Vector.set_3_0:
    %40 = mir.mov_imm 0 : i64
    %41 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_0(%40, %41)
  inline_cont_main_0(%42: i64, %43: i64):
    %16 = mir.cmp ne, %43, %0
    mir.cond_br %16 [then: try_0.otherwise(), else: inlined_Vector.get_0_0()]
  try_0.otherwise:
    %55 = mir.call @__mm_decref_maybenull_helper(%2)
    %18 = mir.global_addr @__panic_msg_8ba9c6a39785d9d9
    %19 = mir.call @mrt_panic(%18)
    mir.ret %0
  inlined_Vector.get_0_0:
    %44 = mir.load %2, 0 width: qword
    %45, %46 = mir.try_call @stdlib.__managed_mem_get(%44, %0)
    %47 = mir.mov_imm 0 : i64
    %48 = mir.cmp ne, %46, %47
    mir.cond_br %48 [then: inlined_Vector.get_1_0(), else: inlined_Vector.get_3_0()]
  inlined_Vector.get_1_0:
    %56 = mir.call @__mm_decref_maybenull_helper(%2)
    %49 = mir.mov_imm 0 : i64
    %50 = mir.mov_imm 1 : i64
    mir.br inline_cont_main_3(%49, %50)
  inlined_Vector.get_3_0:
    %51 = mir.mov_imm 0 : i64
    %54 = mir.call @__mm_decref_maybenull_helper(%2)
    mir.br inline_cont_main_3(%45, %51)
  inline_cont_main_3(%52: i64, %53: i64):
    %28 = mir.cmp ne, %53, %0
    mir.cond_br %28 [then: try_1.merge(%0), else: try_1.ok()]
  try_1.ok:
    mir.br try_1.merge(%52)
  try_1.merge(%32: i64):
    %57 = mir.mov_imm 255 : i64
    %58 = mir.cmp ugt, %32, %57
    mir.cond_br %58 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %59 = mir.global_addr @__panic_msg_1a2166fd7fc0a172
    %60 = mir.call @mrt_panic(%59)
  __range_ok_0:
    mir.ret %32
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

<!-- test: bool-vector-cross-byte -->
```maxon
typealias Bits16 = Vector with 16 bool

function main() returns ExitCode
	var v = Bits16.create()
	try v.set(0, value: true) otherwise panic("test invariant: set OOB")
	try v.set(3, value: true) otherwise panic("test invariant: set OOB")
	try v.set(8, value: true) otherwise panic("test invariant: set OOB")
	try v.set(15, value: true) otherwise panic("test invariant: set OOB")
	var count = 0
	var i = 0
	while i < v.count() 'scan'
		let bit = try v.get(i) otherwise false
		if bit 'isSet'
			count = count + 1
		end 'isSet'
		i = i + 1
	end 'scan'
	return count
end 'main'
```
```exitcode
4
```

<!-- test: bool-vector-overwrite-clear -->
```maxon
typealias Bits8 = Vector with 8 bool

function main() returns ExitCode
	var v = Bits8.create()
	try v.set(2, value: true) otherwise panic("test invariant: set OOB")
	try v.set(5, value: true) otherwise panic("test invariant: set OOB")
	try v.set(2, value: false) otherwise panic("test invariant: set OOB")
	let a = try v.get(2) otherwise true
	let b = try v.get(5) otherwise false
	var r = 0
	if not a 'cleared'
		r = r + 1
	end 'cleared'
	if b 'stillSet'
		r = r + 10
	end 'stillSet'
	return r
end 'main'
```
```exitcode
11
```

<!-- test: bool-vector-from-literal -->
```maxon
function main() returns ExitCode
	let v = Vector from [true, false, true, true, false, true, false, false, true]
	var count = 0
	for bit in v 'each'
		if bit 'isSet'
			count = count + 1
		end 'isSet'
	end 'each'
	return count
end 'main'
```
```exitcode
5
```

## The Size Is Part of the Type

A `Vector with 3 Int` and a `Vector with 4 Int` are two types, wherever they are reached from: a
declared alias, a field of a generic type, or a synthesized instance nothing has named.

<!-- test: capacity-is-part-of-instance-identity -->
The size is part of the type, so a generic type's capacity-4 field must keep that capacity
rather than adopting a declared `Vector with 3` alias that happens to share its element type.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

typealias IntHolder = Holder with Int

function main() returns ExitCode
	var v = Vec3.create()
	var h = IntHolder.create()
	return v.count() + h.size()
end 'main'
```
```exitcode
7
```

<!-- test: distinct-capacities-are-distinct-instances -->
Two generic types whose fields differ only in capacity must not collapse onto one instance,
even when nothing in the project declares a name for either.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder4 uses Element
	typealias Slot4 = Vector with 4 Element

	var quad as Slot4

	export static function create() returns Self
		return Self{quad: Slot4.create()}
	end 'create'

	export function size() returns Int
		return quad.count()
	end 'size'
end 'Holder4'

type Holder7 uses Element
	typealias Slot7 = Vector with 7 Element

	var septet as Slot7

	export static function create() returns Self
		return Self{septet: Slot7.create()}
	end 'create'

	export function size() returns Int
		return septet.count()
	end 'size'
end 'Holder7'

typealias IntHolder4 = Holder4 with Int
typealias IntHolder7 = Holder7 with Int

function main() returns ExitCode
	var a = IntHolder4.create()
	var b = IntHolder7.create()
	return a.size() * 10 + b.size()
end 'main'
```
```exitcode
47
```

<!-- test: error.wrong-size-vector-argument -->
The size is part of the type, so a differently-sized vector is not a widening — it is a different
type, and passing one where the other is declared is refused rather than silently accepted.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int
typealias Vec4 = Vector with 4 Int

function wants4(v Vec4) returns Int
	return v.count()
end 'wants4'

function main() returns ExitCode
	var v = Vec3.create()
	return wants4(v)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/vector/error.wrong-size-vector-argument.test:12:9: argument type mismatch for 'v': expected 'Vec4', got 'Vec3'
```

<!-- test: same-size-aliases-are-one-type -->
Two names for one size are one type, and stay interchangeable — separating the sizes must not
separate two spellings of the same one.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int
typealias Triple = Vector with 3 Int

function first(v Vec3) returns Int
	return try v.get(0) otherwise 0
end 'first'

function main() returns ExitCode
	var t = Triple.create()
	try t.set(0, value: 21) otherwise panic("test invariant: set OOB")
	return first(t) + t.count()
end 'main'
```
```exitcode
24
```
