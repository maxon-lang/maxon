---
feature: basics
status: selfhosted
keywords: [main, return, semantic, validation]
category: basics
---

## Documentation

The compiler performs semantic checks before lowering the IR pipeline. These checks validate program structure requirements.

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
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #42
    arm64.ret
  }
}

```

<!-- test: float-var-if-else -->
```maxon
function main() returns ExitCode
	let x = 3.14
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
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.movsd xmm0, [rip+__float_4614253070214989087]
    x64.movsd xmm1, [rip+__float_4614253070214989087]
    x64.ucomisd xmm1, xmm0
    x64.jp other_0
    x64.jne other_0
  check_0:
    x64.mov r8d, 1
    x64.ret
  other_0:
    x64.xor r8d, r8d
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.ldr d0, [rdata+__float_4614253070214989087]
    arm64.ldr d1, [rdata+__float_4614253070214989087]
    arm64.fcmp d1, d0
    arm64.cset x0, eq
    arm64.b.ne other_0
  check_0:
    arm64.mov x0, #1
    arm64.ret
  other_0:
    arm64.mov x0, #0
    arm64.ret
  }
}

```
