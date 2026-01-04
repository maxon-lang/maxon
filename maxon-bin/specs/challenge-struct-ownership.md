---
feature: challenge-struct-ownership
status: stable
keywords: struct, ownership, borrow, move
category: semantics
---
# Challenge Struct Ownership

## Documentation

## Struct Ownership

Structs follow the same ownership rules as other values: borrow for read-only access, move for mutation.

## Tests

<!-- test: struct-borrow-semantics -->
```maxon
type Data
    export var value int
end 'Data'

function readOnly(d Data) returns int
    return d.value
end 'readOnly'

function main() returns int
    var d = Data{value: 42}
    var result = readOnly(d)
    return d.value + result
end 'main'
```
```exitcode
84
```

<!-- test: struct-move-semantics -->
```maxon
type Data
    export var value int
end 'Data'

function takeOwnership(d Data) returns int
    d.value = 100
    return d.value
end 'takeOwnership'

function main() returns int
    var d = Data{value: 42}
    return takeOwnership(d)
end 'main'
```
```exitcode
100
```

<!-- test: use-after-move-error -->
Using a struct after it has been moved should be a compile-time error.

```maxon
type Data
    export var value int
end 'Data'

function takeOwnership(d Data) returns int
    d.value = 100
    return d.value
end 'takeOwnership'

function main() returns int
    var d = Data{value: 42}
    var result = takeOwnership(d)
    return d.value + result
end 'main'
```
```maxoncstderr
error E008: specs/fragments/challenge-struct-ownership.use-after-move-error.1.test:14:5: use after move: 'd'
```

<!-- test: use-after-move-in-expression-error -->
Using a moved variable in a subsequent expression should fail.

```maxon
type Data
    export var value int
end 'Data'

function consume(d Data)
    d.value = 0
end 'consume'

function main() returns int
    var d = Data{value: 42}
    consume(d)
    consume(d)
    return 0
end 'main'
```
```maxoncstderr
error E008: specs/fragments/challenge-struct-ownership.use-after-move-in-expression-error.1.test:13:5: use after move: 'd'
```

<!-- test: borrow-after-borrow-ok -->
Multiple borrows of the same variable should be allowed.

```maxon
type Data
    export var value int
end 'Data'

function readOnly(d Data) returns int
    return d.value
end 'readOnly'

function main() returns int
    var d = Data{value: 10}
    var a = readOnly(d)
    var b = readOnly(d)
    var c = readOnly(d)
    return a + b + c
end 'main'
```
```exitcode
30
```

<!-- test: move-then-reassign-ok -->
After moving a variable, reassigning it should allow using it again.

```maxon
type Data
    export var value int
end 'Data'

function consume(d Data) returns int
    d.value = 100
    return d.value
end 'consume'

function main() returns int
    var d = Data{value: 10}
    var first = consume(d)
    d = Data{value: 20}
    var second = consume(d)
    return first + second
end 'main'
```
```exitcode
200
```

<!-- test: let-struct-borrow-ok -->
Immutable struct can be borrowed multiple times.

```maxon
type Data
    export var value int
end 'Data'

function readOnly(d Data) returns int
    return d.value
end 'readOnly'

function main() returns int
    let d = Data{value: 50}
    var a = readOnly(d)
    var b = readOnly(d)
    return a + b
end 'main'
```
```exitcode
100
```

<!-- test: let-struct-move-error -->
Immutable struct cannot be passed to function that mutates it.

```maxon
type Data
    export var value int
end 'Data'

function mutate(d Data) returns int
    d.value = 100
    return d.value
end 'mutate'

function main() returns int
    let d = Data{value: 42}
    return mutate(d)
end 'main'
```
```maxoncstderr
error E010: specs/fragments/challenge-struct-ownership.let-struct-move-error.1.test:13:5: cannot move from immutable variable: 'd'
```

<!-- test: let-struct-use-after-borrow-ok -->
Immutable struct can still be used after being borrowed.

```maxon
type Data
    export var value int
end 'Data'

function readOnly(d Data) returns int
    return d.value
end 'readOnly'

function main() returns int
    let d = Data{value: 25}
    var result = readOnly(d)
    return d.value + result
end 'main'
```
```exitcode
50
```

<!-- test: let-cannot-reassign-error -->
Immutable variable cannot be reassigned.

```maxon
type Data
    export var value int
end 'Data'

function main() returns int
    let d = Data{value: 10}
    d = Data{value: 20}
    return d.value
end 'main'
```
```maxoncstderr
error E009: specs/fragments/challenge-struct-ownership.let-cannot-reassign-error.1.test:8:5: cannot assign to immutable variable: 'd'
```

<!-- test: var-from-let-can-move -->
Copying from let to var allows the var to be moved.

```maxon
type Data
    export var value int
end 'Data'

function consume(d Data) returns int
    d.value = 100
    return d.value
end 'consume'

function main() returns int
    let original = Data{value: 42}
    var copy = original
    return consume(copy)
end 'main'
```
```exitcode
100
```
