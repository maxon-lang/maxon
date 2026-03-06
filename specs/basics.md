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
    %0 = maxon.literal {value = 42 : i64}
    maxon.scope_end []
    maxon.return %0
  }
  func @basics.main() -> i64 {
  entry:
    %1 = maxon.call @basics.getValue
    maxon.assign %1 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.binop %1, %2 {op = lt}
    %4 = maxon.literal {value = 4294967295 : i64}
    %5 = maxon.binop %1, %4 {op = gt}
    %6 = maxon.binop %3, %5 {op = or}
    maxon.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at return-function-call.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %8 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_end [__range_val_0]
    maxon.return %8
  }
}
=== standard
module {
  func @basics.getValue() -> i64 {
  entry:
    %0 = arith.constant {value = 42 : i64}
    func.return %0
  }
  func @basics.main() -> u32 {
  entry:
    %1 = func.call @basics.getValue
    memref.store %1, __range_val_0
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.cmpi lt %1, %2
    %4 = arith.constant {value = 4294967295 : i64}
    %5 = arith.cmpi gt %1, %4
    %6 = arith.ori1 %3, %5
    cf.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %7 = memref.lea_symdata __panic_msg_7
    %8 = std.ptr_to_i64 %7
    std.call_runtime @maxon_panic %8
  __range_ok_0:
    %9 = memref.load __range_val_0 : i64
    func.return %9
  }
}
=== x86
module {
  func @basics.getValue() -> i64 {
  entry:
    x86.mov rax, 42
    x86.ret
  }
  func @basics.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.call basics.getValue
    x86.mov [rbp-8], rax
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.setl rcx
    x86.movzx rcx, rcxb
    x86.mov rdx, 4294967295
    x86.cmp rax, rdx
    x86.setg rax
    x86.movzx rax, raxb
    x86.or rcx, rax
    x86.test rcx, rcx
    x86.je basics.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_7]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov rax, [rbp-8]
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
    %0 = maxon.literal {value = 3.14 : f64}
    maxon.assign %0 {var = x} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 3.14 : f64}
    %2 = maxon.binop %0, %1 {op = eq} {kind = f64}
    maxon.cond_br %2 [then: check_0, else: other_1]
  check_0:
    %3 = maxon.literal {value = 1 : i64}
    maxon.scope_end [x]
    maxon.return %3
  other_1:
    %4 = maxon.literal {value = 0 : i64}
    maxon.scope_end [x]
    maxon.return %4
  }
}
=== standard
module {
  func @basics.main() -> u32 {
  entry:
    %0 = arith.float_constant {value = 3.14 : f64}
    %1 = arith.float_constant {value = 3.14 : f64}
    %2 = arith.cmpf eq %0, %1
    cf.cond_br %2 [then: check_0, else: other_1]
  check_0:
    %3 = arith.constant {value = 1 : i64}
    func.return %3
  other_1:
    %4 = arith.constant {value = 0 : i64}
    func.return %4
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
    x86.mov rax, 1
    x86.ret
  other_1:
    x86.xor rax, rax
    x86.ret
  }
}
```
