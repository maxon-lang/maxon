---
feature: memory-safety
status: experimental
keywords: [reference, alias, clone, cloneable, equatable, ownership, region, lifetime]
category: core
---

# Memory Safety

## Documentation

### Reference-by-Default Assignment

In Maxon, assigning a struct variable to another variable copies the **heap pointer**, creating an alias (reference) to the same object:

```text
var a = Point{x: 1, y: 2}
var b = a        // b is an alias — same object as a
b.x = 99        // a.x is also 99 (shared mutation)
```

Rebinding a variable to a new struct does not affect the other:

```text
var a = Point{x: 1, y: 2}
var b = a
b = Point{x: 5, y: 6}  // rebinds b; a is unchanged
```

Primitives are unaffected — `var b = a` copies the value for int, float, bool, and byte.

### Explicit Clone with `.clone()`

To create an independent deep copy, use `.clone()`:

```text
var a = Point{x: 1, y: 2}
var b = a.clone()   // b is a new, independent copy
b.x = 99           // a.x is still 1
```

This requires the type to implement the `Cloneable` interface. The compiler auto-generates `Cloneable` conformance for any struct whose fields are all Cloneable (all primitives, String, Array, and Cloneable structs qualify).

### Equality

- `==` compares contents and requires `Equatable` conformance
- `is` compares reference identity (same heap object)
- The compiler auto-generates `Equatable` conformance for structs whose fields all implement `Equatable`

### Parameter Passing

All function parameters are passed by reference. The compiler infers parameter immutability: parameters that are not assigned to inside the function body are semantically immutable (`let`).

### Ownership and Regions

Every object is owned by a region (stack frame, struct, or array). When a region ends, everything it owns is freed. Return values transfer ownership to the caller's region. References must not outlive the objects they refer to.

## Tests

<!-- test: assignment-creates-alias -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer
end 'Point'

function main() returns ExitCode
	var a = Point{x: 1, y: 2}
	var b = a
	b.x = 99
	print("{a.x}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: rebind-does-not-mutate -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer
end 'Point'

function main() returns ExitCode
	var a = Point{x: 1, y: 2}
	var b = a
	b = Point{x: 99, y: 99}
	print("{a.x}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: non-cloneable-assignment-ok -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum Color
	red
	green
	blue
end 'Color'

type Item
	export var color Color
	export var value Integer
end 'Item'

function main() returns ExitCode
	var a = Item{color: Color.red, value: 42}
	var b = a
	b.value = 99
	return a.value
end 'main'
```
```exitcode
99
```

<!-- test: auto-cloneable -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer
end 'Point'

function main() returns ExitCode
	var a = Point{x: 10, y: 20}
	var b = a.clone()
	b.x = 99
	if a is not b 'diff'
		return a.x + a.y
	end 'diff'
	return 0
end 'main'
```
```exitcode
30
```

<!-- test: string-clone -->
```maxon
function main() returns ExitCode
	var a = "hello"
	var b = a.clone()
	if a is not b 'diff'
		return 1
	end 'diff'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: array-clone -->
```maxon
function main() returns ExitCode
	var a = [1, 2, 3]
	var b = a.clone()
	if a is not b 'diff'
		return 1
	end 'diff'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: nested-struct-clone -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var value Integer
end 'Inner'

type Outer
	export var a Inner
	export var b Integer
end 'Outer'

function main() returns ExitCode
	var x = Outer{a: Inner{value: 42}, b: 10}
	var y = x.clone()
	y.a.value = 99
	y.b = 0
	print("{x.a.value}\n")
	print("{x.b}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
10
```

<!-- test: eq-requires-equatable -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Callback
	export var fn () returns Integer
end 'Callback'

function main() returns ExitCode
	var a = Callback{fn: main}
	var b = Callback{fn: main}
	if a == b 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```maxoncstderr
error E3069: specs/fragments/memory-safety/eq-requires-equatable.test:11:7: '==' requires type 'Callback' to implement 'Equatable'
```

<!-- test: auto-equatable -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer
end 'Point'

function main() returns ExitCode
	var a = Point{x: 1, y: 2}
	var b = Point{x: 1, y: 2}
	var c = Point{x: 3, y: 4}
	var result = 0
	if a == b 'eq1'
		result = result + 1
	end 'eq1'
	if a == c 'eq2'
		result = result + 10
	end 'eq2'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: is-compares-refs -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer
end 'Point'

function main() returns ExitCode
	var a = Point{x: 1, y: 2}
	var b = a
	if a is b 'same'
		return 1
	end 'same'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: is-after-clone -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer
end 'Point'

function main() returns ExitCode
	var a = Point{x: 1, y: 2}
	var b = a.clone()
	if a is not b 'diff'
		return 1
	end 'diff'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: scope-cleanup -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Resource
	export var id Integer
end 'Resource'

function createAndDrop() returns Integer
	@heap var r = Resource{id: 42}
	return r.id
end 'createAndDrop'

function main() returns ExitCode
	return createAndDrop()
end 'main'
```
```exitcode
42
```

<!-- test: return-ownership-transfer -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer
end 'Point'

function makeRef() returns Point
	var local = Point{x: 1, y: 2}
	return local
end 'makeRef'

function main() returns ExitCode
	var p = makeRef()
	print("{p.x}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: block-scope-struct-release -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer
end 'Point'

function main() returns ExitCode
	var result = 0
	if true 'block'
		@heap var p = Point{x: 10, y: 20}
		result = p.x
	end 'block'
	return result
end 'main'
```
```exitcode
10
```
```RequiredMLIR:x86_64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %14 = maxon.literal {value = 0 : i64}
    maxon.assign %14 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 1 : i1}
    maxon.cond_br %15 [then: block_0, else: block_0.merge]
  block_0:
    %16 = maxon.literal {value = 10 : i64}
    %17 = maxon.literal {value = 20 : i64}
    %18 = maxon.struct_literal @Point
    maxon.assign %18 {var = p} {decl = 1 : i1} {mut = 1 : i1}
    %19 = maxon.struct_var_ref p
    %20 = maxon.field_access .x %19
    maxon.assign %20 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [p]
    maxon.br block_0.merge
  block_0.merge:
    %21 = maxon.var_ref {var = result} {type = i64}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at block-scope-struct-release.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [result]
    maxon.return %21
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, result
    %1 = arith.constant {value = 1 : i1}
    cf.cond_br %1 [then: block_0, else: block_0.merge]
  block_0:
    %2 = arith.constant {value = 10 : i64}
    %3 = arith.constant {value = 20 : i64}
    %4 = arith.constant {value = 16 : i64}
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.constant {value = 1 : i64}
    %7 = std.call_runtime @mm_alloc %4, %5, %6
    memref.store %7, p
    %8 = memref.load p : i64
    memref.store_indirect %2, %8+0
    %9 = memref.load p : i64
    memref.store_indirect %3, %9+8
    %10 = memref.load p : i64
    std.call_runtime @mm_incref %10
    %11 = memref.load p : i64
    %12 = memref.load_indirect %11+0
    memref.store %12, result
    %13 = memref.load p : i64
    std.call_runtime_if_nonnull @mm_decref %13
    cf.br block_0.merge
  block_0.merge:
    %15 = memref.load result : i64
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi lt %15, %16
    %18 = arith.constant {value = 4294967295 : i64}
    %19 = arith.cmpi gt %15, %18
    %20 = arith.ori1 %17, %19
    cf.cond_br %20 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %21 = memref.lea_symdata __panic_msg_0
    %22 = std.ptr_to_i64 %21
    std.call_runtime @maxon_panic %22
  __range_ok_1:
    func.return %15
  }
  func @__destruct_Point(ptr: i64) {
  entry:
    %24 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.xor rax, rax
    x86.mov [rbp-8], rax
    x86.mov rcx, 1
    x86.test rcx, rcx
    x86.je main.block_0.merge
  block_0:
    x86.mov rax, 10
    x86.mov rcx, 20
    x86.mov rcx, 16
    x86.xor rdx, rdx
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-16], rax
    x86.mov rdx, [rbp-16]
    x86.mov rbx, 10
    x86.mov [rdx+0], rbx
    x86.mov rsi, [rbp-16]
    x86.mov rdi, 20
    x86.mov [rsi+8], rdi
    x86.mov r8, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.mov r9, [rbp-16]
    x86.mov rax, [r9+0]
    x86.mov [rbp-8], rax
    x86.mov rax, [rbp-16]
    x86.test rax, rax
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.jmp main.block_0.merge
  block_0.merge:
    x86.mov rax, [rbp-8]
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.setl rdx
    x86.movzx rdx, rdxb
    x86.mov rbx, 4294967295
    x86.cmp rax, rbx
    x86.setg rsi
    x86.movzx rsi, rsib
    x86.or rdx, rsi
    x86.test rdx, rdx
    x86.je main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_0]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.epilogue
    x86.ret
  }
  func @__destruct_Point(ptr: i64) {
  entry:
    x86.jmp __destruct_Point.done
  done:
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %14 = maxon.literal {value = 0 : i64}
    maxon.assign %14 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 1 : i1}
    maxon.cond_br %15 [then: block_0, else: block_0.merge]
  block_0:
    %16 = maxon.literal {value = 10 : i64}
    %17 = maxon.literal {value = 20 : i64}
    %18 = maxon.struct_literal @Point
    maxon.assign %18 {var = p} {decl = 1 : i1} {mut = 1 : i1}
    %19 = maxon.struct_var_ref p
    %20 = maxon.field_access .x %19
    maxon.assign %20 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [p]
    maxon.br block_0.merge
  block_0.merge:
    %21 = maxon.var_ref {var = result} {type = i64}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at block-scope-struct-release.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    maxon.scope_end [result]
    maxon.return %21
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, result
    %1 = arith.constant {value = 1 : i1}
    cf.cond_br %1 [then: block_0, else: block_0.merge]
  block_0:
    %2 = arith.constant {value = 10 : i64}
    %3 = arith.constant {value = 20 : i64}
    %4 = arith.constant {value = 16 : i64}
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.constant {value = 1 : i64}
    %7 = std.call_runtime @mm_alloc %4, %5, %6
    memref.store %7, p
    %8 = memref.load p : i64
    memref.store_indirect %2, %8+0
    %9 = memref.load p : i64
    memref.store_indirect %3, %9+8
    %10 = memref.load p : i64
    std.call_runtime @mm_incref %10
    %11 = memref.load p : i64
    %12 = memref.load_indirect %11+0
    memref.store %12, result
    %13 = memref.load p : i64
    std.call_runtime_if_nonnull @mm_decref %13
    cf.br block_0.merge
  block_0.merge:
    %15 = memref.load result : i64
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi lt %15, %16
    %18 = arith.constant {value = 4294967295 : i64}
    %19 = arith.cmpi gt %15, %18
    %20 = arith.ori1 %17, %19
    cf.cond_br %20 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %21 = memref.lea_symdata __panic_msg_0
    %22 = std.ptr_to_i64 %21
    std.call_runtime @maxon_panic %22
  __range_ok_1:
    func.return %15
  }
  func @__destruct_Point(ptr: i64) {
  entry:
    %24 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.cmp x1, #0
    arm64.b.ne main.block_0
    arm64.b main.block_0.merge
  block_0:
    arm64.mov x0, #10
    arm64.mov x1, #20
    arm64.mov x0, #16
    arm64.mov x1, #0
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-16]
    arm64.ldr x2, [x29, #-16]
    arm64.mov x3, #10
    arm64.str x3, [x2, #0]
    arm64.ldr x4, [x29, #-16]
    arm64.mov x5, #20
    arm64.str x5, [x4, #8]
    arm64.ldr x6, [x29, #-16]
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_incref
    arm64.ldr x7, [x29, #-16]
    arm64.ldr x8, [x7, #0]
    arm64.str x8, [x29, #-8]
    arm64.ldr x9, [x29, #-16]
    arm64.cmp x9, #0
    arm64.b.eq main.__skip_guarded_21
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_21
    arm64.b main.block_0.merge
  block_0.merge:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_1
    arm64.b main.__range_ok_1
  __range_panic_1:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_1:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct_Point(ptr: i64) {
  entry:
    arm64.b __destruct_Point.done
  done:
    arm64.ret
  }
}
```

<!-- test: array-push-struct-incref -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value Integer
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	var arr = ItemArray{}
	var item = Item{value: 7}
	arr.push(item)
	var got = try arr.get(0) otherwise Item{value: 0}
	return got.value
end 'main'
```
```exitcode
7
```
```RequiredMLIR:x86_64-windows
=== maxon
module {
  func @Item.clone(self: Item) -> Item {
  entry:
    %0 = maxon.struct_param @Item
    %1 = maxon.field_access .value %0
    %2 = maxon.struct_literal @Item
    maxon.assign %2 {var = __retval_3} {decl = 1 : i1}
    maxon.scope_end [__retval_3]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.literal {value = 0 : i64}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.literal {value = 8 : i64}
    %14 = maxon.struct_literal @__ManagedMemory_Item
    %15 = maxon.struct_literal @ItemArray
    maxon.assign %15 {var = arr} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 7 : i64}
    %17 = maxon.struct_literal @Item
    maxon.assign %17 {var = item} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.struct_var_ref item
    maxon.call @ItemArray.push %15, %18
    %19 = maxon.struct_var_ref arr
    %20 = maxon.literal {value = 0 : i64}
    %23, %22 = maxon.try_call @ItemArray.get %19, %20
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %22, %26 {op = ne}
    maxon.cond_br %27 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %24 = maxon.literal {value = 0 : i64}
    %25 = maxon.struct_literal @Item
    maxon.assign %25 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_success_2:
    maxon.assign %23 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %28 = maxon.struct_var_ref __try_result_0
    maxon.assign %28 {var = got} {decl = 1 : i1} {mut = 1 : i1}
    %29 = maxon.struct_var_ref got
    %30 = maxon.field_access .value %29
    %31 = maxon.literal {value = 0 : i64}
    %32 = maxon.binop %30, %31 {op = lt}
    %33 = maxon.literal {value = 4294967295 : i64}
    %34 = maxon.binop %30, %33 {op = gt}
    %35 = maxon.binop %32, %34 {op = or}
    maxon.cond_br %35 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    maxon.panic "panic at array-push-struct-incref.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_4:
    maxon.scope_end [arr, item, got, __try_result_0]
    maxon.return %30
  }
}
=== standard
module {
  func @Item.clone(__self_ptr: i64) -> i64 {
  entry:
    %0 = func.param self : StdI64
    memref.store %0, self
    %1 = memref.load self : i64
    %2 = memref.load_indirect %1+0
    %3 = arith.constant {value = 8 : i64}
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.constant {value = 1 : i64}
    %6 = std.call_runtime @mm_alloc %3, %4, %5
    memref.store %6, __retval_3
    %7 = memref.load __retval_3 : i64
    memref.store_indirect %2, %7+0
    %8 = memref.load __retval_3 : i64
    std.call_runtime @mm_incref %8
    %9 = memref.load __retval_3 : i64
    func.return %9
  }
  func @main() -> u32 {
  entry:
    %85 = arith.constant {value = 0 : i64}
    memref.store %85, __try_result_0
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.constant {value = 8 : i64}
    %16 = arith.constant {value = 32 : i64}
    %17 = func.ref @__destruct___ManagedMemory_Item
    %18 = std.ptr_to_i64 %17
    %19 = arith.constant {value = 2 : i64}
    %20 = std.call_runtime @mm_alloc %16, %18, %19
    memref.store %20, __struct_14
    %21 = memref.load __struct_14 : i64
    memref.store_indirect %12, %21+0
    %22 = memref.load __struct_14 : i64
    memref.store_indirect %13, %22+8
    %23 = memref.load __struct_14 : i64
    memref.store_indirect %14, %23+16
    %24 = memref.load __struct_14 : i64
    memref.store_indirect %15, %24+24
    %25 = memref.load __struct_14 : i64
    %26 = memref.load_indirect %25+24
    %27 = arith.constant {value = 0 : i64}
    %28 = memref.lea_symdata __mm_panic_element_size_zero
    %29 = std.ptr_to_i64 %28
    std.call_runtime @maxon_bounds_check %27, %26, %29
    %30 = arith.constant {value = 16 : i64}
    %31 = func.ref @__destruct_ItemArray
    %32 = std.ptr_to_i64 %31
    %33 = arith.constant {value = 3 : i64}
    %34 = std.call_runtime @mm_alloc %30, %32, %33
    memref.store %34, arr
    %35 = memref.load arr : i64
    memref.store_indirect %11, %35+0
    %36 = memref.load __struct_14 : i64
    %37 = memref.load arr : i64
    memref.store_indirect %36, %37+8
    std.call_runtime @mm_incref %36
    %38 = memref.load arr : i64
    std.call_runtime @mm_incref %38
    %39 = arith.constant {value = 7 : i64}
    %40 = arith.constant {value = 8 : i64}
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.constant {value = 1 : i64}
    %43 = std.call_runtime @mm_alloc %40, %41, %42
    memref.store %43, item
    %44 = memref.load item : i64
    memref.store_indirect %39, %44+0
    %45 = memref.load item : i64
    std.call_runtime @mm_incref %45
    %46 = memref.load arr : i64
    %47 = memref.load item : i64
    func.call @ItemArray.push %46, %47
    %48 = arith.constant {value = 0 : i64}
    %49 = memref.load arr : i64
    %50, %51 = func.try_call @ItemArray.get %49, %48
    memref.store %50, __callret_23
    %52 = arith.constant {value = 0 : i64}
    %53 = arith.cmpi ne %51, %52
    cf.cond_br %53 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %54 = arith.constant {value = 0 : i64}
    %55 = arith.constant {value = 8 : i64}
    %56 = arith.constant {value = 0 : i64}
    %57 = arith.constant {value = 1 : i64}
    %58 = std.call_runtime @mm_alloc %55, %56, %57
    memref.store %58, __try_result_0
    %59 = memref.load __try_result_0 : i64
    memref.store_indirect %54, %59+0
    %60 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %60
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %61 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %61
    %62 = memref.load __callret_23 : i64
    memref.store %62, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %63 = memref.load __try_result_0 : i64
    memref.store %63, got
    %64 = memref.load got : i64
    std.call_runtime @mm_incref %64
    %65 = memref.load got : i64
    %66 = memref.load_indirect %65+0
    %67 = arith.constant {value = 0 : i64}
    %68 = arith.cmpi lt %66, %67
    %69 = arith.constant {value = 4294967295 : i64}
    %70 = arith.cmpi gt %66, %69
    %71 = arith.ori1 %68, %70
    cf.cond_br %71 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    %72 = memref.lea_symdata __panic_msg_0
    %73 = std.ptr_to_i64 %72
    std.call_runtime @maxon_panic %73
  __range_ok_4:
    %74 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %74
    %76 = memref.load got : i64
    std.call_runtime_if_nonnull @mm_decref %76
    %78 = memref.load item : i64
    std.call_runtime_if_nonnull @mm_decref %78
    %80 = memref.load arr : i64
    std.call_runtime_if_nonnull @mm_decref %80
    func.return %66
  }
  func @__destruct_Item(ptr: i64) {
  entry:
    %226 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory_Item(ptr: i64) {
  entry:
    %227 = func.param ptr : StdI64
    memref.store %227, __destr_ptr
    %230 = memref.load __destr_ptr : i64
    %231 = memref.load_indirect %230+16
    %232 = arith.constant {value = 0 : i64}
    %233 = arith.cmpi ne %231, %232
    cf.cond_br %233 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %234 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %234
    %235 = memref.load __destr_ptr : i64
    %236 = memref.load_indirect %235+0
    std.call_runtime @mm_raw_free %236
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_ItemArray(ptr: i64) {
  entry:
    %237 = func.param ptr : StdI64
    memref.store %237, __destr_ptr
    %238 = memref.load __destr_ptr : i64
    %239 = memref.load_indirect %238+8
    std.call_runtime_if_nonnull @mm_decref %239
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @Item.clone(__self_ptr: i64) -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rdx, [rax+0]
    x86.mov [rbp-24], rdx
    x86.mov rcx, 8
    x86.xor rdx, rdx
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-16], rax
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rbp-24]
    x86.mov [rcx+0], rdx
    x86.mov rbx, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.mov rax, [rbp-16]
    x86.epilogue
    x86.ret
  }
  func @main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor rax, rax
    x86.mov [rbp-8], rax
    x86.xor rcx, rcx
    x86.xor rdx, rdx
    x86.xor rbx, rbx
    x86.xor rsi, rsi
    x86.mov rdi, 8
    x86.lea_func r8, [__destruct___ManagedMemory_Item]
    x86.mov r9, r8
    x86.mov rdx, r9
    x86.mov rcx, 32
    x86.mov r8, 2
    x86.call mm_alloc
    x86.mov [rbp-16], rax
    x86.mov rax, [rbp-16]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-16]
    x86.xor rcx, rcx
    x86.mov [rax+8], rcx
    x86.mov rax, [rbp-16]
    x86.xor rcx, rcx
    x86.mov [rax+16], rcx
    x86.mov rax, [rbp-16]
    x86.mov rcx, 8
    x86.mov [rax+24], rcx
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rax+24]
    x86.lea_symdata rax, [__mm_panic_element_size_zero]
    x86.mov rdx, rax
    x86.mov r8, rdx
    x86.mov rdx, rcx
    x86.xor rcx, rcx
    x86.call maxon_bounds_check
    x86.lea_func rax, [__destruct_ItemArray]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-24], rax
    x86.mov rax, [rbp-24]
    x86.xor rcx, rcx
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-16]
    x86.mov rcx, [rbp-24]
    x86.mov [rcx+8], rax
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov rax, 7
    x86.mov rcx, 8
    x86.xor rdx, rdx
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-32], rax
    x86.mov rax, [rbp-32]
    x86.mov rcx, 7
    x86.mov [rax+0], rcx
    x86.mov rax, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-32]
    x86.mov rcx, [rbp-24]
    x86.mov rdx, [rbp-32]
    x86.call ItemArray.push
    x86.mov rax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.xor rdx, rdx
    x86.call ItemArray.get
    x86.mov [rbp-40], rax
    x86.xor rax, rax
    x86.cmp rdx, rax
    x86.je main.otherwise_default_success_2
  otherwise_default_error_1:
    x86.xor rax, rax
    x86.mov rcx, 8
    x86.xor rdx, rdx
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-8], rax
    x86.mov rcx, [rbp-8]
    x86.xor rdx, rdx
    x86.mov [rcx+0], rdx
    x86.mov rbx, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_incref
    x86.jmp main.otherwise_default_continue_3
  otherwise_default_success_2:
    x86.mov rax, [rbp-8]
    x86.test rax, rax
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-8]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov rcx, [rbp-40]
    x86.mov [rbp-8], rcx
    x86.jmp main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov rax, [rbp-8]
    x86.mov [rbp-48], rax
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
    x86.mov rdx, [rbp-48]
    x86.mov rbx, [rdx+0]
    x86.xor rsi, rsi
    x86.cmp rbx, rsi
    x86.setl rdi
    x86.movzx rdi, rdib
    x86.mov r8, 4294967295
    x86.cmp rbx, r8
    x86.setg r9
    x86.movzx r9, r9b
    x86.or rdi, r9
    x86.test rdi, rdi
    x86.je main.__range_ok_4
  __range_panic_4:
    x86.lea_symdata rax, [__panic_msg_0]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_4:
    x86.mov rax, [rbp-8]
    x86.mov [rbp-56], rbx
    x86.test rax, rax
    x86.jz __nonnull_skip_1
    x86.mov rcx, [rbp-8]
    x86.call mm_decref
    x86.label __nonnull_skip_1
    x86.mov rcx, [rbp-48]
    x86.test rcx, rcx
    x86.jz __nonnull_skip_2
    x86.call mm_decref
    x86.label __nonnull_skip_2
    x86.mov rdx, [rbp-32]
    x86.test rdx, rdx
    x86.jz __nonnull_skip_3
    x86.mov rcx, [rbp-32]
    x86.call mm_decref
    x86.label __nonnull_skip_3
    x86.mov rbx, [rbp-24]
    x86.test rbx, rbx
    x86.jz __nonnull_skip_4
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_4
    x86.mov rax, [rbp-56]
    x86.epilogue
    x86.ret
  }
  func @__destruct_Item(ptr: i64) {
  entry:
    x86.jmp __destruct_Item.done
  done:
    x86.ret
  }
  func @__destruct___ManagedMemory_Item(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+16]
    x86.xor rdx, rdx
    x86.cmp rcx, rdx
    x86.je __destruct___ManagedMemory_Item.skip_buf_0
  free_buf_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_managed_elements
    x86.mov rcx, [rbp-8]
    x86.mov rdx, [rcx+0]
    x86.mov rcx, rdx
    x86.call mm_raw_free
    x86.jmp __destruct___ManagedMemory_Item.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct___ManagedMemory_Item.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_ItemArray(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], rcx
    x86.mov rax, [rbp-8]
    x86.mov rcx, [rax+8]
    x86.mov [rbp-16], rcx
    x86.test rcx, rcx
    x86.jz __nonnull_skip_6
    x86.call mm_decref
    x86.label __nonnull_skip_6
    x86.jmp __destruct_ItemArray.done
  done:
    x86.epilogue
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
=== maxon
module {
  func @Item.clone(self: Item) -> Item {
  entry:
    %0 = maxon.struct_param @Item
    %1 = maxon.field_access .value %0
    %2 = maxon.struct_literal @Item
    maxon.assign %2 {var = __retval_3} {decl = 1 : i1}
    maxon.scope_end [__retval_3]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.literal {value = 0 : i64}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.literal {value = 8 : i64}
    %14 = maxon.struct_literal @__ManagedMemory_Item
    %15 = maxon.struct_literal @ItemArray
    maxon.assign %15 {var = arr} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.literal {value = 7 : i64}
    %17 = maxon.struct_literal @Item
    maxon.assign %17 {var = item} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.struct_var_ref item
    maxon.call @ItemArray.push %15, %18
    %19 = maxon.struct_var_ref arr
    %20 = maxon.literal {value = 0 : i64}
    %23, %22 = maxon.try_call @ItemArray.get %19, %20
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %22, %26 {op = ne}
    maxon.cond_br %27 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %24 = maxon.literal {value = 0 : i64}
    %25 = maxon.struct_literal @Item
    maxon.assign %25 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_success_2:
    maxon.assign %23 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %28 = maxon.struct_var_ref __try_result_0
    maxon.assign %28 {var = got} {decl = 1 : i1} {mut = 1 : i1}
    %29 = maxon.struct_var_ref got
    %30 = maxon.field_access .value %29
    %31 = maxon.literal {value = 0 : i64}
    %32 = maxon.binop %30, %31 {op = lt}
    %33 = maxon.literal {value = 4294967295 : i64}
    %34 = maxon.binop %30, %33 {op = gt}
    %35 = maxon.binop %32, %34 {op = or}
    maxon.cond_br %35 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    maxon.panic "panic at array-push-struct-incref.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_4:
    maxon.scope_end [arr, item, got, __try_result_0]
    maxon.return %30
  }
}
=== standard
module {
  func @Item.clone(__self_ptr: i64) -> i64 {
  entry:
    %0 = func.param self : StdI64
    memref.store %0, self
    %1 = memref.load self : i64
    %2 = memref.load_indirect %1+0
    %3 = arith.constant {value = 8 : i64}
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.constant {value = 1 : i64}
    %6 = std.call_runtime @mm_alloc %3, %4, %5
    memref.store %6, __retval_3
    %7 = memref.load __retval_3 : i64
    memref.store_indirect %2, %7+0
    %8 = memref.load __retval_3 : i64
    std.call_runtime @mm_incref %8
    %9 = memref.load __retval_3 : i64
    func.return %9
  }
  func @main() -> u32 {
  entry:
    %85 = arith.constant {value = 0 : i64}
    memref.store %85, __try_result_0
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.constant {value = 8 : i64}
    %16 = arith.constant {value = 32 : i64}
    %17 = func.ref @__destruct___ManagedMemory_Item
    %18 = std.ptr_to_i64 %17
    %19 = arith.constant {value = 2 : i64}
    %20 = std.call_runtime @mm_alloc %16, %18, %19
    memref.store %20, __struct_14
    %21 = memref.load __struct_14 : i64
    memref.store_indirect %12, %21+0
    %22 = memref.load __struct_14 : i64
    memref.store_indirect %13, %22+8
    %23 = memref.load __struct_14 : i64
    memref.store_indirect %14, %23+16
    %24 = memref.load __struct_14 : i64
    memref.store_indirect %15, %24+24
    %25 = memref.load __struct_14 : i64
    %26 = memref.load_indirect %25+24
    %27 = arith.constant {value = 0 : i64}
    %28 = memref.lea_symdata __mm_panic_element_size_zero
    %29 = std.ptr_to_i64 %28
    std.call_runtime @maxon_bounds_check %27, %26, %29
    %30 = arith.constant {value = 16 : i64}
    %31 = func.ref @__destruct_ItemArray
    %32 = std.ptr_to_i64 %31
    %33 = arith.constant {value = 3 : i64}
    %34 = std.call_runtime @mm_alloc %30, %32, %33
    memref.store %34, arr
    %35 = memref.load arr : i64
    memref.store_indirect %11, %35+0
    %36 = memref.load __struct_14 : i64
    %37 = memref.load arr : i64
    memref.store_indirect %36, %37+8
    std.call_runtime @mm_incref %36
    %38 = memref.load arr : i64
    std.call_runtime @mm_incref %38
    %39 = arith.constant {value = 7 : i64}
    %40 = arith.constant {value = 8 : i64}
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.constant {value = 1 : i64}
    %43 = std.call_runtime @mm_alloc %40, %41, %42
    memref.store %43, item
    %44 = memref.load item : i64
    memref.store_indirect %39, %44+0
    %45 = memref.load item : i64
    std.call_runtime @mm_incref %45
    %46 = memref.load arr : i64
    %47 = memref.load item : i64
    func.call @ItemArray.push %46, %47
    %48 = arith.constant {value = 0 : i64}
    %49 = memref.load arr : i64
    %50, %51 = func.try_call @ItemArray.get %49, %48
    memref.store %50, __callret_23
    %52 = arith.constant {value = 0 : i64}
    %53 = arith.cmpi ne %51, %52
    cf.cond_br %53 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %54 = arith.constant {value = 0 : i64}
    %55 = arith.constant {value = 8 : i64}
    %56 = arith.constant {value = 0 : i64}
    %57 = arith.constant {value = 1 : i64}
    %58 = std.call_runtime @mm_alloc %55, %56, %57
    memref.store %58, __try_result_0
    %59 = memref.load __try_result_0 : i64
    memref.store_indirect %54, %59+0
    %60 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %60
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %61 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %61
    %62 = memref.load __callret_23 : i64
    memref.store %62, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %63 = memref.load __try_result_0 : i64
    memref.store %63, got
    %64 = memref.load got : i64
    std.call_runtime @mm_incref %64
    %65 = memref.load got : i64
    %66 = memref.load_indirect %65+0
    %67 = arith.constant {value = 0 : i64}
    %68 = arith.cmpi lt %66, %67
    %69 = arith.constant {value = 4294967295 : i64}
    %70 = arith.cmpi gt %66, %69
    %71 = arith.ori1 %68, %70
    cf.cond_br %71 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    %72 = memref.lea_symdata __panic_msg_0
    %73 = std.ptr_to_i64 %72
    std.call_runtime @maxon_panic %73
  __range_ok_4:
    %74 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %74
    %76 = memref.load got : i64
    std.call_runtime_if_nonnull @mm_decref %76
    %78 = memref.load item : i64
    std.call_runtime_if_nonnull @mm_decref %78
    %80 = memref.load arr : i64
    std.call_runtime_if_nonnull @mm_decref %80
    func.return %66
  }
  func @__destruct_Item(ptr: i64) {
  entry:
    %226 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory_Item(ptr: i64) {
  entry:
    %227 = func.param ptr : StdI64
    memref.store %227, __destr_ptr
    %230 = memref.load __destr_ptr : i64
    %231 = memref.load_indirect %230+16
    %232 = arith.constant {value = 0 : i64}
    %233 = arith.cmpi ne %231, %232
    cf.cond_br %233 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %234 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %234
    %235 = memref.load __destr_ptr : i64
    %236 = memref.load_indirect %235+0
    std.call_runtime @mm_raw_free %236
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_ItemArray(ptr: i64) {
  entry:
    %237 = func.param ptr : StdI64
    memref.store %237, __destr_ptr
    %238 = memref.load __destr_ptr : i64
    %239 = memref.load_indirect %238+8
    std.call_runtime_if_nonnull @mm_decref %239
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @Item.clone(__self_ptr: i64) -> i64 {
  entry:
    arm64.prologue stack_size=64
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.str x1, [x29, #-24]
    arm64.mov x0, #8
    arm64.mov x1, #0
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-16]
    arm64.ldr x2, [x29, #-16]
    arm64.ldr x3, [x29, #-24]
    arm64.str x3, [x2, #0]
    arm64.ldr x4, [x29, #-16]
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_incref
    arm64.ldr x5, [x29, #-16]
    arm64.mov x0, x5
    arm64.epilogue stack_size=64
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=128
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.mov x2, #0
    arm64.mov x3, #0
    arm64.mov x4, #0
    arm64.mov x5, #8
    arm64.adrp_add_func x6, __destruct___ManagedMemory_Item
    arm64.mov x7, x6
    arm64.mov x1, x7
    arm64.mov x0, #32
    arm64.mov x2, #2
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-16]
    arm64.ldr x8, [x29, #-16]
    arm64.mov x9, #0
    arm64.str x9, [x8, #0]
    arm64.ldr x10, [x29, #-16]
    arm64.mov x11, #0
    arm64.str x11, [x10, #8]
    arm64.ldr x12, [x29, #-16]
    arm64.mov x13, #0
    arm64.str x13, [x12, #16]
    arm64.ldr x14, [x29, #-16]
    arm64.mov x15, #8
    arm64.str x15, [x14, #24]
    arm64.ldr x0, [x29, #-16]
    arm64.ldr x1, [x0, #24]
    arm64.adrp_add_symdata x0, __mm_panic_element_size_zero
    arm64.mov x2, x0
    arm64.mov x0, #0
    arm64.bl maxon_bounds_check
    arm64.adrp_add_func x0, __destruct_ItemArray
    arm64.mov x1, x0
    arm64.mov x0, #16
    arm64.mov x2, #3
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-24]
    arm64.ldr x0, [x29, #-24]
    arm64.mov x1, #0
    arm64.str x1, [x0, #0]
    arm64.ldr x0, [x29, #-16]
    arm64.ldr x1, [x29, #-24]
    arm64.str x0, [x1, #8]
    arm64.bl mm_incref
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_incref
    arm64.mov x0, #7
    arm64.mov x0, #8
    arm64.mov x1, #0
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-32]
    arm64.ldr x0, [x29, #-32]
    arm64.mov x1, #7
    arm64.str x1, [x0, #0]
    arm64.ldr x0, [x29, #-32]
    arm64.bl mm_incref
    arm64.ldr x0, [x29, #-24]
    arm64.ldr x1, [x29, #-32]
    arm64.bl ItemArray.push
    arm64.ldr x0, [x29, #-24]
    arm64.mov x1, #0
    arm64.bl ItemArray.get
    arm64.str x0, [x29, #-40]
    arm64.mov x0, #0
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne main.otherwise_default_error_1
    arm64.b main.otherwise_default_success_2
  otherwise_default_error_1:
    arm64.mov x0, #0
    arm64.mov x0, #8
    arm64.mov x1, #0
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-8]
    arm64.ldr x1, [x29, #-8]
    arm64.mov x2, #0
    arm64.str x2, [x1, #0]
    arm64.ldr x3, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_incref
    arm64.b main.otherwise_default_continue_3
  otherwise_default_success_2:
    arm64.ldr x0, [x29, #-8]
    arm64.cmp x0, #0
    arm64.b.eq main.__skip_guarded_73
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_73
    arm64.ldr x1, [x29, #-40]
    arm64.str x1, [x29, #-8]
    arm64.b main.otherwise_default_continue_3
  otherwise_default_continue_3:
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-48]
    arm64.ldr x1, [x29, #-48]
    arm64.ldr x0, [x29, #-48]
    arm64.bl mm_incref
    arm64.ldr x2, [x29, #-48]
    arm64.ldr x3, [x2, #0]
    arm64.mov x4, #0
    arm64.cmp x3, x4
    arm64.cset x5, lt
    arm64.mov x6, #4294967295
    arm64.cmp x3, x6
    arm64.cset x7, gt
    arm64.orr x8, x5, x7
    arm64.cmp x8, #0
    arm64.b.ne main.__range_panic_4
    arm64.b main.__range_ok_4
  __range_panic_4:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_4:
    arm64.ldr x0, [x29, #-8]
    arm64.str x3, [x29, #-56]
    arm64.cmp x0, #0
    arm64.b.eq main.__skip_guarded_93
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_93
    arm64.ldr x1, [x29, #-48]
    arm64.cmp x1, #0
    arm64.b.eq main.__skip_guarded_95
    arm64.ldr x0, [x29, #-48]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_95
    arm64.ldr x2, [x29, #-32]
    arm64.cmp x2, #0
    arm64.b.eq main.__skip_guarded_97
    arm64.ldr x0, [x29, #-32]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_97
    arm64.ldr x3, [x29, #-24]
    arm64.cmp x3, #0
    arm64.b.eq main.__skip_guarded_99
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_99
    arm64.ldr x0, [x29, #-56]
    arm64.epilogue stack_size=128
    arm64.ret
  }
  func @__destruct_Item(ptr: i64) {
  entry:
    arm64.b __destruct_Item.done
  done:
    arm64.ret
  }
  func @__destruct___ManagedMemory_Item(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #0
    arm64.cmp x1, x2
    arm64.cset x3, ne
    arm64.cmp x3, #0
    arm64.b.ne __destruct___ManagedMemory_Item.free_buf_0
    arm64.b __destruct___ManagedMemory_Item.skip_buf_0
  free_buf_0:
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_decref_managed_elements
    arm64.ldr x1, [x29, #-8]
    arm64.ldr x2, [x1, #0]
    arm64.mov x0, x2
    arm64.bl mm_raw_free
    arm64.b __destruct___ManagedMemory_Item.skip_buf_0
  skip_buf_0:
    arm64.b __destruct___ManagedMemory_Item.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct_ItemArray(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #8]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_ItemArray.__skip_guarded_4
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_ItemArray.__skip_guarded_4
    arm64.b __destruct_ItemArray.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: release-before-break -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n Integer
end 'Counter'

function main() returns ExitCode
	var result = 0
	var i = 0
	while i < 3 'loop'
		var c = Counter{n: i}
		if c.n == 1 'check'
			result = c.n
			break
		end 'check'
		i = i + 1
	end 'loop'
	return result
end 'main'
```
```exitcode
1
```
```RequiredMLIR:x86_64-windows
=== maxon
module {
  func @main() -> i64 {
  entry:
    %9 = maxon.literal {value = 0 : i64}
    maxon.assign %9 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 0 : i64}
    maxon.assign %10 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %11 = maxon.literal {value = 3 : i64}
    %12 = maxon.var_ref {var = i} {type = i64}
    %13 = maxon.binop %12, %11 {op = lt}
    maxon.cond_br %13 [then: loop_0, else: loop_0.exit]
  loop_0:
    %14 = maxon.var_ref {var = i} {type = i64}
    %15 = maxon.struct_literal @Counter
    maxon.assign %15 {var = c} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.struct_var_ref c
    %17 = maxon.field_access .n %16
    %18 = maxon.literal {value = 1 : i64}
    %19 = maxon.binop %17, %18 {op = eq}
    maxon.cond_br %19 [then: check_1, else: check_1.after]
  check_1:
    %20 = maxon.struct_var_ref c
    %21 = maxon.field_access .n %20
    maxon.assign %21 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [c]
    maxon.br loop_0.exit
  check_1.after:
    %22 = maxon.literal {value = 1 : i64}
    %23 = maxon.var_ref {var = i} {type = i64}
    %24 = maxon.binop %23, %22 {op = add}
    maxon.assign %24 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [c]
    maxon.br loop_0.header
  loop_0.exit:
    %25 = maxon.var_ref {var = result} {type = i64}
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %25, %26 {op = lt}
    %28 = maxon.literal {value = 4294967295 : i64}
    %29 = maxon.binop %25, %28 {op = gt}
    %30 = maxon.binop %27, %29 {op = or}
    maxon.cond_br %30 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at release-before-break.test:19: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    maxon.scope_end [result, i]
    maxon.return %25
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, result
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, i
    cf.br loop_0.header
  loop_0.header:
    %2 = arith.constant {value = 3 : i64}
    %3 = memref.load i : i64
    %4 = arith.cmpi lt %3, %2
    cf.cond_br %4 [then: loop_0, else: loop_0.exit]
  loop_0:
    %5 = memref.load i : i64
    memref.bulk_zero __stk_c, 1
    memref.store %5, __stk_c.0
    %6 = memref.load __stk_c.0 : i64
    %7 = arith.constant {value = 1 : i64}
    %8 = arith.cmpi eq %6, %7
    cf.cond_br %8 [then: check_1, else: check_1.after]
  check_1:
    %9 = memref.load __stk_c.0 : i64
    memref.store %9, result
    cf.br loop_0.exit
  check_1.after:
    %10 = arith.constant {value = 1 : i64}
    %11 = memref.load i : i64
    %12 = arith.addi %11, %10
    memref.store %12, i
    cf.br loop_0.header
  loop_0.exit:
    %13 = memref.load result : i64
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @maxon_panic %20
  __range_ok_2:
    func.return %13
  }
}
=== x86
module {
  func @main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor rax, rax
    x86.mov [rbp-8], rax
    x86.xor rcx, rcx
    x86.mov [rbp-16], rcx
    x86.jmp main.loop_0.header
  loop_0.header:
    x86.mov rax, 3
    x86.mov rcx, [rbp-16]
    x86.cmp rcx, rax
    x86.jge main.loop_0.exit
  loop_0:
    x86.mov rax, [rbp-16]
    x86.mov [rbp-24], rax
    x86.mov rcx, [rbp-24]
    x86.mov rdx, 1
    x86.cmp rcx, rdx
    x86.jne main.check_1.after
  check_1:
    x86.mov rax, [rbp-24]
    x86.mov [rbp-8], rax
    x86.jmp main.loop_0.exit
  check_1.after:
    x86.mov rax, 1
    x86.mov rcx, [rbp-16]
    x86.add rcx, rax
    x86.mov [rbp-16], rcx
    x86.jmp main.loop_0.header
  loop_0.exit:
    x86.mov rax, [rbp-8]
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.setl rdx
    x86.movzx rdx, rdxb
    x86.mov rbx, 4294967295
    x86.cmp rax, rbx
    x86.setg rsi
    x86.movzx rsi, rsib
    x86.or rdx, rsi
    x86.test rdx, rdx
    x86.je main.__range_ok_2
  __range_panic_2:
    x86.lea_symdata rax, [__panic_msg_0]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_2:
    x86.epilogue
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
=== maxon
module {
  func @main() -> i64 {
  entry:
    %9 = maxon.literal {value = 0 : i64}
    maxon.assign %9 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.literal {value = 0 : i64}
    maxon.assign %10 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %11 = maxon.literal {value = 3 : i64}
    %12 = maxon.var_ref {var = i} {type = i64}
    %13 = maxon.binop %12, %11 {op = lt}
    maxon.cond_br %13 [then: loop_0, else: loop_0.exit]
  loop_0:
    %14 = maxon.var_ref {var = i} {type = i64}
    %15 = maxon.struct_literal @Counter
    maxon.assign %15 {var = c} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.struct_var_ref c
    %17 = maxon.field_access .n %16
    %18 = maxon.literal {value = 1 : i64}
    %19 = maxon.binop %17, %18 {op = eq}
    maxon.cond_br %19 [then: check_1, else: check_1.after]
  check_1:
    %20 = maxon.struct_var_ref c
    %21 = maxon.field_access .n %20
    maxon.assign %21 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [c]
    maxon.br loop_0.exit
  check_1.after:
    %22 = maxon.literal {value = 1 : i64}
    %23 = maxon.var_ref {var = i} {type = i64}
    %24 = maxon.binop %23, %22 {op = add}
    maxon.assign %24 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [c]
    maxon.br loop_0.header
  loop_0.exit:
    %25 = maxon.var_ref {var = result} {type = i64}
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %25, %26 {op = lt}
    %28 = maxon.literal {value = 4294967295 : i64}
    %29 = maxon.binop %25, %28 {op = gt}
    %30 = maxon.binop %27, %29 {op = or}
    maxon.cond_br %30 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at release-before-break.test:19: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    maxon.scope_end [result, i]
    maxon.return %25
  }
}
=== standard
module {
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, result
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, i
    cf.br loop_0.header
  loop_0.header:
    %2 = arith.constant {value = 3 : i64}
    %3 = memref.load i : i64
    %4 = arith.cmpi lt %3, %2
    cf.cond_br %4 [then: loop_0, else: loop_0.exit]
  loop_0:
    %5 = memref.load i : i64
    memref.bulk_zero __stk_c, 1
    memref.store %5, __stk_c.0
    %6 = memref.load __stk_c.0 : i64
    %7 = arith.constant {value = 1 : i64}
    %8 = arith.cmpi eq %6, %7
    cf.cond_br %8 [then: check_1, else: check_1.after]
  check_1:
    %9 = memref.load __stk_c.0 : i64
    memref.store %9, result
    cf.br loop_0.exit
  check_1.after:
    %10 = arith.constant {value = 1 : i64}
    %11 = memref.load i : i64
    %12 = arith.addi %11, %10
    memref.store %12, i
    cf.br loop_0.header
  loop_0.exit:
    %13 = memref.load result : i64
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @maxon_panic %20
  __range_ok_2:
    func.return %13
  }
}
=== arm64
module {
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=80
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-16]
    arm64.b main.loop_0.header
  loop_0.header:
    arm64.mov x0, #3
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.loop_0
    arm64.b main.loop_0.exit
  loop_0:
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-24]
    arm64.str x0, [x29, #-24]
    arm64.ldr x2, [x29, #-24]
    arm64.mov x3, #1
    arm64.cmp x2, x3
    arm64.cset x4, eq
    arm64.cmp x4, #0
    arm64.b.ne main.check_1
    arm64.b main.check_1.after
  check_1:
    arm64.ldr x0, [x29, #-24]
    arm64.str x0, [x29, #-8]
    arm64.b main.loop_0.exit
  check_1.after:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-16]
    arm64.b main.loop_0.header
  loop_0.exit:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #4294967295
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_2
    arm64.b main.__range_ok_2
  __range_panic_2:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_2:
    arm64.epilogue stack_size=80
    arm64.ret
  }
}
```

<!-- test: release-before-return-in-block -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper
	export var val Integer
end 'Wrapper'

function compute(flag Integer) returns Integer
	if flag > 0 'check'
		@heap var w = Wrapper{val: flag}
		return w.val + 1
	end 'check'
	return 0
end 'compute'

function main() returns ExitCode
	return compute(flag: 5)
end 'main'
```
```exitcode
6
```
```RequiredMLIR:x86_64-windows
=== maxon
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    %9 = maxon.param {index = 0 : i32} {name = flag} {type = i64}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %9, %10 {op = gt} {optimalType = i64}
    maxon.cond_br %11 [then: check_0, else: check_0.after]
  check_0:
    %12 = maxon.var_ref {var = flag} {type = i64}
    %13 = maxon.struct_literal @Wrapper
    maxon.assign %13 {var = w} {decl = 1 : i1} {mut = 1 : i1}
    %14 = maxon.struct_var_ref w
    %15 = maxon.field_access .val %14
    %16 = maxon.literal {value = 1 : i64}
    %17 = maxon.binop %15, %16 {op = add}
    maxon.scope_end [flag, w]
    maxon.return %17
  check_0.after:
    %18 = maxon.literal {value = 0 : i64}
    maxon.scope_end [flag]
    maxon.return %18
  }
  func @main() -> i64 {
  entry:
    %19 = maxon.literal {value = 5 : i64}
    %20 = maxon.call @memory-safety.compute %19
    %21 = maxon.literal {value = 0 : i64}
    %22 = maxon.binop %20, %21 {op = lt}
    %23 = maxon.literal {value = 4294967295 : i64}
    %24 = maxon.binop %20, %23 {op = gt}
    %25 = maxon.binop %22, %24 {op = or}
    maxon.cond_br %25 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at release-before-return-in-block.test:17: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %20
  }
}
=== standard
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    %0 = func.param flag : StdI64
    memref.store %0, flag
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi gt %0, %1
    cf.cond_br %2 [then: check_0, else: check_0.after]
  check_0:
    %3 = memref.load flag : i64
    %4 = arith.constant {value = 8 : i64}
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.constant {value = 1 : i64}
    %7 = std.call_runtime @mm_alloc %4, %5, %6
    memref.store %7, w
    %8 = memref.load w : i64
    memref.store_indirect %3, %8+0
    %9 = memref.load w : i64
    std.call_runtime @mm_incref %9
    %10 = memref.load w : i64
    %11 = memref.load_indirect %10+0
    %12 = arith.constant {value = 1 : i64}
    %13 = arith.addi %11, %12
    %14 = memref.load w : i64
    std.call_runtime_if_nonnull @mm_decref %14
    func.return %13
  check_0.after:
    %16 = arith.constant {value = 0 : i64}
    func.return %16
  }
  func @main() -> u32 {
  entry:
    %18 = arith.constant {value = 5 : i64}
    %19 = func.call @memory-safety.compute %18
    %20 = arith.constant {value = 0 : i64}
    %21 = arith.cmpi lt %19, %20
    %22 = arith.constant {value = 4294967295 : i64}
    %23 = arith.cmpi gt %19, %22
    %24 = arith.ori1 %21, %23
    cf.cond_br %24 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %25 = memref.lea_symdata __panic_msg_0
    %26 = std.ptr_to_i64 %25
    std.call_runtime @maxon_panic %26
  __range_ok_0:
    func.return %19
  }
  func @__destruct_Wrapper(ptr: i64) {
  entry:
    %27 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-8], rcx
    x86.xor rax, rax
    x86.cmp rcx, rax
    x86.jle memory-safety.compute.check_0.after
  check_0:
    x86.mov rax, [rbp-8]
    x86.mov rcx, 8
    x86.xor rdx, rdx
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-16], rax
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rbp-8]
    x86.mov [rcx+0], rdx
    x86.mov rbx, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.mov rsi, [rbp-16]
    x86.mov rdi, [rsi+0]
    x86.mov r8, 1
    x86.add rdi, r8
    x86.mov r9, [rbp-16]
    x86.mov [rbp-24], rdi
    x86.test r9, r9
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov rax, [rbp-24]
    x86.epilogue
    x86.ret
  check_0.after:
    x86.xor rax, rax
    x86.epilogue
    x86.ret
  }
  func @main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov rcx, 5
    x86.call memory-safety.compute
    x86.xor rcx, rcx
    x86.cmp rax, rcx
    x86.setl rcx
    x86.movzx rcx, rcxb
    x86.mov rdx, 4294967295
    x86.cmp rax, rdx
    x86.setg rdx
    x86.movzx rdx, rdxb
    x86.or rcx, rdx
    x86.test rcx, rcx
    x86.je main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_0]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.epilogue
    x86.ret
  }
  func @__destruct_Wrapper(ptr: i64) {
  entry:
    x86.jmp __destruct_Wrapper.done
  done:
    x86.ret
  }
}
```
```RequiredMLIR:aarch64-macos
=== maxon
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    %9 = maxon.param {index = 0 : i32} {name = flag} {type = i64}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %9, %10 {op = gt} {optimalType = i64}
    maxon.cond_br %11 [then: check_0, else: check_0.after]
  check_0:
    %12 = maxon.var_ref {var = flag} {type = i64}
    %13 = maxon.struct_literal @Wrapper
    maxon.assign %13 {var = w} {decl = 1 : i1} {mut = 1 : i1}
    %14 = maxon.struct_var_ref w
    %15 = maxon.field_access .val %14
    %16 = maxon.literal {value = 1 : i64}
    %17 = maxon.binop %15, %16 {op = add}
    maxon.scope_end [flag, w]
    maxon.return %17
  check_0.after:
    %18 = maxon.literal {value = 0 : i64}
    maxon.scope_end [flag]
    maxon.return %18
  }
  func @main() -> i64 {
  entry:
    %19 = maxon.literal {value = 5 : i64}
    %20 = maxon.call @memory-safety.compute %19
    %21 = maxon.literal {value = 0 : i64}
    %22 = maxon.binop %20, %21 {op = lt}
    %23 = maxon.literal {value = 4294967295 : i64}
    %24 = maxon.binop %20, %23 {op = gt}
    %25 = maxon.binop %22, %24 {op = or}
    maxon.cond_br %25 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at release-before-return-in-block.test:17: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %20
  }
}
=== standard
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    %0 = func.param flag : StdI64
    memref.store %0, flag
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi gt %0, %1
    cf.cond_br %2 [then: check_0, else: check_0.after]
  check_0:
    %3 = memref.load flag : i64
    %4 = arith.constant {value = 8 : i64}
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.constant {value = 1 : i64}
    %7 = std.call_runtime @mm_alloc %4, %5, %6
    memref.store %7, w
    %8 = memref.load w : i64
    memref.store_indirect %3, %8+0
    %9 = memref.load w : i64
    std.call_runtime @mm_incref %9
    %10 = memref.load w : i64
    %11 = memref.load_indirect %10+0
    %12 = arith.constant {value = 1 : i64}
    %13 = arith.addi %11, %12
    %14 = memref.load w : i64
    std.call_runtime_if_nonnull @mm_decref %14
    func.return %13
  check_0.after:
    %16 = arith.constant {value = 0 : i64}
    func.return %16
  }
  func @main() -> u32 {
  entry:
    %18 = arith.constant {value = 5 : i64}
    %19 = func.call @memory-safety.compute %18
    %20 = arith.constant {value = 0 : i64}
    %21 = arith.cmpi lt %19, %20
    %22 = arith.constant {value = 4294967295 : i64}
    %23 = arith.cmpi gt %19, %22
    %24 = arith.ori1 %21, %23
    cf.cond_br %24 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %25 = memref.lea_symdata __panic_msg_0
    %26 = std.ptr_to_i64 %25
    std.call_runtime @maxon_panic %26
  __range_ok_0:
    func.return %19
  }
  func @__destruct_Wrapper(ptr: i64) {
  entry:
    %27 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    arm64.prologue stack_size=64
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, gt
    arm64.cmp x2, #0
    arm64.b.ne memory-safety.compute.check_0
    arm64.b memory-safety.compute.check_0.after
  check_0:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x0, #8
    arm64.mov x1, #0
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-16]
    arm64.ldr x1, [x29, #-16]
    arm64.ldr x2, [x29, #-8]
    arm64.str x2, [x1, #0]
    arm64.ldr x3, [x29, #-16]
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_incref
    arm64.ldr x4, [x29, #-16]
    arm64.ldr x5, [x4, #0]
    arm64.mov x6, #1
    arm64.add x7, x5, x6
    arm64.ldr x8, [x29, #-16]
    arm64.str x7, [x29, #-24]
    arm64.cmp x8, #0
    arm64.b.eq memory-safety.compute.__skip_guarded_20
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label memory-safety.compute.__skip_guarded_20
    arm64.ldr x0, [x29, #-24]
    arm64.epilogue stack_size=64
    arm64.ret
  check_0.after:
    arm64.mov x0, #0
    arm64.epilogue stack_size=64
    arm64.ret
  }
  func @main() -> u32 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #5
    arm64.bl memory-safety.compute
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #4294967295
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl maxon_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
  func @__destruct_Wrapper(ptr: i64) {
  entry:
    arm64.b __destruct_Wrapper.done
  done:
    arm64.ret
  }
}
```

### Continue cleans up loop body scope

When `continue` is used inside a loop that allocates structs, the loop body scope
must be exited before jumping back to the header. Otherwise the struct allocated in
that iteration leaks.

<!-- test: release-before-continue -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n Integer
end 'Counter'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 5 'loop'
		i = i + 1
		var c = Counter{n: i}
		if c.n == 3 'skip'
			continue
		end 'skip'
		total = total + c.n
	end 'loop'
	return total
end 'main'
```
```exitcode
12
```

### Labeled break from nested loop cleans up both scopes

When breaking out of an outer loop from inside an inner loop, both the inner
loop body scope and the outer loop body scope must be cleaned up.

<!-- test: release-labeled-break-nested -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var a Integer
	export var b Integer
end 'Pair'

function main() returns ExitCode
	var result = 0
	var i = 0
	while i < 3 'outer'
		var p = Pair{a: i, b: i * 10}
		var j = 0
		while j < 3 'inner'
			var q = Pair{a: j, b: j * 10}
			if p.a == 1 'check'
				if q.a == 2 'found'
					result = p.b + q.b
					break 'outer'
				end 'found'
			end 'check'
			j = j + 1
		end 'inner'
		i = i + 1
	end 'outer'
	return result
end 'main'
```
```exitcode
30
```

### Break from for-in loop cleans up loop scope

For-in loops use the same scope mechanism as while loops. Breaking out of a
for-in loop with struct allocations must clean up the loop body scope.

<!-- test: release-break-for-in -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var val Integer
end 'Item'

function main() returns ExitCode
	var items = [10, 20, 30, 40, 50]
	var result = 0
	for item in items 'search'
		var wrapped = Item{val: item}
		if wrapped.val == 30 'found'
			result = wrapped.val
			break
		end 'found'
	end 'search'
	return result
end 'main'
```
```exitcode
30
```

### Error propagation cleans up function scope

When a `try` call propagates an error to the caller, the function's scope must
be exited so that any allocations made before the try call are freed.

<!-- test: release-on-error-propagation -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Resource
	export var id Integer
end 'Resource'

enum ResourceError
	case notFound
end 'ResourceError'

function loadResource() returns Resource throws ResourceError
	throw ResourceError.notFound
end 'loadResource'

function process() returns Integer throws ResourceError
	@heap var marker = Resource{id: 42}
	var res = try loadResource()
	return res.id + marker.id
end 'process'

function main() returns ExitCode
	var result = try process() otherwise 'err'
		return 99
	end 'err'
	return result
end 'main'
```
```exitcode
99
```

### Error propagation from inside block scope

When error propagation happens inside a nested block scope (e.g., inside an if),
all intermediate scopes plus the function scope must be cleaned up.

<!-- test: release-on-error-propagation-in-block -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper
	export var val Integer
end 'Wrapper'

enum LookupError
	case missing
end 'LookupError'

function failingLookup() returns Integer throws LookupError
	throw LookupError.missing
end 'failingLookup'

function compute(flag Integer) returns Integer throws LookupError
	var w = Wrapper{val: flag}
	if w.val > 0 'positive'
		var inner = Wrapper{val: w.val * 2}
		var result = try failingLookup()
		return result + inner.val
	end 'positive'
	return 0
end 'compute'

function main() returns ExitCode
	var result = try compute(flag: 5) otherwise 'err'
		return 77
	end 'err'
	return result
end 'main'
```
```exitcode
77
```

### Generic function with scope ops (monomorphization)

When a generic function (via interface alias / typealias with) contains scope
management ops (scope_enter, scope_exit, move), the monomorphization pass must
clone these ops correctly. Missing handlers would crash the compiler.

<!-- test: generic-function-with-scope-ops -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper
	export var value Integer
end 'Wrapper'

typealias WrapperArray = Array with Wrapper

function firstOrDefault(arr WrapperArray) returns Wrapper
	var fallback = Wrapper{value: 0}
	var result = try arr.get(0) otherwise fallback
	return result
end 'firstOrDefault'

function main() returns ExitCode
	var arr = WrapperArray{}
	var w = Wrapper{value: 42}
	arr.push(w)
	var got = firstOrDefault(arr: arr)
	return got.value
end 'main'
```
```exitcode
42
```

### Reference identity in generic context

The `is` operator (MaxonRefEqOp) must be handled by function cloner and
monomorphization passes when it appears in generic or cloned functions.

<!-- test: ref-identity-in-cloned-function -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value Integer
end 'Box'

function isSame(a Box, b Box) returns Integer
	if a is b 'same'
		return 1
	end 'same'
	return 0
end 'isSame'

function main() returns ExitCode
	var x = Box{value: 10}
	var y = x
	var z = Box{value: 10}
	var same = isSame(a: x, b: y)
	var diff = isSame(a: x, b: z)
	return same + diff
end 'main'
```
```exitcode
1
```
