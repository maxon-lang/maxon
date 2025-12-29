---
feature: many-args
status: stable
keywords: functions, arguments, calling-convention
category: functions
---

## Developer Notes

Support for functions with more than 4 arguments. Windows x64 ABI passes the first 4
integer/pointer arguments in RCX, RDX, R8, R9 (or XMM0-XMM3 for floats). Additional
arguments are passed on the stack at [RSP + 32 + (i-4)*8] where 32 is the shadow space.

## Documentation

# Functions with Many Arguments

Maxon supports functions with any number of arguments. The first 4 arguments are passed
in registers, while additional arguments are passed on the stack following the Windows
x64 calling convention.

## Tests

<!-- test: five-int-args -->
```maxon
function add5(a int, b int, c int, d int, e int) returns int
    return a + b + c + d + e
end 'add5'

function main() returns int
    return add5(1, 2, 3, 4, 5)
end 'main'
```
```exitcode
15
```

<!-- test: six-int-args -->
```maxon
function add6(a int, b int, c int, d int, e int, f int) returns int
    return a + b + c + d + e + f
end 'add6'

function main() returns int
    return add6(10, 20, 30, 40, 50, 60)
end 'main'
```
```exitcode
210
```

<!-- test: seven-int-args -->
```maxon
function sum7(a int, b int, c int, d int, e int, f int, g int) returns int
    return a + b + c + d + e + f + g
end 'sum7'

function main() returns int
    return sum7(1, 2, 3, 4, 5, 6, 7)
end 'main'
```
```exitcode
28
```

<!-- test: eight-int-args -->
```maxon
function sum8(a int, b int, c int, d int, e int, f int, g int, h int) returns int
    return a + b + c + d + e + f + g + h
end 'sum8'

function main() returns int
    return sum8(1, 2, 3, 4, 5, 6, 7, 8)
end 'main'
```
```exitcode
36
```

<!-- test: mixed-computation-many-args -->
```maxon
function compute(a int, b int, c int, d int, e int, f int) returns int
    let reg_sum int = a + b + c + d
    let stack_sum int = e + f
    return reg_sum * stack_sum
end 'compute'

function main() returns int
    return compute(1, 2, 3, 4, 10, 5)
end 'main'
```
```exitcode
150
```

<!-- test: nested-calls-many-args -->
```maxon
function add5(a int, b int, c int, d int, e int) returns int
    return a + b + c + d + e
end 'add5'

function main() returns int
    let x int = add5(1, 2, 3, 4, 5)
    let y int = add5(10, 20, 30, 40, 50)
    return x + y
end 'main'
```
```exitcode
165
```
