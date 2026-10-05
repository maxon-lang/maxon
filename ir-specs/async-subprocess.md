---
feature: async-subprocess
status: stable
keywords: [subprocess, process, spawn, async, await, runProcess, green-threads, scheduler, netpoll, yield, concurrency, throws, try]
category: concurrency
---

# Subprocess — spawn a child and yield while it runs: the cases that pin emitted code

The cases of `specs/async-subprocess.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: async-subprocess.posix-an-already-exited-child-gets-no-record -->
<!-- unsupported-targets: x64-windows -->
⛔⛔ **`-ESRCH` IS AN ANSWER, NOT A DESCRIPTOR, AND IT MUST NEVER REACH THE RECORD TABLE.** Its neighbour
above pins that an already-exited child is reaped rather than refused; this one pins the road the refusal
travels. `__np_child_open` answers the negative errno, and the open RETURNS on that edge; callers see
`-ESRCH` and take their own road. A block that fell through into `__np_pd_adopt` instead would use
**-3 as the table index**. The bound is a signed compare, so `-3 < capacity` reads as *inside the table*,
and `table + 8*(-3)` is the word 24 bytes BEFORE it: either a wild value readied as a green thread, or a
fresh record's address written over eight bytes belonging to something else.

⚠ **ONE LAP, AND THE ONE IS THE WHOLE POINT.** `posix-a-child-that-has-already-exited` runs fifty and
CANNOT catch this. From the second
lap the record table exists, so `table - 24` lands in live heap and the damage is silent; on the FIRST
`runProcess` of a process the table is unborn, the load is from a fixed wild address, and losing the race
is a hard SIGSEGV naming `__np_pd_adopt`. So `main` spawns exactly once and does nothing before it.

⚠ **IT IS PROBABILISTIC IN THE SAME WAY ITS NEIGHBOUR IS, AND LESS LIKELY TO FIRE.** It fires rarely
even under heavy concurrency on a loaded machine, and far more rarely on an idle one. What makes it worth keeping is that its failure is a crash naming the function, not a wrong number.
```maxon
function once() returns Integer
	return try __Builtins.runProcess("exit 7") otherwise 99
end 'once'

function main() returns ExitCode
	return once() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```
```RequiredRuntime
__np_child_open
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
  __il_body:
    x64.leaRegRdata rcx, [rip + __str_rec_1]  ; "exit 7"
    x64.callDirect __gt_process_run
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_cont
  tryerr:
    x64.movRegImm32 r8, 99
  __il_cont:
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
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at async-subprocess.posix-an-already-exited-child-gets-no-record.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__np_child_open {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 64
    x64.movRegReg rbx, rcx
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rbx
    x64.callDirect mrt_host_netpoll_child_open
    x64.movRegReg r12, rax
    x64.cmpRegImm32 r12, -3
    x64.jcc notEqual, npchsrcchk
  npchsrcgone:
    x64.movRegReg r8, r12
    x64.epilogue 64
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  npchsrcchk:
    x64.cmpRegImm32 r12, 0
    x64.jcc greaterEqual, npchadopt
  npfailed:
    x64.movRegImm32 rcx, 9
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __np_fail
    x64.leaRegRdata rbx, [rip + __abort_msg_103]  ; "fatal error: runtime abort 103 (netpollFailed)\x0a"
    x64.movRegImm32 r12, 47
    x64.callDirect __sched_enter_syscall
    x64.movRegImm32 rdi, 2
    x64.movRegReg rsi, rbx
    x64.movRegReg rdx, r12
    x64.x64Syscall 1
    x64.callDirect __sched_exit_syscall
    x64.movRegImm32 rdi, 103
    x64.movReg32Reg32 rdi, rdi
    x64.x64Syscall 231
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  npchadopt:
    x64.movRegReg rcx, r12
    x64.callDirect __np_pd_adopt
    x64.movRegReg r8, r12
    x64.epilogue 64
    x64.popReg r12
    x64.popReg rbx
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
    arm64.prologue 16
  __il_body:
    arm64.leaRdata x0, __str_rec_1  ; "exit 7"
    arm64.bl __gt_process_run
    arm64.cmp x9, 0
    arm64.b.eq __il_cont
  tryerr:
    arm64.movImm x0, 99
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
    arm64.leaRdata x0, __str_blob_3  ; "panic at async-subprocess.posix-an-already-exited-child-gets-no-record.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @__np_child_open {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.movRegReg x0, x19
    arm64.bl mrt_host_netpoll_child_open
    arm64.movRegReg x20, x0
    arm64.cmp x20, -3
    arm64.b.ne npchsrcchk
  npchsrcgone:
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  npchsrcchk:
    arm64.cmp x20, 0
    arm64.b.ge npchadopt
  npfailed:
    arm64.movImm x0, 9
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __np_fail
    arm64.leaRdata x19, __abort_msg_103  ; "fatal error: runtime abort 103 (netpollFailed)\x0a"
    arm64.movImm x20, 47
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.importCall 1
    arm64.bl mrt_host_status_or_errno
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 103
    arm64.importCall 0
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  npchadopt:
    arm64.movRegReg x0, x20
    arm64.bl __np_pd_adopt
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
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
    arm64.prologue 16
  __il_body:
    arm64.leaRdata x0, __str_rec_1  ; "exit 7"
    arm64.bl __gt_process_run
    arm64.cmp x9, 0
    arm64.b.eq __il_cont
  tryerr:
    arm64.movImm x0, 99
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
    arm64.leaRdata x0, __str_blob_3  ; "panic at async-subprocess.posix-an-already-exited-child-gets-no-record.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @__np_child_open {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.movRegReg x0, x19
    arm64.bl mrt_host_netpoll_child_open
    arm64.movRegReg x20, x0
    arm64.cmp x20, -3
    arm64.b.ne npchsrcchk
  npchsrcgone:
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  npchsrcchk:
    arm64.cmp x20, 0
    arm64.b.ge npchadopt
  npfailed:
    arm64.movImm x0, 9
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __np_fail
    arm64.leaRdata x19, __abort_msg_103  ; "fatal error: runtime abort 103 (netpollFailed)\x0a"
    arm64.movImm x20, 47
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.syscall 64
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 103
    arm64.syscall 94
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  npchadopt:
    arm64.movRegReg x0, x20
    arm64.bl __np_pd_adopt
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```
