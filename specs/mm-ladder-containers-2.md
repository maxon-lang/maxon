---
feature: mm-ladder-containers-2
status: selfhosted
keywords: [refcount, ownership, leak, memory, destructor, container, array, insert, pop, get]
category: memory
---
# Memory-Management Ladder — Containers, part 2 (insert / discarded-result / get-borrow / field-array, nested & reassign mutation)

## Documentation

A continuation of [the container MM ladder](mm-ladder-containers.md) — split out so neither file's
per-fragment mm-trace + RequiredIR regeneration approaches the spec runner's per-worker 60s timeout (the
same documented reason `mm-ladder-containers.md` and `mm-ladder-managed-element-containers.md` were already
split). These rungs isolate the remaining single-arm element-ownership behaviors: `insert`'s fresh-slot
move (no decref-old), a TRANSFER-returning result that is DISCARDED, and a `get` BORROW dropped mid-scope.

**Oracles** (same as the core ladder): exit code, leak gate (`mmAllocCount` delta → exit 101),
`<!-- MmTrace -->` + a `stderr` block (the exact alloc/incref/decref/free tree — the strongest oracle;
the mm-trace runtime PANICS on a `(null)` / garbage tag, so a use-after-free that reads a freed header
fails loudly), and `RequiredIR:x64-windows` pinned via `--update-required`. ALWAYS hand-review a regenerated
trace before locking — the leak gate cannot see a UAF that reads freed-not-yet-reused memory; the mm-trace can.

**Workflow.** Author `disabled-test`, enable ONE at a time, drive to exit-correct + leak-free + a
hand-verified trace, then `--update-required` to pin. A passing rung is a permanent regression test —
never weaken it to make a later change pass; fix the change. Run via
`maxon-selfhosted.exe spec-test --filter=mm-ladder-containers-2`.

## Tests

<!-- test: rung-28-struct-array-insert-shifts-and-owns -->
<!-- MmTrace -->
`insert(at, value)` shifts the tail right and takes a FRESH slot's +1 with NO decref-old — the sibling of
rung-14 `set` (in-bounds OVERWRITE: decref the old element first) and rung-17 `remove` (TRANSFER out). Here
two elements are pushed (`Box 7`, `Box 11`), then `Box 9` is inserted at index 1, sliding `Box 11` right;
no existing element is overwritten, so the inserted `Box`'s +1 simply moves into the new slot (a container
move, like `push`). All three `Box`es are then borrowed by a `for-in` sum and freed exactly once by the
array's element-walk at scope exit. Isolates the "fresh slot vs in-bounds overwrite" branch of the
element-store accounting that `set`/`push` only cover from the other side.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function main() returns ExitCode
	var xs = BoxArray.create()
	xs.push(Box.create(7))
	xs.push(Box.create(11))
	xs.insert(1, value: Box.create(9))
	var total = 0
	for b in xs 'sum'
		total = total + b.value
	end 'sum'
	return total
end 'main'
```
```exitcode
27
```
```stderr
mm_alloc Array #1 size=8 [main]
mm_incref Array #1 rc=1 [main]
mm_alloc __ManagedMemory #2 size=48 [main]
mm_incref __ManagedMemory #2 rc=1 [main]
mm_move Array #1 -> return [main]
mm_alloc Box #3 size=8 [Box.create]
mm_incref Box #3 rc=1 [Box.create]
mm_move Box #3 -> return [Box.create]
mm_move Box #3 -> container [main]
mm_alloc <raw> #4 size=32 [main]
mm_incref <raw> #4 rc=1 [main]
mm_alloc Box #5 size=8 [Box.create]
mm_incref Box #5 rc=1 [Box.create]
mm_move Box #5 -> return [Box.create]
mm_move Box #5 -> container [main]
mm_alloc Box #6 size=8 [Box.create]
mm_incref Box #6 rc=1 [Box.create]
mm_move Box #6 -> return [Box.create]
mm_move Box #6 -> container [main]
mm_alloc <raw> #7 size=40 [main]
mm_incref <raw> #7 rc=1 [main]
mm_incref __ManagedMemory #2 rc=2 [main]
mm_alloc ArrayIterator #8 size=8 [main]
mm_incref ArrayIterator #8 rc=1 [main]
mm_move ArrayIterator #8 -> return [main]
mm_move ArrayIterator #8 -> phi [main]
mm_incref Box #3 rc=2 [main]
mm_decref Box #3 rc=1
mm_incref Box #6 rc=2
mm_decref Box #6 rc=1
mm_incref Box #5 rc=2
mm_decref Box #5 rc=1
mm_decref ArrayIterator #8 rc=0
  mm_decref <raw> #7 rc=0
    mm_decref __ManagedMemory #2 rc=1
    mm_free <raw> #7
  mm_free ArrayIterator #8
mm_decref Array #1 rc=0
  mm_decref __ManagedMemory #2 rc=0
    mm_decref Box #3 rc=0
      mm_free Box #3
    mm_decref Box #6 rc=0
      mm_free Box #6
    mm_decref Box #5 rc=0
      mm_free Box #5
    mm_decref <raw> #4 rc=0
      mm_free <raw> #4
    mm_free __ManagedMemory #2
  mm_free Array #1
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=64
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__layout_Array_Box]
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-32], r8
    x64.mov r8d, 8
    x64.mov r8d, 7
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-32]
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 7
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-32], r8
    x64.mov r8d, 8
    x64.mov r8d, 11
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-32]
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 11
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.lea r13, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8d, 9
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea r9, [rip+__layout_Array_Box]
    x64.mov edx, 1
    x64.mov rcx, r12
    x64.mov rax, r13
    x64.call Array.insert
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.xor r13d, r13d
  inlined_Array.createIterator_0_0:
    x64.mov rcx, [r12+0] (8b)
    x64.call ArrayIterator.create
    x64.mov r14, r8
    x64.test rdx, rdx
    x64.je inlined_Array.createIterator_2_0
  inlined_Array.createIterator_1_0:
    x64.xor ecx, ecx
    x64.mov r14, rcx
    x64.jmp inline_cont_main_0
  inlined_Array.createIterator_2_0:
    x64.mov rcx, r14
    x64.call mm_move_phi
    x64.xor r8d, r8d
    x64.mov rdx, r8
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je sum_0
    x64.jmp __rc_edge_8_0
  inlined_ArrayIterator.advance_0_0:
    x64.mov r8, [r14+0] (8b)
    x64.mov r9, [r8+8] (8b)
    x64.mov rsi, [r8+16] (8b)
    x64.mov rdi, r9
    x64.add rdi, 1
    x64.mov rax, rdi
    x64.sub rax, r9
    x64.cmp rdi, rsi
    x64.setl rsi
    x64.mov rdi, rsi
    x64.imul rdi, rax
    x64.mov eax, 1
    x64.mov ecx, 1
    x64.sub rax, rsi
    x64.imul rax, rcx
    x64.add r9, rdi
    x64.mov [r8+8], r9 (8b)
    x64.xor r8d, r8d
    x64.test rax, rax
    x64.je __phi_trampoline_9_0
  inlined_ArrayIterator.advance_1_0:
    x64.mov edx, 1
  inline_cont_main_1:
    x64.test rdx, rdx
    x64.je sum_0
    x64.jmp __rc_edge_12_0
  sum_0:
    x64.mov r8, [r14+0] (8b)
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    x64.mov r9, r8
    x64.add r9, 24
    x64.mov rsi, [r9+0] (8b)
    x64.mov r9, r8
    x64.add r9, 8
    x64.mov r15, [r9+0] (8b)
    x64.mov r9, [r8+0] (8b)
    x64.test rsi, rsi
    x64.jne inlined_stdlib.__managed_mem_cursor_current_2_0
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    x64.mov ecx, 3
    x64.mov r8, r15
    x64.shr r8, r8, rcx
    x64.xor esi, esi
    x64.add r9, r8
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], rsi
    x64.movzx rax, byte ptr [rax+0]
    x64.mov r8, [rbp-24]
    x64.mov [rbp+-32], r8
    x64.call mm_scope_pop
    x64.mov r8d, 7
    x64.mov rcx, r15
    x64.and rcx, r8
    x64.mov r8d, 1
    x64.mov r9, [rbp+-32]
    x64.shr r9, r9, rcx
    x64.mov r15, r9
    x64.and r15, r8
    x64.jmp __rc_edge_14_0
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    x64.imul r15, rsi
    x64.add r9, r15
  inlined_stdlib.__managed_mem_load_sized_0_0:
    x64.cmp rsi, 1
    x64.jne inlined_stdlib.__managed_mem_load_sized_2_0
  inlined_stdlib.__managed_mem_load_sized_1_0:
    x64.xor r8d, r8d
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], r8
    x64.movzx rax, byte ptr [rax+0]
    x64.mov rcx, [rbp-24]
    x64.mov r15, rcx
    x64.jmp inline_cont_main_2
  inlined_stdlib.__managed_mem_load_sized_2_0:
    x64.cmp rsi, 2
    x64.jne inlined_stdlib.__managed_mem_load_sized_4_0
  inlined_stdlib.__managed_mem_load_sized_3_0:
    x64.movzx rcx, [r9+0] (2b)
    x64.mov r15, rcx
    x64.jmp inline_cont_main_2
  inlined_stdlib.__managed_mem_load_sized_4_0:
    x64.cmp rsi, 4
    x64.jne inlined_stdlib.__managed_mem_load_sized_6_0
  inlined_stdlib.__managed_mem_load_sized_5_0:
    x64.mov rcx, [r9+0] (4b)
    x64.mov r15, rcx
    x64.jmp inline_cont_main_2
  inlined_stdlib.__managed_mem_load_sized_6_0:
    x64.mov rcx, [r9+0] (8b)
    x64.mov r15, rcx
  inline_cont_main_2:
    x64.jmp __rc_edge_24_0
  inline_cont_main_3:
    x64.call mm_scope_pop
    x64.mov r8, [r15+0] (8b)
    x64.mov [rbp+-32], r8
    x64.mov rcx, r15
    x64.call __mm_decref_maybenull_helper
    x64.add r13, [rbp+-32]
    x64.jmp inlined_ArrayIterator.advance_0_0
  sum_0.exit:
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  __rc_edge_8_0:
    x64.mov rcx, r14
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov r12, r13
    x64.jmp sum_0.exit
  __rc_edge_12_0:
    x64.mov rcx, r14
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov r12, r13
    x64.jmp sum_0.exit
  __rc_edge_14_0:
    x64.mov rcx, r15
    x64.call stdlib.__mm_incref
    x64.jmp inline_cont_main_3
  __rc_edge_24_0:
    x64.mov rcx, r15
    x64.call stdlib.__mm_incref
    x64.jmp inline_cont_main_3
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_9faf191c2721188c]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  __phi_trampoline_9_0:
    x64.mov rdx, r8
    x64.jmp inline_cont_main_1
  }
}

```

<!-- test: rung-29-struct-array-pop-result-discarded -->
<!-- MmTrace -->
`pop()` TRANSFERS the lifted element's +1 to the caller (rung-18), but here the result is DISCARDED
(`_ = try xs.pop() otherwise …`, never bound). The transferred +1 still has to land somewhere — with no
binding to consume it, it must be dropped at the discard statement's end, NOT leaked to scope exit and NOT
double-freed by the array's later element-walk. Three `Box`es are pushed, one is popped-and-dropped on the
spot, and the two survivors are freed by the array's element-walk at scope exit. Isolates the
transfer-with-immediate-drop last-use shape — distinct from rung-18, where the popped value's `.value` is
read so the +1 is consumed by a live binding.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function main() returns ExitCode
	var xs = BoxArray.create()
	xs.push(Box.create(7))
	xs.push(Box.create(9))
	xs.push(Box.create(11))
	_ = try xs.pop() otherwise return 99
	return xs.count()
end 'main'
```
```exitcode
2
```
```stderr
mm_alloc Array #1 size=8 [main]
mm_incref Array #1 rc=1 [main]
mm_alloc __ManagedMemory #2 size=48 [main]
mm_incref __ManagedMemory #2 rc=1 [main]
mm_move Array #1 -> return [main]
mm_alloc Box #3 size=8 [Box.create]
mm_incref Box #3 rc=1 [Box.create]
mm_move Box #3 -> return [Box.create]
mm_move Box #3 -> container [main]
mm_alloc <raw> #4 size=32 [main]
mm_incref <raw> #4 rc=1 [main]
mm_alloc Box #5 size=8 [Box.create]
mm_incref Box #5 rc=1 [Box.create]
mm_move Box #5 -> return [Box.create]
mm_move Box #5 -> container [main]
mm_alloc Box #6 size=8 [Box.create]
mm_incref Box #6 rc=1 [Box.create]
mm_move Box #6 -> return [Box.create]
mm_move Box #6 -> container [main]
mm_decref Box #6 rc=0 [main]
  mm_free Box #6
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref Box #3 rc=0 [main]
      mm_free Box #3
    mm_decref Box #5 rc=0 [main]
      mm_free Box #5
    mm_decref <raw> #4 rc=0 [main]
      mm_free <raw> #4
    mm_free __ManagedMemory #2
  mm_free Array #1
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=48
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__layout_Array_Box]
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 7
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-8]
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 7
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 9
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-8]
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 9
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.lea r13, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8d, 11
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.call Array.pop
    x64.mov r13, r8
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 99
    x64.epilogue
    x64.ret
  try_0.merge:
    x64.mov rcx, r13
    x64.call __mm_decref_maybenull_helper
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.call Array.count
    x64.mov r13, r8
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_1f7f762130e4a118]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-30-struct-array-get-borrow-discarded -->
<!-- MmTrace -->
`get(idx)` BORROWS an element — the slot keeps its own reference and the caller INCREFS to share (rung-11),
the OPPOSITE of `remove`/`pop`'s TRANSFER (rung-17/29). Here the borrowed `Box` is read once (`first.value`)
and then goes dead while the array is STILL live and STILL owns the element. The shared +1 the `get` took is
released exactly once at the borrow's last use; the array's own reference is untouched, so the element
survives the borrow's drop and is freed — together with the other element — by the array's element-walk at
scope exit. The mid-scope incref-on-get/decref-on-drop must net to zero against the element's count: the trace
shows `Box #3` rising to rc=2 at the `get` and returning to rc=0 across the element-walk plus the borrow drop,
with NO double-free. Distinguishes the get-BORROW lifecycle (share, then release the share) from rung-29's
transfer-discard (where the popped element leaves the array entirely).
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function main() returns ExitCode
	var xs = BoxArray.create()
	xs.push(Box.create(7))
	xs.push(Box.create(9))
	let first = try xs.get(0) otherwise return 1
	let v = first.value
	return v + xs.count()
end 'main'
```
```exitcode
9
```
```stderr
mm_alloc Array #1 size=8 [main]
mm_incref Array #1 rc=1 [main]
mm_alloc __ManagedMemory #2 size=48 [main]
mm_incref __ManagedMemory #2 rc=1 [main]
mm_move Array #1 -> return [main]
mm_alloc Box #3 size=8 [Box.create]
mm_incref Box #3 rc=1 [Box.create]
mm_move Box #3 -> return [Box.create]
mm_move Box #3 -> container [main]
mm_alloc <raw> #4 size=32 [main]
mm_incref <raw> #4 rc=1 [main]
mm_alloc Box #5 size=8 [Box.create]
mm_incref Box #5 rc=1 [Box.create]
mm_move Box #5 -> return [Box.create]
mm_move Box #5 -> container [main]
mm_incref Box #3 rc=2 [main]
mm_decref Box #3 rc=1 [main]
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref Box #3 rc=0 [main]
      mm_free Box #3
    mm_decref Box #5 rc=0 [main]
      mm_free Box #5
    mm_decref <raw> #4 rc=0 [main]
      mm_free <raw> #4
    mm_free __ManagedMemory #2
  mm_free Array #1
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=48
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__layout_Array_Box]
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 7
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-8]
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 7
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.lea r13, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8d, 9
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.xor edx, edx
  inlined_Array.get_0_0:
    x64.mov rcx, [r12+0] (8b)
    x64.call stdlib.__managed_mem_get
    x64.mov r13, r8
    x64.test rdx, rdx
    x64.je inlined_Array.get_3_0
  inlined_Array.get_1_0:
    x64.mov edx, 1
    x64.xor ecx, ecx
    x64.mov r13, rcx
    x64.jmp inline_cont_main_0
  inlined_Array.get_3_0:
    x64.xor r14d, r14d
    x64.jmp __rc_edge_6_0
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r13
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 1
    x64.epilogue
    x64.ret
  try_0.merge:
    x64.mov r14, [r13+0] (8b)
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.call Array.count
    x64.mov r15, r8
    x64.mov rcx, r13
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r9d, 4294967295
    x64.mov r8, r14
    x64.add r8, r15
    x64.cmp r8, r9
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  __rc_edge_6_0:
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov rdx, r14
    x64.jmp inline_cont_main_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_71d7ab6d360ee5e1]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```



<!-- test: rung-25-struct-field-array-managed-mutation -->
<!-- MmTrace -->
A struct (`Bag`) holds an array of MANAGED elements (`items as Array with Box`), and the array is MUTATED
through the field (`bag.items.push(Box.create(…))`). When the struct drops, `__destruct_Bag` decrefs the
`items` field, whose element-walk frees each pushed `Box`, then the array's record + buffer. Combines the
struct-field → container cascade (rung-13) with the managed-element walk (rung-10) under field-path
mutation — the nested-ownership shape an IR module (a struct holding a function array of managed nodes)
relies on. Two `Box`es, each freed exactly once; no leak, no double-free.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

type Bag
	export var items as BoxArray

	static function create() returns Self
		return Self{items: BoxArray.create()}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var bag = Bag.create()
	bag.items.push(Box.create(7))
	bag.items.push(Box.create(9))
	return bag.items.count()
end 'main'
```
```exitcode
2
```
```stderr
mm_alloc Bag #1 size=8 [Bag.create]
mm_alloc Array #2 size=8 [Bag.create]
mm_incref Array #2 rc=1 [Bag.create]
mm_alloc __ManagedMemory #3 size=48 [Bag.create]
mm_incref __ManagedMemory #3 rc=1 [Bag.create]
mm_move Array #2 -> return [Bag.create]
mm_move Bag #1 -> return [Bag.create]
mm_alloc Box #4 size=8 [Box.create]
mm_incref Box #4 rc=1 [Box.create]
mm_move Box #4 -> return [Box.create]
mm_move Box #4 -> container [main]
mm_alloc <raw> #5 size=32 [main]
mm_incref <raw> #5 rc=1 [main]
mm_alloc Box #6 size=8 [Box.create]
mm_incref Box #6 rc=1 [Box.create]
mm_move Box #6 -> return [Box.create]
mm_move Box #6 -> container [main]
mm_drop Bag #1 [main]
    mm_decref Array #2 rc=0 [main]
      mm_decref __ManagedMemory #3 rc=0 [main]
        mm_decref Box #4 rc=0 [main]
          mm_free Box #4
        mm_decref Box #6 rc=0 [main]
          mm_free Box #6
        mm_decref <raw> #5 rc=0 [main]
          mm_free <raw> #5
        mm_free __ManagedMemory #3
      mm_free Array #2
    mm_free Bag #1
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=48
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__mtstr_scope_Bag_create]
    x64.mov r14d, 61
    x64.lea r15, [rip+__destruct_Bag]
    x64.mov r8d, 8
    x64.lea r8, [rip+__layout_Array_Box]
    x64.mov [rbp+-8], r8
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rdx, r15
    x64.mov rax, r14
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov rcx, [rbp+-8]
    x64.call Array.create
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r13, [r12+0] (8b)
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 7
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-8]
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8, 7
    x64.mov [r14+0], r8 (8b)
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r14
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r13
    x64.mov rdx, r14
    x64.call Array.push
    x64.mov r13, [r12+0] (8b)
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8d, 9
    x64.mov [r14+0], r8 (8b)
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r14
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r13
    x64.mov rdx, r14
    x64.call Array.push
    x64.mov rcx, [r12+0] (8b)
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.call Array.count
    x64.mov r13, r8
    x64.mov rcx, r12
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_15782e093a4408f3]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-26-nested-struct-array-of-arrays -->
<!-- MmTrace -->
A THREE-LEVEL managed nesting built by mutation: an outer array of inner arrays of `Box` (`Array with
(Array with Box)`), each inner array built by `push` and then pushed into the outer. At scope exit the
outer array's element-walk decrefs each inner array, whose own element-walk decrefs each `Box` — a
recursive cascade through all three levels. Every record, buffer, and `Box` freed exactly once, bottom-up,
with no double-free. Proves the element-walk recurses correctly when the element type is ITSELF a managed
container of managed structs (the deepest container nesting in the ladder).
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box
typealias BoxGrid = Array with BoxArray

function main() returns ExitCode
	var grid = BoxGrid.create()
	var row0 = BoxArray.create()
	row0.push(Box.create(7))
	row0.push(Box.create(9))
	grid.push(row0)
	var row1 = BoxArray.create()
	row1.push(Box.create(11))
	grid.push(row1)
	return grid.count()
end 'main'
```
```exitcode
2
```
```stderr
mm_alloc Array #1 size=8 [main]
mm_incref Array #1 rc=1 [main]
mm_alloc __ManagedMemory #2 size=48 [main]
mm_incref __ManagedMemory #2 rc=1 [main]
mm_move Array #1 -> return [main]
mm_alloc Array #3 size=8 [main]
mm_incref Array #3 rc=1 [main]
mm_alloc __ManagedMemory #4 size=48 [main]
mm_incref __ManagedMemory #4 rc=1 [main]
mm_move Array #3 -> return [main]
mm_alloc Box #5 size=8 [Box.create]
mm_incref Box #5 rc=1 [Box.create]
mm_move Box #5 -> return [Box.create]
mm_move Box #5 -> container [main]
mm_alloc <raw> #6 size=32 [main]
mm_incref <raw> #6 rc=1 [main]
mm_alloc Box #7 size=8 [Box.create]
mm_incref Box #7 rc=1 [Box.create]
mm_move Box #7 -> return [Box.create]
mm_move Box #7 -> container [main]
mm_move Array #3 -> container [main]
mm_alloc <raw> #8 size=32 [main]
mm_incref <raw> #8 rc=1 [main]
mm_alloc Array #9 size=8 [main]
mm_incref Array #9 rc=1 [main]
mm_alloc __ManagedMemory #10 size=48 [main]
mm_incref __ManagedMemory #10 rc=1 [main]
mm_move Array #9 -> return [main]
mm_alloc Box #11 size=8 [Box.create]
mm_incref Box #11 rc=1 [Box.create]
mm_move Box #11 -> return [Box.create]
mm_move Box #11 -> container [main]
mm_alloc <raw> #12 size=32 [main]
mm_incref <raw> #12 rc=1 [main]
mm_move Array #9 -> container [main]
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref Array #3 rc=0 [main]
      mm_decref __ManagedMemory #4 rc=0 [main]
        mm_decref Box #5 rc=0 [main]
          mm_free Box #5
        mm_decref Box #7 rc=0 [main]
          mm_free Box #7
        mm_decref <raw> #6 rc=0 [main]
          mm_free <raw> #6
        mm_free __ManagedMemory #4
      mm_free Array #3
    mm_decref Array #9 rc=0 [main]
      mm_decref __ManagedMemory #10 rc=0 [main]
        mm_decref Box #11 rc=0 [main]
          mm_free Box #11
        mm_decref <raw> #12 rc=0 [main]
          mm_free <raw> #12
        mm_free __ManagedMemory #10
      mm_free Array #9
    mm_decref <raw> #8 rc=0 [main]
      mm_free <raw> #8
    mm_free __ManagedMemory #2
  mm_free Array #1
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=48
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__layout_Array_BoxArray]
    x64.lea r14, [rip+__layout_Array_Box]
    x64.lea r15, [rip+__mtstr_scope_Box_create]
    x64.mov r8d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 7
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r14
    x64.call Array.create
    x64.mov r13, r8
    x64.mov rcx, r15
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rdx, [rbp+-8]
    x64.mov rax, 60
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8, 7
    x64.mov [r14+0], r8 (8b)
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r14
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.lea r15, [rip+__mtstr_scope_Box_create]
    x64.mov r8d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 9
    x64.mov rcx, r13
    x64.mov rdx, r14
    x64.call Array.push
    x64.mov rcx, r15
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rdx, [rbp+-8]
    x64.mov rax, 60
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8, 9
    x64.mov [r14+0], r8 (8b)
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r14
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r13
    x64.mov rdx, r14
    x64.call Array.push
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_BoxArray]
    x64.lea r14, [rip+__layout_Array_Box]
    x64.lea r15, [rip+__mtstr_scope_Box_create]
    x64.mov r8d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 11
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.mov rcx, r14
    x64.call Array.create
    x64.mov r13, r8
    x64.mov rcx, r15
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rdx, [rbp+-8]
    x64.mov rax, 60
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8, 11
    x64.mov [r14+0], r8 (8b)
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r14
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r13
    x64.mov rdx, r14
    x64.call Array.push
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_BoxArray]
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.lea rdx, [rip+__layout_Array_BoxArray]
    x64.mov rcx, r12
    x64.call Array.count
    x64.mov r13, r8
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_1196ed9618376cbc]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-27-struct-array-reassign-decrefs-old -->
<!-- MmTrace -->
Reassigning a `var` that holds a managed array (`xs = BoxArray.create()` over an array already holding
elements) must decref the OLD array AT the reassignment — its element-walk frees the prior elements and its
storage right there, NOT leak them to scope exit. The trace shows the first array (Box 7, 9) freed mid-
program at the reassign point; the replacement array (Box 11) then lives to scope end and is freed there.
Proves reassign-decrefs-old (rung-03's behavior for scalars) cascades correctly through a managed
container's element-walk — three `Box`es total, each freed exactly once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function main() returns ExitCode
	var xs = BoxArray.create()
	xs.push(Box.create(7))
	xs.push(Box.create(9))
	xs = BoxArray.create()
	xs.push(Box.create(11))
	return xs.count()
end 'main'
```
```exitcode
1
```
```stderr
mm_alloc Array #1 size=8 [main]
mm_incref Array #1 rc=1 [main]
mm_alloc __ManagedMemory #2 size=48 [main]
mm_incref __ManagedMemory #2 rc=1 [main]
mm_move Array #1 -> return [main]
mm_alloc Box #3 size=8 [Box.create]
mm_incref Box #3 rc=1 [Box.create]
mm_move Box #3 -> return [Box.create]
mm_move Box #3 -> container [main]
mm_alloc <raw> #4 size=32 [main]
mm_incref <raw> #4 rc=1 [main]
mm_alloc Box #5 size=8 [Box.create]
mm_incref Box #5 rc=1 [Box.create]
mm_move Box #5 -> return [Box.create]
mm_move Box #5 -> container [main]
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref Box #3 rc=0 [main]
      mm_free Box #3
    mm_decref Box #5 rc=0 [main]
      mm_free Box #5
    mm_decref <raw> #4 rc=0 [main]
      mm_free <raw> #4
    mm_free __ManagedMemory #2
  mm_free Array #1
mm_alloc Array #6 size=8 [main]
mm_incref Array #6 rc=1 [main]
mm_alloc __ManagedMemory #7 size=48 [main]
mm_incref __ManagedMemory #7 rc=1 [main]
mm_move Array #6 -> return [main]
mm_alloc Box #8 size=8 [Box.create]
mm_incref Box #8 rc=1 [Box.create]
mm_move Box #8 -> return [Box.create]
mm_move Box #8 -> container [main]
mm_alloc <raw> #9 size=32 [main]
mm_incref <raw> #9 rc=1 [main]
mm_decref Array #6 rc=0 [main]
  mm_decref __ManagedMemory #7 rc=0 [main]
    mm_decref Box #8 rc=0 [main]
      mm_free Box #8
    mm_decref <raw> #9 rc=0 [main]
      mm_free <raw> #9
    mm_free __ManagedMemory #7
  mm_free Array #6
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=48
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__layout_Array_Box]
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 7
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-8]
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 7
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 9
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-8]
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 9
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.lea rcx, [rip+__layout_Array_Box]
    x64.lea r12, [rip+__mtstr_scope_Box_create]
    x64.call Array.create
    x64.mov r13, r8
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
    x64.mov r8d, 11
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r12
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r13
    x64.mov rdx, r12
    x64.call Array.push
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.mov rcx, r13
    x64.call Array.count
    x64.mov r12, r8
    x64.mov rcx, r13
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_ff71eb69e4ef8a70]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```
