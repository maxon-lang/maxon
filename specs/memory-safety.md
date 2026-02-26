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
error E3078: '==' requires type 'Callback' to implement 'Equatable'
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
  var r = Resource{id: 42}
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
    var p = Point{x: 10, y: 20}
    result = p.x
  end 'block'
  return result
end 'main'
```
```exitcode
10
```
```RequiredMLIR
=== maxon
module {
  func @memory-safety.main() -> i64 {
  entry:
    __scope_15 = maxon.scope_enter {tag = memory-safety.main}
    %16 = maxon.literal {value = 0 : i64}
    maxon.assign %16 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.literal {value = 1 : i1}
    maxon.cond_br %17 [then: block_0, else: block_0.merge]
  block_0:
    __scope_18 = maxon.scope_enter {tag = if_then}
    %19 = maxon.literal {value = 10 : i64}
    %20 = maxon.literal {value = 20 : i64}
    %21 = maxon.struct_literal @Point
    maxon.assign %21 {var = p} {decl = 1 : i1} {mut = 1 : i1}
    %22 = maxon.struct_var_ref p
    %23 = maxon.field_access .x %22
    maxon.assign %23 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_18} {tag = block_exit}
    maxon.br block_0.merge
  block_0.merge:
    %24 = maxon.var_ref {var = result} {type = i64}
    maxon.assign %24 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %25 = maxon.literal {value = 0 : i64}
    %26 = maxon.binop %24, %25 {op = lt}
    %27 = maxon.literal {value = 4294967295 : i64}
    %28 = maxon.binop %24, %27 {op = gt}
    %29 = maxon.binop %26, %28 {op = or}
    maxon.cond_br %29 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at block-scope-struct-release.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %31 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_15} {tag = return_cleanup}
    maxon.return %31
  }
}
=== standard
module {
  func @memory-safety.main() -> u32 {
  entry:
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, result
    %2 = arith.constant {value = 1 : i1}
    cf.cond_br %2 [then: block_0, else: block_0.merge]
  block_0:
    %4 = arith.constant {value = 10 : i64}
    %5 = arith.constant {value = 20 : i64}
    memref.bulk_zero __stk_6, 3
    %7 = memref.lea __stk_6
    %8 = std.ptr_to_i64 %7
    %9 = arith.constant {value = 8 : i64}
    %10 = arith.addi %8, %9
    memref.store %10, p
    %11 = memref.load p : i64
    memref.store_indirect %4, %11+0
    %12 = memref.load p : i64
    memref.store_indirect %5, %12+8
    %13 = memref.load p : i64
    std.call_runtime @mm_incref %13
    %14 = memref.load p : i64
    %15 = memref.load_indirect %14+0
    memref.store %15, result
    %16 = memref.load p : i64
    std.call_runtime @mm_decref %16
    cf.br block_0.merge
  block_0.merge:
    %17 = memref.load result : i64
    memref.store %17, __range_val_1
    %18 = arith.constant {value = 0 : i64}
    %19 = arith.cmpi lt %17, %18
    %20 = arith.constant {value = 4294967295 : i64}
    %21 = arith.cmpi gt %17, %20
    %22 = arith.ori1 %19, %21
    cf.cond_br %22 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %23 = memref.lea_symdata __panic_msg_30
    %24 = std.ptr_to_i64 %23
    std.call_runtime @maxon_panic %24
  __range_ok_1:
    %25 = memref.load __range_val_1 : i64
    func.return %25
  }
}
=== x86
module {
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.test ecx, ecx
    x86.je memory-safety.main.block_0.merge
  block_0:
    x86.mov eax, 10
    x86.mov ecx, 20
    x86.lea rdi, [rbp-32]
    x86.xor eax, eax
    x86.mov ecx, 3
    x86.rep_stosq
    x86.lea rdx, [rbp-32]
    x86.mov rbx, rdx
    x86.mov esi, 8
    x86.add ebx, esi
    x86.mov [rbp-40], ebx
    x86.mov edi, [rbp-40]
    x86.mov r8, 10
    x86.mov [edi+0], r8
    x86.mov r9, [rbp-40]
    x86.mov eax, 20
    x86.mov [r9+8], eax
    x86.mov eax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.mov eax, [rbp-40]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_decref
    x86.jmp memory-safety.main.block_0.merge
  block_0.merge:
    x86.mov eax, [rbp-8]
    x86.mov [rbp-48], eax
    x86.xor ecx, ecx
    x86.cmp eax, ecx
    x86.setl edx
    x86.movzx edx, edxb
    x86.mov rbx, 4294967295
    x86.cmp rax, rbx
    x86.setg esi
    x86.movzx esi, esib
    x86.or edx, esi
    x86.test edx, edx
    x86.je memory-safety.main.__range_ok_1
  __range_panic_1:
    x86.lea_symdata rax, [__panic_msg_30]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-48]
    x86.epilogue
    x86.ret
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
```RequiredMLIR
=== maxon
module {
  func @Item.clone(self: Item) -> Item {
  entry:
    %0 = maxon.struct_param @Item
    %1 = maxon.field_access .value %0
    __scope_2 = maxon.scope_enter {tag = Item.clone}
    %3 = maxon.struct_literal @Item
    maxon.assign %3 {var = __retval_4} {decl = 1 : i1}
    maxon.move {var = __retval_4} {dest = __scope_2} {tag = return_move}
    maxon.scope_exit {scope = __scope_2} {tag = return_cleanup}
    maxon.return %3
  }
  func @memory-safety.main() -> i64 {
  entry:
    __scope_10 = maxon.scope_enter {tag = memory-safety.main}
    %11 = maxon.literal {value = 0 : i64}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.literal {value = 0 : i64}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.literal {value = 8 : i64}
    %16 = maxon.struct_literal @__ManagedMemory
    %17 = maxon.struct_literal @ItemArray
    maxon.assign %17 {var = arr} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.literal {value = 7 : i64}
    %19 = maxon.struct_literal @Item
    maxon.assign %19 {var = item} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.struct_var_ref item
    maxon.call @ItemArray.push %17, %20
    %21 = maxon.struct_var_ref arr
    %22 = maxon.literal {value = 0 : i64}
    %25, %24 = maxon.try_call @ItemArray.get %21, %22
    %28 = maxon.literal {value = 0 : i64}
    %29 = maxon.binop %24, %28 {op = ne}
    maxon.cond_br %29 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.struct_literal @Item
    maxon.assign %27 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_success_2:
    maxon.assign %25 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %30 = maxon.struct_var_ref __try_result_0
    maxon.assign %30 {var = got} {decl = 1 : i1} {mut = 1 : i1}
    %31 = maxon.struct_var_ref got
    %32 = maxon.field_access .value %31
    maxon.assign %32 {var = __range_val_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %33 = maxon.literal {value = 0 : i64}
    %34 = maxon.binop %32, %33 {op = lt}
    %35 = maxon.literal {value = 4294967295 : i64}
    %36 = maxon.binop %32, %35 {op = gt}
    %37 = maxon.binop %34, %36 {op = or}
    maxon.cond_br %37 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    maxon.panic "panic at array-push-struct-incref.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_4:
    %39 = maxon.var_ref {var = __range_val_4} {type = i64}
    maxon.scope_exit {scope = __scope_10} {tag = return_cleanup}
    maxon.return %39
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
    %4 = arith.constant {value = 8 : i64}
    %5 = memref.lea_symdata __tag_Item
    %6 = std.ptr_to_i64 %5
    %7 = std.call_runtime @mm_alloc %4, %6
    memref.store %7, __retval_4
    %8 = memref.load __retval_4 : i64
    memref.store_indirect %2, %8+0
    %9 = memref.load __retval_4 : i64
    std.call_runtime @mm_incref %9
    %10 = memref.load __retval_4 : i64
    func.return %10
  }
  func @memory-safety.main() -> u32 {
  entry:
    %11 = memref.lea_symdata __tag_memory-safety_main
    %12 = std.ptr_to_i64 %11
    %13 = std.call_runtime @mm_scope_enter %12
    memref.store %13, __scope_10
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.constant {value = 0 : i64}
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.constant {value = 0 : i64}
    %18 = arith.constant {value = 8 : i64}
    %19 = arith.constant {value = 32 : i64}
    %20 = memref.lea_symdata __tag___ManagedMemory
    %21 = std.ptr_to_i64 %20
    %22 = std.call_runtime @mm_alloc %19, %21
    memref.store %22, __struct_16
    %23 = memref.load __struct_16 : i64
    memref.store_indirect %15, %23+0
    %24 = memref.load __struct_16 : i64
    memref.store_indirect %16, %24+8
    %25 = memref.load __struct_16 : i64
    memref.store_indirect %17, %25+16
    %26 = memref.load __struct_16 : i64
    memref.store_indirect %18, %26+24
    %27 = arith.constant {value = 16 : i64}
    %28 = memref.lea_symdata __tag_ItemArray
    %29 = std.ptr_to_i64 %28
    %30 = std.call_runtime @mm_alloc %27, %29
    memref.store %30, arr
    %31 = memref.load arr : i64
    memref.store_indirect %14, %31+0
    %32 = memref.load __struct_16 : i64
    %33 = memref.load arr : i64
    memref.store_indirect %32, %33+8
    %34 = memref.load arr : i64
    %35 = arith.constant {value = 1 : i64}
    std.call_runtime @mm_move %32, %34, %35
    %36 = memref.load arr : i64
    std.call_runtime @mm_incref %36
    %37 = arith.constant {value = 7 : i64}
    %38 = arith.constant {value = 8 : i64}
    %39 = memref.lea_symdata __tag_Item
    %40 = std.ptr_to_i64 %39
    %41 = std.call_runtime @mm_alloc %38, %40
    memref.store %41, item
    %42 = memref.load item : i64
    memref.store_indirect %37, %42+0
    %43 = memref.load item : i64
    std.call_runtime @mm_incref %43
    %44 = memref.load arr : i64
    %45 = memref.load item : i64
    func.call @ItemArray.push %44, %45
    %46 = arith.constant {value = 0 : i64}
    %47 = memref.load arr : i64
    %48, %49 = func.try_call @ItemArray.get %47, %46
    memref.store %48, __callret_25
    %50 = arith.constant {value = 0 : i64}
    %51 = arith.cmpi ne %49, %50
    cf.cond_br %51 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %52 = arith.constant {value = 0 : i64}
    %53 = arith.constant {value = 8 : i64}
    %54 = memref.lea_symdata __tag_Item
    %55 = std.ptr_to_i64 %54
    %56 = std.call_runtime @mm_alloc %53, %55
    memref.store %56, __try_result_0
    %57 = memref.load __try_result_0 : i64
    memref.store_indirect %52, %57+0
    %58 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %58
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %59 = memref.load __callret_25 : i64
    memref.store %59, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %60 = memref.load __try_result_0 : i64
    memref.store %60, got
    %61 = memref.load got : i64
    std.call_runtime @mm_incref %61
    %62 = memref.load got : i64
    %63 = memref.load_indirect %62+0
    memref.store %63, __range_val_4
    %64 = arith.constant {value = 0 : i64}
    %65 = arith.cmpi lt %63, %64
    %66 = arith.constant {value = 4294967295 : i64}
    %67 = arith.cmpi gt %63, %66
    %68 = arith.ori1 %65, %67
    cf.cond_br %68 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    %69 = memref.lea_symdata __panic_msg_38
    %70 = std.ptr_to_i64 %69
    std.call_runtime @maxon_panic %70
  __range_ok_4:
    %71 = memref.load __range_val_4 : i64
    %72 = memref.load arr : i64
    std.call_runtime @mm_decref %72
    %73 = memref.load item : i64
    std.call_runtime @mm_decref %73
    %74 = memref.load __try_result_0 : i64
    std.call_runtime @mm_decref %74
    %75 = memref.load got : i64
    std.call_runtime @mm_decref %75
    %76 = memref.load __scope_10 : i64
    std.call_runtime @mm_scope_exit %76
    func.return %71
  }
}
=== x86
module {
  func @Item.clone(__self_ptr: i64) -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov edx, [eax+0]
    x86.lea_symdata rax, [__tag_Item]
    x86.mov rbx, rax
    x86.mov [rbp-24], edx
    x86.mov rdx, rbx
    x86.mov rcx, 8
    x86.call mm_alloc
    x86.mov [rbp-16], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [rbp-24]
    x86.mov [ecx+0], edx
    x86.mov ebx, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.mov eax, [rbp-16]
    x86.epilogue
    x86.ret
  }
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.lea_symdata rax, [__tag_memory-safety_main]
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor edx, edx
    x86.xor ebx, ebx
    x86.xor esi, esi
    x86.xor edi, edi
    x86.mov r8, 8
    x86.lea_symdata r9, [__tag___ManagedMemory]
    x86.mov rax, r9
    x86.mov rdx, rax
    x86.mov rcx, 32
    x86.call mm_alloc
    x86.mov [rbp-16], eax
    x86.mov eax, [rbp-16]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-16]
    x86.xor ecx, ecx
    x86.mov [eax+8], ecx
    x86.mov eax, [rbp-16]
    x86.xor ecx, ecx
    x86.mov [eax+16], ecx
    x86.mov eax, [rbp-16]
    x86.mov ecx, 8
    x86.mov [eax+24], ecx
    x86.lea_symdata rax, [__tag_ItemArray]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.call mm_alloc
    x86.mov [rbp-24], eax
    x86.mov eax, [rbp-24]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-24]
    x86.mov [ecx+8], eax
    x86.mov ecx, [rbp-24]
    x86.mov rcx, [rbp-16]
    x86.mov rdx, [rbp-24]
    x86.mov r8, 1
    x86.call mm_move
    x86.mov eax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov eax, 7
    x86.lea_symdata rcx, [__tag_Item]
    x86.mov rdx, rcx
    x86.mov rcx, 8
    x86.call mm_alloc
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-32]
    x86.mov ecx, 7
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-32]
    x86.mov rcx, [rbp-24]
    x86.mov rdx, [rbp-32]
    x86.call ItemArray.push
    x86.mov eax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.xor rdx, rdx
    x86.call ItemArray.get
    x86.mov [rbp-40], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je memory-safety.main.otherwise_default_success_2
  otherwise_default_error_1:
    x86.xor eax, eax
    x86.lea_symdata rcx, [__tag_Item]
    x86.mov rdx, rcx
    x86.mov rcx, 8
    x86.call mm_alloc
    x86.mov [rbp-48], eax
    x86.mov ebx, [rbp-48]
    x86.xor esi, esi
    x86.mov [ebx+0], esi
    x86.mov edi, [rbp-48]
    x86.mov rcx, [rbp-48]
    x86.call mm_incref
    x86.jmp memory-safety.main.otherwise_default_continue_3
  otherwise_default_success_2:
    x86.mov eax, [rbp-40]
    x86.mov [rbp-48], eax
    x86.jmp memory-safety.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-48]
    x86.mov [rbp-56], eax
    x86.mov ecx, [rbp-56]
    x86.call mm_incref
    x86.mov edx, [rbp-56]
    x86.mov ebx, [edx+0]
    x86.mov [rbp-64], ebx
    x86.xor esi, esi
    x86.cmp ebx, esi
    x86.setl edi
    x86.movzx edi, edib
    x86.mov r8, 4294967295
    x86.cmp rbx, r8
    x86.setg r9
    x86.movzx r9, r9b
    x86.or edi, r9
    x86.test edi, edi
    x86.je memory-safety.main.__range_ok_4
  __range_panic_4:
    x86.lea_symdata rax, [__panic_msg_38]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_4:
    x86.mov eax, [rbp-64]
    x86.mov ecx, [rbp-24]
    x86.call mm_decref
    x86.mov eax, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.call mm_decref
    x86.mov ecx, [rbp-48]
    x86.call mm_decref
    x86.mov edx, [rbp-56]
    x86.mov rcx, [rbp-56]
    x86.call mm_decref
    x86.mov ebx, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, [rbp-64]
    x86.epilogue
    x86.ret
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
      break 'loop'
    end 'check'
    i = i + 1
  end 'loop'
  return result
end 'main'
```
```exitcode
1
```
```RequiredMLIR
=== maxon
module {
  func @memory-safety.main() -> i64 {
  entry:
    __scope_10 = maxon.scope_enter {tag = memory-safety.main}
    %11 = maxon.literal {value = 0 : i64}
    maxon.assign %11 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 0 : i64}
    maxon.assign %12 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %13 = maxon.literal {value = 3 : i64}
    %14 = maxon.var_ref {var = i} {type = i64}
    %15 = maxon.binop %14, %13 {op = lt}
    maxon.cond_br %15 [then: loop_0, else: loop_0.exit]
  loop_0:
    __scope_16 = maxon.scope_enter {tag = while}
    %17 = maxon.var_ref {var = i} {type = i64}
    %18 = maxon.struct_literal @Counter
    maxon.assign %18 {var = c} {decl = 1 : i1} {mut = 1 : i1}
    %19 = maxon.struct_var_ref c
    %20 = maxon.field_access .n %19
    %21 = maxon.literal {value = 1 : i64}
    %22 = maxon.binop %20, %21 {op = eq}
    maxon.cond_br %22 [then: check_1, else: check_1.after]
  check_1:
    __scope_23 = maxon.scope_enter {tag = if_then}
    %24 = maxon.struct_var_ref c
    %25 = maxon.field_access .n %24
    maxon.assign %25 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_23} {tag = break_cleanup}
    maxon.scope_exit {scope = __scope_16} {tag = break_cleanup}
    maxon.br loop_0.exit
  check_1.after:
    %26 = maxon.literal {value = 1 : i64}
    %27 = maxon.var_ref {var = i} {type = i64}
    %28 = maxon.binop %27, %26 {op = add}
    maxon.assign %28 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_16} {tag = block_exit}
    maxon.br loop_0.header
  loop_0.exit:
    %29 = maxon.var_ref {var = result} {type = i64}
    maxon.assign %29 {var = __range_val_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %30 = maxon.literal {value = 0 : i64}
    %31 = maxon.binop %29, %30 {op = lt}
    %32 = maxon.literal {value = 4294967295 : i64}
    %33 = maxon.binop %29, %32 {op = gt}
    %34 = maxon.binop %31, %33 {op = or}
    maxon.cond_br %34 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at release-before-break.test:19: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    %36 = maxon.var_ref {var = __range_val_2} {type = i64}
    maxon.scope_exit {scope = __scope_10} {tag = return_cleanup}
    maxon.return %36
  }
}
=== standard
module {
  func @memory-safety.main() -> u32 {
  entry:
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, result
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, i
    cf.br loop_0.header
  loop_0.header:
    %3 = arith.constant {value = 3 : i64}
    %4 = memref.load i : i64
    %5 = arith.cmpi lt %4, %3
    cf.cond_br %5 [then: loop_0, else: loop_0.exit]
  loop_0:
    %7 = memref.load i : i64
    memref.bulk_zero __stk_8, 2
    %9 = memref.lea __stk_8
    %10 = std.ptr_to_i64 %9
    %11 = arith.constant {value = 8 : i64}
    %12 = arith.addi %10, %11
    memref.store %12, c
    %13 = memref.load c : i64
    memref.store_indirect %7, %13+0
    %14 = memref.load c : i64
    std.call_runtime @mm_incref %14
    %15 = memref.load c : i64
    %16 = memref.load_indirect %15+0
    %17 = arith.constant {value = 1 : i64}
    %18 = arith.cmpi eq %16, %17
    cf.cond_br %18 [then: check_1, else: check_1.after]
  check_1:
    %20 = memref.load c : i64
    %21 = memref.load_indirect %20+0
    memref.store %21, result
    %22 = memref.load c : i64
    std.call_runtime @mm_decref %22
    cf.br loop_0.exit
  check_1.after:
    %23 = arith.constant {value = 1 : i64}
    %24 = memref.load i : i64
    %25 = arith.addi %24, %23
    memref.store %25, i
    %26 = memref.load c : i64
    std.call_runtime @mm_decref %26
    cf.br loop_0.header
  loop_0.exit:
    %27 = memref.load result : i64
    memref.store %27, __range_val_2
    %28 = arith.constant {value = 0 : i64}
    %29 = arith.cmpi lt %27, %28
    %30 = arith.constant {value = 4294967295 : i64}
    %31 = arith.cmpi gt %27, %30
    %32 = arith.ori1 %29, %31
    cf.cond_br %32 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %33 = memref.lea_symdata __panic_msg_35
    %34 = std.ptr_to_i64 %33
    std.call_runtime @maxon_panic %34
  __range_ok_2:
    %35 = memref.load __range_val_2 : i64
    func.return %35
  }
}
=== x86
module {
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.jmp memory-safety.main.loop_0.header
  loop_0.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-16]
    x86.cmp ecx, eax
    x86.jge memory-safety.main.loop_0.exit
  loop_0:
    x86.mov eax, [rbp-16]
    x86.lea rdi, [rbp-32]
    x86.xor eax, eax
    x86.mov ecx, 2
    x86.rep_stosq
    x86.lea rcx, [rbp-32]
    x86.mov rdx, rcx
    x86.mov ebx, 8
    x86.add edx, ebx
    x86.mov [rbp-40], edx
    x86.mov esi, [rbp-40]
    x86.mov edi, [rbp-16]
    x86.mov [esi+0], edi
    x86.mov r8, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_incref
    x86.mov r9, [rbp-40]
    x86.mov eax, [r9+0]
    x86.mov ecx, 1
    x86.cmp eax, ecx
    x86.jne memory-safety.main.check_1.after
  check_1:
    x86.mov eax, [rbp-40]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-8], ecx
    x86.mov edx, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_decref
    x86.jmp memory-safety.main.loop_0.exit
  check_1.after:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.mov edx, [rbp-40]
    x86.mov rcx, [rbp-40]
    x86.call mm_decref
    x86.jmp memory-safety.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-8]
    x86.mov [rbp-48], eax
    x86.xor ecx, ecx
    x86.cmp eax, ecx
    x86.setl edx
    x86.movzx edx, edxb
    x86.mov rbx, 4294967295
    x86.cmp rax, rbx
    x86.setg esi
    x86.movzx esi, esib
    x86.or edx, esi
    x86.test edx, edx
    x86.je memory-safety.main.__range_ok_2
  __range_panic_2:
    x86.lea_symdata rax, [__panic_msg_35]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_2:
    x86.mov eax, [rbp-48]
    x86.epilogue
    x86.ret
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
    var w = Wrapper{val: flag}
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
```RequiredMLIR
=== maxon
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    __scope_10 = maxon.scope_enter {tag = memory-safety.compute}
    %11 = maxon.param {index = 0 : i32} {name = flag} {type = i64}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %11, %12 {op = gt} {optimalType = i64}
    maxon.cond_br %13 [then: check_0, else: check_0.after]
  check_0:
    __scope_14 = maxon.scope_enter {tag = if_then}
    %15 = maxon.var_ref {var = flag} {type = i64}
    %16 = maxon.struct_literal @Wrapper
    maxon.assign %16 {var = w} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.struct_var_ref w
    %18 = maxon.field_access .val %17
    %19 = maxon.literal {value = 1 : i64}
    %20 = maxon.binop %18, %19 {op = add}
    maxon.scope_exit {scope = __scope_14} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_10} {tag = return_cleanup}
    maxon.return %20
  check_0.after:
    %21 = maxon.literal {value = 0 : i64}
    maxon.scope_exit {scope = __scope_10} {tag = return_cleanup}
    maxon.return %21
  }
  func @memory-safety.main() -> i64 {
  entry:
    __scope_22 = maxon.scope_enter {tag = memory-safety.main}
    %23 = maxon.literal {value = 5 : i64}
    %24 = maxon.call @memory-safety.compute %23
    maxon.assign %24 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %25 = maxon.literal {value = 0 : i64}
    %26 = maxon.binop %24, %25 {op = lt}
    %27 = maxon.literal {value = 4294967295 : i64}
    %28 = maxon.binop %24, %27 {op = gt}
    %29 = maxon.binop %26, %28 {op = or}
    maxon.cond_br %29 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at release-before-return-in-block.test:17: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %31 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_22} {tag = return_cleanup}
    maxon.return %31
  }
}
=== standard
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    %1 = func.param flag : StdI64
    memref.store %1, flag
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.cmpi gt %1, %2
    cf.cond_br %3 [then: check_0, else: check_0.after]
  check_0:
    %5 = memref.load flag : i64
    memref.bulk_zero __stk_6, 2
    %7 = memref.lea __stk_6
    %8 = std.ptr_to_i64 %7
    %9 = arith.constant {value = 8 : i64}
    %10 = arith.addi %8, %9
    memref.store %10, w
    %11 = memref.load w : i64
    memref.store_indirect %5, %11+0
    %12 = memref.load w : i64
    std.call_runtime @mm_incref %12
    %13 = memref.load w : i64
    %14 = memref.load_indirect %13+0
    %15 = arith.constant {value = 1 : i64}
    %16 = arith.addi %14, %15
    %17 = memref.load w : i64
    std.call_runtime @mm_decref %17
    func.return %16
  check_0.after:
    %18 = arith.constant {value = 0 : i64}
    func.return %18
  }
  func @memory-safety.main() -> u32 {
  entry:
    %20 = arith.constant {value = 5 : i64}
    %21 = func.call @memory-safety.compute %20
    memref.store %21, __range_val_0
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi lt %21, %22
    %24 = arith.constant {value = 4294967295 : i64}
    %25 = arith.cmpi gt %21, %24
    %26 = arith.ori1 %23, %25
    cf.cond_br %26 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %27 = memref.lea_symdata __panic_msg_30
    %28 = std.ptr_to_i64 %27
    std.call_runtime @maxon_panic %28
  __range_ok_0:
    %29 = memref.load __range_val_0 : i64
    func.return %29
  }
}
=== x86
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.mov [rbp-8], ecx
    x86.xor eax, eax
    x86.cmp ecx, eax
    x86.jle memory-safety.compute.check_0.after
  check_0:
    x86.mov eax, [rbp-8]
    x86.lea rdi, [rbp-24]
    x86.xor eax, eax
    x86.mov ecx, 2
    x86.rep_stosq
    x86.lea rcx, [rbp-24]
    x86.mov rdx, rcx
    x86.mov ebx, 8
    x86.add edx, ebx
    x86.mov [rbp-32], edx
    x86.mov esi, [rbp-32]
    x86.mov edi, [rbp-8]
    x86.mov [esi+0], edi
    x86.mov r8, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.call mm_incref
    x86.mov r9, [rbp-32]
    x86.mov eax, [r9+0]
    x86.mov ecx, 1
    x86.add eax, ecx
    x86.mov ecx, [rbp-32]
    x86.mov [rbp-40], eax
    x86.call mm_decref
    x86.mov eax, [rbp-40]
    x86.epilogue
    x86.ret
  check_0.after:
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov rcx, 5
    x86.call memory-safety.compute
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.cmp eax, ecx
    x86.setl ecx
    x86.movzx ecx, ecxb
    x86.mov rdx, 4294967295
    x86.cmp rax, rdx
    x86.setg eax
    x86.movzx eax, eaxb
    x86.or ecx, eax
    x86.test ecx, ecx
    x86.je memory-safety.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_30]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
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
      continue 'loop'
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
      break 'search'
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
  var marker = Resource{id: 42}
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
