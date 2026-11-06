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
    x64.leaRegRdata rcx, [rip + __str_rec_78]  ; "-c"
    x64.callDirect __str_retain
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.leaRegRdata rcx, [rip + __str_rec_79]  ; "sleep 1; echo hi"
    x64.callDirect __str_retain
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.callDirect Scheduler.counters
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.leaRegRdata rcx, [rip + __str_rec_80]  ; "/bin/sh"
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.movRegReg r13, r8
    x64.orRegImm32 r13, r8, 1
  __il_body#17:
    x64.leaRegRdata r14, [rip + __str_rec_115]  ; ""
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
    x64.leaRegRdata rdx, [rip + __str_rec_81]  ; "hi"
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.callDirect String.startsWith
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_82]  ; "timerpolls="
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
    x64.leaRegRdata rax, [rip + __str_rec_83]  ; " onpoller="
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
    x64.leaRegRdata rax, [rip + __str_rec_84]  ; " outLen="
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
    x64.leaRegRdata rax, [rip + __str_rec_85]  ; " matches="
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
    x64.leaRegRdata rax, [rip + __str_rec_86]  ; "\x0a"
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
    x64.leaRegRdata rcx, [rip + __str_blob_87]  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:11: /bin/sh runs on every POSIX lane\x0a"
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
    x64.leaRegRdata rcx, [rip + __str_blob_269]  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:18: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  npholdnotended:
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
    arm64.prologue 688 record@48
    arm64.storeSlotReg slot77, x28
    arm64.storeSlotReg slot76, x27
    arm64.storeSlotReg slot75, x26
    arm64.storeSlotReg slot74, x25
    arm64.storeSlotReg slot73, x24
    arm64.storeSlotReg slot72, x23
    arm64.storeSlotReg slot71, x22
    arm64.storeSlotReg slot70, x21
    arm64.storeSlotReg slot69, x20
    arm64.storeSlotReg slot68, x19
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_78  ; "-c"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_79  ; "sleep 1; echo hi"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.bl Scheduler.counters
    arm64.storeSlotReg slot1, x0
    arm64.leaRdata x0, __str_rec_80  ; "/bin/sh"
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.orr x20, x0, 1
    arm64.storeSlotReg slot2, x20
  __il_body#17:
    arm64.leaRdata x21, __str_rec_115  ; ""
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
  __il_body#141:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x1, ge
    arm64.cmp x0, 3
    arm64.cset x2, le
    arm64.and x1, x1, x2
    arm64.cbnz x1, matcharm#146
  matchnext#148:
    arm64.cmp x0, 4
    arm64.b.ne matcharm#146
  matcharm#142:
    arm64.sub x19, x19, 4
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_209  ; "the standard input file path"
    arm64.bl requireNulFree
    arm64.movRegReg x26, x9
    arm64.cmp x26, 0
    arm64.b.eq tryok#143
  tryerr#144:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  exitterminal#145:
    arm64.movRegReg x0, x26
    arm64.b __il_cont#140
  tryok#143:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  matchcont#150:
    arm64.movImm x0, 0
    arm64.b __il_cont#140
  matcharm#146:
    arm64.movImm x0, 0
  __il_cont#140:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#135
  tryok#134:
    arm64.leaRdata x1, __str_rec_207  ; "the standard output file path"
    arm64.movRegReg x0, x21
    arm64.bl requireNulFreeOutputFile
    arm64.cmp x9, 0
    arm64.b.ne tryerr#137
  tryok#136:
    arm64.leaRdata x1, __str_rec_208  ; "the standard error file path"
    arm64.movRegReg x0, x23
    arm64.bl requireNulFreeOutputFile
    arm64.cmp x9, 0
    arm64.b.ne tryerr#139
  tryok#138:
    arm64.movImm x19, 0
    arm64.b __il_cont#132
  tryerr#135:
    arm64.movRegReg x19, x0
    arm64.b __il_cont#132
  tryerr#137:
    arm64.movRegReg x19, x9
    arm64.b __il_cont#132
  tryerr#139:
    arm64.movRegReg x19, x9
  __il_cont#132:
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
    arm64.storeSlotReg slot42, x0
    arm64.bl __uretain_Environment
    arm64.storeSlotReg slot19, x0
  __il_body#175:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x0, eq
    arm64.storeSlotReg slot37, x0
    arm64.b.ne matcharm#180
  matcharm#176:
    arm64.sub x19, x19, 1
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.leaRdata x1, __str_rec_200  ; "the program name"
    arm64.movRegReg x0, x19
    arm64.bl requireNulFree
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#177
  tryerr#178:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movRegReg x0, x22
    arm64.b __il_cont#174
  tryok#177:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.b matchcont#185
  matcharm#180:
    arm64.sub x19, x19, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_201  ; "the program path"
    arm64.bl requireNulFree
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#181
  tryerr#182:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
    arm64.b __il_cont#174
  tryok#181:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  matchcont#185:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.leaRdata x1, __str_rec_202  ; "the working directory"
    arm64.bl requireNulFree
    arm64.cmp x9, 0
    arm64.b.ne tryerr#187
  tryok#186:
    arm64.leaRdata x1, __layout_Array_String
    arm64.movRegReg x0, x20
    arm64.bl Array.withIterator
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr#188
    arm64.b forjoin
  __il_body#403:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot47, x20
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __adapt___witness_ArrayIterator_String.Iterator.current
    arm64.movRegReg x21, x0
  __il_cont#402:
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.storeSlotReg slot54, x0
    arm64.leaRdata x0, __str_rec_203  ; "argument "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x24, x0
  __il_body#404:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#406
  __il_cont#401:
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
    arm64.b.eq tryok#193
  tryerr#194:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#195:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
  exitdrop#196:
    arm64.loadRegSlot x0, slot54
    arm64.bl __str_decref
  exitdrop#197:
    arm64.loadRegSlot x0, slot47
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#198:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#174
  tryok#193:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
    arm64.loadRegSlot x0, slot54
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot47
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  __il_body#408:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#426:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#430
    arm64.b __im_move
  tryerr#428:
    arm64.movImm x0, 1
    arm64.b __il_cont#425
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#427:
    arm64.movImm x0, 0
  __il_cont#425:
    arm64.cmp x0, 0
    arm64.b.ne forhdr#188
  tryok#409:
    arm64.movImm x0, 0
  forhdr#188:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#403
  forexit#192:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
  forjoin:
    arm64.loadRegSlot x0, slot42
    arm64.and x0, x0, 7
    arm64.cmp x0, 1
    arm64.b.ne matchnext#208
  matcharm#200:
    arm64.movImm x0, 0
    arm64.b __il_cont#174
  matchnext#208:
    arm64.cmp x0, 2
    arm64.b.ne matcharm#204
  matcharm#201:
    arm64.loadRegSlot x0, slot42
    arm64.sub x19, x0, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x19
    arm64.bl requireNulFreeEnvironment
    arm64.movRegReg x20, x9
    arm64.cmp x20, 0
    arm64.b.eq tryok#202
  tryerr#203:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#174
  tryok#202:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.b matchcont#210
  matcharm#204:
    arm64.loadRegSlot x0, slot42
    arm64.sub x19, x0, 3
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x19
    arm64.bl requireNulFreeEnvironment
    arm64.movRegReg x20, x9
    arm64.cmp x20, 0
    arm64.b.eq tryok#205
  tryerr#206:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#174
  tryok#205:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
  matchcont#210:
    arm64.movImm x0, 0
    arm64.b __il_cont#174
  tryerr#187:
    arm64.movRegReg x0, x9
  __il_cont#174:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#225
  tryerr#166:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#163
  __il_body#225:
    arm64.loadRegSlot x0, slot37
    arm64.cbz x0, matcharm#230
  matcharm#226:
    arm64.loadRegSlot x0, slot33
    arm64.sub x19, x0, 1
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
  __il_body#412:
    arm64.movRegReg x0, x19
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#413
  tryerr#414:
    arm64.movImm x20, 0
    arm64.cbz x1, __il_body#433
  tryerr#237:
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
    arm64.b __il_cont#234
  tryok#413:
    arm64.movRegReg x20, x0
  __il_body#433:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#440:
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
  __il_cont#439:
    arm64.movImm x22, 0
  whilehdr#434:
    arm64.cmp x22, x21
    arm64.movImm x0, 0
    arm64.b.hs scmerge#436
  andrhs#435:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#442:
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#444
  __il_cont#441:
    arm64.cmp x0, 47
    arm64.cset x0, eq
  scmerge#436:
    arm64.cbz x0, __il_cont#432
  leadingSeparators:
    arm64.add x22, x22, 1
    arm64.b whilehdr#434
  __il_cont#432:
    arm64.cmp x22, 0
    arm64.movImm x21, 1
    arm64.b.hi critsplit#480
  __il_body#260:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#418:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont#417:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#262
  empty:
    arm64.movImm x21, 0
    arm64.b __il_cont#259
  ifcont#262:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#420:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.b whilehdr#263
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
    arm64.b.lt __rc_panic#269
  __rc_ok#268:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#422:
    arm64.movRegReg x1, x21
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#424
  __il_cont#421:
    arm64.cmp x0, 47
    arm64.b.ne ifcont#267
  isSep:
    arm64.add x21, x21, 1
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic#271
    arm64.b __il_cont#259
  ifcont#267:
    arm64.movRegReg x0, x21
  whilehdr#263:
    arm64.cmp x0, 0
    arm64.b.hi scan
  whileexit#265:
    arm64.movImm x21, 0
  __il_cont#259:
    arm64.cmp x21, 0
    arm64.b.ne ifcont#255
  noSep:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
    arm64.b __il_cont#252
  ifcont#255:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#416:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
  __il_cont#415:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl String.addressableBytes
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __managed_slice
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#256
  tryerr#257:
    arm64.leaRdata x0, __str_blob_124  ; "panic at FilePath.maxon:354: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryok#256:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movRegReg x0, x21
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitterminal#258:
    arm64.movRegReg x21, x22
  __il_cont#252:
    arm64.loadRegBaseDisp.word64 x1, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __str_eq
    arm64.eor x22, x0, 1
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.b scmerge#239
  critsplit#480:
    arm64.movRegReg x22, x21
  scmerge#239:
    arm64.cbz x22, ternaryfalse
  ternarytrue:
    arm64.loadRegSlot x1, slot29
    arm64.movRegReg x0, x20
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x21, x0
    arm64.b matchcont#242
  ternaryfalse:
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
  matchcont#242:
    arm64.leaRdata x23, __str_rec_117  ; "/"
    arm64.cbz x22, ifcont#245
  isAPath:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x21
    arm64.b exitterminal#244
  ifcont#245:
    arm64.movRegReg x0, x21
    arm64.bl String.cstr
    arm64.bl __gt_subp_resolve_path
    arm64.bl String.init
    arm64.movRegReg x22, x0
  __il_body#452:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 8]
  __il_body#448:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x22
    arm64.movRegReg x3, x23
    arm64.bl findIn
    arm64.cmp x9, 0
    arm64.b.eq tryok#449
  tryerr#450:
    arm64.movImm x0, 0
    arm64.b __il_cont#445
  tryok#449:
    arm64.movImm x0, 1
  __il_cont#445:
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#250
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
  exitdrop#248:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#249:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
  exitterminal#247:
    arm64.movImm x20, 0
    arm64.movRegReg x21, x20
    arm64.movRegReg x20, x23
    arm64.b __il_cont#234
  ifcont#250:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#251:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
  exitterminal#244:
    arm64.movImm x20, 0
    arm64.movRegReg x21, x0
  __il_cont#234:
    arm64.cmp x20, 0
    arm64.b.eq tryok#227
  tryerr#228:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
  exitterminal#229:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.b __il_cont#224
  tryok#227:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movRegReg x0, x21
    arm64.b matchcont#233
  matcharm#230:
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
  matchcont#233:
    arm64.movImm x1, 0
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x1
  __il_cont#224:
    arm64.storeSlotReg slot26, x19
    arm64.cmp x0, 0
    arm64.b.eq __il_body#212
  tryerr#168:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#163
  __il_body#212:
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
    arm64.b forhdr#213
  arg:
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#221
  __im_load#222:
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
    arm64.cbz x0, critsplit#472
  __im_empty:
    arm64.cmp x1, 0
    arm64.b.eq __im_slow#221
  critsplit#473:
    arm64.movRegReg x9, x2
    arm64.b __im_cont#220
  critsplit#472:
    arm64.movRegReg x9, x2
  __im_cont#220:
    arm64.cmp x9, 0
    arm64.b.ne tryerr#218
  tryok#217:
    arm64.movRegReg x0, x20
    arm64.bl appendNulTerminated
  forstep#215:
    arm64.add x21, x21, 1
  forhdr#213:
    arm64.cmp x21, x19
    arm64.b.lo arg
  forexit#216:
    arm64.movRegReg x0, x20
    arm64.bl String.from
    arm64.storeSlotReg slot30, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#211:
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.add x0, x0, 1
    arm64.storeSlotReg slot22, x0
  __il_body#273:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.loadRegSlot x0, slot42
    arm64.and x0, x0, 7
    arm64.cmp x0, 1
    arm64.b.ne matchnext#285
  __il_body#295:
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
  __il_cont#294:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x21
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.b exitterminal#275
  matchnext#285:
    arm64.cmp x0, 2
    arm64.b.ne matcharm#281
  matcharm#276:
    arm64.loadRegSlot x0, slot42
    arm64.sub x20, x0, 2
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
  __il_body#298:
    arm64.movRegReg x0, x20
    arm64.bl namesByKey
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#300
  __il_body#319:
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x23, 0
  eachEntry:
    arm64.movRegReg x0, x23
    arm64.bl __gt_subp_env_entry
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x0, 0
    arm64.b.ne ifcont#324
  pastTheEnd:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  whileexit#322:
    arm64.movImm x0, 0
    arm64.b __il_cont#318
  ifcont#324:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x0, 1
    arm64.b.ne ifcont#332
  oneByte:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x24
    arm64.bl __managed_byte_at
    arm64.cmp x9, 0
    arm64.b.ne tryerr#327
  tryok#326:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#332
  hostFailed:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#330:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitterminal#329:
    arm64.movImm x0, 3
    arm64.movImm x22, 0
  __il_cont#318:
    arm64.cmp x0, 0
    arm64.b.eq tryok#301
  tryerr#302:
    arm64.leaRdata x22, __str_rec_210  ; "this process's environment could not be read, so no child can inherit it"
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
    arm64.b __il_cont#297
  tryok#301:
    arm64.movImm x0, 0
    arm64.b forhdr#304
  eachInherited:
    arm64.loadRegBaseDisp.word64 x1, [x22 + 0]
    arm64.loadRegBaseIndexScale.word64 x23, [x1 + x0*8 + 0]
  __im_cont#317:
    arm64.cmp x23, 0
    arm64.b.eq emptyslot
  slotfilled:
    arm64.movRegReg x0, x23
    arm64.bl __str_retain
    arm64.storeSlotReg slot39, x0
  __il_body#364:
    arm64.movRegReg x0, x23
    arm64.bl String.toByteArray
    arm64.movRegReg x24, x0
    arm64.movImm x25, 0
  __il_body#368:
    arm64.loadRegBaseDisp.word64 x26, [x24 + 8]
    arm64.movImm x1, 1
  __us_test:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.b forhdr#369
  eachByte#370:
    arm64.cmp x1, x26
    arm64.b.hs __im_slow#378
  __im_load#379:
    arm64.add x2, x0, x1
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  tryok#373:
    arm64.cmp x2, 61
    arm64.b.eq atSeparator
  forstep#371:
    arm64.add x1, x1, 1
  forhdr#369:
    arm64.cmp x1, x26
    arm64.b.lo eachByte#370
    arm64.b __il_body#380
  atSeparator:
    arm64.movRegReg x26, x1
  __il_body#380:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x27, x0
    arm64.movRegReg x1, x25
    arm64.b forhdr#381
  eachByte#382:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#389
  __im_load#390:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
  tryok#385:
    arm64.loadRegBaseDisp.word64 x0, [x27 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x27 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#392
  __im_bounds#393:
    arm64.loadRegBaseDisp.word64 x3, [x27 + 16]
    arm64.cmp x0, x3
    arm64.b.hs __im_slow#392
  __im_buffer#394:
    arm64.loadRegBaseDisp.word64 x3, [x27 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#392
  __im_viewed#395:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#392
  __im_store#396:
    arm64.add x3, x3, x0
    arm64.storeBaseDispReg.byte [x3 + 0], x2
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
  forstep#383:
    arm64.add x1, x1, 1
  forhdr#381:
    arm64.cmp x1, x26
    arm64.b.lo eachByte#382
  forexit#384:
    arm64.movRegReg x0, x27
    arm64.bl String.from
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x27
    arm64.bl __managed_decref
  __il_cont#366:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  __il_cont#363:
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
    arm64.storeSlotReg slot44, x2
    arm64.cmp x9, 0
    arm64.b.ne ifelse
  overridden:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl appendEnvEntry
    arm64.leaRdata x23, __witness_String.Hashable
    arm64.leaRdata x25, __witness_String.Equatable
    arm64.leaRdata x0, __layout_Map_String_String
    arm64.storeSlotReg slot50, x0
  __il_body#456:
    arm64.movImm x26, 5381
  __il_body#464:
    arm64.loadRegBaseDisp.word64 x27, [x24 + 8]
  __il_cont#463:
    arm64.movImm x28, 0
    arm64.b forhdr#457
  hash_loop:
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x28
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#462
  tryok#461:
    arm64.movImm x16, 33
    arm64.mul x1, x26, x16
    arm64.add x0, x1, x0
  forstep#459:
    arm64.add x28, x28, 1
    arm64.movRegReg x26, x0
  forhdr#457:
    arm64.cmp x28, x27
    arm64.b.lo hash_loop
  forexit#460:
    arm64.movImm x0, 4294967295
    arm64.and x2, x26, x0
  __il_cont#453:
    arm64.loadRegSlot x5, slot50
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x24
    arm64.movRegReg x3, x23
    arm64.movRegReg x4, x25
    arm64.bl Map.findSlot
    arm64.movRegReg x26, x0
    arm64.cmp x9, 0
    arm64.b.ne __il_cont#339
  tryok#341:
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x23
    arm64.movRegReg x3, x25
    arm64.bl Map.slotHoldsKey
    arm64.cbz x0, __il_cont#339
  found:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.movRegReg x1, x26
    arm64.bl Array.clearSlot
    arm64.cmp x9, 0
    arm64.b.ne tryerr#345
  tryok#344:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.movRegReg x1, x26
    arm64.bl Array.clearSlot
    arm64.cmp x9, 0
    arm64.b.ne tryerr#347
  tryok#346:
    arm64.movImm x0, 2
    arm64.loadRegBaseDisp.word64 x1, [x21 + 16]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.cmp x26, x2
    arm64.b.hs __im_slow#356
  __im_buffer#357:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x3, [x1 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach
  __im_viewed#361:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach
  critsplit#475:
    arm64.movRegReg x1, x2
  __im_detached:
    arm64.add x1, x1, x26
    arm64.storeBaseDispReg.byte [x1 + 0], x0
  tryok#348:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 32]
    arm64.sub x0, x0, 1
    arm64.movImm x1, 9223372036854775808
    arm64.cmp x0, x1
    arm64.b.hi __rc_panic#352
  __rc_ok#351:
    arm64.storeBaseDispReg.word64 [x21 + 32], x0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 48]
    arm64.add x0, x0, 1
    arm64.movImm x1, 9223372036854775808
    arm64.cmp x0, x1
    arm64.b.hi __rc_panic#354
  __rc_ok#353:
    arm64.storeBaseDispReg.word64 [x21 + 48], x0
  __il_cont#339:
    arm64.loadRegSlot x0, slot44
    arm64.bl __str_decref
    arm64.b ifcont#312
  ifelse:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl appendNulTerminated
  ifcont#312:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot39
    arm64.bl __str_decref
  forstep#306:
    arm64.loadRegSlot x0, slot35
    arm64.add x0, x0, 1
  forhdr#304:
    arm64.storeSlotReg slot35, x0
    arm64.loadRegBaseDisp.word64 x1, [x22 + 8]
    arm64.cmp x0, x1
    arm64.b.lt eachInherited
  forexit#307:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl appendEnvEntries
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#314:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Map_String_String
  exitterminal#313:
    arm64.movImm x21, 0
    arm64.movRegReg x23, x21
    arm64.b __il_cont#297
  tryerr#300:
    arm64.movRegReg x23, x9
  __il_cont#297:
    arm64.cmp x23, 0
    arm64.b.eq tryok#277
  tryerr#278:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.b exitdrop#280
  tryok#277:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.b matchcont#287
  ifcont#332:
    arm64.movRegReg x0, x24
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x1, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#334
  __im_bounds#335:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#334
  __im_buffer#336:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#334
  __im_viewed#337:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#334
  __im_store#338:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
  __im_cont#333:
    arm64.add x23, x23, 1
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
    arm64.b eachEntry
  matcharm#281:
    arm64.loadRegSlot x0, slot42
    arm64.sub x20, x0, 3
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
    arm64.movRegReg x0, x20
    arm64.bl namesByKey
    arm64.movRegReg x21, x0
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#282
  tryerr#283:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x23, x22
  exitdrop#280:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal#279:
    arm64.movImm x19, 0
    arm64.b __il_cont#272
  tryok#282:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl appendEnvEntries
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
  matchcont#287:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#289
  __im_bounds#290:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#289
  __im_buffer#291:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#289
  __im_viewed#292:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#289
  __im_store#293:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
  __im_cont#288:
    arm64.movRegReg x0, x19
    arm64.bl String.from
    arm64.movImm x1, 0
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
  exitterminal#275:
    arm64.movImm x19, 0
    arm64.movRegReg x23, x19
    arm64.movRegReg x19, x0
  __il_cont#272:
    arm64.cmp x23, 0
    arm64.b.eq tryok#169
  tryerr#170:
    arm64.loadRegSlot x0, slot30
    arm64.bl __str_decref
  exitdrop#172:
    arm64.loadRegSlot x0, slot26
    arm64.bl __str_decref
  exitterminal#171:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x23
    arm64.b __il_cont#163
  tryok#169:
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
  exitterminal#173:
    arm64.movImm x19, 0
    arm64.xchgRegReg x19, x20
  __il_cont#163:
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
  __il_body#85:
    arm64.and x0, x20, 7
    arm64.sub x0, x0, 1
    arm64.jumpTable x0, default=matchdead [[0]=matcharm#86, [1]=matcharm#87, [2]=matcharm#88, [3]=matcharm#89, [4]=matcharm#90] @__jumptable_389
  matchdead:
  matcharm#86:
    arm64.movImm x20, 0
    arm64.leaRdata x0, __str_rec_115  ; ""
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
    arm64.b matchcont#92
  matcharm#87:
    arm64.movImm x20, 1
    arm64.leaRdata x0, __str_rec_115  ; ""
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
    arm64.b matchcont#92
  matcharm#88:
    arm64.movImm x20, 2
    arm64.leaRdata x0, __str_rec_115  ; ""
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
    arm64.b matchcont#92
  matcharm#89:
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
    arm64.b matchcont#92
  matcharm#90:
    arm64.movImm x20, 4
    arm64.leaRdata x0, __str_rec_115  ; ""
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
  matchcont#92:
    arm64.storeSlotReg slot5, x20
  __il_cont#84:
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
    arm64.storeSlotReg slot48, x23
    arm64.movRegReg x0, x24
    arm64.bl __destruct_OutputDestination
    arm64.loadRegBaseDisp.word64 x0, [x21 + 64]
  __il_body#93:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
    arm64.cbz x2, ifcont#95
  __rc_ok#98:
    arm64.movImm x1, 1
  ifcont#95:
    arm64.loadRegBaseDisp.byte x0, [x0 + 8]
    arm64.cbz x0, critsplit#468
  newGroup:
    arm64.orr x21, x1, 2
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic#102
  __rc_chk#103:
    arm64.cmp x21, 15
    arm64.b.gt __rc_panic#102
    arm64.b __il_cont#83
  critsplit#468:
    arm64.movRegReg x21, x1
  __il_cont#83:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.addressableBytes
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x25, [x19 + 16]
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x26, [x0 + 16]
    arm64.movRegReg x0, x26
    arm64.bl __mm_retain
    arm64.storeSlotReg slot18, x0
  __il_body#104:
    arm64.loadRegBaseDisp.word64 x0, [x26 + 0]
    arm64.bl __str_retain
    arm64.storeSlotReg slot15, x0
  __il_cont#82:
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
    arm64.storeSlotReg slot49, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.storeSlotReg slot43, x0
    arm64.loadRegBaseDisp.word64 x19, [x20 + 8]
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.storeSlotReg slot24, x0
    arm64.movRegReg x0, x19
    arm64.bl String.cstr
    arm64.storeSlotReg slot38, x0
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
    arm64.loadRegSlot x6, slot38
    arm64.loadRegSlot x5, slot43
    arm64.loadRegSlot x4, slot49
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.movRegReg x3, x27
    arm64.bl __gt_subp_attached_spawn
    arm64.movRegReg x19, x0
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
  __il_body#106:
    arm64.bl __gt_subp_last_error_code
    arm64.movRegReg x20, x0
    arm64.asr x0, x20, 32
    arm64.bl __enum_from_raw_SpawnPreparation
    arm64.cmp x9, 0
    arm64.b.ne tryerr#108
  tryok#107:
    arm64.movImm x1, 4294967295
    arm64.and x1, x20, x1
    arm64.cmp x1, 2
    arm64.movImm x2, 1
    arm64.b.eq critsplit#469
  orrhs:
    arm64.cmp x1, 3
    arm64.cset x1, eq
    arm64.b scmerge#110
  critsplit#469:
    arm64.movRegReg x1, x2
  scmerge#110:
    arm64.movImm x2, 0
    arm64.cbz x1, critsplit#470
  andrhs#111:
    arm64.cmp x0, 0
    arm64.cset x0, eq
    arm64.b scmerge#112
  critsplit#470:
    arm64.movRegReg x0, x2
  scmerge#112:
    arm64.cbz x0, ifcont#114
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
    arm64.b __il_cont#105
  ifcont#114:
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
  __il_cont#105:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_StdioRuntimeTriple
    arm64.b exitdrop#48
  ifcont#51:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic#76
  __rc_ok#75:
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x20, [x0 + 32]
    arm64.movRegReg x0, x20
    arm64.bl __uretain_InputSource
    arm64.movRegReg x21, x0
  __il_body#152:
    arm64.and x0, x20, 7
    arm64.cmp x0, 1
    arm64.cset x1, ge
    arm64.cmp x0, 2
    arm64.cset x2, le
    arm64.and x1, x1, x2
    arm64.cbnz x1, __il_cont#151
  matchnext#156:
    arm64.cmp x0, 3
    arm64.b.ne __il_cont#151
  matcharm#153:
    arm64.sub x20, x20, 3
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
  __il_body#160:
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movRegReg x1, x0
  __il_body#400:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
  __il_cont#399:
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_queue_stdin
    arm64.cmp x0, 0
    arm64.b.eq __il_cont#159
  refused:
    arm64.leaRdata x0, __str_rec_219  ; "panic at Subprocess.maxon:858: "
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.leaRdata x0, __str_rec_220  ; "Subprocess: the runtime refused the stdin payload of handle "
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
    arm64.leaRdata x0, __str_rec_221  ; ", whose spawn has just answered it with a pipe"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_86  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot53, x0
    arm64.add x1, x21, x23
    arm64.add x1, x1, x19
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot41, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot46, x0
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
    arm64.loadRegSlot x2, slot53
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x19, slot46
    arm64.storeBaseDispReg.word64 [x19 + 0], x28
    arm64.loadRegSlot x0, slot41
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
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __il_cont#159:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  __il_cont#151:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_InputSource
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_get_pid
    arm64.storeSlotReg slot52, x0
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x1, [x0 + 56]
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_wait_collect
    arm64.movRegReg x20, x0
    arm64.cmp x20, -1
    arm64.b.ne ifcont#54
  waitFailed:
    arm64.bl lastErrorMessage
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_release
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  exitdrop#53:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_StdioRuntimeTriple
    arm64.movRegReg x20, x19
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
  ifcont#54:
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_kind
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_code
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_stdout
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_stderr
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_duration
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x24
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x27, x0
    arm64.movRegReg x0, x25
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_release
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_release
    arm64.movRegReg x0, x21
    arm64.bl __enum_from_raw_ResultStatusKind
    arm64.cmp x9, 0
    arm64.b.ne tryerr#56
  tryok#55:
    arm64.cmp x22, 0
    arm64.b.lt __rc_panic#78
  __rc_chk#79:
    arm64.movImm x1, 4294967295
    arm64.cmp x22, x1
    arm64.b.gt __rc_panic#78
  __rc_ok#124:
    arm64.cmp x0, 0
    arm64.b.ne matchnext#121
  matcharm#117:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x22
    arm64.movImm x19, 0
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x0
    arm64.b __il_cont#115
  matchnext#121:
    arm64.cmp x0, 1
    arm64.b.ne matcharm#119
  matcharm#118:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x22
    arm64.movImm x19, 0
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x0
    arm64.b __il_cont#115
  matcharm#119:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.storeBaseDispReg.word64 [x19 + 8], x26
    arm64.movRegReg x0, x27
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movRegReg x0, x28
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x20, 0
    arm64.xchgRegReg x19, x20
  __il_cont#115:
    arm64.cmp x20, 0
    arm64.b.eq tryok#57
  tryerr#58:
    arm64.movRegReg x0, x28
    arm64.bl __str_decref
  exitdrop#59:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#60:
    arm64.movRegReg x0, x25
    arm64.bl __managed_decref
  exitdrop#61:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#62:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#63:
    arm64.loadRegSlot x0, slot7
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#64:
    arm64.loadRegSlot x0, slot5
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#65:
    arm64.loadRegSlot x0, slot12
    arm64.bl __destruct_SpawnOperands
    arm64.movRegReg x0, x20
  exitterminal#38:
    arm64.movImm x1, 0
    arm64.xchgRegReg x0, x1
    arm64.b __il_cont#34
  tryok#57:
    arm64.loadRegSlot x0, slot52
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#81
  __rc_ok#80:
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x27
    arm64.bl __str_retain
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x28
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
  __il_body#128:
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x22 + 0], x19
    arm64.movRegReg x0, x20
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 16], x0
    arm64.loadRegSlot x0, slot52
    arm64.storeBaseDispReg.word64 [x22 + 24], x0
    arm64.storeBaseDispReg.word64 [x22 + 32], x26
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#130:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  exitdrop#131:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  __il_cont#127:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  exitdrop#67:
    arm64.movRegReg x0, x28
    arm64.bl __str_decref
  exitdrop#68:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#69:
    arm64.movRegReg x0, x25
    arm64.bl __managed_decref
  exitdrop#70:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#71:
    arm64.loadRegSlot x0, slot48
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#72:
    arm64.loadRegSlot x0, slot7
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#73:
    arm64.loadRegSlot x0, slot5
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#74:
    arm64.loadRegSlot x0, slot12
    arm64.bl __destruct_SpawnOperands
  exitterminal#66:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x22
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
  __il_body#398:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.storeSlotReg slot9, x22
  __il_cont#397:
    arm64.leaRdata x1, __str_rec_81  ; "hi"
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.startsWith
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_82  ; "timerpolls="
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.cmp x20, 16
    arm64.cset x20, ls
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot51, x1
    arm64.movRegReg x0, x20
    arm64.bl __bool_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_83  ; " onpoller="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot45, x1
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.cmp x21, 1
    arm64.cset x21, hs
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot40, x1
    arm64.movRegReg x0, x21
    arm64.bl __bool_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_84  ; " outLen="
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
    arm64.leaRdata x0, __str_rec_85  ; " matches="
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
    arm64.leaRdata x1, __str_rec_86  ; "\x0a"
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
    arm64.loadRegSlot x1, slot51
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x23, x20
    arm64.loadRegSlot x1, slot45
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x20, x20, x25
    arm64.loadRegSlot x1, slot40
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
    arm64.loadRegSlot x0, slot51
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot40
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
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __im_slow#430:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#428
    arm64.b tryok#427
  __im_slow#221:
    arm64.loadRegSlot x0, slot25
    arm64.movRegReg x1, x21
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.b __im_cont#220
  __im_slow#378:
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.cmp x9, 0
    arm64.b.ne tryerr#374
    arm64.b tryok#373
  __im_slow#389:
    arm64.storeSlotReg slot56, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.loadRegSlot x1, slot56
    arm64.cmp x9, 0
    arm64.b.ne tryerr#386
  critsplit#477:
    arm64.movRegReg x2, x0
    arm64.b tryok#385
  __im_slow#392:
    arm64.storeSlotReg slot56, x1
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x2
    arm64.bl __managed_push
    arm64.loadRegSlot x1, slot56
    arm64.b forstep#383
  __im_slow#356:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x26
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#349
    arm64.b tryok#348
  __im_detach:
    arm64.storeSlotReg slot56, x1
    arm64.movRegReg x0, x1
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 2
    arm64.loadRegSlot x1, slot56
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.b __im_detached
  __im_slow#334:
    arm64.movRegReg x0, x22
    arm64.bl __managed_push
    arm64.b __im_cont#333
  __im_slow#289:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.b __im_cont#288
  __rc_panic#406:
    arm64.leaRdata x0, __str_blob_272  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#444:
    arm64.leaRdata x0, __str_blob_185  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#269:
    arm64.leaRdata x0, __str_blob_278  ; "panic at FilePath.maxon:334: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#424:
    arm64.leaRdata x0, __str_blob_185  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#271:
    arm64.leaRdata x0, __str_blob_279  ; "panic at FilePath.maxon:337: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#218:
    arm64.leaRdata x0, __str_blob_198  ; "panic at Subprocess.maxon:642: buildArgvBlob: args.get(i) out of bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#327:
    arm64.leaRdata x0, __str_blob_158  ; "panic at Process.maxon:116: Process.currentEnvironmentEntries: byteAt(0) of a one-byte answer\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  emptyslot:
    arm64.leaRdata x0, __str_rec_211  ; "panic at Subprocess.maxon:769: "
  __rc_ok#315:
    arm64.loadRegSlot x1, slot35
    arm64.bl __emptySlotMessage
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#374:
    arm64.leaRdata x0, __str_rec_159  ; "panic at Process.maxon:180: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.leaRdata x0, __str_rec_160  ; "envEntrySeparatorAt: i < n = bytes.count() but get("
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x6, 0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.movRegReg x1, x6
    arm64.bl __mm_alloc
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x6
    arm64.bl __uint_to_string
    arm64.movRegReg x7, x0
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.leaRdata x0, __str_rec_161  ; ") failed"
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.leaRdata x0, __str_rec_86  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x9, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x10, [x0 + 8]
    arm64.add x0, x3, x5
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x10
    arm64.add x11, x0, 57
    arm64.movImm x12, 0
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x11
    arm64.movRegReg x1, x12
    arm64.bl __mm_alloc
    arm64.movRegReg x11, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.add x12, x11, 56
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x12
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x12, x3
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x4
    arm64.movRegReg x2, x5
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x2, x5
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x6
    arm64.movRegReg x2, x7
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x2, x7
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x1, x2, x8
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x9
    arm64.movRegReg x2, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.storeBaseDispReg.word64 [x11 + 0], x12
    arm64.storeBaseDispReg.word64 [x11 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x11 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x11 + 24], x0
    arm64.storeSlotReg slot66, x11
    arm64.movRegReg x0, x6
    arm64.bl __mm_decref
    arm64.loadRegSlot x11, slot66
    arm64.loadRegBaseDisp.word64 x0, [x11 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#386:
    arm64.leaRdata x0, __str_rec_162  ; "panic at Process.maxon:194: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.leaRdata x0, __str_rec_163  ; "envEntryBytes: i < to <= bytes.count() but get("
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x6, 0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.movRegReg x1, x6
    arm64.bl __mm_alloc
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x6
    arm64.bl __uint_to_string
    arm64.movRegReg x7, x0
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.leaRdata x0, __str_rec_161  ; ") failed"
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.leaRdata x0, __str_rec_86  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x9, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x10, [x0 + 8]
    arm64.add x0, x3, x5
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x10
    arm64.add x11, x0, 57
    arm64.movImm x12, 0
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x11
    arm64.movRegReg x1, x12
    arm64.bl __mm_alloc
    arm64.movRegReg x11, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.add x12, x11, 56
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x12
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x12, x3
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x4
    arm64.movRegReg x2, x5
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x2, x5
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x6
    arm64.movRegReg x2, x7
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x2, x7
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x1, x2, x8
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x9
    arm64.movRegReg x2, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.storeBaseDispReg.word64 [x11 + 0], x12
    arm64.storeBaseDispReg.word64 [x11 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x11 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x11 + 24], x0
    arm64.storeSlotReg slot66, x11
    arm64.movRegReg x0, x6
    arm64.bl __mm_decref
    arm64.loadRegSlot x11, slot66
    arm64.loadRegBaseDisp.word64 x0, [x11 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#462:
    arm64.leaRdata x0, __str_blob_257  ; "panic at hash.maxon:24: hashString: byteAt OOB \xe2\x80\x94 i < s.byteLength() invariant\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#345:
    arm64.leaRdata x0, __str_blob_143  ; "panic at Map.maxon:209: Map.remove: keys.clearSlot OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#347:
    arm64.leaRdata x0, __str_blob_144  ; "panic at Map.maxon:210: Map.remove: values.clearSlot OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#349:
    arm64.leaRdata x0, __str_blob_145  ; "panic at Map.maxon:211: Map.remove: states.set OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __im_missing:
    arm64.leaRdata x0, __str_blob_387  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#352:
    arm64.leaRdata x0, __str_blob_289  ; "panic at Map.maxon:212: Range check failed: value outside typealias 'EntryCount'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#354:
    arm64.leaRdata x0, __str_blob_290  ; "panic at Map.maxon:213: Range check failed: value outside typealias 'TableCapacity'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#102:
    arm64.leaRdata x0, __str_blob_339  ; "panic at Subprocess.maxon:881: Range check failed: value outside typealias 'SpawnFlagsValue'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#108:
    arm64.leaRdata x0, __str_blob_222  ; "panic at Subprocess.maxon:908: Subprocess.spawnFailure: the runtime recorded a spawn step this library does not know\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#76:
    arm64.leaRdata x0, __str_blob_332  ; "panic at Subprocess.maxon:497: Range check failed: value outside typealias 'StreamingHandle'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#56:
    arm64.leaRdata x0, __str_rec_196  ; "panic at Subprocess.maxon:519: "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.leaRdata x0, __str_rec_197  ; "Subprocess.runConfiguration: runtime returned unexpected statusKind raw="
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x5, 0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.movRegReg x1, x5
    arm64.bl __mm_alloc
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x5
    arm64.bl __int_to_string
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.leaRdata x0, __str_rec_86  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x7, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.add x0, x2, x4
    arm64.add x0, x0, x6
    arm64.add x0, x0, x8
    arm64.add x9, x0, 57
    arm64.movImm x10, 0
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.movRegReg x0, x9
    arm64.movRegReg x1, x10
    arm64.bl __mm_alloc
    arm64.movRegReg x9, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.add x10, x9, 56
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.add x1, x10, x2
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x4
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.add x1, x1, x4
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x5
    arm64.movRegReg x2, x6
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.add x1, x1, x6
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x7
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.storeBaseDispReg.word64 [x9 + 0], x10
    arm64.storeBaseDispReg.word64 [x9 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x9 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x9 + 24], x0
    arm64.storeSlotReg slot64, x9
    arm64.movRegReg x0, x5
    arm64.bl __mm_decref
    arm64.loadRegSlot x9, slot64
    arm64.loadRegBaseDisp.word64 x0, [x9 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#78:
    arm64.leaRdata x0, __str_blob_333  ; "panic at Subprocess.maxon:520: Range check failed: value outside typealias 'ExitInt'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#81:
    arm64.leaRdata x0, __str_blob_334  ; "panic at Subprocess.maxon:521: Range check failed: value outside typealias 'Pid'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#2:
    arm64.leaRdata x0, __str_blob_87  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:11: /bin/sh runs on every POSIX lane\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#8:
    arm64.leaRdata x0, __str_blob_269  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:18: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
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
  npholdnotended:
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
    arm64.prologue 688 record@48
    arm64.storeSlotReg slot77, x28
    arm64.storeSlotReg slot76, x27
    arm64.storeSlotReg slot75, x26
    arm64.storeSlotReg slot74, x25
    arm64.storeSlotReg slot73, x24
    arm64.storeSlotReg slot72, x23
    arm64.storeSlotReg slot71, x22
    arm64.storeSlotReg slot70, x21
    arm64.storeSlotReg slot69, x20
    arm64.storeSlotReg slot68, x19
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_78  ; "-c"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_79  ; "sleep 1; echo hi"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.bl Scheduler.counters
    arm64.storeSlotReg slot1, x0
    arm64.leaRdata x0, __str_rec_80  ; "/bin/sh"
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.orr x20, x0, 1
    arm64.storeSlotReg slot2, x20
  __il_body#17:
    arm64.leaRdata x21, __str_rec_115  ; ""
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
  __il_body#141:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x1, ge
    arm64.cmp x0, 3
    arm64.cset x2, le
    arm64.and x1, x1, x2
    arm64.cbnz x1, matcharm#146
  matchnext#148:
    arm64.cmp x0, 4
    arm64.b.ne matcharm#146
  matcharm#142:
    arm64.sub x19, x19, 4
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_209  ; "the standard input file path"
    arm64.bl requireNulFree
    arm64.movRegReg x26, x9
    arm64.cmp x26, 0
    arm64.b.eq tryok#143
  tryerr#144:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  exitterminal#145:
    arm64.movRegReg x0, x26
    arm64.b __il_cont#140
  tryok#143:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  matchcont#150:
    arm64.movImm x0, 0
    arm64.b __il_cont#140
  matcharm#146:
    arm64.movImm x0, 0
  __il_cont#140:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#135
  tryok#134:
    arm64.leaRdata x1, __str_rec_207  ; "the standard output file path"
    arm64.movRegReg x0, x21
    arm64.bl requireNulFreeOutputFile
    arm64.cmp x9, 0
    arm64.b.ne tryerr#137
  tryok#136:
    arm64.leaRdata x1, __str_rec_208  ; "the standard error file path"
    arm64.movRegReg x0, x23
    arm64.bl requireNulFreeOutputFile
    arm64.cmp x9, 0
    arm64.b.ne tryerr#139
  tryok#138:
    arm64.movImm x19, 0
    arm64.b __il_cont#132
  tryerr#135:
    arm64.movRegReg x19, x0
    arm64.b __il_cont#132
  tryerr#137:
    arm64.movRegReg x19, x9
    arm64.b __il_cont#132
  tryerr#139:
    arm64.movRegReg x19, x9
  __il_cont#132:
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
    arm64.storeSlotReg slot42, x0
    arm64.bl __uretain_Environment
    arm64.storeSlotReg slot19, x0
  __il_body#175:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x0, eq
    arm64.storeSlotReg slot37, x0
    arm64.b.ne matcharm#180
  matcharm#176:
    arm64.sub x19, x19, 1
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.leaRdata x1, __str_rec_200  ; "the program name"
    arm64.movRegReg x0, x19
    arm64.bl requireNulFree
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#177
  tryerr#178:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movRegReg x0, x22
    arm64.b __il_cont#174
  tryok#177:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.b matchcont#185
  matcharm#180:
    arm64.sub x19, x19, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_201  ; "the program path"
    arm64.bl requireNulFree
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#181
  tryerr#182:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
    arm64.b __il_cont#174
  tryok#181:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
  matchcont#185:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.leaRdata x1, __str_rec_202  ; "the working directory"
    arm64.bl requireNulFree
    arm64.cmp x9, 0
    arm64.b.ne tryerr#187
  tryok#186:
    arm64.leaRdata x1, __layout_Array_String
    arm64.movRegReg x0, x20
    arm64.bl Array.withIterator
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr#188
    arm64.b forjoin
  __il_body#403:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot47, x20
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __adapt___witness_ArrayIterator_String.Iterator.current
    arm64.movRegReg x21, x0
  __il_cont#402:
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.storeSlotReg slot54, x0
    arm64.leaRdata x0, __str_rec_203  ; "argument "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x24, x0
  __il_body#404:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#406
  __il_cont#401:
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
    arm64.b.eq tryok#193
  tryerr#194:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#195:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
  exitdrop#196:
    arm64.loadRegSlot x0, slot54
    arm64.bl __str_decref
  exitdrop#197:
    arm64.loadRegSlot x0, slot47
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#198:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#174
  tryok#193:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
    arm64.loadRegSlot x0, slot54
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot47
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  __il_body#408:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#426:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#430
    arm64.b __im_move
  tryerr#428:
    arm64.movImm x0, 1
    arm64.b __il_cont#425
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#427:
    arm64.movImm x0, 0
  __il_cont#425:
    arm64.cmp x0, 0
    arm64.b.ne forhdr#188
  tryok#409:
    arm64.movImm x0, 0
  forhdr#188:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#403
  forexit#192:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
  forjoin:
    arm64.loadRegSlot x0, slot42
    arm64.and x0, x0, 7
    arm64.cmp x0, 1
    arm64.b.ne matchnext#208
  matcharm#200:
    arm64.movImm x0, 0
    arm64.b __il_cont#174
  matchnext#208:
    arm64.cmp x0, 2
    arm64.b.ne matcharm#204
  matcharm#201:
    arm64.loadRegSlot x0, slot42
    arm64.sub x19, x0, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x19
    arm64.bl requireNulFreeEnvironment
    arm64.movRegReg x20, x9
    arm64.cmp x20, 0
    arm64.b.eq tryok#202
  tryerr#203:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#174
  tryok#202:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.b matchcont#210
  matcharm#204:
    arm64.loadRegSlot x0, slot42
    arm64.sub x19, x0, 3
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x19
    arm64.bl requireNulFreeEnvironment
    arm64.movRegReg x20, x9
    arm64.cmp x20, 0
    arm64.b.eq tryok#205
  tryerr#206:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#174
  tryok#205:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Map_String_String
  matchcont#210:
    arm64.movImm x0, 0
    arm64.b __il_cont#174
  tryerr#187:
    arm64.movRegReg x0, x9
  __il_cont#174:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#225
  tryerr#166:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#163
  __il_body#225:
    arm64.loadRegSlot x0, slot37
    arm64.cbz x0, matcharm#230
  matcharm#226:
    arm64.loadRegSlot x0, slot33
    arm64.sub x19, x0, 1
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
  __il_body#412:
    arm64.movRegReg x0, x19
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#413
  tryerr#414:
    arm64.movImm x20, 0
    arm64.cbz x1, __il_body#433
  tryerr#237:
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
    arm64.b __il_cont#234
  tryok#413:
    arm64.movRegReg x20, x0
  __il_body#433:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#440:
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
  __il_cont#439:
    arm64.movImm x22, 0
  whilehdr#434:
    arm64.cmp x22, x21
    arm64.movImm x0, 0
    arm64.b.hs scmerge#436
  andrhs#435:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#442:
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#444
  __il_cont#441:
    arm64.cmp x0, 47
    arm64.cset x0, eq
  scmerge#436:
    arm64.cbz x0, __il_cont#432
  leadingSeparators:
    arm64.add x22, x22, 1
    arm64.b whilehdr#434
  __il_cont#432:
    arm64.cmp x22, 0
    arm64.movImm x21, 1
    arm64.b.hi critsplit#480
  __il_body#260:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#418:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont#417:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#262
  empty:
    arm64.movImm x21, 0
    arm64.b __il_cont#259
  ifcont#262:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#420:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.b whilehdr#263
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
    arm64.b.lt __rc_panic#269
  __rc_ok#268:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#422:
    arm64.movRegReg x1, x21
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#424
  __il_cont#421:
    arm64.cmp x0, 47
    arm64.b.ne ifcont#267
  isSep:
    arm64.add x21, x21, 1
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic#271
    arm64.b __il_cont#259
  ifcont#267:
    arm64.movRegReg x0, x21
  whilehdr#263:
    arm64.cmp x0, 0
    arm64.b.hi scan
  whileexit#265:
    arm64.movImm x21, 0
  __il_cont#259:
    arm64.cmp x21, 0
    arm64.b.ne ifcont#255
  noSep:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
    arm64.b __il_cont#252
  ifcont#255:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#416:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
  __il_cont#415:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl String.addressableBytes
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __managed_slice
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#256
  tryerr#257:
    arm64.leaRdata x0, __str_blob_124  ; "panic at FilePath.maxon:354: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryok#256:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movRegReg x0, x21
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitterminal#258:
    arm64.movRegReg x21, x22
  __il_cont#252:
    arm64.loadRegBaseDisp.word64 x1, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __str_eq
    arm64.eor x22, x0, 1
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.b scmerge#239
  critsplit#480:
    arm64.movRegReg x22, x21
  scmerge#239:
    arm64.cbz x22, ternaryfalse
  ternarytrue:
    arm64.loadRegSlot x1, slot29
    arm64.movRegReg x0, x20
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x21, x0
    arm64.b matchcont#242
  ternaryfalse:
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
  matchcont#242:
    arm64.leaRdata x23, __str_rec_117  ; "/"
    arm64.cbz x22, ifcont#245
  isAPath:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x21
    arm64.b exitterminal#244
  ifcont#245:
    arm64.movRegReg x0, x21
    arm64.bl String.cstr
    arm64.bl __gt_subp_resolve_path
    arm64.bl String.init
    arm64.movRegReg x22, x0
  __il_body#452:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 8]
  __il_body#448:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x22
    arm64.movRegReg x3, x23
    arm64.bl findIn
    arm64.cmp x9, 0
    arm64.b.eq tryok#449
  tryerr#450:
    arm64.movImm x0, 0
    arm64.b __il_cont#445
  tryok#449:
    arm64.movImm x0, 1
  __il_cont#445:
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#250
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
  exitdrop#248:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#249:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
  exitterminal#247:
    arm64.movImm x20, 0
    arm64.movRegReg x21, x20
    arm64.movRegReg x20, x23
    arm64.b __il_cont#234
  ifcont#250:
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#251:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
  exitterminal#244:
    arm64.movImm x20, 0
    arm64.movRegReg x21, x0
  __il_cont#234:
    arm64.cmp x20, 0
    arm64.b.eq tryok#227
  tryerr#228:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
  exitterminal#229:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.b __il_cont#224
  tryok#227:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movRegReg x0, x21
    arm64.b matchcont#233
  matcharm#230:
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
  matchcont#233:
    arm64.movImm x1, 0
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x1
  __il_cont#224:
    arm64.storeSlotReg slot26, x19
    arm64.cmp x0, 0
    arm64.b.eq __il_body#212
  tryerr#168:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#163
  __il_body#212:
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
    arm64.b forhdr#213
  arg:
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#221
  __im_load#222:
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
    arm64.cbz x0, critsplit#472
  __im_empty:
    arm64.cmp x1, 0
    arm64.b.eq __im_slow#221
  critsplit#473:
    arm64.movRegReg x9, x2
    arm64.b __im_cont#220
  critsplit#472:
    arm64.movRegReg x9, x2
  __im_cont#220:
    arm64.cmp x9, 0
    arm64.b.ne tryerr#218
  tryok#217:
    arm64.movRegReg x0, x20
    arm64.bl appendNulTerminated
  forstep#215:
    arm64.add x21, x21, 1
  forhdr#213:
    arm64.cmp x21, x19
    arm64.b.lo arg
  forexit#216:
    arm64.movRegReg x0, x20
    arm64.bl String.from
    arm64.storeSlotReg slot30, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#211:
    arm64.loadRegSlot x0, slot25
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.add x0, x0, 1
    arm64.storeSlotReg slot22, x0
  __il_body#273:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.loadRegSlot x0, slot42
    arm64.and x0, x0, 7
    arm64.cmp x0, 1
    arm64.b.ne matchnext#285
  __il_body#295:
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
  __il_cont#294:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x21
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.b exitterminal#275
  matchnext#285:
    arm64.cmp x0, 2
    arm64.b.ne matcharm#281
  matcharm#276:
    arm64.loadRegSlot x0, slot42
    arm64.sub x20, x0, 2
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
  __il_body#298:
    arm64.movRegReg x0, x20
    arm64.bl namesByKey
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#300
  __il_body#319:
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x23, 0
  eachEntry:
    arm64.movRegReg x0, x23
    arm64.bl __gt_subp_env_entry
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x0, 0
    arm64.b.ne ifcont#324
  pastTheEnd:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  whileexit#322:
    arm64.movImm x0, 0
    arm64.b __il_cont#318
  ifcont#324:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x0, 1
    arm64.b.ne ifcont#332
  oneByte:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x24
    arm64.bl __managed_byte_at
    arm64.cmp x9, 0
    arm64.b.ne tryerr#327
  tryok#326:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#332
  hostFailed:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#330:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitterminal#329:
    arm64.movImm x0, 3
    arm64.movImm x22, 0
  __il_cont#318:
    arm64.cmp x0, 0
    arm64.b.eq tryok#301
  tryerr#302:
    arm64.leaRdata x22, __str_rec_210  ; "this process's environment could not be read, so no child can inherit it"
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
    arm64.b __il_cont#297
  tryok#301:
    arm64.movImm x0, 0
    arm64.b forhdr#304
  eachInherited:
    arm64.loadRegBaseDisp.word64 x1, [x22 + 0]
    arm64.loadRegBaseIndexScale.word64 x23, [x1 + x0*8 + 0]
  __im_cont#317:
    arm64.cmp x23, 0
    arm64.b.eq emptyslot
  slotfilled:
    arm64.movRegReg x0, x23
    arm64.bl __str_retain
    arm64.storeSlotReg slot39, x0
  __il_body#364:
    arm64.movRegReg x0, x23
    arm64.bl String.toByteArray
    arm64.movRegReg x24, x0
    arm64.movImm x25, 0
  __il_body#368:
    arm64.loadRegBaseDisp.word64 x26, [x24 + 8]
    arm64.movImm x1, 1
  __us_test:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.b forhdr#369
  eachByte#370:
    arm64.cmp x1, x26
    arm64.b.hs __im_slow#378
  __im_load#379:
    arm64.add x2, x0, x1
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  tryok#373:
    arm64.cmp x2, 61
    arm64.b.eq atSeparator
  forstep#371:
    arm64.add x1, x1, 1
  forhdr#369:
    arm64.cmp x1, x26
    arm64.b.lo eachByte#370
    arm64.b __il_body#380
  atSeparator:
    arm64.movRegReg x26, x1
  __il_body#380:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x27, x0
    arm64.movRegReg x1, x25
    arm64.b forhdr#381
  eachByte#382:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#389
  __im_load#390:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
  tryok#385:
    arm64.loadRegBaseDisp.word64 x0, [x27 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x27 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#392
  __im_bounds#393:
    arm64.loadRegBaseDisp.word64 x3, [x27 + 16]
    arm64.cmp x0, x3
    arm64.b.hs __im_slow#392
  __im_buffer#394:
    arm64.loadRegBaseDisp.word64 x3, [x27 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#392
  __im_viewed#395:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#392
  __im_store#396:
    arm64.add x3, x3, x0
    arm64.storeBaseDispReg.byte [x3 + 0], x2
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
  forstep#383:
    arm64.add x1, x1, 1
  forhdr#381:
    arm64.cmp x1, x26
    arm64.b.lo eachByte#382
  forexit#384:
    arm64.movRegReg x0, x27
    arm64.bl String.from
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x27
    arm64.bl __managed_decref
  __il_cont#366:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  __il_cont#363:
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
    arm64.storeSlotReg slot44, x2
    arm64.cmp x9, 0
    arm64.b.ne ifelse
  overridden:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl appendEnvEntry
    arm64.leaRdata x23, __witness_String.Hashable
    arm64.leaRdata x25, __witness_String.Equatable
    arm64.leaRdata x0, __layout_Map_String_String
    arm64.storeSlotReg slot50, x0
  __il_body#456:
    arm64.movImm x26, 5381
  __il_body#464:
    arm64.loadRegBaseDisp.word64 x27, [x24 + 8]
  __il_cont#463:
    arm64.movImm x28, 0
    arm64.b forhdr#457
  hash_loop:
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x28
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#462
  tryok#461:
    arm64.movImm x16, 33
    arm64.mul x1, x26, x16
    arm64.add x0, x1, x0
  forstep#459:
    arm64.add x28, x28, 1
    arm64.movRegReg x26, x0
  forhdr#457:
    arm64.cmp x28, x27
    arm64.b.lo hash_loop
  forexit#460:
    arm64.movImm x0, 4294967295
    arm64.and x2, x26, x0
  __il_cont#453:
    arm64.loadRegSlot x5, slot50
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x24
    arm64.movRegReg x3, x23
    arm64.movRegReg x4, x25
    arm64.bl Map.findSlot
    arm64.movRegReg x26, x0
    arm64.cmp x9, 0
    arm64.b.ne __il_cont#339
  tryok#341:
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x23
    arm64.movRegReg x3, x25
    arm64.bl Map.slotHoldsKey
    arm64.cbz x0, __il_cont#339
  found:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.movRegReg x1, x26
    arm64.bl Array.clearSlot
    arm64.cmp x9, 0
    arm64.b.ne tryerr#345
  tryok#344:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.movRegReg x1, x26
    arm64.bl Array.clearSlot
    arm64.cmp x9, 0
    arm64.b.ne tryerr#347
  tryok#346:
    arm64.movImm x0, 2
    arm64.loadRegBaseDisp.word64 x1, [x21 + 16]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.cmp x26, x2
    arm64.b.hs __im_slow#356
  __im_buffer#357:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x3, [x1 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach
  __im_viewed#361:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach
  critsplit#475:
    arm64.movRegReg x1, x2
  __im_detached:
    arm64.add x1, x1, x26
    arm64.storeBaseDispReg.byte [x1 + 0], x0
  tryok#348:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 32]
    arm64.sub x0, x0, 1
    arm64.movImm x1, 9223372036854775808
    arm64.cmp x0, x1
    arm64.b.hi __rc_panic#352
  __rc_ok#351:
    arm64.storeBaseDispReg.word64 [x21 + 32], x0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 48]
    arm64.add x0, x0, 1
    arm64.movImm x1, 9223372036854775808
    arm64.cmp x0, x1
    arm64.b.hi __rc_panic#354
  __rc_ok#353:
    arm64.storeBaseDispReg.word64 [x21 + 48], x0
  __il_cont#339:
    arm64.loadRegSlot x0, slot44
    arm64.bl __str_decref
    arm64.b ifcont#312
  ifelse:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl appendNulTerminated
  ifcont#312:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot39
    arm64.bl __str_decref
  forstep#306:
    arm64.loadRegSlot x0, slot35
    arm64.add x0, x0, 1
  forhdr#304:
    arm64.storeSlotReg slot35, x0
    arm64.loadRegBaseDisp.word64 x1, [x22 + 8]
    arm64.cmp x0, x1
    arm64.b.lt eachInherited
  forexit#307:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl appendEnvEntries
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#314:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Map_String_String
  exitterminal#313:
    arm64.movImm x21, 0
    arm64.movRegReg x23, x21
    arm64.b __il_cont#297
  tryerr#300:
    arm64.movRegReg x23, x9
  __il_cont#297:
    arm64.cmp x23, 0
    arm64.b.eq tryok#277
  tryerr#278:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.b exitdrop#280
  tryok#277:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.b matchcont#287
  ifcont#332:
    arm64.movRegReg x0, x24
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x1, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#334
  __im_bounds#335:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#334
  __im_buffer#336:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#334
  __im_viewed#337:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#334
  __im_store#338:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
  __im_cont#333:
    arm64.add x23, x23, 1
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
    arm64.b eachEntry
  matcharm#281:
    arm64.loadRegSlot x0, slot42
    arm64.sub x20, x0, 3
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
    arm64.movRegReg x0, x20
    arm64.bl namesByKey
    arm64.movRegReg x21, x0
    arm64.movRegReg x22, x9
    arm64.cmp x22, 0
    arm64.b.eq tryok#282
  tryerr#283:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x23, x22
  exitdrop#280:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal#279:
    arm64.movImm x19, 0
    arm64.b __il_cont#272
  tryok#282:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl appendEnvEntries
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Map_String_String
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Map_String_String
  matchcont#287:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#289
  __im_bounds#290:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#289
  __im_buffer#291:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#289
  __im_viewed#292:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#289
  __im_store#293:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
  __im_cont#288:
    arm64.movRegReg x0, x19
    arm64.bl String.from
    arm64.movImm x1, 0
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
  exitterminal#275:
    arm64.movImm x19, 0
    arm64.movRegReg x23, x19
    arm64.movRegReg x19, x0
  __il_cont#272:
    arm64.cmp x23, 0
    arm64.b.eq tryok#169
  tryerr#170:
    arm64.loadRegSlot x0, slot30
    arm64.bl __str_decref
  exitdrop#172:
    arm64.loadRegSlot x0, slot26
    arm64.bl __str_decref
  exitterminal#171:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x23
    arm64.b __il_cont#163
  tryok#169:
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
  exitterminal#173:
    arm64.movImm x19, 0
    arm64.xchgRegReg x19, x20
  __il_cont#163:
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
  __il_body#85:
    arm64.and x0, x20, 7
    arm64.sub x0, x0, 1
    arm64.jumpTable x0, default=matchdead [[0]=matcharm#86, [1]=matcharm#87, [2]=matcharm#88, [3]=matcharm#89, [4]=matcharm#90] @__jumptable_389
  matchdead:
  matcharm#86:
    arm64.movImm x20, 0
    arm64.leaRdata x0, __str_rec_115  ; ""
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
    arm64.b matchcont#92
  matcharm#87:
    arm64.movImm x20, 1
    arm64.leaRdata x0, __str_rec_115  ; ""
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
    arm64.b matchcont#92
  matcharm#88:
    arm64.movImm x20, 2
    arm64.leaRdata x0, __str_rec_115  ; ""
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
    arm64.b matchcont#92
  matcharm#89:
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
    arm64.b matchcont#92
  matcharm#90:
    arm64.movImm x20, 4
    arm64.leaRdata x0, __str_rec_115  ; ""
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
  matchcont#92:
    arm64.storeSlotReg slot5, x20
  __il_cont#84:
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
    arm64.storeSlotReg slot48, x23
    arm64.movRegReg x0, x24
    arm64.bl __destruct_OutputDestination
    arm64.loadRegBaseDisp.word64 x0, [x21 + 64]
  __il_body#93:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
    arm64.cbz x2, ifcont#95
  __rc_ok#98:
    arm64.movImm x1, 1
  ifcont#95:
    arm64.loadRegBaseDisp.byte x0, [x0 + 8]
    arm64.cbz x0, critsplit#468
  newGroup:
    arm64.orr x21, x1, 2
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic#102
  __rc_chk#103:
    arm64.cmp x21, 15
    arm64.b.gt __rc_panic#102
    arm64.b __il_cont#83
  critsplit#468:
    arm64.movRegReg x21, x1
  __il_cont#83:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.addressableBytes
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x25, [x19 + 16]
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x26, [x0 + 16]
    arm64.movRegReg x0, x26
    arm64.bl __mm_retain
    arm64.storeSlotReg slot18, x0
  __il_body#104:
    arm64.loadRegBaseDisp.word64 x0, [x26 + 0]
    arm64.bl __str_retain
    arm64.storeSlotReg slot15, x0
  __il_cont#82:
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
    arm64.storeSlotReg slot49, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.storeSlotReg slot43, x0
    arm64.loadRegBaseDisp.word64 x19, [x20 + 8]
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.storeSlotReg slot24, x0
    arm64.movRegReg x0, x19
    arm64.bl String.cstr
    arm64.storeSlotReg slot38, x0
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
    arm64.loadRegSlot x6, slot38
    arm64.loadRegSlot x5, slot43
    arm64.loadRegSlot x4, slot49
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.movRegReg x3, x27
    arm64.bl __gt_subp_attached_spawn
    arm64.movRegReg x19, x0
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
  __il_body#106:
    arm64.bl __gt_subp_last_error_code
    arm64.movRegReg x20, x0
    arm64.asr x0, x20, 32
    arm64.bl __enum_from_raw_SpawnPreparation
    arm64.cmp x9, 0
    arm64.b.ne tryerr#108
  tryok#107:
    arm64.movImm x1, 4294967295
    arm64.and x1, x20, x1
    arm64.cmp x1, 2
    arm64.movImm x2, 1
    arm64.b.eq critsplit#469
  orrhs:
    arm64.cmp x1, 3
    arm64.cset x1, eq
    arm64.b scmerge#110
  critsplit#469:
    arm64.movRegReg x1, x2
  scmerge#110:
    arm64.movImm x2, 0
    arm64.cbz x1, critsplit#470
  andrhs#111:
    arm64.cmp x0, 0
    arm64.cset x0, eq
    arm64.b scmerge#112
  critsplit#470:
    arm64.movRegReg x0, x2
  scmerge#112:
    arm64.cbz x0, ifcont#114
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
    arm64.b __il_cont#105
  ifcont#114:
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
  __il_cont#105:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_StdioRuntimeTriple
    arm64.b exitdrop#48
  ifcont#51:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic#76
  __rc_ok#75:
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x20, [x0 + 32]
    arm64.movRegReg x0, x20
    arm64.bl __uretain_InputSource
    arm64.movRegReg x21, x0
  __il_body#152:
    arm64.and x0, x20, 7
    arm64.cmp x0, 1
    arm64.cset x1, ge
    arm64.cmp x0, 2
    arm64.cset x2, le
    arm64.and x1, x1, x2
    arm64.cbnz x1, __il_cont#151
  matchnext#156:
    arm64.cmp x0, 3
    arm64.b.ne __il_cont#151
  matcharm#153:
    arm64.sub x20, x20, 3
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
  __il_body#160:
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movRegReg x1, x0
  __il_body#400:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
  __il_cont#399:
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_queue_stdin
    arm64.cmp x0, 0
    arm64.b.eq __il_cont#159
  refused:
    arm64.leaRdata x0, __str_rec_219  ; "panic at Subprocess.maxon:858: "
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.leaRdata x0, __str_rec_220  ; "Subprocess: the runtime refused the stdin payload of handle "
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
    arm64.leaRdata x0, __str_rec_221  ; ", whose spawn has just answered it with a pipe"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_86  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot53, x0
    arm64.add x1, x21, x23
    arm64.add x1, x1, x19
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot41, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot46, x0
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
    arm64.loadRegSlot x2, slot53
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x19, slot46
    arm64.storeBaseDispReg.word64 [x19 + 0], x28
    arm64.loadRegSlot x0, slot41
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
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __il_cont#159:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  __il_cont#151:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_InputSource
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_get_pid
    arm64.storeSlotReg slot52, x0
    arm64.loadRegSlot x0, slot8
    arm64.loadRegBaseDisp.word64 x1, [x0 + 56]
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_wait_collect
    arm64.movRegReg x20, x0
    arm64.cmp x20, -1
    arm64.b.ne ifcont#54
  waitFailed:
    arm64.bl lastErrorMessage
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_release
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  exitdrop#53:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_StdioRuntimeTriple
    arm64.movRegReg x20, x19
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
  ifcont#54:
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_kind
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_code
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_stdout
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_stderr
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_duration
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x24
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x27, x0
    arm64.movRegReg x0, x25
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x20
    arm64.bl __gt_subp_result_release
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_release
    arm64.movRegReg x0, x21
    arm64.bl __enum_from_raw_ResultStatusKind
    arm64.cmp x9, 0
    arm64.b.ne tryerr#56
  tryok#55:
    arm64.cmp x22, 0
    arm64.b.lt __rc_panic#78
  __rc_chk#79:
    arm64.movImm x1, 4294967295
    arm64.cmp x22, x1
    arm64.b.gt __rc_panic#78
  __rc_ok#124:
    arm64.cmp x0, 0
    arm64.b.ne matchnext#121
  matcharm#117:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x22
    arm64.movImm x19, 0
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x0
    arm64.b __il_cont#115
  matchnext#121:
    arm64.cmp x0, 1
    arm64.b.ne matcharm#119
  matcharm#118:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x22
    arm64.movImm x19, 0
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x0
    arm64.b __il_cont#115
  matcharm#119:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.storeBaseDispReg.word64 [x19 + 8], x26
    arm64.movRegReg x0, x27
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movRegReg x0, x28
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x20, 0
    arm64.xchgRegReg x19, x20
  __il_cont#115:
    arm64.cmp x20, 0
    arm64.b.eq tryok#57
  tryerr#58:
    arm64.movRegReg x0, x28
    arm64.bl __str_decref
  exitdrop#59:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#60:
    arm64.movRegReg x0, x25
    arm64.bl __managed_decref
  exitdrop#61:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#62:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#63:
    arm64.loadRegSlot x0, slot7
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#64:
    arm64.loadRegSlot x0, slot5
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#65:
    arm64.loadRegSlot x0, slot12
    arm64.bl __destruct_SpawnOperands
    arm64.movRegReg x0, x20
  exitterminal#38:
    arm64.movImm x1, 0
    arm64.xchgRegReg x0, x1
    arm64.b __il_cont#34
  tryok#57:
    arm64.loadRegSlot x0, slot52
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#81
  __rc_ok#80:
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.movRegReg x0, x27
    arm64.bl __str_retain
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x28
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
  __il_body#128:
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x22 + 0], x19
    arm64.movRegReg x0, x20
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 16], x0
    arm64.loadRegSlot x0, slot52
    arm64.storeBaseDispReg.word64 [x22 + 24], x0
    arm64.storeBaseDispReg.word64 [x22 + 32], x26
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#130:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  exitdrop#131:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  __il_cont#127:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  exitdrop#67:
    arm64.movRegReg x0, x28
    arm64.bl __str_decref
  exitdrop#68:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#69:
    arm64.movRegReg x0, x25
    arm64.bl __managed_decref
  exitdrop#70:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  exitdrop#71:
    arm64.loadRegSlot x0, slot48
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#72:
    arm64.loadRegSlot x0, slot7
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#73:
    arm64.loadRegSlot x0, slot5
    arm64.bl __destruct_StdioRuntimeTriple
  exitdrop#74:
    arm64.loadRegSlot x0, slot12
    arm64.bl __destruct_SpawnOperands
  exitterminal#66:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x22
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
  __il_body#398:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.storeSlotReg slot9, x22
  __il_cont#397:
    arm64.leaRdata x1, __str_rec_81  ; "hi"
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.startsWith
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_82  ; "timerpolls="
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.cmp x20, 16
    arm64.cset x20, ls
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot51, x1
    arm64.movRegReg x0, x20
    arm64.bl __bool_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_83  ; " onpoller="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot45, x1
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.cmp x21, 1
    arm64.cset x21, hs
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot40, x1
    arm64.movRegReg x0, x21
    arm64.bl __bool_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_84  ; " outLen="
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
    arm64.leaRdata x0, __str_rec_85  ; " matches="
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
    arm64.leaRdata x1, __str_rec_86  ; "\x0a"
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
    arm64.loadRegSlot x1, slot51
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x23, x20
    arm64.loadRegSlot x1, slot45
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x20, x20, x25
    arm64.loadRegSlot x1, slot40
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
    arm64.loadRegSlot x0, slot51
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot40
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
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __im_slow#430:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#428
    arm64.b tryok#427
  __im_slow#221:
    arm64.loadRegSlot x0, slot25
    arm64.movRegReg x1, x21
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.b __im_cont#220
  __im_slow#378:
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.cmp x9, 0
    arm64.b.ne tryerr#374
    arm64.b tryok#373
  __im_slow#389:
    arm64.storeSlotReg slot56, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.loadRegSlot x1, slot56
    arm64.cmp x9, 0
    arm64.b.ne tryerr#386
  critsplit#477:
    arm64.movRegReg x2, x0
    arm64.b tryok#385
  __im_slow#392:
    arm64.storeSlotReg slot56, x1
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x2
    arm64.bl __managed_push
    arm64.loadRegSlot x1, slot56
    arm64.b forstep#383
  __im_slow#356:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x26
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#349
    arm64.b tryok#348
  __im_detach:
    arm64.storeSlotReg slot56, x1
    arm64.movRegReg x0, x1
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 2
    arm64.loadRegSlot x1, slot56
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.b __im_detached
  __im_slow#334:
    arm64.movRegReg x0, x22
    arm64.bl __managed_push
    arm64.b __im_cont#333
  __im_slow#289:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.b __im_cont#288
  __rc_panic#406:
    arm64.leaRdata x0, __str_blob_272  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#444:
    arm64.leaRdata x0, __str_blob_185  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#269:
    arm64.leaRdata x0, __str_blob_278  ; "panic at FilePath.maxon:334: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#424:
    arm64.leaRdata x0, __str_blob_185  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#271:
    arm64.leaRdata x0, __str_blob_279  ; "panic at FilePath.maxon:337: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#218:
    arm64.leaRdata x0, __str_blob_198  ; "panic at Subprocess.maxon:642: buildArgvBlob: args.get(i) out of bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#327:
    arm64.leaRdata x0, __str_blob_158  ; "panic at Process.maxon:116: Process.currentEnvironmentEntries: byteAt(0) of a one-byte answer\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  emptyslot:
    arm64.leaRdata x0, __str_rec_211  ; "panic at Subprocess.maxon:769: "
  __rc_ok#315:
    arm64.loadRegSlot x1, slot35
    arm64.bl __emptySlotMessage
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#374:
    arm64.leaRdata x0, __str_rec_159  ; "panic at Process.maxon:180: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.leaRdata x0, __str_rec_160  ; "envEntrySeparatorAt: i < n = bytes.count() but get("
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x6, 0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.movRegReg x1, x6
    arm64.bl __mm_alloc
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x6
    arm64.bl __uint_to_string
    arm64.movRegReg x7, x0
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.leaRdata x0, __str_rec_161  ; ") failed"
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.leaRdata x0, __str_rec_86  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x9, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x10, [x0 + 8]
    arm64.add x0, x3, x5
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x10
    arm64.add x11, x0, 57
    arm64.movImm x12, 0
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x11
    arm64.movRegReg x1, x12
    arm64.bl __mm_alloc
    arm64.movRegReg x11, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.add x12, x11, 56
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x12
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x12, x3
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x4
    arm64.movRegReg x2, x5
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x2, x5
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x6
    arm64.movRegReg x2, x7
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x2, x7
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x1, x2, x8
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x9
    arm64.movRegReg x2, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.storeBaseDispReg.word64 [x11 + 0], x12
    arm64.storeBaseDispReg.word64 [x11 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x11 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x11 + 24], x0
    arm64.storeSlotReg slot66, x11
    arm64.movRegReg x0, x6
    arm64.bl __mm_decref
    arm64.loadRegSlot x11, slot66
    arm64.loadRegBaseDisp.word64 x0, [x11 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#386:
    arm64.leaRdata x0, __str_rec_162  ; "panic at Process.maxon:194: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.leaRdata x0, __str_rec_163  ; "envEntryBytes: i < to <= bytes.count() but get("
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x6, 0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.movRegReg x1, x6
    arm64.bl __mm_alloc
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x6
    arm64.bl __uint_to_string
    arm64.movRegReg x7, x0
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.leaRdata x0, __str_rec_161  ; ") failed"
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.leaRdata x0, __str_rec_86  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x9, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x10, [x0 + 8]
    arm64.add x0, x3, x5
    arm64.add x0, x0, x7
    arm64.add x0, x0, x8
    arm64.add x0, x0, x10
    arm64.add x11, x0, 57
    arm64.movImm x12, 0
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x11
    arm64.movRegReg x1, x12
    arm64.bl __mm_alloc
    arm64.movRegReg x11, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.add x12, x11, 56
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x12
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x12, x3
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x4
    arm64.movRegReg x2, x5
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x2, x5
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x6
    arm64.movRegReg x2, x7
    arm64.bl __str_copy
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x2, x2, x7
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x2
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.add x1, x2, x8
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot66, x11
    arm64.storeSlotReg slot67, x12
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x9
    arm64.movRegReg x2, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x11, slot66
    arm64.loadRegSlot x12, slot67
    arm64.storeBaseDispReg.word64 [x11 + 0], x12
    arm64.storeBaseDispReg.word64 [x11 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x11 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x11 + 24], x0
    arm64.storeSlotReg slot66, x11
    arm64.movRegReg x0, x6
    arm64.bl __mm_decref
    arm64.loadRegSlot x11, slot66
    arm64.loadRegBaseDisp.word64 x0, [x11 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#462:
    arm64.leaRdata x0, __str_blob_257  ; "panic at hash.maxon:24: hashString: byteAt OOB \xe2\x80\x94 i < s.byteLength() invariant\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#345:
    arm64.leaRdata x0, __str_blob_143  ; "panic at Map.maxon:209: Map.remove: keys.clearSlot OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#347:
    arm64.leaRdata x0, __str_blob_144  ; "panic at Map.maxon:210: Map.remove: values.clearSlot OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#349:
    arm64.leaRdata x0, __str_blob_145  ; "panic at Map.maxon:211: Map.remove: states.set OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __im_missing:
    arm64.leaRdata x0, __str_blob_387  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#352:
    arm64.leaRdata x0, __str_blob_289  ; "panic at Map.maxon:212: Range check failed: value outside typealias 'EntryCount'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#354:
    arm64.leaRdata x0, __str_blob_290  ; "panic at Map.maxon:213: Range check failed: value outside typealias 'TableCapacity'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#102:
    arm64.leaRdata x0, __str_blob_339  ; "panic at Subprocess.maxon:881: Range check failed: value outside typealias 'SpawnFlagsValue'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#108:
    arm64.leaRdata x0, __str_blob_222  ; "panic at Subprocess.maxon:908: Subprocess.spawnFailure: the runtime recorded a spawn step this library does not know\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#76:
    arm64.leaRdata x0, __str_blob_332  ; "panic at Subprocess.maxon:497: Range check failed: value outside typealias 'StreamingHandle'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#56:
    arm64.leaRdata x0, __str_rec_196  ; "panic at Subprocess.maxon:519: "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.leaRdata x0, __str_rec_197  ; "Subprocess.runConfiguration: runtime returned unexpected statusKind raw="
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x5, 0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.movRegReg x1, x5
    arm64.bl __mm_alloc
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x5
    arm64.bl __int_to_string
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.leaRdata x0, __str_rec_86  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x7, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.add x0, x2, x4
    arm64.add x0, x0, x6
    arm64.add x0, x0, x8
    arm64.add x9, x0, 57
    arm64.movImm x10, 0
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.movRegReg x0, x9
    arm64.movRegReg x1, x10
    arm64.bl __mm_alloc
    arm64.movRegReg x9, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.add x10, x9, 56
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot57, x2
    arm64.storeSlotReg slot58, x3
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x10
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x2, slot57
    arm64.loadRegSlot x3, slot58
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.add x1, x10, x2
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot59, x4
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x4
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x4, slot59
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.add x1, x1, x4
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot56, x1
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot61, x6
    arm64.storeSlotReg slot62, x7
    arm64.storeSlotReg slot63, x8
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x5
    arm64.movRegReg x2, x6
    arm64.bl __str_copy
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x1, slot56
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x6, slot61
    arm64.loadRegSlot x7, slot62
    arm64.loadRegSlot x8, slot63
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.add x1, x1, x6
    arm64.storeSlotReg slot55, x0
    arm64.storeSlotReg slot60, x5
    arm64.storeSlotReg slot64, x9
    arm64.storeSlotReg slot65, x10
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x7
    arm64.movRegReg x2, x8
    arm64.bl __str_copy
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot55
    arm64.loadRegSlot x5, slot60
    arm64.loadRegSlot x9, slot64
    arm64.loadRegSlot x10, slot65
    arm64.storeBaseDispReg.word64 [x9 + 0], x10
    arm64.storeBaseDispReg.word64 [x9 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x9 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x9 + 24], x0
    arm64.storeSlotReg slot64, x9
    arm64.movRegReg x0, x5
    arm64.bl __mm_decref
    arm64.loadRegSlot x9, slot64
    arm64.loadRegBaseDisp.word64 x0, [x9 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#78:
    arm64.leaRdata x0, __str_blob_333  ; "panic at Subprocess.maxon:520: Range check failed: value outside typealias 'ExitInt'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#81:
    arm64.leaRdata x0, __str_blob_334  ; "panic at Subprocess.maxon:521: Range check failed: value outside typealias 'Pid'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  tryerr#2:
    arm64.leaRdata x0, __str_blob_87  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:11: /bin/sh runs on every POSIX lane\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
    arm64.retIndirect
  __rc_panic#8:
    arm64.leaRdata x0, __str_blob_269  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:18: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot68
    arm64.loadRegSlot x20, slot69
    arm64.loadRegSlot x21, slot70
    arm64.loadRegSlot x22, slot71
    arm64.loadRegSlot x23, slot72
    arm64.loadRegSlot x24, slot73
    arm64.loadRegSlot x25, slot74
    arm64.loadRegSlot x26, slot75
    arm64.loadRegSlot x27, slot76
    arm64.loadRegSlot x28, slot77
    arm64.epilogue 688 record@48
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
  npholdnotended:
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
