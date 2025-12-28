---
feature: challenge-array-of-structs
status: stable
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
    var sum = 0
    if let pt0 = points[0] 'g0'
        sum = sum + pt0.x
    end 'g0'
    if let pt1 = points[1] 'g1'
        sum = sum + pt1.y
    end 'g1'
    return sum
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
    if let elem = arr[0] 'get'
        return elem.first + elem.second
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
30
```
