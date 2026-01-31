---
feature: register-allocator
status: experimental
keywords: [regalloc, registers, spilling, codegen]
category: dev
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
    %0 = maxon.literal {value = 42 : i64}
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
    x86.mov eax, 42
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
    %0 = maxon.literal {value = 99 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.return %0
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %1 = arith.constant {value = 99 : i64}
    memref.store %1, x
    func.return %1
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 99
    x86.mov [rbp-8], eax
    x86.epilogue
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
    %0 = maxon.literal {value = 30 : i64}
    %1 = maxon.literal {value = 12 : i64}
    %2 = maxon.binop %0, %1 {op = add} {kind = i64}
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
    x86.mov eax, 30
    x86.mov ecx, 12
    x86.add eax, ecx
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 30 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 12 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %0, %1 {op = add} {kind = i64}
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %3 = arith.constant {value = 30 : i64}
    memref.store %3, a
    %4 = arith.constant {value = 12 : i64}
    memref.store %4, b
    %5 = arith.addi %3, %4
    func.return %5
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 30
    x86.mov [rbp-8], eax
    x86.mov ecx, 12
    x86.mov [rbp-16], ecx
    x86.add eax, ecx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 21 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.binop %0, %0 {op = add} {kind = i64}
    maxon.return %1
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %2 = arith.constant {value = 21 : i64}
    memref.store %2, x
    %3 = arith.addi %2, %2
    func.return %3
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 21
    x86.mov [rbp-8], eax
    x86.add eax, eax
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 5 : i64}
    %2 = maxon.binop %0, %1 {op = add} {kind = i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 7 : i64}
    %4 = maxon.binop %2, %3 {op = add} {kind = i64}
    maxon.assign %4 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 20 : i64}
    %6 = maxon.binop %4, %5 {op = add} {kind = i64}
    maxon.assign %6 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.return %6
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %7 = arith.constant {value = 10 : i64}
    memref.store %7, a
    %8 = arith.constant {value = 5 : i64}
    %9 = arith.addi %7, %8
    memref.store %9, b
    %10 = arith.constant {value = 7 : i64}
    %11 = arith.addi %9, %10
    memref.store %11, c
    %12 = arith.constant {value = 20 : i64}
    %13 = arith.addi %11, %12
    memref.store %13, d
    func.return %13
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 10
    x86.mov [rbp-8], eax
    x86.mov ecx, 5
    x86.add eax, ecx
    x86.mov [rbp-16], eax
    x86.mov edx, 7
    x86.add eax, edx
    x86.mov [rbp-24], eax
    x86.mov ebx, 20
    x86.add eax, ebx
    x86.mov [rbp-32], eax
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 80 : i64}
    %2 = maxon.binop %0, %1 {op = sub} {kind = i64}
    maxon.assign %2 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 22 : i64}
    maxon.assign %3 {var = x} {kind = i64} {mut = 1 : i1}
    %4 = maxon.binop %3, %2 {op = add} {kind = i64}
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %5 = arith.constant {value = 100 : i64}
    memref.store %5, x
    %6 = arith.constant {value = 80 : i64}
    %7 = arith.subi %5, %6
    memref.store %7, y
    %8 = arith.constant {value = 22 : i64}
    memref.store %8, x
    %9 = arith.addi %8, %7
    func.return %9
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 100
    x86.mov [rbp-8], eax
    x86.mov ecx, 80
    x86.sub eax, ecx
    x86.mov [rbp-16], eax
    x86.mov edx, 22
    x86.mov [rbp-8], edx
    x86.add edx, eax
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 4 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 5 : i64}
    maxon.assign %4 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 6 : i64}
    maxon.assign %5 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.binop %0, %1 {op = add} {kind = i64}
    %7 = maxon.binop %6, %2 {op = add} {kind = i64}
    %8 = maxon.binop %7, %3 {op = add} {kind = i64}
    %9 = maxon.binop %8, %4 {op = add} {kind = i64}
    %10 = maxon.binop %9, %5 {op = add} {kind = i64}
    maxon.return %10
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %11 = arith.constant {value = 1 : i64}
    memref.store %11, a
    %12 = arith.constant {value = 2 : i64}
    memref.store %12, b
    %13 = arith.constant {value = 3 : i64}
    memref.store %13, c
    %14 = arith.constant {value = 4 : i64}
    memref.store %14, d
    %15 = arith.constant {value = 5 : i64}
    memref.store %15, e
    %16 = arith.constant {value = 6 : i64}
    memref.store %16, f
    %17 = arith.addi %11, %12
    %18 = arith.addi %17, %13
    %19 = arith.addi %18, %14
    %20 = arith.addi %19, %15
    %21 = arith.addi %20, %16
    func.return %21
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.mov eax, 1
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov [rbp-16], ecx
    x86.mov edx, 3
    x86.mov [rbp-24], edx
    x86.mov ebx, 4
    x86.mov [rbp-32], ebx
    x86.mov esi, 5
    x86.mov [rbp-40], esi
    x86.mov edi, 6
    x86.mov [rbp-48], edi
    x86.add eax, ecx
    x86.add eax, edx
    x86.add eax, ebx
    x86.add eax, esi
    x86.add eax, edi
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 4 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 5 : i64}
    maxon.assign %4 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 6 : i64}
    maxon.assign %5 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 7 : i64}
    maxon.assign %6 {var = g} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 8 : i64}
    maxon.assign %7 {var = h} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 9 : i64}
    maxon.assign %8 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.literal {value = 10 : i64}
    maxon.assign %9 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.binop %0, %1 {op = add} {kind = i64}
    %11 = maxon.binop %10, %2 {op = add} {kind = i64}
    %12 = maxon.binop %11, %3 {op = add} {kind = i64}
    %13 = maxon.binop %12, %4 {op = add} {kind = i64}
    %14 = maxon.binop %13, %5 {op = add} {kind = i64}
    %15 = maxon.binop %14, %6 {op = add} {kind = i64}
    %16 = maxon.binop %15, %7 {op = add} {kind = i64}
    %17 = maxon.binop %16, %8 {op = add} {kind = i64}
    %18 = maxon.binop %17, %9 {op = add} {kind = i64}
    maxon.return %18
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %19 = arith.constant {value = 1 : i64}
    memref.store %19, a
    %20 = arith.constant {value = 2 : i64}
    memref.store %20, b
    %21 = arith.constant {value = 3 : i64}
    memref.store %21, c
    %22 = arith.constant {value = 4 : i64}
    memref.store %22, d
    %23 = arith.constant {value = 5 : i64}
    memref.store %23, e
    %24 = arith.constant {value = 6 : i64}
    memref.store %24, f
    %25 = arith.constant {value = 7 : i64}
    memref.store %25, g
    %26 = arith.constant {value = 8 : i64}
    memref.store %26, h
    %27 = arith.constant {value = 9 : i64}
    memref.store %27, i
    %28 = arith.constant {value = 10 : i64}
    memref.store %28, j
    %29 = arith.addi %19, %20
    %30 = arith.addi %29, %21
    %31 = arith.addi %30, %22
    %32 = arith.addi %31, %23
    %33 = arith.addi %32, %24
    %34 = arith.addi %33, %25
    %35 = arith.addi %34, %26
    %36 = arith.addi %35, %27
    %37 = arith.addi %36, %28
    func.return %37
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=80
    x86.mov eax, 1
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov [rbp-16], ecx
    x86.mov edx, 3
    x86.mov [rbp-24], edx
    x86.mov ebx, 4
    x86.mov [rbp-32], ebx
    x86.mov esi, 5
    x86.mov [rbp-40], esi
    x86.mov edi, 6
    x86.mov [rbp-48], edi
    x86.mov r8, 7
    x86.mov [rbp-56], r8
    x86.mov r9, 8
    x86.mov [rbp-64], r9
    x86.mov eax, 9
    x86.mov [rbp-72], eax
    x86.mov ecx, 10
    x86.mov [rbp-80], ecx
    x86.mov edx, [rbp-16]
    x86.mov ebx, [rbp-8]
    x86.add ebx, edx
    x86.mov edx, [rbp-24]
    x86.add ebx, edx
    x86.mov edx, [rbp-32]
    x86.add ebx, edx
    x86.add ebx, esi
    x86.add ebx, edi
    x86.add ebx, r8
    x86.add ebx, r9
    x86.add ebx, eax
    x86.add ebx, ecx
    x86.mov eax, ebx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 4 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 5 : i64}
    maxon.assign %4 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 6 : i64}
    maxon.assign %5 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 7 : i64}
    maxon.assign %6 {var = g} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 8 : i64}
    maxon.assign %7 {var = h} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 9 : i64}
    maxon.assign %8 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.literal {value = 10 : i64}
    maxon.assign %9 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 11 : i64}
    maxon.assign %10 {var = k} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %11 = maxon.literal {value = 12 : i64}
    maxon.assign %11 {var = l} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 13 : i64}
    maxon.assign %12 {var = m} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.literal {value = 14 : i64}
    maxon.assign %13 {var = n} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %14 = maxon.literal {value = 15 : i64}
    maxon.assign %14 {var = o} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 16 : i64}
    maxon.assign %15 {var = p} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.binop %0, %1 {op = add} {kind = i64}
    %17 = maxon.binop %16, %2 {op = add} {kind = i64}
    %18 = maxon.binop %17, %3 {op = add} {kind = i64}
    %19 = maxon.binop %18, %4 {op = add} {kind = i64}
    %20 = maxon.binop %19, %5 {op = add} {kind = i64}
    %21 = maxon.binop %20, %6 {op = add} {kind = i64}
    %22 = maxon.binop %21, %7 {op = add} {kind = i64}
    %23 = maxon.binop %22, %8 {op = add} {kind = i64}
    %24 = maxon.binop %23, %9 {op = add} {kind = i64}
    %25 = maxon.binop %24, %10 {op = add} {kind = i64}
    %26 = maxon.binop %25, %11 {op = add} {kind = i64}
    %27 = maxon.binop %26, %12 {op = add} {kind = i64}
    %28 = maxon.binop %27, %13 {op = add} {kind = i64}
    %29 = maxon.binop %28, %14 {op = add} {kind = i64}
    %30 = maxon.binop %29, %15 {op = add} {kind = i64}
    %31 = maxon.literal {value = 256 : i64}
    %32 = maxon.binop %30, %31 {op = mod} {kind = i64}
    maxon.return %32
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %33 = arith.constant {value = 1 : i64}
    memref.store %33, a
    %34 = arith.constant {value = 2 : i64}
    memref.store %34, b
    %35 = arith.constant {value = 3 : i64}
    memref.store %35, c
    %36 = arith.constant {value = 4 : i64}
    memref.store %36, d
    %37 = arith.constant {value = 5 : i64}
    memref.store %37, e
    %38 = arith.constant {value = 6 : i64}
    memref.store %38, f
    %39 = arith.constant {value = 7 : i64}
    memref.store %39, g
    %40 = arith.constant {value = 8 : i64}
    memref.store %40, h
    %41 = arith.constant {value = 9 : i64}
    memref.store %41, i
    %42 = arith.constant {value = 10 : i64}
    memref.store %42, j
    %43 = arith.constant {value = 11 : i64}
    memref.store %43, k
    %44 = arith.constant {value = 12 : i64}
    memref.store %44, l
    %45 = arith.constant {value = 13 : i64}
    memref.store %45, m
    %46 = arith.constant {value = 14 : i64}
    memref.store %46, n
    %47 = arith.constant {value = 15 : i64}
    memref.store %47, o
    %48 = arith.constant {value = 16 : i64}
    memref.store %48, p
    %49 = arith.addi %33, %34
    %50 = arith.addi %49, %35
    %51 = arith.addi %50, %36
    %52 = arith.addi %51, %37
    %53 = arith.addi %52, %38
    %54 = arith.addi %53, %39
    %55 = arith.addi %54, %40
    %56 = arith.addi %55, %41
    %57 = arith.addi %56, %42
    %58 = arith.addi %57, %43
    %59 = arith.addi %58, %44
    %60 = arith.addi %59, %45
    %61 = arith.addi %60, %46
    %62 = arith.addi %61, %47
    %63 = arith.addi %62, %48
    %64 = arith.constant {value = 256 : i64}
    %65 = arith.remsi %63, %64
    func.return %65
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=128
    x86.mov eax, 1
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov [rbp-16], ecx
    x86.mov edx, 3
    x86.mov [rbp-24], edx
    x86.mov ebx, 4
    x86.mov [rbp-32], ebx
    x86.mov esi, 5
    x86.mov [rbp-40], esi
    x86.mov edi, 6
    x86.mov [rbp-48], edi
    x86.mov r8, 7
    x86.mov [rbp-56], r8
    x86.mov r9, 8
    x86.mov [rbp-64], r9
    x86.mov eax, 9
    x86.mov [rbp-72], eax
    x86.mov ecx, 10
    x86.mov [rbp-80], ecx
    x86.mov edx, 11
    x86.mov [rbp-88], edx
    x86.mov ebx, 12
    x86.mov [rbp-96], ebx
    x86.mov esi, 13
    x86.mov [rbp-104], esi
    x86.mov edi, 14
    x86.mov [rbp-112], edi
    x86.mov r8, 15
    x86.mov [rbp-120], r8
    x86.mov r9, 16
    x86.mov [rbp-128], r9
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov eax, [rbp-24]
    x86.add ecx, eax
    x86.mov eax, [rbp-32]
    x86.add ecx, eax
    x86.mov eax, [rbp-40]
    x86.add ecx, eax
    x86.mov eax, [rbp-48]
    x86.add ecx, eax
    x86.mov eax, [rbp-56]
    x86.add ecx, eax
    x86.mov eax, [rbp-64]
    x86.add ecx, eax
    x86.mov eax, [rbp-72]
    x86.add ecx, eax
    x86.mov eax, [rbp-80]
    x86.add ecx, eax
    x86.add ecx, edx
    x86.add ecx, ebx
    x86.add ecx, esi
    x86.add ecx, edi
    x86.add ecx, r8
    x86.add ecx, r9
    x86.mov eax, 256
    x86.mov ebx, eax
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 4 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 5 : i64}
    maxon.assign %4 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 6 : i64}
    maxon.assign %5 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 7 : i64}
    maxon.assign %6 {var = g} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 8 : i64}
    maxon.assign %7 {var = h} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 9 : i64}
    maxon.assign %8 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.literal {value = 10 : i64}
    maxon.assign %9 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 11 : i64}
    maxon.assign %10 {var = k} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %11 = maxon.literal {value = 12 : i64}
    maxon.assign %11 {var = l} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 13 : i64}
    maxon.assign %12 {var = m} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.literal {value = 14 : i64}
    maxon.assign %13 {var = n} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %14 = maxon.literal {value = 15 : i64}
    maxon.assign %14 {var = o} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 16 : i64}
    maxon.assign %15 {var = p} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 17 : i64}
    maxon.assign %16 {var = q} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.literal {value = 18 : i64}
    maxon.assign %17 {var = r} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.literal {value = 19 : i64}
    maxon.assign %18 {var = s} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %19 = maxon.literal {value = 20 : i64}
    maxon.assign %19 {var = t} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.binop %0, %1 {op = add} {kind = i64}
    %21 = maxon.binop %20, %2 {op = add} {kind = i64}
    %22 = maxon.binop %21, %3 {op = add} {kind = i64}
    %23 = maxon.binop %22, %4 {op = add} {kind = i64}
    %24 = maxon.binop %23, %5 {op = add} {kind = i64}
    %25 = maxon.binop %24, %6 {op = add} {kind = i64}
    %26 = maxon.binop %25, %7 {op = add} {kind = i64}
    %27 = maxon.binop %26, %8 {op = add} {kind = i64}
    %28 = maxon.binop %27, %9 {op = add} {kind = i64}
    %29 = maxon.binop %28, %10 {op = add} {kind = i64}
    %30 = maxon.binop %29, %11 {op = add} {kind = i64}
    %31 = maxon.binop %30, %12 {op = add} {kind = i64}
    %32 = maxon.binop %31, %13 {op = add} {kind = i64}
    %33 = maxon.binop %32, %14 {op = add} {kind = i64}
    %34 = maxon.binop %33, %15 {op = add} {kind = i64}
    %35 = maxon.binop %34, %16 {op = add} {kind = i64}
    %36 = maxon.binop %35, %17 {op = add} {kind = i64}
    %37 = maxon.binop %36, %18 {op = add} {kind = i64}
    %38 = maxon.binop %37, %19 {op = add} {kind = i64}
    %39 = maxon.literal {value = 256 : i64}
    %40 = maxon.binop %38, %39 {op = mod} {kind = i64}
    maxon.return %40
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %41 = arith.constant {value = 1 : i64}
    memref.store %41, a
    %42 = arith.constant {value = 2 : i64}
    memref.store %42, b
    %43 = arith.constant {value = 3 : i64}
    memref.store %43, c
    %44 = arith.constant {value = 4 : i64}
    memref.store %44, d
    %45 = arith.constant {value = 5 : i64}
    memref.store %45, e
    %46 = arith.constant {value = 6 : i64}
    memref.store %46, f
    %47 = arith.constant {value = 7 : i64}
    memref.store %47, g
    %48 = arith.constant {value = 8 : i64}
    memref.store %48, h
    %49 = arith.constant {value = 9 : i64}
    memref.store %49, i
    %50 = arith.constant {value = 10 : i64}
    memref.store %50, j
    %51 = arith.constant {value = 11 : i64}
    memref.store %51, k
    %52 = arith.constant {value = 12 : i64}
    memref.store %52, l
    %53 = arith.constant {value = 13 : i64}
    memref.store %53, m
    %54 = arith.constant {value = 14 : i64}
    memref.store %54, n
    %55 = arith.constant {value = 15 : i64}
    memref.store %55, o
    %56 = arith.constant {value = 16 : i64}
    memref.store %56, p
    %57 = arith.constant {value = 17 : i64}
    memref.store %57, q
    %58 = arith.constant {value = 18 : i64}
    memref.store %58, r
    %59 = arith.constant {value = 19 : i64}
    memref.store %59, s
    %60 = arith.constant {value = 20 : i64}
    memref.store %60, t
    %61 = arith.addi %41, %42
    %62 = arith.addi %61, %43
    %63 = arith.addi %62, %44
    %64 = arith.addi %63, %45
    %65 = arith.addi %64, %46
    %66 = arith.addi %65, %47
    %67 = arith.addi %66, %48
    %68 = arith.addi %67, %49
    %69 = arith.addi %68, %50
    %70 = arith.addi %69, %51
    %71 = arith.addi %70, %52
    %72 = arith.addi %71, %53
    %73 = arith.addi %72, %54
    %74 = arith.addi %73, %55
    %75 = arith.addi %74, %56
    %76 = arith.addi %75, %57
    %77 = arith.addi %76, %58
    %78 = arith.addi %77, %59
    %79 = arith.addi %78, %60
    %80 = arith.constant {value = 256 : i64}
    %81 = arith.remsi %79, %80
    func.return %81
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=160
    x86.mov eax, 1
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov [rbp-16], ecx
    x86.mov edx, 3
    x86.mov [rbp-24], edx
    x86.mov ebx, 4
    x86.mov [rbp-32], ebx
    x86.mov esi, 5
    x86.mov [rbp-40], esi
    x86.mov edi, 6
    x86.mov [rbp-48], edi
    x86.mov r8, 7
    x86.mov [rbp-56], r8
    x86.mov r9, 8
    x86.mov [rbp-64], r9
    x86.mov eax, 9
    x86.mov [rbp-72], eax
    x86.mov ecx, 10
    x86.mov [rbp-80], ecx
    x86.mov edx, 11
    x86.mov [rbp-88], edx
    x86.mov ebx, 12
    x86.mov [rbp-96], ebx
    x86.mov esi, 13
    x86.mov [rbp-104], esi
    x86.mov edi, 14
    x86.mov [rbp-112], edi
    x86.mov r8, 15
    x86.mov [rbp-120], r8
    x86.mov r9, 16
    x86.mov [rbp-128], r9
    x86.mov eax, 17
    x86.mov [rbp-136], eax
    x86.mov ecx, 18
    x86.mov [rbp-144], ecx
    x86.mov edx, 19
    x86.mov [rbp-152], edx
    x86.mov ebx, 20
    x86.mov [rbp-160], ebx
    x86.mov esi, [rbp-16]
    x86.mov edi, [rbp-8]
    x86.add edi, esi
    x86.mov esi, [rbp-24]
    x86.add edi, esi
    x86.mov esi, [rbp-32]
    x86.add edi, esi
    x86.mov esi, [rbp-40]
    x86.add edi, esi
    x86.mov esi, [rbp-48]
    x86.add edi, esi
    x86.mov esi, [rbp-56]
    x86.add edi, esi
    x86.mov esi, [rbp-64]
    x86.add edi, esi
    x86.mov esi, [rbp-72]
    x86.add edi, esi
    x86.mov esi, [rbp-80]
    x86.add edi, esi
    x86.mov esi, [rbp-88]
    x86.add edi, esi
    x86.mov esi, [rbp-96]
    x86.add edi, esi
    x86.mov esi, [rbp-104]
    x86.add edi, esi
    x86.mov esi, [rbp-112]
    x86.add edi, esi
    x86.add edi, r8
    x86.add edi, r9
    x86.add edi, eax
    x86.add edi, ecx
    x86.add edi, edx
    x86.add edi, ebx
    x86.mov eax, 256
    x86.mov ecx, eax
    x86.mov eax, edi
    x86.cqo
    x86.idiv ecx
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 20 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %0, %1 {op = add} {kind = i64}
    maxon.assign %2 {var = ab} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 30 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 40 : i64}
    maxon.assign %4 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %3, %4 {op = add} {kind = i64}
    maxon.assign %5 {var = cd} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 50 : i64}
    maxon.assign %6 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 60 : i64}
    maxon.assign %7 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.binop %6, %7 {op = add} {kind = i64}
    maxon.assign %8 {var = ef} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.binop %2, %5 {op = add} {kind = i64}
    %10 = maxon.binop %9, %8 {op = add} {kind = i64}
    maxon.assign %10 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %11 = maxon.literal {value = 256 : i64}
    %12 = maxon.binop %10, %11 {op = mod} {kind = i64}
    maxon.return %12
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %13 = arith.constant {value = 10 : i64}
    memref.store %13, a
    %14 = arith.constant {value = 20 : i64}
    memref.store %14, b
    %15 = arith.addi %13, %14
    memref.store %15, ab
    %16 = arith.constant {value = 30 : i64}
    memref.store %16, c
    %17 = arith.constant {value = 40 : i64}
    memref.store %17, d
    %18 = arith.addi %16, %17
    memref.store %18, cd
    %19 = arith.constant {value = 50 : i64}
    memref.store %19, e
    %20 = arith.constant {value = 60 : i64}
    memref.store %20, f
    %21 = arith.addi %19, %20
    memref.store %21, ef
    %22 = arith.addi %15, %18
    %23 = arith.addi %22, %21
    memref.store %23, result
    %24 = arith.constant {value = 256 : i64}
    %25 = arith.remsi %23, %24
    func.return %25
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=80
    x86.mov eax, 10
    x86.mov [rbp-8], eax
    x86.mov ecx, 20
    x86.mov [rbp-16], ecx
    x86.add eax, ecx
    x86.mov [rbp-24], eax
    x86.mov edx, 30
    x86.mov [rbp-32], edx
    x86.mov ebx, 40
    x86.mov [rbp-40], ebx
    x86.add edx, ebx
    x86.mov [rbp-48], edx
    x86.mov esi, 50
    x86.mov [rbp-56], esi
    x86.mov edi, 60
    x86.mov [rbp-64], edi
    x86.add esi, edi
    x86.mov [rbp-72], esi
    x86.add eax, edx
    x86.add eax, esi
    x86.mov [rbp-80], eax
    x86.mov r8, 256
    x86.cqo
    x86.idiv r8
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.assign %0 {var = sum1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = sum2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = sum3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 10 : i64}
    %4 = maxon.binop %0, %3 {op = add} {kind = i64}
    maxon.assign %4 {var = sum1} {kind = i64} {mut = 1 : i1}
    %5 = maxon.literal {value = 20 : i64}
    %6 = maxon.binop %1, %5 {op = add} {kind = i64}
    maxon.assign %6 {var = sum2} {kind = i64} {mut = 1 : i1}
    %7 = maxon.literal {value = 30 : i64}
    %8 = maxon.binop %2, %7 {op = add} {kind = i64}
    maxon.assign %8 {var = sum3} {kind = i64} {mut = 1 : i1}
    %9 = maxon.literal {value = 5 : i64}
    %10 = maxon.binop %4, %9 {op = add} {kind = i64}
    maxon.assign %10 {var = sum1} {kind = i64} {mut = 1 : i1}
    %11 = maxon.literal {value = 10 : i64}
    %12 = maxon.binop %6, %11 {op = add} {kind = i64}
    maxon.assign %12 {var = sum2} {kind = i64} {mut = 1 : i1}
    %13 = maxon.literal {value = 15 : i64}
    %14 = maxon.binop %8, %13 {op = add} {kind = i64}
    maxon.assign %14 {var = sum3} {kind = i64} {mut = 1 : i1}
    %15 = maxon.binop %10, %12 {op = add} {kind = i64}
    %16 = maxon.binop %15, %14 {op = add} {kind = i64}
    maxon.return %16
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %17 = arith.constant {value = 0 : i64}
    memref.store %17, sum1
    %18 = arith.constant {value = 0 : i64}
    memref.store %18, sum2
    %19 = arith.constant {value = 0 : i64}
    memref.store %19, sum3
    %20 = arith.constant {value = 10 : i64}
    %21 = arith.addi %17, %20
    memref.store %21, sum1
    %22 = arith.constant {value = 20 : i64}
    %23 = arith.addi %18, %22
    memref.store %23, sum2
    %24 = arith.constant {value = 30 : i64}
    %25 = arith.addi %19, %24
    memref.store %25, sum3
    %26 = arith.constant {value = 5 : i64}
    %27 = arith.addi %21, %26
    memref.store %27, sum1
    %28 = arith.constant {value = 10 : i64}
    %29 = arith.addi %23, %28
    memref.store %29, sum2
    %30 = arith.constant {value = 15 : i64}
    %31 = arith.addi %25, %30
    memref.store %31, sum3
    %32 = arith.addi %27, %29
    %33 = arith.addi %32, %31
    func.return %33
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 0
    x86.mov [rbp-8], eax
    x86.mov ecx, 0
    x86.mov [rbp-16], ecx
    x86.mov edx, 0
    x86.mov [rbp-24], edx
    x86.mov ebx, 10
    x86.add eax, ebx
    x86.mov [rbp-8], eax
    x86.mov esi, 20
    x86.add ecx, esi
    x86.mov [rbp-16], ecx
    x86.mov edi, 30
    x86.add edx, edi
    x86.mov [rbp-24], edx
    x86.mov r8, 5
    x86.add eax, r8
    x86.mov [rbp-8], eax
    x86.mov r9, 10
    x86.add ecx, r9
    x86.mov [rbp-16], ecx
    x86.mov ebx, 15
    x86.add edx, ebx
    x86.mov [rbp-24], edx
    x86.add eax, ecx
    x86.add eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @getForty() -> i64 {
  entry:
    %0 = maxon.literal {value = 40 : i64}
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.call @getForty
    maxon.assign %2 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add} {kind = i64}
    maxon.return %3
  }
}
=== standard
module {
  func @getForty() -> i64 {
  entry:
    %4 = arith.constant {value = 40 : i64}
    func.return %4
  }
  func @main() -> i64 {
  entry:
    %5 = arith.constant {value = 2 : i64}
    memref.store %5, x
    %6 = func.call @getForty
    memref.store %6, y
    %7 = arith.addi %5, %6
    func.return %7
  }
}
=== x86
module {
  func @getForty() -> i64 {
  entry:
    x86.mov eax, 40
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 2
    x86.mov [rbp-8], eax
    x86.call getForty
    x86.mov [rbp-16], eax
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov eax, ecx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @getTen() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.return %0
  }
  func @getTwo() -> i64 {
  entry:
    %1 = maxon.literal {value = 2 : i64}
    maxon.return %1
  }
  func @main() -> i64 {
  entry:
    %2 = maxon.literal {value = 5 : i64}
    maxon.assign %2 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.call @getTen
    maxon.assign %3 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 7 : i64}
    maxon.assign %4 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.call @getTwo
    maxon.assign %5 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.binop %2, %3 {op = add} {kind = i64}
    %7 = maxon.binop %6, %4 {op = add} {kind = i64}
    %8 = maxon.binop %7, %5 {op = add} {kind = i64}
    maxon.return %8
  }
}
=== standard
module {
  func @getTen() -> i64 {
  entry:
    %9 = arith.constant {value = 10 : i64}
    func.return %9
  }
  func @getTwo() -> i64 {
  entry:
    %10 = arith.constant {value = 2 : i64}
    func.return %10
  }
  func @main() -> i64 {
  entry:
    %11 = arith.constant {value = 5 : i64}
    memref.store %11, a
    %12 = func.call @getTen
    memref.store %12, b
    %13 = arith.constant {value = 7 : i64}
    memref.store %13, c
    %14 = func.call @getTwo
    memref.store %14, d
    %15 = arith.addi %11, %12
    %16 = arith.addi %15, %13
    %17 = arith.addi %16, %14
    func.return %17
  }
}
=== x86
module {
  func @getTen() -> i64 {
  entry:
    x86.mov eax, 10
    x86.ret
  }
  func @getTwo() -> i64 {
  entry:
    x86.mov eax, 2
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 5
    x86.mov [rbp-8], eax
    x86.call getTen
    x86.mov [rbp-16], eax
    x86.mov ecx, 7
    x86.mov [rbp-24], ecx
    x86.call getTwo
    x86.mov [rbp-32], eax
    x86.mov edx, [rbp-16]
    x86.mov ebx, [rbp-8]
    x86.add ebx, edx
    x86.mov esi, [rbp-24]
    x86.add ebx, esi
    x86.add ebx, eax
    x86.mov eax, ebx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @compute() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.call @compute
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.call @compute
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add} {kind = i64}
    %4 = maxon.literal {value = 256 : i64}
    %5 = maxon.binop %3, %4 {op = mod} {kind = i64}
    maxon.return %5
  }
}
=== standard
module {
  func @compute() -> i64 {
  entry:
    %6 = arith.constant {value = 100 : i64}
    func.return %6
  }
  func @main() -> i64 {
  entry:
    %7 = func.call @compute
    memref.store %7, a
    %8 = func.call @compute
    memref.store %8, b
    %9 = arith.addi %7, %8
    %10 = arith.constant {value = 256 : i64}
    %11 = arith.remsi %9, %10
    func.return %11
  }
}
=== x86
module {
  func @compute() -> i64 {
  entry:
    x86.mov eax, 100
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.call compute
    x86.mov [rbp-8], eax
    x86.call compute
    x86.mov [rbp-16], eax
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov eax, 256
    x86.mov ebx, eax
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 126 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 3 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %0, %1 {op = div} {kind = i64}
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %3 = arith.constant {value = 126 : i64}
    memref.store %3, a
    %4 = arith.constant {value = 3 : i64}
    memref.store %4, b
    %5 = arith.divsi %3, %4
    func.return %5
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 126
    x86.mov [rbp-8], eax
    x86.mov ecx, 3
    x86.mov [rbp-16], ecx
    x86.cqo
    x86.idiv ecx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 84 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 2 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = div} {kind = i64}
    maxon.assign %3 {var = quotient} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.binop %3, %0 {op = sub} {kind = i64}
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %5 = arith.constant {value = 10 : i64}
    memref.store %5, x
    %6 = arith.constant {value = 84 : i64}
    memref.store %6, a
    %7 = arith.constant {value = 2 : i64}
    memref.store %7, b
    %8 = arith.divsi %6, %7
    memref.store %8, quotient
    %9 = arith.subi %8, %5
    func.return %9
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 10
    x86.mov [rbp-8], eax
    x86.mov ecx, 84
    x86.mov [rbp-16], ecx
    x86.mov edx, 2
    x86.mov [rbp-24], edx
    x86.mov ebx, edx
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.mov [rbp-32], eax
    x86.mov ebx, [rbp-8]
    x86.sub eax, ebx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @add(a: i64, b: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.binop %0, %1 {op = add} {kind = i64}
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 30 : i64}
    %4 = maxon.literal {value = 12 : i64}
    %5 = maxon.call @add %3, %4
    maxon.return %5
  }
}
=== standard
module {
  func @add(a: i64, b: i64) -> i64 {
  entry:
    %6 = func.param a : StdI64
    memref.store %6, a
    %7 = func.param b : StdI64
    memref.store %7, b
    %8 = arith.addi %6, %7
    func.return %8
  }
  func @main() -> i64 {
  entry:
    %9 = arith.constant {value = 30 : i64}
    %10 = arith.constant {value = 12 : i64}
    %11 = func.call @add %9, %10
    func.return %11
  }
}
=== x86
module {
  func @add(a: i64, b: i64) -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], edx
    x86.add ecx, edx
    x86.mov eax, ecx
    x86.epilogue
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.mov eax, 30
    x86.mov ecx, 12
    x86.mov edx, ecx
    x86.mov ecx, eax
    x86.call add
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 10 : i64}
    %2 = maxon.binop %0, %1 {op = eq} {kind = i64}
    maxon.cond_br %2 [then: check_0, else: other_1]
  check_0:
    %3 = maxon.literal {value = 42 : i64}
    maxon.return %3
  other_1:
    %4 = maxon.literal {value = 0 : i64}
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %5 = arith.constant {value = 10 : i64}
    memref.store %5, x
    %6 = arith.constant {value = 10 : i64}
    %7 = arith.cmpi eq %5, %6
    cf.cond_br %7 [then: check_0, else: other_1]
  check_0:
    %8 = arith.constant {value = 42 : i64}
    func.return %8
  other_1:
    %9 = arith.constant {value = 0 : i64}
    func.return %9
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 10
    x86.mov [rbp-8], eax
    x86.mov ecx, 10
    x86.cmp eax, ecx
    x86.jne main.other_1
  check_0:
    x86.mov eax, 42
    x86.epilogue
    x86.ret
  other_1:
    x86.mov eax, 0
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 40 : i64}
    maxon.assign %0 {var = base} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = cond} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = extra} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %1, %3 {op = eq} {kind = i64}
    maxon.cond_br %4 [then: check_0, else: other_1]
  check_0:
    %5 = maxon.literal {value = 2 : i64}
    maxon.assign %5 {var = extra} {kind = i64} {mut = 1 : i1}
    maxon.br check_0.merge
  other_1:
    %6 = maxon.literal {value = 100 : i64}
    maxon.assign %6 {var = extra} {kind = i64} {mut = 1 : i1}
    maxon.br check_0.merge
  check_0.merge:
    %7 = maxon.var_ref {var = base} {type = i64}
    %8 = maxon.var_ref {var = extra} {type = i64}
    %9 = maxon.binop %7, %8 {op = add} {kind = i64}
    maxon.return %9
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %10 = arith.constant {value = 40 : i64}
    memref.store %10, base
    %11 = arith.constant {value = 1 : i64}
    memref.store %11, cond
    %12 = arith.constant {value = 0 : i64}
    memref.store %12, extra
    %13 = arith.constant {value = 1 : i64}
    %14 = arith.cmpi eq %11, %13
    cf.cond_br %14 [then: check_0, else: other_1]
  check_0:
    %15 = arith.constant {value = 2 : i64}
    memref.store %15, extra
    cf.br check_0.merge
  other_1:
    %16 = arith.constant {value = 100 : i64}
    memref.store %16, extra
    cf.br check_0.merge
  check_0.merge:
    %17 = memref.load base : i64
    %18 = memref.load extra : i64
    %19 = arith.addi %17, %18
    func.return %19
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 40
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov [rbp-16], ecx
    x86.mov edx, 0
    x86.mov [rbp-24], edx
    x86.mov ebx, 1
    x86.cmp ecx, ebx
    x86.jne main.other_1
  check_0:
    x86.mov eax, 2
    x86.mov [rbp-24], eax
    x86.jmp main.check_0.merge
  other_1:
    x86.mov eax, 100
    x86.mov [rbp-24], eax
    x86.jmp main.check_0.merge
  check_0.merge:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-24]
    x86.add eax, ecx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.assign %0 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %1 = maxon.literal {value = 42 : i64}
    %2 = maxon.var_ref {var = i} {type = i64}
    %3 = maxon.binop %2, %1 {op = lt} {kind = i64}
    maxon.cond_br %3 [then: loop_0, else: loop_0.exit]
  loop_0:
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.var_ref {var = i} {type = i64}
    %6 = maxon.binop %5, %4 {op = add} {kind = i64}
    maxon.assign %6 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.exit:
    %7 = maxon.var_ref {var = i} {type = i64}
    maxon.return %7
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %8 = arith.constant {value = 0 : i64}
    memref.store %8, i
    cf.br loop_0.header
  loop_0.header:
    %9 = arith.constant {value = 42 : i64}
    %10 = memref.load i : i64
    %11 = arith.cmpi lt %10, %9
    cf.cond_br %11 [then: loop_0, else: loop_0.exit]
  loop_0:
    %12 = arith.constant {value = 1 : i64}
    %13 = memref.load i : i64
    %14 = arith.addi %13, %12
    memref.store %14, i
    cf.br loop_0.header
  loop_0.exit:
    %15 = memref.load i : i64
    func.return %15
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 0
    x86.mov [rbp-8], eax
    x86.jmp main.loop_0.header
  loop_0.header:
    x86.mov eax, 42
    x86.mov ecx, [rbp-8]
    x86.cmp ecx, eax
    x86.jge main.loop_0.exit
  loop_0:
    x86.mov eax, 1
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.jmp main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.assign %0 {var = sum} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %2 = maxon.literal {value = 10 : i64}
    %3 = maxon.var_ref {var = i} {type = i64}
    %4 = maxon.binop %3, %2 {op = lt} {kind = i64}
    maxon.cond_br %4 [then: loop_0, else: loop_0.exit]
  loop_0:
    %5 = maxon.var_ref {var = sum} {type = i64}
    %6 = maxon.var_ref {var = i} {type = i64}
    %7 = maxon.binop %5, %6 {op = add} {kind = i64}
    maxon.assign %7 {var = sum} {kind = i64} {mut = 1 : i1}
    %8 = maxon.literal {value = 1 : i64}
    %9 = maxon.var_ref {var = i} {type = i64}
    %10 = maxon.binop %9, %8 {op = add} {kind = i64}
    maxon.assign %10 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.exit:
    %11 = maxon.literal {value = 256 : i64}
    %12 = maxon.var_ref {var = sum} {type = i64}
    %13 = maxon.binop %12, %11 {op = mod} {kind = i64}
    maxon.return %13
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %14 = arith.constant {value = 0 : i64}
    memref.store %14, sum
    %15 = arith.constant {value = 0 : i64}
    memref.store %15, i
    cf.br loop_0.header
  loop_0.header:
    %16 = arith.constant {value = 10 : i64}
    %17 = memref.load i : i64
    %18 = arith.cmpi lt %17, %16
    cf.cond_br %18 [then: loop_0, else: loop_0.exit]
  loop_0:
    %19 = memref.load sum : i64
    %20 = memref.load i : i64
    %21 = arith.addi %19, %20
    memref.store %21, sum
    %22 = arith.constant {value = 1 : i64}
    %23 = memref.load i : i64
    %24 = arith.addi %23, %22
    memref.store %24, i
    cf.br loop_0.header
  loop_0.exit:
    %25 = arith.constant {value = 256 : i64}
    %26 = memref.load sum : i64
    %27 = arith.remsi %26, %25
    func.return %27
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 0
    x86.mov [rbp-8], eax
    x86.mov ecx, 0
    x86.mov [rbp-16], ecx
    x86.jmp main.loop_0.header
  loop_0.header:
    x86.mov eax, 10
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jge main.loop_0.exit
  loop_0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.add eax, ecx
    x86.mov [rbp-8], eax
    x86.mov edx, 1
    x86.mov ebx, [rbp-16]
    x86.add ebx, edx
    x86.mov [rbp-16], ebx
    x86.jmp main.loop_0.header
  loop_0.exit:
    x86.mov eax, 256
    x86.mov ecx, [rbp-8]
    x86.mov ebx, eax
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.assign %0 {var = even_sum} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = odd_sum} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = count} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    maxon.assign %3 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %4 = maxon.literal {value = 20 : i64}
    %5 = maxon.var_ref {var = i} {type = i64}
    %6 = maxon.binop %5, %4 {op = lt} {kind = i64}
    maxon.cond_br %6 [then: loop_0, else: loop_0.exit]
  loop_0:
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.var_ref {var = i} {type = i64}
    %9 = maxon.binop %8, %7 {op = mod} {kind = i64}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %9, %10 {op = eq} {kind = i64}
    maxon.cond_br %11 [then: even_1, else: odd_2]
  even_1:
    %12 = maxon.var_ref {var = even_sum} {type = i64}
    %13 = maxon.var_ref {var = i} {type = i64}
    %14 = maxon.binop %12, %13 {op = add} {kind = i64}
    maxon.assign %14 {var = even_sum} {kind = i64} {mut = 1 : i1}
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = count} {type = i64}
    %17 = maxon.binop %16, %15 {op = add} {kind = i64}
    maxon.assign %17 {var = count} {kind = i64} {mut = 1 : i1}
    maxon.br even_1.merge
  odd_2:
    %18 = maxon.var_ref {var = odd_sum} {type = i64}
    %19 = maxon.var_ref {var = i} {type = i64}
    %20 = maxon.binop %18, %19 {op = add} {kind = i64}
    maxon.assign %20 {var = odd_sum} {kind = i64} {mut = 1 : i1}
    maxon.br even_1.merge
  even_1.merge:
    %21 = maxon.literal {value = 1 : i64}
    %22 = maxon.var_ref {var = i} {type = i64}
    %23 = maxon.binop %22, %21 {op = add} {kind = i64}
    maxon.assign %23 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.exit:
    %24 = maxon.var_ref {var = even_sum} {type = i64}
    %25 = maxon.var_ref {var = odd_sum} {type = i64}
    %26 = maxon.binop %24, %25 {op = add} {kind = i64}
    %27 = maxon.var_ref {var = count} {type = i64}
    %28 = maxon.binop %26, %27 {op = add} {kind = i64}
    %29 = maxon.literal {value = 256 : i64}
    %30 = maxon.binop %28, %29 {op = mod} {kind = i64}
    maxon.return %30
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %31 = arith.constant {value = 0 : i64}
    memref.store %31, even_sum
    %32 = arith.constant {value = 0 : i64}
    memref.store %32, odd_sum
    %33 = arith.constant {value = 0 : i64}
    memref.store %33, count
    %34 = arith.constant {value = 0 : i64}
    memref.store %34, i
    cf.br loop_0.header
  loop_0.header:
    %35 = arith.constant {value = 20 : i64}
    %36 = memref.load i : i64
    %37 = arith.cmpi lt %36, %35
    cf.cond_br %37 [then: loop_0, else: loop_0.exit]
  loop_0:
    %38 = arith.constant {value = 2 : i64}
    %39 = memref.load i : i64
    %40 = arith.remsi %39, %38
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.cmpi eq %40, %41
    cf.cond_br %42 [then: even_1, else: odd_2]
  even_1:
    %43 = memref.load even_sum : i64
    %44 = memref.load i : i64
    %45 = arith.addi %43, %44
    memref.store %45, even_sum
    %46 = arith.constant {value = 1 : i64}
    %47 = memref.load count : i64
    %48 = arith.addi %47, %46
    memref.store %48, count
    cf.br even_1.merge
  odd_2:
    %49 = memref.load odd_sum : i64
    %50 = memref.load i : i64
    %51 = arith.addi %49, %50
    memref.store %51, odd_sum
    cf.br even_1.merge
  even_1.merge:
    %52 = arith.constant {value = 1 : i64}
    %53 = memref.load i : i64
    %54 = arith.addi %53, %52
    memref.store %54, i
    cf.br loop_0.header
  loop_0.exit:
    %55 = memref.load even_sum : i64
    %56 = memref.load odd_sum : i64
    %57 = arith.addi %55, %56
    %58 = memref.load count : i64
    %59 = arith.addi %57, %58
    %60 = arith.constant {value = 256 : i64}
    %61 = arith.remsi %59, %60
    func.return %61
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 0
    x86.mov [rbp-8], eax
    x86.mov ecx, 0
    x86.mov [rbp-16], ecx
    x86.mov edx, 0
    x86.mov [rbp-24], edx
    x86.mov ebx, 0
    x86.mov [rbp-32], ebx
    x86.jmp main.loop_0.header
  loop_0.header:
    x86.mov eax, 20
    x86.mov ecx, [rbp-32]
    x86.cmp ecx, eax
    x86.jge main.loop_0.exit
  loop_0:
    x86.mov eax, 2
    x86.mov ecx, [rbp-32]
    x86.mov ebx, eax
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.mov eax, 0
    x86.cmp edx, eax
    x86.jne main.odd_2
  even_1:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-32]
    x86.add eax, ecx
    x86.mov [rbp-8], eax
    x86.mov edx, 1
    x86.mov ebx, [rbp-24]
    x86.add ebx, edx
    x86.mov [rbp-24], ebx
    x86.jmp main.even_1.merge
  odd_2:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-32]
    x86.add eax, ecx
    x86.mov [rbp-16], eax
    x86.jmp main.even_1.merge
  even_1.merge:
    x86.mov eax, 1
    x86.mov ecx, [rbp-32]
    x86.add ecx, eax
    x86.mov [rbp-32], ecx
    x86.jmp main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.add eax, ecx
    x86.mov edx, [rbp-24]
    x86.add eax, edx
    x86.mov ebx, 256
    x86.cqo
    x86.idiv ebx
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.assign %0 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %2 = maxon.literal {value = 10 : i64}
    %3 = maxon.var_ref {var = i} {type = i64}
    %4 = maxon.binop %3, %2 {op = le} {kind = i64}
    maxon.cond_br %4 [then: loop_0, else: loop_0.exit]
  loop_0:
    %5 = maxon.literal {value = 5 : i64}
    %6 = maxon.var_ref {var = i} {type = i64}
    %7 = maxon.binop %6, %5 {op = le} {kind = i64}
    maxon.cond_br %7 [then: first_1, else: second_2]
  first_1:
    %8 = maxon.var_ref {var = result} {type = i64}
    %9 = maxon.var_ref {var = i} {type = i64}
    %10 = maxon.binop %8, %9 {op = add} {kind = i64}
    maxon.assign %10 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br first_1.merge
  second_2:
    %11 = maxon.literal {value = 2 : i64}
    %12 = maxon.var_ref {var = i} {type = i64}
    %13 = maxon.binop %12, %11 {op = mul} {kind = i64}
    %14 = maxon.var_ref {var = result} {type = i64}
    %15 = maxon.binop %14, %13 {op = add} {kind = i64}
    maxon.assign %15 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br first_1.merge
  first_1.merge:
    %16 = maxon.literal {value = 1 : i64}
    %17 = maxon.var_ref {var = i} {type = i64}
    %18 = maxon.binop %17, %16 {op = add} {kind = i64}
    maxon.assign %18 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.exit:
    %19 = maxon.literal {value = 256 : i64}
    %20 = maxon.var_ref {var = result} {type = i64}
    %21 = maxon.binop %20, %19 {op = mod} {kind = i64}
    maxon.return %21
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %22 = arith.constant {value = 0 : i64}
    memref.store %22, result
    %23 = arith.constant {value = 1 : i64}
    memref.store %23, i
    cf.br loop_0.header
  loop_0.header:
    %24 = arith.constant {value = 10 : i64}
    %25 = memref.load i : i64
    %26 = arith.cmpi le %25, %24
    cf.cond_br %26 [then: loop_0, else: loop_0.exit]
  loop_0:
    %27 = arith.constant {value = 5 : i64}
    %28 = memref.load i : i64
    %29 = arith.cmpi le %28, %27
    cf.cond_br %29 [then: first_1, else: second_2]
  first_1:
    %30 = memref.load result : i64
    %31 = memref.load i : i64
    %32 = arith.addi %30, %31
    memref.store %32, result
    cf.br first_1.merge
  second_2:
    %33 = arith.constant {value = 2 : i64}
    %34 = memref.load i : i64
    %35 = arith.muli %34, %33
    %36 = memref.load result : i64
    %37 = arith.addi %36, %35
    memref.store %37, result
    cf.br first_1.merge
  first_1.merge:
    %38 = arith.constant {value = 1 : i64}
    %39 = memref.load i : i64
    %40 = arith.addi %39, %38
    memref.store %40, i
    cf.br loop_0.header
  loop_0.exit:
    %41 = arith.constant {value = 256 : i64}
    %42 = memref.load result : i64
    %43 = arith.remsi %42, %41
    func.return %43
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 0
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov [rbp-16], ecx
    x86.jmp main.loop_0.header
  loop_0.header:
    x86.mov eax, 10
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jg main.loop_0.exit
  loop_0:
    x86.mov eax, 5
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jg main.second_2
  first_1:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.add eax, ecx
    x86.mov [rbp-8], eax
    x86.jmp main.first_1.merge
  second_2:
    x86.mov eax, 2
    x86.mov ecx, [rbp-16]
    x86.imul ecx, eax
    x86.mov edx, [rbp-8]
    x86.add edx, ecx
    x86.mov [rbp-8], edx
    x86.jmp main.first_1.merge
  first_1.merge:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.jmp main.loop_0.header
  loop_0.exit:
    x86.mov eax, 256
    x86.mov ecx, [rbp-8]
    x86.mov ebx, eax
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.assign %0 {var = total} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br outer_0.header
  outer_0.header:
    %2 = maxon.literal {value = 5 : i64}
    %3 = maxon.var_ref {var = i} {type = i64}
    %4 = maxon.binop %3, %2 {op = lt} {kind = i64}
    maxon.cond_br %4 [then: outer_0, else: outer_0.exit]
  outer_0:
    %5 = maxon.literal {value = 0 : i64}
    maxon.assign %5 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.header:
    %6 = maxon.literal {value = 4 : i64}
    %7 = maxon.var_ref {var = j} {type = i64}
    %8 = maxon.binop %7, %6 {op = lt} {kind = i64}
    maxon.cond_br %8 [then: inner_1, else: inner_1.exit]
  inner_1:
    %9 = maxon.literal {value = 1 : i64}
    %10 = maxon.var_ref {var = total} {type = i64}
    %11 = maxon.binop %10, %9 {op = add} {kind = i64}
    maxon.assign %11 {var = total} {kind = i64} {mut = 1 : i1}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = j} {type = i64}
    %14 = maxon.binop %13, %12 {op = add} {kind = i64}
    maxon.assign %14 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.exit:
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = i} {type = i64}
    %17 = maxon.binop %16, %15 {op = add} {kind = i64}
    maxon.assign %17 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.br outer_0.header
  outer_0.exit:
    %18 = maxon.var_ref {var = total} {type = i64}
    maxon.return %18
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %19 = arith.constant {value = 0 : i64}
    memref.store %19, total
    %20 = arith.constant {value = 0 : i64}
    memref.store %20, i
    cf.br outer_0.header
  outer_0.header:
    %21 = arith.constant {value = 5 : i64}
    %22 = memref.load i : i64
    %23 = arith.cmpi lt %22, %21
    cf.cond_br %23 [then: outer_0, else: outer_0.exit]
  outer_0:
    %24 = arith.constant {value = 0 : i64}
    memref.store %24, j
    cf.br inner_1.header
  inner_1.header:
    %25 = arith.constant {value = 4 : i64}
    %26 = memref.load j : i64
    %27 = arith.cmpi lt %26, %25
    cf.cond_br %27 [then: inner_1, else: inner_1.exit]
  inner_1:
    %28 = arith.constant {value = 1 : i64}
    %29 = memref.load total : i64
    %30 = arith.addi %29, %28
    memref.store %30, total
    %31 = arith.constant {value = 1 : i64}
    %32 = memref.load j : i64
    %33 = arith.addi %32, %31
    memref.store %33, j
    cf.br inner_1.header
  inner_1.exit:
    %34 = arith.constant {value = 1 : i64}
    %35 = memref.load i : i64
    %36 = arith.addi %35, %34
    memref.store %36, i
    cf.br outer_0.header
  outer_0.exit:
    %37 = memref.load total : i64
    func.return %37
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 0
    x86.mov [rbp-8], eax
    x86.mov ecx, 0
    x86.mov [rbp-16], ecx
    x86.jmp main.outer_0.header
  outer_0.header:
    x86.mov eax, 5
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jge main.outer_0.exit
  outer_0:
    x86.mov eax, 0
    x86.mov [rbp-24], eax
    x86.jmp main.inner_1.header
  inner_1.header:
    x86.mov eax, 4
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jge main.inner_1.exit
  inner_1:
    x86.mov eax, 1
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.mov edx, 1
    x86.mov ebx, [rbp-24]
    x86.add ebx, edx
    x86.mov [rbp-24], ebx
    x86.jmp main.inner_1.header
  inner_1.exit:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.jmp main.outer_0.header
  outer_0.exit:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.assign %0 {var = total} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br outer_0.header
  outer_0.header:
    %2 = maxon.literal {value = 5 : i64}
    %3 = maxon.var_ref {var = i} {type = i64}
    %4 = maxon.binop %3, %2 {op = le} {kind = i64}
    maxon.cond_br %4 [then: outer_0, else: outer_0.exit]
  outer_0:
    %5 = maxon.literal {value = 1 : i64}
    maxon.assign %5 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.header:
    %6 = maxon.var_ref {var = j} {type = i64}
    %7 = maxon.var_ref {var = i} {type = i64}
    %8 = maxon.binop %6, %7 {op = le} {kind = i64}
    maxon.cond_br %8 [then: inner_1, else: inner_1.exit]
  inner_1:
    %9 = maxon.literal {value = 1 : i64}
    %10 = maxon.var_ref {var = total} {type = i64}
    %11 = maxon.binop %10, %9 {op = add} {kind = i64}
    maxon.assign %11 {var = total} {kind = i64} {mut = 1 : i1}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = j} {type = i64}
    %14 = maxon.binop %13, %12 {op = add} {kind = i64}
    maxon.assign %14 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.exit:
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = i} {type = i64}
    %17 = maxon.binop %16, %15 {op = add} {kind = i64}
    maxon.assign %17 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.br outer_0.header
  outer_0.exit:
    %18 = maxon.var_ref {var = total} {type = i64}
    maxon.return %18
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %19 = arith.constant {value = 0 : i64}
    memref.store %19, total
    %20 = arith.constant {value = 1 : i64}
    memref.store %20, i
    cf.br outer_0.header
  outer_0.header:
    %21 = arith.constant {value = 5 : i64}
    %22 = memref.load i : i64
    %23 = arith.cmpi le %22, %21
    cf.cond_br %23 [then: outer_0, else: outer_0.exit]
  outer_0:
    %24 = arith.constant {value = 1 : i64}
    memref.store %24, j
    cf.br inner_1.header
  inner_1.header:
    %25 = memref.load j : i64
    %26 = memref.load i : i64
    %27 = arith.cmpi le %25, %26
    cf.cond_br %27 [then: inner_1, else: inner_1.exit]
  inner_1:
    %28 = arith.constant {value = 1 : i64}
    %29 = memref.load total : i64
    %30 = arith.addi %29, %28
    memref.store %30, total
    %31 = arith.constant {value = 1 : i64}
    %32 = memref.load j : i64
    %33 = arith.addi %32, %31
    memref.store %33, j
    cf.br inner_1.header
  inner_1.exit:
    %34 = arith.constant {value = 1 : i64}
    %35 = memref.load i : i64
    %36 = arith.addi %35, %34
    memref.store %36, i
    cf.br outer_0.header
  outer_0.exit:
    %37 = memref.load total : i64
    func.return %37
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 0
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov [rbp-16], ecx
    x86.jmp main.outer_0.header
  outer_0.header:
    x86.mov eax, 5
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jg main.outer_0.exit
  outer_0:
    x86.mov eax, 1
    x86.mov [rbp-24], eax
    x86.jmp main.inner_1.header
  inner_1.header:
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-16]
    x86.cmp eax, ecx
    x86.jg main.inner_1.exit
  inner_1:
    x86.mov eax, 1
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.mov edx, 1
    x86.mov ebx, [rbp-24]
    x86.add ebx, edx
    x86.mov [rbp-24], ebx
    x86.jmp main.inner_1.header
  inner_1.exit:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.jmp main.outer_0.header
  outer_0.exit:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @double(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {kind = i64}
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 0 : i64}
    maxon.assign %3 {var = sum} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    maxon.assign %4 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %5 = maxon.literal {value = 5 : i64}
    %6 = maxon.var_ref {var = i} {type = i64}
    %7 = maxon.binop %6, %5 {op = lt} {kind = i64}
    maxon.cond_br %7 [then: loop_0, else: loop_0.exit]
  loop_0:
    %8 = maxon.var_ref {var = i} {type = i64}
    %9 = maxon.call @double %8
    %10 = maxon.var_ref {var = sum} {type = i64}
    %11 = maxon.binop %10, %9 {op = add} {kind = i64}
    maxon.assign %11 {var = sum} {kind = i64} {mut = 1 : i1}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = i} {type = i64}
    %14 = maxon.binop %13, %12 {op = add} {kind = i64}
    maxon.assign %14 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.exit:
    %15 = maxon.var_ref {var = sum} {type = i64}
    maxon.return %15
  }
}
=== standard
module {
  func @double(x: i64) -> i64 {
  entry:
    %16 = func.param x : StdI64
    memref.store %16, x
    %17 = arith.constant {value = 2 : i64}
    %18 = arith.muli %16, %17
    func.return %18
  }
  func @main() -> i64 {
  entry:
    %19 = arith.constant {value = 0 : i64}
    memref.store %19, sum
    %20 = arith.constant {value = 0 : i64}
    memref.store %20, i
    cf.br loop_0.header
  loop_0.header:
    %21 = arith.constant {value = 5 : i64}
    %22 = memref.load i : i64
    %23 = arith.cmpi lt %22, %21
    cf.cond_br %23 [then: loop_0, else: loop_0.exit]
  loop_0:
    %24 = memref.load i : i64
    %25 = func.call @double %24
    %26 = memref.load sum : i64
    %27 = arith.addi %26, %25
    memref.store %27, sum
    %28 = arith.constant {value = 1 : i64}
    %29 = memref.load i : i64
    %30 = arith.addi %29, %28
    memref.store %30, i
    cf.br loop_0.header
  loop_0.exit:
    %31 = memref.load sum : i64
    func.return %31
  }
}
=== x86
module {
  func @double(x: i64) -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov eax, 2
    x86.imul ecx, eax
    x86.mov eax, ecx
    x86.epilogue
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 0
    x86.mov [rbp-8], eax
    x86.mov ecx, 0
    x86.mov [rbp-16], ecx
    x86.jmp main.loop_0.header
  loop_0.header:
    x86.mov eax, 5
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jge main.loop_0.exit
  loop_0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, eax
    x86.call double
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.mov edx, 1
    x86.mov ebx, [rbp-16]
    x86.add ebx, edx
    x86.mov [rbp-16], ebx
    x86.jmp main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = add} {kind = i64}
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.binop %2, %3 {op = mul} {kind = i64}
    %5 = maxon.literal {value = 4 : i64}
    %6 = maxon.binop %4, %5 {op = add} {kind = i64}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = mul} {kind = i64}
    %9 = maxon.literal {value = 6 : i64}
    %10 = maxon.binop %8, %9 {op = add} {kind = i64}
    maxon.return %10
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %11 = arith.constant {value = 1 : i64}
    %12 = arith.constant {value = 2 : i64}
    %13 = arith.addi %11, %12
    %14 = arith.constant {value = 3 : i64}
    %15 = arith.muli %13, %14
    %16 = arith.constant {value = 4 : i64}
    %17 = arith.addi %15, %16
    %18 = arith.constant {value = 2 : i64}
    %19 = arith.muli %17, %18
    %20 = arith.constant {value = 6 : i64}
    %21 = arith.addi %19, %20
    func.return %21
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.mov eax, 1
    x86.mov ecx, 2
    x86.add eax, ecx
    x86.mov edx, 3
    x86.imul eax, edx
    x86.mov ebx, 4
    x86.add eax, ebx
    x86.mov esi, 2
    x86.imul eax, esi
    x86.mov edi, 6
    x86.add eax, edi
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 5 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 7 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 2 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.binop %0, %1 {op = add} {kind = i64}
    %5 = maxon.binop %2, %3 {op = sub} {kind = i64}
    %6 = maxon.binop %4, %5 {op = mul} {kind = i64}
    maxon.return %6
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %7 = arith.constant {value = 3 : i64}
    memref.store %7, a
    %8 = arith.constant {value = 5 : i64}
    memref.store %8, b
    %9 = arith.constant {value = 7 : i64}
    memref.store %9, c
    %10 = arith.constant {value = 2 : i64}
    memref.store %10, d
    %11 = arith.addi %7, %8
    %12 = arith.subi %9, %10
    %13 = arith.muli %11, %12
    func.return %13
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 3
    x86.mov [rbp-8], eax
    x86.mov ecx, 5
    x86.mov [rbp-16], ecx
    x86.mov edx, 7
    x86.mov [rbp-24], edx
    x86.mov ebx, 2
    x86.mov [rbp-32], ebx
    x86.add eax, ecx
    x86.sub edx, ebx
    x86.imul eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.param {index = 2 : i32} {name = c} {type = i64}
    %3 = maxon.param {index = 3 : i32} {name = d} {type = i64}
    %4 = maxon.param {index = 4 : i32} {name = e} {type = i64}
    %5 = maxon.binop %0, %1 {op = add} {kind = i64}
    %6 = maxon.binop %5, %2 {op = add} {kind = i64}
    %7 = maxon.binop %6, %3 {op = add} {kind = i64}
    %8 = maxon.binop %7, %4 {op = add} {kind = i64}
    maxon.return %8
  }
  func @main() -> i64 {
  entry:
    %9 = maxon.literal {value = 5 : i64}
    %10 = maxon.literal {value = 10 : i64}
    %11 = maxon.literal {value = 8 : i64}
    %12 = maxon.literal {value = 12 : i64}
    %13 = maxon.literal {value = 7 : i64}
    %14 = maxon.call @sum5 %9, %10, %11, %12, %13
    maxon.return %14
  }
}
=== standard
module {
  func @sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    %15 = func.param a : StdI64
    memref.store %15, a
    %16 = func.param b : StdI64
    memref.store %16, b
    %17 = func.param c : StdI64
    memref.store %17, c
    %18 = func.param d : StdI64
    memref.store %18, d
    %19 = func.param e : StdI64
    memref.store %19, e
    %20 = arith.addi %15, %16
    %21 = arith.addi %20, %17
    %22 = arith.addi %21, %18
    %23 = arith.addi %22, %19
    func.return %23
  }
  func @main() -> i64 {
  entry:
    %24 = arith.constant {value = 5 : i64}
    %25 = arith.constant {value = 10 : i64}
    %26 = arith.constant {value = 8 : i64}
    %27 = arith.constant {value = 12 : i64}
    %28 = arith.constant {value = 7 : i64}
    %29 = func.call @sum5 %24, %25, %26, %27, %28
    func.return %29
  }
}
=== x86
module {
  func @sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], edx
    x86.mov [rbp-24], r8
    x86.mov [rbp-32], r9
    x86.mov eax, [rbp+16]
    x86.mov [rbp-40], eax
    x86.add ecx, edx
    x86.add ecx, r8
    x86.add ecx, r9
    x86.add ecx, eax
    x86.mov eax, ecx
    x86.epilogue
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.mov eax, 5
    x86.mov ecx, 10
    x86.mov edx, 8
    x86.mov ebx, 12
    x86.mov esi, 7
    x86.sub rsp, 16
    x86.mov [rsp+0], esi
    x86.mov r8, edx
    x86.mov r9, ebx
    x86.mov edx, ecx
    x86.mov ecx, eax
    x86.call sum5
    x86.add rsp, 16
    x86.ret
  }
}
```

<!-- test: int-nine-params-function -->
```maxon
function sum9(a int, b int, c int, d int, e int, f int, g int, h int, i int) returns int
    return a + b + c + d + e + f + g + h + i
end 'sum9'

function main() returns int
    return sum9(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7, h: 8, i: 9)
end 'main'
```
```exitcode
45
```
```RequiredMLIR
=== maxon
module {
  func @sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.param {index = 2 : i32} {name = c} {type = i64}
    %3 = maxon.param {index = 3 : i32} {name = d} {type = i64}
    %4 = maxon.param {index = 4 : i32} {name = e} {type = i64}
    %5 = maxon.param {index = 5 : i32} {name = f} {type = i64}
    %6 = maxon.param {index = 6 : i32} {name = g} {type = i64}
    %7 = maxon.param {index = 7 : i32} {name = h} {type = i64}
    %8 = maxon.param {index = 8 : i32} {name = i} {type = i64}
    %9 = maxon.binop %0, %1 {op = add} {kind = i64}
    %10 = maxon.binop %9, %2 {op = add} {kind = i64}
    %11 = maxon.binop %10, %3 {op = add} {kind = i64}
    %12 = maxon.binop %11, %4 {op = add} {kind = i64}
    %13 = maxon.binop %12, %5 {op = add} {kind = i64}
    %14 = maxon.binop %13, %6 {op = add} {kind = i64}
    %15 = maxon.binop %14, %7 {op = add} {kind = i64}
    %16 = maxon.binop %15, %8 {op = add} {kind = i64}
    maxon.return %16
  }
  func @main() -> i64 {
  entry:
    %17 = maxon.literal {value = 1 : i64}
    %18 = maxon.literal {value = 2 : i64}
    %19 = maxon.literal {value = 3 : i64}
    %20 = maxon.literal {value = 4 : i64}
    %21 = maxon.literal {value = 5 : i64}
    %22 = maxon.literal {value = 6 : i64}
    %23 = maxon.literal {value = 7 : i64}
    %24 = maxon.literal {value = 8 : i64}
    %25 = maxon.literal {value = 9 : i64}
    %26 = maxon.call @sum9 %17, %18, %19, %20, %21, %22, %23, %24, %25
    maxon.return %26
  }
}
=== standard
module {
  func @sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    %27 = func.param a : StdI64
    memref.store %27, a
    %28 = func.param b : StdI64
    memref.store %28, b
    %29 = func.param c : StdI64
    memref.store %29, c
    %30 = func.param d : StdI64
    memref.store %30, d
    %31 = func.param e : StdI64
    memref.store %31, e
    %32 = func.param f : StdI64
    memref.store %32, f
    %33 = func.param g : StdI64
    memref.store %33, g
    %34 = func.param h : StdI64
    memref.store %34, h
    %35 = func.param i : StdI64
    memref.store %35, i
    %36 = arith.addi %27, %28
    %37 = arith.addi %36, %29
    %38 = arith.addi %37, %30
    %39 = arith.addi %38, %31
    %40 = arith.addi %39, %32
    %41 = arith.addi %40, %33
    %42 = arith.addi %41, %34
    %43 = arith.addi %42, %35
    func.return %43
  }
  func @main() -> i64 {
  entry:
    %44 = arith.constant {value = 1 : i64}
    %45 = arith.constant {value = 2 : i64}
    %46 = arith.constant {value = 3 : i64}
    %47 = arith.constant {value = 4 : i64}
    %48 = arith.constant {value = 5 : i64}
    %49 = arith.constant {value = 6 : i64}
    %50 = arith.constant {value = 7 : i64}
    %51 = arith.constant {value = 8 : i64}
    %52 = arith.constant {value = 9 : i64}
    %53 = func.call @sum9 %44, %45, %46, %47, %48, %49, %50, %51, %52
    func.return %53
  }
}
=== x86
module {
  func @sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    x86.prologue stack_size=80
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], edx
    x86.mov [rbp-24], r8
    x86.mov [rbp-32], r9
    x86.mov eax, [rbp+16]
    x86.mov [rbp-40], eax
    x86.mov ebx, [rbp+24]
    x86.mov [rbp-48], ebx
    x86.mov esi, [rbp+32]
    x86.mov [rbp-56], esi
    x86.mov edi, [rbp+40]
    x86.mov [rbp-64], edi
    x86.mov ecx, [rbp+48]
    x86.mov [rbp-72], ecx
    x86.mov r8, [rbp-8]
    x86.add r8, edx
    x86.mov edx, [rbp-24]
    x86.add r8, edx
    x86.add r8, r9
    x86.add r8, eax
    x86.add r8, ebx
    x86.add r8, esi
    x86.add r8, edi
    x86.add r8, ecx
    x86.mov eax, r8
    x86.epilogue
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 1
    x86.mov ecx, 2
    x86.mov edx, 3
    x86.mov ebx, 4
    x86.mov esi, 5
    x86.mov edi, 6
    x86.mov r8, 7
    x86.mov r9, 8
    x86.mov [rbp-8], eax
    x86.mov eax, 9
    x86.sub rsp, 48
    x86.mov [rsp+0], esi
    x86.mov [rsp+8], edi
    x86.mov [rsp+16], r8
    x86.mov [rsp+24], r9
    x86.mov [rsp+32], eax
    x86.mov r8, edx
    x86.mov r9, ebx
    x86.mov edx, ecx
    x86.mov ecx, [rbp-8]
    x86.call sum9
    x86.add rsp, 48
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @factorial(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 1 : i64}
    %2 = maxon.binop %0, %1 {op = le} {kind = i64}
    maxon.cond_br %2 [then: base_0, else: base_0.after]
  base_0:
    %3 = maxon.literal {value = 1 : i64}
    maxon.return %3
  base_0.after:
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.var_ref {var = n} {type = i64}
    %6 = maxon.binop %5, %4 {op = sub} {kind = i64}
    %7 = maxon.call @factorial %6
    %8 = maxon.var_ref {var = n} {type = i64}
    %9 = maxon.binop %8, %7 {op = mul} {kind = i64}
    maxon.return %9
  }
  func @main() -> i64 {
  entry:
    %10 = maxon.literal {value = 5 : i64}
    %11 = maxon.call @factorial %10
    %12 = maxon.literal {value = 256 : i64}
    %13 = maxon.binop %11, %12 {op = mod} {kind = i64}
    maxon.return %13
  }
}
=== standard
module {
  func @factorial(n: i64) -> i64 {
  entry:
    %14 = func.param n : StdI64
    memref.store %14, n
    %15 = arith.constant {value = 1 : i64}
    %16 = arith.cmpi le %14, %15
    cf.cond_br %16 [then: base_0, else: base_0.after]
  base_0:
    %17 = arith.constant {value = 1 : i64}
    func.return %17
  base_0.after:
    %18 = arith.constant {value = 1 : i64}
    %19 = memref.load n : i64
    %20 = arith.subi %19, %18
    %21 = func.call @factorial %20
    %22 = memref.load n : i64
    %23 = arith.muli %22, %21
    func.return %23
  }
  func @main() -> i64 {
  entry:
    %24 = arith.constant {value = 5 : i64}
    %25 = func.call @factorial %24
    %26 = arith.constant {value = 256 : i64}
    %27 = arith.remsi %25, %26
    func.return %27
  }
}
=== x86
module {
  func @factorial(n: i64) -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov eax, 1
    x86.cmp ecx, eax
    x86.jg factorial.base_0.after
  base_0:
    x86.mov eax, 1
    x86.epilogue
    x86.ret
  base_0.after:
    x86.mov eax, 1
    x86.mov ecx, [rbp-8]
    x86.sub ecx, eax
    x86.call factorial
    x86.mov edx, [rbp-8]
    x86.imul edx, eax
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.mov eax, 5
    x86.mov ecx, eax
    x86.call factorial
    x86.mov ecx, 256
    x86.cqo
    x86.idiv ecx
    x86.mov eax, edx
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @identity(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 2 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 4 : i64}
    maxon.assign %4 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 5 : i64}
    maxon.assign %5 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 6 : i64}
    maxon.assign %6 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 0 : i64}
    maxon.assign %7 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %8 = maxon.literal {value = 3 : i64}
    %9 = maxon.var_ref {var = i} {type = i64}
    %10 = maxon.binop %9, %8 {op = lt} {kind = i64}
    maxon.cond_br %10 [then: loop_0, else: loop_0.exit]
  loop_0:
    %11 = maxon.var_ref {var = b} {type = i64}
    %12 = maxon.call @identity %11
    %13 = maxon.var_ref {var = a} {type = i64}
    %14 = maxon.binop %13, %12 {op = add} {kind = i64}
    maxon.assign %14 {var = a} {kind = i64} {mut = 1 : i1}
    %15 = maxon.var_ref {var = d} {type = i64}
    %16 = maxon.call @identity %15
    %17 = maxon.var_ref {var = c} {type = i64}
    %18 = maxon.binop %17, %16 {op = add} {kind = i64}
    maxon.assign %18 {var = c} {kind = i64} {mut = 1 : i1}
    %19 = maxon.var_ref {var = f} {type = i64}
    %20 = maxon.call @identity %19
    %21 = maxon.var_ref {var = e} {type = i64}
    %22 = maxon.binop %21, %20 {op = add} {kind = i64}
    maxon.assign %22 {var = e} {kind = i64} {mut = 1 : i1}
    %23 = maxon.literal {value = 1 : i64}
    %24 = maxon.var_ref {var = i} {type = i64}
    %25 = maxon.binop %24, %23 {op = add} {kind = i64}
    maxon.assign %25 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.exit:
    %26 = maxon.var_ref {var = a} {type = i64}
    %27 = maxon.var_ref {var = c} {type = i64}
    %28 = maxon.binop %26, %27 {op = add} {kind = i64}
    %29 = maxon.var_ref {var = d} {type = i64}
    %30 = maxon.binop %28, %29 {op = add} {kind = i64}
    %31 = maxon.var_ref {var = e} {type = i64}
    %32 = maxon.binop %30, %31 {op = add} {kind = i64}
    %33 = maxon.var_ref {var = f} {type = i64}
    %34 = maxon.binop %32, %33 {op = add} {kind = i64}
    %35 = maxon.literal {value = 256 : i64}
    %36 = maxon.binop %34, %35 {op = mod} {kind = i64}
    maxon.return %36
  }
}
=== standard
module {
  func @identity(x: i64) -> i64 {
  entry:
    %37 = func.param x : StdI64
    memref.store %37, x
    func.return %37
  }
  func @main() -> i64 {
  entry:
    %38 = arith.constant {value = 1 : i64}
    memref.store %38, a
    %39 = arith.constant {value = 2 : i64}
    memref.store %39, b
    %40 = arith.constant {value = 3 : i64}
    memref.store %40, c
    %41 = arith.constant {value = 4 : i64}
    memref.store %41, d
    %42 = arith.constant {value = 5 : i64}
    memref.store %42, e
    %43 = arith.constant {value = 6 : i64}
    memref.store %43, f
    %44 = arith.constant {value = 0 : i64}
    memref.store %44, i
    cf.br loop_0.header
  loop_0.header:
    %45 = arith.constant {value = 3 : i64}
    %46 = memref.load i : i64
    %47 = arith.cmpi lt %46, %45
    cf.cond_br %47 [then: loop_0, else: loop_0.exit]
  loop_0:
    %48 = memref.load b : i64
    %49 = func.call @identity %48
    %50 = memref.load a : i64
    %51 = arith.addi %50, %49
    memref.store %51, a
    %52 = memref.load d : i64
    %53 = func.call @identity %52
    %54 = memref.load c : i64
    %55 = arith.addi %54, %53
    memref.store %55, c
    %56 = memref.load f : i64
    %57 = func.call @identity %56
    %58 = memref.load e : i64
    %59 = arith.addi %58, %57
    memref.store %59, e
    %60 = arith.constant {value = 1 : i64}
    %61 = memref.load i : i64
    %62 = arith.addi %61, %60
    memref.store %62, i
    cf.br loop_0.header
  loop_0.exit:
    %63 = memref.load a : i64
    %64 = memref.load c : i64
    %65 = arith.addi %63, %64
    %66 = memref.load d : i64
    %67 = arith.addi %65, %66
    %68 = memref.load e : i64
    %69 = arith.addi %67, %68
    %70 = memref.load f : i64
    %71 = arith.addi %69, %70
    %72 = arith.constant {value = 256 : i64}
    %73 = arith.remsi %71, %72
    func.return %73
  }
}
=== x86
module {
  func @identity(x: i64) -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov eax, ecx
    x86.epilogue
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=64
    x86.mov eax, 1
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov [rbp-16], ecx
    x86.mov edx, 3
    x86.mov [rbp-24], edx
    x86.mov ebx, 4
    x86.mov [rbp-32], ebx
    x86.mov esi, 5
    x86.mov [rbp-40], esi
    x86.mov edi, 6
    x86.mov [rbp-48], edi
    x86.mov r8, 0
    x86.mov [rbp-56], r8
    x86.jmp main.loop_0.header
  loop_0.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-56]
    x86.cmp ecx, eax
    x86.jge main.loop_0.exit
  loop_0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, eax
    x86.call identity
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.mov edx, [rbp-32]
    x86.mov ecx, edx
    x86.call identity
    x86.mov ebx, [rbp-24]
    x86.add ebx, eax
    x86.mov [rbp-24], ebx
    x86.mov esi, [rbp-48]
    x86.mov ecx, esi
    x86.call identity
    x86.mov edi, [rbp-40]
    x86.add edi, eax
    x86.mov [rbp-40], edi
    x86.mov r8, 1
    x86.mov r9, [rbp-56]
    x86.add r9, r8
    x86.mov [rbp-56], r9
    x86.jmp main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-24]
    x86.add eax, ecx
    x86.mov edx, [rbp-32]
    x86.add eax, edx
    x86.mov ebx, [rbp-40]
    x86.add eax, ebx
    x86.mov esi, [rbp-48]
    x86.add eax, esi
    x86.mov edi, 256
    x86.cqo
    x86.idiv edi
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3.14 : f64}
    maxon.assign %0 {var = x} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 2.86 : f64}
    maxon.assign %1 {var = y} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %0, %1 {op = add} {kind = f64}
    maxon.assign %2 {var = sum_f} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 10 : i64}
    maxon.assign %3 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 20 : i64}
    maxon.assign %4 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %3, %4 {op = add} {kind = i64}
    maxon.assign %5 {var = sum_i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.trunc %2
    %7 = maxon.binop %6, %5 {op = add} {kind = i64}
    maxon.return %7
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %8 = arith.float_constant {value = 3.14 : f64}
    memref.store %8, x
    %9 = arith.float_constant {value = 2.86 : f64}
    memref.store %9, y
    %10 = arith.addf %8, %9
    memref.store %10, sum_f
    %11 = arith.constant {value = 10 : i64}
    memref.store %11, a
    %12 = arith.constant {value = 20 : i64}
    memref.store %12, b
    %13 = arith.addi %11, %12
    memref.store %13, sum_i
    %14 = arith.fptosi %10
    %15 = arith.addi %14, %13
    func.return %15
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.movsd xmm0, [rip+__float_3.14]
    x86.movsd [rbp-8], xmm0
    x86.movsd xmm1, [rip+__float_2.86]
    x86.movsd [rbp-16], xmm1
    x86.movsd xmm2, xmm0
    x86.addsd xmm2, xmm1
    x86.movsd [rbp-24], xmm2
    x86.mov eax, 10
    x86.mov [rbp-32], eax
    x86.mov ecx, 20
    x86.mov [rbp-40], ecx
    x86.add eax, ecx
    x86.mov [rbp-48], eax
    x86.cvttsd2si edx, xmm2
    x86.add edx, eax
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.assign %0 {var = sentinel} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = total} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br outer_0.header
  outer_0.header:
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.var_ref {var = i} {type = i64}
    %5 = maxon.binop %4, %3 {op = lt} {kind = i64}
    maxon.cond_br %5 [then: outer_0, else: outer_0.exit]
  outer_0:
    %6 = maxon.literal {value = 0 : i64}
    maxon.assign %6 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.header:
    %7 = maxon.literal {value = 3 : i64}
    %8 = maxon.var_ref {var = j} {type = i64}
    %9 = maxon.binop %8, %7 {op = lt} {kind = i64}
    maxon.cond_br %9 [then: inner_1, else: inner_1.exit]
  inner_1:
    %10 = maxon.var_ref {var = i} {type = i64}
    %11 = maxon.var_ref {var = j} {type = i64}
    %12 = maxon.binop %10, %11 {op = eq} {kind = i64}
    maxon.cond_br %12 [then: diag_2, else: diag_2.merge]
  diag_2:
    %13 = maxon.literal {value = 1 : i64}
    %14 = maxon.var_ref {var = total} {type = i64}
    %15 = maxon.binop %14, %13 {op = add} {kind = i64}
    maxon.assign %15 {var = total} {kind = i64} {mut = 1 : i1}
    maxon.br diag_2.merge
  diag_2.merge:
    %16 = maxon.literal {value = 1 : i64}
    %17 = maxon.var_ref {var = j} {type = i64}
    %18 = maxon.binop %17, %16 {op = add} {kind = i64}
    maxon.assign %18 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.exit:
    %19 = maxon.literal {value = 1 : i64}
    %20 = maxon.var_ref {var = i} {type = i64}
    %21 = maxon.binop %20, %19 {op = add} {kind = i64}
    maxon.assign %21 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.br outer_0.header
  outer_0.exit:
    %22 = maxon.var_ref {var = sentinel} {type = i64}
    %23 = maxon.var_ref {var = total} {type = i64}
    %24 = maxon.binop %22, %23 {op = add} {kind = i64}
    maxon.return %24
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %25 = arith.constant {value = 100 : i64}
    memref.store %25, sentinel
    %26 = arith.constant {value = 0 : i64}
    memref.store %26, total
    %27 = arith.constant {value = 0 : i64}
    memref.store %27, i
    cf.br outer_0.header
  outer_0.header:
    %28 = arith.constant {value = 3 : i64}
    %29 = memref.load i : i64
    %30 = arith.cmpi lt %29, %28
    cf.cond_br %30 [then: outer_0, else: outer_0.exit]
  outer_0:
    %31 = arith.constant {value = 0 : i64}
    memref.store %31, j
    cf.br inner_1.header
  inner_1.header:
    %32 = arith.constant {value = 3 : i64}
    %33 = memref.load j : i64
    %34 = arith.cmpi lt %33, %32
    cf.cond_br %34 [then: inner_1, else: inner_1.exit]
  inner_1:
    %35 = memref.load i : i64
    %36 = memref.load j : i64
    %37 = arith.cmpi eq %35, %36
    cf.cond_br %37 [then: diag_2, else: diag_2.merge]
  diag_2:
    %38 = arith.constant {value = 1 : i64}
    %39 = memref.load total : i64
    %40 = arith.addi %39, %38
    memref.store %40, total
    cf.br diag_2.merge
  diag_2.merge:
    %41 = arith.constant {value = 1 : i64}
    %42 = memref.load j : i64
    %43 = arith.addi %42, %41
    memref.store %43, j
    cf.br inner_1.header
  inner_1.exit:
    %44 = arith.constant {value = 1 : i64}
    %45 = memref.load i : i64
    %46 = arith.addi %45, %44
    memref.store %46, i
    cf.br outer_0.header
  outer_0.exit:
    %47 = memref.load sentinel : i64
    %48 = memref.load total : i64
    %49 = arith.addi %47, %48
    func.return %49
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 100
    x86.mov [rbp-8], eax
    x86.mov ecx, 0
    x86.mov [rbp-16], ecx
    x86.mov edx, 0
    x86.mov [rbp-24], edx
    x86.jmp main.outer_0.header
  outer_0.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jge main.outer_0.exit
  outer_0:
    x86.mov eax, 0
    x86.mov [rbp-32], eax
    x86.jmp main.inner_1.header
  inner_1.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-32]
    x86.cmp ecx, eax
    x86.jge main.inner_1.exit
  inner_1:
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-32]
    x86.cmp eax, ecx
    x86.jne main.diag_2.merge
  diag_2:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.jmp main.diag_2.merge
  diag_2.merge:
    x86.mov eax, 1
    x86.mov ecx, [rbp-32]
    x86.add ecx, eax
    x86.mov [rbp-32], ecx
    x86.jmp main.inner_1.header
  inner_1.exit:
    x86.mov eax, 1
    x86.mov ecx, [rbp-24]
    x86.add ecx, eax
    x86.mov [rbp-24], ecx
    x86.jmp main.outer_0.header
  outer_0.exit:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.add eax, ecx
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %3 = maxon.literal {value = 13 : i64}
    %4 = maxon.var_ref {var = i} {type = i64}
    %5 = maxon.binop %4, %3 {op = lt} {kind = i64}
    maxon.cond_br %5 [then: loop_0, else: loop_0.exit]
  loop_0:
    %6 = maxon.var_ref {var = a} {type = i64}
    %7 = maxon.var_ref {var = b} {type = i64}
    %8 = maxon.binop %6, %7 {op = add} {kind = i64}
    maxon.assign %8 {var = temp} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.var_ref {var = b} {type = i64}
    maxon.assign %9 {var = a} {kind = i64} {mut = 1 : i1}
    maxon.assign %8 {var = b} {kind = i64} {mut = 1 : i1}
    %10 = maxon.literal {value = 1 : i64}
    %11 = maxon.var_ref {var = i} {type = i64}
    %12 = maxon.binop %11, %10 {op = add} {kind = i64}
    maxon.assign %12 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.exit:
    %13 = maxon.var_ref {var = a} {type = i64}
    maxon.return %13
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %14 = arith.constant {value = 0 : i64}
    memref.store %14, a
    %15 = arith.constant {value = 1 : i64}
    memref.store %15, b
    %16 = arith.constant {value = 0 : i64}
    memref.store %16, i
    cf.br loop_0.header
  loop_0.header:
    %17 = arith.constant {value = 13 : i64}
    %18 = memref.load i : i64
    %19 = arith.cmpi lt %18, %17
    cf.cond_br %19 [then: loop_0, else: loop_0.exit]
  loop_0:
    %20 = memref.load a : i64
    %21 = memref.load b : i64
    %22 = arith.addi %20, %21
    memref.store %22, temp
    %23 = memref.load b : i64
    memref.store %23, a
    memref.store %22, b
    %24 = arith.constant {value = 1 : i64}
    %25 = memref.load i : i64
    %26 = arith.addi %25, %24
    memref.store %26, i
    cf.br loop_0.header
  loop_0.exit:
    %27 = memref.load a : i64
    func.return %27
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 0
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov [rbp-16], ecx
    x86.mov edx, 0
    x86.mov [rbp-24], edx
    x86.jmp main.loop_0.header
  loop_0.header:
    x86.mov eax, 13
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jge main.loop_0.exit
  loop_0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.add eax, ecx
    x86.mov [rbp-32], eax
    x86.mov edx, [rbp-16]
    x86.mov [rbp-8], edx
    x86.mov [rbp-16], eax
    x86.mov ebx, 1
    x86.mov esi, [rbp-24]
    x86.add esi, ebx
    x86.mov [rbp-24], esi
    x86.jmp main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```
