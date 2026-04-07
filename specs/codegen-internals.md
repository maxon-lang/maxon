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
	var v = BigVec.create()
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

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function main() returns ExitCode
	let arr = [10 as Byte, 20 as Byte, 30 as Byte]
	let v0 = try arr.get(0) otherwise 0 as Byte
	let v1 = try arr.get(1) otherwise 0 as Byte
	let v2 = try arr.get(2) otherwise 0 as Byte
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
<!-- MmTrace -->
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
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory #1 size=32 [main]
  sl_alloc __ManagedMemory #1 size=64 class=5
mm_alloc IntArray #2 size=16 [main]
  sl_alloc IntArray #2 size=48 class=4
mm_incref __ManagedMemory #1 rc=1 [main]
mm_incref IntArray #2 rc=1 [main]
mm_raw_alloc #R1 size=8
  sl_alloc size=8 class=0
mm_cow __ManagedMemory #1 rc=1 size=8
mm_decref IntArray #2 rc=0 [main]
  mm_decref __ManagedMemory #1 rc=0 [~IntArray]
    mm_raw_free #R1
      sl_free size=8 class=0
    mm_free __ManagedMemory #1
      sl_free __ManagedMemory #1 size=64 class=5
  mm_free IntArray #2
    sl_free IntArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```
```RequiredRdata
i64 42
```
```RequiredMLIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
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
    maxon.panic "panic at rdata-cow-mutation-copies-to-heap.test:8: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_5:
    maxon.scope_end [arr, __try_default_2, __try_result_1]
    maxon.return %19
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.constant {value = 8 : i64}
    %5 = arith.constant {value = 32 : i64}
    %6 = func.ref @__destruct___ManagedMemory
    %7 = std.ptr_to_i64 %6
    %8 = arith.constant {value = 1 : i64}
    %9 = std.call_runtime @mm_alloc %5, %7, %8
    memref.store %9, __struct_5
    %10 = memref.load __struct_5 : i64
    memref.store_indirect %1, %10+0
    %11 = memref.load __struct_5 : i64
    memref.store_indirect %2, %11+8
    %12 = memref.load __struct_5 : i64
    memref.store_indirect %3, %12+16
    %13 = memref.load __struct_5 : i64
    memref.store_indirect %4, %13+24
    %14 = memref.load __struct_5 : i64
    %15 = memref.load_indirect %14+24
    %16 = arith.constant {value = 0 : i64}
    %17 = memref.lea_symdata __mm_panic_element_size_zero
    %18 = std.ptr_to_i64 %17
    std.call_runtime @maxon_bounds_check %16, %15, %18
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.constant {value = 16 : i64}
    %21 = func.ref @__destruct_IntArray
    %22 = std.ptr_to_i64 %21
    %23 = arith.constant {value = 2 : i64}
    %24 = std.call_runtime @mm_alloc %20, %22, %23
    memref.store %24, arr
    %25 = memref.load arr : i64
    memref.store_indirect %19, %25+0
    %26 = memref.load __struct_5 : i64
    %27 = memref.load arr : i64
    memref.store_indirect %26, %27+8
    std.call_runtime @mm_incref %26
    %28 = memref.lea_rdata __const_array_main_arr
    %29 = std.ptr_to_i64 %28
    %30 = memref.load arr : i64
    %31 = memref.load_indirect %30+8
    memref.store_indirect %29, %31+0
    %32 = memref.load arr : i64
    std.call_runtime @mm_incref %32
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.constant {value = 77 : i64}
    %35 = memref.load arr : i64
    func.call @IntArray.set %35, %33, %34
    %36 = arith.constant {value = 0 : i64}
    %37 = memref.load arr : i64
    %38, %39 = func.try_call @IntArray.get %37, %36
    %40 = arith.constant {value = 0 : i64}
    memref.store %40, __try_default_2
    memref.store %38, __try_result_1
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.cmpi ne %39, %41
    cf.cond_br %42 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %43 = memref.load __try_default_2 : i64
    memref.store %43, __try_result_1
    cf.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %44 = memref.load __try_result_1 : i64
    %45 = arith.constant {value = 0 : i64}
    %46 = arith.cmpi lt %44, %45
    %47 = arith.constant {value = 4294967295 : i64}
    %48 = arith.cmpi gt %44, %47
    %49 = arith.ori1 %46, %48
    cf.cond_br %49 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    %50 = memref.lea_symdata __panic_msg_0
    %51 = std.ptr_to_i64 %50
    std.call_runtime @mrt_panic %51
  __range_ok_5:
    %52 = memref.load arr : i64
    std.call_runtime_if_nonnull @mm_decref %52
    func.return %44
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %123 = func.param ptr : StdI64
    memref.store %123, __destr_ptr
    %126 = memref.load __destr_ptr : i64
    %127 = memref.load_indirect %126+16
    %128 = arith.constant {value = 0 : i64}
    %129 = arith.cmpi ne %127, %128
    cf.cond_br %129 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %130 = memref.load __destr_ptr : i64
    %131 = memref.load_indirect %130+0
    std.call_runtime @mm_raw_free %131
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_IntArray(ptr: i64) {
  entry:
    %132 = func.param ptr : StdI64
    memref.store %132, __destr_ptr
    %133 = memref.load __destr_ptr : i64
    %134 = memref.load_indirect %133+8
    std.call_runtime_if_nonnull @mm_decref %134
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
    x64.xor eax, eax
    x64.mov rcx, 1
    x64.xor edx, edx
    x64.mov rbx, 8
    x64.lea_func rsi, [__destruct___ManagedMemory]
    x64.mov rdi, rsi
    x64.mov rdx, rdi
    x64.mov rcx, 32
    x64.mov r8, 1
    x64.call mm_alloc
    x64.mov [rbp-8], rax
    x64.mov r8, [rbp-8]
    x64.xor r9d, r9d
    x64.mov [r8+0], r9
    x64.mov rax, [rbp-8]
    x64.mov rcx, 1
    x64.mov [rax+8], rcx
    x64.mov rax, [rbp-8]
    x64.xor ecx, ecx
    x64.mov [rax+16], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, 8
    x64.mov [rax+24], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+24]
    x64.lea_symdata rax, [__mm_panic_element_size_zero]
    x64.mov rdx, rax
    x64.mov r8, rdx
    x64.mov rdx, rcx
    x64.xor ecx, ecx
    x64.call maxon_bounds_check
    x64.xor eax, eax
    x64.lea_func rcx, [__destruct_IntArray]
    x64.mov rdx, rcx
    x64.mov rcx, 16
    x64.mov r8, 2
    x64.call mm_alloc
    x64.mov [rbp-16], rax
    x64.mov rax, [rbp-16]
    x64.xor ecx, ecx
    x64.mov [rax+0], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.mov [rcx+8], rax
    x64.mov rcx, [rbp-8]
    x64.call mm_incref
    x64.lea_rdata rax, [__const_array_main_arr]
    x64.mov rcx, rax
    x64.mov rax, [rbp-16]
    x64.mov rdx, [rax+8]
    x64.mov [rdx+0], rcx
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rbp-16]
    x64.call mm_incref
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rbp-16]
    x64.xor edx, edx
    x64.mov r8, 77
    x64.call IntArray.set
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rbp-16]
    x64.xor edx, edx
    x64.call IntArray.get
    x64.xor ecx, ecx
    x64.mov [rbp-24], rcx
    x64.mov [rbp-32], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je main.otherwise_default_continue_4
  otherwise_default_error_3:
    x64.mov rax, [rbp-24]
    x64.mov [rbp-32], rax
    x64.jmp main.otherwise_default_continue_4
  otherwise_default_continue_4:
    x64.mov rax, [rbp-32]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_5
    x64.cmp rax, rcx
    x64.jl main.__range_panic_5
    x64.jmp main.__range_ok_5
  __range_panic_5:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_5:
    x64.mov rbx, [rbp-16]
    x64.test rbx, rbx
    x64.jz __nonnull_skip_0
    x64.mov rcx, [rbp-16]
    x64.call mm_decref
    x64.label __nonnull_skip_0
    x64.mov rax, [rbp-32]
    x64.epilogue
    x64.ret
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.xor edx, edx
    x64.cmp rcx, rdx
    x64.je __destruct___ManagedMemory.skip_buf_0
  free_buf_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.call mm_raw_free
    x64.jmp __destruct___ManagedMemory.skip_buf_0
  skip_buf_0:
    x64.jmp __destruct___ManagedMemory.done
  done:
    x64.epilogue
    x64.ret
  }
  func @__destruct_IntArray(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+8]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_1
    x64.call mm_decref
    x64.label __nonnull_skip_1
    x64.jmp __destruct_IntArray.done
  done:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredMLIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
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
    maxon.panic "panic at rdata-cow-mutation-copies-to-heap.test:8: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_5:
    maxon.scope_end [arr, __try_default_2, __try_result_1]
    maxon.return %19
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.constant {value = 8 : i64}
    %5 = arith.constant {value = 32 : i64}
    %6 = func.ref @__destruct___ManagedMemory
    %7 = std.ptr_to_i64 %6
    %8 = arith.constant {value = 1 : i64}
    %9 = std.call_runtime @mm_alloc %5, %7, %8
    memref.store %9, __struct_5
    %10 = memref.load __struct_5 : i64
    memref.store_indirect %1, %10+0
    %11 = memref.load __struct_5 : i64
    memref.store_indirect %2, %11+8
    %12 = memref.load __struct_5 : i64
    memref.store_indirect %3, %12+16
    %13 = memref.load __struct_5 : i64
    memref.store_indirect %4, %13+24
    %14 = memref.load __struct_5 : i64
    %15 = memref.load_indirect %14+24
    %16 = arith.constant {value = 0 : i64}
    %17 = memref.lea_symdata __mm_panic_element_size_zero
    %18 = std.ptr_to_i64 %17
    std.call_runtime @maxon_bounds_check %16, %15, %18
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.constant {value = 16 : i64}
    %21 = func.ref @__destruct_IntArray
    %22 = std.ptr_to_i64 %21
    %23 = arith.constant {value = 2 : i64}
    %24 = std.call_runtime @mm_alloc %20, %22, %23
    memref.store %24, arr
    %25 = memref.load arr : i64
    memref.store_indirect %19, %25+0
    %26 = memref.load __struct_5 : i64
    %27 = memref.load arr : i64
    memref.store_indirect %26, %27+8
    std.call_runtime @mm_incref %26
    %28 = memref.lea_rdata __const_array_main_arr
    %29 = std.ptr_to_i64 %28
    %30 = memref.load arr : i64
    %31 = memref.load_indirect %30+8
    memref.store_indirect %29, %31+0
    %32 = memref.load arr : i64
    std.call_runtime @mm_incref %32
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.constant {value = 77 : i64}
    %35 = memref.load arr : i64
    func.call @IntArray.set %35, %33, %34
    %36 = arith.constant {value = 0 : i64}
    %37 = memref.load arr : i64
    %38, %39 = func.try_call @IntArray.get %37, %36
    %40 = arith.constant {value = 0 : i64}
    memref.store %40, __try_default_2
    memref.store %38, __try_result_1
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.cmpi ne %39, %41
    cf.cond_br %42 [then: otherwise_default_error_3, else: otherwise_default_continue_4]
  otherwise_default_error_3:
    %43 = memref.load __try_default_2 : i64
    memref.store %43, __try_result_1
    cf.br otherwise_default_continue_4
  otherwise_default_continue_4:
    %44 = memref.load __try_result_1 : i64
    %45 = arith.constant {value = 0 : i64}
    %46 = arith.cmpi lt %44, %45
    %47 = arith.constant {value = 4294967295 : i64}
    %48 = arith.cmpi gt %44, %47
    %49 = arith.ori1 %46, %48
    cf.cond_br %49 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    %50 = memref.lea_symdata __panic_msg_0
    %51 = std.ptr_to_i64 %50
    std.call_runtime @maxon_panic %51
  __range_ok_5:
    %52 = memref.load arr : i64
    std.call_runtime_if_nonnull @mm_decref %52
    func.return %44
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %123 = func.param ptr : StdI64
    memref.store %123, __destr_ptr
    %126 = memref.load __destr_ptr : i64
    %127 = memref.load_indirect %126+16
    %128 = arith.constant {value = 0 : i64}
    %129 = arith.cmpi ne %127, %128
    cf.cond_br %129 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %130 = memref.load __destr_ptr : i64
    %131 = memref.load_indirect %130+0
    std.call_runtime @mm_raw_free %131
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_IntArray(ptr: i64) {
  entry:
    %132 = func.param ptr : StdI64
    memref.store %132, __destr_ptr
    %133 = memref.load __destr_ptr : i64
    %134 = memref.load_indirect %133+8
    std.call_runtime_if_nonnull @mm_decref %134
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=80
    arm64.mov x0, #0
    arm64.mov x1, #1
    arm64.mov x2, #0
    arm64.mov x3, #8
    arm64.adrp_add_func x4, __destruct___ManagedMemory
    arm64.mov x5, x4
    arm64.mov x1, x5
    arm64.mov x0, #32
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-8]
    arm64.ldr x6, [x29, #-8]
    arm64.mov x7, #0
    arm64.str x7, [x6, #0]
    arm64.ldr x8, [x29, #-8]
    arm64.mov x9, #1
    arm64.str x9, [x8, #8]
    arm64.ldr x10, [x29, #-8]
    arm64.mov x11, #0
    arm64.str x11, [x10, #16]
    arm64.ldr x12, [x29, #-8]
    arm64.mov x13, #8
    arm64.str x13, [x12, #24]
    arm64.ldr x14, [x29, #-8]
    arm64.ldr x15, [x14, #24]
    arm64.adrp_add_symdata x0, __mm_panic_element_size_zero
    arm64.mov x1, x0
    arm64.mov x2, x1
    arm64.mov x1, x15
    arm64.mov x0, #0
    arm64.bl maxon_bounds_check
    arm64.mov x0, #0
    arm64.adrp_add_func x1, __destruct_IntArray
    arm64.mov x2, x1
    arm64.mov x1, x2
    arm64.mov x0, #16
    arm64.mov x2, #2
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-16]
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #0
    arm64.str x1, [x0, #0]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.str x0, [x1, #8]
    arm64.bl mm_incref
    arm64.adrp_add_rdata x0, __const_array_main_arr
    arm64.mov x1, x0
    arm64.ldr x0, [x29, #-16]
    arm64.ldr x2, [x0, #8]
    arm64.str x1, [x2, #0]
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_incref
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #0
    arm64.mov x2, #77
    arm64.bl IntArray.set
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #0
    arm64.bl IntArray.get
    arm64.mov x2, #0
    arm64.str x2, [x29, #-24]
    arm64.str x0, [x29, #-32]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_3
    arm64.b main.otherwise_default_continue_4
  otherwise_default_error_3:
    arm64.ldr x0, [x29, #-24]
    arm64.str x0, [x29, #-32]
    arm64.b main.otherwise_default_continue_4
  otherwise_default_continue_4:
    arm64.ldr x0, [x29, #-32]
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
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq main.__skip_guarded_71
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_71
    arm64.ldr x0, [x29, #-32]
    arm64.epilogue stack_size=80
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
  func @__destruct_IntArray(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #8]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_IntArray.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_IntArray.__skip_guarded_4
    arm64.b __destruct_IntArray.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
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
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

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
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var globalArr = [1, 2, 3]

function main() returns ExitCode
	globalArr.set(0, value: 42)
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
typealias IntArray = Array with Integer

var globalArr = [10, 20, 30]

function readFirst() returns Integer
	return try globalArr.get(0) otherwise 0
end 'readFirst'

function main() returns ExitCode
	globalArr.set(0, value: 99)
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
	let a = SmallInt{10}
	let b = SmallInt{3}
	return a + b
end 'main'
```
```exitcode
13
```
```RequiredMLIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    %1 = maxon.cast %0 {target = i16}
    maxon.assign %1 {var = a} {kind = i16} {decl = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    %3 = maxon.cast %2 {target = i16}
    maxon.assign %3 {var = b} {kind = i16} {decl = 1 : i1}
    %4 = maxon.binop %1, %3 {op = add}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-unsigned-add.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.addi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @mrt_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 10
    x64.mov rcx, 3
    x64.add rax, rcx
    x64.xor edx, edx
    x64.mov ebx, 4294967295
    x64.cmp rax, rbx
    x64.jg main.__range_panic_0
    x64.cmp rax, rdx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredMLIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    %1 = maxon.cast %0 {target = i16}
    maxon.assign %1 {var = a} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    %3 = maxon.cast %2 {target = i16}
    maxon.assign %3 {var = b} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.binop %1, %3 {op = add}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-unsigned-add.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.addi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @maxon_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #10
    arm64.mov x1, #3
    arm64.add x2, x0, x1
    arm64.mov x3, #0
    arm64.cmp x2, x3
    arm64.cset x4, lt
    arm64.mov x5, #4294967295
    arm64.cmp x2, x5
    arm64.cset x6, gt
    arm64.orr x7, x4, x6
    arm64.cmp x7, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x2
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: i32-unsigned-div -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
	let a = SmallInt{20}
	let b = SmallInt{3}
	return a / b
end 'main'
```
```exitcode
6
```
```RequiredMLIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 20 : i64}
    %1 = maxon.cast %0 {target = i16}
    maxon.assign %1 {var = a} {kind = i16} {decl = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    %3 = maxon.cast %2 {target = i16}
    maxon.assign %3 {var = b} {kind = i16} {decl = 1 : i1}
    %4 = maxon.binop %1, %3 {op = div}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-unsigned-div.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 20 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.divsi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @mrt_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 20
    x64.mov rcx, 3
    x64.cqo
    x64.idiv rcx
    x64.xor edx, edx
    x64.mov ebx, 4294967295
    x64.cmp rax, rbx
    x64.jg main.__range_panic_0
    x64.cmp rax, rdx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredMLIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 20 : i64}
    %1 = maxon.cast %0 {target = i16}
    maxon.assign %1 {var = a} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    %3 = maxon.cast %2 {target = i16}
    maxon.assign %3 {var = b} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.binop %1, %3 {op = div}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-unsigned-div.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 20 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.divsi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @maxon_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #20
    arm64.mov x1, #3
    arm64.sdiv x2, x0, x1
    arm64.mov x3, #0
    arm64.cmp x2, x3
    arm64.cset x4, lt
    arm64.mov x5, #4294967295
    arm64.cmp x2, x5
    arm64.cset x6, gt
    arm64.orr x7, x4, x6
    arm64.cmp x7, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x2
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: i32-signed-div -->
```maxon
typealias Temp = int(-100000 to 100000)

function main() returns ExitCode
	let a = Temp{20}
	let b = Temp{3}
	return a / b
end 'main'
```
```exitcode
6
```
```RequiredMLIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 20 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 3 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.binop %0, %1 {op = div} {optimalType = i32}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-signed-div.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 20 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.trunci %0
    %3 = arith.trunci %1
    %4 = arith.divsi %2, %3
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.extsi %4
    %7 = arith.cmpi lt %6, %5
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.extsi %4
    %10 = arith.cmpi gt %9, %8
    %11 = arith.ori1 %7, %10
    cf.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %12 = memref.lea_symdata __panic_msg_0
    %13 = std.ptr_to_i64 %12
    std.call_runtime @mrt_panic %13
  __range_ok_0:
    func.return %4
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 20
    x64.mov rcx, 3
    x64.mov edx, eax
    x64.mov ebx, ecx
    x64.mov [rbp-8], rdx
    x64.mov rax, rdx
    x64.cdq
    x64.idiv32 rbx
    x64.xor esi, esi
    x64.movsxd rdi, rax
    x64.mov r8, 4294967295
    x64.movsxd r9, rax
    x64.cmp r9, r8
    x64.jg main.__range_panic_0
    x64.cmp rdi, rsi
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredMLIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 20 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 3 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %0, %1 {op = div} {optimalType = i32}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-signed-div.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 20 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.trunci %0
    %3 = arith.trunci %1
    %4 = arith.divsi %2, %3
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.extsi %4
    %7 = arith.cmpi lt %6, %5
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.extsi %4
    %10 = arith.cmpi gt %9, %8
    %11 = arith.ori1 %7, %10
    cf.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %12 = memref.lea_symdata __panic_msg_0
    %13 = std.ptr_to_i64 %12
    std.call_runtime @maxon_panic %13
  __range_ok_0:
    func.return %4
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #20
    arm64.mov x1, #3
    arm64.mov x2, x0
    arm64.mov x3, x1
    arm64.sdiv x4, x2, x3
    arm64.mov x5, #0
    arm64.sxtw x6, x4
    arm64.cmp x6, x5
    arm64.cset x7, lt
    arm64.mov x8, #4294967295
    arm64.sxtw x9, x4
    arm64.cmp x9, x8
    arm64.cset x10, gt
    arm64.orr x11, x7, x10
    arm64.cmp x11, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x4
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: i32-unsigned-cmp -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
	let a = SmallInt{10}
	let b = SmallInt{3}
	if a > b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```
```RequiredMLIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    %1 = maxon.cast %0 {target = i16}
    maxon.assign %1 {var = a} {kind = i16} {decl = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    %3 = maxon.cast %2 {target = i16}
    maxon.assign %3 {var = b} {kind = i16} {decl = 1 : i1}
    %4 = maxon.binop %1, %3 {op = gt}
    maxon.cond_br %4 [then: check_0, else: check_0.after]
  check_0:
    %5 = maxon.literal {value = 1 : i64}
    maxon.scope_end [a, b]
    maxon.return %5
  check_0.after:
    %6 = maxon.literal {value = 0 : i64}
    maxon.scope_end [a, b]
    maxon.return %6
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.cmpi gt %0, %1
    cf.cond_br %2 [then: check_0, else: check_0.after]
  check_0:
    %3 = arith.constant {value = 1 : i64}
    func.return %3
  check_0.after:
    %4 = arith.constant {value = 0 : i64}
    func.return %4
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.mov rax, 10
    x64.mov rcx, 3
    x64.cmp rax, rcx
    x64.jle main.check_0.after
  check_0:
    x64.mov rax, 1
    x64.ret
  check_0.after:
    x64.xor eax, eax
    x64.ret
  }
}
```
```RequiredMLIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    %1 = maxon.cast %0 {target = i16}
    maxon.assign %1 {var = a} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    %3 = maxon.cast %2 {target = i16}
    maxon.assign %3 {var = b} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.binop %1, %3 {op = gt}
    maxon.cond_br %4 [then: check_0, else: check_0.after]
  check_0:
    %5 = maxon.literal {value = 1 : i64}
    maxon.scope_end [a, b]
    maxon.return %5
  check_0.after:
    %6 = maxon.literal {value = 0 : i64}
    maxon.scope_end [a, b]
    maxon.return %6
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.cmpi gt %0, %1
    cf.cond_br %2 [then: check_0, else: check_0.after]
  check_0:
    %3 = arith.constant {value = 1 : i64}
    func.return %3
  check_0.after:
    %4 = arith.constant {value = 0 : i64}
    func.return %4
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.mov x0, #10
    arm64.mov x1, #3
    arm64.cmp x0, x1
    arm64.cset x2, gt
    arm64.cmp x2, #0
    arm64.b.ne main.check_0
    arm64.b main.check_0.after
  check_0:
    arm64.mov x0, #1
    arm64.ret
  check_0.after:
    arm64.mov x0, #0
    arm64.ret
  }
}
```

<!-- test: i32-unsigned-mod -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
	let a = SmallInt{20}
	let b = SmallInt{3}
	return a mod b
end 'main'
```
```exitcode
2
```
```RequiredMLIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 20 : i64}
    %1 = maxon.cast %0 {target = i16}
    maxon.assign %1 {var = a} {kind = i16} {decl = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    %3 = maxon.cast %2 {target = i16}
    maxon.assign %3 {var = b} {kind = i16} {decl = 1 : i1}
    %4 = maxon.binop %1, %3 {op = mod}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-unsigned-mod.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 20 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.remsi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @mrt_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 20
    x64.mov rcx, 3
    x64.cqo
    x64.idiv rcx
    x64.xor eax, eax
    x64.mov ecx, 4294967295
    x64.cmp rdx, rcx
    x64.jg main.__range_panic_0
    x64.cmp rdx, rax
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredMLIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 20 : i64}
    %1 = maxon.cast %0 {target = i16}
    maxon.assign %1 {var = a} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    %3 = maxon.cast %2 {target = i16}
    maxon.assign %3 {var = b} {kind = i16} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.binop %1, %3 {op = mod}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i32-unsigned-mod.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 20 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.remsi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @maxon_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #20
    arm64.mov x1, #3
    arm64.sdiv x2, x0, x1
    arm64.msub x3, x2, x1, x0
    arm64.mov x4, #0
    arm64.cmp x3, x4
    arm64.cset x5, lt
    arm64.mov x6, #4294967295
    arm64.cmp x3, x6
    arm64.cset x7, gt
    arm64.orr x8, x5, x7
    arm64.cmp x8, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x3
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: i64-signed-no-narrowing -->
```maxon
typealias BigInt = int(-1000000000000 to 1000000000000)

function main() returns ExitCode
	let a = BigInt{20}
	let b = BigInt{3}
	return a / b
end 'main'
```
```exitcode
6
```
```RequiredMLIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 20 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 3 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.binop %0, %1 {op = div} {optimalType = i64}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i64-signed-no-narrowing.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 20 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.divsi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @mrt_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 20
    x64.mov rcx, 3
    x64.cqo
    x64.idiv rcx
    x64.xor edx, edx
    x64.mov ebx, 4294967295
    x64.cmp rax, rbx
    x64.jg main.__range_panic_0
    x64.cmp rax, rdx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredMLIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 20 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 3 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %0, %1 {op = div} {optimalType = i64}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i64-signed-no-narrowing.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 20 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.divsi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @maxon_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #20
    arm64.mov x1, #3
    arm64.sdiv x2, x0, x1
    arm64.mov x3, #0
    arm64.cmp x2, x3
    arm64.cset x4, lt
    arm64.mov x5, #4294967295
    arm64.cmp x2, x5
    arm64.cset x6, gt
    arm64.orr x7, x4, x6
    arm64.cmp x7, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x2
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: i8-range-uses-i32-arithmetic -->
```maxon
typealias Tiny = int(0 to 100)

function main() returns ExitCode
	let a = Tiny{21}
	let b = Tiny{3}
	return a / b
end 'main'
```
```exitcode
7
```
```RequiredMLIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 21 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 3 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.binop %0, %1 {op = div} {optimalType = u8}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i8-range-uses-i32-arithmetic.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 21 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.trunci %0
    %3 = arith.trunci %1
    %4 = arith.divui %2, %3
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.extui %4
    %7 = arith.cmpi lt %6, %5
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.extui %4
    %10 = arith.cmpi gt %9, %8
    %11 = arith.ori1 %7, %10
    cf.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %12 = memref.lea_symdata __panic_msg_0
    %13 = std.ptr_to_i64 %12
    std.call_runtime @mrt_panic %13
  __range_ok_0:
    func.return %4
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 21
    x64.mov rcx, 3
    x64.mov edx, eax
    x64.mov ebx, ecx
    x64.mov [rbp-8], rdx
    x64.mov rax, rdx
    x64.xor edx, edx
    x64.div32 rbx
    x64.xor esi, esi
    x64.mov edi, rax
    x64.mov r8, 4294967295
    x64.mov r9, rax
    x64.cmp r9, r8
    x64.jg main.__range_panic_0
    x64.cmp rdi, rsi
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredMLIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 21 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 3 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %0, %1 {op = div} {optimalType = u8}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at i8-range-uses-i32-arithmetic.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 21 : i64}
    %1 = arith.constant {value = 3 : i64}
    %2 = arith.trunci %0
    %3 = arith.trunci %1
    %4 = arith.divui %2, %3
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.extui %4
    %7 = arith.cmpi lt %6, %5
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.extui %4
    %10 = arith.cmpi gt %9, %8
    %11 = arith.ori1 %7, %10
    cf.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %12 = memref.lea_symdata __panic_msg_0
    %13 = std.ptr_to_i64 %12
    std.call_runtime @maxon_panic %13
  __range_ok_0:
    func.return %4
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #21
    arm64.mov x1, #3
    arm64.mov x2, x0
    arm64.mov x3, x1
    arm64.udiv x4, x2, x3
    arm64.mov x5, #0
    arm64.mov x6, x4
    arm64.cmp x6, x5
    arm64.cset x7, lt
    arm64.mov x8, #4294967295
    arm64.mov x9, x4
    arm64.cmp x9, x8
    arm64.cset x10, gt
    arm64.orr x11, x7, x10
    arm64.cmp x11, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x4
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: f32-arithmetic-uses-ss-instructions -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let a = F{10.0}
	let b = F{3.0}
	return trunc(a + b)
end 'main'
```
```exitcode
13
```
```RequiredMLIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : f64}
    maxon.assign %0 {var = a} {kind = f64} {decl = 1 : i1}
    %1 = maxon.literal {value = 3 : f64}
    maxon.assign %1 {var = b} {kind = f64} {decl = 1 : i1}
    %2 = maxon.binop %0, %1 {op = add} {kind = f64}
    %3 = maxon.trunc %2
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    %6 = maxon.literal {value = 4294967295 : i64}
    %7 = maxon.binop %3, %6 {op = gt}
    %8 = maxon.binop %5, %7 {op = or}
    maxon.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at f32-arithmetic-uses-ss-instructions.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %3
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.float_constant {value = 10 : f64}
    %1 = arith.float_constant {value = 3 : f64}
    %2 = arith.addf %0, %1
    %3 = arith.fptosi %2
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.cmpi lt %3, %4
    %6 = arith.constant {value = 4294967295 : i64}
    %7 = arith.cmpi gt %3, %6
    %8 = arith.ori1 %5, %7
    cf.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %9 = memref.lea_symdata __panic_msg_0
    %10 = std.ptr_to_i64 %9
    std.call_runtime @mrt_panic %10
  __range_ok_0:
    func.return %3
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.movsd xmm0, [rip+__float_10]
    x64.movsd xmm1, [rip+__float_3]
    x64.movsd xmm2, xmm0
    x64.addsd xmm2, xmm1
    x64.cvttsd2si rax, xmm2
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredMLIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : f64}
    maxon.assign %0 {var = a} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 3 : f64}
    maxon.assign %1 {var = b} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %0, %1 {op = add} {kind = f64}
    %3 = maxon.trunc %2
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    %6 = maxon.literal {value = 4294967295 : i64}
    %7 = maxon.binop %3, %6 {op = gt}
    %8 = maxon.binop %5, %7 {op = or}
    maxon.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at f32-arithmetic-uses-ss-instructions.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %3
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.float_constant {value = 10 : f64}
    %1 = arith.float_constant {value = 3 : f64}
    %2 = arith.addf %0, %1
    %3 = arith.fptosi %2
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.cmpi lt %3, %4
    %6 = arith.constant {value = 4294967295 : i64}
    %7 = arith.cmpi gt %3, %6
    %8 = arith.ori1 %5, %7
    cf.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %9 = memref.lea_symdata __panic_msg_0
    %10 = std.ptr_to_i64 %9
    std.call_runtime @maxon_panic %10
  __range_ok_0:
    func.return %3
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.fldr d0, [__float_10]
    arm64.fldr d1, [__float_3]
    arm64.fadd d2, d0, d1
    arm64.fcvtzs x0, d2
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: f32-comparison-uses-ucomiss -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let a = F{3.0}
	let b = F{5.0}
	if a < b 'less'
		return 1
	end 'less'
	return 0
end 'main'
```
```exitcode
1
```
```RequiredMLIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3 : f64}
    maxon.assign %0 {var = a} {kind = f64} {decl = 1 : i1}
    %1 = maxon.literal {value = 5 : f64}
    maxon.assign %1 {var = b} {kind = f64} {decl = 1 : i1}
    %2 = maxon.binop %0, %1 {op = lt} {kind = f64}
    maxon.cond_br %2 [then: less_0, else: less_0.after]
  less_0:
    %3 = maxon.literal {value = 1 : i64}
    maxon.scope_end [a, b]
    maxon.return %3
  less_0.after:
    %4 = maxon.literal {value = 0 : i64}
    maxon.scope_end [a, b]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.float_constant {value = 3 : f64}
    %1 = arith.float_constant {value = 5 : f64}
    %2 = arith.cmpf lt %0, %1
    cf.cond_br %2 [then: less_0, else: less_0.after]
  less_0:
    %3 = arith.constant {value = 1 : i64}
    func.return %3
  less_0.after:
    %4 = arith.constant {value = 0 : i64}
    func.return %4
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.movsd xmm0, [rip+__float_3]
    x64.movsd xmm1, [rip+__float_5]
    x64.ucomisd xmm0, xmm1
    x64.jp main.less_0.after
    x64.jae main.less_0.after
  less_0:
    x64.mov rax, 1
    x64.ret
  less_0.after:
    x64.xor eax, eax
    x64.ret
  }
}
```
```RequiredMLIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3 : f64}
    maxon.assign %0 {var = a} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 5 : f64}
    maxon.assign %1 {var = b} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %0, %1 {op = lt} {kind = f64}
    maxon.cond_br %2 [then: less_0, else: less_0.after]
  less_0:
    %3 = maxon.literal {value = 1 : i64}
    maxon.scope_end [a, b]
    maxon.return %3
  less_0.after:
    %4 = maxon.literal {value = 0 : i64}
    maxon.scope_end [a, b]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.float_constant {value = 3 : f64}
    %1 = arith.float_constant {value = 5 : f64}
    %2 = arith.cmpf lt %0, %1
    cf.cond_br %2 [then: less_0, else: less_0.after]
  less_0:
    %3 = arith.constant {value = 1 : i64}
    func.return %3
  less_0.after:
    %4 = arith.constant {value = 0 : i64}
    func.return %4
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.fldr d0, [__float_3]
    arm64.fldr d1, [__float_5]
    arm64.fcmp d0, d1
    arm64.cset x0, lt
    arm64.cmp x0, #0
    arm64.b.ne main.less_0
    arm64.b main.less_0.after
  less_0:
    arm64.mov x0, #1
    arm64.ret
  less_0.after:
    arm64.mov x0, #0
    arm64.ret
  }
}
```

<!-- test: f32-truncation-uses-cvttss2si -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let a = F{42.9}
	return trunc(a)
end 'main'
```
```exitcode
42
```
```RequiredMLIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 42.9 : f64}
    maxon.assign %0 {var = a} {kind = f64} {decl = 1 : i1}
    %1 = maxon.trunc %0
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.binop %1, %2 {op = lt}
    %4 = maxon.literal {value = 4294967295 : i64}
    %5 = maxon.binop %1, %4 {op = gt}
    %6 = maxon.binop %3, %5 {op = or}
    maxon.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at f32-truncation-uses-cvttss2si.test:6: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a]
    maxon.return %1
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.float_constant {value = 42.9 : f64}
    %1 = arith.fptosi %0
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.cmpi lt %1, %2
    %4 = arith.constant {value = 4294967295 : i64}
    %5 = arith.cmpi gt %1, %4
    %6 = arith.ori1 %3, %5
    cf.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %7 = memref.lea_symdata __panic_msg_0
    %8 = std.ptr_to_i64 %7
    std.call_runtime @mrt_panic %8
  __range_ok_0:
    func.return %1
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.movsd xmm0, [rip+__float_42.9]
    x64.cvttsd2si rax, xmm0
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredMLIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 42.9 : f64}
    maxon.assign %0 {var = a} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.trunc %0
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.binop %1, %2 {op = lt}
    %4 = maxon.literal {value = 4294967295 : i64}
    %5 = maxon.binop %1, %4 {op = gt}
    %6 = maxon.binop %3, %5 {op = or}
    maxon.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at f32-truncation-uses-cvttss2si.test:6: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a]
    maxon.return %1
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.float_constant {value = 42.9 : f64}
    %1 = arith.fptosi %0
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.cmpi lt %1, %2
    %4 = arith.constant {value = 4294967295 : i64}
    %5 = arith.cmpi gt %1, %4
    %6 = arith.ori1 %3, %5
    cf.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %7 = memref.lea_symdata __panic_msg_0
    %8 = std.ptr_to_i64 %7
    std.call_runtime @maxon_panic %8
  __range_ok_0:
    func.return %1
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.fldr d0, [__float_42.9]
    arm64.fcvtzs x0, d0
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```
