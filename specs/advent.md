---
feature: advent of compiler optimization
status: stable
keywords: abs, absolute value, math
category: math-intrinsic
---
# advent of compiler optimization

## Documentation

Matt Godbolt's Advent of Compiler Optimizations 2025
https://www.youtube.com/playlist?list=PL2HVqYf7If8cY4wLk7JUQ2f0JXY_xMQm2

## Tests

<!-- test: day1 -->
```maxon
function main() returns int
  return 0
end 'main'
```
```exitcode
0
```
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.return %0
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    func.return %0
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.xor eax, eax
    x86.ret
  }
}
```

<!-- test: day2 -->
```maxon
function add(x int, y int) returns int
    return x + y
end 'add'

function main() returns int
  return add(3, y: 4)
end 'main'
```
```exitcode
7
```
```RequiredMLIR
=== maxon
module {
  func @add(x: i64, y: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = y} {type = i64}
    %2 = maxon.binop %0, %1 {op = add} {kind = i64}
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.literal {value = 4 : i64}
    %5 = maxon.call @add %3, %4
    maxon.return %5
  }
}
=== standard
module {
  func @add(x: i64, y: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    memref.store %0, x
    %1 = func.param y : StdI64
    memref.store %1, y
    %2 = arith.addi %0, %1
    func.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = arith.constant {value = 3 : i64}
    %4 = arith.constant {value = 4 : i64}
    %5 = func.call @add %3, %4
    func.return %5
  }
}
=== x86
module {
  func @add(x: i64, y: i64) -> i64 {
  entry:
    x86.lea eax, [ecx + edx]
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.mov eax, 3
    x86.mov ecx, 4
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.jmp add
  }
}
```
