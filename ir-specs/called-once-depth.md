---
feature: called-once-depth
status: experimental
keywords: [optimizer, inliner, called-once, depth, codegen]
category: codegen
---

# Called-Once Inlining Depth: the cases that pin emitted code

A function with exactly one call site is spliced into its caller, and a chain of such functions folds leaves-first into one body. The splice is bounded by inline-site depth: a callee whose splice would give its caller more than 32 nested inline sites stays a real call. Each case pins the Target IR its program compiles to, on every native lane.

## Tests

<!-- test: a-called-once-chain-at-the-depth-bound-is-spliced-whole -->
⭐ **THE CONTROL: 32 SINGLE-CALL FUNCTIONS FOLD WHOLE.** `g00` through `g31` each have one call site, and the chain reaches exactly the depth bound. The pin shows `main` with every `depth == 0` compare-and-branch of the chain inline and no call to any `g..` function, and no `g..` function rendered out of line.

⚠ **A COMPILER WITHOUT THE BOUND RENDERS THE SAME TEXT HERE**, which is what makes this the control: it shows the bound refuses nothing at its own limit, so case 2's standing call is the bound and not a chain that never folds.
```maxon
typealias Tally = int(0 to u64.max)

function g00(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g01(y, y: x, depth: depth - 1)
end 'g00'

function g01(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g02(y, y: x, depth: depth - 1)
end 'g01'

function g02(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g03(y, y: x, depth: depth - 1)
end 'g02'

function g03(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g04(y, y: x, depth: depth - 1)
end 'g03'

function g04(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g05(y, y: x, depth: depth - 1)
end 'g04'

function g05(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g06(y, y: x, depth: depth - 1)
end 'g05'

function g06(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g07(y, y: x, depth: depth - 1)
end 'g06'

function g07(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g08(y, y: x, depth: depth - 1)
end 'g07'

function g08(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g09(y, y: x, depth: depth - 1)
end 'g08'

function g09(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g10(y, y: x, depth: depth - 1)
end 'g09'

function g10(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g11(y, y: x, depth: depth - 1)
end 'g10'

function g11(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g12(y, y: x, depth: depth - 1)
end 'g11'

function g12(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g13(y, y: x, depth: depth - 1)
end 'g12'

function g13(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g14(y, y: x, depth: depth - 1)
end 'g13'

function g14(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g15(y, y: x, depth: depth - 1)
end 'g14'

function g15(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g16(y, y: x, depth: depth - 1)
end 'g15'

function g16(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g17(y, y: x, depth: depth - 1)
end 'g16'

function g17(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g18(y, y: x, depth: depth - 1)
end 'g17'

function g18(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g19(y, y: x, depth: depth - 1)
end 'g18'

function g19(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g20(y, y: x, depth: depth - 1)
end 'g19'

function g20(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g21(y, y: x, depth: depth - 1)
end 'g20'

function g21(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g22(y, y: x, depth: depth - 1)
end 'g21'

function g22(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g23(y, y: x, depth: depth - 1)
end 'g22'

function g23(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g24(y, y: x, depth: depth - 1)
end 'g23'

function g24(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g25(y, y: x, depth: depth - 1)
end 'g24'

function g25(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g26(y, y: x, depth: depth - 1)
end 'g25'

function g26(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g27(y, y: x, depth: depth - 1)
end 'g26'

function g27(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g28(y, y: x, depth: depth - 1)
end 'g27'

function g28(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g29(y, y: x, depth: depth - 1)
end 'g28'

function g29(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g30(y, y: x, depth: depth - 1)
end 'g29'

function g30(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g31(y, y: x, depth: depth - 1)
end 'g30'

function g31(x Tally, y Tally, depth Tally) returns Tally
	return x + y + depth
end 'g31'

function main() returns ExitCode
	var depth = 0 as Tally
	var i = 0 as Tally
	while i < 3 'count'
		depth = depth + 1
		i = i + 1
	end 'count'

	print("{g00(1, y: 2, depth: depth)}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.jmp whilehdr
  count:
    x64.leaRegRegImm32 rdx, rax, 1
    x64.cmpRegReg rdx, rax
    x64.jcc below, __rc_panic#5
  __rc_ok#4:
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rax, rdx
  whilehdr:
    x64.cmpRegImm32 rcx, 3
    x64.jcc below, count
  whileexit:
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 2
  __il_body#9:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#10
  ifcont#11:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#13:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#14
  ifcont#15:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#17:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#18
  ifcont#19:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#21:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#22
  ifcont#23:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#25:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#26
  ifcont#27:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#29:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#30
  ifcont#31:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#33:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#34
  ifcont#35:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#37:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#38
  ifcont#39:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#41:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#42
  ifcont#43:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#45:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#46
  ifcont#47:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#49:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#50
  ifcont#51:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#53:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#54
  ifcont#55:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#57:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#58
  ifcont#59:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#61:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#62
  ifcont#63:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#65:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#66
  ifcont#67:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#69:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#70
  ifcont#71:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#73:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#74
  ifcont#75:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#77:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#78
  ifcont#79:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#81:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#82
  ifcont#83:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#85:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#86
  ifcont#87:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#89:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#90
  ifcont#91:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#93:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#94
  ifcont#95:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#97:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#98
  ifcont#99:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#101:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#102
  ifcont#103:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#105:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#106
  ifcont#107:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#109:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#110
  ifcont#111:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#113:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#114
  ifcont#115:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#117:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#118
  ifcont#119:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#121:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#122
  ifcont#123:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#125:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#126
  ifcont#127:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#129:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#130
  ifcont#131:
    x64.leaRegRegImm32 rax, rax, -1
  __rc_ok#134:
    x64.leaRegRegImm32 rax, rax, 3
    x64.cmpRegImm32 rax, 3
    x64.jcc below, __rc_panic#137
    x64.jmp __il_cont#12
  done#130:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#126:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#122:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#118:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#114:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#110:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#106:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#102:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#98:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#94:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#90:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#86:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#82:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#78:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#74:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#70:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#66:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#62:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#58:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#54:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#50:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#46:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#42:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#38:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#34:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#30:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#26:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#22:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#18:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#14:
    x64.movRegReg rax, rdx
  __il_cont#12:
    x64.movRegReg rbx, rax
    x64.jmp __il_cont#8
  done#10:
    x64.movRegReg rbx, rcx
  __il_cont#8:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
  __il_body#7:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont#6:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#5:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-called-once-chain-at-the-depth-bound-is-spliced-whole.test:260: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#137:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-called-once-chain-at-the-depth-bound-is-spliced-whole.test:253: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-windows
data {
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_console_probe_stdin@48 = i8 0
  __mrt_console_probe_stdout@49 = i8 0
  __mrt_console_probe_stderr@50 = i8 0
  __mrt_program_started@51 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.jmp whilehdr
  count:
    x64.leaRegRegImm32 rdx, rax, 1
    x64.cmpRegReg rdx, rax
    x64.jcc below, __rc_panic#5
  __rc_ok#4:
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rax, rdx
  whilehdr:
    x64.cmpRegImm32 rcx, 3
    x64.jcc below, count
  whileexit:
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 2
  __il_body#9:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#10
  ifcont#11:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#13:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#14
  ifcont#15:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#17:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#18
  ifcont#19:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#21:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#22
  ifcont#23:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#25:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#26
  ifcont#27:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#29:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#30
  ifcont#31:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#33:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#34
  ifcont#35:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#37:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#38
  ifcont#39:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#41:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#42
  ifcont#43:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#45:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#46
  ifcont#47:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#49:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#50
  ifcont#51:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#53:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#54
  ifcont#55:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#57:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#58
  ifcont#59:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#61:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#62
  ifcont#63:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#65:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#66
  ifcont#67:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#69:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#70
  ifcont#71:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#73:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#74
  ifcont#75:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#77:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#78
  ifcont#79:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#81:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#82
  ifcont#83:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#85:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#86
  ifcont#87:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#89:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#90
  ifcont#91:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#93:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#94
  ifcont#95:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#97:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#98
  ifcont#99:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#101:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#102
  ifcont#103:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#105:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#106
  ifcont#107:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#109:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#110
  ifcont#111:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#113:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#114
  ifcont#115:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#117:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#118
  ifcont#119:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#121:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#122
  ifcont#123:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#125:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#126
  ifcont#127:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#129:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#130
  ifcont#131:
    x64.leaRegRegImm32 rax, rax, -1
  __rc_ok#134:
    x64.leaRegRegImm32 rax, rax, 3
    x64.cmpRegImm32 rax, 3
    x64.jcc below, __rc_panic#137
    x64.jmp __il_cont#12
  done#130:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#126:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#122:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#118:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#114:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#110:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#106:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#102:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#98:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#94:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#90:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#86:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#82:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#78:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#74:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#70:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#66:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#62:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#58:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#54:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#50:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#46:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#42:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#38:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#34:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#30:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#26:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#22:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#12
  done#18:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#12
  done#14:
    x64.movRegReg rax, rdx
  __il_cont#12:
    x64.movRegReg rbx, rax
    x64.jmp __il_cont#8
  done#10:
    x64.movRegReg rbx, rcx
  __il_cont#8:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
  __il_body#7:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont#6:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#5:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-called-once-chain-at-the-depth-bound-is-spliced-whole.test:260: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#137:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-called-once-chain-at-the-depth-bound-is-spliced-whole.test:253: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.b whilehdr
  count:
    arm64.add x2, x0, 1
    arm64.cmp x2, x0
    arm64.b.lo __rc_panic#5
  __rc_ok#4:
    arm64.add x1, x1, 1
    arm64.movRegReg x0, x2
  whilehdr:
    arm64.cmp x1, 3
    arm64.b.lo count
  whileexit:
    arm64.movImm x1, 1
    arm64.movImm x2, 2
  __il_body#9:
    arm64.cmp x0, 0
    arm64.b.eq done#10
  ifcont#11:
    arm64.sub x0, x0, 1
  __il_body#13:
    arm64.cmp x0, 0
    arm64.b.eq done#14
  ifcont#15:
    arm64.sub x0, x0, 1
  __il_body#17:
    arm64.cmp x0, 0
    arm64.b.eq done#18
  ifcont#19:
    arm64.sub x0, x0, 1
  __il_body#21:
    arm64.cmp x0, 0
    arm64.b.eq done#22
  ifcont#23:
    arm64.sub x0, x0, 1
  __il_body#25:
    arm64.cmp x0, 0
    arm64.b.eq done#26
  ifcont#27:
    arm64.sub x0, x0, 1
  __il_body#29:
    arm64.cmp x0, 0
    arm64.b.eq done#30
  ifcont#31:
    arm64.sub x0, x0, 1
  __il_body#33:
    arm64.cmp x0, 0
    arm64.b.eq done#34
  ifcont#35:
    arm64.sub x0, x0, 1
  __il_body#37:
    arm64.cmp x0, 0
    arm64.b.eq done#38
  ifcont#39:
    arm64.sub x0, x0, 1
  __il_body#41:
    arm64.cmp x0, 0
    arm64.b.eq done#42
  ifcont#43:
    arm64.sub x0, x0, 1
  __il_body#45:
    arm64.cmp x0, 0
    arm64.b.eq done#46
  ifcont#47:
    arm64.sub x0, x0, 1
  __il_body#49:
    arm64.cmp x0, 0
    arm64.b.eq done#50
  ifcont#51:
    arm64.sub x0, x0, 1
  __il_body#53:
    arm64.cmp x0, 0
    arm64.b.eq done#54
  ifcont#55:
    arm64.sub x0, x0, 1
  __il_body#57:
    arm64.cmp x0, 0
    arm64.b.eq done#58
  ifcont#59:
    arm64.sub x0, x0, 1
  __il_body#61:
    arm64.cmp x0, 0
    arm64.b.eq done#62
  ifcont#63:
    arm64.sub x0, x0, 1
  __il_body#65:
    arm64.cmp x0, 0
    arm64.b.eq done#66
  ifcont#67:
    arm64.sub x0, x0, 1
  __il_body#69:
    arm64.cmp x0, 0
    arm64.b.eq done#70
  ifcont#71:
    arm64.sub x0, x0, 1
  __il_body#73:
    arm64.cmp x0, 0
    arm64.b.eq done#74
  ifcont#75:
    arm64.sub x0, x0, 1
  __il_body#77:
    arm64.cmp x0, 0
    arm64.b.eq done#78
  ifcont#79:
    arm64.sub x0, x0, 1
  __il_body#81:
    arm64.cmp x0, 0
    arm64.b.eq done#82
  ifcont#83:
    arm64.sub x0, x0, 1
  __il_body#85:
    arm64.cmp x0, 0
    arm64.b.eq done#86
  ifcont#87:
    arm64.sub x0, x0, 1
  __il_body#89:
    arm64.cmp x0, 0
    arm64.b.eq done#90
  ifcont#91:
    arm64.sub x0, x0, 1
  __il_body#93:
    arm64.cmp x0, 0
    arm64.b.eq done#94
  ifcont#95:
    arm64.sub x0, x0, 1
  __il_body#97:
    arm64.cmp x0, 0
    arm64.b.eq done#98
  ifcont#99:
    arm64.sub x0, x0, 1
  __il_body#101:
    arm64.cmp x0, 0
    arm64.b.eq done#102
  ifcont#103:
    arm64.sub x0, x0, 1
  __il_body#105:
    arm64.cmp x0, 0
    arm64.b.eq done#106
  ifcont#107:
    arm64.sub x0, x0, 1
  __il_body#109:
    arm64.cmp x0, 0
    arm64.b.eq done#110
  ifcont#111:
    arm64.sub x0, x0, 1
  __il_body#113:
    arm64.cmp x0, 0
    arm64.b.eq done#114
  ifcont#115:
    arm64.sub x0, x0, 1
  __il_body#117:
    arm64.cmp x0, 0
    arm64.b.eq done#118
  ifcont#119:
    arm64.sub x0, x0, 1
  __il_body#121:
    arm64.cmp x0, 0
    arm64.b.eq done#122
  ifcont#123:
    arm64.sub x0, x0, 1
  __il_body#125:
    arm64.cmp x0, 0
    arm64.b.eq done#126
  ifcont#127:
    arm64.sub x0, x0, 1
  __il_body#129:
    arm64.cmp x0, 0
    arm64.b.eq done#130
  ifcont#131:
    arm64.sub x0, x0, 1
  __rc_ok#134:
    arm64.add x0, x0, 3
    arm64.cmp x0, 3
    arm64.b.lo __rc_panic#137
    arm64.b __il_cont#12
  done#130:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#126:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#122:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#118:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#114:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#110:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#106:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#102:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#98:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#94:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#90:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#86:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#82:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#78:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#74:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#70:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#66:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#62:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#58:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#54:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#50:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#46:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#42:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#38:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#34:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#30:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#26:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#22:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#18:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#14:
    arm64.movRegReg x0, x2
  __il_cont#12:
    arm64.movRegReg x19, x0
    arm64.b __il_cont#8
  done#10:
    arm64.movRegReg x19, x1
  __il_cont#8:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x19, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x25, x24, 56
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x25, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x25
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body#7:
    arm64.movRegReg x0, x24
    arm64.bl __write_stdout
  __il_cont#6:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic#5:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-called-once-chain-at-the-depth-bound-is-spliced-whole.test:260: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic#137:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-called-once-chain-at-the-depth-bound-is-spliced-whole.test:253: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.b whilehdr
  count:
    arm64.add x2, x0, 1
    arm64.cmp x2, x0
    arm64.b.lo __rc_panic#5
  __rc_ok#4:
    arm64.add x1, x1, 1
    arm64.movRegReg x0, x2
  whilehdr:
    arm64.cmp x1, 3
    arm64.b.lo count
  whileexit:
    arm64.movImm x1, 1
    arm64.movImm x2, 2
  __il_body#9:
    arm64.cmp x0, 0
    arm64.b.eq done#10
  ifcont#11:
    arm64.sub x0, x0, 1
  __il_body#13:
    arm64.cmp x0, 0
    arm64.b.eq done#14
  ifcont#15:
    arm64.sub x0, x0, 1
  __il_body#17:
    arm64.cmp x0, 0
    arm64.b.eq done#18
  ifcont#19:
    arm64.sub x0, x0, 1
  __il_body#21:
    arm64.cmp x0, 0
    arm64.b.eq done#22
  ifcont#23:
    arm64.sub x0, x0, 1
  __il_body#25:
    arm64.cmp x0, 0
    arm64.b.eq done#26
  ifcont#27:
    arm64.sub x0, x0, 1
  __il_body#29:
    arm64.cmp x0, 0
    arm64.b.eq done#30
  ifcont#31:
    arm64.sub x0, x0, 1
  __il_body#33:
    arm64.cmp x0, 0
    arm64.b.eq done#34
  ifcont#35:
    arm64.sub x0, x0, 1
  __il_body#37:
    arm64.cmp x0, 0
    arm64.b.eq done#38
  ifcont#39:
    arm64.sub x0, x0, 1
  __il_body#41:
    arm64.cmp x0, 0
    arm64.b.eq done#42
  ifcont#43:
    arm64.sub x0, x0, 1
  __il_body#45:
    arm64.cmp x0, 0
    arm64.b.eq done#46
  ifcont#47:
    arm64.sub x0, x0, 1
  __il_body#49:
    arm64.cmp x0, 0
    arm64.b.eq done#50
  ifcont#51:
    arm64.sub x0, x0, 1
  __il_body#53:
    arm64.cmp x0, 0
    arm64.b.eq done#54
  ifcont#55:
    arm64.sub x0, x0, 1
  __il_body#57:
    arm64.cmp x0, 0
    arm64.b.eq done#58
  ifcont#59:
    arm64.sub x0, x0, 1
  __il_body#61:
    arm64.cmp x0, 0
    arm64.b.eq done#62
  ifcont#63:
    arm64.sub x0, x0, 1
  __il_body#65:
    arm64.cmp x0, 0
    arm64.b.eq done#66
  ifcont#67:
    arm64.sub x0, x0, 1
  __il_body#69:
    arm64.cmp x0, 0
    arm64.b.eq done#70
  ifcont#71:
    arm64.sub x0, x0, 1
  __il_body#73:
    arm64.cmp x0, 0
    arm64.b.eq done#74
  ifcont#75:
    arm64.sub x0, x0, 1
  __il_body#77:
    arm64.cmp x0, 0
    arm64.b.eq done#78
  ifcont#79:
    arm64.sub x0, x0, 1
  __il_body#81:
    arm64.cmp x0, 0
    arm64.b.eq done#82
  ifcont#83:
    arm64.sub x0, x0, 1
  __il_body#85:
    arm64.cmp x0, 0
    arm64.b.eq done#86
  ifcont#87:
    arm64.sub x0, x0, 1
  __il_body#89:
    arm64.cmp x0, 0
    arm64.b.eq done#90
  ifcont#91:
    arm64.sub x0, x0, 1
  __il_body#93:
    arm64.cmp x0, 0
    arm64.b.eq done#94
  ifcont#95:
    arm64.sub x0, x0, 1
  __il_body#97:
    arm64.cmp x0, 0
    arm64.b.eq done#98
  ifcont#99:
    arm64.sub x0, x0, 1
  __il_body#101:
    arm64.cmp x0, 0
    arm64.b.eq done#102
  ifcont#103:
    arm64.sub x0, x0, 1
  __il_body#105:
    arm64.cmp x0, 0
    arm64.b.eq done#106
  ifcont#107:
    arm64.sub x0, x0, 1
  __il_body#109:
    arm64.cmp x0, 0
    arm64.b.eq done#110
  ifcont#111:
    arm64.sub x0, x0, 1
  __il_body#113:
    arm64.cmp x0, 0
    arm64.b.eq done#114
  ifcont#115:
    arm64.sub x0, x0, 1
  __il_body#117:
    arm64.cmp x0, 0
    arm64.b.eq done#118
  ifcont#119:
    arm64.sub x0, x0, 1
  __il_body#121:
    arm64.cmp x0, 0
    arm64.b.eq done#122
  ifcont#123:
    arm64.sub x0, x0, 1
  __il_body#125:
    arm64.cmp x0, 0
    arm64.b.eq done#126
  ifcont#127:
    arm64.sub x0, x0, 1
  __il_body#129:
    arm64.cmp x0, 0
    arm64.b.eq done#130
  ifcont#131:
    arm64.sub x0, x0, 1
  __rc_ok#134:
    arm64.add x0, x0, 3
    arm64.cmp x0, 3
    arm64.b.lo __rc_panic#137
    arm64.b __il_cont#12
  done#130:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#126:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#122:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#118:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#114:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#110:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#106:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#102:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#98:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#94:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#90:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#86:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#82:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#78:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#74:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#70:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#66:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#62:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#58:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#54:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#50:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#46:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#42:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#38:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#34:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#30:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#26:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#22:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#12
  done#18:
    arm64.movRegReg x0, x1
    arm64.b __il_cont#12
  done#14:
    arm64.movRegReg x0, x2
  __il_cont#12:
    arm64.movRegReg x19, x0
    arm64.b __il_cont#8
  done#10:
    arm64.movRegReg x19, x1
  __il_cont#8:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x19, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x25, x24, 56
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x25, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x25
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body#7:
    arm64.movRegReg x0, x24
    arm64.bl __write_stdout
  __il_cont#6:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic#5:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-called-once-chain-at-the-depth-bound-is-spliced-whole.test:260: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic#137:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-called-once-chain-at-the-depth-bound-is-spliced-whole.test:253: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
}
```

<!-- test: a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call -->
⭐⭐ **ONE LEVEL PAST THE BOUND LEAVES ONE CALL.** `g00` through `g32` is 33 single-call functions. `g01` through `g32` fold into `g00`, which reaches the depth bound, so `main` keeps exactly one direct call, to `g00`, and `g00` is rendered out of line with the `depth == 0` compare-and-branch of every link inline and no call to any `g..`.

⚠ **A COMPILER WITHOUT THE BOUND RENDERS NO CALL AT ALL HERE**: `g00` is spliced into `main` 33 levels deep and no `g..` function is rendered.
```maxon
typealias Tally = int(0 to u64.max)

function g00(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g01(y, y: x, depth: depth - 1)
end 'g00'

function g01(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g02(y, y: x, depth: depth - 1)
end 'g01'

function g02(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g03(y, y: x, depth: depth - 1)
end 'g02'

function g03(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g04(y, y: x, depth: depth - 1)
end 'g03'

function g04(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g05(y, y: x, depth: depth - 1)
end 'g04'

function g05(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g06(y, y: x, depth: depth - 1)
end 'g05'

function g06(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g07(y, y: x, depth: depth - 1)
end 'g06'

function g07(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g08(y, y: x, depth: depth - 1)
end 'g07'

function g08(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g09(y, y: x, depth: depth - 1)
end 'g08'

function g09(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g10(y, y: x, depth: depth - 1)
end 'g09'

function g10(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g11(y, y: x, depth: depth - 1)
end 'g10'

function g11(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g12(y, y: x, depth: depth - 1)
end 'g11'

function g12(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g13(y, y: x, depth: depth - 1)
end 'g12'

function g13(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g14(y, y: x, depth: depth - 1)
end 'g13'

function g14(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g15(y, y: x, depth: depth - 1)
end 'g14'

function g15(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g16(y, y: x, depth: depth - 1)
end 'g15'

function g16(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g17(y, y: x, depth: depth - 1)
end 'g16'

function g17(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g18(y, y: x, depth: depth - 1)
end 'g17'

function g18(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g19(y, y: x, depth: depth - 1)
end 'g18'

function g19(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g20(y, y: x, depth: depth - 1)
end 'g19'

function g20(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g21(y, y: x, depth: depth - 1)
end 'g20'

function g21(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g22(y, y: x, depth: depth - 1)
end 'g21'

function g22(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g23(y, y: x, depth: depth - 1)
end 'g22'

function g23(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g24(y, y: x, depth: depth - 1)
end 'g23'

function g24(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g25(y, y: x, depth: depth - 1)
end 'g24'

function g25(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g26(y, y: x, depth: depth - 1)
end 'g25'

function g26(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g27(y, y: x, depth: depth - 1)
end 'g26'

function g27(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g28(y, y: x, depth: depth - 1)
end 'g27'

function g28(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g29(y, y: x, depth: depth - 1)
end 'g28'

function g29(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g30(y, y: x, depth: depth - 1)
end 'g29'

function g30(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g31(y, y: x, depth: depth - 1)
end 'g30'

function g31(x Tally, y Tally, depth Tally) returns Tally
	if depth == 0 'done'
		return x
	end 'done'

	return g32(y, y: x, depth: depth - 1)
end 'g31'

function g32(x Tally, y Tally, depth Tally) returns Tally
	return x + y + depth
end 'g32'

function main() returns ExitCode
	var depth = 0 as Tally
	var i = 0 as Tally
	while i < 3 'count'
		depth = depth + 1
		i = i + 1
	end 'count'

	print("{g00(1, y: 2, depth: depth)}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @g00 {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont#2
  done#1:
    x64.movRegReg r8, rcx
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont#2:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#4:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#5
  ifcont#6:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#8:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#9
  ifcont#10:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#12:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#13
  ifcont#14:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#16:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#17
  ifcont#18:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#20:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#21
  ifcont#22:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#24:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#25
  ifcont#26:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#28:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#29
  ifcont#30:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#32:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#33
  ifcont#34:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#36:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#37
  ifcont#38:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#40:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#41
  ifcont#42:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#44:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#45
  ifcont#46:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#48:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#49
  ifcont#50:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#52:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#53
  ifcont#54:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#56:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#57
  ifcont#58:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#60:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#61
  ifcont#62:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#64:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#65
  ifcont#66:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#68:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#69
  ifcont#70:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#72:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#73
  ifcont#74:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#76:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#77
  ifcont#78:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#80:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#81
  ifcont#82:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#84:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#85
  ifcont#86:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#88:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#89
  ifcont#90:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#92:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#93
  ifcont#94:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#96:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#97
  ifcont#98:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#100:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#101
  ifcont#102:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#104:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#105
  ifcont#106:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#108:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#109
  ifcont#110:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#112:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#113
  ifcont#114:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#116:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#117
  ifcont#118:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#120:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#121
  ifcont#122:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#124:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#125
  ifcont#126:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#128:
    x64.leaRegRegReg rdx, rcx, rdx
    x64.cmpRegReg rdx, rcx
    x64.jcc below, __rc_panic#130
  __rc_ok:
    x64.leaRegRegReg rax, rdx, rax
    x64.cmpRegReg rax, rdx
    x64.jcc below, __rc_panic#132
    x64.jmp __il_cont#7
  done#125:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#121:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#117:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#113:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#109:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#105:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#101:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#97:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#93:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#89:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#85:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#81:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#77:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#73:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#69:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#65:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#61:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#57:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#53:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#49:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#45:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#41:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#37:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#33:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#29:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#25:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#21:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#17:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#13:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#9:
    x64.movRegReg rax, rcx
  __il_cont#7:
    x64.movRegReg r8, rax
    x64.jmp __il_cont#3
  done#5:
    x64.movRegReg r8, rdx
  __il_cont#3:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#130:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:261: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#132:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:261: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.jmp whilehdr
  count:
    x64.leaRegRegImm32 rdx, rax, 1
    x64.cmpRegReg rdx, rax
    x64.jcc below, __rc_panic
  __rc_ok:
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rax, rdx
  whilehdr:
    x64.cmpRegImm32 rcx, 3
    x64.jcc below, count
  whileexit:
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 2
    x64.callDirect g00
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:268: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-windows
data {
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_console_probe_stdin@48 = i8 0
  __mrt_console_probe_stdout@49 = i8 0
  __mrt_console_probe_stderr@50 = i8 0
  __mrt_program_started@51 = i8 0
}

func @g00 {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont#2
  done#1:
    x64.movRegReg r8, rcx
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont#2:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#4:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#5
  ifcont#6:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#8:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#9
  ifcont#10:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#12:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#13
  ifcont#14:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#16:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#17
  ifcont#18:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#20:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#21
  ifcont#22:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#24:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#25
  ifcont#26:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#28:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#29
  ifcont#30:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#32:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#33
  ifcont#34:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#36:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#37
  ifcont#38:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#40:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#41
  ifcont#42:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#44:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#45
  ifcont#46:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#48:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#49
  ifcont#50:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#52:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#53
  ifcont#54:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#56:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#57
  ifcont#58:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#60:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#61
  ifcont#62:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#64:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#65
  ifcont#66:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#68:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#69
  ifcont#70:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#72:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#73
  ifcont#74:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#76:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#77
  ifcont#78:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#80:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#81
  ifcont#82:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#84:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#85
  ifcont#86:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#88:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#89
  ifcont#90:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#92:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#93
  ifcont#94:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#96:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#97
  ifcont#98:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#100:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#101
  ifcont#102:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#104:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#105
  ifcont#106:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#108:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#109
  ifcont#110:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#112:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#113
  ifcont#114:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#116:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#117
  ifcont#118:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#120:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#121
  ifcont#122:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#124:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done#125
  ifcont#126:
    x64.leaRegRegImm32 rax, rax, -1
  __il_body#128:
    x64.leaRegRegReg rdx, rcx, rdx
    x64.cmpRegReg rdx, rcx
    x64.jcc below, __rc_panic#130
  __rc_ok:
    x64.leaRegRegReg rax, rdx, rax
    x64.cmpRegReg rax, rdx
    x64.jcc below, __rc_panic#132
    x64.jmp __il_cont#7
  done#125:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#121:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#117:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#113:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#109:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#105:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#101:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#97:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#93:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#89:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#85:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#81:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#77:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#73:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#69:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#65:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#61:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#57:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#53:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#49:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#45:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#41:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#37:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#33:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#29:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#25:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#21:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#17:
    x64.movRegReg rax, rcx
    x64.jmp __il_cont#7
  done#13:
    x64.movRegReg rax, rdx
    x64.jmp __il_cont#7
  done#9:
    x64.movRegReg rax, rcx
  __il_cont#7:
    x64.movRegReg r8, rax
    x64.jmp __il_cont#3
  done#5:
    x64.movRegReg r8, rdx
  __il_cont#3:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#130:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:261: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#132:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:261: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.jmp whilehdr
  count:
    x64.leaRegRegImm32 rdx, rax, 1
    x64.cmpRegReg rdx, rax
    x64.jcc below, __rc_panic
  __rc_ok:
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rax, rdx
  whilehdr:
    x64.cmpRegImm32 rcx, 3
    x64.jcc below, count
  whileexit:
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 2
    x64.callDirect g00
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:268: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @g00 {
  entry:
    arm64.prologue 16
    arm64.cmp x2, 0
    arm64.b.ne ifcont#2
  done#1:
    arm64.epilogue 16
    arm64.ret
  ifcont#2:
    arm64.sub x2, x2, 1
  __il_body#4:
    arm64.cmp x2, 0
    arm64.b.eq done#5
  ifcont#6:
    arm64.sub x2, x2, 1
  __il_body#8:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#10:
    arm64.sub x2, x2, 1
  __il_body#12:
    arm64.cmp x2, 0
    arm64.b.eq done#13
  ifcont#14:
    arm64.sub x2, x2, 1
  __il_body#16:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#18:
    arm64.sub x2, x2, 1
  __il_body#20:
    arm64.cmp x2, 0
    arm64.b.eq done#21
  ifcont#22:
    arm64.sub x2, x2, 1
  __il_body#24:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#26:
    arm64.sub x2, x2, 1
  __il_body#28:
    arm64.cmp x2, 0
    arm64.b.eq done#29
  ifcont#30:
    arm64.sub x2, x2, 1
  __il_body#32:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#34:
    arm64.sub x2, x2, 1
  __il_body#36:
    arm64.cmp x2, 0
    arm64.b.eq done#37
  ifcont#38:
    arm64.sub x2, x2, 1
  __il_body#40:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#42:
    arm64.sub x2, x2, 1
  __il_body#44:
    arm64.cmp x2, 0
    arm64.b.eq done#45
  ifcont#46:
    arm64.sub x2, x2, 1
  __il_body#48:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#50:
    arm64.sub x2, x2, 1
  __il_body#52:
    arm64.cmp x2, 0
    arm64.b.eq done#53
  ifcont#54:
    arm64.sub x2, x2, 1
  __il_body#56:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#58:
    arm64.sub x2, x2, 1
  __il_body#60:
    arm64.cmp x2, 0
    arm64.b.eq done#61
  ifcont#62:
    arm64.sub x2, x2, 1
  __il_body#64:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#66:
    arm64.sub x2, x2, 1
  __il_body#68:
    arm64.cmp x2, 0
    arm64.b.eq done#69
  ifcont#70:
    arm64.sub x2, x2, 1
  __il_body#72:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#74:
    arm64.sub x2, x2, 1
  __il_body#76:
    arm64.cmp x2, 0
    arm64.b.eq done#77
  ifcont#78:
    arm64.sub x2, x2, 1
  __il_body#80:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#82:
    arm64.sub x2, x2, 1
  __il_body#84:
    arm64.cmp x2, 0
    arm64.b.eq done#85
  ifcont#86:
    arm64.sub x2, x2, 1
  __il_body#88:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#90:
    arm64.sub x2, x2, 1
  __il_body#92:
    arm64.cmp x2, 0
    arm64.b.eq done#93
  ifcont#94:
    arm64.sub x2, x2, 1
  __il_body#96:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#98:
    arm64.sub x2, x2, 1
  __il_body#100:
    arm64.cmp x2, 0
    arm64.b.eq done#101
  ifcont#102:
    arm64.sub x2, x2, 1
  __il_body#104:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#106:
    arm64.sub x2, x2, 1
  __il_body#108:
    arm64.cmp x2, 0
    arm64.b.eq done#109
  ifcont#110:
    arm64.sub x2, x2, 1
  __il_body#112:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#114:
    arm64.sub x2, x2, 1
  __il_body#116:
    arm64.cmp x2, 0
    arm64.b.eq done#117
  ifcont#118:
    arm64.sub x2, x2, 1
  __il_body#120:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#122:
    arm64.sub x2, x2, 1
  __il_body#124:
    arm64.cmp x2, 0
    arm64.b.eq done#125
  ifcont#126:
    arm64.sub x2, x2, 1
  __il_body#128:
    arm64.add x1, x0, x1
    arm64.cmp x1, x0
    arm64.b.lo __rc_panic#130
  __rc_ok:
    arm64.add x0, x1, x2
    arm64.cmp x0, x1
    arm64.b.lo __rc_panic#132
    arm64.b __il_cont
  done#125:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#117:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#109:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#101:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#93:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#85:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#77:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#69:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#61:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#53:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#45:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#37:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#29:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#21:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#13:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#5:
    arm64.movRegReg x0, x1
  __il_cont:
    arm64.epilogue 16
    arm64.ret
  __rc_panic#130:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:261: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#132:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:261: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 0
    arm64.movImm x0, 0
    arm64.b whilehdr
  count:
    arm64.add x1, x2, 1
    arm64.cmp x1, x2
    arm64.b.lo __rc_panic
  __rc_ok:
    arm64.add x0, x0, 1
    arm64.movRegReg x2, x1
  whilehdr:
    arm64.cmp x0, 3
    arm64.b.lo count
  whileexit:
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.bl g00
    arm64.movRegReg x19, x0
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x19, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x25, x24, 56
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x25, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x25
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x24
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:268: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @g00 {
  entry:
    arm64.prologue 16
    arm64.cmp x2, 0
    arm64.b.ne ifcont#2
  done#1:
    arm64.epilogue 16
    arm64.ret
  ifcont#2:
    arm64.sub x2, x2, 1
  __il_body#4:
    arm64.cmp x2, 0
    arm64.b.eq done#5
  ifcont#6:
    arm64.sub x2, x2, 1
  __il_body#8:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#10:
    arm64.sub x2, x2, 1
  __il_body#12:
    arm64.cmp x2, 0
    arm64.b.eq done#13
  ifcont#14:
    arm64.sub x2, x2, 1
  __il_body#16:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#18:
    arm64.sub x2, x2, 1
  __il_body#20:
    arm64.cmp x2, 0
    arm64.b.eq done#21
  ifcont#22:
    arm64.sub x2, x2, 1
  __il_body#24:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#26:
    arm64.sub x2, x2, 1
  __il_body#28:
    arm64.cmp x2, 0
    arm64.b.eq done#29
  ifcont#30:
    arm64.sub x2, x2, 1
  __il_body#32:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#34:
    arm64.sub x2, x2, 1
  __il_body#36:
    arm64.cmp x2, 0
    arm64.b.eq done#37
  ifcont#38:
    arm64.sub x2, x2, 1
  __il_body#40:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#42:
    arm64.sub x2, x2, 1
  __il_body#44:
    arm64.cmp x2, 0
    arm64.b.eq done#45
  ifcont#46:
    arm64.sub x2, x2, 1
  __il_body#48:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#50:
    arm64.sub x2, x2, 1
  __il_body#52:
    arm64.cmp x2, 0
    arm64.b.eq done#53
  ifcont#54:
    arm64.sub x2, x2, 1
  __il_body#56:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#58:
    arm64.sub x2, x2, 1
  __il_body#60:
    arm64.cmp x2, 0
    arm64.b.eq done#61
  ifcont#62:
    arm64.sub x2, x2, 1
  __il_body#64:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#66:
    arm64.sub x2, x2, 1
  __il_body#68:
    arm64.cmp x2, 0
    arm64.b.eq done#69
  ifcont#70:
    arm64.sub x2, x2, 1
  __il_body#72:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#74:
    arm64.sub x2, x2, 1
  __il_body#76:
    arm64.cmp x2, 0
    arm64.b.eq done#77
  ifcont#78:
    arm64.sub x2, x2, 1
  __il_body#80:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#82:
    arm64.sub x2, x2, 1
  __il_body#84:
    arm64.cmp x2, 0
    arm64.b.eq done#85
  ifcont#86:
    arm64.sub x2, x2, 1
  __il_body#88:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#90:
    arm64.sub x2, x2, 1
  __il_body#92:
    arm64.cmp x2, 0
    arm64.b.eq done#93
  ifcont#94:
    arm64.sub x2, x2, 1
  __il_body#96:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#98:
    arm64.sub x2, x2, 1
  __il_body#100:
    arm64.cmp x2, 0
    arm64.b.eq done#101
  ifcont#102:
    arm64.sub x2, x2, 1
  __il_body#104:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#106:
    arm64.sub x2, x2, 1
  __il_body#108:
    arm64.cmp x2, 0
    arm64.b.eq done#109
  ifcont#110:
    arm64.sub x2, x2, 1
  __il_body#112:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#114:
    arm64.sub x2, x2, 1
  __il_body#116:
    arm64.cmp x2, 0
    arm64.b.eq done#117
  ifcont#118:
    arm64.sub x2, x2, 1
  __il_body#120:
    arm64.cmp x2, 0
    arm64.b.eq __il_cont
  ifcont#122:
    arm64.sub x2, x2, 1
  __il_body#124:
    arm64.cmp x2, 0
    arm64.b.eq done#125
  ifcont#126:
    arm64.sub x2, x2, 1
  __il_body#128:
    arm64.add x1, x0, x1
    arm64.cmp x1, x0
    arm64.b.lo __rc_panic#130
  __rc_ok:
    arm64.add x0, x1, x2
    arm64.cmp x0, x1
    arm64.b.lo __rc_panic#132
    arm64.b __il_cont
  done#125:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#117:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#109:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#101:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#93:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#85:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#77:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#69:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#61:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#53:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#45:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#37:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#29:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#21:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#13:
    arm64.movRegReg x0, x1
    arm64.b __il_cont
  done#5:
    arm64.movRegReg x0, x1
  __il_cont:
    arm64.epilogue 16
    arm64.ret
  __rc_panic#130:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:261: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#132:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:261: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 0
    arm64.movImm x0, 0
    arm64.b whilehdr
  count:
    arm64.add x1, x2, 1
    arm64.cmp x1, x2
    arm64.b.lo __rc_panic
  __rc_ok:
    arm64.add x0, x0, 1
    arm64.movRegReg x2, x1
  whilehdr:
    arm64.cmp x0, 3
    arm64.b.lo count
  whileexit:
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.bl g00
    arm64.movRegReg x19, x0
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x19, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x25, x24, 56
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x25, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x25
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x24
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-called-once-chain-one-level-past-the-depth-bound-keeps-one-call.test:268: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
}
```
