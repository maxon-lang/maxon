---
feature: match-union-typed-binding
status: stable
keywords: [match, union, associated-values, binding]
category: unions
---
# Match Union-Typed Binding

## Documentation

When a union case has an associated value whose type is itself a union, the match binding should correctly track the type so the bound variable can be used in subsequent expressions.

## Tests

<!-- test: match-union-typed-binding-pass-to-function -->
Bind a union-typed associated value in a match and pass it to a function.
```maxon
union Direction
    north
    south
    east
    west
end 'Direction'

union Move
    walk(dir Direction)
    stay
end 'Move'

function directionValue(d Direction) returns ExitCode
    match d 'check'
        north then return 1
        south then return 2
        east then return 3
        west then return 4
    end 'check'
end 'directionValue'

function main() returns ExitCode
    let m = Move.walk(Direction.east)
    match m 'act'
        walk(dir) then return directionValue(dir)
        stay then return 0
    end 'act'
end 'main'
```
```exitcode
3
```

<!-- test: match-union-typed-binding-compare -->
Bind a union-typed associated value in a match and compare it.
```maxon
union Color
    red
    green
    blue
end 'Color'

union Pixel
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
    var result = match c 'g'
        green gives 1
        red gives 0
        blue gives 0
    end 'g'
    return result
end 'checkColor'

function main() returns ExitCode
    let p = Pixel.colored(Color.green)
    return isGreen(p)
end 'main'
```
```exitcode
1
```
