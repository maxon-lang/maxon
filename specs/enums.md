---
feature: enums
status: experimental
keywords: [enum, case, enumeration, associated values, raw values]
category: type-system
---

# Enums

## Developer Notes

Enums are sum types that define a type with a fixed set of named variants (cases). Maxon enums support three forms: simple enums, raw value enums, and enums with associated values.

### Syntax

**Simple enum:**
```
enum EnumName
    case caseName
    case caseName2
end 'EnumName'
```

**Raw value enum:**
```
enum EnumName int
    case caseName = 1
    case caseName2 = 2
end 'EnumName'
```

**Enum with associated values:**
```
enum EnumName
    case caseName(fieldName Type)
    case caseName2(field1 Type1, field2 Type2)
    case caseName3
end 'EnumName'
```

**Enum with methods:**
```
enum EnumName
    case caseName
    case caseName2

    function methodName() ReturnType
        // method body - use 'self' to access the enum value
    end 'methodName'
end 'EnumName'
```

### Memory Layout

**Simple enums:**
- Layout: `[tag: i8]`
- Tag values: 0, 1, 2, ... for each case in declaration order

**Raw value enums:**
- Layout: `[tag: i8][padding][rawValue: T]`
- Raw value type can be `int` or `string`
- Tag still used for fast comparison

**Associated value enums:**
- Layout: `[tag: i8][padding][payload: max_payload_size]`
- Payload size is the maximum of all case payloads
- Each case's associated values stored in the payload area

### Implementation

**Lexer:**
- Add `enum` keyword (Declaration category)
- `case` keyword already exists

**Parser:**
- `parseEnumDef()` in parser_decl.cpp
- Parse optional raw value type after enum name
- Parse case with optional `= value` for raw values
- Parse case with optional `(params)` for associated values
- Parse methods inside enum body (same as struct methods)

**AST Nodes:**
- `EnumCaseAST`: Individual case definition
  - `name`: Case name identifier
  - `rawValue`: Optional raw value expression
  - `associatedValues`: Vector of (name, type) pairs
  - `line`, `column`: Source location
- `EnumDefAST`: Enum type definition
  - `name`: Enum name
  - `rawValueType`: Optional raw value type ("int" or "string")
  - `cases`: Vector of `EnumCaseAST`
  - `methods`: Vector of `FunctionAST`
  - `isExported`: Export visibility

**Semantic Analysis:**
- Register enum types in type system
- Validate all case names are unique within enum
- For raw value enums: validate raw values are unique and match declared type
- For associated values: validate types exist
- Support `==` and `!=` comparison between same enum type
- Validate method signatures (first param must be `self EnumName`)

**MIR Code Generation:**
- Simple enum values: i8 constants
- Raw value enums: struct with tag + raw value
- Associated value enums: tagged union
- Comparison uses tag comparison
- `.rawValue` accessor for raw value enums
- Case construction with associated values

### Files to Modify

1. `maxon-bin/lexer/lexer_keyword_matcher.h` - Add `enum` keyword
2. `maxon-bin/ast.h` - Add AST nodes
3. `maxon-bin/parser/parser_decl.cpp` - Add `parseEnumDef()`
4. `maxon-bin/semantic_analyzer.h` - Add `EnumInfo` struct
5. `maxon-bin/semantic_analyzer.cpp` - Add enum analysis
6. `maxon-bin/codegen_mir/codegen_mir_enum.cpp` - New file for enum codegen
7. `maxon-bin/codegen_mir.h` - Add enum generation declarations
8. `docs/LANGUAGE_REFERENCE.md` - Document enums

## Documentation

# Enums

Enums define a type with a fixed set of named variants called cases. Maxon supports three kinds of enums: simple enums, raw value enums, and enums with associated values.

### Simple Enums

The simplest form of enum defines named cases with no additional data:

```maxon
enum Direction
    case north
    case south
    case east
    case west
end 'Direction'
```

Create enum values using dot notation:

```maxon
var dir = Direction.north
```

### Raw Value Enums

Enums can have an underlying raw value type (`int` or `string`):

```maxon
enum HttpStatus int
    case ok = 200
    case notFound = 404
    case serverError = 500
end 'HttpStatus'

enum Planet string
    case earth = "Earth"
    case mars = "Mars"
    case venus = "Venus"
end 'Planet'
```

Access the raw value with `.rawValue`:

```maxon
var status = HttpStatus.ok
var code = status.rawValue    // 200
```

### Associated Values

Cases can carry additional data called associated values:

```maxon
enum Result
    case success(value int)
    case failure(code int, message string)
    case pending
end 'Result'
```

Construct cases with associated values:

```maxon
var r1 = Result.success(42)
var r2 = Result.failure(404, "Not found")
var r3 = Result.pending
```

> **Note:** Pattern matching with `match` statements to extract associated values will be added in a future release.

### Comparing Enum Values

Enum values can be compared for equality using `==` and `!=`:

```maxon
if dir == Direction.north 'check'
    // handle north
end 'check'
```

For enums with associated values, `==` compares both the case and the associated values.

### Enum Methods

Enums can have methods, similar to structs:

```maxon
enum Direction
    case north
    case south
    case east
    case west

    function opposite() Direction
        if self == Direction.north 'n'
            return Direction.south
        end 'n'
        if self == Direction.south 's'
            return Direction.north
        end 's'
        if self == Direction.east 'e'
            return Direction.west
        end 'e'
        return Direction.east
    end 'opposite'

    function isVertical() bool
        if self == Direction.north 'check'
            return true
        end 'check'
        if self == Direction.south 'check2'
            return true
        end 'check2'
        return false
    end 'isVertical'
end 'Direction'
```

Call methods using instance-dot-method syntax:

```maxon
var dir = Direction.north
var opp = dir.opposite()    // Direction.south
var vert = dir.isVertical() // true
```

## Tests

<!-- test: simple-enum -->
```maxon
enum Direction
    case north
    case south
    case east
    case west
end 'Direction'

function main() int
    var dir = Direction.north
    if dir == Direction.north 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-assignment -->
```maxon
enum Color
    case red
    case green
    case blue
end 'Color'

function main() int
    var c = Color.red
    c = Color.blue
    if c == Color.blue 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-not-equal -->
```maxon
enum Status
    case pending
    case active
    case done
end 'Status'

function main() int
    var s = Status.pending
    if s != Status.active 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-comparison -->
```maxon
enum Status
    case pending
    case active
    case done
end 'Status'

function main() int
    var s1 = Status.pending
    var s2 = Status.pending
    var s3 = Status.active
    if s1 == s2 'eq'
        if s1 != s3 'neq'
            return 1
        end 'neq'
    end 'eq'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-function-param -->
```maxon
enum Status
    case on
    case off
end 'Status'

function isOn(s Status) bool
    if s == Status.on 'check'
        return true
    end 'check'
    return false
end 'isOn'

function main() int
    var status = Status.on
    if isOn(status) 'test'
        return 1
    end 'test'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-return-type -->
```maxon
enum Result
    case success
    case failure
end 'Result'

function getResult(succeed bool) Result
    if succeed 'check'
        return Result.success
    end 'check'
    return Result.failure
end 'getResult'

function main() int
    var r = getResult(true)
    if r == Result.success 'handle'
        return 1
    end 'handle'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: raw-value-int -->
```maxon
enum HttpStatus int
    case ok = 200
    case notFound = 404
    case serverError = 500
end 'HttpStatus'

function main() int
    var status = HttpStatus.ok
    if status.rawValue == 200 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: raw-value-int-comparison -->
```maxon
enum Priority int
    case low = 1
    case medium = 5
    case high = 10
end 'Priority'

function main() int
    var p = Priority.high
    if p.rawValue > 5 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```


<!-- test: associated-value-construction -->
```maxon
enum Container
    case empty
    case value(n int)
end 'Container'

function main() int
    var c = Container.value(42)
    var e = Container.empty
    // Construction works - verify by checking tags are different
    if c != e 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-method -->
```maxon
enum Direction
    case north
    case south

    function isNorth() bool
        if self == Direction.north 'check'
            return true
        end 'check'
        return false
    end 'isNorth'
end 'Direction'

function main() int
    let d = Direction.north
    if d.isNorth() 'test'
        return 1
    end 'test'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-method-returns-enum -->
```maxon
enum Toggle
    case on
    case off

    function flip() Toggle
        if self == Toggle.on 'check'
            return Toggle.off
        end 'check'
        return Toggle.on
    end 'flip'
end 'Toggle'

function main() int
    let t = Toggle.on
    let flipped = t.flip()
    if flipped == Toggle.off 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: error.duplicate-case -->
```maxon
enum Color
    case red
    case red
end 'Color'

function main() int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 4, column 10
Duplicate enum case 'red' in enum 'Color'

  4 |     case red
    |          ^
```

<!-- test: error.unknown-enum-case -->
```maxon
enum Color
    case red
    case blue
end 'Color'

function main() int
    let _c = Color.green
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 8, column 14
Unknown case 'green' for enum 'Color'
  Available cases: red, blue

  8 |     let _c = Color.green
    |              ^

Semantic Error: line 8, column 5
The variable '_c' is assigned but its value is never used

  8 |     let _c = Color.green
    |     ^
```

<!-- test: error.duplicate-raw-value -->
```maxon
enum Status int
    case ok = 200
    case success = 200
end 'Status'

function main() int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 4, column 10
Duplicate raw value 200 in enum 'Status'

  4 |     case success = 200
    |          ^
```

<!-- test: error.raw-value-type-mismatch -->
```maxon
enum Status int
    case ok = "success"
end 'Status'

function main() int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 3, column 15
Raw value type 'string' does not match enum raw value type 'int'

  3 |     case ok = "success"
    |               ^
```

<!-- test: error.rawvalue-on-simple-enum -->
```maxon
enum Color
    case red
    case blue
end 'Color'

function main() int
    var c = Color.red
    return c.rawValue
end 'main'
```
```maxoncstderr
Semantic Error: line 9, column 12
Cannot access 'rawValue' on enum 'Color' which has no raw value type
  Declare the enum with a raw value type: enum Color int

  9 |     return c.rawValue
    |            ^
```

<!-- test: error.associated-value-wrong-count -->
```maxon
enum Result
    case success(value int)
    case failure
end 'Result'

function main() int
    let _r = Result.success(1, 2)
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 8, column 14
Wrong number of associated values for case 'success': expected 1, got 2

  8 |     let _r = Result.success(1, 2)
    |              ^

Semantic Error: line 8, column 5
The variable '_r' is assigned but its value is never used

  8 |     let _r = Result.success(1, 2)
    |     ^
```

<!-- test: error.associated-value-type-mismatch -->
```maxon
enum Container
    case value(n int)
end 'Container'

function main() int
    let _c = Container.value("hello")
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 7, column 30
Type mismatch for associated value 'n': expected 'int', got 'string'

  7 |     let _c = Container.value("hello")
    |                              ^

Semantic Error: line 7, column 5
The variable '_c' is assigned but its value is never used

  7 |     let _c = Container.value("hello")
    |     ^
```
