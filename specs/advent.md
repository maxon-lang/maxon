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
function main() returns int
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
function add(x int, y int) returns int
    return x + y
end 'add'

function main() returns int
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
    %2 = maxon.binop %0, %1 {op = add} {kind = i64}
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
function multiply(x int) returns int
    return x * 1
end 'multiply'

function main() returns int
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
    %2 = maxon.binop %0, %1 {op = mul} {kind = i64}
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
    %11 = maxon.binop %7, %10 {op = ne} {kind = i64}
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
    %19 = maxon.binop %15, %18 {op = ne} {kind = i64}
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
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, __sret_3.managed.element_size
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, __sret_3.managed.capacity
    %4 = arith.constant {value = 0 : i64}
    memref.store %4, __sret_3.managed.length
    %5 = arith.constant {value = 0 : i64}
    memref.store %5, __sret_3.managed.buffer
    %6 = arith.constant {value = 0 : i64}
    memref.store %6, __sret_3.iterIndex
    %7 = memref.lea __sret_3
    func.call @stdlib.CommandLine.args %7
    %8 = memref.load __sret_3.iterIndex : i64
    %9 = memref.load __sret_3.managed.buffer : i64
    %10 = memref.load __sret_3.managed.length : i64
    %11 = memref.load __sret_3.managed.capacity : i64
    %12 = memref.load __sret_3.managed.element_size : i64
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.constant {value = 0 : i64}
    memref.store %14, __sret_8._iterPos
    %15 = arith.constant {value = 0 : i64}
    memref.store %15, __sret_8._managed.element_size
    %16 = arith.constant {value = 0 : i64}
    memref.store %16, __sret_8._managed.capacity
    %17 = arith.constant {value = 0 : i64}
    memref.store %17, __sret_8._managed.length
    %18 = arith.constant {value = 0 : i64}
    memref.store %18, __sret_8._managed.buffer
    %19 = memref.lea __sret_8
    memref.store %12, __selfbuf_20.managed.element_size
    memref.store %11, __selfbuf_20.managed.capacity
    memref.store %10, __selfbuf_20.managed.length
    memref.store %9, __selfbuf_20.managed.buffer
    memref.store %8, __selfbuf_20.iterIndex
    %26 = memref.lea __selfbuf_20
    %27 = func.try_call @StringArray.get %19, %26, %13
    %33 = memref.lea_rdata __str_9
    %34 = std.ptr_to_i64 %33
    %35 = arith.constant {value = 0 : i64}
    %36 = arith.constant {value = 0 : i64}
    %37 = arith.constant {value = 1 : i64}
    %38 = arith.constant {value = 0 : i64}
    memref.store %34, __try_default_1._managed.buffer
    memref.store %35, __try_default_1._managed.length
    memref.store %36, __try_default_1._managed.capacity
    memref.store %37, __try_default_1._managed.element_size
    memref.store %38, __try_default_1._iterPos
    %44 = memref.load __sret_8._managed.buffer : i64
    memref.store %44, __try_result_0._managed.buffer
    %45 = memref.load __sret_8._managed.length : i64
    memref.store %45, __try_result_0._managed.length
    %46 = memref.load __sret_8._managed.capacity : i64
    memref.store %46, __try_result_0._managed.capacity
    %47 = memref.load __sret_8._managed.element_size : i64
    memref.store %47, __try_result_0._managed.element_size
    %48 = memref.load __sret_8._iterPos : i64
    memref.store %48, __try_result_0._iterPos
    %49 = arith.constant {value = 0 : i64}
    %50 = arith.cmpi ne %27, %49
    cf.cond_br %50 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %51 = memref.load __try_default_1._managed.buffer : i64
    memref.store %51, __try_result_0._managed.buffer
    %52 = memref.load __try_default_1._managed.length : i64
    memref.store %52, __try_result_0._managed.length
    %53 = memref.load __try_default_1._managed.capacity : i64
    memref.store %53, __try_result_0._managed.capacity
    %54 = memref.load __try_default_1._managed.element_size : i64
    memref.store %54, __try_result_0._managed.element_size
    %55 = memref.load __try_default_1._iterPos : i64
    memref.store %55, __try_result_0._iterPos
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %57 = memref.load __try_result_0._iterPos : i64
    memref.store %57, __argbuf_56._iterPos
    %58 = memref.load __try_result_0._managed.element_size : i64
    memref.store %58, __argbuf_56._managed.element_size
    %59 = memref.load __try_result_0._managed.capacity : i64
    memref.store %59, __argbuf_56._managed.capacity
    %60 = memref.load __try_result_0._managed.length : i64
    memref.store %60, __argbuf_56._managed.length
    %61 = memref.load __try_result_0._managed.buffer : i64
    memref.store %61, __argbuf_56._managed.buffer
    %62 = memref.lea __argbuf_56
    %63, %64 = func.try_call @stdlib.Parsing.__int_fromString %62
    %65 = arith.constant {value = 0 : i64}
    memref.store %65, __try_default_5
    memref.store %63, __try_result_4
    %66 = arith.constant {value = 0 : i64}
    %67 = arith.cmpi ne %64, %66
    cf.cond_br %67 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %68 = memref.load __try_default_5 : i64
    memref.store %68, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %70 = arith.constant {value = 3 : i64}
    %71 = func.call @advent.multiply %70
    func.return %71
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
    x86.prologue stack_size=256
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.xor ebx, ebx
    x86.mov [rbp-32], ebx
    x86.xor esi, esi
    x86.mov [rbp-40], esi
    x86.lea rdi, [rbp-40]
    x86.mov rcx, rdi
    x86.call stdlib.CommandLine.args
    x86.mov r8, [rbp-40]
    x86.mov r9, [rbp-32]
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-16]
    x86.mov edx, [rbp-8]
    x86.xor ebx, ebx
    x86.xor esi, esi
    x86.mov [rbp-48], esi
    x86.xor esi, esi
    x86.mov [rbp-56], esi
    x86.xor esi, esi
    x86.mov [rbp-64], esi
    x86.xor esi, esi
    x86.mov [rbp-72], esi
    x86.xor esi, esi
    x86.mov [rbp-80], esi
    x86.lea rsi, [rbp-80]
    x86.mov [rbp-88], edx
    x86.mov [rbp-96], ecx
    x86.mov [rbp-104], eax
    x86.mov [rbp-112], r9
    x86.mov [rbp-120], r8
    x86.lea rax, [rbp-120]
    x86.mov rcx, rsi
    x86.mov rdx, rax
    x86.mov r8, rbx
    x86.call StringArray.get
    x86.lea_rdata rax, [__str_9]
    x86.mov rcx, rax
    x86.xor eax, eax
    x86.xor ebx, ebx
    x86.mov esi, 1
    x86.xor edi, edi
    x86.mov [rbp-128], ecx
    x86.mov [rbp-136], eax
    x86.mov [rbp-144], ebx
    x86.mov [rbp-152], esi
    x86.mov [rbp-160], edi
    x86.mov eax, [rbp-80]
    x86.mov [rbp-168], eax
    x86.mov eax, [rbp-72]
    x86.mov [rbp-176], eax
    x86.mov eax, [rbp-64]
    x86.mov [rbp-184], eax
    x86.mov eax, [rbp-56]
    x86.mov [rbp-192], eax
    x86.mov eax, [rbp-48]
    x86.mov [rbp-200], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_3
  otherwise_default_error_2:
    x86.mov eax, [rbp-128]
    x86.mov [rbp-168], eax
    x86.mov ecx, [rbp-136]
    x86.mov [rbp-176], ecx
    x86.mov edx, [rbp-144]
    x86.mov [rbp-184], edx
    x86.mov ebx, [rbp-152]
    x86.mov [rbp-192], ebx
    x86.mov esi, [rbp-160]
    x86.mov [rbp-200], esi
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-200]
    x86.mov [rbp-208], eax
    x86.mov ecx, [rbp-192]
    x86.mov [rbp-216], ecx
    x86.mov edx, [rbp-184]
    x86.mov [rbp-224], edx
    x86.mov ebx, [rbp-176]
    x86.mov [rbp-232], ebx
    x86.mov esi, [rbp-168]
    x86.mov [rbp-240], esi
    x86.lea rdi, [rbp-240]
    x86.mov rcx, rdi
    x86.call stdlib.Parsing.__int_fromString
    x86.xor r8, r8
    x86.mov [rbp-248], r8
    x86.xor r9, r9
    x86.cmp edx, r9
    x86.je advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-248]
    x86.jmp advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, 3
    x86.mov rcx, rax
    x86.call advent.multiply
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: day4b -->
<!-- Args: 3 -->
```maxon
function multiply(x int) returns int
    return x * 2
end 'multiply'

function main() returns int
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
    %2 = maxon.binop %0, %1 {op = mul} {kind = i64}
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
    %11 = maxon.binop %7, %10 {op = ne} {kind = i64}
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
    %19 = maxon.binop %15, %18 {op = ne} {kind = i64}
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
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, __sret_3.managed.element_size
    %4 = arith.constant {value = 0 : i64}
    memref.store %4, __sret_3.managed.capacity
    %5 = arith.constant {value = 0 : i64}
    memref.store %5, __sret_3.managed.length
    %6 = arith.constant {value = 0 : i64}
    memref.store %6, __sret_3.managed.buffer
    %7 = arith.constant {value = 0 : i64}
    memref.store %7, __sret_3.iterIndex
    %8 = memref.lea __sret_3
    func.call @stdlib.CommandLine.args %8
    %9 = memref.load __sret_3.iterIndex : i64
    %10 = memref.load __sret_3.managed.buffer : i64
    %11 = memref.load __sret_3.managed.length : i64
    %12 = memref.load __sret_3.managed.capacity : i64
    %13 = memref.load __sret_3.managed.element_size : i64
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.constant {value = 0 : i64}
    memref.store %15, __sret_8._iterPos
    %16 = arith.constant {value = 0 : i64}
    memref.store %16, __sret_8._managed.element_size
    %17 = arith.constant {value = 0 : i64}
    memref.store %17, __sret_8._managed.capacity
    %18 = arith.constant {value = 0 : i64}
    memref.store %18, __sret_8._managed.length
    %19 = arith.constant {value = 0 : i64}
    memref.store %19, __sret_8._managed.buffer
    %20 = memref.lea __sret_8
    memref.store %13, __selfbuf_21.managed.element_size
    memref.store %12, __selfbuf_21.managed.capacity
    memref.store %11, __selfbuf_21.managed.length
    memref.store %10, __selfbuf_21.managed.buffer
    memref.store %9, __selfbuf_21.iterIndex
    %27 = memref.lea __selfbuf_21
    %28 = func.try_call @StringArray.get %20, %27, %14
    %34 = memref.lea_rdata __str_9
    %35 = std.ptr_to_i64 %34
    %36 = arith.constant {value = 0 : i64}
    %37 = arith.constant {value = 0 : i64}
    %38 = arith.constant {value = 1 : i64}
    %39 = arith.constant {value = 0 : i64}
    memref.store %35, __try_default_1._managed.buffer
    memref.store %36, __try_default_1._managed.length
    memref.store %37, __try_default_1._managed.capacity
    memref.store %38, __try_default_1._managed.element_size
    memref.store %39, __try_default_1._iterPos
    %45 = memref.load __sret_8._managed.buffer : i64
    memref.store %45, __try_result_0._managed.buffer
    %46 = memref.load __sret_8._managed.length : i64
    memref.store %46, __try_result_0._managed.length
    %47 = memref.load __sret_8._managed.capacity : i64
    memref.store %47, __try_result_0._managed.capacity
    %48 = memref.load __sret_8._managed.element_size : i64
    memref.store %48, __try_result_0._managed.element_size
    %49 = memref.load __sret_8._iterPos : i64
    memref.store %49, __try_result_0._iterPos
    %50 = arith.constant {value = 0 : i64}
    %51 = arith.cmpi ne %28, %50
    cf.cond_br %51 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %52 = memref.load __try_default_1._managed.buffer : i64
    memref.store %52, __try_result_0._managed.buffer
    %53 = memref.load __try_default_1._managed.length : i64
    memref.store %53, __try_result_0._managed.length
    %54 = memref.load __try_default_1._managed.capacity : i64
    memref.store %54, __try_result_0._managed.capacity
    %55 = memref.load __try_default_1._managed.element_size : i64
    memref.store %55, __try_result_0._managed.element_size
    %56 = memref.load __try_default_1._iterPos : i64
    memref.store %56, __try_result_0._iterPos
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %58 = memref.load __try_result_0._iterPos : i64
    memref.store %58, __argbuf_57._iterPos
    %59 = memref.load __try_result_0._managed.element_size : i64
    memref.store %59, __argbuf_57._managed.element_size
    %60 = memref.load __try_result_0._managed.capacity : i64
    memref.store %60, __argbuf_57._managed.capacity
    %61 = memref.load __try_result_0._managed.length : i64
    memref.store %61, __argbuf_57._managed.length
    %62 = memref.load __try_result_0._managed.buffer : i64
    memref.store %62, __argbuf_57._managed.buffer
    %63 = memref.lea __argbuf_57
    %64, %65 = func.try_call @stdlib.Parsing.__int_fromString %63
    %66 = arith.constant {value = 0 : i64}
    memref.store %66, __try_default_5
    memref.store %64, __try_result_4
    %67 = arith.constant {value = 0 : i64}
    %68 = arith.cmpi ne %65, %67
    cf.cond_br %68 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %69 = memref.load __try_default_5 : i64
    memref.store %69, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %71 = arith.constant {value = 3 : i64}
    %72 = func.call @advent.multiply %71
    func.return %72
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
    x86.prologue stack_size=256
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.xor ebx, ebx
    x86.mov [rbp-32], ebx
    x86.xor esi, esi
    x86.mov [rbp-40], esi
    x86.lea rdi, [rbp-40]
    x86.mov rcx, rdi
    x86.call stdlib.CommandLine.args
    x86.mov r8, [rbp-40]
    x86.mov r9, [rbp-32]
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-16]
    x86.mov edx, [rbp-8]
    x86.xor ebx, ebx
    x86.xor esi, esi
    x86.mov [rbp-48], esi
    x86.xor esi, esi
    x86.mov [rbp-56], esi
    x86.xor esi, esi
    x86.mov [rbp-64], esi
    x86.xor esi, esi
    x86.mov [rbp-72], esi
    x86.xor esi, esi
    x86.mov [rbp-80], esi
    x86.lea rsi, [rbp-80]
    x86.mov [rbp-88], edx
    x86.mov [rbp-96], ecx
    x86.mov [rbp-104], eax
    x86.mov [rbp-112], r9
    x86.mov [rbp-120], r8
    x86.lea rax, [rbp-120]
    x86.mov rcx, rsi
    x86.mov rdx, rax
    x86.mov r8, rbx
    x86.call StringArray.get
    x86.lea_rdata rax, [__str_9]
    x86.mov rcx, rax
    x86.xor eax, eax
    x86.xor ebx, ebx
    x86.mov esi, 1
    x86.xor edi, edi
    x86.mov [rbp-128], ecx
    x86.mov [rbp-136], eax
    x86.mov [rbp-144], ebx
    x86.mov [rbp-152], esi
    x86.mov [rbp-160], edi
    x86.mov eax, [rbp-80]
    x86.mov [rbp-168], eax
    x86.mov eax, [rbp-72]
    x86.mov [rbp-176], eax
    x86.mov eax, [rbp-64]
    x86.mov [rbp-184], eax
    x86.mov eax, [rbp-56]
    x86.mov [rbp-192], eax
    x86.mov eax, [rbp-48]
    x86.mov [rbp-200], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je advent.main.otherwise_default_continue_3
  otherwise_default_error_2:
    x86.mov eax, [rbp-128]
    x86.mov [rbp-168], eax
    x86.mov ecx, [rbp-136]
    x86.mov [rbp-176], ecx
    x86.mov edx, [rbp-144]
    x86.mov [rbp-184], edx
    x86.mov ebx, [rbp-152]
    x86.mov [rbp-192], ebx
    x86.mov esi, [rbp-160]
    x86.mov [rbp-200], esi
    x86.jmp advent.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-200]
    x86.mov [rbp-208], eax
    x86.mov ecx, [rbp-192]
    x86.mov [rbp-216], ecx
    x86.mov edx, [rbp-184]
    x86.mov [rbp-224], edx
    x86.mov ebx, [rbp-176]
    x86.mov [rbp-232], ebx
    x86.mov esi, [rbp-168]
    x86.mov [rbp-240], esi
    x86.lea rdi, [rbp-240]
    x86.mov rcx, rdi
    x86.call stdlib.Parsing.__int_fromString
    x86.xor r8, r8
    x86.mov [rbp-248], r8
    x86.xor r9, r9
    x86.cmp edx, r9
    x86.je advent.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-248]
    x86.jmp advent.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, 3
    x86.mov rcx, rax
    x86.call advent.multiply
    x86.epilogue
    x86.ret
  }
}
```
