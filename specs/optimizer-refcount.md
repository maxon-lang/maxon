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
  func @Person.create(rcx: i64, rdx: i64) -> i64 {
  entry:
    x64.prologue stack_size=32
    x64.mov rbx, rcx
    x64.mov r12, rdx
    x64.mov eax, 1
    x64.lea rdx, [rip+__destruct_Person]
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.xor r8d, r8d
    x64.mov [r13+0], r8 (8b)
    x64.mov [r13+0], rbx (8b)
    x64.mov [r13+8], r12 (8b)
    x64.mov r8, r13
    x64.epilogue
    x64.ret
  }
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
    x64.mov r9, r8
    x64.sub r9, 8
    x64.lock inc qword ptr [r9]
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
    x64.mov rax, [rsi+0] (8b)
    x64.mov rsi, [r8+0] (8b)
    x64.test rdi, rdi
    x64.jge inlined_stdlib.__managed_mem_cursor_current_2_0
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    x64.xor ecx, ecx
    x64.mov r8, rcx
    x64.sub r8, rdi
    x64.mov rdi, rax
    x64.imul rdi, r8
    x64.mov ecx, 3
    x64.mov rax, rdi
    x64.shr rax, rax, rcx
    x64.xor edx, edx
    x64.add rsi, rax
    x64.mov [rbp-8], rsi
    x64.mov [rbp-16], rdx
    x64.movzx rax, byte ptr [rax+0]
    x64.mov rsi, [rbp-24]
    x64.mov eax, 1
    x64.mov edx, 7
    x64.mov rcx, r8
    x64.shl rax, rax, rcx
    x64.mov r8, rdi
    x64.and r8, rdx
    x64.sub rax, 1
    x64.mov rcx, r8
    x64.shr rsi, rsi, rcx
    x64.and rsi, rax
    x64.mov r8, rsi
    x64.jmp inline_cont_row_total_3
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    x64.imul rax, rdi
    x64.add rsi, rax
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
    x64.mov r9, r8
    x64.sub r9, 8
    x64.lock inc qword ptr [r9]
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
    x64.jge inlined_stdlib.__managed_mem_cursor_current_2_0
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    x64.xor ecx, ecx
    x64.mov r8, rcx
    x64.sub r8, rsi
    x64.imul rdi, r8
    x64.mov ecx, 3
    x64.mov rsi, rdi
    x64.shr rsi, rsi, rcx
    x64.xor eax, eax
    x64.add r9, rsi
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], rax
    x64.movzx rax, byte ptr [rax+0]
    x64.mov r9, [rbp-24]
    x64.mov esi, 1
    x64.mov eax, 7
    x64.mov rcx, r8
    x64.shl rsi, rsi, rcx
    x64.mov r8, rdi
    x64.and r8, rax
    x64.sub rsi, 1
    x64.mov rcx, r8
    x64.shr r9, r9, rcx
    x64.mov r14, r9
    x64.and r14, rsi
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
    x64.mov r14, rcx
    x64.jmp inline_cont_matrix_total_2
  inlined_stdlib.__managed_mem_load_sized_2_0:
    x64.cmp rsi, 2
    x64.jne inlined_stdlib.__managed_mem_load_sized_4_0
  inlined_stdlib.__managed_mem_load_sized_3_0:
    x64.movzx rcx, [r9+0] (2b)
    x64.mov r14, rcx
    x64.jmp inline_cont_matrix_total_2
  inlined_stdlib.__managed_mem_load_sized_4_0:
    x64.cmp rsi, 4
    x64.jne inlined_stdlib.__managed_mem_load_sized_6_0
  inlined_stdlib.__managed_mem_load_sized_5_0:
    x64.mov rcx, [r9+0] (4b)
    x64.mov r14, rcx
    x64.jmp inline_cont_matrix_total_2
  inlined_stdlib.__managed_mem_load_sized_6_0:
    x64.mov rcx, [r9+0] (8b)
    x64.mov r14, rcx
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
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.jmp inline_cont_matrix_total_3
  __rc_edge_24_0:
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
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
    x64.mov r9, r8
    x64.sub r9, 8
    x64.lock inc qword ptr [r9]
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
    x64.jge inlined_stdlib.__managed_mem_cursor_current_2_0
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    x64.xor ecx, ecx
    x64.mov r8, rcx
    x64.sub r8, rsi
    x64.imul rdi, r8
    x64.mov ecx, 3
    x64.mov rsi, rdi
    x64.shr rsi, rsi, rcx
    x64.xor eax, eax
    x64.add r9, rsi
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], rax
    x64.movzx rax, byte ptr [rax+0]
    x64.mov r9, [rbp-24]
    x64.mov esi, 1
    x64.mov eax, 7
    x64.mov rcx, r8
    x64.shl rsi, rsi, rcx
    x64.mov r8, rdi
    x64.and r8, rax
    x64.sub rsi, 1
    x64.mov rcx, r8
    x64.shr r9, r9, rcx
    x64.mov r14, r9
    x64.and r14, rsi
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
    x64.mov r14, rcx
    x64.jmp inline_cont_points_x_sum_2
  inlined_stdlib.__managed_mem_load_sized_2_0:
    x64.cmp rsi, 2
    x64.jne inlined_stdlib.__managed_mem_load_sized_4_0
  inlined_stdlib.__managed_mem_load_sized_3_0:
    x64.movzx rcx, [r9+0] (2b)
    x64.mov r14, rcx
    x64.jmp inline_cont_points_x_sum_2
  inlined_stdlib.__managed_mem_load_sized_4_0:
    x64.cmp rsi, 4
    x64.jne inlined_stdlib.__managed_mem_load_sized_6_0
  inlined_stdlib.__managed_mem_load_sized_5_0:
    x64.mov rcx, [r9+0] (4b)
    x64.mov r14, rcx
    x64.jmp inline_cont_points_x_sum_2
  inlined_stdlib.__managed_mem_load_sized_6_0:
    x64.mov rcx, [r9+0] (8b)
    x64.mov r14, rcx
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
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov rcx, r14
    x64.jmp inline_cont_points_x_sum_3
  __rc_edge_24_0:
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov rcx, r14
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
    x64.mov r9, r12
    x64.add r9, 0
    x64.add r9, 0
    x64.add r9, rbx
    x64.mov r15, r9
    x64.add r15, 0
    x64.mov r9, r15
    x64.add r9, 1
    x64.mov rcx, r9
    x64.mov [rbp+-40], r8
    x64.call mrt_alloc
    x64.mov [rbp+-48], r8
    x64.lea r8, [rip+__istr_0]
    x64.mov r9, [rbp+-48]
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], r8
    x64.mov r8, [rbp+-40]
    x64.mov [rbp-24], r8
    x64.rep_movsb
    x64.mov r9, [rbp+-48]
    x64.add r9, 0
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], r13
    x64.mov [rbp-32], r12
    x64.rep_movsb
    x64.lea rsi, [rip+__istr_0]
    x64.add r9, r12
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], rsi
    x64.mov [rbp-24], r8
    x64.rep_movsb
    x64.add r9, 0
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], r14
    x64.mov [rbp-32], rbx
    x64.rep_movsb
    x64.lea rsi, [rip+__istr_0]
    x64.add r9, rbx
    x64.mov [rbp-8], r9
    x64.mov [rbp-16], rsi
    x64.mov [rbp-24], r8
    x64.rep_movsb
    x64.mov rcx, r14
    x64.call stdlib.__mm_decref
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov ecx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov rbx, r8
    x64.mov r8, [rbp+-40]
    x64.mov [rbx+40], r8 (8b)
    x64.mov r8, [rbp+-48]
    x64.mov [rbx+0], r8 (8b)
    x64.mov [rbx+8], r15 (8b)
    x64.mov [rbx+16], r15 (8b)
    x64.mov r8d, 1
    x64.mov [rbx+24], r8 (8b)
    x64.mov r8, -1
    x64.mov [rbx+32], r8 (8b)
    x64.mov eax, 1
    x64.lea rdx, [rip+__destruct_String]
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r12, r8
    x64.mov [r12+0], rbx (8b)
    x64.mov r8, [rbp+-40]
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
    x64.prologue stack_size=1344
    x64.xor r8d, r8d
    x64.mov r8, 0
    x64.mov [rbp-128], r8
    x64.mov eax, 1
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc_needzero
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
    x64.mov eax, 1
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r9d, 3
    x64.mov r9, 3
    x64.mov [r8+0], r9 (8b)
    x64.mov r9d, 4
    x64.mov [r8+8], r9 (8b)
    x64.mov r12, [r8+8] (8b)
    x64.mov r13, [r8+0] (8b)
    x64.mov rcx, r8
    x64.call mm_drop
    x64.mov eax, 1
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc_needzero
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
    x64.mov [rbp+-1192], r8
    x64.add r13, r12
    x64.add rbx, 0
    x64.add r15, r14
    x64.add rbx, r13
    x64.mov [rbp+-1184], rbx
    x64.mov r8, [rbp+-1184]
    x64.add r8, r15
    x64.mov [rbp+-1184], r8
    x64.mov r8d, 48
    x64.mov r8d, 16
    x64.mov r8, -2
    x64.mov rcx, 0
    x64.mov rbx, rcx
  names_loop_0.header:
    x64.cmp rbx, 5
    x64.jge names_loop_0.exit
  names_loop_0:
    x64.mov ecx, 21
    x64.call mrt_alloc
    x64.mov r12, r8
    x64.mov rcx, rbx
    x64.mov rdx, r12
    x64.call mrt_i64_to_string
    x64.mov r13, r8
    x64.mov r8, r13
    x64.add r8, 5
    x64.mov r14, r8
    x64.add r14, 0
    x64.mov rcx, r14
    x64.add rcx, 1
    x64.call mrt_alloc
    x64.mov r15, r8
    x64.lea r8, [rip+__istr_1]
    x64.mov [rbp-136], r15
    x64.mov [rbp-144], r8
    x64.mov r8, 5
    x64.mov [rbp-152], r8
    x64.rep_movsb
    x64.mov r8, r15
    x64.add r8, 5
    x64.mov [rbp-160], r8
    x64.mov [rbp-168], r12
    x64.mov [rbp-176], r13
    x64.rep_movsb
    x64.lea r9, [rip+__istr_0]
    x64.add r8, r13
    x64.mov [rbp-184], r8
    x64.mov [rbp-192], r9
    x64.mov r8, 0
    x64.mov [rbp-200], r8
    x64.rep_movsb
    x64.mov rcx, r12
    x64.call stdlib.__mm_decref
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov [rbp+-1200], r8
    x64.mov r8, [rbp+-1200]
    x64.mov r9, 0
    x64.mov [r8+40], r9 (8b)
    x64.mov [r8+0], r15 (8b)
    x64.mov [r8+8], r14 (8b)
    x64.mov [r8+16], r14 (8b)
    x64.mov r9, 1
    x64.mov [r8+24], r9 (8b)
    x64.mov r9, -1
    x64.mov [r8+32], r9 (8b)
    x64.mov r8d, 1
    x64.lea r12, [rip+__destruct_String]
  inlined_stdlib.__mm_alloc_needzero_0_0:
    x64.xor r9d, r9d
    x64.mov r9, 16
    x64.cmp r9, 1
    x64.mov r9, 16
    x64.jge inlined_stdlib.__mm_alloc_needzero_2_0
  inlined_stdlib.__mm_alloc_needzero_1_0:
    x64.mov r9d, 1
  inlined_stdlib.__mm_alloc_needzero_2_0:
    x64.mov [rbp+-1216], r9
    x64.lea r9, [rip+__mm_alloc_count]
    x64.lock inc qword ptr [r9]
    x64.test r8, r8
    x64.je inlined_stdlib.__mm_alloc_needzero_4_0
  inlined_stdlib.__mm_alloc_needzero_3_0:
    x64.mov r8d, 1
    x64.mov r13, [rbp+-1216]
    x64.add r13, 32
  inlined_stdlib.__slab_alloc_needzero_0_0:
    x64.xor r8d, r8d
    x64.cmp r13, 32768
    x64.jle inlined_stdlib.__slab_class_index_for_0_0
  inlined_stdlib.__slab_alloc_needzero_1_0:
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
    x64.call stdlib.__slab_current_p_id
    x64.test r8, r8
    x64.mov r8, 0
    x64.jge inlined_stdlib.__slab_alloc_needzero_4_0
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
  inlined_stdlib.__slab_alloc_needzero_4_0:
    x64.test r8, r8
    x64.je inlined_stdlib.__slab_alloc_needzero_6_0
  inlined_stdlib.__slab_alloc_needzero_5_0:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, r13
    x64.mov rdx, 1
    x64.call stdlib.__slab_alloc_class
    x64.mov r13, r8
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_release
    x64.jmp inline_cont_main_2
  inlined_stdlib.__slab_alloc_needzero_6_0:
    x64.mov rcx, r13
    x64.mov rdx, 1
    x64.call stdlib.__slab_alloc_class
    x64.mov r13, r8
  inline_cont_main_2:
    x64.jmp inlined_stdlib.__mm_alloc_needzero_5_0
  inlined_stdlib.__mm_alloc_needzero_4_0:
    x64.xor r8d, r8d
    x64.mov r13, [rbp+-1216]
    x64.add r13, 32
  inlined_stdlib.__slab_alloc_needzero_0_1:
    x64.xor r8d, r8d
    x64.cmp r13, 32768
    x64.jle inlined_stdlib.__slab_class_index_for_0_1
  inlined_stdlib.__slab_alloc_needzero_1_1:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, r13
    x64.call stdlib.__slab_os_direct_alloc
    x64.mov r13, r8
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_release
    x64.jmp inline_cont_main_5
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
    x64.cmp r8, r13
    x64.jl inlined_stdlib.__slab_class_index_for_6_1
    x64.mov r13, r14
    x64.jmp inline_cont_main_3
  inlined_stdlib.__slab_class_index_for_3_1:
    x64.add r15, 1
    x64.mov r14, rcx
    x64.jmp inlined_stdlib.__slab_class_index_for_1_1
  inlined_stdlib.__slab_class_index_for_4_1:
    x64.mov r8d, 136
    x64.xor r13d, r13d
    x64.mov [rbp-224], r8
    x64.call_import slot_0
    x64.jmp inline_cont_main_3
  inlined_stdlib.__slab_class_index_for_6_1:
    x64.mov rcx, r14
    x64.add rcx, 1
    x64.jmp inlined_stdlib.__slab_class_index_for_3_1
  inline_cont_main_3:
    x64.call stdlib.__slab_current_p_id
    x64.test r8, r8
    x64.mov r8, 0
    x64.jge inlined_stdlib.__slab_alloc_needzero_4_1
  inlined_stdlib.__slab_proc_at_0_1:
    x64.mov r8, 0
    x64.test r8, r8
    x64.jge inlined_stdlib.__slab_proc_at_2_1
  inlined_stdlib.__slab_proc_at_1_1:
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_4
  inlined_stdlib.__slab_proc_at_2_1:
    x64.lea r8, [rip+__sched_procs]
    x64.mov r9, [r8+0] (8b)
    x64.test r9, r9
    x64.jne inlined_stdlib.__slab_proc_at_4_1
  inlined_stdlib.__slab_proc_at_3_1:
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_4
  inlined_stdlib.__slab_proc_at_4_1:
    x64.mov ecx, 3
    x64.mov r8, 0
    x64.shl r8, r8, rcx
    x64.add r9, r8
    x64.mov r8, [r9+0] (8b)
  inline_cont_main_4:
    x64.test r8, r8
    x64.setne r8
  inlined_stdlib.__slab_alloc_needzero_4_1:
    x64.test r8, r8
    x64.je inlined_stdlib.__slab_alloc_needzero_6_1
  inlined_stdlib.__slab_alloc_needzero_5_1:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, r13
    x64.mov rdx, 0
    x64.call stdlib.__slab_alloc_class
    x64.mov r13, r8
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_release
    x64.jmp inline_cont_main_5
  inlined_stdlib.__slab_alloc_needzero_6_1:
    x64.mov rcx, r13
    x64.mov rdx, 0
    x64.call stdlib.__slab_alloc_class
    x64.mov r13, r8
  inline_cont_main_5:
  inlined_stdlib.__mm_alloc_needzero_5_0:
    x64.mov r8, 0
    x64.mov [r13+0], r8 (8b)
    x64.mov r8, r13
    x64.add r8, 8
    x64.mov [r8+0], r12 (8b)
    x64.mov r8, r13
    x64.add r8, 16
    x64.mov r9, [rbp+-1216]
    x64.mov [r8+0], r9 (8b)
    x64.mov r8, r13
    x64.add r8, 24
    x64.mov r9, 0
    x64.mov [r8+0], r9 (8b)
    x64.mov rcx, r13
    x64.add rcx, 32
    x64.mov r12, rcx
  inline_cont_main_6:
    x64.call stdlib.__mm_incref
    x64.mov r8, [rbp+-1200]
    x64.mov [r12+0], r8 (8b)
    x64.mov r8, 0
    x64.mov [r12+8], r8 (8b)
    x64.lea rax, [rip+__layout_Array_String]
    x64.mov rdx, r12
    x64.mov rcx, [rbp+-1192]
    x64.call Array.push
  names_loop_0.step:
    x64.mov rcx, rbx
    x64.add rcx, 1
    x64.mov rbx, rcx
    x64.jmp names_loop_0.header
  names_loop_0.exit:
    x64.lea rdx, [rip+__layout_Array_String]
    x64.mov rcx, [rbp+-1192]
    x64.call Array.count
    x64.mov [rbp+-1200], r8
    x64.mov rcx, [rbp+-1192]
    x64.call __mm_decref_maybenull_helper
    x64.lea rcx, [rip+__layout_Array_Integer]
    x64.lea rbx, [rip+__layout_Array_Integer]
    x64.lea r12, [rip+__layout_Array_Integer]
    x64.mov r8d, 2
    x64.lea r13, [rip+__layout_Array_Integer]
    x64.lea r14, [rip+__layout_Array_Integer]
    x64.lea r8, [rip+__layout_Array_Integer]
    x64.mov [rbp+-1192], r8
    x64.mov r8d, 4
    x64.lea r8, [rip+__layout_Array_IntArray]
    x64.mov [rbp+-1216], r8
    x64.lea r8, [rip+__layout_Array_IntArray]
    x64.mov [rbp+-1224], r8
    x64.lea r8, [rip+__layout_Array_IntArray]
    x64.mov [rbp+-1232], r8
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
    x64.mov rax, [rbp+-1192]
    x64.call Array.push
    x64.mov rcx, [rbp+-1216]
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.mov rdx, r15
    x64.mov rax, [rbp+-1224]
    x64.call Array.push
    x64.mov rcx, r12
    x64.mov rdx, rbx
    x64.mov rax, [rbp+-1232]
    x64.call Array.push
    x64.mov rcx, r12
    x64.call matrix_total
    x64.mov rbx, r8
    x64.mov rcx, r12
    x64.call __mm_decref_maybenull_helper
    x64.mov eax, 1
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r9, 0
    x64.mov [r8+0], r9 (8b)
    x64.mov r9, 0
    x64.mov [r8+8], r9 (8b)
    x64.mov r9, [r8+8] (8b)
    x64.mov [rbp+-1216], r9
    x64.mov r9, [r8+0] (8b)
    x64.mov [rbp+-1224], r9
    x64.mov r9, [r8+8] (8b)
    x64.mov [rbp+-1232], r9
    x64.mov r9, [r8+0] (8b)
    x64.mov [rbp+-1240], r9
    x64.mov rcx, r8
    x64.call mm_drop
    x64.mov eax, 1
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.mov r12d, 10
    x64.mov r13d, 20
    x64.call stdlib.__mm_alloc_needzero
    x64.mov [r8+0], r12 (8b)
    x64.mov [r8+8], r13 (8b)
    x64.mov r12, [r8+0] (8b)
    x64.mov rcx, r8
    x64.call mm_drop
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.lea r13, [rip+__istr_2]
    x64.mov r8d, 1
    x64.lea r14, [rip+__destruct_String]
    x64.mov r8d, 30
    x64.lea r8, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov [rbp+-1192], r8
    x64.lea r8, [rip+__istr_3]
    x64.mov [rbp+-1248], r8
    x64.mov r8d, 1
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-1256], r8
    x64.lea r8, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov [rbp+-1264], r8
    x64.lea r8, [rip+__istr_4]
    x64.mov [rbp+-1272], r8
    x64.mov r8d, 1
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-1280], r8
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov r15, r8
    x64.mov r8, 0
    x64.mov [r15+40], r8 (8b)
    x64.mov [r15+0], r13 (8b)
    x64.mov r8, 5
    x64.mov [r15+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r15+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r15+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r15+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, r14
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov [r13+0], r15 (8b)
    x64.mov r8, 1
    x64.mov [r13+8], r8 (8b)
    x64.mov rcx, r13
    x64.mov rdx, 30
    x64.call Person.create
    x64.mov r13, r8
    x64.mov rcx, 48
    x64.mov rdx, [rbp+-1192]
    x64.call mrt_alloc_with_dtor
    x64.mov r14, r8
    x64.mov r8, 0
    x64.mov [r14+40], r8 (8b)
    x64.mov r8, [rbp+-1248]
    x64.mov [r14+0], r8 (8b)
    x64.mov r8, 3
    x64.mov [r14+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r14+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r14+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r14+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1256]
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r15, r8
    x64.mov rcx, r15
    x64.call stdlib.__mm_incref
    x64.mov [r15+0], r14 (8b)
    x64.mov r8, 1
    x64.mov [r15+8], r8 (8b)
    x64.mov rcx, [r13+0] (8b)
    x64.call __mm_decref_maybenull_helper
    x64.mov [r13+0], r15 (8b)
    x64.mov rcx, 48
    x64.mov rdx, [rbp+-1264]
    x64.call mrt_alloc_with_dtor
    x64.mov r14, r8
    x64.mov r8, 0
    x64.mov [r14+40], r8 (8b)
    x64.mov r8, [rbp+-1272]
    x64.mov [r14+0], r8 (8b)
    x64.mov r8, 5
    x64.mov [r14+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r14+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r14+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r14+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1280]
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r15, r8
    x64.mov rcx, r15
    x64.call stdlib.__mm_incref
    x64.mov [r15+0], r14 (8b)
    x64.mov r8, 1
    x64.mov [r15+8], r8 (8b)
    x64.mov rcx, [r13+0] (8b)
    x64.call __mm_decref_maybenull_helper
    x64.mov [r13+0], r15 (8b)
    x64.mov r14, [r13+8] (8b)
    x64.mov rcx, r13
    x64.call __mm_decref_maybenull_helper
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.lea r13, [rip+__istr_5]
    x64.mov r8d, 4
    x64.mov r8d, 1
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-1192], r8
    x64.mov r8d, 1
    x64.lea r8, [rip+__destruct_Shape]
    x64.mov [rbp+-1248], r8
    x64.mov r8d, 16
    x64.xor r8d, r8d
    x64.lea r8, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov [rbp+-1256], r8
    x64.lea r8, [rip+__istr_6]
    x64.mov [rbp+-1264], r8
    x64.mov r8d, 1
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-1272], r8
    x64.mov r8d, 1
    x64.lea r8, [rip+__destruct_Shape]
    x64.mov [rbp+-1280], r8
    x64.mov r8d, 16
    x64.mov r8d, 1
    x64.mov r8d, 1
    x64.lea r8, [rip+__destruct_Shape]
    x64.mov [rbp+-1288], r8
    x64.mov r8d, 16
    x64.mov r8d, 2
    x64.xor r8d, r8d
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov r15, r8
    x64.mov r8, 0
    x64.mov [r15+40], r8 (8b)
    x64.mov [r15+0], r13 (8b)
    x64.mov r8, 4
    x64.mov [r15+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r15+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r15+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r15+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1192]
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov [r13+0], r15 (8b)
    x64.mov r8, 1
    x64.mov [r13+8], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1248]
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov [rbp+-1192], r8
    x64.mov r8, [rbp+-1192]
    x64.mov r9, 0
    x64.mov [r8+0], r9 (8b)
    x64.mov [r8+8], r13 (8b)
    x64.mov rcx, 48
    x64.mov rdx, [rbp+-1256]
    x64.call mrt_alloc_with_dtor
    x64.mov r13, r8
    x64.mov r8, 0
    x64.mov [r13+40], r8 (8b)
    x64.mov r8, [rbp+-1264]
    x64.mov [r13+0], r8 (8b)
    x64.mov r8, 3
    x64.mov [r13+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r13+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r13+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r13+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1272]
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r15, r8
    x64.mov rcx, r15
    x64.call stdlib.__mm_incref
    x64.mov [r15+0], r13 (8b)
    x64.mov r8, 1
    x64.mov [r15+8], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1280]
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov [rbp+-1248], r8
    x64.mov r8, [rbp+-1248]
    x64.mov r9, 1
    x64.mov [r8+0], r9 (8b)
    x64.mov [r8+8], r15 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1288]
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r13, r8
    x64.mov r8, 2
    x64.mov [r13+0], r8 (8b)
    x64.mov r8, 0
    x64.mov [r13+8], r8 (8b)
    x64.mov rcx, [rbp+-1192]
    x64.call describe
    x64.mov [rbp+-1256], r8
    x64.mov rcx, [rbp+-1192]
    x64.call mm_drop
    x64.mov rcx, [rbp+-1248]
    x64.call describe
    x64.mov [rbp+-1264], r8
    x64.mov rcx, [rbp+-1248]
    x64.call mm_drop
    x64.mov rcx, r13
    x64.call describe
    x64.mov [rbp+-1248], r8
    x64.mov rcx, r13
    x64.call mm_drop
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.lea r13, [rip+__istr_7]
    x64.mov r8d, 4
    x64.mov r15d, 1
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-1192], r8
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov [rbp+-1272], r8
    x64.mov r8, [rbp+-1272]
    x64.mov r9, 0
    x64.mov [r8+40], r9 (8b)
    x64.mov [r8+0], r13 (8b)
    x64.mov r9, 4
    x64.mov [r8+8], r9 (8b)
    x64.mov r9, -2
    x64.mov [r8+16], r9 (8b)
    x64.mov r9, 1
    x64.mov [r8+24], r9 (8b)
    x64.mov r9, 0
    x64.mov [r8+32], r9 (8b)
    x64.mov rcx, 16
    x64.mov rax, r15
    x64.mov rdx, [rbp+-1192]
    x64.call stdlib.__mm_alloc_needzero
    x64.mov [rbp+-1192], r8
    x64.mov rcx, [rbp+-1192]
    x64.call stdlib.__mm_incref
    x64.mov r8, [rbp+-1192]
    x64.mov r9, [rbp+-1272]
    x64.mov [r8+0], r9 (8b)
    x64.mov r9, 1
    x64.mov [r8+8], r9 (8b)
    x64.mov rcx, [rbp-128]
    x64.mov r8d, 1
    x64.mov r8d, 8
    x64.lea rax, [rbp-128]
    x64.mov r8, [rbp-240]
    x64.mov [rbp+-1272], r8
    x64.mov r8d, 7
    x64.lea r13, [rip+main$closure_0]
    x64.mov r15d, 8
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, [rbp+-1192]
    x64.mov [rbp-128], r8
    x64.mov rcx, 8
    x64.mov rdx, 0
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov [rbp+-1192], r8
    x64.mov r8, [rbp+-1192]
    x64.mov r9, [rbp+-1272]
    x64.mov [r8+0], r9 (8b)
    x64.mov rcx, 7
    x64.mov rdx, [rbp+-1192]
    x64.call r13
    x64.mov [rbp+-1272], r8
    x64.mov rcx, r15
    x64.mov rdx, [rbp+-1192]
    x64.call r13
    x64.mov [rbp+-1280], r8
    x64.mov rcx, [rbp+-1192]
    x64.call mm_drop
    x64.lea rcx, [rip+__layout_Array_Point]
    x64.mov r8d, 1
    x64.xor r8d, r8d
    x64.mov r13d, 16
    x64.mov r15d, 2
    x64.lea r8, [rip+__layout_Array_Point]
    x64.mov [rbp+-1192], r8
    x64.mov r8d, 1
    x64.xor r8d, r8d
    x64.mov r8d, 16
    x64.mov r8d, 4
    x64.lea r8, [rip+__layout_Array_Point]
    x64.mov [rbp+-1288], r8
    x64.mov r8d, 1
    x64.xor r8d, r8d
    x64.mov r8d, 16
    x64.mov r8d, 6
    x64.lea r8, [rip+__layout_Array_Point]
    x64.mov [rbp+-1296], r8
    x64.call Array.create
    x64.mov [rbp+-1304], r8
    x64.mov rcx, r13
    x64.mov rdx, 0
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 1
    x64.mov [r13+0], r8 (8b)
    x64.mov [r13+8], r15 (8b)
    x64.mov rdx, r13
    x64.mov rcx, [rbp+-1304]
    x64.mov rax, [rbp+-1192]
    x64.call Array.push
    x64.mov rcx, 16
    x64.mov rdx, 0
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 3
    x64.mov [r13+0], r8 (8b)
    x64.mov r8, 4
    x64.mov [r13+8], r8 (8b)
    x64.mov rdx, r13
    x64.mov rcx, [rbp+-1304]
    x64.mov rax, [rbp+-1288]
    x64.call Array.push
    x64.mov rcx, 16
    x64.mov rdx, 0
    x64.mov rax, 1
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r13, r8
    x64.mov rcx, r13
    x64.call stdlib.__mm_incref
    x64.mov r8, 5
    x64.mov [r13+0], r8 (8b)
    x64.mov r8, 6
    x64.mov [r13+8], r8 (8b)
    x64.mov rdx, r13
    x64.mov rcx, [rbp+-1304]
    x64.mov rax, [rbp+-1296]
    x64.call Array.push
    x64.mov rcx, [rbp+-1304]
    x64.call points_x_sum
    x64.mov r13, r8
    x64.mov rcx, [rbp+-1304]
    x64.call __mm_decref_maybenull_helper
    x64.lea rcx, [rip+__layout_Array_Point]
    x64.call Array.create
    x64.mov [rbp+-1192], r8
    x64.mov eax, 1
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r15, r8
    x64.mov rcx, r15
    x64.call stdlib.__mm_incref
    x64.mov r8d, 7
    x64.mov [r15+0], r8 (8b)
    x64.mov r8d, 8
    x64.mov [r15+8], r8 (8b)
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov rdx, r15
    x64.mov rcx, [rbp+-1192]
    x64.call Array.push
    x64.mov eax, 1
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r15, r8
    x64.mov rcx, r15
    x64.call stdlib.__mm_incref
    x64.mov r8d, 9
    x64.mov [r15+0], r8 (8b)
    x64.mov r8d, 10
    x64.mov [r15+8], r8 (8b)
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov rdx, r15
    x64.mov rcx, [rbp+-1192]
    x64.call Array.push
    x64.mov eax, 1
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc_needzero
    x64.mov r15, r8
    x64.mov rcx, r15
    x64.call stdlib.__mm_incref
    x64.mov r8d, 11
    x64.mov r9, [rbp+-1184]
    x64.add r9, [rbp+-1200]
    x64.mov [r15+0], r8 (8b)
    x64.mov r8, [rbp+-1240]
    x64.add r8, [rbp+-1232]
    x64.add r9, rbx
    x64.mov rsi, [rbp+-1224]
    x64.add rsi, [rbp+-1216]
    x64.add r9, r8
    x64.mov r8d, 12
    x64.add r9, rsi
    x64.mov [r15+8], r8 (8b)
    x64.add r9, r12
    x64.add r9, r14
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov rbx, r9
    x64.add rbx, [rbp+-1256]
    x64.mov rdx, r15
    x64.mov rcx, [rbp+-1192]
    x64.call Array.push
    x64.add rbx, [rbp+-1264]
    x64.add rbx, [rbp+-1248]
    x64.add rbx, [rbp+-1272]
    x64.add rbx, [rbp+-1280]
    x64.add rbx, r13
    x64.mov rdx, 0
    x64.mov r12, rdx
  alias_loop_0.header:
    x64.cmp r12, 3
    x64.jge alias_loop_0.exit
  inlined_Array.get_0_0:
    x64.mov r8, [rbp+-1192]
    x64.mov rcx, [r8+0] (8b)
    x64.mov rdx, r12
    x64.call stdlib.__managed_mem_get
    x64.mov [rbp+-1208], r8
    x64.test rdx, rdx
    x64.je inlined_Array.get_3_0
  inlined_Array.get_1_0:
    x64.mov edx, 1
    x64.xor ecx, ecx
    x64.jmp inline_cont_main_7
  inlined_Array.get_3_0:
    x64.xor r8d, r8d
    x64.jmp __rc_edge_16_0
  inline_cont_main_7:
    x64.test rdx, rdx
    x64.je try_0.merge
    x64.jmp try_0.otherwise
  alias_loop_0.step:
    x64.mov rdx, r12
    x64.add rdx, 1
    x64.mov r12, rdx
    x64.jmp alias_loop_0.header
  alias_loop_0.exit:
    x64.mov rcx, [rbp+-1192]
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
    x64.mov [rbp-248], r15
    x64.mov [rbp-256], r9
    x64.mov [rbp-264], r8
    x64.rep_movsb
    x64.mov r8, r15
    x64.add r8, 75
    x64.mov [rbp-272], r8
    x64.mov [rbp-280], rbx
    x64.mov [rbp-288], r12
    x64.rep_movsb
    x64.lea r9, [rip+__istr_9]
    x64.add r8, r12
    x64.mov [rbp-296], r8
    x64.mov [rbp-304], r9
    x64.mov [rbp-312], r13
    x64.rep_movsb
    x64.lea r9, [rip+__istr_10]
    x64.add r8, 20
    x64.mov [rbp-320], r8
    x64.mov [rbp-328], r9
    x64.mov r8, 1
    x64.mov [rbp-336], r8
    x64.rep_movsb
    x64.mov rcx, rbx
    x64.call stdlib.__mm_decref
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
    x64.mov r8d, 1
    x64.lea r12, [rip+__destruct_String]
  inlined_stdlib.__mm_alloc_needzero_0_1:
    x64.xor r9d, r9d
    x64.mov r9, 16
    x64.cmp r9, 1
    x64.mov r9, 16
    x64.jge __phi_trampoline_25_0
  inlined_stdlib.__mm_alloc_needzero_1_1:
    x64.mov r9d, 1
    x64.mov r13, r9
  inlined_stdlib.__mm_alloc_needzero_2_1:
    x64.lea r9, [rip+__mm_alloc_count]
    x64.lock inc qword ptr [r9]
    x64.test r8, r8
    x64.je inlined_stdlib.__mm_alloc_needzero_4_1
  inlined_stdlib.__mm_alloc_needzero_3_1:
    x64.mov r8d, 1
    x64.mov r8, r13
    x64.mov [rbp+-1184], r8
    x64.mov r8, [rbp+-1184]
    x64.add r8, 32
    x64.mov [rbp+-1184], r8
  inlined_stdlib.__slab_alloc_needzero_0_2:
    x64.xor r8d, r8d
    x64.mov r8, [rbp+-1184]
    x64.cmp r8, 32768
    x64.jle inlined_stdlib.__slab_class_index_for_0_2
  inlined_stdlib.__slab_alloc_needzero_1_2:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, [rbp+-1184]
    x64.call stdlib.__slab_os_direct_alloc
    x64.mov r14, r8
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_release
    x64.jmp inline_cont_main_10
  inlined_stdlib.__slab_class_index_for_0_2:
    x64.xor r8d, r8d
    x64.xor ecx, ecx
    x64.mov r14, rcx
    x64.mov r15, r8
  inlined_stdlib.__slab_class_index_for_1_2:
    x64.cmp r15, 18
    x64.jge inlined_stdlib.__slab_class_index_for_4_2
  inlined_stdlib.__slab_class_index_for_2_2:
    x64.mov rcx, r14
    x64.call stdlib.__slab_class_size
    x64.cmp r8, [rbp+-1184]
    x64.jl inlined_stdlib.__slab_class_index_for_6_2
    x64.jmp inline_cont_main_8
  inlined_stdlib.__slab_class_index_for_3_2:
    x64.add r15, 1
    x64.mov r14, rcx
    x64.jmp inlined_stdlib.__slab_class_index_for_1_2
  inlined_stdlib.__slab_class_index_for_4_2:
    x64.mov r8d, 136
    x64.xor r14d, r14d
    x64.mov [rbp-344], r8
    x64.call_import slot_0
    x64.jmp inline_cont_main_8
  inlined_stdlib.__slab_class_index_for_6_2:
    x64.mov rcx, r14
    x64.add rcx, 1
    x64.jmp inlined_stdlib.__slab_class_index_for_3_2
  inline_cont_main_8:
    x64.call stdlib.__slab_current_p_id
    x64.test r8, r8
    x64.mov r8, 0
    x64.jge inlined_stdlib.__slab_alloc_needzero_4_2
  inlined_stdlib.__slab_proc_at_0_2:
    x64.mov r8, 0
    x64.test r8, r8
    x64.jge inlined_stdlib.__slab_proc_at_2_2
  inlined_stdlib.__slab_proc_at_1_2:
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_9
  inlined_stdlib.__slab_proc_at_2_2:
    x64.lea r8, [rip+__sched_procs]
    x64.mov r9, [r8+0] (8b)
    x64.test r9, r9
    x64.jne inlined_stdlib.__slab_proc_at_4_2
  inlined_stdlib.__slab_proc_at_3_2:
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_9
  inlined_stdlib.__slab_proc_at_4_2:
    x64.mov ecx, 3
    x64.mov r8, 0
    x64.shl r8, r8, rcx
    x64.add r9, r8
    x64.mov r8, [r9+0] (8b)
  inline_cont_main_9:
    x64.test r8, r8
    x64.setne r8
  inlined_stdlib.__slab_alloc_needzero_4_2:
    x64.test r8, r8
    x64.je inlined_stdlib.__slab_alloc_needzero_6_2
  inlined_stdlib.__slab_alloc_needzero_5_2:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, r14
    x64.mov rdx, 1
    x64.call stdlib.__slab_alloc_class
    x64.mov r14, r8
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_release
    x64.jmp inline_cont_main_10
  inlined_stdlib.__slab_alloc_needzero_6_2:
    x64.mov rcx, r14
    x64.mov rdx, 1
    x64.call stdlib.__slab_alloc_class
    x64.mov r14, r8
  inline_cont_main_10:
    x64.jmp inlined_stdlib.__mm_alloc_needzero_5_1
  inlined_stdlib.__mm_alloc_needzero_4_1:
    x64.xor r8d, r8d
    x64.mov r8, r13
    x64.mov [rbp+-1184], r8
    x64.mov r8, [rbp+-1184]
    x64.add r8, 32
    x64.mov [rbp+-1184], r8
  inlined_stdlib.__slab_alloc_needzero_0_3:
    x64.xor r8d, r8d
    x64.mov r8, [rbp+-1184]
    x64.cmp r8, 32768
    x64.jle inlined_stdlib.__slab_class_index_for_0_3
  inlined_stdlib.__slab_alloc_needzero_1_3:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, [rbp+-1184]
    x64.call stdlib.__slab_os_direct_alloc
    x64.mov r14, r8
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_release
    x64.jmp inline_cont_main_13
  inlined_stdlib.__slab_class_index_for_0_3:
    x64.xor r8d, r8d
    x64.xor ecx, ecx
    x64.mov r14, rcx
    x64.mov r15, r8
  inlined_stdlib.__slab_class_index_for_1_3:
    x64.cmp r15, 18
    x64.jge inlined_stdlib.__slab_class_index_for_4_3
  inlined_stdlib.__slab_class_index_for_2_3:
    x64.mov rcx, r14
    x64.call stdlib.__slab_class_size
    x64.cmp r8, [rbp+-1184]
    x64.jl inlined_stdlib.__slab_class_index_for_6_3
    x64.jmp inline_cont_main_11
  inlined_stdlib.__slab_class_index_for_3_3:
    x64.add r15, 1
    x64.mov r14, rcx
    x64.jmp inlined_stdlib.__slab_class_index_for_1_3
  inlined_stdlib.__slab_class_index_for_4_3:
    x64.mov r8d, 136
    x64.xor r14d, r14d
    x64.mov [rbp-360], r8
    x64.call_import slot_0
    x64.jmp inline_cont_main_11
  inlined_stdlib.__slab_class_index_for_6_3:
    x64.mov rcx, r14
    x64.add rcx, 1
    x64.jmp inlined_stdlib.__slab_class_index_for_3_3
  inline_cont_main_11:
    x64.call stdlib.__slab_current_p_id
    x64.test r8, r8
    x64.mov r8, 0
    x64.jge inlined_stdlib.__slab_alloc_needzero_4_3
  inlined_stdlib.__slab_proc_at_0_3:
    x64.mov r8, 0
    x64.test r8, r8
    x64.jge inlined_stdlib.__slab_proc_at_2_3
  inlined_stdlib.__slab_proc_at_1_3:
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_12
  inlined_stdlib.__slab_proc_at_2_3:
    x64.lea r8, [rip+__sched_procs]
    x64.mov r9, [r8+0] (8b)
    x64.test r9, r9
    x64.jne inlined_stdlib.__slab_proc_at_4_3
  inlined_stdlib.__slab_proc_at_3_3:
    x64.xor r8d, r8d
    x64.jmp inline_cont_main_12
  inlined_stdlib.__slab_proc_at_4_3:
    x64.mov ecx, 3
    x64.mov r8, 0
    x64.shl r8, r8, rcx
    x64.add r9, r8
    x64.mov r8, [r9+0] (8b)
  inline_cont_main_12:
    x64.test r8, r8
    x64.setne r8
  inlined_stdlib.__slab_alloc_needzero_4_3:
    x64.test r8, r8
    x64.je inlined_stdlib.__slab_alloc_needzero_6_3
  inlined_stdlib.__slab_alloc_needzero_5_3:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, r14
    x64.mov rdx, 0
    x64.call stdlib.__slab_alloc_class
    x64.mov r14, r8
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_release
    x64.jmp inline_cont_main_13
  inlined_stdlib.__slab_alloc_needzero_6_3:
    x64.mov rcx, r14
    x64.mov rdx, 0
    x64.call stdlib.__slab_alloc_class
    x64.mov r14, r8
  inline_cont_main_13:
  inlined_stdlib.__mm_alloc_needzero_5_1:
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
    x64.mov rcx, r14
    x64.add rcx, 32
    x64.mov r12, rcx
  inline_cont_main_14:
    x64.call stdlib.__mm_incref
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
  __rc_edge_16_0:
    x64.mov rcx, [rbp+-1208]
    x64.call stdlib.__mm_incref
    x64.mov rcx, [rbp+-1208]
    x64.mov rdx, 0
    x64.jmp inline_cont_main_7
  __phi_trampoline_25_0:
    x64.mov r13, r9
    x64.jmp inlined_stdlib.__mm_alloc_needzero_2_1
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

```RequiredIR:wasm32-wasi
module {
  func @Person.create(local0: i64, local1: i64) -> i64 {
  entry:
    %8 = mir.param local0 : i64
    %9 = mir.param local1 : i64
    %2 = mir.mov_imm 16 : i64
    %4 = mir.func_addr @__destruct_Person
    %10 = mir.mov_imm 1 : i64
    %11 = mir.call @stdlib.__mm_alloc_needzero(%2, %4, %10)
    %12 = mir.call @stdlib.__mm_incref(%11)
    %5 = mir.mov_imm 0 : i64
    mir.store %5, %11, 0 width: qword
    mir.store %8, %11, 0 width: qword
    mir.store %9, %11, 8 width: qword
    mir.ret %11
  }
  func @describe(local0: i64) -> i64 {
  entry:
    %23 = mir.param local0 : i64
    %4 = mir.mov_imm 0 : i64
    %5 = mir.load %23, 0 width: qword
    %6 = mir.mov_imm 0 : i64
    %7 = mir.cmp eq, %5, %6
    mir.cond_br %7 [then: describe_0.case0(), else: describe_0.next0()]
  describe_0.merge(%22: i64):
    mir.ret %22
  describe_0.case0:
    %9 = mir.load %23, 8 width: qword
    %11 = mir.call @String.count(%9)
    mir.br describe_0.merge(%11)
  describe_0.next0:
    %12 = mir.load %23, 0 width: qword
    %13 = mir.mov_imm 1 : i64
    %14 = mir.cmp eq, %12, %13
    mir.cond_br %14 [then: describe_0.case1(), else: describe_0.next1()]
  describe_0.case1:
    %15 = mir.load %23, 8 width: qword
    %17 = mir.call @String.count(%15)
    mir.br describe_0.merge(%17)
  describe_0.next1:
    %18 = mir.load %23, 0 width: qword
    %19 = mir.mov_imm 2 : i64
    %20 = mir.cmp eq, %18, %19
    mir.cond_br %20 [then: describe_0.case2(), else: describe_0.merge(%4)]
  describe_0.case2:
    %21 = mir.mov_imm 0 : i64
    mir.br describe_0.merge(%21)
  }
  func @row_total(local0: i64) -> i64 {
  entry:
    %24 = mir.param local0 : i64
    %2 = mir.mov_imm 0 : i64
    %4 = mir.global_addr @__layout_Array_Integer
    mir.br inlined_Array.createIterator_0_0()
  inlined_Array.createIterator_0_0:
    %25 = mir.load %24, 0 width: qword
    %55 = mir.mov_imm 8 : i64
    %56 = mir.sub.i64 %25, %55
    mir.atomic_inc %56
    %27, %28 = mir.try_call @ArrayIterator.create(%25, %4)
    %29 = mir.mov_imm 0 : i64
    %30 = mir.cmp ne, %28, %29
    mir.cond_br %30 [then: inlined_Array.createIterator_1_0(), else: inlined_Array.createIterator_2_0()]
  inlined_Array.createIterator_1_0:
    %31 = mir.mov_imm 0 : i64
    mir.br inline_cont_row_total_0(%31, %28)
  inlined_Array.createIterator_2_0:
    %32 = mir.mov_imm 0 : i64
    mir.br inline_cont_row_total_0(%27, %32)
  inline_cont_row_total_0(%33: i64, %34: i64):
    %7 = mir.mov_imm 0 : i64
    %8 = mir.cmp ne, %34, %7
    mir.cond_br %8 [then: __rc_edge_8_0(), else: iter_0(%2)]
  inlined_ArrayIterator.advance_0_0:
    %35 = mir.load %33, 0 width: qword
    %36 = mir.load %35, 8 width: qword
    %37 = mir.load %35, 16 width: qword
    %38 = mir.mov_imm 1 : i64
    %39 = mir.add.i64 %36, %38
    %40 = mir.cmp lt, %39, %37
    %41 = mir.sub.i64 %39, %36
    %42 = mir.mul.i64 %40, %41
    %43 = mir.add.i64 %36, %42
    mir.store %43, %35, 8 width: qword
    %44 = mir.mov_imm 1 : i64
    %45 = mir.sub.i64 %44, %40
    %46 = mir.mov_imm 1 : i64
    %47 = mir.mul.i64 %45, %46
    %48 = mir.mov_imm 0 : i64
    %49 = mir.cmp ne, %47, %48
    mir.cond_br %49 [then: inlined_ArrayIterator.advance_1_0(), else: inline_cont_row_total_2(%48, %48)]
  inlined_ArrayIterator.advance_1_0:
    %50 = mir.mov_imm 1 : i64
    mir.br inline_cont_row_total_2(%48, %50)
  inline_cont_row_total_2(%51: i64, %52: i64):
    %13 = mir.mov_imm 0 : i64
    %14 = mir.cmp ne, %52, %13
    mir.cond_br %14 [then: __rc_edge_12_0(), else: iter_0(%20)]
  iter_0(%22: i64):
    %53 = mir.load %33, 0 width: qword
    mir.br inlined_stdlib.__managed_mem_cursor_current_0_0()
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    %57 = mir.load %53, 0 width: qword
    %58 = mir.mov_imm 8 : i64
    %59 = mir.add.i64 %53, %58
    %60 = mir.load %59, 0 width: qword
    %61 = mir.mov_imm 24 : i64
    %62 = mir.add.i64 %53, %61
    %63 = mir.load %62, 0 width: qword
    %70 = mir.mov_imm 0 : i64
    %71 = mir.cmp lt, %63, %70
    mir.cond_br %71 [then: inlined_stdlib.__managed_mem_cursor_current_1_0(), else: inlined_stdlib.__managed_mem_cursor_current_2_0()]
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    %99 = mir.mov_imm 0 : i64
    %100 = mir.sub.i64 %99, %63
    %73 = mir.mul.i64 %60, %100
    %74 = mir.mov_imm 3 : i64
    %75 = mir.shr.i64 %73, %74
    %76 = mir.add.i64 %57, %75
    %77 = mir.mov_imm 0 : i64
    %78 = mir.load_byte %76, %77
    %79 = mir.mov_imm 1 : i64
    %80 = mir.shl.i64 %79, %100
    %81 = mir.mov_imm 1 : i64
    %82 = mir.sub.i64 %80, %81
    %83 = mir.mov_imm 7 : i64
    %84 = mir.and.i64 %73, %83
    %85 = mir.shr.i64 %78, %84
    %86 = mir.and.i64 %85, %82
    mir.br inline_cont_row_total_3(%86)
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    %66 = mir.mul.i64 %60, %63
    %67 = mir.add.i64 %57, %66
    mir.br inlined_stdlib.__managed_mem_load_sized_0_0()
  inlined_stdlib.__managed_mem_load_sized_0_0:
    %87 = mir.mov_imm 1 : i64
    %88 = mir.cmp eq, %63, %87
    mir.cond_br %88 [then: inlined_stdlib.__managed_mem_load_sized_1_0(), else: inlined_stdlib.__managed_mem_load_sized_2_0()]
  inlined_stdlib.__managed_mem_load_sized_1_0:
    %89 = mir.mov_imm 0 : i64
    %90 = mir.load_byte %67, %89
    mir.br inline_cont_row_total_15(%90)
  inlined_stdlib.__managed_mem_load_sized_2_0:
    %91 = mir.mov_imm 2 : i64
    %92 = mir.cmp eq, %63, %91
    mir.cond_br %92 [then: inlined_stdlib.__managed_mem_load_sized_3_0(), else: inlined_stdlib.__managed_mem_load_sized_4_0()]
  inlined_stdlib.__managed_mem_load_sized_3_0:
    %93 = mir.load %67, 0 width: halfword
    mir.br inline_cont_row_total_15(%93)
  inlined_stdlib.__managed_mem_load_sized_4_0:
    %94 = mir.mov_imm 4 : i64
    %95 = mir.cmp eq, %63, %94
    mir.cond_br %95 [then: inlined_stdlib.__managed_mem_load_sized_5_0(), else: inlined_stdlib.__managed_mem_load_sized_6_0()]
  inlined_stdlib.__managed_mem_load_sized_5_0:
    %96 = mir.load %67, 0 width: word
    mir.br inline_cont_row_total_15(%96)
  inlined_stdlib.__managed_mem_load_sized_6_0:
    %97 = mir.load %67, 0 width: qword
    mir.br inline_cont_row_total_15(%97)
  inline_cont_row_total_15(%98: i64):
    mir.br inline_cont_row_total_3(%98)
  inline_cont_row_total_3(%69: i64):
    %20 = mir.add.i64 %22, %69
    mir.br inlined_ArrayIterator.advance_0_0()
  iter_0.exit(%23: i64):
    mir.ret %23
  __rc_edge_8_0:
    %101 = mir.call @__mm_decref_maybenull_helper(%33)
    mir.br iter_0.exit(%2)
  __rc_edge_12_0:
    %102 = mir.call @__mm_decref_maybenull_helper(%33)
    mir.br iter_0.exit(%20)
  }
  func @matrix_total(local0: i64) -> i64 {
  entry:
    %26 = mir.param local0 : i64
    %3 = mir.mov_imm 0 : i64
    %5 = mir.global_addr @__layout_Array_IntArray
    mir.br inlined_Array.createIterator_0_0()
  inlined_Array.createIterator_0_0:
    %27 = mir.load %26, 0 width: qword
    %57 = mir.mov_imm 8 : i64
    %58 = mir.sub.i64 %27, %57
    mir.atomic_inc %58
    %29, %30 = mir.try_call @ArrayIterator.create(%27, %5)
    %31 = mir.mov_imm 0 : i64
    %32 = mir.cmp ne, %30, %31
    mir.cond_br %32 [then: inlined_Array.createIterator_1_0(), else: inlined_Array.createIterator_2_0()]
  inlined_Array.createIterator_1_0:
    %33 = mir.mov_imm 0 : i64
    mir.br inline_cont_matrix_total_0(%33, %30)
  inlined_Array.createIterator_2_0:
    %34 = mir.mov_imm 0 : i64
    mir.br inline_cont_matrix_total_0(%29, %34)
  inline_cont_matrix_total_0(%35: i64, %36: i64):
    %8 = mir.mov_imm 0 : i64
    %9 = mir.cmp ne, %36, %8
    mir.cond_br %9 [then: __rc_edge_8_0(), else: iter_0(%3)]
  inlined_ArrayIterator.advance_0_0:
    %37 = mir.load %35, 0 width: qword
    %38 = mir.load %37, 8 width: qword
    %39 = mir.load %37, 16 width: qword
    %40 = mir.mov_imm 1 : i64
    %41 = mir.add.i64 %38, %40
    %42 = mir.cmp lt, %41, %39
    %43 = mir.sub.i64 %41, %38
    %44 = mir.mul.i64 %42, %43
    %45 = mir.add.i64 %38, %44
    mir.store %45, %37, 8 width: qword
    %46 = mir.mov_imm 1 : i64
    %47 = mir.sub.i64 %46, %42
    %48 = mir.mov_imm 1 : i64
    %49 = mir.mul.i64 %47, %48
    %50 = mir.mov_imm 0 : i64
    %51 = mir.cmp ne, %49, %50
    mir.cond_br %51 [then: inlined_ArrayIterator.advance_1_0(), else: inline_cont_matrix_total_2(%50, %50)]
  inlined_ArrayIterator.advance_1_0:
    %52 = mir.mov_imm 1 : i64
    mir.br inline_cont_matrix_total_2(%50, %52)
  inline_cont_matrix_total_2(%53: i64, %54: i64):
    %14 = mir.mov_imm 0 : i64
    %15 = mir.cmp ne, %54, %14
    mir.cond_br %15 [then: __rc_edge_12_0(), else: iter_0(%22)]
  iter_0(%24: i64):
    %55 = mir.load %35, 0 width: qword
    mir.br inlined_stdlib.__managed_mem_cursor_current_0_0()
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    %59 = mir.load %55, 0 width: qword
    %60 = mir.mov_imm 8 : i64
    %61 = mir.add.i64 %55, %60
    %62 = mir.load %61, 0 width: qword
    %63 = mir.mov_imm 24 : i64
    %64 = mir.add.i64 %55, %63
    %65 = mir.load %64, 0 width: qword
    %72 = mir.mov_imm 0 : i64
    %73 = mir.cmp lt, %65, %72
    mir.cond_br %73 [then: inlined_stdlib.__managed_mem_cursor_current_1_0(), else: inlined_stdlib.__managed_mem_cursor_current_2_0()]
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    %101 = mir.mov_imm 0 : i64
    %102 = mir.sub.i64 %101, %65
    %75 = mir.mul.i64 %62, %102
    %76 = mir.mov_imm 3 : i64
    %77 = mir.shr.i64 %75, %76
    %78 = mir.add.i64 %59, %77
    %79 = mir.mov_imm 0 : i64
    %80 = mir.load_byte %78, %79
    %81 = mir.mov_imm 1 : i64
    %82 = mir.shl.i64 %81, %102
    %83 = mir.mov_imm 1 : i64
    %84 = mir.sub.i64 %82, %83
    %85 = mir.mov_imm 7 : i64
    %86 = mir.and.i64 %75, %85
    %87 = mir.shr.i64 %80, %86
    %88 = mir.and.i64 %87, %84
    mir.br __rc_edge_14_0()
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    %68 = mir.mul.i64 %62, %65
    %69 = mir.add.i64 %59, %68
    mir.br inlined_stdlib.__managed_mem_load_sized_0_0()
  inlined_stdlib.__managed_mem_load_sized_0_0:
    %89 = mir.mov_imm 1 : i64
    %90 = mir.cmp eq, %65, %89
    mir.cond_br %90 [then: inlined_stdlib.__managed_mem_load_sized_1_0(), else: inlined_stdlib.__managed_mem_load_sized_2_0()]
  inlined_stdlib.__managed_mem_load_sized_1_0:
    %91 = mir.mov_imm 0 : i64
    %92 = mir.load_byte %69, %91
    mir.br inline_cont_matrix_total_15(%92)
  inlined_stdlib.__managed_mem_load_sized_2_0:
    %93 = mir.mov_imm 2 : i64
    %94 = mir.cmp eq, %65, %93
    mir.cond_br %94 [then: inlined_stdlib.__managed_mem_load_sized_3_0(), else: inlined_stdlib.__managed_mem_load_sized_4_0()]
  inlined_stdlib.__managed_mem_load_sized_3_0:
    %95 = mir.load %69, 0 width: halfword
    mir.br inline_cont_matrix_total_15(%95)
  inlined_stdlib.__managed_mem_load_sized_4_0:
    %96 = mir.mov_imm 4 : i64
    %97 = mir.cmp eq, %65, %96
    mir.cond_br %97 [then: inlined_stdlib.__managed_mem_load_sized_5_0(), else: inlined_stdlib.__managed_mem_load_sized_6_0()]
  inlined_stdlib.__managed_mem_load_sized_5_0:
    %98 = mir.load %69, 0 width: word
    mir.br inline_cont_matrix_total_15(%98)
  inlined_stdlib.__managed_mem_load_sized_6_0:
    %99 = mir.load %69, 0 width: qword
    mir.br inline_cont_matrix_total_15(%99)
  inline_cont_matrix_total_15(%100: i64):
    mir.br __rc_edge_24_0()
  inline_cont_matrix_total_3(%71: i64):
    %21 = mir.call @row_total(%71)
    %103 = mir.call @__mm_decref_maybenull_helper(%71)
    %22 = mir.add.i64 %24, %21
    mir.br inlined_ArrayIterator.advance_0_0()
  iter_0.exit(%25: i64):
    mir.ret %25
  __rc_edge_8_0:
    %104 = mir.call @__mm_decref_maybenull_helper(%35)
    mir.br iter_0.exit(%3)
  __rc_edge_12_0:
    %105 = mir.call @__mm_decref_maybenull_helper(%35)
    mir.br iter_0.exit(%22)
  __rc_edge_14_0:
    %106 = mir.call @stdlib.__mm_incref(%88)
    mir.br inline_cont_matrix_total_3(%88)
  __rc_edge_24_0:
    %107 = mir.call @stdlib.__mm_incref(%100)
    mir.br inline_cont_matrix_total_3(%100)
  }
  func @points_x_sum(local0: i64) -> i64 {
  entry:
    %26 = mir.param local0 : i64
    %3 = mir.mov_imm 0 : i64
    %5 = mir.global_addr @__layout_Array_Point
    mir.br inlined_Array.createIterator_0_0()
  inlined_Array.createIterator_0_0:
    %27 = mir.load %26, 0 width: qword
    %57 = mir.mov_imm 8 : i64
    %58 = mir.sub.i64 %27, %57
    mir.atomic_inc %58
    %29, %30 = mir.try_call @ArrayIterator.create(%27, %5)
    %31 = mir.mov_imm 0 : i64
    %32 = mir.cmp ne, %30, %31
    mir.cond_br %32 [then: inlined_Array.createIterator_1_0(), else: inlined_Array.createIterator_2_0()]
  inlined_Array.createIterator_1_0:
    %33 = mir.mov_imm 0 : i64
    mir.br inline_cont_points_x_sum_0(%33, %30)
  inlined_Array.createIterator_2_0:
    %34 = mir.mov_imm 0 : i64
    mir.br inline_cont_points_x_sum_0(%29, %34)
  inline_cont_points_x_sum_0(%35: i64, %36: i64):
    %8 = mir.mov_imm 0 : i64
    %9 = mir.cmp ne, %36, %8
    mir.cond_br %9 [then: __rc_edge_8_0(), else: iter_0(%3)]
  inlined_ArrayIterator.advance_0_0:
    %37 = mir.load %35, 0 width: qword
    %38 = mir.load %37, 8 width: qword
    %39 = mir.load %37, 16 width: qword
    %40 = mir.mov_imm 1 : i64
    %41 = mir.add.i64 %38, %40
    %42 = mir.cmp lt, %41, %39
    %43 = mir.sub.i64 %41, %38
    %44 = mir.mul.i64 %42, %43
    %45 = mir.add.i64 %38, %44
    mir.store %45, %37, 8 width: qword
    %46 = mir.mov_imm 1 : i64
    %47 = mir.sub.i64 %46, %42
    %48 = mir.mov_imm 1 : i64
    %49 = mir.mul.i64 %47, %48
    %50 = mir.mov_imm 0 : i64
    %51 = mir.cmp ne, %49, %50
    mir.cond_br %51 [then: inlined_ArrayIterator.advance_1_0(), else: inline_cont_points_x_sum_2(%50, %50)]
  inlined_ArrayIterator.advance_1_0:
    %52 = mir.mov_imm 1 : i64
    mir.br inline_cont_points_x_sum_2(%50, %52)
  inline_cont_points_x_sum_2(%53: i64, %54: i64):
    %14 = mir.mov_imm 0 : i64
    %15 = mir.cmp ne, %54, %14
    mir.cond_br %15 [then: __rc_edge_12_0(), else: iter_0(%22)]
  iter_0(%24: i64):
    %55 = mir.load %35, 0 width: qword
    mir.br inlined_stdlib.__managed_mem_cursor_current_0_0()
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    %59 = mir.load %55, 0 width: qword
    %60 = mir.mov_imm 8 : i64
    %61 = mir.add.i64 %55, %60
    %62 = mir.load %61, 0 width: qword
    %63 = mir.mov_imm 24 : i64
    %64 = mir.add.i64 %55, %63
    %65 = mir.load %64, 0 width: qword
    %72 = mir.mov_imm 0 : i64
    %73 = mir.cmp lt, %65, %72
    mir.cond_br %73 [then: inlined_stdlib.__managed_mem_cursor_current_1_0(), else: inlined_stdlib.__managed_mem_cursor_current_2_0()]
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    %101 = mir.mov_imm 0 : i64
    %102 = mir.sub.i64 %101, %65
    %75 = mir.mul.i64 %62, %102
    %76 = mir.mov_imm 3 : i64
    %77 = mir.shr.i64 %75, %76
    %78 = mir.add.i64 %59, %77
    %79 = mir.mov_imm 0 : i64
    %80 = mir.load_byte %78, %79
    %81 = mir.mov_imm 1 : i64
    %82 = mir.shl.i64 %81, %102
    %83 = mir.mov_imm 1 : i64
    %84 = mir.sub.i64 %82, %83
    %85 = mir.mov_imm 7 : i64
    %86 = mir.and.i64 %75, %85
    %87 = mir.shr.i64 %80, %86
    %88 = mir.and.i64 %87, %84
    mir.br __rc_edge_14_0()
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    %68 = mir.mul.i64 %62, %65
    %69 = mir.add.i64 %59, %68
    mir.br inlined_stdlib.__managed_mem_load_sized_0_0()
  inlined_stdlib.__managed_mem_load_sized_0_0:
    %89 = mir.mov_imm 1 : i64
    %90 = mir.cmp eq, %65, %89
    mir.cond_br %90 [then: inlined_stdlib.__managed_mem_load_sized_1_0(), else: inlined_stdlib.__managed_mem_load_sized_2_0()]
  inlined_stdlib.__managed_mem_load_sized_1_0:
    %91 = mir.mov_imm 0 : i64
    %92 = mir.load_byte %69, %91
    mir.br inline_cont_points_x_sum_15(%92)
  inlined_stdlib.__managed_mem_load_sized_2_0:
    %93 = mir.mov_imm 2 : i64
    %94 = mir.cmp eq, %65, %93
    mir.cond_br %94 [then: inlined_stdlib.__managed_mem_load_sized_3_0(), else: inlined_stdlib.__managed_mem_load_sized_4_0()]
  inlined_stdlib.__managed_mem_load_sized_3_0:
    %95 = mir.load %69, 0 width: halfword
    mir.br inline_cont_points_x_sum_15(%95)
  inlined_stdlib.__managed_mem_load_sized_4_0:
    %96 = mir.mov_imm 4 : i64
    %97 = mir.cmp eq, %65, %96
    mir.cond_br %97 [then: inlined_stdlib.__managed_mem_load_sized_5_0(), else: inlined_stdlib.__managed_mem_load_sized_6_0()]
  inlined_stdlib.__managed_mem_load_sized_5_0:
    %98 = mir.load %69, 0 width: word
    mir.br inline_cont_points_x_sum_15(%98)
  inlined_stdlib.__managed_mem_load_sized_6_0:
    %99 = mir.load %69, 0 width: qword
    mir.br inline_cont_points_x_sum_15(%99)
  inline_cont_points_x_sum_15(%100: i64):
    mir.br __rc_edge_24_0()
  inline_cont_points_x_sum_3(%71: i64):
    %21 = mir.load %71, 0 width: qword
    %22 = mir.add.i64 %24, %21
    %103 = mir.call @__mm_decref_maybenull_helper(%71)
    mir.br inlined_ArrayIterator.advance_0_0()
  iter_0.exit(%25: i64):
    mir.ret %25
  __rc_edge_8_0:
    %104 = mir.call @__mm_decref_maybenull_helper(%35)
    mir.br iter_0.exit(%3)
  __rc_edge_12_0:
    %105 = mir.call @__mm_decref_maybenull_helper(%35)
    mir.br iter_0.exit(%22)
  __rc_edge_14_0:
    %106 = mir.call @stdlib.__mm_incref(%88)
    mir.br inline_cont_points_x_sum_3(%88)
  __rc_edge_24_0:
    %107 = mir.call @stdlib.__mm_incref(%100)
    mir.br inline_cont_points_x_sum_3(%100)
  }
  func @main$closure_0(local0: i64, local1: i64) -> u64 {
  entry:
    %44 = mir.param local0 : i64
    %45 = mir.param local1 : i64
    %3 = mir.load %45, 0 width: qword
    %4 = mir.load %3, 0 width: qword
    %6 = mir.global_addr @__istr_0
    %7 = mir.mov_imm 0 : i64
    %8 = mir.load %4, 0 width: qword
    %9 = mir.load %8, 0 width: qword
    %10 = mir.load %8, 8 width: qword
    %11 = mir.global_addr @__istr_0
    %13 = mir.mov_imm 21 : i64
    %14 = mir.call @mrt_alloc(%13)
    %15 = mir.call @mrt_i64_to_string(%44, %14)
    %16 = mir.global_addr @__istr_0
    %20 = mir.add.i64 %7, %10
    %21 = mir.add.i64 %20, %7
    %22 = mir.add.i64 %21, %15
    %23 = mir.add.i64 %22, %7
    %24 = mir.mov_imm 1 : i64
    %25 = mir.add.i64 %23, %24
    %26 = mir.call @mrt_alloc(%25)
    mir.memcpy %26, %6, %7
    %27 = mir.add.i64 %26, %7
    mir.memcpy %27, %9, %10
    %28 = mir.add.i64 %27, %10
    mir.memcpy %28, %11, %7
    %29 = mir.add.i64 %28, %7
    mir.memcpy %29, %14, %15
    %30 = mir.add.i64 %29, %15
    mir.memcpy %30, %16, %7
    %32 = mir.call @stdlib.__mm_decref(%14)
    %33 = mir.mov_imm 48 : i64
    %34 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %35 = mir.call @mrt_alloc_with_dtor(%33, %34)
    mir.store %7, %35, 40 width: qword
    mir.store %26, %35, 0 width: qword
    mir.store %23, %35, 8 width: qword
    mir.store %23, %35, 16 width: qword
    %37 = mir.mov_imm 1 : i64
    mir.store %37, %35, 24 width: qword
    %38 = mir.mov_imm -1 : i64
    mir.store %38, %35, 32 width: qword
    %40 = mir.mov_imm 16 : i64
    %41 = mir.func_addr @__destruct_String
    %46 = mir.mov_imm 1 : i64
    %47 = mir.call @stdlib.__mm_alloc_needzero(%40, %41, %46)
    mir.store %35, %47, 0 width: qword
    mir.store %7, %47, 8 width: qword
    %43 = mir.call @String.count(%47)
    %48 = mir.call @mm_drop(%47)
    %49 = mir.mov_imm -1 : i64
    %50 = mir.cmp ugt, %43, %49
    mir.cond_br %50 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %51 = mir.global_addr @__panic_msg_8e407baaf3c984cf
    %52 = mir.call @mrt_panic(%51)
  __range_ok_0:
    mir.ret %43
  }
  func @main() -> u8 {
  entry:
    %370 = mir.mov_imm -2 : i64
    %371 = mir.mov_imm 16 : i64
    %372 = mir.mov_imm 48 : i64
    %0 = mir.mov_imm 0 : i64
    mir.store_slot slot_15, %0
    %18 = mir.mov_imm 1 : i64
    %19 = mir.mov_imm 2 : i64
    %373 = mir.mov_imm 16 : i64
    %374 = mir.mov_imm 0 : i64
    %375 = mir.mov_imm 1 : i64
    %376 = mir.call @stdlib.__mm_alloc_needzero(%373, %374, %375)
    mir.store %18, %376, 0 width: qword
    mir.store %19, %376, 8 width: qword
    %23 = mir.mov_imm 99 : i64
    mir.store %23, %376, 0 width: qword
    %27 = mir.load %376, 0 width: qword
    %28 = mir.add.i64 %0, %27
    %726 = mir.call @mm_drop(%376)
    %30 = mir.mov_imm 3 : i64
    %31 = mir.mov_imm 4 : i64
    %377 = mir.mov_imm 16 : i64
    %378 = mir.mov_imm 0 : i64
    %379 = mir.mov_imm 1 : i64
    %380 = mir.call @stdlib.__mm_alloc_needzero(%377, %378, %379)
    mir.store %30, %380, 0 width: qword
    mir.store %31, %380, 8 width: qword
    %381 = mir.load %380, 0 width: qword
    %382 = mir.load %380, 8 width: qword
    %383 = mir.add.i64 %381, %382
    %34 = mir.add.i64 %28, %383
    %727 = mir.call @mm_drop(%380)
    %36 = mir.mov_imm 5 : i64
    %37 = mir.mov_imm 6 : i64
    %384 = mir.mov_imm 16 : i64
    %385 = mir.mov_imm 0 : i64
    %386 = mir.mov_imm 1 : i64
    %387 = mir.call @stdlib.__mm_alloc_needzero(%384, %385, %386)
    mir.store %36, %387, 0 width: qword
    mir.store %37, %387, 8 width: qword
    %388 = mir.load %387, 0 width: qword
    %389 = mir.load %387, 8 width: qword
    %390 = mir.add.i64 %388, %389
    %40 = mir.add.i64 %34, %390
    %728 = mir.call @mm_drop(%387)
    %41 = mir.global_addr @__layout_Array_String
    %42 = mir.call @Array.create(%41)
    mir.br names_loop_0.header(%0)
  names_loop_0.header(%367: i64):
    %47 = mir.cmp lt, %367, %36
    mir.cond_br %47 [then: names_loop_0(), else: names_loop_0.exit()]
  names_loop_0:
    %50 = mir.global_addr @__istr_1
    %52 = mir.mov_imm 21 : i64
    %53 = mir.call @mrt_alloc(%52)
    %54 = mir.call @mrt_i64_to_string(%367, %53)
    %55 = mir.global_addr @__istr_0
    %59 = mir.add.i64 %36, %54
    %60 = mir.add.i64 %59, %0
    %62 = mir.add.i64 %60, %18
    %63 = mir.call @mrt_alloc(%62)
    mir.memcpy %63, %50, %36
    %64 = mir.add.i64 %63, %36
    mir.memcpy %64, %53, %54
    %65 = mir.add.i64 %64, %54
    mir.memcpy %65, %55, %0
    %67 = mir.call @stdlib.__mm_decref(%53)
    %69 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %70 = mir.call @mrt_alloc_with_dtor(%372, %69)
    mir.store %0, %70, 40 width: qword
    mir.store %63, %70, 0 width: qword
    mir.store %60, %70, 8 width: qword
    mir.store %60, %70, 16 width: qword
    mir.store %18, %70, 24 width: qword
    %73 = mir.mov_imm -1 : i64
    mir.store %73, %70, 32 width: qword
    %76 = mir.func_addr @__destruct_String
    %391 = mir.mov_imm 1 : i64
    mir.br inlined_stdlib.__mm_alloc_needzero_0_0()
  inlined_stdlib.__mm_alloc_needzero_0_0:
    %480 = mir.mov_imm 0 : i64
    %481 = mir.mov_imm 1 : i64
    %482 = mir.cmp lt, %371, %481
    mir.cond_br %482 [then: inlined_stdlib.__mm_alloc_needzero_1_0(), else: inlined_stdlib.__mm_alloc_needzero_2_20(%371)]
  inlined_stdlib.__mm_alloc_needzero_1_0:
    %483 = mir.mov_imm 1 : i64
    mir.br inlined_stdlib.__mm_alloc_needzero_2_20(%483)
  inlined_stdlib.__mm_alloc_needzero_2_20(%484: i64):
    %485 = mir.global_addr @__mm_alloc_count
    mir.atomic_inc %485
    %486 = mir.cmp ne, %391, %480
    mir.cond_br %486 [then: inlined_stdlib.__mm_alloc_needzero_3_0(), else: inlined_stdlib.__mm_alloc_needzero_4_0()]
  inlined_stdlib.__mm_alloc_needzero_3_0:
    %487 = mir.mov_imm 32 : i64
    %488 = mir.add.i64 %484, %487
    %526 = mir.mov_imm 1 : i64
    mir.br inlined_stdlib.__slab_alloc_needzero_0_0()
  inlined_stdlib.__slab_alloc_needzero_0_0:
    %534 = mir.mov_imm 0 : i64
    %535 = mir.mov_imm 32768 : i64
    %536 = mir.cmp gt, %488, %535
    mir.cond_br %536 [then: inlined_stdlib.__slab_alloc_needzero_1_0(), else: inlined_stdlib.__slab_class_index_for_0_0()]
  inlined_stdlib.__slab_alloc_needzero_1_0:
    %537 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %537
    %538 = mir.call @stdlib.__slab_os_direct_alloc(%488)
    %539 = mir.global_addr @__slab_lock
    mir.os_lock_release %539
    mir.br inline_cont_main_21(%538)
  inlined_stdlib.__slab_class_index_for_0_0:
    %602 = mir.mov_imm 0 : i64
    %603 = mir.mov_imm 0 : i64
    %604 = mir.mov_imm 18 : i64
    mir.br inlined_stdlib.__slab_class_index_for_1_65(%602, %603)
  inlined_stdlib.__slab_class_index_for_1_65(%605: i64, %606: i64):
    %607 = mir.cmp lt, %606, %604
    mir.cond_br %607 [then: inlined_stdlib.__slab_class_index_for_2_0(), else: inlined_stdlib.__slab_class_index_for_4_0()]
  inlined_stdlib.__slab_class_index_for_2_0:
    %608 = mir.call @stdlib.__slab_class_size(%605)
    %609 = mir.cmp ge, %608, %488
    mir.cond_br %609 [then: inline_cont_main_34(%605), else: inlined_stdlib.__slab_class_index_for_6_0()]
  inlined_stdlib.__slab_class_index_for_3_0:
    %610 = mir.mov_imm 1 : i64
    %611 = mir.add.i64 %606, %610
    mir.br inlined_stdlib.__slab_class_index_for_1_65(%615, %611)
  inlined_stdlib.__slab_class_index_for_4_0:
    %612 = mir.mov_imm 136 : i64
    mir.os_exit %612
    %613 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_34(%613)
  inlined_stdlib.__slab_class_index_for_6_0:
    %614 = mir.mov_imm 1 : i64
    %615 = mir.add.i64 %605, %614
    mir.br inlined_stdlib.__slab_class_index_for_3_0()
  inline_cont_main_34(%616: i64):
    %541 = mir.call @stdlib.__slab_current_p_id()
    %542 = mir.cmp lt, %541, %534
    mir.cond_br %542 [then: inlined_stdlib.__slab_proc_at_0_0(), else: inlined_stdlib.__slab_alloc_needzero_4_36(%534)]
  inlined_stdlib.__slab_proc_at_0_0:
    %617 = mir.mov_imm 0 : i64
    %618 = mir.cmp lt, %534, %617
    mir.cond_br %618 [then: inlined_stdlib.__slab_proc_at_1_0(), else: inlined_stdlib.__slab_proc_at_2_0()]
  inlined_stdlib.__slab_proc_at_1_0:
    %619 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_35(%619)
  inlined_stdlib.__slab_proc_at_2_0:
    %620 = mir.global_addr @__sched_procs
    %621 = mir.load %620, 0 width: qword
    %622 = mir.mov_imm 0 : i64
    %623 = mir.cmp eq, %621, %622
    mir.cond_br %623 [then: inlined_stdlib.__slab_proc_at_3_0(), else: inlined_stdlib.__slab_proc_at_4_0()]
  inlined_stdlib.__slab_proc_at_3_0:
    %624 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_35(%624)
  inlined_stdlib.__slab_proc_at_4_0:
    %625 = mir.mov_imm 3 : i64
    %626 = mir.shl.i64 %534, %625
    %627 = mir.add.i64 %621, %626
    %628 = mir.load %627, 0 width: qword
    mir.br inline_cont_main_35(%628)
  inline_cont_main_35(%629: i64):
    %544 = mir.cmp ne, %629, %534
    mir.br inlined_stdlib.__slab_alloc_needzero_4_36(%544)
  inlined_stdlib.__slab_alloc_needzero_4_36(%545: i64):
    mir.cond_br %545 [then: inlined_stdlib.__slab_alloc_needzero_5_0(), else: inlined_stdlib.__slab_alloc_needzero_6_0()]
  inlined_stdlib.__slab_alloc_needzero_5_0:
    %546 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %546
    %547 = mir.call @stdlib.__slab_alloc_class(%616, %526)
    %548 = mir.global_addr @__slab_lock
    mir.os_lock_release %548
    mir.br inline_cont_main_21(%547)
  inlined_stdlib.__slab_alloc_needzero_6_0:
    %549 = mir.call @stdlib.__slab_alloc_class(%616, %526)
    mir.br inline_cont_main_21(%549)
  inline_cont_main_21(%550: i64):
    mir.br inlined_stdlib.__mm_alloc_needzero_5_23(%550)
  inlined_stdlib.__mm_alloc_needzero_4_0:
    %490 = mir.mov_imm 32 : i64
    %491 = mir.add.i64 %484, %490
    %528 = mir.mov_imm 0 : i64
    mir.br inlined_stdlib.__slab_alloc_needzero_0_1()
  inlined_stdlib.__slab_alloc_needzero_0_1:
    %551 = mir.mov_imm 0 : i64
    %552 = mir.mov_imm 32768 : i64
    %553 = mir.cmp gt, %491, %552
    mir.cond_br %553 [then: inlined_stdlib.__slab_alloc_needzero_1_1(), else: inlined_stdlib.__slab_class_index_for_0_1()]
  inlined_stdlib.__slab_alloc_needzero_1_1:
    %554 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %554
    %555 = mir.call @stdlib.__slab_os_direct_alloc(%491)
    %556 = mir.global_addr @__slab_lock
    mir.os_lock_release %556
    mir.br inline_cont_main_22(%555)
  inlined_stdlib.__slab_class_index_for_0_1:
    %630 = mir.mov_imm 0 : i64
    %631 = mir.mov_imm 0 : i64
    %632 = mir.mov_imm 18 : i64
    mir.br inlined_stdlib.__slab_class_index_for_1_79(%630, %631)
  inlined_stdlib.__slab_class_index_for_1_79(%633: i64, %634: i64):
    %635 = mir.cmp lt, %634, %632
    mir.cond_br %635 [then: inlined_stdlib.__slab_class_index_for_2_1(), else: inlined_stdlib.__slab_class_index_for_4_1()]
  inlined_stdlib.__slab_class_index_for_2_1:
    %636 = mir.call @stdlib.__slab_class_size(%633)
    %637 = mir.cmp ge, %636, %491
    mir.cond_br %637 [then: inline_cont_main_42(%633), else: inlined_stdlib.__slab_class_index_for_6_1()]
  inlined_stdlib.__slab_class_index_for_3_1:
    %638 = mir.mov_imm 1 : i64
    %639 = mir.add.i64 %634, %638
    mir.br inlined_stdlib.__slab_class_index_for_1_79(%643, %639)
  inlined_stdlib.__slab_class_index_for_4_1:
    %640 = mir.mov_imm 136 : i64
    mir.os_exit %640
    %641 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_42(%641)
  inlined_stdlib.__slab_class_index_for_6_1:
    %642 = mir.mov_imm 1 : i64
    %643 = mir.add.i64 %633, %642
    mir.br inlined_stdlib.__slab_class_index_for_3_1()
  inline_cont_main_42(%644: i64):
    %558 = mir.call @stdlib.__slab_current_p_id()
    %559 = mir.cmp lt, %558, %551
    mir.cond_br %559 [then: inlined_stdlib.__slab_proc_at_0_1(), else: inlined_stdlib.__slab_alloc_needzero_4_44(%551)]
  inlined_stdlib.__slab_proc_at_0_1:
    %645 = mir.mov_imm 0 : i64
    %646 = mir.cmp lt, %551, %645
    mir.cond_br %646 [then: inlined_stdlib.__slab_proc_at_1_1(), else: inlined_stdlib.__slab_proc_at_2_1()]
  inlined_stdlib.__slab_proc_at_1_1:
    %647 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_43(%647)
  inlined_stdlib.__slab_proc_at_2_1:
    %648 = mir.global_addr @__sched_procs
    %649 = mir.load %648, 0 width: qword
    %650 = mir.mov_imm 0 : i64
    %651 = mir.cmp eq, %649, %650
    mir.cond_br %651 [then: inlined_stdlib.__slab_proc_at_3_1(), else: inlined_stdlib.__slab_proc_at_4_1()]
  inlined_stdlib.__slab_proc_at_3_1:
    %652 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_43(%652)
  inlined_stdlib.__slab_proc_at_4_1:
    %653 = mir.mov_imm 3 : i64
    %654 = mir.shl.i64 %551, %653
    %655 = mir.add.i64 %649, %654
    %656 = mir.load %655, 0 width: qword
    mir.br inline_cont_main_43(%656)
  inline_cont_main_43(%657: i64):
    %561 = mir.cmp ne, %657, %551
    mir.br inlined_stdlib.__slab_alloc_needzero_4_44(%561)
  inlined_stdlib.__slab_alloc_needzero_4_44(%562: i64):
    mir.cond_br %562 [then: inlined_stdlib.__slab_alloc_needzero_5_1(), else: inlined_stdlib.__slab_alloc_needzero_6_1()]
  inlined_stdlib.__slab_alloc_needzero_5_1:
    %563 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %563
    %564 = mir.call @stdlib.__slab_alloc_class(%644, %528)
    %565 = mir.global_addr @__slab_lock
    mir.os_lock_release %565
    mir.br inline_cont_main_22(%564)
  inlined_stdlib.__slab_alloc_needzero_6_1:
    %566 = mir.call @stdlib.__slab_alloc_class(%644, %528)
    mir.br inline_cont_main_22(%566)
  inline_cont_main_22(%567: i64):
    mir.br inlined_stdlib.__mm_alloc_needzero_5_23(%567)
  inlined_stdlib.__mm_alloc_needzero_5_23(%493: i64):
    mir.store %480, %493, 0 width: qword
    %494 = mir.mov_imm 8 : i64
    %495 = mir.add.i64 %493, %494
    mir.store %76, %495, 0 width: qword
    %496 = mir.mov_imm 16 : i64
    %497 = mir.add.i64 %493, %496
    mir.store %484, %497, 0 width: qword
    %498 = mir.mov_imm 24 : i64
    %499 = mir.add.i64 %493, %498
    mir.store %480, %499, 0 width: qword
    %500 = mir.mov_imm 32 : i64
    %501 = mir.add.i64 %493, %500
    mir.br inline_cont_main_2(%501)
  inline_cont_main_2(%502: i64):
    %749 = mir.call @stdlib.__mm_incref(%501)
    mir.store %70, %502, 0 width: qword
    mir.store %0, %502, 8 width: qword
    %78 = mir.global_addr @__layout_Array_String
    %79 = mir.call @Array.push(%42, %502, %78)
    mir.br names_loop_0.step()
  names_loop_0.step:
    %82 = mir.add.i64 %367, %18
    mir.br names_loop_0.header(%82)
  names_loop_0.exit:
    %393 = mir.global_addr @__layout_Array_String
    %394 = mir.call @Array.count(%42, %393)
    %86 = mir.add.i64 %40, %394
    %729 = mir.call @__mm_decref_maybenull_helper(%42)
    %87 = mir.global_addr @__layout_Array_Integer
    %88 = mir.call @Array.create(%87)
    %91 = mir.global_addr @__layout_Array_Integer
    %92 = mir.call @Array.push(%88, %18, %91)
    %94 = mir.mov_imm 2 : i64
    %95 = mir.global_addr @__layout_Array_Integer
    %96 = mir.call @Array.push(%88, %94, %95)
    %97 = mir.global_addr @__layout_Array_Integer
    %98 = mir.call @Array.create(%97)
    %101 = mir.global_addr @__layout_Array_Integer
    %102 = mir.call @Array.push(%98, %30, %101)
    %104 = mir.mov_imm 4 : i64
    %105 = mir.global_addr @__layout_Array_Integer
    %106 = mir.call @Array.push(%98, %104, %105)
    %107 = mir.global_addr @__layout_Array_IntArray
    %108 = mir.call @Array.create(%107)
    %111 = mir.global_addr @__layout_Array_IntArray
    %112 = mir.call @Array.push(%108, %88, %111)
    %115 = mir.global_addr @__layout_Array_IntArray
    %116 = mir.call @Array.push(%108, %98, %115)
    %119 = mir.call @matrix_total(%108)
    %730 = mir.call @__mm_decref_maybenull_helper(%108)
    %120 = mir.add.i64 %86, %119
    %395 = mir.mov_imm 16 : i64
    %396 = mir.mov_imm 0 : i64
    %397 = mir.mov_imm 1 : i64
    %398 = mir.call @stdlib.__mm_alloc_needzero(%395, %396, %397)
    mir.store %0, %398, 0 width: qword
    mir.store %0, %398, 8 width: qword
    %399 = mir.load %398, 0 width: qword
    %400 = mir.load %398, 8 width: qword
    %401 = mir.add.i64 %399, %400
    %127 = mir.add.i64 %120, %401
    %402 = mir.load %398, 0 width: qword
    %403 = mir.load %398, 8 width: qword
    %404 = mir.add.i64 %402, %403
    %131 = mir.add.i64 %127, %404
    %731 = mir.call @mm_drop(%398)
    %132 = mir.mov_imm 10 : i64
    %133 = mir.mov_imm 20 : i64
    %405 = mir.mov_imm 16 : i64
    %406 = mir.mov_imm 0 : i64
    %407 = mir.mov_imm 1 : i64
    %408 = mir.call @stdlib.__mm_alloc_needzero(%405, %406, %407)
    mir.store %132, %408, 0 width: qword
    mir.store %133, %408, 8 width: qword
    %137 = mir.load %408, 0 width: qword
    %138 = mir.add.i64 %131, %137
    %732 = mir.call @mm_drop(%408)
    %139 = mir.global_addr @__istr_2
    %141 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %142 = mir.call @mrt_alloc_with_dtor(%372, %141)
    mir.store %0, %142, 40 width: qword
    mir.store %139, %142, 0 width: qword
    mir.store %36, %142, 8 width: qword
    mir.store %370, %142, 16 width: qword
    mir.store %18, %142, 24 width: qword
    mir.store %0, %142, 32 width: qword
    %150 = mir.func_addr @__destruct_String
    %409 = mir.mov_imm 1 : i64
    %410 = mir.call @stdlib.__mm_alloc_needzero(%371, %150, %409)
    %714 = mir.call @stdlib.__mm_incref(%410)
    mir.store %142, %410, 0 width: qword
    mir.store %18, %410, 8 width: qword
    %152 = mir.mov_imm 30 : i64
    %153 = mir.call @Person.create(%410, %152)
    %155 = mir.global_addr @__istr_3
    %157 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %158 = mir.call @mrt_alloc_with_dtor(%372, %157)
    mir.store %0, %158, 40 width: qword
    mir.store %155, %158, 0 width: qword
    mir.store %30, %158, 8 width: qword
    mir.store %370, %158, 16 width: qword
    mir.store %18, %158, 24 width: qword
    mir.store %0, %158, 32 width: qword
    %166 = mir.func_addr @__destruct_String
    %411 = mir.mov_imm 1 : i64
    %412 = mir.call @stdlib.__mm_alloc_needzero(%371, %166, %411)
    %715 = mir.call @stdlib.__mm_incref(%412)
    mir.store %158, %412, 0 width: qword
    mir.store %18, %412, 8 width: qword
    %168 = mir.load %153, 0 width: qword
    %169 = mir.call @__mm_decref_maybenull_helper(%168)
    mir.store %412, %153, 0 width: qword
    %171 = mir.global_addr @__istr_4
    %173 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %174 = mir.call @mrt_alloc_with_dtor(%372, %173)
    mir.store %0, %174, 40 width: qword
    mir.store %171, %174, 0 width: qword
    mir.store %36, %174, 8 width: qword
    mir.store %370, %174, 16 width: qword
    mir.store %18, %174, 24 width: qword
    mir.store %0, %174, 32 width: qword
    %182 = mir.func_addr @__destruct_String
    %413 = mir.mov_imm 1 : i64
    %414 = mir.call @stdlib.__mm_alloc_needzero(%371, %182, %413)
    %716 = mir.call @stdlib.__mm_incref(%414)
    mir.store %174, %414, 0 width: qword
    mir.store %18, %414, 8 width: qword
    %184 = mir.load %153, 0 width: qword
    %185 = mir.call @__mm_decref_maybenull_helper(%184)
    mir.store %414, %153, 0 width: qword
    %188 = mir.load %153, 8 width: qword
    %189 = mir.add.i64 %138, %188
    %733 = mir.call @__mm_decref_maybenull_helper(%153)
    %190 = mir.global_addr @__istr_5
    %192 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %193 = mir.call @mrt_alloc_with_dtor(%372, %192)
    mir.store %0, %193, 40 width: qword
    mir.store %190, %193, 0 width: qword
    %195 = mir.mov_imm 4 : i64
    mir.store %195, %193, 8 width: qword
    mir.store %370, %193, 16 width: qword
    mir.store %18, %193, 24 width: qword
    mir.store %0, %193, 32 width: qword
    %201 = mir.func_addr @__destruct_String
    %415 = mir.mov_imm 1 : i64
    %416 = mir.call @stdlib.__mm_alloc_needzero(%371, %201, %415)
    %717 = mir.call @stdlib.__mm_incref(%416)
    mir.store %193, %416, 0 width: qword
    mir.store %18, %416, 8 width: qword
    %417 = mir.mov_imm 16 : i64
    %418 = mir.func_addr @__destruct_Shape
    %419 = mir.mov_imm 0 : i64
    %420 = mir.mov_imm 1 : i64
    %421 = mir.call @stdlib.__mm_alloc_needzero(%417, %418, %420)
    mir.store %419, %421, 0 width: qword
    mir.store %416, %421, 8 width: qword
    %204 = mir.global_addr @__istr_6
    %206 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %207 = mir.call @mrt_alloc_with_dtor(%372, %206)
    mir.store %0, %207, 40 width: qword
    mir.store %204, %207, 0 width: qword
    mir.store %30, %207, 8 width: qword
    mir.store %370, %207, 16 width: qword
    mir.store %18, %207, 24 width: qword
    mir.store %0, %207, 32 width: qword
    %215 = mir.func_addr @__destruct_String
    %422 = mir.mov_imm 1 : i64
    %423 = mir.call @stdlib.__mm_alloc_needzero(%371, %215, %422)
    %718 = mir.call @stdlib.__mm_incref(%423)
    mir.store %207, %423, 0 width: qword
    mir.store %18, %423, 8 width: qword
    %424 = mir.mov_imm 16 : i64
    %425 = mir.func_addr @__destruct_Shape
    %426 = mir.mov_imm 1 : i64
    %427 = mir.mov_imm 1 : i64
    %428 = mir.call @stdlib.__mm_alloc_needzero(%424, %425, %427)
    mir.store %426, %428, 0 width: qword
    mir.store %423, %428, 8 width: qword
    %429 = mir.mov_imm 16 : i64
    %430 = mir.func_addr @__destruct_Shape
    %431 = mir.mov_imm 2 : i64
    %432 = mir.mov_imm 1 : i64
    %433 = mir.call @stdlib.__mm_alloc_needzero(%429, %430, %432)
    mir.store %431, %433, 0 width: qword
    %434 = mir.mov_imm 0 : i64
    mir.store %434, %433, 8 width: qword
    %221 = mir.call @describe(%421)
    %734 = mir.call @mm_drop(%421)
    %222 = mir.add.i64 %189, %221
    %225 = mir.call @describe(%428)
    %735 = mir.call @mm_drop(%428)
    %226 = mir.add.i64 %222, %225
    %229 = mir.call @describe(%433)
    %736 = mir.call @mm_drop(%433)
    %230 = mir.add.i64 %226, %229
    %231 = mir.global_addr @__istr_7
    %233 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %234 = mir.call @mrt_alloc_with_dtor(%372, %233)
    mir.store %0, %234, 40 width: qword
    mir.store %231, %234, 0 width: qword
    %236 = mir.mov_imm 4 : i64
    mir.store %236, %234, 8 width: qword
    mir.store %370, %234, 16 width: qword
    mir.store %18, %234, 24 width: qword
    mir.store %0, %234, 32 width: qword
    %242 = mir.func_addr @__destruct_String
    %435 = mir.mov_imm 1 : i64
    %436 = mir.call @stdlib.__mm_alloc_needzero(%371, %242, %435)
    %719 = mir.call @stdlib.__mm_incref(%436)
    mir.store %234, %436, 0 width: qword
    mir.store %18, %436, 8 width: qword
    %741 = mir.load_slot slot_15
    %742 = mir.call @__mm_decref_maybenull_helper(%741)
    mir.store_slot slot_15, %436
    %244 = mir.func_addr @main$closure_0
    %245 = mir.stack_slot_addr slot_15
    %246 = mir.mov_imm 8 : i64
    %437 = mir.mov_imm 1 : i64
    %438 = mir.call @stdlib.__mm_alloc_needzero(%246, %0, %437)
    mir.store %245, %438, 0 width: qword
    %251 = mir.mov_imm 7 : i64
    %439 = mir.indirect_call %244(%251, %438)
    %253 = mir.add.i64 %230, %439
    %256 = mir.mov_imm 8 : i64
    %440 = mir.indirect_call %244(%256, %438)
    %737 = mir.call @mm_drop(%438)
    %258 = mir.add.i64 %253, %440
    %259 = mir.global_addr @__layout_Array_Point
    %260 = mir.call @Array.create(%259)
    %263 = mir.mov_imm 2 : i64
    %441 = mir.mov_imm 16 : i64
    %442 = mir.mov_imm 0 : i64
    %443 = mir.mov_imm 1 : i64
    %444 = mir.call @stdlib.__mm_alloc_needzero(%441, %442, %443)
    %720 = mir.call @stdlib.__mm_incref(%444)
    mir.store %18, %444, 0 width: qword
    mir.store %263, %444, 8 width: qword
    %265 = mir.global_addr @__layout_Array_Point
    %266 = mir.call @Array.push(%260, %444, %265)
    %269 = mir.mov_imm 4 : i64
    %445 = mir.mov_imm 16 : i64
    %446 = mir.mov_imm 0 : i64
    %447 = mir.mov_imm 1 : i64
    %448 = mir.call @stdlib.__mm_alloc_needzero(%445, %446, %447)
    %721 = mir.call @stdlib.__mm_incref(%448)
    mir.store %30, %448, 0 width: qword
    mir.store %269, %448, 8 width: qword
    %271 = mir.global_addr @__layout_Array_Point
    %272 = mir.call @Array.push(%260, %448, %271)
    %275 = mir.mov_imm 6 : i64
    %449 = mir.mov_imm 16 : i64
    %450 = mir.mov_imm 0 : i64
    %451 = mir.mov_imm 1 : i64
    %452 = mir.call @stdlib.__mm_alloc_needzero(%449, %450, %451)
    %722 = mir.call @stdlib.__mm_incref(%452)
    mir.store %36, %452, 0 width: qword
    mir.store %275, %452, 8 width: qword
    %277 = mir.global_addr @__layout_Array_Point
    %278 = mir.call @Array.push(%260, %452, %277)
    %281 = mir.call @points_x_sum(%260)
    %738 = mir.call @__mm_decref_maybenull_helper(%260)
    %282 = mir.add.i64 %258, %281
    %283 = mir.global_addr @__layout_Array_Point
    %284 = mir.call @Array.create(%283)
    %286 = mir.mov_imm 7 : i64
    %287 = mir.mov_imm 8 : i64
    %453 = mir.mov_imm 16 : i64
    %454 = mir.mov_imm 0 : i64
    %455 = mir.mov_imm 1 : i64
    %456 = mir.call @stdlib.__mm_alloc_needzero(%453, %454, %455)
    %723 = mir.call @stdlib.__mm_incref(%456)
    mir.store %286, %456, 0 width: qword
    mir.store %287, %456, 8 width: qword
    %289 = mir.global_addr @__layout_Array_Point
    %290 = mir.call @Array.push(%284, %456, %289)
    %292 = mir.mov_imm 9 : i64
    %293 = mir.mov_imm 10 : i64
    %457 = mir.mov_imm 16 : i64
    %458 = mir.mov_imm 0 : i64
    %459 = mir.mov_imm 1 : i64
    %460 = mir.call @stdlib.__mm_alloc_needzero(%457, %458, %459)
    %724 = mir.call @stdlib.__mm_incref(%460)
    mir.store %292, %460, 0 width: qword
    mir.store %293, %460, 8 width: qword
    %295 = mir.global_addr @__layout_Array_Point
    %296 = mir.call @Array.push(%284, %460, %295)
    %298 = mir.mov_imm 11 : i64
    %299 = mir.mov_imm 12 : i64
    %461 = mir.mov_imm 16 : i64
    %462 = mir.mov_imm 0 : i64
    %463 = mir.mov_imm 1 : i64
    %464 = mir.call @stdlib.__mm_alloc_needzero(%461, %462, %463)
    %725 = mir.call @stdlib.__mm_incref(%464)
    mir.store %298, %464, 0 width: qword
    mir.store %299, %464, 8 width: qword
    %301 = mir.global_addr @__layout_Array_Point
    %302 = mir.call @Array.push(%284, %464, %301)
    mir.br alias_loop_0.header(%282, %0)
  alias_loop_0.header(%366: i64, %368: i64):
    %307 = mir.cmp lt, %368, %30
    mir.cond_br %307 [then: inlined_Array.get_0_0(), else: alias_loop_0.exit()]
  inlined_Array.get_0_0:
    %465 = mir.load %284, 0 width: qword
    %466, %467 = mir.try_call @stdlib.__managed_mem_get(%465, %368)
    %468 = mir.mov_imm 0 : i64
    %469 = mir.cmp ne, %467, %468
    mir.cond_br %469 [then: inlined_Array.get_1_0(), else: inlined_Array.get_3_0()]
  inlined_Array.get_1_0:
    %470 = mir.mov_imm 0 : i64
    %471 = mir.mov_imm 1 : i64
    mir.br inline_cont_main_6(%470, %471)
  inlined_Array.get_3_0:
    %472 = mir.mov_imm 0 : i64
    mir.br __rc_edge_16_0()
  inline_cont_main_6(%473: i64, %474: i64):
    %314 = mir.cmp ne, %474, %0
    mir.cond_br %314 [then: try_0.otherwise(), else: try_0.merge(%473)]
  alias_loop_0.step:
    %318 = mir.add.i64 %368, %18
    mir.br alias_loop_0.header(%363, %318)
  alias_loop_0.exit:
    %747 = mir.call @__mm_decref_maybenull_helper(%284)
    %321 = mir.cmp lt, %366, %0
    mir.cond_br %321 [then: guard_0(), else: guard_0.after()]
  try_0.otherwise:
    %748 = mir.call @__mm_decref_maybenull_helper(%473)
    %323 = mir.global_addr @__istr_8
    %324 = mir.mov_imm 75 : i64
    %325 = mir.mov_imm 21 : i64
    %326 = mir.call @mrt_alloc(%325)
    %327 = mir.call @mrt_i64_to_string(%368, %326)
    %328 = mir.global_addr @__istr_9
    %329 = mir.mov_imm 20 : i64
    %330 = mir.global_addr @__istr_10
    %333 = mir.mov_imm 75 : i64
    %334 = mir.add.i64 %333, %327
    %335 = mir.add.i64 %334, %329
    %336 = mir.add.i64 %335, %18
    %338 = mir.add.i64 %336, %18
    %339 = mir.call @mrt_alloc(%338)
    mir.memcpy %339, %323, %324
    %340 = mir.add.i64 %339, %324
    mir.memcpy %340, %326, %327
    %341 = mir.add.i64 %340, %327
    mir.memcpy %341, %328, %329
    %342 = mir.add.i64 %341, %329
    mir.memcpy %342, %330, %18
    %344 = mir.call @stdlib.__mm_decref(%326)
    %346 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %347 = mir.call @mrt_alloc_with_dtor(%372, %346)
    mir.store %0, %347, 40 width: qword
    mir.store %339, %347, 0 width: qword
    mir.store %336, %347, 8 width: qword
    mir.store %336, %347, 16 width: qword
    mir.store %18, %347, 24 width: qword
    %350 = mir.mov_imm -1 : i64
    mir.store %350, %347, 32 width: qword
    %353 = mir.func_addr @__destruct_String
    %475 = mir.mov_imm 1 : i64
    mir.br inlined_stdlib.__mm_alloc_needzero_0_1()
  inlined_stdlib.__mm_alloc_needzero_0_1:
    %503 = mir.mov_imm 0 : i64
    %504 = mir.mov_imm 1 : i64
    %505 = mir.cmp lt, %371, %504
    mir.cond_br %505 [then: inlined_stdlib.__mm_alloc_needzero_1_1(), else: inlined_stdlib.__mm_alloc_needzero_2_27(%371)]
  inlined_stdlib.__mm_alloc_needzero_1_1:
    %506 = mir.mov_imm 1 : i64
    mir.br inlined_stdlib.__mm_alloc_needzero_2_27(%506)
  inlined_stdlib.__mm_alloc_needzero_2_27(%507: i64):
    %508 = mir.global_addr @__mm_alloc_count
    mir.atomic_inc %508
    %509 = mir.cmp ne, %475, %503
    mir.cond_br %509 [then: inlined_stdlib.__mm_alloc_needzero_3_1(), else: inlined_stdlib.__mm_alloc_needzero_4_1()]
  inlined_stdlib.__mm_alloc_needzero_3_1:
    %510 = mir.mov_imm 32 : i64
    %511 = mir.add.i64 %507, %510
    %530 = mir.mov_imm 1 : i64
    mir.br inlined_stdlib.__slab_alloc_needzero_0_2()
  inlined_stdlib.__slab_alloc_needzero_0_2:
    %568 = mir.mov_imm 0 : i64
    %569 = mir.mov_imm 32768 : i64
    %570 = mir.cmp gt, %511, %569
    mir.cond_br %570 [then: inlined_stdlib.__slab_alloc_needzero_1_2(), else: inlined_stdlib.__slab_class_index_for_0_2()]
  inlined_stdlib.__slab_alloc_needzero_1_2:
    %571 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %571
    %572 = mir.call @stdlib.__slab_os_direct_alloc(%511)
    %573 = mir.global_addr @__slab_lock
    mir.os_lock_release %573
    mir.br inline_cont_main_28(%572)
  inlined_stdlib.__slab_class_index_for_0_2:
    %658 = mir.mov_imm 0 : i64
    %659 = mir.mov_imm 0 : i64
    %660 = mir.mov_imm 18 : i64
    mir.br inlined_stdlib.__slab_class_index_for_1_93(%658, %659)
  inlined_stdlib.__slab_class_index_for_1_93(%661: i64, %662: i64):
    %663 = mir.cmp lt, %662, %660
    mir.cond_br %663 [then: inlined_stdlib.__slab_class_index_for_2_2(), else: inlined_stdlib.__slab_class_index_for_4_2()]
  inlined_stdlib.__slab_class_index_for_2_2:
    %664 = mir.call @stdlib.__slab_class_size(%661)
    %665 = mir.cmp ge, %664, %511
    mir.cond_br %665 [then: inline_cont_main_50(%661), else: inlined_stdlib.__slab_class_index_for_6_2()]
  inlined_stdlib.__slab_class_index_for_3_2:
    %666 = mir.mov_imm 1 : i64
    %667 = mir.add.i64 %662, %666
    mir.br inlined_stdlib.__slab_class_index_for_1_93(%671, %667)
  inlined_stdlib.__slab_class_index_for_4_2:
    %668 = mir.mov_imm 136 : i64
    mir.os_exit %668
    %669 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_50(%669)
  inlined_stdlib.__slab_class_index_for_6_2:
    %670 = mir.mov_imm 1 : i64
    %671 = mir.add.i64 %661, %670
    mir.br inlined_stdlib.__slab_class_index_for_3_2()
  inline_cont_main_50(%672: i64):
    %575 = mir.call @stdlib.__slab_current_p_id()
    %576 = mir.cmp lt, %575, %568
    mir.cond_br %576 [then: inlined_stdlib.__slab_proc_at_0_2(), else: inlined_stdlib.__slab_alloc_needzero_4_52(%568)]
  inlined_stdlib.__slab_proc_at_0_2:
    %673 = mir.mov_imm 0 : i64
    %674 = mir.cmp lt, %568, %673
    mir.cond_br %674 [then: inlined_stdlib.__slab_proc_at_1_2(), else: inlined_stdlib.__slab_proc_at_2_2()]
  inlined_stdlib.__slab_proc_at_1_2:
    %675 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_51(%675)
  inlined_stdlib.__slab_proc_at_2_2:
    %676 = mir.global_addr @__sched_procs
    %677 = mir.load %676, 0 width: qword
    %678 = mir.mov_imm 0 : i64
    %679 = mir.cmp eq, %677, %678
    mir.cond_br %679 [then: inlined_stdlib.__slab_proc_at_3_2(), else: inlined_stdlib.__slab_proc_at_4_2()]
  inlined_stdlib.__slab_proc_at_3_2:
    %680 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_51(%680)
  inlined_stdlib.__slab_proc_at_4_2:
    %681 = mir.mov_imm 3 : i64
    %682 = mir.shl.i64 %568, %681
    %683 = mir.add.i64 %677, %682
    %684 = mir.load %683, 0 width: qword
    mir.br inline_cont_main_51(%684)
  inline_cont_main_51(%685: i64):
    %578 = mir.cmp ne, %685, %568
    mir.br inlined_stdlib.__slab_alloc_needzero_4_52(%578)
  inlined_stdlib.__slab_alloc_needzero_4_52(%579: i64):
    mir.cond_br %579 [then: inlined_stdlib.__slab_alloc_needzero_5_2(), else: inlined_stdlib.__slab_alloc_needzero_6_2()]
  inlined_stdlib.__slab_alloc_needzero_5_2:
    %580 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %580
    %581 = mir.call @stdlib.__slab_alloc_class(%672, %530)
    %582 = mir.global_addr @__slab_lock
    mir.os_lock_release %582
    mir.br inline_cont_main_28(%581)
  inlined_stdlib.__slab_alloc_needzero_6_2:
    %583 = mir.call @stdlib.__slab_alloc_class(%672, %530)
    mir.br inline_cont_main_28(%583)
  inline_cont_main_28(%584: i64):
    mir.br inlined_stdlib.__mm_alloc_needzero_5_30(%584)
  inlined_stdlib.__mm_alloc_needzero_4_1:
    %513 = mir.mov_imm 32 : i64
    %514 = mir.add.i64 %507, %513
    %532 = mir.mov_imm 0 : i64
    mir.br inlined_stdlib.__slab_alloc_needzero_0_3()
  inlined_stdlib.__slab_alloc_needzero_0_3:
    %585 = mir.mov_imm 0 : i64
    %586 = mir.mov_imm 32768 : i64
    %587 = mir.cmp gt, %514, %586
    mir.cond_br %587 [then: inlined_stdlib.__slab_alloc_needzero_1_3(), else: inlined_stdlib.__slab_class_index_for_0_3()]
  inlined_stdlib.__slab_alloc_needzero_1_3:
    %588 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %588
    %589 = mir.call @stdlib.__slab_os_direct_alloc(%514)
    %590 = mir.global_addr @__slab_lock
    mir.os_lock_release %590
    mir.br inline_cont_main_29(%589)
  inlined_stdlib.__slab_class_index_for_0_3:
    %686 = mir.mov_imm 0 : i64
    %687 = mir.mov_imm 0 : i64
    %688 = mir.mov_imm 18 : i64
    mir.br inlined_stdlib.__slab_class_index_for_1_107(%686, %687)
  inlined_stdlib.__slab_class_index_for_1_107(%689: i64, %690: i64):
    %691 = mir.cmp lt, %690, %688
    mir.cond_br %691 [then: inlined_stdlib.__slab_class_index_for_2_3(), else: inlined_stdlib.__slab_class_index_for_4_3()]
  inlined_stdlib.__slab_class_index_for_2_3:
    %692 = mir.call @stdlib.__slab_class_size(%689)
    %693 = mir.cmp ge, %692, %514
    mir.cond_br %693 [then: inline_cont_main_58(%689), else: inlined_stdlib.__slab_class_index_for_6_3()]
  inlined_stdlib.__slab_class_index_for_3_3:
    %694 = mir.mov_imm 1 : i64
    %695 = mir.add.i64 %690, %694
    mir.br inlined_stdlib.__slab_class_index_for_1_107(%699, %695)
  inlined_stdlib.__slab_class_index_for_4_3:
    %696 = mir.mov_imm 136 : i64
    mir.os_exit %696
    %697 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_58(%697)
  inlined_stdlib.__slab_class_index_for_6_3:
    %698 = mir.mov_imm 1 : i64
    %699 = mir.add.i64 %689, %698
    mir.br inlined_stdlib.__slab_class_index_for_3_3()
  inline_cont_main_58(%700: i64):
    %592 = mir.call @stdlib.__slab_current_p_id()
    %593 = mir.cmp lt, %592, %585
    mir.cond_br %593 [then: inlined_stdlib.__slab_proc_at_0_3(), else: inlined_stdlib.__slab_alloc_needzero_4_60(%585)]
  inlined_stdlib.__slab_proc_at_0_3:
    %701 = mir.mov_imm 0 : i64
    %702 = mir.cmp lt, %585, %701
    mir.cond_br %702 [then: inlined_stdlib.__slab_proc_at_1_3(), else: inlined_stdlib.__slab_proc_at_2_3()]
  inlined_stdlib.__slab_proc_at_1_3:
    %703 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_59(%703)
  inlined_stdlib.__slab_proc_at_2_3:
    %704 = mir.global_addr @__sched_procs
    %705 = mir.load %704, 0 width: qword
    %706 = mir.mov_imm 0 : i64
    %707 = mir.cmp eq, %705, %706
    mir.cond_br %707 [then: inlined_stdlib.__slab_proc_at_3_3(), else: inlined_stdlib.__slab_proc_at_4_3()]
  inlined_stdlib.__slab_proc_at_3_3:
    %708 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_59(%708)
  inlined_stdlib.__slab_proc_at_4_3:
    %709 = mir.mov_imm 3 : i64
    %710 = mir.shl.i64 %585, %709
    %711 = mir.add.i64 %705, %710
    %712 = mir.load %711, 0 width: qword
    mir.br inline_cont_main_59(%712)
  inline_cont_main_59(%713: i64):
    %595 = mir.cmp ne, %713, %585
    mir.br inlined_stdlib.__slab_alloc_needzero_4_60(%595)
  inlined_stdlib.__slab_alloc_needzero_4_60(%596: i64):
    mir.cond_br %596 [then: inlined_stdlib.__slab_alloc_needzero_5_3(), else: inlined_stdlib.__slab_alloc_needzero_6_3()]
  inlined_stdlib.__slab_alloc_needzero_5_3:
    %597 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %597
    %598 = mir.call @stdlib.__slab_alloc_class(%700, %532)
    %599 = mir.global_addr @__slab_lock
    mir.os_lock_release %599
    mir.br inline_cont_main_29(%598)
  inlined_stdlib.__slab_alloc_needzero_6_3:
    %600 = mir.call @stdlib.__slab_alloc_class(%700, %532)
    mir.br inline_cont_main_29(%600)
  inline_cont_main_29(%601: i64):
    mir.br inlined_stdlib.__mm_alloc_needzero_5_30(%601)
  inlined_stdlib.__mm_alloc_needzero_5_30(%516: i64):
    mir.store %503, %516, 0 width: qword
    %517 = mir.mov_imm 8 : i64
    %518 = mir.add.i64 %516, %517
    mir.store %353, %518, 0 width: qword
    %519 = mir.mov_imm 16 : i64
    %520 = mir.add.i64 %516, %519
    mir.store %507, %520, 0 width: qword
    %521 = mir.mov_imm 24 : i64
    %522 = mir.add.i64 %516, %521
    mir.store %503, %522, 0 width: qword
    %523 = mir.mov_imm 32 : i64
    %524 = mir.add.i64 %516, %523
    mir.br inline_cont_main_9(%524)
  inline_cont_main_9(%525: i64):
    %751 = mir.call @stdlib.__mm_incref(%524)
    mir.store %347, %525, 0 width: qword
    mir.store %0, %525, 8 width: qword
    %355 = mir.load %525, 0 width: qword
    %356 = mir.load %355, 0 width: qword
    %357 = mir.call @mrt_panic(%356)
    %739 = mir.call @mm_drop(%525)
    mir.br try_0.merge(%0)
  try_0.merge(%369: i64):
    %477 = mir.load %369, 0 width: qword
    %478 = mir.load %369, 8 width: qword
    %479 = mir.add.i64 %477, %478
    %363 = mir.add.i64 %366, %479
    %740 = mir.call @__mm_decref_maybenull_helper(%369)
    mir.br alias_loop_0.step()
  guard_0:
    %743 = mir.load_slot slot_15
    %744 = mir.call @__mm_decref_maybenull_helper(%743)
    mir.ret %18
  guard_0.after:
    %745 = mir.load_slot slot_15
    %746 = mir.call @__mm_decref_maybenull_helper(%745)
    mir.ret %0
  __rc_edge_16_0:
    %750 = mir.call @stdlib.__mm_incref(%466)
    mir.br inline_cont_main_6(%466, %472)
  }
}

```

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
