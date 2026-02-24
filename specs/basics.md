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
    %1 = arith.constant {value = 42 : i64}
    func.return %1
  }
  func @basics.main() -> u32 {
  entry:
    %3 = func.call @basics.getValue
    memref.store %3, __range_val_0
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.cmpi lt %3, %4
    %6 = arith.constant {value = 4294967295 : i64}
    %7 = arith.cmpi gt %3, %6
    %8 = arith.ori1 %5, %7
    cf.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %9 = memref.lea_symdata __panic_msg_9
    %10 = std.ptr_to_i64 %9
    std.call_runtime @maxon_panic %10
  __range_ok_0:
    %11 = memref.load __range_val_0 : i64
    func.return %11
  }
}
=== x86
module {
  func @basics.getValue() -> i64 {
  entry:
    x86.mov eax, 42
    x86.ret
  }
  func @basics.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.call basics.getValue
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
    x86.je basics.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_9]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-8]
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
    %1 = arith.float_constant {value = 3.14 : f64}
    %2 = arith.float_constant {value = 3.14 : f64}
    %3 = arith.cmpf eq %1, %2
    cf.cond_br %3 [then: check_0, else: other_1]
  check_0:
    %5 = arith.constant {value = 1 : i64}
    func.return %5
  other_1:
    %7 = arith.constant {value = 0 : i64}
    func.return %7
  }
}
=== x86
module {
  func @basics.main() -> u32 {
  entry:
    x86.movsd xmm0, [rip+__float_3.14]
    x86.movsd xmm1, [rip+__float_3.14]
    x86.ucomisd xmm0, xmm1
    x86.jne basics.main.other_1
    x86.jp basics.main.other_1
  check_0:
    x86.mov eax, 1
    x86.ret
  other_1:
    x86.xor eax, eax
    x86.ret
  }
}
```
