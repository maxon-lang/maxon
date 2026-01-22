---
feature: if-try
status: experimental
keywords: [if, try, let, else, error, binding]
category: error-handling
---

# If-Try Expressions

## Documentation

### Conditional Error Handling

The `if try` construct provides conditional execution based on whether a throwing expression succeeds or fails.

#### Boolean Form

Check if an expression succeeds without binding the result:

```maxon
if try mayFail() 'check'
    print("Operation succeeded!")
end 'check'
```

The if-block executes only if the expression succeeds (doesn't throw).

#### Binding Form

Unwrap and bind the success value:

```maxon
if let value = try mayFail() 'check'
    print("Got: {value}")
end 'check'
```

If successful, the unwrapped value is bound to `value` and available within the if-block.

#### With Else Clause

Handle the error case:

```maxon
if try mayFail() 'check'
    print("Success!")
end 'check' else 'err'
    print("Failed!")
end 'err'
```

#### With Error Binding

Capture the error value in the else block:

```maxon
if let value = try mayFail() 'check'
    print("Got: {value}")
end 'check' else (e) 'err'
    print("Error occurred")
end 'err'
```

The error is bound to `e` and available within the else-block.

## Tests

<!-- test: if-try-boolean-success -->
```maxon
enum MyError is Error
    failed
end 'MyError'

function mayFail(succeed bool) returns int throws MyError
    if not succeed 'check'
        throw MyError.failed
    end 'check'
    return 42
end 'mayFail'

function main() returns int
    var result = 0
    if try mayFail(true) 'check'
        result = 1
    end 'check'
    return result
end 'main'
```
```exitcode
1
```

<!-- test: if-try-boolean-failure -->
```maxon
enum MyError is Error
    failed
end 'MyError'

function mayFail(succeed bool) returns int throws MyError
    if not succeed 'check'
        throw MyError.failed
    end 'check'
    return 42
end 'mayFail'

function main() returns int
    var result = 0
    if try mayFail(false) 'check'
        result = 1
    end 'check'
    return result
end 'main'
```
```exitcode
0
```

<!-- test: if-try-binding-success -->
```maxon
enum MyError is Error
    failed
end 'MyError'

function mayFail(succeed bool) returns int throws MyError
    if not succeed 'check'
        throw MyError.failed
    end 'check'
    return 42
end 'mayFail'

function main() returns int
    if let value = try mayFail(true) 'check'
        return value
    end 'check'
    return 0
end 'main'
```
```exitcode
42
```

<!-- test: if-try-binding-failure -->
```maxon
enum MyError is Error
    failed
end 'MyError'

function mayFail(succeed bool) returns int throws MyError
    if not succeed 'check'
        throw MyError.failed
    end 'check'
    return 42
end 'mayFail'

function main() returns int
    if let value = try mayFail(false) 'check'
        return value
    end 'check'
    return 99
end 'main'
```
```exitcode
99
```

<!-- test: if-try-else-block -->
```maxon
enum MyError is Error
    failed
end 'MyError'

function mayFail(succeed bool) returns int throws MyError
    if not succeed 'check'
        throw MyError.failed
    end 'check'
    return 42
end 'mayFail'

function main() returns int
    var result = 0
    if try mayFail(false) 'check'
        result = 1
    end 'check' else 'err'
        result = 2
    end 'err'
    return result
end 'main'
```
```exitcode
2
```

<!-- test: if-try-else-success -->
```maxon
enum MyError is Error
    failed
end 'MyError'

function mayFail(succeed bool) returns int throws MyError
    if not succeed 'check'
        throw MyError.failed
    end 'check'
    return 42
end 'mayFail'

function main() returns int
    var result = 0
    if try mayFail(true) 'check'
        result = 1
    end 'check' else 'err'
        result = 2
    end 'err'
    return result
end 'main'
```
```exitcode
1
```

<!-- test: if-try-binding-with-else -->
```maxon
enum MyError is Error
    failed
end 'MyError'

function mayFail(succeed bool) returns int throws MyError
    if not succeed 'check'
        throw MyError.failed
    end 'check'
    return 42
end 'mayFail'

function main() returns int
    if let value = try mayFail(false) 'check'
        return value
    end 'check' else 'err'
        return 77
    end 'err'
    return 0
end 'main'
```
```exitcode
77
```

<!-- test: if-try-binding-with-else-success -->
```maxon
enum MyError is Error
    failed
end 'MyError'

function mayFail(succeed bool) returns int throws MyError
    if not succeed 'check'
        throw MyError.failed
    end 'check'
    return 42
end 'mayFail'

function main() returns int
    if let value = try mayFail(true) 'check'
        return value
    end 'check' else 'err'
        return 77
    end 'err'
    return 0
end 'main'
```
```exitcode
42
```

<!-- test: if-try-else-with-error-binding -->
```maxon
enum MyError is Error
    first
    second
end 'MyError'

function mayFail(which int) returns int throws MyError
    if which == 1 'check1'
        throw MyError.first
    end 'check1'
    if which == 2 'check2'
        throw MyError.second
    end 'check2'
    return 42
end 'mayFail'

function main() returns int
    var result = 0
    if try mayFail(1) 'check'
        result = 100
    end 'check' else (e) 'err'
        result = 50
    end 'err'
    return result
end 'main'
```
```exitcode
50
```

<!-- test: if-try-nested -->
```maxon
enum MyError is Error
    failed
end 'MyError'

function mayFail(succeed bool) returns int throws MyError
    if not succeed 'check'
        throw MyError.failed
    end 'check'
    return 42
end 'mayFail'

function main() returns int
    var result = 0
    if try mayFail(true) 'outer'
        if try mayFail(true) 'inner'
            result = 3
        end 'inner'
    end 'outer'
    return result
end 'main'
```
```exitcode
3
```

<!-- test: if-try-in-loop -->
```maxon
enum MyError is Error
    failed
end 'MyError'

function mayFail(n int) returns int throws MyError
    if n < 3 'check'
        throw MyError.failed
    end 'check'
    return n
end 'mayFail'

function main() returns int
    var sum = 0
    var i = 0
    while i < 5 'loop'
        if let val = try mayFail(i) 'check'
            sum = sum + val
        end 'check'
        i = i + 1
    end 'loop'
    return sum
end 'main'
```
```exitcode
7
```

<!-- test: error.if-try-non-throwing -->
Using `if try` with a non-throwing function is a compile-time error.

```maxon
function noThrow() returns int
    return 42
end 'noThrow'

function main() returns int
    if try noThrow() 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```maxoncstderr
error E055: specs/fragments/if-try.error.if-try-non-throwing.1.test:7:5: try requires a throwing function: ''noThrow' does not throw'
```

<!-- test: if-try-binding-struct-multiple-managed-fields -->
<!-- TrackMemory: true -->
When using if-let with a struct that has multiple managed fields (like Array and String fields),
all managed fields must be properly cleaned up when the binding goes out of scope.

```maxon
enum MyError is Error
    failed
end 'MyError'

typealias IntArray is Array with int

type MultiManaged
    export var numbers IntArray
    export var text String
    export var tag String
end 'MultiManaged'

function mayFail(succeed bool) returns MultiManaged throws MyError
    if not succeed 'check'
        throw MyError.failed
    end 'check'
    var nums = IntArray{}
    nums.push(10)
    nums.push(20)
    return {numbers: nums, text: "hello", tag: "world"}
end 'mayFail'

function main() returns int
    var result = 0
    var i = 0
    while i < 3 'loop'
        if let item = try mayFail(true) 'check'
            result = result + (try item.numbers.get(0) otherwise 0)
        end 'check'
        i = i + 1
    end 'loop'
    return result
end 'main'
```
```exitcode
30
```
```stdout
ALLOC #1: 40 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: managed
MOVE: managed
MOVE: nums
DECREF: item -> rc=0
FREE #1: 40 bytes (array cleanup)
ALLOC #2: 40 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: managed
MOVE: managed
MOVE: nums
DECREF: item -> rc=0
FREE #2: 40 bytes (array cleanup)
ALLOC #3: 40 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: managed
MOVE: managed
MOVE: nums
DECREF: item -> rc=0
FREE #3: 40 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 120 bytes
Freed:     120 bytes
Leaked:    0 bytes
Moves:     9
Increfs:   3
Decrefs:   3
```

<!-- test: complex-nested-struct-cleanup -->
<!-- TrackMemory: true -->
Test cleanup of deeply nested structs with multiple managed fields at function return.

```maxon
enum MyError is Error
    failed
end 'MyError'

typealias IntArray is Array with int
typealias StringArray is Array with String

type Inner
    export var name String
    export var values IntArray
end 'Inner'

type Outer
    export var label String
    export var inner Inner
    export var tags StringArray
end 'Outer'

function createOuter() returns Outer
    var inner = Inner{name: "test", values: IntArray{}}
    inner.values.push(1)
    inner.values.push(2)
    var outer = Outer{label: "outer", inner: inner, tags: StringArray{}}
    outer.tags.push("tag1")
    outer.tags.push("tag2")
    return outer
end 'createOuter'

function main() returns int
    var outer = createOuter()
    return try outer.inner.values.get(0) otherwise 0
end 'main'
```
```exitcode
1
```
```stdout
MOVE: managed
ALLOC #1: 40 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: managed
MOVE: inner
MOVE: managed
ALLOC #2: 168 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: managed
MOVE: outer
DECREF: outer -> rc=0
FREE #1: 40 bytes (array cleanup)
DECREF: outer -> rc=0
FREE #2: 168 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 208 bytes
Freed:     208 bytes
Leaked:    0 bytes
Moves:     6
Increfs:   2
Decrefs:   2
```
