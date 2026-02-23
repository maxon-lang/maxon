---
feature: basics
status: stable
keywords: [main, return, semantic, validation]
category: basics
---

## Documentation

The compiler performs semantic checks before lowering the MLIR pipeline. These checks validate program structure requirements.

### E3001: No main function

Every program must have a `main` function. If none is found, the compiler reports:

```text
error E3001: No 'main' function found
```

### E3002: Main wrong return type

The `main` function must return `ExitCode`. If it has no return type or returns a different type, the compiler reports:

```text
error E3002: Function 'main' must return ExitCode
```

## Tests

<!-- test: no-main -->
```maxon

typealias Integer = int(i64.min to i64.max)

function notmain() returns Integer
  return 42
end 'notmain'
```
```maxoncstderr
error E3001: No 'main' function found
```

<!-- test: main-no-return-type -->
```maxon
function main()
  return
end 'main'
```
```maxoncstderr
error E3002: Function 'main' must return ExitCode
```

<!-- test: valid-main -->
```maxon
function main() returns ExitCode
  return 42
end 'main'
```
```exitcode
42
```

<!-- test: return-function-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getValue() returns Integer
  return 42
end 'getValue'

function main() returns ExitCode
  return getValue()
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @basics.getValue() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = basics.getValue}
    %1 = maxon.literal {value = 42 : i64}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %1
  }
  func @basics.main() -> i64 {
  entry:
    __scope_2 = maxon.scope_enter {tag = basics.main}
    %3 = maxon.call @basics.getValue
    maxon.assign %3 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    %6 = maxon.literal {value = 4294967295 : i64}
    %7 = maxon.binop %3, %6 {op = gt}
    %8 = maxon.binop %5, %7 {op = or}
    maxon.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at return-function-call.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %10 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_2} {tag = return_cleanup}
    maxon.return %10
  }
}
=== standard
module {
  func @basics.getValue() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.constant {value = 42 : i64}
    %3 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %3
    func.return %2
  }
  func @basics.main() -> u32 {
  entry:
    %4 = arith.constant {value = 0 : i64}
    %5 = std.call_runtime @mm_scope_enter %4
    memref.store %5, __scope_2
    %6 = func.call @basics.getValue
    memref.store %6, __range_val_0
    %7 = arith.constant {value = 0 : i64}
    %8 = arith.cmpi lt %6, %7
    %9 = arith.constant {value = 4294967295 : i64}
    %10 = arith.cmpi gt %6, %9
    %11 = arith.ori1 %8, %10
    cf.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %12 = memref.lea_symdata __panic_msg_9
    %13 = std.ptr_to_i64 %12
    std.call_runtime @maxon_panic %13
  __range_ok_0:
    %14 = memref.load __range_val_0 : i64
    %15 = memref.load __scope_2 : i64
    std.call_runtime @mm_scope_exit %15
    func.return %14
  }
}
=== x86
module {
  func @basics.getValue() -> i64 {
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
  func @basics.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call basics.getValue
    x86.mov [rbp-16], eax
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
    x86.je basics.main.__range_ok_0
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

<!-- test: float-var-if-else -->
```maxon
function main() returns ExitCode
  var x = 3.14
  if x == 3.14 'check'
    return 1
  end 'check' else 'other'
    return 0
  end 'other'
end 'main'
```
```exitcode
1
```
```RequiredRdata
f64 3.14
```
```RequiredMLIR
=== maxon
module {
  func @basics.main() -> i64 {
  entry:
    __scope_0 = maxon.scope_enter {tag = basics.main}
    %1 = maxon.literal {value = 3.14 : f64}
    maxon.assign %1 {var = x} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3.14 : f64}
    %3 = maxon.binop %1, %2 {op = eq} {kind = f64}
    maxon.cond_br %3 [then: check_0, else: other_1]
  check_0:
    __scope_4 = maxon.scope_enter {tag = if_then}
    %5 = maxon.literal {value = 1 : i64}
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
  func @basics.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = arith.float_constant {value = 3.14 : f64}
    %3 = arith.float_constant {value = 3.14 : f64}
    %4 = arith.cmpf eq %2, %3
    cf.cond_br %4 [then: check_0, else: other_1]
  check_0:
    %5 = arith.constant {value = 0 : i64}
    %6 = std.call_runtime @mm_scope_enter %5
    memref.store %6, __scope_4
    %7 = arith.constant {value = 1 : i64}
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
  func @basics.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.movsd xmm0, [rip+__float_3.14]
    x86.movsd xmm1, [rip+__float_3.14]
    x86.ucomisd xmm0, xmm1
    x86.jne basics.main.other_1
    x86.jp basics.main.other_1
  check_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-16], eax
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.call mm_scope_exit
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.mov eax, 1
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
