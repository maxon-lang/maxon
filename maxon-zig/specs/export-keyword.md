---
feature: export-keyword
status: stable
keywords: [export, visibility, module, function, type]
category: infrastructure
---

# Export Keyword

## Developer Notes

The `export` keyword controls symbol visibility across module boundaries. When compiling multiple files together, only exported symbols from earlier modules are visible to later modules.

**Key Components:**

1. Parser parses `export` before `function`, `type`, and `interface`
2. AST stores `is_export: bool` on `FunctionDecl`, `TypeDecl`, `MethodDecl`, `InterfaceDecl`
3. IR stores `is_exported: bool` on `Function`
4. During module merge, only exported functions are included (via `MergeOptions.exports_only`)

**Export Rules:**
- Functions, types, and interfaces can be exported
- Methods within types can be independently exported
- Non-exported symbols are only visible within the same module
- User code (main module) doesn't need exports - all its symbols are included

**Usage Pattern:**
```maxon
export function helper() returns int
    return 42
end 'helper'

export type Counter
    var value int

    export function increment()
        value = value + 1
    end 'increment'
end 'Counter'
```

## Documentation

### Export Keyword

The `export` keyword marks functions, types, and interfaces as visible to other modules. This is primarily used in the standard library to expose public APIs while keeping implementation details private.

```text
export function publicApi() returns int
    return privateHelper()
end 'publicApi'

function privateHelper() returns int
    return 42
end 'privateHelper'
```

When modules are compiled together, only exported symbols from earlier modules can be called by later modules.

### Exporting Types

Types can be exported to make them available to other modules:

```text
export type Point
    var x int
    var y int
end 'Point'
```

### Exporting Methods

Methods within types can be individually exported:

```text
export type Calculator
    var result int

    export function add(n int)
        result = result + n
    end 'add'

    function internalReset()
        result = 0
    end 'internalReset'
end 'Calculator'
```

## Tests

<!-- test: export-function-basic -->
```maxon
export function helper() returns int
    return 21
end 'helper'

function main() returns int
    return helper() + helper()
end 'main'
```
```exitcode
42
```

<!-- test: export-type-basic -->
```maxon
export type Point
    var x int
    var y int

    export function sum() returns int
        return x + y
    end 'sum'
end 'Point'

function main() returns int
    var p = Point{x: 20, y: 22}
    return p.sum()
end 'main'
```
```exitcode
42
```

<!-- test: non-export-function-works -->
```maxon
function helper() returns int
    return 42
end 'helper'

function main() returns int
    return helper()
end 'main'
```
```exitcode
42
```

<!-- test: mixed-export-and-non-export -->
```maxon
export function publicFunc() returns int
    return privateFunc() + 20
end 'publicFunc'

function privateFunc() returns int
    return 22
end 'privateFunc'

function main() returns int
    return publicFunc()
end 'main'
```
```exitcode
42
```
