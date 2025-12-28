---
feature: challenge-struct-field-assign
status: stable
keywords: struct, field, assignment, mutation
category: semantics
---

# Developer Notes

Tests for Challenge 6 from DEVELOPMENT_CHALLENGES.md: Struct Field Assignment.

Assigning to struct fields after creation.

# Documentation

## Struct Field Assignment

Struct fields can be modified after the struct is created.

## Tests

<!-- test: struct-field-reassignment -->
```maxon
type Counter
    var value int
end 'Counter'

function main() returns int
    var c = Counter{value: 10}
    c.value = 42
    return c.value
end 'main'
```
```exitcode
42
```

<!-- test: nested-struct-field-reassignment -->
```maxon
type Inner
    var x int
end 'Inner'

type Outer
    var inner Inner
end 'Outer'

function main() returns int
    var i = Inner{x: 10}
    var o = Outer{inner: i}
    o.inner.x = 42
    return o.inner.x
end 'main'
```
```exitcode
42
```
