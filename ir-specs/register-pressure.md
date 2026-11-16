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

**A register held at an op that is in no live set is demand too.** A DEAD DEF writes a register that
nothing reads, so it is live at no program point; an x64 REUSE DEF needs a register of its own at its
two-address op when it cannot inherit its input's. `addOpTransientPressure` counts that register
against the full pool, and `opConfinedTransient` counts it against the callee-saved registers a def
that crosses a later call is confined to. The dead-def cases are the unread trailing parameters of
`dead-def-parameter-holds-a-register` and `dead-def-parameter-past-the-arm64-pool`, with
`dead-def-parameter-at-the-pool-boundary` the fit one below them; the reuse-def cases are
`reuse-def-confined-by-a-later-call` and the `confined-reuse-defs-` cases after it.

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
	var s1 = 1 as Integer
	var s2 = 2 as Integer
	var s3 = 3 as Integer
	var s4 = 4 as Integer
	var s5 = 5 as Integer
	var s6 = 6 as Integer
	var s7 = 7 as Integer
	var s8 = 8 as Integer
	var s9 = 9 as Integer
	var s10 = 10 as Integer
	var s11 = 11 as Integer
	var s12 = 12 as Integer
	var s13 = 13 as Integer
	var s14 = 14 as Integer
	var s15 = 15 as Integer
	var s16 = 16 as Integer
	var i = 0 as Integer
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
	var s1 = 1 as Integer
	var s2 = 2 as Integer
	var s3 = 3 as Integer
	var s4 = 4 as Integer
	var s5 = 5 as Integer
	var s6 = 6 as Integer
	var s7 = 7 as Integer
	var s8 = 8 as Integer
	var s9 = 9 as Integer
	var s10 = 10 as Integer
	var s11 = 11 as Integer
	var s12 = 12 as Integer
	var s13 = 13 as Integer
	var s14 = 14 as Integer
	var s15 = 15 as Integer
	var s16 = 16 as Integer
	var s17 = 17 as Integer
	var s18 = 18 as Integer
	var s19 = 19 as Integer
	var s20 = 20 as Integer
	var s21 = 21 as Integer
	var s22 = 22 as Integer
	var s23 = 23 as Integer
	var s24 = 24 as Integer
	var s25 = 25 as Integer
	var s26 = 26 as Integer
	var s27 = 27 as Integer
	var s28 = 28 as Integer
	var i = 0 as Integer
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
	var f0 = 0 as Integer
	var f1 = 0 as Integer
	var f2 = 0 as Integer
	var f3 = 0 as Integer
	var f4 = 0 as Integer
	var f5 = 0 as Integer
	var f6 = 0 as Integer
	var f7 = 0 as Integer
	var f8 = 0 as Integer
	var f9 = 0 as Integer
	var f10 = 0 as Integer
	var f11 = 0 as Integer
	var f12 = 0 as Integer
	var f13 = 0 as Integer
	var f14 = 0 as Integer
	var i = 0 as Integer
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
callee-saved registers survive a call on x64. One value therefore *cannot* stay in a register: the ABI
leaves it exactly one home, the stack. So the splitter stores it before the call and reloads it
after — a placement it does not choose, only obeys — and the loop body grows by one store and one
load, bracketing a call that costs far more. Nothing is searched, and no error is raised. On arm64,
whose ten callee-saved registers `x19`-`x28` hold all six, the loop carries no store and no load.

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
	var s1 = 1 as Integer
	var s2 = 2 as Integer
	var s3 = 3 as Integer
	var s4 = 4 as Integer
	var s5 = 5 as Integer
	var i = 0 as Integer
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

```RequiredFunctions
sink
hotCall
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @sink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegReg r8, rcx
    x64.popReg rbp
    x64.ret
}

func @hotCall {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 r12, 2
    x64.movRegImm32 r13, 3
    x64.movRegImm32 r14, 4
    x64.movRegImm32 r15, 5
    x64.movRegImm32 rcx, 0
    x64.jmp whilehdr
  loop:
    x64.callDirect sink
    x64.leaRegRegReg rbx, rbx, r8
    x64.leaRegRegReg r12, r12, r8
    x64.leaRegRegReg r13, r13, r8
    x64.leaRegRegReg r14, r14, r8
    x64.leaRegRegReg r15, r15, r8
    x64.loadRegSlot rax, slot0
    x64.leaRegRegImm32 rcx, rax, 1
  whilehdr:
    x64.storeSlotReg slot0, rcx
    x64.cmpRegImm32 rcx, 5
    x64.jcc less, loop
  whileexit:
    x64.leaRegRegReg rax, rbx, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.leaRegRegReg r8, rax, r15
    x64.epilogue 40
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
    x64.callDirect hotCall
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

func @sink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegReg r8, rcx
    x64.popReg rbp
    x64.ret
}

func @hotCall {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 r12, 2
    x64.movRegImm32 r13, 3
    x64.movRegImm32 r14, 4
    x64.movRegImm32 r15, 5
    x64.movRegImm32 rcx, 0
    x64.jmp whilehdr
  loop:
    x64.callDirect sink
    x64.leaRegRegReg rbx, rbx, r8
    x64.leaRegRegReg r12, r12, r8
    x64.leaRegRegReg r13, r13, r8
    x64.leaRegRegReg r14, r14, r8
    x64.leaRegRegReg r15, r15, r8
    x64.loadRegSlot rax, slot0
    x64.leaRegRegImm32 rcx, rax, 1
  whilehdr:
    x64.storeSlotReg slot0, rcx
    x64.cmpRegImm32 rcx, 5
    x64.jcc less, loop
  whileexit:
    x64.leaRegRegReg rax, rbx, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.leaRegRegReg r8, rax, r15
    x64.epilogue 40
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
    x64.callDirect hotCall
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

func @sink {
  entry:
    arm64.prologue 16
    arm64.epilogue 16
    arm64.ret
}

func @hotCall {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 1
    arm64.movImm x20, 2
    arm64.movImm x21, 3
    arm64.movImm x22, 4
    arm64.movImm x23, 5
    arm64.movImm x24, 0
    arm64.b whilehdr
  loop:
    arm64.movRegReg x0, x24
    arm64.bl sink
    arm64.add x19, x19, x0
    arm64.add x20, x20, x0
    arm64.add x21, x21, x0
    arm64.add x22, x22, x0
    arm64.add x23, x23, x0
    arm64.add x24, x24, 1
  whilehdr:
    arm64.cmp x24, 5
    arm64.b.lt loop
  whileexit:
    arm64.add x0, x19, x20
    arm64.add x0, x0, x21
    arm64.add x0, x0, x22
    arm64.add x0, x0, x23
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.bl hotCall
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
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @sink {
  entry:
    arm64.prologue 16
    arm64.epilogue 16
    arm64.ret
}

func @hotCall {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 1
    arm64.movImm x20, 2
    arm64.movImm x21, 3
    arm64.movImm x22, 4
    arm64.movImm x23, 5
    arm64.movImm x24, 0
    arm64.b whilehdr
  loop:
    arm64.movRegReg x0, x24
    arm64.bl sink
    arm64.add x19, x19, x0
    arm64.add x20, x20, x0
    arm64.add x21, x21, x0
    arm64.add x22, x22, x0
    arm64.add x23, x23, x0
    arm64.add x24, x24, 1
  whilehdr:
    arm64.cmp x24, 5
    arm64.b.lt loop
  whileexit:
    arm64.add x0, x19, x20
    arm64.add x0, x0, x21
    arm64.add x0, x0, x22
    arm64.add x0, x0, x23
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.bl hotCall
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
genuine working set is just `sum` and `i` — two values — so it fits, and on x64 the cold-spill
splitter stores four of the sixteen idle values (`k13`..`k16`, the excess of eighteen values over the
pool of fourteen) before the loop and reloads them after it. The loop body stays exactly
`sum = sum + i; i = i + 1` with NOTHING added (verify in the `TargetIr` pins: the `loop` block is
two `lea`s, falling into the header's `cmp` and `jcc`). arm64's 25-register pool holds all eighteen,
so nothing is split there. No E5001. Result is `sum(0..4)=10 + sum(1..16)=136 = 146`.
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
	var sum = 0 as Integer
	var i = 0 as Integer
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

```RequiredFunctions
rescued
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
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

func @rescued {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
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
    arm64.add x0, x0, 16
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b whilehdr
  loop:
    arm64.add x20, x20, x21
    arm64.add x21, x21, 1
  whilehdr:
    arm64.cmp x21, 5
    arm64.b.lt loop
  whileexit:
    arm64.add x1, x20, x1
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
    arm64.add x0, x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.bl rescued
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
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @rescued {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
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
    arm64.add x0, x0, 16
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b whilehdr
  loop:
    arm64.add x20, x20, x21
    arm64.add x21, x21, 1
  whilehdr:
    arm64.cmp x21, 5
    arm64.b.lt loop
  whileexit:
    arm64.add x1, x20, x1
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
    arm64.add x0, x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.bl rescued
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
	var s1 = 1 as Integer
	var s2 = 2 as Integer
	var s3 = 3 as Integer
	var s4 = 4 as Integer
	var s5 = 5 as Integer
	var s6 = 6 as Integer
	var s7 = 7 as Integer
	var s8 = 8 as Integer
	var s9 = 9 as Integer
	var s10 = 10 as Integer
	var s11 = 11 as Integer
	var s12 = 12 as Integer
	var s13 = 13 as Integer
	var i = 0 as Integer
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

```RequiredFunctions
sink
wide
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @sink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 64
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.storeSlotReg slot0, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 112]
    x64.storeSlotReg slot1, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 120]
    x64.storeSlotReg slot2, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 128]
    x64.storeSlotReg slot3, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 136]
    x64.storeSlotReg slot4, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 144]
    x64.storeSlotReg slot5, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 152]
    x64.storeSlotReg slot6, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 160]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
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
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg r8, rax, r15
    x64.epilogue 64
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @wide {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 328
    x64.storeSlotReg slot12, rcx
    x64.storeSlotReg slot10, rdx
    x64.storeSlotReg slot8, rax
    x64.storeSlotReg slot5, r9
    x64.storeSlotReg slot2, rsi
    x64.storeSlotReg slot1, rdi
    x64.loadRegBaseDisp.word64 rbx, [rbp + 48]
    x64.storeSlotReg slot20, rbx
    x64.loadRegBaseDisp.word64 rax, [rbp + 56]
    x64.storeSlotReg slot18, rax
    x64.loadRegBaseDisp.word64 rcx, [rbp + 64]
    x64.storeSlotReg slot17, rcx
    x64.loadRegBaseDisp.word64 rcx, [rbp + 72]
    x64.storeSlotReg slot15, rcx
    x64.loadRegBaseDisp.word64 rdx, [rbp + 80]
    x64.storeSlotReg slot14, rdx
    x64.loadRegBaseDisp.word64 r12, [rbp + 88]
    x64.storeSlotReg slot7, r12
    x64.loadRegBaseDisp.word64 r13, [rbp + 96]
    x64.storeSlotReg slot4, r13
    x64.loadRegBaseDisp.word64 r14, [rbp + 104]
    x64.storeSlotReg slot0, r14
    x64.loadRegBaseDisp.word64 rdx, [rbp + 112]
    x64.storeSlotReg slot3, rdx
    x64.loadRegBaseDisp.word64 rsi, [rbp + 120]
    x64.storeSlotReg slot6, rsi
    x64.loadRegBaseDisp.word64 r8, [rbp + 128]
    x64.storeSlotReg slot9, r8
    x64.loadRegBaseDisp.word64 r9, [rbp + 136]
    x64.storeSlotReg slot11, r9
    x64.loadRegBaseDisp.word64 r10, [rbp + 144]
    x64.storeSlotReg slot13, r10
    x64.loadRegBaseDisp.word64 r15, [rbp + 152]
    x64.storeSlotReg slot16, r15
    x64.loadRegBaseDisp.word64 r11, [rbp + 160]
    x64.storeSlotReg slot19, r11
    x64.storeBaseDispReg.word64 [rsp + 32], rbx
    x64.storeBaseDispReg.word64 [rsp + 40], rax
    x64.loadRegSlot rax, slot17
    x64.storeBaseDispReg.word64 [rsp + 48], rax
    x64.storeBaseDispReg.word64 [rsp + 56], rcx
    x64.loadRegSlot rax, slot14
    x64.storeBaseDispReg.word64 [rsp + 64], rax
    x64.storeBaseDispReg.word64 [rsp + 72], r12
    x64.storeBaseDispReg.word64 [rsp + 80], r13
    x64.storeBaseDispReg.word64 [rsp + 88], r14
    x64.storeBaseDispReg.word64 [rsp + 96], rdx
    x64.storeBaseDispReg.word64 [rsp + 104], rsi
    x64.storeBaseDispReg.word64 [rsp + 112], r8
    x64.storeBaseDispReg.word64 [rsp + 120], r9
    x64.storeBaseDispReg.word64 [rsp + 128], r10
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.storeBaseDispReg.word64 [rsp + 144], r11
    x64.loadRegSlot rsi, slot2
    x64.loadRegSlot r9, slot5
    x64.loadRegSlot rax, slot8
    x64.loadRegSlot rdx, slot10
    x64.loadRegSlot rcx, slot12
    x64.callDirect sink
    x64.storeSlotReg slot21, r8
    x64.storeBaseDispReg.word64 [rsp + 32], rbx
    x64.loadRegSlot rax, slot18
    x64.storeBaseDispReg.word64 [rsp + 40], rax
    x64.loadRegSlot rax, slot17
    x64.storeBaseDispReg.word64 [rsp + 48], rax
    x64.loadRegSlot rax, slot15
    x64.storeBaseDispReg.word64 [rsp + 56], rax
    x64.loadRegSlot rbx, slot14
    x64.storeBaseDispReg.word64 [rsp + 64], rbx
    x64.storeBaseDispReg.word64 [rsp + 72], r12
    x64.storeBaseDispReg.word64 [rsp + 80], r13
    x64.storeBaseDispReg.word64 [rsp + 88], r14
    x64.loadRegSlot r12, slot3
    x64.storeBaseDispReg.word64 [rsp + 96], r12
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rsp + 104], rax
    x64.loadRegSlot r13, slot9
    x64.storeBaseDispReg.word64 [rsp + 112], r13
    x64.loadRegSlot rax, slot11
    x64.storeBaseDispReg.word64 [rsp + 120], rax
    x64.loadRegSlot rax, slot13
    x64.storeBaseDispReg.word64 [rsp + 128], rax
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.loadRegSlot rax, slot19
    x64.storeBaseDispReg.word64 [rsp + 144], rax
    x64.loadRegSlot rdi, slot1
    x64.loadRegSlot rsi, slot2
    x64.loadRegSlot r9, slot5
    x64.loadRegSlot rax, slot8
    x64.loadRegSlot rdx, slot10
    x64.loadRegSlot rcx, slot12
    x64.callDirect sink
    x64.loadRegSlot rax, slot21
    x64.leaRegRegReg rax, rax, r8
    x64.loadRegSlot rcx, slot12
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot10
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot8
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot20
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot18
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot17
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot15
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, rbx
    x64.loadRegSlot rcx, slot7
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, r12
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, r13
    x64.loadRegSlot rcx, slot11
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot13
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot16
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot19
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 328
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
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 152
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rsi, 1
    x64.movRegImm32 rdi, 1
    x64.movRegImm32 r8, 1
    x64.movRegImm32 r9, 1
    x64.movRegImm32 r10, 1
    x64.movRegImm32 r11, 1
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 r12, 1
    x64.movRegImm32 r13, 1
    x64.movRegImm32 r14, 1
    x64.movRegImm32 r15, 1
    x64.storeBaseDispReg.word64 [rsp + 32], rax
    x64.storeBaseDispReg.word64 [rsp + 40], rcx
    x64.storeBaseDispReg.word64 [rsp + 48], rdx
    x64.storeBaseDispReg.word64 [rsp + 56], rsi
    x64.storeBaseDispReg.word64 [rsp + 64], rdi
    x64.storeBaseDispReg.word64 [rsp + 72], r8
    x64.storeBaseDispReg.word64 [rsp + 80], r9
    x64.storeBaseDispReg.word64 [rsp + 88], r10
    x64.storeBaseDispReg.word64 [rsp + 96], r11
    x64.storeBaseDispReg.word64 [rsp + 104], rbx
    x64.storeBaseDispReg.word64 [rsp + 112], r12
    x64.storeBaseDispReg.word64 [rsp + 120], r13
    x64.storeBaseDispReg.word64 [rsp + 128], r14
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rsp + 144], rax
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 1
    x64.movRegImm32 r9, 1
    x64.movRegImm32 rsi, 1
    x64.movRegImm32 rdi, 1
    x64.callDirect wide
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 152
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at straight-line-wide-signature-compiles.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 152
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

func @sink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 64
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.storeSlotReg slot0, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 112]
    x64.storeSlotReg slot1, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 120]
    x64.storeSlotReg slot2, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 128]
    x64.storeSlotReg slot3, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 136]
    x64.storeSlotReg slot4, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 144]
    x64.storeSlotReg slot5, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 152]
    x64.storeSlotReg slot6, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 160]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
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
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg r8, rax, r15
    x64.epilogue 64
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @wide {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 328
    x64.storeSlotReg slot12, rcx
    x64.storeSlotReg slot10, rdx
    x64.storeSlotReg slot8, rax
    x64.storeSlotReg slot5, r9
    x64.storeSlotReg slot2, rsi
    x64.storeSlotReg slot1, rdi
    x64.loadRegBaseDisp.word64 rbx, [rbp + 48]
    x64.storeSlotReg slot20, rbx
    x64.loadRegBaseDisp.word64 rax, [rbp + 56]
    x64.storeSlotReg slot18, rax
    x64.loadRegBaseDisp.word64 rcx, [rbp + 64]
    x64.storeSlotReg slot17, rcx
    x64.loadRegBaseDisp.word64 rcx, [rbp + 72]
    x64.storeSlotReg slot15, rcx
    x64.loadRegBaseDisp.word64 rdx, [rbp + 80]
    x64.storeSlotReg slot14, rdx
    x64.loadRegBaseDisp.word64 r12, [rbp + 88]
    x64.storeSlotReg slot7, r12
    x64.loadRegBaseDisp.word64 r13, [rbp + 96]
    x64.storeSlotReg slot4, r13
    x64.loadRegBaseDisp.word64 r14, [rbp + 104]
    x64.storeSlotReg slot0, r14
    x64.loadRegBaseDisp.word64 rdx, [rbp + 112]
    x64.storeSlotReg slot3, rdx
    x64.loadRegBaseDisp.word64 rsi, [rbp + 120]
    x64.storeSlotReg slot6, rsi
    x64.loadRegBaseDisp.word64 r8, [rbp + 128]
    x64.storeSlotReg slot9, r8
    x64.loadRegBaseDisp.word64 r9, [rbp + 136]
    x64.storeSlotReg slot11, r9
    x64.loadRegBaseDisp.word64 r10, [rbp + 144]
    x64.storeSlotReg slot13, r10
    x64.loadRegBaseDisp.word64 r15, [rbp + 152]
    x64.storeSlotReg slot16, r15
    x64.loadRegBaseDisp.word64 r11, [rbp + 160]
    x64.storeSlotReg slot19, r11
    x64.storeBaseDispReg.word64 [rsp + 32], rbx
    x64.storeBaseDispReg.word64 [rsp + 40], rax
    x64.loadRegSlot rax, slot17
    x64.storeBaseDispReg.word64 [rsp + 48], rax
    x64.storeBaseDispReg.word64 [rsp + 56], rcx
    x64.loadRegSlot rax, slot14
    x64.storeBaseDispReg.word64 [rsp + 64], rax
    x64.storeBaseDispReg.word64 [rsp + 72], r12
    x64.storeBaseDispReg.word64 [rsp + 80], r13
    x64.storeBaseDispReg.word64 [rsp + 88], r14
    x64.storeBaseDispReg.word64 [rsp + 96], rdx
    x64.storeBaseDispReg.word64 [rsp + 104], rsi
    x64.storeBaseDispReg.word64 [rsp + 112], r8
    x64.storeBaseDispReg.word64 [rsp + 120], r9
    x64.storeBaseDispReg.word64 [rsp + 128], r10
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.storeBaseDispReg.word64 [rsp + 144], r11
    x64.loadRegSlot rsi, slot2
    x64.loadRegSlot r9, slot5
    x64.loadRegSlot rax, slot8
    x64.loadRegSlot rdx, slot10
    x64.loadRegSlot rcx, slot12
    x64.callDirect sink
    x64.storeSlotReg slot21, r8
    x64.storeBaseDispReg.word64 [rsp + 32], rbx
    x64.loadRegSlot rax, slot18
    x64.storeBaseDispReg.word64 [rsp + 40], rax
    x64.loadRegSlot rax, slot17
    x64.storeBaseDispReg.word64 [rsp + 48], rax
    x64.loadRegSlot rax, slot15
    x64.storeBaseDispReg.word64 [rsp + 56], rax
    x64.loadRegSlot rbx, slot14
    x64.storeBaseDispReg.word64 [rsp + 64], rbx
    x64.storeBaseDispReg.word64 [rsp + 72], r12
    x64.storeBaseDispReg.word64 [rsp + 80], r13
    x64.storeBaseDispReg.word64 [rsp + 88], r14
    x64.loadRegSlot r12, slot3
    x64.storeBaseDispReg.word64 [rsp + 96], r12
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rsp + 104], rax
    x64.loadRegSlot r13, slot9
    x64.storeBaseDispReg.word64 [rsp + 112], r13
    x64.loadRegSlot rax, slot11
    x64.storeBaseDispReg.word64 [rsp + 120], rax
    x64.loadRegSlot rax, slot13
    x64.storeBaseDispReg.word64 [rsp + 128], rax
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.loadRegSlot rax, slot19
    x64.storeBaseDispReg.word64 [rsp + 144], rax
    x64.loadRegSlot rdi, slot1
    x64.loadRegSlot rsi, slot2
    x64.loadRegSlot r9, slot5
    x64.loadRegSlot rax, slot8
    x64.loadRegSlot rdx, slot10
    x64.loadRegSlot rcx, slot12
    x64.callDirect sink
    x64.loadRegSlot rax, slot21
    x64.leaRegRegReg rax, rax, r8
    x64.loadRegSlot rcx, slot12
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot10
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot8
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot20
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot18
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot17
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot15
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, rbx
    x64.loadRegSlot rcx, slot7
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, r12
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, r13
    x64.loadRegSlot rcx, slot11
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot13
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot16
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot19
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 328
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
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 152
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rsi, 1
    x64.movRegImm32 rdi, 1
    x64.movRegImm32 r8, 1
    x64.movRegImm32 r9, 1
    x64.movRegImm32 r10, 1
    x64.movRegImm32 r11, 1
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 r12, 1
    x64.movRegImm32 r13, 1
    x64.movRegImm32 r14, 1
    x64.movRegImm32 r15, 1
    x64.storeBaseDispReg.word64 [rsp + 32], rax
    x64.storeBaseDispReg.word64 [rsp + 40], rcx
    x64.storeBaseDispReg.word64 [rsp + 48], rdx
    x64.storeBaseDispReg.word64 [rsp + 56], rsi
    x64.storeBaseDispReg.word64 [rsp + 64], rdi
    x64.storeBaseDispReg.word64 [rsp + 72], r8
    x64.storeBaseDispReg.word64 [rsp + 80], r9
    x64.storeBaseDispReg.word64 [rsp + 88], r10
    x64.storeBaseDispReg.word64 [rsp + 96], r11
    x64.storeBaseDispReg.word64 [rsp + 104], rbx
    x64.storeBaseDispReg.word64 [rsp + 112], r12
    x64.storeBaseDispReg.word64 [rsp + 120], r13
    x64.storeBaseDispReg.word64 [rsp + 128], r14
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rsp + 144], rax
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 1
    x64.movRegImm32 r9, 1
    x64.movRegImm32 rsi, 1
    x64.movRegImm32 rdi, 1
    x64.callDirect wide
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 152
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at straight-line-wide-signature-compiles.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 152
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

<!-- test: hot-loop-param-used-arm64 -->
<!-- unsupported-targets: x64-windows, x64-linux, wasm32-wasi -->
The arm64 twin of `hot-loop-param-used`: a PARAMETER read every iteration is part of the hot working set and must resolve to its declaration span through `ParamOriginTable` (it is minted by no op) rather than trip the Rule-3 panic. `p` is read in `s1 = s1 + i + p`, so with twenty-six accumulators and the counter it is one of twenty-eight values live against arm64's 25-GPR pool. It ranks first (`<fragment>:2:14` — the `p` token); the counter `i` ranks last. Deficit 3. `main` reaches `hot` from two sites so the called-once inliner leaves it a function; spliced into `main`, `p` would fold to a constant and the diagnostic would be a DIFFERENT one — twenty-seven values and deficit 2, with every span pointing into `main`. The x64 twin fits its pool outright when spliced; this one does not, so the second site is what keeps the case pinning `hot`'s own shape rather than what makes it compile.
```maxon
function hot(p Integer) returns Integer
	var s1 = 1 as Integer
	var s2 = 2 as Integer
	var s3 = 3 as Integer
	var s4 = 4 as Integer
	var s5 = 5 as Integer
	var s6 = 6 as Integer
	var s7 = 7 as Integer
	var s8 = 8 as Integer
	var s9 = 9 as Integer
	var s10 = 10 as Integer
	var s11 = 11 as Integer
	var s12 = 12 as Integer
	var s13 = 13 as Integer
	var s14 = 14 as Integer
	var s15 = 15 as Integer
	var s16 = 16 as Integer
	var s17 = 17 as Integer
	var s18 = 18 as Integer
	var s19 = 19 as Integer
	var s20 = 20 as Integer
	var s21 = 21 as Integer
	var s22 = 22 as Integer
	var s23 = 23 as Integer
	var s24 = 24 as Integer
	var s25 = 25 as Integer
	var s26 = 26 as Integer
	var i = 0 as Integer
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
	var s1 = 1 as Integer
	var s2 = 2 as Integer
	var s3 = 3 as Integer
	var s4 = 4 as Integer
	var s5 = 5 as Integer
	var s6 = 6 as Integer
	var s7 = 7 as Integer
	var s8 = 8 as Integer
	var s9 = 9 as Integer
	var s10 = 10 as Integer
	var s11 = 11 as Integer
	var s12 = 12 as Integer
	var s13 = 13 as Integer
	var s14 = 14 as Integer
	var i = 0 as Integer
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
	var s1 = 1 as Integer
	var s2 = 2 as Integer
	var s3 = 3 as Integer
	var s4 = 4 as Integer
	var s5 = 5 as Integer
	var s6 = 6 as Integer
	var s7 = 7 as Integer
	var s8 = 8 as Integer
	var s9 = 9 as Integer
	var s10 = 10 as Integer
	var s11 = 11 as Integer
	var s12 = 12 as Integer
	var s13 = 13 as Integer
	var s14 = 14 as Integer
	var s15 = 15 as Integer
	var s16 = 16 as Integer
	var s17 = 17 as Integer
	var s18 = 18 as Integer
	var s19 = 19 as Integer
	var s20 = 20 as Integer
	var s21 = 21 as Integer
	var s22 = 22 as Integer
	var s23 = 23 as Integer
	var s24 = 24 as Integer
	var s25 = 25 as Integer
	var s26 = 26 as Integer
	var i = 0 as Integer
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
	var pad = 0 as Integer
	while pad < 3 'pad'
		pad = pad + 1
	end 'pad'
	var s1 = 1 as Integer
	var s2 = 2 as Integer
	var s3 = 3 as Integer
	var s4 = 4 as Integer
	var s5 = 5 as Integer
	var s6 = 6 as Integer
	var s7 = 7 as Integer
	var s8 = 8 as Integer
	var s9 = 9 as Integer
	var s10 = 10 as Integer
	var s11 = 11 as Integer
	var s12 = 12 as Integer
	var s13 = 13 as Integer
	var s14 = 14 as Integer
	var s15 = 15 as Integer
	var s16 = 16 as Integer
	var i = 0 as Integer
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
	var acc = 0 as Integer
	var o = 0 as Integer
	while o < 2 'outer'
		var i = 0 as Integer
		var s1 = 1 as Integer
		var s2 = 2 as Integer
		var s3 = 3 as Integer
		var s4 = 4 as Integer
		var s5 = 5 as Integer
		var s6 = 6 as Integer
		var s7 = 7 as Integer
		var s8 = 8 as Integer
		var s9 = 9 as Integer
		var s10 = 10 as Integer
		var s11 = 11 as Integer
		var s12 = 12 as Integer
		var s13 = 13 as Integer
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
The sixteen `k` values and `p` are all idle across the loop, so on x64 the splitter stores five of
them (`p` and `k13`..`k16`, the excess of nineteen values over the pool of fourteen) before the loop
and reloads them after it, and the body stays two `lea`s falling into the header's `cmp` and `jcc`.
arm64's 25-register pool holds all nineteen, so nothing is split there. It compiles and runs. Result is
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
	var sum = 0 as Integer
	var i = 0 as Integer
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

```RequiredFunctions
relievable
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
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

func @relievable {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
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
    arm64.movImm x21, 0
    arm64.movImm x22, 0
    arm64.b whilehdr
  loop:
    arm64.add x21, x21, x22
    arm64.add x22, x22, 1
  whilehdr:
    arm64.cmp x22, 5
    arm64.b.lt loop
  whileexit:
    arm64.add x1, x21, x1
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
    arm64.add x0, x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.bl relievable
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
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @relievable {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
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
    arm64.movImm x21, 0
    arm64.movImm x22, 0
    arm64.b whilehdr
  loop:
    arm64.add x21, x21, x22
    arm64.add x22, x22, 1
  whilehdr:
    arm64.cmp x22, 5
    arm64.b.lt loop
  whileexit:
    arm64.add x1, x21, x1
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
    arm64.add x0, x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.bl relievable
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
it is live at NO program point and no popcount over a live set can see it — yet its load from the
caller's frame CLOBBERS a register whatever becomes of the value, so the colorer must hand it one.
Fourteen live parameters plus that one dead materialization is FIFTEEN registers against x64's pool of
fourteen, at a single op. arm64's pool of 25 holds all fifteen: its pin shows the dead load into `x14`
and no split. `addOpTransientPressure` counts it: without that charge the splitter finds no overflow
at all, declares the function relieved, and `chooseRegister` panics with every register blocked —
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

```RequiredFunctions
f
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @f {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 16
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.storeSlotReg slot0, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 112]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 16
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
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 104
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 3
    x64.movRegImm32 r9, 4
    x64.movRegImm32 rsi, 5
    x64.movRegImm32 rdi, 7
    x64.movRegImm32 r8, 8
    x64.movRegImm32 r10, 9
    x64.movRegImm32 r11, 10
    x64.movRegImm32 rbx, 11
    x64.movRegImm32 r12, 12
    x64.movRegImm32 r13, 13
    x64.movRegImm32 r14, 14
    x64.movRegImm32 r15, 15
    x64.storeBaseDispReg.word64 [rsp + 32], rdi
    x64.storeBaseDispReg.word64 [rsp + 40], r8
    x64.storeBaseDispReg.word64 [rsp + 48], r10
    x64.storeBaseDispReg.word64 [rsp + 56], r11
    x64.storeBaseDispReg.word64 [rsp + 64], rbx
    x64.storeBaseDispReg.word64 [rsp + 72], r12
    x64.storeBaseDispReg.word64 [rsp + 80], r13
    x64.storeBaseDispReg.word64 [rsp + 88], r14
    x64.storeBaseDispReg.word64 [rsp + 96], r15
    x64.movRegImm32 rdi, 6
    x64.callDirect f
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-def-parameter-holds-a-register.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
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

func @f {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 16
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.storeSlotReg slot0, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 112]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 16
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
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 104
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 3
    x64.movRegImm32 r9, 4
    x64.movRegImm32 rsi, 5
    x64.movRegImm32 rdi, 7
    x64.movRegImm32 r8, 8
    x64.movRegImm32 r10, 9
    x64.movRegImm32 r11, 10
    x64.movRegImm32 rbx, 11
    x64.movRegImm32 r12, 12
    x64.movRegImm32 r13, 13
    x64.movRegImm32 r14, 14
    x64.movRegImm32 r15, 15
    x64.storeBaseDispReg.word64 [rsp + 32], rdi
    x64.storeBaseDispReg.word64 [rsp + 40], r8
    x64.storeBaseDispReg.word64 [rsp + 48], r10
    x64.storeBaseDispReg.word64 [rsp + 56], r11
    x64.storeBaseDispReg.word64 [rsp + 64], rbx
    x64.storeBaseDispReg.word64 [rsp + 72], r12
    x64.storeBaseDispReg.word64 [rsp + 80], r13
    x64.storeBaseDispReg.word64 [rsp + 88], r14
    x64.storeBaseDispReg.word64 [rsp + 96], r15
    x64.movRegImm32 rdi, 6
    x64.callDirect f
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-def-parameter-holds-a-register.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
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

func @f {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x8, [x29 + 16]
    arm64.loadRegBaseDisp.word64 x9, [x29 + 24]
    arm64.loadRegBaseDisp.word64 x10, [x29 + 32]
    arm64.loadRegBaseDisp.word64 x11, [x29 + 40]
    arm64.loadRegBaseDisp.word64 x12, [x29 + 48]
    arm64.loadRegBaseDisp.word64 x13, [x29 + 56]
    arm64.loadRegBaseDisp.word64 x14, [x29 + 64]
    arm64.add x0, x0, x1
    arm64.add x0, x0, x2
    arm64.add x0, x0, x3
    arm64.add x0, x0, x4
    arm64.add x0, x0, x5
    arm64.add x0, x0, x6
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x9
    arm64.add x0, x0, x10
    arm64.add x0, x0, x11
    arm64.add x0, x0, x12
    arm64.add x0, x0, x13
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 80 record@56
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.movImm x2, 3
    arm64.movImm x3, 4
    arm64.movImm x4, 5
    arm64.movImm x5, 6
    arm64.movImm x6, 7
    arm64.movImm x7, 8
    arm64.movImm x8, 9
    arm64.movImm x9, 10
    arm64.movImm x10, 11
    arm64.movImm x11, 12
    arm64.movImm x12, 13
    arm64.movImm x13, 14
    arm64.movImm x14, 15
    arm64.storeOutgoingArg [sp + arg0], x8
    arm64.storeOutgoingArg [sp + arg1], x9
    arm64.storeOutgoingArg [sp + arg2], x10
    arm64.storeOutgoingArg [sp + arg3], x11
    arm64.storeOutgoingArg [sp + arg4], x12
    arm64.storeOutgoingArg [sp + arg5], x13
    arm64.storeOutgoingArg [sp + arg6], x14
    arm64.bl f
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 80 record@56
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-def-parameter-holds-a-register.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 80 record@56
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @f {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x8, [x29 + 16]
    arm64.loadRegBaseDisp.word64 x9, [x29 + 24]
    arm64.loadRegBaseDisp.word64 x10, [x29 + 32]
    arm64.loadRegBaseDisp.word64 x11, [x29 + 40]
    arm64.loadRegBaseDisp.word64 x12, [x29 + 48]
    arm64.loadRegBaseDisp.word64 x13, [x29 + 56]
    arm64.loadRegBaseDisp.word64 x14, [x29 + 64]
    arm64.add x0, x0, x1
    arm64.add x0, x0, x2
    arm64.add x0, x0, x3
    arm64.add x0, x0, x4
    arm64.add x0, x0, x5
    arm64.add x0, x0, x6
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x9
    arm64.add x0, x0, x10
    arm64.add x0, x0, x11
    arm64.add x0, x0, x12
    arm64.add x0, x0, x13
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 80 record@56
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.movImm x2, 3
    arm64.movImm x3, 4
    arm64.movImm x4, 5
    arm64.movImm x5, 6
    arm64.movImm x6, 7
    arm64.movImm x7, 8
    arm64.movImm x8, 9
    arm64.movImm x9, 10
    arm64.movImm x10, 11
    arm64.movImm x11, 12
    arm64.movImm x12, 13
    arm64.movImm x13, 14
    arm64.movImm x14, 15
    arm64.storeOutgoingArg [sp + arg0], x8
    arm64.storeOutgoingArg [sp + arg1], x9
    arm64.storeOutgoingArg [sp + arg2], x10
    arm64.storeOutgoingArg [sp + arg3], x11
    arm64.storeOutgoingArg [sp + arg4], x12
    arm64.storeOutgoingArg [sp + arg5], x13
    arm64.storeOutgoingArg [sp + arg6], x14
    arm64.bl f
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 80 record@56
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-def-parameter-holds-a-register.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 80 record@56
    arm64.ret
}
```

<!-- test: dead-def-parameter-at-the-pool-boundary -->
The BOUNDARY below `dead-def-parameter-holds-a-register`, so a regression that moves the cliff is
caught from both sides. Thirteen live parameters plus one dead materialization is exactly x64's pool
of fourteen (arm64's 25 hold them with room to spare): it fits, nothing is split, and the `TargetIr` pin
must show no store and no reload. Result
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

```RequiredFunctions
f
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @f {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg r8, rax, r14
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
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 104
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 3
    x64.movRegImm32 r9, 4
    x64.movRegImm32 rsi, 5
    x64.movRegImm32 rdi, 6
    x64.movRegImm32 r8, 7
    x64.movRegImm32 r10, 8
    x64.movRegImm32 r11, 9
    x64.movRegImm32 rbx, 10
    x64.movRegImm32 r12, 11
    x64.movRegImm32 r13, 12
    x64.movRegImm32 r14, 13
    x64.movRegImm32 r15, 14
    x64.storeBaseDispReg.word64 [rsp + 32], r8
    x64.storeBaseDispReg.word64 [rsp + 40], r10
    x64.storeBaseDispReg.word64 [rsp + 48], r11
    x64.storeBaseDispReg.word64 [rsp + 56], rbx
    x64.storeBaseDispReg.word64 [rsp + 64], r12
    x64.storeBaseDispReg.word64 [rsp + 72], r13
    x64.storeBaseDispReg.word64 [rsp + 80], r14
    x64.storeBaseDispReg.word64 [rsp + 88], r15
    x64.callDirect f
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-def-parameter-at-the-pool-boundary.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
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

func @f {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg r8, rax, r14
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
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 104
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 3
    x64.movRegImm32 r9, 4
    x64.movRegImm32 rsi, 5
    x64.movRegImm32 rdi, 6
    x64.movRegImm32 r8, 7
    x64.movRegImm32 r10, 8
    x64.movRegImm32 r11, 9
    x64.movRegImm32 rbx, 10
    x64.movRegImm32 r12, 11
    x64.movRegImm32 r13, 12
    x64.movRegImm32 r14, 13
    x64.movRegImm32 r15, 14
    x64.storeBaseDispReg.word64 [rsp + 32], r8
    x64.storeBaseDispReg.word64 [rsp + 40], r10
    x64.storeBaseDispReg.word64 [rsp + 48], r11
    x64.storeBaseDispReg.word64 [rsp + 56], rbx
    x64.storeBaseDispReg.word64 [rsp + 64], r12
    x64.storeBaseDispReg.word64 [rsp + 72], r13
    x64.storeBaseDispReg.word64 [rsp + 80], r14
    x64.storeBaseDispReg.word64 [rsp + 88], r15
    x64.callDirect f
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-def-parameter-at-the-pool-boundary.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
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

func @f {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x8, [x29 + 16]
    arm64.loadRegBaseDisp.word64 x9, [x29 + 24]
    arm64.loadRegBaseDisp.word64 x10, [x29 + 32]
    arm64.loadRegBaseDisp.word64 x11, [x29 + 40]
    arm64.loadRegBaseDisp.word64 x12, [x29 + 48]
    arm64.loadRegBaseDisp.word64 x13, [x29 + 56]
    arm64.add x0, x0, x1
    arm64.add x0, x0, x2
    arm64.add x0, x0, x3
    arm64.add x0, x0, x4
    arm64.add x0, x0, x5
    arm64.add x0, x0, x6
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x9
    arm64.add x0, x0, x10
    arm64.add x0, x0, x11
    arm64.add x0, x0, x12
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 64 record@48
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.movImm x2, 3
    arm64.movImm x3, 4
    arm64.movImm x4, 5
    arm64.movImm x5, 6
    arm64.movImm x6, 7
    arm64.movImm x7, 8
    arm64.movImm x8, 9
    arm64.movImm x9, 10
    arm64.movImm x10, 11
    arm64.movImm x11, 12
    arm64.movImm x12, 13
    arm64.movImm x13, 14
    arm64.storeOutgoingArg [sp + arg0], x8
    arm64.storeOutgoingArg [sp + arg1], x9
    arm64.storeOutgoingArg [sp + arg2], x10
    arm64.storeOutgoingArg [sp + arg3], x11
    arm64.storeOutgoingArg [sp + arg4], x12
    arm64.storeOutgoingArg [sp + arg5], x13
    arm64.bl f
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 64 record@48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-def-parameter-at-the-pool-boundary.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 64 record@48
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @f {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x8, [x29 + 16]
    arm64.loadRegBaseDisp.word64 x9, [x29 + 24]
    arm64.loadRegBaseDisp.word64 x10, [x29 + 32]
    arm64.loadRegBaseDisp.word64 x11, [x29 + 40]
    arm64.loadRegBaseDisp.word64 x12, [x29 + 48]
    arm64.loadRegBaseDisp.word64 x13, [x29 + 56]
    arm64.add x0, x0, x1
    arm64.add x0, x0, x2
    arm64.add x0, x0, x3
    arm64.add x0, x0, x4
    arm64.add x0, x0, x5
    arm64.add x0, x0, x6
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x9
    arm64.add x0, x0, x10
    arm64.add x0, x0, x11
    arm64.add x0, x0, x12
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 64 record@48
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.movImm x2, 3
    arm64.movImm x3, 4
    arm64.movImm x4, 5
    arm64.movImm x5, 6
    arm64.movImm x6, 7
    arm64.movImm x7, 8
    arm64.movImm x8, 9
    arm64.movImm x9, 10
    arm64.movImm x10, 11
    arm64.movImm x11, 12
    arm64.movImm x12, 13
    arm64.movImm x13, 14
    arm64.storeOutgoingArg [sp + arg0], x8
    arm64.storeOutgoingArg [sp + arg1], x9
    arm64.storeOutgoingArg [sp + arg2], x10
    arm64.storeOutgoingArg [sp + arg3], x11
    arm64.storeOutgoingArg [sp + arg4], x12
    arm64.storeOutgoingArg [sp + arg5], x13
    arm64.bl f
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 64 record@48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-def-parameter-at-the-pool-boundary.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 64 record@48
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

```RequiredFunctions
f
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @f {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 112
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.storeSlotReg slot0, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 112]
    x64.storeSlotReg slot1, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 120]
    x64.storeSlotReg slot2, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 128]
    x64.storeSlotReg slot3, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 136]
    x64.storeSlotReg slot4, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 144]
    x64.storeSlotReg slot5, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 152]
    x64.storeSlotReg slot6, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 160]
    x64.storeSlotReg slot7, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 168]
    x64.storeSlotReg slot8, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 176]
    x64.storeSlotReg slot9, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 184]
    x64.storeSlotReg slot10, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 192]
    x64.storeSlotReg slot11, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 200]
    x64.storeSlotReg slot12, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 208]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
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
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot7
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot8
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot9
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot10
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot11
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot12
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 112
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
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 200
    x64.movRegImm32 rax, 7
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 9
    x64.movRegImm32 rsi, 10
    x64.movRegImm32 rdi, 11
    x64.movRegImm32 r8, 12
    x64.movRegImm32 r9, 13
    x64.movRegImm32 r10, 14
    x64.movRegImm32 r11, 15
    x64.movRegImm32 rbx, 16
    x64.movRegImm32 r12, 17
    x64.movRegImm32 r13, 18
    x64.movRegImm32 r14, 19
    x64.movRegImm32 r15, 20
    x64.storeBaseDispReg.word64 [rsp + 32], rax
    x64.storeBaseDispReg.word64 [rsp + 40], rcx
    x64.storeBaseDispReg.word64 [rsp + 48], rdx
    x64.storeBaseDispReg.word64 [rsp + 56], rsi
    x64.storeBaseDispReg.word64 [rsp + 64], rdi
    x64.storeBaseDispReg.word64 [rsp + 72], r8
    x64.storeBaseDispReg.word64 [rsp + 80], r9
    x64.storeBaseDispReg.word64 [rsp + 88], r10
    x64.storeBaseDispReg.word64 [rsp + 96], r11
    x64.storeBaseDispReg.word64 [rsp + 104], rbx
    x64.storeBaseDispReg.word64 [rsp + 112], r12
    x64.storeBaseDispReg.word64 [rsp + 120], r13
    x64.storeBaseDispReg.word64 [rsp + 128], r14
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 144], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 152], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 160], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 168], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 176], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 184], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 192], rax
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 3
    x64.movRegImm32 r9, 4
    x64.movRegImm32 rsi, 5
    x64.movRegImm32 rdi, 6
    x64.callDirect f
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 200
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-def-parameter-past-the-arm64-pool.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 200
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

func @f {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 112
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.storeSlotReg slot0, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 112]
    x64.storeSlotReg slot1, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 120]
    x64.storeSlotReg slot2, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 128]
    x64.storeSlotReg slot3, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 136]
    x64.storeSlotReg slot4, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 144]
    x64.storeSlotReg slot5, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 152]
    x64.storeSlotReg slot6, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 160]
    x64.storeSlotReg slot7, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 168]
    x64.storeSlotReg slot8, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 176]
    x64.storeSlotReg slot9, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 184]
    x64.storeSlotReg slot10, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 192]
    x64.storeSlotReg slot11, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 200]
    x64.storeSlotReg slot12, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 208]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
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
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot7
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot8
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot9
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot10
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot11
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot12
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 112
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
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 200
    x64.movRegImm32 rax, 7
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 9
    x64.movRegImm32 rsi, 10
    x64.movRegImm32 rdi, 11
    x64.movRegImm32 r8, 12
    x64.movRegImm32 r9, 13
    x64.movRegImm32 r10, 14
    x64.movRegImm32 r11, 15
    x64.movRegImm32 rbx, 16
    x64.movRegImm32 r12, 17
    x64.movRegImm32 r13, 18
    x64.movRegImm32 r14, 19
    x64.movRegImm32 r15, 20
    x64.storeBaseDispReg.word64 [rsp + 32], rax
    x64.storeBaseDispReg.word64 [rsp + 40], rcx
    x64.storeBaseDispReg.word64 [rsp + 48], rdx
    x64.storeBaseDispReg.word64 [rsp + 56], rsi
    x64.storeBaseDispReg.word64 [rsp + 64], rdi
    x64.storeBaseDispReg.word64 [rsp + 72], r8
    x64.storeBaseDispReg.word64 [rsp + 80], r9
    x64.storeBaseDispReg.word64 [rsp + 88], r10
    x64.storeBaseDispReg.word64 [rsp + 96], r11
    x64.storeBaseDispReg.word64 [rsp + 104], rbx
    x64.storeBaseDispReg.word64 [rsp + 112], r12
    x64.storeBaseDispReg.word64 [rsp + 120], r13
    x64.storeBaseDispReg.word64 [rsp + 128], r14
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 144], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 152], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 160], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 168], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 176], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 184], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 192], rax
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 3
    x64.movRegImm32 r9, 4
    x64.movRegImm32 rsi, 5
    x64.movRegImm32 rdi, 6
    x64.callDirect f
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 200
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-def-parameter-past-the-arm64-pool.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 200
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

func @f {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.loadRegBaseDisp.word64 x8, [x29 + 112]
    arm64.loadRegBaseDisp.word64 x9, [x29 + 120]
    arm64.loadRegBaseDisp.word64 x10, [x29 + 128]
    arm64.loadRegBaseDisp.word64 x11, [x29 + 136]
    arm64.loadRegBaseDisp.word64 x12, [x29 + 144]
    arm64.loadRegBaseDisp.word64 x13, [x29 + 152]
    arm64.loadRegBaseDisp.word64 x14, [x29 + 160]
    arm64.loadRegBaseDisp.word64 x19, [x29 + 168]
    arm64.loadRegBaseDisp.word64 x20, [x29 + 176]
    arm64.loadRegBaseDisp.word64 x21, [x29 + 184]
    arm64.loadRegBaseDisp.word64 x22, [x29 + 192]
    arm64.loadRegBaseDisp.word64 x23, [x29 + 200]
    arm64.loadRegBaseDisp.word64 x24, [x29 + 208]
    arm64.loadRegBaseDisp.word64 x25, [x29 + 216]
    arm64.loadRegBaseDisp.word64 x26, [x29 + 224]
    arm64.loadRegBaseDisp.word64 x27, [x29 + 232]
    arm64.loadRegBaseDisp.word64 x28, [x29 + 240]
    arm64.storeSlotReg slot0, x28
    arm64.loadRegBaseDisp.word64 x28, [x29 + 248]
    arm64.storeSlotReg slot1, x28
    arm64.loadRegBaseDisp.word64 x28, [x29 + 256]
    arm64.add x0, x0, x1
    arm64.add x0, x0, x2
    arm64.add x0, x0, x3
    arm64.add x0, x0, x4
    arm64.add x0, x0, x5
    arm64.add x0, x0, x6
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x9
    arm64.add x0, x0, x10
    arm64.add x0, x0, x11
    arm64.add x0, x0, x12
    arm64.add x0, x0, x13
    arm64.add x0, x0, x14
    arm64.add x0, x0, x19
    arm64.add x0, x0, x20
    arm64.add x0, x0, x21
    arm64.add x0, x0, x22
    arm64.add x0, x0, x23
    arm64.add x0, x0, x24
    arm64.add x0, x0, x25
    arm64.add x0, x0, x26
    arm64.add x0, x0, x27
    arm64.loadRegSlot x1, slot0
    arm64.add x0, x0, x1
    arm64.loadRegSlot x1, slot1
    arm64.add x0, x0, x1
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 256 record@152
    arm64.storeSlotReg slot9, x28
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.movImm x2, 3
    arm64.movImm x3, 4
    arm64.movImm x4, 5
    arm64.movImm x5, 6
    arm64.movImm x6, 9
    arm64.movImm x7, 10
    arm64.movImm x8, 11
    arm64.movImm x9, 12
    arm64.movImm x10, 13
    arm64.movImm x11, 14
    arm64.movImm x12, 15
    arm64.movImm x13, 16
    arm64.movImm x14, 17
    arm64.movImm x19, 18
    arm64.movImm x20, 19
    arm64.movImm x21, 20
    arm64.movImm x22, 0
    arm64.movImm x23, 0
    arm64.movImm x24, 0
    arm64.movImm x25, 0
    arm64.movImm x26, 0
    arm64.movImm x27, 0
    arm64.movImm x28, 0
    arm64.storeOutgoingArg [sp + arg0], x6
    arm64.storeOutgoingArg [sp + arg1], x7
    arm64.storeOutgoingArg [sp + arg2], x8
    arm64.storeOutgoingArg [sp + arg3], x9
    arm64.storeOutgoingArg [sp + arg4], x10
    arm64.storeOutgoingArg [sp + arg5], x11
    arm64.storeOutgoingArg [sp + arg6], x12
    arm64.storeOutgoingArg [sp + arg7], x13
    arm64.storeOutgoingArg [sp + arg8], x14
    arm64.storeOutgoingArg [sp + arg9], x19
    arm64.storeOutgoingArg [sp + arg10], x20
    arm64.storeOutgoingArg [sp + arg11], x21
    arm64.storeOutgoingArg [sp + arg12], x22
    arm64.storeOutgoingArg [sp + arg13], x23
    arm64.storeOutgoingArg [sp + arg14], x24
    arm64.storeOutgoingArg [sp + arg15], x25
    arm64.storeOutgoingArg [sp + arg16], x26
    arm64.storeOutgoingArg [sp + arg17], x27
    arm64.storeOutgoingArg [sp + arg18], x28
    arm64.movImm x6, 7
    arm64.movImm x7, 8
    arm64.bl f
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
    arm64.epilogue 256 record@152
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-def-parameter-past-the-arm64-pool.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
    arm64.epilogue 256 record@152
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @f {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.loadRegBaseDisp.word64 x8, [x29 + 112]
    arm64.loadRegBaseDisp.word64 x9, [x29 + 120]
    arm64.loadRegBaseDisp.word64 x10, [x29 + 128]
    arm64.loadRegBaseDisp.word64 x11, [x29 + 136]
    arm64.loadRegBaseDisp.word64 x12, [x29 + 144]
    arm64.loadRegBaseDisp.word64 x13, [x29 + 152]
    arm64.loadRegBaseDisp.word64 x14, [x29 + 160]
    arm64.loadRegBaseDisp.word64 x19, [x29 + 168]
    arm64.loadRegBaseDisp.word64 x20, [x29 + 176]
    arm64.loadRegBaseDisp.word64 x21, [x29 + 184]
    arm64.loadRegBaseDisp.word64 x22, [x29 + 192]
    arm64.loadRegBaseDisp.word64 x23, [x29 + 200]
    arm64.loadRegBaseDisp.word64 x24, [x29 + 208]
    arm64.loadRegBaseDisp.word64 x25, [x29 + 216]
    arm64.loadRegBaseDisp.word64 x26, [x29 + 224]
    arm64.loadRegBaseDisp.word64 x27, [x29 + 232]
    arm64.loadRegBaseDisp.word64 x28, [x29 + 240]
    arm64.storeSlotReg slot0, x28
    arm64.loadRegBaseDisp.word64 x28, [x29 + 248]
    arm64.storeSlotReg slot1, x28
    arm64.loadRegBaseDisp.word64 x28, [x29 + 256]
    arm64.add x0, x0, x1
    arm64.add x0, x0, x2
    arm64.add x0, x0, x3
    arm64.add x0, x0, x4
    arm64.add x0, x0, x5
    arm64.add x0, x0, x6
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x9
    arm64.add x0, x0, x10
    arm64.add x0, x0, x11
    arm64.add x0, x0, x12
    arm64.add x0, x0, x13
    arm64.add x0, x0, x14
    arm64.add x0, x0, x19
    arm64.add x0, x0, x20
    arm64.add x0, x0, x21
    arm64.add x0, x0, x22
    arm64.add x0, x0, x23
    arm64.add x0, x0, x24
    arm64.add x0, x0, x25
    arm64.add x0, x0, x26
    arm64.add x0, x0, x27
    arm64.loadRegSlot x1, slot0
    arm64.add x0, x0, x1
    arm64.loadRegSlot x1, slot1
    arm64.add x0, x0, x1
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 256 record@152
    arm64.storeSlotReg slot9, x28
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.movImm x2, 3
    arm64.movImm x3, 4
    arm64.movImm x4, 5
    arm64.movImm x5, 6
    arm64.movImm x6, 9
    arm64.movImm x7, 10
    arm64.movImm x8, 11
    arm64.movImm x9, 12
    arm64.movImm x10, 13
    arm64.movImm x11, 14
    arm64.movImm x12, 15
    arm64.movImm x13, 16
    arm64.movImm x14, 17
    arm64.movImm x19, 18
    arm64.movImm x20, 19
    arm64.movImm x21, 20
    arm64.movImm x22, 0
    arm64.movImm x23, 0
    arm64.movImm x24, 0
    arm64.movImm x25, 0
    arm64.movImm x26, 0
    arm64.movImm x27, 0
    arm64.movImm x28, 0
    arm64.storeOutgoingArg [sp + arg0], x6
    arm64.storeOutgoingArg [sp + arg1], x7
    arm64.storeOutgoingArg [sp + arg2], x8
    arm64.storeOutgoingArg [sp + arg3], x9
    arm64.storeOutgoingArg [sp + arg4], x10
    arm64.storeOutgoingArg [sp + arg5], x11
    arm64.storeOutgoingArg [sp + arg6], x12
    arm64.storeOutgoingArg [sp + arg7], x13
    arm64.storeOutgoingArg [sp + arg8], x14
    arm64.storeOutgoingArg [sp + arg9], x19
    arm64.storeOutgoingArg [sp + arg10], x20
    arm64.storeOutgoingArg [sp + arg11], x21
    arm64.storeOutgoingArg [sp + arg12], x22
    arm64.storeOutgoingArg [sp + arg13], x23
    arm64.storeOutgoingArg [sp + arg14], x24
    arm64.storeOutgoingArg [sp + arg15], x25
    arm64.storeOutgoingArg [sp + arg16], x26
    arm64.storeOutgoingArg [sp + arg17], x27
    arm64.storeOutgoingArg [sp + arg18], x28
    arm64.movImm x6, 7
    arm64.movImm x7, 8
    arm64.bl f
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
    arm64.epilogue 256 record@152
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-def-parameter-past-the-arm64-pool.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
    arm64.epilogue 256 record@152
    arm64.ret
}
```

<!-- test: reuse-def-confined-by-a-later-call -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
A REUSE DEF is confined by its OWN admissible set, and the pressure analysis has to see it.

Two sequential loops over an `Array`, the first carrying a `carry` across its back edge. `keep` is
named in the `RequiredFunctions` block, which keeps it out of line, so each of its two calls in the
first loop is a real call, in a `normal` block, so it confines (the managed accesses' own slow arms
are cold and confine nothing; see `cold-call-spilling`). `limbs`, `s`, `n`, `i` and `carry` are live
across the first call, and `limbs`, `s`, `n`, `i` and `next` across the second: five at each call,
confined to the five callee-saved GPRs, so no call point overflows. The second call is the one that
confines `next`. `next = widened shr 32` is a two-address reuse def whose input `widened` dies
there, and `carry` is still live, read afterwards by `low or carry`, so from the shift to that `or`
`next` is a SIXTH contender for the five. `widened` crosses no call, so `next` cannot count on
inheriting its register: inheriting the input's register is only free when every register the input
may hold is one the def may hold too. The live set entering the shift holds the five and not `next`,
so the confined census counts `next` at the shift through `opConfinedTransient`, the rule this case
pins.

Both x64 pins show the reuse def and its relief. `widened` is in the caller-saved `r8`, so `next` is
a copy + `sarRegImm8 r15, r8, 32` into `r15`, and it crosses the second `callDirect keep` there,
beside `rbx` (`limbs`), `r12` (`s`) and `r14` (`i`). `n` is stored to a spill slot at entry and
reloaded only by the clear loop, which leaves `r15` free for `next`. A back-edge `movRegReg r13, r15`
moves `next` into `carry`'s register. arm64 `asr` is three-address, so arm64 has no reuse def here,
and its ten callee-saved GPRs hold the six values live across the loop's calls: the case does not run
there.

`0xFFFFFFFF << 4` is `0xFFFFFFFF0`, so limb 0 keeps `0xFFFFFFF0` and carries `0xF` into limb 1,
which holds `1 << 4 | 0xF` = **31**. The clear loop then zeroes limb 0 only.
```maxon
typealias Limb = int(i64.min to i64.max)
typealias Limbs = Array with Limb

function keep(x Limb) returns Limb
	if x < 0 'never'
		print("")
	end 'never'
	return x
end 'keep'

function shift(limbs Limbs, s Limb, n ElementIndex)
	var carry = 0 as Limb
	var i = 0 as ElementIndex
	while i < limbs.count() 'shiftEach'
		let widened = keep(try limbs.get(i) otherwise panic("get")) shl s
		let low = widened and 0xFFFFFFFF
		let next = widened shr 32
		try limbs.set(i, value: keep(low or carry)) otherwise panic("set")
		carry = next
		i = i + 1
	end 'shiftEach'

	var v = 0 as ElementIndex
	while v < n 'clear'
		try limbs.set(v, value: 0) otherwise panic("clear")
		v = v + 1
	end 'clear'
end 'shift'

function main() returns ExitCode
	let limbs = Limbs.create(0)
	limbs.push(0xFFFFFFFF)
	limbs.push(1)
	shift(limbs, s: 4, n: 1)
	return try limbs.get(1) otherwise 99
end 'main'
```
```exitcode
31
```

```RequiredFunctions
keep
shift
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

func @keep {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc greaterEqual, ifcont
  never:
    x64.leaRegRdata rcx, [rip + __str_rec_1]  ; ""
  __il_body:
    x64.callDirect __write_stdout
  ifcont:
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @shift {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.storeSlotReg slot0, rax
    x64.movRegImm32 r13, 0
    x64.movRegImm32 r14, 0
    x64.jmp whilehdr#1
  shiftEach:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r14, rax
    x64.jcc aboveEqual, __im_slow#16
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 rcx, [rax + r14*8 + 0]
  tryok#4:
    x64.callDirect keep
    x64.cmpRegImm32 r12, 0
    x64.jcc less, shiftneg
  shiftok:
    x64.movRegReg rax, r12
    x64.andRegImm32 rax, r12, -64
    x64.movRegReg rcx, rax
    x64.negReg rcx, rax
    x64.orRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.sarRegImm8 rax, rcx, 63
    x64.movRegReg rcx, r12
    x64.shlRegCl r8, r8
    x64.notReg rax, rax
    x64.andRegReg r8, r8, rax
    x64.movRegImm32 rax, 4294967295
    x64.movRegReg rdx, r8
    x64.andRegReg rdx, r8, rax
    x64.movRegReg r15, r8
    x64.sarRegImm8 r15, r8, 32
    x64.orRegReg rdx, rdx, r13
    x64.movRegReg rcx, rdx
    x64.callDirect keep
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r14, rax
    x64.jcc aboveEqual, __im_slow#19
  __im_buffer#20:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_missing
  __im_owned#23:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_detach#22
  __im_viewed#24:
    x64.leaRegRegImm32 rcx, rax, -16
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __im_detach#22
  __im_detached#25:
    x64.storeBaseIndexScaleReg.word64 [rax + r14*8 + 0], r8
  tryok#8:
    x64.leaRegRegImm32 r14, r14, 1
    x64.movRegReg r13, r15
  whilehdr#1:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r14, rax
    x64.jcc below, shiftEach
  whileexit#3:
    x64.movRegImm32 rdx, 0
  __us_test#43:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, whilehdr#10
  __us_test#44:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, whilehdr#10
  __us_test#45:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, whilehdr#10
    x64.jmp whilehdr#33
  clear#11:
    x64.movRegImm32 rax, 0
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow#27
  __im_buffer#28:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing
  __im_owned#30:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#29
  __im_viewed#31:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#29
  __im_detached#32:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  tryok#13:
    x64.leaRegRegImm32 rdx, rdx, 1
  whilehdr#10:
    x64.loadRegSlot rax, slot0
    x64.cmpRegReg rdx, rax
    x64.jcc below, clear#11
    x64.jmp whileexit#12
  clear#42:
    x64.movRegImm32 rsi, 0
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#36
  __im_detached#37:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rsi
  tryok#34:
    x64.leaRegRegImm32 rdx, rdx, 1
  whilehdr#33:
    x64.loadRegSlot rsi, slot0
    x64.cmpRegReg rdx, rsi
    x64.jcc below, clear#42
  whileexit#12:
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#16:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#5
  critsplit:
    x64.movRegReg rcx, r8
    x64.jmp tryok#4
  __im_slow#19:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#9
    x64.jmp tryok#8
  __im_detach#22:
    x64.storeSlotReg slot1, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot1
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jmp __im_detached#25
  __im_slow#27:
    x64.storeSlotReg slot2, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rdx, slot2
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#14
    x64.jmp tryok#13
  __im_detach#29:
    x64.storeSlotReg slot2, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 0
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#32
  __im_slow#36:
    x64.storeSlotReg slot3, rax
    x64.storeSlotReg slot4, rcx
    x64.storeSlotReg slot2, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rcx, slot4
    x64.loadRegSlot rdx, slot2
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#14
    x64.jmp tryok#34
  tryerr#5:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at reuse-def-confined-by-a-later-call.test:16: get\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  shiftneg:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at reuse-def-confined-by-a-later-call.test:16: negative shift count\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#9:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at reuse-def-confined-by-a-later-call.test:19: set\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_10]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#14:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at reuse-def-confined-by-a-later-call.test:26: clear\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 72
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
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
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
    x64.movRegImm32 rdx, 4
    x64.movRegImm32 rax, 1
    x64.movRegReg rcx, rbx
    x64.callDirect shift
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 r12, 99
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  tryok:
    x64.movRegReg r12, rax
  trycont:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  critsplit:
    x64.movRegReg rax, r8
    x64.jmp tryok
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "panic at reuse-def-confined-by-a-later-call.test:36: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
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

func @keep {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc greaterEqual, ifcont
  never:
    x64.leaRegRdata rcx, [rip + __str_rec_1]  ; ""
  __il_body:
    x64.callDirect __write_stdout
  ifcont:
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @shift {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.storeSlotReg slot0, rax
    x64.movRegImm32 r13, 0
    x64.movRegImm32 r14, 0
    x64.jmp whilehdr#1
  shiftEach:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r14, rax
    x64.jcc aboveEqual, __im_slow#16
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 rcx, [rax + r14*8 + 0]
  tryok#4:
    x64.callDirect keep
    x64.cmpRegImm32 r12, 0
    x64.jcc less, shiftneg
  shiftok:
    x64.movRegReg rax, r12
    x64.andRegImm32 rax, r12, -64
    x64.movRegReg rcx, rax
    x64.negReg rcx, rax
    x64.orRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.sarRegImm8 rax, rcx, 63
    x64.movRegReg rcx, r12
    x64.shlRegCl r8, r8
    x64.notReg rax, rax
    x64.andRegReg r8, r8, rax
    x64.movRegImm32 rax, 4294967295
    x64.movRegReg rdx, r8
    x64.andRegReg rdx, r8, rax
    x64.movRegReg r15, r8
    x64.sarRegImm8 r15, r8, 32
    x64.orRegReg rdx, rdx, r13
    x64.movRegReg rcx, rdx
    x64.callDirect keep
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r14, rax
    x64.jcc aboveEqual, __im_slow#19
  __im_buffer#20:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_missing
  __im_owned#23:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_detach#22
  __im_viewed#24:
    x64.leaRegRegImm32 rcx, rax, -16
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __im_detach#22
  __im_detached#25:
    x64.storeBaseIndexScaleReg.word64 [rax + r14*8 + 0], r8
  tryok#8:
    x64.leaRegRegImm32 r14, r14, 1
    x64.movRegReg r13, r15
  whilehdr#1:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r14, rax
    x64.jcc below, shiftEach
  whileexit#3:
    x64.movRegImm32 rdx, 0
  __us_test#43:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, whilehdr#10
  __us_test#44:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, whilehdr#10
  __us_test#45:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, whilehdr#10
    x64.jmp whilehdr#33
  clear#11:
    x64.movRegImm32 rax, 0
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow#27
  __im_buffer#28:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing
  __im_owned#30:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#29
  __im_viewed#31:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#29
  __im_detached#32:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  tryok#13:
    x64.leaRegRegImm32 rdx, rdx, 1
  whilehdr#10:
    x64.loadRegSlot rax, slot0
    x64.cmpRegReg rdx, rax
    x64.jcc below, clear#11
    x64.jmp whileexit#12
  clear#42:
    x64.movRegImm32 rsi, 0
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#36
  __im_detached#37:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rsi
  tryok#34:
    x64.leaRegRegImm32 rdx, rdx, 1
  whilehdr#33:
    x64.loadRegSlot rsi, slot0
    x64.cmpRegReg rdx, rsi
    x64.jcc below, clear#42
  whileexit#12:
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#16:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#5
  critsplit:
    x64.movRegReg rcx, r8
    x64.jmp tryok#4
  __im_slow#19:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#9
    x64.jmp tryok#8
  __im_detach#22:
    x64.storeSlotReg slot1, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot1
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jmp __im_detached#25
  __im_slow#27:
    x64.storeSlotReg slot2, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rdx, slot2
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#14
    x64.jmp tryok#13
  __im_detach#29:
    x64.storeSlotReg slot2, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 0
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#32
  __im_slow#36:
    x64.storeSlotReg slot3, rax
    x64.storeSlotReg slot4, rcx
    x64.storeSlotReg slot2, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rcx, slot4
    x64.loadRegSlot rdx, slot2
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#14
    x64.jmp tryok#34
  tryerr#5:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at reuse-def-confined-by-a-later-call.test:16: get\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  shiftneg:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at reuse-def-confined-by-a-later-call.test:16: negative shift count\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#9:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at reuse-def-confined-by-a-later-call.test:19: set\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_10]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#14:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at reuse-def-confined-by-a-later-call.test:26: clear\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 72
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
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
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
    x64.movRegImm32 rdx, 4
    x64.movRegImm32 rax, 1
    x64.movRegReg rcx, rbx
    x64.callDirect shift
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 r12, 99
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  tryok:
    x64.movRegReg r12, rax
  trycont:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  critsplit:
    x64.movRegReg rax, r8
    x64.jmp tryok
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "panic at reuse-def-confined-by-a-later-call.test:36: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

<!-- test: confined-reuse-defs-past-the-callee-saved-half -->
⛔⛔ **THE SAME REUSE-DEF CONFINEMENT AS ABOVE, IN STRAIGHT-LINE CODE.**

Eleven ints cross the `m = sink(n)` call: `a0`..`a9`, read again by `s`, and `q`, read by the
multiply. `sink` is named in the `RequiredFunctions` block, so it stays out of line and its result `m`
is an unknown to the folder. `b = w * q` crosses the `c = sink(s)` call, so it may hold only
callee-saved registers. On x64 the multiply is a two-address `imulRegReg` reuse def over `w`,
and `w = m + 1` crosses no call, so `b` cannot count on inheriting its register. At the multiply `b`
therefore competes with every value still holding a callee-saved register from the `m = sink(n)`
call, `q` among them: `q` is read by the multiply itself and dies there. The live set entering the
multiply holds `q` and not `b`, and the one leaving it holds `b` and not `q`, so no live set holds
both; `opConfinedTransient` counts `b` at the op.

On x64 the `callDirect sink` of `m = sink(n)` holds eleven values against the five callee-saved
GPRs, so six must leave registers there, and the multiply costs a seventh. `a0`..`a2` cross that call
in `rbx`, `r12` and `r13` and `q` in `r14`; `a3`..`a9` cross it in `slot6`..`slot0`. That leaves
`r15` for the multiply: `w` is defined into it, `imulRegReg r15, r15, r14` computes `b` in place, and
`b` crosses the `c = sink(s)` call there.

arm64 `mul` is three-address, so arm64 has no reuse def here: `mul x28, x0, x28` writes `b` into the
register `q` vacates. Its pin shows arm64's own confinement, at the `bl sink` of `m = sink(n)`:
eleven values against ten callee-saved GPRs, so `a0`..`a8` cross it in `x19`..`x27` and `q` in
`x28`, and `a9` crosses it in `slot0`.

`n = 4`, so `a0`..`a9` are `5`..`14` summing to `95`; `q = 15`, `m = 5`, `w = 6`, `b = 90` and
`c = 96`. Total `186`.
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
	let q = n + 11
	let m = sink(n)
	let w = m + 1
	let b = w * q
	let s = a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9
	let c = sink(s)
	let total = b + c
	return 0 if total == 186 else 99
end 'main'
```
```exitcode
0
```

```RequiredFunctions
sink
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @sink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegRegImm32 r8, rcx, 1
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
    x64.prologue 88
    x64.movRegImm32 rcx, 3
    x64.callDirect sink
    x64.leaRegRegImm32 rbx, r8, 1
    x64.leaRegRegImm32 r12, r8, 2
    x64.leaRegRegImm32 r13, r8, 3
    x64.leaRegRegImm32 rax, r8, 4
    x64.storeSlotReg slot6, rax
    x64.leaRegRegImm32 rax, r8, 5
    x64.storeSlotReg slot5, rax
    x64.leaRegRegImm32 rax, r8, 6
    x64.storeSlotReg slot4, rax
    x64.leaRegRegImm32 rax, r8, 7
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rax, r8, 8
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rax, r8, 9
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rax, r8, 10
    x64.storeSlotReg slot0, rax
    x64.leaRegRegImm32 r14, r8, 11
    x64.movRegReg rcx, r8
    x64.callDirect sink
    x64.leaRegRegImm32 r15, r8, 1
    x64.imulRegReg r15, r15, r14
    x64.leaRegRegReg rax, rbx, r12
    x64.leaRegRegReg rax, rax, r13
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rcx, rax, rcx
    x64.callDirect sink
    x64.leaRegRegReg rax, r15, r8
    x64.cmpRegImm32 rax, 186
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp matchcont
  ternaryfalse:
    x64.movRegImm32 r8, 99
  matchcont:
    x64.epilogue 88
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

func @sink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegRegImm32 r8, rcx, 1
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
    x64.prologue 88
    x64.movRegImm32 rcx, 3
    x64.callDirect sink
    x64.leaRegRegImm32 rbx, r8, 1
    x64.leaRegRegImm32 r12, r8, 2
    x64.leaRegRegImm32 r13, r8, 3
    x64.leaRegRegImm32 rax, r8, 4
    x64.storeSlotReg slot6, rax
    x64.leaRegRegImm32 rax, r8, 5
    x64.storeSlotReg slot5, rax
    x64.leaRegRegImm32 rax, r8, 6
    x64.storeSlotReg slot4, rax
    x64.leaRegRegImm32 rax, r8, 7
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rax, r8, 8
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rax, r8, 9
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rax, r8, 10
    x64.storeSlotReg slot0, rax
    x64.leaRegRegImm32 r14, r8, 11
    x64.movRegReg rcx, r8
    x64.callDirect sink
    x64.leaRegRegImm32 r15, r8, 1
    x64.imulRegReg r15, r15, r14
    x64.leaRegRegReg rax, rbx, r12
    x64.leaRegRegReg rax, rax, r13
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rcx, rax, rcx
    x64.callDirect sink
    x64.leaRegRegReg rax, r15, r8
    x64.cmpRegImm32 rax, 186
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp matchcont
  ternaryfalse:
    x64.movRegImm32 r8, 99
  matchcont:
    x64.epilogue 88
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

func @sink {
  entry:
    arm64.prologue 16
    arm64.add x0, x0, 1
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot10, x28
    arm64.storeSlotReg slot9, x27
    arm64.storeSlotReg slot8, x26
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 3
    arm64.bl sink
    arm64.add x19, x0, 1
    arm64.add x20, x0, 2
    arm64.add x21, x0, 3
    arm64.add x22, x0, 4
    arm64.add x23, x0, 5
    arm64.add x24, x0, 6
    arm64.add x25, x0, 7
    arm64.add x26, x0, 8
    arm64.add x27, x0, 9
    arm64.add x1, x0, 10
    arm64.storeSlotReg slot0, x1
    arm64.add x28, x0, 11
    arm64.bl sink
    arm64.add x0, x0, 1
    arm64.mul x28, x0, x28
    arm64.add x0, x19, x20
    arm64.add x0, x0, x21
    arm64.add x0, x0, x22
    arm64.add x0, x0, x23
    arm64.add x0, x0, x24
    arm64.add x0, x0, x25
    arm64.add x0, x0, x26
    arm64.add x0, x0, x27
    arm64.loadRegSlot x1, slot0
    arm64.add x0, x0, x1
    arm64.bl sink
    arm64.add x0, x28, x0
    arm64.cmp x0, 186
    arm64.b.ne ternaryfalse
  ternarytrue:
    arm64.movImm x0, 0
    arm64.b matchcont
  ternaryfalse:
    arm64.movImm x0, 99
  matchcont:
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @sink {
  entry:
    arm64.prologue 16
    arm64.add x0, x0, 1
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot10, x28
    arm64.storeSlotReg slot9, x27
    arm64.storeSlotReg slot8, x26
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 3
    arm64.bl sink
    arm64.add x19, x0, 1
    arm64.add x20, x0, 2
    arm64.add x21, x0, 3
    arm64.add x22, x0, 4
    arm64.add x23, x0, 5
    arm64.add x24, x0, 6
    arm64.add x25, x0, 7
    arm64.add x26, x0, 8
    arm64.add x27, x0, 9
    arm64.add x1, x0, 10
    arm64.storeSlotReg slot0, x1
    arm64.add x28, x0, 11
    arm64.bl sink
    arm64.add x0, x0, 1
    arm64.mul x28, x0, x28
    arm64.add x0, x19, x20
    arm64.add x0, x0, x21
    arm64.add x0, x0, x22
    arm64.add x0, x0, x23
    arm64.add x0, x0, x24
    arm64.add x0, x0, x25
    arm64.add x0, x0, x26
    arm64.add x0, x0, x27
    arm64.loadRegSlot x1, slot0
    arm64.add x0, x0, x1
    arm64.bl sink
    arm64.add x0, x28, x0
    arm64.cmp x0, 186
    arm64.b.ne ternaryfalse
  ternarytrue:
    arm64.movImm x0, 0
    arm64.b matchcont
  ternaryfalse:
    arm64.movImm x0, 99
  matchcont:
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
}
```

<!-- test: confined-reuse-defs-past-the-callee-saved-half-float -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
**THE SIMD TWIN, AND IT IS THE SAME CONFINEMENT AND NOT A FLOAT ONE.** The program above in
floats: eleven values cross the `m = fsink(n)` call, against x64's ten callee-saved XMMs. That call
needs one of them in a spill slot, and the multiply costs a second. `a0`..`a7` cross the
`callDirect fsink` of `m = fsink(n)` in `xmm6`..`xmm13` and `q` in `xmm14`, and `a8` and `a9` cross
it in `slot1` and `slot0`. `w = m + 1.0` is computed in the caller-saved `xmm0`, so `b` is a copy +
`mulsdRegReg xmm15, xmm0, xmm14` into `xmm15`, and it crosses the `c = fsink(s)` call there. arm64
`fmul` is three-address, so arm64 has no reuse def here, and the case does not run there.

The pair exists because the two register files reach the same wall at different counts, and a fix
that reads one file's pool size is a fix only one file ever tests.

`n = 4.0`, so `a0`..`a9` are `5.0`..`14.0` summing to `95.0`; `q = 15.0`, `m = 5.0`, `w = 6.0`,
`b = 90.0` and `c = 96.0`. Total `186.0`.
```maxon
typealias Real = float(f64.min to f64.max)

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
	let q = n + 11.0
	let m = fsink(n)
	let w = m + 1.0
	let b = w * q
	let s = a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9
	let c = fsink(s)
	let total = b + c
	return 0 if total == 186.0 else 99
end 'main'
```
```exitcode
0
```

```RequiredFunctions
fsink
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @fsink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 128
    x64.storeSlotReg slot2, xmm6
    x64.storeSlotReg slot3, xmm7
    x64.storeSlotReg slot4, xmm8
    x64.storeSlotReg slot5, xmm9
    x64.storeSlotReg slot6, xmm10
    x64.storeSlotReg slot7, xmm11
    x64.storeSlotReg slot8, xmm12
    x64.storeSlotReg slot9, xmm13
    x64.storeSlotReg slot10, xmm14
    x64.storeSlotReg slot11, xmm15
    x64.movsdRegRip xmm0, [rip + __fconst_3]
    x64.callDirect fsink
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.movRegReg xmm6, xmm0
    x64.addsdRegReg xmm6, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_2]
    x64.movRegReg xmm7, xmm0
    x64.addsdRegReg xmm7, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.movRegReg xmm8, xmm0
    x64.addsdRegReg xmm8, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_4]
    x64.movRegReg xmm9, xmm0
    x64.addsdRegReg xmm9, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_5]
    x64.movRegReg xmm10, xmm0
    x64.addsdRegReg xmm10, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_6]
    x64.movRegReg xmm11, xmm0
    x64.addsdRegReg xmm11, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_7]
    x64.movRegReg xmm12, xmm0
    x64.addsdRegReg xmm12, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_8]
    x64.movRegReg xmm13, xmm0
    x64.addsdRegReg xmm13, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_9]
    x64.movRegReg xmm2, xmm0
    x64.addsdRegReg xmm2, xmm0, xmm1
    x64.storeSlotReg slot1, xmm2
    x64.movsdRegRip xmm1, [rip + __fconst_10]
    x64.movRegReg xmm2, xmm0
    x64.addsdRegReg xmm2, xmm0, xmm1
    x64.storeSlotReg slot0, xmm2
    x64.movsdRegRip xmm1, [rip + __fconst_11]
    x64.movRegReg xmm14, xmm0
    x64.addsdRegReg xmm14, xmm0, xmm1
    x64.callDirect fsink
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.movRegReg xmm15, xmm0
    x64.mulsdRegReg xmm15, xmm0, xmm14
    x64.addsdRegReg xmm6, xmm6, xmm7
    x64.addsdRegReg xmm6, xmm6, xmm8
    x64.addsdRegReg xmm6, xmm6, xmm9
    x64.addsdRegReg xmm6, xmm6, xmm10
    x64.addsdRegReg xmm6, xmm6, xmm11
    x64.addsdRegReg xmm6, xmm6, xmm12
    x64.addsdRegReg xmm6, xmm6, xmm13
    x64.loadRegSlot xmm0, slot1
    x64.addsdRegReg xmm6, xmm6, xmm0
    x64.loadRegSlot xmm0, slot0
    x64.addsdRegReg xmm6, xmm6, xmm0
    x64.movRegReg xmm0, xmm6
    x64.callDirect fsink
    x64.addsdRegReg xmm15, xmm15, xmm0
    x64.movsdRegRip xmm0, [rip + __fconst_186]
    x64.ucomisdRegReg xmm15, xmm0
    x64.jcc parity, ternaryfalse
  fordered:
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp matchcont
  ternaryfalse:
    x64.movRegImm32 r8, 99
  matchcont:
    x64.loadRegSlot xmm6, slot2
    x64.loadRegSlot xmm7, slot3
    x64.loadRegSlot xmm8, slot4
    x64.loadRegSlot xmm9, slot5
    x64.loadRegSlot xmm10, slot6
    x64.loadRegSlot xmm11, slot7
    x64.loadRegSlot xmm12, slot8
    x64.loadRegSlot xmm13, slot9
    x64.loadRegSlot xmm14, slot10
    x64.loadRegSlot xmm15, slot11
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

func @fsink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 128
    x64.storeSlotReg slot2, xmm6
    x64.storeSlotReg slot3, xmm7
    x64.storeSlotReg slot4, xmm8
    x64.storeSlotReg slot5, xmm9
    x64.storeSlotReg slot6, xmm10
    x64.storeSlotReg slot7, xmm11
    x64.storeSlotReg slot8, xmm12
    x64.storeSlotReg slot9, xmm13
    x64.storeSlotReg slot10, xmm14
    x64.storeSlotReg slot11, xmm15
    x64.movsdRegRip xmm0, [rip + __fconst_3]
    x64.callDirect fsink
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.movRegReg xmm6, xmm0
    x64.addsdRegReg xmm6, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_2]
    x64.movRegReg xmm7, xmm0
    x64.addsdRegReg xmm7, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.movRegReg xmm8, xmm0
    x64.addsdRegReg xmm8, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_4]
    x64.movRegReg xmm9, xmm0
    x64.addsdRegReg xmm9, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_5]
    x64.movRegReg xmm10, xmm0
    x64.addsdRegReg xmm10, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_6]
    x64.movRegReg xmm11, xmm0
    x64.addsdRegReg xmm11, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_7]
    x64.movRegReg xmm12, xmm0
    x64.addsdRegReg xmm12, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_8]
    x64.movRegReg xmm13, xmm0
    x64.addsdRegReg xmm13, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_9]
    x64.movRegReg xmm2, xmm0
    x64.addsdRegReg xmm2, xmm0, xmm1
    x64.storeSlotReg slot1, xmm2
    x64.movsdRegRip xmm1, [rip + __fconst_10]
    x64.movRegReg xmm2, xmm0
    x64.addsdRegReg xmm2, xmm0, xmm1
    x64.storeSlotReg slot0, xmm2
    x64.movsdRegRip xmm1, [rip + __fconst_11]
    x64.movRegReg xmm14, xmm0
    x64.addsdRegReg xmm14, xmm0, xmm1
    x64.callDirect fsink
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.movRegReg xmm15, xmm0
    x64.mulsdRegReg xmm15, xmm0, xmm14
    x64.addsdRegReg xmm6, xmm6, xmm7
    x64.addsdRegReg xmm6, xmm6, xmm8
    x64.addsdRegReg xmm6, xmm6, xmm9
    x64.addsdRegReg xmm6, xmm6, xmm10
    x64.addsdRegReg xmm6, xmm6, xmm11
    x64.addsdRegReg xmm6, xmm6, xmm12
    x64.addsdRegReg xmm6, xmm6, xmm13
    x64.loadRegSlot xmm0, slot1
    x64.addsdRegReg xmm6, xmm6, xmm0
    x64.loadRegSlot xmm0, slot0
    x64.addsdRegReg xmm6, xmm6, xmm0
    x64.movRegReg xmm0, xmm6
    x64.callDirect fsink
    x64.addsdRegReg xmm15, xmm15, xmm0
    x64.movsdRegRip xmm0, [rip + __fconst_186]
    x64.ucomisdRegReg xmm15, xmm0
    x64.jcc parity, ternaryfalse
  fordered:
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp matchcont
  ternaryfalse:
    x64.movRegImm32 r8, 99
  matchcont:
    x64.loadRegSlot xmm6, slot2
    x64.loadRegSlot xmm7, slot3
    x64.loadRegSlot xmm8, slot4
    x64.loadRegSlot xmm9, slot5
    x64.loadRegSlot xmm10, slot6
    x64.loadRegSlot xmm11, slot7
    x64.loadRegSlot xmm12, slot8
    x64.loadRegSlot xmm13, slot9
    x64.loadRegSlot xmm14, slot10
    x64.loadRegSlot xmm15, slot11
    x64.epilogue 128
    x64.popReg rbp
    x64.ret
}
```

<!-- test: confined-reuse-defs-at-x64s-own-scale-float -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
⭐ **THE SAME DEMAND AT x64's OWN XMM SCALE, WHERE THE MULTIPLY IS THE ONLY POINT THAT OVERFLOWS.**
Ten floats cross the `m = fsink(n)` call, `a0`..`a8` and `q`: exactly x64's ten callee-saved XMMs, so
neither call point overflows, and neither does the op after the multiply, where `a0`..`a8` and `b`
are live. At the multiply the ten are still live, `q` dying there, and `b`, which crosses the
`c = fsink(s)` call, is an eleventh contender that only `opConfinedTransient` counts. `a0`..`a7`
cross the `callDirect fsink` of `m = fsink(n)` in `xmm6`..`xmm13` and `q` in `xmm14`, and `a8`
crosses it in `slot0`. `w = m + 1.0` is computed in the caller-saved `xmm0`, so `b` is a copy +
`mulsdRegReg xmm15, xmm0, xmm14` into `xmm15`, and it crosses the `c = fsink(s)` call there. Gated to
x64 because it is sized to x64's ten callee-saved XMMs.

`n = 4.0`, so `a0`..`a8` are `5.0`..`13.0` summing to `81.0`; `q = 14.0`, `m = 5.0`, `w = 6.0`,
`b = 84.0` and `c = 82.0`. Total `166.0`.
```maxon
typealias Real = float(f64.min to f64.max)

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
	let q = n + 10.0
	let m = fsink(n)
	let w = m + 1.0
	let b = w * q
	let s = a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8
	let c = fsink(s)
	let total = b + c
	return 0 if total == 166.0 else 99
end 'main'
```
```exitcode
0
```

```RequiredFunctions
fsink
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @fsink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 128
    x64.storeSlotReg slot1, xmm6
    x64.storeSlotReg slot2, xmm7
    x64.storeSlotReg slot3, xmm8
    x64.storeSlotReg slot4, xmm9
    x64.storeSlotReg slot5, xmm10
    x64.storeSlotReg slot6, xmm11
    x64.storeSlotReg slot7, xmm12
    x64.storeSlotReg slot8, xmm13
    x64.storeSlotReg slot9, xmm14
    x64.storeSlotReg slot10, xmm15
    x64.movsdRegRip xmm0, [rip + __fconst_3]
    x64.callDirect fsink
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.movRegReg xmm6, xmm0
    x64.addsdRegReg xmm6, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_2]
    x64.movRegReg xmm7, xmm0
    x64.addsdRegReg xmm7, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.movRegReg xmm8, xmm0
    x64.addsdRegReg xmm8, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_4]
    x64.movRegReg xmm9, xmm0
    x64.addsdRegReg xmm9, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_5]
    x64.movRegReg xmm10, xmm0
    x64.addsdRegReg xmm10, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_6]
    x64.movRegReg xmm11, xmm0
    x64.addsdRegReg xmm11, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_7]
    x64.movRegReg xmm12, xmm0
    x64.addsdRegReg xmm12, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_8]
    x64.movRegReg xmm13, xmm0
    x64.addsdRegReg xmm13, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_9]
    x64.movRegReg xmm2, xmm0
    x64.addsdRegReg xmm2, xmm0, xmm1
    x64.storeSlotReg slot0, xmm2
    x64.movsdRegRip xmm1, [rip + __fconst_10]
    x64.movRegReg xmm14, xmm0
    x64.addsdRegReg xmm14, xmm0, xmm1
    x64.callDirect fsink
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.movRegReg xmm15, xmm0
    x64.mulsdRegReg xmm15, xmm0, xmm14
    x64.addsdRegReg xmm6, xmm6, xmm7
    x64.addsdRegReg xmm6, xmm6, xmm8
    x64.addsdRegReg xmm6, xmm6, xmm9
    x64.addsdRegReg xmm6, xmm6, xmm10
    x64.addsdRegReg xmm6, xmm6, xmm11
    x64.addsdRegReg xmm6, xmm6, xmm12
    x64.addsdRegReg xmm6, xmm6, xmm13
    x64.loadRegSlot xmm0, slot0
    x64.addsdRegReg xmm6, xmm6, xmm0
    x64.movRegReg xmm0, xmm6
    x64.callDirect fsink
    x64.addsdRegReg xmm15, xmm15, xmm0
    x64.movsdRegRip xmm0, [rip + __fconst_166]
    x64.ucomisdRegReg xmm15, xmm0
    x64.jcc parity, ternaryfalse
  fordered:
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp matchcont
  ternaryfalse:
    x64.movRegImm32 r8, 99
  matchcont:
    x64.loadRegSlot xmm6, slot1
    x64.loadRegSlot xmm7, slot2
    x64.loadRegSlot xmm8, slot3
    x64.loadRegSlot xmm9, slot4
    x64.loadRegSlot xmm10, slot5
    x64.loadRegSlot xmm11, slot6
    x64.loadRegSlot xmm12, slot7
    x64.loadRegSlot xmm13, slot8
    x64.loadRegSlot xmm14, slot9
    x64.loadRegSlot xmm15, slot10
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

func @fsink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 128
    x64.storeSlotReg slot1, xmm6
    x64.storeSlotReg slot2, xmm7
    x64.storeSlotReg slot3, xmm8
    x64.storeSlotReg slot4, xmm9
    x64.storeSlotReg slot5, xmm10
    x64.storeSlotReg slot6, xmm11
    x64.storeSlotReg slot7, xmm12
    x64.storeSlotReg slot8, xmm13
    x64.storeSlotReg slot9, xmm14
    x64.storeSlotReg slot10, xmm15
    x64.movsdRegRip xmm0, [rip + __fconst_3]
    x64.callDirect fsink
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.movRegReg xmm6, xmm0
    x64.addsdRegReg xmm6, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_2]
    x64.movRegReg xmm7, xmm0
    x64.addsdRegReg xmm7, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.movRegReg xmm8, xmm0
    x64.addsdRegReg xmm8, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_4]
    x64.movRegReg xmm9, xmm0
    x64.addsdRegReg xmm9, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_5]
    x64.movRegReg xmm10, xmm0
    x64.addsdRegReg xmm10, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_6]
    x64.movRegReg xmm11, xmm0
    x64.addsdRegReg xmm11, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_7]
    x64.movRegReg xmm12, xmm0
    x64.addsdRegReg xmm12, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_8]
    x64.movRegReg xmm13, xmm0
    x64.addsdRegReg xmm13, xmm0, xmm1
    x64.movsdRegRip xmm1, [rip + __fconst_9]
    x64.movRegReg xmm2, xmm0
    x64.addsdRegReg xmm2, xmm0, xmm1
    x64.storeSlotReg slot0, xmm2
    x64.movsdRegRip xmm1, [rip + __fconst_10]
    x64.movRegReg xmm14, xmm0
    x64.addsdRegReg xmm14, xmm0, xmm1
    x64.callDirect fsink
    x64.movsdRegRip xmm1, [rip + __fconst_1]
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.movRegReg xmm15, xmm0
    x64.mulsdRegReg xmm15, xmm0, xmm14
    x64.addsdRegReg xmm6, xmm6, xmm7
    x64.addsdRegReg xmm6, xmm6, xmm8
    x64.addsdRegReg xmm6, xmm6, xmm9
    x64.addsdRegReg xmm6, xmm6, xmm10
    x64.addsdRegReg xmm6, xmm6, xmm11
    x64.addsdRegReg xmm6, xmm6, xmm12
    x64.addsdRegReg xmm6, xmm6, xmm13
    x64.loadRegSlot xmm0, slot0
    x64.addsdRegReg xmm6, xmm6, xmm0
    x64.movRegReg xmm0, xmm6
    x64.callDirect fsink
    x64.addsdRegReg xmm15, xmm15, xmm0
    x64.movsdRegRip xmm0, [rip + __fconst_166]
    x64.ucomisdRegReg xmm15, xmm0
    x64.jcc parity, ternaryfalse
  fordered:
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp matchcont
  ternaryfalse:
    x64.movRegImm32 r8, 99
  matchcont:
    x64.loadRegSlot xmm6, slot1
    x64.loadRegSlot xmm7, slot2
    x64.loadRegSlot xmm8, slot3
    x64.loadRegSlot xmm9, slot4
    x64.loadRegSlot xmm10, slot5
    x64.loadRegSlot xmm11, slot6
    x64.loadRegSlot xmm12, slot7
    x64.loadRegSlot xmm13, slot8
    x64.loadRegSlot xmm14, slot9
    x64.loadRegSlot xmm15, slot10
    x64.epilogue 128
    x64.popReg rbp
    x64.ret
}
```

<!-- test: confined-reuse-defs-at-x64s-own-scale-int -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
The GPR half of the case above, at x64's own GPR scale: five ints cross the `m = sink(n)` call,
`a0`..`a3` and `q`, exactly x64's five callee-saved GPRs, so neither call point overflows, and `b` is
one more contender at the multiply than the five callee-saved registers hold.
`a0`..`a2` cross the `callDirect sink` of
`m = sink(n)` in `rbx`, `r12` and `r13` and `q` in `r14`, and `a3` crosses it in `slot0`, which
leaves `r15` for the multiply: `w` is defined into it, `imulRegReg r15, r15, r14` computes `b` in
place, and `b` crosses the `c = sink(s)` call there. arm64 `mul` is three-address, so arm64 has no
reuse def here, and five call-crossing values fit its ten callee-saved GPRs: the case does not run
there.

`n = 4`, so `a0`..`a3` are `5`..`8` summing to `26`; `q = 9`, `m = 5`, `w = 6`, `b = 54` and `c = 27`.
Total `81`.
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
	let q = n + 5
	let m = sink(n)
	let w = m + 1
	let b = w * q
	let s = a0 + a1 + a2 + a3
	let c = sink(s)
	let total = b + c
	return 0 if total == 81 else 99
end 'main'
```
```exitcode
0
```

```RequiredFunctions
sink
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @sink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegRegImm32 r8, rcx, 1
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
    x64.prologue 40
    x64.movRegImm32 rcx, 3
    x64.callDirect sink
    x64.leaRegRegImm32 rbx, r8, 1
    x64.leaRegRegImm32 r12, r8, 2
    x64.leaRegRegImm32 r13, r8, 3
    x64.leaRegRegImm32 rax, r8, 4
    x64.storeSlotReg slot0, rax
    x64.leaRegRegImm32 r14, r8, 5
    x64.movRegReg rcx, r8
    x64.callDirect sink
    x64.leaRegRegImm32 r15, r8, 1
    x64.imulRegReg r15, r15, r14
    x64.leaRegRegReg rax, rbx, r12
    x64.leaRegRegReg rax, rax, r13
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rcx, rax, rcx
    x64.callDirect sink
    x64.leaRegRegReg rax, r15, r8
    x64.cmpRegImm32 rax, 81
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp matchcont
  ternaryfalse:
    x64.movRegImm32 r8, 99
  matchcont:
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
  __mrt_program_started@16 = i8 0
}

func @sink {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegRegImm32 r8, rcx, 1
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
    x64.prologue 40
    x64.movRegImm32 rcx, 3
    x64.callDirect sink
    x64.leaRegRegImm32 rbx, r8, 1
    x64.leaRegRegImm32 r12, r8, 2
    x64.leaRegRegImm32 r13, r8, 3
    x64.leaRegRegImm32 rax, r8, 4
    x64.storeSlotReg slot0, rax
    x64.leaRegRegImm32 r14, r8, 5
    x64.movRegReg rcx, r8
    x64.callDirect sink
    x64.leaRegRegImm32 r15, r8, 1
    x64.imulRegReg r15, r15, r14
    x64.leaRegRegReg rax, rbx, r12
    x64.leaRegRegReg rax, rax, r13
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rcx, rax, rcx
    x64.callDirect sink
    x64.leaRegRegReg rax, r15, r8
    x64.cmpRegImm32 rax, 81
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r8, 0
    x64.jmp matchcont
  ternaryfalse:
    x64.movRegImm32 r8, 99
  matchcont:
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

<!-- test: the-runtime-overflows-its-own-pool-and-is-relieved -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
⭐⭐ **E5001 IS AN INSTRUCTION TO AN AUTHOR, AND THE COMPILER'S OWN EMITTED CODE HAS NONE — so case 3 above
does not reach it, and this four-line program is what proves it.** `sleep` installs the green-thread
runtime, whose `__gt_timer_check` walks a min-heap of parked deadlines: a 27-block function with a nested
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

⚠ **THE CASE IS GATED TO x64 BECAUSE 18 VALUES ONLY EXCEED A POOL OF FOURTEEN** — arm64's is wide
enough that the same walk never overflows, so the case would pass there without discriminating anything.
```maxon
function main() returns ExitCode
	sleep(1)
	return 0
end 'main'
```
```exitcode
0
```

```RequiredFunctions
__gt_timer_check
```
```TargetIr:x64-windows
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_allg_lock@24 = i64 0
  __gt_hold_epoch@32 = i64 0
  __gt_run_queue_head@40 = i64 0
  __gt_run_queue_tail@48 = i64 0
  __gt_timer_count@56 = i64 0
  __gt_timer_heap_base@64 = i64 0
  __gt_timer_capacity@72 = i64 0
  __gt_timer_seq@80 = i64 0
  __gt_timer_when@88 = i64 9223372036854775807
  __gt_quiesce_waiter@96 = i64 0
  __gt_panic_flush_claim@104 = i64 0
  __gt_seed_bytes@112 = i64 8192
  __gt_seed_sum_seen@120 = i64 0
  __gt_seed_count_seen@128 = i64 0
  __gt_seed_guard@136 = i64 0
  __gt_keeps_cpu_clock@144 = i64 0
  __gt_stack_pool@152 = i64 0
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
  __gt_gfree_nostack_head@240 = i64 0
  __gt_cell_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __gt_gfree_lock@280 = i64 0
  __gt_gfree_lock_owner@288 = i64 0
  __gt_gfree_lock_depth@296 = i64 0
  __sched_npidle@304 = i64 0
  __sched_runq_size@312 = i64 0
  __sched_global_pushes@320 = i64 0
  __sched_timer_scan_steps@328 = i64 0
  __sched_mcount@336 = i64 0
  __sched_nmidle@344 = i64 0
  __sched_nmidlelocked@352 = i64 0
  __sched_main_m@360 = i64 0
  __sched_phase@368 = i64 0
  __sched_lastpoll@376 = i64 0
  __sched_poll_until@384 = i64 0
  __sched_regain_head@392 = i64 0
  __np_poller@400 = i64 0
  __np_break@408 = i64 0
  __np_wake_sig@416 = i64 0
  __np_pd_table@424 = i64 0
  __np_pd_cap@432 = i64 0
  __np_waiters@440 = i64 0
  __sched_num_procs@448 = i64 0
  __sched_ncpu@456 = i64 0
  __sched_shutdown_flag@464 = i64 0
  __sched_lock@472 = i64 0
  __sched_lock_depth_sink@480 = i64 0
  __sched_nmspinning@488 = i64 0
  __sched_needspinning@496 = i64 0
  __sched_sysmon_event@504 = i64 0
  __sched_sysmon_wait@512 = i64 0
  __sched_timer_starts@520 = i64 0
  __sched_preempt_ext_lock@528 = i64 0
  __sched_preempt_context@536 = i64 0
  __slab_arena_list@544 = i64 0
  __slab_arena_map_l1@552 = i64 0
  __slab_state@560 = i64 0
  __mrt_console_probe_stdin@568 = i8 0
  __mrt_console_probe_stdout@569 = i8 0
  __mrt_console_probe_stderr@570 = i8 0
  __mrt_program_started@571 = i8 0
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
    x64.retIndirect
}

func @__gt_timer_check {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 104
    x64.movRegReg rbx, rcx
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.aluBaseDispImm32.add [rax + 832], 1
    x64.movRegImm32 rdx, 1
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __rl_slow#25
  __rl_cont#24:
    x64.leaRegGlobal rax, __gt_timer_count
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __gt_timer_heap_base
    x64.loadRegBaseDisp.word64 r12, [rcx + 0]
    x64.movRegImm32 r13, 0
    x64.movRegReg rcx, r13
    x64.jmp loophdr
  checkdue:
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.cmpRegReg rdx, rbx
    x64.jcc greater, done
  poproot:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 8]
    x64.storeSlotReg slot2, rcx
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 24]
    x64.storeSlotReg slot3, rcx
    x64.leaRegRegImm32 r14, rax, -1
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, fire
  movelast:
    x64.imulRegRegImm32 rax, r14, 32
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.loadRegBaseDisp.word64 rsi, [rax + 16]
    x64.loadRegBaseDisp.word64 rax, [rax + 24]
    x64.storeBaseDispReg.word64 [r12 + 0], rcx
    x64.storeBaseDispReg.word64 [r12 + 8], rdx
    x64.storeBaseDispReg.word64 [r12 + 16], rsi
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.movRegReg rax, r13
    x64.jmp siftdownhdr
  siftdownhaveleft:
    x64.leaRegRegImm32 rdx, rcx, 1
    x64.cmpRegReg rdx, r14
    x64.jcc greaterEqual, siftdowncompare
  siftdownchoose:
    x64.imulRegRegImm32 rsi, rcx, 32
    x64.leaRegRegReg rsi, r12, rsi
    x64.imulRegRegImm32 rdi, rdx, 32
    x64.leaRegRegReg rdi, r12, rdi
    x64.loadRegBaseDisp.word64 r8, [rsi + 0]
    x64.loadRegBaseDisp.word64 r9, [rsi + 8]
    x64.loadRegBaseDisp.word64 r9, [rsi + 16]
    x64.loadRegBaseDisp.word64 rsi, [rsi + 24]
    x64.loadRegBaseDisp.word64 rsi, [rdi + 0]
    x64.loadRegBaseDisp.word64 r10, [rdi + 8]
    x64.loadRegBaseDisp.word64 r10, [rdi + 16]
    x64.loadRegBaseDisp.word64 rdi, [rdi + 24]
    x64.cmpRegReg rsi, r8
    x64.setccReg less, rdi
    x64.cmpRegReg rsi, r8
    x64.setccReg equal, rsi
    x64.cmpRegReg r10, r9
    x64.setccReg less, r8
    x64.andRegReg rsi, rsi, r8
    x64.orRegReg rdi, rdi, rsi
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, siftdowncompare
  siftdowntakeright:
    x64.movRegReg rcx, rdx
  siftdowncompare:
    x64.storeSlotReg slot0, rcx
    x64.imulRegRegImm32 rcx, rcx, 32
    x64.leaRegRegReg rcx, r12, rcx
    x64.imulRegRegImm32 rax, rax, 32
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.loadRegBaseDisp.word64 r8, [rcx + 24]
    x64.loadRegBaseDisp.word64 r9, [rax + 0]
    x64.loadRegBaseDisp.word64 r10, [rax + 8]
    x64.storeSlotReg slot6, r10
    x64.loadRegBaseDisp.word64 r10, [rax + 16]
    x64.storeSlotReg slot5, r10
    x64.loadRegBaseDisp.word64 r11, [rax + 24]
    x64.storeSlotReg slot4, r11
    x64.cmpRegReg rdx, r9
    x64.setccReg less, r11
    x64.cmpRegReg rdx, r9
    x64.setccReg equal, r15
    x64.cmpRegReg rdi, r10
    x64.setccReg less, r10
    x64.andRegReg r15, r15, r10
    x64.orRegReg r11, r11, r15
    x64.cmpRegImm32 r11, 0
    x64.jcc equal, fire
  siftdownswap:
    x64.storeBaseDispReg.word64 [rax + 0], rdx
    x64.storeBaseDispReg.word64 [rax + 8], rsi
    x64.storeBaseDispReg.word64 [rax + 16], rdi
    x64.storeBaseDispReg.word64 [rax + 24], r8
    x64.storeBaseDispReg.word64 [rcx + 0], r9
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rcx + 8], rax
    x64.loadRegSlot rax, slot5
    x64.storeBaseDispReg.word64 [rcx + 16], rax
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rcx + 24], rax
    x64.loadRegSlot rax, slot0
  siftdownhdr:
    x64.leaRegRegReg rcx, rax, rax
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.cmpRegReg rcx, r14
    x64.jcc less, siftdownhaveleft
  fire:
    x64.loadRegSlot rax, slot3
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, firenotsleep
  firesleep:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __gt_ready_locked_held
    x64.jmp firejoin
  firenotsleep:
    x64.loadRegSlot rax, slot3
    x64.cmpRegImm32 rax, 3
    x64.jcc notEqual, firenetpoll
  firesend:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 4
    x64.loadRegSlot rdx, slot2
    x64.leaRegRegImm32 rdx, rdx, 688
    x64.lock cmpxchg [rdx], rcx
    x64.setccReg equal, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, firesendwon
  firesendlost:
    x64.movRegImm32 rax, 0
    x64.movRegReg r8, rax
    x64.jmp firejoin
  firesendwon:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __gt_ready_locked_held
    x64.jmp firejoin
  firenetpoll:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __np_pd_expire
  firejoin:
    x64.loadRegSlot rax, slot1
    x64.orRegReg rax, rax, r8
    x64.movRegReg rcx, rax
    x64.movRegReg rax, r14
  loophdr:
    x64.storeSlotReg slot1, rcx
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, rcx
    x64.jcc notEqual, checkdue
  done:
    x64.leaRegGlobal rdx, __gt_timer_count
    x64.storeBaseDispReg.word64 [rdx + 0], rax
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, timerwhenroot
  timerwhenempty:
    x64.movRegImm rax, 9223372036854775807
    x64.leaRegGlobal rcx, __gt_timer_when
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.jmp timerwhenpublished
  timerwhenroot:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.leaRegGlobal rcx, __gt_timer_when
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  timerwhenpublished:
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, __rl_slow#27
  __rl_cont#26:
    x64.aluBaseDispImm32.add [rax + 832], -1
    x64.loadRegSlot r8, slot1
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rl_slow#25:
    x64.callDirect __rtlock_acquire
    x64.jmp __rl_cont#24
  __rl_slow#27:
    x64.storeSlotReg slot7, rax
    x64.callDirect __rtlock_release
    x64.loadRegSlot rax, slot7
    x64.jmp __rl_cont#26
}
```

```TargetIr:x64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_allg_lock@24 = i64 0
  __gt_hold_epoch@32 = i64 0
  __gt_run_queue_head@40 = i64 0
  __gt_run_queue_tail@48 = i64 0
  __gt_timer_count@56 = i64 0
  __gt_timer_heap_base@64 = i64 0
  __gt_timer_capacity@72 = i64 0
  __gt_timer_seq@80 = i64 0
  __gt_timer_when@88 = i64 9223372036854775807
  __gt_quiesce_waiter@96 = i64 0
  __gt_panic_flush_claim@104 = i64 0
  __gt_seed_bytes@112 = i64 2048
  __gt_seed_sum_seen@120 = i64 0
  __gt_seed_count_seen@128 = i64 0
  __gt_seed_guard@136 = i64 0
  __gt_keeps_cpu_clock@144 = i64 0
  __gt_stack_pool@152 = i64 0
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
  __gt_gfree_nostack_head@240 = i64 0
  __gt_cell_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __gt_gfree_lock@280 = i64 0
  __gt_gfree_lock_owner@288 = i64 0
  __gt_gfree_lock_depth@296 = i64 0
  __sched_npidle@304 = i64 0
  __sched_runq_size@312 = i64 0
  __sched_global_pushes@320 = i64 0
  __sched_timer_scan_steps@328 = i64 0
  __sched_mcount@336 = i64 0
  __sched_nmidle@344 = i64 0
  __sched_nmidlelocked@352 = i64 0
  __sched_main_m@360 = i64 0
  __sched_phase@368 = i64 0
  __sched_lastpoll@376 = i64 0
  __sched_poll_until@384 = i64 0
  __sched_regain_head@392 = i64 0
  __np_poller@400 = i64 0
  __np_break@408 = i64 0
  __np_wake_sig@416 = i64 0
  __np_pd_table@424 = i64 0
  __np_pd_cap@432 = i64 0
  __np_waiters@440 = i64 0
  __np_orphan_exited@448 = i64 0
  __np_src_key_free@456 = i64 0
  __np_src_key_high@464 = i64 0
  __np_child_signal@472 = i64 0
  __np_pd_mtable@480 = i64 0
  __np_pd_mcap@488 = i64 0
  __sched_num_procs@496 = i64 0
  __sched_ncpu@504 = i64 0
  __sched_shutdown_flag@512 = i64 0
  __sched_lock@520 = i64 0
  __sched_lock_depth_sink@528 = i64 0
  __sched_nmspinning@536 = i64 0
  __sched_needspinning@544 = i64 0
  __sched_sysmon_event@552 = i64 0
  __sched_sysmon_wait@560 = i64 0
  __sched_timer_starts@568 = i64 0
  __mrt_envp@576 = i64 0
  __mrt_signal_stack_bytes@584 = i64 0
  __mrt_tls_next_key@592 = i64 0
  __slab_arena_list@600 = i64 0
  __slab_arena_map_l1@608 = i64 0
  __slab_state@616 = i64 0
  __mrt_program_started@624 = i8 0
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
    x64.retIndirect
}

func @__gt_timer_check {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 104
    x64.movRegReg rbx, rcx
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.aluBaseDispImm32.add [rax + 832], 1
    x64.movRegImm32 rdx, 1
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __rl_slow#25
  __rl_cont#24:
    x64.leaRegGlobal rax, __gt_timer_count
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __gt_timer_heap_base
    x64.loadRegBaseDisp.word64 r12, [rcx + 0]
    x64.movRegImm32 r13, 0
    x64.movRegReg rcx, r13
    x64.jmp loophdr
  checkdue:
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.cmpRegReg rdx, rbx
    x64.jcc greater, done
  poproot:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 8]
    x64.storeSlotReg slot2, rcx
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 24]
    x64.storeSlotReg slot3, rcx
    x64.leaRegRegImm32 r14, rax, -1
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, fire
  movelast:
    x64.imulRegRegImm32 rax, r14, 32
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.loadRegBaseDisp.word64 rsi, [rax + 16]
    x64.loadRegBaseDisp.word64 rax, [rax + 24]
    x64.storeBaseDispReg.word64 [r12 + 0], rcx
    x64.storeBaseDispReg.word64 [r12 + 8], rdx
    x64.storeBaseDispReg.word64 [r12 + 16], rsi
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.movRegReg rax, r13
    x64.jmp siftdownhdr
  siftdownhaveleft:
    x64.leaRegRegImm32 rdx, rcx, 1
    x64.cmpRegReg rdx, r14
    x64.jcc greaterEqual, siftdowncompare
  siftdownchoose:
    x64.imulRegRegImm32 rsi, rcx, 32
    x64.leaRegRegReg rsi, r12, rsi
    x64.imulRegRegImm32 rdi, rdx, 32
    x64.leaRegRegReg rdi, r12, rdi
    x64.loadRegBaseDisp.word64 r8, [rsi + 0]
    x64.loadRegBaseDisp.word64 r9, [rsi + 8]
    x64.loadRegBaseDisp.word64 r9, [rsi + 16]
    x64.loadRegBaseDisp.word64 rsi, [rsi + 24]
    x64.loadRegBaseDisp.word64 rsi, [rdi + 0]
    x64.loadRegBaseDisp.word64 r10, [rdi + 8]
    x64.loadRegBaseDisp.word64 r10, [rdi + 16]
    x64.loadRegBaseDisp.word64 rdi, [rdi + 24]
    x64.cmpRegReg rsi, r8
    x64.setccReg less, rdi
    x64.cmpRegReg rsi, r8
    x64.setccReg equal, rsi
    x64.cmpRegReg r10, r9
    x64.setccReg less, r8
    x64.andRegReg rsi, rsi, r8
    x64.orRegReg rdi, rdi, rsi
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, siftdowncompare
  siftdowntakeright:
    x64.movRegReg rcx, rdx
  siftdowncompare:
    x64.storeSlotReg slot0, rcx
    x64.imulRegRegImm32 rcx, rcx, 32
    x64.leaRegRegReg rcx, r12, rcx
    x64.imulRegRegImm32 rax, rax, 32
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.loadRegBaseDisp.word64 r8, [rcx + 24]
    x64.loadRegBaseDisp.word64 r9, [rax + 0]
    x64.loadRegBaseDisp.word64 r10, [rax + 8]
    x64.storeSlotReg slot6, r10
    x64.loadRegBaseDisp.word64 r10, [rax + 16]
    x64.storeSlotReg slot5, r10
    x64.loadRegBaseDisp.word64 r11, [rax + 24]
    x64.storeSlotReg slot4, r11
    x64.cmpRegReg rdx, r9
    x64.setccReg less, r11
    x64.cmpRegReg rdx, r9
    x64.setccReg equal, r15
    x64.cmpRegReg rdi, r10
    x64.setccReg less, r10
    x64.andRegReg r15, r15, r10
    x64.orRegReg r11, r11, r15
    x64.cmpRegImm32 r11, 0
    x64.jcc equal, fire
  siftdownswap:
    x64.storeBaseDispReg.word64 [rax + 0], rdx
    x64.storeBaseDispReg.word64 [rax + 8], rsi
    x64.storeBaseDispReg.word64 [rax + 16], rdi
    x64.storeBaseDispReg.word64 [rax + 24], r8
    x64.storeBaseDispReg.word64 [rcx + 0], r9
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rcx + 8], rax
    x64.loadRegSlot rax, slot5
    x64.storeBaseDispReg.word64 [rcx + 16], rax
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rcx + 24], rax
    x64.loadRegSlot rax, slot0
  siftdownhdr:
    x64.leaRegRegReg rcx, rax, rax
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.cmpRegReg rcx, r14
    x64.jcc less, siftdownhaveleft
  fire:
    x64.loadRegSlot rax, slot3
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, firenotsleep
  firesleep:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __gt_ready_locked_held
    x64.jmp firejoin
  firenotsleep:
    x64.loadRegSlot rax, slot3
    x64.cmpRegImm32 rax, 3
    x64.jcc notEqual, firenetpoll
  firesend:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 4
    x64.loadRegSlot rdx, slot2
    x64.leaRegRegImm32 rdx, rdx, 688
    x64.lock cmpxchg [rdx], rcx
    x64.setccReg equal, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, firesendwon
  firesendlost:
    x64.movRegImm32 rax, 0
    x64.movRegReg r8, rax
    x64.jmp firejoin
  firesendwon:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __gt_ready_locked_held
    x64.jmp firejoin
  firenetpoll:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __np_pd_expire
  firejoin:
    x64.loadRegSlot rax, slot1
    x64.orRegReg rax, rax, r8
    x64.movRegReg rcx, rax
    x64.movRegReg rax, r14
  loophdr:
    x64.storeSlotReg slot1, rcx
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, rcx
    x64.jcc notEqual, checkdue
  done:
    x64.leaRegGlobal rdx, __gt_timer_count
    x64.storeBaseDispReg.word64 [rdx + 0], rax
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, timerwhenroot
  timerwhenempty:
    x64.movRegImm rax, 9223372036854775807
    x64.leaRegGlobal rcx, __gt_timer_when
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.jmp timerwhenpublished
  timerwhenroot:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.leaRegGlobal rcx, __gt_timer_when
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  timerwhenpublished:
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, __rl_slow#27
  __rl_cont#26:
    x64.aluBaseDispImm32.add [rax + 832], -1
    x64.loadRegSlot r8, slot1
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rl_slow#25:
    x64.callDirect __rtlock_acquire
    x64.jmp __rl_cont#24
  __rl_slow#27:
    x64.storeSlotReg slot7, rax
    x64.callDirect __rtlock_release
    x64.loadRegSlot rax, slot7
    x64.jmp __rl_cont#26
}
```
