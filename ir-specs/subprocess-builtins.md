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
	var argv = StringArray.create()
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
  __sched_num_procs@448 = i64 0
  __sched_ncpu@456 = i64 0
  __sched_shutdown_flag@464 = i64 0
  __sched_lock@472 = i64 0
  __sched_lock_owner@480 = i64 0
  __sched_lock_depth@488 = i64 0
  __sched_lock_depth_sink@496 = i64 0
  __sched_nmspinning@504 = i64 0
  __sched_needspinning@512 = i64 0
  __sched_sysmon_event@520 = i64 0
  __sched_sysmon_wait@528 = i64 0
  __sched_timer_starts@536 = i64 0
  __mrt_envp@544 = i64 0
  __mrt_signal_stack_bytes@552 = i64 0
  __mrt_tls_next_key@560 = i64 0
  __io_pipe_seq@568 = i64 0
  __subp_table@576 = i64 0
  __subp_last_error@584 = i64 0
  __slab_arena_list@592 = i64 0
  __slab_arena_map_l1@600 = i64 0
  __slab_state@608 = i64 0
  __mrt_program_started@616 = i8 0
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
    x64.leaRegRdata rcx, [rip + __str_rec_76]  ; "-c"
    x64.callDirect __str_retain
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.leaRegRdata rcx, [rip + __str_rec_77]  ; "sleep 1; echo hi"
    x64.callDirect __str_retain
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.callDirect Scheduler.counters
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.leaRegRdata r13, [rip + __str_rec_78]  ; "/bin/sh"
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r14 + 0], rax
    x64.movRegReg rcx, r13
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r14 + 8], r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, rbx
    x64.callDirect Subprocess.run
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot2, rbx
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.movRegReg rcx, r14
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
  __il_body#13:
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.storeSlotReg slot4, r12
  __il_cont#12:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.leaRegRdata rdx, [rip + __str_rec_79]  ; "hi"
    x64.callDirect String.startsWith
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_80]  ; "timerpolls="
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
    x64.leaRegRdata rax, [rip + __str_rec_81]  ; " onpoller="
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
    x64.leaRegRdata rax, [rip + __str_rec_82]  ; " outLen="
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
    x64.leaRegRdata rax, [rip + __str_rec_83]  ; " matches="
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
    x64.leaRegRdata rax, [rip + __str_rec_84]  ; "\x0a"
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
  __il_body#11:
    x64.loadRegSlot rcx, slot17
    x64.callDirect __write_stdout
  __il_cont#10:
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
    x64.leaRegRdata rcx, [rip + __str_blob_85]  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:11: /bin/sh runs on every POSIX lane\x0a"
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
    x64.leaRegRdata rcx, [rip + __str_blob_264]  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:18: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    x64.prologue 56
    x64.movRegReg rbx, rcx
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rdx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rdx, 0
    x64.setccReg notEqual, rsi
    x64.andRegReg rax, rax, rsi
    x64.movRegImm32 rsi, 0
    x64.subRegReg rsi, rsi, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rdx, rdx, 656
    x64.leaRegGlobal rdi, __sched_lock_depth_sink
    x64.andRegReg rdx, rdx, rsi
    x64.andRegReg rdi, rdi, rax
    x64.orRegReg rdx, rdx, rdi
    x64.aluBaseDispImm32.add [rdx + 0], 1
    x64.movRegImm32 rax, 4088
    x64.mov rax, gs:[rax]
    x64.movRegImm32 rdx, 1
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __rl_slow#37
  __rl_owned#36:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
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
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __rl_nested#47
  __rl_outermost#48:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rcx + 8], rax
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc equal, __rl_cont#46
    x64.jmp __rl_slow#49
  __rl_nested#47:
    x64.leaRegRegImm32 rax, rax, -1
    x64.storeBaseDispReg.word64 [rcx + 16], rax
  __rl_cont#46:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 656
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegImm32 rcx, 10
    x64.movRegReg rdx, rbx
    x64.callDirect __gt_park
  npgaresumed:
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rdx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rdx, 0
    x64.setccReg notEqual, rsi
    x64.andRegReg rax, rax, rsi
    x64.movRegImm32 rsi, 0
    x64.subRegReg rsi, rsi, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rdx, rdx, 656
    x64.leaRegGlobal rdi, __sched_lock_depth_sink
    x64.andRegReg rdx, rdx, rsi
    x64.andRegReg rdi, rdi, rax
    x64.orRegReg rdx, rdx, rdi
    x64.aluBaseDispImm32.add [rdx + 0], 1
    x64.movRegImm32 rax, 4088
    x64.mov rax, gs:[rax]
    x64.movRegImm32 rdx, 1
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __rl_slow#52
  __rl_owned#51:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __rl_cont#50:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 r13, 0
  npgaclearhdr:
    x64.cmpRegReg r13, r12
    x64.jcc less, npgaclearbody
  npgadone:
    x64.loadRegBaseDisp.word64 r8, [rbx + 16]
    x64.leaRegGlobal rcx, __sched_lock
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __rl_nested#54
  __rl_outermost#55:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rcx + 8], rax
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc equal, __rl_cont#53
    x64.jmp __rl_slow#56
  __rl_nested#54:
    x64.leaRegRegImm32 rax, rax, -1
    x64.storeBaseDispReg.word64 [rcx + 16], rax
  __rl_cont#53:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 656
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.epilogue 56
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
    x64.epilogue 56
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
    x64.epilogue 56
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
    x64.epilogue 56
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
    x64.epilogue 56
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
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __rl_nested#43
  __rl_outermost#44:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rcx + 8], rax
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc equal, __rl_cont#42
    x64.jmp __rl_slow#45
  __rl_nested#43:
    x64.leaRegRegImm32 rax, rax, -1
    x64.storeBaseDispReg.word64 [rcx + 16], rax
  __rl_cont#42:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 656
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegImm32 r8, 1
    x64.movRegReg rcx, r13
    x64.shlRegCl r8, r8
    x64.epilogue 56
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
    x64.epilogue 56
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
    x64.epilogue 56
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
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __rl_nested#39
  __rl_outermost#40:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rcx + 8], rax
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc equal, __rl_cont#38
    x64.jmp __rl_slow#41
  __rl_nested#39:
    x64.leaRegRegImm32 rax, rax, -1
    x64.storeBaseDispReg.word64 [rcx + 16], rax
  __rl_cont#38:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 656
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegImm32 r8, 0
    x64.epilogue 56
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
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rl_slow#37:
    x64.callDirect __rtlock_enter_slow
    x64.jmp __rl_cont#35
  __rl_slow#49:
    x64.callDirect __rtlock_release
    x64.jmp __rl_cont#46
  __rl_slow#52:
    x64.callDirect __rtlock_enter_slow
    x64.jmp __rl_cont#50
  __rl_slow#56:
    x64.storeSlotReg slot2, r8
    x64.callDirect __rtlock_release
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot2
    x64.jmp __rl_cont#53
  __rl_slow#45:
    x64.callDirect __rtlock_release
    x64.jmp __rl_cont#42
  __rl_slow#41:
    x64.callDirect __rtlock_release
    x64.jmp __rl_cont#38
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
  __sched_num_procs@448 = i64 0
  __sched_ncpu@456 = i64 0
  __sched_shutdown_flag@464 = i64 0
  __sched_lock@472 = i64 0
  __sched_lock_owner@480 = i64 0
  __sched_lock_depth@488 = i64 0
  __sched_lock_depth_sink@496 = i64 0
  __sched_nmspinning@504 = i64 0
  __sched_needspinning@512 = i64 0
  __sched_sysmon_event@520 = i64 0
  __sched_sysmon_wait@528 = i64 0
  __sched_timer_starts@536 = i64 0
  __io_pipe_seq@544 = i64 0
  __subp_table@552 = i64 0
  __subp_last_error@560 = i64 0
  __slab_arena_list@568 = i64 0
  __slab_arena_map_l1@576 = i64 0
  __slab_state@584 = i64 0
  __mrt_program_started@592 = i8 0
}

func @main {
  entry:
    arm64.prologue 224
    arm64.storeSlotReg slot25, x28
    arm64.storeSlotReg slot24, x27
    arm64.storeSlotReg slot23, x26
    arm64.storeSlotReg slot22, x25
    arm64.storeSlotReg slot21, x24
    arm64.storeSlotReg slot20, x23
    arm64.storeSlotReg slot19, x22
    arm64.storeSlotReg slot18, x21
    arm64.storeSlotReg slot17, x20
    arm64.storeSlotReg slot16, x19
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_76  ; "-c"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_77  ; "sleep 1; echo hi"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.bl Scheduler.counters
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot1, x20
    arm64.leaRdata x21, __str_rec_78  ; "/bin/sh"
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x19
    arm64.bl Subprocess.run
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot2, x19
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_Executable
    arm64.bl Scheduler.counters
    arm64.storeSlotReg slot3, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 64]
    arm64.loadRegBaseDisp.word64 x2, [x20 + 64]
    arm64.sub x21, x1, x2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 72]
    arm64.loadRegBaseDisp.word64 x1, [x20 + 72]
    arm64.sub x20, x0, x1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
  __il_body#13:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.storeSlotReg slot4, x22
  __il_cont#12:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.leaRdata x1, __str_rec_79  ; "hi"
    arm64.bl String.startsWith
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_80  ; "timerpolls="
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.cmp x21, 16
    arm64.cset x21, ls
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot15, x1
    arm64.movRegReg x0, x21
    arm64.bl __bool_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_81  ; " onpoller="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot14, x1
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.cmp x20, 1
    arm64.cset x20, hs
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot13, x1
    arm64.movRegReg x0, x20
    arm64.bl __bool_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_82  ; " outLen="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot12, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot11, x1
    arm64.movRegReg x0, x22
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_83  ; " matches="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot10, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __bool_to_string
    arm64.storeSlotReg slot9, x0
    arm64.leaRdata x1, __str_rec_84  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot8, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot7, x1
    arm64.add x2, x24, x21
    arm64.add x2, x2, x25
    arm64.add x2, x2, x20
    arm64.add x2, x2, x26
    arm64.add x2, x2, x22
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot5, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot6, x0
    arm64.add x19, x0, 56
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x23, x19, x24
    arm64.loadRegSlot x1, slot15
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x21, x23, x21
    arm64.loadRegSlot x1, slot14
    arm64.movRegReg x0, x21
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x21, x21, x25
    arm64.loadRegSlot x1, slot13
    arm64.movRegReg x0, x21
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x21, x20
    arm64.loadRegSlot x1, slot12
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x20, x20, x26
    arm64.loadRegSlot x1, slot11
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x20, x20, x22
    arm64.loadRegSlot x1, slot10
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x20, x20, x27
    arm64.loadRegSlot x2, slot9
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot9
    arm64.add x20, x20, x0
    arm64.loadRegSlot x2, slot7
    arm64.loadRegSlot x1, slot8
    arm64.movRegReg x0, x20
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot7
    arm64.add x0, x20, x0
    arm64.loadRegSlot x0, slot6
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.loadRegSlot x1, slot5
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.loadRegSlot x0, slot15
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot13
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot11
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
  __il_body#11:
    arm64.loadRegSlot x0, slot6
    arm64.bl __write_stdout
  __il_cont#10:
    arm64.loadRegSlot x0, slot6
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot4
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.loadRegSlot x0, slot4
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegSlot x0, slot3
    arm64.bl __mm_decref
  exitdrop#4:
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_CollectedOutput
  exitdrop#5:
    arm64.loadRegSlot x0, slot1
    arm64.bl __mm_decref
  exitdrop#6:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.loadRegSlot x0, slot4
    arm64.loadRegSlot x19, slot16
    arm64.loadRegSlot x20, slot17
    arm64.loadRegSlot x21, slot18
    arm64.loadRegSlot x22, slot19
    arm64.loadRegSlot x23, slot20
    arm64.loadRegSlot x24, slot21
    arm64.loadRegSlot x25, slot22
    arm64.loadRegSlot x26, slot23
    arm64.loadRegSlot x27, slot24
    arm64.loadRegSlot x28, slot25
    arm64.epilogue 224
    arm64.retIndirect
  tryerr:
    arm64.leaRdata x0, __str_blob_85  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:11: /bin/sh runs on every POSIX lane\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot16
    arm64.loadRegSlot x20, slot17
    arm64.loadRegSlot x21, slot18
    arm64.loadRegSlot x22, slot19
    arm64.loadRegSlot x23, slot20
    arm64.loadRegSlot x24, slot21
    arm64.loadRegSlot x25, slot22
    arm64.loadRegSlot x26, slot23
    arm64.loadRegSlot x27, slot24
    arm64.loadRegSlot x28, slot25
    arm64.epilogue 224
    arm64.retIndirect
  __rc_panic:
    arm64.leaRdata x0, __str_blob_264  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:18: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot16
    arm64.loadRegSlot x20, slot17
    arm64.loadRegSlot x21, slot18
    arm64.loadRegSlot x22, slot19
    arm64.loadRegSlot x23, slot20
    arm64.loadRegSlot x24, slot21
    arm64.loadRegSlot x25, slot22
    arm64.loadRegSlot x26, slot23
    arm64.loadRegSlot x27, slot24
    arm64.loadRegSlot x28, slot25
    arm64.epilogue 224
    arm64.retIndirect
}

func @__np_pd_wait_any {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movRegReg x19, x0
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, 0
    arm64.cset x1, ne
    arm64.cmp x2, 0
    arm64.cset x3, ne
    arm64.and x1, x1, x3
    arm64.movImm x3, 0
    arm64.sub x3, x3, x1
    arm64.sub x1, x1, 1
    arm64.add x2, x2, 656
    arm64.leaGlobal x4, __sched_lock_depth_sink
    arm64.and x2, x2, x3
    arm64.and x1, x4, x1
    arm64.orr x1, x2, x1
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.arm64ThreadPointer x1, darwinMaskedReadOnly
    arm64.movImm x2, 1
    arm64.arm64AtomicRmw.exchangeByte x2, [x0], x2
    arm64.cmp x2, 0
    arm64.b.ne __rl_slow#37
  __rl_owned#36:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
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
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __rl_nested#47
  __rl_outermost#48:
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 0
    arm64.arm64AtomicRmw.exchangeByte x1, [x0], x1
    arm64.cmp x1, 1
    arm64.b.eq __rl_cont#46
    arm64.b __rl_slow#49
  __rl_nested#47:
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
  __rl_cont#46:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 656
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x0, 10
    arm64.movRegReg x1, x19
    arm64.bl __gt_park
  npgaresumed:
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, 0
    arm64.cset x1, ne
    arm64.cmp x2, 0
    arm64.cset x3, ne
    arm64.and x1, x1, x3
    arm64.movImm x3, 0
    arm64.sub x3, x3, x1
    arm64.sub x1, x1, 1
    arm64.add x2, x2, 656
    arm64.leaGlobal x4, __sched_lock_depth_sink
    arm64.and x2, x2, x3
    arm64.and x1, x4, x1
    arm64.orr x1, x2, x1
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.arm64ThreadPointer x1, darwinMaskedReadOnly
    arm64.movImm x2, 1
    arm64.arm64AtomicRmw.exchangeByte x2, [x0], x2
    arm64.cmp x2, 0
    arm64.b.ne __rl_slow#52
  __rl_owned#51:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  __rl_cont#50:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x21, 0
  npgaclearhdr:
    arm64.cmp x21, x20
    arm64.b.lt npgaclearbody
  npgadone:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.leaGlobal x1, __sched_lock
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.cmp x2, 0
    arm64.b.ne __rl_nested#54
  __rl_outermost#55:
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.movImm x2, 0
    arm64.arm64AtomicRmw.exchangeByte x2, [x1], x2
    arm64.cmp x2, 1
    arm64.b.eq __rl_cont#53
    arm64.b __rl_slow#56
  __rl_nested#54:
    arm64.add x2, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
  __rl_cont#53:
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, 0
    arm64.cset x1, ne
    arm64.cmp x2, 0
    arm64.cset x3, ne
    arm64.and x1, x1, x3
    arm64.movImm x3, 0
    arm64.sub x3, x3, x1
    arm64.sub x1, x1, 1
    arm64.add x2, x2, 656
    arm64.leaGlobal x4, __sched_lock_depth_sink
    arm64.and x2, x2, x3
    arm64.and x1, x4, x1
    arm64.orr x1, x2, x1
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
    arm64.retIndirect
  npholdlost:
    arm64.leaRdata x0, __abort_msg_115  ; "fatal error: runtime abort 115 (netpollHoldLost)\x0a"
    arm64.movImm x1, 49
    arm64.movImm x2, 115
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __rl_nested#43
  __rl_outermost#44:
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 0
    arm64.arm64AtomicRmw.exchangeByte x1, [x0], x1
    arm64.cmp x1, 1
    arm64.b.eq __rl_cont#42
    arm64.b __rl_slow#45
  __rl_nested#43:
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
  __rl_cont#42:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 656
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x0, 1
    arm64.lslv x0, x0, x21
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
    arm64.retIndirect
  npnopd#26:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __rl_nested#39
  __rl_outermost#40:
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 0
    arm64.arm64AtomicRmw.exchangeByte x1, [x0], x1
    arm64.cmp x1, 1
    arm64.b.eq __rl_cont#38
    arm64.b __rl_slow#41
  __rl_nested#39:
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
  __rl_cont#38:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 656
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
    arm64.retIndirect
  npnopd#25:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
    arm64.retIndirect
  __rl_slow#37:
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_enter_slow
    arm64.b __rl_cont#35
  __rl_slow#49:
    arm64.bl __rtlock_release
    arm64.b __rl_cont#46
  __rl_slow#52:
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_enter_slow
    arm64.b __rl_cont#50
  __rl_slow#56:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_release
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __rl_cont#53
  __rl_slow#45:
    arm64.bl __rtlock_release
    arm64.b __rl_cont#42
  __rl_slow#41:
    arm64.bl __rtlock_release
    arm64.b __rl_cont#38
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
  __sched_num_procs@448 = i64 0
  __sched_ncpu@456 = i64 0
  __sched_shutdown_flag@464 = i64 0
  __sched_lock@472 = i64 0
  __sched_lock_owner@480 = i64 0
  __sched_lock_depth@488 = i64 0
  __sched_lock_depth_sink@496 = i64 0
  __sched_nmspinning@504 = i64 0
  __sched_needspinning@512 = i64 0
  __sched_sysmon_event@520 = i64 0
  __sched_sysmon_wait@528 = i64 0
  __sched_timer_starts@536 = i64 0
  __mrt_envp@544 = i64 0
  __mrt_signal_stack_bytes@552 = i64 0
  __mrt_hwcap_atomics@560 = i64 0
  __mrt_tls_next_key@568 = i64 0
  __io_pipe_seq@576 = i64 0
  __subp_table@584 = i64 0
  __subp_last_error@592 = i64 0
  __slab_arena_list@600 = i64 0
  __slab_arena_map_l1@608 = i64 0
  __slab_state@616 = i64 0
  __mrt_program_started@624 = i8 0
}

func @main {
  entry:
    arm64.prologue 224
    arm64.storeSlotReg slot25, x28
    arm64.storeSlotReg slot24, x27
    arm64.storeSlotReg slot23, x26
    arm64.storeSlotReg slot22, x25
    arm64.storeSlotReg slot21, x24
    arm64.storeSlotReg slot20, x23
    arm64.storeSlotReg slot19, x22
    arm64.storeSlotReg slot18, x21
    arm64.storeSlotReg slot17, x20
    arm64.storeSlotReg slot16, x19
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_76  ; "-c"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_77  ; "sleep 1; echo hi"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.bl Scheduler.counters
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot1, x20
    arm64.leaRdata x21, __str_rec_78  ; "/bin/sh"
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x19
    arm64.bl Subprocess.run
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot2, x19
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_Executable
    arm64.bl Scheduler.counters
    arm64.storeSlotReg slot3, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 64]
    arm64.loadRegBaseDisp.word64 x2, [x20 + 64]
    arm64.sub x21, x1, x2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 72]
    arm64.loadRegBaseDisp.word64 x1, [x20 + 72]
    arm64.sub x20, x0, x1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
  __il_body#13:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.storeSlotReg slot4, x22
  __il_cont#12:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.leaRdata x1, __str_rec_79  ; "hi"
    arm64.bl String.startsWith
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_80  ; "timerpolls="
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.cmp x21, 16
    arm64.cset x21, ls
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot15, x1
    arm64.movRegReg x0, x21
    arm64.bl __bool_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_81  ; " onpoller="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot14, x1
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.cmp x20, 1
    arm64.cset x20, hs
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot13, x1
    arm64.movRegReg x0, x20
    arm64.bl __bool_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_82  ; " outLen="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot12, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot11, x1
    arm64.movRegReg x0, x22
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_83  ; " matches="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot10, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __bool_to_string
    arm64.storeSlotReg slot9, x0
    arm64.leaRdata x1, __str_rec_84  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot8, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot7, x1
    arm64.add x2, x24, x21
    arm64.add x2, x2, x25
    arm64.add x2, x2, x20
    arm64.add x2, x2, x26
    arm64.add x2, x2, x22
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot5, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot6, x0
    arm64.add x19, x0, 56
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x23, x19, x24
    arm64.loadRegSlot x1, slot15
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x21, x23, x21
    arm64.loadRegSlot x1, slot14
    arm64.movRegReg x0, x21
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x21, x21, x25
    arm64.loadRegSlot x1, slot13
    arm64.movRegReg x0, x21
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x21, x20
    arm64.loadRegSlot x1, slot12
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x20, x20, x26
    arm64.loadRegSlot x1, slot11
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x20, x20, x22
    arm64.loadRegSlot x1, slot10
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x20, x20, x27
    arm64.loadRegSlot x2, slot9
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot9
    arm64.add x20, x20, x0
    arm64.loadRegSlot x2, slot7
    arm64.loadRegSlot x1, slot8
    arm64.movRegReg x0, x20
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot7
    arm64.add x0, x20, x0
    arm64.loadRegSlot x0, slot6
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.loadRegSlot x1, slot5
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.loadRegSlot x0, slot15
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot13
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot11
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
  __il_body#11:
    arm64.loadRegSlot x0, slot6
    arm64.bl __write_stdout
  __il_cont#10:
    arm64.loadRegSlot x0, slot6
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot4
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.loadRegSlot x0, slot4
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegSlot x0, slot3
    arm64.bl __mm_decref
  exitdrop#4:
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_CollectedOutput
  exitdrop#5:
    arm64.loadRegSlot x0, slot1
    arm64.bl __mm_decref
  exitdrop#6:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.loadRegSlot x0, slot4
    arm64.loadRegSlot x19, slot16
    arm64.loadRegSlot x20, slot17
    arm64.loadRegSlot x21, slot18
    arm64.loadRegSlot x22, slot19
    arm64.loadRegSlot x23, slot20
    arm64.loadRegSlot x24, slot21
    arm64.loadRegSlot x25, slot22
    arm64.loadRegSlot x26, slot23
    arm64.loadRegSlot x27, slot24
    arm64.loadRegSlot x28, slot25
    arm64.epilogue 224
    arm64.retIndirect
  tryerr:
    arm64.leaRdata x0, __str_blob_85  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:11: /bin/sh runs on every POSIX lane\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot16
    arm64.loadRegSlot x20, slot17
    arm64.loadRegSlot x21, slot18
    arm64.loadRegSlot x22, slot19
    arm64.loadRegSlot x23, slot20
    arm64.loadRegSlot x24, slot21
    arm64.loadRegSlot x25, slot22
    arm64.loadRegSlot x26, slot23
    arm64.loadRegSlot x27, slot24
    arm64.loadRegSlot x28, slot25
    arm64.epilogue 224
    arm64.retIndirect
  __rc_panic:
    arm64.leaRdata x0, __str_blob_264  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:18: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot16
    arm64.loadRegSlot x20, slot17
    arm64.loadRegSlot x21, slot18
    arm64.loadRegSlot x22, slot19
    arm64.loadRegSlot x23, slot20
    arm64.loadRegSlot x24, slot21
    arm64.loadRegSlot x25, slot22
    arm64.loadRegSlot x26, slot23
    arm64.loadRegSlot x27, slot24
    arm64.loadRegSlot x28, slot25
    arm64.epilogue 224
    arm64.retIndirect
}

func @__np_pd_wait_any {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movRegReg x19, x0
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, 0
    arm64.cset x1, ne
    arm64.cmp x2, 0
    arm64.cset x3, ne
    arm64.and x1, x1, x3
    arm64.movImm x3, 0
    arm64.sub x3, x3, x1
    arm64.sub x1, x1, 1
    arm64.add x2, x2, 656
    arm64.leaGlobal x4, __sched_lock_depth_sink
    arm64.and x2, x2, x3
    arm64.and x1, x4, x1
    arm64.orr x1, x2, x1
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.arm64ThreadPointer x1, linuxWritable
    arm64.movImm x2, 1
    arm64.arm64AtomicRmw.exchangeByte x2, [x0], x2
    arm64.cmp x2, 0
    arm64.b.ne __rl_slow#37
  __rl_owned#36:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
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
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __rl_nested#47
  __rl_outermost#48:
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 0
    arm64.arm64AtomicRmw.exchangeByte x1, [x0], x1
    arm64.cmp x1, 1
    arm64.b.eq __rl_cont#46
    arm64.b __rl_slow#49
  __rl_nested#47:
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
  __rl_cont#46:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 656
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x0, 10
    arm64.movRegReg x1, x19
    arm64.bl __gt_park
  npgaresumed:
    arm64.leaGlobal x0, __sched_lock
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, 0
    arm64.cset x1, ne
    arm64.cmp x2, 0
    arm64.cset x3, ne
    arm64.and x1, x1, x3
    arm64.movImm x3, 0
    arm64.sub x3, x3, x1
    arm64.sub x1, x1, 1
    arm64.add x2, x2, 656
    arm64.leaGlobal x4, __sched_lock_depth_sink
    arm64.and x2, x2, x3
    arm64.and x1, x4, x1
    arm64.orr x1, x2, x1
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.arm64ThreadPointer x1, linuxWritable
    arm64.movImm x2, 1
    arm64.arm64AtomicRmw.exchangeByte x2, [x0], x2
    arm64.cmp x2, 0
    arm64.b.ne __rl_slow#52
  __rl_owned#51:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  __rl_cont#50:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x21, 0
  npgaclearhdr:
    arm64.cmp x21, x20
    arm64.b.lt npgaclearbody
  npgadone:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.leaGlobal x1, __sched_lock
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.cmp x2, 0
    arm64.b.ne __rl_nested#54
  __rl_outermost#55:
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.movImm x2, 0
    arm64.arm64AtomicRmw.exchangeByte x2, [x1], x2
    arm64.cmp x2, 1
    arm64.b.eq __rl_cont#53
    arm64.b __rl_slow#56
  __rl_nested#54:
    arm64.add x2, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
  __rl_cont#53:
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, 0
    arm64.cset x1, ne
    arm64.cmp x2, 0
    arm64.cset x3, ne
    arm64.and x1, x1, x3
    arm64.movImm x3, 0
    arm64.sub x3, x3, x1
    arm64.sub x1, x1, 1
    arm64.add x2, x2, 656
    arm64.leaGlobal x4, __sched_lock_depth_sink
    arm64.and x2, x2, x3
    arm64.and x1, x4, x1
    arm64.orr x1, x2, x1
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
    arm64.retIndirect
  npholdlost:
    arm64.leaRdata x0, __abort_msg_115  ; "fatal error: runtime abort 115 (netpollHoldLost)\x0a"
    arm64.movImm x1, 49
    arm64.movImm x2, 115
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __rl_nested#43
  __rl_outermost#44:
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 0
    arm64.arm64AtomicRmw.exchangeByte x1, [x0], x1
    arm64.cmp x1, 1
    arm64.b.eq __rl_cont#42
    arm64.b __rl_slow#45
  __rl_nested#43:
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
  __rl_cont#42:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 656
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x0, 1
    arm64.lslv x0, x0, x21
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
    arm64.retIndirect
  npnopd#26:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
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
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __rl_nested#39
  __rl_outermost#40:
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 0
    arm64.arm64AtomicRmw.exchangeByte x1, [x0], x1
    arm64.cmp x1, 1
    arm64.b.eq __rl_cont#38
    arm64.b __rl_slow#41
  __rl_nested#39:
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
  __rl_cont#38:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 656
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
    arm64.retIndirect
  npnopd#25:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.epilogue 64
    arm64.retIndirect
  __rl_slow#37:
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_enter_slow
    arm64.b __rl_cont#35
  __rl_slow#49:
    arm64.bl __rtlock_release
    arm64.b __rl_cont#46
  __rl_slow#52:
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_enter_slow
    arm64.b __rl_cont#50
  __rl_slow#56:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_release
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __rl_cont#53
  __rl_slow#45:
    arm64.bl __rtlock_release
    arm64.b __rl_cont#42
  __rl_slow#41:
    arm64.bl __rtlock_release
    arm64.b __rl_cont#38
}
```
