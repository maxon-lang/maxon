---
feature: many-args
status: stable
keywords: functions, arguments, calling-convention
category: functions
---
# Functions with Many Arguments

## Documentation

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
  return add5(1, b: 2, c: 3, d: 4, e: 5)
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
  return add6(10, b: 20, c: 30, d: 40, e: 50, f: 60)
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
  return sum7(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7)
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
  return sum8(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7, h: 8)
end 'main'
```
```exitcode
36
```

<!-- test: mixed-computation-many-args -->
```maxon
function compute(a int, b int, c int, d int, e int, f int) returns int
  let reg_sum = a + b + c + d
  let stack_sum = e + f
  return reg_sum * stack_sum
end 'compute'

function main() returns int
  return compute(1, b: 2, c: 3, d: 4, e: 10, f: 5)
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
  let x = add5(1, b: 2, c: 3, d: 4, e: 5)
  let y = add5(10, b: 20, c: 30, d: 40, e: 50)
  return x + y
end 'main'
```
```exitcode
165
```
