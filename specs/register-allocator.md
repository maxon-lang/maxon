---
feature: register-allocator
status: experimental
keywords: [regalloc, registers, spilling, codegen]
category: codegen
---

## Documentation

These tests exercise register allocation with progressively increasing difficulty. They are organized into six levels:

1. **Basic Value Tracking** — Single values flowing to return. A trivial allocator can pass these.
2. **Multiple Values and Reuse** — More than one live value at a time; values reused across expressions.
3. **Register Pressure and Spilling** — More live values than physical registers, forcing spills to stack.
4. **Function Calls and Fixed Register Constraints** — Caller-saved register preservation, IDIV constraints (RAX/RDX), parameter passing.
5. **Control Flow and Loops** — Values live across branches, loop back-edges, and nested control flow.
6. **Advanced Scenarios** — Combined challenges: recursion, deep expressions, mixed int/float, long live ranges, parallel copy.

## Tests

### Level 1: Basic Value Tracking

<!-- test: int-constant -->
```maxon
function main() returns int
    return 42
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.constant {value = 42 : i64}
    maxon.return %0
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %1 = arith.constant {value = 42 : i64}
    func.return %1
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.push rbp
    x86.mov rbp, rsp
    x86.mov eax, 42
    x86.pop rbp
    x86.ret
  }
}
```

<!-- test: int-var-roundtrip -->
```maxon
function main() returns int
    var x = 99
    return x
end 'main'
```
```exitcode
99
```
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.constant {value = 99 : i64}
    maxon.var_decl x %0
    %1 = maxon.var_load x {type = i64}
    maxon.return %1
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %2 = arith.constant {value = 99 : i64}
    memref.alloca x : i64
    memref.store %2, x
    %3 = memref.load x : i64
    func.return %3
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 16
    x86.mov eax, 99
    x86.movsd [rbp-8], xmm0
    x86.movsd xmm0, [rbp-8]
    x86.add rsp, 16
    x86.pop rbp
    x86.ret
  }
}
```

<!-- test: int-add-constants -->
```maxon
function main() returns int
    return 30 + 12
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.constant {value = 30 : i64}
    %1 = maxon.constant {value = 12 : i64}
    %2 = maxon.addi %0, %1
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %3 = arith.constant {value = 30 : i64}
    %4 = arith.constant {value = 12 : i64}
    %5 = arith.addi %3, %4
    func.return %5
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.push rbp
    x86.mov rbp, rsp
    x86.mov eax, 30
    x86.mov ecx, 12
    x86.add eax, ecx
    x86.pop rbp
    x86.ret
  }
}
```

<!-- test: int-subtract-constants -->
```maxon
function main() returns int
    return 100 - 58
end 'main'
```
```exitcode
42
```

### Level 2: Multiple Values and Reuse

<!-- disabled-test: int-two-vars-add -->
```maxon
function main() returns int
    var a = 30
    var b = 12
    return a + b
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-three-vars-arithmetic -->
```maxon
function main() returns int
    var a = 50
    var b = 20
    var c = 28
    return a + b - c
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-var-reuse-twice -->
```maxon
function main() returns int
    var x = 21
    return x + x
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-multiply -->
```maxon
function main() returns int
    var a = 6
    var b = 7
    return a * b
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-chained-assignments -->
```maxon
function main() returns int
    var a = 10
    var b = a + 5
    var c = b + 7
    var d = c + 20
    return d
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-reassignment -->
```maxon
function main() returns int
    var x = 100
    var y = x - 80
    x = 22
    return x + y
end 'main'
```
```exitcode
42
```

### Level 3: Register Pressure and Spilling

<!-- disabled-test: int-six-vars-alive -->
```maxon
function main() returns int
    var a = 1
    var b = 2
    var c = 3
    var d = 4
    var e = 5
    var f = 6
    return a + b + c + d + e + f
end 'main'
```
```exitcode
21
```

<!-- disabled-test: int-ten-vars-alive -->
```maxon
function main() returns int
    var a = 1
    var b = 2
    var c = 3
    var d = 4
    var e = 5
    var f = 6
    var g = 7
    var h = 8
    var i = 9
    var j = 10
    return a + b + c + d + e + f + g + h + i + j
end 'main'
```
```exitcode
55
```

<!-- disabled-test: int-sixteen-vars-spill -->
```maxon
function main() returns int
    var a = 1
    var b = 2
    var c = 3
    var d = 4
    var e = 5
    var f = 6
    var g = 7
    var h = 8
    var i = 9
    var j = 10
    var k = 11
    var l = 12
    var m = 13
    var n = 14
    var o = 15
    var p = 16
    return (a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p) mod 256
end 'main'
```
```exitcode
136
```

<!-- disabled-test: int-twenty-vars-heavy-spill -->
```maxon
function main() returns int
    var a = 1
    var b = 2
    var c = 3
    var d = 4
    var e = 5
    var f = 6
    var g = 7
    var h = 8
    var i = 9
    var j = 10
    var k = 11
    var l = 12
    var m = 13
    var n = 14
    var o = 15
    var p = 16
    var q = 17
    var r = 18
    var s = 19
    var t = 20
    return (a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p + q + r + s + t) mod 256
end 'main'
```
```exitcode
210
```

<!-- disabled-test: int-interleaved-lifetimes -->
```maxon
function main() returns int
    var a = 10
    var b = 20
    var ab = a + b
    var c = 30
    var d = 40
    var cd = c + d
    var e = 50
    var f = 60
    var ef = e + f
    var result = ab + cd + ef
    return result mod 256
end 'main'
```
```exitcode
210
```

<!-- disabled-test: int-parallel-accumulation -->
```maxon
function main() returns int
    var sum1 = 0
    var sum2 = 0
    var sum3 = 0
    sum1 = sum1 + 10
    sum2 = sum2 + 20
    sum3 = sum3 + 30
    sum1 = sum1 + 5
    sum2 = sum2 + 10
    sum3 = sum3 + 15
    return sum1 + sum2 + sum3
end 'main'
```
```exitcode
90
```

### Level 4: Function Calls and Fixed Register Constraints

<!-- disabled-test: int-call-preserves-value -->
```maxon
function getForty() returns int
    return 40
end 'getForty'

function main() returns int
    var x = 2
    var y = getForty()
    return x + y
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-multiple-calls-preserve -->
```maxon
function getTen() returns int
    return 10
end 'getTen'

function getTwo() returns int
    return 2
end 'getTwo'

function main() returns int
    var a = 5
    var b = getTen()
    var c = 7
    var d = getTwo()
    return a + b + c + d
end 'main'
```
```exitcode
24
```

<!-- disabled-test: int-call-result-used-later -->
```maxon
function compute() returns int
    return 100
end 'compute'

function main() returns int
    var a = compute()
    var b = compute()
    return (a + b) mod 256
end 'main'
```
```exitcode
200
```

<!-- disabled-test: int-division-fixed-regs -->
```maxon
function main() returns int
    var a = 126
    var b = 3
    return a / b
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-modulo-fixed-regs -->
```maxon
function main() returns int
    var a = 142
    var b = 100
    return a mod b
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-division-preserves-other-values -->
```maxon
function main() returns int
    var x = 10
    var a = 84
    var b = 2
    var quotient = a / b
    return quotient - x
end 'main'
```
```exitcode
32
```

<!-- disabled-test: int-function-with-params -->
```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(30, b: 12)
end 'main'
```
```exitcode
42
```

### Level 5: Control Flow and Loops

<!-- disabled-test: int-if-else-simple -->
```maxon
function main() returns int
    var x = 10
    if x == 10 'check'
        return 42
    end 'check' else 'other'
        return 0
    end 'other'
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-if-else-value-survives-branch -->
```maxon
function main() returns int
    var base = 40
    var cond = 1
    var extra = 0
    if cond == 1 'check'
        extra = 2
    end 'check' else 'other'
        extra = 100
    end 'other'
    return base + extra
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-while-loop-counter -->
```maxon
function main() returns int
    var i = 0
    while i < 42 'loop'
        i = i + 1
    end 'loop'
    return i
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-while-loop-accumulator -->
```maxon
function main() returns int
    var sum = 0
    var i = 0
    while i < 10 'loop'
        sum = sum + i
        i = i + 1
    end 'loop'
    return sum mod 256
end 'main'
```
```exitcode
45
```

<!-- disabled-test: int-while-loop-multiple-accumulators -->
```maxon
function main() returns int
    var even_sum = 0
    var odd_sum = 0
    var count = 0
    var i = 0
    while i < 20 'loop'
        if i mod 2 == 0 'even'
            even_sum = even_sum + i
            count = count + 1
        end 'even' else 'odd'
            odd_sum = odd_sum + i
        end 'odd'
        i = i + 1
    end 'loop'
    return (even_sum + odd_sum + count) mod 256
end 'main'
```
```exitcode
200
```

<!-- disabled-test: int-nested-if-in-loop -->
```maxon
function main() returns int
    var result = 0
    var i = 1
    while i <= 10 'loop'
        if i <= 5 'first'
            result = result + i
        end 'first' else 'second'
            result = result + i * 2
        end 'second'
        i = i + 1
    end 'loop'
    return result mod 256
end 'main'
```
```exitcode
95
```

<!-- disabled-test: int-nested-loops -->
```maxon
function main() returns int
    var total = 0
    var i = 0
    while i < 5 'outer'
        var j = 0
        while j < 4 'inner'
            total = total + 1
            j = j + 1
        end 'inner'
        i = i + 1
    end 'outer'
    return total
end 'main'
```
```exitcode
20
```

<!-- disabled-test: int-nested-loops-with-outer-var -->
```maxon
function main() returns int
    var total = 0
    var i = 1
    while i <= 5 'outer'
        var j = 1
        while j <= i 'inner'
            total = total + 1
            j = j + 1
        end 'inner'
        i = i + 1
    end 'outer'
    return total
end 'main'
```
```exitcode
15
```

<!-- disabled-test: int-loop-with-function-call -->
```maxon
function double(x int) returns int
    return x * 2
end 'double'

function main() returns int
    var sum = 0
    var i = 0
    while i < 5 'loop'
        sum = sum + double(i)
        i = i + 1
    end 'loop'
    return sum
end 'main'
```
```exitcode
20
```

### Level 6: Advanced Scenarios

<!-- disabled-test: int-nested-expressions-deep -->
```maxon
function main() returns int
    return ((((1 + 2) * 3) + 4) * 2) + 6
end 'main'
```
```exitcode
32
```

<!-- disabled-test: int-expression-both-sides-complex -->
```maxon
function main() returns int
    var a = 3
    var b = 5
    var c = 7
    var d = 2
    return (a + b) * (c - d)
end 'main'
```
```exitcode
40
```

<!-- disabled-test: int-many-params-function -->
```maxon
function sum5(a int, b int, c int, d int, e int) returns int
    return a + b + c + d + e
end 'sum5'

function main() returns int
    return sum5(5, b: 10, c: 8, d: 12, e: 7)
end 'main'
```
```exitcode
42
```

<!-- disabled-test: int-recursive-factorial -->
```maxon
function factorial(n int) returns int
    if n <= 1 'base'
        return 1
    end 'base'
    return n * factorial(n - 1)
end 'factorial'

function main() returns int
    return factorial(5) mod 256
end 'main'
```
```exitcode
120
```

<!-- disabled-test: int-loop-pressure-with-call -->
```maxon
function identity(x int) returns int
    return x
end 'identity'

function main() returns int
    var a = 1
    var b = 2
    var c = 3
    var d = 4
    var e = 5
    var f = 6
    var i = 0
    while i < 3 'loop'
        a = a + identity(b)
        c = c + identity(d)
        e = e + identity(f)
        i = i + 1
    end 'loop'
    return (a + c + d + e + f) mod 256
end 'main'
```
```exitcode
55
```

<!-- disabled-test: float-and-int-mixed-pressure -->
```maxon
function main() returns int
    var x = 3.14
    var y = 2.86
    var sum_f = x + y
    var a = 10
    var b = 20
    var sum_i = a + b
    return trunc(sum_f) + sum_i
end 'main'
```
```exitcode
36
```

<!-- disabled-test: int-value-live-across-nested-control -->
```maxon
function main() returns int
    var sentinel = 100
    var total = 0
    var i = 0
    while i < 3 'outer'
        var j = 0
        while j < 3 'inner'
            if i == j 'diag'
                total = total + 1
            end 'diag'
            j = j + 1
        end 'inner'
        i = i + 1
    end 'outer'
    return sentinel + total
end 'main'
```
```exitcode
103
```

<!-- disabled-test: int-fibonacci -->
```maxon
function main() returns int
    var a = 0
    var b = 1
    var i = 0
    while i < 13 'loop'
        var temp = a + b
        a = b
        b = temp
        i = i + 1
    end 'loop'
    return a
end 'main'
```
```exitcode
233
```
