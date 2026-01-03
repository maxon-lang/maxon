---
feature: extern-functions
status: stable
keywords: [extern, ffi, foreign-function]
category: interop
---

# Extern Functions

## Documentation

The `extern` keyword declares functions defined outside your Maxon code, such as system APIs or C library functions.

### Syntax

```maxon
extern function functionName(params) returns returnType "library_name"
```
Note: No function body or `end` statement.

### Example

```maxon
extern function add_numbers(a int, b int) returns int "ffi_test_lib"

function main() returns int
    return add_numbers(10, 20)
end 'main'
```
```exitcode
0
```


## Tests

<!-- test: basic-extern -->
```maxon
extern function add_numbers(a int, b int) returns int "ffi_test_lib"

function main() returns int
    return 0
end 'main'
```
```exitcode
0
```

