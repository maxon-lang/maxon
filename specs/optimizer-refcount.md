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
    x64.lea rdx, [rip+__destruct_String]
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
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
    x64.mov [rbp+-976], r8
    x64.add r13, r12
    x64.add rbx, 0
    x64.add r15, r14
    x64.add rbx, r13
    x64.mov [rbp+-968], rbx
    x64.mov r8, [rbp+-968]
    x64.add r8, r15
    x64.mov [rbp+-968], r8
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
    x64.mov [rbp+-984], r8
    x64.mov r8, [rbp+-984]
    x64.mov r9, 0
    x64.mov [r8+40], r9 (8b)
    x64.mov [r8+0], r15 (8b)
    x64.mov [r8+8], r14 (8b)
    x64.mov [r8+16], r14 (8b)
    x64.mov r9, 1
    x64.mov [r8+24], r9 (8b)
    x64.mov r9, -1
    x64.mov [r8+32], r9 (8b)
    x64.lea r12, [rip+__destruct_String]
  inlined_stdlib.__mm_alloc_0_0:
    x64.xor r8d, r8d
    x64.mov r8, 16
    x64.cmp r8, 1
    x64.mov r8, 16
    x64.jge inlined_stdlib.__mm_alloc_2_0
  inlined_stdlib.__mm_alloc_1_0:
    x64.mov r8d, 1
  inlined_stdlib.__mm_alloc_2_0:
    x64.mov [rbp+-992], r8
    x64.lea r8, [rip+__mm_alloc_count]
    x64.lock inc qword ptr [r8]
    x64.mov r13, [rbp+-992]
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
    x64.mov [r8+0], r12 (8b)
    x64.mov r8, r13
    x64.add r8, 16
    x64.mov r9, [rbp+-992]
    x64.mov [r8+0], r9 (8b)
    x64.mov r8, r13
    x64.add r8, 24
    x64.mov r9, 0
    x64.mov [r8+0], r9 (8b)
    x64.mov rcx, r13
    x64.add rcx, 32
    x64.mov r12, rcx
  inline_cont_main_3:
    x64.call stdlib.__mm_incref
    x64.mov r8, [rbp+-984]
    x64.mov [r12+0], r8 (8b)
    x64.mov r8, 0
    x64.mov [r12+8], r8 (8b)
    x64.lea rax, [rip+__layout_Array_String]
    x64.mov rdx, r12
    x64.mov rcx, [rbp+-976]
    x64.call Array.push
  names_loop_0.step:
    x64.mov rcx, rbx
    x64.add rcx, 1
    x64.mov rbx, rcx
    x64.jmp names_loop_0.header
  names_loop_0.exit:
    x64.lea rdx, [rip+__layout_Array_String]
    x64.mov rcx, [rbp+-976]
    x64.call Array.count
    x64.mov [rbp+-984], r8
    x64.mov rcx, [rbp+-976]
    x64.call __mm_decref_maybenull_helper
    x64.lea rcx, [rip+__layout_Array_Integer]
    x64.lea rbx, [rip+__layout_Array_Integer]
    x64.lea r12, [rip+__layout_Array_Integer]
    x64.mov r8d, 2
    x64.lea r13, [rip+__layout_Array_Integer]
    x64.lea r14, [rip+__layout_Array_Integer]
    x64.lea r8, [rip+__layout_Array_Integer]
    x64.mov [rbp+-976], r8
    x64.mov r8d, 4
    x64.lea r8, [rip+__layout_Array_IntArray]
    x64.mov [rbp+-992], r8
    x64.lea r8, [rip+__layout_Array_IntArray]
    x64.mov [rbp+-1008], r8
    x64.lea r8, [rip+__layout_Array_IntArray]
    x64.mov [rbp+-1016], r8
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
    x64.mov rax, [rbp+-976]
    x64.call Array.push
    x64.mov rcx, [rbp+-992]
    x64.call Array.create
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.mov rdx, r15
    x64.mov rax, [rbp+-1008]
    x64.call Array.push
    x64.mov rcx, r12
    x64.mov rdx, rbx
    x64.mov rax, [rbp+-1016]
    x64.call Array.push
    x64.mov rcx, r12
    x64.call matrix_total
    x64.mov [rbp+-992], r8
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
    x64.mov [rbp+-1016], r9
    x64.mov r9, [r8+8] (8b)
    x64.mov [rbp+-1024], r9
    x64.mov r9, [r8+0] (8b)
    x64.mov [rbp+-1032], r9
    x64.mov rcx, r8
    x64.call mm_drop
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.mov ebx, 10
    x64.mov r12d, 20
    x64.call stdlib.__mm_alloc
    x64.mov [r8+0], rbx (8b)
    x64.mov [r8+8], r12 (8b)
    x64.mov r9, [r8+0] (8b)
    x64.mov [rbp+-1040], r9
    x64.mov rcx, r8
    x64.call mm_drop
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.lea rbx, [rip+__istr_2]
    x64.lea r12, [rip+__destruct_String]
    x64.lea r8, [rip+__destruct_Person]
    x64.mov [rbp+-976], r8
    x64.mov r8d, 16
    x64.xor r13d, r13d
    x64.mov r14d, 30
    x64.lea r8, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov [rbp+-1048], r8
    x64.lea r8, [rip+__istr_3]
    x64.mov [rbp+-1056], r8
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-1064], r8
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov r15, r8
    x64.mov r8, 0
    x64.mov [r15+40], r8 (8b)
    x64.mov [r15+0], rbx (8b)
    x64.mov r8, 5
    x64.mov [r15+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [r15+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r15+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r15+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, r12
    x64.call stdlib.__mm_alloc
    x64.mov rbx, r8
    x64.mov rcx, rbx
    x64.call stdlib.__mm_incref
    x64.mov [rbx+0], r15 (8b)
    x64.mov r8, 1
    x64.mov [rbx+8], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-976]
    x64.call stdlib.__mm_alloc
    x64.mov [rbp+-976], r8
    x64.mov r8, [rbp+-976]
    x64.mov [r8+0], r13 (8b)
    x64.mov [r8+0], rbx (8b)
    x64.mov [r8+8], r14 (8b)
    x64.mov rcx, 48
    x64.mov rdx, [rbp+-1048]
    x64.call mrt_alloc_with_dtor
    x64.mov rbx, r8
    x64.mov r8, 0
    x64.mov [rbx+40], r8 (8b)
    x64.mov r8, [rbp+-1056]
    x64.mov [rbx+0], r8 (8b)
    x64.mov r8, 3
    x64.mov [rbx+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [rbx+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [rbx+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [rbx+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1064]
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
    x64.mov [r12+0], rbx (8b)
    x64.mov r8, 1
    x64.mov [r12+8], r8 (8b)
    x64.mov r8, [rbp+-976]
    x64.mov rcx, [r8+0] (8b)
    x64.lea rbx, [rip+stdlib.__destruct___ManagedMemory]
    x64.lea r13, [rip+__istr_4]
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-1048], r8
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, [rbp+-976]
    x64.mov [r8+0], r12 (8b)
    x64.mov rcx, 48
    x64.mov rdx, rbx
    x64.call mrt_alloc_with_dtor
    x64.mov rbx, r8
    x64.mov r8, 0
    x64.mov [rbx+40], r8 (8b)
    x64.mov [rbx+0], r13 (8b)
    x64.mov r8, 5
    x64.mov [rbx+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [rbx+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [rbx+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [rbx+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1048]
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov rcx, r12
    x64.call stdlib.__mm_incref
    x64.mov [r12+0], rbx (8b)
    x64.mov r8, 1
    x64.mov [r12+8], r8 (8b)
    x64.mov r8, [rbp+-976]
    x64.mov rcx, [r8+0] (8b)
    x64.call __mm_decref_maybenull_helper
    x64.mov r8, [rbp+-976]
    x64.mov [r8+0], r12 (8b)
    x64.mov r9, [r8+8] (8b)
    x64.mov [rbp+-1048], r9
    x64.mov rcx, [rbp+-976]
    x64.call mm_drop
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.lea rbx, [rip+__istr_5]
    x64.mov r12d, 4
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-976], r8
    x64.lea r8, [rip+__destruct_Shape]
    x64.mov [rbp+-1056], r8
    x64.mov r8d, 16
    x64.xor r8d, r8d
    x64.lea r8, [rip+stdlib.__destruct___ManagedMemory]
    x64.mov [rbp+-1064], r8
    x64.lea r8, [rip+__istr_6]
    x64.mov [rbp+-1072], r8
    x64.lea r8, [rip+__destruct_String]
    x64.mov [rbp+-1080], r8
    x64.lea r8, [rip+__destruct_Shape]
    x64.mov [rbp+-1088], r8
    x64.mov r13d, 16
    x64.mov r8d, 1
    x64.lea r14, [rip+__destruct_Shape]
    x64.mov r8d, 16
    x64.mov r8d, 2
    x64.xor r8d, r8d
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov r15, r8
    x64.mov r8, 0
    x64.mov [r15+40], r8 (8b)
    x64.mov [r15+0], rbx (8b)
    x64.mov [r15+8], r12 (8b)
    x64.mov r8, -2
    x64.mov [r15+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r15+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r15+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-976]
    x64.call stdlib.__mm_alloc
    x64.mov rbx, r8
    x64.mov rcx, rbx
    x64.call stdlib.__mm_incref
    x64.mov [rbx+0], r15 (8b)
    x64.mov r8, 1
    x64.mov [rbx+8], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1056]
    x64.call stdlib.__mm_alloc
    x64.mov r12, r8
    x64.mov r8, 0
    x64.mov [r12+0], r8 (8b)
    x64.mov [r12+8], rbx (8b)
    x64.mov rcx, 48
    x64.mov rdx, [rbp+-1064]
    x64.call mrt_alloc_with_dtor
    x64.mov rbx, r8
    x64.mov r8, 0
    x64.mov [rbx+40], r8 (8b)
    x64.mov r8, [rbp+-1072]
    x64.mov [rbx+0], r8 (8b)
    x64.mov r8, 3
    x64.mov [rbx+8], r8 (8b)
    x64.mov r8, -2
    x64.mov [rbx+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [rbx+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [rbx+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, [rbp+-1080]
    x64.call stdlib.__mm_alloc
    x64.mov [rbp+-976], r8
    x64.mov rcx, [rbp+-976]
    x64.call stdlib.__mm_incref
    x64.mov r8, [rbp+-976]
    x64.mov [r8+0], rbx (8b)
    x64.mov r9, 1
    x64.mov [r8+8], r9 (8b)
    x64.mov rcx, r13
    x64.mov rdx, [rbp+-1088]
    x64.call stdlib.__mm_alloc
    x64.mov rbx, r8
    x64.mov r8, 1
    x64.mov [rbx+0], r8 (8b)
    x64.mov r8, [rbp+-976]
    x64.mov [rbx+8], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, r14
    x64.call stdlib.__mm_alloc
    x64.mov r13, r8
    x64.mov r8, 2
    x64.mov [r13+0], r8 (8b)
    x64.mov r8, 0
    x64.mov [r13+8], r8 (8b)
    x64.mov rcx, r12
    x64.call describe
    x64.mov [rbp+-1056], r8
    x64.mov rcx, r12
    x64.call mm_drop
    x64.mov rcx, rbx
    x64.call describe
    x64.mov [rbp+-1064], r8
    x64.mov rcx, rbx
    x64.call mm_drop
    x64.mov rcx, r13
    x64.call describe
    x64.mov [rbp+-1072], r8
    x64.mov rcx, r13
    x64.call mm_drop
    x64.lea rdx, [rip+stdlib.__destruct___ManagedMemory]
    x64.lea rbx, [rip+__istr_7]
    x64.mov r12d, 4
    x64.lea r13, [rip+__destruct_String]
    x64.mov rcx, 48
    x64.call mrt_alloc_with_dtor
    x64.mov r14, r8
    x64.mov r8, 0
    x64.mov [r14+40], r8 (8b)
    x64.mov [r14+0], rbx (8b)
    x64.mov [r14+8], r12 (8b)
    x64.mov r8, -2
    x64.mov [r14+16], r8 (8b)
    x64.mov r8, 1
    x64.mov [r14+24], r8 (8b)
    x64.mov r8, 0
    x64.mov [r14+32], r8 (8b)
    x64.mov rcx, 16
    x64.mov rdx, r13
    x64.call stdlib.__mm_alloc
    x64.mov rbx, r8
    x64.mov rcx, rbx
    x64.call stdlib.__mm_incref
    x64.mov [rbx+0], r14 (8b)
    x64.mov r8, 1
    x64.mov [rbx+8], r8 (8b)
    x64.mov rcx, [rbp-128]
    x64.mov r12d, 8
    x64.lea rax, [rbp-128]
    x64.mov r13, [rbp-224]
    x64.mov r14d, 7
    x64.lea r15, [rip+main$closure_0]
    x64.mov r8d, 8
    x64.call __mm_decref_maybenull_helper
    x64.mov [rbp-128], rbx
    x64.mov rcx, r12
    x64.mov rdx, 0
    x64.call stdlib.__mm_alloc
    x64.mov rbx, r8
    x64.mov [rbx+0], r13 (8b)
    x64.mov rcx, r14
    x64.mov rdx, rbx
    x64.call r15
    x64.mov r12, r8
    x64.mov rcx, 8
    x64.mov rdx, rbx
    x64.call r15
    x64.mov r13, r8
    x64.mov rcx, rbx
    x64.call mm_drop
    x64.lea rcx, [rip+__layout_Array_Point]
    x64.xor ebx, ebx
    x64.mov r14d, 16
    x64.mov r8d, 2
    x64.lea r8, [rip+__layout_Array_Point]
    x64.mov [rbp+-976], r8
    x64.xor r8d, r8d
    x64.mov r15d, 16
    x64.mov r8d, 4
    x64.lea r8, [rip+__layout_Array_Point]
    x64.mov [rbp+-1080], r8
    x64.xor r8d, r8d
    x64.mov r8d, 16
    x64.mov r8d, 6
    x64.lea r8, [rip+__layout_Array_Point]
    x64.mov [rbp+-1088], r8
    x64.call Array.create
    x64.mov [rbp+-1096], r8
    x64.mov rcx, r14
    x64.mov rdx, rbx
    x64.call stdlib.__mm_alloc
    x64.mov rbx, r8
    x64.mov rcx, rbx
    x64.call stdlib.__mm_incref
    x64.mov r8, 1
    x64.mov [rbx+0], r8 (8b)
    x64.mov r8, 2
    x64.mov [rbx+8], r8 (8b)
    x64.mov rdx, rbx
    x64.mov rcx, [rbp+-1096]
    x64.mov rax, [rbp+-976]
    x64.call Array.push
    x64.mov rcx, r15
    x64.mov rdx, 0
    x64.call stdlib.__mm_alloc
    x64.mov rbx, r8
    x64.mov rcx, rbx
    x64.call stdlib.__mm_incref
    x64.mov r8, 3
    x64.mov [rbx+0], r8 (8b)
    x64.mov r8, 4
    x64.mov [rbx+8], r8 (8b)
    x64.mov rdx, rbx
    x64.mov rcx, [rbp+-1096]
    x64.mov rax, [rbp+-1080]
    x64.call Array.push
    x64.mov rcx, 16
    x64.mov rdx, 0
    x64.call stdlib.__mm_alloc
    x64.mov rbx, r8
    x64.mov rcx, rbx
    x64.call stdlib.__mm_incref
    x64.mov r8, 5
    x64.mov [rbx+0], r8 (8b)
    x64.mov r8, 6
    x64.mov [rbx+8], r8 (8b)
    x64.mov rdx, rbx
    x64.mov rcx, [rbp+-1096]
    x64.mov rax, [rbp+-1088]
    x64.call Array.push
    x64.mov rcx, [rbp+-1096]
    x64.call points_x_sum
    x64.mov rbx, r8
    x64.mov rcx, [rbp+-1096]
    x64.call __mm_decref_maybenull_helper
    x64.lea rcx, [rip+__layout_Array_Point]
    x64.call Array.create
    x64.mov [rbp+-976], r8
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8d, 7
    x64.mov [r14+0], r8 (8b)
    x64.mov r8d, 8
    x64.mov [r14+8], r8 (8b)
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov rdx, r14
    x64.mov rcx, [rbp+-976]
    x64.call Array.push
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8d, 9
    x64.mov [r14+0], r8 (8b)
    x64.mov r8d, 10
    x64.mov [r14+8], r8 (8b)
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov rdx, r14
    x64.mov rcx, [rbp+-976]
    x64.call Array.push
    x64.xor edx, edx
    x64.mov ecx, 16
    x64.call stdlib.__mm_alloc
    x64.mov r14, r8
    x64.mov rcx, r14
    x64.call stdlib.__mm_incref
    x64.mov r8d, 11
    x64.mov r9, [rbp+-968]
    x64.add r9, [rbp+-984]
    x64.mov [r14+0], r8 (8b)
    x64.mov r8, [rbp+-1032]
    x64.add r8, [rbp+-1024]
    x64.add r9, [rbp+-992]
    x64.mov rsi, [rbp+-1016]
    x64.add rsi, [rbp+-1008]
    x64.add r9, r8
    x64.mov r8d, 12
    x64.add r9, rsi
    x64.mov [r14+8], r8 (8b)
    x64.add r9, [rbp+-1040]
    x64.add r9, [rbp+-1048]
    x64.lea rax, [rip+__layout_Array_Point]
    x64.mov r15, r9
    x64.add r15, [rbp+-1056]
    x64.mov rdx, r14
    x64.mov rcx, [rbp+-976]
    x64.call Array.push
    x64.add r15, [rbp+-1064]
    x64.add r15, [rbp+-1072]
    x64.add r15, r12
    x64.add r15, r13
    x64.add r15, rbx
    x64.mov rdx, 0
    x64.mov rbx, rdx
  alias_loop_0.header:
    x64.cmp rbx, 3
    x64.jge alias_loop_0.exit
  inlined_Array.get_0_0:
    x64.mov r8, [rbp+-976]
    x64.mov rcx, [r8+0] (8b)
    x64.mov rdx, rbx
    x64.call stdlib.__managed_mem_get
    x64.mov [rbp+-1000], r8
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
    x64.mov rdx, rbx
    x64.add rdx, 1
    x64.mov rbx, rdx
    x64.jmp alias_loop_0.header
  alias_loop_0.exit:
    x64.mov rcx, [rbp+-976]
    x64.call __mm_decref_maybenull_helper
    x64.test r15, r15
    x64.jge guard_0.after
    x64.jmp guard_0
  try_0.otherwise:
    x64.call __mm_decref_maybenull_helper
    x64.mov ecx, 21
    x64.call mrt_alloc
    x64.mov r12, r8
    x64.mov rcx, rbx
    x64.mov rdx, r12
    x64.call mrt_i64_to_string
    x64.mov rbx, r8
    x64.mov r13d, 20
    x64.mov r8, rbx
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
    x64.mov [rbp-264], r12
    x64.mov [rbp-272], rbx
    x64.rep_movsb
    x64.lea r9, [rip+__istr_9]
    x64.add r8, rbx
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
    x64.mov rcx, r12
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
    x64.lea r12, [rip+__destruct_String]
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
    x64.mov [rbp+-968], r8
    x64.mov r8, [rbp+-968]
    x64.add r8, 32
    x64.mov [rbp+-968], r8
  inlined_stdlib.__slab_alloc_0_1:
    x64.xor r8d, r8d
    x64.mov r8, [rbp+-968]
    x64.cmp r8, 32768
    x64.jle inlined_stdlib.__slab_class_index_for_0_1
  inlined_stdlib.__slab_alloc_1_1:
    x64.lea rcx, [rip+__slab_lock]
    x64.call __rt_oslock_acquire
    x64.mov rcx, [rbp+-968]
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
    x64.cmp r8, [rbp+-968]
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
    x64.mov rcx, r14
    x64.add rcx, 32
    x64.mov r12, rcx
  inline_cont_main_8:
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
    x64.mov r12, [rcx+8] (8b)
    x64.mov r13, [rcx+0] (8b)
    x64.call __mm_decref_maybenull_helper
    x64.add r13, r12
    x64.add r15, r13
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
    x64.mov rcx, [rbp+-1000]
    x64.call stdlib.__mm_incref
    x64.mov rcx, [rbp+-1000]
    x64.mov rdx, 0
    x64.jmp inline_cont_main_4
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

```RequiredIR:wasm32-wasi
module {
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
    %26, %27 = mir.try_call @ArrayIterator.create(%25, %4)
    %28 = mir.mov_imm 0 : i64
    %29 = mir.cmp ne, %27, %28
    mir.cond_br %29 [then: inlined_Array.createIterator_1_0(), else: inlined_Array.createIterator_2_0()]
  inlined_Array.createIterator_1_0:
    %30 = mir.mov_imm 0 : i64
    mir.br inline_cont_row_total_0(%30, %27)
  inlined_Array.createIterator_2_0:
    %31 = mir.mov_imm 0 : i64
    mir.br inline_cont_row_total_0(%26, %31)
  inline_cont_row_total_0(%32: i64, %33: i64):
    %7 = mir.mov_imm 0 : i64
    %8 = mir.cmp ne, %33, %7
    mir.cond_br %8 [then: __rc_edge_8_0(), else: iter_0(%2)]
  inlined_ArrayIterator.advance_0_0:
    %34 = mir.load %32, 0 width: qword
    %35 = mir.load %34, 8 width: qword
    %36 = mir.load %34, 16 width: qword
    %37 = mir.mov_imm 1 : i64
    %38 = mir.add.i64 %35, %37
    %39 = mir.cmp lt, %38, %36
    %40 = mir.sub.i64 %38, %35
    %41 = mir.mul.i64 %39, %40
    %42 = mir.add.i64 %35, %41
    mir.store %42, %34, 8 width: qword
    %43 = mir.mov_imm 1 : i64
    %44 = mir.sub.i64 %43, %39
    %45 = mir.mov_imm 1 : i64
    %46 = mir.mul.i64 %44, %45
    %47 = mir.mov_imm 0 : i64
    %48 = mir.cmp ne, %46, %47
    mir.cond_br %48 [then: inlined_ArrayIterator.advance_1_0(), else: inline_cont_row_total_2(%47, %47)]
  inlined_ArrayIterator.advance_1_0:
    %49 = mir.mov_imm 1 : i64
    mir.br inline_cont_row_total_2(%47, %49)
  inline_cont_row_total_2(%50: i64, %51: i64):
    %13 = mir.mov_imm 0 : i64
    %14 = mir.cmp ne, %51, %13
    mir.cond_br %14 [then: __rc_edge_12_0(), else: iter_0(%20)]
  iter_0(%22: i64):
    %52 = mir.load %32, 0 width: qword
    mir.br inlined_stdlib.__managed_mem_cursor_current_0_0()
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    %54 = mir.load %52, 0 width: qword
    %55 = mir.mov_imm 8 : i64
    %56 = mir.add.i64 %52, %55
    %57 = mir.load %56, 0 width: qword
    %58 = mir.mov_imm 24 : i64
    %59 = mir.add.i64 %52, %58
    %60 = mir.load %59, 0 width: qword
    %67 = mir.mov_imm 0 : i64
    %68 = mir.cmp lt, %60, %67
    mir.cond_br %68 [then: inlined_stdlib.__managed_mem_cursor_current_1_0(), else: inlined_stdlib.__managed_mem_cursor_current_2_0()]
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    %96 = mir.mov_imm 0 : i64
    %97 = mir.sub.i64 %96, %60
    %70 = mir.mul.i64 %57, %97
    %71 = mir.mov_imm 3 : i64
    %72 = mir.shr.i64 %70, %71
    %73 = mir.add.i64 %54, %72
    %74 = mir.mov_imm 0 : i64
    %75 = mir.load_byte %73, %74
    %76 = mir.mov_imm 1 : i64
    %77 = mir.shl.i64 %76, %97
    %78 = mir.mov_imm 1 : i64
    %79 = mir.sub.i64 %77, %78
    %80 = mir.mov_imm 7 : i64
    %81 = mir.and.i64 %70, %80
    %82 = mir.shr.i64 %75, %81
    %83 = mir.and.i64 %82, %79
    mir.br inline_cont_row_total_3(%83)
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    %63 = mir.mul.i64 %57, %60
    %64 = mir.add.i64 %54, %63
    mir.br inlined_stdlib.__managed_mem_load_sized_0_0()
  inlined_stdlib.__managed_mem_load_sized_0_0:
    %84 = mir.mov_imm 1 : i64
    %85 = mir.cmp eq, %60, %84
    mir.cond_br %85 [then: inlined_stdlib.__managed_mem_load_sized_1_0(), else: inlined_stdlib.__managed_mem_load_sized_2_0()]
  inlined_stdlib.__managed_mem_load_sized_1_0:
    %86 = mir.mov_imm 0 : i64
    %87 = mir.load_byte %64, %86
    mir.br inline_cont_row_total_15(%87)
  inlined_stdlib.__managed_mem_load_sized_2_0:
    %88 = mir.mov_imm 2 : i64
    %89 = mir.cmp eq, %60, %88
    mir.cond_br %89 [then: inlined_stdlib.__managed_mem_load_sized_3_0(), else: inlined_stdlib.__managed_mem_load_sized_4_0()]
  inlined_stdlib.__managed_mem_load_sized_3_0:
    %90 = mir.load %64, 0 width: halfword
    mir.br inline_cont_row_total_15(%90)
  inlined_stdlib.__managed_mem_load_sized_4_0:
    %91 = mir.mov_imm 4 : i64
    %92 = mir.cmp eq, %60, %91
    mir.cond_br %92 [then: inlined_stdlib.__managed_mem_load_sized_5_0(), else: inlined_stdlib.__managed_mem_load_sized_6_0()]
  inlined_stdlib.__managed_mem_load_sized_5_0:
    %93 = mir.load %64, 0 width: word
    mir.br inline_cont_row_total_15(%93)
  inlined_stdlib.__managed_mem_load_sized_6_0:
    %94 = mir.load %64, 0 width: qword
    mir.br inline_cont_row_total_15(%94)
  inline_cont_row_total_15(%95: i64):
    mir.br inline_cont_row_total_3(%95)
  inline_cont_row_total_3(%66: i64):
    %20 = mir.add.i64 %22, %66
    mir.br inlined_ArrayIterator.advance_0_0()
  iter_0.exit(%23: i64):
    mir.ret %23
  __rc_edge_8_0:
    %98 = mir.call @__mm_decref_maybenull_helper(%32)
    mir.br iter_0.exit(%2)
  __rc_edge_12_0:
    %99 = mir.call @__mm_decref_maybenull_helper(%32)
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
    %28, %29 = mir.try_call @ArrayIterator.create(%27, %5)
    %30 = mir.mov_imm 0 : i64
    %31 = mir.cmp ne, %29, %30
    mir.cond_br %31 [then: inlined_Array.createIterator_1_0(), else: inlined_Array.createIterator_2_0()]
  inlined_Array.createIterator_1_0:
    %32 = mir.mov_imm 0 : i64
    mir.br inline_cont_matrix_total_0(%32, %29)
  inlined_Array.createIterator_2_0:
    %33 = mir.mov_imm 0 : i64
    mir.br inline_cont_matrix_total_0(%28, %33)
  inline_cont_matrix_total_0(%34: i64, %35: i64):
    %8 = mir.mov_imm 0 : i64
    %9 = mir.cmp ne, %35, %8
    mir.cond_br %9 [then: __rc_edge_8_0(), else: iter_0(%3)]
  inlined_ArrayIterator.advance_0_0:
    %36 = mir.load %34, 0 width: qword
    %37 = mir.load %36, 8 width: qword
    %38 = mir.load %36, 16 width: qword
    %39 = mir.mov_imm 1 : i64
    %40 = mir.add.i64 %37, %39
    %41 = mir.cmp lt, %40, %38
    %42 = mir.sub.i64 %40, %37
    %43 = mir.mul.i64 %41, %42
    %44 = mir.add.i64 %37, %43
    mir.store %44, %36, 8 width: qword
    %45 = mir.mov_imm 1 : i64
    %46 = mir.sub.i64 %45, %41
    %47 = mir.mov_imm 1 : i64
    %48 = mir.mul.i64 %46, %47
    %49 = mir.mov_imm 0 : i64
    %50 = mir.cmp ne, %48, %49
    mir.cond_br %50 [then: inlined_ArrayIterator.advance_1_0(), else: inline_cont_matrix_total_2(%49, %49)]
  inlined_ArrayIterator.advance_1_0:
    %51 = mir.mov_imm 1 : i64
    mir.br inline_cont_matrix_total_2(%49, %51)
  inline_cont_matrix_total_2(%52: i64, %53: i64):
    %14 = mir.mov_imm 0 : i64
    %15 = mir.cmp ne, %53, %14
    mir.cond_br %15 [then: __rc_edge_12_0(), else: iter_0(%22)]
  iter_0(%24: i64):
    %54 = mir.load %34, 0 width: qword
    mir.br inlined_stdlib.__managed_mem_cursor_current_0_0()
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    %56 = mir.load %54, 0 width: qword
    %57 = mir.mov_imm 8 : i64
    %58 = mir.add.i64 %54, %57
    %59 = mir.load %58, 0 width: qword
    %60 = mir.mov_imm 24 : i64
    %61 = mir.add.i64 %54, %60
    %62 = mir.load %61, 0 width: qword
    %69 = mir.mov_imm 0 : i64
    %70 = mir.cmp lt, %62, %69
    mir.cond_br %70 [then: inlined_stdlib.__managed_mem_cursor_current_1_0(), else: inlined_stdlib.__managed_mem_cursor_current_2_0()]
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    %98 = mir.mov_imm 0 : i64
    %99 = mir.sub.i64 %98, %62
    %72 = mir.mul.i64 %59, %99
    %73 = mir.mov_imm 3 : i64
    %74 = mir.shr.i64 %72, %73
    %75 = mir.add.i64 %56, %74
    %76 = mir.mov_imm 0 : i64
    %77 = mir.load_byte %75, %76
    %78 = mir.mov_imm 1 : i64
    %79 = mir.shl.i64 %78, %99
    %80 = mir.mov_imm 1 : i64
    %81 = mir.sub.i64 %79, %80
    %82 = mir.mov_imm 7 : i64
    %83 = mir.and.i64 %72, %82
    %84 = mir.shr.i64 %77, %83
    %85 = mir.and.i64 %84, %81
    mir.br __rc_edge_14_0()
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    %65 = mir.mul.i64 %59, %62
    %66 = mir.add.i64 %56, %65
    mir.br inlined_stdlib.__managed_mem_load_sized_0_0()
  inlined_stdlib.__managed_mem_load_sized_0_0:
    %86 = mir.mov_imm 1 : i64
    %87 = mir.cmp eq, %62, %86
    mir.cond_br %87 [then: inlined_stdlib.__managed_mem_load_sized_1_0(), else: inlined_stdlib.__managed_mem_load_sized_2_0()]
  inlined_stdlib.__managed_mem_load_sized_1_0:
    %88 = mir.mov_imm 0 : i64
    %89 = mir.load_byte %66, %88
    mir.br inline_cont_matrix_total_15(%89)
  inlined_stdlib.__managed_mem_load_sized_2_0:
    %90 = mir.mov_imm 2 : i64
    %91 = mir.cmp eq, %62, %90
    mir.cond_br %91 [then: inlined_stdlib.__managed_mem_load_sized_3_0(), else: inlined_stdlib.__managed_mem_load_sized_4_0()]
  inlined_stdlib.__managed_mem_load_sized_3_0:
    %92 = mir.load %66, 0 width: halfword
    mir.br inline_cont_matrix_total_15(%92)
  inlined_stdlib.__managed_mem_load_sized_4_0:
    %93 = mir.mov_imm 4 : i64
    %94 = mir.cmp eq, %62, %93
    mir.cond_br %94 [then: inlined_stdlib.__managed_mem_load_sized_5_0(), else: inlined_stdlib.__managed_mem_load_sized_6_0()]
  inlined_stdlib.__managed_mem_load_sized_5_0:
    %95 = mir.load %66, 0 width: word
    mir.br inline_cont_matrix_total_15(%95)
  inlined_stdlib.__managed_mem_load_sized_6_0:
    %96 = mir.load %66, 0 width: qword
    mir.br inline_cont_matrix_total_15(%96)
  inline_cont_matrix_total_15(%97: i64):
    mir.br __rc_edge_24_0()
  inline_cont_matrix_total_3(%68: i64):
    %21 = mir.call @row_total(%68)
    %100 = mir.call @__mm_decref_maybenull_helper(%68)
    %22 = mir.add.i64 %24, %21
    mir.br inlined_ArrayIterator.advance_0_0()
  iter_0.exit(%25: i64):
    mir.ret %25
  __rc_edge_8_0:
    %101 = mir.call @__mm_decref_maybenull_helper(%34)
    mir.br iter_0.exit(%3)
  __rc_edge_12_0:
    %102 = mir.call @__mm_decref_maybenull_helper(%34)
    mir.br iter_0.exit(%22)
  __rc_edge_14_0:
    %103 = mir.call @stdlib.__mm_incref(%85)
    mir.br inline_cont_matrix_total_3(%85)
  __rc_edge_24_0:
    %104 = mir.call @stdlib.__mm_incref(%97)
    mir.br inline_cont_matrix_total_3(%97)
  }
  func @points_x_sum(local0: i64) -> i64 {
  entry:
    %26 = mir.param local0 : i64
    %3 = mir.mov_imm 0 : i64
    %5 = mir.global_addr @__layout_Array_Point
    mir.br inlined_Array.createIterator_0_0()
  inlined_Array.createIterator_0_0:
    %27 = mir.load %26, 0 width: qword
    %28, %29 = mir.try_call @ArrayIterator.create(%27, %5)
    %30 = mir.mov_imm 0 : i64
    %31 = mir.cmp ne, %29, %30
    mir.cond_br %31 [then: inlined_Array.createIterator_1_0(), else: inlined_Array.createIterator_2_0()]
  inlined_Array.createIterator_1_0:
    %32 = mir.mov_imm 0 : i64
    mir.br inline_cont_points_x_sum_0(%32, %29)
  inlined_Array.createIterator_2_0:
    %33 = mir.mov_imm 0 : i64
    mir.br inline_cont_points_x_sum_0(%28, %33)
  inline_cont_points_x_sum_0(%34: i64, %35: i64):
    %8 = mir.mov_imm 0 : i64
    %9 = mir.cmp ne, %35, %8
    mir.cond_br %9 [then: __rc_edge_8_0(), else: iter_0(%3)]
  inlined_ArrayIterator.advance_0_0:
    %36 = mir.load %34, 0 width: qword
    %37 = mir.load %36, 8 width: qword
    %38 = mir.load %36, 16 width: qword
    %39 = mir.mov_imm 1 : i64
    %40 = mir.add.i64 %37, %39
    %41 = mir.cmp lt, %40, %38
    %42 = mir.sub.i64 %40, %37
    %43 = mir.mul.i64 %41, %42
    %44 = mir.add.i64 %37, %43
    mir.store %44, %36, 8 width: qword
    %45 = mir.mov_imm 1 : i64
    %46 = mir.sub.i64 %45, %41
    %47 = mir.mov_imm 1 : i64
    %48 = mir.mul.i64 %46, %47
    %49 = mir.mov_imm 0 : i64
    %50 = mir.cmp ne, %48, %49
    mir.cond_br %50 [then: inlined_ArrayIterator.advance_1_0(), else: inline_cont_points_x_sum_2(%49, %49)]
  inlined_ArrayIterator.advance_1_0:
    %51 = mir.mov_imm 1 : i64
    mir.br inline_cont_points_x_sum_2(%49, %51)
  inline_cont_points_x_sum_2(%52: i64, %53: i64):
    %14 = mir.mov_imm 0 : i64
    %15 = mir.cmp ne, %53, %14
    mir.cond_br %15 [then: __rc_edge_12_0(), else: iter_0(%22)]
  iter_0(%24: i64):
    %54 = mir.load %34, 0 width: qword
    mir.br inlined_stdlib.__managed_mem_cursor_current_0_0()
  inlined_stdlib.__managed_mem_cursor_current_0_0:
    %56 = mir.load %54, 0 width: qword
    %57 = mir.mov_imm 8 : i64
    %58 = mir.add.i64 %54, %57
    %59 = mir.load %58, 0 width: qword
    %60 = mir.mov_imm 24 : i64
    %61 = mir.add.i64 %54, %60
    %62 = mir.load %61, 0 width: qword
    %69 = mir.mov_imm 0 : i64
    %70 = mir.cmp lt, %62, %69
    mir.cond_br %70 [then: inlined_stdlib.__managed_mem_cursor_current_1_0(), else: inlined_stdlib.__managed_mem_cursor_current_2_0()]
  inlined_stdlib.__managed_mem_cursor_current_1_0:
    %98 = mir.mov_imm 0 : i64
    %99 = mir.sub.i64 %98, %62
    %72 = mir.mul.i64 %59, %99
    %73 = mir.mov_imm 3 : i64
    %74 = mir.shr.i64 %72, %73
    %75 = mir.add.i64 %56, %74
    %76 = mir.mov_imm 0 : i64
    %77 = mir.load_byte %75, %76
    %78 = mir.mov_imm 1 : i64
    %79 = mir.shl.i64 %78, %99
    %80 = mir.mov_imm 1 : i64
    %81 = mir.sub.i64 %79, %80
    %82 = mir.mov_imm 7 : i64
    %83 = mir.and.i64 %72, %82
    %84 = mir.shr.i64 %77, %83
    %85 = mir.and.i64 %84, %81
    mir.br __rc_edge_14_0()
  inlined_stdlib.__managed_mem_cursor_current_2_0:
    %65 = mir.mul.i64 %59, %62
    %66 = mir.add.i64 %56, %65
    mir.br inlined_stdlib.__managed_mem_load_sized_0_0()
  inlined_stdlib.__managed_mem_load_sized_0_0:
    %86 = mir.mov_imm 1 : i64
    %87 = mir.cmp eq, %62, %86
    mir.cond_br %87 [then: inlined_stdlib.__managed_mem_load_sized_1_0(), else: inlined_stdlib.__managed_mem_load_sized_2_0()]
  inlined_stdlib.__managed_mem_load_sized_1_0:
    %88 = mir.mov_imm 0 : i64
    %89 = mir.load_byte %66, %88
    mir.br inline_cont_points_x_sum_15(%89)
  inlined_stdlib.__managed_mem_load_sized_2_0:
    %90 = mir.mov_imm 2 : i64
    %91 = mir.cmp eq, %62, %90
    mir.cond_br %91 [then: inlined_stdlib.__managed_mem_load_sized_3_0(), else: inlined_stdlib.__managed_mem_load_sized_4_0()]
  inlined_stdlib.__managed_mem_load_sized_3_0:
    %92 = mir.load %66, 0 width: halfword
    mir.br inline_cont_points_x_sum_15(%92)
  inlined_stdlib.__managed_mem_load_sized_4_0:
    %93 = mir.mov_imm 4 : i64
    %94 = mir.cmp eq, %62, %93
    mir.cond_br %94 [then: inlined_stdlib.__managed_mem_load_sized_5_0(), else: inlined_stdlib.__managed_mem_load_sized_6_0()]
  inlined_stdlib.__managed_mem_load_sized_5_0:
    %95 = mir.load %66, 0 width: word
    mir.br inline_cont_points_x_sum_15(%95)
  inlined_stdlib.__managed_mem_load_sized_6_0:
    %96 = mir.load %66, 0 width: qword
    mir.br inline_cont_points_x_sum_15(%96)
  inline_cont_points_x_sum_15(%97: i64):
    mir.br __rc_edge_24_0()
  inline_cont_points_x_sum_3(%68: i64):
    %21 = mir.load %68, 0 width: qword
    %22 = mir.add.i64 %24, %21
    %100 = mir.call @__mm_decref_maybenull_helper(%68)
    mir.br inlined_ArrayIterator.advance_0_0()
  iter_0.exit(%25: i64):
    mir.ret %25
  __rc_edge_8_0:
    %101 = mir.call @__mm_decref_maybenull_helper(%34)
    mir.br iter_0.exit(%3)
  __rc_edge_12_0:
    %102 = mir.call @__mm_decref_maybenull_helper(%34)
    mir.br iter_0.exit(%22)
  __rc_edge_14_0:
    %103 = mir.call @stdlib.__mm_incref(%85)
    mir.br inline_cont_points_x_sum_3(%85)
  __rc_edge_24_0:
    %104 = mir.call @stdlib.__mm_incref(%97)
    mir.br inline_cont_points_x_sum_3(%97)
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
    %39 = mir.call @stdlib.__mm_alloc(%40, %41)
    mir.store %35, %39, 0 width: qword
    mir.store %7, %39, 8 width: qword
    %43 = mir.call @String.count(%39)
    %46 = mir.call @mm_drop(%39)
    %47 = mir.mov_imm -1 : i64
    %48 = mir.cmp ugt, %43, %47
    mir.cond_br %48 [then: __range_panic_0(), else: __range_ok_0()]
  __range_panic_0:
    %49 = mir.global_addr @__panic_msg_8e407baaf3c984cf
    %50 = mir.call @mrt_panic(%49)
  __range_ok_0:
    mir.ret %43
  }
  func @main() -> u8 {
  entry:
    %372 = mir.mov_imm -2 : i64
    %373 = mir.mov_imm 16 : i64
    %374 = mir.mov_imm 48 : i64
    %0 = mir.mov_imm 0 : i64
    mir.store_slot slot_15, %0
    %18 = mir.mov_imm 1 : i64
    %19 = mir.mov_imm 2 : i64
    %375 = mir.mov_imm 16 : i64
    %376 = mir.mov_imm 0 : i64
    %377 = mir.call @stdlib.__mm_alloc(%375, %376)
    mir.store %18, %377, 0 width: qword
    mir.store %19, %377, 8 width: qword
    %23 = mir.mov_imm 99 : i64
    mir.store %23, %377, 0 width: qword
    %27 = mir.load %377, 0 width: qword
    %28 = mir.add.i64 %0, %27
    %594 = mir.call @mm_drop(%377)
    %30 = mir.mov_imm 3 : i64
    %31 = mir.mov_imm 4 : i64
    %378 = mir.mov_imm 16 : i64
    %379 = mir.mov_imm 0 : i64
    %380 = mir.call @stdlib.__mm_alloc(%378, %379)
    mir.store %30, %380, 0 width: qword
    mir.store %31, %380, 8 width: qword
    %381 = mir.load %380, 0 width: qword
    %382 = mir.load %380, 8 width: qword
    %383 = mir.add.i64 %381, %382
    %34 = mir.add.i64 %28, %383
    %595 = mir.call @mm_drop(%380)
    %36 = mir.mov_imm 5 : i64
    %37 = mir.mov_imm 6 : i64
    %384 = mir.mov_imm 16 : i64
    %385 = mir.mov_imm 0 : i64
    %386 = mir.call @stdlib.__mm_alloc(%384, %385)
    mir.store %36, %386, 0 width: qword
    mir.store %37, %386, 8 width: qword
    %387 = mir.load %386, 0 width: qword
    %388 = mir.load %386, 8 width: qword
    %389 = mir.add.i64 %387, %388
    %40 = mir.add.i64 %34, %389
    %596 = mir.call @mm_drop(%386)
    %41 = mir.global_addr @__layout_Array_String
    %42 = mir.call @Array.create(%41)
    mir.br names_loop_0.header(%0)
  names_loop_0.header(%369: i64):
    %47 = mir.cmp lt, %369, %36
    mir.cond_br %47 [then: names_loop_0(), else: names_loop_0.exit()]
  names_loop_0:
    %50 = mir.global_addr @__istr_1
    %52 = mir.mov_imm 21 : i64
    %53 = mir.call @mrt_alloc(%52)
    %54 = mir.call @mrt_i64_to_string(%369, %53)
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
    %70 = mir.call @mrt_alloc_with_dtor(%374, %69)
    mir.store %0, %70, 40 width: qword
    mir.store %63, %70, 0 width: qword
    mir.store %60, %70, 8 width: qword
    mir.store %60, %70, 16 width: qword
    mir.store %18, %70, 24 width: qword
    %73 = mir.mov_imm -1 : i64
    mir.store %73, %70, 32 width: qword
    %76 = mir.func_addr @__destruct_String
    mir.br inlined_stdlib.__mm_alloc_0_0()
  inlined_stdlib.__mm_alloc_0_0:
    %390 = mir.mov_imm 0 : i64
    %391 = mir.mov_imm 1 : i64
    %392 = mir.cmp lt, %373, %391
    mir.cond_br %392 [then: inlined_stdlib.__mm_alloc_1_0(), else: inlined_stdlib.__mm_alloc_2_16(%373)]
  inlined_stdlib.__mm_alloc_1_0:
    %393 = mir.mov_imm 1 : i64
    mir.br inlined_stdlib.__mm_alloc_2_16(%393)
  inlined_stdlib.__mm_alloc_2_16(%394: i64):
    %395 = mir.global_addr @__mm_alloc_count
    mir.atomic_inc %395
    %396 = mir.mov_imm 32 : i64
    %397 = mir.add.i64 %394, %396
    mir.br inlined_stdlib.__slab_alloc_0_0()
  inlined_stdlib.__slab_alloc_0_0:
    %490 = mir.mov_imm 0 : i64
    %491 = mir.mov_imm 32768 : i64
    %492 = mir.cmp gt, %397, %491
    mir.cond_br %492 [then: inlined_stdlib.__slab_alloc_1_0(), else: inlined_stdlib.__slab_class_index_for_0_0()]
  inlined_stdlib.__slab_alloc_1_0:
    %493 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %493
    %494 = mir.call @stdlib.__slab_os_direct_alloc(%397)
    %495 = mir.global_addr @__slab_lock
    mir.os_lock_release %495
    mir.br inline_cont_main_16(%494)
  inlined_stdlib.__slab_class_index_for_0_0:
    %524 = mir.mov_imm 0 : i64
    %525 = mir.mov_imm 0 : i64
    %526 = mir.mov_imm 18 : i64
    mir.br inlined_stdlib.__slab_class_index_for_1_43(%524, %525)
  inlined_stdlib.__slab_class_index_for_1_43(%527: i64, %528: i64):
    %529 = mir.cmp lt, %528, %526
    mir.cond_br %529 [then: inlined_stdlib.__slab_class_index_for_2_0(), else: inlined_stdlib.__slab_class_index_for_4_0()]
  inlined_stdlib.__slab_class_index_for_2_0:
    %530 = mir.call @stdlib.__slab_class_size(%527)
    %531 = mir.cmp ge, %530, %397
    mir.cond_br %531 [then: inline_cont_main_28(%527), else: inlined_stdlib.__slab_class_index_for_6_0()]
  inlined_stdlib.__slab_class_index_for_3_0:
    %532 = mir.mov_imm 1 : i64
    %533 = mir.add.i64 %528, %532
    mir.br inlined_stdlib.__slab_class_index_for_1_43(%537, %533)
  inlined_stdlib.__slab_class_index_for_4_0:
    %534 = mir.mov_imm 136 : i64
    mir.os_exit %534
    %535 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_28(%535)
  inlined_stdlib.__slab_class_index_for_6_0:
    %536 = mir.mov_imm 1 : i64
    %537 = mir.add.i64 %527, %536
    mir.br inlined_stdlib.__slab_class_index_for_3_0()
  inline_cont_main_28(%538: i64):
    %580 = mir.mov_imm -1 : i64
    %498 = mir.cmp lt, %580, %490
    mir.cond_br %498 [then: inlined_stdlib.__slab_proc_at_0_0(), else: inlined_stdlib.__slab_alloc_4_30(%490)]
  inlined_stdlib.__slab_proc_at_0_0:
    %539 = mir.mov_imm 0 : i64
    %540 = mir.cmp lt, %490, %539
    mir.cond_br %540 [then: inlined_stdlib.__slab_proc_at_1_0(), else: inlined_stdlib.__slab_proc_at_2_0()]
  inlined_stdlib.__slab_proc_at_1_0:
    %541 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_29(%541)
  inlined_stdlib.__slab_proc_at_2_0:
    %542 = mir.global_addr @__sched_procs
    %543 = mir.load %542, 0 width: qword
    %544 = mir.mov_imm 0 : i64
    %545 = mir.cmp eq, %543, %544
    mir.cond_br %545 [then: inlined_stdlib.__slab_proc_at_3_0(), else: inlined_stdlib.__slab_proc_at_4_0()]
  inlined_stdlib.__slab_proc_at_3_0:
    %546 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_29(%546)
  inlined_stdlib.__slab_proc_at_4_0:
    %547 = mir.mov_imm 3 : i64
    %548 = mir.shl.i64 %490, %547
    %549 = mir.add.i64 %543, %548
    %550 = mir.load %549, 0 width: qword
    mir.br inline_cont_main_29(%550)
  inline_cont_main_29(%551: i64):
    %500 = mir.cmp ne, %551, %490
    mir.br inlined_stdlib.__slab_alloc_4_30(%500)
  inlined_stdlib.__slab_alloc_4_30(%501: i64):
    mir.cond_br %501 [then: inlined_stdlib.__slab_alloc_5_0(), else: inlined_stdlib.__slab_alloc_6_0()]
  inlined_stdlib.__slab_alloc_5_0:
    %502 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %502
    %503 = mir.call @stdlib.__slab_alloc_class(%538)
    %504 = mir.global_addr @__slab_lock
    mir.os_lock_release %504
    mir.br inline_cont_main_16(%503)
  inlined_stdlib.__slab_alloc_6_0:
    %505 = mir.call @stdlib.__slab_alloc_class(%538)
    mir.br inline_cont_main_16(%505)
  inline_cont_main_16(%506: i64):
    mir.store %390, %506, 0 width: qword
    %399 = mir.mov_imm 8 : i64
    %400 = mir.add.i64 %506, %399
    mir.store %76, %400, 0 width: qword
    %401 = mir.mov_imm 16 : i64
    %402 = mir.add.i64 %506, %401
    mir.store %394, %402, 0 width: qword
    %403 = mir.mov_imm 24 : i64
    %404 = mir.add.i64 %506, %403
    mir.store %390, %404, 0 width: qword
    %405 = mir.mov_imm 32 : i64
    %406 = mir.add.i64 %506, %405
    mir.br inline_cont_main_2(%406)
  inline_cont_main_2(%407: i64):
    %617 = mir.call @stdlib.__mm_incref(%406)
    mir.store %70, %407, 0 width: qword
    mir.store %0, %407, 8 width: qword
    %78 = mir.global_addr @__layout_Array_String
    %79 = mir.call @Array.push(%42, %407, %78)
    mir.br names_loop_0.step()
  names_loop_0.step:
    %82 = mir.add.i64 %369, %18
    mir.br names_loop_0.header(%82)
  names_loop_0.exit:
    %408 = mir.global_addr @__layout_Array_String
    %409 = mir.call @Array.count(%42, %408)
    %86 = mir.add.i64 %40, %409
    %597 = mir.call @__mm_decref_maybenull_helper(%42)
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
    %598 = mir.call @__mm_decref_maybenull_helper(%108)
    %120 = mir.add.i64 %86, %119
    %410 = mir.mov_imm 16 : i64
    %411 = mir.mov_imm 0 : i64
    %412 = mir.call @stdlib.__mm_alloc(%410, %411)
    mir.store %0, %412, 0 width: qword
    mir.store %0, %412, 8 width: qword
    %413 = mir.load %412, 0 width: qword
    %414 = mir.load %412, 8 width: qword
    %415 = mir.add.i64 %413, %414
    %127 = mir.add.i64 %120, %415
    %416 = mir.load %412, 0 width: qword
    %417 = mir.load %412, 8 width: qword
    %418 = mir.add.i64 %416, %417
    %131 = mir.add.i64 %127, %418
    %599 = mir.call @mm_drop(%412)
    %132 = mir.mov_imm 10 : i64
    %133 = mir.mov_imm 20 : i64
    %419 = mir.mov_imm 16 : i64
    %420 = mir.mov_imm 0 : i64
    %421 = mir.call @stdlib.__mm_alloc(%419, %420)
    mir.store %132, %421, 0 width: qword
    mir.store %133, %421, 8 width: qword
    %137 = mir.load %421, 0 width: qword
    %138 = mir.add.i64 %131, %137
    %600 = mir.call @mm_drop(%421)
    %139 = mir.global_addr @__istr_2
    %141 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %142 = mir.call @mrt_alloc_with_dtor(%374, %141)
    mir.store %0, %142, 40 width: qword
    mir.store %139, %142, 0 width: qword
    mir.store %36, %142, 8 width: qword
    mir.store %372, %142, 16 width: qword
    mir.store %18, %142, 24 width: qword
    mir.store %0, %142, 32 width: qword
    %150 = mir.func_addr @__destruct_String
    %148 = mir.call @stdlib.__mm_alloc(%373, %150)
    %582 = mir.call @stdlib.__mm_incref(%148)
    mir.store %142, %148, 0 width: qword
    mir.store %18, %148, 8 width: qword
    %152 = mir.mov_imm 30 : i64
    %422 = mir.mov_imm 16 : i64
    %423 = mir.func_addr @__destruct_Person
    %424 = mir.call @stdlib.__mm_alloc(%422, %423)
    %425 = mir.mov_imm 0 : i64
    mir.store %425, %424, 0 width: qword
    mir.store %148, %424, 0 width: qword
    mir.store %152, %424, 8 width: qword
    %155 = mir.global_addr @__istr_3
    %157 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %158 = mir.call @mrt_alloc_with_dtor(%374, %157)
    mir.store %0, %158, 40 width: qword
    mir.store %155, %158, 0 width: qword
    mir.store %30, %158, 8 width: qword
    mir.store %372, %158, 16 width: qword
    mir.store %18, %158, 24 width: qword
    mir.store %0, %158, 32 width: qword
    %166 = mir.func_addr @__destruct_String
    %164 = mir.call @stdlib.__mm_alloc(%373, %166)
    %583 = mir.call @stdlib.__mm_incref(%164)
    mir.store %158, %164, 0 width: qword
    mir.store %18, %164, 8 width: qword
    %168 = mir.load %424, 0 width: qword
    %169 = mir.call @__mm_decref_maybenull_helper(%168)
    mir.store %164, %424, 0 width: qword
    %171 = mir.global_addr @__istr_4
    %173 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %174 = mir.call @mrt_alloc_with_dtor(%374, %173)
    mir.store %0, %174, 40 width: qword
    mir.store %171, %174, 0 width: qword
    mir.store %36, %174, 8 width: qword
    mir.store %372, %174, 16 width: qword
    mir.store %18, %174, 24 width: qword
    mir.store %0, %174, 32 width: qword
    %182 = mir.func_addr @__destruct_String
    %180 = mir.call @stdlib.__mm_alloc(%373, %182)
    %584 = mir.call @stdlib.__mm_incref(%180)
    mir.store %174, %180, 0 width: qword
    mir.store %18, %180, 8 width: qword
    %184 = mir.load %424, 0 width: qword
    %185 = mir.call @__mm_decref_maybenull_helper(%184)
    mir.store %180, %424, 0 width: qword
    %188 = mir.load %424, 8 width: qword
    %189 = mir.add.i64 %138, %188
    %601 = mir.call @mm_drop(%424)
    %190 = mir.global_addr @__istr_5
    %192 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %193 = mir.call @mrt_alloc_with_dtor(%374, %192)
    mir.store %0, %193, 40 width: qword
    mir.store %190, %193, 0 width: qword
    %195 = mir.mov_imm 4 : i64
    mir.store %195, %193, 8 width: qword
    mir.store %372, %193, 16 width: qword
    mir.store %18, %193, 24 width: qword
    mir.store %0, %193, 32 width: qword
    %201 = mir.func_addr @__destruct_String
    %199 = mir.call @stdlib.__mm_alloc(%373, %201)
    %585 = mir.call @stdlib.__mm_incref(%199)
    mir.store %193, %199, 0 width: qword
    mir.store %18, %199, 8 width: qword
    %426 = mir.mov_imm 16 : i64
    %427 = mir.func_addr @__destruct_Shape
    %428 = mir.mov_imm 0 : i64
    %429 = mir.call @stdlib.__mm_alloc(%426, %427)
    mir.store %428, %429, 0 width: qword
    mir.store %199, %429, 8 width: qword
    %204 = mir.global_addr @__istr_6
    %206 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %207 = mir.call @mrt_alloc_with_dtor(%374, %206)
    mir.store %0, %207, 40 width: qword
    mir.store %204, %207, 0 width: qword
    mir.store %30, %207, 8 width: qword
    mir.store %372, %207, 16 width: qword
    mir.store %18, %207, 24 width: qword
    mir.store %0, %207, 32 width: qword
    %215 = mir.func_addr @__destruct_String
    %213 = mir.call @stdlib.__mm_alloc(%373, %215)
    %586 = mir.call @stdlib.__mm_incref(%213)
    mir.store %207, %213, 0 width: qword
    mir.store %18, %213, 8 width: qword
    %430 = mir.mov_imm 16 : i64
    %431 = mir.func_addr @__destruct_Shape
    %432 = mir.mov_imm 1 : i64
    %433 = mir.call @stdlib.__mm_alloc(%430, %431)
    mir.store %432, %433, 0 width: qword
    mir.store %213, %433, 8 width: qword
    %434 = mir.mov_imm 16 : i64
    %435 = mir.func_addr @__destruct_Shape
    %436 = mir.mov_imm 2 : i64
    %437 = mir.call @stdlib.__mm_alloc(%434, %435)
    mir.store %436, %437, 0 width: qword
    %438 = mir.mov_imm 0 : i64
    mir.store %438, %437, 8 width: qword
    %221 = mir.call @describe(%429)
    %602 = mir.call @mm_drop(%429)
    %222 = mir.add.i64 %189, %221
    %225 = mir.call @describe(%433)
    %603 = mir.call @mm_drop(%433)
    %226 = mir.add.i64 %222, %225
    %229 = mir.call @describe(%437)
    %604 = mir.call @mm_drop(%437)
    %230 = mir.add.i64 %226, %229
    %231 = mir.global_addr @__istr_7
    %233 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %234 = mir.call @mrt_alloc_with_dtor(%374, %233)
    mir.store %0, %234, 40 width: qword
    mir.store %231, %234, 0 width: qword
    %236 = mir.mov_imm 4 : i64
    mir.store %236, %234, 8 width: qword
    mir.store %372, %234, 16 width: qword
    mir.store %18, %234, 24 width: qword
    mir.store %0, %234, 32 width: qword
    %242 = mir.func_addr @__destruct_String
    %240 = mir.call @stdlib.__mm_alloc(%373, %242)
    %587 = mir.call @stdlib.__mm_incref(%240)
    mir.store %234, %240, 0 width: qword
    mir.store %18, %240, 8 width: qword
    %609 = mir.load_slot slot_15
    %610 = mir.call @__mm_decref_maybenull_helper(%609)
    mir.store_slot slot_15, %240
    %244 = mir.func_addr @main$closure_0
    %245 = mir.stack_slot_addr slot_15
    %246 = mir.mov_imm 8 : i64
    %248 = mir.call @stdlib.__mm_alloc(%246, %0)
    mir.store %245, %248, 0 width: qword
    %251 = mir.mov_imm 7 : i64
    %439 = mir.indirect_call %244(%251, %248)
    %254 = mir.add.i64 %230, %439
    %257 = mir.mov_imm 8 : i64
    %440 = mir.indirect_call %244(%257, %248)
    %605 = mir.call @mm_drop(%248)
    %260 = mir.add.i64 %254, %440
    %261 = mir.global_addr @__layout_Array_Point
    %262 = mir.call @Array.create(%261)
    %265 = mir.mov_imm 2 : i64
    %441 = mir.mov_imm 16 : i64
    %442 = mir.mov_imm 0 : i64
    %443 = mir.call @stdlib.__mm_alloc(%441, %442)
    %588 = mir.call @stdlib.__mm_incref(%443)
    mir.store %18, %443, 0 width: qword
    mir.store %265, %443, 8 width: qword
    %267 = mir.global_addr @__layout_Array_Point
    %268 = mir.call @Array.push(%262, %443, %267)
    %271 = mir.mov_imm 4 : i64
    %444 = mir.mov_imm 16 : i64
    %445 = mir.mov_imm 0 : i64
    %446 = mir.call @stdlib.__mm_alloc(%444, %445)
    %589 = mir.call @stdlib.__mm_incref(%446)
    mir.store %30, %446, 0 width: qword
    mir.store %271, %446, 8 width: qword
    %273 = mir.global_addr @__layout_Array_Point
    %274 = mir.call @Array.push(%262, %446, %273)
    %277 = mir.mov_imm 6 : i64
    %447 = mir.mov_imm 16 : i64
    %448 = mir.mov_imm 0 : i64
    %449 = mir.call @stdlib.__mm_alloc(%447, %448)
    %590 = mir.call @stdlib.__mm_incref(%449)
    mir.store %36, %449, 0 width: qword
    mir.store %277, %449, 8 width: qword
    %279 = mir.global_addr @__layout_Array_Point
    %280 = mir.call @Array.push(%262, %449, %279)
    %283 = mir.call @points_x_sum(%262)
    %606 = mir.call @__mm_decref_maybenull_helper(%262)
    %284 = mir.add.i64 %260, %283
    %285 = mir.global_addr @__layout_Array_Point
    %286 = mir.call @Array.create(%285)
    %288 = mir.mov_imm 7 : i64
    %289 = mir.mov_imm 8 : i64
    %450 = mir.mov_imm 16 : i64
    %451 = mir.mov_imm 0 : i64
    %452 = mir.call @stdlib.__mm_alloc(%450, %451)
    %591 = mir.call @stdlib.__mm_incref(%452)
    mir.store %288, %452, 0 width: qword
    mir.store %289, %452, 8 width: qword
    %291 = mir.global_addr @__layout_Array_Point
    %292 = mir.call @Array.push(%286, %452, %291)
    %294 = mir.mov_imm 9 : i64
    %295 = mir.mov_imm 10 : i64
    %453 = mir.mov_imm 16 : i64
    %454 = mir.mov_imm 0 : i64
    %455 = mir.call @stdlib.__mm_alloc(%453, %454)
    %592 = mir.call @stdlib.__mm_incref(%455)
    mir.store %294, %455, 0 width: qword
    mir.store %295, %455, 8 width: qword
    %297 = mir.global_addr @__layout_Array_Point
    %298 = mir.call @Array.push(%286, %455, %297)
    %300 = mir.mov_imm 11 : i64
    %301 = mir.mov_imm 12 : i64
    %456 = mir.mov_imm 16 : i64
    %457 = mir.mov_imm 0 : i64
    %458 = mir.call @stdlib.__mm_alloc(%456, %457)
    %593 = mir.call @stdlib.__mm_incref(%458)
    mir.store %300, %458, 0 width: qword
    mir.store %301, %458, 8 width: qword
    %303 = mir.global_addr @__layout_Array_Point
    %304 = mir.call @Array.push(%286, %458, %303)
    mir.br alias_loop_0.header(%284, %0)
  alias_loop_0.header(%368: i64, %370: i64):
    %309 = mir.cmp lt, %370, %30
    mir.cond_br %309 [then: inlined_Array.get_0_0(), else: alias_loop_0.exit()]
  inlined_Array.get_0_0:
    %459 = mir.load %286, 0 width: qword
    %460, %461 = mir.try_call @stdlib.__managed_mem_get(%459, %370)
    %462 = mir.mov_imm 0 : i64
    %463 = mir.cmp ne, %461, %462
    mir.cond_br %463 [then: inlined_Array.get_1_0(), else: inlined_Array.get_3_0()]
  inlined_Array.get_1_0:
    %464 = mir.mov_imm 0 : i64
    %465 = mir.mov_imm 1 : i64
    mir.br inline_cont_main_6(%464, %465)
  inlined_Array.get_3_0:
    %466 = mir.mov_imm 0 : i64
    mir.br __rc_edge_20_0()
  inline_cont_main_6(%467: i64, %468: i64):
    %316 = mir.cmp ne, %468, %0
    mir.cond_br %316 [then: try_0.otherwise(), else: try_0.merge(%467)]
  alias_loop_0.step:
    %320 = mir.add.i64 %370, %18
    mir.br alias_loop_0.header(%365, %320)
  alias_loop_0.exit:
    %615 = mir.call @__mm_decref_maybenull_helper(%286)
    %323 = mir.cmp lt, %368, %0
    mir.cond_br %323 [then: guard_0(), else: guard_0.after()]
  try_0.otherwise:
    %616 = mir.call @__mm_decref_maybenull_helper(%467)
    %325 = mir.global_addr @__istr_8
    %326 = mir.mov_imm 75 : i64
    %327 = mir.mov_imm 21 : i64
    %328 = mir.call @mrt_alloc(%327)
    %329 = mir.call @mrt_i64_to_string(%370, %328)
    %330 = mir.global_addr @__istr_9
    %331 = mir.mov_imm 20 : i64
    %332 = mir.global_addr @__istr_10
    %335 = mir.mov_imm 75 : i64
    %336 = mir.add.i64 %335, %329
    %337 = mir.add.i64 %336, %331
    %338 = mir.add.i64 %337, %18
    %340 = mir.add.i64 %338, %18
    %341 = mir.call @mrt_alloc(%340)
    mir.memcpy %341, %325, %326
    %342 = mir.add.i64 %341, %326
    mir.memcpy %342, %328, %329
    %343 = mir.add.i64 %342, %329
    mir.memcpy %343, %330, %331
    %344 = mir.add.i64 %343, %331
    mir.memcpy %344, %332, %18
    %346 = mir.call @stdlib.__mm_decref(%328)
    %348 = mir.func_addr @stdlib.__destruct___ManagedMemory
    %349 = mir.call @mrt_alloc_with_dtor(%374, %348)
    mir.store %0, %349, 40 width: qword
    mir.store %341, %349, 0 width: qword
    mir.store %338, %349, 8 width: qword
    mir.store %338, %349, 16 width: qword
    mir.store %18, %349, 24 width: qword
    %352 = mir.mov_imm -1 : i64
    mir.store %352, %349, 32 width: qword
    %355 = mir.func_addr @__destruct_String
    mir.br inlined_stdlib.__mm_alloc_0_1()
  inlined_stdlib.__mm_alloc_0_1:
    %469 = mir.mov_imm 0 : i64
    %470 = mir.mov_imm 1 : i64
    %471 = mir.cmp lt, %373, %470
    mir.cond_br %471 [then: inlined_stdlib.__mm_alloc_1_1(), else: inlined_stdlib.__mm_alloc_2_24(%373)]
  inlined_stdlib.__mm_alloc_1_1:
    %472 = mir.mov_imm 1 : i64
    mir.br inlined_stdlib.__mm_alloc_2_24(%472)
  inlined_stdlib.__mm_alloc_2_24(%473: i64):
    %474 = mir.global_addr @__mm_alloc_count
    mir.atomic_inc %474
    %475 = mir.mov_imm 32 : i64
    %476 = mir.add.i64 %473, %475
    mir.br inlined_stdlib.__slab_alloc_0_1()
  inlined_stdlib.__slab_alloc_0_1:
    %507 = mir.mov_imm 0 : i64
    %508 = mir.mov_imm 32768 : i64
    %509 = mir.cmp gt, %476, %508
    mir.cond_br %509 [then: inlined_stdlib.__slab_alloc_1_1(), else: inlined_stdlib.__slab_class_index_for_0_1()]
  inlined_stdlib.__slab_alloc_1_1:
    %510 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %510
    %511 = mir.call @stdlib.__slab_os_direct_alloc(%476)
    %512 = mir.global_addr @__slab_lock
    mir.os_lock_release %512
    mir.br inline_cont_main_24(%511)
  inlined_stdlib.__slab_class_index_for_0_1:
    %552 = mir.mov_imm 0 : i64
    %553 = mir.mov_imm 0 : i64
    %554 = mir.mov_imm 18 : i64
    mir.br inlined_stdlib.__slab_class_index_for_1_57(%552, %553)
  inlined_stdlib.__slab_class_index_for_1_57(%555: i64, %556: i64):
    %557 = mir.cmp lt, %556, %554
    mir.cond_br %557 [then: inlined_stdlib.__slab_class_index_for_2_1(), else: inlined_stdlib.__slab_class_index_for_4_1()]
  inlined_stdlib.__slab_class_index_for_2_1:
    %558 = mir.call @stdlib.__slab_class_size(%555)
    %559 = mir.cmp ge, %558, %476
    mir.cond_br %559 [then: inline_cont_main_36(%555), else: inlined_stdlib.__slab_class_index_for_6_1()]
  inlined_stdlib.__slab_class_index_for_3_1:
    %560 = mir.mov_imm 1 : i64
    %561 = mir.add.i64 %556, %560
    mir.br inlined_stdlib.__slab_class_index_for_1_57(%565, %561)
  inlined_stdlib.__slab_class_index_for_4_1:
    %562 = mir.mov_imm 136 : i64
    mir.os_exit %562
    %563 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_36(%563)
  inlined_stdlib.__slab_class_index_for_6_1:
    %564 = mir.mov_imm 1 : i64
    %565 = mir.add.i64 %555, %564
    mir.br inlined_stdlib.__slab_class_index_for_3_1()
  inline_cont_main_36(%566: i64):
    %581 = mir.mov_imm -1 : i64
    %515 = mir.cmp lt, %581, %507
    mir.cond_br %515 [then: inlined_stdlib.__slab_proc_at_0_1(), else: inlined_stdlib.__slab_alloc_4_38(%507)]
  inlined_stdlib.__slab_proc_at_0_1:
    %567 = mir.mov_imm 0 : i64
    %568 = mir.cmp lt, %507, %567
    mir.cond_br %568 [then: inlined_stdlib.__slab_proc_at_1_1(), else: inlined_stdlib.__slab_proc_at_2_1()]
  inlined_stdlib.__slab_proc_at_1_1:
    %569 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_37(%569)
  inlined_stdlib.__slab_proc_at_2_1:
    %570 = mir.global_addr @__sched_procs
    %571 = mir.load %570, 0 width: qword
    %572 = mir.mov_imm 0 : i64
    %573 = mir.cmp eq, %571, %572
    mir.cond_br %573 [then: inlined_stdlib.__slab_proc_at_3_1(), else: inlined_stdlib.__slab_proc_at_4_1()]
  inlined_stdlib.__slab_proc_at_3_1:
    %574 = mir.mov_imm 0 : i64
    mir.br inline_cont_main_37(%574)
  inlined_stdlib.__slab_proc_at_4_1:
    %575 = mir.mov_imm 3 : i64
    %576 = mir.shl.i64 %507, %575
    %577 = mir.add.i64 %571, %576
    %578 = mir.load %577, 0 width: qword
    mir.br inline_cont_main_37(%578)
  inline_cont_main_37(%579: i64):
    %517 = mir.cmp ne, %579, %507
    mir.br inlined_stdlib.__slab_alloc_4_38(%517)
  inlined_stdlib.__slab_alloc_4_38(%518: i64):
    mir.cond_br %518 [then: inlined_stdlib.__slab_alloc_5_1(), else: inlined_stdlib.__slab_alloc_6_1()]
  inlined_stdlib.__slab_alloc_5_1:
    %519 = mir.global_addr @__slab_lock
    mir.os_lock_acquire %519
    %520 = mir.call @stdlib.__slab_alloc_class(%566)
    %521 = mir.global_addr @__slab_lock
    mir.os_lock_release %521
    mir.br inline_cont_main_24(%520)
  inlined_stdlib.__slab_alloc_6_1:
    %522 = mir.call @stdlib.__slab_alloc_class(%566)
    mir.br inline_cont_main_24(%522)
  inline_cont_main_24(%523: i64):
    mir.store %469, %523, 0 width: qword
    %478 = mir.mov_imm 8 : i64
    %479 = mir.add.i64 %523, %478
    mir.store %355, %479, 0 width: qword
    %480 = mir.mov_imm 16 : i64
    %481 = mir.add.i64 %523, %480
    mir.store %473, %481, 0 width: qword
    %482 = mir.mov_imm 24 : i64
    %483 = mir.add.i64 %523, %482
    mir.store %469, %483, 0 width: qword
    %484 = mir.mov_imm 32 : i64
    %485 = mir.add.i64 %523, %484
    mir.br inline_cont_main_9(%485)
  inline_cont_main_9(%486: i64):
    %619 = mir.call @stdlib.__mm_incref(%485)
    mir.store %349, %486, 0 width: qword
    mir.store %0, %486, 8 width: qword
    %357 = mir.load %486, 0 width: qword
    %358 = mir.load %357, 0 width: qword
    %359 = mir.call @mrt_panic(%358)
    %607 = mir.call @mm_drop(%486)
    mir.br try_0.merge(%0)
  try_0.merge(%371: i64):
    %487 = mir.load %371, 0 width: qword
    %488 = mir.load %371, 8 width: qword
    %489 = mir.add.i64 %487, %488
    %365 = mir.add.i64 %368, %489
    %608 = mir.call @__mm_decref_maybenull_helper(%371)
    mir.br alias_loop_0.step()
  guard_0:
    %611 = mir.load_slot slot_15
    %612 = mir.call @__mm_decref_maybenull_helper(%611)
    mir.ret %18
  guard_0.after:
    %613 = mir.load_slot slot_15
    %614 = mir.call @__mm_decref_maybenull_helper(%613)
    mir.ret %0
  __rc_edge_20_0:
    %618 = mir.call @stdlib.__mm_incref(%460)
    mir.br inline_cont_main_6(%460, %466)
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
