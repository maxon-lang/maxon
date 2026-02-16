---
feature: namespaces
status: stable
keywords: [namespace, organization, scope, export]
category: organization
---

# Namespaces

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
typealias Score = int(i64.min to i64.max)

export function public_add(a Score, b Score) returns Score
  return a + b
end 'public_add'

function private_helper(x Score) returns Score
  return x * 2
end 'private_helper'
```
Only `public_add` can be called from other files. `private_helper` is file-private.

### Example

File: `math/operations.maxon`

```maxon
typealias Score = int(i64.min to i64.max)

export function add(a Score, b Score) returns Score
  return a + b
end 'add'

export function multiply(x Score, y Score) returns Score
  return x * y
end 'multiply'

function main() returns ExitCode
  return add(3, b: 4)  // Called from within same file
end 'main'
```
```exitcode
7
```


## Tests

<!-- test: basic-namespace -->
```maxon

typealias Integer = int(i64.min to i64.max)

export function add(a Integer, b Integer) returns Integer
  return a + b
end 'add'

function main() returns ExitCode
  return add(10, b: 20)
end 'main'
```
```exitcode
30
```


<!-- test: multiple-functions -->
```maxon

typealias Integer = int(i64.min to i64.max)

export function double(x Integer) returns Integer
  return x * 2
end 'double'

export function triple(x Integer) returns Integer
  return x * 3
end 'triple'

function main() returns ExitCode
  return double(5) + triple(4)
end 'main'
```
```exitcode
22
```


<!-- test: nested-calls-in-namespace -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
  return a + b
end 'add'

function sum_three(a Integer, b Integer, c Integer) returns Integer
  return add(add(a, b: b), b: c)
end 'sum_three'

function main() returns ExitCode
  return sum_three(1, b: 2, c: 3)
end 'main'
```
```exitcode
6
```

