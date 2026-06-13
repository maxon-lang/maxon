---
feature: mm-ladder-containers
status: selfhosted
keywords: [refcount, ownership, leak, memory, destructor, container, array, map]
category: memory
---
# Memory-Management Ladder — Containers (mutation & ownership transfer)

## Documentation

A continuation of [the core MM ladder](mm-ladder.md), focused on CONTAINER mutation and element-ownership
transfer — the behaviors past the basic element-walk (`mm-ladder.md` rungs 09–16 cover create / push / get
/ clear / set / nested element-walk). Each rung here isolates ONE container-mutation ownership behavior:
how a managed element's +1 moves between a slot and the caller across `remove` / `pop` / re-`insert` /
slice / iteration, and how the container's destructor accounts for partially-consumed storage.

**Oracles** (same as the core ladder): exit code, leak gate (`mmAllocCount` delta → exit 101),
`<!-- MmTrace -->` + a `stderr` block (the exact alloc/incref/decref/free tree — the strongest oracle;
the mm-trace runtime now PANICS on a `(null)` / garbage tag, so a use-after-free that reads a freed header
fails loudly instead of silently rendering `(null)`), and `RequiredIR:x64-windows` pinned via
`--update-required`. ALWAYS hand-review a regenerated trace before locking — the leak gate cannot see a
UAF that reads freed-not-yet-reused memory; the mm-trace can.

**Workflow.** Author `disabled-test`, enable ONE at a time, drive to exit-correct + leak-free + a
hand-verified trace, then `--update-required` to pin. A passing rung is a permanent regression test —
never weaken it to make a later change pass; fix the change. Run via
`maxon-selfhosted.exe spec-test --filter=mm-ladder-containers`.

## Tests

<!-- test: rung-17-struct-array-remove-transfer -->
<!-- MmTrace -->
`remove(idx)` lifts a managed element OUT of the array and TRANSFERS ownership to the caller — the slot
relinquishes its +1, the returned value adopts it. `takeFirst` removes element 0 and returns its value;
the removed `Box` is freed exactly once at the result's last use (its single +1, transferred from the
slot, consumed). The OTHER (still-in-array) element is freed by the destructor walk at scope exit. Proves
the remove TRANSFER (vs the rung-11 get BORROW, where the slot keeps its ref and the caller increfs to
share). Transfer-returning container methods stay non-inlined so the call-site classifies the result as
owning the transferred +1 — inlining would lose that and leak the removed element.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function takeFirst(xs BoxArray) returns Integer throws ArrayError
	let b = try xs.remove(0) otherwise throw ArrayError.indexOutOfBounds
	return b.value
end 'takeFirst'

function main() returns ExitCode
	var xs = BoxArray.create()
	xs.push(Box.create(7))
	xs.push(Box.create(9))
	return try takeFirst(xs) otherwise 0
end 'main'
```
```exitcode
7
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
mm_decref Box #3 rc=0 [takeFirst]
  mm_free Box #3
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref Box #5 rc=0 [main]
      mm_free Box #5
    mm_decref <raw> #4 rc=0 [main]
      mm_free <raw> #4
    mm_free __ManagedMemory #2
  mm_free Array #1
```
```RequiredIR:x64-windows
module {
  func @takeFirst(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=32
    x64.mov r12, rcx
    x64.lea r13, [rip+__mtstr_scope_takeFirst]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.lea rax, [rip+__layout_Array_Box]
    x64.xor r13d, r13d
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.remove
    x64.mov r12, r8
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.call mm_scope_pop
    x64.mov edx, 1
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  try_0.merge:
    x64.mov r14, [r12+0] (8b)
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8, r14
    x64.mov rdx, r13
    x64.epilogue
    x64.ret
  }
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
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.mov rcx, r14
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
    x64.mov rcx, r12
    x64.call takeFirst
    x64.mov r13, r8
    x64.mov r14, rdx
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.test r14, r14
    x64.je __phi_trampoline_0_0
  try_0.otherwise:
    x64.xor r8d, r8d
    x64.mov r12, r8
  try_0.merge:
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_04c454e3078f1164]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  __phi_trampoline_0_0:
    x64.mov r12, r13
    x64.jmp try_0.merge
  }
}

```

<!-- test: rung-18-struct-array-pop-transfer -->
<!-- MmTrace -->
`pop()` is the LIFO sibling of `remove`: it lifts the LAST managed element out and TRANSFERS ownership to
the caller (same transfer accounting as remove, different access pattern). `takeLast` pops the last `Box`
and returns its value; the popped `Box` is freed exactly once at the result's last use, the remaining
element is freed by the destructor walk at scope exit. Confirms the transfer rule covers `pop` as well as
`remove` (both stay non-inlined so the call site owns the transferred +1).
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function takeLast(xs BoxArray) returns Integer throws ArrayError
	let b = try xs.pop() otherwise throw ArrayError.indexOutOfBounds
	return b.value
end 'takeLast'

function main() returns ExitCode
	var xs = BoxArray.create()
	xs.push(Box.create(7))
	xs.push(Box.create(9))
	return try takeLast(xs) otherwise 0
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
mm_decref Box #5 rc=0 [takeLast]
  mm_free Box #5
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref Box #3 rc=0 [main]
      mm_free Box #3
    mm_decref <raw> #4 rc=0 [main]
      mm_free <raw> #4
    mm_free __ManagedMemory #2
  mm_free Array #1
```
```RequiredIR:x64-windows
module {
  func @takeLast(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=32
    x64.mov r12, rcx
    x64.lea r13, [rip+__mtstr_scope_takeLast]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.call Array.pop
    x64.mov r12, r8
    x64.xor r13d, r13d
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.call mm_scope_pop
    x64.mov edx, 1
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  try_0.merge:
    x64.mov r14, [r12+0] (8b)
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8, r14
    x64.mov rdx, r13
    x64.epilogue
    x64.ret
  }
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
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.mov rcx, r14
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
    x64.mov rcx, r12
    x64.call takeLast
    x64.mov r13, r8
    x64.mov r14, rdx
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.test r14, r14
    x64.je __phi_trampoline_0_0
  try_0.otherwise:
    x64.xor r8d, r8d
    x64.mov r12, r8
  try_0.merge:
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_619486479506635f]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  __phi_trampoline_0_0:
    x64.mov r12, r13
    x64.jmp try_0.merge
  }
}

```

<!-- test: rung-19-struct-array-slice -->
<!-- MmTrace -->
`slice(start, endIndex)` builds a NEW array over a byte-COPY of the element range. The slice owns its own
backing record + buffer (freed when it drops); it does NOT own the elements (a shallow pointer copy —
`element_destroy=0`), so the PARENT array still frees them on its own walk (no double-free). Two distinct
managed allocations must be accounted: (1) the sliced record returned by `managed.slice()` is rc=1 (the
new Array's `managed` field is its lone owner), and (2) the record `synthesizeManagedFieldInits` pre-built
for the slice's `Self{managed: …}` is freed before the real one overwrites it (the synthesized-backing
decref-old — else it leaks). Every allocation freed exactly once.
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
	let sub = try xs.slice(1, endIndex: 3) otherwise return 1
	return sub.count()
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
mm_alloc __ManagedMemory #7 size=48 [main]
mm_incref __ManagedMemory #7 rc=1 [main]
mm_alloc <raw> #8 size=16 [main]
mm_incref <raw> #8 rc=1 [main]
mm_incref Box #5 rc=2 [main]
mm_incref Box #6 rc=2 [main]
mm_alloc Array #9 size=8 [main]
mm_incref Array #9 rc=1 [main]
mm_alloc __ManagedMemory #10 size=48 [main]
mm_incref __ManagedMemory #10 rc=1 [main]
mm_decref __ManagedMemory #10 rc=0 [main]
  mm_free __ManagedMemory #10
mm_move Array #9 -> return [main]
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref Box #3 rc=0 [main]
      mm_free Box #3
    mm_decref Box #5 rc=1 [main]
    mm_decref Box #6 rc=1 [main]
    mm_decref <raw> #4 rc=0 [main]
      mm_free <raw> #4
    mm_free __ManagedMemory #2
  mm_free Array #1
mm_decref Array #9 rc=0 [main]
  mm_decref __ManagedMemory #7 rc=0 [main]
    mm_decref Box #5 rc=0 [main]
      mm_free Box #5
    mm_decref Box #6 rc=0 [main]
      mm_free Box #6
    mm_decref <raw> #8 rc=0 [main]
      mm_free <raw> #8
    mm_free __ManagedMemory #7
  mm_free Array #9
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
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.mov rcx, r14
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
    x64.lea r9, [rip+__layout_Array_Box]
    x64.mov eax, 3
    x64.mov edx, 1
    x64.mov rcx, r12
    x64.call Array.slice
    x64.mov r13, r8
    x64.mov r14, rdx
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.test r14, r14
    x64.je try_0.merge
  try_0.otherwise:
    x64.call mm_scope_pop
    x64.mov r8d, 1
    x64.epilogue
    x64.ret
  try_0.merge:
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
    x64.lea r12, [rip+__panic_msg_5bc80502e525d629]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-20-struct-array-drain-loop -->
<!-- MmTrace -->
Drains an array by `remove(0)` in a loop until empty — exercises the element-TRANSFER plus the tail-shift
repeatedly under loop control. Each iteration lifts element 0 out (its +1 transfers to `b`), sums its
value, and frees it at `b`'s last use; the array shrinks each pass. After the loop the now-empty array's
record + buffer are freed at scope exit (the element-walk finds length 0, no double-free). Three `Box`es,
each removed and freed exactly once; no element survives, no leak. Stresses the transfer accounting across
repeated loop iterations (vs the single `remove` of rung-17).
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
	var total = 0
	while xs.count() > 0 'drain'
		let b = try xs.remove(0) otherwise return 99
		total = total + b.value
	end 'drain'
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
mm_decref Box #3 rc=0 [main]
  mm_free Box #3
mm_decref Box #5 rc=0 [main]
  mm_free Box #5
mm_decref Box #6 rc=0 [main]
  mm_free Box #6
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
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
    x64.xor r13d, r13d
    x64.mov r14, r13
  drain_0.header:
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.call Array.count
    x64.test r8, r8
    x64.jle drain_0.exit
  drain_0:
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.remove
    x64.mov r15, r8
    x64.test rdx, rdx
    x64.je try_0.merge
    x64.jmp try_0.otherwise
  drain_0.exit:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r14, r8
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  try_0.otherwise:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 99
    x64.epilogue
    x64.ret
  try_0.merge:
    x64.mov r8, [r15+0] (8b)
    x64.mov [rbp+-8], r8
    x64.mov rcx, r15
    x64.call __mm_decref_maybenull_helper
    x64.add r14, [rbp+-8]
    x64.jmp drain_0.header
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_6f9dc238c5955a7d]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r14
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-21-struct-array-for-in -->
<!-- MmTrace -->
`for b in xs` over an array of MANAGED elements iterates a cursor that BORROWS the backing buffer. The
iterator retains the source `__ManagedMemory` for the loop's duration (the cursor's source-retain: incref
at create, the iterator's destructor decrefs at teardown), so the buffer stays valid while the loop reads
each element's field. The loop body BORROWS each `Box` (reads `.value`) without taking ownership; no
per-element incref/decref leaks. After the loop the iterator frees (releasing the source retain), then the
array drops and the element-walk frees each `Box` + the record + buffer. Combines the cursor borrow
(rung's source-retain — see the for-in fix) with the managed-element walk; every allocation freed once.
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
mm_incref Box #5 rc=2
mm_decref Box #5 rc=1
mm_incref Box #6 rc=2
mm_decref Box #6 rc=1
mm_decref ArrayIterator #8 rc=0
  mm_decref <raw> #7 rc=0
    mm_decref __ManagedMemory #2 rc=1
    mm_free <raw> #7
  mm_free ArrayIterator #8
mm_decref Array #1 rc=0
  mm_decref __ManagedMemory #2 rc=0
    mm_decref Box #3 rc=0
      mm_free Box #3
    mm_decref Box #5 rc=0
      mm_free Box #5
    mm_decref Box #6 rc=0
      mm_free Box #6
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
    x64.mov r8d, 9
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
    x64.lea r12, [rip+__panic_msg_13bab5c296270099]
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

<!-- test: rung-22-struct-array-literal -->
<!-- MmTrace -->
An ARRAY LITERAL of managed elements (`[Box.create(7), Box.create(9), Box.create(11)]`) lowers to
`__ManagedMemory.create(N, 8)` + per-element `set` + `Array.init(record)` (a transparent wrapper). The
literal's backing record must (1) be rc=1 — its owning Array via the transparent-wrap is the lone owner —
and (2) have `element_destroy` stamped (`&__mm_decref`, the element `Box` is a managed pointer), so the
destructor walk frees each element. Every allocation — the record, its buffer, and all three `Box`es —
freed exactly once when the array drops. Proves the literal-construction path matches the create+push path
(rungs 09-10) for element ownership.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	let xs = [Box.create(7), Box.create(9), Box.create(11)]
	return xs.count()
end 'main'
```
```exitcode
3
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_incref Box #1 rc=1 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_alloc Box #2 size=8 [Box.create]
mm_incref Box #2 rc=1 [Box.create]
mm_move Box #2 -> return [Box.create]
mm_alloc Box #3 size=8 [Box.create]
mm_incref Box #3 rc=1 [Box.create]
mm_move Box #3 -> return [Box.create]
mm_alloc __ManagedMemory #4 size=48 [main]
mm_incref __ManagedMemory #4 rc=1 [main]
mm_alloc <raw> #5 size=24 [main]
mm_incref <raw> #5 rc=1 [main]
mm_move Box #1 -> container
mm_move Box #2 -> container
mm_move Box #3 -> container
mm_move __ManagedMemory #4 -> container
mm_alloc Array #6 size=8
mm_incref Array #6 rc=1
mm_alloc __ManagedMemory #7 size=48
mm_incref __ManagedMemory #7 rc=1
mm_decref __ManagedMemory #7 rc=0
  mm_free __ManagedMemory #7
mm_move Array #6 -> return
mm_decref Array #6 rc=0
  mm_decref __ManagedMemory #4 rc=0
    mm_decref Box #1 rc=0
      mm_free Box #1
    mm_decref Box #2 rc=0
      mm_free Box #2
    mm_decref Box #3 rc=0
      mm_free Box #3
    mm_decref <raw> #5 rc=0
      mm_free <raw> #5
    mm_free __ManagedMemory #4
  mm_free Array #6
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=48
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__mtstr_scope_Box_create]
    x64.mov r14d, 60
    x64.xor r15d, r15d
    x64.mov r8d, 8
    x64.mov r8d, 7
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rdx, r15
    x64.mov rax, r14
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
    x64.mov r8, 7
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.lea r13, [rip+__mtstr_scope_Box_create]
    x64.mov r14d, 60
    x64.xor r15d, r15d
    x64.mov r8d, 8
    x64.mov r8d, 9
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rdx, r15
    x64.mov rax, r14
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 9
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 11
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-8]
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8, 11
    x64.mov [r14+0], r8 (8b)
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.xor eax, eax
    x64.mov edx, 8
    x64.mov ecx, 3
    x64.call stdlib.__managed_mem_create_managed
    x64.mov r15, r8
    x64.call mm_scope_pop
    x64.mov rcx, r12
    x64.call mm_move_container
    x64.xor edx, edx
    x64.mov rcx, r15
    x64.mov rax, r12
    x64.call stdlib.__managed_mem_set
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.mov edx, 1
    x64.mov rcx, r15
    x64.mov rax, r13
    x64.call stdlib.__managed_mem_set
    x64.mov rcx, r14
    x64.call mm_move_container
    x64.mov edx, 2
    x64.mov rcx, r15
    x64.mov rax, r14
    x64.call stdlib.__managed_mem_set
    x64.mov edx, 3
    x64.mov rcx, r15
    x64.call stdlib.__managed_mem_set_length
    x64.lea r8, [rip+stdlib.__mm_decref]
    x64.mov [r15+40], r8 (8b)
    x64.mov rcx, r15
    x64.call mm_move_container
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.mov rcx, r15
    x64.call Array.init
    x64.mov r12, r8
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
    x64.lea r12, [rip+__panic_msg_2fc3d208e149bb27]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-23-struct-array-combined-mutation -->
<!-- MmTrace -->
An INTEGRATION rung combining several container behaviors in one program: push three managed elements,
`remove(1)` the middle one (TRANSFER — its +1 moves to `removed`), then `for b in xs` over the two
survivors (BORROW each), summing the removed value plus the survivors. At scope exit the removed `Box` is
freed at its last use, the two survivors are freed by the array's element-walk, and the array/record/buffer
are freed. Three `Box`es, each freed exactly once — proving push, remove-transfer, for-in-borrow, and the
element-walk compose correctly with no double-free or leak.
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
	let removed = try xs.remove(1) otherwise return 1
	var rest = 0
	for b in xs 'sum'
		rest = rest + b.value
	end 'sum'
	return removed.value + rest
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
    mm_decref <raw> #4 rc=0
      mm_free <raw> #4
    mm_free __ManagedMemory #2
  mm_free Array #1
mm_decref Box #5 rc=0
  mm_free Box #5
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=80
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
    x64.mov r8d, 9
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
    x64.lea rax, [rip+__layout_Array_Box]
    x64.mov edx, 1
    x64.mov rcx, r12
    x64.call Array.remove
    x64.mov r13, r8
    x64.xor r14d, r14d
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 1
    x64.epilogue
    x64.ret
  try_0.merge:
    x64.lea rdx, [rip+__layout_Array_Box]
  inlined_Array.createIterator_0_0:
    x64.mov rcx, [r12+0] (8b)
    x64.call ArrayIterator.create
    x64.mov r15, r8
    x64.test rdx, rdx
    x64.je inlined_Array.createIterator_2_0
  inlined_Array.createIterator_1_0:
    x64.xor ecx, ecx
    x64.mov r15, rcx
    x64.jmp inline_cont_main_0
  inlined_Array.createIterator_2_0:
    x64.mov rcx, r15
    x64.call mm_move_phi
    x64.xor r8d, r8d
    x64.mov rdx, r8
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je sum_0
    x64.jmp __rc_edge_11_0
  inlined_ArrayIterator.advance_0_0:
    x64.mov r8, [r15+0] (8b)
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
    x64.je __phi_trampoline_12_0
  inlined_ArrayIterator.advance_1_0:
    x64.mov edx, 1
  inline_cont_main_1:
    x64.test rdx, rdx
    x64.je sum_0
    x64.jmp __rc_edge_15_0
  sum_0:
    x64.mov r8, [r15+0] (8b)
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    x64.mov r9, r8
    x64.add r9, 24
    x64.mov rsi, [r9+0] (8b)
    x64.mov r9, r8
    x64.add r9, 8
    x64.mov rdi, [r9+0] (8b)
    x64.mov [rbp+-32], rdi
    x64.mov r9, [r8+0] (8b)
    x64.test rsi, rsi
    x64.jne inlined_stdlib.__managed_mem_cursor_current_2_0
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    x64.mov ecx, 3
    x64.mov r8, [rbp+-32]
    x64.shr r8, r8, rcx
    x64.xor esi, esi
    x64.add r9, r8
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], rsi
    x64.movzx rax, byte ptr [rax+0]
    x64.mov r8, [rbp-24]
    x64.mov [rbp+-40], r8
    x64.call mm_scope_pop
    x64.mov r8d, 7
    x64.mov rcx, [rbp+-32]
    x64.and rcx, r8
    x64.mov r8d, 1
    x64.mov r9, [rbp+-40]
    x64.shr r9, r9, rcx
    x64.mov [rbp+-32], r9
    x64.mov r9, [rbp+-32]
    x64.and r9, r8
    x64.mov [rbp+-32], r9
    x64.jmp __rc_edge_17_0
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    x64.mov r8, [rbp+-32]
    x64.imul r8, rsi
    x64.add r9, r8
  inlined_stdlib.__managed_mem_load_sized_0_0:
    x64.cmp rsi, 1
    x64.jne inlined_stdlib.__managed_mem_load_sized_2_0
  inlined_stdlib.__managed_mem_load_sized_1_0:
    x64.xor r8d, r8d
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], r8
    x64.movzx rax, byte ptr [rax+0]
    x64.mov r8, [rbp-24]
    x64.jmp inline_cont_main_2
  inlined_stdlib.__managed_mem_load_sized_2_0:
    x64.cmp rsi, 2
    x64.jne inlined_stdlib.__managed_mem_load_sized_4_0
  inlined_stdlib.__managed_mem_load_sized_3_0:
    x64.movzx r8, [r9+0] (2b)
    x64.jmp inline_cont_main_2
  inlined_stdlib.__managed_mem_load_sized_4_0:
    x64.cmp rsi, 4
    x64.jne inlined_stdlib.__managed_mem_load_sized_6_0
  inlined_stdlib.__managed_mem_load_sized_5_0:
    x64.mov r8, [r9+0] (4b)
    x64.jmp inline_cont_main_2
  inlined_stdlib.__managed_mem_load_sized_6_0:
    x64.mov r8, [r9+0] (8b)
  inline_cont_main_2:
    x64.mov [rbp+-32], r8
    x64.jmp __rc_edge_27_0
  inline_cont_main_3:
    x64.mov [rbp+-32], r8
    x64.call mm_scope_pop
    x64.mov r8, [rbp+-32]
    x64.mov r9, [r8+0] (8b)
    x64.mov [rbp+-40], r9
    x64.mov rcx, [rbp+-32]
    x64.call __mm_decref_maybenull_helper
    x64.add r14, [rbp+-40]
    x64.jmp inlined_ArrayIterator.advance_0_0
  sum_0.exit:
    x64.mov r12, [r13+0] (8b)
    x64.mov rcx, r13
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r9d, 4294967295
    x64.mov r8, r12
    x64.add r8, r14
    x64.cmp r8, r9
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  __rc_edge_11_0:
    x64.mov rcx, r15
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.jmp sum_0.exit
  __rc_edge_15_0:
    x64.mov rcx, r15
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.jmp sum_0.exit
  __rc_edge_17_0:
    x64.mov rcx, [rbp+-32]
    x64.call stdlib.__mm_incref
    x64.mov r8, [rbp+-32]
    x64.mov r8, [rbp+-32]
    x64.jmp inline_cont_main_3
  __rc_edge_27_0:
    x64.mov rcx, [rbp+-32]
    x64.call stdlib.__mm_incref
    x64.mov r8, [rbp+-32]
    x64.mov r8, [rbp+-32]
    x64.jmp inline_cont_main_3
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_613888409128eb35]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  __phi_trampoline_12_0:
    x64.mov rdx, r8
    x64.jmp inline_cont_main_1
  }
}

```

<!-- test: rung-24-string-array-remove-transfer -->
<!-- MmTrace -->
`remove(0)` from an array of `String` elements — managed elements that THEMSELVES own heap. The removed
String's +1 transfers to the caller; reading its grapheme count then dropping it cascades into its own
destructor (freeing the String's backing record + buffer). The remaining String is freed by the array's
element-walk at scope exit. Combines the remove-TRANSFER (rung-17) with a managed element that owns heap
(rung-15's recursive walk): every String + its backing memory + the array storage freed exactly once.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String

function takeFirst(xs StringArray) returns Integer throws ArrayError
	let s = try xs.remove(0) otherwise throw ArrayError.indexOutOfBounds
	return s.count()
end 'takeFirst'

function main() returns ExitCode
	var xs = StringArray.create()
	xs.push("hello heap string here")
	xs.push("another heap one here")
	return try takeFirst(xs) otherwise 0
end 'main'
```
```exitcode
22
```
```stderr
mm_alloc Array #1 size=8 [main]
mm_incref Array #1 rc=1 [main]
mm_alloc __ManagedMemory #2 size=48 [main]
mm_incref __ManagedMemory #2 rc=1 [main]
mm_move Array #1 -> return [main]
mm_alloc <raw> #3 size=48 [main]
mm_incref <raw> #3 rc=1 [main]
mm_alloc String #4 size=16 [main]
mm_incref String #4 rc=1 [main]
mm_move String #4 -> container [main]
mm_alloc <raw> #5 size=32 [main]
mm_incref <raw> #5 rc=1 [main]
mm_alloc <raw> #6 size=48 [main]
mm_incref <raw> #6 rc=1 [main]
mm_alloc String #7 size=16 [main]
mm_incref String #7 rc=1 [main]
mm_move String #7 -> container [main]
mm_decref String #4 rc=0 [takeFirst]
  mm_decref <raw> #3 rc=0 [takeFirst]
    mm_free <raw> #3
  mm_free String #4
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref String #7 rc=0 [main]
      mm_decref <raw> #6 rc=0 [main]
        mm_free <raw> #6
      mm_free String #7
    mm_decref <raw> #5 rc=0 [main]
      mm_free <raw> #5
    mm_free __ManagedMemory #2
  mm_free Array #1
```
```RequiredIR:x64-windows
module {
  func @takeFirst(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=32
    x64.mov r12, rcx
    x64.lea r13, [rip+__mtstr_scope_takeFirst]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.lea rax, [rip+__layout_Array_String]
    x64.xor r13d, r13d
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.remove
    x64.mov r12, r8
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.call mm_scope_pop
    x64.mov edx, 1
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  try_0.merge:
    x64.mov rcx, r12
    x64.call String.count
    x64.mov r14, r8
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8, r14
    x64.mov rdx, r13
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=64
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__layout_Array_String]
    x64.lea r14, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov r15d, 48
    x64.xor r8d, r8d
    x64.lea r9, [rip+__istr_0]
    x64.mov esi, 22
    x64.mov rdi, -2
    x64.mov edi, 1
    x64.mov edi, 6
    x64.lea rdi, [rip+__destruct_String]
    x64.mov [rbp+-8], rdi
    x64.mov edi, 16
    x64.mov rcx, r12
    x64.mov [rbp+-16], r8
    x64.mov [rbp+-24], r9
    x64.mov [rbp+-32], rsi
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r15
    x64.mov rdx, r14
    x64.call mrt_alloc_with_dtor
    x64.mov r13, r8
    x64.mov r8, [rbp+-16]
    x64.mov [r13+40], r8 (8b)
    x64.mov r9, [rbp+-24]
    x64.mov [r13+0], r9 (8b)
    x64.mov r9, [rbp+-32]
    x64.mov [r13+8], r9 (8b)
    x64.mov r9, -2
    x64.mov [r13+16], r9 (8b)
    x64.mov r9, 1
    x64.mov [r13+24], r9 (8b)
    x64.mov [r13+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-8]
    x64.mov rax, 6
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov [r14+0], r13 (8b)
    x64.mov r8d, 1
    x64.mov [r14+8], r8 (8b)
    x64.mov rcx, r14
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_String]
    x64.mov rcx, r12
    x64.mov rdx, r14
    x64.call Array.push
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov ecx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov r13, r8
    x64.mov r8, [rbp+-16]
    x64.mov [r13+40], r8 (8b)
    x64.lea r9, [rip+__istr_1]
    x64.mov [r13+0], r9 (8b)
    x64.mov r9d, 21
    x64.mov [r13+8], r9 (8b)
    x64.mov r9, -2
    x64.mov [r13+16], r9 (8b)
    x64.mov r9d, 1
    x64.mov [r13+24], r9 (8b)
    x64.mov [r13+32], r8 (8b)
    x64.mov eax, 6
    x64.lea rdx, [rip+__destruct_String]
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov [r14+0], r13 (8b)
    x64.mov r8d, 1
    x64.mov [r14+8], r8 (8b)
    x64.mov rcx, r14
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_String]
    x64.mov rcx, r12
    x64.mov rdx, r14
    x64.call Array.push
    x64.mov rcx, r12
    x64.call takeFirst
    x64.mov r13, r8
    x64.mov r14, rdx
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.test r14, r14
    x64.mov r8, [rbp+-16]
    x64.je try_0.ok
    x64.mov r12, r8
    x64.jmp try_0.merge
  try_0.ok:
    x64.mov r12, r13
  try_0.merge:
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_83187401d1e16ba0]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```
