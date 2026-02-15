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
function main() returns Integer
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
  func @advent.main() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    func.return %0
  }
}
=== x86
module {
  func @advent.main() -> i64 {
  entry:
    x86.xor eax, eax
    x86.ret
  }
}
```

<!-- test: day2 -->
```maxon
function add(x Integer, y Integer) returns Integer
    return x + y
end 'add'

function main() returns Integer
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
  func @advent.main() -> i64 {
  entry:
    %3 = arith.constant {value = 3 : i64}
    %4 = arith.constant {value = 4 : i64}
    %5 = func.call @advent.add %3, %4
    func.return %5
  }
}
=== x86
module {
  func @advent.add(x: i64, y: i64) -> i64 {
  entry:
    x86.lea eax, [ecx + edx]
    x86.ret
  }
  func @advent.main() -> i64 {
  entry:
    x86.mov eax, 3
    x86.mov ecx, 4
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.jmp advent.add
  }
}
```

<!-- test: day4a -->
<!-- Args: 1 -->
```maxon
function multiply(x Integer) returns Integer
    return x * 1
end 'multiply'

function main() returns Integer
  let args = CommandLine.args()
  let a = try int.fromString(try args.get(0) otherwise "") otherwise 0
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
    maxon.cond_br %11 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %12 = maxon.struct_var_ref __try_default_1
    maxon.assign %12 {var = __try_result_0} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %13 = maxon.struct_var_ref __try_result_0
    %16, %15 = maxon.try_call @stdlib.Parsing.__int_fromString %13
    %17 = maxon.literal {value = 0 : i64}
    maxon.assign %17 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %16 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.literal {value = 0 : i64}
    %19 = maxon.binop %15, %18 {op = ne}
    maxon.cond_br %19 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %20 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %20 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %21 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %21 {var = a} {kind = i64} {decl = 1 : i1}
    %22 = maxon.literal {value = 3 : i64}
    %23 = maxon.call @advent.multiply %22
    maxon.return %23
  }
}
=== standard
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    func.return %0
  }
  func @advent.main() -> i64 {
  entry:
    %2 = func.call @stdlib.CommandLine.args
    memref.store %2, args
    %4 = arith.constant {value = 0 : i64}
    %5 = memref.load args : i64
    %6, %7 = func.try_call @StringArray.get %5, %4
    %8 = memref.lea_rdata __str_9
    %9 = std.ptr_to_i64 %8
    %10 = arith.constant {value = 0 : i64}
    %11 = arith.constant {value = 32 : i64}
    %12 = std.call_runtime @maxon_alloc %11
    memref.store %12, __strtmp_managed_9
    %13 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %9, %13+0
    %14 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %10, %14+8
    %15 = arith.constant {value = 0 : i64}
    %16 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %15, %16+16
    %17 = arith.constant {value = 1 : i64}
    %18 = memref.load __strtmp_managed_9 : i64
    memref.store_indirect %17, %18+24
    %19 = arith.constant {value = 16 : i64}
    %20 = std.call_runtime @maxon_alloc %19
    memref.store %20, __strtmp_9
    %21 = memref.load __strtmp_managed_9 : i64
    %22 = memref.load __strtmp_9 : i64
    memref.store_indirect %21, %22+0
    %23 = arith.constant {value = 0 : i64}
    %24 = memref.load __strtmp_9 : i64
    memref.store_indirect %23, %24+8
    memref.store %20, __try_default_1
    memref.store %6, __try_result_0
    %27 = arith.constant {value = 0 : i64}
    %28 = arith.cmpi ne %7, %27
    cf.cond_br %28 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %29 = memref.load __try_default_1 : i64
    memref.store %29, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %30 = memref.load __try_result_0 : i64
    %31, %32 = func.try_call @stdlib.Parsing.__int_fromString %30
    %33 = arith.constant {value = 0 : i64}
    memref.store %33, __try_default_5
    memref.store %31, __try_result_4
    %34 = arith.constant {value = 0 : i64}
    %35 = arith.cmpi ne %32, %34
    cf.cond_br %35 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %36 = memref.load __try_default_5 : i64
    memref.store %36, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %38 = arith.constant {value = 3 : i64}
    %39 = func.call @advent.multiply %38
    func.return %39
  }
}
=== x86
module {
  func @advent.multiply(x: i64) -> i64 {
  entry:
    x86.mov eax, ecx
    x86.ret
  }
  func @advent.main() -> i64 {
  entry:
    x86.prologue stack_size=80
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
    x86.mov [rbp-56], eax
    x86.mov [rbp-64], edx
    x86.mov [rbp-72], ebx
    x86.mov rcx, rsi
    x86.call maxon_alloc
    x86.mov [rbp-16], eax
    x86.mov edx, [rbp-16]
    x86.mov ebx, [rbp-72]
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
    x86.mov eax, [rbp-56]
    x86.mov [rbp-40], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-64]
    x86.cmp ecx, eax
    x86.je advent.main.otherwise_default_continue_3
  otherwise_default_error_2:
    x86.mov eax, [rbp-32]
    x86.mov [rbp-40], eax
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-40]
    x86.mov rcx, rax
    x86.call stdlib.Parsing.__int_fromString
    x86.xor ecx, ecx
    x86.mov [rbp-48], ecx
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-48]
    x86.jmp advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, 3
    x86.mov rcx, rax
    x86.epilogue
    x86.jmp advent.multiply
  }
}
```

<!-- test: day4b -->
<!-- Args: 3 -->
```maxon
function multiply(x Integer) returns Integer
    return x * 2
end 'multiply'

function main() returns Integer
  let args = CommandLine.args()
  let a = try int.fromString(try args.get(0) otherwise "") otherwise 0
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
    maxon.cond_br %11 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %12 = maxon.struct_var_ref __try_default_1
    maxon.assign %12 {var = __try_result_0} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %13 = maxon.struct_var_ref __try_result_0
    %16, %15 = maxon.try_call @stdlib.Parsing.__int_fromString %13
    %17 = maxon.literal {value = 0 : i64}
    maxon.assign %17 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %16 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.literal {value = 0 : i64}
    %19 = maxon.binop %15, %18 {op = ne}
    maxon.cond_br %19 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %20 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %20 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %21 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %21 {var = a} {kind = i64} {decl = 1 : i1}
    %22 = maxon.literal {value = 3 : i64}
    %23 = maxon.call @advent.multiply %22
    maxon.return %23
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
  func @advent.main() -> i64 {
  entry:
    %3 = func.call @stdlib.CommandLine.args
    memref.store %3, args
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
    %28 = arith.constant {value = 0 : i64}
    %29 = arith.cmpi ne %8, %28
    cf.cond_br %29 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %30 = memref.load __try_default_1 : i64
    memref.store %30, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %31 = memref.load __try_result_0 : i64
    %32, %33 = func.try_call @stdlib.Parsing.__int_fromString %31
    %34 = arith.constant {value = 0 : i64}
    memref.store %34, __try_default_5
    memref.store %32, __try_result_4
    %35 = arith.constant {value = 0 : i64}
    %36 = arith.cmpi ne %33, %35
    cf.cond_br %36 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %37 = memref.load __try_default_5 : i64
    memref.store %37, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %39 = arith.constant {value = 3 : i64}
    %40 = func.call @advent.multiply %39
    func.return %40
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
  func @advent.main() -> i64 {
  entry:
    x86.prologue stack_size=80
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
    x86.mov [rbp-56], eax
    x86.mov [rbp-64], edx
    x86.mov [rbp-72], ebx
    x86.mov rcx, rsi
    x86.call maxon_alloc
    x86.mov [rbp-16], eax
    x86.mov edx, [rbp-16]
    x86.mov ebx, [rbp-72]
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
    x86.mov eax, [rbp-56]
    x86.mov [rbp-40], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-64]
    x86.cmp ecx, eax
    x86.je advent.main.otherwise_default_continue_3
  otherwise_default_error_2:
    x86.mov eax, [rbp-32]
    x86.mov [rbp-40], eax
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-40]
    x86.mov rcx, rax
    x86.call stdlib.Parsing.__int_fromString
    x86.xor ecx, ecx
    x86.mov [rbp-48], ecx
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-48]
    x86.jmp advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, 3
    x86.mov rcx, rax
    x86.epilogue
    x86.jmp advent.multiply
  }
}
```
