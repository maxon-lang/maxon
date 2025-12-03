---
feature: extern-functions
status: stable
keywords: [extern, ffi, foreign-function]
category: interop
---

# Extern Functions

## Developer Notes

The `extern` keyword declares functions that are defined outside the Maxon program (typically in system libraries or C libraries).

Implementation:
- Parsed in `Parser::parseExternFunction()`
- Represented by `ExternFunctionDecl` AST node
- No function body - just signature
- Code generation creates LLVM `declare` statement
- Calling convention defaults to C calling convention
- No name mangling applied to extern functions
- Common use: System APIs and C library functions

The compiler assumes extern functions exist at link time. The linker resolves them against system libraries or maxon-runtime.

## Documentation

The `extern` keyword declares functions defined outside your Maxon code, such as system APIs or C library functions.

### Syntax

```maxon
extern function name(param1 type1, param2 type2) returnType "library_name"
```
Note: No function body or `end` statement.

### Example

```maxon
extern function TestFunc(x int, y int, c character) int "ffi_test_lib"

function main() int
    return 0
end 'main'
```
```exitcode
0
```


## Tests

<!-- test: basic-extern -->
```maxon
extern function TestFunc(x int, y int, c character) int "ffi_test_lib"

function main() int
    return 0
end 'main'
```
```exitcode
0
```

