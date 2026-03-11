---
feature: conditional-compilation
status: experimental
keywords: [conditional, compilation, os, arch, platform, if]
category: basics
---

# Conditional Compilation

## Documentation

Maxon supports Swift-style conditional compilation directives `#if`, `#else`, and `#endif` to include or exclude code based on the target platform.

### `#if os(...)`

Use `#if os(Windows)` or `#if os(Linux)` to conditionally compile based on the target operating system:

```maxon
function main() returns ExitCode
  #if os(Windows)
    print("windows\n")
  #else
    print("other\n")
  #endif
  return 0
end 'main'
```
```exitcode
0
```
```stdout
windows
```

### `#if arch(...)`

Use `#if arch(x86_64)` or `#if arch(aarch64)` to conditionally compile based on the target CPU architecture:

```maxon
function main() returns ExitCode
  #if arch(x86_64)
    print("x86\n")
  #else
    print("arm\n")
  #endif
  return 0
end 'main'
```
```exitcode
0
```
```stdout
x86
```

## Tests

<!-- test: if-os-windows -->
```maxon
function main() returns ExitCode
  #if os(Windows)
    print("win\n")
  #endif
  return 0
end 'main'
```
```exitcode
0
```
```stdout
win
```

<!-- test: if-os-else -->
```maxon
function main() returns ExitCode
  #if os(Linux)
    print("linux\n")
  #else
    print("not-linux\n")
  #endif
  return 0
end 'main'
```
```exitcode
0
```
```stdout
not-linux
```

<!-- test: if-arch-x86-64 -->
```maxon
function main() returns ExitCode
  #if arch(x86_64)
    print("x86_64\n")
  #endif
  return 0
end 'main'
```
```exitcode
0
```
```stdout
x86_64
```

<!-- test: if-arch-else -->
```maxon
function main() returns ExitCode
  #if arch(aarch64)
    print("arm\n")
  #else
    print("not-arm\n")
  #endif
  return 0
end 'main'
```
```exitcode
0
```
```stdout
not-arm
```

<!-- test: if-os-in-function -->
```maxon
function getPlatform() returns String
  #if os(Windows)
    return "Windows"
  #else
    return "Other"
  #endif
end 'getPlatform'

function main() returns ExitCode
  print("{getPlatform()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
Windows
```

<!-- test: if-os-top-level -->
```maxon
#if os(Windows)
  let platform = "Windows"
#else
  let platform = "Other"
#endif

function main() returns ExitCode
  print("{platform}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
Windows
```

<!-- test: if-os-top-level-function -->
```maxon
#if os(Windows)
function hello() returns String
  return "hello from windows"
end 'hello'
#else
function hello() returns String
  return "hello from other"
end 'hello'
#endif

function main() returns ExitCode
  print("{hello()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello from windows
```

<!-- test: if-arch-top-level -->
```maxon
#if arch(x86_64)
  let arch = "x86_64"
#else
  let arch = "aarch64"
#endif

function main() returns ExitCode
  print("{arch}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
x86_64
```
