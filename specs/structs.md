---
feature: types
status: stable
keywords: [type, field, let, var, mutability, instance]
category: type-system
---

# Types

## Developer Notes

Types are composite types that group related data together.

**Declaration:**
- Type fields must be declared with `let` (immutable) or `var` (mutable)
- Fields can have explicit types or inferred types with initializers

**Mutability Rules:**
- Type instance mutability determined by `let` vs `var` declaration
- `let` type: Cannot modify any fields (instance is immutable)
- `var` type: Can modify `var` fields, cannot modify `let` fields
- Field-level mutability is checked at compile time

**Implementation:**
- Parser: `parseStructDef()` in parser_decl.cpp
- AST: `StructDefAST` with `StructField` entries containing `isImmutable` flag
- Semantic: `VariableInfo.isImmutable` tracks instance mutability
- Semantic: `StructFieldInfo.isImmutable` tracks field mutability
- Assignment validation in `analyzeAssignmentStmt()` checks both

## Documentation

Types define custom data types with named fields.

### Declaration

```maxon
type Point
    var x int
    var y int
end 'Point'
```

Fields must use `let` (immutable) or `var` (mutable):
```maxon
type Config
    let version int    // Cannot be changed after initialization
    var count int      // Can be modified
end 'Config'
```

### Instantiation

Create type instances with literal syntax:
```maxon
var p = Point{x: 10, y: 20}
let config = Config{version: 1, count: 0}
```

### Instance Mutability

The mutability of a type instance is determined by `let` vs `var`:

**var type** - Can modify `var` fields:
```maxon
var p = Point{x: 10, y: 20}
p.x = 30   // OK: type is mutable, field is var
```

**let type** - Cannot modify any fields:
```maxon
let p = Point{x: 10, y: 20}
// p.x = 30   // ERROR: type instance is immutable
```

### Field Mutability

Even on a `var` type, `let` fields cannot be modified:
```maxon
var c = Config{version: 1, count: 0}
c.count = 5     // OK: field is var
// c.version = 2   // ERROR: field is let
```

## Tests

<!-- test: var-struct-field-assign -->
```maxon
type Point
    var x int
    var y int
end 'Point'

function main() returns int
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
type Config
    let version int
    var count int
end 'Config'

function main() returns int
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
type Point
    var x int
    var y int
end 'Point'

function main() returns int
    let p = Point{x: 10, y: 20}
    p.x = 30
    return p.x
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:9:5
Cannot assign to field of read-only type 'p'
  Variable declared with 'let' at line 8, column 5
  Note: Variables declared with 'let' are immutable (read-only). Use 'var' for mutable types

  9 |     p.x = 30
    |     ^
```

<!-- test: error.let-field-assign -->
```maxon
type Config
    let version int
    var count int
end 'Config'

function main() returns int
    var c = Config{version: 1, count: 0}
    c.version = 2
    return c.version
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:9:5
Cannot assign to immutable field 'version' of type 'Config'
  Field declared with 'let' at line 3
  Note: Fields declared with 'let' are immutable. Use 'var' for mutable fields

  9 |     c.version = 2
    |     ^
```

<!-- test: simple-type -->
```maxon
type Point
    var x int
    var y int
end 'Point'

function main() returns int
    var p = Point { x: 3, y: 4 }
    return p.x + p.y
end 'main'
```
```exitcode
7
```

<!-- test: struct-field-access -->
```maxon
type Rect
    var width int
    var height int
end 'Rect'

function main() returns int
    var r = Rect { width: 5, height: 10 }
    return r.width * r.height
end 'main'
```
```exitcode
50
```

<!-- test: struct-param -->
```maxon
type Vec2
    var x int
    var y int
end 'Vec2'

function dot(a Vec2, b Vec2) returns int
    return a.x * b.x + a.y * b.y
end 'dot'

function main() returns int
    var v1 = Vec2 { x: 3, y: 4 }
    var v2 = Vec2 { x: 2, y: 1 }
    return dot(v1, v2)
end 'main'
```
```exitcode
10
```

<!-- test: struct-return -->
```maxon
type Pair
    var first int
    var second int
end 'Pair'

function makePair(a int, b int) returns Pair
    return Pair { first: a, second: b }
end 'makePair'

function main() returns int
    var p = makePair(5, 7)
    return p.first + p.second
end 'main'
```
```exitcode
12
```

<!-- test: struct-literal-as-arg -->
```maxon
type Point
    var x int
    var y int
end 'Point'

function acceptPoint(p Point) returns int
    return p.x + p.y
end 'acceptPoint'

function main() returns int
    return acceptPoint(Point{x: 3, y: 4})
end 'main'
```
```exitcode
7
```

<!-- test: struct-field-default -->
```maxon
type Counter
	var value int = 0
	var step int = 1
end 'Counter'

function main() returns int
	var c1 = Counter{}
	var c2 = Counter{value: 40}
	var c3 = Counter{value: 10, step: 2}
	return c1.value + c2.value + c3.step
end 'main'
```
```exitcode
42
```

<!-- test: struct-field-inferred-type -->
```maxon
type Settings
	let maxRetries = 5
	var timeout = 50.0
end 'Settings'

function main() returns int
	var s = Settings{}
	return s.maxRetries + trunc(s.timeout)
end 'main'
```
```exitcode
55
```

