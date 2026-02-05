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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 42 : i64}
    maxon.return %0
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 42 : i64}
    func.return %0
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 99 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.return %0
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 99 : i64}
    memref.store %0, x
    func.return %0
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 99
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 30 : i64}
    %1 = maxon.literal {value = 12 : i64}
    %2 = maxon.binop %0, %1 {op = add} {kind = i64}
    maxon.return %2
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 30 : i64}
    %1 = arith.constant {value = 12 : i64}
    %2 = arith.addi %0, %1
    func.return %2
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 30 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 12 : i64}
    memref.store %1, b
    %2 = arith.addi %0, %1
    func.return %2
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 30
    x86.mov ecx, 12
    x86.add eax, ecx
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 21 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.binop %0, %0 {op = add} {kind = i64}
    maxon.return %1
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 21 : i64}
    memref.store %0, x
    %1 = arith.addi %0, %0
    func.return %1
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 21
    x86.add eax, eax
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 5 : i64}
    %2 = arith.addi %0, %1
    memref.store %2, b
    %3 = arith.constant {value = 7 : i64}
    %4 = arith.addi %2, %3
    memref.store %4, c
    %5 = arith.constant {value = 20 : i64}
    %6 = arith.addi %4, %5
    memref.store %6, d
    func.return %6
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 10
    x86.mov ecx, 5
    x86.add eax, ecx
    x86.mov edx, 7
    x86.add eax, edx
    x86.mov ebx, 20
    x86.add eax, ebx
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    memref.store %0, x
    %1 = arith.constant {value = 80 : i64}
    %2 = arith.subi %0, %1
    memref.store %2, y
    %3 = arith.constant {value = 22 : i64}
    memref.store %3, x
    %4 = arith.addi %3, %2
    func.return %4
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 100
    x86.mov ecx, 80
    x86.sub eax, ecx
    x86.mov edx, 22
    x86.lea eax, [edx + eax]
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 2 : i64}
    memref.store %1, b
    %2 = arith.constant {value = 3 : i64}
    memref.store %2, c
    %3 = arith.constant {value = 4 : i64}
    memref.store %3, d
    %4 = arith.constant {value = 5 : i64}
    memref.store %4, e
    %5 = arith.constant {value = 6 : i64}
    memref.store %5, f
    %6 = arith.addi %0, %1
    %7 = arith.addi %6, %2
    %8 = arith.addi %7, %3
    %9 = arith.addi %8, %4
    %10 = arith.addi %9, %5
    func.return %10
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 1
    x86.mov ecx, 2
    x86.mov edx, 3
    x86.mov ebx, 4
    x86.mov esi, 5
    x86.mov edi, 6
    x86.add eax, ecx
    x86.add eax, edx
    x86.add eax, ebx
    x86.add eax, esi
    x86.add eax, edi
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 2 : i64}
    memref.store %1, b
    %2 = arith.constant {value = 3 : i64}
    memref.store %2, c
    %3 = arith.constant {value = 4 : i64}
    memref.store %3, d
    %4 = arith.constant {value = 5 : i64}
    memref.store %4, e
    %5 = arith.constant {value = 6 : i64}
    memref.store %5, f
    %6 = arith.constant {value = 7 : i64}
    memref.store %6, g
    %7 = arith.constant {value = 8 : i64}
    memref.store %7, h
    %8 = arith.constant {value = 9 : i64}
    memref.store %8, i
    %9 = arith.constant {value = 10 : i64}
    memref.store %9, j
    %10 = arith.addi %0, %1
    %11 = arith.addi %10, %2
    %12 = arith.addi %11, %3
    %13 = arith.addi %12, %4
    %14 = arith.addi %13, %5
    %15 = arith.addi %14, %6
    %16 = arith.addi %15, %7
    %17 = arith.addi %16, %8
    %18 = arith.addi %17, %9
    func.return %18
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=32
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
    x86.mov [rbp-16], ecx
    x86.mov ecx, 10
    x86.mov [rbp-24], edx
    x86.mov edx, [rbp-16]
    x86.mov [rbp-32], ebx
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
    x86.lea eax, [ebx + ecx]
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 2 : i64}
    memref.store %1, b
    %2 = arith.constant {value = 3 : i64}
    memref.store %2, c
    %3 = arith.constant {value = 4 : i64}
    memref.store %3, d
    %4 = arith.constant {value = 5 : i64}
    memref.store %4, e
    %5 = arith.constant {value = 6 : i64}
    memref.store %5, f
    %6 = arith.constant {value = 7 : i64}
    memref.store %6, g
    %7 = arith.constant {value = 8 : i64}
    memref.store %7, h
    %8 = arith.constant {value = 9 : i64}
    memref.store %8, i
    %9 = arith.constant {value = 10 : i64}
    memref.store %9, j
    %10 = arith.constant {value = 11 : i64}
    memref.store %10, k
    %11 = arith.constant {value = 12 : i64}
    memref.store %11, l
    %12 = arith.constant {value = 13 : i64}
    memref.store %12, m
    %13 = arith.constant {value = 14 : i64}
    memref.store %13, n
    %14 = arith.constant {value = 15 : i64}
    memref.store %14, o
    %15 = arith.constant {value = 16 : i64}
    memref.store %15, p
    %16 = arith.addi %0, %1
    %17 = arith.addi %16, %2
    %18 = arith.addi %17, %3
    %19 = arith.addi %18, %4
    %20 = arith.addi %19, %5
    %21 = arith.addi %20, %6
    %22 = arith.addi %21, %7
    %23 = arith.addi %22, %8
    %24 = arith.addi %23, %9
    %25 = arith.addi %24, %10
    %26 = arith.addi %25, %11
    %27 = arith.addi %26, %12
    %28 = arith.addi %27, %13
    %29 = arith.addi %28, %14
    %30 = arith.addi %29, %15
    %31 = arith.constant {value = 256 : i64}
    %32 = arith.remsi %30, %31
    func.return %32
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=96
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
    x86.mov [rbp-16], ecx
    x86.mov ecx, 10
    x86.mov [rbp-24], edx
    x86.mov edx, 11
    x86.mov [rbp-32], ebx
    x86.mov ebx, 12
    x86.mov [rbp-40], esi
    x86.mov esi, 13
    x86.mov [rbp-48], edi
    x86.mov edi, 14
    x86.mov [rbp-56], r8
    x86.mov r8, 15
    x86.mov [rbp-64], r9
    x86.mov r9, 16
    x86.mov [rbp-72], eax
    x86.mov eax, [rbp-16]
    x86.mov [rbp-80], ecx
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
    x86.mov [rbp-88], eax
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 2 : i64}
    memref.store %1, b
    %2 = arith.constant {value = 3 : i64}
    memref.store %2, c
    %3 = arith.constant {value = 4 : i64}
    memref.store %3, d
    %4 = arith.constant {value = 5 : i64}
    memref.store %4, e
    %5 = arith.constant {value = 6 : i64}
    memref.store %5, f
    %6 = arith.constant {value = 7 : i64}
    memref.store %6, g
    %7 = arith.constant {value = 8 : i64}
    memref.store %7, h
    %8 = arith.constant {value = 9 : i64}
    memref.store %8, i
    %9 = arith.constant {value = 10 : i64}
    memref.store %9, j
    %10 = arith.constant {value = 11 : i64}
    memref.store %10, k
    %11 = arith.constant {value = 12 : i64}
    memref.store %11, l
    %12 = arith.constant {value = 13 : i64}
    memref.store %12, m
    %13 = arith.constant {value = 14 : i64}
    memref.store %13, n
    %14 = arith.constant {value = 15 : i64}
    memref.store %14, o
    %15 = arith.constant {value = 16 : i64}
    memref.store %15, p
    %16 = arith.constant {value = 17 : i64}
    memref.store %16, q
    %17 = arith.constant {value = 18 : i64}
    memref.store %17, r
    %18 = arith.constant {value = 19 : i64}
    memref.store %18, s
    %19 = arith.constant {value = 20 : i64}
    memref.store %19, t
    %20 = arith.addi %0, %1
    %21 = arith.addi %20, %2
    %22 = arith.addi %21, %3
    %23 = arith.addi %22, %4
    %24 = arith.addi %23, %5
    %25 = arith.addi %24, %6
    %26 = arith.addi %25, %7
    %27 = arith.addi %26, %8
    %28 = arith.addi %27, %9
    %29 = arith.addi %28, %10
    %30 = arith.addi %29, %11
    %31 = arith.addi %30, %12
    %32 = arith.addi %31, %13
    %33 = arith.addi %32, %14
    %34 = arith.addi %33, %15
    %35 = arith.addi %34, %16
    %36 = arith.addi %35, %17
    %37 = arith.addi %36, %18
    %38 = arith.addi %37, %19
    %39 = arith.constant {value = 256 : i64}
    %40 = arith.remsi %38, %39
    func.return %40
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=128
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
    x86.mov [rbp-16], ecx
    x86.mov ecx, 10
    x86.mov [rbp-24], edx
    x86.mov edx, 11
    x86.mov [rbp-32], ebx
    x86.mov ebx, 12
    x86.mov [rbp-40], esi
    x86.mov esi, 13
    x86.mov [rbp-48], edi
    x86.mov edi, 14
    x86.mov [rbp-56], r8
    x86.mov r8, 15
    x86.mov [rbp-64], r9
    x86.mov r9, 16
    x86.mov [rbp-72], eax
    x86.mov eax, 17
    x86.mov [rbp-80], ecx
    x86.mov ecx, 18
    x86.mov [rbp-88], edx
    x86.mov edx, 19
    x86.mov [rbp-96], ebx
    x86.mov ebx, 20
    x86.mov [rbp-104], esi
    x86.mov esi, [rbp-16]
    x86.mov [rbp-112], edi
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
    x86.mov [rbp-120], eax
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 20 : i64}
    memref.store %1, b
    %2 = arith.addi %0, %1
    memref.store %2, ab
    %3 = arith.constant {value = 30 : i64}
    memref.store %3, c
    %4 = arith.constant {value = 40 : i64}
    memref.store %4, d
    %5 = arith.addi %3, %4
    memref.store %5, cd
    %6 = arith.constant {value = 50 : i64}
    memref.store %6, e
    %7 = arith.constant {value = 60 : i64}
    memref.store %7, f
    %8 = arith.addi %6, %7
    memref.store %8, ef
    %9 = arith.addi %2, %5
    %10 = arith.addi %9, %8
    memref.store %10, result
    %11 = arith.constant {value = 256 : i64}
    %12 = arith.remsi %10, %11
    func.return %12
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 10
    x86.mov ecx, 20
    x86.add eax, ecx
    x86.mov edx, 30
    x86.mov ebx, 40
    x86.add edx, ebx
    x86.mov esi, 50
    x86.mov edi, 60
    x86.add esi, edi
    x86.add eax, edx
    x86.add eax, esi
    x86.mov r8, 256
    x86.mov [rbp-8], eax
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, sum1
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, sum2
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, sum3
    %3 = arith.constant {value = 10 : i64}
    %4 = arith.addi %0, %3
    memref.store %4, sum1
    %5 = arith.constant {value = 20 : i64}
    %6 = arith.addi %1, %5
    memref.store %6, sum2
    %7 = arith.constant {value = 30 : i64}
    %8 = arith.addi %2, %7
    memref.store %8, sum3
    %9 = arith.constant {value = 5 : i64}
    %10 = arith.addi %4, %9
    memref.store %10, sum1
    %11 = arith.constant {value = 10 : i64}
    %12 = arith.addi %6, %11
    memref.store %12, sum2
    %13 = arith.constant {value = 15 : i64}
    %14 = arith.addi %8, %13
    memref.store %14, sum3
    %15 = arith.addi %10, %12
    %16 = arith.addi %15, %14
    func.return %16
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.xor eax, eax
    x86.xor ecx, ecx
    x86.xor edx, edx
    x86.mov ebx, 10
    x86.add eax, ebx
    x86.mov esi, 20
    x86.add ecx, esi
    x86.mov edi, 30
    x86.add edx, edi
    x86.mov r8, 5
    x86.add eax, r8
    x86.mov r9, 10
    x86.add ecx, r9
    x86.mov ebx, 15
    x86.add edx, ebx
    x86.add eax, ecx
    x86.add eax, edx
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
  func @register-allocator.getForty() -> i64 {
  entry:
    %0 = maxon.literal {value = 40 : i64}
    maxon.return %0
  }
  func @register-allocator.main() -> i64 {
  entry:
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.call @register-allocator.getForty
    maxon.assign %2 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add} {kind = i64}
    maxon.return %3
  }
}
=== standard
module {
  func @register-allocator.getForty() -> i64 {
  entry:
    %0 = arith.constant {value = 40 : i64}
    func.return %0
  }
  func @register-allocator.main() -> i64 {
  entry:
    %1 = arith.constant {value = 2 : i64}
    memref.store %1, x
    %2 = func.call @register-allocator.getForty
    memref.store %2, y
    %3 = arith.addi %1, %2
    func.return %3
  }
}
=== x86
module {
  func @register-allocator.getForty() -> i64 {
  entry:
    x86.mov eax, 40
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 2
    x86.mov [rbp-8], eax
    x86.call register-allocator.getForty
    x86.mov ecx, [rbp-8]
    x86.lea eax, [ecx + eax]
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
  func @register-allocator.getTen() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.return %0
  }
  func @register-allocator.getTwo() -> i64 {
  entry:
    %1 = maxon.literal {value = 2 : i64}
    maxon.return %1
  }
  func @register-allocator.main() -> i64 {
  entry:
    %2 = maxon.literal {value = 5 : i64}
    maxon.assign %2 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.call @register-allocator.getTen
    maxon.assign %3 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 7 : i64}
    maxon.assign %4 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.call @register-allocator.getTwo
    maxon.assign %5 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.binop %2, %3 {op = add} {kind = i64}
    %7 = maxon.binop %6, %4 {op = add} {kind = i64}
    %8 = maxon.binop %7, %5 {op = add} {kind = i64}
    maxon.return %8
  }
}
=== standard
module {
  func @register-allocator.getTen() -> i64 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    func.return %0
  }
  func @register-allocator.getTwo() -> i64 {
  entry:
    %1 = arith.constant {value = 2 : i64}
    func.return %1
  }
  func @register-allocator.main() -> i64 {
  entry:
    %2 = arith.constant {value = 5 : i64}
    memref.store %2, a
    %3 = func.call @register-allocator.getTen
    memref.store %3, b
    %4 = arith.constant {value = 7 : i64}
    memref.store %4, c
    %5 = func.call @register-allocator.getTwo
    memref.store %5, d
    %6 = arith.addi %2, %3
    %7 = arith.addi %6, %4
    %8 = arith.addi %7, %5
    func.return %8
  }
}
=== x86
module {
  func @register-allocator.getTen() -> i64 {
  entry:
    x86.mov eax, 10
    x86.ret
  }
  func @register-allocator.getTwo() -> i64 {
  entry:
    x86.mov eax, 2
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 5
    x86.mov [rbp-8], eax
    x86.call register-allocator.getTen
    x86.mov ecx, 7
    x86.mov [rbp-16], eax
    x86.mov [rbp-24], ecx
    x86.call register-allocator.getTwo
    x86.mov edx, [rbp-16]
    x86.mov ebx, [rbp-8]
    x86.add ebx, edx
    x86.mov esi, [rbp-24]
    x86.add ebx, esi
    x86.lea eax, [ebx + eax]
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
  func @register-allocator.compute() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.return %0
  }
  func @register-allocator.main() -> i64 {
  entry:
    %1 = maxon.call @register-allocator.compute
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.call @register-allocator.compute
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add} {kind = i64}
    %4 = maxon.literal {value = 256 : i64}
    %5 = maxon.binop %3, %4 {op = mod} {kind = i64}
    maxon.return %5
  }
}
=== standard
module {
  func @register-allocator.compute() -> i64 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    func.return %0
  }
  func @register-allocator.main() -> i64 {
  entry:
    %1 = func.call @register-allocator.compute
    memref.store %1, a
    %2 = func.call @register-allocator.compute
    memref.store %2, b
    %3 = arith.addi %1, %2
    %4 = arith.constant {value = 256 : i64}
    %5 = arith.remsi %3, %4
    func.return %5
  }
}
=== x86
module {
  func @register-allocator.compute() -> i64 {
  entry:
    x86.mov eax, 100
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.call register-allocator.compute
    x86.mov [rbp-8], eax
    x86.call register-allocator.compute
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov eax, 256
    x86.mov ebx, eax
    x86.mov [rbp-16], eax
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
  return trunc(a / b)
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 126 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 3 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.int_to_float %0
    %3 = maxon.int_to_float %1
    %4 = maxon.binop %2, %3 {op = div} {kind = f64}
    %5 = maxon.trunc %4
    maxon.return %5
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 126 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 3 : i64}
    memref.store %1, b
    %2 = arith.sitofp %0
    %3 = arith.sitofp %1
    %4 = arith.divf %2, %3
    %5 = arith.fptosi %4
    func.return %5
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 126
    x86.mov ecx, 3
    x86.cvtsi2sd xmm0, eax
    x86.cvtsi2sd xmm1, ecx
    x86.movsd xmm2, xmm0
    x86.divsd xmm2, xmm1
    x86.cvttsd2si edx, xmm2
    x86.mov eax, edx
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
  return trunc(quotient - x)
end 'main'
```
```exitcode
32
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 84 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 2 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.int_to_float %1
    %4 = maxon.int_to_float %2
    %5 = maxon.binop %3, %4 {op = div} {kind = f64}
    maxon.assign %5 {var = quotient} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.int_to_float %0
    %7 = maxon.binop %5, %6 {op = sub} {kind = f64}
    %8 = maxon.trunc %7
    maxon.return %8
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    memref.store %0, x
    %1 = arith.constant {value = 84 : i64}
    memref.store %1, a
    %2 = arith.constant {value = 2 : i64}
    memref.store %2, b
    %3 = arith.sitofp %1
    %4 = arith.sitofp %2
    %5 = arith.divf %3, %4
    memref.store %5, quotient
    %6 = arith.sitofp %0
    %7 = arith.subf %5, %6
    %8 = arith.fptosi %7
    func.return %8
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 10
    x86.mov ecx, 84
    x86.mov edx, 2
    x86.cvtsi2sd xmm0, ecx
    x86.cvtsi2sd xmm1, edx
    x86.movsd xmm2, xmm0
    x86.divsd xmm2, xmm1
    x86.cvtsi2sd xmm3, eax
    x86.movsd xmm4, xmm2
    x86.subsd xmm4, xmm3
    x86.cvttsd2si ebx, xmm4
    x86.mov eax, ebx
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
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.binop %0, %1 {op = add} {kind = i64}
    maxon.return %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3 = maxon.literal {value = 30 : i64}
    %4 = maxon.literal {value = 12 : i64}
    %5 = maxon.call @register-allocator.add %3, %4
    maxon.return %5
  }
}
=== standard
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    memref.store %0, a
    %1 = func.param b : StdI64
    memref.store %1, b
    %2 = arith.addi %0, %1
    func.return %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3 = arith.constant {value = 30 : i64}
    %4 = arith.constant {value = 12 : i64}
    %5 = func.call @register-allocator.add %3, %4
    func.return %5
  }
}
=== x86
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    x86.lea eax, [ecx + edx]
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 30
    x86.mov ecx, 12
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.jmp register-allocator.add
  }
}
```

<!-- test: int-mov-reg-reg-32bit -->
```maxon
function add(a int, b int) returns int
  return a + b
end 'add'

function main() returns int
  var x = 20
  var y = 22
  return add(y, b: x)
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.binop %0, %1 {op = add} {kind = i64}
    maxon.return %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3 = maxon.literal {value = 20 : i64}
    maxon.assign %3 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 22 : i64}
    maxon.assign %4 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.call @register-allocator.add %4, %3
    maxon.return %5
  }
}
=== standard
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    memref.store %0, a
    %1 = func.param b : StdI64
    memref.store %1, b
    %2 = arith.addi %0, %1
    func.return %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3 = arith.constant {value = 20 : i64}
    memref.store %3, x
    %4 = arith.constant {value = 22 : i64}
    memref.store %4, y
    %5 = func.call @register-allocator.add %4, %3
    func.return %5
  }
}
=== x86
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    x86.lea eax, [ecx + edx]
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 20
    x86.mov ecx, 22
    x86.mov rdx, rax
    x86.jmp register-allocator.add
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    memref.store %0, x
    %1 = arith.constant {value = 10 : i64}
    %2 = arith.cmpi eq %0, %1
    cf.cond_br %2 [then: check_0, else: other_1]
  check_0:
    %3 = arith.constant {value = 42 : i64}
    func.return %3
  other_1:
    %4 = arith.constant {value = 0 : i64}
    func.return %4
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 10
    x86.mov ecx, 10
    x86.cmp eax, ecx
    x86.jne register-allocator.main.other_1
  check_0:
    x86.mov eax, 42
    x86.ret
  other_1:
    x86.xor eax, eax
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 40 : i64}
    memref.store %0, base
    %1 = arith.constant {value = 1 : i64}
    memref.store %1, cond
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, extra
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.cmpi eq %1, %3
    cf.cond_br %4 [then: check_0, else: other_1]
  check_0:
    %5 = arith.constant {value = 2 : i64}
    memref.store %5, extra
    cf.br check_0.merge
  other_1:
    %6 = arith.constant {value = 100 : i64}
    memref.store %6, extra
    cf.br check_0.merge
  check_0.merge:
    %7 = memref.load base : i64
    %8 = memref.load extra : i64
    %9 = arith.addi %7, %8
    func.return %9
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 40
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.xor edx, edx
    x86.mov [rbp-16], edx
    x86.mov ebx, 1
    x86.cmp ecx, ebx
    x86.jne register-allocator.main.other_1
  check_0:
    x86.mov eax, 2
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.check_0.merge
  other_1:
    x86.mov eax, 100
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.check_0.merge
  check_0.merge:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, i
    cf.br loop_0.header
  loop_0.header:
    %1 = arith.constant {value = 42 : i64}
    %2 = memref.load i : i64
    %3 = arith.cmpi lt %2, %1
    cf.cond_br %3 [then: loop_0, else: loop_0.exit]
  loop_0:
    %4 = arith.constant {value = 1 : i64}
    %5 = memref.load i : i64
    %6 = arith.addi %5, %4
    memref.store %6, i
    cf.br loop_0.header
  loop_0.exit:
    %7 = memref.load i : i64
    func.return %7
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 42
    x86.mov ecx, [rbp-8]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.mov eax, 1
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.jmp register-allocator.main.loop_0.header
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, sum
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, i
    cf.br loop_0.header
  loop_0.header:
    %2 = arith.constant {value = 10 : i64}
    %3 = memref.load i : i64
    %4 = arith.cmpi lt %3, %2
    cf.cond_br %4 [then: loop_0, else: loop_0.exit]
  loop_0:
    %5 = memref.load sum : i64
    %6 = memref.load i : i64
    %7 = arith.addi %5, %6
    memref.store %7, sum
    %8 = arith.constant {value = 1 : i64}
    %9 = memref.load i : i64
    %10 = arith.addi %9, %8
    memref.store %10, i
    cf.br loop_0.header
  loop_0.exit:
    %11 = arith.constant {value = 256 : i64}
    %12 = memref.load sum : i64
    %13 = arith.remsi %12, %11
    func.return %13
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 10
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.add eax, ecx
    x86.mov [rbp-8], eax
    x86.mov edx, 1
    x86.mov ebx, [rbp-16]
    x86.add ebx, edx
    x86.mov [rbp-16], ebx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.exit:
    x86.mov eax, 256
    x86.mov ecx, [rbp-8]
    x86.mov ebx, eax
    x86.mov [rbp-24], eax
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, even_sum
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, odd_sum
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, count
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, i
    cf.br loop_0.header
  loop_0.header:
    %4 = arith.constant {value = 20 : i64}
    %5 = memref.load i : i64
    %6 = arith.cmpi lt %5, %4
    cf.cond_br %6 [then: loop_0, else: loop_0.exit]
  loop_0:
    %7 = arith.constant {value = 2 : i64}
    %8 = memref.load i : i64
    %9 = arith.remsi %8, %7
    %10 = arith.constant {value = 0 : i64}
    %11 = arith.cmpi eq %9, %10
    cf.cond_br %11 [then: even_1, else: odd_2]
  even_1:
    %12 = memref.load even_sum : i64
    %13 = memref.load i : i64
    %14 = arith.addi %12, %13
    memref.store %14, even_sum
    %15 = arith.constant {value = 1 : i64}
    %16 = memref.load count : i64
    %17 = arith.addi %16, %15
    memref.store %17, count
    cf.br even_1.merge
  odd_2:
    %18 = memref.load odd_sum : i64
    %19 = memref.load i : i64
    %20 = arith.addi %18, %19
    memref.store %20, odd_sum
    cf.br even_1.merge
  even_1.merge:
    %21 = arith.constant {value = 1 : i64}
    %22 = memref.load i : i64
    %23 = arith.addi %22, %21
    memref.store %23, i
    cf.br loop_0.header
  loop_0.exit:
    %24 = memref.load even_sum : i64
    %25 = memref.load odd_sum : i64
    %26 = arith.addi %24, %25
    %27 = memref.load count : i64
    %28 = arith.addi %26, %27
    %29 = arith.constant {value = 256 : i64}
    %30 = arith.remsi %28, %29
    func.return %30
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.xor ebx, ebx
    x86.mov [rbp-32], ebx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 20
    x86.mov ecx, [rbp-32]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.mov eax, 2
    x86.mov ecx, [rbp-32]
    x86.mov ebx, eax
    x86.mov [rbp-40], eax
    x86.mov eax, ecx
    x86.cqo
    x86.idiv ebx
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.jne register-allocator.main.odd_2
  even_1:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-32]
    x86.add eax, ecx
    x86.mov [rbp-8], eax
    x86.mov edx, 1
    x86.mov ebx, [rbp-24]
    x86.add ebx, edx
    x86.mov [rbp-24], ebx
    x86.jmp register-allocator.main.even_1.merge
  odd_2:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-32]
    x86.add eax, ecx
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.even_1.merge
  even_1.merge:
    x86.mov eax, 1
    x86.mov ecx, [rbp-32]
    x86.add ecx, eax
    x86.mov [rbp-32], ecx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.add eax, ecx
    x86.mov edx, [rbp-24]
    x86.add eax, edx
    x86.mov ebx, 256
    x86.mov [rbp-40], eax
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, result
    %1 = arith.constant {value = 1 : i64}
    memref.store %1, i
    cf.br loop_0.header
  loop_0.header:
    %2 = arith.constant {value = 10 : i64}
    %3 = memref.load i : i64
    %4 = arith.cmpi le %3, %2
    cf.cond_br %4 [then: loop_0, else: loop_0.exit]
  loop_0:
    %5 = arith.constant {value = 5 : i64}
    %6 = memref.load i : i64
    %7 = arith.cmpi le %6, %5
    cf.cond_br %7 [then: first_1, else: second_2]
  first_1:
    %8 = memref.load result : i64
    %9 = memref.load i : i64
    %10 = arith.addi %8, %9
    memref.store %10, result
    cf.br first_1.merge
  second_2:
    %11 = arith.constant {value = 2 : i64}
    %12 = memref.load i : i64
    %13 = arith.muli %12, %11
    %14 = memref.load result : i64
    %15 = arith.addi %14, %13
    memref.store %15, result
    cf.br first_1.merge
  first_1.merge:
    %16 = arith.constant {value = 1 : i64}
    %17 = memref.load i : i64
    %18 = arith.addi %17, %16
    memref.store %18, i
    cf.br loop_0.header
  loop_0.exit:
    %19 = arith.constant {value = 256 : i64}
    %20 = memref.load result : i64
    %21 = arith.remsi %20, %19
    func.return %21
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 10
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jg register-allocator.main.loop_0.exit
  loop_0:
    x86.mov eax, 5
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jg register-allocator.main.second_2
  first_1:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.add eax, ecx
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.first_1.merge
  second_2:
    x86.mov eax, 2
    x86.mov ecx, [rbp-16]
    x86.imul ecx, eax
    x86.mov edx, [rbp-8]
    x86.add edx, ecx
    x86.mov [rbp-8], edx
    x86.jmp register-allocator.main.first_1.merge
  first_1.merge:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.exit:
    x86.mov eax, 256
    x86.mov ecx, [rbp-8]
    x86.mov ebx, eax
    x86.mov [rbp-24], eax
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, total
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, i
    cf.br outer_0.header
  outer_0.header:
    %2 = arith.constant {value = 5 : i64}
    %3 = memref.load i : i64
    %4 = arith.cmpi lt %3, %2
    cf.cond_br %4 [then: outer_0, else: outer_0.exit]
  outer_0:
    %5 = arith.constant {value = 0 : i64}
    memref.store %5, j
    cf.br inner_1.header
  inner_1.header:
    %6 = arith.constant {value = 4 : i64}
    %7 = memref.load j : i64
    %8 = arith.cmpi lt %7, %6
    cf.cond_br %8 [then: inner_1, else: inner_1.exit]
  inner_1:
    %9 = arith.constant {value = 1 : i64}
    %10 = memref.load total : i64
    %11 = arith.addi %10, %9
    memref.store %11, total
    %12 = arith.constant {value = 1 : i64}
    %13 = memref.load j : i64
    %14 = arith.addi %13, %12
    memref.store %14, j
    cf.br inner_1.header
  inner_1.exit:
    %15 = arith.constant {value = 1 : i64}
    %16 = memref.load i : i64
    %17 = arith.addi %16, %15
    memref.store %17, i
    cf.br outer_0.header
  outer_0.exit:
    %18 = memref.load total : i64
    func.return %18
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.outer_0.header
  outer_0.header:
    x86.mov eax, 5
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.outer_0.exit
  outer_0:
    x86.xor eax, eax
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.inner_1.header
  inner_1.header:
    x86.mov eax, 4
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.inner_1.exit
  inner_1:
    x86.mov eax, 1
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.mov edx, 1
    x86.mov ebx, [rbp-24]
    x86.add ebx, edx
    x86.mov [rbp-24], ebx
    x86.jmp register-allocator.main.inner_1.header
  inner_1.exit:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.outer_0.header
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, total
    %1 = arith.constant {value = 1 : i64}
    memref.store %1, i
    cf.br outer_0.header
  outer_0.header:
    %2 = arith.constant {value = 5 : i64}
    %3 = memref.load i : i64
    %4 = arith.cmpi le %3, %2
    cf.cond_br %4 [then: outer_0, else: outer_0.exit]
  outer_0:
    %5 = arith.constant {value = 1 : i64}
    memref.store %5, j
    cf.br inner_1.header
  inner_1.header:
    %6 = memref.load j : i64
    %7 = memref.load i : i64
    %8 = arith.cmpi le %6, %7
    cf.cond_br %8 [then: inner_1, else: inner_1.exit]
  inner_1:
    %9 = arith.constant {value = 1 : i64}
    %10 = memref.load total : i64
    %11 = arith.addi %10, %9
    memref.store %11, total
    %12 = arith.constant {value = 1 : i64}
    %13 = memref.load j : i64
    %14 = arith.addi %13, %12
    memref.store %14, j
    cf.br inner_1.header
  inner_1.exit:
    %15 = arith.constant {value = 1 : i64}
    %16 = memref.load i : i64
    %17 = arith.addi %16, %15
    memref.store %17, i
    cf.br outer_0.header
  outer_0.exit:
    %18 = memref.load total : i64
    func.return %18
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.outer_0.header
  outer_0.header:
    x86.mov eax, 5
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jg register-allocator.main.outer_0.exit
  outer_0:
    x86.mov eax, 1
    x86.mov [rbp-24], eax
    x86.jmp register-allocator.main.inner_1.header
  inner_1.header:
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-16]
    x86.cmp eax, ecx
    x86.jg register-allocator.main.inner_1.exit
  inner_1:
    x86.mov eax, 1
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.mov edx, 1
    x86.mov ebx, [rbp-24]
    x86.add ebx, edx
    x86.mov [rbp-24], ebx
    x86.jmp register-allocator.main.inner_1.header
  inner_1.exit:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.outer_0.header
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
  func @register-allocator.double(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {kind = i64}
    maxon.return %2
  }
  func @register-allocator.main() -> i64 {
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
    %9 = maxon.call @register-allocator.double %8
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
  func @register-allocator.double(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    memref.store %0, x
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.muli %0, %1
    func.return %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, sum
    %4 = arith.constant {value = 0 : i64}
    memref.store %4, i
    cf.br loop_0.header
  loop_0.header:
    %5 = arith.constant {value = 5 : i64}
    %6 = memref.load i : i64
    %7 = arith.cmpi lt %6, %5
    cf.cond_br %7 [then: loop_0, else: loop_0.exit]
  loop_0:
    %8 = memref.load i : i64
    %9 = func.call @register-allocator.double %8
    %10 = memref.load sum : i64
    %11 = arith.addi %10, %9
    memref.store %11, sum
    %12 = arith.constant {value = 1 : i64}
    %13 = memref.load i : i64
    %14 = arith.addi %13, %12
    memref.store %14, i
    cf.br loop_0.header
  loop_0.exit:
    %15 = memref.load sum : i64
    func.return %15
  }
}
=== x86
module {
  func @register-allocator.double(x: i64) -> i64 {
  entry:
    x86.mov eax, 2
    x86.imul ecx, eax
    x86.mov eax, ecx
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 5
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.mov eax, [rbp-16]
    x86.mov rcx, rax
    x86.call register-allocator.double
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.mov edx, 1
    x86.mov ebx, [rbp-16]
    x86.add ebx, edx
    x86.mov [rbp-16], ebx
    x86.jmp register-allocator.main.loop_0.header
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.addi %0, %1
    %3 = arith.constant {value = 3 : i64}
    %4 = arith.muli %2, %3
    %5 = arith.constant {value = 4 : i64}
    %6 = arith.addi %4, %5
    %7 = arith.constant {value = 2 : i64}
    %8 = arith.muli %6, %7
    %9 = arith.constant {value = 6 : i64}
    %10 = arith.addi %8, %9
    func.return %10
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 3 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 5 : i64}
    memref.store %1, b
    %2 = arith.constant {value = 7 : i64}
    memref.store %2, c
    %3 = arith.constant {value = 2 : i64}
    memref.store %3, d
    %4 = arith.addi %0, %1
    %5 = arith.subi %2, %3
    %6 = arith.muli %4, %5
    func.return %6
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 3
    x86.mov ecx, 5
    x86.mov edx, 7
    x86.mov ebx, 2
    x86.add eax, ecx
    x86.sub edx, ebx
    x86.imul eax, edx
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
  func @register-allocator.sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %9 = maxon.literal {value = 5 : i64}
    %10 = maxon.literal {value = 10 : i64}
    %11 = maxon.literal {value = 8 : i64}
    %12 = maxon.literal {value = 12 : i64}
    %13 = maxon.literal {value = 7 : i64}
    %14 = maxon.call @register-allocator.sum5 %9, %10, %11, %12, %13
    maxon.return %14
  }
}
=== standard
module {
  func @register-allocator.sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    memref.store %0, a
    %1 = func.param b : StdI64
    memref.store %1, b
    %2 = func.param c : StdI64
    memref.store %2, c
    %3 = func.param d : StdI64
    memref.store %3, d
    %4 = func.param e : StdI64
    memref.store %4, e
    %5 = arith.addi %0, %1
    %6 = arith.addi %5, %2
    %7 = arith.addi %6, %3
    %8 = arith.addi %7, %4
    func.return %8
  }
  func @register-allocator.main() -> i64 {
  entry:
    %9 = arith.constant {value = 5 : i64}
    %10 = arith.constant {value = 10 : i64}
    %11 = arith.constant {value = 8 : i64}
    %12 = arith.constant {value = 12 : i64}
    %13 = arith.constant {value = 7 : i64}
    %14 = func.call @register-allocator.sum5 %9, %10, %11, %12, %13
    func.return %14
  }
}
=== x86
module {
  func @register-allocator.sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    x86.add ecx, edx
    x86.add ecx, r8
    x86.add ecx, r9
    x86.lea eax, [ecx + esi]
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 5
    x86.mov ecx, 10
    x86.mov edx, 8
    x86.mov ebx, 12
    x86.mov esi, 7
    x86.mov r8, rdx
    x86.mov r9, rbx
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.jmp register-allocator.sum5
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
  func @register-allocator.sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
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
  func @register-allocator.main() -> i64 {
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
    %26 = maxon.call @register-allocator.sum9 %17, %18, %19, %20, %21, %22, %23, %24, %25
    maxon.return %26
  }
}
=== standard
module {
  func @register-allocator.sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    memref.store %0, a
    %1 = func.param b : StdI64
    memref.store %1, b
    %2 = func.param c : StdI64
    memref.store %2, c
    %3 = func.param d : StdI64
    memref.store %3, d
    %4 = func.param e : StdI64
    memref.store %4, e
    %5 = func.param f : StdI64
    memref.store %5, f
    %6 = func.param g : StdI64
    memref.store %6, g
    %7 = func.param h : StdI64
    memref.store %7, h
    %8 = func.param i : StdI64
    memref.store %8, i
    %9 = arith.addi %0, %1
    %10 = arith.addi %9, %2
    %11 = arith.addi %10, %3
    %12 = arith.addi %11, %4
    %13 = arith.addi %12, %5
    %14 = arith.addi %13, %6
    %15 = arith.addi %14, %7
    %16 = arith.addi %15, %8
    func.return %16
  }
  func @register-allocator.main() -> i64 {
  entry:
    %17 = arith.constant {value = 1 : i64}
    %18 = arith.constant {value = 2 : i64}
    %19 = arith.constant {value = 3 : i64}
    %20 = arith.constant {value = 4 : i64}
    %21 = arith.constant {value = 5 : i64}
    %22 = arith.constant {value = 6 : i64}
    %23 = arith.constant {value = 7 : i64}
    %24 = arith.constant {value = 8 : i64}
    %25 = arith.constant {value = 9 : i64}
    %26 = func.call @register-allocator.sum9 %17, %18, %19, %20, %21, %22, %23, %24, %25
    func.return %26
  }
}
=== x86
module {
  func @register-allocator.sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov ecx, [rbp+16]
    x86.mov [rbp-16], r8
    x86.mov r8, [rbp-8]
    x86.add r8, edx
    x86.mov edx, [rbp-16]
    x86.add r8, edx
    x86.add r8, r9
    x86.add r8, esi
    x86.add r8, edi
    x86.add r8, eax
    x86.add r8, ebx
    x86.lea eax, [r8 + ecx]
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> i64 {
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
    x86.sub rsp, 16
    x86.mov [rsp+0], eax
    x86.mov rax, r8
    x86.mov r8, rdx
    x86.mov rdx, rcx
    x86.mov rcx, [rbp-8]
    x86.xchg rbx, r9
    x86.call register-allocator.sum9
    x86.add rsp, 16
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
  func @register-allocator.factorial(n: i64) -> i64 {
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
    %7 = maxon.call @register-allocator.factorial %6
    %8 = maxon.var_ref {var = n} {type = i64}
    %9 = maxon.binop %8, %7 {op = mul} {kind = i64}
    maxon.return %9
  }
  func @register-allocator.main() -> i64 {
  entry:
    %10 = maxon.literal {value = 5 : i64}
    %11 = maxon.call @register-allocator.factorial %10
    %12 = maxon.literal {value = 256 : i64}
    %13 = maxon.binop %11, %12 {op = mod} {kind = i64}
    maxon.return %13
  }
}
=== standard
module {
  func @register-allocator.factorial(n: i64) -> i64 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, n
    %1 = arith.constant {value = 1 : i64}
    %2 = arith.cmpi le %0, %1
    cf.cond_br %2 [then: base_0, else: base_0.after]
  base_0:
    %3 = arith.constant {value = 1 : i64}
    func.return %3
  base_0.after:
    %4 = arith.constant {value = 1 : i64}
    %5 = memref.load n : i64
    %6 = arith.subi %5, %4
    %7 = func.call @register-allocator.factorial %6
    %8 = memref.load n : i64
    %9 = arith.muli %8, %7
    func.return %9
  }
  func @register-allocator.main() -> i64 {
  entry:
    %10 = arith.constant {value = 5 : i64}
    %11 = func.call @register-allocator.factorial %10
    %12 = arith.constant {value = 256 : i64}
    %13 = arith.remsi %11, %12
    func.return %13
  }
}
=== x86
module {
  func @register-allocator.factorial(n: i64) -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov eax, 1
    x86.cmp ecx, eax
    x86.jg register-allocator.factorial.base_0.after
  base_0:
    x86.mov eax, 1
    x86.epilogue
    x86.ret
  base_0.after:
    x86.mov eax, 1
    x86.mov ecx, [rbp-8]
    x86.sub ecx, eax
    x86.call register-allocator.factorial
    x86.mov edx, [rbp-8]
    x86.imul edx, eax
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 5
    x86.mov rcx, rax
    x86.call register-allocator.factorial
    x86.mov ecx, 256
    x86.mov [rbp-8], eax
    x86.cqo
    x86.idiv ecx
    x86.mov eax, edx
    x86.epilogue
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
  func @register-allocator.identity(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    maxon.return %0
  }
  func @register-allocator.main() -> i64 {
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
    %12 = maxon.call @register-allocator.identity %11
    %13 = maxon.var_ref {var = a} {type = i64}
    %14 = maxon.binop %13, %12 {op = add} {kind = i64}
    maxon.assign %14 {var = a} {kind = i64} {mut = 1 : i1}
    %15 = maxon.var_ref {var = d} {type = i64}
    %16 = maxon.call @register-allocator.identity %15
    %17 = maxon.var_ref {var = c} {type = i64}
    %18 = maxon.binop %17, %16 {op = add} {kind = i64}
    maxon.assign %18 {var = c} {kind = i64} {mut = 1 : i1}
    %19 = maxon.var_ref {var = f} {type = i64}
    %20 = maxon.call @register-allocator.identity %19
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
  func @register-allocator.identity(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    memref.store %0, x
    func.return %0
  }
  func @register-allocator.main() -> i64 {
  entry:
    %1 = arith.constant {value = 1 : i64}
    memref.store %1, a
    %2 = arith.constant {value = 2 : i64}
    memref.store %2, b
    %3 = arith.constant {value = 3 : i64}
    memref.store %3, c
    %4 = arith.constant {value = 4 : i64}
    memref.store %4, d
    %5 = arith.constant {value = 5 : i64}
    memref.store %5, e
    %6 = arith.constant {value = 6 : i64}
    memref.store %6, f
    %7 = arith.constant {value = 0 : i64}
    memref.store %7, i
    cf.br loop_0.header
  loop_0.header:
    %8 = arith.constant {value = 3 : i64}
    %9 = memref.load i : i64
    %10 = arith.cmpi lt %9, %8
    cf.cond_br %10 [then: loop_0, else: loop_0.exit]
  loop_0:
    %11 = memref.load b : i64
    %12 = func.call @register-allocator.identity %11
    %13 = memref.load a : i64
    %14 = arith.addi %13, %12
    memref.store %14, a
    %15 = memref.load d : i64
    %16 = func.call @register-allocator.identity %15
    %17 = memref.load c : i64
    %18 = arith.addi %17, %16
    memref.store %18, c
    %19 = memref.load f : i64
    %20 = func.call @register-allocator.identity %19
    %21 = memref.load e : i64
    %22 = arith.addi %21, %20
    memref.store %22, e
    %23 = arith.constant {value = 1 : i64}
    %24 = memref.load i : i64
    %25 = arith.addi %24, %23
    memref.store %25, i
    cf.br loop_0.header
  loop_0.exit:
    %26 = memref.load a : i64
    %27 = memref.load c : i64
    %28 = arith.addi %26, %27
    %29 = memref.load d : i64
    %30 = arith.addi %28, %29
    %31 = memref.load e : i64
    %32 = arith.addi %30, %31
    %33 = memref.load f : i64
    %34 = arith.addi %32, %33
    %35 = arith.constant {value = 256 : i64}
    %36 = arith.remsi %34, %35
    func.return %36
  }
}
=== x86
module {
  func @register-allocator.identity(x: i64) -> i64 {
  entry:
    x86.mov eax, ecx
    x86.ret
  }
  func @register-allocator.main() -> i64 {
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
    x86.xor r8, r8
    x86.mov [rbp-56], r8
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-56]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.mov eax, [rbp-16]
    x86.mov rcx, rax
    x86.call register-allocator.identity
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.mov edx, [rbp-32]
    x86.mov rcx, rdx
    x86.call register-allocator.identity
    x86.mov ebx, [rbp-24]
    x86.add ebx, eax
    x86.mov [rbp-24], ebx
    x86.mov esi, [rbp-48]
    x86.mov rcx, rsi
    x86.call register-allocator.identity
    x86.mov edi, [rbp-40]
    x86.add edi, eax
    x86.mov [rbp-40], edi
    x86.mov r8, 1
    x86.mov r9, [rbp-56]
    x86.add r9, r8
    x86.mov [rbp-56], r9
    x86.jmp register-allocator.main.loop_0.header
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
    x86.mov [rbp-64], eax
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.float_constant {value = 3.14 : f64}
    memref.store %0, x
    %1 = arith.float_constant {value = 2.86 : f64}
    memref.store %1, y
    %2 = arith.addf %0, %1
    memref.store %2, sum_f
    %3 = arith.constant {value = 10 : i64}
    memref.store %3, a
    %4 = arith.constant {value = 20 : i64}
    memref.store %4, b
    %5 = arith.addi %3, %4
    memref.store %5, sum_i
    %6 = arith.fptosi %2
    %7 = arith.addi %6, %5
    func.return %7
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.movsd xmm0, [rip+__float_3.14]
    x86.movsd xmm1, [rip+__float_2.86]
    x86.movsd xmm2, xmm0
    x86.addsd xmm2, xmm1
    x86.mov eax, 10
    x86.mov ecx, 20
    x86.add eax, ecx
    x86.cvttsd2si edx, xmm2
    x86.lea eax, [edx + eax]
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    memref.store %0, sentinel
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, total
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, i
    cf.br outer_0.header
  outer_0.header:
    %3 = arith.constant {value = 3 : i64}
    %4 = memref.load i : i64
    %5 = arith.cmpi lt %4, %3
    cf.cond_br %5 [then: outer_0, else: outer_0.exit]
  outer_0:
    %6 = arith.constant {value = 0 : i64}
    memref.store %6, j
    cf.br inner_1.header
  inner_1.header:
    %7 = arith.constant {value = 3 : i64}
    %8 = memref.load j : i64
    %9 = arith.cmpi lt %8, %7
    cf.cond_br %9 [then: inner_1, else: inner_1.exit]
  inner_1:
    %10 = memref.load i : i64
    %11 = memref.load j : i64
    %12 = arith.cmpi eq %10, %11
    cf.cond_br %12 [then: diag_2, else: diag_2.merge]
  diag_2:
    %13 = arith.constant {value = 1 : i64}
    %14 = memref.load total : i64
    %15 = arith.addi %14, %13
    memref.store %15, total
    cf.br diag_2.merge
  diag_2.merge:
    %16 = arith.constant {value = 1 : i64}
    %17 = memref.load j : i64
    %18 = arith.addi %17, %16
    memref.store %18, j
    cf.br inner_1.header
  inner_1.exit:
    %19 = arith.constant {value = 1 : i64}
    %20 = memref.load i : i64
    %21 = arith.addi %20, %19
    memref.store %21, i
    cf.br outer_0.header
  outer_0.exit:
    %22 = memref.load sentinel : i64
    %23 = memref.load total : i64
    %24 = arith.addi %22, %23
    func.return %24
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov eax, 100
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.jmp register-allocator.main.outer_0.header
  outer_0.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.outer_0.exit
  outer_0:
    x86.xor eax, eax
    x86.mov [rbp-32], eax
    x86.jmp register-allocator.main.inner_1.header
  inner_1.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-32]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.inner_1.exit
  inner_1:
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-32]
    x86.cmp eax, ecx
    x86.jne register-allocator.main.diag_2.merge
  diag_2:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.jmp register-allocator.main.diag_2.merge
  diag_2.merge:
    x86.mov eax, 1
    x86.mov ecx, [rbp-32]
    x86.add ecx, eax
    x86.mov [rbp-32], ecx
    x86.jmp register-allocator.main.inner_1.header
  inner_1.exit:
    x86.mov eax, 1
    x86.mov ecx, [rbp-24]
    x86.add ecx, eax
    x86.mov [rbp-24], ecx
    x86.jmp register-allocator.main.outer_0.header
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
  func @register-allocator.main() -> i64 {
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
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 1 : i64}
    memref.store %1, b
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, i
    cf.br loop_0.header
  loop_0.header:
    %3 = arith.constant {value = 13 : i64}
    %4 = memref.load i : i64
    %5 = arith.cmpi lt %4, %3
    cf.cond_br %5 [then: loop_0, else: loop_0.exit]
  loop_0:
    %6 = memref.load a : i64
    %7 = memref.load b : i64
    %8 = arith.addi %6, %7
    memref.store %8, temp
    %9 = memref.load b : i64
    memref.store %9, a
    memref.store %8, b
    %10 = arith.constant {value = 1 : i64}
    %11 = memref.load i : i64
    %12 = arith.addi %11, %10
    memref.store %12, i
    cf.br loop_0.header
  loop_0.exit:
    %13 = memref.load a : i64
    func.return %13
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.jmp register-allocator.main.loop_0.header
  loop_0.header:
    x86.mov eax, 13
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jge register-allocator.main.loop_0.exit
  loop_0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.add eax, ecx
    x86.mov edx, [rbp-16]
    x86.mov [rbp-8], edx
    x86.mov [rbp-16], eax
    x86.mov ebx, 1
    x86.mov esi, [rbp-24]
    x86.add esi, ebx
    x86.mov [rbp-24], esi
    x86.jmp register-allocator.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-division-high-pressure -->
```maxon
function main() returns int
  var a = 10
  var b = 20
  var c = 30
  var d = 40
  var e = 50
  var f = 60
  var g = 70
  var h = 2
  return trunc((a + b + c + d + e + f + g) / h)
end 'main'
```
```exitcode
140
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 20 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 30 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 40 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 50 : i64}
    maxon.assign %4 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 60 : i64}
    maxon.assign %5 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 70 : i64}
    maxon.assign %6 {var = g} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 2 : i64}
    maxon.assign %7 {var = h} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.binop %0, %1 {op = add} {kind = i64}
    %9 = maxon.binop %8, %2 {op = add} {kind = i64}
    %10 = maxon.binop %9, %3 {op = add} {kind = i64}
    %11 = maxon.binop %10, %4 {op = add} {kind = i64}
    %12 = maxon.binop %11, %5 {op = add} {kind = i64}
    %13 = maxon.binop %12, %6 {op = add} {kind = i64}
    %14 = maxon.int_to_float %13
    %15 = maxon.int_to_float %7
    %16 = maxon.binop %14, %15 {op = div} {kind = f64}
    %17 = maxon.trunc %16
    maxon.return %17
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 20 : i64}
    memref.store %1, b
    %2 = arith.constant {value = 30 : i64}
    memref.store %2, c
    %3 = arith.constant {value = 40 : i64}
    memref.store %3, d
    %4 = arith.constant {value = 50 : i64}
    memref.store %4, e
    %5 = arith.constant {value = 60 : i64}
    memref.store %5, f
    %6 = arith.constant {value = 70 : i64}
    memref.store %6, g
    %7 = arith.constant {value = 2 : i64}
    memref.store %7, h
    %8 = arith.addi %0, %1
    %9 = arith.addi %8, %2
    %10 = arith.addi %9, %3
    %11 = arith.addi %10, %4
    %12 = arith.addi %11, %5
    %13 = arith.addi %12, %6
    %14 = arith.sitofp %13
    %15 = arith.sitofp %7
    %16 = arith.divf %14, %15
    %17 = arith.fptosi %16
    func.return %17
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 10
    x86.mov ecx, 20
    x86.mov edx, 30
    x86.mov ebx, 40
    x86.mov esi, 50
    x86.mov edi, 60
    x86.mov r8, 70
    x86.mov r9, 2
    x86.add eax, ecx
    x86.add eax, edx
    x86.add eax, ebx
    x86.add eax, esi
    x86.add eax, edi
    x86.add eax, r8
    x86.cvtsi2sd xmm0, eax
    x86.cvtsi2sd xmm1, r9
    x86.movsd xmm2, xmm0
    x86.divsd xmm2, xmm1
    x86.cvttsd2si eax, xmm2
    x86.ret
  }
}
```

<!-- test: int-callee-saved-clobber -->
```maxon
function useRegs(a int, b int, c int, d int) returns int
  var x = a + b
  var y = c + d
  var z = x + y
  return z
end 'useRegs'

function main() returns int
  var sentinel = 42
  var result = useRegs(1, b: 2, c: 3, d: 4)
  return sentinel + result
end 'main'
```
```exitcode
52
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.param {index = 2 : i32} {name = c} {type = i64}
    %3 = maxon.param {index = 3 : i32} {name = d} {type = i64}
    %4 = maxon.binop %0, %1 {op = add} {kind = i64}
    maxon.assign %4 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %2, %3 {op = add} {kind = i64}
    maxon.assign %5 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.binop %4, %5 {op = add} {kind = i64}
    maxon.assign %6 {var = z} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.return %6
  }
  func @register-allocator.main() -> i64 {
  entry:
    %7 = maxon.literal {value = 42 : i64}
    maxon.assign %7 {var = sentinel} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 1 : i64}
    %9 = maxon.literal {value = 2 : i64}
    %10 = maxon.literal {value = 3 : i64}
    %11 = maxon.literal {value = 4 : i64}
    %12 = maxon.call @register-allocator.useRegs %8, %9, %10, %11
    maxon.assign %12 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.binop %7, %12 {op = add} {kind = i64}
    maxon.return %13
  }
}
=== standard
module {
  func @register-allocator.useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    memref.store %0, a
    %1 = func.param b : StdI64
    memref.store %1, b
    %2 = func.param c : StdI64
    memref.store %2, c
    %3 = func.param d : StdI64
    memref.store %3, d
    %4 = arith.addi %0, %1
    memref.store %4, x
    %5 = arith.addi %2, %3
    memref.store %5, y
    %6 = arith.addi %4, %5
    memref.store %6, z
    func.return %6
  }
  func @register-allocator.main() -> i64 {
  entry:
    %7 = arith.constant {value = 42 : i64}
    memref.store %7, sentinel
    %8 = arith.constant {value = 1 : i64}
    %9 = arith.constant {value = 2 : i64}
    %10 = arith.constant {value = 3 : i64}
    %11 = arith.constant {value = 4 : i64}
    %12 = func.call @register-allocator.useRegs %8, %9, %10, %11
    memref.store %12, result
    %13 = arith.addi %7, %12
    func.return %13
  }
}
=== x86
module {
  func @register-allocator.useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    x86.add ecx, edx
    x86.add r8, r9
    x86.lea eax, [ecx + r8]
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 42
    x86.mov ecx, 1
    x86.mov edx, 2
    x86.mov ebx, 3
    x86.mov esi, 4
    x86.mov [rbp-8], eax
    x86.mov r8, rbx
    x86.mov r9, rsi
    x86.call register-allocator.useRegs
    x86.mov edi, [rbp-8]
    x86.lea eax, [edi + eax]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-float-survives-call -->
```maxon
function getInt() returns int
  return 40
end 'getInt'

function main() returns int
  var f = 3.14
  var x = getInt()
  return trunc(f) + x
end 'main'
```
```exitcode
43
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.getInt() -> i64 {
  entry:
    %0 = maxon.literal {value = 40 : i64}
    maxon.return %0
  }
  func @register-allocator.main() -> i64 {
  entry:
    %1 = maxon.literal {value = 3.14 : f64}
    maxon.assign %1 {var = f} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.call @register-allocator.getInt
    maxon.assign %2 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.trunc %1
    %4 = maxon.binop %3, %2 {op = add} {kind = i64}
    maxon.return %4
  }
}
=== standard
module {
  func @register-allocator.getInt() -> i64 {
  entry:
    %0 = arith.constant {value = 40 : i64}
    func.return %0
  }
  func @register-allocator.main() -> i64 {
  entry:
    %1 = arith.float_constant {value = 3.14 : f64}
    memref.store %1, f
    %2 = func.call @register-allocator.getInt
    memref.store %2, x
    %3 = arith.fptosi %1
    %4 = arith.addi %3, %2
    func.return %4
  }
}
=== x86
module {
  func @register-allocator.getInt() -> i64 {
  entry:
    x86.mov eax, 40
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.movsd xmm0, [rip+__float_3.14]
    x86.movsd [rbp-8], xmm0
    x86.call register-allocator.getInt
    x86.movsd xmm0, [rbp-8]
    x86.cvttsd2si ecx, xmm0
    x86.lea eax, [ecx + eax]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-sequential-divisions -->
```maxon
function main() returns int
  var a = 100
  var b = 5
  var c = 84
  var d = 4
  return trunc(a / b + c / d)
end 'main'
```
```exitcode
41
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 5 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 84 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 4 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.int_to_float %0
    %5 = maxon.int_to_float %1
    %6 = maxon.binop %4, %5 {op = div} {kind = f64}
    %7 = maxon.int_to_float %2
    %8 = maxon.int_to_float %3
    %9 = maxon.binop %7, %8 {op = div} {kind = f64}
    %10 = maxon.binop %6, %9 {op = add} {kind = f64}
    %11 = maxon.trunc %10
    maxon.return %11
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 5 : i64}
    memref.store %1, b
    %2 = arith.constant {value = 84 : i64}
    memref.store %2, c
    %3 = arith.constant {value = 4 : i64}
    memref.store %3, d
    %4 = arith.sitofp %0
    %5 = arith.sitofp %1
    %6 = arith.divf %4, %5
    %7 = arith.sitofp %2
    %8 = arith.sitofp %3
    %9 = arith.divf %7, %8
    %10 = arith.addf %6, %9
    %11 = arith.fptosi %10
    func.return %11
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 100
    x86.mov ecx, 5
    x86.mov edx, 84
    x86.mov ebx, 4
    x86.cvtsi2sd xmm0, eax
    x86.cvtsi2sd xmm1, ecx
    x86.movsd xmm2, xmm0
    x86.divsd xmm2, xmm1
    x86.cvtsi2sd xmm3, edx
    x86.cvtsi2sd xmm4, ebx
    x86.movsd xmm5, xmm3
    x86.divsd xmm5, xmm4
    x86.movsd xmm6, xmm2
    x86.addsd xmm6, xmm5
    x86.cvttsd2si esi, xmm6
    x86.mov eax, esi
    x86.ret
  }
}
```

<!-- test: int-remainder-in-arithmetic -->
```maxon
function main() returns int
  var a = 100
  var b = 7
  var c = 10
  var rem = a mod b
  return rem * c
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 7 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 10 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %0, %1 {op = mod} {kind = i64}
    maxon.assign %3 {var = rem} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.binop %3, %2 {op = mul} {kind = i64}
    maxon.return %4
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 7 : i64}
    memref.store %1, b
    %2 = arith.constant {value = 10 : i64}
    memref.store %2, c
    %3 = arith.remsi %0, %1
    memref.store %3, rem
    %4 = arith.muli %3, %2
    func.return %4
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 100
    x86.mov ecx, 7
    x86.mov edx, 10
    x86.mov [rbp-8], eax
    x86.mov [rbp-16], edx
    x86.cqo
    x86.idiv ecx
    x86.mov ebx, [rbp-16]
    x86.imul edx, ebx
    x86.mov eax, edx
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: int-call-arg-reverse -->
```maxon
function sub(a int, b int) returns int
  return a - b
end 'sub'

function main() returns int
  var x = 10
  var y = 3
  var result = sub(y, b: x)
  return result + 45
end 'main'
```
```exitcode
38
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.sub(a: i64, b: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.binop %0, %1 {op = sub} {kind = i64}
    maxon.return %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3 = maxon.literal {value = 10 : i64}
    maxon.assign %3 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 3 : i64}
    maxon.assign %4 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.call @register-allocator.sub %4, %3
    maxon.assign %5 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 45 : i64}
    %7 = maxon.binop %5, %6 {op = add} {kind = i64}
    maxon.return %7
  }
}
=== standard
module {
  func @register-allocator.sub(a: i64, b: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    memref.store %0, a
    %1 = func.param b : StdI64
    memref.store %1, b
    %2 = arith.subi %0, %1
    func.return %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3 = arith.constant {value = 10 : i64}
    memref.store %3, x
    %4 = arith.constant {value = 3 : i64}
    memref.store %4, y
    %5 = func.call @register-allocator.sub %4, %3
    memref.store %5, result
    %6 = arith.constant {value = 45 : i64}
    %7 = arith.addi %5, %6
    func.return %7
  }
}
=== x86
module {
  func @register-allocator.sub(a: i64, b: i64) -> i64 {
  entry:
    x86.sub ecx, edx
    x86.mov eax, ecx
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 10
    x86.mov ecx, 3
    x86.mov rdx, rax
    x86.call register-allocator.sub
    x86.mov edx, 45
    x86.add eax, edx
    x86.ret
  }
}
```

<!-- test: int-subtraction-high-pressure -->
```maxon
function main() returns int
  var a = 100
  var b = 1
  var c = 2
  var d = 3
  var e = 4
  var f = 5
  var g = 6
  var h = 7
  return a - b - c - d - e - f - g - h
end 'main'
```
```exitcode
72
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 2 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 4 : i64}
    maxon.assign %4 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 5 : i64}
    maxon.assign %5 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 6 : i64}
    maxon.assign %6 {var = g} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 7 : i64}
    maxon.assign %7 {var = h} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.binop %0, %1 {op = sub} {kind = i64}
    %9 = maxon.binop %8, %2 {op = sub} {kind = i64}
    %10 = maxon.binop %9, %3 {op = sub} {kind = i64}
    %11 = maxon.binop %10, %4 {op = sub} {kind = i64}
    %12 = maxon.binop %11, %5 {op = sub} {kind = i64}
    %13 = maxon.binop %12, %6 {op = sub} {kind = i64}
    %14 = maxon.binop %13, %7 {op = sub} {kind = i64}
    maxon.return %14
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    memref.store %0, a
    %1 = arith.constant {value = 1 : i64}
    memref.store %1, b
    %2 = arith.constant {value = 2 : i64}
    memref.store %2, c
    %3 = arith.constant {value = 3 : i64}
    memref.store %3, d
    %4 = arith.constant {value = 4 : i64}
    memref.store %4, e
    %5 = arith.constant {value = 5 : i64}
    memref.store %5, f
    %6 = arith.constant {value = 6 : i64}
    memref.store %6, g
    %7 = arith.constant {value = 7 : i64}
    memref.store %7, h
    %8 = arith.subi %0, %1
    %9 = arith.subi %8, %2
    %10 = arith.subi %9, %3
    %11 = arith.subi %10, %4
    %12 = arith.subi %11, %5
    %13 = arith.subi %12, %6
    %14 = arith.subi %13, %7
    func.return %14
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.mov eax, 100
    x86.mov ecx, 1
    x86.mov edx, 2
    x86.mov ebx, 3
    x86.mov esi, 4
    x86.mov edi, 5
    x86.mov r8, 6
    x86.mov r9, 7
    x86.sub eax, ecx
    x86.sub eax, edx
    x86.sub eax, ebx
    x86.sub eax, esi
    x86.sub eax, edi
    x86.sub eax, r8
    x86.sub eax, r9
    x86.ret
  }
}
```

<!-- test: int-multi-var-branch-merge -->
```maxon
function main() returns int
  var x = 0
  var y = 0
  var z = 0
  if 1 < 2 'branch'
    x = 10
    y = 20
    z = 12
  end 'branch' else 'other'
    x = 1
    y = 2
    z = 3
  end 'other'
  return x + y + z
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = z} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.literal {value = 2 : i64}
    %5 = maxon.binop %3, %4 {op = lt} {kind = i64}
    maxon.cond_br %5 [then: branch_0, else: other_1]
  branch_0:
    %6 = maxon.literal {value = 10 : i64}
    maxon.assign %6 {var = x} {kind = i64} {mut = 1 : i1}
    %7 = maxon.literal {value = 20 : i64}
    maxon.assign %7 {var = y} {kind = i64} {mut = 1 : i1}
    %8 = maxon.literal {value = 12 : i64}
    maxon.assign %8 {var = z} {kind = i64} {mut = 1 : i1}
    maxon.br branch_0.merge
  other_1:
    %9 = maxon.literal {value = 1 : i64}
    maxon.assign %9 {var = x} {kind = i64} {mut = 1 : i1}
    %10 = maxon.literal {value = 2 : i64}
    maxon.assign %10 {var = y} {kind = i64} {mut = 1 : i1}
    %11 = maxon.literal {value = 3 : i64}
    maxon.assign %11 {var = z} {kind = i64} {mut = 1 : i1}
    maxon.br branch_0.merge
  branch_0.merge:
    %12 = maxon.var_ref {var = x} {type = i64}
    %13 = maxon.var_ref {var = y} {type = i64}
    %14 = maxon.binop %12, %13 {op = add} {kind = i64}
    %15 = maxon.var_ref {var = z} {type = i64}
    %16 = maxon.binop %14, %15 {op = add} {kind = i64}
    maxon.return %16
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, x
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, y
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, z
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.constant {value = 2 : i64}
    %5 = arith.cmpi lt %3, %4
    cf.cond_br %5 [then: branch_0, else: other_1]
  branch_0:
    %6 = arith.constant {value = 10 : i64}
    memref.store %6, x
    %7 = arith.constant {value = 20 : i64}
    memref.store %7, y
    %8 = arith.constant {value = 12 : i64}
    memref.store %8, z
    cf.br branch_0.merge
  other_1:
    %9 = arith.constant {value = 1 : i64}
    memref.store %9, x
    %10 = arith.constant {value = 2 : i64}
    memref.store %10, y
    %11 = arith.constant {value = 3 : i64}
    memref.store %11, z
    cf.br branch_0.merge
  branch_0.merge:
    %12 = memref.load x : i64
    %13 = memref.load y : i64
    %14 = arith.addi %12, %13
    %15 = memref.load z : i64
    %16 = arith.addi %14, %15
    func.return %16
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.mov ebx, 1
    x86.mov esi, 2
    x86.cmp ebx, esi
    x86.jge register-allocator.main.other_1
  branch_0:
    x86.mov eax, 10
    x86.mov [rbp-8], eax
    x86.mov ecx, 20
    x86.mov [rbp-16], ecx
    x86.mov edx, 12
    x86.mov [rbp-24], edx
    x86.jmp register-allocator.main.branch_0.merge
  other_1:
    x86.mov eax, 1
    x86.mov [rbp-8], eax
    x86.mov ecx, 2
    x86.mov [rbp-16], ecx
    x86.mov edx, 3
    x86.mov [rbp-24], edx
    x86.jmp register-allocator.main.branch_0.merge
  branch_0.merge:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.add eax, ecx
    x86.mov edx, [rbp-24]
    x86.add eax, edx
    x86.epilogue
    x86.ret
  }
}
```

### Level 7: Match Statements and Expressions

<!-- test: match-statement-simple -->
```maxon
function main() returns int
  var x = 2
  match x 'check'
    1 then return 10
    2 then return 20
    default then return 0
  end 'check'
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 2 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_check_0} {kind = i64} {decl = 1 : i1}
    maxon.br check_0.cmp0
  check_0.cmp0:
    %1 = maxon.var_ref {var = __match_check_0} {type = i64}
    %2 = maxon.literal {value = 1 : i64}
    %3 = maxon.binop %1, %2 {op = eq} {kind = i64}
    maxon.cond_br %3 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %4 = maxon.literal {value = 10 : i64}
    maxon.return %4
  check_0.cmp1:
    %5 = maxon.var_ref {var = __match_check_0} {type = i64}
    %6 = maxon.literal {value = 2 : i64}
    %7 = maxon.binop %5, %6 {op = eq} {kind = i64}
    maxon.cond_br %7 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %8 = maxon.literal {value = 20 : i64}
    maxon.return %8
  check_0.case2:
    %9 = maxon.literal {value = 0 : i64}
    maxon.return %9
  check_0.merge:
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 2 : i64}
    memref.store %0, x
    memref.store %0, __match_check_0
    cf.br check_0.cmp0
  check_0.cmp0:
    %1 = memref.load __match_check_0 : i64
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.cmpi eq %1, %2
    cf.cond_br %3 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %4 = arith.constant {value = 10 : i64}
    func.return %4
  check_0.cmp1:
    %5 = memref.load __match_check_0 : i64
    %6 = arith.constant {value = 2 : i64}
    %7 = arith.cmpi eq %5, %6
    cf.cond_br %7 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %8 = arith.constant {value = 20 : i64}
    func.return %8
  check_0.case2:
    %9 = arith.constant {value = 0 : i64}
    func.return %9
  check_0.merge:
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 2
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.check_0.cmp0
  check_0.cmp0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.check_0.cmp1
  check_0.case0:
    x86.mov eax, 10
    x86.epilogue
    x86.ret
  check_0.cmp1:
    x86.mov eax, [rbp-8]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.check_0.case2
  check_0.case1:
    x86.mov eax, 20
    x86.epilogue
    x86.ret
  check_0.case2:
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  check_0.merge:
  }
}
```

<!-- test: match-statement-assignment -->
```maxon
function main() returns int
  var x = 2
  var result = 0
  match x 'process'
    1 then result = 100
    2 then result = 200
    default then result = 0
  end 'process'
  return result
end 'main'
```
```exitcode
200
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 2 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_process_0} {kind = i64} {decl = 1 : i1}
    maxon.br process_0.cmp0
  process_0.cmp0:
    %2 = maxon.var_ref {var = __match_process_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq} {kind = i64}
    maxon.cond_br %4 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    %5 = maxon.literal {value = 100 : i64}
    maxon.assign %5 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br process_0.merge
  process_0.cmp1:
    %6 = maxon.var_ref {var = __match_process_0} {type = i64}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = eq} {kind = i64}
    maxon.cond_br %8 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    %9 = maxon.literal {value = 200 : i64}
    maxon.assign %9 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br process_0.merge
  process_0.case2:
    %10 = maxon.literal {value = 0 : i64}
    maxon.assign %10 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br process_0.merge
  process_0.merge:
    %11 = maxon.var_ref {var = result} {type = i64}
    maxon.return %11
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 2 : i64}
    memref.store %0, x
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, result
    memref.store %0, __match_process_0
    cf.br process_0.cmp0
  process_0.cmp0:
    %2 = memref.load __match_process_0 : i64
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.cmpi eq %2, %3
    cf.cond_br %4 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    %5 = arith.constant {value = 100 : i64}
    memref.store %5, result
    cf.br process_0.merge
  process_0.cmp1:
    %6 = memref.load __match_process_0 : i64
    %7 = arith.constant {value = 2 : i64}
    %8 = arith.cmpi eq %6, %7
    cf.cond_br %8 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    %9 = arith.constant {value = 200 : i64}
    memref.store %9, result
    cf.br process_0.merge
  process_0.case2:
    %10 = arith.constant {value = 0 : i64}
    memref.store %10, result
    cf.br process_0.merge
  process_0.merge:
    %11 = memref.load result : i64
    func.return %11
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 2
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.process_0.cmp0
  process_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.process_0.cmp1
  process_0.case0:
    x86.mov eax, 100
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.process_0.merge
  process_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.process_0.case2
  process_0.case1:
    x86.mov eax, 200
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.process_0.merge
  process_0.case2:
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.process_0.merge
  process_0.merge:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: match-statement-or-patterns -->
```maxon
function main() returns int
  var x = 3
  match x 'check'
    1 or 2 then return 10
    3 or 4 then return 20
    default then return 0
  end 'check'
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_check_0} {kind = i64} {decl = 1 : i1}
    maxon.br check_0.cmp0
  check_0.cmp0:
    %1 = maxon.var_ref {var = __match_check_0} {type = i64}
    %2 = maxon.literal {value = 1 : i64}
    %3 = maxon.binop %1, %2 {op = eq} {kind = i64}
    %4 = maxon.literal {value = 2 : i64}
    %5 = maxon.binop %1, %4 {op = eq} {kind = i64}
    %6 = maxon.binop %3, %5 {op = or} {kind = i1}
    maxon.cond_br %6 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %7 = maxon.literal {value = 10 : i64}
    maxon.return %7
  check_0.cmp1:
    %8 = maxon.var_ref {var = __match_check_0} {type = i64}
    %9 = maxon.literal {value = 3 : i64}
    %10 = maxon.binop %8, %9 {op = eq} {kind = i64}
    %11 = maxon.literal {value = 4 : i64}
    %12 = maxon.binop %8, %11 {op = eq} {kind = i64}
    %13 = maxon.binop %10, %12 {op = or} {kind = i1}
    maxon.cond_br %13 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %14 = maxon.literal {value = 20 : i64}
    maxon.return %14
  check_0.case2:
    %15 = maxon.literal {value = 0 : i64}
    maxon.return %15
  check_0.merge:
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 3 : i64}
    memref.store %0, x
    memref.store %0, __match_check_0
    cf.br check_0.cmp0
  check_0.cmp0:
    %1 = memref.load __match_check_0 : i64
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.cmpi eq %1, %2
    %4 = arith.constant {value = 2 : i64}
    %5 = arith.cmpi eq %1, %4
    %6 = arith.ori1 %3, %5
    cf.cond_br %6 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %7 = arith.constant {value = 10 : i64}
    func.return %7
  check_0.cmp1:
    %8 = memref.load __match_check_0 : i64
    %9 = arith.constant {value = 3 : i64}
    %10 = arith.cmpi eq %8, %9
    %11 = arith.constant {value = 4 : i64}
    %12 = arith.cmpi eq %8, %11
    %13 = arith.ori1 %10, %12
    cf.cond_br %13 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %14 = arith.constant {value = 20 : i64}
    func.return %14
  check_0.case2:
    %15 = arith.constant {value = 0 : i64}
    func.return %15
  check_0.merge:
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 3
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.check_0.cmp0
  check_0.cmp0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.sete edx
    x86.movzx edx, edxb
    x86.mov ebx, 2
    x86.cmp eax, ebx
    x86.sete esi
    x86.movzx esi, esib
    x86.or edx, esi
    x86.test edx, edx
    x86.je register-allocator.main.check_0.cmp1
  check_0.case0:
    x86.mov eax, 10
    x86.epilogue
    x86.ret
  check_0.cmp1:
    x86.mov eax, [rbp-8]
    x86.mov ecx, 3
    x86.cmp eax, ecx
    x86.sete edx
    x86.movzx edx, edxb
    x86.mov ebx, 4
    x86.cmp eax, ebx
    x86.sete esi
    x86.movzx esi, esib
    x86.or edx, esi
    x86.test edx, edx
    x86.je register-allocator.main.check_0.case2
  check_0.case1:
    x86.mov eax, 20
    x86.epilogue
    x86.ret
  check_0.case2:
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  check_0.merge:
  }
}
```

<!-- test: match-statement-fallthrough -->
```maxon
function main() returns int
  var x = 1
  var result = 0
  match x 'cascade'
    1 then result = result + 10 and fallthrough
    2 then result = result + 20 and fallthrough
    3 then result = result + 30
    default then result = 100
  end 'cascade'
  return result
end 'main'
```
```exitcode
60
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_cascade_0} {kind = i64} {decl = 1 : i1}
    maxon.br cascade_0.cmp0
  cascade_0.cmp0:
    %2 = maxon.var_ref {var = __match_cascade_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq} {kind = i64}
    maxon.cond_br %4 [then: cascade_0.case0, else: cascade_0.cmp1]
  cascade_0.case0:
    %5 = maxon.literal {value = 10 : i64}
    %6 = maxon.var_ref {var = result} {type = i64}
    %7 = maxon.binop %6, %5 {op = add} {kind = i64}
    maxon.assign %7 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br cascade_0.case1
  cascade_0.cmp1:
    %8 = maxon.var_ref {var = __match_cascade_0} {type = i64}
    %9 = maxon.literal {value = 2 : i64}
    %10 = maxon.binop %8, %9 {op = eq} {kind = i64}
    maxon.cond_br %10 [then: cascade_0.case1, else: cascade_0.cmp2]
  cascade_0.case1:
    %11 = maxon.literal {value = 20 : i64}
    %12 = maxon.var_ref {var = result} {type = i64}
    %13 = maxon.binop %12, %11 {op = add} {kind = i64}
    maxon.assign %13 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br cascade_0.case2
  cascade_0.cmp2:
    %14 = maxon.var_ref {var = __match_cascade_0} {type = i64}
    %15 = maxon.literal {value = 3 : i64}
    %16 = maxon.binop %14, %15 {op = eq} {kind = i64}
    maxon.cond_br %16 [then: cascade_0.case2, else: cascade_0.case3]
  cascade_0.case2:
    %17 = maxon.literal {value = 30 : i64}
    %18 = maxon.var_ref {var = result} {type = i64}
    %19 = maxon.binop %18, %17 {op = add} {kind = i64}
    maxon.assign %19 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br cascade_0.merge
  cascade_0.case3:
    %20 = maxon.literal {value = 100 : i64}
    maxon.assign %20 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br cascade_0.merge
  cascade_0.merge:
    %21 = maxon.var_ref {var = result} {type = i64}
    maxon.return %21
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    memref.store %0, x
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, result
    memref.store %0, __match_cascade_0
    cf.br cascade_0.cmp0
  cascade_0.cmp0:
    %2 = memref.load __match_cascade_0 : i64
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.cmpi eq %2, %3
    cf.cond_br %4 [then: cascade_0.case0, else: cascade_0.cmp1]
  cascade_0.case0:
    %5 = arith.constant {value = 10 : i64}
    %6 = memref.load result : i64
    %7 = arith.addi %6, %5
    memref.store %7, result
    cf.br cascade_0.case1
  cascade_0.cmp1:
    %8 = memref.load __match_cascade_0 : i64
    %9 = arith.constant {value = 2 : i64}
    %10 = arith.cmpi eq %8, %9
    cf.cond_br %10 [then: cascade_0.case1, else: cascade_0.cmp2]
  cascade_0.case1:
    %11 = arith.constant {value = 20 : i64}
    %12 = memref.load result : i64
    %13 = arith.addi %12, %11
    memref.store %13, result
    cf.br cascade_0.case2
  cascade_0.cmp2:
    %14 = memref.load __match_cascade_0 : i64
    %15 = arith.constant {value = 3 : i64}
    %16 = arith.cmpi eq %14, %15
    cf.cond_br %16 [then: cascade_0.case2, else: cascade_0.case3]
  cascade_0.case2:
    %17 = arith.constant {value = 30 : i64}
    %18 = memref.load result : i64
    %19 = arith.addi %18, %17
    memref.store %19, result
    cf.br cascade_0.merge
  cascade_0.case3:
    %20 = arith.constant {value = 100 : i64}
    memref.store %20, result
    cf.br cascade_0.merge
  cascade_0.merge:
    %21 = memref.load result : i64
    func.return %21
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 1
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.cascade_0.cmp0
  cascade_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.cascade_0.cmp1
  cascade_0.case0:
    x86.mov eax, 10
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.jmp register-allocator.main.cascade_0.case1
  cascade_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.cascade_0.cmp2
  cascade_0.case1:
    x86.mov eax, 20
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.jmp register-allocator.main.cascade_0.case2
  cascade_0.cmp2:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 3
    x86.cmp eax, ecx
    x86.jne register-allocator.main.cascade_0.case3
  cascade_0.case2:
    x86.mov eax, 30
    x86.mov ecx, [rbp-8]
    x86.add ecx, eax
    x86.mov [rbp-8], ecx
    x86.jmp register-allocator.main.cascade_0.merge
  cascade_0.case3:
    x86.mov eax, 100
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.cascade_0.merge
  cascade_0.merge:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: match-expression-basic -->
```maxon
function main() returns int
  var x = 2
  let result = match x 'eval'
    1 gives 10
    2 gives 20
    default gives 0
  end 'eval'
  return result
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 2 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = __matchexpr_eval_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_eval_0} {kind = i64} {decl = 1 : i1}
    maxon.br eval_0.cmp0
  eval_0.cmp0:
    %2 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq} {kind = i64}
    maxon.cond_br %4 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %5 = maxon.literal {value = 10 : i64}
    maxon.assign %5 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %6 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = eq} {kind = i64}
    maxon.cond_br %8 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %9 = maxon.literal {value = 20 : i64}
    maxon.assign %9 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.case2:
    %10 = maxon.literal {value = 0 : i64}
    maxon.assign %10 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.merge:
    %11 = maxon.var_ref {var = __matchexpr_eval_0} {type = i64}
    maxon.assign %11 {var = result} {kind = i64} {decl = 1 : i1}
    maxon.return %11
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 2 : i64}
    memref.store %0, x
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, __matchexpr_eval_0
    memref.store %0, __match_eval_0
    cf.br eval_0.cmp0
  eval_0.cmp0:
    %2 = memref.load __match_eval_0 : i64
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.cmpi eq %2, %3
    cf.cond_br %4 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %5 = arith.constant {value = 10 : i64}
    memref.store %5, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.cmp1:
    %6 = memref.load __match_eval_0 : i64
    %7 = arith.constant {value = 2 : i64}
    %8 = arith.cmpi eq %6, %7
    cf.cond_br %8 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %9 = arith.constant {value = 20 : i64}
    memref.store %9, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.case2:
    %10 = arith.constant {value = 0 : i64}
    memref.store %10, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.merge:
    %11 = memref.load __matchexpr_eval_0 : i64
    memref.store %11, result
    func.return %11
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 2
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.eval_0.cmp0
  eval_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.eval_0.cmp1
  eval_0.case0:
    x86.mov eax, 10
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.eval_0.case2
  eval_0.case1:
    x86.mov eax, 20
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.case2:
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.merge:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: match-expression-or-patterns -->
```maxon
function main() returns int
  var x = 4
  let result = match x 'eval'
    1 or 2 gives 10
    3 or 4 gives 20
    default gives 0
  end 'eval'
  return result
end 'main'
```
```exitcode
20
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 4 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = __matchexpr_eval_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_eval_0} {kind = i64} {decl = 1 : i1}
    maxon.br eval_0.cmp0
  eval_0.cmp0:
    %2 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq} {kind = i64}
    %5 = maxon.literal {value = 2 : i64}
    %6 = maxon.binop %2, %5 {op = eq} {kind = i64}
    %7 = maxon.binop %4, %6 {op = or} {kind = i1}
    maxon.cond_br %7 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %8 = maxon.literal {value = 10 : i64}
    maxon.assign %8 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %9 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %10 = maxon.literal {value = 3 : i64}
    %11 = maxon.binop %9, %10 {op = eq} {kind = i64}
    %12 = maxon.literal {value = 4 : i64}
    %13 = maxon.binop %9, %12 {op = eq} {kind = i64}
    %14 = maxon.binop %11, %13 {op = or} {kind = i1}
    maxon.cond_br %14 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %15 = maxon.literal {value = 20 : i64}
    maxon.assign %15 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.case2:
    %16 = maxon.literal {value = 0 : i64}
    maxon.assign %16 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.merge:
    %17 = maxon.var_ref {var = __matchexpr_eval_0} {type = i64}
    maxon.assign %17 {var = result} {kind = i64} {decl = 1 : i1}
    maxon.return %17
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 4 : i64}
    memref.store %0, x
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, __matchexpr_eval_0
    memref.store %0, __match_eval_0
    cf.br eval_0.cmp0
  eval_0.cmp0:
    %2 = memref.load __match_eval_0 : i64
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.cmpi eq %2, %3
    %5 = arith.constant {value = 2 : i64}
    %6 = arith.cmpi eq %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %8 = arith.constant {value = 10 : i64}
    memref.store %8, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.cmp1:
    %9 = memref.load __match_eval_0 : i64
    %10 = arith.constant {value = 3 : i64}
    %11 = arith.cmpi eq %9, %10
    %12 = arith.constant {value = 4 : i64}
    %13 = arith.cmpi eq %9, %12
    %14 = arith.ori1 %11, %13
    cf.cond_br %14 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %15 = arith.constant {value = 20 : i64}
    memref.store %15, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.case2:
    %16 = arith.constant {value = 0 : i64}
    memref.store %16, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.merge:
    %17 = memref.load __matchexpr_eval_0 : i64
    memref.store %17, result
    func.return %17
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 4
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.eval_0.cmp0
  eval_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.sete edx
    x86.movzx edx, edxb
    x86.mov ebx, 2
    x86.cmp eax, ebx
    x86.sete esi
    x86.movzx esi, esib
    x86.or edx, esi
    x86.test edx, edx
    x86.je register-allocator.main.eval_0.cmp1
  eval_0.case0:
    x86.mov eax, 10
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 3
    x86.cmp eax, ecx
    x86.sete edx
    x86.movzx edx, edxb
    x86.mov ebx, 4
    x86.cmp eax, ebx
    x86.sete esi
    x86.movzx esi, esib
    x86.or edx, esi
    x86.test edx, edx
    x86.je register-allocator.main.eval_0.case2
  eval_0.case1:
    x86.mov eax, 20
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.case2:
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.merge:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: match-expression-in-arithmetic -->
```maxon
function main() returns int
  var x = 2
  let doubled = match x 'eval'
    1 gives 10
    2 gives 20
    default gives 0
  end 'eval' * 2
  return doubled
end 'main'
```
```exitcode
40
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = maxon.literal {value = 2 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = __matchexpr_eval_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_eval_0} {kind = i64} {decl = 1 : i1}
    maxon.br eval_0.cmp0
  eval_0.cmp0:
    %2 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq} {kind = i64}
    maxon.cond_br %4 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %5 = maxon.literal {value = 10 : i64}
    maxon.assign %5 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %6 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = eq} {kind = i64}
    maxon.cond_br %8 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %9 = maxon.literal {value = 20 : i64}
    maxon.assign %9 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.case2:
    %10 = maxon.literal {value = 0 : i64}
    maxon.assign %10 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.merge:
    %11 = maxon.var_ref {var = __matchexpr_eval_0} {type = i64}
    %12 = maxon.literal {value = 2 : i64}
    %13 = maxon.binop %11, %12 {op = mul} {kind = i64}
    maxon.assign %13 {var = doubled} {kind = i64} {decl = 1 : i1}
    maxon.return %13
  }
}
=== standard
module {
  func @register-allocator.main() -> i64 {
  entry:
    %0 = arith.constant {value = 2 : i64}
    memref.store %0, x
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, __matchexpr_eval_0
    memref.store %0, __match_eval_0
    cf.br eval_0.cmp0
  eval_0.cmp0:
    %2 = memref.load __match_eval_0 : i64
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.cmpi eq %2, %3
    cf.cond_br %4 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %5 = arith.constant {value = 10 : i64}
    memref.store %5, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.cmp1:
    %6 = memref.load __match_eval_0 : i64
    %7 = arith.constant {value = 2 : i64}
    %8 = arith.cmpi eq %6, %7
    cf.cond_br %8 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %9 = arith.constant {value = 20 : i64}
    memref.store %9, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.case2:
    %10 = arith.constant {value = 0 : i64}
    memref.store %10, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.merge:
    %11 = memref.load __matchexpr_eval_0 : i64
    %12 = arith.constant {value = 2 : i64}
    %13 = arith.muli %11, %12
    memref.store %13, doubled
    func.return %13
  }
}
=== x86
module {
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 2
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.eval_0.cmp0
  eval_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.eval_0.cmp1
  eval_0.case0:
    x86.mov eax, 10
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.eval_0.case2
  eval_0.case1:
    x86.mov eax, 20
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.case2:
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.eval_0.merge
  eval_0.merge:
    x86.mov eax, [rbp-8]
    x86.mov ecx, 2
    x86.imul eax, ecx
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: match-statement-with-function-call -->
```maxon
function double(n int) returns int
  return n * 2
end 'double'

function main() returns int
  var x = 2
  var result = 0
  match x 'process'
    1 then result = double(10)
    2 then result = double(20)
    default then result = 0
  end 'process'
  return result
end 'main'
```
```exitcode
40
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.double(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {kind = i64}
    maxon.return %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3 = maxon.literal {value = 2 : i64}
    maxon.assign %3 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    maxon.assign %4 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %3 {var = __match_process_0} {kind = i64} {decl = 1 : i1}
    maxon.br process_0.cmp0
  process_0.cmp0:
    %5 = maxon.var_ref {var = __match_process_0} {type = i64}
    %6 = maxon.literal {value = 1 : i64}
    %7 = maxon.binop %5, %6 {op = eq} {kind = i64}
    maxon.cond_br %7 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    %8 = maxon.literal {value = 10 : i64}
    %9 = maxon.call @register-allocator.double %8
    maxon.assign %9 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br process_0.merge
  process_0.cmp1:
    %10 = maxon.var_ref {var = __match_process_0} {type = i64}
    %11 = maxon.literal {value = 2 : i64}
    %12 = maxon.binop %10, %11 {op = eq} {kind = i64}
    maxon.cond_br %12 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    %13 = maxon.literal {value = 20 : i64}
    %14 = maxon.call @register-allocator.double %13
    maxon.assign %14 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br process_0.merge
  process_0.case2:
    %15 = maxon.literal {value = 0 : i64}
    maxon.assign %15 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br process_0.merge
  process_0.merge:
    %16 = maxon.var_ref {var = result} {type = i64}
    maxon.return %16
  }
}
=== standard
module {
  func @register-allocator.double(n: i64) -> i64 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, n
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.muli %0, %1
    func.return %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3 = arith.constant {value = 2 : i64}
    memref.store %3, x
    %4 = arith.constant {value = 0 : i64}
    memref.store %4, result
    memref.store %3, __match_process_0
    cf.br process_0.cmp0
  process_0.cmp0:
    %5 = memref.load __match_process_0 : i64
    %6 = arith.constant {value = 1 : i64}
    %7 = arith.cmpi eq %5, %6
    cf.cond_br %7 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    %8 = arith.constant {value = 10 : i64}
    %9 = func.call @register-allocator.double %8
    memref.store %9, result
    cf.br process_0.merge
  process_0.cmp1:
    %10 = memref.load __match_process_0 : i64
    %11 = arith.constant {value = 2 : i64}
    %12 = arith.cmpi eq %10, %11
    cf.cond_br %12 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    %13 = arith.constant {value = 20 : i64}
    %14 = func.call @register-allocator.double %13
    memref.store %14, result
    cf.br process_0.merge
  process_0.case2:
    %15 = arith.constant {value = 0 : i64}
    memref.store %15, result
    cf.br process_0.merge
  process_0.merge:
    %16 = memref.load result : i64
    func.return %16
  }
}
=== x86
module {
  func @register-allocator.double(n: i64) -> i64 {
  entry:
    x86.mov eax, 2
    x86.imul ecx, eax
    x86.mov eax, ecx
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 2
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.process_0.cmp0
  process_0.cmp0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.main.process_0.cmp1
  process_0.case0:
    x86.mov eax, 10
    x86.mov rcx, rax
    x86.call register-allocator.double
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.process_0.merge
  process_0.cmp1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.main.process_0.case2
  process_0.case1:
    x86.mov eax, 20
    x86.mov rcx, rax
    x86.call register-allocator.double
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.process_0.merge
  process_0.case2:
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.process_0.merge
  process_0.merge:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

### Level 8: Error Handling

<!-- test: error-otherwise-ignore -->
```maxon
enum MyError is Error
  failed
end 'MyError'

function mayFail() returns int throws MyError
  throw MyError.failed
end 'mayFail'

function main() returns int
  try mayFail() otherwise ignore
  return 42
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    %0 = maxon.enum_literal @MyError.failed
    maxon.throw @MyError %0
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3, %2 = maxon.try_call @register-allocator.mayFail
    %4 = maxon.literal {value = 42 : i64}
    maxon.return %4
  }
}
=== standard
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = arith.constant {value = 1 : i64}
    %2 = arith.addi %0, %1
    func.error_return %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3, %4 = func.try_call @register-allocator.mayFail
    memref.store %4, __error_flag
    %5 = arith.constant {value = 42 : i64}
    func.return %5
  }
}
=== x86
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov edx, eax
    x86.xor eax, eax
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.call register-allocator.mayFail
    x86.mov ecx, 42
    x86.mov eax, ecx
    x86.ret
  }
}
```

<!-- test: error-otherwise-block -->
```maxon
enum MyError is Error
  failed
end 'MyError'

function mayFail() returns int throws MyError
  throw MyError.failed
end 'mayFail'

function main() returns int
  var result = 0
  try mayFail() otherwise 'err'
    result = 42
  end 'err'
  return result
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    %0 = maxon.enum_literal @MyError.failed
    maxon.throw @MyError %0
  }
  func @register-allocator.main() -> i64 {
  entry:
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4, %3 = maxon.try_call @register-allocator.mayFail
    maxon.assign %3 {var = __try_error_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %4 {var = __try_result_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %3, %5 {op = ne} {kind = i64}
    maxon.cond_br %6 [then: otherwise_error_0, else: otherwise_continue_1]
  otherwise_error_0:
    %7 = maxon.literal {value = 42 : i64}
    maxon.assign %7 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_continue_1
  otherwise_continue_1:
    %8 = maxon.var_ref {var = __try_result_3} {type = i64}
    %9 = maxon.var_ref {var = result} {type = i64}
    maxon.return %9
  }
}
=== standard
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = arith.constant {value = 1 : i64}
    %2 = arith.addi %0, %1
    func.error_return %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, result
    %4, %5 = func.try_call @register-allocator.mayFail
    memref.store %5, __error_flag
    memref.store %5, __try_error_2
    memref.store %4, __try_result_3
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi ne %5, %6
    cf.cond_br %7 [then: otherwise_error_0, else: otherwise_continue_1]
  otherwise_error_0:
    %8 = arith.constant {value = 42 : i64}
    memref.store %8, result
    cf.br otherwise_continue_1
  otherwise_continue_1:
    %9 = memref.load __try_result_3 : i64
    %10 = memref.load result : i64
    func.return %10
  }
}
=== x86
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov edx, eax
    x86.xor eax, eax
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.call register-allocator.mayFail
    x86.mov [rbp-16], eax
    x86.xor ecx, ecx
    x86.cmp edx, ecx
    x86.je register-allocator.main.otherwise_continue_1
  otherwise_error_0:
    x86.mov eax, 42
    x86.mov [rbp-8], eax
    x86.jmp register-allocator.main.otherwise_continue_1
  otherwise_continue_1:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-8]
    x86.mov eax, ecx
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: error-propagate-through-caller -->
```maxon
enum MyError is Error
  failed
end 'MyError'

function inner() returns int throws MyError
  throw MyError.failed
end 'inner'

function middle() returns int throws MyError
  let x = try inner()
  return x
end 'middle'

function main() returns int
  let x = try middle() otherwise 99
  return x
end 'main'
```
```exitcode
99
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.inner() -> i64 {
  entry:
    %0 = maxon.enum_literal @MyError.failed
    maxon.throw @MyError %0
  }
  func @register-allocator.middle() -> i64 {
  entry:
    %3, %2 = maxon.try_call @register-allocator.inner
    maxon.assign %2 {var = __try_error_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %3 {var = __try_result_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.binop %2, %4 {op = ne} {kind = i64}
    maxon.cond_br %5 [then: propagate_error_0, else: try_continue_1]
  propagate_error_0:
    %6 = maxon.var_ref {var = __try_error_2} {type = i64}
    maxon.return %6
  try_continue_1:
    %7 = maxon.var_ref {var = __try_result_3} {type = i64}
    maxon.assign %7 {var = x} {kind = i64} {decl = 1 : i1}
    maxon.return %7
  }
  func @register-allocator.main() -> i64 {
  entry:
    %10, %9 = maxon.try_call @register-allocator.middle
    %11 = maxon.literal {value = 99 : i64}
    maxon.assign %11 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %10 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %9, %12 {op = ne} {kind = i64}
    maxon.cond_br %13 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %14 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %14 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %15 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %15 {var = x} {kind = i64} {decl = 1 : i1}
    maxon.return %15
  }
}
=== standard
module {
  func @register-allocator.inner() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = arith.constant {value = 1 : i64}
    %2 = arith.addi %0, %1
    func.error_return %2
  }
  func @register-allocator.middle() -> i64 {
  entry:
    %3, %4 = func.try_call @register-allocator.inner
    memref.store %4, __error_flag
    memref.store %4, __try_error_2
    memref.store %3, __try_result_3
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.cmpi ne %4, %5
    cf.cond_br %6 [then: propagate_error_0, else: try_continue_1]
  propagate_error_0:
    %7 = memref.load __try_error_2 : i64
    func.error_return %7
  try_continue_1:
    %8 = memref.load __try_result_3 : i64
    memref.store %8, x
    func.return %8
  }
  func @register-allocator.main() -> i64 {
  entry:
    %9, %10 = func.try_call @register-allocator.middle
    memref.store %10, __error_flag
    %11 = arith.constant {value = 99 : i64}
    memref.store %11, __try_default_1
    memref.store %9, __try_result_0
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.cmpi ne %10, %12
    cf.cond_br %13 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %14 = memref.load __try_default_1 : i64
    memref.store %14, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %15 = memref.load __try_result_0 : i64
    memref.store %15, x
    func.return %15
  }
}
=== x86
module {
  func @register-allocator.inner() -> i64 {
  entry:
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov edx, eax
    x86.xor eax, eax
    x86.ret
  }
  func @register-allocator.middle() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.call register-allocator.inner
    x86.mov [rbp-8], edx
    x86.mov [rbp-16], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.middle.try_continue_1
  propagate_error_0:
    x86.mov eax, [rbp-8]
    x86.mov edx, eax
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  try_continue_1:
    x86.mov eax, [rbp-16]
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.call register-allocator.middle
    x86.mov ecx, 99
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_3
  otherwise_default_error_2:
    x86.mov eax, [rbp-8]
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-16]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: error-multiple-try-calls -->
```maxon
enum MyError is Error
  failed
end 'MyError'

function getA() returns int throws MyError
  return 10
end 'getA'

function getB() returns int throws MyError
  return 20
end 'getB'

function getC() returns int throws MyError
  throw MyError.failed
end 'getC'

function main() returns int
  let a = try getA() otherwise 0
  let b = try getB() otherwise 0
  let c = try getC() otherwise 12
  return a + b + c
end 'main'
```
```exitcode
42
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.getA() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.return %0
  }
  func @register-allocator.getB() -> i64 {
  entry:
    %1 = maxon.literal {value = 20 : i64}
    maxon.return %1
  }
  func @register-allocator.getC() -> i64 {
  entry:
    %2 = maxon.enum_literal @MyError.failed
    maxon.throw @MyError %2
  }
  func @register-allocator.main() -> i64 {
  entry:
    %5, %4 = maxon.try_call @register-allocator.getA
    %6 = maxon.literal {value = 0 : i64}
    maxon.assign %6 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %5 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 0 : i64}
    %8 = maxon.binop %4, %7 {op = ne} {kind = i64}
    maxon.cond_br %8 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %9 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %9 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %10 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %10 {var = a} {kind = i64} {decl = 1 : i1}
    %13, %12 = maxon.try_call @register-allocator.getB
    %14 = maxon.literal {value = 0 : i64}
    maxon.assign %14 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %13 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 0 : i64}
    %16 = maxon.binop %12, %15 {op = ne} {kind = i64}
    maxon.cond_br %16 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %17 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %17 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %18 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %18 {var = b} {kind = i64} {decl = 1 : i1}
    %21, %20 = maxon.try_call @register-allocator.getC
    %22 = maxon.literal {value = 12 : i64}
    maxon.assign %22 {var = __try_default_9} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %21 {var = __try_result_8} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %23 = maxon.literal {value = 0 : i64}
    %24 = maxon.binop %20, %23 {op = ne} {kind = i64}
    maxon.cond_br %24 [then: otherwise_default_error_10, else: otherwise_default_continue_11]
  otherwise_default_error_10:
    %25 = maxon.var_ref {var = __try_default_9} {type = i64}
    maxon.assign %25 {var = __try_result_8} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_11
  otherwise_default_continue_11:
    %26 = maxon.var_ref {var = __try_result_8} {type = i64}
    maxon.assign %26 {var = c} {kind = i64} {decl = 1 : i1}
    %27 = maxon.var_ref {var = a} {type = i64}
    %28 = maxon.var_ref {var = b} {type = i64}
    %29 = maxon.binop %27, %28 {op = add} {kind = i64}
    %30 = maxon.binop %29, %26 {op = add} {kind = i64}
    maxon.return %30
  }
}
=== standard
module {
  func @register-allocator.getA() -> i64 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    func.return %0
  }
  func @register-allocator.getB() -> i64 {
  entry:
    %1 = arith.constant {value = 20 : i64}
    func.return %1
  }
  func @register-allocator.getC() -> i64 {
  entry:
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.addi %2, %3
    func.error_return %4
  }
  func @register-allocator.main() -> i64 {
  entry:
    %5, %6 = func.try_call @register-allocator.getA
    memref.store %6, __error_flag
    %7 = arith.constant {value = 0 : i64}
    memref.store %7, __try_default_1
    memref.store %5, __try_result_0
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.cmpi ne %6, %8
    cf.cond_br %9 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %10 = memref.load __try_default_1 : i64
    memref.store %10, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %11 = memref.load __try_result_0 : i64
    memref.store %11, a
    %12, %13 = func.try_call @register-allocator.getB
    memref.store %13, __error_flag
    %14 = arith.constant {value = 0 : i64}
    memref.store %14, __try_default_5
    memref.store %12, __try_result_4
    %15 = arith.constant {value = 0 : i64}
    %16 = arith.cmpi ne %13, %15
    cf.cond_br %16 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %17 = memref.load __try_default_5 : i64
    memref.store %17, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %18 = memref.load __try_result_4 : i64
    memref.store %18, b
    %19, %20 = func.try_call @register-allocator.getC
    memref.store %20, __error_flag
    %21 = arith.constant {value = 12 : i64}
    memref.store %21, __try_default_9
    memref.store %19, __try_result_8
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi ne %20, %22
    cf.cond_br %23 [then: otherwise_default_error_10, else: otherwise_default_continue_11]
  otherwise_default_error_10:
    %24 = memref.load __try_default_9 : i64
    memref.store %24, __try_result_8
    cf.br otherwise_default_continue_11
  otherwise_default_continue_11:
    %25 = memref.load __try_result_8 : i64
    memref.store %25, c
    %26 = memref.load a : i64
    %27 = memref.load b : i64
    %28 = arith.addi %26, %27
    %29 = arith.addi %28, %25
    func.return %29
  }
}
=== x86
module {
  func @register-allocator.getA() -> i64 {
  entry:
    x86.mov eax, 10
    x86.xor edx, edx
    x86.ret
  }
  func @register-allocator.getB() -> i64 {
  entry:
    x86.mov eax, 20
    x86.xor edx, edx
    x86.ret
  }
  func @register-allocator.getC() -> i64 {
  entry:
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov edx, eax
    x86.xor eax, eax
    x86.ret
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=64
    x86.call register-allocator.getA
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_3
  otherwise_default_error_2:
    x86.mov eax, [rbp-8]
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-24], eax
    x86.call register-allocator.getB
    x86.xor ecx, ecx
    x86.mov [rbp-32], ecx
    x86.mov [rbp-40], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-32]
    x86.mov [rbp-40], eax
    x86.jmp register-allocator.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, [rbp-40]
    x86.mov [rbp-48], eax
    x86.call register-allocator.getC
    x86.mov ecx, 12
    x86.mov [rbp-56], ecx
    x86.mov [rbp-64], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_11
  otherwise_default_error_10:
    x86.mov eax, [rbp-56]
    x86.mov [rbp-64], eax
    x86.jmp register-allocator.main.otherwise_default_continue_11
  otherwise_default_continue_11:
    x86.mov eax, [rbp-64]
    x86.mov ecx, [rbp-24]
    x86.mov edx, [rbp-48]
    x86.add ecx, edx
    x86.lea eax, [ecx + eax]
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: error-throw-in-match -->
```maxon
enum MyError is Error
  invalidInput
  notFound
end 'MyError'

function lookup(key int) returns int throws MyError
  match key 'dispatch'
    1 then return 100
    2 then return 200
    default then throw MyError.notFound
  end 'dispatch'
end 'lookup'

function main() returns int
  let a = try lookup(2) otherwise 0
  let b = try lookup(99) otherwise 42
  return a + b mod 256
end 'main'
```
```exitcode
242
```
```RequiredMLIR
=== maxon
module {
  func @register-allocator.lookup(key: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = key} {type = i64}
    maxon.assign %0 {var = __match_dispatch_0} {kind = i64} {decl = 1 : i1}
    maxon.br dispatch_0.cmp0
  dispatch_0.cmp0:
    %1 = maxon.var_ref {var = __match_dispatch_0} {type = i64}
    %2 = maxon.literal {value = 1 : i64}
    %3 = maxon.binop %1, %2 {op = eq} {kind = i64}
    maxon.cond_br %3 [then: dispatch_0.case0, else: dispatch_0.cmp1]
  dispatch_0.case0:
    %4 = maxon.literal {value = 100 : i64}
    maxon.return %4
  dispatch_0.cmp1:
    %5 = maxon.var_ref {var = __match_dispatch_0} {type = i64}
    %6 = maxon.literal {value = 2 : i64}
    %7 = maxon.binop %5, %6 {op = eq} {kind = i64}
    maxon.cond_br %7 [then: dispatch_0.case1, else: dispatch_0.case2]
  dispatch_0.case1:
    %8 = maxon.literal {value = 200 : i64}
    maxon.return %8
  dispatch_0.case2:
    %9 = maxon.enum_literal @MyError.notFound
    maxon.throw @MyError %9
  dispatch_0.merge:
  }
  func @register-allocator.main() -> i64 {
  entry:
    %10 = maxon.literal {value = 2 : i64}
    %13, %12 = maxon.try_call @register-allocator.lookup %10
    %14 = maxon.literal {value = 0 : i64}
    maxon.assign %14 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %13 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 0 : i64}
    %16 = maxon.binop %12, %15 {op = ne} {kind = i64}
    maxon.cond_br %16 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %17 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %17 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %18 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %18 {var = a} {kind = i64} {decl = 1 : i1}
    %19 = maxon.literal {value = 99 : i64}
    %22, %21 = maxon.try_call @register-allocator.lookup %19
    %23 = maxon.literal {value = 42 : i64}
    maxon.assign %23 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %22 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %24 = maxon.literal {value = 0 : i64}
    %25 = maxon.binop %21, %24 {op = ne} {kind = i64}
    maxon.cond_br %25 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %26 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %26 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %27 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %27 {var = b} {kind = i64} {decl = 1 : i1}
    %28 = maxon.literal {value = 256 : i64}
    %29 = maxon.binop %27, %28 {op = mod} {kind = i64}
    %30 = maxon.var_ref {var = a} {type = i64}
    %31 = maxon.binop %30, %29 {op = add} {kind = i64}
    maxon.return %31
  }
}
=== standard
module {
  func @register-allocator.lookup(key: i64) -> i64 {
  entry:
    %0 = func.param key : StdI64
    memref.store %0, key
    memref.store %0, __match_dispatch_0
    cf.br dispatch_0.cmp0
  dispatch_0.cmp0:
    %1 = memref.load __match_dispatch_0 : i64
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.cmpi eq %1, %2
    cf.cond_br %3 [then: dispatch_0.case0, else: dispatch_0.cmp1]
  dispatch_0.case0:
    %4 = arith.constant {value = 100 : i64}
    func.return %4
  dispatch_0.cmp1:
    %5 = memref.load __match_dispatch_0 : i64
    %6 = arith.constant {value = 2 : i64}
    %7 = arith.cmpi eq %5, %6
    cf.cond_br %7 [then: dispatch_0.case1, else: dispatch_0.case2]
  dispatch_0.case1:
    %8 = arith.constant {value = 200 : i64}
    func.return %8
  dispatch_0.case2:
    %9 = arith.constant {value = 1 : i64}
    %10 = arith.constant {value = 1 : i64}
    %11 = arith.addi %9, %10
    func.error_return %11
  dispatch_0.merge:
  }
  func @register-allocator.main() -> i64 {
  entry:
    %12 = arith.constant {value = 2 : i64}
    %13, %14 = func.try_call @register-allocator.lookup %12
    memref.store %14, __error_flag
    %15 = arith.constant {value = 0 : i64}
    memref.store %15, __try_default_1
    memref.store %13, __try_result_0
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi ne %14, %16
    cf.cond_br %17 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %18 = memref.load __try_default_1 : i64
    memref.store %18, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %19 = memref.load __try_result_0 : i64
    memref.store %19, a
    %20 = arith.constant {value = 99 : i64}
    %21, %22 = func.try_call @register-allocator.lookup %20
    memref.store %22, __error_flag
    %23 = arith.constant {value = 42 : i64}
    memref.store %23, __try_default_5
    memref.store %21, __try_result_4
    %24 = arith.constant {value = 0 : i64}
    %25 = arith.cmpi ne %22, %24
    cf.cond_br %25 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %26 = memref.load __try_default_5 : i64
    memref.store %26, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %27 = memref.load __try_result_4 : i64
    memref.store %27, b
    %28 = arith.constant {value = 256 : i64}
    %29 = arith.remsi %27, %28
    %30 = memref.load a : i64
    %31 = arith.addi %30, %29
    func.return %31
  }
}
=== x86
module {
  func @register-allocator.lookup(key: i64) -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.jmp register-allocator.lookup.dispatch_0.cmp0
  dispatch_0.cmp0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne register-allocator.lookup.dispatch_0.cmp1
  dispatch_0.case0:
    x86.mov eax, 100
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  dispatch_0.cmp1:
    x86.mov eax, [rbp-8]
    x86.mov ecx, 2
    x86.cmp eax, ecx
    x86.jne register-allocator.lookup.dispatch_0.case2
  dispatch_0.case1:
    x86.mov eax, 200
    x86.xor edx, edx
    x86.epilogue
    x86.ret
  dispatch_0.case2:
    x86.mov eax, 1
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov edx, eax
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  dispatch_0.merge:
  }
  func @register-allocator.main() -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.mov eax, 2
    x86.mov rcx, rax
    x86.call register-allocator.lookup
    x86.xor ecx, ecx
    x86.mov [rbp-8], ecx
    x86.mov [rbp-16], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_3
  otherwise_default_error_2:
    x86.mov eax, [rbp-8]
    x86.mov [rbp-16], eax
    x86.jmp register-allocator.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-24], eax
    x86.mov ecx, 99
    x86.call register-allocator.lookup
    x86.mov ecx, 42
    x86.mov [rbp-32], ecx
    x86.mov [rbp-40], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je register-allocator.main.otherwise_default_continue_7
  otherwise_default_error_6:
    x86.mov eax, [rbp-32]
    x86.mov [rbp-40], eax
    x86.jmp register-allocator.main.otherwise_default_continue_7
  otherwise_default_continue_7:
    x86.mov eax, [rbp-40]
    x86.mov ecx, 256
    x86.mov [rbp-48], eax
    x86.cqo
    x86.idiv ecx
    x86.mov eax, [rbp-24]
    x86.add eax, edx
    x86.epilogue
    x86.ret
  }
}
```
