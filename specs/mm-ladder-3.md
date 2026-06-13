---
feature: mm-ladder-3
status: selfhosted
keywords: [refcount, ownership, leak, memory, destructor, phi, return, loop]
category: memory
---
# Memory-Management Ladder — value lifetimes, part 2 (branch-return & loop-carried)

## Documentation

A continuation of [the core MM ladder](mm-ladder.md) (rungs 00–08 cover the basic value lifetimes:
alloc/free, alias, reassign-decrefs-old, return-transfer, param-borrow, if/else diamond, loop-body,
early-return). These rungs isolate the harder phi/return and loop-carried ownership shapes the basic
diamond did not reach: a managed value RETURNED from one branch but DROPPED on the sibling (the
`moveIntoPhi` × `returnTransfer` interaction — the return-transfer suppression must be PATH-SENSITIVE, not
value-global), and a managed value that SURVIVES a loop via a loop-carried phi (reassign each pass, read
after). Split into its own file so its per-fragment mm-trace + RequiredIR regeneration stays well under the
spec runner's per-worker 60s timeout.

**Oracles** (same as the core ladder): exit code, leak gate (`mmAllocCount` delta → exit 101),
`<!-- MmTrace -->` + a `stderr` block (the exact alloc/incref/decref/free tree — the strongest oracle;
the mm-trace runtime PANICS on a `(null)` / garbage tag, so a use-after-free that reads a freed header
fails loudly), and `RequiredIR:x64-windows` pinned via `--update-required`. ALWAYS hand-review a regenerated
trace before locking — the leak gate cannot see a UAF that reads freed-not-yet-reused memory; the mm-trace can.

**Workflow.** Author `disabled-test`, enable ONE at a time, drive to exit-correct + leak-free + a
hand-verified trace, then `--update-required` to pin. A passing rung is a permanent regression test —
never weaken it to make a later change pass; fix the change. Run via
`maxon-selfhosted.exe spec-test --filter=mm-ladder-3`.

## Tests

<!-- test: rung-31-managed-returned-from-one-arm -->
<!-- MmTrace -->
Two managed locals (`a`, `b`) are created, then ONE is returned and the OTHER dropped, depending on a flag:
on the `flag` arm `a` transfers out via `return` and `b` must be dropped; on the fall-through arm `b`
transfers out and `a` must be dropped. Each value is RETURNED on one path and DIES on the sibling — the
`moveIntoPhi` × `returnTransfer` interaction. The return-transfer suppression must therefore be
PATH-SENSITIVE: a value-global "this value is returned, never release it" rule leaks the sibling that is
dropped on the non-returning arm (both `a` and `b` are returned on SOME path). Here the inserter acquires
each value's +1 at its def (it escapes via the return on one arm) and releases it at the terminator that
does NOT return it; the returning terminator transfers the +1 to the caller. `main` calls with `flag=true`,
so `a` (7) is returned and `b` (9) is dropped inside `pick`; the returned `a` is then dropped in `main`
after its value is read. Two `Box`es, each freed exactly once; no leak, no double-free.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function pick(flag bool) returns Box
	let a = Box.create(7)
	let b = Box.create(9)
	if flag 'br'
		return a
	end 'br'
	return b
end 'pick'

function main() returns ExitCode
	let chosen = pick(true)
	return chosen.value
end 'main'
```
```exitcode
7
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_incref Box #1 rc=1 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_alloc Box #2 size=8 [Box.create]
mm_incref Box #2 rc=1 [Box.create]
mm_move Box #2 -> return [Box.create]
mm_decref Box #2 rc=0 [pick]
  mm_free Box #2
mm_move Box #1 -> return [pick]
mm_decref Box #1 rc=0 [main]
  mm_free Box #1
```
```RequiredIR:x64-windows
module {
  func @pick(rcx: i1) -> i64 {
  entry:
    x64.prologue stack_size=32
    x64.mov r12, rcx
    x64.lea r13, [rip+__mtstr_scope_pick]
    x64.mov rcx, r13
    x64.call mm_scope_push
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
    x64.mov r8d, 7
    x64.mov [r13+0], r8 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
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
    x64.test r12, r12
    x64.je br_0.after
  br_0:
    x64.mov rcx, r14
    x64.call stdlib.__mm_decref
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  br_0.after:
    x64.mov rcx, r13
    x64.call stdlib.__mm_decref
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r8, r14
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov ecx, 1
    x64.call pick
    x64.mov r12, [r8+0] (8b)
    x64.mov rcx, r8
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_ef2a39cbd94f687b]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-32-loop-carried-managed-var -->
<!-- MmTrace -->
An OUTER managed `var acc` is reassigned each pass of a `while` loop and SURVIVES the loop via a loop-carried
phi, then is read after. Unlike rung-07 (a managed local allocated and freed INSIDE the loop body each
iteration), here the value crosses the back-edge: each pass `acc = Box.create(acc.value + …)` produces a new
`Box` that is MOVED into the loop-carried block-arg (the phi), and the PREVIOUS occupant is decref'd at that
reassignment (reassign-decrefs-old, rung-03, across a back-edge). Four `Box`es are created — the initial
(value 0) plus one per iteration — and the first three are each freed at the reassignment that overwrites
them; the final survivor (value 6) is freed after its `.value` is read past the loop. Every allocation freed
exactly once; the loop-carried phi neither leaks the survivor nor double-frees an overwritten box.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	var acc = Box.create(0)
	var i = 0
	while i < 3 'accumulate'
		acc = Box.create(acc.value + i + 1)
		i = i + 1
	end 'accumulate'
	return acc.value
end 'main'
```
```exitcode
6
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_incref Box #1 rc=1 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_move Box #1 -> phi [main]
mm_alloc Box #2 size=8 [Box.create]
mm_incref Box #2 rc=1 [Box.create]
mm_move Box #2 -> return [Box.create]
mm_move Box #2 -> phi [main]
mm_decref Box #1 rc=0 [main]
  mm_free Box #1
mm_alloc Box #3 size=8 [Box.create]
mm_incref Box #3 rc=1 [Box.create]
mm_move Box #3 -> return [Box.create]
mm_move Box #3 -> phi [main]
mm_decref Box #2 rc=0 [main]
  mm_free Box #2
mm_alloc Box #4 size=8 [Box.create]
mm_incref Box #4 rc=1 [Box.create]
mm_move Box #4 -> return [Box.create]
mm_move Box #4 -> phi [main]
mm_decref Box #3 rc=0 [main]
  mm_free Box #3
mm_decref Box #4 rc=0 [main]
  mm_free Box #4
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
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
    x64.xor r8d, r8d
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r12
    x64.call mm_move_phi
    x64.xor r8d, r8d
    x64.mov r13, r8
  accumulate_0.header:
    x64.cmp r13, 3
    x64.jge accumulate_0.exit
  accumulate_0:
    x64.mov r14, [r12+0] (8b)
    x64.lea r15, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r15
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r15, r8
    x64.mov rcx, r15
    x64.call stdlib.__mm_incref
    x64.add r14, r13
    x64.add r14, 1
    x64.mov [r15+0], r14 (8b)
    x64.mov rcx, r15
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r15
    x64.call mm_move_phi
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.add r13, 1
    x64.mov r12, r15
    x64.jmp accumulate_0.header
  accumulate_0.exit:
    x64.mov r13, [r12+0] (8b)
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_307640d376df0d0d]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

