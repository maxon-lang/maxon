---
feature: mm-ladder-2
status: selfhosted
keywords: [refcount, ownership, leak, memory, destructor, lifetime]
category: memory
---
# Memory-Management Ladder (Part 2 — Containers)

## Documentation

The second half of the memory-management ladder (see `mm-ladder.md` for Part 1, rungs 00–08, and
the ladder's full rationale). This file holds the container rungs (Group 3 onward). It is split out
from Part 1 purely so each half runs as its own spec-test job — the combined 18-rung ladder exceeds
the parallel runner's per-worker timeout when every fragment is funneled into a single worker under
the slower wasm target. The two files share one ladder; keep the rung numbering contiguous across
them and never weaken a locked rung to make a later change pass.

Run with the SELF-HOSTED compiler (`maxon-selfhosted.exe spec-test --filter=mm-ladder-2`).

## Tests

### Group 3 — Containers

<!-- test: rung-09-int-array-push-get -->
<!-- MmTrace -->
An array of NON-managed elements (`Integer`) is created, pushed twice, and read. The array's heap
allocation and its backing `__ManagedMemory` buffer are both freed when the array goes out of scope in
`main`; the element slots hold plain integers so the destructor walk does no per-element decref. Isolates
the container's own two-allocation lifecycle (the `IntArray` handle + its growable backing store) before
managed-element walks are introduced.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(10)
	xs.push(20)
	return try xs.get(1) otherwise 0
end 'main'
```
```exitcode
20
```
```stderr
mm_alloc Array #1 size=8 [main]
mm_incref Array #1 rc=1 [main]
mm_alloc __ManagedMemory #2 size=48 [main]
mm_incref __ManagedMemory #2 rc=1 [main]
mm_move Array #1 -> return [main]
mm_alloc <raw> #3 size=32 [main]
mm_incref <raw> #3 rc=1 [main]
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref <raw> #3 rc=0 [main]
      mm_free <raw> #3
    mm_free __ManagedMemory #2
  mm_free Array #1
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.lea rcx, [rip+__layout_Array_Integer]
    x64.call Array.create
    x64.mov r12, r8
    x64.lea rax, [rip+__layout_Array_Integer]
    x64.mov edx, 10
    x64.mov rcx, r12
    x64.call Array.push
    x64.lea rax, [rip+__layout_Array_Integer]
    x64.mov edx, 20
    x64.mov rcx, r12
    x64.call Array.push
    x64.mov edx, 1
  inlined_Array.get_0_0:
    x64.mov rcx, [r12+0] (8b)
    x64.call stdlib.__managed_mem_get
    x64.mov r13, r8
    x64.test rdx, rdx
    x64.je inlined_Array.get_3_0
  inlined_Array.get_1_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov edx, 1
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_0
  inlined_Array.get_3_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.xor edx, edx
    x64.mov r8, r13
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je __phi_trampoline_7_0
  try_0.otherwise:
    x64.xor r8d, r8d
    x64.mov r12, r8
  try_0.merge:
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_0bbc49cb9c06d4cb]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  __phi_trampoline_7_0:
    x64.mov r12, r8
    x64.jmp try_0.merge
  }
}

```

<!-- test: rung-10-struct-array-walks-elements -->
<!-- MmTrace -->
An array of MANAGED elements (`Box` structs) is created and pushed twice, then goes out of scope in
`main` WITHOUT reading any element back. The array's destructor must WALK the backing buffer and decref
each live element — not just free the buffer — so every pushed `Box` reaches rc=0 and frees. This is the
managed-element walk: the backing `__ManagedMemory`'s `element_destroy` slot is stamped with `&__mm_decref`
(the element type is drop-tracked, read from the instance's layout descriptor at grow), so the ROOT-branch
teardown cascades into each element's own destructor. Builds on rung-09 (non-managed `Integer` elements,
which did no per-element work). Isolates the WALK alone — returning `count()` avoids the get-borrow lifetime
that a later rung covers.
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
    x64.lea r12, [rip+__panic_msg_f17dffc9800dd7d8]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-11-struct-array-get-borrow -->
<!-- MmTrace -->
A managed element is read OUT of an array (`get`) and KEPT past the array's lifetime: `first` returns
element 0, `main` binds it to `b`, then the array goes out of scope BEFORE `b` is used. The element must
survive the array's element-walk teardown — the get-result acquires its own +1 (the inserter's
`incomingOwner` acquire) so the walk's per-element decref drops it from rc=2 to rc=1 (alive), and `b`'s
own last-use decref frees it afterward. The OTHER element (never read) is freed by the walk. Proves the
get-borrow lifetime: reading a managed element shares ownership rather than handing out a dangling alias.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function first(xs BoxArray) returns Box throws ArrayError
	return try xs.get(0) otherwise throw ArrayError.indexOutOfBounds
end 'first'

function main() returns ExitCode
	var xs = BoxArray.create()
	xs.push(Box.create(7))
	xs.push(Box.create(9))
	let b = try first(xs) otherwise Box.create(0)
	return b.value
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
mm_incref Box #3 rc=2 [first]
mm_move Box #3 -> return [first]
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref Box #3 rc=1 [main]
    mm_decref Box #5 rc=0 [main]
      mm_free Box #5
    mm_decref <raw> #4 rc=0 [main]
      mm_free <raw> #4
    mm_free __ManagedMemory #2
  mm_free Array #1
mm_move Box #3 -> phi [main]
mm_decref Box #3 rc=0 [main]
  mm_free Box #3
```
```RequiredIR:x64-windows
module {
  func @first(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=32
    x64.mov r12, rcx
    x64.lea r13, [rip+__mtstr_scope_first]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.xor r13d, r13d
  inlined_Array.get_0_0:
    x64.mov rcx, [r12+0] (8b)
    x64.mov rdx, r13
    x64.call stdlib.__managed_mem_get
    x64.mov r12, r8
    x64.test rdx, rdx
    x64.je inlined_Array.get_3_0
  inlined_Array.get_1_0:
    x64.mov edx, 1
    x64.xor ecx, ecx
    x64.mov r15, rcx
    x64.jmp inline_cont_first_0
  inlined_Array.get_3_0:
    x64.xor r14d, r14d
    x64.jmp __rc_edge_6_0
  inline_cont_first_0:
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.mov rcx, r15
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov edx, 1
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  try_0.merge:
    x64.mov rcx, r15
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r8, r15
    x64.mov rdx, r13
    x64.epilogue
    x64.ret
  __rc_edge_6_0:
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
    x64.mov r15, r12
    x64.mov rdx, r14
    x64.jmp inline_cont_first_0
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
    x64.call first
    x64.mov r13, r8
    x64.mov r14, rdx
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r13
    x64.call mm_move_phi
    x64.xor r12d, r12d
    x64.test r14, r14
    x64.je __phi_trampoline_0_0
  try_0.otherwise:
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
    x64.mov [r13+0], r12 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_phi
    x64.mov rcx, r13
  try_0.merge:
    x64.mov r12, [rcx+0] (8b)
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_74869256fdf4ab8d]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  __phi_trampoline_0_0:
    x64.mov rcx, r13
    x64.jmp try_0.merge
  }
}

```

<!-- test: rung-12-struct-array-clear-decrefs-all -->
<!-- MmTrace -->
`clear()` on an array of MANAGED elements must DROP every live element before truncating to empty — not
just zero the length (which would orphan each slot's +1 and leak every element). Two `Box`es are pushed,
then `clear()` walks `[0, length)` via the stamped `element_destroy` and decrefs each (freeing both), and
sets length=0. The now-empty array, its backing record, and buffer are then freed at scope exit — and the
destructor's own element-walk finds length=0, so it does not double-free the already-dropped elements.
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
	xs.clear()
	return xs.count()
end 'main'
```
```exitcode
0
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
mm_decref Box #3 rc=0 [main]
  mm_free Box #3
mm_decref Box #5 rc=0 [main]
  mm_free Box #5
mm_decref Array #1 rc=0
  mm_decref __ManagedMemory #2 rc=0
    mm_decref <raw> #4 rc=0
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
    x64.mov r13, [r12+0] (8b)
    x64.mov rcx, r13
    x64.call stdlib.__managed_mem_walk_elements
    x64.add r13, 8
    x64.xor r8d, r8d
    x64.mov [r13+0], r8 (8b)
    x64.call mm_scope_pop
    x64.call mm_scope_pop
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
    x64.lea r12, [rip+__panic_msg_1fb4fb0c02a3cbfe]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-13-struct-field-container -->
<!-- MmTrace -->
A user struct holds a CONTAINER field (`Bag.items as Array with Integer`). Dropping the struct must
cascade into the field: the synthesized `__destruct_Bag` decrefs `items`, whose own destructor frees the
backing record and buffer. Proves the struct-field → container → record → buffer cascade composes — the
nested-ownership shape that a struct-with-array (e.g. an IR module holding a function array) relies on.
Integer elements need no per-element walk, so this isolates the field-cascade alone.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create()}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var bag = Bag.create()
	bag.items.push(10)
	bag.items.push(20)
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
mm_alloc <raw> #4 size=32 [main]
mm_incref <raw> #4 rc=1 [main]
mm_drop Bag #1 [main]
    mm_decref Array #2 rc=0 [main]
      mm_decref __ManagedMemory #3 rc=0 [main]
        mm_decref <raw> #4 rc=0 [main]
          mm_free <raw> #4
        mm_free __ManagedMemory #3
      mm_free Array #2
    mm_free Bag #1
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.lea r12, [rip+__mtstr_scope_Bag_create]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.lea rdx, [rip+__destruct_Bag]
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.lea rcx, [rip+__layout_Array_Integer]
    x64.call Array.create
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r13, [r12+0] (8b)
    x64.lea rax, [rip+__layout_Array_Integer]
    x64.mov edx, 10
    x64.mov rcx, r13
    x64.call Array.push
    x64.mov r13, [r12+0] (8b)
    x64.lea rax, [rip+__layout_Array_Integer]
    x64.mov edx, 20
    x64.mov rcx, r13
    x64.call Array.push
    x64.mov rcx, [r12+0] (8b)
    x64.lea rdx, [rip+__layout_Array_Integer]
    x64.call Array.count
    x64.mov r13, r8
    x64.mov rcx, r12
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_8adcd9a244189a61]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-14-struct-array-set-decrefs-old -->
<!-- MmTrace -->
Overwriting a LIVE managed element via `set(idx, value)` must DROP the displaced occupant — else its +1
leaks (the teardown walk only ever sees whatever the slot holds at teardown, i.e. the NEW value). One
`Box(7)` is pushed, then `set(0, Box(42))` loads the old slot occupant and decrefs it (freeing `Box #3`)
before storing the new one. The new value's +1 moves in (caller's last-use decref suppressed). At scope
exit the walk frees the surviving element. `push` (write at idx==length, a fresh slot) is correctly
excluded from the decref-old — only an in-bounds `idx < length` overwrite releases.
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
	try xs.set(0, value: Box.create(42)) otherwise return 1
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
mm_decref Box #3 rc=0 [main]
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
    x64.mov r8d, 42
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.xor r14d, r14d
  inlined_Array.set_0_0:
    x64.mov r15, [r12+0] (8b)
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.mov rcx, r15
    x64.mov rdx, r14
    x64.mov rax, r13
    x64.call stdlib.__managed_mem_set
    x64.test rdx, rdx
    x64.je inlined_Array.set_3_0
  inlined_Array.set_1_0:
    x64.mov edx, 1
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_0
  inlined_Array.set_3_0:
    x64.xor edx, edx
    x64.xor r8d, r8d
  inline_cont_main_0:
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
    x64.lea r12, [rip+__panic_msg_357303251c27237a]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-15-string-array-element-walk -->
<!-- MmTrace -->
An array of `String` elements — managed elements that THEMSELVES own heap (each String wraps a backing
`__ManagedMemory` + buffer). When the array drops, its element-walk decrefs each String, and each String's
own destructor then frees its record and buffer. Proves the element-walk composes recursively: a two-level
cascade (array → String element → String's record → buffer), every allocation freed exactly once. This is
the array-of-stdlib-managed-type case that previously leaked (the element_destroy was never stamped).
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var xs = StringArray.create()
	xs.push("hello world this is heap")
	xs.push("another heap string here")
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
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref String #4 rc=0 [main]
      mm_decref <raw> #3 rc=0 [main]
        mm_free <raw> #3
      mm_free String #4
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
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=80
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__layout_Array_String]
    x64.lea r14, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov r15d, 48
    x64.xor r8d, r8d
    x64.lea r9, [rip+__istr_0]
    x64.mov esi, 24
    x64.mov rdi, -2
    x64.mov edi, 1
    x64.xor edi, edi
    x64.mov [rbp+-8], rdi
    x64.mov edi, 6
    x64.lea rdi, [rip+__destruct_String]
    x64.mov [rbp+-16], rdi
    x64.mov edi, 16
    x64.mov rcx, r12
    x64.mov [rbp+-24], r9
    x64.mov [rbp+-32], r8
    x64.mov [rbp+-40], rsi
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r15
    x64.mov rdx, r14
    x64.call mrt_alloc_with_dtor
    x64.mov r13, r8
    x64.mov r8, [rbp+-32]
    x64.mov [r13+40], r8 (8b)
    x64.mov r8, [rbp+-24]
    x64.mov [r13+0], r8 (8b)
    x64.mov r8, [rbp+-40]
    x64.mov [r13+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r13+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r13+24], r8 (8b)
    x64.mov r8, [rbp+-8]
    x64.mov [r13+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-16]
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
    x64.xor r8d, r8d
    x64.mov [r13+40], r8 (8b)
    x64.lea r8, [rip+__istr_1]
    x64.mov [r13+0], r8 (8b)
    x64.mov r8d, 24
    x64.mov [r13+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r13+16], r8 (8b)
    x64.mov r8d, 1
    x64.mov [r13+24], r8 (8b)
    x64.xor r8d, r8d
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
    x64.lea rdx, [rip+__layout_Array_String]
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
    x64.lea r12, [rip+__panic_msg_9418fb3ad2852075]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-16-nested-array-element-walk -->
<!-- MmTrace -->
An array of ARRAYS (`Matrix = Array with IntArray`) — each element is itself a container that owns heap.
When the outer array drops, its element-walk decrefs each row array, and each row's own destructor frees
its record and buffer: a three-level cascade (Matrix → row Array → row record → row buffer), every
allocation freed exactly once. Proves the element-walk recurses through nested containers — and that the
element type, a generic-container TYPEALIAS (`IntArray = Array with Integer`, which reaches the layout
descriptor as `named("IntArray")`), is chased to its underlying `Array with Integer` so HAS_HEAP_REFS is
set and the walk fires (a bare struct check missed the alias → the rows leaked).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Matrix = Array with IntArray

function main() returns ExitCode
	var m = Matrix.create()
	var row0 = IntArray.create()
	row0.push(1)
	row0.push(2)
	m.push(row0)
	var row1 = IntArray.create()
	row1.push(3)
	m.push(row1)
	return m.count()
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
mm_alloc <raw> #5 size=32 [main]
mm_incref <raw> #5 rc=1 [main]
mm_move Array #3 -> container [main]
mm_alloc <raw> #6 size=32 [main]
mm_incref <raw> #6 rc=1 [main]
mm_alloc Array #7 size=8 [main]
mm_incref Array #7 rc=1 [main]
mm_alloc __ManagedMemory #8 size=48 [main]
mm_incref __ManagedMemory #8 rc=1 [main]
mm_move Array #7 -> return [main]
mm_alloc <raw> #9 size=32 [main]
mm_incref <raw> #9 rc=1 [main]
mm_move Array #7 -> container [main]
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref Array #3 rc=0 [main]
      mm_decref __ManagedMemory #4 rc=0 [main]
        mm_decref <raw> #5 rc=0 [main]
          mm_free <raw> #5
        mm_free __ManagedMemory #4
      mm_free Array #3
    mm_decref Array #7 rc=0 [main]
      mm_decref __ManagedMemory #8 rc=0 [main]
        mm_decref <raw> #9 rc=0 [main]
          mm_free <raw> #9
        mm_free __ManagedMemory #8
      mm_free Array #7
    mm_decref <raw> #6 rc=0 [main]
      mm_free <raw> #6
    mm_free __ManagedMemory #2
  mm_free Array #1
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=48
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__layout_Array_IntArray]
    x64.lea r14, [rip+__layout_Array_Integer]
    x64.lea r15, [rip+__layout_Array_Integer]
    x64.mov r8d, 1
    x64.lea r8, [rip+__layout_Array_Integer]
    x64.mov [rbp+-8], r8
    x64.mov r8d, 2
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r14
    x64.call Array.create
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.mov rdx, 1
    x64.mov rax, r15
    x64.call Array.push
    x64.mov rcx, r13
    x64.mov rdx, 2
    x64.mov rax, [rbp+-8]
    x64.call Array.push
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_IntArray]
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.lea rcx, [rip+__layout_Array_Integer]
    x64.call Array.create
    x64.mov r13, r8
    x64.lea rax, [rip+__layout_Array_Integer]
    x64.mov edx, 3
    x64.mov rcx, r13
    x64.call Array.push
    x64.mov rcx, r13
    x64.call mm_move_container
    x64.lea rax, [rip+__layout_Array_IntArray]
    x64.mov rcx, r12
    x64.mov rdx, r13
    x64.call Array.push
    x64.lea rdx, [rip+__layout_Array_IntArray]
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
    x64.lea r12, [rip+__panic_msg_e50e6c88b279a131]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```
