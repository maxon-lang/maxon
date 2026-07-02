---
feature: optimizer-refcount
status: selfhosted
keywords: [refcount, incref, decref, optimization, mm-trace, managed-memory, whole-program]
category: compiler
---

# Refcount Optimization Baseline

## Documentation

This spec is the regression harness and visible scoreboard for the refcount
optimizer. It holds one whole-program test that exercises a wide variety of
patterns known to produce `mm_incref` / `mm_decref` traffic:

- struct aliasing
- short-lived temporaries passed into functions
- loop-carried container pushes
- nested containers
- function parameter passing (caller incref / callee scope-end decref)
- return-ownership transfer (factory pattern)
- struct field reassignment
- union-with-managed-payload matching
- closure capturing a managed value

The committed `stderr` block is the full `--mm-trace` output at the time the
baseline was generated; the `RequiredIR:x64-windows` block is the full IR
dump at every pipeline stage. Neither block should be hand-written — both are
regenerated via `maxon spec-test --filter=optimizer-refcount --update-required`.

When a refcount optimization lands, both blocks will change. The diff **is**
the measured impact: fewer lines in `stderr` means fewer runtime
increfs/decrefs; fewer `mm_incref` / `mm_decref` ops in the IR confirms the
optimizer (not just runtime folding) was responsible. Reviewing the diff is
how we keep the pass correct — the set of `mm_alloc` / `mm_free` must stay
identical, and every object must still reach `rc=0`.

The program is deliberately larger than a typical spec test: future
whole-program / interprocedural passes need multi-function call graphs,
cross-function ownership flow, and nested scopes all present at once to have
anything meaningful to optimize.

## Tests

<!-- test: refcount-baseline-whole-program -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias StringArray = Array with String
typealias Matrix = Array with IntArray
typealias PointArray = Array with Point

typealias FnTypeAlias1 = function(Integer) returns Integer

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Person
	export var name as String
	export var age as Integer

	static function create(name String, age Integer) returns Self
		return Self{name: name, age: age}
	end 'create'
end 'Person'

union Shape
	circle(label String)
	square(label String)
	blank
end 'Shape'

function sum_point(p Point) returns Integer
	return p.x + p.y
end 'sum_point'

function make_point(x Integer, y Integer) returns Point
	return Point.create(x, y: y)
end 'make_point'

function describe(s Shape) returns Integer
	return match s 'describe'
		circle(label) gives label.count()
		square(label) gives label.count()
		blank gives 0
	end 'describe'
end 'describe'

function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function names_total(arr StringArray) returns Integer
	return arr.count()
end 'names_total'

function row_total(arr IntArray) returns Integer
	var sum = 0
	for v in arr 'iter'
		sum = sum + v
	end 'iter'
	return sum
end 'row_total'

function matrix_total(m Matrix) returns Integer
	var sum = 0
	for row in m 'iter'
		sum = sum + row_total(row)
	end 'iter'
	return sum
end 'matrix_total'

function points_x_sum(pts PointArray) returns Integer
	var sum = 0
	for p in pts 'iter'
		sum = sum + p.x
	end 'iter'
	return sum
end 'points_x_sum'

function main() returns ExitCode
	var total = 0

	// --- section 1: struct literal + alias ---
	var a = Point.create(1, y: 2)
	var b = a
	b.x = 99
	a = b
	total = total + a.x

	// --- section 2: short-lived temp passed to function ---
	total = total + sum_point(Point.create(3, y: 4))
	total = total + sum_point(Point.create(5, y: 6))

	// --- section 3: loop-carried container pushes ---
	var names = StringArray.create()
	for i in 0 upto 5 'names_loop'
		names.push("name_{i}")
	end 'names_loop'
	total = total + names_total(names)

	// --- section 4: nested container ---
	var row1 = IntArray.create()
	row1.push(1)
	row1.push(2)
	var row2 = IntArray.create()
	row2.push(3)
	row2.push(4)
	var matrix = Matrix.create()
	matrix.push(row1)
	matrix.push(row2)
	total = total + matrix_total(matrix)

	// --- section 5: function parameter passing ---
	var origin = Point.create(0, y: 0)
	total = total + sum_point(origin)
	total = total + sum_point(origin)

	// --- section 6: return-ownership transfer (factory) ---
	let made = make_point(10, y: 20)
	total = total + made.x

	// --- section 7: struct field reassignment ---
	var person = Person.create("alice", age: 30)
	person.name = "bob"
	person.name = "carol"
	total = total + person.age

	// --- section 8: union with managed payload ---
	let shape1 = Shape.circle("ring")
	let shape2 = Shape.square("box")
	let shape3 = Shape.blank
	total = total + describe(shape1)
	total = total + describe(shape2)
	total = total + describe(shape3)

	// --- section 9: closure capturing a managed value ---
	let prefix = "pfx_"
	let builder = function(n Integer) gives "{prefix}{n}".count()
	total = total + apply(builder, x: 7)
	total = total + apply(builder, x: 8)

	// --- section 10: for-in over managed elements, primitive body ---
	// exercises the for-in lowering pattern (__forin_result + user var alias)
	var points = PointArray.create()
	points.push(Point.create(1, y: 2))
	points.push(Point.create(3, y: 4))
	points.push(Point.create(5, y: 6))
	total = total + points_x_sum(points)

	// --- section 11: in-loop try-alias + borrow-call bracket ---
	// Inside each iteration, a try-binding creates an implicit alias between
	// the try-result slot and the user-visible `p`. The emitter brackets the
	// subsequent borrowed use with an incref/decref on `p`. Loop-invariant
	// elimination collapses the bracket: the try-result owns the rc=1
	// transferred reference and the direct call is borrow-only, so the
	// extra +1/-1 is pure overhead.
	var triplet = PointArray.create()
	triplet.push(Point.create(7, y: 8))
	triplet.push(Point.create(9, y: 10))
	triplet.push(Point.create(11, y: 12))
	for i in 0 upto 3 'alias_loop'
		let p = try triplet.get(i) otherwise 'missErr'
			panic("alias_loop: triplet.get({i}) invariant violated")
		end 'missErr'
		total = total + sum_point(p)
	end 'alias_loop'

	// Prevent optimizer from eliminating the work — but exit 0.
	if total < 0 'guard'
		return 1
	end 'guard'
	return 0
end 'main'
```
```exitcode
0
```
```RequiredIR:x64-windows
module {
  func @describe(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=16
    x64.mov rbx, rcx
    x64.mov r9, [rbx+0] (8b)
    x64.xor r8d, r8d
    x64.test r9, r9
    x64.jne describe_0.next0
    x64.jmp describe_0.case0
  describe_0.merge:
    x64.epilogue
    x64.ret
  describe_0.case0:
    x64.mov rcx, [rbx+8] (8b)
    x64.call String.count
    x64.jmp describe_0.merge
  describe_0.next0:
    x64.mov r9, [rbx+0] (8b)
    x64.cmp r9, 1
    x64.jne describe_0.next1
  describe_0.case1:
    x64.mov rcx, [rbx+8] (8b)
    x64.call String.count
    x64.jmp describe_0.merge
  describe_0.next1:
    x64.mov r9, [rbx+0] (8b)
    x64.cmp r9, 2
    x64.jne describe_0.merge
  describe_0.case2:
    x64.xor r8d, r8d
    x64.jmp describe_0.merge
  }
  func @row_total(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=48
    x64.lea rdx, [rip+__layout_Array_Integer]
    x64.xor ebx, ebx
  inlined_Array.createIterator_0_0:
    x64.mov r8, [rcx+0] (8b)
    x64.mov rcx, r8
    x64.call ArrayIterator.create
    x64.test rdx, rdx
    x64.je inlined_Array.createIterator_2_0
  inlined_Array.createIterator_1_0:
    x64.xor ecx, ecx
    x64.mov r12, rcx
    x64.jmp inline_cont_row_total_0
  inlined_Array.createIterator_2_0:
    x64.xor r9d, r9d
    x64.mov r12, r8
    x64.mov rdx, r9
  inline_cont_row_total_0:
    x64.test rdx, rdx
    x64.je __phi_trampoline_8_0
    x64.jmp __rc_edge_8_0
  inlined_ArrayIterator.advance_0_0:
    x64.mov r8, [r12+0] (8b)
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
  inline_cont_row_total_1:
    x64.test rdx, rdx
    x64.je __phi_trampoline_12_0
    x64.jmp __rc_edge_12_0
  iter_0:
    x64.mov r8, [r12+0] (8b)
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    x64.mov rsi, r8
    x64.add rsi, 24
    x64.mov rdi, [rsi+0] (8b)
    x64.mov rsi, r8
    x64.add rsi, 8
    x64.mov r13, [rsi+0] (8b)
    x64.mov rsi, [r8+0] (8b)
    x64.test rdi, rdi
    x64.jne inlined_stdlib.__managed_mem_cursor_current_2_0
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    x64.mov ecx, 3
    x64.mov r8, r13
    x64.shr r8, r8, rcx
    x64.xor edi, edi
    x64.add rsi, r8
    x64.mov [rbp-8], rsi
    x64.mov [rbp-16], rdi
    x64.movzx rax, byte ptr [rax+0]
    x64.mov r8, [rbp-24]
    x64.mov esi, 7
    x64.mov rdi, r13
    x64.and rdi, rsi
    x64.mov esi, 1
    x64.mov rcx, rdi
    x64.shr r8, r8, rcx
    x64.and r8, rsi
    x64.jmp inline_cont_row_total_3
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    x64.imul r13, rdi
    x64.add rsi, r13
  inlined_stdlib.__managed_mem_load_sized_0_0:
    x64.cmp rdi, 1
    x64.jne inlined_stdlib.__managed_mem_load_sized_2_0
  inlined_stdlib.__managed_mem_load_sized_1_0:
    x64.xor r8d, r8d
    x64.mov [rbp-8], rsi
    x64.mov [rbp-16], r8
    x64.movzx rax, byte ptr [rax+0]
    x64.mov r8, [rbp-24]
    x64.jmp inline_cont_row_total_2
  inlined_stdlib.__managed_mem_load_sized_2_0:
    x64.cmp rdi, 2
    x64.jne inlined_stdlib.__managed_mem_load_sized_4_0
  inlined_stdlib.__managed_mem_load_sized_3_0:
    x64.movzx r8, [rsi+0] (2b)
    x64.jmp inline_cont_row_total_2
  inlined_stdlib.__managed_mem_load_sized_4_0:
    x64.cmp rdi, 4
    x64.jne inlined_stdlib.__managed_mem_load_sized_6_0
  inlined_stdlib.__managed_mem_load_sized_5_0:
    x64.mov r8, [rsi+0] (4b)
    x64.jmp inline_cont_row_total_2
  inlined_stdlib.__managed_mem_load_sized_6_0:
    x64.mov r8, [rsi+0] (8b)
  inline_cont_row_total_2:
  inline_cont_row_total_3:
    x64.mov r13, r9
    x64.add r13, r8
    x64.jmp inlined_ArrayIterator.advance_0_0
  iter_0.exit:
    x64.epilogue
    x64.ret
  __rc_edge_8_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, rbx
    x64.jmp iter_0.exit
  __rc_edge_12_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, r13
    x64.jmp iter_0.exit
  __phi_trampoline_8_0:
    x64.mov r9, rbx
    x64.jmp iter_0
  __phi_trampoline_9_0:
    x64.mov rdx, r8
    x64.jmp inline_cont_row_total_1
  __phi_trampoline_12_0:
    x64.mov r9, r13
    x64.jmp iter_0
  }
  func @matrix_total(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=64
    x64.lea rdx, [rip+__layout_Array_IntArray]
    x64.xor ebx, ebx
  inlined_Array.createIterator_0_0:
    x64.mov r8, [rcx+0] (8b)
    x64.mov rcx, r8
    x64.call ArrayIterator.create
    x64.test rdx, rdx
    x64.je inlined_Array.createIterator_2_0
  inlined_Array.createIterator_1_0:
    x64.xor ecx, ecx
    x64.mov r12, rcx
    x64.jmp inline_cont_matrix_total_0
  inlined_Array.createIterator_2_0:
    x64.xor r9d, r9d
    x64.mov r12, r8
    x64.mov rdx, r9
  inline_cont_matrix_total_0:
    x64.test rdx, rdx
    x64.je __phi_trampoline_8_0
    x64.jmp __rc_edge_8_0
  inlined_ArrayIterator.advance_0_0:
    x64.mov r8, [r12+0] (8b)
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
  inline_cont_matrix_total_1:
    x64.test rdx, rdx
    x64.je iter_0
    x64.jmp __rc_edge_12_0
  iter_0:
    x64.mov r8, [r12+0] (8b)
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    x64.mov r9, r8
    x64.add r9, 24
    x64.mov rsi, [r9+0] (8b)
    x64.mov r9, r8
    x64.add r9, 8
    x64.mov rdi, [r9+0] (8b)
    x64.mov r9, [r8+0] (8b)
    x64.test rsi, rsi
    x64.jne inlined_stdlib.__managed_mem_cursor_current_2_0
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    x64.mov ecx, 3
    x64.mov r8, rdi
    x64.shr r8, r8, rcx
    x64.xor esi, esi
    x64.add r9, r8
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], rsi
    x64.movzx rax, byte ptr [rax+0]
    x64.mov r8, [rbp-24]
    x64.mov r9d, 7
    x64.mov rsi, rdi
    x64.and rsi, r9
    x64.mov r9d, 1
    x64.mov rcx, rsi
    x64.shr r8, r8, rcx
    x64.and r8, r9
    x64.jmp __rc_edge_14_0
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    x64.imul rdi, rsi
    x64.add r9, rdi
  inlined_stdlib.__managed_mem_load_sized_0_0:
    x64.cmp rsi, 1
    x64.jne inlined_stdlib.__managed_mem_load_sized_2_0
  inlined_stdlib.__managed_mem_load_sized_1_0:
    x64.xor r8d, r8d
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], r8
    x64.movzx rax, byte ptr [rax+0]
    x64.mov rcx, [rbp-24]
    x64.jmp inline_cont_matrix_total_2
  inlined_stdlib.__managed_mem_load_sized_2_0:
    x64.cmp rsi, 2
    x64.jne inlined_stdlib.__managed_mem_load_sized_4_0
  inlined_stdlib.__managed_mem_load_sized_3_0:
    x64.movzx rcx, [r9+0] (2b)
    x64.jmp inline_cont_matrix_total_2
  inlined_stdlib.__managed_mem_load_sized_4_0:
    x64.cmp rsi, 4
    x64.jne inlined_stdlib.__managed_mem_load_sized_6_0
  inlined_stdlib.__managed_mem_load_sized_5_0:
    x64.mov rcx, [r9+0] (4b)
    x64.jmp inline_cont_matrix_total_2
  inlined_stdlib.__managed_mem_load_sized_6_0:
    x64.mov rcx, [r9+0] (8b)
  inline_cont_matrix_total_2:
    x64.jmp __rc_edge_24_0
  inline_cont_matrix_total_3:
    x64.mov rcx, r14
    x64.call row_total
    x64.mov r15, r8
    x64.mov rcx, r14
    x64.call __mm_decref_maybenull_helper
    x64.add r13, r15
    x64.jmp inlined_ArrayIterator.advance_0_0
  iter_0.exit:
    x64.epilogue
    x64.ret
  __rc_edge_8_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, rbx
    x64.jmp iter_0.exit
  __rc_edge_12_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, r13
    x64.jmp iter_0.exit
  __rc_edge_14_0:
    x64.mov r9, r8
    x64.sub r9, 8
    x64.lock inc qword ptr [r9]
    x64.mov r14, r8
    x64.jmp inline_cont_matrix_total_3
  __rc_edge_24_0:
    x64.mov r8, rcx
    x64.sub r8, 8
    x64.lock inc qword ptr [r8]
    x64.mov r14, rcx
    x64.jmp inline_cont_matrix_total_3
  __phi_trampoline_8_0:
    x64.mov r13, rbx
    x64.jmp iter_0
  __phi_trampoline_9_0:
    x64.mov rdx, r8
    x64.jmp inline_cont_matrix_total_1
  }
  func @points_x_sum(rcx: i64) -> i64 {
  entry:
    x64.prologue stack_size=64
    x64.lea rdx, [rip+__layout_Array_Point]
    x64.xor ebx, ebx
  inlined_Array.createIterator_0_0:
    x64.mov r8, [rcx+0] (8b)
    x64.mov rcx, r8
    x64.call ArrayIterator.create
    x64.test rdx, rdx
    x64.je inlined_Array.createIterator_2_0
  inlined_Array.createIterator_1_0:
    x64.xor ecx, ecx
    x64.mov r12, rcx
    x64.jmp inline_cont_points_x_sum_0
  inlined_Array.createIterator_2_0:
    x64.xor r9d, r9d
    x64.mov r12, r8
    x64.mov rdx, r9
  inline_cont_points_x_sum_0:
    x64.test rdx, rdx
    x64.je __phi_trampoline_8_0
    x64.jmp __rc_edge_8_0
  inlined_ArrayIterator.advance_0_0:
    x64.mov r8, [r12+0] (8b)
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
  inline_cont_points_x_sum_1:
    x64.test rdx, rdx
    x64.je iter_0
    x64.jmp __rc_edge_12_0
  iter_0:
    x64.mov r8, [r12+0] (8b)
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    x64.mov r9, r8
    x64.add r9, 24
    x64.mov rsi, [r9+0] (8b)
    x64.mov r9, r8
    x64.add r9, 8
    x64.mov rdi, [r9+0] (8b)
    x64.mov r9, [r8+0] (8b)
    x64.test rsi, rsi
    x64.jne inlined_stdlib.__managed_mem_cursor_current_2_0
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    x64.mov ecx, 3
    x64.mov r8, rdi
    x64.shr r8, r8, rcx
    x64.xor esi, esi
    x64.add r9, r8
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], rsi
    x64.movzx rax, byte ptr [rax+0]
    x64.mov r8, [rbp-24]
    x64.mov r9d, 7
    x64.mov rsi, rdi
    x64.and rsi, r9
    x64.mov r9d, 1
    x64.mov rcx, rsi
    x64.shr r8, r8, rcx
    x64.and r8, r9
    x64.jmp __rc_edge_14_0
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    x64.imul rdi, rsi
    x64.add r9, rdi
  inlined_stdlib.__managed_mem_load_sized_0_0:
    x64.cmp rsi, 1
    x64.jne inlined_stdlib.__managed_mem_load_sized_2_0
  inlined_stdlib.__managed_mem_load_sized_1_0:
    x64.xor r8d, r8d
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], r8
    x64.movzx rax, byte ptr [rax+0]
    x64.mov rcx, [rbp-24]
    x64.jmp inline_cont_points_x_sum_2
  inlined_stdlib.__managed_mem_load_sized_2_0:
    x64.cmp rsi, 2
    x64.jne inlined_stdlib.__managed_mem_load_sized_4_0
  inlined_stdlib.__managed_mem_load_sized_3_0:
    x64.movzx rcx, [r9+0] (2b)
    x64.jmp inline_cont_points_x_sum_2
  inlined_stdlib.__managed_mem_load_sized_4_0:
    x64.cmp rsi, 4
    x64.jne inlined_stdlib.__managed_mem_load_sized_6_0
  inlined_stdlib.__managed_mem_load_sized_5_0:
    x64.mov rcx, [r9+0] (4b)
    x64.jmp inline_cont_points_x_sum_2
  inlined_stdlib.__managed_mem_load_sized_6_0:
    x64.mov rcx, [r9+0] (8b)
  inline_cont_points_x_sum_2:
    x64.jmp __rc_edge_24_0
  inline_cont_points_x_sum_3:
    x64.mov r14, [rcx+0] (8b)
    x64.call __mm_decref_maybenull_helper
    x64.add r13, r14
    x64.jmp inlined_ArrayIterator.advance_0_0
  iter_0.exit:
    x64.epilogue
    x64.ret
  __rc_edge_8_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, rbx
    x64.jmp iter_0.exit
  __rc_edge_12_0:
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, r13
    x64.jmp iter_0.exit
  __rc_edge_14_0:
    x64.mov r9, r8
    x64.sub r9, 8
    x64.lock inc qword ptr [r9]
    x64.mov rcx, r8
    x64.jmp inline_cont_points_x_sum_3
  __rc_edge_24_0:
    x64.mov r8, rcx
    x64.sub r8, 8
    x64.lock inc qword ptr [r8]
    x64.jmp inline_cont_points_x_sum_3
  __phi_trampoline_8_0:
    x64.mov r13, rbx
    x64.jmp iter_0
  __phi_trampoline_9_0:
    x64.mov rdx, r8
    x64.jmp inline_cont_points_x_sum_1
  }
  func @main$closure_0(rcx: i64, rdx: i64) -> u64 {
  entry:
    x64.prologue stack_size=96
    x64.mov rbx, rcx
    x64.mov r8, [rdx+0] (8b)
    x64.mov r9, [r8+0] (8b)
    x64.mov r8, [r9+0] (8b)
    x64.mov r12, [r8+8] (8b)
    x64.mov r13, [r8+0] (8b)
    x64.mov r8d, 21
    x64.mov rcx, r8
    x64.call mrt_alloc
    x64.mov r14, r8
    x64.mov rcx, rbx
    x64.mov rdx, r14
    x64.call mrt_i64_to_string
    x64.mov rbx, r8
    x64.xor r8d, r8d
    x64.mov r8, r12
    x64.add r8, 0
    x64.add r8, 0
    x64.add r8, rbx
    x64.mov [rbp+-40], r8
    x64.mov r8, [rbp+-40]
    x64.add r8, 0
    x64.mov [rbp+-40], r8
    x64.mov r8, [rbp+-40]
    x64.add r8, 1
    x64.mov rcx, r8
    x64.mov r8, 0
    x64.mov r15, r8
    x64.mov r8, 0
    x64.mov r8, [rbp+-40]
    x64.call mrt_alloc
    x64.mov [rbp+-48], r8
    x64.lea r8, [rip+__istr_0]
    x64.mov r9, [rbp+-48]
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], r8
    x64.mov [rbp-24], r15
    x64.rep_movsb
    x64.mov r8, [rbp+-48]
    x64.add r8, 0
    x64.mov [rbp-8], r8
    x64.mov [rbp-16], r13
    x64.mov [rbp-32], r12
    x64.rep_movsb
    x64.lea r9, [rip+__istr_0]
    x64.add r8, r12
    x64.mov [rbp-8], r8
    x64.mov [rbp-16], r9
    x64.mov [rbp-24], r15
    x64.rep_movsb
    x64.add r8, 0
    x64.mov [rbp-8], r8
    x64.mov [rbp-16], r14
    x64.mov [rbp-32], rbx
    x64.rep_movsb
    x64.lea r9, [rip+__istr_0]
    x64.add r8, rbx
    x64.mov [rbp-8], r8
    x64.mov [rbp-16], r9
    x64.mov [rbp-24], r15
    x64.rep_movsb
    x64.mov r8, -1
    x64.mov r9, r14
    x64.sub r9, 8
    x64.lock xadd qword ptr [r9], r8
    x64.cmp r8, 1
    x64.jne __decref_cont_0
  __decref_free_0:
    x64.mov rcx, r14
    x64.call mm_free
  __decref_cont_0:
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov ecx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov rbx, r8
    x64.mov r8, 0
    x64.mov [rbx+40], r8 (8b)
    x64.mov r8, [rbp+-48]
    x64.mov [rbx+0], r8 (8b)
    x64.mov r8, [rbp+-40]
    x64.mov [rbx+8], r8 (8b)
    x64.mov [rbx+16], r8 (8b)
    x64.mov r8d, 1
    x64.mov [rbx+24], r8 (8b)
    x64.mov r8, -1
    x64.mov [rbx+32], r8 (8b)
    x64.lea rdx, [rip+__destruct_String]
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov [r12+0], rbx (8b)
    x64.mov r8, 0
    x64.mov [r12+8], r8 (8b)
    x64.mov rcx, r12
    x64.call String.count
    x64.mov rbx, r8
    x64.mov rcx, r12
    x64.call mm_drop
    x64.mov r8, -1
    x64.cmp rbx, r8
    x64.jbe __range_ok_0
  __range_panic_0:
    x64.lea rcx, [rip+__panic_msg_8e407baaf3c984cf]
    x64.call mrt_panic
  __range_ok_0:
    x64.mov r8, rbx
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=1136
    x64.xor r8d, r8d
    x64.mov r8, 0
    x64.mov [rbp-128], r8
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r9d, 1
    x64.mov r9, 1
    x64.mov [r8+0], r9 (8b)
    x64.mov r9d, 2
    x64.mov [r8+8], r9 (8b)
    x64.mov r9d, 99
    x64.mov [r8+0], r9 (8b)
    x64.mov rbx, [r8+0] (8b)
    x64.mov rcx, r8
    x64.call mm_drop
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r9d, 3
    x64.mov r9, 3
    x64.mov [r8+0], r9 (8b)
    x64.mov r9d, 4
    x64.mov [r8+8], r9 (8b)
    x64.mov r12, [r8+8] (8b)
    x64.mov r13, [r8+0] (8b)
    x64.mov rcx, r8
    x64.call mm_drop
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r9d, 5
    x64.mov r9, 5
    x64.mov [r8+0], r9 (8b)
    x64.mov r9d, 6
    x64.mov [r8+8], r9 (8b)
    x64.mov r14, [r8+8] (8b)
    x64.mov r15, [r8+0] (8b)
    x64.mov rcx, r8
    x64.call mm_drop
    x64.lea rcx, [rip+__layout_Array_String]
    x64.call Array.create
    x64.mov [rbp+-992], r8
    x64.add r13, r12
    x64.add rbx, 0
    x64.add r15, r14
    x64.add rbx, r13
    x64.mov [rbp+-984], rbx
    x64.mov r8, [rbp+-984]
    x64.add r8, r15
    x64.mov [rbp+-984], r8
    x64.mov r8d, 48
    x64.mov r8d, 16
    x64.mov r8, -2
    x64.mov r8, 0
  names_loop_0.header:
    x64.mov [rbp+-1008], r8
    x64.mov r8, [rbp+-1008]
    x64.cmp r8, 5
    x64.jge names_loop_0.exit
  names_loop_0:
    x64.mov ecx, 21
    x64.call mrt_alloc
    x64.mov rbx, r8
    x64.mov rdx, rbx
    x64.mov rcx, [rbp+-1008]
    x64.call mrt_i64_to_string
    x64.mov r12, r8
    x64.mov r8, r12
    x64.add r8, 5
    x64.mov r13, r8
    x64.add r13, 0
    x64.mov rcx, r13
    x64.add rcx, 1
    x64.call mrt_alloc
    x64.mov r14, r8
    x64.lea r8, [rip+__istr_1]
    x64.mov [rbp-136], r14
    x64.mov [rbp-144], r8
    x64.mov r8, 5
    x64.mov [rbp-152], r8
    x64.rep_movsb
    x64.mov r8, r14
    x64.add r8, 5
    x64.mov [rbp-160], r8
    x64.mov [rbp-168], rbx
    x64.mov [rbp-176], r12
    x64.rep_movsb
    x64.lea r9, [rip+__istr_0]
    x64.add r8, r12
    x64.mov [rbp-184], r8
    x64.mov [rbp-192], r9
    x64.mov r8, 0
    x64.mov [rbp-200], r8
    x64.rep_movsb
    x64.mov r8, -1
    x64.mov r9, rbx
    x64.sub r9, 8
    x64.lock xadd qword ptr [r9], r8
    x64.cmp r8, 1
    x64.jne __decref_cont_0
    x64.jmp __decref_free_0
  inlined_stdlib.__mm_alloc_0_0:
    x64.xor r8d, r8d
    x64.mov r8, 16
    x64.cmp r8, 1
    x64.mov r8, 16
    x64.jge __phi_trampoline_14_0
  inlined_stdlib.__mm_alloc_1_0:
    x64.mov r8d, 1
    x64.mov r12, r8
  inlined_stdlib.__mm_alloc_2_0:
    x64.lea r8, [rip+__mm_alloc_count]
    x64.lock inc qword ptr [r8]
    x64.mov r13, r12
    x64.add r13, 32
  inlined_stdlib.__slab_alloc_0_0:
    x64.xor r8d, r8d
    x64.cmp r13, 32768
    x64.jle inlined_stdlib.__slab_class_index_for_0_0
  inlined_stdlib.__slab_alloc_1_0:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, r13
    x64.call stdlib.__slab_os_direct_alloc
    x64.mov r13, r8
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_release
    x64.jmp inline_cont_main_2
  inlined_stdlib.__slab_class_index_for_0_0:
    x64.xor r8d, r8d
    x64.xor ecx, ecx
    x64.mov r14, rcx
    x64.mov r15, r8
  inlined_stdlib.__slab_class_index_for_1_0:
    x64.cmp r15, 18
    x64.jge inlined_stdlib.__slab_class_index_for_4_0
  inlined_stdlib.__slab_class_index_for_2_0:
    x64.mov rcx, r14
    x64.call stdlib.__slab_class_size
    x64.cmp r8, r13
    x64.jl inlined_stdlib.__slab_class_index_for_6_0
    x64.mov r13, r14
    x64.jmp inline_cont_main_0
  inlined_stdlib.__slab_class_index_for_3_0:
    x64.add r15, 1
    x64.mov r14, rcx
    x64.jmp inlined_stdlib.__slab_class_index_for_1_0
  inlined_stdlib.__slab_class_index_for_4_0:
    x64.mov r8d, 136
    x64.xor r13d, r13d
    x64.mov [rbp-208], r8
    x64.call_import slot_0
    x64.jmp inline_cont_main_0
  inlined_stdlib.__slab_class_index_for_6_0:
    x64.mov rcx, r14
    x64.add rcx, 1
    x64.jmp inlined_stdlib.__slab_class_index_for_3_0
  inline_cont_main_0:
    x64.mov r8, -1
    x64.test r8, r8
    x64.mov r8, 0
    x64.jge inlined_stdlib.__slab_alloc_4_0
  inlined_stdlib.__slab_proc_at_0_0:
    x64.mov r8, 0
    x64.test r8, r8
    x64.jge inlined_stdlib.__slab_proc_at_2_0
  inlined_stdlib.__slab_proc_at_1_0:
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_1
  inlined_stdlib.__slab_proc_at_2_0:
    x64.lea r8, [rip+__sched_procs]
    x64.mov r9, [r8+0] (8b)
    x64.test r9, r9
    x64.jne inlined_stdlib.__slab_proc_at_4_0
  inlined_stdlib.__slab_proc_at_3_0:
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_1
  inlined_stdlib.__slab_proc_at_4_0:
    x64.mov ecx, 3
    x64.mov r8, 0
    x64.shl r8, r8, rcx
    x64.add r9, r8
    x64.mov r8, [r9+0] (8b)
  inline_cont_main_1:
    x64.test r8, r8
    x64.setne r8
  inlined_stdlib.__slab_alloc_4_0:
    x64.test r8, r8
    x64.je inlined_stdlib.__slab_alloc_6_0
  inlined_stdlib.__slab_alloc_5_0:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, r13
    x64.call stdlib.__slab_alloc_class
    x64.mov r13, r8
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_release
    x64.jmp inline_cont_main_2
  inlined_stdlib.__slab_alloc_6_0:
    x64.mov rcx, r13
    x64.call stdlib.__slab_alloc_class
    x64.mov r13, r8
  inline_cont_main_2:
    x64.mov r8, 0
    x64.mov [r13+0], r8 (8b)
    x64.mov r8, r13
    x64.add r8, 8
    x64.mov [r8+0], rbx (8b)
    x64.mov r8, r13
    x64.add r8, 16
    x64.mov [r8+0], r12 (8b)
    x64.mov r8, r13
    x64.add r8, 24
    x64.mov r9, 0
    x64.mov [r8+0], r9 (8b)
    x64.add r13, 32
    x64.mov rdx, r13
  inline_cont_main_3:
    x64.sub r13, 8
    x64.lock inc qword ptr [r13]
    x64.mov r8, [rbp+-1000]
    x64.mov [rdx+0], r8 (8b)
    x64.mov r8, 0
    x64.mov [rdx+8], r8 (8b)
    x64.lea rax, [rip+__layout_Array_String]
    x64.mov rcx, [rbp+-992]
    x64.call Array.push
  names_loop_0.step:
    x64.mov r8, [rbp+-1008]
    x64.add r8, 1
    x64.jmp names_loop_0.header
  names_loop_0.exit:
    x64.lea rdx, [rip+__layout_Array_String]
    x64.mov rcx, [rbp+-992]
    x64.call Array.count
    x64.mov [rbp+-1000], r8
    x64.mov rcx, [rbp+-992]
    x64.call __mm_decref_maybenull_helper
    x64.lea rcx, [rip+__layout_Array_Integer]
    x64.lea rbx, [rip+__layout_Array_Integer]
    x64.lea r12, [rip+__layout_Array_Integer]
    x64.mov r8d, 2
    x64.lea r13, [rip+__layout_Array_Integer]
    x64.lea r14, [rip+__layout_Array_Integer]
    x64.lea r8, [rip+__layout_Array_Integer]
    x64.mov [rbp+-992], r8
    x64.mov r8d, 4
    x64.lea r8, [rip+__layout_Array_IntArray]
    x64.mov [rbp+-1008], r8
    x64.lea r8, [rip+__layout_Array_IntArray]
    x64.mov [rbp+-1024], r8
    x64.lea r8, [rip+__layout_Array_IntArray]
    x64.mov [rbp+-1032], r8
    x64.call Array.create
    x64.mov r15, r8
    x64.mov rcx, r15
    x64.mov rdx, 1
    x64.mov rax, rbx
    x64.call Array.push
    x64.mov rcx, r15
    x64.mov rdx, 2
    x64.mov rax, r12
    x64.call Array.push
    x64.mov rcx, r13
    x64.call Array.create
    x64.mov rbx, r8
    x64.mov rcx, rbx
    x64.mov rdx, 3
    x64.mov rax, r14
    x64.call Array.push
    x64.mov rcx, rbx
    x64.mov rdx, 4
    x64.mov rax, [rbp+-992]
    x64.call Array.push
    x64.mov rcx, [rbp+-1008]
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.mov rdx, r15
    x64.mov rax, [rbp+-1024]
    x64.call Array.push
    x64.mov rcx, r12
    x64.mov rdx, rbx
    x64.mov rax, [rbp+-1032]
    x64.call Array.push
    x64.mov rcx, r12
    x64.call matrix_total
    x64.mov rbx, r8
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r9, 0
    x64.mov [r8+0], r9 (8b)
    x64.mov r9, 0
    x64.mov [r8+8], r9 (8b)
    x64.mov r9, [r8+8] (8b)
    x64.mov [rbp+-1008], r9
    x64.mov r9, [r8+0] (8b)
    x64.mov [rbp+-1024], r9
    x64.mov r9, [r8+8] (8b)
    x64.mov [rbp+-1032], r9
    x64.mov r9, [r8+0] (8b)
    x64.mov [rbp+-1040], r9
    x64.mov rcx, r8
    x64.call mm_drop
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.mov r12d, 10
    x64.mov r13d, 20
    x64.call stdlib.__mm_alloc
    x64.mov [r8+0], r12 (8b)
    x64.mov [r8+8], r13 (8b)
    x64.mov r9, [r8+0] (8b)
    x64.mov [rbp+-1048], r9
    x64.mov rcx, r8
    x64.call mm_drop
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.lea r12, [rip+__istr_2]
    x64.lea r13, [rip+__destruct_String]
    x64.lea r14, [rip+__destruct_Person]
    x64.mov r8d, 16
    x64.xor r8d, r8d
    x64.mov r8d, 30
    x64.lea r8, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov [rbp+-992], r8
    x64.lea r8, [rip+__istr_3]
    x64.mov [rbp+-1056], r8
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-1064], r8
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov r15, r8
    x64.mov r8, 0
    x64.mov [r15+40], r8 (8b)
    x64.mov [r15+0], r12 (8b)
    x64.mov r8, 5
    x64.mov [r15+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r15+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r15+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r15+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, r13
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov r8, r12
    x64.sub r8, 8
    x64.lock inc qword ptr [r8]
    x64.mov [r12+0], r15 (8b)
    x64.mov r8, 1
    x64.mov [r12+8], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, r14
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov r8, 0
    x64.mov [r13+0], r8 (8b)
    x64.mov [r13+0], r12 (8b)
    x64.mov r8, 30
    x64.mov [r13+8], r8 (8b)
    x64.mov rcx, 48
    x64.mov rdx, [rbp+-992]
    x64.call mrt_alloc_with_dtor
    x64.mov r12, r8
    x64.mov r8, 0
    x64.mov [r12+40], r8 (8b)
    x64.mov r8, [rbp+-1056]
    x64.mov [r12+0], r8 (8b)
    x64.mov r8, 3
    x64.mov [r12+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r12+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r12+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r12+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1064]
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov r8, r14
    x64.sub r8, 8
    x64.lock inc qword ptr [r8]
    x64.mov [r14+0], r12 (8b)
    x64.mov r8, 1
    x64.mov [r14+8], r8 (8b)
    x64.mov rcx, [r13+0] (8b)
    x64.lea r12, [rip+stdlib.__destruct___ManagedMemory]
    x64.lea r15, [rip+__istr_4]
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-992], r8
    x64.call __mm_decref_maybenull_helper
    x64.mov [r13+0], r14 (8b)
    x64.mov rcx, 48
    x64.mov rdx, r12
    x64.call mrt_alloc_with_dtor
    x64.mov r12, r8
    x64.mov r8, 0
    x64.mov [r12+40], r8 (8b)
    x64.mov [r12+0], r15 (8b)
    x64.mov r8, 5
    x64.mov [r12+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r12+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r12+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r12+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-992]
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov r8, r14
    x64.sub r8, 8
    x64.lock inc qword ptr [r8]
    x64.mov [r14+0], r12 (8b)
    x64.mov r8, 1
    x64.mov [r14+8], r8 (8b)
    x64.mov rcx, [r13+0] (8b)
    x64.call __mm_decref_maybenull_helper
    x64.mov [r13+0], r14 (8b)
    x64.mov r8, [r13+8] (8b)
    x64.mov [rbp+-1056], r8
    x64.mov rcx, r13
    x64.call mm_drop
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.lea r8, [rip+__istr_5]
    x64.mov [rbp+-992], r8
    x64.mov r8d, 4
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-1064], r8
    x64.lea r12, [rip+__destruct_Shape]
    x64.mov r8d, 16
    x64.xor r8d, r8d
    x64.lea r8, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov [rbp+-1072], r8
    x64.lea r8, [rip+__istr_6]
    x64.mov [rbp+-1080], r8
    x64.lea r13, [rip+__destruct_String]
    x64.lea r14, [rip+__destruct_Shape]
    x64.mov r8d, 16
    x64.mov r8d, 1
    x64.lea r15, [rip+__destruct_Shape]
    x64.mov r8d, 16
    x64.mov r8d, 2
    x64.xor r8d, r8d
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov [rbp+-1088], r8
    x64.mov r8, [rbp+-1088]
    x64.mov r9, 0
    x64.mov [r8+40], r9 (8b)
    x64.mov r9, [rbp+-992]
    x64.mov [r8+0], r9 (8b)
    x64.mov r9, 4
    x64.mov [r8+8], r9 (8b)
    x64.mov r9, -2
    x64.mov [r8+16], r9 (8b)
    x64.mov r9, 1
    x64.mov [r8+24], r9 (8b)
    x64.mov r9, 0
    x64.mov [r8+32], r9 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1064]
    x64.call stdlib.__mm_alloc
    x64.mov [rbp+-992], r8
    x64.mov r8, [rbp+-992]
    x64.sub r8, 8
    x64.lock inc qword ptr [r8]
    x64.mov r8, [rbp+-992]
    x64.mov r9, [rbp+-1088]
    x64.mov [r8+0], r9 (8b)
    x64.mov r9, 1
    x64.mov [r8+8], r9 (8b)
    x64.mov rcx, 16
    x64.mov rdx, r12
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov r8, 0
    x64.mov [r12+0], r8 (8b)
    x64.mov r8, [rbp+-992]
    x64.mov [r12+8], r8 (8b)
    x64.mov rcx, 48
    x64.mov rdx, [rbp+-1072]
    x64.call mrt_alloc_with_dtor
    x64.mov [rbp+-992], r8
    x64.mov r8, [rbp+-992]
    x64.mov r9, 0
    x64.mov [r8+40], r9 (8b)
    x64.mov r9, [rbp+-1080]
    x64.mov [r8+0], r9 (8b)
    x64.mov r9, 3
    x64.mov [r8+8], r9 (8b)
    x64.mov r9, -2
    x64.mov [r8+16], r9 (8b)
    x64.mov r9, 1
    x64.mov [r8+24], r9 (8b)
    x64.mov r9, 0
    x64.mov [r8+32], r9 (8b)
    x64.mov rcx, 16
    x64.mov rdx, r13
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov r8, r13
    x64.sub r8, 8
    x64.lock inc qword ptr [r8]
    x64.mov r8, [rbp+-992]
    x64.mov [r13+0], r8 (8b)
    x64.mov r8, 1
    x64.mov [r13+8], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, r14
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov r8, 1
    x64.mov [r14+0], r8 (8b)
    x64.mov [r14+8], r13 (8b)
    x64.mov rcx, 16
    x64.mov rdx, r15
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov r8, 2
    x64.mov [r13+0], r8 (8b)
    x64.mov r8, 0
    x64.mov [r13+8], r8 (8b)
    x64.mov rcx, r12
    x64.call describe
    x64.mov [rbp+-1064], r8
    x64.mov rcx, r12
    x64.call mm_drop
    x64.mov rcx, r14
    x64.call describe
    x64.mov [rbp+-1072], r8
    x64.mov rcx, r14
    x64.call mm_drop
    x64.mov rcx, r13
    x64.call describe
    x64.mov [rbp+-1080], r8
    x64.mov rcx, r13
    x64.call mm_drop
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.lea r12, [rip+__istr_7]
    x64.mov r13d, 4
    x64.lea r14, [rip+__destruct_String]
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov r15, r8
    x64.mov r8, 0
    x64.mov [r15+40], r8 (8b)
    x64.mov [r15+0], r12 (8b)
    x64.mov [r15+8], r13 (8b)
    x64.mov r8, -2
    x64.mov [r15+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r15+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r15+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, r14
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov r8, r12
    x64.sub r8, 8
    x64.lock inc qword ptr [r8]
    x64.mov [r12+0], r15 (8b)
    x64.mov r8, 1
    x64.mov [r12+8], r8 (8b)
    x64.mov rcx, [rbp-128]
    x64.mov r13d, 8
    x64.lea rax, [rbp-128]
    x64.mov r14, [rbp-224]
    x64.mov r8d, 7
    x64.lea r15, [rip+main$closure_0]
    x64.mov r8d, 8
    x64.call __mm_decref_maybenull_helper
    x64.mov [rbp-128], r12
    x64.mov rcx, r13
    x64.mov rdx, 0
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov [r12+0], r14 (8b)
    x64.mov rcx, 7
    x64.mov rdx, r12
    x64.call r15
    x64.mov r13, r8
    x64.mov rcx, 8
    x64.mov rdx, r12
    x64.call r15
    x64.mov r14, r8
    x64.mov rcx, r12
    x64.call mm_drop
    x64.lea rcx, [rip+__layout_Array_Point]
    x64.call Array.create
    x64.mov r12, r8
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r9, r8
    x64.sub r9, 8
    x64.lock inc qword ptr [r9]
    x64.mov r9, 1
    x64.mov [r8+0], r9 (8b)
    x64.mov r9d, 2
    x64.mov [r8+8], r9 (8b)
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov rcx, r12
    x64.mov rdx, r8
    x64.call Array.push
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r9, r8
    x64.sub r9, 8
    x64.lock inc qword ptr [r9]
    x64.mov r9, 3
    x64.mov [r8+0], r9 (8b)
    x64.mov r9d, 4
    x64.mov [r8+8], r9 (8b)
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov rcx, r12
    x64.mov rdx, r8
    x64.call Array.push
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r9, r8
    x64.sub r9, 8
    x64.lock inc qword ptr [r9]
    x64.mov r9, 5
    x64.mov [r8+0], r9 (8b)
    x64.mov r9d, 6
    x64.mov [r8+8], r9 (8b)
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov rcx, r12
    x64.mov rdx, r8
    x64.call Array.push
    x64.mov rcx, r12
    x64.call points_x_sum
    x64.mov r15, r8
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.lea rcx, [rip+__layout_Array_Point]
    x64.call Array.create
    x64.mov [rbp+-992], r8
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r9, r8
    x64.sub r9, 8
    x64.lock inc qword ptr [r9]
    x64.mov r9d, 7
    x64.mov [r8+0], r9 (8b)
    x64.mov r9d, 8
    x64.mov [r8+8], r9 (8b)
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov rdx, r8
    x64.mov rcx, [rbp+-992]
    x64.call Array.push
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r9, r8
    x64.sub r9, 8
    x64.lock inc qword ptr [r9]
    x64.mov r9d, 9
    x64.mov [r8+0], r9 (8b)
    x64.mov r9d, 10
    x64.mov [r8+8], r9 (8b)
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov rdx, r8
    x64.mov rcx, [rbp+-992]
    x64.call Array.push
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r9, r8
    x64.sub r9, 8
    x64.lock inc qword ptr [r9]
    x64.mov r9d, 11
    x64.mov rsi, [rbp+-984]
    x64.add rsi, [rbp+-1000]
    x64.mov [r8+0], r9 (8b)
    x64.mov r9, [rbp+-1040]
    x64.add r9, [rbp+-1032]
    x64.add rsi, rbx
    x64.mov rdi, [rbp+-1024]
    x64.add rdi, [rbp+-1008]
    x64.add rsi, r9
    x64.mov r9d, 12
    x64.add rsi, rdi
    x64.mov [r8+8], r9 (8b)
    x64.add rsi, [rbp+-1048]
    x64.add rsi, [rbp+-1056]
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov rbx, rsi
    x64.add rbx, [rbp+-1064]
    x64.mov rdx, r8
    x64.mov rcx, [rbp+-992]
    x64.call Array.push
    x64.add rbx, [rbp+-1072]
    x64.add rbx, [rbp+-1080]
    x64.add rbx, r13
    x64.add rbx, r14
    x64.add rbx, r15
    x64.mov rdx, 0
    x64.mov r12, rdx
  alias_loop_0.header:
    x64.cmp r12, 3
    x64.jge alias_loop_0.exit
  inlined_Array.get_0_0:
    x64.mov r8, [rbp+-992]
    x64.mov rcx, [r8+0] (8b)
    x64.mov rdx, r12
    x64.call stdlib.__managed_mem_get
    x64.mov [rbp+-1016], r8
    x64.test rdx, rdx
    x64.je inlined_Array.get_3_0
  inlined_Array.get_1_0:
    x64.mov edx, 1
    x64.xor ecx, ecx
    x64.jmp inline_cont_main_4
  inlined_Array.get_3_0:
    x64.xor r8d, r8d
    x64.jmp __rc_edge_20_0
  inline_cont_main_4:
    x64.test rdx, rdx
    x64.je try_0.merge
    x64.jmp try_0.otherwise
  alias_loop_0.step:
    x64.mov rdx, r12
    x64.add rdx, 1
    x64.mov r12, rdx
    x64.jmp alias_loop_0.header
  alias_loop_0.exit:
    x64.mov rcx, [rbp+-992]
    x64.call __mm_decref_maybenull_helper
    x64.test rbx, rbx
    x64.jge guard_0.after
    x64.jmp guard_0
  try_0.otherwise:
    x64.call __mm_decref_maybenull_helper
    x64.mov ecx, 21
    x64.call mrt_alloc
    x64.mov rbx, r8
    x64.mov rcx, r12
    x64.mov rdx, rbx
    x64.call mrt_i64_to_string
    x64.mov r12, r8
    x64.mov r13d, 20
    x64.mov r8, r12
    x64.add r8, 75
    x64.add r8, 20
    x64.mov r14, r8
    x64.add r14, 1
    x64.mov rcx, r14
    x64.add rcx, 1
    x64.call mrt_alloc
    x64.mov r15, r8
    x64.mov r8d, 75
    x64.lea r9, [rip+__istr_8]
    x64.mov [rbp-232], r15
    x64.mov [rbp-240], r9
    x64.mov [rbp-248], r8
    x64.rep_movsb
    x64.mov r8, r15
    x64.add r8, 75
    x64.mov [rbp-256], r8
    x64.mov [rbp-264], rbx
    x64.mov [rbp-272], r12
    x64.rep_movsb
    x64.lea r9, [rip+__istr_9]
    x64.add r8, r12
    x64.mov [rbp-280], r8
    x64.mov [rbp-288], r9
    x64.mov [rbp-296], r13
    x64.rep_movsb
    x64.lea r9, [rip+__istr_10]
    x64.add r8, 20
    x64.mov [rbp-304], r8
    x64.mov [rbp-312], r9
    x64.mov r8, 1
    x64.mov [rbp-320], r8
    x64.rep_movsb
    x64.mov r8, -1
    x64.mov r9, rbx
    x64.sub r9, 8
    x64.lock xadd qword ptr [r9], r8
    x64.cmp r8, 1
    x64.jne __decref_cont_1
    x64.jmp __decref_free_1
  inlined_stdlib.__mm_alloc_0_1:
    x64.xor r8d, r8d
    x64.mov r8, 16
    x64.cmp r8, 1
    x64.mov r8, 16
    x64.jge __phi_trampoline_22_0
  inlined_stdlib.__mm_alloc_1_1:
    x64.mov r8d, 1
    x64.mov r13, r8
  inlined_stdlib.__mm_alloc_2_1:
    x64.lea r8, [rip+__mm_alloc_count]
    x64.lock inc qword ptr [r8]
    x64.mov r8, r13
    x64.mov [rbp+-984], r8
    x64.mov r8, [rbp+-984]
    x64.add r8, 32
    x64.mov [rbp+-984], r8
  inlined_stdlib.__slab_alloc_0_1:
    x64.xor r8d, r8d
    x64.mov r8, [rbp+-984]
    x64.cmp r8, 32768
    x64.jle inlined_stdlib.__slab_class_index_for_0_1
  inlined_stdlib.__slab_alloc_1_1:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, [rbp+-984]
    x64.call stdlib.__slab_os_direct_alloc
    x64.mov r14, r8
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_release
    x64.jmp inline_cont_main_7
  inlined_stdlib.__slab_class_index_for_0_1:
    x64.xor r8d, r8d
    x64.xor ecx, ecx
    x64.mov r14, rcx
    x64.mov r15, r8
  inlined_stdlib.__slab_class_index_for_1_1:
    x64.cmp r15, 18
    x64.jge inlined_stdlib.__slab_class_index_for_4_1
  inlined_stdlib.__slab_class_index_for_2_1:
    x64.mov rcx, r14
    x64.call stdlib.__slab_class_size
    x64.cmp r8, [rbp+-984]
    x64.jl inlined_stdlib.__slab_class_index_for_6_1
    x64.jmp inline_cont_main_5
  inlined_stdlib.__slab_class_index_for_3_1:
    x64.add r15, 1
    x64.mov r14, rcx
    x64.jmp inlined_stdlib.__slab_class_index_for_1_1
  inlined_stdlib.__slab_class_index_for_4_1:
    x64.mov r8d, 136
    x64.xor r14d, r14d
    x64.mov [rbp-328], r8
    x64.call_import slot_0
    x64.jmp inline_cont_main_5
  inlined_stdlib.__slab_class_index_for_6_1:
    x64.mov rcx, r14
    x64.add rcx, 1
    x64.jmp inlined_stdlib.__slab_class_index_for_3_1
  inline_cont_main_5:
    x64.mov r8, -1
    x64.test r8, r8
    x64.mov r8, 0
    x64.jge inlined_stdlib.__slab_alloc_4_1
  inlined_stdlib.__slab_proc_at_0_1:
    x64.mov r8, 0
    x64.test r8, r8
    x64.jge inlined_stdlib.__slab_proc_at_2_1
  inlined_stdlib.__slab_proc_at_1_1:
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_6
  inlined_stdlib.__slab_proc_at_2_1:
    x64.lea r8, [rip+__sched_procs]
    x64.mov r9, [r8+0] (8b)
    x64.test r9, r9
    x64.jne inlined_stdlib.__slab_proc_at_4_1
  inlined_stdlib.__slab_proc_at_3_1:
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_6
  inlined_stdlib.__slab_proc_at_4_1:
    x64.mov ecx, 3
    x64.mov r8, 0
    x64.shl r8, r8, rcx
    x64.add r9, r8
    x64.mov r8, [r9+0] (8b)
  inline_cont_main_6:
    x64.test r8, r8
    x64.setne r8
  inlined_stdlib.__slab_alloc_4_1:
    x64.test r8, r8
    x64.je inlined_stdlib.__slab_alloc_6_1
  inlined_stdlib.__slab_alloc_5_1:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, r14
    x64.call stdlib.__slab_alloc_class
    x64.mov r14, r8
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_release
    x64.jmp inline_cont_main_7
  inlined_stdlib.__slab_alloc_6_1:
    x64.mov rcx, r14
    x64.call stdlib.__slab_alloc_class
    x64.mov r14, r8
  inline_cont_main_7:
    x64.mov r8, 0
    x64.mov [r14+0], r8 (8b)
    x64.mov r8, r14
    x64.add r8, 8
    x64.mov [r8+0], r12 (8b)
    x64.mov r8, r14
    x64.add r8, 16
    x64.mov [r8+0], r13 (8b)
    x64.mov r8, r14
    x64.add r8, 24
    x64.mov r9, 0
    x64.mov [r8+0], r9 (8b)
    x64.add r14, 32
    x64.mov r12, r14
  inline_cont_main_8:
    x64.sub r14, 8
    x64.lock inc qword ptr [r14]
    x64.mov [r12+0], rbx (8b)
    x64.mov r8, 0
    x64.mov [r12+8], r8 (8b)
    x64.mov r8, [r12+0] (8b)
    x64.mov rcx, [r8+0] (8b)
    x64.call mrt_panic
    x64.mov rcx, r12
    x64.call mm_drop
    x64.mov rcx, 0
  try_0.merge:
    x64.mov r13, [rcx+8] (8b)
    x64.mov r14, [rcx+0] (8b)
    x64.call __mm_decref_maybenull_helper
    x64.add r14, r13
    x64.add rbx, r14
    x64.jmp alias_loop_0.step
  guard_0:
    x64.mov rcx, [rbp-128]
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, 1
    x64.epilogue
    x64.ret
  guard_0.after:
    x64.mov rcx, [rbp-128]
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, 0
    x64.epilogue
    x64.ret
  __rc_edge_20_0:
    x64.mov r8, [rbp+-1016]
    x64.sub r8, 8
    x64.lock inc qword ptr [r8]
    x64.mov rcx, [rbp+-1016]
    x64.mov rdx, 0
    x64.jmp inline_cont_main_4
  __decref_free_0:
    x64.mov rcx, rbx
    x64.call mm_free
  __decref_cont_0:
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov [rbp+-1000], r8
    x64.mov r8, [rbp+-1000]
    x64.mov r9, 0
    x64.mov [r8+40], r9 (8b)
    x64.mov [r8+0], r14 (8b)
    x64.mov [r8+8], r13 (8b)
    x64.mov [r8+16], r13 (8b)
    x64.mov r9, 1
    x64.mov [r8+24], r9 (8b)
    x64.mov r9, -1
    x64.mov [r8+32], r9 (8b)
    x64.lea rbx, [rip+__destruct_String]
    x64.jmp inlined_stdlib.__mm_alloc_0_0
  __decref_free_1:
    x64.mov rcx, rbx
    x64.call mm_free
  __decref_cont_1:
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov rbx, r8
    x64.mov r8, 0
    x64.mov [rbx+40], r8 (8b)
    x64.mov [rbx+0], r15 (8b)
    x64.mov [rbx+8], r14 (8b)
    x64.mov [rbx+16], r14 (8b)
    x64.mov r8, 1
    x64.mov [rbx+24], r8 (8b)
    x64.mov r8, -1
    x64.mov [rbx+32], r8 (8b)
    x64.lea r12, [rip+__destruct_String]
    x64.jmp inlined_stdlib.__mm_alloc_0_1
  __phi_trampoline_14_0:
    x64.mov r12, r8
    x64.jmp inlined_stdlib.__mm_alloc_2_0
  __phi_trampoline_22_0:
    x64.mov r13, r8
    x64.jmp inlined_stdlib.__mm_alloc_2_1
  }
}

```

## Phase 3 regression tests — aliasFromStore prefix-kill relaxation

These fragments guard the relaxation in
`IsCrossBlockPairSafe` / `TryPrefixIsBenignSiblingCleanup` that accepts
a prefix containing sibling scope-end cleanup ops (load + decref of
unrelated slots, plus optionally a decref of srcVar) as safe under
Maxon's borrow convention. The relaxation unlocks for-in tuple
brackets and similar shapes where srcVar's own scope-end decref fires
in the same block before varName's decref.

<!-- test: prefix-kill-sibling-cleanup -->
Two aliased struct slots both scope-end-decreffed in the same block.
When `b`'s decref comes first in the prefix, the alias anchor for `a`
is already "killed" in the legacy sense — the relaxation recognises
this as sibling cleanup and eliminates the alias bracket.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	@heap let a = Box.create(7)
	@heap let c = Box.create(11)
	var total = 0
	if true 'outer'
		let b = a
		let d = c
		total = b.value + d.value
	end 'outer'
	return total
end 'main'
```
```exitcode
18
```

## Phase 2 regression tests — multi-exit bracket elimination

These fragments guard the relaxation in `CancelCrossBlockRedundantRefcounts`
that allows an incref to pair with more than one reachable decref block
when the matched decrefs are on mutually-exclusive paths (e.g. match arms
that both scope-clean the same slot at their exits).

<!-- test: multi-exit-match-arm-brackets -->
An aliased slot whose scope-end decrefs sit on two mutually-exclusive
match arms. The incref in the pre-match block dominates both decref
blocks; each iteration from the incref hits exactly one of them. Phase 2
eliminates the bracket as a group.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

union Tag
	first
	second
end 'Tag'

function main() returns ExitCode
	@heap let a = Box.create(42)
	let tag = Tag.first
	var total = 0
	if true 'inner'
		let b = a
		match tag 'branch'
			first then total = b.value
			second then total = b.value + 1
		end 'branch'
	end 'inner'
	return total
end 'main'
```
```exitcode
42
```

<!-- test: multi-exit-three-way-split -->
Three-way exit (three match arms, each decrefing the aliased slot at
its scope end). Phase 2 eliminates the shared-source bracket across all
three.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

union Tag
	a
	b
	c
end 'Tag'

function main() returns ExitCode
	@heap let x = Box.create(7)
	let tag = Tag.b
	var total = 0
	if true 'inner'
		let alias = x
		match tag 'three'
			a then total = alias.value
			b then total = alias.value * 2
			c then total = alias.value * 3
		end 'three'
	end 'inner'
	return total
end 'main'
```
```exitcode
14
```

## Phase 1 regression tests — try-call borrow-awareness

These fragments are regression guards for the try-call relaxation of
`RefcountOptimizationPass.ClassifyAliasingOp`. Before Phase 1 they would
leave an incref/decref bracket on the aliased slot intact; after Phase 1
the bracket is eliminated because the try-call's callee is proven
borrow-only on every argument. The scoreboard stderr block is the
authoritative assertion — reviewing its diff after a future change
catches accidental regression of this optimization.

<!-- test: try-call-borrow-only-window -->
Alias assignment `let b = a` in an inner block, followed by a try-call
on a borrow-only callee inside the same block. The bracket on `b`
spans the try-call and `b`'s scope-end decref fires before `a`'s outer
scope-end decref — so `a`'s decref is not inside `b`'s window and the
firstStoreOf-safety check passes. Phase 1 eliminates `b`'s bracket.
```maxon
typealias Integer = int(i64.min to i64.max)

union BoxError
	negative
end 'BoxError'

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function inspect(b Box) returns Integer throws BoxError
	if b.value < 0 'neg'
		throw BoxError.negative
	end 'neg'
	return b.value
end 'inspect'

function main() returns ExitCode
	@heap let a = Box.create(42)
	var total = 0
	if true 'inner'
		let b = a
		let n = try inspect(b) otherwise 0
		total = n
	end 'inner'
	return total
end 'main'
```
```exitcode
42
```

<!-- test: try-call-retaining-callee-preserved -->
Negative: same shape but the callee retains its argument (stores it
into a container field). The bracket must be preserved — the callee
holds its own ref independently and could outlive the caller's window.
```maxon
typealias Integer = int(i64.min to i64.max)

union BoxError
	negative
end 'BoxError'

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function stash(arr BoxArray, b Box) returns Integer throws BoxError
	if b.value < 0 'neg'
		throw BoxError.negative
	end 'neg'
	arr.push(b)
	return b.value
end 'stash'

function main() returns ExitCode
	var arr = BoxArray.create()
	@heap let a = Box.create(42)
	let b = a
	let n = try stash(arr, b: b) otherwise 0
	return n
end 'main'
```
```exitcode
42
```

<!-- test: try-call-aliasfromstore-window -->
The firstStoreOf alias shape (same SSA heap pointer stored into two
slots with a try-call between). Mirrors the for-in lowering that
stores `iter.current()` into both `__forin_result` and the user's
loop variable. Phase 1 eliminates the second slot's incref/decref
pair.
```maxon
typealias Integer = int(i64.min to i64.max)

union BoxError
	negative
end 'BoxError'

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function peek(b Box) returns Integer throws BoxError
	if b.value < 0 'neg'
		throw BoxError.negative
	end 'neg'
	return b.value
end 'peek'

function pair() returns Box
	return Box.create(42)
end 'pair'

function main() returns ExitCode
	@heap let primary = pair()
	var total = 0
	if true 'inner'
		let alias = primary
		let n = try peek(alias) otherwise 0
		total = n
	end 'inner'
	return total
end 'main'
```
```exitcode
42
```

<!-- test: try-call-inside-loop-body -->
Try-call inside a loop body where the alias source is stable across
iterations. The loop-invariant sub-pass eliminates the per-iteration
incref/decref on the alias slot. Mirrors the
`__ListIterator_OpIndex.advance` hot spot surfaced by the whole-compiler
baseline.
```maxon
typealias Integer = int(i64.min to i64.max)

union BoxError
	negative
end 'BoxError'

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function peek(b Box) returns Integer throws BoxError
	if b.value < 0 'neg'
		throw BoxError.negative
	end 'neg'
	return b.value
end 'peek'

function main() returns ExitCode
	@heap let boxed = Box.create(7)
	var total = 0
	for _ in 0 upto 3 'loop'
		let alias = boxed
		let n = try peek(alias) otherwise 0
		total = total + n
	end 'loop'
	return total
end 'main'
```
```exitcode
21
```

## Phase 4 regression tests — global-load anchor elimination

These fragments guard `CancelGlobalLoadOrphanBrackets` in
`RefcountOptimizationPass`. The sub-pass removes the `mm_incref` +
`mm_decref_if_nonnull` bracket emitted around a module-global load into
an orphan temp, when the function is proven borrow-only on that global
(no tainted-from-global SSA value reaches a retention event in the body).

<!-- test: global-struct-load-borrow -->
A module-level managed struct global is read borrow-only inside a
function — it reads a single field via `load_indirect` and returns a
scalar comparison. The emitter wraps the global load in incref+decref
brackets (orphan-temp pattern). After Phase 4, the brackets are gone:
`mm_incref Config [check]` and `mm_decref Config [check]` do not appear
in the trace.
```maxon
typealias Integer = int(i64.min to i64.max)

type Config
	export var threshold as Integer

	static function create(threshold Integer) returns Self
		return Self{threshold: threshold}
	end 'create'
end 'Config'

var cfg = Config.create(10)

function check(value Integer) returns Integer
	if value > cfg.threshold 'high'
		return value
	end 'high'
	return 0
end 'check'

function main() returns ExitCode
	return check(42)
end 'main'
```
```exitcode
42
```
