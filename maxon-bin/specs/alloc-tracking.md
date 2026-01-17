---
feature: alloc-tracking
status: stable
keywords: [memory, allocation, tracking, debug, heap]
category: debugging
---
# Allocation Tracking

## Documentation

Debug memory issues by tracking heap allocations at runtime.

## Usage

Compile with the `--track-allocs` flag:

```text
maxon compile --track-allocs myprogram.maxon
```

## Output Format

Each allocation prints:
```text
ALLOC #1: 80 bytes (array grow)
```

Each free prints:
```text
FREE #1: 80 bytes (array cleanup)
```

At exit, a summary is printed:
```text
=== MEMORY STATS ===
Allocated: 80 bytes
Freed:     80 bytes
Leaked:    0 bytes
```

## Tests

<!-- test: dynamic-array-no-leak -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function main() returns int
    let size = 10
    var arr = IntArray{}
    arr.resize(size)
    arr.set(0, value: 42)
    return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```
```stdout
ALLOC #1: 88 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #1: 88 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 88 bytes
Freed:     88 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   1
Decrefs:   1
```

<!-- test: no-alloc-empty-program -->
<!-- TrackMemory: true -->
```maxon
function main() returns int
    return 42
end 'main'
```
```exitcode
42
```
```stdout

=== MEMORY STATS ===
Allocated: 0 bytes
Freed:     0 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   0
Decrefs:   0
```

<!-- test: multiple-arrays -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function main() returns int
    let size1 = 5
    let size2 = 10
    var arr1 = IntArray{}
    arr1.resize(size1)
    var arr2 = IntArray{}
    arr2.resize(size2)
    arr1.set(0, value: 1)
    arr2.set(0, value: 2)
    var a = try arr1.get(0) otherwise 0
    var b = try arr2.get(0) otherwise 0
    return a + b
end 'main'
```
```exitcode
3
```
```stdout
ALLOC #1: 48 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #2: 88 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr1 -> rc=0
FREE #1: 48 bytes (array cleanup)
DECREF: arr2 -> rc=0
FREE #2: 88 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 136 bytes
Freed:     136 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   2
Decrefs:   2
```

<!-- test: array-early-return-true -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function main() returns int
    let size = 5
    var arr = IntArray{}
    arr.resize(size)
    arr.set(0, value: 42)
    if true 'check'
        return try arr.get(0) otherwise 0
    end 'check'
    return 0
end 'main'
```
```exitcode
42
```
```stdout
ALLOC #1: 48 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #1: 48 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 48 bytes
Freed:     48 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   1
Decrefs:   1
```

<!-- test: array-early-return-false -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function main() returns int
    let size = 5
    var arr = IntArray{}
    arr.resize(size)
    arr.set(0, value: 42)
    if false 'check'
        return 0
    end 'check'
    return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```
```stdout
ALLOC #1: 48 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #1: 48 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 48 bytes
Freed:     48 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   1
Decrefs:   1
```

<!-- test: array-passed-to-function -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function sum_first(arr IntArray) returns int
    return try arr.get(0) otherwise 0
end 'sum_first'

function main() returns int
    let size = 5
    var arr = IntArray{}
    arr.resize(size)
    arr.set(0, value: 99)
    return sum_first(arr)
end 'main'
```
```exitcode
99
```
```stdout
ALLOC #1: 48 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #1: 48 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 48 bytes
Freed:     48 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   1
Decrefs:   1
```

<!-- test: array-computed-size -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function main() returns int
    let a = 2
    let b = 5
    var arr = IntArray{}
    arr.resize(a * b)
    arr.set(0, value: 77)
    return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
77
```
```stdout
ALLOC #1: 88 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #1: 88 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 88 bytes
Freed:     88 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   1
Decrefs:   1
```

<!-- test: array-borrow-no-leak -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function readFirst(arr IntArray) returns int
    return try arr.get(0) otherwise 0
end 'readFirst'

function main() returns int
    let size = 5
    var arr = IntArray{}
    arr.resize(size)
    arr.set(0, value: 42)
    let result = readFirst(arr)
    var sum = try arr.get(0) otherwise 0
    return sum + result
end 'main'
```
```exitcode
84
```
```stdout
ALLOC #1: 48 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #1: 48 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 48 bytes
Freed:     48 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   1
Decrefs:   1
```

<!-- test: array-borrow-multiple-times -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function getElement(arr IntArray, idx int) returns int
    return try arr.get(idx) otherwise 0
end 'getElement'

function main() returns int
    let size = 5
    var arr = IntArray{}
    arr.resize(size)
    arr.set(0, value: 10)
    arr.set(1, value: 20)
    arr.set(2, value: 30)
    let a = getElement(arr, idx: 0)
    let b = getElement(arr, idx: 1)
    let c = getElement(arr, idx: 2)
    return a + b + c
end 'main'
```
```exitcode
60
```
```stdout
ALLOC #1: 48 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #1: 48 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 48 bytes
Freed:     48 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   1
Decrefs:   1
```

<!-- test: array-in-loop -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function main() returns int
    var i = 0
    var sum = 0
    while i < 3 'loop'
        let size = 5
        var arr = IntArray{}
        arr.resize(size)
        arr.set(0, value: i)
        var val = try arr.get(0) otherwise 0
        sum = sum + val
        i = i + 1
    end 'loop'
    return sum
end 'main'
```
```exitcode
3
```
```stdout
ALLOC #1: 48 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #1: 48 bytes (array cleanup)
ALLOC #2: 48 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #2: 48 bytes (array cleanup)
ALLOC #3: 48 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #3: 48 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 144 bytes
Freed:     144 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   3
Decrefs:   3
```

<!-- test: array-move-no-leak -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function mutateFirst(arr IntArray) returns int
    arr.set(0, value: 100)
    return try arr.get(0) otherwise 0
end 'mutateFirst'

function main() returns int
    let size = 5
    var arr = IntArray{}
    arr.resize(size)
    arr.set(0, value: 42)
    return mutateFirst(arr)
end 'main'
```
```exitcode
100
```
```stdout
ALLOC #1: 48 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: arr
DECREF: arr -> rc=0
FREE #1: 48 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 48 bytes
Freed:     48 bytes
Leaked:    0 bytes
Moves:     1
Increfs:   1
Decrefs:   1
```

<!-- test: array-move-chain -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function addTen(arr IntArray) returns int
    var val = try arr.get(0) otherwise 0
    arr.set(0, value: val + 10)
    return try arr.get(0) otherwise 0
end 'addTen'

function doubleAddTen(arr IntArray) returns int
    var val = try arr.get(0) otherwise 0
    arr.set(0, value: val * 2)
    return addTen(arr)
end 'doubleAddTen'

function main() returns int
    let size = 5
    var arr = IntArray{}
    arr.resize(size)
    arr.set(0, value: 5)
    return doubleAddTen(arr)
end 'main'
```
```exitcode
20
```
```stdout
ALLOC #1: 48 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: arr
MOVE: arr
DECREF: arr -> rc=0
FREE #1: 48 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 48 bytes
Freed:     48 bytes
Leaked:    0 bytes
Moves:     2
Increfs:   1
Decrefs:   1
```

<!-- test: two-arrays-one-moved-one-borrowed -->
<!-- TrackMemory: true -->
```maxon
typealias IntArray is Array with int

function readIt(arr IntArray) returns int
    return try arr.get(0) otherwise 0
end 'readIt'

function writeIt(arr IntArray) returns int
    arr.set(0, value: 99)
    return try arr.get(0) otherwise 0
end 'writeIt'

function main() returns int
    let size = 5
    var arr1 = IntArray{}
    arr1.resize(size)
    var arr2 = IntArray{}
    arr2.resize(size)
    arr1.set(0, value: 10)
    arr2.set(0, value: 20)
    let borrowed = readIt(arr1)
    let moved = writeIt(arr2)
    return borrowed + moved
end 'main'
```
```exitcode
109
```
```stdout
ALLOC #1: 48 bytes (array grow)
INCREF: array grow -> rc=1
ALLOC #2: 48 bytes (array grow)
INCREF: array grow -> rc=1
MOVE: arr2
DECREF: arr -> rc=0
FREE #2: 48 bytes (array cleanup)
DECREF: arr1 -> rc=0
FREE #1: 48 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 96 bytes
Freed:     96 bytes
Leaked:    0 bytes
Moves:     1
Increfs:   2
Decrefs:   2
```

<!-- test: array-zero-size-no-alloc -->
<!-- TrackMemory: true -->
Zero-size arrays don't allocate memory.

```maxon
typealias IntArray is Array with int

function main() returns int
    let size = 0
    var arr = IntArray{}
    arr.resize(size)
    if size > 0 'check'
        arr.set(0, value: 1)
    end 'check'
    return 42
end 'main'
```
```exitcode
42
```
```stdout

=== MEMORY STATS ===
Allocated: 0 bytes
Freed:     0 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   0
Decrefs:   0
```

<!-- test: heap-array-reassign -->
<!-- TrackMemory: true -->
Reassigning a heap array frees the old memory and allocates new memory.

```maxon
typealias IntArray is Array with int

function main() returns int
    let size = 5
    var arr = IntArray{}
    arr.resize(size)
    arr.set(0, value: 10)
    arr = IntArray{}
    arr.resize(size)
    arr.set(0, value: 20)
    return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
20
```
```stdout
ALLOC #1: 48 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #1: 48 bytes (array cleanup)
ALLOC #2: 48 bytes (array grow)
INCREF: array grow -> rc=1
DECREF: arr -> rc=0
FREE #2: 48 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 96 bytes
Freed:     96 bytes
Leaked:    0 bytes
Moves:     0
Increfs:   2
Decrefs:   2
