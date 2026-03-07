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
```RequiredMLIR
=== maxon
module {
  func @vector.main() -> i64 {
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
    maxon.assign %19 {var = __range_val_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %19, %20 {op = lt}
    %22 = maxon.literal {value = 4294967295 : i64}
    %23 = maxon.binop %19, %22 {op = gt}
    %24 = maxon.binop %21, %23 {op = or}
    maxon.cond_br %24 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    maxon.panic "panic at create-zero-initialized.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_5:
    %26 = maxon.var_ref {var = __range_val_5} {type = i64}
    maxon.scope_end [v, __try_default_2, __range_val_5, __try_result_1]
    maxon.return %26
  }
}
=== standard
module {
  func @vector.main() -> u32 {
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
    %17 = arith.constant {value = 16 : i64}
    %18 = func.ref @__destruct_Vec3
    %19 = std.ptr_to_i64 %18
    %20 = arith.constant {value = 2 : i64}
    %21 = std.call_runtime @mm_alloc %17, %19, %20
    memref.store %21, v
    %22 = memref.load v : i64
    memref.store_indirect %0, %22+0
    %23 = memref.load __struct_8 : i64
    %24 = memref.load v : i64
    memref.store_indirect %23, %24+8
    std.call_runtime @mm_incref %23
    %25 = memref.lea __arr_0
    %26 = std.ptr_to_i64 %25
    %27 = memref.load v : i64
    %28 = memref.load_indirect %27+8
    memref.store_indirect %26, %28+0
    %29 = memref.load v : i64
    std.call_runtime @mm_incref %29
    %30 = arith.constant {value = 0 : i64}
    %31 = memref.load v : i64
    %32, %33 = func.try_call @Vec3.get %31, %30
    %34 = arith.constant {value = -1 : i64}
    memref.store %34, __try_default_2
    memref.store %32, __try_result_1
    %35 = arith.constant {value = 0 : i64}
    %36 = arith.cmpi ne %33, %35
    cf.cond_br %36 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %37 = memref.load __try_default_2 : i64
    memref.store %37, __try_result_1
    cf.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %38 = memref.load __try_result_1 : i64
    memref.store %38, __range_val_5
    %39 = arith.constant {value = 0 : i64}
    %40 = arith.cmpi lt %38, %39
    %41 = arith.constant {value = 4294967295 : i64}
    %42 = arith.cmpi gt %38, %41
    %43 = arith.ori1 %40, %42
    cf.cond_br %43 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    %44 = memref.lea_symdata __panic_msg_25
    %45 = std.ptr_to_i64 %44
    std.call_runtime @maxon_panic %45
  __range_ok_5:
    %46 = memref.load __range_val_5 : i64
    %47 = memref.load v : i64
    std.call_runtime_if_nonnull @mm_decref %47
    func.return %46
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %78 = func.param ptr : StdI64
    memref.store %78, __destr_ptr
    %81 = memref.load __destr_ptr : i64
    %82 = memref.load_indirect %81+16
    %83 = arith.constant {value = 0 : i64}
    %84 = arith.cmpi ne %82, %83
    cf.cond_br %84 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %85 = memref.load __destr_ptr : i64
    %86 = memref.load_indirect %85+0
    std.call_runtime @mm_raw_free %86
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    %87 = func.param ptr : StdI64
    memref.store %87, __destr_ptr
    %88 = memref.load __destr_ptr : i64
    %89 = memref.load_indirect %88+8
    std.call_runtime_if_nonnull @mm_decref %89
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @vector.main() -> u32 {
  entry:
    x86.prologue stack_size=64
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
    x86.mov [rdx+0], rcx
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
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
    x86.je vector.main.otherwise_default_continue_4
  otherwise_default_error_3:
    x86.mov rax, [rbp-48]
    x86.mov [rbp-56], rax
    x86.jmp vector.main.otherwise_default_continue_4
  otherwise_default_continue_4:
    x86.mov rax, [rbp-56]
    x86.mov [rbp-64], rax
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
    x86.je vector.main.__range_ok_5
  __range_panic_5:
    x86.lea_symdata rax, [__panic_msg_25]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_5:
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-40]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_0
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov rax, [rbp-64]
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
```RequiredMLIR
=== maxon
module {
  func @vector.main() -> i64 {
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
    maxon.assign %21 {var = __range_val_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    maxon.panic "panic at set-and-get.test:8: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_5:
    %28 = maxon.var_ref {var = __range_val_5} {type = i64}
    maxon.scope_end [v, __try_default_2, __range_val_5, __try_result_1]
    maxon.return %28
  }
}
=== standard
module {
  func @vector.main() -> u32 {
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
    %17 = arith.constant {value = 16 : i64}
    %18 = func.ref @__destruct_Vec3
    %19 = std.ptr_to_i64 %18
    %20 = arith.constant {value = 2 : i64}
    %21 = std.call_runtime @mm_alloc %17, %19, %20
    memref.store %21, v
    %22 = memref.load v : i64
    memref.store_indirect %0, %22+0
    %23 = memref.load __struct_8 : i64
    %24 = memref.load v : i64
    memref.store_indirect %23, %24+8
    std.call_runtime @mm_incref %23
    %25 = memref.lea __arr_0
    %26 = std.ptr_to_i64 %25
    %27 = memref.load v : i64
    %28 = memref.load_indirect %27+8
    memref.store_indirect %26, %28+0
    %29 = memref.load v : i64
    std.call_runtime @mm_incref %29
    %30 = arith.constant {value = 0 : i64}
    %31 = arith.constant {value = 42 : i64}
    %32 = memref.load v : i64
    func.call @Vec3.set %32, %30, %31
    %33 = arith.constant {value = 0 : i64}
    %34 = memref.load v : i64
    %35, %36 = func.try_call @Vec3.get %34, %33
    %37 = arith.constant {value = 0 : i64}
    memref.store %37, __try_default_2
    memref.store %35, __try_result_1
    %38 = arith.constant {value = 0 : i64}
    %39 = arith.cmpi ne %36, %38
    cf.cond_br %39 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %40 = memref.load __try_default_2 : i64
    memref.store %40, __try_result_1
    cf.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %41 = memref.load __try_result_1 : i64
    memref.store %41, __range_val_5
    %42 = arith.constant {value = 0 : i64}
    %43 = arith.cmpi lt %41, %42
    %44 = arith.constant {value = 4294967295 : i64}
    %45 = arith.cmpi gt %41, %44
    %46 = arith.ori1 %43, %45
    cf.cond_br %46 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    %47 = memref.lea_symdata __panic_msg_27
    %48 = std.ptr_to_i64 %47
    std.call_runtime @maxon_panic %48
  __range_ok_5:
    %49 = memref.load __range_val_5 : i64
    %50 = memref.load v : i64
    std.call_runtime_if_nonnull @mm_decref %50
    func.return %49
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %121 = func.param ptr : StdI64
    memref.store %121, __destr_ptr
    %124 = memref.load __destr_ptr : i64
    %125 = memref.load_indirect %124+16
    %126 = arith.constant {value = 0 : i64}
    %127 = arith.cmpi ne %125, %126
    cf.cond_br %127 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %128 = memref.load __destr_ptr : i64
    %129 = memref.load_indirect %128+0
    std.call_runtime @mm_raw_free %129
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_Vec3(ptr: i64) {
  entry:
    %130 = func.param ptr : StdI64
    memref.store %130, __destr_ptr
    %131 = memref.load __destr_ptr : i64
    %132 = memref.load_indirect %131+8
    std.call_runtime_if_nonnull @mm_decref %132
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @vector.main() -> u32 {
  entry:
    x86.prologue stack_size=64
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
    x86.mov [rdx+0], rcx
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
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
    x86.je vector.main.otherwise_default_continue_4
  otherwise_default_error_3:
    x86.mov rax, [rbp-48]
    x86.mov [rbp-56], rax
    x86.jmp vector.main.otherwise_default_continue_4
  otherwise_default_continue_4:
    x86.mov rax, [rbp-56]
    x86.mov [rbp-64], rax
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
    x86.je vector.main.__range_ok_5
  __range_panic_5:
    x86.lea_symdata rax, [__panic_msg_27]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_5:
    x86.mov rax, [rbp-64]
    x86.mov rcx, [rbp-40]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_0
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov rax, [rbp-64]
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
