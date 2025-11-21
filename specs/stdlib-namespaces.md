---
feature: stdlib-namespaces
status: stable
keywords: [stdlib, namespace, organization, fs, sys]
category: stdlib
---

# Standard Library Namespaces

## Developer Notes

The standard library is organized into namespaces for different functionality areas.

Current namespaces:
- `sys/` - System utilities (print, exit, etc.)
- `fs/` - File system operations
- `fmt/` - Formatting functions
- `math/` - Mathematical functions (planned expansion)

Implementation:
- Each stdlib namespace is a directory under `stdlib/`
- Contains `.maxon` files with function definitions
- Auto-discovered by compiler when functions are called
- Functions can be called with or without namespace qualification depending on context

Common pattern: Helper functions in same namespace for code organization.

## Documentation

The standard library organizes functions into logical namespaces.

### Available Namespaces

- **sys** - System operations (I/O, process control)
- **fs** - File system and stream operations
- **fmt** - String formatting and conversion
- **math** - Mathematical functions

### Example

```maxon
namespace fs 'fs'
    function stdout() ptr
        // Returns stdout handle
    end 'stdout'
    
    function write(handle ptr, buffer ptr, length int) int
        // Write bytes to file handle
    end 'write'
end 'fs'

function main() int
    let out = stdout()
    var msg = "Hello\n"
    write(out, msg, 6)
    return 0
end 'main'
```

## Tests

<!-- test: fs-namespace -->
```maxon
extern function GetStdHandle(nStdHandle int) ptr
extern function WriteFile(hFile ptr, lpBuffer ptr, nNumberOfBytesToWrite int, lpNumberOfBytesWritten ptr, lpOverlapped ptr) int

namespace sys 'sys'
    function STD_OUTPUT_HANDLE() int
        return 0 - 11
    end 'STD_OUTPUT_HANDLE'
end 'sys'

namespace fs 'fs'
    function stdout() ptr
        var handle = STD_OUTPUT_HANDLE()
        let h = GetStdHandle(handle)
        return h
    end 'stdout'
    
    function write(handle ptr, buffer ptr, length int) int
        var bytesWritten = 0
        let pBytesWritten = &bytesWritten
        let overlapped = 0 as ptr
        
        var result = WriteFile(handle, buffer, length, pBytesWritten, overlapped)
        
        if result = 0 'check_result'
            return 0
        end 'check_result'
        
        return bytesWritten
    end 'write'
end 'fs'

function main() int
    let out = stdout()
    var text = "OK"
    var written = write(out, text, 2)
    return 0
end 'main'
```
```
ExitCode: 0
Stdout: OK
```
