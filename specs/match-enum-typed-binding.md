---
feature: match-enum-typed-binding
status: stable
keywords: [match, enum, associated-values, binding]
category: enums
---
# Match Enum-Typed Binding

## Documentation

When an enum case has an associated value whose type is itself an enum, the match binding should correctly track the type so the bound variable can be used in subsequent expressions.

## Tests

<!-- test: match-enum-typed-binding-pass-to-function -->
Bind an enum-typed associated value in a match and pass it to a function.
```maxon
enum Direction
    north
    south
    east
    west
end 'Direction'

enum Move
    walk(dir Direction)
    stay
end 'Move'

function directionValue(d Direction) returns ExitCode
    match d 'check'
        Direction.north then return 1
        Direction.south then return 2
        Direction.east then return 3
        Direction.west then return 4
    end 'check'
end 'directionValue'

function main() returns ExitCode
    let m = Move.walk(Direction.east)
    match m 'act'
        walk(dir) then return directionValue(dir)
        stay then return 0
    end 'act'
    return 0
end 'main'
```
```exitcode
3
```

<!-- test: match-enum-typed-binding-compare -->
Bind an enum-typed associated value in a match and compare it.
```maxon
enum Color
    red
    green
    blue
end 'Color'

enum Pixel
    colored(c Color)
    transparent
end 'Pixel'

function isGreen(p Pixel) returns ExitCode
    match p 'check'
        colored(c) then return checkColor(c)
        transparent then return 0
    end 'check'
end 'isGreen'

function checkColor(c Color) returns ExitCode
    if c == Color.green 'g'
        return 1
    end 'g'
    return 0
end 'checkColor'

function main() returns ExitCode
    let p = Pixel.colored(Color.green)
    return isGreen(p)
end 'main'
```
```exitcode
1
```
