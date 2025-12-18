---
feature: challenge-array-of-structs
status: draft
keywords: array, struct, elements, memory
category: semantics
---

# Developer Notes

Tests for Challenge 4 from DEVELOPMENT_CHALLENGES.md: Array with Struct Elements.

Arrays containing struct elements require proper copying of each struct.

# Documentation

## Arrays of Structs

Arrays can contain struct values. Each element is a complete copy of the struct.

## Tests

<!-- test: array-of-structs-literal -->
```maxon
type Point
    var x int
    var y int
end 'Point'

function main() returns int
    var p1 = Point{x: 1, y: 2}
    var p2 = Point{x: 3, y: 4}
    var points = [p1, p2]
    return points[0].x + points[1].y
end 'main'
```
```exitcode
5
```

<!-- test: array-of-structs-indexed-access -->
```maxon
type Pair
    var first int
    var second int
end 'Pair'

function main() returns int
    var p = Pair{first: 10, second: 20}
    var arr = [p]
    return arr[0].first + arr[0].second
end 'main'
```
```exitcode
30
```
