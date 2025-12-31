---
feature: enums
status: experimental
keywords: [enum, enumeration, associated values, raw values]
category: type-system
---

# Enums

## Developer Notes

Enums are sum types that define a type with a fixed set of named variants (cases). Maxon enums support three forms: simple enums, raw value enums, and enums with associated values.

### Syntax

**Simple enum:**
```
enum EnumName
    caseName
    caseName2
end 'EnumName'
```

**Raw value enum:**
```
enum EnumName int
    caseName = 1
    caseName2 = 2
end 'EnumName'
```

**Enum with associated values:**
```
enum EnumName
    caseName(fieldName Type)
    caseName2(field1 Type1, field2 Type2)
    caseName3
end 'EnumName'
```

**Enum with methods:**
```
enum EnumName
    caseName
    caseName2

    function methodName() returns ReturnType
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

**Parser:**
- `parseEnumDef()` in parser_decl.cpp
- Parse optional raw value type after enum name
- Parse enum case with optional `= value` for raw values
- Parse enum case with optional `(params)` for associated values
- Parse methods inside enum body (same as type methods)

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
- Raw value enums: type with tag + raw value
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
    north
    south
    east
    west
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
    ok = 200
    notFound = 404
    serverError = 500
end 'HttpStatus'

enum Planet String
    earth = "Earth"
    mars = "Mars"
    venus = "Venus"
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
    success(value int)
    failure(code int, message String)
    pending
end 'Result'
```

Contype cases with associated values:

```maxon
var r1 = Result.success(42)
var r2 = Result.failure(404, "Not found")
var r3 = Result.pending
```

### Pattern Matching with Value Extraction

Use `match` statements to extract associated values from enum cases:

```maxon
match result 'handle'
    success(value) then return value
    failure(code, msg) then print(msg)
    pending then print("waiting...")
end 'handle'
```

Each binding name becomes a local variable within the case body, with the type inferred from the enum case definition.

Match expressions also support value extraction using `gives`:

```maxon
var extracted = match container 'get'
    empty gives 0
    value(n) gives n
end 'get'
```

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
    north
    south
    east
    west

    function opposite() returns Direction
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

    function isVertical() returns bool
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
    north
    south
    east
    west
end 'Direction'

function main() returns int
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
    red
    green
    blue
end 'Color'

function main() returns int
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
    pending
    active
    done
end 'Status'

function main() returns int
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
    pending
    active
    done
end 'Status'

function main() returns int
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
    on
    off
end 'Status'

function isOn(s Status) returns bool
    if s == Status.on 'check'
        return true
    end 'check'
    return false
end 'isOn'

function main() returns int
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
    success
    failure
end 'Result'

function getResult(succeed bool) returns Result
    if succeed 'check'
        return Result.success
    end 'check'
    return Result.failure
end 'getResult'

function main() returns int
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
    ok = 200
    notFound = 404
    serverError = 500
end 'HttpStatus'

function main() returns int
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
    low = 1
    medium = 5
    high = 10
end 'Priority'

function main() returns int
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
    empty
    value(n int)
end 'Container'

function main() returns int
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
    north
    south

    function isNorth() returns bool
        if self == Direction.north 'check'
            return true
        end 'check'
        return false
    end 'isNorth'
end 'Direction'

function main() returns int
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
    on
    off

    function flip() returns Toggle
        if self == Toggle.on 'check'
            return Toggle.off
        end 'check'
        return Toggle.on
    end 'flip'
end 'Toggle'

function main() returns int
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
    red
    red
end 'Color'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:4:5
Duplicate enum case 'red' in enum 'Color'

  4 |     red
    |     ^
```

<!-- test: error.unknown-enum-case -->
```maxon
enum Color
    red
    blue
end 'Color'

function main() returns int
    let _c = Color.green
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:8:14
Unknown case 'green' for enum 'Color'
  Available cases: red, blue

  8 |     let _c = Color.green
    |              ^

Semantic Error: temp_fragment.maxon:8:5
The variable '_c' is assigned but its value is never used

  8 |     let _c = Color.green
    |     ^
```

<!-- test: error.duplicate-raw-value -->
```maxon
enum Status int
    ok = 200
    success = 200
end 'Status'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:4:5
Duplicate raw value 200 in enum 'Status'

  4 |     success = 200
    |     ^
```

<!-- test: error.raw-value-type-mismatch -->
```maxon
enum Status int
    ok = "success"
end 'Status'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:3:10
Raw value type 'string' does not match enum raw value type 'int'

  3 |     ok = "success"
    |          ^
```

<!-- test: error.rawvalue-on-simple-enum -->
```maxon
enum Color
    red
    blue
end 'Color'

function main() returns int
    var c = Color.red
    return c.rawValue
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:9:12
Cannot access 'rawValue' on enum 'Color' which has no raw value type
  Declare the enum with a raw value type: enum Color int

  9 |     return c.rawValue
    |            ^
```

<!-- test: error.associated-value-wrong-count -->
```maxon
enum Result
    success(value int)
    failure
end 'Result'

function main() returns int
    let _r = Result.success(1, 2)
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:8:14
Wrong number of associated values for case 'success': expected 1, got 2

  8 |     let _r = Result.success(1, 2)
    |              ^

Semantic Error: temp_fragment.maxon:8:5
The variable '_r' is assigned but its value is never used

  8 |     let _r = Result.success(1, 2)
    |     ^
```

<!-- test: error.associated-value-type-mismatch -->
```maxon
enum Container
    value(n int)
end 'Container'

function main() returns int
    let _c = Container.value("hello")
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:7:30
Type mismatch for associated value 'n': expected 'int', got 'string'

  7 |     let _c = Container.value("hello")
    |                              ^

Semantic Error: temp_fragment.maxon:7:5
The variable '_c' is assigned but its value is never used

  7 |     let _c = Container.value("hello")
    |     ^
```

<!-- test: match-enum-binding-simple -->
```maxon
enum Container
    empty
    value(n int)
end 'Container'

function main() returns int
    var c = Container.value(42)
    match c 'extract'
        empty then return 0
        value(n) then return n
    end 'extract'
end 'main'
```
```exitcode
42
```

<!-- test: match-enum-binding-multiple -->
```maxon
enum Result
    success(value int)
    failure(code int)
end 'Result'

function main() returns int
    var r = Result.failure(404)
    match r 'handle'
        success(v) then return v
        failure(c) then return c
    end 'handle'
end 'main'
```
```exitcode
404
```

<!-- test: match-expr-enum-binding -->
```maxon
enum Container
    empty
    value(n int)
end 'Container'

function main() returns int
    var c = Container.value(10)
    var result = match c 'get'
        empty gives 0
        value(n) gives n * 2
    end 'get'
    return result
end 'main'
```
```exitcode
20
```

<!-- test: match-enum-no-binding -->
```maxon
enum Container
    empty
    value(n int)
end 'Container'

function main() returns int
    var c = Container.empty
    match c 'check'
        empty then return 1
        value(n) then return n
    end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: error.match-enum-wrong-binding-count -->
```maxon
enum Container
    value(n int)
end 'Container'

function main() returns int
    var c = Container.value(42)
    match c 'extract'
        value(a, b) then return a
    end 'extract'
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:9:9
Wrong number of bindings for case 'value': expected 1, got 2

  9 |         value(a, b) then return a
    |         ^
```

<!-- test: error.match-enum-unknown-case -->
```maxon
enum Container
    empty
    value(n int)
end 'Container'

function main() returns int
    var c = Container.value(42)
    match c 'extract'
        unknown(x) then return x
    end 'extract'
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:10:9
Unknown case 'unknown' for enum 'Container'

  10 |         unknown(x) then return x
     |         ^

Semantic Error: temp_fragment.maxon:9:5
Match on enum 'Container' is not exhaustive
  Missing cases: empty, value

  9 |     match c 'extract'
    |     ^

Semantic Error: temp_fragment.maxon:7:1
Function 'main' must return a value of type 'int'
  Note: All execution paths through the function must end with a return statement

  7 | function main() returns int
    | ^
```
