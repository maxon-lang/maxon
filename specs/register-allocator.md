---
feature: register-allocator
status: experimental
keywords: [regalloc, registers, spilling, codegen]
category: dev
---

## Documentation

These tests exercise register allocation with progressively increasing difficulty. They are organized into six levels:

1. **Basic Value Tracking** — Single values flowing to return. A trivial allocator can pass these.
2. **Multiple Values and Reuse** — More than one live value at a time; values reused across expressions.
3. **Register Pressure and Spilling** — More live values than physical registers, forcing spills to stack.
4. **Function Calls and Fixed Register Constraints** — Caller-saved register preservation, IDIV constraints (RAX/RDX), parameter passing.
5. **Control Flow and Loops** — Values live across branches, loop back-edges, and nested control flow.
6. **Advanced Scenarios** — Combined challenges: recursion, deep expressions, mixed int/float, long live ranges, parallel copy.

## Tests

### Level 1: Basic Value Tracking

<!-- test: int-constant -->
```maxon
function main() returns ExitCode
  return 42
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 42 : i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %1
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 42 : i64}
    %3 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %3
    func.return %2
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov eax, 42
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, 42
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-var-roundtrip -->
```maxon
function main() returns ExitCode
  var x = 99
  return x
end 'main'
```
```exitcode
99
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 99 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %1 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.binop %1, %2 {op = lt}
    %4 = maxon.literal {value = 4294967295 : i64}
    %5 = maxon.binop %1, %4 {op = gt}
    %6 = maxon.binop %3, %5 {op = or}
    maxon.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-var-roundtrip.test:4: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %8 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %8
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 99 : i64}
    memref.store %2, __range_val_0
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_7
    %9 = std.ptr_to_i64 %8
    std.call_runtime @maxon_panic %9
  __range_ok_0:
    %10 = memref.load __range_val_0 : i64
    %11 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %11
    func.return %10
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 99
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.cmp ecx, edx
    x86.setl ebx
    x86.movzx ebx, ebxb
    x86.mov rsi, 4294967295
    x86.cmp rcx, rsi
    x86.setg edi
    x86.movzx edi, edib
    x86.or ebx, edi
    x86.test ebx, ebx
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_7]
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

<!-- test: int-add-constants -->
```maxon
function main() returns ExitCode
  return 30 + 12
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 30 : i64}
    %2 = maxon.literal {value = 12 : i64}
    %3 = maxon.binop %1, %2 {op = add}
    maxon.assign %3 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    %6 = maxon.literal {value = 4294967295 : i64}
    %7 = maxon.binop %3, %6 {op = gt}
    %8 = maxon.binop %5, %7 {op = or}
    maxon.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-add-constants.test:3: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %10 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %10
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 30 : i64}
    %3 = arith.constant {value = 12 : i64}
    %4 = arith.addi %2, %3
    memref.store %4, __range_val_0
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.cmpi lt %4, %5
    %7 = arith.constant {value = 4294967295 : i64}
    %8 = arith.cmpi gt %4, %7
    %9 = arith.ori1 %6, %8
    cf.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %10 = memref.lea_symdata __panic_msg_9
    %11 = std.ptr_to_i64 %10
    std.call_runtime @maxon_panic %11
  __range_ok_0:
    %12 = memref.load __range_val_0 : i64
    %13 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %13
    func.return %12
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 30
    x86.mov edx, 12
    x86.add ecx, edx
    x86.mov [rbp-16], ecx
    x86.xor ebx, ebx
    x86.cmp ecx, ebx
    x86.setl esi
    x86.movzx esi, esib
    x86.mov rdi, 4294967295
    x86.cmp rcx, rdi
    x86.setg r8
    x86.movzx r8, r8b
    x86.or esi, r8
    x86.test esi, esi
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_9]
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

### Level 2: Multiple Values and Reuse

<!-- test: int-two-vars-add -->
```maxon
function main() returns ExitCode
  var a = 30
  var b = 12
  return a + b
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 30 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 12 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add}
    maxon.assign %3 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    %6 = maxon.literal {value = 4294967295 : i64}
    %7 = maxon.binop %3, %6 {op = gt}
    %8 = maxon.binop %5, %7 {op = or}
    maxon.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-two-vars-add.test:5: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %10 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %10
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 30 : i64}
    %3 = arith.constant {value = 12 : i64}
    %4 = arith.addi %2, %3
    memref.store %4, __range_val_0
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.cmpi lt %4, %5
    %7 = arith.constant {value = 4294967295 : i64}
    %8 = arith.cmpi gt %4, %7
    %9 = arith.ori1 %6, %8
    cf.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %10 = memref.lea_symdata __panic_msg_9
    %11 = std.ptr_to_i64 %10
    std.call_runtime @maxon_panic %11
  __range_ok_0:
    %12 = memref.load __range_val_0 : i64
    %13 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %13
    func.return %12
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 30
    x86.mov edx, 12
    x86.add ecx, edx
    x86.mov [rbp-16], ecx
    x86.xor ebx, ebx
    x86.cmp ecx, ebx
    x86.setl esi
    x86.movzx esi, esib
    x86.mov rdi, 4294967295
    x86.cmp rcx, rdi
    x86.setg r8
    x86.movzx r8, r8b
    x86.or esi, r8
    x86.test esi, esi
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_9]
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

<!-- test: int-var-reuse-twice -->
```maxon
function main() returns ExitCode
  var x = 21
  return x + x
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 21 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %1, %1 {op = add}
    maxon.assign %2 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-var-reuse-twice.test:4: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %9 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %9
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 21 : i64}
    %3 = arith.addi %2, %2
    memref.store %3, __range_val_0
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.cmpi lt %3, %4
    %6 = arith.constant {value = 4294967295 : i64}
    %7 = arith.cmpi gt %3, %6
    %8 = arith.ori1 %5, %7
    cf.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %9 = memref.lea_symdata __panic_msg_8
    %10 = std.ptr_to_i64 %9
    std.call_runtime @maxon_panic %10
  __range_ok_0:
    %11 = memref.load __range_val_0 : i64
    %12 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %12
    func.return %11
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 21
    x86.add ecx, ecx
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.cmp ecx, edx
    x86.setl ebx
    x86.movzx ebx, ebxb
    x86.mov rsi, 4294967295
    x86.cmp rcx, rsi
    x86.setg edi
    x86.movzx edi, edib
    x86.or ebx, edi
    x86.test ebx, ebx
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_8]
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

<!-- test: int-chained-assignments -->
```maxon
function main() returns ExitCode
  var a = 10
  var b = a + 5
  var c = b + 7
  var d = c + 20
  return d
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 10 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 5 : i64}
    %3 = maxon.binop %1, %2 {op = add}
    maxon.assign %3 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 7 : i64}
    %5 = maxon.binop %3, %4 {op = add}
    maxon.assign %5 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 20 : i64}
    %7 = maxon.binop %5, %6 {op = add}
    maxon.assign %7 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %7 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-chained-assignments.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %14 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %14
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 10 : i64}
    %3 = arith.constant {value = 5 : i64}
    %4 = arith.addi %2, %3
    %5 = arith.constant {value = 7 : i64}
    %6 = arith.addi %4, %5
    %7 = arith.constant {value = 20 : i64}
    %8 = arith.addi %6, %7
    memref.store %8, __range_val_0
    %9 = arith.constant {value = 0 : i64}
    %10 = arith.cmpi lt %8, %9
    %11 = arith.constant {value = 4294967295 : i64}
    %12 = arith.cmpi gt %8, %11
    %13 = arith.ori1 %10, %12
    cf.cond_br %13 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %14 = memref.lea_symdata __panic_msg_13
    %15 = std.ptr_to_i64 %14
    std.call_runtime @maxon_panic %15
  __range_ok_0:
    %16 = memref.load __range_val_0 : i64
    %17 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %17
    func.return %16
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 10
    x86.mov edx, 5
    x86.add ecx, edx
    x86.mov ebx, 7
    x86.add ecx, ebx
    x86.mov esi, 20
    x86.add ecx, esi
    x86.mov [rbp-16], ecx
    x86.xor edi, edi
    x86.cmp ecx, edi
    x86.setl r8
    x86.movzx r8, r8b
    x86.mov r9, 4294967295
    x86.cmp rcx, r9
    x86.setg eax
    x86.movzx eax, eaxb
    x86.or r8, eax
    x86.test r8, r8
    x86.je register-allocator.main.__range_ok_0
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

<!-- test: int-reassignment -->
```maxon
function main() returns ExitCode
  var x = 100
  var y = x - 80
  x = 22
  return x + y
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 100 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 80 : i64}
    %3 = maxon.binop %1, %2 {op = sub}
    maxon.assign %3 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 22 : i64}
    maxon.assign %4 {var = x} {kind = i64} {mut = 1 : i1}
    %5 = maxon.binop %4, %3 {op = add}
    maxon.assign %5 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-reassignment.test:6: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %12 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %12
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 100 : i64}
    %3 = arith.constant {value = 80 : i64}
    %4 = arith.subi %2, %3
    %5 = arith.constant {value = 22 : i64}
    %6 = arith.addi %5, %4
    memref.store %6, __range_val_0
    %7 = arith.constant {value = 0 : i64}
    %8 = arith.cmpi lt %6, %7
    %9 = arith.constant {value = 4294967295 : i64}
    %10 = arith.cmpi gt %6, %9
    %11 = arith.ori1 %8, %10
    cf.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %12 = memref.lea_symdata __panic_msg_11
    %13 = std.ptr_to_i64 %12
    std.call_runtime @maxon_panic %13
  __range_ok_0:
    %14 = memref.load __range_val_0 : i64
    %15 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %15
    func.return %14
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 100
    x86.mov edx, 80
    x86.sub ecx, edx
    x86.mov ebx, 22
    x86.add ebx, ecx
    x86.mov [rbp-16], ebx
    x86.xor esi, esi
    x86.cmp ebx, esi
    x86.setl edi
    x86.movzx edi, edib
    x86.mov r8, 4294967295
    x86.cmp rbx, r8
    x86.setg r9
    x86.movzx r9, r9b
    x86.or edi, r9
    x86.test edi, edi
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_11]
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

### Level 3: Register Pressure and Spilling

<!-- test: int-six-vars-alive -->
```maxon
function main() returns ExitCode
  var a = 1
  var b = 2
  var c = 3
  var d = 4
  var e = 5
  var f = 6
  return a + b + c + d + e + f
end 'main'
```
```exitcode
21
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 2 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 4 : i64}
    maxon.assign %4 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 5 : i64}
    maxon.assign %5 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 6 : i64}
    maxon.assign %6 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.binop %1, %2 {op = add}
    %8 = maxon.binop %7, %3 {op = add}
    %9 = maxon.binop %8, %4 {op = add}
    %10 = maxon.binop %9, %5 {op = add}
    %11 = maxon.binop %10, %6 {op = add}
    maxon.assign %11 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %11, %12 {op = lt}
    %14 = maxon.literal {value = 4294967295 : i64}
    %15 = maxon.binop %11, %14 {op = gt}
    %16 = maxon.binop %13, %15 {op = or}
    maxon.cond_br %16 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-six-vars-alive.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %18 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %18
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 2 : i64}
    %4 = arith.constant {value = 3 : i64}
    %5 = arith.constant {value = 4 : i64}
    %6 = arith.constant {value = 5 : i64}
    %7 = arith.constant {value = 6 : i64}
    %8 = arith.addi %2, %3
    %9 = arith.addi %8, %4
    %10 = arith.addi %9, %5
    %11 = arith.addi %10, %6
    %12 = arith.addi %11, %7
    memref.store %12, __range_val_0
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.cmpi lt %12, %13
    %15 = arith.constant {value = 4294967295 : i64}
    %16 = arith.cmpi gt %12, %15
    %17 = arith.ori1 %14, %16
    cf.cond_br %17 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %18 = memref.lea_symdata __panic_msg_17
    %19 = std.ptr_to_i64 %18
    std.call_runtime @maxon_panic %19
  __range_ok_0:
    %20 = memref.load __range_val_0 : i64
    %21 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %21
    func.return %20
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov edx, 2
    x86.mov ebx, 3
    x86.mov esi, 4
    x86.mov edi, 5
    x86.mov r8, 6
    x86.add ecx, edx
    x86.add ecx, ebx
    x86.add ecx, esi
    x86.add ecx, edi
    x86.add ecx, r8
    x86.mov [rbp-16], ecx
    x86.xor r9, r9
    x86.cmp ecx, r9
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rdx, 4294967295
    x86.cmp rcx, rdx
    x86.setg ecx
    x86.movzx ecx, ecxb
    x86.or eax, ecx
    x86.test eax, eax
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_17]
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

<!-- test: int-ten-vars-alive -->
```maxon
function main() returns ExitCode
  var a = 1
  var b = 2
  var c = 3
  var d = 4
  var e = 5
  var f = 6
  var g = 7
  var h = 8
  var i = 9
  var j = 10
  return a + b + c + d + e + f + g + h + i + j
end 'main'
```
```exitcode
55
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 2 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 4 : i64}
    maxon.assign %4 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 5 : i64}
    maxon.assign %5 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 6 : i64}
    maxon.assign %6 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 7 : i64}
    maxon.assign %7 {var = g} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 8 : i64}
    maxon.assign %8 {var = h} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.literal {value = 9 : i64}
    maxon.assign %9 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 10 : i64}
    maxon.assign %10 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %11 = maxon.binop %1, %2 {op = add}
    %12 = maxon.binop %11, %3 {op = add}
    %13 = maxon.binop %12, %4 {op = add}
    %14 = maxon.binop %13, %5 {op = add}
    %15 = maxon.binop %14, %6 {op = add}
    %16 = maxon.binop %15, %7 {op = add}
    %17 = maxon.binop %16, %8 {op = add}
    %18 = maxon.binop %17, %9 {op = add}
    %19 = maxon.binop %18, %10 {op = add}
    maxon.assign %19 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %19, %20 {op = lt}
    %22 = maxon.literal {value = 4294967295 : i64}
    %23 = maxon.binop %19, %22 {op = gt}
    %24 = maxon.binop %21, %23 {op = or}
    maxon.cond_br %24 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-ten-vars-alive.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %26 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %26
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 2 : i64}
    %4 = arith.constant {value = 3 : i64}
    %5 = arith.constant {value = 4 : i64}
    %6 = arith.constant {value = 5 : i64}
    %7 = arith.constant {value = 6 : i64}
    %8 = arith.constant {value = 7 : i64}
    %9 = arith.constant {value = 8 : i64}
    %10 = arith.constant {value = 9 : i64}
    %11 = arith.constant {value = 10 : i64}
    %12 = arith.addi %2, %3
    %13 = arith.addi %12, %4
    %14 = arith.addi %13, %5
    %15 = arith.addi %14, %6
    %16 = arith.addi %15, %7
    %17 = arith.addi %16, %8
    %18 = arith.addi %17, %9
    %19 = arith.addi %18, %10
    %20 = arith.addi %19, %11
    memref.store %20, __range_val_0
    %21 = arith.constant {value = 0 : i64}
    %22 = arith.cmpi lt %20, %21
    %23 = arith.constant {value = 4294967295 : i64}
    %24 = arith.cmpi gt %20, %23
    %25 = arith.ori1 %22, %24
    cf.cond_br %25 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %26 = memref.lea_symdata __panic_msg_25
    %27 = std.ptr_to_i64 %26
    std.call_runtime @maxon_panic %27
  __range_ok_0:
    %28 = memref.load __range_val_0 : i64
    %29 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %29
    func.return %28
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov edx, 2
    x86.mov ebx, 3
    x86.mov esi, 4
    x86.mov edi, 5
    x86.mov r8, 6
    x86.mov r9, 7
    x86.mov eax, 8
    x86.mov ecx, 9
    x86.mov edx, 10
    x86.mov ebx, 2
    x86.mov esi, 1
    x86.add esi, ebx
    x86.mov ebx, 3
    x86.add esi, ebx
    x86.mov ebx, 4
    x86.add esi, ebx
    x86.add esi, edi
    x86.add esi, r8
    x86.add esi, r9
    x86.add esi, eax
    x86.add esi, ecx
    x86.add esi, edx
    x86.mov [rbp-16], esi
    x86.xor eax, eax
    x86.cmp esi, eax
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rcx, 4294967295
    x86.cmp rsi, rcx
    x86.setg ecx
    x86.movzx ecx, ecxb
    x86.or eax, ecx
    x86.test eax, eax
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_25]
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

<!-- test: int-sixteen-vars-spill -->
```maxon
function main() returns ExitCode
  var a = 1
  var b = 2
  var c = 3
  var d = 4
  var e = 5
  var f = 6
  var g = 7
  var h = 8
  var i = 9
  var j = 10
  var k = 11
  var l = 12
  var m = 13
  var n = 14
  var o = 15
  var p = 16
  return (a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p) mod 256
end 'main'
```
```exitcode
136
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 2 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 4 : i64}
    maxon.assign %4 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 5 : i64}
    maxon.assign %5 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 6 : i64}
    maxon.assign %6 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 7 : i64}
    maxon.assign %7 {var = g} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 8 : i64}
    maxon.assign %8 {var = h} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.literal {value = 9 : i64}
    maxon.assign %9 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 10 : i64}
    maxon.assign %10 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %11 = maxon.literal {value = 11 : i64}
    maxon.assign %11 {var = k} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 12 : i64}
    maxon.assign %12 {var = l} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.literal {value = 13 : i64}
    maxon.assign %13 {var = m} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %14 = maxon.literal {value = 14 : i64}
    maxon.assign %14 {var = n} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 15 : i64}
    maxon.assign %15 {var = o} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 16 : i64}
    maxon.assign %16 {var = p} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.binop %1, %2 {op = add}
    %18 = maxon.binop %17, %3 {op = add}
    %19 = maxon.binop %18, %4 {op = add}
    %20 = maxon.binop %19, %5 {op = add}
    %21 = maxon.binop %20, %6 {op = add}
    %22 = maxon.binop %21, %7 {op = add}
    %23 = maxon.binop %22, %8 {op = add}
    %24 = maxon.binop %23, %9 {op = add}
    %25 = maxon.binop %24, %10 {op = add}
    %26 = maxon.binop %25, %11 {op = add}
    %27 = maxon.binop %26, %12 {op = add}
    %28 = maxon.binop %27, %13 {op = add}
    %29 = maxon.binop %28, %14 {op = add}
    %30 = maxon.binop %29, %15 {op = add}
    %31 = maxon.binop %30, %16 {op = add}
    %32 = maxon.literal {value = 256 : i64}
    %33 = maxon.binop %31, %32 {op = mod}
    maxon.assign %33 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %34 = maxon.literal {value = 0 : i64}
    %35 = maxon.binop %33, %34 {op = lt}
    %36 = maxon.literal {value = 4294967295 : i64}
    %37 = maxon.binop %33, %36 {op = gt}
    %38 = maxon.binop %35, %37 {op = or}
    maxon.cond_br %38 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-sixteen-vars-spill.test:19: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %40 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %40
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 2 : i64}
    %4 = arith.constant {value = 3 : i64}
    %5 = arith.constant {value = 4 : i64}
    %6 = arith.constant {value = 5 : i64}
    %7 = arith.constant {value = 6 : i64}
    %8 = arith.constant {value = 7 : i64}
    %9 = arith.constant {value = 8 : i64}
    %10 = arith.constant {value = 9 : i64}
    %11 = arith.constant {value = 10 : i64}
    %12 = arith.constant {value = 11 : i64}
    %13 = arith.constant {value = 12 : i64}
    %14 = arith.constant {value = 13 : i64}
    %15 = arith.constant {value = 14 : i64}
    %16 = arith.constant {value = 15 : i64}
    %17 = arith.constant {value = 16 : i64}
    %18 = arith.addi %2, %3
    %19 = arith.addi %18, %4
    %20 = arith.addi %19, %5
    %21 = arith.addi %20, %6
    %22 = arith.addi %21, %7
    %23 = arith.addi %22, %8
    %24 = arith.addi %23, %9
    %25 = arith.addi %24, %10
    %26 = arith.addi %25, %11
    %27 = arith.addi %26, %12
    %28 = arith.addi %27, %13
    %29 = arith.addi %28, %14
    %30 = arith.addi %29, %15
    %31 = arith.addi %30, %16
    %32 = arith.addi %31, %17
    %33 = arith.constant {value = 256 : i64}
    %34 = arith.remsi %32, %33
    memref.store %34, __range_val_0
    %35 = arith.constant {value = 0 : i64}
    %36 = arith.cmpi lt %34, %35
    %37 = arith.constant {value = 4294967295 : i64}
    %38 = arith.cmpi gt %34, %37
    %39 = arith.ori1 %36, %38
    cf.cond_br %39 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %40 = memref.lea_symdata __panic_msg_39
    %41 = std.ptr_to_i64 %40
    std.call_runtime @maxon_panic %41
  __range_ok_0:
    %42 = memref.load __range_val_0 : i64
    %43 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %43
    func.return %42
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov edx, 2
    x86.mov ebx, 3
    x86.mov esi, 4
    x86.mov edi, 5
    x86.mov r8, 6
    x86.mov r9, 7
    x86.mov eax, 8
    x86.mov ecx, 9
    x86.mov edx, 10
    x86.mov ebx, 11
    x86.mov esi, 12
    x86.mov edi, 13
    x86.mov r8, 14
    x86.mov r9, 15
    x86.mov eax, 16
    x86.mov ecx, 2
    x86.mov edx, 1
    x86.add edx, ecx
    x86.mov ecx, 3
    x86.add edx, ecx
    x86.mov ecx, 4
    x86.add edx, ecx
    x86.mov ecx, 5
    x86.add edx, ecx
    x86.mov ecx, 6
    x86.add edx, ecx
    x86.mov ecx, 7
    x86.add edx, ecx
    x86.mov ecx, 8
    x86.add edx, ecx
    x86.mov ecx, 9
    x86.add edx, ecx
    x86.mov ecx, 10
    x86.add edx, ecx
    x86.add edx, ebx
    x86.add edx, esi
    x86.add edx, edi
    x86.add edx, r8
    x86.add edx, r9
    x86.add edx, eax
    x86.mov eax, 256
    x86.mov ecx, eax
    x86.mov [rbp-24], edx
    x86.mov eax, edx
    x86.cqo
    x86.idiv ecx
    x86.mov [rbp-16], edx
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rcx, 4294967295
    x86.cmp rdx, rcx
    x86.setg ecx
    x86.movzx ecx, ecxb
    x86.or eax, ecx
    x86.test eax, eax
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_39]
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

<!-- test: int-twenty-vars-heavy-spill -->
```maxon
function main() returns ExitCode
  var a = 1
  var b = 2
  var c = 3
  var d = 4
  var e = 5
  var f = 6
  var g = 7
  var h = 8
  var i = 9
  var j = 10
  var k = 11
  var l = 12
  var m = 13
  var n = 14
  var o = 15
  var p = 16
  var q = 17
  var r = 18
  var s = 19
  var t = 20
  return (a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p + q + r + s + t) mod 256
end 'main'
```
```exitcode
210
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 2 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 4 : i64}
    maxon.assign %4 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 5 : i64}
    maxon.assign %5 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 6 : i64}
    maxon.assign %6 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 7 : i64}
    maxon.assign %7 {var = g} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 8 : i64}
    maxon.assign %8 {var = h} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.literal {value = 9 : i64}
    maxon.assign %9 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 10 : i64}
    maxon.assign %10 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %11 = maxon.literal {value = 11 : i64}
    maxon.assign %11 {var = k} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 12 : i64}
    maxon.assign %12 {var = l} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.literal {value = 13 : i64}
    maxon.assign %13 {var = m} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %14 = maxon.literal {value = 14 : i64}
    maxon.assign %14 {var = n} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 15 : i64}
    maxon.assign %15 {var = o} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 16 : i64}
    maxon.assign %16 {var = p} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.literal {value = 17 : i64}
    maxon.assign %17 {var = q} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.literal {value = 18 : i64}
    maxon.assign %18 {var = r} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %19 = maxon.literal {value = 19 : i64}
    maxon.assign %19 {var = s} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.literal {value = 20 : i64}
    maxon.assign %20 {var = t} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %21 = maxon.binop %1, %2 {op = add}
    %22 = maxon.binop %21, %3 {op = add}
    %23 = maxon.binop %22, %4 {op = add}
    %24 = maxon.binop %23, %5 {op = add}
    %25 = maxon.binop %24, %6 {op = add}
    %26 = maxon.binop %25, %7 {op = add}
    %27 = maxon.binop %26, %8 {op = add}
    %28 = maxon.binop %27, %9 {op = add}
    %29 = maxon.binop %28, %10 {op = add}
    %30 = maxon.binop %29, %11 {op = add}
    %31 = maxon.binop %30, %12 {op = add}
    %32 = maxon.binop %31, %13 {op = add}
    %33 = maxon.binop %32, %14 {op = add}
    %34 = maxon.binop %33, %15 {op = add}
    %35 = maxon.binop %34, %16 {op = add}
    %36 = maxon.binop %35, %17 {op = add}
    %37 = maxon.binop %36, %18 {op = add}
    %38 = maxon.binop %37, %19 {op = add}
    %39 = maxon.binop %38, %20 {op = add}
    %40 = maxon.literal {value = 256 : i64}
    %41 = maxon.binop %39, %40 {op = mod}
    maxon.assign %41 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %42 = maxon.literal {value = 0 : i64}
    %43 = maxon.binop %41, %42 {op = lt}
    %44 = maxon.literal {value = 4294967295 : i64}
    %45 = maxon.binop %41, %44 {op = gt}
    %46 = maxon.binop %43, %45 {op = or}
    maxon.cond_br %46 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-twenty-vars-heavy-spill.test:23: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %48 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %48
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 2 : i64}
    %4 = arith.constant {value = 3 : i64}
    %5 = arith.constant {value = 4 : i64}
    %6 = arith.constant {value = 5 : i64}
    %7 = arith.constant {value = 6 : i64}
    %8 = arith.constant {value = 7 : i64}
    %9 = arith.constant {value = 8 : i64}
    %10 = arith.constant {value = 9 : i64}
    %11 = arith.constant {value = 10 : i64}
    %12 = arith.constant {value = 11 : i64}
    %13 = arith.constant {value = 12 : i64}
    %14 = arith.constant {value = 13 : i64}
    %15 = arith.constant {value = 14 : i64}
    %16 = arith.constant {value = 15 : i64}
    %17 = arith.constant {value = 16 : i64}
    %18 = arith.constant {value = 17 : i64}
    %19 = arith.constant {value = 18 : i64}
    %20 = arith.constant {value = 19 : i64}
    %21 = arith.constant {value = 20 : i64}
    %22 = arith.addi %2, %3
    %23 = arith.addi %22, %4
    %24 = arith.addi %23, %5
    %25 = arith.addi %24, %6
    %26 = arith.addi %25, %7
    %27 = arith.addi %26, %8
    %28 = arith.addi %27, %9
    %29 = arith.addi %28, %10
    %30 = arith.addi %29, %11
    %31 = arith.addi %30, %12
    %32 = arith.addi %31, %13
    %33 = arith.addi %32, %14
    %34 = arith.addi %33, %15
    %35 = arith.addi %34, %16
    %36 = arith.addi %35, %17
    %37 = arith.addi %36, %18
    %38 = arith.addi %37, %19
    %39 = arith.addi %38, %20
    %40 = arith.addi %39, %21
    %41 = arith.constant {value = 256 : i64}
    %42 = arith.remsi %40, %41
    memref.store %42, __range_val_0
    %43 = arith.constant {value = 0 : i64}
    %44 = arith.cmpi lt %42, %43
    %45 = arith.constant {value = 4294967295 : i64}
    %46 = arith.cmpi gt %42, %45
    %47 = arith.ori1 %44, %46
    cf.cond_br %47 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %48 = memref.lea_symdata __panic_msg_47
    %49 = std.ptr_to_i64 %48
    std.call_runtime @maxon_panic %49
  __range_ok_0:
    %50 = memref.load __range_val_0 : i64
    %51 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %51
    func.return %50
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov edx, 2
    x86.mov ebx, 3
    x86.mov esi, 4
    x86.mov edi, 5
    x86.mov r8, 6
    x86.mov r9, 7
    x86.mov eax, 8
    x86.mov ecx, 9
    x86.mov edx, 10
    x86.mov ebx, 11
    x86.mov esi, 12
    x86.mov edi, 13
    x86.mov r8, 14
    x86.mov r9, 15
    x86.mov eax, 16
    x86.mov ecx, 17
    x86.mov edx, 18
    x86.mov ebx, 19
    x86.mov esi, 20
    x86.mov edi, 2
    x86.mov r8, 1
    x86.add r8, edi
    x86.mov edi, 3
    x86.add r8, edi
    x86.mov edi, 4
    x86.add r8, edi
    x86.mov edi, 5
    x86.add r8, edi
    x86.mov edi, 6
    x86.add r8, edi
    x86.mov edi, 7
    x86.add r8, edi
    x86.mov edi, 8
    x86.add r8, edi
    x86.mov edi, 9
    x86.add r8, edi
    x86.mov edi, 10
    x86.add r8, edi
    x86.mov edi, 11
    x86.add r8, edi
    x86.mov edi, 12
    x86.add r8, edi
    x86.mov edi, 13
    x86.add r8, edi
    x86.mov edi, 14
    x86.add r8, edi
    x86.add r8, r9
    x86.add r8, eax
    x86.add r8, ecx
    x86.add r8, edx
    x86.add r8, ebx
    x86.add r8, esi
    x86.mov eax, 256
    x86.mov ecx, eax
    x86.mov eax, r8
    x86.cqo
    x86.idiv ecx
    x86.mov [rbp-16], edx
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rcx, 4294967295
    x86.cmp rdx, rcx
    x86.setg ecx
    x86.movzx ecx, ecxb
    x86.or eax, ecx
    x86.test eax, eax
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_47]
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

<!-- test: int-interleaved-lifetimes -->
```maxon
function main() returns ExitCode
  var a = 10
  var b = 20
  var ab = a + b
  var c = 30
  var d = 40
  var cd = c + d
  var e = 50
  var f = 60
  var ef = e + f
  var result = ab + cd + ef
  return result mod 256
end 'main'
```
```exitcode
210
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 10 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 20 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add}
    maxon.assign %3 {var = ab} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 30 : i64}
    maxon.assign %4 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 40 : i64}
    maxon.assign %5 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.binop %4, %5 {op = add}
    maxon.assign %6 {var = cd} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 50 : i64}
    maxon.assign %7 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 60 : i64}
    maxon.assign %8 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.binop %7, %8 {op = add}
    maxon.assign %9 {var = ef} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.binop %3, %6 {op = add}
    %11 = maxon.binop %10, %9 {op = add}
    maxon.assign %11 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 256 : i64}
    %13 = maxon.binop %11, %12 {op = mod}
    maxon.assign %13 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %13, %14 {op = lt}
    %16 = maxon.literal {value = 4294967295 : i64}
    %17 = maxon.binop %13, %16 {op = gt}
    %18 = maxon.binop %15, %17 {op = or}
    maxon.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-interleaved-lifetimes.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %20 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %20
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 10 : i64}
    %3 = arith.constant {value = 20 : i64}
    %4 = arith.addi %2, %3
    %5 = arith.constant {value = 30 : i64}
    %6 = arith.constant {value = 40 : i64}
    %7 = arith.addi %5, %6
    %8 = arith.constant {value = 50 : i64}
    %9 = arith.constant {value = 60 : i64}
    %10 = arith.addi %8, %9
    %11 = arith.addi %4, %7
    %12 = arith.addi %11, %10
    %13 = arith.constant {value = 256 : i64}
    %14 = arith.remsi %12, %13
    memref.store %14, __range_val_0
    %15 = arith.constant {value = 0 : i64}
    %16 = arith.cmpi lt %14, %15
    %17 = arith.constant {value = 4294967295 : i64}
    %18 = arith.cmpi gt %14, %17
    %19 = arith.ori1 %16, %18
    cf.cond_br %19 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %20 = memref.lea_symdata __panic_msg_19
    %21 = std.ptr_to_i64 %20
    std.call_runtime @maxon_panic %21
  __range_ok_0:
    %22 = memref.load __range_val_0 : i64
    %23 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %23
    func.return %22
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 10
    x86.mov edx, 20
    x86.add ecx, edx
    x86.mov ebx, 30
    x86.mov esi, 40
    x86.add ebx, esi
    x86.mov edi, 50
    x86.mov r8, 60
    x86.add edi, r8
    x86.add ecx, ebx
    x86.add ecx, edi
    x86.mov r9, 256
    x86.mov eax, ecx
    x86.cqo
    x86.idiv r9
    x86.mov [rbp-16], edx
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rcx, 4294967295
    x86.cmp rdx, rcx
    x86.setg ecx
    x86.movzx ecx, ecxb
    x86.or eax, ecx
    x86.test eax, eax
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_19]
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

<!-- test: int-parallel-accumulation -->
```maxon
function main() returns ExitCode
  var sum1 = 0
  var sum2 = 0
  var sum3 = 0
  sum1 = sum1 + 10
  sum2 = sum2 + 20
  sum3 = sum3 + 30
  sum1 = sum1 + 5
  sum2 = sum2 + 10
  sum3 = sum3 + 15
  return sum1 + sum2 + sum3
end 'main'
```
```exitcode
90
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = sum1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = sum2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    maxon.assign %3 {var = sum3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 10 : i64}
    %5 = maxon.binop %1, %4 {op = add}
    maxon.assign %5 {var = sum1} {kind = i64} {mut = 1 : i1}
    %6 = maxon.literal {value = 20 : i64}
    %7 = maxon.binop %2, %6 {op = add}
    maxon.assign %7 {var = sum2} {kind = i64} {mut = 1 : i1}
    %8 = maxon.literal {value = 30 : i64}
    %9 = maxon.binop %3, %8 {op = add}
    maxon.assign %9 {var = sum3} {kind = i64} {mut = 1 : i1}
    %10 = maxon.literal {value = 5 : i64}
    %11 = maxon.binop %5, %10 {op = add}
    maxon.assign %11 {var = sum1} {kind = i64} {mut = 1 : i1}
    %12 = maxon.literal {value = 10 : i64}
    %13 = maxon.binop %7, %12 {op = add}
    maxon.assign %13 {var = sum2} {kind = i64} {mut = 1 : i1}
    %14 = maxon.literal {value = 15 : i64}
    %15 = maxon.binop %9, %14 {op = add}
    maxon.assign %15 {var = sum3} {kind = i64} {mut = 1 : i1}
    %16 = maxon.binop %11, %13 {op = add}
    %17 = maxon.binop %16, %15 {op = add}
    maxon.assign %17 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.literal {value = 0 : i64}
    %19 = maxon.binop %17, %18 {op = lt}
    %20 = maxon.literal {value = 4294967295 : i64}
    %21 = maxon.binop %17, %20 {op = gt}
    %22 = maxon.binop %19, %21 {op = or}
    maxon.cond_br %22 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-parallel-accumulation.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %24 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %24
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %5 = arith.constant {value = 10 : i64}
    %6 = arith.constant {value = 20 : i64}
    %7 = arith.constant {value = 30 : i64}
    %8 = arith.constant {value = 5 : i64}
    %9 = arith.addi %5, %8
    %10 = arith.constant {value = 10 : i64}
    %11 = arith.addi %6, %10
    %12 = arith.constant {value = 15 : i64}
    %13 = arith.addi %7, %12
    %14 = arith.addi %9, %11
    %15 = arith.addi %14, %13
    memref.store %15, __range_val_0
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi lt %15, %16
    %18 = arith.constant {value = 4294967295 : i64}
    %19 = arith.cmpi gt %15, %18
    %20 = arith.ori1 %17, %19
    cf.cond_br %20 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %21 = memref.lea_symdata __panic_msg_23
    %22 = std.ptr_to_i64 %21
    std.call_runtime @maxon_panic %22
  __range_ok_0:
    %23 = memref.load __range_val_0 : i64
    %24 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %24
    func.return %23
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 10
    x86.mov edx, 20
    x86.mov ebx, 30
    x86.mov esi, 5
    x86.add ecx, esi
    x86.mov edi, 10
    x86.add edx, edi
    x86.mov r8, 15
    x86.add ebx, r8
    x86.add ecx, edx
    x86.add ecx, ebx
    x86.mov [rbp-16], ecx
    x86.xor r9, r9
    x86.cmp ecx, r9
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rdx, 4294967295
    x86.cmp rcx, rdx
    x86.setg ecx
    x86.movzx ecx, ecxb
    x86.or eax, ecx
    x86.test eax, eax
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_23]
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

### Level 4: Function Calls and Fixed Register Constraints

<!-- test: int-call-preserves-value -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getForty() returns Integer
  return 40
end 'getForty'

function main() returns ExitCode
  var x = 2
  var y = getForty()
  return x + y
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.getForty() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.getForty}
    %1 = maxon.literal {value = 40 : i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %1
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_2 = maxon.scope_enter {tag = register-allocator.main}
    %3 = maxon.literal {value = 2 : i64}
    maxon.assign %3 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.call @register-allocator.getForty
    maxon.assign %4 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %3, %4 {op = add}
    maxon.assign %5 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-call-preserves-value.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %12 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_2} {tag = return_cleanup}
    maxon.return %12
  }
}
=== standard
module {
  func @register-allocator.getForty() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 40 : i64}
    %3 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %3
    func.return %2
  }
  func @register-allocator.main() -> u32 {
  entry:
    %4 = arith.constant {value = 0 : i64}
    %5 = std.call_runtime @mm_scope_enter %4
    memref.store %5, __scope_2
    %6 = arith.constant {value = 2 : i64}
    %7 = func.call @register-allocator.getForty
    %8 = arith.addi %6, %7
    memref.store %8, __range_val_0
    %9 = arith.constant {value = 0 : i64}
    %10 = arith.cmpi lt %8, %9
    %11 = arith.constant {value = 4294967295 : i64}
    %12 = arith.cmpi gt %8, %11
    %13 = arith.ori1 %10, %12
    cf.cond_br %13 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %14 = memref.lea_symdata __panic_msg_11
    %15 = std.ptr_to_i64 %14
    std.call_runtime @maxon_panic %15
  __range_ok_0:
    %16 = memref.load __range_val_0 : i64
    %17 = memref.load __scope_2 : i64
    std.call_runtime @mm_scope_exit %17
    func.return %16
  }
}
=== x86
module {
  func @register-allocator.getForty() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov eax, 40
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, 40
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.call register-allocator.getForty
    x86.mov edx, 2
    x86.add edx, eax
    x86.mov [rbp-16], edx
    x86.xor ebx, ebx
    x86.cmp edx, ebx
    x86.setl esi
    x86.movzx esi, esib
    x86.mov rdi, 4294967295
    x86.cmp rdx, rdi
    x86.setg r8
    x86.movzx r8, r8b
    x86.or esi, r8
    x86.test esi, esi
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_11]
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

<!-- test: int-multiple-calls-preserve -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getTen() returns Integer
  return 10
end 'getTen'

function getTwo() returns Integer
  return 2
end 'getTwo'

function main() returns ExitCode
  var a = 5
  var b = getTen()
  var c = 7
  var d = getTwo()
  return a + b + c + d
end 'main'
```
```exitcode
24
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.getTen() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.getTen}
    %1 = maxon.literal {value = 10 : i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %1
  }
  func @register-allocator.getTwo() -> i64 {
  entry:
    __scope_2 = maxon.scope_enter {tag = register-allocator.getTwo}
    %3 = maxon.literal {value = 2 : i64}
    maxon.scope_exit {scope = __scope_2} {tag = return_cleanup}
    maxon.return %3
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_4 = maxon.scope_enter {tag = register-allocator.main}
    %5 = maxon.literal {value = 5 : i64}
    maxon.assign %5 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.call @register-allocator.getTen
    maxon.assign %6 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 7 : i64}
    maxon.assign %7 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.call @register-allocator.getTwo
    maxon.assign %8 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.binop %5, %6 {op = add}
    %10 = maxon.binop %9, %7 {op = add}
    %11 = maxon.binop %10, %8 {op = add}
    maxon.assign %11 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %11, %12 {op = lt}
    %14 = maxon.literal {value = 4294967295 : i64}
    %15 = maxon.binop %11, %14 {op = gt}
    %16 = maxon.binop %13, %15 {op = or}
    maxon.cond_br %16 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-multiple-calls-preserve.test:18: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %18 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %18
  }
}
=== standard
module {
  func @register-allocator.getTen() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 10 : i64}
    %3 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %3
    func.return %2
  }
  func @register-allocator.getTwo() -> i64 {
  entry:
    %4 = arith.constant {value = 0 : i64}
    %5 = std.call_runtime @mm_scope_enter %4
    memref.store %5, __scope_2
    %6 = arith.constant {value = 2 : i64}
    %7 = memref.load __scope_2 : i64
    std.call_runtime @mm_scope_exit %7
    func.return %6
  }
  func @register-allocator.main() -> u32 {
  entry:
    %8 = arith.constant {value = 0 : i64}
    %9 = std.call_runtime @mm_scope_enter %8
    memref.store %9, __scope_4
    %10 = arith.constant {value = 5 : i64}
    %11 = func.call @register-allocator.getTen
    %12 = arith.constant {value = 7 : i64}
    %13 = func.call @register-allocator.getTwo
    %14 = arith.addi %10, %11
    %15 = arith.addi %14, %12
    %16 = arith.addi %15, %13
    memref.store %16, __range_val_0
    %17 = arith.constant {value = 0 : i64}
    %18 = arith.cmpi lt %16, %17
    %19 = arith.constant {value = 4294967295 : i64}
    %20 = arith.cmpi gt %16, %19
    %21 = arith.ori1 %18, %20
    cf.cond_br %21 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %22 = memref.lea_symdata __panic_msg_17
    %23 = std.ptr_to_i64 %22
    std.call_runtime @maxon_panic %23
  __range_ok_0:
    %24 = memref.load __range_val_0 : i64
    %25 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %25
    func.return %24
  }
}
=== x86
module {
  func @register-allocator.getTen() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov eax, 10
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, 10
    x86.epilogue
    x86.ret
  }
  func @register-allocator.getTwo() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov eax, 2
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, 2
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 5
    x86.call register-allocator.getTen
    x86.mov edx, 7
    x86.mov [rbp-24], eax
    x86.call register-allocator.getTwo
    x86.mov ebx, [rbp-24]
    x86.mov esi, 5
    x86.add esi, ebx
    x86.mov edi, 7
    x86.add esi, edi
    x86.add esi, eax
    x86.mov [rbp-16], esi
    x86.xor r8, r8
    x86.cmp esi, r8
    x86.setl r9
    x86.movzx r9, r9b
    x86.mov rax, 4294967295
    x86.cmp rsi, rax
    x86.setg eax
    x86.movzx eax, eaxb
    x86.or r9, eax
    x86.test r9, r9
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_17]
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

<!-- test: int-call-result-used-later -->
```maxon

typealias Integer = int(i64.min to i64.max)

function compute() returns Integer
  return 100
end 'compute'

function main() returns ExitCode
  var a = compute()
  var b = compute()
  return (a + b) mod 256
end 'main'
```
```exitcode
200
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.compute() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.compute}
    %1 = maxon.literal {value = 100 : i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %1
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_2 = maxon.scope_enter {tag = register-allocator.main}
    %3 = maxon.call @register-allocator.compute
    maxon.assign %3 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.call @register-allocator.compute
    maxon.assign %4 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %3, %4 {op = add}
    %6 = maxon.literal {value = 256 : i64}
    %7 = maxon.binop %5, %6 {op = mod}
    maxon.assign %7 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-call-result-used-later.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %14 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_2} {tag = return_cleanup}
    maxon.return %14
  }
}
=== standard
module {
  func @register-allocator.compute() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 100 : i64}
    %3 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %3
    func.return %2
  }
  func @register-allocator.main() -> u32 {
  entry:
    %4 = arith.constant {value = 0 : i64}
    %5 = std.call_runtime @mm_scope_enter %4
    memref.store %5, __scope_2
    %6 = func.call @register-allocator.compute
    %7 = func.call @register-allocator.compute
    %8 = arith.addi %6, %7
    %9 = arith.constant {value = 256 : i64}
    %10 = arith.remsi %8, %9
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
    %19 = memref.load __scope_2 : i64
    std.call_runtime @mm_scope_exit %19
    func.return %18
  }
}
=== x86
module {
  func @register-allocator.compute() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov eax, 100
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, 100
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call register-allocator.compute
    x86.mov [rbp-24], eax
    x86.call register-allocator.compute
    x86.mov ecx, [rbp-24]
    x86.add ecx, eax
    x86.mov edx, 256
    x86.mov ebx, edx
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.mov [rbp-16], edx
    x86.xor ebx, ebx
    x86.cmp edx, ebx
    x86.setl esi
    x86.movzx esi, esib
    x86.mov rdi, 4294967295
    x86.cmp rdx, rdi
    x86.setg r8
    x86.movzx r8, r8b
    x86.or esi, r8
    x86.test esi, esi
    x86.je register-allocator.main.__range_ok_0
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

<!-- test: int-division-fixed-regs -->
```maxon
function main() returns ExitCode
  var a = 126
  var b = 3
  return a / b
end 'main'
```
```exitcode
42
```

<!-- test: int-division-preserves-other-values -->
```maxon
function main() returns ExitCode
  var x = 10
  var a = 84
  var b = 2
  var quotient = a / b
  return quotient - x
end 'main'
```
```exitcode
32
```

<!-- test: int-function-with-params -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
  return a + b
end 'add'

function main() returns ExitCode
  return add(30, b: 12)
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.add}
    %1 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %2 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %3 = maxon.binop %1, %2 {op = add} {optimalType = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %3
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_4 = maxon.scope_enter {tag = register-allocator.main}
    %5 = maxon.literal {value = 30 : i64}
    %6 = maxon.literal {value = 12 : i64}
    %7 = maxon.call @register-allocator.add %5, %6
    maxon.assign %7 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-function-with-params.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %14 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %14
  }
}
=== standard
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param a : StdI64
    %3 = func.param b : StdI64
    %4 = arith.addi %2, %3
    %5 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %5
    func.return %4
  }
  func @register-allocator.main() -> u32 {
  entry:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_4
    %8 = arith.constant {value = 30 : i64}
    %9 = arith.constant {value = 12 : i64}
    %10 = func.call @register-allocator.add %8, %9
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
  func @register-allocator.add(a: i64, b: i64) -> i64 {
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
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 30
    x86.mov edx, 12
    x86.call register-allocator.add
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
    x86.je register-allocator.main.__range_ok_0
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

<!-- test: int-mov-reg-reg-32bit -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
  return a + b
end 'add'

function main() returns ExitCode
  var x = 20
  var y = 22
  return add(y, b: x)
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.add}
    %1 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %2 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %3 = maxon.binop %1, %2 {op = add} {optimalType = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %3
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_4 = maxon.scope_enter {tag = register-allocator.main}
    %5 = maxon.literal {value = 20 : i64}
    maxon.assign %5 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 22 : i64}
    maxon.assign %6 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.call @register-allocator.add %6, %5
    maxon.assign %7 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-mov-reg-reg-32bit.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %14 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %14
  }
}
=== standard
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param a : StdI64
    %3 = func.param b : StdI64
    %4 = arith.addi %2, %3
    %5 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %5
    func.return %4
  }
  func @register-allocator.main() -> u32 {
  entry:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_4
    %8 = arith.constant {value = 20 : i64}
    %9 = arith.constant {value = 22 : i64}
    %10 = func.call @register-allocator.add %9, %8
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
  func @register-allocator.add(a: i64, b: i64) -> i64 {
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
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 20
    x86.mov edx, 22
    x86.xchg rdx, rcx
    x86.call register-allocator.add
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
    x86.je register-allocator.main.__range_ok_0
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

### Level 5: Control Flow and Loops

<!-- test: int-if-else-simple -->
```maxon
function main() returns ExitCode
  var x = 10
  if x == 10 'check'
    return 42
  end 'check' else 'other'
    return 0
  end 'other'
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 10 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 10 : i64}
    %3 = maxon.binop %1, %2 {op = eq}
    maxon.cond_br %3 [then: check_0, else: other_1]
  check_0:
    __scope_4 = maxon.scope_enter {tag = if_then}
    %5 = maxon.literal {value = 42 : i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %5
  other_1:
    __scope_6 = maxon.scope_enter {tag = else}
    %7 = maxon.literal {value = 0 : i64}
    maxon.scope_exit {scope = __scope_6} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %7
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 10 : i64}
    %3 = arith.constant {value = 10 : i64}
    %4 = arith.cmpi eq %2, %3
    cf.cond_br %4 [then: check_0, else: other_1]
  check_0:
    %5 = arith.constant {value = 0 : i64}
    %6 = std.call_runtime @mm_scope_enter %5
    memref.store %6, __scope_4
    %7 = arith.constant {value = 42 : i64}
    %8 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %8
    %9 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %9
    func.return %7
  other_1:
    %10 = arith.constant {value = 0 : i64}
    %11 = std.call_runtime @mm_scope_enter %10
    memref.store %11, __scope_6
    %12 = arith.constant {value = 0 : i64}
    %13 = memref.load __scope_6 : i64
    std.call_runtime @mm_scope_exit %13
    %14 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %14
    func.return %12
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 10
    x86.mov edx, 10
    x86.cmp ecx, edx
    x86.jne register-allocator.main.other_1
  check_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-16], eax
    x86.mov eax, 42
    x86.mov ecx, [rbp-16]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov eax, 42
    x86.epilogue
    x86.ret
  other_1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-24], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-24]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-if-else-value-survives-branch -->
```maxon
function main() returns ExitCode
  var base = 40
  var cond = 1
  var extra = 0
  if cond == 1 'check'
    extra = 2
  end 'check' else 'other'
    extra = 100
  end 'other'
  return base + extra
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 40 : i64}
    maxon.assign %1 {var = base} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 1 : i64}
    maxon.assign %2 {var = cond} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    maxon.assign %3 {var = extra} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.binop %2, %4 {op = eq}
    maxon.cond_br %5 [then: check_0, else: other_1]
  check_0:
    __scope_6 = maxon.scope_enter {tag = if_then}
    %7 = maxon.literal {value = 2 : i64}
    maxon.assign %7 {var = extra} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_6} {tag = block_exit}
    maxon.br check_0.merge
  other_1:
    __scope_8 = maxon.scope_enter {tag = else}
    %9 = maxon.literal {value = 100 : i64}
    maxon.assign %9 {var = extra} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_8} {tag = block_exit}
    maxon.br check_0.merge
  check_0.merge:
    %10 = maxon.var_ref {var = base} {type = i64}
    %11 = maxon.var_ref {var = extra} {type = i64}
    %12 = maxon.binop %10, %11 {op = add}
    maxon.assign %12 {var = __range_val_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.literal {value = 0 : i64}
    %14 = maxon.binop %12, %13 {op = lt}
    %15 = maxon.literal {value = 4294967295 : i64}
    %16 = maxon.binop %12, %15 {op = gt}
    %17 = maxon.binop %14, %16 {op = or}
    maxon.cond_br %17 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at int-if-else-value-survives-branch.test:11: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    %19 = maxon.var_ref {var = __range_val_2} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %19
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 40 : i64}
    memref.store %2, base
    %3 = arith.constant {value = 1 : i64}
    %5 = arith.constant {value = 1 : i64}
    %6 = arith.cmpi eq %3, %5
    cf.cond_br %6 [then: check_0, else: other_1]
  check_0:
    %7 = arith.constant {value = 0 : i64}
    %8 = std.call_runtime @mm_scope_enter %7
    memref.store %8, __scope_6
    %9 = arith.constant {value = 2 : i64}
    memref.store %9, extra
    %10 = memref.load __scope_6 : i64
    std.call_runtime @mm_scope_exit %10
    cf.br check_0.merge
  other_1:
    %11 = arith.constant {value = 0 : i64}
    %12 = std.call_runtime @mm_scope_enter %11
    memref.store %12, __scope_8
    %13 = arith.constant {value = 100 : i64}
    memref.store %13, extra
    %14 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %14
    cf.br check_0.merge
  check_0.merge:
    %15 = memref.load base : i64
    %16 = memref.load extra : i64
    %17 = arith.addi %15, %16
    memref.store %17, __range_val_2
    %18 = arith.constant {value = 0 : i64}
    %19 = arith.cmpi lt %17, %18
    %20 = arith.constant {value = 4294967295 : i64}
    %21 = arith.cmpi gt %17, %20
    %22 = arith.ori1 %19, %21
    cf.cond_br %22 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %23 = memref.lea_symdata __panic_msg_18
    %24 = std.ptr_to_i64 %23
    std.call_runtime @maxon_panic %24
  __range_ok_2:
    %25 = memref.load __range_val_2 : i64
    %26 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %26
    func.return %25
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 40
    x86.mov [rbp-16], ecx
    x86.mov edx, 1
    x86.mov ebx, 1
    x86.cmp edx, ebx
    x86.jne register-allocator.main.other_1
  check_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-24], eax
    x86.mov ecx, 2
    x86.mov [rbp-32], ecx
    x86.mov edx, [rbp-24]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.check_0.merge
  other_1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.mov ecx, 100
    x86.mov [rbp-32], ecx
    x86.mov edx, [rbp-40]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.check_0.merge
  check_0.merge:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-32]
    x86.add eax, ecx
    x86.mov [rbp-48], eax
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
    x86.je register-allocator.main.__range_ok_2
  __range_panic_2:
    x86.lea_symdata rax, [__panic_msg_18]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_2:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-56], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-56]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-while-loop-counter -->
```maxon
function main() returns ExitCode
  var i = 0
  while i < 42 'loop'
    i = i + 1
  end 'loop'
  return i
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %2 = maxon.literal {value = 42 : i64}
    %3 = maxon.var_ref {var = i} {type = i64}
    %4 = maxon.binop %3, %2 {op = lt}
    maxon.cond_br %4 [then: loop_0, else: loop_0.exit]
  loop_0:
    __scope_5 = maxon.scope_enter {tag = while}
    %6 = maxon.literal {value = 1 : i64}
    %7 = maxon.var_ref {var = i} {type = i64}
    %8 = maxon.binop %7, %6 {op = add}
    maxon.assign %8 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_5} {tag = block_exit}
    maxon.br loop_0.header
  loop_0.exit:
    %9 = maxon.var_ref {var = i} {type = i64}
    maxon.assign %9 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %9, %10 {op = lt}
    %12 = maxon.literal {value = 4294967295 : i64}
    %13 = maxon.binop %9, %12 {op = gt}
    %14 = maxon.binop %11, %13 {op = or}
    maxon.cond_br %14 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at int-while-loop-counter.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %16 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %16
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, i
    cf.br loop_0.header
  loop_0.header:
    %3 = arith.constant {value = 42 : i64}
    %4 = memref.load i : i64
    %5 = arith.cmpi lt %4, %3
    cf.cond_br %5 [then: loop_0, else: loop_0.exit]
  loop_0:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_5
    %8 = arith.constant {value = 1 : i64}
    %9 = memref.load i : i64
    %10 = arith.addi %9, %8
    memref.store %10, i
    %11 = memref.load __scope_5 : i64
    std.call_runtime @mm_scope_exit %11
    cf.br loop_0.header
  loop_0.exit:
    %12 = memref.load i : i64
    memref.store %12, __range_val_1
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.cmpi lt %12, %13
    %15 = arith.constant {value = 4294967295 : i64}
    %16 = arith.cmpi gt %12, %15
    %17 = arith.ori1 %14, %16
    cf.cond_br %17 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %18 = memref.lea_symdata __panic_msg_15
    %19 = std.ptr_to_i64 %18
    std.call_runtime @maxon_panic %19
  __range_ok_1:
    %20 = memref.load __range_val_1 : i64
    %21 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %21
    func.return %20
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 42
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-24], eax
    x86.mov ecx, 1
    x86.mov edx, [rbp-16]
    x86.add edx, ecx
    x86.mov [rbp-16], edx
    x86.mov rcx, [rbp-24]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-32], eax
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
    x86.je register-allocator.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_15]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-40], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-40]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-while-loop-accumulator -->
```maxon
function main() returns ExitCode
  var sum = 0
  var i = 0
  while i < 10 'loop'
    sum = sum + i
    i = i + 1
  end 'loop'
  return sum mod 256
end 'main'
```
```exitcode
45
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = sum} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %3 = maxon.literal {value = 10 : i64}
    %4 = maxon.var_ref {var = i} {type = i64}
    %5 = maxon.binop %4, %3 {op = lt}
    maxon.cond_br %5 [then: loop_0, else: loop_0.exit]
  loop_0:
    __scope_6 = maxon.scope_enter {tag = while}
    %7 = maxon.var_ref {var = sum} {type = i64}
    %8 = maxon.var_ref {var = i} {type = i64}
    %9 = maxon.binop %7, %8 {op = add}
    maxon.assign %9 {var = sum} {kind = i64} {mut = 1 : i1}
    %10 = maxon.literal {value = 1 : i64}
    %11 = maxon.var_ref {var = i} {type = i64}
    %12 = maxon.binop %11, %10 {op = add}
    maxon.assign %12 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_6} {tag = block_exit}
    maxon.br loop_0.header
  loop_0.exit:
    %13 = maxon.literal {value = 256 : i64}
    %14 = maxon.var_ref {var = sum} {type = i64}
    %15 = maxon.binop %14, %13 {op = mod}
    maxon.assign %15 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 0 : i64}
    %17 = maxon.binop %15, %16 {op = lt}
    %18 = maxon.literal {value = 4294967295 : i64}
    %19 = maxon.binop %15, %18 {op = gt}
    %20 = maxon.binop %17, %19 {op = or}
    maxon.cond_br %20 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at int-while-loop-accumulator.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %22 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %22
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, sum
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, i
    cf.br loop_0.header
  loop_0.header:
    %4 = arith.constant {value = 10 : i64}
    %5 = memref.load i : i64
    %6 = arith.cmpi lt %5, %4
    cf.cond_br %6 [then: loop_0, else: loop_0.exit]
  loop_0:
    %7 = arith.constant {value = 0 : i64}
    %8 = std.call_runtime @mm_scope_enter %7
    memref.store %8, __scope_6
    %9 = memref.load sum : i64
    %10 = memref.load i : i64
    %11 = arith.addi %9, %10
    memref.store %11, sum
    %12 = arith.constant {value = 1 : i64}
    %13 = memref.load i : i64
    %14 = arith.addi %13, %12
    memref.store %14, i
    %15 = memref.load __scope_6 : i64
    std.call_runtime @mm_scope_exit %15
    cf.br loop_0.header
  loop_0.exit:
    %16 = arith.constant {value = 256 : i64}
    %17 = memref.load sum : i64
    %18 = arith.remsi %17, %16
    memref.store %18, __range_val_1
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 4294967295 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %24 = memref.lea_symdata __panic_msg_21
    %25 = std.ptr_to_i64 %24
    std.call_runtime @maxon_panic %25
  __range_ok_1:
    %26 = memref.load __range_val_1 : i64
    %27 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %27
    func.return %26
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 10
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-32], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [rbp-24]
    x86.add ecx, edx
    x86.mov [rbp-16], ecx
    x86.mov ebx, 1
    x86.mov esi, [rbp-24]
    x86.add esi, ebx
    x86.mov [rbp-24], esi
    x86.mov rcx, [rbp-32]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.loop_0.header
  loop_0.exit:
    x86.mov eax, 256
    x86.mov ecx, [rbp-16]
    x86.mov ebx, eax
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.mov [rbp-40], edx
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rcx, 4294967295
    x86.cmp rdx, rcx
    x86.setg edx
    x86.movzx edx, edxb
    x86.or eax, edx
    x86.test eax, eax
    x86.je register-allocator.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_21]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-40]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-48], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-48]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-while-loop-multiple-accumulators -->
```maxon
function main() returns ExitCode
  var even_sum = 0
  var odd_sum = 0
  var count = 0
  var i = 0
  while i < 20 'loop'
    if i mod 2 == 0 'even'
      even_sum = even_sum + i
      count = count + 1
    end 'even' else 'odd'
      odd_sum = odd_sum + i
    end 'odd'
    i = i + 1
  end 'loop'
  return (even_sum + odd_sum + count) mod 256
end 'main'
```
```exitcode
200
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = even_sum} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = odd_sum} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    maxon.assign %3 {var = count} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    maxon.assign %4 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %5 = maxon.literal {value = 20 : i64}
    %6 = maxon.var_ref {var = i} {type = i64}
    %7 = maxon.binop %6, %5 {op = lt}
    maxon.cond_br %7 [then: loop_0, else: loop_0.exit]
  loop_0:
    __scope_8 = maxon.scope_enter {tag = while}
    %9 = maxon.literal {value = 2 : i64}
    %10 = maxon.var_ref {var = i} {type = i64}
    %11 = maxon.binop %10, %9 {op = mod}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %11, %12 {op = eq}
    maxon.cond_br %13 [then: even_1, else: odd_2]
  even_1:
    __scope_14 = maxon.scope_enter {tag = if_then}
    %15 = maxon.var_ref {var = even_sum} {type = i64}
    %16 = maxon.var_ref {var = i} {type = i64}
    %17 = maxon.binop %15, %16 {op = add}
    maxon.assign %17 {var = even_sum} {kind = i64} {mut = 1 : i1}
    %18 = maxon.literal {value = 1 : i64}
    %19 = maxon.var_ref {var = count} {type = i64}
    %20 = maxon.binop %19, %18 {op = add}
    maxon.assign %20 {var = count} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_14} {tag = block_exit}
    maxon.br even_1.merge
  odd_2:
    __scope_21 = maxon.scope_enter {tag = else}
    %22 = maxon.var_ref {var = odd_sum} {type = i64}
    %23 = maxon.var_ref {var = i} {type = i64}
    %24 = maxon.binop %22, %23 {op = add}
    maxon.assign %24 {var = odd_sum} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_21} {tag = block_exit}
    maxon.br even_1.merge
  even_1.merge:
    %25 = maxon.literal {value = 1 : i64}
    %26 = maxon.var_ref {var = i} {type = i64}
    %27 = maxon.binop %26, %25 {op = add}
    maxon.assign %27 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_8} {tag = block_exit}
    maxon.br loop_0.header
  loop_0.exit:
    %28 = maxon.var_ref {var = even_sum} {type = i64}
    %29 = maxon.var_ref {var = odd_sum} {type = i64}
    %30 = maxon.binop %28, %29 {op = add}
    %31 = maxon.var_ref {var = count} {type = i64}
    %32 = maxon.binop %30, %31 {op = add}
    %33 = maxon.literal {value = 256 : i64}
    %34 = maxon.binop %32, %33 {op = mod}
    maxon.assign %34 {var = __range_val_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %35 = maxon.literal {value = 0 : i64}
    %36 = maxon.binop %34, %35 {op = lt}
    %37 = maxon.literal {value = 4294967295 : i64}
    %38 = maxon.binop %34, %37 {op = gt}
    %39 = maxon.binop %36, %38 {op = or}
    maxon.cond_br %39 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    maxon.panic "panic at int-while-loop-multiple-accumulators.test:16: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_3:
    %41 = maxon.var_ref {var = __range_val_3} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %41
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, even_sum
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, odd_sum
    %4 = arith.constant {value = 0 : i64}
    memref.store %4, count
    %5 = arith.constant {value = 0 : i64}
    memref.store %5, i
    cf.br loop_0.header
  loop_0.header:
    %6 = arith.constant {value = 20 : i64}
    %7 = memref.load i : i64
    %8 = arith.cmpi lt %7, %6
    cf.cond_br %8 [then: loop_0, else: loop_0.exit]
  loop_0:
    %9 = arith.constant {value = 0 : i64}
    %10 = std.call_runtime @mm_scope_enter %9
    memref.store %10, __scope_8
    %11 = arith.constant {value = 2 : i64}
    %12 = memref.load i : i64
    %13 = arith.remsi %12, %11
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi eq %13, %14
    cf.cond_br %15 [then: even_1, else: odd_2]
  even_1:
    %16 = arith.constant {value = 0 : i64}
    %17 = std.call_runtime @mm_scope_enter %16
    memref.store %17, __scope_14
    %18 = memref.load even_sum : i64
    %19 = memref.load i : i64
    %20 = arith.addi %18, %19
    memref.store %20, even_sum
    %21 = arith.constant {value = 1 : i64}
    %22 = memref.load count : i64
    %23 = arith.addi %22, %21
    memref.store %23, count
    %24 = memref.load __scope_14 : i64
    std.call_runtime @mm_scope_exit %24
    cf.br even_1.merge
  odd_2:
    %25 = arith.constant {value = 0 : i64}
    %26 = std.call_runtime @mm_scope_enter %25
    memref.store %26, __scope_21
    %27 = memref.load odd_sum : i64
    %28 = memref.load i : i64
    %29 = arith.addi %27, %28
    memref.store %29, odd_sum
    %30 = memref.load __scope_21 : i64
    std.call_runtime @mm_scope_exit %30
    cf.br even_1.merge
  even_1.merge:
    %31 = arith.constant {value = 1 : i64}
    %32 = memref.load i : i64
    %33 = arith.addi %32, %31
    memref.store %33, i
    %34 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %34
    cf.br loop_0.header
  loop_0.exit:
    %35 = memref.load even_sum : i64
    %36 = memref.load odd_sum : i64
    %37 = arith.addi %35, %36
    %38 = memref.load count : i64
    %39 = arith.addi %37, %38
    %40 = arith.constant {value = 256 : i64}
    %41 = arith.remsi %39, %40
    memref.store %41, __range_val_3
    %42 = arith.constant {value = 0 : i64}
    %43 = arith.cmpi lt %41, %42
    %44 = arith.constant {value = 4294967295 : i64}
    %45 = arith.cmpi gt %41, %44
    %46 = arith.ori1 %43, %45
    cf.cond_br %46 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    %47 = memref.lea_symdata __panic_msg_40
    %48 = std.ptr_to_i64 %47
    std.call_runtime @maxon_panic %48
  __range_ok_3:
    %49 = memref.load __range_val_3 : i64
    %50 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %50
    func.return %49
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=80
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.xor ebx, ebx
    x86.mov [rbp-32], ebx
    x86.xor esi, esi
    x86.mov [rbp-40], esi
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 20
    x86.mov ecx, [rbp-40]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-48], eax
    x86.mov ecx, 2
    x86.mov edx, [rbp-40]
    x86.mov [rbp-80], edx
    x86.mov eax, edx
    x86.cqo
    x86.idiv ecx
    x86.xor ebx, ebx
    x86.cmp edx, ebx
    x86.jne register-allocator.main.odd_2
  even_1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-56], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [rbp-40]
    x86.add ecx, edx
    x86.mov [rbp-16], ecx
    x86.mov ebx, 1
    x86.mov esi, [rbp-32]
    x86.add esi, ebx
    x86.mov [rbp-32], esi
    x86.mov rcx, [rbp-56]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.even_1.merge
  odd_2:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-64], eax
    x86.mov ecx, [rbp-24]
    x86.mov edx, [rbp-40]
    x86.add ecx, edx
    x86.mov [rbp-24], ecx
    x86.mov rcx, [rbp-64]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.even_1.merge
  even_1.merge:
    x86.mov eax, 1
    x86.mov ecx, [rbp-40]
    x86.add ecx, eax
    x86.mov [rbp-40], ecx
    x86.mov edx, [rbp-48]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-24]
    x86.add eax, ecx
    x86.mov edx, [rbp-32]
    x86.add eax, edx
    x86.mov ebx, 256
    x86.mov [rbp-80], eax
    x86.cqo
    x86.idiv ebx
    x86.mov [rbp-72], edx
    x86.xor esi, esi
    x86.cmp edx, esi
    x86.setl edi
    x86.movzx edi, edib
    x86.mov r8, 4294967295
    x86.cmp rdx, r8
    x86.setg r9
    x86.movzx r9, r9b
    x86.or edi, r9
    x86.test edi, edi
    x86.je register-allocator.main.__range_ok_3
  __range_panic_3:
    x86.lea_symdata rax, [__panic_msg_40]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_3:
    x86.mov eax, [rbp-72]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-80], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-80]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-nested-if-in-loop -->
```maxon
function main() returns ExitCode
  var result = 0
  var i = 1
  while i <= 10 'loop'
    if i <= 5 'first'
      result = result + i
    end 'first' else 'second'
      result = result + i * 2
    end 'second'
    i = i + 1
  end 'loop'
  return result mod 256
end 'main'
```
```exitcode
95
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 1 : i64}
    maxon.assign %2 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %3 = maxon.literal {value = 10 : i64}
    %4 = maxon.var_ref {var = i} {type = i64}
    %5 = maxon.binop %4, %3 {op = le}
    maxon.cond_br %5 [then: loop_0, else: loop_0.exit]
  loop_0:
    __scope_6 = maxon.scope_enter {tag = while}
    %7 = maxon.literal {value = 5 : i64}
    %8 = maxon.var_ref {var = i} {type = i64}
    %9 = maxon.binop %8, %7 {op = le}
    maxon.cond_br %9 [then: first_1, else: second_2]
  first_1:
    __scope_10 = maxon.scope_enter {tag = if_then}
    %11 = maxon.var_ref {var = result} {type = i64}
    %12 = maxon.var_ref {var = i} {type = i64}
    %13 = maxon.binop %11, %12 {op = add}
    maxon.assign %13 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_10} {tag = block_exit}
    maxon.br first_1.merge
  second_2:
    __scope_14 = maxon.scope_enter {tag = else}
    %15 = maxon.literal {value = 2 : i64}
    %16 = maxon.var_ref {var = i} {type = i64}
    %17 = maxon.binop %16, %15 {op = mul}
    %18 = maxon.var_ref {var = result} {type = i64}
    %19 = maxon.binop %18, %17 {op = add}
    maxon.assign %19 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_14} {tag = block_exit}
    maxon.br first_1.merge
  first_1.merge:
    %20 = maxon.literal {value = 1 : i64}
    %21 = maxon.var_ref {var = i} {type = i64}
    %22 = maxon.binop %21, %20 {op = add}
    maxon.assign %22 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_6} {tag = block_exit}
    maxon.br loop_0.header
  loop_0.exit:
    %23 = maxon.literal {value = 256 : i64}
    %24 = maxon.var_ref {var = result} {type = i64}
    %25 = maxon.binop %24, %23 {op = mod}
    maxon.assign %25 {var = __range_val_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %25, %26 {op = lt}
    %28 = maxon.literal {value = 4294967295 : i64}
    %29 = maxon.binop %25, %28 {op = gt}
    %30 = maxon.binop %27, %29 {op = or}
    maxon.cond_br %30 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    maxon.panic "panic at int-nested-if-in-loop.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_3:
    %32 = maxon.var_ref {var = __range_val_3} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %32
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, result
    %3 = arith.constant {value = 1 : i64}
    memref.store %3, i
    cf.br loop_0.header
  loop_0.header:
    %4 = arith.constant {value = 10 : i64}
    %5 = memref.load i : i64
    %6 = arith.cmpi le %5, %4
    cf.cond_br %6 [then: loop_0, else: loop_0.exit]
  loop_0:
    %7 = arith.constant {value = 0 : i64}
    %8 = std.call_runtime @mm_scope_enter %7
    memref.store %8, __scope_6
    %9 = arith.constant {value = 5 : i64}
    %10 = memref.load i : i64
    %11 = arith.cmpi le %10, %9
    cf.cond_br %11 [then: first_1, else: second_2]
  first_1:
    %12 = arith.constant {value = 0 : i64}
    %13 = std.call_runtime @mm_scope_enter %12
    memref.store %13, __scope_10
    %14 = memref.load result : i64
    %15 = memref.load i : i64
    %16 = arith.addi %14, %15
    memref.store %16, result
    %17 = memref.load __scope_10 : i64
    std.call_runtime @mm_scope_exit %17
    cf.br first_1.merge
  second_2:
    %18 = arith.constant {value = 0 : i64}
    %19 = std.call_runtime @mm_scope_enter %18
    memref.store %19, __scope_14
    %20 = arith.constant {value = 2 : i64}
    %21 = memref.load i : i64
    %22 = arith.muli %21, %20
    %23 = memref.load result : i64
    %24 = arith.addi %23, %22
    memref.store %24, result
    %25 = memref.load __scope_14 : i64
    std.call_runtime @mm_scope_exit %25
    cf.br first_1.merge
  first_1.merge:
    %26 = arith.constant {value = 1 : i64}
    %27 = memref.load i : i64
    %28 = arith.addi %27, %26
    memref.store %28, i
    %29 = memref.load __scope_6 : i64
    std.call_runtime @mm_scope_exit %29
    cf.br loop_0.header
  loop_0.exit:
    %30 = arith.constant {value = 256 : i64}
    %31 = memref.load result : i64
    %32 = arith.remsi %31, %30
    memref.store %32, __range_val_3
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.cmpi lt %32, %33
    %35 = arith.constant {value = 4294967295 : i64}
    %36 = arith.cmpi gt %32, %35
    %37 = arith.ori1 %34, %36
    cf.cond_br %37 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    %38 = memref.lea_symdata __panic_msg_31
    %39 = std.ptr_to_i64 %38
    std.call_runtime @maxon_panic %39
  __range_ok_3:
    %40 = memref.load __range_val_3 : i64
    %41 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %41
    func.return %40
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.mov edx, 1
    x86.mov [rbp-24], edx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 10
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jg register-allocator.main.loop_0.exit
  loop_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-32], eax
    x86.mov ecx, 5
    x86.mov edx, [rbp-24]
    x86.cmp edx, ecx
    x86.jg register-allocator.main.second_2
  first_1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [rbp-24]
    x86.add ecx, edx
    x86.mov [rbp-16], ecx
    x86.mov rcx, [rbp-40]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.first_1.merge
  second_2:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-48], eax
    x86.mov ecx, 2
    x86.mov edx, [rbp-24]
    x86.imul edx, ecx
    x86.mov ebx, [rbp-16]
    x86.add ebx, edx
    x86.mov [rbp-16], ebx
    x86.mov rcx, [rbp-48]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.first_1.merge
  first_1.merge:
    x86.mov eax, 1
    x86.mov ecx, [rbp-24]
    x86.add ecx, eax
    x86.mov [rbp-24], ecx
    x86.mov edx, [rbp-32]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.loop_0.header
  loop_0.exit:
    x86.mov eax, 256
    x86.mov ecx, [rbp-16]
    x86.mov ebx, eax
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.mov [rbp-56], edx
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rcx, 4294967295
    x86.cmp rdx, rcx
    x86.setg edx
    x86.movzx edx, edxb
    x86.or eax, edx
    x86.test eax, eax
    x86.je register-allocator.main.__range_ok_3
  __range_panic_3:
    x86.lea_symdata rax, [__panic_msg_31]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_3:
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-64], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-64]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-nested-loops -->
```maxon
function main() returns ExitCode
  var total = 0
  var i = 0
  while i < 5 'outer'
    var j = 0
    while j < 4 'inner'
      total = total + 1
      j = j + 1
    end 'inner'
    i = i + 1
  end 'outer'
  return total
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = total} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br outer_0.header
  outer_0.header:
    %3 = maxon.literal {value = 5 : i64}
    %4 = maxon.var_ref {var = i} {type = i64}
    %5 = maxon.binop %4, %3 {op = lt}
    maxon.cond_br %5 [then: outer_0, else: outer_0.exit]
  outer_0:
    __scope_6 = maxon.scope_enter {tag = while}
    %7 = maxon.literal {value = 0 : i64}
    maxon.assign %7 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.header:
    %8 = maxon.literal {value = 4 : i64}
    %9 = maxon.var_ref {var = j} {type = i64}
    %10 = maxon.binop %9, %8 {op = lt}
    maxon.cond_br %10 [then: inner_1, else: inner_1.exit]
  inner_1:
    __scope_11 = maxon.scope_enter {tag = while}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = total} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = total} {kind = i64} {mut = 1 : i1}
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = j} {type = i64}
    %17 = maxon.binop %16, %15 {op = add}
    maxon.assign %17 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_11} {tag = block_exit}
    maxon.br inner_1.header
  inner_1.exit:
    %18 = maxon.literal {value = 1 : i64}
    %19 = maxon.var_ref {var = i} {type = i64}
    %20 = maxon.binop %19, %18 {op = add}
    maxon.assign %20 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_6} {tag = block_exit}
    maxon.br outer_0.header
  outer_0.exit:
    %21 = maxon.var_ref {var = total} {type = i64}
    maxon.assign %21 {var = __range_val_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at int-nested-loops.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    %28 = maxon.var_ref {var = __range_val_2} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %28
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, total
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, i
    cf.br outer_0.header
  outer_0.header:
    %4 = arith.constant {value = 5 : i64}
    %5 = memref.load i : i64
    %6 = arith.cmpi lt %5, %4
    cf.cond_br %6 [then: outer_0, else: outer_0.exit]
  outer_0:
    %7 = arith.constant {value = 0 : i64}
    %8 = std.call_runtime @mm_scope_enter %7
    memref.store %8, __scope_6
    %9 = arith.constant {value = 0 : i64}
    memref.store %9, j
    cf.br inner_1.header
  inner_1.header:
    %10 = arith.constant {value = 4 : i64}
    %11 = memref.load j : i64
    %12 = arith.cmpi lt %11, %10
    cf.cond_br %12 [then: inner_1, else: inner_1.exit]
  inner_1:
    %13 = arith.constant {value = 0 : i64}
    %14 = std.call_runtime @mm_scope_enter %13
    memref.store %14, __scope_11
    %15 = arith.constant {value = 1 : i64}
    %16 = memref.load total : i64
    %17 = arith.addi %16, %15
    memref.store %17, total
    %18 = arith.constant {value = 1 : i64}
    %19 = memref.load j : i64
    %20 = arith.addi %19, %18
    memref.store %20, j
    %21 = memref.load __scope_11 : i64
    std.call_runtime @mm_scope_exit %21
    cf.br inner_1.header
  inner_1.exit:
    %22 = arith.constant {value = 1 : i64}
    %23 = memref.load i : i64
    %24 = arith.addi %23, %22
    memref.store %24, i
    %25 = memref.load __scope_6 : i64
    std.call_runtime @mm_scope_exit %25
    cf.br outer_0.header
  outer_0.exit:
    %26 = memref.load total : i64
    memref.store %26, __range_val_2
    %27 = arith.constant {value = 0 : i64}
    %28 = arith.cmpi lt %26, %27
    %29 = arith.constant {value = 4294967295 : i64}
    %30 = arith.cmpi gt %26, %29
    %31 = arith.ori1 %28, %30
    cf.cond_br %31 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %32 = memref.lea_symdata __panic_msg_27
    %33 = std.ptr_to_i64 %32
    std.call_runtime @maxon_panic %33
  __range_ok_2:
    %34 = memref.load __range_val_2 : i64
    %35 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %35
    func.return %34
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.jmp register-allocator.main.outer_0.header
  outer_0.header:
    x86.mov eax, 5
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.outer_0.exit
  outer_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-32], eax
    x86.xor ecx, ecx
    x86.mov [rbp-40], ecx
    x86.jmp register-allocator.main.inner_1.header
  inner_1.header:
    x86.mov eax, 4
    x86.mov ecx, [rbp-40]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.inner_1.exit
  inner_1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-48], eax
    x86.mov ecx, 1
    x86.mov edx, [rbp-16]
    x86.add edx, ecx
    x86.mov [rbp-16], edx
    x86.mov ebx, 1
    x86.mov esi, [rbp-40]
    x86.add esi, ebx
    x86.mov [rbp-40], esi
    x86.mov rcx, [rbp-48]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.inner_1.header
  inner_1.exit:
    x86.mov eax, 1
    x86.mov ecx, [rbp-24]
    x86.add ecx, eax
    x86.mov [rbp-24], ecx
    x86.mov edx, [rbp-32]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.outer_0.header
  outer_0.exit:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-56], eax
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
    x86.je register-allocator.main.__range_ok_2
  __range_panic_2:
    x86.lea_symdata rax, [__panic_msg_27]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_2:
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-64], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-64]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-nested-loops-with-outer-var -->
```maxon
function main() returns ExitCode
  var total = 0
  var i = 1
  while i <= 5 'outer'
    var j = 1
    while j <= i 'inner'
      total = total + 1
      j = j + 1
    end 'inner'
    i = i + 1
  end 'outer'
  return total
end 'main'
```
```exitcode
15
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = total} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 1 : i64}
    maxon.assign %2 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br outer_0.header
  outer_0.header:
    %3 = maxon.literal {value = 5 : i64}
    %4 = maxon.var_ref {var = i} {type = i64}
    %5 = maxon.binop %4, %3 {op = le}
    maxon.cond_br %5 [then: outer_0, else: outer_0.exit]
  outer_0:
    __scope_6 = maxon.scope_enter {tag = while}
    %7 = maxon.literal {value = 1 : i64}
    maxon.assign %7 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.header:
    %8 = maxon.var_ref {var = j} {type = i64}
    %9 = maxon.var_ref {var = i} {type = i64}
    %10 = maxon.binop %8, %9 {op = le}
    maxon.cond_br %10 [then: inner_1, else: inner_1.exit]
  inner_1:
    __scope_11 = maxon.scope_enter {tag = while}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = total} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = total} {kind = i64} {mut = 1 : i1}
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = j} {type = i64}
    %17 = maxon.binop %16, %15 {op = add}
    maxon.assign %17 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_11} {tag = block_exit}
    maxon.br inner_1.header
  inner_1.exit:
    %18 = maxon.literal {value = 1 : i64}
    %19 = maxon.var_ref {var = i} {type = i64}
    %20 = maxon.binop %19, %18 {op = add}
    maxon.assign %20 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_6} {tag = block_exit}
    maxon.br outer_0.header
  outer_0.exit:
    %21 = maxon.var_ref {var = total} {type = i64}
    maxon.assign %21 {var = __range_val_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at int-nested-loops-with-outer-var.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    %28 = maxon.var_ref {var = __range_val_2} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %28
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, total
    %3 = arith.constant {value = 1 : i64}
    memref.store %3, i
    cf.br outer_0.header
  outer_0.header:
    %4 = arith.constant {value = 5 : i64}
    %5 = memref.load i : i64
    %6 = arith.cmpi le %5, %4
    cf.cond_br %6 [then: outer_0, else: outer_0.exit]
  outer_0:
    %7 = arith.constant {value = 0 : i64}
    %8 = std.call_runtime @mm_scope_enter %7
    memref.store %8, __scope_6
    %9 = arith.constant {value = 1 : i64}
    memref.store %9, j
    cf.br inner_1.header
  inner_1.header:
    %10 = memref.load j : i64
    %11 = memref.load i : i64
    %12 = arith.cmpi le %10, %11
    cf.cond_br %12 [then: inner_1, else: inner_1.exit]
  inner_1:
    %13 = arith.constant {value = 0 : i64}
    %14 = std.call_runtime @mm_scope_enter %13
    memref.store %14, __scope_11
    %15 = arith.constant {value = 1 : i64}
    %16 = memref.load total : i64
    %17 = arith.addi %16, %15
    memref.store %17, total
    %18 = arith.constant {value = 1 : i64}
    %19 = memref.load j : i64
    %20 = arith.addi %19, %18
    memref.store %20, j
    %21 = memref.load __scope_11 : i64
    std.call_runtime @mm_scope_exit %21
    cf.br inner_1.header
  inner_1.exit:
    %22 = arith.constant {value = 1 : i64}
    %23 = memref.load i : i64
    %24 = arith.addi %23, %22
    memref.store %24, i
    %25 = memref.load __scope_6 : i64
    std.call_runtime @mm_scope_exit %25
    cf.br outer_0.header
  outer_0.exit:
    %26 = memref.load total : i64
    memref.store %26, __range_val_2
    %27 = arith.constant {value = 0 : i64}
    %28 = arith.cmpi lt %26, %27
    %29 = arith.constant {value = 4294967295 : i64}
    %30 = arith.cmpi gt %26, %29
    %31 = arith.ori1 %28, %30
    cf.cond_br %31 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %32 = memref.lea_symdata __panic_msg_27
    %33 = std.ptr_to_i64 %32
    std.call_runtime @maxon_panic %33
  __range_ok_2:
    %34 = memref.load __range_val_2 : i64
    %35 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %35
    func.return %34
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.mov edx, 1
    x86.mov [rbp-24], edx
    x86.jmp register-allocator.main.outer_0.header
  outer_0.header:
    x86.mov eax, 5
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jg register-allocator.main.outer_0.exit
  outer_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-32], eax
    x86.mov ecx, 1
    x86.mov [rbp-40], ecx
    x86.jmp register-allocator.main.inner_1.header
  inner_1.header:
    x86.mov eax, [rbp-40]
    x86.mov ecx, [rbp-24]
    x86.cmp eax, ecx
    x86.jg register-allocator.main.inner_1.exit
  inner_1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-48], eax
    x86.mov ecx, 1
    x86.mov edx, [rbp-16]
    x86.add edx, ecx
    x86.mov [rbp-16], edx
    x86.mov ebx, 1
    x86.mov esi, [rbp-40]
    x86.add esi, ebx
    x86.mov [rbp-40], esi
    x86.mov rcx, [rbp-48]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.inner_1.header
  inner_1.exit:
    x86.mov eax, 1
    x86.mov ecx, [rbp-24]
    x86.add ecx, eax
    x86.mov [rbp-24], ecx
    x86.mov edx, [rbp-32]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.outer_0.header
  outer_0.exit:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-56], eax
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
    x86.je register-allocator.main.__range_ok_2
  __range_panic_2:
    x86.lea_symdata rax, [__panic_msg_27]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_2:
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-64], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-64]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-loop-with-function-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
  return x * 2
end 'double'

function main() returns ExitCode
  var sum = 0
  var i = 0
  while i < 5 'loop'
    sum = sum + double(i)
    i = i + 1
  end 'loop'
  return sum
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.double(x: i64) -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.double}
    %1 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %2 = maxon.literal {value = 2 : i64}
    %3 = maxon.binop %1, %2 {op = mul} {optimalType = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %3
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_4 = maxon.scope_enter {tag = register-allocator.main}
    %5 = maxon.literal {value = 0 : i64}
    maxon.assign %5 {var = sum} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 0 : i64}
    maxon.assign %6 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %7 = maxon.literal {value = 5 : i64}
    %8 = maxon.var_ref {var = i} {type = i64}
    %9 = maxon.binop %8, %7 {op = lt}
    maxon.cond_br %9 [then: loop_0, else: loop_0.exit]
  loop_0:
    __scope_10 = maxon.scope_enter {tag = while}
    %11 = maxon.var_ref {var = i} {type = i64}
    %12 = maxon.call @register-allocator.double %11
    %13 = maxon.var_ref {var = sum} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = sum} {kind = i64} {mut = 1 : i1}
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = i} {type = i64}
    %17 = maxon.binop %16, %15 {op = add}
    maxon.assign %17 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_10} {tag = block_exit}
    maxon.br loop_0.header
  loop_0.exit:
    %18 = maxon.var_ref {var = sum} {type = i64}
    maxon.assign %18 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %19 = maxon.literal {value = 0 : i64}
    %20 = maxon.binop %18, %19 {op = lt}
    %21 = maxon.literal {value = 4294967295 : i64}
    %22 = maxon.binop %18, %21 {op = gt}
    %23 = maxon.binop %20, %22 {op = or}
    maxon.cond_br %23 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at int-loop-with-function-call.test:16: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %25 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %25
  }
}
=== standard
module {
  func @register-allocator.double(x: i64) -> i64 {
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
  func @register-allocator.main() -> u32 {
  entry:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_4
    %8 = arith.constant {value = 0 : i64}
    memref.store %8, sum
    %9 = arith.constant {value = 0 : i64}
    memref.store %9, i
    cf.br loop_0.header
  loop_0.header:
    %10 = arith.constant {value = 5 : i64}
    %11 = memref.load i : i64
    %12 = arith.cmpi lt %11, %10
    cf.cond_br %12 [then: loop_0, else: loop_0.exit]
  loop_0:
    %13 = arith.constant {value = 0 : i64}
    %14 = std.call_runtime @mm_scope_enter %13
    memref.store %14, __scope_10
    %15 = memref.load i : i64
    %16 = func.call @register-allocator.double %15
    %17 = memref.load sum : i64
    %18 = arith.addi %17, %16
    memref.store %18, sum
    %19 = arith.constant {value = 1 : i64}
    %20 = memref.load i : i64
    %21 = arith.addi %20, %19
    memref.store %21, i
    %22 = memref.load __scope_10 : i64
    std.call_runtime @mm_scope_exit %22
    cf.br loop_0.header
  loop_0.exit:
    %23 = memref.load sum : i64
    memref.store %23, __range_val_1
    %24 = arith.constant {value = 0 : i64}
    %25 = arith.cmpi lt %23, %24
    %26 = arith.constant {value = 4294967295 : i64}
    %27 = arith.cmpi gt %23, %26
    %28 = arith.ori1 %25, %27
    cf.cond_br %28 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %29 = memref.lea_symdata __panic_msg_24
    %30 = std.ptr_to_i64 %29
    std.call_runtime @maxon_panic %30
  __range_ok_1:
    %31 = memref.load __range_val_1 : i64
    %32 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %32
    func.return %31
  }
}
=== x86
module {
  func @register-allocator.double(x: i64) -> i64 {
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
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 5
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-32], eax
    x86.mov ecx, [rbp-24]
    x86.call register-allocator.double
    x86.mov edx, [rbp-16]
    x86.add edx, eax
    x86.mov [rbp-16], edx
    x86.mov ebx, 1
    x86.mov esi, [rbp-24]
    x86.add esi, ebx
    x86.mov [rbp-24], esi
    x86.mov rcx, [rbp-32]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-40], eax
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
    x86.je register-allocator.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_24]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-40]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-48], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-48]
    x86.epilogue
    x86.ret
  }
}
```

### Level 6: Advanced Scenarios

<!-- test: int-nested-expressions-deep -->
```maxon
function main() returns ExitCode
  return ((((1 + 2) * 3) + 4) * 2) + 6
end 'main'
```
```exitcode
32
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 1 : i64}
    %2 = maxon.literal {value = 2 : i64}
    %3 = maxon.binop %1, %2 {op = add}
    %4 = maxon.literal {value = 3 : i64}
    %5 = maxon.binop %3, %4 {op = mul}
    %6 = maxon.literal {value = 4 : i64}
    %7 = maxon.binop %5, %6 {op = add}
    %8 = maxon.literal {value = 2 : i64}
    %9 = maxon.binop %7, %8 {op = mul}
    %10 = maxon.literal {value = 6 : i64}
    %11 = maxon.binop %9, %10 {op = add}
    maxon.assign %11 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %11, %12 {op = lt}
    %14 = maxon.literal {value = 4294967295 : i64}
    %15 = maxon.binop %11, %14 {op = gt}
    %16 = maxon.binop %13, %15 {op = or}
    maxon.cond_br %16 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-nested-expressions-deep.test:3: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %18 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %18
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 2 : i64}
    %4 = arith.addi %2, %3
    %5 = arith.constant {value = 3 : i64}
    %6 = arith.muli %4, %5
    %7 = arith.constant {value = 4 : i64}
    %8 = arith.addi %6, %7
    %9 = arith.constant {value = 2 : i64}
    %10 = arith.muli %8, %9
    %11 = arith.constant {value = 6 : i64}
    %12 = arith.addi %10, %11
    memref.store %12, __range_val_0
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.cmpi lt %12, %13
    %15 = arith.constant {value = 4294967295 : i64}
    %16 = arith.cmpi gt %12, %15
    %17 = arith.ori1 %14, %16
    cf.cond_br %17 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %18 = memref.lea_symdata __panic_msg_17
    %19 = std.ptr_to_i64 %18
    std.call_runtime @maxon_panic %19
  __range_ok_0:
    %20 = memref.load __range_val_0 : i64
    %21 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %21
    func.return %20
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov edx, 2
    x86.add ecx, edx
    x86.mov ebx, 3
    x86.imul ecx, ebx
    x86.mov esi, 4
    x86.add ecx, esi
    x86.mov edi, 2
    x86.imul ecx, edi
    x86.mov r8, 6
    x86.add ecx, r8
    x86.mov [rbp-16], ecx
    x86.xor r9, r9
    x86.cmp ecx, r9
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rdx, 4294967295
    x86.cmp rcx, rdx
    x86.setg ecx
    x86.movzx ecx, ecxb
    x86.or eax, ecx
    x86.test eax, eax
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_17]
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

<!-- test: int-expression-both-sides-complex -->
```maxon
function main() returns ExitCode
  var a = 3
  var b = 5
  var c = 7
  var d = 2
  return (a + b) * (c - d)
end 'main'
```
```exitcode
40
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 3 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 5 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 7 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 2 : i64}
    maxon.assign %4 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %1, %2 {op = add}
    %6 = maxon.binop %3, %4 {op = sub}
    %7 = maxon.binop %5, %6 {op = mul}
    maxon.assign %7 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-expression-both-sides-complex.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %14 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %14
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.constant {value = 5 : i64}
    %4 = arith.constant {value = 7 : i64}
    %5 = arith.constant {value = 2 : i64}
    %6 = arith.addi %2, %3
    %7 = arith.subi %4, %5
    %8 = arith.muli %6, %7
    memref.store %8, __range_val_0
    %9 = arith.constant {value = 0 : i64}
    %10 = arith.cmpi lt %8, %9
    %11 = arith.constant {value = 4294967295 : i64}
    %12 = arith.cmpi gt %8, %11
    %13 = arith.ori1 %10, %12
    cf.cond_br %13 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %14 = memref.lea_symdata __panic_msg_13
    %15 = std.ptr_to_i64 %14
    std.call_runtime @maxon_panic %15
  __range_ok_0:
    %16 = memref.load __range_val_0 : i64
    %17 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %17
    func.return %16
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 3
    x86.mov edx, 5
    x86.mov ebx, 7
    x86.mov esi, 2
    x86.add ecx, edx
    x86.sub ebx, esi
    x86.imul ecx, ebx
    x86.mov [rbp-16], ecx
    x86.xor edi, edi
    x86.cmp ecx, edi
    x86.setl r8
    x86.movzx r8, r8b
    x86.mov r9, 4294967295
    x86.cmp rcx, r9
    x86.setg eax
    x86.movzx eax, eaxb
    x86.or r8, eax
    x86.test r8, r8
    x86.je register-allocator.main.__range_ok_0
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

<!-- test: int-many-params-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sum5(a Integer, b Integer, c Integer, d Integer, e Integer) returns Integer
  return a + b + c + d + e
end 'sum5'

function main() returns ExitCode
  return sum5(5, b: 10, c: 8, d: 12, e: 7)
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.sum5}
    %1 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %2 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %3 = maxon.param {index = 2 : i32} {name = c} {type = i64}
    %4 = maxon.param {index = 3 : i32} {name = d} {type = i64}
    %5 = maxon.param {index = 4 : i32} {name = e} {type = i64}
    %6 = maxon.binop %1, %2 {op = add} {optimalType = i64}
    %7 = maxon.binop %6, %3 {op = add} {optimalType = i64}
    %8 = maxon.binop %7, %4 {op = add} {optimalType = i64}
    %9 = maxon.binop %8, %5 {op = add} {optimalType = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %9
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_10 = maxon.scope_enter {tag = register-allocator.main}
    %11 = maxon.literal {value = 5 : i64}
    %12 = maxon.literal {value = 10 : i64}
    %13 = maxon.literal {value = 8 : i64}
    %14 = maxon.literal {value = 12 : i64}
    %15 = maxon.literal {value = 7 : i64}
    %16 = maxon.call @register-allocator.sum5 %11, %12, %13, %14, %15
    maxon.assign %16 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %16, %17 {op = lt}
    %19 = maxon.literal {value = 4294967295 : i64}
    %20 = maxon.binop %16, %19 {op = gt}
    %21 = maxon.binop %18, %20 {op = or}
    maxon.cond_br %21 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-many-params-function.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %23 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_10} {tag = return_cleanup}
    maxon.return %23
  }
}
=== standard
module {
  func @register-allocator.sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param a : StdI64
    %3 = func.param b : StdI64
    %4 = func.param c : StdI64
    %5 = func.param d : StdI64
    %6 = func.param e : StdI64
    %7 = arith.addi %2, %3
    %8 = arith.addi %7, %4
    %9 = arith.addi %8, %5
    %10 = arith.addi %9, %6
    %11 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %11
    func.return %10
  }
  func @register-allocator.main() -> u32 {
  entry:
    %12 = arith.constant {value = 0 : i64}
    %13 = std.call_runtime @mm_scope_enter %12
    memref.store %13, __scope_10
    %14 = arith.constant {value = 5 : i64}
    %15 = arith.constant {value = 10 : i64}
    %16 = arith.constant {value = 8 : i64}
    %17 = arith.constant {value = 12 : i64}
    %18 = arith.constant {value = 7 : i64}
    %19 = func.call @register-allocator.sum5 %14, %15, %16, %17, %18
    memref.store %19, __range_val_0
    %20 = arith.constant {value = 0 : i64}
    %21 = arith.cmpi lt %19, %20
    %22 = arith.constant {value = 4294967295 : i64}
    %23 = arith.cmpi gt %19, %22
    %24 = arith.ori1 %21, %23
    cf.cond_br %24 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %25 = memref.lea_symdata __panic_msg_22
    %26 = std.ptr_to_i64 %25
    std.call_runtime @maxon_panic %26
  __range_ok_0:
    %27 = memref.load __range_val_0 : i64
    %28 = memref.load __scope_10 : i64
    std.call_runtime @mm_scope_exit %28
    func.return %27
  }
}
=== x86
module {
  func @register-allocator.sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.mov [rbp-16], ecx
    x86.mov [rbp-24], edx
    x86.mov [rbp-32], esi
    x86.mov [rbp-40], r8
    x86.mov [rbp-48], r9
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, [rbp-24]
    x86.mov edx, [rbp-16]
    x86.add edx, ecx
    x86.mov ebx, [rbp-40]
    x86.add edx, ebx
    x86.mov esi, [rbp-48]
    x86.add edx, esi
    x86.mov edi, [rbp-32]
    x86.add edx, edi
    x86.mov r8, [rbp-8]
    x86.mov [rbp-56], edx
    x86.mov rcx, r8
    x86.call mm_scope_exit
    x86.mov eax, [rbp-56]
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 5
    x86.mov edx, 10
    x86.mov ebx, 8
    x86.mov esi, 12
    x86.mov edi, 7
    x86.mov r8, rbx
    x86.mov r9, rsi
    x86.mov rsi, rdi
    x86.call register-allocator.sum5
    x86.mov [rbp-16], eax
    x86.xor r8, r8
    x86.cmp eax, r8
    x86.setl r9
    x86.movzx r9, r9b
    x86.mov rcx, 4294967295
    x86.cmp rax, rcx
    x86.setg eax
    x86.movzx eax, eaxb
    x86.or r9, eax
    x86.test r9, r9
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_22]
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

<!-- test: int-nine-params-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sum9(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer, h Integer, i Integer) returns Integer
  return a + b + c + d + e + f + g + h + i
end 'sum9'

function main() returns ExitCode
  return sum9(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7, h: 8, i: 9)
end 'main'
```
```exitcode
45
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.sum9}
    %1 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %2 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %3 = maxon.param {index = 2 : i32} {name = c} {type = i64}
    %4 = maxon.param {index = 3 : i32} {name = d} {type = i64}
    %5 = maxon.param {index = 4 : i32} {name = e} {type = i64}
    %6 = maxon.param {index = 5 : i32} {name = f} {type = i64}
    %7 = maxon.param {index = 6 : i32} {name = g} {type = i64}
    %8 = maxon.param {index = 7 : i32} {name = h} {type = i64}
    %9 = maxon.param {index = 8 : i32} {name = i} {type = i64}
    %10 = maxon.binop %1, %2 {op = add} {optimalType = i64}
    %11 = maxon.binop %10, %3 {op = add} {optimalType = i64}
    %12 = maxon.binop %11, %4 {op = add} {optimalType = i64}
    %13 = maxon.binop %12, %5 {op = add} {optimalType = i64}
    %14 = maxon.binop %13, %6 {op = add} {optimalType = i64}
    %15 = maxon.binop %14, %7 {op = add} {optimalType = i64}
    %16 = maxon.binop %15, %8 {op = add} {optimalType = i64}
    %17 = maxon.binop %16, %9 {op = add} {optimalType = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %17
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_18 = maxon.scope_enter {tag = register-allocator.main}
    %19 = maxon.literal {value = 1 : i64}
    %20 = maxon.literal {value = 2 : i64}
    %21 = maxon.literal {value = 3 : i64}
    %22 = maxon.literal {value = 4 : i64}
    %23 = maxon.literal {value = 5 : i64}
    %24 = maxon.literal {value = 6 : i64}
    %25 = maxon.literal {value = 7 : i64}
    %26 = maxon.literal {value = 8 : i64}
    %27 = maxon.literal {value = 9 : i64}
    %28 = maxon.call @register-allocator.sum9 %19, %20, %21, %22, %23, %24, %25, %26, %27
    maxon.assign %28 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %29 = maxon.literal {value = 0 : i64}
    %30 = maxon.binop %28, %29 {op = lt}
    %31 = maxon.literal {value = 4294967295 : i64}
    %32 = maxon.binop %28, %31 {op = gt}
    %33 = maxon.binop %30, %32 {op = or}
    maxon.cond_br %33 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-nine-params-function.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %35 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_18} {tag = return_cleanup}
    maxon.return %35
  }
}
=== standard
module {
  func @register-allocator.sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param a : StdI64
    %3 = func.param b : StdI64
    %4 = func.param c : StdI64
    %5 = func.param d : StdI64
    %6 = func.param e : StdI64
    %7 = func.param f : StdI64
    %8 = func.param g : StdI64
    %9 = func.param h : StdI64
    %10 = func.param i : StdI64
    %11 = arith.addi %2, %3
    %12 = arith.addi %11, %4
    %13 = arith.addi %12, %5
    %14 = arith.addi %13, %6
    %15 = arith.addi %14, %7
    %16 = arith.addi %15, %8
    %17 = arith.addi %16, %9
    %18 = arith.addi %17, %10
    %19 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %19
    func.return %18
  }
  func @register-allocator.main() -> u32 {
  entry:
    %20 = arith.constant {value = 0 : i64}
    %21 = std.call_runtime @mm_scope_enter %20
    memref.store %21, __scope_18
    %22 = arith.constant {value = 1 : i64}
    %23 = arith.constant {value = 2 : i64}
    %24 = arith.constant {value = 3 : i64}
    %25 = arith.constant {value = 4 : i64}
    %26 = arith.constant {value = 5 : i64}
    %27 = arith.constant {value = 6 : i64}
    %28 = arith.constant {value = 7 : i64}
    %29 = arith.constant {value = 8 : i64}
    %30 = arith.constant {value = 9 : i64}
    %31 = func.call @register-allocator.sum9 %22, %23, %24, %25, %26, %27, %28, %29, %30
    memref.store %31, __range_val_0
    %32 = arith.constant {value = 0 : i64}
    %33 = arith.cmpi lt %31, %32
    %34 = arith.constant {value = 4294967295 : i64}
    %35 = arith.cmpi gt %31, %34
    %36 = arith.ori1 %33, %35
    cf.cond_br %36 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %37 = memref.lea_symdata __panic_msg_34
    %38 = std.ptr_to_i64 %37
    std.call_runtime @maxon_panic %38
  __range_ok_0:
    %39 = memref.load __range_val_0 : i64
    %40 = memref.load __scope_18 : i64
    std.call_runtime @mm_scope_exit %40
    func.return %39
  }
}
=== x86
module {
  func @register-allocator.sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    x86.prologue stack_size=80
    x86.mov [rbp-16], ecx
    x86.xor ecx, ecx
    x86.mov [rbp-24], eax
    x86.mov [rbp-32], edx
    x86.mov [rbp-40], ebx
    x86.mov [rbp-48], esi
    x86.mov [rbp-56], edi
    x86.mov [rbp-64], r8
    x86.mov [rbp-72], r9
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov eax, [rbp+16]
    x86.mov ecx, [rbp-32]
    x86.mov edx, [rbp-16]
    x86.add edx, ecx
    x86.mov ebx, [rbp-64]
    x86.add edx, ebx
    x86.mov esi, [rbp-72]
    x86.add edx, esi
    x86.mov edi, [rbp-48]
    x86.add edx, edi
    x86.mov r8, [rbp-56]
    x86.add edx, r8
    x86.mov r9, [rbp-24]
    x86.add edx, r9
    x86.mov ecx, [rbp-40]
    x86.add edx, ecx
    x86.add edx, eax
    x86.mov eax, [rbp-8]
    x86.mov [rbp-80], edx
    x86.mov rcx, rax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-80]
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov edx, 2
    x86.mov ebx, 3
    x86.mov esi, 4
    x86.mov edi, 5
    x86.mov r8, 6
    x86.mov r9, 7
    x86.mov eax, 8
    x86.mov ecx, 9
    x86.sub rsp, 16
    x86.mov [rsp+0], ecx
    x86.xchg rbx, r8
    x86.xchg rsi, r9
    x86.xchg rdi, rsi
    x86.xchg rbx, rdi
    x86.xchg rbx, rax
    x86.mov rcx, 1
    x86.call register-allocator.sum9
    x86.add rsp, 16
    x86.mov [rbp-16], eax
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
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_34]
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

<!-- test: int-recursive-factorial -->
```maxon

typealias Integer = int(i64.min to i64.max)

function factorial(n Integer) returns Integer
  if n <= 1 'base'
    return 1
  end 'base'
  return n * factorial(n - 1)
end 'factorial'

function main() returns ExitCode
  return factorial(5) mod 256
end 'main'
```
```exitcode
120
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.factorial(n: i64) -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.factorial}
    %1 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %2 = maxon.literal {value = 1 : i64}
    %3 = maxon.binop %1, %2 {op = le} {optimalType = i64}
    maxon.cond_br %3 [then: base_0, else: base_0.after]
  base_0:
    __scope_4 = maxon.scope_enter {tag = if_then}
    %5 = maxon.literal {value = 1 : i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %5
  base_0.after:
    %6 = maxon.literal {value = 1 : i64}
    %7 = maxon.var_ref {var = n} {type = i64}
    %8 = maxon.binop %7, %6 {op = sub}
    %9 = maxon.call @register-allocator.factorial %8
    %10 = maxon.var_ref {var = n} {type = i64}
    %11 = maxon.binop %10, %9 {op = mul}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %11
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_12 = maxon.scope_enter {tag = register-allocator.main}
    %13 = maxon.literal {value = 5 : i64}
    %14 = maxon.call @register-allocator.factorial %13
    %15 = maxon.literal {value = 256 : i64}
    %16 = maxon.binop %14, %15 {op = mod}
    maxon.assign %16 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %16, %17 {op = lt}
    %19 = maxon.literal {value = 4294967295 : i64}
    %20 = maxon.binop %16, %19 {op = gt}
    %21 = maxon.binop %18, %20 {op = or}
    maxon.cond_br %21 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-recursive-factorial.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %23 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_12} {tag = return_cleanup}
    maxon.return %23
  }
}
=== standard
module {
  func @register-allocator.factorial(n: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param n : StdI64
    memref.store %2, n
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.cmpi le %2, %3
    cf.cond_br %4 [then: base_0, else: base_0.after]
  base_0:
    %5 = arith.constant {value = 0 : i64}
    %6 = std.call_runtime @mm_scope_enter %5
    memref.store %6, __scope_4
    %7 = arith.constant {value = 1 : i64}
    %8 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %8
    %9 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %9
    func.return %7
  base_0.after:
    %10 = arith.constant {value = 1 : i64}
    %11 = memref.load n : i64
    %12 = arith.subi %11, %10
    %13 = func.call @register-allocator.factorial %12
    %14 = memref.load n : i64
    %15 = arith.muli %14, %13
    %16 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %16
    func.return %15
  }
  func @register-allocator.main() -> u32 {
  entry:
    %17 = arith.constant {value = 0 : i64}
    %18 = std.call_runtime @mm_scope_enter %17
    memref.store %18, __scope_12
    %19 = arith.constant {value = 5 : i64}
    %20 = func.call @register-allocator.factorial %19
    %21 = arith.constant {value = 256 : i64}
    %22 = arith.remsi %20, %21
    memref.store %22, __range_val_0
    %23 = arith.constant {value = 0 : i64}
    %24 = arith.cmpi lt %22, %23
    %25 = arith.constant {value = 4294967295 : i64}
    %26 = arith.cmpi gt %22, %25
    %27 = arith.ori1 %24, %26
    cf.cond_br %27 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %28 = memref.lea_symdata __panic_msg_22
    %29 = std.ptr_to_i64 %28
    std.call_runtime @maxon_panic %29
  __range_ok_0:
    %30 = memref.load __range_val_0 : i64
    %31 = memref.load __scope_12 : i64
    std.call_runtime @mm_scope_exit %31
    func.return %30
  }
}
=== x86
module {
  func @register-allocator.factorial(n: i64) -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-16], ecx
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov edx, [rbp-16]
    x86.cmp edx, ecx
    x86.jg register-allocator.factorial.base_0.after
  base_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-24], eax
    x86.mov eax, 1
    x86.mov ecx, [rbp-24]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov eax, 1
    x86.epilogue
    x86.ret
  base_0.after:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.sub ecx, eax
    x86.call register-allocator.factorial
    x86.mov edx, [rbp-16]
    x86.imul edx, eax
    x86.mov ebx, [rbp-8]
    x86.mov [rbp-32], edx
    x86.mov rcx, rbx
    x86.call mm_scope_exit
    x86.mov eax, [rbp-32]
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 5
    x86.call register-allocator.factorial
    x86.mov edx, 256
    x86.mov ecx, edx
    x86.mov [rbp-24], eax
    x86.cqo
    x86.idiv ecx
    x86.mov [rbp-16], edx
    x86.xor ebx, ebx
    x86.cmp edx, ebx
    x86.setl esi
    x86.movzx esi, esib
    x86.mov rdi, 4294967295
    x86.cmp rdx, rdi
    x86.setg r8
    x86.movzx r8, r8b
    x86.or esi, r8
    x86.test esi, esi
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_22]
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

<!-- test: int-loop-pressure-with-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function identity(x Integer) returns Integer
  return x
end 'identity'

function main() returns ExitCode
  var a = 1
  var b = 2
  var c = 3
  var d = 4
  var e = 5
  var f = 6
  var i = 0
  while i < 3 'loop'
    a = a + identity(b)
    c = c + identity(d)
    e = e + identity(f)
    i = i + 1
  end 'loop'
  return (a + c + d + e + f) mod 256
end 'main'
```
```exitcode
55
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.identity(x: i64) -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.identity}
    %1 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %1
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_2 = maxon.scope_enter {tag = register-allocator.main}
    %3 = maxon.literal {value = 1 : i64}
    maxon.assign %3 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 2 : i64}
    maxon.assign %4 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 3 : i64}
    maxon.assign %5 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 4 : i64}
    maxon.assign %6 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 5 : i64}
    maxon.assign %7 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 6 : i64}
    maxon.assign %8 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.literal {value = 0 : i64}
    maxon.assign %9 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %10 = maxon.literal {value = 3 : i64}
    %11 = maxon.var_ref {var = i} {type = i64}
    %12 = maxon.binop %11, %10 {op = lt}
    maxon.cond_br %12 [then: loop_0, else: loop_0.exit]
  loop_0:
    __scope_13 = maxon.scope_enter {tag = while}
    %14 = maxon.var_ref {var = b} {type = i64}
    %15 = maxon.call @register-allocator.identity %14
    %16 = maxon.var_ref {var = a} {type = i64}
    %17 = maxon.binop %16, %15 {op = add}
    maxon.assign %17 {var = a} {kind = i64} {mut = 1 : i1}
    %18 = maxon.var_ref {var = d} {type = i64}
    %19 = maxon.call @register-allocator.identity %18
    %20 = maxon.var_ref {var = c} {type = i64}
    %21 = maxon.binop %20, %19 {op = add}
    maxon.assign %21 {var = c} {kind = i64} {mut = 1 : i1}
    %22 = maxon.var_ref {var = f} {type = i64}
    %23 = maxon.call @register-allocator.identity %22
    %24 = maxon.var_ref {var = e} {type = i64}
    %25 = maxon.binop %24, %23 {op = add}
    maxon.assign %25 {var = e} {kind = i64} {mut = 1 : i1}
    %26 = maxon.literal {value = 1 : i64}
    %27 = maxon.var_ref {var = i} {type = i64}
    %28 = maxon.binop %27, %26 {op = add}
    maxon.assign %28 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_13} {tag = block_exit}
    maxon.br loop_0.header
  loop_0.exit:
    %29 = maxon.var_ref {var = a} {type = i64}
    %30 = maxon.var_ref {var = c} {type = i64}
    %31 = maxon.binop %29, %30 {op = add}
    %32 = maxon.var_ref {var = d} {type = i64}
    %33 = maxon.binop %31, %32 {op = add}
    %34 = maxon.var_ref {var = e} {type = i64}
    %35 = maxon.binop %33, %34 {op = add}
    %36 = maxon.var_ref {var = f} {type = i64}
    %37 = maxon.binop %35, %36 {op = add}
    %38 = maxon.literal {value = 256 : i64}
    %39 = maxon.binop %37, %38 {op = mod}
    maxon.assign %39 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %40 = maxon.literal {value = 0 : i64}
    %41 = maxon.binop %39, %40 {op = lt}
    %42 = maxon.literal {value = 4294967295 : i64}
    %43 = maxon.binop %39, %42 {op = gt}
    %44 = maxon.binop %41, %43 {op = or}
    maxon.cond_br %44 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at int-loop-pressure-with-call.test:23: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %46 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_2} {tag = return_cleanup}
    maxon.return %46
  }
}
=== standard
module {
  func @register-allocator.identity(x: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param x : StdI64
    %3 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %3
    func.return %2
  }
  func @register-allocator.main() -> u32 {
  entry:
    %4 = arith.constant {value = 0 : i64}
    %5 = std.call_runtime @mm_scope_enter %4
    memref.store %5, __scope_2
    %6 = arith.constant {value = 1 : i64}
    memref.store %6, a
    %7 = arith.constant {value = 2 : i64}
    memref.store %7, b
    %8 = arith.constant {value = 3 : i64}
    memref.store %8, c
    %9 = arith.constant {value = 4 : i64}
    memref.store %9, d
    %10 = arith.constant {value = 5 : i64}
    memref.store %10, e
    %11 = arith.constant {value = 6 : i64}
    memref.store %11, f
    %12 = arith.constant {value = 0 : i64}
    memref.store %12, i
    cf.br loop_0.header
  loop_0.header:
    %13 = arith.constant {value = 3 : i64}
    %14 = memref.load i : i64
    %15 = arith.cmpi lt %14, %13
    cf.cond_br %15 [then: loop_0, else: loop_0.exit]
  loop_0:
    %16 = arith.constant {value = 0 : i64}
    %17 = std.call_runtime @mm_scope_enter %16
    memref.store %17, __scope_13
    %18 = memref.load b : i64
    %19 = func.call @register-allocator.identity %18
    %20 = memref.load a : i64
    %21 = arith.addi %20, %19
    memref.store %21, a
    %22 = memref.load d : i64
    %23 = func.call @register-allocator.identity %22
    %24 = memref.load c : i64
    %25 = arith.addi %24, %23
    memref.store %25, c
    %26 = memref.load f : i64
    %27 = func.call @register-allocator.identity %26
    %28 = memref.load e : i64
    %29 = arith.addi %28, %27
    memref.store %29, e
    %30 = arith.constant {value = 1 : i64}
    %31 = memref.load i : i64
    %32 = arith.addi %31, %30
    memref.store %32, i
    %33 = memref.load __scope_13 : i64
    std.call_runtime @mm_scope_exit %33
    cf.br loop_0.header
  loop_0.exit:
    %34 = memref.load a : i64
    %35 = memref.load c : i64
    %36 = arith.addi %34, %35
    %37 = memref.load d : i64
    %38 = arith.addi %36, %37
    %39 = memref.load e : i64
    %40 = arith.addi %38, %39
    %41 = memref.load f : i64
    %42 = arith.addi %40, %41
    %43 = arith.constant {value = 256 : i64}
    %44 = arith.remsi %42, %43
    memref.store %44, __range_val_1
    %45 = arith.constant {value = 0 : i64}
    %46 = arith.cmpi lt %44, %45
    %47 = arith.constant {value = 4294967295 : i64}
    %48 = arith.cmpi gt %44, %47
    %49 = arith.ori1 %46, %48
    cf.cond_br %49 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %50 = memref.lea_symdata __panic_msg_45
    %51 = std.ptr_to_i64 %50
    std.call_runtime @maxon_panic %51
  __range_ok_1:
    %52 = memref.load __range_val_1 : i64
    %53 = memref.load __scope_2 : i64
    std.call_runtime @mm_scope_exit %53
    func.return %52
  }
}
=== x86
module {
  func @register-allocator.identity(x: i64) -> i64 {
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
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=96
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov [rbp-16], ecx
    x86.mov edx, 2
    x86.mov [rbp-24], edx
    x86.mov ebx, 3
    x86.mov [rbp-32], ebx
    x86.mov esi, 4
    x86.mov [rbp-40], esi
    x86.mov edi, 5
    x86.mov [rbp-48], edi
    x86.mov r8, 6
    x86.mov [rbp-56], r8
    x86.xor r9, r9
    x86.mov [rbp-64], r9
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-64]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-72], eax
    x86.mov ecx, [rbp-24]
    x86.call register-allocator.identity
    x86.mov edx, [rbp-16]
    x86.add edx, eax
    x86.mov [rbp-16], edx
    x86.mov rcx, [rbp-40]
    x86.call register-allocator.identity
    x86.mov esi, [rbp-32]
    x86.add esi, eax
    x86.mov [rbp-32], esi
    x86.mov rcx, [rbp-56]
    x86.call register-allocator.identity
    x86.mov r8, [rbp-48]
    x86.add r8, eax
    x86.mov [rbp-48], r8
    x86.mov r9, 1
    x86.mov eax, [rbp-64]
    x86.add eax, r9
    x86.mov [rbp-64], eax
    x86.mov rcx, [rbp-72]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-32]
    x86.add eax, ecx
    x86.mov edx, [rbp-40]
    x86.add eax, edx
    x86.mov ebx, [rbp-48]
    x86.add eax, ebx
    x86.mov esi, [rbp-56]
    x86.add eax, esi
    x86.mov edi, 256
    x86.mov [rbp-88], eax
    x86.cqo
    x86.idiv edi
    x86.mov [rbp-80], edx
    x86.xor r8, r8
    x86.cmp edx, r8
    x86.setl r9
    x86.movzx r9, r9b
    x86.mov rax, 4294967295
    x86.cmp rdx, rax
    x86.setg eax
    x86.movzx eax, eaxb
    x86.or r9, eax
    x86.test r9, r9
    x86.je register-allocator.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_45]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
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

<!-- test: float-and-int-mixed-pressure -->
```maxon
function main() returns ExitCode
  var x = 3.14
  var y = 2.86
  var sum_f = x + y
  var a = 10
  var b = 20
  var sum_i = a + b
  return trunc(sum_f) + sum_i
end 'main'
```
```exitcode
36
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 3.14 : f64}
    maxon.assign %1 {var = x} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 2.86 : f64}
    maxon.assign %2 {var = y} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add} {kind = f64}
    maxon.assign %3 {var = sum_f} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 10 : i64}
    maxon.assign %4 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 20 : i64}
    maxon.assign %5 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.binop %4, %5 {op = add}
    maxon.assign %6 {var = sum_i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.trunc %3
    %8 = maxon.binop %7, %6 {op = add}
    maxon.assign %8 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.binop %8, %9 {op = lt}
    %11 = maxon.literal {value = 4294967295 : i64}
    %12 = maxon.binop %8, %11 {op = gt}
    %13 = maxon.binop %10, %12 {op = or}
    maxon.cond_br %13 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at float-and-int-mixed-pressure.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %15 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %15
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.float_constant {value = 3.14 : f64}
    %3 = arith.float_constant {value = 2.86 : f64}
    %4 = arith.addf %2, %3
    %5 = arith.constant {value = 10 : i64}
    %6 = arith.constant {value = 20 : i64}
    %7 = arith.addi %5, %6
    %8 = arith.fptosi %4
    %9 = arith.addi %8, %7
    memref.store %9, __range_val_0
    %10 = arith.constant {value = 0 : i64}
    %11 = arith.cmpi lt %9, %10
    %12 = arith.constant {value = 4294967295 : i64}
    %13 = arith.cmpi gt %9, %12
    %14 = arith.ori1 %11, %13
    cf.cond_br %14 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %15 = memref.lea_symdata __panic_msg_14
    %16 = std.ptr_to_i64 %15
    std.call_runtime @maxon_panic %16
  __range_ok_0:
    %17 = memref.load __range_val_0 : i64
    %18 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %18
    func.return %17
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.movsd xmm0, [rip+__float_3.14]
    x86.movsd xmm1, [rip+__float_2.86]
    x86.movsd xmm2, xmm0
    x86.addsd xmm2, xmm1
    x86.mov ecx, 10
    x86.mov edx, 20
    x86.add ecx, edx
    x86.cvttsd2si ebx, xmm2
    x86.add ebx, ecx
    x86.mov [rbp-16], ebx
    x86.xor esi, esi
    x86.cmp ebx, esi
    x86.setl edi
    x86.movzx edi, edib
    x86.mov r8, 4294967295
    x86.cmp rbx, r8
    x86.setg r9
    x86.movzx r9, r9b
    x86.or edi, r9
    x86.test edi, edi
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_14]
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

<!-- test: int-value-live-across-nested-control -->
```maxon
function main() returns ExitCode
  var sentinel = 100
  var total = 0
  var i = 0
  while i < 3 'outer'
    var j = 0
    while j < 3 'inner'
      if i == j 'diag'
        total = total + 1
      end 'diag'
      j = j + 1
    end 'inner'
    i = i + 1
  end 'outer'
  return sentinel + total
end 'main'
```
```exitcode
103
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 100 : i64}
    maxon.assign %1 {var = sentinel} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = total} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    maxon.assign %3 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br outer_0.header
  outer_0.header:
    %4 = maxon.literal {value = 3 : i64}
    %5 = maxon.var_ref {var = i} {type = i64}
    %6 = maxon.binop %5, %4 {op = lt}
    maxon.cond_br %6 [then: outer_0, else: outer_0.exit]
  outer_0:
    __scope_7 = maxon.scope_enter {tag = while}
    %8 = maxon.literal {value = 0 : i64}
    maxon.assign %8 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.header:
    %9 = maxon.literal {value = 3 : i64}
    %10 = maxon.var_ref {var = j} {type = i64}
    %11 = maxon.binop %10, %9 {op = lt}
    maxon.cond_br %11 [then: inner_1, else: inner_1.exit]
  inner_1:
    __scope_12 = maxon.scope_enter {tag = while}
    %13 = maxon.var_ref {var = i} {type = i64}
    %14 = maxon.var_ref {var = j} {type = i64}
    %15 = maxon.binop %13, %14 {op = eq}
    maxon.cond_br %15 [then: diag_2, else: diag_2.merge]
  diag_2:
    __scope_16 = maxon.scope_enter {tag = if_then}
    %17 = maxon.literal {value = 1 : i64}
    %18 = maxon.var_ref {var = total} {type = i64}
    %19 = maxon.binop %18, %17 {op = add}
    maxon.assign %19 {var = total} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_16} {tag = block_exit}
    maxon.br diag_2.merge
  diag_2.merge:
    %20 = maxon.literal {value = 1 : i64}
    %21 = maxon.var_ref {var = j} {type = i64}
    %22 = maxon.binop %21, %20 {op = add}
    maxon.assign %22 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_12} {tag = block_exit}
    maxon.br inner_1.header
  inner_1.exit:
    %23 = maxon.literal {value = 1 : i64}
    %24 = maxon.var_ref {var = i} {type = i64}
    %25 = maxon.binop %24, %23 {op = add}
    maxon.assign %25 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_7} {tag = block_exit}
    maxon.br outer_0.header
  outer_0.exit:
    %26 = maxon.var_ref {var = sentinel} {type = i64}
    %27 = maxon.var_ref {var = total} {type = i64}
    %28 = maxon.binop %26, %27 {op = add}
    maxon.assign %28 {var = __range_val_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %29 = maxon.literal {value = 0 : i64}
    %30 = maxon.binop %28, %29 {op = lt}
    %31 = maxon.literal {value = 4294967295 : i64}
    %32 = maxon.binop %28, %31 {op = gt}
    %33 = maxon.binop %30, %32 {op = or}
    maxon.cond_br %33 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    maxon.panic "panic at int-value-live-across-nested-control.test:16: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_3:
    %35 = maxon.var_ref {var = __range_val_3} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %35
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 100 : i64}
    memref.store %2, sentinel
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, total
    %4 = arith.constant {value = 0 : i64}
    memref.store %4, i
    cf.br outer_0.header
  outer_0.header:
    %5 = arith.constant {value = 3 : i64}
    %6 = memref.load i : i64
    %7 = arith.cmpi lt %6, %5
    cf.cond_br %7 [then: outer_0, else: outer_0.exit]
  outer_0:
    %8 = arith.constant {value = 0 : i64}
    %9 = std.call_runtime @mm_scope_enter %8
    memref.store %9, __scope_7
    %10 = arith.constant {value = 0 : i64}
    memref.store %10, j
    cf.br inner_1.header
  inner_1.header:
    %11 = arith.constant {value = 3 : i64}
    %12 = memref.load j : i64
    %13 = arith.cmpi lt %12, %11
    cf.cond_br %13 [then: inner_1, else: inner_1.exit]
  inner_1:
    %14 = arith.constant {value = 0 : i64}
    %15 = std.call_runtime @mm_scope_enter %14
    memref.store %15, __scope_12
    %16 = memref.load i : i64
    %17 = memref.load j : i64
    %18 = arith.cmpi eq %16, %17
    cf.cond_br %18 [then: diag_2, else: diag_2.merge]
  diag_2:
    %19 = arith.constant {value = 0 : i64}
    %20 = std.call_runtime @mm_scope_enter %19
    memref.store %20, __scope_16
    %21 = arith.constant {value = 1 : i64}
    %22 = memref.load total : i64
    %23 = arith.addi %22, %21
    memref.store %23, total
    %24 = memref.load __scope_16 : i64
    std.call_runtime @mm_scope_exit %24
    cf.br diag_2.merge
  diag_2.merge:
    %25 = arith.constant {value = 1 : i64}
    %26 = memref.load j : i64
    %27 = arith.addi %26, %25
    memref.store %27, j
    %28 = memref.load __scope_12 : i64
    std.call_runtime @mm_scope_exit %28
    cf.br inner_1.header
  inner_1.exit:
    %29 = arith.constant {value = 1 : i64}
    %30 = memref.load i : i64
    %31 = arith.addi %30, %29
    memref.store %31, i
    %32 = memref.load __scope_7 : i64
    std.call_runtime @mm_scope_exit %32
    cf.br outer_0.header
  outer_0.exit:
    %33 = memref.load sentinel : i64
    %34 = memref.load total : i64
    %35 = arith.addi %33, %34
    memref.store %35, __range_val_3
    %36 = arith.constant {value = 0 : i64}
    %37 = arith.cmpi lt %35, %36
    %38 = arith.constant {value = 4294967295 : i64}
    %39 = arith.cmpi gt %35, %38
    %40 = arith.ori1 %37, %39
    cf.cond_br %40 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    %41 = memref.lea_symdata __panic_msg_34
    %42 = std.ptr_to_i64 %41
    std.call_runtime @maxon_panic %42
  __range_ok_3:
    %43 = memref.load __range_val_3 : i64
    %44 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %44
    func.return %43
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=80
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 100
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.xor ebx, ebx
    x86.mov [rbp-32], ebx
    x86.jmp register-allocator.main.outer_0.header
  outer_0.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-32]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.outer_0.exit
  outer_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.xor ecx, ecx
    x86.mov [rbp-48], ecx
    x86.jmp register-allocator.main.inner_1.header
  inner_1.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-48]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.inner_1.exit
  inner_1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-56], eax
    x86.mov ecx, [rbp-32]
    x86.mov edx, [rbp-48]
    x86.cmp ecx, edx
    x86.jne register-allocator.main.diag_2.merge
  diag_2:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-64], eax
    x86.mov ecx, 1
    x86.mov edx, [rbp-24]
    x86.add edx, ecx
    x86.mov [rbp-24], edx
    x86.mov rcx, [rbp-64]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.diag_2.merge
  diag_2.merge:
    x86.mov eax, 1
    x86.mov ecx, [rbp-48]
    x86.add ecx, eax
    x86.mov [rbp-48], ecx
    x86.mov edx, [rbp-56]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.inner_1.header
  inner_1.exit:
    x86.mov eax, 1
    x86.mov ecx, [rbp-32]
    x86.add ecx, eax
    x86.mov [rbp-32], ecx
    x86.mov edx, [rbp-40]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.outer_0.header
  outer_0.exit:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-24]
    x86.add eax, ecx
    x86.mov [rbp-72], eax
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
    x86.je register-allocator.main.__range_ok_3
  __range_panic_3:
    x86.lea_symdata rax, [__panic_msg_34]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_3:
    x86.mov eax, [rbp-72]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-80], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-80]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-fibonacci -->
```maxon
function main() returns ExitCode
  var a = 0
  var b = 1
  var i = 0
  while i < 13 'loop'
    var temp = a + b
    a = b
    b = temp
    i = i + 1
  end 'loop'
  return a
end 'main'
```
```exitcode
233
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 1 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    maxon.assign %3 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %4 = maxon.literal {value = 13 : i64}
    %5 = maxon.var_ref {var = i} {type = i64}
    %6 = maxon.binop %5, %4 {op = lt}
    maxon.cond_br %6 [then: loop_0, else: loop_0.exit]
  loop_0:
    __scope_7 = maxon.scope_enter {tag = while}
    %8 = maxon.var_ref {var = a} {type = i64}
    %9 = maxon.var_ref {var = b} {type = i64}
    %10 = maxon.binop %8, %9 {op = add}
    maxon.assign %10 {var = temp} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %11 = maxon.var_ref {var = b} {type = i64}
    maxon.assign %11 {var = a} {kind = i64} {mut = 1 : i1}
    maxon.assign %10 {var = b} {kind = i64} {mut = 1 : i1}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = i} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_7} {tag = block_exit}
    maxon.br loop_0.header
  loop_0.exit:
    %15 = maxon.var_ref {var = a} {type = i64}
    maxon.assign %15 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 0 : i64}
    %17 = maxon.binop %15, %16 {op = lt}
    %18 = maxon.literal {value = 4294967295 : i64}
    %19 = maxon.binop %15, %18 {op = gt}
    %20 = maxon.binop %17, %19 {op = or}
    maxon.cond_br %20 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at int-fibonacci.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %22 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %22
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, a
    %3 = arith.constant {value = 1 : i64}
    memref.store %3, b
    %4 = arith.constant {value = 0 : i64}
    memref.store %4, i
    cf.br loop_0.header
  loop_0.header:
    %5 = arith.constant {value = 13 : i64}
    %6 = memref.load i : i64
    %7 = arith.cmpi lt %6, %5
    cf.cond_br %7 [then: loop_0, else: loop_0.exit]
  loop_0:
    %8 = arith.constant {value = 0 : i64}
    %9 = std.call_runtime @mm_scope_enter %8
    memref.store %9, __scope_7
    %10 = memref.load a : i64
    %11 = memref.load b : i64
    %12 = arith.addi %10, %11
    %13 = memref.load b : i64
    memref.store %13, a
    memref.store %12, b
    %14 = arith.constant {value = 1 : i64}
    %15 = memref.load i : i64
    %16 = arith.addi %15, %14
    memref.store %16, i
    %17 = memref.load __scope_7 : i64
    std.call_runtime @mm_scope_exit %17
    cf.br loop_0.header
  loop_0.exit:
    %18 = memref.load a : i64
    memref.store %18, __range_val_1
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 4294967295 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %24 = memref.lea_symdata __panic_msg_21
    %25 = std.ptr_to_i64 %24
    std.call_runtime @maxon_panic %25
  __range_ok_1:
    %26 = memref.load __range_val_1 : i64
    %27 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %27
    func.return %26
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.mov edx, 1
    x86.mov [rbp-24], edx
    x86.xor ebx, ebx
    x86.mov [rbp-32], ebx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 13
    x86.mov ecx, [rbp-32]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [rbp-24]
    x86.add ecx, edx
    x86.mov ebx, [rbp-24]
    x86.mov [rbp-16], ebx
    x86.mov [rbp-24], ecx
    x86.mov esi, 1
    x86.mov edi, [rbp-32]
    x86.add edi, esi
    x86.mov [rbp-32], edi
    x86.mov r8, [rbp-40]
    x86.mov rcx, r8
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-48], eax
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
    x86.je register-allocator.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_21]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-48]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-56], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-56]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-division-high-pressure -->
```maxon
function main() returns ExitCode
  var a = 10
  var b = 20
  var c = 30
  var d = 40
  var e = 50
  var f = 60
  var g = 70
  var h = 2
  return (a + b + c + d + e + f + g) / h
end 'main'
```
```exitcode
140
```

<!-- test: int-callee-saved-clobber -->
```maxon

typealias Integer = int(i64.min to i64.max)

function useRegs(a Integer, b Integer, c Integer, d Integer) returns Integer
  var x = a + b
  var y = c + d
  var z = x + y
  return z
end 'useRegs'

function main() returns ExitCode
  var sentinel = 42
  var result = useRegs(1, b: 2, c: 3, d: 4)
  return sentinel + result
end 'main'
```
```exitcode
52
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.useRegs}
    %1 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %2 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %3 = maxon.param {index = 2 : i32} {name = c} {type = i64}
    %4 = maxon.param {index = 3 : i32} {name = d} {type = i64}
    %5 = maxon.binop %1, %2 {op = add} {optimalType = i64}
    maxon.assign %5 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.binop %3, %4 {op = add} {optimalType = i64}
    maxon.assign %6 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.binop %5, %6 {op = add}
    maxon.assign %7 {var = z} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %7
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_8 = maxon.scope_enter {tag = register-allocator.main}
    %9 = maxon.literal {value = 42 : i64}
    maxon.assign %9 {var = sentinel} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 1 : i64}
    %11 = maxon.literal {value = 2 : i64}
    %12 = maxon.literal {value = 3 : i64}
    %13 = maxon.literal {value = 4 : i64}
    %14 = maxon.call @register-allocator.useRegs %10, %11, %12, %13
    maxon.assign %14 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.binop %9, %14 {op = add}
    maxon.assign %15 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 0 : i64}
    %17 = maxon.binop %15, %16 {op = lt}
    %18 = maxon.literal {value = 4294967295 : i64}
    %19 = maxon.binop %15, %18 {op = gt}
    %20 = maxon.binop %17, %19 {op = or}
    maxon.cond_br %20 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-callee-saved-clobber.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %22 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.return %22
  }
}
=== standard
module {
  func @register-allocator.useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param a : StdI64
    %3 = func.param b : StdI64
    %4 = func.param c : StdI64
    %5 = func.param d : StdI64
    %6 = arith.addi %2, %3
    %7 = arith.addi %4, %5
    %8 = arith.addi %6, %7
    %9 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %9
    func.return %8
  }
  func @register-allocator.main() -> u32 {
  entry:
    %10 = arith.constant {value = 0 : i64}
    %11 = std.call_runtime @mm_scope_enter %10
    memref.store %11, __scope_8
    %12 = arith.constant {value = 42 : i64}
    %13 = arith.constant {value = 1 : i64}
    %14 = arith.constant {value = 2 : i64}
    %15 = arith.constant {value = 3 : i64}
    %16 = arith.constant {value = 4 : i64}
    %17 = func.call @register-allocator.useRegs %13, %14, %15, %16
    %18 = arith.addi %12, %17
    memref.store %18, __range_val_0
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 4294967295 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %24 = memref.lea_symdata __panic_msg_21
    %25 = std.ptr_to_i64 %24
    std.call_runtime @maxon_panic %25
  __range_ok_0:
    %26 = memref.load __range_val_0 : i64
    %27 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %27
    func.return %26
  }
}
=== x86
module {
  func @register-allocator.useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov [rbp-16], ecx
    x86.mov [rbp-24], edx
    x86.mov [rbp-32], r8
    x86.mov [rbp-40], r9
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, [rbp-24]
    x86.mov edx, [rbp-16]
    x86.add edx, ecx
    x86.mov ebx, [rbp-40]
    x86.mov esi, [rbp-32]
    x86.add esi, ebx
    x86.add edx, esi
    x86.mov edi, [rbp-8]
    x86.mov [rbp-48], edx
    x86.mov rcx, rdi
    x86.call mm_scope_exit
    x86.mov eax, [rbp-48]
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 42
    x86.mov edx, 1
    x86.mov ebx, 2
    x86.mov esi, 3
    x86.mov edi, 4
    x86.mov rcx, rdx
    x86.mov rdx, rbx
    x86.mov r8, rsi
    x86.mov r9, rdi
    x86.call register-allocator.useRegs
    x86.mov r8, 42
    x86.add r8, eax
    x86.mov [rbp-16], r8
    x86.xor r9, r9
    x86.cmp r8, r9
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rcx, 4294967295
    x86.cmp r8, rcx
    x86.setg ecx
    x86.movzx ecx, ecxb
    x86.or eax, ecx
    x86.test eax, eax
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_21]
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

<!-- test: int-float-survives-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getInt() returns Integer
  return 40
end 'getInt'

function main() returns ExitCode
  var f = 3.14
  var x = getInt()
  return trunc(f) + x
end 'main'
```
```exitcode
43
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.getInt() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.getInt}
    %1 = maxon.literal {value = 40 : i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %1
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_2 = maxon.scope_enter {tag = register-allocator.main}
    %3 = maxon.literal {value = 3.14 : f64}
    maxon.assign %3 {var = f} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.call @register-allocator.getInt
    maxon.assign %4 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.trunc %3
    %6 = maxon.binop %5, %4 {op = add}
    maxon.assign %6 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 0 : i64}
    %8 = maxon.binop %6, %7 {op = lt}
    %9 = maxon.literal {value = 4294967295 : i64}
    %10 = maxon.binop %6, %9 {op = gt}
    %11 = maxon.binop %8, %10 {op = or}
    maxon.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-float-survives-call.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %13 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_2} {tag = return_cleanup}
    maxon.return %13
  }
}
=== standard
module {
  func @register-allocator.getInt() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 40 : i64}
    %3 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %3
    func.return %2
  }
  func @register-allocator.main() -> u32 {
  entry:
    %4 = arith.constant {value = 0 : i64}
    %5 = std.call_runtime @mm_scope_enter %4
    memref.store %5, __scope_2
    %6 = arith.float_constant {value = 3.14 : f64}
    %7 = func.call @register-allocator.getInt
    %8 = arith.fptosi %6
    %9 = arith.addi %8, %7
    memref.store %9, __range_val_0
    %10 = arith.constant {value = 0 : i64}
    %11 = arith.cmpi lt %9, %10
    %12 = arith.constant {value = 4294967295 : i64}
    %13 = arith.cmpi gt %9, %12
    %14 = arith.ori1 %11, %13
    cf.cond_br %14 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %15 = memref.lea_symdata __panic_msg_12
    %16 = std.ptr_to_i64 %15
    std.call_runtime @maxon_panic %16
  __range_ok_0:
    %17 = memref.load __range_val_0 : i64
    %18 = memref.load __scope_2 : i64
    std.call_runtime @mm_scope_exit %18
    func.return %17
  }
}
=== x86
module {
  func @register-allocator.getInt() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov eax, 40
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, 40
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.movsd xmm0, [rip+__float_3.14]
    x86.movsd [rbp-24], xmm0
    x86.call register-allocator.getInt
    x86.movsd xmm0, [rbp-24]
    x86.cvttsd2si ecx, xmm0
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.cmp ecx, edx
    x86.setl ebx
    x86.movzx ebx, ebxb
    x86.mov rsi, 4294967295
    x86.cmp rcx, rsi
    x86.setg edi
    x86.movzx edi, edib
    x86.or ebx, edi
    x86.test ebx, ebx
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_12]
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

<!-- test: int-sequential-divisions -->
```maxon
function main() returns ExitCode
  var a = 100
  var b = 5
  var c = 84
  var d = 4
  return a / b + c / d
end 'main'
```
```exitcode
41
```

<!-- test: int-remainder-in-arithmetic -->
```maxon
function main() returns ExitCode
  var a = 100
  var b = 7
  var c = 10
  var rem = a mod b
  return rem * c
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 100 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 7 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 10 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.binop %1, %2 {op = mod}
    maxon.assign %4 {var = rem} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %4, %3 {op = mul}
    maxon.assign %5 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-remainder-in-arithmetic.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %12 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %12
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 100 : i64}
    %3 = arith.constant {value = 7 : i64}
    %4 = arith.constant {value = 10 : i64}
    %5 = arith.remsi %2, %3
    %6 = arith.muli %5, %4
    memref.store %6, __range_val_0
    %7 = arith.constant {value = 0 : i64}
    %8 = arith.cmpi lt %6, %7
    %9 = arith.constant {value = 4294967295 : i64}
    %10 = arith.cmpi gt %6, %9
    %11 = arith.ori1 %8, %10
    cf.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %12 = memref.lea_symdata __panic_msg_11
    %13 = std.ptr_to_i64 %12
    std.call_runtime @maxon_panic %13
  __range_ok_0:
    %14 = memref.load __range_val_0 : i64
    %15 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %15
    func.return %14
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 100
    x86.mov edx, 7
    x86.mov ebx, 10
    x86.mov esi, edx
    x86.mov eax, ecx
    x86.cqo
    x86.idiv esi
    x86.imul edx, ebx
    x86.mov [rbp-16], edx
    x86.xor esi, esi
    x86.cmp edx, esi
    x86.setl edi
    x86.movzx edi, edib
    x86.mov r8, 4294967295
    x86.cmp rdx, r8
    x86.setg r9
    x86.movzx r9, r9b
    x86.or edi, r9
    x86.test edi, edi
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_11]
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

<!-- test: int-call-arg-reverse -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sub(a Integer, b Integer) returns Integer
  return a - b
end 'sub'

function main() returns ExitCode
  var x = 10
  var y = 3
  var result = sub(y, b: x)
  return result + 45
end 'main'
```
```exitcode
38
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.sub(a: i64, b: i64) -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.sub}
    %1 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %2 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %3 = maxon.binop %1, %2 {op = sub} {optimalType = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %3
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_4 = maxon.scope_enter {tag = register-allocator.main}
    %5 = maxon.literal {value = 10 : i64}
    maxon.assign %5 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 3 : i64}
    maxon.assign %6 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.call @register-allocator.sub %6, %5
    maxon.assign %7 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 45 : i64}
    %9 = maxon.binop %7, %8 {op = add}
    maxon.assign %9 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %9, %10 {op = lt}
    %12 = maxon.literal {value = 4294967295 : i64}
    %13 = maxon.binop %9, %12 {op = gt}
    %14 = maxon.binop %11, %13 {op = or}
    maxon.cond_br %14 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-call-arg-reverse.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %16 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %16
  }
}
=== standard
module {
  func @register-allocator.sub(a: i64, b: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param a : StdI64
    %3 = func.param b : StdI64
    %4 = arith.subi %2, %3
    %5 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %5
    func.return %4
  }
  func @register-allocator.main() -> u32 {
  entry:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_4
    %8 = arith.constant {value = 10 : i64}
    %9 = arith.constant {value = 3 : i64}
    %10 = func.call @register-allocator.sub %9, %8
    %11 = arith.constant {value = 45 : i64}
    %12 = arith.addi %10, %11
    memref.store %12, __range_val_0
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.cmpi lt %12, %13
    %15 = arith.constant {value = 4294967295 : i64}
    %16 = arith.cmpi gt %12, %15
    %17 = arith.ori1 %14, %16
    cf.cond_br %17 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %18 = memref.lea_symdata __panic_msg_15
    %19 = std.ptr_to_i64 %18
    std.call_runtime @maxon_panic %19
  __range_ok_0:
    %20 = memref.load __range_val_0 : i64
    %21 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %21
    func.return %20
  }
}
=== x86
module {
  func @register-allocator.sub(a: i64, b: i64) -> i64 {
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
    x86.sub edx, ecx
    x86.mov ebx, [rbp-8]
    x86.mov [rbp-32], edx
    x86.mov rcx, rbx
    x86.call mm_scope_exit
    x86.mov eax, [rbp-32]
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 10
    x86.mov edx, 3
    x86.xchg rdx, rcx
    x86.call register-allocator.sub
    x86.mov ebx, 45
    x86.add eax, ebx
    x86.mov [rbp-16], eax
    x86.xor esi, esi
    x86.cmp eax, esi
    x86.setl edi
    x86.movzx edi, edib
    x86.mov r8, 4294967295
    x86.cmp rax, r8
    x86.setg r9
    x86.movzx r9, r9b
    x86.or edi, r9
    x86.test edi, edi
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_15]
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

<!-- test: int-subtraction-high-pressure -->
```maxon
function main() returns ExitCode
  var a = 100
  var b = 1
  var c = 2
  var d = 3
  var e = 4
  var f = 5
  var g = 6
  var h = 7
  return a - b - c - d - e - f - g - h
end 'main'
```
```exitcode
72
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 100 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 1 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 2 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 3 : i64}
    maxon.assign %4 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 4 : i64}
    maxon.assign %5 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 5 : i64}
    maxon.assign %6 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 6 : i64}
    maxon.assign %7 {var = g} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 7 : i64}
    maxon.assign %8 {var = h} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.binop %1, %2 {op = sub}
    %10 = maxon.binop %9, %3 {op = sub}
    %11 = maxon.binop %10, %4 {op = sub}
    %12 = maxon.binop %11, %5 {op = sub}
    %13 = maxon.binop %12, %6 {op = sub}
    %14 = maxon.binop %13, %7 {op = sub}
    %15 = maxon.binop %14, %8 {op = sub}
    maxon.assign %15 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 0 : i64}
    %17 = maxon.binop %15, %16 {op = lt}
    %18 = maxon.literal {value = 4294967295 : i64}
    %19 = maxon.binop %15, %18 {op = gt}
    %20 = maxon.binop %17, %19 {op = or}
    maxon.cond_br %20 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-subtraction-high-pressure.test:11: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %22 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %22
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 100 : i64}
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.constant {value = 2 : i64}
    %5 = arith.constant {value = 3 : i64}
    %6 = arith.constant {value = 4 : i64}
    %7 = arith.constant {value = 5 : i64}
    %8 = arith.constant {value = 6 : i64}
    %9 = arith.constant {value = 7 : i64}
    %10 = arith.subi %2, %3
    %11 = arith.subi %10, %4
    %12 = arith.subi %11, %5
    %13 = arith.subi %12, %6
    %14 = arith.subi %13, %7
    %15 = arith.subi %14, %8
    %16 = arith.subi %15, %9
    memref.store %16, __range_val_0
    %17 = arith.constant {value = 0 : i64}
    %18 = arith.cmpi lt %16, %17
    %19 = arith.constant {value = 4294967295 : i64}
    %20 = arith.cmpi gt %16, %19
    %21 = arith.ori1 %18, %20
    cf.cond_br %21 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %22 = memref.lea_symdata __panic_msg_21
    %23 = std.ptr_to_i64 %22
    std.call_runtime @maxon_panic %23
  __range_ok_0:
    %24 = memref.load __range_val_0 : i64
    %25 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %25
    func.return %24
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 100
    x86.mov edx, 1
    x86.mov ebx, 2
    x86.mov esi, 3
    x86.mov edi, 4
    x86.mov r8, 5
    x86.mov r9, 6
    x86.mov eax, 7
    x86.sub ecx, edx
    x86.sub ecx, ebx
    x86.sub ecx, esi
    x86.sub ecx, edi
    x86.sub ecx, r8
    x86.sub ecx, r9
    x86.sub ecx, eax
    x86.mov [rbp-16], ecx
    x86.xor eax, eax
    x86.cmp ecx, eax
    x86.setl eax
    x86.movzx eax, eaxb
    x86.mov rdx, 4294967295
    x86.cmp rcx, rdx
    x86.setg ecx
    x86.movzx ecx, ecxb
    x86.or eax, ecx
    x86.test eax, eax
    x86.je register-allocator.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_21]
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

<!-- test: int-multi-var-branch-merge -->
```maxon
function main() returns ExitCode
  var x = 0
  var y = 0
  var z = 0
  if 1 < 2 'branch'
    x = 10
    y = 20
    z = 12
  end 'branch' else 'other'
    x = 1
    y = 2
    z = 3
  end 'other'
  return x + y + z
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    maxon.assign %3 {var = z} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.literal {value = 2 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    maxon.cond_br %6 [then: branch_0, else: other_1]
  branch_0:
    __scope_7 = maxon.scope_enter {tag = if_then}
    %8 = maxon.literal {value = 10 : i64}
    maxon.assign %8 {var = x} {kind = i64} {mut = 1 : i1}
    %9 = maxon.literal {value = 20 : i64}
    maxon.assign %9 {var = y} {kind = i64} {mut = 1 : i1}
    %10 = maxon.literal {value = 12 : i64}
    maxon.assign %10 {var = z} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_7} {tag = block_exit}
    maxon.br branch_0.merge
  other_1:
    __scope_11 = maxon.scope_enter {tag = else}
    %12 = maxon.literal {value = 1 : i64}
    maxon.assign %12 {var = x} {kind = i64} {mut = 1 : i1}
    %13 = maxon.literal {value = 2 : i64}
    maxon.assign %13 {var = y} {kind = i64} {mut = 1 : i1}
    %14 = maxon.literal {value = 3 : i64}
    maxon.assign %14 {var = z} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_11} {tag = block_exit}
    maxon.br branch_0.merge
  branch_0.merge:
    %15 = maxon.var_ref {var = x} {type = i64}
    %16 = maxon.var_ref {var = y} {type = i64}
    %17 = maxon.binop %15, %16 {op = add}
    %18 = maxon.var_ref {var = z} {type = i64}
    %19 = maxon.binop %17, %18 {op = add}
    maxon.assign %19 {var = __range_val_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %19, %20 {op = lt}
    %22 = maxon.literal {value = 4294967295 : i64}
    %23 = maxon.binop %19, %22 {op = gt}
    %24 = maxon.binop %21, %23 {op = or}
    maxon.cond_br %24 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at int-multi-var-branch-merge.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    %26 = maxon.var_ref {var = __range_val_2} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %26
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %5 = arith.constant {value = 1 : i64}
    %6 = arith.constant {value = 2 : i64}
    %7 = arith.cmpi lt %5, %6
    cf.cond_br %7 [then: branch_0, else: other_1]
  branch_0:
    %8 = arith.constant {value = 0 : i64}
    %9 = std.call_runtime @mm_scope_enter %8
    memref.store %9, __scope_7
    %10 = arith.constant {value = 10 : i64}
    memref.store %10, x
    %11 = arith.constant {value = 20 : i64}
    memref.store %11, y
    %12 = arith.constant {value = 12 : i64}
    memref.store %12, z
    %13 = memref.load __scope_7 : i64
    std.call_runtime @mm_scope_exit %13
    cf.br branch_0.merge
  other_1:
    %14 = arith.constant {value = 0 : i64}
    %15 = std.call_runtime @mm_scope_enter %14
    memref.store %15, __scope_11
    %16 = arith.constant {value = 1 : i64}
    memref.store %16, x
    %17 = arith.constant {value = 2 : i64}
    memref.store %17, y
    %18 = arith.constant {value = 3 : i64}
    memref.store %18, z
    %19 = memref.load __scope_11 : i64
    std.call_runtime @mm_scope_exit %19
    cf.br branch_0.merge
  branch_0.merge:
    %20 = memref.load x : i64
    %21 = memref.load y : i64
    %22 = arith.addi %20, %21
    %23 = memref.load z : i64
    %24 = arith.addi %22, %23
    memref.store %24, __range_val_2
    %25 = arith.constant {value = 0 : i64}
    %26 = arith.cmpi lt %24, %25
    %27 = arith.constant {value = 4294967295 : i64}
    %28 = arith.cmpi gt %24, %27
    %29 = arith.ori1 %26, %28
    cf.cond_br %29 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %30 = memref.lea_symdata __panic_msg_25
    %31 = std.ptr_to_i64 %30
    std.call_runtime @maxon_panic %31
  __range_ok_2:
    %32 = memref.load __range_val_2 : i64
    %33 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %33
    func.return %32
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov edx, 2
    x86.cmp ecx, edx
    x86.jge register-allocator.main.other_1
  branch_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-16], eax
    x86.mov ecx, 10
    x86.mov [rbp-24], ecx
    x86.mov edx, 20
    x86.mov [rbp-32], edx
    x86.mov ebx, 12
    x86.mov [rbp-40], ebx
    x86.mov rcx, [rbp-16]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.branch_0.merge
  other_1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-48], eax
    x86.mov ecx, 1
    x86.mov [rbp-24], ecx
    x86.mov edx, 2
    x86.mov [rbp-32], edx
    x86.mov ebx, 3
    x86.mov [rbp-40], ebx
    x86.mov rcx, [rbp-48]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.branch_0.merge
  branch_0.merge:
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-32]
    x86.add eax, ecx
    x86.mov edx, [rbp-40]
    x86.add eax, edx
    x86.mov [rbp-56], eax
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
    x86.je register-allocator.main.__range_ok_2
  __range_panic_2:
    x86.lea_symdata rax, [__panic_msg_25]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_2:
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-64], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-64]
    x86.epilogue
    x86.ret
  }
}
```

### Level 7: Match Statements and Expressions

<!-- test: match-statement-simple -->
```maxon
function main() returns ExitCode
  var x = 2
  match x 'check'
    1 then return 10
    2 then return 20
    default then return 0
  end 'check'
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %1 {var = __match_check_0} {kind = i64} {decl = 1 : i1}
    maxon.br check_0.cmp0
  check_0.cmp0:
    %2 = maxon.var_ref {var = __match_check_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq}
    maxon.cond_br %4 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    __scope_5 = maxon.scope_enter {tag = match_case}
    %6 = maxon.literal {value = 10 : i64}
    maxon.scope_exit {scope = __scope_5} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %6
  check_0.cmp1:
    %7 = maxon.var_ref {var = __match_check_0} {type = i64}
    %8 = maxon.literal {value = 2 : i64}
    %9 = maxon.binop %7, %8 {op = eq}
    maxon.cond_br %9 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    __scope_10 = maxon.scope_enter {tag = match_case}
    %11 = maxon.literal {value = 20 : i64}
    maxon.scope_exit {scope = __scope_10} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %11
  check_0.case2:
    __scope_12 = maxon.scope_enter {tag = match_case}
    %13 = maxon.literal {value = 0 : i64}
    maxon.scope_exit {scope = __scope_12} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %13
  check_0.merge:
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 2 : i64}
    memref.store %2, __match_check_0
    cf.br check_0.cmp0
  check_0.cmp0:
    %3 = memref.load __match_check_0 : i64
    %4 = arith.constant {value = 1 : i64}
    %5 = arith.cmpi eq %3, %4
    cf.cond_br %5 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_5
    %8 = arith.constant {value = 10 : i64}
    %9 = memref.load __scope_5 : i64
    std.call_runtime @mm_scope_exit %9
    %10 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %10
    func.return %8
  check_0.cmp1:
    %11 = memref.load __match_check_0 : i64
    %12 = arith.constant {value = 2 : i64}
    %13 = arith.cmpi eq %11, %12
    cf.cond_br %13 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %14 = arith.constant {value = 0 : i64}
    %15 = std.call_runtime @mm_scope_enter %14
    memref.store %15, __scope_10
    %16 = arith.constant {value = 20 : i64}
    %17 = memref.load __scope_10 : i64
    std.call_runtime @mm_scope_exit %17
    %18 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %18
    func.return %16
  check_0.case2:
    %19 = arith.constant {value = 0 : i64}
    %20 = std.call_runtime @mm_scope_enter %19
    memref.store %20, __scope_12
    %21 = arith.constant {value = 0 : i64}
    %22 = memref.load __scope_12 : i64
    std.call_runtime @mm_scope_exit %22
    %23 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %23
    func.return %21
  check_0.merge:
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.check_0.cmp0
  check_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.check_0.cmp1
  check_0.case0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-24], eax
    x86.mov eax, 10
    x86.mov ecx, [rbp-24]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov eax, 10
    x86.epilogue
    x86.ret
  check_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.check_0.case2
  check_0.case1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-32], eax
    x86.mov eax, 20
    x86.mov ecx, [rbp-32]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov eax, 20
    x86.epilogue
    x86.ret
  check_0.case2:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-40]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  check_0.merge:
  }
}
```

<!-- test: match-statement-assignment -->
```maxon
function main() returns ExitCode
  var x = 2
  var result = 0
  match x 'process'
    1 then result = 100
    2 then result = 200
    default then result = 0
  end 'process'
  return result
end 'main'
```
```exitcode
200
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %1 {var = __match_process_0} {kind = i64} {decl = 1 : i1}
    maxon.br process_0.cmp0
  process_0.cmp0:
    %3 = maxon.var_ref {var = __match_process_0} {type = i64}
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.binop %3, %4 {op = eq}
    maxon.cond_br %5 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    __scope_6 = maxon.scope_enter {tag = match_case}
    %7 = maxon.literal {value = 100 : i64}
    maxon.assign %7 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_6} {tag = block_exit}
    maxon.br process_0.merge
  process_0.cmp1:
    %8 = maxon.var_ref {var = __match_process_0} {type = i64}
    %9 = maxon.literal {value = 2 : i64}
    %10 = maxon.binop %8, %9 {op = eq}
    maxon.cond_br %10 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    __scope_11 = maxon.scope_enter {tag = match_case}
    %12 = maxon.literal {value = 200 : i64}
    maxon.assign %12 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_11} {tag = block_exit}
    maxon.br process_0.merge
  process_0.case2:
    __scope_13 = maxon.scope_enter {tag = match_case}
    %14 = maxon.literal {value = 0 : i64}
    maxon.assign %14 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_13} {tag = block_exit}
    maxon.br process_0.merge
  process_0.merge:
    %15 = maxon.var_ref {var = result} {type = i64}
    maxon.assign %15 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 0 : i64}
    %17 = maxon.binop %15, %16 {op = lt}
    %18 = maxon.literal {value = 4294967295 : i64}
    %19 = maxon.binop %15, %18 {op = gt}
    %20 = maxon.binop %17, %19 {op = or}
    maxon.cond_br %20 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-statement-assignment.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %22 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %22
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 2 : i64}
    memref.store %2, __match_process_0
    cf.br process_0.cmp0
  process_0.cmp0:
    %4 = memref.load __match_process_0 : i64
    %5 = arith.constant {value = 1 : i64}
    %6 = arith.cmpi eq %4, %5
    cf.cond_br %6 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    %7 = arith.constant {value = 0 : i64}
    %8 = std.call_runtime @mm_scope_enter %7
    memref.store %8, __scope_6
    %9 = arith.constant {value = 100 : i64}
    memref.store %9, result
    %10 = memref.load __scope_6 : i64
    std.call_runtime @mm_scope_exit %10
    cf.br process_0.merge
  process_0.cmp1:
    %11 = memref.load __match_process_0 : i64
    %12 = arith.constant {value = 2 : i64}
    %13 = arith.cmpi eq %11, %12
    cf.cond_br %13 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    %14 = arith.constant {value = 0 : i64}
    %15 = std.call_runtime @mm_scope_enter %14
    memref.store %15, __scope_11
    %16 = arith.constant {value = 200 : i64}
    memref.store %16, result
    %17 = memref.load __scope_11 : i64
    std.call_runtime @mm_scope_exit %17
    cf.br process_0.merge
  process_0.case2:
    %18 = arith.constant {value = 0 : i64}
    %19 = std.call_runtime @mm_scope_enter %18
    memref.store %19, __scope_13
    %20 = arith.constant {value = 0 : i64}
    memref.store %20, result
    %21 = memref.load __scope_13 : i64
    std.call_runtime @mm_scope_exit %21
    cf.br process_0.merge
  process_0.merge:
    %22 = memref.load result : i64
    memref.store %22, __range_val_1
    %23 = arith.constant {value = 0 : i64}
    %24 = arith.cmpi lt %22, %23
    %25 = arith.constant {value = 4294967295 : i64}
    %26 = arith.cmpi gt %22, %25
    %27 = arith.ori1 %24, %26
    cf.cond_br %27 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %28 = memref.lea_symdata __panic_msg_21
    %29 = std.ptr_to_i64 %28
    std.call_runtime @maxon_panic %29
  __range_ok_1:
    %30 = memref.load __range_val_1 : i64
    %31 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %31
    func.return %30
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.process_0.cmp0
  process_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.process_0.cmp1
  process_0.case0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-24], eax
    x86.mov ecx, 100
    x86.mov [rbp-32], ecx
    x86.mov edx, [rbp-24]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.process_0.merge
  process_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.process_0.case2
  process_0.case1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.mov ecx, 200
    x86.mov [rbp-32], ecx
    x86.mov edx, [rbp-40]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.process_0.merge
  process_0.case2:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-48], eax
    x86.xor ecx, ecx
    x86.mov [rbp-32], ecx
    x86.mov edx, [rbp-48]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.process_0.merge
  process_0.merge:
    x86.mov eax, [rbp-32]
    x86.mov [rbp-56], eax
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
    x86.je register-allocator.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_21]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-64], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-64]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: match-statement-or-patterns -->
```maxon
function main() returns ExitCode
  var x = 3
  match x 'check'
    1 or 2 then return 10
    3 or 4 then return 20
    default then return 0
  end 'check'
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 3 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %1 {var = __match_check_0} {kind = i64} {decl = 1 : i1}
    maxon.br check_0.cmp0
  check_0.cmp0:
    %2 = maxon.var_ref {var = __match_check_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq}
    %5 = maxon.var_ref {var = __match_check_0} {type = i64}
    %6 = maxon.literal {value = 2 : i64}
    %7 = maxon.binop %5, %6 {op = eq}
    %8 = maxon.binop %4, %7 {op = or}
    maxon.cond_br %8 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    __scope_9 = maxon.scope_enter {tag = match_case}
    %10 = maxon.literal {value = 10 : i64}
    maxon.scope_exit {scope = __scope_9} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %10
  check_0.cmp1:
    %11 = maxon.var_ref {var = __match_check_0} {type = i64}
    %12 = maxon.literal {value = 3 : i64}
    %13 = maxon.binop %11, %12 {op = eq}
    %14 = maxon.var_ref {var = __match_check_0} {type = i64}
    %15 = maxon.literal {value = 4 : i64}
    %16 = maxon.binop %14, %15 {op = eq}
    %17 = maxon.binop %13, %16 {op = or}
    maxon.cond_br %17 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    __scope_18 = maxon.scope_enter {tag = match_case}
    %19 = maxon.literal {value = 20 : i64}
    maxon.scope_exit {scope = __scope_18} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %19
  check_0.case2:
    __scope_20 = maxon.scope_enter {tag = match_case}
    %21 = maxon.literal {value = 0 : i64}
    maxon.scope_exit {scope = __scope_20} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %21
  check_0.merge:
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 3 : i64}
    memref.store %2, __match_check_0
    cf.br check_0.cmp0
  check_0.cmp0:
    %3 = memref.load __match_check_0 : i64
    %4 = arith.constant {value = 1 : i64}
    %5 = arith.cmpi eq %3, %4
    %6 = memref.load __match_check_0 : i64
    %7 = arith.constant {value = 2 : i64}
    %8 = arith.cmpi eq %6, %7
    %9 = arith.ori1 %5, %8
    cf.cond_br %9 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %10 = arith.constant {value = 0 : i64}
    %11 = std.call_runtime @mm_scope_enter %10
    memref.store %11, __scope_9
    %12 = arith.constant {value = 10 : i64}
    %13 = memref.load __scope_9 : i64
    std.call_runtime @mm_scope_exit %13
    %14 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %14
    func.return %12
  check_0.cmp1:
    %15 = memref.load __match_check_0 : i64
    %16 = arith.constant {value = 3 : i64}
    %17 = arith.cmpi eq %15, %16
    %18 = memref.load __match_check_0 : i64
    %19 = arith.constant {value = 4 : i64}
    %20 = arith.cmpi eq %18, %19
    %21 = arith.ori1 %17, %20
    cf.cond_br %21 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %22 = arith.constant {value = 0 : i64}
    %23 = std.call_runtime @mm_scope_enter %22
    memref.store %23, __scope_18
    %24 = arith.constant {value = 20 : i64}
    %25 = memref.load __scope_18 : i64
    std.call_runtime @mm_scope_exit %25
    %26 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %26
    func.return %24
  check_0.case2:
    %27 = arith.constant {value = 0 : i64}
    %28 = std.call_runtime @mm_scope_enter %27
    memref.store %28, __scope_20
    %29 = arith.constant {value = 0 : i64}
    %30 = memref.load __scope_20 : i64
    std.call_runtime @mm_scope_exit %30
    %31 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %31
    func.return %29
  check_0.merge:
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 3
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.check_0.cmp0
  check_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.sete edx
    x86.movzx edx, edxb
    x86.mov ebx, [rbp-16]
    x86.mov esi, 2
    x86.cmp ebx, esi
    x86.sete edi
    x86.movzx edi, edib
    x86.or edx, edi
    x86.test edx, edx
    x86.je register-allocator.main.check_0.cmp1
  check_0.case0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-24], eax
    x86.mov eax, 10
    x86.mov ecx, [rbp-24]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov eax, 10
    x86.epilogue
    x86.ret
  check_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 3
    x86.cmp eax, ecx
    x86.sete edx
    x86.movzx edx, edxb
    x86.mov ebx, [rbp-16]
    x86.mov esi, 4
    x86.cmp ebx, esi
    x86.sete edi
    x86.movzx edi, edib
    x86.or edx, edi
    x86.test edx, edx
    x86.je register-allocator.main.check_0.case2
  check_0.case1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-32], eax
    x86.mov eax, 20
    x86.mov ecx, [rbp-32]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov eax, 20
    x86.epilogue
    x86.ret
  check_0.case2:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-40]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  check_0.merge:
  }
}
```

<!-- test: match-statement-fallthrough -->
```maxon
function main() returns ExitCode
  var x = 1
  var result = 0
  match x 'cascade'
    1 then result = result + 10 and fallthrough
    2 then result = result + 20 and fallthrough
    3 then result = result + 30
    default then result = 100
  end 'cascade'
  return result
end 'main'
```
```exitcode
60
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %1 {var = __match_cascade_0} {kind = i64} {decl = 1 : i1}
    maxon.br cascade_0.cmp0
  cascade_0.cmp0:
    %3 = maxon.var_ref {var = __match_cascade_0} {type = i64}
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.binop %3, %4 {op = eq}
    maxon.cond_br %5 [then: cascade_0.case0, else: cascade_0.cmp1]
  cascade_0.case0:
    __scope_6 = maxon.scope_enter {tag = match_case}
    %7 = maxon.literal {value = 10 : i64}
    %8 = maxon.var_ref {var = result} {type = i64}
    %9 = maxon.binop %8, %7 {op = add}
    maxon.assign %9 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_6} {tag = block_exit}
    maxon.br cascade_0.case1
  cascade_0.cmp1:
    %10 = maxon.var_ref {var = __match_cascade_0} {type = i64}
    %11 = maxon.literal {value = 2 : i64}
    %12 = maxon.binop %10, %11 {op = eq}
    maxon.cond_br %12 [then: cascade_0.case1, else: cascade_0.cmp2]
  cascade_0.case1:
    __scope_13 = maxon.scope_enter {tag = match_case}
    %14 = maxon.literal {value = 20 : i64}
    %15 = maxon.var_ref {var = result} {type = i64}
    %16 = maxon.binop %15, %14 {op = add}
    maxon.assign %16 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_13} {tag = block_exit}
    maxon.br cascade_0.case2
  cascade_0.cmp2:
    %17 = maxon.var_ref {var = __match_cascade_0} {type = i64}
    %18 = maxon.literal {value = 3 : i64}
    %19 = maxon.binop %17, %18 {op = eq}
    maxon.cond_br %19 [then: cascade_0.case2, else: cascade_0.case3]
  cascade_0.case2:
    __scope_20 = maxon.scope_enter {tag = match_case}
    %21 = maxon.literal {value = 30 : i64}
    %22 = maxon.var_ref {var = result} {type = i64}
    %23 = maxon.binop %22, %21 {op = add}
    maxon.assign %23 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_20} {tag = block_exit}
    maxon.br cascade_0.merge
  cascade_0.case3:
    __scope_24 = maxon.scope_enter {tag = match_case}
    %25 = maxon.literal {value = 100 : i64}
    maxon.assign %25 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_24} {tag = block_exit}
    maxon.br cascade_0.merge
  cascade_0.merge:
    %26 = maxon.var_ref {var = result} {type = i64}
    maxon.assign %26 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %27 = maxon.literal {value = 0 : i64}
    %28 = maxon.binop %26, %27 {op = lt}
    %29 = maxon.literal {value = 4294967295 : i64}
    %30 = maxon.binop %26, %29 {op = gt}
    %31 = maxon.binop %28, %30 {op = or}
    maxon.cond_br %31 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-statement-fallthrough.test:11: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %33 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %33
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, result
    memref.store %2, __match_cascade_0
    cf.br cascade_0.cmp0
  cascade_0.cmp0:
    %4 = memref.load __match_cascade_0 : i64
    %5 = arith.constant {value = 1 : i64}
    %6 = arith.cmpi eq %4, %5
    cf.cond_br %6 [then: cascade_0.case0, else: cascade_0.cmp1]
  cascade_0.case0:
    %7 = arith.constant {value = 0 : i64}
    %8 = std.call_runtime @mm_scope_enter %7
    memref.store %8, __scope_6
    %9 = arith.constant {value = 10 : i64}
    %10 = memref.load result : i64
    %11 = arith.addi %10, %9
    memref.store %11, result
    %12 = memref.load __scope_6 : i64
    std.call_runtime @mm_scope_exit %12
    cf.br cascade_0.case1
  cascade_0.cmp1:
    %13 = memref.load __match_cascade_0 : i64
    %14 = arith.constant {value = 2 : i64}
    %15 = arith.cmpi eq %13, %14
    cf.cond_br %15 [then: cascade_0.case1, else: cascade_0.cmp2]
  cascade_0.case1:
    %16 = arith.constant {value = 0 : i64}
    %17 = std.call_runtime @mm_scope_enter %16
    memref.store %17, __scope_13
    %18 = arith.constant {value = 20 : i64}
    %19 = memref.load result : i64
    %20 = arith.addi %19, %18
    memref.store %20, result
    %21 = memref.load __scope_13 : i64
    std.call_runtime @mm_scope_exit %21
    cf.br cascade_0.case2
  cascade_0.cmp2:
    %22 = memref.load __match_cascade_0 : i64
    %23 = arith.constant {value = 3 : i64}
    %24 = arith.cmpi eq %22, %23
    cf.cond_br %24 [then: cascade_0.case2, else: cascade_0.case3]
  cascade_0.case2:
    %25 = arith.constant {value = 0 : i64}
    %26 = std.call_runtime @mm_scope_enter %25
    memref.store %26, __scope_20
    %27 = arith.constant {value = 30 : i64}
    %28 = memref.load result : i64
    %29 = arith.addi %28, %27
    memref.store %29, result
    %30 = memref.load __scope_20 : i64
    std.call_runtime @mm_scope_exit %30
    cf.br cascade_0.merge
  cascade_0.case3:
    %31 = arith.constant {value = 0 : i64}
    %32 = std.call_runtime @mm_scope_enter %31
    memref.store %32, __scope_24
    %33 = arith.constant {value = 100 : i64}
    memref.store %33, result
    %34 = memref.load __scope_24 : i64
    std.call_runtime @mm_scope_exit %34
    cf.br cascade_0.merge
  cascade_0.merge:
    %35 = memref.load result : i64
    memref.store %35, __range_val_1
    %36 = arith.constant {value = 0 : i64}
    %37 = arith.cmpi lt %35, %36
    %38 = arith.constant {value = 4294967295 : i64}
    %39 = arith.cmpi gt %35, %38
    %40 = arith.ori1 %37, %39
    cf.cond_br %40 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %41 = memref.lea_symdata __panic_msg_32
    %42 = std.ptr_to_i64 %41
    std.call_runtime @maxon_panic %42
  __range_ok_1:
    %43 = memref.load __range_val_1 : i64
    %44 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %44
    func.return %43
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=80
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.xor edx, edx
    x86.mov [rbp-16], edx
    x86.mov [rbp-24], ecx
    x86.jmp register-allocator.main.cascade_0.cmp0
  cascade_0.cmp0:
    x86.mov eax, [rbp-24]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.cascade_0.cmp1
  cascade_0.case0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-32], eax
    x86.mov ecx, 10
    x86.mov edx, [rbp-16]
    x86.add edx, ecx
    x86.mov [rbp-16], edx
    x86.mov rcx, [rbp-32]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.cascade_0.case1
  cascade_0.cmp1:
    x86.mov eax, [rbp-24]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.cascade_0.cmp2
  cascade_0.case1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.mov ecx, 20
    x86.mov edx, [rbp-16]
    x86.add edx, ecx
    x86.mov [rbp-16], edx
    x86.mov rcx, [rbp-40]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.cascade_0.case2
  cascade_0.cmp2:
    x86.mov eax, [rbp-24]
    x86.mov ecx, 3
    x86.cmp eax, ecx
    x86.jne register-allocator.main.cascade_0.case3
  cascade_0.case2:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-48], eax
    x86.mov ecx, 30
    x86.mov edx, [rbp-16]
    x86.add edx, ecx
    x86.mov [rbp-16], edx
    x86.mov rcx, [rbp-48]
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.cascade_0.merge
  cascade_0.case3:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-56], eax
    x86.mov ecx, 100
    x86.mov [rbp-16], ecx
    x86.mov edx, [rbp-56]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.cascade_0.merge
  cascade_0.merge:
    x86.mov eax, [rbp-16]
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
    x86.je register-allocator.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_32]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-64]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-72], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-72]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: match-expression-basic -->
```maxon
function main() returns ExitCode
  var x = 2
  let result = match x 'eval'
    1 gives 10
    2 gives 20
    default gives 0
  end 'eval'
  return result
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = __matchexpr_eval_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %1 {var = __match_eval_0} {kind = i64} {decl = 1 : i1}
    maxon.br eval_0.cmp0
  eval_0.cmp0:
    %3 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.binop %3, %4 {op = eq}
    maxon.cond_br %5 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %6 = maxon.literal {value = 10 : i64}
    maxon.assign %6 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %7 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %8 = maxon.literal {value = 2 : i64}
    %9 = maxon.binop %7, %8 {op = eq}
    maxon.cond_br %9 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %10 = maxon.literal {value = 20 : i64}
    maxon.assign %10 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.case2:
    %11 = maxon.literal {value = 0 : i64}
    maxon.assign %11 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.merge:
    %12 = maxon.var_ref {var = __matchexpr_eval_0} {type = i64}
    maxon.assign %12 {var = result} {kind = i64} {decl = 1 : i1}
    maxon.assign %12 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.literal {value = 0 : i64}
    %14 = maxon.binop %12, %13 {op = lt}
    %15 = maxon.literal {value = 4294967295 : i64}
    %16 = maxon.binop %12, %15 {op = gt}
    %17 = maxon.binop %14, %16 {op = or}
    maxon.cond_br %17 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-expression-basic.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %19 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %19
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 2 : i64}
    memref.store %2, __match_eval_0
    cf.br eval_0.cmp0
  eval_0.cmp0:
    %4 = memref.load __match_eval_0 : i64
    %5 = arith.constant {value = 1 : i64}
    %6 = arith.cmpi eq %4, %5
    cf.cond_br %6 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %7 = arith.constant {value = 10 : i64}
    memref.store %7, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.cmp1:
    %8 = memref.load __match_eval_0 : i64
    %9 = arith.constant {value = 2 : i64}
    %10 = arith.cmpi eq %8, %9
    cf.cond_br %10 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %11 = arith.constant {value = 20 : i64}
    memref.store %11, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.case2:
    %12 = arith.constant {value = 0 : i64}
    memref.store %12, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.merge:
    %13 = memref.load __matchexpr_eval_0 : i64
    memref.store %13, __range_val_1
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %19 = memref.lea_symdata __panic_msg_18
    %20 = std.ptr_to_i64 %19
    std.call_runtime @maxon_panic %20
  __range_ok_1:
    %21 = memref.load __range_val_1 : i64
    %22 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %22
    func.return %21
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.eval_0.cmp0
  eval_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.eval_0.cmp1
  eval_0.case0:
    x86.mov eax, 10
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.eval_0.case2
  eval_0.case1:
    x86.mov eax, 20
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.case2:
    x86.xor eax, eax
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.merge:
    x86.mov eax, [rbp-24]
    x86.mov [rbp-32], eax
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
    x86.je register-allocator.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_18]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-40], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-40]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: match-expression-or-patterns -->
```maxon
function main() returns ExitCode
  var x = 4
  let result = match x 'eval'
    1 or 2 gives 10
    3 or 4 gives 20
    default gives 0
  end 'eval'
  return result
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 4 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = __matchexpr_eval_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %1 {var = __match_eval_0} {kind = i64} {decl = 1 : i1}
    maxon.br eval_0.cmp0
  eval_0.cmp0:
    %3 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.binop %3, %4 {op = eq}
    %6 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = eq}
    %9 = maxon.binop %5, %8 {op = or}
    maxon.cond_br %9 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %10 = maxon.literal {value = 10 : i64}
    maxon.assign %10 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %11 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %12 = maxon.literal {value = 3 : i64}
    %13 = maxon.binop %11, %12 {op = eq}
    %14 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %15 = maxon.literal {value = 4 : i64}
    %16 = maxon.binop %14, %15 {op = eq}
    %17 = maxon.binop %13, %16 {op = or}
    maxon.cond_br %17 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %18 = maxon.literal {value = 20 : i64}
    maxon.assign %18 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.case2:
    %19 = maxon.literal {value = 0 : i64}
    maxon.assign %19 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.merge:
    %20 = maxon.var_ref {var = __matchexpr_eval_0} {type = i64}
    maxon.assign %20 {var = result} {kind = i64} {decl = 1 : i1}
    maxon.assign %20 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %21 = maxon.literal {value = 0 : i64}
    %22 = maxon.binop %20, %21 {op = lt}
    %23 = maxon.literal {value = 4294967295 : i64}
    %24 = maxon.binop %20, %23 {op = gt}
    %25 = maxon.binop %22, %24 {op = or}
    maxon.cond_br %25 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-expression-or-patterns.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %27 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %27
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 4 : i64}
    memref.store %2, __match_eval_0
    cf.br eval_0.cmp0
  eval_0.cmp0:
    %4 = memref.load __match_eval_0 : i64
    %5 = arith.constant {value = 1 : i64}
    %6 = arith.cmpi eq %4, %5
    %7 = memref.load __match_eval_0 : i64
    %8 = arith.constant {value = 2 : i64}
    %9 = arith.cmpi eq %7, %8
    %10 = arith.ori1 %6, %9
    cf.cond_br %10 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %11 = arith.constant {value = 10 : i64}
    memref.store %11, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.cmp1:
    %12 = memref.load __match_eval_0 : i64
    %13 = arith.constant {value = 3 : i64}
    %14 = arith.cmpi eq %12, %13
    %15 = memref.load __match_eval_0 : i64
    %16 = arith.constant {value = 4 : i64}
    %17 = arith.cmpi eq %15, %16
    %18 = arith.ori1 %14, %17
    cf.cond_br %18 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %19 = arith.constant {value = 20 : i64}
    memref.store %19, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.case2:
    %20 = arith.constant {value = 0 : i64}
    memref.store %20, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.merge:
    %21 = memref.load __matchexpr_eval_0 : i64
    memref.store %21, __range_val_1
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi lt %21, %22
    %24 = arith.constant {value = 4294967295 : i64}
    %25 = arith.cmpi gt %21, %24
    %26 = arith.ori1 %23, %25
    cf.cond_br %26 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %27 = memref.lea_symdata __panic_msg_26
    %28 = std.ptr_to_i64 %27
    std.call_runtime @maxon_panic %28
  __range_ok_1:
    %29 = memref.load __range_val_1 : i64
    %30 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %30
    func.return %29
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 4
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.eval_0.cmp0
  eval_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.sete edx
    x86.movzx edx, edxb
    x86.mov ebx, [rbp-16]
    x86.mov esi, 2
    x86.cmp ebx, esi
    x86.sete edi
    x86.movzx edi, edib
    x86.or edx, edi
    x86.test edx, edx
    x86.je register-allocator.main.eval_0.cmp1
  eval_0.case0:
    x86.mov eax, 10
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 3
    x86.cmp eax, ecx
    x86.sete edx
    x86.movzx edx, edxb
    x86.mov ebx, [rbp-16]
    x86.mov esi, 4
    x86.cmp ebx, esi
    x86.sete edi
    x86.movzx edi, edib
    x86.or edx, edi
    x86.test edx, edx
    x86.je register-allocator.main.eval_0.case2
  eval_0.case1:
    x86.mov eax, 20
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.case2:
    x86.xor eax, eax
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.merge:
    x86.mov eax, [rbp-24]
    x86.mov [rbp-32], eax
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
    x86.je register-allocator.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_26]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-40], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-40]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: match-expression-in-arithmetic -->
```maxon
function main() returns ExitCode
  var x = 2
  let doubled = match x 'eval'
    1 gives 10
    2 gives 20
    default gives 0
  end 'eval' * 2
  return doubled
end 'main'
```
```exitcode
40
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.main}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = __matchexpr_eval_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %1 {var = __match_eval_0} {kind = i64} {decl = 1 : i1}
    maxon.br eval_0.cmp0
  eval_0.cmp0:
    %3 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.binop %3, %4 {op = eq}
    maxon.cond_br %5 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %6 = maxon.literal {value = 10 : i64}
    maxon.assign %6 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %7 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %8 = maxon.literal {value = 2 : i64}
    %9 = maxon.binop %7, %8 {op = eq}
    maxon.cond_br %9 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %10 = maxon.literal {value = 20 : i64}
    maxon.assign %10 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.case2:
    %11 = maxon.literal {value = 0 : i64}
    maxon.assign %11 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.merge:
    %12 = maxon.var_ref {var = __matchexpr_eval_0} {type = i64}
    %13 = maxon.literal {value = 2 : i64}
    %14 = maxon.binop %12, %13 {op = mul}
    maxon.assign %14 {var = doubled} {kind = i64} {decl = 1 : i1}
    maxon.assign %14 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 0 : i64}
    %16 = maxon.binop %14, %15 {op = lt}
    %17 = maxon.literal {value = 4294967295 : i64}
    %18 = maxon.binop %14, %17 {op = gt}
    %19 = maxon.binop %16, %18 {op = or}
    maxon.cond_br %19 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-expression-in-arithmetic.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %21 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %21
  }
}
=== standard
module {
  func @register-allocator.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 2 : i64}
    memref.store %2, __match_eval_0
    cf.br eval_0.cmp0
  eval_0.cmp0:
    %4 = memref.load __match_eval_0 : i64
    %5 = arith.constant {value = 1 : i64}
    %6 = arith.cmpi eq %4, %5
    cf.cond_br %6 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %7 = arith.constant {value = 10 : i64}
    memref.store %7, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.cmp1:
    %8 = memref.load __match_eval_0 : i64
    %9 = arith.constant {value = 2 : i64}
    %10 = arith.cmpi eq %8, %9
    cf.cond_br %10 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %11 = arith.constant {value = 20 : i64}
    memref.store %11, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.case2:
    %12 = arith.constant {value = 0 : i64}
    memref.store %12, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.merge:
    %13 = memref.load __matchexpr_eval_0 : i64
    %14 = arith.constant {value = 2 : i64}
    %15 = arith.muli %13, %14
    memref.store %15, __range_val_1
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi lt %15, %16
    %18 = arith.constant {value = 4294967295 : i64}
    %19 = arith.cmpi gt %15, %18
    %20 = arith.ori1 %17, %19
    cf.cond_br %20 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %21 = memref.lea_symdata __panic_msg_20
    %22 = std.ptr_to_i64 %21
    std.call_runtime @maxon_panic %22
  __range_ok_1:
    %23 = memref.load __range_val_1 : i64
    %24 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %24
    func.return %23
  }
}
=== x86
module {
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.eval_0.cmp0
  eval_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.eval_0.cmp1
  eval_0.case0:
    x86.mov eax, 10
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.eval_0.case2
  eval_0.case1:
    x86.mov eax, 20
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.case2:
    x86.xor eax, eax
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.merge:
    x86.mov eax, [rbp-24]
    x86.mov ecx, 2
    x86.imul eax, ecx
    x86.mov [rbp-32], eax
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
    x86.je register-allocator.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_20]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-40], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-40]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: match-statement-with-function-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(n Integer) returns Integer
  return n * 2
end 'double'

function main() returns ExitCode
  var x = 2
  var result = 0
  match x 'process'
    1 then result = double(10)
    2 then result = double(20)
    default then result = 0
  end 'process'
  return result
end 'main'
```
```exitcode
40
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.double(n: i64) -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = register-allocator.double}
    %1 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %2 = maxon.literal {value = 2 : i64}
    %3 = maxon.binop %1, %2 {op = mul} {optimalType = i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %3
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_4 = maxon.scope_enter {tag = register-allocator.main}
    %5 = maxon.literal {value = 2 : i64}
    maxon.assign %5 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 0 : i64}
    maxon.assign %6 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %5 {var = __match_process_0} {kind = i64} {decl = 1 : i1}
    maxon.br process_0.cmp0
  process_0.cmp0:
    %7 = maxon.var_ref {var = __match_process_0} {type = i64}
    %8 = maxon.literal {value = 1 : i64}
    %9 = maxon.binop %7, %8 {op = eq}
    maxon.cond_br %9 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    __scope_10 = maxon.scope_enter {tag = match_case}
    %11 = maxon.literal {value = 10 : i64}
    %12 = maxon.call @register-allocator.double %11
    maxon.assign %12 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_10} {tag = block_exit}
    maxon.br process_0.merge
  process_0.cmp1:
    %13 = maxon.var_ref {var = __match_process_0} {type = i64}
    %14 = maxon.literal {value = 2 : i64}
    %15 = maxon.binop %13, %14 {op = eq}
    maxon.cond_br %15 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    __scope_16 = maxon.scope_enter {tag = match_case}
    %17 = maxon.literal {value = 20 : i64}
    %18 = maxon.call @register-allocator.double %17
    maxon.assign %18 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_16} {tag = block_exit}
    maxon.br process_0.merge
  process_0.case2:
    __scope_19 = maxon.scope_enter {tag = match_case}
    %20 = maxon.literal {value = 0 : i64}
    maxon.assign %20 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_19} {tag = block_exit}
    maxon.br process_0.merge
  process_0.merge:
    %21 = maxon.var_ref {var = result} {type = i64}
    maxon.assign %21 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-statement-with-function-call.test:17: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %28 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_4} {tag = return_cleanup}
    maxon.return %28
  }
}
=== standard
module {
  func @register-allocator.double(n: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param n : StdI64
    %3 = arith.constant {value = 2 : i64}
    %4 = arith.muli %2, %3
    %5 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %5
    func.return %4
  }
  func @register-allocator.main() -> u32 {
  entry:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_4
    %8 = arith.constant {value = 2 : i64}
    memref.store %8, __match_process_0
    cf.br process_0.cmp0
  process_0.cmp0:
    %10 = memref.load __match_process_0 : i64
    %11 = arith.constant {value = 1 : i64}
    %12 = arith.cmpi eq %10, %11
    cf.cond_br %12 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    %13 = arith.constant {value = 0 : i64}
    %14 = std.call_runtime @mm_scope_enter %13
    memref.store %14, __scope_10
    %15 = arith.constant {value = 10 : i64}
    %16 = func.call @register-allocator.double %15
    memref.store %16, result
    %17 = memref.load __scope_10 : i64
    std.call_runtime @mm_scope_exit %17
    cf.br process_0.merge
  process_0.cmp1:
    %18 = memref.load __match_process_0 : i64
    %19 = arith.constant {value = 2 : i64}
    %20 = arith.cmpi eq %18, %19
    cf.cond_br %20 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    %21 = arith.constant {value = 0 : i64}
    %22 = std.call_runtime @mm_scope_enter %21
    memref.store %22, __scope_16
    %23 = arith.constant {value = 20 : i64}
    %24 = func.call @register-allocator.double %23
    memref.store %24, result
    %25 = memref.load __scope_16 : i64
    std.call_runtime @mm_scope_exit %25
    cf.br process_0.merge
  process_0.case2:
    %26 = arith.constant {value = 0 : i64}
    %27 = std.call_runtime @mm_scope_enter %26
    memref.store %27, __scope_19
    %28 = arith.constant {value = 0 : i64}
    memref.store %28, result
    %29 = memref.load __scope_19 : i64
    std.call_runtime @mm_scope_exit %29
    cf.br process_0.merge
  process_0.merge:
    %30 = memref.load result : i64
    memref.store %30, __range_val_1
    %31 = arith.constant {value = 0 : i64}
    %32 = arith.cmpi lt %30, %31
    %33 = arith.constant {value = 4294967295 : i64}
    %34 = arith.cmpi gt %30, %33
    %35 = arith.ori1 %32, %34
    cf.cond_br %35 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %36 = memref.lea_symdata __panic_msg_27
    %37 = std.ptr_to_i64 %36
    std.call_runtime @maxon_panic %37
  __range_ok_1:
    %38 = memref.load __range_val_1 : i64
    %39 = memref.load __scope_4 : i64
    std.call_runtime @mm_scope_exit %39
    func.return %38
  }
}
=== x86
module {
  func @register-allocator.double(n: i64) -> i64 {
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
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.process_0.cmp0
  process_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.process_0.cmp1
  process_0.case0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-24], eax
    x86.mov ecx, 10
    x86.call register-allocator.double
    x86.mov [rbp-32], eax
    x86.mov edx, [rbp-24]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.process_0.merge
  process_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.process_0.case2
  process_0.case1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.mov ecx, 20
    x86.call register-allocator.double
    x86.mov [rbp-32], eax
    x86.mov edx, [rbp-40]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.process_0.merge
  process_0.case2:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-48], eax
    x86.xor ecx, ecx
    x86.mov [rbp-32], ecx
    x86.mov edx, [rbp-48]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp register-allocator.main.process_0.merge
  process_0.merge:
    x86.mov eax, [rbp-32]
    x86.mov [rbp-56], eax
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
    x86.je register-allocator.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_27]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-64], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-64]
    x86.epilogue
    x86.ret
  }
}
```

### Level 8: Error Handling

<!-- test: error-otherwise-ignore -->
```maxon

typealias Integer = int(i64.min to i64.max)

union MyError implements Error
  failed
end 'MyError'

function mayFail() returns Integer throws MyError
  throw MyError.failed
end 'mayFail'

function main() returns ExitCode
  try mayFail() otherwise ignore
  return 42
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    __scope_8 = maxon.scope_enter {tag = register-allocator.mayFail}
    %9 = maxon.enum_literal @MyError.failed
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.throw @MyError %9
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_10 = maxon.scope_enter {tag = register-allocator.main}
    %13, %12 = maxon.try_call @register-allocator.mayFail
    %14 = maxon.literal {value = 42 : i64}
    maxon.scope_exit {scope = __scope_10} {tag = return_cleanup}
    maxon.return %14
  }
}
=== standard
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_8
    %2 = arith.constant {value = 0 : i64}
    %3 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %3
    %4 = arith.constant {value = 1 : i64}
    %5 = arith.addi %2, %4
    func.error_return %5
  }
  func @register-allocator.main() -> u32 {
  entry:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_10
    %8, %9 = func.try_call @register-allocator.mayFail
    %10 = arith.constant {value = 42 : i64}
    %11 = memref.load __scope_10 : i64
    std.call_runtime @mm_scope_exit %11
    func.return %10
  }
}
=== x86
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov ebx, 1
    x86.xor esi, esi
    x86.lea edx, [esi + ebx]
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call register-allocator.mayFail
    x86.mov ecx, 42
    x86.mov ebx, [rbp-8]
    x86.mov [rbp-16], eax
    x86.mov [rbp-24], edx
    x86.mov rcx, rbx
    x86.call mm_scope_exit
    x86.mov eax, 42
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: error-otherwise-block -->
```maxon

typealias Integer = int(i64.min to i64.max)

union MyError implements Error
  failed
end 'MyError'

function mayFail() returns Integer throws MyError
  throw MyError.failed
end 'mayFail'

function main() returns ExitCode
  var result = 0
  try mayFail() otherwise 'err'
    result = 42
  end 'err'
  return result
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    __scope_8 = maxon.scope_enter {tag = register-allocator.mayFail}
    %9 = maxon.enum_literal @MyError.failed
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.throw @MyError %9
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_10 = maxon.scope_enter {tag = register-allocator.main}
    %11 = maxon.literal {value = 0 : i64}
    maxon.assign %11 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %14, %13 = maxon.try_call @register-allocator.mayFail
    maxon.assign %13 {var = __try_error_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %14 {var = __try_result_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 0 : i64}
    %16 = maxon.binop %13, %15 {op = ne}
    maxon.cond_br %16 [then: otherwise_error_0, else: otherwise_continue_1]
  otherwise_error_0:
    %17 = maxon.literal {value = 42 : i64}
    maxon.assign %17 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_continue_1
  otherwise_continue_1:
    %18 = maxon.var_ref {var = __try_result_3} {type = i64}
    %19 = maxon.var_ref {var = result} {type = i64}
    maxon.assign %19 {var = __range_val_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %19, %20 {op = lt}
    %22 = maxon.literal {value = 4294967295 : i64}
    %23 = maxon.binop %19, %22 {op = gt}
    %24 = maxon.binop %21, %23 {op = or}
    maxon.cond_br %24 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    maxon.panic "panic at error-otherwise-block.test:18: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_4:
    %26 = maxon.var_ref {var = __range_val_4} {type = i64}
    maxon.scope_exit {scope = __scope_10} {tag = return_cleanup}
    maxon.return %26
  }
}
=== standard
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_8
    %2 = arith.constant {value = 0 : i64}
    %3 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %3
    %4 = arith.constant {value = 1 : i64}
    %5 = arith.addi %2, %4
    func.error_return %5
  }
  func @register-allocator.main() -> u32 {
  entry:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_10
    %8 = arith.constant {value = 0 : i64}
    memref.store %8, result
    %9, %10 = func.try_call @register-allocator.mayFail
    memref.store %9, __try_result_3
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.cmpi ne %10, %11
    cf.cond_br %12 [then: otherwise_error_0, else: otherwise_continue_1]
  otherwise_error_0:
    %13 = arith.constant {value = 42 : i64}
    memref.store %13, result
    cf.br otherwise_continue_1
  otherwise_continue_1:
    %15 = memref.load result : i64
    memref.store %15, __range_val_4
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi lt %15, %16
    %18 = arith.constant {value = 4294967295 : i64}
    %19 = arith.cmpi gt %15, %18
    %20 = arith.ori1 %17, %19
    cf.cond_br %20 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    %21 = memref.lea_symdata __panic_msg_25
    %22 = std.ptr_to_i64 %21
    std.call_runtime @maxon_panic %22
  __range_ok_4:
    %23 = memref.load __range_val_4 : i64
    %24 = memref.load __scope_10 : i64
    std.call_runtime @mm_scope_exit %24
    func.return %23
  }
}
=== x86
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov ebx, 1
    x86.xor esi, esi
    x86.lea edx, [esi + ebx]
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.call register-allocator.mayFail
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_continue_1
  otherwise_error_0:
    x86.mov eax, 42
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.otherwise_continue_1
  otherwise_continue_1:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-24], eax
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
    x86.je register-allocator.main.__range_ok_4
  __range_panic_4:
    x86.lea_symdata rax, [__panic_msg_25]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_4:
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-32], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-32]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: error-propagate-through-caller -->
```maxon

typealias Integer = int(i64.min to i64.max)

union MyError implements Error
  failed
end 'MyError'

function inner() returns Integer throws MyError
  throw MyError.failed
end 'inner'

function middle() returns Integer throws MyError
  let x = try inner()
  return x
end 'middle'

function main() returns ExitCode
  let x = try middle() otherwise 99
  return x
end 'main'
```
```exitcode
99
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.inner() -> i64 {
  entry:
    __scope_8 = maxon.scope_enter {tag = register-allocator.inner}
    %9 = maxon.enum_literal @MyError.failed
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.throw @MyError %9
  }
  func @register-allocator.middle() -> i64 {
  entry:
    __scope_10 = maxon.scope_enter {tag = register-allocator.middle}
    %13, %12 = maxon.try_call @register-allocator.inner
    maxon.assign %12 {var = __try_error_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %13 {var = __try_result_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %12, %14 {op = ne}
    maxon.cond_br %15 [then: propagate_error_0, else: try_continue_1]
  propagate_error_0:
    %16 = maxon.var_ref {var = __try_error_2} {type = i64}
    maxon.return %16
  try_continue_1:
    %17 = maxon.var_ref {var = __try_result_3} {type = i64}
    maxon.assign %17 {var = x} {kind = i64} {decl = 1 : i1}
    maxon.scope_exit {scope = __scope_10} {tag = return_cleanup}
    maxon.return %17
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_18 = maxon.scope_enter {tag = register-allocator.main}
    %21, %20 = maxon.try_call @register-allocator.middle
    %22 = maxon.literal {value = 99 : i64}
    maxon.assign %22 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %21 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %23 = maxon.literal {value = 0 : i64}
    %24 = maxon.binop %20, %23 {op = ne}
    maxon.cond_br %24 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %25 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %25 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %26 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %26 {var = x} {kind = i64} {decl = 1 : i1}
    maxon.assign %26 {var = __range_val_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %27 = maxon.literal {value = 0 : i64}
    %28 = maxon.binop %26, %27 {op = lt}
    %29 = maxon.literal {value = 4294967295 : i64}
    %30 = maxon.binop %26, %29 {op = gt}
    %31 = maxon.binop %28, %30 {op = or}
    maxon.cond_br %31 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    maxon.panic "panic at error-propagate-through-caller.test:20: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_4:
    %33 = maxon.var_ref {var = __range_val_4} {type = i64}
    maxon.scope_exit {scope = __scope_18} {tag = return_cleanup}
    maxon.return %33
  }
}
=== standard
module {
  func @register-allocator.inner() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_8
    %2 = arith.constant {value = 0 : i64}
    %3 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %3
    %4 = arith.constant {value = 1 : i64}
    %5 = arith.addi %2, %4
    func.error_return %5
  }
  func @register-allocator.middle() -> i64 {
  entry:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_10
    %8, %9 = func.try_call @register-allocator.inner
    memref.store %9, __try_error_2
    memref.store %8, __try_result_3
    %10 = arith.constant {value = 0 : i64}
    %11 = arith.cmpi ne %9, %10
    cf.cond_br %11 [then: propagate_error_0, else: try_continue_1]
  propagate_error_0:
    %12 = memref.load __try_error_2 : i64
    func.error_return %12
  try_continue_1:
    %13 = memref.load __try_result_3 : i64
    %14 = memref.load __scope_10 : i64
    std.call_runtime @mm_scope_exit %14
    func.return %13
  }
  func @register-allocator.main() -> u32 {
  entry:
    %15 = arith.constant {value = 0 : i64}
    %16 = std.call_runtime @mm_scope_enter %15
    memref.store %16, __scope_18
    %17, %18 = func.try_call @register-allocator.middle
    %19 = arith.constant {value = 99 : i64}
    memref.store %19, __try_default_1
    memref.store %17, __try_result_0
    %20 = arith.constant {value = 0 : i64}
    %21 = arith.cmpi ne %18, %20
    cf.cond_br %21 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %22 = memref.load __try_default_1 : i64
    memref.store %22, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %23 = memref.load __try_result_0 : i64
    memref.store %23, __range_val_4
    %24 = arith.constant {value = 0 : i64}
    %25 = arith.cmpi lt %23, %24
    %26 = arith.constant {value = 4294967295 : i64}
    %27 = arith.cmpi gt %23, %26
    %28 = arith.ori1 %25, %27
    cf.cond_br %28 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    %29 = memref.lea_symdata __panic_msg_32
    %30 = std.ptr_to_i64 %29
    std.call_runtime @maxon_panic %30
  __range_ok_4:
    %31 = memref.load __range_val_4 : i64
    %32 = memref.load __scope_18 : i64
    std.call_runtime @mm_scope_exit %32
    func.return %31
  }
}
=== x86
module {
  func @register-allocator.inner() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov ebx, 1
    x86.xor esi, esi
    x86.lea edx, [esi + ebx]
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
  func @register-allocator.middle() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call register-allocator.inner
    x86.mov [rbp-16], edx
    x86.mov [rbp-24], eax
    x86.xor ecx, ecx
    x86.cmp edx, ecx
    x86.je register-allocator.middle.try_continue_1
  propagate_error_0:
    x86.mov edx, [rbp-16]
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  try_continue_1:
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-32], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-32]
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call register-allocator.middle
    x86.mov ecx, 99
    x86.mov [rbp-16], ecx
    x86.mov [rbp-24], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_3
  otherwise_default_error_2:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-24]
    x86.mov [rbp-32], eax
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
    x86.je register-allocator.main.__range_ok_4
  __range_panic_4:
    x86.lea_symdata rax, [__panic_msg_32]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_4:
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-40], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-40]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: error-multiple-try-calls -->
```maxon

typealias Integer = int(i64.min to i64.max)

union MyError implements Error
  failed
end 'MyError'

function getA() returns Integer throws MyError
  return 10
end 'getA'

function getB() returns Integer throws MyError
  return 20
end 'getB'

function getC() returns Integer throws MyError
  throw MyError.failed
end 'getC'

function main() returns ExitCode
  let a = try getA() otherwise 0
  let b = try getB() otherwise 0
  let c = try getC() otherwise 12
  return a + b + c
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.getA() -> i64 {
  entry:
    __scope_8 = maxon.scope_enter {tag = register-allocator.getA}
    %9 = maxon.literal {value = 10 : i64}
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.return %9
  }
  func @register-allocator.getB() -> i64 {
  entry:
    __scope_10 = maxon.scope_enter {tag = register-allocator.getB}
    %11 = maxon.literal {value = 20 : i64}
    maxon.scope_exit {scope = __scope_10} {tag = return_cleanup}
    maxon.return %11
  }
  func @register-allocator.getC() -> i64 {
  entry:
    __scope_12 = maxon.scope_enter {tag = register-allocator.getC}
    %13 = maxon.enum_literal @MyError.failed
    maxon.scope_exit {scope = __scope_12} {tag = return_cleanup}
    maxon.throw @MyError %13
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_14 = maxon.scope_enter {tag = register-allocator.main}
    %17, %16 = maxon.try_call @register-allocator.getA
    %18 = maxon.literal {value = 0 : i64}
    maxon.assign %18 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %17 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %19 = maxon.literal {value = 0 : i64}
    %20 = maxon.binop %16, %19 {op = ne}
    maxon.cond_br %20 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %21 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %21 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %22 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %22 {var = a} {kind = i64} {decl = 1 : i1}
    %25, %24 = maxon.try_call @register-allocator.getB
    %26 = maxon.literal {value = 0 : i64}
    maxon.assign %26 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %25 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %27 = maxon.literal {value = 0 : i64}
    %28 = maxon.binop %24, %27 {op = ne}
    maxon.cond_br %28 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %29 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %29 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %30 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %30 {var = b} {kind = i64} {decl = 1 : i1}
    %33, %32 = maxon.try_call @register-allocator.getC
    %34 = maxon.literal {value = 12 : i64}
    maxon.assign %34 {var = __try_default_9} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %33 {var = __try_result_8} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %35 = maxon.literal {value = 0 : i64}
    %36 = maxon.binop %32, %35 {op = ne}
    maxon.cond_br %36 [then: otherwise_default_error_10, else: otherwise_default_continue_11]
  otherwise_default_error_10:
    %37 = maxon.var_ref {var = __try_default_9} {type = i64}
    maxon.assign %37 {var = __try_result_8} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_11
  otherwise_default_continue_11:
    %38 = maxon.var_ref {var = __try_result_8} {type = i64}
    maxon.assign %38 {var = c} {kind = i64} {decl = 1 : i1}
    %39 = maxon.var_ref {var = a} {type = i64}
    %40 = maxon.var_ref {var = b} {type = i64}
    %41 = maxon.binop %39, %40 {op = add}
    %42 = maxon.binop %41, %38 {op = add}
    maxon.assign %42 {var = __range_val_12} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %43 = maxon.literal {value = 0 : i64}
    %44 = maxon.binop %42, %43 {op = lt}
    %45 = maxon.literal {value = 4294967295 : i64}
    %46 = maxon.binop %42, %45 {op = gt}
    %47 = maxon.binop %44, %46 {op = or}
    maxon.cond_br %47 [then: __range_panic_12, else: __range_ok_12]
  __range_panic_12:
    maxon.panic "panic at error-multiple-try-calls.test:25: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_12:
    %49 = maxon.var_ref {var = __range_val_12} {type = i64}
    maxon.scope_exit {scope = __scope_14} {tag = return_cleanup}
    maxon.return %49
  }
}
=== standard
module {
  func @register-allocator.getA() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_8
    %2 = arith.constant {value = 10 : i64}
    %3 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %3
    func.return %2
  }
  func @register-allocator.getB() -> i64 {
  entry:
    %4 = arith.constant {value = 0 : i64}
    %5 = std.call_runtime @mm_scope_enter %4
    memref.store %5, __scope_10
    %6 = arith.constant {value = 20 : i64}
    %7 = memref.load __scope_10 : i64
    std.call_runtime @mm_scope_exit %7
    func.return %6
  }
  func @register-allocator.getC() -> i64 {
  entry:
    %8 = arith.constant {value = 0 : i64}
    %9 = std.call_runtime @mm_scope_enter %8
    memref.store %9, __scope_12
    %10 = arith.constant {value = 0 : i64}
    %11 = memref.load __scope_12 : i64
    std.call_runtime @mm_scope_exit %11
    %12 = arith.constant {value = 1 : i64}
    %13 = arith.addi %10, %12
    func.error_return %13
  }
  func @register-allocator.main() -> u32 {
  entry:
    %14 = arith.constant {value = 0 : i64}
    %15 = std.call_runtime @mm_scope_enter %14
    memref.store %15, __scope_14
    %16, %17 = func.try_call @register-allocator.getA
    %18 = arith.constant {value = 0 : i64}
    memref.store %18, __try_default_1
    memref.store %16, __try_result_0
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi ne %17, %19
    cf.cond_br %20 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %21 = memref.load __try_default_1 : i64
    memref.store %21, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %22 = memref.load __try_result_0 : i64
    memref.store %22, a
    %23, %24 = func.try_call @register-allocator.getB
    %25 = arith.constant {value = 0 : i64}
    memref.store %25, __try_default_5
    memref.store %23, __try_result_4
    %26 = arith.constant {value = 0 : i64}
    %27 = arith.cmpi ne %24, %26
    cf.cond_br %27 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %28 = memref.load __try_default_5 : i64
    memref.store %28, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %29 = memref.load __try_result_4 : i64
    memref.store %29, b
    %30, %31 = func.try_call @register-allocator.getC
    %32 = arith.constant {value = 12 : i64}
    memref.store %32, __try_default_9
    memref.store %30, __try_result_8
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.cmpi ne %31, %33
    cf.cond_br %34 [then: otherwise_default_error_10, else: otherwise_default_continue_11]
  otherwise_default_error_10:
    %35 = memref.load __try_default_9 : i64
    memref.store %35, __try_result_8
    cf.br otherwise_default_continue_11
  otherwise_default_continue_11:
    %36 = memref.load __try_result_8 : i64
    %37 = memref.load a : i64
    %38 = memref.load b : i64
    %39 = arith.addi %37, %38
    %40 = arith.addi %39, %36
    memref.store %40, __range_val_12
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.cmpi lt %40, %41
    %43 = arith.constant {value = 4294967295 : i64}
    %44 = arith.cmpi gt %40, %43
    %45 = arith.ori1 %42, %44
    cf.cond_br %45 [then: __range_panic_12, else: __range_ok_12]
  __range_panic_12:
    %46 = memref.lea_symdata __panic_msg_48
    %47 = std.ptr_to_i64 %46
    std.call_runtime @maxon_panic %47
  __range_ok_12:
    %48 = memref.load __range_val_12 : i64
    %49 = memref.load __scope_14 : i64
    std.call_runtime @mm_scope_exit %49
    func.return %48
  }
}
=== x86
module {
  func @register-allocator.getA() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov eax, 10
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, 10
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  }
  func @register-allocator.getB() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov eax, 20
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, 20
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  }
  func @register-allocator.getC() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov ebx, 1
    x86.xor esi, esi
    x86.lea edx, [esi + ebx]
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=96
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call register-allocator.getA
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.mov [rbp-24], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_3
  otherwise_default_error_2:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-24]
    x86.mov [rbp-32], eax
    x86.call register-allocator.getB
    x86.xor ecx, ecx
    x86.mov [rbp-40], ecx
    x86.mov [rbp-48], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-40]
    x86.mov [rbp-48], eax
    x86.jmp register-allocator.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, [rbp-48]
    x86.mov [rbp-56], eax
    x86.call register-allocator.getC
    x86.mov ecx, 12
    x86.mov [rbp-64], ecx
    x86.mov [rbp-72], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_11
  otherwise_default_error_10:
    x86.mov eax, [rbp-64]
    x86.mov [rbp-72], eax
    x86.jmp register-allocator.main.otherwise_default_continue_11
  otherwise_default_continue_11:
    x86.mov eax, [rbp-72]
    x86.mov ecx, [rbp-32]
    x86.mov edx, [rbp-56]
    x86.add ecx, edx
    x86.add ecx, eax
    x86.mov [rbp-80], ecx
    x86.xor ebx, ebx
    x86.cmp ecx, ebx
    x86.setl esi
    x86.movzx esi, esib
    x86.mov rdi, 4294967295
    x86.cmp rcx, rdi
    x86.setg r8
    x86.movzx r8, r8b
    x86.or esi, r8
    x86.test esi, esi
    x86.je register-allocator.main.__range_ok_12
  __range_panic_12:
    x86.lea_symdata rax, [__panic_msg_48]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_12:
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

<!-- test: error-throw-in-match -->
```maxon

typealias Integer = int(i64.min to i64.max)

union MyError implements Error
  invalidInput
  notFound
end 'MyError'

function lookup(key Integer) returns Integer throws MyError
  match key 'dispatch'
    1 then return 100
    2 then return 200
    default then throw MyError.notFound
  end 'dispatch'
end 'lookup'

function main() returns ExitCode
  let a = try lookup(2) otherwise 0
  let b = try lookup(99) otherwise 42
  return a + b mod 256
end 'main'
```
```exitcode
242
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.lookup(key: i64) -> i64 {
  entry:
    __scope_8 = maxon.scope_enter {tag = register-allocator.lookup}
    %9 = maxon.param {index = 0 : i32} {name = key} {type = i64}
    maxon.assign %9 {var = __match_dispatch_0} {kind = i64} {decl = 1 : i1}
    maxon.br dispatch_0.cmp0
  dispatch_0.cmp0:
    %10 = maxon.var_ref {var = __match_dispatch_0} {type = i64}
    %11 = maxon.literal {value = 1 : i64}
    %12 = maxon.binop %10, %11 {op = eq}
    maxon.cond_br %12 [then: dispatch_0.case0, else: dispatch_0.cmp1]
  dispatch_0.case0:
    __scope_13 = maxon.scope_enter {tag = match_case}
    %14 = maxon.literal {value = 100 : i64}
    maxon.scope_exit {scope = __scope_13} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.return %14
  dispatch_0.cmp1:
    %15 = maxon.var_ref {var = __match_dispatch_0} {type = i64}
    %16 = maxon.literal {value = 2 : i64}
    %17 = maxon.binop %15, %16 {op = eq}
    maxon.cond_br %17 [then: dispatch_0.case1, else: dispatch_0.case2]
  dispatch_0.case1:
    __scope_18 = maxon.scope_enter {tag = match_case}
    %19 = maxon.literal {value = 200 : i64}
    maxon.scope_exit {scope = __scope_18} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.return %19
  dispatch_0.case2:
    __scope_20 = maxon.scope_enter {tag = match_case}
    %21 = maxon.enum_literal @MyError.notFound
    maxon.scope_exit {scope = __scope_20} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.throw @MyError %21
  dispatch_0.merge:
  }
  func @register-allocator.main() -> i64 {
  entry:
    __scope_22 = maxon.scope_enter {tag = register-allocator.main}
    %23 = maxon.literal {value = 2 : i64}
    %26, %25 = maxon.try_call @register-allocator.lookup %23
    %27 = maxon.literal {value = 0 : i64}
    maxon.assign %27 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %26 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %28 = maxon.literal {value = 0 : i64}
    %29 = maxon.binop %25, %28 {op = ne}
    maxon.cond_br %29 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %30 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %30 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %31 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %31 {var = a} {kind = i64} {decl = 1 : i1}
    %32 = maxon.literal {value = 99 : i64}
    %35, %34 = maxon.try_call @register-allocator.lookup %32
    %36 = maxon.literal {value = 42 : i64}
    maxon.assign %36 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %35 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %37 = maxon.literal {value = 0 : i64}
    %38 = maxon.binop %34, %37 {op = ne}
    maxon.cond_br %38 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %39 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %39 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %40 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %40 {var = b} {kind = i64} {decl = 1 : i1}
    %41 = maxon.literal {value = 256 : i64}
    %42 = maxon.binop %40, %41 {op = mod}
    %43 = maxon.var_ref {var = a} {type = i64}
    %44 = maxon.binop %43, %42 {op = add}
    maxon.assign %44 {var = __range_val_8} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %45 = maxon.literal {value = 0 : i64}
    %46 = maxon.binop %44, %45 {op = lt}
    %47 = maxon.literal {value = 4294967295 : i64}
    %48 = maxon.binop %44, %47 {op = gt}
    %49 = maxon.binop %46, %48 {op = or}
    maxon.cond_br %49 [then: __range_panic_8, else: __range_ok_8]
  __range_panic_8:
    maxon.panic "panic at error-throw-in-match.test:21: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_8:
    %51 = maxon.var_ref {var = __range_val_8} {type = i64}
    maxon.scope_exit {scope = __scope_22} {tag = return_cleanup}
    maxon.return %51
  }
}
=== standard
module {
  func @register-allocator.lookup(key: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_8
    %2 = func.param key : StdI64
    memref.store %2, __match_dispatch_0
    cf.br dispatch_0.cmp0
  dispatch_0.cmp0:
    %3 = memref.load __match_dispatch_0 : i64
    %4 = arith.constant {value = 1 : i64}
    %5 = arith.cmpi eq %3, %4
    cf.cond_br %5 [then: dispatch_0.case0, else: dispatch_0.cmp1]
  dispatch_0.case0:
    %6 = arith.constant {value = 0 : i64}
    %7 = std.call_runtime @mm_scope_enter %6
    memref.store %7, __scope_13
    %8 = arith.constant {value = 100 : i64}
    %9 = memref.load __scope_13 : i64
    std.call_runtime @mm_scope_exit %9
    %10 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %10
    func.return %8
  dispatch_0.cmp1:
    %11 = memref.load __match_dispatch_0 : i64
    %12 = arith.constant {value = 2 : i64}
    %13 = arith.cmpi eq %11, %12
    cf.cond_br %13 [then: dispatch_0.case1, else: dispatch_0.case2]
  dispatch_0.case1:
    %14 = arith.constant {value = 0 : i64}
    %15 = std.call_runtime @mm_scope_enter %14
    memref.store %15, __scope_18
    %16 = arith.constant {value = 200 : i64}
    %17 = memref.load __scope_18 : i64
    std.call_runtime @mm_scope_exit %17
    %18 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %18
    func.return %16
  dispatch_0.case2:
    %19 = arith.constant {value = 0 : i64}
    %20 = std.call_runtime @mm_scope_enter %19
    memref.store %20, __scope_20
    %21 = arith.constant {value = 1 : i64}
    %22 = memref.load __scope_20 : i64
    std.call_runtime @mm_scope_exit %22
    %23 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %23
    %24 = arith.constant {value = 1 : i64}
    %25 = arith.addi %21, %24
    func.error_return %25
  dispatch_0.merge:
  }
  func @register-allocator.main() -> u32 {
  entry:
    %26 = arith.constant {value = 0 : i64}
    %27 = std.call_runtime @mm_scope_enter %26
    memref.store %27, __scope_22
    %28 = arith.constant {value = 2 : i64}
    %29, %30 = func.try_call @register-allocator.lookup %28
    %31 = arith.constant {value = 0 : i64}
    memref.store %31, __try_default_1
    memref.store %29, __try_result_0
    %32 = arith.constant {value = 0 : i64}
    %33 = arith.cmpi ne %30, %32
    cf.cond_br %33 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %34 = memref.load __try_default_1 : i64
    memref.store %34, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %35 = memref.load __try_result_0 : i64
    memref.store %35, a
    %36 = arith.constant {value = 99 : i64}
    %37, %38 = func.try_call @register-allocator.lookup %36
    %39 = arith.constant {value = 42 : i64}
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
    %44 = arith.constant {value = 256 : i64}
    %45 = arith.remsi %43, %44
    %46 = memref.load a : i64
    %47 = arith.addi %46, %45
    memref.store %47, __range_val_8
    %48 = arith.constant {value = 0 : i64}
    %49 = arith.cmpi lt %47, %48
    %50 = arith.constant {value = 4294967295 : i64}
    %51 = arith.cmpi gt %47, %50
    %52 = arith.ori1 %49, %51
    cf.cond_br %52 [then: __range_panic_8, else: __range_ok_8]
  __range_panic_8:
    %53 = memref.lea_symdata __panic_msg_50
    %54 = std.ptr_to_i64 %53
    std.call_runtime @maxon_panic %54
  __range_ok_8:
    %55 = memref.load __range_val_8 : i64
    %56 = memref.load __scope_22 : i64
    std.call_runtime @mm_scope_exit %56
    func.return %55
  }
}
=== x86
module {
  func @register-allocator.lookup(key: i64) -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov [rbp-48], ecx
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, [rbp-48]
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.lookup.dispatch_0.cmp0
  dispatch_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.lookup.dispatch_0.cmp1
  dispatch_0.case0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-24], eax
    x86.mov eax, 100
    x86.mov ecx, [rbp-24]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov eax, 100
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  dispatch_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.lookup.dispatch_0.case2
  dispatch_0.case1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-32], eax
    x86.mov eax, 200
    x86.mov ecx, [rbp-32]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov eax, 200
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  dispatch_0.case2:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-40], eax
    x86.mov ecx, 1
    x86.mov edx, [rbp-40]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov rcx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov esi, 1
    x86.mov edi, 1
    x86.lea edx, [edi + esi]
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  dispatch_0.merge:
  }
  func @register-allocator.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.call register-allocator.lookup
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.mov [rbp-24], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_3
  otherwise_default_error_2:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-24]
    x86.mov [rbp-32], eax
    x86.mov ecx, 99
    x86.call register-allocator.lookup
    x86.mov ecx, 42
    x86.mov [rbp-40], ecx
    x86.mov [rbp-48], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-40]
    x86.mov [rbp-48], eax
    x86.jmp register-allocator.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, [rbp-48]
    x86.mov ecx, 256
    x86.mov [rbp-64], eax
    x86.cqo
    x86.idiv ecx
    x86.mov eax, [rbp-32]
    x86.add eax, edx
    x86.mov [rbp-56], eax
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
    x86.je register-allocator.main.__range_ok_8
  __range_panic_8:
    x86.lea_symdata rax, [__panic_msg_50]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_8:
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-64], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-64]
    x86.epilogue
    x86.ret
  }
}
```
