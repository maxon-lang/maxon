---
feature: safe-ffi
status: experimental
keywords: [extern, ffi, foreign-function, subprocess, isolation, safe]
category: interop
---

# Safe FFI

## Developer Notes

Safe FFI provides automatic process isolation for all `extern` function calls. External code runs in a separate subprocess, protecting the main Maxon process from crashes, memory corruption, and malicious code.

### Architecture Overview

When the compiler encounters an `extern` function call:
1. Arguments are serialized to shared memory
2. A semaphore signals the FFI worker subprocess
3. The worker deserializes arguments and invokes the external function
4. Results are serialized back to shared memory
5. The worker signals completion via semaphore
6. The main process deserializes and returns the result

### Components

**Runtime Functions (Windows):**
- `@CreateProcessA` - Spawn FFI worker subprocess
- `@CreateFileMappingA` - Create shared memory region
- `@MapViewOfFile` / `@UnmapViewOfFile` - Map shared memory
- `@CreateSemaphoreA` - Create synchronization primitives
- `@WaitForSingleObject` - Wait for worker completion
- `@ReleaseSemaphore` - Signal worker
- `@TerminateProcess` - Kill crashed/hung worker
- `@GetExitCodeProcess` - Detect worker crash

**Runtime Functions (Linux):**
- `fork()` via syscall #57
- `mmap()` with MAP_SHARED via syscall #9
- `sem_init` / `sem_wait` / `sem_post` via futex syscall #202
- `waitpid()` via syscall #61
- `kill()` via syscall #62

**Shared Memory Layout:**
```
Offset  Size    Description
0       4       Magic number (0x4D584649 = "MXFI")
4       4       Version (1)
8       4       Request type: 0=call, 1=shutdown
12      4       Function ID (index in extern table)
16      4       Argument count
20      4       Return type code
24      8       Reserved
32      N       Serialized arguments (type-tagged)
32+N    M       Serialized return value
```

**Serialization Format:**
- `int` (i32): tag=1, 4 bytes little-endian
- `float` (f64): tag=2, 8 bytes IEEE 754
- `bool`: tag=3, 1 byte (0 or 1)
- `ptr`: tag=4, 8 bytes (passed as opaque handle, not dereferenceable in subprocess)
- `char`: tag=5, 1 byte

**Worker Lifecycle:**
1. NOT spawned if program has no extern function calls
2. Spawned lazily on first extern call (zero overhead if no externs used)
3. Persistent - reused for subsequent calls within same execution
4. Crashed workers are detected and respawned automatically
5. Terminated on main process exit (via atexit handler or explicit cleanup)

**Crash Handling:**
When the subprocess crashes:
1. `WaitForSingleObject` times out or returns due to process termination
2. `GetExitCodeProcess` confirms crash
3. Main process prints error message and exits with code 1
4. Future: `try_extern` syntax to handle crashes gracefully

**FFI Error Codes:**
- `100` - DLL failed to load (file not found, wrong architecture, etc.)
- `101` - Function not found in DLL (typo in function name, wrong DLL, etc.)
- `102` - Unknown function ID (internal error)

**Performance Considerations:**
- First extern call incurs subprocess spawn overhead (~10-50ms)
- Subsequent calls: ~10-100μs overhead (shared memory + semaphores)
- Batching multiple calls not yet supported
- Future: `@trusted extern` annotation for zero-overhead direct calls

### Files Modified

- `maxon-runtime/runtime_windows.mir` - Add Windows IPC functions
- `maxon-runtime/runtime_linux.mir` - Add Linux IPC syscalls
- `maxon-bin/codegen_mir.cpp` - Generate FFI wrapper code
- `maxon-bin/codegen_mir_output.cpp` - Add PE/ELF imports

## Documentation

# Safe FFI

Maxon's Safe FFI automatically isolates external code in a subprocess, protecting your program from crashes and memory corruption in C libraries.

## How It Works

When you call an `extern` function, Maxon:
1. Serializes arguments to shared memory
2. Signals a worker subprocess to execute the call
3. Waits for the result
4. Returns the value to your code

If the external code crashes, your program detects the failure and reports an error instead of crashing itself.

## Declaring Extern Functions

```text
extern function add_numbers(a int, b int) int
extern function multiply_floats(x float, y float) float
```

## Calling Extern Functions

Extern functions are called like normal functions:

```text
var sum = add_numbers(10, 20)
var product = multiply_floats(3.14, 2.0)
```

## Crash Isolation

If external code crashes, Maxon reports an error:

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
| `char` | `char` | 1 byte |
| `ptr` | `void*` | Opaque handle |

## Limitations

- Pointers passed to extern functions cannot be dereferenced by external code (they are opaque handles)
- Callbacks from C into Maxon not yet supported
- Struct passing not yet supported

## Tests

<!-- test: ffi-call-add-numbers -->
```maxon
extern function add_numbers(a int, b int) int "ffi_test_lib"

function main() int
    var result = add_numbers(5, 3)
    return result
end 'main'
```
```exitcode
8
```

<!-- test: basic-extern-int -->
```maxon
extern function add_numbers(x int, y int) int "ffi_test_lib"

function main() int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: extern-no-params -->
```maxon
extern function get_constant() int "ffi_test_lib"

function main() int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: extern-float-return -->
```maxon
extern function add_floats(x float, y float) float "ffi_test_lib"

function main() int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: extern-void-return -->
```maxon
extern function do_nothing(x int) "ffi_test_lib"

function main() int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: extern-ptr-param -->
```maxon
extern function process_ptr(p ptr) int "ffi_test_lib"

function main() int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: ffi-error-missing-dll -->
```maxon
extern function some_function(x int) int "nonexistent_dll"

function main() int
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
extern function nonexistent_function(a int, b int) int "ffi_test_lib"

function main() int
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
