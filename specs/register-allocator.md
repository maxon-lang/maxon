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
function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 42 : i64}
    maxon.scope_end []
    maxon.return %0
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 42 : i64}
    func.return %0
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.mov rax, 42
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 42 : i64}
    maxon.scope_end []
    maxon.return %0
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 42 : i64}
    func.return %0
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.mov x0, #42
    arm64.ret
  }
}
```

<!-- test: int-var-roundtrip -->
```maxon
function main() returns ExitCode
	let x = 99
	return x
end 'main'
```
```exitcode
99
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 99 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1}
    maxon.scope_end [x]
    maxon.return %0
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 99 : i64}
    func.return %0
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.mov rax, 99
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 99 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %0, %1 {op = lt}
    %3 = maxon.literal {value = 4294967295 : i64}
    %4 = maxon.binop %0, %3 {op = gt}
    %5 = maxon.binop %2, %4 {op = or}
    maxon.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-var-roundtrip.test:4: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [x]
    maxon.return %0
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 99 : i64}
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi lt %0, %1
    %3 = arith.constant {value = 4294967295 : i64}
    %4 = arith.cmpi gt %0, %3
    %5 = arith.ori1 %2, %4
    cf.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %6 = memref.lea_symdata __panic_msg_0
    %7 = std.ptr_to_i64 %6
    std.call_runtime @maxon_panic %7
  __range_ok_0:
    func.return %0
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #99
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-add-constants -->
```maxon
function main() returns ExitCode
	return 30 + 12
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 30 : i64}
    %1 = maxon.literal {value = 12 : i64}
    %2 = maxon.binop %0, %1 {op = add}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-add-constants.test:3: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 30 : i64}
    %1 = arith.constant {value = 12 : i64}
    %2 = arith.addi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @mrt_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 30
    x64.mov rcx, 12
    x64.add rax, rcx
    x64.xor edx, edx
    x64.mov ebx, 4294967295
    x64.cmp rax, rbx
    x64.jg main.__range_panic_0
    x64.cmp rax, rdx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 30 : i64}
    %1 = maxon.literal {value = 12 : i64}
    %2 = maxon.binop %0, %1 {op = add}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-add-constants.test:3: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 30 : i64}
    %1 = arith.constant {value = 12 : i64}
    %2 = arith.addi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @maxon_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #30
    arm64.mov x1, #12
    arm64.add x2, x0, x1
    arm64.mov x3, #0
    arm64.cmp x2, x3
    arm64.cset x4, lt
    arm64.mov x5, #4294967295
    arm64.cmp x2, x5
    arm64.cset x6, gt
    arm64.orr x7, x4, x6
    arm64.cmp x7, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x2
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

### Level 2: Multiple Values and Reuse

<!-- test: int-two-vars-add -->
```maxon
function main() returns ExitCode
	let a = 30
	let b = 12
	return a + b
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 30 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 12 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.binop %0, %1 {op = add}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-two-vars-add.test:5: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 30 : i64}
    %1 = arith.constant {value = 12 : i64}
    %2 = arith.addi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @mrt_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 30
    x64.mov rcx, 12
    x64.add rax, rcx
    x64.xor edx, edx
    x64.mov ebx, 4294967295
    x64.cmp rax, rbx
    x64.jg main.__range_panic_0
    x64.cmp rax, rdx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 30 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 12 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %0, %1 {op = add}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.binop %2, %3 {op = lt}
    %5 = maxon.literal {value = 4294967295 : i64}
    %6 = maxon.binop %2, %5 {op = gt}
    %7 = maxon.binop %4, %6 {op = or}
    maxon.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-two-vars-add.test:5: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %2
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 30 : i64}
    %1 = arith.constant {value = 12 : i64}
    %2 = arith.addi %0, %1
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi lt %2, %3
    %5 = arith.constant {value = 4294967295 : i64}
    %6 = arith.cmpi gt %2, %5
    %7 = arith.ori1 %4, %6
    cf.cond_br %7 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %8 = memref.lea_symdata __panic_msg_0
    %9 = std.ptr_to_i64 %8
    std.call_runtime @maxon_panic %9
  __range_ok_0:
    func.return %2
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #30
    arm64.mov x1, #12
    arm64.add x2, x0, x1
    arm64.mov x3, #0
    arm64.cmp x2, x3
    arm64.cset x4, lt
    arm64.mov x5, #4294967295
    arm64.cmp x2, x5
    arm64.cset x6, gt
    arm64.orr x7, x4, x6
    arm64.cmp x7, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x2
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-var-reuse-twice -->
```maxon
function main() returns ExitCode
	let x = 21
	return x + x
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 21 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1}
    %1 = maxon.binop %0, %0 {op = add}
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.binop %1, %2 {op = lt}
    %4 = maxon.literal {value = 4294967295 : i64}
    %5 = maxon.binop %1, %4 {op = gt}
    %6 = maxon.binop %3, %5 {op = or}
    maxon.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-var-reuse-twice.test:4: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x]
    maxon.return %1
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 21 : i64}
    %1 = arith.addi %0, %0
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.cmpi lt %1, %2
    %4 = arith.constant {value = 4294967295 : i64}
    %5 = arith.cmpi gt %1, %4
    %6 = arith.ori1 %3, %5
    cf.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %7 = memref.lea_symdata __panic_msg_0
    %8 = std.ptr_to_i64 %7
    std.call_runtime @mrt_panic %8
  __range_ok_0:
    func.return %1
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 21
    x64.add rax, rax
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 21 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.binop %0, %0 {op = add}
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.binop %1, %2 {op = lt}
    %4 = maxon.literal {value = 4294967295 : i64}
    %5 = maxon.binop %1, %4 {op = gt}
    %6 = maxon.binop %3, %5 {op = or}
    maxon.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-var-reuse-twice.test:4: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [x]
    maxon.return %1
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 21 : i64}
    %1 = arith.addi %0, %0
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.cmpi lt %1, %2
    %4 = arith.constant {value = 4294967295 : i64}
    %5 = arith.cmpi gt %1, %4
    %6 = arith.ori1 %3, %5
    cf.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %7 = memref.lea_symdata __panic_msg_0
    %8 = std.ptr_to_i64 %7
    std.call_runtime @maxon_panic %8
  __range_ok_0:
    func.return %1
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #21
    arm64.add x1, x0, x0
    arm64.mov x2, #0
    arm64.cmp x1, x2
    arm64.cset x3, lt
    arm64.mov x4, #4294967295
    arm64.cmp x1, x4
    arm64.cset x5, gt
    arm64.orr x6, x3, x5
    arm64.cmp x6, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x1
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-chained-assignments -->
```maxon
function main() returns ExitCode
	let a = 10
	let b = a + 5
	let c = b + 7
	let d = c + 20
	return d
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 5 : i64}
    %2 = maxon.binop %0, %1 {op = add}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 7 : i64}
    %4 = maxon.binop %2, %3 {op = add}
    maxon.assign %4 {var = c} {kind = i64} {decl = 1 : i1}
    %5 = maxon.literal {value = 20 : i64}
    %6 = maxon.binop %4, %5 {op = add}
    maxon.assign %6 {var = d} {kind = i64} {decl = 1 : i1}
    %7 = maxon.literal {value = 0 : i64}
    %8 = maxon.binop %6, %7 {op = lt}
    %9 = maxon.literal {value = 4294967295 : i64}
    %10 = maxon.binop %6, %9 {op = gt}
    %11 = maxon.binop %8, %10 {op = or}
    maxon.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-chained-assignments.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, c, d]
    maxon.return %6
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    %1 = arith.constant {value = 5 : i64}
    %2 = arith.addi %0, %1
    %3 = arith.constant {value = 7 : i64}
    %4 = arith.addi %2, %3
    %5 = arith.constant {value = 20 : i64}
    %6 = arith.addi %4, %5
    %7 = arith.constant {value = 0 : i64}
    %8 = arith.cmpi lt %6, %7
    %9 = arith.constant {value = 4294967295 : i64}
    %10 = arith.cmpi gt %6, %9
    %11 = arith.ori1 %8, %10
    cf.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %12 = memref.lea_symdata __panic_msg_0
    %13 = std.ptr_to_i64 %12
    std.call_runtime @mrt_panic %13
  __range_ok_0:
    func.return %6
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 10
    x64.mov rcx, 5
    x64.add rax, rcx
    x64.mov rdx, 7
    x64.add rax, rdx
    x64.mov rbx, 20
    x64.add rax, rbx
    x64.xor esi, esi
    x64.mov edi, 4294967295
    x64.cmp rax, rdi
    x64.jg main.__range_panic_0
    x64.cmp rax, rsi
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 5 : i64}
    %2 = maxon.binop %0, %1 {op = add}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 7 : i64}
    %4 = maxon.binop %2, %3 {op = add}
    maxon.assign %4 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.literal {value = 20 : i64}
    %6 = maxon.binop %4, %5 {op = add}
    maxon.assign %6 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 0 : i64}
    %8 = maxon.binop %6, %7 {op = lt}
    %9 = maxon.literal {value = 4294967295 : i64}
    %10 = maxon.binop %6, %9 {op = gt}
    %11 = maxon.binop %8, %10 {op = or}
    maxon.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-chained-assignments.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b, c, d]
    maxon.return %6
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    %1 = arith.constant {value = 5 : i64}
    %2 = arith.addi %0, %1
    %3 = arith.constant {value = 7 : i64}
    %4 = arith.addi %2, %3
    %5 = arith.constant {value = 20 : i64}
    %6 = arith.addi %4, %5
    %7 = arith.constant {value = 0 : i64}
    %8 = arith.cmpi lt %6, %7
    %9 = arith.constant {value = 4294967295 : i64}
    %10 = arith.cmpi gt %6, %9
    %11 = arith.ori1 %8, %10
    cf.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %12 = memref.lea_symdata __panic_msg_0
    %13 = std.ptr_to_i64 %12
    std.call_runtime @maxon_panic %13
  __range_ok_0:
    func.return %6
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #10
    arm64.mov x1, #5
    arm64.add x2, x0, x1
    arm64.mov x3, #7
    arm64.add x4, x2, x3
    arm64.mov x5, #20
    arm64.add x6, x4, x5
    arm64.mov x7, #0
    arm64.cmp x6, x7
    arm64.cset x8, lt
    arm64.mov x9, #4294967295
    arm64.cmp x6, x9
    arm64.cset x10, gt
    arm64.orr x11, x8, x10
    arm64.cmp x11, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x6
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-reassignment -->
```maxon
function main() returns ExitCode
	var x = 100
	let y = x - 80
	x = 22
	return x + y
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 80 : i64}
    %2 = maxon.binop %0, %1 {op = sub}
    maxon.assign %2 {var = y} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 22 : i64}
    maxon.assign %3 {var = x} {kind = i64} {mut = 1 : i1}
    %4 = maxon.binop %3, %2 {op = add}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-reassignment.test:6: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, y]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    %1 = arith.constant {value = 80 : i64}
    %2 = arith.subi %0, %1
    %3 = arith.constant {value = 22 : i64}
    %4 = arith.addi %3, %2
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.cmpi lt %4, %5
    %7 = arith.constant {value = 4294967295 : i64}
    %8 = arith.cmpi gt %4, %7
    %9 = arith.ori1 %6, %8
    cf.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %10 = memref.lea_symdata __panic_msg_0
    %11 = std.ptr_to_i64 %10
    std.call_runtime @mrt_panic %11
  __range_ok_0:
    func.return %4
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 100
    x64.mov rcx, 80
    x64.sub rax, rcx
    x64.mov rdx, 22
    x64.add rdx, rax
    x64.xor ebx, ebx
    x64.mov esi, 4294967295
    x64.cmp rdx, rsi
    x64.jg main.__range_panic_0
    x64.cmp rdx, rbx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 80 : i64}
    %2 = maxon.binop %0, %1 {op = sub}
    maxon.assign %2 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 22 : i64}
    maxon.assign %3 {var = x} {kind = i64} {mut = 1 : i1}
    %4 = maxon.binop %3, %2 {op = add}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-reassignment.test:6: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [x, y]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    %1 = arith.constant {value = 80 : i64}
    %2 = arith.subi %0, %1
    %3 = arith.constant {value = 22 : i64}
    %4 = arith.addi %3, %2
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.cmpi lt %4, %5
    %7 = arith.constant {value = 4294967295 : i64}
    %8 = arith.cmpi gt %4, %7
    %9 = arith.ori1 %6, %8
    cf.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %10 = memref.lea_symdata __panic_msg_0
    %11 = std.ptr_to_i64 %10
    std.call_runtime @maxon_panic %11
  __range_ok_0:
    func.return %4
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #100
    arm64.mov x1, #80
    arm64.sub x2, x0, x1
    arm64.mov x3, #22
    arm64.add x4, x3, x2
    arm64.mov x5, #0
    arm64.cmp x4, x5
    arm64.cset x6, lt
    arm64.mov x7, #4294967295
    arm64.cmp x4, x7
    arm64.cset x8, gt
    arm64.orr x9, x6, x8
    arm64.cmp x9, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x4
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

### Level 3: Register Pressure and Spilling

<!-- test: int-six-vars-alive -->
```maxon
function main() returns ExitCode
	let a = 1
	let b = 2
	let c = 3
	let d = 4
	let e = 5
	let f = 6
	return a + b + c + d + e + f
end 'main'
```
```exitcode
21
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 4 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 5 : i64}
    maxon.assign %4 {var = e} {kind = i64} {decl = 1 : i1}
    %5 = maxon.literal {value = 6 : i64}
    maxon.assign %5 {var = f} {kind = i64} {decl = 1 : i1}
    %6 = maxon.binop %0, %1 {op = add}
    %7 = maxon.binop %6, %2 {op = add}
    %8 = maxon.binop %7, %3 {op = add}
    %9 = maxon.binop %8, %4 {op = add}
    %10 = maxon.binop %9, %5 {op = add}
    %11 = maxon.literal {value = 0 : i64}
    %12 = maxon.binop %10, %11 {op = lt}
    %13 = maxon.literal {value = 4294967295 : i64}
    %14 = maxon.binop %10, %13 {op = gt}
    %15 = maxon.binop %12, %14 {op = or}
    maxon.cond_br %15 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-six-vars-alive.test:9: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, c, d, e, f]
    maxon.return %10
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.constant {value = 4 : i64}
    %4 = arith.constant {value = 5 : i64}
    %5 = arith.constant {value = 6 : i64}
    %6 = arith.addi %0, %1
    %7 = arith.addi %6, %2
    %8 = arith.addi %7, %3
    %9 = arith.addi %8, %4
    %10 = arith.addi %9, %5
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.cmpi lt %10, %11
    %13 = arith.constant {value = 4294967295 : i64}
    %14 = arith.cmpi gt %10, %13
    %15 = arith.ori1 %12, %14
    cf.cond_br %15 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %16 = memref.lea_symdata __panic_msg_0
    %17 = std.ptr_to_i64 %16
    std.call_runtime @mrt_panic %17
  __range_ok_0:
    func.return %10
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 1
    x64.mov rcx, 2
    x64.mov rdx, 3
    x64.mov rbx, 4
    x64.mov rsi, 5
    x64.mov rdi, 6
    x64.add rax, rcx
    x64.add rax, rdx
    x64.add rax, rbx
    x64.add rax, rsi
    x64.add rax, rdi
    x64.xor r8d, r8d
    x64.mov r9, 4294967295
    x64.cmp rax, r9
    x64.jg main.__range_panic_0
    x64.cmp rax, r8
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %6 = maxon.binop %0, %1 {op = add}
    %7 = maxon.binop %6, %2 {op = add}
    %8 = maxon.binop %7, %3 {op = add}
    %9 = maxon.binop %8, %4 {op = add}
    %10 = maxon.binop %9, %5 {op = add}
    %11 = maxon.literal {value = 0 : i64}
    %12 = maxon.binop %10, %11 {op = lt}
    %13 = maxon.literal {value = 4294967295 : i64}
    %14 = maxon.binop %10, %13 {op = gt}
    %15 = maxon.binop %12, %14 {op = or}
    maxon.cond_br %15 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-six-vars-alive.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b, c, d, e, f]
    maxon.return %10
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.constant {value = 4 : i64}
    %4 = arith.constant {value = 5 : i64}
    %5 = arith.constant {value = 6 : i64}
    %6 = arith.addi %0, %1
    %7 = arith.addi %6, %2
    %8 = arith.addi %7, %3
    %9 = arith.addi %8, %4
    %10 = arith.addi %9, %5
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.cmpi lt %10, %11
    %13 = arith.constant {value = 4294967295 : i64}
    %14 = arith.cmpi gt %10, %13
    %15 = arith.ori1 %12, %14
    cf.cond_br %15 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %16 = memref.lea_symdata __panic_msg_0
    %17 = std.ptr_to_i64 %16
    std.call_runtime @maxon_panic %17
  __range_ok_0:
    func.return %10
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #1
    arm64.mov x1, #2
    arm64.mov x2, #3
    arm64.mov x3, #4
    arm64.mov x4, #5
    arm64.mov x5, #6
    arm64.add x6, x0, x1
    arm64.add x7, x6, x2
    arm64.add x8, x7, x3
    arm64.add x9, x8, x4
    arm64.add x10, x9, x5
    arm64.mov x11, #0
    arm64.cmp x10, x11
    arm64.cset x12, lt
    arm64.mov x13, #4294967295
    arm64.cmp x10, x13
    arm64.cset x14, gt
    arm64.orr x15, x12, x14
    arm64.cmp x15, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x10
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-ten-vars-alive -->
```maxon
function main() returns ExitCode
	let a = 1
	let b = 2
	let c = 3
	let d = 4
	let e = 5
	let f = 6
	let g = 7
	let h = 8
	let i = 9
	let j = 10
	return a + b + c + d + e + f + g + h + i + j
end 'main'
```
```exitcode
55
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 4 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 5 : i64}
    maxon.assign %4 {var = e} {kind = i64} {decl = 1 : i1}
    %5 = maxon.literal {value = 6 : i64}
    maxon.assign %5 {var = f} {kind = i64} {decl = 1 : i1}
    %6 = maxon.literal {value = 7 : i64}
    maxon.assign %6 {var = g} {kind = i64} {decl = 1 : i1}
    %7 = maxon.literal {value = 8 : i64}
    maxon.assign %7 {var = h} {kind = i64} {decl = 1 : i1}
    %8 = maxon.literal {value = 9 : i64}
    maxon.assign %8 {var = i} {kind = i64} {decl = 1 : i1}
    %9 = maxon.literal {value = 10 : i64}
    maxon.assign %9 {var = j} {kind = i64} {decl = 1 : i1}
    %10 = maxon.binop %0, %1 {op = add}
    %11 = maxon.binop %10, %2 {op = add}
    %12 = maxon.binop %11, %3 {op = add}
    %13 = maxon.binop %12, %4 {op = add}
    %14 = maxon.binop %13, %5 {op = add}
    %15 = maxon.binop %14, %6 {op = add}
    %16 = maxon.binop %15, %7 {op = add}
    %17 = maxon.binop %16, %8 {op = add}
    %18 = maxon.binop %17, %9 {op = add}
    %19 = maxon.literal {value = 0 : i64}
    %20 = maxon.binop %18, %19 {op = lt}
    %21 = maxon.literal {value = 4294967295 : i64}
    %22 = maxon.binop %18, %21 {op = gt}
    %23 = maxon.binop %20, %22 {op = or}
    maxon.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-ten-vars-alive.test:13: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, c, d, e, f, g, h, i, j]
    maxon.return %18
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.constant {value = 4 : i64}
    %4 = arith.constant {value = 5 : i64}
    %5 = arith.constant {value = 6 : i64}
    %6 = arith.constant {value = 7 : i64}
    %7 = arith.constant {value = 8 : i64}
    %8 = arith.constant {value = 9 : i64}
    %9 = arith.constant {value = 10 : i64}
    %10 = arith.addi %0, %1
    %11 = arith.addi %10, %2
    %12 = arith.addi %11, %3
    %13 = arith.addi %12, %4
    %14 = arith.addi %13, %5
    %15 = arith.addi %14, %6
    %16 = arith.addi %15, %7
    %17 = arith.addi %16, %8
    %18 = arith.addi %17, %9
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 4294967295 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %24 = memref.lea_symdata __panic_msg_0
    %25 = std.ptr_to_i64 %24
    std.call_runtime @mrt_panic %25
  __range_ok_0:
    func.return %18
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 1
    x64.mov rcx, 2
    x64.mov rdx, 3
    x64.mov rbx, 4
    x64.mov rsi, 5
    x64.mov rdi, 6
    x64.mov r8, 7
    x64.mov r9, 8
    x64.mov rax, 9
    x64.mov rcx, 10
    x64.mov rdx, 2
    x64.mov rbx, 1
    x64.add rbx, rdx
    x64.mov rdx, 3
    x64.add rbx, rdx
    x64.mov rdx, 4
    x64.add rbx, rdx
    x64.add rbx, rsi
    x64.add rbx, rdi
    x64.add rbx, r8
    x64.add rbx, r9
    x64.add rbx, rax
    x64.add rbx, rcx
    x64.xor eax, eax
    x64.mov ecx, 4294967295
    x64.cmp rbx, rcx
    x64.jg main.__range_panic_0
    x64.cmp rbx, rax
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rbx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %10 = maxon.binop %0, %1 {op = add}
    %11 = maxon.binop %10, %2 {op = add}
    %12 = maxon.binop %11, %3 {op = add}
    %13 = maxon.binop %12, %4 {op = add}
    %14 = maxon.binop %13, %5 {op = add}
    %15 = maxon.binop %14, %6 {op = add}
    %16 = maxon.binop %15, %7 {op = add}
    %17 = maxon.binop %16, %8 {op = add}
    %18 = maxon.binop %17, %9 {op = add}
    %19 = maxon.literal {value = 0 : i64}
    %20 = maxon.binop %18, %19 {op = lt}
    %21 = maxon.literal {value = 4294967295 : i64}
    %22 = maxon.binop %18, %21 {op = gt}
    %23 = maxon.binop %20, %22 {op = or}
    maxon.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-ten-vars-alive.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b, c, d, e, f, g, h, i, j]
    maxon.return %18
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.constant {value = 4 : i64}
    %4 = arith.constant {value = 5 : i64}
    %5 = arith.constant {value = 6 : i64}
    %6 = arith.constant {value = 7 : i64}
    %7 = arith.constant {value = 8 : i64}
    %8 = arith.constant {value = 9 : i64}
    %9 = arith.constant {value = 10 : i64}
    %10 = arith.addi %0, %1
    %11 = arith.addi %10, %2
    %12 = arith.addi %11, %3
    %13 = arith.addi %12, %4
    %14 = arith.addi %13, %5
    %15 = arith.addi %14, %6
    %16 = arith.addi %15, %7
    %17 = arith.addi %16, %8
    %18 = arith.addi %17, %9
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 4294967295 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %24 = memref.lea_symdata __panic_msg_0
    %25 = std.ptr_to_i64 %24
    std.call_runtime @maxon_panic %25
  __range_ok_0:
    func.return %18
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #1
    arm64.mov x1, #2
    arm64.mov x2, #3
    arm64.mov x3, #4
    arm64.mov x4, #5
    arm64.mov x5, #6
    arm64.mov x6, #7
    arm64.mov x7, #8
    arm64.mov x8, #9
    arm64.mov x9, #10
    arm64.add x10, x0, x1
    arm64.add x11, x10, x2
    arm64.add x12, x11, x3
    arm64.add x13, x12, x4
    arm64.add x14, x13, x5
    arm64.add x15, x14, x6
    arm64.add x0, x15, x7
    arm64.add x1, x0, x8
    arm64.add x0, x1, x9
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #4294967295
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-sixteen-vars-spill -->
```maxon
function main() returns ExitCode
	let a = 1
	let b = 2
	let c = 3
	let d = 4
	let e = 5
	let f = 6
	let g = 7
	let h = 8
	let i = 9
	let j = 10
	let k = 11
	let l = 12
	let m = 13
	let n = 14
	let o = 15
	let p = 16
	return (a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p) mod 256
end 'main'
```
```exitcode
136
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 4 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 5 : i64}
    maxon.assign %4 {var = e} {kind = i64} {decl = 1 : i1}
    %5 = maxon.literal {value = 6 : i64}
    maxon.assign %5 {var = f} {kind = i64} {decl = 1 : i1}
    %6 = maxon.literal {value = 7 : i64}
    maxon.assign %6 {var = g} {kind = i64} {decl = 1 : i1}
    %7 = maxon.literal {value = 8 : i64}
    maxon.assign %7 {var = h} {kind = i64} {decl = 1 : i1}
    %8 = maxon.literal {value = 9 : i64}
    maxon.assign %8 {var = i} {kind = i64} {decl = 1 : i1}
    %9 = maxon.literal {value = 10 : i64}
    maxon.assign %9 {var = j} {kind = i64} {decl = 1 : i1}
    %10 = maxon.literal {value = 11 : i64}
    maxon.assign %10 {var = k} {kind = i64} {decl = 1 : i1}
    %11 = maxon.literal {value = 12 : i64}
    maxon.assign %11 {var = l} {kind = i64} {decl = 1 : i1}
    %12 = maxon.literal {value = 13 : i64}
    maxon.assign %12 {var = m} {kind = i64} {decl = 1 : i1}
    %13 = maxon.literal {value = 14 : i64}
    maxon.assign %13 {var = n} {kind = i64} {decl = 1 : i1}
    %14 = maxon.literal {value = 15 : i64}
    maxon.assign %14 {var = o} {kind = i64} {decl = 1 : i1}
    %15 = maxon.literal {value = 16 : i64}
    maxon.assign %15 {var = p} {kind = i64} {decl = 1 : i1}
    %16 = maxon.binop %0, %1 {op = add}
    %17 = maxon.binop %16, %2 {op = add}
    %18 = maxon.binop %17, %3 {op = add}
    %19 = maxon.binop %18, %4 {op = add}
    %20 = maxon.binop %19, %5 {op = add}
    %21 = maxon.binop %20, %6 {op = add}
    %22 = maxon.binop %21, %7 {op = add}
    %23 = maxon.binop %22, %8 {op = add}
    %24 = maxon.binop %23, %9 {op = add}
    %25 = maxon.binop %24, %10 {op = add}
    %26 = maxon.binop %25, %11 {op = add}
    %27 = maxon.binop %26, %12 {op = add}
    %28 = maxon.binop %27, %13 {op = add}
    %29 = maxon.binop %28, %14 {op = add}
    %30 = maxon.binop %29, %15 {op = add}
    %31 = maxon.literal {value = 256 : i64}
    %32 = maxon.binop %30, %31 {op = mod}
    %33 = maxon.literal {value = 0 : i64}
    %34 = maxon.binop %32, %33 {op = lt}
    %35 = maxon.literal {value = 4294967295 : i64}
    %36 = maxon.binop %32, %35 {op = gt}
    %37 = maxon.binop %34, %36 {op = or}
    maxon.cond_br %37 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-sixteen-vars-spill.test:19: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p]
    maxon.return %32
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.constant {value = 4 : i64}
    %4 = arith.constant {value = 5 : i64}
    %5 = arith.constant {value = 6 : i64}
    %6 = arith.constant {value = 7 : i64}
    %7 = arith.constant {value = 8 : i64}
    %8 = arith.constant {value = 9 : i64}
    %9 = arith.constant {value = 10 : i64}
    %10 = arith.constant {value = 11 : i64}
    %11 = arith.constant {value = 12 : i64}
    %12 = arith.constant {value = 13 : i64}
    %13 = arith.constant {value = 14 : i64}
    %14 = arith.constant {value = 15 : i64}
    %15 = arith.constant {value = 16 : i64}
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
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.cmpi lt %32, %33
    %35 = arith.constant {value = 4294967295 : i64}
    %36 = arith.cmpi gt %32, %35
    %37 = arith.ori1 %34, %36
    cf.cond_br %37 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %38 = memref.lea_symdata __panic_msg_0
    %39 = std.ptr_to_i64 %38
    std.call_runtime @mrt_panic %39
  __range_ok_0:
    func.return %32
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 1
    x64.mov rcx, 2
    x64.mov rdx, 3
    x64.mov rbx, 4
    x64.mov rsi, 5
    x64.mov rdi, 6
    x64.mov r8, 7
    x64.mov r9, 8
    x64.mov rax, 9
    x64.mov rcx, 10
    x64.mov rdx, 11
    x64.mov rbx, 12
    x64.mov rsi, 13
    x64.mov rdi, 14
    x64.mov r8, 15
    x64.mov r9, 16
    x64.mov rax, 2
    x64.mov rcx, 1
    x64.add rcx, rax
    x64.mov rax, 3
    x64.add rcx, rax
    x64.mov rax, 4
    x64.add rcx, rax
    x64.mov rax, 5
    x64.add rcx, rax
    x64.mov rax, 6
    x64.add rcx, rax
    x64.mov rax, 7
    x64.add rcx, rax
    x64.mov rax, 8
    x64.add rcx, rax
    x64.mov rax, 9
    x64.add rcx, rax
    x64.mov rax, 10
    x64.add rcx, rax
    x64.add rcx, rdx
    x64.add rcx, rbx
    x64.add rcx, rsi
    x64.add rcx, rdi
    x64.add rcx, r8
    x64.add rcx, r9
    x64.mov rax, 256
    x64.mov rbx, rax
    x64.mov rax, rcx
    x64.cqo
    x64.idiv rbx
    x64.xor eax, eax
    x64.mov ecx, 4294967295
    x64.cmp rdx, rcx
    x64.jg main.__range_panic_0
    x64.cmp rdx, rax
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %16 = maxon.binop %0, %1 {op = add}
    %17 = maxon.binop %16, %2 {op = add}
    %18 = maxon.binop %17, %3 {op = add}
    %19 = maxon.binop %18, %4 {op = add}
    %20 = maxon.binop %19, %5 {op = add}
    %21 = maxon.binop %20, %6 {op = add}
    %22 = maxon.binop %21, %7 {op = add}
    %23 = maxon.binop %22, %8 {op = add}
    %24 = maxon.binop %23, %9 {op = add}
    %25 = maxon.binop %24, %10 {op = add}
    %26 = maxon.binop %25, %11 {op = add}
    %27 = maxon.binop %26, %12 {op = add}
    %28 = maxon.binop %27, %13 {op = add}
    %29 = maxon.binop %28, %14 {op = add}
    %30 = maxon.binop %29, %15 {op = add}
    %31 = maxon.literal {value = 256 : i64}
    %32 = maxon.binop %30, %31 {op = mod}
    %33 = maxon.literal {value = 0 : i64}
    %34 = maxon.binop %32, %33 {op = lt}
    %35 = maxon.literal {value = 4294967295 : i64}
    %36 = maxon.binop %32, %35 {op = gt}
    %37 = maxon.binop %34, %36 {op = or}
    maxon.cond_br %37 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-sixteen-vars-spill.test:19: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p]
    maxon.return %32
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.constant {value = 4 : i64}
    %4 = arith.constant {value = 5 : i64}
    %5 = arith.constant {value = 6 : i64}
    %6 = arith.constant {value = 7 : i64}
    %7 = arith.constant {value = 8 : i64}
    %8 = arith.constant {value = 9 : i64}
    %9 = arith.constant {value = 10 : i64}
    %10 = arith.constant {value = 11 : i64}
    %11 = arith.constant {value = 12 : i64}
    %12 = arith.constant {value = 13 : i64}
    %13 = arith.constant {value = 14 : i64}
    %14 = arith.constant {value = 15 : i64}
    %15 = arith.constant {value = 16 : i64}
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
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.cmpi lt %32, %33
    %35 = arith.constant {value = 4294967295 : i64}
    %36 = arith.cmpi gt %32, %35
    %37 = arith.ori1 %34, %36
    cf.cond_br %37 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %38 = memref.lea_symdata __panic_msg_0
    %39 = std.ptr_to_i64 %38
    std.call_runtime @maxon_panic %39
  __range_ok_0:
    func.return %32
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #1
    arm64.mov x1, #2
    arm64.mov x2, #3
    arm64.mov x3, #4
    arm64.mov x4, #5
    arm64.mov x5, #6
    arm64.mov x6, #7
    arm64.mov x7, #8
    arm64.mov x8, #9
    arm64.mov x9, #10
    arm64.mov x10, #11
    arm64.mov x11, #12
    arm64.mov x12, #13
    arm64.mov x13, #14
    arm64.mov x14, #15
    arm64.mov x15, #16
    arm64.add x2, x0, x1
    arm64.mov x0, #3
    arm64.add x1, x2, x0
    arm64.add x0, x1, x3
    arm64.add x1, x0, x4
    arm64.add x0, x1, x5
    arm64.add x1, x0, x6
    arm64.add x0, x1, x7
    arm64.add x1, x0, x8
    arm64.add x0, x1, x9
    arm64.add x1, x0, x10
    arm64.add x0, x1, x11
    arm64.add x1, x0, x12
    arm64.add x0, x1, x13
    arm64.add x1, x0, x14
    arm64.add x0, x1, x15
    arm64.mov x1, #256
    arm64.sdiv x2, x0, x1
    arm64.msub x3, x2, x1, x0
    arm64.mov x0, #0
    arm64.cmp x3, x0
    arm64.cset x1, lt
    arm64.mov x0, #4294967295
    arm64.cmp x3, x0
    arm64.cset x2, gt
    arm64.orr x0, x1, x2
    arm64.cmp x0, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x3
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-twenty-vars-heavy-spill -->
```maxon
function main() returns ExitCode
	let a = 1
	let b = 2
	let c = 3
	let d = 4
	let e = 5
	let f = 6
	let g = 7
	let h = 8
	let i = 9
	let j = 10
	let k = 11
	let l = 12
	let m = 13
	let n = 14
	let o = 15
	let p = 16
	let q = 17
	let r = 18
	let s = 19
	let t = 20
	return (a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p + q + r + s + t) mod 256
end 'main'
```
```exitcode
210
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 4 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 5 : i64}
    maxon.assign %4 {var = e} {kind = i64} {decl = 1 : i1}
    %5 = maxon.literal {value = 6 : i64}
    maxon.assign %5 {var = f} {kind = i64} {decl = 1 : i1}
    %6 = maxon.literal {value = 7 : i64}
    maxon.assign %6 {var = g} {kind = i64} {decl = 1 : i1}
    %7 = maxon.literal {value = 8 : i64}
    maxon.assign %7 {var = h} {kind = i64} {decl = 1 : i1}
    %8 = maxon.literal {value = 9 : i64}
    maxon.assign %8 {var = i} {kind = i64} {decl = 1 : i1}
    %9 = maxon.literal {value = 10 : i64}
    maxon.assign %9 {var = j} {kind = i64} {decl = 1 : i1}
    %10 = maxon.literal {value = 11 : i64}
    maxon.assign %10 {var = k} {kind = i64} {decl = 1 : i1}
    %11 = maxon.literal {value = 12 : i64}
    maxon.assign %11 {var = l} {kind = i64} {decl = 1 : i1}
    %12 = maxon.literal {value = 13 : i64}
    maxon.assign %12 {var = m} {kind = i64} {decl = 1 : i1}
    %13 = maxon.literal {value = 14 : i64}
    maxon.assign %13 {var = n} {kind = i64} {decl = 1 : i1}
    %14 = maxon.literal {value = 15 : i64}
    maxon.assign %14 {var = o} {kind = i64} {decl = 1 : i1}
    %15 = maxon.literal {value = 16 : i64}
    maxon.assign %15 {var = p} {kind = i64} {decl = 1 : i1}
    %16 = maxon.literal {value = 17 : i64}
    maxon.assign %16 {var = q} {kind = i64} {decl = 1 : i1}
    %17 = maxon.literal {value = 18 : i64}
    maxon.assign %17 {var = r} {kind = i64} {decl = 1 : i1}
    %18 = maxon.literal {value = 19 : i64}
    maxon.assign %18 {var = s} {kind = i64} {decl = 1 : i1}
    %19 = maxon.literal {value = 20 : i64}
    maxon.assign %19 {var = t} {kind = i64} {decl = 1 : i1}
    %20 = maxon.binop %0, %1 {op = add}
    %21 = maxon.binop %20, %2 {op = add}
    %22 = maxon.binop %21, %3 {op = add}
    %23 = maxon.binop %22, %4 {op = add}
    %24 = maxon.binop %23, %5 {op = add}
    %25 = maxon.binop %24, %6 {op = add}
    %26 = maxon.binop %25, %7 {op = add}
    %27 = maxon.binop %26, %8 {op = add}
    %28 = maxon.binop %27, %9 {op = add}
    %29 = maxon.binop %28, %10 {op = add}
    %30 = maxon.binop %29, %11 {op = add}
    %31 = maxon.binop %30, %12 {op = add}
    %32 = maxon.binop %31, %13 {op = add}
    %33 = maxon.binop %32, %14 {op = add}
    %34 = maxon.binop %33, %15 {op = add}
    %35 = maxon.binop %34, %16 {op = add}
    %36 = maxon.binop %35, %17 {op = add}
    %37 = maxon.binop %36, %18 {op = add}
    %38 = maxon.binop %37, %19 {op = add}
    %39 = maxon.literal {value = 256 : i64}
    %40 = maxon.binop %38, %39 {op = mod}
    %41 = maxon.literal {value = 0 : i64}
    %42 = maxon.binop %40, %41 {op = lt}
    %43 = maxon.literal {value = 4294967295 : i64}
    %44 = maxon.binop %40, %43 {op = gt}
    %45 = maxon.binop %42, %44 {op = or}
    maxon.cond_br %45 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-twenty-vars-heavy-spill.test:23: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p, q, r, s, t]
    maxon.return %40
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.constant {value = 4 : i64}
    %4 = arith.constant {value = 5 : i64}
    %5 = arith.constant {value = 6 : i64}
    %6 = arith.constant {value = 7 : i64}
    %7 = arith.constant {value = 8 : i64}
    %8 = arith.constant {value = 9 : i64}
    %9 = arith.constant {value = 10 : i64}
    %10 = arith.constant {value = 11 : i64}
    %11 = arith.constant {value = 12 : i64}
    %12 = arith.constant {value = 13 : i64}
    %13 = arith.constant {value = 14 : i64}
    %14 = arith.constant {value = 15 : i64}
    %15 = arith.constant {value = 16 : i64}
    %16 = arith.constant {value = 17 : i64}
    %17 = arith.constant {value = 18 : i64}
    %18 = arith.constant {value = 19 : i64}
    %19 = arith.constant {value = 20 : i64}
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
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.cmpi lt %40, %41
    %43 = arith.constant {value = 4294967295 : i64}
    %44 = arith.cmpi gt %40, %43
    %45 = arith.ori1 %42, %44
    cf.cond_br %45 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %46 = memref.lea_symdata __panic_msg_0
    %47 = std.ptr_to_i64 %46
    std.call_runtime @mrt_panic %47
  __range_ok_0:
    func.return %40
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 1
    x64.mov rcx, 2
    x64.mov rdx, 3
    x64.mov rbx, 4
    x64.mov rsi, 5
    x64.mov rdi, 6
    x64.mov r8, 7
    x64.mov r9, 8
    x64.mov rax, 9
    x64.mov rcx, 10
    x64.mov rdx, 11
    x64.mov rbx, 12
    x64.mov rsi, 13
    x64.mov rdi, 14
    x64.mov r8, 15
    x64.mov r9, 16
    x64.mov rax, 17
    x64.mov rcx, 18
    x64.mov rdx, 19
    x64.mov rbx, 20
    x64.mov rsi, 2
    x64.mov rdi, 1
    x64.add rdi, rsi
    x64.mov rsi, 3
    x64.add rdi, rsi
    x64.mov rsi, 4
    x64.add rdi, rsi
    x64.mov rsi, 5
    x64.add rdi, rsi
    x64.mov rsi, 6
    x64.add rdi, rsi
    x64.mov rsi, 7
    x64.add rdi, rsi
    x64.mov rsi, 8
    x64.add rdi, rsi
    x64.mov rsi, 9
    x64.add rdi, rsi
    x64.mov rsi, 10
    x64.add rdi, rsi
    x64.mov rsi, 11
    x64.add rdi, rsi
    x64.mov rsi, 12
    x64.add rdi, rsi
    x64.mov rsi, 13
    x64.add rdi, rsi
    x64.mov rsi, 14
    x64.add rdi, rsi
    x64.add rdi, r8
    x64.add rdi, r9
    x64.add rdi, rax
    x64.add rdi, rcx
    x64.add rdi, rdx
    x64.add rdi, rbx
    x64.mov rax, 256
    x64.mov rcx, rax
    x64.mov rax, rdi
    x64.cqo
    x64.idiv rcx
    x64.xor eax, eax
    x64.mov ecx, 4294967295
    x64.cmp rdx, rcx
    x64.jg main.__range_panic_0
    x64.cmp rdx, rax
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %20 = maxon.binop %0, %1 {op = add}
    %21 = maxon.binop %20, %2 {op = add}
    %22 = maxon.binop %21, %3 {op = add}
    %23 = maxon.binop %22, %4 {op = add}
    %24 = maxon.binop %23, %5 {op = add}
    %25 = maxon.binop %24, %6 {op = add}
    %26 = maxon.binop %25, %7 {op = add}
    %27 = maxon.binop %26, %8 {op = add}
    %28 = maxon.binop %27, %9 {op = add}
    %29 = maxon.binop %28, %10 {op = add}
    %30 = maxon.binop %29, %11 {op = add}
    %31 = maxon.binop %30, %12 {op = add}
    %32 = maxon.binop %31, %13 {op = add}
    %33 = maxon.binop %32, %14 {op = add}
    %34 = maxon.binop %33, %15 {op = add}
    %35 = maxon.binop %34, %16 {op = add}
    %36 = maxon.binop %35, %17 {op = add}
    %37 = maxon.binop %36, %18 {op = add}
    %38 = maxon.binop %37, %19 {op = add}
    %39 = maxon.literal {value = 256 : i64}
    %40 = maxon.binop %38, %39 {op = mod}
    %41 = maxon.literal {value = 0 : i64}
    %42 = maxon.binop %40, %41 {op = lt}
    %43 = maxon.literal {value = 4294967295 : i64}
    %44 = maxon.binop %40, %43 {op = gt}
    %45 = maxon.binop %42, %44 {op = or}
    maxon.cond_br %45 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-twenty-vars-heavy-spill.test:23: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p, q, r, s, t]
    maxon.return %40
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.constant {value = 3 : i64}
    %3 = arith.constant {value = 4 : i64}
    %4 = arith.constant {value = 5 : i64}
    %5 = arith.constant {value = 6 : i64}
    %6 = arith.constant {value = 7 : i64}
    %7 = arith.constant {value = 8 : i64}
    %8 = arith.constant {value = 9 : i64}
    %9 = arith.constant {value = 10 : i64}
    %10 = arith.constant {value = 11 : i64}
    %11 = arith.constant {value = 12 : i64}
    %12 = arith.constant {value = 13 : i64}
    %13 = arith.constant {value = 14 : i64}
    %14 = arith.constant {value = 15 : i64}
    %15 = arith.constant {value = 16 : i64}
    %16 = arith.constant {value = 17 : i64}
    %17 = arith.constant {value = 18 : i64}
    %18 = arith.constant {value = 19 : i64}
    %19 = arith.constant {value = 20 : i64}
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
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.cmpi lt %40, %41
    %43 = arith.constant {value = 4294967295 : i64}
    %44 = arith.cmpi gt %40, %43
    %45 = arith.ori1 %42, %44
    cf.cond_br %45 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %46 = memref.lea_symdata __panic_msg_0
    %47 = std.ptr_to_i64 %46
    std.call_runtime @maxon_panic %47
  __range_ok_0:
    func.return %40
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #1
    arm64.mov x1, #2
    arm64.mov x2, #3
    arm64.mov x3, #4
    arm64.mov x4, #5
    arm64.mov x5, #6
    arm64.mov x6, #7
    arm64.mov x7, #8
    arm64.mov x8, #9
    arm64.mov x9, #10
    arm64.mov x10, #11
    arm64.mov x11, #12
    arm64.mov x12, #13
    arm64.mov x13, #14
    arm64.mov x14, #15
    arm64.mov x15, #16
    arm64.mov x0, #17
    arm64.mov x1, #18
    arm64.mov x2, #19
    arm64.mov x3, #20
    arm64.mov x4, #2
    arm64.mov x5, #1
    arm64.add x6, x5, x4
    arm64.mov x4, #3
    arm64.add x5, x6, x4
    arm64.mov x4, #4
    arm64.add x6, x5, x4
    arm64.mov x4, #5
    arm64.add x5, x6, x4
    arm64.mov x4, #6
    arm64.add x6, x5, x4
    arm64.mov x4, #7
    arm64.add x5, x6, x4
    arm64.add x4, x5, x7
    arm64.add x5, x4, x8
    arm64.add x4, x5, x9
    arm64.add x5, x4, x10
    arm64.add x4, x5, x11
    arm64.add x5, x4, x12
    arm64.add x4, x5, x13
    arm64.add x5, x4, x14
    arm64.add x4, x5, x15
    arm64.add x5, x4, x0
    arm64.add x0, x5, x1
    arm64.add x1, x0, x2
    arm64.add x0, x1, x3
    arm64.mov x1, #256
    arm64.sdiv x2, x0, x1
    arm64.msub x3, x2, x1, x0
    arm64.mov x0, #0
    arm64.cmp x3, x0
    arm64.cset x1, lt
    arm64.mov x0, #4294967295
    arm64.cmp x3, x0
    arm64.cset x2, gt
    arm64.orr x0, x1, x2
    arm64.cmp x0, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x3
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-interleaved-lifetimes -->
```maxon
function main() returns ExitCode
	let a = 10
	let b = 20
	let ab = a + b
	let c = 30
	let d = 40
	let cd = c + d
	let e = 50
	let f = 60
	let ef = e + f
	let result = ab + cd + ef
	return result mod 256
end 'main'
```
```exitcode
210
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 20 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.binop %0, %1 {op = add}
    maxon.assign %2 {var = ab} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 30 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 40 : i64}
    maxon.assign %4 {var = d} {kind = i64} {decl = 1 : i1}
    %5 = maxon.binop %3, %4 {op = add}
    maxon.assign %5 {var = cd} {kind = i64} {decl = 1 : i1}
    %6 = maxon.literal {value = 50 : i64}
    maxon.assign %6 {var = e} {kind = i64} {decl = 1 : i1}
    %7 = maxon.literal {value = 60 : i64}
    maxon.assign %7 {var = f} {kind = i64} {decl = 1 : i1}
    %8 = maxon.binop %6, %7 {op = add}
    maxon.assign %8 {var = ef} {kind = i64} {decl = 1 : i1}
    %9 = maxon.binop %2, %5 {op = add}
    %10 = maxon.binop %9, %8 {op = add}
    maxon.assign %10 {var = result} {kind = i64} {decl = 1 : i1}
    %11 = maxon.literal {value = 256 : i64}
    %12 = maxon.binop %10, %11 {op = mod}
    %13 = maxon.literal {value = 0 : i64}
    %14 = maxon.binop %12, %13 {op = lt}
    %15 = maxon.literal {value = 4294967295 : i64}
    %16 = maxon.binop %12, %15 {op = gt}
    %17 = maxon.binop %14, %16 {op = or}
    maxon.cond_br %17 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-interleaved-lifetimes.test:13: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, ab, c, d, cd, e, f, ef, result]
    maxon.return %12
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    %1 = arith.constant {value = 20 : i64}
    %2 = arith.addi %0, %1
    %3 = arith.constant {value = 30 : i64}
    %4 = arith.constant {value = 40 : i64}
    %5 = arith.addi %3, %4
    %6 = arith.constant {value = 50 : i64}
    %7 = arith.constant {value = 60 : i64}
    %8 = arith.addi %6, %7
    %9 = arith.addi %2, %5
    %10 = arith.addi %9, %8
    %11 = arith.constant {value = 256 : i64}
    %12 = arith.remsi %10, %11
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.cmpi lt %12, %13
    %15 = arith.constant {value = 4294967295 : i64}
    %16 = arith.cmpi gt %12, %15
    %17 = arith.ori1 %14, %16
    cf.cond_br %17 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %18 = memref.lea_symdata __panic_msg_0
    %19 = std.ptr_to_i64 %18
    std.call_runtime @mrt_panic %19
  __range_ok_0:
    func.return %12
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 10
    x64.mov rcx, 20
    x64.add rax, rcx
    x64.mov rdx, 30
    x64.mov rbx, 40
    x64.add rdx, rbx
    x64.mov rsi, 50
    x64.mov rdi, 60
    x64.add rsi, rdi
    x64.add rax, rdx
    x64.add rax, rsi
    x64.mov r8, 256
    x64.mov [rbp-8], rax
    x64.cqo
    x64.idiv r8
    x64.xor r9d, r9d
    x64.mov eax, 4294967295
    x64.cmp rdx, rax
    x64.jg main.__range_panic_0
    x64.cmp rdx, r9
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 20 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.binop %0, %1 {op = add}
    maxon.assign %2 {var = ab} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 30 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 40 : i64}
    maxon.assign %4 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %3, %4 {op = add}
    maxon.assign %5 {var = cd} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 50 : i64}
    maxon.assign %6 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.literal {value = 60 : i64}
    maxon.assign %7 {var = f} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.binop %6, %7 {op = add}
    maxon.assign %8 {var = ef} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.binop %2, %5 {op = add}
    %10 = maxon.binop %9, %8 {op = add}
    maxon.assign %10 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %11 = maxon.literal {value = 256 : i64}
    %12 = maxon.binop %10, %11 {op = mod}
    %13 = maxon.literal {value = 0 : i64}
    %14 = maxon.binop %12, %13 {op = lt}
    %15 = maxon.literal {value = 4294967295 : i64}
    %16 = maxon.binop %12, %15 {op = gt}
    %17 = maxon.binop %14, %16 {op = or}
    maxon.cond_br %17 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-interleaved-lifetimes.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b, ab, c, d, cd, e, f, ef, result]
    maxon.return %12
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    %1 = arith.constant {value = 20 : i64}
    %2 = arith.addi %0, %1
    %3 = arith.constant {value = 30 : i64}
    %4 = arith.constant {value = 40 : i64}
    %5 = arith.addi %3, %4
    %6 = arith.constant {value = 50 : i64}
    %7 = arith.constant {value = 60 : i64}
    %8 = arith.addi %6, %7
    %9 = arith.addi %2, %5
    %10 = arith.addi %9, %8
    %11 = arith.constant {value = 256 : i64}
    %12 = arith.remsi %10, %11
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.cmpi lt %12, %13
    %15 = arith.constant {value = 4294967295 : i64}
    %16 = arith.cmpi gt %12, %15
    %17 = arith.ori1 %14, %16
    cf.cond_br %17 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %18 = memref.lea_symdata __panic_msg_0
    %19 = std.ptr_to_i64 %18
    std.call_runtime @maxon_panic %19
  __range_ok_0:
    func.return %12
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #10
    arm64.mov x1, #20
    arm64.add x2, x0, x1
    arm64.mov x3, #30
    arm64.mov x4, #40
    arm64.add x5, x3, x4
    arm64.mov x6, #50
    arm64.mov x7, #60
    arm64.add x8, x6, x7
    arm64.add x9, x2, x5
    arm64.add x10, x9, x8
    arm64.mov x11, #256
    arm64.sdiv x12, x10, x11
    arm64.msub x13, x12, x11, x10
    arm64.mov x14, #0
    arm64.cmp x13, x14
    arm64.cset x15, lt
    arm64.mov x0, #4294967295
    arm64.cmp x13, x0
    arm64.cset x1, gt
    arm64.orr x0, x15, x1
    arm64.cmp x0, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x13
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-parallel-accumulation -->
```maxon
function main() returns ExitCode
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
```RequiredIR:x64-windows
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
    %4 = maxon.binop %0, %3 {op = add}
    maxon.assign %4 {var = sum1} {kind = i64} {mut = 1 : i1}
    %5 = maxon.literal {value = 20 : i64}
    %6 = maxon.binop %1, %5 {op = add}
    maxon.assign %6 {var = sum2} {kind = i64} {mut = 1 : i1}
    %7 = maxon.literal {value = 30 : i64}
    %8 = maxon.binop %2, %7 {op = add}
    maxon.assign %8 {var = sum3} {kind = i64} {mut = 1 : i1}
    %9 = maxon.literal {value = 5 : i64}
    %10 = maxon.binop %4, %9 {op = add}
    maxon.assign %10 {var = sum1} {kind = i64} {mut = 1 : i1}
    %11 = maxon.literal {value = 10 : i64}
    %12 = maxon.binop %6, %11 {op = add}
    maxon.assign %12 {var = sum2} {kind = i64} {mut = 1 : i1}
    %13 = maxon.literal {value = 15 : i64}
    %14 = maxon.binop %8, %13 {op = add}
    maxon.assign %14 {var = sum3} {kind = i64} {mut = 1 : i1}
    %15 = maxon.binop %10, %12 {op = add}
    %16 = maxon.binop %15, %14 {op = add}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %16, %17 {op = lt}
    %19 = maxon.literal {value = 4294967295 : i64}
    %20 = maxon.binop %16, %19 {op = gt}
    %21 = maxon.binop %18, %20 {op = or}
    maxon.cond_br %21 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-parallel-accumulation.test:12: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [sum1, sum2, sum3]
    maxon.return %16
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 10 : i64}
    %4 = arith.constant {value = 20 : i64}
    %5 = arith.constant {value = 30 : i64}
    %6 = arith.constant {value = 5 : i64}
    %7 = arith.addi %3, %6
    %8 = arith.constant {value = 10 : i64}
    %9 = arith.addi %4, %8
    %10 = arith.constant {value = 15 : i64}
    %11 = arith.addi %5, %10
    %12 = arith.addi %7, %9
    %13 = arith.addi %12, %11
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @mrt_panic %20
  __range_ok_0:
    func.return %13
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 10
    x64.mov rcx, 20
    x64.mov rdx, 30
    x64.mov rbx, 5
    x64.add rax, rbx
    x64.mov rsi, 10
    x64.add rcx, rsi
    x64.mov rdi, 15
    x64.add rdx, rdi
    x64.add rax, rcx
    x64.add rax, rdx
    x64.xor r8d, r8d
    x64.mov r9, 4294967295
    x64.cmp rax, r9
    x64.jg main.__range_panic_0
    x64.cmp rax, r8
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %4 = maxon.binop %0, %3 {op = add}
    maxon.assign %4 {var = sum1} {kind = i64} {mut = 1 : i1}
    %5 = maxon.literal {value = 20 : i64}
    %6 = maxon.binop %1, %5 {op = add}
    maxon.assign %6 {var = sum2} {kind = i64} {mut = 1 : i1}
    %7 = maxon.literal {value = 30 : i64}
    %8 = maxon.binop %2, %7 {op = add}
    maxon.assign %8 {var = sum3} {kind = i64} {mut = 1 : i1}
    %9 = maxon.literal {value = 5 : i64}
    %10 = maxon.binop %4, %9 {op = add}
    maxon.assign %10 {var = sum1} {kind = i64} {mut = 1 : i1}
    %11 = maxon.literal {value = 10 : i64}
    %12 = maxon.binop %6, %11 {op = add}
    maxon.assign %12 {var = sum2} {kind = i64} {mut = 1 : i1}
    %13 = maxon.literal {value = 15 : i64}
    %14 = maxon.binop %8, %13 {op = add}
    maxon.assign %14 {var = sum3} {kind = i64} {mut = 1 : i1}
    %15 = maxon.binop %10, %12 {op = add}
    %16 = maxon.binop %15, %14 {op = add}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %16, %17 {op = lt}
    %19 = maxon.literal {value = 4294967295 : i64}
    %20 = maxon.binop %16, %19 {op = gt}
    %21 = maxon.binop %18, %20 {op = or}
    maxon.cond_br %21 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-parallel-accumulation.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [sum1, sum2, sum3]
    maxon.return %16
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 10 : i64}
    %4 = arith.constant {value = 20 : i64}
    %5 = arith.constant {value = 30 : i64}
    %6 = arith.constant {value = 5 : i64}
    %7 = arith.addi %3, %6
    %8 = arith.constant {value = 10 : i64}
    %9 = arith.addi %4, %8
    %10 = arith.constant {value = 15 : i64}
    %11 = arith.addi %5, %10
    %12 = arith.addi %7, %9
    %13 = arith.addi %12, %11
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @maxon_panic %20
  __range_ok_0:
    func.return %13
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #10
    arm64.mov x1, #20
    arm64.mov x2, #30
    arm64.mov x3, #5
    arm64.add x4, x0, x3
    arm64.mov x5, #10
    arm64.add x6, x1, x5
    arm64.mov x7, #15
    arm64.add x8, x2, x7
    arm64.add x9, x4, x6
    arm64.add x10, x9, x8
    arm64.mov x11, #0
    arm64.cmp x10, x11
    arm64.cset x12, lt
    arm64.mov x13, #4294967295
    arm64.cmp x10, x13
    arm64.cset x14, gt
    arm64.orr x15, x12, x14
    arm64.cmp x15, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x10
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

### Level 4: Function Calls and Fixed Register Constraints

<!-- test: int-call-preserves-value -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getForty() returns Integer
	return 40
end 'getForty'

function main() returns ExitCode
	let x = 2
	let y = getForty()
	return x + y
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @getForty() -> i64 {
  entry:
    %0 = maxon.literal {value = 40 : i64}
    maxon.scope_end []
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1}
    %2 = maxon.call @getForty
    maxon.assign %2 {var = y} {kind = i64} {decl = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add} {optimalType = i64}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    %6 = maxon.literal {value = 4294967295 : i64}
    %7 = maxon.binop %3, %6 {op = gt}
    %8 = maxon.binop %5, %7 {op = or}
    maxon.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-call-preserves-value.test:12: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, y]
    maxon.return %3
  }
}
=== standard
module {
  func @getForty() -> i64 {
  entry:
    %0 = arith.constant {value = 40 : i64}
    func.return %0
  }
  func @main() -> u32 {
  entry:
    %1 = arith.constant {value = 2 : i64}
    %2 = func.call @getForty
    %3 = arith.addi %1, %2
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.cmpi lt %3, %4
    %6 = arith.constant {value = 4294967295 : i64}
    %7 = arith.cmpi gt %3, %6
    %8 = arith.ori1 %5, %7
    cf.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %9 = memref.lea_symdata __panic_msg_0
    %10 = std.ptr_to_i64 %9
    std.call_runtime @mrt_panic %10
  __range_ok_0:
    func.return %3
  }
}
=== x86
module {
  func @getForty() -> i64 {
  entry:
    x64.mov rax, 40
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 2
    x64.call getForty
    x64.mov rcx, 2
    x64.add rcx, rax
    x64.xor edx, edx
    x64.mov ebx, 4294967295
    x64.cmp rcx, rbx
    x64.jg main.__range_panic_0
    x64.cmp rcx, rdx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rcx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.getForty() -> i64 {
  entry:
    %0 = maxon.literal {value = 40 : i64}
    maxon.scope_end []
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.literal {value = 2 : i64}
    maxon.assign %1 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.call @register-allocator.getForty
    maxon.assign %2 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add}
    %4 = maxon.literal {value = 0 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    %6 = maxon.literal {value = 4294967295 : i64}
    %7 = maxon.binop %3, %6 {op = gt}
    %8 = maxon.binop %5, %7 {op = or}
    maxon.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-call-preserves-value.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [x, y]
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
  func @main() -> u32 {
  entry:
    %1 = arith.constant {value = 2 : i64}
    %2 = func.call @register-allocator.getForty
    %3 = arith.addi %1, %2
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.cmpi lt %3, %4
    %6 = arith.constant {value = 4294967295 : i64}
    %7 = arith.cmpi gt %3, %6
    %8 = arith.ori1 %5, %7
    cf.cond_br %8 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %9 = memref.lea_symdata __panic_msg_0
    %10 = std.ptr_to_i64 %9
    std.call_runtime @maxon_panic %10
  __range_ok_0:
    func.return %3
  }
}
=== arm64
module {
  func @register-allocator.getForty() -> i64 {
  entry:
    arm64.mov x0, #40
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #2
    arm64.bl register-allocator.getForty
    arm64.mov x1, #2
    arm64.add x2, x1, x0
    arm64.mov x3, #0
    arm64.cmp x2, x3
    arm64.cset x4, lt
    arm64.mov x5, #4294967295
    arm64.cmp x2, x5
    arm64.cset x6, gt
    arm64.orr x7, x4, x6
    arm64.cmp x7, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x2
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-multiple-calls-preserve -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getTen() returns Integer
	return 10
end 'getTen'

function getTwo() returns Integer
	return 2
end 'getTwo'

function main() returns ExitCode
	let a = 5
	let b = getTen()
	let c = 7
	let d = getTwo()
	return a + b + c + d
end 'main'
```
```exitcode
24
```
```RequiredIR:x64-windows
=== maxon
module {
  func @getTen() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.scope_end []
    maxon.return %0
  }
  func @getTwo() -> i64 {
  entry:
    %1 = maxon.literal {value = 2 : i64}
    maxon.scope_end []
    maxon.return %1
  }
  func @main() -> i64 {
  entry:
    %2 = maxon.literal {value = 5 : i64}
    maxon.assign %2 {var = a} {kind = i64} {decl = 1 : i1}
    %3 = maxon.call @getTen
    maxon.assign %3 {var = b} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 7 : i64}
    maxon.assign %4 {var = c} {kind = i64} {decl = 1 : i1}
    %5 = maxon.call @getTwo
    maxon.assign %5 {var = d} {kind = i64} {decl = 1 : i1}
    %6 = maxon.binop %2, %3 {op = add} {optimalType = i64}
    %7 = maxon.binop %6, %4 {op = add}
    %8 = maxon.binop %7, %5 {op = add} {optimalType = i64}
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.binop %8, %9 {op = lt}
    %11 = maxon.literal {value = 4294967295 : i64}
    %12 = maxon.binop %8, %11 {op = gt}
    %13 = maxon.binop %10, %12 {op = or}
    maxon.cond_br %13 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-multiple-calls-preserve.test:18: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, c, d]
    maxon.return %8
  }
}
=== standard
module {
  func @getTen() -> i64 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    func.return %0
  }
  func @getTwo() -> i64 {
  entry:
    %1 = arith.constant {value = 2 : i64}
    func.return %1
  }
  func @main() -> u32 {
  entry:
    %2 = arith.constant {value = 5 : i64}
    %3 = func.call @getTen
    %4 = arith.constant {value = 7 : i64}
    %5 = func.call @getTwo
    %6 = arith.addi %2, %3
    %7 = arith.addi %6, %4
    %8 = arith.addi %7, %5
    %9 = arith.constant {value = 0 : i64}
    %10 = arith.cmpi lt %8, %9
    %11 = arith.constant {value = 4294967295 : i64}
    %12 = arith.cmpi gt %8, %11
    %13 = arith.ori1 %10, %12
    cf.cond_br %13 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %14 = memref.lea_symdata __panic_msg_0
    %15 = std.ptr_to_i64 %14
    std.call_runtime @mrt_panic %15
  __range_ok_0:
    func.return %8
  }
}
=== x86
module {
  func @getTen() -> i64 {
  entry:
    x64.mov rax, 10
    x64.ret
  }
  func @getTwo() -> i64 {
  entry:
    x64.mov rax, 2
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 5
    x64.call getTen
    x64.mov rcx, 7
    x64.mov [rbp-8], rax
    x64.call getTwo
    x64.mov rdx, [rbp-8]
    x64.mov rbx, 5
    x64.add rbx, rdx
    x64.mov rsi, 7
    x64.add rbx, rsi
    x64.add rbx, rax
    x64.xor edi, edi
    x64.mov r8, 4294967295
    x64.cmp rbx, r8
    x64.jg main.__range_panic_0
    x64.cmp rbx, rdi
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rbx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.getTen() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.scope_end []
    maxon.return %0
  }
  func @register-allocator.getTwo() -> i64 {
  entry:
    %1 = maxon.literal {value = 2 : i64}
    maxon.scope_end []
    maxon.return %1
  }
  func @main() -> i64 {
  entry:
    %2 = maxon.literal {value = 5 : i64}
    maxon.assign %2 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.call @register-allocator.getTen
    maxon.assign %3 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 7 : i64}
    maxon.assign %4 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.call @register-allocator.getTwo
    maxon.assign %5 {var = d} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.binop %2, %3 {op = add}
    %7 = maxon.binop %6, %4 {op = add}
    %8 = maxon.binop %7, %5 {op = add}
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.binop %8, %9 {op = lt}
    %11 = maxon.literal {value = 4294967295 : i64}
    %12 = maxon.binop %8, %11 {op = gt}
    %13 = maxon.binop %10, %12 {op = or}
    maxon.cond_br %13 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-multiple-calls-preserve.test:18: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b, c, d]
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
  func @main() -> u32 {
  entry:
    %2 = arith.constant {value = 5 : i64}
    %3 = func.call @register-allocator.getTen
    %4 = arith.constant {value = 7 : i64}
    %5 = func.call @register-allocator.getTwo
    %6 = arith.addi %2, %3
    %7 = arith.addi %6, %4
    %8 = arith.addi %7, %5
    %9 = arith.constant {value = 0 : i64}
    %10 = arith.cmpi lt %8, %9
    %11 = arith.constant {value = 4294967295 : i64}
    %12 = arith.cmpi gt %8, %11
    %13 = arith.ori1 %10, %12
    cf.cond_br %13 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %14 = memref.lea_symdata __panic_msg_0
    %15 = std.ptr_to_i64 %14
    std.call_runtime @maxon_panic %15
  __range_ok_0:
    func.return %8
  }
}
=== arm64
module {
  func @register-allocator.getTen() -> i64 {
  entry:
    arm64.mov x0, #10
    arm64.ret
  }
  func @register-allocator.getTwo() -> i64 {
  entry:
    arm64.mov x0, #2
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=32
    arm64.mov x0, #5
    arm64.bl register-allocator.getTen
    arm64.mov x1, #7
    arm64.str x0, [x29, #-8]
    arm64.bl register-allocator.getTwo
    arm64.ldr x2, [x29, #-8]
    arm64.mov x3, #5
    arm64.add x4, x3, x2
    arm64.mov x5, #7
    arm64.add x6, x4, x5
    arm64.add x7, x6, x0
    arm64.mov x8, #0
    arm64.cmp x7, x8
    arm64.cset x9, lt
    arm64.mov x10, #4294967295
    arm64.cmp x7, x10
    arm64.cset x11, gt
    arm64.orr x12, x9, x11
    arm64.cmp x12, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x7
    arm64.epilogue stack_size=32
    arm64.ret
  }
}
```

<!-- test: int-call-result-used-later -->
```maxon

typealias Integer = int(i64.min to i64.max)

function compute() returns Integer
	return 100
end 'compute'

function main() returns ExitCode
	let a = compute()
	let b = compute()
	return (a + b) mod 256
end 'main'
```
```exitcode
200
```
```RequiredIR:x64-windows
=== maxon
module {
  func @compute() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.scope_end []
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.call @compute
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1}
    %2 = maxon.call @compute
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add} {optimalType = i64}
    %4 = maxon.literal {value = 256 : i64}
    %5 = maxon.binop %3, %4 {op = mod}
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-call-result-used-later.test:12: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b]
    maxon.return %5
  }
}
=== standard
module {
  func @compute() -> i64 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    func.return %0
  }
  func @main() -> u32 {
  entry:
    %1 = func.call @compute
    %2 = func.call @compute
    %3 = arith.addi %1, %2
    %4 = arith.constant {value = 256 : i64}
    %5 = arith.remsi %3, %4
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi lt %5, %6
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.cmpi gt %5, %8
    %10 = arith.ori1 %7, %9
    cf.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %11 = memref.lea_symdata __panic_msg_0
    %12 = std.ptr_to_i64 %11
    std.call_runtime @mrt_panic %12
  __range_ok_0:
    func.return %5
  }
}
=== x86
module {
  func @compute() -> i64 {
  entry:
    x64.mov rax, 100
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.call compute
    x64.mov [rbp-8], rax
    x64.call compute
    x64.mov rcx, [rbp-8]
    x64.add rcx, rax
    x64.mov rax, 256
    x64.mov rbx, rax
    x64.mov rax, rcx
    x64.cqo
    x64.idiv rbx
    x64.xor ecx, ecx
    x64.mov eax, 4294967295
    x64.cmp rdx, rax
    x64.jg main.__range_panic_0
    x64.cmp rdx, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.compute() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.scope_end []
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.call @register-allocator.compute
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.call @register-allocator.compute
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %1, %2 {op = add}
    %4 = maxon.literal {value = 256 : i64}
    %5 = maxon.binop %3, %4 {op = mod}
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-call-result-used-later.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b]
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
  func @main() -> u32 {
  entry:
    %1 = func.call @register-allocator.compute
    %2 = func.call @register-allocator.compute
    %3 = arith.addi %1, %2
    %4 = arith.constant {value = 256 : i64}
    %5 = arith.remsi %3, %4
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi lt %5, %6
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.cmpi gt %5, %8
    %10 = arith.ori1 %7, %9
    cf.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %11 = memref.lea_symdata __panic_msg_0
    %12 = std.ptr_to_i64 %11
    std.call_runtime @maxon_panic %12
  __range_ok_0:
    func.return %5
  }
}
=== arm64
module {
  func @register-allocator.compute() -> i64 {
  entry:
    arm64.mov x0, #100
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=32
    arm64.bl register-allocator.compute
    arm64.str x0, [x29, #-8]
    arm64.bl register-allocator.compute
    arm64.ldr x1, [x29, #-8]
    arm64.add x2, x1, x0
    arm64.mov x0, #256
    arm64.sdiv x1, x2, x0
    arm64.msub x3, x1, x0, x2
    arm64.mov x2, #0
    arm64.cmp x3, x2
    arm64.cset x0, lt
    arm64.mov x1, #4294967295
    arm64.cmp x3, x1
    arm64.cset x2, gt
    arm64.orr x1, x0, x2
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x3
    arm64.epilogue stack_size=32
    arm64.ret
  }
}
```

<!-- test: int-division-fixed-regs -->
```maxon
function main() returns ExitCode
	let a = 126
	let b = 3
	return a / b
end 'main'
```
```exitcode
42
```

<!-- test: int-division-preserves-other-values -->
```maxon
function main() returns ExitCode
	let x = 10
	let a = 84
	let b = 2
	let quotient = a / b
	return quotient - x
end 'main'
```
```exitcode
32
```

<!-- test: int-function-with-params -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(30, b: 12)
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @add(a: i64, b: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    maxon.scope_end [a, b]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 30 : i64}
    %4 = maxon.literal {value = 12 : i64}
    %5 = maxon.call @add %3, %4
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-function-with-params.test:10: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %5
  }
}
=== standard
module {
  func @add(a: i64, b: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = arith.addi %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 30 : i64}
    %4 = arith.constant {value = 12 : i64}
    %5 = func.call @add %3, %4
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi lt %5, %6
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.cmpi gt %5, %8
    %10 = arith.ori1 %7, %9
    cf.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %11 = memref.lea_symdata __panic_msg_0
    %12 = std.ptr_to_i64 %11
    std.call_runtime @mrt_panic %12
  __range_ok_0:
    func.return %5
  }
}
=== x86
module {
  func @add(a: i64, b: i64) -> i64 {
  entry:
    x64.lea rax, [rcx + rdx]
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 30
    x64.mov rdx, 12
    x64.call add
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    maxon.scope_end [a, b]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 30 : i64}
    %4 = maxon.literal {value = 12 : i64}
    %5 = maxon.call @register-allocator.add %3, %4
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-function-with-params.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %5
  }
}
=== standard
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = arith.addi %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 30 : i64}
    %4 = arith.constant {value = 12 : i64}
    %5 = func.call @register-allocator.add %3, %4
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi lt %5, %6
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.cmpi gt %5, %8
    %10 = arith.ori1 %7, %9
    cf.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %11 = memref.lea_symdata __panic_msg_0
    %12 = std.ptr_to_i64 %11
    std.call_runtime @maxon_panic %12
  __range_ok_0:
    func.return %5
  }
}
=== arm64
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    arm64.add x2, x0, x1
    arm64.mov x0, x2
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #30
    arm64.mov x1, #12
    arm64.bl register-allocator.add
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #4294967295
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-mov-reg-reg-32bit -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	let x = 20
	let y = 22
	return add(y, b: x)
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @add(a: i64, b: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    maxon.scope_end [a, b]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 20 : i64}
    maxon.assign %3 {var = x} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 22 : i64}
    maxon.assign %4 {var = y} {kind = i64} {decl = 1 : i1}
    %5 = maxon.call @add %4, %3
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-mov-reg-reg-32bit.test:12: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, y]
    maxon.return %5
  }
}
=== standard
module {
  func @add(a: i64, b: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = arith.addi %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 20 : i64}
    %4 = arith.constant {value = 22 : i64}
    %5 = func.call @add %4, %3
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi lt %5, %6
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.cmpi gt %5, %8
    %10 = arith.ori1 %7, %9
    cf.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %11 = memref.lea_symdata __panic_msg_0
    %12 = std.ptr_to_i64 %11
    std.call_runtime @mrt_panic %12
  __range_ok_0:
    func.return %5
  }
}
=== x86
module {
  func @add(a: i64, b: i64) -> i64 {
  entry:
    x64.lea rax, [rcx + rdx]
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 22
    x64.mov rdx, 20
    x64.call add
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    maxon.scope_end [a, b]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 20 : i64}
    maxon.assign %3 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 22 : i64}
    maxon.assign %4 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.call @register-allocator.add %4, %3
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.binop %5, %6 {op = lt}
    %8 = maxon.literal {value = 4294967295 : i64}
    %9 = maxon.binop %5, %8 {op = gt}
    %10 = maxon.binop %7, %9 {op = or}
    maxon.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-mov-reg-reg-32bit.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [x, y]
    maxon.return %5
  }
}
=== standard
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = arith.addi %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 20 : i64}
    %4 = arith.constant {value = 22 : i64}
    %5 = func.call @register-allocator.add %4, %3
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi lt %5, %6
    %8 = arith.constant {value = 4294967295 : i64}
    %9 = arith.cmpi gt %5, %8
    %10 = arith.ori1 %7, %9
    cf.cond_br %10 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %11 = memref.lea_symdata __panic_msg_0
    %12 = std.ptr_to_i64 %11
    std.call_runtime @maxon_panic %12
  __range_ok_0:
    func.return %5
  }
}
=== arm64
module {
  func @register-allocator.add(a: i64, b: i64) -> i64 {
  entry:
    arm64.add x2, x0, x1
    arm64.mov x0, x2
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #22
    arm64.mov x1, #20
    arm64.bl register-allocator.add
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #4294967295
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

### Level 5: Control Flow and Loops

<!-- test: int-if-else-simple -->
```maxon
function main() returns ExitCode
	let x = 10
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
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 10 : i64}
    %2 = maxon.binop %0, %1 {op = eq}
    maxon.cond_br %2 [then: check_0, else: other_0]
  check_0:
    %3 = maxon.literal {value = 42 : i64}
    maxon.scope_end [x]
    maxon.return %3
  other_0:
    %4 = maxon.literal {value = 0 : i64}
    maxon.scope_end [x]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    %1 = arith.constant {value = 10 : i64}
    %2 = arith.cmpi eq %0, %1
    cf.cond_br %2 [then: check_0, else: other_0]
  check_0:
    %3 = arith.constant {value = 42 : i64}
    func.return %3
  other_0:
    %4 = arith.constant {value = 0 : i64}
    func.return %4
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.mov rax, 10
    x64.mov rcx, 10
    x64.cmp rax, rcx
    x64.jne main.other_0
  check_0:
    x64.mov rax, 42
    x64.ret
  other_0:
    x64.xor eax, eax
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 10 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 10 : i64}
    %2 = maxon.binop %0, %1 {op = eq}
    maxon.cond_br %2 [then: check_0, else: other_1]
  check_0:
    %3 = maxon.literal {value = 42 : i64}
    maxon.scope_end [x]
    maxon.return %3
  other_1:
    %4 = maxon.literal {value = 0 : i64}
    maxon.scope_end [x]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 10 : i64}
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
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.mov x0, #10
    arm64.mov x1, #10
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.check_0
    arm64.b main.other_1
  check_0:
    arm64.mov x0, #42
    arm64.ret
  other_1:
    arm64.mov x0, #0
    arm64.ret
  }
}
```

<!-- test: int-if-else-value-survives-branch -->
```maxon
function main() returns ExitCode
	let base = 40
	let cond = 1
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
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 40 : i64}
    maxon.assign %0 {var = base} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = cond} {kind = i64} {decl = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = extra} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %1, %3 {op = eq}
    maxon.cond_br %4 [then: check_0, else: other_0]
  check_0:
    %5 = maxon.literal {value = 2 : i64}
    maxon.assign %5 {var = extra} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br check_0.merge
  other_0:
    %6 = maxon.literal {value = 100 : i64}
    maxon.assign %6 {var = extra} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br check_0.merge
  check_0.merge:
    %7 = maxon.var_ref {var = base} {type = i64}
    %8 = maxon.var_ref {var = extra} {type = i64}
    %9 = maxon.binop %7, %8 {op = add}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %9, %10 {op = lt}
    %12 = maxon.literal {value = 4294967295 : i64}
    %13 = maxon.binop %9, %12 {op = gt}
    %14 = maxon.binop %11, %13 {op = or}
    maxon.cond_br %14 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-if-else-value-survives-branch.test:11: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [base, cond, extra]
    maxon.return %9
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 40 : i64}
    memref.store %0, base
    %1 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.cmpi eq %1, %3
    cf.cond_br %4 [then: check_0, else: other_0]
  check_0:
    %5 = arith.constant {value = 2 : i64}
    memref.store %5, extra
    cf.br check_0.merge
  other_0:
    %6 = arith.constant {value = 100 : i64}
    memref.store %6, extra
    cf.br check_0.merge
  check_0.merge:
    %7 = memref.load base : i64
    %8 = memref.load extra : i64
    %9 = arith.addi %7, %8
    %10 = arith.constant {value = 0 : i64}
    %11 = arith.cmpi lt %9, %10
    %12 = arith.constant {value = 4294967295 : i64}
    %13 = arith.cmpi gt %9, %12
    %14 = arith.ori1 %11, %13
    cf.cond_br %14 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %15 = memref.lea_symdata __panic_msg_0
    %16 = std.ptr_to_i64 %15
    std.call_runtime @mrt_panic %16
  __range_ok_0:
    func.return %9
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 40
    x64.mov [rbp-8], rax
    x64.mov rcx, 1
    x64.mov rdx, 1
    x64.cmp rcx, rdx
    x64.jne main.other_0
  check_0:
    x64.mov rax, 2
    x64.mov [rbp-16], rax
    x64.jmp main.check_0.merge
  other_0:
    x64.mov rax, 100
    x64.mov [rbp-16], rax
    x64.jmp main.check_0.merge
  check_0.merge:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.add rax, rcx
    x64.xor edx, edx
    x64.mov ebx, 4294967295
    x64.cmp rax, rbx
    x64.jg main.__range_panic_0
    x64.cmp rax, rdx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %4 = maxon.binop %1, %3 {op = eq}
    maxon.cond_br %4 [then: check_0, else: other_1]
  check_0:
    %5 = maxon.literal {value = 2 : i64}
    maxon.assign %5 {var = extra} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br check_0.merge
  other_1:
    %6 = maxon.literal {value = 100 : i64}
    maxon.assign %6 {var = extra} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br check_0.merge
  check_0.merge:
    %7 = maxon.var_ref {var = base} {type = i64}
    %8 = maxon.var_ref {var = extra} {type = i64}
    %9 = maxon.binop %7, %8 {op = add}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %9, %10 {op = lt}
    %12 = maxon.literal {value = 4294967295 : i64}
    %13 = maxon.binop %9, %12 {op = gt}
    %14 = maxon.binop %11, %13 {op = or}
    maxon.cond_br %14 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at int-if-else-value-survives-branch.test:11: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    maxon.scope_end [base, cond, extra]
    maxon.return %9
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 40 : i64}
    memref.store %0, base
    %1 = arith.constant {value = 1 : i64}
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
    %10 = arith.constant {value = 0 : i64}
    %11 = arith.cmpi lt %9, %10
    %12 = arith.constant {value = 4294967295 : i64}
    %13 = arith.cmpi gt %9, %12
    %14 = arith.ori1 %11, %13
    cf.cond_br %14 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %15 = memref.lea_symdata __panic_msg_0
    %16 = std.ptr_to_i64 %15
    std.call_runtime @maxon_panic %16
  __range_ok_2:
    func.return %9
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #40
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.mov x2, #1
    arm64.cmp x1, x2
    arm64.cset x3, eq
    arm64.cmp x3, #0
    arm64.b.ne main.check_0
    arm64.b main.other_1
  check_0:
    arm64.mov x0, #2
    arm64.str x0, [x29, #-16]
    arm64.b main.check_0.merge
  other_1:
    arm64.mov x0, #100
    arm64.str x0, [x29, #-16]
    arm64.b main.check_0.merge
  check_0.merge:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x0, x1
    arm64.mov x3, #0
    arm64.cmp x2, x3
    arm64.cset x4, lt
    arm64.mov x5, #4294967295
    arm64.cmp x2, x5
    arm64.cset x6, gt
    arm64.orr x7, x4, x6
    arm64.cmp x7, #0
    arm64.b.ne main.__range_panic_2
    arm64.b main.__range_ok_2
  __range_panic_2:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_2:
    arm64.mov x0, x2
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: int-while-loop-counter -->
```maxon
function main() returns ExitCode
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
```RequiredIR:x64-windows
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
    %3 = maxon.binop %2, %1 {op = lt}
    maxon.cond_br %3 [then: loop_0, else: loop_0.exit]
  loop_0:
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.var_ref {var = i} {type = i64}
    %6 = maxon.binop %5, %4 {op = add}
    maxon.assign %6 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %7 = maxon.var_ref {var = i} {type = i64}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-while-loop-counter.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [i]
    maxon.return %7
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.cmpi lt %7, %8
    %10 = arith.constant {value = 4294967295 : i64}
    %11 = arith.cmpi gt %7, %10
    %12 = arith.ori1 %9, %11
    cf.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %13 = memref.lea_symdata __panic_msg_0
    %14 = std.ptr_to_i64 %13
    std.call_runtime @mrt_panic %14
  __range_ok_0:
    func.return %7
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.jmp main.loop_0.header
  loop_0.header:
    x64.mov rax, 42
    x64.mov rcx, [rbp-8]
    x64.cmp rcx, rax
    x64.jge main.loop_0.exit
  loop_0:
    x64.mov rax, 1
    x64.mov rcx, [rbp-8]
    x64.add rcx, rax
    x64.mov [rbp-8], rcx
    x64.jmp main.loop_0.header
  loop_0.exit:
    x64.mov rax, [rbp-8]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %3 = maxon.binop %2, %1 {op = lt}
    maxon.cond_br %3 [then: loop_0, else: loop_0.exit]
  loop_0:
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.var_ref {var = i} {type = i64}
    %6 = maxon.binop %5, %4 {op = add}
    maxon.assign %6 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %7 = maxon.var_ref {var = i} {type = i64}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at int-while-loop-counter.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [i]
    maxon.return %7
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.cmpi lt %7, %8
    %10 = arith.constant {value = 4294967295 : i64}
    %11 = arith.cmpi gt %7, %10
    %12 = arith.ori1 %9, %11
    cf.cond_br %12 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %13 = memref.lea_symdata __panic_msg_0
    %14 = std.ptr_to_i64 %13
    std.call_runtime @maxon_panic %14
  __range_ok_1:
    func.return %7
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.b main.loop_0.header
  loop_0.header:
    arm64.mov x0, #42
    arm64.ldr x1, [x29, #-8]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.loop_0
    arm64.b main.loop_0.exit
  loop_0:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-8]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-8]
    arm64.b main.loop_0.header
  loop_0.exit:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: int-while-loop-accumulator -->
```maxon
function main() returns ExitCode
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
```RequiredIR:x64-windows
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
    %4 = maxon.binop %3, %2 {op = lt}
    maxon.cond_br %4 [then: loop_0, else: loop_0.exit]
  loop_0:
    %5 = maxon.var_ref {var = sum} {type = i64}
    %6 = maxon.var_ref {var = i} {type = i64}
    %7 = maxon.binop %5, %6 {op = add}
    maxon.assign %7 {var = sum} {kind = i64} {mut = 1 : i1}
    %8 = maxon.literal {value = 1 : i64}
    %9 = maxon.var_ref {var = i} {type = i64}
    %10 = maxon.binop %9, %8 {op = add}
    maxon.assign %10 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %11 = maxon.literal {value = 256 : i64}
    %12 = maxon.var_ref {var = sum} {type = i64}
    %13 = maxon.binop %12, %11 {op = mod}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %13, %14 {op = lt}
    %16 = maxon.literal {value = 4294967295 : i64}
    %17 = maxon.binop %13, %16 {op = gt}
    %18 = maxon.binop %15, %17 {op = or}
    maxon.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-while-loop-accumulator.test:9: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [sum, i]
    maxon.return %13
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @mrt_panic %20
  __range_ok_0:
    func.return %13
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.xor ecx, ecx
    x64.mov [rbp-16], rcx
    x64.jmp main.loop_0.header
  loop_0.header:
    x64.mov rax, 10
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.jge main.loop_0.exit
  loop_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.add rax, rcx
    x64.mov [rbp-8], rax
    x64.mov rdx, 1
    x64.mov rbx, [rbp-16]
    x64.add rbx, rdx
    x64.mov [rbp-16], rbx
    x64.jmp main.loop_0.header
  loop_0.exit:
    x64.mov rax, 256
    x64.mov rcx, [rbp-8]
    x64.mov rbx, rax
    x64.mov rax, rcx
    x64.cqo
    x64.idiv rbx
    x64.xor eax, eax
    x64.mov ecx, 4294967295
    x64.cmp rdx, rcx
    x64.jg main.__range_panic_0
    x64.cmp rdx, rax
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %4 = maxon.binop %3, %2 {op = lt}
    maxon.cond_br %4 [then: loop_0, else: loop_0.exit]
  loop_0:
    %5 = maxon.var_ref {var = sum} {type = i64}
    %6 = maxon.var_ref {var = i} {type = i64}
    %7 = maxon.binop %5, %6 {op = add}
    maxon.assign %7 {var = sum} {kind = i64} {mut = 1 : i1}
    %8 = maxon.literal {value = 1 : i64}
    %9 = maxon.var_ref {var = i} {type = i64}
    %10 = maxon.binop %9, %8 {op = add}
    maxon.assign %10 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %11 = maxon.literal {value = 256 : i64}
    %12 = maxon.var_ref {var = sum} {type = i64}
    %13 = maxon.binop %12, %11 {op = mod}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %13, %14 {op = lt}
    %16 = maxon.literal {value = 4294967295 : i64}
    %17 = maxon.binop %13, %16 {op = gt}
    %18 = maxon.binop %15, %17 {op = or}
    maxon.cond_br %18 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at int-while-loop-accumulator.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [sum, i]
    maxon.return %13
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @maxon_panic %20
  __range_ok_1:
    func.return %13
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-16]
    arm64.b main.loop_0.header
  loop_0.header:
    arm64.mov x0, #10
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.loop_0
    arm64.b main.loop_0.exit
  loop_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x0, x1
    arm64.str x2, [x29, #-8]
    arm64.mov x3, #1
    arm64.ldr x4, [x29, #-16]
    arm64.add x5, x4, x3
    arm64.str x5, [x29, #-16]
    arm64.b main.loop_0.header
  loop_0.exit:
    arm64.mov x0, #256
    arm64.ldr x1, [x29, #-8]
    arm64.sdiv x2, x1, x0
    arm64.msub x3, x2, x0, x1
    arm64.mov x4, #0
    arm64.cmp x3, x4
    arm64.cset x5, lt
    arm64.mov x6, #4294967295
    arm64.cmp x3, x6
    arm64.cset x7, gt
    arm64.orr x8, x5, x7
    arm64.cmp x8, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.mov x0, x3
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: int-while-loop-multiple-accumulators -->
```maxon
function main() returns ExitCode
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
```RequiredIR:x64-windows
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
    %6 = maxon.binop %5, %4 {op = lt}
    maxon.cond_br %6 [then: loop_0, else: loop_0.exit]
  loop_0:
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.var_ref {var = i} {type = i64}
    %9 = maxon.binop %8, %7 {op = mod}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %9, %10 {op = eq}
    maxon.cond_br %11 [then: even_0, else: odd_0]
  even_0:
    %12 = maxon.var_ref {var = even_sum} {type = i64}
    %13 = maxon.var_ref {var = i} {type = i64}
    %14 = maxon.binop %12, %13 {op = add}
    maxon.assign %14 {var = even_sum} {kind = i64} {mut = 1 : i1}
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = count} {type = i64}
    %17 = maxon.binop %16, %15 {op = add}
    maxon.assign %17 {var = count} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br even_0.merge
  odd_0:
    %18 = maxon.var_ref {var = odd_sum} {type = i64}
    %19 = maxon.var_ref {var = i} {type = i64}
    %20 = maxon.binop %18, %19 {op = add}
    maxon.assign %20 {var = odd_sum} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br even_0.merge
  even_0.merge:
    %21 = maxon.literal {value = 1 : i64}
    %22 = maxon.var_ref {var = i} {type = i64}
    %23 = maxon.binop %22, %21 {op = add}
    maxon.assign %23 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %24 = maxon.var_ref {var = even_sum} {type = i64}
    %25 = maxon.var_ref {var = odd_sum} {type = i64}
    %26 = maxon.binop %24, %25 {op = add}
    %27 = maxon.var_ref {var = count} {type = i64}
    %28 = maxon.binop %26, %27 {op = add}
    %29 = maxon.literal {value = 256 : i64}
    %30 = maxon.binop %28, %29 {op = mod}
    %31 = maxon.literal {value = 0 : i64}
    %32 = maxon.binop %30, %31 {op = lt}
    %33 = maxon.literal {value = 4294967295 : i64}
    %34 = maxon.binop %30, %33 {op = gt}
    %35 = maxon.binop %32, %34 {op = or}
    maxon.cond_br %35 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-while-loop-multiple-accumulators.test:16: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [even_sum, odd_sum, count, i]
    maxon.return %30
  }
}
=== standard
module {
  func @main() -> u32 {
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
    cf.cond_br %11 [then: even_0, else: odd_0]
  even_0:
    %12 = memref.load even_sum : i64
    %13 = memref.load i : i64
    %14 = arith.addi %12, %13
    memref.store %14, even_sum
    %15 = arith.constant {value = 1 : i64}
    %16 = memref.load count : i64
    %17 = arith.addi %16, %15
    memref.store %17, count
    cf.br even_0.merge
  odd_0:
    %18 = memref.load odd_sum : i64
    %19 = memref.load i : i64
    %20 = arith.addi %18, %19
    memref.store %20, odd_sum
    cf.br even_0.merge
  even_0.merge:
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
    %31 = arith.constant {value = 0 : i64}
    %32 = arith.cmpi lt %30, %31
    %33 = arith.constant {value = 4294967295 : i64}
    %34 = arith.cmpi gt %30, %33
    %35 = arith.ori1 %32, %34
    cf.cond_br %35 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %36 = memref.lea_symdata __panic_msg_0
    %37 = std.ptr_to_i64 %36
    std.call_runtime @mrt_panic %37
  __range_ok_0:
    func.return %30
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=48
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.xor ecx, ecx
    x64.mov [rbp-16], rcx
    x64.xor edx, edx
    x64.mov [rbp-24], rdx
    x64.xor ebx, ebx
    x64.mov [rbp-32], rbx
    x64.jmp main.loop_0.header
  loop_0.header:
    x64.mov rax, 20
    x64.mov rcx, [rbp-32]
    x64.cmp rcx, rax
    x64.jge main.loop_0.exit
  loop_0:
    x64.mov rax, 2
    x64.mov rcx, [rbp-32]
    x64.mov rbx, rax
    x64.mov rax, rcx
    x64.cqo
    x64.idiv rbx
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.jne main.odd_0
  even_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-32]
    x64.add rax, rcx
    x64.mov [rbp-8], rax
    x64.mov rdx, 1
    x64.mov rbx, [rbp-24]
    x64.add rbx, rdx
    x64.mov [rbp-24], rbx
    x64.jmp main.even_0.merge
  odd_0:
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rbp-32]
    x64.add rax, rcx
    x64.mov [rbp-16], rax
    x64.jmp main.even_0.merge
  even_0.merge:
    x64.mov rax, 1
    x64.mov rcx, [rbp-32]
    x64.add rcx, rax
    x64.mov [rbp-32], rcx
    x64.jmp main.loop_0.header
  loop_0.exit:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.add rax, rcx
    x64.mov rdx, [rbp-24]
    x64.add rax, rdx
    x64.mov rbx, 256
    x64.mov [rbp-40], rax
    x64.cqo
    x64.idiv rbx
    x64.xor esi, esi
    x64.mov edi, 4294967295
    x64.cmp rdx, rdi
    x64.jg main.__range_panic_0
    x64.cmp rdx, rsi
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %6 = maxon.binop %5, %4 {op = lt}
    maxon.cond_br %6 [then: loop_0, else: loop_0.exit]
  loop_0:
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.var_ref {var = i} {type = i64}
    %9 = maxon.binop %8, %7 {op = mod}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %9, %10 {op = eq}
    maxon.cond_br %11 [then: even_1, else: odd_2]
  even_1:
    %12 = maxon.var_ref {var = even_sum} {type = i64}
    %13 = maxon.var_ref {var = i} {type = i64}
    %14 = maxon.binop %12, %13 {op = add}
    maxon.assign %14 {var = even_sum} {kind = i64} {mut = 1 : i1}
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = count} {type = i64}
    %17 = maxon.binop %16, %15 {op = add}
    maxon.assign %17 {var = count} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br even_1.merge
  odd_2:
    %18 = maxon.var_ref {var = odd_sum} {type = i64}
    %19 = maxon.var_ref {var = i} {type = i64}
    %20 = maxon.binop %18, %19 {op = add}
    maxon.assign %20 {var = odd_sum} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br even_1.merge
  even_1.merge:
    %21 = maxon.literal {value = 1 : i64}
    %22 = maxon.var_ref {var = i} {type = i64}
    %23 = maxon.binop %22, %21 {op = add}
    maxon.assign %23 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %24 = maxon.var_ref {var = even_sum} {type = i64}
    %25 = maxon.var_ref {var = odd_sum} {type = i64}
    %26 = maxon.binop %24, %25 {op = add}
    %27 = maxon.var_ref {var = count} {type = i64}
    %28 = maxon.binop %26, %27 {op = add}
    %29 = maxon.literal {value = 256 : i64}
    %30 = maxon.binop %28, %29 {op = mod}
    %31 = maxon.literal {value = 0 : i64}
    %32 = maxon.binop %30, %31 {op = lt}
    %33 = maxon.literal {value = 4294967295 : i64}
    %34 = maxon.binop %30, %33 {op = gt}
    %35 = maxon.binop %32, %34 {op = or}
    maxon.cond_br %35 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    maxon.panic "panic at int-while-loop-multiple-accumulators.test:16: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_3:
    maxon.scope_end [even_sum, odd_sum, count, i]
    maxon.return %30
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %31 = arith.constant {value = 0 : i64}
    %32 = arith.cmpi lt %30, %31
    %33 = arith.constant {value = 4294967295 : i64}
    %34 = arith.cmpi gt %30, %33
    %35 = arith.ori1 %32, %34
    cf.cond_br %35 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    %36 = memref.lea_symdata __panic_msg_0
    %37 = std.ptr_to_i64 %36
    std.call_runtime @maxon_panic %37
  __range_ok_3:
    func.return %30
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=80
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-16]
    arm64.mov x2, #0
    arm64.str x2, [x29, #-24]
    arm64.mov x3, #0
    arm64.str x3, [x29, #-32]
    arm64.b main.loop_0.header
  loop_0.header:
    arm64.mov x0, #20
    arm64.ldr x1, [x29, #-32]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.loop_0
    arm64.b main.loop_0.exit
  loop_0:
    arm64.mov x0, #2
    arm64.ldr x1, [x29, #-32]
    arm64.sdiv x2, x1, x0
    arm64.msub x3, x2, x0, x1
    arm64.mov x4, #0
    arm64.cmp x3, x4
    arm64.cset x5, eq
    arm64.cmp x5, #0
    arm64.b.ne main.even_1
    arm64.b main.odd_2
  even_1:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-32]
    arm64.add x2, x0, x1
    arm64.str x2, [x29, #-8]
    arm64.mov x3, #1
    arm64.ldr x4, [x29, #-24]
    arm64.add x5, x4, x3
    arm64.str x5, [x29, #-24]
    arm64.b main.even_1.merge
  odd_2:
    arm64.ldr x0, [x29, #-16]
    arm64.ldr x1, [x29, #-32]
    arm64.add x2, x0, x1
    arm64.str x2, [x29, #-16]
    arm64.b main.even_1.merge
  even_1.merge:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-32]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-32]
    arm64.b main.loop_0.header
  loop_0.exit:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x0, x1
    arm64.ldr x3, [x29, #-24]
    arm64.add x4, x2, x3
    arm64.mov x5, #256
    arm64.sdiv x6, x4, x5
    arm64.msub x7, x6, x5, x4
    arm64.mov x8, #0
    arm64.cmp x7, x8
    arm64.cset x9, lt
    arm64.mov x10, #4294967295
    arm64.cmp x7, x10
    arm64.cset x11, gt
    arm64.orr x12, x9, x11
    arm64.cmp x12, #0
    arm64.b.ne main.__range_panic_3
    arm64.b main.__range_ok_3
  __range_panic_3:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_3:
    arm64.mov x0, x7
    arm64.epilogue stack_size=80
    arm64.ret
  }
}
```

<!-- test: int-nested-if-in-loop -->
```maxon
function main() returns ExitCode
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
```RequiredIR:x64-windows
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
    %4 = maxon.binop %3, %2 {op = le}
    maxon.cond_br %4 [then: loop_0, else: loop_0.exit]
  loop_0:
    %5 = maxon.literal {value = 5 : i64}
    %6 = maxon.var_ref {var = i} {type = i64}
    %7 = maxon.binop %6, %5 {op = le}
    maxon.cond_br %7 [then: first_0, else: second_0]
  first_0:
    %8 = maxon.var_ref {var = result} {type = i64}
    %9 = maxon.var_ref {var = i} {type = i64}
    %10 = maxon.binop %8, %9 {op = add}
    maxon.assign %10 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br first_0.merge
  second_0:
    %11 = maxon.literal {value = 2 : i64}
    %12 = maxon.var_ref {var = i} {type = i64}
    %13 = maxon.binop %12, %11 {op = mul}
    %14 = maxon.var_ref {var = result} {type = i64}
    %15 = maxon.binop %14, %13 {op = add}
    maxon.assign %15 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br first_0.merge
  first_0.merge:
    %16 = maxon.literal {value = 1 : i64}
    %17 = maxon.var_ref {var = i} {type = i64}
    %18 = maxon.binop %17, %16 {op = add}
    maxon.assign %18 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %19 = maxon.literal {value = 256 : i64}
    %20 = maxon.var_ref {var = result} {type = i64}
    %21 = maxon.binop %20, %19 {op = mod}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-nested-if-in-loop.test:13: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [result, i]
    maxon.return %21
  }
}
=== standard
module {
  func @main() -> u32 {
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
    cf.cond_br %7 [then: first_0, else: second_0]
  first_0:
    %8 = memref.load result : i64
    %9 = memref.load i : i64
    %10 = arith.addi %8, %9
    memref.store %10, result
    cf.br first_0.merge
  second_0:
    %11 = arith.constant {value = 2 : i64}
    %12 = memref.load i : i64
    %13 = arith.muli %12, %11
    %14 = memref.load result : i64
    %15 = arith.addi %14, %13
    memref.store %15, result
    cf.br first_0.merge
  first_0.merge:
    %16 = arith.constant {value = 1 : i64}
    %17 = memref.load i : i64
    %18 = arith.addi %17, %16
    memref.store %18, i
    cf.br loop_0.header
  loop_0.exit:
    %19 = arith.constant {value = 256 : i64}
    %20 = memref.load result : i64
    %21 = arith.remsi %20, %19
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi lt %21, %22
    %24 = arith.constant {value = 4294967295 : i64}
    %25 = arith.cmpi gt %21, %24
    %26 = arith.ori1 %23, %25
    cf.cond_br %26 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %27 = memref.lea_symdata __panic_msg_0
    %28 = std.ptr_to_i64 %27
    std.call_runtime @mrt_panic %28
  __range_ok_0:
    func.return %21
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.mov rcx, 1
    x64.mov [rbp-16], rcx
    x64.jmp main.loop_0.header
  loop_0.header:
    x64.mov rax, 10
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.jg main.loop_0.exit
  loop_0:
    x64.mov rax, 5
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.jg main.second_0
  first_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.add rax, rcx
    x64.mov [rbp-8], rax
    x64.jmp main.first_0.merge
  second_0:
    x64.mov rax, 2
    x64.mov rcx, [rbp-16]
    x64.imul rcx, rax
    x64.mov rdx, [rbp-8]
    x64.add rdx, rcx
    x64.mov [rbp-8], rdx
    x64.jmp main.first_0.merge
  first_0.merge:
    x64.mov rax, 1
    x64.mov rcx, [rbp-16]
    x64.add rcx, rax
    x64.mov [rbp-16], rcx
    x64.jmp main.loop_0.header
  loop_0.exit:
    x64.mov rax, 256
    x64.mov rcx, [rbp-8]
    x64.mov rbx, rax
    x64.mov rax, rcx
    x64.cqo
    x64.idiv rbx
    x64.xor eax, eax
    x64.mov ecx, 4294967295
    x64.cmp rdx, rcx
    x64.jg main.__range_panic_0
    x64.cmp rdx, rax
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %4 = maxon.binop %3, %2 {op = le}
    maxon.cond_br %4 [then: loop_0, else: loop_0.exit]
  loop_0:
    %5 = maxon.literal {value = 5 : i64}
    %6 = maxon.var_ref {var = i} {type = i64}
    %7 = maxon.binop %6, %5 {op = le}
    maxon.cond_br %7 [then: first_1, else: second_2]
  first_1:
    %8 = maxon.var_ref {var = result} {type = i64}
    %9 = maxon.var_ref {var = i} {type = i64}
    %10 = maxon.binop %8, %9 {op = add}
    maxon.assign %10 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br first_1.merge
  second_2:
    %11 = maxon.literal {value = 2 : i64}
    %12 = maxon.var_ref {var = i} {type = i64}
    %13 = maxon.binop %12, %11 {op = mul}
    %14 = maxon.var_ref {var = result} {type = i64}
    %15 = maxon.binop %14, %13 {op = add}
    maxon.assign %15 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br first_1.merge
  first_1.merge:
    %16 = maxon.literal {value = 1 : i64}
    %17 = maxon.var_ref {var = i} {type = i64}
    %18 = maxon.binop %17, %16 {op = add}
    maxon.assign %18 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %19 = maxon.literal {value = 256 : i64}
    %20 = maxon.var_ref {var = result} {type = i64}
    %21 = maxon.binop %20, %19 {op = mod}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    maxon.panic "panic at int-nested-if-in-loop.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_3:
    maxon.scope_end [result, i]
    maxon.return %21
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi lt %21, %22
    %24 = arith.constant {value = 4294967295 : i64}
    %25 = arith.cmpi gt %21, %24
    %26 = arith.ori1 %23, %25
    cf.cond_br %26 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    %27 = memref.lea_symdata __panic_msg_0
    %28 = std.ptr_to_i64 %27
    std.call_runtime @maxon_panic %28
  __range_ok_3:
    func.return %21
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.str x1, [x29, #-16]
    arm64.b main.loop_0.header
  loop_0.header:
    arm64.mov x0, #10
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, le
    arm64.cmp x2, #0
    arm64.b.ne main.loop_0
    arm64.b main.loop_0.exit
  loop_0:
    arm64.mov x0, #5
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, le
    arm64.cmp x2, #0
    arm64.b.ne main.first_1
    arm64.b main.second_2
  first_1:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x0, x1
    arm64.str x2, [x29, #-8]
    arm64.b main.first_1.merge
  second_2:
    arm64.mov x0, #2
    arm64.ldr x1, [x29, #-16]
    arm64.mul x2, x1, x0
    arm64.ldr x3, [x29, #-8]
    arm64.add x4, x3, x2
    arm64.str x4, [x29, #-8]
    arm64.b main.first_1.merge
  first_1.merge:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-16]
    arm64.b main.loop_0.header
  loop_0.exit:
    arm64.mov x0, #256
    arm64.ldr x1, [x29, #-8]
    arm64.sdiv x2, x1, x0
    arm64.msub x3, x2, x0, x1
    arm64.mov x4, #0
    arm64.cmp x3, x4
    arm64.cset x5, lt
    arm64.mov x6, #4294967295
    arm64.cmp x3, x6
    arm64.cset x7, gt
    arm64.orr x8, x5, x7
    arm64.cmp x8, #0
    arm64.b.ne main.__range_panic_3
    arm64.b main.__range_ok_3
  __range_panic_3:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_3:
    arm64.mov x0, x3
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: int-nested-loops -->
```maxon
function main() returns ExitCode
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
```RequiredIR:x64-windows
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
    %4 = maxon.binop %3, %2 {op = lt}
    maxon.cond_br %4 [then: outer_0, else: outer_0.exit]
  outer_0:
    %5 = maxon.literal {value = 0 : i64}
    maxon.assign %5 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_0.header
  inner_0.header:
    %6 = maxon.literal {value = 4 : i64}
    %7 = maxon.var_ref {var = j} {type = i64}
    %8 = maxon.binop %7, %6 {op = lt}
    maxon.cond_br %8 [then: inner_0, else: inner_0.exit]
  inner_0:
    %9 = maxon.literal {value = 1 : i64}
    %10 = maxon.var_ref {var = total} {type = i64}
    %11 = maxon.binop %10, %9 {op = add}
    maxon.assign %11 {var = total} {kind = i64} {mut = 1 : i1}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = j} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br inner_0.header
  inner_0.exit:
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = i} {type = i64}
    %17 = maxon.binop %16, %15 {op = add}
    maxon.assign %17 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [j]
    maxon.br outer_0.header
  outer_0.exit:
    %18 = maxon.var_ref {var = total} {type = i64}
    %19 = maxon.literal {value = 0 : i64}
    %20 = maxon.binop %18, %19 {op = lt}
    %21 = maxon.literal {value = 4294967295 : i64}
    %22 = maxon.binop %18, %21 {op = gt}
    %23 = maxon.binop %20, %22 {op = or}
    maxon.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-nested-loops.test:13: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [total, i]
    maxon.return %18
  }
}
=== standard
module {
  func @main() -> u32 {
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
    cf.br inner_0.header
  inner_0.header:
    %6 = arith.constant {value = 4 : i64}
    %7 = memref.load j : i64
    %8 = arith.cmpi lt %7, %6
    cf.cond_br %8 [then: inner_0, else: inner_0.exit]
  inner_0:
    %9 = arith.constant {value = 1 : i64}
    %10 = memref.load total : i64
    %11 = arith.addi %10, %9
    memref.store %11, total
    %12 = arith.constant {value = 1 : i64}
    %13 = memref.load j : i64
    %14 = arith.addi %13, %12
    memref.store %14, j
    cf.br inner_0.header
  inner_0.exit:
    %15 = arith.constant {value = 1 : i64}
    %16 = memref.load i : i64
    %17 = arith.addi %16, %15
    memref.store %17, i
    cf.br outer_0.header
  outer_0.exit:
    %18 = memref.load total : i64
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 4294967295 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %24 = memref.lea_symdata __panic_msg_0
    %25 = std.ptr_to_i64 %24
    std.call_runtime @mrt_panic %25
  __range_ok_0:
    func.return %18
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.xor ecx, ecx
    x64.mov [rbp-16], rcx
    x64.jmp main.outer_0.header
  outer_0.header:
    x64.mov rax, 5
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.jge main.outer_0.exit
  outer_0:
    x64.xor eax, eax
    x64.mov [rbp-24], rax
    x64.jmp main.inner_0.header
  inner_0.header:
    x64.mov rax, 4
    x64.mov rcx, [rbp-24]
    x64.cmp rcx, rax
    x64.jge main.inner_0.exit
  inner_0:
    x64.mov rax, 1
    x64.mov rcx, [rbp-8]
    x64.add rcx, rax
    x64.mov [rbp-8], rcx
    x64.mov rdx, 1
    x64.mov rbx, [rbp-24]
    x64.add rbx, rdx
    x64.mov [rbp-24], rbx
    x64.jmp main.inner_0.header
  inner_0.exit:
    x64.mov rax, 1
    x64.mov rcx, [rbp-16]
    x64.add rcx, rax
    x64.mov [rbp-16], rcx
    x64.jmp main.outer_0.header
  outer_0.exit:
    x64.mov rax, [rbp-8]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %4 = maxon.binop %3, %2 {op = lt}
    maxon.cond_br %4 [then: outer_0, else: outer_0.exit]
  outer_0:
    %5 = maxon.literal {value = 0 : i64}
    maxon.assign %5 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.header:
    %6 = maxon.literal {value = 4 : i64}
    %7 = maxon.var_ref {var = j} {type = i64}
    %8 = maxon.binop %7, %6 {op = lt}
    maxon.cond_br %8 [then: inner_1, else: inner_1.exit]
  inner_1:
    %9 = maxon.literal {value = 1 : i64}
    %10 = maxon.var_ref {var = total} {type = i64}
    %11 = maxon.binop %10, %9 {op = add}
    maxon.assign %11 {var = total} {kind = i64} {mut = 1 : i1}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = j} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br inner_1.header
  inner_1.exit:
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = i} {type = i64}
    %17 = maxon.binop %16, %15 {op = add}
    maxon.assign %17 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [j]
    maxon.br outer_0.header
  outer_0.exit:
    %18 = maxon.var_ref {var = total} {type = i64}
    %19 = maxon.literal {value = 0 : i64}
    %20 = maxon.binop %18, %19 {op = lt}
    %21 = maxon.literal {value = 4294967295 : i64}
    %22 = maxon.binop %18, %21 {op = gt}
    %23 = maxon.binop %20, %22 {op = or}
    maxon.cond_br %23 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at int-nested-loops.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    maxon.scope_end [total, i]
    maxon.return %18
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 4294967295 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %24 = memref.lea_symdata __panic_msg_0
    %25 = std.ptr_to_i64 %24
    std.call_runtime @maxon_panic %25
  __range_ok_2:
    func.return %18
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=80
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-16]
    arm64.b main.outer_0.header
  outer_0.header:
    arm64.mov x0, #5
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.outer_0
    arm64.b main.outer_0.exit
  outer_0:
    arm64.mov x0, #0
    arm64.str x0, [x29, #-24]
    arm64.b main.inner_1.header
  inner_1.header:
    arm64.mov x0, #4
    arm64.ldr x1, [x29, #-24]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.inner_1
    arm64.b main.inner_1.exit
  inner_1:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-8]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-8]
    arm64.mov x3, #1
    arm64.ldr x4, [x29, #-24]
    arm64.add x5, x4, x3
    arm64.str x5, [x29, #-24]
    arm64.b main.inner_1.header
  inner_1.exit:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-16]
    arm64.b main.outer_0.header
  outer_0.exit:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_2
    arm64.b main.__range_ok_2
  __range_panic_2:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_2:
    arm64.epilogue stack_size=80
    arm64.ret
  }
}
```

<!-- test: int-nested-loops-with-outer-var -->
```maxon
function main() returns ExitCode
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
```RequiredIR:x64-windows
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
    %4 = maxon.binop %3, %2 {op = le}
    maxon.cond_br %4 [then: outer_0, else: outer_0.exit]
  outer_0:
    %5 = maxon.literal {value = 1 : i64}
    maxon.assign %5 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_0.header
  inner_0.header:
    %6 = maxon.var_ref {var = j} {type = i64}
    %7 = maxon.var_ref {var = i} {type = i64}
    %8 = maxon.binop %6, %7 {op = le}
    maxon.cond_br %8 [then: inner_0, else: inner_0.exit]
  inner_0:
    %9 = maxon.literal {value = 1 : i64}
    %10 = maxon.var_ref {var = total} {type = i64}
    %11 = maxon.binop %10, %9 {op = add}
    maxon.assign %11 {var = total} {kind = i64} {mut = 1 : i1}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = j} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br inner_0.header
  inner_0.exit:
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = i} {type = i64}
    %17 = maxon.binop %16, %15 {op = add}
    maxon.assign %17 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [j]
    maxon.br outer_0.header
  outer_0.exit:
    %18 = maxon.var_ref {var = total} {type = i64}
    %19 = maxon.literal {value = 0 : i64}
    %20 = maxon.binop %18, %19 {op = lt}
    %21 = maxon.literal {value = 4294967295 : i64}
    %22 = maxon.binop %18, %21 {op = gt}
    %23 = maxon.binop %20, %22 {op = or}
    maxon.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-nested-loops-with-outer-var.test:13: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [total, i]
    maxon.return %18
  }
}
=== standard
module {
  func @main() -> u32 {
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
    cf.br inner_0.header
  inner_0.header:
    %6 = memref.load j : i64
    %7 = memref.load i : i64
    %8 = arith.cmpi le %6, %7
    cf.cond_br %8 [then: inner_0, else: inner_0.exit]
  inner_0:
    %9 = arith.constant {value = 1 : i64}
    %10 = memref.load total : i64
    %11 = arith.addi %10, %9
    memref.store %11, total
    %12 = arith.constant {value = 1 : i64}
    %13 = memref.load j : i64
    %14 = arith.addi %13, %12
    memref.store %14, j
    cf.br inner_0.header
  inner_0.exit:
    %15 = arith.constant {value = 1 : i64}
    %16 = memref.load i : i64
    %17 = arith.addi %16, %15
    memref.store %17, i
    cf.br outer_0.header
  outer_0.exit:
    %18 = memref.load total : i64
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 4294967295 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %24 = memref.lea_symdata __panic_msg_0
    %25 = std.ptr_to_i64 %24
    std.call_runtime @mrt_panic %25
  __range_ok_0:
    func.return %18
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.mov rcx, 1
    x64.mov [rbp-16], rcx
    x64.jmp main.outer_0.header
  outer_0.header:
    x64.mov rax, 5
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.jg main.outer_0.exit
  outer_0:
    x64.mov rax, 1
    x64.mov [rbp-24], rax
    x64.jmp main.inner_0.header
  inner_0.header:
    x64.mov rax, [rbp-24]
    x64.mov rcx, [rbp-16]
    x64.cmp rax, rcx
    x64.jg main.inner_0.exit
  inner_0:
    x64.mov rax, 1
    x64.mov rcx, [rbp-8]
    x64.add rcx, rax
    x64.mov [rbp-8], rcx
    x64.mov rdx, 1
    x64.mov rbx, [rbp-24]
    x64.add rbx, rdx
    x64.mov [rbp-24], rbx
    x64.jmp main.inner_0.header
  inner_0.exit:
    x64.mov rax, 1
    x64.mov rcx, [rbp-16]
    x64.add rcx, rax
    x64.mov [rbp-16], rcx
    x64.jmp main.outer_0.header
  outer_0.exit:
    x64.mov rax, [rbp-8]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %4 = maxon.binop %3, %2 {op = le}
    maxon.cond_br %4 [then: outer_0, else: outer_0.exit]
  outer_0:
    %5 = maxon.literal {value = 1 : i64}
    maxon.assign %5 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.header:
    %6 = maxon.var_ref {var = j} {type = i64}
    %7 = maxon.var_ref {var = i} {type = i64}
    %8 = maxon.binop %6, %7 {op = le}
    maxon.cond_br %8 [then: inner_1, else: inner_1.exit]
  inner_1:
    %9 = maxon.literal {value = 1 : i64}
    %10 = maxon.var_ref {var = total} {type = i64}
    %11 = maxon.binop %10, %9 {op = add}
    maxon.assign %11 {var = total} {kind = i64} {mut = 1 : i1}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = j} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br inner_1.header
  inner_1.exit:
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.var_ref {var = i} {type = i64}
    %17 = maxon.binop %16, %15 {op = add}
    maxon.assign %17 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [j]
    maxon.br outer_0.header
  outer_0.exit:
    %18 = maxon.var_ref {var = total} {type = i64}
    %19 = maxon.literal {value = 0 : i64}
    %20 = maxon.binop %18, %19 {op = lt}
    %21 = maxon.literal {value = 4294967295 : i64}
    %22 = maxon.binop %18, %21 {op = gt}
    %23 = maxon.binop %20, %22 {op = or}
    maxon.cond_br %23 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at int-nested-loops-with-outer-var.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    maxon.scope_end [total, i]
    maxon.return %18
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 4294967295 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %24 = memref.lea_symdata __panic_msg_0
    %25 = std.ptr_to_i64 %24
    std.call_runtime @maxon_panic %25
  __range_ok_2:
    func.return %18
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=80
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.str x1, [x29, #-16]
    arm64.b main.outer_0.header
  outer_0.header:
    arm64.mov x0, #5
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, le
    arm64.cmp x2, #0
    arm64.b.ne main.outer_0
    arm64.b main.outer_0.exit
  outer_0:
    arm64.mov x0, #1
    arm64.str x0, [x29, #-24]
    arm64.b main.inner_1.header
  inner_1.header:
    arm64.ldr x0, [x29, #-24]
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x0, x1
    arm64.cset x2, le
    arm64.cmp x2, #0
    arm64.b.ne main.inner_1
    arm64.b main.inner_1.exit
  inner_1:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-8]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-8]
    arm64.mov x3, #1
    arm64.ldr x4, [x29, #-24]
    arm64.add x5, x4, x3
    arm64.str x5, [x29, #-24]
    arm64.b main.inner_1.header
  inner_1.exit:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-16]
    arm64.b main.outer_0.header
  outer_0.exit:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_2
    arm64.b main.__range_ok_2
  __range_panic_2:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_2:
    arm64.epilogue stack_size=80
    arm64.ret
  }
}
```

<!-- test: int-loop-with-function-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
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
```RequiredIR:x64-windows
=== maxon
module {
  func @double(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {optimalType = i64}
    maxon.scope_end [x]
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
    %7 = maxon.binop %6, %5 {op = lt}
    maxon.cond_br %7 [then: loop_0, else: loop_0.exit]
  loop_0:
    %8 = maxon.var_ref {var = i} {type = i64}
    %9 = maxon.call @double %8
    %10 = maxon.var_ref {var = sum} {type = i64}
    %11 = maxon.binop %10, %9 {op = add}
    maxon.assign %11 {var = sum} {kind = i64} {mut = 1 : i1}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = i} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %15 = maxon.var_ref {var = sum} {type = i64}
    %16 = maxon.literal {value = 0 : i64}
    %17 = maxon.binop %15, %16 {op = lt}
    %18 = maxon.literal {value = 4294967295 : i64}
    %19 = maxon.binop %15, %18 {op = gt}
    %20 = maxon.binop %17, %19 {op = or}
    maxon.cond_br %20 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-loop-with-function-call.test:16: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [sum, i]
    maxon.return %15
  }
}
=== standard
module {
  func @double(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.muli %0, %1
    func.return %2
  }
  func @main() -> u32 {
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
    %9 = func.call @double %8
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
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi lt %15, %16
    %18 = arith.constant {value = 4294967295 : i64}
    %19 = arith.cmpi gt %15, %18
    %20 = arith.ori1 %17, %19
    cf.cond_br %20 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %21 = memref.lea_symdata __panic_msg_0
    %22 = std.ptr_to_i64 %21
    std.call_runtime @mrt_panic %22
  __range_ok_0:
    func.return %15
  }
}
=== x86
module {
  func @double(x: i64) -> i64 {
  entry:
    x64.mov rax, 2
    x64.imul rcx, rax
    x64.mov rax, rcx
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.xor ecx, ecx
    x64.mov [rbp-16], rcx
    x64.jmp main.loop_0.header
  loop_0.header:
    x64.mov rax, 5
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.jge main.loop_0.exit
  loop_0:
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rbp-16]
    x64.call double
    x64.mov rcx, [rbp-8]
    x64.add rcx, rax
    x64.mov [rbp-8], rcx
    x64.mov rdx, 1
    x64.mov rbx, [rbp-16]
    x64.add rbx, rdx
    x64.mov [rbp-16], rbx
    x64.jmp main.loop_0.header
  loop_0.exit:
    x64.mov rax, [rbp-8]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.double(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {optimalType = i64}
    maxon.scope_end [x]
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
    %7 = maxon.binop %6, %5 {op = lt}
    maxon.cond_br %7 [then: loop_0, else: loop_0.exit]
  loop_0:
    %8 = maxon.var_ref {var = i} {type = i64}
    %9 = maxon.call @register-allocator.double %8
    %10 = maxon.var_ref {var = sum} {type = i64}
    %11 = maxon.binop %10, %9 {op = add}
    maxon.assign %11 {var = sum} {kind = i64} {mut = 1 : i1}
    %12 = maxon.literal {value = 1 : i64}
    %13 = maxon.var_ref {var = i} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %15 = maxon.var_ref {var = sum} {type = i64}
    %16 = maxon.literal {value = 0 : i64}
    %17 = maxon.binop %15, %16 {op = lt}
    %18 = maxon.literal {value = 4294967295 : i64}
    %19 = maxon.binop %15, %18 {op = gt}
    %20 = maxon.binop %17, %19 {op = or}
    maxon.cond_br %20 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at int-loop-with-function-call.test:16: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [sum, i]
    maxon.return %15
  }
}
=== standard
module {
  func @register-allocator.double(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.muli %0, %1
    func.return %2
  }
  func @main() -> u32 {
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
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi lt %15, %16
    %18 = arith.constant {value = 4294967295 : i64}
    %19 = arith.cmpi gt %15, %18
    %20 = arith.ori1 %17, %19
    cf.cond_br %20 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %21 = memref.lea_symdata __panic_msg_0
    %22 = std.ptr_to_i64 %21
    std.call_runtime @maxon_panic %22
  __range_ok_1:
    func.return %15
  }
}
=== arm64
module {
  func @register-allocator.double(x: i64) -> i64 {
  entry:
    arm64.mov x1, #2
    arm64.mul x2, x0, x1
    arm64.mov x0, x2
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-16]
    arm64.b main.loop_0.header
  loop_0.header:
    arm64.mov x0, #5
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.loop_0
    arm64.b main.loop_0.exit
  loop_0:
    arm64.ldr x0, [x29, #-16]
    arm64.bl register-allocator.double
    arm64.ldr x1, [x29, #-8]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-8]
    arm64.mov x3, #1
    arm64.ldr x4, [x29, #-16]
    arm64.add x5, x4, x3
    arm64.str x5, [x29, #-16]
    arm64.b main.loop_0.header
  loop_0.exit:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

### Level 6: Advanced Scenarios

<!-- test: int-nested-expressions-deep -->
```maxon
function main() returns ExitCode
	return ((((1 + 2) * 3) + 4) * 2) + 6
end 'main'
```
```exitcode
32
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = add}
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.binop %2, %3 {op = mul}
    %5 = maxon.literal {value = 4 : i64}
    %6 = maxon.binop %4, %5 {op = add}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = mul}
    %9 = maxon.literal {value = 6 : i64}
    %10 = maxon.binop %8, %9 {op = add}
    %11 = maxon.literal {value = 0 : i64}
    %12 = maxon.binop %10, %11 {op = lt}
    %13 = maxon.literal {value = 4294967295 : i64}
    %14 = maxon.binop %10, %13 {op = gt}
    %15 = maxon.binop %12, %14 {op = or}
    maxon.cond_br %15 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-nested-expressions-deep.test:3: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %10
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.cmpi lt %10, %11
    %13 = arith.constant {value = 4294967295 : i64}
    %14 = arith.cmpi gt %10, %13
    %15 = arith.ori1 %12, %14
    cf.cond_br %15 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %16 = memref.lea_symdata __panic_msg_0
    %17 = std.ptr_to_i64 %16
    std.call_runtime @mrt_panic %17
  __range_ok_0:
    func.return %10
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 1
    x64.mov rcx, 2
    x64.add rax, rcx
    x64.mov rdx, 3
    x64.imul rax, rdx
    x64.mov rbx, 4
    x64.add rax, rbx
    x64.mov rsi, 2
    x64.imul rax, rsi
    x64.mov rdi, 6
    x64.add rax, rdi
    x64.xor r8d, r8d
    x64.mov r9, 4294967295
    x64.cmp rax, r9
    x64.jg main.__range_panic_0
    x64.cmp rax, r8
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = add}
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.binop %2, %3 {op = mul}
    %5 = maxon.literal {value = 4 : i64}
    %6 = maxon.binop %4, %5 {op = add}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = mul}
    %9 = maxon.literal {value = 6 : i64}
    %10 = maxon.binop %8, %9 {op = add}
    %11 = maxon.literal {value = 0 : i64}
    %12 = maxon.binop %10, %11 {op = lt}
    %13 = maxon.literal {value = 4294967295 : i64}
    %14 = maxon.binop %10, %13 {op = gt}
    %15 = maxon.binop %12, %14 {op = or}
    maxon.cond_br %15 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-nested-expressions-deep.test:3: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %10
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.cmpi lt %10, %11
    %13 = arith.constant {value = 4294967295 : i64}
    %14 = arith.cmpi gt %10, %13
    %15 = arith.ori1 %12, %14
    cf.cond_br %15 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %16 = memref.lea_symdata __panic_msg_0
    %17 = std.ptr_to_i64 %16
    std.call_runtime @maxon_panic %17
  __range_ok_0:
    func.return %10
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #1
    arm64.mov x1, #2
    arm64.add x2, x0, x1
    arm64.mov x3, #3
    arm64.mul x4, x2, x3
    arm64.mov x5, #4
    arm64.add x6, x4, x5
    arm64.mov x7, #2
    arm64.mul x8, x6, x7
    arm64.mov x9, #6
    arm64.add x10, x8, x9
    arm64.mov x11, #0
    arm64.cmp x10, x11
    arm64.cset x12, lt
    arm64.mov x13, #4294967295
    arm64.cmp x10, x13
    arm64.cset x14, gt
    arm64.orr x15, x12, x14
    arm64.cmp x15, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x10
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-expression-both-sides-complex -->
```maxon
function main() returns ExitCode
	let a = 3
	let b = 5
	let c = 7
	let d = 2
	return (a + b) * (c - d)
end 'main'
```
```exitcode
40
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 5 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.literal {value = 7 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 2 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1}
    %4 = maxon.binop %0, %1 {op = add}
    %5 = maxon.binop %2, %3 {op = sub}
    %6 = maxon.binop %4, %5 {op = mul}
    %7 = maxon.literal {value = 0 : i64}
    %8 = maxon.binop %6, %7 {op = lt}
    %9 = maxon.literal {value = 4294967295 : i64}
    %10 = maxon.binop %6, %9 {op = gt}
    %11 = maxon.binop %8, %10 {op = or}
    maxon.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-expression-both-sides-complex.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, c, d]
    maxon.return %6
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 3 : i64}
    %1 = arith.constant {value = 5 : i64}
    %2 = arith.constant {value = 7 : i64}
    %3 = arith.constant {value = 2 : i64}
    %4 = arith.addi %0, %1
    %5 = arith.subi %2, %3
    %6 = arith.muli %4, %5
    %7 = arith.constant {value = 0 : i64}
    %8 = arith.cmpi lt %6, %7
    %9 = arith.constant {value = 4294967295 : i64}
    %10 = arith.cmpi gt %6, %9
    %11 = arith.ori1 %8, %10
    cf.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %12 = memref.lea_symdata __panic_msg_0
    %13 = std.ptr_to_i64 %12
    std.call_runtime @mrt_panic %13
  __range_ok_0:
    func.return %6
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 3
    x64.mov rcx, 5
    x64.mov rdx, 7
    x64.mov rbx, 2
    x64.add rax, rcx
    x64.sub rdx, rbx
    x64.imul rax, rdx
    x64.xor esi, esi
    x64.mov edi, 4294967295
    x64.cmp rax, rdi
    x64.jg main.__range_panic_0
    x64.cmp rax, rsi
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %4 = maxon.binop %0, %1 {op = add}
    %5 = maxon.binop %2, %3 {op = sub}
    %6 = maxon.binop %4, %5 {op = mul}
    %7 = maxon.literal {value = 0 : i64}
    %8 = maxon.binop %6, %7 {op = lt}
    %9 = maxon.literal {value = 4294967295 : i64}
    %10 = maxon.binop %6, %9 {op = gt}
    %11 = maxon.binop %8, %10 {op = or}
    maxon.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-expression-both-sides-complex.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b, c, d]
    maxon.return %6
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 3 : i64}
    %1 = arith.constant {value = 5 : i64}
    %2 = arith.constant {value = 7 : i64}
    %3 = arith.constant {value = 2 : i64}
    %4 = arith.addi %0, %1
    %5 = arith.subi %2, %3
    %6 = arith.muli %4, %5
    %7 = arith.constant {value = 0 : i64}
    %8 = arith.cmpi lt %6, %7
    %9 = arith.constant {value = 4294967295 : i64}
    %10 = arith.cmpi gt %6, %9
    %11 = arith.ori1 %8, %10
    cf.cond_br %11 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %12 = memref.lea_symdata __panic_msg_0
    %13 = std.ptr_to_i64 %12
    std.call_runtime @maxon_panic %13
  __range_ok_0:
    func.return %6
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #3
    arm64.mov x1, #5
    arm64.mov x2, #7
    arm64.mov x3, #2
    arm64.add x4, x0, x1
    arm64.sub x5, x2, x3
    arm64.mul x6, x4, x5
    arm64.mov x7, #0
    arm64.cmp x6, x7
    arm64.cset x8, lt
    arm64.mov x9, #4294967295
    arm64.cmp x6, x9
    arm64.cset x10, gt
    arm64.orr x11, x8, x10
    arm64.cmp x11, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x6
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-many-params-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sum5(a Integer, b Integer, c Integer, d Integer, e Integer) returns Integer
	return a + b + c + d + e
end 'sum5'

function main() returns ExitCode
	return sum5(5, b: 10, c: 8, d: 12, e: 7)
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.param {index = 2 : i32} {name = c} {type = i64}
    %3 = maxon.param {index = 3 : i32} {name = d} {type = i64}
    %4 = maxon.param {index = 4 : i32} {name = e} {type = i64}
    %5 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    %6 = maxon.binop %5, %2 {op = add} {optimalType = i64}
    %7 = maxon.binop %6, %3 {op = add} {optimalType = i64}
    %8 = maxon.binop %7, %4 {op = add} {optimalType = i64}
    maxon.scope_end [a, b, c, d, e]
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
    %15 = maxon.literal {value = 0 : i64}
    %16 = maxon.binop %14, %15 {op = lt}
    %17 = maxon.literal {value = 4294967295 : i64}
    %18 = maxon.binop %14, %17 {op = gt}
    %19 = maxon.binop %16, %18 {op = or}
    maxon.cond_br %19 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-many-params-function.test:10: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %14
  }
}
=== standard
module {
  func @sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = func.param c : StdI64
    %3 = func.param d : StdI64
    %4 = func.param e : StdI64
    %5 = arith.addi %0, %1
    %6 = arith.addi %5, %2
    %7 = arith.addi %6, %3
    %8 = arith.addi %7, %4
    func.return %8
  }
  func @main() -> u32 {
  entry:
    %9 = arith.constant {value = 5 : i64}
    %10 = arith.constant {value = 10 : i64}
    %11 = arith.constant {value = 8 : i64}
    %12 = arith.constant {value = 12 : i64}
    %13 = arith.constant {value = 7 : i64}
    %14 = func.call @sum5 %9, %10, %11, %12, %13
    %15 = arith.constant {value = 0 : i64}
    %16 = arith.cmpi lt %14, %15
    %17 = arith.constant {value = 4294967295 : i64}
    %18 = arith.cmpi gt %14, %17
    %19 = arith.ori1 %16, %18
    cf.cond_br %19 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %20 = memref.lea_symdata __panic_msg_0
    %21 = std.ptr_to_i64 %20
    std.call_runtime @mrt_panic %21
  __range_ok_0:
    func.return %14
  }
}
=== x86
module {
  func @sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    x64.add rcx, rdx
    x64.add rcx, r8
    x64.add rcx, r9
    x64.lea rax, [rcx + rsi]
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 5
    x64.mov rdx, 10
    x64.mov r8, 8
    x64.mov r9, 12
    x64.mov rsi, 7
    x64.call sum5
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.param {index = 2 : i32} {name = c} {type = i64}
    %3 = maxon.param {index = 3 : i32} {name = d} {type = i64}
    %4 = maxon.param {index = 4 : i32} {name = e} {type = i64}
    %5 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    %6 = maxon.binop %5, %2 {op = add} {optimalType = i64}
    %7 = maxon.binop %6, %3 {op = add} {optimalType = i64}
    %8 = maxon.binop %7, %4 {op = add} {optimalType = i64}
    maxon.scope_end [a, b, c, d, e]
    maxon.return %8
  }
  func @main() -> i64 {
  entry:
    %9 = maxon.literal {value = 5 : i64}
    %10 = maxon.literal {value = 10 : i64}
    %11 = maxon.literal {value = 8 : i64}
    %12 = maxon.literal {value = 12 : i64}
    %13 = maxon.literal {value = 7 : i64}
    %14 = maxon.call @register-allocator.sum5 %9, %10, %11, %12, %13
    %15 = maxon.literal {value = 0 : i64}
    %16 = maxon.binop %14, %15 {op = lt}
    %17 = maxon.literal {value = 4294967295 : i64}
    %18 = maxon.binop %14, %17 {op = gt}
    %19 = maxon.binop %16, %18 {op = or}
    maxon.cond_br %19 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-many-params-function.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %14
  }
}
=== standard
module {
  func @register-allocator.sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = func.param c : StdI64
    %3 = func.param d : StdI64
    %4 = func.param e : StdI64
    %5 = arith.addi %0, %1
    %6 = arith.addi %5, %2
    %7 = arith.addi %6, %3
    %8 = arith.addi %7, %4
    func.return %8
  }
  func @main() -> u32 {
  entry:
    %9 = arith.constant {value = 5 : i64}
    %10 = arith.constant {value = 10 : i64}
    %11 = arith.constant {value = 8 : i64}
    %12 = arith.constant {value = 12 : i64}
    %13 = arith.constant {value = 7 : i64}
    %14 = func.call @register-allocator.sum5 %9, %10, %11, %12, %13
    %15 = arith.constant {value = 0 : i64}
    %16 = arith.cmpi lt %14, %15
    %17 = arith.constant {value = 4294967295 : i64}
    %18 = arith.cmpi gt %14, %17
    %19 = arith.ori1 %16, %18
    cf.cond_br %19 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %20 = memref.lea_symdata __panic_msg_0
    %21 = std.ptr_to_i64 %20
    std.call_runtime @maxon_panic %21
  __range_ok_0:
    func.return %14
  }
}
=== arm64
module {
  func @register-allocator.sum5(a: i64, b: i64, c: i64, d: i64, e: i64) -> i64 {
  entry:
    arm64.add x5, x0, x1
    arm64.add x0, x5, x2
    arm64.add x1, x0, x3
    arm64.add x2, x1, x4
    arm64.mov x0, x2
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #5
    arm64.mov x1, #10
    arm64.mov x2, #8
    arm64.mov x3, #12
    arm64.mov x4, #7
    arm64.bl register-allocator.sum5
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #4294967295
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-nine-params-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sum9(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer, h Integer, i Integer) returns Integer
	return a + b + c + d + e + f + g + h + i
end 'sum9'

function main() returns ExitCode
	return sum9(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7, h: 8, i: 9)
end 'main'
```
```exitcode
45
```
```RequiredIR:x64-windows
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
    %9 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    %10 = maxon.binop %9, %2 {op = add} {optimalType = i64}
    %11 = maxon.binop %10, %3 {op = add} {optimalType = i64}
    %12 = maxon.binop %11, %4 {op = add} {optimalType = i64}
    %13 = maxon.binop %12, %5 {op = add} {optimalType = i64}
    %14 = maxon.binop %13, %6 {op = add} {optimalType = i64}
    %15 = maxon.binop %14, %7 {op = add} {optimalType = i64}
    %16 = maxon.binop %15, %8 {op = add} {optimalType = i64}
    maxon.scope_end [a, b, c, d, e, f, g, h, i]
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
    %27 = maxon.literal {value = 0 : i64}
    %28 = maxon.binop %26, %27 {op = lt}
    %29 = maxon.literal {value = 4294967295 : i64}
    %30 = maxon.binop %26, %29 {op = gt}
    %31 = maxon.binop %28, %30 {op = or}
    maxon.cond_br %31 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-nine-params-function.test:10: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %26
  }
}
=== standard
module {
  func @sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = func.param c : StdI64
    %3 = func.param d : StdI64
    %4 = func.param e : StdI64
    %5 = func.param f : StdI64
    %6 = func.param g : StdI64
    %7 = func.param h : StdI64
    %8 = func.param i : StdI64
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
  func @main() -> u32 {
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
    %26 = func.call @sum9 %17, %18, %19, %20, %21, %22, %23, %24, %25
    %27 = arith.constant {value = 0 : i64}
    %28 = arith.cmpi lt %26, %27
    %29 = arith.constant {value = 4294967295 : i64}
    %30 = arith.cmpi gt %26, %29
    %31 = arith.ori1 %28, %30
    cf.cond_br %31 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %32 = memref.lea_symdata __panic_msg_0
    %33 = std.ptr_to_i64 %32
    std.call_runtime @mrt_panic %33
  __range_ok_0:
    func.return %26
  }
}
=== x86
module {
  func @sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rcx, [rbp+16]
    x64.mov [rbp-16], rcx
    x64.mov rcx, [rbp-8]
    x64.add rcx, rdx
    x64.add rcx, r8
    x64.add rcx, r9
    x64.add rcx, rsi
    x64.add rcx, rdi
    x64.add rcx, rax
    x64.add rcx, rbx
    x64.mov rax, [rbp-16]
    x64.lea rax, [rcx + rax]
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.sub rsp, 16
    x64.mov rax, 9
    x64.mov [rsp+0], rax
    x64.mov rcx, 1
    x64.mov rdx, 2
    x64.mov r8, 3
    x64.mov r9, 4
    x64.mov rsi, 5
    x64.mov rdi, 6
    x64.mov rax, 7
    x64.mov rbx, 8
    x64.call sum9
    x64.add rsp, 16
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %9 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    %10 = maxon.binop %9, %2 {op = add} {optimalType = i64}
    %11 = maxon.binop %10, %3 {op = add} {optimalType = i64}
    %12 = maxon.binop %11, %4 {op = add} {optimalType = i64}
    %13 = maxon.binop %12, %5 {op = add} {optimalType = i64}
    %14 = maxon.binop %13, %6 {op = add} {optimalType = i64}
    %15 = maxon.binop %14, %7 {op = add} {optimalType = i64}
    %16 = maxon.binop %15, %8 {op = add} {optimalType = i64}
    maxon.scope_end [a, b, c, d, e, f, g, h, i]
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
    %26 = maxon.call @register-allocator.sum9 %17, %18, %19, %20, %21, %22, %23, %24, %25
    %27 = maxon.literal {value = 0 : i64}
    %28 = maxon.binop %26, %27 {op = lt}
    %29 = maxon.literal {value = 4294967295 : i64}
    %30 = maxon.binop %26, %29 {op = gt}
    %31 = maxon.binop %28, %30 {op = or}
    maxon.cond_br %31 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-nine-params-function.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %26
  }
}
=== standard
module {
  func @register-allocator.sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = func.param c : StdI64
    %3 = func.param d : StdI64
    %4 = func.param e : StdI64
    %5 = func.param f : StdI64
    %6 = func.param g : StdI64
    %7 = func.param h : StdI64
    %8 = func.param i : StdI64
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
  func @main() -> u32 {
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
    %27 = arith.constant {value = 0 : i64}
    %28 = arith.cmpi lt %26, %27
    %29 = arith.constant {value = 4294967295 : i64}
    %30 = arith.cmpi gt %26, %29
    %31 = arith.ori1 %28, %30
    cf.cond_br %31 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %32 = memref.lea_symdata __panic_msg_0
    %33 = std.ptr_to_i64 %32
    std.call_runtime @maxon_panic %33
  __range_ok_0:
    func.return %26
  }
}
=== arm64
module {
  func @register-allocator.sum9(a: i64, b: i64, c: i64, d: i64, e: i64, f: i64, g: i64, h: i64, i: i64) -> i64 {
  entry:
    arm64.prologue stack_size=16
    arm64.ldr x8, [x29, #16]
    arm64.add x9, x0, x1
    arm64.add x0, x9, x2
    arm64.add x1, x0, x3
    arm64.add x2, x1, x4
    arm64.add x3, x2, x5
    arm64.add x4, x3, x6
    arm64.add x5, x4, x7
    arm64.add x6, x5, x8
    arm64.mov x0, x6
    arm64.epilogue stack_size=16
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.sub sp, sp, #16
    arm64.mov x0, #9
    arm64.str x0, [sp, #0]
    arm64.mov x0, #1
    arm64.mov x1, #2
    arm64.mov x2, #3
    arm64.mov x3, #4
    arm64.mov x4, #5
    arm64.mov x5, #6
    arm64.mov x6, #7
    arm64.mov x7, #8
    arm64.bl register-allocator.sum9
    arm64.add sp, sp, #16
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-recursive-factorial -->
```maxon

typealias Integer = int(i64.min to i64.max)

function factorial(n Integer) returns Integer
	if n <= 1 'base'
		return 1
	end 'base'
	return n * factorial(n - 1)
end 'factorial'

function main() returns ExitCode
	return factorial(5) mod 256
end 'main'
```
```exitcode
120
```
```RequiredIR:x64-windows
=== maxon
module {
  func @factorial(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 1 : i64}
    %2 = maxon.binop %0, %1 {op = le}
    maxon.cond_br %2 [then: base_0, else: base_0.after]
  base_0:
    %3 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n]
    maxon.return %3
  base_0.after:
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.var_ref {var = n} {type = i64}
    %6 = maxon.binop %5, %4 {op = sub}
    %7 = maxon.call @factorial %6
    %8 = maxon.var_ref {var = n} {type = i64}
    %9 = maxon.binop %8, %7 {op = mul}
    maxon.scope_end [n]
    maxon.return %9
  }
  func @main() -> i64 {
  entry:
    %10 = maxon.literal {value = 5 : i64}
    %11 = maxon.call @factorial %10
    %12 = maxon.literal {value = 256 : i64}
    %13 = maxon.binop %11, %12 {op = mod}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %13, %14 {op = lt}
    %16 = maxon.literal {value = 4294967295 : i64}
    %17 = maxon.binop %13, %16 {op = gt}
    %18 = maxon.binop %15, %17 {op = or}
    maxon.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-recursive-factorial.test:13: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %13
  }
}
=== standard
module {
  func @factorial(n: i64) -> i64 {
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
    %7 = func.call @factorial %6
    %8 = memref.load n : i64
    %9 = arith.muli %8, %7
    func.return %9
  }
  func @main() -> u32 {
  entry:
    %10 = arith.constant {value = 5 : i64}
    %11 = func.call @factorial %10
    %12 = arith.constant {value = 256 : i64}
    %13 = arith.remsi %11, %12
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @mrt_panic %20
  __range_ok_0:
    func.return %13
  }
}
=== x86
module {
  func @factorial(n: i64) -> i64 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, 1
    x64.cmp rcx, rax
    x64.jg factorial.base_0.after
  base_0:
    x64.mov rax, 1
    x64.epilogue
    x64.ret
  base_0.after:
    x64.mov rax, 1
    x64.mov rcx, [rbp-8]
    x64.sub rcx, rax
    x64.call factorial
    x64.mov rdx, [rbp-8]
    x64.imul rdx, rax
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 5
    x64.call factorial
    x64.mov rcx, 256
    x64.mov [rbp-8], rax
    x64.cqo
    x64.idiv rcx
    x64.xor eax, eax
    x64.mov ecx, 4294967295
    x64.cmp rdx, rcx
    x64.jg main.__range_panic_0
    x64.cmp rdx, rax
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.factorial(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 1 : i64}
    %2 = maxon.binop %0, %1 {op = le} {optimalType = i64}
    maxon.cond_br %2 [then: base_0, else: base_0.after]
  base_0:
    %3 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n]
    maxon.return %3
  base_0.after:
    %4 = maxon.literal {value = 1 : i64}
    %5 = maxon.var_ref {var = n} {type = i64}
    %6 = maxon.binop %5, %4 {op = sub}
    %7 = maxon.call @register-allocator.factorial %6
    %8 = maxon.var_ref {var = n} {type = i64}
    %9 = maxon.binop %8, %7 {op = mul}
    maxon.scope_end [n]
    maxon.return %9
  }
  func @main() -> i64 {
  entry:
    %10 = maxon.literal {value = 5 : i64}
    %11 = maxon.call @register-allocator.factorial %10
    %12 = maxon.literal {value = 256 : i64}
    %13 = maxon.binop %11, %12 {op = mod}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %13, %14 {op = lt}
    %16 = maxon.literal {value = 4294967295 : i64}
    %17 = maxon.binop %13, %16 {op = gt}
    %18 = maxon.binop %15, %17 {op = or}
    maxon.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-recursive-factorial.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end []
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
  func @main() -> u32 {
  entry:
    %10 = arith.constant {value = 5 : i64}
    %11 = func.call @register-allocator.factorial %10
    %12 = arith.constant {value = 256 : i64}
    %13 = arith.remsi %11, %12
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @maxon_panic %20
  __range_ok_0:
    func.return %13
  }
}
=== arm64
module {
  func @register-allocator.factorial(n: i64) -> i64 {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.cmp x0, x1
    arm64.cset x2, le
    arm64.cmp x2, #0
    arm64.b.ne register-allocator.factorial.base_0
    arm64.b register-allocator.factorial.base_0.after
  base_0:
    arm64.mov x0, #1
    arm64.epilogue stack_size=48
    arm64.ret
  base_0.after:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-8]
    arm64.sub x2, x1, x0
    arm64.mov x0, x2
    arm64.bl register-allocator.factorial
    arm64.ldr x3, [x29, #-8]
    arm64.mul x4, x3, x0
    arm64.mov x0, x4
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #5
    arm64.bl register-allocator.factorial
    arm64.mov x1, #256
    arm64.sdiv x2, x0, x1
    arm64.msub x3, x2, x1, x0
    arm64.mov x0, #0
    arm64.cmp x3, x0
    arm64.cset x1, lt
    arm64.mov x2, #4294967295
    arm64.cmp x3, x2
    arm64.cset x0, gt
    arm64.orr x2, x1, x0
    arm64.cmp x2, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x3
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-loop-pressure-with-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function identity(x Integer) returns Integer
	return x
end 'identity'

function main() returns ExitCode
	var a = 1
	let b = 2
	var c = 3
	let d = 4
	var e = 5
	let f = 6
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
```RequiredIR:x64-windows
=== maxon
module {
  func @identity(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    maxon.scope_end [x]
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 2 : i64}
    maxon.assign %2 {var = b} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    maxon.assign %3 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 4 : i64}
    maxon.assign %4 {var = d} {kind = i64} {decl = 1 : i1}
    %5 = maxon.literal {value = 5 : i64}
    maxon.assign %5 {var = e} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 6 : i64}
    maxon.assign %6 {var = f} {kind = i64} {decl = 1 : i1}
    %7 = maxon.literal {value = 0 : i64}
    maxon.assign %7 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %8 = maxon.literal {value = 3 : i64}
    %9 = maxon.var_ref {var = i} {type = i64}
    %10 = maxon.binop %9, %8 {op = lt}
    maxon.cond_br %10 [then: loop_0, else: loop_0.exit]
  loop_0:
    %11 = maxon.var_ref {var = b} {type = i64}
    %12 = maxon.call @identity %11
    %13 = maxon.var_ref {var = a} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = a} {kind = i64} {mut = 1 : i1}
    %15 = maxon.var_ref {var = d} {type = i64}
    %16 = maxon.call @identity %15
    %17 = maxon.var_ref {var = c} {type = i64}
    %18 = maxon.binop %17, %16 {op = add}
    maxon.assign %18 {var = c} {kind = i64} {mut = 1 : i1}
    %19 = maxon.var_ref {var = f} {type = i64}
    %20 = maxon.call @identity %19
    %21 = maxon.var_ref {var = e} {type = i64}
    %22 = maxon.binop %21, %20 {op = add}
    maxon.assign %22 {var = e} {kind = i64} {mut = 1 : i1}
    %23 = maxon.literal {value = 1 : i64}
    %24 = maxon.var_ref {var = i} {type = i64}
    %25 = maxon.binop %24, %23 {op = add}
    maxon.assign %25 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %26 = maxon.var_ref {var = a} {type = i64}
    %27 = maxon.var_ref {var = c} {type = i64}
    %28 = maxon.binop %26, %27 {op = add}
    %29 = maxon.var_ref {var = d} {type = i64}
    %30 = maxon.binop %28, %29 {op = add}
    %31 = maxon.var_ref {var = e} {type = i64}
    %32 = maxon.binop %30, %31 {op = add}
    %33 = maxon.var_ref {var = f} {type = i64}
    %34 = maxon.binop %32, %33 {op = add}
    %35 = maxon.literal {value = 256 : i64}
    %36 = maxon.binop %34, %35 {op = mod}
    %37 = maxon.literal {value = 0 : i64}
    %38 = maxon.binop %36, %37 {op = lt}
    %39 = maxon.literal {value = 4294967295 : i64}
    %40 = maxon.binop %36, %39 {op = gt}
    %41 = maxon.binop %38, %40 {op = or}
    maxon.cond_br %41 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-loop-pressure-with-call.test:23: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, c, d, e, f, i]
    maxon.return %36
  }
}
=== standard
module {
  func @identity(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    func.return %0
  }
  func @main() -> u32 {
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
    %12 = func.call @identity %11
    %13 = memref.load a : i64
    %14 = arith.addi %13, %12
    memref.store %14, a
    %15 = memref.load d : i64
    %16 = func.call @identity %15
    %17 = memref.load c : i64
    %18 = arith.addi %17, %16
    memref.store %18, c
    %19 = memref.load f : i64
    %20 = func.call @identity %19
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
    %37 = arith.constant {value = 0 : i64}
    %38 = arith.cmpi lt %36, %37
    %39 = arith.constant {value = 4294967295 : i64}
    %40 = arith.cmpi gt %36, %39
    %41 = arith.ori1 %38, %40
    cf.cond_br %41 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %42 = memref.lea_symdata __panic_msg_0
    %43 = std.ptr_to_i64 %42
    std.call_runtime @mrt_panic %43
  __range_ok_0:
    func.return %36
  }
}
=== x86
module {
  func @identity(x: i64) -> i64 {
  entry:
    x64.mov rax, rcx
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=64
    x64.mov rax, 1
    x64.mov [rbp-8], rax
    x64.mov rcx, 2
    x64.mov [rbp-16], rcx
    x64.mov rdx, 3
    x64.mov [rbp-24], rdx
    x64.mov rbx, 4
    x64.mov [rbp-32], rbx
    x64.mov rsi, 5
    x64.mov [rbp-40], rsi
    x64.mov rdi, 6
    x64.mov [rbp-48], rdi
    x64.xor r8d, r8d
    x64.mov [rbp-56], r8
    x64.jmp main.loop_0.header
  loop_0.header:
    x64.mov rax, 3
    x64.mov rcx, [rbp-56]
    x64.cmp rcx, rax
    x64.jge main.loop_0.exit
  loop_0:
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rbp-16]
    x64.call identity
    x64.mov rcx, [rbp-8]
    x64.add rcx, rax
    x64.mov [rbp-8], rcx
    x64.mov rdx, [rbp-32]
    x64.mov rcx, [rbp-32]
    x64.call identity
    x64.mov rbx, [rbp-24]
    x64.add rbx, rax
    x64.mov [rbp-24], rbx
    x64.mov rsi, [rbp-48]
    x64.mov rcx, [rbp-48]
    x64.call identity
    x64.mov rdi, [rbp-40]
    x64.add rdi, rax
    x64.mov [rbp-40], rdi
    x64.mov r8, 1
    x64.mov r9, [rbp-56]
    x64.add r9, r8
    x64.mov [rbp-56], r9
    x64.jmp main.loop_0.header
  loop_0.exit:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-24]
    x64.add rax, rcx
    x64.mov rdx, [rbp-32]
    x64.add rax, rdx
    x64.mov rbx, [rbp-40]
    x64.add rax, rbx
    x64.mov rsi, [rbp-48]
    x64.add rax, rsi
    x64.mov rdi, 256
    x64.mov [rbp-64], rax
    x64.cqo
    x64.idiv rdi
    x64.xor r8d, r8d
    x64.mov r9, 4294967295
    x64.cmp rdx, r9
    x64.jg main.__range_panic_0
    x64.cmp rdx, r8
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.identity(x: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    maxon.scope_end [x]
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
    %10 = maxon.binop %9, %8 {op = lt}
    maxon.cond_br %10 [then: loop_0, else: loop_0.exit]
  loop_0:
    %11 = maxon.var_ref {var = b} {type = i64}
    %12 = maxon.call @register-allocator.identity %11
    %13 = maxon.var_ref {var = a} {type = i64}
    %14 = maxon.binop %13, %12 {op = add}
    maxon.assign %14 {var = a} {kind = i64} {mut = 1 : i1}
    %15 = maxon.var_ref {var = d} {type = i64}
    %16 = maxon.call @register-allocator.identity %15
    %17 = maxon.var_ref {var = c} {type = i64}
    %18 = maxon.binop %17, %16 {op = add}
    maxon.assign %18 {var = c} {kind = i64} {mut = 1 : i1}
    %19 = maxon.var_ref {var = f} {type = i64}
    %20 = maxon.call @register-allocator.identity %19
    %21 = maxon.var_ref {var = e} {type = i64}
    %22 = maxon.binop %21, %20 {op = add}
    maxon.assign %22 {var = e} {kind = i64} {mut = 1 : i1}
    %23 = maxon.literal {value = 1 : i64}
    %24 = maxon.var_ref {var = i} {type = i64}
    %25 = maxon.binop %24, %23 {op = add}
    maxon.assign %25 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br loop_0.header
  loop_0.exit:
    %26 = maxon.var_ref {var = a} {type = i64}
    %27 = maxon.var_ref {var = c} {type = i64}
    %28 = maxon.binop %26, %27 {op = add}
    %29 = maxon.var_ref {var = d} {type = i64}
    %30 = maxon.binop %28, %29 {op = add}
    %31 = maxon.var_ref {var = e} {type = i64}
    %32 = maxon.binop %30, %31 {op = add}
    %33 = maxon.var_ref {var = f} {type = i64}
    %34 = maxon.binop %32, %33 {op = add}
    %35 = maxon.literal {value = 256 : i64}
    %36 = maxon.binop %34, %35 {op = mod}
    %37 = maxon.literal {value = 0 : i64}
    %38 = maxon.binop %36, %37 {op = lt}
    %39 = maxon.literal {value = 4294967295 : i64}
    %40 = maxon.binop %36, %39 {op = gt}
    %41 = maxon.binop %38, %40 {op = or}
    maxon.cond_br %41 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at int-loop-pressure-with-call.test:23: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [a, b, c, d, e, f, i]
    maxon.return %36
  }
}
=== standard
module {
  func @register-allocator.identity(x: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    func.return %0
  }
  func @main() -> u32 {
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
    %37 = arith.constant {value = 0 : i64}
    %38 = arith.cmpi lt %36, %37
    %39 = arith.constant {value = 4294967295 : i64}
    %40 = arith.cmpi gt %36, %39
    %41 = arith.ori1 %38, %40
    cf.cond_br %41 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %42 = memref.lea_symdata __panic_msg_0
    %43 = std.ptr_to_i64 %42
    std.call_runtime @maxon_panic %43
  __range_ok_1:
    func.return %36
  }
}
=== arm64
module {
  func @register-allocator.identity(x: i64) -> i64 {
  entry:
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=144
    arm64.mov x0, #1
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #2
    arm64.str x1, [x29, #-16]
    arm64.mov x2, #3
    arm64.str x2, [x29, #-24]
    arm64.mov x3, #4
    arm64.str x3, [x29, #-32]
    arm64.mov x4, #5
    arm64.str x4, [x29, #-40]
    arm64.mov x5, #6
    arm64.str x5, [x29, #-48]
    arm64.mov x6, #0
    arm64.str x6, [x29, #-56]
    arm64.b main.loop_0.header
  loop_0.header:
    arm64.mov x0, #3
    arm64.ldr x1, [x29, #-56]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.loop_0
    arm64.b main.loop_0.exit
  loop_0:
    arm64.ldr x0, [x29, #-16]
    arm64.bl register-allocator.identity
    arm64.ldr x1, [x29, #-8]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-8]
    arm64.ldr x3, [x29, #-32]
    arm64.ldr x0, [x29, #-32]
    arm64.bl register-allocator.identity
    arm64.ldr x4, [x29, #-24]
    arm64.add x5, x4, x0
    arm64.str x5, [x29, #-24]
    arm64.ldr x6, [x29, #-48]
    arm64.ldr x0, [x29, #-48]
    arm64.bl register-allocator.identity
    arm64.ldr x7, [x29, #-40]
    arm64.add x8, x7, x0
    arm64.str x8, [x29, #-40]
    arm64.mov x9, #1
    arm64.ldr x10, [x29, #-56]
    arm64.add x11, x10, x9
    arm64.str x11, [x29, #-56]
    arm64.b main.loop_0.header
  loop_0.exit:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-24]
    arm64.add x2, x0, x1
    arm64.ldr x3, [x29, #-32]
    arm64.add x4, x2, x3
    arm64.ldr x5, [x29, #-40]
    arm64.add x6, x4, x5
    arm64.ldr x7, [x29, #-48]
    arm64.add x8, x6, x7
    arm64.mov x9, #256
    arm64.sdiv x10, x8, x9
    arm64.msub x11, x10, x9, x8
    arm64.mov x12, #0
    arm64.cmp x11, x12
    arm64.cset x13, lt
    arm64.mov x14, #4294967295
    arm64.cmp x11, x14
    arm64.cset x15, gt
    arm64.orr x0, x13, x15
    arm64.cmp x0, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.mov x0, x11
    arm64.epilogue stack_size=144
    arm64.ret
  }
}
```

<!-- test: float-and-int-mixed-pressure -->
```maxon
function main() returns ExitCode
	let x = 3.14
	let y = 2.86
	let sum_f = x + y
	let a = 10
	let b = 20
	let sum_i = a + b
	return trunc(sum_f) + sum_i
end 'main'
```
```exitcode
36
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3.14 : f64}
    maxon.assign %0 {var = x} {kind = f64} {decl = 1 : i1}
    %1 = maxon.literal {value = 2.86 : f64}
    maxon.assign %1 {var = y} {kind = f64} {decl = 1 : i1}
    %2 = maxon.binop %0, %1 {op = add} {kind = f64}
    maxon.assign %2 {var = sum_f} {kind = f64} {decl = 1 : i1}
    %3 = maxon.literal {value = 10 : i64}
    maxon.assign %3 {var = a} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 20 : i64}
    maxon.assign %4 {var = b} {kind = i64} {decl = 1 : i1}
    %5 = maxon.binop %3, %4 {op = add}
    maxon.assign %5 {var = sum_i} {kind = i64} {decl = 1 : i1}
    %6 = maxon.trunc %2
    %7 = maxon.binop %6, %5 {op = add}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at float-and-int-mixed-pressure.test:9: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, y, sum_f, a, b, sum_i]
    maxon.return %7
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.float_constant {value = 3.14 : f64}
    %1 = arith.float_constant {value = 2.86 : f64}
    %2 = arith.addf %0, %1
    %3 = arith.constant {value = 10 : i64}
    %4 = arith.constant {value = 20 : i64}
    %5 = arith.addi %3, %4
    %6 = arith.fptosi %2
    %7 = arith.addi %6, %5
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.cmpi lt %7, %8
    %10 = arith.constant {value = 4294967295 : i64}
    %11 = arith.cmpi gt %7, %10
    %12 = arith.ori1 %9, %11
    cf.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %13 = memref.lea_symdata __panic_msg_0
    %14 = std.ptr_to_i64 %13
    std.call_runtime @mrt_panic %14
  __range_ok_0:
    func.return %7
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.movsd xmm0, [rip+__float_3.14]
    x64.movsd xmm1, [rip+__float_2.86]
    x64.movsd xmm2, xmm0
    x64.addsd xmm2, xmm1
    x64.mov rax, 10
    x64.mov rcx, 20
    x64.add rax, rcx
    x64.cvttsd2si rdx, xmm2
    x64.add rdx, rax
    x64.xor ebx, ebx
    x64.mov esi, 4294967295
    x64.cmp rdx, rsi
    x64.jg main.__range_panic_0
    x64.cmp rdx, rbx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %5 = maxon.binop %3, %4 {op = add}
    maxon.assign %5 {var = sum_i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.trunc %2
    %7 = maxon.binop %6, %5 {op = add}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at float-and-int-mixed-pressure.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [x, y, sum_f, a, b, sum_i]
    maxon.return %7
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.float_constant {value = 3.14 : f64}
    %1 = arith.float_constant {value = 2.86 : f64}
    %2 = arith.addf %0, %1
    %3 = arith.constant {value = 10 : i64}
    %4 = arith.constant {value = 20 : i64}
    %5 = arith.addi %3, %4
    %6 = arith.fptosi %2
    %7 = arith.addi %6, %5
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.cmpi lt %7, %8
    %10 = arith.constant {value = 4294967295 : i64}
    %11 = arith.cmpi gt %7, %10
    %12 = arith.ori1 %9, %11
    cf.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %13 = memref.lea_symdata __panic_msg_0
    %14 = std.ptr_to_i64 %13
    std.call_runtime @maxon_panic %14
  __range_ok_0:
    func.return %7
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.fldr d0, [__float_3.14]
    arm64.fldr d1, [__float_2.86]
    arm64.fadd d2, d0, d1
    arm64.mov x0, #10
    arm64.mov x1, #20
    arm64.add x2, x0, x1
    arm64.fcvtzs x3, d2
    arm64.add x4, x3, x2
    arm64.mov x5, #0
    arm64.cmp x4, x5
    arm64.cset x6, lt
    arm64.mov x7, #4294967295
    arm64.cmp x4, x7
    arm64.cset x8, gt
    arm64.orr x9, x6, x8
    arm64.cmp x9, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x4
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-value-live-across-nested-control -->
```maxon
function main() returns ExitCode
	let sentinel = 100
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
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.assign %0 {var = sentinel} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = total} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br outer_0.header
  outer_0.header:
    %3 = maxon.literal {value = 3 : i64}
    %4 = maxon.var_ref {var = i} {type = i64}
    %5 = maxon.binop %4, %3 {op = lt}
    maxon.cond_br %5 [then: outer_0, else: outer_0.exit]
  outer_0:
    %6 = maxon.literal {value = 0 : i64}
    maxon.assign %6 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_0.header
  inner_0.header:
    %7 = maxon.literal {value = 3 : i64}
    %8 = maxon.var_ref {var = j} {type = i64}
    %9 = maxon.binop %8, %7 {op = lt}
    maxon.cond_br %9 [then: inner_0, else: inner_0.exit]
  inner_0:
    %10 = maxon.var_ref {var = i} {type = i64}
    %11 = maxon.var_ref {var = j} {type = i64}
    %12 = maxon.binop %10, %11 {op = eq}
    maxon.cond_br %12 [then: diag_0, else: diag_0.merge]
  diag_0:
    %13 = maxon.literal {value = 1 : i64}
    %14 = maxon.var_ref {var = total} {type = i64}
    %15 = maxon.binop %14, %13 {op = add}
    maxon.assign %15 {var = total} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br diag_0.merge
  diag_0.merge:
    %16 = maxon.literal {value = 1 : i64}
    %17 = maxon.var_ref {var = j} {type = i64}
    %18 = maxon.binop %17, %16 {op = add}
    maxon.assign %18 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br inner_0.header
  inner_0.exit:
    %19 = maxon.literal {value = 1 : i64}
    %20 = maxon.var_ref {var = i} {type = i64}
    %21 = maxon.binop %20, %19 {op = add}
    maxon.assign %21 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [j]
    maxon.br outer_0.header
  outer_0.exit:
    %22 = maxon.var_ref {var = sentinel} {type = i64}
    %23 = maxon.var_ref {var = total} {type = i64}
    %24 = maxon.binop %22, %23 {op = add}
    %25 = maxon.literal {value = 0 : i64}
    %26 = maxon.binop %24, %25 {op = lt}
    %27 = maxon.literal {value = 4294967295 : i64}
    %28 = maxon.binop %24, %27 {op = gt}
    %29 = maxon.binop %26, %28 {op = or}
    maxon.cond_br %29 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-value-live-across-nested-control.test:16: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [sentinel, total, i]
    maxon.return %24
  }
}
=== standard
module {
  func @main() -> u32 {
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
    cf.br inner_0.header
  inner_0.header:
    %7 = arith.constant {value = 3 : i64}
    %8 = memref.load j : i64
    %9 = arith.cmpi lt %8, %7
    cf.cond_br %9 [then: inner_0, else: inner_0.exit]
  inner_0:
    %10 = memref.load i : i64
    %11 = memref.load j : i64
    %12 = arith.cmpi eq %10, %11
    cf.cond_br %12 [then: diag_0, else: diag_0.merge]
  diag_0:
    %13 = arith.constant {value = 1 : i64}
    %14 = memref.load total : i64
    %15 = arith.addi %14, %13
    memref.store %15, total
    cf.br diag_0.merge
  diag_0.merge:
    %16 = arith.constant {value = 1 : i64}
    %17 = memref.load j : i64
    %18 = arith.addi %17, %16
    memref.store %18, j
    cf.br inner_0.header
  inner_0.exit:
    %19 = arith.constant {value = 1 : i64}
    %20 = memref.load i : i64
    %21 = arith.addi %20, %19
    memref.store %21, i
    cf.br outer_0.header
  outer_0.exit:
    %22 = memref.load sentinel : i64
    %23 = memref.load total : i64
    %24 = arith.addi %22, %23
    %25 = arith.constant {value = 0 : i64}
    %26 = arith.cmpi lt %24, %25
    %27 = arith.constant {value = 4294967295 : i64}
    %28 = arith.cmpi gt %24, %27
    %29 = arith.ori1 %26, %28
    cf.cond_br %29 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %30 = memref.lea_symdata __panic_msg_0
    %31 = std.ptr_to_i64 %30
    std.call_runtime @mrt_panic %31
  __range_ok_0:
    func.return %24
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.mov rax, 100
    x64.mov [rbp-8], rax
    x64.xor ecx, ecx
    x64.mov [rbp-16], rcx
    x64.xor edx, edx
    x64.mov [rbp-24], rdx
    x64.jmp main.outer_0.header
  outer_0.header:
    x64.mov rax, 3
    x64.mov rcx, [rbp-24]
    x64.cmp rcx, rax
    x64.jge main.outer_0.exit
  outer_0:
    x64.xor eax, eax
    x64.mov [rbp-32], rax
    x64.jmp main.inner_0.header
  inner_0.header:
    x64.mov rax, 3
    x64.mov rcx, [rbp-32]
    x64.cmp rcx, rax
    x64.jge main.inner_0.exit
  inner_0:
    x64.mov rax, [rbp-24]
    x64.mov rcx, [rbp-32]
    x64.cmp rax, rcx
    x64.jne main.diag_0.merge
  diag_0:
    x64.mov rax, 1
    x64.mov rcx, [rbp-16]
    x64.add rcx, rax
    x64.mov [rbp-16], rcx
    x64.jmp main.diag_0.merge
  diag_0.merge:
    x64.mov rax, 1
    x64.mov rcx, [rbp-32]
    x64.add rcx, rax
    x64.mov [rbp-32], rcx
    x64.jmp main.inner_0.header
  inner_0.exit:
    x64.mov rax, 1
    x64.mov rcx, [rbp-24]
    x64.add rcx, rax
    x64.mov [rbp-24], rcx
    x64.jmp main.outer_0.header
  outer_0.exit:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.add rax, rcx
    x64.xor edx, edx
    x64.mov ebx, 4294967295
    x64.cmp rax, rbx
    x64.jg main.__range_panic_0
    x64.cmp rax, rdx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %5 = maxon.binop %4, %3 {op = lt}
    maxon.cond_br %5 [then: outer_0, else: outer_0.exit]
  outer_0:
    %6 = maxon.literal {value = 0 : i64}
    maxon.assign %6 {var = j} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br inner_1.header
  inner_1.header:
    %7 = maxon.literal {value = 3 : i64}
    %8 = maxon.var_ref {var = j} {type = i64}
    %9 = maxon.binop %8, %7 {op = lt}
    maxon.cond_br %9 [then: inner_1, else: inner_1.exit]
  inner_1:
    %10 = maxon.var_ref {var = i} {type = i64}
    %11 = maxon.var_ref {var = j} {type = i64}
    %12 = maxon.binop %10, %11 {op = eq}
    maxon.cond_br %12 [then: diag_2, else: diag_2.merge]
  diag_2:
    %13 = maxon.literal {value = 1 : i64}
    %14 = maxon.var_ref {var = total} {type = i64}
    %15 = maxon.binop %14, %13 {op = add}
    maxon.assign %15 {var = total} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br diag_2.merge
  diag_2.merge:
    %16 = maxon.literal {value = 1 : i64}
    %17 = maxon.var_ref {var = j} {type = i64}
    %18 = maxon.binop %17, %16 {op = add}
    maxon.assign %18 {var = j} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br inner_1.header
  inner_1.exit:
    %19 = maxon.literal {value = 1 : i64}
    %20 = maxon.var_ref {var = i} {type = i64}
    %21 = maxon.binop %20, %19 {op = add}
    maxon.assign %21 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [j]
    maxon.br outer_0.header
  outer_0.exit:
    %22 = maxon.var_ref {var = sentinel} {type = i64}
    %23 = maxon.var_ref {var = total} {type = i64}
    %24 = maxon.binop %22, %23 {op = add}
    %25 = maxon.literal {value = 0 : i64}
    %26 = maxon.binop %24, %25 {op = lt}
    %27 = maxon.literal {value = 4294967295 : i64}
    %28 = maxon.binop %24, %27 {op = gt}
    %29 = maxon.binop %26, %28 {op = or}
    maxon.cond_br %29 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    maxon.panic "panic at int-value-live-across-nested-control.test:16: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_3:
    maxon.scope_end [sentinel, total, i]
    maxon.return %24
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %25 = arith.constant {value = 0 : i64}
    %26 = arith.cmpi lt %24, %25
    %27 = arith.constant {value = 4294967295 : i64}
    %28 = arith.cmpi gt %24, %27
    %29 = arith.ori1 %26, %28
    cf.cond_br %29 [then: __range_panic_3, else: __range_ok_3]
  __range_panic_3:
    %30 = memref.lea_symdata __panic_msg_0
    %31 = std.ptr_to_i64 %30
    std.call_runtime @maxon_panic %31
  __range_ok_3:
    func.return %24
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=80
    arm64.mov x0, #100
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-16]
    arm64.mov x2, #0
    arm64.str x2, [x29, #-24]
    arm64.b main.outer_0.header
  outer_0.header:
    arm64.mov x0, #3
    arm64.ldr x1, [x29, #-24]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.outer_0
    arm64.b main.outer_0.exit
  outer_0:
    arm64.mov x0, #0
    arm64.str x0, [x29, #-32]
    arm64.b main.inner_1.header
  inner_1.header:
    arm64.mov x0, #3
    arm64.ldr x1, [x29, #-32]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.inner_1
    arm64.b main.inner_1.exit
  inner_1:
    arm64.ldr x0, [x29, #-24]
    arm64.ldr x1, [x29, #-32]
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.diag_2
    arm64.b main.diag_2.merge
  diag_2:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-16]
    arm64.b main.diag_2.merge
  diag_2.merge:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-32]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-32]
    arm64.b main.inner_1.header
  inner_1.exit:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-24]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-24]
    arm64.b main.outer_0.header
  outer_0.exit:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x0, x1
    arm64.mov x3, #0
    arm64.cmp x2, x3
    arm64.cset x4, lt
    arm64.mov x5, #4294967295
    arm64.cmp x2, x5
    arm64.cset x6, gt
    arm64.orr x7, x4, x6
    arm64.cmp x7, #0
    arm64.b.ne main.__range_panic_3
    arm64.b main.__range_ok_3
  __range_panic_3:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_3:
    arm64.mov x0, x2
    arm64.epilogue stack_size=80
    arm64.ret
  }
}
```

<!-- test: int-fibonacci -->
```maxon
function main() returns ExitCode
	var a = 0
	var b = 1
	var i = 0
	while i < 13 'loop'
		let temp = a + b
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
```RequiredIR:x64-windows
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
    %5 = maxon.binop %4, %3 {op = lt}
    maxon.cond_br %5 [then: loop_0, else: loop_0.exit]
  loop_0:
    %6 = maxon.var_ref {var = a} {type = i64}
    %7 = maxon.var_ref {var = b} {type = i64}
    %8 = maxon.binop %6, %7 {op = add}
    maxon.assign %8 {var = temp} {kind = i64} {decl = 1 : i1}
    %9 = maxon.var_ref {var = b} {type = i64}
    maxon.assign %9 {var = a} {kind = i64} {mut = 1 : i1}
    maxon.assign %8 {var = b} {kind = i64} {mut = 1 : i1}
    %10 = maxon.literal {value = 1 : i64}
    %11 = maxon.var_ref {var = i} {type = i64}
    %12 = maxon.binop %11, %10 {op = add}
    maxon.assign %12 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [temp]
    maxon.br loop_0.header
  loop_0.exit:
    %13 = maxon.var_ref {var = a} {type = i64}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %13, %14 {op = lt}
    %16 = maxon.literal {value = 4294967295 : i64}
    %17 = maxon.binop %13, %16 {op = gt}
    %18 = maxon.binop %15, %17 {op = or}
    maxon.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-fibonacci.test:12: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, i]
    maxon.return %13
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @mrt_panic %20
  __range_ok_0:
    func.return %13
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.mov rcx, 1
    x64.mov [rbp-16], rcx
    x64.xor edx, edx
    x64.mov [rbp-24], rdx
    x64.jmp main.loop_0.header
  loop_0.header:
    x64.mov rax, 13
    x64.mov rcx, [rbp-24]
    x64.cmp rcx, rax
    x64.jge main.loop_0.exit
  loop_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.add rax, rcx
    x64.mov rdx, [rbp-16]
    x64.mov [rbp-8], rdx
    x64.mov [rbp-16], rax
    x64.mov rbx, 1
    x64.mov rsi, [rbp-24]
    x64.add rsi, rbx
    x64.mov [rbp-24], rsi
    x64.jmp main.loop_0.header
  loop_0.exit:
    x64.mov rax, [rbp-8]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
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
    %5 = maxon.binop %4, %3 {op = lt}
    maxon.cond_br %5 [then: loop_0, else: loop_0.exit]
  loop_0:
    %6 = maxon.var_ref {var = a} {type = i64}
    %7 = maxon.var_ref {var = b} {type = i64}
    %8 = maxon.binop %6, %7 {op = add}
    maxon.assign %8 {var = temp} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.var_ref {var = b} {type = i64}
    maxon.assign %9 {var = a} {kind = i64} {mut = 1 : i1}
    maxon.assign %8 {var = b} {kind = i64} {mut = 1 : i1}
    %10 = maxon.literal {value = 1 : i64}
    %11 = maxon.var_ref {var = i} {type = i64}
    %12 = maxon.binop %11, %10 {op = add}
    maxon.assign %12 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [temp]
    maxon.br loop_0.header
  loop_0.exit:
    %13 = maxon.var_ref {var = a} {type = i64}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %13, %14 {op = lt}
    %16 = maxon.literal {value = 4294967295 : i64}
    %17 = maxon.binop %13, %16 {op = gt}
    %18 = maxon.binop %15, %17 {op = or}
    maxon.cond_br %18 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at int-fibonacci.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [a, b, i]
    maxon.return %13
  }
}
=== standard
module {
  func @main() -> u32 {
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
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @maxon_panic %20
  __range_ok_1:
    func.return %13
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=80
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.str x1, [x29, #-16]
    arm64.mov x2, #0
    arm64.str x2, [x29, #-24]
    arm64.b main.loop_0.header
  loop_0.header:
    arm64.mov x0, #13
    arm64.ldr x1, [x29, #-24]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.loop_0
    arm64.b main.loop_0.exit
  loop_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x0, x1
    arm64.ldr x3, [x29, #-16]
    arm64.str x3, [x29, #-8]
    arm64.str x2, [x29, #-16]
    arm64.mov x4, #1
    arm64.ldr x5, [x29, #-24]
    arm64.add x6, x5, x4
    arm64.str x6, [x29, #-24]
    arm64.b main.loop_0.header
  loop_0.exit:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.epilogue stack_size=80
    arm64.ret
  }
}
```

<!-- test: int-division-high-pressure -->
```maxon
function main() returns ExitCode
	let a = 10
	let b = 20
	let c = 30
	let d = 40
	let e = 50
	let f = 60
	let g = 70
	let h = 2
	return (a + b + c + d + e + f + g) / h
end 'main'
```
```exitcode
140
```

<!-- test: int-callee-saved-clobber -->
```maxon

typealias Integer = int(i64.min to i64.max)

function useRegs(a Integer, b Integer, c Integer, d Integer) returns Integer
	let x = a + b
	let y = c + d
	let z = x + y
	return z
end 'useRegs'

function main() returns ExitCode
	let sentinel = 42
	let result = useRegs(1, b: 2, c: 3, d: 4)
	return sentinel + result
end 'main'
```
```exitcode
52
```
```RequiredIR:x64-windows
=== maxon
module {
  func @useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.param {index = 2 : i32} {name = c} {type = i64}
    %3 = maxon.param {index = 3 : i32} {name = d} {type = i64}
    %4 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    maxon.assign %4 {var = x} {kind = i64} {decl = 1 : i1}
    %5 = maxon.binop %2, %3 {op = add} {optimalType = i64}
    maxon.assign %5 {var = y} {kind = i64} {decl = 1 : i1}
    %6 = maxon.binop %4, %5 {op = add}
    maxon.assign %6 {var = z} {kind = i64} {decl = 1 : i1}
    maxon.scope_end [a, b, c, d, x, y, z]
    maxon.return %6
  }
  func @main() -> i64 {
  entry:
    %7 = maxon.literal {value = 42 : i64}
    maxon.assign %7 {var = sentinel} {kind = i64} {decl = 1 : i1}
    %8 = maxon.literal {value = 1 : i64}
    %9 = maxon.literal {value = 2 : i64}
    %10 = maxon.literal {value = 3 : i64}
    %11 = maxon.literal {value = 4 : i64}
    %12 = maxon.call @useRegs %8, %9, %10, %11
    maxon.assign %12 {var = result} {kind = i64} {decl = 1 : i1}
    %13 = maxon.binop %7, %12 {op = add} {optimalType = i64}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %13, %14 {op = lt}
    %16 = maxon.literal {value = 4294967295 : i64}
    %17 = maxon.binop %13, %16 {op = gt}
    %18 = maxon.binop %15, %17 {op = or}
    maxon.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-callee-saved-clobber.test:15: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [sentinel, result]
    maxon.return %13
  }
}
=== standard
module {
  func @useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = func.param c : StdI64
    %3 = func.param d : StdI64
    %4 = arith.addi %0, %1
    %5 = arith.addi %2, %3
    %6 = arith.addi %4, %5
    func.return %6
  }
  func @main() -> u32 {
  entry:
    %7 = arith.constant {value = 42 : i64}
    %8 = arith.constant {value = 1 : i64}
    %9 = arith.constant {value = 2 : i64}
    %10 = arith.constant {value = 3 : i64}
    %11 = arith.constant {value = 4 : i64}
    %12 = func.call @useRegs %8, %9, %10, %11
    %13 = arith.addi %7, %12
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @mrt_panic %20
  __range_ok_0:
    func.return %13
  }
}
=== x86
module {
  func @useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    x64.add rcx, rdx
    x64.add r8, r9
    x64.lea rax, [rcx + r8]
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 42
    x64.mov rcx, 1
    x64.mov rdx, 2
    x64.mov r8, 3
    x64.mov r9, 4
    x64.call useRegs
    x64.mov rcx, 42
    x64.add rcx, rax
    x64.xor edx, edx
    x64.mov ebx, 4294967295
    x64.cmp rcx, rbx
    x64.jg main.__range_panic_0
    x64.cmp rcx, rdx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rcx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.param {index = 2 : i32} {name = c} {type = i64}
    %3 = maxon.param {index = 3 : i32} {name = d} {type = i64}
    %4 = maxon.binop %0, %1 {op = add} {optimalType = i64}
    maxon.assign %4 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.binop %2, %3 {op = add} {optimalType = i64}
    maxon.assign %5 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.binop %4, %5 {op = add}
    maxon.assign %6 {var = z} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.scope_end [a, b, c, d, x, y, z]
    maxon.return %6
  }
  func @main() -> i64 {
  entry:
    %7 = maxon.literal {value = 42 : i64}
    maxon.assign %7 {var = sentinel} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %8 = maxon.literal {value = 1 : i64}
    %9 = maxon.literal {value = 2 : i64}
    %10 = maxon.literal {value = 3 : i64}
    %11 = maxon.literal {value = 4 : i64}
    %12 = maxon.call @register-allocator.useRegs %8, %9, %10, %11
    maxon.assign %12 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.binop %7, %12 {op = add}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %13, %14 {op = lt}
    %16 = maxon.literal {value = 4294967295 : i64}
    %17 = maxon.binop %13, %16 {op = gt}
    %18 = maxon.binop %15, %17 {op = or}
    maxon.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-callee-saved-clobber.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [sentinel, result]
    maxon.return %13
  }
}
=== standard
module {
  func @register-allocator.useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = func.param c : StdI64
    %3 = func.param d : StdI64
    %4 = arith.addi %0, %1
    %5 = arith.addi %2, %3
    %6 = arith.addi %4, %5
    func.return %6
  }
  func @main() -> u32 {
  entry:
    %7 = arith.constant {value = 42 : i64}
    %8 = arith.constant {value = 1 : i64}
    %9 = arith.constant {value = 2 : i64}
    %10 = arith.constant {value = 3 : i64}
    %11 = arith.constant {value = 4 : i64}
    %12 = func.call @register-allocator.useRegs %8, %9, %10, %11
    %13 = arith.addi %7, %12
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @maxon_panic %20
  __range_ok_0:
    func.return %13
  }
}
=== arm64
module {
  func @register-allocator.useRegs(a: i64, b: i64, c: i64, d: i64) -> i64 {
  entry:
    arm64.add x4, x0, x1
    arm64.add x0, x2, x3
    arm64.add x1, x4, x0
    arm64.mov x0, x1
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #42
    arm64.mov x0, #1
    arm64.mov x1, #2
    arm64.mov x2, #3
    arm64.mov x3, #4
    arm64.bl register-allocator.useRegs
    arm64.mov x1, #42
    arm64.add x2, x1, x0
    arm64.mov x3, #0
    arm64.cmp x2, x3
    arm64.cset x4, lt
    arm64.mov x5, #4294967295
    arm64.cmp x2, x5
    arm64.cset x6, gt
    arm64.orr x7, x4, x6
    arm64.cmp x7, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x2
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-float-survives-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getInt() returns Integer
	return 40
end 'getInt'

function main() returns ExitCode
	let f = 3.14
	let x = getInt()
	return trunc(f) + x
end 'main'
```
```exitcode
43
```
```RequiredIR:x64-windows
=== maxon
module {
  func @getInt() -> i64 {
  entry:
    %0 = maxon.literal {value = 40 : i64}
    maxon.scope_end []
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.literal {value = 3.14 : f64}
    maxon.assign %1 {var = f} {kind = f64} {decl = 1 : i1}
    %2 = maxon.call @getInt
    maxon.assign %2 {var = x} {kind = i64} {decl = 1 : i1}
    %3 = maxon.trunc %1
    %4 = maxon.binop %3, %2 {op = add} {optimalType = i64}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-float-survives-call.test:12: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [f, x]
    maxon.return %4
  }
}
=== standard
module {
  func @getInt() -> i64 {
  entry:
    %0 = arith.constant {value = 40 : i64}
    func.return %0
  }
  func @main() -> u32 {
  entry:
    %1 = arith.float_constant {value = 3.14 : f64}
    %2 = func.call @getInt
    %3 = arith.fptosi %1
    %4 = arith.addi %3, %2
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.cmpi lt %4, %5
    %7 = arith.constant {value = 4294967295 : i64}
    %8 = arith.cmpi gt %4, %7
    %9 = arith.ori1 %6, %8
    cf.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %10 = memref.lea_symdata __panic_msg_0
    %11 = std.ptr_to_i64 %10
    std.call_runtime @mrt_panic %11
  __range_ok_0:
    func.return %4
  }
}
=== x86
module {
  func @getInt() -> i64 {
  entry:
    x64.mov rax, 40
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.movsd xmm0, [rip+__float_3.14]
    x64.movsd [rbp-8], xmm0
    x64.call getInt
    x64.movsd xmm0, [rbp-8]
    x64.cvttsd2si rcx, xmm0
    x64.add rcx, rax
    x64.xor eax, eax
    x64.mov edx, 4294967295
    x64.cmp rcx, rdx
    x64.jg main.__range_panic_0
    x64.cmp rcx, rax
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rcx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.getInt() -> i64 {
  entry:
    %0 = maxon.literal {value = 40 : i64}
    maxon.scope_end []
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.literal {value = 3.14 : f64}
    maxon.assign %1 {var = f} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.call @register-allocator.getInt
    maxon.assign %2 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.trunc %1
    %4 = maxon.binop %3, %2 {op = add}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-float-survives-call.test:12: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [f, x]
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
  func @main() -> u32 {
  entry:
    %1 = arith.float_constant {value = 3.14 : f64}
    %2 = func.call @register-allocator.getInt
    %3 = arith.fptosi %1
    %4 = arith.addi %3, %2
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.cmpi lt %4, %5
    %7 = arith.constant {value = 4294967295 : i64}
    %8 = arith.cmpi gt %4, %7
    %9 = arith.ori1 %6, %8
    cf.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %10 = memref.lea_symdata __panic_msg_0
    %11 = std.ptr_to_i64 %10
    std.call_runtime @maxon_panic %11
  __range_ok_0:
    func.return %4
  }
}
=== arm64
module {
  func @register-allocator.getInt() -> i64 {
  entry:
    arm64.mov x0, #40
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=32
    arm64.fldr d0, [__float_3.14]
    arm64.fstr d0, [x29, #-8]
    arm64.bl register-allocator.getInt
    arm64.fldr d0, [x29, #-8]
    arm64.fcvtzs x1, d0
    arm64.add x2, x1, x0
    arm64.mov x0, #0
    arm64.cmp x2, x0
    arm64.cset x1, lt
    arm64.mov x0, #4294967295
    arm64.cmp x2, x0
    arm64.cset x3, gt
    arm64.orr x0, x1, x3
    arm64.cmp x0, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x2
    arm64.epilogue stack_size=32
    arm64.ret
  }
}
```

<!-- test: int-sequential-divisions -->
```maxon
function main() returns ExitCode
	let a = 100
	let b = 5
	let c = 84
	let d = 4
	return a / b + c / d
end 'main'
```
```exitcode
41
```

<!-- test: int-remainder-in-arithmetic -->
```maxon
function main() returns ExitCode
	let a = 100
	let b = 7
	let c = 10
	let rem = a mod b
	return rem * c
end 'main'
```
```exitcode
20
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 7 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.literal {value = 10 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1}
    %3 = maxon.binop %0, %1 {op = mod}
    maxon.assign %3 {var = rem} {kind = i64} {decl = 1 : i1}
    %4 = maxon.binop %3, %2 {op = mul}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-remainder-in-arithmetic.test:7: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, c, rem]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    %1 = arith.constant {value = 7 : i64}
    %2 = arith.constant {value = 10 : i64}
    %3 = arith.remsi %0, %1
    %4 = arith.muli %3, %2
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.cmpi lt %4, %5
    %7 = arith.constant {value = 4294967295 : i64}
    %8 = arith.cmpi gt %4, %7
    %9 = arith.ori1 %6, %8
    cf.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %10 = memref.lea_symdata __panic_msg_0
    %11 = std.ptr_to_i64 %10
    std.call_runtime @mrt_panic %11
  __range_ok_0:
    func.return %4
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 100
    x64.mov rcx, 7
    x64.mov rdx, 10
    x64.cqo
    x64.idiv rcx
    x64.mov rbx, 10
    x64.imul rdx, rbx
    x64.xor esi, esi
    x64.mov edi, 4294967295
    x64.cmp rdx, rdi
    x64.jg main.__range_panic_0
    x64.cmp rdx, rsi
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rdx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 7 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 10 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.binop %0, %1 {op = mod}
    maxon.assign %3 {var = rem} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.binop %3, %2 {op = mul}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.binop %4, %5 {op = lt}
    %7 = maxon.literal {value = 4294967295 : i64}
    %8 = maxon.binop %4, %7 {op = gt}
    %9 = maxon.binop %6, %8 {op = or}
    maxon.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-remainder-in-arithmetic.test:7: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b, c, rem]
    maxon.return %4
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    %1 = arith.constant {value = 7 : i64}
    %2 = arith.constant {value = 10 : i64}
    %3 = arith.remsi %0, %1
    %4 = arith.muli %3, %2
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.cmpi lt %4, %5
    %7 = arith.constant {value = 4294967295 : i64}
    %8 = arith.cmpi gt %4, %7
    %9 = arith.ori1 %6, %8
    cf.cond_br %9 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %10 = memref.lea_symdata __panic_msg_0
    %11 = std.ptr_to_i64 %10
    std.call_runtime @maxon_panic %11
  __range_ok_0:
    func.return %4
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #100
    arm64.mov x1, #7
    arm64.mov x2, #10
    arm64.sdiv x3, x0, x1
    arm64.msub x4, x3, x1, x0
    arm64.mul x5, x4, x2
    arm64.mov x6, #0
    arm64.cmp x5, x6
    arm64.cset x7, lt
    arm64.mov x8, #4294967295
    arm64.cmp x5, x8
    arm64.cset x9, gt
    arm64.orr x10, x7, x9
    arm64.cmp x10, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x5
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-call-arg-reverse -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sub(a Integer, b Integer) returns Integer
	return a - b
end 'sub'

function main() returns ExitCode
	let x = 10
	let y = 3
	let result = sub(y, b: x)
	return result + 45
end 'main'
```
```exitcode
38
```
```RequiredIR:x64-windows
=== maxon
module {
  func @sub(a: i64, b: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.binop %0, %1 {op = sub} {optimalType = i64}
    maxon.scope_end [a, b]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 10 : i64}
    maxon.assign %3 {var = x} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 3 : i64}
    maxon.assign %4 {var = y} {kind = i64} {decl = 1 : i1}
    %5 = maxon.call @sub %4, %3
    maxon.assign %5 {var = result} {kind = i64} {decl = 1 : i1}
    %6 = maxon.literal {value = 45 : i64}
    %7 = maxon.binop %5, %6 {op = add} {optimalType = i64}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-call-arg-reverse.test:13: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, y, result]
    maxon.return %7
  }
}
=== standard
module {
  func @sub(a: i64, b: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = arith.subi %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 10 : i64}
    %4 = arith.constant {value = 3 : i64}
    %5 = func.call @sub %4, %3
    %6 = arith.constant {value = 45 : i64}
    %7 = arith.addi %5, %6
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.cmpi lt %7, %8
    %10 = arith.constant {value = 4294967295 : i64}
    %11 = arith.cmpi gt %7, %10
    %12 = arith.ori1 %9, %11
    cf.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %13 = memref.lea_symdata __panic_msg_0
    %14 = std.ptr_to_i64 %13
    std.call_runtime @mrt_panic %14
  __range_ok_0:
    func.return %7
  }
}
=== x86
module {
  func @sub(a: i64, b: i64) -> i64 {
  entry:
    x64.sub rcx, rdx
    x64.mov rax, rcx
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 3
    x64.mov rdx, 10
    x64.call sub
    x64.mov rcx, 45
    x64.add rax, rcx
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.sub(a: i64, b: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = a} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = b} {type = i64}
    %2 = maxon.binop %0, %1 {op = sub} {optimalType = i64}
    maxon.scope_end [a, b]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 10 : i64}
    maxon.assign %3 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4 = maxon.literal {value = 3 : i64}
    maxon.assign %4 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %5 = maxon.call @register-allocator.sub %4, %3
    maxon.assign %5 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %6 = maxon.literal {value = 45 : i64}
    %7 = maxon.binop %5, %6 {op = add}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.binop %7, %8 {op = lt}
    %10 = maxon.literal {value = 4294967295 : i64}
    %11 = maxon.binop %7, %10 {op = gt}
    %12 = maxon.binop %9, %11 {op = or}
    maxon.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-call-arg-reverse.test:13: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [x, y, result]
    maxon.return %7
  }
}
=== standard
module {
  func @register-allocator.sub(a: i64, b: i64) -> i64 {
  entry:
    %0 = func.param a : StdI64
    %1 = func.param b : StdI64
    %2 = arith.subi %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 10 : i64}
    %4 = arith.constant {value = 3 : i64}
    %5 = func.call @register-allocator.sub %4, %3
    %6 = arith.constant {value = 45 : i64}
    %7 = arith.addi %5, %6
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.cmpi lt %7, %8
    %10 = arith.constant {value = 4294967295 : i64}
    %11 = arith.cmpi gt %7, %10
    %12 = arith.ori1 %9, %11
    cf.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %13 = memref.lea_symdata __panic_msg_0
    %14 = std.ptr_to_i64 %13
    std.call_runtime @maxon_panic %14
  __range_ok_0:
    func.return %7
  }
}
=== arm64
module {
  func @register-allocator.sub(a: i64, b: i64) -> i64 {
  entry:
    arm64.sub x2, x0, x1
    arm64.mov x0, x2
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #3
    arm64.mov x1, #10
    arm64.bl register-allocator.sub
    arm64.mov x1, #45
    arm64.add x2, x0, x1
    arm64.mov x0, #0
    arm64.cmp x2, x0
    arm64.cset x1, lt
    arm64.mov x0, #4294967295
    arm64.cmp x2, x0
    arm64.cset x3, gt
    arm64.orr x0, x1, x3
    arm64.cmp x0, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x2
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-subtraction-high-pressure -->
```maxon
function main() returns ExitCode
	let a = 100
	let b = 1
	let c = 2
	let d = 3
	let e = 4
	let f = 5
	let g = 6
	let h = 7
	return a - b - c - d - e - f - g - h
end 'main'
```
```exitcode
72
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 100 : i64}
    maxon.assign %0 {var = a} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 1 : i64}
    maxon.assign %1 {var = b} {kind = i64} {decl = 1 : i1}
    %2 = maxon.literal {value = 2 : i64}
    maxon.assign %2 {var = c} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 3 : i64}
    maxon.assign %3 {var = d} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 4 : i64}
    maxon.assign %4 {var = e} {kind = i64} {decl = 1 : i1}
    %5 = maxon.literal {value = 5 : i64}
    maxon.assign %5 {var = f} {kind = i64} {decl = 1 : i1}
    %6 = maxon.literal {value = 6 : i64}
    maxon.assign %6 {var = g} {kind = i64} {decl = 1 : i1}
    %7 = maxon.literal {value = 7 : i64}
    maxon.assign %7 {var = h} {kind = i64} {decl = 1 : i1}
    %8 = maxon.binop %0, %1 {op = sub}
    %9 = maxon.binop %8, %2 {op = sub}
    %10 = maxon.binop %9, %3 {op = sub}
    %11 = maxon.binop %10, %4 {op = sub}
    %12 = maxon.binop %11, %5 {op = sub}
    %13 = maxon.binop %12, %6 {op = sub}
    %14 = maxon.binop %13, %7 {op = sub}
    %15 = maxon.literal {value = 0 : i64}
    %16 = maxon.binop %14, %15 {op = lt}
    %17 = maxon.literal {value = 4294967295 : i64}
    %18 = maxon.binop %14, %17 {op = gt}
    %19 = maxon.binop %16, %18 {op = or}
    maxon.cond_br %19 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-subtraction-high-pressure.test:11: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [a, b, c, d, e, f, g, h]
    maxon.return %14
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    %1 = arith.constant {value = 1 : i64}
    %2 = arith.constant {value = 2 : i64}
    %3 = arith.constant {value = 3 : i64}
    %4 = arith.constant {value = 4 : i64}
    %5 = arith.constant {value = 5 : i64}
    %6 = arith.constant {value = 6 : i64}
    %7 = arith.constant {value = 7 : i64}
    %8 = arith.subi %0, %1
    %9 = arith.subi %8, %2
    %10 = arith.subi %9, %3
    %11 = arith.subi %10, %4
    %12 = arith.subi %11, %5
    %13 = arith.subi %12, %6
    %14 = arith.subi %13, %7
    %15 = arith.constant {value = 0 : i64}
    %16 = arith.cmpi lt %14, %15
    %17 = arith.constant {value = 4294967295 : i64}
    %18 = arith.cmpi gt %14, %17
    %19 = arith.ori1 %16, %18
    cf.cond_br %19 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %20 = memref.lea_symdata __panic_msg_0
    %21 = std.ptr_to_i64 %20
    std.call_runtime @mrt_panic %21
  __range_ok_0:
    func.return %14
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 100
    x64.mov rcx, 1
    x64.mov rdx, 2
    x64.mov rbx, 3
    x64.mov rsi, 4
    x64.mov rdi, 5
    x64.mov r8, 6
    x64.mov r9, 7
    x64.sub rax, rcx
    x64.sub rax, rdx
    x64.sub rax, rbx
    x64.sub rax, rsi
    x64.sub rax, rdi
    x64.sub rax, r8
    x64.sub rax, r9
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
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
    %8 = maxon.binop %0, %1 {op = sub}
    %9 = maxon.binop %8, %2 {op = sub}
    %10 = maxon.binop %9, %3 {op = sub}
    %11 = maxon.binop %10, %4 {op = sub}
    %12 = maxon.binop %11, %5 {op = sub}
    %13 = maxon.binop %12, %6 {op = sub}
    %14 = maxon.binop %13, %7 {op = sub}
    %15 = maxon.literal {value = 0 : i64}
    %16 = maxon.binop %14, %15 {op = lt}
    %17 = maxon.literal {value = 4294967295 : i64}
    %18 = maxon.binop %14, %17 {op = gt}
    %19 = maxon.binop %16, %18 {op = or}
    maxon.cond_br %19 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-subtraction-high-pressure.test:11: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end [a, b, c, d, e, f, g, h]
    maxon.return %14
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 100 : i64}
    %1 = arith.constant {value = 1 : i64}
    %2 = arith.constant {value = 2 : i64}
    %3 = arith.constant {value = 3 : i64}
    %4 = arith.constant {value = 4 : i64}
    %5 = arith.constant {value = 5 : i64}
    %6 = arith.constant {value = 6 : i64}
    %7 = arith.constant {value = 7 : i64}
    %8 = arith.subi %0, %1
    %9 = arith.subi %8, %2
    %10 = arith.subi %9, %3
    %11 = arith.subi %10, %4
    %12 = arith.subi %11, %5
    %13 = arith.subi %12, %6
    %14 = arith.subi %13, %7
    %15 = arith.constant {value = 0 : i64}
    %16 = arith.cmpi lt %14, %15
    %17 = arith.constant {value = 4294967295 : i64}
    %18 = arith.cmpi gt %14, %17
    %19 = arith.ori1 %16, %18
    cf.cond_br %19 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %20 = memref.lea_symdata __panic_msg_0
    %21 = std.ptr_to_i64 %20
    std.call_runtime @maxon_panic %21
  __range_ok_0:
    func.return %14
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #100
    arm64.mov x1, #1
    arm64.mov x2, #2
    arm64.mov x3, #3
    arm64.mov x4, #4
    arm64.mov x5, #5
    arm64.mov x6, #6
    arm64.mov x7, #7
    arm64.sub x8, x0, x1
    arm64.sub x9, x8, x2
    arm64.sub x10, x9, x3
    arm64.sub x11, x10, x4
    arm64.sub x12, x11, x5
    arm64.sub x13, x12, x6
    arm64.sub x14, x13, x7
    arm64.mov x15, #0
    arm64.cmp x14, x15
    arm64.cset x0, lt
    arm64.mov x1, #4294967295
    arm64.cmp x14, x1
    arm64.cset x2, gt
    arm64.orr x1, x0, x2
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.mov x0, x14
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

<!-- test: int-multi-var-branch-merge -->
```maxon
function main() returns ExitCode
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
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = z} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.literal {value = 2 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    maxon.cond_br %5 [then: branch_0, else: other_0]
  branch_0:
    %6 = maxon.literal {value = 10 : i64}
    maxon.assign %6 {var = x} {kind = i64} {mut = 1 : i1}
    %7 = maxon.literal {value = 20 : i64}
    maxon.assign %7 {var = y} {kind = i64} {mut = 1 : i1}
    %8 = maxon.literal {value = 12 : i64}
    maxon.assign %8 {var = z} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br branch_0.merge
  other_0:
    %9 = maxon.literal {value = 1 : i64}
    maxon.assign %9 {var = x} {kind = i64} {mut = 1 : i1}
    %10 = maxon.literal {value = 2 : i64}
    maxon.assign %10 {var = y} {kind = i64} {mut = 1 : i1}
    %11 = maxon.literal {value = 3 : i64}
    maxon.assign %11 {var = z} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br branch_0.merge
  branch_0.merge:
    %12 = maxon.var_ref {var = x} {type = i64}
    %13 = maxon.var_ref {var = y} {type = i64}
    %14 = maxon.binop %12, %13 {op = add}
    %15 = maxon.var_ref {var = z} {type = i64}
    %16 = maxon.binop %14, %15 {op = add}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %16, %17 {op = lt}
    %19 = maxon.literal {value = 4294967295 : i64}
    %20 = maxon.binop %16, %19 {op = gt}
    %21 = maxon.binop %18, %20 {op = or}
    maxon.cond_br %21 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at int-multi-var-branch-merge.test:15: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, y, z]
    maxon.return %16
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.constant {value = 2 : i64}
    %5 = arith.cmpi lt %3, %4
    cf.cond_br %5 [then: branch_0, else: other_0]
  branch_0:
    %6 = arith.constant {value = 10 : i64}
    memref.store %6, x
    %7 = arith.constant {value = 20 : i64}
    memref.store %7, y
    %8 = arith.constant {value = 12 : i64}
    memref.store %8, z
    cf.br branch_0.merge
  other_0:
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
    %17 = arith.constant {value = 0 : i64}
    %18 = arith.cmpi lt %16, %17
    %19 = arith.constant {value = 4294967295 : i64}
    %20 = arith.cmpi gt %16, %19
    %21 = arith.ori1 %18, %20
    cf.cond_br %21 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %22 = memref.lea_symdata __panic_msg_0
    %23 = std.ptr_to_i64 %22
    std.call_runtime @mrt_panic %23
  __range_ok_0:
    func.return %16
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.mov rax, 1
    x64.mov rcx, 2
    x64.cmp rax, rcx
    x64.jge main.other_0
  branch_0:
    x64.mov rax, 10
    x64.mov [rbp-8], rax
    x64.mov rcx, 20
    x64.mov [rbp-16], rcx
    x64.mov rdx, 12
    x64.mov [rbp-24], rdx
    x64.jmp main.branch_0.merge
  other_0:
    x64.mov rax, 1
    x64.mov [rbp-8], rax
    x64.mov rcx, 2
    x64.mov [rbp-16], rcx
    x64.mov rdx, 3
    x64.mov [rbp-24], rdx
    x64.jmp main.branch_0.merge
  branch_0.merge:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.add rax, rcx
    x64.mov rdx, [rbp-24]
    x64.add rax, rdx
    x64.xor ebx, ebx
    x64.mov esi, 4294967295
    x64.cmp rax, rsi
    x64.jg main.__range_panic_0
    x64.cmp rax, rbx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %2 = maxon.literal {value = 0 : i64}
    maxon.assign %2 {var = z} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.literal {value = 2 : i64}
    %5 = maxon.binop %3, %4 {op = lt}
    maxon.cond_br %5 [then: branch_0, else: other_1]
  branch_0:
    %6 = maxon.literal {value = 10 : i64}
    maxon.assign %6 {var = x} {kind = i64} {mut = 1 : i1}
    %7 = maxon.literal {value = 20 : i64}
    maxon.assign %7 {var = y} {kind = i64} {mut = 1 : i1}
    %8 = maxon.literal {value = 12 : i64}
    maxon.assign %8 {var = z} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br branch_0.merge
  other_1:
    %9 = maxon.literal {value = 1 : i64}
    maxon.assign %9 {var = x} {kind = i64} {mut = 1 : i1}
    %10 = maxon.literal {value = 2 : i64}
    maxon.assign %10 {var = y} {kind = i64} {mut = 1 : i1}
    %11 = maxon.literal {value = 3 : i64}
    maxon.assign %11 {var = z} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br branch_0.merge
  branch_0.merge:
    %12 = maxon.var_ref {var = x} {type = i64}
    %13 = maxon.var_ref {var = y} {type = i64}
    %14 = maxon.binop %12, %13 {op = add}
    %15 = maxon.var_ref {var = z} {type = i64}
    %16 = maxon.binop %14, %15 {op = add}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %16, %17 {op = lt}
    %19 = maxon.literal {value = 4294967295 : i64}
    %20 = maxon.binop %16, %19 {op = gt}
    %21 = maxon.binop %18, %20 {op = or}
    maxon.cond_br %21 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at int-multi-var-branch-merge.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    maxon.scope_end [x, y, z]
    maxon.return %16
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
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
    %17 = arith.constant {value = 0 : i64}
    %18 = arith.cmpi lt %16, %17
    %19 = arith.constant {value = 4294967295 : i64}
    %20 = arith.cmpi gt %16, %19
    %21 = arith.ori1 %18, %20
    cf.cond_br %21 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %22 = memref.lea_symdata __panic_msg_0
    %23 = std.ptr_to_i64 %22
    std.call_runtime @maxon_panic %23
  __range_ok_2:
    func.return %16
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=80
    arm64.mov x0, #1
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.branch_0
    arm64.b main.other_1
  branch_0:
    arm64.mov x0, #10
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #20
    arm64.str x1, [x29, #-16]
    arm64.mov x2, #12
    arm64.str x2, [x29, #-24]
    arm64.b main.branch_0.merge
  other_1:
    arm64.mov x0, #1
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #2
    arm64.str x1, [x29, #-16]
    arm64.mov x2, #3
    arm64.str x2, [x29, #-24]
    arm64.b main.branch_0.merge
  branch_0.merge:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x0, x1
    arm64.ldr x3, [x29, #-24]
    arm64.add x4, x2, x3
    arm64.mov x5, #0
    arm64.cmp x4, x5
    arm64.cset x6, lt
    arm64.mov x7, #4294967295
    arm64.cmp x4, x7
    arm64.cset x8, gt
    arm64.orr x9, x6, x8
    arm64.cmp x9, #0
    arm64.b.ne main.__range_panic_2
    arm64.b main.__range_ok_2
  __range_panic_2:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_2:
    arm64.mov x0, x4
    arm64.epilogue stack_size=80
    arm64.ret
  }
}
```

### Level 7: Match Statements and Expressions

<!-- test: match-statement-simple -->
```maxon
function main() returns ExitCode
	let x = 2
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
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 2 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1}
    maxon.assign %0 {var = __match_check_0} {kind = i64} {decl = 1 : i1}
    maxon.br check_0.cmp0
  check_0.cmp0:
    %1 = maxon.var_ref {var = __match_check_0} {type = i64}
    %2 = maxon.literal {value = 1 : i64}
    %3 = maxon.binop %1, %2 {op = eq}
    maxon.cond_br %3 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %4 = maxon.literal {value = 10 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %4
  check_0.cmp1:
    %5 = maxon.var_ref {var = __match_check_0} {type = i64}
    %6 = maxon.literal {value = 2 : i64}
    %7 = maxon.binop %5, %6 {op = eq}
    maxon.cond_br %7 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %8 = maxon.literal {value = 20 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %8
  check_0.case2:
    %9 = maxon.literal {value = 0 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %9
  check_0.merge:
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 2 : i64}
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
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 2
    x64.mov [rbp-8], rax
    x64.jmp main.check_0.cmp0
  check_0.cmp0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 1
    x64.cmp rax, rcx
    x64.jne main.check_0.cmp1
  check_0.case0:
    x64.mov rax, 10
    x64.epilogue
    x64.ret
  check_0.cmp1:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 2
    x64.cmp rax, rcx
    x64.jne main.check_0.case2
  check_0.case1:
    x64.mov rax, 20
    x64.epilogue
    x64.ret
  check_0.case2:
    x64.xor eax, eax
    x64.epilogue
    x64.ret
  check_0.merge:
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 2 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_check_0} {kind = i64} {decl = 1 : i1}
    maxon.br check_0.cmp0
  check_0.cmp0:
    %1 = maxon.var_ref {var = __match_check_0} {type = i64}
    %2 = maxon.literal {value = 1 : i64}
    %3 = maxon.binop %1, %2 {op = eq}
    maxon.cond_br %3 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %4 = maxon.literal {value = 10 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %4
  check_0.cmp1:
    %5 = maxon.var_ref {var = __match_check_0} {type = i64}
    %6 = maxon.literal {value = 2 : i64}
    %7 = maxon.binop %5, %6 {op = eq}
    maxon.cond_br %7 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %8 = maxon.literal {value = 20 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %8
  check_0.case2:
    %9 = maxon.literal {value = 0 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %9
  check_0.merge:
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 2 : i64}
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
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #2
    arm64.str x0, [x29, #-8]
    arm64.b main.check_0.cmp0
  check_0.cmp0:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.check_0.case0
    arm64.b main.check_0.cmp1
  check_0.case0:
    arm64.mov x0, #10
    arm64.epilogue stack_size=48
    arm64.ret
  check_0.cmp1:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.check_0.case1
    arm64.b main.check_0.case2
  check_0.case1:
    arm64.mov x0, #20
    arm64.epilogue stack_size=48
    arm64.ret
  check_0.case2:
    arm64.mov x0, #0
    arm64.epilogue stack_size=48
    arm64.ret
  check_0.merge:
  }
}
```

<!-- test: match-statement-assignment -->
```maxon
function main() returns ExitCode
	let x = 2
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
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 2 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_process_0} {kind = i64} {decl = 1 : i1}
    maxon.br process_0.cmp0
  process_0.cmp0:
    %2 = maxon.var_ref {var = __match_process_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq}
    maxon.cond_br %4 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    %5 = maxon.literal {value = 100 : i64}
    maxon.assign %5 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.cmp1:
    %6 = maxon.var_ref {var = __match_process_0} {type = i64}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = eq}
    maxon.cond_br %8 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    %9 = maxon.literal {value = 200 : i64}
    maxon.assign %9 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.case2:
    %10 = maxon.literal {value = 0 : i64}
    maxon.assign %10 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.merge:
    %11 = maxon.var_ref {var = result} {type = i64}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %11, %12 {op = lt}
    %14 = maxon.literal {value = 4294967295 : i64}
    %15 = maxon.binop %11, %14 {op = gt}
    %16 = maxon.binop %13, %15 {op = or}
    maxon.cond_br %16 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at match-statement-assignment.test:10: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, result, __match_process_0]
    maxon.return %11
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 2 : i64}
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
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.cmpi lt %11, %12
    %14 = arith.constant {value = 4294967295 : i64}
    %15 = arith.cmpi gt %11, %14
    %16 = arith.ori1 %13, %15
    cf.cond_br %16 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %17 = memref.lea_symdata __panic_msg_0
    %18 = std.ptr_to_i64 %17
    std.call_runtime @mrt_panic %18
  __range_ok_0:
    func.return %11
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 2
    x64.mov [rbp-8], rax
    x64.jmp main.process_0.cmp0
  process_0.cmp0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 1
    x64.cmp rax, rcx
    x64.jne main.process_0.cmp1
  process_0.case0:
    x64.mov rax, 100
    x64.mov [rbp-16], rax
    x64.jmp main.process_0.merge
  process_0.cmp1:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 2
    x64.cmp rax, rcx
    x64.jne main.process_0.case2
  process_0.case1:
    x64.mov rax, 200
    x64.mov [rbp-16], rax
    x64.jmp main.process_0.merge
  process_0.case2:
    x64.xor eax, eax
    x64.mov [rbp-16], rax
    x64.jmp main.process_0.merge
  process_0.merge:
    x64.mov rax, [rbp-16]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
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
    %4 = maxon.binop %2, %3 {op = eq}
    maxon.cond_br %4 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    %5 = maxon.literal {value = 100 : i64}
    maxon.assign %5 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.cmp1:
    %6 = maxon.var_ref {var = __match_process_0} {type = i64}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = eq}
    maxon.cond_br %8 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    %9 = maxon.literal {value = 200 : i64}
    maxon.assign %9 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.case2:
    %10 = maxon.literal {value = 0 : i64}
    maxon.assign %10 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.merge:
    %11 = maxon.var_ref {var = result} {type = i64}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %11, %12 {op = lt}
    %14 = maxon.literal {value = 4294967295 : i64}
    %15 = maxon.binop %11, %14 {op = gt}
    %16 = maxon.binop %13, %15 {op = or}
    maxon.cond_br %16 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-statement-assignment.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [x, result, __match_process_0]
    maxon.return %11
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 2 : i64}
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
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.cmpi lt %11, %12
    %14 = arith.constant {value = 4294967295 : i64}
    %15 = arith.cmpi gt %11, %14
    %16 = arith.ori1 %13, %15
    cf.cond_br %16 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %17 = memref.lea_symdata __panic_msg_0
    %18 = std.ptr_to_i64 %17
    std.call_runtime @maxon_panic %18
  __range_ok_1:
    func.return %11
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #2
    arm64.str x0, [x29, #-8]
    arm64.b main.process_0.cmp0
  process_0.cmp0:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.process_0.case0
    arm64.b main.process_0.cmp1
  process_0.case0:
    arm64.mov x0, #100
    arm64.str x0, [x29, #-16]
    arm64.b main.process_0.merge
  process_0.cmp1:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.process_0.case1
    arm64.b main.process_0.case2
  process_0.case1:
    arm64.mov x0, #200
    arm64.str x0, [x29, #-16]
    arm64.b main.process_0.merge
  process_0.case2:
    arm64.mov x0, #0
    arm64.str x0, [x29, #-16]
    arm64.b main.process_0.merge
  process_0.merge:
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: match-statement-or-patterns -->
```maxon
function main() returns ExitCode
	let x = 3
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
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1}
    maxon.assign %0 {var = __match_check_0} {kind = i64} {decl = 1 : i1}
    maxon.br check_0.cmp0
  check_0.cmp0:
    %1 = maxon.var_ref {var = __match_check_0} {type = i64}
    %2 = maxon.literal {value = 1 : i64}
    %3 = maxon.binop %1, %2 {op = eq}
    %4 = maxon.var_ref {var = __match_check_0} {type = i64}
    %5 = maxon.literal {value = 2 : i64}
    %6 = maxon.binop %4, %5 {op = eq}
    %7 = maxon.binop %3, %6 {op = or}
    maxon.cond_br %7 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %8 = maxon.literal {value = 10 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %8
  check_0.cmp1:
    %9 = maxon.var_ref {var = __match_check_0} {type = i64}
    %10 = maxon.literal {value = 3 : i64}
    %11 = maxon.binop %9, %10 {op = eq}
    %12 = maxon.var_ref {var = __match_check_0} {type = i64}
    %13 = maxon.literal {value = 4 : i64}
    %14 = maxon.binop %12, %13 {op = eq}
    %15 = maxon.binop %11, %14 {op = or}
    maxon.cond_br %15 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %16 = maxon.literal {value = 20 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %16
  check_0.case2:
    %17 = maxon.literal {value = 0 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %17
  check_0.merge:
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 3 : i64}
    memref.store %0, __match_check_0
    cf.br check_0.cmp0
  check_0.cmp0:
    %1 = memref.load __match_check_0 : i64
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.cmpi eq %1, %2
    %4 = memref.load __match_check_0 : i64
    %5 = arith.constant {value = 2 : i64}
    %6 = arith.cmpi eq %4, %5
    %7 = arith.ori1 %3, %6
    cf.cond_br %7 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %8 = arith.constant {value = 10 : i64}
    func.return %8
  check_0.cmp1:
    %9 = memref.load __match_check_0 : i64
    %10 = arith.constant {value = 3 : i64}
    %11 = arith.cmpi eq %9, %10
    %12 = memref.load __match_check_0 : i64
    %13 = arith.constant {value = 4 : i64}
    %14 = arith.cmpi eq %12, %13
    %15 = arith.ori1 %11, %14
    cf.cond_br %15 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %16 = arith.constant {value = 20 : i64}
    func.return %16
  check_0.case2:
    %17 = arith.constant {value = 0 : i64}
    func.return %17
  check_0.merge:
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 3
    x64.mov [rbp-8], rax
    x64.jmp main.check_0.cmp0
  check_0.cmp0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 1
    x64.mov rdx, [rbp-8]
    x64.mov rbx, 2
    x64.cmp rdx, rbx
    x64.je main.check_0.case0
    x64.cmp rax, rcx
    x64.je main.check_0.case0
    x64.jmp main.check_0.cmp1
  check_0.case0:
    x64.mov rax, 10
    x64.epilogue
    x64.ret
  check_0.cmp1:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 3
    x64.mov rdx, [rbp-8]
    x64.mov rbx, 4
    x64.cmp rdx, rbx
    x64.je main.check_0.case1
    x64.cmp rax, rcx
    x64.je main.check_0.case1
    x64.jmp main.check_0.case2
  check_0.case1:
    x64.mov rax, 20
    x64.epilogue
    x64.ret
  check_0.case2:
    x64.xor eax, eax
    x64.epilogue
    x64.ret
  check_0.merge:
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_check_0} {kind = i64} {decl = 1 : i1}
    maxon.br check_0.cmp0
  check_0.cmp0:
    %1 = maxon.var_ref {var = __match_check_0} {type = i64}
    %2 = maxon.literal {value = 1 : i64}
    %3 = maxon.binop %1, %2 {op = eq}
    %4 = maxon.var_ref {var = __match_check_0} {type = i64}
    %5 = maxon.literal {value = 2 : i64}
    %6 = maxon.binop %4, %5 {op = eq}
    %7 = maxon.binop %3, %6 {op = or}
    maxon.cond_br %7 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %8 = maxon.literal {value = 10 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %8
  check_0.cmp1:
    %9 = maxon.var_ref {var = __match_check_0} {type = i64}
    %10 = maxon.literal {value = 3 : i64}
    %11 = maxon.binop %9, %10 {op = eq}
    %12 = maxon.var_ref {var = __match_check_0} {type = i64}
    %13 = maxon.literal {value = 4 : i64}
    %14 = maxon.binop %12, %13 {op = eq}
    %15 = maxon.binop %11, %14 {op = or}
    maxon.cond_br %15 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %16 = maxon.literal {value = 20 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %16
  check_0.case2:
    %17 = maxon.literal {value = 0 : i64}
    maxon.scope_end [x, __match_check_0]
    maxon.return %17
  check_0.merge:
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 3 : i64}
    memref.store %0, __match_check_0
    cf.br check_0.cmp0
  check_0.cmp0:
    %1 = memref.load __match_check_0 : i64
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.cmpi eq %1, %2
    %4 = memref.load __match_check_0 : i64
    %5 = arith.constant {value = 2 : i64}
    %6 = arith.cmpi eq %4, %5
    %7 = arith.ori1 %3, %6
    cf.cond_br %7 [then: check_0.case0, else: check_0.cmp1]
  check_0.case0:
    %8 = arith.constant {value = 10 : i64}
    func.return %8
  check_0.cmp1:
    %9 = memref.load __match_check_0 : i64
    %10 = arith.constant {value = 3 : i64}
    %11 = arith.cmpi eq %9, %10
    %12 = memref.load __match_check_0 : i64
    %13 = arith.constant {value = 4 : i64}
    %14 = arith.cmpi eq %12, %13
    %15 = arith.ori1 %11, %14
    cf.cond_br %15 [then: check_0.case1, else: check_0.case2]
  check_0.case1:
    %16 = arith.constant {value = 20 : i64}
    func.return %16
  check_0.case2:
    %17 = arith.constant {value = 0 : i64}
    func.return %17
  check_0.merge:
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #3
    arm64.str x0, [x29, #-8]
    arm64.b main.check_0.cmp0
  check_0.cmp0:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.ldr x3, [x29, #-8]
    arm64.mov x4, #2
    arm64.cmp x3, x4
    arm64.cset x5, eq
    arm64.orr x6, x2, x5
    arm64.cmp x6, #0
    arm64.b.ne main.check_0.case0
    arm64.b main.check_0.cmp1
  check_0.case0:
    arm64.mov x0, #10
    arm64.epilogue stack_size=48
    arm64.ret
  check_0.cmp1:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #3
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.ldr x3, [x29, #-8]
    arm64.mov x4, #4
    arm64.cmp x3, x4
    arm64.cset x5, eq
    arm64.orr x6, x2, x5
    arm64.cmp x6, #0
    arm64.b.ne main.check_0.case1
    arm64.b main.check_0.case2
  check_0.case1:
    arm64.mov x0, #20
    arm64.epilogue stack_size=48
    arm64.ret
  check_0.case2:
    arm64.mov x0, #0
    arm64.epilogue stack_size=48
    arm64.ret
  check_0.merge:
  }
}
```

<!-- test: match-statement-fallthrough -->
```maxon
function main() returns ExitCode
	let x = 1
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
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_cascade_0} {kind = i64} {decl = 1 : i1}
    maxon.br cascade_0.cmp0
  cascade_0.cmp0:
    %2 = maxon.var_ref {var = __match_cascade_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq}
    maxon.cond_br %4 [then: cascade_0.case0, else: cascade_0.cmp1]
  cascade_0.case0:
    %5 = maxon.literal {value = 10 : i64}
    %6 = maxon.var_ref {var = result} {type = i64}
    %7 = maxon.binop %6, %5 {op = add}
    maxon.assign %7 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br cascade_0.case1
  cascade_0.cmp1:
    %8 = maxon.var_ref {var = __match_cascade_0} {type = i64}
    %9 = maxon.literal {value = 2 : i64}
    %10 = maxon.binop %8, %9 {op = eq}
    maxon.cond_br %10 [then: cascade_0.case1, else: cascade_0.cmp2]
  cascade_0.case1:
    %11 = maxon.literal {value = 20 : i64}
    %12 = maxon.var_ref {var = result} {type = i64}
    %13 = maxon.binop %12, %11 {op = add}
    maxon.assign %13 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br cascade_0.case2
  cascade_0.cmp2:
    %14 = maxon.var_ref {var = __match_cascade_0} {type = i64}
    %15 = maxon.literal {value = 3 : i64}
    %16 = maxon.binop %14, %15 {op = eq}
    maxon.cond_br %16 [then: cascade_0.case2, else: cascade_0.case3]
  cascade_0.case2:
    %17 = maxon.literal {value = 30 : i64}
    %18 = maxon.var_ref {var = result} {type = i64}
    %19 = maxon.binop %18, %17 {op = add}
    maxon.assign %19 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br cascade_0.merge
  cascade_0.case3:
    %20 = maxon.literal {value = 100 : i64}
    maxon.assign %20 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br cascade_0.merge
  cascade_0.merge:
    %21 = maxon.var_ref {var = result} {type = i64}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at match-statement-fallthrough.test:11: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, result, __match_cascade_0]
    maxon.return %21
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 1 : i64}
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
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi lt %21, %22
    %24 = arith.constant {value = 4294967295 : i64}
    %25 = arith.cmpi gt %21, %24
    %26 = arith.ori1 %23, %25
    cf.cond_br %26 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %27 = memref.lea_symdata __panic_msg_0
    %28 = std.ptr_to_i64 %27
    std.call_runtime @mrt_panic %28
  __range_ok_0:
    func.return %21
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 1
    x64.xor ecx, ecx
    x64.mov [rbp-8], rcx
    x64.mov [rbp-16], rax
    x64.jmp main.cascade_0.cmp0
  cascade_0.cmp0:
    x64.mov rax, [rbp-16]
    x64.mov rcx, 1
    x64.cmp rax, rcx
    x64.jne main.cascade_0.cmp1
  cascade_0.case0:
    x64.mov rax, 10
    x64.mov rcx, [rbp-8]
    x64.add rcx, rax
    x64.mov [rbp-8], rcx
    x64.jmp main.cascade_0.case1
  cascade_0.cmp1:
    x64.mov rax, [rbp-16]
    x64.mov rcx, 2
    x64.cmp rax, rcx
    x64.jne main.cascade_0.cmp2
  cascade_0.case1:
    x64.mov rax, 20
    x64.mov rcx, [rbp-8]
    x64.add rcx, rax
    x64.mov [rbp-8], rcx
    x64.jmp main.cascade_0.case2
  cascade_0.cmp2:
    x64.mov rax, [rbp-16]
    x64.mov rcx, 3
    x64.cmp rax, rcx
    x64.jne main.cascade_0.case3
  cascade_0.case2:
    x64.mov rax, 30
    x64.mov rcx, [rbp-8]
    x64.add rcx, rax
    x64.mov [rbp-8], rcx
    x64.jmp main.cascade_0.merge
  cascade_0.case3:
    x64.mov rax, 100
    x64.mov [rbp-8], rax
    x64.jmp main.cascade_0.merge
  cascade_0.merge:
    x64.mov rax, [rbp-8]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
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
    %4 = maxon.binop %2, %3 {op = eq}
    maxon.cond_br %4 [then: cascade_0.case0, else: cascade_0.cmp1]
  cascade_0.case0:
    %5 = maxon.literal {value = 10 : i64}
    %6 = maxon.var_ref {var = result} {type = i64}
    %7 = maxon.binop %6, %5 {op = add}
    maxon.assign %7 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br cascade_0.case1
  cascade_0.cmp1:
    %8 = maxon.var_ref {var = __match_cascade_0} {type = i64}
    %9 = maxon.literal {value = 2 : i64}
    %10 = maxon.binop %8, %9 {op = eq}
    maxon.cond_br %10 [then: cascade_0.case1, else: cascade_0.cmp2]
  cascade_0.case1:
    %11 = maxon.literal {value = 20 : i64}
    %12 = maxon.var_ref {var = result} {type = i64}
    %13 = maxon.binop %12, %11 {op = add}
    maxon.assign %13 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br cascade_0.case2
  cascade_0.cmp2:
    %14 = maxon.var_ref {var = __match_cascade_0} {type = i64}
    %15 = maxon.literal {value = 3 : i64}
    %16 = maxon.binop %14, %15 {op = eq}
    maxon.cond_br %16 [then: cascade_0.case2, else: cascade_0.case3]
  cascade_0.case2:
    %17 = maxon.literal {value = 30 : i64}
    %18 = maxon.var_ref {var = result} {type = i64}
    %19 = maxon.binop %18, %17 {op = add}
    maxon.assign %19 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br cascade_0.merge
  cascade_0.case3:
    %20 = maxon.literal {value = 100 : i64}
    maxon.assign %20 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br cascade_0.merge
  cascade_0.merge:
    %21 = maxon.var_ref {var = result} {type = i64}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-statement-fallthrough.test:11: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [x, result, __match_cascade_0]
    maxon.return %21
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 1 : i64}
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
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi lt %21, %22
    %24 = arith.constant {value = 4294967295 : i64}
    %25 = arith.cmpi gt %21, %24
    %26 = arith.ori1 %23, %25
    cf.cond_br %26 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %27 = memref.lea_symdata __panic_msg_0
    %28 = std.ptr_to_i64 %27
    std.call_runtime @maxon_panic %28
  __range_ok_1:
    func.return %21
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #1
    arm64.mov x1, #0
    arm64.str x1, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.b main.cascade_0.cmp0
  cascade_0.cmp0:
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #1
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.cascade_0.case0
    arm64.b main.cascade_0.cmp1
  cascade_0.case0:
    arm64.mov x0, #10
    arm64.ldr x1, [x29, #-8]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-8]
    arm64.b main.cascade_0.case1
  cascade_0.cmp1:
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.cascade_0.case1
    arm64.b main.cascade_0.cmp2
  cascade_0.case1:
    arm64.mov x0, #20
    arm64.ldr x1, [x29, #-8]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-8]
    arm64.b main.cascade_0.case2
  cascade_0.cmp2:
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #3
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.cascade_0.case2
    arm64.b main.cascade_0.case3
  cascade_0.case2:
    arm64.mov x0, #30
    arm64.ldr x1, [x29, #-8]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-8]
    arm64.b main.cascade_0.merge
  cascade_0.case3:
    arm64.mov x0, #100
    arm64.str x0, [x29, #-8]
    arm64.b main.cascade_0.merge
  cascade_0.merge:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: match-expression-basic -->
```maxon
function main() returns ExitCode
	let x = 2
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
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 2 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = __matchexpr_eval_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_eval_0} {kind = i64} {decl = 1 : i1}
    maxon.br eval_0.cmp0
  eval_0.cmp0:
    %2 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq}
    maxon.cond_br %4 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %5 = maxon.literal {value = 10 : i64}
    maxon.assign %5 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %6 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = eq}
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
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %11, %12 {op = lt}
    %14 = maxon.literal {value = 4294967295 : i64}
    %15 = maxon.binop %11, %14 {op = gt}
    %16 = maxon.binop %13, %15 {op = or}
    maxon.cond_br %16 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at match-expression-basic.test:9: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, __matchexpr_eval_0, result, __match_eval_0]
    maxon.return %11
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 2 : i64}
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
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.cmpi lt %11, %12
    %14 = arith.constant {value = 4294967295 : i64}
    %15 = arith.cmpi gt %11, %14
    %16 = arith.ori1 %13, %15
    cf.cond_br %16 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %17 = memref.lea_symdata __panic_msg_0
    %18 = std.ptr_to_i64 %17
    std.call_runtime @mrt_panic %18
  __range_ok_0:
    func.return %11
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 2
    x64.mov [rbp-8], rax
    x64.jmp main.eval_0.cmp0
  eval_0.cmp0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 1
    x64.cmp rax, rcx
    x64.jne main.eval_0.cmp1
  eval_0.case0:
    x64.mov rax, 10
    x64.mov [rbp-16], rax
    x64.jmp main.eval_0.merge
  eval_0.cmp1:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 2
    x64.cmp rax, rcx
    x64.jne main.eval_0.case2
  eval_0.case1:
    x64.mov rax, 20
    x64.mov [rbp-16], rax
    x64.jmp main.eval_0.merge
  eval_0.case2:
    x64.xor eax, eax
    x64.mov [rbp-16], rax
    x64.jmp main.eval_0.merge
  eval_0.merge:
    x64.mov rax, [rbp-16]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
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
    %4 = maxon.binop %2, %3 {op = eq}
    maxon.cond_br %4 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %5 = maxon.literal {value = 10 : i64}
    maxon.assign %5 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %6 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = eq}
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
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %11, %12 {op = lt}
    %14 = maxon.literal {value = 4294967295 : i64}
    %15 = maxon.binop %11, %14 {op = gt}
    %16 = maxon.binop %13, %15 {op = or}
    maxon.cond_br %16 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-expression-basic.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [x, __matchexpr_eval_0, result, __match_eval_0]
    maxon.return %11
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 2 : i64}
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
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.cmpi lt %11, %12
    %14 = arith.constant {value = 4294967295 : i64}
    %15 = arith.cmpi gt %11, %14
    %16 = arith.ori1 %13, %15
    cf.cond_br %16 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %17 = memref.lea_symdata __panic_msg_0
    %18 = std.ptr_to_i64 %17
    std.call_runtime @maxon_panic %18
  __range_ok_1:
    func.return %11
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #2
    arm64.str x0, [x29, #-8]
    arm64.b main.eval_0.cmp0
  eval_0.cmp0:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.eval_0.case0
    arm64.b main.eval_0.cmp1
  eval_0.case0:
    arm64.mov x0, #10
    arm64.str x0, [x29, #-16]
    arm64.b main.eval_0.merge
  eval_0.cmp1:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.eval_0.case1
    arm64.b main.eval_0.case2
  eval_0.case1:
    arm64.mov x0, #20
    arm64.str x0, [x29, #-16]
    arm64.b main.eval_0.merge
  eval_0.case2:
    arm64.mov x0, #0
    arm64.str x0, [x29, #-16]
    arm64.b main.eval_0.merge
  eval_0.merge:
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: match-expression-or-patterns -->
```maxon
function main() returns ExitCode
	let x = 4
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
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 4 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = __matchexpr_eval_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_eval_0} {kind = i64} {decl = 1 : i1}
    maxon.br eval_0.cmp0
  eval_0.cmp0:
    %2 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq}
    %5 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %6 = maxon.literal {value = 2 : i64}
    %7 = maxon.binop %5, %6 {op = eq}
    %8 = maxon.binop %4, %7 {op = or}
    maxon.cond_br %8 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %9 = maxon.literal {value = 10 : i64}
    maxon.assign %9 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %10 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %11 = maxon.literal {value = 3 : i64}
    %12 = maxon.binop %10, %11 {op = eq}
    %13 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %14 = maxon.literal {value = 4 : i64}
    %15 = maxon.binop %13, %14 {op = eq}
    %16 = maxon.binop %12, %15 {op = or}
    maxon.cond_br %16 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %17 = maxon.literal {value = 20 : i64}
    maxon.assign %17 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.case2:
    %18 = maxon.literal {value = 0 : i64}
    maxon.assign %18 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.merge:
    %19 = maxon.var_ref {var = __matchexpr_eval_0} {type = i64}
    maxon.assign %19 {var = result} {kind = i64} {decl = 1 : i1}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %19, %20 {op = lt}
    %22 = maxon.literal {value = 4294967295 : i64}
    %23 = maxon.binop %19, %22 {op = gt}
    %24 = maxon.binop %21, %23 {op = or}
    maxon.cond_br %24 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at match-expression-or-patterns.test:9: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, __matchexpr_eval_0, result, __match_eval_0]
    maxon.return %19
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 4 : i64}
    memref.store %0, __match_eval_0
    cf.br eval_0.cmp0
  eval_0.cmp0:
    %2 = memref.load __match_eval_0 : i64
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.cmpi eq %2, %3
    %5 = memref.load __match_eval_0 : i64
    %6 = arith.constant {value = 2 : i64}
    %7 = arith.cmpi eq %5, %6
    %8 = arith.ori1 %4, %7
    cf.cond_br %8 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %9 = arith.constant {value = 10 : i64}
    memref.store %9, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.cmp1:
    %10 = memref.load __match_eval_0 : i64
    %11 = arith.constant {value = 3 : i64}
    %12 = arith.cmpi eq %10, %11
    %13 = memref.load __match_eval_0 : i64
    %14 = arith.constant {value = 4 : i64}
    %15 = arith.cmpi eq %13, %14
    %16 = arith.ori1 %12, %15
    cf.cond_br %16 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %17 = arith.constant {value = 20 : i64}
    memref.store %17, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.case2:
    %18 = arith.constant {value = 0 : i64}
    memref.store %18, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.merge:
    %19 = memref.load __matchexpr_eval_0 : i64
    %20 = arith.constant {value = 0 : i64}
    %21 = arith.cmpi lt %19, %20
    %22 = arith.constant {value = 4294967295 : i64}
    %23 = arith.cmpi gt %19, %22
    %24 = arith.ori1 %21, %23
    cf.cond_br %24 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %25 = memref.lea_symdata __panic_msg_0
    %26 = std.ptr_to_i64 %25
    std.call_runtime @mrt_panic %26
  __range_ok_0:
    func.return %19
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 4
    x64.mov [rbp-8], rax
    x64.jmp main.eval_0.cmp0
  eval_0.cmp0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 1
    x64.mov rdx, [rbp-8]
    x64.mov rbx, 2
    x64.cmp rdx, rbx
    x64.je main.eval_0.case0
    x64.cmp rax, rcx
    x64.je main.eval_0.case0
    x64.jmp main.eval_0.cmp1
  eval_0.case0:
    x64.mov rax, 10
    x64.mov [rbp-16], rax
    x64.jmp main.eval_0.merge
  eval_0.cmp1:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 3
    x64.mov rdx, [rbp-8]
    x64.mov rbx, 4
    x64.cmp rdx, rbx
    x64.je main.eval_0.case1
    x64.cmp rax, rcx
    x64.je main.eval_0.case1
    x64.jmp main.eval_0.case2
  eval_0.case1:
    x64.mov rax, 20
    x64.mov [rbp-16], rax
    x64.jmp main.eval_0.merge
  eval_0.case2:
    x64.xor eax, eax
    x64.mov [rbp-16], rax
    x64.jmp main.eval_0.merge
  eval_0.merge:
    x64.mov rax, [rbp-16]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
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
    %4 = maxon.binop %2, %3 {op = eq}
    %5 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %6 = maxon.literal {value = 2 : i64}
    %7 = maxon.binop %5, %6 {op = eq}
    %8 = maxon.binop %4, %7 {op = or}
    maxon.cond_br %8 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %9 = maxon.literal {value = 10 : i64}
    maxon.assign %9 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %10 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %11 = maxon.literal {value = 3 : i64}
    %12 = maxon.binop %10, %11 {op = eq}
    %13 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %14 = maxon.literal {value = 4 : i64}
    %15 = maxon.binop %13, %14 {op = eq}
    %16 = maxon.binop %12, %15 {op = or}
    maxon.cond_br %16 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %17 = maxon.literal {value = 20 : i64}
    maxon.assign %17 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.case2:
    %18 = maxon.literal {value = 0 : i64}
    maxon.assign %18 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.merge:
    %19 = maxon.var_ref {var = __matchexpr_eval_0} {type = i64}
    maxon.assign %19 {var = result} {kind = i64} {decl = 1 : i1}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %19, %20 {op = lt}
    %22 = maxon.literal {value = 4294967295 : i64}
    %23 = maxon.binop %19, %22 {op = gt}
    %24 = maxon.binop %21, %23 {op = or}
    maxon.cond_br %24 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-expression-or-patterns.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [x, __matchexpr_eval_0, result, __match_eval_0]
    maxon.return %19
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 4 : i64}
    memref.store %0, __match_eval_0
    cf.br eval_0.cmp0
  eval_0.cmp0:
    %2 = memref.load __match_eval_0 : i64
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.cmpi eq %2, %3
    %5 = memref.load __match_eval_0 : i64
    %6 = arith.constant {value = 2 : i64}
    %7 = arith.cmpi eq %5, %6
    %8 = arith.ori1 %4, %7
    cf.cond_br %8 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %9 = arith.constant {value = 10 : i64}
    memref.store %9, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.cmp1:
    %10 = memref.load __match_eval_0 : i64
    %11 = arith.constant {value = 3 : i64}
    %12 = arith.cmpi eq %10, %11
    %13 = memref.load __match_eval_0 : i64
    %14 = arith.constant {value = 4 : i64}
    %15 = arith.cmpi eq %13, %14
    %16 = arith.ori1 %12, %15
    cf.cond_br %16 [then: eval_0.case1, else: eval_0.case2]
  eval_0.case1:
    %17 = arith.constant {value = 20 : i64}
    memref.store %17, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.case2:
    %18 = arith.constant {value = 0 : i64}
    memref.store %18, __matchexpr_eval_0
    cf.br eval_0.merge
  eval_0.merge:
    %19 = memref.load __matchexpr_eval_0 : i64
    %20 = arith.constant {value = 0 : i64}
    %21 = arith.cmpi lt %19, %20
    %22 = arith.constant {value = 4294967295 : i64}
    %23 = arith.cmpi gt %19, %22
    %24 = arith.ori1 %21, %23
    cf.cond_br %24 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %25 = memref.lea_symdata __panic_msg_0
    %26 = std.ptr_to_i64 %25
    std.call_runtime @maxon_panic %26
  __range_ok_1:
    func.return %19
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #4
    arm64.str x0, [x29, #-8]
    arm64.b main.eval_0.cmp0
  eval_0.cmp0:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.ldr x3, [x29, #-8]
    arm64.mov x4, #2
    arm64.cmp x3, x4
    arm64.cset x5, eq
    arm64.orr x6, x2, x5
    arm64.cmp x6, #0
    arm64.b.ne main.eval_0.case0
    arm64.b main.eval_0.cmp1
  eval_0.case0:
    arm64.mov x0, #10
    arm64.str x0, [x29, #-16]
    arm64.b main.eval_0.merge
  eval_0.cmp1:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #3
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.ldr x3, [x29, #-8]
    arm64.mov x4, #4
    arm64.cmp x3, x4
    arm64.cset x5, eq
    arm64.orr x6, x2, x5
    arm64.cmp x6, #0
    arm64.b.ne main.eval_0.case1
    arm64.b main.eval_0.case2
  eval_0.case1:
    arm64.mov x0, #20
    arm64.str x0, [x29, #-16]
    arm64.b main.eval_0.merge
  eval_0.case2:
    arm64.mov x0, #0
    arm64.str x0, [x29, #-16]
    arm64.b main.eval_0.merge
  eval_0.merge:
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: match-expression-in-arithmetic -->
```maxon
function main() returns ExitCode
	let x = 2
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
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 2 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    maxon.assign %1 {var = __matchexpr_eval_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %0 {var = __match_eval_0} {kind = i64} {decl = 1 : i1}
    maxon.br eval_0.cmp0
  eval_0.cmp0:
    %2 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %3 = maxon.literal {value = 1 : i64}
    %4 = maxon.binop %2, %3 {op = eq}
    maxon.cond_br %4 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %5 = maxon.literal {value = 10 : i64}
    maxon.assign %5 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %6 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = eq}
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
    %13 = maxon.binop %11, %12 {op = mul}
    maxon.assign %13 {var = doubled} {kind = i64} {decl = 1 : i1}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %13, %14 {op = lt}
    %16 = maxon.literal {value = 4294967295 : i64}
    %17 = maxon.binop %13, %16 {op = gt}
    %18 = maxon.binop %15, %17 {op = or}
    maxon.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at match-expression-in-arithmetic.test:9: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, __matchexpr_eval_0, doubled, __match_eval_0]
    maxon.return %13
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 2 : i64}
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
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @mrt_panic %20
  __range_ok_0:
    func.return %13
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 2
    x64.mov [rbp-8], rax
    x64.jmp main.eval_0.cmp0
  eval_0.cmp0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 1
    x64.cmp rax, rcx
    x64.jne main.eval_0.cmp1
  eval_0.case0:
    x64.mov rax, 10
    x64.mov [rbp-16], rax
    x64.jmp main.eval_0.merge
  eval_0.cmp1:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 2
    x64.cmp rax, rcx
    x64.jne main.eval_0.case2
  eval_0.case1:
    x64.mov rax, 20
    x64.mov [rbp-16], rax
    x64.jmp main.eval_0.merge
  eval_0.case2:
    x64.xor eax, eax
    x64.mov [rbp-16], rax
    x64.jmp main.eval_0.merge
  eval_0.merge:
    x64.mov rax, [rbp-16]
    x64.mov rcx, 2
    x64.imul rax, rcx
    x64.xor edx, edx
    x64.mov ebx, 4294967295
    x64.cmp rax, rbx
    x64.jg main.__range_panic_0
    x64.cmp rax, rdx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @main() -> i64 {
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
    %4 = maxon.binop %2, %3 {op = eq}
    maxon.cond_br %4 [then: eval_0.case0, else: eval_0.cmp1]
  eval_0.case0:
    %5 = maxon.literal {value = 10 : i64}
    maxon.assign %5 {var = __matchexpr_eval_0} {kind = i64} {mut = 1 : i1}
    maxon.br eval_0.merge
  eval_0.cmp1:
    %6 = maxon.var_ref {var = __match_eval_0} {type = i64}
    %7 = maxon.literal {value = 2 : i64}
    %8 = maxon.binop %6, %7 {op = eq}
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
    %13 = maxon.binop %11, %12 {op = mul}
    maxon.assign %13 {var = doubled} {kind = i64} {decl = 1 : i1}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.binop %13, %14 {op = lt}
    %16 = maxon.literal {value = 4294967295 : i64}
    %17 = maxon.binop %13, %16 {op = gt}
    %18 = maxon.binop %15, %17 {op = or}
    maxon.cond_br %18 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-expression-in-arithmetic.test:9: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [x, __matchexpr_eval_0, doubled, __match_eval_0]
    maxon.return %13
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 2 : i64}
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
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @maxon_panic %20
  __range_ok_1:
    func.return %13
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #2
    arm64.str x0, [x29, #-8]
    arm64.b main.eval_0.cmp0
  eval_0.cmp0:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.eval_0.case0
    arm64.b main.eval_0.cmp1
  eval_0.case0:
    arm64.mov x0, #10
    arm64.str x0, [x29, #-16]
    arm64.b main.eval_0.merge
  eval_0.cmp1:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.eval_0.case1
    arm64.b main.eval_0.case2
  eval_0.case1:
    arm64.mov x0, #20
    arm64.str x0, [x29, #-16]
    arm64.b main.eval_0.merge
  eval_0.case2:
    arm64.mov x0, #0
    arm64.str x0, [x29, #-16]
    arm64.b main.eval_0.merge
  eval_0.merge:
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #2
    arm64.mul x2, x0, x1
    arm64.mov x3, #0
    arm64.cmp x2, x3
    arm64.cset x4, lt
    arm64.mov x5, #4294967295
    arm64.cmp x2, x5
    arm64.cset x6, gt
    arm64.orr x7, x4, x6
    arm64.cmp x7, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.mov x0, x2
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: match-statement-with-function-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(n Integer) returns Integer
	return n * 2
end 'double'

function main() returns ExitCode
	let x = 2
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
```RequiredIR:x64-windows
=== maxon
module {
  func @double(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {optimalType = i64}
    maxon.scope_end [n]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %3 = maxon.literal {value = 2 : i64}
    maxon.assign %3 {var = x} {kind = i64} {decl = 1 : i1}
    %4 = maxon.literal {value = 0 : i64}
    maxon.assign %4 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %3 {var = __match_process_0} {kind = i64} {decl = 1 : i1}
    maxon.br process_0.cmp0
  process_0.cmp0:
    %5 = maxon.var_ref {var = __match_process_0} {type = i64}
    %6 = maxon.literal {value = 1 : i64}
    %7 = maxon.binop %5, %6 {op = eq}
    maxon.cond_br %7 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    %8 = maxon.literal {value = 10 : i64}
    %9 = maxon.call @double %8
    maxon.assign %9 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.cmp1:
    %10 = maxon.var_ref {var = __match_process_0} {type = i64}
    %11 = maxon.literal {value = 2 : i64}
    %12 = maxon.binop %10, %11 {op = eq}
    maxon.cond_br %12 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    %13 = maxon.literal {value = 20 : i64}
    %14 = maxon.call @double %13
    maxon.assign %14 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.case2:
    %15 = maxon.literal {value = 0 : i64}
    maxon.assign %15 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.merge:
    %16 = maxon.var_ref {var = result} {type = i64}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %16, %17 {op = lt}
    %19 = maxon.literal {value = 4294967295 : i64}
    %20 = maxon.binop %16, %19 {op = gt}
    %21 = maxon.binop %18, %20 {op = or}
    maxon.cond_br %21 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at match-statement-with-function-call.test:17: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [x, result, __match_process_0]
    maxon.return %16
  }
}
=== standard
module {
  func @double(n: i64) -> i64 {
  entry:
    %0 = func.param n : StdI64
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.muli %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 2 : i64}
    memref.store %3, __match_process_0
    cf.br process_0.cmp0
  process_0.cmp0:
    %5 = memref.load __match_process_0 : i64
    %6 = arith.constant {value = 1 : i64}
    %7 = arith.cmpi eq %5, %6
    cf.cond_br %7 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    %8 = arith.constant {value = 10 : i64}
    %9 = func.call @double %8
    memref.store %9, result
    cf.br process_0.merge
  process_0.cmp1:
    %10 = memref.load __match_process_0 : i64
    %11 = arith.constant {value = 2 : i64}
    %12 = arith.cmpi eq %10, %11
    cf.cond_br %12 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    %13 = arith.constant {value = 20 : i64}
    %14 = func.call @double %13
    memref.store %14, result
    cf.br process_0.merge
  process_0.case2:
    %15 = arith.constant {value = 0 : i64}
    memref.store %15, result
    cf.br process_0.merge
  process_0.merge:
    %16 = memref.load result : i64
    %17 = arith.constant {value = 0 : i64}
    %18 = arith.cmpi lt %16, %17
    %19 = arith.constant {value = 4294967295 : i64}
    %20 = arith.cmpi gt %16, %19
    %21 = arith.ori1 %18, %20
    cf.cond_br %21 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %22 = memref.lea_symdata __panic_msg_0
    %23 = std.ptr_to_i64 %22
    std.call_runtime @mrt_panic %23
  __range_ok_0:
    func.return %16
  }
}
=== x86
module {
  func @double(n: i64) -> i64 {
  entry:
    x64.mov rax, 2
    x64.imul rcx, rax
    x64.mov rax, rcx
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rax, 2
    x64.mov [rbp-8], rax
    x64.jmp main.process_0.cmp0
  process_0.cmp0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 1
    x64.cmp rax, rcx
    x64.jne main.process_0.cmp1
  process_0.case0:
    x64.mov rcx, 10
    x64.call double
    x64.mov [rbp-16], rax
    x64.jmp main.process_0.merge
  process_0.cmp1:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 2
    x64.cmp rax, rcx
    x64.jne main.process_0.case2
  process_0.case1:
    x64.mov rcx, 20
    x64.call double
    x64.mov [rbp-16], rax
    x64.jmp main.process_0.merge
  process_0.case2:
    x64.xor eax, eax
    x64.mov [rbp-16], rax
    x64.jmp main.process_0.merge
  process_0.merge:
    x64.mov rax, [rbp-16]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.double(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.binop %0, %1 {op = mul} {optimalType = i64}
    maxon.scope_end [n]
    maxon.return %2
  }
  func @main() -> i64 {
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
    %7 = maxon.binop %5, %6 {op = eq}
    maxon.cond_br %7 [then: process_0.case0, else: process_0.cmp1]
  process_0.case0:
    %8 = maxon.literal {value = 10 : i64}
    %9 = maxon.call @register-allocator.double %8
    maxon.assign %9 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.cmp1:
    %10 = maxon.var_ref {var = __match_process_0} {type = i64}
    %11 = maxon.literal {value = 2 : i64}
    %12 = maxon.binop %10, %11 {op = eq}
    maxon.cond_br %12 [then: process_0.case1, else: process_0.case2]
  process_0.case1:
    %13 = maxon.literal {value = 20 : i64}
    %14 = maxon.call @register-allocator.double %13
    maxon.assign %14 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.case2:
    %15 = maxon.literal {value = 0 : i64}
    maxon.assign %15 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br process_0.merge
  process_0.merge:
    %16 = maxon.var_ref {var = result} {type = i64}
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %16, %17 {op = lt}
    %19 = maxon.literal {value = 4294967295 : i64}
    %20 = maxon.binop %16, %19 {op = gt}
    %21 = maxon.binop %18, %20 {op = or}
    maxon.cond_br %21 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at match-statement-with-function-call.test:17: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [x, result, __match_process_0]
    maxon.return %16
  }
}
=== standard
module {
  func @register-allocator.double(n: i64) -> i64 {
  entry:
    %0 = func.param n : StdI64
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.muli %0, %1
    func.return %2
  }
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 2 : i64}
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
    %17 = arith.constant {value = 0 : i64}
    %18 = arith.cmpi lt %16, %17
    %19 = arith.constant {value = 4294967295 : i64}
    %20 = arith.cmpi gt %16, %19
    %21 = arith.ori1 %18, %20
    cf.cond_br %21 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %22 = memref.lea_symdata __panic_msg_0
    %23 = std.ptr_to_i64 %22
    std.call_runtime @maxon_panic %23
  __range_ok_1:
    func.return %16
  }
}
=== arm64
module {
  func @register-allocator.double(n: i64) -> i64 {
  entry:
    arm64.mov x1, #2
    arm64.mul x2, x0, x1
    arm64.mov x0, x2
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #2
    arm64.str x0, [x29, #-8]
    arm64.b main.process_0.cmp0
  process_0.cmp0:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.process_0.case0
    arm64.b main.process_0.cmp1
  process_0.case0:
    arm64.mov x0, #10
    arm64.bl register-allocator.double
    arm64.str x0, [x29, #-16]
    arm64.b main.process_0.merge
  process_0.cmp1:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne main.process_0.case1
    arm64.b main.process_0.case2
  process_0.case1:
    arm64.mov x0, #20
    arm64.bl register-allocator.double
    arm64.str x0, [x29, #-16]
    arm64.b main.process_0.merge
  process_0.case2:
    arm64.mov x0, #0
    arm64.str x0, [x29, #-16]
    arm64.b main.process_0.merge
  process_0.merge:
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

### Level 8: Error Handling

<!-- test: error-otherwise-ignore -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	try mayFail() otherwise ignore
	return 42
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @mayFail() -> i64 {
  entry:
    %8 = maxon.enum_literal @MyError.failed
    maxon.scope_end []
    maxon.throw @MyError %8
  }
  func @main() -> i64 {
  entry:
    %11, %10 = maxon.try_call @mayFail
    %12 = maxon.literal {value = 42 : i64}
    maxon.scope_end []
    maxon.return %12
  }
}
=== standard
module {
  func @mayFail() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = arith.constant {value = 1 : i64}
    %2 = arith.addi %0, %1
    func.error_return %2
  }
  func @main() -> u32 {
  entry:
    %3, %4 = func.try_call @mayFail
    %5 = arith.constant {value = 42 : i64}
    func.return %5
  }
}
=== x86
module {
  func @mayFail() -> i64 {
  entry:
    x64.xor eax, eax
    x64.mov rcx, 1
    x64.add rax, rcx
    x64.mov rdx, rax
    x64.xor eax, eax
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.call mayFail
    x64.mov rcx, 42
    x64.mov rax, rcx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    %8 = maxon.enum_literal @MyError.failed
    maxon.scope_end []
    maxon.throw @MyError %8
  }
  func @main() -> i64 {
  entry:
    %11, %10 = maxon.try_call @register-allocator.mayFail
    %12 = maxon.literal {value = 42 : i64}
    maxon.scope_end []
    maxon.return %12
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
  func @main() -> u32 {
  entry:
    %3, %4 = func.try_call @register-allocator.mayFail
    %5 = arith.constant {value = 42 : i64}
    func.return %5
  }
}
=== arm64
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    arm64.mov x0, #0
    arm64.mov x1, #1
    arm64.add x2, x0, x1
    arm64.mov x1, x2
    arm64.mov x0, #0
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=32
    arm64.bl register-allocator.mayFail
    arm64.mov x2, #42
    arm64.str x0, [x29, #-8]
    arm64.mov x0, x2
    arm64.epilogue stack_size=32
    arm64.ret
  }
}
```

<!-- test: error-otherwise-block -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
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
```RequiredIR:x64-windows
=== maxon
module {
  func @mayFail() -> i64 {
  entry:
    %8 = maxon.enum_literal @MyError.failed
    maxon.scope_end []
    maxon.throw @MyError %8
  }
  func @main() -> i64 {
  entry:
    %9 = maxon.literal {value = 0 : i64}
    maxon.assign %9 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12, %11 = maxon.try_call @mayFail
    maxon.assign %11 {var = __try_error_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %12 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.literal {value = 0 : i64}
    %14 = maxon.binop %11, %13 {op = ne}
    maxon.cond_br %14 [then: otherwise_error_0, else: otherwise_continue_0]
  otherwise_error_0:
    %15 = maxon.literal {value = 42 : i64}
    maxon.assign %15 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_continue_0
  otherwise_continue_0:
    %16 = maxon.var_ref {var = __try_result_0} {type = i64}
    %17 = maxon.var_ref {var = result} {type = i64}
    %18 = maxon.literal {value = 0 : i64}
    %19 = maxon.binop %17, %18 {op = lt}
    %20 = maxon.literal {value = 4294967295 : i64}
    %21 = maxon.binop %17, %20 {op = gt}
    %22 = maxon.binop %19, %21 {op = or}
    maxon.cond_br %22 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at error-otherwise-block.test:18: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [result, __try_error_0, __try_result_0]
    maxon.return %17
  }
}
=== standard
module {
  func @mayFail() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = arith.constant {value = 1 : i64}
    %2 = arith.addi %0, %1
    func.error_return %2
  }
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, result
    %4, %5 = func.try_call @mayFail
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi ne %5, %6
    cf.cond_br %7 [then: otherwise_error_0, else: otherwise_continue_0]
  otherwise_error_0:
    %8 = arith.constant {value = 42 : i64}
    memref.store %8, result
    cf.br otherwise_continue_0
  otherwise_continue_0:
    %10 = memref.load result : i64
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.cmpi lt %10, %11
    %13 = arith.constant {value = 4294967295 : i64}
    %14 = arith.cmpi gt %10, %13
    %15 = arith.ori1 %12, %14
    cf.cond_br %15 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %16 = memref.lea_symdata __panic_msg_0
    %17 = std.ptr_to_i64 %16
    std.call_runtime @mrt_panic %17
  __range_ok_0:
    func.return %10
  }
}
=== x86
module {
  func @mayFail() -> i64 {
  entry:
    x64.xor eax, eax
    x64.mov rcx, 1
    x64.add rax, rcx
    x64.mov rdx, rax
    x64.xor eax, eax
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.call mayFail
    x64.xor ecx, ecx
    x64.cmp rdx, rcx
    x64.je main.otherwise_continue_0
  otherwise_error_0:
    x64.mov rax, 42
    x64.mov [rbp-8], rax
    x64.jmp main.otherwise_continue_0
  otherwise_continue_0:
    x64.mov rax, [rbp-8]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    %8 = maxon.enum_literal @MyError.failed
    maxon.scope_end []
    maxon.throw @MyError %8
  }
  func @main() -> i64 {
  entry:
    %9 = maxon.literal {value = 0 : i64}
    maxon.assign %9 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12, %11 = maxon.try_call @register-allocator.mayFail
    maxon.assign %11 {var = __try_error_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %12 {var = __try_result_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.literal {value = 0 : i64}
    %14 = maxon.binop %11, %13 {op = ne}
    maxon.cond_br %14 [then: otherwise_error_0, else: otherwise_continue_1]
  otherwise_error_0:
    %15 = maxon.literal {value = 42 : i64}
    maxon.assign %15 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_continue_1
  otherwise_continue_1:
    %16 = maxon.var_ref {var = __try_result_3} {type = i64}
    %17 = maxon.var_ref {var = result} {type = i64}
    %18 = maxon.literal {value = 0 : i64}
    %19 = maxon.binop %17, %18 {op = lt}
    %20 = maxon.literal {value = 4294967295 : i64}
    %21 = maxon.binop %17, %20 {op = gt}
    %22 = maxon.binop %19, %21 {op = or}
    maxon.cond_br %22 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    maxon.panic "panic at error-otherwise-block.test:18: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_4:
    maxon.scope_end [result, __try_error_2, __try_result_3]
    maxon.return %17
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
  func @main() -> u32 {
  entry:
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, result
    %4, %5 = func.try_call @register-allocator.mayFail
    %6 = arith.constant {value = 0 : i64}
    %7 = arith.cmpi ne %5, %6
    cf.cond_br %7 [then: otherwise_error_0, else: otherwise_continue_1]
  otherwise_error_0:
    %8 = arith.constant {value = 42 : i64}
    memref.store %8, result
    cf.br otherwise_continue_1
  otherwise_continue_1:
    %10 = memref.load result : i64
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.cmpi lt %10, %11
    %13 = arith.constant {value = 4294967295 : i64}
    %14 = arith.cmpi gt %10, %13
    %15 = arith.ori1 %12, %14
    cf.cond_br %15 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    %16 = memref.lea_symdata __panic_msg_0
    %17 = std.ptr_to_i64 %16
    std.call_runtime @maxon_panic %17
  __range_ok_4:
    func.return %10
  }
}
=== arm64
module {
  func @register-allocator.mayFail() -> i64 {
  entry:
    arm64.mov x0, #0
    arm64.mov x1, #1
    arm64.add x2, x0, x1
    arm64.mov x1, x2
    arm64.mov x0, #0
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.bl register-allocator.mayFail
    arm64.mov x2, #0
    arm64.cmp x1, x2
    arm64.cset x3, ne
    arm64.cmp x3, #0
    arm64.b.ne main.otherwise_error_0
    arm64.b main.otherwise_continue_1
  otherwise_error_0:
    arm64.mov x0, #42
    arm64.str x0, [x29, #-8]
    arm64.b main.otherwise_continue_1
  otherwise_continue_1:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_4
    arm64.b main.__range_ok_4
  __range_panic_4:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_4:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: error-propagate-through-caller -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function inner() returns Integer throws MyError
	throw MyError.failed
end 'inner'

function middle() returns Integer throws MyError
	let x = try inner()
	return x
end 'middle'

function main() returns ExitCode
	let x = try middle() otherwise 99
	return x
end 'main'
```
```exitcode
99
```
```RequiredIR:x64-windows
=== maxon
module {
  func @inner() -> i64 {
  entry:
    %8 = maxon.enum_literal @MyError.failed
    maxon.scope_end []
    maxon.throw @MyError %8
  }
  func @middle() -> i64 {
  entry:
    %11, %10 = maxon.try_call @inner
    maxon.assign %10 {var = __try_error_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %11 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %10, %12 {op = ne}
    maxon.cond_br %13 [then: propagate_error_0, else: try_continue_0]
  propagate_error_0:
    %14 = maxon.var_ref {var = __try_error_0} {type = i64}
    maxon.scope_end [__try_error_0, __try_result_0]
    maxon.return %14
  try_continue_0:
    %15 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %15 {var = x} {kind = i64} {decl = 1 : i1}
    maxon.scope_end [__try_error_0, x, __try_result_0]
    maxon.return %15
  }
  func @main() -> i64 {
  entry:
    %18, %17 = maxon.try_call @middle
    %19 = maxon.literal {value = 99 : i64}
    maxon.assign %19 {var = __try_default_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %18 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %17, %20 {op = ne}
    maxon.cond_br %21 [then: otherwise_default_error_0, else: otherwise_default_continue_0]
  otherwise_default_error_0:
    %22 = maxon.var_ref {var = __try_default_0} {type = i64}
    maxon.assign %22 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_0
  otherwise_default_continue_0:
    %23 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %23 {var = x} {kind = i64} {decl = 1 : i1}
    %24 = maxon.literal {value = 0 : i64}
    %25 = maxon.binop %23, %24 {op = lt}
    %26 = maxon.literal {value = 4294967295 : i64}
    %27 = maxon.binop %23, %26 {op = gt}
    %28 = maxon.binop %25, %27 {op = or}
    maxon.cond_br %28 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at error-propagate-through-caller.test:20: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [__try_default_0, x, __try_result_0]
    maxon.return %23
  }
}
=== standard
module {
  func @inner() -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = arith.constant {value = 1 : i64}
    %2 = arith.addi %0, %1
    func.error_return %2
  }
  func @middle() -> i64 {
  entry:
    %3, %4 = func.try_call @inner
    memref.store %4, __try_error_0
    memref.store %3, __try_result_0
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.cmpi ne %4, %5
    cf.cond_br %6 [then: propagate_error_0, else: try_continue_0]
  propagate_error_0:
    %7 = memref.load __try_error_0 : i64
    func.error_return %7
  try_continue_0:
    %8 = memref.load __try_result_0 : i64
    func.return %8
  }
  func @main() -> u32 {
  entry:
    %9, %10 = func.try_call @middle
    %11 = arith.constant {value = 99 : i64}
    memref.store %11, __try_default_0
    memref.store %9, __try_result_0
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.cmpi ne %10, %12
    cf.cond_br %13 [then: otherwise_default_error_0, else: otherwise_default_continue_0]
  otherwise_default_error_0:
    %14 = memref.load __try_default_0 : i64
    memref.store %14, __try_result_0
    cf.br otherwise_default_continue_0
  otherwise_default_continue_0:
    %15 = memref.load __try_result_0 : i64
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi lt %15, %16
    %18 = arith.constant {value = 4294967295 : i64}
    %19 = arith.cmpi gt %15, %18
    %20 = arith.ori1 %17, %19
    cf.cond_br %20 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %21 = memref.lea_symdata __panic_msg_0
    %22 = std.ptr_to_i64 %21
    std.call_runtime @mrt_panic %22
  __range_ok_0:
    func.return %15
  }
}
=== x86
module {
  func @inner() -> i64 {
  entry:
    x64.xor eax, eax
    x64.mov rcx, 1
    x64.add rax, rcx
    x64.mov rdx, rax
    x64.xor eax, eax
    x64.ret
  }
  func @middle() -> i64 {
  entry:
    x64.prologue stack_size=16
    x64.call inner
    x64.mov [rbp-8], rdx
    x64.mov [rbp-16], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je middle.try_continue_0
  propagate_error_0:
    x64.mov rdx, [rbp-8]
    x64.xor eax, eax
    x64.epilogue
    x64.ret
  try_continue_0:
    x64.mov rax, [rbp-16]
    x64.xor edx, edx
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.call middle
    x64.mov rcx, 99
    x64.mov [rbp-8], rcx
    x64.mov [rbp-16], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je main.otherwise_default_continue_0
  otherwise_default_error_0:
    x64.mov rax, [rbp-8]
    x64.mov [rbp-16], rax
    x64.jmp main.otherwise_default_continue_0
  otherwise_default_continue_0:
    x64.mov rax, [rbp-16]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.inner() -> i64 {
  entry:
    %8 = maxon.enum_literal @MyError.failed
    maxon.scope_end []
    maxon.throw @MyError %8
  }
  func @register-allocator.middle() -> i64 {
  entry:
    %11, %10 = maxon.try_call @register-allocator.inner
    maxon.assign %10 {var = __try_error_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %11 {var = __try_result_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %10, %12 {op = ne}
    maxon.cond_br %13 [then: propagate_error_0, else: try_continue_1]
  propagate_error_0:
    %14 = maxon.var_ref {var = __try_error_2} {type = i64}
    maxon.scope_end [__try_error_2, __try_result_3]
    maxon.return %14
  try_continue_1:
    %15 = maxon.var_ref {var = __try_result_3} {type = i64}
    maxon.assign %15 {var = x} {kind = i64} {decl = 1 : i1}
    maxon.scope_end [__try_error_2, x, __try_result_3]
    maxon.return %15
  }
  func @main() -> i64 {
  entry:
    %18, %17 = maxon.try_call @register-allocator.middle
    %19 = maxon.literal {value = 99 : i64}
    maxon.assign %19 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %18 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %17, %20 {op = ne}
    maxon.cond_br %21 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %22 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %22 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %23 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %23 {var = x} {kind = i64} {decl = 1 : i1}
    %24 = maxon.literal {value = 0 : i64}
    %25 = maxon.binop %23, %24 {op = lt}
    %26 = maxon.literal {value = 4294967295 : i64}
    %27 = maxon.binop %23, %26 {op = gt}
    %28 = maxon.binop %25, %27 {op = or}
    maxon.cond_br %28 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    maxon.panic "panic at error-propagate-through-caller.test:20: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_4:
    maxon.scope_end [__try_default_1, x, __try_result_0]
    maxon.return %23
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
    func.return %8
  }
  func @main() -> u32 {
  entry:
    %9, %10 = func.try_call @register-allocator.middle
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
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi lt %15, %16
    %18 = arith.constant {value = 4294967295 : i64}
    %19 = arith.cmpi gt %15, %18
    %20 = arith.ori1 %17, %19
    cf.cond_br %20 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    %21 = memref.lea_symdata __panic_msg_0
    %22 = std.ptr_to_i64 %21
    std.call_runtime @maxon_panic %22
  __range_ok_4:
    func.return %15
  }
}
=== arm64
module {
  func @register-allocator.inner() -> i64 {
  entry:
    arm64.mov x0, #0
    arm64.mov x1, #1
    arm64.add x2, x0, x1
    arm64.mov x1, x2
    arm64.mov x0, #0
    arm64.ret
  }
  func @register-allocator.middle() -> i64 {
  entry:
    arm64.prologue stack_size=48
    arm64.bl register-allocator.inner
    arm64.str x1, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne register-allocator.middle.propagate_error_0
    arm64.b register-allocator.middle.try_continue_1
  propagate_error_0:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, x0
    arm64.mov x0, #0
    arm64.epilogue stack_size=48
    arm64.ret
  try_continue_1:
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #0
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.bl register-allocator.middle
    arm64.mov x2, #99
    arm64.str x2, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_2
    arm64.b main.otherwise_default_continue_3
  otherwise_default_error_2:
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.b main.otherwise_default_continue_3
  otherwise_default_continue_3:
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_4
    arm64.b main.__range_ok_4
  __range_panic_4:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_4:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: error-multiple-try-calls -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function getA() returns Integer throws MyError
	return 10
end 'getA'

function getB() returns Integer throws MyError
	return 20
end 'getB'

function getC() returns Integer throws MyError
	throw MyError.failed
end 'getC'

function main() returns ExitCode
	let a = try getA() otherwise 0
	let b = try getB() otherwise 0
	let c = try getC() otherwise 12
	return a + b + c
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @getA() -> i64 {
  entry:
    %8 = maxon.literal {value = 10 : i64}
    maxon.scope_end []
    maxon.return %8
  }
  func @getB() -> i64 {
  entry:
    %9 = maxon.literal {value = 20 : i64}
    maxon.scope_end []
    maxon.return %9
  }
  func @getC() -> i64 {
  entry:
    %10 = maxon.enum_literal @MyError.failed
    maxon.scope_end []
    maxon.throw @MyError %10
  }
  func @main() -> i64 {
  entry:
    %13, %12 = maxon.try_call @getA
    %14 = maxon.literal {value = 0 : i64}
    maxon.assign %14 {var = __try_default_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %13 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 0 : i64}
    %16 = maxon.binop %12, %15 {op = ne}
    maxon.cond_br %16 [then: otherwise_default_error_0, else: otherwise_default_continue_0]
  otherwise_default_error_0:
    %17 = maxon.var_ref {var = __try_default_0} {type = i64}
    maxon.assign %17 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_0
  otherwise_default_continue_0:
    %18 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %18 {var = a} {kind = i64} {decl = 1 : i1}
    %21, %20 = maxon.try_call @getB
    %22 = maxon.literal {value = 0 : i64}
    maxon.assign %22 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %21 {var = __try_result_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %23 = maxon.literal {value = 0 : i64}
    %24 = maxon.binop %20, %23 {op = ne}
    maxon.cond_br %24 [then: otherwise_default_error_1, else: otherwise_default_continue_1]
  otherwise_default_error_1:
    %25 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %25 {var = __try_result_1} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_1
  otherwise_default_continue_1:
    %26 = maxon.var_ref {var = __try_result_1} {type = i64}
    maxon.assign %26 {var = b} {kind = i64} {decl = 1 : i1}
    %29, %28 = maxon.try_call @getC
    %30 = maxon.literal {value = 12 : i64}
    maxon.assign %30 {var = __try_default_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %29 {var = __try_result_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %31 = maxon.literal {value = 0 : i64}
    %32 = maxon.binop %28, %31 {op = ne}
    maxon.cond_br %32 [then: otherwise_default_error_2, else: otherwise_default_continue_2]
  otherwise_default_error_2:
    %33 = maxon.var_ref {var = __try_default_2} {type = i64}
    maxon.assign %33 {var = __try_result_2} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_2
  otherwise_default_continue_2:
    %34 = maxon.var_ref {var = __try_result_2} {type = i64}
    maxon.assign %34 {var = c} {kind = i64} {decl = 1 : i1}
    %35 = maxon.var_ref {var = a} {type = i64}
    %36 = maxon.var_ref {var = b} {type = i64}
    %37 = maxon.binop %35, %36 {op = add}
    %38 = maxon.binop %37, %34 {op = add} {optimalType = i64}
    %39 = maxon.literal {value = 0 : i64}
    %40 = maxon.binop %38, %39 {op = lt}
    %41 = maxon.literal {value = 4294967295 : i64}
    %42 = maxon.binop %38, %41 {op = gt}
    %43 = maxon.binop %40, %42 {op = or}
    maxon.cond_br %43 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at error-multiple-try-calls.test:25: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [__try_default_0, a, __try_default_1, b, __try_default_2, c, __try_result_0, __try_result_1, __try_result_2]
    maxon.return %38
  }
}
=== standard
module {
  func @getA() -> i64 {
  entry:
    %0 = arith.constant {value = 10 : i64}
    func.return %0
  }
  func @getB() -> i64 {
  entry:
    %1 = arith.constant {value = 20 : i64}
    func.return %1
  }
  func @getC() -> i64 {
  entry:
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.constant {value = 1 : i64}
    %4 = arith.addi %2, %3
    func.error_return %4
  }
  func @main() -> u32 {
  entry:
    %5, %6 = func.try_call @getA
    %7 = arith.constant {value = 0 : i64}
    memref.store %7, __try_default_0
    memref.store %5, __try_result_0
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.cmpi ne %6, %8
    cf.cond_br %9 [then: otherwise_default_error_0, else: otherwise_default_continue_0]
  otherwise_default_error_0:
    %10 = memref.load __try_default_0 : i64
    memref.store %10, __try_result_0
    cf.br otherwise_default_continue_0
  otherwise_default_continue_0:
    %11 = memref.load __try_result_0 : i64
    memref.store %11, a
    %12, %13 = func.try_call @getB
    %14 = arith.constant {value = 0 : i64}
    memref.store %14, __try_default_1
    memref.store %12, __try_result_1
    %15 = arith.constant {value = 0 : i64}
    %16 = arith.cmpi ne %13, %15
    cf.cond_br %16 [then: otherwise_default_error_1, else: otherwise_default_continue_1]
  otherwise_default_error_1:
    %17 = memref.load __try_default_1 : i64
    memref.store %17, __try_result_1
    cf.br otherwise_default_continue_1
  otherwise_default_continue_1:
    %18 = memref.load __try_result_1 : i64
    memref.store %18, b
    %19, %20 = func.try_call @getC
    %21 = arith.constant {value = 12 : i64}
    memref.store %21, __try_default_2
    memref.store %19, __try_result_2
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi ne %20, %22
    cf.cond_br %23 [then: otherwise_default_error_2, else: otherwise_default_continue_2]
  otherwise_default_error_2:
    %24 = memref.load __try_default_2 : i64
    memref.store %24, __try_result_2
    cf.br otherwise_default_continue_2
  otherwise_default_continue_2:
    %25 = memref.load __try_result_2 : i64
    %26 = memref.load a : i64
    %27 = memref.load b : i64
    %28 = arith.addi %26, %27
    %29 = arith.addi %28, %25
    %30 = arith.constant {value = 0 : i64}
    %31 = arith.cmpi lt %29, %30
    %32 = arith.constant {value = 4294967295 : i64}
    %33 = arith.cmpi gt %29, %32
    %34 = arith.ori1 %31, %33
    cf.cond_br %34 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %35 = memref.lea_symdata __panic_msg_0
    %36 = std.ptr_to_i64 %35
    std.call_runtime @mrt_panic %36
  __range_ok_0:
    func.return %29
  }
}
=== x86
module {
  func @getA() -> i64 {
  entry:
    x64.mov rax, 10
    x64.xor edx, edx
    x64.ret
  }
  func @getB() -> i64 {
  entry:
    x64.mov rax, 20
    x64.xor edx, edx
    x64.ret
  }
  func @getC() -> i64 {
  entry:
    x64.xor eax, eax
    x64.mov rcx, 1
    x64.add rax, rcx
    x64.mov rdx, rax
    x64.xor eax, eax
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=64
    x64.call getA
    x64.xor ecx, ecx
    x64.mov [rbp-8], rcx
    x64.mov [rbp-16], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je main.otherwise_default_continue_0
  otherwise_default_error_0:
    x64.mov rax, [rbp-8]
    x64.mov [rbp-16], rax
    x64.jmp main.otherwise_default_continue_0
  otherwise_default_continue_0:
    x64.mov rax, [rbp-16]
    x64.mov [rbp-24], rax
    x64.call getB
    x64.xor ecx, ecx
    x64.mov [rbp-32], rcx
    x64.mov [rbp-40], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je main.otherwise_default_continue_1
  otherwise_default_error_1:
    x64.mov rax, [rbp-32]
    x64.mov [rbp-40], rax
    x64.jmp main.otherwise_default_continue_1
  otherwise_default_continue_1:
    x64.mov rax, [rbp-40]
    x64.mov [rbp-48], rax
    x64.call getC
    x64.mov rcx, 12
    x64.mov [rbp-56], rcx
    x64.mov [rbp-64], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je main.otherwise_default_continue_2
  otherwise_default_error_2:
    x64.mov rax, [rbp-56]
    x64.mov [rbp-64], rax
    x64.jmp main.otherwise_default_continue_2
  otherwise_default_continue_2:
    x64.mov rax, [rbp-64]
    x64.mov rcx, [rbp-24]
    x64.mov rdx, [rbp-48]
    x64.add rcx, rdx
    x64.add rcx, rax
    x64.xor ebx, ebx
    x64.mov esi, 4294967295
    x64.cmp rcx, rsi
    x64.jg main.__range_panic_0
    x64.cmp rcx, rbx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, rcx
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.getA() -> i64 {
  entry:
    %8 = maxon.literal {value = 10 : i64}
    maxon.scope_end []
    maxon.return %8
  }
  func @register-allocator.getB() -> i64 {
  entry:
    %9 = maxon.literal {value = 20 : i64}
    maxon.scope_end []
    maxon.return %9
  }
  func @register-allocator.getC() -> i64 {
  entry:
    %10 = maxon.enum_literal @MyError.failed
    maxon.scope_end []
    maxon.throw @MyError %10
  }
  func @main() -> i64 {
  entry:
    %13, %12 = maxon.try_call @register-allocator.getA
    %14 = maxon.literal {value = 0 : i64}
    maxon.assign %14 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %13 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 0 : i64}
    %16 = maxon.binop %12, %15 {op = ne}
    maxon.cond_br %16 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %17 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %17 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %18 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %18 {var = a} {kind = i64} {decl = 1 : i1}
    %21, %20 = maxon.try_call @register-allocator.getB
    %22 = maxon.literal {value = 0 : i64}
    maxon.assign %22 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %21 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %23 = maxon.literal {value = 0 : i64}
    %24 = maxon.binop %20, %23 {op = ne}
    maxon.cond_br %24 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %25 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %25 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %26 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %26 {var = b} {kind = i64} {decl = 1 : i1}
    %29, %28 = maxon.try_call @register-allocator.getC
    %30 = maxon.literal {value = 12 : i64}
    maxon.assign %30 {var = __try_default_9} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %29 {var = __try_result_8} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %31 = maxon.literal {value = 0 : i64}
    %32 = maxon.binop %28, %31 {op = ne}
    maxon.cond_br %32 [then: otherwise_default_error_10, else: otherwise_default_continue_11]
  otherwise_default_error_10:
    %33 = maxon.var_ref {var = __try_default_9} {type = i64}
    maxon.assign %33 {var = __try_result_8} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_11
  otherwise_default_continue_11:
    %34 = maxon.var_ref {var = __try_result_8} {type = i64}
    maxon.assign %34 {var = c} {kind = i64} {decl = 1 : i1}
    %35 = maxon.var_ref {var = a} {type = i64}
    %36 = maxon.var_ref {var = b} {type = i64}
    %37 = maxon.binop %35, %36 {op = add}
    %38 = maxon.binop %37, %34 {op = add}
    %39 = maxon.literal {value = 0 : i64}
    %40 = maxon.binop %38, %39 {op = lt}
    %41 = maxon.literal {value = 4294967295 : i64}
    %42 = maxon.binop %38, %41 {op = gt}
    %43 = maxon.binop %40, %42 {op = or}
    maxon.cond_br %43 [then: __range_panic_12, else: __range_ok_12]
  __range_panic_12:
    maxon.panic "panic at error-multiple-try-calls.test:25: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_12:
    maxon.scope_end [__try_default_1, a, __try_default_5, b, __try_default_9, c, __try_result_0, __try_result_4, __try_result_8]
    maxon.return %38
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
  func @main() -> u32 {
  entry:
    %5, %6 = func.try_call @register-allocator.getA
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
    %26 = memref.load a : i64
    %27 = memref.load b : i64
    %28 = arith.addi %26, %27
    %29 = arith.addi %28, %25
    %30 = arith.constant {value = 0 : i64}
    %31 = arith.cmpi lt %29, %30
    %32 = arith.constant {value = 4294967295 : i64}
    %33 = arith.cmpi gt %29, %32
    %34 = arith.ori1 %31, %33
    cf.cond_br %34 [then: __range_panic_12, else: __range_ok_12]
  __range_panic_12:
    %35 = memref.lea_symdata __panic_msg_0
    %36 = std.ptr_to_i64 %35
    std.call_runtime @maxon_panic %36
  __range_ok_12:
    func.return %29
  }
}
=== arm64
module {
  func @register-allocator.getA() -> i64 {
  entry:
    arm64.mov x0, #10
    arm64.mov x1, #0
    arm64.ret
  }
  func @register-allocator.getB() -> i64 {
  entry:
    arm64.mov x0, #20
    arm64.mov x1, #0
    arm64.ret
  }
  func @register-allocator.getC() -> i64 {
  entry:
    arm64.mov x0, #0
    arm64.mov x1, #1
    arm64.add x2, x0, x1
    arm64.mov x1, x2
    arm64.mov x0, #0
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=144
    arm64.bl register-allocator.getA
    arm64.mov x2, #0
    arm64.str x2, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_2
    arm64.b main.otherwise_default_continue_3
  otherwise_default_error_2:
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.b main.otherwise_default_continue_3
  otherwise_default_continue_3:
    arm64.ldr x0, [x29, #-16]
    arm64.str x0, [x29, #-24]
    arm64.bl register-allocator.getB
    arm64.mov x2, #0
    arm64.str x2, [x29, #-32]
    arm64.str x0, [x29, #-40]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_6
    arm64.b main.otherwise_default_continue_7
  otherwise_default_error_6:
    arm64.ldr x0, [x29, #-32]
    arm64.str x0, [x29, #-40]
    arm64.b main.otherwise_default_continue_7
  otherwise_default_continue_7:
    arm64.ldr x0, [x29, #-40]
    arm64.str x0, [x29, #-48]
    arm64.bl register-allocator.getC
    arm64.mov x2, #12
    arm64.str x2, [x29, #-56]
    arm64.str x0, [x29, #-64]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_10
    arm64.b main.otherwise_default_continue_11
  otherwise_default_error_10:
    arm64.ldr x0, [x29, #-56]
    arm64.str x0, [x29, #-64]
    arm64.b main.otherwise_default_continue_11
  otherwise_default_continue_11:
    arm64.ldr x0, [x29, #-64]
    arm64.ldr x1, [x29, #-24]
    arm64.ldr x2, [x29, #-48]
    arm64.add x3, x1, x2
    arm64.add x4, x3, x0
    arm64.mov x5, #0
    arm64.cmp x4, x5
    arm64.cset x6, lt
    arm64.mov x7, #4294967295
    arm64.cmp x4, x7
    arm64.cset x8, gt
    arm64.orr x9, x6, x8
    arm64.cmp x9, #0
    arm64.b.ne main.__range_panic_12
    arm64.b main.__range_ok_12
  __range_panic_12:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_12:
    arm64.mov x0, x4
    arm64.epilogue stack_size=144
    arm64.ret
  }
}
```

<!-- test: error-throw-in-match -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	invalidInput
	notFound
end 'MyError'

function lookup(key Integer) returns Integer throws MyError
	match key 'dispatch'
		1 then return 100
		2 then return 200
		default then throw MyError.notFound
	end 'dispatch'
end 'lookup'

function main() returns ExitCode
	let a = try lookup(2) otherwise 0
	let b = try lookup(99) otherwise 42
	return a + b mod 256
end 'main'
```
```exitcode
242
```
```RequiredIR:x64-windows
=== maxon
module {
  func @lookup(key: i64) -> i64 {
  entry:
    %8 = maxon.param {index = 0 : i32} {name = key} {type = i64}
    maxon.assign %8 {var = __match_dispatch_0} {kind = i64} {decl = 1 : i1}
    maxon.br dispatch_0.cmp0
  dispatch_0.cmp0:
    %9 = maxon.var_ref {var = __match_dispatch_0} {type = i64}
    %10 = maxon.literal {value = 1 : i64}
    %11 = maxon.binop %9, %10 {op = eq}
    maxon.cond_br %11 [then: dispatch_0.case0, else: dispatch_0.cmp1]
  dispatch_0.case0:
    %12 = maxon.literal {value = 100 : i64}
    maxon.scope_end [key, __match_dispatch_0]
    maxon.return %12
  dispatch_0.cmp1:
    %13 = maxon.var_ref {var = __match_dispatch_0} {type = i64}
    %14 = maxon.literal {value = 2 : i64}
    %15 = maxon.binop %13, %14 {op = eq}
    maxon.cond_br %15 [then: dispatch_0.case1, else: dispatch_0.case2]
  dispatch_0.case1:
    %16 = maxon.literal {value = 200 : i64}
    maxon.scope_end [key, __match_dispatch_0]
    maxon.return %16
  dispatch_0.case2:
    %17 = maxon.enum_literal @MyError.notFound
    maxon.scope_end [key, __match_dispatch_0]
    maxon.throw @MyError %17
  dispatch_0.merge:
  }
  func @main() -> i64 {
  entry:
    %18 = maxon.literal {value = 2 : i64}
    %21, %20 = maxon.try_call @lookup %18
    %22 = maxon.literal {value = 0 : i64}
    maxon.assign %22 {var = __try_default_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %21 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %23 = maxon.literal {value = 0 : i64}
    %24 = maxon.binop %20, %23 {op = ne}
    maxon.cond_br %24 [then: otherwise_default_error_0, else: otherwise_default_continue_0]
  otherwise_default_error_0:
    %25 = maxon.var_ref {var = __try_default_0} {type = i64}
    maxon.assign %25 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_0
  otherwise_default_continue_0:
    %26 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %26 {var = a} {kind = i64} {decl = 1 : i1}
    %27 = maxon.literal {value = 99 : i64}
    %30, %29 = maxon.try_call @lookup %27
    %31 = maxon.literal {value = 42 : i64}
    maxon.assign %31 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %30 {var = __try_result_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %32 = maxon.literal {value = 0 : i64}
    %33 = maxon.binop %29, %32 {op = ne}
    maxon.cond_br %33 [then: otherwise_default_error_1, else: otherwise_default_continue_1]
  otherwise_default_error_1:
    %34 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %34 {var = __try_result_1} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_1
  otherwise_default_continue_1:
    %35 = maxon.var_ref {var = __try_result_1} {type = i64}
    maxon.assign %35 {var = b} {kind = i64} {decl = 1 : i1}
    %36 = maxon.literal {value = 256 : i64}
    %37 = maxon.binop %35, %36 {op = mod} {optimalType = i64}
    %38 = maxon.var_ref {var = a} {type = i64}
    %39 = maxon.binop %38, %37 {op = add}
    %40 = maxon.literal {value = 0 : i64}
    %41 = maxon.binop %39, %40 {op = lt}
    %42 = maxon.literal {value = 4294967295 : i64}
    %43 = maxon.binop %39, %42 {op = gt}
    %44 = maxon.binop %41, %43 {op = or}
    maxon.cond_br %44 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at error-throw-in-match.test:21: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [__try_default_0, a, __try_default_1, b, __try_result_0, __try_result_1]
    maxon.return %39
  }
}
=== standard
module {
  func @lookup(key: i64) -> i64 {
  entry:
    %0 = func.param key : StdI64
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
  func @main() -> u32 {
  entry:
    %12 = arith.constant {value = 2 : i64}
    %13, %14 = func.try_call @lookup %12
    %15 = arith.constant {value = 0 : i64}
    memref.store %15, __try_default_0
    memref.store %13, __try_result_0
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi ne %14, %16
    cf.cond_br %17 [then: otherwise_default_error_0, else: otherwise_default_continue_0]
  otherwise_default_error_0:
    %18 = memref.load __try_default_0 : i64
    memref.store %18, __try_result_0
    cf.br otherwise_default_continue_0
  otherwise_default_continue_0:
    %19 = memref.load __try_result_0 : i64
    memref.store %19, a
    %20 = arith.constant {value = 99 : i64}
    %21, %22 = func.try_call @lookup %20
    %23 = arith.constant {value = 42 : i64}
    memref.store %23, __try_default_1
    memref.store %21, __try_result_1
    %24 = arith.constant {value = 0 : i64}
    %25 = arith.cmpi ne %22, %24
    cf.cond_br %25 [then: otherwise_default_error_1, else: otherwise_default_continue_1]
  otherwise_default_error_1:
    %26 = memref.load __try_default_1 : i64
    memref.store %26, __try_result_1
    cf.br otherwise_default_continue_1
  otherwise_default_continue_1:
    %27 = memref.load __try_result_1 : i64
    %28 = arith.constant {value = 256 : i64}
    %29 = arith.remsi %27, %28
    %30 = memref.load a : i64
    %31 = arith.addi %30, %29
    %32 = arith.constant {value = 0 : i64}
    %33 = arith.cmpi lt %31, %32
    %34 = arith.constant {value = 4294967295 : i64}
    %35 = arith.cmpi gt %31, %34
    %36 = arith.ori1 %33, %35
    cf.cond_br %36 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %37 = memref.lea_symdata __panic_msg_0
    %38 = std.ptr_to_i64 %37
    std.call_runtime @mrt_panic %38
  __range_ok_0:
    func.return %31
  }
}
=== x86
module {
  func @lookup(key: i64) -> i64 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.jmp lookup.dispatch_0.cmp0
  dispatch_0.cmp0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 1
    x64.cmp rax, rcx
    x64.jne lookup.dispatch_0.cmp1
  dispatch_0.case0:
    x64.mov rax, 100
    x64.xor edx, edx
    x64.epilogue
    x64.ret
  dispatch_0.cmp1:
    x64.mov rax, [rbp-8]
    x64.mov rcx, 2
    x64.cmp rax, rcx
    x64.jne lookup.dispatch_0.case2
  dispatch_0.case1:
    x64.mov rax, 200
    x64.xor edx, edx
    x64.epilogue
    x64.ret
  dispatch_0.case2:
    x64.mov rax, 1
    x64.mov rcx, 1
    x64.add rax, rcx
    x64.mov rdx, rax
    x64.xor eax, eax
    x64.epilogue
    x64.ret
  dispatch_0.merge:
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=48
    x64.mov rcx, 2
    x64.call lookup
    x64.xor ecx, ecx
    x64.mov [rbp-8], rcx
    x64.mov [rbp-16], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je main.otherwise_default_continue_0
  otherwise_default_error_0:
    x64.mov rax, [rbp-8]
    x64.mov [rbp-16], rax
    x64.jmp main.otherwise_default_continue_0
  otherwise_default_continue_0:
    x64.mov rax, [rbp-16]
    x64.mov [rbp-24], rax
    x64.mov rcx, 99
    x64.call lookup
    x64.mov rcx, 42
    x64.mov [rbp-32], rcx
    x64.mov [rbp-40], rax
    x64.xor eax, eax
    x64.cmp rdx, rax
    x64.je main.otherwise_default_continue_1
  otherwise_default_error_1:
    x64.mov rax, [rbp-32]
    x64.mov [rbp-40], rax
    x64.jmp main.otherwise_default_continue_1
  otherwise_default_continue_1:
    x64.mov rax, [rbp-40]
    x64.mov rcx, 256
    x64.cqo
    x64.idiv rcx
    x64.mov rax, [rbp-24]
    x64.add rax, rdx
    x64.xor edx, edx
    x64.mov ebx, 4294967295
    x64.cmp rax, rbx
    x64.jg main.__range_panic_0
    x64.cmp rax, rdx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @register-allocator.lookup(key: i64) -> i64 {
  entry:
    %8 = maxon.param {index = 0 : i32} {name = key} {type = i64}
    maxon.assign %8 {var = __match_dispatch_0} {kind = i64} {decl = 1 : i1}
    maxon.br dispatch_0.cmp0
  dispatch_0.cmp0:
    %9 = maxon.var_ref {var = __match_dispatch_0} {type = i64}
    %10 = maxon.literal {value = 1 : i64}
    %11 = maxon.binop %9, %10 {op = eq}
    maxon.cond_br %11 [then: dispatch_0.case0, else: dispatch_0.cmp1]
  dispatch_0.case0:
    %12 = maxon.literal {value = 100 : i64}
    maxon.scope_end [key, __match_dispatch_0]
    maxon.return %12
  dispatch_0.cmp1:
    %13 = maxon.var_ref {var = __match_dispatch_0} {type = i64}
    %14 = maxon.literal {value = 2 : i64}
    %15 = maxon.binop %13, %14 {op = eq}
    maxon.cond_br %15 [then: dispatch_0.case1, else: dispatch_0.case2]
  dispatch_0.case1:
    %16 = maxon.literal {value = 200 : i64}
    maxon.scope_end [key, __match_dispatch_0]
    maxon.return %16
  dispatch_0.case2:
    %17 = maxon.enum_literal @MyError.notFound
    maxon.scope_end [key, __match_dispatch_0]
    maxon.throw @MyError %17
  dispatch_0.merge:
  }
  func @main() -> i64 {
  entry:
    %18 = maxon.literal {value = 2 : i64}
    %21, %20 = maxon.try_call @register-allocator.lookup %18
    %22 = maxon.literal {value = 0 : i64}
    maxon.assign %22 {var = __try_default_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %21 {var = __try_result_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %23 = maxon.literal {value = 0 : i64}
    %24 = maxon.binop %20, %23 {op = ne}
    maxon.cond_br %24 [then: otherwise_default_error_2, else: otherwise_default_continue_3]
  otherwise_default_error_2:
    %25 = maxon.var_ref {var = __try_default_1} {type = i64}
    maxon.assign %25 {var = __try_result_0} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %26 = maxon.var_ref {var = __try_result_0} {type = i64}
    maxon.assign %26 {var = a} {kind = i64} {decl = 1 : i1}
    %27 = maxon.literal {value = 99 : i64}
    %30, %29 = maxon.try_call @register-allocator.lookup %27
    %31 = maxon.literal {value = 42 : i64}
    maxon.assign %31 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %30 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %32 = maxon.literal {value = 0 : i64}
    %33 = maxon.binop %29, %32 {op = ne}
    maxon.cond_br %33 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %34 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %34 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %35 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %35 {var = b} {kind = i64} {decl = 1 : i1}
    %36 = maxon.literal {value = 256 : i64}
    %37 = maxon.binop %35, %36 {op = mod}
    %38 = maxon.var_ref {var = a} {type = i64}
    %39 = maxon.binop %38, %37 {op = add}
    %40 = maxon.literal {value = 0 : i64}
    %41 = maxon.binop %39, %40 {op = lt}
    %42 = maxon.literal {value = 4294967295 : i64}
    %43 = maxon.binop %39, %42 {op = gt}
    %44 = maxon.binop %41, %43 {op = or}
    maxon.cond_br %44 [then: __range_panic_8, else: __range_ok_8]
  __range_panic_8:
    maxon.panic "panic at error-throw-in-match.test:21: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_8:
    maxon.scope_end [__try_default_1, a, __try_default_5, b, __try_result_0, __try_result_4]
    maxon.return %39
  }
}
=== standard
module {
  func @register-allocator.lookup(key: i64) -> i64 {
  entry:
    %0 = func.param key : StdI64
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
  func @main() -> u32 {
  entry:
    %12 = arith.constant {value = 2 : i64}
    %13, %14 = func.try_call @register-allocator.lookup %12
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
    %28 = arith.constant {value = 256 : i64}
    %29 = arith.remsi %27, %28
    %30 = memref.load a : i64
    %31 = arith.addi %30, %29
    %32 = arith.constant {value = 0 : i64}
    %33 = arith.cmpi lt %31, %32
    %34 = arith.constant {value = 4294967295 : i64}
    %35 = arith.cmpi gt %31, %34
    %36 = arith.ori1 %33, %35
    cf.cond_br %36 [then: __range_panic_8, else: __range_ok_8]
  __range_panic_8:
    %37 = memref.lea_symdata __panic_msg_0
    %38 = std.ptr_to_i64 %37
    std.call_runtime @maxon_panic %38
  __range_ok_8:
    func.return %31
  }
}
=== arm64
module {
  func @register-allocator.lookup(key: i64) -> i64 {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.b register-allocator.lookup.dispatch_0.cmp0
  dispatch_0.cmp0:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne register-allocator.lookup.dispatch_0.case0
    arm64.b register-allocator.lookup.dispatch_0.cmp1
  dispatch_0.case0:
    arm64.mov x0, #100
    arm64.mov x1, #0
    arm64.epilogue stack_size=48
    arm64.ret
  dispatch_0.cmp1:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x2, eq
    arm64.cmp x2, #0
    arm64.b.ne register-allocator.lookup.dispatch_0.case1
    arm64.b register-allocator.lookup.dispatch_0.case2
  dispatch_0.case1:
    arm64.mov x0, #200
    arm64.mov x1, #0
    arm64.epilogue stack_size=48
    arm64.ret
  dispatch_0.case2:
    arm64.mov x0, #1
    arm64.mov x1, #1
    arm64.add x2, x0, x1
    arm64.mov x1, x2
    arm64.mov x0, #0
    arm64.epilogue stack_size=48
    arm64.ret
  dispatch_0.merge:
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=112
    arm64.mov x0, #2
    arm64.bl register-allocator.lookup
    arm64.mov x2, #0
    arm64.str x2, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_2
    arm64.b main.otherwise_default_continue_3
  otherwise_default_error_2:
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.b main.otherwise_default_continue_3
  otherwise_default_continue_3:
    arm64.ldr x0, [x29, #-16]
    arm64.str x0, [x29, #-24]
    arm64.mov x0, #99
    arm64.bl register-allocator.lookup
    arm64.mov x2, #42
    arm64.str x2, [x29, #-32]
    arm64.str x0, [x29, #-40]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_6
    arm64.b main.otherwise_default_continue_7
  otherwise_default_error_6:
    arm64.ldr x0, [x29, #-32]
    arm64.str x0, [x29, #-40]
    arm64.b main.otherwise_default_continue_7
  otherwise_default_continue_7:
    arm64.ldr x0, [x29, #-40]
    arm64.mov x1, #256
    arm64.sdiv x2, x0, x1
    arm64.msub x3, x2, x1, x0
    arm64.ldr x4, [x29, #-24]
    arm64.add x5, x4, x3
    arm64.mov x6, #0
    arm64.cmp x5, x6
    arm64.cset x7, lt
    arm64.mov x8, #4294967295
    arm64.cmp x5, x8
    arm64.cset x9, gt
    arm64.orr x10, x7, x9
    arm64.cmp x10, #0
    arm64.b.ne main.__range_panic_8
    arm64.b main.__range_ok_8
  __range_panic_8:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_8:
    arm64.mov x0, x5
    arm64.epilogue stack_size=112
    arm64.ret
  }
}
```

### Level 7: Phi-Merge Splitting and Memory-Only Phi Spilling

These tests exercise the LiveRangeSplitter (which breaks each phi-merge's
disjoint anchor intervals into independent sub-ranges so the chordal
allocator doesn't over-coalesce interference) and its memory-only phi
fallback (which spills the parent merge when a sub-range can't be colored,
mirroring LLVM Greedy's stack-slot demotion). Without these techniques the
chordal SSA coloring panics at `colorLookupGpr` on URL.resolve and on
similar functions that mutate many locals across nested control flow.

<!-- test: phi-merge-split-multi-anchor -->
```maxon
function main() returns ExitCode
	var a = 0
	var b = 0
	var c = 0
	var d = 0
	if 1 < 2 'g1'
		a = 1
		b = 2
		c = 3
		d = 4
	end 'g1' else 'g1e'
		a = 10
		b = 20
		c = 30
		d = 40
	end 'g1e'
	if a > 0 'g2'
		a = a + 100
		c = c + 100
	end 'g2' else 'g2e'
		b = b + 100
		d = d + 100
	end 'g2e'
	if b > 0 'g3'
		a = a + b
		c = c + d
	end 'g3' else 'g3e'
		b = a - 1
		d = c - 1
	end 'g3e'
	return (a + b + c + d) mod 256
end 'main'
```
```exitcode
216
```

