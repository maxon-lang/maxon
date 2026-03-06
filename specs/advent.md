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
```RequiredMLIR
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
    x86.xor eax, eax
    x86.ret
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
```RequiredMLIR
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
=== x86
module {
  func @advent.add(x: i64, y: i64) -> i64 {
  entry:
    x86.lea eax, [ecx + edx]
    x86.ret
  }
  func @advent.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov rcx, 3
    x86.mov rdx, 4
    x86.call advent.add
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.cmp eax, ecx
    x86.setl ecx
    x86.movzx ecx, ecxb
    x86.mov rdx, 4294967295
    x86.cmp rax, rdx
    x86.setg eax
    x86.movzx eax, eaxb
    x86.or ecx, eax
    x86.test ecx, ecx
    x86.je advent.main.__range_ok_0
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
```RequiredMLIR
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
    %51 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %51
    %53 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %53
    %55 = memref.load __try_result_0 : i64
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
    %67 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %67
    %69 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %69
    %71 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %71
    func.return %66
  }
  func @__destruct_ElementMemory(ptr: i64) {
  entry:
    %206 = func.param ptr : StdI64
    memref.store %206, __destr_ptr
    %207 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %207
    %210 = memref.load __destr_ptr : i64
    %211 = memref.load_indirect %210+16
    %212 = arith.constant {value = 0 : i64}
    %213 = arith.cmpi ne %211, %212
    cf.cond_br %213 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %214 = memref.load __destr_ptr : i64
    %215 = memref.load_indirect %214+0
    std.call_runtime @mm_raw_free %215
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    %216 = func.param ptr : StdI64
    memref.store %216, __destr_ptr
    %217 = memref.load __destr_ptr : i64
    %218 = memref.load_indirect %217+8
    std.call_runtime_if_nonnull @mm_decref %218
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %219 = func.param ptr : StdI64
    memref.store %219, __destr_ptr
    %222 = memref.load __destr_ptr : i64
    %223 = memref.load_indirect %222+16
    %224 = arith.constant {value = 0 : i64}
    %225 = arith.cmpi ne %223, %224
    cf.cond_br %225 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %226 = memref.load __destr_ptr : i64
    %227 = memref.load_indirect %226+0
    std.call_runtime @mm_raw_free %227
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_String(ptr: i64) {
  entry:
    %228 = func.param ptr : StdI64
    memref.store %228, __destr_ptr
    %229 = memref.load __destr_ptr : i64
    %230 = memref.load_indirect %229+0
    std.call_runtime_if_nonnull @mm_decref %230
    cf.br done
  done:
    func.return
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    %231 = func.param ptr : StdI64
    memref.store %231, __destr_ptr
    %232 = memref.load __destr_ptr : i64
    %233 = memref.load_indirect %232+0
    std.call_runtime_if_nonnull @mm_decref %233
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    x86.mov eax, ecx
    x86.ret
  }
  func @advent.main() -> u32 {
  entry:
    x86.prologue stack_size=80
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-24], eax
    x86.mov edx, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.xor rdx, rdx
    x86.call StringArray.get
    x86.mov [rbp-32], eax
    x86.xor ebx, ebx
    x86.cmp edx, ebx
    x86.je advent.main.otherwise_default_success_2
  otherwise_default_error_1:
    x86.lea_rdata rax, [__str_0]
    x86.mov rcx, rax
    x86.xor edx, edx
    x86.lea_func ebx, [__destruct_String]
    x86.mov rsi, rbx
    x86.mov [rbp-72], ecx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-8], eax
    x86.lea_func edi, [__destruct___ManagedMemory]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-40], eax
    x86.mov r9, [rbp-40]
    x86.mov eax, [rbp-72]
    x86.mov [r9+0], eax
    x86.mov eax, [rbp-40]
    x86.xor ecx, ecx
    x86.mov [eax+8], ecx
    x86.xor eax, eax
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+16], eax
    x86.mov eax, 1
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+24], eax
    x86.mov eax, [rbp-40]
    x86.mov ecx, [rbp-8]
    x86.mov [ecx+0], eax
    x86.mov eax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.xor eax, eax
    x86.mov ecx, [rbp-8]
    x86.mov [ecx+8], eax
    x86.mov eax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_incref
    x86.mov eax, [rbp-8]
    x86.mov [rbp-16], eax
    x86.mov eax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_success_2:
    x86.mov eax, [rbp-16]
    x86.test eax, eax
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov ecx, [rbp-32]
    x86.mov [rbp-16], ecx
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call stdlib.Parsing.__int_fromString
    x86.xor ecx, ecx
    x86.mov [rbp-48], ecx
    x86.mov [rbp-56], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-48]
    x86.mov [rbp-56], eax
    x86.jmp advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, [rbp-56]
    x86.mov ecx, 1000
    x86.cmp eax, ecx
    x86.jle advent.main.guard_8.after
  guard_8:
    x86.mov eax, [rbp-24]
    x86.test eax, eax
    x86.jz __nonnull_skip_1
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_1
    x86.mov ecx, [rbp-8]
    x86.test ecx, ecx
    x86.jz __nonnull_skip_2
    x86.call mm_decref
    x86.label __nonnull_skip_2
    x86.mov edx, [rbp-16]
    x86.test edx, edx
    x86.jz __nonnull_skip_3
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_3
    x86.mov eax, 99
    x86.epilogue
    x86.ret
  guard_8.after:
    x86.mov rcx, 3
    x86.call advent.multiply
    x86.mov [rbp-64], eax
    x86.xor ecx, ecx
    x86.cmp eax, ecx
    x86.setl ecx
    x86.movzx ecx, ecxb
    x86.mov rdx, 4294967295
    x86.cmp rax, rdx
    x86.setg eax
    x86.movzx eax, eaxb
    x86.or ecx, eax
    x86.test ecx, ecx
    x86.je advent.main.__range_ok_9
  __range_panic_9:
    x86.lea_symdata rax, [__panic_msg_31]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_9:
    x86.mov eax, [rbp-64]
    x86.mov ecx, [rbp-24]
    x86.test ecx, ecx
    x86.jz __nonnull_skip_4
    x86.call mm_decref
    x86.label __nonnull_skip_4
    x86.mov eax, [rbp-8]
    x86.test eax, eax
    x86.jz __nonnull_skip_5
    x86.mov rcx, [rbp-8]
    x86.call mm_decref
    x86.label __nonnull_skip_5
    x86.mov ecx, [rbp-16]
    x86.test ecx, ecx
    x86.jz __nonnull_skip_6
    x86.call mm_decref
    x86.label __nonnull_skip_6
    x86.mov eax, [rbp-64]
    x86.epilogue
    x86.ret
  }
  func @__destruct_ElementMemory(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_managed_elements
    x86.mov ecx, [rbp-8]
    x86.mov edx, [ecx+16]
    x86.xor ebx, ebx
    x86.cmp edx, ebx
    x86.je __destruct_ElementMemory.skip_buf_0
  free_buf_0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+0]
    x86.call mm_raw_free
    x86.jmp __destruct_ElementMemory.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct_ElementMemory.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+8]
    x86.mov [rbp-16], ecx
    x86.test ecx, ecx
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
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+16]
    x86.xor edx, edx
    x86.cmp ecx, edx
    x86.je __destruct___ManagedMemory.skip_buf_0
  free_buf_0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+0]
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
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-16], ecx
    x86.test ecx, ecx
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
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-16], ecx
    x86.test ecx, ecx
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
```RequiredMLIR
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
    %52 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %52
    %54 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %54
    %56 = memref.load __try_result_0 : i64
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
    %68 = memref.load args : i64
    std.call_runtime_if_nonnull @mm_decref %68
    %70 = memref.load __lit_tmp_9 : i64
    std.call_runtime_if_nonnull @mm_decref %70
    %72 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %72
    func.return %67
  }
  func @__destruct_ElementMemory(ptr: i64) {
  entry:
    %207 = func.param ptr : StdI64
    memref.store %207, __destr_ptr
    %208 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %208
    %211 = memref.load __destr_ptr : i64
    %212 = memref.load_indirect %211+16
    %213 = arith.constant {value = 0 : i64}
    %214 = arith.cmpi ne %212, %213
    cf.cond_br %214 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %215 = memref.load __destr_ptr : i64
    %216 = memref.load_indirect %215+0
    std.call_runtime @mm_raw_free %216
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    %217 = func.param ptr : StdI64
    memref.store %217, __destr_ptr
    %218 = memref.load __destr_ptr : i64
    %219 = memref.load_indirect %218+8
    std.call_runtime_if_nonnull @mm_decref %219
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %220 = func.param ptr : StdI64
    memref.store %220, __destr_ptr
    %223 = memref.load __destr_ptr : i64
    %224 = memref.load_indirect %223+16
    %225 = arith.constant {value = 0 : i64}
    %226 = arith.cmpi ne %224, %225
    cf.cond_br %226 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %227 = memref.load __destr_ptr : i64
    %228 = memref.load_indirect %227+0
    std.call_runtime @mm_raw_free %228
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_String(ptr: i64) {
  entry:
    %229 = func.param ptr : StdI64
    memref.store %229, __destr_ptr
    %230 = memref.load __destr_ptr : i64
    %231 = memref.load_indirect %230+0
    std.call_runtime_if_nonnull @mm_decref %231
    cf.br done
  done:
    func.return
  }
  func @__destruct_CodepointView(ptr: i64) {
  entry:
    %232 = func.param ptr : StdI64
    memref.store %232, __destr_ptr
    %233 = memref.load __destr_ptr : i64
    %234 = memref.load_indirect %233+0
    std.call_runtime_if_nonnull @mm_decref %234
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    x86.mov eax, 2
    x86.imul ecx, eax
    x86.mov eax, ecx
    x86.ret
  }
  func @advent.main() -> u32 {
  entry:
    x86.prologue stack_size=80
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-24], eax
    x86.mov edx, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.xor rdx, rdx
    x86.call StringArray.get
    x86.mov [rbp-32], eax
    x86.xor ebx, ebx
    x86.cmp edx, ebx
    x86.je advent.main.otherwise_default_success_2
  otherwise_default_error_1:
    x86.lea_rdata rax, [__str_0]
    x86.mov rcx, rax
    x86.xor edx, edx
    x86.lea_func ebx, [__destruct_String]
    x86.mov rsi, rbx
    x86.mov [rbp-72], ecx
    x86.mov rdx, rsi
    x86.mov rcx, 16
    x86.mov r8, 4
    x86.call mm_alloc
    x86.mov [rbp-8], eax
    x86.lea_func edi, [__destruct___ManagedMemory]
    x86.mov r8, rdi
    x86.mov rdx, r8
    x86.mov rcx, 32
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-40], eax
    x86.mov r9, [rbp-40]
    x86.mov eax, [rbp-72]
    x86.mov [r9+0], eax
    x86.mov eax, [rbp-40]
    x86.xor ecx, ecx
    x86.mov [eax+8], ecx
    x86.xor eax, eax
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+16], eax
    x86.mov eax, 1
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+24], eax
    x86.mov eax, [rbp-40]
    x86.mov ecx, [rbp-8]
    x86.mov [ecx+0], eax
    x86.mov eax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.xor eax, eax
    x86.mov ecx, [rbp-8]
    x86.mov [ecx+8], eax
    x86.mov eax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_incref
    x86.mov eax, [rbp-8]
    x86.mov [rbp-16], eax
    x86.mov eax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_success_2:
    x86.mov eax, [rbp-16]
    x86.test eax, eax
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov ecx, [rbp-32]
    x86.mov [rbp-16], ecx
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call stdlib.Parsing.__int_fromString
    x86.xor ecx, ecx
    x86.mov [rbp-48], ecx
    x86.mov [rbp-56], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-48]
    x86.mov [rbp-56], eax
    x86.jmp advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, [rbp-56]
    x86.mov ecx, 1000
    x86.cmp eax, ecx
    x86.jle advent.main.guard_8.after
  guard_8:
    x86.mov eax, [rbp-24]
    x86.test eax, eax
    x86.jz __nonnull_skip_1
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_1
    x86.mov ecx, [rbp-8]
    x86.test ecx, ecx
    x86.jz __nonnull_skip_2
    x86.call mm_decref
    x86.label __nonnull_skip_2
    x86.mov edx, [rbp-16]
    x86.test edx, edx
    x86.jz __nonnull_skip_3
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_3
    x86.mov eax, 99
    x86.epilogue
    x86.ret
  guard_8.after:
    x86.mov rcx, 3
    x86.call advent.multiply
    x86.mov [rbp-64], eax
    x86.xor ecx, ecx
    x86.cmp eax, ecx
    x86.setl ecx
    x86.movzx ecx, ecxb
    x86.mov rdx, 4294967295
    x86.cmp rax, rdx
    x86.setg eax
    x86.movzx eax, eaxb
    x86.or ecx, eax
    x86.test ecx, ecx
    x86.je advent.main.__range_ok_9
  __range_panic_9:
    x86.lea_symdata rax, [__panic_msg_31]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_9:
    x86.mov eax, [rbp-64]
    x86.mov ecx, [rbp-24]
    x86.test ecx, ecx
    x86.jz __nonnull_skip_4
    x86.call mm_decref
    x86.label __nonnull_skip_4
    x86.mov eax, [rbp-8]
    x86.test eax, eax
    x86.jz __nonnull_skip_5
    x86.mov rcx, [rbp-8]
    x86.call mm_decref
    x86.label __nonnull_skip_5
    x86.mov ecx, [rbp-16]
    x86.test ecx, ecx
    x86.jz __nonnull_skip_6
    x86.call mm_decref
    x86.label __nonnull_skip_6
    x86.mov eax, [rbp-64]
    x86.epilogue
    x86.ret
  }
  func @__destruct_ElementMemory(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_managed_elements
    x86.mov ecx, [rbp-8]
    x86.mov edx, [ecx+16]
    x86.xor ebx, ebx
    x86.cmp edx, ebx
    x86.je __destruct_ElementMemory.skip_buf_0
  free_buf_0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+0]
    x86.call mm_raw_free
    x86.jmp __destruct_ElementMemory.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct_ElementMemory.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_StringArray(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+8]
    x86.mov [rbp-16], ecx
    x86.test ecx, ecx
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
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+16]
    x86.xor edx, edx
    x86.cmp ecx, edx
    x86.je __destruct___ManagedMemory.skip_buf_0
  free_buf_0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+0]
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
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-16], ecx
    x86.test ecx, ecx
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
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-16], ecx
    x86.test ecx, ecx
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
