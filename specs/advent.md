---
feature: advent of compiler optimization
status: stable
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
```RequiredMLIR:x86_64-windows
=== maxon
module {
  func @advent.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.scope_end []
    maxon.return %0
  }
}
=== standard
module {
  func @advent.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    func.return %0
  }
}
=== x86
module {
  func @advent.main() -> u32 {
  entry:
    x86.xor rax, rax
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
=== maxon
module {
  func @advent.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.scope_end []
    maxon.return %0
  }
}
=== standard
module {
  func @advent.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    func.return %0
  }
}
=== arm64
module {
  func @advent.main() -> u32 {
  entry:
    arm64.mov x0, #0
    arm64.ret
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
```RequiredMLIR:x86_64-windows
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
  func @advent.main() -> i64 {
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
    maxon.panic "panic at day2.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
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
  func @advent.main() -> u32 {
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
    %11 = memref.lea_symdata __panic_msg_11
    %12 = std.ptr_to_i64 %11
    std.call_runtime @maxon_panic %12
  __range_ok_0:
    func.return %5
  }
}
=== x86
module {
  func @advent.add(x: i64, y: i64) -> i64 {
  entry:
    x86.lea rax, [rcx + rdx]
    x86.ret
  }
  func @advent.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov rcx, 3
    x86.mov rdx, 4
    x86.call advent.add
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.setl rcx
    x86.movzx rcx, rcxb
    x86.mov rdx, 4294967295
    x86.cmp rax, rdx
    x86.setg rdx
    x86.movzx rdx, rdxb
    x86.or rcx, rdx
    x86.test rcx, rcx
    x86.je advent.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_11]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.epilogue
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
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
  func @advent.main() -> i64 {
  entry:
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.literal {value = 4 : i64}
    %5 = maxon.call @advent.add %3, %4
    maxon.assign %5 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at day2.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %12 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_end [__range_val_0]
    maxon.return %12
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
  func @advent.main() -> u32 {
  entry:
    %3 = arith.constant {value = 3 : i64}
    %4 = arith.constant {value = 4 : i64}
    %5 = func.call @advent.add %3, %4
    memref.store %5, __range_val_0
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi lt %5, %6
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.cmpi gt %5, %8
    %10 = arith.ori1 %7, %9
    cf.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %11 = memref.lea_symdata __panic_msg_11
    %12 = std.ptr_to_i64 %11
    std.call_runtime @maxon_panic %12
  __range_ok_0:
    %13 = memref.load __range_val_0 : i64
    func.return %13
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
  func @advent.main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #3
    arm64.mov x1, #4
    arm64.bl advent.add
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #4294967295
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x0, x2, x3
    arm64.cmp x0, #0
    arm64.b.ne advent.main.__range_panic_0
    arm64.b advent.main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_11
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.ldr x0, [x29, #-8]
    arm64.epilogue stack_size=48
    arm64.ret
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
  let parsed = try int.fromString(try args.get(0) otherwise "") otherwise 0
  if parsed > 1000 'guard'
    return 99
  end 'guard'
  return multiply(3)
end 'main'
```
```exitcode
3
```
```RequiredMLIR:x86_64-windows
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
  func @advent.main() -> i64 {
  entry:
    %3 = maxon.call @stdlib.CommandLine.args
    maxon.assign %3 {var = __call_tmp_3} {decl = 1 : i1}
    maxon.assign %3 {var = args} {decl = 1 : i1}
    %4 = maxon.struct_var_ref args
    %5 = maxon.literal {value = 0 : i64}
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
    %15, %14 = maxon.try_call @stdlib.Parsing.__int_fromString %12
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
    maxon.panic "panic at day4a.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
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
  func @advent.main() -> u32 {
  entry:
    %73 = arith.constant {value = 0 : i64}
    memref.store %73, __lit_tmp_9
    %74 = arith.constant {value = 0 : i64}
    memref.store %74, __try_result_0
    %2 = func.call @stdlib.CommandLine.args
    memref.store %2, args
    %5 = arith.constant {value = 0 : i64}
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
    %19 = arith.constant {value = 32 : i64}
    %20 = func.ref @__destruct___ManagedMemory
    %21 = std.ptr_to_i64 %20
    %22 = arith.constant {value = 3 : i64}
    %23 = std.call_runtime @mm_alloc %19, %21, %22
    memref.store %23, __strtmp_managed_9
    %24 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %12, %24+0
    %25 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %13, %25+8
    %26 = arith.constant {value = 0 : i64}
    %27 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %26, %27+16
    %28 = arith.constant {value = 1 : i64}
    %29 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %28, %29+24
    %30 = memref.load __strtmp_managed_9 : i64
    %31 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %30, %31+0
    %32 = memref.load __strtmp_managed_9 : i64
    std.call_runtime @mm_incref %32
    %33 = arith.constant {value = 0 : i64}
    %34 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %33, %34+8
    %35 = memref.load __lit_tmp_9 : i64
    std.call_runtime @mm_incref %35
    memref.store %18, __try_result_0
    %37 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %37
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %38 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %38
    %39 = memref.load __callret_8 : i64
    memref.store %39, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %40 = memref.load __try_result_0 : i64
    %41, %42 = func.try_call @stdlib.Parsing.__int_fromString %40
    %43 = arith.constant {value = 0 : i64}
    memref.store %43, __try_default_5
    memref.store %41, __try_result_4
    %44 = arith.constant {value = 0 : i64}
    %45 = arith.cmpi ne %42, %44
    cf.cond_br %45 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %46 = memref.load __try_default_5 : i64
    memref.store %46, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %47 = memref.load __try_result_4 : i64
    %48 = arith.constant {value = 1000 : i64}
    %49 = arith.cmpi gt %47, %48
    cf.cond_br %49 [then: guard_8, else: guard_8.after]
  guard_8:
    %50 = arith.constant {value = 99 : i64}
    %51 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %51
    %53 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %53
    %55 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %55
    func.return %50
  guard_8.after:
    %57 = arith.constant {value = 3 : i64}
    %58 = func.call @advent.multiply %57
    %59 = arith.constant {value = 0 : i64}
    %60 = arith.cmpi lt %58, %59
    %61 = arith.constant {value = 4294967295 : i64}
    %62 = arith.cmpi gt %58, %61
    %63 = arith.ori1 %60, %62
    cf.cond_br %63 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    %64 = memref.lea_symdata __panic_msg_31
    %65 = std.ptr_to_i64 %64
    std.call_runtime @maxon_panic %65
  __range_ok_9:
    %66 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %66
    %68 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %68
    %70 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %70
    func.return %58
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    %215 = func.param ptr : StdI64
    memref.store %215, __destr_ptr
    %218 = memref.load __destr_ptr : i64
    %219 = memref.load_indirect %218+16
    %220 = arith.constant {value = 0 : i64}
    %221 = arith.cmpi ne %219, %220
    cf.cond_br %221 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %222 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %222
    %223 = memref.load __destr_ptr : i64
    %224 = memref.load_indirect %223+0
    std.call_runtime @mm_raw_free %224
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    %225 = func.param ptr : StdI64
    memref.store %225, __destr_ptr
    %226 = memref.load __destr_ptr : i64
    %227 = memref.load_indirect %226+8
    std.call_runtime_if_nonnull @mm_decref %227
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %228 = func.param ptr : StdI64
    memref.store %228, __destr_ptr
    %231 = memref.load __destr_ptr : i64
    %232 = memref.load_indirect %231+16
    %233 = arith.constant {value = 0 : i64}
    %234 = arith.cmpi ne %232, %233
    cf.cond_br %234 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %235 = memref.load __destr_ptr : i64
    %236 = memref.load_indirect %235+0
    std.call_runtime @mm_raw_free %236
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_String(ptr: i64) {
  entry:
    %237 = func.param ptr : StdI64
    memref.store %237, __destr_ptr
    %238 = memref.load __destr_ptr : i64
    %239 = memref.load_indirect %238+0
    std.call_runtime_if_nonnull @mm_decref %239
    cf.br done
  done:
    func.return
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    %240 = func.param ptr : StdI64
    memref.store %240, __destr_ptr
    %241 = memref.load __destr_ptr : i64
    %242 = memref.load_indirect %241+0
    std.call_runtime_if_nonnull @mm_decref %242
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    x86.mov rax, rcx
    x86.ret
  }
  func @advent.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor rax, rax
    x86.mov [rbp-8], rax
    x86.xor rcx, rcx
    x86.mov [rbp-16], rcx
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-24], rax
    x86.mov rdx, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.xor rdx, rdx
    x86.call StringArray.get
    x86.mov [rbp-32], rax
    x86.xor rbx, rbx
    x86.cmp rdx, rbx
    x86.je advent.main.otherwise_default_success_2
  otherwise_default_error_1:
    x86.lea_rdata rax, [__str_0]
    x86.mov rcx, rax
    x86.xor rdx, rdx
    x86.lea_func rbx, [__destruct_String]
    x86.mov rsi, rbx
    x86.mov [rbp-64], rcx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-8], rax
    x86.lea_func rdi, [__destruct___ManagedMemory]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-40], rax
    x86.mov r9, [rbp-40]
    x86.mov rax, [rbp-64]
    x86.mov [r9+0], rax
    x86.mov rax, [rbp-40]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.xor rax, rax
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_incref
    x86.mov rax, [rbp-8]
    x86.mov [rbp-16], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_success_2:
    x86.mov rax, [rbp-16]
    x86.test rax, rax
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov rcx, [rbp-32]
    x86.mov [rbp-16], rcx
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call stdlib.Parsing.__int_fromString
    x86.xor rcx, rcx
    x86.mov [rbp-48], rcx
    x86.mov [rbp-56], rax
    x86.xor rax, rax
    x86.cmp rdx, rax
    x86.je advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov rax, [rbp-48]
    x86.mov [rbp-56], rax
    x86.jmp advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov rax, [rbp-56]
    x86.mov rcx, 1000
    x86.cmp rax, rcx
    x86.jle advent.main.guard_8.after
  guard_8:
    x86.mov rax, [rbp-16]
    x86.test rax, rax
    x86.jz __nonnull_skip_1
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_1
    x86.mov rcx, [rbp-8]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_2
    x86.call mm_decref
    x86.label __nonnull_skip_2
    x86.mov rdx, [rbp-24]
    x86.test rdx, rdx
    x86.jz __nonnull_skip_3
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_3
    x86.mov rax, 99
    x86.epilogue
    x86.ret
  guard_8.after:
    x86.mov rcx, 3
    x86.call advent.multiply
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.setl rcx
    x86.movzx rcx, rcxb
    x86.mov rdx, 4294967295
    x86.cmp rax, rdx
    x86.setg rdx
    x86.movzx rdx, rdxb
    x86.or rcx, rdx
    x86.test rcx, rcx
    x86.je advent.main.__range_ok_9
  __range_panic_9:
    x86.lea_symdata rax, [__panic_msg_31]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_9:
    x86.mov rcx, [rbp-16]
    x86.mov [rbp-64], rax
    x86.test rcx, rcx
    x86.jz __nonnull_skip_4
    x86.call mm_decref
    x86.label __nonnull_skip_4
    x86.mov rax, [rbp-8]
    x86.test rax, rax
    x86.jz __nonnull_skip_5
    x86.mov rcx, [rbp-8]
    x86.call mm_decref
    x86.label __nonnull_skip_5
    x86.mov rcx, [rbp-24]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_6
    x86.call mm_decref
    x86.label __nonnull_skip_6
    x86.mov rax, [rbp-64]
    x86.epilogue
    x86.ret
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.je __destruct___ManagedMemory_String.skip_buf_0
  free_buf_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_managed_elements
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+0]
    x86.mov rcx, rdx
    x86.call mm_raw_free
    x86.jmp __destruct___ManagedMemory_String.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct___ManagedMemory_String.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_8
    x86.call mm_decref
    x86.label __nonnull_skip_8
    x86.jmp __destruct_StringArray.done
  done:
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
  func @__destruct_String(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_9
    x86.call mm_decref
    x86.label __nonnull_skip_9
    x86.jmp __destruct_String.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_10
    x86.call mm_decref
    x86.label __nonnull_skip_10
    x86.jmp __destruct_CodepointView.done
  done:
    x86.epilogue
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
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
  func @advent.main() -> i64 {
  entry:
    %3 = maxon.call @stdlib.CommandLine.args
    maxon.assign %3 {var = __call_tmp_3} {decl = 1 : i1}
    maxon.assign %3 {var = args} {decl = 1 : i1}
    %4 = maxon.struct_var_ref args
    %5 = maxon.literal {value = 0 : i64}
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
    %15, %14 = maxon.try_call @stdlib.Parsing.__int_fromString %12
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
    maxon.assign %25 {var = __range_val_9} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %25, %26 {op = lt}
    %28 = maxon.literal {value = 4294967295 : i64}
    %29 = maxon.binop %25, %28 {op = gt}
    %30 = maxon.binop %27, %29 {op = or}
    maxon.cond_br %30 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    maxon.panic "panic at day4a.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_9:
    %32 = maxon.var_ref {var = __range_val_9} {type = i64}
    maxon.scope_end [args, __try_default_5, parsed, __range_val_9, __lit_tmp_9, __try_result_0, __try_result_4]
    maxon.return %32
  }
}
=== standard
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    func.return %0
  }
  func @advent.main() -> u32 {
  entry:
    %74 = arith.constant {value = 0 : i64}
    memref.store %74, __lit_tmp_9
    %75 = arith.constant {value = 0 : i64}
    memref.store %75, __try_result_0
    %2 = func.call @stdlib.CommandLine.args
    memref.store %2, args
    %5 = arith.constant {value = 0 : i64}
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
    %19 = arith.constant {value = 32 : i64}
    %20 = func.ref @__destruct___ManagedMemory
    %21 = std.ptr_to_i64 %20
    %22 = arith.constant {value = 3 : i64}
    %23 = std.call_runtime @mm_alloc %19, %21, %22
    memref.store %23, __strtmp_managed_9
    %24 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %12, %24+0
    %25 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %13, %25+8
    %26 = arith.constant {value = 0 : i64}
    %27 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %26, %27+16
    %28 = arith.constant {value = 1 : i64}
    %29 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %28, %29+24
    %30 = memref.load __strtmp_managed_9 : i64
    %31 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %30, %31+0
    %32 = memref.load __strtmp_managed_9 : i64
    std.call_runtime @mm_incref %32
    %33 = arith.constant {value = 0 : i64}
    %34 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %33, %34+8
    %35 = memref.load __lit_tmp_9 : i64
    std.call_runtime @mm_incref %35
    memref.store %18, __try_result_0
    %37 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %37
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %38 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %38
    %39 = memref.load __callret_8 : i64
    memref.store %39, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %40 = memref.load __try_result_0 : i64
    %41, %42 = func.try_call @stdlib.Parsing.__int_fromString %40
    %43 = arith.constant {value = 0 : i64}
    memref.store %43, __try_default_5
    memref.store %41, __try_result_4
    %44 = arith.constant {value = 0 : i64}
    %45 = arith.cmpi ne %42, %44
    cf.cond_br %45 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %46 = memref.load __try_default_5 : i64
    memref.store %46, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %47 = memref.load __try_result_4 : i64
    %48 = arith.constant {value = 1000 : i64}
    %49 = arith.cmpi gt %47, %48
    cf.cond_br %49 [then: guard_8, else: guard_8.after]
  guard_8:
    %50 = arith.constant {value = 99 : i64}
    %51 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %51
    %53 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %53
    %55 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %55
    func.return %50
  guard_8.after:
    %57 = arith.constant {value = 3 : i64}
    %58 = func.call @advent.multiply %57
    memref.store %58, __range_val_9
    %59 = arith.constant {value = 0 : i64}
    %60 = arith.cmpi lt %58, %59
    %61 = arith.constant {value = 4294967295 : i64}
    %62 = arith.cmpi gt %58, %61
    %63 = arith.ori1 %60, %62
    cf.cond_br %63 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    %64 = memref.lea_symdata __panic_msg_31
    %65 = std.ptr_to_i64 %64
    std.call_runtime @maxon_panic %65
  __range_ok_9:
    %66 = memref.load __range_val_9 : i64
    %67 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %67
    %69 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %69
    %71 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %71
    func.return %66
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    %212 = func.param ptr : StdI64
    memref.store %212, __destr_ptr
    %215 = memref.load __destr_ptr : i64
    %216 = memref.load_indirect %215+16
    %217 = arith.constant {value = 0 : i64}
    %218 = arith.cmpi ne %216, %217
    cf.cond_br %218 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %219 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %219
    %220 = memref.load __destr_ptr : i64
    %221 = memref.load_indirect %220+0
    std.call_runtime @mm_raw_free %221
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    %222 = func.param ptr : StdI64
    memref.store %222, __destr_ptr
    %223 = memref.load __destr_ptr : i64
    %224 = memref.load_indirect %223+8
    std.call_runtime_if_nonnull @mm_decref %224
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %225 = func.param ptr : StdI64
    memref.store %225, __destr_ptr
    %228 = memref.load __destr_ptr : i64
    %229 = memref.load_indirect %228+16
    %230 = arith.constant {value = 0 : i64}
    %231 = arith.cmpi ne %229, %230
    cf.cond_br %231 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %232 = memref.load __destr_ptr : i64
    %233 = memref.load_indirect %232+0
    std.call_runtime @mm_raw_free %233
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_String(ptr: i64) {
  entry:
    %234 = func.param ptr : StdI64
    memref.store %234, __destr_ptr
    %235 = memref.load __destr_ptr : i64
    %236 = memref.load_indirect %235+0
    std.call_runtime_if_nonnull @mm_decref %236
    cf.br done
  done:
    func.return
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    %237 = func.param ptr : StdI64
    memref.store %237, __destr_ptr
    %238 = memref.load __destr_ptr : i64
    %239 = memref.load_indirect %238+0
    std.call_runtime_if_nonnull @mm_decref %239
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
  func @advent.main() -> u32 {
  entry:
    arm64.prologue stack_size=160
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-16]
    arm64.bl stdlib.CommandLine.args
    arm64.str x0, [x29, #-24]
    arm64.ldr x2, [x29, #-24]
    arm64.ldr x0, [x29, #-24]
    arm64.mov x1, #0
    arm64.bl StringArray.get
    arm64.str x0, [x29, #-32]
    arm64.mov x3, #0
    arm64.cmp x1, x3
    arm64.cset x4, ne
    arm64.cmp x4, #0
    arm64.b.ne advent.main.otherwise_default_error_1
    arm64.b advent.main.otherwise_default_success_2
  otherwise_default_error_1:
    arm64.adrp_add_rdata x0, __str_0
    arm64.mov x1, x0
    arm64.mov x2, #0
    arm64.adrp_add_func x3, __destruct_String
    arm64.mov x4, x3
    arm64.str x1, [x29, #-72]
    arm64.mov x1, x4
    arm64.mov x0, #16
    arm64.mov x2, #4
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-8]
    arm64.adrp_add_func x5, __destruct___ManagedMemory
    arm64.mov x6, x5
    arm64.mov x1, x6
    arm64.mov x0, #32
    arm64.mov x2, #3
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-40]
    arm64.ldr x7, [x29, #-40]
    arm64.ldr x8, [x29, #-72]
    arm64.str x8, [x7, #0]
    arm64.ldr x9, [x29, #-40]
    arm64.mov x10, #0
    arm64.str x10, [x9, #8]
    arm64.mov x11, #0
    arm64.ldr x12, [x29, #-40]
    arm64.str x11, [x12, #16]
    arm64.mov x13, #1
    arm64.ldr x14, [x29, #-40]
    arm64.str x13, [x14, #24]
    arm64.ldr x15, [x29, #-40]
    arm64.ldr x0, [x29, #-8]
    arm64.str x15, [x0, #0]
    arm64.ldr x0, [x29, #-40]
    arm64.bl mm_incref
    arm64.mov x0, #0
    arm64.ldr x1, [x29, #-8]
    arm64.str x0, [x1, #8]
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_incref
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_incref
    arm64.b advent.main.otherwise_default_continue_3
  otherwise_default_success_2:
    arm64.ldr x0, [x29, #-16]
    arm64.cmp x0, #0
    arm64.b.eq advent.main.__skip_guarded_53
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_53
    arm64.ldr x1, [x29, #-32]
    arm64.str x1, [x29, #-16]
    arm64.b advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    arm64.ldr x0, [x29, #-16]
    arm64.bl stdlib.Parsing.__int_fromString
    arm64.mov x2, #0
    arm64.str x2, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne advent.main.otherwise_default_error_6
    arm64.b advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    arm64.ldr x0, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.b advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    arm64.ldr x0, [x29, #-56]
    arm64.mov x1, #1000
    arm64.cmp x0, x1
    arm64.cset x2, gt
    arm64.cmp x2, #0
    arm64.b.ne advent.main.guard_8
    arm64.b advent.main.guard_8.after
  guard_8:
    arm64.ldr x0, [x29, #-16]
    arm64.cmp x0, #0
    arm64.b.eq advent.main.__skip_guarded_74
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_74
    arm64.ldr x1, [x29, #-8]
    arm64.cmp x1, #0
    arm64.b.eq advent.main.__skip_guarded_76
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_76
    arm64.ldr x2, [x29, #-24]
    arm64.cmp x2, #0
    arm64.b.eq advent.main.__skip_guarded_78
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_78
    arm64.mov x0, #99
    arm64.epilogue stack_size=160
    arm64.ret
  guard_8.after:
    arm64.mov x0, #3
    arm64.bl advent.multiply
    arm64.str x0, [x29, #-64]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #4294967295
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x0, x2, x3
    arm64.cmp x0, #0
    arm64.b.ne advent.main.__range_panic_9
    arm64.b advent.main.__range_ok_9
  __range_panic_9:
    arm64.adrp_add_symdata x0, __panic_msg_31
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_9:
    arm64.ldr x0, [x29, #-64]
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq advent.main.__skip_guarded_94
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_94
    arm64.ldr x2, [x29, #-8]
    arm64.cmp x2, #0
    arm64.b.eq advent.main.__skip_guarded_96
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_96
    arm64.ldr x3, [x29, #-24]
    arm64.cmp x3, #0
    arm64.b.eq advent.main.__skip_guarded_98
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_98
    arm64.ldr x0, [x29, #-64]
    arm64.epilogue stack_size=160
    arm64.ret
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #0
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
    arm64.ldr x1, [x0, #8]
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
  let parsed = try int.fromString(try args.get(0) otherwise "") otherwise 0
  if parsed > 1000 'guard'
    return 99
  end 'guard'
  return multiply(3)
end 'main'
```
```exitcode
6
```
```RequiredMLIR:x86_64-windows
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
  func @advent.main() -> i64 {
  entry:
    %3 = maxon.call @stdlib.CommandLine.args
    maxon.assign %3 {var = __call_tmp_3} {decl = 1 : i1}
    maxon.assign %3 {var = args} {decl = 1 : i1}
    %4 = maxon.struct_var_ref args
    %5 = maxon.literal {value = 0 : i64}
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
    %15, %14 = maxon.try_call @stdlib.Parsing.__int_fromString %12
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
    maxon.panic "panic at day4b.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
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
  func @advent.main() -> u32 {
  entry:
    %74 = arith.constant {value = 0 : i64}
    memref.store %74, __lit_tmp_9
    %75 = arith.constant {value = 0 : i64}
    memref.store %75, __try_result_0
    %3 = func.call @stdlib.CommandLine.args
    memref.store %3, args
    %6 = arith.constant {value = 0 : i64}
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
    %20 = arith.constant {value = 32 : i64}
    %21 = func.ref @__destruct___ManagedMemory
    %22 = std.ptr_to_i64 %21
    %23 = arith.constant {value = 3 : i64}
    %24 = std.call_runtime @mm_alloc %20, %22, %23
    memref.store %24, __strtmp_managed_9
    %25 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %13, %25+0
    %26 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %14, %26+8
    %27 = arith.constant {value = 0 : i64}
    %28 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %27, %28+16
    %29 = arith.constant {value = 1 : i64}
    %30 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %29, %30+24
    %31 = memref.load __strtmp_managed_9 : i64
    %32 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %31, %32+0
    %33 = memref.load __strtmp_managed_9 : i64
    std.call_runtime @mm_incref %33
    %34 = arith.constant {value = 0 : i64}
    %35 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %34, %35+8
    %36 = memref.load __lit_tmp_9 : i64
    std.call_runtime @mm_incref %36
    memref.store %19, __try_result_0
    %38 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %38
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %39 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %39
    %40 = memref.load __callret_8 : i64
    memref.store %40, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %41 = memref.load __try_result_0 : i64
    %42, %43 = func.try_call @stdlib.Parsing.__int_fromString %41
    %44 = arith.constant {value = 0 : i64}
    memref.store %44, __try_default_5
    memref.store %42, __try_result_4
    %45 = arith.constant {value = 0 : i64}
    %46 = arith.cmpi ne %43, %45
    cf.cond_br %46 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %47 = memref.load __try_default_5 : i64
    memref.store %47, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %48 = memref.load __try_result_4 : i64
    %49 = arith.constant {value = 1000 : i64}
    %50 = arith.cmpi gt %48, %49
    cf.cond_br %50 [then: guard_8, else: guard_8.after]
  guard_8:
    %51 = arith.constant {value = 99 : i64}
    %52 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %52
    %54 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %54
    %56 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %56
    func.return %51
  guard_8.after:
    %58 = arith.constant {value = 3 : i64}
    %59 = func.call @advent.multiply %58
    %60 = arith.constant {value = 0 : i64}
    %61 = arith.cmpi lt %59, %60
    %62 = arith.constant {value = 4294967295 : i64}
    %63 = arith.cmpi gt %59, %62
    %64 = arith.ori1 %61, %63
    cf.cond_br %64 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    %65 = memref.lea_symdata __panic_msg_31
    %66 = std.ptr_to_i64 %65
    std.call_runtime @maxon_panic %66
  __range_ok_9:
    %67 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %67
    %69 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %69
    %71 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %71
    func.return %59
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    %216 = func.param ptr : StdI64
    memref.store %216, __destr_ptr
    %219 = memref.load __destr_ptr : i64
    %220 = memref.load_indirect %219+16
    %221 = arith.constant {value = 0 : i64}
    %222 = arith.cmpi ne %220, %221
    cf.cond_br %222 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %223 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %223
    %224 = memref.load __destr_ptr : i64
    %225 = memref.load_indirect %224+0
    std.call_runtime @mm_raw_free %225
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    %226 = func.param ptr : StdI64
    memref.store %226, __destr_ptr
    %227 = memref.load __destr_ptr : i64
    %228 = memref.load_indirect %227+8
    std.call_runtime_if_nonnull @mm_decref %228
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %229 = func.param ptr : StdI64
    memref.store %229, __destr_ptr
    %232 = memref.load __destr_ptr : i64
    %233 = memref.load_indirect %232+16
    %234 = arith.constant {value = 0 : i64}
    %235 = arith.cmpi ne %233, %234
    cf.cond_br %235 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %236 = memref.load __destr_ptr : i64
    %237 = memref.load_indirect %236+0
    std.call_runtime @mm_raw_free %237
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_String(ptr: i64) {
  entry:
    %238 = func.param ptr : StdI64
    memref.store %238, __destr_ptr
    %239 = memref.load __destr_ptr : i64
    %240 = memref.load_indirect %239+0
    std.call_runtime_if_nonnull @mm_decref %240
    cf.br done
  done:
    func.return
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    %241 = func.param ptr : StdI64
    memref.store %241, __destr_ptr
    %242 = memref.load __destr_ptr : i64
    %243 = memref.load_indirect %242+0
    std.call_runtime_if_nonnull @mm_decref %243
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    x86.mov rax, 2
    x86.imul rcx, rax
    x86.mov rax, rcx
    x86.ret
  }
  func @advent.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor rax, rax
    x86.mov [rbp-8], rax
    x86.xor rcx, rcx
    x86.mov [rbp-16], rcx
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-24], rax
    x86.mov rdx, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.xor rdx, rdx
    x86.call StringArray.get
    x86.mov [rbp-32], rax
    x86.xor rbx, rbx
    x86.cmp rdx, rbx
    x86.je advent.main.otherwise_default_success_2
  otherwise_default_error_1:
    x86.lea_rdata rax, [__str_0]
    x86.mov rcx, rax
    x86.xor rdx, rdx
    x86.lea_func rbx, [__destruct_String]
    x86.mov rsi, rbx
    x86.mov [rbp-64], rcx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-8], rax
    x86.lea_func rdi, [__destruct___ManagedMemory]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-40], rax
    x86.mov r9, [rbp-40]
    x86.mov rax, [rbp-64]
    x86.mov [r9+0], rax
    x86.mov rax, [rbp-40]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.xor rax, rax
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+16], rax
    x86.mov rax, 1
    x86.mov rcx, [rbp-40]
    x86.mov [rcx+24], rax
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+0], rax
    x86.mov rax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.xor rax, rax
    x86.mov rcx, [rbp-8]
    x86.mov [rcx+8], rax
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_incref
    x86.mov rax, [rbp-8]
    x86.mov [rbp-16], rax
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_success_2:
    x86.mov rax, [rbp-16]
    x86.test rax, rax
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov rcx, [rbp-32]
    x86.mov [rbp-16], rcx
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call stdlib.Parsing.__int_fromString
    x86.xor rcx, rcx
    x86.mov [rbp-48], rcx
    x86.mov [rbp-56], rax
    x86.xor rax, rax
    x86.cmp rdx, rax
    x86.je advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov rax, [rbp-48]
    x86.mov [rbp-56], rax
    x86.jmp advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov rax, [rbp-56]
    x86.mov rcx, 1000
    x86.cmp rax, rcx
    x86.jle advent.main.guard_8.after
  guard_8:
    x86.mov rax, [rbp-16]
    x86.test rax, rax
    x86.jz __nonnull_skip_1
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_1
    x86.mov rcx, [rbp-8]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_2
    x86.call mm_decref
    x86.label __nonnull_skip_2
    x86.mov rdx, [rbp-24]
    x86.test rdx, rdx
    x86.jz __nonnull_skip_3
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_3
    x86.mov rax, 99
    x86.epilogue
    x86.ret
  guard_8.after:
    x86.mov rcx, 3
    x86.call advent.multiply
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.setl rcx
    x86.movzx rcx, rcxb
    x86.mov rdx, 4294967295
    x86.cmp rax, rdx
    x86.setg rdx
    x86.movzx rdx, rdxb
    x86.or rcx, rdx
    x86.test rcx, rcx
    x86.je advent.main.__range_ok_9
  __range_panic_9:
    x86.lea_symdata rax, [__panic_msg_31]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_9:
    x86.mov rcx, [rbp-16]
    x86.mov [rbp-64], rax
    x86.test rcx, rcx
    x86.jz __nonnull_skip_4
    x86.call mm_decref
    x86.label __nonnull_skip_4
    x86.mov rax, [rbp-8]
    x86.test rax, rax
    x86.jz __nonnull_skip_5
    x86.mov rcx, [rbp-8]
    x86.call mm_decref
    x86.label __nonnull_skip_5
    x86.mov rcx, [rbp-24]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_6
    x86.call mm_decref
    x86.label __nonnull_skip_6
    x86.mov rax, [rbp-64]
    x86.epilogue
    x86.ret
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.je __destruct___ManagedMemory_String.skip_buf_0
  free_buf_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_managed_elements
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+0]
    x86.mov rcx, rdx
    x86.call mm_raw_free
    x86.jmp __destruct___ManagedMemory_String.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct___ManagedMemory_String.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_8
    x86.call mm_decref
    x86.label __nonnull_skip_8
    x86.jmp __destruct_StringArray.done
  done:
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
  func @__destruct_String(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_9
    x86.call mm_decref
    x86.label __nonnull_skip_9
    x86.jmp __destruct_String.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+0]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_10
    x86.call mm_decref
    x86.label __nonnull_skip_10
    x86.jmp __destruct_CodepointView.done
  done:
    x86.epilogue
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
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
  func @advent.main() -> i64 {
  entry:
    %3 = maxon.call @stdlib.CommandLine.args
    maxon.assign %3 {var = __call_tmp_3} {decl = 1 : i1}
    maxon.assign %3 {var = args} {decl = 1 : i1}
    %4 = maxon.struct_var_ref args
    %5 = maxon.literal {value = 0 : i64}
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
    %15, %14 = maxon.try_call @stdlib.Parsing.__int_fromString %12
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
    maxon.assign %25 {var = __range_val_9} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %25, %26 {op = lt}
    %28 = maxon.literal {value = 4294967295 : i64}
    %29 = maxon.binop %25, %28 {op = gt}
    %30 = maxon.binop %27, %29 {op = or}
    maxon.cond_br %30 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    maxon.panic "panic at day4b.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_9:
    %32 = maxon.var_ref {var = __range_val_9} {type = i64}
    maxon.scope_end [args, __try_default_5, parsed, __range_val_9, __lit_tmp_9, __try_result_0, __try_result_4]
    maxon.return %32
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
  func @advent.main() -> u32 {
  entry:
    %75 = arith.constant {value = 0 : i64}
    memref.store %75, __lit_tmp_9
    %76 = arith.constant {value = 0 : i64}
    memref.store %76, __try_result_0
    %3 = func.call @stdlib.CommandLine.args
    memref.store %3, args
    %6 = arith.constant {value = 0 : i64}
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
    %20 = arith.constant {value = 32 : i64}
    %21 = func.ref @__destruct___ManagedMemory
    %22 = std.ptr_to_i64 %21
    %23 = arith.constant {value = 3 : i64}
    %24 = std.call_runtime @mm_alloc %20, %22, %23
    memref.store %24, __strtmp_managed_9
    %25 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %13, %25+0
    %26 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %14, %26+8
    %27 = arith.constant {value = 0 : i64}
    %28 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %27, %28+16
    %29 = arith.constant {value = 1 : i64}
    %30 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %29, %30+24
    %31 = memref.load __strtmp_managed_9 : i64
    %32 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %31, %32+0
    %33 = memref.load __strtmp_managed_9 : i64
    std.call_runtime @mm_incref %33
    %34 = arith.constant {value = 0 : i64}
    %35 = memref.load __lit_tmp_9 : i64
    memref.store_indirect %34, %35+8
    %36 = memref.load __lit_tmp_9 : i64
    std.call_runtime @mm_incref %36
    memref.store %19, __try_result_0
    %38 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %38
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %39 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %39
    %40 = memref.load __callret_8 : i64
    memref.store %40, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %41 = memref.load __try_result_0 : i64
    %42, %43 = func.try_call @stdlib.Parsing.__int_fromString %41
    %44 = arith.constant {value = 0 : i64}
    memref.store %44, __try_default_5
    memref.store %42, __try_result_4
    %45 = arith.constant {value = 0 : i64}
    %46 = arith.cmpi ne %43, %45
    cf.cond_br %46 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %47 = memref.load __try_default_5 : i64
    memref.store %47, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %48 = memref.load __try_result_4 : i64
    %49 = arith.constant {value = 1000 : i64}
    %50 = arith.cmpi gt %48, %49
    cf.cond_br %50 [then: guard_8, else: guard_8.after]
  guard_8:
    %51 = arith.constant {value = 99 : i64}
    %52 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %52
    %54 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %54
    %56 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %56
    func.return %51
  guard_8.after:
    %58 = arith.constant {value = 3 : i64}
    %59 = func.call @advent.multiply %58
    memref.store %59, __range_val_9
    %60 = arith.constant {value = 0 : i64}
    %61 = arith.cmpi lt %59, %60
    %62 = arith.constant {value = 4294967295 : i64}
    %63 = arith.cmpi gt %59, %62
    %64 = arith.ori1 %61, %63
    cf.cond_br %64 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    %65 = memref.lea_symdata __panic_msg_31
    %66 = std.ptr_to_i64 %65
    std.call_runtime @maxon_panic %66
  __range_ok_9:
    %67 = memref.load __range_val_9 : i64
    %68 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %68
    %70 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %70
    %72 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %72
    func.return %67
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    %213 = func.param ptr : StdI64
    memref.store %213, __destr_ptr
    %216 = memref.load __destr_ptr : i64
    %217 = memref.load_indirect %216+16
    %218 = arith.constant {value = 0 : i64}
    %219 = arith.cmpi ne %217, %218
    cf.cond_br %219 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %220 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %220
    %221 = memref.load __destr_ptr : i64
    %222 = memref.load_indirect %221+0
    std.call_runtime @mm_raw_free %222
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    %223 = func.param ptr : StdI64
    memref.store %223, __destr_ptr
    %224 = memref.load __destr_ptr : i64
    %225 = memref.load_indirect %224+8
    std.call_runtime_if_nonnull @mm_decref %225
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %226 = func.param ptr : StdI64
    memref.store %226, __destr_ptr
    %229 = memref.load __destr_ptr : i64
    %230 = memref.load_indirect %229+16
    %231 = arith.constant {value = 0 : i64}
    %232 = arith.cmpi ne %230, %231
    cf.cond_br %232 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %233 = memref.load __destr_ptr : i64
    %234 = memref.load_indirect %233+0
    std.call_runtime @mm_raw_free %234
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_String(ptr: i64) {
  entry:
    %235 = func.param ptr : StdI64
    memref.store %235, __destr_ptr
    %236 = memref.load __destr_ptr : i64
    %237 = memref.load_indirect %236+0
    std.call_runtime_if_nonnull @mm_decref %237
    cf.br done
  done:
    func.return
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    %238 = func.param ptr : StdI64
    memref.store %238, __destr_ptr
    %239 = memref.load __destr_ptr : i64
    %240 = memref.load_indirect %239+0
    std.call_runtime_if_nonnull @mm_decref %240
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
  func @advent.main() -> u32 {
  entry:
    arm64.prologue stack_size=160
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-16]
    arm64.bl stdlib.CommandLine.args
    arm64.str x0, [x29, #-24]
    arm64.ldr x2, [x29, #-24]
    arm64.ldr x0, [x29, #-24]
    arm64.mov x1, #0
    arm64.bl StringArray.get
    arm64.str x0, [x29, #-32]
    arm64.mov x3, #0
    arm64.cmp x1, x3
    arm64.cset x4, ne
    arm64.cmp x4, #0
    arm64.b.ne advent.main.otherwise_default_error_1
    arm64.b advent.main.otherwise_default_success_2
  otherwise_default_error_1:
    arm64.adrp_add_rdata x0, __str_0
    arm64.mov x1, x0
    arm64.mov x2, #0
    arm64.adrp_add_func x3, __destruct_String
    arm64.mov x4, x3
    arm64.str x1, [x29, #-72]
    arm64.mov x1, x4
    arm64.mov x0, #16
    arm64.mov x2, #4
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-8]
    arm64.adrp_add_func x5, __destruct___ManagedMemory
    arm64.mov x6, x5
    arm64.mov x1, x6
    arm64.mov x0, #32
    arm64.mov x2, #3
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-40]
    arm64.ldr x7, [x29, #-40]
    arm64.ldr x8, [x29, #-72]
    arm64.str x8, [x7, #0]
    arm64.ldr x9, [x29, #-40]
    arm64.mov x10, #0
    arm64.str x10, [x9, #8]
    arm64.mov x11, #0
    arm64.ldr x12, [x29, #-40]
    arm64.str x11, [x12, #16]
    arm64.mov x13, #1
    arm64.ldr x14, [x29, #-40]
    arm64.str x13, [x14, #24]
    arm64.ldr x15, [x29, #-40]
    arm64.ldr x0, [x29, #-8]
    arm64.str x15, [x0, #0]
    arm64.ldr x0, [x29, #-40]
    arm64.bl mm_incref
    arm64.mov x0, #0
    arm64.ldr x1, [x29, #-8]
    arm64.str x0, [x1, #8]
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_incref
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_incref
    arm64.b advent.main.otherwise_default_continue_3
  otherwise_default_success_2:
    arm64.ldr x0, [x29, #-16]
    arm64.cmp x0, #0
    arm64.b.eq advent.main.__skip_guarded_53
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_53
    arm64.ldr x1, [x29, #-32]
    arm64.str x1, [x29, #-16]
    arm64.b advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    arm64.ldr x0, [x29, #-16]
    arm64.bl stdlib.Parsing.__int_fromString
    arm64.mov x2, #0
    arm64.str x2, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne advent.main.otherwise_default_error_6
    arm64.b advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    arm64.ldr x0, [x29, #-48]
    arm64.str x0, [x29, #-56]
    arm64.b advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    arm64.ldr x0, [x29, #-56]
    arm64.mov x1, #1000
    arm64.cmp x0, x1
    arm64.cset x2, gt
    arm64.cmp x2, #0
    arm64.b.ne advent.main.guard_8
    arm64.b advent.main.guard_8.after
  guard_8:
    arm64.ldr x0, [x29, #-16]
    arm64.cmp x0, #0
    arm64.b.eq advent.main.__skip_guarded_74
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_74
    arm64.ldr x1, [x29, #-8]
    arm64.cmp x1, #0
    arm64.b.eq advent.main.__skip_guarded_76
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_76
    arm64.ldr x2, [x29, #-24]
    arm64.cmp x2, #0
    arm64.b.eq advent.main.__skip_guarded_78
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_78
    arm64.mov x0, #99
    arm64.epilogue stack_size=160
    arm64.ret
  guard_8.after:
    arm64.mov x0, #3
    arm64.bl advent.multiply
    arm64.str x0, [x29, #-64]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #4294967295
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x0, x2, x3
    arm64.cmp x0, #0
    arm64.b.ne advent.main.__range_panic_9
    arm64.b advent.main.__range_ok_9
  __range_panic_9:
    arm64.adrp_add_symdata x0, __panic_msg_31
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_9:
    arm64.ldr x0, [x29, #-64]
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq advent.main.__skip_guarded_94
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_94
    arm64.ldr x2, [x29, #-8]
    arm64.cmp x2, #0
    arm64.b.eq advent.main.__skip_guarded_96
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_96
    arm64.ldr x3, [x29, #-24]
    arm64.cmp x3, #0
    arm64.b.eq advent.main.__skip_guarded_98
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label advent.main.__skip_guarded_98
    arm64.ldr x0, [x29, #-64]
    arm64.epilogue stack_size=160
    arm64.ret
  }
  func @__destruct___ManagedMemory_String(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #0
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
    arm64.ldr x1, [x0, #8]
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
}
```
