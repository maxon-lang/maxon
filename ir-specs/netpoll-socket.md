---
feature: netpoll-socket
status: experimental
keywords: [socket, netpoll, poller, deadline, park, concurrency, tcp, green-threads, scheduler, listener, accept]
category: system
---

# A socket operation parks its green thread on the poller: the cases that pin emitted code

The cases of `specs/netpoll-socket.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: netpoll-socket.a-read-deadline-fires -->
**A READ DEADLINE ANSWERS `timedOut`, AND PROMPTLY.** The peer accepts the connection and sends nothing, so a
read with no deadline waits for the kernel's own TCP timeout — minutes. The 300 ms deadline must end it, and
`prompt` is what tells a deadline that fired from one the kernel eventually supplied.

⚠ **THE PEER OUTLIVES THE DEADLINE BECAUSE THE READER IS WHAT ENDS IT.** A peer that closed on a timer of its
own would race the deadline, and a closed connection answers `connectionClosed` — the wrong reading, reached
for the wrong reason. So the peer parks on a read of its own and is released by the reader's close, which can
only happen after the deadline has already fired.

⚠ The `default` arm says *unreachable*, and it has to say so with `panic`: a `default throws` in a match
whose error has nowhere to go is silently discarded, and with a payload-carrying error it leaks the box.

⭐ **THE MARK THIS ASSERTS IS WRITTEN ONLY INTO THE LIFE OF THE RECORD THE READ BEGAN IN.** `timedOut=true` is
`__np_pd_mark_timed_out(fd, gen)` storing into the poll record, and the late exit that stores it can run after a
park of the thread's own; by the time it resumes, the descriptor may name another socket's life of the same
record, whose owner would then read a deadline it never set. So the store is conditional on the record still
carrying the generation the operation was handed by `__np_pd_op_begin`, and the pinned body below shows that
compare ahead of the conditional store.
```maxon
// Far above the 300 ms deadline and far below any kernel-side idle-read timeout: an elapsed reading under
// this can only have come from the deadline itself.
let promptMs = 3000

function holdSilently(listener TcpListener) returns ExitCode
	let conn = try listener.accept() otherwise return 1
	try conn.recv(1024) otherwise ignore

	return 0
end 'holdSilently'

function readPastTheDeadline(listener TcpListener) returns ExitCode throws NetworkError
	let client = try TcpClient.connect("127.0.0.1", port: listener.port())
	try client.setReadDeadline(300)

	var fired = false
	let start = Clock.nowMs()

	try client.recv(1024) otherwise (e) 'readErr'
		match e 'check'
			timedOut then fired = true
			default panic("unreachable: nothing was sent, so only the read deadline can end this recv")
		end 'check'
	end 'readErr'

	let tookMs = Clock.elapsedMs(start)
	client.close()

	print("timedOut={fired} prompt={tookMs < promptMs}\n")
	return 0
end 'readPastTheDeadline'

function exchangeOver(listener TcpListener) returns ExitCode
	let peer = async holdSilently(listener)
	let result = try readPastTheDeadline(listener) otherwise 1
	let held = await peer

	if held != 0 'peer'
		panic("unreachable: the connection whose read ran out of time was accepted by this peer")
	end 'peer'

	return result
end 'exchangeOver'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	return exchangeOver(listener)
end 'main'
```
```stdout
timedOut=true prompt=true
```
```exitcode
0
```
```RequiredRuntime
__np_pd_mark_timed_out
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
  __gt_seed_bytes@104 = i64 8192
  __gt_seed_sum_seen@112 = i64 0
  __gt_seed_count_seen@120 = i64 0
  __gt_seed_guard@128 = i64 0
  __gt_keeps_cpu_clock@136 = i64 0
  __gt_stack_pool@144 = i64 0
  __sched_active_workers@152 = i64 1
  __sched_max_active_workers@160 = i64 1
  __sched_tls_index@168 = i64 0
  __sched_tls_teb_offset@176 = i64 0
  __sched_procs@184 = i64 0
  __sched_allm@192 = i64 0
  __sched_async_preempt_off@200 = i64 0
  __sched_midle@208 = i64 0
  __sched_pidle@216 = i64 0
  __gt_gfree_head@224 = i64 0
  __gt_gfree_nostack_head@232 = i64 0
  __gt_cell_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __gt_gfree_lock@272 = i64 0
  __gt_gfree_lock_owner@280 = i64 0
  __gt_gfree_lock_depth@288 = i64 0
  __sched_npidle@296 = i64 0
  __sched_runq_size@304 = i64 0
  __sched_global_pushes@312 = i64 0
  __sched_timer_scan_steps@320 = i64 0
  __sched_mcount@328 = i64 0
  __sched_nmidle@336 = i64 0
  __sched_nmidlelocked@344 = i64 0
  __sched_main_m@352 = i64 0
  __sched_phase@360 = i64 0
  __sched_lastpoll@368 = i64 0
  __sched_poll_until@376 = i64 0
  __sched_regain_head@384 = i64 0
  __np_poller@392 = i64 0
  __np_break@400 = i64 0
  __np_wake_sig@408 = i64 0
  __np_pd_table@416 = i64 0
  __np_pd_cap@424 = i64 0
  __np_waiters@432 = i64 0
  __np_src_key_free@440 = i64 0
  __np_src_key_high@448 = i64 0
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
  __sched_preempt_ext_lock@536 = i64 0
  __sched_preempt_context@544 = i64 0
  __slab_arena_list@552 = i64 0
  __slab_arena_map_l1@560 = i64 0
  __slab_state@568 = i64 0
  __ms_wsa_started@576 = i64 0
  __mrt_console_probe_stdin@584 = i8 0
  __mrt_console_probe_stdout@585 = i8 0
  __mrt_console_probe_stderr@586 = i8 0
  __mrt_program_started@587 = i8 0
}

func @holdSilently {
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
  __il_body#8:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __ml_accept
    x64.movRegReg r12, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#9
  tryerr#10:
    x64.leaRegRegImm32 r12, r10, -1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __ms_timed_out
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont
  late:
    x64.movRegImm32 rax, 6
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#7
  ifcont:
    x64.jumpTable r12, default=matchdead [[0..1]=matcharm#15, [2]=matcharm#14, [3]=matcharm#15, [4]=matcharm#14, [5]=matcharm#13] @__jumptable_43
  matchdead:
  matcharm#15:
    x64.leaRegRdata rax, [rip + __str_rec_23]  ; "panic at TcpListener.maxon:53: "
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.leaRegRdata rax, [rip + __str_rec_24]  ; "TcpListener.accept: `accept` does not throw `"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 r15, [rax + 8]
    x64.cmpRegImm32 r12, 0
    x64.setccReg equal, rax
    x64.leaRegRdata rcx, [rip + __str_rec_25]  ; "resolveFailed"
    x64.imulRegReg rax, rax, rcx
    x64.cmpRegImm32 r12, 1
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_26]  ; "bindFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r12, 2
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_27]  ; "acceptFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r12, 3
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_28]  ; "addressFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r12, 4
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_29]  ; "closed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r12, 5
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_30]  ; "acceptRetriable"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot7, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot6, rax
    x64.leaRegRdata rcx, [rip + __str_rec_31]  ; "`"
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.storeSlotReg slot5, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.storeSlotReg slot4, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_17]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rsi, [rdx + 0]
    x64.storeSlotReg slot3, rsi
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.storeSlotReg slot2, rdx
    x64.leaRegRegReg rsi, r13, r15
    x64.leaRegRegReg rax, rsi, rax
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, rdx
    x64.storeSlotReg slot0, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot1, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.movRegReg rax, r15
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r15
    x64.loadRegSlot rax, slot6
    x64.loadRegSlot rdx, slot7
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot6
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rdx, slot5
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot4
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rdx, slot3
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot2
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  matcharm#14:
    x64.movRegImm32 rax, 8
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#7
  matcharm#13:
    x64.movRegImm32 rax, 9
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#7
  tryok#9:
    x64.movRegReg rcx, r12
    x64.callDirect __mm_own
    x64.movRegReg rbx, r8
  __il_body#20:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_own
    x64.storeBaseDispReg.word64 [r13 + 0], r8
    x64.movRegReg rcx, rbx
    x64.callDirect __ms_destruct
  __il_cont#19:
    x64.movRegReg rcx, r12
    x64.callDirect __ms_destruct
  exitterminal:
    x64.movRegImm32 rax, 0
    x64.movRegReg rbx, r13
  __il_cont#7:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.movRegImm32 r8, 1
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#1:
    x64.movRegImm32 rdx, 1024
    x64.movRegReg rcx, rbx
    x64.callDirect TcpClient.recv
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, trycont
  tryok#3:
    x64.movRegReg rcx, r8
    x64.callDirect __str_decref
  trycont:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_TcpClient
    x64.movRegReg r8, r12
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
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
    x64.prologue 120
    x64.leaRegRdata rcx, [rip + __str_rec_13]  ; "127.0.0.1"
    x64.movRegImm32 rbx, 0
  __il_body#12:
    x64.callDirect String.addressableBytes
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __ml_bind
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#5
  tryerr#6:
    x64.leaRegRegImm32 rax, r10, -1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, matchdefault#9
  matcharm#7:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg rax, rbx
    x64.movRegImm32 r8, 1
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  matchdefault#9:
    x64.movRegImm32 rbx, 6
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg rax, rbx
    x64.movRegImm32 r8, 1
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#5:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_own
    x64.storeBaseDispReg.word64 [r12 + 0], r8
    x64.movRegReg rcx, rbx
    x64.callDirect __ms_destruct
  __il_body#14:
    x64.movRegReg rcx, r12
    x64.callDirect __mm_own
    x64.movRegReg rbx, r8
    x64.leaRegFunc rcx, [rip + holdSilently]
    x64.callDirect __gt_spawn
    x64.storeSlotReg slot1, r8
    x64.storeBaseDispReg.word64 [r8 + 424], rbx
    x64.leaRegFunc rax, [rip + __gt_release_holdSilently]
    x64.storeBaseDispReg.word64 [r8 + 88], rax
    x64.movRegReg rcx, r8
    x64.callDirect __gt_ready
  __il_body#21:
    x64.leaRegRdata rbx, [rip + __str_rec_13]  ; "127.0.0.1"
  __il_body#35:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __ml_port
    x64.movRegReg r12, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_body#54
  tryerr#37:
    x64.leaRegRdata rcx, [rip + __str_blob_22]  ; "panic at TcpListener.maxon:31: TcpListener.port: this listener is closed, so it holds no bound port to report\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __il_body#54:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic#58
  __rc_chk:
    x64.cmpRegImm32 r12, 65535
    x64.jcc greater, __rc_panic#58
  __rc_ok#57:
    x64.movRegImm32 r13, 0
  __il_body#70:
    x64.movRegReg rcx, rbx
    x64.callDirect String.addressableBytes
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __ms_tcp_connect
    x64.movRegReg r12, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#61
  tryerr#62:
    x64.leaRegRegImm32 rax, r10, -1
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, matchnext
  matcharm#63:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg rax, r12
    x64.jmp exitterminal#64
  matchnext:
    x64.cmpRegImm32 rax, 7
    x64.jcc notEqual, matchdefault#66
  matcharm#65:
    x64.movRegImm32 r12, 5
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg rax, r12
    x64.jmp exitterminal#64
  matchdefault#66:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg rax, r12
  exitterminal#64:
    x64.leaRegRegImm32 rax, rax, 1
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#60
  tryok#61:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_own
    x64.storeBaseDispReg.word64 [rbx + 0], r8
    x64.movRegReg rcx, r12
    x64.callDirect __ms_destruct
  exitterminal#69:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rbx
  __il_cont#60:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#55
  tryerr#56:
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#53
  tryok#55:
    x64.movRegImm32 rax, 0
    x64.movRegReg rbx, rcx
  __il_cont#53:
    x64.storeSlotReg slot2, rbx
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#16
  tryok#22:
    x64.movRegImm32 r12, 300
  __il_body#42:
    x64.loadRegBaseDisp.word64 r13, [rbx + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __mm_retain
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r12
    x64.callDirect __ms_set_read_deadline
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#39
  tryerr#40:
    x64.movRegReg rcx, r14
    x64.callDirect __ms_destruct
  exitterminal#41:
    x64.movRegImm32 rax, 5
    x64.jmp __il_cont#38
  tryok#39:
    x64.movRegReg rcx, r14
    x64.callDirect __ms_destruct
    x64.movRegImm32 rax, 0
  __il_cont#38:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#24
  tryerr#25:
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_TcpClient
  tryerr#16:
    x64.movRegImm32 rbx, 1
    x64.jmp trycont
  tryok#24:
    x64.movRegImm32 r12, 0
    x64.callDirect Clock.nowMs
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1024
    x64.movRegReg rcx, rbx
    x64.callDirect TcpClient.recv
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#27
  tryerr#28:
    x64.leaRegRegImm32 rax, r10, -1
    x64.cmpRegImm32 rax, 5
    x64.jcc notEqual, matchdefault#30
  matcharm#29:
    x64.movRegImm32 r12, 1
    x64.jmp __il_body#46
  matchdefault#30:
    x64.leaRegRdata rcx, [rip + __str_blob_14]  ; "panic at netpoll-socket.a-read-deadline-fires.test:23: unreachable: nothing was sent, so only the read deadline can end this recv\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#27:
    x64.movRegReg rcx, r8
    x64.callDirect __str_decref
  __il_body#46:
    x64.callDirect Clock.nowMs
    x64.cmpRegReg r8, r13
    x64.jcc aboveEqual, ifcont
  clockSkew:
    x64.movRegImm32 r13, 0
    x64.jmp __il_body#51
  ifcont:
    x64.movRegReg rax, r8
    x64.subRegReg rax, r8, r13
    x64.movRegReg rcx, r8
    x64.xorRegImm32 rcx, r8, -1
    x64.andRegReg rcx, rcx, r13
    x64.xorRegReg r8, r8, r13
    x64.xorRegImm32 r8, r8, -1
    x64.andRegReg r8, r8, rax
    x64.orRegReg rcx, rcx, r8
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#50
  __rc_ok#49:
    x64.movRegReg r13, rax
  __il_body#51:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __ms_close
  __il_cont#44:
    x64.leaRegRdata rax, [rip + __str_rec_15]  ; "timedOut="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot10, rcx
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot9, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __bool_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_16]  ; " prompt="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.cmpRegImm32 r13, 3000
    x64.setccReg below, r13
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.callDirect __bool_to_string
    x64.storeSlotReg slot7, r8
    x64.leaRegRdata rax, [rip + __str_rec_17]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rcx, rbx, r12
    x64.leaRegRegReg rcx, rcx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, rbx
    x64.loadRegSlot rdx, slot9
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r14
    x64.loadRegSlot rax, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot5
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rax + 0], r13
    x64.loadRegSlot rcx, slot3
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot9
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
  __il_body#52:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __write_stdout
  __il_cont#43:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot2
    x64.callDirect __destruct_TcpClient
  trycont:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __gt_await
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, __il_cont#13
  peer:
    x64.leaRegRdata rcx, [rip + __str_blob_18]  ; "panic at netpoll-socket.a-read-deadline-fires.test:40: unreachable: the connection whose read ran out of time was accepted by this peer\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __il_cont#13:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __destruct_TcpListener
    x64.movRegReg r8, rbx
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic#58:
    x64.leaRegRdata rcx, [rip + __str_blob_34]  ; "panic at TcpClient.maxon:49: Range check failed: value outside typealias 'NetworkPort'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic#50:
    x64.leaRegRdata rcx, [rip + __str_blob_33]  ; "panic at Clock.maxon:52: Range check failed: value outside typealias 'DurationMs'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
}

func @__np_pd_mark_timed_out {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 56
    x64.movRegReg rbx, rdx
    x64.leaRegGlobal rax, __sched_lock
    x64.leaRegGlobal rdx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.mov rdx, gs:[rdx]
    x64.aluBaseDispImm32.add [rdx + 808], 1
    x64.movRegImm32 rdx, 1
    x64.xchg8 [rax], rdx <- rdx
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __rl_slow#6
  __rl_cont#5:
    x64.callDirect __np_pd_slot
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, npnopd
  npmtknown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 48]
    x64.cmpRegReg rax, rbx
    x64.jcc notEqual, npmtdone
  npmtstore:
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 40], rax
  npmtdone:
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, __rl_slow#8
  __rl_cont#7:
    x64.aluBaseDispImm32.add [rax + 808], -1
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  npnopd:
    x64.leaRegRdata rcx, [rip + __abort_msg_104]  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    x64.movRegImm32 rdx, 54
    x64.movRegImm32 rax, 104
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rl_slow#6:
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rax
    x64.callDirect __rtlock_acquire
    x64.loadRegSlot rcx, slot0
    x64.jmp __rl_cont#5
  __rl_slow#8:
    x64.storeSlotReg slot1, rax
    x64.callDirect __rtlock_release
    x64.loadRegSlot rax, slot1
    x64.jmp __rl_cont#7
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
  __gt_seed_bytes@104 = i64 2048
  __gt_seed_sum_seen@112 = i64 0
  __gt_seed_count_seen@120 = i64 0
  __gt_seed_guard@128 = i64 0
  __gt_keeps_cpu_clock@136 = i64 0
  __gt_stack_pool@144 = i64 0
  __sched_active_workers@152 = i64 1
  __sched_max_active_workers@160 = i64 1
  __sched_tls_index@168 = i64 0
  __sched_tls_teb_offset@176 = i64 0
  __sched_procs@184 = i64 0
  __sched_allm@192 = i64 0
  __sched_async_preempt_off@200 = i64 0
  __sched_midle@208 = i64 0
  __sched_pidle@216 = i64 0
  __gt_gfree_head@224 = i64 0
  __gt_gfree_nostack_head@232 = i64 0
  __gt_cell_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __gt_gfree_lock@272 = i64 0
  __gt_gfree_lock_owner@280 = i64 0
  __gt_gfree_lock_depth@288 = i64 0
  __sched_npidle@296 = i64 0
  __sched_runq_size@304 = i64 0
  __sched_global_pushes@312 = i64 0
  __sched_timer_scan_steps@320 = i64 0
  __sched_mcount@328 = i64 0
  __sched_nmidle@336 = i64 0
  __sched_nmidlelocked@344 = i64 0
  __sched_main_m@352 = i64 0
  __sched_phase@360 = i64 0
  __sched_lastpoll@368 = i64 0
  __sched_poll_until@376 = i64 0
  __sched_regain_head@384 = i64 0
  __np_poller@392 = i64 0
  __np_break@400 = i64 0
  __np_wake_sig@408 = i64 0
  __np_pd_table@416 = i64 0
  __np_pd_cap@424 = i64 0
  __np_waiters@432 = i64 0
  __np_orphan_exited@440 = i64 0
  __np_src_key_free@448 = i64 0
  __np_src_key_high@456 = i64 0
  __np_child_signal@464 = i64 0
  __np_pd_mtable@472 = i64 0
  __np_pd_mcap@480 = i64 0
  __sched_num_procs@488 = i64 0
  __sched_ncpu@496 = i64 0
  __sched_shutdown_flag@504 = i64 0
  __sched_lock@512 = i64 0
  __sched_lock_depth_sink@520 = i64 0
  __sched_nmspinning@528 = i64 0
  __sched_needspinning@536 = i64 0
  __sched_sysmon_event@544 = i64 0
  __sched_sysmon_wait@552 = i64 0
  __sched_timer_starts@560 = i64 0
  __mrt_envp@568 = i64 0
  __mrt_signal_stack_bytes@576 = i64 0
  __mrt_tls_next_key@584 = i64 0
  __slab_arena_list@592 = i64 0
  __slab_arena_map_l1@600 = i64 0
  __slab_state@608 = i64 0
  __ms_wsa_started@616 = i64 0
  __mrt_program_started@624 = i8 0
}

func @holdSilently {
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
  __il_body#8:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __ml_accept
    x64.movRegReg r12, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#9
  tryerr#10:
    x64.leaRegRegImm32 r12, r10, -1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __ms_timed_out
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont
  late:
    x64.movRegImm32 rax, 6
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#7
  ifcont:
    x64.jumpTable r12, default=matchdead [[0..1]=matcharm#15, [2]=matcharm#14, [3]=matcharm#15, [4]=matcharm#14, [5]=matcharm#13] @__jumptable_43
  matchdead:
  matcharm#15:
    x64.leaRegRdata rax, [rip + __str_rec_23]  ; "panic at TcpListener.maxon:53: "
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.leaRegRdata rax, [rip + __str_rec_24]  ; "TcpListener.accept: `accept` does not throw `"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 r15, [rax + 8]
    x64.cmpRegImm32 r12, 0
    x64.setccReg equal, rax
    x64.leaRegRdata rcx, [rip + __str_rec_25]  ; "resolveFailed"
    x64.imulRegReg rax, rax, rcx
    x64.cmpRegImm32 r12, 1
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_26]  ; "bindFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r12, 2
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_27]  ; "acceptFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r12, 3
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_28]  ; "addressFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r12, 4
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_29]  ; "closed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r12, 5
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_30]  ; "acceptRetriable"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot7, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot6, rax
    x64.leaRegRdata rcx, [rip + __str_rec_31]  ; "`"
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.storeSlotReg slot5, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.storeSlotReg slot4, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_17]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rsi, [rdx + 0]
    x64.storeSlotReg slot3, rsi
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.storeSlotReg slot2, rdx
    x64.leaRegRegReg rsi, r13, r15
    x64.leaRegRegReg rax, rsi, rax
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, rdx
    x64.storeSlotReg slot0, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot1, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.movRegReg rax, r15
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r15
    x64.loadRegSlot rax, slot6
    x64.loadRegSlot rdx, slot7
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot6
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rdx, slot5
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot4
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rdx, slot3
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot2
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  matcharm#14:
    x64.movRegImm32 rax, 8
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#7
  matcharm#13:
    x64.movRegImm32 rax, 9
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#7
  tryok#9:
    x64.movRegReg rcx, r12
    x64.callDirect __mm_own
    x64.movRegReg rbx, r8
  __il_body#20:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_own
    x64.storeBaseDispReg.word64 [r13 + 0], r8
    x64.movRegReg rcx, rbx
    x64.callDirect __ms_destruct
  __il_cont#19:
    x64.movRegReg rcx, r12
    x64.callDirect __ms_destruct
  exitterminal:
    x64.movRegImm32 rax, 0
    x64.movRegReg rbx, r13
  __il_cont#7:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.movRegImm32 r8, 1
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#1:
    x64.movRegImm32 rdx, 1024
    x64.movRegReg rcx, rbx
    x64.callDirect TcpClient.recv
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, trycont
  tryok#3:
    x64.movRegReg rcx, r8
    x64.callDirect __str_decref
  trycont:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_TcpClient
    x64.movRegReg r8, r12
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
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
    x64.prologue 120
    x64.leaRegRdata rcx, [rip + __str_rec_13]  ; "127.0.0.1"
    x64.movRegImm32 rbx, 0
  __il_body#12:
    x64.callDirect String.addressableBytes
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __ml_bind
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#5
  tryerr#6:
    x64.leaRegRegImm32 rax, r10, -1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, matchdefault#9
  matcharm#7:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg rax, rbx
    x64.movRegImm32 r8, 1
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  matchdefault#9:
    x64.movRegImm32 rbx, 6
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg rax, rbx
    x64.movRegImm32 r8, 1
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#5:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_own
    x64.storeBaseDispReg.word64 [r12 + 0], r8
    x64.movRegReg rcx, rbx
    x64.callDirect __ms_destruct
  __il_body#14:
    x64.movRegReg rcx, r12
    x64.callDirect __mm_own
    x64.movRegReg rbx, r8
    x64.leaRegFunc rcx, [rip + holdSilently]
    x64.callDirect __gt_spawn
    x64.storeSlotReg slot1, r8
    x64.storeBaseDispReg.word64 [r8 + 424], rbx
    x64.leaRegFunc rax, [rip + __gt_release_holdSilently]
    x64.storeBaseDispReg.word64 [r8 + 88], rax
    x64.movRegReg rcx, r8
    x64.callDirect __gt_ready
  __il_body#21:
    x64.leaRegRdata rbx, [rip + __str_rec_13]  ; "127.0.0.1"
  __il_body#35:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __ml_port
    x64.movRegReg r12, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_body#54
  tryerr#37:
    x64.leaRegRdata rcx, [rip + __str_blob_22]  ; "panic at TcpListener.maxon:31: TcpListener.port: this listener is closed, so it holds no bound port to report\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __il_body#54:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic#58
  __rc_chk:
    x64.cmpRegImm32 r12, 65535
    x64.jcc greater, __rc_panic#58
  __rc_ok#57:
    x64.movRegImm32 r13, 0
  __il_body#70:
    x64.movRegReg rcx, rbx
    x64.callDirect String.addressableBytes
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __ms_tcp_connect
    x64.movRegReg r12, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#61
  tryerr#62:
    x64.leaRegRegImm32 rax, r10, -1
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, matchnext
  matcharm#63:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg rax, r12
    x64.jmp exitterminal#64
  matchnext:
    x64.cmpRegImm32 rax, 7
    x64.jcc notEqual, matchdefault#66
  matcharm#65:
    x64.movRegImm32 r12, 5
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg rax, r12
    x64.jmp exitterminal#64
  matchdefault#66:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg rax, r12
  exitterminal#64:
    x64.leaRegRegImm32 rax, rax, 1
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#60
  tryok#61:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_own
    x64.storeBaseDispReg.word64 [rbx + 0], r8
    x64.movRegReg rcx, r12
    x64.callDirect __ms_destruct
  exitterminal#69:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rbx
  __il_cont#60:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#55
  tryerr#56:
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#53
  tryok#55:
    x64.movRegImm32 rax, 0
    x64.movRegReg rbx, rcx
  __il_cont#53:
    x64.storeSlotReg slot2, rbx
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#16
  tryok#22:
    x64.movRegImm32 r12, 300
  __il_body#42:
    x64.loadRegBaseDisp.word64 r13, [rbx + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __mm_retain
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r12
    x64.callDirect __ms_set_read_deadline
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#39
  tryerr#40:
    x64.movRegReg rcx, r14
    x64.callDirect __ms_destruct
  exitterminal#41:
    x64.movRegImm32 rax, 5
    x64.jmp __il_cont#38
  tryok#39:
    x64.movRegReg rcx, r14
    x64.callDirect __ms_destruct
    x64.movRegImm32 rax, 0
  __il_cont#38:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#24
  tryerr#25:
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_TcpClient
  tryerr#16:
    x64.movRegImm32 rbx, 1
    x64.jmp trycont
  tryok#24:
    x64.movRegImm32 r12, 0
    x64.callDirect Clock.nowMs
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1024
    x64.movRegReg rcx, rbx
    x64.callDirect TcpClient.recv
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#27
  tryerr#28:
    x64.leaRegRegImm32 rax, r10, -1
    x64.cmpRegImm32 rax, 5
    x64.jcc notEqual, matchdefault#30
  matcharm#29:
    x64.movRegImm32 r12, 1
    x64.jmp __il_body#46
  matchdefault#30:
    x64.leaRegRdata rcx, [rip + __str_blob_14]  ; "panic at netpoll-socket.a-read-deadline-fires.test:23: unreachable: nothing was sent, so only the read deadline can end this recv\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#27:
    x64.movRegReg rcx, r8
    x64.callDirect __str_decref
  __il_body#46:
    x64.callDirect Clock.nowMs
    x64.cmpRegReg r8, r13
    x64.jcc aboveEqual, ifcont
  clockSkew:
    x64.movRegImm32 r13, 0
    x64.jmp __il_body#51
  ifcont:
    x64.movRegReg rax, r8
    x64.subRegReg rax, r8, r13
    x64.movRegReg rcx, r8
    x64.xorRegImm32 rcx, r8, -1
    x64.andRegReg rcx, rcx, r13
    x64.xorRegReg r8, r8, r13
    x64.xorRegImm32 r8, r8, -1
    x64.andRegReg r8, r8, rax
    x64.orRegReg rcx, rcx, r8
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#50
  __rc_ok#49:
    x64.movRegReg r13, rax
  __il_body#51:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __ms_close
  __il_cont#44:
    x64.leaRegRdata rax, [rip + __str_rec_15]  ; "timedOut="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot10, rcx
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot9, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __bool_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_16]  ; " prompt="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.cmpRegImm32 r13, 3000
    x64.setccReg below, r13
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.callDirect __bool_to_string
    x64.storeSlotReg slot7, r8
    x64.leaRegRdata rax, [rip + __str_rec_17]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rcx, rbx, r12
    x64.leaRegRegReg rcx, rcx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, rbx
    x64.loadRegSlot rdx, slot9
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r14
    x64.loadRegSlot rax, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot5
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rax + 0], r13
    x64.loadRegSlot rcx, slot3
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot9
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
  __il_body#52:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __write_stdout
  __il_cont#43:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot2
    x64.callDirect __destruct_TcpClient
  trycont:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __gt_await
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, __il_cont#13
  peer:
    x64.leaRegRdata rcx, [rip + __str_blob_18]  ; "panic at netpoll-socket.a-read-deadline-fires.test:40: unreachable: the connection whose read ran out of time was accepted by this peer\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __il_cont#13:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __destruct_TcpListener
    x64.movRegReg r8, rbx
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic#58:
    x64.leaRegRdata rcx, [rip + __str_blob_34]  ; "panic at TcpClient.maxon:49: Range check failed: value outside typealias 'NetworkPort'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic#50:
    x64.leaRegRdata rcx, [rip + __str_blob_33]  ; "panic at Clock.maxon:52: Range check failed: value outside typealias 'DurationMs'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
}

func @__np_pd_mark_timed_out {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 56
    x64.movRegReg rbx, rdx
    x64.leaRegGlobal rax, __sched_lock
    x64.leaRegGlobal rdx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.mov rdx, gs:[rdx]
    x64.aluBaseDispImm32.add [rdx + 808], 1
    x64.movRegImm32 rdx, 1
    x64.xchg8 [rax], rdx <- rdx
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __rl_slow#6
  __rl_cont#5:
    x64.callDirect __np_pd_slot
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, npnopd
  npmtknown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 48]
    x64.cmpRegReg rax, rbx
    x64.jcc notEqual, npmtdone
  npmtstore:
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 40], rax
  npmtdone:
    x64.leaRegGlobal rcx, __sched_lock
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.movRegImm32 rdx, 0
    x64.xchg8 [rcx], rdx <- rdx
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, __rl_slow#8
  __rl_cont#7:
    x64.aluBaseDispImm32.add [rax + 808], -1
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  npnopd:
    x64.leaRegRdata rcx, [rip + __abort_msg_104]  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    x64.movRegImm32 rdx, 54
    x64.movRegImm32 rax, 104
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rl_slow#6:
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rax
    x64.callDirect __rtlock_acquire
    x64.loadRegSlot rcx, slot0
    x64.jmp __rl_cont#5
  __rl_slow#8:
    x64.storeSlotReg slot1, rax
    x64.callDirect __rtlock_release
    x64.loadRegSlot rax, slot1
    x64.jmp __rl_cont#7
}
```

```TargetIr:arm64-macos
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
  __gt_seed_bytes@104 = i64 2048
  __gt_seed_sum_seen@112 = i64 0
  __gt_seed_count_seen@120 = i64 0
  __gt_seed_guard@128 = i64 0
  __gt_keeps_cpu_clock@136 = i64 0
  __gt_stack_pool@144 = i64 0
  __sched_active_workers@152 = i64 1
  __sched_max_active_workers@160 = i64 1
  __sched_tls_index@168 = i64 0
  __sched_tls_teb_offset@176 = i64 0
  __sched_procs@184 = i64 0
  __sched_allm@192 = i64 0
  __sched_async_preempt_off@200 = i64 0
  __sched_midle@208 = i64 0
  __sched_pidle@216 = i64 0
  __gt_gfree_head@224 = i64 0
  __gt_gfree_nostack_head@232 = i64 0
  __gt_cell_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __gt_gfree_lock@272 = i64 0
  __gt_gfree_lock_owner@280 = i64 0
  __gt_gfree_lock_depth@288 = i64 0
  __sched_npidle@296 = i64 0
  __sched_runq_size@304 = i64 0
  __sched_global_pushes@312 = i64 0
  __sched_timer_scan_steps@320 = i64 0
  __sched_mcount@328 = i64 0
  __sched_nmidle@336 = i64 0
  __sched_nmidlelocked@344 = i64 0
  __sched_main_m@352 = i64 0
  __sched_phase@360 = i64 0
  __sched_lastpoll@368 = i64 0
  __sched_poll_until@376 = i64 0
  __sched_regain_head@384 = i64 0
  __np_poller@392 = i64 0
  __np_break@400 = i64 0
  __np_wake_sig@408 = i64 0
  __np_pd_table@416 = i64 0
  __np_pd_cap@424 = i64 0
  __np_waiters@432 = i64 0
  __np_orphan_exited@440 = i64 0
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
  __slab_arena_list@528 = i64 0
  __slab_arena_map_l1@536 = i64 0
  __slab_state@544 = i64 0
  __ms_wsa_started@552 = i64 0
  __mrt_program_started@560 = i8 0
}

func @holdSilently {
  entry:
    arm64.prologue 128
    arm64.storeSlotReg slot12, x28
    arm64.storeSlotReg slot11, x27
    arm64.storeSlotReg slot10, x26
    arm64.storeSlotReg slot9, x25
    arm64.storeSlotReg slot8, x24
    arm64.storeSlotReg slot7, x23
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movRegReg x19, x0
  __il_body#8:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __ml_accept
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#9
  tryerr#10:
    arm64.sub x20, x9, 1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __ms_timed_out
    arm64.cbz x0, ifcont
  late:
    arm64.movImm x0, 6
    arm64.movImm x19, 0
    arm64.b __il_cont#7
  ifcont:
    arm64.jumpTable x20, default=matchdead [[0..1]=matcharm#15, [2]=matcharm#14, [3]=matcharm#15, [4]=matcharm#14, [5]=matcharm#13] @__jumptable_43
  matchdead:
  matcharm#15:
    arm64.leaRdata x0, __str_rec_23  ; "panic at TcpListener.maxon:53: "
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.leaRdata x0, __str_rec_24  ; "TcpListener.accept: `accept` does not throw `"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.cmp x20, 0
    arm64.cset x0, eq
    arm64.leaRdata x1, __str_rec_25  ; "resolveFailed"
    arm64.mul x0, x0, x1
    arm64.cmp x20, 1
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_26  ; "bindFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x20, 2
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_27  ; "acceptFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x20, 3
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_28  ; "addressFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x20, 4
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_29  ; "closed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x20, 5
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_30  ; "acceptRetriable"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.leaRdata x0, __str_rec_31  ; "`"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_17  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot2, x0
    arm64.add x1, x21, x23
    arm64.add x1, x1, x24
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot0, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot1, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x28, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x19, x19, x24
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x19, x19, x26
    arm64.loadRegSlot x2, slot2
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot2
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot0
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.retIndirect
  matcharm#14:
    arm64.movImm x0, 8
    arm64.movImm x19, 0
    arm64.b __il_cont#7
  matcharm#13:
    arm64.movImm x0, 9
    arm64.movImm x19, 0
    arm64.b __il_cont#7
  tryok#9:
    arm64.movRegReg x0, x20
    arm64.bl __mm_own
    arm64.movRegReg x19, x0
  __il_body#20:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __ms_destruct
  __il_cont#19:
    arm64.movRegReg x0, x20
    arm64.bl __ms_destruct
  exitterminal:
    arm64.movImm x0, 0
    arm64.movRegReg x19, x21
  __il_cont#7:
    arm64.cmp x0, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.retIndirect
  tryok#1:
    arm64.movImm x1, 1024
    arm64.movRegReg x0, x19
    arm64.bl TcpClient.recv
    arm64.cmp x9, 0
    arm64.b.ne trycont
  tryok#3:
    arm64.bl __str_decref
  trycont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __destruct_TcpClient
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.retIndirect
}

func @main {
  entry:
    arm64.prologue 144
    arm64.storeSlotReg slot15, x28
    arm64.storeSlotReg slot14, x27
    arm64.storeSlotReg slot13, x26
    arm64.storeSlotReg slot12, x25
    arm64.storeSlotReg slot11, x24
    arm64.storeSlotReg slot10, x23
    arm64.storeSlotReg slot9, x22
    arm64.storeSlotReg slot8, x21
    arm64.storeSlotReg slot7, x20
    arm64.storeSlotReg slot6, x19
    arm64.leaRdata x0, __str_rec_13  ; "127.0.0.1"
    arm64.movImm x19, 0
  __il_body#12:
    arm64.bl String.addressableBytes
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __ml_bind
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#5
  tryerr#6:
    arm64.sub x0, x9, 1
    arm64.cmp x0, 0
    arm64.b.ne matchdefault#9
  matcharm#7:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  matchdefault#9:
    arm64.movImm x19, 6
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  tryok#5:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot0, x20
    arm64.movRegReg x0, x19
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __ms_destruct
  __il_body#14:
    arm64.movRegReg x0, x20
    arm64.bl __mm_own
    arm64.movRegReg x19, x0
    arm64.leaFuncAddr x0, holdSilently
    arm64.bl __gt_spawn
    arm64.storeSlotReg slot1, x0
    arm64.storeBaseDispReg.word64 [x0 + 424], x19
    arm64.leaFuncAddr x1, __gt_release_holdSilently
    arm64.storeBaseDispReg.word64 [x0 + 88], x1
    arm64.bl __gt_ready
  __il_body#21:
    arm64.leaRdata x19, __str_rec_13  ; "127.0.0.1"
  __il_body#35:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __ml_port
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.eq __il_body#54
  tryerr#37:
    arm64.leaRdata x0, __str_blob_22  ; "panic at TcpListener.maxon:31: TcpListener.port: this listener is closed, so it holds no bound port to report\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  __il_body#54:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#58
  __rc_chk:
    arm64.movImm x16, 65535
    arm64.cmp x20, x16
    arm64.b.gt __rc_panic#58
  __rc_ok#57:
    arm64.movImm x21, 0
  __il_body#70:
    arm64.movRegReg x0, x19
    arm64.bl String.addressableBytes
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __ms_tcp_connect
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#61
  tryerr#62:
    arm64.sub x0, x9, 1
    arm64.cmp x0, 1
    arm64.b.ne matchnext
  matcharm#63:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.b exitterminal#64
  matchnext:
    arm64.cmp x0, 7
    arm64.b.ne matchdefault#66
  matcharm#65:
    arm64.movImm x20, 5
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.b exitterminal#64
  matchdefault#66:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
  exitterminal#64:
    arm64.add x0, x0, 1
    arm64.movImm x1, 0
    arm64.b __il_cont#60
  tryok#61:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __ms_destruct
  exitterminal#69:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x19
  __il_cont#60:
    arm64.cmp x0, 0
    arm64.b.eq tryok#55
  tryerr#56:
    arm64.movImm x19, 0
    arm64.b __il_cont#53
  tryok#55:
    arm64.movImm x0, 0
    arm64.movRegReg x19, x1
  __il_cont#53:
    arm64.storeSlotReg slot2, x19
    arm64.cmp x0, 0
    arm64.b.ne tryerr#16
  tryok#22:
    arm64.movImm x20, 300
  __il_body#42:
    arm64.loadRegBaseDisp.word64 x21, [x19 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x20
    arm64.bl __ms_set_read_deadline
    arm64.cmp x9, 0
    arm64.b.eq tryok#39
  tryerr#40:
    arm64.movRegReg x0, x22
    arm64.bl __ms_destruct
  exitterminal#41:
    arm64.movImm x0, 5
    arm64.b __il_cont#38
  tryok#39:
    arm64.movRegReg x0, x22
    arm64.bl __ms_destruct
    arm64.movImm x0, 0
  __il_cont#38:
    arm64.cmp x0, 0
    arm64.b.eq tryok#24
  tryerr#25:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_TcpClient
  tryerr#16:
    arm64.movImm x19, 1
    arm64.b trycont
  tryok#24:
    arm64.movImm x20, 0
    arm64.bl Clock.nowMs
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1024
    arm64.movRegReg x0, x19
    arm64.bl TcpClient.recv
    arm64.cmp x9, 0
    arm64.b.eq tryok#27
  tryerr#28:
    arm64.sub x0, x9, 1
    arm64.cmp x0, 5
    arm64.b.ne matchdefault#30
  matcharm#29:
    arm64.movImm x20, 1
    arm64.b __il_body#46
  matchdefault#30:
    arm64.leaRdata x0, __str_blob_14  ; "panic at netpoll-socket.a-read-deadline-fires.test:23: unreachable: nothing was sent, so only the read deadline can end this recv\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  tryok#27:
    arm64.bl __str_decref
  __il_body#46:
    arm64.bl Clock.nowMs
    arm64.cmp x0, x21
    arm64.b.hs ifcont
  clockSkew:
    arm64.movImm x21, 0
    arm64.b __il_body#51
  ifcont:
    arm64.sub x22, x0, x21
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x0, x16
    arm64.and x1, x1, x21
    arm64.eor x0, x0, x21
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x0, x16
    arm64.and x0, x0, x22
    arm64.orr x0, x1, x0
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#50
  __rc_ok#49:
    arm64.movRegReg x21, x22
  __il_body#51:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __ms_close
  __il_cont#44:
    arm64.leaRdata x0, __str_rec_15  ; "timedOut="
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.bl __bool_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_16  ; " prompt="
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.cmp x21, 3000
    arm64.cset x21, lo
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x26
    arm64.bl __bool_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_17  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot5, x0
    arm64.add x1, x22, x20
    arm64.add x1, x1, x25
    arm64.add x1, x1, x21
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot3, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot4, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x19, x28, x22
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x19, x21
    arm64.loadRegSlot x2, slot5
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot5
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot4
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
    arm64.movRegReg x0, x26
    arm64.bl __mm_decref
  __il_body#52:
    arm64.loadRegSlot x0, slot4
    arm64.bl __write_stdout
  __il_cont#43:
    arm64.loadRegSlot x0, slot4
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_TcpClient
  trycont:
    arm64.loadRegSlot x0, slot1
    arm64.bl __gt_await
    arm64.cmp x0, 0
    arm64.b.eq __il_cont#13
  peer:
    arm64.leaRdata x0, __str_blob_18  ; "panic at netpoll-socket.a-read-deadline-fires.test:40: unreachable: the connection whose read ran out of time was accepted by this peer\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  __il_cont#13:
    arm64.loadRegSlot x0, slot0
    arm64.bl __destruct_TcpListener
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  __rc_panic#58:
    arm64.leaRdata x0, __str_blob_34  ; "panic at TcpClient.maxon:49: Range check failed: value outside typealias 'NetworkPort'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  __rc_panic#50:
    arm64.leaRdata x0, __str_blob_33  ; "panic at Clock.maxon:52: Range check failed: value outside typealias 'DurationMs'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
}

func @__np_pd_mark_timed_out {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x19
    arm64.movRegReg x19, x1
    arm64.leaGlobal x1, __sched_lock
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.loadRegBaseDisp.word64 x3, [x2 + 808]
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x2 + 808], x3
    arm64.movImm x2, 1
    arm64.arm64AtomicRmw.exchangeByte x2, [x1], x2
    arm64.cmp x2, 0
    arm64.b.ne __rl_slow#6
  __rl_cont#5:
    arm64.bl __np_pd_slot
    arm64.cmp x0, 0
    arm64.b.eq npnopd
  npmtknown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 48]
    arm64.cmp x1, x19
    arm64.b.ne npmtdone
  npmtstore:
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 40], x1
  npmtdone:
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
    arm64.b.ne __rl_slow#8
  __rl_cont#7:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 808]
    arm64.add x2, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 808], x2
    arm64.loadRegSlot x19, slot2
    arm64.epilogue 48
    arm64.ret
  npnopd:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot2
    arm64.epilogue 48
    arm64.ret
  __rl_slow#6:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_acquire
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __rl_cont#5
  __rl_slow#8:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_release
    arm64.loadRegSlot x1, slot1
    arm64.b __rl_cont#7
}
```

```TargetIr:arm64-linux
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
  __gt_seed_bytes@104 = i64 2048
  __gt_seed_sum_seen@112 = i64 0
  __gt_seed_count_seen@120 = i64 0
  __gt_seed_guard@128 = i64 0
  __gt_keeps_cpu_clock@136 = i64 0
  __gt_stack_pool@144 = i64 0
  __sched_active_workers@152 = i64 1
  __sched_max_active_workers@160 = i64 1
  __sched_tls_index@168 = i64 0
  __sched_tls_teb_offset@176 = i64 0
  __sched_procs@184 = i64 0
  __sched_allm@192 = i64 0
  __sched_async_preempt_off@200 = i64 0
  __sched_midle@208 = i64 0
  __sched_pidle@216 = i64 0
  __gt_gfree_head@224 = i64 0
  __gt_gfree_nostack_head@232 = i64 0
  __gt_cell_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __gt_gfree_lock@272 = i64 0
  __gt_gfree_lock_owner@280 = i64 0
  __gt_gfree_lock_depth@288 = i64 0
  __sched_npidle@296 = i64 0
  __sched_runq_size@304 = i64 0
  __sched_global_pushes@312 = i64 0
  __sched_timer_scan_steps@320 = i64 0
  __sched_mcount@328 = i64 0
  __sched_nmidle@336 = i64 0
  __sched_nmidlelocked@344 = i64 0
  __sched_main_m@352 = i64 0
  __sched_phase@360 = i64 0
  __sched_lastpoll@368 = i64 0
  __sched_poll_until@376 = i64 0
  __sched_regain_head@384 = i64 0
  __np_poller@392 = i64 0
  __np_break@400 = i64 0
  __np_wake_sig@408 = i64 0
  __np_pd_table@416 = i64 0
  __np_pd_cap@424 = i64 0
  __np_waiters@432 = i64 0
  __np_orphan_exited@440 = i64 0
  __np_src_key_free@448 = i64 0
  __np_src_key_high@456 = i64 0
  __np_child_signal@464 = i64 0
  __np_pd_mtable@472 = i64 0
  __np_pd_mcap@480 = i64 0
  __sched_num_procs@488 = i64 0
  __sched_ncpu@496 = i64 0
  __sched_shutdown_flag@504 = i64 0
  __sched_lock@512 = i64 0
  __sched_lock_depth_sink@520 = i64 0
  __sched_nmspinning@528 = i64 0
  __sched_needspinning@536 = i64 0
  __sched_sysmon_event@544 = i64 0
  __sched_sysmon_wait@552 = i64 0
  __sched_timer_starts@560 = i64 0
  __mrt_envp@568 = i64 0
  __mrt_signal_stack_bytes@576 = i64 0
  __mrt_hwcap_atomics@584 = i64 0
  __mrt_tls_next_key@592 = i64 0
  __slab_arena_list@600 = i64 0
  __slab_arena_map_l1@608 = i64 0
  __slab_state@616 = i64 0
  __ms_wsa_started@624 = i64 0
  __mrt_program_started@632 = i8 0
}

func @holdSilently {
  entry:
    arm64.prologue 128
    arm64.storeSlotReg slot12, x28
    arm64.storeSlotReg slot11, x27
    arm64.storeSlotReg slot10, x26
    arm64.storeSlotReg slot9, x25
    arm64.storeSlotReg slot8, x24
    arm64.storeSlotReg slot7, x23
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movRegReg x19, x0
  __il_body#8:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __ml_accept
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#9
  tryerr#10:
    arm64.sub x20, x9, 1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __ms_timed_out
    arm64.cbz x0, ifcont
  late:
    arm64.movImm x0, 6
    arm64.movImm x19, 0
    arm64.b __il_cont#7
  ifcont:
    arm64.jumpTable x20, default=matchdead [[0..1]=matcharm#15, [2]=matcharm#14, [3]=matcharm#15, [4]=matcharm#14, [5]=matcharm#13] @__jumptable_43
  matchdead:
  matcharm#15:
    arm64.leaRdata x0, __str_rec_23  ; "panic at TcpListener.maxon:53: "
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.leaRdata x0, __str_rec_24  ; "TcpListener.accept: `accept` does not throw `"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.cmp x20, 0
    arm64.cset x0, eq
    arm64.leaRdata x1, __str_rec_25  ; "resolveFailed"
    arm64.mul x0, x0, x1
    arm64.cmp x20, 1
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_26  ; "bindFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x20, 2
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_27  ; "acceptFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x20, 3
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_28  ; "addressFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x20, 4
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_29  ; "closed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x20, 5
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_30  ; "acceptRetriable"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.leaRdata x0, __str_rec_31  ; "`"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_17  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot2, x0
    arm64.add x1, x21, x23
    arm64.add x1, x1, x24
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot0, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot1, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x28, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x19, x19, x24
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x19, x19, x26
    arm64.loadRegSlot x2, slot2
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot2
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot0
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.retIndirect
  matcharm#14:
    arm64.movImm x0, 8
    arm64.movImm x19, 0
    arm64.b __il_cont#7
  matcharm#13:
    arm64.movImm x0, 9
    arm64.movImm x19, 0
    arm64.b __il_cont#7
  tryok#9:
    arm64.movRegReg x0, x20
    arm64.bl __mm_own
    arm64.movRegReg x19, x0
  __il_body#20:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __ms_destruct
  __il_cont#19:
    arm64.movRegReg x0, x20
    arm64.bl __ms_destruct
  exitterminal:
    arm64.movImm x0, 0
    arm64.movRegReg x19, x21
  __il_cont#7:
    arm64.cmp x0, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.retIndirect
  tryok#1:
    arm64.movImm x1, 1024
    arm64.movRegReg x0, x19
    arm64.bl TcpClient.recv
    arm64.cmp x9, 0
    arm64.b.ne trycont
  tryok#3:
    arm64.bl __str_decref
  trycont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __destruct_TcpClient
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.retIndirect
}

func @main {
  entry:
    arm64.prologue 144
    arm64.storeSlotReg slot15, x28
    arm64.storeSlotReg slot14, x27
    arm64.storeSlotReg slot13, x26
    arm64.storeSlotReg slot12, x25
    arm64.storeSlotReg slot11, x24
    arm64.storeSlotReg slot10, x23
    arm64.storeSlotReg slot9, x22
    arm64.storeSlotReg slot8, x21
    arm64.storeSlotReg slot7, x20
    arm64.storeSlotReg slot6, x19
    arm64.leaRdata x0, __str_rec_13  ; "127.0.0.1"
    arm64.movImm x19, 0
  __il_body#12:
    arm64.bl String.addressableBytes
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __ml_bind
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#5
  tryerr#6:
    arm64.sub x0, x9, 1
    arm64.cmp x0, 0
    arm64.b.ne matchdefault#9
  matcharm#7:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  matchdefault#9:
    arm64.movImm x19, 6
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  tryok#5:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot0, x20
    arm64.movRegReg x0, x19
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __ms_destruct
  __il_body#14:
    arm64.movRegReg x0, x20
    arm64.bl __mm_own
    arm64.movRegReg x19, x0
    arm64.leaFuncAddr x0, holdSilently
    arm64.bl __gt_spawn
    arm64.storeSlotReg slot1, x0
    arm64.storeBaseDispReg.word64 [x0 + 424], x19
    arm64.leaFuncAddr x1, __gt_release_holdSilently
    arm64.storeBaseDispReg.word64 [x0 + 88], x1
    arm64.bl __gt_ready
  __il_body#21:
    arm64.leaRdata x19, __str_rec_13  ; "127.0.0.1"
  __il_body#35:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __ml_port
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.eq __il_body#54
  tryerr#37:
    arm64.leaRdata x0, __str_blob_22  ; "panic at TcpListener.maxon:31: TcpListener.port: this listener is closed, so it holds no bound port to report\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  __il_body#54:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#58
  __rc_chk:
    arm64.movImm x16, 65535
    arm64.cmp x20, x16
    arm64.b.gt __rc_panic#58
  __rc_ok#57:
    arm64.movImm x21, 0
  __il_body#70:
    arm64.movRegReg x0, x19
    arm64.bl String.addressableBytes
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __ms_tcp_connect
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#61
  tryerr#62:
    arm64.sub x0, x9, 1
    arm64.cmp x0, 1
    arm64.b.ne matchnext
  matcharm#63:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.b exitterminal#64
  matchnext:
    arm64.cmp x0, 7
    arm64.b.ne matchdefault#66
  matcharm#65:
    arm64.movImm x20, 5
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.b exitterminal#64
  matchdefault#66:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
  exitterminal#64:
    arm64.add x0, x0, 1
    arm64.movImm x1, 0
    arm64.b __il_cont#60
  tryok#61:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __ms_destruct
  exitterminal#69:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x19
  __il_cont#60:
    arm64.cmp x0, 0
    arm64.b.eq tryok#55
  tryerr#56:
    arm64.movImm x19, 0
    arm64.b __il_cont#53
  tryok#55:
    arm64.movImm x0, 0
    arm64.movRegReg x19, x1
  __il_cont#53:
    arm64.storeSlotReg slot2, x19
    arm64.cmp x0, 0
    arm64.b.ne tryerr#16
  tryok#22:
    arm64.movImm x20, 300
  __il_body#42:
    arm64.loadRegBaseDisp.word64 x21, [x19 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x20
    arm64.bl __ms_set_read_deadline
    arm64.cmp x9, 0
    arm64.b.eq tryok#39
  tryerr#40:
    arm64.movRegReg x0, x22
    arm64.bl __ms_destruct
  exitterminal#41:
    arm64.movImm x0, 5
    arm64.b __il_cont#38
  tryok#39:
    arm64.movRegReg x0, x22
    arm64.bl __ms_destruct
    arm64.movImm x0, 0
  __il_cont#38:
    arm64.cmp x0, 0
    arm64.b.eq tryok#24
  tryerr#25:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_TcpClient
  tryerr#16:
    arm64.movImm x19, 1
    arm64.b trycont
  tryok#24:
    arm64.movImm x20, 0
    arm64.bl Clock.nowMs
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1024
    arm64.movRegReg x0, x19
    arm64.bl TcpClient.recv
    arm64.cmp x9, 0
    arm64.b.eq tryok#27
  tryerr#28:
    arm64.sub x0, x9, 1
    arm64.cmp x0, 5
    arm64.b.ne matchdefault#30
  matcharm#29:
    arm64.movImm x20, 1
    arm64.b __il_body#46
  matchdefault#30:
    arm64.leaRdata x0, __str_blob_14  ; "panic at netpoll-socket.a-read-deadline-fires.test:23: unreachable: nothing was sent, so only the read deadline can end this recv\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  tryok#27:
    arm64.bl __str_decref
  __il_body#46:
    arm64.bl Clock.nowMs
    arm64.cmp x0, x21
    arm64.b.hs ifcont
  clockSkew:
    arm64.movImm x21, 0
    arm64.b __il_body#51
  ifcont:
    arm64.sub x22, x0, x21
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x0, x16
    arm64.and x1, x1, x21
    arm64.eor x0, x0, x21
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x0, x16
    arm64.and x0, x0, x22
    arm64.orr x0, x1, x0
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#50
  __rc_ok#49:
    arm64.movRegReg x21, x22
  __il_body#51:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __ms_close
  __il_cont#44:
    arm64.leaRdata x0, __str_rec_15  ; "timedOut="
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.bl __bool_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_16  ; " prompt="
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.cmp x21, 3000
    arm64.cset x21, lo
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x26
    arm64.bl __bool_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_17  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot5, x0
    arm64.add x1, x22, x20
    arm64.add x1, x1, x25
    arm64.add x1, x1, x21
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot3, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot4, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x19, x28, x22
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x19, x21
    arm64.loadRegSlot x2, slot5
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot5
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot4
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
    arm64.movRegReg x0, x26
    arm64.bl __mm_decref
  __il_body#52:
    arm64.loadRegSlot x0, slot4
    arm64.bl __write_stdout
  __il_cont#43:
    arm64.loadRegSlot x0, slot4
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_TcpClient
  trycont:
    arm64.loadRegSlot x0, slot1
    arm64.bl __gt_await
    arm64.cmp x0, 0
    arm64.b.eq __il_cont#13
  peer:
    arm64.leaRdata x0, __str_blob_18  ; "panic at netpoll-socket.a-read-deadline-fires.test:40: unreachable: the connection whose read ran out of time was accepted by this peer\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  __il_cont#13:
    arm64.loadRegSlot x0, slot0
    arm64.bl __destruct_TcpListener
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  __rc_panic#58:
    arm64.leaRdata x0, __str_blob_34  ; "panic at TcpClient.maxon:49: Range check failed: value outside typealias 'NetworkPort'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
  __rc_panic#50:
    arm64.leaRdata x0, __str_blob_33  ; "panic at Clock.maxon:52: Range check failed: value outside typealias 'DurationMs'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.retIndirect
}

func @__np_pd_mark_timed_out {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x19
    arm64.movRegReg x19, x1
    arm64.leaGlobal x1, __sched_lock
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.loadRegBaseDisp.word64 x3, [x2 + 808]
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x2 + 808], x3
    arm64.movImm x2, 1
    arm64.arm64AtomicRmw.exchangeByte x2, [x1], x2
    arm64.cmp x2, 0
    arm64.b.ne __rl_slow#6
  __rl_cont#5:
    arm64.bl __np_pd_slot
    arm64.cmp x0, 0
    arm64.b.eq npnopd
  npmtknown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 48]
    arm64.cmp x1, x19
    arm64.b.ne npmtdone
  npmtstore:
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 40], x1
  npmtdone:
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
    arm64.b.ne __rl_slow#8
  __rl_cont#7:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 808]
    arm64.add x2, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 808], x2
    arm64.loadRegSlot x19, slot2
    arm64.epilogue 48
    arm64.ret
  npnopd:
    arm64.leaRdata x0, __abort_msg_104  ; "fatal error: runtime abort 104 (netpollUnknownSource)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 104
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot2
    arm64.epilogue 48
    arm64.ret
  __rl_slow#6:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_acquire
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __rl_cont#5
  __rl_slow#8:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x1, x2
    arm64.bl __rtlock_release
    arm64.loadRegSlot x1, slot1
    arm64.b __rl_cont#7
}
```

<!-- test: netpoll-socket.a-send-the-kernel-finished-is-not-sent-again-past-its-deadline -->
<!-- procs: 1 -->
**A SEND THE KERNEL FINISHED IS ANSWERED WITH ITS COUNT, WHATEVER ITS DEADLINE SAYS.** The write side of the
rule above. A send that completed before its cancel has already put its bytes on the wire, so `timedOut` tells a
program that retries a timeout to send them again, and the peer receives them twice.

⭐ The race is built the same way: the deadline has passed and the send buffer has room before the send is
issued, so the send completes the moment it is issued while its wait finds the deadline already expired.

⛔ **THE PACKET IS A `(status, bytes)` PAIR, AND THE PAIR IS THE ANSWER, NOT THE STATUS ALONE.** A `WSASend` the
cancel caught after part of its buffer was copied to the kernel completes cancelled with a non-zero count, and
those bytes are on the wire; the runtime answers it as a short send with that count and no `timedOut` mark, and the
deadline reports on the next call, which moves nothing. The receive twin — a cancelled `WSARecv` that had landed
bytes — is the same block. The pinned `__ms_send_from` body shows the cancelled road testing the byte count before
it chooses the `timedOut` exit. On x64-windows loopback the kernel accepts an 8, 64 or 256 MiB overlapped send
whole with the peer reading nothing, so no spec program can park a send for a cancel to catch mid-copy, and the
pinned body is the evidence.
```maxon
function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let dialing = async TcpClient.connect("127.0.0.1", port: listener.port())
	let peer = try listener.accept() otherwise return 1
	let client = try await dialing otherwise return 1

	try client.setWriteDeadline(1) otherwise return 1
	sleep(100)

	var reported = 0 as BytePos
	var timeouts = 0 as Count

	while reported == 0 'sending'
		if let sent = try client.send("xyz") 'sent'
			reported = sent
		end 'sent' else (e) 'failed'
			match e 'why'
				timedOut then timeouts = timeouts + 1
				default panic("unreachable: a send with room in its buffer ends in its bytes or its deadline")
			end 'why'

			try client.setWriteDeadline(0) otherwise panic("unreachable: the sending side is still open")
		end 'failed'
	end 'sending'

	client.close()

	var heard = ""
	var open = true

	while open 'reading'
		if let chunk = try peer.recv(1024) 'got'
			heard = "{heard}{chunk}"
		end 'got' else 'closed'
			open = false
		end 'closed'
	end 'reading'

	print("reported={reported} timeouts={timeouts} heard={heard}\n")
	return 0
end 'main'
```
```stdout
reported=3 timeouts=0 heard=xyz
```
```exitcode
0
```
```RequiredRuntime
__ms_send_from
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
  __gt_seed_bytes@104 = i64 8192
  __gt_seed_sum_seen@112 = i64 0
  __gt_seed_count_seen@120 = i64 0
  __gt_seed_guard@128 = i64 0
  __gt_keeps_cpu_clock@136 = i64 0
  __gt_stack_pool@144 = i64 0
  __sched_active_workers@152 = i64 1
  __sched_max_active_workers@160 = i64 1
  __sched_tls_index@168 = i64 0
  __sched_tls_teb_offset@176 = i64 0
  __sched_procs@184 = i64 0
  __sched_allm@192 = i64 0
  __sched_async_preempt_off@200 = i64 0
  __sched_midle@208 = i64 0
  __sched_pidle@216 = i64 0
  __gt_gfree_head@224 = i64 0
  __gt_gfree_nostack_head@232 = i64 0
  __gt_cell_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __gt_gfree_lock@272 = i64 0
  __gt_gfree_lock_owner@280 = i64 0
  __gt_gfree_lock_depth@288 = i64 0
  __sched_npidle@296 = i64 0
  __sched_runq_size@304 = i64 0
  __sched_global_pushes@312 = i64 0
  __sched_timer_scan_steps@320 = i64 0
  __sched_mcount@328 = i64 0
  __sched_nmidle@336 = i64 0
  __sched_nmidlelocked@344 = i64 0
  __sched_main_m@352 = i64 0
  __sched_phase@360 = i64 0
  __sched_lastpoll@368 = i64 0
  __sched_poll_until@376 = i64 0
  __sched_regain_head@384 = i64 0
  __np_poller@392 = i64 0
  __np_break@400 = i64 0
  __np_wake_sig@408 = i64 0
  __np_pd_table@416 = i64 0
  __np_pd_cap@424 = i64 0
  __np_waiters@432 = i64 0
  __np_src_key_free@440 = i64 0
  __np_src_key_high@448 = i64 0
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
  __sched_preempt_ext_lock@536 = i64 0
  __sched_preempt_context@544 = i64 0
  __slab_arena_list@552 = i64 0
  __slab_arena_map_l1@560 = i64 0
  __slab_state@568 = i64 0
  __ms_wsa_started@576 = i64 0
  __mrt_console_probe_stdin@584 = i8 0
  __mrt_console_probe_stdout@585 = i8 0
  __mrt_console_probe_stderr@586 = i8 0
  __mrt_program_started@587 = i8 0
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
    x64.prologue 344
    x64.leaRegRdata rcx, [rip + __str_rec_16]  ; "127.0.0.1"
    x64.movRegImm32 rbx, 0
  __il_body#38:
    x64.callDirect String.addressableBytes
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __ml_bind
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#31
  tryerr#32:
    x64.leaRegRegImm32 rax, r10, -1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, matchdefault#35
  matcharm#33:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg rax, rbx
    x64.movRegImm32 r8, 1
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  matchdefault#35:
    x64.movRegImm32 rbx, 6
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg rax, rbx
    x64.movRegImm32 r8, 1
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#31:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_own
    x64.storeBaseDispReg.word64 [r12 + 0], r8
    x64.movRegReg rcx, rbx
    x64.callDirect __ms_destruct
  tryok#1:
    x64.leaRegRdata rbx, [rip + __str_rec_16]  ; "127.0.0.1"
  __il_body#40:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __ml_port
    x64.storeSlotReg slot38, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_cont#39
  tryerr#42:
    x64.leaRegRdata rcx, [rip + __str_blob_29]  ; "panic at TcpListener.maxon:31: TcpListener.port: this listener is closed, so it holds no bound port to report\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __il_cont#39:
    x64.loadRegBaseDisp.word64 r13, [rbx + 0]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.leaRegRegImm32 rcx, rbx, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.leaRegRegImm32 r15, r14, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r15, rbx
    x64.storeBaseDispReg.word64 [r14 + 0], r15
    x64.storeBaseDispReg.word64 [r14 + 8], rbx
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r14 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r14 + 24], rax
    x64.leaRegFunc rcx, [rip + TcpClient.connect]
    x64.callDirect __gt_spawn
    x64.movRegReg rbx, r8
    x64.storeBaseDispReg.word64 [rbx + 424], r14
    x64.loadRegSlot rax, slot38
    x64.storeBaseDispReg.word64 [rbx + 432], rax
    x64.leaRegFunc rax, [rip + __gt_release_TcpClient.connect]
    x64.storeBaseDispReg.word64 [rbx + 88], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_ready
  __il_body#72:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __ml_accept
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#73
  tryerr#74:
    x64.leaRegRegImm32 r13, r10, -1
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __ms_timed_out
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#76
  late#75:
    x64.movRegImm32 rax, 6
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#71
  ifcont#76:
    x64.jumpTable r13, default=matchdead [[0..1]=matcharm#79, [2]=matcharm#78, [3]=matcharm#79, [4]=matcharm#78, [5]=matcharm#77] @__jumptable_50
  matchdead:
  matcharm#79:
    x64.leaRegRdata rax, [rip + __str_rec_30]  ; "panic at TcpListener.maxon:53: "
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.leaRegRdata rax, [rip + __str_rec_31]  ; "TcpListener.accept: `accept` does not throw `"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 r15, [rax + 8]
    x64.cmpRegImm32 r13, 0
    x64.setccReg equal, rax
    x64.leaRegRdata rcx, [rip + __str_rec_32]  ; "resolveFailed"
    x64.imulRegReg rax, rax, rcx
    x64.cmpRegImm32 r13, 1
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_33]  ; "bindFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r13, 2
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_34]  ; "acceptFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r13, 3
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_35]  ; "addressFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r13, 4
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_36]  ; "closed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r13, 5
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_37]  ; "acceptRetriable"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot37, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot32, rax
    x64.leaRegRdata rcx, [rip + __str_rec_38]  ; "`"
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.storeSlotReg slot28, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.storeSlotReg slot24, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_24]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rsi, [rdx + 0]
    x64.storeSlotReg slot19, rsi
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.storeSlotReg slot16, rdx
    x64.leaRegRegReg rsi, r12, r15
    x64.leaRegRegReg rax, rsi, rax
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, rdx
    x64.storeSlotReg slot10, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot13, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.movRegReg rax, r15
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r15
    x64.loadRegSlot rax, slot32
    x64.loadRegSlot rdx, slot37
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot32
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot24
    x64.loadRegSlot rdx, slot28
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot24
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot16
    x64.loadRegSlot rdx, slot19
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot16
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot13
    x64.storeBaseDispReg.word64 [rax + 0], r13
    x64.loadRegSlot rcx, slot10
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  matcharm#78:
    x64.movRegImm32 rax, 8
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#71
  matcharm#77:
    x64.movRegImm32 rax, 9
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#71
  tryok#73:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_own
    x64.movRegReg r12, r8
  __il_body#84:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_own
    x64.storeBaseDispReg.word64 [r14 + 0], r8
    x64.movRegReg rcx, r12
    x64.callDirect __ms_destruct
  __il_cont#83:
    x64.movRegReg rcx, r13
    x64.callDirect __ms_destruct
  exitterminal#82:
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, r14
  __il_cont#71:
    x64.storeSlotReg slot7, r12
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#3
  tryerr#4:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_promise_drop
    x64.jmp exitdrop#6
  tryok#3:
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_try_await
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot9, rbx
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#7
  tryerr#8:
    x64.movRegImm32 rbx, 1
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_TcpClient
    x64.movRegReg r12, rbx
    x64.jmp exitdrop#6
  tryok#7:
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect TcpClient.setWriteDeadline
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#9
  tryerr#10:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_TcpClient
    x64.jmp exitdrop#11
  tryok#9:
    x64.movRegImm32 rcx, 100
  __il_body#44:
    x64.callDirect __gt_sleep
  __il_cont#43:
    x64.movRegImm32 r13, 0
    x64.movRegImm32 r14, 0
    x64.jmp whilehdr#12
  sending:
    x64.leaRegRdata rcx, [rip + __str_rec_17]  ; "xyz"
  __il_body#46:
    x64.callDirect String.addressableBytes
    x64.storeSlotReg slot29, r8
    x64.movRegImm32 rax, 0
    x64.loadRegBaseDisp.word64 r15, [r8 + 8]
    x64.jmp whilehdr#47
  send_loop:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.storeSlotReg slot34, rcx
    x64.callDirect __mm_retain
    x64.storeSlotReg slot23, r8
    x64.cmpRegImm32 r15, 1048576
    x64.jcc below, ternarytrue
  ternaryfalse:
    x64.movRegImm32 r9, 1048576
    x64.jmp matchcont#52
  ternarytrue:
    x64.movRegReg r9, r15
  matchcont#52:
    x64.loadRegSlot rax, slot25
    x64.loadRegSlot rdx, slot29
    x64.loadRegSlot rcx, slot34
    x64.callDirect __ms_send_from
    x64.movRegReg r15, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#53
  tryerr#54:
    x64.leaRegRegImm32 r15, r10, -1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __ms_timed_out
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#58
  late#55:
    x64.movRegImm32 r15, 5
    x64.loadRegSlot rcx, slot23
    x64.callDirect __ms_destruct
    x64.jmp exitdrop#57
  ifcont#58:
    x64.cmpRegImm32 r15, 0
    x64.jcc notEqual, matchdefault#60
  matcharm#59:
    x64.leaRegRdata rcx, [rip + __str_blob_26]  ; "panic at TcpClient.maxon:97: TcpClient.send: sendFrom OOB \xe2\x80\x94 loop invariant violated\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  matchdefault#60:
    x64.movRegImm32 r15, 2
    x64.loadRegSlot rcx, slot23
    x64.callDirect __ms_destruct
  exitdrop#57:
    x64.loadRegSlot rcx, slot29
    x64.callDirect __managed_decref
    x64.movRegReg rax, r15
    x64.jmp exitterminal#56
  tryok#53:
    x64.loadRegSlot rcx, slot23
    x64.callDirect __ms_destruct
    x64.cmpRegImm32 r15, 0
    x64.jcc notEqual, ifcont#63
  closed#62:
    x64.movRegImm32 r15, 4
    x64.loadRegSlot rcx, slot29
    x64.callDirect __managed_decref
    x64.movRegReg rax, r15
  exitterminal#56:
    x64.leaRegRegImm32 rax, rax, 1
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#45
  ifcont#63:
    x64.loadRegSlot rax, slot25
    x64.leaRegRegReg rax, rax, r15
    x64.loadRegSlot rcx, slot22
    x64.movRegReg rdx, rcx
    x64.subRegReg rdx, rcx, r15
    x64.movRegReg rsi, rcx
    x64.xorRegImm32 rsi, rcx, -1
    x64.andRegReg rsi, rsi, r15
    x64.xorRegReg rcx, rcx, r15
    x64.xorRegImm32 rcx, rcx, -1
    x64.andRegReg rcx, rcx, rdx
    x64.orRegReg rsi, rsi, rcx
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __rc_panic
  __rc_ok:
    x64.movRegReg r15, rdx
  whilehdr#47:
    x64.storeSlotReg slot25, rax
    x64.storeSlotReg slot22, r15
    x64.cmpRegImm32 r15, 0
    x64.jcc above, send_loop
  whileexit#49:
    x64.loadRegSlot rcx, slot29
    x64.callDirect __managed_decref
  exitterminal#64:
    x64.movRegImm32 rax, 0
    x64.loadRegSlot rcx, slot25
  __il_cont#45:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, sent
  ifelse#16:
    x64.leaRegRegImm32 rax, rax, -1
    x64.cmpRegImm32 rax, 5
    x64.jcc notEqual, matchdefault#18
  matcharm#17:
    x64.leaRegRegImm32 r14, r14, 1
  matchcont#19:
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, rbx
    x64.callDirect TcpClient.setWriteDeadline
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#21
    x64.jmp tryok#20
  sent:
    x64.movRegReg rax, r14
    x64.jmp ifcont#22
  tryok#20:
    x64.movRegReg rcx, r13
    x64.movRegReg rax, r14
  ifcont#22:
    x64.movRegReg r13, rcx
    x64.movRegReg r14, rax
  whilehdr#12:
    x64.storeSlotReg slot15, r13
    x64.storeSlotReg slot12, r14
    x64.cmpRegImm32 r13, 0
    x64.jcc equal, sending
    x64.jmp __il_body#68
  matchdefault#18:
    x64.leaRegRdata rcx, [rip + __str_blob_19]  ; "panic at netpoll-socket.a-send-the-kernel-finished-is-not-sent-again-past-its-deadline.test:20: unreachable: a send with room in its buffer ends in its bytes or"...
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __il_body#68:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __ms_close
  __il_cont#67:
    x64.leaRegRdata rax, [rip + __str_rec_18]  ; ""
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.leaRegRegImm32 rcx, r13, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.leaRegRegImm32 r15, r14, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r15, r13
    x64.storeBaseDispReg.word64 [r14 + 0], r15
    x64.storeBaseDispReg.word64 [r14 + 8], r13
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r14 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r14 + 24], rax
    x64.movRegImm32 rax, 1
    x64.jmp whilehdr#23
  reading:
    x64.movRegImm32 rcx, 1024
  __il_body#100:
    x64.movRegImm32 rdx, 1
    x64.callDirect __managed_mem_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot33, rbx
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#87
  tryerr#88:
    x64.leaRegRdata rcx, [rip + __str_blob_27]  ; "panic at TcpClient.maxon:119: TcpClient.recv: create \xe2\x80\x94 bufferSize >= 0 guaranteed\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#87:
    x64.loadRegBaseDisp.word64 r13, [r12 + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __mm_retain
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect __ms_recv
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#89
  tryerr#90:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __ms_timed_out
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#94
  late#91:
    x64.movRegImm32 r13, 5
    x64.movRegReg rcx, r15
    x64.callDirect __ms_destruct
    x64.jmp exitdrop#93
  ifcont#94:
    x64.movRegImm32 r13, 3
    x64.movRegReg rcx, r15
    x64.callDirect __ms_destruct
  exitdrop#93:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg rax, r13
    x64.jmp ifelse#27
  tryok#89:
    x64.movRegReg rcx, r15
    x64.callDirect __ms_destruct
    x64.cmpRegImm32 r13, 0
    x64.jcc notEqual, ifcont#96
  closed#95:
    x64.movRegImm32 r13, 4
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg rax, r13
  ifelse#27:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rax, r14
    x64.jmp ifcont#28
  ifcont#96:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_set_length
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#97
  tryerr#98:
    x64.leaRegRdata rcx, [rip + __str_blob_28]  ; "panic at TcpClient.maxon:136: TcpClient.recv: setLength(received) \xe2\x80\x94 received is within the buffer's capacity\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#97:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_retain
    x64.movRegReg rbx, r8
  __il_body#105:
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
    x64.movRegImm32 r15, 0
    x64.jmp forhdr
  scan:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __managed_byte_at
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#111
  tryok#110:
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
    x64.jmp __il_cont#104
  forstep:
    x64.leaRegRegImm32 r15, r15, 1
  forhdr:
    x64.cmpRegReg r15, r13
    x64.jcc below, scan
  forexit:
    x64.movRegImm32 rdx, 1
  __il_cont#104:
    x64.movRegReg rcx, rbx
    x64.callDirect __str_of_buffer
    x64.movRegReg r13, r8
    x64.storeSlotReg slot18, r13
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  __il_cont#101:
    x64.loadRegSlot rcx, slot33
    x64.callDirect __managed_decref
  got:
    x64.loadRegBaseDisp.word64 rbx, [r14 + 0]
    x64.loadRegBaseDisp.word64 r15, [r14 + 8]
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.storeSlotReg slot36, rax
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.storeSlotReg slot31, rax
    x64.leaRegRegReg rax, r15, rax
    x64.storeSlotReg slot21, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot27, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r15
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, r15
    x64.loadRegSlot rax, slot31
    x64.loadRegSlot rdx, slot36
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot31
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot27
    x64.storeBaseDispReg.word64 [rbx + 0], r13
    x64.loadRegSlot rax, slot21
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.loadRegSlot rcx, slot18
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot8
    x64.movRegReg rcx, rax
    x64.movRegReg rax, rbx
  ifcont#28:
    x64.movRegReg r14, rax
    x64.movRegReg rax, rcx
  whilehdr#23:
    x64.storeSlotReg slot11, r14
    x64.storeSlotReg slot8, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, reading
  whileexit#25:
    x64.leaRegRdata rax, [rip + __str_rec_21]  ; "reported="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot35, rcx
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot30, r8
    x64.loadRegSlot rcx, slot15
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_22]  ; " timeouts="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot26, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot5, r8
    x64.loadRegSlot rcx, slot12
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg r15, r8
    x64.leaRegRdata rax, [rip + __str_rec_23]  ; " heard="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.loadRegBaseDisp.word64 rcx, [r14 + 0]
    x64.storeSlotReg slot1, rcx
    x64.loadRegBaseDisp.word64 rcx, [r14 + 8]
    x64.storeSlotReg slot2, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_24]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rsi, [rdx + 0]
    x64.storeSlotReg slot6, rsi
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.storeSlotReg slot20, rdx
    x64.leaRegRegReg rsi, rbx, r12
    x64.leaRegRegReg rsi, rsi, r13
    x64.leaRegRegReg rsi, rsi, r15
    x64.leaRegRegReg rax, rsi, rax
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, rdx
    x64.storeSlotReg slot14, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot17, r8
    x64.leaRegRegImm32 r14, r8, 56
    x64.loadRegSlot rdx, slot35
    x64.movRegReg rcx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, rbx
    x64.loadRegSlot rdx, slot30
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rdx, slot26
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r13
    x64.loadRegSlot rdx, slot5
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r15
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r15
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot1
    x64.loadRegSlot rax, slot2
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot2
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot6
    x64.loadRegSlot rax, slot20
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot20
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot17
    x64.storeBaseDispReg.word64 [rax + 0], r14
    x64.loadRegSlot rcx, slot14
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot30
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot5
    x64.callDirect __mm_decref
  __il_body#70:
    x64.loadRegSlot rcx, slot17
    x64.callDirect __write_stdout
  __il_cont#69:
    x64.loadRegSlot rcx, slot17
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot11
    x64.callDirect __str_decref
  exitdrop#29:
    x64.loadRegSlot rcx, slot9
    x64.callDirect __destruct_TcpClient
    x64.movRegReg r12, rbx
  exitdrop#11:
    x64.loadRegSlot rcx, slot7
    x64.callDirect __destruct_TcpClient
  exitdrop#6:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __destruct_TcpListener
  exitterminal#5:
    x64.movRegReg r8, r12
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_43]  ; "panic at TcpClient.maxon:107: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryerr#21:
    x64.leaRegRdata rcx, [rip + __str_blob_20]  ; "panic at netpoll-socket.a-send-the-kernel-finished-is-not-sent-again-past-its-deadline.test:23: unreachable: the sending side is still open\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryerr#111:
    x64.leaRegRdata rcx, [rip + __str_blob_25]  ; "panic at String.maxon:1133: scanSingleByteGraphemes: byteAt OOB \xe2\x80\x94 i < len invariant\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
}

func @__ms_send_from {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 88
    x64.movRegReg rbx, rdx
    x64.movRegReg r12, rax
    x64.movRegReg r13, r9
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, rcx
    x64.leaRegRegImm32 r14, rax, -1
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, mschkoff
  closedthrow:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 7
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  mschkoff:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, msoob
  mschklen:
    x64.cmpRegImm32 r13, 0
    x64.jcc less, msoob
  msbound:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc greaterEqual, msownb
  msviewb:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
  __il_body#34:
    x64.movRegReg rdx, rcx
    x64.sarRegImm8 rdx, rcx, 63
    x64.movRegReg rsi, rdx
    x64.xorRegImm32 rsi, rdx, -1
    x64.movRegImm32 rdi, 0
    x64.subRegReg rdi, rdi, rcx
    x64.shlRegImm8 rcx, rcx, 3
    x64.andRegReg rdi, rdi, rdx
    x64.andRegReg rcx, rcx, rsi
    x64.orRegReg rdi, rdi, rcx
  __il_cont#33:
    x64.imulRegReg rax, rax, rdi
  __il_body#35:
    x64.leaRegRegImm32 rax, rax, 7
    x64.shrRegImm8 rax, rax, 3
    x64.jmp mschkcap
  msownb:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
  __il_body#40:
    x64.movRegReg rdx, rcx
    x64.sarRegImm8 rdx, rcx, 63
    x64.movRegReg rsi, rdx
    x64.xorRegImm32 rsi, rdx, -1
    x64.movRegImm32 rdi, 0
    x64.subRegReg rdi, rdi, rcx
    x64.shlRegImm8 rcx, rcx, 3
    x64.andRegReg rdi, rdi, rdx
    x64.andRegReg rcx, rcx, rsi
    x64.orRegReg rdi, rdi, rcx
  __il_cont#39:
    x64.imulRegReg rax, rax, rdi
  __il_body#41:
    x64.leaRegRegImm32 rax, rax, 7
    x64.shrRegImm8 rax, rax, 3
  mschkcap:
    x64.leaRegRegReg rcx, r12, r13
    x64.cmpRegReg rcx, rax
    x64.jcc lessEqual, mssend
  msoob:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  mssend:
    x64.callDirect __gt_io_park
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, msfail
  mssendgo:
    x64.movRegReg r15, r14
    x64.shlRegImm8 r15, r14, 1
    x64.movRegReg rcx, r15
    x64.callDirect __np_pd_op_begin
    x64.storeSlotReg slot1, r8
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegReg rbx, rax, r12
  mssendtry:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot0, rax
    x64.leaRegRegImm32 rax, rax, 328
    x64.storeSlotReg slot2, rax
    x64.movRegImm32 rcx, 0
    x64.storeBaseDispReg.word64 [rax + 0], rcx
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.movRegImm32 rcx, 24
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r13, rax
    x64.setccReg greater, rcx
    x64.movRegReg rdx, r13
    x64.subRegReg rdx, r13, rax
    x64.imulRegReg rcx, rcx, rdx
    x64.subRegReg r13, r13, rcx
    x64.storeBaseDispReg.word64 [r12 + 0], r13
    x64.storeBaseDispReg.word64 [r12 + 8], rbx
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, r15
    x64.movRegReg rax, r14
    x64.callDirect __np_op_aim
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 32], rax
    x64.loadRegSlot rcx, slot2
    x64.storeBaseDispReg.word64 [rsp + 40], rcx
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 48], rax
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegImm32 r8, 1
    x64.movRegImm32 r9, 0
    x64.iatCall 116
    x64.shlRegImm8 rax, rax, 32
    x64.movRegReg rbx, rax
    x64.sarRegImm8 rbx, rax, 32
    x64.iatCall 24
    x64.movReg32Reg32 rax, rax
    x64.cmpRegImm32 rbx, 0
    x64.setccReg equal, rcx
    x64.cmpRegImm32 rax, 997
    x64.setccReg equal, rax
    x64.orRegReg rcx, rcx, rax
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, msnostart
  mswaitovl:
    x64.movRegImm32 rdx, 1
    x64.loadRegSlot rax, slot1
    x64.movRegReg rcx, r15
    x64.callDirect __np_pd_wait
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, msnotready
  mslanded:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rax, [rax + 392]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, mswaitovl
    x64.jmp mscompleted
  msnotready:
    x64.cmpRegImm32 r8, 1
    x64.jcc notEqual, msstalecollect
  mslatecollect:
    x64.loadRegSlot rdx, slot2
    x64.movRegReg rcx, r14
    x64.iatCall 26
    x64.callDirect __np_op_settle
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, mslatecollected
  mscollect#24:
    x64.movRegReg rcx, rbx
    x64.callDirect __np_pd_op_begin
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r8
    x64.callDirect __np_pd_wait
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, mscollectbad#26
  mscollected#25:
    x64.movRegReg rcx, rbx
    x64.callDirect __np_op_close
    x64.jmp mslatecollected
  mscollectbad#26:
    x64.leaRegRdata rcx, [rip + __abort_msg_111]  ; "fatal error: runtime abort 111 (netpollOverlappedWaitNotACompletion)\x0a"
    x64.movRegImm32 rdx, 69
    x64.movRegImm32 rax, 111
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  mslatecollected:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rcx, [rax + 360]
    x64.loadRegBaseDisp.word64 rax, [rax + 368]
    x64.movRegImm32 rdx, 3221225760
    x64.cmpRegReg rcx, rdx
    x64.setccReg equal, rcx
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, rax
    x64.andRegReg rcx, rcx, rax
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, mscompleted
  mslatefree:
    x64.movRegReg rcx, r12
    x64.callDirect __mm_free
  mslate:
    x64.loadRegSlot rdx, slot1
    x64.movRegReg rcx, r15
    x64.callDirect __np_pd_mark_timed_out
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 4
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  msstalecollect:
    x64.callDirect __np_op_settle
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, mscompleted
  mscollect#27:
    x64.movRegReg rcx, rbx
    x64.callDirect __np_pd_op_begin
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r8
    x64.callDirect __np_pd_wait
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, mscollectbad#29
  mscollected#28:
    x64.movRegReg rcx, rbx
    x64.callDirect __np_op_close
    x64.jmp mscompleted
  mscollectbad#29:
    x64.leaRegRdata rcx, [rip + __abort_msg_111]  ; "fatal error: runtime abort 111 (netpollOverlappedWaitNotACompletion)\x0a"
    x64.movRegImm32 rdx, 69
    x64.movRegImm32 rax, 111
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  mscompleted:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rbx, [rax + 360]
    x64.loadRegBaseDisp.word64 r13, [rax + 368]
    x64.movRegImm32 rax, 3221225760
    x64.cmpRegReg rbx, rax
    x64.setccReg equal, r14
    x64.cmpRegImm32 r13, 0
    x64.setccReg equal, r15
    x64.andRegReg r15, r15, r14
    x64.movRegReg rcx, r12
    x64.callDirect __mm_free
    x64.cmpRegImm32 rbx, 0
    x64.setccReg equal, rax
    x64.cmpRegImm32 r15, 0
    x64.setccReg equal, rcx
    x64.andRegReg rcx, rcx, r14
    x64.orRegReg rax, rax, rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, msfail
  msdone:
    x64.movRegImm32 r10, 0
    x64.movRegReg r8, r13
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  msnostart:
    x64.callDirect __np_op_clear
    x64.movRegReg rcx, r12
    x64.callDirect __mm_free
  msfail:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 4
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
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
  __gt_seed_bytes@104 = i64 2048
  __gt_seed_sum_seen@112 = i64 0
  __gt_seed_count_seen@120 = i64 0
  __gt_seed_guard@128 = i64 0
  __gt_keeps_cpu_clock@136 = i64 0
  __gt_stack_pool@144 = i64 0
  __sched_active_workers@152 = i64 1
  __sched_max_active_workers@160 = i64 1
  __sched_tls_index@168 = i64 0
  __sched_tls_teb_offset@176 = i64 0
  __sched_procs@184 = i64 0
  __sched_allm@192 = i64 0
  __sched_async_preempt_off@200 = i64 0
  __sched_midle@208 = i64 0
  __sched_pidle@216 = i64 0
  __gt_gfree_head@224 = i64 0
  __gt_gfree_nostack_head@232 = i64 0
  __gt_cell_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __gt_gfree_lock@272 = i64 0
  __gt_gfree_lock_owner@280 = i64 0
  __gt_gfree_lock_depth@288 = i64 0
  __sched_npidle@296 = i64 0
  __sched_runq_size@304 = i64 0
  __sched_global_pushes@312 = i64 0
  __sched_timer_scan_steps@320 = i64 0
  __sched_mcount@328 = i64 0
  __sched_nmidle@336 = i64 0
  __sched_nmidlelocked@344 = i64 0
  __sched_main_m@352 = i64 0
  __sched_phase@360 = i64 0
  __sched_lastpoll@368 = i64 0
  __sched_poll_until@376 = i64 0
  __sched_regain_head@384 = i64 0
  __np_poller@392 = i64 0
  __np_break@400 = i64 0
  __np_wake_sig@408 = i64 0
  __np_pd_table@416 = i64 0
  __np_pd_cap@424 = i64 0
  __np_waiters@432 = i64 0
  __np_orphan_exited@440 = i64 0
  __np_src_key_free@448 = i64 0
  __np_src_key_high@456 = i64 0
  __np_child_signal@464 = i64 0
  __np_pd_mtable@472 = i64 0
  __np_pd_mcap@480 = i64 0
  __sched_num_procs@488 = i64 0
  __sched_ncpu@496 = i64 0
  __sched_shutdown_flag@504 = i64 0
  __sched_lock@512 = i64 0
  __sched_lock_depth_sink@520 = i64 0
  __sched_nmspinning@528 = i64 0
  __sched_needspinning@536 = i64 0
  __sched_sysmon_event@544 = i64 0
  __sched_sysmon_wait@552 = i64 0
  __sched_timer_starts@560 = i64 0
  __mrt_envp@568 = i64 0
  __mrt_signal_stack_bytes@576 = i64 0
  __mrt_tls_next_key@584 = i64 0
  __slab_arena_list@592 = i64 0
  __slab_arena_map_l1@600 = i64 0
  __slab_state@608 = i64 0
  __ms_wsa_started@616 = i64 0
  __mrt_program_started@624 = i8 0
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
    x64.prologue 344
    x64.leaRegRdata rcx, [rip + __str_rec_16]  ; "127.0.0.1"
    x64.movRegImm32 rbx, 0
  __il_body#38:
    x64.callDirect String.addressableBytes
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __ml_bind
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#31
  tryerr#32:
    x64.leaRegRegImm32 rax, r10, -1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, matchdefault#35
  matcharm#33:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg rax, rbx
    x64.movRegImm32 r8, 1
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  matchdefault#35:
    x64.movRegImm32 rbx, 6
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg rax, rbx
    x64.movRegImm32 r8, 1
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#31:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_own
    x64.storeBaseDispReg.word64 [r12 + 0], r8
    x64.movRegReg rcx, rbx
    x64.callDirect __ms_destruct
  tryok#1:
    x64.leaRegRdata rbx, [rip + __str_rec_16]  ; "127.0.0.1"
  __il_body#40:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __ml_port
    x64.storeSlotReg slot38, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_cont#39
  tryerr#42:
    x64.leaRegRdata rcx, [rip + __str_blob_29]  ; "panic at TcpListener.maxon:31: TcpListener.port: this listener is closed, so it holds no bound port to report\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __il_cont#39:
    x64.loadRegBaseDisp.word64 r13, [rbx + 0]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.leaRegRegImm32 rcx, rbx, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.leaRegRegImm32 r15, r14, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r15, rbx
    x64.storeBaseDispReg.word64 [r14 + 0], r15
    x64.storeBaseDispReg.word64 [r14 + 8], rbx
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r14 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r14 + 24], rax
    x64.leaRegFunc rcx, [rip + TcpClient.connect]
    x64.callDirect __gt_spawn
    x64.movRegReg rbx, r8
    x64.storeBaseDispReg.word64 [rbx + 424], r14
    x64.loadRegSlot rax, slot38
    x64.storeBaseDispReg.word64 [rbx + 432], rax
    x64.leaRegFunc rax, [rip + __gt_release_TcpClient.connect]
    x64.storeBaseDispReg.word64 [rbx + 88], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_ready
  __il_body#72:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __ml_accept
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#73
  tryerr#74:
    x64.leaRegRegImm32 r13, r10, -1
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __ms_timed_out
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#76
  late#75:
    x64.movRegImm32 rax, 6
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#71
  ifcont#76:
    x64.jumpTable r13, default=matchdead [[0..1]=matcharm#79, [2]=matcharm#78, [3]=matcharm#79, [4]=matcharm#78, [5]=matcharm#77] @__jumptable_50
  matchdead:
  matcharm#79:
    x64.leaRegRdata rax, [rip + __str_rec_30]  ; "panic at TcpListener.maxon:53: "
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.leaRegRdata rax, [rip + __str_rec_31]  ; "TcpListener.accept: `accept` does not throw `"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 r15, [rax + 8]
    x64.cmpRegImm32 r13, 0
    x64.setccReg equal, rax
    x64.leaRegRdata rcx, [rip + __str_rec_32]  ; "resolveFailed"
    x64.imulRegReg rax, rax, rcx
    x64.cmpRegImm32 r13, 1
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_33]  ; "bindFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r13, 2
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_34]  ; "acceptFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r13, 3
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_35]  ; "addressFailed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r13, 4
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_36]  ; "closed"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.cmpRegImm32 r13, 5
    x64.setccReg equal, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_37]  ; "acceptRetriable"
    x64.imulRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot37, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot32, rax
    x64.leaRegRdata rcx, [rip + __str_rec_38]  ; "`"
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.storeSlotReg slot28, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.storeSlotReg slot24, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_24]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rsi, [rdx + 0]
    x64.storeSlotReg slot19, rsi
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.storeSlotReg slot16, rdx
    x64.leaRegRegReg rsi, r12, r15
    x64.leaRegRegReg rax, rsi, rax
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, rdx
    x64.storeSlotReg slot10, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot13, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.movRegReg rax, r15
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r15
    x64.loadRegSlot rax, slot32
    x64.loadRegSlot rdx, slot37
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot32
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot24
    x64.loadRegSlot rdx, slot28
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot24
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot16
    x64.loadRegSlot rdx, slot19
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot16
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot13
    x64.storeBaseDispReg.word64 [rax + 0], r13
    x64.loadRegSlot rcx, slot10
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  matcharm#78:
    x64.movRegImm32 rax, 8
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#71
  matcharm#77:
    x64.movRegImm32 rax, 9
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#71
  tryok#73:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_own
    x64.movRegReg r12, r8
  __il_body#84:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_own
    x64.storeBaseDispReg.word64 [r14 + 0], r8
    x64.movRegReg rcx, r12
    x64.callDirect __ms_destruct
  __il_cont#83:
    x64.movRegReg rcx, r13
    x64.callDirect __ms_destruct
  exitterminal#82:
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, r14
  __il_cont#71:
    x64.storeSlotReg slot7, r12
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#3
  tryerr#4:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_promise_drop
    x64.jmp exitdrop#6
  tryok#3:
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_try_await
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot9, rbx
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#7
  tryerr#8:
    x64.movRegImm32 rbx, 1
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_TcpClient
    x64.movRegReg r12, rbx
    x64.jmp exitdrop#6
  tryok#7:
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect TcpClient.setWriteDeadline
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#9
  tryerr#10:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_TcpClient
    x64.jmp exitdrop#11
  tryok#9:
    x64.movRegImm32 rcx, 100
  __il_body#44:
    x64.callDirect __gt_sleep
  __il_cont#43:
    x64.movRegImm32 r13, 0
    x64.movRegImm32 r14, 0
    x64.jmp whilehdr#12
  sending:
    x64.leaRegRdata rcx, [rip + __str_rec_17]  ; "xyz"
  __il_body#46:
    x64.callDirect String.addressableBytes
    x64.storeSlotReg slot29, r8
    x64.movRegImm32 rax, 0
    x64.loadRegBaseDisp.word64 r15, [r8 + 8]
    x64.jmp whilehdr#47
  send_loop:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.storeSlotReg slot34, rcx
    x64.callDirect __mm_retain
    x64.storeSlotReg slot23, r8
    x64.cmpRegImm32 r15, 1048576
    x64.jcc below, ternarytrue
  ternaryfalse:
    x64.movRegImm32 r9, 1048576
    x64.jmp matchcont#52
  ternarytrue:
    x64.movRegReg r9, r15
  matchcont#52:
    x64.loadRegSlot rax, slot25
    x64.loadRegSlot rdx, slot29
    x64.loadRegSlot rcx, slot34
    x64.callDirect __ms_send_from
    x64.movRegReg r15, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#53
  tryerr#54:
    x64.leaRegRegImm32 r15, r10, -1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __ms_timed_out
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#58
  late#55:
    x64.movRegImm32 r15, 5
    x64.loadRegSlot rcx, slot23
    x64.callDirect __ms_destruct
    x64.jmp exitdrop#57
  ifcont#58:
    x64.cmpRegImm32 r15, 0
    x64.jcc notEqual, matchdefault#60
  matcharm#59:
    x64.leaRegRdata rcx, [rip + __str_blob_26]  ; "panic at TcpClient.maxon:97: TcpClient.send: sendFrom OOB \xe2\x80\x94 loop invariant violated\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  matchdefault#60:
    x64.movRegImm32 r15, 2
    x64.loadRegSlot rcx, slot23
    x64.callDirect __ms_destruct
  exitdrop#57:
    x64.loadRegSlot rcx, slot29
    x64.callDirect __managed_decref
    x64.movRegReg rax, r15
    x64.jmp exitterminal#56
  tryok#53:
    x64.loadRegSlot rcx, slot23
    x64.callDirect __ms_destruct
    x64.cmpRegImm32 r15, 0
    x64.jcc notEqual, ifcont#63
  closed#62:
    x64.movRegImm32 r15, 4
    x64.loadRegSlot rcx, slot29
    x64.callDirect __managed_decref
    x64.movRegReg rax, r15
  exitterminal#56:
    x64.leaRegRegImm32 rax, rax, 1
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#45
  ifcont#63:
    x64.loadRegSlot rax, slot25
    x64.leaRegRegReg rax, rax, r15
    x64.loadRegSlot rcx, slot22
    x64.movRegReg rdx, rcx
    x64.subRegReg rdx, rcx, r15
    x64.movRegReg rsi, rcx
    x64.xorRegImm32 rsi, rcx, -1
    x64.andRegReg rsi, rsi, r15
    x64.xorRegReg rcx, rcx, r15
    x64.xorRegImm32 rcx, rcx, -1
    x64.andRegReg rcx, rcx, rdx
    x64.orRegReg rsi, rsi, rcx
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __rc_panic
  __rc_ok:
    x64.movRegReg r15, rdx
  whilehdr#47:
    x64.storeSlotReg slot25, rax
    x64.storeSlotReg slot22, r15
    x64.cmpRegImm32 r15, 0
    x64.jcc above, send_loop
  whileexit#49:
    x64.loadRegSlot rcx, slot29
    x64.callDirect __managed_decref
  exitterminal#64:
    x64.movRegImm32 rax, 0
    x64.loadRegSlot rcx, slot25
  __il_cont#45:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, sent
  ifelse#16:
    x64.leaRegRegImm32 rax, rax, -1
    x64.cmpRegImm32 rax, 5
    x64.jcc notEqual, matchdefault#18
  matcharm#17:
    x64.leaRegRegImm32 r14, r14, 1
  matchcont#19:
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, rbx
    x64.callDirect TcpClient.setWriteDeadline
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#21
    x64.jmp tryok#20
  sent:
    x64.movRegReg rax, r14
    x64.jmp ifcont#22
  tryok#20:
    x64.movRegReg rcx, r13
    x64.movRegReg rax, r14
  ifcont#22:
    x64.movRegReg r13, rcx
    x64.movRegReg r14, rax
  whilehdr#12:
    x64.storeSlotReg slot15, r13
    x64.storeSlotReg slot12, r14
    x64.cmpRegImm32 r13, 0
    x64.jcc equal, sending
    x64.jmp __il_body#68
  matchdefault#18:
    x64.leaRegRdata rcx, [rip + __str_blob_19]  ; "panic at netpoll-socket.a-send-the-kernel-finished-is-not-sent-again-past-its-deadline.test:20: unreachable: a send with room in its buffer ends in its bytes or"...
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __il_body#68:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __ms_close
  __il_cont#67:
    x64.leaRegRdata rax, [rip + __str_rec_18]  ; ""
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.leaRegRegImm32 rcx, r13, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.leaRegRegImm32 r15, r14, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r15, r13
    x64.storeBaseDispReg.word64 [r14 + 0], r15
    x64.storeBaseDispReg.word64 [r14 + 8], r13
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r14 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r14 + 24], rax
    x64.movRegImm32 rax, 1
    x64.jmp whilehdr#23
  reading:
    x64.movRegImm32 rcx, 1024
  __il_body#100:
    x64.movRegImm32 rdx, 1
    x64.callDirect __managed_mem_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot33, rbx
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#87
  tryerr#88:
    x64.leaRegRdata rcx, [rip + __str_blob_27]  ; "panic at TcpClient.maxon:119: TcpClient.recv: create \xe2\x80\x94 bufferSize >= 0 guaranteed\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#87:
    x64.loadRegBaseDisp.word64 r13, [r12 + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __mm_retain
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect __ms_recv
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#89
  tryerr#90:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __ms_timed_out
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#94
  late#91:
    x64.movRegImm32 r13, 5
    x64.movRegReg rcx, r15
    x64.callDirect __ms_destruct
    x64.jmp exitdrop#93
  ifcont#94:
    x64.movRegImm32 r13, 3
    x64.movRegReg rcx, r15
    x64.callDirect __ms_destruct
  exitdrop#93:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg rax, r13
    x64.jmp ifelse#27
  tryok#89:
    x64.movRegReg rcx, r15
    x64.callDirect __ms_destruct
    x64.cmpRegImm32 r13, 0
    x64.jcc notEqual, ifcont#96
  closed#95:
    x64.movRegImm32 r13, 4
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg rax, r13
  ifelse#27:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rax, r14
    x64.jmp ifcont#28
  ifcont#96:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_set_length
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#97
  tryerr#98:
    x64.leaRegRdata rcx, [rip + __str_blob_28]  ; "panic at TcpClient.maxon:136: TcpClient.recv: setLength(received) \xe2\x80\x94 received is within the buffer's capacity\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#97:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_retain
    x64.movRegReg rbx, r8
  __il_body#105:
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
    x64.movRegImm32 r15, 0
    x64.jmp forhdr
  scan:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __managed_byte_at
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#111
  tryok#110:
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
    x64.jmp __il_cont#104
  forstep:
    x64.leaRegRegImm32 r15, r15, 1
  forhdr:
    x64.cmpRegReg r15, r13
    x64.jcc below, scan
  forexit:
    x64.movRegImm32 rdx, 1
  __il_cont#104:
    x64.movRegReg rcx, rbx
    x64.callDirect __str_of_buffer
    x64.movRegReg r13, r8
    x64.storeSlotReg slot18, r13
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  __il_cont#101:
    x64.loadRegSlot rcx, slot33
    x64.callDirect __managed_decref
  got:
    x64.loadRegBaseDisp.word64 rbx, [r14 + 0]
    x64.loadRegBaseDisp.word64 r15, [r14 + 8]
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.storeSlotReg slot36, rax
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.storeSlotReg slot31, rax
    x64.leaRegRegReg rax, r15, rax
    x64.storeSlotReg slot21, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot27, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r15
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, r15
    x64.loadRegSlot rax, slot31
    x64.loadRegSlot rdx, slot36
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot31
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot27
    x64.storeBaseDispReg.word64 [rbx + 0], r13
    x64.loadRegSlot rax, slot21
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.loadRegSlot rcx, slot18
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot8
    x64.movRegReg rcx, rax
    x64.movRegReg rax, rbx
  ifcont#28:
    x64.movRegReg r14, rax
    x64.movRegReg rax, rcx
  whilehdr#23:
    x64.storeSlotReg slot11, r14
    x64.storeSlotReg slot8, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, reading
  whileexit#25:
    x64.leaRegRdata rax, [rip + __str_rec_21]  ; "reported="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot35, rcx
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot30, r8
    x64.loadRegSlot rcx, slot15
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_22]  ; " timeouts="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot26, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot5, r8
    x64.loadRegSlot rcx, slot12
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg r15, r8
    x64.leaRegRdata rax, [rip + __str_rec_23]  ; " heard="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.loadRegBaseDisp.word64 rcx, [r14 + 0]
    x64.storeSlotReg slot1, rcx
    x64.loadRegBaseDisp.word64 rcx, [r14 + 8]
    x64.storeSlotReg slot2, rcx
    x64.leaRegRdata rdx, [rip + __str_rec_24]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rsi, [rdx + 0]
    x64.storeSlotReg slot6, rsi
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.storeSlotReg slot20, rdx
    x64.leaRegRegReg rsi, rbx, r12
    x64.leaRegRegReg rsi, rsi, r13
    x64.leaRegRegReg rsi, rsi, r15
    x64.leaRegRegReg rax, rsi, rax
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rax, rax, rdx
    x64.storeSlotReg slot14, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot17, r8
    x64.leaRegRegImm32 r14, r8, 56
    x64.loadRegSlot rdx, slot35
    x64.movRegReg rcx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, rbx
    x64.loadRegSlot rdx, slot30
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rdx, slot26
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r13
    x64.loadRegSlot rdx, slot5
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r15
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r15
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot1
    x64.loadRegSlot rax, slot2
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot2
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot6
    x64.loadRegSlot rax, slot20
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot20
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot17
    x64.storeBaseDispReg.word64 [rax + 0], r14
    x64.loadRegSlot rcx, slot14
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot30
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot5
    x64.callDirect __mm_decref
  __il_body#70:
    x64.loadRegSlot rcx, slot17
    x64.callDirect __write_stdout
  __il_cont#69:
    x64.loadRegSlot rcx, slot17
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot11
    x64.callDirect __str_decref
  exitdrop#29:
    x64.loadRegSlot rcx, slot9
    x64.callDirect __destruct_TcpClient
    x64.movRegReg r12, rbx
  exitdrop#11:
    x64.loadRegSlot rcx, slot7
    x64.callDirect __destruct_TcpClient
  exitdrop#6:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __destruct_TcpListener
  exitterminal#5:
    x64.movRegReg r8, r12
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_43]  ; "panic at TcpClient.maxon:107: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryerr#21:
    x64.leaRegRdata rcx, [rip + __str_blob_20]  ; "panic at netpoll-socket.a-send-the-kernel-finished-is-not-sent-again-past-its-deadline.test:23: unreachable: the sending side is still open\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryerr#111:
    x64.leaRegRdata rcx, [rip + __str_blob_25]  ; "panic at String.maxon:1133: scanSingleByteGraphemes: byteAt OOB \xe2\x80\x94 i < len invariant\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 344
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
}

func @__ms_send_from {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rdx
    x64.movRegReg r12, rax
    x64.movRegReg r13, r9
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, rcx
    x64.leaRegRegImm32 r14, rax, -1
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, mschkoff
  closedthrow:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 7
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  mschkoff:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, msoob
  mschklen:
    x64.cmpRegImm32 r13, 0
    x64.jcc less, msoob
  msbound:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc greaterEqual, msownb
  msviewb:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
  __il_body#22:
    x64.movRegReg rdx, rcx
    x64.sarRegImm8 rdx, rcx, 63
    x64.movRegReg rsi, rdx
    x64.xorRegImm32 rsi, rdx, -1
    x64.movRegImm32 rdi, 0
    x64.subRegReg rdi, rdi, rcx
    x64.shlRegImm8 rcx, rcx, 3
    x64.andRegReg rdi, rdi, rdx
    x64.andRegReg rcx, rcx, rsi
    x64.orRegReg rdi, rdi, rcx
  __il_cont#21:
    x64.imulRegReg rax, rax, rdi
  __il_body#23:
    x64.leaRegRegImm32 rax, rax, 7
    x64.shrRegImm8 rax, rax, 3
    x64.jmp mschkcap
  msownb:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
  __il_body#28:
    x64.movRegReg rdx, rcx
    x64.sarRegImm8 rdx, rcx, 63
    x64.movRegReg rsi, rdx
    x64.xorRegImm32 rsi, rdx, -1
    x64.movRegImm32 rdi, 0
    x64.subRegReg rdi, rdi, rcx
    x64.shlRegImm8 rcx, rcx, 3
    x64.andRegReg rdi, rdi, rdx
    x64.andRegReg rcx, rcx, rsi
    x64.orRegReg rdi, rdi, rcx
  __il_cont#27:
    x64.imulRegReg rax, rax, rdi
  __il_body#29:
    x64.leaRegRegImm32 rax, rax, 7
    x64.shrRegImm8 rax, rax, 3
  mschkcap:
    x64.leaRegRegReg rcx, r12, r13
    x64.cmpRegReg rcx, rax
    x64.jcc lessEqual, mssend
  msoob:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  mssend:
    x64.callDirect __gt_io_park
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, msfail
  mssendgo:
    x64.movRegReg rcx, r14
    x64.callDirect __np_pd_op_begin
    x64.movRegReg r15, r8
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegReg rbx, rax, r12
    x64.jmp mssendtry
  msclassify:
    x64.movRegImm32 rcx, 0
    x64.subRegReg rcx, rcx, rax
    x64.cmpRegImm32 rcx, 11
    x64.setccReg equal, rax
    x64.cmpRegImm32 rcx, 115
    x64.setccReg equal, rdx
    x64.cmpRegImm32 rcx, 4
    x64.setccReg equal, rcx
    x64.orRegReg rax, rax, rdx
    x64.orRegReg rax, rax, rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, msfail
  mswait:
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, r14
    x64.movRegReg rax, r15
    x64.callDirect __np_pd_wait
    x64.cmpRegImm32 r8, 2
    x64.jcc equal, msfail
  mswaitlive:
    x64.cmpRegImm32 r8, 1
    x64.jcc equal, mslate
  mssendtry:
    x64.movRegReg rdi, r14
    x64.movRegReg rsi, rbx
    x64.movRegReg rdx, r13
    x64.movRegImm32 r10, 16384
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r9, 0
    x64.x64Syscall 44
    x64.cmpRegImm32 rax, 0
    x64.jcc less, msclassify
    x64.jmp msdone
  mslate:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r15
    x64.callDirect __np_pd_mark_timed_out
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 4
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  msfail:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 4
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  msdone:
    x64.movRegImm32 r10, 0
    x64.movRegReg r8, rax
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
}
```

```TargetIr:arm64-macos
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
  __gt_seed_bytes@104 = i64 2048
  __gt_seed_sum_seen@112 = i64 0
  __gt_seed_count_seen@120 = i64 0
  __gt_seed_guard@128 = i64 0
  __gt_keeps_cpu_clock@136 = i64 0
  __gt_stack_pool@144 = i64 0
  __sched_active_workers@152 = i64 1
  __sched_max_active_workers@160 = i64 1
  __sched_tls_index@168 = i64 0
  __sched_tls_teb_offset@176 = i64 0
  __sched_procs@184 = i64 0
  __sched_allm@192 = i64 0
  __sched_async_preempt_off@200 = i64 0
  __sched_midle@208 = i64 0
  __sched_pidle@216 = i64 0
  __gt_gfree_head@224 = i64 0
  __gt_gfree_nostack_head@232 = i64 0
  __gt_cell_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __gt_gfree_lock@272 = i64 0
  __gt_gfree_lock_owner@280 = i64 0
  __gt_gfree_lock_depth@288 = i64 0
  __sched_npidle@296 = i64 0
  __sched_runq_size@304 = i64 0
  __sched_global_pushes@312 = i64 0
  __sched_timer_scan_steps@320 = i64 0
  __sched_mcount@328 = i64 0
  __sched_nmidle@336 = i64 0
  __sched_nmidlelocked@344 = i64 0
  __sched_main_m@352 = i64 0
  __sched_phase@360 = i64 0
  __sched_lastpoll@368 = i64 0
  __sched_poll_until@376 = i64 0
  __sched_regain_head@384 = i64 0
  __np_poller@392 = i64 0
  __np_break@400 = i64 0
  __np_wake_sig@408 = i64 0
  __np_pd_table@416 = i64 0
  __np_pd_cap@424 = i64 0
  __np_waiters@432 = i64 0
  __np_orphan_exited@440 = i64 0
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
  __slab_arena_list@528 = i64 0
  __slab_arena_map_l1@536 = i64 0
  __slab_state@544 = i64 0
  __ms_wsa_started@552 = i64 0
  __mrt_program_started@560 = i8 0
}

func @main {
  entry:
    arm64.prologue 240
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
    arm64.leaRdata x0, __str_rec_16  ; "127.0.0.1"
    arm64.movImm x19, 0
  __il_body#38:
    arm64.bl String.addressableBytes
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __ml_bind
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#31
  tryerr#32:
    arm64.sub x0, x9, 1
    arm64.cmp x0, 0
    arm64.b.ne matchdefault#35
  matcharm#33:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.movImm x0, 1
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
    arm64.epilogue 240
    arm64.retIndirect
  matchdefault#35:
    arm64.movImm x19, 6
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.movImm x0, 1
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
    arm64.epilogue 240
    arm64.retIndirect
  tryok#31:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot0, x20
    arm64.movRegReg x0, x19
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __ms_destruct
  tryok#1:
    arm64.leaRdata x19, __str_rec_16  ; "127.0.0.1"
  __il_body#40:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __ml_port
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq __il_cont#39
  tryerr#42:
    arm64.leaRdata x0, __str_blob_29  ; "panic at TcpListener.maxon:31: TcpListener.port: this listener is closed, so it holds no bound port to report\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  __il_cont#39:
    arm64.loadRegBaseDisp.word64 x22, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x19, [x19 + 8]
    arm64.add x0, x19, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.add x24, x23, 56
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x24, x19
    arm64.storeBaseDispReg.word64 [x23 + 0], x24
    arm64.storeBaseDispReg.word64 [x23 + 8], x19
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x23 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x23 + 24], x0
    arm64.leaFuncAddr x0, TcpClient.connect
    arm64.bl __gt_spawn
    arm64.movRegReg x19, x0
    arm64.storeBaseDispReg.word64 [x19 + 424], x23
    arm64.storeBaseDispReg.word64 [x19 + 432], x21
    arm64.leaFuncAddr x0, __gt_release_TcpClient.connect
    arm64.storeBaseDispReg.word64 [x19 + 88], x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_ready
  __il_body#72:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __ml_accept
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#73
  tryerr#74:
    arm64.sub x21, x9, 1
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __ms_timed_out
    arm64.cbz x0, ifcont#76
  late#75:
    arm64.movImm x0, 6
    arm64.movImm x20, 0
    arm64.b __il_cont#71
  ifcont#76:
    arm64.jumpTable x21, default=matchdead [[0..1]=matcharm#79, [2]=matcharm#78, [3]=matcharm#79, [4]=matcharm#78, [5]=matcharm#77] @__jumptable_50
  matchdead:
  matcharm#79:
    arm64.leaRdata x0, __str_rec_30  ; "panic at TcpListener.maxon:53: "
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.leaRdata x0, __str_rec_31  ; "TcpListener.accept: `accept` does not throw `"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.cmp x21, 0
    arm64.cset x0, eq
    arm64.leaRdata x1, __str_rec_32  ; "resolveFailed"
    arm64.mul x0, x0, x1
    arm64.cmp x21, 1
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_33  ; "bindFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x21, 2
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_34  ; "acceptFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x21, 3
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_35  ; "addressFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x21, 4
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_36  ; "closed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x21, 5
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_37  ; "acceptRetriable"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.leaRdata x0, __str_rec_38  ; "`"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_24  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot16, x0
    arm64.add x1, x20, x23
    arm64.add x1, x1, x24
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot10, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot13, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x28, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x19, x19, x24
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x19, x19, x26
    arm64.loadRegSlot x2, slot16
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot16
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot13
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot10
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
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
    arm64.epilogue 240
    arm64.retIndirect
  matcharm#78:
    arm64.movImm x0, 8
    arm64.movImm x20, 0
    arm64.b __il_cont#71
  matcharm#77:
    arm64.movImm x0, 9
    arm64.movImm x20, 0
    arm64.b __il_cont#71
  tryok#73:
    arm64.movRegReg x0, x21
    arm64.bl __mm_own
    arm64.movRegReg x20, x0
  __il_body#84:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __ms_destruct
  __il_cont#83:
    arm64.movRegReg x0, x21
    arm64.bl __ms_destruct
  exitterminal#82:
    arm64.movImm x0, 0
    arm64.movRegReg x20, x22
  __il_cont#71:
    arm64.storeSlotReg slot1, x20
    arm64.cmp x0, 0
    arm64.b.eq tryok#3
  tryerr#4:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __gt_promise_drop
    arm64.b exitdrop#6
  tryok#3:
    arm64.movRegReg x0, x19
    arm64.bl __gt_try_await
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot2, x19
    arm64.cmp x9, 0
    arm64.b.eq tryok#7
  tryerr#8:
    arm64.movImm x19, 1
    arm64.movRegReg x0, x20
    arm64.bl __destruct_TcpClient
    arm64.movRegReg x20, x19
    arm64.b exitdrop#6
  tryok#7:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl TcpClient.setWriteDeadline
    arm64.cmp x9, 0
    arm64.b.eq tryok#9
  tryerr#10:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __destruct_TcpClient
    arm64.b exitdrop#11
  tryok#9:
    arm64.movImm x0, 100
  __il_body#44:
    arm64.bl __gt_sleep
  __il_cont#43:
    arm64.movImm x21, 0
    arm64.movImm x22, 0
    arm64.b whilehdr#12
  sending:
    arm64.leaRdata x0, __str_rec_17  ; "xyz"
  __il_body#46:
    arm64.bl String.addressableBytes
    arm64.movRegReg x23, x0
    arm64.movImm x24, 0
    arm64.loadRegBaseDisp.word64 x25, [x23 + 8]
    arm64.b whilehdr#47
  send_loop:
    arm64.loadRegBaseDisp.word64 x26, [x19 + 0]
    arm64.movRegReg x0, x26
    arm64.bl __mm_retain
    arm64.movRegReg x27, x0
    arm64.cmp x25, 1048576
    arm64.b.lo ternarytrue
  ternaryfalse:
    arm64.movImm x3, 1048576
    arm64.b matchcont#52
  ternarytrue:
    arm64.movRegReg x3, x25
  matchcont#52:
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __ms_send_from
    arm64.movRegReg x26, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#53
  tryerr#54:
    arm64.sub x24, x9, 1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __ms_timed_out
    arm64.cbz x0, ifcont#58
  late#55:
    arm64.movImm x24, 5
    arm64.movRegReg x0, x27
    arm64.bl __ms_destruct
    arm64.b exitdrop#57
  ifcont#58:
    arm64.cmp x24, 0
    arm64.b.ne matchdefault#60
  matcharm#59:
    arm64.leaRdata x0, __str_blob_26  ; "panic at TcpClient.maxon:97: TcpClient.send: sendFrom OOB \xe2\x80\x94 loop invariant violated\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  matchdefault#60:
    arm64.movImm x24, 2
    arm64.movRegReg x0, x27
    arm64.bl __ms_destruct
  exitdrop#57:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movRegReg x0, x24
    arm64.b exitterminal#56
  tryok#53:
    arm64.movRegReg x0, x27
    arm64.bl __ms_destruct
    arm64.cmp x26, 0
    arm64.b.ne ifcont#63
  closed#62:
    arm64.movImm x24, 4
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movRegReg x0, x24
  exitterminal#56:
    arm64.add x0, x0, 1
    arm64.movImm x1, 0
    arm64.b __il_cont#45
  ifcont#63:
    arm64.add x24, x24, x26
    arm64.sub x0, x25, x26
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x25, x16
    arm64.and x1, x1, x26
    arm64.eor x2, x25, x26
    arm64.movImm x16, 18446744073709551615
    arm64.eor x2, x2, x16
    arm64.and x2, x2, x0
    arm64.orr x1, x1, x2
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __rc_ok:
    arm64.movRegReg x25, x0
  whilehdr#47:
    arm64.cmp x25, 0
    arm64.b.hi send_loop
  whileexit#49:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitterminal#64:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x24
  __il_cont#45:
    arm64.cmp x0, 0
    arm64.b.eq sent
  ifelse#16:
    arm64.sub x0, x0, 1
    arm64.cmp x0, 5
    arm64.b.ne matchdefault#18
  matcharm#17:
    arm64.add x22, x22, 1
  matchcont#19:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl TcpClient.setWriteDeadline
    arm64.cmp x9, 0
    arm64.b.ne tryerr#21
    arm64.b tryok#20
  sent:
    arm64.movRegReg x0, x22
    arm64.b ifcont#22
  tryok#20:
    arm64.movRegReg x1, x21
    arm64.movRegReg x0, x22
  ifcont#22:
    arm64.movRegReg x21, x1
    arm64.movRegReg x22, x0
  whilehdr#12:
    arm64.storeSlotReg slot15, x21
    arm64.storeSlotReg slot12, x22
    arm64.cmp x21, 0
    arm64.b.eq sending
    arm64.b __il_body#68
  matchdefault#18:
    arm64.leaRdata x0, __str_blob_19  ; "panic at netpoll-socket.a-send-the-kernel-finished-is-not-sent-again-past-its-deadline.test:20: unreachable: a send with room in its buffer ends in its bytes or"...
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
    arm64.epilogue 240
    arm64.retIndirect
  __il_body#68:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __ms_close
  __il_cont#67:
    arm64.leaRdata x0, __str_rec_18  ; ""
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.add x0, x21, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.add x23, x22, 56
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x0, x23, x21
    arm64.storeBaseDispReg.word64 [x22 + 0], x23
    arm64.storeBaseDispReg.word64 [x22 + 8], x21
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x22 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x22 + 24], x0
    arm64.movImm x0, 1
    arm64.b whilehdr#23
  reading:
    arm64.movImm x0, 1024
  __il_body#100:
    arm64.movImm x1, 1
    arm64.bl __managed_mem_create
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#87
  tryerr#88:
    arm64.leaRdata x0, __str_blob_27  ; "panic at TcpClient.maxon:119: TcpClient.recv: create \xe2\x80\x94 bufferSize >= 0 guaranteed\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  tryok#87:
    arm64.loadRegBaseDisp.word64 x21, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl __ms_recv
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#89
  tryerr#90:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __ms_timed_out
    arm64.cbz x0, ifcont#94
  late#91:
    arm64.movImm x21, 5
    arm64.movRegReg x0, x23
    arm64.bl __ms_destruct
    arm64.b exitdrop#93
  ifcont#94:
    arm64.movImm x21, 3
    arm64.movRegReg x0, x23
    arm64.bl __ms_destruct
  exitdrop#93:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x21
    arm64.b ifelse#27
  tryok#89:
    arm64.movRegReg x0, x23
    arm64.bl __ms_destruct
    arm64.cmp x21, 0
    arm64.b.ne ifcont#96
  closed#95:
    arm64.movImm x21, 4
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x21
  ifelse#27:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x22
    arm64.b ifcont#28
  ifcont#96:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_set_length
    arm64.cmp x9, 0
    arm64.b.eq tryok#97
  tryerr#98:
    arm64.leaRdata x0, __str_blob_28  ; "panic at TcpClient.maxon:136: TcpClient.recv: setLength(received) \xe2\x80\x94 received is within the buffer's capacity\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  tryok#97:
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.movRegReg x21, x0
  __il_body#105:
    arm64.loadRegBaseDisp.word64 x23, [x21 + 8]
    arm64.movImm x24, 0
    arm64.b forhdr
  scan:
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x24
    arm64.bl __managed_byte_at
    arm64.cmp x9, 0
    arm64.b.ne tryerr#111
  tryok#110:
    arm64.cmp x0, 128
    arm64.movImm x1, 1
    arm64.b.ge critsplit
  orrhs:
    arm64.cmp x0, 13
    arm64.cset x0, eq
    arm64.b scmerge
  critsplit:
    arm64.movRegReg x0, x1
  scmerge:
    arm64.cbz x0, forstep
  notSingleByte:
    arm64.movImm x1, 0
    arm64.b __il_cont#104
  forstep:
    arm64.add x24, x24, 1
  forhdr:
    arm64.cmp x24, x23
    arm64.b.lo scan
  forexit:
    arm64.movImm x1, 1
  __il_cont#104:
    arm64.movRegReg x0, x21
    arm64.bl __str_of_buffer
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  __il_cont#101:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  got:
    arm64.loadRegBaseDisp.word64 x19, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x24, [x23 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x23 + 8]
    arm64.add x26, x21, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x28, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.movRegReg x0, x23
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot9
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x27
  ifcont#28:
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x1
  whilehdr#23:
    arm64.storeSlotReg slot9, x0
    arm64.storeSlotReg slot3, x22
    arm64.cbnz x0, reading
  whileexit#25:
    arm64.leaRdata x0, __str_rec_21  ; "reported="
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.loadRegSlot x0, slot15
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_22  ; " timeouts="
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.loadRegSlot x0, slot12
    arm64.movRegReg x1, x26
    arm64.bl __uint_to_string
    arm64.movRegReg x27, x0
    arm64.leaRdata x0, __str_rec_23  ; " heard="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot14, x1
    arm64.loadRegBaseDisp.word64 x28, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.storeSlotReg slot11, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.storeSlotReg slot8, x0
    arm64.leaRdata x1, __str_rec_24  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot7, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot6, x1
    arm64.add x2, x20, x23
    arm64.add x2, x2, x25
    arm64.add x2, x2, x27
    arm64.add x2, x2, x28
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot4, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot5, x0
    arm64.add x22, x0, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x22, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x19, x19, x27
    arm64.loadRegSlot x1, slot14
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x28
    arm64.bl __str_copy
    arm64.add x19, x19, x28
    arm64.loadRegSlot x2, slot8
    arm64.loadRegSlot x1, slot11
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot8
    arm64.add x19, x19, x0
    arm64.loadRegSlot x2, slot6
    arm64.loadRegSlot x1, slot7
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot6
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot5
    arm64.storeBaseDispReg.word64 [x0 + 0], x22
    arm64.loadRegSlot x1, slot4
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.movRegReg x0, x26
    arm64.bl __mm_decref
  __il_body#70:
    arm64.loadRegSlot x0, slot5
    arm64.bl __write_stdout
  __il_cont#69:
    arm64.loadRegSlot x0, slot5
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot3
    arm64.bl __str_decref
  exitdrop#29:
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_TcpClient
    arm64.movRegReg x20, x19
  exitdrop#11:
    arm64.loadRegSlot x0, slot1
    arm64.bl __destruct_TcpClient
  exitdrop#6:
    arm64.loadRegSlot x0, slot0
    arm64.bl __destruct_TcpListener
  exitterminal#5:
    arm64.movRegReg x0, x20
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
    arm64.epilogue 240
    arm64.retIndirect
  __rc_panic:
    arm64.leaRdata x0, __str_blob_43  ; "panic at TcpClient.maxon:107: Range check failed: value outside typealias 'BytePos'\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  tryerr#21:
    arm64.leaRdata x0, __str_blob_20  ; "panic at netpoll-socket.a-send-the-kernel-finished-is-not-sent-again-past-its-deadline.test:23: unreachable: the sending side is still open\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  tryerr#111:
    arm64.leaRdata x0, __str_blob_25  ; "panic at String.maxon:1133: scanSingleByteGraphemes: byteAt OOB \xe2\x80\x94 i < len invariant\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
}

func @__ms_send_from {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x1
    arm64.movRegReg x20, x2
    arm64.movRegReg x21, x3
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.cset x1, eq
    arm64.sub x22, x0, 1
    arm64.cbz x1, mschkoff
  closedthrow:
    arm64.movImm x0, 0
    arm64.movImm x9, 7
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.retIndirect
  mschkoff:
    arm64.cmp x20, 0
    arm64.b.lt msoob
  mschklen:
    arm64.cmp x21, 0
    arm64.b.lt msoob
  msbound:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.ge msownb
  msviewb:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x1, [x19 + 24]
  __il_body#22:
    arm64.asr x2, x1, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x2, x16
    arm64.movImm x4, 0
    arm64.sub x4, x4, x1
    arm64.lsl x1, x1, 3
    arm64.and x2, x4, x2
    arm64.and x1, x1, x3
    arm64.orr x1, x2, x1
  __il_cont#21:
    arm64.mul x0, x0, x1
  __il_body#23:
    arm64.add x0, x0, 7
    arm64.lsr x0, x0, 3
    arm64.b mschkcap
  msownb:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.loadRegBaseDisp.word64 x1, [x19 + 24]
  __il_body#28:
    arm64.asr x2, x1, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x2, x16
    arm64.movImm x4, 0
    arm64.sub x4, x4, x1
    arm64.lsl x1, x1, 3
    arm64.and x2, x4, x2
    arm64.and x1, x1, x3
    arm64.orr x1, x2, x1
  __il_cont#27:
    arm64.mul x0, x0, x1
  __il_body#29:
    arm64.add x0, x0, 7
    arm64.lsr x0, x0, 3
  mschkcap:
    arm64.add x1, x20, x21
    arm64.cmp x1, x0
    arm64.b.le mssend
  msoob:
    arm64.movImm x0, 0
    arm64.movImm x9, 1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.retIndirect
  mssend:
    arm64.bl __gt_io_park
    arm64.cmp x0, 0
    arm64.b.ne msfail
  mssendgo:
    arm64.movRegReg x0, x22
    arm64.bl __np_pd_op_begin
    arm64.movRegReg x23, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x19, x0, x20
    arm64.b mssendtry
  msclassify:
    arm64.movImm x1, 0
    arm64.sub x0, x1, x0
    arm64.cmp x0, 35
    arm64.cset x1, eq
    arm64.cmp x0, 36
    arm64.cset x2, eq
    arm64.cmp x0, 4
    arm64.cset x0, eq
    arm64.orr x1, x1, x2
    arm64.orr x0, x1, x0
    arm64.cbz x0, msfail
  mswait:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x22
    arm64.movRegReg x2, x23
    arm64.bl __np_pd_wait
    arm64.cmp x0, 2
    arm64.b.eq msfail
  mswaitlive:
    arm64.cmp x0, 1
    arm64.b.eq mslate
  mssendtry:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x21
    arm64.movImm x3, 524288
    arm64.importCall 68
    arm64.bl mrt_host_status_or_errno
    arm64.cmp x0, 0
    arm64.b.lt msclassify
    arm64.b msdone
  mslate:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __np_pd_mark_timed_out
    arm64.movImm x0, 0
    arm64.movImm x9, 4
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.retIndirect
  msfail:
    arm64.movImm x0, 0
    arm64.movImm x9, 4
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.retIndirect
  msdone:
    arm64.movImm x9, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.retIndirect
}
```

```TargetIr:arm64-linux
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
  __gt_seed_bytes@104 = i64 2048
  __gt_seed_sum_seen@112 = i64 0
  __gt_seed_count_seen@120 = i64 0
  __gt_seed_guard@128 = i64 0
  __gt_keeps_cpu_clock@136 = i64 0
  __gt_stack_pool@144 = i64 0
  __sched_active_workers@152 = i64 1
  __sched_max_active_workers@160 = i64 1
  __sched_tls_index@168 = i64 0
  __sched_tls_teb_offset@176 = i64 0
  __sched_procs@184 = i64 0
  __sched_allm@192 = i64 0
  __sched_async_preempt_off@200 = i64 0
  __sched_midle@208 = i64 0
  __sched_pidle@216 = i64 0
  __gt_gfree_head@224 = i64 0
  __gt_gfree_nostack_head@232 = i64 0
  __gt_cell_gfree_head@240 = i64 0
  __sched_arena_cursor@248 = i64 0
  __sched_arena_limit@256 = i64 0
  __gt_records_carved@264 = i64 0
  __gt_gfree_lock@272 = i64 0
  __gt_gfree_lock_owner@280 = i64 0
  __gt_gfree_lock_depth@288 = i64 0
  __sched_npidle@296 = i64 0
  __sched_runq_size@304 = i64 0
  __sched_global_pushes@312 = i64 0
  __sched_timer_scan_steps@320 = i64 0
  __sched_mcount@328 = i64 0
  __sched_nmidle@336 = i64 0
  __sched_nmidlelocked@344 = i64 0
  __sched_main_m@352 = i64 0
  __sched_phase@360 = i64 0
  __sched_lastpoll@368 = i64 0
  __sched_poll_until@376 = i64 0
  __sched_regain_head@384 = i64 0
  __np_poller@392 = i64 0
  __np_break@400 = i64 0
  __np_wake_sig@408 = i64 0
  __np_pd_table@416 = i64 0
  __np_pd_cap@424 = i64 0
  __np_waiters@432 = i64 0
  __np_orphan_exited@440 = i64 0
  __np_src_key_free@448 = i64 0
  __np_src_key_high@456 = i64 0
  __np_child_signal@464 = i64 0
  __np_pd_mtable@472 = i64 0
  __np_pd_mcap@480 = i64 0
  __sched_num_procs@488 = i64 0
  __sched_ncpu@496 = i64 0
  __sched_shutdown_flag@504 = i64 0
  __sched_lock@512 = i64 0
  __sched_lock_depth_sink@520 = i64 0
  __sched_nmspinning@528 = i64 0
  __sched_needspinning@536 = i64 0
  __sched_sysmon_event@544 = i64 0
  __sched_sysmon_wait@552 = i64 0
  __sched_timer_starts@560 = i64 0
  __mrt_envp@568 = i64 0
  __mrt_signal_stack_bytes@576 = i64 0
  __mrt_hwcap_atomics@584 = i64 0
  __mrt_tls_next_key@592 = i64 0
  __slab_arena_list@600 = i64 0
  __slab_arena_map_l1@608 = i64 0
  __slab_state@616 = i64 0
  __ms_wsa_started@624 = i64 0
  __mrt_program_started@632 = i8 0
}

func @main {
  entry:
    arm64.prologue 240
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
    arm64.leaRdata x0, __str_rec_16  ; "127.0.0.1"
    arm64.movImm x19, 0
  __il_body#38:
    arm64.bl String.addressableBytes
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __ml_bind
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#31
  tryerr#32:
    arm64.sub x0, x9, 1
    arm64.cmp x0, 0
    arm64.b.ne matchdefault#35
  matcharm#33:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.movImm x0, 1
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
    arm64.epilogue 240
    arm64.retIndirect
  matchdefault#35:
    arm64.movImm x19, 6
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.movImm x0, 1
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
    arm64.epilogue 240
    arm64.retIndirect
  tryok#31:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot0, x20
    arm64.movRegReg x0, x19
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __ms_destruct
  tryok#1:
    arm64.leaRdata x19, __str_rec_16  ; "127.0.0.1"
  __il_body#40:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __ml_port
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq __il_cont#39
  tryerr#42:
    arm64.leaRdata x0, __str_blob_29  ; "panic at TcpListener.maxon:31: TcpListener.port: this listener is closed, so it holds no bound port to report\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  __il_cont#39:
    arm64.loadRegBaseDisp.word64 x22, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x19, [x19 + 8]
    arm64.add x0, x19, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.add x24, x23, 56
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x24, x19
    arm64.storeBaseDispReg.word64 [x23 + 0], x24
    arm64.storeBaseDispReg.word64 [x23 + 8], x19
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x23 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x23 + 24], x0
    arm64.leaFuncAddr x0, TcpClient.connect
    arm64.bl __gt_spawn
    arm64.movRegReg x19, x0
    arm64.storeBaseDispReg.word64 [x19 + 424], x23
    arm64.storeBaseDispReg.word64 [x19 + 432], x21
    arm64.leaFuncAddr x0, __gt_release_TcpClient.connect
    arm64.storeBaseDispReg.word64 [x19 + 88], x0
    arm64.movRegReg x0, x19
    arm64.bl __gt_ready
  __il_body#72:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __ml_accept
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#73
  tryerr#74:
    arm64.sub x21, x9, 1
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __ms_timed_out
    arm64.cbz x0, ifcont#76
  late#75:
    arm64.movImm x0, 6
    arm64.movImm x20, 0
    arm64.b __il_cont#71
  ifcont#76:
    arm64.jumpTable x21, default=matchdead [[0..1]=matcharm#79, [2]=matcharm#78, [3]=matcharm#79, [4]=matcharm#78, [5]=matcharm#77] @__jumptable_50
  matchdead:
  matcharm#79:
    arm64.leaRdata x0, __str_rec_30  ; "panic at TcpListener.maxon:53: "
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.leaRdata x0, __str_rec_31  ; "TcpListener.accept: `accept` does not throw `"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.cmp x21, 0
    arm64.cset x0, eq
    arm64.leaRdata x1, __str_rec_32  ; "resolveFailed"
    arm64.mul x0, x0, x1
    arm64.cmp x21, 1
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_33  ; "bindFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x21, 2
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_34  ; "acceptFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x21, 3
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_35  ; "addressFailed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x21, 4
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_36  ; "closed"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.cmp x21, 5
    arm64.cset x1, eq
    arm64.leaRdata x2, __str_rec_37  ; "acceptRetriable"
    arm64.mul x1, x1, x2
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.leaRdata x0, __str_rec_38  ; "`"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_24  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot16, x0
    arm64.add x1, x20, x23
    arm64.add x1, x1, x24
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot10, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot13, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x28, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x19, x19, x24
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x19, x19, x26
    arm64.loadRegSlot x2, slot16
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot16
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot13
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot10
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
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
    arm64.epilogue 240
    arm64.retIndirect
  matcharm#78:
    arm64.movImm x0, 8
    arm64.movImm x20, 0
    arm64.b __il_cont#71
  matcharm#77:
    arm64.movImm x0, 9
    arm64.movImm x20, 0
    arm64.b __il_cont#71
  tryok#73:
    arm64.movRegReg x0, x21
    arm64.bl __mm_own
    arm64.movRegReg x20, x0
  __il_body#84:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_own
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __ms_destruct
  __il_cont#83:
    arm64.movRegReg x0, x21
    arm64.bl __ms_destruct
  exitterminal#82:
    arm64.movImm x0, 0
    arm64.movRegReg x20, x22
  __il_cont#71:
    arm64.storeSlotReg slot1, x20
    arm64.cmp x0, 0
    arm64.b.eq tryok#3
  tryerr#4:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __gt_promise_drop
    arm64.b exitdrop#6
  tryok#3:
    arm64.movRegReg x0, x19
    arm64.bl __gt_try_await
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot2, x19
    arm64.cmp x9, 0
    arm64.b.eq tryok#7
  tryerr#8:
    arm64.movImm x19, 1
    arm64.movRegReg x0, x20
    arm64.bl __destruct_TcpClient
    arm64.movRegReg x20, x19
    arm64.b exitdrop#6
  tryok#7:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl TcpClient.setWriteDeadline
    arm64.cmp x9, 0
    arm64.b.eq tryok#9
  tryerr#10:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __destruct_TcpClient
    arm64.b exitdrop#11
  tryok#9:
    arm64.movImm x0, 100
  __il_body#44:
    arm64.bl __gt_sleep
  __il_cont#43:
    arm64.movImm x21, 0
    arm64.movImm x22, 0
    arm64.b whilehdr#12
  sending:
    arm64.leaRdata x0, __str_rec_17  ; "xyz"
  __il_body#46:
    arm64.bl String.addressableBytes
    arm64.movRegReg x23, x0
    arm64.movImm x24, 0
    arm64.loadRegBaseDisp.word64 x25, [x23 + 8]
    arm64.b whilehdr#47
  send_loop:
    arm64.loadRegBaseDisp.word64 x26, [x19 + 0]
    arm64.movRegReg x0, x26
    arm64.bl __mm_retain
    arm64.movRegReg x27, x0
    arm64.cmp x25, 1048576
    arm64.b.lo ternarytrue
  ternaryfalse:
    arm64.movImm x3, 1048576
    arm64.b matchcont#52
  ternarytrue:
    arm64.movRegReg x3, x25
  matchcont#52:
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __ms_send_from
    arm64.movRegReg x26, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#53
  tryerr#54:
    arm64.sub x24, x9, 1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __ms_timed_out
    arm64.cbz x0, ifcont#58
  late#55:
    arm64.movImm x24, 5
    arm64.movRegReg x0, x27
    arm64.bl __ms_destruct
    arm64.b exitdrop#57
  ifcont#58:
    arm64.cmp x24, 0
    arm64.b.ne matchdefault#60
  matcharm#59:
    arm64.leaRdata x0, __str_blob_26  ; "panic at TcpClient.maxon:97: TcpClient.send: sendFrom OOB \xe2\x80\x94 loop invariant violated\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  matchdefault#60:
    arm64.movImm x24, 2
    arm64.movRegReg x0, x27
    arm64.bl __ms_destruct
  exitdrop#57:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movRegReg x0, x24
    arm64.b exitterminal#56
  tryok#53:
    arm64.movRegReg x0, x27
    arm64.bl __ms_destruct
    arm64.cmp x26, 0
    arm64.b.ne ifcont#63
  closed#62:
    arm64.movImm x24, 4
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movRegReg x0, x24
  exitterminal#56:
    arm64.add x0, x0, 1
    arm64.movImm x1, 0
    arm64.b __il_cont#45
  ifcont#63:
    arm64.add x24, x24, x26
    arm64.sub x0, x25, x26
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x25, x16
    arm64.and x1, x1, x26
    arm64.eor x2, x25, x26
    arm64.movImm x16, 18446744073709551615
    arm64.eor x2, x2, x16
    arm64.and x2, x2, x0
    arm64.orr x1, x1, x2
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __rc_ok:
    arm64.movRegReg x25, x0
  whilehdr#47:
    arm64.cmp x25, 0
    arm64.b.hi send_loop
  whileexit#49:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitterminal#64:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x24
  __il_cont#45:
    arm64.cmp x0, 0
    arm64.b.eq sent
  ifelse#16:
    arm64.sub x0, x0, 1
    arm64.cmp x0, 5
    arm64.b.ne matchdefault#18
  matcharm#17:
    arm64.add x22, x22, 1
  matchcont#19:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl TcpClient.setWriteDeadline
    arm64.cmp x9, 0
    arm64.b.ne tryerr#21
    arm64.b tryok#20
  sent:
    arm64.movRegReg x0, x22
    arm64.b ifcont#22
  tryok#20:
    arm64.movRegReg x1, x21
    arm64.movRegReg x0, x22
  ifcont#22:
    arm64.movRegReg x21, x1
    arm64.movRegReg x22, x0
  whilehdr#12:
    arm64.storeSlotReg slot15, x21
    arm64.storeSlotReg slot12, x22
    arm64.cmp x21, 0
    arm64.b.eq sending
    arm64.b __il_body#68
  matchdefault#18:
    arm64.leaRdata x0, __str_blob_19  ; "panic at netpoll-socket.a-send-the-kernel-finished-is-not-sent-again-past-its-deadline.test:20: unreachable: a send with room in its buffer ends in its bytes or"...
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
    arm64.epilogue 240
    arm64.retIndirect
  __il_body#68:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __ms_close
  __il_cont#67:
    arm64.leaRdata x0, __str_rec_18  ; ""
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.add x0, x21, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.add x23, x22, 56
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x0, x23, x21
    arm64.storeBaseDispReg.word64 [x22 + 0], x23
    arm64.storeBaseDispReg.word64 [x22 + 8], x21
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x22 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x22 + 24], x0
    arm64.movImm x0, 1
    arm64.b whilehdr#23
  reading:
    arm64.movImm x0, 1024
  __il_body#100:
    arm64.movImm x1, 1
    arm64.bl __managed_mem_create
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#87
  tryerr#88:
    arm64.leaRdata x0, __str_blob_27  ; "panic at TcpClient.maxon:119: TcpClient.recv: create \xe2\x80\x94 bufferSize >= 0 guaranteed\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  tryok#87:
    arm64.loadRegBaseDisp.word64 x21, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl __ms_recv
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#89
  tryerr#90:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __ms_timed_out
    arm64.cbz x0, ifcont#94
  late#91:
    arm64.movImm x21, 5
    arm64.movRegReg x0, x23
    arm64.bl __ms_destruct
    arm64.b exitdrop#93
  ifcont#94:
    arm64.movImm x21, 3
    arm64.movRegReg x0, x23
    arm64.bl __ms_destruct
  exitdrop#93:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x21
    arm64.b ifelse#27
  tryok#89:
    arm64.movRegReg x0, x23
    arm64.bl __ms_destruct
    arm64.cmp x21, 0
    arm64.b.ne ifcont#96
  closed#95:
    arm64.movImm x21, 4
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x21
  ifelse#27:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x22
    arm64.b ifcont#28
  ifcont#96:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_set_length
    arm64.cmp x9, 0
    arm64.b.eq tryok#97
  tryerr#98:
    arm64.leaRdata x0, __str_blob_28  ; "panic at TcpClient.maxon:136: TcpClient.recv: setLength(received) \xe2\x80\x94 received is within the buffer's capacity\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  tryok#97:
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.movRegReg x21, x0
  __il_body#105:
    arm64.loadRegBaseDisp.word64 x23, [x21 + 8]
    arm64.movImm x24, 0
    arm64.b forhdr
  scan:
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x24
    arm64.bl __managed_byte_at
    arm64.cmp x9, 0
    arm64.b.ne tryerr#111
  tryok#110:
    arm64.cmp x0, 128
    arm64.movImm x1, 1
    arm64.b.ge critsplit
  orrhs:
    arm64.cmp x0, 13
    arm64.cset x0, eq
    arm64.b scmerge
  critsplit:
    arm64.movRegReg x0, x1
  scmerge:
    arm64.cbz x0, forstep
  notSingleByte:
    arm64.movImm x1, 0
    arm64.b __il_cont#104
  forstep:
    arm64.add x24, x24, 1
  forhdr:
    arm64.cmp x24, x23
    arm64.b.lo scan
  forexit:
    arm64.movImm x1, 1
  __il_cont#104:
    arm64.movRegReg x0, x21
    arm64.bl __str_of_buffer
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  __il_cont#101:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  got:
    arm64.loadRegBaseDisp.word64 x19, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x24, [x23 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x23 + 8]
    arm64.add x26, x21, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x28, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.movRegReg x0, x23
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot9
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x27
  ifcont#28:
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x1
  whilehdr#23:
    arm64.storeSlotReg slot9, x0
    arm64.storeSlotReg slot3, x22
    arm64.cbnz x0, reading
  whileexit#25:
    arm64.leaRdata x0, __str_rec_21  ; "reported="
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.loadRegSlot x0, slot15
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_22  ; " timeouts="
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.loadRegSlot x0, slot12
    arm64.movRegReg x1, x26
    arm64.bl __uint_to_string
    arm64.movRegReg x27, x0
    arm64.leaRdata x0, __str_rec_23  ; " heard="
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot14, x1
    arm64.loadRegBaseDisp.word64 x28, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.storeSlotReg slot11, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.storeSlotReg slot8, x0
    arm64.leaRdata x1, __str_rec_24  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot7, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot6, x1
    arm64.add x2, x20, x23
    arm64.add x2, x2, x25
    arm64.add x2, x2, x27
    arm64.add x2, x2, x28
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot4, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot5, x0
    arm64.add x22, x0, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x22, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x19, x19, x27
    arm64.loadRegSlot x1, slot14
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x28
    arm64.bl __str_copy
    arm64.add x19, x19, x28
    arm64.loadRegSlot x2, slot8
    arm64.loadRegSlot x1, slot11
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot8
    arm64.add x19, x19, x0
    arm64.loadRegSlot x2, slot6
    arm64.loadRegSlot x1, slot7
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot6
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot5
    arm64.storeBaseDispReg.word64 [x0 + 0], x22
    arm64.loadRegSlot x1, slot4
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.movRegReg x0, x26
    arm64.bl __mm_decref
  __il_body#70:
    arm64.loadRegSlot x0, slot5
    arm64.bl __write_stdout
  __il_cont#69:
    arm64.loadRegSlot x0, slot5
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot3
    arm64.bl __str_decref
  exitdrop#29:
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_TcpClient
    arm64.movRegReg x20, x19
  exitdrop#11:
    arm64.loadRegSlot x0, slot1
    arm64.bl __destruct_TcpClient
  exitdrop#6:
    arm64.loadRegSlot x0, slot0
    arm64.bl __destruct_TcpListener
  exitterminal#5:
    arm64.movRegReg x0, x20
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
    arm64.epilogue 240
    arm64.retIndirect
  __rc_panic:
    arm64.leaRdata x0, __str_blob_43  ; "panic at TcpClient.maxon:107: Range check failed: value outside typealias 'BytePos'\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  tryerr#21:
    arm64.leaRdata x0, __str_blob_20  ; "panic at netpoll-socket.a-send-the-kernel-finished-is-not-sent-again-past-its-deadline.test:23: unreachable: the sending side is still open\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
  tryerr#111:
    arm64.leaRdata x0, __str_blob_25  ; "panic at String.maxon:1133: scanSingleByteGraphemes: byteAt OOB \xe2\x80\x94 i < len invariant\x0a"
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
    arm64.epilogue 240
    arm64.retIndirect
}

func @__ms_send_from {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x1
    arm64.movRegReg x20, x2
    arm64.movRegReg x21, x3
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.cset x1, eq
    arm64.sub x22, x0, 1
    arm64.cbz x1, mschkoff
  closedthrow:
    arm64.movImm x0, 0
    arm64.movImm x9, 7
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.retIndirect
  mschkoff:
    arm64.cmp x20, 0
    arm64.b.lt msoob
  mschklen:
    arm64.cmp x21, 0
    arm64.b.lt msoob
  msbound:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.ge msownb
  msviewb:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x1, [x19 + 24]
  __il_body#22:
    arm64.asr x2, x1, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x2, x16
    arm64.movImm x4, 0
    arm64.sub x4, x4, x1
    arm64.lsl x1, x1, 3
    arm64.and x2, x4, x2
    arm64.and x1, x1, x3
    arm64.orr x1, x2, x1
  __il_cont#21:
    arm64.mul x0, x0, x1
  __il_body#23:
    arm64.add x0, x0, 7
    arm64.lsr x0, x0, 3
    arm64.b mschkcap
  msownb:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.loadRegBaseDisp.word64 x1, [x19 + 24]
  __il_body#28:
    arm64.asr x2, x1, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x2, x16
    arm64.movImm x4, 0
    arm64.sub x4, x4, x1
    arm64.lsl x1, x1, 3
    arm64.and x2, x4, x2
    arm64.and x1, x1, x3
    arm64.orr x1, x2, x1
  __il_cont#27:
    arm64.mul x0, x0, x1
  __il_body#29:
    arm64.add x0, x0, 7
    arm64.lsr x0, x0, 3
  mschkcap:
    arm64.add x1, x20, x21
    arm64.cmp x1, x0
    arm64.b.le mssend
  msoob:
    arm64.movImm x0, 0
    arm64.movImm x9, 1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.retIndirect
  mssend:
    arm64.bl __gt_io_park
    arm64.cmp x0, 0
    arm64.b.ne msfail
  mssendgo:
    arm64.movRegReg x0, x22
    arm64.bl __np_pd_op_begin
    arm64.movRegReg x23, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x19, x0, x20
    arm64.b mssendtry
  msclassify:
    arm64.movImm x1, 0
    arm64.sub x0, x1, x0
    arm64.cmp x0, 11
    arm64.cset x1, eq
    arm64.cmp x0, 115
    arm64.cset x2, eq
    arm64.cmp x0, 4
    arm64.cset x0, eq
    arm64.orr x1, x1, x2
    arm64.orr x0, x1, x0
    arm64.cbz x0, msfail
  mswait:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x22
    arm64.movRegReg x2, x23
    arm64.bl __np_pd_wait
    arm64.cmp x0, 2
    arm64.b.eq msfail
  mswaitlive:
    arm64.cmp x0, 1
    arm64.b.eq mslate
  mssendtry:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x21
    arm64.movImm x3, 16384
    arm64.movImm x4, 0
    arm64.movImm x5, 0
    arm64.syscall 206
    arm64.cmp x0, 0
    arm64.b.lt msclassify
    arm64.b msdone
  mslate:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __np_pd_mark_timed_out
    arm64.movImm x0, 0
    arm64.movImm x9, 4
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.retIndirect
  msfail:
    arm64.movImm x0, 0
    arm64.movImm x9, 4
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.retIndirect
  msdone:
    arm64.movImm x9, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.retIndirect
}
```
