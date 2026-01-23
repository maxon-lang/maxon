---
feature: challenge-struct-field-assign
status: stable
keywords: struct, field, assignment, mutation
category: semantics
---
# Challenge Struct Field Assign

## Documentation

## Struct Field Assignment

Struct fields can be modified after the struct is created.

## Tests

<!-- test: struct-field-reassignment -->
```maxon
type Counter
    export var value int
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

<!-- test: immutable-field-assign-error -->
Assigning to an immutable (`let`) field should be a compile-time error.

```maxon
type Config
    export let id int
    export var count int
end 'Config'

function main() returns int
    var c = Config{id: 1, count: 0}
    c.id = 2
    return c.id
end 'main'
```
```maxoncstderr
error E009: specs/fragments/challenge-struct-field-assign.immutable-field-assign-error.1.test:9:5: cannot assign to immutable variable: 'id'
```

<!-- test: nested-struct-field-reassignment -->
```maxon
type Inner
    export var x int
end 'Inner'

type Outer
    export var inner Inner
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
