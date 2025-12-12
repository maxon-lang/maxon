---
feature: array-type-element-member-access
status: stable
keywords: array, type, member, access, indexing, field
category: expressions
---

## Developer Notes

This feature enables accessing type fields on array elements: `arr[i].field`.

**Problem:**
When arrays are declared with `var arr = array of N StructType`, the `variableTypes` map stores the type as `array<StructType>` (via `makeArrayStructType`). However, the codegen for member access on array elements was only checking for `_ManagedArray<T>` format (via `isArrayType`), not the `array<T>` format.

**Root Cause:**
In `codegen_mir_expr.cpp`, when handling `arr[i].field`:
```cpp
std::string arrayType = variableTypes[arrayIndexExpr->arrayName];
if (maxon::TypeConversion::isArrayType(arrayType)) {  // Only checks _ManagedArray<T>
    varType = maxon::TypeConversion::getArrayElementType(arrayType);
}
```

This fails for `array<Planet>` because `isArrayType` only matches `_ManagedArray<T>` or `_StaticArray<N, T>`.

**Fix:**
Also check `isArrayStructType` and use `getArrayStructElementType`:
```cpp
if (maxon::TypeConversion::isArrayType(arrayType)) {
    varType = maxon::TypeConversion::getArrayElementType(arrayType);
} else if (maxon::TypeConversion::isArrayStructType(arrayType)) {
    varType = maxon::TypeConversion::getArrayStructElementType(arrayType);
}
```

**Type Formats:**
- `array<T>` - used by `variableTypes` for local array variables (via `makeArrayStructType`)
- `_ManagedArray<T>` - internal MIR representation for managed arrays
- `_StaticArray<N, T>` - internal MIR representation for static arrays

## Documentation

# Type Field Access on Array Elements

You can access type fields directly on array elements using `arr[i].field` syntax.

## Basic Usage

```maxon
type Point
    var x int
    var y int
end 'Point'

function main() returns int
    var points = array of 3 Point
    points[0] = Point{x: 10, y: 20}
    return points[0].x  // Access field on array element
end 'main'
```

## In Loops

This is commonly used when iterating over arrays of types:

```maxon
type Body
    var x float
    var vx float
    var mass float
end 'Body'

function sumMomentum(bodies array of Body, n int) returns float
    var px = 0.0
    for i in range(0, n) 'loop'
        px = px + bodies[i].vx * bodies[i].mass
    end 'loop'
    return px
end 'sumMomentum'
```

## Tests

<!-- test: basic-field-access -->
```maxon
type Point
    var x int
    var y int
end 'Point'

function main() returns int
    var points = array of 3 Point
    points[0] = Point{x: 10, y: 20}
    points[1] = Point{x: 30, y: 40}
    return points[0].x + points[1].y
end 'main'
```
```exitcode
50
```

<!-- test: field-access-in-loop -->
```maxon
type Item
    var value int
end 'Item'

function sumValues(items array of Item, n int) returns int
    var total = 0
    for i in range(0, n) 'loop'
        total = total + items[i].value
    end 'loop'
    return total
end 'sumValues'

function main() returns int
    var arr = array of 3 Item
    arr[0] = Item{value: 10}
    arr[1] = Item{value: 20}
    arr[2] = Item{value: 30}
    return sumValues(arr, 3)
end 'main'
```
```exitcode
60
```

<!-- test: field-access-with-float -->
```maxon
type Body
    var x float
    var vx float
    var mass float
end 'Body'

function momentum(bodies array of Body, n int) returns float
    var px = 0.0
    for i in range(0, n) 'loop'
        px = px + bodies[i].vx * bodies[i].mass
    end 'loop'
    return px
end 'momentum'

function main() returns int
    var bodies = array of 2 Body
    bodies[0] = Body{x: 0.0, vx: 1.5, mass: 2.0}
    bodies[1] = Body{x: 0.0, vx: 3.0, mass: 4.0}
    let p = momentum(bodies, 2)
    // 1.5 * 2.0 + 3.0 * 4.0 = 3.0 + 12.0 = 15.0
    return trunc(p)
end 'main'
```
```exitcode
15
```

<!-- test: modify-field-through-index -->
```maxon
type Counter
    var count int
end 'Counter'

function main() returns int
    var counters = array of 2 Counter
    counters[0] = Counter{count: 0}
    counters[1] = Counter{count: 100}
    counters[0].count = 50
    return counters[0].count + counters[1].count
end 'main'
```
```exitcode
150
```
