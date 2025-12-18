---
feature: challenge-array-deep-copy
status: draft
keywords: array, struct, deep-copy, memory
category: semantics
---

# Developer Notes

Tests for Challenge 1 from DEVELOPMENT_CHALLENGES.md: Array Deep Copy in Struct Literals.

When an array variable is assigned to a struct field, the entire array contents must be copied, not just the array header/pointer. This prevents dangling pointers when the original array goes out of scope.

# Documentation

## Array Deep Copy in Structs

When you assign an array to a struct field, Maxon performs a deep copy of the array contents. This ensures the struct owns its own copy of the data.

## Tests

<!-- test: struct-with-array-field-simple -->
```maxon
type Container
    var data array of int
end 'Container'

function main() returns int
    var arr = [10, 20, 30]
    var c = Container{data: arr}
    return c.data[0]
end 'main'
```
```exitcode
10
```

<!-- test: struct-array-field-independent -->
```maxon
type Container
    var data array of int
end 'Container'

function main() returns int
    var arr = [10, 20, 30]
    var c = Container{data: arr}
    arr[0] = 999
    return c.data[0]
end 'main'
```
```exitcode
10
```

<!-- test: struct-array-field-modify-struct -->
```maxon
type Container
    var data array of int
end 'Container'

function main() returns int
    var arr = [10, 20, 30]
    var c = Container{data: arr}
    c.data[0] = 999
    return arr[0]
end 'main'
```
```exitcode
10
```

<!-- test: struct-array-returned-from-function -->
```maxon
type Container
    var data array of int
end 'Container'

function makeContainer() returns Container
    var arr = [42, 84, 126]
    return Container{data: arr}
end 'makeContainer'

function main() returns int
    var c = makeContainer()
    return c.data[0]
end 'main'
```
```exitcode
42
```

<!-- test: struct-array-returned-all-elements -->
```maxon
type Container
    var data array of int
end 'Container'

function makeContainer() returns Container
    var arr = [1, 2, 3, 4, 5]
    return Container{data: arr}
end 'makeContainer'

function main() returns int
    var c = makeContainer()
    return c.data[0] + c.data[1] + c.data[2] + c.data[3] + c.data[4]
end 'main'
```
```exitcode
15
```

<!-- test: nested-function-calls-with-struct-array -->
```maxon
type Container
    var data array of int
end 'Container'

function processContainer(c Container) returns int
    return c.data[0] + c.data[1]
end 'processContainer'

function makeAndProcess() returns int
    var arr = [10, 20, 30]
    var c = Container{data: arr}
    return processContainer(c)
end 'makeAndProcess'

function main() returns int
    return makeAndProcess()
end 'main'
```
```exitcode
30
```
