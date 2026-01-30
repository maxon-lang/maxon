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
--- maxon
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
--- standard
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
--- x86
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
