---
feature: builtins-cpu-parallel
status: stable
keywords: [builtins, __Builtins, cpuCount, schedMaxActiveWorkers, intrinsics, parallel, scheduler, green-threads]
category: system
---

# `__Builtins.cpuCount()` and `__Builtins.schedMaxActiveWorkers()`: the cases that pin emitted code

The cases of `specs/builtins-cpu-parallel.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: builtins-cpu-parallel.cpu-count-body-is-runtime-source -->
⭐⭐ **THE MACHINE QUERY'S BODY IS MAXON SOURCE THE COMPILER READS OUT OF THE TREE, AND THIS IS THE CASE
THAT SEES IT.** `runtime/CpuParallel.maxon` writes `__cpu_count` against the `__Raw` floor — the host read
and the floor test over it — and the block below renders what the back end made of that source. Every other
`cpuCount` case here reads the ANSWER, and each would pass just as happily against a body the compiler built
itself.

⛔ **THE FLOOR TEST IS THE HALF THAT NEEDS WATCHING, AND IT MUST BE SIGNED.** `GetActiveProcessorCount`
reports 0 for an invalid processor group and `sysconf` reports -1, and one signed `< 1` covers both. The
source states the count full-range signed for exactly that reason; widened to a `bits(64)` machine word the
comparison reads unsigned, -1 becomes the largest count a machine could have, and the guard silently stops
guarding. That change is invisible to every other case in this file — the floor arm is unreachable through
this call site on a healthy host — and it shows up HERE, as the compare's condition.

⚠ `main` reads the count TWICE so that the rendered body is the one that runs.
```maxon
function main() returns ExitCode
	let first = __Builtins.cpuCount()
	let second = __Builtins.cpuCount()
	var score = 0
	if first >= 1 'atLeastOne'
		score = score + 1
	end 'atLeastOne'
	if second == first 'stable'
		score = score + 1
	end 'stable'
	return score as ExitCode
end 'main'
```
```exitcode
2
```
```RequiredRuntime
__cpu_count
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
    x64.prologue 40
    x64.callDirect __cpu_count
    x64.movRegReg rbx, r8
    x64.callDirect __cpu_count
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 rbx, 1
    x64.jcc less, ifcont
  atLeastOne:
    x64.movRegImm32 rax, 1
  ifcont:
    x64.cmpRegReg r8, rbx
    x64.jcc notEqual, critsplit
  stable:
    x64.leaRegRegImm32 r8, rax, 1
    x64.jmp __rc_ok
  critsplit:
    x64.movRegReg r8, rax
  __rc_ok:
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpu_count {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 65535
    x64.iatCall 49
    x64.movReg32Reg32 r8, rax
    x64.cmpRegImm32 r8, 1
    x64.jcc greaterEqual, ifcont
  belowFloor:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
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
    x64.pushReg rbx
    x64.prologue 40
    x64.callDirect __cpu_count
    x64.movRegReg rbx, r8
    x64.callDirect __cpu_count
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 rbx, 1
    x64.jcc less, ifcont
  atLeastOne:
    x64.movRegImm32 rax, 1
  ifcont:
    x64.cmpRegReg r8, rbx
    x64.jcc notEqual, critsplit
  stable:
    x64.leaRegRegImm32 r8, rax, 1
    x64.jmp __rc_ok
  critsplit:
    x64.movRegReg r8, rax
  __rc_ok:
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpu_count {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.callDirect mrt_host_cpu_count
    x64.cmpRegImm32 rax, 1
    x64.jcc greaterEqual, ifcont
  belowFloor:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegReg r8, rax
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
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.bl __cpu_count
    arm64.movRegReg x19, x0
    arm64.bl __cpu_count
    arm64.movImm x1, 0
    arm64.cmp x19, 1
    arm64.b.lt ifcont
  atLeastOne:
    arm64.movImm x1, 1
  ifcont:
    arm64.cmp x0, x19
    arm64.b.ne critsplit
  stable:
    arm64.add x0, x1, 1
    arm64.b __rc_ok
  critsplit:
    arm64.movRegReg x0, x1
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__cpu_count {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 58
    arm64.importCall 7
    arm64.cmp x0, 1
    arm64.b.ge ifcont
  belowFloor:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
  ifcont:
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
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.bl __cpu_count
    arm64.movRegReg x19, x0
    arm64.bl __cpu_count
    arm64.movImm x1, 0
    arm64.cmp x19, 1
    arm64.b.lt ifcont
  atLeastOne:
    arm64.movImm x1, 1
  ifcont:
    arm64.cmp x0, x19
    arm64.b.ne critsplit
  stable:
    arm64.add x0, x1, 1
    arm64.b __rc_ok
  critsplit:
    arm64.movRegReg x0, x1
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__cpu_count {
  entry:
    arm64.prologue 16
    arm64.bl mrt_host_cpu_count
    arm64.cmp x0, 1
    arm64.b.ge ifcont
  belowFloor:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: builtins-cpu-parallel.sched-processor-count-body-is-runtime-source -->
⭐⭐ **THE QUERY READS A `.data` WORD WHOSE LABEL IS NOT ITS OWN NAME, AND ONLY A RENDERED BODY SAYS WHICH
WORD.** `runtime/CpuParallel.maxon` writes `__sched_processor_count` as an address row plus a load, and the
address row names `__sched_num_procs` — the P array's length wearing this query's second hat. Its band-mate
`__sched_max_active_workers` is the one whose label DOES match its entry point, so the two rows are exactly
the pair a mix-up would swap, and a swapped body still compiles, still links and still answers a plausible
number.

⚠ **THE ANSWER IS AT LEAST 1 ALTHOUGH THIS PROGRAM SPAWNS NOTHING**, because asking is a use of the
scheduler: the query installs it, and `__gt_init` resolves the count before `main` runs. A 0 — the word's
`.data` seed — would be a count read off a scheduler that never initialized, which is no answer at all.
What a resolved count equals is `sched-default-procs.md`'s; asking that here would make this case depend
on the machine it runs on.
```maxon
function main() returns ExitCode
	let first = __Builtins.schedProcessorCount()
	let second = __Builtins.schedProcessorCount()
	var score = 0
	if first >= 1 'resolvedBeforeMain'
		score = score + 1
	end 'resolvedBeforeMain'
	if second == first 'stable'
		score = score + 1
	end 'stable'
	return score as ExitCode
end 'main'
```
```exitcode
2
```
```RequiredRuntime
__sched_processor_count
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
    x64.pushReg rbx
    x64.prologue 40
    x64.callDirect __sched_processor_count
    x64.movRegReg rbx, r8
    x64.callDirect __sched_processor_count
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 rbx, 1
    x64.jcc less, ifcont
  resolvedBeforeMain:
    x64.movRegImm32 rax, 1
  ifcont:
    x64.cmpRegReg r8, rbx
    x64.jcc notEqual, critsplit
  stable:
    x64.leaRegRegImm32 r8, rax, 1
    x64.jmp __rc_ok
  critsplit:
    x64.movRegReg r8, rax
  __rc_ok:
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__sched_processor_count {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __sched_num_procs
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
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
    x64.pushReg rbx
    x64.prologue 40
    x64.callDirect __sched_processor_count
    x64.movRegReg rbx, r8
    x64.callDirect __sched_processor_count
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 rbx, 1
    x64.jcc less, ifcont
  resolvedBeforeMain:
    x64.movRegImm32 rax, 1
  ifcont:
    x64.cmpRegReg r8, rbx
    x64.jcc notEqual, critsplit
  stable:
    x64.leaRegRegImm32 r8, rax, 1
    x64.jmp __rc_ok
  critsplit:
    x64.movRegReg r8, rax
  __rc_ok:
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__sched_processor_count {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __sched_num_procs
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
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
  __slab_arena_list@472 = i64 0
  __slab_arena_map_l1@480 = i64 0
  __slab_state@488 = i64 0
  __mrt_program_started@496 = i8 0
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.bl __sched_processor_count
    arm64.movRegReg x19, x0
    arm64.bl __sched_processor_count
    arm64.movImm x1, 0
    arm64.cmp x19, 1
    arm64.b.lt ifcont
  resolvedBeforeMain:
    arm64.movImm x1, 1
  ifcont:
    arm64.cmp x0, x19
    arm64.b.ne critsplit
  stable:
    arm64.add x0, x1, 1
    arm64.b __rc_ok
  critsplit:
    arm64.movRegReg x0, x1
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__sched_processor_count {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __sched_num_procs
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
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
  __mrt_errno@472 = i64 0
  __mrt_envp@480 = i64 0
  __mrt_signal_stack_bytes@488 = i64 0
  __mrt_tls_next_key@496 = i64 0
  __slab_arena_list@504 = i64 0
  __slab_arena_map_l1@512 = i64 0
  __slab_state@520 = i64 0
  __mrt_program_started@528 = i8 0
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.bl __sched_processor_count
    arm64.movRegReg x19, x0
    arm64.bl __sched_processor_count
    arm64.movImm x1, 0
    arm64.cmp x19, 1
    arm64.b.lt ifcont
  resolvedBeforeMain:
    arm64.movImm x1, 1
  ifcont:
    arm64.cmp x0, x19
    arm64.b.ne critsplit
  stable:
    arm64.add x0, x1, 1
    arm64.b __rc_ok
  critsplit:
    arm64.movRegReg x0, x1
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__sched_processor_count {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __sched_num_procs
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.epilogue 16
    arm64.ret
}
```
