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
	let child = try StreamingSubprocess.spawn(Executable.name("true"), arguments: StringArray.create(0)) otherwise return 1
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
  __sched_descendants_armed@512 = i64 0
  __sched_shutdown_flag@520 = i64 0
  __sched_lock@528 = i64 0
  __sched_lock_depth_sink@536 = i64 0
  __sched_nmspinning@544 = i64 0
  __sched_needspinning@552 = i64 0
  __sched_sysmon_event@560 = i64 0
  __sched_sysmon_wait@568 = i64 0
  __sched_timer_starts@576 = i64 0
  __mrt_envp@584 = i64 0
  __mrt_signal_stack_bytes@592 = i64 0
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
  __il_body#46:
    x64.leaRegRdata rcx, [rip + __str_rec_104]  ; ""
  __il_body#198:
    x64.callDirect FilePath.create
    x64.cmpRegImm32 r10, 0
    x64.setccReg notEqual, rax
    x64.jcc equal, tryok#199
  tryerr#200:
    x64.movRegImm32 r13, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#48
    x64.jmp __il_cont#197
  tryok#199:
    x64.movRegReg r13, r8
  __il_cont#197:
    x64.storeSlotReg slot0, r13
  __il_body#100:
    x64.movRegReg rax, rbx
    x64.andRegImm32 rax, rbx, 7
    x64.cmpRegImm32 rax, 1
    x64.setccReg equal, rax
    x64.storeSlotReg slot3, rax
    x64.jcc notEqual, matcharm#105
  matcharm#101:
    x64.leaRegRegImm32 rbx, rbx, -1
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg r14, r8
    x64.leaRegRdata rdx, [rip + __str_rec_177]  ; "the program name"
    x64.movRegReg rcx, rbx
    x64.callDirect requireNulFree
    x64.movRegReg rbx, r10
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, tryok#102
  tryerr#103:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.movRegReg rax, rbx
    x64.jmp __il_cont#99
  tryok#102:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.jmp matchcont#110
  matcharm#105:
    x64.leaRegRegImm32 rbx, rbx, -2
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r14, r8
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.leaRegRdata rdx, [rip + __str_rec_178]  ; "the program path"
    x64.callDirect requireNulFree
    x64.movRegReg rbx, r10
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, tryok#106
  tryerr#107:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_FilePath
    x64.movRegReg rax, rbx
    x64.jmp __il_cont#99
  tryok#106:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_FilePath
  matchcont#110:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.leaRegRdata rdx, [rip + __str_rec_179]  ; "the working directory"
    x64.callDirect requireNulFree
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#112
  tryok#111:
    x64.leaRegRdata rdx, [rip + __layout_Array_String]
    x64.movRegReg rcx, r12
    x64.callDirect Array.withIterator
    x64.movRegReg rbx, r8
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forhdr#113
    x64.jmp matcharm#125
  __il_body#203:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.storeSlotReg slot4, r12
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __adapt___witness_ArrayIterator_String.Iterator.current
    x64.storeSlotReg slot8, r8
  __il_cont#202:
    x64.movRegReg rcx, r8
    x64.callDirect __str_retain
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_180]  ; "argument "
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.storeSlotReg slot6, r8
  __il_body#204:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic#206
  __il_cont#201:
    x64.leaRegRegImm32 r12, rax, 1
    x64.cmpRegReg r12, rax
    x64.jcc below, __rc_panic#137
  __rc_ok#136:
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
    x64.jcc equal, tryok#118
  tryerr#119:
    x64.loadRegSlot rcx, slot13
    x64.callDirect __str_decref
  exitdrop#120:
    x64.loadRegSlot rcx, slot6
    x64.callDirect __destruct_ArrayIterator_String
  exitdrop#121:
    x64.loadRegSlot rcx, slot5
    x64.callDirect __str_decref
  exitdrop#122:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __destruct_ArrayIterator_String
    x64.loadRegSlot rcx, slot8
    x64.callDirect __str_decref
  exitdrop#123:
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_String_String
    x64.movRegReg rax, r12
    x64.jmp __il_cont#99
  tryok#118:
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
  __il_body#208:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
  __il_body#226:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#230
    x64.jmp __im_move
  tryerr#228:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#225
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#227:
    x64.movRegImm32 rax, 0
  __il_cont#225:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, forhdr#113
  tryok#209:
    x64.movRegImm32 rax, 0
  forhdr#113:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#203
  forexit#117:
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_String_String
  matcharm#125:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#99
  tryerr#112:
    x64.movRegReg rax, r10
  __il_cont#99:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#152
  tryerr#89:
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#86
  __il_body#152:
    x64.loadRegSlot rax, slot3
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, matcharm#157
  matcharm#153:
    x64.loadRegSlot rax, slot1
    x64.leaRegRegImm32 rbx, rax, -1
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.storeSlotReg slot16, r8
  __il_body#212:
    x64.movRegReg rcx, rbx
    x64.callDirect FilePath.create
    x64.cmpRegImm32 r10, 0
    x64.setccReg notEqual, rax
    x64.jcc equal, tryok#213
  tryerr#214:
    x64.movRegImm32 r12, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#233
  tryerr#164:
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
    x64.jmp __il_cont#161
  tryok#213:
    x64.movRegReg r12, r8
  __il_body#233:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#240:
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
  __il_cont#239:
    x64.movRegImm32 r14, 0
  whilehdr#234:
    x64.cmpRegReg r14, r13
    x64.movRegImm32 rax, 0
    x64.jcc aboveEqual, scmerge#236
  andrhs#235:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
  __il_body#242:
    x64.movRegReg rdx, r14
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#244
  __il_cont#241:
    x64.cmpRegImm32 r8, 47
    x64.setccReg equal, rax
  scmerge#236:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_cont#232
  leadingSeparators:
    x64.leaRegRegImm32 r14, r14, 1
    x64.jmp whilehdr#234
  __il_cont#232:
    x64.cmpRegImm32 r14, 0
    x64.movRegImm32 r13, 1
    x64.jcc above, critsplit#253
  __il_body#187:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#218:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __il_cont#217:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont#189
  empty:
    x64.movRegImm32 r13, 0
    x64.jmp __il_cont#186
  ifcont#189:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#220:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.jmp whilehdr#190
  scan:
    x64.leaRegRegImm32 r13, rax, -1
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
  __il_body#222:
    x64.movRegReg rdx, r13
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#224
  __il_cont#221:
    x64.cmpRegImm32 r8, 47
    x64.jcc notEqual, ifcont#194
  isSep:
    x64.leaRegRegImm32 r14, r13, 1
    x64.cmpRegReg r14, r13
    x64.jcc below, __rc_panic#196
    x64.jmp __rc_ok#195
  ifcont#194:
    x64.movRegReg rax, r13
  whilehdr#190:
    x64.cmpRegImm32 rax, 0
    x64.jcc above, scan
  whileexit:
    x64.movRegImm32 r13, 0
    x64.jmp __il_cont#186
  __rc_ok#195:
    x64.movRegReg r13, r14
  __il_cont#186:
    x64.cmpRegImm32 r13, 0
    x64.jcc notEqual, ifcont#182
  noSep:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __str_retain
    x64.movRegReg r13, r8
    x64.jmp __il_cont#179
  ifcont#182:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#216:
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
  __il_cont#215:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect String.addressableBytes
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __managed_slice
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#183
  tryerr#184:
    x64.leaRegRdata rcx, [rip + __str_blob_102]  ; "panic at FilePath.maxon:352: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
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
  tryok#183:
    x64.movRegReg rcx, r15
    x64.callDirect __managed_decref
    x64.movRegReg rcx, r13
    x64.callDirect __managed_retain
    x64.movRegReg rcx, r8
    x64.callDirect String.init
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitterminal#185:
    x64.movRegReg r13, r14
  __il_cont#179:
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __str_eq
    x64.movRegReg r14, r8
    x64.xorRegImm32 r14, r8, 1
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.jmp scmerge#166
  critsplit#253:
    x64.movRegReg r14, r13
  scmerge#166:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, ternaryfalse#168
  ternarytrue#167:
    x64.loadRegSlot rdx, slot0
    x64.movRegReg rcx, r12
    x64.callDirect launchPathInWorkingDirectory
    x64.movRegReg r13, r8
    x64.jmp matchcont#169
  ternaryfalse#168:
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg r13, r8
  matchcont#169:
    x64.leaRegRdata r15, [rip + __str_rec_95]  ; "/"
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, ifcont#172
  isAPath:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_FilePath
    x64.movRegReg rax, r13
    x64.jmp exitterminal#171
  ifcont#172:
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
    x64.jcc equal, ifcont#177
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
  exitdrop#175:
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
  exitdrop#176:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_FilePath
  exitterminal#174:
    x64.movRegImm32 rbx, 0
    x64.movRegReg r12, r15
    x64.jmp __il_cont#161
  ifcont#177:
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
  exitdrop#178:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_FilePath
    x64.movRegReg rax, r14
  exitterminal#171:
    x64.movRegImm32 rbx, 0
    x64.movRegReg r12, rbx
    x64.movRegReg rbx, rax
  __il_cont#161:
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, tryok#154
  tryerr#155:
    x64.loadRegSlot rcx, slot16
    x64.callDirect __str_decref
  exitterminal#156:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rax, r12
    x64.jmp __il_cont#151
  tryok#154:
    x64.loadRegSlot rcx, slot16
    x64.callDirect __str_decref
    x64.movRegReg rax, rbx
    x64.jmp matchcont#160
  matcharm#157:
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
  matchcont#160:
    x64.movRegImm32 rcx, 0
    x64.movRegReg rbx, rax
    x64.movRegReg rax, rcx
  __il_cont#151:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#139
  tryerr#91:
    x64.movRegImm32 rbx, 0
    x64.jmp __il_cont#86
  __il_body#139:
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
    x64.jmp forhdr#140
  arg:
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegReg r14, rax
    x64.jcc aboveEqual, __im_slow#148
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
    x64.jcc equal, critsplit#248
  __im_empty:
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_slow#148
  critsplit#249:
    x64.movRegReg r10, rcx
    x64.jmp __im_cont
  critsplit#248:
    x64.movRegReg r10, rcx
  __im_cont:
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#145
  tryok#144:
    x64.movRegReg rcx, r12
    x64.callDirect appendNulTerminated
  forstep:
    x64.leaRegRegImm32 r14, r14, 1
  forhdr#140:
    x64.cmpRegReg r14, r13
    x64.jcc below, arg
  forexit#143:
    x64.movRegReg rcx, r12
    x64.callDirect String.from
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  __il_cont#138:
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.leaRegRegImm32 r12, rax, 1
    x64.cmpRegReg r12, rax
    x64.jcc below, __rc_panic#98
  __rc_ok#97:
    x64.movRegImm32 rcx, 1
    x64.callDirect spawnEnvironmentFor
    x64.movRegReg r14, r8
    x64.movRegReg r15, r10
    x64.cmpRegImm32 r15, 0
    x64.jcc equal, tryok#92
  tryerr#93:
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
  exitdrop#95:
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
  exitterminal#94:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rax, r15
    x64.jmp __il_cont#86
  tryok#92:
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
  exitterminal#96:
    x64.movRegImm32 rax, 0
    x64.movRegReg rbx, r15
  __il_cont#86:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, matchcont#67
  tryerr#64:
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#61
  matchcont#67:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.callDirect String.addressableBytes
    x64.movRegReg r12, r8
    x64.loadRegBaseDisp.word64 r13, [rbx + 16]
  __il_body#75:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect __str_retain
    x64.storeSlotReg slot12, r8
  __il_cont#74:
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
    x64.jcc notEqual, ifcont#70
  spawnFailed:
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
  __il_body#77:
    x64.callDirect __gt_subp_last_error_code
    x64.movRegReg rcx, r8
    x64.sarRegImm8 rcx, r8, 32
    x64.callDirect __enum_from_raw_SpawnPreparation
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#79
  tryok#78:
    x64.callDirect lastOsErrorCode
    x64.cmpRegImm32 r8, 2
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#81
  orrhs:
    x64.cmpRegImm32 r8, 3
    x64.setccReg equal, rax
  scmerge#81:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#246
  andrhs#82:
    x64.cmpRegImm32 r13, 0
    x64.setccReg equal, rax
    x64.jmp scmerge#83
  critsplit#246:
    x64.movRegReg rax, rcx
  scmerge#83:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#85
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
    x64.jmp __il_cont#76
  ifcont#85:
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
  __il_cont#76:
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_SpawnOperands
  exitterminal#69:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rax, r13
    x64.jmp __il_cont#61
  ifcont#70:
    x64.cmpRegImm32 r13, 0
    x64.jcc less, __rc_panic#73
  __rc_ok#72:
    x64.movRegImm32 r12, 0
    x64.movRegImm32 r14, 0
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeBaseDispReg.word64 [r15 + 0], r13
    x64.storeBaseDispReg.byte [r15 + 8], r12
    x64.storeBaseDispReg.byte [r15 + 16], r14
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r15 + 24], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_SpawnOperands
  exitterminal#71:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r15
  __il_cont#61:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#59
  tryerr#60:
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#57
  tryok#59:
    x64.movRegImm32 rax, 0
  __il_cont#57:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#55
  tryerr#56:
    x64.movRegImm32 rbx, 0
    x64.movRegReg r12, rax
    x64.jmp __il_cont#53
  tryok#55:
    x64.movRegImm32 rbx, 0
    x64.movRegReg r12, rbx
    x64.movRegReg rbx, rcx
  __il_cont#53:
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, tryok#49
  tryerr#50:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __destruct_FilePath
  exitterminal#51:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r12
    x64.jmp __il_cont#45
  tryok#49:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __destruct_FilePath
  exitterminal#52:
    x64.movRegImm32 rcx, 0
  __il_cont#45:
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
  __il_body#22:
    x64.movRegImm32 r12, 0
  __il_body#42:
    x64.loadRegBaseDisp.byte rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#44
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
    x64.jmp __il_cont#41
  ifcont#44:
    x64.movRegImm32 rax, 0
  __il_cont#41:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#27
  tryerr#28:
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#25
  tryok#27:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.movRegReg rdx, r12
    x64.callDirect __gt_subp_wait_exit
    x64.cmpRegImm32 r8, -2
    x64.jcc notEqual, ifcont#30
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
    x64.jmp __il_cont#25
  ifcont#30:
    x64.cmpRegImm32 r8, -4
    x64.jcc notEqual, ifcont#32
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
    x64.jmp __il_cont#25
  ifcont#32:
    x64.cmpRegImm32 r8, 0
    x64.jcc greaterEqual, __rc_chk#40
  failed:
    x64.cmpRegImm32 r8, -1
    x64.jcc notEqual, ternaryfalse#35
  ternarytrue#34:
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
    x64.jmp matchcont#36
  ternaryfalse#35:
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
  matchcont#36:
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#25
  __rc_chk#40:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic#39
  __rc_ok#38:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r8
  __il_cont#25:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#23
  tryerr#24:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, rax
    x64.jmp __il_cont#21
  tryok#23:
    x64.movRegImm32 rax, 0
    x64.movRegReg rbx, rcx
    x64.movRegReg rcx, rax
  __il_cont#21:
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
    x64.leaRegRdata rcx, [rip + __str_blob_197]  ; "panic at Subprocess.maxon:1429: StreamingSubprocess.release: the stdout reader is still out; reclaimStdout first\x0a"
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
    x64.loadRegBaseDisp.byte rax, [rax + 24]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#18
  stderrReaderStillOut:
    x64.leaRegRdata rcx, [rip + __str_blob_198]  ; "panic at Subprocess.maxon:1433: StreamingSubprocess.release: the stderr reader is still out; reclaimStderr first\x0a"
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
  ifcont#18:
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
  __im_slow#230:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#228
    x64.jmp tryok#227
  __im_slow#148:
    x64.loadRegSlot rcx, slot2
    x64.movRegReg rdx, r14
    x64.callDirect __managed_get
    x64.movRegReg rdx, r8
    x64.jmp __im_cont
  tryerr#48:
    x64.leaRegRdata rcx, [rip + __str_blob_194]  ; "panic at Subprocess.maxon:1074: StreamingSubprocess.spawn: empty path is the sentinel for 'inherit cwd'\x0a"
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
  __rc_panic#206:
    x64.leaRegRdata rcx, [rip + __str_blob_248]  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  __rc_panic#137:
    x64.leaRegRdata rcx, [rip + __str_blob_325]  ; "panic at Subprocess.maxon:721: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  tryerr#244:
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
  tryerr#224:
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
  __rc_panic#196:
    x64.leaRegRdata rcx, [rip + __str_blob_257]  ; "panic at FilePath.maxon:335: Range check failed: value outside typealias 'BytePos'\x0a"
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
  tryerr#145:
    x64.leaRegRdata rcx, [rip + __str_blob_175]  ; "panic at Subprocess.maxon:692: buildArgvBlob: args.get(i) out of bounds\x0a"
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
  __rc_panic#98:
    x64.leaRegRdata rcx, [rip + __str_blob_324]  ; "panic at Subprocess.maxon:215: Range check failed: value outside typealias 'Count'\x0a"
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
  tryerr#79:
    x64.leaRegRdata rcx, [rip + __str_blob_193]  ; "panic at Subprocess.maxon:957: Subprocess.spawnFailure: the runtime recorded a spawn step this library does not know\x0a"
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
  __rc_panic#73:
    x64.leaRegRdata rcx, [rip + __str_blob_327]  ; "panic at Subprocess.maxon:1113: Range check failed: value outside typealias 'StreamingHandle'\x0a"
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
  __rc_panic#39:
    x64.leaRegRdata rcx, [rip + __str_blob_328]  ; "panic at Subprocess.maxon:1261: Range check failed: value outside typealias 'ExitInt'\x0a"
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
    x64.leaRegRdata rcx, [rip + __str_blob_245]  ; "panic at subprocess-reap.a-failed-wait-answers-reaped-elsewhere.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    0010: 48 b8 ff ff ff ff 00 00 00 00 48 39 c2 0f 85 1c
    0020: 00 00 00 ba 01 00 00 00 e8 00 00 00 00 48 81 f8
    0030: 02 00 00 00 0f 84 6e 00 00 00 e9 55 00 00 00 e8
    0040: 00 00 00 00 48 8b 4d f0 48 01 c8 48 89 45 e8 48
    0050: 8b 4d f8 ba 00 00 00 00 e8 00 00 00 00 48 81 f8
    0060: 02 00 00 00 0f 84 3e 00 00 00 48 85 c0 0f 85 21
    0070: 00 00 00 e8 00 00 00 00 48 8b 4d e8 48 39 c8 0f
    0080: 8d 19 00 00 00 b9 01 00 00 00 e8 00 00 00 00 e9
    0090: bb ff ff ff b8 00 00 00 00 e9 0f 00 00 00 b8 02
    00a0: 01 00 00 e9 05 00 00 00 b8 80 00 00 00 48 83 c4
    00b0: 40 5d c3
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
  __sched_descendants_armed@512 = i64 0
  __sched_shutdown_flag@520 = i64 0
  __sched_lock@528 = i64 0
  __sched_lock_depth_sink@536 = i64 0
  __sched_nmspinning@544 = i64 0
  __sched_needspinning@552 = i64 0
  __sched_sysmon_event@560 = i64 0
  __sched_sysmon_wait@568 = i64 0
  __sched_timer_starts@576 = i64 0
  __mrt_envp@584 = i64 0
  __mrt_signal_stack_bytes@592 = i64 0
  __mrt_hwcap_atomics@600 = i64 0
  __mrt_tls_next_key@608 = i64 0
  __io_pipe_seq@616 = i64 0
  __subp_table@624 = i64 0
  __subp_last_error@632 = i64 0
  __slab_arena_list@640 = i64 0
  __slab_arena_map_l1@648 = i64 0
  __slab_state@656 = i64 0
  __mrt_program_started@664 = i8 0
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
  __il_body#46:
    arm64.leaRdata x0, __str_rec_104  ; ""
  __il_body#325:
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#326
  tryerr#327:
    arm64.movImm x21, 0
    arm64.cbnz x1, tryerr#48
    arm64.b __il_cont#324
  tryok#326:
    arm64.movRegReg x21, x0
  __il_cont#324:
    arm64.storeSlotReg slot1, x21
  __il_body#100:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x0, eq
    arm64.storeSlotReg slot3, x0
    arm64.b.ne matcharm#105
  matcharm#101:
    arm64.sub x19, x19, 1
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
    arm64.leaRdata x1, __str_rec_177  ; "the program name"
    arm64.movRegReg x0, x19
    arm64.bl requireNulFree
    arm64.movRegReg x19, x9
    arm64.cmp x19, 0
    arm64.b.eq tryok#102
  tryerr#103:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.b __il_cont#99
  tryok#102:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.b matchcont#110
  matcharm#105:
    arm64.sub x19, x19, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_178  ; "the program path"
    arm64.bl requireNulFree
    arm64.movRegReg x19, x9
    arm64.cmp x19, 0
    arm64.b.eq tryok#106
  tryerr#107:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x19
    arm64.b __il_cont#99
  tryok#106:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_FilePath
  matchcont#110:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.leaRdata x1, __str_rec_179  ; "the working directory"
    arm64.bl requireNulFree
    arm64.cmp x9, 0
    arm64.b.ne tryerr#112
  tryok#111:
    arm64.leaRdata x1, __layout_Array_String
    arm64.movRegReg x0, x20
    arm64.bl Array.withIterator
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr#113
    arm64.b matcharm#125
  __il_body#330:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot4, x20
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __adapt___witness_ArrayIterator_String.Iterator.current
    arm64.movRegReg x21, x0
  __il_cont#329:
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.storeSlotReg slot5, x0
    arm64.leaRdata x0, __str_rec_180  ; "argument "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x24, x0
  __il_body#331:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#333
  __il_cont#328:
    arm64.add x20, x0, 1
    arm64.cmp x20, x0
    arm64.b.lo __rc_panic#137
  __rc_ok#136:
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
    arm64.b.eq tryok#118
  tryerr#119:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#120:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
  exitdrop#121:
    arm64.loadRegSlot x0, slot5
    arm64.bl __str_decref
  exitdrop#122:
    arm64.loadRegSlot x0, slot4
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#123:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#99
  tryok#118:
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
  __il_body#335:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#353:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#357
    arm64.b __im_move
  tryerr#355:
    arm64.movImm x0, 1
    arm64.b __il_cont#352
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#354:
    arm64.movImm x0, 0
  __il_cont#352:
    arm64.cmp x0, 0
    arm64.b.ne forhdr#113
  tryok#336:
    arm64.movImm x0, 0
  forhdr#113:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#330
  forexit#117:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
  matcharm#125:
    arm64.movImm x0, 0
    arm64.b __il_cont#99
  tryerr#112:
    arm64.movRegReg x0, x9
  __il_cont#99:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#152
  tryerr#89:
    arm64.movImm x19, 0
    arm64.b __il_cont#86
  __il_body#152:
    arm64.loadRegSlot x0, slot3
    arm64.cbz x0, matcharm#157
  matcharm#153:
    arm64.loadRegSlot x0, slot2
    arm64.sub x19, x0, 1
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x20, x0
  __il_body#339:
    arm64.movRegReg x0, x19
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#340
  tryerr#341:
    arm64.movImm x21, 0
    arm64.cbz x1, __il_body#360
  tryerr#164:
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
    arm64.b __il_cont#161
  tryok#340:
    arm64.movRegReg x21, x0
  __il_body#360:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#367:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
  __il_cont#366:
    arm64.movImm x23, 0
  whilehdr#361:
    arm64.cmp x23, x22
    arm64.movImm x0, 0
    arm64.b.hs scmerge#363
  andrhs#362:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#369:
    arm64.movRegReg x1, x23
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#371
  __il_cont#368:
    arm64.cmp x0, 47
    arm64.cset x0, eq
  scmerge#363:
    arm64.cbz x0, __il_cont#359
  leadingSeparators:
    arm64.add x23, x23, 1
    arm64.b whilehdr#361
  __il_cont#359:
    arm64.cmp x23, 0
    arm64.movImm x22, 1
    arm64.b.hi critsplit#400
  __il_body#187:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#345:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont#344:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#189
  empty:
    arm64.movImm x22, 0
    arm64.b __il_cont#186
  ifcont#189:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#347:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.b whilehdr#190
  scan:
    arm64.sub x22, x0, 1
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#349:
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#351
  __il_cont#348:
    arm64.cmp x0, 47
    arm64.b.ne ifcont#194
  isSep:
    arm64.add x23, x22, 1
    arm64.cmp x23, x22
    arm64.b.lo __rc_panic#196
    arm64.b __rc_ok#195
  ifcont#194:
    arm64.movRegReg x0, x22
  whilehdr#190:
    arm64.cmp x0, 0
    arm64.b.hi scan
  whileexit:
    arm64.movImm x22, 0
    arm64.b __il_cont#186
  __rc_ok#195:
    arm64.movRegReg x22, x23
  __il_cont#186:
    arm64.cmp x22, 0
    arm64.b.ne ifcont#182
  noSep:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
    arm64.b __il_cont#179
  ifcont#182:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#343:
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
  __il_cont#342:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.bl String.addressableBytes
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __managed_slice
    arm64.movRegReg x22, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#183
  tryerr#184:
    arm64.leaRdata x0, __str_blob_102  ; "panic at FilePath.maxon:352: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
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
  tryok#183:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
    arm64.movRegReg x0, x22
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitterminal#185:
    arm64.movRegReg x22, x23
  __il_cont#179:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.movRegReg x0, x22
    arm64.bl __str_eq
    arm64.eor x23, x0, 1
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.b scmerge#166
  critsplit#400:
    arm64.movRegReg x23, x22
  scmerge#166:
    arm64.cbz x23, ternaryfalse#168
  ternarytrue#167:
    arm64.loadRegSlot x1, slot1
    arm64.movRegReg x0, x21
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x22, x0
    arm64.b matchcont#169
  ternaryfalse#168:
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
  matchcont#169:
    arm64.leaRdata x24, __str_rec_95  ; "/"
    arm64.cbz x23, ifcont#172
  isAPath:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
    arm64.b exitterminal#171
  ifcont#172:
    arm64.movRegReg x0, x22
    arm64.bl String.cstr
    arm64.bl __gt_subp_resolve_path
    arm64.bl String.init
    arm64.movRegReg x23, x0
  __il_body#379:
    arm64.loadRegBaseDisp.word64 x2, [x23 + 8]
  __il_body#375:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x23
    arm64.movRegReg x3, x24
    arm64.bl findIn
    arm64.cmp x9, 0
    arm64.b.eq tryok#376
  tryerr#377:
    arm64.movImm x0, 0
    arm64.b __il_cont#372
  tryok#376:
    arm64.movImm x0, 1
  __il_cont#372:
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#177
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
  exitdrop#175:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#176:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
  exitterminal#174:
    arm64.movImm x19, 0
    arm64.movRegReg x21, x24
    arm64.b __il_cont#161
  ifcont#177:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#178:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x23
  exitterminal#171:
    arm64.movImm x19, 0
    arm64.movRegReg x21, x19
    arm64.movRegReg x19, x0
  __il_cont#161:
    arm64.cmp x21, 0
    arm64.b.eq tryok#154
  tryerr#155:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  exitterminal#156:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x21
    arm64.b __il_cont#151
  tryok#154:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.b matchcont#160
  matcharm#157:
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
  matchcont#160:
    arm64.movImm x1, 0
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x1
  __il_cont#151:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#139
  tryerr#91:
    arm64.movImm x19, 0
    arm64.b __il_cont#86
  __il_body#139:
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
    arm64.b forhdr#140
  arg:
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x22, x0
    arm64.b.hs __im_slow#148
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
    arm64.cbz x0, critsplit#395
  __im_empty:
    arm64.cmp x1, 0
    arm64.b.eq __im_slow#148
  critsplit#396:
    arm64.movRegReg x9, x2
    arm64.b __im_cont
  critsplit#395:
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.cmp x9, 0
    arm64.b.ne tryerr#145
  tryok#144:
    arm64.movRegReg x0, x20
    arm64.bl appendNulTerminated
  forstep:
    arm64.add x22, x22, 1
  forhdr#140:
    arm64.cmp x22, x21
    arm64.b.lo arg
  forexit#143:
    arm64.movRegReg x0, x20
    arm64.bl String.from
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#138:
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.add x20, x0, 1
    arm64.cmp x20, x0
    arm64.b.lo __rc_panic#98
  __il_body#198:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
  __il_body#220:
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
  __il_cont#219:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x24
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  tryok#92:
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
  exitterminal#96:
    arm64.movImm x0, 0
    arm64.movRegReg x19, x22
  __il_cont#86:
    arm64.cmp x0, 0
    arm64.b.eq ternaryfalse#66
  tryerr#64:
    arm64.movImm x1, 0
    arm64.b __il_cont#61
  ternaryfalse#66:
    arm64.movImm x20, 0
  matchcont#67:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.addressableBytes
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x22, [x19 + 16]
  __il_body#75:
    arm64.loadRegSlot x0, slot1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x23, x0
  __il_cont#74:
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
    arm64.b.ne ifcont#70
  spawnFailed:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 0]
  __il_body#77:
    arm64.bl __gt_subp_last_error_code
    arm64.asr x0, x0, 32
    arm64.bl __enum_from_raw_SpawnPreparation
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#79
  tryok#78:
    arm64.bl lastOsErrorCode
    arm64.cmp x0, 2
    arm64.movImm x1, 1
    arm64.b.eq critsplit#392
  orrhs:
    arm64.cmp x0, 3
    arm64.cset x0, eq
    arm64.b scmerge#81
  critsplit#392:
    arm64.movRegReg x0, x1
  scmerge#81:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#393
  andrhs#82:
    arm64.cmp x21, 0
    arm64.cset x0, eq
    arm64.b scmerge#83
  critsplit#393:
    arm64.movRegReg x0, x1
  scmerge#83:
    arm64.cbz x0, ifcont#85
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
    arm64.b __il_cont#76
  ifcont#85:
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
  __il_cont#76:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_SpawnOperands
  exitterminal#69:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x21
    arm64.b __il_cont#61
  ifcont#70:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#73
  __rc_ok#72:
    arm64.movImm x21, 0
    arm64.movImm x22, 0
    arm64.movImm x23, 0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.storeBaseDispReg.word64 [x24 + 0], x20
    arm64.storeBaseDispReg.byte [x24 + 8], x21
    arm64.storeBaseDispReg.byte [x24 + 16], x22
    arm64.storeBaseDispReg.byte [x24 + 24], x23
    arm64.movRegReg x0, x19
    arm64.bl __destruct_SpawnOperands
  exitterminal#71:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x24
  __il_cont#61:
    arm64.cmp x0, 0
    arm64.b.eq tryok#59
  tryerr#60:
    arm64.movImm x1, 0
    arm64.b __il_cont#57
  tryok#59:
    arm64.movImm x0, 0
  __il_cont#57:
    arm64.cmp x0, 0
    arm64.b.eq tryok#55
  tryerr#56:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#53
  tryok#55:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x1
  __il_cont#53:
    arm64.cmp x20, 0
    arm64.b.eq tryok#49
  tryerr#50:
    arm64.loadRegSlot x0, slot1
    arm64.bl __destruct_FilePath
  exitterminal#51:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.b __il_cont#45
  tryok#49:
    arm64.loadRegSlot x0, slot1
    arm64.bl __destruct_FilePath
  exitterminal#52:
    arm64.movImm x0, 0
  __il_cont#45:
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
  __il_body#22:
    arm64.movImm x20, 0
  __il_body#42:
    arm64.loadRegBaseDisp.byte x0, [x19 + 8]
    arm64.cbz x0, ifcont#44
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
    arm64.b __il_cont#41
  ifcont#44:
    arm64.movImm x0, 0
  __il_cont#41:
    arm64.cmp x0, 0
    arm64.b.eq tryok#27
  tryerr#28:
    arm64.movImm x1, 0
    arm64.b __il_cont#25
  tryok#27:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.movRegReg x1, x20
    arm64.bl __gt_subp_wait_exit
    arm64.cmp x0, -2
    arm64.b.ne ifcont#30
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
    arm64.b __il_cont#25
  ifcont#30:
    arm64.cmp x0, -4
    arm64.b.ne ifcont#32
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
    arm64.b __il_cont#25
  ifcont#32:
    arm64.cmp x0, 0
    arm64.b.ge __rc_chk#40
  failed:
    arm64.cmp x0, -1
    arm64.b.ne ternaryfalse#35
  ternarytrue#34:
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
    arm64.b matchcont#36
  ternaryfalse#35:
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
  matchcont#36:
    arm64.movImm x1, 0
    arm64.b __il_cont#25
  __rc_chk#40:
    arm64.movImm x1, 4294967295
    arm64.cmp x0, x1
    arm64.b.gt __rc_panic#39
  __rc_ok#38:
    arm64.movImm x1, 0
    arm64.xchgRegReg x1, x0
  __il_cont#25:
    arm64.cmp x0, 0
    arm64.b.eq tryok#23
  tryerr#24:
    arm64.movImm x20, 0
    arm64.b __il_cont#21
  tryok#23:
    arm64.movImm x0, 0
    arm64.movRegReg x20, x1
  __il_cont#21:
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
    arm64.leaRdata x0, __str_blob_197  ; "panic at Subprocess.maxon:1429: StreamingSubprocess.release: the stdout reader is still out; reclaimStdout first\x0a"
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
    arm64.loadRegBaseDisp.byte x0, [x19 + 24]
    arm64.cbz x0, ifcont#18
  stderrReaderStillOut:
    arm64.leaRdata x0, __str_blob_198  ; "panic at Subprocess.maxon:1433: StreamingSubprocess.release: the stderr reader is still out; reclaimStderr first\x0a"
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
  ifcont#18:
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
  __im_slow#357:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#355
    arm64.b tryok#354
  __im_slow#148:
    arm64.loadRegSlot x0, slot0
    arm64.movRegReg x1, x22
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.b __im_cont
  tryerr#48:
    arm64.leaRdata x0, __str_blob_194  ; "panic at Subprocess.maxon:1074: StreamingSubprocess.spawn: empty path is the sentinel for 'inherit cwd'\x0a"
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
  __rc_panic#333:
    arm64.leaRdata x0, __str_blob_248  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  __rc_panic#137:
    arm64.leaRdata x0, __str_blob_325  ; "panic at Subprocess.maxon:721: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  tryerr#371:
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
  tryerr#351:
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
  __rc_panic#196:
    arm64.leaRdata x0, __str_blob_257  ; "panic at FilePath.maxon:335: Range check failed: value outside typealias 'BytePos'\x0a"
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
  tryerr#145:
    arm64.leaRdata x0, __str_blob_175  ; "panic at Subprocess.maxon:692: buildArgvBlob: args.get(i) out of bounds\x0a"
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
  __rc_panic#98:
    arm64.leaRdata x0, __str_blob_324  ; "panic at Subprocess.maxon:215: Range check failed: value outside typealias 'Count'\x0a"
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
  tryerr#79:
    arm64.leaRdata x0, __str_blob_193  ; "panic at Subprocess.maxon:957: Subprocess.spawnFailure: the runtime recorded a spawn step this library does not know\x0a"
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
  __rc_panic#73:
    arm64.leaRdata x0, __str_blob_327  ; "panic at Subprocess.maxon:1113: Range check failed: value outside typealias 'StreamingHandle'\x0a"
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
  __rc_panic#39:
    arm64.leaRdata x0, __str_blob_328  ; "panic at Subprocess.maxon:1261: Range check failed: value outside typealias 'ExitInt'\x0a"
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
    arm64.leaRdata x0, __str_blob_245  ; "panic at subprocess-reap.a-failed-wait-answers-reaped-elsewhere.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    0010: e2 ff 9f d2 e2 ff bf f2 3f 00 02 eb c1 00 00 54
    0020: 21 00 80 d2 00 00 00 94 1f 08 00 f1 e0 02 00 54
    0030: 12 00 00 14 00 00 00 94 a1 0f 40 f9 00 00 01 8b
    0040: a0 13 00 f9 a0 0b 40 f9 e1 03 1f aa 00 00 00 94
    0050: 1f 08 00 f1 a0 01 00 54 00 01 00 b5 00 00 00 94
    0060: a1 13 40 f9 1f 00 01 eb ca 00 00 54 20 00 80 d2
    0070: 00 00 00 94 f4 ff ff 17 e0 03 1f aa 04 00 00 14
    0080: 40 20 80 d2 02 00 00 14 00 10 80 d2 fd 7b c3 a8
    0090: c0 03 5f d6
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
  __sched_descendants_armed@472 = i64 0
  __sched_shutdown_flag@480 = i64 0
  __sched_lock@488 = i64 0
  __sched_descendant_ledger@496 = i64 0
  __sched_lock_depth_sink@504 = i64 0
  __sched_nmspinning@512 = i64 0
  __sched_needspinning@520 = i64 0
  __sched_sysmon_event@528 = i64 0
  __sched_sysmon_wait@536 = i64 0
  __sched_timer_starts@544 = i64 0
  __io_pipe_seq@552 = i64 0
  __subp_table@560 = i64 0
  __subp_last_error@568 = i64 0
  __slab_arena_list@576 = i64 0
  __slab_arena_map_l1@584 = i64 0
  __slab_state@592 = i64 0
  __mrt_program_started@600 = i8 0
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
  __il_body#46:
    arm64.leaRdata x0, __str_rec_104  ; ""
  __il_body#325:
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#326
  tryerr#327:
    arm64.movImm x21, 0
    arm64.cbnz x1, tryerr#48
    arm64.b __il_cont#324
  tryok#326:
    arm64.movRegReg x21, x0
  __il_cont#324:
    arm64.storeSlotReg slot1, x21
  __il_body#100:
    arm64.and x0, x19, 7
    arm64.cmp x0, 1
    arm64.cset x0, eq
    arm64.storeSlotReg slot3, x0
    arm64.b.ne matcharm#105
  matcharm#101:
    arm64.sub x19, x19, 1
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
    arm64.leaRdata x1, __str_rec_177  ; "the program name"
    arm64.movRegReg x0, x19
    arm64.bl requireNulFree
    arm64.movRegReg x19, x9
    arm64.cmp x19, 0
    arm64.b.eq tryok#102
  tryerr#103:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.b __il_cont#99
  tryok#102:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.b matchcont#110
  matcharm#105:
    arm64.sub x19, x19, 2
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.leaRdata x1, __str_rec_178  ; "the program path"
    arm64.bl requireNulFree
    arm64.movRegReg x19, x9
    arm64.cmp x19, 0
    arm64.b.eq tryok#106
  tryerr#107:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x19
    arm64.b __il_cont#99
  tryok#106:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_FilePath
  matchcont#110:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.leaRdata x1, __str_rec_179  ; "the working directory"
    arm64.bl requireNulFree
    arm64.cmp x9, 0
    arm64.b.ne tryerr#112
  tryok#111:
    arm64.leaRdata x1, __layout_Array_String
    arm64.movRegReg x0, x20
    arm64.bl Array.withIterator
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr#113
    arm64.b matcharm#125
  __il_body#330:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot4, x20
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __adapt___witness_ArrayIterator_String.Iterator.current
    arm64.movRegReg x21, x0
  __il_cont#329:
    arm64.movRegReg x0, x21
    arm64.bl __str_retain
    arm64.storeSlotReg slot5, x0
    arm64.leaRdata x0, __str_rec_180  ; "argument "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x24, x0
  __il_body#331:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#333
  __il_cont#328:
    arm64.add x20, x0, 1
    arm64.cmp x20, x0
    arm64.b.lo __rc_panic#137
  __rc_ok#136:
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
    arm64.b.eq tryok#118
  tryerr#119:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  exitdrop#120:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_String
  exitdrop#121:
    arm64.loadRegSlot x0, slot5
    arm64.bl __str_decref
  exitdrop#122:
    arm64.loadRegSlot x0, slot4
    arm64.bl __destruct_ArrayIterator_String
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  exitdrop#123:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
    arm64.movRegReg x0, x20
    arm64.b __il_cont#99
  tryok#118:
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
  __il_body#335:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#353:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#357
    arm64.b __im_move
  tryerr#355:
    arm64.movImm x0, 1
    arm64.b __il_cont#352
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#354:
    arm64.movImm x0, 0
  __il_cont#352:
    arm64.cmp x0, 0
    arm64.b.ne forhdr#113
  tryok#336:
    arm64.movImm x0, 0
  forhdr#113:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#330
  forexit#117:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_String_String
  matcharm#125:
    arm64.movImm x0, 0
    arm64.b __il_cont#99
  tryerr#112:
    arm64.movRegReg x0, x9
  __il_cont#99:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#152
  tryerr#89:
    arm64.movImm x19, 0
    arm64.b __il_cont#86
  __il_body#152:
    arm64.loadRegSlot x0, slot3
    arm64.cbz x0, matcharm#157
  matcharm#153:
    arm64.loadRegSlot x0, slot2
    arm64.sub x19, x0, 1
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x20, x0
  __il_body#339:
    arm64.movRegReg x0, x19
    arm64.bl FilePath.create
    arm64.cmp x9, 0
    arm64.cset x1, ne
    arm64.b.eq tryok#340
  tryerr#341:
    arm64.movImm x21, 0
    arm64.cbz x1, __il_body#360
  tryerr#164:
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
    arm64.b __il_cont#161
  tryok#340:
    arm64.movRegReg x21, x0
  __il_body#360:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#367:
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
  __il_cont#366:
    arm64.movImm x23, 0
  whilehdr#361:
    arm64.cmp x23, x22
    arm64.movImm x0, 0
    arm64.b.hs scmerge#363
  andrhs#362:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#369:
    arm64.movRegReg x1, x23
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#371
  __il_cont#368:
    arm64.cmp x0, 47
    arm64.cset x0, eq
  scmerge#363:
    arm64.cbz x0, __il_cont#359
  leadingSeparators:
    arm64.add x23, x23, 1
    arm64.b whilehdr#361
  __il_cont#359:
    arm64.cmp x23, 0
    arm64.movImm x22, 1
    arm64.b.hi critsplit#400
  __il_body#187:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#345:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont#344:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#189
  empty:
    arm64.movImm x22, 0
    arm64.b __il_cont#186
  ifcont#189:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#347:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.b whilehdr#190
  scan:
    arm64.sub x22, x0, 1
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#349:
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr#351
  __il_cont#348:
    arm64.cmp x0, 47
    arm64.b.ne ifcont#194
  isSep:
    arm64.add x23, x22, 1
    arm64.cmp x23, x22
    arm64.b.lo __rc_panic#196
    arm64.b __rc_ok#195
  ifcont#194:
    arm64.movRegReg x0, x22
  whilehdr#190:
    arm64.cmp x0, 0
    arm64.b.hi scan
  whileexit:
    arm64.movImm x22, 0
    arm64.b __il_cont#186
  __rc_ok#195:
    arm64.movRegReg x22, x23
  __il_cont#186:
    arm64.cmp x22, 0
    arm64.b.ne ifcont#182
  noSep:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
    arm64.b __il_cont#179
  ifcont#182:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
  __il_body#343:
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
  __il_cont#342:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.bl String.addressableBytes
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __managed_slice
    arm64.movRegReg x22, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#183
  tryerr#184:
    arm64.leaRdata x0, __str_blob_102  ; "panic at FilePath.maxon:352: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
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
  tryok#183:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
    arm64.movRegReg x0, x22
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitterminal#185:
    arm64.movRegReg x22, x23
  __il_cont#179:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.movRegReg x0, x22
    arm64.bl __str_eq
    arm64.eor x23, x0, 1
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.b scmerge#166
  critsplit#400:
    arm64.movRegReg x23, x22
  scmerge#166:
    arm64.cbz x23, ternaryfalse#168
  ternarytrue#167:
    arm64.loadRegSlot x1, slot1
    arm64.movRegReg x0, x21
    arm64.bl launchPathInWorkingDirectory
    arm64.movRegReg x22, x0
    arm64.b matchcont#169
  ternaryfalse#168:
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x22, x0
  matchcont#169:
    arm64.leaRdata x24, __str_rec_95  ; "/"
    arm64.cbz x23, ifcont#172
  isAPath:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x22
    arm64.b exitterminal#171
  ifcont#172:
    arm64.movRegReg x0, x22
    arm64.bl String.cstr
    arm64.bl __gt_subp_resolve_path
    arm64.bl String.init
    arm64.movRegReg x23, x0
  __il_body#379:
    arm64.loadRegBaseDisp.word64 x2, [x23 + 8]
  __il_body#375:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x23
    arm64.movRegReg x3, x24
    arm64.bl findIn
    arm64.cmp x9, 0
    arm64.b.eq tryok#376
  tryerr#377:
    arm64.movImm x0, 0
    arm64.b __il_cont#372
  tryok#376:
    arm64.movImm x0, 1
  __il_cont#372:
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#177
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
  exitdrop#175:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#176:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
  exitterminal#174:
    arm64.movImm x19, 0
    arm64.movRegReg x21, x24
    arm64.b __il_cont#161
  ifcont#177:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
  exitdrop#178:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x23
  exitterminal#171:
    arm64.movImm x19, 0
    arm64.movRegReg x21, x19
    arm64.movRegReg x19, x0
  __il_cont#161:
    arm64.cmp x21, 0
    arm64.b.eq tryok#154
  tryerr#155:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  exitterminal#156:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x21
    arm64.b __il_cont#151
  tryok#154:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.b matchcont#160
  matcharm#157:
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
  matchcont#160:
    arm64.movImm x1, 0
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x1
  __il_cont#151:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#139
  tryerr#91:
    arm64.movImm x19, 0
    arm64.b __il_cont#86
  __il_body#139:
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
    arm64.b forhdr#140
  arg:
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x22, x0
    arm64.b.hs __im_slow#148
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
    arm64.cbz x0, critsplit#395
  __im_empty:
    arm64.cmp x1, 0
    arm64.b.eq __im_slow#148
  critsplit#396:
    arm64.movRegReg x9, x2
    arm64.b __im_cont
  critsplit#395:
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.cmp x9, 0
    arm64.b.ne tryerr#145
  tryok#144:
    arm64.movRegReg x0, x20
    arm64.bl appendNulTerminated
  forstep:
    arm64.add x22, x22, 1
  forhdr#140:
    arm64.cmp x22, x21
    arm64.b.lo arg
  forexit#143:
    arm64.movRegReg x0, x20
    arm64.bl String.from
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#138:
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.add x20, x0, 1
    arm64.cmp x20, x0
    arm64.b.lo __rc_panic#98
  __il_body#198:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
  __il_body#220:
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
  __il_cont#219:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x24
    arm64.bl SpawnEnvironment.create
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  tryok#92:
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
  exitterminal#96:
    arm64.movImm x0, 0
    arm64.movRegReg x19, x22
  __il_cont#86:
    arm64.cmp x0, 0
    arm64.b.eq ternaryfalse#66
  tryerr#64:
    arm64.movImm x1, 0
    arm64.b __il_cont#61
  ternaryfalse#66:
    arm64.movImm x20, 0
  matchcont#67:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.bl String.addressableBytes
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x22, [x19 + 16]
  __il_body#75:
    arm64.loadRegSlot x0, slot1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x23, x0
  __il_cont#74:
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
    arm64.b.ne ifcont#70
  spawnFailed:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 0]
  __il_body#77:
    arm64.bl __gt_subp_last_error_code
    arm64.asr x0, x0, 32
    arm64.bl __enum_from_raw_SpawnPreparation
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#79
  tryok#78:
    arm64.bl lastOsErrorCode
    arm64.cmp x0, 2
    arm64.movImm x1, 1
    arm64.b.eq critsplit#392
  orrhs:
    arm64.cmp x0, 3
    arm64.cset x0, eq
    arm64.b scmerge#81
  critsplit#392:
    arm64.movRegReg x0, x1
  scmerge#81:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#393
  andrhs#82:
    arm64.cmp x21, 0
    arm64.cset x0, eq
    arm64.b scmerge#83
  critsplit#393:
    arm64.movRegReg x0, x1
  scmerge#83:
    arm64.cbz x0, ifcont#85
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
    arm64.b __il_cont#76
  ifcont#85:
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
  __il_cont#76:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_SpawnOperands
  exitterminal#69:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x21
    arm64.b __il_cont#61
  ifcont#70:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#73
  __rc_ok#72:
    arm64.movImm x21, 0
    arm64.movImm x22, 0
    arm64.movImm x23, 0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.storeBaseDispReg.word64 [x24 + 0], x20
    arm64.storeBaseDispReg.byte [x24 + 8], x21
    arm64.storeBaseDispReg.byte [x24 + 16], x22
    arm64.storeBaseDispReg.byte [x24 + 24], x23
    arm64.movRegReg x0, x19
    arm64.bl __destruct_SpawnOperands
  exitterminal#71:
    arm64.movImm x0, 0
    arm64.movRegReg x1, x24
  __il_cont#61:
    arm64.cmp x0, 0
    arm64.b.eq tryok#59
  tryerr#60:
    arm64.movImm x1, 0
    arm64.b __il_cont#57
  tryok#59:
    arm64.movImm x0, 0
  __il_cont#57:
    arm64.cmp x0, 0
    arm64.b.eq tryok#55
  tryerr#56:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x0
    arm64.b __il_cont#53
  tryok#55:
    arm64.movImm x19, 0
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x1
  __il_cont#53:
    arm64.cmp x20, 0
    arm64.b.eq tryok#49
  tryerr#50:
    arm64.loadRegSlot x0, slot1
    arm64.bl __destruct_FilePath
  exitterminal#51:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.b __il_cont#45
  tryok#49:
    arm64.loadRegSlot x0, slot1
    arm64.bl __destruct_FilePath
  exitterminal#52:
    arm64.movImm x0, 0
  __il_cont#45:
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
  __il_body#22:
    arm64.movImm x20, 0
  __il_body#42:
    arm64.loadRegBaseDisp.byte x0, [x19 + 8]
    arm64.cbz x0, ifcont#44
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
    arm64.b __il_cont#41
  ifcont#44:
    arm64.movImm x0, 0
  __il_cont#41:
    arm64.cmp x0, 0
    arm64.b.eq tryok#27
  tryerr#28:
    arm64.movImm x1, 0
    arm64.b __il_cont#25
  tryok#27:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.movRegReg x1, x20
    arm64.bl __gt_subp_wait_exit
    arm64.cmp x0, -2
    arm64.b.ne ifcont#30
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
    arm64.b __il_cont#25
  ifcont#30:
    arm64.cmp x0, -4
    arm64.b.ne ifcont#32
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
    arm64.b __il_cont#25
  ifcont#32:
    arm64.cmp x0, 0
    arm64.b.ge __rc_chk#40
  failed:
    arm64.cmp x0, -1
    arm64.b.ne ternaryfalse#35
  ternarytrue#34:
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
    arm64.b matchcont#36
  ternaryfalse#35:
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
  matchcont#36:
    arm64.movImm x1, 0
    arm64.b __il_cont#25
  __rc_chk#40:
    arm64.movImm x1, 4294967295
    arm64.cmp x0, x1
    arm64.b.gt __rc_panic#39
  __rc_ok#38:
    arm64.movImm x1, 0
    arm64.xchgRegReg x1, x0
  __il_cont#25:
    arm64.cmp x0, 0
    arm64.b.eq tryok#23
  tryerr#24:
    arm64.movImm x20, 0
    arm64.b __il_cont#21
  tryok#23:
    arm64.movImm x0, 0
    arm64.movRegReg x20, x1
  __il_cont#21:
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
    arm64.leaRdata x0, __str_blob_197  ; "panic at Subprocess.maxon:1429: StreamingSubprocess.release: the stdout reader is still out; reclaimStdout first\x0a"
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
    arm64.loadRegBaseDisp.byte x0, [x19 + 24]
    arm64.cbz x0, ifcont#18
  stderrReaderStillOut:
    arm64.leaRdata x0, __str_blob_198  ; "panic at Subprocess.maxon:1433: StreamingSubprocess.release: the stderr reader is still out; reclaimStderr first\x0a"
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
  ifcont#18:
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
  __im_slow#357:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#355
    arm64.b tryok#354
  __im_slow#148:
    arm64.loadRegSlot x0, slot0
    arm64.movRegReg x1, x22
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.b __im_cont
  tryerr#48:
    arm64.leaRdata x0, __str_blob_194  ; "panic at Subprocess.maxon:1074: StreamingSubprocess.spawn: empty path is the sentinel for 'inherit cwd'\x0a"
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
  __rc_panic#333:
    arm64.leaRdata x0, __str_blob_248  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  __rc_panic#137:
    arm64.leaRdata x0, __str_blob_325  ; "panic at Subprocess.maxon:721: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  tryerr#371:
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
  tryerr#351:
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
  __rc_panic#196:
    arm64.leaRdata x0, __str_blob_257  ; "panic at FilePath.maxon:335: Range check failed: value outside typealias 'BytePos'\x0a"
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
  tryerr#145:
    arm64.leaRdata x0, __str_blob_175  ; "panic at Subprocess.maxon:692: buildArgvBlob: args.get(i) out of bounds\x0a"
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
  __rc_panic#98:
    arm64.leaRdata x0, __str_blob_324  ; "panic at Subprocess.maxon:215: Range check failed: value outside typealias 'Count'\x0a"
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
  tryerr#79:
    arm64.leaRdata x0, __str_blob_193  ; "panic at Subprocess.maxon:957: Subprocess.spawnFailure: the runtime recorded a spawn step this library does not know\x0a"
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
  __rc_panic#73:
    arm64.leaRdata x0, __str_blob_327  ; "panic at Subprocess.maxon:1113: Range check failed: value outside typealias 'StreamingHandle'\x0a"
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
  __rc_panic#39:
    arm64.leaRdata x0, __str_blob_328  ; "panic at Subprocess.maxon:1261: Range check failed: value outside typealias 'ExitInt'\x0a"
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
    arm64.leaRdata x0, __str_blob_245  ; "panic at subprocess-reap.a-failed-wait-answers-reaped-elsewhere.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    0010: e2 ff 9f d2 e2 ff bf f2 3f 00 02 eb c1 00 00 54
    0020: 21 00 80 d2 00 00 00 94 1f 08 00 f1 e0 02 00 54
    0030: 12 00 00 14 00 00 00 94 a1 0f 40 f9 00 00 01 8b
    0040: a0 13 00 f9 a0 0b 40 f9 e1 03 1f aa 00 00 00 94
    0050: 1f 08 00 f1 a0 01 00 54 00 01 00 b5 00 00 00 94
    0060: a1 13 40 f9 1f 00 01 eb ca 00 00 54 20 00 80 d2
    0070: 00 00 00 94 f4 ff ff 17 e0 03 1f aa 04 00 00 14
    0080: 40 20 80 d2 02 00 00 14 00 10 80 d2 fd 7b c3 a8
    0090: c0 03 5f d6
}
```
