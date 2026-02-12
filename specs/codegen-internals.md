---
feature: codegen-internals
status: stable
keywords: [rdata, cow, managed-memory, strings, stack-probing]
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

<!-- disabled-test: stack-probing-large-struct-recursive -->
<!-- NOTE: This test requires runtime execution with a struct that has 2000 fields.
     The source is generated programmatically and cannot be expressed inline.
     See docs/SPECS.md documentation section on stack probing. -->
```maxon
function main() returns int
  return 0
end 'main'
```
```exitcode
0
```

<!-- disabled-test: managed-memory-heap-array-generates-free -->
```maxon
typealias IntArray = Array with int

function main() returns int
  var arr = IntArray{}
  arr.push(1)
  arr.push(2)
  return arr.count()
end 'main'
```
```exitcode
2
```

<!-- disabled-test: managed-memory-scope-cleanup-generates-free -->
```maxon
typealias IntArray = Array with int

function main() returns int
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

<!-- disabled-test: managed-memory-loop-growth-generates-realloc -->
```maxon
typealias IntArray = Array with int

function main() returns int
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

<!-- disabled-test: managed-memory-fixed-size-array-literal-cleanup -->
```maxon
function main() returns int
  var arr = [10, 20, 30]
  return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: rdata-constant-array-uses-rdata -->
```maxon
function main() returns int
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
function main() returns int
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
typealias ByteArray = Array with byte

function main() returns int
  let arr = [10 as byte, 20 as byte, 30 as byte]
  var v0 = try arr.get(0) otherwise 0 as byte
  var v1 = try arr.get(1) otherwise 0 as byte
  var v2 = try arr.get(2) otherwise 0 as byte
  return (v0 as int) + (v1 as int) + (v2 as int)
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
function main() returns int
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
    %0 = maxon.literal {value = 42 : i64}
    maxon.assign %0 {var = __arr_0.0} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.literal {value = 1 : i64}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.literal {value = 8 : i64}
    %5 = maxon.struct_literal @__ManagedMemory
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.struct_literal @IntArray
    maxon.assign %7 {var = arr} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.literal {value = 77 : i64}
    maxon.call @IntArray.set %7, %8, %9
    %10 = maxon.struct_var_ref arr
    %11 = maxon.literal {value = 0 : i64}
    %14, %13 = maxon.try_call @IntArray.get %10, %11
    %15 = maxon.literal {value = 0 : i64}
    maxon.assign %15 {var = __try_default_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %14 {var = __try_result_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 0 : i64}
    %17 = maxon.binop %13, %16 {op = ne} {kind = i64}
    maxon.cond_br %17 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %18 = maxon.var_ref {var = __try_default_2} {type = i64}
    maxon.assign %18 {var = __try_result_1} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %19 = maxon.var_ref {var = __try_result_1} {type = i64}
    maxon.return %19
  }
}
=== standard
module {
  func @codegen-internals.main() -> i64 {
  entry:
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.constant {value = 8 : i64}
    %5 = arith.constant {value = 32 : i64}
    %6 = std.call_runtime @maxon_alloc %5
    memref.store %6, __struct_5
    %7 = memref.load __struct_5 : i64
    memref.store_indirect %1, %7+0
    %8 = memref.load __struct_5 : i64
    memref.store_indirect %2, %8+8
    %9 = memref.load __struct_5 : i64
    memref.store_indirect %3, %9+16
    %10 = memref.load __struct_5 : i64
    memref.store_indirect %4, %10+24
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.constant {value = 16 : i64}
    %13 = std.call_runtime @maxon_alloc %12
    memref.store %13, arr
    %14 = memref.load arr : i64
    memref.store_indirect %11, %14+0
    %15 = memref.load __struct_5 : i64
    %16 = memref.load arr : i64
    memref.store_indirect %15, %16+8
    %17 = memref.lea_rdata __const_array_codegen-internals.main_arr
    %18 = std.ptr_to_i64 %17
    %19 = memref.load arr : i64
    %20 = memref.load_indirect %19+8
    memref.store_indirect %18, %20+0
    %21 = arith.constant {value = 0 : i64}
    %22 = arith.constant {value = 77 : i64}
    %23 = memref.load arr : i64
    func.call @IntArray.set %23, %21, %22
    %24 = arith.constant {value = 0 : i64}
    %25 = memref.load arr : i64
    %26, %27 = func.try_call @IntArray.get %25, %24
    %28 = arith.constant {value = 0 : i64}
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
    func.return %32
  }
}
=== x86
module {
  func @codegen-internals.main() -> i64 {
  entry:
    x86.prologue stack_size=80
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.xor edx, edx
    x86.mov ebx, 8
    x86.mov esi, 32
    x86.mov [rbp-40], eax
    x86.mov [rbp-48], ecx
    x86.mov [rbp-56], edx
    x86.mov [rbp-64], ebx
    x86.mov rcx, rsi
    x86.call maxon_alloc
    x86.mov [rbp-8], eax
    x86.mov edi, [rbp-8]
    x86.mov r8, [rbp-40]
    x86.mov [edi+0], r8
    x86.mov r9, [rbp-8]
    x86.mov eax, [rbp-48]
    x86.mov [r9+8], eax
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-56]
    x86.mov [eax+16], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-64]
    x86.mov [eax+24], ecx
    x86.xor eax, eax
    x86.mov ecx, 16
    x86.mov [rbp-72], eax
    x86.call maxon_alloc
    x86.mov [rbp-16], eax
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-72]
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.mov [ecx+8], eax
    x86.lea_rdata rax, [__const_array_codegen-internals.main_arr]
    x86.mov rcx, rax
    x86.mov eax, [rbp-16]
    x86.mov edx, [eax+8]
    x86.mov [edx+0], ecx
    x86.xor eax, eax
    x86.mov ecx, 77
    x86.mov edx, [rbp-16]
    x86.mov r8, rcx
    x86.mov rcx, rdx
    x86.mov rdx, rax
    x86.call IntArray.set
    x86.xor eax, eax
    x86.mov ecx, [rbp-16]
    x86.mov rdx, rax
    x86.call IntArray.get
    x86.xor ecx, ecx
    x86.mov [rbp-24], ecx
    x86.mov [rbp-32], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je codegen-internals.main.otherwise_default_continue_4
  otherwise_default_error_3:
    x86.mov eax, [rbp-24]
    x86.mov [rbp-32], eax
    x86.jmp codegen-internals.main.otherwise_default_continue_4
  otherwise_default_continue_4:
    x86.mov eax, [rbp-32]
    x86.epilogue
    x86.ret
  }
}
```

<!-- disabled-test: rdata-cow-multiple-mutations -->
```maxon
function main() returns int
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

<!-- disabled-test: rdata-non-constant-array-uses-heap -->
```maxon
function main() returns int
  var x = 5
  var arr = [1, x, 3]
  return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
5
```

<!-- disabled-test: managed-string-heap-string-generates-cleanup -->
```maxon
function main() returns int
  var s = "this is a heap allocated string!"
  return s.byteLength()
end 'main'
```
```exitcode
31
```
```RequiredRdata
utf8 "this is a heap allocated string!\0"
```

<!-- disabled-test: managed-string-reassignment-handles-old-value -->
```maxon
function main() returns int
  var s = "first heap allocated value!!"
  s = "second heap allocated here!!"
  return s.byteLength()
end 'main'
```
```exitcode
27
```
```RequiredRdata
utf8 "first heap allocated value!!\0"
utf8 "second heap allocated here!!\0"
```

<!-- disabled-test: managed-string-substring-retains-parent -->
```maxon
function main() returns int
  var s = "hello world from heap!!"
  var subManaged = __managed_memory_slice(s._managed, 0, 5)
  return __managed_memory_len(subManaged)
end 'main'
```
```exitcode
5
```

<!-- disabled-test: managed-string-print-heap-string -->
```maxon
function main() returns int
  var s = "heap allocated string here!!"
  return s.byteLength()
end 'main'
```
```exitcode
27
```
```RequiredRdata
utf8 "heap allocated string here!!\0"
```

<!-- disabled-test: managed-string-short-string-sso -->
```maxon
function main() returns int
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

<!-- disabled-test: managed-string-loop-concatenation-cleanup -->
```maxon
function main() returns int
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

<!-- disabled-test: managed-string-literal-deduplication -->
```maxon
function main() returns int
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
