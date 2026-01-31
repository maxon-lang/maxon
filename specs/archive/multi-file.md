---
feature: multi-file
status: stable
keywords: [multi-file, compilation, module, merge, stdlib]
category: infrastructure
---

# Multi-file Compilation

## Documentation

### Multi-file Compilation

The Maxon compiler supports compiling multiple source files into a single executable. This enables modular code organization and standard library integration.

When multiple files are compiled together:
1. Each file is compiled independently
2. IR (Intermediate Representation) from all files is merged
3. The merged IR is optimized and compiled to native code

### Standard Library

The standard library is automatically located relative to the compiler executable. User code can use types and functions from stdlib modules.

## Tests

<!-- test: single-file-still-works -->
```maxon
function main() returns int
  return 42
end 'main'
```
```exitcode
42
```

<!-- test: function-call-basic -->
```maxon
function helper() returns int
  return 21
end 'helper'

function main() returns int
  return helper() + helper()
end 'main'
```
```exitcode
42
```

<!-- test: type-with-method-basic -->
```maxon
type Counter
  var value int

  function add(n int) returns int
    value = value + n
    return value
  end 'add'
end 'Counter'

function main() returns int
  var c = Counter{value: 0}
  c.add(20)
  return c.add(22)
end 'main'
```
```exitcode
42
```
