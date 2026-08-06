---
feature: codegen-internals
status: selfhosted
status-reason: 11 of its 32 cases fail here on whole-module RequiredIR blocks written in v1's dump format, which this runner's section comparer cannot read; the other 21 pass (measured 2026-08-06, BATCH29/A3a). shv2 runs 19 of the 32 as authored and needs ByteArray, a `.rdata` typealias surface and per-stage IR pins for the rest, so porting it is a rung of its own.
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
	try v.set(2047, value: n) otherwise panic("test invariant: set OOB")
	if n <= 0 'base'
		return 0
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

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let arr = [10 as Byte, 20 as Byte, 30 as Byte]
	let v0 = try arr.get(0) otherwise 0 as Byte
	let v1 = try arr.get(1) otherwise 0 as Byte
	let v2 = try arr.get(2) otherwise 0 as Byte
	return v0 + v1 + v2
end 'main'
```
```exitcode
60
```
```RequiredRdata
i8[] 10, 20, 30
```

<!-- test: rdata-typealias-byte-array-uses-i8 -->
```maxon

typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let arr = ByteArray from [10, 20, 30]
	let v0 = try arr.get(0) otherwise 0 as Byte
	let v1 = try arr.get(1) otherwise 0 as Byte
	let v2 = try arr.get(2) otherwise 0 as Byte
	return v0 + v1 + v2
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
function main() returns ExitCode
	var arr = [42]
	try arr.set(0, value: 77) otherwise panic("test invariant: set OOB")
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
77
```
```RequiredRdata
i64 42
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.mov eax, 1
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov ecx, 48
    x64.call stdlib.__mm_alloc_needzero
    x64.mov rbx, r8
    x64.mov rcx, rbx
    x64.call stdlib.__mm_incref
    x64.lea r8, [rip+__rdata_arr_main_0]
    x64.mov [rbx+0], r8 (8b)
    x64.mov r8d, 1
    x64.mov [rbx+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [rbx+16], r8 (8b)
    x64.mov r8d, 8
    x64.mov [rbx+24], r8 (8b)
    x64.mov r8, -2
    x64.mov [rbx+32], r8 (8b)
    x64.xor r12d, r12d
    x64.mov [rbx+40], r12 (8b)
    x64.lea rdx, [rip+__layout_Array_Int]
    x64.mov rcx, rbx
    x64.call Array.init
    x64.mov rbx, r8
    x64.mov eax, 77
  inlined_Array.set_0_0:
    x64.mov rcx, [rbx+0] (8b)
    x64.mov rdx, r12
    x64.call stdlib.__managed_mem_set
    x64.test rdx, rdx
    x64.je inlined_Array.set_3_0
  inlined_Array.set_1_0:
    x64.mov edx, 1
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_0
  inlined_Array.set_3_0:
    x64.xor edx, edx
    x64.xor r8d, r8d
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je inlined_Array.get_0_0
  try_0.otherwise:
    x64.mov rcx, rbx
    x64.call __mm_decref_maybenull_helper
    x64.lea rcx, [rip+__panic_msg_ae90e3f7d6f93ae6]
    x64.call mrt_panic
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  inlined_Array.get_0_0:
    x64.mov rcx, [rbx+0] (8b)
    x64.mov rdx, r12
    x64.call stdlib.__managed_mem_get
    x64.mov r13, r8
    x64.test rdx, rdx
    x64.je inlined_Array.get_3_0
  inlined_Array.get_1_0:
    x64.mov rcx, rbx
    x64.call __mm_decref_maybenull_helper
    x64.mov edx, 1
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_1
  inlined_Array.get_3_0:
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
    x64.lea rcx, [rip+__panic_msg_62b6e9add79cfc21]
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
    arm64.adrp+add x1, stdlib.__destruct___ManagedMemory
    arm64.mov x0, #48
    arm64.bl stdlib.__mm_alloc
    arm64.mov x19, x0
    arm64.mov x0, x19
    arm64.bl stdlib.__mm_incref
    arm64.adrp_add_rdata x0, __rdata_arr_main_0
    arm64.ldr x0, [x29, #16]
    arm64.str x0, [x19, #0] (8b)
    arm64.mov x0, #1
    arm64.str x0, [x19, #8] (8b)
    arm64.mov x0, #-2
    arm64.str x0, [x19, #16] (8b)
    arm64.mov x0, #8
    arm64.str x0, [x19, #24] (8b)
    arm64.mov x0, #-2
    arm64.str x0, [x19, #32] (8b)
    arm64.mov x20, #0
    arm64.str x20, [x19, #40] (8b)
    arm64.adrp_add_rdata x0, __layout_Array_Int
    arm64.ldr x1, [x29, #16]
    arm64.mov x0, x19
    arm64.bl Array.init
    arm64.mov x19, x0
    arm64.mov x2, #77
  inlined_Array.set_0_0:
    arm64.ldr x0, [x19, #0] (8b)
    arm64.mov x1, x20
    arm64.bl stdlib.__managed_mem_set
    arm64.mov x0, x1
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x0, ne
    arm64.b.eq inlined_Array.set_3_0
  inlined_Array.set_1_0:
    arm64.mov x1, #1
    arm64.mov x0, #0
    arm64.b inline_cont_main_0
  inlined_Array.set_3_0:
    arm64.mov x1, #0
    arm64.mov x0, #0
  inline_cont_main_0:
    arm64.cmp x1, x20
    arm64.cset x0, ne
    arm64.b.eq inlined_Array.get_0_0
  try_0.otherwise:
    arm64.mov x0, x19
    arm64.bl __mm_decref_maybenull_helper
    arm64.adrp_add_rdata x0, __panic_msg_ae90e3f7d6f93ae6
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
    arm64.mov x0, x20
    arm64.epilogue
    arm64.ret
  inlined_Array.get_0_0:
    arm64.ldr x0, [x19, #0] (8b)
    arm64.mov x1, x20
    arm64.bl stdlib.__managed_mem_get
    arm64.mov x21, x0
    arm64.mov x0, x1
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x0, ne
    arm64.b.eq inlined_Array.get_3_0
  inlined_Array.get_1_0:
    arm64.mov x0, x19
    arm64.bl __mm_decref_maybenull_helper
    arm64.mov x1, #1
    arm64.mov x0, #0
    arm64.b inline_cont_main_1
  inlined_Array.get_3_0:
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
    arm64.adrp_add_rdata x0, __panic_msg_62b6e9add79cfc21
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
    %4 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %5 = mir.mov_imm 48 : i64
    %42 = mir.mov_imm 1 : i64
    %43 = mir.call @stdlib.__mm_alloc_needzero(%5, %4, %42)
    %65 = mir.call @stdlib.__mm_incref(%43)
    %7 = mir.global_addr @__rdata_arr_main_0
    mir.store %7, %43, 0 width: qword
    %8 = mir.mov_imm 1 : i64
    mir.store %8, %43, 8 width: qword
    %9 = mir.mov_imm -2 : i64
    mir.store %9, %43, 16 width: qword
    %10 = mir.mov_imm 8 : i64
    mir.store %10, %43, 24 width: qword
    %11 = mir.mov_imm -2 : i64
    mir.store %11, %43, 32 width: qword
    mir.store %0, %43, 40 width: qword
    %16 = mir.global_addr @__layout_Array_Int
    %17 = mir.call @Array.init(%43, %16)
    %20 = mir.mov_imm 77 : i64
    mir.br inlined_Array.set_0_0()
  inlined_Array.set_0_0:
    %44 = mir.load %17, 0 width: qword
    %45, %46 = mir.try_call @stdlib.__managed_mem_set(%44, %0, %20)
    %47 = mir.mov_imm 0 : i64
    %48 = mir.cmp ne, %46, %47
    mir.cond_br %48 [then: inlined_Array.set_1_0(), else: inlined_Array.set_3_0()]
  inlined_Array.set_1_0:
    %49 = mir.mov_imm 0 : i64
    %50 = mir.mov_imm 1 : i64
    mir.br inline_cont_main_0(%49, %50)
  inlined_Array.set_3_0:
    %51 = mir.mov_imm 0 : i64
    %52 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_0(%51, %52)
  inline_cont_main_0(%53: i64, %54: i64):
    %25 = mir.cmp ne, %54, %0
    mir.cond_br %25 [then: try_0.otherwise(), else: inlined_Array.get_0_0()]
  try_0.otherwise:
    %67 = mir.call @__mm_decref_maybenull_helper(%17)
    %27 = mir.global_addr @__panic_msg_ae90e3f7d6f93ae6
    %28 = mir.call @mrt_panic(%27)
    mir.ret %0
  inlined_Array.get_0_0:
    %55 = mir.load %17, 0 width: qword
    %56, %57 = mir.try_call @stdlib.__managed_mem_get(%55, %0)
    %58 = mir.mov_imm 0 : i64
    %59 = mir.cmp ne, %57, %58
    mir.cond_br %59 [then: inlined_Array.get_1_0(), else: inlined_Array.get_3_0()]
  inlined_Array.get_1_0:
    %68 = mir.call @__mm_decref_maybenull_helper(%17)
    %60 = mir.mov_imm 0 : i64
    %61 = mir.mov_imm 1 : i64
    mir.br inline_cont_main_3(%60, %61)
  inlined_Array.get_3_0:
    %62 = mir.mov_imm 0 : i64
    %66 = mir.call @__mm_decref_maybenull_helper(%17)
    mir.br inline_cont_main_3(%56, %62)
  inline_cont_main_3(%63: i64, %64: i64):
    %37 = mir.cmp ne, %64, %0
    mir.cond_br %37 [then: try_1.merge(%0), else: try_1.ok()]
  try_1.ok:
    mir.br try_1.merge(%63)
  try_1.merge(%41: i64):
    %69 = mir.mov_imm 255 : i64
    %70 = mir.cmp ugt, %41, %69
    mir.cond_br %70 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %71 = mir.global_addr @__panic_msg_62b6e9add79cfc21
    %72 = mir.call @mrt_panic(%71)
  __range_ok_0:
    mir.ret %41
  }
}

```

<!-- test: rdata-cow-multiple-mutations -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3]
	try arr.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try arr.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try arr.set(2, value: 30) otherwise panic("test invariant: set OOB")
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
var globalArr = [1, 2, 3]

function main() returns ExitCode
	try globalArr.set(0, value: 42) otherwise panic("test invariant: set OOB")
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

var globalArr = [10, 20, 30]

function readFirst() returns Integer
	return try globalArr.get(0) otherwise 0
end 'readFirst'

function main() returns ExitCode
	try globalArr.set(0, value: 99) otherwise panic("test invariant: set OOB")
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
	let a = 10 as SmallInt
	let b = 3 as SmallInt
	return a + b
end 'main'
```
```exitcode
13
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 13
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #13
    arm64.ret
  }
}

```

```RequiredIR:wasm32-wasi
module {
  func @main() -> u8 {
  entry:
    %4 = mir.mov_imm 13 : i64
    mir.ret %4
  }
}

```

<!-- test: i32-unsigned-div -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
	let a = 20 as SmallInt
	let b = 3 as SmallInt
	return a / b
end 'main'
```
```exitcode
6
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 3
    x64.mov eax, 20
    x64.xor edx, edx
    x64.idiv r8
    x64.mov r8, rax
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea rcx, [rip+__panic_msg_e03f78fd39cbf137]
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
    arm64.prologue stack_size=16
    arm64.mov x1, #3
    arm64.mov x2, #20
    arm64.sdiv x0, x2, x1
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_e03f78fd39cbf137
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
    %0 = mir.mov_imm 20 : i64
    %1 = mir.mov_imm 3 : i64
    %4 = mir.div.i64 %0, %1
    %5 = mir.mov_imm 255 : i64
    %6 = mir.cmp ugt, %4, %5
    mir.cond_br %6 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %7 = mir.global_addr @__panic_msg_e03f78fd39cbf137
    %8 = mir.call @mrt_panic(%7)
  __range_ok_0:
    mir.ret %4
  }
}

```

<!-- test: i32-signed-div -->
```maxon
typealias Temp = int(-100000 to 100000)

function main() returns ExitCode
	let a = 20 as Temp
	let b = 3 as Temp
	return a / b
end 'main'
```
```exitcode
6
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 3
    x64.mov eax, 20
    x64.xor edx, edx
    x64.idiv r8
    x64.mov r8, rax
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea rcx, [rip+__panic_msg_dc6712b5d40a6c5e]
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
    arm64.prologue stack_size=16
    arm64.mov x1, #3
    arm64.mov x2, #20
    arm64.sdiv x0, x2, x1
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_dc6712b5d40a6c5e
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
    %0 = mir.mov_imm 20 : i64
    %1 = mir.mov_imm 3 : i64
    %4 = mir.div.i64 %0, %1
    %5 = mir.mov_imm 255 : i64
    %6 = mir.cmp ugt, %4, %5
    mir.cond_br %6 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %7 = mir.global_addr @__panic_msg_dc6712b5d40a6c5e
    %8 = mir.call @mrt_panic(%7)
  __range_ok_0:
    mir.ret %4
  }
}

```

<!-- test: i32-unsigned-cmp -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
	let a = 10 as SmallInt
	let b = 3 as SmallInt
	if a > b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 1
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #1
    arm64.ret
  }
}

```

```RequiredIR:wasm32-wasi
module {
  func @main() -> u8 {
  entry:
    %5 = mir.mov_imm 1 : i64
    mir.ret %5
  }
}

```

<!-- test: i32-unsigned-mod -->
```maxon
typealias SmallInt = int(0 to 1000)

function main() returns ExitCode
	let a = 20 as SmallInt
	let b = 3 as SmallInt
	return a mod b
end 'main'
```
```exitcode
2
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 3
    x64.mov eax, 20
    x64.xor edx, edx
    x64.idiv r8
    x64.mov r8, rdx
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea rcx, [rip+__panic_msg_c4ba6be625f9de38]
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
    arm64.prologue stack_size=16
    arm64.mov x1, #3
    arm64.mov x2, #20
    arm64.msub x0, x2, x1
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_c4ba6be625f9de38
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
    %0 = mir.mov_imm 20 : i64
    %1 = mir.mov_imm 3 : i64
    %4 = mir.rem.i64 %0, %1
    %5 = mir.mov_imm 255 : i64
    %6 = mir.cmp ugt, %4, %5
    mir.cond_br %6 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %7 = mir.global_addr @__panic_msg_c4ba6be625f9de38
    %8 = mir.call @mrt_panic(%7)
  __range_ok_0:
    mir.ret %4
  }
}

```

<!-- test: i64-signed-no-narrowing -->
```maxon
typealias BigInt = int(-1000000000000 to 1000000000000)

function main() returns ExitCode
	let a = 20 as BigInt
	let b = 3 as BigInt
	return a / b
end 'main'
```
```exitcode
6
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 3
    x64.mov eax, 20
    x64.xor edx, edx
    x64.idiv r8
    x64.mov r8, rax
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea rcx, [rip+__panic_msg_e3b71750342575b4]
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
    arm64.prologue stack_size=16
    arm64.mov x1, #3
    arm64.mov x2, #20
    arm64.sdiv x0, x2, x1
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_e3b71750342575b4
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
    %0 = mir.mov_imm 20 : i64
    %1 = mir.mov_imm 3 : i64
    %4 = mir.div.i64 %0, %1
    %5 = mir.mov_imm 255 : i64
    %6 = mir.cmp ugt, %4, %5
    mir.cond_br %6 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %7 = mir.global_addr @__panic_msg_e3b71750342575b4
    %8 = mir.call @mrt_panic(%7)
  __range_ok_0:
    mir.ret %4
  }
}

```

<!-- test: i8-range-uses-i32-arithmetic -->
```maxon
typealias Tiny = int(0 to 100)

function main() returns ExitCode
	let a = 21 as Tiny
	let b = 3 as Tiny
	return a / b
end 'main'
```
```exitcode
7
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 3
    x64.mov eax, 21
    x64.xor edx, edx
    x64.idiv r8
    x64.mov r8, rax
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea rcx, [rip+__panic_msg_771a87d3fb99fca9]
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
    arm64.prologue stack_size=16
    arm64.mov x1, #3
    arm64.mov x2, #21
    arm64.sdiv x0, x2, x1
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_771a87d3fb99fca9
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
    %0 = mir.mov_imm 21 : i64
    %1 = mir.mov_imm 3 : i64
    %4 = mir.div.i64 %0, %1
    %5 = mir.mov_imm 255 : i64
    %6 = mir.cmp ugt, %4, %5
    mir.cond_br %6 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %7 = mir.global_addr @__panic_msg_771a87d3fb99fca9
    %8 = mir.call @mrt_panic(%7)
  __range_ok_0:
    mir.ret %4
  }
}

```

<!-- test: f32-arithmetic-uses-ss-instructions -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let a = 10.0 as F
	let b = 3.0 as F
	return trunc(a + b)
end 'main'
```
```exitcode
13
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.movsd xmm0, [rip+__float_4613937818241073152]
    x64.movsd xmm1, [rip+__float_4621819117588971520]
    x64.addsd xmm1, xmm0
    x64.mov r9d, 4294967295
    x64.cvttsd2si r8, xmm1
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea rcx, [rip+__panic_msg_91aa5b022612377c]
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
    arm64.prologue stack_size=16
    arm64.ldr d0, [rdata+__float_4613937818241073152]
    arm64.ldr d1, [rdata+__float_4621819117588971520]
    arm64.fadd d2, d1, d0
    arm64.mov x1, #255
    arm64.fcvtzs x0, d2
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_91aa5b022612377c
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
    %0 = mir.mov_imm 4621819117588971520 : f64
    %1 = mir.mov_imm 4613937818241073152 : f64
    %4 = mir.add.f64 %0, %1
    %5 = mir.fptosi.i64 %4
    %6 = mir.mov_imm 255 : i64
    %7 = mir.cmp ugt, %5, %6
    mir.cond_br %7 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %8 = mir.global_addr @__panic_msg_91aa5b022612377c
    %9 = mir.call @mrt_panic(%8)
  __range_ok_0:
    mir.ret %5
  }
}

```

<!-- test: f32-comparison-uses-ucomiss -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let a = 3.0 as F
	let b = 5.0 as F
	if a < b 'less'
		return 1
	end 'less'
	return 0
end 'main'
```
```exitcode
1
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.movsd xmm0, [rip+__float_4617315517961601024]
    x64.movsd xmm1, [rip+__float_4613937818241073152]
    x64.ucomisd xmm1, xmm0
    x64.jp less_0.after
    x64.jae less_0.after
  less_0:
    x64.mov r8d, 1
    x64.ret
  less_0.after:
    x64.xor r8d, r8d
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.ldr d0, [rdata+__float_4617315517961601024]
    arm64.ldr d1, [rdata+__float_4613937818241073152]
    arm64.fcmp d1, d0
    arm64.cset x0, mi
    arm64.b.pl less_0.after
  less_0:
    arm64.mov x0, #1
    arm64.ret
  less_0.after:
    arm64.mov x0, #0
    arm64.ret
  }
}

```

```RequiredIR:wasm32-wasi
module {
  func @main() -> u8 {
  entry:
    %0 = mir.mov_imm 4613937818241073152 : f64
    %1 = mir.mov_imm 4617315517961601024 : f64
    %4 = mir.cmp flt, %0, %1
    mir.cond_br %4 [then: less_0(), else: less_0.after()]
  less_0:
    %5 = mir.mov_imm 1 : i64
    mir.ret %5
  less_0.after:
    %6 = mir.mov_imm 0 : i64
    mir.ret %6
  }
}

```

<!-- test: f32-truncation-uses-cvttss2si -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let a = 42.9 as F
	return trunc(a)
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.movsd xmm0, [rip+__float_4631234455559942963]
    x64.mov r9d, 4294967295
    x64.cvttsd2si r8, xmm0
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea rcx, [rip+__panic_msg_74470c2bcd4c1409]
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
    arm64.prologue stack_size=16
    arm64.ldr d0, [rdata+__float_4631234455559942963]
    arm64.mov x1, #255
    arm64.fcvtzs x0, d0
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_74470c2bcd4c1409
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
    %0 = mir.mov_imm 4631234455559942963 : f64
    %2 = mir.fptosi.i64 %0
    %3 = mir.mov_imm 255 : i64
    %4 = mir.cmp ugt, %2, %3
    mir.cond_br %4 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %5 = mir.global_addr @__panic_msg_74470c2bcd4c1409
    %6 = mir.call @mrt_panic(%5)
  __range_ok_0:
    mir.ret %2
  }
}

```
