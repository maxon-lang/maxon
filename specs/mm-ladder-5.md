---
feature: mm-ladder-5
status: selfhosted
keywords: [refcount, ownership, leak, memory, destructor, struct, map, nested, cascade]
category: memory
---
# Memory-Management Ladder — composed / recursive ownership (struct→container cascades)

## Documentation

A continuation of the MM ladder into COMPOSED ownership: a managed value reached through more than one level
of struct/container nesting, where the OUTER value's destructor must cascade correctly through every interior
owner down to the leaf managed elements. Each rung exercises a distinct nesting shape the earlier single-level
rungs (a struct holding ONE array — rung-25; an array of arrays — rung-26) did not reach:

- a struct holding a `Map` field, mutated THROUGH the field (struct → Map → values-array → `Box` elements);
- a struct holding a struct that holds an array (outer → inner → array → `Box` elements).

The teardown is a destructor CASCADE: the outer struct's `__destruct_<S>` decrefs its managed field, whose
own destructor decrefs ITS managed field, …, until the leaf `Box`es reach rc=0 and free. A missing link
anywhere in the chain leaks the subtree below it; a double-link double-frees.

**Oracles** (same as the core ladder): exit code, leak gate (`mmAllocCount` delta → exit 101),
`<!-- MmTrace -->` + a `stderr` block (the exact alloc/incref/decref/free tree — the strongest oracle; the
mm-trace runtime PANICS on a `(null)` / garbage tag, so a use-after-free that reads a freed header fails
loudly), and `RequiredIR:x64-windows` pinned via `--update-required`. ALWAYS hand-review a regenerated trace
before locking — the leak gate cannot see a UAF that reads freed-not-yet-reused memory; the mm-trace can.

**Workflow.** Author `disabled-test`, enable ONE at a time, drive to exit-correct + leak-free + a
hand-verified trace, then `--update-required` to pin. A passing rung is a permanent regression test —
never weaken it to make a later change pass; fix the change. Run via
`maxon-selfhosted.exe spec-test --filter=mm-ladder-5`.

## Tests

<!-- test: rung-37-struct-with-map-field -->
<!-- MmTrace -->
A struct (`Cache`) holds a `Map with (Integer, Box)` field and is MUTATED THROUGH THE FIELD
(`c.entries.upsert(...)`) — the struct→Map→values-array→`Box` cascade, combining the managed-element-container
work (a `Map<Int,Box>`'s per-type-param value-array walk) with the struct-field-cascade (rung-25's struct→Array,
here struct→Map). Two `Box`es are inserted through the field; `get(1)` reads one back as a BORROW that takes its
own +1 (survives the struct's drop, freed once at its last use). At scope exit `Cache`'s destructor decrefs the
`Map` field → the Map's destructor walks its three internal arrays (keys=`Integer` non-managed, values=`Box`
managed, states non-managed), freeing each stored `Box`. Every allocation freed exactly once; exit 7.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxMap = Map with (Integer, Box)

type Cache
	export var entries as BoxMap

	static function create() returns Self
		return Self{entries: BoxMap.create()}
	end 'create'
end 'Cache'

function main() returns ExitCode
	var c = Cache.create()
	c.entries.upsert(1, value: Box.create(7))
	c.entries.upsert(2, value: Box.create(9))
	let b = try c.entries.get(1) otherwise return 99
	return b.value
end 'main'
```
```exitcode
7
```
```stderr
mm_alloc Cache #1 size=8 [Cache.create]
mm_incref Cache #1 rc=1 [Cache.create]
mm_alloc Map #2 size=40 [Cache.create]
mm_incref Map #2 rc=1 [Cache.create]
mm_alloc Array #3 size=8 [Cache.create]
mm_incref Array #3 rc=1 [Cache.create]
mm_alloc __ManagedMemory #4 size=48 [Cache.create]
mm_incref __ManagedMemory #4 rc=1 [Cache.create]
mm_move Array #3 -> return [Cache.create]
mm_move Array #3 -> return [Cache.create]
mm_alloc Array #5 size=8 [main]
mm_incref Array #5 rc=1 [main]
mm_alloc __ManagedMemory #6 size=48 [main]
mm_incref __ManagedMemory #6 rc=1 [main]
mm_move Array #5 -> return [main]
mm_move Array #5 -> return [main]
mm_alloc Array #7 size=8
mm_incref Array #7 rc=1
mm_alloc __ManagedMemory #8 size=48
mm_incref __ManagedMemory #8 rc=1
mm_move Array #7 -> return
mm_move Array #7 -> return
mm_move Map #2 -> return
mm_move Cache #1 -> return
mm_alloc Box #9 size=8 [Box.create]
mm_incref Box #9 rc=1 [Box.create]
mm_move Box #9 -> return [Box.create]
mm_move Box #9 -> container
mm_alloc Array #10 size=8
mm_incref Array #10 rc=1
mm_alloc __ManagedMemory #11 size=48
mm_incref __ManagedMemory #11 rc=1
mm_decref Array #3 rc=0
  mm_decref __ManagedMemory #4 rc=0
    mm_free __ManagedMemory #4
  mm_free Array #3
mm_alloc <raw> #12 size=128
mm_incref <raw> #12 rc=1
mm_alloc Array #13 size=8
mm_incref Array #13 rc=1
mm_alloc __ManagedMemory #14 size=48
mm_incref __ManagedMemory #14 rc=1
mm_decref Array #5 rc=0
  mm_decref __ManagedMemory #6 rc=0
    mm_free __ManagedMemory #6
  mm_free Array #5
mm_alloc <raw> #15 size=128
mm_incref <raw> #15 rc=1
mm_alloc Array #16 size=8
mm_incref Array #16 rc=1
mm_alloc __ManagedMemory #17 size=48
mm_incref __ManagedMemory #17 rc=1
mm_decref Array #7 rc=0
  mm_decref __ManagedMemory #8 rc=0
    mm_free __ManagedMemory #8
  mm_free Array #7
mm_alloc <raw> #18 size=128
mm_incref <raw> #18 rc=1
mm_alloc Box #19 size=8 [Box.create]
mm_incref Box #19 rc=1 [Box.create]
mm_move Box #19 -> return [Box.create]
mm_move Box #19 -> container
mm_incref Box #9 rc=2
mm_decref Cache #1 rc=0
  mm_decref Map #2 rc=0
    mm_decref Array #10 rc=0
      mm_decref __ManagedMemory #11 rc=0
        mm_decref <raw> #12 rc=0
          mm_free <raw> #12
        mm_free __ManagedMemory #11
      mm_free Array #10
    mm_decref Array #13 rc=0
      mm_decref __ManagedMemory #14 rc=0
        mm_decref Box #9 rc=1
        mm_decref Box #19 rc=0
          mm_free Box #19
        mm_decref <raw> #15 rc=0
          mm_free <raw> #15
        mm_free __ManagedMemory #14
      mm_free Array #13
    mm_decref Array #16 rc=0
      mm_decref __ManagedMemory #17 rc=0
        mm_decref <raw> #18 rc=0
          mm_free <raw> #18
        mm_free __ManagedMemory #17
      mm_free Array #16
    mm_free Map #2
  mm_free Cache #1
mm_decref Box #9 rc=0
  mm_free Box #9
```
```RequiredIR:x64-windows
module {
  func @Cache.create() -> i64 {
  entry:
    x64.prologue stack_size=48
    x64.lea r12, [rip+__mtstr_scope_Cache_create]
    x64.mov r13d, 61
    x64.lea r14, [rip+__destruct_Cache]
    x64.mov r15d, 8
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r15
    x64.mov rdx, r14
    x64.mov rax, r13
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
    x64.mov eax, 37
    x64.lea rdx, [rip+__destruct_Map]
    x64.mov ecx, 40
    x64.lea r13, [rip+__layout_Map_Integer_Box]
    x64.mov r14d, 8
    x64.lea r15, [rip+__layout_elem_raw]
    x64.lea r8, [rip+__layout_elem_managed]
    x64.mov [rbp+-8], r8
    x64.mov r8d, 1
    x64.call stdlib.__mm_alloc
    x64.mov [rbp+-16], r8
    x64.mov rcx, [rbp+-16]
    x64.call stdlib.__mm_incref
    x64.mov r8, [rbp+-16]
    x64.mov r8, [r13+24] (8b)
    x64.mov rcx, r14
    x64.shr r8, r8, rcx
    x64.mov r9, [rbp+-8]
    x64.sub r9, r15
    x64.mov rsi, 1
    x64.and r8, rsi
    x64.imul r8, r9
    x64.mov r9, r15
    x64.add r9, r8
    x64.mov rcx, r9
    x64.call Array.create
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r8, [rbp+-16]
    x64.mov [r8+0], r14 (8b)
    x64.mov r8, [r13+24] (8b)
    x64.mov ecx, 9
    x64.lea r9, [rip+__layout_elem_raw]
    x64.lea rsi, [rip+__layout_elem_managed]
    x64.mov edi, 1
    x64.shr r8, r8, rcx
    x64.sub rsi, r9
    x64.and r8, rdi
    x64.imul r8, rsi
    x64.add r9, r8
    x64.mov rcx, r9
    x64.call Array.create
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r8, [rbp+-16]
    x64.mov [r8+8], r13 (8b)
    x64.lea rcx, [rip+__layout_Array_SlotState]
    x64.call Array.create
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r8, [rbp+-16]
    x64.mov [r8+16], r13 (8b)
    x64.call mm_scope_pop
    x64.xor r8d, r8d
    x64.mov r9, [rbp+-16]
    x64.mov [r9+24], r8 (8b)
    x64.call mm_scope_pop
    x64.xor r8d, r8d
    x64.mov r9, [rbp+-16]
    x64.mov [r9+32], r8 (8b)
    x64.mov rcx, [rbp+-16]
    x64.call mm_move_return
    x64.mov r8, [rbp+-16]
    x64.call mm_scope_pop
    x64.mov r8, [rbp+-16]
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=48
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.call Cache.create
    x64.mov r12, r8
    x64.lea r13, [rip+__mtstr_scope_Box_create]
    x64.mov r14d, 60
    x64.xor r15d, r15d
    x64.mov r8d, 8
    x64.mov r8d, 7
    x64.mov r8, [r12+0] (8b)
    x64.mov [rbp+-8], r8
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rdx, r15
    x64.mov rax, r14
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
    x64.lea rdi, [rip+__witness_int_Equatable]
    x64.lea rsi, [rip+__witness_int_Hashable]
    x64.lea r9, [rip+__layout_Map_Integer_Box]
    x64.mov edx, 1
    x64.mov rax, r13
    x64.mov rcx, [rbp+-8]
    x64.call Map.upsert
    x64.mov r8, [rbp+-8]
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
    x64.lea rdi, [rip+__witness_int_Equatable]
    x64.lea rsi, [rip+__witness_int_Hashable]
    x64.lea r9, [rip+__layout_Map_Integer_Box]
    x64.mov edx, 2
    x64.mov rcx, r13
    x64.mov rax, r14
    x64.call Map.upsert
    x64.mov rcx, [r12+0] (8b)
    x64.lea rsi, [rip+__witness_int_Equatable]
    x64.lea r9, [rip+__witness_int_Hashable]
    x64.lea rax, [rip+__layout_Map_Integer_Box]
    x64.mov edx, 1
    x64.call Map.get
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
    x64.mov r14, [r13+0] (8b)
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r13
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r14, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_d19e0d427ab0878b]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r14
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-38-struct-in-struct-array -->
<!-- MmTrace -->
Two struct-nesting levels where the INNER struct owns an `Array with Box`: outer `Outer` → inner `Inner` →
`items` array → `Box` elements. The inner struct is built and its array pushed-into, then MOVED into the outer
struct (`Outer.create(inner)` — the inner's `+1` transfers into the outer's field, no extra retain). Reading
`o.inner.items.count()` walks the whole chain. At scope exit `Outer`'s destructor decrefs the `Inner` field →
`Inner`'s destructor decrefs the `items` array → the array's element-walk frees each `Box`. Distinct from
rung-26 (an array OF arrays — one container level nesting another); this nests STRUCTS, so the cascade runs
through two `__destruct_<S>` field-decrefs before reaching the array walk. Every allocation freed exactly once;
exit 2 (the element count).
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

type Inner
	export var items as BoxArray

	static function create() returns Self
		return Self{items: BoxArray.create()}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	var inner = Inner.create()
	inner.items.push(Box.create(7))
	inner.items.push(Box.create(9))
	let o = Outer.create(inner)
	return o.inner.items.count()
end 'main'
```
```exitcode
2
```
```stderr
mm_alloc Inner #1 size=8 [Inner.create]
mm_incref Inner #1 rc=1 [Inner.create]
mm_alloc Array #2 size=8 [Inner.create]
mm_incref Array #2 rc=1 [Inner.create]
mm_alloc __ManagedMemory #3 size=48 [Inner.create]
mm_incref __ManagedMemory #3 rc=1 [Inner.create]
mm_move Array #2 -> return [Inner.create]
mm_move Inner #1 -> return [Inner.create]
mm_incref Array #2 rc=2 [main]
mm_alloc Box #4 size=8 [Box.create]
mm_incref Box #4 rc=1 [Box.create]
mm_move Box #4 -> return [Box.create]
mm_move Box #4 -> container [main]
mm_alloc <raw> #5 size=32 [main]
mm_incref <raw> #5 rc=1 [main]
mm_decref Array #2 rc=1 [main]
mm_incref Array #2 rc=2 [main]
mm_alloc Box #6 size=8 [Box.create]
mm_incref Box #6 rc=1 [Box.create]
mm_move Box #6 -> return [Box.create]
mm_move Box #6 -> container [main]
mm_decref Array #2 rc=1 [main]
mm_alloc Outer #7 size=8 [Outer.create]
mm_move Outer #7 -> return [Outer.create]
mm_drop Outer #7 [main]
    mm_decref Inner #1 rc=0 [main]
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
      mm_free Inner #1
    mm_free Outer #7
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=48
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.lea r13, [rip+__mtstr_scope_Inner_create]
    x64.mov r14d, 61
    x64.lea r15, [rip+__destruct_Inner]
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
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
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
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
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
    x64.mov rcx, r13
    x64.call __mm_decref_maybenull_helper
    x64.mov r13, [r12+0] (8b)
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 9
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-8]
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
    x64.call __mm_decref_maybenull_helper
    x64.lea r13, [rip+__mtstr_scope_Outer_create]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov eax, 62
    x64.lea rdx, [rip+__destruct_Outer]
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov [r13+0], r12 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r8, [r13+0] (8b)
    x64.mov rcx, [r8+0] (8b)
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.call Array.count
    x64.mov r12, r8
    x64.mov rcx, r13
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_ea902e6586b08d09]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-39-enum-managed-payload -->
<!-- MmTrace -->
A `union` with a MANAGED payload (`Maybe.wrapped(Box.create(7))`) heap-boxes the payload and must free it at the
box's drop. `match`-and-extract reads the payload (`wrapped(b) gives b.value`) as a BORROW (the union still owns
it); the non-matched `empty` case owns nothing. Five compiler gaps this rung found and fixed (the
`enum-payload-double-incref` the plan predicted, plus the universal-struct-management hole it exposed):
(1) `lowerEnumConstruct`'s `__mm_alloc` passed the case ORDINAL as the mm-trace TYPE tag → garbage-tag panic;
now passes the interned union type-name tag (`Maybe #N`), ordinal stored separately at offset 0.
(2) `lowerEnumConstruct` + `lowerEnumPayloadRead` emitted MANUAL `__mm_incref`s (stale inline-RAII) on top of the
late inserter's accounting → double-incref; removed (the inserter is the sole placer).
(3) a payload-bearing union value is heap-boxed (`__mm_alloc`) but unions live in `enumTypes`, so
`structNameIsHeapManaged` (which only checked `structTypes`) classified `let m = Maybe.wrapped(...)` `notManaged`
→ NEVER dropped; now a managed-payload union is heap-managed.
(4) a struct/union FIELD of a heap-managed type whose OWN destructor is a null no-op (a plain `Box` with only a
non-managed `Integer` field) was NOT decref'd by the enclosing destructor (`fieldTypeIsDropTracked` deferred to
the recursive has-a-managed-SUB-field test, false for `Box`); now ANY heap-managed field is drop-tracked → the
enclosing `__destruct_<S>` decrefs it.
(5) a CONSUMED parameter stored into the constructor's box (`__construct_Maybe_wrapped`'s payload field-store)
was treated as a borrow-COPY (store-incref) instead of a MOVE of the caller's transferred +1 → double-count;
now a consumed param carries a +1 (`transferValueAlreadyOwnsPlusOne` + `storeIsManagedFieldTransfer`), so the
field-store is a move with no incref.
The `Box` is freed EXACTLY ONCE when the `Maybe` box drops at scope exit; exit 7.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

union Maybe
	wrapped(item Box)
	empty
end 'Maybe'

function main() returns ExitCode
	let m = Maybe.wrapped(Box.create(7))
	return match m 'check'
		wrapped(b) gives b.value
		empty gives 0
	end 'check'
end 'main'
```
```exitcode
7
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_incref Box #1 rc=1 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_move Box #1 -> container [main]
mm_alloc Maybe #2 size=16 [__construct_Maybe_wrapped]
mm_incref Maybe #2 rc=1 [__construct_Maybe_wrapped]
mm_move Maybe #2 -> return [__construct_Maybe_wrapped]
mm_decref Maybe #2 rc=0 [main]
  mm_decref Box #1 rc=0 [main]
    mm_free Box #1
  mm_free Maybe #2
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.lea r12, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
    x64.mov r8d, 7
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r12
    x64.call mm_move_container
    x64.mov rcx, r12
    x64.call __construct_Maybe_wrapped
    x64.mov r9, [r8+0] (8b)
    x64.xor r12d, r12d
    x64.test r9, r9
    x64.jne check_0.next0
    x64.jmp check_0.case0
  check_0.merge:
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  check_0.case0:
    x64.mov r9, [r8+8] (8b)
    x64.mov r12, [r9+0] (8b)
    x64.mov rcx, r8
    x64.call __mm_decref_maybenull_helper
    x64.jmp check_0.merge
  check_0.next0:
    x64.mov r13, [r8+0] (8b)
    x64.mov rcx, r8
    x64.call __mm_decref_maybenull_helper
    x64.cmp r13, 1
    x64.jne check_0.merge
  check_0.case1:
    x64.xor r12d, r12d
    x64.jmp check_0.merge
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_9af0b112576b3fb5]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-40-closure-captures-managed -->
<!-- MmTrace -->
A closure CAPTURES a managed local (`b`, a `Box`) and is called once. Maxon captures BY REFERENCE (uniform
reference semantics): `closureCreate` stores the captured variable's slot ADDRESS into the closure env, and the
lifted body reads the live value through a double-deref — so the closure sees the SAME `Box` the outer scope
owns. This exercises the ADDRESS-TAKEN-SLOT lifetime support the inserter previously lacked entirely (it was the
root of the pre-existing `ownership/closure-captures-borrow` garbage-output failure). Three coupled fixes:
(1) mem2reg already PINS a `stack_addr`-captured slot in memory (won't promote it); now the inserter recognizes
a managed value stored into such a slot as `moveIntoAddressTakenSlot` and SUPPRESSES its SSA-store decref — so
`b` is NOT freed at the textual store, BEFORE the closure reads it (the prior dangling-pointer / garbage read).
(2) `planAddressTakenSlotDrops` releases the slot's content at the function's scope EXIT (after every closure
use) via a null-guarded `loadSlot`+decref — so `b` is freed EXACTLY ONCE, at `main`'s end. (3) the closure env
block is now `__mm_alloc`-tracked with a null destructor (it holds only borrowed slot addresses) so the inserter
drops + frees it after the closure call instead of leaking it. Both `Box` and the env are freed exactly once;
exit 7.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntFn = function() returns Integer

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function callIt(f IntFn) returns Integer
	return f()
end 'callIt'

function main() returns ExitCode
	let b = Box.create(7)
	return callIt(function() gives b.value)
end 'main'
```
```exitcode
7
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_incref Box #1 rc=1 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_alloc __closure_env #2 size=8 [main]
mm_drop __closure_env #2 [callIt]
    mm_free __closure_env #2
mm_decref Box #1 rc=0 [main]
  mm_free Box #1
```
```RequiredIR:x64-windows
module {
  func @main$closure_0(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=16
    x64.mov r12, rcx
    x64.lea r13, [rip+__mtstr_scope_main_closure_0]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov r8, [r12+0] (8b)
    x64.mov r9, [r8+0] (8b)
    x64.mov r12, [r9+0] (8b)
    x64.call mm_scope_pop
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=96
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.xor r13d, r13d
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-56], r8
    x64.mov r8d, 8
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov [rbp-8], r13
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-56]
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
    x64.mov r8d, 7
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov [rbp-8], r12
    x64.mov eax, 9
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.lea rax, [rbp-8]
    x64.mov r8, [rbp-16]
    x64.mov [r12+0], r8 (8b)
    x64.lea r13, [rip+__mtstr_scope_callIt]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.lea r13, [rip+main$closure_0]
    x64.mov rcx, r12
    x64.call r13
    x64.mov r13, r8
    x64.mov rcx, r12
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.mov rcx, [rbp-8]
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_1ebff9872053aabe]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-41-aliased-managed-element -->
<!-- MmTrace -->
Two managed-container bindings ALIAS the same backing (`var xs = …; let ys = xs`), then a managed element is
read back through the alias as a BORROW (`let a = try ys.get(0)`) and used AFTER the array has logically gone out
of scope. `let ys = xs` is a pure SSA alias — mem2reg coalesces `ys` into `xs`'s value, so there is ONE array
with ONE +1, freed exactly once. The hard part is the BORROW lifetime: `Array.get` returns an interior pointer
INTO the array's buffer (the element the container owns, NOT a fresh +1), so `a` must take its OWN retain (+1 on
`Box #3` → rc=2) BEFORE the array's teardown decrefs that element back to rc=1, leaving `a` holding the last
reference until `return a.value`. This rung is the regression test for the ALIASED-MANAGED-ELEMENT use-after-free:
`__managed_mem_get` is now a recognized receiver-borrow callee (`StdLiveness.calleeReturnsReceiverBorrow`), so the
inserter's interior-borrow liveness keeps the array live across the get; and the array's drop is DEFERRED onto the
phi edge AFTER the borrowed element's retain (`planBorrowRootEdgeRelease` + the `borrowRootDeferredToPhiEdge`
term-drop suppression). Without either, the array (and `Box #3`) freed at the `get` call, then the borrow-retain
increfed a freed header → garbage-tag panic. `Box #5` (never aliased) frees with the array; `Box #3` frees once at
`a`'s last use. Exit 7.
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
	let ys = xs
	let a = try ys.get(0) otherwise return 99
	return a.value
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
mm_incref Box #3 rc=2 [main]
mm_decref Array #1 rc=0 [main]
  mm_decref __ManagedMemory #2 rc=0 [main]
    mm_decref Box #3 rc=1 [main]
    mm_decref Box #5 rc=0 [main]
      mm_free Box #5
    mm_decref <raw> #4 rc=0 [main]
      mm_free <raw> #4
    mm_free __ManagedMemory #2
  mm_free Array #1
mm_decref Box #3 rc=0 [main]
  mm_free Box #3
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
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov edx, 1
    x64.xor ecx, ecx
    x64.mov r12, rcx
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
    x64.call mm_scope_pop
    x64.mov r8d, 99
    x64.epilogue
    x64.ret
  try_0.merge:
    x64.mov r13, [r12+0] (8b)
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  __rc_edge_6_0:
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov r12, r13
    x64.mov rdx, r14
    x64.jmp inline_cont_main_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_e9c6b7024ede6c0f]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-42-grand-integration -->
<!-- MmTrace -->
The GRAND INTEGRATION rung — every move-shape the ladder built, composed in one program, every allocation freed
exactly once. A `Store` struct holds BOTH a `Map with (Integer, Box)` AND an `Array with Box`; it is constructed
and populated inside a HELPER (`build`) and RETURNED by value (the struct + both managed fields + every element
move out of `build`'s frame into `main`'s). In `main` the struct is then exercised four ways at once:
(1) a `for-in` over `s.items` BORROWS each element through an `ArrayIterator` that holds an interior pointer into
the array's backing buffer — the array must outlive the iterator (iterator-borrow liveness); (2) `s.items.pop()`
TRANSFERS the last element OUT of the array into `main`'s `popped` — that `Box` is removed from the container, so
the array's teardown element-walk must NOT free it (it is freed once, later, at `popped`'s last use); (3)
`s.index.get(1)` BORROWS a map value, which takes its OWN retain (rc=2) and is brought back to rc=1 by the map's
value-array walk, then freed once at `b`'s last use (the aliased-managed-element borrow-root deferral); (4) the
whole `Store` is dropped at `main`'s scope exit, cascading struct → Array (element-walk frees the two
NON-popped boxes) and struct → Map (per-type-param value-array walk frees the map's box). A single missing or
doubled link anywhere in this cascade leaks or double-frees; the rung passes only when all 26 allocations balance.
Exit 115 (`for-in` sum 10+20+30=60, plus map value 25, plus popped 30). The values keep the total within the
narrowest `ExitCode` range across targets (wasm32-wasi caps at 0..125), while preserving the exact cascade.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box
typealias BoxMap = Map with (Integer, Box)

type Store
	export var items as BoxArray
	export var index as BoxMap

	static function create() returns Self
		return Self{items: BoxArray.create(), index: BoxMap.create()}
	end 'create'
end 'Store'

function build() returns Store
	var s = Store.create()
	s.items.push(Box.create(10))
	s.items.push(Box.create(20))
	s.items.push(Box.create(30))
	s.index.upsert(1, value: Box.create(25))
	return s
end 'build'

function main() returns ExitCode
	var s = build()
	var sum = 0
	for it in s.items 'each'
		sum = sum + it.value
	end 'each'
	let popped = try s.items.pop() otherwise return 98
	let b = try s.index.get(1) otherwise return 99
	return sum + b.value + popped.value
end 'main'
```
```exitcode
115
```
```stderr
mm_alloc Store #1 size=16 [Store.create]
mm_incref Store #1 rc=1 [Store.create]
mm_alloc Array #2 size=8 [Store.create]
mm_incref Array #2 rc=1 [Store.create]
mm_alloc __ManagedMemory #3 size=48 [Store.create]
mm_incref __ManagedMemory #3 rc=1 [Store.create]
mm_move Array #2 -> return [Store.create]
mm_alloc Map #4 size=40 [Store.create]
mm_incref Map #4 rc=1 [Store.create]
mm_alloc Array #5 size=8 [Store.create]
mm_incref Array #5 rc=1 [Store.create]
mm_alloc __ManagedMemory #6 size=48 [Store.create]
mm_incref __ManagedMemory #6 rc=1 [Store.create]
mm_move Array #5 -> return [Store.create]
mm_move Array #5 -> return [Store.create]
mm_alloc Array #7 size=8 [build]
mm_incref Array #7 rc=1 [build]
mm_alloc __ManagedMemory #8 size=48 [build]
mm_incref __ManagedMemory #8 rc=1 [build]
mm_move Array #7 -> return [build]
mm_move Array #7 -> return [build]
mm_alloc Array #9 size=8 [main]
mm_incref Array #9 rc=1 [main]
mm_alloc __ManagedMemory #10 size=48 [main]
mm_incref __ManagedMemory #10 rc=1 [main]
mm_move Array #9 -> return [main]
mm_move Array #9 -> return [main]
mm_move Map #4 -> return
mm_move Store #1 -> return
mm_incref Array #2 rc=2
mm_alloc Box #11 size=8 [Box.create]
mm_incref Box #11 rc=1 [Box.create]
mm_move Box #11 -> return [Box.create]
mm_move Box #11 -> container
mm_alloc <raw> #12 size=32
mm_incref <raw> #12 rc=1
mm_decref Array #2 rc=1
mm_incref Array #2 rc=2
mm_alloc Box #13 size=8 [Box.create]
mm_incref Box #13 rc=1 [Box.create]
mm_move Box #13 -> return [Box.create]
mm_move Box #13 -> container
mm_decref Array #2 rc=1
mm_incref Array #2 rc=2
mm_alloc Box #14 size=8 [Box.create]
mm_incref Box #14 rc=1 [Box.create]
mm_move Box #14 -> return [Box.create]
mm_move Box #14 -> container
mm_decref Array #2 rc=1
mm_incref Map #4 rc=2
mm_alloc Box #15 size=8 [Box.create]
mm_incref Box #15 rc=1 [Box.create]
mm_move Box #15 -> return [Box.create]
mm_move Box #15 -> container
mm_alloc Array #16 size=8
mm_incref Array #16 rc=1
mm_alloc __ManagedMemory #17 size=48
mm_incref __ManagedMemory #17 rc=1
mm_decref Array #5 rc=0
  mm_decref __ManagedMemory #6 rc=0
    mm_free __ManagedMemory #6
  mm_free Array #5
mm_alloc <raw> #18 size=128
mm_incref <raw> #18 rc=1
mm_alloc Array #19 size=8
mm_incref Array #19 rc=1
mm_alloc __ManagedMemory #20 size=48
mm_incref __ManagedMemory #20 rc=1
mm_decref Array #7 rc=0
  mm_decref __ManagedMemory #8 rc=0
    mm_free __ManagedMemory #8
  mm_free Array #7
mm_alloc <raw> #21 size=128
mm_incref <raw> #21 rc=1
mm_alloc Array #22 size=8
mm_incref Array #22 rc=1
mm_alloc __ManagedMemory #23 size=48
mm_incref __ManagedMemory #23 rc=1
mm_decref Array #9 rc=0
  mm_decref __ManagedMemory #10 rc=0
    mm_free __ManagedMemory #10
  mm_free Array #9
mm_alloc <raw> #24 size=128
mm_incref <raw> #24 rc=1
mm_move Store #1 -> return
mm_decref Map #4 rc=1
mm_alloc <raw> #25 size=40
mm_incref <raw> #25 rc=1
mm_incref __ManagedMemory #3 rc=2
mm_alloc ArrayIterator #26 size=8
mm_incref ArrayIterator #26 rc=1
mm_move ArrayIterator #26 -> return
mm_move ArrayIterator #26 -> phi
mm_incref Box #11 rc=2
mm_decref Box #11 rc=1
mm_incref Box #13 rc=2
mm_decref Box #13 rc=1
mm_incref Box #14 rc=2
mm_decref Box #14 rc=1
mm_decref ArrayIterator #26 rc=0
  mm_decref <raw> #25 rc=0
    mm_decref __ManagedMemory #3 rc=1
    mm_free <raw> #25
  mm_free ArrayIterator #26
mm_incref Box #15 rc=2
mm_decref Box #15 rc=1
mm_decref Store #1 rc=0
  mm_decref Array #2 rc=0
    mm_decref __ManagedMemory #3 rc=0
      mm_decref Box #11 rc=0
        mm_free Box #11
      mm_decref Box #13 rc=0
        mm_free Box #13
      mm_decref <raw> #12 rc=0
        mm_free <raw> #12
      mm_free __ManagedMemory #3
    mm_free Array #2
  mm_decref Map #4 rc=0
    mm_decref Array #16 rc=0
      mm_decref __ManagedMemory #17 rc=0
        mm_decref <raw> #18 rc=0
          mm_free <raw> #18
        mm_free __ManagedMemory #17
      mm_free Array #16
    mm_decref Array #19 rc=0
      mm_decref __ManagedMemory #20 rc=0
        mm_decref Box #15 rc=0
          mm_free Box #15
        mm_decref <raw> #21 rc=0
          mm_free <raw> #21
        mm_free __ManagedMemory #20
      mm_free Array #19
    mm_decref Array #22 rc=0
      mm_decref __ManagedMemory #23 rc=0
        mm_decref <raw> #24 rc=0
          mm_free <raw> #24
        mm_free __ManagedMemory #23
      mm_free Array #22
    mm_free Map #4
  mm_free Store #1
mm_decref Box #14 rc=0
  mm_free Box #14
```
```RequiredIR:x64-windows
module {
  func @Store.create() -> i64 {
  entry:
    x64.prologue stack_size=64
    x64.lea r12, [rip+__mtstr_scope_Store_create]
    x64.mov r13d, 61
    x64.lea r14, [rip+__destruct_Store]
    x64.mov r15d, 16
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r15
    x64.mov rdx, r14
    x64.mov rax, r13
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
    x64.lea rcx, [rip+__layout_Array_Box]
    x64.mov r13d, 37
    x64.lea r14, [rip+__destruct_Map]
    x64.mov r15d, 40
    x64.lea r8, [rip+__layout_Map_Integer_Box]
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.lea r8, [rip+__layout_elem_raw]
    x64.mov [rbp+-16], r8
    x64.lea r8, [rip+__layout_elem_managed]
    x64.mov [rbp+-24], r8
    x64.mov r8d, 1
    x64.call Array.create
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r15
    x64.mov rdx, r14
    x64.mov rax, r13
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, [rbp+-8]
    x64.mov r9, [r8+24] (8b)
    x64.mov rcx, 8
    x64.shr r9, r9, rcx
    x64.mov r8, [rbp+-24]
    x64.sub r8, [rbp+-16]
    x64.mov rsi, 1
    x64.and r9, rsi
    x64.imul r9, r8
    x64.mov r8, [rbp+-16]
    x64.add r8, r9
    x64.mov rcx, r8
    x64.call Array.create
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov [r13+0], r14 (8b)
    x64.mov r8, [rbp+-8]
    x64.mov r9, [r8+24] (8b)
    x64.mov ecx, 9
    x64.lea r8, [rip+__layout_elem_raw]
    x64.lea rsi, [rip+__layout_elem_managed]
    x64.mov edi, 1
    x64.shr r9, r9, rcx
    x64.sub rsi, r8
    x64.and r9, rdi
    x64.imul r9, rsi
    x64.add r8, r9
    x64.mov rcx, r8
    x64.call Array.create
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov [r13+8], r14 (8b)
    x64.lea rcx, [rip+__layout_Array_SlotState]
    x64.call Array.create
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov [r13+16], r14 (8b)
    x64.call mm_scope_pop
    x64.xor r8d, r8d
    x64.mov [r13+24], r8 (8b)
    x64.call mm_scope_pop
    x64.xor r8d, r8d
    x64.mov [r13+32], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov [r12+8], r13 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
  func @build() -> i64 {
  entry:
    x64.prologue stack_size=48
    x64.lea r12, [rip+__mtstr_scope_build]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.call Store.create
    x64.mov r12, r8
    x64.mov r13, [r12+0] (8b)
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 10
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-8]
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8, 10
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
    x64.call __mm_decref_maybenull_helper
    x64.mov r13, [r12+0] (8b)
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 20
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-8]
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8, 20
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
    x64.call __mm_decref_maybenull_helper
    x64.mov r13, [r12+0] (8b)
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov r15d, 60
    x64.xor r8d, r8d
    x64.mov [rbp+-8], r8
    x64.mov r8d, 8
    x64.mov r8d, 30
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rax, r15
    x64.mov rdx, [rbp+-8]
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8, 30
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
    x64.call __mm_decref_maybenull_helper
    x64.mov r13, [r12+8] (8b)
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8d, 25
    x64.mov [r14+0], r8 (8b)
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r14
    x64.call mm_move_container
    x64.lea rdi, [rip+__witness_int_Equatable]
    x64.lea rsi, [rip+__witness_int_Hashable]
    x64.lea r9, [rip+__layout_Map_Integer_Box]
    x64.mov edx, 1
    x64.mov rcx, r13
    x64.mov rax, r14
    x64.call Map.upsert
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.mov rcx, r13
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=64
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.call build
    x64.mov r12, r8
    x64.mov r8, [r12+0] (8b)
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.xor r13d, r13d
  inlined_Array.createIterator_0_0:
    x64.mov rcx, [r8+0] (8b)
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
    x64.je each_0
    x64.jmp __rc_edge_14_0
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
    x64.je __phi_trampoline_15_0
  inlined_ArrayIterator.advance_1_0:
    x64.mov edx, 1
  inline_cont_main_1:
    x64.test rdx, rdx
    x64.je each_0
    x64.jmp __rc_edge_18_0
  each_0:
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
    x64.jmp __rc_edge_20_0
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
    x64.jmp __rc_edge_30_0
  inline_cont_main_3:
    x64.call mm_scope_pop
    x64.mov r8, [r15+0] (8b)
    x64.mov [rbp+-32], r8
    x64.mov rcx, r15
    x64.call __mm_decref_maybenull_helper
    x64.add r13, [rbp+-32]
    x64.jmp inlined_ArrayIterator.advance_0_0
  each_0.exit:
    x64.mov rcx, [r12+0] (8b)
    x64.lea rdx, [rip+__layout_Array_Box]
    x64.call Array.pop
    x64.mov r14, r8
    x64.test rdx, rdx
    x64.je try_0.merge
  try_0.otherwise:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 98
    x64.epilogue
    x64.ret
  try_0.merge:
    x64.mov rcx, [r12+8] (8b)
    x64.lea rsi, [rip+__witness_int_Equatable]
    x64.lea r9, [rip+__witness_int_Hashable]
    x64.lea rax, [rip+__layout_Map_Integer_Box]
    x64.mov edx, 1
    x64.call Map.get
    x64.mov r15, r8
    x64.test rdx, rdx
    x64.je try_1.merge
  try_1.otherwise:
    x64.mov rcx, r14
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 99
    x64.epilogue
    x64.ret
  try_1.merge:
    x64.mov r8, [r15+0] (8b)
    x64.mov [rbp+-32], r8
    x64.mov rcx, r15
    x64.call __mm_decref_maybenull_helper
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov r12, [r14+0] (8b)
    x64.mov rcx, r14
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.add r13, [rbp+-32]
    x64.mov r9d, 4294967295
    x64.mov r8, r13
    x64.add r8, r12
    x64.cmp r8, r9
    x64.jbe __range_ok_0
    x64.jmp __range_panic_0
  __rc_edge_14_0:
    x64.mov rcx, r14
    x64.call __mm_decref_maybenull_helper
    x64.jmp each_0.exit
  __rc_edge_18_0:
    x64.mov rcx, r14
    x64.call __mm_decref_maybenull_helper
    x64.jmp each_0.exit
  __rc_edge_20_0:
    x64.mov rcx, r15
    x64.call stdlib.__mm_incref
    x64.jmp inline_cont_main_3
  __rc_edge_30_0:
    x64.mov rcx, r15
    x64.call stdlib.__mm_incref
    x64.jmp inline_cont_main_3
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_7b7dd3d0727e68ae]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  __phi_trampoline_15_0:
    x64.mov rdx, r8
    x64.jmp inline_cont_main_1
  }
}

```

```RequiredIR:wasm32-wasi
module {
  func @Store.create() -> i64 {
  entry:
    %10 = mir.global_addr @__mtstr_scope_Store_create
    %11 = mir.call @mm_scope_push(%10)
    %0 = mir.mov_imm 16 : i64
    %2 = mir.func_addr @__destruct_Store
    %3 = mir.mov_imm 61 : i64
    %1 = mir.call @stdlib.__mm_alloc(%0, %2, %3)
    %63 = mir.call @stdlib.__mm_incref(%1)
    %4 = mir.global_addr @__layout_Array_Box
    %5 = mir.call @Array.create(%4)
    mir.store %5, %1, 0 width: qword
    %6 = mir.global_addr @__layout_Map_Integer_Box
    %12 = mir.mov_imm 40 : i64
    %13 = mir.func_addr @__destruct_Map
    %14 = mir.mov_imm 37 : i64
    %15 = mir.call @stdlib.__mm_alloc(%12, %13, %14)
    %64 = mir.call @stdlib.__mm_incref(%15)
    %24 = mir.load %6, 24 width: qword
    %25 = mir.mov_imm 8 : i64
    %26 = mir.shr.i64 %24, %25
    %27 = mir.mov_imm 1 : i64
    %28 = mir.and.i64 %26, %27
    %29 = mir.global_addr @__layout_elem_managed
    %30 = mir.global_addr @__layout_elem_raw
    %31 = mir.sub.i64 %29, %30
    %32 = mir.mul.i64 %28, %31
    %33 = mir.add.i64 %30, %32
    %34 = mir.call @Array.create(%33)
    %36 = mir.call @mm_move_return(%34)
    %37 = mir.call @mm_scope_pop()
    mir.store %34, %15, 0 width: qword
    %38 = mir.load %6, 24 width: qword
    %39 = mir.mov_imm 9 : i64
    %40 = mir.shr.i64 %38, %39
    %41 = mir.mov_imm 1 : i64
    %42 = mir.and.i64 %40, %41
    %43 = mir.global_addr @__layout_elem_managed
    %44 = mir.global_addr @__layout_elem_raw
    %45 = mir.sub.i64 %43, %44
    %46 = mir.mul.i64 %42, %45
    %47 = mir.add.i64 %44, %46
    %48 = mir.call @Array.create(%47)
    %50 = mir.call @mm_move_return(%48)
    %51 = mir.call @mm_scope_pop()
    mir.store %48, %15, 8 width: qword
    %52 = mir.global_addr @__layout_Array_SlotState
    %53 = mir.call @Array.create(%52)
    %55 = mir.call @mm_move_return(%53)
    %56 = mir.call @mm_scope_pop()
    mir.store %53, %15, 16 width: qword
    %57 = mir.mov_imm 0 : i64
    %59 = mir.call @mm_scope_pop()
    mir.store %57, %15, 24 width: qword
    %60 = mir.mov_imm 0 : i64
    %62 = mir.call @mm_scope_pop()
    mir.store %60, %15, 32 width: qword
    %22 = mir.call @mm_move_return(%15)
    %23 = mir.call @mm_scope_pop()
    mir.store %15, %1, 8 width: qword
    %65 = mir.call @mm_move_return(%1)
    %66 = mir.call @mm_scope_pop()
    mir.ret %1
  }
  func @build() -> i64 {
  entry:
    %30 = mir.global_addr @__mtstr_scope_build
    %31 = mir.call @mm_scope_push(%30)
    %1 = mir.call @Store.create()
    %3 = mir.load %1, 0 width: qword
    %68 = mir.call @stdlib.__mm_incref(%3)
    %4 = mir.mov_imm 10 : i64
    %32 = mir.global_addr @__mtstr_scope_Box_create
    %33 = mir.call @mm_scope_push(%32)
    %34 = mir.mov_imm 8 : i64
    %35 = mir.mov_imm 0 : i64
    %36 = mir.mov_imm 60 : i64
    %37 = mir.call @stdlib.__mm_alloc(%34, %35, %36)
    %69 = mir.call @stdlib.__mm_incref(%37)
    mir.store %4, %37, 0 width: qword
    %39 = mir.call @mm_move_return(%37)
    %40 = mir.call @mm_scope_pop()
    %6 = mir.global_addr @__layout_Array_Box
    %80 = mir.call @mm_move_container(%37)
    %7 = mir.call @Array.push(%3, %37, %6)
    %76 = mir.call @__mm_decref_maybenull_helper(%3)
    %9 = mir.load %1, 0 width: qword
    %70 = mir.call @stdlib.__mm_incref(%9)
    %10 = mir.mov_imm 20 : i64
    %41 = mir.global_addr @__mtstr_scope_Box_create
    %42 = mir.call @mm_scope_push(%41)
    %43 = mir.mov_imm 8 : i64
    %44 = mir.mov_imm 0 : i64
    %45 = mir.mov_imm 60 : i64
    %46 = mir.call @stdlib.__mm_alloc(%43, %44, %45)
    %71 = mir.call @stdlib.__mm_incref(%46)
    mir.store %10, %46, 0 width: qword
    %48 = mir.call @mm_move_return(%46)
    %49 = mir.call @mm_scope_pop()
    %12 = mir.global_addr @__layout_Array_Box
    %81 = mir.call @mm_move_container(%46)
    %13 = mir.call @Array.push(%9, %46, %12)
    %77 = mir.call @__mm_decref_maybenull_helper(%9)
    %15 = mir.load %1, 0 width: qword
    %72 = mir.call @stdlib.__mm_incref(%15)
    %16 = mir.mov_imm 30 : i64
    %50 = mir.global_addr @__mtstr_scope_Box_create
    %51 = mir.call @mm_scope_push(%50)
    %52 = mir.mov_imm 8 : i64
    %53 = mir.mov_imm 0 : i64
    %54 = mir.mov_imm 60 : i64
    %55 = mir.call @stdlib.__mm_alloc(%52, %53, %54)
    %73 = mir.call @stdlib.__mm_incref(%55)
    mir.store %16, %55, 0 width: qword
    %57 = mir.call @mm_move_return(%55)
    %58 = mir.call @mm_scope_pop()
    %18 = mir.global_addr @__layout_Array_Box
    %82 = mir.call @mm_move_container(%55)
    %19 = mir.call @Array.push(%15, %55, %18)
    %78 = mir.call @__mm_decref_maybenull_helper(%15)
    %21 = mir.load %1, 8 width: qword
    %74 = mir.call @stdlib.__mm_incref(%21)
    %22 = mir.mov_imm 1 : i64
    %23 = mir.mov_imm 25 : i64
    %59 = mir.global_addr @__mtstr_scope_Box_create
    %60 = mir.call @mm_scope_push(%59)
    %61 = mir.mov_imm 8 : i64
    %62 = mir.mov_imm 0 : i64
    %63 = mir.mov_imm 60 : i64
    %64 = mir.call @stdlib.__mm_alloc(%61, %62, %63)
    %75 = mir.call @stdlib.__mm_incref(%64)
    mir.store %23, %64, 0 width: qword
    %66 = mir.call @mm_move_return(%64)
    %67 = mir.call @mm_scope_pop()
    %25 = mir.global_addr @__layout_Map_Integer_Box
    %26 = mir.global_addr @__witness_int_Hashable
    %27 = mir.global_addr @__witness_int_Equatable
    %83 = mir.call @mm_move_container(%64)
    %28 = mir.call @Map.upsert(%21, %22, %64, %25, %26, %27)
    %84 = mir.call @mm_move_return(%1)
    %79 = mir.call @__mm_decref_maybenull_helper(%21)
    %85 = mir.call @mm_scope_pop()
    mir.ret %1
  }
  func @main() -> u8 {
  entry:
    %59 = mir.global_addr @__mtstr_scope_main
    %60 = mir.call @mm_scope_push(%59)
    %0 = mir.mov_imm 0 : i64
    %7 = mir.call @build()
    %10 = mir.load %7, 0 width: qword
    %11 = mir.global_addr @__layout_Array_Box
    mir.br inlined_Array.createIterator_0_0()
  inlined_Array.createIterator_0_0:
    %63 = mir.load %10, 0 width: qword
    %64, %65 = mir.try_call @ArrayIterator.create(%63, %11)
    %66 = mir.mov_imm 0 : i64
    %67 = mir.cmp ne, %65, %66
    mir.cond_br %67 [then: inlined_Array.createIterator_1_0(), else: inlined_Array.createIterator_2_0()]
  inlined_Array.createIterator_1_0:
    %68 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_0(%68, %65)
  inlined_Array.createIterator_2_0:
    %69 = mir.mov_imm 0 : i64
    %136 = mir.call @mm_move_phi(%64)
    mir.br inline_cont_main_0(%64, %69)
  inline_cont_main_0(%70: i64, %71: i64):
    %15 = mir.cmp ne, %71, %0
    mir.cond_br %15 [then: __rc_edge_14_0(), else: each_0(%0)]
  inlined_ArrayIterator.advance_0_0:
    %72 = mir.load %70, 0 width: qword
    %73 = mir.load %72, 8 width: qword
    %74 = mir.load %72, 16 width: qword
    %75 = mir.mov_imm 1 : i64
    %76 = mir.add.i64 %73, %75
    %77 = mir.cmp lt, %76, %74
    %78 = mir.sub.i64 %76, %73
    %79 = mir.mul.i64 %77, %78
    %80 = mir.add.i64 %73, %79
    mir.store %80, %72, 8 width: qword
    %81 = mir.mov_imm 1 : i64
    %82 = mir.sub.i64 %81, %77
    %83 = mir.mov_imm 1 : i64
    %84 = mir.mul.i64 %82, %83
    %85 = mir.mov_imm 0 : i64
    %86 = mir.cmp ne, %84, %85
    mir.cond_br %86 [then: inlined_ArrayIterator.advance_1_0(), else: inline_cont_main_2(%85, %85)]
  inlined_ArrayIterator.advance_1_0:
    %87 = mir.mov_imm 1 : i64
    mir.br inline_cont_main_2(%85, %87)
  inline_cont_main_2(%88: i64, %89: i64):
    %21 = mir.cmp ne, %89, %0
    mir.cond_br %21 [then: __rc_edge_18_0(), else: each_0(%28)]
  each_0(%61: i64):
    %90 = mir.load %70, 0 width: qword
    mir.br inlined_stdlib.__managed_mem_cursor_current_0_0()
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    %94 = mir.load %90, 0 width: qword
    %95 = mir.mov_imm 8 : i64
    %96 = mir.add.i64 %90, %95
    %97 = mir.load %96, 0 width: qword
    %98 = mir.mov_imm 24 : i64
    %99 = mir.add.i64 %90, %98
    %100 = mir.load %99, 0 width: qword
    %101 = mir.mov_imm 0 : i64
    %102 = mir.cmp eq, %100, %101
    mir.cond_br %102 [then: inlined_stdlib.__managed_mem_cursor_current_1_0(), else: inlined_stdlib.__managed_mem_cursor_current_2_0()]
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    %108 = mir.mov_imm 3 : i64
    %109 = mir.shr.i64 %97, %108
    %110 = mir.add.i64 %94, %109
    %111 = mir.mov_imm 0 : i64
    %112 = mir.load_byte %110, %111
    %113 = mir.mov_imm 7 : i64
    %114 = mir.and.i64 %97, %113
    %115 = mir.shr.i64 %112, %114
    %116 = mir.mov_imm 1 : i64
    %117 = mir.and.i64 %115, %116
    %119 = mir.call @mm_scope_pop()
    mir.br __rc_edge_20_0()
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    %104 = mir.mul.i64 %97, %100
    %105 = mir.add.i64 %94, %104
    mir.br inlined_stdlib.__managed_mem_load_sized_0_0()
  inlined_stdlib.__managed_mem_load_sized_0_0:
    %120 = mir.mov_imm 1 : i64
    %121 = mir.cmp eq, %100, %120
    mir.cond_br %121 [then: inlined_stdlib.__managed_mem_load_sized_1_0(), else: inlined_stdlib.__managed_mem_load_sized_2_0()]
  inlined_stdlib.__managed_mem_load_sized_1_0:
    %122 = mir.mov_imm 0 : i64
    %123 = mir.load_byte %105, %122
    mir.br inline_cont_main_21(%123)
  inlined_stdlib.__managed_mem_load_sized_2_0:
    %124 = mir.mov_imm 2 : i64
    %125 = mir.cmp eq, %100, %124
    mir.cond_br %125 [then: inlined_stdlib.__managed_mem_load_sized_3_0(), else: inlined_stdlib.__managed_mem_load_sized_4_0()]
  inlined_stdlib.__managed_mem_load_sized_3_0:
    %126 = mir.load %105, 0 width: halfword
    mir.br inline_cont_main_21(%126)
  inlined_stdlib.__managed_mem_load_sized_4_0:
    %127 = mir.mov_imm 4 : i64
    %128 = mir.cmp eq, %100, %127
    mir.cond_br %128 [then: inlined_stdlib.__managed_mem_load_sized_5_0(), else: inlined_stdlib.__managed_mem_load_sized_6_0()]
  inlined_stdlib.__managed_mem_load_sized_5_0:
    %129 = mir.load %105, 0 width: word
    mir.br inline_cont_main_21(%129)
  inlined_stdlib.__managed_mem_load_sized_6_0:
    %130 = mir.load %105, 0 width: qword
    mir.br inline_cont_main_21(%130)
  inline_cont_main_21(%131: i64):
    mir.br __rc_edge_30_0()
  inline_cont_main_3(%107: i64):
    %93 = mir.call @mm_scope_pop()
    %27 = mir.load %107, 0 width: qword
    %28 = mir.add.i64 %61, %27
    %132 = mir.call @__mm_decref_maybenull_helper(%107)
    mir.br inlined_ArrayIterator.advance_0_0()
  each_0.exit(%62: i64):
    %30 = mir.load %7, 0 width: qword
    %31 = mir.global_addr @__layout_Array_Box
    %32, %33 = mir.try_call @Array.pop(%30, %31)
    %35 = mir.cmp ne, %33, %0
    mir.cond_br %35 [then: try_0.otherwise(), else: try_0.merge()]
  try_0.otherwise:
    %142 = mir.call @__mm_decref_maybenull_helper(%7)
    %37 = mir.mov_imm 98 : i64
    %137 = mir.call @mm_scope_pop()
    mir.ret %37
  try_0.merge:
    %40 = mir.load %7, 8 width: qword
    %41 = mir.mov_imm 1 : i64
    %42 = mir.global_addr @__layout_Map_Integer_Box
    %43 = mir.global_addr @__witness_int_Hashable
    %44 = mir.global_addr @__witness_int_Equatable
    %45, %46 = mir.try_call @Map.get(%40, %41, %42, %43, %44)
    %48 = mir.cmp ne, %46, %0
    mir.cond_br %48 [then: try_1.otherwise(), else: try_1.merge()]
  try_1.otherwise:
    %144 = mir.call @__mm_decref_maybenull_helper(%32)
    %143 = mir.call @__mm_decref_maybenull_helper(%7)
    %50 = mir.mov_imm 99 : i64
    %138 = mir.call @mm_scope_pop()
    mir.ret %50
  try_1.merge:
    %54 = mir.load %45, 0 width: qword
    %55 = mir.add.i64 %62, %54
    %133 = mir.call @__mm_decref_maybenull_helper(%45)
    %134 = mir.call @__mm_decref_maybenull_helper(%7)
    %57 = mir.load %32, 0 width: qword
    %58 = mir.add.i64 %55, %57
    %135 = mir.call @__mm_decref_maybenull_helper(%32)
    %139 = mir.call @mm_scope_pop()
    %147 = mir.mov_imm 125 : i64
    %148 = mir.cmp ugt, %58, %147
    mir.cond_br %148 [then: __range_panic_0(), else: __range_ok_0()]
  __rc_edge_14_0:
    %140 = mir.call @__mm_decref_maybenull_helper(%70)
    mir.br each_0.exit(%0)
  __rc_edge_18_0:
    %141 = mir.call @__mm_decref_maybenull_helper(%70)
    mir.br each_0.exit(%28)
  __rc_edge_20_0:
    %145 = mir.call @stdlib.__mm_incref(%117)
    mir.br inline_cont_main_3(%117)
  __rc_edge_30_0:
    %146 = mir.call @stdlib.__mm_incref(%131)
    mir.br inline_cont_main_3(%131)
  __range_panic_0:
    %149 = mir.global_addr @__panic_msg_7b7dd3d0727e68ae
    %150 = mir.call @mrt_panic(%149)
  __range_ok_0:
    mir.ret %58
  }
}

```

<!-- test: rung-43-command-line-args -->
`CommandLine.args()` returns a freshly-built `StringArray` (`Array with String`) — one `String` per process
argument, each wrapping a `__ManagedMemory` over the UTF-8 bytes the platform argv accessor returns. This rung
guards the WHOLE argv lifecycle against leaks: the array + every element `String` + its inner `__ManagedMemory`
+ raw buffer must all free at the array's drop, AND — the bug this rung was written for — the platform-level
argv CACHE itself. On x64-Windows the argv bytes come from `mrt_win32_get_argc`, which lazily `mrt_alloc`s a
process-lifetime pointer array plus one UTF-8 buffer per argument and stashes them in globals; nothing ever
freed them, so every `CommandLine.args()` program read as `argc + 1` permanent `mrt_alloc` leaks (exit 101).
The fix is a teardown counterpart `mrt_win32_free_argv_cache` (hand-emitted alongside the other `mrt_win32_*`
helpers) that `__mm_decref_maybenull_helper`s each cached buffer and the array; `patchMrtStartFreeArgvCache`
injects a call to it into `mrt_start` just before the leak check, so the cache is freed exactly once at exit.

No `MmTrace`/`RequiredIR` is pinned here: the argv buffer SIZES and the argv[0] path are machine-dependent, so
an exact trace would not be portable. The LEAK GATE (the spec runner's `mmAllocCount` delta → exit 101) is the
load-bearing oracle — it is precisely what the cache leak tripped. The rung runs with no extra arguments, so
`args.count()` is 1 (just argv[0]); indexing each entry and summing byte lengths exercises the element borrow
without depending on argv content, and the result is collapsed so the exit code stays a deterministic 0.
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	var total = 0
	var i = 0
	while i < args.count() 'scan'
		let arg = try args.get(i) otherwise return 1
		total = total + arg.byteLength()
		i = i + 1
	end 'scan'
	// argv[0] is always present and non-empty, so `total` is positive on every
	// platform; collapse it to a deterministic exit code independent of the path.
	if total > 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```




