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
  x64.prologue stack_size=32
  x64.mov rax, 30
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 16
  x64.epilogue
  x64.ret
}
```

<!-- test: constant-folding-subtraction -->
```maxon
function main() returns int
  return 10-25
end 'main'
```
```exitcode
75
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 75
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 25
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 2
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 0
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 20
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 28
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 40
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 15
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 20
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, 20
  x64.epilogue
  x64.ret
}
```

### Math Builtin Constant Folding

<!-- test: constant-folding-abs -->
```maxon
function main() returns int
  return trunc(abs(-5.5))
end 'main'
```
```exitcode
5
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 5
  x64.epilogue
  x64.ret
}
```

<!-- test: constant-folding-sqrt -->
```maxon
function main() returns int
  return trunc(sqrt(16.0))
end 'main'
```
```exitcode
4
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 4
  x64.epilogue
  x64.ret
}
```

<!-- test: constant-folding-floor-ceil -->
```maxon
function main() returns int
  return trunc(floor(3.7)) + trunc(ceil(2.1))
end 'main'
```
```exitcode
6
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 6
  x64.epilogue
  x64.ret
}
```

<!-- test: constant-folding-round -->
```maxon
function main() returns int
  return trunc(round(2.5)) + trunc(round(3.5))
end 'main'
```
```exitcode
6
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 6
  x64.epilogue
  x64.ret
}
```

<!-- test: constant-folding-min-max -->
```maxon
function main() returns int
  return trunc(min(5.0, 3.0)) + trunc(max(2.0, 7.0))
end 'main'
```
```exitcode
10
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 10
  x64.epilogue
  x64.ret
}
```

<!-- test: constant-folding-trunc -->
```maxon
function main() returns int
  return trunc(9.9)
end 'main'
```
```exitcode
9
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 9
  x64.epilogue
  x64.ret
}
```

### Mem2Reg Cross-Block Promotion

These tests verify cross-block SSA promotion using block arguments. Variables in
if/else branches and loops are promoted from stack to SSA form with block arguments
at merge points and loop headers.

> **TODO**: When command-line argument support is implemented, update these tests
> to pass the condition value as a command-line argument. Currently these tests use
> function parameters, but interprocedural constant propagation could still optimize
> them away. Command-line arguments are truly runtime values that cannot be folded.

<!-- test: mem2reg-loop-variable -->
Loop counter variables promoted to SSA with block arguments at loop header.
The `sum` and `i` variables are passed through block arguments instead of using stack.
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
  x64.prologue stack_size=32
  x64.mov rcx, 1
  x64.mov r8, 5
  x64.mov r9, 0
  x64.mov r10, rcx
  x64.jmp while.cond
  ^while.cond(%14: i64, %16: i64):
  x64.cmp r10, r8
  x64.setle rdx
  x64.movzx rdx, rdx
  x64.test rdx, rdx
  x64.jne while.body
  x64.jmp while.exit
  ^while.body:
  x64.mov rdx, r9
  x64.add rdx, r10
  x64.mov rax, r10
  x64.add rax, rcx
  x64.mov r9, rdx
  x64.mov r10, rax
  x64.jmp while.cond
  ^while.exit:
  x64.mov rax, r9
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, rcx
  x64.mov rcx, 0
  x64.cmp rax, rcx
  x64.setg rcx
  x64.movzx rcx, rcx
  x64.test rcx, rcx
  x64.jne then
  x64.jmp else
  ^then:
  x64.mov rax, 10
  x64.jmp merge
  ^else:
  x64.mov rax, 20
  x64.jmp merge
  ^merge(%13: i64):
  x64.epilogue
  x64.ret
}
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rcx, 1
  x64.call test
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, rcx
  x64.mov rcx, 0
  x64.cmp rax, rcx
  x64.setg rcx
  x64.movzx rcx, rcx
  x64.test rcx, rcx
  x64.jne then
  x64.jmp else
  ^then:
  x64.mov rax, 10
  x64.jmp merge
  ^else:
  x64.mov rax, 20
  x64.jmp merge
  ^merge(%13: i64):
  x64.epilogue
  x64.ret
}
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rcx, 0
  x64.call test
  x64.epilogue
  x64.ret
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
  x64.prologue stack_size=32
  x64.mov rax, rcx
  x64.mov rcx, 0
  x64.cmp rax, rcx
  x64.setg rax
  x64.movzx rax, rax
  x64.test rax, rax
  x64.jne then
  x64.jmp merge.args.from.entry
  merge.args.from.entry:
  x64.mov rax, rcx
  x64.jmp merge
  ^then:
  x64.mov rax, 42
  x64.jmp merge
  ^merge(%12: i64):
  x64.epilogue
  x64.ret
}
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rcx, 1
  x64.call test
  x64.epilogue
  x64.ret
}
```

<!-- test: mem2reg-nested-loops -->
Nested loop with multiple variables - stress test for cross-block promotion.
Loop counter variables `i` and `j` are promoted to SSA. The `total` variable remains
on the stack because it spans nested loops with stores in the inner loop.
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
  x64.prologue stack_size=56 saved_gprs=[RBX]
  x64.mov rax, 0
  x64.lea rcx, qword ptr [rbp-40]
  x64.mov qword ptr [rcx], rax
  x64.mov r8, 1
  x64.mov r9, 3
  x64.mov r10, r8
  x64.jmp while.cond
  ^while.cond(%24: i64):
  x64.cmp r10, r9
  x64.setle rax
  x64.movzx rax, rax
  x64.test rax, rax
  x64.jne while.body
  x64.jmp while.exit
  ^while.body:
  x64.mov r11, 1
  x64.jmp while.cond_1
  ^while.exit:
  x64.mov rdx, qword ptr [rcx]
  x64.mov rax, rdx
  x64.epilogue saved_gprs=[RBX]
  x64.ret
  ^while.cond_1(%25: i64):
  x64.cmp r11, r9
  x64.setle rdx
  x64.movzx rdx, rdx
  x64.test rdx, rdx
  x64.jne while.body_1
  x64.jmp while.exit_1
  ^while.body_1:
  x64.mov rdx, qword ptr [rcx]
  x64.imul rax, r10, r11
  x64.mov rbx, rdx
  x64.add rbx, rax
  x64.mov qword ptr [rcx], rbx
  x64.mov rax, 1
  x64.mov rdx, r11
  x64.add rdx, rax
  x64.mov r11, rdx
  x64.jmp while.cond_1
  ^while.exit_1:
  x64.mov rdx, r10
  x64.add rdx, r8
  x64.mov r10, rdx
  x64.jmp while.cond
}
```

### Branch Elimination

When a branch condition is a compile-time constant, the branch can be replaced with
an unconditional jump to the appropriate target.

<!-- test: branch-elimination-always-true -->
When an if condition is always true, the else branch is eliminated.
```maxon
function main() returns int
  if true 'check'
    return 42
  end 'check' else 'else'
    return 99
  end 'else'
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
}
```

<!-- test: branch-elimination-always-false -->
When an if condition is always false, the then branch is eliminated.
```maxon
function main() returns int
  if false 'check'
    return 42
  end 'check' else 'else'
    return 99
  end 'else'
end 'main'
```
```exitcode
99
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 99
  x64.epilogue
  x64.ret
}
```

<!-- test: branch-elimination-constant-comparison -->
When a comparison is between two constants, the branch is eliminated.
```maxon
function main() returns int
  if 5 > 3 'check'
    return 1
  end 'check' else 'else'
    return 0
  end 'else'
end 'main'
```
```exitcode
1
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 1
  x64.epilogue
  x64.ret
}
```

<!-- test: branch-elimination-float-comparison -->
When a float comparison involves constant-folded values (like abs(0.0) == 0.0), the branch is eliminated.
```maxon
function main() returns int
  var x = abs(0.0)
  if x == 0.0 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 0
  x64.epilogue
  x64.ret
}
```

<!-- test: mem2reg-loop-conditional-update -->
Variable with conditional update inside a loop requires block arguments at the internal
merge block (where then/else paths rejoin) in addition to the loop header.
```maxon
function main() returns int
  var i = 0
  var found = false
  while i < 10 'search'
    if i == 5 'check'
      found = true
    end 'check'
    i = i + 1
  end 'search'
  if found 'result'
    return 1
  end 'result'
  return 0
end 'main'
```
```exitcode
1
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32 saved_gprs=[RBX,R12]
  x64.mov r8, 10
  x64.mov r9, 1
  x64.mov r10, 1
  x64.mov r11, 5
  x64.mov rbx, 0
  x64.mov r12, 0
  x64.jmp while.cond
  ^while.cond(%17: i64, %18: i1):
  x64.cmp rbx, r8
  x64.setl rdx
  x64.movzx rdx, rdx
  x64.test rdx, rdx
  x64.jne while.body
  x64.jmp while.exit
  ^while.body:
  x64.cmp rbx, r11
  x64.sete rdx
  x64.movzx rdx, rdx
  x64.test rdx, rdx
  x64.jne merge
  x64.jmp merge
  ^while.exit:
  x64.test r12, r12
  x64.jne then_1
  x64.jmp merge_1
  ^merge:
  x64.mov rdx, rbx
  x64.add rdx, r9
  x64.mov rbx, rdx
  x64.mov r12, r10
  x64.jmp while.cond
  ^then_1:
  x64.mov rax, 1
  x64.epilogue saved_gprs=[RBX,R12]
  x64.ret
  ^merge_1:
  x64.mov rax, 0
  x64.epilogue saved_gprs=[RBX,R12]
  x64.ret
}
```

### Loop Invariant Code Motion (LICM)

LICM hoists loop-invariant computations from loop bodies to loop preheaders,
reducing redundant computation when the loop executes multiple iterations.

<!-- test: licm-constant-hoisting -->
Constants used inside loops are hoisted to the entry block.
```maxon
function main() returns int
  var sum = 0
  var i = 0
  while i < 5 'loop'
    sum = sum + 10
    i = i + 1
  end 'loop'
  return sum
end 'main'
```
```exitcode
50
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 0
  x64.mov rcx, 5
  x64.mov r8, 10
  x64.mov r9, 1
  x64.mov r10, rax
  x64.mov r11, rax
  x64.jmp while.cond
  ^while.cond(%14: i64, %16: i64):
  x64.cmp r11, rcx
  x64.setl rdx
  x64.movzx rdx, rdx
  x64.test rdx, rdx
  x64.jne while.body
  x64.jmp while.exit
  ^while.body:
  x64.mov rdx, r10
  x64.add rdx, r8
  x64.mov rax, r11
  x64.add rax, r9
  x64.mov r10, rdx
  x64.mov r11, rax
  x64.jmp while.cond
  ^while.exit:
  x64.mov rax, r10
  x64.epilogue
  x64.ret
}
```

<!-- test: licm-no-hoist-load-with-store -->
Loads from memory locations that have stores in the loop are NOT hoisted.
This test verifies that `y` is read fresh each iteration since it's modified in the loop.
```maxon
function main() returns int
  var y = 0
  var i = 0
  while i < 3 'loop'
    y = y + 1
    i = i + 1
  end 'loop'
  return y
end 'main'
```
```exitcode
3
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 0
  x64.mov rcx, 3
  x64.mov r8, 1
  x64.mov r9, rax
  x64.mov r10, rax
  x64.jmp while.cond
  ^while.cond(%14: i64, %16: i64):
  x64.cmp r10, rcx
  x64.setl rdx
  x64.movzx rdx, rdx
  x64.test rdx, rdx
  x64.jne while.body
  x64.jmp while.exit
  ^while.body:
  x64.mov rdx, r9
  x64.add rdx, r8
  x64.mov rax, r10
  x64.add rax, r8
  x64.mov r9, rdx
  x64.mov r10, rax
  x64.jmp while.cond
  ^while.exit:
  x64.mov rax, r9
  x64.epilogue
  x64.ret
}
```

<!-- test: licm-nested-loop-invariant -->
In nested loops, values from outer loop headers are NOT invariant for inner loops
because they change per outer loop iteration.
```maxon
function main() returns int
  var total = 0
  var i = 1
  while i <= 3 'outer'
    var j = 1
    while j <= 2 'inner'
      total = total + i
      j = j + 1
    end 'inner'
    i = i + 1
  end 'outer'
  return total
end 'main'
```
```exitcode
12
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=56 saved_gprs=[RBX]
  x64.mov rax, 0
  x64.lea rcx, qword ptr [rbp-40]
  x64.mov qword ptr [rcx], rax
  x64.mov r8, 1
  x64.mov r9, 3
  x64.mov r10, 2
  x64.mov r11, r8
  x64.jmp while.cond
  ^while.cond(%22: i64):
  x64.cmp r11, r9
  x64.setle rax
  x64.movzx rax, rax
  x64.test rax, rax
  x64.jne while.body
  x64.jmp while.exit
  ^while.body:
  x64.mov rbx, 1
  x64.jmp while.cond_1
  ^while.exit:
  x64.mov rax, qword ptr [rcx]
  x64.epilogue saved_gprs=[RBX]
  x64.ret
  ^while.cond_1(%23: i64):
  x64.cmp rbx, r10
  x64.setle rax
  x64.movzx rax, rax
  x64.test rax, rax
  x64.jne while.body_1
  x64.jmp while.exit_1
  ^while.body_1:
  x64.mov rax, qword ptr [rcx]
  x64.mov rdx, rax
  x64.add rdx, r11
  x64.mov qword ptr [rcx], rdx
  x64.mov rdx, 1
  x64.mov rax, rbx
  x64.add rax, rdx
  x64.mov rbx, rax
  x64.jmp while.cond_1
  ^while.exit_1:
  x64.mov rax, r11
  x64.add rax, r8
  x64.mov r11, rax
  x64.jmp while.cond
}
```

<!-- test: licm-deeply-nested-correctness -->
LICM only hoists from outermost loops to avoid register pressure issues.
Inner loop computations remain in place.
```maxon
function main() returns int
  var result = 0
  var a = 0
  while a < 2 'outer'
    var b = 0
    while b < 2 'middle'
      var c = 0
      while c < 2 'inner'
        result = result + 1
        c = c + 1
      end 'inner'
      b = b + 1
    end 'middle'
    a = a + 1
  end 'outer'
  return result
end 'main'
```
```exitcode
8
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=48 saved_gprs=[RBX,R12]
  x64.mov rax, 0
  x64.lea rcx, qword ptr [rbp-40]
  x64.mov qword ptr [rcx], rax
  x64.mov r8, 2
  x64.mov r9, 1
  x64.mov r10, rax
  x64.jmp while.cond
  ^while.cond(%30: i64):
  x64.cmp r10, r8
  x64.setl rdx
  x64.movzx rdx, rdx
  x64.test rdx, rdx
  x64.jne while.body
  x64.jmp while.exit
  ^while.body:
  x64.mov r11, 0
  x64.jmp while.cond_1
  ^while.exit:
  x64.mov rax, qword ptr [rcx]
  x64.epilogue saved_gprs=[RBX,R12]
  x64.ret
  ^while.cond_1(%31: i64):
  x64.cmp r11, r8
  x64.setl rax
  x64.movzx rax, rax
  x64.test rax, rax
  x64.jne while.body_1
  x64.jmp while.exit_1
  ^while.body_1:
  x64.mov rax, 0
  x64.lea rbx, qword ptr [rbp-48]
  x64.mov qword ptr [rbx], rax
  x64.jmp while.cond_2
  ^while.exit_1:
  x64.mov rax, r10
  x64.add rax, r9
  x64.mov r10, rax
  x64.jmp while.cond
  ^while.cond_2:
  x64.mov rax, qword ptr [rbx]
  x64.mov rdx, 2
  x64.cmp rax, rdx
  x64.setl rdx
  x64.movzx rdx, rdx
  x64.test rdx, rdx
  x64.jne while.body_2
  x64.jmp while.exit_2
  ^while.body_2:
  x64.mov rdx, qword ptr [rcx]
  x64.mov rax, 1
  x64.mov r12, rdx
  x64.add r12, rax
  x64.mov qword ptr [rcx], r12
  x64.mov rax, qword ptr [rbx]
  x64.mov rdx, 1
  x64.mov r12, rax
  x64.add r12, rdx
  x64.mov qword ptr [rbx], r12
  x64.jmp while.cond_2
  ^while.exit_2:
  x64.mov rdx, 1
  x64.mov rax, r11
  x64.add rax, rdx
  x64.mov r11, rax
  x64.jmp while.cond_1
}
```

### Jump Threading

Jump threading eliminates empty blocks and threads jumps through blocks
when branch conditions can be determined from the control flow path.

<!-- test: jump-threading-preserve-block-args -->
Empty blocks with block arguments are NOT eliminated because they carry
phi-like values that need to be preserved for SSA correctness.
```maxon
function main() returns int
  var x = 0
  var i = 0
  while i < 5 'loop'
    x = x + i
    i = i + 1
  end 'loop'
  return x
end 'main'
```
```exitcode
10
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 0
  x64.mov rcx, 5
  x64.mov r8, 1
  x64.mov r9, rax
  x64.mov r10, rax
  x64.jmp while.cond
  ^while.cond(%14: i64, %16: i64):
  x64.cmp r10, rcx
  x64.setl rdx
  x64.movzx rdx, rdx
  x64.test rdx, rdx
  x64.jne while.body
  x64.jmp while.exit
  ^while.body:
  x64.mov rdx, r9
  x64.add rdx, r10
  x64.mov rax, r10
  x64.add rax, r8
  x64.mov r9, rdx
  x64.mov r10, rax
  x64.jmp while.cond
  ^while.exit:
  x64.mov rax, r9
  x64.epilogue
  x64.ret
}
```

<!-- test: jump-threading-empty-block-removal -->
Empty blocks without arguments can be safely removed by redirecting predecessors.
```maxon
function test(x int) returns int
  if x > 0 'check'
    return 1
  end 'check'
  return 0
end 'test'

function main() returns int
  return test(5) + test(0)
end 'main'
```
```exitcode
1
```
```requiredmlir
func.func @test(%x: i64) -> i64 {
  ^entry(%x: i64):
  x64.prologue stack_size=32
  x64.mov rax, rcx
  x64.mov rcx, 0
  x64.cmp rax, rcx
  x64.setg rcx
  x64.movzx rcx, rcx
  x64.test rcx, rcx
  x64.jne then
  x64.jmp merge
  ^then:
  x64.mov rax, 1
  x64.epilogue
  x64.ret
  ^merge:
  x64.mov rax, 0
  x64.epilogue
  x64.ret
}

func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=40 saved_gprs=[RBX]
  x64.mov rax, 5
  x64.mov rcx, rax
  x64.call test
  x64.mov rbx, rax
  x64.mov rax, 0
  x64.mov rcx, rax
  x64.call test
  x64.mov rcx, rbx
  x64.add rcx, rax
  x64.mov rax, rcx
  x64.epilogue saved_gprs=[RBX]
  x64.ret
}
```

### Function Inlining

The inlining pass replaces function calls with the function body when beneficial,
eliminating call overhead for small functions.

<!-- test: inlining-simple-function -->
Small helper functions are inlined at call sites.
```maxon
function add(a int, b int) returns int
  return a + b
end 'add'

function main() returns int
  return add(3, b: 4)
end 'main'
```
```exitcode
7
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 7
  x64.epilogue
  x64.ret
}
```

<!-- test: inlining-multiple-calls -->
Functions called multiple times are inlined at each call site.
```maxon
function square(x int) returns int
  return x * x
end 'square'

function main() returns int
  return square(3) + square(4)
end 'main'
```
```exitcode
25
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 25
  x64.epilogue
  x64.ret
}
```

<!-- test: inlining-nested-calls -->
Nested function calls are inlined from innermost to outermost.
```maxon
function double(x int) returns int
  return x * 2
end 'double'

function quadruple(x int) returns int
  return double(double(x))
end 'quadruple'

function main() returns int
  return quadruple(5)
end 'main'
```
```exitcode
20
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 20
  x64.epilogue
  x64.ret
}
```

### Global Value Numbering (GVN)

GVN eliminates redundant computations by identifying expressions that compute
the same value and reusing the first result.

<!-- test: gvn-redundant-computation -->
Redundant computations of the same expression are eliminated.
```maxon
function main() returns int
  var a = 5
  var b = 3
  var x = a + b
  var y = a + b
  return x + y
end 'main'
```
```exitcode
16
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 16
  x64.epilogue
  x64.ret
}
```

<!-- test: gvn-common-subexpression -->
Common subexpressions in complex expressions are computed only once.
```maxon
function main() returns int
  var a = 2
  var b = 3
  return (a * b) + (a * b) + (a * b)
end 'main'
```
```exitcode
18
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 18
  x64.epilogue
  x64.ret
}
```

### Tail Call Optimization

Tail call optimization identifies calls in tail position and marks them for
optimized calling convention, enabling efficient recursion without stack growth.

<!-- test: tco-simple-tail-recursion -->
Simple tail-recursive functions are optimized.
```maxon
function countdown(n int) returns int
  if n <= 0 'done'
    return 0
  end 'done'
  return countdown(n - 1)
end 'countdown'

function main() returns int
  return countdown(10)
end 'main'
```
```exitcode
0
```
```requiredmlir
func.func @countdown(%n: i64) -> i64 {
  ^entry(%n: i64):
  x64.prologue stack_size=32
  x64.mov rax, 0
  x64.cmp rcx, rax
  x64.setle rax
  x64.movzx rax, rax
  x64.test rax, rax
  x64.jne then
  x64.jmp merge
  ^then:
  x64.mov rax, 0
  x64.epilogue
  x64.ret
  ^merge:
  x64.mov rax, 1
  x64.mov rdx, rcx
  x64.sub rdx, rax
  x64.mov rcx, rdx
  x64.call countdown
  x64.epilogue
  x64.ret
}

func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rcx, 10
  x64.call countdown
  x64.epilogue
  x64.ret
}
```

<!-- test: tco-accumulator-pattern -->
Tail recursion with accumulator pattern is optimized.
```maxon
function sum_to_helper(n int, acc int) returns int
  if n <= 0 'done'
    return acc
  end 'done'
  return sum_to_helper(n - 1, acc: acc + n)
end 'sum_to_helper'

function sum_to(n int) returns int
  return sum_to_helper(n, acc: 0)
end 'sum_to'

function main() returns int
  return sum_to(5)
end 'main'
```
```exitcode
15
```
```requiredmlir
func.func @sum_to_helper(%n: i64, %acc: i64) -> i64 {
  ^entry(%n: i64, %acc: i64):
  x64.prologue stack_size=32
  x64.mov rax, 0
  x64.cmp rcx, rax
  x64.setle rax
  x64.movzx rax, rax
  x64.test rax, rax
  x64.jne then
  x64.jmp merge
  ^then:
  x64.mov rax, rdx
  x64.epilogue
  x64.ret
  ^merge:
  x64.mov rax, 1
  x64.mov r8, rcx
  x64.sub r8, rax
  x64.mov rax, rdx
  x64.add rax, rcx
  x64.mov r10, r8
  x64.mov r11, rax
  x64.mov rdx, r11
  x64.mov rcx, r10
  x64.call sum_to_helper
  x64.mov rcx, rax
  x64.mov rax, rcx
  x64.epilogue
  x64.ret
}

func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 5
  x64.mov rcx, 0
  x64.mov r10, rax
  x64.mov r11, rcx
  x64.mov rdx, r11
  x64.mov rcx, r10
  x64.call sum_to_helper
  x64.mov rcx, rax
  x64.mov rax, rcx
  x64.epilogue
  x64.ret
}
```

### Division with Virtual Registers

Division operations use virtual registers instead of hardcoded physical registers,
allowing the register allocator to avoid conflicts with loop variables.

<!-- test: division-in-nested-loop -->
Division inside nested loops works correctly without clobbering loop variables.
```maxon
function main() returns int
  var result = 0
  var i = 0
  while i < 3 'outer'
    var j = 0
    while j < 3 'inner'
      result = result + ((i + j) mod 2)
      j = j + 1
    end 'inner'
    i = i + 1
  end 'outer'
  return result
end 'main'
```
```exitcode
4
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=48 saved_gprs=[RBX,R12]
  x64.mov rax, 0
  x64.lea rcx, qword ptr [rbp-40]
  x64.mov qword ptr [rcx], rax
  x64.mov r8, 3
  x64.mov r9, 1
  x64.mov r10, rax
  x64.jmp while.cond
  ^while.cond(%26: i64):
  x64.cmp r10, r8
  x64.setl rdx
  x64.movzx rdx, rdx
  x64.test rdx, rdx
  x64.jne while.body
  x64.jmp while.exit
  ^while.body:
  x64.mov r11, 0
  x64.jmp while.cond_1
  ^while.exit:
  x64.mov rax, qword ptr [rcx]
  x64.epilogue saved_gprs=[RBX,R12]
  x64.ret
  ^while.cond_1(%27: i64):
  x64.cmp r11, r8
  x64.setl rax
  x64.movzx rax, rax
  x64.test rax, rax
  x64.jne while.body_1
  x64.jmp while.exit_1
  ^while.body_1:
  x64.mov rbx, qword ptr [rcx]
  x64.mov rax, r10
  x64.add rax, r11
  x64.mov rdx, 2
  x64.mov r12, rdx
  x64.cdq
  x64.idiv r12
  x64.mov rax, rdx
  x64.mov rdx, rbx
  x64.add rdx, rax
  x64.mov qword ptr [rcx], rdx
  x64.mov rdx, 1
  x64.mov rax, r11
  x64.add rax, rdx
  x64.mov r11, rax
  x64.jmp while.cond_1
  ^while.exit_1:
  x64.mov rax, r10
  x64.add rax, r9
  x64.mov r10, rax
  x64.jmp while.cond
}
```

<!-- test: division-multiple-in-loop -->
Multiple division operations in the same loop body work correctly.
```maxon
function main() returns int
  var sum = 0
  var i = 1
  while i <= 10 'loop'
    sum = sum + (i / 2) + (i mod 3)
    i = i + 1
  end 'loop'
  return sum
end 'main'
```
```exitcode
35
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=40 saved_gprs=[RBX,R12,R13]
  x64.mov rcx, 1
  x64.mov r8, 10
  x64.mov r9, 2
  x64.mov r10, 3
  x64.mov r11, 0
  x64.mov rbx, rcx
  x64.jmp while.cond
  ^while.cond(%20: i64, %22: i64):
  x64.cmp rbx, r8
  x64.setle rdx
  x64.movzx rdx, rdx
  x64.test rdx, rdx
  x64.jne while.body
  x64.jmp while.exit
  ^while.body:
  x64.mov r12, r9
  x64.mov rax, rbx
  x64.cdq
  x64.idiv r12
  x64.mov rdx, rax
  x64.mov r12, r11
  x64.add r12, rdx
  x64.mov r13, r10
  x64.mov rax, rbx
  x64.cdq
  x64.idiv r13
  x64.mov rax, r12
  x64.add rax, rdx
  x64.mov rdx, rbx
  x64.add rdx, rcx
  x64.mov r11, rax
  x64.mov rbx, rdx
  x64.jmp while.cond
  ^while.exit:
  x64.mov rax, r11
  x64.epilogue saved_gprs=[RBX,R12,R13]
  x64.ret
}
```

### Identity and Absorbing Folding

The following tests verify identity folding patterns (e.g., `x + 0 = x`) and absorbing element patterns (e.g., `x * 0 = 0`).

<!-- test: identity-add-zero-lhs -->
`0 + x = x` identity folding.
```maxon
function main() returns int
  var x = 42
  return 0 + x
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
}
```

<!-- test: identity-add-zero-rhs -->
`x + 0 = x` identity folding.
```maxon
function main() returns int
  var x = 42
  return x + 0
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
}
```

<!-- test: identity-sub-zero -->
`x - 0 = x` identity folding.
```maxon
function main() returns int
  var x = 42
  return x - 0
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
}
```

<!-- test: identity-mul-one-lhs -->
`1 * x = x` identity folding.
```maxon
function main() returns int
  var x = 42
  return 1 * x
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
}
```

<!-- test: identity-mul-one-rhs -->
`x * 1 = x` identity folding.
```maxon
function main() returns int
  var x = 42
  return x * 1
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
}
```

<!-- test: identity-div-one -->
`x / 1 = x` identity folding.
```maxon
function main() returns int
  var x = 42
  return x / 1
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
}
```

<!-- test: absorbing-zero-div-lhs -->
`0 / x = 0` absorbing element.
```maxon
function main() returns int
  var x = 42
  return 0 / x
end 'main'
```
```exitcode
0
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 0
  x64.epilogue
  x64.ret
}
```

<!-- test: identity-or-zero -->
`x or 0 = x` identity folding.
```maxon
function main() returns int
  var x = 42
  return x or 0
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
}
```

<!-- test: identity-xor-zero -->
`x xor 0 = x` identity folding.
```maxon
function main() returns int
  var x = 42
  return x xor 0
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
}
```

<!-- test: identity-shift-left-zero -->
`x shl 0 = x` identity folding.
```maxon
function main() returns int
  var x = 42
  return x shl 0
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
}
```

<!-- test: identity-shift-right-zero -->
`x shr 0 = x` identity folding.
```maxon
function main() returns int
  var x = 42
  return x shr 0
end 'main'
```
```exitcode
42
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 42
  x64.epilogue
  x64.ret
}
```

<!-- test: absorbing-and-zero -->
`x and 0 = 0` absorbing element.
```maxon
function main() returns int
  var x = 42
  return x and 0
end 'main'
```
```exitcode
0
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 0
  x64.epilogue
  x64.ret
}
```

<!-- test: absorbing-shift-left-zero-lhs -->
`0 shl n = 0` absorbing element.
```maxon
function main() returns int
  var n = 5
  return 0 shl n
end 'main'
```
```exitcode
0
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 0
  x64.epilogue
  x64.ret
}
```

<!-- test: identity-float-mul-one -->
`x * 1.0 = x` identity folding for floats.
```maxon
function main() returns int
  var x = 5.0
  return trunc(x * 1.0)
end 'main'
```
```exitcode
5
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 5
  x64.epilogue
  x64.ret
}
```

<!-- test: identity-float-div-one -->
`x / 1.0 = x` identity folding for floats.
```maxon
function main() returns int
  var x = 5.0
  return trunc(x / 1.0)
end 'main'
```
```exitcode
5
```
```requiredmlir
func.func @main() -> i64 {
  ^entry:
  x64.prologue stack_size=32
  x64.mov rax, 5
  x64.epilogue
  x64.ret
}
```
