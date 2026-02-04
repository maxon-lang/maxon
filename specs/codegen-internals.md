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
typealias IntArray is Array with int

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
typealias IntArray is Array with int

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
typealias IntArray is Array with int

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
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 42 : i64}
    maxon.assign %0 {var = __arr_0.0} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.literal {value = 1 : i64}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.struct_literal @__ManagedMemory
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.struct_literal @Array
    maxon.assign %6 {var = arr} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 0 : i64}
    %8 = maxon.literal {value = 77 : i64}
    maxon.call @Array.set %6, %7, %8
    %9 = maxon.literal {value = 0 : i64}
    %12, %11 = maxon.try_call @Array.get %6, %9
    %13 = maxon.literal {value = 0 : i64}
    maxon.assign %13 {var = __try_default_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %12 {var = __try_result_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %11, %14 {op = ne} {kind = i64}
    maxon.cond_br %15 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %16 = maxon.var_ref {var = __try_default_2} {type = i64}
    maxon.assign %16 {var = __try_result_1} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %17 = maxon.var_ref {var = __try_result_1} {type = i64}
    maxon.return %17
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %0 = arith.constant {value = 42 : i64}
    memref.store %0, __arr_0.0
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 0 : i64}
    memref.store %1, __struct_4.buffer
    memref.store %2, __struct_4.length
    memref.store %3, __struct_4.capacity
    %4 = arith.constant {value = 0 : i64}
    memref.store %4, __struct_6.iterIndex
    %5 = memref.load __struct_4.buffer : i64
    memref.store %5, __struct_6.managed.buffer
    %6 = memref.load __struct_4.length : i64
    memref.store %6, __struct_6.managed.length
    %7 = memref.load __struct_4.capacity : i64
    memref.store %7, __struct_6.managed.capacity
    %8 = memref.lea_rdata __const_array_main_arr
    %9 = std.ptr_to_i64 %8
    memref.store %9, __struct_6.managed.buffer
    %10 = memref.load __struct_6.iterIndex : i64
    memref.store %10, arr.iterIndex
    %11 = memref.load __struct_6.managed.buffer : i64
    memref.store %11, arr.managed.buffer
    %12 = memref.load __struct_6.managed.length : i64
    memref.store %12, arr.managed.length
    %13 = memref.load __struct_6.managed.capacity : i64
    memref.store %13, arr.managed.capacity
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.constant {value = 77 : i64}
    %17 = memref.load arr.managed.capacity : i64
    memref.store %17, __selfbuf_16.managed.capacity
    %18 = memref.load arr.managed.length : i64
    memref.store %18, __selfbuf_16.managed.length
    %19 = memref.load arr.managed.buffer : i64
    memref.store %19, __selfbuf_16.managed.buffer
    %20 = memref.load arr.iterIndex : i64
    memref.store %20, __selfbuf_16.iterIndex
    %21 = memref.lea __selfbuf_16
    func.call @Array.set %21, %14, %15
    %22 = memref.load __selfbuf_16.iterIndex : i64
    memref.store %22, arr.iterIndex
    %23 = memref.load __selfbuf_16.managed.buffer : i64
    memref.store %23, arr.managed.buffer
    %24 = memref.load __selfbuf_16.managed.length : i64
    memref.store %24, arr.managed.length
    %25 = memref.load __selfbuf_16.managed.capacity : i64
    memref.store %25, arr.managed.capacity
    %26 = arith.constant {value = 0 : i64}
    %28 = memref.load arr.managed.capacity : i64
    memref.store %28, __selfbuf_27.managed.capacity
    %29 = memref.load arr.managed.length : i64
    memref.store %29, __selfbuf_27.managed.length
    %30 = memref.load arr.managed.buffer : i64
    memref.store %30, __selfbuf_27.managed.buffer
    %31 = memref.load arr.iterIndex : i64
    memref.store %31, __selfbuf_27.iterIndex
    %32 = memref.lea __selfbuf_27
    %33, %34 = func.try_call @Array.get %32, %26
    memref.store %34, __error_flag
    %35 = memref.load __selfbuf_27.iterIndex : i64
    memref.store %35, arr.iterIndex
    %36 = memref.load __selfbuf_27.managed.buffer : i64
    memref.store %36, arr.managed.buffer
    %37 = memref.load __selfbuf_27.managed.length : i64
    memref.store %37, arr.managed.length
    %38 = memref.load __selfbuf_27.managed.capacity : i64
    memref.store %38, arr.managed.capacity
    %39 = arith.constant {value = 0 : i64}
    memref.store %39, __try_default_2
    memref.store %33, __try_result_1
    %40 = arith.constant {value = 0 : i64}
    %41 = arith.cmpi ne %34, %40
    cf.cond_br %41 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %42 = memref.load __try_default_2 : i64
    memref.store %42, __try_result_1
    cf.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %43 = memref.load __try_result_1 : i64
    func.return %43
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=192
    x86.mov eax, 42
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov edx, 1
    x86.xor ebx, ebx
    x86.mov [rbp-16], ecx
    x86.mov [rbp-24], edx
    x86.mov [rbp-32], ebx
    x86.xor esi, esi
    x86.mov [rbp-40], esi
    x86.mov edi, [rbp-16]
    x86.mov [rbp-48], edi
    x86.mov r8, [rbp-24]
    x86.mov [rbp-56], r8
    x86.mov r9, [rbp-32]
    x86.mov [rbp-64], r9
    x86.lea_rdata rax, [__const_array_main_arr]
    x86.mov rcx, rax
    x86.mov [rbp-48], ecx
    x86.mov eax, [rbp-40]
    x86.mov [rbp-72], eax
    x86.mov eax, [rbp-48]
    x86.mov [rbp-80], eax
    x86.mov eax, [rbp-56]
    x86.mov [rbp-88], eax
    x86.mov eax, [rbp-64]
    x86.mov [rbp-96], eax
    x86.xor eax, eax
    x86.mov ecx, 77
    x86.mov edx, [rbp-96]
    x86.mov [rbp-104], edx
    x86.mov edx, [rbp-88]
    x86.mov [rbp-112], edx
    x86.mov edx, [rbp-80]
    x86.mov [rbp-120], edx
    x86.mov edx, [rbp-72]
    x86.mov [rbp-128], edx
    x86.lea rdx, [rbp-128]
    x86.mov r8, rcx
    x86.mov rcx, rdx
    x86.mov rdx, rax
    x86.call Array.set
    x86.mov eax, [rbp-128]
    x86.mov [rbp-72], eax
    x86.mov eax, [rbp-120]
    x86.mov [rbp-80], eax
    x86.mov eax, [rbp-112]
    x86.mov [rbp-88], eax
    x86.mov eax, [rbp-104]
    x86.mov [rbp-96], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-96]
    x86.mov [rbp-136], ecx
    x86.mov ecx, [rbp-88]
    x86.mov [rbp-144], ecx
    x86.mov ecx, [rbp-80]
    x86.mov [rbp-152], ecx
    x86.mov ecx, [rbp-72]
    x86.mov [rbp-160], ecx
    x86.lea rcx, [rbp-160]
    x86.mov rdx, rax
    x86.call Array.get
    x86.mov [rbp-168], edx
    x86.mov ecx, [rbp-160]
    x86.mov [rbp-72], ecx
    x86.mov ecx, [rbp-152]
    x86.mov [rbp-80], ecx
    x86.mov ecx, [rbp-144]
    x86.mov [rbp-88], ecx
    x86.mov ecx, [rbp-136]
    x86.mov [rbp-96], ecx
    x86.xor ecx, ecx
    x86.mov [rbp-176], ecx
    x86.mov [rbp-184], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je main.otherwise_default_continue_4
  otherwise_default_error_3:
    x86.mov eax, [rbp-176]
    x86.mov [rbp-184], eax
    x86.jmp main.otherwise_default_continue_4
  otherwise_default_continue_4:
    x86.mov eax, [rbp-184]
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
