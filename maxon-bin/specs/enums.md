---
feature: enums
status: experimental
keywords: [enum, enumeration, associated values, raw values]
category: type-system
---

# Enums

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

Enums can have raw values. The backing type is inferred from the values:

```maxon
enum HttpStatus
    ok = 200
    notFound = 404
    serverError = 500
end 'HttpStatus'

enum Planet
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
enum HttpStatus
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
enum Priority
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
error E030: specs/fragments/enums.error.duplicate-case.1.test:4:5: duplicate enum case: 'red'
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
error E034: specs/fragments/enums.error.unknown-enum-case.1.test:8:5: unknown enum case: 'green'
```

<!-- test: error.duplicate-raw-value -->
```maxon
enum Status
    ok = 200
    success = 200
end 'Status'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
error E031: specs/fragments/enums.error.duplicate-raw-value.1.test:4:5: duplicate raw value: '200'
```

<!-- test: error.raw-value-type-mismatch -->
```maxon
enum Status
    ok = 100
    fail = "error"
end 'Status'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
error E032: specs/fragments/enums.error.raw-value-type-mismatch.1.test:4:5: raw value type mismatch: 'expected int, got String'
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
error E033: specs/fragments/enums.error.rawvalue-on-simple-enum.1.test:9:5: rawValue requires raw value enum: 'Color'
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
error E011: specs/fragments/enums.error.associated-value-wrong-count.1.test:8:5: wrong argument count: 'success'
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
error E022: specs/fragments/enums.error.associated-value-type-mismatch.1.test:7:5: type mismatch: 'n'
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
    var r = Result.failure(99)
    match r 'handle'
        success(v) then return v
        failure(c) then return c
    end 'handle'
end 'main'
```
```exitcode
99
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
error E035: specs/fragments/enums.error.match-enum-wrong-binding-count.1.test:8:5: wrong binding count: 'value'
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
error E034: specs/fragments/enums.error.match-enum-unknown-case.1.test:9:5: unknown enum case: 'unknown'
```
