---
feature: namespaces
status: stable
keywords: [namespace, organization, scope, export]
category: organization
---

# Namespaces

## Developer Notes

Namespaces are automatically derived from file paths and provide logical grouping of functions. Key implementation:

- Namespace derived from file path in `deriveNamespace()` (compiler.cpp)
- File path `stdlib/fmt/integer.maxon` → namespace `stdlib.fmt`
- `export` keyword makes functions visible outside the file
- Non-exported functions have internal linkage (file-private)
- Functions are registered with qualified names (namespace.function)
- Suffix matching allows unqualified calls when unambiguous
- Names are mangled with namespace prefix for uniqueness in LLVM IR

The semantic analyzer handles name resolution using suffix matching for unqualified names.

## Documentation

Namespaces are derived from the file's location in the directory structure. Functions can be exported to make them available to other files.

### File-Based Namespaces

The namespace of a file is determined by its path:
- `math.maxon` in root → no namespace (global)
- `utils/helpers.maxon` → namespace `utils`
- `stdlib/fmt/integer.maxon` → namespace `stdlib.fmt`

### Export Keyword

Use `export` to make functions visible outside the file:

```maxon
export function public_add(a int, b int) returns int
    return a + b
end 'public_add'

function private_helper(x int) returns int
    return x * 2
end 'private_helper'
```
Only `public_add` can be called from other files. `private_helper` is file-private.

### Example

File: `math/operations.maxon`

```maxon
export function add(a int, b int) returns int
    return a + b
end 'add'

export function multiply(x int, y int) returns int
    return x * y
end 'multiply'

function main() returns int
    return add(3, 4)  // Called from within same file
end 'main'
```
```exitcode
7
```


## Tests

<!-- test: basic-namespace -->
```maxon
export function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(10, 20)
end 'main'
```
```exitcode
30
```


<!-- test: multiple-functions -->
```maxon
export function double(x int) returns int
    return x * 2
end 'double'

export function triple(x int) returns int
    return x * 3
end 'triple'

function main() returns int
    return double(5) + triple(4)
end 'main'
```
```exitcode
22
```


<!-- test: nested-calls-in-namespace -->
```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function sum_three(a int, b int, c int) returns int
    return add(add(a, b), c)
end 'sum_three'

function main() returns int
    return sum_three(1, 2, 3)
end 'main'
```
```exitcode
6
```

