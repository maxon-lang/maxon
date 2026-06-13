---
feature: advent of compiler optimization
status: selfhosted
keywords: abs, absolute value, math
category: math-intrinsic
---
# advent of compiler optimization

## Documentation

Matt Godbolt's Advent of Compiler Optimizations 2025
https://www.youtube.com/playlist?list=PL2HVqYf7If8cY4wLk7JUQ2f0JXY_xMQm2

## Tests

<!-- test: day1 -->
```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.xor r8d, r8d
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
    maxon.scope_end []
    maxon.return %0
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    func.return %0
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.mov x0, #0
    arm64.ret
  }
}
```
```RequiredIR:x64-linux
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.scope_end []
    maxon.return %0
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    func.return %0
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.xor eax, eax
    x64.ret
  }
}
```

<!-- test: day2 -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(x Integer, y Integer) returns Integer
		return x + y
end 'add'

function main() returns ExitCode
	return add(3, y: 4)
end 'main'
```
```exitcode
7
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 7
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
=== maxon
module {
  func @advent.add(x: i64, y: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = y} {type = i64}
    %2 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    maxon.scope_end [x, y]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.literal {value = 4 : i64}
    %5 = maxon.call @advent.add %3, %4
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at day2.test:10: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %5
  }
}
=== standard
module {
  func @advent.add(x: i64, y: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    %1 = func.param y : StdI64
    %2 = arith.addi %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 3 : i64}
    %4 = arith.constant {value = 4 : i64}
    %5 = func.call @advent.add %3, %4
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi lt %5, %6
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.cmpi gt %5, %8
    %10 = arith.ori1 %7, %9
    cf.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %11 = memref.lea_symdata __panic_msg_0
    %12 = std.ptr_to_i64 %11
    std.call_runtime @mrt_panic %12
  __range_ok_0:
    func.return %5
  }
}
=== arm64
module {
  func @advent.add(x: i64, y: i64) -> i64 {
  entry:
    arm64.add x2, x0, x1
    arm64.mov x0, x2
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #3
    arm64.mov x1, #4
    arm64.bl advent.add
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov w1, #4294967295
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```
```RequiredIR:x64-linux
=== maxon
module {
  func @advent.add(x: i64, y: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = y} {type = i64}
    %2 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    maxon.scope_end [x, y]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.literal {value = 4 : i64}
    %5 = maxon.call @advent.add %3, %4
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at day2.test:10: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %5
  }
}
=== standard
module {
  func @advent.add(x: i64, y: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    %1 = func.param y : StdI64
    %2 = arith.addi %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 3 : i64}
    %4 = arith.constant {value = 4 : i64}
    %5 = func.call @advent.add %3, %4
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi lt %5, %6
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.cmpi gt %5, %8
    %10 = arith.ori1 %7, %9
    cf.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %11 = memref.lea_symdata __panic_msg_0
    %12 = std.ptr_to_i64 %11
    std.call_runtime @mrt_panic %12
  __range_ok_0:
    func.return %5
  }
}
=== x86
module {
  func @advent.add(x: i64, y: i64) -> i64 {
  entry:
    x64.lea rax, [rcx + rdx]
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 3
    x64.mov rdx, 4
    x64.call advent.add
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

<!-- test: day4a -->
<!-- Args: 1 -->
```maxon

typealias Integer = int(i64.min to i64.max)

function multiply(x Integer) returns Integer
		return x * 1
end 'multiply'

function main() returns ExitCode
	let args = CommandLine.args()
	let parsed = try int.fromString(try args.get(1) otherwise "") otherwise 0
	if parsed > 1000 'guard'
		return 99
	end 'guard'
	return multiply(3)
end 'main'
```
```exitcode
3
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=64
    x64.call CommandLine.args
    x64.mov r12, r8
    x64.mov edx, 1
    x64.xor r13d, r13d
  inlined_Array.get_0_0:
    x64.mov rcx, [r12+0] (8b)
    x64.call stdlib.__managed_mem_get
    x64.mov r14, r8
    x64.test rdx, rdx
    x64.je inlined_Array.get_3_0
  inlined_Array.get_1_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov edx, 1
    x64.xor ecx, ecx
    x64.jmp inline_cont_main_0
  inlined_Array.get_3_0:
    x64.xor r15d, r15d
    x64.jmp __rc_edge_11_0
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.call __mm_decref_maybenull_helper
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov ecx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov [rbp+-8], r8
    x64.mov r8, [rbp+-8]
    x64.mov [r8+40], r13 (8b)
    x64.lea r9, [rip+__istr_0]
    x64.mov [r8+0], r9 (8b)
    x64.mov [r8+8], r13 (8b)
    x64.mov r9, -2
    x64.mov [r8+16], r9 (8b)
    x64.mov r9d, 1
    x64.mov [r8+24], r9 (8b)
    x64.mov [r8+32], r13 (8b)
    x64.lea rdx, [rip+__destruct_String]
    x64.mov ecx, 16
    x64.mov rax, r13
    x64.call stdlib.__mm_alloc
    x64.mov [rbp+-16], r8
    x64.mov rcx, [rbp+-16]
    x64.call stdlib.__mm_incref
    x64.mov r8, [rbp+-16]
    x64.mov r8, [rbp+-8]
    x64.mov r9, [rbp+-16]
    x64.mov [r9+0], r8 (8b)
    x64.mov r8d, 1
    x64.mov [r9+8], r8 (8b)
    x64.mov r8, [rbp+-16]
    x64.mov rcx, r8
  try_0.merge:
    x64.mov [rbp+-16], rcx
    x64.mov rcx, [rbp+-16]
    x64.call stdlib.__int_fromString
    x64.mov [rbp+-8], r8
    x64.mov [rbp+-24], rdx
    x64.mov rcx, [rbp+-16]
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, [rbp+-24]
    x64.test r8, r8
    x64.je try_1.ok
    x64.mov r8, r13
    x64.jmp try_1.merge
  try_1.ok:
    x64.mov r8, [rbp+-8]
  try_1.merge:
    x64.cmp r8, 1000
    x64.jle guard_0.after
  guard_0:
    x64.mov r8d, 99
    x64.epilogue
    x64.ret
  guard_0.after:
    x64.mov r8d, 3
    x64.epilogue
    x64.ret
  __rc_edge_11_0:
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r14
    x64.mov rdx, r15
    x64.jmp inline_cont_main_0
  }
}

```
```RequiredIR:arm64-macos
=== maxon
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.literal {value = 1 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {optimalType = i64}
    maxon.scope_end [x]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.call @stdlib.CommandLine.args
    maxon.assign %3 {var = __call_tmp_3} {decl = 1 : i1}
    maxon.assign %3 {var = args} {decl = 1 : i1}
    %4 = maxon.struct_var_ref args
    %5 = maxon.literal {value = 1 : i64}
    %8, %7 = maxon.try_call @StringArray.get %4, %5
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %7, %10 {op = ne}
    maxon.cond_br %11 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %9 = maxon.string_literal ""
    maxon.assign %9 {var = __lit_tmp_9} {decl = 1 : i1}
    maxon.assign %9 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_success_2:
    maxon.assign %8 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %12 = maxon.struct_var_ref __try_result_0
    %15, %14 = maxon.try_call @stdlib.Builtins.__int_fromString %12
    %16 = maxon.literal {value = 0 : i64}
    maxon.assign %16 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %15 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %14, %17 {op = ne}
    maxon.cond_br %18 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %19 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %19 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %20 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %20 {var = parsed} {kind = i64} {decl = 1 : i1}
    %21 = maxon.literal {value = 1000 : i64}
    %22 = maxon.binop %20, %21 {op = gt}
    maxon.cond_br %22 [then: guard_8, else: guard_8.after]
  guard_8:
    %23 = maxon.literal {value = 99 : i64}
    maxon.scope_end [args, __try_default_5, parsed, __lit_tmp_9, __try_result_0, __try_result_4]
    maxon.return %23
  guard_8.after:
    %24 = maxon.literal {value = 3 : i64}
    %25 = maxon.call @advent.multiply %24
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %25, %26 {op = lt}
    %28 = maxon.literal {value = 4294967295 : i64}
    %29 = maxon.binop %25, %28 {op = gt}
    %30 = maxon.binop %27, %29 {op = or}
    maxon.cond_br %30 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    maxon.panic "panic at day4a.test:15: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_9:
    maxon.scope_end [args, __try_default_5, parsed, __lit_tmp_9, __try_result_0, __try_result_4]
    maxon.return %25
  }
}
=== standard
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    func.return %0
  }
  func @main() -> u32 {
  entry:
    %75 = arith.constant {value = 0 : i64}
    memref.store %75, __lit_tmp_9
    %76 = arith.constant {value = 0 : i64}
    memref.store %76, __try_result_0
    %2 = func.call @stdlib.CommandLine.args
    memref.store %2, args
    %5 = arith.constant {value = 1 : i64}
    %6 = memref.load args : i64
    %7, %8 = func.try_call @StringArray.get %6, %5
    memref.store %7, __callret_8
    %9 = arith.constant {value = 0 : i64}
    %10 = arith.cmpi ne %8, %9
    cf.cond_br %10 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %11 = memref.lea_rdata __str_0
    %12 = std.ptr_to_i64 %11
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.constant {value = 16 : i64}
    %15 = func.ref @__destruct_String
    %16 = std.ptr_to_i64 %15
    %17 = arith.constant {value = 4 : i64}
    %18 = std.call_runtime @mm_alloc %14, %16, %17
    memref.store %18, __lit_tmp_9
    %19 = arith.constant {value = 40 : i64}
    %20 = func.ref @__destruct___ManagedMemory
    %21 = std.ptr_to_i64 %20
    %22 = arith.constant {value = 3 : i64}
    %23 = std.call_runtime @mm_alloc %19, %21, %22
    memref.store %23, __strtmp_managed_9
    %24 = arith.constant {value = -2 : i64}
    %25 = arith.constant {value = 1 : i64}
    %26 = arith.constant {value = 0 : i64}
    %27 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %12, %27+0
    %28 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %13, %28+8
    %29 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %24, %29+16
    %30 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %25, %30+24
    %31 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %26, %31+32
    %32 = memref.load __strtmp_managed_9 : i64
    %33 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %32, %33+0
    %34 = memref.load __strtmp_managed_9 : i64
    std.call_runtime @mm_incref %34
    %35 = arith.constant {value = 1 : i64}
    %36 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %35, %36+8
    %37 = memref.load __lit_tmp_9 : i64
    std.call_runtime @mm_incref %37
    memref.store %18, __try_result_0
    %39 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %39
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %40 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %40
    %41 = memref.load __callret_8 : i64
    memref.store %41, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %42 = memref.load __try_result_0 : i64
    %43, %44 = func.try_call @stdlib.Builtins.__int_fromString %42
    %45 = arith.constant {value = 0 : i64}
    memref.store %45, __try_default_5
    memref.store %43, __try_result_4
    %46 = arith.constant {value = 0 : i64}
    %47 = arith.cmpi ne %44, %46
    cf.cond_br %47 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %48 = memref.load __try_default_5 : i64
    memref.store %48, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %49 = memref.load __try_result_4 : i64
    %50 = arith.constant {value = 1000 : i64}
    %51 = arith.cmpi gt %49, %50
    cf.cond_br %51 [then: guard_8, else: guard_8.after]
  guard_8:
    %52 = arith.constant {value = 99 : i64}
    %53 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %53
    %55 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %55
    %57 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %57
    func.return %52
  guard_8.after:
    %59 = arith.constant {value = 3 : i64}
    %60 = func.call @advent.multiply %59
    %61 = arith.constant {value = 0 : i64}
    %62 = arith.cmpi lt %60, %61
    %63 = arith.constant {value = 4294967295 : i64}
    %64 = arith.cmpi gt %60, %63
    %65 = arith.ori1 %62, %64
    cf.cond_br %65 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    %66 = memref.lea_symdata __panic_msg_0
    %67 = std.ptr_to_i64 %66
    std.call_runtime @mrt_panic %67
  __range_ok_9:
    %68 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %68
    %70 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %70
    %72 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %72
    func.return %60
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    %77 = func.param ptr : StdI64
    memref.store %77, __destr_ptr
    %78 = memref.load __destr_ptr : i64
    %79 = memref.load_indirect %78+0
    std.call_runtime_if_nonnull @mm_decref %79
    cf.br done
  done:
    func.return
  }
  func @__destruct_CodepointIterator(ptr: i64) {
  entry:
    %80 = func.param ptr : StdI64
    memref.store %80, __destr_ptr
    %81 = memref.load __destr_ptr : i64
    %82 = memref.load_indirect %81+0
    std.call_runtime_if_nonnull @mm_decref %82
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %83 = func.param ptr : StdI64
    memref.store %83, __destr_ptr
    %86 = memref.load __destr_ptr : i64
    %87 = memref.load_indirect %86+16
    %88 = arith.constant {value = -1 : i64}
    %89 = arith.cmpi eq %87, %88
    cf.cond_br %89 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %90 = memref.load __destr_ptr : i64
    %91 = memref.load_indirect %90+32
    std.call_runtime_if_nonnull @mm_decref %91
    cf.br skip_buf_0
  check_owned_0:
    %92 = memref.load __destr_ptr : i64
    %93 = memref.load_indirect %92+16
    %94 = arith.constant {value = -2 : i64}
    %95 = arith.cmpi ne %93, %94
    cf.cond_br %95 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %96 = memref.load __destr_ptr : i64
    %97 = memref.load_indirect %96+0
    std.call_runtime @mm_raw_free %97
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_String(ptr: i64) {
  entry:
    %98 = func.param ptr : StdI64
    memref.store %98, __destr_ptr
    %99 = memref.load __destr_ptr : i64
    %100 = memref.load_indirect %99+0
    std.call_runtime_if_nonnull @mm_decref %100
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    %101 = func.param ptr : StdI64
    memref.store %101, __destr_ptr
    %104 = memref.load __destr_ptr : i64
    %105 = memref.load_indirect %104+16
    %106 = arith.constant {value = -1 : i64}
    %107 = arith.cmpi eq %105, %106
    cf.cond_br %107 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %108 = memref.load __destr_ptr : i64
    %109 = memref.load_indirect %108+32
    std.call_runtime_if_nonnull @mm_decref %109
    cf.br skip_buf_0
  check_owned_0:
    %110 = memref.load __destr_ptr : i64
    %111 = memref.load_indirect %110+16
    %112 = arith.constant {value = -2 : i64}
    %113 = arith.cmpi ne %111, %112
    cf.cond_br %113 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %114 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %114
    %115 = memref.load __destr_ptr : i64
    %116 = memref.load_indirect %115+0
    std.call_runtime @mm_raw_free %116
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    %117 = func.param ptr : StdI64
    memref.store %117, __destr_ptr
    %118 = memref.load __destr_ptr : i64
    %119 = memref.load_indirect %118+0
    std.call_runtime_if_nonnull @mm_decref %119
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=144
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-16]
    arm64.bl stdlib.CommandLine.args
    arm64.str x0, [x29, #-24]
    arm64.ldr x2, [x29, #-24]
    arm64.ldr x0, [x29, #-24]
    arm64.mov x1, #1
    arm64.bl StringArray.get
    arm64.str x0, [x29, #-32]
    arm64.mov x3, #0
    arm64.cmp x1, x3
    arm64.cset x4, ne
    arm64.cmp x4, #0
    arm64.b.ne main.otherwise_default_error_1
    arm64.b main.otherwise_default_success_2
  otherwise_default_error_1:
    arm64.adrp_add_rdata x0, __str_0
    arm64.mov x1, x0
    arm64.mov x2, #0
    arm64.adrp_add_func x3, __destruct_String
    arm64.mov x4, x3
    arm64.str x1, [x29, #-64]
    arm64.mov x1, x4
    arm64.mov x0, #16
    arm64.mov x2, #4
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-8]
    arm64.adrp_add_func x5, __destruct___ManagedMemory
    arm64.mov x6, x5
    arm64.mov x1, x6
    arm64.mov x0, #40
    arm64.mov x2, #3
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-40]
    arm64.mov x7, #-2
    arm64.mov x8, #1
    arm64.mov x9, #0
    arm64.ldr x10, [x29, #-40]
    arm64.ldr x11, [x29, #-64]
    arm64.str x11, [x10, #0]
    arm64.ldr x12, [x29, #-40]
    arm64.mov x13, #0
    arm64.str x13, [x12, #8]
    arm64.ldr x14, [x29, #-40]
    arm64.str x7, [x14, #16]
    arm64.ldr x15, [x29, #-40]
    arm64.str x8, [x15, #24]
    arm64.ldr x0, [x29, #-40]
    arm64.str x9, [x0, #32]
    arm64.ldr x0, [x29, #-40]
    arm64.ldr x1, [x29, #-8]
    arm64.str x0, [x1, #0]
    arm64.ldr x0, [x29, #-40]
    arm64.bl mm_incref
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-8]
    arm64.str x0, [x1, #8]
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_incref
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_incref
    arm64.b main.otherwise_default_continue_3
  otherwise_default_success_2:
    arm64.ldr x0, [x29, #-16]
    arm64.cmp x0, #0
    arm64.b.eq main.__skip_guarded_56
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_56
    arm64.ldr x1, [x29, #-32]
    arm64.str x1, [x29, #-16]
    arm64.b main.otherwise_default_continue_3
  otherwise_default_continue_3:
    arm64.ldr x0, [x29, #-16]
    arm64.bl stdlib.Builtins.__int_fromString
    arm64.mov x2, #0
    arm64.str x2, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_6
    arm64.b main.otherwise_default_continue_7
  otherwise_default_error_6:
    arm64.ldr x0, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.b main.otherwise_default_continue_7
  otherwise_default_continue_7:
    arm64.ldr x0, [x29, #-56]
    arm64.mov x1, #1000
    arm64.cmp x0, x1
    arm64.cset x2, gt
    arm64.cmp x2, #0
    arm64.b.ne main.guard_8
    arm64.b main.guard_8.after
  guard_8:
    arm64.ldr x0, [x29, #-16]
    arm64.cmp x0, #0
    arm64.b.eq main.__skip_guarded_77
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_77
    arm64.ldr x1, [x29, #-8]
    arm64.cmp x1, #0
    arm64.b.eq main.__skip_guarded_79
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_79
    arm64.ldr x2, [x29, #-24]
    arm64.cmp x2, #0
    arm64.b.eq main.__skip_guarded_81
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_81
    arm64.mov x0, #99
    arm64.epilogue stack_size=144
    arm64.ret
  guard_8.after:
    arm64.mov x0, #3
    arm64.bl advent.multiply
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov w1, #4294967295
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_9
    arm64.b main.__range_ok_9
  __range_panic_9:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_9:
    arm64.ldr x1, [x29, #-16]
    arm64.str x0, [x29, #-64]
    arm64.cmp x1, #0
    arm64.b.eq main.__skip_guarded_95
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_95
    arm64.ldr x0, [x29, #-8]
    arm64.cmp x0, #0
    arm64.b.eq main.__skip_guarded_97
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_97
    arm64.ldr x1, [x29, #-24]
    arm64.cmp x1, #0
    arm64.b.eq main.__skip_guarded_99
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_99
    arm64.ldr x0, [x29, #-64]
    arm64.epilogue stack_size=144
    arm64.ret
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_CodepointView.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_CodepointView.__skip_guarded_4
    arm64.b __destruct_CodepointView.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct_CodepointIterator(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_CodepointIterator.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_CodepointIterator.__skip_guarded_4
    arm64.b __destruct_CodepointIterator.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #-1
    arm64.cmp x1, x2
    arm64.cset x3, eq
    arm64.cmp x3, #0
    arm64.b.ne __destruct___ManagedMemory.slice_cleanup_0
    arm64.b __destruct___ManagedMemory.check_owned_0
  slice_cleanup_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #32]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct___ManagedMemory.__skip_guarded_9
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct___ManagedMemory.__skip_guarded_9
    arm64.b __destruct___ManagedMemory.skip_buf_0
  check_owned_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #-2
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
  func @__destruct_String(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_String.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_String.__skip_guarded_4
    arm64.b __destruct_String.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #-1
    arm64.cmp x1, x2
    arm64.cset x3, eq
    arm64.cmp x3, #0
    arm64.b.ne __destruct___ManagedMemory_String.slice_cleanup_0
    arm64.b __destruct___ManagedMemory_String.check_owned_0
  slice_cleanup_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #32]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct___ManagedMemory_String.__skip_guarded_9
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct___ManagedMemory_String.__skip_guarded_9
    arm64.b __destruct___ManagedMemory_String.skip_buf_0
  check_owned_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #-2
    arm64.cmp x1, x2
    arm64.cset x3, ne
    arm64.cmp x3, #0
    arm64.b.ne __destruct___ManagedMemory_String.free_buf_0
    arm64.b __destruct___ManagedMemory_String.skip_buf_0
  free_buf_0:
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_decref_managed_elements
    arm64.ldr x1, [x29, #-8]
    arm64.ldr x2, [x1, #0]
    arm64.mov x0, x2
    arm64.bl mm_raw_free
    arm64.b __destruct___ManagedMemory_String.skip_buf_0
  skip_buf_0:
    arm64.b __destruct___ManagedMemory_String.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_StringArray.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_StringArray.__skip_guarded_4
    arm64.b __destruct_StringArray.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```
```RequiredIR:x64-linux
=== maxon
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.literal {value = 1 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {optimalType = i64}
    maxon.scope_end [x]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.call @stdlib.CommandLine.args
    maxon.assign %3 {var = __call_tmp_3} {decl = 1 : i1}
    maxon.assign %3 {var = args} {decl = 1 : i1}
    %4 = maxon.struct_var_ref args
    %5 = maxon.literal {value = 1 : i64}
    %8, %7 = maxon.try_call @StringArray.get %4, %5
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %7, %10 {op = ne}
    maxon.cond_br %11 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %9 = maxon.string_literal ""
    maxon.assign %9 {var = __lit_tmp_9} {decl = 1 : i1}
    maxon.assign %9 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_success_2:
    maxon.assign %8 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %12 = maxon.struct_var_ref __try_result_0
    %15, %14 = maxon.try_call @stdlib.Builtins.__int_fromString %12
    %16 = maxon.literal {value = 0 : i64}
    maxon.assign %16 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %15 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %14, %17 {op = ne}
    maxon.cond_br %18 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %19 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %19 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %20 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %20 {var = parsed} {kind = i64} {decl = 1 : i1}
    %21 = maxon.literal {value = 1000 : i64}
    %22 = maxon.binop %20, %21 {op = gt}
    maxon.cond_br %22 [then: guard_8, else: guard_8.after]
  guard_8:
    %23 = maxon.literal {value = 99 : i64}
    maxon.scope_end [args, __try_default_5, parsed, __lit_tmp_9, __try_result_0, __try_result_4]
    maxon.return %23
  guard_8.after:
    %24 = maxon.literal {value = 3 : i64}
    %25 = maxon.call @advent.multiply %24
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %25, %26 {op = lt}
    %28 = maxon.literal {value = 4294967295 : i64}
    %29 = maxon.binop %25, %28 {op = gt}
    %30 = maxon.binop %27, %29 {op = or}
    maxon.cond_br %30 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    maxon.panic "panic at day4a.test:15: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_9:
    maxon.scope_end [args, __try_default_5, parsed, __lit_tmp_9, __try_result_0, __try_result_4]
    maxon.return %25
  }
}
=== standard
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    func.return %0
  }
  func @main() -> u32 {
  entry:
    %75 = arith.constant {value = 0 : i64}
    memref.store %75, __lit_tmp_9
    %76 = arith.constant {value = 0 : i64}
    memref.store %76, __try_result_0
    %2 = func.call @stdlib.CommandLine.args
    memref.store %2, args
    %5 = arith.constant {value = 1 : i64}
    %6 = memref.load args : i64
    %7, %8 = func.try_call @StringArray.get %6, %5
    memref.store %7, __callret_8
    %9 = arith.constant {value = 0 : i64}
    %10 = arith.cmpi ne %8, %9
    cf.cond_br %10 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %11 = memref.lea_rdata __str_0
    %12 = std.ptr_to_i64 %11
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.constant {value = 16 : i64}
    %15 = func.ref @__destruct_String
    %16 = std.ptr_to_i64 %15
    %17 = arith.constant {value = 4 : i64}
    %18 = std.call_runtime @mm_alloc %14, %16, %17
    memref.store %18, __lit_tmp_9
    %19 = arith.constant {value = 40 : i64}
    %20 = func.ref @__destruct___ManagedMemory
    %21 = std.ptr_to_i64 %20
    %22 = arith.constant {value = 3 : i64}
    %23 = std.call_runtime @mm_alloc %19, %21, %22
    memref.store %23, __strtmp_managed_9
    %24 = arith.constant {value = -2 : i64}
    %25 = arith.constant {value = 1 : i64}
    %26 = arith.constant {value = 0 : i64}
    %27 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %12, %27+0
    %28 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %13, %28+8
    %29 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %24, %29+16
    %30 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %25, %30+24
    %31 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %26, %31+32
    %32 = memref.load __strtmp_managed_9 : i64
    %33 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %32, %33+0
    %34 = memref.load __strtmp_managed_9 : i64
    std.call_runtime @mm_incref %34
    %35 = arith.constant {value = 1 : i64}
    %36 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %35, %36+8
    %37 = memref.load __lit_tmp_9 : i64
    std.call_runtime @mm_incref %37
    memref.store %18, __try_result_0
    %39 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %39
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %40 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %40
    %41 = memref.load __callret_8 : i64
    memref.store %41, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %42 = memref.load __try_result_0 : i64
    %43, %44 = func.try_call @stdlib.Builtins.__int_fromString %42
    %45 = arith.constant {value = 0 : i64}
    memref.store %45, __try_default_5
    memref.store %43, __try_result_4
    %46 = arith.constant {value = 0 : i64}
    %47 = arith.cmpi ne %44, %46
    cf.cond_br %47 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %48 = memref.load __try_default_5 : i64
    memref.store %48, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %49 = memref.load __try_result_4 : i64
    %50 = arith.constant {value = 1000 : i64}
    %51 = arith.cmpi gt %49, %50
    cf.cond_br %51 [then: guard_8, else: guard_8.after]
  guard_8:
    %52 = arith.constant {value = 99 : i64}
    %53 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %53
    %55 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %55
    %57 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %57
    func.return %52
  guard_8.after:
    %59 = arith.constant {value = 3 : i64}
    %60 = func.call @advent.multiply %59
    %61 = arith.constant {value = 0 : i64}
    %62 = arith.cmpi lt %60, %61
    %63 = arith.constant {value = 4294967295 : i64}
    %64 = arith.cmpi gt %60, %63
    %65 = arith.ori1 %62, %64
    cf.cond_br %65 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    %66 = memref.lea_symdata __panic_msg_0
    %67 = std.ptr_to_i64 %66
    std.call_runtime @mrt_panic %67
  __range_ok_9:
    %68 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %68
    %70 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %70
    %72 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %72
    func.return %60
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    %77 = func.param ptr : StdI64
    memref.store %77, __destr_ptr
    %78 = memref.load __destr_ptr : i64
    %79 = memref.load_indirect %78+0
    std.call_runtime_if_nonnull @mm_decref %79
    cf.br done
  done:
    func.return
  }
  func @__destruct_CodepointIterator(ptr: i64) {
  entry:
    %80 = func.param ptr : StdI64
    memref.store %80, __destr_ptr
    %81 = memref.load __destr_ptr : i64
    %82 = memref.load_indirect %81+0
    std.call_runtime_if_nonnull @mm_decref %82
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %83 = func.param ptr : StdI64
    memref.store %83, __destr_ptr
    %86 = memref.load __destr_ptr : i64
    %87 = memref.load_indirect %86+16
    %88 = arith.constant {value = -1 : i64}
    %89 = arith.cmpi eq %87, %88
    cf.cond_br %89 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %90 = memref.load __destr_ptr : i64
    %91 = memref.load_indirect %90+32
    std.call_runtime_if_nonnull @mm_decref %91
    cf.br skip_buf_0
  check_owned_0:
    %92 = memref.load __destr_ptr : i64
    %93 = memref.load_indirect %92+16
    %94 = arith.constant {value = -2 : i64}
    %95 = arith.cmpi ne %93, %94
    cf.cond_br %95 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %96 = memref.load __destr_ptr : i64
    %97 = memref.load_indirect %96+0
    std.call_runtime @mm_raw_free %97
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_String(ptr: i64) {
  entry:
    %98 = func.param ptr : StdI64
    memref.store %98, __destr_ptr
    %99 = memref.load __destr_ptr : i64
    %100 = memref.load_indirect %99+0
    std.call_runtime_if_nonnull @mm_decref %100
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    %101 = func.param ptr : StdI64
    memref.store %101, __destr_ptr
    %104 = memref.load __destr_ptr : i64
    %105 = memref.load_indirect %104+16
    %106 = arith.constant {value = -1 : i64}
    %107 = arith.cmpi eq %105, %106
    cf.cond_br %107 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %108 = memref.load __destr_ptr : i64
    %109 = memref.load_indirect %108+32
    std.call_runtime_if_nonnull @mm_decref %109
    cf.br skip_buf_0
  check_owned_0:
    %110 = memref.load __destr_ptr : i64
    %111 = memref.load_indirect %110+16
    %112 = arith.constant {value = -2 : i64}
    %113 = arith.cmpi ne %111, %112
    cf.cond_br %113 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %114 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %114
    %115 = memref.load __destr_ptr : i64
    %116 = memref.load_indirect %115+0
    std.call_runtime @mm_raw_free %116
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    %117 = func.param ptr : StdI64
    memref.store %117, __destr_ptr
    %118 = memref.load __destr_ptr : i64
    %119 = memref.load_indirect %118+0
    std.call_runtime_if_nonnull @mm_decref %119
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    x64.mov rax, rcx
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=64
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.xor ecx, ecx
    x64.mov [rbp-16], rcx
    x64.call stdlib.CommandLine.args
    x64.mov [rbp-24], rax
    x64.mov rdx, [rbp-24]
    x64.mov rcx, [rbp-24]
    x64.mov rdx, 1
    x64.call StringArray.get
    x64.mov [rbp-32], rax
    x64.xor ebx, ebx
    x64.cmp rdx, rbx
    x64.je main.otherwise_default_success_2
  otherwise_default_error_1:
    x64.lea_rdata rax, [__str_0]
    x64.mov rcx, rax
    x64.xor edx, edx
    x64.lea_func rbx, [__destruct_String]
    x64.mov rsi, rbx
    x64.mov [rbp-64], rcx
    x64.mov rdx, rsi
    x64.mov rcx, 16
    x64.mov r8, 4
    x64.call mm_alloc
    x64.mov [rbp-8], rax
    x64.lea_func rdi, [__destruct___ManagedMemory]
    x64.mov r8, rdi
    x64.mov rdx, r8
    x64.mov rcx, 40
    x64.mov r8, 3
    x64.call mm_alloc
    x64.mov [rbp-40], rax
    x64.mov r9, -2
    x64.mov rax, 1
    x64.xor ecx, ecx
    x64.mov rdx, [rbp-40]
    x64.mov rbx, [rbp-64]
    x64.mov [rdx+0], rbx
    x64.mov rdx, [rbp-40]
    x64.xor ebx, ebx
    x64.mov [rdx+8], rbx
    x64.mov rdx, [rbp-40]
    x64.mov [rdx+16], r9
    x64.mov rdx, [rbp-40]
    x64.mov [rdx+24], rax
    x64.mov rax, [rbp-40]
    x64.mov [rax+32], rcx
    x64.mov rax, [rbp-40]
    x64.mov rcx, [rbp-8]
    x64.mov [rcx+0], rax
    x64.mov rax, [rbp-40]
    x64.mov rcx, [rbp-40]
    x64.call mm_incref
    x64.mov rax, 1
    x64.mov rcx, [rbp-8]
    x64.mov [rcx+8], rax
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.call mm_incref
    x64.mov rax, [rbp-8]
    x64.mov [rbp-16], rax
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rbp-16]
    x64.call mm_incref
    x64.jmp main.otherwise_default_continue_3
  otherwise_default_success_2:
    x64.mov rax, [rbp-16]
    x64.test rax, rax
    x64.jz __nonnull_skip_0
    x64.mov rcx, [rbp-16]
    x64.call mm_decref
    x64.label __nonnull_skip_0
    x64.mov rcx, [rbp-32]
    x64.mov [rbp-16], rcx
    x64.jmp main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rbp-16]
    x64.call stdlib.Builtins.__int_fromString
    x64.xor ecx, ecx
    x64.mov [rbp-48], rcx
    x64.mov [rbp-56], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je main.otherwise_default_continue_7
  otherwise_default_error_6:
    x64.mov rax, [rbp-48]
    x64.mov [rbp-56], rax
    x64.jmp main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x64.mov rax, [rbp-56]
    x64.mov rcx, 1000
    x64.cmp rax, rcx
    x64.jle main.guard_8.after
  guard_8:
    x64.mov rax, [rbp-16]
    x64.test rax, rax
    x64.jz __nonnull_skip_1
    x64.mov rcx, [rbp-16]
    x64.call mm_decref
    x64.label __nonnull_skip_1
    x64.mov rcx, [rbp-8]
    x64.test rcx, rcx
    x64.jz __nonnull_skip_2
    x64.call mm_decref
    x64.label __nonnull_skip_2
    x64.mov rdx, [rbp-24]
    x64.test rdx, rdx
    x64.jz __nonnull_skip_3
    x64.mov rcx, [rbp-24]
    x64.call mm_decref
    x64.label __nonnull_skip_3
    x64.mov rax, 99
    x64.epilogue
    x64.ret
  guard_8.after:
    x64.mov rcx, 3
    x64.call advent.multiply
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_9
    x64.cmp rax, rcx
    x64.jl main.__range_panic_9
    x64.jmp main.__range_ok_9
  __range_panic_9:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_9:
    x64.mov rbx, [rbp-16]
    x64.mov [rbp-64], rax
    x64.test rbx, rbx
    x64.jz __nonnull_skip_4
    x64.mov rcx, [rbp-16]
    x64.call mm_decref
    x64.label __nonnull_skip_4
    x64.mov rax, [rbp-8]
    x64.test rax, rax
    x64.jz __nonnull_skip_5
    x64.mov rcx, [rbp-8]
    x64.call mm_decref
    x64.label __nonnull_skip_5
    x64.mov rcx, [rbp-24]
    x64.test rcx, rcx
    x64.jz __nonnull_skip_6
    x64.call mm_decref
    x64.label __nonnull_skip_6
    x64.mov rax, [rbp-64]
    x64.epilogue
    x64.ret
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_11
    x64.call mm_decref
    x64.label __nonnull_skip_11
    x64.jmp __destruct_CodepointView.done
  done:
    x64.epilogue
    x64.ret
  }
  func @__destruct_CodepointIterator(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_12
    x64.call mm_decref
    x64.label __nonnull_skip_12
    x64.jmp __destruct_CodepointIterator.done
  done:
    x64.epilogue
    x64.ret
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -1
    x64.cmp rcx, rdx
    x64.jne __destruct___ManagedMemory.check_owned_0
  slice_cleanup_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+32]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_13
    x64.call mm_decref
    x64.label __nonnull_skip_13
    x64.jmp __destruct___ManagedMemory.skip_buf_0
  check_owned_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -2
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
  func @__destruct_String(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_14
    x64.call mm_decref
    x64.label __nonnull_skip_14
    x64.jmp __destruct_String.done
  done:
    x64.epilogue
    x64.ret
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -1
    x64.cmp rcx, rdx
    x64.jne __destruct___ManagedMemory_String.check_owned_0
  slice_cleanup_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+32]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_15
    x64.call mm_decref
    x64.label __nonnull_skip_15
    x64.jmp __destruct___ManagedMemory_String.skip_buf_0
  check_owned_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -2
    x64.cmp rcx, rdx
    x64.je __destruct___ManagedMemory_String.skip_buf_0
  free_buf_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.call mm_decref_managed_elements
    x64.mov rcx, [rbp-8]
    x64.mov rdx, [rcx+0]
    x64.mov rcx, rdx
    x64.call mm_raw_free
    x64.jmp __destruct___ManagedMemory_String.skip_buf_0
  skip_buf_0:
    x64.jmp __destruct___ManagedMemory_String.done
  done:
    x64.epilogue
    x64.ret
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_16
    x64.call mm_decref
    x64.label __nonnull_skip_16
    x64.jmp __destruct_StringArray.done
  done:
    x64.epilogue
    x64.ret
  }
}
```

<!-- test: day4b -->
<!-- Args: 3 -->
```maxon

typealias Integer = int(i64.min to i64.max)

function multiply(x Integer) returns Integer
		return x * 2
end 'multiply'

function main() returns ExitCode
	let args = CommandLine.args()
	let parsed = try int.fromString(try args.get(1) otherwise "") otherwise 0
	if parsed > 1000 'guard'
		return 99
	end 'guard'
	return multiply(3)
end 'main'
```
```exitcode
6
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=64
    x64.call CommandLine.args
    x64.mov r12, r8
    x64.mov edx, 1
    x64.xor r13d, r13d
  inlined_Array.get_0_0:
    x64.mov rcx, [r12+0] (8b)
    x64.call stdlib.__managed_mem_get
    x64.mov r14, r8
    x64.test rdx, rdx
    x64.je inlined_Array.get_3_0
  inlined_Array.get_1_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov edx, 1
    x64.xor ecx, ecx
    x64.jmp inline_cont_main_0
  inlined_Array.get_3_0:
    x64.xor r15d, r15d
    x64.jmp __rc_edge_11_0
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.call __mm_decref_maybenull_helper
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov ecx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov [rbp+-8], r8
    x64.mov r8, [rbp+-8]
    x64.mov [r8+40], r13 (8b)
    x64.lea r9, [rip+__istr_0]
    x64.mov [r8+0], r9 (8b)
    x64.mov [r8+8], r13 (8b)
    x64.mov r9, -2
    x64.mov [r8+16], r9 (8b)
    x64.mov r9d, 1
    x64.mov [r8+24], r9 (8b)
    x64.mov [r8+32], r13 (8b)
    x64.lea rdx, [rip+__destruct_String]
    x64.mov ecx, 16
    x64.mov rax, r13
    x64.call stdlib.__mm_alloc
    x64.mov [rbp+-16], r8
    x64.mov rcx, [rbp+-16]
    x64.call stdlib.__mm_incref
    x64.mov r8, [rbp+-16]
    x64.mov r8, [rbp+-8]
    x64.mov r9, [rbp+-16]
    x64.mov [r9+0], r8 (8b)
    x64.mov r8d, 1
    x64.mov [r9+8], r8 (8b)
    x64.mov r8, [rbp+-16]
    x64.mov rcx, r8
  try_0.merge:
    x64.mov [rbp+-16], rcx
    x64.mov rcx, [rbp+-16]
    x64.call stdlib.__int_fromString
    x64.mov [rbp+-8], r8
    x64.mov [rbp+-24], rdx
    x64.mov rcx, [rbp+-16]
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, [rbp+-24]
    x64.test r8, r8
    x64.je try_1.ok
    x64.mov r8, r13
    x64.jmp try_1.merge
  try_1.ok:
    x64.mov r8, [rbp+-8]
  try_1.merge:
    x64.cmp r8, 1000
    x64.jle guard_0.after
  guard_0:
    x64.mov r8d, 99
    x64.epilogue
    x64.ret
  guard_0.after:
    x64.mov r8d, 6
    x64.epilogue
    x64.ret
  __rc_edge_11_0:
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r14
    x64.mov rdx, r15
    x64.jmp inline_cont_main_0
  }
}

```
```RequiredIR:arm64-macos
=== maxon
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {optimalType = i64}
    maxon.scope_end [x]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.call @stdlib.CommandLine.args
    maxon.assign %3 {var = __call_tmp_3} {decl = 1 : i1}
    maxon.assign %3 {var = args} {decl = 1 : i1}
    %4 = maxon.struct_var_ref args
    %5 = maxon.literal {value = 1 : i64}
    %8, %7 = maxon.try_call @StringArray.get %4, %5
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %7, %10 {op = ne}
    maxon.cond_br %11 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %9 = maxon.string_literal ""
    maxon.assign %9 {var = __lit_tmp_9} {decl = 1 : i1}
    maxon.assign %9 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_success_2:
    maxon.assign %8 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %12 = maxon.struct_var_ref __try_result_0
    %15, %14 = maxon.try_call @stdlib.Builtins.__int_fromString %12
    %16 = maxon.literal {value = 0 : i64}
    maxon.assign %16 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %15 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %14, %17 {op = ne}
    maxon.cond_br %18 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %19 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %19 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %20 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %20 {var = parsed} {kind = i64} {decl = 1 : i1}
    %21 = maxon.literal {value = 1000 : i64}
    %22 = maxon.binop %20, %21 {op = gt}
    maxon.cond_br %22 [then: guard_8, else: guard_8.after]
  guard_8:
    %23 = maxon.literal {value = 99 : i64}
    maxon.scope_end [args, __try_default_5, parsed, __lit_tmp_9, __try_result_0, __try_result_4]
    maxon.return %23
  guard_8.after:
    %24 = maxon.literal {value = 3 : i64}
    %25 = maxon.call @advent.multiply %24
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %25, %26 {op = lt}
    %28 = maxon.literal {value = 4294967295 : i64}
    %29 = maxon.binop %25, %28 {op = gt}
    %30 = maxon.binop %27, %29 {op = or}
    maxon.cond_br %30 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    maxon.panic "panic at day4b.test:15: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_9:
    maxon.scope_end [args, __try_default_5, parsed, __lit_tmp_9, __try_result_0, __try_result_4]
    maxon.return %25
  }
}
=== standard
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.muli %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %76 = arith.constant {value = 0 : i64}
    memref.store %76, __lit_tmp_9
    %77 = arith.constant {value = 0 : i64}
    memref.store %77, __try_result_0
    %3 = func.call @stdlib.CommandLine.args
    memref.store %3, args
    %6 = arith.constant {value = 1 : i64}
    %7 = memref.load args : i64
    %8, %9 = func.try_call @StringArray.get %7, %6
    memref.store %8, __callret_8
    %10 = arith.constant {value = 0 : i64}
    %11 = arith.cmpi ne %9, %10
    cf.cond_br %11 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %12 = memref.lea_rdata __str_0
    %13 = std.ptr_to_i64 %12
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.constant {value = 16 : i64}
    %16 = func.ref @__destruct_String
    %17 = std.ptr_to_i64 %16
    %18 = arith.constant {value = 4 : i64}
    %19 = std.call_runtime @mm_alloc %15, %17, %18
    memref.store %19, __lit_tmp_9
    %20 = arith.constant {value = 40 : i64}
    %21 = func.ref @__destruct___ManagedMemory
    %22 = std.ptr_to_i64 %21
    %23 = arith.constant {value = 3 : i64}
    %24 = std.call_runtime @mm_alloc %20, %22, %23
    memref.store %24, __strtmp_managed_9
    %25 = arith.constant {value = -2 : i64}
    %26 = arith.constant {value = 1 : i64}
    %27 = arith.constant {value = 0 : i64}
    %28 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %13, %28+0
    %29 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %14, %29+8
    %30 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %25, %30+16
    %31 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %26, %31+24
    %32 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %27, %32+32
    %33 = memref.load __strtmp_managed_9 : i64
    %34 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %33, %34+0
    %35 = memref.load __strtmp_managed_9 : i64
    std.call_runtime @mm_incref %35
    %36 = arith.constant {value = 1 : i64}
    %37 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %36, %37+8
    %38 = memref.load __lit_tmp_9 : i64
    std.call_runtime @mm_incref %38
    memref.store %19, __try_result_0
    %40 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %40
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %41 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %41
    %42 = memref.load __callret_8 : i64
    memref.store %42, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %43 = memref.load __try_result_0 : i64
    %44, %45 = func.try_call @stdlib.Builtins.__int_fromString %43
    %46 = arith.constant {value = 0 : i64}
    memref.store %46, __try_default_5
    memref.store %44, __try_result_4
    %47 = arith.constant {value = 0 : i64}
    %48 = arith.cmpi ne %45, %47
    cf.cond_br %48 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %49 = memref.load __try_default_5 : i64
    memref.store %49, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %50 = memref.load __try_result_4 : i64
    %51 = arith.constant {value = 1000 : i64}
    %52 = arith.cmpi gt %50, %51
    cf.cond_br %52 [then: guard_8, else: guard_8.after]
  guard_8:
    %53 = arith.constant {value = 99 : i64}
    %54 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %54
    %56 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %56
    %58 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %58
    func.return %53
  guard_8.after:
    %60 = arith.constant {value = 3 : i64}
    %61 = func.call @advent.multiply %60
    %62 = arith.constant {value = 0 : i64}
    %63 = arith.cmpi lt %61, %62
    %64 = arith.constant {value = 4294967295 : i64}
    %65 = arith.cmpi gt %61, %64
    %66 = arith.ori1 %63, %65
    cf.cond_br %66 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    %67 = memref.lea_symdata __panic_msg_0
    %68 = std.ptr_to_i64 %67
    std.call_runtime @mrt_panic %68
  __range_ok_9:
    %69 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %69
    %71 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %71
    %73 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %73
    func.return %61
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    %78 = func.param ptr : StdI64
    memref.store %78, __destr_ptr
    %79 = memref.load __destr_ptr : i64
    %80 = memref.load_indirect %79+0
    std.call_runtime_if_nonnull @mm_decref %80
    cf.br done
  done:
    func.return
  }
  func @__destruct_CodepointIterator(ptr: i64) {
  entry:
    %81 = func.param ptr : StdI64
    memref.store %81, __destr_ptr
    %82 = memref.load __destr_ptr : i64
    %83 = memref.load_indirect %82+0
    std.call_runtime_if_nonnull @mm_decref %83
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %84 = func.param ptr : StdI64
    memref.store %84, __destr_ptr
    %87 = memref.load __destr_ptr : i64
    %88 = memref.load_indirect %87+16
    %89 = arith.constant {value = -1 : i64}
    %90 = arith.cmpi eq %88, %89
    cf.cond_br %90 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %91 = memref.load __destr_ptr : i64
    %92 = memref.load_indirect %91+32
    std.call_runtime_if_nonnull @mm_decref %92
    cf.br skip_buf_0
  check_owned_0:
    %93 = memref.load __destr_ptr : i64
    %94 = memref.load_indirect %93+16
    %95 = arith.constant {value = -2 : i64}
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
  func @__destruct_String(ptr: i64) {
  entry:
    %99 = func.param ptr : StdI64
    memref.store %99, __destr_ptr
    %100 = memref.load __destr_ptr : i64
    %101 = memref.load_indirect %100+0
    std.call_runtime_if_nonnull @mm_decref %101
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    %102 = func.param ptr : StdI64
    memref.store %102, __destr_ptr
    %105 = memref.load __destr_ptr : i64
    %106 = memref.load_indirect %105+16
    %107 = arith.constant {value = -1 : i64}
    %108 = arith.cmpi eq %106, %107
    cf.cond_br %108 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %109 = memref.load __destr_ptr : i64
    %110 = memref.load_indirect %109+32
    std.call_runtime_if_nonnull @mm_decref %110
    cf.br skip_buf_0
  check_owned_0:
    %111 = memref.load __destr_ptr : i64
    %112 = memref.load_indirect %111+16
    %113 = arith.constant {value = -2 : i64}
    %114 = arith.cmpi ne %112, %113
    cf.cond_br %114 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %115 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %115
    %116 = memref.load __destr_ptr : i64
    %117 = memref.load_indirect %116+0
    std.call_runtime @mm_raw_free %117
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    %118 = func.param ptr : StdI64
    memref.store %118, __destr_ptr
    %119 = memref.load __destr_ptr : i64
    %120 = memref.load_indirect %119+0
    std.call_runtime_if_nonnull @mm_decref %120
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    arm64.mov x1, #2
    arm64.mul x2, x0, x1
    arm64.mov x0, x2
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=144
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-16]
    arm64.bl stdlib.CommandLine.args
    arm64.str x0, [x29, #-24]
    arm64.ldr x2, [x29, #-24]
    arm64.ldr x0, [x29, #-24]
    arm64.mov x1, #1
    arm64.bl StringArray.get
    arm64.str x0, [x29, #-32]
    arm64.mov x3, #0
    arm64.cmp x1, x3
    arm64.cset x4, ne
    arm64.cmp x4, #0
    arm64.b.ne main.otherwise_default_error_1
    arm64.b main.otherwise_default_success_2
  otherwise_default_error_1:
    arm64.adrp_add_rdata x0, __str_0
    arm64.mov x1, x0
    arm64.mov x2, #0
    arm64.adrp_add_func x3, __destruct_String
    arm64.mov x4, x3
    arm64.str x1, [x29, #-64]
    arm64.mov x1, x4
    arm64.mov x0, #16
    arm64.mov x2, #4
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-8]
    arm64.adrp_add_func x5, __destruct___ManagedMemory
    arm64.mov x6, x5
    arm64.mov x1, x6
    arm64.mov x0, #40
    arm64.mov x2, #3
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-40]
    arm64.mov x7, #-2
    arm64.mov x8, #1
    arm64.mov x9, #0
    arm64.ldr x10, [x29, #-40]
    arm64.ldr x11, [x29, #-64]
    arm64.str x11, [x10, #0]
    arm64.ldr x12, [x29, #-40]
    arm64.mov x13, #0
    arm64.str x13, [x12, #8]
    arm64.ldr x14, [x29, #-40]
    arm64.str x7, [x14, #16]
    arm64.ldr x15, [x29, #-40]
    arm64.str x8, [x15, #24]
    arm64.ldr x0, [x29, #-40]
    arm64.str x9, [x0, #32]
    arm64.ldr x0, [x29, #-40]
    arm64.ldr x1, [x29, #-8]
    arm64.str x0, [x1, #0]
    arm64.ldr x0, [x29, #-40]
    arm64.bl mm_incref
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-8]
    arm64.str x0, [x1, #8]
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_incref
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_incref
    arm64.b main.otherwise_default_continue_3
  otherwise_default_success_2:
    arm64.ldr x0, [x29, #-16]
    arm64.cmp x0, #0
    arm64.b.eq main.__skip_guarded_56
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_56
    arm64.ldr x1, [x29, #-32]
    arm64.str x1, [x29, #-16]
    arm64.b main.otherwise_default_continue_3
  otherwise_default_continue_3:
    arm64.ldr x0, [x29, #-16]
    arm64.bl stdlib.Builtins.__int_fromString
    arm64.mov x2, #0
    arm64.str x2, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_6
    arm64.b main.otherwise_default_continue_7
  otherwise_default_error_6:
    arm64.ldr x0, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.b main.otherwise_default_continue_7
  otherwise_default_continue_7:
    arm64.ldr x0, [x29, #-56]
    arm64.mov x1, #1000
    arm64.cmp x0, x1
    arm64.cset x2, gt
    arm64.cmp x2, #0
    arm64.b.ne main.guard_8
    arm64.b main.guard_8.after
  guard_8:
    arm64.ldr x0, [x29, #-16]
    arm64.cmp x0, #0
    arm64.b.eq main.__skip_guarded_77
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_77
    arm64.ldr x1, [x29, #-8]
    arm64.cmp x1, #0
    arm64.b.eq main.__skip_guarded_79
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_79
    arm64.ldr x2, [x29, #-24]
    arm64.cmp x2, #0
    arm64.b.eq main.__skip_guarded_81
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_81
    arm64.mov x0, #99
    arm64.epilogue stack_size=144
    arm64.ret
  guard_8.after:
    arm64.mov x0, #3
    arm64.bl advent.multiply
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov w1, #4294967295
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_9
    arm64.b main.__range_ok_9
  __range_panic_9:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_9:
    arm64.ldr x1, [x29, #-16]
    arm64.str x0, [x29, #-64]
    arm64.cmp x1, #0
    arm64.b.eq main.__skip_guarded_95
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_95
    arm64.ldr x0, [x29, #-8]
    arm64.cmp x0, #0
    arm64.b.eq main.__skip_guarded_97
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_97
    arm64.ldr x1, [x29, #-24]
    arm64.cmp x1, #0
    arm64.b.eq main.__skip_guarded_99
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_99
    arm64.ldr x0, [x29, #-64]
    arm64.epilogue stack_size=144
    arm64.ret
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_CodepointView.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_CodepointView.__skip_guarded_4
    arm64.b __destruct_CodepointView.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct_CodepointIterator(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_CodepointIterator.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_CodepointIterator.__skip_guarded_4
    arm64.b __destruct_CodepointIterator.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #-1
    arm64.cmp x1, x2
    arm64.cset x3, eq
    arm64.cmp x3, #0
    arm64.b.ne __destruct___ManagedMemory.slice_cleanup_0
    arm64.b __destruct___ManagedMemory.check_owned_0
  slice_cleanup_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #32]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct___ManagedMemory.__skip_guarded_9
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct___ManagedMemory.__skip_guarded_9
    arm64.b __destruct___ManagedMemory.skip_buf_0
  check_owned_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #-2
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
  func @__destruct_String(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_String.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_String.__skip_guarded_4
    arm64.b __destruct_String.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #-1
    arm64.cmp x1, x2
    arm64.cset x3, eq
    arm64.cmp x3, #0
    arm64.b.ne __destruct___ManagedMemory_String.slice_cleanup_0
    arm64.b __destruct___ManagedMemory_String.check_owned_0
  slice_cleanup_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #32]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct___ManagedMemory_String.__skip_guarded_9
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct___ManagedMemory_String.__skip_guarded_9
    arm64.b __destruct___ManagedMemory_String.skip_buf_0
  check_owned_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #-2
    arm64.cmp x1, x2
    arm64.cset x3, ne
    arm64.cmp x3, #0
    arm64.b.ne __destruct___ManagedMemory_String.free_buf_0
    arm64.b __destruct___ManagedMemory_String.skip_buf_0
  free_buf_0:
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_decref_managed_elements
    arm64.ldr x1, [x29, #-8]
    arm64.ldr x2, [x1, #0]
    arm64.mov x0, x2
    arm64.bl mm_raw_free
    arm64.b __destruct___ManagedMemory_String.skip_buf_0
  skip_buf_0:
    arm64.b __destruct___ManagedMemory_String.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_StringArray.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_StringArray.__skip_guarded_4
    arm64.b __destruct_StringArray.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```
```RequiredIR:x64-linux
=== maxon
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {optimalType = i64}
    maxon.scope_end [x]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.call @stdlib.CommandLine.args
    maxon.assign %3 {var = __call_tmp_3} {decl = 1 : i1}
    maxon.assign %3 {var = args} {decl = 1 : i1}
    %4 = maxon.struct_var_ref args
    %5 = maxon.literal {value = 1 : i64}
    %8, %7 = maxon.try_call @StringArray.get %4, %5
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %7, %10 {op = ne}
    maxon.cond_br %11 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %9 = maxon.string_literal ""
    maxon.assign %9 {var = __lit_tmp_9} {decl = 1 : i1}
    maxon.assign %9 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_success_2:
    maxon.assign %8 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %12 = maxon.struct_var_ref __try_result_0
    %15, %14 = maxon.try_call @stdlib.Builtins.__int_fromString %12
    %16 = maxon.literal {value = 0 : i64}
    maxon.assign %16 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %15 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %14, %17 {op = ne}
    maxon.cond_br %18 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %19 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %19 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %20 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %20 {var = parsed} {kind = i64} {decl = 1 : i1}
    %21 = maxon.literal {value = 1000 : i64}
    %22 = maxon.binop %20, %21 {op = gt}
    maxon.cond_br %22 [then: guard_8, else: guard_8.after]
  guard_8:
    %23 = maxon.literal {value = 99 : i64}
    maxon.scope_end [args, __try_default_5, parsed, __lit_tmp_9, __try_result_0, __try_result_4]
    maxon.return %23
  guard_8.after:
    %24 = maxon.literal {value = 3 : i64}
    %25 = maxon.call @advent.multiply %24
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %25, %26 {op = lt}
    %28 = maxon.literal {value = 4294967295 : i64}
    %29 = maxon.binop %25, %28 {op = gt}
    %30 = maxon.binop %27, %29 {op = or}
    maxon.cond_br %30 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    maxon.panic "panic at day4b.test:15: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_9:
    maxon.scope_end [args, __try_default_5, parsed, __lit_tmp_9, __try_result_0, __try_result_4]
    maxon.return %25
  }
}
=== standard
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.muli %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %76 = arith.constant {value = 0 : i64}
    memref.store %76, __lit_tmp_9
    %77 = arith.constant {value = 0 : i64}
    memref.store %77, __try_result_0
    %3 = func.call @stdlib.CommandLine.args
    memref.store %3, args
    %6 = arith.constant {value = 1 : i64}
    %7 = memref.load args : i64
    %8, %9 = func.try_call @StringArray.get %7, %6
    memref.store %8, __callret_8
    %10 = arith.constant {value = 0 : i64}
    %11 = arith.cmpi ne %9, %10
    cf.cond_br %11 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %12 = memref.lea_rdata __str_0
    %13 = std.ptr_to_i64 %12
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.constant {value = 16 : i64}
    %16 = func.ref @__destruct_String
    %17 = std.ptr_to_i64 %16
    %18 = arith.constant {value = 4 : i64}
    %19 = std.call_runtime @mm_alloc %15, %17, %18
    memref.store %19, __lit_tmp_9
    %20 = arith.constant {value = 40 : i64}
    %21 = func.ref @__destruct___ManagedMemory
    %22 = std.ptr_to_i64 %21
    %23 = arith.constant {value = 3 : i64}
    %24 = std.call_runtime @mm_alloc %20, %22, %23
    memref.store %24, __strtmp_managed_9
    %25 = arith.constant {value = -2 : i64}
    %26 = arith.constant {value = 1 : i64}
    %27 = arith.constant {value = 0 : i64}
    %28 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %13, %28+0
    %29 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %14, %29+8
    %30 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %25, %30+16
    %31 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %26, %31+24
    %32 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %27, %32+32
    %33 = memref.load __strtmp_managed_9 : i64
    %34 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %33, %34+0
    %35 = memref.load __strtmp_managed_9 : i64
    std.call_runtime @mm_incref %35
    %36 = arith.constant {value = 1 : i64}
    %37 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %36, %37+8
    %38 = memref.load __lit_tmp_9 : i64
    std.call_runtime @mm_incref %38
    memref.store %19, __try_result_0
    %40 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %40
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %41 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %41
    %42 = memref.load __callret_8 : i64
    memref.store %42, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %43 = memref.load __try_result_0 : i64
    %44, %45 = func.try_call @stdlib.Builtins.__int_fromString %43
    %46 = arith.constant {value = 0 : i64}
    memref.store %46, __try_default_5
    memref.store %44, __try_result_4
    %47 = arith.constant {value = 0 : i64}
    %48 = arith.cmpi ne %45, %47
    cf.cond_br %48 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %49 = memref.load __try_default_5 : i64
    memref.store %49, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %50 = memref.load __try_result_4 : i64
    %51 = arith.constant {value = 1000 : i64}
    %52 = arith.cmpi gt %50, %51
    cf.cond_br %52 [then: guard_8, else: guard_8.after]
  guard_8:
    %53 = arith.constant {value = 99 : i64}
    %54 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %54
    %56 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %56
    %58 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %58
    func.return %53
  guard_8.after:
    %60 = arith.constant {value = 3 : i64}
    %61 = func.call @advent.multiply %60
    %62 = arith.constant {value = 0 : i64}
    %63 = arith.cmpi lt %61, %62
    %64 = arith.constant {value = 4294967295 : i64}
    %65 = arith.cmpi gt %61, %64
    %66 = arith.ori1 %63, %65
    cf.cond_br %66 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    %67 = memref.lea_symdata __panic_msg_0
    %68 = std.ptr_to_i64 %67
    std.call_runtime @mrt_panic %68
  __range_ok_9:
    %69 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %69
    %71 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %71
    %73 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %73
    func.return %61
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    %78 = func.param ptr : StdI64
    memref.store %78, __destr_ptr
    %79 = memref.load __destr_ptr : i64
    %80 = memref.load_indirect %79+0
    std.call_runtime_if_nonnull @mm_decref %80
    cf.br done
  done:
    func.return
  }
  func @__destruct_CodepointIterator(ptr: i64) {
  entry:
    %81 = func.param ptr : StdI64
    memref.store %81, __destr_ptr
    %82 = memref.load __destr_ptr : i64
    %83 = memref.load_indirect %82+0
    std.call_runtime_if_nonnull @mm_decref %83
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %84 = func.param ptr : StdI64
    memref.store %84, __destr_ptr
    %87 = memref.load __destr_ptr : i64
    %88 = memref.load_indirect %87+16
    %89 = arith.constant {value = -1 : i64}
    %90 = arith.cmpi eq %88, %89
    cf.cond_br %90 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %91 = memref.load __destr_ptr : i64
    %92 = memref.load_indirect %91+32
    std.call_runtime_if_nonnull @mm_decref %92
    cf.br skip_buf_0
  check_owned_0:
    %93 = memref.load __destr_ptr : i64
    %94 = memref.load_indirect %93+16
    %95 = arith.constant {value = -2 : i64}
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
  func @__destruct_String(ptr: i64) {
  entry:
    %99 = func.param ptr : StdI64
    memref.store %99, __destr_ptr
    %100 = memref.load __destr_ptr : i64
    %101 = memref.load_indirect %100+0
    std.call_runtime_if_nonnull @mm_decref %101
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    %102 = func.param ptr : StdI64
    memref.store %102, __destr_ptr
    %105 = memref.load __destr_ptr : i64
    %106 = memref.load_indirect %105+16
    %107 = arith.constant {value = -1 : i64}
    %108 = arith.cmpi eq %106, %107
    cf.cond_br %108 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %109 = memref.load __destr_ptr : i64
    %110 = memref.load_indirect %109+32
    std.call_runtime_if_nonnull @mm_decref %110
    cf.br skip_buf_0
  check_owned_0:
    %111 = memref.load __destr_ptr : i64
    %112 = memref.load_indirect %111+16
    %113 = arith.constant {value = -2 : i64}
    %114 = arith.cmpi ne %112, %113
    cf.cond_br %114 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %115 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %115
    %116 = memref.load __destr_ptr : i64
    %117 = memref.load_indirect %116+0
    std.call_runtime @mm_raw_free %117
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    %118 = func.param ptr : StdI64
    memref.store %118, __destr_ptr
    %119 = memref.load __destr_ptr : i64
    %120 = memref.load_indirect %119+0
    std.call_runtime_if_nonnull @mm_decref %120
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    x64.mov rax, 2
    x64.imul rcx, rax
    x64.mov rax, rcx
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=64
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.xor ecx, ecx
    x64.mov [rbp-16], rcx
    x64.call stdlib.CommandLine.args
    x64.mov [rbp-24], rax
    x64.mov rdx, [rbp-24]
    x64.mov rcx, [rbp-24]
    x64.mov rdx, 1
    x64.call StringArray.get
    x64.mov [rbp-32], rax
    x64.xor ebx, ebx
    x64.cmp rdx, rbx
    x64.je main.otherwise_default_success_2
  otherwise_default_error_1:
    x64.lea_rdata rax, [__str_0]
    x64.mov rcx, rax
    x64.xor edx, edx
    x64.lea_func rbx, [__destruct_String]
    x64.mov rsi, rbx
    x64.mov [rbp-64], rcx
    x64.mov rdx, rsi
    x64.mov rcx, 16
    x64.mov r8, 4
    x64.call mm_alloc
    x64.mov [rbp-8], rax
    x64.lea_func rdi, [__destruct___ManagedMemory]
    x64.mov r8, rdi
    x64.mov rdx, r8
    x64.mov rcx, 40
    x64.mov r8, 3
    x64.call mm_alloc
    x64.mov [rbp-40], rax
    x64.mov r9, -2
    x64.mov rax, 1
    x64.xor ecx, ecx
    x64.mov rdx, [rbp-40]
    x64.mov rbx, [rbp-64]
    x64.mov [rdx+0], rbx
    x64.mov rdx, [rbp-40]
    x64.xor ebx, ebx
    x64.mov [rdx+8], rbx
    x64.mov rdx, [rbp-40]
    x64.mov [rdx+16], r9
    x64.mov rdx, [rbp-40]
    x64.mov [rdx+24], rax
    x64.mov rax, [rbp-40]
    x64.mov [rax+32], rcx
    x64.mov rax, [rbp-40]
    x64.mov rcx, [rbp-8]
    x64.mov [rcx+0], rax
    x64.mov rax, [rbp-40]
    x64.mov rcx, [rbp-40]
    x64.call mm_incref
    x64.mov rax, 1
    x64.mov rcx, [rbp-8]
    x64.mov [rcx+8], rax
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.call mm_incref
    x64.mov rax, [rbp-8]
    x64.mov [rbp-16], rax
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rbp-16]
    x64.call mm_incref
    x64.jmp main.otherwise_default_continue_3
  otherwise_default_success_2:
    x64.mov rax, [rbp-16]
    x64.test rax, rax
    x64.jz __nonnull_skip_0
    x64.mov rcx, [rbp-16]
    x64.call mm_decref
    x64.label __nonnull_skip_0
    x64.mov rcx, [rbp-32]
    x64.mov [rbp-16], rcx
    x64.jmp main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rbp-16]
    x64.call stdlib.Builtins.__int_fromString
    x64.xor ecx, ecx
    x64.mov [rbp-48], rcx
    x64.mov [rbp-56], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je main.otherwise_default_continue_7
  otherwise_default_error_6:
    x64.mov rax, [rbp-48]
    x64.mov [rbp-56], rax
    x64.jmp main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x64.mov rax, [rbp-56]
    x64.mov rcx, 1000
    x64.cmp rax, rcx
    x64.jle main.guard_8.after
  guard_8:
    x64.mov rax, [rbp-16]
    x64.test rax, rax
    x64.jz __nonnull_skip_1
    x64.mov rcx, [rbp-16]
    x64.call mm_decref
    x64.label __nonnull_skip_1
    x64.mov rcx, [rbp-8]
    x64.test rcx, rcx
    x64.jz __nonnull_skip_2
    x64.call mm_decref
    x64.label __nonnull_skip_2
    x64.mov rdx, [rbp-24]
    x64.test rdx, rdx
    x64.jz __nonnull_skip_3
    x64.mov rcx, [rbp-24]
    x64.call mm_decref
    x64.label __nonnull_skip_3
    x64.mov rax, 99
    x64.epilogue
    x64.ret
  guard_8.after:
    x64.mov rcx, 3
    x64.call advent.multiply
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_9
    x64.cmp rax, rcx
    x64.jl main.__range_panic_9
    x64.jmp main.__range_ok_9
  __range_panic_9:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_9:
    x64.mov rbx, [rbp-16]
    x64.mov [rbp-64], rax
    x64.test rbx, rbx
    x64.jz __nonnull_skip_4
    x64.mov rcx, [rbp-16]
    x64.call mm_decref
    x64.label __nonnull_skip_4
    x64.mov rax, [rbp-8]
    x64.test rax, rax
    x64.jz __nonnull_skip_5
    x64.mov rcx, [rbp-8]
    x64.call mm_decref
    x64.label __nonnull_skip_5
    x64.mov rcx, [rbp-24]
    x64.test rcx, rcx
    x64.jz __nonnull_skip_6
    x64.call mm_decref
    x64.label __nonnull_skip_6
    x64.mov rax, [rbp-64]
    x64.epilogue
    x64.ret
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_11
    x64.call mm_decref
    x64.label __nonnull_skip_11
    x64.jmp __destruct_CodepointView.done
  done:
    x64.epilogue
    x64.ret
  }
  func @__destruct_CodepointIterator(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_12
    x64.call mm_decref
    x64.label __nonnull_skip_12
    x64.jmp __destruct_CodepointIterator.done
  done:
    x64.epilogue
    x64.ret
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -1
    x64.cmp rcx, rdx
    x64.jne __destruct___ManagedMemory.check_owned_0
  slice_cleanup_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+32]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_13
    x64.call mm_decref
    x64.label __nonnull_skip_13
    x64.jmp __destruct___ManagedMemory.skip_buf_0
  check_owned_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -2
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
  func @__destruct_String(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_14
    x64.call mm_decref
    x64.label __nonnull_skip_14
    x64.jmp __destruct_String.done
  done:
    x64.epilogue
    x64.ret
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -1
    x64.cmp rcx, rdx
    x64.jne __destruct___ManagedMemory_String.check_owned_0
  slice_cleanup_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+32]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_15
    x64.call mm_decref
    x64.label __nonnull_skip_15
    x64.jmp __destruct___ManagedMemory_String.skip_buf_0
  check_owned_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -2
    x64.cmp rcx, rdx
    x64.je __destruct___ManagedMemory_String.skip_buf_0
  free_buf_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.call mm_decref_managed_elements
    x64.mov rcx, [rbp-8]
    x64.mov rdx, [rcx+0]
    x64.mov rcx, rdx
    x64.call mm_raw_free
    x64.jmp __destruct___ManagedMemory_String.skip_buf_0
  skip_buf_0:
    x64.jmp __destruct___ManagedMemory_String.done
  done:
    x64.epilogue
    x64.ret
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_16
    x64.call mm_decref
    x64.label __nonnull_skip_16
    x64.jmp __destruct_StringArray.done
  done:
    x64.epilogue
    x64.ret
  }
}
```
