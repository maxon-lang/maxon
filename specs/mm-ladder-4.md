---
feature: mm-ladder-4
status: selfhosted
keywords: [refcount, ownership, leak, memory, destructor, transparent-wrap, global, relay, type-param]
category: memory
---
# Memory-Management Ladder — birth-case move-shapes (transparent-wrap / global / relay / type-param)

## Documentation

A continuation of the MM ladder isolating the FOUR remaining `transferDisposition` move-shapes that the
earlier rungs only touch incidentally inside large feature specs — each one a documented birth-case of an
ad-hoc inserter predicate, now pinned to a minimal rung that fails loudly (exit 101 / mm-trace garbage-tag
panic) if its handling regresses:

- **transparent-wrapper** (`Array.init`/`Vector.init` reinterpreting an already-owned buffer): the wrapped
  argument's `+1` transfers THROUGH the no-alloc reinterpret into the result, which already owns it
  (`callTransparentlyWrapsArg` suppresses the arg decref; `isTransparentWrapResult` adds NO store-incref).
  Born from the Sha256 K-table `g = Array.init(__mm_alloc(...))` UAF.
- **module-global managed value** (`moveIntoGlobal`): a managed value stored through `@__data_*` at module
  init transfers its `+1` to the global, which `__maxon_global_cleanup` releases at program exit — NOT a
  local scope-exit drop. Born from the global-init UAF; the shape was entirely absent from the ladder.
- **`return try EXPR` relay-slot** (`moveIntoReturnedRelay`): a `try`-result stored into a returned
  `__try_*_result` slot transfers out of the function. Born from the `Array.createIterator` for-in UAF.
- **type-param value into an opaque sink** (`moveIntoTypeParamSink`): a generic struct/function stores its
  type-parameter argument where the inserter cannot see the concrete type, so the conservative
  bias-toward-suppression arm fires and the layout-routed type-param destroy frees it at the holder's drop.

**Oracles** (same as the core ladder): exit code, leak gate (`mmAllocCount` delta → exit 101),
`<!-- MmTrace -->` + a `stderr` block (the exact alloc/incref/decref/free tree — the strongest oracle; the
mm-trace runtime PANICS on a `(null)` / garbage tag, so a use-after-free that reads a freed header fails
loudly), and `RequiredIR:x64-windows` pinned via `--update-required`. ALWAYS hand-review a regenerated trace
before locking — the leak gate cannot see a UAF that reads freed-not-yet-reused memory; the mm-trace can.

**Workflow.** Author `disabled-test`, enable ONE at a time, drive to exit-correct + leak-free + a
hand-verified trace, then `--update-required` to pin. A passing rung is a permanent regression test —
never weaken it to make a later change pass; fix the change. Run via
`maxon-selfhosted.exe spec-test --filter=mm-ladder-4`.

## Tests

<!-- test: rung-33-transparent-wrap-owned-buffer -->
<!-- MmTrace -->
The TRANSPARENT-WRAPPER shape over an already-owned managed buffer. A byte-string literal `b"hi"` lowers to
`Array.init(buf)` where `buf` is a freshly-owned `__ManagedMemory` (the raw byte storage). `Array.init`'s body
is exactly `return Self{managed: buf}` — a no-alloc REINTERPRET of `buf`'s pointer as the `ByteArray` (the
`__Array_i8` envelope is the only fresh allocation; the buffer is NOT re-allocated). The wrapped `buf`'s `+1`
must transfer THROUGH the call into the result, NOT be decref'd at the `Array.init` call as a normal argument
would: the inserter suppresses the arg decref (`callTransparentlyWrapsArg`) AND treats the result as already
owning that `+1` (`isTransparentWrapResult`). Reading a byte back (`bytes.get(0)` = ASCII `h` = 104) past the
wrap proves the buffer is still live — a double-free of `buf` at the wrap would corrupt the readback or
garbage-tag-panic the trace. Both the envelope and the wrapped buffer are freed EXACTLY ONCE at the local's
scope-exit drop; exit 104.
```maxon
function main() returns ExitCode
	let bytes = b"hi"
	return try bytes.get(0) otherwise 0
end 'main'
```
```exitcode
104
```
```stderr
mm_alloc <raw> #1 size=48 [main]
mm_incref <raw> #1 rc=1 [main]
mm_alloc Array #2 size=8 [main]
mm_drop Array #2 [main]
    mm_decref <raw> #1 rc=0 [main]
      mm_free <raw> #1
    mm_free Array #2
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov ecx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov r12, r8
    x64.xor r13d, r13d
    x64.mov [r12+40], r13 (8b)
    x64.lea r8, [rip+__istr_0]
    x64.mov [r12+0], r8 (8b)
    x64.mov r8d, 2
    x64.mov [r12+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r12+16], r8 (8b)
    x64.mov r8d, 1
    x64.mov [r12+24], r8 (8b)
    x64.mov [r12+32], r13 (8b)
    x64.mov eax, 7
    x64.lea rdx, [rip+__destruct_Array]
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov [r14+0], r12 (8b)
  inlined_Array.get_0_0:
    x64.mov rcx, [r14+0] (8b)
    x64.mov rdx, r13
    x64.call stdlib.__managed_mem_get
    x64.mov r12, r8
    x64.test rdx, rdx
    x64.je inlined_Array.get_3_0
  inlined_Array.get_1_0:
    x64.mov rcx, r14
    x64.call mm_drop
    x64.mov edx, 1
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_0
  inlined_Array.get_3_0:
    x64.mov rcx, r14
    x64.call mm_drop
    x64.xor edx, edx
    x64.mov r8, r12
  inline_cont_main_0:
    x64.test rdx, rdx
    x64.je try_0.ok
    x64.mov r12, r13
    x64.jmp try_0.merge
  try_0.ok:
    x64.mov r12, r8
  try_0.merge:
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_cbe4036ad42d7dad]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-34-module-global-managed-value -->
<!-- MmTrace -->
The `moveIntoGlobal` shape — entirely absent from the ladder until now. A module-scope `let g` holds a managed
`Box`. `Box.create` runs in `__module_init`, and the result is stored through the global's `@__data_*` address;
that `store_indirect` is a MOVE into the global (the global takes the `+1`, an `__mm_incref` to rc=2 for the
persistent reference), NOT a local last-use decref — the value must OUTLIVE `__module_init`. `main` reads
`g.value` (a borrow of the still-live global). At program exit `__maxon_global_cleanup` releases the global's
reference, dropping the `Box` to rc=0 and freeing it EXACTLY ONCE. A missed global-move would free the `Box` at
the end of `__module_init` → `main` reads freed memory; a missed cleanup-release would leak it. Exit 7.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

let g = Box.create(7)

function main() returns ExitCode
	return g.value
end 'main'
```
```exitcode
7
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_incref Box #1 rc=1 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_move Box #1 -> container [__module_init_2]
mm_decref Box #1 rc=0
  mm_free Box #1
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.lea r8, [rip+__data_g]
    x64.mov r9, [r8+0] (8b)
    x64.mov r12, [r9+0] (8b)
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_322536c63b56263b]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-35-return-try-relay-slot -->
<!-- MmTrace -->
The `moveIntoReturnedRelay` shape — a `return try EXPR` whose result is stored into a returned `__try_*_result`
relay slot and transfers out of the function. `firstOf` does `return try xs.get(0)`: `Array.get` returns a
BORROW of the array's element, which the return-borrow analysis reclassifies so the WRAPPER (`firstOf`) retains
it into a genuinely-owned `+1` handed to the caller (the rung-11 kept-get contract, here through a NON-inlined
relay-return rather than an inline get). The retained element survives `main`'s drop of the source array `xs`
and is freed EXACTLY ONCE at its own last use. A missed relay-transfer would leak the retained `+1`; a missed
get-retain would free the element under the still-live caller binding. Exit 7.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function firstOf(xs BoxArray) returns Box throws ArrayError
	return try xs.get(0)
end 'firstOf'

function main() returns ExitCode
	var xs = BoxArray.create()
	xs.push(Box.create(7))
	let b = try firstOf(xs) otherwise return 99
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
mm_incref Box #3 rc=2 [firstOf]
mm_move Box #3 -> return [firstOf]
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref Box #3 rc=1 [main]
    mm_decref <raw> #4 rc=0 [main]
      mm_free <raw> #4
    mm_free __ManagedMemory #2
  mm_free Array #1
mm_decref Box #3 rc=0 [main]
  mm_free Box #3
```
```RequiredIR:x64-windows
module {
  func @firstOf(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=32
    x64.mov r12, rcx
    x64.lea r13, [rip+__mtstr_scope_firstOf]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.xor edx, edx
  inlined_Array.get_0_0:
    x64.mov rcx, [r12+0] (8b)
    x64.call stdlib.__managed_mem_get
    x64.mov r12, r8
    x64.test rdx, rdx
    x64.je inlined_Array.get_3_0
  inlined_Array.get_1_0:
    x64.mov edx, 1
    x64.xor ecx, ecx
    x64.mov r13, rcx
    x64.mov r15, rdx
    x64.jmp inline_cont_firstOf_0
  inlined_Array.get_3_0:
    x64.xor r14d, r14d
    x64.jmp __rc_edge_5_0
  inline_cont_firstOf_0:
    x64.test r15, r15
    x64.je try_0.cont
  try_0.err:
    x64.mov rcx, r13
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.xor r8d, r8d
    x64.mov rdx, r15
    x64.epilogue
    x64.ret
  try_0.cont:
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.xor edx, edx
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  __rc_edge_5_0:
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
    x64.mov r13, r12
    x64.mov r15, r14
    x64.jmp inline_cont_firstOf_0
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__layout_Array_Box]
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8d, 7
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
    x64.call firstOf
    x64.mov r13, r8
    x64.mov r14, rdx
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.test r14, r14
    x64.je try_0.merge
  try_0.otherwise:
    x64.call mm_scope_pop
    x64.mov r8d, 99
    x64.epilogue
    x64.ret
  try_0.merge:
    x64.mov r12, [r13+0] (8b)
    x64.mov rcx, r13
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_5c4716d9dd6c47d5]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-36-type-param-sink -->
<!-- MmTrace -->
The `moveIntoTypeParamSink` shape — a generic struct stores its TYPE-PARAMETER argument into a field where the
inserter cannot see the concrete type, so the conservative bias-toward-suppression arm fires. `Wrapper uses
Item` has a `var item as Item`; `Wrapper.wrap(item)` does `return Self{item: item}` — a field store of a
type-param value. At the call site `BoxWrapper.wrap(Box.create(7))` the `Box`'s `+1` moves into the wrapper's
type-param field (suppress the local decref; the field-store-incref is balanced by the holder's destructor).
`Wrapper`'s drop routes through the per-instance layout descriptor whose `Item`-slot destroy frees the `Box`.
Reading `w.item.get()` (a method on the type-param field, resolved to `Box` in this instantiation) proves the
stored element is live. The `Box` is freed EXACTLY ONCE when the wrapper drops at scope exit; exit 7.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'

	export function get() returns Integer
		return value
	end 'get'
end 'Box'

type Wrapper uses Item
	export var item as Item

	static function wrap(item Item) returns Self
		return Self{item: item}
	end 'wrap'
end 'Wrapper'

typealias BoxWrapper = Wrapper with Box

function main() returns ExitCode
	let w = BoxWrapper.wrap(Box.create(7))
	return w.item.get()
end 'main'
```
```exitcode
7
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_incref Box #1 rc=1 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_alloc Wrapper #2 size=8 [Wrapper.wrap]
mm_move Wrapper #2 -> return [Wrapper.wrap]
mm_drop Wrapper #2 [main]
    mm_decref Box #1 rc=0 [__layout_destroy_Wrapper_Box]
      mm_free Box #1
    mm_free Wrapper #2
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
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
    x64.lea r13, [rip+__mtstr_scope_Wrapper_wrap]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.lea r8, [rip+__layout_Wrapper_Box]
    x64.mov rdx, [r8+40] (8b)
    x64.mov eax, 61
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov [r13+0], r12 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r12, [r13+0] (8b)
    x64.lea r14, [rip+__mtstr_scope_Box_get]
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov r14, [r12+0] (8b)
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r14, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_345c9908d1e96d45]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r14
    x64.epilogue
    x64.ret
  }
}

```
