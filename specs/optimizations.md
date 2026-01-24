---
feature: optimizations
status: stable
keywords: optimization, constant folding, dead code
category: compiler
---
# Compiler Optimizations

## Documentation

The Maxon compiler includes optimization passes that improve code efficiency:

### HIR Optimizations (High-level)

1. **Constant Folding** - Evaluates constant expressions at compile time
2. **Dead Code Elimination** - Removes unused variables and computations
3. **Dead Function Elimination** - Removes functions never called from main
4. **Peephole Optimizations** - Strength reduction (e.g., `x * 2` → `x << 1`)

### LIR Optimizations (Low-level)

1. **Common Subexpression Elimination (CSE)** - Reuses computed values
2. **Copy Propagation** - Eliminates redundant moves
3. **Dead Code Elimination** - Removes unused instructions
4. **Peephole Optimizations** - Identity elimination and strength reduction

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
```requiredhir
fn main() -> int {
entry:
  %2 = const 30
  ret %2
}
```
```requiredlir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 30
  ret v0
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
```requiredhir
fn main() -> int {
entry:
  %4 = const 16
  ret %4
}
```
```requiredlir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 16
  ret v0
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
```requiredhir
fn main() -> int {
entry:
  %2 = const 75
  ret %2
}
```
```requiredlir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 75
  ret v0
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
```requiredhir
fn main() -> int {
entry:
  %2 = const 42
  ret %2
}
```
```requiredlir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 42
  ret v0
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
```requiredhir
fn main() -> int {
entry:
  %2 = const 25
  ret %2
}
```
```requiredlir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 25
  ret v0
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
```requiredhir
fn main() -> int {
entry:
  %2 = const 2
  ret %2
}
```
```requiredlir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 2
  ret v0
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
```requiredhir
fn main() -> int {
entry:
  %0 = alloca int
  %1 = const 42
  store %0, %1 : int
  %4 = const 0
  ret %4
}
```
```requiredlir
fn main() -> I64 [stack=16] {
entry:
  v0 = addressof [rbp-8]
  v1 = mov 42
  store v0, v1 (8)
  v2 = mov 0
  ret v2
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
```requiredhir
fn main() -> int {
entry:
  %0 = const 42
  ret %0
}
```
```requiredlir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 42
  ret v0
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
```requiredhir
fn main() -> int {
entry:
  %0 = alloca int
  %1 = const 10
  store %0, %1 : int
  %2 = load %0
  %5 = const 1
  %4 = shl %2, %5
  ret %4
}
```
```requiredlir
fn main() -> I64 [stack=16] {
entry:
  v0 = addressof [rbp-8]
  v1 = mov 10
  store v0, v1 (8)
  v2 = load v0 (8)
  v3 = mov 1
  v4 = shl v2, v3
  ret v4
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
```requiredhir
fn main() -> int {
entry:
  %0 = alloca int
  %1 = const 7
  store %0, %1 : int
  %2 = load %0
  %5 = const 2
  %4 = shl %2, %5
  ret %4
}
```
```requiredlir
fn main() -> I64 [stack=16] {
entry:
  v0 = addressof [rbp-8]
  v1 = mov 7
  store v0, v1 (8)
  v2 = load v0 (8)
  v3 = mov 2
  v4 = shl v2, v3
  ret v4
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
```requiredhir
fn main() -> int {
entry:
  %0 = alloca int
  %1 = const 5
  store %0, %1 : int
  %2 = load %0
  %5 = const 3
  %4 = shl %2, %5
  ret %4
}
```
```requiredlir
fn main() -> I64 [stack=16] {
entry:
  v0 = addressof [rbp-8]
  v1 = mov 5
  store v0, v1 (8)
  v2 = load v0 (8)
  v3 = mov 3
  v4 = shl v2, v3
  ret v4
}
```

<!-- test: complex-constant-expression -->
```maxon
function main() returns int
    return ((2 + 3) * 4) - 10
end 'main'
```
```exitcode
10
```
```requiredhir
fn main() -> int {
entry:
  %6 = const 10
  ret %6
}
```
```requiredlir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 10
  ret v0
}
```
