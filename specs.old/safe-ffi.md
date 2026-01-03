---
feature: safe-ffi
status: experimental
keywords: [extern, ffi, foreign-function, subprocess, isolation, safe]
category: interop
---

# Safe FFI

## Documentation

# Safe FFI

Maxon's Safe FFI automatically isolates external code in a subprocess, protecting your program from crashes and memory corruption in C libraries.

## How It Works

When you call an `extern` function linked to a DLL, Maxon:
1. Serializes arguments to shared memory
2. Signals a worker subprocess to execute the call
3. Waits for the result
4. Returns the value to your code

If the external code crashes, your program detects the failure and reports an error instead of crashing itself.

## Static Libraries vs DLLs

The compiler automatically detects whether to use static or dynamic linking:

- **Static library (.lib found):** Code is linked directly into your executable. No crash isolation, but zero runtime overhead.
- **DLL (no .lib found):** Code runs in a subprocess with crash isolation. Small overhead per call.

The compiler searches for `<libname>.lib` in the current directory and standard locations. If found, static linking is used automatically.

## Declaring Extern Functions

```maxon
extern function add_numbers(a int, b int) returns int "mylib"
extern function multiply_floats(a float, b float) returns float "mathlib"
```

The library name is specified as a string after the return type. If `mylib.lib` exists, static linking is used. Otherwise, `mylib.dll` is loaded at runtime.

## Calling Extern Functions

Extern functions are called like normal functions:

```maxon
var sum = add_numbers(10, 20)
var product = multiply_floats(3.14, 2.0)
```

## Crash Isolation

If external code in a DLL crashes, Maxon reports an error:

```text
' If crash_now() dereferences null:
var x = crash_now()  ' Program exits with FFI error, not segfault
```

## Supported Types

| Maxon Type | C Type | Notes |
|------------|--------|-------|
| `int` | `int32_t` | 4 bytes |
| `float` | `double` | 8 bytes |
| `bool` | `int8_t` | 1 byte |
| `character` | `character` | 1 byte |
| `ptr` | `void*` | Opaque handle |

## Limitations

- Pointers passed to extern functions cannot be dereferenced by external code (they are opaque handles)
- Callbacks from C into Maxon not yet supported
- Struct passing not yet supported

## Tests

<!-- test: ffi-call-add-numbers -->
```maxon
extern function add_numbers(a int, b int) returns int "ffi_test_lib"

function main() returns int
    var result = add_numbers(5, 3)
    return result
end 'main'
```
```exitcode
8
```

<!-- test: basic-extern-int -->
```maxon
extern function add_numbers(a int, b int) returns int "ffi_test_lib"

function main() returns int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: extern-no-params -->
```maxon
extern function get_constant() returns int "ffi_test_lib"

function main() returns int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: extern-float-return -->
```maxon
extern function get_pi() returns float "ffi_test_lib"

function main() returns int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: extern-void-return -->
```maxon
extern function do_nothing() "ffi_test_lib"

function main() returns int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: extern-ptr-param -->
```maxon
extern function process_ptr(p ptr) returns int "ffi_test_lib"

function main() returns int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: ffi-error-missing-dll -->
```maxon
extern function some_function(x int) returns int "nonexistent_dll"

function main() returns int
    var result = some_function(42)
    return result
end 'main'
```
```exitcode
100
```
```stdout
FFI Error: Failed to load DLL 'nonexistent_dll.dll'
```

<!-- test: ffi-error-missing-function -->
```maxon
extern function nonexistent_function(a int, b int) returns int "ffi_test_lib"

function main() returns int
    var result = nonexistent_function(5, 3)
    return result
end 'main'
```
```exitcode
101
```
```stdout
FFI Error: Function 'nonexistent_function' not found in 'ffi_test_lib.dll'
```

<!-- test: ffi-worker-crash-null-deref -->
```maxon
extern function crash_null_deref() returns int "ffi_test_lib"

function main() returns int
    var result = crash_null_deref()
    return result
end 'main'
```
```exitcode
103
```
```stdout
FFI Error: Worker process crashed
```

<!-- test: ffi-worker-crash-div-zero -->
```maxon
extern function crash_divide_by_zero(x int) returns int "ffi_test_lib"

function main() returns int
    var result = crash_divide_by_zero(42)
    return result
end 'main'
```
```exitcode
103
```
```stdout
FFI Error: Worker process crashed
```

<!-- test: ffi-worker-terminates-with-parent -->
```maxon
extern function GetCurrentProcessId() returns int "kernel32"

function main() returns int
    return GetCurrentProcessId() - GetCurrentProcessId()
end 'main'
```
```exitcode
0
```
