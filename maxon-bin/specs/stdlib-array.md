---
feature: stdlib-array
status: stable
keywords: [stdlib, Array, generic, collection, push, pop, get, set, count, capacity, String, ownership]
category: stdlib
---

# Stdlib Array Type

## Documentation

### Array Type

The `Array` type is a generic, dynamically-sized collection that can hold elements of any type.

### Creating Arrays

Create an empty array:
```text
var arr = Array of int{}
```

Create an array from a literal:
```text
var arr = [1, 2, 3]
```

### Adding Elements

Use `push` to add elements to the end:
```text
var arr = Array of int{}
arr.push(10)
arr.push(20)
arr.push(30)
```

Use `insert` to add at a specific index:
```text
arr.insert(1, 15)  // Insert 15 at index 1
```

### Accessing Elements

Use `get` to safely access elements (returns optional):
```text
if let val = arr.get(0) 'found'
    // use val
end 'found'
```

Use `first` and `last` for convenience:
```text
if let first = arr.first() 'f'
    // first element
end 'f'

if let last = arr.last() 'l'
    // last element  
end 'l'
```

### Modifying Elements

Use `set` to modify an element:
```text
arr.set(0, 100)  // Set first element to 100
```

### Removing Elements

Use `pop` to remove and return the last element:
```text
if let val = arr.pop() 'popped'
    // val is the removed element
end 'popped'
```

Use `remove` to remove at a specific index:
```text
arr.remove(1)  // Remove element at index 1
```

Use `clear` to remove all elements:
```text
arr.clear()
```

### Size and Capacity

```text
var size = arr.count()       // Number of elements
var cap = arr.capacity()     // Current capacity
var empty = arr.isEmpty()    // true if count() == 0
arr.reserve(100)             // Ensure capacity >= 100
```

### Iteration

Use `for-in` to iterate over elements:
```text
for item in arr 'loop'
    // use item
end 'loop'
```

## Tests

<!-- test: empty-array -->
Create an empty array and verify it starts empty.

```maxon
function main() returns int
    var arr = Array of int{}
    if arr.count() != 0 'check'
        return 1
    end 'check'
    if arr.isEmpty() == false 'empty'
        return 2
    end 'empty'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: push-single -->
Push a single element and retrieve it.

```maxon
function main() returns int
    var arr = Array of int{}
    arr.push(42)
    if let val = arr.get(0) 'get'
        if val != 42 'check'
            return 1
        end 'check'
        return 0
    end 'get' else 'nil'
        return 2
    end 'nil'
end 'main'
```
```exitcode
0
```

<!-- test: push-multiple -->
Push multiple elements and verify count.

```maxon
function main() returns int
    var arr = Array of int{}
    arr.push(10)
    arr.push(20)
    arr.push(30)
    if arr.count() != 3 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: get-all-elements -->
Push multiple elements and get each one.

```maxon
function main() returns int
    var arr = Array of int{}
    arr.push(10)
    arr.push(20)
    arr.push(30)
    
    if let v0 = arr.get(0) 'g0'
        if v0 != 10 'c0'
            return 1
        end 'c0'
    end 'g0' else 'e0'
        return 10
    end 'e0'
    
    if let v1 = arr.get(1) 'g1'
        if v1 != 20 'c1'
            return 2
        end 'c1'
    end 'g1' else 'e1'
        return 11
    end 'e1'
    
    if let v2 = arr.get(2) 'g2'
        if v2 != 30 'c2'
            return 3
        end 'c2'
    end 'g2' else 'e2'
        return 12
    end 'e2'
    
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: set-element -->
Set an element and verify the change.

```maxon
function main() returns int
    var arr = Array of int{}
    arr.push(10)
    arr.push(20)
    arr.push(30)
    
    arr.set(1, 99)
    
    if let val = arr.get(1) 'get'
        if val != 99 'check'
            return 1
        end 'check'
        return 0
    end 'get' else 'nil'
        return 2
    end 'nil'
end 'main'
```
```exitcode
0
```

<!-- test: first-last -->
Test first() and last() methods.

```maxon
function main() returns int
    var arr = Array of int{}
    arr.push(10)
    arr.push(20)
    arr.push(30)
    
    if let f = arr.first() 'first'
        if f != 10 'fc'
            return 1
        end 'fc'
    end 'first' else 'fe'
        return 2
    end 'fe'
    
    if let l = arr.last() 'last'
        if l != 30 'lc'
            return 3
        end 'lc'
    end 'last' else 'le'
        return 4
    end 'le'
    
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: pop-element -->
Pop elements and verify they are removed.

```maxon
function main() returns int
    var arr = Array of int{}
    arr.push(10)
    arr.push(20)
    arr.push(30)
    
    if let val = arr.pop() 'pop'
        if val != 30 'check'
            return 1
        end 'check'
    end 'pop' else 'nil'
        return 2
    end 'nil'
    
    if arr.count() != 2 'cnt'
        return 3
    end 'cnt'
    
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: array-literal -->
Initialize from array literal.

```maxon
function main() returns int
    var arr = [1, 2, 3]
    
    if arr.count() != 3 'cnt'
        return 1
    end 'cnt'
    
    if let v = arr.get(1) 'get'
        if v != 2 'check'
            return 2
        end 'check'
        return 0
    end 'get' else 'nil'
        return 3
    end 'nil'
end 'main'
```
```exitcode
0
```

<!-- test: for-in-iteration -->
Iterate over array using for-in loop.

```maxon
function main() returns int
    var arr = Array of int{}
    arr.push(10)
    arr.push(20)
    arr.push(30)
    
    var sum = 0
    for item in arr 'loop'
        sum = sum + item
    end 'loop'
    
    if sum != 60 'check'
        return 1
    end 'check'
    
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: isEmpty-transitions -->
Verify isEmpty changes with push/pop.

```maxon
function main() returns int
    var arr = Array of int{}
    
    if arr.isEmpty() == false 'e1'
        return 1
    end 'e1'
    
    arr.push(42)
    
    if arr.isEmpty() == true 'e2'
        return 2
    end 'e2'
    
    if let _ = arr.pop() 'pop'
    end 'pop'
    
    if arr.isEmpty() == false 'e3'
        return 3
    end 'e3'
    
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: clear-array -->
Clear an array and verify it's empty.

```maxon
function main() returns int
    var arr = Array of int{}
    arr.push(10)
    arr.push(20)
    arr.push(30)
    
    arr.clear()
    
    if arr.count() != 0 'cnt'
        return 1
    end 'cnt'
    
    if arr.isEmpty() == false 'empty'
        return 2
    end 'empty'
    
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: get-out-of-bounds -->
Get returns nil for out of bounds index.

```maxon
function main() returns int
    var arr = Array of int{}
    arr.push(10)
    
    if let _ = arr.get(5) 'get'
        return 1
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
0
```

<!-- test: reserve-capacity -->
Reserve capacity for efficiency.

```maxon
function main() returns int
    var arr = Array of int{}
    arr.reserve(100)

    if arr.capacity() < 100 'cap'
        return 1
    end 'cap'

    if arr.count() != 0 'cnt'
        return 2
    end 'cnt'

    return 0
end 'main'
```
```exitcode
0
```

<!-- test: push-string-literals -->
Push string literals into an array and retrieve them.

```maxon
function main() returns int
    var arr = Array of String{}
    arr.push("hello")
    arr.push("world")

    if arr.count() != 2 'cnt'
        return 1
    end 'cnt'

    print(arr[0])
    print(arr[1])
    return 0
end 'main'
```
```output
helloworld
```
```exitcode
0
```

<!-- test: push-string-literals-long -->
Push longer string literals (heap-allocated) into an array.

```maxon
function main() returns int
    var arr = Array of String{}
    arr.push("hello this is a longer string")
    arr.push("world this is also a longer string")

    if arr.count() != 2 'cnt'
        return 1
    end 'cnt'

    print(arr[0])
    print(arr[1])
    return 0
end 'main'
```
```output
hello this is a longer stringworld this is also a longer string
```
```exitcode
0
```

<!-- test: push-string-variables -->
Push string variables into an array.

```maxon
function main() returns int
    var arr = Array of String{}
    var s1 = "first"
    var s2 = "second"
    arr.push(s1)
    arr.push(s2)

    print(arr[0])
    print(arr[1])
    return 0
end 'main'
```
```output
firstsecond
```
```exitcode
0
```

<!-- test: string-array-iteration -->
Iterate over an array of strings.

```maxon
function main() returns int
    var arr = Array of String{}
    arr.push("a")
    arr.push("b")
    arr.push("c")

    for item in arr 'loop'
        print(item)
    end 'loop'
    return 0
end 'main'
```
```output
abc
```
```exitcode
0
```

<!-- test: string-array-get -->
Get strings from array using get method.

```maxon
function main() returns int
    var arr = Array of String{}
    arr.push("one")
    arr.push("two")
    arr.push("three")

    if let val = arr.get(1) 'get'
        print(val)
    end 'get' else 'nil'
        return 1
    end 'nil'
    return 0
end 'main'
```
```output
two
```
```exitcode
0
```

<!-- test: string-array-memory -->
<!-- TrackMemory: true -->
Verify string array memory is properly managed (no leaks).

```maxon
function main() returns int
    var arr = Array of String{}
    arr.push("test string one")
    arr.push("test string two")
    arr.push("test string three")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 16 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #2: 128 bytes (array grow)
ALLOC #3: 16 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
ALLOC #4: 18 bytes (string buffer)
MOVE: managed
INCREF: <struct copy> -> rc=1
FREE #1: 16 bytes (string cleanup)
FREE #3: 16 bytes (string cleanup)
FREE #4: 18 bytes (string cleanup)
FREE #2: 128 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 178 bytes
Freed:     178 bytes
Leaked:    0 bytes
Moves:     3
Increfs:   3
Decrefs:   0
```
