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

All enums support `.rawValue` access. Simple enums return their ordinal (0, 1, 2...), while backed enums return their explicit value.

#### Integer-backed Enums

```maxon
enum HttpStatus
    ok = 200
    notFound = 404
    serverError = 500
end 'HttpStatus'

var status = HttpStatus.ok
var code = status.rawValue    // 200
```

#### String-backed Enums

Cases can have explicit string values, or use the case name as the value:

```maxon
// Explicit string values
enum Planet
    earth = "Earth"
    mars = "Mars"
end 'Planet'

// Implicit string values - case name IS the raw value
enum Direction
    "North"
    "South"
end 'Direction'

var dir = Direction.North  // Access without quotes
```

#### Character-backed Enums

```maxon
// Explicit character values
enum CardSuit
    Hearts = 'H'
    Diamonds = 'D'
end 'CardSuit'

// Implicit character values
enum Compass
    'N'
    'S'
    'E'
    'W'
end 'Compass'

var c = Compass.N  // Access without quotes
```

#### Float-backed Enums

```maxon
enum Weights
    light = 1.5
    medium = 2.5
    heavy = 3.5
end 'Weights'

var w = Weights.medium
if w.rawValue > 2.0 'check'
    // weight is above 2.0
end 'check'
```

#### Simple Enum rawValue

Simple enums (no explicit values) also support `.rawValue`, returning their ordinal:

```maxon
enum Color
    red     // rawValue = 0
    green   // rawValue = 1
    blue    // rawValue = 2
end 'Color'

var c = Color.green
var ordinal = c.rawValue  // 1
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
error E011: specs/fragments/enums.error.associated-value-wrong-count.1.test:8:21: wrong argument count: 'expected 1, got 2'
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
error E022: specs/fragments/enums.error.associated-value-type-mismatch.1.test:7:24: type mismatch: 'expected int, got String'
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

<!-- test: simple-enum-rawvalue -->
```maxon
enum Direction
    north
    south
    east
end 'Direction'

function main() returns int
    var d = Direction.south
    return d.rawValue
end 'main'
```
```exitcode
1
```

<!-- test: implicit-string-backed -->
```maxon
enum StringBacked
    "North"
    "South"
    "East"
end 'StringBacked'

function main() returns int
    var dir = StringBacked.North
    if dir == StringBacked.South 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
1
```

<!-- test: implicit-char-backed -->
```maxon
enum CharBacked
    'N'
    'S'
    'E'
end 'CharBacked'

function main() returns int
    var dir = CharBacked.N
    if dir == CharBacked.S 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
1
```

<!-- test: explicit-char-backed -->
```maxon
enum Direction
    North = 'n'
    South = 's'
    East = 'e'
end 'Direction'

function main() returns int
    var d = Direction.North
    if d == Direction.South 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
1
```

<!-- test: float-backed -->
```maxon
enum FloatBacked
    North = 1.1
    South = 2.2
    East = 3.3
end 'FloatBacked'

function main() returns int
    var f = FloatBacked.North
    if f == FloatBacked.South 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
1
```

<!-- test: float-backed-rawvalue -->
```maxon
enum Weights
    light = 1.5
    medium = 2.5
    heavy = 3.5
end 'Weights'

function main() returns int
    var w = Weights.medium
    var rawVal = w.rawValue
    if rawVal > 2.0 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: int-backed-rawvalue -->
```maxon
enum HttpStatus
    ok = 200
    notFound = 404
    serverError = 500
end 'HttpStatus'

function main() returns int
    var status = HttpStatus.notFound
    var code = status.rawValue
    if code == 404 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: explicit-string-backed-rawvalue -->
```maxon
enum Planet
    earth = "Earth"
    mars = "Mars"
end 'Planet'

function main() returns int
    var p = Planet.mars
    var name = p.rawValue
    if name == "Mars" 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: implicit-string-backed-rawvalue -->
```maxon
enum Direction
    "North"
    "South"
    "East"
end 'Direction'

function main() returns int
    var d = Direction.South
    var name = d.rawValue
    if name == "South" 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: explicit-char-backed-rawvalue -->
```maxon
enum CardSuit
    Hearts = 'H'
    Diamonds = 'D'
    Spades = 'S'
end 'CardSuit'

function main() returns int
    var suit = CardSuit.Diamonds
    var ch = suit.rawValue
    if ch == 'D' 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: implicit-char-backed-rawvalue -->
```maxon
enum Compass
    'N'
    'S'
    'E'
    'W'
end 'Compass'

function main() returns int
    var c = Compass.E
    var ch = c.rawValue
    if ch == 'E' 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-rawvalue-dynamic-comparison -->
```maxon
enum Planet
    earth = "Earth"
    mars = "Mars"
end 'Planet'

function getName() returns String
    return "Mars"
end 'getName'

function main() returns int
    var p = Planet.mars
    var name = p.rawValue
    var expected = getName()
    if name == expected 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-rawvalue-after-reassign -->
```maxon
enum Planet
    earth = "Earth"
    mars = "Mars"
end 'Planet'

function main() returns int
    var p = Planet.earth
    p = Planet.mars
    var name = p.rawValue
    if name == "Mars" 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: float-rawvalue-in-function -->
```maxon
enum Weights
    light = 1.5
    medium = 2.5
end 'Weights'

function getRaw(w Weights) returns float
    return w.rawValue
end 'getRaw'

function main() returns int
    var w = Weights.medium
    var raw = getRaw(w)
    if raw > 2.0 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: int-rawvalue-in-function -->
```maxon
enum HttpStatus
    ok = 200
    notFound = 404
end 'HttpStatus'

function getCode(s HttpStatus) returns int
    return s.rawValue
end 'getCode'

function main() returns int
    var status = HttpStatus.notFound
    var code = getCode(status)
    if code == 404 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-rawvalue-function-param -->
```maxon
enum Planet
    earth = "Earth"
    mars = "Mars"
    venus = "Venus"
end 'Planet'

function getName(p Planet) returns String
    return p.rawValue
end 'getName'

function main() returns int
    var planet = Planet.mars
    var name = getName(planet)
    if name == "Mars" 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: char-rawvalue-function-param -->
```maxon
enum Grade
    excellent = 'A'
    good = 'B'
    average = 'C'
end 'Grade'

function getLetter(g Grade) returns Character
    return g.rawValue
end 'getLetter'

function main() returns int
    var grade = Grade.good
    var letter = getLetter(grade)
    if letter == 'B' 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: error.mixed-backing-types -->
```maxon
enum Mixed
    first = 1
    second = "two"
end 'Mixed'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
error E032: specs/fragments/enums.error.mixed-backing-types.1.test:4:5: raw value type mismatch: 'expected int, got String'
```
