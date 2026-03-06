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
error E3069: specs/fragments/memory-safety/eq-requires-equatable.test:11:8: '==' requires type 'Callback' to implement 'Equatable'
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
    maxon.assign %21 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at block-scope-struct-release.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %28 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_end [result, __range_val_1]
    maxon.return %28
  }
}
=== standard
module {
  func @memory-safety.main() -> u32 {
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
    memref.store %15, __range_val_1
    %16 = arith.constant {value = 0 : i64}
    %17 = arith.cmpi lt %15, %16
    %18 = arith.constant {value = 4294967295 : i64}
    %19 = arith.cmpi gt %15, %18
    %20 = arith.ori1 %17, %19
    cf.cond_br %20 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %21 = memref.lea_symdata __panic_msg_27
    %22 = std.ptr_to_i64 %21
    std.call_runtime @maxon_panic %22
  __range_ok_1:
    %23 = memref.load __range_val_1 : i64
    func.return %23
  }
  func @__destruct_Point(ptr: i64) {
  entry:
    %25 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.test ecx, ecx
    x86.je memory-safety.main.block_0.merge
  block_0:
    x86.mov eax, 10
    x86.mov ecx, 20
    x86.mov rcx, 16
    x86.xor rdx, rdx
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-16], eax
    x86.mov edx, [rbp-16]
    x86.mov ebx, 10
    x86.mov [edx+0], ebx
    x86.mov esi, [rbp-16]
    x86.mov edi, 20
    x86.mov [esi+8], edi
    x86.mov r8, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.mov r9, [rbp-16]
    x86.mov eax, [r9+0]
    x86.mov [rbp-8], eax
    x86.mov eax, [rbp-16]
    x86.test eax, eax
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.jmp memory-safety.main.block_0.merge
  block_0.merge:
    x86.mov eax, [rbp-8]
    x86.mov [rbp-24], eax
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
    x86.lea_symdata rax, [__panic_msg_27]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-24]
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
    %2 = maxon.struct_literal @Item
    maxon.assign %2 {var = __retval_3} {decl = 1 : i1}
    maxon.scope_end [__retval_3]
    maxon.return %2
  }
  func @memory-safety.main() -> i64 {
  entry:
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.literal {value = 0 : i64}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.literal {value = 8 : i64}
    %14 = maxon.struct_literal @ElementMemory
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
    maxon.assign %30 {var = __range_val_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %31 = maxon.literal {value = 0 : i64}
    %32 = maxon.binop %30, %31 {op = lt}
    %33 = maxon.literal {value = 4294967295 : i64}
    %34 = maxon.binop %30, %33 {op = gt}
    %35 = maxon.binop %32, %34 {op = or}
    maxon.cond_br %35 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    maxon.panic "panic at array-push-struct-incref.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_4:
    %37 = maxon.var_ref {var = __range_val_4} {type = i64}
    maxon.scope_end [arr, item, got, __range_val_4, __try_result_0]
    maxon.return %37
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
  func @memory-safety.main() -> u32 {
  entry:
    %81 = arith.constant {value = 0 : i64}
    memref.store %81, __try_result_0
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.constant {value = 0 : i64}
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.constant {value = 8 : i64}
    %16 = arith.constant {value = 32 : i64}
    %17 = func.ref @__destruct_ElementMemory
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
    %25 = arith.constant {value = 16 : i64}
    %26 = func.ref @__destruct_ItemArray
    %27 = std.ptr_to_i64 %26
    %28 = arith.constant {value = 3 : i64}
    %29 = std.call_runtime @mm_alloc %25, %27, %28
    memref.store %29, arr
    %30 = memref.load arr : i64
    memref.store_indirect %11, %30+0
    %31 = memref.load __struct_14 : i64
    %32 = memref.load arr : i64
    memref.store_indirect %31, %32+8
    std.call_runtime @mm_incref %31
    %33 = memref.load arr : i64
    std.call_runtime @mm_incref %33
    %34 = arith.constant {value = 7 : i64}
    %35 = arith.constant {value = 8 : i64}
    %36 = arith.constant {value = 0 : i64}
    %37 = arith.constant {value = 1 : i64}
    %38 = std.call_runtime @mm_alloc %35, %36, %37
    memref.store %38, item
    %39 = memref.load item : i64
    memref.store_indirect %34, %39+0
    %40 = memref.load item : i64
    std.call_runtime @mm_incref %40
    %41 = memref.load arr : i64
    %42 = memref.load item : i64
    func.call @ItemArray.push %41, %42
    %43 = arith.constant {value = 0 : i64}
    %44 = memref.load arr : i64
    %45, %46 = func.try_call @ItemArray.get %44, %43
    memref.store %45, __callret_23
    %47 = arith.constant {value = 0 : i64}
    %48 = arith.cmpi ne %46, %47
    cf.cond_br %48 [then: otherwise_default_error_1, else: otherwise_default_success_2]
  otherwise_default_error_1:
    %49 = arith.constant {value = 0 : i64}
    %50 = arith.constant {value = 8 : i64}
    %51 = arith.constant {value = 0 : i64}
    %52 = arith.constant {value = 1 : i64}
    %53 = std.call_runtime @mm_alloc %50, %51, %52
    memref.store %53, __try_result_0
    %54 = memref.load __try_result_0 : i64
    memref.store_indirect %49, %54+0
    %55 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %55
    cf.br otherwise_default_continue_3
  otherwise_default_success_2:
    %56 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %56
    %57 = memref.load __callret_23 : i64
    memref.store %57, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %58 = memref.load __try_result_0 : i64
    memref.store %58, got
    %59 = memref.load got : i64
    std.call_runtime @mm_incref %59
    %60 = memref.load got : i64
    %61 = memref.load_indirect %60+0
    memref.store %61, __range_val_4
    %62 = arith.constant {value = 0 : i64}
    %63 = arith.cmpi lt %61, %62
    %64 = arith.constant {value = 4294967295 : i64}
    %65 = arith.cmpi gt %61, %64
    %66 = arith.ori1 %63, %65
    cf.cond_br %66 [then: __range_panic_4, else: __range_ok_4]
  __range_panic_4:
    %67 = memref.lea_symdata __panic_msg_36
    %68 = std.ptr_to_i64 %67
    std.call_runtime @maxon_panic %68
  __range_ok_4:
    %69 = memref.load __range_val_4 : i64
    %70 = memref.load arr : i64
    std.call_runtime_if_nonnull @mm_decref %70
    %72 = memref.load item : i64
    std.call_runtime_if_nonnull @mm_decref %72
    %74 = memref.load got : i64
    std.call_runtime_if_nonnull @mm_decref %74
    %76 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %76
    func.return %69
  }
  func @__destruct_Item(ptr: i64) {
  entry:
    %212 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
  func @__destruct_ElementMemory(ptr: i64) {
  entry:
    %213 = func.param ptr : StdI64
    memref.store %213, __destr_ptr
    %214 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %214
    %217 = memref.load __destr_ptr : i64
    %218 = memref.load_indirect %217+16
    %219 = arith.constant {value = 0 : i64}
    %220 = arith.cmpi ne %218, %219
    cf.cond_br %220 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %221 = memref.load __destr_ptr : i64
    %222 = memref.load_indirect %221+0
    std.call_runtime @mm_raw_free %222
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_ItemArray(ptr: i64) {
  entry:
    %223 = func.param ptr : StdI64
    memref.store %223, __destr_ptr
    %224 = memref.load __destr_ptr : i64
    %225 = memref.load_indirect %224+8
    std.call_runtime_if_nonnull @mm_decref %225
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
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov edx, [eax+0]
    x86.mov [rbp-24], edx
    x86.mov rcx, 8
    x86.xor rdx, rdx
    x86.mov r8, 1
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
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.xor edx, edx
    x86.xor ebx, ebx
    x86.xor esi, esi
    x86.mov edi, 8
    x86.lea_func r8, [__destruct_ElementMemory]
    x86.mov r9, r8
    x86.mov rdx, r9
    x86.mov rcx, 32
    x86.mov r8, 2
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
    x86.lea_func eax, [__destruct_ItemArray]
    x86.mov rcx, rax
    x86.mov rdx, rcx
    x86.mov rcx, 16
    x86.mov r8, 3
    x86.call mm_alloc
    x86.mov [rbp-24], eax
    x86.mov eax, [rbp-24]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-24]
    x86.mov [ecx+8], eax
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.mov eax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov eax, 7
    x86.mov rcx, 8
    x86.xor rdx, rdx
    x86.mov r8, 1
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
    x86.mov rcx, 8
    x86.xor rdx, rdx
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-8], eax
    x86.mov ecx, [rbp-8]
    x86.xor edx, edx
    x86.mov [ecx+0], edx
    x86.mov ebx, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_incref
    x86.jmp memory-safety.main.otherwise_default_continue_3
  otherwise_default_success_2:
    x86.mov eax, [rbp-8]
    x86.test eax, eax
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-8]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov ecx, [rbp-40]
    x86.mov [rbp-8], ecx
    x86.jmp memory-safety.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-8]
    x86.mov [rbp-48], eax
    x86.mov ecx, [rbp-48]
    x86.call mm_incref
    x86.mov edx, [rbp-48]
    x86.mov ebx, [edx+0]
    x86.mov [rbp-56], ebx
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
    x86.lea_symdata rax, [__panic_msg_36]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_4:
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-24]
    x86.test ecx, ecx
    x86.jz __nonnull_skip_1
    x86.call mm_decref
    x86.label __nonnull_skip_1
    x86.mov eax, [rbp-32]
    x86.test eax, eax
    x86.jz __nonnull_skip_2
    x86.mov rcx, [rbp-32]
    x86.call mm_decref
    x86.label __nonnull_skip_2
    x86.mov ecx, [rbp-48]
    x86.test ecx, ecx
    x86.jz __nonnull_skip_3
    x86.call mm_decref
    x86.label __nonnull_skip_3
    x86.mov edx, [rbp-8]
    x86.test edx, edx
    x86.jz __nonnull_skip_4
    x86.mov rcx, [rbp-8]
    x86.call mm_decref
    x86.label __nonnull_skip_4
    x86.mov eax, [rbp-56]
    x86.epilogue
    x86.ret
  }
  func @__destruct_Item(ptr: i64) {
  entry:
    x86.jmp __destruct_Item.done
  done:
    x86.ret
  }
  func @__destruct_ElementMemory(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_decref_managed_elements
    x86.mov ecx, [rbp-8]
    x86.mov edx, [ecx+16]
    x86.xor ebx, ebx
    x86.cmp edx, ebx
    x86.je __destruct_ElementMemory.skip_buf_0
  free_buf_0:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+0]
    x86.call mm_raw_free
    x86.jmp __destruct_ElementMemory.skip_buf_0
  skip_buf_0:
    x86.jmp __destruct_ElementMemory.done
  done:
    x86.epilogue
    x86.ret
  }
  func @__destruct_ItemArray(ptr: i64) {
  entry:
    x86.prologue stack_size=16
    x86.mov [rbp-8], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+8]
    x86.mov [rbp-16], ecx
    x86.test ecx, ecx
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
```RequiredMLIR
=== maxon
module {
  func @memory-safety.main() -> i64 {
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
    maxon.assign %25 {var = __range_val_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %25, %26 {op = lt}
    %28 = maxon.literal {value = 4294967295 : i64}
    %29 = maxon.binop %25, %28 {op = gt}
    %30 = maxon.binop %27, %29 {op = or}
    maxon.cond_br %30 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at release-before-break.test:19: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    %32 = maxon.var_ref {var = __range_val_2} {type = i64}
    maxon.scope_end [result, i, __range_val_2]
    maxon.return %32
  }
}
=== standard
module {
  func @memory-safety.main() -> u32 {
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
    %6 = arith.constant {value = 8 : i64}
    %7 = arith.constant {value = 0 : i64}
    %8 = arith.constant {value = 1 : i64}
    %9 = std.call_runtime @mm_alloc %6, %7, %8
    memref.store %9, c
    %10 = memref.load c : i64
    memref.store_indirect %5, %10+0
    %11 = memref.load c : i64
    std.call_runtime @mm_incref %11
    %12 = memref.load c : i64
    %13 = memref.load_indirect %12+0
    %14 = arith.constant {value = 1 : i64}
    %15 = arith.cmpi eq %13, %14
    cf.cond_br %15 [then: check_1, else: check_1.after]
  check_1:
    %16 = memref.load c : i64
    %17 = memref.load_indirect %16+0
    memref.store %17, result
    %18 = memref.load c : i64
    std.call_runtime_if_nonnull @mm_decref %18
    cf.br loop_0.exit
  check_1.after:
    %20 = arith.constant {value = 1 : i64}
    %21 = memref.load i : i64
    %22 = arith.addi %21, %20
    memref.store %22, i
    %23 = memref.load c : i64
    std.call_runtime_if_nonnull @mm_decref %23
    cf.br loop_0.header
  loop_0.exit:
    %25 = memref.load result : i64
    memref.store %25, __range_val_2
    %26 = arith.constant {value = 0 : i64}
    %27 = arith.cmpi lt %25, %26
    %28 = arith.constant {value = 4294967295 : i64}
    %29 = arith.cmpi gt %25, %28
    %30 = arith.ori1 %27, %29
    cf.cond_br %30 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %31 = memref.lea_symdata __panic_msg_31
    %32 = std.ptr_to_i64 %31
    std.call_runtime @maxon_panic %32
  __range_ok_2:
    %33 = memref.load __range_val_2 : i64
    func.return %33
  }
  func @__destruct_Counter(ptr: i64) {
  entry:
    %35 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=32
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
    x86.mov rcx, 8
    x86.xor rdx, rdx
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-24], eax
    x86.mov ecx, [rbp-24]
    x86.mov edx, [rbp-16]
    x86.mov [ecx+0], edx
    x86.mov ebx, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.call mm_incref
    x86.mov esi, [rbp-24]
    x86.mov edi, [esi+0]
    x86.mov r8, 1
    x86.cmp edi, r8
    x86.jne memory-safety.main.check_1.after
  check_1:
    x86.mov eax, [rbp-24]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-8], ecx
    x86.mov edx, [rbp-24]
    x86.test edx, edx
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.jmp memory-safety.main.loop_0.exit
  check_1.after:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.mov edx, [rbp-24]
    x86.test edx, edx
    x86.jz __nonnull_skip_1
    x86.mov rcx, [rbp-24]
    x86.call mm_decref
    x86.label __nonnull_skip_1
    x86.jmp memory-safety.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-8]
    x86.mov [rbp-32], eax
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
    x86.lea_symdata rax, [__panic_msg_31]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_2:
    x86.mov eax, [rbp-32]
    x86.epilogue
    x86.ret
  }
  func @__destruct_Counter(ptr: i64) {
  entry:
    x86.jmp __destruct_Counter.done
  done:
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
  func @memory-safety.main() -> i64 {
  entry:
    %19 = maxon.literal {value = 5 : i64}
    %20 = maxon.call @memory-safety.compute %19
    maxon.assign %20 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %21 = maxon.literal {value = 0 : i64}
    %22 = maxon.binop %20, %21 {op = lt}
    %23 = maxon.literal {value = 4294967295 : i64}
    %24 = maxon.binop %20, %23 {op = gt}
    %25 = maxon.binop %22, %24 {op = or}
    maxon.cond_br %25 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at release-before-return-in-block.test:17: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %27 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_end [__range_val_0]
    maxon.return %27
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
  func @memory-safety.main() -> u32 {
  entry:
    %18 = arith.constant {value = 5 : i64}
    %19 = func.call @memory-safety.compute %18
    memref.store %19, __range_val_0
    %20 = arith.constant {value = 0 : i64}
    %21 = arith.cmpi lt %19, %20
    %22 = arith.constant {value = 4294967295 : i64}
    %23 = arith.cmpi gt %19, %22
    %24 = arith.ori1 %21, %23
    cf.cond_br %24 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %25 = memref.lea_symdata __panic_msg_26
    %26 = std.ptr_to_i64 %25
    std.call_runtime @maxon_panic %26
  __range_ok_0:
    %27 = memref.load __range_val_0 : i64
    func.return %27
  }
  func @__destruct_Wrapper(ptr: i64) {
  entry:
    %28 = func.param ptr : StdI64
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
    x86.mov [rbp-8], ecx
    x86.xor eax, eax
    x86.cmp ecx, eax
    x86.jle memory-safety.compute.check_0.after
  check_0:
    x86.mov eax, [rbp-8]
    x86.mov rcx, 8
    x86.xor rdx, rdx
    x86.mov r8, 1
    x86.call mm_alloc
    x86.mov [rbp-16], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [rbp-8]
    x86.mov [ecx+0], edx
    x86.mov ebx, [rbp-16]
    x86.mov rcx, [rbp-16]
    x86.call mm_incref
    x86.mov esi, [rbp-16]
    x86.mov edi, [esi+0]
    x86.mov r8, 1
    x86.add edi, r8
    x86.mov r9, [rbp-16]
    x86.mov [rbp-24], edi
    x86.test r9, r9
    x86.jz __nonnull_skip_0
    x86.mov rcx, [rbp-16]
    x86.call mm_decref
    x86.label __nonnull_skip_0
    x86.mov eax, [rbp-24]
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
    x86.lea_symdata rax, [__panic_msg_26]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-8]
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
