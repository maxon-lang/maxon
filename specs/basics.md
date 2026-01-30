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
  func @getValue() -> i64 {
  entry:
    %0 = maxon.constant {value = 42 : i64}
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    maxon.call @getValue
    maxon.return
  }
}
=== standard
module {
  func @getValue() -> i64 {
  entry:
    %1 = arith.constant {value = 42 : i64}
    func.return %1
  }
  func @main() -> i64 {
  entry:
    %2 = func.call @getValue
    func.return %2
  }
}
=== x86
module {
  func @getValue() -> i64 {
  entry:
    x86.push rbp
    x86.mov rbp, rsp
    x86.mov eax, 42
    x86.pop rbp
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.push rbp
    x86.mov rbp, rsp
    x86.call getValue
    x86.pop rbp
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
  func @main() -> i64 {
  entry:
    %0 = maxon.constant {value = 3.14 : f64}
    maxon.var_decl x %0
    %1 = maxon.var_load x {type = f64}
    %2 = maxon.constant {value = 3.14 : f64}
    %3 = maxon.cmp eq %1, %2 {type = f64}
    maxon.cond_br %3 [then: check, else: other]
  check:
    %4 = maxon.constant {value = 1 : i64}
    maxon.return %4
  other:
    %5 = maxon.constant {value = 0 : i64}
    maxon.return %5
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %6 = arith.float_constant {value = 3.14 : f64}
    memref.store %6, x
    %7 = memref.load x : f64
    %8 = arith.float_constant {value = 3.14 : f64}
    %9 = arith.cmpf eq %7, %8
    cf.cond_br %9 [then: check, else: other]
  check:
    %10 = arith.constant {value = 1 : i64}
    func.return %10
  other:
    %11 = arith.constant {value = 0 : i64}
    func.return %11
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 16
    x86.movsd xmm0, [rip+__float_3.14]
    x86.movsd [rbp-8], xmm0
    x86.movsd xmm0, [rbp-8]
    x86.movsd xmm1, [rip+__float_3.14]
    x86.ucomisd xmm0, xmm1
    x86.jne main.other
    x86.jp main.other
  check:
    x86.mov eax, 1
    x86.add rsp, 16
    x86.pop rbp
    x86.ret
  other:
    x86.mov eax, 0
    x86.add rsp, 16
    x86.pop rbp
    x86.ret
  }
}
```
