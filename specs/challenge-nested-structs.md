---
feature: challenge-nested-structs
status: stable
keywords: struct, nested, type-size, memory
category: semantics
---
# Challenge Nested Structs

## Documentation

## Nested Structs

Structs can contain other structs as fields. The compiler must correctly compute sizes and offsets for nested struct access.

## Tests

<!-- test: nested-struct-simple -->
```maxon

typealias Integer = i64

type Inner
  export var x Integer
  export var y Integer
end 'Inner'

type Outer
  export var inner Inner
  export var z Integer
end 'Outer'

function main() returns ExitCode
  var inner = Inner{x: 10, y: 20}
  var outer = Outer{inner: inner, z: 30}
  return outer.inner.x + outer.inner.y + outer.z
end 'main'
```
```exitcode
60
```

<!-- test: nested-struct-returned -->
```maxon

typealias Integer = i64

type Inner
  export var value Integer
end 'Inner'

type Outer
  export var inner Inner
end 'Outer'

function makeOuter() returns Outer
  var i = Inner{value: 42}
  return {inner: i}
end 'makeOuter'

function main() returns ExitCode
  var o = makeOuter()
  return o.inner.value
end 'main'
```
```exitcode
42
```

<!-- test: deeply-nested-struct -->
```maxon

typealias Integer = i64

type Level1
  export var value Integer
end 'Level1'

type Level2
  export var inner Level1
end 'Level2'

type Level3
  export var inner Level2
end 'Level3'

function main() returns ExitCode
  var l1 = Level1{value: 42}
  var l2 = Level2{inner: l1}
  var l3 = Level3{inner: l2}
  return l3.inner.inner.value
end 'main'
```
```exitcode
42
```

<!-- test: struct-with-multiple-nested-fields -->
```maxon

typealias Integer = i64

type Point
  export var x Integer
  export var y Integer
end 'Point'

type Line
  export var start Point
  export var finish Point
end 'Line'

function main() returns ExitCode
  var p1 = Point{x: 1, y: 2}
  var p2 = Point{x: 10, y: 20}
  var line = Line{start: p1, finish: p2}
  return line.start.x + line.start.y + line.finish.x + line.finish.y
end 'main'
```
```exitcode
33
```
