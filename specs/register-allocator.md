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

<!-- test: int-add-constants -->
```maxon
function main() returns int
    return 30 + 12
end 'main'
```
```exitcode
42
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

<!-- test: int-two-vars-add -->
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

<!-- test: int-three-vars-arithmetic -->
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

<!-- test: int-var-reuse-twice -->
```maxon
function main() returns int
    var x = 21
    return x + x
end 'main'
```
```exitcode
42
```

<!-- test: int-multiply -->
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

<!-- test: int-chained-assignments -->
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

<!-- test: int-reassignment -->
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

<!-- test: int-six-vars-alive -->
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

<!-- test: int-ten-vars-alive -->
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

<!-- test: int-sixteen-vars-spill -->
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

<!-- test: int-twenty-vars-heavy-spill -->
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

<!-- test: int-interleaved-lifetimes -->
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

<!-- test: int-parallel-accumulation -->
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

<!-- test: int-call-preserves-value -->
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

<!-- test: int-multiple-calls-preserve -->
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

<!-- test: int-call-result-used-later -->
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

<!-- test: int-division-fixed-regs -->
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

<!-- test: int-modulo-fixed-regs -->
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

<!-- test: int-division-preserves-other-values -->
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

<!-- test: int-function-with-params -->
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

<!-- test: int-if-else-simple -->
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

<!-- test: int-if-else-value-survives-branch -->
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

<!-- test: int-while-loop-counter -->
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

<!-- test: int-while-loop-accumulator -->
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

<!-- test: int-while-loop-multiple-accumulators -->
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

<!-- test: int-nested-if-in-loop -->
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

<!-- test: int-nested-loops -->
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

<!-- test: int-nested-loops-with-outer-var -->
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

<!-- test: int-loop-with-function-call -->
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

<!-- test: int-nested-expressions-deep -->
```maxon
function main() returns int
    return ((((1 + 2) * 3) + 4) * 2) + 6
end 'main'
```
```exitcode
32
```

<!-- test: int-expression-both-sides-complex -->
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

<!-- test: int-many-params-function -->
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

<!-- test: int-recursive-factorial -->
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

<!-- test: int-loop-pressure-with-call -->
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

<!-- test: float-and-int-mixed-pressure -->
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

<!-- test: int-value-live-across-nested-control -->
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

<!-- test: int-fibonacci -->
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
