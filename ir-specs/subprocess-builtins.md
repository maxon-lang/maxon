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
<!-- unsupported-targets: x64-windows -->
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
`__Builtins.schedTimerStartCount()` counts the machines the system monitor starts for an overdue timer,
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
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

// The whole second the child takes to answer. A drain parked on the poller returns a handful of times;
// one that yields a millisecond per idle pass returns about two thousand.
let quietWakes = 16

// The collect wait is a park on the poller — at least the one this program makes.
let pollerParks = 1

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "/bin/sh")
	appendToken(argv, token: "-c")
	appendToken(argv, token: "sleep 1; echo hi")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let beforeWakes = __Builtins.schedParkWakeCount()
	let beforeParks = __Builtins.schedNetpollBlockCount()
	let h = __Builtins.subprocessSpawn(argv, 3, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let wakes = __Builtins.schedParkWakeCount() - beforeWakes
	let parks = __Builtins.schedNetpollBlockCount() - beforeParks
	let out = String.init(__Builtins.subprocessResultStdout(r))
	let n = out.byteLength()
	let matches = out.startsWith("hi")
	print("timerpolls={wakes <= quietWakes} onpoller={parks >= pollerParks} outLen={n} matches={matches}\n")
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
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
  __io_pipe_seq@512 = i64 0
  __subp_table@520 = i64 0
  __subp_last_error@528 = i64 0
  __slab_arena_list@536 = i64 0
  __slab_arena_map_l1@544 = i64 0
  __slab_state@552 = i64 0
  __mrt_program_started@560 = i8 0
}

func @appendToken {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
  __il_body#16:
    x64.movRegImm32 rax, 0
  __il_body#20:
    x64.loadRegBaseDisp.word64 rsi, [rdx + 8]
  __il_cont#19:
    x64.movRegReg rcx, rdx
    x64.movRegReg rdx, rax
    x64.movRegReg rax, rsi
    x64.callDirect __managed_slice
    x64.movRegReg r12, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#17
  tryerr#18:
    x64.leaRegRdata rcx, [rip + __str_blob_22]  ; "panic at String.maxon:149: String.toByteArray: slice 0..byteLength() is in bounds by construction\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#17:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_retain
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  __il_cont#15:
    x64.loadRegBaseDisp.word64 r12, [r13 + 8]
    x64.movRegImm32 r14, 0
    x64.jmp forhdr
  byteLoop:
    x64.loadRegBaseDisp.word64 rax, [r13 + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegReg r14, rax
    x64.jcc aboveEqual, __im_slow
  __im_load#13:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + r14*8 + 0]
    x64.jmp __im_loaded
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegReg r14, rax
    x64.jcc aboveEqual, __im_slow
  __im_load#14:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  __im_loaded:
    x64.movRegReg rdx, rax
    x64.jmp tryok#5
  __im_slow:
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#6
  critsplit:
    x64.movRegReg rdx, r8
  tryok#5:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
  forstep:
    x64.leaRegRegImm32 r14, r14, 1
  forhdr:
    x64.cmpRegReg r14, r12
    x64.jcc less, byteLoop
  forexit:
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#6:
    x64.leaRegRdata rcx, [rip + __str_blob_10]  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:16: appendToken: get is in range\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 32
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
    x64.prologue 328
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.leaRegRdata rdx, [rip + __str_rec_11]  ; "/bin/sh"
    x64.movRegReg rcx, rbx
    x64.callDirect appendToken
    x64.leaRegRdata rdx, [rip + __str_rec_12]  ; "-c"
    x64.movRegReg rcx, rbx
    x64.callDirect appendToken
    x64.leaRegRdata rdx, [rip + __str_rec_13]  ; "sleep 1; echo hi"
    x64.movRegReg rcx, rbx
    x64.callDirect appendToken
    x64.leaRegRdata r12, [rip + __str_rec_14]  ; ""
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 1
    x64.callDirect __managed_mem_create
    x64.movRegReg r13, r8
    x64.storeSlotReg slot1, r13
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
  tryok#1:
    x64.callDirect __sched_park_wake_count
    x64.storeSlotReg slot25, r8
    x64.callDirect __sched_netpoll_block_count
    x64.storeSlotReg slot22, r8
    x64.movRegReg rcx, r12
    x64.callDirect String.cstr
    x64.storeSlotReg slot28, r8
    x64.movRegReg rcx, r12
    x64.callDirect String.cstr
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r12
    x64.callDirect String.cstr
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.callDirect String.cstr
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.storeBaseDispReg.word64 [rsp + 32], r14
    x64.movRegImm32 rdx, 2
    x64.storeBaseDispReg.word64 [rsp + 40], rdx
    x64.storeBaseDispReg.word64 [rsp + 48], r15
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rsp + 56], rdx
    x64.movRegImm32 rdx, 2
    x64.storeBaseDispReg.word64 [rsp + 64], rdx
    x64.storeBaseDispReg.word64 [rsp + 72], r8
    x64.storeBaseDispReg.word64 [rsp + 80], rax
    x64.storeBaseDispReg.word64 [rsp + 88], rcx
    x64.movRegReg rcx, rbx
    x64.movRegImm32 rdx, 3
    x64.loadRegSlot rax, slot28
    x64.movRegReg r9, r13
    x64.movRegImm32 rsi, 1
    x64.movRegImm32 rdi, 0
    x64.callDirect __gt_subp_attached_spawn
    x64.storeSlotReg slot4, r8
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, r8
    x64.callDirect __gt_subp_wait_collect
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot5, rbx
    x64.callDirect __sched_park_wake_count
    x64.loadRegSlot rax, slot25
    x64.movRegReg r12, r8
    x64.subRegReg r12, r8, rax
    x64.callDirect __sched_netpoll_block_count
    x64.loadRegSlot rax, slot22
    x64.movRegReg r13, r8
    x64.subRegReg r13, r8, rax
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_subp_result_stdout
    x64.movRegReg rbx, r8
  __il_body#11:
    x64.loadRegBaseDisp.word64 r14, [rbx + 8]
    x64.movRegImm32 r15, 0
    x64.jmp forhdr
  scan:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __managed_byte_at
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#17
  tryok#16:
    x64.cmpRegImm32 r8, 128
    x64.movRegImm32 rax, 1
    x64.jcc greaterEqual, scmerge
  orrhs:
    x64.cmpRegImm32 r8, 13
    x64.setccReg equal, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forstep
  notSingleByte:
    x64.movRegImm32 rdx, 0
    x64.jmp __il_cont#10
  forstep:
    x64.leaRegRegImm32 r15, r15, 1
  forhdr:
    x64.cmpRegReg r15, r14
    x64.jcc less, scan
  forexit:
    x64.movRegImm32 rdx, 1
  __il_cont#10:
    x64.movRegReg rcx, rbx
    x64.callDirect __str_of_buffer
    x64.movRegReg r14, r8
    x64.storeSlotReg slot2, r14
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  __il_body#23:
    x64.loadRegBaseDisp.word64 rbx, [r14 + 8]
    x64.storeSlotReg slot3, rbx
  __il_cont#22:
    x64.leaRegRdata rdx, [rip + __str_rec_15]  ; "hi"
    x64.movRegReg rcx, r14
    x64.callDirect String.startsWith
    x64.movRegReg r14, r8
    x64.leaRegRdata rax, [rip + __str_rec_16]  ; "timerpolls="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot17, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot26, rax
    x64.cmpRegImm32 r12, 16
    x64.setccReg lessEqual, r12
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot16, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __bool_to_string
    x64.movRegReg r12, r8
    x64.storeSlotReg slot24, r12
    x64.leaRegRdata rax, [rip + __str_rec_17]  ; " onpoller="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot15, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot23, rax
    x64.cmpRegImm32 r13, 1
    x64.setccReg greaterEqual, r13
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot14, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r8
    x64.callDirect __bool_to_string
    x64.movRegReg r13, r8
    x64.storeSlotReg slot21, r13
    x64.leaRegRdata rax, [rip + __str_rec_18]  ; " outLen="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot20, rax
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot6, rbx
    x64.leaRegRdata rax, [rip + __str_rec_19]  ; " matches="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r15, [rax + 8]
    x64.storeSlotReg slot8, r15
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot10, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r8
    x64.callDirect __bool_to_string
    x64.storeSlotReg slot27, r8
    x64.leaRegRdata rax, [rip + __str_rec_20]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot12, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot19, rax
    x64.loadRegSlot r14, slot26
    x64.leaRegRegReg rax, r14, r12
    x64.loadRegSlot r12, slot23
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.loadRegSlot r13, slot20
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r15
    x64.loadRegSlot rbx, slot27
    x64.leaRegRegReg rax, rax, rbx
    x64.loadRegSlot rcx, slot19
    x64.leaRegRegReg rax, rax, rcx
    x64.storeSlotReg slot13, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot18, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.loadRegSlot rdx, slot17
    x64.movRegReg rcx, r15
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg r14, r15, r14
    x64.loadRegSlot rdx, slot16
    x64.loadRegSlot rax, slot24
    x64.movRegReg rcx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot24
    x64.leaRegRegReg r14, r14, rax
    x64.loadRegSlot rdx, slot15
    x64.movRegReg rcx, r14
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r14, r12
    x64.loadRegSlot rdx, slot14
    x64.loadRegSlot rax, slot21
    x64.movRegReg rcx, r12
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot21
    x64.leaRegRegReg r12, r12, rax
    x64.loadRegSlot rdx, slot11
    x64.movRegReg rcx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r12, r13
    x64.loadRegSlot rax, slot6
    x64.loadRegSlot rdx, slot7
    x64.movRegReg rcx, r12
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot6
    x64.leaRegRegReg r12, r12, rax
    x64.loadRegSlot rax, slot8
    x64.loadRegSlot rdx, slot9
    x64.movRegReg rcx, r12
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot8
    x64.leaRegRegReg r12, r12, rax
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
    x64.loadRegSlot rdx, slot12
    x64.loadRegSlot rax, slot19
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot19
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot18
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot13
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot16
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot14
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot7
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot10
    x64.callDirect __mm_decref
  __il_body#7:
    x64.loadRegSlot rcx, slot18
    x64.callDirect __write_stdout
  __il_cont#6:
    x64.loadRegSlot rcx, slot18
    x64.callDirect __str_decref
    x64.loadRegSlot rcx, slot5
    x64.callDirect __gt_subp_result_release
    x64.loadRegSlot rcx, slot4
    x64.callDirect __gt_subp_release
    x64.loadRegSlot rax, slot3
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.loadRegSlot rax, slot3
    x64.cmpRegImm32 rax, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.loadRegSlot r8, slot3
    x64.epilogue 328
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_21]  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:27: create(1, 1) cannot fail\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 328
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#17:
    x64.leaRegRdata rcx, [rip + __str_blob_25]  ; "panic at String.maxon:1122: scanSingleByteGraphemes: byteAt OOB \xe2\x80\x94 i < len invariant\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 328
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_27]  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:40: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 328
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
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
    x64.leaRegRegImm32 rcx, rcx, 584
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.movRegImm32 rax, 1
    x64.lock add [rcx], rax
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_enter
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
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
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
    x64.leaRegRegImm32 rcx, rcx, 584
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.movRegImm rax, 18446744073709551615
    x64.lock add [rcx], rax
    x64.movRegImm32 rcx, 10
    x64.movRegReg rdx, rbx
    x64.callDirect __gt_park
  npgaresumed:
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
    x64.leaRegRegImm32 rcx, rcx, 584
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.movRegImm32 rax, 1
    x64.lock add [rcx], rax
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_enter
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 r13, 0
  npgaclearhdr:
    x64.cmpRegReg r13, r12
    x64.jcc less, npgaclearbody
  npgadone:
    x64.loadRegBaseDisp.word64 rbx, [rbx + 16]
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
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
    x64.leaRegRegImm32 rcx, rcx, 584
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.movRegImm rax, 18446744073709551615
    x64.lock add [rcx], rax
    x64.movRegReg r8, rbx
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
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
    x64.leaRegRdata rbx, [rip + __abort_msg_109]  ; "fatal error: runtime abort 109 (netpollUnhookMissed)\x0a"
    x64.movRegImm32 r12, 53
    x64.callDirect __sched_enter_syscall
    x64.movRegImm32 rdi, 2
    x64.movRegReg rsi, rbx
    x64.movRegReg rdx, r12
    x64.x64Syscall 1
    x64.callDirect __sched_exit_syscall
    x64.movRegImm32 rdi, 109
    x64.movReg32Reg32 rdi, rdi
    x64.x64Syscall 231
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  npholdlost:
    x64.leaRegRdata rbx, [rip + __abort_msg_115]  ; "fatal error: runtime abort 115 (netpollHoldLost)\x0a"
    x64.movRegImm32 r12, 49
    x64.callDirect __sched_enter_syscall
    x64.movRegImm32 rdi, 2
    x64.movRegReg rsi, rbx
    x64.movRegReg rdx, r12
    x64.x64Syscall 1
    x64.callDirect __sched_exit_syscall
    x64.movRegImm32 rdi, 115
    x64.movReg32Reg32 rdi, rdi
    x64.x64Syscall 231
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  npgaclearnext:
    x64.leaRegRegImm32 r13, r13, 1
    x64.jmp npgaclearhdr
  npnopd#30:
    x64.leaRegRdata rbx, [rip + __abort_msg_104]  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    x64.movRegImm32 r12, 54
    x64.callDirect __sched_enter_syscall
    x64.movRegImm32 rdi, 2
    x64.movRegReg rsi, rbx
    x64.movRegReg rdx, r12
    x64.x64Syscall 1
    x64.callDirect __sched_exit_syscall
    x64.movRegImm32 rdi, 104
    x64.movReg32Reg32 rdi, rdi
    x64.x64Syscall 231
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
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
    x64.leaRegRdata rbx, [rip + __abort_msg_104]  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    x64.movRegImm32 r12, 54
    x64.callDirect __sched_enter_syscall
    x64.movRegImm32 rdi, 2
    x64.movRegReg rsi, rbx
    x64.movRegReg rdx, r12
    x64.x64Syscall 1
    x64.callDirect __sched_exit_syscall
    x64.movRegImm32 rdi, 104
    x64.movReg32Reg32 rdi, rdi
    x64.x64Syscall 231
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
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
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
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
    x64.leaRegRegImm32 rcx, rcx, 584
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.movRegImm rax, 18446744073709551615
    x64.lock add [rcx], rax
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
    x64.ret
  npwsnotready:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, npwsdouble
  npgalooknext:
    x64.leaRegRegImm32 r13, r13, 1
    x64.jmp npgalookhdr
  npwsdouble:
    x64.leaRegRdata rbx, [rip + __abort_msg_107]  ; "fatal error: runtime abort 107 (netpollDoubleWait)\x0a"
    x64.movRegImm32 r12, 51
    x64.callDirect __sched_enter_syscall
    x64.movRegImm32 rdi, 2
    x64.movRegReg rsi, rbx
    x64.movRegReg rdx, r12
    x64.x64Syscall 1
    x64.callDirect __sched_exit_syscall
    x64.movRegImm32 rdi, 107
    x64.movReg32Reg32 rdi, rdi
    x64.x64Syscall 231
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  npnopd#26:
    x64.leaRegRdata rbx, [rip + __abort_msg_104]  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    x64.movRegImm32 r12, 54
    x64.callDirect __sched_enter_syscall
    x64.movRegImm32 rdi, 2
    x64.movRegReg rsi, rbx
    x64.movRegReg rdx, r12
    x64.x64Syscall 1
    x64.callDirect __sched_exit_syscall
    x64.movRegImm32 rdi, 104
    x64.movReg32Reg32 rdi, rdi
    x64.x64Syscall 231
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
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
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
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
    x64.leaRegRegImm32 rcx, rcx, 584
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.movRegImm rax, 18446744073709551615
    x64.lock add [rcx], rax
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  npnopd#25:
    x64.leaRegRdata rbx, [rip + __abort_msg_104]  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    x64.movRegImm32 r12, 54
    x64.callDirect __sched_enter_syscall
    x64.movRegImm32 rdi, 2
    x64.movRegReg rsi, rbx
    x64.movRegReg rdx, r12
    x64.x64Syscall 1
    x64.callDirect __sched_exit_syscall
    x64.movRegImm32 rdi, 104
    x64.movReg32Reg32 rdi, rdi
    x64.x64Syscall 231
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
  __io_pipe_seq@472 = i64 0
  __subp_table@480 = i64 0
  __subp_last_error@488 = i64 0
  __slab_arena_list@496 = i64 0
  __slab_arena_map_l1@504 = i64 0
  __slab_state@512 = i64 0
  __mrt_program_started@520 = i8 0
}

func @appendToken {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
  __il_body#16:
    arm64.movImm x2, 0
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x3, [x1 + 8]
  __il_cont#19:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __managed_slice
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#17
  tryerr#18:
    arm64.leaRdata x0, __str_blob_22  ; "panic at String.maxon:149: String.toByteArray: slice 0..byteLength() is in bounds by construction\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  tryok#17:
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#15:
    arm64.loadRegBaseDisp.word64 x20, [x21 + 8]
    arm64.movImm x22, 0
    arm64.b forhdr
  byteLoop:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x22, x0
    arm64.b.hs __im_slow
  __im_load#13:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x22*8 + 0]
    arm64.b __im_loaded
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x22, x0
    arm64.b.hs __im_slow
  __im_load#14:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.add x0, x0, x22
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
  __im_loaded:
    arm64.movRegReg x1, x0
    arm64.b tryok#5
  __im_slow:
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
  critsplit:
    arm64.movRegReg x1, x0
  tryok#5:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
  forstep:
    arm64.add x22, x22, 1
  forhdr:
    arm64.cmp x22, x20
    arm64.b.lt byteLoop
  forexit:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  tryerr#6:
    arm64.leaRdata x0, __str_blob_10  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:16: appendToken: get is in range\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 288 record@48
    arm64.storeSlotReg slot26, x28
    arm64.storeSlotReg slot25, x27
    arm64.storeSlotReg slot24, x26
    arm64.storeSlotReg slot23, x25
    arm64.storeSlotReg slot22, x24
    arm64.storeSlotReg slot21, x23
    arm64.storeSlotReg slot20, x22
    arm64.storeSlotReg slot19, x21
    arm64.storeSlotReg slot18, x20
    arm64.storeSlotReg slot17, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x1, __str_rec_11  ; "/bin/sh"
    arm64.movRegReg x0, x19
    arm64.bl appendToken
    arm64.leaRdata x1, __str_rec_12  ; "-c"
    arm64.movRegReg x0, x19
    arm64.bl appendToken
    arm64.leaRdata x1, __str_rec_13  ; "sleep 1; echo hi"
    arm64.movRegReg x0, x19
    arm64.bl appendToken
    arm64.leaRdata x20, __str_rec_14  ; ""
    arm64.movImm x0, 1
    arm64.movImm x1, 1
    arm64.bl __managed_mem_create
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot1, x21
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
  tryok#1:
    arm64.bl __sched_park_wake_count
    arm64.movRegReg x22, x0
    arm64.bl __sched_netpoll_block_count
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movRegReg x26, x0
    arm64.movImm x27, 0
    arm64.movImm x28, 2
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movImm x1, 0
    arm64.movImm x2, 0
    arm64.storeOutgoingArg [sp + arg0], x26
    arm64.storeOutgoingArg [sp + arg1], x27
    arm64.storeOutgoingArg [sp + arg2], x28
    arm64.storeOutgoingArg [sp + arg3], x0
    arm64.storeOutgoingArg [sp + arg4], x1
    arm64.storeOutgoingArg [sp + arg5], x2
    arm64.movRegReg x0, x19
    arm64.movImm x1, 3
    arm64.movRegReg x2, x24
    arm64.movRegReg x3, x21
    arm64.movImm x4, 1
    arm64.movImm x5, 0
    arm64.movRegReg x6, x25
    arm64.movImm x7, 2
    arm64.bl __gt_subp_attached_spawn
    arm64.storeSlotReg slot4, x0
    arm64.movImm x1, 0
    arm64.bl __gt_subp_wait_collect
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot5, x19
    arm64.bl __sched_park_wake_count
    arm64.sub x20, x0, x22
    arm64.bl __sched_netpoll_block_count
    arm64.sub x21, x0, x23
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_stdout
    arm64.movRegReg x19, x0
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x22, [x19 + 8]
    arm64.movImm x23, 0
    arm64.b forhdr#13
  scan:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __managed_byte_at
    arm64.cmp x9, 0
    arm64.b.ne tryerr#18
  tryok#17:
    arm64.cmp x0, 128
    arm64.movImm x1, 1
    arm64.b.ge scmerge
  orrhs:
    arm64.cmp x0, 13
    arm64.cset x0, eq
    arm64.movRegReg x1, x0
  scmerge:
    arm64.cbz x1, forstep#15
  notSingleByte:
    arm64.movImm x1, 0
    arm64.b __il_cont#11
  forstep#15:
    arm64.add x23, x23, 1
  forhdr#13:
    arm64.cmp x23, x22
    arm64.b.lt scan
  forexit#16:
    arm64.movImm x1, 1
  __il_cont#11:
    arm64.movRegReg x0, x19
    arm64.bl __str_of_buffer
    arm64.movRegReg x22, x0
    arm64.storeSlotReg slot2, x22
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  __il_body#43:
    arm64.loadRegBaseDisp.word64 x19, [x22 + 8]
    arm64.storeSlotReg slot3, x19
  __il_cont#42:
    arm64.leaRdata x23, __str_rec_15  ; "hi"
  __il_body#46:
    arm64.loadRegBaseDisp.word64 x24, [x23 + 8]
  __il_body#47:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
  __il_cont#44:
    arm64.cmp x24, x0
    arm64.b.ls __il_body#27
  tooLong:
    arm64.movImm x22, 0
    arm64.b __il_cont#8
  __il_body#27:
    arm64.movImm x25, 0
    arm64.b forhdr#28
  __rc_ok#38:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x25
    arm64.bl String.byteAt
    arm64.movRegReg x26, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#33
  __rc_ok#40:
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x25
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#35
  tryok#34:
    arm64.cmp x26, x0
    arm64.b.eq forstep#30
  byte_check:
    arm64.movImm x0, 0
    arm64.b __il_cont#26
  forstep#30:
    arm64.add x25, x25, 1
  forhdr#28:
    arm64.cmp x25, x24
    arm64.b.lt __rc_ok#38
  forexit#31:
    arm64.movImm x0, 1
  __il_cont#26:
    arm64.movRegReg x22, x0
  __il_cont#8:
    arm64.leaRdata x0, __str_rec_16  ; "timerpolls="
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.cmp x20, 16
    arm64.cset x20, le
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot16, x1
    arm64.movRegReg x0, x20
    arm64.bl __bool_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_17  ; " onpoller="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot15, x1
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.cmp x21, 1
    arm64.cset x21, ge
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot14, x1
    arm64.movRegReg x0, x21
    arm64.bl __bool_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_18  ; " outLen="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot13, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot12, x1
    arm64.movRegReg x0, x19
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_19  ; " matches="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot11, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x28
    arm64.bl __bool_to_string
    arm64.storeSlotReg slot10, x0
    arm64.leaRdata x1, __str_rec_20  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot9, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot8, x1
    arm64.add x2, x24, x20
    arm64.add x2, x2, x25
    arm64.add x2, x2, x21
    arm64.add x2, x2, x26
    arm64.add x2, x2, x19
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot6, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot7, x0
    arm64.add x22, x0, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x23, x22, x24
    arm64.loadRegSlot x1, slot16
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x23, x20
    arm64.loadRegSlot x1, slot15
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x20, x20, x25
    arm64.loadRegSlot x1, slot14
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x20, x21
    arm64.loadRegSlot x1, slot13
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x20, x20, x26
    arm64.loadRegSlot x1, slot12
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x20, x19
    arm64.loadRegSlot x1, slot11
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x19, x19, x27
    arm64.loadRegSlot x2, slot10
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot10
    arm64.add x19, x19, x0
    arm64.loadRegSlot x2, slot8
    arm64.loadRegSlot x1, slot9
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot8
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot7
    arm64.storeBaseDispReg.word64 [x0 + 0], x22
    arm64.loadRegSlot x1, slot6
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.loadRegSlot x0, slot16
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot14
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot12
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
  __il_body#7:
    arm64.loadRegSlot x0, slot7
    arm64.bl __write_stdout
  __il_cont#6:
    arm64.loadRegSlot x0, slot7
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot5
    arm64.bl __gt_subp_result_release
    arm64.loadRegSlot x0, slot4
    arm64.bl __gt_subp_release
    arm64.loadRegSlot x0, slot3
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.loadRegSlot x0, slot3
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok#3:
    arm64.loadRegSlot x0, slot2
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
    arm64.loadRegSlot x0, slot3
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
  tryerr#2:
    arm64.leaRdata x0, __str_blob_21  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:27: create(1, 1) cannot fail\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
  tryerr#18:
    arm64.leaRdata x0, __str_blob_25  ; "panic at String.maxon:1122: scanSingleByteGraphemes: byteAt OOB \xe2\x80\x94 i < len invariant\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
  tryerr#33:
    arm64.leaRdata x0, __str_blob_23  ; "panic at String.maxon:1007: bytesEqual: byteAt OOB \xe2\x80\x94 caller guarantees aOffset + len <= a.byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
  tryerr#35:
    arm64.leaRdata x0, __str_blob_24  ; "panic at String.maxon:1008: bytesEqual: byteAt OOB \xe2\x80\x94 caller guarantees bOffset + len <= b.byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_27  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:40: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
}

func @__np_pd_wait_any {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 1
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 18
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
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 18446744073709551615
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.movImm x0, 10
    arm64.movRegReg x1, x19
    arm64.bl __gt_park
  npgaresumed:
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 1
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 18
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x21, 0
  npgaclearhdr:
    arm64.cmp x21, x20
    arm64.b.lt npgaclearbody
  npgadone:
    arm64.loadRegBaseDisp.word64 x19, [x19 + 16]
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 18446744073709551615
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
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
    arm64.leaRdata x19, __abort_msg_109  ; "fatal error: runtime abort 109 (netpollUnhookMissed)\x0a"
    arm64.movImm x20, 53
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.importCall 1
    arm64.bl mrt_host_status_or_errno
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 109
    arm64.importCall 0
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  npholdlost:
    arm64.leaRdata x19, __abort_msg_115  ; "fatal error: runtime abort 115 (netpollHoldLost)\x0a"
    arm64.movImm x20, 49
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.importCall 1
    arm64.bl mrt_host_status_or_errno
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 115
    arm64.importCall 0
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  npgaclearnext:
    arm64.add x21, x21, 1
    arm64.b npgaclearhdr
  npnopd#30:
    arm64.leaRdata x19, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x20, 54
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.importCall 1
    arm64.bl mrt_host_status_or_errno
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 104
    arm64.importCall 0
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
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
    arm64.leaRdata x19, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x20, 54
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.importCall 1
    arm64.bl mrt_host_status_or_errno
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 104
    arm64.importCall 0
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
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
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 18446744073709551615
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.movImm x0, 1
    arm64.lslv x0, x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  npwsnotready:
    arm64.cmp x1, 0
    arm64.b.ne npwsdouble
  npgalooknext:
    arm64.add x21, x21, 1
    arm64.b npgalookhdr
  npwsdouble:
    arm64.leaRdata x19, __abort_msg_107  ; "fatal error: runtime abort 107 (netpollDoubleWait)\x0a"
    arm64.movImm x20, 51
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.importCall 1
    arm64.bl mrt_host_status_or_errno
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 107
    arm64.importCall 0
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  npnopd#26:
    arm64.leaRdata x19, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x20, 54
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.importCall 1
    arm64.bl mrt_host_status_or_errno
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 104
    arm64.importCall 0
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
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
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 18446744073709551615
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  npnopd#25:
    arm64.leaRdata x19, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x20, 54
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.importCall 1
    arm64.bl mrt_host_status_or_errno
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 104
    arm64.importCall 0
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
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
  __io_pipe_seq@504 = i64 0
  __subp_table@512 = i64 0
  __subp_last_error@520 = i64 0
  __slab_arena_list@528 = i64 0
  __slab_arena_map_l1@536 = i64 0
  __slab_state@544 = i64 0
  __mrt_program_started@552 = i8 0
}

func @appendToken {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
  __il_body#16:
    arm64.movImm x2, 0
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x3, [x1 + 8]
  __il_cont#19:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __managed_slice
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#17
  tryerr#18:
    arm64.leaRdata x0, __str_blob_22  ; "panic at String.maxon:149: String.toByteArray: slice 0..byteLength() is in bounds by construction\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  tryok#17:
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#15:
    arm64.loadRegBaseDisp.word64 x20, [x21 + 8]
    arm64.movImm x22, 0
    arm64.b forhdr
  byteLoop:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x22, x0
    arm64.b.hs __im_slow
  __im_load#13:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x22*8 + 0]
    arm64.b __im_loaded
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x22, x0
    arm64.b.hs __im_slow
  __im_load#14:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.add x0, x0, x22
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
  __im_loaded:
    arm64.movRegReg x1, x0
    arm64.b tryok#5
  __im_slow:
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
  critsplit:
    arm64.movRegReg x1, x0
  tryok#5:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
  forstep:
    arm64.add x22, x22, 1
  forhdr:
    arm64.cmp x22, x20
    arm64.b.lt byteLoop
  forexit:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  tryerr#6:
    arm64.leaRdata x0, __str_blob_10  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:16: appendToken: get is in range\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 288 record@48
    arm64.storeSlotReg slot26, x28
    arm64.storeSlotReg slot25, x27
    arm64.storeSlotReg slot24, x26
    arm64.storeSlotReg slot23, x25
    arm64.storeSlotReg slot22, x24
    arm64.storeSlotReg slot21, x23
    arm64.storeSlotReg slot20, x22
    arm64.storeSlotReg slot19, x21
    arm64.storeSlotReg slot18, x20
    arm64.storeSlotReg slot17, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x1, __str_rec_11  ; "/bin/sh"
    arm64.movRegReg x0, x19
    arm64.bl appendToken
    arm64.leaRdata x1, __str_rec_12  ; "-c"
    arm64.movRegReg x0, x19
    arm64.bl appendToken
    arm64.leaRdata x1, __str_rec_13  ; "sleep 1; echo hi"
    arm64.movRegReg x0, x19
    arm64.bl appendToken
    arm64.leaRdata x20, __str_rec_14  ; ""
    arm64.movImm x0, 1
    arm64.movImm x1, 1
    arm64.bl __managed_mem_create
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot1, x21
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
  tryok#1:
    arm64.bl __sched_park_wake_count
    arm64.movRegReg x22, x0
    arm64.bl __sched_netpoll_block_count
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movRegReg x26, x0
    arm64.movImm x27, 0
    arm64.movImm x28, 2
    arm64.movRegReg x0, x20
    arm64.bl String.cstr
    arm64.movImm x1, 0
    arm64.movImm x2, 0
    arm64.storeOutgoingArg [sp + arg0], x26
    arm64.storeOutgoingArg [sp + arg1], x27
    arm64.storeOutgoingArg [sp + arg2], x28
    arm64.storeOutgoingArg [sp + arg3], x0
    arm64.storeOutgoingArg [sp + arg4], x1
    arm64.storeOutgoingArg [sp + arg5], x2
    arm64.movRegReg x0, x19
    arm64.movImm x1, 3
    arm64.movRegReg x2, x24
    arm64.movRegReg x3, x21
    arm64.movImm x4, 1
    arm64.movImm x5, 0
    arm64.movRegReg x6, x25
    arm64.movImm x7, 2
    arm64.bl __gt_subp_attached_spawn
    arm64.storeSlotReg slot4, x0
    arm64.movImm x1, 0
    arm64.bl __gt_subp_wait_collect
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot5, x19
    arm64.bl __sched_park_wake_count
    arm64.sub x20, x0, x22
    arm64.bl __sched_netpoll_block_count
    arm64.sub x21, x0, x23
    arm64.movRegReg x0, x19
    arm64.bl __gt_subp_result_stdout
    arm64.movRegReg x19, x0
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x22, [x19 + 8]
    arm64.movImm x23, 0
    arm64.b forhdr#13
  scan:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __managed_byte_at
    arm64.cmp x9, 0
    arm64.b.ne tryerr#18
  tryok#17:
    arm64.cmp x0, 128
    arm64.movImm x1, 1
    arm64.b.ge scmerge
  orrhs:
    arm64.cmp x0, 13
    arm64.cset x0, eq
    arm64.movRegReg x1, x0
  scmerge:
    arm64.cbz x1, forstep#15
  notSingleByte:
    arm64.movImm x1, 0
    arm64.b __il_cont#11
  forstep#15:
    arm64.add x23, x23, 1
  forhdr#13:
    arm64.cmp x23, x22
    arm64.b.lt scan
  forexit#16:
    arm64.movImm x1, 1
  __il_cont#11:
    arm64.movRegReg x0, x19
    arm64.bl __str_of_buffer
    arm64.movRegReg x22, x0
    arm64.storeSlotReg slot2, x22
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  __il_body#43:
    arm64.loadRegBaseDisp.word64 x19, [x22 + 8]
    arm64.storeSlotReg slot3, x19
  __il_cont#42:
    arm64.leaRdata x23, __str_rec_15  ; "hi"
  __il_body#46:
    arm64.loadRegBaseDisp.word64 x24, [x23 + 8]
  __il_body#47:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
  __il_cont#44:
    arm64.cmp x24, x0
    arm64.b.ls __il_body#27
  tooLong:
    arm64.movImm x22, 0
    arm64.b __il_cont#8
  __il_body#27:
    arm64.movImm x25, 0
    arm64.b forhdr#28
  __rc_ok#38:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x25
    arm64.bl String.byteAt
    arm64.movRegReg x26, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#33
  __rc_ok#40:
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x25
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#35
  tryok#34:
    arm64.cmp x26, x0
    arm64.b.eq forstep#30
  byte_check:
    arm64.movImm x0, 0
    arm64.b __il_cont#26
  forstep#30:
    arm64.add x25, x25, 1
  forhdr#28:
    arm64.cmp x25, x24
    arm64.b.lt __rc_ok#38
  forexit#31:
    arm64.movImm x0, 1
  __il_cont#26:
    arm64.movRegReg x22, x0
  __il_cont#8:
    arm64.leaRdata x0, __str_rec_16  ; "timerpolls="
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.cmp x20, 16
    arm64.cset x20, le
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot16, x1
    arm64.movRegReg x0, x20
    arm64.bl __bool_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_17  ; " onpoller="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot15, x1
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.cmp x21, 1
    arm64.cset x21, ge
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot14, x1
    arm64.movRegReg x0, x21
    arm64.bl __bool_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_18  ; " outLen="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot13, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot12, x1
    arm64.movRegReg x0, x19
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_19  ; " matches="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot11, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x28
    arm64.bl __bool_to_string
    arm64.storeSlotReg slot10, x0
    arm64.leaRdata x1, __str_rec_20  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot9, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot8, x1
    arm64.add x2, x24, x20
    arm64.add x2, x2, x25
    arm64.add x2, x2, x21
    arm64.add x2, x2, x26
    arm64.add x2, x2, x19
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot6, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot7, x0
    arm64.add x22, x0, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x23, x22, x24
    arm64.loadRegSlot x1, slot16
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x23, x20
    arm64.loadRegSlot x1, slot15
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x20, x20, x25
    arm64.loadRegSlot x1, slot14
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x20, x21
    arm64.loadRegSlot x1, slot13
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x20, x20, x26
    arm64.loadRegSlot x1, slot12
    arm64.movRegReg x0, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x20, x19
    arm64.loadRegSlot x1, slot11
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x19, x19, x27
    arm64.loadRegSlot x2, slot10
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot10
    arm64.add x19, x19, x0
    arm64.loadRegSlot x2, slot8
    arm64.loadRegSlot x1, slot9
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot8
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot7
    arm64.storeBaseDispReg.word64 [x0 + 0], x22
    arm64.loadRegSlot x1, slot6
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.loadRegSlot x0, slot16
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot14
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot12
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
  __il_body#7:
    arm64.loadRegSlot x0, slot7
    arm64.bl __write_stdout
  __il_cont#6:
    arm64.loadRegSlot x0, slot7
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot5
    arm64.bl __gt_subp_result_release
    arm64.loadRegSlot x0, slot4
    arm64.bl __gt_subp_release
    arm64.loadRegSlot x0, slot3
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.loadRegSlot x0, slot3
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok#3:
    arm64.loadRegSlot x0, slot2
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
    arm64.loadRegSlot x0, slot3
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
  tryerr#2:
    arm64.leaRdata x0, __str_blob_21  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:27: create(1, 1) cannot fail\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
  tryerr#18:
    arm64.leaRdata x0, __str_blob_25  ; "panic at String.maxon:1122: scanSingleByteGraphemes: byteAt OOB \xe2\x80\x94 i < len invariant\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
  tryerr#33:
    arm64.leaRdata x0, __str_blob_23  ; "panic at String.maxon:1007: bytesEqual: byteAt OOB \xe2\x80\x94 caller guarantees aOffset + len <= a.byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
  tryerr#35:
    arm64.leaRdata x0, __str_blob_24  ; "panic at String.maxon:1008: bytesEqual: byteAt OOB \xe2\x80\x94 caller guarantees bOffset + len <= b.byteLength()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_27  ; "panic at subprocess-builtins.posix-a-collected-child-costs-no-timer-poll.test:40: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot17
    arm64.loadRegSlot x20, slot18
    arm64.loadRegSlot x21, slot19
    arm64.loadRegSlot x22, slot20
    arm64.loadRegSlot x23, slot21
    arm64.loadRegSlot x24, slot22
    arm64.loadRegSlot x25, slot23
    arm64.loadRegSlot x26, slot24
    arm64.loadRegSlot x27, slot25
    arm64.loadRegSlot x28, slot26
    arm64.epilogue 288 record@48
    arm64.ret
}

func @__np_pd_wait_any {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 1
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_enter
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
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 18446744073709551615
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.movImm x0, 10
    arm64.movRegReg x1, x19
    arm64.bl __gt_park
  npgaresumed:
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 1
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_enter
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x21, 0
  npgaclearhdr:
    arm64.cmp x21, x20
    arm64.b.lt npgaclearbody
  npgadone:
    arm64.loadRegBaseDisp.word64 x19, [x19 + 16]
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 18446744073709551615
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
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
    arm64.leaRdata x19, __abort_msg_109  ; "fatal error: runtime abort 109 (netpollUnhookMissed)\x0a"
    arm64.movImm x20, 53
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.syscall 64
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 109
    arm64.syscall 94
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  npholdlost:
    arm64.leaRdata x19, __abort_msg_115  ; "fatal error: runtime abort 115 (netpollHoldLost)\x0a"
    arm64.movImm x20, 49
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.syscall 64
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 115
    arm64.syscall 94
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  npgaclearnext:
    arm64.add x21, x21, 1
    arm64.b npgaclearhdr
  npnopd#30:
    arm64.leaRdata x19, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x20, 54
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.syscall 64
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 104
    arm64.syscall 94
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
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
    arm64.leaRdata x19, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x20, 54
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.syscall 64
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 104
    arm64.syscall 94
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
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
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 18446744073709551615
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.movImm x0, 1
    arm64.lslv x0, x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  npwsnotready:
    arm64.cmp x1, 0
    arm64.b.ne npwsdouble
  npgalooknext:
    arm64.add x21, x21, 1
    arm64.b npgalookhdr
  npwsdouble:
    arm64.leaRdata x19, __abort_msg_107  ; "fatal error: runtime abort 107 (netpollDoubleWait)\x0a"
    arm64.movImm x20, 51
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.syscall 64
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 107
    arm64.syscall 94
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  npnopd#26:
    arm64.leaRdata x19, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x20, 54
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.syscall 64
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 104
    arm64.syscall 94
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
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
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
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
    arm64.add x1, x1, 584
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.movImm x1, 18446744073709551615
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  npnopd#25:
    arm64.leaRdata x19, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x20, 54
    arm64.bl __sched_enter_syscall
    arm64.movImm x0, 2
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.syscall 64
    arm64.bl __sched_exit_syscall
    arm64.movImm x0, 104
    arm64.syscall 94
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
}
```
