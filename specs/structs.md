---
feature: structs
status: stable
keywords: [struct, field, let, var, mutability, instance]
category: type-system
---

# Structs

## Developer Notes

Structs are composite types that group related data together.

**Declaration:**
- Struct fields must be declared with `let` (immutable) or `var` (mutable)
- Fields can have explicit types or inferred types with initializers

**Mutability Rules:**
- Struct instance mutability determined by `let` vs `var` declaration
- `let` struct: Cannot modify any fields (instance is immutable)
- `var` struct: Can modify `var` fields, cannot modify `let` fields
- Field-level mutability is checked at compile time

**Implementation:**
- Parser: `parseStructDef()` in parser_decl.cpp
- AST: `StructDefAST` with `StructField` entries containing `isImmutable` flag
- Semantic: `VariableInfo.isImmutable` tracks instance mutability
- Semantic: `StructFieldInfo.isImmutable` tracks field mutability
- Assignment validation in `analyzeAssignmentStmt()` checks both

## Documentation

Structs define custom data types with named fields.

### Declaration

```maxon
struct Point
    var x int
    var y int
end 'Point'
```

Fields must use `let` (immutable) or `var` (mutable):
```maxon
struct Config
    let version int    // Cannot be changed after initialization
    var count int      // Can be modified
end 'Config'
```

### Instantiation

Create struct instances with literal syntax:
```maxon
var p = Point{x: 10, y: 20}
let config = Config{version: 1, count: 0}
```

### Instance Mutability

The mutability of a struct instance is determined by `let` vs `var`:

**var struct** - Can modify `var` fields:
```maxon
var p = Point{x: 10, y: 20}
p.x = 30   // OK: struct is mutable, field is var
```

**let struct** - Cannot modify any fields:
```maxon
let p = Point{x: 10, y: 20}
// p.x = 30   // ERROR: struct instance is immutable
```

### Field Mutability

Even on a `var` struct, `let` fields cannot be modified:
```maxon
var c = Config{version: 1, count: 0}
c.count = 5     // OK: field is var
// c.version = 2   // ERROR: field is let
```

## Tests

<!-- test: var-struct-field-assign -->
```maxon
struct Point
    var x int
    var y int
end 'Point'

function main() int
    var p = Point{x: 10, y: 20}
    p.x = 30
    return p.x
end 'main'
```
```exitcode
30
```

<!-- test: var-field-assign -->
```maxon
struct Config
    let version int
    var count int
end 'Config'

function main() int
    var c = Config{version: 1, count: 0}
    c.count = 5
    return c.count
end 'main'
```
```exitcode
5
```

<!-- test: error.let-struct-field-assign -->
```maxon
struct Point
    var x int
    var y int
end 'Point'

function main() int
    let p = Point{x: 10, y: 20}
    p.x = 30
    return p.x
end 'main'
```
```maxoncstderr
Semantic Error: line 9, column 5
Cannot assign to field of read-only struct 'p'
  Variable declared with 'let' at line 8, column 5
  Note: Variables declared with 'let' are immutable (read-only). Use 'var' for mutable structs

  9 |     p.x = 30
    |     ^
```

<!-- test: error.let-field-assign -->
```maxon
struct Config
    let version int
    var count int
end 'Config'

function main() int
    var c = Config{version: 1, count: 0}
    c.version = 2
    return c.version
end 'main'
```
```maxoncstderr
Semantic Error: line 9, column 5
Cannot assign to immutable field 'version' of struct 'Config'
  Field declared with 'let' at line 3
  Note: Fields declared with 'let' are immutable. Use 'var' for mutable fields

  9 |     c.version = 2
    |     ^
```

