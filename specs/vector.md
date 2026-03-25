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
var v = Vec3{}  // zero-initialized, 3 elements on the stack
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
var v = Vec4{}
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
var v = Vec3{}
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
  var v = Vec3{}
  return try v.get(0) otherwise -1
end 'main'
```
```exitcode
0
```
```RequiredMLIR:x86_64-windows
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
=== x86
module {
  func @main() -> u32 {
  entry:
    x86.prologue stack_size=96
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.mov [rbp-8], rcx
    x86.xor rdx, rdx
    x86.mov [rbp-16], rdx
    x86.xor rbx, rbx
    x86.mov [rbp-24], rbx
    x86.xor rsi, rsi
    x86.mov rdi, 3
    x86.xor r8, r8
    x86.mov r9, 8
    x86.lea_func rcx, [__destruct___ManagedMemory]
    x86.mov rdx, rcx
    x86.mov rcx, 32
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-32], rax
    x86.mov rax, [rbp-32]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, 3
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-32]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rax+24]
    x86.lea_symdata rax, [__mm_panic_element_size_zero]
    x86.mov rdx, rax
    x86.mov r8, rdx
    x86.mov rdx, rcx
    x86.xor rcx, rcx
    x86.call maxon_bounds_check
    x86.lea_func rax, [__destruct_Vec3]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 2
    x86.call mm_alloc
    x86.mov [rbp-40], rax
    x86.mov rax, [rbp-40]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+8], rax
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.lea rax, [rbp-24]
    x86.mov rcx, rax
    x86.mov rax, [rbp-40]
    x86.mov rdx, [rax+8]
    x86.mov rax, [rdx+24]
    x86.mov rdx, 3
    x86.imul rdx, rax
    x86.mov [rbp-64], rcx
    x86.mov [rbp-72], rdx
    x86.mov rcx, [rbp-72]
    x86.call mm_raw_alloc
    x86.mov [rbp-80], rax
    x86.mov rcx, [rbp-80]
    x86.mov rdx, [rbp-64]
    x86.mov r8, [rbp-72]
    x86.call maxon_memcpy
    x86.mov rcx, [rbp-40]
    x86.mov rdx, [rcx+8]
    x86.mov rcx, [rbp-80]
    x86.mov [rdx+0], rcx
    x86.mov rcx, 3
    x86.mov [rdx+16], rcx
    x86.mov rcx, [rbp-40]
    x86.mov [rbp-88], rax
    x86.call mm_incref
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.xor rdx, rdx
    x86.call Vec3.get
    x86.mov rcx, -1
    x86.mov [rbp-48], rcx
    x86.mov [rbp-56], rax
    x86.xor rax, rax
    x86.cmp rdx, rax
    x86.je main.otherwise_default_continue_4
  otherwise_default_error_3:
    x86.mov rax, [rbp-48]
    x86.mov [rbp-56], rax
    x86.jmp main.otherwise_default_continue_4
  otherwise_default_continue_4:
    x86.mov rax, [rbp-56]
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.setl rdx
    x86.movzx rdx, rdxb
    x86.mov rbx, 4294967295
    x86.cmp rax, rbx
    x86.setg rsi
    x86.movzx rsi, rsib
    x86.or rdx, rsi
    x86.test rdx, rdx
    x86.je main.__range_ok_5
  __range_panic_5:
    x86.lea_symdata rax, [__panic_msg_0]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_5:
    x86.mov rcx, [rbp-40]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_0
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov rax, [rbp-56]
    x86.epilogue
    x86.ret
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.je __destruct___ManagedMemory.skip_buf_0
  free_buf_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.call mm_raw_free
    x86.jmp __destruct___ManagedMemory.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct___ManagedMemory.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_1
    x86.call mm_decref
    x86.label __nonnull_skip_1
    x86.jmp __destruct_Vec3.done
  done:
    x86.epilogue
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
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
  var v = Vec4{}
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
  var v = Vec3{}
  v.set(0, value: 42)
  return try v.get(0) otherwise 0
end 'main'
```
```exitcode
42
```
```RequiredMLIR:x86_64-windows
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
=== x86
module {
  func @main() -> u32 {
  entry:
    x86.prologue stack_size=96
    x86.xor rax, rax
    x86.xor rcx, rcx
    x86.mov [rbp-8], rcx
    x86.xor rdx, rdx
    x86.mov [rbp-16], rdx
    x86.xor rbx, rbx
    x86.mov [rbp-24], rbx
    x86.xor rsi, rsi
    x86.mov rdi, 3
    x86.xor r8, r8
    x86.mov r9, 8
    x86.lea_func rcx, [__destruct___ManagedMemory]
    x86.mov rdx, rcx
    x86.mov rcx, 32
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-32], rax
    x86.mov rax, [rbp-32]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, 3
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-32]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rax+24]
    x86.lea_symdata rax, [__mm_panic_element_size_zero]
    x86.mov rdx, rax
    x86.mov r8, rdx
    x86.mov rdx, rcx
    x86.xor rcx, rcx
    x86.call maxon_bounds_check
    x86.lea_func rax, [__destruct_Vec3]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 2
    x86.call mm_alloc
    x86.mov [rbp-40], rax
    x86.mov rax, [rbp-40]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+8], rax
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.lea rax, [rbp-24]
    x86.mov rcx, rax
    x86.mov rax, [rbp-40]
    x86.mov rdx, [rax+8]
    x86.mov rax, [rdx+24]
    x86.mov rdx, 3
    x86.imul rdx, rax
    x86.mov [rbp-64], rcx
    x86.mov [rbp-72], rdx
    x86.mov rcx, [rbp-72]
    x86.call mm_raw_alloc
    x86.mov [rbp-80], rax
    x86.mov rcx, [rbp-80]
    x86.mov rdx, [rbp-64]
    x86.mov r8, [rbp-72]
    x86.call maxon_memcpy
    x86.mov rcx, [rbp-40]
    x86.mov rdx, [rcx+8]
    x86.mov rcx, [rbp-80]
    x86.mov [rdx+0], rcx
    x86.mov rcx, 3
    x86.mov [rdx+16], rcx
    x86.mov rcx, [rbp-40]
    x86.mov [rbp-88], rax
    x86.call mm_incref
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.xor rdx, rdx
    x86.mov r8, 42
    x86.call Vec3.set
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.xor rdx, rdx
    x86.call Vec3.get
    x86.xor rcx, rcx
    x86.mov [rbp-48], rcx
    x86.mov [rbp-56], rax
    x86.xor rax, rax
    x86.cmp rdx, rax
    x86.je main.otherwise_default_continue_4
  otherwise_default_error_3:
    x86.mov rax, [rbp-48]
    x86.mov [rbp-56], rax
    x86.jmp main.otherwise_default_continue_4
  otherwise_default_continue_4:
    x86.mov rax, [rbp-56]
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.setl rdx
    x86.movzx rdx, rdxb
    x86.mov rbx, 4294967295
    x86.cmp rax, rbx
    x86.setg rsi
    x86.movzx rsi, rsib
    x86.or rdx, rsi
    x86.test rdx, rdx
    x86.je main.__range_ok_5
  __range_panic_5:
    x86.lea_symdata rax, [__panic_msg_0]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_5:
    x86.mov rcx, [rbp-40]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_0
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov rax, [rbp-56]
    x86.epilogue
    x86.ret
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.je __destruct___ManagedMemory.skip_buf_0
  free_buf_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.call mm_raw_free
    x86.jmp __destruct___ManagedMemory.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct___ManagedMemory.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_1
    x86.call mm_decref
    x86.label __nonnull_skip_1
    x86.jmp __destruct_Vec3.done
  done:
    x86.epilogue
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
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
  var v = Vec3{}
  v.set(0, value: 10)
  v.set(1, value: 20)
  v.set(2, value: 30)
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
  var c = try v.get(2) otherwise 0
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
  var v = Vec2{}
  v.set(0, value: 10)
  var result = try v.get(5) otherwise -1
  print("{result}\n")
  return 0
end 'main'
```
```stdout
-1
```

<!-- test: set-out-of-bounds-noop -->
Setting an out-of-bounds index is a no-op, matching Array behavior.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec2 = Vector with 2 Int

function main() returns ExitCode
  var v = Vec2{}
  v.set(0, value: 10)
  v.set(5, value: 99)
  return try v.get(0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: single-element -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec1 = Vector with 1 Int

function main() returns ExitCode
  var v = Vec1{}
  v.set(0, value: 77)
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
  var v = Vec10{}
  var i = 0
  while i < 10 'fill'
    v.set(i, value: i * 10)
    i = i + 1
  end 'fill'
  var first = try v.get(0) otherwise -1
  var last = try v.get(9) otherwise -1
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
  var v = Vec1{}
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
  var v = Vec3{}
  v.set(1, value: 10)
  v.set(1, value: 42)
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
  var v = Vec2F{}
  v.set(0, value: 2.5)
  v.set(1, value: 3.5)
  var a = try v.get(0) otherwise 0.0
  var b = try v.get(1) otherwise 0.0
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
  var v = ByteVec4{}
  v.set(0, value: 10)
  v.set(1, value: 20)
  v.set(2, value: 30)
  v.set(3, value: 40)
  var a = try v.get(0) otherwise 0
  var b = try v.get(3) otherwise 0
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
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
  var c = try v.get(2) otherwise 0
  return a + b + c
end 'sum'

function main() returns ExitCode
  var v = Vec3{}
  v.set(0, value: 10)
  v.set(1, value: 20)
  v.set(2, value: 12)
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
  var v = Vec2{}
  v.set(0, value: a)
  v.set(1, value: b)
  return v
end 'makeVec'

function main() returns ExitCode
  var v = makeVec(30, b: 12)
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
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
  var v = Vec4{}
  v.set(0, value: 1)
  v.set(1, value: 2)
  v.set(2, value: 3)
  v.set(3, value: 4)
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
  var v = Vec3{}
  v.set(0, value: 10)
  v.set(1, value: 20)
  v.set(2, value: 12)
  return v
end 'makeVec'

function main() returns ExitCode
  let v = makeVec()
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
  var c = try v.get(2) otherwise 0
  return a + b + c
end 'main'
```
```exitcode
42
```

<!-- test: from-array-literal -->
```maxon
function main() returns ExitCode
  var v = Vector from [10, 20, 30]
  return try v.get(0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: from-array-literal-sum -->
```maxon
function main() returns ExitCode
  var v = Vector from [10, 20, 30]
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
  var c = try v.get(2) otherwise 0
  return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: from-array-literal-float -->
```maxon
function main() returns ExitCode
  var v = Vector from [1.5, 2.5]
  var a = try v.get(0) otherwise 0.0
  var b = try v.get(1) otherwise 0.0
  return trunc(a + b)
end 'main'
```
```exitcode
4
```

<!-- test: from-array-literal-iterate -->
```maxon
function main() returns ExitCode
  var v = Vector from [1, 2, 3, 4]
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
  var v = Vector from [99]
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
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
  var c = try v.get(2) otherwise 0
  return a + b + c
end 'sum'

function main() returns ExitCode
  var v = Vec3{}
  v.set(0, value: 10)
  v.set(1, value: 20)
  v.set(2, value: 12)
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
  var v = Vec5{}
  v.set(0, value: 10)
  v.set(1, value: 20)
  v.set(2, value: 30)
  v.set(3, value: 40)
  v.set(4, value: 50)
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
