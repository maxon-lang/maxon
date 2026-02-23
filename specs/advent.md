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
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 0 : i64}
    %3 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %3
    func.return %2
  }
}
=== x86
module {
  func @advent.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.xor eax, eax
    x86.epilogue
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
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param x : StdI64
    %3 = func.param y : StdI64
    %4 = arith.addi %2, %3
    %5 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %5
    func.return %4
  }
  func @advent.main() -> u32 {
  entry:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_4
    %8 = arith.constant {value = 3 : i64}
    %9 = arith.constant {value = 4 : i64}
    %10 = func.call @advent.add %8, %9
    memref.store %10, __range_val_0
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.cmpi lt %10, %11
    %13 = arith.constant {value = 4294967295 : i64}
    %14 = arith.cmpi gt %10, %13
    %15 = arith.ori1 %12, %14
    cf.cond_br %15 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %16 = memref.lea_symdata __panic_msg_13
    %17 = std.ptr_to_i64 %16
    std.call_runtime @maxon_panic %17
  __range_ok_0:
    %18 = memref.load __range_val_0 : i64
    %19 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %19
    func.return %18
  }
}
=== x86
module {
  func @advent.add(x: i64, y: i64) -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov [rbp-16], ecx
    x86.mov [rbp-24], edx
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, [rbp-24]
    x86.mov edx, [rbp-16]
    x86.add edx, ecx
    x86.mov ebx, [rbp-8]
    x86.mov [rbp-32], edx
    x86.mov rcx, rbx
    x86.call mm_scope_exit
    x86.mov eax, [rbp-32]
    x86.epilogue
    x86.ret
  }
  func @advent.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 3
    x86.mov edx, 4
    x86.call advent.add
    x86.mov [rbp-16], eax
    x86.xor ebx, ebx
    x86.cmp eax, ebx
    x86.setl esi
    x86.movzx esi, esib
    x86.mov rdi, 4294967295
    x86.cmp rax, rdi
    x86.setg r8
    x86.movzx r8, r8b
    x86.or esi, r8
    x86.test esi, esi
    x86.je advent.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_13]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-24], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-24]
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
    maxon.assign %5 {var = args} {decl = 1 : i1}
    %6 = maxon.struct_var_ref args
    %7 = maxon.literal {value = 0 : i64}
    %10, %9 = maxon.try_call @StringArray.get %6, %7
    %11 = maxon.string_literal ""
    maxon.assign %11 {var = __try_default_1} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %10 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %9, %12 {op = ne}
    maxon.cond_br %13 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %14 = maxon.struct_var_ref __try_default_1
    maxon.assign %14 {var = __try_result_0} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    maxon.release {var = __try_default_1} {type = String}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %15 = maxon.struct_var_ref __try_result_0
    %18, %17 = maxon.try_call @stdlib.Parsing.__int_fromString %15
    %19 = maxon.literal {value = 0 : i64}
    maxon.assign %19 {var = __try_default_6} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %18 {var = __try_result_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %17, %20 {op = ne}
    maxon.cond_br %21 [then: otherwise_default_error_7, else: otherwise_default_continue_8]
  otherwise_default_error_7:
    %22 = maxon.var_ref {var = __try_default_6} {type = i64}
    maxon.assign %22 {var = __try_result_5} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_8
  otherwise_default_continue_8:
    %23 = maxon.var_ref {var = __try_result_5} {type = i64}
    maxon.assign %23 {var = parsed} {kind = i64} {decl = 1 : i1}
    %24 = maxon.literal {value = 1000 : i64}
    %25 = maxon.binop %23, %24 {op = gt}
    maxon.cond_br %25 [then: guard_9, else: guard_9.after]
  guard_9:
    __scope_26 = maxon.scope_enter {tag = if_then}
    %27 = maxon.literal {value = 99 : i64}
    maxon.scope_exit {scope = __scope_26} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %27
  guard_9.after:
    %28 = maxon.literal {value = 3 : i64}
    %29 = maxon.call @advent.multiply %28
    maxon.assign %29 {var = __range_val_10} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %30 = maxon.literal {value = 0 : i64}
    %31 = maxon.binop %29, %30 {op = lt}
    %32 = maxon.literal {value = 4294967295 : i64}
    %33 = maxon.binop %29, %32 {op = gt}
    %34 = maxon.binop %31, %33 {op = or}
    maxon.cond_br %34 [then: __range_panic_10, else: __range_ok_10]
  __range_panic_10:
    maxon.panic "panic at day4a.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_10:
    %36 = maxon.var_ref {var = __range_val_10} {type = i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %36
  }
}
=== standard
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param x : StdI64
    %4 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %4
    func.return %2
  }
  func @advent.main() -> u32 {
  entry:
    %5 = arith.constant {value = 0 : i64}
    %6 = std.call_runtime @mm_scope_enter %5
    memref.store %6, __scope_4
    %7 = func.call @stdlib.CommandLine.args
    memref.store %7, args
    %9 = arith.constant {value = 0 : i64}
    %10 = memref.load args : i64
    %11, %12 = func.try_call @StringArray.get %10, %9
    %13 = memref.lea_rdata __str_11
    %14 = std.ptr_to_i64 %13
    %15 = arith.constant {value = 0 : i64}
    %16 = arith.constant {value = 16 : i64}
    %17 = arith.constant {value = 0 : i64}
    %18 = std.call_runtime @mm_alloc %16, %17
    memref.store %18, __strtmp_11
    %19 = arith.constant {value = 32 : i64}
    %20 = arith.constant {value = 0 : i64}
    %21 = std.call_runtime @mm_alloc_in %19, %18, %20
    memref.store %21, __strtmp_managed_11
    %22 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %14, %22+0
    %23 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %15, %23+8
    %24 = arith.constant {value = 0 : i64}
    %25 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %24, %25+16
    %26 = arith.constant {value = 1 : i64}
    %27 = memref.load __strtmp_managed_11 : i64
    memref.store_indirect %26, %27+24
    %28 = memref.load __strtmp_managed_11 : i64
    %29 = memref.load __strtmp_11 : i64
    memref.store_indirect %28, %29+0
    %30 = arith.constant {value = 0 : i64}
    %31 = memref.load __strtmp_11 : i64
    memref.store_indirect %30, %31+8
    memref.store %18, __try_default_1
    memref.store %11, __try_result_0
    %34 = arith.constant {value = 0 : i64}
    %35 = arith.cmpi ne %12, %34
    cf.cond_br %35 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %36 = memref.load __try_default_1 : i64
    memref.store %36, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %37 = memref.load __try_result_0 : i64
    %38, %39 = func.try_call @stdlib.Parsing.__int_fromString %37
    %40 = arith.constant {value = 0 : i64}
    memref.store %40, __try_default_6
    memref.store %38, __try_result_5
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.cmpi ne %39, %41
    cf.cond_br %42 [then: otherwise_default_error_7, else: otherwise_default_continue_8]
  otherwise_default_error_7:
    %43 = memref.load __try_default_6 : i64
    memref.store %43, __try_result_5
    cf.br otherwise_default_continue_8
  otherwise_default_continue_8:
    %44 = memref.load __try_result_5 : i64
    %45 = arith.constant {value = 1000 : i64}
    %46 = arith.cmpi gt %44, %45
    cf.cond_br %46 [then: guard_9, else: guard_9.after]
  guard_9:
    %47 = arith.constant {value = 0 : i64}
    %48 = std.call_runtime @mm_scope_enter %47
    memref.store %48, __scope_26
    %49 = arith.constant {value = 99 : i64}
    %50 = memref.load __scope_26 : i64
    std.call_runtime @mm_scope_exit %50
    %51 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %51
    func.return %49
  guard_9.after:
    %52 = arith.constant {value = 3 : i64}
    %53 = func.call @advent.multiply %52
    memref.store %53, __range_val_10
    %54 = arith.constant {value = 0 : i64}
    %55 = arith.cmpi lt %53, %54
    %56 = arith.constant {value = 4294967295 : i64}
    %57 = arith.cmpi gt %53, %56
    %58 = arith.ori1 %55, %57
    cf.cond_br %58 [then: __range_panic_10, else: __range_ok_10]
  __range_panic_10:
    %59 = memref.lea_symdata __panic_msg_35
    %60 = std.ptr_to_i64 %59
    std.call_runtime @maxon_panic %60
  __range_ok_10:
    %61 = memref.load __range_val_10 : i64
    %62 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %62
    func.return %61
  }
}
=== x86
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov [rbp-16], ecx
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, [rbp-16]
    x86.epilogue
    x86.ret
  }
  func @advent.main() -> u32 {
  entry:
    x86.prologue stack_size=112
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-16], eax
    x86.xor ecx, ecx
    x86.mov edx, [rbp-16]
    x86.xchg rdx, rcx
    x86.call StringArray.get
    x86.lea_rdata rbx, [__str_11]
    x86.mov rsi, rbx
    x86.xor edi, edi
    x86.mov r8, 16
    x86.xor r9, r9
    x86.mov [rbp-88], eax
    x86.mov [rbp-96], edx
    x86.mov [rbp-104], esi
    x86.mov rcx, r8
    x86.mov rdx, r9
    x86.call mm_alloc
    x86.mov [rbp-24], eax
    x86.mov ecx, 32
    x86.xor edx, edx
    x86.mov r8, rdx
    x86.mov rdx, rax
    x86.call mm_alloc_in
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-104]
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-32]
    x86.xor ecx, ecx
    x86.mov [eax+8], ecx
    x86.xor eax, eax
    x86.mov ecx, [rbp-32]
    x86.mov [ecx+16], eax
    x86.mov eax, 1
    x86.mov ecx, [rbp-32]
    x86.mov [ecx+24], eax
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-24]
    x86.mov [ecx+0], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-24]
    x86.mov [ecx+8], eax
    x86.mov eax, [rbp-24]
    x86.mov [rbp-40], eax
    x86.mov eax, [rbp-88]
    x86.mov [rbp-48], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-96]
    x86.cmp ecx, eax
    x86.je advent.main.otherwise_default_cleanup_4
  otherwise_default_error_2:
    x86.mov eax, [rbp-40]
    x86.mov [rbp-48], eax
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_cleanup_4:
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-48]
    x86.mov rcx, rax
    x86.call stdlib.Parsing.__int_fromString
    x86.xor ecx, ecx
    x86.mov [rbp-56], ecx
    x86.mov [rbp-64], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_8
  otherwise_default_error_7:
    x86.mov eax, [rbp-56]
    x86.mov [rbp-64], eax
    x86.jmp advent.main.otherwise_default_continue_8
  otherwise_default_continue_8:
    x86.mov eax, [rbp-64]
    x86.mov ecx, 1000
    x86.cmp eax, ecx
    x86.jle advent.main.guard_9.after
  guard_9:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-72], eax
    x86.mov eax, 99
    x86.mov ecx, [rbp-72]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov eax, 99
    x86.epilogue
    x86.ret
  guard_9.after:
    x86.mov eax, 3
    x86.mov rcx, rax
    x86.call advent.multiply
    x86.mov [rbp-80], eax
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
    x86.je advent.main.__range_ok_10
  __range_panic_10:
    x86.lea_symdata rax, [__panic_msg_35]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_10:
    x86.mov eax, [rbp-80]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-88], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-88]
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
    maxon.assign %5 {var = args} {decl = 1 : i1}
    %6 = maxon.struct_var_ref args
    %7 = maxon.literal {value = 0 : i64}
    %10, %9 = maxon.try_call @StringArray.get %6, %7
    %11 = maxon.string_literal ""
    maxon.assign %11 {var = __try_default_1} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %10 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %9, %12 {op = ne}
    maxon.cond_br %13 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %14 = maxon.struct_var_ref __try_default_1
    maxon.assign %14 {var = __try_result_0} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    maxon.release {var = __try_default_1} {type = String}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %15 = maxon.struct_var_ref __try_result_0
    %18, %17 = maxon.try_call @stdlib.Parsing.__int_fromString %15
    %19 = maxon.literal {value = 0 : i64}
    maxon.assign %19 {var = __try_default_6} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %18 {var = __try_result_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %17, %20 {op = ne}
    maxon.cond_br %21 [then: otherwise_default_error_7, else: otherwise_default_continue_8]
  otherwise_default_error_7:
    %22 = maxon.var_ref {var = __try_default_6} {type = i64}
    maxon.assign %22 {var = __try_result_5} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_8
  otherwise_default_continue_8:
    %23 = maxon.var_ref {var = __try_result_5} {type = i64}
    maxon.assign %23 {var = parsed} {kind = i64} {decl = 1 : i1}
    %24 = maxon.literal {value = 1000 : i64}
    %25 = maxon.binop %23, %24 {op = gt}
    maxon.cond_br %25 [then: guard_9, else: guard_9.after]
  guard_9:
    __scope_26 = maxon.scope_enter {tag = if_then}
    %27 = maxon.literal {value = 99 : i64}
    maxon.scope_exit {scope = __scope_26} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %27
  guard_9.after:
    %28 = maxon.literal {value = 3 : i64}
    %29 = maxon.call @advent.multiply %28
    maxon.assign %29 {var = __range_val_10} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %30 = maxon.literal {value = 0 : i64}
    %31 = maxon.binop %29, %30 {op = lt}
    %32 = maxon.literal {value = 4294967295 : i64}
    %33 = maxon.binop %29, %32 {op = gt}
    %34 = maxon.binop %31, %33 {op = or}
    maxon.cond_br %34 [then: __range_panic_10, else: __range_ok_10]
  __range_panic_10:
    maxon.panic "panic at day4b.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_10:
    %36 = maxon.var_ref {var = __range_val_10} {type = i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %36
  }
}
=== standard
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param x : StdI64
    %3 = arith.constant {value = 2 : i64}
    %4 = arith.muli %2, %3
    %5 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %5
    func.return %4
  }
  func @advent.main() -> u32 {
  entry:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_4
    %8 = func.call @stdlib.CommandLine.args
    memref.store %8, args
    %10 = arith.constant {value = 0 : i64}
    %11 = memref.load args : i64
    %12, %13 = func.try_call @StringArray.get %11, %10
    %14 = memref.lea_rdata __str_11
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
    memref.store %19, __try_default_1
    memref.store %12, __try_result_0
    %35 = arith.constant {value = 0 : i64}
    %36 = arith.cmpi ne %13, %35
    cf.cond_br %36 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %37 = memref.load __try_default_1 : i64
    memref.store %37, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %38 = memref.load __try_result_0 : i64
    %39, %40 = func.try_call @stdlib.Parsing.__int_fromString %38
    %41 = arith.constant {value = 0 : i64}
    memref.store %41, __try_default_6
    memref.store %39, __try_result_5
    %42 = arith.constant {value = 0 : i64}
    %43 = arith.cmpi ne %40, %42
    cf.cond_br %43 [then: otherwise_default_error_7, else: otherwise_default_continue_8]
  otherwise_default_error_7:
    %44 = memref.load __try_default_6 : i64
    memref.store %44, __try_result_5
    cf.br otherwise_default_continue_8
  otherwise_default_continue_8:
    %45 = memref.load __try_result_5 : i64
    %46 = arith.constant {value = 1000 : i64}
    %47 = arith.cmpi gt %45, %46
    cf.cond_br %47 [then: guard_9, else: guard_9.after]
  guard_9:
    %48 = arith.constant {value = 0 : i64}
    %49 = std.call_runtime @mm_scope_enter %48
    memref.store %49, __scope_26
    %50 = arith.constant {value = 99 : i64}
    %51 = memref.load __scope_26 : i64
    std.call_runtime @mm_scope_exit %51
    %52 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %52
    func.return %50
  guard_9.after:
    %53 = arith.constant {value = 3 : i64}
    %54 = func.call @advent.multiply %53
    memref.store %54, __range_val_10
    %55 = arith.constant {value = 0 : i64}
    %56 = arith.cmpi lt %54, %55
    %57 = arith.constant {value = 4294967295 : i64}
    %58 = arith.cmpi gt %54, %57
    %59 = arith.ori1 %56, %58
    cf.cond_br %59 [then: __range_panic_10, else: __range_ok_10]
  __range_panic_10:
    %60 = memref.lea_symdata __panic_msg_35
    %61 = std.ptr_to_i64 %60
    std.call_runtime @maxon_panic %61
  __range_ok_10:
    %62 = memref.load __range_val_10 : i64
    %63 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %63
    func.return %62
  }
}
=== x86
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov [rbp-16], ecx
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov edx, [rbp-16]
    x86.imul edx, ecx
    x86.mov ebx, [rbp-8]
    x86.mov [rbp-24], edx
    x86.mov rcx, rbx
    x86.call mm_scope_exit
    x86.mov eax, [rbp-24]
    x86.epilogue
    x86.ret
  }
  func @advent.main() -> u32 {
  entry:
    x86.prologue stack_size=112
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-16], eax
    x86.xor ecx, ecx
    x86.mov edx, [rbp-16]
    x86.xchg rdx, rcx
    x86.call StringArray.get
    x86.lea_rdata rbx, [__str_11]
    x86.mov rsi, rbx
    x86.xor edi, edi
    x86.mov r8, 16
    x86.xor r9, r9
    x86.mov [rbp-88], eax
    x86.mov [rbp-96], edx
    x86.mov [rbp-104], esi
    x86.mov rcx, r8
    x86.mov rdx, r9
    x86.call mm_alloc
    x86.mov [rbp-24], eax
    x86.mov ecx, 32
    x86.xor edx, edx
    x86.mov r8, rdx
    x86.mov rdx, rax
    x86.call mm_alloc_in
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-104]
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-32]
    x86.xor ecx, ecx
    x86.mov [eax+8], ecx
    x86.xor eax, eax
    x86.mov ecx, [rbp-32]
    x86.mov [ecx+16], eax
    x86.mov eax, 1
    x86.mov ecx, [rbp-32]
    x86.mov [ecx+24], eax
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-24]
    x86.mov [ecx+0], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-24]
    x86.mov [ecx+8], eax
    x86.mov eax, [rbp-24]
    x86.mov [rbp-40], eax
    x86.mov eax, [rbp-88]
    x86.mov [rbp-48], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-96]
    x86.cmp ecx, eax
    x86.je advent.main.otherwise_default_cleanup_4
  otherwise_default_error_2:
    x86.mov eax, [rbp-40]
    x86.mov [rbp-48], eax
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_cleanup_4:
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-48]
    x86.mov rcx, rax
    x86.call stdlib.Parsing.__int_fromString
    x86.xor ecx, ecx
    x86.mov [rbp-56], ecx
    x86.mov [rbp-64], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_8
  otherwise_default_error_7:
    x86.mov eax, [rbp-56]
    x86.mov [rbp-64], eax
    x86.jmp advent.main.otherwise_default_continue_8
  otherwise_default_continue_8:
    x86.mov eax, [rbp-64]
    x86.mov ecx, 1000
    x86.cmp eax, ecx
    x86.jle advent.main.guard_9.after
  guard_9:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-72], eax
    x86.mov eax, 99
    x86.mov ecx, [rbp-72]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov eax, 99
    x86.epilogue
    x86.ret
  guard_9.after:
    x86.mov eax, 3
    x86.mov rcx, rax
    x86.call advent.multiply
    x86.mov [rbp-80], eax
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
    x86.je advent.main.__range_ok_10
  __range_panic_10:
    x86.lea_symdata rax, [__panic_msg_35]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_10:
    x86.mov eax, [rbp-80]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-88], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-88]
    x86.epilogue
    x86.ret
  }
}
```
