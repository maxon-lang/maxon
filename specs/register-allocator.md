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
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 16
    x86.mov eax, 99
    x86.mov [rbp-8], eax
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
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 16
    x86.mov eax, 30
    x86.mov [rbp-8], eax
    x86.mov ecx, 12
    x86.mov [rbp-16], ecx
    x86.add eax, ecx
    x86.add rsp, 16
    x86.pop rbp
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
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 16
    x86.mov eax, 21
    x86.mov [rbp-8], eax
    x86.add eax, eax
    x86.add rsp, 16
    x86.pop rbp
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
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 32
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
    x86.add rsp, 32
    x86.pop rbp
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
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 16
    x86.mov eax, 100
    x86.mov [rbp-8], eax
    x86.mov ecx, 80
    x86.sub eax, ecx
    x86.mov [rbp-16], eax
    x86.mov edx, 22
    x86.mov [rbp-8], edx
    x86.add edx, eax
    x86.mov eax, edx
    x86.add rsp, 16
    x86.pop rbp
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
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 48
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
    x86.add rsp, 48
    x86.pop rbp
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
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 80
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
    x86.mov eax, 7
    x86.mov [rbp-56], eax
    x86.mov ecx, 8
    x86.mov [rbp-64], ecx
    x86.mov edx, 9
    x86.mov [rbp-72], edx
    x86.mov ebx, 10
    x86.mov [rbp-80], ebx
    x86.mov esi, [rbp-8]
    x86.mov edi, [rbp-16]
    x86.add esi, edi
    x86.mov edi, [rbp-24]
    x86.add esi, edi
    x86.mov edi, [rbp-32]
    x86.add esi, edi
    x86.mov edi, [rbp-40]
    x86.add esi, edi
    x86.mov edi, [rbp-48]
    x86.add esi, edi
    x86.add esi, eax
    x86.add esi, ecx
    x86.add esi, edx
    x86.add esi, ebx
    x86.mov eax, esi
    x86.add rsp, 80
    x86.pop rbp
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
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 128
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
    x86.mov eax, 7
    x86.mov [rbp-56], eax
    x86.mov ecx, 8
    x86.mov [rbp-64], ecx
    x86.mov edx, 9
    x86.mov [rbp-72], edx
    x86.mov ebx, 10
    x86.mov [rbp-80], ebx
    x86.mov esi, 11
    x86.mov [rbp-88], esi
    x86.mov edi, 12
    x86.mov [rbp-96], edi
    x86.mov eax, 13
    x86.mov [rbp-104], eax
    x86.mov ecx, 14
    x86.mov [rbp-112], ecx
    x86.mov edx, 15
    x86.mov [rbp-120], edx
    x86.mov ebx, 16
    x86.mov [rbp-128], ebx
    x86.mov esi, [rbp-8]
    x86.mov edi, [rbp-16]
    x86.add esi, edi
    x86.mov edi, [rbp-24]
    x86.add esi, edi
    x86.mov edi, [rbp-32]
    x86.add esi, edi
    x86.mov edi, [rbp-40]
    x86.add esi, edi
    x86.mov edi, [rbp-48]
    x86.add esi, edi
    x86.mov edi, [rbp-56]
    x86.add esi, edi
    x86.mov edi, [rbp-64]
    x86.add esi, edi
    x86.mov edi, [rbp-72]
    x86.add esi, edi
    x86.mov edi, [rbp-80]
    x86.add esi, edi
    x86.mov edi, [rbp-88]
    x86.add esi, edi
    x86.mov edi, [rbp-96]
    x86.add esi, edi
    x86.add esi, eax
    x86.add esi, ecx
    x86.add esi, edx
    x86.add esi, ebx
    x86.mov eax, 256
    x86.mov ecx, eax
    x86.mov eax, esi
    x86.cqo
    x86.idiv ecx
    x86.mov eax, edx
    x86.add rsp, 128
    x86.pop rbp
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
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 160
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
    x86.mov eax, 7
    x86.mov [rbp-56], eax
    x86.mov ecx, 8
    x86.mov [rbp-64], ecx
    x86.mov edx, 9
    x86.mov [rbp-72], edx
    x86.mov ebx, 10
    x86.mov [rbp-80], ebx
    x86.mov esi, 11
    x86.mov [rbp-88], esi
    x86.mov edi, 12
    x86.mov [rbp-96], edi
    x86.mov eax, 13
    x86.mov [rbp-104], eax
    x86.mov ecx, 14
    x86.mov [rbp-112], ecx
    x86.mov edx, 15
    x86.mov [rbp-120], edx
    x86.mov ebx, 16
    x86.mov [rbp-128], ebx
    x86.mov esi, 17
    x86.mov [rbp-136], esi
    x86.mov edi, 18
    x86.mov [rbp-144], edi
    x86.mov eax, 19
    x86.mov [rbp-152], eax
    x86.mov ecx, 20
    x86.mov [rbp-160], ecx
    x86.mov edx, [rbp-8]
    x86.mov ebx, [rbp-16]
    x86.add edx, ebx
    x86.mov ebx, [rbp-24]
    x86.add edx, ebx
    x86.mov ebx, [rbp-32]
    x86.add edx, ebx
    x86.mov ebx, [rbp-40]
    x86.add edx, ebx
    x86.mov ebx, [rbp-48]
    x86.add edx, ebx
    x86.mov ebx, [rbp-56]
    x86.add edx, ebx
    x86.mov ebx, [rbp-64]
    x86.add edx, ebx
    x86.mov ebx, [rbp-72]
    x86.add edx, ebx
    x86.mov ebx, [rbp-80]
    x86.add edx, ebx
    x86.mov ebx, [rbp-88]
    x86.add edx, ebx
    x86.mov ebx, [rbp-96]
    x86.add edx, ebx
    x86.mov ebx, [rbp-104]
    x86.add edx, ebx
    x86.mov ebx, [rbp-112]
    x86.add edx, ebx
    x86.mov ebx, [rbp-120]
    x86.add edx, ebx
    x86.mov ebx, [rbp-128]
    x86.add edx, ebx
    x86.add edx, esi
    x86.add edx, edi
    x86.add edx, eax
    x86.add edx, ecx
    x86.mov eax, 256
    x86.mov ecx, eax
    x86.mov eax, edx
    x86.cqo
    x86.idiv ecx
    x86.mov eax, edx
    x86.add rsp, 160
    x86.pop rbp
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
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 80
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
    x86.mov ecx, 256
    x86.cqo
    x86.idiv ecx
    x86.mov eax, edx
    x86.add rsp, 80
    x86.pop rbp
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
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 32
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
    x86.mov ebx, 5
    x86.add eax, ebx
    x86.mov [rbp-8], eax
    x86.mov ebx, 10
    x86.add ecx, ebx
    x86.mov [rbp-16], ecx
    x86.mov ebx, 15
    x86.add edx, ebx
    x86.mov [rbp-24], edx
    x86.add eax, ecx
    x86.add eax, edx
    x86.add rsp, 32
    x86.pop rbp
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
    x86.push rbp
    x86.mov rbp, rsp
    x86.mov eax, 40
    x86.pop rbp
    x86.ret
  }
  func @main() -> i64 {
  entry:
    x86.push rbp
    x86.mov rbp, rsp
    x86.sub rsp, 16
    x86.mov eax, 2
    x86.mov [rbp-8], eax
    x86.call getForty
    x86.mov [rbp-16], eax
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov eax, ecx
    x86.add rsp, 16
    x86.pop rbp
    x86.ret
  }
}
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
```RequiredMLIR
TO BE FILLED IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
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
```RequiredMLIR
FILL ME IN
```
