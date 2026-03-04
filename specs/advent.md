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
    %68 = arith.constant {value = 0 : i64}
    memref.store %68, __lit_tmp_9
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
    %15 = arith.constant {value = 0 : i64}
    %16 = std.call_runtime @mm_alloc %14, %15
    memref.store %16, __strtmp_9
    %17 = arith.constant {value = 32 : i64}
    %18 = arith.constant {value = 0 : i64}
    %19 = std.call_runtime @mm_alloc_in %17, %16, %18
    memref.store %19, __strtmp_managed_9
    %20 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %12, %20+0
    %21 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %13, %21+8
    %22 = arith.constant {value = 0 : i64}
    %23 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %22, %23+16
    %24 = arith.constant {value = 1 : i64}
    %25 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %24, %25+24
    %26 = memref.load __strtmp_managed_9 : i64
    %27 = memref.load __strtmp_9 : i64
    memref.store_indirect %26, %27+0
    %28 = arith.constant {value = 0 : i64}
    %29 = memref.load __strtmp_9 : i64
    memref.store_indirect %28, %29+8
    memref.store %16, __lit_tmp_9
    %31 = memref.load __lit_tmp_9 : i64
    std.call_runtime @mm_incref %31
    memref.store %16, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %33 = memref.load __callret_8 : i64
    memref.store %33, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %34 = memref.load __try_result_0 : i64
    %35, %36 = func.try_call @stdlib.Parsing.__int_fromString %34
    %37 = arith.constant {value = 0 : i64}
    memref.store %37, __try_default_5
    memref.store %35, __try_result_4
    %38 = arith.constant {value = 0 : i64}
    %39 = arith.cmpi ne %36, %38
    cf.cond_br %39 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %40 = memref.load __try_default_5 : i64
    memref.store %40, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %41 = memref.load __try_result_4 : i64
    %42 = arith.constant {value = 1000 : i64}
    %43 = arith.cmpi gt %41, %42
    cf.cond_br %43 [then: guard_8, else: guard_8.after]
  guard_8:
    %44 = arith.constant {value = 99 : i64}
    %45 = memref.load args : i64
    mm.destruct_struct %45 fields=[+8] null_guarded
    %47 = memref.load __lit_tmp_9 : i64
    mm.destruct_struct %47 fields=[+0] null_guarded
    %49 = memref.load __try_result_0 : i64
    mm.destruct_struct %49 fields=[+0] null_guarded
    func.return %44
  guard_8.after:
    %51 = arith.constant {value = 3 : i64}
    %52 = func.call @advent.multiply %51
    memref.store %52, __range_val_9
    %53 = arith.constant {value = 0 : i64}
    %54 = arith.cmpi lt %52, %53
    %55 = arith.constant {value = 4294967295 : i64}
    %56 = arith.cmpi gt %52, %55
    %57 = arith.ori1 %54, %56
    cf.cond_br %57 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    %58 = memref.lea_symdata __panic_msg_31
    %59 = std.ptr_to_i64 %58
    std.call_runtime @maxon_panic %59
  __range_ok_9:
    %60 = memref.load __range_val_9 : i64
    %61 = memref.load args : i64
    mm.destruct_struct %61 fields=[+8] null_guarded
    %63 = memref.load __lit_tmp_9 : i64
    mm.destruct_struct %63 fields=[+0] null_guarded
    %65 = memref.load __try_result_0 : i64
    mm.destruct_struct %65 fields=[+0] null_guarded
    func.return %60
  }
  func @__destruct_elements_String(managed_ptr: i64) {
  entry:
    %201 = func.param managed_ptr : StdI64
    %202 = memref.load_indirect %201+0
    %203 = memref.load_indirect %201+8
    %204 = arith.constant {value = 0 : i64}
    memref.store %204, __destr_index
    memref.store %202, __destr_buffer
    memref.store %203, __destr_length
    cf.br loop_header
  loop_header:
    %205 = memref.load __destr_index : i64
    %206 = memref.load __destr_length : i64
    %207 = arith.cmpui ult %205, %206
    cf.cond_br %207 [then: loop_body, else: done]
  loop_body:
    %208 = memref.load __destr_index : i64
    %209 = arith.constant {value = 8 : i64}
    %210 = arith.muli %208, %209
    %211 = memref.load __destr_buffer : i64
    %212 = arith.addi %211, %210
    %213 = memref.load_indirect %212+0
    memref.store %213, __destr_elem
    %214 = memref.load __destr_elem : i64
    mm.destruct_struct %214 fields=[+0] null_guarded
    %215 = memref.load __destr_index : i64
    %216 = arith.constant {value = 1 : i64}
    %217 = arith.addi %215, %216
    memref.store %217, __destr_index
    cf.br loop_header
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
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-16], eax
    x86.mov ecx, [rbp-16]
    x86.xor rdx, rdx
    x86.call StringArray.get
    x86.mov [rbp-24], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_success_2
  otherwise_default_error_1:
    x86.lea_rdata rax, [__str_0]
    x86.mov rcx, rax
    x86.xor edx, edx
    x86.mov [rbp-80], ecx
    x86.mov rcx, 16
    x86.xor rdx, rdx
    x86.call mm_alloc
    x86.mov [rbp-32], eax
    x86.mov rdx, [rbp-32]
    x86.mov rcx, 32
    x86.xor r8, r8
    x86.call mm_alloc_in
    x86.mov [rbp-40], eax
    x86.mov ebx, [rbp-40]
    x86.mov esi, [rbp-80]
    x86.mov [ebx+0], esi
    x86.mov edi, [rbp-40]
    x86.xor r8, r8
    x86.mov [edi+8], r8
    x86.xor r9, r9
    x86.mov eax, [rbp-40]
    x86.mov [eax+16], r9
    x86.mov eax, 1
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+24], eax
    x86.mov eax, [rbp-40]
    x86.mov ecx, [rbp-32]
    x86.mov [ecx+0], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-32]
    x86.mov [ecx+8], eax
    x86.mov eax, [rbp-32]
    x86.mov [rbp-8], eax
    x86.mov ecx, [rbp-8]
    x86.call mm_incref
    x86.mov eax, [rbp-8]
    x86.mov [rbp-48], eax
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_success_2:
    x86.mov eax, [rbp-24]
    x86.mov [rbp-48], eax
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call stdlib.Parsing.__int_fromString
    x86.xor ecx, ecx
    x86.mov [rbp-56], ecx
    x86.mov [rbp-64], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-56]
    x86.mov [rbp-64], eax
    x86.jmp advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, [rbp-64]
    x86.mov ecx, 1000
    x86.cmp eax, ecx
    x86.jle advent.main.guard_8.after
  guard_8:
    x86.mov eax, [rbp-16]
    x86.test eax, eax
    x86.jz __destruct_nullguard_0
    x86.mov rcx, [rbp-16]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_1
    x86.mov ecx, [rbp-16]
    x86.mov edx, [ecx+8]
    x86.mov [rbp-80], edx
    x86.mov rcx, [rbp-80]
    x86.call __destruct_elements_String
    x86.mov rcx, [rbp-80]
    x86.call mm_decref
    x86.mov rcx, [rbp-16]
    x86.call mm_free
    x86.label __destruct_skip_1
    x86.label __destruct_nullguard_0
    x86.mov ebx, [rbp-8]
    x86.test ebx, ebx
    x86.jz __destruct_nullguard_2
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_3
    x86.mov esi, [rbp-8]
    x86.mov edi, [esi+0]
    x86.mov rcx, rdi
    x86.call mm_decref_if_owned
    x86.mov rcx, [rbp-8]
    x86.call mm_free
    x86.label __destruct_skip_3
    x86.label __destruct_nullguard_2
    x86.mov r8, [rbp-48]
    x86.test r8, r8
    x86.jz __destruct_nullguard_4
    x86.mov rcx, [rbp-48]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_5
    x86.mov r9, [rbp-48]
    x86.mov ecx, [r9+0]
    x86.call mm_decref_if_owned
    x86.mov rcx, [rbp-48]
    x86.call mm_free
    x86.label __destruct_skip_5
    x86.label __destruct_nullguard_4
    x86.mov eax, 99
    x86.epilogue
    x86.ret
  guard_8.after:
    x86.mov rcx, 3
    x86.call advent.multiply
    x86.mov [rbp-72], eax
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
    x86.mov eax, [rbp-72]
    x86.mov ecx, [rbp-16]
    x86.test ecx, ecx
    x86.jz __destruct_nullguard_6
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_7
    x86.mov ecx, [rbp-16]
    x86.mov edx, [ecx+8]
    x86.mov [rbp-80], edx
    x86.mov rcx, [rbp-80]
    x86.call __destruct_elements_String
    x86.mov rcx, [rbp-80]
    x86.call mm_decref
    x86.mov rcx, [rbp-16]
    x86.call mm_free
    x86.label __destruct_skip_7
    x86.label __destruct_nullguard_6
    x86.mov eax, [rbp-8]
    x86.test eax, eax
    x86.jz __destruct_nullguard_8
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_9
    x86.mov ecx, [rbp-8]
    x86.mov edx, [ecx+0]
    x86.mov rcx, rdx
    x86.call mm_decref_if_owned
    x86.mov rcx, [rbp-8]
    x86.call mm_free
    x86.label __destruct_skip_9
    x86.label __destruct_nullguard_8
    x86.mov ebx, [rbp-48]
    x86.test ebx, ebx
    x86.jz __destruct_nullguard_10
    x86.mov rcx, [rbp-48]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_11
    x86.mov esi, [rbp-48]
    x86.mov edi, [esi+0]
    x86.mov rcx, rdi
    x86.call mm_decref_if_owned
    x86.mov rcx, [rbp-48]
    x86.call mm_free
    x86.label __destruct_skip_11
    x86.label __destruct_nullguard_10
    x86.mov eax, [rbp-72]
    x86.epilogue
    x86.ret
  }
  func @__destruct_elements_String(managed_ptr: i64) {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, [ecx+0]
    x86.mov edx, [ecx+8]
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], eax
    x86.mov [rbp-24], edx
    x86.jmp __destruct_elements_String.loop_header
  loop_header:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-24]
    x86.cmp eax, ecx
    x86.jae __destruct_elements_String.done
  loop_body:
    x86.mov eax, [rbp-8]
    x86.mov ecx, 8
    x86.imul eax, ecx
    x86.mov edx, [rbp-16]
    x86.add edx, eax
    x86.mov ebx, [edx+0]
    x86.mov [rbp-32], ebx
    x86.mov esi, [rbp-32]
    x86.test esi, esi
    x86.jz __destruct_nullguard_13
    x86.mov rcx, [rbp-32]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_14
    x86.mov edi, [rbp-32]
    x86.mov r8, [edi+0]
    x86.mov rcx, r8
    x86.call mm_decref_if_owned
    x86.mov rcx, [rbp-32]
    x86.call mm_free
    x86.label __destruct_skip_14
    x86.label __destruct_nullguard_13
    x86.mov r9, [rbp-8]
    x86.mov eax, 1
    x86.add r9, eax
    x86.mov [rbp-8], r9
    x86.jmp __destruct_elements_String.loop_header
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
    %69 = arith.constant {value = 0 : i64}
    memref.store %69, __lit_tmp_9
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
    %16 = arith.constant {value = 0 : i64}
    %17 = std.call_runtime @mm_alloc %15, %16
    memref.store %17, __strtmp_9
    %18 = arith.constant {value = 32 : i64}
    %19 = arith.constant {value = 0 : i64}
    %20 = std.call_runtime @mm_alloc_in %18, %17, %19
    memref.store %20, __strtmp_managed_9
    %21 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %13, %21+0
    %22 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %14, %22+8
    %23 = arith.constant {value = 0 : i64}
    %24 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %23, %24+16
    %25 = arith.constant {value = 1 : i64}
    %26 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %25, %26+24
    %27 = memref.load __strtmp_managed_9 : i64
    %28 = memref.load __strtmp_9 : i64
    memref.store_indirect %27, %28+0
    %29 = arith.constant {value = 0 : i64}
    %30 = memref.load __strtmp_9 : i64
    memref.store_indirect %29, %30+8
    memref.store %17, __lit_tmp_9
    %32 = memref.load __lit_tmp_9 : i64
    std.call_runtime @mm_incref %32
    memref.store %17, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %34 = memref.load __callret_8 : i64
    memref.store %34, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %35 = memref.load __try_result_0 : i64
    %36, %37 = func.try_call @stdlib.Parsing.__int_fromString %35
    %38 = arith.constant {value = 0 : i64}
    memref.store %38, __try_default_5
    memref.store %36, __try_result_4
    %39 = arith.constant {value = 0 : i64}
    %40 = arith.cmpi ne %37, %39
    cf.cond_br %40 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %41 = memref.load __try_default_5 : i64
    memref.store %41, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %42 = memref.load __try_result_4 : i64
    %43 = arith.constant {value = 1000 : i64}
    %44 = arith.cmpi gt %42, %43
    cf.cond_br %44 [then: guard_8, else: guard_8.after]
  guard_8:
    %45 = arith.constant {value = 99 : i64}
    %46 = memref.load args : i64
    mm.destruct_struct %46 fields=[+8] null_guarded
    %48 = memref.load __lit_tmp_9 : i64
    mm.destruct_struct %48 fields=[+0] null_guarded
    %50 = memref.load __try_result_0 : i64
    mm.destruct_struct %50 fields=[+0] null_guarded
    func.return %45
  guard_8.after:
    %52 = arith.constant {value = 3 : i64}
    %53 = func.call @advent.multiply %52
    memref.store %53, __range_val_9
    %54 = arith.constant {value = 0 : i64}
    %55 = arith.cmpi lt %53, %54
    %56 = arith.constant {value = 4294967295 : i64}
    %57 = arith.cmpi gt %53, %56
    %58 = arith.ori1 %55, %57
    cf.cond_br %58 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    %59 = memref.lea_symdata __panic_msg_31
    %60 = std.ptr_to_i64 %59
    std.call_runtime @maxon_panic %60
  __range_ok_9:
    %61 = memref.load __range_val_9 : i64
    %62 = memref.load args : i64
    mm.destruct_struct %62 fields=[+8] null_guarded
    %64 = memref.load __lit_tmp_9 : i64
    mm.destruct_struct %64 fields=[+0] null_guarded
    %66 = memref.load __try_result_0 : i64
    mm.destruct_struct %66 fields=[+0] null_guarded
    func.return %61
  }
  func @__destruct_elements_String(managed_ptr: i64) {
  entry:
    %202 = func.param managed_ptr : StdI64
    %203 = memref.load_indirect %202+0
    %204 = memref.load_indirect %202+8
    %205 = arith.constant {value = 0 : i64}
    memref.store %205, __destr_index
    memref.store %203, __destr_buffer
    memref.store %204, __destr_length
    cf.br loop_header
  loop_header:
    %206 = memref.load __destr_index : i64
    %207 = memref.load __destr_length : i64
    %208 = arith.cmpui ult %206, %207
    cf.cond_br %208 [then: loop_body, else: done]
  loop_body:
    %209 = memref.load __destr_index : i64
    %210 = arith.constant {value = 8 : i64}
    %211 = arith.muli %209, %210
    %212 = memref.load __destr_buffer : i64
    %213 = arith.addi %212, %211
    %214 = memref.load_indirect %213+0
    memref.store %214, __destr_elem
    %215 = memref.load __destr_elem : i64
    mm.destruct_struct %215 fields=[+0] null_guarded
    %216 = memref.load __destr_index : i64
    %217 = arith.constant {value = 1 : i64}
    %218 = arith.addi %216, %217
    memref.store %218, __destr_index
    cf.br loop_header
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
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-16], eax
    x86.mov ecx, [rbp-16]
    x86.xor rdx, rdx
    x86.call StringArray.get
    x86.mov [rbp-24], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_success_2
  otherwise_default_error_1:
    x86.lea_rdata rax, [__str_0]
    x86.mov rcx, rax
    x86.xor edx, edx
    x86.mov [rbp-80], ecx
    x86.mov rcx, 16
    x86.xor rdx, rdx
    x86.call mm_alloc
    x86.mov [rbp-32], eax
    x86.mov rdx, [rbp-32]
    x86.mov rcx, 32
    x86.xor r8, r8
    x86.call mm_alloc_in
    x86.mov [rbp-40], eax
    x86.mov ebx, [rbp-40]
    x86.mov esi, [rbp-80]
    x86.mov [ebx+0], esi
    x86.mov edi, [rbp-40]
    x86.xor r8, r8
    x86.mov [edi+8], r8
    x86.xor r9, r9
    x86.mov eax, [rbp-40]
    x86.mov [eax+16], r9
    x86.mov eax, 1
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+24], eax
    x86.mov eax, [rbp-40]
    x86.mov ecx, [rbp-32]
    x86.mov [ecx+0], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-32]
    x86.mov [ecx+8], eax
    x86.mov eax, [rbp-32]
    x86.mov [rbp-8], eax
    x86.mov ecx, [rbp-8]
    x86.call mm_incref
    x86.mov eax, [rbp-8]
    x86.mov [rbp-48], eax
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_success_2:
    x86.mov eax, [rbp-24]
    x86.mov [rbp-48], eax
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call stdlib.Parsing.__int_fromString
    x86.xor ecx, ecx
    x86.mov [rbp-56], ecx
    x86.mov [rbp-64], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-56]
    x86.mov [rbp-64], eax
    x86.jmp advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, [rbp-64]
    x86.mov ecx, 1000
    x86.cmp eax, ecx
    x86.jle advent.main.guard_8.after
  guard_8:
    x86.mov eax, [rbp-16]
    x86.test eax, eax
    x86.jz __destruct_nullguard_0
    x86.mov rcx, [rbp-16]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_1
    x86.mov ecx, [rbp-16]
    x86.mov edx, [ecx+8]
    x86.mov [rbp-80], edx
    x86.mov rcx, [rbp-80]
    x86.call __destruct_elements_String
    x86.mov rcx, [rbp-80]
    x86.call mm_decref
    x86.mov rcx, [rbp-16]
    x86.call mm_free
    x86.label __destruct_skip_1
    x86.label __destruct_nullguard_0
    x86.mov ebx, [rbp-8]
    x86.test ebx, ebx
    x86.jz __destruct_nullguard_2
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_3
    x86.mov esi, [rbp-8]
    x86.mov edi, [esi+0]
    x86.mov rcx, rdi
    x86.call mm_decref_if_owned
    x86.mov rcx, [rbp-8]
    x86.call mm_free
    x86.label __destruct_skip_3
    x86.label __destruct_nullguard_2
    x86.mov r8, [rbp-48]
    x86.test r8, r8
    x86.jz __destruct_nullguard_4
    x86.mov rcx, [rbp-48]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_5
    x86.mov r9, [rbp-48]
    x86.mov ecx, [r9+0]
    x86.call mm_decref_if_owned
    x86.mov rcx, [rbp-48]
    x86.call mm_free
    x86.label __destruct_skip_5
    x86.label __destruct_nullguard_4
    x86.mov eax, 99
    x86.epilogue
    x86.ret
  guard_8.after:
    x86.mov rcx, 3
    x86.call advent.multiply
    x86.mov [rbp-72], eax
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
    x86.mov eax, [rbp-72]
    x86.mov ecx, [rbp-16]
    x86.test ecx, ecx
    x86.jz __destruct_nullguard_6
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_7
    x86.mov ecx, [rbp-16]
    x86.mov edx, [ecx+8]
    x86.mov [rbp-80], edx
    x86.mov rcx, [rbp-80]
    x86.call __destruct_elements_String
    x86.mov rcx, [rbp-80]
    x86.call mm_decref
    x86.mov rcx, [rbp-16]
    x86.call mm_free
    x86.label __destruct_skip_7
    x86.label __destruct_nullguard_6
    x86.mov eax, [rbp-8]
    x86.test eax, eax
    x86.jz __destruct_nullguard_8
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_9
    x86.mov ecx, [rbp-8]
    x86.mov edx, [ecx+0]
    x86.mov rcx, rdx
    x86.call mm_decref_if_owned
    x86.mov rcx, [rbp-8]
    x86.call mm_free
    x86.label __destruct_skip_9
    x86.label __destruct_nullguard_8
    x86.mov ebx, [rbp-48]
    x86.test ebx, ebx
    x86.jz __destruct_nullguard_10
    x86.mov rcx, [rbp-48]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_11
    x86.mov esi, [rbp-48]
    x86.mov edi, [esi+0]
    x86.mov rcx, rdi
    x86.call mm_decref_if_owned
    x86.mov rcx, [rbp-48]
    x86.call mm_free
    x86.label __destruct_skip_11
    x86.label __destruct_nullguard_10
    x86.mov eax, [rbp-72]
    x86.epilogue
    x86.ret
  }
  func @__destruct_elements_String(managed_ptr: i64) {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, [ecx+0]
    x86.mov edx, [ecx+8]
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], eax
    x86.mov [rbp-24], edx
    x86.jmp __destruct_elements_String.loop_header
  loop_header:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-24]
    x86.cmp eax, ecx
    x86.jae __destruct_elements_String.done
  loop_body:
    x86.mov eax, [rbp-8]
    x86.mov ecx, 8
    x86.imul eax, ecx
    x86.mov edx, [rbp-16]
    x86.add edx, eax
    x86.mov ebx, [edx+0]
    x86.mov [rbp-32], ebx
    x86.mov esi, [rbp-32]
    x86.test esi, esi
    x86.jz __destruct_nullguard_13
    x86.mov rcx, [rbp-32]
    x86.call mm_decref_check
    x86.test eax, eax
    x86.jnz __destruct_skip_14
    x86.mov edi, [rbp-32]
    x86.mov r8, [edi+0]
    x86.mov rcx, r8
    x86.call mm_decref_if_owned
    x86.mov rcx, [rbp-32]
    x86.call mm_free
    x86.label __destruct_skip_14
    x86.label __destruct_nullguard_13
    x86.mov r9, [rbp-8]
    x86.mov eax, 1
    x86.add r9, eax
    x86.mov [rbp-8], r9
    x86.jmp __destruct_elements_String.loop_header
  done:
    x86.epilogue
    x86.ret
  }
}
```
