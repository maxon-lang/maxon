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

The `main` function must return `int`. If it has no return type or returns a different type, the compiler reports:

```text
error E3002: Function 'main' must return int
```

## Tests

<!-- test: no-main -->
```maxon
function notmain() returns int
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
error E3002: Function 'main' must return int
```

<!-- test: valid-main -->
```maxon
function main() returns int
  return 42
end 'main'
```
```exitcode
42
```

<!-- test: return-function-call -->
```maxon
function getValue() returns int
  return 42
end 'getValue'

function main() returns int
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
    maxon.return %0
  }
  func @basics.main() -> i64 {
  entry:
    %1 = maxon.call @basics.getValue
    maxon.return %1
  }
}
=== standard
module {
  func @basics.getValue() -> i64 {
  entry:
    %0 = arith.constant {value = 42 : i64}
    func.return %0
  }
  func @basics.main() -> i64 {
  entry:
    %1 = func.call @basics.getValue
    func.return %1
  }
}
=== x86
module {
  func @basics.getValue() -> i64 {
  entry:
    x86.mov eax, 42
    x86.ret
  }
  func @basics.main() -> i64 {
  entry:
    x86.call basics.getValue
    x86.ret
  }
}
```

<!-- test: float-var-if-else -->
```maxon
function main() returns int
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
    maxon.return %3
  other_1:
    %4 = maxon.literal {value = 0 : i64}
    maxon.return %4
  }
}
=== standard
module {
  func @basics.main() -> i64 {
  entry:
    %0 = arith.float_constant {value = 3.14 : f64}
    memref.store %0, x
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
  func @basics.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.movsd xmm0, [rip+__float_3.14]
    x86.movsd [rbp-8], xmm0
    x86.movsd xmm1, [rip+__float_3.14]
    x86.ucomisd xmm0, xmm1
    x86.jne basics.main.other_1
    x86.jp basics.main.other_1
  check_0:
    x86.mov eax, 1
    x86.epilogue
    x86.ret
  other_1:
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
}
```
