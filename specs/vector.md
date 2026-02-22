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
    %9 = std.call_runtime @maxon_alloc %8
    memref.store %9, __struct_8
    %10 = memref.load __struct_8 : i64
    memref.store_indirect %4, %10+0
    %11 = memref.load __struct_8 : i64
    memref.store_indirect %5, %11+8
    %12 = memref.load __struct_8 : i64
    memref.store_indirect %6, %12+16
    %13 = memref.load __struct_8 : i64
    memref.store_indirect %7, %13+24
    %14 = arith.constant {value = 16 : i64}
    %15 = std.call_runtime @maxon_alloc %14
    memref.store %15, v
    %16 = memref.load v : i64
    memref.store_indirect %0, %16+0
    %17 = memref.load __struct_8 : i64
    %18 = memref.load v : i64
    memref.store_indirect %17, %18+8
    %19 = memref.lea __arr_0
    %20 = std.ptr_to_i64 %19
    %21 = memref.load v : i64
    %22 = memref.load_indirect %21+8
    memref.store_indirect %20, %22+0
    %23 = arith.constant {value = 3 : i64}
    memref.store_indirect %23, %22+16
    %24 = arith.constant {value = 0 : i64}
    %25 = memref.load v : i64
    %26, %27 = func.try_call @Vec3.get %25, %24
    %28 = arith.constant {value = -1 : i64}
    memref.store %28, __try_default_2
    memref.store %26, __try_result_1
    %29 = arith.constant {value = 0 : i64}
    %30 = arith.cmpi ne %27, %29
    cf.cond_br %30 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %31 = memref.load __try_default_2 : i64
    memref.store %31, __try_result_1
    cf.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %32 = memref.load __try_result_1 : i64
    memref.store %32, __range_val_5
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.cmpi lt %32, %33
    %35 = arith.constant {value = 4294967295 : i64}
    %36 = arith.cmpi gt %32, %35
    %37 = arith.ori1 %34, %36
    cf.cond_br %37 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    %38 = memref.lea_symdata __panic_msg_25
    %39 = std.ptr_to_i64 %38
    std.call_runtime @maxon_panic %39
  __range_ok_5:
    %40 = memref.load __range_val_5 : i64
    func.return %40
  }
}
=== x86
module {
  func @vector.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.xor edx, edx
    x86.mov [rbp-16], edx
    x86.xor ebx, ebx
    x86.mov [rbp-24], ebx
    x86.xor esi, esi
    x86.mov edi, 3
    x86.xor r8, r8
    x86.mov r9, 8
    x86.mov ecx, 32
    x86.call maxon_alloc
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-32]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-32]
    x86.mov ecx, 3
    x86.mov [eax+8], ecx
    x86.mov eax, [rbp-32]
    x86.xor ecx, ecx
    x86.mov [eax+16], ecx
    x86.mov eax, [rbp-32]
    x86.mov ecx, 8
    x86.mov [eax+24], ecx
    x86.mov eax, 16
    x86.mov rcx, rax
    x86.call maxon_alloc
    x86.mov [rbp-40], eax
    x86.mov eax, [rbp-40]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+8], eax
    x86.lea rax, [rbp-24]
    x86.mov rcx, rax
    x86.mov eax, [rbp-40]
    x86.mov edx, [eax+8]
    x86.mov [edx+0], ecx
    x86.mov eax, 3
    x86.mov [edx+16], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-40]
    x86.mov rdx, rax
    x86.call Vec3.get
    x86.mov ecx, -1
    x86.mov [rbp-48], ecx
    x86.mov [rbp-56], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je vector.main.otherwise_default_continue_4
  otherwise_default_error_3:
    x86.mov eax, [rbp-48]
    x86.mov [rbp-56], eax
    x86.jmp vector.main.otherwise_default_continue_4
  otherwise_default_continue_4:
    x86.mov eax, [rbp-56]
    x86.mov [rbp-64], eax
    x86.xor ecx, ecx
    x86.cmp eax, ecx
    x86.setl edx
    x86.movzx edx, edxb
    x86.mov rbx, 4294967295
    x86.cmp rax, rbx
    x86.setg esi
    x86.movzx esi, esib
    x86.or edx, esi
    x86.test edx, edx
    x86.je vector.main.__range_ok_5
  __range_panic_5:
    x86.lea_symdata rax, [__panic_msg_25]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_5:
    x86.mov eax, [rbp-64]
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
    %9 = std.call_runtime @maxon_alloc %8
    memref.store %9, __struct_8
    %10 = memref.load __struct_8 : i64
    memref.store_indirect %4, %10+0
    %11 = memref.load __struct_8 : i64
    memref.store_indirect %5, %11+8
    %12 = memref.load __struct_8 : i64
    memref.store_indirect %6, %12+16
    %13 = memref.load __struct_8 : i64
    memref.store_indirect %7, %13+24
    %14 = arith.constant {value = 16 : i64}
    %15 = std.call_runtime @maxon_alloc %14
    memref.store %15, v
    %16 = memref.load v : i64
    memref.store_indirect %0, %16+0
    %17 = memref.load __struct_8 : i64
    %18 = memref.load v : i64
    memref.store_indirect %17, %18+8
    %19 = memref.lea __arr_0
    %20 = std.ptr_to_i64 %19
    %21 = memref.load v : i64
    %22 = memref.load_indirect %21+8
    memref.store_indirect %20, %22+0
    %23 = arith.constant {value = 3 : i64}
    memref.store_indirect %23, %22+16
    %24 = arith.constant {value = 0 : i64}
    %25 = arith.constant {value = 42 : i64}
    %26 = memref.load v : i64
    func.call @Vec3.set %26, %24, %25
    %27 = arith.constant {value = 0 : i64}
    %28 = memref.load v : i64
    %29, %30 = func.try_call @Vec3.get %28, %27
    %31 = arith.constant {value = 0 : i64}
    memref.store %31, __try_default_2
    memref.store %29, __try_result_1
    %32 = arith.constant {value = 0 : i64}
    %33 = arith.cmpi ne %30, %32
    cf.cond_br %33 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %34 = memref.load __try_default_2 : i64
    memref.store %34, __try_result_1
    cf.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %35 = memref.load __try_result_1 : i64
    memref.store %35, __range_val_5
    %36 = arith.constant {value = 0 : i64}
    %37 = arith.cmpi lt %35, %36
    %38 = arith.constant {value = 4294967295 : i64}
    %39 = arith.cmpi gt %35, %38
    %40 = arith.ori1 %37, %39
    cf.cond_br %40 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    %41 = memref.lea_symdata __panic_msg_27
    %42 = std.ptr_to_i64 %41
    std.call_runtime @maxon_panic %42
  __range_ok_5:
    %43 = memref.load __range_val_5 : i64
    func.return %43
  }
}
=== x86
module {
  func @vector.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.xor edx, edx
    x86.mov [rbp-16], edx
    x86.xor ebx, ebx
    x86.mov [rbp-24], ebx
    x86.xor esi, esi
    x86.mov edi, 3
    x86.xor r8, r8
    x86.mov r9, 8
    x86.mov ecx, 32
    x86.call maxon_alloc
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-32]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-32]
    x86.mov ecx, 3
    x86.mov [eax+8], ecx
    x86.mov eax, [rbp-32]
    x86.xor ecx, ecx
    x86.mov [eax+16], ecx
    x86.mov eax, [rbp-32]
    x86.mov ecx, 8
    x86.mov [eax+24], ecx
    x86.mov eax, 16
    x86.mov rcx, rax
    x86.call maxon_alloc
    x86.mov [rbp-40], eax
    x86.mov eax, [rbp-40]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+8], eax
    x86.lea rax, [rbp-24]
    x86.mov rcx, rax
    x86.mov eax, [rbp-40]
    x86.mov edx, [eax+8]
    x86.mov [edx+0], ecx
    x86.mov eax, 3
    x86.mov [edx+16], eax
    x86.xor eax, eax
    x86.mov ecx, 42
    x86.mov edx, [rbp-40]
    x86.mov r8, rcx
    x86.mov rcx, rdx
    x86.mov rdx, rax
    x86.call Vec3.set
    x86.xor eax, eax
    x86.mov ecx, [rbp-40]
    x86.mov rdx, rax
    x86.call Vec3.get
    x86.xor ecx, ecx
    x86.mov [rbp-48], ecx
    x86.mov [rbp-56], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je vector.main.otherwise_default_continue_4
  otherwise_default_error_3:
    x86.mov eax, [rbp-48]
    x86.mov [rbp-56], eax
    x86.jmp vector.main.otherwise_default_continue_4
  otherwise_default_continue_4:
    x86.mov eax, [rbp-56]
    x86.mov [rbp-64], eax
    x86.xor ecx, ecx
    x86.cmp eax, ecx
    x86.setl edx
    x86.movzx edx, edxb
    x86.mov rbx, 4294967295
    x86.cmp rax, rbx
    x86.setg esi
    x86.movzx esi, esib
    x86.or edx, esi
    x86.test edx, edx
    x86.je vector.main.__range_ok_5
  __range_panic_5:
    x86.lea_symdata rax, [__panic_msg_27]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_5:
    x86.mov eax, [rbp-64]
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
