---
feature: sched-runqueue
status: stable
keywords: [scheduler, green-threads, coroutine, run-queue, ring, work-stealing, GMP, async, MAXON_MAX_PROCS]
category: system
---

# The strand queue, and the green-thread run-queue hierarchy behind it: the cases that pin emitted code

The cases of `specs/sched-runqueue.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: sched-runqueue.a-thief-reads-the-victims-ring-with-acquire-loads -->
<!-- procs: 1 -->
⭐⭐ **A THIEF READS ANOTHER P's RING WITH ACQUIRE LOADS, BECAUSE THE TAIL IT READS IS A PUBLICATION AND
THE SLOT IT DESCRIBES IS THE THING PUBLISHED.** `__sched_runq_put` writes the green thread into
`runq[tail % n]` and only then advances the tail, and that advance is ordered — `emitRunqPublishTail`
makes it a read-modify-write, so the slot store cannot sink past it. That is one half of a pair and it is
worthless alone. The other half is the READER: `__sched_steal` loads the victim's `runqHead` and
`runqTail` — and its `runnext` — off another P's struct, and a plain load of the tail may be satisfied
from a stale line while the load of the slot it announces is satisfied from a fresh one. The thief then
copies a slot the victim has not written yet, and whatever word was there before is run as a green
thread: on a ring that has wrapped, a green thread that is already running on the victim, twice.

⛔ **x64 CANNOT SEE THIS AND NEITHER CAN THREE OF THE FOUR LANES.** TSO never reorders load with load, so
the plain `mov` pair is already an acquire pair there and the `wasm32-wasi` lane is single-threaded; the
window belongs to arm64, where a `ldr` pair may complete in either order. What a green thread run twice
looks like is a compiler that dies in its own pass workers — exit 86, a fault inside a refcount step, or
the leak gate — and the pass pool is the one program in this tree that keeps every P's ring full for
minutes at a time. **This window is one member of that class, and it is not claimed to be the whole
class.**

⇒ the three reads are `StdOp.loadAcquire`, which is `ldar` on arm64 and the same plain `mov` on x64: the
ordering is a property of the OP, so the lane that needs the barrier gets it and the lane that does not
pays nothing.

⚖ **WHAT THIS CASE PINS.** Its ```RequiredRuntime block renders `__sched_steal`'s emitted body into the
case's Target IR, and its `TargetIr:arm64-macos` and `TargetIr:arm64-linux` pins are where a reader sees
`arm64.ldar.word64` standing against the victim's `runqHead`, `runqTail` and `runnext`; a compile that
renders anything else fails the case. On the x64 lanes an acquire load lowers to the same plain `mov`, so
their pins cannot show the ordering. What the run checks is `hits=1` and exit 0 — that a spawned service
still answers its one message.

⇒ **THE ORDERING IS ALSO GATED TWICE OUTSIDE THIS FILE.**
`tests/emitted-runtime/steal-reads-the-victim-ring-with-acquire-loads.maxtest` cross-builds this
same shape for `arm64-macos` and `arm64-linux`, cuts `func @__sched_steal` out of the printed Target
IR and FAILS on a plain `ldr` of any of the three words, naming it. And the compiler refuses one
before it is ever emitted, on every target and so on lanes no pin here can show:
`assertRunQueueObservationIsOrdered` (`SchedRuntime.maxon`) walks the finished module and panics on a
plain read of one of those three words — wherever it can attribute the base the read goes through to a
processor, which is the reach of that check and the limit of what it promises.
```maxon
type Ping
	var hits as Integer

	static function create() returns Self
		return Self{hits: 0}
	end 'create'

	export function hit() returns Integer
		self.hits = self.hits + 1
		return self.hits
	end 'hit'
end 'Ping'

function main() returns ExitCode
	let service = spawn Ping.create()
	let seen = try await service.hit() otherwise panic("the one message this frame sends to a service it owns cannot fail")
	print("hits={seen}")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
hits=1
```
```exitcode
0
```
```RequiredRuntime
__sched_steal
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
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 8192
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __sched_active_workers@168 = i64 1
  __sched_max_active_workers@176 = i64 1
  __sched_tls_index@184 = i64 0
  __sched_tls_teb_offset@192 = i64 0
  __sched_procs@200 = i64 0
  __sched_allm@208 = i64 0
  __sched_async_preempt_off@216 = i64 0
  __sched_midle@224 = i64 0
  __sched_pidle@232 = i64 0
  __gt_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __sched_npidle@272 = i64 0
  __sched_runq_size@280 = i64 0
  __sched_global_pushes@288 = i64 0
  __sched_timer_scan_steps@296 = i64 0
  __sched_mcount@304 = i64 0
  __sched_nmidle@312 = i64 0
  __sched_main_m@320 = i64 0
  __sched_phase@328 = i64 0
  __sched_lastpoll@336 = i64 0
  __sched_poll_until@344 = i64 0
  __sched_regain_head@352 = i64 0
  __np_poller@360 = i64 0
  __np_break@368 = i64 0
  __np_wake_sig@376 = i64 0
  __np_pd_table@384 = i64 0
  __np_pd_cap@392 = i64 0
  __np_waiters@400 = i64 0
  __sched_num_procs@408 = i64 0
  __sched_shutdown_flag@416 = i64 0
  __sched_lock@424 = i64 0
  __sched_lock_depth_sink@432 = i64 0
  __sched_nmspinning@440 = i64 0
  __sched_needspinning@448 = i64 0
  __sched_sysmon_event@456 = i64 0
  __sched_sysmon_wait@464 = i64 0
  __sched_timer_starts@472 = i64 0
  __sched_preempt_ext_lock@480 = i64 0
  __sched_preempt_context@488 = i64 0
  __slab_arena_list@496 = i64 0
  __slab_arena_map_l1@504 = i64 0
  __slab_state@512 = i64 0
  __mrt_console_probe_stdin@520 = i8 0
  __mrt_console_probe_stdout@521 = i8 0
  __mrt_console_probe_stderr@522 = i8 0
  __mrt_program_started@523 = i8 0
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
  __il_body#5:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont#4:
    x64.leaRegFunc rcx, [rip + Ping.__loop]
    x64.leaRegFunc rax, [rip + Ping.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 8], rbx
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_try_await
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "hits="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRegReg rax, r13, rbx
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r12, rbx
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
  __il_body#7:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont#6:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at sched-runqueue.a-thief-reads-the-victims-ring-with-acquire-loads.test:17: the one message this frame sends to a service it owns cannot fail\x0a"
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

func @Ping.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=hit] @__jumptable_11
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  hit:
    x64.loadRegBaseDisp.word64 r13, [r8 + 8]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.loadRegBaseDisp.word64 r14, [rbx + 0]
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Ping.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=hit] @__jumptable_12
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  hit:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__sched_steal {
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
    x64.movRegReg r13, rax
    x64.loadRegBaseDisp.word64 r14, [rbx + 8]
  sgrab:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 8]
    x64.subRegReg rcx, rcx, rax
    x64.movRegReg rdx, rcx
    x64.shrRegImm8 rdx, rcx, 1
    x64.subRegReg rcx, rcx, rdx
    x64.cmpRegImm32 rcx, 0
    x64.jcc greater, ssized
  sslot:
    x64.cmpRegImm32 r13, 0
    x64.setccReg notEqual, rax
    x64.loadRegBaseDisp.word64 r15, [r12 + 88]
    x64.cmpRegImm32 r15, 0
    x64.setccReg notEqual, rcx
    x64.andRegReg rax, rax, rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, sfail
  sslotbusy:
    x64.loadRegBaseDisp.word64 rax, [r12 + 72]
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, sslottake
  sslotbackoff:
    x64.movRegImm32 rax, 3
    x64.movRegImm32 rax, 0
    x64.iatCall 77
    x64.shlRegImm8 rax, rax, 32
    x64.sarRegImm8 rax, rax, 32
  sslottake:
    x64.leaRegRegImm32 rcx, r12, 88
    x64.movRegImm32 rdx, 0
    x64.movRegReg rax, r15
    x64.lock cmpxchg [rcx], rdx
    x64.setccReg equal, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, sgrab
  sslottook:
    x64.movRegReg r8, r15
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  sfail:
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ssized:
    x64.cmpRegImm32 rcx, 128
    x64.movRegImm32 rdx, 0
    x64.jcc greater, sgrab
  scopyhdr:
    x64.cmpRegReg rdx, rcx
    x64.jcc less, scopybody
  scommit:
    x64.leaRegRegReg rdx, rax, rcx
    x64.lock cmpxchg [r12], rdx
    x64.setccReg equal, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, sgrab
  spublish:
    x64.leaRegRegImm32 rax, rcx, -1
    x64.leaRegRegReg rcx, r14, rax
    x64.andRegImm32 rcx, rcx, 255
    x64.leaRegRegImm32 rdx, rbx, 240
    x64.loadRegBaseIndexScale.word64 r8, [rdx + rcx*8 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, sanswer
  spubtail:
    x64.leaRegRegImm32 rcx, rbx, 8
    x64.lock add [rcx], rax
  sanswer:
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  scopybody:
    x64.leaRegRegReg rsi, rax, rdx
    x64.andRegImm32 rsi, rsi, 255
    x64.leaRegRegImm32 rdi, r12, 240
    x64.loadRegBaseIndexScale.word64 rsi, [rdi + rsi*8 + 0]
    x64.leaRegRegReg rdi, r14, rdx
    x64.andRegImm32 rdi, rdi, 255
    x64.leaRegRegImm32 r8, rbx, 240
    x64.storeBaseIndexScaleReg.word64 [r8 + rdi*8 + 0], rsi
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp scopyhdr
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
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __sched_active_workers@168 = i64 1
  __sched_max_active_workers@176 = i64 1
  __sched_tls_index@184 = i64 0
  __sched_tls_teb_offset@192 = i64 0
  __sched_procs@200 = i64 0
  __sched_allm@208 = i64 0
  __sched_async_preempt_off@216 = i64 0
  __sched_midle@224 = i64 0
  __sched_pidle@232 = i64 0
  __gt_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __sched_npidle@272 = i64 0
  __sched_runq_size@280 = i64 0
  __sched_global_pushes@288 = i64 0
  __sched_timer_scan_steps@296 = i64 0
  __sched_mcount@304 = i64 0
  __sched_nmidle@312 = i64 0
  __sched_main_m@320 = i64 0
  __sched_phase@328 = i64 0
  __sched_lastpoll@336 = i64 0
  __sched_poll_until@344 = i64 0
  __sched_regain_head@352 = i64 0
  __np_poller@360 = i64 0
  __np_break@368 = i64 0
  __np_wake_sig@376 = i64 0
  __np_pd_table@384 = i64 0
  __np_pd_cap@392 = i64 0
  __np_waiters@400 = i64 0
  __sched_num_procs@408 = i64 0
  __sched_shutdown_flag@416 = i64 0
  __sched_lock@424 = i64 0
  __sched_lock_depth_sink@432 = i64 0
  __sched_nmspinning@440 = i64 0
  __sched_needspinning@448 = i64 0
  __sched_sysmon_event@456 = i64 0
  __sched_sysmon_wait@464 = i64 0
  __sched_timer_starts@472 = i64 0
  __mrt_envp@480 = i64 0
  __mrt_signal_stack_bytes@488 = i64 0
  __mrt_tls_next_key@496 = i64 0
  __mrt_errno@504 = i64 0
  __mrt_tls_ready@512 = i64 0
  __slab_arena_list@520 = i64 0
  __slab_arena_map_l1@528 = i64 0
  __slab_state@536 = i64 0
  __mrt_program_started@544 = i8 0
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
  __il_body#5:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont#4:
    x64.leaRegFunc rcx, [rip + Ping.__loop]
    x64.leaRegFunc rax, [rip + Ping.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 8], rbx
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_try_await
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "hits="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRegReg rax, r13, rbx
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r12, rbx
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
  __il_body#7:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont#6:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at sched-runqueue.a-thief-reads-the-victims-ring-with-acquire-loads.test:17: the one message this frame sends to a service it owns cannot fail\x0a"
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

func @Ping.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=hit] @__jumptable_11
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  hit:
    x64.loadRegBaseDisp.word64 r13, [r8 + 8]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.loadRegBaseDisp.word64 r14, [rbx + 0]
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Ping.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=hit] @__jumptable_12
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  hit:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__sched_steal {
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
    x64.movRegReg r13, rax
    x64.loadRegBaseDisp.word64 r14, [rbx + 8]
  sgrab:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 8]
    x64.subRegReg rcx, rcx, rax
    x64.movRegReg rdx, rcx
    x64.shrRegImm8 rdx, rcx, 1
    x64.subRegReg rcx, rcx, rdx
    x64.cmpRegImm32 rcx, 0
    x64.jcc greater, ssized
  sslot:
    x64.cmpRegImm32 r13, 0
    x64.setccReg notEqual, rax
    x64.loadRegBaseDisp.word64 r15, [r12 + 88]
    x64.cmpRegImm32 r15, 0
    x64.setccReg notEqual, rcx
    x64.andRegReg rax, rax, rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, sfail
  sslotbusy:
    x64.loadRegBaseDisp.word64 rax, [r12 + 72]
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, sslottake
  sslotbackoff:
    x64.movRegImm32 rcx, 3
    x64.movRegImm32 rax, 0
    x64.callDirect mrt_host_netpoll_sleep
  sslottake:
    x64.leaRegRegImm32 rcx, r12, 88
    x64.movRegImm32 rdx, 0
    x64.movRegReg rax, r15
    x64.lock cmpxchg [rcx], rdx
    x64.setccReg equal, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, sgrab
  sslottook:
    x64.movRegReg r8, r15
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  sfail:
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ssized:
    x64.cmpRegImm32 rcx, 128
    x64.movRegImm32 rdx, 0
    x64.jcc greater, sgrab
  scopyhdr:
    x64.cmpRegReg rdx, rcx
    x64.jcc less, scopybody
  scommit:
    x64.leaRegRegReg rdx, rax, rcx
    x64.lock cmpxchg [r12], rdx
    x64.setccReg equal, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, sgrab
  spublish:
    x64.leaRegRegImm32 rax, rcx, -1
    x64.leaRegRegReg rcx, r14, rax
    x64.andRegImm32 rcx, rcx, 255
    x64.leaRegRegImm32 rdx, rbx, 240
    x64.loadRegBaseIndexScale.word64 r8, [rdx + rcx*8 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, sanswer
  spubtail:
    x64.leaRegRegImm32 rcx, rbx, 8
    x64.lock add [rcx], rax
  sanswer:
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  scopybody:
    x64.leaRegRegReg rsi, rax, rdx
    x64.andRegImm32 rsi, rsi, 255
    x64.leaRegRegImm32 rdi, r12, 240
    x64.loadRegBaseIndexScale.word64 rsi, [rdi + rsi*8 + 0]
    x64.leaRegRegReg rdi, r14, rdx
    x64.andRegImm32 rdi, rdi, 255
    x64.leaRegRegImm32 r8, rbx, 240
    x64.storeBaseIndexScaleReg.word64 [r8 + rdi*8 + 0], rsi
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp scopyhdr
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
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __sched_active_workers@168 = i64 1
  __sched_max_active_workers@176 = i64 1
  __sched_tls_index@184 = i64 0
  __sched_tls_teb_offset@192 = i64 0
  __sched_procs@200 = i64 0
  __sched_allm@208 = i64 0
  __sched_async_preempt_off@216 = i64 0
  __sched_midle@224 = i64 0
  __sched_pidle@232 = i64 0
  __gt_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __sched_npidle@272 = i64 0
  __sched_runq_size@280 = i64 0
  __sched_global_pushes@288 = i64 0
  __sched_timer_scan_steps@296 = i64 0
  __sched_mcount@304 = i64 0
  __sched_nmidle@312 = i64 0
  __sched_main_m@320 = i64 0
  __sched_phase@328 = i64 0
  __sched_lastpoll@336 = i64 0
  __sched_poll_until@344 = i64 0
  __sched_regain_head@352 = i64 0
  __np_poller@360 = i64 0
  __np_break@368 = i64 0
  __np_wake_sig@376 = i64 0
  __np_pd_table@384 = i64 0
  __np_pd_cap@392 = i64 0
  __np_waiters@400 = i64 0
  __sched_num_procs@408 = i64 0
  __sched_shutdown_flag@416 = i64 0
  __sched_lock@424 = i64 0
  __sched_lock_depth_sink@432 = i64 0
  __sched_nmspinning@440 = i64 0
  __sched_needspinning@448 = i64 0
  __sched_sysmon_event@456 = i64 0
  __sched_sysmon_wait@464 = i64 0
  __sched_timer_starts@472 = i64 0
  __slab_arena_list@480 = i64 0
  __slab_arena_map_l1@488 = i64 0
  __slab_state@496 = i64 0
  __mrt_program_started@504 = i8 0
}

func @main {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#5:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont#4:
    arm64.leaFuncAddr x0, Ping.__loop
    arm64.leaFuncAddr x2, Ping.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x19, x0
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 8], x19
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x19
    arm64.bl __gt_try_await
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.leaRdata x0, __str_rec_1  ; "hits="
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.add x24, x22, x19
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x21, x26, x22
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x21, x19
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_body#7:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont#6:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_2  ; "panic at sched-runqueue.a-thief-reads-the-victims-ring-with-acquire-loads.test:17: the one message this frame sends to a service it owns cannot fail\x0a"
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
    arm64.epilogue 80
    arm64.ret
}

func @Ping.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=hit] @__jumptable_11
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  hit:
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
  __il_body:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x1
    arm64.loadRegBaseDisp.word64 x22, [x19 + 0]
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Ping.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=hit] @__jumptable_12
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  hit:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__sched_steal {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.add x16, x19, 8
    arm64.ldar.word64 x22, [x16]
  sgrab:
    arm64.ldar.word64 x0, [x20]
    arm64.add x16, x20, 8
    arm64.ldar.word64 x1, [x16]
    arm64.sub x1, x1, x0
    arm64.lsr x2, x1, 1
    arm64.sub x1, x1, x2
    arm64.cmp x1, 0
    arm64.b.gt ssized
  sslot:
    arm64.cmp x21, 0
    arm64.cset x0, ne
    arm64.add x16, x20, 88
    arm64.ldar.word64 x23, [x16]
    arm64.cmp x23, 0
    arm64.cset x1, ne
    arm64.and x0, x0, x1
    arm64.cbz x0, sfail
  sslotbusy:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 72]
    arm64.cmp x0, 1
    arm64.b.ne sslottake
  sslotbackoff:
    arm64.movImm x0, 3
    arm64.movImm x1, 0
    arm64.bl mrt_host_netpoll_sleep
  sslottake:
    arm64.add x0, x20, 88
    arm64.movImm x1, 0
    arm64.arm64AtomicCas x0, [x0], x23, x1
    arm64.cmp x0, 0
    arm64.b.eq sgrab
  sslottook:
    arm64.movRegReg x0, x23
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  sfail:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  ssized:
    arm64.cmp x1, 128
    arm64.movImm x2, 0
    arm64.b.gt sgrab
  scopyhdr:
    arm64.cmp x2, x1
    arm64.b.lt scopybody
  scommit:
    arm64.add x2, x0, x1
    arm64.arm64AtomicCas x0, [x20], x0, x2
    arm64.cmp x0, 0
    arm64.b.eq sgrab
  spublish:
    arm64.sub x0, x1, 1
    arm64.add x1, x22, x0
    arm64.and x1, x1, 255
    arm64.add x2, x19, 240
    arm64.loadRegBaseIndexScale.word64 x1, [x2 + x1*8 + 0]
    arm64.cmp x0, 0
    arm64.b.eq sanswer
  spubtail:
    arm64.add x2, x19, 8
    arm64.arm64AtomicRmw.add x0, [x2], x0
  sanswer:
    arm64.movRegReg x0, x1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  scopybody:
    arm64.add x3, x0, x2
    arm64.and x3, x3, 255
    arm64.add x4, x20, 240
    arm64.loadRegBaseIndexScale.word64 x3, [x4 + x3*8 + 0]
    arm64.add x4, x22, x2
    arm64.and x4, x4, 255
    arm64.add x5, x19, 240
    arm64.storeBaseIndexScaleReg.word64 [x5 + x4*8 + 0], x3
    arm64.add x2, x2, 1
    arm64.b scopyhdr
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
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __sched_active_workers@168 = i64 1
  __sched_max_active_workers@176 = i64 1
  __sched_tls_index@184 = i64 0
  __sched_tls_teb_offset@192 = i64 0
  __sched_procs@200 = i64 0
  __sched_allm@208 = i64 0
  __sched_async_preempt_off@216 = i64 0
  __sched_midle@224 = i64 0
  __sched_pidle@232 = i64 0
  __gt_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __sched_npidle@272 = i64 0
  __sched_runq_size@280 = i64 0
  __sched_global_pushes@288 = i64 0
  __sched_timer_scan_steps@296 = i64 0
  __sched_mcount@304 = i64 0
  __sched_nmidle@312 = i64 0
  __sched_main_m@320 = i64 0
  __sched_phase@328 = i64 0
  __sched_lastpoll@336 = i64 0
  __sched_poll_until@344 = i64 0
  __sched_regain_head@352 = i64 0
  __np_poller@360 = i64 0
  __np_break@368 = i64 0
  __np_wake_sig@376 = i64 0
  __np_pd_table@384 = i64 0
  __np_pd_cap@392 = i64 0
  __np_waiters@400 = i64 0
  __sched_num_procs@408 = i64 0
  __sched_shutdown_flag@416 = i64 0
  __sched_lock@424 = i64 0
  __sched_lock_depth_sink@432 = i64 0
  __sched_nmspinning@440 = i64 0
  __sched_needspinning@448 = i64 0
  __sched_sysmon_event@456 = i64 0
  __sched_sysmon_wait@464 = i64 0
  __sched_timer_starts@472 = i64 0
  __mrt_errno@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#5:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont#4:
    arm64.leaFuncAddr x0, Ping.__loop
    arm64.leaFuncAddr x2, Ping.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x19, x0
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 8], x19
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x19
    arm64.bl __gt_try_await
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.leaRdata x0, __str_rec_1  ; "hits="
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.add x24, x22, x19
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x21, x26, x22
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x21, x19
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_body#7:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont#6:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_2  ; "panic at sched-runqueue.a-thief-reads-the-victims-ring-with-acquire-loads.test:17: the one message this frame sends to a service it owns cannot fail\x0a"
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
    arm64.epilogue 80
    arm64.ret
}

func @Ping.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=hit] @__jumptable_11
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  hit:
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
  __il_body:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x1
    arm64.loadRegBaseDisp.word64 x22, [x19 + 0]
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Ping.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=hit] @__jumptable_12
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  hit:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__sched_steal {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.add x16, x19, 8
    arm64.ldar.word64 x22, [x16]
  sgrab:
    arm64.ldar.word64 x0, [x20]
    arm64.add x16, x20, 8
    arm64.ldar.word64 x1, [x16]
    arm64.sub x1, x1, x0
    arm64.lsr x2, x1, 1
    arm64.sub x1, x1, x2
    arm64.cmp x1, 0
    arm64.b.gt ssized
  sslot:
    arm64.cmp x21, 0
    arm64.cset x0, ne
    arm64.add x16, x20, 88
    arm64.ldar.word64 x23, [x16]
    arm64.cmp x23, 0
    arm64.cset x1, ne
    arm64.and x0, x0, x1
    arm64.cbz x0, sfail
  sslotbusy:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 72]
    arm64.cmp x0, 1
    arm64.b.ne sslottake
  sslotbackoff:
    arm64.movImm x0, 3
    arm64.movImm x1, 0
    arm64.bl mrt_host_netpoll_sleep
  sslottake:
    arm64.add x0, x20, 88
    arm64.movImm x1, 0
    arm64.arm64AtomicCas x0, [x0], x23, x1
    arm64.cmp x0, 0
    arm64.b.eq sgrab
  sslottook:
    arm64.movRegReg x0, x23
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  sfail:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  ssized:
    arm64.cmp x1, 128
    arm64.movImm x2, 0
    arm64.b.gt sgrab
  scopyhdr:
    arm64.cmp x2, x1
    arm64.b.lt scopybody
  scommit:
    arm64.add x2, x0, x1
    arm64.arm64AtomicCas x0, [x20], x0, x2
    arm64.cmp x0, 0
    arm64.b.eq sgrab
  spublish:
    arm64.sub x0, x1, 1
    arm64.add x1, x22, x0
    arm64.and x1, x1, 255
    arm64.add x2, x19, 240
    arm64.loadRegBaseIndexScale.word64 x1, [x2 + x1*8 + 0]
    arm64.cmp x0, 0
    arm64.b.eq sanswer
  spubtail:
    arm64.add x2, x19, 8
    arm64.arm64AtomicRmw.add x0, [x2], x0
  sanswer:
    arm64.movRegReg x0, x1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  scopybody:
    arm64.add x3, x0, x2
    arm64.and x3, x3, 255
    arm64.add x4, x20, 240
    arm64.loadRegBaseIndexScale.word64 x3, [x4 + x3*8 + 0]
    arm64.add x4, x22, x2
    arm64.and x4, x4, 255
    arm64.add x5, x19, 240
    arm64.storeBaseIndexScaleReg.word64 [x5 + x4*8 + 0], x3
    arm64.add x2, x2, 1
    arm64.b scopyhdr
}
```
