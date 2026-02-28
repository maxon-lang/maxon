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
    __scope_0 = maxon.scope_enter {tag = advent.main}
    %1 = maxon.literal {value = 0 : i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %1
  }
}
=== standard
module {
  func @advent.main() -> u32 {
  entry:
    %1 = arith.constant {value = 0 : i64}
    func.return %1
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
    __scope_0 = maxon.scope_enter {tag = advent.add}
    %1 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %2 = maxon.param {index = 1 : i32} {name = y} {type = i64}
    %3 = maxon.binop %1, %2 {op = add} {optimalType = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %3
  }
  func @advent.main() -> i64 {
  entry:
    __scope_4 = maxon.scope_enter {tag = advent.main}
    %5 = maxon.literal {value = 3 : i64}
    %6 = maxon.literal {value = 4 : i64}
    %7 = maxon.call @advent.add %5, %6
    maxon.assign %7 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at day2.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %14 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %14
  }
}
=== standard
module {
  func @advent.add(x: i64, y: i64) -> i64 {
  entry:
    %1 = func.param x : StdI64
    %2 = func.param y : StdI64
    %3 = arith.addi %1, %2
    func.return %3
  }
  func @advent.main() -> u32 {
  entry:
    %5 = arith.constant {value = 3 : i64}
    %6 = arith.constant {value = 4 : i64}
    %7 = func.call @advent.add %5, %6
    memref.store %7, __range_val_0
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.cmpi lt %7, %8
    %10 = arith.constant {value = 4294967295 : i64}
    %11 = arith.cmpi gt %7, %10
    %12 = arith.ori1 %9, %11
    cf.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %13 = memref.lea_symdata __panic_msg_13
    %14 = std.ptr_to_i64 %13
    std.call_runtime @maxon_panic %14
  __range_ok_0:
    %15 = memref.load __range_val_0 : i64
    func.return %15
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
    x86.lea_symdata rax, [__panic_msg_13]
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
    __scope_0 = maxon.scope_enter {tag = advent.multiply}
    %1 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %2 = maxon.literal {value = 1 : i64}
    %3 = maxon.binop %1, %2 {op = mul} {optimalType = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %3
  }
  func @advent.main() -> i64 {
  entry:
    __scope_4 = maxon.scope_enter {tag = advent.main}
    %5 = maxon.call @stdlib.CommandLine.args
    maxon.assign %5 {var = __call_tmp_5} {decl = 1 : i1}
    maxon.assign %5 {var = args} {decl = 1 : i1}
    %6 = maxon.struct_var_ref args
    %7 = maxon.literal {value = 0 : i64}
    %10, %9 = maxon.try_call @StringArray.get %6, %7
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %9, %12 {op = ne}
    maxon.cond_br %13 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %11 = maxon.string_literal ""
    maxon.assign %11 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_success_2:
    maxon.assign %10 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %14 = maxon.struct_var_ref __try_result_0
    %17, %16 = maxon.try_call @stdlib.Parsing.__int_fromString %14
    %18 = maxon.literal {value = 0 : i64}
    maxon.assign %18 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %17 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %19 = maxon.literal {value = 0 : i64}
    %20 = maxon.binop %16, %19 {op = ne}
    maxon.cond_br %20 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %21 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %21 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %22 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %22 {var = parsed} {kind = i64} {decl = 1 : i1}
    %23 = maxon.literal {value = 1000 : i64}
    %24 = maxon.binop %22, %23 {op = gt}
    maxon.cond_br %24 [then: guard_8, else: guard_8.after]
  guard_8:
    __scope_25 = maxon.scope_enter {tag = if_then}
    %26 = maxon.literal {value = 99 : i64}
    maxon.scope_exit {scope = __scope_25} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %26
  guard_8.after:
    %27 = maxon.literal {value = 3 : i64}
    %28 = maxon.call @advent.multiply %27
    maxon.assign %28 {var = __range_val_9} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %29 = maxon.literal {value = 0 : i64}
    %30 = maxon.binop %28, %29 {op = lt}
    %31 = maxon.literal {value = 4294967295 : i64}
    %32 = maxon.binop %28, %31 {op = gt}
    %33 = maxon.binop %30, %32 {op = or}
    maxon.cond_br %33 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    maxon.panic "panic at day4a.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_9:
    %35 = maxon.var_ref {var = __range_val_9} {type = i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %35
  }
}
=== standard
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %1 = func.param x : StdI64
    func.return %1
  }
  func @advent.main() -> u32 {
  entry:
    %3 = arith.constant {value = 0 : i64}
    %4 = std.call_runtime @mm_scope_enter %3
    memref.store %4, __scope_4
    %5 = func.call @stdlib.CommandLine.args
    memref.store %5, args
    %8 = arith.constant {value = 0 : i64}
    %9 = memref.load args : i64
    %10, %11 = func.try_call @StringArray.get %9, %8
    memref.store %10, __callret_10
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.cmpi ne %11, %12
    cf.cond_br %13 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %14 = memref.lea_rdata __str_0
    %15 = std.ptr_to_i64 %14
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.constant {value = 16 : i64}
    %18 = arith.constant {value = 0 : i64}
    %19 = std.call_runtime @mm_alloc %17, %18
    memref.store %19, __strtmp_11
    %20 = arith.constant {value = 32 : i64}
    %21 = arith.constant {value = 0 : i64}
    %22 = std.call_runtime @mm_alloc_in %20, %19, %21
    memref.store %22, __strtmp_managed_11
    %23 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %15, %23+0
    %24 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %16, %24+8
    %25 = arith.constant {value = 0 : i64}
    %26 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %25, %26+16
    %27 = arith.constant {value = 1 : i64}
    %28 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %27, %28+24
    %29 = memref.load __strtmp_managed_11 : i64
    %30 = memref.load __strtmp_11 : i64
    memref.store_indirect %29, %30+0
    %31 = arith.constant {value = 0 : i64}
    %32 = memref.load __strtmp_11 : i64
    memref.store_indirect %31, %32+8
    memref.store %19, __try_result_0
    %34 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %34
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %35 = memref.load __callret_10 : i64
    memref.store %35, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %36 = memref.load __try_result_0 : i64
    %37, %38 = func.try_call @stdlib.Parsing.__int_fromString %36
    %39 = arith.constant {value = 0 : i64}
    memref.store %39, __try_default_5
    memref.store %37, __try_result_4
    %40 = arith.constant {value = 0 : i64}
    %41 = arith.cmpi ne %38, %40
    cf.cond_br %41 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %42 = memref.load __try_default_5 : i64
    memref.store %42, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %43 = memref.load __try_result_4 : i64
    %44 = arith.constant {value = 1000 : i64}
    %45 = arith.cmpi gt %43, %44
    cf.cond_br %45 [then: guard_8, else: guard_8.after]
  guard_8:
    %47 = arith.constant {value = 99 : i64}
    %48 = memref.load args : i64
    std.call_runtime @mm_decref %48
    %49 = memref.load __try_result_0 : i64
    std.call_runtime @mm_decref %49
    %50 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %50
    func.return %47
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
    %58 = memref.lea_symdata __panic_msg_34
    %59 = std.ptr_to_i64 %58
    std.call_runtime @maxon_panic %59
  __range_ok_9:
    %60 = memref.load __range_val_9 : i64
    %61 = memref.load args : i64
    std.call_runtime @mm_decref %61
    %62 = memref.load __try_result_0 : i64
    std.call_runtime @mm_decref %62
    %63 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %63
    func.return %60
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
    x86.xor rcx, rcx
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-16], eax
    x86.mov eax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.xor rdx, rdx
    x86.call StringArray.get
    x86.mov [rbp-24], eax
    x86.xor ecx, ecx
    x86.cmp edx, ecx
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
    x86.mov [rbp-48], eax
    x86.mov eax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
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
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.mov ecx, [rbp-48]
    x86.call mm_decref
    x86.mov edx, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_scope_exit
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
    x86.lea_symdata rax, [__panic_msg_34]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_9:
    x86.mov eax, [rbp-72]
    x86.mov ecx, [rbp-16]
    x86.call mm_decref
    x86.mov eax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call mm_decref
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, [rbp-72]
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
    __scope_0 = maxon.scope_enter {tag = advent.multiply}
    %1 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %2 = maxon.literal {value = 2 : i64}
    %3 = maxon.binop %1, %2 {op = mul} {optimalType = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %3
  }
  func @advent.main() -> i64 {
  entry:
    __scope_4 = maxon.scope_enter {tag = advent.main}
    %5 = maxon.call @stdlib.CommandLine.args
    maxon.assign %5 {var = __call_tmp_5} {decl = 1 : i1}
    maxon.assign %5 {var = args} {decl = 1 : i1}
    %6 = maxon.struct_var_ref args
    %7 = maxon.literal {value = 0 : i64}
    %10, %9 = maxon.try_call @StringArray.get %6, %7
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %9, %12 {op = ne}
    maxon.cond_br %13 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %11 = maxon.string_literal ""
    maxon.assign %11 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_success_2:
    maxon.assign %10 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %14 = maxon.struct_var_ref __try_result_0
    %17, %16 = maxon.try_call @stdlib.Parsing.__int_fromString %14
    %18 = maxon.literal {value = 0 : i64}
    maxon.assign %18 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %17 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %19 = maxon.literal {value = 0 : i64}
    %20 = maxon.binop %16, %19 {op = ne}
    maxon.cond_br %20 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %21 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %21 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %22 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %22 {var = parsed} {kind = i64} {decl = 1 : i1}
    %23 = maxon.literal {value = 1000 : i64}
    %24 = maxon.binop %22, %23 {op = gt}
    maxon.cond_br %24 [then: guard_8, else: guard_8.after]
  guard_8:
    __scope_25 = maxon.scope_enter {tag = if_then}
    %26 = maxon.literal {value = 99 : i64}
    maxon.scope_exit {scope = __scope_25} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %26
  guard_8.after:
    %27 = maxon.literal {value = 3 : i64}
    %28 = maxon.call @advent.multiply %27
    maxon.assign %28 {var = __range_val_9} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %29 = maxon.literal {value = 0 : i64}
    %30 = maxon.binop %28, %29 {op = lt}
    %31 = maxon.literal {value = 4294967295 : i64}
    %32 = maxon.binop %28, %31 {op = gt}
    %33 = maxon.binop %30, %32 {op = or}
    maxon.cond_br %33 [then: __range_panic_9, else: __range_ok_9]
  __range_panic_9:
    maxon.panic "panic at day4b.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_9:
    %35 = maxon.var_ref {var = __range_val_9} {type = i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %35
  }
}
=== standard
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %1 = func.param x : StdI64
    %2 = arith.constant {value = 2 : i64}
    %3 = arith.muli %1, %2
    func.return %3
  }
  func @advent.main() -> u32 {
  entry:
    %4 = arith.constant {value = 0 : i64}
    %5 = std.call_runtime @mm_scope_enter %4
    memref.store %5, __scope_4
    %6 = func.call @stdlib.CommandLine.args
    memref.store %6, args
    %9 = arith.constant {value = 0 : i64}
    %10 = memref.load args : i64
    %11, %12 = func.try_call @StringArray.get %10, %9
    memref.store %11, __callret_10
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.cmpi ne %12, %13
    cf.cond_br %14 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %15 = memref.lea_rdata __str_0
    %16 = std.ptr_to_i64 %15
    %17 = arith.constant {value = 0 : i64}
    %18 = arith.constant {value = 16 : i64}
    %19 = arith.constant {value = 0 : i64}
    %20 = std.call_runtime @mm_alloc %18, %19
    memref.store %20, __strtmp_11
    %21 = arith.constant {value = 32 : i64}
    %22 = arith.constant {value = 0 : i64}
    %23 = std.call_runtime @mm_alloc_in %21, %20, %22
    memref.store %23, __strtmp_managed_11
    %24 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %16, %24+0
    %25 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %17, %25+8
    %26 = arith.constant {value = 0 : i64}
    %27 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %26, %27+16
    %28 = arith.constant {value = 1 : i64}
    %29 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %28, %29+24
    %30 = memref.load __strtmp_managed_11 : i64
    %31 = memref.load __strtmp_11 : i64
    memref.store_indirect %30, %31+0
    %32 = arith.constant {value = 0 : i64}
    %33 = memref.load __strtmp_11 : i64
    memref.store_indirect %32, %33+8
    memref.store %20, __try_result_0
    %35 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %35
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %36 = memref.load __callret_10 : i64
    memref.store %36, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %37 = memref.load __try_result_0 : i64
    %38, %39 = func.try_call @stdlib.Parsing.__int_fromString %37
    %40 = arith.constant {value = 0 : i64}
    memref.store %40, __try_default_5
    memref.store %38, __try_result_4
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.cmpi ne %39, %41
    cf.cond_br %42 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %43 = memref.load __try_default_5 : i64
    memref.store %43, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %44 = memref.load __try_result_4 : i64
    %45 = arith.constant {value = 1000 : i64}
    %46 = arith.cmpi gt %44, %45
    cf.cond_br %46 [then: guard_8, else: guard_8.after]
  guard_8:
    %48 = arith.constant {value = 99 : i64}
    %49 = memref.load args : i64
    std.call_runtime @mm_decref %49
    %50 = memref.load __try_result_0 : i64
    std.call_runtime @mm_decref %50
    %51 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %51
    func.return %48
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
    %59 = memref.lea_symdata __panic_msg_34
    %60 = std.ptr_to_i64 %59
    std.call_runtime @maxon_panic %60
  __range_ok_9:
    %61 = memref.load __range_val_9 : i64
    %62 = memref.load args : i64
    std.call_runtime @mm_decref %62
    %63 = memref.load __try_result_0 : i64
    std.call_runtime @mm_decref %63
    %64 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %64
    func.return %61
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
    x86.xor rcx, rcx
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-16], eax
    x86.mov eax, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.xor rdx, rdx
    x86.call StringArray.get
    x86.mov [rbp-24], eax
    x86.xor ecx, ecx
    x86.cmp edx, ecx
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
    x86.mov [rbp-48], eax
    x86.mov eax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
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
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.mov ecx, [rbp-48]
    x86.call mm_decref
    x86.mov edx, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_scope_exit
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
    x86.lea_symdata rax, [__panic_msg_34]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_9:
    x86.mov eax, [rbp-72]
    x86.mov ecx, [rbp-16]
    x86.call mm_decref
    x86.mov eax, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call mm_decref
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, [rbp-72]
    x86.epilogue
    x86.ret
  }
}
```
