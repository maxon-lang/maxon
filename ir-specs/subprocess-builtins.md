---
feature: subprocess-builtins
status: experimental
keywords: [subprocess, __Builtins, intrinsics, spawn, collect, stdout, stderr, stdin, timeout, detach, PATH, PATHEXT, pipe, handle-table, green-threads]
category: system
---

# The `__Builtins.subprocess*` intrinsics — `stdlib/Subprocess.maxon`'s whole floor: the cases that pin emitted code

The cases of `specs/subprocess-builtins.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: subprocess-builtins.posix-a-collected-child-costs-no-timer-poll -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
<!-- procs: 1 -->
⭐ **THE COLLECT LOOP WAITS ON THE POLLER, SO A SLOW CHILD COSTS IT NOTHING TO WAIT FOR.**
`posix-collect-echo`'s child answers at once and says nothing about the wait; this one takes a whole
second to produce its three bytes, and that second is the subject. A drain that yields a millisecond on
every idle pass pays a park and a wake per pass — about two thousand across the second — and never reaches
the poller at all; a drain registered with the poller blocks there until a pipe is readable or the child
exits, and pays a handful. `timerpolls=` bounds the wakes and `onpoller=` says the wait was on the poller,
and neither alone is the property: a loop that merely spun would also arm no timer, and a loop that parked
on the poller and then woke on a period of its own would still be a poll.

⚠ **THE PARK-WAKE COUNT IS WHAT A PER-PASS YIELD ACTUALLY COSTS, WHICH IS WHY IT IS THE ONE READ.**
`Scheduler.counters().timerStarts` counts the machines the system monitor starts for an overdue timer,
not the timers a program arms, and a drain whose own machine wakes at each deadline gives the monitor
nothing to rescue — 0 over this second, against well over a thousand park wakes. A bound on the monitor's count
would therefore hold whatever the drain does.

⚠ **THE COLLECTED BYTES ARE PINNED BESIDE THE COUNTS, BECAUSE A LOOP THAT COLLECTED NOTHING IS ALSO
QUIET.** `outLen` and `matches` are what stop this case passing on an empty capture, exactly as
`collect-echo`'s printed line stops that one passing on its exit code alone. `sh` ends the line with a bare
LF, so `hi\n` is THREE bytes.

⭐ **THE GROUP WAIT'S BODY IS PINNED HERE BECAUSE THIS IS THE COLLECT THAT PARKS IN IT.** The pinned
`__np_pd_wait_any` marks a member only while its record is still in the life the collect began in — a compare
of the generation the member carries from the build against the record's own before the `PdWait` store — so a
member whose record was closed and reissued between the build and the lock is refused rather than stamped into
the next owner's record.
```maxon
let quietWakes = 16

let pollerParks = 1

function main() returns ExitCode
	var argv = StringArray.create(0)
	argv.push("-c")
	argv.push("sleep 1; echo hi")
	let before = Scheduler.counters()
	let result = try Subprocess.run(Executable.name("/bin/sh"), arguments: argv) otherwise panic("/bin/sh runs on every POSIX lane")
	let after = Scheduler.counters()
	let wakes = after.parkWakes - before.parkWakes
	let parks = after.netpollBlocks - before.netpollBlocks
	let n = result.stdout.byteLength()
	let matches = result.stdout.startsWith("hi")
	print("timerpolls={wakes <= quietWakes} onpoller={parks >= pollerParks} outLen={n} matches={matches}\n")
	return n as ExitCode
end 'main'
```
```exitcode
3
```
```stdout
timerpolls=true onpoller=true outLen=3 matches=true
```
```RequiredRuntime
__np_pd_wait_any
```

```TargetIr:x64-linux
data {
  __data_CharacterSet.cachedWhitespacesAndNewlines@0 = i64 0
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
  __gt_quiesce_waiter@104 = i64 0
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
  __io_pipe_seq@600 = i64 0
  __subp_table@608 = i64 0
  __subp_last_error@616 = i64 0
  __slab_arena_list@624 = i64 0
  __slab_arena_map_l1@632 = i64 0
  __slab_state@640 = i64 0
  __mrt_program_started@648 = i8 0
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
    x64.prologue 232
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __str_decref]
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.leaRegRdata rcx, [rip + __str_rec_80]  ; "-c"
    x64.callDirect __str_retain
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.leaRegRdata rcx, [rip + __str_rec_81]  ; "sleep 1; echo hi"
    x64.callDirect __str_retain
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.callDirect Scheduler.counters
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.leaRegRdata rcx, [rip + __str_rec_82]  ; "/bin/sh"
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.movRegReg r13, r8
    x64.orRegImm32 r13, r8, 1
  __il_body#17:
    x64.leaRegRdata r14, [rip + __str_rec_117]  ; ""
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r15 + 0], r8
  __il_body#11:
    x64.movRegImm32 r9, 0
  __il_cont#10:
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r15
    x64.callDirect Subprocess.run
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot2, rbx
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.movRegReg rcx, r15
    x64.callDirect __destruct_FilePath
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_Executable
    x64.callDirect Scheduler.counters
    x64.storeSlotReg slot3, r8
    x64.loadRegBaseDisp.word64 r13, [r8 + 64]
    x64.loadRegBaseDisp.word64 rax, [r12 + 64]
    x64.subRegReg r13, r13, rax
    x64.loadRegBaseDisp.word64 r14, [r8 + 72]
    x64.loadRegBaseDisp.word64 rax, [r12 + 72]
    x64.subRegReg r14, r14, rax
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
  __il_body#19:
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.storeSlotReg slot4, r12
  __il_cont#18:
    x64.leaRegRdata rdx, [rip + __str_rec_83]  ; "hi"
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.callDirect String.startsWith
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_84]  ; "timerpolls="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot16, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot23, rax
    x64.cmpRegImm32 r13, 16
    x64.setccReg belowEqual, r13
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot15, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r8
    x64.callDirect __bool_to_string
    x64.movRegReg r13, r8
    x64.storeSlotReg slot22, r13
    x64.leaRegRdata rax, [rip + __str_rec_85]  ; " onpoller="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot14, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot21, rax
    x64.cmpRegImm32 r14, 1
    x64.setccReg aboveEqual, r14
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot13, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r8
    x64.callDirect __bool_to_string
    x64.movRegReg r14, r8
    x64.storeSlotReg slot20, r14
    x64.leaRegRdata rax, [rip + __str_rec_86]  ; " outLen="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot10, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot19, rax
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg r12, r8
    x64.storeSlotReg slot5, r12
    x64.leaRegRdata rax, [rip + __str_rec_87]  ; " matches="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r15, [rax + 8]
    x64.storeSlotReg slot7, r15
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot9, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __bool_to_string
    x64.storeSlotReg slot24, r8
    x64.leaRegRdata rax, [rip + __str_rec_88]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot18, rax
    x64.loadRegSlot rbx, slot23
    x64.leaRegRegReg rax, rbx, r13
    x64.loadRegSlot r13, slot21
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegSlot r14, slot19
    x64.leaRegRegReg rax, rax, r14
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r15
    x64.loadRegSlot r12, slot24
    x64.leaRegRegReg rax, rax, r12
    x64.loadRegSlot rcx, slot18
    x64.leaRegRegReg rax, rax, rcx
    x64.storeSlotReg slot12, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot17, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.loadRegSlot rdx, slot16
    x64.movRegReg rcx, r15
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.loadRegSlot rdx, slot15
    x64.loadRegSlot rax, slot22
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot22
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot14
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r13
    x64.loadRegSlot rdx, slot13
    x64.loadRegSlot rax, slot20
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot20
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r14
    x64.loadRegSlot rax, slot5
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot7
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot9
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rdx, slot11
    x64.loadRegSlot rax, slot18
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot18
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot17
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot12
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot15
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot13
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot6
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot9
    x64.callDirect __mm_decref
  __il_body#13:
    x64.loadRegSlot rcx, slot17
    x64.callDirect __write_stdout
  __il_cont#12:
    x64.loadRegSlot rcx, slot17
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot4
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.loadRegSlot rax, slot4
    x64.cmpRegImm32 rax, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __mm_decref
  exitdrop#4:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __destruct_CollectedOutput
  exitdrop#5:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  exitdrop#6:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitterminal:
    x64.loadRegSlot r8, slot4
    x64.epilogue 232
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_89]  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:11: /bin/sh runs on every POSIX lane\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 232
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_273]  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:18: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 232
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
}

func @__np_pd_wait_any {
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
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.aluBaseDispImm32.add [rax + 824], 1
    x64.movRegImm32 rdx, 1
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __rl_slow#36
  __rl_cont#35:
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 r13, 0
  npgalifehdr:
    x64.cmpRegReg r13, r12
    x64.jcc less, npgalifebody
  npgalookpre:
    x64.movRegImm32 r13, 0
  npgalookhdr:
    x64.cmpRegReg r13, r12
    x64.jcc less, npgalookbody
  npgamarkpre:
    x64.movRegImm32 r13, 0
  npgamarkhdr:
    x64.cmpRegReg r13, r12
    x64.jcc less, npgamarkbody
  npgapark:
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, __rl_slow#42
  __rl_cont#41:
    x64.aluBaseDispImm32.add [rax + 824], -1
    x64.movRegImm32 rcx, 10
    x64.movRegReg rdx, rbx
    x64.callDirect __gt_park
  npgaresumed:
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.aluBaseDispImm32.add [rax + 824], 1
    x64.movRegImm32 rdx, 1
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __rl_slow#44
  __rl_cont#43:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 r13, 0
  npgaclearhdr:
    x64.cmpRegReg r13, r12
    x64.jcc less, npgaclearbody
  npgadone:
    x64.loadRegBaseDisp.word64 r8, [rbx + 16]
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, __rl_slow#46
  __rl_cont#45:
    x64.aluBaseDispImm32.add [rax + 824], -1
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  npgaclearbody:
    x64.imulRegRegImm32 rax, r13, 16
    x64.leaRegRegImm32 rax, rax, 32
    x64.leaRegRegReg r14, rbx, rax
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
    x64.movRegReg rcx, rax
    x64.andRegImm32 rcx, rax, 1
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 1
    x64.callDirect __np_pd_slot
    x64.cmpRegImm32 r8, 0
    x64.setccReg notEqual, rax
    x64.loadRegSlot rcx, slot0
    x64.imulRegRegImm32 rcx, rcx, 8
    x64.leaRegRegImm32 rcx, rcx, 8
    x64.leaRegRegReg rcx, r8, rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, npnopd#30
  npgaclearact:
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.loadRegBaseDisp.word64 rdx, [r8 + 48]
    x64.cmpRegReg rdx, rax
    x64.jcc notEqual, npgaclearnext
  npgaclearlive:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rcx + 0], rdx
    x64.cmpRegImm32 rax, 3
    x64.jcc notEqual, npholdnotwoken
  npgaclearfired:
    x64.movRegImm32 rax, 1
    x64.movRegReg rcx, r13
    x64.shlRegCl rax, rax
    x64.aluBaseDispReg.bitOr [rbx + 16], rax
    x64.jmp npgaclearnext
  npholdnotwoken:
    x64.cmpRegImm32 rax, 2
    x64.jcc equal, npgaclearnext
  npholdnotended: [async-unsafe]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegReg rax, rcx
    x64.jcc notEqual, npholdlost
  npholdpublished:
    x64.leaRegRdata rcx, [rip + __abort_msg_109]  ; "fatal error: runtime abort 109 (netpollUnhookMissed)\x0a"
    x64.movRegImm32 rdx, 53
    x64.movRegImm32 rax, 109
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  npholdlost:
    x64.leaRegRdata rcx, [rip + __abort_msg_115]  ; "fatal error: runtime abort 115 (netpollHoldLost)\x0a"
    x64.movRegImm32 rdx, 49
    x64.movRegImm32 rax, 115
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  npgaclearnext:
    x64.leaRegRegImm32 r13, r13, 1
    x64.jmp npgaclearhdr
  npnopd#30:
    x64.leaRegRdata rcx, [rip + __abort_msg_104]  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    x64.movRegImm32 rdx, 54
    x64.movRegImm32 rax, 104
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  npgamarkbody:
    x64.imulRegRegImm32 rax, r13, 16
    x64.leaRegRegImm32 rax, rax, 32
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.movRegReg r15, r14
    x64.andRegImm32 r15, r14, 1
    x64.movRegReg rcx, r14
    x64.shrRegImm8 rcx, r14, 1
    x64.callDirect __np_pd_slot
    x64.cmpRegImm32 r8, 0
    x64.setccReg notEqual, rax
    x64.imulRegRegImm32 rcx, r15, 8
    x64.leaRegRegImm32 rcx, rcx, 8
    x64.leaRegRegReg rcx, r8, rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, npnopd#29
  npgamarkact:
    x64.movRegImm32 rax, 2
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.leaRegRegImm32 r13, r13, 1
    x64.jmp npgamarkhdr
  npnopd#29:
    x64.leaRegRdata rcx, [rip + __abort_msg_104]  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    x64.movRegImm32 rdx, 54
    x64.movRegImm32 rax, 104
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  npgalookbody:
    x64.imulRegRegImm32 rax, r13, 16
    x64.leaRegRegImm32 rax, rax, 32
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.movRegReg r15, r14
    x64.andRegImm32 r15, r14, 1
    x64.movRegReg rcx, r14
    x64.shrRegImm8 rcx, r14, 1
    x64.callDirect __np_pd_slot
    x64.cmpRegImm32 r8, 0
    x64.setccReg notEqual, rax
    x64.imulRegRegImm32 rcx, r15, 8
    x64.leaRegRegImm32 rcx, rcx, 8
    x64.leaRegRegReg rcx, r8, rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, npnopd#26
  npgalookact:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, npwsnotready
  npgaready:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, __rl_slow#40
  __rl_cont#39:
    x64.aluBaseDispImm32.add [rax + 824], -1
    x64.movRegImm32 r8, 1
    x64.movRegReg rcx, r13
    x64.shlRegCl r8, r8
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  npwsnotready:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, npwsdouble
  npgalooknext:
    x64.leaRegRegImm32 r13, r13, 1
    x64.jmp npgalookhdr
  npwsdouble:
    x64.leaRegRdata rcx, [rip + __abort_msg_107]  ; "fatal error: runtime abort 107 (netpollDoubleWait)\x0a"
    x64.movRegImm32 rdx, 51
    x64.movRegImm32 rax, 107
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  npnopd#26:
    x64.leaRegRdata rcx, [rip + __abort_msg_104]  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    x64.movRegImm32 rdx, 54
    x64.movRegImm32 rax, 104
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  npgalifebody:
    x64.imulRegRegImm32 rax, r13, 16
    x64.leaRegRegImm32 rax, rax, 32
    x64.leaRegRegReg r14, rbx, rax
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
    x64.movRegReg rcx, rax
    x64.andRegImm32 rcx, rax, 1
    x64.storeSlotReg slot1, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 1
    x64.callDirect __np_pd_slot
    x64.cmpRegImm32 r8, 0
    x64.setccReg notEqual, rax
    x64.loadRegSlot rcx, slot1
    x64.imulRegRegImm32 rcx, rcx, 8
    x64.leaRegRegImm32 rcx, rcx, 8
    x64.leaRegRegReg rcx, r8, rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, npnopd#25
  npgalifeact:
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r8 + 48]
    x64.cmpRegReg rcx, rax
    x64.jcc notEqual, npgastale
  npgalifenext:
    x64.leaRegRegImm32 r13, r13, 1
    x64.jmp npgalifehdr
  npgastale:
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, __rl_slow#38
  __rl_cont#37:
    x64.aluBaseDispImm32.add [rax + 824], -1
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  npnopd#25:
    x64.leaRegRdata rcx, [rip + __abort_msg_104]  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    x64.movRegImm32 rdx, 54
    x64.movRegImm32 rax, 104
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rl_slow#36:
    x64.callDirect __rtlock_acquire
    x64.jmp __rl_cont#35
  __rl_slow#42:
    x64.storeSlotReg slot2, rax
    x64.callDirect __rtlock_release
    x64.loadRegSlot rax, slot2
    x64.jmp __rl_cont#41
  __rl_slow#44:
    x64.callDirect __rtlock_acquire
    x64.jmp __rl_cont#43
  __rl_slow#46:
    x64.storeSlotReg slot2, rax
    x64.storeSlotReg slot3, r8
    x64.callDirect __rtlock_release
    x64.movRegReg rcx, r8
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot r8, slot3
    x64.jmp __rl_cont#45
  __rl_slow#40:
    x64.storeSlotReg slot2, rax
    x64.callDirect __rtlock_release
    x64.loadRegSlot rax, slot2
    x64.jmp __rl_cont#39
  __rl_slow#38:
    x64.storeSlotReg slot2, rax
    x64.callDirect __rtlock_release
    x64.loadRegSlot rax, slot2
    x64.jmp __rl_cont#37
}
```

```TargetIr:arm64-macos
data {
  __data_CharacterSet.cachedWhitespacesAndNewlines@0 = i64 0
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
  __gt_quiesce_waiter@104 = i64 0
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
  __sched_num_procs@456 = i64 0
  __sched_ncpu@464 = i64 0
  __sched_shutdown_flag@472 = i64 0
  __sched_lock@480 = i64 0
  __sched_lock_depth_sink@488 = i64 0
  __sched_nmspinning@496 = i64 0
  __sched_needspinning@504 = i64 0
  __sched_sysmon_event@512 = i64 0
  __sched_sysmon_wait@520 = i64 0
  __sched_timer_starts@528 = i64 0
  __io_pipe_seq@536 = i64 0
  __subp_table@544 = i64 0
  __subp_last_error@552 = i64 0
  __slab_arena_list@560 = i64 0
  __slab_arena_map_l1@568 = i64 0
  __slab_state@576 = i64 0
  __mrt_program_started@584 = i8 0
}

func @main {
  entry:
    arm64.prologue 704 record@48
    arm64.storeSlotReg slot79, x28
    arm64.storeSlotReg slot78, x27
    arm64.storeSlotReg slot77, x26
    arm64.storeSlotReg slot76, x25
    arm64.storeSlotReg slot75, x24
    arm64.storeSlotReg slot74, x23
    arm64.storeSlotReg slot73, x22
    arm64.storeSlotReg slot72, x21
    arm64.storeSlotReg slot71, x20
    arm64.storeSlotReg slot70, x19
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_80  ; "-c"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_81  ; "sleep 1; echo hi"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.bl Scheduler.counters
    arm64.storeSlotReg slot1, x0
    arm64.leaRdata x0, __str_rec_82  ; "/bin/sh"
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.orr x20, x0, 1
    arm64.storeSlotReg slot2, x20
  __il_body#17:
    arm64.leaRdata x21, __str_rec_117  ; ""
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.storeSlotReg slot3, x22
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
  __il_body#19:
    arm64.movRegReg x0, x20
    arm64.bl __uretain_Executable
    arm64.movRegReg x0, x22
    arm64.bl __mm_own
    arm64.movRegReg x21, x0
  __il_body#25:
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x23, 1
    arm64.movImm x24, 16777216
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x25 + 0], x0
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x24, 16777216
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x26 + 0], x0
    arm64.storeBaseDispReg.word64 [x26 + 8], x24
  __il_body#29:
    arm64.movImm x24, 0
    arm64.movImm x27, 0
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.storeBaseDispReg.byte [x28 + 0], x24
    arm64.storeBaseDispReg.byte [x28 + 8], x27
  __il_cont#28:
    arm64.movImm x0, 72
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.storeSlotReg slot8, x24
    arm64.movRegReg x0, x20
    arm64.bl __uretain_Executable
    arm64.storeBaseDispReg.word64 [x24 + 0], x20
    arm64.storeBaseDispReg.word64 [x24 + 8], x22
    arm64.movRegReg x0, x21
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movRegReg x0, x23
    arm64.bl __uretain_Environment
    arm64.storeBaseDispReg.word64 [x24 + 24], x23
    arm64.movImm x0, 1
    arm64.bl __uretain_InputSource
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 32], x0
    arm64.storeBaseDispReg.word64 [x24 + 40], x25
    arm64.storeBaseDispReg.word64 [x24 + 48], x26
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 56], x0
    arm64.storeBaseDispReg.word64 [x24 + 64], x28
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
  exitdrop#27:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Executable
  __il_cont#24:
    arm64.loadRegBaseDisp.word64 x20, [x24 + 8]
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.storeBaseDispReg.word64 [x24 + 8], x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 56], x0
  __il_body#35:
    arm64.loadRegBaseDisp.word64 x19, [x24 + 32]
    arm64.movRegReg x0, x19
    arm64.bl __uretain_InputSource
    arm64.movRegReg x20, x0
    arm64.loadRegBaseDisp.word64 x21, [x24 + 40]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.loadRegBaseDisp.word64 x23, [x24 + 48]
    arm64.movRegReg x0, x23
    arm64.bl __mm_retain
    arm64.movRegReg x25, x0
  __il_body#146:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x1, ge
    arm64.cmp x0, 3
    arm64.cset x2, le
    arm64.and x1, x1, x2
    arm64.cbnz x1, matcharm#151
  matchnext#153:
    arm64.cmp x0, 4
    arm64.b.ne matcharm#151
  matcharm#147:
    arm64.sub x19, x19, 4
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_211  ; "the standard input file path"
    arm64.bl requireNulFree
    arm64.movRegReg x26, x9
    arm64.cmp x26, 0
    arm64.b.eq tryok#148
  tryerr#149:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  exitterminal#150:
    arm64.movRegReg x0, x26
    arm64.b __il_cont#145
  tryok#148:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  matchcont#155:
    arm64.movImm x0, 0
    arm64.b __il_cont#145
  matcharm#151:
    arm64.movImm x0, 0
  __il_cont#145:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#140
  tryok#139:
    arm64.leaRdata x1, __str_rec_209  ; "the standard output file path"
    arm64.movRegReg x0, x21
    arm64.bl requireNulFreeOutputFile
    arm64.cmp x9, 0
    arm64.b.ne tryerr#142
  tryok#141:
    arm64.leaRdata x1, __str_rec_210  ; "the standard error file path"
    arm64.movRegReg x0, x23
    arm64.bl requireNulFreeOutputFile
    arm64.cmp x9, 0
    arm64.b.ne tryerr#144
  tryok#143:
    arm64.movImm x19, 0
    arm64.b __il_cont#137
  tryerr#140:
    arm64.movRegReg x19, x0
    arm64.b __il_cont#137
  tryerr#142:
    arm64.movRegReg x19, x9
    arm64.b __il_cont#137
  tryerr#144:
    arm64.movRegReg x19, x9
  __il_cont#137:
    arm64.cmp x19, 0
    arm64.b.eq tryok#36
  tryerr#37:
    arm64.movRegReg x0, x25
    arm64.bl __destruct_OutputDestination
  exitdrop#39:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_OutputDestination
  exitdrop#40:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_InputSource
    arm64.movRegReg x0, x19
    arm64.b exitterminal#38
  tryok#36:
    arm64.movRegReg x0, x25
    arm64.bl __destruct_OutputDestination
    arm64.movRegReg x0, x22
    arm64.bl __destruct_OutputDestination
    arm64.movRegReg x0, x20
    arm64.bl __destruct_InputSource
    arm64.loadRegBaseDisp.word64 x19, [x24 + 0]
    arm64.storeSlotReg slot33, x19
    arm64.movRegReg x0, x19
    arm64.bl __uretain_Executable
    arm64.storeSlotReg slot10, x0
    arm64.loadRegBaseDisp.word64 x20, [x24 + 8]
    arm64.storeSlotReg slot25, x20
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.storeSlotReg slot13, x0
    arm64.loadRegBaseDisp.word64 x21, [x24 + 16]
    arm64.storeSlotReg slot29, x21
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.storeSlotReg slot16, x0
    arm64.loadRegBaseDisp.word64 x0, [x24 + 24]
    arm64.storeSlotReg slot44, x0
    arm64.bl __uretain_Environment
    arm64.storeSlotReg slot19, x0
  __il_body#180:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x0, eq
    arm64.storeSlotReg slot38, x0
    arm64.b.ne matcharm#185
  matcharm#181:
    arm64.sub x19, x19, 1
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.leaRdata x1, __str_rec_202  ; "the program name"
    arm64.movRegReg x0, x19
    arm64.bl requireNulFree
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#182
  tryerr#183:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movRegReg x0, x22
    arm64.b __il_cont#179
  tryok#182:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.b matchcont#190
  matcharm#185:
    arm64.sub x19, x19, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_203  ; "the program path"
    arm64.bl requireNulFree
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#186
  tryerr#187:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
    arm64.b __il_cont#179
  tryok#186:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  matchcont#190:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.leaRdata x1, __str_rec_204  ; "the working directory"
    arm64.bl requireNulFree
    arm64.cmp x9, 0
    arm64.b.ne tryerr#192
  tryok#191:
    arm64.leaRdata x1, __layout_Array_String
    arm64.movRegReg x0, x20
    arm64.bl Array.withIterator
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr#193
    arm64.b forjoin
  __il_body#408:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot50, x20
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __adapt___witness_ArrayIterator_String.Iterator.current
    arm64.movRegReg x21, x0
  __il_cont#407:
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.storeSlotReg slot56, x0
    arm64.leaRdata x0, __str_rec_205  ; "argument "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x24, x0
  __il_body#409:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#411
  __il_cont#406:
    arm64.add x20, x0, 1
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x25
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.add x26, x23, x20
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x28, x23
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x25
    arm64.bl __mm_decref
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x27
    arm64.bl requireNulFree
    arm64.movRegReg x20, x9
    arm64.cmp x20, 0
    arm64.b.eq tryok#198
  tryerr#199:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#200:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
  exitdrop#201:
    arm64.loadRegSlot x0, slot56
    arm64.bl __str_decref
  exitdrop#202:
    arm64.loadRegSlot x0, slot50
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#203:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#179
  tryok#198:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
    arm64.loadRegSlot x0, slot56
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot50
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  __il_body#413:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#431:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#435
    arm64.b __im_move
  tryerr#433:
    arm64.movImm x0, 1
    arm64.b __il_cont#430
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#432:
    arm64.movImm x0, 0
  __il_cont#430:
    arm64.cmp x0, 0
    arm64.b.ne forhdr#193
  tryok#414:
    arm64.movImm x0, 0
  forhdr#193:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#408
  forexit#197:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
  forjoin:
    arm64.loadRegSlot x0, slot44
    arm64.and x0, x0, 7
    arm64.cmp x0, 1
    arm64.b.ne matchnext#213
  matcharm#205:
    arm64.movImm x0, 0
    arm64.b __il_cont#179
  matchnext#213:
    arm64.cmp x0, 2
    arm64.b.ne matcharm#209
  matcharm#206:
    arm64.loadRegSlot x0, slot44
    arm64.sub x19, x0, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x19
    arm64.bl requireNulFreeEnvironment
    arm64.movRegReg x20, x9
    arm64.cmp x20, 0
    arm64.b.eq tryok#207
  tryerr#208:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#179
  tryok#207:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.b matchcont#215
  matcharm#209:
    arm64.loadRegSlot x0, slot44
    arm64.sub x19, x0, 3
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x19
    arm64.bl requireNulFreeEnvironment
    arm64.movRegReg x20, x9
    arm64.cmp x20, 0
    arm64.b.eq tryok#210
  tryerr#211:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#179
  tryok#210:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
  matchcont#215:
    arm64.movImm x0, 0
    arm64.b __il_cont#179
  tryerr#192:
    arm64.movRegReg x0, x9
  __il_cont#179:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#230
  tryerr#171:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#168
  __il_body#230:
    arm64.loadRegSlot x0, slot38
    arm64.cbz x0, matcharm#235
  matcharm#231:
    arm64.loadRegSlot x0, slot33
    arm64.sub x19, x0, 1
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
  __il_body#417:
    arm64.movRegReg x0, x19
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#418
  tryerr#419:
    arm64.movImm x20, 0
    arm64.cbz x1, __il_body#438
  tryerr#242:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movImm x21, 0
    arm64.b __il_cont#239
  tryok#418:
    arm64.movRegReg x20, x0
  __il_body#438:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#445:
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
  __il_cont#444:
    arm64.movImm x22, 0
  whilehdr#439:
    arm64.cmp x22, x21
    arm64.movImm x0, 0
    arm64.b.hs scmerge#441
  andrhs#440:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#447:
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#449
  __il_cont#446:
    arm64.cmp x0, 47
    arm64.cset x0, eq
  scmerge#441:
    arm64.cbz x0, __il_cont#437
  leadingSeparators:
    arm64.add x22, x22, 1
    arm64.b whilehdr#439
  __il_cont#437:
    arm64.cmp x22, 0
    arm64.movImm x21, 1
    arm64.b.hi critsplit#485
  __il_body#265:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#423:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont#422:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#267
  empty:
    arm64.movImm x21, 0
    arm64.b __il_cont#264
  ifcont#267:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#425:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.b whilehdr#268
  scan:
    arm64.sub x21, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x0, x16
    arm64.and x1, x1, 1
    arm64.eor x0, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x0, x16
    arm64.and x0, x0, x21
    arm64.orr x0, x1, x0
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#274
  __rc_ok#273:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#427:
    arm64.movRegReg x1, x21
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#429
  __il_cont#426:
    arm64.cmp x0, 47
    arm64.b.ne ifcont#272
  isSep:
    arm64.add x21, x21, 1
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic#276
    arm64.b __il_cont#264
  ifcont#272:
    arm64.movRegReg x0, x21
  whilehdr#268:
    arm64.cmp x0, 0
    arm64.b.hi scan
  whileexit#270:
    arm64.movImm x21, 0
  __il_cont#264:
    arm64.cmp x21, 0
    arm64.b.ne ifcont#260
  noSep:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
    arm64.b __il_cont#257
  ifcont#260:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#421:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
  __il_cont#420:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl String.addressableBytes
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __managed_slice
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#261
  tryerr#262:
    arm64.leaRdata x0, __str_blob_126  ; "panic at FilePath.maxon:354: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryok#261:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movRegReg x0, x21
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitterminal#263:
    arm64.movRegReg x21, x22
  __il_cont#257:
    arm64.loadRegBaseDisp.word64 x1, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __str_eq
    arm64.eor x22, x0, 1
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.b scmerge#244
  critsplit#485:
    arm64.movRegReg x22, x21
  scmerge#244:
    arm64.cbz x22, ternaryfalse#246
  ternarytrue#245:
    arm64.loadRegSlot x1, slot29
    arm64.movRegReg x0, x20
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x21, x0
    arm64.b matchcont#247
  ternaryfalse#246:
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
  matchcont#247:
    arm64.leaRdata x23, __str_rec_119  ; "/"
    arm64.cbz x22, ifcont#250
  isAPath:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x21
    arm64.b exitterminal#249
  ifcont#250:
    arm64.movRegReg x0, x21
    arm64.bl String.cstr
    arm64.bl __gt_subp_resolve_path
    arm64.bl String.init
    arm64.movRegReg x22, x0
  __il_body#457:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 8]
  __il_body#453:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x22
    arm64.movRegReg x3, x23
    arm64.bl findIn
    arm64.cmp x9, 0
    arm64.b.eq tryok#454
  tryerr#455:
    arm64.movImm x0, 0
    arm64.b __il_cont#450
  tryok#454:
    arm64.movImm x0, 1
  __il_cont#450:
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#255
  notOnPath:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x23 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#253:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#254:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
  exitterminal#252:
    arm64.movImm x20, 0
    arm64.movRegReg x21, x20
    arm64.movRegReg x20, x23
    arm64.b __il_cont#239
  ifcont#255:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#256:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
  exitterminal#249:
    arm64.movImm x20, 0
    arm64.movRegReg x21, x0
  __il_cont#239:
    arm64.cmp x20, 0
    arm64.b.eq tryok#232
  tryerr#233:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
  exitterminal#234:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.b __il_cont#229
  tryok#232:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movRegReg x0, x21
    arm64.b matchcont#238
  matcharm#235:
    arm64.loadRegSlot x0, slot33
    arm64.sub x19, x0, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.loadRegSlot x1, slot29
    arm64.movRegReg x0, x19
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x20
  matchcont#238:
    arm64.movImm x1, 0
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x1
  __il_cont#229:
    arm64.storeSlotReg slot26, x19
    arm64.cmp x0, 0
    arm64.b.eq __il_body#217
  tryerr#173:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#168
  __il_body#217:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl appendNulTerminated
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.movImm x21, 0
    arm64.b forhdr#218
  arg:
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#226
  __im_load#227:
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x1 + x21*8 + 0]
    arm64.movImm x2, 0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
    arm64.cmp x0, 0
    arm64.cset x3, ne
    arm64.leaFuncAddr x4, __gt_promise_drop
    arm64.cmp x0, x4
    arm64.cset x0, ne
    arm64.and x0, x3, x0
    arm64.cbz x0, critsplit#477
  __im_empty:
    arm64.cmp x1, 0
    arm64.b.eq __im_slow#226
  critsplit#478:
    arm64.movRegReg x9, x2
    arm64.b __im_cont#225
  critsplit#477:
    arm64.movRegReg x9, x2
  __im_cont#225:
    arm64.cmp x9, 0
    arm64.b.ne tryerr#223
  tryok#222:
    arm64.movRegReg x0, x20
    arm64.bl appendNulTerminated
  forstep#220:
    arm64.add x21, x21, 1
  forhdr#218:
    arm64.cmp x21, x19
    arm64.b.lo arg
  forexit#221:
    arm64.movRegReg x0, x20
    arm64.bl String.from
    arm64.storeSlotReg slot30, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#216:
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.add x0, x0, 1
    arm64.storeSlotReg slot22, x0
  __il_body#278:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.loadRegSlot x0, slot44
    arm64.and x0, x0, 7
    arm64.cmp x0, 1
    arm64.b.ne matchnext#290
  __il_body#300:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x1, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movRegReg x0, x20
    arm64.bl String.from
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#299:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x21
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.b exitterminal#280
  matchnext#290:
    arm64.cmp x0, 2
    arm64.b.ne matcharm#286
  matcharm#281:
    arm64.loadRegSlot x0, slot44
    arm64.sub x20, x0, 2
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
  __il_body#303:
    arm64.movRegReg x0, x20
    arm64.bl namesByKey
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#305
  __il_body#324:
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x23, 0
  eachEntry:
    arm64.movRegReg x0, x23
    arm64.bl __env_entry
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x0, 0
    arm64.b.ne ifcont#329
  pastTheEnd:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  whileexit#327:
    arm64.movImm x0, 0
    arm64.b __il_cont#323
  ifcont#329:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x0, 1
    arm64.b.ne ifcont#337
  oneByte:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x24
    arm64.bl __managed_byte_at
    arm64.cmp x9, 0
    arm64.b.ne tryerr#332
  tryok#331:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#337
  hostFailed:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#335:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitterminal#334:
    arm64.movImm x0, 3
    arm64.movImm x22, 0
  __il_cont#323:
    arm64.cmp x0, 0
    arm64.b.eq tryok#306
  tryerr#307:
    arm64.leaRdata x22, __str_rec_212  ; "this process's environment could not be read, so no child can inherit it"
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x23 + 0], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x23 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Map_String_String
    arm64.b __il_cont#302
  tryok#306:
    arm64.movImm x0, 0
    arm64.b forhdr#309
  eachInherited:
    arm64.loadRegBaseDisp.word64 x1, [x22 + 0]
    arm64.loadRegBaseIndexScale.word64 x23, [x1 + x0*8 + 0]
  __im_cont#322:
    arm64.cmp x23, 0
    arm64.b.eq emptyslot
  slotfilled:
    arm64.movRegReg x0, x23
    arm64.bl __str_retain
    arm64.storeSlotReg slot40, x0
  __il_body#369:
    arm64.movRegReg x0, x23
    arm64.bl String.toByteArray
    arm64.movRegReg x24, x0
    arm64.movImm x25, 0
  __il_body#373:
    arm64.loadRegBaseDisp.word64 x26, [x24 + 8]
    arm64.movImm x1, 1
  __us_test:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.b forhdr#374
  eachByte#375:
    arm64.cmp x1, x26
    arm64.b.hs __im_slow#383
  __im_load#384:
    arm64.add x2, x0, x1
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  tryok#378:
    arm64.cmp x2, 61
    arm64.b.eq atSeparator
  forstep#376:
    arm64.add x1, x1, 1
  forhdr#374:
    arm64.cmp x1, x26
    arm64.b.lo eachByte#375
    arm64.b __il_body#385
  atSeparator:
    arm64.movRegReg x26, x1
  __il_body#385:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x27, x0
    arm64.movRegReg x1, x25
    arm64.b forhdr#386
  eachByte#387:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#394
  __im_load#395:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
  tryok#390:
    arm64.loadRegBaseDisp.word64 x0, [x27 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x27 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#397
  __im_bounds#398:
    arm64.loadRegBaseDisp.word64 x3, [x27 + 16]
    arm64.cmp x0, x3
    arm64.b.hs __im_slow#397
  __im_buffer#399:
    arm64.loadRegBaseDisp.word64 x3, [x27 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#397
  __im_viewed#400:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#397
  __im_store#401:
    arm64.add x3, x3, x0
    arm64.storeBaseDispReg.byte [x3 + 0], x2
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
  forstep#388:
    arm64.add x1, x1, 1
  forhdr#386:
    arm64.cmp x1, x26
    arm64.b.lo eachByte#387
  forexit#389:
    arm64.movRegReg x0, x27
    arm64.bl String.from
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x27
    arm64.bl __managed_decref
  __il_cont#371:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  __il_cont#368:
    arm64.movRegReg x0, x25
    arm64.bl Process.envNameKey
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.leaRdata x2, __witness_String.Hashable
    arm64.leaRdata x3, __witness_String.Equatable
    arm64.leaRdata x4, __layout_Map_String_String
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x24
    arm64.bl Map.get
    arm64.movRegReg x2, x0
    arm64.storeSlotReg slot46, x2
    arm64.cmp x9, 0
    arm64.b.ne ifelse
  overridden:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl appendEnvEntry
    arm64.leaRdata x23, __witness_String.Hashable
    arm64.leaRdata x25, __witness_String.Equatable
    arm64.leaRdata x0, __layout_Map_String_String
    arm64.storeSlotReg slot52, x0
  __il_body#461:
    arm64.movImm x26, 5381
  __il_body#469:
    arm64.loadRegBaseDisp.word64 x27, [x24 + 8]
  __il_cont#468:
    arm64.movImm x28, 0
    arm64.b forhdr#462
  hash_loop:
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x28
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#467
  tryok#466:
    arm64.movImm x16, 33
    arm64.mul x1, x26, x16
    arm64.add x0, x1, x0
  forstep#464:
    arm64.add x28, x28, 1
    arm64.movRegReg x26, x0
  forhdr#462:
    arm64.cmp x28, x27
    arm64.b.lo hash_loop
  forexit#465:
    arm64.movImm x0, 4294967295
    arm64.and x2, x26, x0
  __il_cont#458:
    arm64.loadRegSlot x5, slot52
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x24
    arm64.movRegReg x3, x23
    arm64.movRegReg x4, x25
    arm64.bl Map.findSlot
    arm64.movRegReg x26, x0
    arm64.cmp x9, 0
    arm64.b.ne __il_cont#344
  tryok#346:
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x23
    arm64.movRegReg x3, x25
    arm64.bl Map.slotHoldsKey
    arm64.cbz x0, __il_cont#344
  found:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.movRegReg x1, x26
    arm64.bl Array.clearSlot
    arm64.cmp x9, 0
    arm64.b.ne tryerr#350
  tryok#349:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.movRegReg x1, x26
    arm64.bl Array.clearSlot
    arm64.cmp x9, 0
    arm64.b.ne tryerr#352
  tryok#351:
    arm64.movImm x0, 2
    arm64.loadRegBaseDisp.word64 x1, [x21 + 16]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.cmp x26, x2
    arm64.b.hs __im_slow#361
  __im_buffer#362:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x3, [x1 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach
  __im_viewed#366:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach
  critsplit#480:
    arm64.movRegReg x1, x2
  __im_detached:
    arm64.add x1, x1, x26
    arm64.storeBaseDispReg.byte [x1 + 0], x0
  tryok#353:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 32]
    arm64.sub x0, x0, 1
    arm64.movImm x1, 9223372036854775808
    arm64.cmp x0, x1
    arm64.b.hi __rc_panic#357
  __rc_ok#356:
    arm64.storeBaseDispReg.word64 [x21 + 32], x0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 48]
    arm64.add x0, x0, 1
    arm64.movImm x1, 9223372036854775808
    arm64.cmp x0, x1
    arm64.b.hi __rc_panic#359
  __rc_ok#358:
    arm64.storeBaseDispReg.word64 [x21 + 48], x0
  __il_cont#344:
    arm64.loadRegSlot x0, slot46
    arm64.bl __str_decref
    arm64.b ifcont#317
  ifelse:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl appendNulTerminated
  ifcont#317:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot40
    arm64.bl __str_decref
  forstep#311:
    arm64.loadRegSlot x0, slot35
    arm64.add x0, x0, 1
  forhdr#309:
    arm64.storeSlotReg slot35, x0
    arm64.loadRegBaseDisp.word64 x1, [x22 + 8]
    arm64.cmp x0, x1
    arm64.b.lt eachInherited
  forexit#312:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl appendEnvEntries
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#319:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Map_String_String
  exitterminal#318:
    arm64.movImm x21, 0
    arm64.movRegReg x23, x21
    arm64.b __il_cont#302
  tryerr#305:
    arm64.movRegReg x23, x9
  __il_cont#302:
    arm64.cmp x23, 0
    arm64.b.eq tryok#282
  tryerr#283:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.b exitdrop#285
  tryok#282:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.b matchcont#292
  ifcont#337:
    arm64.movRegReg x0, x24
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x1, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#339
  __im_bounds#340:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#339
  __im_buffer#341:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#339
  __im_viewed#342:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#339
  __im_store#343:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
  __im_cont#338:
    arm64.add x23, x23, 1
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
    arm64.b eachEntry
  matcharm#286:
    arm64.loadRegSlot x0, slot44
    arm64.sub x20, x0, 3
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
    arm64.movRegReg x0, x20
    arm64.bl namesByKey
    arm64.movRegReg x21, x0
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#287
  tryerr#288:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x23, x22
  exitdrop#285:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal#284:
    arm64.movImm x19, 0
    arm64.b __il_cont#277
  tryok#287:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl appendEnvEntries
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
  matchcont#292:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#294
  __im_bounds#295:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#294
  __im_buffer#296:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#294
  __im_viewed#297:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#294
  __im_store#298:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
  __im_cont#293:
    arm64.movRegReg x0, x19
    arm64.bl String.from
    arm64.movImm x1, 0
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
  exitterminal#280:
    arm64.movImm x19, 0
    arm64.movRegReg x23, x19
    arm64.movRegReg x19, x0
  __il_cont#277:
    arm64.cmp x23, 0
    arm64.b.eq tryok#174
  tryerr#175:
    arm64.loadRegSlot x0, slot30
    arm64.bl __str_decref
  exitdrop#177:
    arm64.loadRegSlot x0, slot26
    arm64.bl __str_decref
  exitterminal#176:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x23
    arm64.b __il_cont#168
  tryok#174:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.loadRegSlot x0, slot26
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.loadRegSlot x0, slot30
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.loadRegSlot x0, slot22
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.storeBaseDispReg.word64 [x20 + 24], x19
    arm64.loadRegSlot x0, slot26
    arm64.bl __str_decref
  exitterminal#178:
    arm64.movImm x19, 0
    arm64.xchgRegReg x19, x20
  __il_cont#168:
    arm64.storeSlotReg slot12, x19
    arm64.cmp x20, 0
    arm64.b.eq tryok#41
  tryerr#42:
    arm64.loadRegSlot x0, slot19
    arm64.bl __destruct_Environment
  exitdrop#43:
    arm64.loadRegSlot x0, slot16
    arm64.bl __destruct_FilePath
  exitdrop#44:
    arm64.loadRegSlot x0, slot13
    arm64.bl __managed_decref
  exitdrop#45:
    arm64.loadRegSlot x0, slot10
    arm64.bl __destruct_Executable
    arm64.movRegReg x0, x20
    arm64.b exitterminal#38
  tryok#41:
    arm64.loadRegSlot x0, slot19
    arm64.bl __destruct_Environment
    arm64.loadRegSlot x0, slot16
    arm64.bl __destruct_FilePath
    arm64.loadRegSlot x0, slot13
    arm64.bl __managed_decref
    arm64.loadRegSlot x0, slot10
    arm64.bl __destruct_Executable
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x20, [x0 + 32]
    arm64.movRegReg x0, x20
    arm64.bl __uretain_InputSource
    arm64.movRegReg x21, x0
  __il_body#88:
    arm64.and x0, x20, 7
    arm64.sub x0, x0, 1
    arm64.jumpTable x0, default=matchdead [[0]=matcharm#89, [1]=matcharm#90, [2]=matcharm#91, [3]=matcharm#92, [4]=matcharm#93] @__jumptable_393
  matchdead:
  matcharm#89:
    arm64.movImm x20, 0
    arm64.leaRdata x0, __str_rec_117  ; ""
    arm64.movImm x22, 0
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x22
    arm64.bl StdioRuntimeTriple.create
    arm64.movRegReg x20, x0
    arm64.b matchcont#95
  matcharm#90:
    arm64.movImm x20, 1
    arm64.leaRdata x0, __str_rec_117  ; ""
    arm64.movImm x22, 0
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x22
    arm64.bl StdioRuntimeTriple.create
    arm64.movRegReg x20, x0
    arm64.b matchcont#95
  matcharm#91:
    arm64.movImm x20, 2
    arm64.leaRdata x0, __str_rec_117  ; ""
    arm64.movImm x22, 0
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x22
    arm64.bl StdioRuntimeTriple.create
    arm64.movRegReg x20, x0
    arm64.b matchcont#95
  matcharm#92:
    arm64.sub x20, x20, 4
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
    arm64.movImm x22, 3
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.movImm x23, 0
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x0, x25, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.add x27, x26, 56
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x25
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x23
    arm64.bl StdioRuntimeTriple.create
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x20, x22
    arm64.b matchcont#95
  matcharm#93:
    arm64.movImm x20, 4
    arm64.leaRdata x0, __str_rec_117  ; ""
    arm64.movImm x22, 0
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x22
    arm64.bl StdioRuntimeTriple.create
    arm64.movRegReg x20, x0
  matchcont#95:
    arm64.storeSlotReg slot5, x20
  __il_cont#87:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_InputSource
    arm64.loadRegSlot x21, slot8
    arm64.loadRegBaseDisp.word64 x22, [x21 + 40]
    arm64.movRegReg x0, x22
    arm64.bl __mm_retain
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl outputTripleFromDestination
    arm64.movRegReg x22, x0
    arm64.storeSlotReg slot7, x22
    arm64.movRegReg x0, x23
    arm64.bl __destruct_OutputDestination
    arm64.loadRegBaseDisp.word64 x23, [x21 + 48]
    arm64.movRegReg x0, x23
    arm64.bl __mm_retain
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.bl outputTripleFromDestination
    arm64.movRegReg x23, x0
    arm64.storeSlotReg slot37, x23
    arm64.movRegReg x0, x24
    arm64.bl __destruct_OutputDestination
    arm64.loadRegBaseDisp.word64 x0, [x21 + 64]
  __il_body#96:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
    arm64.cbz x2, ifcont#98
  __rc_ok#101:
    arm64.movImm x1, 1
  ifcont#98:
    arm64.loadRegBaseDisp.byte x0, [x0 + 8]
    arm64.cbz x0, critsplit#473
  newGroup:
    arm64.orr x21, x1, 2
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic#105
  __rc_chk#106:
    arm64.cmp x21, 15
    arm64.b.gt __rc_panic#105
    arm64.b __il_cont#86
  critsplit#473:
    arm64.movRegReg x21, x1
  __il_cont#86:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.addressableBytes
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x25, [x19 + 16]
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x26, [x0 + 16]
    arm64.movRegReg x0, x26
    arm64.bl __mm_retain
    arm64.storeSlotReg slot18, x0
  __il_body#107:
    arm64.loadRegBaseDisp.word64 x0, [x26 + 0]
    arm64.bl __str_retain
    arm64.storeSlotReg slot15, x0
  __il_cont#85:
    arm64.bl String.cstr
    arm64.movRegReg x26, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.movRegReg x0, x27
    arm64.bl __str_retain
    arm64.storeSlotReg slot21, x0
    arm64.movRegReg x0, x27
    arm64.bl String.addressableBytes
    arm64.movRegReg x27, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot51, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.storeSlotReg slot45, x0
    arm64.loadRegBaseDisp.word64 x19, [x20 + 8]
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.storeSlotReg slot24, x0
    arm64.movRegReg x0, x19
    arm64.bl String.cstr
    arm64.storeSlotReg slot39, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.storeSlotReg slot34, x0
    arm64.loadRegBaseDisp.word64 x19, [x22 + 8]
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.storeSlotReg slot28, x0
    arm64.movRegReg x0, x19
    arm64.bl String.cstr
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x20, [x22 + 16]
    arm64.loadRegBaseDisp.word64 x22, [x23 + 0]
    arm64.loadRegBaseDisp.word64 x28, [x23 + 8]
    arm64.movRegReg x0, x28
    arm64.bl __str_retain
    arm64.storeSlotReg slot32, x0
    arm64.movRegReg x0, x28
    arm64.bl String.cstr
    arm64.loadRegBaseDisp.word64 x1, [x23 + 16]
    arm64.storeOutgoingArg [sp + arg0], x19
    arm64.storeOutgoingArg [sp + arg1], x20
    arm64.storeOutgoingArg [sp + arg2], x22
    arm64.storeOutgoingArg [sp + arg3], x0
    arm64.storeOutgoingArg [sp + arg4], x1
    arm64.storeOutgoingArg [sp + arg5], x21
    arm64.loadRegSlot x7, slot34
    arm64.loadRegSlot x6, slot39
    arm64.loadRegSlot x5, slot45
    arm64.loadRegSlot x4, slot51
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.movRegReg x3, x27
    arm64.bl __gt_subp_attached_spawn
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot42, x19
    arm64.loadRegSlot x0, slot32
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot28
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot24
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot21
    arm64.bl __str_decref
    arm64.movRegReg x0, x27
    arm64.bl __managed_decref
    arm64.loadRegSlot x0, slot18
    arm64.bl __destruct_FilePath
    arm64.loadRegSlot x0, slot15
    arm64.bl __str_decref
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
    arm64.cmp x19, -1
    arm64.b.ne ifcont#51
  spawnFailed:
    arm64.loadRegSlot x0, slot12
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
  __il_body#109:
    arm64.bl __gt_subp_last_error_code
    arm64.movRegReg x20, x0
    arm64.asr x0, x20, 32
    arm64.bl __enum_from_raw_SpawnPreparation
    arm64.cmp x9, 0
    arm64.b.ne tryerr#111
  tryok#110:
    arm64.movImm x1, 4294967295
    arm64.and x1, x20, x1
    arm64.cmp x1, 2
    arm64.movImm x2, 1
    arm64.b.eq critsplit#474
  orrhs:
    arm64.cmp x1, 3
    arm64.cset x1, eq
    arm64.b scmerge#113
  critsplit#474:
    arm64.movRegReg x1, x2
  scmerge#113:
    arm64.movImm x2, 0
    arm64.cbz x1, critsplit#475
  andrhs#114:
    arm64.cmp x0, 0
    arm64.cset x0, eq
    arm64.b scmerge#115
  critsplit#475:
    arm64.movRegReg x0, x2
  scmerge#115:
    arm64.cbz x0, ifcont#117
  executableAbsent:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.b __il_cont#108
  ifcont#117:
    arm64.bl lastErrorMessage
    arm64.movRegReg x19, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
  __il_cont#108:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_StdioRuntimeTriple
    arm64.b exitdrop#48
  ifcont#51:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic#79
  __rc_ok#78:
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x20, [x0 + 32]
    arm64.movRegReg x0, x20
    arm64.bl __uretain_InputSource
    arm64.movRegReg x21, x0
  __il_body#157:
    arm64.and x0, x20, 7
    arm64.cmp x0, 1
    arm64.cset x1, ge
    arm64.cmp x0, 2
    arm64.cset x2, le
    arm64.and x1, x1, x2
    arm64.cbnz x1, __il_cont#156
  matchnext#161:
    arm64.cmp x0, 3
    arm64.b.ne __il_cont#156
  matcharm#158:
    arm64.sub x20, x20, 3
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
  __il_body#165:
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movRegReg x1, x0
  __il_body#405:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
  __il_cont#404:
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_queue_stdin
    arm64.cmp x0, 0
    arm64.b.eq __il_cont#164
  refused:
    arm64.leaRdata x0, __str_rec_221  ; "panic at Subprocess.maxon:865: "
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.leaRdata x0, __str_rec_222  ; "Subprocess: the runtime refused the stdin payload of handle "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_223  ; ", whose spawn has just answered it with a pipe"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_88  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot55, x0
    arm64.add x1, x21, x23
    arm64.add x1, x1, x19
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot43, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot49, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x28, x21
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x20, x20, x23
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x20, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.loadRegSlot x2, slot55
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x19, slot49
    arm64.storeBaseDispReg.word64 [x19 + 0], x28
    arm64.loadRegSlot x0, slot43
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __il_cont#164:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  __il_cont#156:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_InputSource
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_get_pid
    arm64.movRegReg x20, x0
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x1, [x0 + 56]
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_wait_collect
    arm64.movRegReg x19, x0
    arm64.cmp x19, 0
    arm64.b.ge ifcont#57
  waitFailed:
    arm64.cmp x19, -1
    arm64.b.ne ternaryfalse#54
  ternarytrue#53:
    arm64.bl lastErrorMessage
    arm64.movRegReg x19, x0
    arm64.b matchcont#55
  ternaryfalse#54:
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x19, [x0 + 56]
    arm64.bl lastErrorMessage
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot48, x20
  __il_body#119:
    arm64.leaRdata x0, __str_rec_225  ; "timed out after "
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_226  ; "ms, but the child's process tree could not be fully ended: "
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x26, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x20 + 8]
    arm64.add x0, x22, x19
    arm64.add x0, x0, x25
    arm64.add x0, x0, x20
    arm64.storeSlotReg slot54, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x21, x28, x22
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x21, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot54
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_cont#118:
    arm64.loadRegSlot x0, slot48
    arm64.bl __str_decref
    arm64.movRegReg x19, x27
  matchcont#55:
    arm64.loadRegSlot x0, slot42
    arm64.bl __gt_subp_release
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
  exitdrop#56:
    arm64.loadRegSlot x0, slot37
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#48:
    arm64.loadRegSlot x0, slot7
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#49:
    arm64.loadRegSlot x0, slot5
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#50:
    arm64.loadRegSlot x0, slot12
    arm64.bl __destruct_SpawnOperands
  exitterminal#47:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x20
    arm64.b __il_cont#34
  ifcont#57:
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_kind
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_code
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_stdout
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_stderr
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_duration
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x23
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x24
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x27, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_release
    arm64.loadRegSlot x0, slot42
    arm64.bl __gt_subp_release
    arm64.movRegReg x0, x21
    arm64.bl __enum_from_raw_ResultStatusKind
    arm64.cmp x9, 0
    arm64.b.ne tryerr#59
  tryok#58:
    arm64.cmp x22, 0
    arm64.b.lt __rc_panic#81
  __rc_chk#82:
    arm64.movImm x1, 4294967295
    arm64.cmp x22, x1
    arm64.b.gt __rc_panic#81
  __rc_ok#129:
    arm64.cmp x0, 0
    arm64.b.ne matchnext#126
  matcharm#122:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x22
    arm64.movImm x19, 0
    arm64.movRegReg x21, x19
    arm64.movRegReg x19, x0
    arm64.b __il_cont#120
  matchnext#126:
    arm64.cmp x0, 1
    arm64.b.ne matcharm#124
  matcharm#123:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x22
    arm64.movImm x19, 0
    arm64.movRegReg x21, x19
    arm64.movRegReg x19, x0
    arm64.b __il_cont#120
  matcharm#124:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.storeBaseDispReg.word64 [x19 + 8], x25
    arm64.movRegReg x0, x26
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movRegReg x0, x27
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x21, 0
    arm64.xchgRegReg x19, x21
  __il_cont#120:
    arm64.cmp x21, 0
    arm64.b.eq tryok#60
  tryerr#61:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#62:
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
  exitdrop#63:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#64:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitdrop#65:
    arm64.loadRegSlot x0, slot37
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#66:
    arm64.loadRegSlot x0, slot7
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#67:
    arm64.loadRegSlot x0, slot5
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#68:
    arm64.loadRegSlot x0, slot12
    arm64.bl __destruct_SpawnOperands
    arm64.movRegReg x0, x21
  exitterminal#38:
    arm64.movImm x1, 0
    arm64.xchgRegReg x0, x1
    arm64.b __il_cont#34
  tryok#60:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#84
  __rc_ok#83:
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x26
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x27
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
  __il_body#133:
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x28 + 0], x19
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x28 + 8], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x28 + 16], x0
    arm64.storeBaseDispReg.word64 [x28 + 24], x20
    arm64.storeBaseDispReg.word64 [x28 + 32], x25
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#135:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#136:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  __il_cont#132:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  exitdrop#70:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#71:
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
  exitdrop#72:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#73:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitdrop#74:
    arm64.loadRegSlot x0, slot37
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#75:
    arm64.loadRegSlot x0, slot7
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#76:
    arm64.loadRegSlot x0, slot5
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#77:
    arm64.loadRegSlot x0, slot12
    arm64.bl __destruct_SpawnOperands
  exitterminal#69:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x28
  __il_cont#34:
    arm64.cmp x1, 0
    arm64.b.eq tryok#32
  tryerr#33:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x1
    arm64.b __il_cont#30
  tryok#32:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x0
  __il_cont#30:
    arm64.cmp x20, 0
    arm64.b.eq tryok#20
  tryerr#21:
    arm64.loadRegSlot x0, slot8
    arm64.bl __destruct_Configuration
  exitterminal#22:
    arm64.movImm x19, 0
    arm64.cmp x20, 0
    arm64.b.ne tryerr#2
    arm64.b __il_cont#18
  tryok#20:
    arm64.loadRegSlot x0, slot8
    arm64.bl __destruct_Configuration
  __il_cont#18:
    arm64.storeSlotReg slot4, x19
  tryok#1:
    arm64.loadRegSlot x0, slot3
    arm64.bl __destruct_FilePath
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_Executable
    arm64.bl Scheduler.counters
    arm64.storeSlotReg slot6, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 64]
    arm64.loadRegSlot x2, slot1
    arm64.loadRegBaseDisp.word64 x3, [x2 + 64]
    arm64.sub x20, x1, x3
    arm64.loadRegBaseDisp.word64 x0, [x0 + 72]
    arm64.loadRegBaseDisp.word64 x1, [x2 + 72]
    arm64.sub x21, x0, x1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
  __il_body#403:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.storeSlotReg slot9, x22
  __il_cont#402:
    arm64.leaRdata x1, __str_rec_83  ; "hi"
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.startsWith
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_84  ; "timerpolls="
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.cmp x20, 16
    arm64.cset x20, ls
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot53, x1
    arm64.movRegReg x0, x20
    arm64.bl __bool_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_85  ; " onpoller="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot47, x1
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.cmp x21, 1
    arm64.cset x21, hs
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot41, x1
    arm64.movRegReg x0, x21
    arm64.bl __bool_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_86  ; " outLen="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot36, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot31, x1
    arm64.movRegReg x0, x22
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_87  ; " matches="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot27, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __bool_to_string
    arm64.storeSlotReg slot23, x0
    arm64.leaRdata x1, __str_rec_88  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot20, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot17, x1
    arm64.add x2, x24, x20
    arm64.add x2, x2, x25
    arm64.add x2, x2, x21
    arm64.add x2, x2, x26
    arm64.add x2, x2, x22
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot11, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot14, x0
    arm64.add x19, x0, 56
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x23, x19, x24
    arm64.loadRegSlot x1, slot53
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x23, x20
    arm64.loadRegSlot x1, slot47
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x20, x20, x25
    arm64.loadRegSlot x1, slot41
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x20, x21
    arm64.loadRegSlot x1, slot36
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x20, x20, x26
    arm64.loadRegSlot x1, slot31
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x20, x20, x22
    arm64.loadRegSlot x1, slot27
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x20, x20, x27
    arm64.loadRegSlot x2, slot23
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot23
    arm64.add x0, x20, x0
    arm64.loadRegSlot x2, slot17
    arm64.loadRegSlot x1, slot20
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot14
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.loadRegSlot x1, slot11
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.loadRegSlot x0, slot53
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot41
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot31
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
  __il_body#13:
    arm64.loadRegSlot x0, slot14
    arm64.bl __write_stdout
  __il_cont#12:
    arm64.loadRegSlot x0, slot14
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot9
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#8
  __rc_chk#9:
    arm64.loadRegSlot x0, slot9
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#8
  __rc_ok#7:
    arm64.loadRegSlot x0, slot6
    arm64.bl __mm_decref
  exitdrop#4:
    arm64.loadRegSlot x0, slot4
    arm64.bl __destruct_CollectedOutput
  exitdrop#5:
    arm64.loadRegSlot x0, slot1
    arm64.bl __mm_decref
  exitdrop#6:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal#3:
    arm64.loadRegSlot x0, slot9
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __im_slow#435:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#433
    arm64.b tryok#432
  __im_slow#226:
    arm64.loadRegSlot x0, slot25
    arm64.movRegReg x1, x21
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.b __im_cont#225
  __im_slow#383:
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.cmp x9, 0
    arm64.b.ne tryerr#379
    arm64.b tryok#378
  __im_slow#394:
    arm64.storeSlotReg slot58, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.loadRegSlot x1, slot58
    arm64.cmp x9, 0
    arm64.b.ne tryerr#391
  critsplit#482:
    arm64.movRegReg x2, x0
    arm64.b tryok#390
  __im_slow#397:
    arm64.storeSlotReg slot58, x1
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x2
    arm64.bl __managed_push
    arm64.loadRegSlot x1, slot58
    arm64.b forstep#388
  __im_slow#361:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x26
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#354
    arm64.b tryok#353
  __im_detach:
    arm64.storeSlotReg slot58, x1
    arm64.movRegReg x0, x1
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 2
    arm64.loadRegSlot x1, slot58
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.b __im_detached
  __im_slow#339:
    arm64.movRegReg x0, x22
    arm64.bl __managed_push
    arm64.b __im_cont#338
  __im_slow#294:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.b __im_cont#293
  __rc_panic#411:
    arm64.leaRdata x0, __str_blob_276  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#449:
    arm64.leaRdata x0, __str_blob_187  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#274:
    arm64.leaRdata x0, __str_blob_282  ; "panic at FilePath.maxon:334: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#429:
    arm64.leaRdata x0, __str_blob_187  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#276:
    arm64.leaRdata x0, __str_blob_283  ; "panic at FilePath.maxon:337: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#223:
    arm64.leaRdata x0, __str_blob_200  ; "panic at Subprocess.maxon:649: buildArgvBlob: args.get(i) out of bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#332:
    arm64.leaRdata x0, __str_blob_160  ; "panic at Process.maxon:117: Process.currentEnvironmentEntries: byteAt(0) of a one-byte answer\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  emptyslot:
    arm64.leaRdata x0, __str_rec_213  ; "panic at Subprocess.maxon:776: "
  __rc_ok#320:
    arm64.loadRegSlot x1, slot35
    arm64.bl __emptySlotMessage
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#379:
    arm64.leaRdata x0, __str_rec_161  ; "panic at Process.maxon:181: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.leaRdata x0, __str_rec_162  ; "envEntrySeparatorAt: i < n = bytes.count() but get("
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x6, 0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.movRegReg x1, x6
    arm64.bl __mm_alloc
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x6
    arm64.bl __uint_to_string
    arm64.movRegReg x7, x0
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.leaRdata x0, __str_rec_163  ; ") failed"
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.leaRdata x0, __str_rec_88  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x9, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x10, [x0 + 8]
    arm64.add x0, x3, x5
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x10
    arm64.add x11, x0, 57
    arm64.movImm x12, 0
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x11
    arm64.movRegReg x1, x12
    arm64.bl __mm_alloc
    arm64.movRegReg x11, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.add x12, x11, 56
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x12
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x12, x3
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x4
    arm64.movRegReg x2, x5
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x2, x5
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x6
    arm64.movRegReg x2, x7
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x2, x7
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x1, x2, x8
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x9
    arm64.movRegReg x2, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.storeBaseDispReg.word64 [x11 + 0], x12
    arm64.storeBaseDispReg.word64 [x11 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x11 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x11 + 24], x0
    arm64.storeSlotReg slot68, x11
    arm64.movRegReg x0, x6
    arm64.bl __mm_decref
    arm64.loadRegSlot x11, slot68
    arm64.loadRegBaseDisp.word64 x0, [x11 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#391:
    arm64.leaRdata x0, __str_rec_164  ; "panic at Process.maxon:195: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.leaRdata x0, __str_rec_165  ; "envEntryBytes: i < to <= bytes.count() but get("
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x6, 0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.movRegReg x1, x6
    arm64.bl __mm_alloc
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x6
    arm64.bl __uint_to_string
    arm64.movRegReg x7, x0
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.leaRdata x0, __str_rec_163  ; ") failed"
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.leaRdata x0, __str_rec_88  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x9, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x10, [x0 + 8]
    arm64.add x0, x3, x5
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x10
    arm64.add x11, x0, 57
    arm64.movImm x12, 0
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x11
    arm64.movRegReg x1, x12
    arm64.bl __mm_alloc
    arm64.movRegReg x11, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.add x12, x11, 56
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x12
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x12, x3
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x4
    arm64.movRegReg x2, x5
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x2, x5
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x6
    arm64.movRegReg x2, x7
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x2, x7
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x1, x2, x8
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x9
    arm64.movRegReg x2, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.storeBaseDispReg.word64 [x11 + 0], x12
    arm64.storeBaseDispReg.word64 [x11 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x11 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x11 + 24], x0
    arm64.storeSlotReg slot68, x11
    arm64.movRegReg x0, x6
    arm64.bl __mm_decref
    arm64.loadRegSlot x11, slot68
    arm64.loadRegBaseDisp.word64 x0, [x11 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#467:
    arm64.leaRdata x0, __str_blob_261  ; "panic at hash.maxon:24: hashString: byteAt OOB \xe2\x80\x94 i < s.byteLength() invariant\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#350:
    arm64.leaRdata x0, __str_blob_145  ; "panic at Map.maxon:209: Map.remove: keys.clearSlot OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#352:
    arm64.leaRdata x0, __str_blob_146  ; "panic at Map.maxon:210: Map.remove: values.clearSlot OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#354:
    arm64.leaRdata x0, __str_blob_147  ; "panic at Map.maxon:211: Map.remove: states.set OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __im_missing:
    arm64.leaRdata x0, __str_blob_391  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#357:
    arm64.leaRdata x0, __str_blob_293  ; "panic at Map.maxon:212: Range check failed: value outside typealias 'EntryCount'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#359:
    arm64.leaRdata x0, __str_blob_294  ; "panic at Map.maxon:213: Range check failed: value outside typealias 'TableCapacity'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#105:
    arm64.leaRdata x0, __str_blob_343  ; "panic at Subprocess.maxon:888: Range check failed: value outside typealias 'SpawnFlagsValue'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#111:
    arm64.leaRdata x0, __str_blob_224  ; "panic at Subprocess.maxon:915: Subprocess.spawnFailure: the runtime recorded a spawn step this library does not know\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#79:
    arm64.leaRdata x0, __str_blob_336  ; "panic at Subprocess.maxon:503: Range check failed: value outside typealias 'StreamingHandle'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#59:
    arm64.leaRdata x0, __str_rec_198  ; "panic at Subprocess.maxon:525: "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.leaRdata x0, __str_rec_199  ; "Subprocess.runConfiguration: runtime returned unexpected statusKind raw="
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x5, 0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.movRegReg x1, x5
    arm64.bl __mm_alloc
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x5
    arm64.bl __int_to_string
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.leaRdata x0, __str_rec_88  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x7, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.add x0, x2, x4
    arm64.add x0, x0, x6
    arm64.add x0, x0, x8
    arm64.add x9, x0, 57
    arm64.movImm x10, 0
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.movRegReg x0, x9
    arm64.movRegReg x1, x10
    arm64.bl __mm_alloc
    arm64.movRegReg x9, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.add x10, x9, 56
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.add x1, x10, x2
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x4
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.add x1, x1, x4
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x5
    arm64.movRegReg x2, x6
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.add x1, x1, x6
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x7
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.storeBaseDispReg.word64 [x9 + 0], x10
    arm64.storeBaseDispReg.word64 [x9 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x9 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x9 + 24], x0
    arm64.storeSlotReg slot66, x9
    arm64.movRegReg x0, x5
    arm64.bl __mm_decref
    arm64.loadRegSlot x9, slot66
    arm64.loadRegBaseDisp.word64 x0, [x9 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#81:
    arm64.leaRdata x0, __str_blob_337  ; "panic at Subprocess.maxon:526: Range check failed: value outside typealias 'ExitInt'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#84:
    arm64.leaRdata x0, __str_blob_338  ; "panic at Subprocess.maxon:527: Range check failed: value outside typealias 'Pid'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#2:
    arm64.leaRdata x0, __str_blob_89  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:11: /bin/sh runs on every POSIX lane\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#8:
    arm64.leaRdata x0, __str_blob_273  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:18: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
}

func @__np_pd_wait_any {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot7, x23
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movRegReg x19, x0
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.loadRegBaseDisp.word64 x2, [x1 + 824]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 824], x2
    arm64.movImm x1, 1
    arm64.arm64AtomicRmw.exchangeByte x1, [x0], x1
    arm64.cmp x1, 0
    arm64.b.ne __rl_slow#36
  __rl_cont#35:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 0]
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x21, 0
  npgalifehdr:
    arm64.cmp x21, x20
    arm64.b.lt npgalifebody
  npgalookpre:
    arm64.movImm x21, 0
  npgalookhdr:
    arm64.cmp x21, x20
    arm64.b.lt npgalookbody
  npgamarkpre:
    arm64.movImm x21, 0
  npgamarkhdr:
    arm64.cmp x21, x20
    arm64.b.lt npgamarkbody
  npgapark:
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.movImm x2, 0
    arm64.arm64AtomicRmw.exchangeByte x2, [x0], x2
    arm64.cmp x2, 1
    arm64.b.ne __rl_slow#42
  __rl_cont#41:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 824]
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x1 + 824], x0
    arm64.movImm x0, 10
    arm64.movRegReg x1, x19
    arm64.bl __gt_park
  npgaresumed:
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.loadRegBaseDisp.word64 x2, [x1 + 824]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 824], x2
    arm64.movImm x1, 1
    arm64.arm64AtomicRmw.exchangeByte x1, [x0], x1
    arm64.cmp x1, 0
    arm64.b.ne __rl_slow#44
  __rl_cont#43:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x21, 0
  npgaclearhdr:
    arm64.cmp x21, x20
    arm64.b.lt npgaclearbody
  npgadone:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.leaGlobal x1, __sched_lock
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.movImm x3, 0
    arm64.arm64AtomicRmw.exchangeByte x3, [x1], x3
    arm64.cmp x3, 1
    arm64.b.ne __rl_slow#46
  __rl_cont#45:
    arm64.loadRegBaseDisp.word64 x1, [x2 + 824]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x2 + 824], x1
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npgaclearbody:
    arm64.lsl x0, x21, 4
    arm64.add x0, x0, 32
    arm64.add x22, x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.and x23, x0, 1
    arm64.lsr x0, x0, 1
    arm64.bl __np_pd_slot
    arm64.cmp x0, 0
    arm64.cset x1, ne
    arm64.lsl x2, x23, 3
    arm64.add x2, x2, 8
    arm64.add x2, x0, x2
    arm64.cbz x1, npnopd#30
  npgaclearact:
    arm64.loadRegBaseDisp.word64 x1, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 48]
    arm64.cmp x0, x1
    arm64.b.ne npgaclearnext
  npgaclearlive:
    arm64.loadRegBaseDisp.word64 x0, [x2 + 0]
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x2 + 0], x1
    arm64.cmp x0, 3
    arm64.b.ne npholdnotwoken
  npgaclearfired:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 1
    arm64.lslv x1, x1, x21
    arm64.orr x0, x0, x1
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.b npgaclearnext
  npholdnotwoken:
    arm64.cmp x0, 2
    arm64.b.eq npgaclearnext
  npholdnotended: [async-unsafe]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x0, x1
    arm64.b.ne npholdlost
  npholdpublished:
    arm64.leaRdata x0, __abort_msg_109  ; "fatal error: runtime abort 109 (netpollUnhookMissed)\x0a"
    arm64.movImm x1, 53
    arm64.movImm x2, 109
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npholdlost:
    arm64.leaRdata x0, __abort_msg_115  ; "fatal error: runtime abort 115 (netpollHoldLost)\x0a"
    arm64.movImm x1, 49
    arm64.movImm x2, 115
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npgaclearnext:
    arm64.add x21, x21, 1
    arm64.b npgaclearhdr
  npnopd#30:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npgamarkbody:
    arm64.lsl x0, x21, 4
    arm64.add x0, x0, 32
    arm64.add x0, x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.and x22, x0, 1
    arm64.lsr x0, x0, 1
    arm64.bl __np_pd_slot
    arm64.cmp x0, 0
    arm64.cset x1, ne
    arm64.lsl x2, x22, 3
    arm64.add x2, x2, 8
    arm64.add x0, x0, x2
    arm64.cbz x1, npnopd#29
  npgamarkact:
    arm64.movImm x1, 2
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.add x21, x21, 1
    arm64.b npgamarkhdr
  npnopd#29:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npgalookbody:
    arm64.lsl x0, x21, 4
    arm64.add x0, x0, 32
    arm64.add x0, x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.and x22, x0, 1
    arm64.lsr x0, x0, 1
    arm64.bl __np_pd_slot
    arm64.cmp x0, 0
    arm64.cset x1, ne
    arm64.lsl x2, x22, 3
    arm64.add x2, x2, 8
    arm64.add x0, x0, x2
    arm64.cbz x1, npnopd#26
  npgalookact:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 1
    arm64.b.ne npwsnotready
  npgaready:
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.movImm x2, 0
    arm64.arm64AtomicRmw.exchangeByte x2, [x0], x2
    arm64.cmp x2, 1
    arm64.b.ne __rl_slow#40
  __rl_cont#39:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 824]
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x1 + 824], x0
    arm64.movImm x0, 1
    arm64.lslv x0, x0, x21
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npwsnotready:
    arm64.cmp x1, 0
    arm64.b.ne npwsdouble
  npgalooknext:
    arm64.add x21, x21, 1
    arm64.b npgalookhdr
  npwsdouble:
    arm64.leaRdata x0, __abort_msg_107  ; "fatal error: runtime abort 107 (netpollDoubleWait)\x0a"
    arm64.movImm x1, 51
    arm64.movImm x2, 107
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npnopd#26:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npgalifebody:
    arm64.lsl x0, x21, 4
    arm64.add x0, x0, 32
    arm64.add x22, x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.and x23, x0, 1
    arm64.lsr x0, x0, 1
    arm64.bl __np_pd_slot
    arm64.cmp x0, 0
    arm64.cset x1, ne
    arm64.lsl x2, x23, 3
    arm64.add x2, x2, 8
    arm64.add x2, x0, x2
    arm64.cbz x1, npnopd#25
  npgalifeact:
    arm64.loadRegBaseDisp.word64 x1, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 48]
    arm64.cmp x0, x1
    arm64.b.ne npgastale
  npgalifenext:
    arm64.add x21, x21, 1
    arm64.b npgalifehdr
  npgastale:
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.movImm x2, 0
    arm64.arm64AtomicRmw.exchangeByte x2, [x0], x2
    arm64.cmp x2, 1
    arm64.b.ne __rl_slow#38
  __rl_cont#37:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 824]
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x1 + 824], x0
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npnopd#25:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  __rl_slow#36:
    arm64.bl __rtlock_acquire
    arm64.b __rl_cont#35
  __rl_slow#42:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_release
    arm64.loadRegSlot x1, slot0
    arm64.b __rl_cont#41
  __rl_slow#44:
    arm64.bl __rtlock_acquire
    arm64.b __rl_cont#43
  __rl_slow#46:
    arm64.storeSlotReg slot1, x0
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x3
    arm64.bl __rtlock_release
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot1
    arm64.loadRegSlot x2, slot2
    arm64.b __rl_cont#45
  __rl_slow#40:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_release
    arm64.loadRegSlot x1, slot0
    arm64.b __rl_cont#39
  __rl_slow#38:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_release
    arm64.loadRegSlot x1, slot0
    arm64.b __rl_cont#37
}
```

```TargetIr:arm64-linux
data {
  __data_CharacterSet.cachedWhitespacesAndNewlines@0 = i64 0
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
  __gt_quiesce_waiter@104 = i64 0
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
  __mrt_hwcap_atomics@592 = i64 0
  __mrt_tls_next_key@600 = i64 0
  __io_pipe_seq@608 = i64 0
  __subp_table@616 = i64 0
  __subp_last_error@624 = i64 0
  __slab_arena_list@632 = i64 0
  __slab_arena_map_l1@640 = i64 0
  __slab_state@648 = i64 0
  __mrt_program_started@656 = i8 0
}

func @main {
  entry:
    arm64.prologue 704 record@48
    arm64.storeSlotReg slot79, x28
    arm64.storeSlotReg slot78, x27
    arm64.storeSlotReg slot77, x26
    arm64.storeSlotReg slot76, x25
    arm64.storeSlotReg slot75, x24
    arm64.storeSlotReg slot74, x23
    arm64.storeSlotReg slot73, x22
    arm64.storeSlotReg slot72, x21
    arm64.storeSlotReg slot71, x20
    arm64.storeSlotReg slot70, x19
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_80  ; "-c"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_81  ; "sleep 1; echo hi"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.bl Scheduler.counters
    arm64.storeSlotReg slot1, x0
    arm64.leaRdata x0, __str_rec_82  ; "/bin/sh"
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.orr x20, x0, 1
    arm64.storeSlotReg slot2, x20
  __il_body#17:
    arm64.leaRdata x21, __str_rec_117  ; ""
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.storeSlotReg slot3, x22
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
  __il_body#19:
    arm64.movRegReg x0, x20
    arm64.bl __uretain_Executable
    arm64.movRegReg x0, x22
    arm64.bl __mm_own
    arm64.movRegReg x21, x0
  __il_body#25:
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x23, 1
    arm64.movImm x24, 16777216
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x25 + 0], x0
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x24, 16777216
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x26 + 0], x0
    arm64.storeBaseDispReg.word64 [x26 + 8], x24
  __il_body#29:
    arm64.movImm x24, 0
    arm64.movImm x27, 0
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.storeBaseDispReg.byte [x28 + 0], x24
    arm64.storeBaseDispReg.byte [x28 + 8], x27
  __il_cont#28:
    arm64.movImm x0, 72
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.storeSlotReg slot8, x24
    arm64.movRegReg x0, x20
    arm64.bl __uretain_Executable
    arm64.storeBaseDispReg.word64 [x24 + 0], x20
    arm64.storeBaseDispReg.word64 [x24 + 8], x22
    arm64.movRegReg x0, x21
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movRegReg x0, x23
    arm64.bl __uretain_Environment
    arm64.storeBaseDispReg.word64 [x24 + 24], x23
    arm64.movImm x0, 1
    arm64.bl __uretain_InputSource
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 32], x0
    arm64.storeBaseDispReg.word64 [x24 + 40], x25
    arm64.storeBaseDispReg.word64 [x24 + 48], x26
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 56], x0
    arm64.storeBaseDispReg.word64 [x24 + 64], x28
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
  exitdrop#27:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Executable
  __il_cont#24:
    arm64.loadRegBaseDisp.word64 x20, [x24 + 8]
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.storeBaseDispReg.word64 [x24 + 8], x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 56], x0
  __il_body#35:
    arm64.loadRegBaseDisp.word64 x19, [x24 + 32]
    arm64.movRegReg x0, x19
    arm64.bl __uretain_InputSource
    arm64.movRegReg x20, x0
    arm64.loadRegBaseDisp.word64 x21, [x24 + 40]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.loadRegBaseDisp.word64 x23, [x24 + 48]
    arm64.movRegReg x0, x23
    arm64.bl __mm_retain
    arm64.movRegReg x25, x0
  __il_body#146:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x1, ge
    arm64.cmp x0, 3
    arm64.cset x2, le
    arm64.and x1, x1, x2
    arm64.cbnz x1, matcharm#151
  matchnext#153:
    arm64.cmp x0, 4
    arm64.b.ne matcharm#151
  matcharm#147:
    arm64.sub x19, x19, 4
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_211  ; "the standard input file path"
    arm64.bl requireNulFree
    arm64.movRegReg x26, x9
    arm64.cmp x26, 0
    arm64.b.eq tryok#148
  tryerr#149:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  exitterminal#150:
    arm64.movRegReg x0, x26
    arm64.b __il_cont#145
  tryok#148:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  matchcont#155:
    arm64.movImm x0, 0
    arm64.b __il_cont#145
  matcharm#151:
    arm64.movImm x0, 0
  __il_cont#145:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#140
  tryok#139:
    arm64.leaRdata x1, __str_rec_209  ; "the standard output file path"
    arm64.movRegReg x0, x21
    arm64.bl requireNulFreeOutputFile
    arm64.cmp x9, 0
    arm64.b.ne tryerr#142
  tryok#141:
    arm64.leaRdata x1, __str_rec_210  ; "the standard error file path"
    arm64.movRegReg x0, x23
    arm64.bl requireNulFreeOutputFile
    arm64.cmp x9, 0
    arm64.b.ne tryerr#144
  tryok#143:
    arm64.movImm x19, 0
    arm64.b __il_cont#137
  tryerr#140:
    arm64.movRegReg x19, x0
    arm64.b __il_cont#137
  tryerr#142:
    arm64.movRegReg x19, x9
    arm64.b __il_cont#137
  tryerr#144:
    arm64.movRegReg x19, x9
  __il_cont#137:
    arm64.cmp x19, 0
    arm64.b.eq tryok#36
  tryerr#37:
    arm64.movRegReg x0, x25
    arm64.bl __destruct_OutputDestination
  exitdrop#39:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_OutputDestination
  exitdrop#40:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_InputSource
    arm64.movRegReg x0, x19
    arm64.b exitterminal#38
  tryok#36:
    arm64.movRegReg x0, x25
    arm64.bl __destruct_OutputDestination
    arm64.movRegReg x0, x22
    arm64.bl __destruct_OutputDestination
    arm64.movRegReg x0, x20
    arm64.bl __destruct_InputSource
    arm64.loadRegBaseDisp.word64 x19, [x24 + 0]
    arm64.storeSlotReg slot33, x19
    arm64.movRegReg x0, x19
    arm64.bl __uretain_Executable
    arm64.storeSlotReg slot10, x0
    arm64.loadRegBaseDisp.word64 x20, [x24 + 8]
    arm64.storeSlotReg slot25, x20
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.storeSlotReg slot13, x0
    arm64.loadRegBaseDisp.word64 x21, [x24 + 16]
    arm64.storeSlotReg slot29, x21
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.storeSlotReg slot16, x0
    arm64.loadRegBaseDisp.word64 x0, [x24 + 24]
    arm64.storeSlotReg slot44, x0
    arm64.bl __uretain_Environment
    arm64.storeSlotReg slot19, x0
  __il_body#180:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x0, eq
    arm64.storeSlotReg slot38, x0
    arm64.b.ne matcharm#185
  matcharm#181:
    arm64.sub x19, x19, 1
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.leaRdata x1, __str_rec_202  ; "the program name"
    arm64.movRegReg x0, x19
    arm64.bl requireNulFree
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#182
  tryerr#183:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movRegReg x0, x22
    arm64.b __il_cont#179
  tryok#182:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.b matchcont#190
  matcharm#185:
    arm64.sub x19, x19, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_203  ; "the program path"
    arm64.bl requireNulFree
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#186
  tryerr#187:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
    arm64.b __il_cont#179
  tryok#186:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  matchcont#190:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.leaRdata x1, __str_rec_204  ; "the working directory"
    arm64.bl requireNulFree
    arm64.cmp x9, 0
    arm64.b.ne tryerr#192
  tryok#191:
    arm64.leaRdata x1, __layout_Array_String
    arm64.movRegReg x0, x20
    arm64.bl Array.withIterator
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr#193
    arm64.b forjoin
  __il_body#408:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot50, x20
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __adapt___witness_ArrayIterator_String.Iterator.current
    arm64.movRegReg x21, x0
  __il_cont#407:
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.storeSlotReg slot56, x0
    arm64.leaRdata x0, __str_rec_205  ; "argument "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x24, x0
  __il_body#409:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#411
  __il_cont#406:
    arm64.add x20, x0, 1
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x25
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.add x26, x23, x20
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x28, x23
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x25
    arm64.bl __mm_decref
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x27
    arm64.bl requireNulFree
    arm64.movRegReg x20, x9
    arm64.cmp x20, 0
    arm64.b.eq tryok#198
  tryerr#199:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#200:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
  exitdrop#201:
    arm64.loadRegSlot x0, slot56
    arm64.bl __str_decref
  exitdrop#202:
    arm64.loadRegSlot x0, slot50
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#203:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#179
  tryok#198:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
    arm64.loadRegSlot x0, slot56
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot50
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  __il_body#413:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#431:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#435
    arm64.b __im_move
  tryerr#433:
    arm64.movImm x0, 1
    arm64.b __il_cont#430
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#432:
    arm64.movImm x0, 0
  __il_cont#430:
    arm64.cmp x0, 0
    arm64.b.ne forhdr#193
  tryok#414:
    arm64.movImm x0, 0
  forhdr#193:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#408
  forexit#197:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
  forjoin:
    arm64.loadRegSlot x0, slot44
    arm64.and x0, x0, 7
    arm64.cmp x0, 1
    arm64.b.ne matchnext#213
  matcharm#205:
    arm64.movImm x0, 0
    arm64.b __il_cont#179
  matchnext#213:
    arm64.cmp x0, 2
    arm64.b.ne matcharm#209
  matcharm#206:
    arm64.loadRegSlot x0, slot44
    arm64.sub x19, x0, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x19
    arm64.bl requireNulFreeEnvironment
    arm64.movRegReg x20, x9
    arm64.cmp x20, 0
    arm64.b.eq tryok#207
  tryerr#208:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#179
  tryok#207:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.b matchcont#215
  matcharm#209:
    arm64.loadRegSlot x0, slot44
    arm64.sub x19, x0, 3
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x19
    arm64.bl requireNulFreeEnvironment
    arm64.movRegReg x20, x9
    arm64.cmp x20, 0
    arm64.b.eq tryok#210
  tryerr#211:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#179
  tryok#210:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
  matchcont#215:
    arm64.movImm x0, 0
    arm64.b __il_cont#179
  tryerr#192:
    arm64.movRegReg x0, x9
  __il_cont#179:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#230
  tryerr#171:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#168
  __il_body#230:
    arm64.loadRegSlot x0, slot38
    arm64.cbz x0, matcharm#235
  matcharm#231:
    arm64.loadRegSlot x0, slot33
    arm64.sub x19, x0, 1
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
  __il_body#417:
    arm64.movRegReg x0, x19
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#418
  tryerr#419:
    arm64.movImm x20, 0
    arm64.cbz x1, __il_body#438
  tryerr#242:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movImm x21, 0
    arm64.b __il_cont#239
  tryok#418:
    arm64.movRegReg x20, x0
  __il_body#438:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#445:
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
  __il_cont#444:
    arm64.movImm x22, 0
  whilehdr#439:
    arm64.cmp x22, x21
    arm64.movImm x0, 0
    arm64.b.hs scmerge#441
  andrhs#440:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#447:
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#449
  __il_cont#446:
    arm64.cmp x0, 47
    arm64.cset x0, eq
  scmerge#441:
    arm64.cbz x0, __il_cont#437
  leadingSeparators:
    arm64.add x22, x22, 1
    arm64.b whilehdr#439
  __il_cont#437:
    arm64.cmp x22, 0
    arm64.movImm x21, 1
    arm64.b.hi critsplit#485
  __il_body#265:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#423:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont#422:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#267
  empty:
    arm64.movImm x21, 0
    arm64.b __il_cont#264
  ifcont#267:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#425:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.b whilehdr#268
  scan:
    arm64.sub x21, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x0, x16
    arm64.and x1, x1, 1
    arm64.eor x0, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x0, x16
    arm64.and x0, x0, x21
    arm64.orr x0, x1, x0
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#274
  __rc_ok#273:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#427:
    arm64.movRegReg x1, x21
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#429
  __il_cont#426:
    arm64.cmp x0, 47
    arm64.b.ne ifcont#272
  isSep:
    arm64.add x21, x21, 1
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic#276
    arm64.b __il_cont#264
  ifcont#272:
    arm64.movRegReg x0, x21
  whilehdr#268:
    arm64.cmp x0, 0
    arm64.b.hi scan
  whileexit#270:
    arm64.movImm x21, 0
  __il_cont#264:
    arm64.cmp x21, 0
    arm64.b.ne ifcont#260
  noSep:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
    arm64.b __il_cont#257
  ifcont#260:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#421:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
  __il_cont#420:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl String.addressableBytes
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __managed_slice
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#261
  tryerr#262:
    arm64.leaRdata x0, __str_blob_126  ; "panic at FilePath.maxon:354: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryok#261:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movRegReg x0, x21
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitterminal#263:
    arm64.movRegReg x21, x22
  __il_cont#257:
    arm64.loadRegBaseDisp.word64 x1, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __str_eq
    arm64.eor x22, x0, 1
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.b scmerge#244
  critsplit#485:
    arm64.movRegReg x22, x21
  scmerge#244:
    arm64.cbz x22, ternaryfalse#246
  ternarytrue#245:
    arm64.loadRegSlot x1, slot29
    arm64.movRegReg x0, x20
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x21, x0
    arm64.b matchcont#247
  ternaryfalse#246:
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
  matchcont#247:
    arm64.leaRdata x23, __str_rec_119  ; "/"
    arm64.cbz x22, ifcont#250
  isAPath:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x21
    arm64.b exitterminal#249
  ifcont#250:
    arm64.movRegReg x0, x21
    arm64.bl String.cstr
    arm64.bl __gt_subp_resolve_path
    arm64.bl String.init
    arm64.movRegReg x22, x0
  __il_body#457:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 8]
  __il_body#453:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x22
    arm64.movRegReg x3, x23
    arm64.bl findIn
    arm64.cmp x9, 0
    arm64.b.eq tryok#454
  tryerr#455:
    arm64.movImm x0, 0
    arm64.b __il_cont#450
  tryok#454:
    arm64.movImm x0, 1
  __il_cont#450:
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#255
  notOnPath:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x23 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#253:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#254:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
  exitterminal#252:
    arm64.movImm x20, 0
    arm64.movRegReg x21, x20
    arm64.movRegReg x20, x23
    arm64.b __il_cont#239
  ifcont#255:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#256:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
  exitterminal#249:
    arm64.movImm x20, 0
    arm64.movRegReg x21, x0
  __il_cont#239:
    arm64.cmp x20, 0
    arm64.b.eq tryok#232
  tryerr#233:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
  exitterminal#234:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.b __il_cont#229
  tryok#232:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movRegReg x0, x21
    arm64.b matchcont#238
  matcharm#235:
    arm64.loadRegSlot x0, slot33
    arm64.sub x19, x0, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.loadRegSlot x1, slot29
    arm64.movRegReg x0, x19
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x20
  matchcont#238:
    arm64.movImm x1, 0
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x1
  __il_cont#229:
    arm64.storeSlotReg slot26, x19
    arm64.cmp x0, 0
    arm64.b.eq __il_body#217
  tryerr#173:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#168
  __il_body#217:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl appendNulTerminated
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.movImm x21, 0
    arm64.b forhdr#218
  arg:
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#226
  __im_load#227:
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x1 + x21*8 + 0]
    arm64.movImm x2, 0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
    arm64.cmp x0, 0
    arm64.cset x3, ne
    arm64.leaFuncAddr x4, __gt_promise_drop
    arm64.cmp x0, x4
    arm64.cset x0, ne
    arm64.and x0, x3, x0
    arm64.cbz x0, critsplit#477
  __im_empty:
    arm64.cmp x1, 0
    arm64.b.eq __im_slow#226
  critsplit#478:
    arm64.movRegReg x9, x2
    arm64.b __im_cont#225
  critsplit#477:
    arm64.movRegReg x9, x2
  __im_cont#225:
    arm64.cmp x9, 0
    arm64.b.ne tryerr#223
  tryok#222:
    arm64.movRegReg x0, x20
    arm64.bl appendNulTerminated
  forstep#220:
    arm64.add x21, x21, 1
  forhdr#218:
    arm64.cmp x21, x19
    arm64.b.lo arg
  forexit#221:
    arm64.movRegReg x0, x20
    arm64.bl String.from
    arm64.storeSlotReg slot30, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#216:
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.add x0, x0, 1
    arm64.storeSlotReg slot22, x0
  __il_body#278:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.loadRegSlot x0, slot44
    arm64.and x0, x0, 7
    arm64.cmp x0, 1
    arm64.b.ne matchnext#290
  __il_body#300:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x1, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movRegReg x0, x20
    arm64.bl String.from
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#299:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x21
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.b exitterminal#280
  matchnext#290:
    arm64.cmp x0, 2
    arm64.b.ne matcharm#286
  matcharm#281:
    arm64.loadRegSlot x0, slot44
    arm64.sub x20, x0, 2
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
  __il_body#303:
    arm64.movRegReg x0, x20
    arm64.bl namesByKey
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#305
  __il_body#324:
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x23, 0
  eachEntry:
    arm64.movRegReg x0, x23
    arm64.bl __env_entry
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x0, 0
    arm64.b.ne ifcont#329
  pastTheEnd:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  whileexit#327:
    arm64.movImm x0, 0
    arm64.b __il_cont#323
  ifcont#329:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x0, 1
    arm64.b.ne ifcont#337
  oneByte:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x24
    arm64.bl __managed_byte_at
    arm64.cmp x9, 0
    arm64.b.ne tryerr#332
  tryok#331:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#337
  hostFailed:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#335:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitterminal#334:
    arm64.movImm x0, 3
    arm64.movImm x22, 0
  __il_cont#323:
    arm64.cmp x0, 0
    arm64.b.eq tryok#306
  tryerr#307:
    arm64.leaRdata x22, __str_rec_212  ; "this process's environment could not be read, so no child can inherit it"
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x23 + 0], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x23 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Map_String_String
    arm64.b __il_cont#302
  tryok#306:
    arm64.movImm x0, 0
    arm64.b forhdr#309
  eachInherited:
    arm64.loadRegBaseDisp.word64 x1, [x22 + 0]
    arm64.loadRegBaseIndexScale.word64 x23, [x1 + x0*8 + 0]
  __im_cont#322:
    arm64.cmp x23, 0
    arm64.b.eq emptyslot
  slotfilled:
    arm64.movRegReg x0, x23
    arm64.bl __str_retain
    arm64.storeSlotReg slot40, x0
  __il_body#369:
    arm64.movRegReg x0, x23
    arm64.bl String.toByteArray
    arm64.movRegReg x24, x0
    arm64.movImm x25, 0
  __il_body#373:
    arm64.loadRegBaseDisp.word64 x26, [x24 + 8]
    arm64.movImm x1, 1
  __us_test:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.b forhdr#374
  eachByte#375:
    arm64.cmp x1, x26
    arm64.b.hs __im_slow#383
  __im_load#384:
    arm64.add x2, x0, x1
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  tryok#378:
    arm64.cmp x2, 61
    arm64.b.eq atSeparator
  forstep#376:
    arm64.add x1, x1, 1
  forhdr#374:
    arm64.cmp x1, x26
    arm64.b.lo eachByte#375
    arm64.b __il_body#385
  atSeparator:
    arm64.movRegReg x26, x1
  __il_body#385:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x27, x0
    arm64.movRegReg x1, x25
    arm64.b forhdr#386
  eachByte#387:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#394
  __im_load#395:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
  tryok#390:
    arm64.loadRegBaseDisp.word64 x0, [x27 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x27 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#397
  __im_bounds#398:
    arm64.loadRegBaseDisp.word64 x3, [x27 + 16]
    arm64.cmp x0, x3
    arm64.b.hs __im_slow#397
  __im_buffer#399:
    arm64.loadRegBaseDisp.word64 x3, [x27 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#397
  __im_viewed#400:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#397
  __im_store#401:
    arm64.add x3, x3, x0
    arm64.storeBaseDispReg.byte [x3 + 0], x2
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
  forstep#388:
    arm64.add x1, x1, 1
  forhdr#386:
    arm64.cmp x1, x26
    arm64.b.lo eachByte#387
  forexit#389:
    arm64.movRegReg x0, x27
    arm64.bl String.from
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x27
    arm64.bl __managed_decref
  __il_cont#371:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  __il_cont#368:
    arm64.movRegReg x0, x25
    arm64.bl Process.envNameKey
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.leaRdata x2, __witness_String.Hashable
    arm64.leaRdata x3, __witness_String.Equatable
    arm64.leaRdata x4, __layout_Map_String_String
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x24
    arm64.bl Map.get
    arm64.movRegReg x2, x0
    arm64.storeSlotReg slot46, x2
    arm64.cmp x9, 0
    arm64.b.ne ifelse
  overridden:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl appendEnvEntry
    arm64.leaRdata x23, __witness_String.Hashable
    arm64.leaRdata x25, __witness_String.Equatable
    arm64.leaRdata x0, __layout_Map_String_String
    arm64.storeSlotReg slot52, x0
  __il_body#461:
    arm64.movImm x26, 5381
  __il_body#469:
    arm64.loadRegBaseDisp.word64 x27, [x24 + 8]
  __il_cont#468:
    arm64.movImm x28, 0
    arm64.b forhdr#462
  hash_loop:
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x28
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#467
  tryok#466:
    arm64.movImm x16, 33
    arm64.mul x1, x26, x16
    arm64.add x0, x1, x0
  forstep#464:
    arm64.add x28, x28, 1
    arm64.movRegReg x26, x0
  forhdr#462:
    arm64.cmp x28, x27
    arm64.b.lo hash_loop
  forexit#465:
    arm64.movImm x0, 4294967295
    arm64.and x2, x26, x0
  __il_cont#458:
    arm64.loadRegSlot x5, slot52
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x24
    arm64.movRegReg x3, x23
    arm64.movRegReg x4, x25
    arm64.bl Map.findSlot
    arm64.movRegReg x26, x0
    arm64.cmp x9, 0
    arm64.b.ne __il_cont#344
  tryok#346:
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x23
    arm64.movRegReg x3, x25
    arm64.bl Map.slotHoldsKey
    arm64.cbz x0, __il_cont#344
  found:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.movRegReg x1, x26
    arm64.bl Array.clearSlot
    arm64.cmp x9, 0
    arm64.b.ne tryerr#350
  tryok#349:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.movRegReg x1, x26
    arm64.bl Array.clearSlot
    arm64.cmp x9, 0
    arm64.b.ne tryerr#352
  tryok#351:
    arm64.movImm x0, 2
    arm64.loadRegBaseDisp.word64 x1, [x21 + 16]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.cmp x26, x2
    arm64.b.hs __im_slow#361
  __im_buffer#362:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x3, [x1 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach
  __im_viewed#366:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach
  critsplit#480:
    arm64.movRegReg x1, x2
  __im_detached:
    arm64.add x1, x1, x26
    arm64.storeBaseDispReg.byte [x1 + 0], x0
  tryok#353:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 32]
    arm64.sub x0, x0, 1
    arm64.movImm x1, 9223372036854775808
    arm64.cmp x0, x1
    arm64.b.hi __rc_panic#357
  __rc_ok#356:
    arm64.storeBaseDispReg.word64 [x21 + 32], x0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 48]
    arm64.add x0, x0, 1
    arm64.movImm x1, 9223372036854775808
    arm64.cmp x0, x1
    arm64.b.hi __rc_panic#359
  __rc_ok#358:
    arm64.storeBaseDispReg.word64 [x21 + 48], x0
  __il_cont#344:
    arm64.loadRegSlot x0, slot46
    arm64.bl __str_decref
    arm64.b ifcont#317
  ifelse:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl appendNulTerminated
  ifcont#317:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot40
    arm64.bl __str_decref
  forstep#311:
    arm64.loadRegSlot x0, slot35
    arm64.add x0, x0, 1
  forhdr#309:
    arm64.storeSlotReg slot35, x0
    arm64.loadRegBaseDisp.word64 x1, [x22 + 8]
    arm64.cmp x0, x1
    arm64.b.lt eachInherited
  forexit#312:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl appendEnvEntries
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#319:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Map_String_String
  exitterminal#318:
    arm64.movImm x21, 0
    arm64.movRegReg x23, x21
    arm64.b __il_cont#302
  tryerr#305:
    arm64.movRegReg x23, x9
  __il_cont#302:
    arm64.cmp x23, 0
    arm64.b.eq tryok#282
  tryerr#283:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.b exitdrop#285
  tryok#282:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.b matchcont#292
  ifcont#337:
    arm64.movRegReg x0, x24
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x1, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#339
  __im_bounds#340:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#339
  __im_buffer#341:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#339
  __im_viewed#342:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#339
  __im_store#343:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
  __im_cont#338:
    arm64.add x23, x23, 1
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
    arm64.b eachEntry
  matcharm#286:
    arm64.loadRegSlot x0, slot44
    arm64.sub x20, x0, 3
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
    arm64.movRegReg x0, x20
    arm64.bl namesByKey
    arm64.movRegReg x21, x0
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#287
  tryerr#288:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x23, x22
  exitdrop#285:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal#284:
    arm64.movImm x19, 0
    arm64.b __il_cont#277
  tryok#287:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl appendEnvEntries
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
  matchcont#292:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#294
  __im_bounds#295:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#294
  __im_buffer#296:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#294
  __im_viewed#297:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#294
  __im_store#298:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
  __im_cont#293:
    arm64.movRegReg x0, x19
    arm64.bl String.from
    arm64.movImm x1, 0
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
  exitterminal#280:
    arm64.movImm x19, 0
    arm64.movRegReg x23, x19
    arm64.movRegReg x19, x0
  __il_cont#277:
    arm64.cmp x23, 0
    arm64.b.eq tryok#174
  tryerr#175:
    arm64.loadRegSlot x0, slot30
    arm64.bl __str_decref
  exitdrop#177:
    arm64.loadRegSlot x0, slot26
    arm64.bl __str_decref
  exitterminal#176:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x23
    arm64.b __il_cont#168
  tryok#174:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.loadRegSlot x0, slot26
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.loadRegSlot x0, slot30
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.loadRegSlot x0, slot22
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.storeBaseDispReg.word64 [x20 + 24], x19
    arm64.loadRegSlot x0, slot26
    arm64.bl __str_decref
  exitterminal#178:
    arm64.movImm x19, 0
    arm64.xchgRegReg x19, x20
  __il_cont#168:
    arm64.storeSlotReg slot12, x19
    arm64.cmp x20, 0
    arm64.b.eq tryok#41
  tryerr#42:
    arm64.loadRegSlot x0, slot19
    arm64.bl __destruct_Environment
  exitdrop#43:
    arm64.loadRegSlot x0, slot16
    arm64.bl __destruct_FilePath
  exitdrop#44:
    arm64.loadRegSlot x0, slot13
    arm64.bl __managed_decref
  exitdrop#45:
    arm64.loadRegSlot x0, slot10
    arm64.bl __destruct_Executable
    arm64.movRegReg x0, x20
    arm64.b exitterminal#38
  tryok#41:
    arm64.loadRegSlot x0, slot19
    arm64.bl __destruct_Environment
    arm64.loadRegSlot x0, slot16
    arm64.bl __destruct_FilePath
    arm64.loadRegSlot x0, slot13
    arm64.bl __managed_decref
    arm64.loadRegSlot x0, slot10
    arm64.bl __destruct_Executable
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x20, [x0 + 32]
    arm64.movRegReg x0, x20
    arm64.bl __uretain_InputSource
    arm64.movRegReg x21, x0
  __il_body#88:
    arm64.and x0, x20, 7
    arm64.sub x0, x0, 1
    arm64.jumpTable x0, default=matchdead [[0]=matcharm#89, [1]=matcharm#90, [2]=matcharm#91, [3]=matcharm#92, [4]=matcharm#93] @__jumptable_393
  matchdead:
  matcharm#89:
    arm64.movImm x20, 0
    arm64.leaRdata x0, __str_rec_117  ; ""
    arm64.movImm x22, 0
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x22
    arm64.bl StdioRuntimeTriple.create
    arm64.movRegReg x20, x0
    arm64.b matchcont#95
  matcharm#90:
    arm64.movImm x20, 1
    arm64.leaRdata x0, __str_rec_117  ; ""
    arm64.movImm x22, 0
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x22
    arm64.bl StdioRuntimeTriple.create
    arm64.movRegReg x20, x0
    arm64.b matchcont#95
  matcharm#91:
    arm64.movImm x20, 2
    arm64.leaRdata x0, __str_rec_117  ; ""
    arm64.movImm x22, 0
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x22
    arm64.bl StdioRuntimeTriple.create
    arm64.movRegReg x20, x0
    arm64.b matchcont#95
  matcharm#92:
    arm64.sub x20, x20, 4
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
    arm64.movImm x22, 3
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.movImm x23, 0
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x0, x25, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.add x27, x26, 56
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x25
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x23
    arm64.bl StdioRuntimeTriple.create
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x20, x22
    arm64.b matchcont#95
  matcharm#93:
    arm64.movImm x20, 4
    arm64.leaRdata x0, __str_rec_117  ; ""
    arm64.movImm x22, 0
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x22
    arm64.bl StdioRuntimeTriple.create
    arm64.movRegReg x20, x0
  matchcont#95:
    arm64.storeSlotReg slot5, x20
  __il_cont#87:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_InputSource
    arm64.loadRegSlot x21, slot8
    arm64.loadRegBaseDisp.word64 x22, [x21 + 40]
    arm64.movRegReg x0, x22
    arm64.bl __mm_retain
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl outputTripleFromDestination
    arm64.movRegReg x22, x0
    arm64.storeSlotReg slot7, x22
    arm64.movRegReg x0, x23
    arm64.bl __destruct_OutputDestination
    arm64.loadRegBaseDisp.word64 x23, [x21 + 48]
    arm64.movRegReg x0, x23
    arm64.bl __mm_retain
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.bl outputTripleFromDestination
    arm64.movRegReg x23, x0
    arm64.storeSlotReg slot37, x23
    arm64.movRegReg x0, x24
    arm64.bl __destruct_OutputDestination
    arm64.loadRegBaseDisp.word64 x0, [x21 + 64]
  __il_body#96:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
    arm64.cbz x2, ifcont#98
  __rc_ok#101:
    arm64.movImm x1, 1
  ifcont#98:
    arm64.loadRegBaseDisp.byte x0, [x0 + 8]
    arm64.cbz x0, critsplit#473
  newGroup:
    arm64.orr x21, x1, 2
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic#105
  __rc_chk#106:
    arm64.cmp x21, 15
    arm64.b.gt __rc_panic#105
    arm64.b __il_cont#86
  critsplit#473:
    arm64.movRegReg x21, x1
  __il_cont#86:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.addressableBytes
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x25, [x19 + 16]
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x26, [x0 + 16]
    arm64.movRegReg x0, x26
    arm64.bl __mm_retain
    arm64.storeSlotReg slot18, x0
  __il_body#107:
    arm64.loadRegBaseDisp.word64 x0, [x26 + 0]
    arm64.bl __str_retain
    arm64.storeSlotReg slot15, x0
  __il_cont#85:
    arm64.bl String.cstr
    arm64.movRegReg x26, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.movRegReg x0, x27
    arm64.bl __str_retain
    arm64.storeSlotReg slot21, x0
    arm64.movRegReg x0, x27
    arm64.bl String.addressableBytes
    arm64.movRegReg x27, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot51, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.storeSlotReg slot45, x0
    arm64.loadRegBaseDisp.word64 x19, [x20 + 8]
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.storeSlotReg slot24, x0
    arm64.movRegReg x0, x19
    arm64.bl String.cstr
    arm64.storeSlotReg slot39, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.storeSlotReg slot34, x0
    arm64.loadRegBaseDisp.word64 x19, [x22 + 8]
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.storeSlotReg slot28, x0
    arm64.movRegReg x0, x19
    arm64.bl String.cstr
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x20, [x22 + 16]
    arm64.loadRegBaseDisp.word64 x22, [x23 + 0]
    arm64.loadRegBaseDisp.word64 x28, [x23 + 8]
    arm64.movRegReg x0, x28
    arm64.bl __str_retain
    arm64.storeSlotReg slot32, x0
    arm64.movRegReg x0, x28
    arm64.bl String.cstr
    arm64.loadRegBaseDisp.word64 x1, [x23 + 16]
    arm64.storeOutgoingArg [sp + arg0], x19
    arm64.storeOutgoingArg [sp + arg1], x20
    arm64.storeOutgoingArg [sp + arg2], x22
    arm64.storeOutgoingArg [sp + arg3], x0
    arm64.storeOutgoingArg [sp + arg4], x1
    arm64.storeOutgoingArg [sp + arg5], x21
    arm64.loadRegSlot x7, slot34
    arm64.loadRegSlot x6, slot39
    arm64.loadRegSlot x5, slot45
    arm64.loadRegSlot x4, slot51
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.movRegReg x3, x27
    arm64.bl __gt_subp_attached_spawn
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot42, x19
    arm64.loadRegSlot x0, slot32
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot28
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot24
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot21
    arm64.bl __str_decref
    arm64.movRegReg x0, x27
    arm64.bl __managed_decref
    arm64.loadRegSlot x0, slot18
    arm64.bl __destruct_FilePath
    arm64.loadRegSlot x0, slot15
    arm64.bl __str_decref
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
    arm64.cmp x19, -1
    arm64.b.ne ifcont#51
  spawnFailed:
    arm64.loadRegSlot x0, slot12
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
  __il_body#109:
    arm64.bl __gt_subp_last_error_code
    arm64.movRegReg x20, x0
    arm64.asr x0, x20, 32
    arm64.bl __enum_from_raw_SpawnPreparation
    arm64.cmp x9, 0
    arm64.b.ne tryerr#111
  tryok#110:
    arm64.movImm x1, 4294967295
    arm64.and x1, x20, x1
    arm64.cmp x1, 2
    arm64.movImm x2, 1
    arm64.b.eq critsplit#474
  orrhs:
    arm64.cmp x1, 3
    arm64.cset x1, eq
    arm64.b scmerge#113
  critsplit#474:
    arm64.movRegReg x1, x2
  scmerge#113:
    arm64.movImm x2, 0
    arm64.cbz x1, critsplit#475
  andrhs#114:
    arm64.cmp x0, 0
    arm64.cset x0, eq
    arm64.b scmerge#115
  critsplit#475:
    arm64.movRegReg x0, x2
  scmerge#115:
    arm64.cbz x0, ifcont#117
  executableAbsent:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.b __il_cont#108
  ifcont#117:
    arm64.bl lastErrorMessage
    arm64.movRegReg x19, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
  __il_cont#108:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_StdioRuntimeTriple
    arm64.b exitdrop#48
  ifcont#51:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic#79
  __rc_ok#78:
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x20, [x0 + 32]
    arm64.movRegReg x0, x20
    arm64.bl __uretain_InputSource
    arm64.movRegReg x21, x0
  __il_body#157:
    arm64.and x0, x20, 7
    arm64.cmp x0, 1
    arm64.cset x1, ge
    arm64.cmp x0, 2
    arm64.cset x2, le
    arm64.and x1, x1, x2
    arm64.cbnz x1, __il_cont#156
  matchnext#161:
    arm64.cmp x0, 3
    arm64.b.ne __il_cont#156
  matcharm#158:
    arm64.sub x20, x20, 3
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
  __il_body#165:
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movRegReg x1, x0
  __il_body#405:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
  __il_cont#404:
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_queue_stdin
    arm64.cmp x0, 0
    arm64.b.eq __il_cont#164
  refused:
    arm64.leaRdata x0, __str_rec_221  ; "panic at Subprocess.maxon:865: "
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.leaRdata x0, __str_rec_222  ; "Subprocess: the runtime refused the stdin payload of handle "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_223  ; ", whose spawn has just answered it with a pipe"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_88  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot55, x0
    arm64.add x1, x21, x23
    arm64.add x1, x1, x19
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot43, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot49, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x28, x21
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x20, x20, x23
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x20, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.loadRegSlot x2, slot55
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x19, slot49
    arm64.storeBaseDispReg.word64 [x19 + 0], x28
    arm64.loadRegSlot x0, slot43
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __il_cont#164:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  __il_cont#156:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_InputSource
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_get_pid
    arm64.movRegReg x20, x0
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x1, [x0 + 56]
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_wait_collect
    arm64.movRegReg x19, x0
    arm64.cmp x19, 0
    arm64.b.ge ifcont#57
  waitFailed:
    arm64.cmp x19, -1
    arm64.b.ne ternaryfalse#54
  ternarytrue#53:
    arm64.bl lastErrorMessage
    arm64.movRegReg x19, x0
    arm64.b matchcont#55
  ternaryfalse#54:
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x19, [x0 + 56]
    arm64.bl lastErrorMessage
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot48, x20
  __il_body#119:
    arm64.leaRdata x0, __str_rec_225  ; "timed out after "
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_226  ; "ms, but the child's process tree could not be fully ended: "
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x26, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x20 + 8]
    arm64.add x0, x22, x19
    arm64.add x0, x0, x25
    arm64.add x0, x0, x20
    arm64.storeSlotReg slot54, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x21, x28, x22
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x21, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot54
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_cont#118:
    arm64.loadRegSlot x0, slot48
    arm64.bl __str_decref
    arm64.movRegReg x19, x27
  matchcont#55:
    arm64.loadRegSlot x0, slot42
    arm64.bl __gt_subp_release
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
  exitdrop#56:
    arm64.loadRegSlot x0, slot37
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#48:
    arm64.loadRegSlot x0, slot7
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#49:
    arm64.loadRegSlot x0, slot5
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#50:
    arm64.loadRegSlot x0, slot12
    arm64.bl __destruct_SpawnOperands
  exitterminal#47:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x20
    arm64.b __il_cont#34
  ifcont#57:
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_kind
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_code
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_stdout
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_stderr
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_duration
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x23
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x24
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x27, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_release
    arm64.loadRegSlot x0, slot42
    arm64.bl __gt_subp_release
    arm64.movRegReg x0, x21
    arm64.bl __enum_from_raw_ResultStatusKind
    arm64.cmp x9, 0
    arm64.b.ne tryerr#59
  tryok#58:
    arm64.cmp x22, 0
    arm64.b.lt __rc_panic#81
  __rc_chk#82:
    arm64.movImm x1, 4294967295
    arm64.cmp x22, x1
    arm64.b.gt __rc_panic#81
  __rc_ok#129:
    arm64.cmp x0, 0
    arm64.b.ne matchnext#126
  matcharm#122:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x22
    arm64.movImm x19, 0
    arm64.movRegReg x21, x19
    arm64.movRegReg x19, x0
    arm64.b __il_cont#120
  matchnext#126:
    arm64.cmp x0, 1
    arm64.b.ne matcharm#124
  matcharm#123:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x22
    arm64.movImm x19, 0
    arm64.movRegReg x21, x19
    arm64.movRegReg x19, x0
    arm64.b __il_cont#120
  matcharm#124:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.storeBaseDispReg.word64 [x19 + 8], x25
    arm64.movRegReg x0, x26
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movRegReg x0, x27
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x21, 0
    arm64.xchgRegReg x19, x21
  __il_cont#120:
    arm64.cmp x21, 0
    arm64.b.eq tryok#60
  tryerr#61:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#62:
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
  exitdrop#63:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#64:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitdrop#65:
    arm64.loadRegSlot x0, slot37
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#66:
    arm64.loadRegSlot x0, slot7
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#67:
    arm64.loadRegSlot x0, slot5
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#68:
    arm64.loadRegSlot x0, slot12
    arm64.bl __destruct_SpawnOperands
    arm64.movRegReg x0, x21
  exitterminal#38:
    arm64.movImm x1, 0
    arm64.xchgRegReg x0, x1
    arm64.b __il_cont#34
  tryok#60:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#84
  __rc_ok#83:
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x26
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x27
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
  __il_body#133:
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x28 + 0], x19
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x28 + 8], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x28 + 16], x0
    arm64.storeBaseDispReg.word64 [x28 + 24], x20
    arm64.storeBaseDispReg.word64 [x28 + 32], x25
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#135:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#136:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  __il_cont#132:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  exitdrop#70:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#71:
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
  exitdrop#72:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#73:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitdrop#74:
    arm64.loadRegSlot x0, slot37
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#75:
    arm64.loadRegSlot x0, slot7
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#76:
    arm64.loadRegSlot x0, slot5
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#77:
    arm64.loadRegSlot x0, slot12
    arm64.bl __destruct_SpawnOperands
  exitterminal#69:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x28
  __il_cont#34:
    arm64.cmp x1, 0
    arm64.b.eq tryok#32
  tryerr#33:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x1
    arm64.b __il_cont#30
  tryok#32:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x0
  __il_cont#30:
    arm64.cmp x20, 0
    arm64.b.eq tryok#20
  tryerr#21:
    arm64.loadRegSlot x0, slot8
    arm64.bl __destruct_Configuration
  exitterminal#22:
    arm64.movImm x19, 0
    arm64.cmp x20, 0
    arm64.b.ne tryerr#2
    arm64.b __il_cont#18
  tryok#20:
    arm64.loadRegSlot x0, slot8
    arm64.bl __destruct_Configuration
  __il_cont#18:
    arm64.storeSlotReg slot4, x19
  tryok#1:
    arm64.loadRegSlot x0, slot3
    arm64.bl __destruct_FilePath
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_Executable
    arm64.bl Scheduler.counters
    arm64.storeSlotReg slot6, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 64]
    arm64.loadRegSlot x2, slot1
    arm64.loadRegBaseDisp.word64 x3, [x2 + 64]
    arm64.sub x20, x1, x3
    arm64.loadRegBaseDisp.word64 x0, [x0 + 72]
    arm64.loadRegBaseDisp.word64 x1, [x2 + 72]
    arm64.sub x21, x0, x1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
  __il_body#403:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.storeSlotReg slot9, x22
  __il_cont#402:
    arm64.leaRdata x1, __str_rec_83  ; "hi"
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.startsWith
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_84  ; "timerpolls="
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.cmp x20, 16
    arm64.cset x20, ls
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot53, x1
    arm64.movRegReg x0, x20
    arm64.bl __bool_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_85  ; " onpoller="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot47, x1
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.cmp x21, 1
    arm64.cset x21, hs
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot41, x1
    arm64.movRegReg x0, x21
    arm64.bl __bool_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_86  ; " outLen="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot36, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot31, x1
    arm64.movRegReg x0, x22
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_87  ; " matches="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot27, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __bool_to_string
    arm64.storeSlotReg slot23, x0
    arm64.leaRdata x1, __str_rec_88  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot20, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot17, x1
    arm64.add x2, x24, x20
    arm64.add x2, x2, x25
    arm64.add x2, x2, x21
    arm64.add x2, x2, x26
    arm64.add x2, x2, x22
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot11, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot14, x0
    arm64.add x19, x0, 56
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x23, x19, x24
    arm64.loadRegSlot x1, slot53
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x23, x20
    arm64.loadRegSlot x1, slot47
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x20, x20, x25
    arm64.loadRegSlot x1, slot41
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x20, x21
    arm64.loadRegSlot x1, slot36
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x20, x20, x26
    arm64.loadRegSlot x1, slot31
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x20, x20, x22
    arm64.loadRegSlot x1, slot27
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x20, x20, x27
    arm64.loadRegSlot x2, slot23
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot23
    arm64.add x0, x20, x0
    arm64.loadRegSlot x2, slot17
    arm64.loadRegSlot x1, slot20
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot14
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.loadRegSlot x1, slot11
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.loadRegSlot x0, slot53
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot41
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot31
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
  __il_body#13:
    arm64.loadRegSlot x0, slot14
    arm64.bl __write_stdout
  __il_cont#12:
    arm64.loadRegSlot x0, slot14
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot9
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#8
  __rc_chk#9:
    arm64.loadRegSlot x0, slot9
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#8
  __rc_ok#7:
    arm64.loadRegSlot x0, slot6
    arm64.bl __mm_decref
  exitdrop#4:
    arm64.loadRegSlot x0, slot4
    arm64.bl __destruct_CollectedOutput
  exitdrop#5:
    arm64.loadRegSlot x0, slot1
    arm64.bl __mm_decref
  exitdrop#6:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal#3:
    arm64.loadRegSlot x0, slot9
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __im_slow#435:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#433
    arm64.b tryok#432
  __im_slow#226:
    arm64.loadRegSlot x0, slot25
    arm64.movRegReg x1, x21
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.b __im_cont#225
  __im_slow#383:
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.cmp x9, 0
    arm64.b.ne tryerr#379
    arm64.b tryok#378
  __im_slow#394:
    arm64.storeSlotReg slot58, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.loadRegSlot x1, slot58
    arm64.cmp x9, 0
    arm64.b.ne tryerr#391
  critsplit#482:
    arm64.movRegReg x2, x0
    arm64.b tryok#390
  __im_slow#397:
    arm64.storeSlotReg slot58, x1
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x2
    arm64.bl __managed_push
    arm64.loadRegSlot x1, slot58
    arm64.b forstep#388
  __im_slow#361:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x26
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#354
    arm64.b tryok#353
  __im_detach:
    arm64.storeSlotReg slot58, x1
    arm64.movRegReg x0, x1
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 2
    arm64.loadRegSlot x1, slot58
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.b __im_detached
  __im_slow#339:
    arm64.movRegReg x0, x22
    arm64.bl __managed_push
    arm64.b __im_cont#338
  __im_slow#294:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.b __im_cont#293
  __rc_panic#411:
    arm64.leaRdata x0, __str_blob_276  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#449:
    arm64.leaRdata x0, __str_blob_187  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#274:
    arm64.leaRdata x0, __str_blob_282  ; "panic at FilePath.maxon:334: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#429:
    arm64.leaRdata x0, __str_blob_187  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#276:
    arm64.leaRdata x0, __str_blob_283  ; "panic at FilePath.maxon:337: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#223:
    arm64.leaRdata x0, __str_blob_200  ; "panic at Subprocess.maxon:649: buildArgvBlob: args.get(i) out of bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#332:
    arm64.leaRdata x0, __str_blob_160  ; "panic at Process.maxon:117: Process.currentEnvironmentEntries: byteAt(0) of a one-byte answer\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  emptyslot:
    arm64.leaRdata x0, __str_rec_213  ; "panic at Subprocess.maxon:776: "
  __rc_ok#320:
    arm64.loadRegSlot x1, slot35
    arm64.bl __emptySlotMessage
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#379:
    arm64.leaRdata x0, __str_rec_161  ; "panic at Process.maxon:181: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.leaRdata x0, __str_rec_162  ; "envEntrySeparatorAt: i < n = bytes.count() but get("
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x6, 0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.movRegReg x1, x6
    arm64.bl __mm_alloc
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x6
    arm64.bl __uint_to_string
    arm64.movRegReg x7, x0
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.leaRdata x0, __str_rec_163  ; ") failed"
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.leaRdata x0, __str_rec_88  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x9, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x10, [x0 + 8]
    arm64.add x0, x3, x5
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x10
    arm64.add x11, x0, 57
    arm64.movImm x12, 0
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x11
    arm64.movRegReg x1, x12
    arm64.bl __mm_alloc
    arm64.movRegReg x11, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.add x12, x11, 56
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x12
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x12, x3
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x4
    arm64.movRegReg x2, x5
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x2, x5
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x6
    arm64.movRegReg x2, x7
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x2, x7
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x1, x2, x8
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x9
    arm64.movRegReg x2, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.storeBaseDispReg.word64 [x11 + 0], x12
    arm64.storeBaseDispReg.word64 [x11 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x11 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x11 + 24], x0
    arm64.storeSlotReg slot68, x11
    arm64.movRegReg x0, x6
    arm64.bl __mm_decref
    arm64.loadRegSlot x11, slot68
    arm64.loadRegBaseDisp.word64 x0, [x11 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#391:
    arm64.leaRdata x0, __str_rec_164  ; "panic at Process.maxon:195: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.leaRdata x0, __str_rec_165  ; "envEntryBytes: i < to <= bytes.count() but get("
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x6, 0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.movRegReg x1, x6
    arm64.bl __mm_alloc
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x6
    arm64.bl __uint_to_string
    arm64.movRegReg x7, x0
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.leaRdata x0, __str_rec_163  ; ") failed"
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.leaRdata x0, __str_rec_88  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x9, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x10, [x0 + 8]
    arm64.add x0, x3, x5
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x10
    arm64.add x11, x0, 57
    arm64.movImm x12, 0
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x11
    arm64.movRegReg x1, x12
    arm64.bl __mm_alloc
    arm64.movRegReg x11, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.add x12, x11, 56
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x12
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x12, x3
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x4
    arm64.movRegReg x2, x5
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x2, x5
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x6
    arm64.movRegReg x2, x7
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x2, x2, x7
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.add x1, x2, x8
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot68, x11
    arm64.storeSlotReg slot69, x12
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x9
    arm64.movRegReg x2, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x11, slot68
    arm64.loadRegSlot x12, slot69
    arm64.storeBaseDispReg.word64 [x11 + 0], x12
    arm64.storeBaseDispReg.word64 [x11 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x11 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x11 + 24], x0
    arm64.storeSlotReg slot68, x11
    arm64.movRegReg x0, x6
    arm64.bl __mm_decref
    arm64.loadRegSlot x11, slot68
    arm64.loadRegBaseDisp.word64 x0, [x11 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#467:
    arm64.leaRdata x0, __str_blob_261  ; "panic at hash.maxon:24: hashString: byteAt OOB \xe2\x80\x94 i < s.byteLength() invariant\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#350:
    arm64.leaRdata x0, __str_blob_145  ; "panic at Map.maxon:209: Map.remove: keys.clearSlot OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#352:
    arm64.leaRdata x0, __str_blob_146  ; "panic at Map.maxon:210: Map.remove: values.clearSlot OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#354:
    arm64.leaRdata x0, __str_blob_147  ; "panic at Map.maxon:211: Map.remove: states.set OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __im_missing:
    arm64.leaRdata x0, __str_blob_391  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#357:
    arm64.leaRdata x0, __str_blob_293  ; "panic at Map.maxon:212: Range check failed: value outside typealias 'EntryCount'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#359:
    arm64.leaRdata x0, __str_blob_294  ; "panic at Map.maxon:213: Range check failed: value outside typealias 'TableCapacity'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#105:
    arm64.leaRdata x0, __str_blob_343  ; "panic at Subprocess.maxon:888: Range check failed: value outside typealias 'SpawnFlagsValue'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#111:
    arm64.leaRdata x0, __str_blob_224  ; "panic at Subprocess.maxon:915: Subprocess.spawnFailure: the runtime recorded a spawn step this library does not know\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#79:
    arm64.leaRdata x0, __str_blob_336  ; "panic at Subprocess.maxon:503: Range check failed: value outside typealias 'StreamingHandle'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#59:
    arm64.leaRdata x0, __str_rec_198  ; "panic at Subprocess.maxon:525: "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.leaRdata x0, __str_rec_199  ; "Subprocess.runConfiguration: runtime returned unexpected statusKind raw="
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x5, 0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.movRegReg x1, x5
    arm64.bl __mm_alloc
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x5
    arm64.bl __int_to_string
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.leaRdata x0, __str_rec_88  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x7, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.add x0, x2, x4
    arm64.add x0, x0, x6
    arm64.add x0, x0, x8
    arm64.add x9, x0, 57
    arm64.movImm x10, 0
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.movRegReg x0, x9
    arm64.movRegReg x1, x10
    arm64.bl __mm_alloc
    arm64.movRegReg x9, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.add x10, x9, 56
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot59, x2
    arm64.storeSlotReg slot60, x3
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x2, slot59
    arm64.loadRegSlot x3, slot60
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.add x1, x10, x2
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot61, x4
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x4
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x4, slot61
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.add x1, x1, x4
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot58, x1
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot63, x6
    arm64.storeSlotReg slot64, x7
    arm64.storeSlotReg slot65, x8
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x5
    arm64.movRegReg x2, x6
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x1, slot58
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x6, slot63
    arm64.loadRegSlot x7, slot64
    arm64.loadRegSlot x8, slot65
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.add x1, x1, x6
    arm64.storeSlotReg slot57, x0
    arm64.storeSlotReg slot62, x5
    arm64.storeSlotReg slot66, x9
    arm64.storeSlotReg slot67, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x7
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot57
    arm64.loadRegSlot x5, slot62
    arm64.loadRegSlot x9, slot66
    arm64.loadRegSlot x10, slot67
    arm64.storeBaseDispReg.word64 [x9 + 0], x10
    arm64.storeBaseDispReg.word64 [x9 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x9 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x9 + 24], x0
    arm64.storeSlotReg slot66, x9
    arm64.movRegReg x0, x5
    arm64.bl __mm_decref
    arm64.loadRegSlot x9, slot66
    arm64.loadRegBaseDisp.word64 x0, [x9 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#81:
    arm64.leaRdata x0, __str_blob_337  ; "panic at Subprocess.maxon:526: Range check failed: value outside typealias 'ExitInt'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#84:
    arm64.leaRdata x0, __str_blob_338  ; "panic at Subprocess.maxon:527: Range check failed: value outside typealias 'Pid'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  tryerr#2:
    arm64.leaRdata x0, __str_blob_89  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:11: /bin/sh runs on every POSIX lane\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
  __rc_panic#8:
    arm64.leaRdata x0, __str_blob_273  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:18: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot70
    arm64.loadRegSlot x20, slot71
    arm64.loadRegSlot x21, slot72
    arm64.loadRegSlot x22, slot73
    arm64.loadRegSlot x23, slot74
    arm64.loadRegSlot x24, slot75
    arm64.loadRegSlot x25, slot76
    arm64.loadRegSlot x26, slot77
    arm64.loadRegSlot x27, slot78
    arm64.loadRegSlot x28, slot79
    arm64.epilogue 704 record@48
    arm64.retIndirect
}

func @__np_pd_wait_any {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot7, x23
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movRegReg x19, x0
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.loadRegBaseDisp.word64 x2, [x1 + 824]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 824], x2
    arm64.movImm x1, 1
    arm64.arm64AtomicRmw.exchangeByte x1, [x0], x1
    arm64.cmp x1, 0
    arm64.b.ne __rl_slow#36
  __rl_cont#35:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 0]
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x21, 0
  npgalifehdr:
    arm64.cmp x21, x20
    arm64.b.lt npgalifebody
  npgalookpre:
    arm64.movImm x21, 0
  npgalookhdr:
    arm64.cmp x21, x20
    arm64.b.lt npgalookbody
  npgamarkpre:
    arm64.movImm x21, 0
  npgamarkhdr:
    arm64.cmp x21, x20
    arm64.b.lt npgamarkbody
  npgapark:
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.movImm x2, 0
    arm64.arm64AtomicRmw.exchangeByte x2, [x0], x2
    arm64.cmp x2, 1
    arm64.b.ne __rl_slow#42
  __rl_cont#41:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 824]
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x1 + 824], x0
    arm64.movImm x0, 10
    arm64.movRegReg x1, x19
    arm64.bl __gt_park
  npgaresumed:
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.loadRegBaseDisp.word64 x2, [x1 + 824]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 824], x2
    arm64.movImm x1, 1
    arm64.arm64AtomicRmw.exchangeByte x1, [x0], x1
    arm64.cmp x1, 0
    arm64.b.ne __rl_slow#44
  __rl_cont#43:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x21, 0
  npgaclearhdr:
    arm64.cmp x21, x20
    arm64.b.lt npgaclearbody
  npgadone:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.leaGlobal x1, __sched_lock
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.movImm x3, 0
    arm64.arm64AtomicRmw.exchangeByte x3, [x1], x3
    arm64.cmp x3, 1
    arm64.b.ne __rl_slow#46
  __rl_cont#45:
    arm64.loadRegBaseDisp.word64 x1, [x2 + 824]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x2 + 824], x1
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npgaclearbody:
    arm64.lsl x0, x21, 4
    arm64.add x0, x0, 32
    arm64.add x22, x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.and x23, x0, 1
    arm64.lsr x0, x0, 1
    arm64.bl __np_pd_slot
    arm64.cmp x0, 0
    arm64.cset x1, ne
    arm64.lsl x2, x23, 3
    arm64.add x2, x2, 8
    arm64.add x2, x0, x2
    arm64.cbz x1, npnopd#30
  npgaclearact:
    arm64.loadRegBaseDisp.word64 x1, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 48]
    arm64.cmp x0, x1
    arm64.b.ne npgaclearnext
  npgaclearlive:
    arm64.loadRegBaseDisp.word64 x0, [x2 + 0]
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x2 + 0], x1
    arm64.cmp x0, 3
    arm64.b.ne npholdnotwoken
  npgaclearfired:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 1
    arm64.lslv x1, x1, x21
    arm64.orr x0, x0, x1
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.b npgaclearnext
  npholdnotwoken:
    arm64.cmp x0, 2
    arm64.b.eq npgaclearnext
  npholdnotended: [async-unsafe]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x0, x1
    arm64.b.ne npholdlost
  npholdpublished:
    arm64.leaRdata x0, __abort_msg_109  ; "fatal error: runtime abort 109 (netpollUnhookMissed)\x0a"
    arm64.movImm x1, 53
    arm64.movImm x2, 109
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npholdlost:
    arm64.leaRdata x0, __abort_msg_115  ; "fatal error: runtime abort 115 (netpollHoldLost)\x0a"
    arm64.movImm x1, 49
    arm64.movImm x2, 115
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npgaclearnext:
    arm64.add x21, x21, 1
    arm64.b npgaclearhdr
  npnopd#30:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npgamarkbody:
    arm64.lsl x0, x21, 4
    arm64.add x0, x0, 32
    arm64.add x0, x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.and x22, x0, 1
    arm64.lsr x0, x0, 1
    arm64.bl __np_pd_slot
    arm64.cmp x0, 0
    arm64.cset x1, ne
    arm64.lsl x2, x22, 3
    arm64.add x2, x2, 8
    arm64.add x0, x0, x2
    arm64.cbz x1, npnopd#29
  npgamarkact:
    arm64.movImm x1, 2
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.add x21, x21, 1
    arm64.b npgamarkhdr
  npnopd#29:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npgalookbody:
    arm64.lsl x0, x21, 4
    arm64.add x0, x0, 32
    arm64.add x0, x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.and x22, x0, 1
    arm64.lsr x0, x0, 1
    arm64.bl __np_pd_slot
    arm64.cmp x0, 0
    arm64.cset x1, ne
    arm64.lsl x2, x22, 3
    arm64.add x2, x2, 8
    arm64.add x0, x0, x2
    arm64.cbz x1, npnopd#26
  npgalookact:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 1
    arm64.b.ne npwsnotready
  npgaready:
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.movImm x2, 0
    arm64.arm64AtomicRmw.exchangeByte x2, [x0], x2
    arm64.cmp x2, 1
    arm64.b.ne __rl_slow#40
  __rl_cont#39:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 824]
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x1 + 824], x0
    arm64.movImm x0, 1
    arm64.lslv x0, x0, x21
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npwsnotready:
    arm64.cmp x1, 0
    arm64.b.ne npwsdouble
  npgalooknext:
    arm64.add x21, x21, 1
    arm64.b npgalookhdr
  npwsdouble:
    arm64.leaRdata x0, __abort_msg_107  ; "fatal error: runtime abort 107 (netpollDoubleWait)\x0a"
    arm64.movImm x1, 51
    arm64.movImm x2, 107
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npnopd#26:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npgalifebody:
    arm64.lsl x0, x21, 4
    arm64.add x0, x0, 32
    arm64.add x22, x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.and x23, x0, 1
    arm64.lsr x0, x0, 1
    arm64.bl __np_pd_slot
    arm64.cmp x0, 0
    arm64.cset x1, ne
    arm64.lsl x2, x23, 3
    arm64.add x2, x2, 8
    arm64.add x2, x0, x2
    arm64.cbz x1, npnopd#25
  npgalifeact:
    arm64.loadRegBaseDisp.word64 x1, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 48]
    arm64.cmp x0, x1
    arm64.b.ne npgastale
  npgalifenext:
    arm64.add x21, x21, 1
    arm64.b npgalifehdr
  npgastale:
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.movImm x2, 0
    arm64.arm64AtomicRmw.exchangeByte x2, [x0], x2
    arm64.cmp x2, 1
    arm64.b.ne __rl_slow#38
  __rl_cont#37:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 824]
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x1 + 824], x0
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  npnopd#25:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.retIndirect
  __rl_slow#36:
    arm64.bl __rtlock_acquire
    arm64.b __rl_cont#35
  __rl_slow#42:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_release
    arm64.loadRegSlot x1, slot0
    arm64.b __rl_cont#41
  __rl_slow#44:
    arm64.bl __rtlock_acquire
    arm64.b __rl_cont#43
  __rl_slow#46:
    arm64.storeSlotReg slot1, x0
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x3
    arm64.bl __rtlock_release
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot1
    arm64.loadRegSlot x2, slot2
    arm64.b __rl_cont#45
  __rl_slow#40:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_release
    arm64.loadRegSlot x1, slot0
    arm64.b __rl_cont#39
  __rl_slow#38:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_release
    arm64.loadRegSlot x1, slot0
    arm64.b __rl_cont#37
}
```
