---
feature: challenge-array-of-structs
status: stable
keywords: array, struct, elements, memory
category: semantics
---
# Challenge Array Of Structs

## Documentation

## Arrays of Structs

Arrays can contain struct values. Each element is a complete copy of the struct.

## Tests

<!-- test: array-of-structs-literal -->
```maxon
type Point
    export var x int
    export var y int
end 'Point'

function main() returns int
    var p1 = Point{x: 1, y: 2}
    var p2 = Point{x: 3, y: 4}
    var points = [p1, p2]
    var pt0 = try points.get(0) otherwise Point{x: 0, y: 0}
    var pt1 = try points.get(1) otherwise Point{x: 0, y: 0}
    return pt0.x + pt1.y
end 'main'
```
```exitcode
5
```

<!-- test: array-of-structs-indexed-access -->
```maxon
type Pair
    export var first int
    export var second int
end 'Pair'

function main() returns int
    var p = Pair{first: 10, second: 20}
    var arr = [p]
    var elem = try arr.get(0) otherwise Pair{first: 0, second: 0}
    return elem.first + elem.second
end 'main'
```
```exitcode
30
```
