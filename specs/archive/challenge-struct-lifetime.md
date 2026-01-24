---
feature: challenge-struct-lifetime
status: stable
keywords: struct, lifetime, return, stack, heap
category: semantics
---
# Challenge Struct Lifetime

## Documentation

## Struct Lifetime

Structs returned from functions must have their data survive beyond the function's stack frame.

## Tests

<!-- test: return-struct-with-computed-fields -->
```maxon
type Result
    export var sum int
    export var product int
end 'Result'

function compute(a int, b int) returns Result
    return {sum: a + b, product: a * b}
end 'compute'

function main() returns int
    var r = compute(3, b: 4)
    return r.sum + r.product
end 'main'
```
```exitcode
19
```

<!-- test: pass-struct-to-function-and-return -->
```maxon
type Counter
    export var value int
end 'Counter'

function increment(c Counter) returns Counter
    return {value: c.value + 1}
end 'increment'

function main() returns int
    var c1 = Counter{value: 10}
    var c2 = increment(c1)
    return c2.value
end 'main'
```
```exitcode
11
```

<!-- test: multiple-struct-returns -->
```maxon
type Value
    export var n int
end 'Value'

function step1() returns Value
    return {n: 1}
end 'step1'

function step2(v Value) returns Value
    return {n: v.n + 10}
end 'step2'

function step3(v Value) returns Value
    return {n: v.n + 100}
end 'step3'

function main() returns int
    var v1 = step1()
    var v2 = step2(v1)
    var v3 = step3(v2)
    return v3.n
end 'main'
```
```exitcode
111
```
