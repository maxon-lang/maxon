---
feature: ownership
status: stable
keywords: ownership, borrow, move
category: semantics
---
# Ownership

## Documentation

## Ownership Model

Every value in Maxon has a single owner. When you pass a value to a function:

- **Borrow**: If the function only reads the parameter, you retain ownership and can continue using the variable
- **Move**: If the function modifies the parameter, ownership transfers to the function and you can no longer use the variable

This is determined automatically by analyzing what each function does with its parameters.

### Example

```maxon
function main() returns int
    var a = 42
    var b = foo(a)  // foo only reads a -> borrow, a still usable
    a = a + 1       // OK, we still own a
    bar(a)          // bar modifies its param -> ownership transfers
    // a = a + 1    // ERROR: a was moved to bar
    return b
end 'main'

function foo(z int) returns int
    return z + 4    // z is only read -> borrow
end 'foo'

function bar(z int)
    z = z + 1       // z is modified -> takes ownership
end 'bar'
```

## Tests

<!-- test: reassignment -->
Variables can be reassigned as long as they haven't been moved.

```maxon
function main() returns int
    var a = 10
    a = 20
    a = 30
    return a
end 'main'
```
```exitcode
30
```

<!-- test: multiple-assignments -->
Multiple assignments in sequence work correctly.

```maxon
function main() returns int
    var a = 1
    var b = 2
    a = a + b
    b = a + b
    return b
end 'main'
```
```exitcode
5
```

<!-- test: assign-expression -->
Assignment with complex expression on the right side.

```maxon
function main() returns int
    var a = 5
    var b = 3
    a = a * b + 2
    return a
end 'main'
```
```exitcode
17
```

<!-- test: reassign-to-self -->
Reassigning a variable to itself plus something.

```maxon
function main() returns int
    var x = 10
    x = x + 5
    x = x + 5
    return x
end 'main'
```
```exitcode
20
```

<!-- test: chain-assignments -->
Chain of assignments between variables.

```maxon
function main() returns int
    var a = 1
    var b = 2
    var c = 3
    a = b
    b = c
    c = a + b + c
    return c
end 'main'
```
```exitcode
8
```

<!-- test: var-after-let -->
Mutable variable can be reassigned after immutable declaration.

```maxon
function main() returns int
    let x = 10
    var y = x
    y = y + 5
    return y
end 'main'
```
```exitcode
15
```

<!-- test: multiple-vars-reassign -->
Multiple variables can each be reassigned.

```maxon
function main() returns int
    var a = 1
    var b = 2
    var c = 3
    a = 10
    b = 20
    c = 30
    return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: reassign-arithmetic -->
Reassignment with various arithmetic operations.

```maxon
function main() returns int
    var x = 100
    x = x - 50
    x = x * 2
    x = trunc(x / 5)
    return x
end 'main'
```
```exitcode
20
```

<!-- test: struct-array-field-moved-into-type -->
```maxon
type Container
    var data Array of int
end 'Container'

function main() returns int
    var arr = [10, 20, 30]
    var c = Container{data: arr}
    arr[0] = 999
    return c.data[0]
end 'main'
```
```maxoncstderr
error E008: specs/fragments/ownership.struct-array-field-moved-into-type.1.test:9:5: use after move: 'arr'
```

## Memory Safety Tests

These tests verify that the borrow checker prevents use-after-free and double-free issues.

### Use-After-Free Prevention

<!-- test: error.use-after-conditional-move -->
Moving a variable in an if branch makes it unavailable after the if statement.
```maxon
function consume(x int)
    x = x + 1
end 'consume'

function main() returns int
    var a = 42
    if true 'cond'
        consume(a)
    end 'cond'
    return a
end 'main'
```
```maxoncstderr
error E008: specs/fragments/ownership.error.use-after-conditional-move.1.test:11:5: use after move: 'a'
```

<!-- test: error.use-after-move-in-loop -->
Using a moved variable on the next loop iteration is an error.
```maxon
function consume(x int)
    x = x + 1
end 'consume'

function main() returns int
    var a = 42
    var i = 0
    while i < 3 'loop'
        consume(a)
        i = i + 1
    end 'loop'
    return 0
end 'main'
```
```maxoncstderr
error E008: specs/fragments/ownership.error.use-after-move-in-loop.1.test:11:9: use after move: 'a'
```

<!-- test: reassign-after-move-primitive -->
Reassignment after move restores ownership for primitives.
```maxon
function consume(x int)
    x = x + 1
end 'consume'

function main() returns int
    var a = 10
    consume(a)
    a = 20
    return a
end 'main'
```
```exitcode
20
```

### Double-Free Prevention

<!-- test: moved-array-not-double-freed -->
<!-- TrackMemory: true -->
Moved arrays are not freed by the original owner, preventing double-free.
The caller allocates, the callee (mutateFirst) takes ownership and frees.
```maxon
function mutateFirst(arr Array of int) returns int
    arr[0] = 100
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'mutateFirst'

function main() returns int
    let size = 3
    var arr = Array of size int
    arr[0] = 42
    return mutateFirst(arr)
end 'main'
```
```exitcode
100
```
```stdout
ALLOC #1: 24 bytes (array buffer)
MOVE: managed
MOVE: arr
FREE #1: 24 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 24 bytes
Freed:     24 bytes
Leaked:    0 bytes
Moves:     2
Increfs:   0
Decrefs:   0
```

### Mutually Exclusive Branches

<!-- test: mutually-exclusive-if-branches-with-return -->
Moves in mutually exclusive if branches with returns should not conflict.
When an if branch terminates with a return, the move only affects that branch.
```maxon
type Position
    export var x int
    export var y int
end 'Position'

function testMutuallyExclusiveMoves(choice int) returns int
    var pos = Position{x: 10, y: 20}

    if choice == 1 'first'
        var temp = pos
        return temp.x
    end 'first'

    if choice == 2 'second'
        var temp = pos
        return temp.y
    end 'second'

    return pos.x + pos.y
end 'testMutuallyExclusiveMoves'

function main() returns int
    return testMutuallyExclusiveMoves(1)
end 'main'
```
```exitcode
10
```

<!-- test: mutually-exclusive-if-branches-choice-2 -->
Same test, but taking the second branch.
```maxon
type Position
    export var x int
    export var y int
end 'Position'

function testMutuallyExclusiveMoves(choice int) returns int
    var pos = Position{x: 10, y: 20}

    if choice == 1 'first'
        var temp = pos
        return temp.x
    end 'first'

    if choice == 2 'second'
        var temp = pos
        return temp.y
    end 'second'

    return pos.x + pos.y
end 'testMutuallyExclusiveMoves'

function main() returns int
    return testMutuallyExclusiveMoves(2)
end 'main'
```
```exitcode
20
```

<!-- test: mutually-exclusive-if-branches-fallthrough -->
When neither branch is taken, the variable is still usable.
```maxon
type Position
    export var x int
    export var y int
end 'Position'

function testMutuallyExclusiveMoves(choice int) returns int
    var pos = Position{x: 10, y: 20}

    if choice == 1 'first'
        var temp = pos
        return temp.x
    end 'first'

    if choice == 2 'second'
        var temp = pos
        return temp.y
    end 'second'

    return pos.x + pos.y
end 'testMutuallyExclusiveMoves'

function main() returns int
    return testMutuallyExclusiveMoves(3)
end 'main'
```
```exitcode
30
```

<!-- test: if-else-then-terminates -->
When the then branch terminates with return, else branch sees original ownership.
```maxon
type Position
    export var x int
    export var y int
end 'Position'

function test(take_first bool) returns int
    var pos = Position{x: 10, y: 20}

    if take_first 'branch'
        var temp = pos  // Move pos
        return temp.x
    end 'branch' else 'other'
        // pos should still be owned here since then returned
        return pos.y
    end 'other'
end 'test'

function main() returns int
    return test(false)
end 'main'
```
```exitcode
20
```

<!-- test: if-else-else-terminates -->
When the else branch terminates with return, code after if-else sees then branch state.
```maxon
function test(take_first bool) returns int
    var x = 10

    if take_first 'branch'
        x = x + 5
    end 'branch' else 'other'
        return 99
    end 'other'

    // Only reachable if take_first was true
    return x
end 'test'

function main() returns int
    return test(true)
end 'main'
```
```exitcode
15
```

<!-- test: match-mutually-exclusive-cases -->
Match cases are mutually exclusive, returning from one case doesn't affect others.
```maxon
function test(choice int) returns int
    var x = 10

    match choice 'select'
        1 then return x + 1
        2 then return x + 2
        default then return x
    end 'select'
end 'test'

function main() returns int
    return test(1)
end 'main'
```
```exitcode
11
```

<!-- test: match-default-case -->
Match default case executes when no other cases match.
```maxon
function test(choice int) returns int
    var x = 10

    match choice 'select'
        1 then return x + 1
        2 then return x + 2
        default then return x
    end 'select'
end 'test'

function main() returns int
    return test(99)
end 'main'
```
```exitcode
10
```

<!-- test: or-block-early-return -->
Or block with early return allows variable use after guard-let.
```maxon
function getValue(opt int or nil) returns int
    let v = opt or 'empty'
        return 0
    end 'empty'
    return v
end 'getValue'

function main() returns int
    return getValue(42)
end 'main'
```
```exitcode
42
```

<!-- test: or-block-nil-case -->
Or block nil case returns early, leaving variable unused.
```maxon
function getValue(opt int or nil) returns int
    let v = opt or 'empty'
        return 0
    end 'empty'
    return v
end 'getValue'

function main() returns int
    return getValue(nil)
end 'main'
```
```exitcode
0
```

<!-- test: nested-if-with-returns -->
Nested if statements with returns restore ownership correctly.
```maxon
type Position
    export var x int
    export var y int
end 'Position'

function test(a int, b int) returns int
    var pos = Position{x: 10, y: 20}

    if a == 1 'outer'
        if b == 1 'inner'
            var temp = pos
            return temp.x
        end 'inner'
        // pos still owned here if inner didn't match
        return pos.y
    end 'outer'

    // pos still owned here if outer didn't match
    return pos.x + pos.y
end 'test'

function main() returns int
    return test(2, 2)
end 'main'
```
```exitcode
30
```
