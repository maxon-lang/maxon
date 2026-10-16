---
feature: runtime-source-tier
status: stable
keywords: [runtime, raw-intrinsics, compiler-internals, source-tier, reserved-identifier]
category: diagnostics
---

# The Runtime Source Tier: the cases that pin emitted code

The cases of `specs/runtime-source-tier.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: an-unreached-runtime-body-costs-the-program-nothing -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⚠ **THE PROPERTY IS TARGET-NEUTRAL AND THE CHANNEL IS NOT.** `RequiredData` is a PREFIX compare, so it can only catch a word inserted where something still TRAILS it — and the only globals laid out after a program's own are the x64-windows console probes. On a lane without them the pinned tail runs past the end of the section, and a shorter `.data` is all the gate can say. The reachability this measures is a target-neutral walk; what is missing elsewhere is an anchor, not the behaviour.
⛔⛔ **A TIER BODY THE PROGRAM CANNOT REACH MUST NOT SPEND ITS BUDGET.** A runtime name is never
`unreachable` — the tier's bodies are built unconditionally, because no source call edge earns them — and
`scanRuntimeUsage`'s only skip reads that same set. So a CALL inside a tier body is credited to EVERY
program the tier is linked into, including the ones dead-function elimination sweeps the body out of.

Here `probeWorkers` asks for the worker mark and `main` asks for nothing. The body is swept, so the
program cannot observe the answer — and `.data` follows the sweep: a runtime word is laid out only where a
SURVIVING function names it (`GlobalDataTable.layOut`, `DataReach.walked`), so no word ships for a body
that is not there to read it. The `.data` roster is the channel that shows it.

⚠ The probe calls a RUNTIME ENTRY and not a `__Raw` row, and that is the whole point: a `rawIntrinsic`
names no callee and records nothing, which is why every tier family before this one left the question
untouched.
⚠ **THE PIN STATES THE WHOLE ROSTER, NOT A LEADING SLOT, AND IT HAS TO.** `RequiredData` is a PREFIX
compare and a program's own globals are laid out AHEAD of the runtime's, so a word the program did not
earn lands BEHIND `used` where a one-line pin cannot see it. Spelling the trailing console probes is what
makes an inserted word a mismatch rather than a longer tail.
```maxon
// --- runtime-file: Probe.maxon
module function probeWorkers() returns MachineWord
	return __sched_max_active_workers()
end 'probeWorkers'
// --- file: main.maxon
var used = 42 as FullRangeInt

function main() returns ExitCode
	return used - 42
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i8 0
i8 0
i8 0
```

```TargetIr:x64-windows
data {
  __data_used@0 = i64 42
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_used
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -42
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at main.maxon:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

<!-- test: a-reached-runtime-entry-still-earns-its-word -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⚠ **THE PROPERTY IS TARGET-NEUTRAL AND THE CHANNEL IS NOT.** `RequiredData` is a PREFIX compare, so it can only catch a word inserted where something still TRAILS it — and the only globals laid out after a program's own are the x64-windows console probes. On a lane without them the pinned tail runs past the end of the section, and a shorter `.data` is all the gate can say. The reachability this measures is a target-neutral walk; what is missing elsewhere is an anchor, not the behaviour.
⭐⭐ **THE CONTROL ON THE CASE ABOVE, AND WITHOUT IT THE RULE COULD BE *"CREDIT NO TIER BODY, EVER"*.** That
answer passes the unreached case and is the dangerous direction: a body left uncredited while it survives
is the disagreement the panic below refuses. So the same `Probe.maxon` stands here unchanged and `main`
asks for the worker mark itself — a call the compiler mints is the only root a program can reach a tier
entry through — and both halves are pinned: the word the body reads is in `.data`, and the body is in the
image.

⚠ **ONE WORD, NOT THE PAIR.** `__sched_max_active_workers` is the only word this body names;
`__sched_active_workers` beside it is stepped by the scheduler's worker loop alone, which no program
without a scheduler carries, so it is not laid out here — `.data` holds what a surviving function names,
not a family's whole roster.

⚠ **`main` CALLS THE INTRINSIC ITSELF, SO THE CASE COMPILES IN THE LIBRARY TIER.** `__Builtins` is callable
only from a source file under `stdlib/` or `runtime/` (E3185). The public spelling,
`Scheduler.counters().maxActiveWorkers`, reads all fourteen scheduler counters and lays out a word for each,
and a roster that names one word cannot be pinned through it.

⚠ **WHAT ACTUALLY FIRES ON THE BAD ANSWER IS A PANIC, NOT A MISMATCH.** Uncredit this entry and it enters
`LibraryFacts.unreachedRuntimeTier` while `main` still calls it, which is the disagreement
`DeadFunctionElimination.requireUnreachableLibraryStayedDead` exists to refuse — so this case reddens on an
abort with the name in it rather than on a shorter `.data` roster.

⚠ It also puts the DERIVATION's other path under a case. The unreached program above names no library
function at all, so the reach split is decided by `deriveLibraryFacts`' short-circuit; this one crosses into
library source at `__sched_max_active_workers`, so the precise from-`main` walk decides it.
```maxon
// --- runtime-file: Probe.maxon
module function probeWorkers() returns MachineWord
	return __sched_max_active_workers()
end 'probeWorkers'
// --- file: main.maxon
var used = 42 as Count

function main() returns ExitCode
	let workers = __Builtins.schedMaxActiveWorkers()
	return used - 41 - workers
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i64 1
i8 0
i8 0
i8 0
```
```RequiredRuntime
__sched_max_active_workers
```

```TargetIr:x64-windows
data {
  __data_used@0 = i64 42
  __sched_max_active_workers@8 = i64 1
  __mrt_console_probe_stdin@16 = i8 0
  __mrt_console_probe_stdout@17 = i8 0
  __mrt_console_probe_stderr@18 = i8 0
  __mrt_program_started@19 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.callDirect __sched_max_active_workers
    x64.leaRegGlobal rax, __data_used
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 rax, rax, -41
    x64.subRegReg rax, rax, r8
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rcx, 4294967295
    x64.cmpRegReg rax, rcx
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg r8, rax
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at main.maxon:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__sched_max_active_workers {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __sched_max_active_workers
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
    x64.popReg rbp
    x64.ret
}
```

<!-- test: reaching-one-family-does-not-credit-another-tier-body -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⚠ **THE PROPERTY IS TARGET-NEUTRAL AND THE CHANNEL IS NOT.** `RequiredData` is a PREFIX compare, so it can only catch a word inserted where something still TRAILS it — and the only globals laid out after a program's own are the x64-windows console probes. On a lane without them the pinned tail runs past the end of the section, and a shorter `.data` is all the gate can say. The reachability this measures is a target-neutral walk; what is missing elsewhere is an anchor, not the behaviour.
⭐ **REACHED IS PER ENTRY POINT, NOT PER TIER.** `main` reaches the process family and nothing else, so the
precise walk runs and files `probeWorkers` unreached — and the worker counters stay out of `.data` even
though a tier body, compiled into this very image, holds the query that names them.

⚠ **THE SECOND FAMILY IS `__proc_pid` BECAUSE IT IS THE ONE THAT PINS HONESTLY HERE.** The other bits a tier
body could set on this lane — `usesBackgroundPriority`, `usesCpuCount`, `usesWallClock` — reach only the PE
writer's optional import band and the non-Windows host chunks, which no spec channel renders; the pid read
lays no word of its own, so its presence leaves the `.data` roster exactly as the unreached case's and the
two sched words are the whole difference between the three programs in this group.
```maxon
// --- runtime-file: Probe.maxon
module function probeWorkers() returns MachineWord
	return __sched_max_active_workers()
end 'probeWorkers'
// --- file: main.maxon
var used = 42 as FullRangeInt

function main() returns ExitCode
	let pid = Process.id()
	if pid > 0 'aRealProcess'
		return used - 42
	end 'aRealProcess'

	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i8 0
i8 0
i8 0
```

```TargetIr:x64-windows
data {
  __data_used@0 = i64 42
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
  __il_body:
    x64.callDirect __proc_pid
  __il_cont:
    x64.cmpRegImm32 r8, 0
    x64.jcc belowEqual, ifcont
  aRealProcess:
    x64.leaRegGlobal rax, __data_used
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -42
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
  ifcont:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at main.maxon:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

<!-- test: a-word-only-a-dead-user-function-reads-is-not-laid-out -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⭐⭐ **`.data` FOLLOWS REACHABILITY, NOT THE PROGRAM'S VOCABULARY.** The word behind
`Scheduler.counters()` is laid out only when a function that survives dead-function elimination reads
it. `dormant` is a user function nothing calls, so its query reaches no body and lays out no word — the
same answer the unreached tier probe above gets, here for the program's own dead code. The sched words
would land between `used` and the console probes, which is where a prefix compare can see them.
```maxon
typealias WorkerCount = int(0 to u64.max)

var used = 42 as FullRangeInt

function dormant() returns WorkerCount
	return Scheduler.counters().maxActiveWorkers
end 'dormant'

function main() returns ExitCode
	return used - 42
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i8 0
i8 0
i8 0
```

```TargetIr:x64-windows
data {
  __data_used@0 = i64 42
  __gt_allg@8 = i64 0
  __gt_allglen@16 = i64 0
  __gt_allgcap@24 = i64 0
  __gt_allg_lock@32 = i64 0
  __gt_hold_epoch@40 = i64 0
  __gt_run_queue_head@48 = i64 0
  __gt_run_queue_tail@56 = i64 0
  __gt_timer_count@64 = i64 0
  __gt_timer_heap_base@72 = i64 0
  __gt_timer_capacity@80 = i64 0
  __gt_timer_seq@88 = i64 0
  __gt_timer_when@96 = i64 9223372036854775807
  __gt_live_count@104 = i64 0
  __gt_quiesce_waiter@112 = i64 0
  __gt_quiesce_stuck@120 = i64 0
  __gt_seed_bytes@128 = i64 16384
  __gt_stack_bytes_sum@136 = i64 0
  __gt_stack_bytes_count@144 = i64 0
  __gt_seed_sum_seen@152 = i64 0
  __gt_seed_count_seen@160 = i64 0
  __gt_seed_guard@168 = i64 0
  __gt_keeps_cpu_clock@176 = i64 0
  __gt_stack_pool@184 = i64 0
  __sched_active_workers@192 = i64 1
  __sched_max_active_workers@200 = i64 1
  __sched_tls_index@208 = i64 0
  __sched_tls_teb_offset@216 = i64 0
  __sched_procs@224 = i64 0
  __sched_allm@232 = i64 0
  __sched_async_preempt_off@240 = i64 0
  __sched_midle@248 = i64 0
  __sched_pidle@256 = i64 0
  __gt_gfree_head@264 = i64 0
  __sched_arena_cursor@272 = i64 0
  __sched_arena_limit@280 = i64 0
  __gt_records_carved@288 = i64 0
  __gt_gfree_lock@296 = i64 0
  __maxon_registry_lock@304 = i64 0
  __sched_npidle@312 = i64 0
  __sched_runq_size@320 = i64 0
  __sched_global_pushes@328 = i64 0
  __sched_timer_scan_steps@336 = i64 0
  __sched_mcount@344 = i64 0
  __sched_nmidle@352 = i64 0
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
  __sched_shutdown_flag@456 = i64 0
  __sched_lock@464 = i64 0
  __sched_lock_depth_sink@472 = i64 0
  __sched_nmspinning@480 = i64 0
  __sched_needspinning@488 = i64 0
  __sched_sysmon_event@496 = i64 0
  __sched_sysmon_wait@504 = i64 0
  __sched_timer_starts@512 = i64 0
  __sched_preempt_ext_lock@520 = i64 0
  __sched_preempt_context@528 = i64 0
  __slab_arena_list@536 = i64 0
  __slab_arena_map_l1@544 = i64 0
  __slab_state@552 = i64 0
  __mrt_console_probe_stdin@560 = i8 0
  __mrt_console_probe_stdout@561 = i8 0
  __mrt_console_probe_stderr@562 = i8 0
  __mrt_program_started@563 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_used
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -42
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
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-word-only-a-dead-user-function-reads-is-not-laid-out.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```
