---
feature: alloc-tracking
status: experimental
keywords: [memory, allocation, tracking, debug, heap]
category: debugging
---

## Developer Notes

Memory allocation tracking provides runtime visibility into heap allocations for debugging memory leaks and understanding allocation patterns. When enabled via `--track-allocs`:

1. Every `heap_alloc` prints: `ALLOC #N: X bytes`
2. Every `heap_free` prints: `FREE #N: X bytes`
3. At program exit, prints leak summary and statistics

### Implementation

- Global tracking state in .data section
- Tracking table holds up to 256 concurrent allocations
- Each entry: {ptr, size, id} = 24 bytes
- `_start` wrapper enables tracking, calls main, prints summary

### Compile Flag

`maxon compile --track-allocs file.maxon`

## Documentation

# Allocation Tracking

Debug memory issues by tracking heap allocations at runtime.

## Usage

Compile with the `--track-allocs` flag:

```text
maxon compile --track-allocs myprogram.maxon
```

## Output Format

Each allocation prints:
```text
ALLOC #1: 80 bytes (dynamic array)
```

Each free prints:
```text
FREE #1: 80 bytes (dynamic array)
```

At exit, a summary is printed:
```text
=== ALLOC STATS ===
Allocated: 80 bytes
Freed:     80 bytes
Leaked:    0 bytes
```

## Tests

<!-- test: dynamic-array-no-leak -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    let size = 10
    var arr = array of size int
    arr[0] = 42
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
42
```
```stdout
ALLOC #1: 80 bytes (dynamic array)
FREE #1: 80 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 80 bytes
Freed:     80 bytes
Leaked:    0 bytes
```

<!-- test: no-alloc-empty-program -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    return 42
end 'main'
```
```exitcode
42
```
```stdout

=== ALLOC STATS ===
Allocated: 0 bytes
Freed:     0 bytes
Leaked:    0 bytes
```

<!-- test: multiple-arrays -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    let size1 = 5
    let size2 = 10
    var arr1 = array of size1 int
    var arr2 = array of size2 int
    arr1[0] = 1
    arr2[0] = 2
    var sum = 0
    if let a = arr1[0] 'g1'
        sum = sum + a
    end 'g1'
    if let b = arr2[0] 'g2'
        sum = sum + b
    end 'g2'
    return sum
end 'main'
```
```exitcode
3
```
```stdout
ALLOC #1: 40 bytes (dynamic array)
ALLOC #2: 80 bytes (dynamic array)
FREE #1: 40 bytes (dynamic array)
FREE #2: 80 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 120 bytes
Freed:     120 bytes
Leaked:    0 bytes
```

<!-- test: array-early-return-true -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    let size = 5
    var arr = array of size int
    arr[0] = 42
    if true 'check'
        if let val = arr[0] 'get'
            return val
        end 'get' else 'nil'
            return 0
        end 'nil'
    end 'check'
    return 0
end 'main'
```
```exitcode
42
```
```stdout
ALLOC #1: 40 bytes (dynamic array)
FREE #1: 40 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 40 bytes
Freed:     40 bytes
Leaked:    0 bytes
```

<!-- test: array-early-return-false -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    let size = 5
    var arr = array of size int
    arr[0] = 42
    if false 'check'
        return 0
    end 'check'
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
42
```
```stdout
ALLOC #1: 40 bytes (dynamic array)
FREE #1: 40 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 40 bytes
Freed:     40 bytes
Leaked:    0 bytes
```

<!-- test: array-passed-to-function -->
<!-- TrackAllocs: true -->
```maxon
function sum_first(arr array of int) returns int
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'sum_first'

function main() returns int
    let size = 5
    var arr = array of size int
    arr[0] = 99
    return sum_first(arr)
end 'main'
```
```exitcode
99
```
```stdout
ALLOC #1: 40 bytes (dynamic array)
FREE #1: 40 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 40 bytes
Freed:     40 bytes
Leaked:    0 bytes
```

<!-- test: array-computed-size -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    let a = 2
    let b = 5
    var arr = array of (a * b) int
    arr[0] = 77
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
77
```
```stdout
ALLOC #1: 80 bytes (dynamic array)
FREE #1: 80 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 80 bytes
Freed:     80 bytes
Leaked:    0 bytes
```

<!-- test: array-borrow-no-leak -->
<!-- TrackAllocs: true -->
```maxon
function readFirst(arr array of int) returns int
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'readFirst'

function main() returns int
    let size = 5
    var arr = array of size int
    arr[0] = 42
    let result = readFirst(arr)
    var sum = 0
    if let val = arr[0] 'get'
        sum = val
    end 'get'
    return sum + result
end 'main'
```
```exitcode
84
```
```stdout
ALLOC #1: 40 bytes (dynamic array)
FREE #1: 40 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 40 bytes
Freed:     40 bytes
Leaked:    0 bytes
```

<!-- test: array-borrow-multiple-times -->
<!-- TrackAllocs: true -->
```maxon
function getElement(arr array of int, idx int) returns int
    if let val = arr[idx] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'getElement'

function main() returns int
    let size = 5
    var arr = array of size int
    arr[0] = 10
    arr[1] = 20
    arr[2] = 30
    let a = getElement(arr, 0)
    let b = getElement(arr, 1)
    let c = getElement(arr, 2)
    return a + b + c
end 'main'
```
```exitcode
60
```
```stdout
ALLOC #1: 40 bytes (dynamic array)
FREE #1: 40 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 40 bytes
Freed:     40 bytes
Leaked:    0 bytes
```

<!-- test: array-in-loop -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    var i = 0
    var sum = 0
    while i < 3 'loop'
        let size = 5
        var arr = array of size int
        arr[0] = i
        if let val = arr[0] 'get'
            sum = sum + val
        end 'get'
        i = i + 1
    end 'loop'
    return sum
end 'main'
```
```exitcode
3
```
```stdout
ALLOC #1: 40 bytes (dynamic array)
FREE #1: 40 bytes (dynamic array)
ALLOC #2: 40 bytes (dynamic array)
FREE #2: 40 bytes (dynamic array)
ALLOC #3: 40 bytes (dynamic array)
FREE #3: 40 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 120 bytes
Freed:     120 bytes
Leaked:    0 bytes
```

<!-- test: array-move-no-leak -->
<!-- TrackAllocs: true -->
```maxon
function mutateFirst(arr array of int) returns int
    arr[0] = 100
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'mutateFirst'

function main() returns int
    let size = 5
    var arr = array of size int
    arr[0] = 42
    return mutateFirst(arr)
end 'main'
```
```exitcode
100
```
```stdout
ALLOC #1: 40 bytes (dynamic array)
FREE #1: 40 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 40 bytes
Freed:     40 bytes
Leaked:    0 bytes
```

<!-- test: array-move-chain -->
<!-- TrackAllocs: true -->
```maxon
function addTen(arr array of int) returns int
    var val = 0
    if let v = arr[0] 'get'
        val = v
    end 'get'
    arr[0] = val + 10
    if let v2 = arr[0] 'get2'
        return v2
    end 'get2' else 'nil'
        return 0
    end 'nil'
end 'addTen'

function doubleAddTen(arr array of int) returns int
    var val = 0
    if let v = arr[0] 'get'
        val = v
    end 'get'
    arr[0] = val * 2
    return addTen(arr)
end 'doubleAddTen'

function main() returns int
    let size = 5
    var arr = array of size int
    arr[0] = 5
    return doubleAddTen(arr)
end 'main'
```
```exitcode
20
```
```stdout
ALLOC #1: 40 bytes (dynamic array)
FREE #1: 40 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 40 bytes
Freed:     40 bytes
Leaked:    0 bytes
```

<!-- test: two-arrays-one-moved-one-borrowed -->
<!-- TrackAllocs: true -->
```maxon
function readIt(arr array of int) returns int
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'readIt'

function writeIt(arr array of int) returns int
    arr[0] = 99
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'writeIt'

function main() returns int
    let size = 5
    var arr1 = array of size int
    var arr2 = array of size int
    arr1[0] = 10
    arr2[0] = 20
    let borrowed = readIt(arr1)
    let moved = writeIt(arr2)
    return borrowed + moved
end 'main'
```
```exitcode
109
```
```stdout
ALLOC #1: 40 bytes (dynamic array)
ALLOC #2: 40 bytes (dynamic array)
FREE #2: 40 bytes (dynamic array)
FREE #1: 40 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 80 bytes
Freed:     80 bytes
Leaked:    0 bytes
```

<!-- test: array-zero-size-no-alloc -->
<!-- TrackAllocs: true -->
Zero-size arrays do not allocate memory.

```maxon
function main() returns int
    let size = 0
    var arr = array of size int
    if size > 0 'check'
        arr[0] = 1
    end 'check'
    return 42
end 'main'
```
```exitcode
42
```
```stdout

=== ALLOC STATS ===
Allocated: 0 bytes
Freed:     0 bytes
Leaked:    0 bytes
```

<!-- test: heap-array-reassign -->
<!-- TrackAllocs: true -->
Reassigning a heap array frees the old memory and allocates new memory.

```maxon
function main() returns int
    let size = 5
    var arr = array of size int
    arr[0] = 10
    arr = array of size int
    arr[0] = 20
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
20
```
```stdout
ALLOC #1: 40 bytes (dynamic array)
FREE #1: 40 bytes (dynamic array)
ALLOC #2: 40 bytes (dynamic array)
FREE #2: 40 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 80 bytes
Freed:     80 bytes
Leaked:    0 bytes
```

