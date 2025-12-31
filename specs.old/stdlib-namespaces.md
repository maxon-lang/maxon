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
- `stdlib/sys/` - System utilities
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

- **stdlib.sys** - System operations
- **stdlib.fs** - File system and stream operations  
- **stdlib.fmt** - String formatting and conversion
- **stdlib.math** - Mathematical functions
- **stdlib.iter** - Iterators and ranges

### Example

```maxon
function main() returns int
    // Use standard library function
    print("{42}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```


## Tests

