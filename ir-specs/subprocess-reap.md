---
feature: subprocess-reap
status: experimental
keywords: [subprocess, process, reap, wait, waitpid, waitid, ECHILD, child, tracer, hand-assembled, chunk]
category: codegen
---

# Subprocess — the reap of a child: the hand-assembled chunks that pin a failed wait

A POSIX wait that fails for a child this process spawned means the child is no longer its own: another
process took its status. The reap answers that apart from "still running", and the wait that parks on it
answers `WaitAbandoned` rather than a timeout. No program written in Maxon can make the kernel fail such a wait,
so the two chunks that decide it are pinned as the bytes the assembler wrote, one dump per native POSIX lane.

## Tests

<!-- test: subprocess-reap.a-failed-wait-answers-reaped-elsewhere -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
`mrt_host_proc_reap` answers `PosixChildReapedElsewhere` (2) where its wait or its by-pid peek fails without
being interrupted: on arm64-macos at the `waitpid` itself, on the two Linux lanes at the `waitid` peek, because
their reap peeks before it consumes. `mrt_host_proc_await` maps that answer to `WaitAbandoned` (0x80), distinct
from `WaitTimeout`, so `__gt_subp_poll_exit`, `__gt_subp_wait_exit` and `__gt_subp_wait_collect` end instead of
polling a child that can no longer be waited on.

⚠ **THE PROGRAM EXISTS ONLY TO REACH THE TWO CHUNKS.** It spawns `true` and waits for it; a child that is its own
never reaches the new road, so the exit code says nothing about it. The pin is the gate.

```maxon
function main() returns ExitCode
	var child = try StreamingSubprocess.spawn(Executable.name("true"), arguments: StringArray.create(0)) otherwise return 1
	let code = try child.wait() otherwise return 2

	child.release()

	return code as ExitCode
end 'main'
```
```exitcode
0
```
```RequiredRuntime
mrt_host_proc_reap
mrt_host_proc_await
```

```TargetIr:x64-linux
data {
  __data_CharacterSet.whitespacesAndNewlines@0 = i64 0
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
    x64.prologue 184
    x64.leaRegRdata rcx, [rip + __str_rec_65]  ; "true"
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.movRegReg rbx, r8
    x64.orRegImm32 rbx, r8, 1
    x64.storeSlotReg slot1, rbx
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __str_decref]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot2, r12
  __il_body#44:
    x64.leaRegRdata rcx, [rip + __str_rec_104]  ; ""
  __il_body#196:
    x64.callDirect FilePath.create
    x64.cmpRegImm32 r10, 0
    x64.setccReg notEqual, rax
    x64.jcc equal, tryok#197
  tryerr#198:
    x64.movRegImm32 r13, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#46
    x64.jmp __il_cont#195
  tryok#197:
    x64.movRegReg r13, r8
  __il_cont#195:
    x64.storeSlotReg slot0, r13
  __il_body#98:
    x64.movRegReg rax, rbx
    x64.andRegImm32 rax, rbx, 7
    x64.cmpRegImm32 rax, 1
    x64.setccReg equal, rax
    x64.storeSlotReg slot3, rax
    x64.jcc notEqual, matcharm#103
  matcharm#99:
    x64.leaRegRegImm32 rbx, rbx, -1
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg r14, r8
    x64.leaRegRdata rdx, [rip + __str_rec_177]  ; "the program name"
    x64.movRegReg rcx, rbx
    x64.callDirect requireNulFree
    x64.movRegReg rbx, r10
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, tryok#100
  tryerr#101:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.movRegReg rax, rbx
    x64.jmp __il_cont#97
  tryok#100:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.jmp matchcont#108
  matcharm#103:
    x64.leaRegRegImm32 rbx, rbx, -2
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r14, r8
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.leaRegRdata rdx, [rip + __str_rec_178]  ; "the program path"
    x64.callDirect requireNulFree
    x64.movRegReg rbx, r10
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, tryok#104
  tryerr#105:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_FilePath
    x64.movRegReg rax, rbx
    x64.jmp __il_cont#97
  tryok#104:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_FilePath
  matchcont#108:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.leaRegRdata rdx, [rip + __str_rec_179]  ; "the working directory"
    x64.callDirect requireNulFree
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#110
  tryok#109:
    x64.leaRegRdata rdx, [rip + __layout_Array_String]
    x64.movRegReg rcx, r12
    x64.callDirect Array.withIterator
    x64.movRegReg rbx, r8
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forhdr#111
    x64.jmp matcharm#123
  __il_body#201:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.storeSlotReg slot4, r12
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __adapt___witness_ArrayIterator_String.Iterator.current
    x64.storeSlotReg slot8, r8
  __il_cont#200:
    x64.movRegReg rcx, r8
    x64.callDirect __str_retain
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_180]  ; "argument "
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.storeSlotReg slot6, r8
  __il_body#202:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic#204
  __il_cont#199:
    x64.leaRegRegImm32 r12, rax, 1
    x64.cmpRegReg r12, rax
    x64.jcc below, __rc_panic#135
  __rc_ok#134:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot17, r8
    x64.leaRegRegReg rax, r14, r8
    x64.storeSlotReg slot10, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot13, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rcx, r12, r14
    x64.loadRegSlot rax, slot17
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot r13, slot13
    x64.storeBaseDispReg.word64 [r13 + 0], r12
    x64.loadRegSlot rax, slot10
    x64.storeBaseDispReg.word64 [r13 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot8
    x64.movRegReg rdx, r13
    x64.callDirect requireNulFree
    x64.movRegReg r12, r10
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, tryok#116
  tryerr#117:
    x64.loadRegSlot rcx, slot13
    x64.callDirect __str_decref
  exitdrop#118:
    x64.loadRegSlot rcx, slot6
    x64.callDirect __destruct_ArrayIterator_String
  exitdrop#119:
    x64.loadRegSlot rcx, slot5
    x64.callDirect __str_decref
  exitdrop#120:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __destruct_ArrayIterator_String
    x64.loadRegSlot rcx, slot8
    x64.callDirect __str_decref
  exitdrop#121:
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_String_String
    x64.movRegReg rax, r12
    x64.jmp __il_cont#97
  tryok#116:
    x64.loadRegSlot rcx, slot13
    x64.callDirect __str_decref
    x64.loadRegSlot rcx, slot6
    x64.callDirect __destruct_ArrayIterator_String
    x64.loadRegSlot rcx, slot5
    x64.callDirect __str_decref
    x64.loadRegSlot rcx, slot4
    x64.callDirect __destruct_ArrayIterator_String
    x64.loadRegSlot rcx, slot8
    x64.callDirect __str_decref
  __il_body#206:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
  __il_body#224:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#228
    x64.jmp __im_move
  tryerr#226:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#223
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#225:
    x64.movRegImm32 rax, 0
  __il_cont#223:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, forhdr#111
  tryok#207:
    x64.movRegImm32 rax, 0
  forhdr#111:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#201
  forexit#115:
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_String_String
  matcharm#123:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#97
  tryerr#110:
    x64.movRegReg rax, r10
  __il_cont#97:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#150
  tryerr#87:
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#84
  __il_body#150:
    x64.loadRegSlot rax, slot3
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, matcharm#155
  matcharm#151:
    x64.loadRegSlot rax, slot1
    x64.leaRegRegImm32 rbx, rax, -1
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.storeSlotReg slot16, r8
  __il_body#210:
    x64.movRegReg rcx, rbx
    x64.callDirect FilePath.create
    x64.cmpRegImm32 r10, 0
    x64.setccReg notEqual, rax
    x64.jcc equal, tryok#211
  tryerr#212:
    x64.movRegImm32 r12, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#231
  tryerr#162:
    x64.movRegImm32 rcx, 40
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r12 + 8], r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 32], rax
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#159
  tryok#211:
    x64.movRegReg r12, r8
  __il_body#231:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#238:
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
  __il_cont#237:
    x64.movRegImm32 r14, 0
  whilehdr#232:
    x64.cmpRegReg r14, r13
    x64.movRegImm32 rax, 0
    x64.jcc aboveEqual, scmerge#234
  andrhs#233:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
  __il_body#240:
    x64.movRegReg rdx, r14
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#242
  __il_cont#239:
    x64.cmpRegImm32 r8, 47
    x64.setccReg equal, rax
  scmerge#234:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_cont#230
  leadingSeparators:
    x64.leaRegRegImm32 r14, r14, 1
    x64.jmp whilehdr#232
  __il_cont#230:
    x64.cmpRegImm32 r14, 0
    x64.movRegImm32 r13, 1
    x64.jcc above, critsplit#251
  __il_body#185:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#216:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __il_cont#215:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont#187
  empty:
    x64.movRegImm32 r13, 0
    x64.jmp __il_cont#184
  ifcont#187:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#218:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.jmp whilehdr#188
  scan:
    x64.leaRegRegImm32 r13, rax, -1
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
  __il_body#220:
    x64.movRegReg rdx, r13
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#222
  __il_cont#219:
    x64.cmpRegImm32 r8, 47
    x64.jcc notEqual, ifcont#192
  isSep:
    x64.leaRegRegImm32 r14, r13, 1
    x64.cmpRegReg r14, r13
    x64.jcc below, __rc_panic#194
    x64.jmp __rc_ok#193
  ifcont#192:
    x64.movRegReg rax, r13
  whilehdr#188:
    x64.cmpRegImm32 rax, 0
    x64.jcc above, scan
  whileexit:
    x64.movRegImm32 r13, 0
    x64.jmp __il_cont#184
  __rc_ok#193:
    x64.movRegReg r13, r14
  __il_cont#184:
    x64.cmpRegImm32 r13, 0
    x64.jcc notEqual, ifcont#180
  noSep:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __str_retain
    x64.movRegReg r13, r8
    x64.jmp __il_cont#177
  ifcont#180:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#214:
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
  __il_cont#213:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect String.addressableBytes
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __managed_slice
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#181
  tryerr#182:
    x64.leaRegRdata rcx, [rip + __str_blob_102]  ; "panic at FilePath.maxon:354: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryok#181:
    x64.movRegReg rcx, r15
    x64.callDirect __managed_decref
    x64.movRegReg rcx, r13
    x64.callDirect __managed_retain
    x64.movRegReg rcx, r8
    x64.callDirect String.init
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitterminal#183:
    x64.movRegReg r13, r14
  __il_cont#177:
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __str_eq
    x64.movRegReg r14, r8
    x64.xorRegImm32 r14, r8, 1
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.jmp scmerge#164
  critsplit#251:
    x64.movRegReg r14, r13
  scmerge#164:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, ternaryfalse#166
  ternarytrue#165:
    x64.loadRegSlot rdx, slot0
    x64.movRegReg rcx, r12
    x64.callDirect launchPathInWorkingDirectory
    x64.movRegReg r13, r8
    x64.jmp matchcont#167
  ternaryfalse#166:
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg r13, r8
  matchcont#167:
    x64.leaRegRdata r15, [rip + __str_rec_95]  ; "/"
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, ifcont#170
  isAPath:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_FilePath
    x64.movRegReg rax, r13
    x64.jmp exitterminal#169
  ifcont#170:
    x64.movRegReg rcx, r13
    x64.callDirect String.cstr
    x64.movRegReg rcx, r8
    x64.callDirect __gt_subp_resolve_path
    x64.movRegReg rcx, r8
    x64.callDirect String.init
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r15
    x64.callDirect String.contains
    x64.xorRegImm32 r8, r8, 1
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#175
  notOnPath:
    x64.movRegImm32 rcx, 40
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 0], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r15 + 8], r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 16], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 24], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 32], rax
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
  exitdrop#173:
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
  exitdrop#174:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_FilePath
  exitterminal#172:
    x64.movRegImm32 rbx, 0
    x64.movRegReg r12, r15
    x64.jmp __il_cont#159
  ifcont#175:
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
  exitdrop#176:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_FilePath
    x64.movRegReg rax, r14
  exitterminal#169:
    x64.movRegImm32 rbx, 0
    x64.movRegReg r12, rbx
    x64.movRegReg rbx, rax
  __il_cont#159:
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, tryok#152
  tryerr#153:
    x64.loadRegSlot rcx, slot16
    x64.callDirect __str_decref
  exitterminal#154:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rax, r12
    x64.jmp __il_cont#149
  tryok#152:
    x64.loadRegSlot rcx, slot16
    x64.callDirect __str_decref
    x64.movRegReg rax, rbx
    x64.jmp matchcont#158
  matcharm#155:
    x64.loadRegSlot rax, slot1
    x64.leaRegRegImm32 rbx, rax, -2
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.loadRegSlot rdx, slot0
    x64.movRegReg rcx, rbx
    x64.callDirect launchPathInWorkingDirectory
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_FilePath
    x64.movRegReg rax, rbx
  matchcont#158:
    x64.movRegImm32 rcx, 0
    x64.movRegReg rbx, rax
    x64.movRegReg rax, rcx
  __il_cont#149:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#137
  tryerr#89:
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#84
  __il_body#137:
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect appendNulTerminated
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 r14, 0
    x64.jmp forhdr#138
  arg:
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegReg r14, rax
    x64.jcc aboveEqual, __im_slow#146
  __im_load:
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseIndexScale.word64 rdx, [rcx + r14*8 + 0]
    x64.movRegImm32 rcx, 0
    x64.loadRegBaseDisp.word64 rax, [rax + 40]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rsi
    x64.leaRegFunc rdi, [rip + __gt_promise_drop]
    x64.cmpRegReg rax, rdi
    x64.setccReg notEqual, rax
    x64.andRegReg rsi, rsi, rax
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#246
  __im_empty:
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_slow#146
  critsplit#247:
    x64.movRegReg r10, rcx
    x64.jmp __im_cont
  critsplit#246:
    x64.movRegReg r10, rcx
  __im_cont:
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#143
  tryok#142:
    x64.movRegReg rcx, r12
    x64.callDirect appendNulTerminated
  forstep:
    x64.leaRegRegImm32 r14, r14, 1
  forhdr#138:
    x64.cmpRegReg r14, r13
    x64.jcc below, arg
  forexit#141:
    x64.movRegReg rcx, r12
    x64.callDirect String.from
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  __il_cont#136:
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.leaRegRegImm32 r12, rax, 1
    x64.cmpRegReg r12, rax
    x64.jcc below, __rc_panic#96
  __rc_ok#95:
    x64.movRegImm32 rcx, 1
    x64.callDirect spawnEnvironmentFor
    x64.movRegReg r14, r8
    x64.movRegReg r15, r10
    x64.cmpRegImm32 r15, 0
    x64.jcc equal, tryok#90
  tryerr#91:
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
  exitdrop#93:
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
  exitterminal#92:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rax, r15
    x64.jmp __il_cont#84
  tryok#90:
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r15 + 0], r8
    x64.movRegReg rcx, r13
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r15 + 8], r8
    x64.storeBaseDispReg.word64 [r15 + 16], r12
    x64.storeBaseDispReg.word64 [r15 + 24], r14
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
  exitterminal#94:
    x64.movRegImm32 rax, 0
    x64.movRegReg rbx, r15
  __il_cont#84:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, matchcont#65
  tryerr#62:
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#59
  matchcont#65:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.callDirect String.addressableBytes
    x64.movRegReg r12, r8
    x64.loadRegBaseDisp.word64 r13, [rbx + 16]
  __il_body#73:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect __str_retain
    x64.storeSlotReg slot12, r8
  __il_cont#72:
    x64.movRegReg rcx, r8
    x64.callDirect String.cstr
    x64.movRegReg r14, r8
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.loadRegBaseDisp.word64 r15, [rax + 0]
    x64.movRegReg rcx, r15
    x64.callDirect __str_retain
    x64.storeSlotReg slot15, r8
    x64.movRegReg rcx, r15
    x64.callDirect String.addressableBytes
    x64.movRegReg r15, r8
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.loadRegBaseDisp.word64 rdi, [rax + 8]
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.movRegImm32 r9, 0
    x64.movRegReg rsi, r15
    x64.callDirect __gt_subp_stream_spawn
    x64.movRegReg r13, r8
    x64.loadRegSlot rcx, slot15
    x64.callDirect __str_decref
    x64.movRegReg rcx, r15
    x64.callDirect __managed_decref
    x64.loadRegSlot rcx, slot12
    x64.callDirect __str_decref
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.cmpRegImm32 r13, -1
    x64.jcc notEqual, ifcont#68
  spawnFailed:
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
  __il_body#75:
    x64.callDirect __gt_subp_last_error_code
    x64.movRegReg rcx, r8
    x64.sarRegImm8 rcx, r8, 32
    x64.callDirect __enum_from_raw_SpawnPreparation
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#77
  tryok#76:
    x64.callDirect lastOsErrorCode
    x64.cmpRegImm32 r8, 2
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#79
  orrhs:
    x64.cmpRegImm32 r8, 3
    x64.setccReg equal, rax
  scmerge#79:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#244
  andrhs#80:
    x64.cmpRegImm32 r13, 0
    x64.setccReg equal, rax
    x64.jmp scmerge#81
  critsplit#244:
    x64.movRegReg rax, rcx
  scmerge#81:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#83
  executableAbsent:
    x64.movRegImm32 rcx, 40
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 0], rax
    x64.movRegReg rcx, r12
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r13 + 8], r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 32], rax
    x64.jmp __il_cont#74
  ifcont#83:
    x64.callDirect lastErrorMessage
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 40
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 0], rax
    x64.movRegReg rcx, r12
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r13 + 8], r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 32], rax
  __il_cont#74:
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_SpawnOperands
  exitterminal#67:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rax, r13
    x64.jmp __il_cont#59
  ifcont#68:
    x64.cmpRegImm32 r13, 0
    x64.jcc less, __rc_panic#71
  __rc_ok#70:
    x64.movRegImm32 r12, 0
    x64.movRegImm32 r14, 0
    x64.movRegImm32 rcx, 24
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeBaseDispReg.word64 [r15 + 0], r13
    x64.storeBaseDispReg.byte [r15 + 8], r12
    x64.storeBaseDispReg.byte [r15 + 16], r14
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_SpawnOperands
  exitterminal#69:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r15
  __il_cont#59:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#57
  tryerr#58:
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#55
  tryok#57:
    x64.movRegImm32 rax, 0
  __il_cont#55:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#53
  tryerr#54:
    x64.movRegImm32 rbx, 0
    x64.movRegReg r12, rax
    x64.jmp __il_cont#51
  tryok#53:
    x64.movRegImm32 rbx, 0
    x64.movRegReg r12, rbx
    x64.movRegReg rbx, rcx
  __il_cont#51:
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, tryok#47
  tryerr#48:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __destruct_FilePath
  exitterminal#49:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r12
    x64.jmp __il_cont#43
  tryok#47:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __destruct_FilePath
  exitterminal#50:
    x64.movRegImm32 rcx, 0
  __il_cont#43:
    x64.storeSlotReg slot7, rbx
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.movRegImm32 rbx, 1
    x64.callDirect __destruct_SubprocessError
  exitdrop#4:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitdrop#5:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __destruct_Executable
    x64.movRegReg r8, rbx
    x64.jmp exitterminal#3
  tryok#1:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
    x64.loadRegSlot rcx, slot1
    x64.callDirect __destruct_Executable
  __il_body#20:
    x64.movRegImm32 r12, 0
  __il_body#40:
    x64.loadRegBaseDisp.byte rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#42
  gone:
    x64.leaRegRdata r13, [rip + __str_rec_196]  ; "used after release()"
    x64.movRegImm32 rcx, 40
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegImm32 rax, 2
    x64.storeBaseDispReg.word64 [r14 + 0], rax
    x64.movRegReg rcx, r13
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r14 + 8], r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r14 + 16], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r14 + 24], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r14 + 32], rax
    x64.movRegReg rax, r14
    x64.jmp __il_cont#39
  ifcont#42:
    x64.movRegImm32 rax, 0
  __il_cont#39:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#25
  tryerr#26:
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#23
  tryok#25:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.movRegReg rdx, r12
    x64.callDirect __gt_subp_wait_exit
    x64.cmpRegImm32 r8, -2
    x64.jcc notEqual, ifcont#28
  timedOut:
    x64.leaRegRdata rbx, [rip + __str_rec_104]  ; ""
    x64.leaRegRdata r13, [rip + __str_rec_104]  ; ""
    x64.movRegImm32 rcx, 40
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r14 + 0], rax
    x64.storeBaseDispReg.word64 [r14 + 8], r12
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r14 + 16], r8
    x64.movRegReg rcx, r13
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r14 + 24], r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r14 + 32], rax
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rax, r14
    x64.jmp __il_cont#23
  ifcont#28:
    x64.cmpRegImm32 r8, -4
    x64.jcc notEqual, ifcont#30
  statusLost:
    x64.leaRegRdata rbx, [rip + __str_rec_104]  ; ""
    x64.leaRegRdata r12, [rip + __str_rec_104]  ; ""
    x64.movRegImm32 rcx, 40
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r13 + 0], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r13 + 8], r8
    x64.movRegReg rcx, r12
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r13 + 16], r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 32], rax
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rax, r13
    x64.jmp __il_cont#23
  ifcont#30:
    x64.cmpRegImm32 r8, 0
    x64.jcc greaterEqual, __rc_chk#38
  failed:
    x64.cmpRegImm32 r8, -1
    x64.jcc notEqual, ternaryfalse#33
  ternarytrue#32:
    x64.leaRegRdata rax, [rip + __str_rec_195]  ; "wait failed: "
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.callDirect lastErrorMessage
    x64.storeSlotReg slot9, r8
    x64.loadRegBaseDisp.word64 r13, [r8 + 0]
    x64.loadRegBaseDisp.word64 r14, [r8 + 8]
    x64.leaRegRegReg rax, r12, r14
    x64.storeSlotReg slot11, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot14, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rcx, r15, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rbx, slot14
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot11
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 rcx, 40
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 2
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.storeBaseDispReg.word64 [r12 + 8], rbx
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 32], rax
    x64.loadRegSlot rcx, slot9
    x64.callDirect __str_decref
    x64.movRegReg rax, r12
    x64.jmp matchcont#34
  ternaryfalse#33:
    x64.leaRegRdata rbx, [rip + __str_rec_104]  ; ""
    x64.leaRegRdata r13, [rip + __str_rec_104]  ; ""
    x64.callDirect lastOsErrorCode
    x64.movRegReg r14, r8
    x64.movRegImm32 rcx, 40
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [r15 + 0], rax
    x64.storeBaseDispReg.word64 [r15 + 8], r12
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r15 + 16], r8
    x64.movRegReg rcx, r13
    x64.callDirect __str_retain
    x64.movRegReg rcx, r8
    x64.callDirect __str_unshare
    x64.storeBaseDispReg.word64 [r15 + 24], r8
    x64.storeBaseDispReg.word64 [r15 + 32], r14
    x64.movRegReg rax, r15
  matchcont#34:
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#23
  __rc_chk#38:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic#37
  __rc_ok#36:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r8
  __il_cont#23:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#21
  tryerr#22:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, rax
    x64.jmp __il_cont#19
  tryok#21:
    x64.movRegImm32 rax, 0
    x64.movRegReg rbx, rcx
    x64.movRegReg rcx, rax
  __il_cont#19:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __il_body#14
  tryerr#7:
    x64.movRegImm32 rbx, 2
    x64.callDirect __destruct_SubprocessError
  exitdrop#8:
    x64.loadRegSlot rcx, slot7
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
  exitterminal#3:
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __il_body#14:
    x64.loadRegSlot rax, slot7
    x64.loadRegBaseDisp.byte rax, [rax + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#16
  readerStillOut:
    x64.leaRegRdata rcx, [rip + __str_blob_197]  ; "panic at Subprocess.maxon:1368: StreamingSubprocess.release: the stdout reader is still out; reclaimStdout first\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  ifcont#16:
    x64.loadRegSlot rax, slot7
    x64.loadRegBaseDisp.byte rax, [rax + 8]
    x64.xorRegImm32 rax, rax, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_cont#13
  notYet:
    x64.loadRegSlot r12, slot7
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __gt_subp_release
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.byte [r12 + 8], rax
  __il_cont#13:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic#11
  __rc_chk#12:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic#11
  __rc_ok#10:
    x64.loadRegSlot rcx, slot7
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __im_slow#228:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#226
    x64.jmp tryok#225
  __im_slow#146:
    x64.loadRegSlot rcx, slot2
    x64.movRegReg rdx, r14
    x64.callDirect __managed_get
    x64.movRegReg rdx, r8
    x64.jmp __im_cont
  tryerr#46:
    x64.leaRegRdata rcx, [rip + __str_blob_194]  ; "panic at Subprocess.maxon:1041: StreamingSubprocess.spawn: empty path is the sentinel for 'inherit cwd'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic#204:
    x64.leaRegRdata rcx, [rip + __str_blob_247]  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic#135:
    x64.leaRegRdata rcx, [rip + __str_blob_324]  ; "panic at Subprocess.maxon:720: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryerr#242:
    x64.leaRegRdata rcx, [rip + __str_blob_164]  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryerr#222:
    x64.leaRegRdata rcx, [rip + __str_blob_164]  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic#194:
    x64.leaRegRdata rcx, [rip + __str_blob_256]  ; "panic at FilePath.maxon:337: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryerr#143:
    x64.leaRegRdata rcx, [rip + __str_blob_175]  ; "panic at Subprocess.maxon:691: buildArgvBlob: args.get(i) out of bounds\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic#96:
    x64.leaRegRdata rcx, [rip + __str_blob_323]  ; "panic at Subprocess.maxon:215: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  tryerr#77:
    x64.leaRegRdata rcx, [rip + __str_blob_193]  ; "panic at Subprocess.maxon:956: Subprocess.spawnFailure: the runtime recorded a spawn step this library does not know\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic#71:
    x64.leaRegRdata rcx, [rip + __str_blob_326]  ; "panic at Subprocess.maxon:1080: Range check failed: value outside typealias 'StreamingHandle'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic#37:
    x64.leaRegRdata rcx, [rip + __str_blob_327]  ; "panic at Subprocess.maxon:1210: Range check failed: value outside typealias 'ExitInt'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
  __rc_panic#11:
    x64.leaRegRdata rcx, [rip + __str_blob_244]  ; "panic at subprocess-reap.a-failed-wait-answers-reaped-elsewhere.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.retIndirect
}

chunk @mrt_host_proc_reap {
    0000: 55 48 89 e5 48 83 ec 60 48 89 4d f8 48 89 55 f0
    0010: b8 01 00 00 00 48 89 45 e0 48 8b 41 08 48 85 c0
    0020: 0f 85 75 01 00 00 b8 00 00 00 00 48 89 45 d0 48
    0030: 8b 4d f8 48 8b 71 18 48 8d 55 c0 41 b8 00 00 00
    0040: 00 48 8b 45 e0 48 85 c0 0f 84 0a 00 00 00 bf 02
    0050: 00 00 00 e9 05 00 00 00 bf 01 00 00 00 48 8b 45
    0060: f0 48 85 c0 0f 85 0b 00 00 00 41 ba 05 00 00 41
    0070: e9 06 00 00 00 41 ba 04 00 00 41 b8 f7 00 00 00
    0080: 0f 05 48 81 f8 fc ff ff ff 0f 84 ec ff ff ff 48
    0090: 81 f8 00 00 00 00 0f 8c 68 00 00 00 8b 7d d0 85
    00a0: ff 0f 84 fe 00 00 00 8b 45 c8 48 81 e8 01 00 00
    00b0: 00 48 81 f8 02 00 00 00 0f 87 66 00 00 00 48 8d
    00c0: 75 e8 ba 01 00 00 40 41 ba 00 00 00 00 b8 3d 00
    00d0: 00 00 0f 05 48 81 f8 fc ff ff ff 0f 84 ec ff ff
    00e0: ff 48 b9 ff ff ff ff 00 00 00 00 48 21 c8 48 8b
    00f0: 4d f8 48 8b 51 18 48 39 d0 0f 84 41 00 00 00 e9
    0100: 22 ff ff ff 48 8b 45 e0 48 85 c0 0f 85 05 00 00
    0110: 00 e9 99 00 00 00 b8 00 00 00 00 48 89 45 e0 e9
    0120: 02 ff ff ff 48 8b 45 f0 48 85 c0 0f 84 74 00 00
    0130: 00 b9 01 00 00 00 e8 00 00 00 00 e9 e6 fe ff ff
    0140: 8b 55 e8 48 89 d0 48 81 e0 7f 00 00 00 48 81 f8
    0150: 09 00 00 00 0f 85 0d 00 00 00 48 8b 79 28 48 85
    0160: ff 0f 85 2b 00 00 00 48 85 c0 0f 85 13 00 00 00
    0170: 48 89 d0 48 c1 e8 08 48 81 e0 ff 00 00 00 e9 0b
    0180: 00 00 00 48 89 41 30 48 81 c0 80 00 00 00 48 89
    0190: 41 20 b8 01 00 00 00 48 89 41 08 b8 01 00 00 00
    01a0: e9 0f 00 00 00 b8 00 00 00 00 e9 05 00 00 00 b8
    01b0: 02 00 00 00 48 83 c4 60 5d c3
}

chunk @mrt_host_proc_await {
    0000: 55 48 89 e5 48 83 ec 40 48 89 4d f8 48 89 55 f0
    0010: 48 b8 ff ff ff ff 00 00 00 00 48 39 c2 0f 85 0f
    0020: 00 00 00 ba 01 00 00 00 e8 00 00 00 00 e9 55 00
    0030: 00 00 e8 00 00 00 00 48 8b 4d f0 48 01 c8 48 89
    0040: 45 e8 48 8b 4d f8 ba 00 00 00 00 e8 00 00 00 00
    0050: 48 81 f8 02 00 00 00 0f 84 3e 00 00 00 48 85 c0
    0060: 0f 85 21 00 00 00 e8 00 00 00 00 48 8b 4d e8 48
    0070: 39 c8 0f 8d 19 00 00 00 b9 01 00 00 00 e8 00 00
    0080: 00 00 e9 bb ff ff ff b8 00 00 00 00 e9 0f 00 00
    0090: 00 b8 02 01 00 00 e9 05 00 00 00 b8 80 00 00 00
    00a0: 48 83 c4 40 5d c3
}
```

```TargetIr:arm64-linux
data {
  __data_CharacterSet.whitespacesAndNewlines@0 = i64 0
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
    arm64.leaRdata x0, __str_rec_65  ; "true"
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.orr x19, x0, 1
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot0, x20
  __il_body#44:
    arm64.leaRdata x0, __str_rec_104  ; ""
  __il_body#323:
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#324
  tryerr#325:
    arm64.movImm x21, 0
    arm64.cbnz x1, tryerr#46
    arm64.b __il_cont#322
  tryok#324:
    arm64.movRegReg x21, x0
  __il_cont#322:
    arm64.storeSlotReg slot1, x21
  __il_body#98:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x0, eq
    arm64.storeSlotReg slot3, x0
    arm64.b.ne matcharm#103
  matcharm#99:
    arm64.sub x19, x19, 1
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
    arm64.leaRdata x1, __str_rec_177  ; "the program name"
    arm64.movRegReg x0, x19
    arm64.bl requireNulFree
    arm64.movRegReg x19, x9
    arm64.cmp x19, 0
    arm64.b.eq tryok#100
  tryerr#101:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.b __il_cont#97
  tryok#100:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.b matchcont#108
  matcharm#103:
    arm64.sub x19, x19, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_178  ; "the program path"
    arm64.bl requireNulFree
    arm64.movRegReg x19, x9
    arm64.cmp x19, 0
    arm64.b.eq tryok#104
  tryerr#105:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x19
    arm64.b __il_cont#97
  tryok#104:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_FilePath
  matchcont#108:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.leaRdata x1, __str_rec_179  ; "the working directory"
    arm64.bl requireNulFree
    arm64.cmp x9, 0
    arm64.b.ne tryerr#110
  tryok#109:
    arm64.leaRdata x1, __layout_Array_String
    arm64.movRegReg x0, x20
    arm64.bl Array.withIterator
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr#111
    arm64.b matcharm#123
  __il_body#328:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot4, x20
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __adapt___witness_ArrayIterator_String.Iterator.current
    arm64.movRegReg x21, x0
  __il_cont#327:
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.storeSlotReg slot5, x0
    arm64.leaRdata x0, __str_rec_180  ; "argument "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x24, x0
  __il_body#329:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#331
  __il_cont#326:
    arm64.add x20, x0, 1
    arm64.cmp x20, x0
    arm64.b.lo __rc_panic#135
  __rc_ok#134:
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
    arm64.b.eq tryok#116
  tryerr#117:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#118:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
  exitdrop#119:
    arm64.loadRegSlot x0, slot5
    arm64.bl __str_decref
  exitdrop#120:
    arm64.loadRegSlot x0, slot4
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#121:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#97
  tryok#116:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
    arm64.loadRegSlot x0, slot5
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot4
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  __il_body#333:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#351:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#355
    arm64.b __im_move
  tryerr#353:
    arm64.movImm x0, 1
    arm64.b __il_cont#350
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#352:
    arm64.movImm x0, 0
  __il_cont#350:
    arm64.cmp x0, 0
    arm64.b.ne forhdr#111
  tryok#334:
    arm64.movImm x0, 0
  forhdr#111:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#328
  forexit#115:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
  matcharm#123:
    arm64.movImm x0, 0
    arm64.b __il_cont#97
  tryerr#110:
    arm64.movRegReg x0, x9
  __il_cont#97:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#150
  tryerr#87:
    arm64.movImm x19, 0
    arm64.b __il_cont#84
  __il_body#150:
    arm64.loadRegSlot x0, slot3
    arm64.cbz x0, matcharm#155
  matcharm#151:
    arm64.loadRegSlot x0, slot2
    arm64.sub x19, x0, 1
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x20, x0
  __il_body#337:
    arm64.movRegReg x0, x19
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#338
  tryerr#339:
    arm64.movImm x21, 0
    arm64.cbz x1, __il_body#358
  tryerr#162:
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x21 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 32], x0
    arm64.movImm x19, 0
    arm64.b __il_cont#159
  tryok#338:
    arm64.movRegReg x21, x0
  __il_body#358:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#365:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
  __il_cont#364:
    arm64.movImm x23, 0
  whilehdr#359:
    arm64.cmp x23, x22
    arm64.movImm x0, 0
    arm64.b.hs scmerge#361
  andrhs#360:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#367:
    arm64.movRegReg x1, x23
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#369
  __il_cont#366:
    arm64.cmp x0, 47
    arm64.cset x0, eq
  scmerge#361:
    arm64.cbz x0, __il_cont#357
  leadingSeparators:
    arm64.add x23, x23, 1
    arm64.b whilehdr#359
  __il_cont#357:
    arm64.cmp x23, 0
    arm64.movImm x22, 1
    arm64.b.hi critsplit#398
  __il_body#185:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#343:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont#342:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#187
  empty:
    arm64.movImm x22, 0
    arm64.b __il_cont#184
  ifcont#187:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#345:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.b whilehdr#188
  scan:
    arm64.sub x22, x0, 1
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#347:
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#349
  __il_cont#346:
    arm64.cmp x0, 47
    arm64.b.ne ifcont#192
  isSep:
    arm64.add x23, x22, 1
    arm64.cmp x23, x22
    arm64.b.lo __rc_panic#194
    arm64.b __rc_ok#193
  ifcont#192:
    arm64.movRegReg x0, x22
  whilehdr#188:
    arm64.cmp x0, 0
    arm64.b.hi scan
  whileexit:
    arm64.movImm x22, 0
    arm64.b __il_cont#184
  __rc_ok#193:
    arm64.movRegReg x22, x23
  __il_cont#184:
    arm64.cmp x22, 0
    arm64.b.ne ifcont#180
  noSep:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
    arm64.b __il_cont#177
  ifcont#180:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#341:
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
  __il_cont#340:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.bl String.addressableBytes
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __managed_slice
    arm64.movRegReg x22, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#181
  tryerr#182:
    arm64.leaRdata x0, __str_blob_102  ; "panic at FilePath.maxon:354: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
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
  tryok#181:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
    arm64.movRegReg x0, x22
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitterminal#183:
    arm64.movRegReg x22, x23
  __il_cont#177:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.movRegReg x0, x22
    arm64.bl __str_eq
    arm64.eor x23, x0, 1
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.b scmerge#164
  critsplit#398:
    arm64.movRegReg x23, x22
  scmerge#164:
    arm64.cbz x23, ternaryfalse#166
  ternarytrue#165:
    arm64.loadRegSlot x1, slot1
    arm64.movRegReg x0, x21
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x22, x0
    arm64.b matchcont#167
  ternaryfalse#166:
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
  matchcont#167:
    arm64.leaRdata x24, __str_rec_95  ; "/"
    arm64.cbz x23, ifcont#170
  isAPath:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
    arm64.b exitterminal#169
  ifcont#170:
    arm64.movRegReg x0, x22
    arm64.bl String.cstr
    arm64.bl __gt_subp_resolve_path
    arm64.bl String.init
    arm64.movRegReg x23, x0
  __il_body#377:
    arm64.loadRegBaseDisp.word64 x2, [x23 + 8]
  __il_body#373:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x23
    arm64.movRegReg x3, x24
    arm64.bl findIn
    arm64.cmp x9, 0
    arm64.b.eq tryok#374
  tryerr#375:
    arm64.movImm x0, 0
    arm64.b __il_cont#370
  tryok#374:
    arm64.movImm x0, 1
  __il_cont#370:
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#175
  notOnPath:
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x24 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 32], x0
    arm64.movRegReg x0, x23
    arm64.bl __str_decref
  exitdrop#173:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#174:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
  exitterminal#172:
    arm64.movImm x19, 0
    arm64.movRegReg x21, x24
    arm64.b __il_cont#159
  ifcont#175:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#176:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x23
  exitterminal#169:
    arm64.movImm x19, 0
    arm64.movRegReg x21, x19
    arm64.movRegReg x19, x0
  __il_cont#159:
    arm64.cmp x21, 0
    arm64.b.eq tryok#152
  tryerr#153:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  exitterminal#154:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x21
    arm64.b __il_cont#149
  tryok#152:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.b matchcont#158
  matcharm#155:
    arm64.loadRegSlot x0, slot2
    arm64.sub x19, x0, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.loadRegSlot x1, slot1
    arm64.movRegReg x0, x19
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x19
  matchcont#158:
    arm64.movImm x1, 0
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x1
  __il_cont#149:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#137
  tryerr#89:
    arm64.movImm x19, 0
    arm64.b __il_cont#84
  __il_body#137:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl appendNulTerminated
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x22, 0
    arm64.b forhdr#138
  arg:
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x22, x0
    arm64.b.hs __im_slow#146
  __im_load:
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x1 + x22*8 + 0]
    arm64.movImm x2, 0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
    arm64.cmp x0, 0
    arm64.cset x3, ne
    arm64.leaFuncAddr x4, __gt_promise_drop
    arm64.cmp x0, x4
    arm64.cset x0, ne
    arm64.and x0, x3, x0
    arm64.cbz x0, critsplit#393
  __im_empty:
    arm64.cmp x1, 0
    arm64.b.eq __im_slow#146
  critsplit#394:
    arm64.movRegReg x9, x2
    arm64.b __im_cont
  critsplit#393:
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.cmp x9, 0
    arm64.b.ne tryerr#143
  tryok#142:
    arm64.movRegReg x0, x20
    arm64.bl appendNulTerminated
  forstep:
    arm64.add x22, x22, 1
  forhdr#138:
    arm64.cmp x22, x21
    arm64.b.lo arg
  forexit#141:
    arm64.movRegReg x0, x20
    arm64.bl String.from
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#136:
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.add x20, x0, 1
    arm64.cmp x20, x0
    arm64.b.lo __rc_panic#96
  __il_body#196:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
  __il_body#218:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x23, x0
    arm64.movImm x1, 0
    arm64.movRegReg x0, x23
    arm64.bl __managed_push
    arm64.movRegReg x0, x23
    arm64.bl String.from
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  __il_cont#217:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x24
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  tryok#90:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
    arm64.storeBaseDispReg.word64 [x22 + 16], x20
    arm64.storeBaseDispReg.word64 [x22 + 24], x23
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
  exitterminal#94:
    arm64.movImm x0, 0
    arm64.movRegReg x19, x22
  __il_cont#84:
    arm64.cmp x0, 0
    arm64.b.eq ternaryfalse#64
  tryerr#62:
    arm64.movImm x1, 0
    arm64.b __il_cont#59
  ternaryfalse#64:
    arm64.movImm x20, 0
  matchcont#65:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.addressableBytes
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x22, [x19 + 16]
  __il_body#73:
    arm64.loadRegSlot x0, slot1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x23, x0
  __il_cont#72:
    arm64.movRegReg x0, x23
    arm64.bl String.cstr
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.movRegReg x0, x25
    arm64.bl __str_retain
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x25
    arm64.bl String.addressableBytes
    arm64.movRegReg x25, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x24
    arm64.movRegReg x3, x20
    arm64.movRegReg x4, x25
    arm64.bl __gt_subp_stream_spawn
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
    arm64.movRegReg x0, x25
    arm64.bl __managed_decref
    arm64.movRegReg x0, x23
    arm64.bl __str_decref
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.cmp x20, -1
    arm64.b.ne ifcont#68
  spawnFailed:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 0]
  __il_body#75:
    arm64.bl __gt_subp_last_error_code
    arm64.asr x0, x0, 32
    arm64.bl __enum_from_raw_SpawnPreparation
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#77
  tryok#76:
    arm64.bl lastOsErrorCode
    arm64.cmp x0, 2
    arm64.movImm x1, 1
    arm64.b.eq critsplit#390
  orrhs:
    arm64.cmp x0, 3
    arm64.cset x0, eq
    arm64.b scmerge#79
  critsplit#390:
    arm64.movRegReg x0, x1
  scmerge#79:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#391
  andrhs#80:
    arm64.cmp x21, 0
    arm64.cset x0, eq
    arm64.b scmerge#81
  critsplit#391:
    arm64.movRegReg x0, x1
  scmerge#81:
    arm64.cbz x0, ifcont#83
  executableAbsent:
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x21 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 32], x0
    arm64.b __il_cont#74
  ifcont#83:
    arm64.bl lastErrorMessage
    arm64.movRegReg x20, x0
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x21 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 32], x0
  __il_cont#74:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_SpawnOperands
  exitterminal#67:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x21
    arm64.b __il_cont#59
  ifcont#68:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#71
  __rc_ok#70:
    arm64.movImm x21, 0
    arm64.movImm x22, 0
    arm64.movImm x0, 24
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.storeBaseDispReg.word64 [x23 + 0], x20
    arm64.storeBaseDispReg.byte [x23 + 8], x21
    arm64.storeBaseDispReg.byte [x23 + 16], x22
    arm64.movRegReg x0, x19
    arm64.bl __destruct_SpawnOperands
  exitterminal#69:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x23
  __il_cont#59:
    arm64.cmp x0, 0
    arm64.b.eq tryok#57
  tryerr#58:
    arm64.movImm x1, 0
    arm64.b __il_cont#55
  tryok#57:
    arm64.movImm x0, 0
  __il_cont#55:
    arm64.cmp x0, 0
    arm64.b.eq tryok#53
  tryerr#54:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#51
  tryok#53:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x1
  __il_cont#51:
    arm64.cmp x20, 0
    arm64.b.eq tryok#47
  tryerr#48:
    arm64.loadRegSlot x0, slot1
    arm64.bl __destruct_FilePath
  exitterminal#49:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.b __il_cont#43
  tryok#47:
    arm64.loadRegSlot x0, slot1
    arm64.bl __destruct_FilePath
  exitterminal#50:
    arm64.movImm x0, 0
  __il_cont#43:
    arm64.cmp x0, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x19, 1
    arm64.bl __destruct_SubprocessError
  exitdrop#4:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_Executable
    arm64.movRegReg x0, x19
    arm64.b exitterminal#3
  tryok#1:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_Executable
  __il_body#20:
    arm64.movImm x20, 0
  __il_body#40:
    arm64.loadRegBaseDisp.byte x0, [x19 + 8]
    arm64.cbz x0, ifcont#42
  gone:
    arm64.leaRdata x21, __str_rec_196  ; "used after release()"
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 32], x0
    arm64.movRegReg x0, x22
    arm64.b __il_cont#39
  ifcont#42:
    arm64.movImm x0, 0
  __il_cont#39:
    arm64.cmp x0, 0
    arm64.b.eq tryok#25
  tryerr#26:
    arm64.movImm x1, 0
    arm64.b __il_cont#23
  tryok#25:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.movRegReg x1, x20
    arm64.bl __gt_subp_wait_exit
    arm64.cmp x0, -2
    arm64.b.ne ifcont#28
  timedOut:
    arm64.leaRdata x21, __str_rec_104  ; ""
    arm64.leaRdata x22, __str_rec_104  ; ""
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x23 + 0], x0
    arm64.storeBaseDispReg.word64 [x23 + 8], x20
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x23 + 16], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x23 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 32], x0
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x23
    arm64.b __il_cont#23
  ifcont#28:
    arm64.cmp x0, -4
    arm64.b.ne ifcont#30
  statusLost:
    arm64.leaRdata x20, __str_rec_104  ; ""
    arm64.leaRdata x21, __str_rec_104  ; ""
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 32], x0
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x22
    arm64.b __il_cont#23
  ifcont#30:
    arm64.cmp x0, 0
    arm64.b.ge __rc_chk#38
  failed:
    arm64.cmp x0, -1
    arm64.b.ne ternaryfalse#33
  ternarytrue#32:
    arm64.leaRdata x0, __str_rec_195  ; "wait failed: "
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.bl lastErrorMessage
    arm64.movRegReg x22, x0
    arm64.loadRegBaseDisp.word64 x23, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x22 + 8]
    arm64.add x25, x21, x24
    arm64.add x0, x25, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.add x27, x26, 56
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x0, x27, x21
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x25
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.storeBaseDispReg.word64 [x20 + 8], x26
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 32], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.movRegReg x0, x20
    arm64.b matchcont#34
  ternaryfalse#33:
    arm64.leaRdata x21, __str_rec_104  ; ""
    arm64.leaRdata x22, __str_rec_104  ; ""
    arm64.bl lastOsErrorCode
    arm64.movRegReg x23, x0
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movImm x0, 4
    arm64.storeBaseDispReg.word64 [x24 + 0], x0
    arm64.storeBaseDispReg.word64 [x24 + 8], x20
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.storeBaseDispReg.word64 [x24 + 32], x23
    arm64.movRegReg x0, x24
  matchcont#34:
    arm64.movImm x1, 0
    arm64.b __il_cont#23
  __rc_chk#38:
    arm64.movImm x1, 4294967295
    arm64.cmp x0, x1
    arm64.b.gt __rc_panic#37
  __rc_ok#36:
    arm64.movImm x1, 0
    arm64.xchgRegReg x1, x0
  __il_cont#23:
    arm64.cmp x0, 0
    arm64.b.eq tryok#21
  tryerr#22:
    arm64.movImm x20, 0
    arm64.b __il_cont#19
  tryok#21:
    arm64.movImm x0, 0
    arm64.movRegReg x20, x1
  __il_cont#19:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#14
  tryerr#7:
    arm64.movImm x20, 2
    arm64.bl __destruct_SubprocessError
  exitdrop#8:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
  exitterminal#3:
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
  __il_body#14:
    arm64.loadRegBaseDisp.byte x0, [x19 + 16]
    arm64.cbz x0, ifcont#16
  readerStillOut:
    arm64.leaRdata x0, __str_blob_197  ; "panic at Subprocess.maxon:1368: StreamingSubprocess.release: the stdout reader is still out; reclaimStdout first\x0a"
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
  ifcont#16:
    arm64.loadRegBaseDisp.byte x0, [x19 + 8]
    arm64.eor x0, x0, 1
    arm64.cbz x0, __il_cont#13
  notYet:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __gt_subp_release
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.byte [x19 + 8], x0
  __il_cont#13:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#11
  __rc_chk#12:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic#11
  __rc_ok#10:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
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
  __im_slow#355:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#353
    arm64.b tryok#352
  __im_slow#146:
    arm64.loadRegSlot x0, slot0
    arm64.movRegReg x1, x22
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.b __im_cont
  tryerr#46:
    arm64.leaRdata x0, __str_blob_194  ; "panic at Subprocess.maxon:1041: StreamingSubprocess.spawn: empty path is the sentinel for 'inherit cwd'\x0a"
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
  __rc_panic#331:
    arm64.leaRdata x0, __str_blob_247  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  __rc_panic#135:
    arm64.leaRdata x0, __str_blob_324  ; "panic at Subprocess.maxon:720: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  tryerr#369:
    arm64.leaRdata x0, __str_blob_164  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
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
  tryerr#349:
    arm64.leaRdata x0, __str_blob_164  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
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
  __rc_panic#194:
    arm64.leaRdata x0, __str_blob_256  ; "panic at FilePath.maxon:337: Range check failed: value outside typealias 'BytePos'\x0a"
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
  tryerr#143:
    arm64.leaRdata x0, __str_blob_175  ; "panic at Subprocess.maxon:691: buildArgvBlob: args.get(i) out of bounds\x0a"
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
  __rc_panic#96:
    arm64.leaRdata x0, __str_blob_323  ; "panic at Subprocess.maxon:215: Range check failed: value outside typealias 'Count'\x0a"
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
  tryerr#77:
    arm64.leaRdata x0, __str_blob_193  ; "panic at Subprocess.maxon:956: Subprocess.spawnFailure: the runtime recorded a spawn step this library does not know\x0a"
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
  __rc_panic#71:
    arm64.leaRdata x0, __str_blob_326  ; "panic at Subprocess.maxon:1080: Range check failed: value outside typealias 'StreamingHandle'\x0a"
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
  __rc_panic#37:
    arm64.leaRdata x0, __str_blob_327  ; "panic at Subprocess.maxon:1210: Range check failed: value outside typealias 'ExitInt'\x0a"
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
  __rc_panic#11:
    arm64.leaRdata x0, __str_blob_244  ; "panic at subprocess-reap.a-failed-wait-answers-reaped-elsewhere.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
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

chunk @mrt_host_proc_reap {
    0000: fd 7b bb a9 fd 03 00 91 a0 0b 00 f9 a1 0f 00 f9
    0010: 23 00 80 d2 a3 17 00 f9 02 04 40 f9 e2 09 00 b5
    0020: e2 03 1f aa a2 23 00 f9 a1 0b 40 f9 21 0c 40 f9
    0030: a4 17 40 f9 64 00 00 b4 40 00 80 d2 02 00 00 14
    0040: 20 00 80 d2 a2 c3 00 91 a4 0f 40 f9 84 00 00 b5
    0050: a3 00 80 d2 03 20 a8 f2 03 00 00 14 83 00 80 d2
    0060: 03 20 a8 f2 e4 03 1f aa e8 0b 80 d2 01 00 00 d4
    0070: 1f 10 00 b1 60 fd ff 54 1f 00 00 f1 4b 01 00 54
    0080: a2 c3 00 91 40 10 40 b9 c0 06 00 b4 43 08 40 b9
    0090: 24 00 80 d2 63 00 04 cb 7f 08 00 f1 e8 00 00 54
    00a0: 0b 00 00 14 a4 17 40 f9 04 06 00 b4 e4 03 1f aa
    00b0: a4 17 00 f9 db ff ff 17 a4 0f 40 f9 24 05 00 b4
    00c0: 20 00 80 d2 00 00 00 94 d6 ff ff 17 a1 83 00 91
    00d0: 22 00 80 d2 02 00 a8 f2 e3 03 1f aa 88 20 80 d2
    00e0: 01 00 00 d4 1f 10 00 b1 c0 f9 ff 54 e3 ff 9f d2
    00f0: e3 ff bf f2 00 00 03 8a a1 0b 40 f9 21 0c 40 f9
    0100: 1f 00 01 eb e1 f8 ff 54 a1 0b 40 f9 a2 83 00 91
    0110: 43 00 40 b9 e2 0f 80 d2 65 00 02 8a bf 24 00 f1
    0120: 61 00 00 54 24 14 40 f9 44 01 00 b5 a5 00 00 b5
    0130: 65 fc 48 d3 e2 1f 80 d2 a5 00 02 8a 04 00 00 14
    0140: 25 18 00 f9 02 10 80 d2 a5 00 02 8b 25 10 00 f9
    0150: 22 00 80 d2 22 04 00 f9 20 00 80 d2 04 00 00 14
    0160: e0 03 1f aa 02 00 00 14 40 00 80 d2 fd 7b c5 a8
    0170: c0 03 5f d6
}

chunk @mrt_host_proc_await {
    0000: fd 7b bd a9 fd 03 00 91 a0 0b 00 f9 a1 0f 00 f9
    0010: e2 ff 9f d2 e2 ff bf f2 3f 00 02 eb 81 00 00 54
    0020: 21 00 80 d2 00 00 00 94 12 00 00 14 00 00 00 94
    0030: a1 0f 40 f9 00 00 01 8b a0 13 00 f9 a0 0b 40 f9
    0040: e1 03 1f aa 00 00 00 94 1f 08 00 f1 a0 01 00 54
    0050: 00 01 00 b5 00 00 00 94 a1 13 40 f9 1f 00 01 eb
    0060: ca 00 00 54 20 00 80 d2 00 00 00 94 f4 ff ff 17
    0070: e0 03 1f aa 04 00 00 14 40 20 80 d2 02 00 00 14
    0080: 00 10 80 d2 fd 7b c3 a8 c0 03 5f d6
}
```

```TargetIr:arm64-macos
data {
  __data_CharacterSet.whitespacesAndNewlines@0 = i64 0
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
    arm64.leaRdata x0, __str_rec_65  ; "true"
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.orr x19, x0, 1
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot0, x20
  __il_body#44:
    arm64.leaRdata x0, __str_rec_104  ; ""
  __il_body#323:
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#324
  tryerr#325:
    arm64.movImm x21, 0
    arm64.cbnz x1, tryerr#46
    arm64.b __il_cont#322
  tryok#324:
    arm64.movRegReg x21, x0
  __il_cont#322:
    arm64.storeSlotReg slot1, x21
  __il_body#98:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x0, eq
    arm64.storeSlotReg slot3, x0
    arm64.b.ne matcharm#103
  matcharm#99:
    arm64.sub x19, x19, 1
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
    arm64.leaRdata x1, __str_rec_177  ; "the program name"
    arm64.movRegReg x0, x19
    arm64.bl requireNulFree
    arm64.movRegReg x19, x9
    arm64.cmp x19, 0
    arm64.b.eq tryok#100
  tryerr#101:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.b __il_cont#97
  tryok#100:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.b matchcont#108
  matcharm#103:
    arm64.sub x19, x19, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_178  ; "the program path"
    arm64.bl requireNulFree
    arm64.movRegReg x19, x9
    arm64.cmp x19, 0
    arm64.b.eq tryok#104
  tryerr#105:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x19
    arm64.b __il_cont#97
  tryok#104:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_FilePath
  matchcont#108:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.leaRdata x1, __str_rec_179  ; "the working directory"
    arm64.bl requireNulFree
    arm64.cmp x9, 0
    arm64.b.ne tryerr#110
  tryok#109:
    arm64.leaRdata x1, __layout_Array_String
    arm64.movRegReg x0, x20
    arm64.bl Array.withIterator
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr#111
    arm64.b matcharm#123
  __il_body#328:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot4, x20
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __adapt___witness_ArrayIterator_String.Iterator.current
    arm64.movRegReg x21, x0
  __il_cont#327:
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.storeSlotReg slot5, x0
    arm64.leaRdata x0, __str_rec_180  ; "argument "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x24, x0
  __il_body#329:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#331
  __il_cont#326:
    arm64.add x20, x0, 1
    arm64.cmp x20, x0
    arm64.b.lo __rc_panic#135
  __rc_ok#134:
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
    arm64.b.eq tryok#116
  tryerr#117:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#118:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
  exitdrop#119:
    arm64.loadRegSlot x0, slot5
    arm64.bl __str_decref
  exitdrop#120:
    arm64.loadRegSlot x0, slot4
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#121:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#97
  tryok#116:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
    arm64.loadRegSlot x0, slot5
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot4
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  __il_body#333:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#351:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#355
    arm64.b __im_move
  tryerr#353:
    arm64.movImm x0, 1
    arm64.b __il_cont#350
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#352:
    arm64.movImm x0, 0
  __il_cont#350:
    arm64.cmp x0, 0
    arm64.b.ne forhdr#111
  tryok#334:
    arm64.movImm x0, 0
  forhdr#111:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#328
  forexit#115:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
  matcharm#123:
    arm64.movImm x0, 0
    arm64.b __il_cont#97
  tryerr#110:
    arm64.movRegReg x0, x9
  __il_cont#97:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#150
  tryerr#87:
    arm64.movImm x19, 0
    arm64.b __il_cont#84
  __il_body#150:
    arm64.loadRegSlot x0, slot3
    arm64.cbz x0, matcharm#155
  matcharm#151:
    arm64.loadRegSlot x0, slot2
    arm64.sub x19, x0, 1
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x20, x0
  __il_body#337:
    arm64.movRegReg x0, x19
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#338
  tryerr#339:
    arm64.movImm x21, 0
    arm64.cbz x1, __il_body#358
  tryerr#162:
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x21 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 32], x0
    arm64.movImm x19, 0
    arm64.b __il_cont#159
  tryok#338:
    arm64.movRegReg x21, x0
  __il_body#358:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#365:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
  __il_cont#364:
    arm64.movImm x23, 0
  whilehdr#359:
    arm64.cmp x23, x22
    arm64.movImm x0, 0
    arm64.b.hs scmerge#361
  andrhs#360:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#367:
    arm64.movRegReg x1, x23
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#369
  __il_cont#366:
    arm64.cmp x0, 47
    arm64.cset x0, eq
  scmerge#361:
    arm64.cbz x0, __il_cont#357
  leadingSeparators:
    arm64.add x23, x23, 1
    arm64.b whilehdr#359
  __il_cont#357:
    arm64.cmp x23, 0
    arm64.movImm x22, 1
    arm64.b.hi critsplit#398
  __il_body#185:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#343:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont#342:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#187
  empty:
    arm64.movImm x22, 0
    arm64.b __il_cont#184
  ifcont#187:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#345:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.b whilehdr#188
  scan:
    arm64.sub x22, x0, 1
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#347:
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#349
  __il_cont#346:
    arm64.cmp x0, 47
    arm64.b.ne ifcont#192
  isSep:
    arm64.add x23, x22, 1
    arm64.cmp x23, x22
    arm64.b.lo __rc_panic#194
    arm64.b __rc_ok#193
  ifcont#192:
    arm64.movRegReg x0, x22
  whilehdr#188:
    arm64.cmp x0, 0
    arm64.b.hi scan
  whileexit:
    arm64.movImm x22, 0
    arm64.b __il_cont#184
  __rc_ok#193:
    arm64.movRegReg x22, x23
  __il_cont#184:
    arm64.cmp x22, 0
    arm64.b.ne ifcont#180
  noSep:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
    arm64.b __il_cont#177
  ifcont#180:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#341:
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
  __il_cont#340:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.bl String.addressableBytes
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __managed_slice
    arm64.movRegReg x22, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#181
  tryerr#182:
    arm64.leaRdata x0, __str_blob_102  ; "panic at FilePath.maxon:354: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
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
  tryok#181:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
    arm64.movRegReg x0, x22
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitterminal#183:
    arm64.movRegReg x22, x23
  __il_cont#177:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.movRegReg x0, x22
    arm64.bl __str_eq
    arm64.eor x23, x0, 1
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.b scmerge#164
  critsplit#398:
    arm64.movRegReg x23, x22
  scmerge#164:
    arm64.cbz x23, ternaryfalse#166
  ternarytrue#165:
    arm64.loadRegSlot x1, slot1
    arm64.movRegReg x0, x21
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x22, x0
    arm64.b matchcont#167
  ternaryfalse#166:
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
  matchcont#167:
    arm64.leaRdata x24, __str_rec_95  ; "/"
    arm64.cbz x23, ifcont#170
  isAPath:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
    arm64.b exitterminal#169
  ifcont#170:
    arm64.movRegReg x0, x22
    arm64.bl String.cstr
    arm64.bl __gt_subp_resolve_path
    arm64.bl String.init
    arm64.movRegReg x23, x0
  __il_body#377:
    arm64.loadRegBaseDisp.word64 x2, [x23 + 8]
  __il_body#373:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x23
    arm64.movRegReg x3, x24
    arm64.bl findIn
    arm64.cmp x9, 0
    arm64.b.eq tryok#374
  tryerr#375:
    arm64.movImm x0, 0
    arm64.b __il_cont#370
  tryok#374:
    arm64.movImm x0, 1
  __il_cont#370:
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#175
  notOnPath:
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x24 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x24 + 32], x0
    arm64.movRegReg x0, x23
    arm64.bl __str_decref
  exitdrop#173:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#174:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
  exitterminal#172:
    arm64.movImm x19, 0
    arm64.movRegReg x21, x24
    arm64.b __il_cont#159
  ifcont#175:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#176:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x23
  exitterminal#169:
    arm64.movImm x19, 0
    arm64.movRegReg x21, x19
    arm64.movRegReg x19, x0
  __il_cont#159:
    arm64.cmp x21, 0
    arm64.b.eq tryok#152
  tryerr#153:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  exitterminal#154:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x21
    arm64.b __il_cont#149
  tryok#152:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.b matchcont#158
  matcharm#155:
    arm64.loadRegSlot x0, slot2
    arm64.sub x19, x0, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.loadRegSlot x1, slot1
    arm64.movRegReg x0, x19
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x19
  matchcont#158:
    arm64.movImm x1, 0
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x1
  __il_cont#149:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#137
  tryerr#89:
    arm64.movImm x19, 0
    arm64.b __il_cont#84
  __il_body#137:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl appendNulTerminated
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x22, 0
    arm64.b forhdr#138
  arg:
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x22, x0
    arm64.b.hs __im_slow#146
  __im_load:
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x1 + x22*8 + 0]
    arm64.movImm x2, 0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
    arm64.cmp x0, 0
    arm64.cset x3, ne
    arm64.leaFuncAddr x4, __gt_promise_drop
    arm64.cmp x0, x4
    arm64.cset x0, ne
    arm64.and x0, x3, x0
    arm64.cbz x0, critsplit#393
  __im_empty:
    arm64.cmp x1, 0
    arm64.b.eq __im_slow#146
  critsplit#394:
    arm64.movRegReg x9, x2
    arm64.b __im_cont
  critsplit#393:
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.cmp x9, 0
    arm64.b.ne tryerr#143
  tryok#142:
    arm64.movRegReg x0, x20
    arm64.bl appendNulTerminated
  forstep:
    arm64.add x22, x22, 1
  forhdr#138:
    arm64.cmp x22, x21
    arm64.b.lo arg
  forexit#141:
    arm64.movRegReg x0, x20
    arm64.bl String.from
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#136:
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.add x20, x0, 1
    arm64.cmp x20, x0
    arm64.b.lo __rc_panic#96
  __il_body#196:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
  __il_body#218:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x23, x0
    arm64.movImm x1, 0
    arm64.movRegReg x0, x23
    arm64.bl __managed_push
    arm64.movRegReg x0, x23
    arm64.bl String.from
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  __il_cont#217:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x24
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  tryok#90:
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
    arm64.storeBaseDispReg.word64 [x22 + 16], x20
    arm64.storeBaseDispReg.word64 [x22 + 24], x23
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
  exitterminal#94:
    arm64.movImm x0, 0
    arm64.movRegReg x19, x22
  __il_cont#84:
    arm64.cmp x0, 0
    arm64.b.eq ternaryfalse#64
  tryerr#62:
    arm64.movImm x1, 0
    arm64.b __il_cont#59
  ternaryfalse#64:
    arm64.movImm x20, 0
  matchcont#65:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.addressableBytes
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x22, [x19 + 16]
  __il_body#73:
    arm64.loadRegSlot x0, slot1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x23, x0
  __il_cont#72:
    arm64.movRegReg x0, x23
    arm64.bl String.cstr
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.movRegReg x0, x25
    arm64.bl __str_retain
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x25
    arm64.bl String.addressableBytes
    arm64.movRegReg x25, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x24
    arm64.movRegReg x3, x20
    arm64.movRegReg x4, x25
    arm64.bl __gt_subp_stream_spawn
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
    arm64.movRegReg x0, x25
    arm64.bl __managed_decref
    arm64.movRegReg x0, x23
    arm64.bl __str_decref
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.cmp x20, -1
    arm64.b.ne ifcont#68
  spawnFailed:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 0]
  __il_body#75:
    arm64.bl __gt_subp_last_error_code
    arm64.asr x0, x0, 32
    arm64.bl __enum_from_raw_SpawnPreparation
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#77
  tryok#76:
    arm64.bl lastOsErrorCode
    arm64.cmp x0, 2
    arm64.movImm x1, 1
    arm64.b.eq critsplit#390
  orrhs:
    arm64.cmp x0, 3
    arm64.cset x0, eq
    arm64.b scmerge#79
  critsplit#390:
    arm64.movRegReg x0, x1
  scmerge#79:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#391
  andrhs#80:
    arm64.cmp x21, 0
    arm64.cset x0, eq
    arm64.b scmerge#81
  critsplit#391:
    arm64.movRegReg x0, x1
  scmerge#81:
    arm64.cbz x0, ifcont#83
  executableAbsent:
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x21 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 32], x0
    arm64.b __il_cont#74
  ifcont#83:
    arm64.bl lastErrorMessage
    arm64.movRegReg x20, x0
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x21 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x21 + 32], x0
  __il_cont#74:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_SpawnOperands
  exitterminal#67:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x21
    arm64.b __il_cont#59
  ifcont#68:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#71
  __rc_ok#70:
    arm64.movImm x21, 0
    arm64.movImm x22, 0
    arm64.movImm x0, 24
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.storeBaseDispReg.word64 [x23 + 0], x20
    arm64.storeBaseDispReg.byte [x23 + 8], x21
    arm64.storeBaseDispReg.byte [x23 + 16], x22
    arm64.movRegReg x0, x19
    arm64.bl __destruct_SpawnOperands
  exitterminal#69:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x23
  __il_cont#59:
    arm64.cmp x0, 0
    arm64.b.eq tryok#57
  tryerr#58:
    arm64.movImm x1, 0
    arm64.b __il_cont#55
  tryok#57:
    arm64.movImm x0, 0
  __il_cont#55:
    arm64.cmp x0, 0
    arm64.b.eq tryok#53
  tryerr#54:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#51
  tryok#53:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x1
  __il_cont#51:
    arm64.cmp x20, 0
    arm64.b.eq tryok#47
  tryerr#48:
    arm64.loadRegSlot x0, slot1
    arm64.bl __destruct_FilePath
  exitterminal#49:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.b __il_cont#43
  tryok#47:
    arm64.loadRegSlot x0, slot1
    arm64.bl __destruct_FilePath
  exitterminal#50:
    arm64.movImm x0, 0
  __il_cont#43:
    arm64.cmp x0, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x19, 1
    arm64.bl __destruct_SubprocessError
  exitdrop#4:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_Executable
    arm64.movRegReg x0, x19
    arm64.b exitterminal#3
  tryok#1:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
    arm64.loadRegSlot x0, slot2
    arm64.bl __destruct_Executable
  __il_body#20:
    arm64.movImm x20, 0
  __il_body#40:
    arm64.loadRegBaseDisp.byte x0, [x19 + 8]
    arm64.cbz x0, ifcont#42
  gone:
    arm64.leaRdata x21, __str_rec_196  ; "used after release()"
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 32], x0
    arm64.movRegReg x0, x22
    arm64.b __il_cont#39
  ifcont#42:
    arm64.movImm x0, 0
  __il_cont#39:
    arm64.cmp x0, 0
    arm64.b.eq tryok#25
  tryerr#26:
    arm64.movImm x1, 0
    arm64.b __il_cont#23
  tryok#25:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.movRegReg x1, x20
    arm64.bl __gt_subp_wait_exit
    arm64.cmp x0, -2
    arm64.b.ne ifcont#28
  timedOut:
    arm64.leaRdata x21, __str_rec_104  ; ""
    arm64.leaRdata x22, __str_rec_104  ; ""
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x23 + 0], x0
    arm64.storeBaseDispReg.word64 [x23 + 8], x20
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x23 + 16], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x23 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 32], x0
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x23
    arm64.b __il_cont#23
  ifcont#28:
    arm64.cmp x0, -4
    arm64.b.ne ifcont#30
  statusLost:
    arm64.leaRdata x20, __str_rec_104  ; ""
    arm64.leaRdata x21, __str_rec_104  ; ""
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x20
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 8], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x22 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 32], x0
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x22
    arm64.b __il_cont#23
  ifcont#30:
    arm64.cmp x0, 0
    arm64.b.ge __rc_chk#38
  failed:
    arm64.cmp x0, -1
    arm64.b.ne ternaryfalse#33
  ternarytrue#32:
    arm64.leaRdata x0, __str_rec_195  ; "wait failed: "
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.bl lastErrorMessage
    arm64.movRegReg x22, x0
    arm64.loadRegBaseDisp.word64 x23, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x22 + 8]
    arm64.add x25, x21, x24
    arm64.add x0, x25, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.add x27, x26, 56
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x0, x27, x21
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x25
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.storeBaseDispReg.word64 [x20 + 8], x26
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 32], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.movRegReg x0, x20
    arm64.b matchcont#34
  ternaryfalse#33:
    arm64.leaRdata x21, __str_rec_104  ; ""
    arm64.leaRdata x22, __str_rec_104  ; ""
    arm64.bl lastOsErrorCode
    arm64.movRegReg x23, x0
    arm64.movImm x0, 40
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movImm x0, 4
    arm64.storeBaseDispReg.word64 [x24 + 0], x0
    arm64.storeBaseDispReg.word64 [x24 + 8], x20
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movRegReg x0, x22
    arm64.bl __str_retain
    arm64.bl __str_unshare
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.storeBaseDispReg.word64 [x24 + 32], x23
    arm64.movRegReg x0, x24
  matchcont#34:
    arm64.movImm x1, 0
    arm64.b __il_cont#23
  __rc_chk#38:
    arm64.movImm x1, 4294967295
    arm64.cmp x0, x1
    arm64.b.gt __rc_panic#37
  __rc_ok#36:
    arm64.movImm x1, 0
    arm64.xchgRegReg x1, x0
  __il_cont#23:
    arm64.cmp x0, 0
    arm64.b.eq tryok#21
  tryerr#22:
    arm64.movImm x20, 0
    arm64.b __il_cont#19
  tryok#21:
    arm64.movImm x0, 0
    arm64.movRegReg x20, x1
  __il_cont#19:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#14
  tryerr#7:
    arm64.movImm x20, 2
    arm64.bl __destruct_SubprocessError
  exitdrop#8:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
  exitterminal#3:
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
  __il_body#14:
    arm64.loadRegBaseDisp.byte x0, [x19 + 16]
    arm64.cbz x0, ifcont#16
  readerStillOut:
    arm64.leaRdata x0, __str_blob_197  ; "panic at Subprocess.maxon:1368: StreamingSubprocess.release: the stdout reader is still out; reclaimStdout first\x0a"
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
  ifcont#16:
    arm64.loadRegBaseDisp.byte x0, [x19 + 8]
    arm64.eor x0, x0, 1
    arm64.cbz x0, __il_cont#13
  notYet:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __gt_subp_release
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.byte [x19 + 8], x0
  __il_cont#13:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#11
  __rc_chk#12:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic#11
  __rc_ok#10:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
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
  __im_slow#355:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#353
    arm64.b tryok#352
  __im_slow#146:
    arm64.loadRegSlot x0, slot0
    arm64.movRegReg x1, x22
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.b __im_cont
  tryerr#46:
    arm64.leaRdata x0, __str_blob_194  ; "panic at Subprocess.maxon:1041: StreamingSubprocess.spawn: empty path is the sentinel for 'inherit cwd'\x0a"
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
  __rc_panic#331:
    arm64.leaRdata x0, __str_blob_247  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  __rc_panic#135:
    arm64.leaRdata x0, __str_blob_324  ; "panic at Subprocess.maxon:720: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  tryerr#369:
    arm64.leaRdata x0, __str_blob_164  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
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
  tryerr#349:
    arm64.leaRdata x0, __str_blob_164  ; "panic at String.maxon:302: String.byteAtOrPanic: index out of bounds \xe2\x80\x94 caller guarantees index < byteLength()\x0a"
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
  __rc_panic#194:
    arm64.leaRdata x0, __str_blob_256  ; "panic at FilePath.maxon:337: Range check failed: value outside typealias 'BytePos'\x0a"
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
  tryerr#143:
    arm64.leaRdata x0, __str_blob_175  ; "panic at Subprocess.maxon:691: buildArgvBlob: args.get(i) out of bounds\x0a"
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
  __rc_panic#96:
    arm64.leaRdata x0, __str_blob_323  ; "panic at Subprocess.maxon:215: Range check failed: value outside typealias 'Count'\x0a"
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
  tryerr#77:
    arm64.leaRdata x0, __str_blob_193  ; "panic at Subprocess.maxon:956: Subprocess.spawnFailure: the runtime recorded a spawn step this library does not know\x0a"
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
  __rc_panic#71:
    arm64.leaRdata x0, __str_blob_326  ; "panic at Subprocess.maxon:1080: Range check failed: value outside typealias 'StreamingHandle'\x0a"
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
  __rc_panic#37:
    arm64.leaRdata x0, __str_blob_327  ; "panic at Subprocess.maxon:1210: Range check failed: value outside typealias 'ExitInt'\x0a"
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
  __rc_panic#11:
    arm64.leaRdata x0, __str_blob_244  ; "panic at subprocess-reap.a-failed-wait-answers-reaped-elsewhere.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
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

chunk @mrt_host_proc_reap {
    0000: fd 7b bd a9 fd 03 00 91 a0 0b 00 f9 a1 0f 00 f9
    0010: 02 04 40 f9 c2 06 00 b5 a0 0b 40 f9 00 0c 40 f9
    0020: a1 83 00 91 a2 0f 40 f9 5f 00 00 f1 e2 17 9f 9a
    0030: e3 03 1f aa 10 00 00 90 10 02 40 f9 00 00 00 94
    0040: 00 7c 60 d3 00 fc 60 93 1f 00 00 f1 6a 01 00 54
    0050: 10 00 00 90 10 02 40 f9 00 00 00 94 01 00 40 b9
    0060: 3f 10 00 f1 a0 fd ff 54 e0 ff 9f d2 e0 ff bf f2
    0070: e0 ff df f2 e0 ff ff f2 e3 ff 9f d2 e3 ff bf f2
    0080: 00 00 03 8a 1f 00 03 eb a0 03 00 54 a1 0b 40 f9
    0090: 21 0c 40 f9 1f 00 01 eb e1 02 00 54 a1 0b 40 f9
    00a0: a2 83 00 91 43 00 40 b9 e2 0f 80 d2 65 00 02 8a
    00b0: bf 24 00 f1 61 00 00 54 24 14 40 f9 44 01 00 b5
    00c0: a5 00 00 b5 65 fc 48 d3 e2 1f 80 d2 a5 00 02 8a
    00d0: 04 00 00 14 25 18 00 f9 02 10 80 d2 a5 00 02 8b
    00e0: 25 10 00 f9 22 00 80 d2 22 04 00 f9 20 00 80 d2
    00f0: 04 00 00 14 e0 03 1f aa 02 00 00 14 40 00 80 d2
    0100: fd 7b c3 a8 c0 03 5f d6
}

chunk @mrt_host_proc_await {
    0000: fd 7b bd a9 fd 03 00 91 a0 0b 00 f9 a1 0f 00 f9
    0010: e2 ff 9f d2 e2 ff bf f2 3f 00 02 eb 81 00 00 54
    0020: 21 00 80 d2 00 00 00 94 12 00 00 14 00 00 00 94
    0030: a1 0f 40 f9 00 00 01 8b a0 13 00 f9 a0 0b 40 f9
    0040: e1 03 1f aa 00 00 00 94 1f 08 00 f1 a0 01 00 54
    0050: 00 01 00 b5 00 00 00 94 a1 13 40 f9 1f 00 01 eb
    0060: ca 00 00 54 20 00 80 d2 00 00 00 94 f4 ff ff 17
    0070: e0 03 1f aa 04 00 00 14 40 20 80 d2 02 00 00 14
    0080: 00 10 80 d2 fd 7b c3 a8 c0 03 5f d6
}
```
