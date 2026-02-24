---
feature: memory-safety
status: experimental
keywords: [ref, reference, copy, clone, cloneable, equatable, ownership, region, lifetime]
category: core
---

# Memory Safety

## Documentation

### Copy-by-Default Assignment

In Maxon, assigning a struct variable to another variable creates a **deep copy** by default:

```text
var a = Point{x: 1, y: 2}
var b = a        // b is an independent copy of a
b.x = 99        // a.x is still 1
```

This requires the type to implement the `Cloneable` interface. The compiler auto-generates `Cloneable` conformance for any struct whose fields are all Cloneable (all primitives, String, Array, and Cloneable structs qualify).

### References with `ref`

To create a reference (alias) to an existing struct, use `ref`:

```text
var a = Point{x: 1, y: 2}
var b = ref a    // b is a reference to a (same object)
b.x = 99        // a.x is also 99
```

References can also target struct fields and array elements:

```text
let p = Point{x: 1, y: 2}
var xRef = ref p.x    // reference to the x field
```

A `ref` to a struct field or array element requires the source container to be immutable (`let`). This prevents dangling references when the source is reassigned.

### Reference Rules

- `ref` target must be `var`, not `let` (`let b = ref a` is an error)
- `ref` to a standalone primitive is an error (`var b = ref 42`)
- `ref` to a field/element of a mutable (`var`) container is an error
- Use `is` to compare reference identity; use `==` for content equality (requires `Equatable`)

### Equality

- `==` compares contents and requires `Equatable` conformance
- `is` compares reference identity (same heap object)
- The compiler auto-generates `Equatable` conformance for structs whose fields all implement `Equatable`

### Parameter Passing

All function parameters are passed by reference. The compiler infers parameter immutability: parameters that are not assigned to inside the function body are semantically immutable (`let`).

### Ownership and Regions

Every object is owned by a region (stack frame, struct, or array). When a region ends, everything it owns is freed. Return values transfer ownership to the caller's region. References must not outlive the objects they refer to.

## Tests

<!-- test: copy-by-default -->
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
1
```

<!-- test: ref-creates-alias -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = ref a
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

<!-- test: ref-let-binding-error -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  let b = ref a
  return 0
end 'main'
```
```maxoncstderr
error E3070: 'ref' binding must use 'var', not 'let'
```

<!-- test: ref-standalone-primitive-error -->
```maxon
function main() returns ExitCode
  var a = 42
  var b = ref a
  return 0
end 'main'
```
```maxoncstderr
error E3071: 'ref' cannot reference a standalone primitive variable; ref targets structs, fields, or array elements
```

<!-- test: ref-field-immutable-ok -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  let p = Point{x: 42, y: 10}
  var r = ref p.x
  return r
end 'main'
```
```exitcode
42
```

<!-- test: ref-field-mutable-error -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var p = Point{x: 1, y: 2}
  var r = ref p.x
  return 0
end 'main'
```
```maxoncstderr
error E3076: 'ref' to field of mutable variable 'p'; source must be immutable ('let')
```

<!-- test: non-cloneable-copy-error -->
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
  var a = Item{color: Color.red, value: 1}
  var b = a
  print("{b.value}")
  return 0
end 'main'
```
```maxoncstderr
error E3077: cannot copy type 'Item': not all fields implement 'Cloneable'
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
  var b = a
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
  var b = a
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
  var b = a
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
  var y = x
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
  var b = ref a
  if a is b 'same'
    return 1
  end 'same'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: is-after-copy -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = a
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
```RequiredMLIR
=== maxon
module {
  func @memory-safety.createAndDrop() -> i64 {
  entry:
    __scope_8 = maxon.scope_enter {tag = memory-safety.createAndDrop}
    %9 = maxon.literal {value = 42 : i64}
    %10 = maxon.struct_literal @Resource
    maxon.assign %10 {var = r} {decl = 1 : i1} {mut = 1 : i1}
    %11 = maxon.struct_var_ref r
    %12 = maxon.field_access .id %11
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.return %12
  }
  func @memory-safety.main() -> i64 {
  entry:
    __scope_13 = maxon.scope_enter {tag = memory-safety.main}
    %14 = maxon.call @memory-safety.createAndDrop
    maxon.assign %14 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 0 : i64}
    %16 = maxon.binop %14, %15 {op = lt}
    %17 = maxon.literal {value = 4294967295 : i64}
    %18 = maxon.binop %14, %17 {op = gt}
    %19 = maxon.binop %16, %18 {op = or}
    maxon.cond_br %19 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at scope-cleanup.test:14: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %21 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_13} {tag = return_cleanup}
    maxon.return %21
  }
}
=== standard
module {
  func @memory-safety.createAndDrop() -> i64 {
  entry:
    %1 = arith.constant {value = 42 : i64}
    memref.bulk_zero __stk_2, 1
    %3 = memref.lea __stk_2
    %4 = std.ptr_to_i64 %3
    memref.store %4, r
    %5 = memref.load r : i64
    memref.store_indirect %1, %5+0
    %6 = memref.load r : i64
    %7 = memref.load_indirect %6+0
    func.return %7
  }
  func @memory-safety.main() -> u32 {
  entry:
    %9 = func.call @memory-safety.createAndDrop
    memref.store %9, __range_val_0
    %10 = arith.constant {value = 0 : i64}
    %11 = arith.cmpi lt %9, %10
    %12 = arith.constant {value = 4294967295 : i64}
    %13 = arith.cmpi gt %9, %12
    %14 = arith.ori1 %11, %13
    cf.cond_br %14 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %15 = memref.lea_symdata __panic_msg_20
    %16 = std.ptr_to_i64 %15
    std.call_runtime @maxon_panic %16
  __range_ok_0:
    %17 = memref.load __range_val_0 : i64
    func.return %17
  }
}
=== x86
module {
  func @memory-safety.createAndDrop() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 42
    x86.lea rdi, [rbp-8]
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.rep_stosq
    x86.lea rcx, [rbp-8]
    x86.mov rdx, rcx
    x86.mov [rbp-16], edx
    x86.mov ebx, [rbp-16]
    x86.mov esi, 42
    x86.mov [ebx+0], esi
    x86.mov edi, [rbp-16]
    x86.mov eax, [edi+0]
    x86.epilogue
    x86.ret
  }
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.call memory-safety.createAndDrop
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
    x86.lea_symdata rax, [__panic_msg_20]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
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
```RequiredMLIR
=== maxon
module {
  func @memory-safety.makeRef() -> Point {
  entry:
    __scope_13 = maxon.scope_enter {tag = memory-safety.makeRef}
    %14 = maxon.literal {value = 1 : i64}
    %15 = maxon.literal {value = 2 : i64}
    %16 = maxon.struct_literal @Point
    maxon.assign %16 {var = local} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.struct_var_ref local
    maxon.move {var = local} {dest = __scope_13} {tag = return_move}
    maxon.scope_exit {scope = __scope_13} {tag = return_cleanup}
    maxon.return %17
  }
  func @memory-safety.main() -> i64 {
  entry:
    __scope_18 = maxon.scope_enter {tag = memory-safety.main}
    %19 = maxon.call @memory-safety.makeRef
    maxon.assign %19 {var = p} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.struct_var_ref p
    %21 = maxon.field_access .x %20
    %22 = maxon.string_interp
    maxon.call @stdlib.Print.print %22
    %23 = maxon.literal {value = 0 : i64}
    maxon.scope_exit {scope = __scope_18} {tag = return_cleanup}
    maxon.return %23
  }
}
=== standard
module {
  func @memory-safety.makeRef() -> i64 {
  entry:
    %1 = arith.constant {value = 1 : i64}
    %2 = arith.constant {value = 2 : i64}
    %3 = arith.constant {value = 16 : i64}
    %4 = arith.constant {value = 0 : i64}
    %5 = std.call_runtime @mm_alloc %3, %4
    memref.store %5, local
    %6 = memref.load local : i64
    memref.store_indirect %1, %6+0
    %7 = memref.load local : i64
    memref.store_indirect %2, %7+8
    %8 = memref.load local : i64
    func.return %8
  }
  func @memory-safety.main() -> u32 {
  entry:
    %9 = arith.constant {value = 0 : i64}
    %10 = std.call_runtime @mm_scope_enter %9
    memref.store %10, __scope_18
    %11 = func.call @memory-safety.makeRef
    memref.store %11, p
    %13 = memref.load p : i64
    %14 = memref.load_indirect %13+0
    %15 = arith.constant {value = 21 : i64}
    %16 = arith.constant {value = 0 : i64}
    %17 = std.call_runtime @mm_alloc %15, %16
    memref.store %17, __tostr_buf_17
    %18 = std.call_runtime @maxon_i64_to_string %14, %17
    %19 = memref.load __tostr_buf_17 : i64
    %20 = arith.constant {value = 16 : i64}
    %21 = arith.constant {value = 0 : i64}
    %22 = std.call_runtime @mm_alloc %20, %21
    memref.store %22, __interptmp_22
    %23 = arith.constant {value = 32 : i64}
    %24 = arith.constant {value = 0 : i64}
    %25 = std.call_runtime @mm_alloc_in %23, %22, %24
    memref.store %25, __interp_managed_22
    %26 = arith.constant {value = 1 : i64}
    %27 = arith.addi %18, %26
    %28 = arith.constant {value = 0 : i64}
    %29 = std.call_runtime @mm_alloc_in %27, %25, %28
    %30 = arith.constant {value = 0 : i64}
    memref.store %30, __interp_offset_22
    memref.store %29, __interp_buf_22
    memref.store %18, __interp_totallen_22
    memref.store %19, __interp_partbuf_22_0
    memref.store %18, __interp_partlen_22_0
    %31 = memref.load __interp_buf_22 : i64
    %32 = memref.load __interp_offset_22 : i64
    %33 = arith.addi %31, %32
    %34 = memref.load __interp_partbuf_22_0 : i64
    %35 = memref.load __interp_partlen_22_0 : i64
    std.memcopy %34, %33, %35
    %39 = memref.load __interp_buf_22 : i64
    %40 = memref.load __interp_totallen_22 : i64
    %41 = arith.addi %39, %40
    %42 = arith.constant {value = 0 : i64}
    memref.store_indirect %42, %41+0
    %43 = memref.load __tostr_buf_17 : i64
    std.call_runtime @mm_free %43
    %44 = memref.load __interp_buf_22 : i64
    %45 = memref.load __interp_managed_22 : i64
    memref.store_indirect %44, %45+0
    %46 = memref.load __interp_totallen_22 : i64
    %47 = memref.load __interp_managed_22 : i64
    memref.store_indirect %46, %47+8
    %48 = memref.load __interp_managed_22 : i64
    memref.store_indirect %46, %48+16
    %49 = arith.constant {value = 1 : i64}
    %50 = memref.load __interp_managed_22 : i64
    memref.store_indirect %49, %50+24
    %51 = memref.load __interp_managed_22 : i64
    %52 = memref.load __interptmp_22 : i64
    memref.store_indirect %51, %52+0
    %53 = arith.constant {value = 0 : i64}
    %54 = memref.load __interptmp_22 : i64
    memref.store_indirect %53, %54+8
    %55 = memref.load __interptmp_22 : i64
    func.call @stdlib.Print.print %55
    %56 = arith.constant {value = 0 : i64}
    %57 = memref.load __scope_18 : i64
    std.call_runtime @mm_scope_exit %57
    func.return %56
  }
}
=== x86
module {
  func @memory-safety.makeRef() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 1
    x86.mov ecx, 2
    x86.mov rcx, 16
    x86.xor rdx, rdx
    x86.call mm_alloc
    x86.mov [rbp-8], eax
    x86.mov edx, [rbp-8]
    x86.mov ebx, 1
    x86.mov [edx+0], ebx
    x86.mov esi, [rbp-8]
    x86.mov edi, 2
    x86.mov [esi+8], edi
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=96
    x86.xor rcx, rcx
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call memory-safety.makeRef
    x86.mov [rbp-16], eax
    x86.mov eax, [rbp-16]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-88], ecx
    x86.mov rcx, 21
    x86.xor rdx, rdx
    x86.call mm_alloc
    x86.mov [rbp-24], eax
    x86.mov rcx, [rbp-88]
    x86.mov rdx, [rbp-24]
    x86.call maxon_i64_to_string
    x86.mov edx, [rbp-24]
    x86.mov [rbp-96], eax
    x86.mov rcx, 16
    x86.xor rdx, rdx
    x86.call mm_alloc
    x86.mov [rbp-32], eax
    x86.mov rdx, [rbp-32]
    x86.mov rcx, 32
    x86.xor r8, r8
    x86.call mm_alloc_in
    x86.mov [rbp-40], eax
    x86.mov ebx, 1
    x86.mov esi, [rbp-96]
    x86.lea edi, [esi + ebx]
    x86.mov rcx, rdi
    x86.mov rdx, [rbp-40]
    x86.xor r8, r8
    x86.call mm_alloc_in
    x86.xor r8, r8
    x86.mov [rbp-48], r8
    x86.mov [rbp-56], eax
    x86.mov r9, [rbp-96]
    x86.mov [rbp-64], r9
    x86.mov eax, [rbp-24]
    x86.mov [rbp-72], eax
    x86.mov [rbp-80], r9
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-48]
    x86.add eax, ecx
    x86.mov ecx, [rbp-72]
    x86.mov edx, [rbp-80]
    x86.mov rsi, ecx
    x86.mov rdi, eax
    x86.mov rcx, edx
    x86.rep_movsb
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-64]
    x86.add eax, ecx
    x86.xor ecx, ecx
    x86.mov byte ptr [eax+0], ecxb
    x86.mov eax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.call mm_free
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+0], eax
    x86.mov eax, [rbp-64]
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+8], eax
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+16], eax
    x86.mov eax, 1
    x86.mov ecx, [rbp-40]
    x86.mov [ecx+24], eax
    x86.mov eax, [rbp-40]
    x86.mov ecx, [rbp-32]
    x86.mov [ecx+0], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-32]
    x86.mov [ecx+8], eax
    x86.mov eax, [rbp-32]
    x86.mov rcx, [rbp-32]
    x86.call stdlib.Print.print
    x86.mov eax, [rbp-8]
    x86.mov rcx, [rbp-8]
    x86.call mm_scope_exit
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
}
```

<!-- test: ref-escapes-scope-error -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function makeRef() returns Point
  var local = Point{x: 1, y: 2}
  return ref local
end 'makeRef'

function main() returns ExitCode
  var p = makeRef()
  return 0
end 'main'
```
```maxoncstderr
error E3072: cannot return a reference to local variable 'local'
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
    __scope_13 = maxon.scope_enter {tag = memory-safety.main}
    %14 = maxon.literal {value = 0 : i64}
    maxon.assign %14 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 1 : i1}
    maxon.cond_br %15 [then: block_0, else: block_0.merge]
  block_0:
    __scope_16 = maxon.scope_enter {tag = if_then}
    %17 = maxon.literal {value = 10 : i64}
    %18 = maxon.literal {value = 20 : i64}
    %19 = maxon.struct_literal @Point
    maxon.assign %19 {var = p} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.struct_var_ref p
    %21 = maxon.field_access .x %20
    maxon.assign %21 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_16} {tag = block_exit}
    maxon.br block_0.merge
  block_0.merge:
    %22 = maxon.var_ref {var = result} {type = i64}
    maxon.assign %22 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %23 = maxon.literal {value = 0 : i64}
    %24 = maxon.binop %22, %23 {op = lt}
    %25 = maxon.literal {value = 4294967295 : i64}
    %26 = maxon.binop %22, %25 {op = gt}
    %27 = maxon.binop %24, %26 {op = or}
    maxon.cond_br %27 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at block-scope-struct-release.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %29 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.scope_exit {scope = __scope_13} {tag = return_cleanup}
    maxon.return %29
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
    memref.bulk_zero __stk_6, 2
    %7 = memref.lea __stk_6
    %8 = std.ptr_to_i64 %7
    memref.store %8, p
    %9 = memref.load p : i64
    memref.store_indirect %4, %9+0
    %10 = memref.load p : i64
    memref.store_indirect %5, %10+8
    %11 = memref.load p : i64
    %12 = memref.load_indirect %11+0
    memref.store %12, result
    cf.br block_0.merge
  block_0.merge:
    %13 = memref.load result : i64
    memref.store %13, __range_val_1
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %19 = memref.lea_symdata __panic_msg_28
    %20 = std.ptr_to_i64 %19
    std.call_runtime @maxon_panic %20
  __range_ok_1:
    %21 = memref.load __range_val_1 : i64
    func.return %21
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
    x86.lea rdi, [rbp-24]
    x86.xor eax, eax
    x86.mov ecx, 2
    x86.rep_stosq
    x86.lea rdx, [rbp-24]
    x86.mov rbx, rdx
    x86.mov [rbp-32], ebx
    x86.mov esi, [rbp-32]
    x86.mov edi, 10
    x86.mov [esi+0], edi
    x86.mov r8, [rbp-32]
    x86.mov r9, 20
    x86.mov [r8+8], r9
    x86.mov eax, [rbp-32]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-8], ecx
    x86.jmp memory-safety.main.block_0.merge
  block_0.merge:
    x86.mov eax, [rbp-8]
    x86.mov [rbp-40], eax
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
    x86.lea_symdata rax, [__panic_msg_28]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-40]
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
  func @memory-safety.main() -> i64 {
  entry:
    __scope_8 = maxon.scope_enter {tag = memory-safety.main}
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.literal {value = 0 : i64}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.literal {value = 8 : i64}
    %14 = maxon.struct_literal @__ManagedMemory
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
    %24 = maxon.literal {value = 0 : i64}
    %25 = maxon.struct_literal @Item
    maxon.assign %25 {var = __try_default_1} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %23 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    %26 = maxon.literal {value = 0 : i64}
    %27 = maxon.binop %22, %26 {op = ne}
    maxon.cond_br %27 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %28 = maxon.struct_var_ref __try_default_1
    maxon.assign %28 {var = __try_result_0} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    maxon.release {var = __try_default_1} {type = Item}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %29 = maxon.struct_var_ref __try_result_0
    maxon.assign %29 {var = got} {decl = 1 : i1} {mut = 1 : i1}
    %30 = maxon.struct_var_ref got
    %31 = maxon.field_access .value %30
    maxon.assign %31 {var = __range_val_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %32 = maxon.literal {value = 0 : i64}
    %33 = maxon.binop %31, %32 {op = lt}
    %34 = maxon.literal {value = 4294967295 : i64}
    %35 = maxon.binop %31, %34 {op = gt}
    %36 = maxon.binop %33, %35 {op = or}
    maxon.cond_br %36 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    maxon.panic "panic at array-push-struct-incref.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_5:
    %38 = maxon.var_ref {var = __range_val_5} {type = i64}
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.return %38
  }
}
=== standard
module {
  func @memory-safety.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_8
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.constant {value = 0 : i64}
    %6 = arith.constant {value = 8 : i64}
    %7 = arith.constant {value = 32 : i64}
    %8 = arith.constant {value = 0 : i64}
    %9 = std.call_runtime @mm_alloc %7, %8
    memref.store %9, __struct_14
    %10 = memref.load __struct_14 : i64
    memref.store_indirect %3, %10+0
    %11 = memref.load __struct_14 : i64
    memref.store_indirect %4, %11+8
    %12 = memref.load __struct_14 : i64
    memref.store_indirect %5, %12+16
    %13 = memref.load __struct_14 : i64
    memref.store_indirect %6, %13+24
    %14 = arith.constant {value = 16 : i64}
    %15 = arith.constant {value = 0 : i64}
    %16 = std.call_runtime @mm_alloc %14, %15
    memref.store %16, arr
    %17 = memref.load arr : i64
    memref.store_indirect %2, %17+0
    %18 = memref.load __struct_14 : i64
    %19 = memref.load arr : i64
    memref.store_indirect %18, %19+8
    %20 = memref.load arr : i64
    %21 = arith.constant {value = 1 : i64}
    std.call_runtime @mm_move %18, %20, %21
    %22 = arith.constant {value = 7 : i64}
    %23 = arith.constant {value = 8 : i64}
    %24 = arith.constant {value = 0 : i64}
    %25 = std.call_runtime @mm_alloc %23, %24
    memref.store %25, item
    %26 = memref.load item : i64
    memref.store_indirect %22, %26+0
    %27 = memref.load arr : i64
    %28 = memref.load item : i64
    func.call @ItemArray.push %27, %28
    %29 = arith.constant {value = 0 : i64}
    %30 = memref.load arr : i64
    %31, %32 = func.try_call @ItemArray.get %30, %29
    %33 = arith.constant {value = 0 : i64}
    memref.bulk_zero __stk_34, 1
    %35 = memref.lea __stk_34
    %36 = std.ptr_to_i64 %35
    memref.store %36, __try_default_1
    %37 = memref.load __try_default_1 : i64
    memref.store_indirect %33, %37+0
    memref.store %31, __try_result_0
    %39 = arith.constant {value = 0 : i64}
    %40 = arith.cmpi ne %32, %39
    cf.cond_br %40 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %41 = memref.load __try_default_1 : i64
    memref.store %41, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %42 = memref.load __try_result_0 : i64
    memref.store %42, got
    %43 = memref.load got : i64
    %44 = memref.load_indirect %43+0
    memref.store %44, __range_val_5
    %45 = arith.constant {value = 0 : i64}
    %46 = arith.cmpi lt %44, %45
    %47 = arith.constant {value = 4294967295 : i64}
    %48 = arith.cmpi gt %44, %47
    %49 = arith.ori1 %46, %48
    cf.cond_br %49 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    %50 = memref.lea_symdata __panic_msg_37
    %51 = std.ptr_to_i64 %50
    std.call_runtime @maxon_panic %51
  __range_ok_5:
    %52 = memref.load __range_val_5 : i64
    %53 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %53
    func.return %52
  }
}
=== x86
module {
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=80
    x86.xor rcx, rcx
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor eax, eax
    x86.xor ecx, ecx
    x86.xor edx, edx
    x86.xor ebx, ebx
    x86.mov esi, 8
    x86.mov rcx, 32
    x86.xor rdx, rdx
    x86.call mm_alloc
    x86.mov [rbp-16], eax
    x86.mov edi, [rbp-16]
    x86.xor r8, r8
    x86.mov [edi+0], r8
    x86.mov r9, [rbp-16]
    x86.xor eax, eax
    x86.mov [r9+8], eax
    x86.mov eax, [rbp-16]
    x86.xor ecx, ecx
    x86.mov [eax+16], ecx
    x86.mov eax, [rbp-16]
    x86.mov ecx, 8
    x86.mov [eax+24], ecx
    x86.mov rcx, 16
    x86.xor rdx, rdx
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
    x86.mov eax, 7
    x86.mov rcx, 8
    x86.xor rdx, rdx
    x86.call mm_alloc
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-32]
    x86.mov ecx, 7
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-32]
    x86.mov rcx, [rbp-24]
    x86.mov rdx, [rbp-32]
    x86.call ItemArray.push
    x86.mov eax, [rbp-24]
    x86.mov rcx, [rbp-24]
    x86.xor rdx, rdx
    x86.call ItemArray.get
    x86.xor ecx, ecx
    x86.mov [rbp-80], eax
    x86.lea rdi, [rbp-40]
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.rep_stosq
    x86.lea rax, [rbp-40]
    x86.mov rcx, rax
    x86.mov [rbp-48], ecx
    x86.mov eax, [rbp-48]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-80]
    x86.mov [rbp-56], eax
    x86.xor eax, eax
    x86.cmp edx, eax
    x86.je memory-safety.main.otherwise_default_cleanup_4
  otherwise_default_error_2:
    x86.mov eax, [rbp-48]
    x86.mov [rbp-56], eax
    x86.jmp memory-safety.main.otherwise_default_continue_3
  otherwise_default_cleanup_4:
    x86.jmp memory-safety.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-56]
    x86.mov [rbp-64], eax
    x86.mov ecx, [rbp-64]
    x86.mov edx, [ecx+0]
    x86.mov [rbp-72], edx
    x86.xor ebx, ebx
    x86.cmp edx, ebx
    x86.setl esi
    x86.movzx esi, esib
    x86.mov rdi, 4294967295
    x86.cmp rdx, rdi
    x86.setg r8
    x86.movzx r8, r8b
    x86.or esi, r8
    x86.test esi, esi
    x86.je memory-safety.main.__range_ok_5
  __range_panic_5:
    x86.lea_symdata rax, [__panic_msg_37]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_5:
    x86.mov eax, [rbp-72]
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, [rbp-72]
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
    __scope_8 = maxon.scope_enter {tag = memory-safety.main}
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
    __scope_14 = maxon.scope_enter {tag = while}
    %15 = maxon.var_ref {var = i} {type = i64}
    %16 = maxon.struct_literal @Counter
    maxon.assign %16 {var = c} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.struct_var_ref c
    %18 = maxon.field_access .n %17
    %19 = maxon.literal {value = 1 : i64}
    %20 = maxon.binop %18, %19 {op = eq}
    maxon.cond_br %20 [then: check_1, else: check_1.after]
  check_1:
    __scope_21 = maxon.scope_enter {tag = if_then}
    %22 = maxon.struct_var_ref c
    %23 = maxon.field_access .n %22
    maxon.assign %23 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_21} {tag = break_cleanup}
    maxon.scope_exit {scope = __scope_14} {tag = break_cleanup}
    maxon.br loop_0.exit
  check_1.after:
    %24 = maxon.literal {value = 1 : i64}
    %25 = maxon.var_ref {var = i} {type = i64}
    %26 = maxon.binop %25, %24 {op = add}
    maxon.assign %26 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_exit {scope = __scope_14} {tag = block_exit}
    maxon.br loop_0.header
  loop_0.exit:
    %27 = maxon.var_ref {var = result} {type = i64}
    maxon.assign %27 {var = __range_val_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %28 = maxon.literal {value = 0 : i64}
    %29 = maxon.binop %27, %28 {op = lt}
    %30 = maxon.literal {value = 4294967295 : i64}
    %31 = maxon.binop %27, %30 {op = gt}
    %32 = maxon.binop %29, %31 {op = or}
    maxon.cond_br %32 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at release-before-break.test:19: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    %34 = maxon.var_ref {var = __range_val_2} {type = i64}
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.return %34
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
    memref.bulk_zero __stk_8, 1
    %9 = memref.lea __stk_8
    %10 = std.ptr_to_i64 %9
    memref.store %10, c
    %11 = memref.load c : i64
    memref.store_indirect %7, %11+0
    %12 = memref.load c : i64
    %13 = memref.load_indirect %12+0
    %14 = arith.constant {value = 1 : i64}
    %15 = arith.cmpi eq %13, %14
    cf.cond_br %15 [then: check_1, else: check_1.after]
  check_1:
    %17 = memref.load c : i64
    %18 = memref.load_indirect %17+0
    memref.store %18, result
    cf.br loop_0.exit
  check_1.after:
    %19 = arith.constant {value = 1 : i64}
    %20 = memref.load i : i64
    %21 = arith.addi %20, %19
    memref.store %21, i
    cf.br loop_0.header
  loop_0.exit:
    %22 = memref.load result : i64
    memref.store %22, __range_val_2
    %23 = arith.constant {value = 0 : i64}
    %24 = arith.cmpi lt %22, %23
    %25 = arith.constant {value = 4294967295 : i64}
    %26 = arith.cmpi gt %22, %25
    %27 = arith.ori1 %24, %26
    cf.cond_br %27 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %28 = memref.lea_symdata __panic_msg_33
    %29 = std.ptr_to_i64 %28
    std.call_runtime @maxon_panic %29
  __range_ok_2:
    %30 = memref.load __range_val_2 : i64
    func.return %30
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
    x86.lea rdi, [rbp-24]
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.rep_stosq
    x86.lea rcx, [rbp-24]
    x86.mov rdx, rcx
    x86.mov [rbp-32], edx
    x86.mov ebx, [rbp-32]
    x86.mov esi, [rbp-16]
    x86.mov [ebx+0], esi
    x86.mov edi, [rbp-32]
    x86.mov r8, [edi+0]
    x86.mov r9, 1
    x86.cmp r8, r9
    x86.jne memory-safety.main.check_1.after
  check_1:
    x86.mov eax, [rbp-32]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-8], ecx
    x86.jmp memory-safety.main.loop_0.exit
  check_1.after:
    x86.mov eax, 1
    x86.mov ecx, [rbp-16]
    x86.add ecx, eax
    x86.mov [rbp-16], ecx
    x86.jmp memory-safety.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-8]
    x86.mov [rbp-40], eax
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
    x86.lea_symdata rax, [__panic_msg_33]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_2:
    x86.mov eax, [rbp-40]
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
    __scope_8 = maxon.scope_enter {tag = memory-safety.compute}
    %9 = maxon.param {index = 0 : i32} {name = flag} {type = i64}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.binop %9, %10 {op = gt} {optimalType = i64}
    maxon.cond_br %11 [then: check_0, else: check_0.after]
  check_0:
    __scope_12 = maxon.scope_enter {tag = if_then}
    %13 = maxon.var_ref {var = flag} {type = i64}
    %14 = maxon.struct_literal @Wrapper
    maxon.assign %14 {var = w} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.struct_var_ref w
    %16 = maxon.field_access .val %15
    %17 = maxon.literal {value = 1 : i64}
    %18 = maxon.binop %16, %17 {op = add}
    maxon.scope_exit {scope = __scope_12} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.return %18
  check_0.after:
    %19 = maxon.literal {value = 0 : i64}
    maxon.scope_exit {scope = __scope_8} {tag = return_cleanup}
    maxon.return %19
  }
  func @memory-safety.main() -> i64 {
  entry:
    __scope_20 = maxon.scope_enter {tag = memory-safety.main}
    %21 = maxon.literal {value = 5 : i64}
    %22 = maxon.call @memory-safety.compute %21
    maxon.assign %22 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %23 = maxon.literal {value = 0 : i64}
    %24 = maxon.binop %22, %23 {op = lt}
    %25 = maxon.literal {value = 4294967295 : i64}
    %26 = maxon.binop %22, %25 {op = gt}
    %27 = maxon.binop %24, %26 {op = or}
    maxon.cond_br %27 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at release-before-return-in-block.test:17: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %29 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_20} {tag = return_cleanup}
    maxon.return %29
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
    memref.bulk_zero __stk_6, 1
    %7 = memref.lea __stk_6
    %8 = std.ptr_to_i64 %7
    memref.store %8, w
    %9 = memref.load w : i64
    memref.store_indirect %5, %9+0
    %10 = memref.load w : i64
    %11 = memref.load_indirect %10+0
    %12 = arith.constant {value = 1 : i64}
    %13 = arith.addi %11, %12
    func.return %13
  check_0.after:
    %14 = arith.constant {value = 0 : i64}
    func.return %14
  }
  func @memory-safety.main() -> u32 {
  entry:
    %16 = arith.constant {value = 5 : i64}
    %17 = func.call @memory-safety.compute %16
    memref.store %17, __range_val_0
    %18 = arith.constant {value = 0 : i64}
    %19 = arith.cmpi lt %17, %18
    %20 = arith.constant {value = 4294967295 : i64}
    %21 = arith.cmpi gt %17, %20
    %22 = arith.ori1 %19, %21
    cf.cond_br %22 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %23 = memref.lea_symdata __panic_msg_28
    %24 = std.ptr_to_i64 %23
    std.call_runtime @maxon_panic %24
  __range_ok_0:
    %25 = memref.load __range_val_0 : i64
    func.return %25
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
    x86.lea rdi, [rbp-16]
    x86.xor eax, eax
    x86.mov ecx, 1
    x86.rep_stosq
    x86.lea rcx, [rbp-16]
    x86.mov rdx, rcx
    x86.mov [rbp-24], edx
    x86.mov ebx, [rbp-24]
    x86.mov esi, [rbp-8]
    x86.mov [ebx+0], esi
    x86.mov edi, [rbp-24]
    x86.mov r8, [edi+0]
    x86.mov r9, 1
    x86.lea eax, [r8 + r9]
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
    x86.lea_symdata rax, [__panic_msg_28]
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
  var y = ref x
  var z = Box{value: 10}
  var same = isSame(a: x, b: y)
  var diff = isSame(a: x, b: z)
  return same + diff
end 'main'
```
```exitcode
1
```

