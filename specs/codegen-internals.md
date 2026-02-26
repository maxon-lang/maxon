---
feature: codegen-internals
status: stable
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
  var v = BigVec{skipZeroInit: true}
  v.set(2047, value: n)
  if n <= 0 'base'
    return Depth{0}
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
  var arr = IntArray{}
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
    var outer_arr = IntArray{}
    outer_arr.push(100)
    if true 'inner'
      var inner_arr = IntArray{}
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
  var arr = IntArray{}
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
  var arr = [10, 20, 30]
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

<!-- test: rdata-bool-array-uses-i8 -->
```maxon
function main() returns ExitCode
  let arr = [true, false, true, false]
  var v0 = try arr.get(0) otherwise false
  var v1 = try arr.get(1) otherwise true
  var v2 = try arr.get(2) otherwise false
  var v3 = try arr.get(3) otherwise true
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
i8[] 1, 0, 1, 0
```

<!-- test: rdata-byte-array-uses-i8 -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function main() returns ExitCode
  let arr = [10 as Byte, 20 as Byte, 30 as Byte]
  var v0 = try arr.get(0) otherwise 0 as Byte
  var v1 = try arr.get(1) otherwise 0 as Byte
  var v2 = try arr.get(2) otherwise 0 as Byte
  return (v0 as Integer) + (v1 as Integer) + (v2 as Integer)
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
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
  var arr = [42]
  arr.set(0, value: 77)
  return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
77
```
```RequiredRdata
i64 42
```
```RequiredMLIR
=== maxon
module {
  func @codegen-internals.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = codegen-internals.main}
    %1 = maxon.literal {value = 42 : i64}
    maxon.assign %1 {var = __arr_0.0} {kind = i64} {decl = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.literal {value = 8 : i64}
    %6 = maxon.struct_literal @__ManagedMemory
    %7 = maxon.literal {value = 0 : i64}
    %8 = maxon.struct_literal @IntArray
    maxon.assign %8 {var = arr} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.literal {value = 77 : i64}
    maxon.call @IntArray.set %8, %9, %10
    %11 = maxon.struct_var_ref arr
    %12 = maxon.literal {value = 0 : i64}
    %15, %14 = maxon.try_call @IntArray.get %11, %12
    %16 = maxon.literal {value = 0 : i64}
    maxon.assign %16 {var = __try_default_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %15 {var = __try_result_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %14, %17 {op = ne}
    maxon.cond_br %18 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %19 = maxon.var_ref {var = __try_default_2} {type = i64}
    maxon.assign %19 {var = __try_result_1} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %20 = maxon.var_ref {var = __try_result_1} {type = i64}
    maxon.assign %20 {var = __range_val_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %21 = maxon.literal {value = 0 : i64}
    %22 = maxon.binop %20, %21 {op = lt}
    %23 = maxon.literal {value = 4294967295 : i64}
    %24 = maxon.binop %20, %23 {op = gt}
    %25 = maxon.binop %22, %24 {op = or}
    maxon.cond_br %25 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    maxon.panic "panic at rdata-cow-mutation-copies-to-heap.test:8: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_5:
    %27 = maxon.var_ref {var = __range_val_5} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %27
  }
}
=== standard
module {
  func @codegen-internals.main() -> u32 {
  entry:
    %0 = memref.lea_symdata __tag_codegen-internals_main
    %1 = std.ptr_to_i64 %0
    %2 = std.call_runtime @mm_scope_enter %1
    memref.store %2, __scope_0
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.constant {value = 1 : i64}
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.constant {value = 8 : i64}
    %8 = arith.constant {value = 32 : i64}
    %9 = memref.lea_symdata __tag___ManagedMemory
    %10 = std.ptr_to_i64 %9
    %11 = std.call_runtime @mm_alloc %8, %10
    memref.store %11, __struct_6
    %12 = memref.load __struct_6 : i64
    memref.store_indirect %4, %12+0
    %13 = memref.load __struct_6 : i64
    memref.store_indirect %5, %13+8
    %14 = memref.load __struct_6 : i64
    memref.store_indirect %6, %14+16
    %15 = memref.load __struct_6 : i64
    memref.store_indirect %7, %15+24
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.constant {value = 16 : i64}
    %18 = memref.lea_symdata __tag_IntArray
    %19 = std.ptr_to_i64 %18
    %20 = std.call_runtime @mm_alloc %17, %19
    memref.store %20, arr
    %21 = memref.load arr : i64
    memref.store_indirect %16, %21+0
    %22 = memref.load __struct_6 : i64
    %23 = memref.load arr : i64
    memref.store_indirect %22, %23+8
    %24 = memref.load arr : i64
    %25 = arith.constant {value = 1 : i64}
    std.call_runtime @mm_move %22, %24, %25
    %26 = memref.lea_rdata __const_array_codegen-internals.main_arr
    %27 = std.ptr_to_i64 %26
    %28 = memref.load arr : i64
    %29 = memref.load_indirect %28+8
    memref.store_indirect %27, %29+0
    %30 = memref.load arr : i64
    std.call_runtime @mm_incref %30
    %31 = arith.constant {value = 0 : i64}
    %32 = arith.constant {value = 77 : i64}
    %33 = memref.load arr : i64
    func.call @IntArray.set %33, %31, %32
    %34 = arith.constant {value = 0 : i64}
    %35 = memref.load arr : i64
    %36, %37 = func.try_call @IntArray.get %35, %34
    %38 = arith.constant {value = 0 : i64}
    memref.store %38, __try_default_2
    memref.store %36, __try_result_1
    %39 = arith.constant {value = 0 : i64}
    %40 = arith.cmpi ne %37, %39
    cf.cond_br %40 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %41 = memref.load __try_default_2 : i64
    memref.store %41, __try_result_1
    cf.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %42 = memref.load __try_result_1 : i64
    memref.store %42, __range_val_5
    %43 = arith.constant {value = 0 : i64}
    %44 = arith.cmpi lt %42, %43
    %45 = arith.constant {value = 4294967295 : i64}
    %46 = arith.cmpi gt %42, %45
    %47 = arith.ori1 %44, %46
    cf.cond_br %47 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    %48 = memref.lea_symdata __panic_msg_26
    %49 = std.ptr_to_i64 %48
    std.call_runtime @maxon_panic %49
  __range_ok_5:
    %50 = memref.load __range_val_5 : i64
    %51 = memref.load arr : i64
    std.call_runtime @mm_decref %51
    %52 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %52
    func.return %50
  }
}
=== x86
module {
  func @codegen-internals.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.lea_symdata rax, [__tag_codegen-internals_main]
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor edx, edx
    x86.mov ebx, 1
    x86.xor esi, esi
    x86.mov edi, 8
    x86.lea_symdata r8, [__tag___ManagedMemory]
    x86.mov r9, r8
    x86.mov rdx, r9
    x86.mov rcx, 32
    x86.call mm_alloc
    x86.mov [rbp-16], eax
    x86.mov eax, [rbp-16]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.mov [eax+8], ecx
    x86.mov eax, [rbp-16]
    x86.xor ecx, ecx
    x86.mov [eax+16], ecx
    x86.mov eax, [rbp-16]
    x86.mov ecx, 8
    x86.mov [eax+24], ecx
    x86.xor eax, eax
    x86.lea_symdata rcx, [__tag_IntArray]
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.call mm_alloc
    x86.mov [rbp-24], eax
    x86.mov eax, [rbp-24]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-24]
    x86.mov [ecx+8], eax
    x86.mov ecx, [rbp-24]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rbp-24]
    x86.mov r8, 1
    x86.call mm_move
    x86.lea_rdata rax, [__const_array_codegen-internals.main_arr]
    x86.mov rcx, rax
    x86.mov eax, [rbp-24]
    x86.mov edx, [eax+8]
    x86.mov [edx+0], ecx
    x86.mov eax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov eax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.xor rdx, rdx
    x86.mov r8, 77
    x86.call IntArray.set
    x86.mov eax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.xor rdx, rdx
    x86.call IntArray.get
    x86.xor ecx, ecx
    x86.mov [rbp-32], ecx
    x86.mov [rbp-40], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je codegen-internals.main.otherwise_default_continue_4
  otherwise_default_error_3:
    x86.mov eax, [rbp-32]
    x86.mov [rbp-40], eax
    x86.jmp codegen-internals.main.otherwise_default_continue_4
  otherwise_default_continue_4:
    x86.mov eax, [rbp-40]
    x86.mov [rbp-48], eax
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
    x86.je codegen-internals.main.__range_ok_5
  __range_panic_5:
    x86.lea_symdata rax, [__panic_msg_26]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_5:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-24]
    x86.call mm_decref
    x86.mov eax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, [rbp-48]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: rdata-cow-multiple-mutations -->
```maxon
function main() returns ExitCode
  var arr = [1, 2, 3]
  arr.set(0, value: 10)
  arr.set(1, value: 20)
  arr.set(2, value: 30)
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
  var x = 5
  var arr = [1, x, 3]
  return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
5
```

<!-- test: managed-string-heap-string-generates-cleanup -->
```maxon
function main() returns ExitCode
  var s = "this is a heap allocated string!"
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
  var s = "heap allocated string here!!"
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
  var s = "short"
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
  var a = "a"
  var i = 0
  while i < 5 'loop'
    s = s.concat(a)
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
  var a = "hello world"
  var b = "hello world"
  var c = "hello world"
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
  var a = SmallInt{10}
  var b = SmallInt{3}
  return a + b
end 'main'
```
```exitcode
13
```
```RequiredMLIR
=== maxon
module {
  func @codegen-internals.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = codegen-internals.main}
    %1 = maxon.literal {value = 10 : i64}
    %2 = maxon.cast %1 {target = i16}
    maxon.assign %2 {var = a} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.cast %3 {target = i16}
    maxon.assign %4 {var = b} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %2, %4 {op = add}
    maxon.assign %5 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-unsigned-add.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %12 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %12
  }
}
=== standard
module {
  func @codegen-internals.main() -> u32 {
  entry:
    %1 = arith.constant {value = 10 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.addi %1, %2
    memref.store %3, __range_val_0
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.cmpi lt %3, %4
    %6 = arith.constant {value = 4294967295 : i64}
    %7 = arith.cmpi gt %3, %6
    %8 = arith.ori1 %5, %7
    cf.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %9 = memref.lea_symdata __panic_msg_11
    %10 = std.ptr_to_i64 %9
    std.call_runtime @maxon_panic %10
  __range_ok_0:
    %11 = memref.load __range_val_0 : i64
    func.return %11
  }
}
=== x86
module {
  func @codegen-internals.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 10
    x86.mov ecx, 3
    x86.add eax, ecx
    x86.mov [rbp-8], eax
    x86.xor edx, edx
    x86.cmp eax, edx
    x86.setl ebx
    x86.movzx ebx, ebxb
    x86.mov rsi, 4294967295
    x86.cmp rax, rsi
    x86.setg edi
    x86.movzx edi, edib
    x86.or ebx, edi
    x86.test ebx, ebx
    x86.je codegen-internals.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_11]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: i32-unsigned-div -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
  var a = SmallInt{20}
  var b = SmallInt{3}
  return a / b
end 'main'
```
```exitcode
6
```
```RequiredMLIR
=== maxon
module {
  func @codegen-internals.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = codegen-internals.main}
    %1 = maxon.literal {value = 20 : i64}
    %2 = maxon.cast %1 {target = i16}
    maxon.assign %2 {var = a} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.cast %3 {target = i16}
    maxon.assign %4 {var = b} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %2, %4 {op = div}
    maxon.assign %5 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-unsigned-div.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %12 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %12
  }
}
=== standard
module {
  func @codegen-internals.main() -> u32 {
  entry:
    %1 = arith.constant {value = 20 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.divsi %1, %2
    memref.store %3, __range_val_0
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.cmpi lt %3, %4
    %6 = arith.constant {value = 4294967295 : i64}
    %7 = arith.cmpi gt %3, %6
    %8 = arith.ori1 %5, %7
    cf.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %9 = memref.lea_symdata __panic_msg_11
    %10 = std.ptr_to_i64 %9
    std.call_runtime @maxon_panic %10
  __range_ok_0:
    %11 = memref.load __range_val_0 : i64
    func.return %11
  }
}
=== x86
module {
  func @codegen-internals.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 20
    x86.mov ecx, 3
    x86.cqo
    x86.idiv ecx
    x86.mov [rbp-8], eax
    x86.xor edx, edx
    x86.cmp eax, edx
    x86.setl ebx
    x86.movzx ebx, ebxb
    x86.mov rsi, 4294967295
    x86.cmp rax, rsi
    x86.setg edi
    x86.movzx edi, edib
    x86.or ebx, edi
    x86.test ebx, ebx
    x86.je codegen-internals.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_11]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: i32-signed-div -->
```maxon
typealias Temp = int(-100000 to 100000)

function main() returns ExitCode
  var a = Temp{20}
  var b = Temp{3}
  return a / b
end 'main'
```
```exitcode
6
```
```RequiredMLIR
=== maxon
module {
  func @codegen-internals.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = codegen-internals.main}
    %1 = maxon.literal {value = 20 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = div} {optimalType = i32}
    maxon.assign %3 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    %6 = maxon.literal {value = 4294967295 : i64}
    %7 = maxon.binop %3, %6 {op = gt}
    %8 = maxon.binop %5, %7 {op = or}
    maxon.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-signed-div.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %10 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %10
  }
}
=== standard
module {
  func @codegen-internals.main() -> u32 {
  entry:
    %1 = arith.constant {value = 20 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.trunci %1
    %4 = arith.trunci %2
    %5 = arith.divsi %3, %4
    memref.store %5, __range_val_0
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.extsi %5
    %8 = arith.cmpi lt %7, %6
    %9 = arith.constant {value = 4294967295 : i64}
    %10 = arith.extsi %5
    %11 = arith.cmpi gt %10, %9
    %12 = arith.ori1 %8, %11
    cf.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %13 = memref.lea_symdata __panic_msg_9
    %14 = std.ptr_to_i64 %13
    std.call_runtime @maxon_panic %14
  __range_ok_0:
    %15 = memref.load __range_val_0 : i32
    func.return %15
  }
}
=== x86
module {
  func @codegen-internals.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 20
    x86.mov ecx, 3
    x86.mov edx, eax
    x86.mov ebx, ecx
    x86.mov [rbp-12], edx
    x86.mov eax, edx
    x86.cdq
    x86.idiv32 ebx
    x86.mov [rbp-4], eax
    x86.xor esi, esi
    x86.movsxd rdi, eax
    x86.cmp edi, esi
    x86.setl r8
    x86.movzx r8, r8b
    x86.mov r9, 4294967295
    x86.movsxd rcx, eax
    x86.cmp rcx, r9
    x86.setg eax
    x86.movzx eax, eaxb
    x86.or r8, eax
    x86.test r8, r8
    x86.je codegen-internals.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_9]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-4]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: i32-unsigned-cmp -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
  var a = SmallInt{10}
  var b = SmallInt{3}
  if a > b 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```
```RequiredMLIR
=== maxon
module {
  func @codegen-internals.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = codegen-internals.main}
    %1 = maxon.literal {value = 10 : i64}
    %2 = maxon.cast %1 {target = i16}
    maxon.assign %2 {var = a} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.cast %3 {target = i16}
    maxon.assign %4 {var = b} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %2, %4 {op = gt}
    maxon.cond_br %5 [then: check_0, else: check_0.after]
  check_0:
    __scope_6 = maxon.scope_enter {tag = if_then}
    %7 = maxon.literal {value = 1 : i64}
    maxon.scope_exit {scope = __scope_6} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %7
  check_0.after:
    %8 = maxon.literal {value = 0 : i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %8
  }
}
=== standard
module {
  func @codegen-internals.main() -> u32 {
  entry:
    %1 = arith.constant {value = 10 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.cmpi gt %1, %2
    cf.cond_br %3 [then: check_0, else: check_0.after]
  check_0:
    %5 = arith.constant {value = 1 : i64}
    func.return %5
  check_0.after:
    %6 = arith.constant {value = 0 : i64}
    func.return %6
  }
}
=== x86
module {
  func @codegen-internals.main() -> u32 {
  entry:
    x86.mov eax, 10
    x86.mov ecx, 3
    x86.cmp eax, ecx
    x86.jle codegen-internals.main.check_0.after
  check_0:
    x86.mov eax, 1
    x86.ret
  check_0.after:
    x86.xor eax, eax
    x86.ret
  }
}
```

<!-- test: i32-unsigned-mod -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
  var a = SmallInt{20}
  var b = SmallInt{3}
  return a mod b
end 'main'
```
```exitcode
2
```
```RequiredMLIR
=== maxon
module {
  func @codegen-internals.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = codegen-internals.main}
    %1 = maxon.literal {value = 20 : i64}
    %2 = maxon.cast %1 {target = i16}
    maxon.assign %2 {var = a} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.cast %3 {target = i16}
    maxon.assign %4 {var = b} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %2, %4 {op = mod}
    maxon.assign %5 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-unsigned-mod.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %12 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %12
  }
}
=== standard
module {
  func @codegen-internals.main() -> u32 {
  entry:
    %1 = arith.constant {value = 20 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.remsi %1, %2
    memref.store %3, __range_val_0
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.cmpi lt %3, %4
    %6 = arith.constant {value = 4294967295 : i64}
    %7 = arith.cmpi gt %3, %6
    %8 = arith.ori1 %5, %7
    cf.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %9 = memref.lea_symdata __panic_msg_11
    %10 = std.ptr_to_i64 %9
    std.call_runtime @maxon_panic %10
  __range_ok_0:
    %11 = memref.load __range_val_0 : i64
    func.return %11
  }
}
=== x86
module {
  func @codegen-internals.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 20
    x86.mov ecx, 3
    x86.cqo
    x86.idiv ecx
    x86.mov [rbp-8], edx
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rcx, 4294967295
    x86.cmp rdx, rcx
    x86.setg edx
    x86.movzx edx, edxb
    x86.or eax, edx
    x86.test eax, eax
    x86.je codegen-internals.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_11]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: i64-signed-no-narrowing -->
```maxon
typealias BigInt = int(-1000000000000 to 1000000000000)

function main() returns ExitCode
  var a = BigInt{20}
  var b = BigInt{3}
  return a / b
end 'main'
```
```exitcode
6
```
```RequiredMLIR
=== maxon
module {
  func @codegen-internals.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = codegen-internals.main}
    %1 = maxon.literal {value = 20 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = div} {optimalType = i64}
    maxon.assign %3 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    %6 = maxon.literal {value = 4294967295 : i64}
    %7 = maxon.binop %3, %6 {op = gt}
    %8 = maxon.binop %5, %7 {op = or}
    maxon.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i64-signed-no-narrowing.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %10 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %10
  }
}
=== standard
module {
  func @codegen-internals.main() -> u32 {
  entry:
    %1 = arith.constant {value = 20 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.divsi %1, %2
    memref.store %3, __range_val_0
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.cmpi lt %3, %4
    %6 = arith.constant {value = 4294967295 : i64}
    %7 = arith.cmpi gt %3, %6
    %8 = arith.ori1 %5, %7
    cf.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %9 = memref.lea_symdata __panic_msg_9
    %10 = std.ptr_to_i64 %9
    std.call_runtime @maxon_panic %10
  __range_ok_0:
    %11 = memref.load __range_val_0 : i64
    func.return %11
  }
}
=== x86
module {
  func @codegen-internals.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 20
    x86.mov ecx, 3
    x86.cqo
    x86.idiv ecx
    x86.mov [rbp-8], eax
    x86.xor edx, edx
    x86.cmp eax, edx
    x86.setl ebx
    x86.movzx ebx, ebxb
    x86.mov rsi, 4294967295
    x86.cmp rax, rsi
    x86.setg edi
    x86.movzx edi, edib
    x86.or ebx, edi
    x86.test ebx, ebx
    x86.je codegen-internals.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_9]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: i8-range-uses-i32-arithmetic -->
```maxon
typealias Tiny = int(0 to 100)

function main() returns ExitCode
  var a = Tiny{21}
  var b = Tiny{3}
  return a / b
end 'main'
```
```exitcode
7
```
```RequiredMLIR
=== maxon
module {
  func @codegen-internals.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = codegen-internals.main}
    %1 = maxon.literal {value = 21 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = div} {optimalType = u8}
    maxon.assign %3 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    %6 = maxon.literal {value = 4294967295 : i64}
    %7 = maxon.binop %3, %6 {op = gt}
    %8 = maxon.binop %5, %7 {op = or}
    maxon.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i8-range-uses-i32-arithmetic.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %10 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %10
  }
}
=== standard
module {
  func @codegen-internals.main() -> u32 {
  entry:
    %1 = arith.constant {value = 21 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.trunci %1
    %4 = arith.trunci %2
    %5 = arith.divui %3, %4
    memref.store %5, __range_val_0
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.extui %5
    %8 = arith.cmpi lt %7, %6
    %9 = arith.constant {value = 4294967295 : i64}
    %10 = arith.extui %5
    %11 = arith.cmpi gt %10, %9
    %12 = arith.ori1 %8, %11
    cf.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %13 = memref.lea_symdata __panic_msg_9
    %14 = std.ptr_to_i64 %13
    std.call_runtime @maxon_panic %14
  __range_ok_0:
    %15 = memref.load __range_val_0 : i32
    func.return %15
  }
}
=== x86
module {
  func @codegen-internals.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 21
    x86.mov ecx, 3
    x86.mov edx, eax
    x86.mov ebx, ecx
    x86.mov [rbp-12], edx
    x86.mov eax, edx
    x86.xor edx, edx
    x86.div32 ebx
    x86.mov [rbp-4], eax
    x86.xor esi, esi
    x86.mov edi, eax
    x86.cmp edi, esi
    x86.setl r8
    x86.movzx r8, r8b
    x86.mov r9, 4294967295
    x86.mov ecx, eax
    x86.cmp rcx, r9
    x86.setg eax
    x86.movzx eax, eaxb
    x86.or r8, eax
    x86.test r8, r8
    x86.je codegen-internals.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_9]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-4]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: f32-arithmetic-uses-ss-instructions -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
  var a = F{10.0}
  var b = F{3.0}
  return trunc(a + b)
end 'main'
```
```exitcode
13
```
```RequiredMLIR
=== maxon
module {
  func @codegen-internals.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = codegen-internals.main}
    %1 = maxon.literal {value = 10 : f64}
    maxon.assign %1 {var = a} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : f64}
    maxon.assign %2 {var = b} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add} {kind = f64}
    %4 = maxon.trunc %3
    maxon.assign %4 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at f32-arithmetic-uses-ss-instructions.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %11 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %11
  }
}
=== standard
module {
  func @codegen-internals.main() -> u32 {
  entry:
    %1 = arith.float_constant {value = 10 : f64}
    %2 = arith.float_constant {value = 3 : f64}
    %3 = arith.addf %1, %2
    %4 = arith.fptosi %3
    memref.store %4, __range_val_0
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.cmpi lt %4, %5
    %7 = arith.constant {value = 4294967295 : i64}
    %8 = arith.cmpi gt %4, %7
    %9 = arith.ori1 %6, %8
    cf.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %10 = memref.lea_symdata __panic_msg_10
    %11 = std.ptr_to_i64 %10
    std.call_runtime @maxon_panic %11
  __range_ok_0:
    %12 = memref.load __range_val_0 : i64
    func.return %12
  }
}
=== x86
module {
  func @codegen-internals.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.movsd xmm0, [rip+__float_10]
    x86.movsd xmm1, [rip+__float_3]
    x86.movsd xmm2, xmm0
    x86.addsd xmm2, xmm1
    x86.cvttsd2si eax, xmm2
    x86.mov [rbp-8], eax
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
    x86.je codegen-internals.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_10]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: f32-comparison-uses-ucomiss -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
  var a = F{3.0}
  var b = F{5.0}
  if a < b 'less'
    return 1
  end 'less'
  return 0
end 'main'
```
```exitcode
1
```
```RequiredMLIR
=== maxon
module {
  func @codegen-internals.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = codegen-internals.main}
    %1 = maxon.literal {value = 3 : f64}
    maxon.assign %1 {var = a} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 5 : f64}
    maxon.assign %2 {var = b} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = lt} {kind = f64}
    maxon.cond_br %3 [then: less_0, else: less_0.after]
  less_0:
    __scope_4 = maxon.scope_enter {tag = if_then}
    %5 = maxon.literal {value = 1 : i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %5
  less_0.after:
    %6 = maxon.literal {value = 0 : i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %6
  }
}
=== standard
module {
  func @codegen-internals.main() -> u32 {
  entry:
    %1 = arith.float_constant {value = 3 : f64}
    %2 = arith.float_constant {value = 5 : f64}
    %3 = arith.cmpf lt %1, %2
    cf.cond_br %3 [then: less_0, else: less_0.after]
  less_0:
    %5 = arith.constant {value = 1 : i64}
    func.return %5
  less_0.after:
    %6 = arith.constant {value = 0 : i64}
    func.return %6
  }
}
=== x86
module {
  func @codegen-internals.main() -> u32 {
  entry:
    x86.movsd xmm0, [rip+__float_3]
    x86.movsd xmm1, [rip+__float_5]
    x86.ucomisd xmm0, xmm1
    x86.jp codegen-internals.main.less_0.after
    x86.jae codegen-internals.main.less_0.after
  less_0:
    x86.mov eax, 1
    x86.ret
  less_0.after:
    x86.xor eax, eax
    x86.ret
  }
}
```

<!-- test: f32-truncation-uses-cvttss2si -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
  var a = F{42.9}
  return trunc(a)
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @codegen-internals.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = codegen-internals.main}
    %1 = maxon.literal {value = 42.9 : f64}
    maxon.assign %1 {var = a} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.trunc %1
    maxon.assign %2 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at f32-truncation-uses-cvttss2si.test:6: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %9 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %9
  }
}
=== standard
module {
  func @codegen-internals.main() -> u32 {
  entry:
    %1 = arith.float_constant {value = 42.9 : f64}
    %2 = arith.fptosi %1
    memref.store %2, __range_val_0
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_8
    %9 = std.ptr_to_i64 %8
    std.call_runtime @maxon_panic %9
  __range_ok_0:
    %10 = memref.load __range_val_0 : i64
    func.return %10
  }
}
=== x86
module {
  func @codegen-internals.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.movsd xmm0, [rip+__float_42.9]
    x86.cvttsd2si eax, xmm0
    x86.mov [rbp-8], eax
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
    x86.je codegen-internals.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_8]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```
