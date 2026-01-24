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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 30
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 16
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 75
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 42
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 25
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 2
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 0
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 42
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 20
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 28
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 40
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 15
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
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
module {
  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 20
      x86.mov rax, rax
      x86.epilogue
      x86.ret
  }
}
```

<!-- test: store-to-load-global -->
Store-to-load forwarding propagates constants through global variable stores and loads.
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
module {
  memref.global @global.g : i64 = 0 : i64

  func.func @main() -> i64 {
    ^entry:
      x86.prologue stack_size=32
      x86.mov rax, 5
      x86.lea_global rcx, @global.g
      x86.mov qword ptr [rcx], rax
      x86.mov rdx, 20
      x86.mov rax, rdx
      x86.epilogue
      x86.ret
  }
}
```
