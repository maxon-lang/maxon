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
    var data Array of int
end 'Container'

function main() returns int
    var arr = [10, 20, 30]
    var c = Container{data: arr}
    if let val = c.data[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
10
```

<!-- test: struct-array-field-independent -->
```maxon
type Container
    var data Array of int
end 'Container'

function main() returns int
    var arr = [10, 20, 30]
    var c = Container{data: arr}
    arr[0] = 999
    if let val = c.data[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
10
```

<!-- test: struct-array-field-modify-struct -->
```maxon
type Container
    var data Array of int
end 'Container'

function main() returns int
    var arr = [10, 20, 30]
    var c = Container{data: arr}
    c.data[0] = 999
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
10
```

<!-- test: struct-array-returned-from-function -->
```maxon
type Container
    var data Array of int
end 'Container'

function makeContainer() returns Container
    var arr = [42, 84, 126]
    return Container{data: arr}
end 'makeContainer'

function main() returns int
    var c = makeContainer()
    if let val = c.data[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
42
```

<!-- test: struct-array-returned-all-elements -->
```maxon
type Container
    var data Array of int
end 'Container'

function makeContainer() returns Container
    var arr = [1, 2, 3, 4, 5]
    return Container{data: arr}
end 'makeContainer'

function main() returns int
    var c = makeContainer()
    var sum = 0
    if let v0 = c.data[0] 'g0'
        sum = sum + v0
    end 'g0'
    if let v1 = c.data[1] 'g1'
        sum = sum + v1
    end 'g1'
    if let v2 = c.data[2] 'g2'
        sum = sum + v2
    end 'g2'
    if let v3 = c.data[3] 'g3'
        sum = sum + v3
    end 'g3'
    if let v4 = c.data[4] 'g4'
        sum = sum + v4
    end 'g4'
    return sum
end 'main'
```
```exitcode
15
```

<!-- test: nested-function-calls-with-struct-array -->
```maxon
type Container
    var data Array of int
end 'Container'

function processContainer(c Container) returns int
    var sum = 0
    if let v0 = c.data[0] 'g0'
        sum = sum + v0
    end 'g0'
    if let v1 = c.data[1] 'g1'
        sum = sum + v1
    end 'g1'
    return sum
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
