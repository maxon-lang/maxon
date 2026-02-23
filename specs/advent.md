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
    x86.mov eax, 3
    x86.mov ecx, 4
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.call advent.add
    x86.mov [rbp-8], eax
    x86.xor edx, edx
    x86.cmp eax, edx
    x86.setl ebx
    x86.movzx ebx, ebxb
    x86.mov rsi, 4294967295
    x86.cmp rax, rsi
    x86.setg edi
    x86.movzx edi, edib
    x86.or ebx, edi
    x86.test ebx, ebx
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
    maxon.return %2
  }
  func @advent.main() -> i64 {
  entry:
    %3 = maxon.call @stdlib.CommandLine.args
    maxon.assign %3 {var = args} {decl = 1 : i1}
    %4 = maxon.struct_var_ref args
    %5 = maxon.literal {value = 0 : i64}
    %8, %7 = maxon.try_call @StringArray.get %4, %5
    %9 = maxon.string_literal ""
    maxon.assign %9 {var = __try_default_1} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %8 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %7, %10 {op = ne}
    maxon.cond_br %11 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %12 = maxon.struct_var_ref __try_default_1
    maxon.assign %12 {var = __try_result_0} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    maxon.release {var = __try_default_1} {type = String}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %13 = maxon.struct_var_ref __try_result_0
    %16, %15 = maxon.try_call @stdlib.Parsing.__int_fromString %13
    %17 = maxon.literal {value = 0 : i64}
    maxon.assign %17 {var = __try_default_6} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %16 {var = __try_result_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.literal {value = 0 : i64}
    %19 = maxon.binop %15, %18 {op = ne}
    maxon.cond_br %19 [then: otherwise_default_error_7, else: otherwise_default_continue_8]
  otherwise_default_error_7:
    %20 = maxon.var_ref {var = __try_default_6} {type = i64}
    maxon.assign %20 {var = __try_result_5} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_8
  otherwise_default_continue_8:
    %21 = maxon.var_ref {var = __try_result_5} {type = i64}
    maxon.assign %21 {var = parsed} {kind = i64} {decl = 1 : i1}
    %22 = maxon.literal {value = 1000 : i64}
    %23 = maxon.binop %21, %22 {op = gt}
    maxon.cond_br %23 [then: guard_9, else: guard_9.after]
  guard_9:
    %24 = maxon.literal {value = 99 : i64}
    maxon.return %24
  guard_9.after:
    %25 = maxon.literal {value = 3 : i64}
    %26 = maxon.call @advent.multiply %25
    maxon.assign %26 {var = __range_val_10} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %27 = maxon.literal {value = 0 : i64}
    %28 = maxon.binop %26, %27 {op = lt}
    %29 = maxon.literal {value = 4294967295 : i64}
    %30 = maxon.binop %26, %29 {op = gt}
    %31 = maxon.binop %28, %30 {op = or}
    maxon.cond_br %31 [then: __range_panic_10, else: __range_ok_10]
  __range_panic_10:
    maxon.panic "panic at day4a.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_10:
    %33 = maxon.var_ref {var = __range_val_10} {type = i64}
    maxon.return %33
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
    %2 = func.call @stdlib.CommandLine.args
    memref.store %2, args
    %5 = arith.constant {value = 0 : i64}
    %6 = memref.load args : i64
    %7, %8 = func.try_call @StringArray.get %6, %5
    %9 = memref.lea_rdata __str_9
    %10 = std.ptr_to_i64 %9
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.constant {value = 32 : i64}
    %13 = std.call_runtime @maxon_alloc %12
    memref.store %13, __strtmp_managed_9
    %14 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %10, %14+0
    %15 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %11, %15+8
    %16 = arith.constant {value = 0 : i64}
    %17 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %16, %17+16
    %18 = arith.constant {value = 1 : i64}
    %19 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %18, %19+24
    %20 = arith.constant {value = 16 : i64}
    %21 = std.call_runtime @maxon_alloc %20
    memref.store %21, __strtmp_9
    %22 = memref.load __strtmp_managed_9 : i64
    %23 = memref.load __strtmp_9 : i64
    memref.store_indirect %22, %23+0
    %24 = arith.constant {value = 0 : i64}
    %25 = memref.load __strtmp_9 : i64
    memref.store_indirect %24, %25+8
    memref.store %21, __try_default_1
    memref.store %7, __try_result_0
    %29 = arith.constant {value = 0 : i64}
    %30 = arith.cmpi ne %8, %29
    cf.cond_br %30 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %31 = memref.load __try_result_0 : i64
    std.call_runtime @__destroy_String %31
    %32 = memref.load __try_default_1 : i64
    memref.store %32, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    %33 = memref.load __try_default_1 : i64
    std.call_runtime @__destroy_String %33
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %34 = memref.load __try_result_0 : i64
    %35, %36 = func.try_call @stdlib.Parsing.__int_fromString %34
    %37 = arith.constant {value = 0 : i64}
    memref.store %37, __try_default_6
    memref.store %35, __try_result_5
    %38 = arith.constant {value = 0 : i64}
    %39 = arith.cmpi ne %36, %38
    cf.cond_br %39 [then: otherwise_default_error_7, else: otherwise_default_continue_8]
  otherwise_default_error_7:
    %40 = memref.load __try_default_6 : i64
    memref.store %40, __try_result_5
    cf.br otherwise_default_continue_8
  otherwise_default_continue_8:
    %41 = memref.load __try_result_5 : i64
    %42 = arith.constant {value = 1000 : i64}
    %43 = arith.cmpi gt %41, %42
    cf.cond_br %43 [then: guard_9, else: guard_9.after]
  guard_9:
    %44 = arith.constant {value = 99 : i64}
    %45 = memref.load args : i64
    std.call_runtime @__destroy_StringArray %45
    %46 = memref.load __try_result_0 : i64
    std.call_runtime @__destroy_String %46
    func.return %44
  guard_9.after:
    %47 = arith.constant {value = 3 : i64}
    %48 = func.call @advent.multiply %47
    memref.store %48, __range_val_10
    %49 = arith.constant {value = 0 : i64}
    %50 = arith.cmpi lt %48, %49
    %51 = arith.constant {value = 4294967295 : i64}
    %52 = arith.cmpi gt %48, %51
    %53 = arith.ori1 %50, %52
    cf.cond_br %53 [then: __range_panic_10, else: __range_ok_10]
  __range_panic_10:
    %54 = memref.lea_symdata __panic_msg_32
    %55 = std.ptr_to_i64 %54
    std.call_runtime @maxon_panic %55
  __range_ok_10:
    %56 = memref.load __range_val_10 : i64
    %57 = memref.load args : i64
    std.call_runtime @__destroy_StringArray %57
    %58 = memref.load __try_result_0 : i64
    std.call_runtime @__destroy_String %58
    func.return %56
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
    x86.prologue stack_size=96
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-8], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-8]
    x86.mov rdx, rax
    x86.call StringArray.get
    x86.lea_rdata rcx, [__str_9]
    x86.mov rbx, rcx
    x86.xor ecx, ecx
    x86.mov esi, 32
    x86.mov [rbp-72], eax
    x86.mov [rbp-80], edx
    x86.mov [rbp-88], ebx
    x86.mov rcx, rsi
    x86.call maxon_alloc
    x86.mov [rbp-16], eax
    x86.mov edx, [rbp-16]
    x86.mov ebx, [rbp-88]
    x86.mov [edx+0], ebx
    x86.mov esi, [rbp-16]
    x86.xor edi, edi
    x86.mov [esi+8], edi
    x86.xor r8, r8
    x86.mov r9, [rbp-16]
    x86.mov [r9+16], r8
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.mov [ecx+24], eax
    x86.mov eax, 16
    x86.mov rcx, rax
    x86.call maxon_alloc
    x86.mov [rbp-24], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [rbp-24]
    x86.mov [edx+0], ecx
    x86.xor ecx, ecx
    x86.mov edx, [rbp-24]
    x86.mov [edx+8], ecx
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-72]
    x86.mov [rbp-40], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-80]
    x86.cmp ecx, eax
    x86.je advent.main.otherwise_default_cleanup_4
  otherwise_default_error_2:
    x86.mov rcx, [rbp-40]
    x86.call __destroy_String
    x86.mov ecx, [rbp-32]
    x86.mov [rbp-40], ecx
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_cleanup_4:
    x86.mov rcx, [rbp-32]
    x86.call __destroy_String
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-40]
    x86.mov rcx, rax
    x86.call stdlib.Parsing.__int_fromString
    x86.xor ecx, ecx
    x86.mov [rbp-48], ecx
    x86.mov [rbp-56], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_8
  otherwise_default_error_7:
    x86.mov eax, [rbp-48]
    x86.mov [rbp-56], eax
    x86.jmp advent.main.otherwise_default_continue_8
  otherwise_default_continue_8:
    x86.mov eax, [rbp-56]
    x86.mov ecx, 1000
    x86.cmp eax, ecx
    x86.jle advent.main.guard_9.after
  guard_9:
    x86.mov eax, 99
    x86.mov ecx, [rbp-8]
    x86.call __destroy_StringArray
    x86.mov rcx, [rbp-40]
    x86.call __destroy_String
    x86.mov eax, 99
    x86.epilogue
    x86.ret
  guard_9.after:
    x86.mov eax, 3
    x86.mov rcx, rax
    x86.call advent.multiply
    x86.mov [rbp-64], eax
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
    x86.lea_symdata rax, [__panic_msg_32]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_10:
    x86.mov eax, [rbp-64]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-72], eax
    x86.call __destroy_StringArray
    x86.mov rcx, [rbp-40]
    x86.call __destroy_String
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
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {optimalType = i64}
    maxon.return %2
  }
  func @advent.main() -> i64 {
  entry:
    %3 = maxon.call @stdlib.CommandLine.args
    maxon.assign %3 {var = args} {decl = 1 : i1}
    %4 = maxon.struct_var_ref args
    %5 = maxon.literal {value = 0 : i64}
    %8, %7 = maxon.try_call @StringArray.get %4, %5
    %9 = maxon.string_literal ""
    maxon.assign %9 {var = __try_default_1} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %8 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %7, %10 {op = ne}
    maxon.cond_br %11 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %12 = maxon.struct_var_ref __try_default_1
    maxon.assign %12 {var = __try_result_0} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    maxon.release {var = __try_default_1} {type = String}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %13 = maxon.struct_var_ref __try_result_0
    %16, %15 = maxon.try_call @stdlib.Parsing.__int_fromString %13
    %17 = maxon.literal {value = 0 : i64}
    maxon.assign %17 {var = __try_default_6} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %16 {var = __try_result_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.literal {value = 0 : i64}
    %19 = maxon.binop %15, %18 {op = ne}
    maxon.cond_br %19 [then: otherwise_default_error_7, else: otherwise_default_continue_8]
  otherwise_default_error_7:
    %20 = maxon.var_ref {var = __try_default_6} {type = i64}
    maxon.assign %20 {var = __try_result_5} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_8
  otherwise_default_continue_8:
    %21 = maxon.var_ref {var = __try_result_5} {type = i64}
    maxon.assign %21 {var = parsed} {kind = i64} {decl = 1 : i1}
    %22 = maxon.literal {value = 1000 : i64}
    %23 = maxon.binop %21, %22 {op = gt}
    maxon.cond_br %23 [then: guard_9, else: guard_9.after]
  guard_9:
    %24 = maxon.literal {value = 99 : i64}
    maxon.return %24
  guard_9.after:
    %25 = maxon.literal {value = 3 : i64}
    %26 = maxon.call @advent.multiply %25
    maxon.assign %26 {var = __range_val_10} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %27 = maxon.literal {value = 0 : i64}
    %28 = maxon.binop %26, %27 {op = lt}
    %29 = maxon.literal {value = 4294967295 : i64}
    %30 = maxon.binop %26, %29 {op = gt}
    %31 = maxon.binop %28, %30 {op = or}
    maxon.cond_br %31 [then: __range_panic_10, else: __range_ok_10]
  __range_panic_10:
    maxon.panic "panic at day4b.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_10:
    %33 = maxon.var_ref {var = __range_val_10} {type = i64}
    maxon.return %33
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
    %3 = func.call @stdlib.CommandLine.args
    memref.store %3, args
    %6 = arith.constant {value = 0 : i64}
    %7 = memref.load args : i64
    %8, %9 = func.try_call @StringArray.get %7, %6
    %10 = memref.lea_rdata __str_9
    %11 = std.ptr_to_i64 %10
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.constant {value = 32 : i64}
    %14 = std.call_runtime @maxon_alloc %13
    memref.store %14, __strtmp_managed_9
    %15 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %11, %15+0
    %16 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %12, %16+8
    %17 = arith.constant {value = 0 : i64}
    %18 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %17, %18+16
    %19 = arith.constant {value = 1 : i64}
    %20 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %19, %20+24
    %21 = arith.constant {value = 16 : i64}
    %22 = std.call_runtime @maxon_alloc %21
    memref.store %22, __strtmp_9
    %23 = memref.load __strtmp_managed_9 : i64
    %24 = memref.load __strtmp_9 : i64
    memref.store_indirect %23, %24+0
    %25 = arith.constant {value = 0 : i64}
    %26 = memref.load __strtmp_9 : i64
    memref.store_indirect %25, %26+8
    memref.store %22, __try_default_1
    memref.store %8, __try_result_0
    %30 = arith.constant {value = 0 : i64}
    %31 = arith.cmpi ne %9, %30
    cf.cond_br %31 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %32 = memref.load __try_result_0 : i64
    std.call_runtime @__destroy_String %32
    %33 = memref.load __try_default_1 : i64
    memref.store %33, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    %34 = memref.load __try_default_1 : i64
    std.call_runtime @__destroy_String %34
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %35 = memref.load __try_result_0 : i64
    %36, %37 = func.try_call @stdlib.Parsing.__int_fromString %35
    %38 = arith.constant {value = 0 : i64}
    memref.store %38, __try_default_6
    memref.store %36, __try_result_5
    %39 = arith.constant {value = 0 : i64}
    %40 = arith.cmpi ne %37, %39
    cf.cond_br %40 [then: otherwise_default_error_7, else: otherwise_default_continue_8]
  otherwise_default_error_7:
    %41 = memref.load __try_default_6 : i64
    memref.store %41, __try_result_5
    cf.br otherwise_default_continue_8
  otherwise_default_continue_8:
    %42 = memref.load __try_result_5 : i64
    %43 = arith.constant {value = 1000 : i64}
    %44 = arith.cmpi gt %42, %43
    cf.cond_br %44 [then: guard_9, else: guard_9.after]
  guard_9:
    %45 = arith.constant {value = 99 : i64}
    %46 = memref.load args : i64
    std.call_runtime @__destroy_StringArray %46
    %47 = memref.load __try_result_0 : i64
    std.call_runtime @__destroy_String %47
    func.return %45
  guard_9.after:
    %48 = arith.constant {value = 3 : i64}
    %49 = func.call @advent.multiply %48
    memref.store %49, __range_val_10
    %50 = arith.constant {value = 0 : i64}
    %51 = arith.cmpi lt %49, %50
    %52 = arith.constant {value = 4294967295 : i64}
    %53 = arith.cmpi gt %49, %52
    %54 = arith.ori1 %51, %53
    cf.cond_br %54 [then: __range_panic_10, else: __range_ok_10]
  __range_panic_10:
    %55 = memref.lea_symdata __panic_msg_32
    %56 = std.ptr_to_i64 %55
    std.call_runtime @maxon_panic %56
  __range_ok_10:
    %57 = memref.load __range_val_10 : i64
    %58 = memref.load args : i64
    std.call_runtime @__destroy_StringArray %58
    %59 = memref.load __try_result_0 : i64
    std.call_runtime @__destroy_String %59
    func.return %57
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
    x86.prologue stack_size=96
    x86.call stdlib.CommandLine.args
    x86.mov [rbp-8], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-8]
    x86.mov rdx, rax
    x86.call StringArray.get
    x86.lea_rdata rcx, [__str_9]
    x86.mov rbx, rcx
    x86.xor ecx, ecx
    x86.mov esi, 32
    x86.mov [rbp-72], eax
    x86.mov [rbp-80], edx
    x86.mov [rbp-88], ebx
    x86.mov rcx, rsi
    x86.call maxon_alloc
    x86.mov [rbp-16], eax
    x86.mov edx, [rbp-16]
    x86.mov ebx, [rbp-88]
    x86.mov [edx+0], ebx
    x86.mov esi, [rbp-16]
    x86.xor edi, edi
    x86.mov [esi+8], edi
    x86.xor r8, r8
    x86.mov r9, [rbp-16]
    x86.mov [r9+16], r8
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.mov [ecx+24], eax
    x86.mov eax, 16
    x86.mov rcx, rax
    x86.call maxon_alloc
    x86.mov [rbp-24], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [rbp-24]
    x86.mov [edx+0], ecx
    x86.xor ecx, ecx
    x86.mov edx, [rbp-24]
    x86.mov [edx+8], ecx
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-72]
    x86.mov [rbp-40], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-80]
    x86.cmp ecx, eax
    x86.je advent.main.otherwise_default_cleanup_4
  otherwise_default_error_2:
    x86.mov rcx, [rbp-40]
    x86.call __destroy_String
    x86.mov ecx, [rbp-32]
    x86.mov [rbp-40], ecx
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_cleanup_4:
    x86.mov rcx, [rbp-32]
    x86.call __destroy_String
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-40]
    x86.mov rcx, rax
    x86.call stdlib.Parsing.__int_fromString
    x86.xor ecx, ecx
    x86.mov [rbp-48], ecx
    x86.mov [rbp-56], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_8
  otherwise_default_error_7:
    x86.mov eax, [rbp-48]
    x86.mov [rbp-56], eax
    x86.jmp advent.main.otherwise_default_continue_8
  otherwise_default_continue_8:
    x86.mov eax, [rbp-56]
    x86.mov ecx, 1000
    x86.cmp eax, ecx
    x86.jle advent.main.guard_9.after
  guard_9:
    x86.mov eax, 99
    x86.mov ecx, [rbp-8]
    x86.call __destroy_StringArray
    x86.mov rcx, [rbp-40]
    x86.call __destroy_String
    x86.mov eax, 99
    x86.epilogue
    x86.ret
  guard_9.after:
    x86.mov eax, 3
    x86.mov rcx, rax
    x86.call advent.multiply
    x86.mov [rbp-64], eax
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
    x86.lea_symdata rax, [__panic_msg_32]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_10:
    x86.mov eax, [rbp-64]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-72], eax
    x86.call __destroy_StringArray
    x86.mov rcx, [rbp-40]
    x86.call __destroy_String
    x86.mov eax, [rbp-72]
    x86.epilogue
    x86.ret
  }
}
```
