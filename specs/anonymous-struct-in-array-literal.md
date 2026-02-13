---
feature: anonymous-struct-in-array-literal
status: stable
keywords: [array, struct, literal, anonymous, inference]
category: type-system
---

# Anonymous Struct Literals in Array Literals

## Documentation

When the expected element type of an array literal is known, anonymous struct literals can be used inside the array:

```text
// Mixed: first element names the type, subsequent elements infer it
var points = [Point{x: 1, y: 2}, { x: 3, y: 4 }]

// In function call arguments where parameter type is an Array with struct elements
processItems([{ value: 10 }, { value: 20 }])
```

The struct type is inferred from context: when the first element is a named struct literal, subsequent `{...}` elements use the same type. When passed as a function argument where the parameter type is an Array with struct elements, all `{...}` elements infer the element type.

## Tests

<!-- test: anon-struct-array-mixed -->
```maxon
type Vec2
  export var x int
  export var y int
end 'Vec2'

function main() returns int
  var vecs = [Vec2{x: 1, y: 2}, { x: 3, y: 4 }]
  var v0 = try vecs.get(0) otherwise Vec2{x: 0, y: 0}
  var v1 = try vecs.get(1) otherwise Vec2{x: 0, y: 0}
  return v0.x + v1.y
end 'main'
```
```exitcode
5
```

<!-- test: anon-struct-array-mixed-three -->
```maxon
type Pair
  export var a int
  export var b int
end 'Pair'

function main() returns int
  var pairs = [Pair{a: 10, b: 20}, { a: 30, b: 40 }, { a: 50, b: 60 }]
  var p0 = try pairs.get(0) otherwise Pair{a: 0, b: 0}
  var p1 = try pairs.get(1) otherwise Pair{a: 0, b: 0}
  var p2 = try pairs.get(2) otherwise Pair{a: 0, b: 0}
  return p0.a + p1.b + p2.a
end 'main'
```
```exitcode
100
```

<!-- test: anon-struct-array-return -->
```maxon
type Pair
  export var first int
  export var second int
end 'Pair'

typealias PairArray = Array with Pair

function makePairs() returns PairArray
  return [{ first: 10, second: 20 }, { first: 30, second: 40 }]
end 'makePairs'

function main() returns int
  var pairs = makePairs()
  var p0 = try pairs.get(0) otherwise Pair{first: 0, second: 0}
  var p1 = try pairs.get(1) otherwise Pair{first: 0, second: 0}
  return p0.first + p1.second
end 'main'
```
```exitcode
50
```

<!-- test: anon-struct-array-func-arg -->
```maxon
type Item
  export var value int
end 'Item'

typealias ItemArray = Array with Item

function sumItems(items ItemArray) returns int
  var total = 0
  for item in items 'loop'
    total = total + item.value
  end 'loop'
  return total
end 'sumItems'

function main() returns int
  return sumItems([{ value: 10 }, { value: 20 }, { value: 12 }])
end 'main'
```
```exitcode
42
```
