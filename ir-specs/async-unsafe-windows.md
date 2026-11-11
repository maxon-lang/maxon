---
feature: async-unsafe-windows
status: experimental
keywords: [slab, allocator, inline, asynchronous preemption, safe point, green threads, services]
category: codegen
---

# The inline slab pop is an asynchronously unsafe window

Each case pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window -->
A service creates a constant-capacity array in a program with green threads. The create is expanded in its
caller as the allocator's hit path: no call to `__mm_alloc`, with every block from the first read of the
machine to the store that completes the pop marked `[async-unsafe]`, and the function lists the code ranges
those blocks occupy.
```maxon
typealias Word = int(0 to u32.max)
typealias WordArray = Array with Word

type Counter
	var total as Word

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function run(rounds Word)
		var round = 0 as Word

		while round < rounds 'eachRound'
			let scratch = WordArray.create(4, zeroed: false)
			scratch.push(round)
			self.total = self.total + (scratch.count() as Word)
			round = round + 1
		end 'eachRound'
	end 'run'

	export function result() returns Word
		return self.total
	end 'result'
end 'Counter'

function main() returns ExitCode
	let counter = spawn Counter.create()
	try counter.run(10) otherwise panic("a mailbox holds the one message")
	let total = try await counter.result() otherwise 0
	print("{total}\n")
	return 0
end 'main'
```
```stdout
10
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
  __sched_num_procs@440 = i64 0
  __sched_ncpu@448 = i64 0
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

func @Counter.run {
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
    x64.movRegReg r12, rdx
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic#5
  __rc_chk#6:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic#5
  __rc_ok#4:
    x64.movRegImm32 rsi, 0
    x64.jmp whilehdr
  eachRound:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r9, 4
    x64.movRegImm32 r10, 0
    x64.movRegImm32 r11, 0
  cls#20: [async-unsafe]
    x64.leaRegRdata rax, [rip + __slab_size_to_class8]
    x64.loadRegBaseDisp.byte rax, [rax + 12]
  cls#22: [async-unsafe]
    x64.leaRegGlobal rcx, __slab_state
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __ia_slow
  state: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 24]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, walk#31
  walk#24: [async-unsafe]
    x64.loadRegBaseIndexScale.word64 r13, [rcx + rax*8 + 139328]
    x64.cmpRegImm32 r13, 0
    x64.jcc equal, __ia_slow
  probe#34: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [r13 + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  probe#35: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [r13 + 48]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __ia_slow
  probe#33: [async-unsafe]
    x64.aluBaseDispImm32.add [rcx + 152120], 1
    x64.aluBaseDispImm32.add [rcx + 152128], 80
    x64.jmp pop#32
  walk#31: [async-unsafe]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  walk#25: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdi, [rdx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __ia_slow
  walk#28: [async-unsafe]
    x64.loadRegBaseDisp.word64 r13, [rdi + 32]
    x64.cmpRegImm32 r13, 255
    x64.jcc greaterEqual, __ia_slow
  walk#30: [async-unsafe]
    x64.imulRegRegImm32 r14, r13, 544
    x64.leaRegRegReg r14, rcx, r14
    x64.loadRegBaseIndexScale.word64 r14, [r14 + rax*8 + 608]
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, __ia_slow
  ownprobe#37: [async-unsafe]
    x64.loadRegBaseDisp.word64 r15, [r14 + 16]
    x64.cmpRegImm32 r15, 0
    x64.jcc equal, __ia_slow
  ownprobe#38: [async-unsafe]
    x64.loadRegBaseDisp.word64 r15, [r14 + 48]
    x64.cmpRegReg r15, rdi
    x64.jcc notEqual, __ia_slow
  ownprobe#36: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, ownthread#40
  ownthread#39: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 112]
    x64.aluBaseDispImm32.add [rdx + 512], 1
    x64.aluBaseDispImm32.add [rdx + 528], 96
  ownthread#40: [async-unsafe]
    x64.imulRegRegImm32 rdx, r13, 48
    x64.leaRegRegImm32 rcx, rcx, 139872
    x64.leaRegRegReg rcx, rcx, rdx
    x64.aluBaseDispImm32.add [rcx + 8], 1
    x64.aluBaseDispImm32.add [rcx + 16], 80
    x64.movRegReg r13, r14
  pop#32: [async-unsafe]
    x64.leaRegRdata rcx, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rcx, [rcx + rax*8 + 0]
    x64.loadRegBaseDisp.word64 r14, [r13 + 8]
    x64.cmpRegImm32 r14, 0
    x64.jcc notEqual, pop#41
  pop#42: [async-unsafe]
    x64.loadRegBaseDisp.word64 rax, [r13 + 40]
    x64.loadRegBaseDisp.word64 rdx, [r13 + 32]
    x64.leaRegRdata rdi, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rdx, [rdi + rdx*8 + 4]
    x64.movRegReg rdi, rcx
    x64.imulRegReg rdi, rcx, rdx
    x64.loadRegBaseDisp.word64 rdx, [r13 + 0]
    x64.leaRegRegReg rdx, rdx, rdi
    x64.cmpRegReg rax, rdx
    x64.jcc greaterEqual, pop#44
  pop#43: [async-unsafe]
    x64.leaRegRegReg rcx, rax, rcx
    x64.storeBaseDispReg.word64 [r13 + 40], rcx
    x64.jmp pop#45
  pop#41: [async-unsafe]
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
    x64.storeBaseDispReg.word64 [r13 + 8], rax
    x64.movRegReg rdi, r14
    x64.x64MemFill 0x00
    x64.movRegReg rax, r14
  pop#45: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 16]
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeBaseDispReg.word64 [r13 + 16], rcx
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#47
  pop#48: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 48]
    x64.loadRegBaseDisp.word64 rdx, [r13 + 32]
    x64.leaRegGlobal rdi, __slab_state
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#49
  pop#50: [async-unsafe]
    x64.leaRegRegImm32 rcx, rdi, 138720
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
    x64.jmp pop#46
  pop#49: [async-unsafe]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, pop#52
  pop#51: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 32]
    x64.imulRegRegImm32 rcx, rcx, 544
    x64.leaRegRegReg rcx, rdi, rcx
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
  pop#46: [async-unsafe]
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rcx + 0], rdx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 48], rcx
  pop#47: [async-unsafe]
    x64.storeBaseDispReg.word64 [rax + 0], r11
    x64.leaRegRegImm32 rcx, rax, 16
  __ia_done:
    x64.storeBaseDispReg.word64 [rcx + 24], r9
    x64.storeBaseDispReg.word64 [rcx + 40], r10
    x64.movRegImm32 rax, 4
    x64.leaRegRegImm32 rdx, rcx, 64
    x64.storeBaseDispReg.word64 [rcx + 0], rdx
    x64.storeBaseDispReg.word64 [rcx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rcx + 32], rax
    x64.storeBaseDispReg.word64 [rcx + 8], r8
  __im_cont#16:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 16]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rdx, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word32 [rdx + rax*4 + 0], rsi
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __im_cont#53:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic#8
  __rc_chk#9:
    x64.movRegImm32 rdi, 4294967295
    x64.cmpRegReg rdx, rdi
    x64.jcc greater, __rc_panic#8
  __rc_ok#7:
    x64.leaRegRegReg rax, rax, rdx
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic#11
  __rc_chk#12:
    x64.movRegImm32 rdx, 4294967295
    x64.cmpRegReg rax, rdx
    x64.jcc greater, __rc_panic#11
  __rc_ok#10:
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.leaRegRegImm32 r13, rsi, 1
  __rc_ok#13:
    x64.callDirect __managed_decref
    x64.movRegReg rsi, r13
  whilehdr:
    x64.cmpRegReg rsi, r12
    x64.jcc less, eachRound
  whileexit:
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __ia_slow:
    x64.movRegImm32 rcx, 80
    x64.storeSlotReg slot0, rsi
    x64.movRegReg rdx, r11
    x64.callDirect __mm_alloc_slow
    x64.movRegReg rcx, r8
    x64.loadRegSlot rsi, slot0
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r9, 4
    x64.movRegImm32 r10, 0
    x64.jmp __ia_done
  __im_slow:
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot0, rsi
    x64.movRegReg rdx, rsi
    x64.callDirect __managed_push
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rsi, slot0
    x64.jmp __im_cont#53
  __rc_panic#5:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:12: Range check failed: value outside typealias 'Word"...
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  pop#44: [async-unsafe]
    x64.callDirect __slab_past_end
    x64.movRegReg rax, r8
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  pop#52: [async-unsafe]
    x64.callDirect __slab_evict_unowned
    x64.movRegReg rax, r8
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#8:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:18: Range check failed: value outside typealias 'Word"...
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#11:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:18: Range check failed: value outside typealias 'Word"...
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
    async-unsafe [00bd, 0449)
    async-unsafe [05b4, 05e2)
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
    x64.prologue 72
    x64.movRegImm32 rbx, 1024
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegFunc rcx, [rip + Counter.__start_Counter.create]
    x64.leaRegFunc rsi, [rip + Counter.__behave]
    x64.leaRegFunc rdi, [rip + Counter.__finish]
    x64.movRegImm32 rax, 16
    x64.movRegReg rdx, r8
    x64.movRegReg r9, rbx
    x64.callDirect __cown_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __cown_handle_destruct]
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 10
    x64.callDirect __cown_staging
    x64.movRegReg r13, r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 0], rax
    x64.storeBaseDispReg.word64 [r13 + 8], rbx
    x64.movRegImm32 rax, 16
    x64.movRegImm32 r9, 0
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r13
    x64.callDirect __cown_send
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, sendrefused#1
  tryok#4:
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg rbx, r8
    x64.callDirect __cown_staging
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 8], rbx
    x64.movRegImm32 rax, 16
    x64.movRegImm32 r9, 0
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __cown_send
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, sendrefused#6
  sendaccepted:
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#8
  tryerr#9:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok#8:
    x64.movRegReg rbx, r8
  trycont:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot3, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot2
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  sendrefused#1:
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.storeSlotReg slot4, r8
    x64.callDirect __mm_alloc
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot4
    x64.leaRegRegImm32 rcx, r8, -1
    x64.storeBaseDispReg.word64 [rax + 0], rcx
    x64.loadRegBaseDisp.word64 rcx, [r13 + 8]
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#5
    x64.jmp tryok#4
  sendrefused#6:
    x64.movRegReg rcx, rbx
    x64.callDirect __cown_reply_refused
    x64.jmp tryerr#9
  tryerr#5:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:30: a mailbox holds the one message\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
}

func @Counter.__behave {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rcx, [rcx + 40]
    x64.loadRegBaseDisp.word64 rax, [rdx + 0]
    x64.jumpTable rax, default=unknowntag [[0]=run, [1]=result] @__jumptable_10
  unknowntag:
    x64.callDirect __cown_unknown_tag
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  run:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.callDirect Counter.run
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  result:
    x64.loadRegBaseDisp.word64 rsi, [rdx + 8]
  __il_body:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
  __il_cont:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rsi
    x64.callDirect __gt_cell_complete
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @Counter.__finish {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rcx, [rcx + 40]
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @Counter.__start_Counter.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 r12, [rdx + 24]
  __il_body:
    x64.movRegImm32 r13, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.storeBaseDispReg.word64 [r14 + 0], r13
  __il_cont:
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.storeBaseDispReg.word64 [rbx + 40], r14
    x64.epilogue 32
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
  __mrt_program_started@616 = i8 0
}

func @Counter.run {
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
    x64.movRegReg r12, rdx
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic#5
  __rc_chk#6:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic#5
  __rc_ok#4:
    x64.movRegImm32 rsi, 0
    x64.jmp whilehdr
  eachRound:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r9, 4
    x64.movRegImm32 r10, 0
    x64.movRegImm32 r11, 0
  cls#20: [async-unsafe]
    x64.leaRegRdata rax, [rip + __slab_size_to_class8]
    x64.loadRegBaseDisp.byte rax, [rax + 12]
  cls#22: [async-unsafe]
    x64.leaRegGlobal rcx, __slab_state
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __ia_slow
  state: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 24]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, walk#31
  walk#24: [async-unsafe]
    x64.loadRegBaseIndexScale.word64 r13, [rcx + rax*8 + 139328]
    x64.cmpRegImm32 r13, 0
    x64.jcc equal, __ia_slow
  probe#34: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [r13 + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  probe#35: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [r13 + 48]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __ia_slow
  probe#33: [async-unsafe]
    x64.aluBaseDispImm32.add [rcx + 152120], 1
    x64.aluBaseDispImm32.add [rcx + 152128], 80
    x64.jmp pop#32
  walk#31: [async-unsafe]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  walk#25: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdi, [rdx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __ia_slow
  walk#28: [async-unsafe]
    x64.loadRegBaseDisp.word64 r13, [rdi + 32]
    x64.cmpRegImm32 r13, 255
    x64.jcc greaterEqual, __ia_slow
  walk#30: [async-unsafe]
    x64.imulRegRegImm32 r14, r13, 544
    x64.leaRegRegReg r14, rcx, r14
    x64.loadRegBaseIndexScale.word64 r14, [r14 + rax*8 + 608]
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, __ia_slow
  ownprobe#37: [async-unsafe]
    x64.loadRegBaseDisp.word64 r15, [r14 + 16]
    x64.cmpRegImm32 r15, 0
    x64.jcc equal, __ia_slow
  ownprobe#38: [async-unsafe]
    x64.loadRegBaseDisp.word64 r15, [r14 + 48]
    x64.cmpRegReg r15, rdi
    x64.jcc notEqual, __ia_slow
  ownprobe#36: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, ownthread#40
  ownthread#39: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 112]
    x64.aluBaseDispImm32.add [rdx + 512], 1
    x64.aluBaseDispImm32.add [rdx + 528], 96
  ownthread#40: [async-unsafe]
    x64.imulRegRegImm32 rdx, r13, 48
    x64.leaRegRegImm32 rcx, rcx, 139872
    x64.leaRegRegReg rcx, rcx, rdx
    x64.aluBaseDispImm32.add [rcx + 8], 1
    x64.aluBaseDispImm32.add [rcx + 16], 80
    x64.movRegReg r13, r14
  pop#32: [async-unsafe]
    x64.leaRegRdata rcx, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rcx, [rcx + rax*8 + 0]
    x64.loadRegBaseDisp.word64 r14, [r13 + 8]
    x64.cmpRegImm32 r14, 0
    x64.jcc notEqual, pop#41
  pop#42: [async-unsafe]
    x64.loadRegBaseDisp.word64 rax, [r13 + 40]
    x64.loadRegBaseDisp.word64 rdx, [r13 + 32]
    x64.leaRegRdata rdi, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rdx, [rdi + rdx*8 + 4]
    x64.movRegReg rdi, rcx
    x64.imulRegReg rdi, rcx, rdx
    x64.loadRegBaseDisp.word64 rdx, [r13 + 0]
    x64.leaRegRegReg rdx, rdx, rdi
    x64.cmpRegReg rax, rdx
    x64.jcc greaterEqual, pop#44
  pop#43: [async-unsafe]
    x64.leaRegRegReg rcx, rax, rcx
    x64.storeBaseDispReg.word64 [r13 + 40], rcx
    x64.jmp pop#45
  pop#41: [async-unsafe]
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
    x64.storeBaseDispReg.word64 [r13 + 8], rax
    x64.movRegReg rdi, r14
    x64.x64MemFill 0x00
    x64.movRegReg rax, r14
  pop#45: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 16]
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeBaseDispReg.word64 [r13 + 16], rcx
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#47
  pop#48: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 48]
    x64.loadRegBaseDisp.word64 rdx, [r13 + 32]
    x64.leaRegGlobal rdi, __slab_state
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#49
  pop#50: [async-unsafe]
    x64.leaRegRegImm32 rcx, rdi, 138720
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
    x64.jmp pop#46
  pop#49: [async-unsafe]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, pop#52
  pop#51: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 32]
    x64.imulRegRegImm32 rcx, rcx, 544
    x64.leaRegRegReg rcx, rdi, rcx
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
  pop#46: [async-unsafe]
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rcx + 0], rdx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 48], rcx
  pop#47: [async-unsafe]
    x64.storeBaseDispReg.word64 [rax + 0], r11
    x64.leaRegRegImm32 rcx, rax, 16
  __ia_done:
    x64.storeBaseDispReg.word64 [rcx + 24], r9
    x64.storeBaseDispReg.word64 [rcx + 40], r10
    x64.movRegImm32 rax, 4
    x64.leaRegRegImm32 rdx, rcx, 64
    x64.storeBaseDispReg.word64 [rcx + 0], rdx
    x64.storeBaseDispReg.word64 [rcx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rcx + 32], rax
    x64.storeBaseDispReg.word64 [rcx + 8], r8
  __im_cont#16:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 16]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rdx, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word32 [rdx + rax*4 + 0], rsi
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __im_cont#53:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic#8
  __rc_chk#9:
    x64.movRegImm32 rdi, 4294967295
    x64.cmpRegReg rdx, rdi
    x64.jcc greater, __rc_panic#8
  __rc_ok#7:
    x64.leaRegRegReg rax, rax, rdx
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic#11
  __rc_chk#12:
    x64.movRegImm32 rdx, 4294967295
    x64.cmpRegReg rax, rdx
    x64.jcc greater, __rc_panic#11
  __rc_ok#10:
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.leaRegRegImm32 r13, rsi, 1
  __rc_ok#13:
    x64.callDirect __managed_decref
    x64.movRegReg rsi, r13
  whilehdr:
    x64.cmpRegReg rsi, r12
    x64.jcc less, eachRound
  whileexit:
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __ia_slow:
    x64.movRegImm32 rcx, 80
    x64.storeSlotReg slot0, rsi
    x64.movRegReg rdx, r11
    x64.callDirect __mm_alloc_slow
    x64.movRegReg rcx, r8
    x64.loadRegSlot rsi, slot0
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r9, 4
    x64.movRegImm32 r10, 0
    x64.jmp __ia_done
  __im_slow:
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot0, rsi
    x64.movRegReg rdx, rsi
    x64.callDirect __managed_push
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rsi, slot0
    x64.jmp __im_cont#53
  __rc_panic#5:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:12: Range check failed: value outside typealias 'Word"...
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  pop#44: [async-unsafe]
    x64.callDirect __slab_past_end
    x64.movRegReg rax, r8
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  pop#52: [async-unsafe]
    x64.callDirect __slab_evict_unowned
    x64.movRegReg rax, r8
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#8:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:18: Range check failed: value outside typealias 'Word"...
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#11:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:18: Range check failed: value outside typealias 'Word"...
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
    async-unsafe [00bd, 0449)
    async-unsafe [05b4, 05e2)
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
    x64.prologue 72
    x64.movRegImm32 rbx, 1024
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegFunc rcx, [rip + Counter.__start_Counter.create]
    x64.leaRegFunc rsi, [rip + Counter.__behave]
    x64.leaRegFunc rdi, [rip + Counter.__finish]
    x64.movRegImm32 rax, 16
    x64.movRegReg rdx, r8
    x64.movRegReg r9, rbx
    x64.callDirect __cown_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __cown_handle_destruct]
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 10
    x64.callDirect __cown_staging
    x64.movRegReg r13, r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 0], rax
    x64.storeBaseDispReg.word64 [r13 + 8], rbx
    x64.movRegImm32 rax, 16
    x64.movRegImm32 r9, 0
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r13
    x64.callDirect __cown_send
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, sendrefused#1
  tryok#4:
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg rbx, r8
    x64.callDirect __cown_staging
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 8], rbx
    x64.movRegImm32 rax, 16
    x64.movRegImm32 r9, 0
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __cown_send
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, sendrefused#6
  sendaccepted:
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#8
  tryerr#9:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok#8:
    x64.movRegReg rbx, r8
  trycont:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot3, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot2
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  sendrefused#1:
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.storeSlotReg slot4, r8
    x64.callDirect __mm_alloc
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot4
    x64.leaRegRegImm32 rcx, r8, -1
    x64.storeBaseDispReg.word64 [rax + 0], rcx
    x64.loadRegBaseDisp.word64 rcx, [r13 + 8]
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#5
    x64.jmp tryok#4
  sendrefused#6:
    x64.movRegReg rcx, rbx
    x64.callDirect __cown_reply_refused
    x64.jmp tryerr#9
  tryerr#5:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:30: a mailbox holds the one message\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
}

func @Counter.__behave {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rcx, [rcx + 40]
    x64.loadRegBaseDisp.word64 rax, [rdx + 0]
    x64.jumpTable rax, default=unknowntag [[0]=run, [1]=result] @__jumptable_10
  unknowntag:
    x64.callDirect __cown_unknown_tag
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  run:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.callDirect Counter.run
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  result:
    x64.loadRegBaseDisp.word64 rsi, [rdx + 8]
  __il_body:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
  __il_cont:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rsi
    x64.callDirect __gt_cell_complete
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @Counter.__finish {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rcx, [rcx + 40]
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @Counter.__start_Counter.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 r12, [rdx + 24]
  __il_body:
    x64.movRegImm32 r13, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.storeBaseDispReg.word64 [r14 + 0], r13
  __il_cont:
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.storeBaseDispReg.word64 [rbx + 40], r14
    x64.epilogue 32
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
  __mrt_program_started@552 = i8 0
}

func @Counter.run {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#5
  __rc_chk#6:
    arm64.movImm x0, 4294967295
    arm64.cmp x20, x0
    arm64.b.gt __rc_panic#5
  __rc_ok#4:
    arm64.movImm x4, 0
    arm64.b whilehdr
  eachRound:
    arm64.movImm x5, 0
    arm64.movImm x6, 4
    arm64.movImm x7, 0
    arm64.movImm x8, 0
  cls#20: [async-unsafe]
    arm64.leaRdata x0, __slab_size_to_class8
    arm64.loadRegBaseDisp.byte x0, [x0 + 12]
  cls#22: [async-unsafe]
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __ia_slow
  state: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.cmp x2, 0
    arm64.b.ne walk#31
  walk#24: [async-unsafe]
    arm64.arm64AddLsl x2, x1, x0, lsl #3
    arm64.add x16, x2, 139328
    arm64.loadRegBaseDisp.word64 x9, [x16 + 0]
    arm64.cmp x9, 0
    arm64.b.eq __ia_slow
  probe#34: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x9 + 16]
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  probe#35: [async-unsafe]
    arm64.add x16, x9, 48
    arm64.ldar.word64 x2, [x16]
    arm64.cmp x2, 0
    arm64.b.ne __ia_slow
  probe#33: [async-unsafe]
    arm64.add x1, x1, 152112
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 80
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.b pop#32
  walk#31: [async-unsafe]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  walk#25: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x3, [x2 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __ia_slow
  walk#28: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x9, [x3 + 32]
    arm64.cmp x9, 255
    arm64.b.ge __ia_slow
  walk#30: [async-unsafe]
    arm64.movImm x16, 544
    arm64.mul x10, x9, x16
    arm64.add x10, x1, x10
    arm64.arm64AddLsl x10, x10, x0, lsl #3
    arm64.loadRegBaseDisp.word64 x10, [x10 + 608]
    arm64.cmp x10, 0
    arm64.b.eq __ia_slow
  ownprobe#37: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x11, [x10 + 16]
    arm64.cmp x11, 0
    arm64.b.eq __ia_slow
  ownprobe#38: [async-unsafe]
    arm64.add x16, x10, 48
    arm64.ldar.word64 x11, [x16]
    arm64.cmp x11, x3
    arm64.b.ne __ia_slow
  ownprobe#36: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.eq ownthread#40
  ownthread#39: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 112]
    arm64.loadRegBaseDisp.word64 x3, [x2 + 512]
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x2 + 512], x3
    arm64.loadRegBaseDisp.word64 x3, [x2 + 528]
    arm64.add x3, x3, 96
    arm64.storeBaseDispReg.word64 [x2 + 528], x3
  ownthread#40: [async-unsafe]
    arm64.movImm x16, 48
    arm64.mul x2, x9, x16
    arm64.add x1, x1, 139872
    arm64.add x1, x1, x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 80
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.movRegReg x9, x10
  pop#32: [async-unsafe]
    arm64.leaRdata x1, __slab_class_geom
    arm64.arm64AddLsl x0, x1, x0, lsl #3
    arm64.loadRegBaseDisp.word32 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x10, [x9 + 8]
    arm64.cmp x10, 0
    arm64.b.ne pop#41
  pop#42: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x0, [x9 + 40]
    arm64.loadRegBaseDisp.word64 x2, [x9 + 32]
    arm64.leaRdata x3, __slab_class_geom
    arm64.arm64AddLsl x2, x3, x2, lsl #3
    arm64.loadRegBaseDisp.word32 x2, [x2 + 4]
    arm64.mul x2, x1, x2
    arm64.loadRegBaseDisp.word64 x3, [x9 + 0]
    arm64.add x2, x3, x2
    arm64.cmp x0, x2
    arm64.b.ge pop#44
  pop#43: [async-unsafe]
    arm64.add x1, x0, x1
    arm64.storeBaseDispReg.word64 [x9 + 40], x1
    arm64.b pop#45
  pop#41: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x0, [x10 + 0]
    arm64.storeBaseDispReg.word64 [x9 + 8], x0
    arm64.movRegReg x0, x10
    arm64.arm64MemFill 0x00
    arm64.movRegReg x0, x10
  pop#45: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x1, [x9 + 16]
    arm64.sub x1, x1, 1
    arm64.storeBaseDispReg.word64 [x9 + 16], x1
    arm64.cmp x1, 0
    arm64.b.ne pop#47
  pop#48: [async-unsafe]
    arm64.add x16, x9, 48
    arm64.ldar.word64 x1, [x16]
    arm64.loadRegBaseDisp.word64 x2, [x9 + 32]
    arm64.leaGlobal x3, __slab_state
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.cmp x1, 0
    arm64.b.ne pop#49
  pop#50: [async-unsafe]
    arm64.add x1, x3, 138720
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
    arm64.b pop#46
  pop#49: [async-unsafe]
    arm64.cmp x1, 0
    arm64.b.lt pop#52
  pop#51: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 32]
    arm64.movImm x16, 544
    arm64.mul x1, x1, x16
    arm64.add x1, x3, x1
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
  pop#46: [async-unsafe]
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.movImm x1, 18446744073709551613
    arm64.add x16, x9, 48
    arm64.stlr.word64 [x16], x1
  pop#47: [async-unsafe]
    arm64.storeBaseDispReg.word64 [x0 + 0], x8
    arm64.add x0, x0, 16
  __ia_done:
    arm64.storeBaseDispReg.word64 [x0 + 24], x6
    arm64.storeBaseDispReg.word64 [x0 + 40], x7
    arm64.movImm x1, 4
    arm64.add x2, x0, 64
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 32], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x5
  __im_cont#16:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 16]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word32 [x2 + x1*4 + 0], x4
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  __im_cont#53:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic#8
  __rc_chk#9:
    arm64.movImm x3, 4294967295
    arm64.cmp x2, x3
    arm64.b.gt __rc_panic#8
  __rc_ok#7:
    arm64.add x1, x1, x2
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#11
  __rc_chk#12:
    arm64.movImm x2, 4294967295
    arm64.cmp x1, x2
    arm64.b.gt __rc_panic#11
  __rc_ok#10:
    arm64.storeBaseDispReg.word64 [x19 + 0], x1
    arm64.add x21, x4, 1
  __rc_ok#13:
    arm64.bl __managed_decref
    arm64.movRegReg x4, x21
  whilehdr:
    arm64.cmp x4, x20
    arm64.b.lt eachRound
  whileexit:
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  __ia_slow:
    arm64.movImm x0, 80
    arm64.storeSlotReg slot0, x4
    arm64.movRegReg x1, x8
    arm64.bl __mm_alloc_slow
    arm64.loadRegSlot x4, slot0
    arm64.movImm x5, 0
    arm64.movImm x6, 4
    arm64.movImm x7, 0
    arm64.b __ia_done
  __im_slow:
    arm64.storeSlotReg slot1, x0
    arm64.storeSlotReg slot0, x4
    arm64.movRegReg x1, x4
    arm64.bl __managed_push
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot1
    arm64.loadRegSlot x4, slot0
    arm64.b __im_cont#53
  __rc_panic#5:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:12: Range check failed: value outside typealias 'Word"...
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  pop#44: [async-unsafe]
    arm64.movRegReg x0, x1
    arm64.bl __slab_past_end
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  pop#52: [async-unsafe]
    arm64.movRegReg x0, x1
    arm64.bl __slab_evict_unowned
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic#8:
    arm64.leaRdata x0, __str_blob_5  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:18: Range check failed: value outside typealias 'Word"...
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic#11:
    arm64.leaRdata x0, __str_blob_5  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:18: Range check failed: value outside typealias 'Word"...
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
    async-unsafe [00c8, 03ac)
    async-unsafe [04e0, 0520)
}

func @main {
  entry:
    arm64.prologue 96
    arm64.storeSlotReg slot8, x26
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movImm x19, 1024
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.leaFuncAddr x0, Counter.__start_Counter.create
    arm64.leaFuncAddr x4, Counter.__behave
    arm64.leaFuncAddr x5, Counter.__finish
    arm64.movImm x2, 16
    arm64.movRegReg x3, x19
    arm64.bl __cown_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __cown_handle_destruct
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 10
    arm64.bl __cown_staging
    arm64.movRegReg x21, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.storeBaseDispReg.word64 [x21 + 8], x19
    arm64.movImm x2, 16
    arm64.movImm x3, 0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.movRegReg x1, x21
    arm64.bl __cown_send
    arm64.cmp x0, 0
    arm64.b.ne sendrefused#1
  tryok#4:
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x19, x0
    arm64.bl __cown_staging
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 8], x19
    arm64.movImm x2, 16
    arm64.movImm x3, 0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __cown_send
    arm64.cmp x0, 0
    arm64.b.ne sendrefused#6
  sendaccepted:
    arm64.movRegReg x0, x19
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok#8
  tryerr#9:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok#8:
    arm64.movRegReg x19, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x19, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x26, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x19, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.epilogue 96
    arm64.retIndirect
  sendrefused#1:
    arm64.movImm x2, 16
    arm64.movImm x1, 0
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x2
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.storeBaseDispReg.word64 [x1 + 8], x0
    arm64.cmp x1, 0
    arm64.b.ne tryerr#5
    arm64.b tryok#4
  sendrefused#6:
    arm64.movRegReg x0, x19
    arm64.bl __cown_reply_refused
    arm64.b tryerr#9
  tryerr#5:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:30: a mailbox holds the one message\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.epilogue 96
    arm64.retIndirect
}

func @Counter.__behave {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.jumpTable x2, default=unknowntag [[0]=run, [1]=result] @__jumptable_10
  unknowntag:
    arm64.bl __cown_unknown_tag
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
  run:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.bl Counter.run
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
  result:
    arm64.loadRegBaseDisp.word64 x3, [x1 + 8]
  __il_body:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
  __il_cont:
    arm64.movImm x2, 0
    arm64.movRegReg x0, x3
    arm64.bl __gt_cell_complete
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
}

func @Counter.__finish {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
}

func @Counter.__start_Counter.create {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x20, [x1 + 24]
  __il_body:
    arm64.movImm x21, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.storeBaseDispReg.word64 [x22 + 0], x21
  __il_cont:
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x19 + 40], x22
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
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
  __mrt_program_started@624 = i8 0
}

func @Counter.run {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#5
  __rc_chk#6:
    arm64.movImm x0, 4294967295
    arm64.cmp x20, x0
    arm64.b.gt __rc_panic#5
  __rc_ok#4:
    arm64.movImm x4, 0
    arm64.b whilehdr
  eachRound:
    arm64.movImm x5, 0
    arm64.movImm x6, 4
    arm64.movImm x7, 0
    arm64.movImm x8, 0
  cls#20: [async-unsafe]
    arm64.leaRdata x0, __slab_size_to_class8
    arm64.loadRegBaseDisp.byte x0, [x0 + 12]
  cls#22: [async-unsafe]
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __ia_slow
  state: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.cmp x2, 0
    arm64.b.ne walk#31
  walk#24: [async-unsafe]
    arm64.arm64AddLsl x2, x1, x0, lsl #3
    arm64.add x16, x2, 139328
    arm64.loadRegBaseDisp.word64 x9, [x16 + 0]
    arm64.cmp x9, 0
    arm64.b.eq __ia_slow
  probe#34: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x9 + 16]
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  probe#35: [async-unsafe]
    arm64.add x16, x9, 48
    arm64.ldar.word64 x2, [x16]
    arm64.cmp x2, 0
    arm64.b.ne __ia_slow
  probe#33: [async-unsafe]
    arm64.add x1, x1, 152112
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 80
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.b pop#32
  walk#31: [async-unsafe]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  walk#25: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x3, [x2 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __ia_slow
  walk#28: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x9, [x3 + 32]
    arm64.cmp x9, 255
    arm64.b.ge __ia_slow
  walk#30: [async-unsafe]
    arm64.movImm x16, 544
    arm64.mul x10, x9, x16
    arm64.add x10, x1, x10
    arm64.arm64AddLsl x10, x10, x0, lsl #3
    arm64.loadRegBaseDisp.word64 x10, [x10 + 608]
    arm64.cmp x10, 0
    arm64.b.eq __ia_slow
  ownprobe#37: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x11, [x10 + 16]
    arm64.cmp x11, 0
    arm64.b.eq __ia_slow
  ownprobe#38: [async-unsafe]
    arm64.add x16, x10, 48
    arm64.ldar.word64 x11, [x16]
    arm64.cmp x11, x3
    arm64.b.ne __ia_slow
  ownprobe#36: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.eq ownthread#40
  ownthread#39: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 112]
    arm64.loadRegBaseDisp.word64 x3, [x2 + 512]
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x2 + 512], x3
    arm64.loadRegBaseDisp.word64 x3, [x2 + 528]
    arm64.add x3, x3, 96
    arm64.storeBaseDispReg.word64 [x2 + 528], x3
  ownthread#40: [async-unsafe]
    arm64.movImm x16, 48
    arm64.mul x2, x9, x16
    arm64.add x1, x1, 139872
    arm64.add x1, x1, x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 80
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.movRegReg x9, x10
  pop#32: [async-unsafe]
    arm64.leaRdata x1, __slab_class_geom
    arm64.arm64AddLsl x0, x1, x0, lsl #3
    arm64.loadRegBaseDisp.word32 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x10, [x9 + 8]
    arm64.cmp x10, 0
    arm64.b.ne pop#41
  pop#42: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x0, [x9 + 40]
    arm64.loadRegBaseDisp.word64 x2, [x9 + 32]
    arm64.leaRdata x3, __slab_class_geom
    arm64.arm64AddLsl x2, x3, x2, lsl #3
    arm64.loadRegBaseDisp.word32 x2, [x2 + 4]
    arm64.mul x2, x1, x2
    arm64.loadRegBaseDisp.word64 x3, [x9 + 0]
    arm64.add x2, x3, x2
    arm64.cmp x0, x2
    arm64.b.ge pop#44
  pop#43: [async-unsafe]
    arm64.add x1, x0, x1
    arm64.storeBaseDispReg.word64 [x9 + 40], x1
    arm64.b pop#45
  pop#41: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x0, [x10 + 0]
    arm64.storeBaseDispReg.word64 [x9 + 8], x0
    arm64.movRegReg x0, x10
    arm64.arm64MemFill 0x00
    arm64.movRegReg x0, x10
  pop#45: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x1, [x9 + 16]
    arm64.sub x1, x1, 1
    arm64.storeBaseDispReg.word64 [x9 + 16], x1
    arm64.cmp x1, 0
    arm64.b.ne pop#47
  pop#48: [async-unsafe]
    arm64.add x16, x9, 48
    arm64.ldar.word64 x1, [x16]
    arm64.loadRegBaseDisp.word64 x2, [x9 + 32]
    arm64.leaGlobal x3, __slab_state
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.cmp x1, 0
    arm64.b.ne pop#49
  pop#50: [async-unsafe]
    arm64.add x1, x3, 138720
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
    arm64.b pop#46
  pop#49: [async-unsafe]
    arm64.cmp x1, 0
    arm64.b.lt pop#52
  pop#51: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 32]
    arm64.movImm x16, 544
    arm64.mul x1, x1, x16
    arm64.add x1, x3, x1
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
  pop#46: [async-unsafe]
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.movImm x1, 18446744073709551613
    arm64.add x16, x9, 48
    arm64.stlr.word64 [x16], x1
  pop#47: [async-unsafe]
    arm64.storeBaseDispReg.word64 [x0 + 0], x8
    arm64.add x0, x0, 16
  __ia_done:
    arm64.storeBaseDispReg.word64 [x0 + 24], x6
    arm64.storeBaseDispReg.word64 [x0 + 40], x7
    arm64.movImm x1, 4
    arm64.add x2, x0, 64
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 32], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x5
  __im_cont#16:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 16]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word32 [x2 + x1*4 + 0], x4
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  __im_cont#53:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic#8
  __rc_chk#9:
    arm64.movImm x3, 4294967295
    arm64.cmp x2, x3
    arm64.b.gt __rc_panic#8
  __rc_ok#7:
    arm64.add x1, x1, x2
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#11
  __rc_chk#12:
    arm64.movImm x2, 4294967295
    arm64.cmp x1, x2
    arm64.b.gt __rc_panic#11
  __rc_ok#10:
    arm64.storeBaseDispReg.word64 [x19 + 0], x1
    arm64.add x21, x4, 1
  __rc_ok#13:
    arm64.bl __managed_decref
    arm64.movRegReg x4, x21
  whilehdr:
    arm64.cmp x4, x20
    arm64.b.lt eachRound
  whileexit:
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  __ia_slow:
    arm64.movImm x0, 80
    arm64.storeSlotReg slot0, x4
    arm64.movRegReg x1, x8
    arm64.bl __mm_alloc_slow
    arm64.loadRegSlot x4, slot0
    arm64.movImm x5, 0
    arm64.movImm x6, 4
    arm64.movImm x7, 0
    arm64.b __ia_done
  __im_slow:
    arm64.storeSlotReg slot1, x0
    arm64.storeSlotReg slot0, x4
    arm64.movRegReg x1, x4
    arm64.bl __managed_push
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot1
    arm64.loadRegSlot x4, slot0
    arm64.b __im_cont#53
  __rc_panic#5:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:12: Range check failed: value outside typealias 'Word"...
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  pop#44: [async-unsafe]
    arm64.movRegReg x0, x1
    arm64.bl __slab_past_end
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  pop#52: [async-unsafe]
    arm64.movRegReg x0, x1
    arm64.bl __slab_evict_unowned
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic#8:
    arm64.leaRdata x0, __str_blob_5  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:18: Range check failed: value outside typealias 'Word"...
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic#11:
    arm64.leaRdata x0, __str_blob_5  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:18: Range check failed: value outside typealias 'Word"...
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
    async-unsafe [00bc, 0398)
    async-unsafe [04cc, 050c)
}

func @main {
  entry:
    arm64.prologue 96
    arm64.storeSlotReg slot8, x26
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movImm x19, 1024
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.leaFuncAddr x0, Counter.__start_Counter.create
    arm64.leaFuncAddr x4, Counter.__behave
    arm64.leaFuncAddr x5, Counter.__finish
    arm64.movImm x2, 16
    arm64.movRegReg x3, x19
    arm64.bl __cown_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __cown_handle_destruct
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 10
    arm64.bl __cown_staging
    arm64.movRegReg x21, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.storeBaseDispReg.word64 [x21 + 8], x19
    arm64.movImm x2, 16
    arm64.movImm x3, 0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.movRegReg x1, x21
    arm64.bl __cown_send
    arm64.cmp x0, 0
    arm64.b.ne sendrefused#1
  tryok#4:
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x19, x0
    arm64.bl __cown_staging
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 8], x19
    arm64.movImm x2, 16
    arm64.movImm x3, 0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __cown_send
    arm64.cmp x0, 0
    arm64.b.ne sendrefused#6
  sendaccepted:
    arm64.movRegReg x0, x19
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok#8
  tryerr#9:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok#8:
    arm64.movRegReg x19, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x19, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x26, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x19, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.epilogue 96
    arm64.retIndirect
  sendrefused#1:
    arm64.movImm x2, 16
    arm64.movImm x1, 0
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x2
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.storeBaseDispReg.word64 [x1 + 8], x0
    arm64.cmp x1, 0
    arm64.b.ne tryerr#5
    arm64.b tryok#4
  sendrefused#6:
    arm64.movRegReg x0, x19
    arm64.bl __cown_reply_refused
    arm64.b tryerr#9
  tryerr#5:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-constant-capacity-create-in-a-green-thread-program-expands-its-pop-inside-an-unsafe-window.test:30: a mailbox holds the one message\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.epilogue 96
    arm64.retIndirect
}

func @Counter.__behave {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.jumpTable x2, default=unknowntag [[0]=run, [1]=result] @__jumptable_10
  unknowntag:
    arm64.bl __cown_unknown_tag
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
  run:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.bl Counter.run
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
  result:
    arm64.loadRegBaseDisp.word64 x3, [x1 + 8]
  __il_body:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
  __il_cont:
    arm64.movImm x2, 0
    arm64.movRegReg x0, x3
    arm64.bl __gt_cell_complete
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
}

func @Counter.__finish {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
}

func @Counter.__start_Counter.create {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x20, [x1 + 24]
  __il_body:
    arm64.movImm x21, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.storeBaseDispReg.word64 [x22 + 0], x21
  __il_cont:
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x19 + 40], x22
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```
