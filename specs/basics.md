---
feature: basics
status: stable
keywords: [main, return, semantic, validation]
category: basics
---

## Documentation

The compiler performs semantic checks before lowering the IR pipeline. These checks validate program structure requirements.

### E3001: No main function

Every program must have a `main` function. If none is found, the compiler reports:

```text
error E3001: No 'main' function found
```

### E3002: Main wrong return type

The `main` function must return `ExitCode`. If it has no return type or returns a different type, the compiler reports:

```text
error E3002: Function 'main' must return ExitCode
```

## Tests

<!-- test: no-main -->
```maxon

typealias Integer = int(i64.min to i64.max)

function notmain() returns Integer
	return 42
end 'notmain'
```
```maxoncstderr
error E3001: No 'main' function found
```

<!-- test: main-no-return-type -->
```maxon
function main()
	return
end 'main'
```
```maxoncstderr
error E3002: Function 'main' must return ExitCode
```

<!-- test: valid-main -->
```maxon
function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```
```RequiredLowering:x64-windows
=== After semantic-check ===
Functions: 2, Blocks: 2, Ops: 10
  maxon: 10, std: 0, mir: 0, x64: 0, arm64: 0
=== After dead-function-elimination ===
Functions: 1, Blocks: 1, Ops: 2
  maxon: 2, std: 0, mir: 0, x64: 0, arm64: 0
=== After lower-maxon-to-std ===
Functions: 1, Blocks: 1, Ops: 2
  maxon: 0, std: 2, mir: 0, x64: 0, arm64: 0
=== After borrow-check ===
Functions: 1, Blocks: 1, Ops: 2
  maxon: 0, std: 2, mir: 0, x64: 0, arm64: 0
=== After inject-drops ===
Functions: 1, Blocks: 1, Ops: 2
  maxon: 0, std: 2, mir: 0, x64: 0, arm64: 0
=== After mem2reg ===
Functions: 1, Blocks: 1, Ops: 2
  maxon: 0, std: 2, mir: 0, x64: 0, arm64: 0
=== After canonicalize ===
Functions: 1, Blocks: 1, Ops: 2
  maxon: 0, std: 2, mir: 0, x64: 0, arm64: 0
=== After cse ===
Functions: 1, Blocks: 1, Ops: 2
  maxon: 0, std: 2, mir: 0, x64: 0, arm64: 0
=== After licm ===
Functions: 1, Blocks: 1, Ops: 2
  maxon: 0, std: 2, mir: 0, x64: 0, arm64: 0
=== After dce ===
Functions: 1, Blocks: 1, Ops: 2
  maxon: 0, std: 2, mir: 0, x64: 0, arm64: 0
=== After insert-range-checks ===
Functions: 1, Blocks: 1, Ops: 2
  maxon: 0, std: 2, mir: 0, x64: 0, arm64: 0
=== After lower-abi ===
Functions: 1, Blocks: 1, Ops: 2
  maxon: 0, std: 2, mir: 0, x64: 0, arm64: 0
=== After augment-with-runtime ===
Functions: 10, Blocks: 36, Ops: 255
  maxon: 0, std: 255, mir: 0, x64: 0, arm64: 0
=== After lower-std-to-mir ===
Functions: 10, Blocks: 36, Ops: 255
  maxon: 0, std: 0, mir: 255, x64: 0, arm64: 0
=== After schedule-instructions ===
Functions: 10, Blocks: 36, Ops: 255
  maxon: 0, std: 0, mir: 255, x64: 0, arm64: 0
mid-to-x64: === lowering function: mrt_start ===
mid-to-x64: === lowering function: mrt_write_stdout ===
mid-to-x64: === lowering function: mrt_write_stderr ===
mid-to-x64: === lowering function: mrt_i64_to_string ===
mid-to-x64: === lowering function: mrt_write_cstr_stderr ===
mid-to-x64: === lowering function: mrt_panic ===
mid-to-x64: === lowering function: mrt_panic_print_frame ===
mid-to-x64: === lowering function: mrt_alloc ===
mid-to-x64: === lowering function: mrt_printInt ===
mid-to-x64: === lowering function: main ===
=== After lower-mir-to-target ===
Functions: 10, Blocks: 36, Ops: 348
  maxon: 0, std: 0, mir: 0, x64: 348, arm64: 0
SSA regalloc: func=mrt_start colored=11 iterations=0
SSA regalloc: func=mrt_write_stdout colored=10 iterations=0
SSA regalloc: func=mrt_write_stderr colored=13 iterations=0
SSA regalloc: func=mrt_i64_to_string colored=80 iterations=0
SSA regalloc: func=mrt_write_cstr_stderr colored=81 iterations=0
SSA regalloc: func=mrt_panic colored=133 iterations=0
SSA regalloc: func=mrt_panic_print_frame colored=186 iterations=0
SSA regalloc: func=mrt_alloc colored=169 iterations=0
SSA regalloc: func=mrt_printInt colored=193 iterations=0
SSA regalloc: func=main colored=1 iterations=0
=== After allocate-registers ===
Functions: 10, Blocks: 36, Ops: 296
  maxon: 0, std: 0, mir: 0, x64: 296, arm64: 0
  frame: var=16 spill=0 total=16 aligned=16
  frame: var=64 spill=0 total=64 aligned=64
  frame: var=64 spill=0 total=64 aligned=64
  frame: var=200 spill=0 total=200 aligned=208
  frame: var=40 spill=0 total=40 aligned=48
  frame: var=96 spill=0 total=96 aligned=96
  frame: var=72 spill=0 total=72 aligned=80
  frame: var=40 spill=0 total=40 aligned=48
  frame: var=32 spill=0 total=32 aligned=32
  frame: var=0 spill=0 total=0 aligned=0
=== After insert-prologue-epilogue ===
Functions: 10, Blocks: 36, Ops: 304
  maxon: 0, std: 0, mir: 0, x64: 304, arm64: 0
```

<!-- test: return-function-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getValue() returns Integer
	return 42
end 'getValue'

function main() returns ExitCode
	return getValue()
end 'main'
```
```exitcode
42
```
```RequiredIR:x64-windows
=== maxon
module {
  func @basics.getValue() -> i64 {
  entry:
    %0 = maxon.literal {value = 42 : i64}
    maxon.scope_end []
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.call @basics.getValue
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.binop %1, %2 {op = lt}
    %4 = maxon.literal {value = 4294967295 : i64}
    %5 = maxon.binop %1, %4 {op = gt}
    %6 = maxon.binop %3, %5 {op = or}
    maxon.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at return-function-call.test:10: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %1
  }
}
=== standard
module {
  func @basics.getValue() -> i64 {
  entry:
    %0 = arith.constant {value = 42 : i64}
    func.return %0
  }
  func @main() -> u32 {
  entry:
    %1 = func.call @basics.getValue
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
  func @basics.getValue() -> i64 {
  entry:
    x64.mov rax, 42
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.call basics.getValue
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
  func @basics.getValue() -> i64 {
  entry:
    %0 = maxon.literal {value = 42 : i64}
    maxon.scope_end []
    maxon.return %0
  }
  func @main() -> i64 {
  entry:
    %1 = maxon.call @basics.getValue
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.binop %1, %2 {op = lt}
    %4 = maxon.literal {value = 4294967295 : i64}
    %5 = maxon.binop %1, %4 {op = gt}
    %6 = maxon.binop %3, %5 {op = or}
    maxon.cond_br %6 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at return-function-call.test:10: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %1
  }
}
=== standard
module {
  func @basics.getValue() -> i64 {
  entry:
    %0 = arith.constant {value = 42 : i64}
    func.return %0
  }
  func @main() -> u32 {
  entry:
    %1 = func.call @basics.getValue
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
  func @basics.getValue() -> i64 {
  entry:
    arm64.mov x0, #42
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.bl basics.getValue
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

<!-- test: float-var-if-else -->
```maxon
function main() returns ExitCode
	let x = 3.14
	if x == 3.14 'check'
		return 1
	end 'check' else 'other'
		return 0
	end 'other'
end 'main'
```
```exitcode
1
```
```RequiredRdata
f64 3.14
```
```RequiredIR:x64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3.14 : f64}
    maxon.assign %0 {var = x} {kind = f64} {decl = 1 : i1}
    %1 = maxon.literal {value = 3.14 : f64}
    %2 = maxon.binop %0, %1 {op = eq} {kind = f64}
    maxon.cond_br %2 [then: check_0, else: other_1]
  check_0:
    %3 = maxon.literal {value = 1 : i64}
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
    %0 = arith.float_constant {value = 3.14 : f64}
    %1 = arith.float_constant {value = 3.14 : f64}
    %2 = arith.cmpf eq %0, %1
    cf.cond_br %2 [then: check_0, else: other_1]
  check_0:
    %3 = arith.constant {value = 1 : i64}
    func.return %3
  other_1:
    %4 = arith.constant {value = 0 : i64}
    func.return %4
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x64.movsd xmm0, [rip+__float_3.14]
    x64.movsd xmm1, [rip+__float_3.14]
    x64.ucomisd xmm0, xmm1
    x64.jne main.other_1
    x64.jp main.other_1
  check_0:
    x64.mov rax, 1
    x64.ret
  other_1:
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
    %0 = maxon.literal {value = 3.14 : f64}
    maxon.assign %0 {var = x} {kind = f64} {decl = 1 : i1}
    %1 = maxon.literal {value = 3.14 : f64}
    %2 = maxon.binop %0, %1 {op = eq} {kind = f64}
    maxon.cond_br %2 [then: check_0, else: other_1]
  check_0:
    %3 = maxon.literal {value = 1 : i64}
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
    %0 = arith.float_constant {value = 3.14 : f64}
    %1 = arith.float_constant {value = 3.14 : f64}
    %2 = arith.cmpf eq %0, %1
    cf.cond_br %2 [then: check_0, else: other_1]
  check_0:
    %3 = arith.constant {value = 1 : i64}
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
    arm64.fldr d0, [__float_3.14]
    arm64.fldr d1, [__float_3.14]
    arm64.fcmp d0, d1
    arm64.cset x0, eq
    arm64.cmp x0, #0
    arm64.b.ne main.check_0
    arm64.b main.other_1
  check_0:
    arm64.mov x0, #1
    arm64.ret
  other_1:
    arm64.mov x0, #0
    arm64.ret
  }
}
```



























































