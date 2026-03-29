---
feature: basics
status: stable
keywords: [main, return, semantic, validation]
category: basics
---

## Documentation

The compiler performs semantic checks before lowering the MLIR pipeline. These checks validate program structure requirements.

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
```RequiredLowering:x86_64-windows
=== After semantic-check ===
Functions: 2, Blocks: 2, Ops: 10
  maxhl: 10, arith: 0, cf: 0, func: 0, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After dead-function-elimination ===
Functions: 1, Blocks: 1, Ops: 2
  maxhl: 2, arith: 0, cf: 0, func: 0, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After convert-maxon-to-arith ===
Functions: 1, Blocks: 1, Ops: 2
  maxhl: 0, arith: 1, cf: 0, func: 1, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After borrow-check ===
Functions: 1, Blocks: 1, Ops: 2
  maxhl: 0, arith: 1, cf: 0, func: 1, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After inject-drops ===
Functions: 1, Blocks: 1, Ops: 2
  maxhl: 0, arith: 1, cf: 0, func: 1, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After mem2reg ===
Functions: 1, Blocks: 1, Ops: 2
  maxhl: 0, arith: 1, cf: 0, func: 1, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After canonicalize ===
Functions: 1, Blocks: 1, Ops: 2
  maxhl: 0, arith: 1, cf: 0, func: 1, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After cse ===
Functions: 1, Blocks: 1, Ops: 2
  maxhl: 0, arith: 1, cf: 0, func: 1, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After dce ===
Functions: 1, Blocks: 1, Ops: 2
  maxhl: 0, arith: 1, cf: 0, func: 1, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After insert-range-checks ===
Functions: 1, Blocks: 1, Ops: 2
  maxhl: 0, arith: 1, cf: 0, func: 1, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After convert-maxon-to-sys-and-runtime ===
Functions: 1, Blocks: 1, Ops: 2
  maxhl: 0, arith: 1, cf: 0, func: 1, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After lower-abi ===
Functions: 1, Blocks: 1, Ops: 2
  maxhl: 0, arith: 1, cf: 0, func: 1, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 0, arm64: 0
=== After augment-with-runtime ===
Functions: 9, Blocks: 35, Ops: 233
  maxhl: 0, arith: 83, cf: 25, func: 32, memref: 67, runtime: 0, sys: 26, mir: 0, x64: 0, arm64: 0
=== After convert-to-mir ===
Functions: 9, Blocks: 35, Ops: 233
  maxhl: 0, arith: 0, cf: 0, func: 0, memref: 67, runtime: 0, sys: 9, mir: 157, x64: 0, arm64: 0
=== After schedule-instructions ===
Functions: 9, Blocks: 35, Ops: 233
  maxhl: 0, arith: 0, cf: 0, func: 0, memref: 67, runtime: 0, sys: 9, mir: 157, x64: 0, arm64: 0
mid-to-x86: === lowering function: _start ===
mid-to-x86: === lowering function: write_stdout ===
mid-to-x86: === lowering function: write_stderr ===
mid-to-x86: === lowering function: i64_to_string ===
mid-to-x86: === lowering function: write_cstr_stderr ===
mid-to-x86: === lowering function: maxon_panic ===
mid-to-x86: === lowering function: maxon_panic_print_frame ===
mid-to-x86: === lowering function: __rt_printInt ===
mid-to-x86: === lowering function: main ===
=== After convert-mir-to-target ===
Functions: 9, Blocks: 35, Ops: 376
  maxhl: 0, arith: 0, cf: 0, func: 0, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 376, arm64: 0
regalloc: --- block 'entry' (7 ops) func=_start ---
regalloc: clobber caller-saved (func=_start)
regalloc: --- block 'entry' (16 ops) func=write_stdout ---
regalloc: --- block 'entry' (16 ops) func=write_stderr ---
regalloc: --- block 'entry' (7 ops) func=i64_to_string ---
regalloc: spill-all-before-terminator (func=i64_to_string)
regalloc: --- block 'zero_case' (10 ops) func=i64_to_string ---
regalloc: remat v13=0 into rcx (func=i64_to_string)
regalloc: remat v14=48 into rdx (func=i64_to_string)
regalloc: --- block 'check_neg' (7 ops) func=i64_to_string ---
regalloc: remat v16=0 into rax (func=i64_to_string)
regalloc: spill-all-before-terminator (func=i64_to_string)
regalloc: --- block 'negate' (5 ops) func=i64_to_string ---
regalloc: spill-all-before-terminator (func=i64_to_string)
regalloc: remat v22=1 into rcx (func=i64_to_string)
regalloc: --- block 'setup' (11 ops) func=i64_to_string ---
regalloc: remat v27=0 into rcx (func=i64_to_string)
regalloc: remat v26=0 into rdx (func=i64_to_string)
regalloc: spill-all-before-terminator (func=i64_to_string)
regalloc: --- block 'digit_loop' (29 ops) func=i64_to_string ---
regalloc: remat v32=10 into rcx (func=i64_to_string)
regalloc: reload v31 from [rbp-56] into rbx (func=i64_to_string)
regalloc: relocate v36 from rdx to rbx (func=i64_to_string)
regalloc: reload v30 from [rbp-64] into rax (func=i64_to_string)
regalloc: remat v37=0 into rdx (func=i64_to_string)
regalloc: spill-all-before-terminator (func=i64_to_string)
regalloc: --- block 'check_sign' (4 ops) func=i64_to_string ---
regalloc: spill-all-before-terminator (func=i64_to_string)
regalloc: --- block 'prepend_minus' (10 ops) func=i64_to_string ---
regalloc: remat v48=0 into rcx (func=i64_to_string)
regalloc: remat v47=45 into rdx (func=i64_to_string)
regalloc: spill-all-before-terminator (func=i64_to_string)
regalloc: --- block 'copy_phase' (15 ops) func=i64_to_string ---
regalloc: reload v54 from [rbp-16] into rbx (func=i64_to_string)
regalloc: --- block 'entry' (3 ops) func=write_cstr_stderr ---
regalloc: spill-all-before-terminator (func=write_cstr_stderr)
regalloc: remat v56=0 into rax (func=write_cstr_stderr)
regalloc: --- block 'scan' (10 ops) func=write_cstr_stderr ---
regalloc: spill-all-before-terminator (func=write_cstr_stderr)
regalloc: --- block 'scan_next' (5 ops) func=write_cstr_stderr ---
regalloc: spill-all-before-terminator (func=write_cstr_stderr)
regalloc: --- block 'write' (9 ops) func=write_cstr_stderr ---
regalloc: clobber caller-saved (func=write_cstr_stderr)
regalloc: spill v67 from rax to [rbp-56] (func=write_cstr_stderr)
regalloc: --- block 'entry' (16 ops) func=maxon_panic ---
regalloc: clobber caller-saved (func=maxon_panic)
regalloc: spill v74 from rax to [rbp-136] (func=maxon_panic)
regalloc: spill v73 from rdx to [rbp-144] (func=maxon_panic)
regalloc: spill v71 from rbx to [rbp-152] (func=maxon_panic)
regalloc: reload v71 from [rbp-152] into rdx (func=maxon_panic)
regalloc: clobber caller-saved (func=maxon_panic)
regalloc: spill v70 from rax to [rbp-160] (func=maxon_panic)
regalloc: reload v73 from [rbp-144] into rcx (func=maxon_panic)
regalloc: reload v74 from [rbp-136] into rdx (func=maxon_panic)
regalloc: spill-all-before-terminator (func=maxon_panic)
regalloc: remat v76=32 into rbx (func=maxon_panic)
regalloc: --- block 'walk_loop' (5 ops) func=maxon_panic ---
regalloc: spill-all-before-terminator (func=maxon_panic)
regalloc: --- block 'check_frame_ptr' (10 ops) func=maxon_panic ---
regalloc: spill-all-before-terminator (func=maxon_panic)
regalloc: --- block 'get_return_addr' (10 ops) func=maxon_panic ---
regalloc: spill-all-before-terminator (func=maxon_panic)
regalloc: --- block 'compute_offset' (9 ops) func=maxon_panic ---
regalloc: spill-all-before-terminator (func=maxon_panic)
regalloc: --- block 'check_upper_bound' (7 ops) func=maxon_panic ---
regalloc: spill-all-before-terminator (func=maxon_panic)
regalloc: --- block 'print_frame' (12 ops) func=maxon_panic ---
regalloc: clobber caller-saved (func=maxon_panic)
regalloc: spill-all-before-terminator (func=maxon_panic)
regalloc: --- block 'walk_done' (6 ops) func=maxon_panic ---
regalloc: remat v105=1 into rax (func=maxon_panic)
regalloc: --- block 'entry' (19 ops) func=maxon_panic_print_frame ---
regalloc: clobber caller-saved (func=maxon_panic_print_frame)
regalloc: spill v109 from rcx to [rbp-128] (func=maxon_panic_print_frame)
regalloc: reload v108 from [rbp-16] into rcx (func=maxon_panic_print_frame)
regalloc: remat v112=4294967295 into rsi (func=maxon_panic_print_frame)
regalloc: remat v114=0 into rdi (func=maxon_panic_print_frame)
regalloc: spill-all-before-terminator (func=maxon_panic_print_frame)
regalloc: remat v115=-1 into r8 (func=maxon_panic_print_frame)
regalloc: --- block 'lookup_loop' (4 ops) func=maxon_panic_print_frame ---
regalloc: spill-all-before-terminator (func=maxon_panic_print_frame)
regalloc: --- block 'lookup_body' (22 ops) func=maxon_panic_print_frame ---
regalloc: remat v123=8 into rdx (func=maxon_panic_print_frame)
regalloc: remat v127=4294967295 into r8 (func=maxon_panic_print_frame)
regalloc: spill-all-before-terminator (func=maxon_panic_print_frame)
regalloc: --- block 'lookup_update' (11 ops) func=maxon_panic_print_frame ---
regalloc: remat v135=4294967295 into rbx (func=maxon_panic_print_frame)
regalloc: spill-all-before-terminator (func=maxon_panic_print_frame)
regalloc: --- block 'lookup_next' (5 ops) func=maxon_panic_print_frame ---
regalloc: spill-all-before-terminator (func=maxon_panic_print_frame)
regalloc: --- block 'lookup_done' (5 ops) func=maxon_panic_print_frame ---
regalloc: remat v141=-1 into rcx (func=maxon_panic_print_frame)
regalloc: spill-all-before-terminator (func=maxon_panic_print_frame)
regalloc: --- block 'print_name' (7 ops) func=maxon_panic_print_frame ---
regalloc: clobber caller-saved (func=maxon_panic_print_frame)
regalloc: spill v145 from rcx to [rbp-176] (func=maxon_panic_print_frame)
regalloc: spill-all-before-terminator (func=maxon_panic_print_frame)
regalloc: --- block 'print_unknown' (3 ops) func=maxon_panic_print_frame ---
regalloc: clobber caller-saved (func=maxon_panic_print_frame)
regalloc: spill v147 from rcx to [rbp-192] (func=maxon_panic_print_frame)
regalloc: spill-all-before-terminator (func=maxon_panic_print_frame)
regalloc: --- block 'print_newline' (7 ops) func=maxon_panic_print_frame ---
regalloc: clobber caller-saved (func=maxon_panic_print_frame)
regalloc: spill v149 from rcx to [rbp-208] (func=maxon_panic_print_frame)
regalloc: spill v150 from rax to [rbp-216] (func=maxon_panic_print_frame)
regalloc: --- block 'entry' (16 ops) func=__rt_printInt ---
regalloc: clobber caller-saved (func=__rt_printInt)
regalloc: spill v152 from rcx to [rbp-40] (func=__rt_printInt)
regalloc: reload v155 from [rbp-24] into rdx (func=__rt_printInt)
regalloc: clobber caller-saved (func=__rt_printInt)
regalloc: spill v154 from rdx to [rbp-48] (func=__rt_printInt)
regalloc: spill v156 from rax to [rbp-56] (func=__rt_printInt)
regalloc: --- block 'entry' (3 ops) func=main ---
=== After allocate-registers ===
Functions: 9, Blocks: 35, Ops: 339
  maxhl: 0, arith: 0, cf: 0, func: 0, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 339, arm64: 0
  frame: var=16 spill=0 total=16 aligned=16
  frame: var=64 spill=0 total=64 aligned=64
  frame: var=64 spill=0 total=64 aligned=64
  frame: var=200 spill=32 total=232 aligned=240
  frame: var=40 spill=16 total=56 aligned=64
  frame: var=128 spill=96 total=224 aligned=224
  frame: var=120 spill=96 total=216 aligned=224
  frame: var=32 spill=32 total=64 aligned=64
  frame: var=0 spill=0 total=0 aligned=0
=== After insert-prologue-epilogue ===
Functions: 9, Blocks: 35, Ops: 346
  maxhl: 0, arith: 0, cf: 0, func: 0, memref: 0, runtime: 0, sys: 0, mir: 0, x64: 346, arm64: 0
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
```RequiredMLIR:x86_64-windows
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
=== x86
module {
  func @basics.getValue() -> i64 {
  entry:
    x86.mov rax, 42
    x86.ret
  }
  func @main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.call basics.getValue
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.setl rcx
    x86.movzx rcx, rcxb
    x86.mov rdx, 4294967295
    x86.cmp rax, rdx
    x86.setg rdx
    x86.movzx rdx, rdxb
    x86.or rcx, rdx
    x86.test rcx, rcx
    x86.je main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_0]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.epilogue
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
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
	var x = 3.14
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
```RequiredMLIR:x86_64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3.14 : f64}
    maxon.assign %0 {var = x} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
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
    x86.movsd xmm0, [rip+__float_3.14]
    x86.movsd xmm1, [rip+__float_3.14]
    x86.ucomisd xmm0, xmm1
    x86.jne main.other_1
    x86.jp main.other_1
  check_0:
    x86.mov rax, 1
    x86.ret
  other_1:
    x86.xor rax, rax
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 3.14 : f64}
    maxon.assign %0 {var = x} {kind = f64} {decl = 1 : i1} {mut = 1 : i1}
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



























































