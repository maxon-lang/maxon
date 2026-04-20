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
typealias Byte = byte(0 to u8.max)
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
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.call @Vec3.create
    maxon.assign %0 {var = __call_tmp_0} {decl = 1 : i1}
    maxon.assign %0 {var = v} {decl = 1 : i1}
    %1 = maxon.struct_var_ref v
    %2 = maxon.literal {value = 0 : i64}
    %5, %4 = maxon.try_call @Vec3.get %1, %2
    %6 = maxon.literal {value = -1 : i64}
    maxon.assign %6 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %5 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 0 : i64}
    %8 = maxon.binop %4, %7 {op = ne}
    maxon.cond_br %8 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %9 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %9 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %10 = maxon.var_ref {var = __try_result_0} {type = i64}
    %11 = maxon.literal {value = 0 : i64}
    %12 = maxon.binop %10, %11 {op = lt}
    %13 = maxon.literal {value = 4294967295 : i64}
    %14 = maxon.binop %10, %13 {op = gt}
    %15 = maxon.binop %12, %14 {op = or}
    maxon.cond_br %15 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    maxon.panic "panic at create-zero-initialized.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_4:
    maxon.scope_end [__try_default_1, v, __try_result_0]
    maxon.return %10
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = func.call @Vec3.create
    memref.store %0, v
    %3 = arith.constant {value = 0 : i64}
    %4 = memref.load v : i64
    %5, %6 = func.try_call @Vec3.get %4, %3
    %7 = arith.constant {value = -1 : i64}
    memref.store %7, __try_default_1
    memref.store %5, __try_result_0
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.cmpi ne %6, %8
    cf.cond_br %9 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %10 = memref.load __try_default_1 : i64
    memref.store %10, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %11 = memref.load __try_result_0 : i64
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.cmpi lt %11, %12
    %14 = arith.constant {value = 4294967295 : i64}
    %15 = arith.cmpi gt %11, %14
    %16 = arith.ori1 %13, %15
    cf.cond_br %16 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    %17 = memref.lea_symdata __panic_msg_0
    %18 = std.ptr_to_i64 %17
    std.call_runtime @mrt_panic %18
  __range_ok_4:
    %19 = memref.load v : i64
    std.call_runtime_if_nonnull @mm_decref %19
    func.return %11
  }
  func @__destruct___ManagedMemory_Int(ptr: i64) {
  entry:
    %121 = func.param ptr : StdI64
    memref.store %121, __destr_ptr
    %124 = memref.load __destr_ptr : i64
    %125 = memref.load_indirect %124+16
    %126 = arith.constant {value = -1 : i64}
    %127 = arith.cmpi eq %125, %126
    cf.cond_br %127 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %128 = memref.load __destr_ptr : i64
    %129 = memref.load_indirect %128+32
    std.call_runtime_if_nonnull @mm_decref %129
    cf.br skip_buf_0
  check_owned_0:
    %130 = memref.load __destr_ptr : i64
    %131 = memref.load_indirect %130+16
    %132 = arith.constant {value = -2 : i64}
    %133 = arith.cmpi ne %131, %132
    cf.cond_br %133 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %134 = memref.load __destr_ptr : i64
    %135 = memref.load_indirect %134+0
    std.call_runtime @mm_raw_free %135
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    %136 = func.param ptr : StdI64
    memref.store %136, __destr_ptr
    %137 = memref.load __destr_ptr : i64
    %138 = memref.load_indirect %137+0
    std.call_runtime_if_nonnull @mm_decref %138
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.call Vec3.create
    x64.mov [rbp-8], rax
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.xor edx, edx
    x64.call Vec3.get
    x64.mov rcx, -1
    x64.mov [rbp-16], rcx
    x64.mov [rbp-24], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je main.otherwise_default_continue_3
  otherwise_default_error_2:
    x64.mov rax, [rbp-16]
    x64.mov [rbp-24], rax
    x64.jmp main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x64.mov rax, [rbp-24]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_4
    x64.cmp rax, rcx
    x64.jl main.__range_panic_4
    x64.jmp main.__range_ok_4
  __range_panic_4:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_4:
    x64.mov rbx, [rbp-8]
    x64.test rbx, rbx
    x64.jz __nonnull_skip_0
    x64.mov rcx, [rbp-8]
    x64.call mm_decref
    x64.label __nonnull_skip_0
    x64.mov rax, [rbp-24]
    x64.epilogue
    x64.ret
  }
  func @__destruct___ManagedMemory_Int(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -1
    x64.cmp rcx, rdx
    x64.jne __destruct___ManagedMemory_Int.check_owned_0
  slice_cleanup_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+32]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_2
    x64.call mm_decref
    x64.label __nonnull_skip_2
    x64.jmp __destruct___ManagedMemory_Int.skip_buf_0
  check_owned_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -2
    x64.cmp rcx, rdx
    x64.je __destruct___ManagedMemory_Int.skip_buf_0
  free_buf_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.call mm_raw_free
    x64.jmp __destruct___ManagedMemory_Int.skip_buf_0
  skip_buf_0:
    x64.jmp __destruct___ManagedMemory_Int.done
  done:
    x64.epilogue
    x64.ret
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_3
    x64.call mm_decref
    x64.label __nonnull_skip_3
    x64.jmp __destruct_Vec3.done
  done:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = __arr_0.2} {kind = i64} {decl = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = __arr_0.1} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    maxon.assign %3 {var = __arr_0.0} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.literal {value = 3 : i64}
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.literal {value = 8 : i64}
    %8 = maxon.struct_literal @__ManagedMemory
    %9 = maxon.struct_literal @Vec3
    maxon.assign %9 {var = v} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.struct_var_ref v
    %11 = maxon.literal {value = 0 : i64}
    %14, %13 = maxon.try_call @Vec3.get %10, %11
    %15 = maxon.literal {value = -1 : i64}
    maxon.assign %15 {var = __try_default_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %14 {var = __try_result_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 0 : i64}
    %17 = maxon.binop %13, %16 {op = ne}
    maxon.cond_br %17 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %18 = maxon.var_ref {var = __try_default_2} {type = i64}
    maxon.assign %18 {var = __try_result_1} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %19 = maxon.var_ref {var = __try_result_1} {type = i64}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %19, %20 {op = lt}
    %22 = maxon.literal {value = 4294967295 : i64}
    %23 = maxon.binop %19, %22 {op = gt}
    %24 = maxon.binop %21, %23 {op = or}
    maxon.cond_br %24 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    maxon.panic "panic at create-zero-initialized.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_5:
    maxon.scope_end [v, __try_default_2, __try_result_1]
    maxon.return %19
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, __arr_0.2
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, __arr_0.1
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, __arr_0.0
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.constant {value = 3 : i64}
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.constant {value = 8 : i64}
    %8 = arith.constant {value = 32 : i64}
    %9 = func.ref @__destruct___ManagedMemory
    %10 = std.ptr_to_i64 %9
    %11 = arith.constant {value = 1 : i64}
    %12 = std.call_runtime @mm_alloc %8, %10, %11
    memref.store %12, __struct_8
    %13 = memref.load __struct_8 : i64
    memref.store_indirect %4, %13+0
    %14 = memref.load __struct_8 : i64
    memref.store_indirect %5, %14+8
    %15 = memref.load __struct_8 : i64
    memref.store_indirect %6, %15+16
    %16 = memref.load __struct_8 : i64
    memref.store_indirect %7, %16+24
    %17 = memref.load __struct_8 : i64
    %18 = memref.load_indirect %17+24
    %19 = arith.constant {value = 0 : i64}
    %20 = memref.lea_symdata __mm_panic_element_size_zero
    %21 = std.ptr_to_i64 %20
    std.call_runtime @maxon_bounds_check %19, %18, %21
    %22 = arith.constant {value = 16 : i64}
    %23 = func.ref @__destruct_Vec3
    %24 = std.ptr_to_i64 %23
    %25 = arith.constant {value = 2 : i64}
    %26 = std.call_runtime @mm_alloc %22, %24, %25
    memref.store %26, v
    %27 = memref.load v : i64
    memref.store_indirect %0, %27+0
    %28 = memref.load __struct_8 : i64
    %29 = memref.load v : i64
    memref.store_indirect %28, %29+8
    std.call_runtime @mm_incref %28
    %30 = memref.lea __arr_0
    %31 = std.ptr_to_i64 %30
    %32 = memref.load v : i64
    %33 = memref.load_indirect %32+8
    %34 = memref.load_indirect %33+24
    %35 = arith.constant {value = 3 : i64}
    %36 = arith.muli %35, %34
    %37 = std.call_runtime @mm_raw_alloc %36
    %38 = std.call_runtime @maxon_memcpy %37, %31, %36
    %39 = memref.load v : i64
    %40 = memref.load_indirect %39+8
    memref.store_indirect %37, %40+0
    %41 = arith.constant {value = 3 : i64}
    memref.store_indirect %41, %40+16
    %42 = memref.load v : i64
    std.call_runtime @mm_incref %42
    %43 = arith.constant {value = 0 : i64}
    %44 = memref.load v : i64
    %45, %46 = func.try_call @Vec3.get %44, %43
    %47 = arith.constant {value = -1 : i64}
    memref.store %47, __try_default_2
    memref.store %45, __try_result_1
    %48 = arith.constant {value = 0 : i64}
    %49 = arith.cmpi ne %46, %48
    cf.cond_br %49 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %50 = memref.load __try_default_2 : i64
    memref.store %50, __try_result_1
    cf.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %51 = memref.load __try_result_1 : i64
    %52 = arith.constant {value = 0 : i64}
    %53 = arith.cmpi lt %51, %52
    %54 = arith.constant {value = 4294967295 : i64}
    %55 = arith.cmpi gt %51, %54
    %56 = arith.ori1 %53, %55
    cf.cond_br %56 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    %57 = memref.lea_symdata __panic_msg_0
    %58 = std.ptr_to_i64 %57
    std.call_runtime @maxon_panic %58
  __range_ok_5:
    %59 = memref.load v : i64
    std.call_runtime_if_nonnull @mm_decref %59
    func.return %51
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %90 = func.param ptr : StdI64
    memref.store %90, __destr_ptr
    %93 = memref.load __destr_ptr : i64
    %94 = memref.load_indirect %93+16
    %95 = arith.constant {value = 0 : i64}
    %96 = arith.cmpi ne %94, %95
    cf.cond_br %96 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %97 = memref.load __destr_ptr : i64
    %98 = memref.load_indirect %97+0
    std.call_runtime @mm_raw_free %98
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    %99 = func.param ptr : StdI64
    memref.store %99, __destr_ptr
    %100 = memref.load __destr_ptr : i64
    %101 = memref.load_indirect %100+8
    std.call_runtime_if_nonnull @mm_decref %101
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=176
    arm64.mov x0, #0
    arm64.mov x1, #0
    arm64.str x1, [x29, #-8]
    arm64.mov x2, #0
    arm64.str x2, [x29, #-16]
    arm64.mov x3, #0
    arm64.str x3, [x29, #-24]
    arm64.mov x4, #0
    arm64.mov x5, #3
    arm64.mov x6, #0
    arm64.mov x7, #8
    arm64.adrp_add_func x8, __destruct___ManagedMemory
    arm64.mov x9, x8
    arm64.mov x1, x9
    arm64.mov x0, #32
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-32]
    arm64.ldr x10, [x29, #-32]
    arm64.mov x11, #0
    arm64.str x11, [x10, #0]
    arm64.ldr x12, [x29, #-32]
    arm64.mov x13, #3
    arm64.str x13, [x12, #8]
    arm64.ldr x14, [x29, #-32]
    arm64.mov x15, #0
    arm64.str x15, [x14, #16]
    arm64.ldr x0, [x29, #-32]
    arm64.mov x1, #8
    arm64.str x1, [x0, #24]
    arm64.ldr x0, [x29, #-32]
    arm64.ldr x1, [x0, #24]
    arm64.adrp_add_symdata x0, __mm_panic_element_size_zero
    arm64.mov x2, x0
    arm64.mov x0, #0
    arm64.bl maxon_bounds_check
    arm64.adrp_add_func x0, __destruct_Vec3
    arm64.mov x1, x0
    arm64.mov x0, #16
    arm64.mov x2, #2
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-40]
    arm64.ldr x0, [x29, #-40]
    arm64.mov x1, #0
    arm64.str x1, [x0, #0]
    arm64.ldr x0, [x29, #-32]
    arm64.ldr x1, [x29, #-40]
    arm64.str x0, [x1, #8]
    arm64.bl mm_incref
    arm64.add x0, x29, #-24
    arm64.mov x1, x0
    arm64.ldr x0, [x29, #-40]
    arm64.ldr x2, [x0, #8]
    arm64.ldr x0, [x2, #24]
    arm64.mov x2, #3
    arm64.mul x3, x2, x0
    arm64.str x1, [x29, #-64]
    arm64.str x3, [x29, #-72]
    arm64.ldr x0, [x29, #-72]
    arm64.bl mm_raw_alloc
    arm64.str x0, [x29, #-80]
    arm64.ldr x1, [x29, #-64]
    arm64.ldr x2, [x29, #-72]
    arm64.bl maxon_memcpy
    arm64.ldr x1, [x29, #-40]
    arm64.ldr x2, [x1, #8]
    arm64.ldr x1, [x29, #-80]
    arm64.str x1, [x2, #0]
    arm64.mov x1, #3
    arm64.str x1, [x2, #16]
    arm64.ldr x1, [x29, #-40]
    arm64.str x0, [x29, #-88]
    arm64.ldr x0, [x29, #-40]
    arm64.bl mm_incref
    arm64.ldr x0, [x29, #-40]
    arm64.mov x1, #0
    arm64.bl Vec3.get
    arm64.mov x2, #-1
    arm64.str x2, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_3
    arm64.b main.otherwise_default_continue_4
  otherwise_default_error_3:
    arm64.ldr x0, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.b main.otherwise_default_continue_4
  otherwise_default_continue_4:
    arm64.ldr x0, [x29, #-56]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_5
    arm64.b main.__range_ok_5
  __range_panic_5:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_5:
    arm64.ldr x1, [x29, #-40]
    arm64.cmp x1, #0
    arm64.b.eq main.__skip_guarded_82
    arm64.ldr x0, [x29, #-40]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_82
    arm64.ldr x0, [x29, #-56]
    arm64.epilogue stack_size=176
    arm64.ret
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #0
    arm64.cmp x1, x2
    arm64.cset x3, ne
    arm64.cmp x3, #0
    arm64.b.ne __destruct___ManagedMemory.free_buf_0
    arm64.b __destruct___ManagedMemory.skip_buf_0
  free_buf_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.mov x0, x1
    arm64.bl mm_raw_free
    arm64.b __destruct___ManagedMemory.skip_buf_0
  skip_buf_0:
    arm64.b __destruct___ManagedMemory.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #8]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_Vec3.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_Vec3.__skip_guarded_4
    arm64.b __destruct_Vec3.done
  done:
    arm64.epilogue stack_size=48
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
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.call @Vec3.create
    maxon.assign %0 {var = __call_tmp_0} {decl = 1 : i1}
    maxon.assign %0 {var = v} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.struct_var_ref v
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.literal {value = 42 : i64}
    %4 = maxon.try_call @Vec3.set %1, %2, %3
    maxon.assign %4 {var = __try_error_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = ne}
    maxon.cond_br %6 [then: otherwise_panic_0, else: otherwise_continue_1]
  otherwise_panic_0:
    maxon.panic "panic at set-and-get.test:7: test invariant: set OOB"
  otherwise_continue_1:
    %7 = maxon.struct_var_ref v
    %8 = maxon.literal {value = 0 : i64}
    %11, %10 = maxon.try_call @Vec3.get %7, %8
    %12 = maxon.literal {value = 0 : i64}
    maxon.assign %12 {var = __try_default_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %11 {var = __try_result_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.literal {value = 0 : i64}
    %14 = maxon.binop %10, %13 {op = ne}
    maxon.cond_br %14 [then: otherwise_default_error_5, else: otherwise_default_continue_6]
  otherwise_default_error_5:
    %15 = maxon.var_ref {var = __try_default_4} {type = i64}
    maxon.assign %15 {var = __try_result_3} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_6
  otherwise_default_continue_6:
    %16 = maxon.var_ref {var = __try_result_3} {type = i64}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %16, %17 {op = lt}
    %19 = maxon.literal {value = 4294967295 : i64}
    %20 = maxon.binop %16, %19 {op = gt}
    %21 = maxon.binop %18, %20 {op = or}
    maxon.cond_br %21 [then: __range_panic_7, else: __range_ok_7]
  __range_panic_7:
    maxon.panic "panic at set-and-get.test:8: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_7:
    maxon.scope_end [__try_error_2, v, __try_default_4, __try_result_3]
    maxon.return %16
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = func.call @Vec3.create
    memref.store %0, v
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.constant {value = 42 : i64}
    %5 = memref.load v : i64
    %6 = func.try_call @Vec3.set %5, %3, %4
    %7 = arith.constant {value = 0 : i64}
    %8 = arith.cmpi ne %6, %7
    cf.cond_br %8 [then: otherwise_panic_0, else: otherwise_continue_1]
  otherwise_panic_0:
    %9 = memref.lea_symdata __panic_msg_0
    %10 = std.ptr_to_i64 %9
    std.call_runtime @mrt_panic %10
  otherwise_continue_1:
    %11 = arith.constant {value = 0 : i64}
    %12 = memref.load v : i64
    %13, %14 = func.try_call @Vec3.get %12, %11
    %15 = arith.constant {value = 0 : i64}
    memref.store %15, __try_default_4
    memref.store %13, __try_result_3
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi ne %14, %16
    cf.cond_br %17 [then: otherwise_default_error_5, else: otherwise_default_continue_6]
  otherwise_default_error_5:
    %18 = memref.load __try_default_4 : i64
    memref.store %18, __try_result_3
    cf.br otherwise_default_continue_6
  otherwise_default_continue_6:
    %19 = memref.load __try_result_3 : i64
    %20 = arith.constant {value = 0 : i64}
    %21 = arith.cmpi lt %19, %20
    %22 = arith.constant {value = 4294967295 : i64}
    %23 = arith.cmpi gt %19, %22
    %24 = arith.ori1 %21, %23
    cf.cond_br %24 [then: __range_panic_7, else: __range_ok_7]
  __range_panic_7:
    %25 = memref.lea_symdata __panic_msg_1
    %26 = std.ptr_to_i64 %25
    std.call_runtime @mrt_panic %26
  __range_ok_7:
    %27 = memref.load v : i64
    std.call_runtime_if_nonnull @mm_decref %27
    func.return %19
  }
  func @__destruct___ManagedMemory_Int(ptr: i64) {
  entry:
    %184 = func.param ptr : StdI64
    memref.store %184, __destr_ptr
    %187 = memref.load __destr_ptr : i64
    %188 = memref.load_indirect %187+16
    %189 = arith.constant {value = -1 : i64}
    %190 = arith.cmpi eq %188, %189
    cf.cond_br %190 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %191 = memref.load __destr_ptr : i64
    %192 = memref.load_indirect %191+32
    std.call_runtime_if_nonnull @mm_decref %192
    cf.br skip_buf_0
  check_owned_0:
    %193 = memref.load __destr_ptr : i64
    %194 = memref.load_indirect %193+16
    %195 = arith.constant {value = -2 : i64}
    %196 = arith.cmpi ne %194, %195
    cf.cond_br %196 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %197 = memref.load __destr_ptr : i64
    %198 = memref.load_indirect %197+0
    std.call_runtime @mm_raw_free %198
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    %199 = func.param ptr : StdI64
    memref.store %199, __destr_ptr
    %200 = memref.load __destr_ptr : i64
    %201 = memref.load_indirect %200+0
    std.call_runtime_if_nonnull @mm_decref %201
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.call Vec3.create
    x64.mov [rbp-8], rax
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.xor edx, edx
    x64.mov r8, 42
    x64.call Vec3.set
    x64.xor ecx, ecx
    x64.cmp rdx, rcx
    x64.je main.otherwise_continue_1
  otherwise_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  otherwise_continue_1:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.xor edx, edx
    x64.call Vec3.get
    x64.xor ecx, ecx
    x64.mov [rbp-16], rcx
    x64.mov [rbp-24], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je main.otherwise_default_continue_6
  otherwise_default_error_5:
    x64.mov rax, [rbp-16]
    x64.mov [rbp-24], rax
    x64.jmp main.otherwise_default_continue_6
  otherwise_default_continue_6:
    x64.mov rax, [rbp-24]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_7
    x64.cmp rax, rcx
    x64.jl main.__range_panic_7
    x64.jmp main.__range_ok_7
  __range_panic_7:
    x64.lea_symdata rax, [__panic_msg_1]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_7:
    x64.mov rbx, [rbp-8]
    x64.test rbx, rbx
    x64.jz __nonnull_skip_0
    x64.mov rcx, [rbp-8]
    x64.call mm_decref
    x64.label __nonnull_skip_0
    x64.mov rax, [rbp-24]
    x64.epilogue
    x64.ret
  }
  func @__destruct___ManagedMemory_Int(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -1
    x64.cmp rcx, rdx
    x64.jne __destruct___ManagedMemory_Int.check_owned_0
  slice_cleanup_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+32]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_3
    x64.call mm_decref
    x64.label __nonnull_skip_3
    x64.jmp __destruct___ManagedMemory_Int.skip_buf_0
  check_owned_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -2
    x64.cmp rcx, rdx
    x64.je __destruct___ManagedMemory_Int.skip_buf_0
  free_buf_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.call mm_raw_free
    x64.jmp __destruct___ManagedMemory_Int.skip_buf_0
  skip_buf_0:
    x64.jmp __destruct___ManagedMemory_Int.done
  done:
    x64.epilogue
    x64.ret
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_4
    x64.call mm_decref
    x64.label __nonnull_skip_4
    x64.jmp __destruct_Vec3.done
  done:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = __arr_0.2} {kind = i64} {decl = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = __arr_0.1} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    maxon.assign %3 {var = __arr_0.0} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.literal {value = 3 : i64}
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.literal {value = 8 : i64}
    %8 = maxon.struct_literal @__ManagedMemory
    %9 = maxon.struct_literal @Vec3
    maxon.assign %9 {var = v} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.literal {value = 42 : i64}
    maxon.call @Vec3.set %9, %10, %11
    %12 = maxon.struct_var_ref v
    %13 = maxon.literal {value = 0 : i64}
    %16, %15 = maxon.try_call @Vec3.get %12, %13
    %17 = maxon.literal {value = 0 : i64}
    maxon.assign %17 {var = __try_default_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %16 {var = __try_result_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.literal {value = 0 : i64}
    %19 = maxon.binop %15, %18 {op = ne}
    maxon.cond_br %19 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %20 = maxon.var_ref {var = __try_default_2} {type = i64}
    maxon.assign %20 {var = __try_result_1} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %21 = maxon.var_ref {var = __try_result_1} {type = i64}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    maxon.panic "panic at set-and-get.test:8: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_5:
    maxon.scope_end [v, __try_default_2, __try_result_1]
    maxon.return %21
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, __arr_0.2
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, __arr_0.1
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, __arr_0.0
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.constant {value = 3 : i64}
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.constant {value = 8 : i64}
    %8 = arith.constant {value = 32 : i64}
    %9 = func.ref @__destruct___ManagedMemory
    %10 = std.ptr_to_i64 %9
    %11 = arith.constant {value = 1 : i64}
    %12 = std.call_runtime @mm_alloc %8, %10, %11
    memref.store %12, __struct_8
    %13 = memref.load __struct_8 : i64
    memref.store_indirect %4, %13+0
    %14 = memref.load __struct_8 : i64
    memref.store_indirect %5, %14+8
    %15 = memref.load __struct_8 : i64
    memref.store_indirect %6, %15+16
    %16 = memref.load __struct_8 : i64
    memref.store_indirect %7, %16+24
    %17 = memref.load __struct_8 : i64
    %18 = memref.load_indirect %17+24
    %19 = arith.constant {value = 0 : i64}
    %20 = memref.lea_symdata __mm_panic_element_size_zero
    %21 = std.ptr_to_i64 %20
    std.call_runtime @maxon_bounds_check %19, %18, %21
    %22 = arith.constant {value = 16 : i64}
    %23 = func.ref @__destruct_Vec3
    %24 = std.ptr_to_i64 %23
    %25 = arith.constant {value = 2 : i64}
    %26 = std.call_runtime @mm_alloc %22, %24, %25
    memref.store %26, v
    %27 = memref.load v : i64
    memref.store_indirect %0, %27+0
    %28 = memref.load __struct_8 : i64
    %29 = memref.load v : i64
    memref.store_indirect %28, %29+8
    std.call_runtime @mm_incref %28
    %30 = memref.lea __arr_0
    %31 = std.ptr_to_i64 %30
    %32 = memref.load v : i64
    %33 = memref.load_indirect %32+8
    %34 = memref.load_indirect %33+24
    %35 = arith.constant {value = 3 : i64}
    %36 = arith.muli %35, %34
    %37 = std.call_runtime @mm_raw_alloc %36
    %38 = std.call_runtime @maxon_memcpy %37, %31, %36
    %39 = memref.load v : i64
    %40 = memref.load_indirect %39+8
    memref.store_indirect %37, %40+0
    %41 = arith.constant {value = 3 : i64}
    memref.store_indirect %41, %40+16
    %42 = memref.load v : i64
    std.call_runtime @mm_incref %42
    %43 = arith.constant {value = 0 : i64}
    %44 = arith.constant {value = 42 : i64}
    %45 = memref.load v : i64
    func.call @Vec3.set %45, %43, %44
    %46 = arith.constant {value = 0 : i64}
    %47 = memref.load v : i64
    %48, %49 = func.try_call @Vec3.get %47, %46
    %50 = arith.constant {value = 0 : i64}
    memref.store %50, __try_default_2
    memref.store %48, __try_result_1
    %51 = arith.constant {value = 0 : i64}
    %52 = arith.cmpi ne %49, %51
    cf.cond_br %52 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %53 = memref.load __try_default_2 : i64
    memref.store %53, __try_result_1
    cf.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %54 = memref.load __try_result_1 : i64
    %55 = arith.constant {value = 0 : i64}
    %56 = arith.cmpi lt %54, %55
    %57 = arith.constant {value = 4294967295 : i64}
    %58 = arith.cmpi gt %54, %57
    %59 = arith.ori1 %56, %58
    cf.cond_br %59 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    %60 = memref.lea_symdata __panic_msg_0
    %61 = std.ptr_to_i64 %60
    std.call_runtime @maxon_panic %61
  __range_ok_5:
    %62 = memref.load v : i64
    std.call_runtime_if_nonnull @mm_decref %62
    func.return %54
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %133 = func.param ptr : StdI64
    memref.store %133, __destr_ptr
    %136 = memref.load __destr_ptr : i64
    %137 = memref.load_indirect %136+16
    %138 = arith.constant {value = 0 : i64}
    %139 = arith.cmpi ne %137, %138
    cf.cond_br %139 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %140 = memref.load __destr_ptr : i64
    %141 = memref.load_indirect %140+0
    std.call_runtime @mm_raw_free %141
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    %142 = func.param ptr : StdI64
    memref.store %142, __destr_ptr
    %143 = memref.load __destr_ptr : i64
    %144 = memref.load_indirect %143+8
    std.call_runtime_if_nonnull @mm_decref %144
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=176
    arm64.mov x0, #0
    arm64.mov x1, #0
    arm64.str x1, [x29, #-8]
    arm64.mov x2, #0
    arm64.str x2, [x29, #-16]
    arm64.mov x3, #0
    arm64.str x3, [x29, #-24]
    arm64.mov x4, #0
    arm64.mov x5, #3
    arm64.mov x6, #0
    arm64.mov x7, #8
    arm64.adrp_add_func x8, __destruct___ManagedMemory
    arm64.mov x9, x8
    arm64.mov x1, x9
    arm64.mov x0, #32
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-32]
    arm64.ldr x10, [x29, #-32]
    arm64.mov x11, #0
    arm64.str x11, [x10, #0]
    arm64.ldr x12, [x29, #-32]
    arm64.mov x13, #3
    arm64.str x13, [x12, #8]
    arm64.ldr x14, [x29, #-32]
    arm64.mov x15, #0
    arm64.str x15, [x14, #16]
    arm64.ldr x0, [x29, #-32]
    arm64.mov x1, #8
    arm64.str x1, [x0, #24]
    arm64.ldr x0, [x29, #-32]
    arm64.ldr x1, [x0, #24]
    arm64.adrp_add_symdata x0, __mm_panic_element_size_zero
    arm64.mov x2, x0
    arm64.mov x0, #0
    arm64.bl maxon_bounds_check
    arm64.adrp_add_func x0, __destruct_Vec3
    arm64.mov x1, x0
    arm64.mov x0, #16
    arm64.mov x2, #2
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-40]
    arm64.ldr x0, [x29, #-40]
    arm64.mov x1, #0
    arm64.str x1, [x0, #0]
    arm64.ldr x0, [x29, #-32]
    arm64.ldr x1, [x29, #-40]
    arm64.str x0, [x1, #8]
    arm64.bl mm_incref
    arm64.add x0, x29, #-24
    arm64.mov x1, x0
    arm64.ldr x0, [x29, #-40]
    arm64.ldr x2, [x0, #8]
    arm64.ldr x0, [x2, #24]
    arm64.mov x2, #3
    arm64.mul x3, x2, x0
    arm64.str x1, [x29, #-64]
    arm64.str x3, [x29, #-72]
    arm64.ldr x0, [x29, #-72]
    arm64.bl mm_raw_alloc
    arm64.str x0, [x29, #-80]
    arm64.ldr x1, [x29, #-64]
    arm64.ldr x2, [x29, #-72]
    arm64.bl maxon_memcpy
    arm64.ldr x1, [x29, #-40]
    arm64.ldr x2, [x1, #8]
    arm64.ldr x1, [x29, #-80]
    arm64.str x1, [x2, #0]
    arm64.mov x1, #3
    arm64.str x1, [x2, #16]
    arm64.ldr x1, [x29, #-40]
    arm64.str x0, [x29, #-88]
    arm64.ldr x0, [x29, #-40]
    arm64.bl mm_incref
    arm64.ldr x0, [x29, #-40]
    arm64.mov x1, #0
    arm64.mov x2, #42
    arm64.bl Vec3.set
    arm64.ldr x0, [x29, #-40]
    arm64.mov x1, #0
    arm64.bl Vec3.get
    arm64.mov x2, #0
    arm64.str x2, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_3
    arm64.b main.otherwise_default_continue_4
  otherwise_default_error_3:
    arm64.ldr x0, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.b main.otherwise_default_continue_4
  otherwise_default_continue_4:
    arm64.ldr x0, [x29, #-56]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_5
    arm64.b main.__range_ok_5
  __range_panic_5:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_5:
    arm64.ldr x1, [x29, #-40]
    arm64.cmp x1, #0
    arm64.b.eq main.__skip_guarded_86
    arm64.ldr x0, [x29, #-40]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_86
    arm64.ldr x0, [x29, #-56]
    arm64.epilogue stack_size=176
    arm64.ret
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #0
    arm64.cmp x1, x2
    arm64.cset x3, ne
    arm64.cmp x3, #0
    arm64.b.ne __destruct___ManagedMemory.free_buf_0
    arm64.b __destruct___ManagedMemory.skip_buf_0
  free_buf_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.mov x0, x1
    arm64.bl mm_raw_free
    arm64.b __destruct___ManagedMemory.skip_buf_0
  skip_buf_0:
    arm64.b __destruct___ManagedMemory.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #8]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_Vec3.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_Vec3.__skip_guarded_4
    arm64.b __destruct_Vec3.done
  done:
    arm64.epilogue stack_size=48
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

typealias Integer = int(i64.min to i64.max)

typealias Byte = byte(0 to u8.max)
typealias ByteVec4 = Vector with 4 Byte

function main() returns ExitCode
	var v = ByteVec4.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 30) otherwise panic("test invariant: set OOB")
	try v.set(3, value: 40) otherwise panic("test invariant: set OOB")
	let a = try v.get(0) otherwise 0
	let b = try v.get(3) otherwise 0
	return (a as Integer) + (b as Integer)
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
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 30) otherwise panic("test invariant: set OOB")
	try v.set(3, value: 40) otherwise panic("test invariant: set OOB")
	try v.set(4, value: 50) otherwise panic("test invariant: set OOB")
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
150
```
