---
feature: redundant-type-annotation
status: stable
keywords: [struct, literal, type, annotation, inference, redundant, closure, parameter]
category: type-system
---

# Redundant Type Annotation

## Documentation

Maxon can infer types from context. When a type can be inferred, an explicit type annotation is redundant and produces an error.

### When Type Inference Works

Type inference works in:
- Function return statements (when return type is a struct)
- Closure return values (when closure return type is known)
- Closure parameter types (when function signature specifies the type)
- Struct field initializers (when field type is known)

### Struct Literal Error Example

```text
function makePoint() returns Point
  return Point{x: 1, y: 2}  // Error E3015: 'Point' can be inferred
end 'makePoint'
```

### Closure Parameter Error Example

```text
m.map((p StringIntPair) gives {key: p.key, value: p.value * 10})
      // Error E3015: 'StringIntPair' can be inferred from m.map's signature
```

### Solution

Omit redundant type annotations when the type can be inferred:

```maxon
function makePoint() returns Point
  return {x: 1, y: 2}
end 'makePoint'
```
```exitcode
0
```

## Tests

<!-- test: error.exact-match-return -->
```maxon
type Point
  var x Integer
  var y Integer
end 'Point'

function makePoint() returns Point
  return Point{x: 1, y: 2}
end 'makePoint'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3015: specs/fragments/redundant-type-annotation/error.exact-match-return.test:8:10: redundant type annotation: 'Point'
```

<!-- test: error.closure-return -->
```maxon
type Point
  var x Integer
  var y Integer
end 'Point'

function transform(f (Integer) returns Point, n Integer) returns Point
  return f(n)
end 'transform'

function main() returns ExitCode
  var p = transform((x Integer) gives Point{x: x, y: x * 2}, n: 5)
  return p.x
end 'main'
```
```maxoncstderr
error E3015: specs/fragments/redundant-type-annotation/error.closure-return.test:12:39: redundant type annotation: 'Point'
```

<!-- test: ok.no-context -->
```maxon
type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var p = Point{x: 1, y: 2}
  return p.x
end 'main'
```
```exitcode
1
```

<!-- test: ok.anonymous-literal -->
```maxon
type Point
  export var x Integer
  export var y Integer
end 'Point'

function makePoint() returns Point
  return {x: 1, y: 2}
end 'makePoint'

function main() returns ExitCode
  var p = makePoint()
  return p.x + p.y
end 'main'
```
```exitcode
3
```

<!-- test: error.closure-param -->
```maxon
type Point
  export var x Integer
  export var y Integer
end 'Point'

function transform(f (Point) returns Integer) returns Integer
  return f(Point{x: 5, y: 10})
end 'transform'

function main() returns ExitCode
  return transform((p Point) gives p.x + p.y)
end 'main'
```
```maxoncstderr
error E3015: specs/fragments/redundant-type-annotation/error.closure-param.test:12:23: redundant type annotation: 'Point'
```

<!-- test: ok.closure-param-no-context -->
```maxon
type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var f = (p Point) gives p.x + p.y
  return f(Point{x: 3, y: 4})
end 'main'
```
```exitcode
7
```

<!-- test: ok.closure-param-inferred -->
```maxon
type Point
  export var x Integer
  export var y Integer
end 'Point'

function transform(f (Point) returns Integer) returns Integer
  return f(Point{x: 5, y: 10})
end 'transform'

function main() returns ExitCode
  return transform((p) gives p.x + p.y)
end 'main'
```
```exitcode
15
```

