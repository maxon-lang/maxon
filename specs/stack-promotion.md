---
feature: stack-promotion
status: experimental
keywords: [stack, allocation, escape-analysis, optimization]
category: optimization
---

# Stack Promotion for Structs

Tests verifying that the escape analysis correctly promotes eligible structs to stack allocation.
Uses `MmTrace: true` to verify that promoted structs produce NO heap allocation trace lines,
while escaped structs still produce normal heap allocation traces.

## Tests

<!-- test: stack-local-primitive-struct -->
<!-- MmTrace -->
Simple struct with all-primitive fields, used only locally. Must produce NO mm_alloc for Point.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var p = Point{x: 10, y: 20}
  return p.x + p.y
end 'main'
```
```exitcode
30
```
```stderr
sl_init
  os_alloc size=67108864
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: stack-local-field-mutation -->
<!-- MmTrace -->
Stack-promoted struct with field mutation. No heap allocation.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var p = Point{x: 1, y: 2}
  p.x = 100
  return p.x
end 'main'
```
```exitcode
100
```
```stderr
sl_init
  os_alloc size=67108864
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: heap-when-aliased -->
<!-- MmTrace -->
Struct assigned to another variable must remain heap-allocated (aliasing creates shared reference).
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = a
  return b.x
end 'main'
```
```exitcode
1
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Point #1 size=16 [stack-promotion.main]
  sl_alloc Point #1 size=48 class=4
mm_incref Point #1 rc=1 [stack-promotion.main]
mm_incref Point #1 rc=2 [stack-promotion.main]
mm_decref Point #1 rc=1 [stack-promotion.main]
mm_decref Point #1 rc=0 [stack-promotion.main]
  mm_free Point #1
    sl_free Point #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: heap-when-passed-to-function -->
<!-- MmTrace -->
Struct passed to a function must remain heap-allocated (Phase 1).
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function readX(p Point) returns Integer
  return p.x
end 'readX'

function main() returns ExitCode
  var p = Point{x: 42, y: 0}
  return readX(p)
end 'main'
```
```exitcode
42
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Point #1 size=16 [stack-promotion.main]
  sl_alloc Point #1 size=48 class=4
mm_incref Point #1 rc=1 [stack-promotion.main]
mm_decref Point #1 rc=0 [stack-promotion.main]
  mm_free Point #1
    sl_free Point #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: heap-when-returned -->
<!-- MmTrace -->
Struct returned from function must remain heap-allocated.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function makePoint(x Integer, y Integer) returns Point
  return Point{x: x, y: y}
end 'makePoint'

function main() returns ExitCode
  var p = makePoint(x: 5, y: 10)
  return p.x
end 'main'
```
```exitcode
5
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Point #1 size=16 [stack-promotion.makePoint]
  sl_alloc Point #1 size=48 class=4
mm_incref Point #1 rc=1 [stack-promotion.makePoint]
mm_transfer Point #1 rc=1 [stack-promotion.makePoint]
mm_decref Point #1 rc=0 [stack-promotion.main]
  mm_free Point #1
    sl_free Point #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```
