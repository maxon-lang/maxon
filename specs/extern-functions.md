---
feature: extern-functions
status: stable
keywords: [extern, ffi, foreign-function]
category: interop
---

# Extern Functions

## Developer Notes

The `extern` keyword declares functions that are defined outside the Maxon program (typically in Windows API or C libraries).

Implementation:
- Parsed in `Parser::parseExternFunction()`
- Represented by `ExternFunctionDecl` AST node
- No function body - just signature
- Code generation creates LLVM `declare` statement
- Calling convention defaults to C calling convention
- No name mangling applied to extern functions
- Common use: Windows API functions (GetStdHandle, WriteFile, etc.)

The compiler assumes extern functions exist at link time. The linker resolves them against system libraries or maxon-runtime.

## Documentation

The `extern` keyword declares functions defined outside your Maxon code, such as Windows API functions.

### Syntax

```maxon
extern function name(param1 type1, param2 type2) returnType
```

Note: No function body or `end` statement.

### Example

```maxon
extern function TestFunc(x int, p ptr, c char) int

function main() int
    return 0
end 'main'
```
```output
ExitCode: 0
```

## Tests

<!-- test: basic-extern -->
```maxon
extern function TestFunc(x int, p ptr, c char) int

function main() int
    return 0
end 'main'
```
```
ExitCode: 0
```

<!-- test: windows-api -->
```maxon
extern function GetStdHandle(handle int) ptr
extern function WriteFile(hFile ptr, buffer ptr, nBytes int, written ptr, overlapped ptr) int

function main() int
    let stdout = GetStdHandle(-11)
    var written = 0
    var text = "Test"
    WriteFile(stdout, text, 4, &written, 0 as ptr)
    return 0
end 'main'
```
```
ExitCode: 0
Stdout: Test
```
