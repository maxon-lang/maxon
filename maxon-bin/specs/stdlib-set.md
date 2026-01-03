---
feature: stdlib-set
status: experimental
keywords: [stdlib, Set, generic, collection, hash, insert, contains, remove]
category: stdlib
---

# Stdlib Set Type

## Documentation

### Set Type

The `Set` type is a generic hash set that stores unique elements.

### Creating Sets

Create an empty set:
```text
var s = Set of int{}
```

Create a set from literals (requires type annotation):
```text
var s Set of int = [1, 2, 3]
```

### Adding Elements

Use `insert` to add elements:
```text
var s = Set of int{}
s.insert(10)
s.insert(20)
```

Duplicate insertions are ignored:
```text
s.insert(10)  // Already exists, no-op
```

### Checking Membership

Use `contains` to check if an element exists:
```text
if s.contains(10) 'check'
    // element is in set
end 'check'
```

### Removing Elements

Use `remove` to delete an element:
```text
var removed = s.remove(10)  // returns true if found
```

### Size

```text
var size = s.count()      // Number of elements
var cap = s.capacity()    // Current capacity
```

## Tests

<!-- test: empty-set -->
Create an empty set and verify it starts empty.

```maxon
function main() returns int
    var s = Set of int{}
    if s.count() != 0 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: insert-single -->
Insert a single element and check count.

```maxon
function main() returns int
    var s = Set of int{}
    s.insert(42)
    if s.count() != 1 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: insert-multiple -->
Insert multiple elements and verify count.

```maxon
function main() returns int
    var s = Set of int{}
    s.insert(10)
    s.insert(20)
    s.insert(30)
    if s.count() != 3 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: insert-duplicate -->
Inserting duplicate should not increase count.

```maxon
function main() returns int
    var s = Set of int{}
    s.insert(42)
    s.insert(42)
    s.insert(42)
    if s.count() != 1 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: contains-present -->
Check contains returns true for present element.

```maxon
function main() returns int
    var s = Set of int{}
    s.insert(42)
    if s.contains(42) == false 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: contains-absent -->
Check contains returns false for absent element.

```maxon
function main() returns int
    var s = Set of int{}
    s.insert(42)
    if s.contains(99) == true 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: remove-present -->
Remove an element and verify it's gone.

```maxon
function main() returns int
    var s = Set of int{}
    s.insert(42)
    var removed = s.remove(42)
    if removed == false 'check1'
        return 1
    end 'check1'
    if s.count() != 0 'check2'
        return 2
    end 'check2'
    if s.contains(42) == true 'check3'
        return 3
    end 'check3'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: remove-absent -->
Remove returns false for absent element.

```maxon
function main() returns int
    var s = Set of int{}
    s.insert(42)
    var removed = s.remove(99)
    if removed == true 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: set-from-literal -->
Initialize set from array literal.

```maxon
function main() returns int
    var s Set of int = [1, 2, 3]
    if s.count() != 3 'count'
        return 1
    end 'count'
    if s.contains(1) == false 'c1'
        return 2
    end 'c1'
    if s.contains(2) == false 'c2'
        return 3
    end 'c2'
    if s.contains(3) == false 'c3'
        return 4
    end 'c3'
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: grow-capacity -->
Verify set grows when exceeding load factor.

```maxon
function main() returns int
    var s = Set of int{}
    var initialCap = s.capacity()

    // Insert enough elements to trigger growth (75% of 16 = 12)
    var i = 0
    while i < 15 'insert'
        s.insert(i)
        i = i + 1
    end 'insert'

    if s.capacity() <= initialCap 'grew'
        return 1
    end 'grew'

    if s.count() != 15 'count'
        return 2
    end 'count'

    return 0
end 'main'
```
```exitcode
0
```
