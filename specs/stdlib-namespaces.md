---
feature: stdlib-namespaces
status: stable
keywords: [stdlib, namespace, organization, fs, sys, export]
category: stdlib
---

# Standard Library Namespaces

## Developer Notes

The standard library is organized into namespaces based on file location.

Current namespaces:
- `stdlib/sys/` - System utilities (Windows API wrappers)
- `stdlib/fs/` - File system operations
- `stdlib/fmt/` - Formatting functions
- `stdlib/math/` - Mathematical functions
- `stdlib/iter/` - Iterator support

Implementation:
- Each stdlib namespace is a directory under `stdlib/`
- Contains `.maxon` files with function definitions
- Auto-discovered by compiler when functions are called
- Functions use `export` keyword for public API
- Helper functions without `export` are file-private

## Documentation

The standard library organizes functions into logical namespaces based on file paths.

### Available Namespaces

- **stdlib.sys** - System operations (Windows API wrappers)
- **stdlib.fs** - File system and stream operations  
- **stdlib.fmt** - String formatting and conversion
- **stdlib.math** - Mathematical functions
- **stdlib.iter** - Iterators and ranges

### Example

```maxon
function main() int
    // Use standard library function
    print(42)
    return 0
end 'main'
```
```output
ExitCode: 0
Stdout: 42
```

## Tests

<!-- test: fs-namespace -->
```maxon
export extern function GetStdHandle(nStdHandle int) ptr
export extern function WriteFile(hFile ptr, lpBuffer ptr, nNumberOfBytesToWrite int, lpNumberOfBytesWritten ptr, lpOverlapped ptr) int

export function STD_OUTPUT_HANDLE() int
    return 0 - 11
end 'STD_OUTPUT_HANDLE'

export function stdout() ptr
    var handle = STD_OUTPUT_HANDLE()
    let h = GetStdHandle(handle)
    return h
end 'stdout'

export function write(handle ptr, buffer ptr, length int) int
    var bytesWritten = 0
    let pBytesWritten = &bytesWritten
    let overlapped = 0 as ptr
    
    var result = WriteFile(handle, buffer, length, pBytesWritten, overlapped)
    
    if result = 0 'check_result'
        return 0
    end 'check_result'
    
    return bytesWritten
end 'write'

function main() int
    let out = stdout()
    var text = "OK"
    write(out, text, 2)
    return 0
end 'main'
```
```output
ExitCode: 0
Stdout: OK
```
