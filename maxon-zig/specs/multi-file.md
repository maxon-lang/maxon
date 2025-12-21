---
feature: multi-file
status: stable
keywords: [multi-file, compilation, module, merge, stdlib]
category: infrastructure
---

# Multi-file Compilation

## Developer Notes

Multi-file compilation allows combining multiple source files into a single executable. This is the foundation for stdlib integration.

**Key Components:**

1. `Source` struct - represents a source file with path and content
2. `compileMultiple(sources, ...)` - compiles multiple sources and merges IR
3. `compileWithStdlib(user_source, stdlib_sources, ...)` - stdlib-first compilation
4. `findStdlibPath()` - locates stdlib relative to executable
5. `loadStdlibModule(stdlib_path, module_name)` - loads a stdlib module

**IR Merge Behavior:**
- Functions from each module are merged into a single IR module
- Duplicate function names: first definition wins (stdlib before user code)
- Each source file is compiled independently, then IR is merged before codegen

**Stdlib Path Resolution:**
1. Try `exe_dir/../stdlib` (development layout)
2. Try `exe_dir/stdlib` (installed layout)

**Usage Pattern:**
```zig
// Load stdlib modules
const stdlib_path = try findStdlibPath(allocator);
const array_mod = try loadStdlibModule(stdlib_path, "collections/array", allocator);

// Compile with stdlib
try compileWithStdlib(user_source, &.{array_mod}, output_path, options, allocator, &result);
```

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
