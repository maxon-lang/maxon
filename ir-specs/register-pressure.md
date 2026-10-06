---
feature: register-pressure
status: selfhosted
keywords: [register-allocator, E5001, register-pressure, hot-spill, diagnostic, value-origin, callee-saved]
category: register-allocator
---

# Register-pressure diagnostic (E5001)

## Documentation

The register allocator's pool is 14 GPRs. E5001 exists to refuse **the search**, not to refuse
**the spill** — and those are different things. The allocator will always emit a spill whose
placement is *forced*, because deciding it costs nothing. It raises E5001 only where relieving the
pressure would require it to *search* — an eviction tournament, a spill-cost model, iterated
re-splitting — which is the expensive, heuristic, non-deterministic machinery this design exists
to avoid. That gives three cases:

**1. A value idle across a pressured region → split it, free.** The **cold-spill splitter** (see
`register-spill`) stores it before the region and reloads it after, with **nothing added to the
loop body**. One placement, no choice.

**2. A value live across a fixed-register point → bracket it, cheap.** At a **call**, the callee
clobbers all 9 caller-saved registers, so a value that must survive the call can only live in one
of the **5 callee-saved** registers — or on the stack. When more than 5 values are live across a
call, the excess *must* go to memory: a store before the call and a reload after. That placement
is **forced by the ABI, not chosen by a search**, so the allocator simply emits it — *even inside
a loop*. The pair is cheap against the call it brackets: one store and one load, against a call
that already costs far more. (An `idiv` is the same case in miniature, reserving `RAX`/`RDX`.)

This is **not** E5001, and it must never become E5001. Such a program fits the machine — the
values are excluded only from the registers one op happens to clobber. Refusing it would be a
false positive, and the "restructuring" it would demand (hoist the loop's values into an array)
emits a load *and* a store at **every use** — strictly worse code than the single bracket the
compiler declined to emit.

**3. A value the loop genuinely USES, when the loop's working set exceeds the whole pool →
E5001.** Here there is nothing to bracket: spilling any of them puts a reload at *every use*,
every iteration, and choosing which to sacrifice is exactly the search this allocator refuses to
run. The working set is simply larger than the machine, and no spiller can fix that — only a
restructuring the programmer can do: hold the working set in an array (array elements are never
promoted into registers, so the spill stays spilled).

⛔ **CASE 3 IS THE ONLY ONE THAT ASKS THE AUTHOR FOR ANYTHING, AND THAT IS PRECISELY WHY IT STOPS AT THE
EDGE OF WHAT AN AUTHOR WROTE.** The refusal only makes sense against a restructuring somebody can perform.
A function the COMPILER emitted — the green-thread runtime, the memory manager, the synthesized builtin
bodies — has no source file, no line numbers, and no value the author can see, so there is nobody to
address and nothing to rewrite; the diagnostic cannot even be constructed, because every route
`defRangeOf` chases ends in a table only the parser fills. There the per-use reload is not a cost the
compiler is declining to pay silently, it is the only remaining way to emit the program at all, so the
splitter takes it and case 3 does not apply. See `the-runtime-overflows-its-own-pool-and-is-relieved`,
which is a four-line program: the whole overflow is inside `__gt_timer_check`.

The diagnostic is the feature, not the error path. Because SSA interference is chordal, the
per-program-point `maxlive` **is** the exact minimum register count for the program as lowered —
so E5001 fires **iff** the loop truly does not fit the full pool, never on a loop that a smarter
allocator would have colored. It is designed to let its consumer converge in one step:

- **The exact deficit** — "remove N values", not "too many" — against the **full pool of 14**.
- **Each blocking value's source def site**, recovered through the `ValueOrigin` table
  (`(funcIndex, ValueId)` → the Maxon op that defined it → its source span), ranked
  cheapest-to-move first: fewest uses inside the loop means fewest reloads after the array
  rewrite. A loop-carried value (an SSA phi) has no defining op, so it is chased to the
  incoming value it copies — its declaration.
- **The transformation**, named, not just the diagnosis.

The message is **deterministic byte-for-byte** — same program, same message — which is what
the ` ```maxoncstderr ` blocks below assert.

**Targets — a REGISTER-POOL gate, and the pool is the subject.** The deficit each message quotes is
computed against the target's own file, so the same program yields a DIFFERENT byte-exact message per
ISA — which is why the cases below come in `x64-windows, x64-linux` / `arm64-macos, arm64-linux` twins
rather than one shared case. `wasm32-wasi` is excluded from all of them because a stack machine has no
register cap to exceed, so E5001 cannot fire there at all. The float case is gated to x64 for the same
reason one file down: it is calibrated to x64's sixteen-deep XMM pool.

⚠ Each x64 twin fails on arm64 and each
arm64 twin on x64 with a *compiler-error mismatch* — the deficits genuinely differ — so no twin is
gated merely because nobody minted its pin.

## Tests

<!-- test: hot-loop-overflow -->
<!-- unsupported-targets: arm64-macos, arm64-linux, wasm32-wasi -->
Sixteen accumulators `s1`..`s16` are ALL updated every iteration, plus the loop counter `i`
— seventeen values the loop genuinely uses, against a pool of fourteen. None is idle across
the loop, so the cold-spill splitter cannot relieve any of them: this is a HOT overflow, and
the compiler reports E5001. The deficit is exactly 3 (17 − 14). The accumulators are each
used once in the loop (their own update), so they rank first (cheapest to hoist into an
array); the counter `i`, read by all sixteen updates plus the condition plus its own
increment, ranks last. Each value points at its declaration's source span.
```maxon
function hot(_ Integer) returns Integer
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var s14 = 14
	var s15 = 15
	var s16 = 16
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		s14 = s14 + i
		s15 = s15 + i
		s16 = s16 + i
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13 + s14 + s15 + s16
end 'hot'

function main() returns ExitCode
	return hot(0)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E5001: the loop at <fragment>:21 needs 3 more register(s) than are available
  17 values must be held in registers at once inside this loop, but
  only 14 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 3 of these 17 value(s) from the loop, cheapest first (ranked by reads inside the loop):
    <fragment>:3:11   read 1 time in the loop
    <fragment>:4:11   read 1 time in the loop
    <fragment>:5:11   read 1 time in the loop
    <fragment>:6:11   read 1 time in the loop
    <fragment>:7:11   read 1 time in the loop
    <fragment>:8:11   read 1 time in the loop
    <fragment>:9:11   read 1 time in the loop
    <fragment>:10:11   read 1 time in the loop
    <fragment>:11:11   read 1 time in the loop
    <fragment>:12:12   read 1 time in the loop
    <fragment>:13:12   read 1 time in the loop
    <fragment>:14:12   read 1 time in the loop
    <fragment>:15:12   read 1 time in the loop
    <fragment>:16:12   read 1 time in the loop
    <fragment>:17:12   read 1 time in the loop
    <fragment>:18:12   read 1 time in the loop
    <fragment>:19:10   read 18 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: hot-loop-overflow-arm64 -->
<!-- unsupported-targets: x64-windows, x64-linux, wasm32-wasi -->
arm64 allocates from 25 GPRs (x0-x14 ∪ x19-x28), not x64's 14 — x15 is the register the asynchronous-preemption trampoline returns through, since this ISA has no stack-popping return and every other scratch is spoken for, so an overflow needs more live values than `hot-loop-overflow`. Twenty-eight accumulators `s1`..`s28` are all updated every iteration, plus the counter `i` — twenty-nine values live at the loop header against a pool of twenty-five. The condition adds nothing: `i < N` lowers to a fused `cmp`+`b.cond`, materializing no boolean into a GPR. The deficit is exactly 4 (29 − 25), reported against the FULL arm64 pool, and each accumulator points at its declaration span.
```maxon
function hot(_ Integer) returns Integer
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var s14 = 14
	var s15 = 15
	var s16 = 16
	var s17 = 17
	var s18 = 18
	var s19 = 19
	var s20 = 20
	var s21 = 21
	var s22 = 22
	var s23 = 23
	var s24 = 24
	var s25 = 25
	var s26 = 26
	var s27 = 27
	var s28 = 28
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		s14 = s14 + i
		s15 = s15 + i
		s16 = s16 + i
		s17 = s17 + i
		s18 = s18 + i
		s19 = s19 + i
		s20 = s20 + i
		s21 = s21 + i
		s22 = s22 + i
		s23 = s23 + i
		s24 = s24 + i
		s25 = s25 + i
		s26 = s26 + i
		s27 = s27 + i
		s28 = s28 + i
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13 + s14 + s15 + s16 + s17 + s18 + s19 + s20 + s21 + s22 + s23 + s24 + s25 + s26 + s27 + s28
end 'hot'

function main() returns ExitCode
	return hot(0)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E5001: the loop at <fragment>:33 needs 4 more register(s) than are available
  29 values must be held in registers at once inside this loop, but
  only 25 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 4 of these 29 value(s) from the loop, cheapest first (ranked by reads inside the loop):
    <fragment>:3:11   read 1 time in the loop
    <fragment>:4:11   read 1 time in the loop
    <fragment>:5:11   read 1 time in the loop
    <fragment>:6:11   read 1 time in the loop
    <fragment>:7:11   read 1 time in the loop
    <fragment>:8:11   read 1 time in the loop
    <fragment>:9:11   read 1 time in the loop
    <fragment>:10:11   read 1 time in the loop
    <fragment>:11:11   read 1 time in the loop
    <fragment>:12:12   read 1 time in the loop
    <fragment>:13:12   read 1 time in the loop
    <fragment>:14:12   read 1 time in the loop
    <fragment>:15:12   read 1 time in the loop
    <fragment>:16:12   read 1 time in the loop
    <fragment>:17:12   read 1 time in the loop
    <fragment>:18:12   read 1 time in the loop
    <fragment>:19:12   read 1 time in the loop
    <fragment>:20:12   read 1 time in the loop
    <fragment>:21:12   read 1 time in the loop
    <fragment>:22:12   read 1 time in the loop
    <fragment>:23:12   read 1 time in the loop
    <fragment>:24:12   read 1 time in the loop
    <fragment>:25:12   read 1 time in the loop
    <fragment>:26:12   read 1 time in the loop
    <fragment>:27:12   read 1 time in the loop
    <fragment>:28:12   read 1 time in the loop
    <fragment>:29:12   read 1 time in the loop
    <fragment>:30:12   read 1 time in the loop
    <fragment>:31:10   read 30 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: assigned-only-loop-carried-vars-overflow -->
<!-- unsupported-targets: arm64-macos, arm64-linux, wasm32-wasi -->
⭐ **A blocking value that reads ZERO times in the loop is not always a compiler surplus, and this is
the case that says so.** Fifteen `var`s are declared before the loop, ASSIGNED inside it — one arm of
an `if`/`else if` chain each — and read only after it. That is the shape of an argument parser, and
`maxon-bin/Main.maxon`'s own `MaxonArgs.parse` is one, at 21 flags. Each is a BLOCK ARG defined
INSIDE the loop — the header phi the loop mints for a name it assigns, and the join phi the
`if`/`else if` chain merges that name into — so `isColdSpillable` refuses it on the DEPTH of that
def, and no live-range split shortens its range: the store would anchor at the block that defines
it, inside the loop, and the reloads would sit at its in-loop edge uses. Seventeen values (15 flags
+ `argc` + `i`) against a pool of fourteen, deficit exactly 3.

The ranked list prints all fifteen at **`read 0 times in the loop`**, and that is honest rather than
contradictory: the count is of OP operands, and every in-loop use of a loop-carried `var` is a
branch-edge arg (`RegionReadCount`). The word is `read` for exactly this reason — these values ARE
written in the loop, further down, and calling that "used 0 times" told an author the loop
does not touch a value they can see it assigning. ⚠ It also means a 0 here is **necessary but not
sufficient** as the tell of a surplus value (`docs/internals/register-allocation.md`, The contract): nothing
upstream over-produced anything in this program.

No arm64 twin, and not for want of a pin: arm64 allocates from 25 GPRs, so
seventeen live values fit and the same source compiles there. The
pool is the subject, exactly as this file's header says. `main` reaches `parseFlags` from two sites so the
called-once inliner leaves it a function; spliced into `main`, `i < 0` folds and the loop vanishes.
```maxon
function parseFlags(argc Integer) returns Integer
	var f0 = 0
	var f1 = 0
	var f2 = 0
	var f3 = 0
	var f4 = 0
	var f5 = 0
	var f6 = 0
	var f7 = 0
	var f8 = 0
	var f9 = 0
	var f10 = 0
	var f11 = 0
	var f12 = 0
	var f13 = 0
	var f14 = 0
	var i = 0
	while i < argc 'scan'
		if i == 0 'k0'
			f0 = 1
		end 'k0' else if i == 1 'k1'
			f1 = 1
		end 'k1' else if i == 2 'k2'
			f2 = 1
		end 'k2' else if i == 3 'k3'
			f3 = 1
		end 'k3' else if i == 4 'k4'
			f4 = 1
		end 'k4' else if i == 5 'k5'
			f5 = 1
		end 'k5' else if i == 6 'k6'
			f6 = 1
		end 'k6' else if i == 7 'k7'
			f7 = 1
		end 'k7' else if i == 8 'k8'
			f8 = 1
		end 'k8' else if i == 9 'k9'
			f9 = 1
		end 'k9' else if i == 10 'k10'
			f10 = 1
		end 'k10' else if i == 11 'k11'
			f11 = 1
		end 'k11' else if i == 12 'k12'
			f12 = 1
		end 'k12' else if i == 13 'k13'
			f13 = 1
		end 'k13' else if i == 14 'k14'
			f14 = 1
		end 'k14'
		i = i + 1
	end 'scan'
	return f0 + f1 + f2 + f3 + f4 + f5 + f6 + f7 + f8 + f9 + f10 + f11 + f12 + f13 + f14
end 'parseFlags'

function main() returns ExitCode
	return parseFlags(0) + parseFlags(15)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E5001: the loop at <fragment>:21 needs 3 more register(s) than are available
  17 values must be held in registers at once inside this loop, but
  only 14 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 3 of these 17 value(s) from the loop, cheapest first (ranked by reads inside the loop):
    <fragment>:3:11   read 0 times in the loop
    <fragment>:4:11   read 0 times in the loop
    <fragment>:5:11   read 0 times in the loop
    <fragment>:6:11   read 0 times in the loop
    <fragment>:7:11   read 0 times in the loop
    <fragment>:8:11   read 0 times in the loop
    <fragment>:9:11   read 0 times in the loop
    <fragment>:10:11   read 0 times in the loop
    <fragment>:11:11   read 0 times in the loop
    <fragment>:12:11   read 0 times in the loop
    <fragment>:13:12   read 0 times in the loop
    <fragment>:14:12   read 0 times in the loop
    <fragment>:15:12   read 0 times in the loop
    <fragment>:16:12   read 0 times in the loop
    <fragment>:17:12   read 0 times in the loop
    <fragment>:2:21   read 1 time in the loop
    <fragment>:18:10   read 17 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: hot-loop-across-call -->
A call inside a loop is **NOT** E5001 — it is case 2, the forced bracket. Five accumulators AND
the loop counter (six values) are live across the `sink` call inside the loop, but only five
callee-saved registers survive a call. One value therefore *cannot* stay in a register: the ABI
leaves it exactly one home, the stack. So the splitter stores it before the call and reloads it
after — a placement it does not choose, only obeys — and the loop body grows by one store and one
load, bracketing a call that costs far more. Nothing is searched, and no error is raised.

This case must never raise E5001. The program fits the machine: six values,
fourteen registers. The array rewrite an E5001 would demand puts all five accumulators in
memory and reads *and writes* each one every iteration — ten memory ops per iteration to avoid
two. Refusing the spill would produce strictly worse code AND a false error.

Every accumulator is loop-carried (an SSA phi) and its update is a back-edge arg, so this also
covers the two shapes a forced spill must handle: a phi's store anchors at its block's entry, and
a spilled value's branch-edge args are repointed at the reload alongside its op uses.

`sink(i) = i`, so each `sk` accumulates `0+1+2+3+4 = 10`: `s1..s5 = 11,12,13,14,15`, summing to 65.
```maxon
function sink(x Integer) returns Integer
	return x
end 'sink'

function hotCall(_ Integer) returns Integer
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var i = 0
	while i < 5 'loop'
		let r = sink(i)
		s1 = s1 + r
		s2 = s2 + r
		s3 = s3 + r
		s4 = s4 + r
		s5 = s5 + r
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5
end 'hotCall'

function main() returns ExitCode
	return hotCall(0)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
65
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
  __il_body:
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 3
    x64.movRegImm32 rsi, 4
    x64.movRegImm32 rdi, 5
    x64.movRegImm32 r8, 0
    x64.jmp whilehdr
  __il_cont#9:
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rdx, rdx, r8
    x64.leaRegRegReg rsi, rsi, r8
    x64.leaRegRegReg rdi, rdi, r8
    x64.leaRegRegImm32 r8, r8, 1
  whilehdr:
    x64.cmpRegImm32 r8, 5
    x64.jcc less, __il_cont#9
  whileexit:
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, rdx
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg r8, rax, rdi
  __il_cont#4:
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at hot-loop-across-call.test:26: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
  __il_body:
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 3
    x64.movRegImm32 rsi, 4
    x64.movRegImm32 rdi, 5
    x64.movRegImm32 r8, 0
    x64.jmp whilehdr
  __il_cont#9:
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rdx, rdx, r8
    x64.leaRegRegReg rsi, rsi, r8
    x64.leaRegRegReg rdi, rdi, r8
    x64.leaRegRegImm32 r8, r8, 1
  whilehdr:
    x64.cmpRegImm32 r8, 5
    x64.jcc less, __il_cont#9
  whileexit:
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, rdx
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg r8, rax, rdi
  __il_cont#4:
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at hot-loop-across-call.test:26: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.movImm x2, 3
    arm64.movImm x3, 4
    arm64.movImm x4, 5
    arm64.movImm x5, 0
    arm64.b whilehdr
  __il_cont#9:
    arm64.add x0, x0, x5
    arm64.add x1, x1, x5
    arm64.add x2, x2, x5
    arm64.add x3, x3, x5
    arm64.add x4, x4, x5
    arm64.add x5, x5, 1
  whilehdr:
    arm64.cmp x5, 5
    arm64.b.lt __il_cont#9
  whileexit:
    arm64.add x0, x0, x1
    arm64.add x0, x0, x2
    arm64.add x0, x0, x3
    arm64.add x0, x0, x4
  __il_cont#4:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at hot-loop-across-call.test:26: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.movImm x2, 3
    arm64.movImm x3, 4
    arm64.movImm x4, 5
    arm64.movImm x5, 0
    arm64.b whilehdr
  __il_cont#9:
    arm64.add x0, x0, x5
    arm64.add x1, x1, x5
    arm64.add x2, x2, x5
    arm64.add x3, x3, x5
    arm64.add x4, x4, x5
    arm64.add x5, x5, 1
  whilehdr:
    arm64.cmp x5, 5
    arm64.b.lt __il_cont#9
  whileexit:
    arm64.add x0, x0, x1
    arm64.add x0, x0, x2
    arm64.add x0, x0, x3
    arm64.add x0, x0, x4
  __il_cont#4:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at hot-loop-across-call.test:26: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: rescued-idle-around-loop -->
The CONTRAST to `hot-loop-overflow`: the SAME sixteen values, but here they are idle across
the loop (computed before it, summed after it) rather than updated inside it. The loop's
genuine working set is just `sum` and `i` — two values — so it fits, and the cold-spill
splitter stores the sixteen idle values around the loop. The loop body stays exactly
`sum = sum + i; i = i + 1` with NOTHING added (verify in the `TargetIr` pin: the `loop` block is
two `lea`s and a `jmp`). No E5001. Result is `sum(0..4)=10 + sum(1..16)=136 = 146`.
```maxon
function rescued(p Integer) returns Integer
	let k1 = p + 1
	let k2 = p + 2
	let k3 = p + 3
	let k4 = p + 4
	let k5 = p + 5
	let k6 = p + 6
	let k7 = p + 7
	let k8 = p + 8
	let k9 = p + 9
	let k10 = p + 10
	let k11 = p + 11
	let k12 = p + 12
	let k13 = p + 13
	let k14 = p + 14
	let k15 = p + 15
	let k16 = p + 16
	var sum = 0
	var i = 0
	while i < 5 'loop'
		sum = sum + i
		i = i + 1
	end 'loop'
	return sum + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12 + k13 + k14 + k15 + k16
end 'rescued'

function main() returns ExitCode
	return rescued(0)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
146
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @rescued {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 32
    x64.leaRegRegImm32 rax, rcx, 1
    x64.leaRegRegImm32 rdx, rcx, 2
    x64.leaRegRegImm32 rsi, rcx, 3
    x64.leaRegRegImm32 rdi, rcx, 4
    x64.leaRegRegImm32 r8, rcx, 5
    x64.leaRegRegImm32 r9, rcx, 6
    x64.leaRegRegImm32 r10, rcx, 7
    x64.leaRegRegImm32 r11, rcx, 8
    x64.leaRegRegImm32 rbx, rcx, 9
    x64.leaRegRegImm32 r12, rcx, 10
    x64.leaRegRegImm32 r13, rcx, 11
    x64.leaRegRegImm32 r14, rcx, 12
    x64.leaRegRegImm32 r15, rcx, 13
    x64.storeSlotReg slot0, r15
    x64.leaRegRegImm32 r15, rcx, 14
    x64.storeSlotReg slot1, r15
    x64.leaRegRegImm32 r15, rcx, 15
    x64.storeSlotReg slot3, r15
    x64.leaRegRegImm32 rcx, rcx, 16
    x64.storeSlotReg slot2, rcx
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 r15, 0
    x64.jmp whilehdr
  loop:
    x64.leaRegRegReg rcx, rcx, r15
    x64.leaRegRegImm32 r15, r15, 1
  whilehdr:
    x64.cmpRegImm32 r15, 5
    x64.jcc less, loop
  whileexit:
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, rdx
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 32
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 0
    x64.callDirect rescued
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at rescued-idle-around-loop.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @rescued {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 32
    x64.leaRegRegImm32 rax, rcx, 1
    x64.leaRegRegImm32 rdx, rcx, 2
    x64.leaRegRegImm32 rsi, rcx, 3
    x64.leaRegRegImm32 rdi, rcx, 4
    x64.leaRegRegImm32 r8, rcx, 5
    x64.leaRegRegImm32 r9, rcx, 6
    x64.leaRegRegImm32 r10, rcx, 7
    x64.leaRegRegImm32 r11, rcx, 8
    x64.leaRegRegImm32 rbx, rcx, 9
    x64.leaRegRegImm32 r12, rcx, 10
    x64.leaRegRegImm32 r13, rcx, 11
    x64.leaRegRegImm32 r14, rcx, 12
    x64.leaRegRegImm32 r15, rcx, 13
    x64.storeSlotReg slot0, r15
    x64.leaRegRegImm32 r15, rcx, 14
    x64.storeSlotReg slot1, r15
    x64.leaRegRegImm32 r15, rcx, 15
    x64.storeSlotReg slot3, r15
    x64.leaRegRegImm32 rcx, rcx, 16
    x64.storeSlotReg slot2, rcx
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 r15, 0
    x64.jmp whilehdr
  loop:
    x64.leaRegRegReg rcx, rcx, r15
    x64.leaRegRegImm32 r15, r15, 1
  whilehdr:
    x64.cmpRegImm32 r15, 5
    x64.jcc less, loop
  whileexit:
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, rdx
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 32
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 0
    x64.callDirect rescued
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at rescued-idle-around-loop.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.b whilehdr
  loop:
    arm64.add x0, x0, x1
    arm64.add x1, x1, 1
  whilehdr:
    arm64.cmp x1, 5
    arm64.b.lt loop
  whileexit:
    arm64.add x0, x0, 1
    arm64.add x0, x0, 2
    arm64.add x0, x0, 3
    arm64.add x0, x0, 4
    arm64.add x0, x0, 5
    arm64.add x0, x0, 6
    arm64.add x0, x0, 7
    arm64.add x0, x0, 8
    arm64.add x0, x0, 9
    arm64.add x0, x0, 10
    arm64.add x0, x0, 11
    arm64.add x0, x0, 12
    arm64.add x0, x0, 13
    arm64.add x0, x0, 14
    arm64.add x0, x0, 15
    arm64.add x0, x0, 16
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at rescued-idle-around-loop.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.b whilehdr
  loop:
    arm64.add x0, x0, x1
    arm64.add x1, x1, 1
  whilehdr:
    arm64.cmp x1, 5
    arm64.b.lt loop
  whileexit:
    arm64.add x0, x0, 1
    arm64.add x0, x0, 2
    arm64.add x0, x0, 3
    arm64.add x0, x0, 4
    arm64.add x0, x0, 5
    arm64.add x0, x0, 6
    arm64.add x0, x0, 7
    arm64.add x0, x0, 8
    arm64.add x0, x0, 9
    arm64.add x0, x0, 10
    arm64.add x0, x0, 11
    arm64.add x0, x0, 12
    arm64.add x0, x0, 13
    arm64.add x0, x0, 14
    arm64.add x0, x0, 15
    arm64.add x0, x0, 16
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at rescued-idle-around-loop.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: hot-loop-param-used -->
<!-- unsupported-targets: arm64-macos, arm64-linux, wasm32-wasi -->
A PARAMETER used every iteration is part of the hot working set, so it can appear in a
blocking set — and it is a user-visible, deletable value. Here `p` is read inside the loop
(`s1 = s1 + i + p`), so with thirteen accumulators plus the counter it is one of fifteen
values live at once against a pool of fourteen. `p` has NO defining op (it is captured at
entry, ValueId 0), yet it must NOT trip the Rule-3 "compiler-introduced value" panic: it is
resolved to its DECLARATION span in the signature (`<fragment>:2:14` — the `p` token) through
the `ParamOriginTable`. It ranks first (used once in the loop); the counter `i` ranks last. `main`
reaches `hot` from two sites so the called-once inliner leaves it a function; spliced into `main`, `p`
is a constant and the fourteen values fit the pool.
```maxon
function hot(p Integer) returns Integer
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i + p
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13
end 'hot'

function main() returns ExitCode
	return hot(0) + hot(1)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E5001: the loop at <fragment>:18 needs 1 more register(s) than are available
  15 values must be held in registers at once inside this loop, but
  only 14 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 1 of these 15 value(s) from the loop, cheapest first (ranked by reads inside the loop):
    <fragment>:2:14   read 1 time in the loop
    <fragment>:3:11   read 1 time in the loop
    <fragment>:4:11   read 1 time in the loop
    <fragment>:5:11   read 1 time in the loop
    <fragment>:6:11   read 1 time in the loop
    <fragment>:7:11   read 1 time in the loop
    <fragment>:8:11   read 1 time in the loop
    <fragment>:9:11   read 1 time in the loop
    <fragment>:10:11   read 1 time in the loop
    <fragment>:11:11   read 1 time in the loop
    <fragment>:12:12   read 1 time in the loop
    <fragment>:13:12   read 1 time in the loop
    <fragment>:14:12   read 1 time in the loop
    <fragment>:15:12   read 1 time in the loop
    <fragment>:16:10   read 15 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: straight-line-wide-signature-compiles -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
⚠ **THE SUBJECT IS THE x64 REGISTER POOL.** How wide a signature has to be before the allocator spills is
a property of how many registers there are, and arm64 has a different count — which is why this file
pairs its overflow cases with explicit `-arm64` twins rather than running one case on both.
⚠⚠ **NO `E5001` HERE: THE PEAK IS ALREADY PAID FOR.** Twenty-one parameters,
each read by both calls and again by the sum, are live across the calls and outnumber the fourteen-GPR pool
with no loop anywhere.

Every one of those values is ALREADY IN MEMORY by the time the full-pool
peak is reached: each is confined at the two calls, and the forced bracket the ABI owes it puts it in a slot.
Relieving the peak therefore costs NOTHING that has not already been paid — no new store, no new slot, only a
reload before the uses that remain — and `isForcedBracketVictim` offers exactly that at a COLD peak as
well as a confined one. The program compiles, and returns 21 + 21 + 21.

⚠ **THE `E5001` THE CONTRACT DEFINES IS THE LINE THIS CASE SITS BESIDE**
(`docs/internals/register-allocation.md`, The contract).
The refusal is for *"a value the LOOP genuinely uses, when the working set exceeds the whole pool"* — the
per-iteration cost of a reload at every use inside a loop body. A value used inside a loop is not
cold-spillable, so the re-relief arm cannot reach it: `hot-loop-overflow`, `hot-loop-param-used` and every
other loop case above raise E5001, byte for byte. This case is the one the cost argument does not
cover — straight-line code, where there is no iteration and no per-iteration cost.
```maxon
function sink(p1 Integer, p2 Integer, p3 Integer, p4 Integer, p5 Integer, p6 Integer, p7 Integer, p8 Integer, p9 Integer, p10 Integer, p11 Integer, p12 Integer, p13 Integer, p14 Integer, p15 Integer, p16 Integer, p17 Integer, p18 Integer, p19 Integer, p20 Integer, p21 Integer) returns Integer
	return p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11 + p12 + p13 + p14 + p15 + p16 + p17 + p18 + p19 + p20 + p21
end 'sink'

function wide(p1 Integer, p2 Integer, p3 Integer, p4 Integer, p5 Integer, p6 Integer, p7 Integer, p8 Integer, p9 Integer, p10 Integer, p11 Integer, p12 Integer, p13 Integer, p14 Integer, p15 Integer, p16 Integer, p17 Integer, p18 Integer, p19 Integer, p20 Integer, p21 Integer) returns Integer
	let a = sink(p1, p2: p2, p3: p3, p4: p4, p5: p5, p6: p6, p7: p7, p8: p8, p9: p9, p10: p10, p11: p11, p12: p12, p13: p13, p14: p14, p15: p15, p16: p16, p17: p17, p18: p18, p19: p19, p20: p20, p21: p21)
	let b = sink(p1, p2: p2, p3: p3, p4: p4, p5: p5, p6: p6, p7: p7, p8: p8, p9: p9, p10: p10, p11: p11, p12: p12, p13: p13, p14: p14, p15: p15, p16: p16, p17: p17, p18: p18, p19: p19, p20: p20, p21: p21)
	return a + b + p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11 + p12 + p13 + p14 + p15 + p16 + p17 + p18 + p19 + p20 + p21
end 'wide'

function main() returns ExitCode
	return wide(1, p2: 1, p3: 1, p4: 1, p5: 1, p6: 1, p7: 1, p8: 1, p9: 1, p10: 1, p11: 1, p12: 1, p13: 1, p14: 1, p15: 1, p16: 1, p17: 1, p18: 1, p19: 1, p20: 1, p21: 1)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
63
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
  __il_body:
    x64.movRegImm32 rax, 21
  __il_cont:
    x64.leaRegRegImm32 rax, rax, 21
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 r8, rax, 1
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
  __il_body:
    x64.movRegImm32 rax, 21
  __il_cont:
    x64.leaRegRegImm32 rax, rax, 21
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.leaRegRegImm32 r8, rax, 1
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```

<!-- test: hot-loop-param-used-arm64 -->
<!-- unsupported-targets: x64-windows, x64-linux, wasm32-wasi -->
The arm64 twin of `hot-loop-param-used`: a PARAMETER read every iteration is part of the hot working set and must resolve to its declaration span through `ParamOriginTable` (it is minted by no op) rather than trip the Rule-3 panic. `p` is read in `s1 = s1 + i + p`, so with twenty-six accumulators and the counter it is one of twenty-eight values live against arm64's 25-GPR pool. It ranks first (`<fragment>:2:14` — the `p` token); the counter `i` ranks last. Deficit 3. `main` reaches `hot` from two sites so the called-once inliner leaves it a function; spliced into `main`, `p` would fold to a constant and the diagnostic would be a DIFFERENT one — twenty-seven values and deficit 2, with every span pointing into `main`. The x64 twin fits its pool outright when spliced; this one does not, so the second site is what keeps the case pinning `hot`'s own shape rather than what makes it compile.
```maxon
function hot(p Integer) returns Integer
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var s14 = 14
	var s15 = 15
	var s16 = 16
	var s17 = 17
	var s18 = 18
	var s19 = 19
	var s20 = 20
	var s21 = 21
	var s22 = 22
	var s23 = 23
	var s24 = 24
	var s25 = 25
	var s26 = 26
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i + p
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		s14 = s14 + i
		s15 = s15 + i
		s16 = s16 + i
		s17 = s17 + i
		s18 = s18 + i
		s19 = s19 + i
		s20 = s20 + i
		s21 = s21 + i
		s22 = s22 + i
		s23 = s23 + i
		s24 = s24 + i
		s25 = s25 + i
		s26 = s26 + i
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13 + s14 + s15 + s16 + s17 + s18 + s19 + s20 + s21 + s22 + s23 + s24 + s25 + s26
end 'hot'

function main() returns ExitCode
	return hot(0) + hot(1)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E5001: the loop at <fragment>:31 needs 3 more register(s) than are available
  28 values must be held in registers at once inside this loop, but
  only 25 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 3 of these 28 value(s) from the loop, cheapest first (ranked by reads inside the loop):
    <fragment>:2:14   read 1 time in the loop
    <fragment>:3:11   read 1 time in the loop
    <fragment>:4:11   read 1 time in the loop
    <fragment>:5:11   read 1 time in the loop
    <fragment>:6:11   read 1 time in the loop
    <fragment>:7:11   read 1 time in the loop
    <fragment>:8:11   read 1 time in the loop
    <fragment>:9:11   read 1 time in the loop
    <fragment>:10:11   read 1 time in the loop
    <fragment>:11:11   read 1 time in the loop
    <fragment>:12:12   read 1 time in the loop
    <fragment>:13:12   read 1 time in the loop
    <fragment>:14:12   read 1 time in the loop
    <fragment>:15:12   read 1 time in the loop
    <fragment>:16:12   read 1 time in the loop
    <fragment>:17:12   read 1 time in the loop
    <fragment>:18:12   read 1 time in the loop
    <fragment>:19:12   read 1 time in the loop
    <fragment>:20:12   read 1 time in the loop
    <fragment>:21:12   read 1 time in the loop
    <fragment>:22:12   read 1 time in the loop
    <fragment>:23:12   read 1 time in the loop
    <fragment>:24:12   read 1 time in the loop
    <fragment>:25:12   read 1 time in the loop
    <fragment>:26:12   read 1 time in the loop
    <fragment>:27:12   read 1 time in the loop
    <fragment>:28:12   read 1 time in the loop
    <fragment>:29:10   read 28 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: hot-loop-rematerialized-constant -->
<!-- unsupported-targets: arm64-macos, arm64-linux, wasm32-wasi -->
A constant the loop uses (`let d`, read by `s14 = d - s14`) is REMATERIALIZED by the
splitter — re-emitted before its use with a FRESH ValueId — and that fresh id, minted after
parsing, has no origin of its own. When it lands in the blocking set it must NOT trip the
Rule-3 panic: it is chased through `SplitLineage` back to the original constant, so it resolves
to the `let d` literal (`<fragment>:24:11`). The remaining working set (thirteen accumulators
plus the counter) still overflows by two, and the deficit (2) never exceeds the sixteen listed
values. This pins that a fresh rematerialized id never trips a false panic.
```maxon
function hot() returns Integer
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var s14 = 14
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		let d = 1000000007
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		s14 = d - s14
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13 + s14
end 'hot'

function main() returns ExitCode
	return hot()
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E5001: the loop at <fragment>:19 needs 2 more register(s) than are available
  16 values must be held in registers at once inside this loop, but
  only 14 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 2 of these 16 value(s) from the loop, cheapest first (ranked by reads inside the loop):
    <fragment>:19:11   read 0 times in the loop
    <fragment>:20:11   read 0 times in the loop
    <fragment>:21:11   read 0 times in the loop
    <fragment>:22:11   read 0 times in the loop
    <fragment>:23:11   read 0 times in the loop
    <fragment>:25:11   read 0 times in the loop
    <fragment>:26:11   read 0 times in the loop
    <fragment>:27:11   read 0 times in the loop
    <fragment>:28:11   read 0 times in the loop
    <fragment>:29:13   read 0 times in the loop
    <fragment>:30:13   read 0 times in the loop
    <fragment>:31:13   read 0 times in the loop
    <fragment>:32:13   read 0 times in the loop
    <fragment>:16:12   read 1 time in the loop
    <fragment>:24:11   read 1 time in the loop
    <fragment>:17:10   read 15 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: hot-loop-rematerialized-constant-arm64 -->
<!-- unsupported-targets: x64-windows, x64-linux, wasm32-wasi -->
The arm64 twin of `hot-loop-rematerialized-constant`: a constant the loop uses (`let d`, read by `s26 = d - s26`) is REMATERIALIZED by the splitter with a fresh ValueId that has no origin of its own, and must be chased through `SplitLineage` back to the `let d` literal (`<fragment>:56:11`) rather than trip the Rule-3 panic. Twenty-six accumulators plus the counter plus the rematerialized constant overflow arm64's 25-GPR pool by three.
```maxon
function hot() returns Integer
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var s14 = 14
	var s15 = 15
	var s16 = 16
	var s17 = 17
	var s18 = 18
	var s19 = 19
	var s20 = 20
	var s21 = 21
	var s22 = 22
	var s23 = 23
	var s24 = 24
	var s25 = 25
	var s26 = 26
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		s14 = s14 + i
		s15 = s15 + i
		s16 = s16 + i
		s17 = s17 + i
		s18 = s18 + i
		s19 = s19 + i
		s20 = s20 + i
		s21 = s21 + i
		s22 = s22 + i
		s23 = s23 + i
		s24 = s24 + i
		s25 = s25 + i
		let d = 1000000007
		s26 = d - s26
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13 + s14 + s15 + s16 + s17 + s18 + s19 + s20 + s21 + s22 + s23 + s24 + s25 + s26
end 'hot'

function main() returns ExitCode
	return hot()
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E5001: the loop at <fragment>:31 needs 3 more register(s) than are available
  28 values must be held in registers at once inside this loop, but
  only 25 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 3 of these 28 value(s) from the loop, cheapest first (ranked by reads inside the loop):
    <fragment>:31:11   read 0 times in the loop
    <fragment>:32:11   read 0 times in the loop
    <fragment>:33:11   read 0 times in the loop
    <fragment>:34:11   read 0 times in the loop
    <fragment>:35:11   read 0 times in the loop
    <fragment>:36:11   read 0 times in the loop
    <fragment>:37:11   read 0 times in the loop
    <fragment>:38:11   read 0 times in the loop
    <fragment>:39:11   read 0 times in the loop
    <fragment>:40:13   read 0 times in the loop
    <fragment>:41:13   read 0 times in the loop
    <fragment>:42:13   read 0 times in the loop
    <fragment>:43:13   read 0 times in the loop
    <fragment>:44:13   read 0 times in the loop
    <fragment>:45:13   read 0 times in the loop
    <fragment>:46:13   read 0 times in the loop
    <fragment>:47:13   read 0 times in the loop
    <fragment>:48:13   read 0 times in the loop
    <fragment>:49:13   read 0 times in the loop
    <fragment>:50:13   read 0 times in the loop
    <fragment>:51:13   read 0 times in the loop
    <fragment>:52:13   read 0 times in the loop
    <fragment>:53:13   read 0 times in the loop
    <fragment>:54:13   read 0 times in the loop
    <fragment>:55:13   read 0 times in the loop
    <fragment>:28:12   read 1 time in the loop
    <fragment>:56:11   read 1 time in the loop
    <fragment>:29:10   read 27 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: two-loops-later-one-overflows -->
<!-- unsupported-targets: arm64-macos, arm64-linux, wasm32-wasi -->
⭐ **THE ANCHOR IS THE PEAK'S OWN LOOP, NOT THE FIRST LOOP IN THE FILE.** Two loops in one function
and only the SECOND overflows: the `pad` loop's entire body is `pad = pad + 1`, while the sixteen
accumulators and the counter are the working set of the loop below it. An anchor at the
smallest source line over EVERY block at loop depth ≥ 1 — i.e. over every loop in the function — would
name `pad`'s body at `:5` while every value it went on to rank was declared *after* that loop had
ended. A value declared at `:22` cannot be live in a loop that ends at `:6`: such a report contradicts
itself because the headline and the ranked list come from different places.

`pad` shows the same wrong region in the RANKING. It is live across the second loop (the `return`
reads it) and used ZERO times inside it; counting its two uses in the FIRST loop as uses "in the
loop" would rank it *below* every accumulator — the one value the author could most cheaply move
out of the way, listed last. Both numbers come from the peak's own loop.

It really is one of the eighteen, and that is the point of keeping it in the set: the value the
`return` reads is the first loop's own carried phi, defined at depth 1, so the store of a cold split
anchored at its def would land inside the first loop's body — the placement Rule 2 forbids. Nothing
can move it, so it holds a register across the second loop and is counted.
```maxon
function hot(_ Integer) returns Integer
	var pad = 0
	while pad < 3 'pad'
		pad = pad + 1
	end 'pad'
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var s14 = 14
	var s15 = 15
	var s16 = 16
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		s14 = s14 + i
		s15 = s15 + i
		s16 = s16 + i
		i = i + 1
	end 'loop'
	return pad + s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13 + s14 + s15 + s16
end 'hot'

function main() returns ExitCode
	return hot(0)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E5001: the loop at <fragment>:25 needs 4 more register(s) than are available
  18 values must be held in registers at once inside this loop, but
  only 14 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 4 of these 18 value(s) from the loop, cheapest first (ranked by reads inside the loop):
    <fragment>:3:12   read 0 times in the loop
    <fragment>:7:11   read 1 time in the loop
    <fragment>:8:11   read 1 time in the loop
    <fragment>:9:11   read 1 time in the loop
    <fragment>:10:11   read 1 time in the loop
    <fragment>:11:11   read 1 time in the loop
    <fragment>:12:11   read 1 time in the loop
    <fragment>:13:11   read 1 time in the loop
    <fragment>:14:11   read 1 time in the loop
    <fragment>:15:11   read 1 time in the loop
    <fragment>:16:12   read 1 time in the loop
    <fragment>:17:12   read 1 time in the loop
    <fragment>:18:12   read 1 time in the loop
    <fragment>:19:12   read 1 time in the loop
    <fragment>:20:12   read 1 time in the loop
    <fragment>:21:12   read 1 time in the loop
    <fragment>:22:12   read 1 time in the loop
    <fragment>:23:10   read 18 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: nested-loop-anchors-on-the-nest -->
<!-- unsupported-targets: arm64-macos, arm64-linux, wasm32-wasi -->
A NEST IS ONE REGION, and the loop the message names is the OUTERMOST one containing the peak. The
overflow is at the inner loop, but the values blocking it are not all the inner loop's: `acc` and `o`
are the outer loop's, live across the inner one and read only outside it. Neither can be moved out of
the way — a cold split's store anchors at the def, and both are defined by the outer loop's phis at
depth 1, so the store would land in the outer body — so the region the author must actually thin is
the whole nest. That is what the headline names (`:6`, the outer body's first statement) and what the
use counts are taken over (`s1` is read once by its inner-loop update and once by the outer sum, so
two).

Naming the INNER loop instead would report `acc` as "used 0 times in the loop" and ask for it to be
removed from a loop it is not in, while the array rewrite that would actually relieve the peak has to
happen in the outer body either way. This case is the difference between those two readings: every
line of it is identical under a whole-function anchor, so it also pins that restricting the scan to
the peak's own nest changes nothing for a function that has only one.
```maxon
function hot(_ Integer) returns Integer
	var acc = 0
	var o = 0
	while o < 2 'outer'
		var i = 0
		var s1 = 1
		var s2 = 2
		var s3 = 3
		var s4 = 4
		var s5 = 5
		var s6 = 6
		var s7 = 7
		var s8 = 8
		var s9 = 9
		var s10 = 10
		var s11 = 11
		var s12 = 12
		var s13 = 13
		while i < 5 'inner'
			s1 = s1 + i
			s2 = s2 + i
			s3 = s3 + i
			s4 = s4 + i
			s5 = s5 + i
			s6 = s6 + i
			s7 = s7 + i
			s8 = s8 + i
			s9 = s9 + i
			s10 = s10 + i
			s11 = s11 + i
			s12 = s12 + i
			s13 = s13 + i
			i = i + 1
		end 'inner'
		acc = acc + s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13
		o = o + 1
	end 'outer'
	return acc
end 'hot'

function main() returns ExitCode
	return hot(0)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E5001: the loop at <fragment>:6 needs 2 more register(s) than are available
  16 values must be held in registers at once inside this loop, but
  only 14 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 2 of these 16 value(s) from the loop, cheapest first (ranked by reads inside the loop):
    <fragment>:3:12   read 1 time in the loop
    <fragment>:4:10   read 2 times in the loop
    <fragment>:7:12   read 2 times in the loop
    <fragment>:8:12   read 2 times in the loop
    <fragment>:9:12   read 2 times in the loop
    <fragment>:10:12   read 2 times in the loop
    <fragment>:11:12   read 2 times in the loop
    <fragment>:12:12   read 2 times in the loop
    <fragment>:13:12   read 2 times in the loop
    <fragment>:14:12   read 2 times in the loop
    <fragment>:15:12   read 2 times in the loop
    <fragment>:16:13   read 2 times in the loop
    <fragment>:17:13   read 2 times in the loop
    <fragment>:18:13   read 2 times in the loop
    <fragment>:19:13   read 2 times in the loop
    <fragment>:6:11   read 15 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: relievable-param-live-across-loop -->
A parameter LIVE ACROSS the loop but not USED inside it is cold-spillable, so high pressure
here is relieved — no E5001. `p` is read before the loop (the `k` computations) and after it
(the `return`), so it is live across the loop; but the loop body touches only `sum` and `i`.
The sixteen `k` values and `p` are all idle across the loop, so the splitter stores them
around it and the body stays two `lea`s and a `jmp`. It compiles and runs. Result is
`sum(0..4)=10 + sum(1..16)=136 + p=0 = 146`.
```maxon
function relievable(p Integer) returns Integer
	let k1 = p + 1
	let k2 = p + 2
	let k3 = p + 3
	let k4 = p + 4
	let k5 = p + 5
	let k6 = p + 6
	let k7 = p + 7
	let k8 = p + 8
	let k9 = p + 9
	let k10 = p + 10
	let k11 = p + 11
	let k12 = p + 12
	let k13 = p + 13
	let k14 = p + 14
	let k15 = p + 15
	let k16 = p + 16
	var sum = 0
	var i = 0
	while i < 5 'loop'
		sum = sum + i
		i = i + 1
	end 'loop'
	return sum + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12 + k13 + k14 + k15 + k16 + p
end 'relievable'

function main() returns ExitCode
	return relievable(0)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
146
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @relievable {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 48
    x64.storeSlotReg slot0, rcx
    x64.leaRegRegImm32 rax, rcx, 1
    x64.leaRegRegImm32 rdx, rcx, 2
    x64.leaRegRegImm32 rsi, rcx, 3
    x64.leaRegRegImm32 rdi, rcx, 4
    x64.leaRegRegImm32 r8, rcx, 5
    x64.leaRegRegImm32 r9, rcx, 6
    x64.leaRegRegImm32 r10, rcx, 7
    x64.leaRegRegImm32 r11, rcx, 8
    x64.leaRegRegImm32 rbx, rcx, 9
    x64.leaRegRegImm32 r12, rcx, 10
    x64.leaRegRegImm32 r13, rcx, 11
    x64.leaRegRegImm32 r14, rcx, 12
    x64.leaRegRegImm32 r15, rcx, 13
    x64.storeSlotReg slot1, r15
    x64.leaRegRegImm32 r15, rcx, 14
    x64.storeSlotReg slot2, r15
    x64.leaRegRegImm32 r15, rcx, 15
    x64.storeSlotReg slot4, r15
    x64.leaRegRegImm32 rcx, rcx, 16
    x64.storeSlotReg slot3, rcx
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 r15, 0
    x64.jmp whilehdr
  loop:
    x64.leaRegRegReg rcx, rcx, r15
    x64.leaRegRegImm32 r15, r15, 1
  whilehdr:
    x64.cmpRegImm32 r15, 5
    x64.jcc less, loop
  whileexit:
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, rdx
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 48
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 0
    x64.callDirect relievable
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at relievable-param-live-across-loop.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @relievable {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 48
    x64.storeSlotReg slot0, rcx
    x64.leaRegRegImm32 rax, rcx, 1
    x64.leaRegRegImm32 rdx, rcx, 2
    x64.leaRegRegImm32 rsi, rcx, 3
    x64.leaRegRegImm32 rdi, rcx, 4
    x64.leaRegRegImm32 r8, rcx, 5
    x64.leaRegRegImm32 r9, rcx, 6
    x64.leaRegRegImm32 r10, rcx, 7
    x64.leaRegRegImm32 r11, rcx, 8
    x64.leaRegRegImm32 rbx, rcx, 9
    x64.leaRegRegImm32 r12, rcx, 10
    x64.leaRegRegImm32 r13, rcx, 11
    x64.leaRegRegImm32 r14, rcx, 12
    x64.leaRegRegImm32 r15, rcx, 13
    x64.storeSlotReg slot1, r15
    x64.leaRegRegImm32 r15, rcx, 14
    x64.storeSlotReg slot2, r15
    x64.leaRegRegImm32 r15, rcx, 15
    x64.storeSlotReg slot4, r15
    x64.leaRegRegImm32 rcx, rcx, 16
    x64.storeSlotReg slot3, rcx
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 r15, 0
    x64.jmp whilehdr
  loop:
    x64.leaRegRegReg rcx, rcx, r15
    x64.leaRegRegImm32 r15, r15, 1
  whilehdr:
    x64.cmpRegImm32 r15, 5
    x64.jcc less, loop
  whileexit:
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, rdx
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 48
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 0
    x64.callDirect relievable
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at relievable-param-live-across-loop.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.b whilehdr
  loop:
    arm64.add x0, x0, x1
    arm64.add x1, x1, 1
  whilehdr:
    arm64.cmp x1, 5
    arm64.b.lt loop
  whileexit:
    arm64.add x0, x0, 1
    arm64.add x0, x0, 2
    arm64.add x0, x0, 3
    arm64.add x0, x0, 4
    arm64.add x0, x0, 5
    arm64.add x0, x0, 6
    arm64.add x0, x0, 7
    arm64.add x0, x0, 8
    arm64.add x0, x0, 9
    arm64.add x0, x0, 10
    arm64.add x0, x0, 11
    arm64.add x0, x0, 12
    arm64.add x0, x0, 13
    arm64.add x0, x0, 14
    arm64.add x0, x0, 15
    arm64.add x0, x0, 16
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at relievable-param-live-across-loop.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.b whilehdr
  loop:
    arm64.add x0, x0, x1
    arm64.add x1, x1, 1
  whilehdr:
    arm64.cmp x1, 5
    arm64.b.lt loop
  whileexit:
    arm64.add x0, x0, 1
    arm64.add x0, x0, 2
    arm64.add x0, x0, 3
    arm64.add x0, x0, 4
    arm64.add x0, x0, 5
    arm64.add x0, x0, 6
    arm64.add x0, x0, 7
    arm64.add x0, x0, 8
    arm64.add x0, x0, 9
    arm64.add x0, x0, 10
    arm64.add x0, x0, 11
    arm64.add x0, x0, 12
    arm64.add x0, x0, 13
    arm64.add x0, x0, 14
    arm64.add x0, x0, 15
    arm64.add x0, x0, 16
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at relievable-param-live-across-loop.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: dead-def-parameter-holds-a-register -->
A DEAD DEF still costs a register, and the pressure model has to say so. The trailing `_` is never read, so
it is live at NO program point and no popcount over a live set can see it — yet `mov rax, [rbp+k]`
CLOBBERS a register whatever becomes of the value, so the colorer must hand it one. Fourteen live
parameters plus that one dead materialization is FIFTEEN registers against a pool of fourteen, at a
single op. Before `addOpTransientPressure` counted it, the splitter found no overflow at all,
declared the function relieved, and `chooseRegister` then panicked with every register blocked —
the one demand a live-set model is structurally blind to. Result is `sum(1..14) = 105`.
```maxon
function f(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, _ Integer) returns Integer
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14, _: 15)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
105
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
  __il_body:
    x64.movRegImm32 r8, 105
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
  __il_body:
    x64.movRegImm32 r8, 105
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 105
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 105
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: dead-def-parameter-at-the-pool-boundary -->
The BOUNDARY below `dead-def-parameter-holds-a-register`, so a regression that moves the cliff is
caught from both sides. Thirteen live parameters plus one dead materialization is exactly the pool
of fourteen: it fits, nothing is split, and the `TargetIr` pin must show no store and no reload. Result
is `sum(1..13) = 91`.
```maxon
function f(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, _ Integer) returns Integer
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, _: 14)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
91
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
  __il_body:
    x64.movRegImm32 r8, 91
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
  __il_body:
    x64.movRegImm32 r8, 91
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 91
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 91
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: dead-def-mid-block-holds-a-register -->
The same demand where the dead def is NOT a parameter, so the correction cannot be mistaken for a
fact about the entry block. All fourteen parameters are read by the `return`, so all fourteen are
live across `unused` — and `unused` itself is read by nothing. Its `lea` still writes a register,
and with no operand of its own dying there (`a1` and `a2` are both read later) there is none to
inherit, so the point genuinely wants fifteen. Result is `sum(1..14) = 105`.
```maxon
function f(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer) returns Integer
	let unused = a1 + a2
	print("{unused}")
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
105
```
```stdout
5

```

```TargetIr:x64-windows
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
  __il_body:
    x64.movRegImm32 rbx, 5
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRegImm32 rcx, rbx, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.leaRegRegImm32 r14, r13, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r14, rbx
    x64.storeBaseDispReg.word64 [r13 + 0], r14
    x64.storeBaseDispReg.word64 [r13 + 8], rbx
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r13
    x64.callDirect print
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 105
  __rc_ok:
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
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
    x64.prologue 32
  __il_body:
    x64.movRegImm32 rbx, 5
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRegImm32 rcx, rbx, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.leaRegRegImm32 r14, r13, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r14, rbx
    x64.storeBaseDispReg.word64 [r13 + 0], r14
    x64.storeBaseDispReg.word64 [r13 + 8], rbx
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r13
    x64.callDirect print
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 105
  __rc_ok:
    x64.epilogue 32
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
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#5:
    arm64.movImm x19, 5
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.add x0, x19, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.add x22, x21, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x22, x19
    arm64.storeBaseDispReg.word64 [x21 + 0], x22
    arm64.storeBaseDispReg.word64 [x21 + 8], x19
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body#7:
    arm64.movRegReg x0, x21
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.movImm x0, 105
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```

```TargetIr:arm64-linux
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
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#5:
    arm64.movImm x19, 5
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.add x0, x19, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.add x22, x21, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x22, x19
    arm64.storeBaseDispReg.word64 [x21 + 0], x22
    arm64.storeBaseDispReg.word64 [x21 + 8], x19
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body#7:
    arm64.movRegReg x0, x21
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.movImm x0, 105
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: dead-def-inherits-only-its-own-register-file -->
A dead def may inherit the register of a use that DIES at the same op — but only one of its OWN FILE.
`dyingRegsAt` names every dying operand's register whatever file it is in, yet `allocateDef` ORs
`fullRegisterMask() and not classPool` into `blocked`, so a dying XMM frees nothing a GPR def can take.
Here `trunc(x)` is a `cvttsd2si` whose GPR def is read by nothing and whose only dying operand is the
FLOAT `x`: fourteen live parameters plus that dead GPR def still want fifteen GPRs, and scoring the
dying float as "this op frees a register" put the colorer back into `chooseRegister`'s panic — the
dead-def correction's own wrong answer, one register file over. Result is `sum(1..14) = 105`.
```maxon
function f(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, x Real) returns Integer
	let unused = trunc(x)
	print("{unused}")
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14, x: 1.5)
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)
```
```exitcode
105
```
```stdout
1
```

```TargetIr:x64-windows
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
  __il_body:
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRegImm32 rcx, rbx, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.leaRegRegImm32 r14, r13, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r14, rbx
    x64.storeBaseDispReg.word64 [r13 + 0], r14
    x64.storeBaseDispReg.word64 [r13 + 8], rbx
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r13
    x64.callDirect print
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 105
  __rc_ok:
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
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
    x64.prologue 32
  __il_body:
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRegImm32 rcx, rbx, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.leaRegRegImm32 r14, r13, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r14, rbx
    x64.storeBaseDispReg.word64 [r13 + 0], r14
    x64.storeBaseDispReg.word64 [r13 + 8], rbx
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r13
    x64.callDirect print
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 105
  __rc_ok:
    x64.epilogue 32
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
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#5:
    arm64.movImm x19, 1
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.add x0, x19, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.add x22, x21, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x22, x19
    arm64.storeBaseDispReg.word64 [x21 + 0], x22
    arm64.storeBaseDispReg.word64 [x21 + 8], x19
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body#7:
    arm64.movRegReg x0, x21
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.movImm x0, 105
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```

```TargetIr:arm64-linux
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
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#5:
    arm64.movImm x19, 1
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.add x0, x19, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.add x22, x21, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x22, x19
    arm64.storeBaseDispReg.word64 [x21 + 0], x22
    arm64.storeBaseDispReg.word64 [x21 + 8], x19
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body#7:
    arm64.movRegReg x0, x21
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.movImm x0, 105
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: dead-def-inherits-only-its-own-register-file-float -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
⚠ **THE SUBJECT IS THE x64 REGISTER FILE SPLIT** — a float def must not take pressure from the integer
file. The two files' sizes are this ISA's, so the arm64 reading is its own case.
The MIRROR of `dead-def-inherits-only-its-own-register-file`, so the correction is pinned in the file it
is easier to forget. `y as float` is a `cvtsi2sd` whose XMM def is read by nothing and whose only dying
operand is the INT `y`; sixteen live float parameters plus that dead XMM def want a seventeenth XMM
against the sixteen-register float pool. A class-blind "does this op free a register?" scored the dying
GPR as relief and panicked the colorer in the XMM file. Gated to x64 because it counts against x64's
sixteen-deep float pool. Every argument is `1.0`, so the sum is `16.0`.
```maxon
typealias SmallReal = float(0.0 to 1000.0)
function g(f0 Real, f1 Real, f2 Real, f3 Real, f4 Real, f5 Real, f6 Real, f7 Real, f8 Real, f9 Real, f10 Real, f11 Real, f12 Real, f13 Real, f14 Real, f15 Real, y Integer) returns Real
	let unused = y as SmallReal
	print("{unused}")
	return f0 + f1 + f2 + f3 + f4 + f5 + f6 + f7 + f8 + f9 + f10 + f11 + f12 + f13 + f14 + f15
end 'g'

function main() returns ExitCode
	return trunc(g(1.0, f1: 1.0, f2: 1.0, f3: 1.0, f4: 1.0, f5: 1.0, f6: 1.0, f7: 1.0, f8: 1.0, f9: 1.0, f10: 1.0, f11: 1.0, f12: 1.0, f13: 1.0, f14: 1.0, f15: 1.0, y: 3))
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)
```
```exitcode
16
```
```stdout
3.0

```

```TargetIr:x64-windows
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
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
    x64.prologue 40
  __il_body:
    x64.movsdRegRip xmm0, [rip + __fconst_3]
    x64.callDirect __float_toString
    x64.movRegReg rbx, r8
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
    x64.leaRegRegImm32 rcx, r13, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.leaRegRegImm32 r15, r14, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r15, r13
    x64.storeBaseDispReg.word64 [r14 + 0], r15
    x64.storeBaseDispReg.word64 [r14 + 8], r13
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r14 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r14 + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect print
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movsdRegRip xmm0, [rip + __fconst_16]
  __il_cont:
    x64.cvttsd2siRegReg r8, xmm0
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_205]  ; "panic at dead-def-inherits-only-its-own-register-file-float.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
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
    x64.prologue 40
  __il_body:
    x64.movsdRegRip xmm0, [rip + __fconst_3]
    x64.callDirect __float_toString
    x64.movRegReg rbx, r8
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
    x64.leaRegRegImm32 rcx, r13, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.leaRegRegImm32 r15, r14, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r15, r13
    x64.storeBaseDispReg.word64 [r14 + 0], r15
    x64.storeBaseDispReg.word64 [r14 + 8], r13
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r14 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r14 + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect print
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movsdRegRip xmm0, [rip + __fconst_16]
  __il_cont:
    x64.cvttsd2siRegReg r8, xmm0
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_205]  ; "panic at dead-def-inherits-only-its-own-register-file-float.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

<!-- test: dead-def-past-the-arm64-pool-across-register-files -->
The arm64 twin of `dead-def-inherits-only-its-own-register-file`, at the arm64 cliff: twenty-six live
integer parameters plus a dead GPR def whose only dying operand is a float. Without the class filter
it panics `chooseRegister` on arm64 for exactly the reason the x64 case does, so the class filter is
pinned on BOTH lanes of the shared pressure model. Ungated: on x64 the same
program is well past the pool and the splitter relieves it cold, which is worth pinning too. The
trailing arguments are zero so the sum fits an exit code. Result is `sum(1..20) = 210`.
```maxon
function f(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer, a20 Integer, a21 Integer, a22 Integer, a23 Integer, a24 Integer, a25 Integer, x Real) returns Integer
	let unused = trunc(x)
	print("{unused}")
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22 + a23 + a24 + a25
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14, a14: 15, a15: 16, a16: 17, a17: 18, a18: 19, a19: 20, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, x: 1.5)
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)
```
```exitcode
210
```
```stdout
1

```

```TargetIr:x64-windows
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
  __il_body:
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRegImm32 rcx, rbx, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.leaRegRegImm32 r14, r13, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r14, rbx
    x64.storeBaseDispReg.word64 [r13 + 0], r14
    x64.storeBaseDispReg.word64 [r13 + 8], rbx
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r13
    x64.callDirect print
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 210
  __rc_ok:
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
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
    x64.prologue 32
  __il_body:
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRegImm32 rcx, rbx, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.leaRegRegImm32 r14, r13, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r14, rbx
    x64.storeBaseDispReg.word64 [r13 + 0], r14
    x64.storeBaseDispReg.word64 [r13 + 8], rbx
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r13
    x64.callDirect print
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 210
  __rc_ok:
    x64.epilogue 32
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
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 1
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.add x0, x19, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.add x22, x21, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x22, x19
    arm64.storeBaseDispReg.word64 [x21 + 0], x22
    arm64.storeBaseDispReg.word64 [x21 + 8], x19
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x21
    arm64.bl print
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.movImm x0, 210
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```

```TargetIr:arm64-linux
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
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 1
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.add x0, x19, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.add x22, x21, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x22, x19
    arm64.storeBaseDispReg.word64 [x21 + 0], x22
    arm64.storeBaseDispReg.word64 [x21 + 8], x19
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x21
    arm64.bl print
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.movImm x0, 210
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: dead-reuse-def-costs-one-copy-not-two -->
The dead-def transient and the reuse-copy transient are the SAME register and must be charged ONCE.
`a1 * a2` is a two-address `imul` whose dest is a REUSE of `a1`, and `a1` is live after (the `return`
reads it), so the allocator materializes `mov dest, a1` before the op — and the dest is then read by
nothing. That is one extra register at one point, not two: `addOpTransientPressure` answers in the reuse
arm and stops, rather than charging the copy and then charging the dead def again. Double-charging would
not crash, it would over-split — a store and a reload this program does not need. Result is
`sum(1..14) = 105`.
```maxon
function f(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer) returns Integer
	let unused = a1 * a2
	print("{unused}")
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
105
```
```stdout
6

```

```TargetIr:x64-windows
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
  __il_body:
    x64.movRegImm32 rbx, 6
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRegImm32 rcx, rbx, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.leaRegRegImm32 r14, r13, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r14, rbx
    x64.storeBaseDispReg.word64 [r13 + 0], r14
    x64.storeBaseDispReg.word64 [r13 + 8], rbx
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r13
    x64.callDirect print
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 105
  __rc_ok:
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
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
    x64.prologue 32
  __il_body:
    x64.movRegImm32 rbx, 6
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRegImm32 rcx, rbx, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.leaRegRegImm32 r14, r13, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r14, rbx
    x64.storeBaseDispReg.word64 [r13 + 0], r14
    x64.storeBaseDispReg.word64 [r13 + 8], rbx
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r13
    x64.callDirect print
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 105
  __rc_ok:
    x64.epilogue 32
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
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#5:
    arm64.movImm x19, 6
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.add x0, x19, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.add x22, x21, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x22, x19
    arm64.storeBaseDispReg.word64 [x21 + 0], x22
    arm64.storeBaseDispReg.word64 [x21 + 8], x19
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body#7:
    arm64.movRegReg x0, x21
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.movImm x0, 105
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```

```TargetIr:arm64-linux
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
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#5:
    arm64.movImm x19, 6
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.add x0, x19, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.add x22, x21, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x22, x19
    arm64.storeBaseDispReg.word64 [x21 + 0], x22
    arm64.storeBaseDispReg.word64 [x21 + 8], x19
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body#7:
    arm64.movRegReg x0, x21
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.movImm x0, 105
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: dead-def-parameter-past-the-arm64-pool -->
The arm64 twin of the boundary. arm64 allocates from 25 GPRs (x0-x14 ∪ x19-x28 — x15 is the
asynchronous-preemption trampoline's return register), so the dead-def cliff sits at 26 where x64's
sits at 15 — twenty-six live parameters plus one dead materialization is two past the arm64 pool, the
same cliff the x64 boundary pins. It is not gated to arm64: on x64 the same program is simply well past the pool and the
splitter relieves it cold, which is worth pinning too. The trailing arguments are zero so the sum
fits an exit code while the first twenty stay distinct — a swapped register still changes the
answer. Result is `sum(1..20) = 210`.
```maxon
function f(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer, a20 Integer, a21 Integer, a22 Integer, a23 Integer, a24 Integer, a25 Integer, _ Integer) returns Integer
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22 + a23 + a24 + a25
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14, a14: 15, a15: 16, a16: 17, a17: 18, a18: 19, a19: 20, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, _: 0)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
210
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
  __il_body:
    x64.movRegImm32 r8, 210
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
  __il_body:
    x64.movRegImm32 r8, 210
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 210
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x0, 210
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: reuse-def-confined-by-a-later-call -->
A REUSE DEF is confined by its OWN admissible set, and the pressure analysis has to see it.

Two sequential loops over an `Array`, the first carrying a `carry` across its back edge. `keep` is a
real call on every iteration of the first loop — an impure helper, so it is never inlined, in a
`normal` block, so it confines (the managed accesses' own slow arms are cold and confine nothing;
see `cold-call-spilling`). Five values are live across it — confined to the five callee-saved GPRs —
and `carry = widened shr 32` is a two-address reuse def that becomes a SIXTH. It holds a register at
an op where it is in no live set, so no popcount over a live row can see it; the full-pool figure
counts it (14 registers, no overflow), and the CONFINED census must too, whatever the premise that
*"a transient is not a value, so it is not confined by anything"* suggests. It is a value: it crosses
the next iteration's call and may live in exactly those five registers. A census that misses it
relieves nothing, and `chooseRegister` dies with `forbidden` covering the caller-saved half and the
held set covering the rest. `opConfinedTransient` is the rule; inheriting the input's register is only free when every
register the input may hold is one the def may hold too.

`0xFFFFFFFF << 4` is `0xFFFFFFFF0`, so limb 0 keeps `0xFFFFFFF0` and carries `0xF` into limb 1,
which holds `1 << 4 | 0xF` = **31**. The clear loop then zeroes limb 0 only.

This case carries `TargetIr` pins for the two x64 lanes only; on arm64 it checks the exit code.
```maxon
typealias Limb = int(i64.min to i64.max)
typealias Limbs = Array with Limb

function keep(x Limb) returns Limb
	if x < 0 'never'
		print("")
	end 'never'
	return x
end 'keep'

function shift(limbs Limbs, s Limb, n Limb)
	var carry = 0
	var i = 0
	while i < limbs.count() 'shiftEach'
		let widened = (keep(try limbs.get(i) otherwise panic("get")) shl s) or carry
		try limbs.set(i, value: widened and 0xFFFFFFFF) otherwise panic("set")
		carry = widened shr 32
		i = i + 1
	end 'shiftEach'

	var v = 0
	while v < n 'clear'
		try limbs.set(v, value: 0) otherwise panic("clear")
		v = v + 1
	end 'clear'
end 'shift'

function main() returns ExitCode
	var limbs = Limbs.create()
	limbs.push(0xFFFFFFFF)
	limbs.push(1)
	shift(limbs, s: 4, n: 1)
	return try limbs.get(1) otherwise 99
end 'main'
```
```exitcode
31
```

```TargetIr:x64-windows
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 64
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 4294967295
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
  __il_body#12:
    x64.movRegImm32 r12, 0
    x64.movRegImm32 r13, 0
    x64.jmp whilehdr#13
  __rc_ok#27:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r13, rax
    x64.jcc aboveEqual, __im_slow#34
  __im_load#35:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 r14, [rax + r13*8 + 0]
  __il_body#49:
    x64.cmpRegImm32 r14, 0
    x64.jcc greaterEqual, shiftok
  never:
    x64.leaRegRdata rcx, [rip + __str_rec_1]  ; ""
  __il_body#53:
    x64.callDirect __write_stdout
  shiftok:
    x64.shlRegImm8 r14, r14, 4
    x64.orRegReg r14, r14, r12
    x64.movRegImm32 rax, 4294967295
    x64.movRegReg rsi, r14
    x64.andRegReg rsi, r14, rax
  __rc_ok#29:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow#37
  __im_bounds#38:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r13, rax
    x64.jcc aboveEqual, __im_slow#37
  __im_buffer#39:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#37
  __im_viewed#40:
    x64.leaRegRegImm32 rcx, rax, -24
    x64.loadRegBaseDisp.word64 rcx, [rcx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __im_slow#37
  __im_store#41:
    x64.storeBaseIndexScaleReg.word64 [rax + r13*8 + 0], rsi
  tryok#20:
    x64.sarRegImm8 r14, r14, 32
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg r12, r14
  whilehdr#13:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r13, rax
    x64.jcc less, __rc_ok#27
  whileexit:
    x64.movRegImm32 rdx, 0
  __us_test#64:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, whilehdr#22
  __us_test#65:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, whilehdr#22
  __us_test#66:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, whilehdr#22
    x64.jmp whilehdr#54
  clear#23:
    x64.movRegImm32 rax, 0
  __rc_ok#31:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#43
  __im_bounds#44:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow#43
  __im_buffer#45:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#43
  __im_viewed#46:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#43
  __im_store#47:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  tryok#25:
    x64.leaRegRegImm32 rdx, rdx, 1
  whilehdr#22:
    x64.cmpRegImm32 rdx, 1
    x64.jcc less, clear#23
    x64.jmp __il_cont
  clear#63:
    x64.movRegImm32 rsi, 0
  __im_bounds#61:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#57
  __im_store#58:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rsi
  tryok#55:
    x64.leaRegRegImm32 rdx, rdx, 1
  whilehdr#54:
    x64.cmpRegImm32 rdx, 1
    x64.jcc less, clear#63
  __il_cont:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow#9
    x64.jmp __im_load#10
  tryerr#2:
    x64.movRegImm32 r12, 99
    x64.jmp trycont
  __im_load#10:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + rdx*8 + 0]
    x64.movRegReg r8, rax
  tryok#1:
    x64.movRegReg r12, r8
  trycont:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic
  __rc_ok#5:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#34:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr#17:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at reuse-def-confined-by-a-later-call.test:16: get\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  critsplit:
    x64.movRegReg r14, r8
    x64.jmp __il_body#49
  __im_slow#37:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rsi
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#20
  tryerr#21:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at reuse-def-confined-by-a-later-call.test:17: set\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#43:
    x64.storeSlotReg slot0, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_set
    x64.loadRegSlot rdx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#26
    x64.jmp tryok#25
  __im_slow#57:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot2, rcx
    x64.storeSlotReg slot0, rdx
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, rsi
    x64.callDirect __managed_set
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot2
    x64.loadRegSlot rdx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#55
  tryerr#26:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at reuse-def-confined-by-a-later-call.test:24: clear\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#9:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
    x64.jmp tryok#1
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_10]  ; "panic at reuse-def-confined-by-a-later-call.test:34: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
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
    x64.prologue 64
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 4294967295
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
  __il_body#12:
    x64.movRegImm32 r12, 0
    x64.movRegImm32 r13, 0
    x64.jmp whilehdr#13
  __rc_ok#27:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r13, rax
    x64.jcc aboveEqual, __im_slow#34
  __im_load#35:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 r14, [rax + r13*8 + 0]
  __il_body#49:
    x64.cmpRegImm32 r14, 0
    x64.jcc greaterEqual, shiftok
  never:
    x64.leaRegRdata rcx, [rip + __str_rec_1]  ; ""
  __il_body#53:
    x64.callDirect __write_stdout
  shiftok:
    x64.shlRegImm8 r14, r14, 4
    x64.orRegReg r14, r14, r12
    x64.movRegImm32 rax, 4294967295
    x64.movRegReg rsi, r14
    x64.andRegReg rsi, r14, rax
  __rc_ok#29:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow#37
  __im_bounds#38:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r13, rax
    x64.jcc aboveEqual, __im_slow#37
  __im_buffer#39:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#37
  __im_viewed#40:
    x64.leaRegRegImm32 rcx, rax, -24
    x64.loadRegBaseDisp.word64 rcx, [rcx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __im_slow#37
  __im_store#41:
    x64.storeBaseIndexScaleReg.word64 [rax + r13*8 + 0], rsi
  tryok#20:
    x64.sarRegImm8 r14, r14, 32
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg r12, r14
  whilehdr#13:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r13, rax
    x64.jcc less, __rc_ok#27
  whileexit:
    x64.movRegImm32 rdx, 0
  __us_test#64:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, whilehdr#22
  __us_test#65:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, whilehdr#22
  __us_test#66:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, whilehdr#22
    x64.jmp whilehdr#54
  clear#23:
    x64.movRegImm32 rax, 0
  __rc_ok#31:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#43
  __im_bounds#44:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow#43
  __im_buffer#45:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#43
  __im_viewed#46:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#43
  __im_store#47:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  tryok#25:
    x64.leaRegRegImm32 rdx, rdx, 1
  whilehdr#22:
    x64.cmpRegImm32 rdx, 1
    x64.jcc less, clear#23
    x64.jmp __il_cont
  clear#63:
    x64.movRegImm32 rsi, 0
  __im_bounds#61:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#57
  __im_store#58:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rsi
  tryok#55:
    x64.leaRegRegImm32 rdx, rdx, 1
  whilehdr#54:
    x64.cmpRegImm32 rdx, 1
    x64.jcc less, clear#63
  __il_cont:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow#9
    x64.jmp __im_load#10
  tryerr#2:
    x64.movRegImm32 r12, 99
    x64.jmp trycont
  __im_load#10:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + rdx*8 + 0]
    x64.movRegReg r8, rax
  tryok#1:
    x64.movRegReg r12, r8
  trycont:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok#5:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#34:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr#17:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at reuse-def-confined-by-a-later-call.test:16: get\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  critsplit:
    x64.movRegReg r14, r8
    x64.jmp __il_body#49
  __im_slow#37:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rsi
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#20
  tryerr#21:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at reuse-def-confined-by-a-later-call.test:17: set\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#43:
    x64.storeSlotReg slot0, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_set
    x64.loadRegSlot rdx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#26
    x64.jmp tryok#25
  __im_slow#57:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot2, rcx
    x64.storeSlotReg slot0, rdx
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, rsi
    x64.callDirect __managed_set
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot2
    x64.loadRegSlot rdx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#55
  tryerr#26:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at reuse-def-confined-by-a-later-call.test:24: clear\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#9:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
    x64.jmp tryok#1
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_10]  ; "panic at reuse-def-confined-by-a-later-call.test:34: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
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
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 4294967295
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
  __il_body#12:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b whilehdr#13
  __rc_ok#27:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#34
  __im_load#35:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseIndexScale.word64 x22, [x0 + x21*8 + 0]
  __il_body#49:
    arm64.cmp x22, 0
    arm64.b.ge shiftok
  never:
    arm64.leaRdata x0, __str_rec_1  ; ""
  __il_body#53:
    arm64.bl __write_stdout
  shiftok:
    arm64.lsl x0, x22, 4
    arm64.orr x0, x0, x20
    arm64.movImm x1, 4294967295
    arm64.and x2, x0, x1
  __rc_ok#29:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x1, 0
    arm64.b.lt __im_slow#37
  __im_bounds#38:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 8]
    arm64.cmp x21, x1
    arm64.b.hs __im_slow#37
  __im_buffer#39:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_slow#37
  __im_viewed#40:
    arm64.sub x3, x1, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#37
  __im_store#41:
    arm64.storeBaseIndexScaleReg.word64 [x1 + x21*8 + 0], x2
  tryok#20:
    arm64.asr x0, x0, 32
    arm64.add x21, x21, 1
    arm64.movRegReg x20, x0
  whilehdr#13:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x21, x0
    arm64.b.lt __rc_ok#27
  whileexit:
    arm64.movImm x1, 0
  __us_test#64:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt whilehdr#22
  __us_test#65:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq whilehdr#22
  __us_test#66:
    arm64.sub x3, x2, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne whilehdr#22
    arm64.b whilehdr#54
  clear#23:
    arm64.movImm x2, 0
  __rc_ok#31:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#43
  __im_bounds#44:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#43
  __im_buffer#45:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#43
  __im_viewed#46:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#43
  __im_store#47:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  tryok#25:
    arm64.add x1, x1, 1
  whilehdr#22:
    arm64.cmp x1, 1
    arm64.b.lt clear#23
    arm64.b __il_cont
  clear#63:
    arm64.movImm x3, 0
  __im_bounds#61:
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#57
  __im_store#58:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x3
  tryok#55:
    arm64.add x1, x1, 1
  whilehdr#54:
    arm64.cmp x1, 1
    arm64.b.lt clear#63
  __il_cont:
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow#9
    arm64.b __im_load#10
  tryerr#2:
    arm64.movImm x20, 99
    arm64.b trycont
  __im_load#10:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
  tryok#1:
    arm64.movRegReg x20, x0
  trycont:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok#5:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_slow#34:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq critsplit
  tryerr#17:
    arm64.leaRdata x0, __str_blob_2  ; "panic at reuse-def-confined-by-a-later-call.test:16: get\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  critsplit:
    arm64.movRegReg x22, x0
    arm64.b __il_body#49
  __im_slow#37:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.eq tryok#20
  tryerr#21:
    arm64.leaRdata x0, __str_blob_4  ; "panic at reuse-def-confined-by-a-later-call.test:17: set\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_slow#43:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x19
    arm64.bl __managed_set
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#26
    arm64.b tryok#25
  __im_slow#57:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x3
    arm64.bl __managed_set
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.eq tryok#55
  tryerr#26:
    arm64.leaRdata x0, __str_blob_5  ; "panic at reuse-def-confined-by-a-later-call.test:24: clear\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_slow#9:
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
    arm64.b tryok#1
  __rc_panic:
    arm64.leaRdata x0, __str_blob_10  ; "panic at reuse-def-confined-by-a-later-call.test:34: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
}
```

```TargetIr:arm64-linux
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
    arm64.prologue 80
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 4294967295
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
  __il_body#12:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b whilehdr#13
  __rc_ok#27:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#34
  __im_load#35:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseIndexScale.word64 x22, [x0 + x21*8 + 0]
  __il_body#49:
    arm64.cmp x22, 0
    arm64.b.ge shiftok
  never:
    arm64.leaRdata x0, __str_rec_1  ; ""
  __il_body#53:
    arm64.bl __write_stdout
  shiftok:
    arm64.lsl x0, x22, 4
    arm64.orr x0, x0, x20
    arm64.movImm x1, 4294967295
    arm64.and x2, x0, x1
  __rc_ok#29:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x1, 0
    arm64.b.lt __im_slow#37
  __im_bounds#38:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 8]
    arm64.cmp x21, x1
    arm64.b.hs __im_slow#37
  __im_buffer#39:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_slow#37
  __im_viewed#40:
    arm64.sub x3, x1, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#37
  __im_store#41:
    arm64.storeBaseIndexScaleReg.word64 [x1 + x21*8 + 0], x2
  tryok#20:
    arm64.asr x0, x0, 32
    arm64.add x21, x21, 1
    arm64.movRegReg x20, x0
  whilehdr#13:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x21, x0
    arm64.b.lt __rc_ok#27
  whileexit:
    arm64.movImm x1, 0
  __us_test#64:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt whilehdr#22
  __us_test#65:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq whilehdr#22
  __us_test#66:
    arm64.sub x3, x2, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne whilehdr#22
    arm64.b whilehdr#54
  clear#23:
    arm64.movImm x2, 0
  __rc_ok#31:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#43
  __im_bounds#44:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#43
  __im_buffer#45:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#43
  __im_viewed#46:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#43
  __im_store#47:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  tryok#25:
    arm64.add x1, x1, 1
  whilehdr#22:
    arm64.cmp x1, 1
    arm64.b.lt clear#23
    arm64.b __il_cont
  clear#63:
    arm64.movImm x3, 0
  __im_bounds#61:
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#57
  __im_store#58:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x3
  tryok#55:
    arm64.add x1, x1, 1
  whilehdr#54:
    arm64.cmp x1, 1
    arm64.b.lt clear#63
  __il_cont:
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow#9
    arm64.b __im_load#10
  tryerr#2:
    arm64.movImm x20, 99
    arm64.b trycont
  __im_load#10:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
  tryok#1:
    arm64.movRegReg x20, x0
  trycont:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok#5:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_slow#34:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq critsplit
  tryerr#17:
    arm64.leaRdata x0, __str_blob_2  ; "panic at reuse-def-confined-by-a-later-call.test:16: get\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  critsplit:
    arm64.movRegReg x22, x0
    arm64.b __il_body#49
  __im_slow#37:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.eq tryok#20
  tryerr#21:
    arm64.leaRdata x0, __str_blob_4  ; "panic at reuse-def-confined-by-a-later-call.test:17: set\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_slow#43:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x19
    arm64.bl __managed_set
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#26
    arm64.b tryok#25
  __im_slow#57:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x3
    arm64.bl __managed_set
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.eq tryok#55
  tryerr#26:
    arm64.leaRdata x0, __str_blob_5  ; "panic at reuse-def-confined-by-a-later-call.test:24: clear\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_slow#9:
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
    arm64.b tryok#1
  __rc_panic:
    arm64.leaRdata x0, __str_blob_10  ; "panic at reuse-def-confined-by-a-later-call.test:34: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
}
```

<!-- test: confined-reuse-defs-past-the-callee-saved-half -->
⛔⛔ **THE SAME REUSE-DEF CONFINEMENT AS ABOVE, IN A PROGRAM WITH NOTHING EXOTIC IN IT, ON
BOTH ISAs.**

Twenty-six ints that cross no call, then eleven `b` values that do. Each `b` is a
two-address `imul` reuse def whose input is still live, so each holds a register of its own
at an op where it is in NO live set; all eleven are live across `sink(s)`, which confines
them to the callee-saved half of the GPR file.

The point is CONFINED to a subset, not a full-pool pigeonhole: each `b` is forbidden the
caller-saved registers, and the callee-saved ones are held. **Twenty-four and twelve, and
twenty-two and fourteen, do not create it**: adding MORE call-crossing values is not what
confines the point. The demand this shape creates is not one any popcount over the full pool
can see; `opConfinedTransient` counts it, and a value the greedy colorer still cannot place is
split and re-coloured by `colorRepairingConfinements`.

`n = 4`, so `a0`..`a25` are `5`..`30` summing to `455`; `c = 456`; `b0`..`b10` are `10`,
`12`, … `30`, summing to `220`. Total `676`.
```maxon
typealias Integer = int(i64.min to i64.max)

function sink(x Integer) returns Integer
	return x + 1
end 'sink'

function main() returns ExitCode
	let n = sink(3)
	let a0 = n + 1
	let a1 = n + 2
	let a2 = n + 3
	let a3 = n + 4
	let a4 = n + 5
	let a5 = n + 6
	let a6 = n + 7
	let a7 = n + 8
	let a8 = n + 9
	let a9 = n + 10
	let a10 = n + 11
	let a11 = n + 12
	let a12 = n + 13
	let a13 = n + 14
	let a14 = n + 15
	let a15 = n + 16
	let a16 = n + 17
	let a17 = n + 18
	let a18 = n + 19
	let a19 = n + 20
	let a20 = n + 21
	let a21 = n + 22
	let a22 = n + 23
	let a23 = n + 24
	let a24 = n + 25
	let a25 = n + 26
	let b0 = a0 * 2
	let b1 = a1 * 2
	let b2 = a2 * 2
	let b3 = a3 * 2
	let b4 = a4 * 2
	let b5 = a5 * 2
	let b6 = a6 * 2
	let b7 = a7 * 2
	let b8 = a8 * 2
	let b9 = a9 * 2
	let b10 = a10 * 2
	let s = a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22 + a23 + a24 + a25
	let c = sink(s)
	let total = b0 + b1 + b2 + b3 + b4 + b5 + b6 + b7 + b8 + b9 + b10 + c
	return 0 if total == 676 else 99
end 'main'
```
```exitcode
0
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
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
    x64.prologue 192
  __il_body#9:
    x64.movRegImm32 rax, 4
  __il_cont#8:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.leaRegRegImm32 rdx, rax, 2
    x64.leaRegRegImm32 rsi, rax, 3
    x64.leaRegRegImm32 rdi, rax, 4
    x64.leaRegRegImm32 r8, rax, 5
    x64.leaRegRegImm32 r9, rax, 6
    x64.leaRegRegImm32 r10, rax, 7
    x64.leaRegRegImm32 r11, rax, 8
    x64.leaRegRegImm32 rbx, rax, 9
    x64.leaRegRegImm32 r12, rax, 10
    x64.leaRegRegImm32 r13, rax, 11
    x64.leaRegRegImm32 r14, rax, 12
    x64.leaRegRegImm32 r15, rax, 13
    x64.storeSlotReg slot0, r15
    x64.leaRegRegImm32 r15, rax, 14
    x64.storeSlotReg slot1, r15
    x64.leaRegRegImm32 r15, rax, 15
    x64.storeSlotReg slot2, r15
    x64.leaRegRegImm32 r15, rax, 16
    x64.storeSlotReg slot3, r15
    x64.leaRegRegImm32 r15, rax, 17
    x64.storeSlotReg slot4, r15
    x64.leaRegRegImm32 r15, rax, 18
    x64.storeSlotReg slot5, r15
    x64.leaRegRegImm32 r15, rax, 19
    x64.storeSlotReg slot6, r15
    x64.leaRegRegImm32 r15, rax, 20
    x64.storeSlotReg slot7, r15
    x64.leaRegRegImm32 r15, rax, 21
    x64.storeSlotReg slot8, r15
    x64.leaRegRegImm32 r15, rax, 22
    x64.storeSlotReg slot9, r15
    x64.leaRegRegImm32 r15, rax, 23
    x64.storeSlotReg slot10, r15
    x64.leaRegRegImm32 r15, rax, 24
    x64.storeSlotReg slot11, r15
    x64.leaRegRegImm32 r15, rax, 25
    x64.leaRegRegImm32 rax, rax, 26
    x64.storeSlotReg slot12, rax
    x64.imulRegRegImm32 rax, rcx, 2
    x64.storeSlotReg slot13, rax
    x64.imulRegRegImm32 rax, rdx, 2
    x64.storeSlotReg slot14, rax
    x64.imulRegRegImm32 rax, rsi, 2
    x64.storeSlotReg slot15, rax
    x64.imulRegRegImm32 rax, rdi, 2
    x64.storeSlotReg slot16, rax
    x64.imulRegRegImm32 rax, r8, 2
    x64.storeSlotReg slot17, rax
    x64.imulRegRegImm32 rax, r9, 2
    x64.storeSlotReg slot18, rax
    x64.imulRegRegImm32 rax, r10, 2
    x64.storeSlotReg slot19, rax
    x64.imulRegRegImm32 rax, r11, 2
    x64.storeSlotReg slot20, rax
    x64.imulRegRegImm32 rax, rbx, 2
    x64.storeSlotReg slot21, rax
    x64.imulRegRegImm32 rax, r12, 2
    x64.storeSlotReg slot22, rax
    x64.imulRegRegImm32 rax, r13, 2
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rcx, rcx, rsi
    x64.leaRegRegReg rcx, rcx, rdi
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rcx, rcx, r9
    x64.leaRegRegReg rcx, rcx, r10
    x64.leaRegRegReg rcx, rcx, r11
    x64.leaRegRegReg rcx, rcx, rbx
    x64.leaRegRegReg rcx, rcx, r12
    x64.leaRegRegReg rcx, rcx, r13
    x64.leaRegRegReg rcx, rcx, r14
    x64.loadRegSlot rdx, slot0
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot1
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot2
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot3
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot4
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot5
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot6
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot7
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot8
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot9
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot10
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot11
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rcx, rcx, r15
    x64.loadRegSlot rdx, slot12
    x64.leaRegRegReg rcx, rcx, rdx
  __il_body#10:
    x64.leaRegRegImm32 rcx, rcx, 1
  __il_cont#7:
    x64.loadRegSlot rdx, slot13
    x64.loadRegSlot rsi, slot14
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot15
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot16
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot17
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot18
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot19
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot20
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot21
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot22
    x64.leaRegRegReg rdx, rdx, rsi
    x64.leaRegRegReg rax, rdx, rax
    x64.leaRegRegReg rax, rax, rcx
  ternarytrue:
    x64.movRegImm32 r8, 0
  __rc_ok:
    x64.epilogue 192
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
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
    x64.prologue 192
  __il_body#9:
    x64.movRegImm32 rax, 4
  __il_cont#8:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.leaRegRegImm32 rdx, rax, 2
    x64.leaRegRegImm32 rsi, rax, 3
    x64.leaRegRegImm32 rdi, rax, 4
    x64.leaRegRegImm32 r8, rax, 5
    x64.leaRegRegImm32 r9, rax, 6
    x64.leaRegRegImm32 r10, rax, 7
    x64.leaRegRegImm32 r11, rax, 8
    x64.leaRegRegImm32 rbx, rax, 9
    x64.leaRegRegImm32 r12, rax, 10
    x64.leaRegRegImm32 r13, rax, 11
    x64.leaRegRegImm32 r14, rax, 12
    x64.leaRegRegImm32 r15, rax, 13
    x64.storeSlotReg slot0, r15
    x64.leaRegRegImm32 r15, rax, 14
    x64.storeSlotReg slot1, r15
    x64.leaRegRegImm32 r15, rax, 15
    x64.storeSlotReg slot2, r15
    x64.leaRegRegImm32 r15, rax, 16
    x64.storeSlotReg slot3, r15
    x64.leaRegRegImm32 r15, rax, 17
    x64.storeSlotReg slot4, r15
    x64.leaRegRegImm32 r15, rax, 18
    x64.storeSlotReg slot5, r15
    x64.leaRegRegImm32 r15, rax, 19
    x64.storeSlotReg slot6, r15
    x64.leaRegRegImm32 r15, rax, 20
    x64.storeSlotReg slot7, r15
    x64.leaRegRegImm32 r15, rax, 21
    x64.storeSlotReg slot8, r15
    x64.leaRegRegImm32 r15, rax, 22
    x64.storeSlotReg slot9, r15
    x64.leaRegRegImm32 r15, rax, 23
    x64.storeSlotReg slot10, r15
    x64.leaRegRegImm32 r15, rax, 24
    x64.storeSlotReg slot11, r15
    x64.leaRegRegImm32 r15, rax, 25
    x64.leaRegRegImm32 rax, rax, 26
    x64.storeSlotReg slot12, rax
    x64.imulRegRegImm32 rax, rcx, 2
    x64.storeSlotReg slot13, rax
    x64.imulRegRegImm32 rax, rdx, 2
    x64.storeSlotReg slot14, rax
    x64.imulRegRegImm32 rax, rsi, 2
    x64.storeSlotReg slot15, rax
    x64.imulRegRegImm32 rax, rdi, 2
    x64.storeSlotReg slot16, rax
    x64.imulRegRegImm32 rax, r8, 2
    x64.storeSlotReg slot17, rax
    x64.imulRegRegImm32 rax, r9, 2
    x64.storeSlotReg slot18, rax
    x64.imulRegRegImm32 rax, r10, 2
    x64.storeSlotReg slot19, rax
    x64.imulRegRegImm32 rax, r11, 2
    x64.storeSlotReg slot20, rax
    x64.imulRegRegImm32 rax, rbx, 2
    x64.storeSlotReg slot21, rax
    x64.imulRegRegImm32 rax, r12, 2
    x64.storeSlotReg slot22, rax
    x64.imulRegRegImm32 rax, r13, 2
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rcx, rcx, rsi
    x64.leaRegRegReg rcx, rcx, rdi
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rcx, rcx, r9
    x64.leaRegRegReg rcx, rcx, r10
    x64.leaRegRegReg rcx, rcx, r11
    x64.leaRegRegReg rcx, rcx, rbx
    x64.leaRegRegReg rcx, rcx, r12
    x64.leaRegRegReg rcx, rcx, r13
    x64.leaRegRegReg rcx, rcx, r14
    x64.loadRegSlot rdx, slot0
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot1
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot2
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot3
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot4
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot5
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot6
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot7
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot8
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot9
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot10
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegSlot rdx, slot11
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rcx, rcx, r15
    x64.loadRegSlot rdx, slot12
    x64.leaRegRegReg rcx, rcx, rdx
  __il_body#10:
    x64.leaRegRegImm32 rcx, rcx, 1
  __il_cont#7:
    x64.loadRegSlot rdx, slot13
    x64.loadRegSlot rsi, slot14
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot15
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot16
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot17
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot18
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot19
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot20
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot21
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot22
    x64.leaRegRegReg rdx, rdx, rsi
    x64.leaRegRegReg rax, rdx, rax
    x64.leaRegRegReg rax, rax, rcx
  ternarytrue:
    x64.movRegImm32 r8, 0
  __rc_ok:
    x64.epilogue 192
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
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 192
    arm64.storeSlotReg slot21, x28
    arm64.storeSlotReg slot20, x27
    arm64.storeSlotReg slot19, x26
    arm64.storeSlotReg slot18, x25
    arm64.storeSlotReg slot17, x24
    arm64.storeSlotReg slot16, x23
    arm64.storeSlotReg slot15, x22
    arm64.storeSlotReg slot14, x21
    arm64.storeSlotReg slot13, x20
    arm64.storeSlotReg slot12, x19
  __il_body#9:
    arm64.movImm x0, 4
  __il_cont#8:
    arm64.add x1, x0, 1
    arm64.add x2, x0, 2
    arm64.add x3, x0, 3
    arm64.add x4, x0, 4
    arm64.add x5, x0, 5
    arm64.add x6, x0, 6
    arm64.add x7, x0, 7
    arm64.add x8, x0, 8
    arm64.add x9, x0, 9
    arm64.add x10, x0, 10
    arm64.add x11, x0, 11
    arm64.add x12, x0, 12
    arm64.add x13, x0, 13
    arm64.add x14, x0, 14
    arm64.add x19, x0, 15
    arm64.add x20, x0, 16
    arm64.add x21, x0, 17
    arm64.add x22, x0, 18
    arm64.add x23, x0, 19
    arm64.add x24, x0, 20
    arm64.add x25, x0, 21
    arm64.add x26, x0, 22
    arm64.add x27, x0, 23
    arm64.add x28, x0, 24
    arm64.storeSlotReg slot0, x28
    arm64.add x28, x0, 25
    arm64.add x0, x0, 26
    arm64.storeSlotReg slot1, x0
    arm64.lsl x0, x1, 1
    arm64.storeSlotReg slot2, x0
    arm64.lsl x0, x2, 1
    arm64.storeSlotReg slot3, x0
    arm64.lsl x0, x3, 1
    arm64.storeSlotReg slot4, x0
    arm64.lsl x0, x4, 1
    arm64.storeSlotReg slot5, x0
    arm64.lsl x0, x5, 1
    arm64.storeSlotReg slot6, x0
    arm64.lsl x0, x6, 1
    arm64.storeSlotReg slot7, x0
    arm64.lsl x0, x7, 1
    arm64.storeSlotReg slot8, x0
    arm64.lsl x0, x8, 1
    arm64.storeSlotReg slot9, x0
    arm64.lsl x0, x9, 1
    arm64.storeSlotReg slot10, x0
    arm64.lsl x0, x10, 1
    arm64.storeSlotReg slot11, x0
    arm64.lsl x0, x11, 1
    arm64.add x1, x1, x2
    arm64.add x1, x1, x3
    arm64.add x1, x1, x4
    arm64.add x1, x1, x5
    arm64.add x1, x1, x6
    arm64.add x1, x1, x7
    arm64.add x1, x1, x8
    arm64.add x1, x1, x9
    arm64.add x1, x1, x10
    arm64.add x1, x1, x11
    arm64.add x1, x1, x12
    arm64.add x1, x1, x13
    arm64.add x1, x1, x14
    arm64.add x1, x1, x19
    arm64.add x1, x1, x20
    arm64.add x1, x1, x21
    arm64.add x1, x1, x22
    arm64.add x1, x1, x23
    arm64.add x1, x1, x24
    arm64.add x1, x1, x25
    arm64.add x1, x1, x26
    arm64.add x1, x1, x27
    arm64.loadRegSlot x2, slot0
    arm64.add x1, x1, x2
    arm64.add x1, x1, x28
    arm64.loadRegSlot x2, slot1
    arm64.add x1, x1, x2
  __il_body#10:
    arm64.add x1, x1, 1
  __il_cont#7:
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot4
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot5
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot6
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot7
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot8
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot9
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot10
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot11
    arm64.add x2, x2, x3
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
  ternarytrue:
    arm64.movImm x0, 0
  __rc_ok:
    arm64.loadRegSlot x19, slot12
    arm64.loadRegSlot x20, slot13
    arm64.loadRegSlot x21, slot14
    arm64.loadRegSlot x22, slot15
    arm64.loadRegSlot x23, slot16
    arm64.loadRegSlot x24, slot17
    arm64.loadRegSlot x25, slot18
    arm64.loadRegSlot x26, slot19
    arm64.loadRegSlot x27, slot20
    arm64.loadRegSlot x28, slot21
    arm64.epilogue 192
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 192
    arm64.storeSlotReg slot21, x28
    arm64.storeSlotReg slot20, x27
    arm64.storeSlotReg slot19, x26
    arm64.storeSlotReg slot18, x25
    arm64.storeSlotReg slot17, x24
    arm64.storeSlotReg slot16, x23
    arm64.storeSlotReg slot15, x22
    arm64.storeSlotReg slot14, x21
    arm64.storeSlotReg slot13, x20
    arm64.storeSlotReg slot12, x19
  __il_body#9:
    arm64.movImm x0, 4
  __il_cont#8:
    arm64.add x1, x0, 1
    arm64.add x2, x0, 2
    arm64.add x3, x0, 3
    arm64.add x4, x0, 4
    arm64.add x5, x0, 5
    arm64.add x6, x0, 6
    arm64.add x7, x0, 7
    arm64.add x8, x0, 8
    arm64.add x9, x0, 9
    arm64.add x10, x0, 10
    arm64.add x11, x0, 11
    arm64.add x12, x0, 12
    arm64.add x13, x0, 13
    arm64.add x14, x0, 14
    arm64.add x19, x0, 15
    arm64.add x20, x0, 16
    arm64.add x21, x0, 17
    arm64.add x22, x0, 18
    arm64.add x23, x0, 19
    arm64.add x24, x0, 20
    arm64.add x25, x0, 21
    arm64.add x26, x0, 22
    arm64.add x27, x0, 23
    arm64.add x28, x0, 24
    arm64.storeSlotReg slot0, x28
    arm64.add x28, x0, 25
    arm64.add x0, x0, 26
    arm64.storeSlotReg slot1, x0
    arm64.lsl x0, x1, 1
    arm64.storeSlotReg slot2, x0
    arm64.lsl x0, x2, 1
    arm64.storeSlotReg slot3, x0
    arm64.lsl x0, x3, 1
    arm64.storeSlotReg slot4, x0
    arm64.lsl x0, x4, 1
    arm64.storeSlotReg slot5, x0
    arm64.lsl x0, x5, 1
    arm64.storeSlotReg slot6, x0
    arm64.lsl x0, x6, 1
    arm64.storeSlotReg slot7, x0
    arm64.lsl x0, x7, 1
    arm64.storeSlotReg slot8, x0
    arm64.lsl x0, x8, 1
    arm64.storeSlotReg slot9, x0
    arm64.lsl x0, x9, 1
    arm64.storeSlotReg slot10, x0
    arm64.lsl x0, x10, 1
    arm64.storeSlotReg slot11, x0
    arm64.lsl x0, x11, 1
    arm64.add x1, x1, x2
    arm64.add x1, x1, x3
    arm64.add x1, x1, x4
    arm64.add x1, x1, x5
    arm64.add x1, x1, x6
    arm64.add x1, x1, x7
    arm64.add x1, x1, x8
    arm64.add x1, x1, x9
    arm64.add x1, x1, x10
    arm64.add x1, x1, x11
    arm64.add x1, x1, x12
    arm64.add x1, x1, x13
    arm64.add x1, x1, x14
    arm64.add x1, x1, x19
    arm64.add x1, x1, x20
    arm64.add x1, x1, x21
    arm64.add x1, x1, x22
    arm64.add x1, x1, x23
    arm64.add x1, x1, x24
    arm64.add x1, x1, x25
    arm64.add x1, x1, x26
    arm64.add x1, x1, x27
    arm64.loadRegSlot x2, slot0
    arm64.add x1, x1, x2
    arm64.add x1, x1, x28
    arm64.loadRegSlot x2, slot1
    arm64.add x1, x1, x2
  __il_body#10:
    arm64.add x1, x1, 1
  __il_cont#7:
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot4
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot5
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot6
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot7
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot8
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot9
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot10
    arm64.add x2, x2, x3
    arm64.loadRegSlot x3, slot11
    arm64.add x2, x2, x3
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
  ternarytrue:
    arm64.movImm x0, 0
  __rc_ok:
    arm64.loadRegSlot x19, slot12
    arm64.loadRegSlot x20, slot13
    arm64.loadRegSlot x21, slot14
    arm64.loadRegSlot x22, slot15
    arm64.loadRegSlot x23, slot16
    arm64.loadRegSlot x24, slot17
    arm64.loadRegSlot x25, slot18
    arm64.loadRegSlot x26, slot19
    arm64.loadRegSlot x27, slot20
    arm64.loadRegSlot x28, slot21
    arm64.epilogue 192
    arm64.ret
}
```

<!-- test: confined-reuse-defs-past-the-callee-saved-half-float -->
**THE SIMD TWIN, AND IT IS THE SAME CONFINEMENT AND NOT A FLOAT ONE.** Thirty-two floats that
cross no call and eight that do — `mulsd` / `fmul` reuse defs, live across `fsink(s)`, confined
to the callee-saved vector registers. It is relieved the same way on both ISAs.

The pair exists because the two register files reach the same wall at different counts, and
a fix that reads one file's pool size is a fix only one file ever tests.

`n = 4.0`, so `a0`..`a31` are `5.0`..`36.0` summing to `656.0`; `c = 657.0`; `b0`..`b7` are
`10.0`, `12.0`, … `24.0`, summing to `136.0`. Total `793.0`.
```maxon
function fsink(x Real) returns Real
	return x + 1.0
end 'fsink'

function main() returns ExitCode
	let n = fsink(3.0)
	let a0 = n + 1.0
	let a1 = n + 2.0
	let a2 = n + 3.0
	let a3 = n + 4.0
	let a4 = n + 5.0
	let a5 = n + 6.0
	let a6 = n + 7.0
	let a7 = n + 8.0
	let a8 = n + 9.0
	let a9 = n + 10.0
	let a10 = n + 11.0
	let a11 = n + 12.0
	let a12 = n + 13.0
	let a13 = n + 14.0
	let a14 = n + 15.0
	let a15 = n + 16.0
	let a16 = n + 17.0
	let a17 = n + 18.0
	let a18 = n + 19.0
	let a19 = n + 20.0
	let a20 = n + 21.0
	let a21 = n + 22.0
	let a22 = n + 23.0
	let a23 = n + 24.0
	let a24 = n + 25.0
	let a25 = n + 26.0
	let a26 = n + 27.0
	let a27 = n + 28.0
	let a28 = n + 29.0
	let a29 = n + 30.0
	let a30 = n + 31.0
	let a31 = n + 32.0
	let b0 = a0 * 2.0
	let b1 = a1 * 2.0
	let b2 = a2 * 2.0
	let b3 = a3 * 2.0
	let b4 = a4 * 2.0
	let b5 = a5 * 2.0
	let b6 = a6 * 2.0
	let b7 = a7 * 2.0
	let s = a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22 + a23 + a24 + a25 + a26 + a27 + a28 + a29 + a30 + a31
	let c = fsink(s)
	let total = b0 + b1 + b2 + b3 + b4 + b5 + b6 + b7 + c
	return 0 if total == 793.0 else 99
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
0
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 288
    x64.storeSlotReg slot25, xmm6
    x64.storeSlotReg slot26, xmm7
    x64.storeSlotReg slot27, xmm8
    x64.storeSlotReg slot28, xmm9
    x64.storeSlotReg slot29, xmm10
    x64.storeSlotReg slot30, xmm11
    x64.storeSlotReg slot31, xmm12
    x64.storeSlotReg slot32, xmm13
    x64.storeSlotReg slot33, xmm14
    x64.storeSlotReg slot34, xmm15
  __il_body#9:
    x64.movsdRegRip xmm0, [rip + __fconst_4]
  __il_cont#8:
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.movRegReg xmm2, xmm0
    x64.addsdRegReg xmm2, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_2]
    x64.movRegReg xmm3, xmm0
    x64.addsdRegReg xmm3, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.movRegReg xmm4, xmm0
    x64.addsdRegReg xmm4, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_4]
    x64.movRegReg xmm5, xmm0
    x64.addsdRegReg xmm5, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_5]
    x64.movRegReg xmm6, xmm0
    x64.addsdRegReg xmm6, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_6]
    x64.movRegReg xmm7, xmm0
    x64.addsdRegReg xmm7, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_7]
    x64.movRegReg xmm8, xmm0
    x64.addsdRegReg xmm8, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_8]
    x64.movRegReg xmm9, xmm0
    x64.addsdRegReg xmm9, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_9]
    x64.movRegReg xmm10, xmm0
    x64.addsdRegReg xmm10, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_10]
    x64.movRegReg xmm11, xmm0
    x64.addsdRegReg xmm11, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_11]
    x64.movRegReg xmm12, xmm0
    x64.addsdRegReg xmm12, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_12]
    x64.movRegReg xmm13, xmm0
    x64.addsdRegReg xmm13, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_13]
    x64.movRegReg xmm14, xmm0
    x64.addsdRegReg xmm14, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_14]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot24, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_15]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot0, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_16]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot1, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_17]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot2, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_18]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot3, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_19]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot4, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_20]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot5, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_21]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot6, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_22]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot7, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_23]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot8, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_24]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot9, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_25]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot10, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_26]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot11, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_27]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot12, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_28]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot13, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_29]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot14, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_30]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot15, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_31]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_32]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.storeSlotReg slot16, xmm0
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm2
    x64.mulsdRegReg xmm1, xmm2, xmm0
    x64.storeSlotReg slot17, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm3
    x64.mulsdRegReg xmm1, xmm3, xmm0
    x64.storeSlotReg slot18, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm4
    x64.mulsdRegReg xmm1, xmm4, xmm0
    x64.storeSlotReg slot19, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm5
    x64.mulsdRegReg xmm1, xmm5, xmm0
    x64.storeSlotReg slot20, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm6
    x64.mulsdRegReg xmm1, xmm6, xmm0
    x64.storeSlotReg slot21, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm7
    x64.mulsdRegReg xmm1, xmm7, xmm0
    x64.storeSlotReg slot22, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm8
    x64.mulsdRegReg xmm1, xmm8, xmm0
    x64.storeSlotReg slot23, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm9
    x64.mulsdRegReg xmm1, xmm9, xmm0
    x64.addsdRegReg xmm2, xmm2, xmm3
    x64.addsdRegReg xmm2, xmm2, xmm4
    x64.addsdRegReg xmm2, xmm2, xmm5
    x64.addsdRegReg xmm2, xmm2, xmm6
    x64.addsdRegReg xmm2, xmm2, xmm7
    x64.addsdRegReg xmm2, xmm2, xmm8
    x64.addsdRegReg xmm2, xmm2, xmm9
    x64.addsdRegReg xmm2, xmm2, xmm10
    x64.addsdRegReg xmm2, xmm2, xmm11
    x64.addsdRegReg xmm2, xmm2, xmm12
    x64.addsdRegReg xmm2, xmm2, xmm13
    x64.addsdRegReg xmm2, xmm2, xmm14
    x64.loadRegSlot xmm0, slot24
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot0
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot1
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot2
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot3
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot4
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot5
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot6
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot7
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot8
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot9
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot10
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot11
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot12
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot13
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot14
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot15
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.addsdRegReg xmm2, xmm2, xmm15
    x64.loadRegSlot xmm0, slot16
    x64.addsdRegReg xmm2, xmm2, xmm0
  __il_body#10:
    x64.movsdRegRip xmm0, [rip + __fconst_1]
    x64.addsdRegReg xmm2, xmm2, xmm0
  __il_cont#7:
    x64.loadRegSlot xmm0, slot17
    x64.loadRegSlot xmm3, slot18
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot19
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot20
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot21
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot22
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot23
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.addsdRegReg xmm0, xmm0, xmm2
    x64.movsdRegRip xmm1, [rip + __fconst_793]
    x64.ucomisdRegReg xmm0, xmm1
    x64.jcc parity, ternaryfalse
  fordered:
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp __rc_ok
  ternaryfalse:
    x64.movRegImm32 r8, 99
  __rc_ok:
    x64.loadRegSlot xmm6, slot25
    x64.loadRegSlot xmm7, slot26
    x64.loadRegSlot xmm8, slot27
    x64.loadRegSlot xmm9, slot28
    x64.loadRegSlot xmm10, slot29
    x64.loadRegSlot xmm11, slot30
    x64.loadRegSlot xmm12, slot31
    x64.loadRegSlot xmm13, slot32
    x64.loadRegSlot xmm14, slot33
    x64.loadRegSlot xmm15, slot34
    x64.epilogue 288
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 288
    x64.storeSlotReg slot25, xmm6
    x64.storeSlotReg slot26, xmm7
    x64.storeSlotReg slot27, xmm8
    x64.storeSlotReg slot28, xmm9
    x64.storeSlotReg slot29, xmm10
    x64.storeSlotReg slot30, xmm11
    x64.storeSlotReg slot31, xmm12
    x64.storeSlotReg slot32, xmm13
    x64.storeSlotReg slot33, xmm14
    x64.storeSlotReg slot34, xmm15
  __il_body#9:
    x64.movsdRegRip xmm0, [rip + __fconst_4]
  __il_cont#8:
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.movRegReg xmm2, xmm0
    x64.addsdRegReg xmm2, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_2]
    x64.movRegReg xmm3, xmm0
    x64.addsdRegReg xmm3, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.movRegReg xmm4, xmm0
    x64.addsdRegReg xmm4, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_4]
    x64.movRegReg xmm5, xmm0
    x64.addsdRegReg xmm5, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_5]
    x64.movRegReg xmm6, xmm0
    x64.addsdRegReg xmm6, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_6]
    x64.movRegReg xmm7, xmm0
    x64.addsdRegReg xmm7, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_7]
    x64.movRegReg xmm8, xmm0
    x64.addsdRegReg xmm8, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_8]
    x64.movRegReg xmm9, xmm0
    x64.addsdRegReg xmm9, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_9]
    x64.movRegReg xmm10, xmm0
    x64.addsdRegReg xmm10, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_10]
    x64.movRegReg xmm11, xmm0
    x64.addsdRegReg xmm11, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_11]
    x64.movRegReg xmm12, xmm0
    x64.addsdRegReg xmm12, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_12]
    x64.movRegReg xmm13, xmm0
    x64.addsdRegReg xmm13, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_13]
    x64.movRegReg xmm14, xmm0
    x64.addsdRegReg xmm14, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_14]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot24, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_15]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot0, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_16]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot1, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_17]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot2, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_18]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot3, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_19]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot4, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_20]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot5, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_21]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot6, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_22]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot7, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_23]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot8, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_24]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot9, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_25]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot10, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_26]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot11, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_27]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot12, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_28]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot13, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_29]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot14, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_30]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot15, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_31]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_32]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.storeSlotReg slot16, xmm0
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm2
    x64.mulsdRegReg xmm1, xmm2, xmm0
    x64.storeSlotReg slot17, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm3
    x64.mulsdRegReg xmm1, xmm3, xmm0
    x64.storeSlotReg slot18, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm4
    x64.mulsdRegReg xmm1, xmm4, xmm0
    x64.storeSlotReg slot19, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm5
    x64.mulsdRegReg xmm1, xmm5, xmm0
    x64.storeSlotReg slot20, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm6
    x64.mulsdRegReg xmm1, xmm6, xmm0
    x64.storeSlotReg slot21, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm7
    x64.mulsdRegReg xmm1, xmm7, xmm0
    x64.storeSlotReg slot22, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm8
    x64.mulsdRegReg xmm1, xmm8, xmm0
    x64.storeSlotReg slot23, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm9
    x64.mulsdRegReg xmm1, xmm9, xmm0
    x64.addsdRegReg xmm2, xmm2, xmm3
    x64.addsdRegReg xmm2, xmm2, xmm4
    x64.addsdRegReg xmm2, xmm2, xmm5
    x64.addsdRegReg xmm2, xmm2, xmm6
    x64.addsdRegReg xmm2, xmm2, xmm7
    x64.addsdRegReg xmm2, xmm2, xmm8
    x64.addsdRegReg xmm2, xmm2, xmm9
    x64.addsdRegReg xmm2, xmm2, xmm10
    x64.addsdRegReg xmm2, xmm2, xmm11
    x64.addsdRegReg xmm2, xmm2, xmm12
    x64.addsdRegReg xmm2, xmm2, xmm13
    x64.addsdRegReg xmm2, xmm2, xmm14
    x64.loadRegSlot xmm0, slot24
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot0
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot1
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot2
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot3
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot4
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot5
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot6
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot7
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot8
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot9
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot10
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot11
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot12
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot13
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot14
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.loadRegSlot xmm0, slot15
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.addsdRegReg xmm2, xmm2, xmm15
    x64.loadRegSlot xmm0, slot16
    x64.addsdRegReg xmm2, xmm2, xmm0
  __il_body#10:
    x64.movsdRegRip xmm0, [rip + __fconst_1]
    x64.addsdRegReg xmm2, xmm2, xmm0
  __il_cont#7:
    x64.loadRegSlot xmm0, slot17
    x64.loadRegSlot xmm3, slot18
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot19
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot20
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot21
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot22
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot23
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.addsdRegReg xmm0, xmm0, xmm2
    x64.movsdRegRip xmm1, [rip + __fconst_793]
    x64.ucomisdRegReg xmm0, xmm1
    x64.jcc parity, ternaryfalse
  fordered:
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp __rc_ok
  ternaryfalse:
    x64.movRegImm32 r8, 99
  __rc_ok:
    x64.loadRegSlot xmm6, slot25
    x64.loadRegSlot xmm7, slot26
    x64.loadRegSlot xmm8, slot27
    x64.loadRegSlot xmm9, slot28
    x64.loadRegSlot xmm10, slot29
    x64.loadRegSlot xmm11, slot30
    x64.loadRegSlot xmm12, slot31
    x64.loadRegSlot xmm13, slot32
    x64.loadRegSlot xmm14, slot33
    x64.loadRegSlot xmm15, slot34
    x64.epilogue 288
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 144
    arm64.storeSlotReg slot15, d15
    arm64.storeSlotReg slot14, d14
    arm64.storeSlotReg slot13, d13
    arm64.storeSlotReg slot12, d12
    arm64.storeSlotReg slot11, d11
    arm64.storeSlotReg slot10, d10
    arm64.storeSlotReg slot9, d9
    arm64.storeSlotReg slot8, d8
  __il_body#9:
    arm64.fpLoadRdata d0, [rdata + __fconst_4]
  __il_cont#8:
    arm64.fpLoadRdata d1, [rdata + __fconst_1]
    arm64.fadd d1, d0, d1
    arm64.fpLoadRdata d2, [rdata + __fconst_2]
    arm64.fadd d2, d0, d2
    arm64.fpLoadRdata d3, [rdata + __fconst_3]
    arm64.fadd d3, d0, d3
    arm64.fpLoadRdata d4, [rdata + __fconst_4]
    arm64.fadd d4, d0, d4
    arm64.fpLoadRdata d5, [rdata + __fconst_5]
    arm64.fadd d5, d0, d5
    arm64.fpLoadRdata d6, [rdata + __fconst_6]
    arm64.fadd d6, d0, d6
    arm64.fpLoadRdata d7, [rdata + __fconst_7]
    arm64.fadd d7, d0, d7
    arm64.fpLoadRdata d16, [rdata + __fconst_8]
    arm64.fadd d16, d0, d16
    arm64.fpLoadRdata d17, [rdata + __fconst_9]
    arm64.fadd d17, d0, d17
    arm64.fpLoadRdata d18, [rdata + __fconst_10]
    arm64.fadd d18, d0, d18
    arm64.fpLoadRdata d19, [rdata + __fconst_11]
    arm64.fadd d19, d0, d19
    arm64.fpLoadRdata d20, [rdata + __fconst_12]
    arm64.fadd d20, d0, d20
    arm64.fpLoadRdata d21, [rdata + __fconst_13]
    arm64.fadd d21, d0, d21
    arm64.fpLoadRdata d22, [rdata + __fconst_14]
    arm64.fadd d22, d0, d22
    arm64.fpLoadRdata d23, [rdata + __fconst_15]
    arm64.fadd d23, d0, d23
    arm64.fpLoadRdata d24, [rdata + __fconst_16]
    arm64.fadd d24, d0, d24
    arm64.fpLoadRdata d25, [rdata + __fconst_17]
    arm64.fadd d25, d0, d25
    arm64.fpLoadRdata d26, [rdata + __fconst_18]
    arm64.fadd d26, d0, d26
    arm64.fpLoadRdata d27, [rdata + __fconst_19]
    arm64.fadd d27, d0, d27
    arm64.fpLoadRdata d28, [rdata + __fconst_20]
    arm64.fadd d28, d0, d28
    arm64.fpLoadRdata d29, [rdata + __fconst_21]
    arm64.fadd d29, d0, d29
    arm64.fpLoadRdata d30, [rdata + __fconst_22]
    arm64.fadd d30, d0, d30
    arm64.fpLoadRdata d31, [rdata + __fconst_23]
    arm64.fadd d31, d0, d31
    arm64.fpLoadRdata d8, [rdata + __fconst_24]
    arm64.fadd d8, d0, d8
    arm64.fpLoadRdata d9, [rdata + __fconst_25]
    arm64.fadd d9, d0, d9
    arm64.fpLoadRdata d10, [rdata + __fconst_26]
    arm64.fadd d10, d0, d10
    arm64.fpLoadRdata d11, [rdata + __fconst_27]
    arm64.fadd d11, d0, d11
    arm64.fpLoadRdata d12, [rdata + __fconst_28]
    arm64.fadd d12, d0, d12
    arm64.fpLoadRdata d13, [rdata + __fconst_29]
    arm64.fadd d13, d0, d13
    arm64.fpLoadRdata d14, [rdata + __fconst_30]
    arm64.fadd d14, d0, d14
    arm64.fpLoadRdata d15, [rdata + __fconst_31]
    arm64.fadd d15, d0, d15
    arm64.storeSlotReg slot7, d15
    arm64.fpLoadRdata d15, [rdata + __fconst_32]
    arm64.fadd d0, d0, d15
    arm64.storeSlotReg slot0, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d1, d0
    arm64.storeSlotReg slot1, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d2, d0
    arm64.storeSlotReg slot2, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d3, d0
    arm64.storeSlotReg slot3, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d4, d0
    arm64.storeSlotReg slot4, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d5, d0
    arm64.storeSlotReg slot5, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d6, d0
    arm64.storeSlotReg slot6, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d7, d0
    arm64.fpLoadRdata d15, [rdata + __fconst_2]
    arm64.fmul d15, d16, d15
    arm64.fadd d1, d1, d2
    arm64.fadd d1, d1, d3
    arm64.fadd d1, d1, d4
    arm64.fadd d1, d1, d5
    arm64.fadd d1, d1, d6
    arm64.fadd d1, d1, d7
    arm64.fadd d1, d1, d16
    arm64.fadd d1, d1, d17
    arm64.fadd d1, d1, d18
    arm64.fadd d1, d1, d19
    arm64.fadd d1, d1, d20
    arm64.fadd d1, d1, d21
    arm64.fadd d1, d1, d22
    arm64.fadd d1, d1, d23
    arm64.fadd d1, d1, d24
    arm64.fadd d1, d1, d25
    arm64.fadd d1, d1, d26
    arm64.fadd d1, d1, d27
    arm64.fadd d1, d1, d28
    arm64.fadd d1, d1, d29
    arm64.fadd d1, d1, d30
    arm64.fadd d1, d1, d31
    arm64.fadd d1, d1, d8
    arm64.fadd d1, d1, d9
    arm64.fadd d1, d1, d10
    arm64.fadd d1, d1, d11
    arm64.fadd d1, d1, d12
    arm64.fadd d1, d1, d13
    arm64.fadd d1, d1, d14
    arm64.loadRegSlot d2, slot7
    arm64.fadd d1, d1, d2
    arm64.loadRegSlot d2, slot0
    arm64.fadd d1, d1, d2
  __il_body#10:
    arm64.fpLoadRdata d2, [rdata + __fconst_1]
    arm64.fadd d1, d1, d2
  __il_cont#7:
    arm64.loadRegSlot d2, slot1
    arm64.loadRegSlot d3, slot2
    arm64.fadd d2, d2, d3
    arm64.loadRegSlot d3, slot3
    arm64.fadd d2, d2, d3
    arm64.loadRegSlot d3, slot4
    arm64.fadd d2, d2, d3
    arm64.loadRegSlot d3, slot5
    arm64.fadd d2, d2, d3
    arm64.loadRegSlot d3, slot6
    arm64.fadd d2, d2, d3
    arm64.fadd d0, d2, d0
    arm64.fadd d0, d0, d15
    arm64.fadd d0, d0, d1
    arm64.fpLoadRdata d1, [rdata + __fconst_793]
    arm64.fcmp d0, d1
    arm64.b.ne ternaryfalse
  ternarytrue:
    arm64.movImm x0, 0
    arm64.b __rc_ok
  ternaryfalse:
    arm64.movImm x0, 99
  __rc_ok:
    arm64.loadRegSlot d8, slot8
    arm64.loadRegSlot d9, slot9
    arm64.loadRegSlot d10, slot10
    arm64.loadRegSlot d11, slot11
    arm64.loadRegSlot d12, slot12
    arm64.loadRegSlot d13, slot13
    arm64.loadRegSlot d14, slot14
    arm64.loadRegSlot d15, slot15
    arm64.epilogue 144
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 144
    arm64.storeSlotReg slot15, d15
    arm64.storeSlotReg slot14, d14
    arm64.storeSlotReg slot13, d13
    arm64.storeSlotReg slot12, d12
    arm64.storeSlotReg slot11, d11
    arm64.storeSlotReg slot10, d10
    arm64.storeSlotReg slot9, d9
    arm64.storeSlotReg slot8, d8
  __il_body#9:
    arm64.fpLoadRdata d0, [rdata + __fconst_4]
  __il_cont#8:
    arm64.fpLoadRdata d1, [rdata + __fconst_1]
    arm64.fadd d1, d0, d1
    arm64.fpLoadRdata d2, [rdata + __fconst_2]
    arm64.fadd d2, d0, d2
    arm64.fpLoadRdata d3, [rdata + __fconst_3]
    arm64.fadd d3, d0, d3
    arm64.fpLoadRdata d4, [rdata + __fconst_4]
    arm64.fadd d4, d0, d4
    arm64.fpLoadRdata d5, [rdata + __fconst_5]
    arm64.fadd d5, d0, d5
    arm64.fpLoadRdata d6, [rdata + __fconst_6]
    arm64.fadd d6, d0, d6
    arm64.fpLoadRdata d7, [rdata + __fconst_7]
    arm64.fadd d7, d0, d7
    arm64.fpLoadRdata d16, [rdata + __fconst_8]
    arm64.fadd d16, d0, d16
    arm64.fpLoadRdata d17, [rdata + __fconst_9]
    arm64.fadd d17, d0, d17
    arm64.fpLoadRdata d18, [rdata + __fconst_10]
    arm64.fadd d18, d0, d18
    arm64.fpLoadRdata d19, [rdata + __fconst_11]
    arm64.fadd d19, d0, d19
    arm64.fpLoadRdata d20, [rdata + __fconst_12]
    arm64.fadd d20, d0, d20
    arm64.fpLoadRdata d21, [rdata + __fconst_13]
    arm64.fadd d21, d0, d21
    arm64.fpLoadRdata d22, [rdata + __fconst_14]
    arm64.fadd d22, d0, d22
    arm64.fpLoadRdata d23, [rdata + __fconst_15]
    arm64.fadd d23, d0, d23
    arm64.fpLoadRdata d24, [rdata + __fconst_16]
    arm64.fadd d24, d0, d24
    arm64.fpLoadRdata d25, [rdata + __fconst_17]
    arm64.fadd d25, d0, d25
    arm64.fpLoadRdata d26, [rdata + __fconst_18]
    arm64.fadd d26, d0, d26
    arm64.fpLoadRdata d27, [rdata + __fconst_19]
    arm64.fadd d27, d0, d27
    arm64.fpLoadRdata d28, [rdata + __fconst_20]
    arm64.fadd d28, d0, d28
    arm64.fpLoadRdata d29, [rdata + __fconst_21]
    arm64.fadd d29, d0, d29
    arm64.fpLoadRdata d30, [rdata + __fconst_22]
    arm64.fadd d30, d0, d30
    arm64.fpLoadRdata d31, [rdata + __fconst_23]
    arm64.fadd d31, d0, d31
    arm64.fpLoadRdata d8, [rdata + __fconst_24]
    arm64.fadd d8, d0, d8
    arm64.fpLoadRdata d9, [rdata + __fconst_25]
    arm64.fadd d9, d0, d9
    arm64.fpLoadRdata d10, [rdata + __fconst_26]
    arm64.fadd d10, d0, d10
    arm64.fpLoadRdata d11, [rdata + __fconst_27]
    arm64.fadd d11, d0, d11
    arm64.fpLoadRdata d12, [rdata + __fconst_28]
    arm64.fadd d12, d0, d12
    arm64.fpLoadRdata d13, [rdata + __fconst_29]
    arm64.fadd d13, d0, d13
    arm64.fpLoadRdata d14, [rdata + __fconst_30]
    arm64.fadd d14, d0, d14
    arm64.fpLoadRdata d15, [rdata + __fconst_31]
    arm64.fadd d15, d0, d15
    arm64.storeSlotReg slot7, d15
    arm64.fpLoadRdata d15, [rdata + __fconst_32]
    arm64.fadd d0, d0, d15
    arm64.storeSlotReg slot0, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d1, d0
    arm64.storeSlotReg slot1, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d2, d0
    arm64.storeSlotReg slot2, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d3, d0
    arm64.storeSlotReg slot3, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d4, d0
    arm64.storeSlotReg slot4, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d5, d0
    arm64.storeSlotReg slot5, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d6, d0
    arm64.storeSlotReg slot6, d0
    arm64.fpLoadRdata d0, [rdata + __fconst_2]
    arm64.fmul d0, d7, d0
    arm64.fpLoadRdata d15, [rdata + __fconst_2]
    arm64.fmul d15, d16, d15
    arm64.fadd d1, d1, d2
    arm64.fadd d1, d1, d3
    arm64.fadd d1, d1, d4
    arm64.fadd d1, d1, d5
    arm64.fadd d1, d1, d6
    arm64.fadd d1, d1, d7
    arm64.fadd d1, d1, d16
    arm64.fadd d1, d1, d17
    arm64.fadd d1, d1, d18
    arm64.fadd d1, d1, d19
    arm64.fadd d1, d1, d20
    arm64.fadd d1, d1, d21
    arm64.fadd d1, d1, d22
    arm64.fadd d1, d1, d23
    arm64.fadd d1, d1, d24
    arm64.fadd d1, d1, d25
    arm64.fadd d1, d1, d26
    arm64.fadd d1, d1, d27
    arm64.fadd d1, d1, d28
    arm64.fadd d1, d1, d29
    arm64.fadd d1, d1, d30
    arm64.fadd d1, d1, d31
    arm64.fadd d1, d1, d8
    arm64.fadd d1, d1, d9
    arm64.fadd d1, d1, d10
    arm64.fadd d1, d1, d11
    arm64.fadd d1, d1, d12
    arm64.fadd d1, d1, d13
    arm64.fadd d1, d1, d14
    arm64.loadRegSlot d2, slot7
    arm64.fadd d1, d1, d2
    arm64.loadRegSlot d2, slot0
    arm64.fadd d1, d1, d2
  __il_body#10:
    arm64.fpLoadRdata d2, [rdata + __fconst_1]
    arm64.fadd d1, d1, d2
  __il_cont#7:
    arm64.loadRegSlot d2, slot1
    arm64.loadRegSlot d3, slot2
    arm64.fadd d2, d2, d3
    arm64.loadRegSlot d3, slot3
    arm64.fadd d2, d2, d3
    arm64.loadRegSlot d3, slot4
    arm64.fadd d2, d2, d3
    arm64.loadRegSlot d3, slot5
    arm64.fadd d2, d2, d3
    arm64.loadRegSlot d3, slot6
    arm64.fadd d2, d2, d3
    arm64.fadd d0, d2, d0
    arm64.fadd d0, d0, d15
    arm64.fadd d0, d0, d1
    arm64.fpLoadRdata d1, [rdata + __fconst_793]
    arm64.fcmp d0, d1
    arm64.b.ne ternaryfalse
  ternarytrue:
    arm64.movImm x0, 0
    arm64.b __rc_ok
  ternaryfalse:
    arm64.movImm x0, 99
  __rc_ok:
    arm64.loadRegSlot d8, slot8
    arm64.loadRegSlot d9, slot9
    arm64.loadRegSlot d10, slot10
    arm64.loadRegSlot d11, slot11
    arm64.loadRegSlot d12, slot12
    arm64.loadRegSlot d13, slot13
    arm64.loadRegSlot d14, slot14
    arm64.loadRegSlot d15, slot15
    arm64.epilogue 144
    arm64.ret
}
```

<!-- test: confined-reuse-defs-at-x64s-own-scale-float -->
⭐ **THE SAME DEMAND, TWENTY-FIVE LINES LONG.** x64 has sixteen XMMs of which ten are
callee-saved, so it reaches the wall at sixteen non-crossing floats and four crossing ones.
arm64's thirty-two vector registers absorb it, so this case is a plain correctness program
there; the value of keeping it is that it is the SMALLEST program of this shape that reaches
the wall.

**Twenty and twenty-four unconstrained floats both compile clean**, which is the pigeonhole
control: nineteen live floats already pass x64's pool, so the difference this case makes is
the confinement and nothing else.

`n = 4.0`, so `a0`..`a15` are `5.0`..`20.0` summing to `200.0`; `c = 201.0`; `b0`..`b3` are
`10.0`, `12.0`, `14.0`, `16.0`. Total `253.0`.
```maxon
function fsink(x Real) returns Real
	return x + 1.0
end 'fsink'

function main() returns ExitCode
	let n = fsink(3.0)
	let a0 = n + 1.0
	let a1 = n + 2.0
	let a2 = n + 3.0
	let a3 = n + 4.0
	let a4 = n + 5.0
	let a5 = n + 6.0
	let a6 = n + 7.0
	let a7 = n + 8.0
	let a8 = n + 9.0
	let a9 = n + 10.0
	let a10 = n + 11.0
	let a11 = n + 12.0
	let a12 = n + 13.0
	let a13 = n + 14.0
	let a14 = n + 15.0
	let a15 = n + 16.0
	let b0 = a0 * 2.0
	let b1 = a1 * 2.0
	let b2 = a2 * 2.0
	let b3 = a3 * 2.0
	let s = a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15
	let c = fsink(s)
	let total = b0 + b1 + b2 + b3 + c
	return 0 if total == 253.0 else 99
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
0
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 128
    x64.storeSlotReg slot5, xmm6
    x64.storeSlotReg slot6, xmm7
    x64.storeSlotReg slot7, xmm8
    x64.storeSlotReg slot8, xmm9
    x64.storeSlotReg slot9, xmm10
    x64.storeSlotReg slot10, xmm11
    x64.storeSlotReg slot11, xmm12
    x64.storeSlotReg slot12, xmm13
    x64.storeSlotReg slot13, xmm14
    x64.storeSlotReg slot14, xmm15
  __il_body#9:
    x64.movsdRegRip xmm0, [rip + __fconst_4]
  __il_cont#8:
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.movRegReg xmm2, xmm0
    x64.addsdRegReg xmm2, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_2]
    x64.movRegReg xmm3, xmm0
    x64.addsdRegReg xmm3, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.movRegReg xmm4, xmm0
    x64.addsdRegReg xmm4, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_4]
    x64.movRegReg xmm5, xmm0
    x64.addsdRegReg xmm5, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_5]
    x64.movRegReg xmm6, xmm0
    x64.addsdRegReg xmm6, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_6]
    x64.movRegReg xmm7, xmm0
    x64.addsdRegReg xmm7, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_7]
    x64.movRegReg xmm8, xmm0
    x64.addsdRegReg xmm8, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_8]
    x64.movRegReg xmm9, xmm0
    x64.addsdRegReg xmm9, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_9]
    x64.movRegReg xmm10, xmm0
    x64.addsdRegReg xmm10, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_10]
    x64.movRegReg xmm11, xmm0
    x64.addsdRegReg xmm11, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_11]
    x64.movRegReg xmm12, xmm0
    x64.addsdRegReg xmm12, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_12]
    x64.movRegReg xmm13, xmm0
    x64.addsdRegReg xmm13, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_13]
    x64.movRegReg xmm14, xmm0
    x64.addsdRegReg xmm14, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_14]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot4, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_15]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_16]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.storeSlotReg slot0, xmm0
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm2
    x64.mulsdRegReg xmm1, xmm2, xmm0
    x64.storeSlotReg slot1, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm3
    x64.mulsdRegReg xmm1, xmm3, xmm0
    x64.storeSlotReg slot2, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm4
    x64.mulsdRegReg xmm1, xmm4, xmm0
    x64.storeSlotReg slot3, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm5
    x64.mulsdRegReg xmm1, xmm5, xmm0
    x64.addsdRegReg xmm2, xmm2, xmm3
    x64.addsdRegReg xmm2, xmm2, xmm4
    x64.addsdRegReg xmm2, xmm2, xmm5
    x64.addsdRegReg xmm2, xmm2, xmm6
    x64.addsdRegReg xmm2, xmm2, xmm7
    x64.addsdRegReg xmm2, xmm2, xmm8
    x64.addsdRegReg xmm2, xmm2, xmm9
    x64.addsdRegReg xmm2, xmm2, xmm10
    x64.addsdRegReg xmm2, xmm2, xmm11
    x64.addsdRegReg xmm2, xmm2, xmm12
    x64.addsdRegReg xmm2, xmm2, xmm13
    x64.addsdRegReg xmm2, xmm2, xmm14
    x64.loadRegSlot xmm0, slot4
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.addsdRegReg xmm2, xmm2, xmm15
    x64.loadRegSlot xmm0, slot0
    x64.addsdRegReg xmm2, xmm2, xmm0
  __il_body#10:
    x64.movsdRegRip xmm0, [rip + __fconst_1]
    x64.addsdRegReg xmm2, xmm2, xmm0
  __il_cont#7:
    x64.loadRegSlot xmm0, slot1
    x64.loadRegSlot xmm3, slot2
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot3
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.addsdRegReg xmm0, xmm0, xmm2
    x64.movsdRegRip xmm1, [rip + __fconst_253]
    x64.ucomisdRegReg xmm0, xmm1
    x64.jcc parity, ternaryfalse
  fordered:
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp __rc_ok
  ternaryfalse:
    x64.movRegImm32 r8, 99
  __rc_ok:
    x64.loadRegSlot xmm6, slot5
    x64.loadRegSlot xmm7, slot6
    x64.loadRegSlot xmm8, slot7
    x64.loadRegSlot xmm9, slot8
    x64.loadRegSlot xmm10, slot9
    x64.loadRegSlot xmm11, slot10
    x64.loadRegSlot xmm12, slot11
    x64.loadRegSlot xmm13, slot12
    x64.loadRegSlot xmm14, slot13
    x64.loadRegSlot xmm15, slot14
    x64.epilogue 128
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 128
    x64.storeSlotReg slot5, xmm6
    x64.storeSlotReg slot6, xmm7
    x64.storeSlotReg slot7, xmm8
    x64.storeSlotReg slot8, xmm9
    x64.storeSlotReg slot9, xmm10
    x64.storeSlotReg slot10, xmm11
    x64.storeSlotReg slot11, xmm12
    x64.storeSlotReg slot12, xmm13
    x64.storeSlotReg slot13, xmm14
    x64.storeSlotReg slot14, xmm15
  __il_body#9:
    x64.movsdRegRip xmm0, [rip + __fconst_4]
  __il_cont#8:
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.movRegReg xmm2, xmm0
    x64.addsdRegReg xmm2, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_2]
    x64.movRegReg xmm3, xmm0
    x64.addsdRegReg xmm3, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.movRegReg xmm4, xmm0
    x64.addsdRegReg xmm4, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_4]
    x64.movRegReg xmm5, xmm0
    x64.addsdRegReg xmm5, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_5]
    x64.movRegReg xmm6, xmm0
    x64.addsdRegReg xmm6, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_6]
    x64.movRegReg xmm7, xmm0
    x64.addsdRegReg xmm7, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_7]
    x64.movRegReg xmm8, xmm0
    x64.addsdRegReg xmm8, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_8]
    x64.movRegReg xmm9, xmm0
    x64.addsdRegReg xmm9, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_9]
    x64.movRegReg xmm10, xmm0
    x64.addsdRegReg xmm10, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_10]
    x64.movRegReg xmm11, xmm0
    x64.addsdRegReg xmm11, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_11]
    x64.movRegReg xmm12, xmm0
    x64.addsdRegReg xmm12, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_12]
    x64.movRegReg xmm13, xmm0
    x64.addsdRegReg xmm13, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_13]
    x64.movRegReg xmm14, xmm0
    x64.addsdRegReg xmm14, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_14]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.storeSlotReg slot4, xmm15
    x64.movsdRegRip xmm1, [rip + __fconst_15]
    x64.movRegReg xmm15, xmm0
    x64.addsdRegReg xmm15, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_16]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.storeSlotReg slot0, xmm0
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm2
    x64.mulsdRegReg xmm1, xmm2, xmm0
    x64.storeSlotReg slot1, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm3
    x64.mulsdRegReg xmm1, xmm3, xmm0
    x64.storeSlotReg slot2, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm4
    x64.mulsdRegReg xmm1, xmm4, xmm0
    x64.storeSlotReg slot3, xmm1
    x64.movsdRegRip xmm0, [rip + __fconst_2]
    x64.movRegReg xmm1, xmm5
    x64.mulsdRegReg xmm1, xmm5, xmm0
    x64.addsdRegReg xmm2, xmm2, xmm3
    x64.addsdRegReg xmm2, xmm2, xmm4
    x64.addsdRegReg xmm2, xmm2, xmm5
    x64.addsdRegReg xmm2, xmm2, xmm6
    x64.addsdRegReg xmm2, xmm2, xmm7
    x64.addsdRegReg xmm2, xmm2, xmm8
    x64.addsdRegReg xmm2, xmm2, xmm9
    x64.addsdRegReg xmm2, xmm2, xmm10
    x64.addsdRegReg xmm2, xmm2, xmm11
    x64.addsdRegReg xmm2, xmm2, xmm12
    x64.addsdRegReg xmm2, xmm2, xmm13
    x64.addsdRegReg xmm2, xmm2, xmm14
    x64.loadRegSlot xmm0, slot4
    x64.addsdRegReg xmm2, xmm2, xmm0
    x64.addsdRegReg xmm2, xmm2, xmm15
    x64.loadRegSlot xmm0, slot0
    x64.addsdRegReg xmm2, xmm2, xmm0
  __il_body#10:
    x64.movsdRegRip xmm0, [rip + __fconst_1]
    x64.addsdRegReg xmm2, xmm2, xmm0
  __il_cont#7:
    x64.loadRegSlot xmm0, slot1
    x64.loadRegSlot xmm3, slot2
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.loadRegSlot xmm3, slot3
    x64.addsdRegReg xmm0, xmm0, xmm3
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.addsdRegReg xmm0, xmm0, xmm2
    x64.movsdRegRip xmm1, [rip + __fconst_253]
    x64.ucomisdRegReg xmm0, xmm1
    x64.jcc parity, ternaryfalse
  fordered:
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp __rc_ok
  ternaryfalse:
    x64.movRegImm32 r8, 99
  __rc_ok:
    x64.loadRegSlot xmm6, slot5
    x64.loadRegSlot xmm7, slot6
    x64.loadRegSlot xmm8, slot7
    x64.loadRegSlot xmm9, slot8
    x64.loadRegSlot xmm10, slot9
    x64.loadRegSlot xmm11, slot10
    x64.loadRegSlot xmm12, slot11
    x64.loadRegSlot xmm13, slot12
    x64.loadRegSlot xmm14, slot13
    x64.loadRegSlot xmm15, slot14
    x64.epilogue 128
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body#9:
    arm64.fpLoadRdata d0, [rdata + __fconst_4]
  __il_cont#8:
    arm64.fpLoadRdata d1, [rdata + __fconst_1]
    arm64.fadd d1, d0, d1
    arm64.fpLoadRdata d2, [rdata + __fconst_2]
    arm64.fadd d2, d0, d2
    arm64.fpLoadRdata d3, [rdata + __fconst_3]
    arm64.fadd d3, d0, d3
    arm64.fpLoadRdata d4, [rdata + __fconst_4]
    arm64.fadd d4, d0, d4
    arm64.fpLoadRdata d5, [rdata + __fconst_5]
    arm64.fadd d5, d0, d5
    arm64.fpLoadRdata d6, [rdata + __fconst_6]
    arm64.fadd d6, d0, d6
    arm64.fpLoadRdata d7, [rdata + __fconst_7]
    arm64.fadd d7, d0, d7
    arm64.fpLoadRdata d16, [rdata + __fconst_8]
    arm64.fadd d16, d0, d16
    arm64.fpLoadRdata d17, [rdata + __fconst_9]
    arm64.fadd d17, d0, d17
    arm64.fpLoadRdata d18, [rdata + __fconst_10]
    arm64.fadd d18, d0, d18
    arm64.fpLoadRdata d19, [rdata + __fconst_11]
    arm64.fadd d19, d0, d19
    arm64.fpLoadRdata d20, [rdata + __fconst_12]
    arm64.fadd d20, d0, d20
    arm64.fpLoadRdata d21, [rdata + __fconst_13]
    arm64.fadd d21, d0, d21
    arm64.fpLoadRdata d22, [rdata + __fconst_14]
    arm64.fadd d22, d0, d22
    arm64.fpLoadRdata d23, [rdata + __fconst_15]
    arm64.fadd d23, d0, d23
    arm64.fpLoadRdata d24, [rdata + __fconst_16]
    arm64.fadd d0, d0, d24
    arm64.fpLoadRdata d24, [rdata + __fconst_2]
    arm64.fmul d24, d1, d24
    arm64.fpLoadRdata d25, [rdata + __fconst_2]
    arm64.fmul d25, d2, d25
    arm64.fpLoadRdata d26, [rdata + __fconst_2]
    arm64.fmul d26, d3, d26
    arm64.fpLoadRdata d27, [rdata + __fconst_2]
    arm64.fmul d27, d4, d27
    arm64.fadd d1, d1, d2
    arm64.fadd d1, d1, d3
    arm64.fadd d1, d1, d4
    arm64.fadd d1, d1, d5
    arm64.fadd d1, d1, d6
    arm64.fadd d1, d1, d7
    arm64.fadd d1, d1, d16
    arm64.fadd d1, d1, d17
    arm64.fadd d1, d1, d18
    arm64.fadd d1, d1, d19
    arm64.fadd d1, d1, d20
    arm64.fadd d1, d1, d21
    arm64.fadd d1, d1, d22
    arm64.fadd d1, d1, d23
    arm64.fadd d0, d1, d0
  __il_body#10:
    arm64.fpLoadRdata d1, [rdata + __fconst_1]
    arm64.fadd d0, d0, d1
  __il_cont#7:
    arm64.fadd d1, d24, d25
    arm64.fadd d1, d1, d26
    arm64.fadd d1, d1, d27
    arm64.fadd d0, d1, d0
    arm64.fpLoadRdata d1, [rdata + __fconst_253]
    arm64.fcmp d0, d1
    arm64.b.ne ternaryfalse
  ternarytrue:
    arm64.movImm x0, 0
    arm64.b __rc_ok
  ternaryfalse:
    arm64.movImm x0, 99
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
  __il_body#9:
    arm64.fpLoadRdata d0, [rdata + __fconst_4]
  __il_cont#8:
    arm64.fpLoadRdata d1, [rdata + __fconst_1]
    arm64.fadd d1, d0, d1
    arm64.fpLoadRdata d2, [rdata + __fconst_2]
    arm64.fadd d2, d0, d2
    arm64.fpLoadRdata d3, [rdata + __fconst_3]
    arm64.fadd d3, d0, d3
    arm64.fpLoadRdata d4, [rdata + __fconst_4]
    arm64.fadd d4, d0, d4
    arm64.fpLoadRdata d5, [rdata + __fconst_5]
    arm64.fadd d5, d0, d5
    arm64.fpLoadRdata d6, [rdata + __fconst_6]
    arm64.fadd d6, d0, d6
    arm64.fpLoadRdata d7, [rdata + __fconst_7]
    arm64.fadd d7, d0, d7
    arm64.fpLoadRdata d16, [rdata + __fconst_8]
    arm64.fadd d16, d0, d16
    arm64.fpLoadRdata d17, [rdata + __fconst_9]
    arm64.fadd d17, d0, d17
    arm64.fpLoadRdata d18, [rdata + __fconst_10]
    arm64.fadd d18, d0, d18
    arm64.fpLoadRdata d19, [rdata + __fconst_11]
    arm64.fadd d19, d0, d19
    arm64.fpLoadRdata d20, [rdata + __fconst_12]
    arm64.fadd d20, d0, d20
    arm64.fpLoadRdata d21, [rdata + __fconst_13]
    arm64.fadd d21, d0, d21
    arm64.fpLoadRdata d22, [rdata + __fconst_14]
    arm64.fadd d22, d0, d22
    arm64.fpLoadRdata d23, [rdata + __fconst_15]
    arm64.fadd d23, d0, d23
    arm64.fpLoadRdata d24, [rdata + __fconst_16]
    arm64.fadd d0, d0, d24
    arm64.fpLoadRdata d24, [rdata + __fconst_2]
    arm64.fmul d24, d1, d24
    arm64.fpLoadRdata d25, [rdata + __fconst_2]
    arm64.fmul d25, d2, d25
    arm64.fpLoadRdata d26, [rdata + __fconst_2]
    arm64.fmul d26, d3, d26
    arm64.fpLoadRdata d27, [rdata + __fconst_2]
    arm64.fmul d27, d4, d27
    arm64.fadd d1, d1, d2
    arm64.fadd d1, d1, d3
    arm64.fadd d1, d1, d4
    arm64.fadd d1, d1, d5
    arm64.fadd d1, d1, d6
    arm64.fadd d1, d1, d7
    arm64.fadd d1, d1, d16
    arm64.fadd d1, d1, d17
    arm64.fadd d1, d1, d18
    arm64.fadd d1, d1, d19
    arm64.fadd d1, d1, d20
    arm64.fadd d1, d1, d21
    arm64.fadd d1, d1, d22
    arm64.fadd d1, d1, d23
    arm64.fadd d0, d1, d0
  __il_body#10:
    arm64.fpLoadRdata d1, [rdata + __fconst_1]
    arm64.fadd d0, d0, d1
  __il_cont#7:
    arm64.fadd d1, d24, d25
    arm64.fadd d1, d1, d26
    arm64.fadd d1, d1, d27
    arm64.fadd d0, d1, d0
    arm64.fpLoadRdata d1, [rdata + __fconst_253]
    arm64.fcmp d0, d1
    arm64.b.ne ternaryfalse
  ternarytrue:
    arm64.movImm x0, 0
    arm64.b __rc_ok
  ternaryfalse:
    arm64.movImm x0, 99
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: confined-reuse-defs-at-x64s-own-scale-int -->
The GPR half of the case above, at the GPR file's own scale: fourteen ints that cross no
call and four that do, against fourteen allocatable GPRs of which five are callee-saved.
Twelve and four compiles clean; fourteen and four does not.

`n = 4`, so `a0`..`a13` are `5`..`18` summing to `161`; `c = 162`; `b0`..`b3` are `10`,
`12`, `14`, `16`. Total `214`.
```maxon
typealias Integer = int(i64.min to i64.max)

function sink(x Integer) returns Integer
	return x + 1
end 'sink'

function main() returns ExitCode
	let n = sink(3)
	let a0 = n + 1
	let a1 = n + 2
	let a2 = n + 3
	let a3 = n + 4
	let a4 = n + 5
	let a5 = n + 6
	let a6 = n + 7
	let a7 = n + 8
	let a8 = n + 9
	let a9 = n + 10
	let a10 = n + 11
	let a11 = n + 12
	let a12 = n + 13
	let a13 = n + 14
	let b0 = a0 * 2
	let b1 = a1 * 2
	let b2 = a2 * 2
	let b3 = a3 * 2
	let s = a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13
	let c = sink(s)
	let total = b0 + b1 + b2 + b3 + c
	return 0 if total == 214 else 99
end 'main'
```
```exitcode
0
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
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
    x64.prologue 32
  __il_body#9:
    x64.movRegImm32 rax, 4
  __il_cont#8:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.leaRegRegImm32 rdx, rax, 2
    x64.leaRegRegImm32 rsi, rax, 3
    x64.leaRegRegImm32 rdi, rax, 4
    x64.leaRegRegImm32 r8, rax, 5
    x64.leaRegRegImm32 r9, rax, 6
    x64.leaRegRegImm32 r10, rax, 7
    x64.leaRegRegImm32 r11, rax, 8
    x64.leaRegRegImm32 rbx, rax, 9
    x64.leaRegRegImm32 r12, rax, 10
    x64.leaRegRegImm32 r13, rax, 11
    x64.leaRegRegImm32 r14, rax, 12
    x64.leaRegRegImm32 r15, rax, 13
    x64.leaRegRegImm32 rax, rax, 14
    x64.storeSlotReg slot0, rax
    x64.imulRegRegImm32 rax, rcx, 2
    x64.storeSlotReg slot1, rax
    x64.imulRegRegImm32 rax, rdx, 2
    x64.storeSlotReg slot2, rax
    x64.imulRegRegImm32 rax, rsi, 2
    x64.storeSlotReg slot3, rax
    x64.imulRegRegImm32 rax, rdi, 2
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rcx, rcx, rsi
    x64.leaRegRegReg rcx, rcx, rdi
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rcx, rcx, r9
    x64.leaRegRegReg rcx, rcx, r10
    x64.leaRegRegReg rcx, rcx, r11
    x64.leaRegRegReg rcx, rcx, rbx
    x64.leaRegRegReg rcx, rcx, r12
    x64.leaRegRegReg rcx, rcx, r13
    x64.leaRegRegReg rcx, rcx, r14
    x64.leaRegRegReg rcx, rcx, r15
    x64.loadRegSlot rdx, slot0
    x64.leaRegRegReg rcx, rcx, rdx
  __il_body#10:
    x64.leaRegRegImm32 rcx, rcx, 1
  __il_cont#7:
    x64.loadRegSlot rdx, slot1
    x64.loadRegSlot rsi, slot2
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot3
    x64.leaRegRegReg rdx, rdx, rsi
    x64.leaRegRegReg rax, rdx, rax
    x64.leaRegRegReg rax, rax, rcx
  ternarytrue:
    x64.movRegImm32 r8, 0
  __rc_ok:
    x64.epilogue 32
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
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
    x64.prologue 32
  __il_body#9:
    x64.movRegImm32 rax, 4
  __il_cont#8:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.leaRegRegImm32 rdx, rax, 2
    x64.leaRegRegImm32 rsi, rax, 3
    x64.leaRegRegImm32 rdi, rax, 4
    x64.leaRegRegImm32 r8, rax, 5
    x64.leaRegRegImm32 r9, rax, 6
    x64.leaRegRegImm32 r10, rax, 7
    x64.leaRegRegImm32 r11, rax, 8
    x64.leaRegRegImm32 rbx, rax, 9
    x64.leaRegRegImm32 r12, rax, 10
    x64.leaRegRegImm32 r13, rax, 11
    x64.leaRegRegImm32 r14, rax, 12
    x64.leaRegRegImm32 r15, rax, 13
    x64.leaRegRegImm32 rax, rax, 14
    x64.storeSlotReg slot0, rax
    x64.imulRegRegImm32 rax, rcx, 2
    x64.storeSlotReg slot1, rax
    x64.imulRegRegImm32 rax, rdx, 2
    x64.storeSlotReg slot2, rax
    x64.imulRegRegImm32 rax, rsi, 2
    x64.storeSlotReg slot3, rax
    x64.imulRegRegImm32 rax, rdi, 2
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rcx, rcx, rsi
    x64.leaRegRegReg rcx, rcx, rdi
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rcx, rcx, r9
    x64.leaRegRegReg rcx, rcx, r10
    x64.leaRegRegReg rcx, rcx, r11
    x64.leaRegRegReg rcx, rcx, rbx
    x64.leaRegRegReg rcx, rcx, r12
    x64.leaRegRegReg rcx, rcx, r13
    x64.leaRegRegReg rcx, rcx, r14
    x64.leaRegRegReg rcx, rcx, r15
    x64.loadRegSlot rdx, slot0
    x64.leaRegRegReg rcx, rcx, rdx
  __il_body#10:
    x64.leaRegRegImm32 rcx, rcx, 1
  __il_cont#7:
    x64.loadRegSlot rdx, slot1
    x64.loadRegSlot rsi, slot2
    x64.leaRegRegReg rdx, rdx, rsi
    x64.loadRegSlot rsi, slot3
    x64.leaRegRegReg rdx, rdx, rsi
    x64.leaRegRegReg rax, rdx, rax
    x64.leaRegRegReg rax, rax, rcx
  ternarytrue:
    x64.movRegImm32 r8, 0
  __rc_ok:
    x64.epilogue 32
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
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#9:
    arm64.movImm x0, 4
  __il_cont#8:
    arm64.add x1, x0, 1
    arm64.add x2, x0, 2
    arm64.add x3, x0, 3
    arm64.add x4, x0, 4
    arm64.add x5, x0, 5
    arm64.add x6, x0, 6
    arm64.add x7, x0, 7
    arm64.add x8, x0, 8
    arm64.add x9, x0, 9
    arm64.add x10, x0, 10
    arm64.add x11, x0, 11
    arm64.add x12, x0, 12
    arm64.add x13, x0, 13
    arm64.add x0, x0, 14
    arm64.lsl x14, x1, 1
    arm64.lsl x19, x2, 1
    arm64.lsl x20, x3, 1
    arm64.lsl x21, x4, 1
    arm64.add x1, x1, x2
    arm64.add x1, x1, x3
    arm64.add x1, x1, x4
    arm64.add x1, x1, x5
    arm64.add x1, x1, x6
    arm64.add x1, x1, x7
    arm64.add x1, x1, x8
    arm64.add x1, x1, x9
    arm64.add x1, x1, x10
    arm64.add x1, x1, x11
    arm64.add x1, x1, x12
    arm64.add x1, x1, x13
    arm64.add x0, x1, x0
  __il_body#10:
    arm64.add x0, x0, 1
  __il_cont#7:
    arm64.add x1, x14, x19
    arm64.add x1, x1, x20
    arm64.add x1, x1, x21
    arm64.add x0, x1, x0
  ternarytrue:
    arm64.movImm x0, 0
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#9:
    arm64.movImm x0, 4
  __il_cont#8:
    arm64.add x1, x0, 1
    arm64.add x2, x0, 2
    arm64.add x3, x0, 3
    arm64.add x4, x0, 4
    arm64.add x5, x0, 5
    arm64.add x6, x0, 6
    arm64.add x7, x0, 7
    arm64.add x8, x0, 8
    arm64.add x9, x0, 9
    arm64.add x10, x0, 10
    arm64.add x11, x0, 11
    arm64.add x12, x0, 12
    arm64.add x13, x0, 13
    arm64.add x0, x0, 14
    arm64.lsl x14, x1, 1
    arm64.lsl x19, x2, 1
    arm64.lsl x20, x3, 1
    arm64.lsl x21, x4, 1
    arm64.add x1, x1, x2
    arm64.add x1, x1, x3
    arm64.add x1, x1, x4
    arm64.add x1, x1, x5
    arm64.add x1, x1, x6
    arm64.add x1, x1, x7
    arm64.add x1, x1, x8
    arm64.add x1, x1, x9
    arm64.add x1, x1, x10
    arm64.add x1, x1, x11
    arm64.add x1, x1, x12
    arm64.add x1, x1, x13
    arm64.add x0, x1, x0
  __il_body#10:
    arm64.add x0, x0, 1
  __il_cont#7:
    arm64.add x1, x14, x19
    arm64.add x1, x1, x20
    arm64.add x1, x1, x21
    arm64.add x0, x1, x0
  ternarytrue:
    arm64.movImm x0, 0
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: the-runtime-overflows-its-own-pool-and-is-relieved -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
⭐⭐ **E5001 IS AN INSTRUCTION TO AN AUTHOR, AND THE COMPILER'S OWN EMITTED CODE HAS NONE — so case 3 above
does not reach it, and this four-line program is what proves it.** `sleep` installs the green-thread
runtime, whose `__gt_timer_check` walks a min-heap of parked deadlines: a 21-block function with a nested
sift-down, holding **18 GPRs against x64's pool of 14** at a point inside its outer loop, every one of the
18 defined or read inside that loop. Under case 3's rule the cold-spill gate refuses all 18 and the
allocator raises E5001 — against a function with no source file, no line numbers, and no value the author
can see or delete. The diagnostic cannot even be BUILT for it: `defRangeOf`'s four routes all end in
tables only the parser fills, so it PANICS on the first blocking value (RULE 3) and no binary is produced.
So refusing would make **every program using a green thread fail to compile**, this one included, and
the compiler's own self-compile with it.

⇒ The splitter retries such a peak under the FORCED bracket — a reload before every use, paid per
iteration — because that cost is the only alternative to refusing, and refusing is a message to nobody
(`SplitLiveRanges.relievePressure`, `noAuthorToRefuse`). The relief is worth exactly what it costs and no
more: the four splits it takes here are inside the scheduler's timer walk, which runs once per netpoll
pass, not in anything the program wrote.

⚠ **THE GATE IS `x64-windows` FOR TWO REASONS AT ONCE, AND EITHER ALONE WOULD BE ENOUGH**: it is a
green-thread substrate lane (see `async-scheduler`'s *Targets*), and 18 values only exceed a pool of
FOURTEEN — arm64's is wide enough that the same walk never overflows, so the case would pass there
without discriminating anything.
```maxon
function main() returns ExitCode
	sleep(1)
	return 0
end 'main'
```
```exitcode
0
```

```TargetIr:x64-windows
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_live_count@88 = i64 0
  __gt_quiesce_waiter@96 = i64 0
  __gt_quiesce_stuck@104 = i64 0
  __gt_seed_bytes@112 = i64 8192
  __gt_stack_bytes_sum@120 = i64 0
  __gt_stack_bytes_count@128 = i64 0
  __gt_seed_sum_seen@136 = i64 0
  __gt_seed_count_seen@144 = i64 0
  __gt_keeps_cpu_clock@152 = i64 0
  __sched_active_workers@160 = i64 1
  __sched_max_active_workers@168 = i64 1
  __sched_tls_index@176 = i64 0
  __sched_tls_teb_offset@184 = i64 0
  __sched_procs@192 = i64 0
  __sched_allm@200 = i64 0
  __sched_async_preempt_off@208 = i64 0
  __sched_midle@216 = i64 0
  __sched_pidle@224 = i64 0
  __gt_gfree_head@232 = i64 0
  __sched_arena_cursor@240 = i64 0
  __sched_arena_limit@248 = i64 0
  __gt_records_carved@256 = i64 0
  __sched_npidle@264 = i64 0
  __sched_runq_size@272 = i64 0
  __sched_global_pushes@280 = i64 0
  __sched_timer_scan_steps@288 = i64 0
  __sched_mcount@296 = i64 0
  __sched_nmidle@304 = i64 0
  __sched_main_m@312 = i64 0
  __sched_phase@320 = i64 0
  __sched_lastpoll@328 = i64 0
  __sched_poll_until@336 = i64 0
  __sched_regain_head@344 = i64 0
  __np_poller@352 = i64 0
  __np_break@360 = i64 0
  __np_wake_sig@368 = i64 0
  __np_pd_table@376 = i64 0
  __np_pd_cap@384 = i64 0
  __np_waiters@392 = i64 0
  __sched_num_procs@400 = i64 0
  __sched_shutdown_flag@408 = i64 0
  __sched_lock@416 = i64 0
  __sched_lock_depth_sink@424 = i64 0
  __sched_nmspinning@432 = i64 0
  __sched_needspinning@440 = i64 0
  __sched_sysmon_event@448 = i64 0
  __sched_sysmon_wait@456 = i64 0
  __sched_timer_starts@464 = i64 0
  __sched_preempt_ext_lock@472 = i64 0
  __sched_preempt_context@480 = i64 0
  __slab_arena_list@488 = i64 0
  __slab_arena_map_l1@496 = i64 0
  __slab_state@504 = i64 0
  __mrt_console_probe_stdin@512 = i8 0
  __mrt_console_probe_stdout@513 = i8 0
  __mrt_console_probe_stderr@514 = i8 0
  __mrt_program_started@515 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 1
  __il_body:
    x64.callDirect __gt_sleep
  __il_cont:
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_live_count@88 = i64 0
  __gt_quiesce_waiter@96 = i64 0
  __gt_quiesce_stuck@104 = i64 0
  __gt_seed_bytes@112 = i64 2048
  __gt_stack_bytes_sum@120 = i64 0
  __gt_stack_bytes_count@128 = i64 0
  __gt_seed_sum_seen@136 = i64 0
  __gt_seed_count_seen@144 = i64 0
  __gt_keeps_cpu_clock@152 = i64 0
  __sched_active_workers@160 = i64 1
  __sched_max_active_workers@168 = i64 1
  __sched_tls_index@176 = i64 0
  __sched_tls_teb_offset@184 = i64 0
  __sched_procs@192 = i64 0
  __sched_allm@200 = i64 0
  __sched_async_preempt_off@208 = i64 0
  __sched_midle@216 = i64 0
  __sched_pidle@224 = i64 0
  __gt_gfree_head@232 = i64 0
  __sched_arena_cursor@240 = i64 0
  __sched_arena_limit@248 = i64 0
  __gt_records_carved@256 = i64 0
  __sched_npidle@264 = i64 0
  __sched_runq_size@272 = i64 0
  __sched_global_pushes@280 = i64 0
  __sched_timer_scan_steps@288 = i64 0
  __sched_mcount@296 = i64 0
  __sched_nmidle@304 = i64 0
  __sched_main_m@312 = i64 0
  __sched_phase@320 = i64 0
  __sched_lastpoll@328 = i64 0
  __sched_poll_until@336 = i64 0
  __sched_regain_head@344 = i64 0
  __np_poller@352 = i64 0
  __np_break@360 = i64 0
  __np_wake_sig@368 = i64 0
  __np_pd_table@376 = i64 0
  __np_pd_cap@384 = i64 0
  __np_waiters@392 = i64 0
  __sched_num_procs@400 = i64 0
  __sched_shutdown_flag@408 = i64 0
  __sched_lock@416 = i64 0
  __sched_lock_depth_sink@424 = i64 0
  __sched_nmspinning@432 = i64 0
  __sched_needspinning@440 = i64 0
  __sched_sysmon_event@448 = i64 0
  __sched_sysmon_wait@456 = i64 0
  __sched_timer_starts@464 = i64 0
  __mrt_envp@472 = i64 0
  __mrt_signal_stack_bytes@480 = i64 0
  __mrt_tls_next_key@488 = i64 0
  __mrt_errno@496 = i64 0
  __mrt_tls_ready@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 1
  __il_body:
    x64.callDirect __gt_sleep
  __il_cont:
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```
