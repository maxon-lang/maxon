---
feature: arithmetic
status: stable
keywords: arithmetic, operators, math
category: operators
---
# Arithmetic Operators

## Documentation

Maxon supports basic arithmetic operators for integers:

- `+` addition
- `-` subtraction
- `*` multiplication
- `/` division
- `mod` modulo

## Tests

<!-- test: addition -->
```maxon
function main() returns int
    return 10 + 5
end 'main'
```
```exitcode
15
```
```hir
fn main() -> int {
entry:
  %0 = const 10
  %1 = const 5
  %2 = add %0, %1
  ret %2
}
```
```lir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 10
  v1 = mov 5
  v2 = add v0, v1
  ret v2
}
```

<!-- test: subtraction -->
```maxon
function main() returns int
    return 20-8
end 'main'
```
```exitcode
12
```
```hir
fn main() -> int {
entry:
  %0 = const 20
  %1 = const 8
  %2 = sub %0, %1
  ret %2
}
```
```lir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 20
  v1 = mov 8
  v2 = sub v0, v1
  ret v2
}
```

<!-- test: multiplication -->
```maxon
function main() returns int
    return 6 * 7
end 'main'
```
```exitcode
42
```
```hir
fn main() -> int {
entry:
  %0 = const 6
  %1 = const 7
  %2 = mul %0, %1
  ret %2
}
```
```lir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 6
  v1 = mov 7
  v2 = imul v0, v1
  ret v2
}
```

<!-- test: division -->
```maxon
function main() returns int
    return 100 / 4
end 'main'
```
```exitcode
25
```
```hir
fn main() -> int {
entry:
  %0 = const 100
  %1 = const 4
  %2 = div %0, %1
  ret %2
}
```
```lir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 100
  v1 = mov 4
  v2 = idiv v0, v1
  ret v2
}
```

<!-- test: modulo -->
```maxon
function main() returns int
    return 17 mod 5
end 'main'
```
```exitcode
2
```
```hir
fn main() -> int {
entry:
  %0 = const 17
  %1 = const 5
  %2 = mod %0, %1
  ret %2
}
```
```lir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 17
  v1 = mov 5
  v2 = mod v0, v1
  ret v2
}
```

<!-- test: complex-expression -->
```maxon
function main() returns int
    return 10 + 5 * 2
end 'main'
```
```exitcode
20
```
```hir
fn main() -> int {
entry:
  %0 = const 10
  %1 = const 5
  %2 = const 2
  %3 = mul %1, %2
  %4 = add %0, %3
  ret %4
}
```
```lir
fn main() -> I64 [stack=0] {
entry:
  v0 = mov 10
  v1 = mov 5
  v2 = mov 2
  v3 = imul v1, v2
  v4 = add v0, v3
  ret v4
}
```
