---
feature: stdlib-namespaces
status: stable
keywords: [stdlib, namespace, organization, fs, sys, export]
category: stdlib
---

# Standard Library Namespaces

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

