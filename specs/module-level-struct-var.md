---
feature: module-level-struct-var
status: stable
keywords: [module, var, struct, field, assignment, method]
category: declaration
---
# Module-Level Struct Variables

## Documentation

Module-level `var` declarations with struct types support field assignment and method calls on fields.

## Tests

<!-- test: module-struct-field-assign -->
Module-level struct var field assignment.
```maxon
typealias SmallInt = int(0 to 255)

type Counter
    export var value SmallInt
end 'Counter'

var state = Counter{value: 0}

function main() returns ExitCode
    state.value = 42
    return state.value
end 'main'
```
```exitcode
42
```

<!-- test: module-struct-nested-method-call -->
Module-level struct var nested field method call.
```maxon
typealias Byte = byte(0 to u8.max)
typealias ByteArray = Array with Byte

type State
    export var items ByteArray
end 'State'

var state = State{items: ByteArray{}}

function main() returns ExitCode
    state.items.push(10)
    state.items.push(20)
    return state.items.count()
end 'main'
```
```exitcode
2
```

<!-- test: module-struct-nested-field-assign -->
Module-level struct var nested field assignment through chain.
```maxon
typealias SmallInt = int(0 to 255)

type Inner
    export var x SmallInt
end 'Inner'

type Outer
    export var inner Inner
end 'Outer'

var state = Outer{inner: Inner{x: 0}}

function main() returns ExitCode
    state.inner.x = 99
    return state.inner.x
end 'main'
```
```exitcode
99
```
