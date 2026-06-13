---
feature: mm-ladder
status: selfhosted
keywords: [refcount, ownership, leak, memory, destructor, lifetime]
category: memory
---
# Memory-Management Ladder

## Documentation

A graduated, test-driven regression ladder for the self-hosted compiler's ownership and
memory-management machinery. Each rung isolates ONE ownership/MM behavior, starting from the absolute
floor and adding one mechanism at a time.

**Oracles (added as the implementation gains the capability):**
- **exit code** — every rung (the program must produce the right answer).
- **leak gate** — the spec runner snapshots `__Builtins.mmAllocCount()` around each test; a grown count
  is exit 101. Every rung must be leak-free.
- **mm-trace** (`<!-- MmTrace -->` + a `stderr` block) — added once the alloc-at-0 / mm-trace runtime is
  ported in. The exact `mm_alloc`/`incref`/`decref`/`free` tree; the strongest MM oracle.
- **RequiredIR** (`RequiredIR:x64-windows` / `:arm64-macos`) — pinned via `--update-required` once a
  rung's lowering is stable.

**Workflow.** Rungs are authored `disabled-test` and enabled ONE AT A TIME in ladder order (`rung-NN-…`).
Run with the SELF-HOSTED compiler (`maxon-selfhosted.exe spec-test --filter=mm-ladder`). Each passing rung
is a permanent regression test — never weaken a locked rung to make a later change pass; fix the change.
Real rebuild via `./bin/maxon.exe build maxon-selfhosted`; exit code 101 = a per-test leak.

## Tests

### Group 0 — Floor (no managed allocation)

<!-- test: rung-00-no-alloc -->
<!-- MmTrace -->
The absolute floor: a program that allocates nothing managed. Proves the harness, exit code, and
leak gate all work before any memory management is involved. The mm-trace is empty — no managed
allocation, so no `mm_alloc`/`incref`/`decref`/`free` lines.
```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```stderr

```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.xor r8d, r8d
    x64.epilogue
    x64.ret
  }
}

```

### Group 1 — Core value lifetimes

<!-- test: rung-01-single-alloc-freed -->
<!-- MmTrace -->
A single heap struct, allocated, used once, and freed at its last use — the simplest managed lifetime.
Must be leak-free.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	let b = Box.create(7)
	return b.value
end 'main'
```
```exitcode
7
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_drop Box #1 [main]
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
    x64.lea r12, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov r8d, 7
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r13, [r12+0] (8b)
    x64.mov rcx, r12
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_5b074d262f8db1ca]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-02-alias-incref -->
<!-- MmTrace -->
Aliasing a managed value (`let b = a`) shares ONE heap object: freed exactly once. No second alloc, no
leak, no double-free.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create(11)
	let b = a
	return b.value
end 'main'
```
```exitcode
11
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_drop Box #1 [main]
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
    x64.lea r12, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov r8d, 11
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r13, [r12+0] (8b)
    x64.mov rcx, r12
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_1e47caf1e02e76c3]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-03-reassign-decrefs-old -->
<!-- MmTrace -->
Reassigning a managed `var` frees the OLD occupant before the new value takes the slot. Two allocs, two
frees, no leak.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	var b = Box.create(1)
	b = Box.create(2)
	return b.value
end 'main'
```
```exitcode
2
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_drop Box #1 [Box.create]
    mm_free Box #1
mm_alloc Box #2 size=8 [Box.create]
mm_move Box #2 -> return [Box.create]
mm_drop Box #2 [main]
    mm_free Box #2
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
    x64.mov r8d, 1
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov rcx, 8
    x64.mov rdx, r15
    x64.mov rax, r14
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov r8, 1
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.mov rcx, r12
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.lea r12, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov r8d, 2
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r13, [r12+0] (8b)
    x64.mov rcx, r12
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_fa747f3bc64caedb]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-04-return-transfers-ownership -->
<!-- MmTrace -->
Returning a freshly-allocated managed value transfers ownership to the caller — freed once in `main`,
not double-freed across the call boundary.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function makeBox(n Integer) returns Box
	let local = Box.create(n)
	return local
end 'makeBox'

function main() returns ExitCode
	let b = makeBox(5)
	return b.value
end 'main'
```
```exitcode
5
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_incref Box #1 rc=1 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_move Box #1 -> return [makeBox]
mm_decref Box #1 rc=0 [main]
  mm_free Box #1
```
```RequiredIR:x64-windows
module {
  func @makeBox(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=16
    x64.mov r12, rcx
    x64.lea r13, [rip+__mtstr_scope_makeBox]
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
    x64.mov [r13+0], r12 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov ecx, 5
    x64.call makeBox
    x64.mov r12, [r8+0] (8b)
    x64.mov rcx, r8
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_470a54a8df2172f0]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-05-param-borrow -->
<!-- MmTrace -->
A function parameter is borrowed: the caller retains ownership across the call. One alloc, one free, both
in `main`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function readBox(b Box) returns Integer
	return b.value
end 'readBox'

function main() returns ExitCode
	let b = Box.create(9)
	return readBox(b)
end 'main'
```
```exitcode
9
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_drop Box #1 [main]
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
    x64.lea r12, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov r8d, 9
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.lea r13, [rip+__mtstr_scope_readBox]
    x64.mov rcx, r13
    x64.call mm_scope_push
    x64.mov r13, [r12+0] (8b)
    x64.call mm_scope_pop
    x64.mov rcx, r12
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r13, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_fd3c86066f946dce]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
}

```

### Group 2 — Scope & control flow

<!-- test: rung-06-if-else-both-assign -->
<!-- MmTrace -->
A managed local is reassigned inside BOTH arms of an if/else: the original `Box #1` is dropped at the
reassignment, and whichever arm runs produces `Box #2` that lives to the end and is freed in `main`. Proves
the scope stack and refcount inserter stay balanced across a diamond CFG.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	var b = Box.create(1)
	if b.value > 0 'pos'
		b = Box.create(2)
	end 'pos' else 'neg'
		b = Box.create(3)
	end 'neg'
	return b.value
end 'main'
```
```exitcode
2
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_drop Box #1 [main]
    mm_free Box #1
mm_alloc Box #2 size=8 [Box.create]
mm_incref Box #2 rc=1 [Box.create]
mm_move Box #2 -> return [Box.create]
mm_move Box #2 -> phi [main]
mm_decref Box #2 rc=0 [main]
  mm_free Box #2
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
    x64.mov r8d, 1
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r13, [r12+0] (8b)
    x64.mov rcx, r12
    x64.call mm_drop
    x64.test r13, r13
    x64.jle neg_0
  pos_0:
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
    x64.mov r8d, 2
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r12
    x64.call mm_move_phi
    x64.mov rcx, r12
    x64.jmp pos_0.merge
  neg_0:
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
    x64.mov r8d, 3
    x64.mov [r12+0], r8 (8b)
    x64.mov rcx, r12
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov rcx, r12
    x64.call mm_move_phi
    x64.mov rcx, r12
  pos_0.merge:
    x64.mov r12, [rcx+0] (8b)
    x64.call __mm_decref_maybenull_helper
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_12a8894afa67b273]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-07-loop-iteration-freed -->
<!-- MmTrace -->
A managed local allocated INSIDE a loop body is freed at the end of EACH iteration (not accumulated): three
iterations → three alloc/free pairs, all in `main`, leak-free.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	var sum = 0
	for i in 1 to 3 'loop'
		let b = Box.create(i)
		sum = sum + b.value
	end 'loop'
	return sum
end 'main'
```
```exitcode
6
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_drop Box #1 [main]
    mm_free Box #1
mm_alloc Box #2 size=8 [Box.create]
mm_move Box #2 -> return [Box.create]
mm_drop Box #2 [main]
    mm_free Box #2
mm_alloc Box #3 size=8 [Box.create]
mm_move Box #3 -> return [Box.create]
mm_drop Box #3 [main]
    mm_free Box #3
```
```RequiredIR:x64-windows
module {
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov r8d, 1
    x64.xor r9d, r9d
    x64.mov r12, r9
    x64.mov r13, r8
  loop_0.header:
    x64.cmp r13, 3
    x64.jg loop_0.exit
  loop_0:
    x64.lea r14, [rip+__mtstr_scope_Box_create]
    x64.mov rcx, r14
    x64.call mm_scope_push
    x64.mov eax, 60
    x64.xor edx, edx
    x64.mov ecx, 8
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov [r14+0], r13 (8b)
    x64.mov rcx, r14
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.mov r15, [r14+0] (8b)
    x64.mov rcx, r14
    x64.call mm_drop
    x64.add r12, r15
  loop_0.step:
    x64.add r13, 1
    x64.jmp loop_0.header
  loop_0.exit:
    x64.call mm_scope_pop
    x64.mov r8d, 4294967295
    x64.cmp r12, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_ddd5203208018c1a]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  }
}

```

<!-- test: rung-08-return-from-inner-scope -->
<!-- MmTrace -->
A managed local in a callee is dropped before an EARLY return from an inner scope. `pick` allocates a Box,
returns a plain int out of an `if`, and the Box is freed in `pick` (its scope) on that path — proving
scope-exit cleanup fires on a non-tail return path.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function pick(n Integer) returns Integer
	let b = Box.create(n)
	if n > 5 'big'
		return b.value
	end 'big'
	return 0
end 'pick'

function main() returns ExitCode
	return pick(9)
end 'main'
```
```exitcode
9
```
```stderr
mm_alloc Box #1 size=8 [Box.create]
mm_move Box #1 -> return [Box.create]
mm_drop Box #1 [pick]
    mm_free Box #1
```
```RequiredIR:x64-windows
module {
  func @pick(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=16
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
    x64.mov [r13+0], r12 (8b)
    x64.mov rcx, r13
    x64.call mm_move_return
    x64.call mm_scope_pop
    x64.cmp r12, 5
    x64.jle big_0.after
  big_0:
    x64.mov r12, [r13+0] (8b)
    x64.mov rcx, r13
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.mov r8, r12
    x64.epilogue
    x64.ret
  big_0.after:
    x64.mov rcx, r13
    x64.call mm_drop
    x64.call mm_scope_pop
    x64.xor r8d, r8d
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.lea r12, [rip+__mtstr_scope_main]
    x64.mov rcx, r12
    x64.call mm_scope_push
    x64.mov ecx, 9
    x64.call pick
    x64.mov r9d, 4294967295
    x64.cmp r8, r9
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea r12, [rip+__panic_msg_aca19bd80fd12e88]
    x64.mov rcx, r12
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}

```
