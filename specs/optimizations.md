---
feature: optimizations
status: stable
keywords: optimization, constant folding, dead code
category: compiler
---
# Compiler Optimizations

## Documentation

The Maxon compiler includes optimization passes that improve code efficiency:

### MLIR Optimizations

1. **Constant Folding** - Evaluates constant expressions at compile time
2. **Dead Code Elimination** - Removes unused variables and computations
3. **Dead Function Elimination** - Removes functions never called from main
4. **Dead Store Elimination** - Removes stores to memory that is never read
5. **Peephole Optimization** - Eliminates redundant instructions (self-moves, dead moves, unnecessary copies)

## Tests

<!-- test: constant-folding-basic -->
```maxon
function main() returns int
    return 10 + 20
end 'main'
```
```exitcode
30
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 30
    x86.epilogue
    x86.ret
}
```

<!-- test: constant-folding-nested -->
```maxon
function main() returns int
    return (5 + 3) * 2
end 'main'
```
```exitcode
16
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 16
    x86.epilogue
    x86.ret
}
```

<!-- test: constant-folding-subtraction -->
```maxon
function main() returns int
    return 100 - 25
end 'main'
```
```exitcode
75
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 75
    x86.epilogue
    x86.ret
}
```

<!-- test: constant-folding-multiplication -->
```maxon
function main() returns int
    return 6 * 7
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 42
    x86.epilogue
    x86.ret
}
```

<!-- test: constant-folding-division -->
```maxon
function main() returns int
    return 100 / 4
end 'main'
```
```exitcode
25
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 25
    x86.epilogue
    x86.ret
}
```

<!-- test: constant-folding-modulo -->
```maxon
function main() returns int
    return 17 mod 5
end 'main'
```
```exitcode
2
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 2
    x86.epilogue
    x86.ret
}
```

<!-- test: multiply-by-zero -->
```maxon
function main() returns int
    var x = 42
    return x * 0
end 'main'
```
```exitcode
0
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 0
    x86.epilogue
    x86.ret
}
```

<!-- test: dead-function-elimination -->
```maxon
function unused() returns int
    return 999
end 'unused'

function main() returns int
    return 42
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 42
    x86.epilogue
    x86.ret
}
```

<!-- test: strength-reduction-mul-by-2 -->
```maxon
function main() returns int
    var x = 10
    return x * 2
end 'main'
```
```exitcode
20
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 20
    x86.epilogue
    x86.ret
}
```

<!-- test: strength-reduction-mul-by-4 -->
```maxon
function main() returns int
    var x = 7
    return x * 4
end 'main'
```
```exitcode
28
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 28
    x86.epilogue
    x86.ret
}
```

<!-- test: strength-reduction-mul-by-8 -->
```maxon
function main() returns int
    var x = 5
    return x * 8
end 'main'
```
```exitcode
40
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 40
    x86.epilogue
    x86.ret
}
```

<!-- test: complex-constant-expression -->
```maxon
function main() returns int
    return ((2 + 3) * 4) - 5
end 'main'
```
```exitcode
15
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 15
    x86.epilogue
    x86.ret
}
```

<!-- test: constant-folding-chained -->
```maxon
function main() returns int
    var a = 2 + 3
    var b = a * 4
    return b
end 'main'
```
```exitcode
20
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 20
    x86.epilogue
    x86.ret
}
```

<!-- test: store-to-load-global -->
Store-to-load forwarding propagates constants through global variable stores and loads.
Dead store elimination then removes the unused global and its store.
```maxon
var g = 0

function main() returns int
    g = 5
    return g * 4
end 'main'
```
```exitcode
20
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rax, 20
    x86.epilogue
    x86.ret
}
```

### Mem2Reg Cross-Block Promotion

These tests verify cross-block SSA promotion using block arguments. Variables in
if/else branches are promoted from stack to SSA form with block arguments at merge
points. Loop variables remain on the stack due to complex register allocation
requirements for loop-carried values.

> **TODO**: When command-line argument support is implemented, update these tests
> to pass the condition value as a command-line argument. Currently these tests use
> function parameters, but interprocedural constant propagation could still optimize
> them away. Command-line arguments are truly runtime values that cannot be folded.

<!-- test: mem2reg-loop-variable -->
Loop counter variable modified each iteration - candidate for SSA promotion.
```maxon
function main() returns int
    var sum = 0
    var i = 1
    while i <= 5 'loop'
        sum = sum + i
        i = i + 1
    end 'loop'
    return sum
end 'main'
```
```exitcode
15
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=48
    x86.mov rax, 0
    x86.lea rcx, qword ptr [rbp-40]
    x86.mov qword ptr [rcx], rax
    x86.mov rax, 1
    x86.lea r8, qword ptr [rbp-48]
    x86.mov qword ptr [r8], rax
    x86.jmp while.cond
  ^while.cond:
    x86.mov rdx, qword ptr [r8]
    x86.mov rax, 5
    x86.cmp rdx, rax
    x86.setle rax
    x86.test rax, rax
    x86.jne while.body
    x86.jmp while.exit
  ^while.body:
    x86.mov rax, qword ptr [rcx]
    x86.mov rdx, qword ptr [r8]
    x86.mov r9, rax
    x86.add r9, rdx
    x86.mov qword ptr [rcx], r9
    x86.mov r9, qword ptr [r8]
    x86.mov rdx, 1
    x86.mov rax, r9
    x86.add rax, rdx
    x86.mov qword ptr [r8], rax
    x86.jmp while.cond
  ^while.exit:
    x86.mov rax, qword ptr [rcx]
    x86.epilogue
    x86.ret
}
```

<!-- test: mem2reg-conditional-assignment -->
Variable assigned different values in if/else branches - promoted to block arguments at merge.
Uses a runtime condition (comparison) to prevent constant folding from eliminating branches.
```maxon
function test(cond int) returns int
    var x = 0
    if cond > 0 'check'
        x = 10
    end 'check' else 'else'
        x = 20
    end 'else'
    return x
end 'test'

function main() returns int
    return test(1)
end 'main'
```
```exitcode
10
```
```requiredmlir
func.func @test(%cond: i64) -> i64 {
  ^entry(%cond: i64):
    x86.prologue stack_size=32
    x86.mov rax, rcx
    x86.mov rcx, 0
    x86.cmp rax, rcx
    x86.setg rcx
    x86.test rcx, rcx
    x86.jne then
    x86.jmp else
  ^then:
    x86.mov rax, 10
    x86.jmp merge
  ^else:
    x86.mov rax, 20
    x86.jmp merge
  ^merge(%11: i64):
    x86.epilogue
    x86.ret
}
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rcx, 1
    x86.call test
    x86.epilogue
    x86.ret
}
```

<!-- test: mem2reg-conditional-assignment-false -->
Same pattern with false condition to verify both branches work.
```maxon
function test(cond int) returns int
    var x = 0
    if cond > 0 'check'
        x = 10
    end 'check' else 'else'
        x = 20
    end 'else'
    return x
end 'test'

function main() returns int
    return test(0)
end 'main'
```
```exitcode
20
```
```requiredmlir
func.func @test(%cond: i64) -> i64 {
  ^entry(%cond: i64):
    x86.prologue stack_size=32
    x86.mov rax, rcx
    x86.mov rcx, 0
    x86.cmp rax, rcx
    x86.setg rcx
    x86.test rcx, rcx
    x86.jne then
    x86.jmp else
  ^then:
    x86.mov rax, 10
    x86.jmp merge
  ^else:
    x86.mov rax, 20
    x86.jmp merge
  ^merge(%11: i64):
    x86.epilogue
    x86.ret
}
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rcx, 0
    x86.call test
    x86.epilogue
    x86.ret
}
```

<!-- test: mem2reg-cross-block-read -->
Variable assigned in one block and read in another - requires cross-block promotion.
Uses a runtime condition to prevent constant folding.
```maxon
function test(cond int) returns int
    var result = 0
    if cond > 0 'check'
        result = 42
    end 'check'
    return result
end 'test'

function main() returns int
    return test(1)
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @test(%cond: i64) -> i64 {
  ^entry(%cond: i64):
    x86.prologue stack_size=32
    x86.mov rax, rcx
    x86.mov rcx, 0
    x86.mov rdx, 0
    x86.cmp rax, rdx
    x86.setg rdx
    x86.test rdx, rdx
    x86.jne then
    x86.jmp merge.args
  merge.args:
    x86.mov rdx, rcx
    x86.jmp merge
  ^then:
    x86.mov rdx, 42
    x86.jmp merge
  ^merge(%10: i64):
    x86.mov rax, rdx
    x86.epilogue
    x86.ret
}
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=32
    x86.mov rcx, 1
    x86.call test
    x86.epilogue
    x86.ret
}
```

<!-- test: mem2reg-nested-loops -->
Nested loop with multiple variables - stress test for cross-block promotion.
```maxon
function main() returns int
    var total = 0
    var i = 1
    while i <= 3 'outer'
        var j = 1
        while j <= 3 'inner'
            total = total + i * j
            j = j + 1
        end 'inner'
        i = i + 1
    end 'outer'
    return total
end 'main'
```
```exitcode
36
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
    x86.prologue stack_size=64
    x86.mov rax, 0
    x86.lea rcx, qword ptr [rbp-40]
    x86.mov qword ptr [rcx], rax
    x86.mov rax, 1
    x86.lea r8, qword ptr [rbp-48]
    x86.mov qword ptr [r8], rax
    x86.jmp while.cond
  ^while.cond:
    x86.mov rdx, qword ptr [r8]
    x86.mov rax, 3
    x86.cmp rdx, rax
    x86.setle rax
    x86.test rax, rax
    x86.jne while.body
    x86.jmp while.exit
  ^while.body:
    x86.mov rax, 1
    x86.lea r9, qword ptr [rbp-56]
    x86.mov qword ptr [r9], rax
    x86.jmp while.cond_1
  ^while.exit:
    x86.mov rdx, qword ptr [rcx]
    x86.mov rax, rdx
    x86.epilogue
    x86.ret
  ^while.cond_1:
    x86.mov rdx, qword ptr [r9]
    x86.mov rax, 3
    x86.cmp rdx, rax
    x86.setle rax
    x86.test rax, rax
    x86.jne while.body_1
    x86.jmp while.exit_1
  ^while.body_1:
    x86.mov rax, qword ptr [rcx]
    x86.mov rdx, qword ptr [r8]
    x86.mov r10, qword ptr [r9]
    x86.imul r11, rdx, r10
    x86.mov r10, rax
    x86.add r10, r11
    x86.mov qword ptr [rcx], r10
    x86.mov r10, qword ptr [r9]
    x86.mov r11, 1
    x86.mov rax, r10
    x86.add rax, r11
    x86.mov qword ptr [r9], rax
    x86.jmp while.cond_1
  ^while.exit_1:
    x86.mov rax, qword ptr [r8]
    x86.mov r11, 1
    x86.mov r10, rax
    x86.add r10, r11
    x86.mov qword ptr [r8], r10
    x86.jmp while.cond
}
```
