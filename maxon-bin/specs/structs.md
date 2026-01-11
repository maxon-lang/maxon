---
feature: types
status: stable
keywords: [type, field, let, var, mutability, instance]
category: type-system
---

# Types

## Documentation

Types define custom data types with named fields.

### Declaration

```maxon
type Point
    export var x int
    export var y int
end 'Point'
```

Fields must use `let` (immutable) or `var` (mutable), and can be `export` for external access:
```maxon
type Config
    export let version int    // Cannot be changed after initialization, accessible externally
    export var count int      // Can be modified, accessible externally
    var internal int          // Private - only accessible in methods
end 'Config'
```

### Instantiation

Create type instances with literal syntax:
```maxon
var p = Point{x: 10, y: 20}
let config = Config{version: 1, count: 0}
```

### Instance Mutability

The mutability of a type instance is determined by `let` vs `var`:

**var type** - Can modify `var` fields:
```maxon
var p = Point{x: 10, y: 20}
p.x = 30   // OK: type is mutable, field is var
```

**let type** - Cannot modify any fields:
```maxon
let p = Point{x: 10, y: 20}
// p.x = 30   // ERROR: type instance is immutable
```

### Field Mutability

Even on a `var` type, `let` fields cannot be modified:
```maxon
var c = Config{version: 1, count: 0}
c.count = 5     // OK: field is var
// c.version = 2   // ERROR: field is let
```

## Tests

<!-- test: var-struct-field-assign -->
```maxon
type Point
    export var x int
    export var y int
end 'Point'

function main() returns int
    var p = Point{x: 10, y: 20}
    p.x = 30
    return p.x
end 'main'
```
```exitcode
30
```

<!-- test: var-field-assign -->
```maxon
type Config
    export let version int
    export var count int
end 'Config'

function main() returns int
    var c = Config{version: 1, count: 0}
    c.count = 5
    return c.count
end 'main'
```
```exitcode
5
```

<!-- test: error.let-struct-field-assign -->
```maxon
type Point
    export var x int
    export var y int
end 'Point'

function main() returns int
    let p = Point{x: 10, y: 20}
    p.x = 30
    return p.x
end 'main'
```
```maxoncstderr
error E009: specs/fragments/structs.error.let-struct-field-assign.1.test:9:5: cannot assign to immutable variable: 'p'
```

<!-- test: error.let-field-assign -->
```maxon
type Config
    export let version int
    export var count int
end 'Config'

function main() returns int
    var c = Config{version: 1, count: 0}
    c.version = 2
    return c.version
end 'main'
```
```maxoncstderr
error E009: specs/fragments/structs.error.let-field-assign.1.test:9:5: cannot assign to immutable variable: 'version'
```

<!-- test: simple-type -->
```maxon
type Point
    export var x int
    export var y int
end 'Point'

function main() returns int
    var p = Point { x: 3, y: 4 }
    return p.x + p.y
end 'main'
```
```exitcode
7
```

<!-- test: struct-field-access -->
```maxon
type Rect
    export var width int
    export var height int
end 'Rect'

function main() returns int
    var r = Rect { width: 5, height: 10 }
    return r.width * r.height
end 'main'
```
```exitcode
50
```

<!-- test: struct-param -->
```maxon
type Vec2
    export var x int
    export var y int
end 'Vec2'

function dot(a Vec2, b Vec2) returns int
    return a.x * b.x + a.y * b.y
end 'dot'

function main() returns int
    var v1 = Vec2 { x: 3, y: 4 }
    var v2 = Vec2 { x: 2, y: 1 }
    return dot(v1, b: v2)
end 'main'
```
```exitcode
10
```

<!-- test: struct-return -->
```maxon
type Pair
    export var first int
    export var second int
end 'Pair'

function makePair(a int, b int) returns Pair
    return Pair { first: a, second: b }
end 'makePair'

function main() returns int
    var p = makePair(5, b: 7)
    return p.first + p.second
end 'main'
```
```exitcode
12
```

<!-- test: struct-literal-as-arg -->
```maxon
type Point
    export var x int
    export var y int
end 'Point'

function acceptPoint(p Point) returns int
    return p.x + p.y
end 'acceptPoint'

function main() returns int
    return acceptPoint(Point{x: 3, y: 4})
end 'main'
```
```exitcode
7
```

<!-- test: struct-field-default -->
```maxon
type Counter
	export var value int = 0
	export var step int = 1
end 'Counter'

function main() returns int
	var c1 = Counter{}
	var c2 = Counter{value: 40}
	var c3 = Counter{value: 10, step: 2}
	return c1.value + c2.value + c3.step
end 'main'
```
```exitcode
42
```

<!-- test: struct-field-inferred-type -->
```maxon
type Settings
	export let maxRetries = 5
	export var timeout = 50.0
end 'Settings'

function main() returns int
	var s = Settings{}
	return s.maxRetries + trunc(s.timeout)
end 'main'
```
```exitcode
55
```

