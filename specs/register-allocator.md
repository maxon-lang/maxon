---
feature: register-allocator
status: selfhosted
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 99
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #99
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #42
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #42
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #42
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #42
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #42
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 21
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #21
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 55
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #55
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
	return (a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p) and 125
end 'main'
```
```exitcode
8
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 8
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #8
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
	return (a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p + q + r + s + t) and 125
end 'main'
```
```exitcode
80
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 80
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #80
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
	return result and 125
end 'main'
```
```exitcode
80
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 80
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #80
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 90
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #90
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #42
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 24
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #24
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
	return (a + b) and 125
end 'main'
```
```exitcode
72
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 72
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #72
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #42
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #42
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #42
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
  check_0:
    x64.mov r8d, 2
  check_0.merge:
    x64.mov r9d, 4294967295
    x64.add r8, 40
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_9083dd3838d7ca20]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x1, #40
  check_0:
    arm64.mov x0, #2
  check_0.merge:
    arm64.mov x2, #255
    arm64.add x3, x0, x1
    arm64.cmp x3, x2
    arm64.cset x0, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_9083dd3838d7ca20
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.mov x0, x3
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor r8d, r8d
  loop_0.header:
    x64.cmp r8, 42
    x64.jge loop_0.exit
  loop_0:
    x64.add r8, 1
    x64.jmp loop_0.header
  loop_0.exit:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_fea5c1de7fb1df46]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #0
  loop_0.header:
    arm64.mov x1, #42
    arm64.cmp x0, x1
    arm64.cset x1, lt
    arm64.b.ge loop_0.exit
  loop_0:
    arm64.mov x1, #1
    arm64.add x2, x0, x1
    arm64.mov x0, x2
    arm64.b loop_0.header
  loop_0.exit:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_fea5c1de7fb1df46
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor r8d, r8d
    x64.xor eax, eax
  loop_0.header:
    x64.cmp r8, 10
    x64.jge loop_0.exit
  loop_0:
    x64.mov r9, r8
    x64.add r9, 1
    x64.add rax, r8
    x64.mov r8, r9
    x64.jmp loop_0.header
  loop_0.exit:
    x64.mov r8d, 256
    x64.cqo
    x64.idiv r8
    x64.mov r8, rdx
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_21624daf49be2c6a]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #0
    arm64.mov x1, #0
  loop_0.header:
    arm64.mov x2, #10
    arm64.cmp x0, x2
    arm64.cset x2, lt
    arm64.b.ge loop_0.exit
  loop_0:
    arm64.mov x2, #1
    arm64.add x3, x0, x2
    arm64.add x2, x1, x0
    arm64.mov x1, x2
    arm64.mov x0, x3
    arm64.b loop_0.header
  loop_0.exit:
    arm64.mov x2, #256
    arm64.msub x0, x1, x2
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_21624daf49be2c6a
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
	return (even_sum + odd_sum + count) and 125
end 'main'
```
```exitcode
72
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor eax, eax
    x64.xor r8d, r8d
    x64.xor r9d, r9d
    x64.xor esi, esi
    x64.mov rdi, rax
  loop_0.header:
    x64.cmp rdi, 20
    x64.jge loop_0.exit
  loop_0:
    x64.mov ecx, 2
    x64.mov rax, rdi
    x64.cqo
    x64.idiv rcx
    x64.test rdx, rdx
    x64.jne odd_0
    x64.jmp even_0
  loop_0.exit:
    x64.add rsi, r9
    x64.mov r9d, 125
    x64.add rsi, r8
    x64.mov edi, 4294967295
    x64.mov r8, rsi
    x64.and r8, r9
    x64.cmp r8, rdi
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  even_0:
    x64.add r8, 1
    x64.add rsi, rdi
    x64.jmp even_0.merge
  odd_0:
    x64.add r9, rdi
  even_0.merge:
    x64.mov rax, rdi
    x64.add rax, 1
    x64.mov rdi, rax
    x64.jmp loop_0.header
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_e449517a4b4af179]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #0
    arm64.mov x1, #0
    arm64.mov x2, #0
    arm64.mov x3, #0
  loop_0.header:
    arm64.mov x4, #20
    arm64.cmp x0, x4
    arm64.cset x4, lt
    arm64.b.ge loop_0.exit
  loop_0:
    arm64.mov x4, #2
    arm64.msub x5, x0, x4
    arm64.mov x4, #0
    arm64.cmp x5, x4
    arm64.cset x4, eq
    arm64.b.ne odd_0
    arm64.b even_0
  loop_0.exit:
    arm64.add x0, x3, x2
    arm64.mov x2, #125
    arm64.add x3, x0, x1
    arm64.mov x1, #255
    arm64.and x0, x3, x2
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
    arm64.b __range_panic_0
  even_0:
    arm64.mov x4, #1
    arm64.add x5, x1, x4
    arm64.add x1, x3, x0
    arm64.b even_0.merge
  odd_0:
    arm64.add x4, x2, x0
    arm64.mov x2, x4
    arm64.mov x5, x1
    arm64.mov x1, x3
  even_0.merge:
    arm64.mov x3, #1
    arm64.add x4, x0, x3
    arm64.mov x3, x1
    arm64.mov x1, x5
    arm64.mov x0, x4
    arm64.b loop_0.header
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_e449517a4b4af179
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 1
    x64.xor eax, eax
  loop_0.header:
    x64.cmp r8, 10
    x64.jg loop_0.exit
  loop_0:
    x64.cmp r8, 5
    x64.jg second_0
    x64.jmp first_0
  loop_0.exit:
    x64.mov r8d, 256
    x64.cqo
    x64.idiv r8
    x64.mov r8, rdx
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  first_0:
    x64.add rax, r8
    x64.jmp first_0.merge
  second_0:
    x64.mov r9d, 2
    x64.mov rsi, r8
    x64.imul rsi, r9
    x64.add rax, rsi
  first_0.merge:
    x64.add r8, 1
    x64.jmp loop_0.header
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_aea90f7456a1aa52]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #1
    arm64.mov x1, #0
  loop_0.header:
    arm64.mov x2, #10
    arm64.cmp x0, x2
    arm64.cset x2, le
    arm64.b.gt loop_0.exit
  loop_0:
    arm64.mov x2, #5
    arm64.cmp x0, x2
    arm64.cset x2, le
    arm64.b.gt second_0
    arm64.b first_0
  loop_0.exit:
    arm64.mov x2, #256
    arm64.msub x0, x1, x2
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
    arm64.b __range_panic_0
  first_0:
    arm64.add x2, x1, x0
    arm64.b first_0.merge
  second_0:
    arm64.mov x2, #2
    arm64.mul x3, x0, x2
    arm64.add x2, x1, x3
  first_0.merge:
    arm64.mov x1, #1
    arm64.add x3, x0, x1
    arm64.mov x1, x2
    arm64.mov x0, x3
    arm64.b loop_0.header
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_aea90f7456a1aa52
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor r8d, r8d
    x64.xor r9d, r9d
    x64.xchg r8, r9
  outer_0.header:
    x64.cmp r9, 5
    x64.jge outer_0.exit
  outer_0:
    x64.xor esi, esi
    x64.jmp inner_0.header
  outer_0.exit:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  inner_0.header:
    x64.cmp rsi, 4
    x64.jge inner_0.exit
  inner_0:
    x64.add rsi, 1
    x64.add r8, 1
    x64.jmp inner_0.header
  inner_0.exit:
    x64.add r9, 1
    x64.jmp outer_0.header
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_1dbec400299d1eb2]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #0
    arm64.mov x1, #0
    arm64.mov x16, x1
    arm64.mov x1, x0
    arm64.mov x0, x16
  outer_0.header:
    arm64.mov x2, #5
    arm64.cmp x1, x2
    arm64.cset x2, lt
    arm64.b.ge outer_0.exit
  outer_0:
    arm64.mov x2, #0
    arm64.b inner_0.header
  outer_0.exit:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
    arm64.b __range_panic_0
  inner_0.header:
    arm64.mov x3, #4
    arm64.cmp x2, x3
    arm64.cset x3, lt
    arm64.b.ge inner_0.exit
  inner_0:
    arm64.mov x3, #1
    arm64.mov x4, #1
    arm64.add x5, x2, x3
    arm64.add x2, x0, x4
    arm64.mov x0, x2
    arm64.mov x2, x5
    arm64.b inner_0.header
  inner_0.exit:
    arm64.mov x2, #1
    arm64.add x3, x1, x2
    arm64.mov x1, x3
    arm64.b outer_0.header
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_1dbec400299d1eb2
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 1
    x64.xor r9d, r9d
    x64.xchg r8, r9
  outer_0.header:
    x64.cmp r9, 5
    x64.jg outer_0.exit
  outer_0:
    x64.mov esi, 1
    x64.jmp inner_0.header
  outer_0.exit:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  inner_0.header:
    x64.cmp rsi, r9
    x64.jg inner_0.exit
  inner_0:
    x64.add rsi, 1
    x64.add r8, 1
    x64.jmp inner_0.header
  inner_0.exit:
    x64.add r9, 1
    x64.jmp outer_0.header
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_d8bcae5a113f3996]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #1
    arm64.mov x1, #0
    arm64.mov x16, x1
    arm64.mov x1, x0
    arm64.mov x0, x16
  outer_0.header:
    arm64.mov x2, #5
    arm64.cmp x1, x2
    arm64.cset x2, le
    arm64.b.gt outer_0.exit
  outer_0:
    arm64.mov x2, #1
    arm64.b inner_0.header
  outer_0.exit:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
    arm64.b __range_panic_0
  inner_0.header:
    arm64.cmp x2, x1
    arm64.cset x3, le
    arm64.b.gt inner_0.exit
  inner_0:
    arm64.mov x3, #1
    arm64.mov x4, #1
    arm64.add x5, x2, x3
    arm64.add x2, x0, x4
    arm64.mov x0, x2
    arm64.mov x2, x5
    arm64.b inner_0.header
  inner_0.exit:
    arm64.mov x2, #1
    arm64.add x3, x1, x2
    arm64.mov x1, x3
    arm64.b outer_0.header
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_d8bcae5a113f3996
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor r8d, r8d
    x64.xor r9d, r9d
    x64.xchg r8, r9
  loop_0.header:
    x64.cmp r9, 5
    x64.jge loop_0.exit
  loop_0:
    x64.mov esi, 2
    x64.mov rdi, r9
    x64.imul rdi, rsi
    x64.add r9, 1
    x64.add r8, rdi
    x64.jmp loop_0.header
  loop_0.exit:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_c8a3486f5d92e518]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #0
    arm64.mov x1, #0
    arm64.mov x16, x1
    arm64.mov x1, x0
    arm64.mov x0, x16
  loop_0.header:
    arm64.mov x2, #5
    arm64.cmp x1, x2
    arm64.cset x2, lt
    arm64.b.ge loop_0.exit
  loop_0:
    arm64.mov x2, #2
    arm64.mul x3, x1, x2
    arm64.mov x2, #1
    arm64.add x4, x1, x2
    arm64.add x1, x0, x3
    arm64.mov x0, x1
    arm64.mov x1, x4
    arm64.b loop_0.header
  loop_0.exit:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_c8a3486f5d92e518
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 32
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #32
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 40
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #40
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #42
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 45
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #45
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
module {
  func @factorial(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=16
    x64.mov r12, rcx
    x64.cmp r12, 1
    x64.jg base_0.after
  base_0:
    x64.mov r8d, 1
    x64.epilogue
    x64.ret
  base_0.after:
    x64.mov rcx, r12
    x64.sub rcx, 1
    x64.call factorial
    x64.mov r9, r12
    x64.imul r9, r8
    x64.mov r8, r9
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov ecx, 5
    x64.call factorial
    x64.mov r9d, 256
    x64.mov rax, r8
    x64.cqo
    x64.idiv r9
    x64.mov r8, rdx
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_65da0ac706b41d36]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @factorial(x0: i64) -> i64 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x19, x0
    arm64.mov x0, #1
    arm64.cmp x19, x0
    arm64.cset x0, le
    arm64.b.gt base_0.after
  base_0:
    arm64.mov x0, #1
    arm64.epilogue
    arm64.ret
  base_0.after:
    arm64.mov x0, #1
    arm64.sub x1, x19, x0
    arm64.mov x0, x1
    arm64.bl factorial
    arm64.mov x1, x0
    arm64.mul x0, x19, x1
    arm64.epilogue
    arm64.ret
  }
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #5
    arm64.bl factorial
    arm64.mov x1, x0
    arm64.mov x2, #256
    arm64.msub x0, x1, x2
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_65da0ac706b41d36
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor r8d, r8d
    x64.mov r9d, 5
    x64.mov esi, 3
    x64.mov edi, 1
  loop_0.header:
    x64.cmp r8, 3
    x64.jge loop_0.exit
  loop_0:
    x64.add r8, 1
    x64.add r9, 6
    x64.add rsi, 4
    x64.add rdi, 2
    x64.jmp loop_0.header
  loop_0.exit:
    x64.add rdi, rsi
    x64.add rdi, 4
    x64.add rdi, r9
    x64.mov r8d, 256
    x64.mov rax, rdi
    x64.add rax, 6
    x64.cqo
    x64.idiv r8
    x64.mov r8, rdx
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_3e90fd62943769c1]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #0
    arm64.mov x1, #6
    arm64.mov x2, #5
    arm64.mov x3, #4
    arm64.mov x4, #3
    arm64.mov x5, #2
    arm64.mov x6, #1
  loop_0.header:
    arm64.mov x7, #3
    arm64.cmp x0, x7
    arm64.cset x7, lt
    arm64.b.ge loop_0.exit
  loop_0:
    arm64.mov x7, #1
    arm64.add x8, x0, x7
    arm64.add x0, x2, x1
    arm64.add x2, x4, x3
    arm64.add x4, x6, x5
    arm64.mov x6, x4
    arm64.mov x4, x2
    arm64.mov x2, x0
    arm64.mov x0, x8
    arm64.b loop_0.header
  loop_0.exit:
    arm64.add x0, x6, x4
    arm64.add x4, x0, x3
    arm64.add x0, x4, x2
    arm64.mov x2, #256
    arm64.add x3, x0, x1
    arm64.msub x0, x3, x2
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_3e90fd62943769c1
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.movsd xmm0, [rip+__float_4613622566267157217]
    x64.movsd xmm1, [rip+__float_4614253070214989087]
    x64.addsd xmm1, xmm0
    x64.cvttsd2si r8, xmm1
    x64.mov r9d, 4294967295
    x64.add r8, 30
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_289b40fc57b8fe49]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.ldr d0, [rdata+__float_4613622566267157217]
    arm64.ldr d1, [rdata+__float_4614253070214989087]
    arm64.fadd d2, d1, d0
    arm64.mov x1, #30
    arm64.fcvtzs x2, d2
    arm64.mov x3, #255
    arm64.add x0, x2, x1
    arm64.cmp x0, x3
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_289b40fc57b8fe49
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor r8d, r8d
    x64.xor r9d, r9d
  outer_0.header:
    x64.cmp r8, 3
    x64.jge outer_0.exit
  outer_0:
    x64.xor esi, esi
    x64.jmp inner_0.header
  outer_0.exit:
    x64.mov esi, 4294967295
    x64.mov r8, r9
    x64.add r8, 100
    x64.cmp r8, rsi
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  inner_0.header:
    x64.cmp rsi, 3
    x64.jge inner_0.exit
  inner_0:
    x64.cmp r8, rsi
    x64.jne diag_0.after
    x64.jmp diag_0
  inner_0.exit:
    x64.add r8, 1
    x64.jmp outer_0.header
  diag_0:
    x64.add r9, 1
  diag_0.after:
    x64.add rsi, 1
    x64.jmp inner_0.header
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_34b9b0cb2410b454]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #0
    arm64.mov x1, #0
    arm64.mov x2, #100
  outer_0.header:
    arm64.mov x3, #3
    arm64.cmp x0, x3
    arm64.cset x3, lt
    arm64.b.ge outer_0.exit
  outer_0:
    arm64.mov x3, #0
    arm64.b inner_0.header
  outer_0.exit:
    arm64.mov x3, #255
    arm64.add x0, x1, x2
    arm64.cmp x0, x3
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
    arm64.b __range_panic_0
  inner_0.header:
    arm64.mov x4, #3
    arm64.cmp x3, x4
    arm64.cset x4, lt
    arm64.b.ge inner_0.exit
  inner_0:
    arm64.cmp x0, x3
    arm64.cset x4, eq
    arm64.b.ne diag_0.after
    arm64.b diag_0
  inner_0.exit:
    arm64.mov x3, #1
    arm64.add x4, x0, x3
    arm64.mov x0, x4
    arm64.b outer_0.header
  diag_0:
    arm64.mov x4, #1
    arm64.add x5, x1, x4
    arm64.mov x1, x5
  diag_0.after:
    arm64.mov x4, #1
    arm64.add x5, x3, x4
    arm64.mov x3, x5
    arm64.b inner_0.header
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_34b9b0cb2410b454
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
	return a and 125
end 'main'
```
```exitcode
105
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor r8d, r8d
    x64.mov r9d, 1
    x64.xor esi, esi
  loop_0.header:
    x64.cmp r8, 13
    x64.jge loop_0.exit
  loop_0:
    x64.add r8, 1
    x64.add rsi, r9
    x64.xchg rsi, r9
    x64.jmp loop_0.header
  loop_0.exit:
    x64.mov r9d, 125
    x64.mov edi, 4294967295
    x64.mov r8, rsi
    x64.and r8, r9
    x64.cmp r8, rdi
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_5ffb4c10096b25df]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #0
    arm64.mov x1, #1
    arm64.mov x2, #0
  loop_0.header:
    arm64.mov x3, #13
    arm64.cmp x0, x3
    arm64.cset x3, lt
    arm64.b.ge loop_0.exit
  loop_0:
    arm64.mov x3, #1
    arm64.add x4, x0, x3
    arm64.add x0, x2, x1
    arm64.mov x2, x1
    arm64.mov x1, x0
    arm64.mov x0, x4
    arm64.b loop_0.header
  loop_0.exit:
    arm64.mov x1, #125
    arm64.mov x3, #255
    arm64.and x0, x2, x1
    arm64.cmp x0, x3
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_5ffb4c10096b25df
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
	return ((a + b + c + d + e + f + g) / h) and 125
end 'main'
```
```exitcode
12
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 52
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #52
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.movsd xmm0, [rip+__float_4614253070214989087]
    x64.cvttsd2si r8, xmm0
    x64.mov r9d, 4294967295
    x64.add r8, 40
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_04f881d71f72fcd2]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.ldr d0, [rdata+__float_4614253070214989087]
    arm64.mov x1, #40
    arm64.fcvtzs x2, d0
    arm64.mov x3, #255
    arm64.add x0, x2, x1
    arm64.cmp x0, x3
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_40f881d71f72fcd2
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 7
    x64.mov eax, 100
    x64.xor edx, edx
    x64.idiv r8
    x64.mov r8, rdx
    x64.mov r9d, 10
    x64.imul r8, r9
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_bf3600189d269600]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #7
    arm64.mov x1, #100
    arm64.msub x2, x1, x0
    arm64.mov x1, #10
    arm64.mul x0, x2, x1
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_bf3600189d269600
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 38
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #38
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 72
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #72
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 12
    x64.mov r9d, 20
    x64.mov esi, 10
  branch_0.merge:
    x64.add rsi, r9
    x64.mov r9d, 4294967295
    x64.add rsi, r8
    x64.cmp rsi, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_e2f81ed7640ab46a]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, rsi
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #12
    arm64.mov x1, #20
    arm64.mov x2, #10
  branch_0.merge:
    arm64.add x3, x2, x1
    arm64.mov x1, #255
    arm64.add x2, x3, x0
    arm64.cmp x2, x1
    arm64.cset x0, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_e2f81ed7640ab46a
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.mov x0, x2
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 2
  check_0.next0:
    x64.cmp r8, 2
    x64.jne check_0.case2
  check_0.case1:
    x64.mov r8d, 20
    x64.ret
  check_0.case2:
    x64.xor r8d, r8d
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #2
  check_0.next0:
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x0, eq
    arm64.b.ne check_0.case2
  check_0.case1:
    arm64.mov x0, #20
    arm64.ret
  check_0.case2:
    arm64.mov x0, #0
    arm64.ret
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
	return result and 125
end 'main'
```
```exitcode
72
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 2
    x64.jmp process_0.next0
  process_0.merge:
    x64.mov r9d, 125
    x64.mov esi, 4294967295
    x64.and r8, r9
    x64.cmp r8, rsi
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  process_0.next0:
    x64.cmp r8, 2
    x64.jne process_0.case2
  process_0.case1:
    x64.mov r8d, 200
    x64.jmp process_0.merge
  process_0.case2:
    x64.xor r8d, r8d
    x64.jmp process_0.merge
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_388127f7ac41c7d7]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #2
    arm64.b process_0.next0
  process_0.merge:
    arm64.mov x2, #125
    arm64.mov x3, #255
    arm64.and x0, x1, x2
    arm64.cmp x0, x3
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
    arm64.b __range_panic_0
  process_0.next0:
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x0, eq
    arm64.b.ne process_0.case2
  process_0.case1:
    arm64.mov x1, #200
    arm64.b process_0.merge
  process_0.case2:
    arm64.mov x1, #0
    arm64.b process_0.merge
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_388127f7ac41c7d7
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.mov r8d, 3
  check_0.next0:
    x64.cmp r8, 4
    x64.sete r9
    x64.cmp r8, 3
    x64.sete r8
    x64.or r8, r9
    x64.je check_0.case2
  check_0.case1:
    x64.mov r8d, 20
    x64.ret
  check_0.case2:
    x64.xor r8d, r8d
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #3
  check_0.next0:
    arm64.mov x1, #4
    arm64.mov x2, #3
    arm64.cmp x0, x1
    arm64.cset x1, eq
    arm64.cmp x0, x2
    arm64.cset x0, eq
    arm64.orr x2, x0, x1
    arm64.cmp x2, #0
    arm64.b.eq check_0.case2
  check_0.case1:
    arm64.mov x0, #20
    arm64.ret
  check_0.case2:
    arm64.mov x0, #0
    arm64.ret
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor r8d, r8d
    x64.jmp cascade_0.case0
  cascade_0.merge:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  cascade_0.case0:
    x64.add r8, 10
  cascade_0.case1:
    x64.add r8, 20
  cascade_0.case2:
    x64.add r8, 30
    x64.jmp cascade_0.merge
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_6ac6ec73b5bac89c]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #0
    arm64.b cascade_0.case0
  cascade_0.merge:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
    arm64.b __range_panic_0
  cascade_0.case0:
    arm64.mov x1, #10
    arm64.add x2, x0, x1
  cascade_0.case1:
    arm64.mov x0, #20
    arm64.add x1, x2, x0
  cascade_0.case2:
    arm64.mov x2, #30
    arm64.add x0, x1, x2
    arm64.b cascade_0.merge
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_6ac6ec73b5bac89c
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 2
    x64.jmp eval_0.next0
  eval_0.merge:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  eval_0.next0:
    x64.cmp r8, 2
    x64.jne eval_0.case2
  eval_0.case1:
    x64.mov r8d, 20
    x64.jmp eval_0.merge
  eval_0.case2:
    x64.xor r8d, r8d
    x64.jmp eval_0.merge
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_c4227e4f5df12c9a]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #2
    arm64.b eval_0.next0
  eval_0.merge:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
    arm64.b __range_panic_0
  eval_0.next0:
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x0, eq
    arm64.b.ne eval_0.case2
  eval_0.case1:
    arm64.mov x0, #20
    arm64.b eval_0.merge
  eval_0.case2:
    arm64.mov x0, #0
    arm64.b eval_0.merge
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_c4227e4f5df12c9a
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 4
    x64.jmp eval_0.next0
  eval_0.merge:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  eval_0.next0:
    x64.cmp r8, 4
    x64.sete r9
    x64.cmp r8, 3
    x64.sete r8
    x64.or r8, r9
    x64.je eval_0.case2
  eval_0.case1:
    x64.mov r8d, 20
    x64.jmp eval_0.merge
  eval_0.case2:
    x64.xor r8d, r8d
    x64.jmp eval_0.merge
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_628d51227dda8cbd]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #4
    arm64.b eval_0.next0
  eval_0.merge:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
    arm64.b __range_panic_0
  eval_0.next0:
    arm64.mov x1, #4
    arm64.mov x2, #3
    arm64.cmp x0, x1
    arm64.cset x1, eq
    arm64.cmp x0, x2
    arm64.cset x0, eq
    arm64.orr x2, x0, x1
    arm64.cmp x2, #0
    arm64.b.eq eval_0.case2
  eval_0.case1:
    arm64.mov x0, #20
    arm64.b eval_0.merge
  eval_0.case2:
    arm64.mov x0, #0
    arm64.b eval_0.merge
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_628d51227dda8cbd
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 2
    x64.jmp eval_0.next0
  eval_0.merge:
    x64.mov r9d, 2
    x64.imul r8, r9
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  eval_0.next0:
    x64.cmp r8, 2
    x64.jne eval_0.case2
  eval_0.case1:
    x64.mov r8d, 20
    x64.jmp eval_0.merge
  eval_0.case2:
    x64.xor r8d, r8d
    x64.jmp eval_0.merge
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_27e6ae53d7b1aeee]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #2
    arm64.b eval_0.next0
  eval_0.merge:
    arm64.mov x2, #2
    arm64.mul x0, x1, x2
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
    arm64.b __range_panic_0
  eval_0.next0:
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x0, eq
    arm64.b.ne eval_0.case2
  eval_0.case1:
    arm64.mov x1, #20
    arm64.b eval_0.merge
  eval_0.case2:
    arm64.mov x1, #0
    arm64.b eval_0.merge
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_27e6ae53d7b1aeee
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 2
    x64.jmp process_0.next0
  process_0.merge:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  process_0.next0:
    x64.cmp r8, 2
    x64.jne process_0.case2
  process_0.case1:
    x64.mov r8d, 40
    x64.jmp process_0.merge
  process_0.case2:
    x64.xor r8d, r8d
    x64.jmp process_0.merge
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_4ec22f4a8f937639]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #2
    arm64.b process_0.next0
  process_0.merge:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
    arm64.b __range_panic_0
  process_0.next0:
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x0, eq
    arm64.b.ne process_0.case2
  process_0.case1:
    arm64.mov x0, #40
    arm64.b process_0.merge
  process_0.case2:
    arm64.mov x0, #0
    arm64.b process_0.merge
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_4ec22f4a8f937639
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.xor r8d, r8d
  try_0.merge:
    x64.mov r8d, 42
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.mov x0, #0
  try_0.merge:
    arm64.mov x0, #42
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor r9d, r9d
    x64.mov r8d, 42
  try_0.merge:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_52fc415dc3ac3fb2]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x1, #0
    arm64.mov x0, #42
  try_0.merge:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_52fc415dc3ac3fb2
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov edx, 1
    x64.xor r8d, r8d
  inlined_middle_1_0:
    x64.xor r8d, r8d
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.mov r8d, 99
  try_0.merge:
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_765ddc695c2d01c9]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x1, #1
    arm64.mov x0, #0
  inlined_middle_1_0:
    arm64.mov x0, #0
  inline_cont_main_0:
    arm64.mov x2, #0
    arm64.cmp x1, x2
    arm64.cset x1, ne
    arm64.b.eq try_0.merge
  try_0.otherwise:
    arm64.mov x0, #99
  try_0.merge:
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_765ddc695c2d01c9
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor r8d, r8d
    x64.mov r9d, 10
  try_0.merge:
    x64.xor esi, esi
    x64.mov edi, 20
    x64.test rsi, rsi
    x64.je try_1.ok
    x64.jmp try_1.merge
  try_1.ok:
    x64.mov r8, rdi
  try_1.merge:
    x64.mov esi, 1
    x64.xor edi, edi
    x64.test rsi, rsi
    x64.je try_2.merge
  try_2.otherwise:
    x64.mov esi, 12
    x64.mov rdi, rsi
  try_2.merge:
    x64.add r9, r8
    x64.mov esi, 4294967295
    x64.mov r8, r9
    x64.add r8, rdi
    x64.cmp r8, rsi
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_4088112a38757a69]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #0
    arm64.mov x1, #10
  try_0.merge:
    arm64.mov x2, #0
    arm64.mov x3, #20
    arm64.cmp x2, x0
    arm64.cset x2, ne
    arm64.b.eq try_1.ok
    arm64.mov x2, x0
    arm64.b try_1.merge
  try_1.ok:
    arm64.mov x2, x3
  try_1.merge:
    arm64.mov x3, #1
    arm64.mov x4, #0
    arm64.cmp x3, x0
    arm64.cset x0, ne
    arm64.b.eq try_2.merge
  try_2.otherwise:
    arm64.mov x0, #12
    arm64.mov x4, x0
  try_2.merge:
    arm64.add x3, x1, x2
    arm64.mov x1, #255
    arm64.add x0, x3, x4
    arm64.cmp x0, x1
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_4088112a38757a69
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
	return (a + b) and 125
end 'main'
```
```exitcode
112
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov r8d, 2
  inlined_lookup_0_0:
    x64.cmp r8, 1
    x64.jne inlined_lookup_3_0
  inlined_lookup_2_0:
    x64.xor edx, edx
    x64.mov r8d, 100
    x64.jmp inline_cont_main_0
  inlined_lookup_3_0:
    x64.cmp r8, 2
    x64.jne inlined_lookup_6_0
  inlined_lookup_4_0:
    x64.xor edx, edx
    x64.mov r8d, 200
    x64.jmp inline_cont_main_0
  inlined_lookup_6_0:
    x64.mov edx, 2
    x64.xor r8d, r8d
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.xor r8d, r8d
  try_0.merge:
    x64.mov r9d, 99
  inlined_lookup_0_1:
    x64.cmp r9, 1
    x64.jne inlined_lookup_3_1
  inlined_lookup_2_1:
    x64.xor edx, edx
    x64.mov r9d, 100
    x64.jmp inline_cont_main_1
  inlined_lookup_3_1:
    x64.cmp r9, 2
    x64.jne inlined_lookup_6_1
  inlined_lookup_4_1:
    x64.xor edx, edx
    x64.mov r9d, 200
    x64.jmp inline_cont_main_1
  inlined_lookup_6_1:
    x64.mov edx, 2
    x64.xor r9d, r9d
  inline_cont_main_1:
    x64.test rdx, rdx
    x64.je try_1.merge
  try_1.otherwise:
    x64.mov r9d, 42
  try_1.merge:
    x64.mov esi, 125
    x64.add r8, r9
    x64.mov r9d, 4294967295
    x64.and r8, rsi
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_f84624c80ce4f2a5]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
```RequiredIR:arm64-macos
module {
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #2
  inlined_lookup_0_0:
    arm64.mov x1, #1
    arm64.cmp x0, x1
    arm64.cset x1, eq
    arm64.b.ne inlined_lookup_3_0
  inlined_lookup_2_0:
    arm64.mov x1, #0
    arm64.mov x0, #100
    arm64.b inline_cont_main_0
  inlined_lookup_3_0:
    arm64.mov x1, #2
    arm64.cmp x0, x1
    arm64.cset x0, eq
    arm64.b.ne inlined_lookup_6_0
  inlined_lookup_4_0:
    arm64.mov x1, #0
    arm64.mov x0, #200
    arm64.b inline_cont_main_0
  inlined_lookup_6_0:
    arm64.mov x1, #2
    arm64.mov x0, #0
  inline_cont_main_0:
    arm64.mov x2, #0
    arm64.cmp x1, x2
    arm64.cset x1, ne
    arm64.b.eq try_0.merge
  try_0.otherwise:
    arm64.mov x0, #0
  try_0.merge:
    arm64.mov x1, #99
  inlined_lookup_0_1:
    arm64.mov x2, #1
    arm64.cmp x1, x2
    arm64.cset x2, eq
    arm64.b.ne inlined_lookup_3_1
  inlined_lookup_2_1:
    arm64.mov x1, #0
    arm64.mov x2, #100
    arm64.b inline_cont_main_1
  inlined_lookup_3_1:
    arm64.mov x2, #2
    arm64.cmp x1, x2
    arm64.cset x1, eq
    arm64.b.ne inlined_lookup_6_1
  inlined_lookup_4_1:
    arm64.mov x1, #0
    arm64.mov x2, #200
    arm64.b inline_cont_main_1
  inlined_lookup_6_1:
    arm64.mov x1, #2
    arm64.mov x2, #0
  inline_cont_main_1:
    arm64.mov x3, #0
    arm64.cmp x1, x3
    arm64.cset x1, ne
    arm64.b.eq try_1.merge
  try_1.otherwise:
    arm64.mov x1, #42
    arm64.mov x2, x1
  try_1.merge:
    arm64.mov x1, #125
    arm64.add x3, x0, x2
    arm64.mov x2, #255
    arm64.and x0, x3, x1
    arm64.cmp x0, x2
    arm64.cset x1, hi
    arm64.b.ls __range_ok_0
  __range_panic_0:
    arm64.adrp_add_rdata x0, __panic_msg_f84624c80ce4f2a5
    arm64.ldr x19, [x29, #16]
    arm64.mov x0, x19
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue
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
	return (a + b + c + d) and 125
end 'main'
```
```exitcode
88
```

