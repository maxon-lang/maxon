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
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_8
    %2 = arith.constant {value = 42 : i64}
    %3 = arith.constant {value = 8 : i64}
    %4 = arith.constant {value = 0 : i64}
    %5 = std.call_runtime @mm_alloc %3, %4
    memref.store %5, r
    %6 = memref.load r : i64
    memref.store_indirect %2, %6+0
    %7 = memref.load r : i64
    %8 = memref.load_indirect %7+0
    %9 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %9
    func.return %8
  }
  func @memory-safety.main() -> u32 {
  entry:
    %10 = arith.constant {value = 0 : i64}
    %11 = std.call_runtime @mm_scope_enter %10
    memref.store %11, __scope_13
    %12 = func.call @memory-safety.createAndDrop
    memref.store %12, __range_val_0
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.cmpi lt %12, %13
    %15 = arith.constant {value = 4294967295 : i64}
    %16 = arith.cmpi gt %12, %15
    %17 = arith.ori1 %14, %16
    cf.cond_br %17 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %18 = memref.lea_symdata __panic_msg_20
    %19 = std.ptr_to_i64 %18
    std.call_runtime @maxon_panic %19
  __range_ok_0:
    %20 = memref.load __range_val_0 : i64
    %21 = memref.load __scope_13 : i64
    std.call_runtime @mm_scope_exit %21
    func.return %20
  }
}
=== x86
module {
  func @memory-safety.createAndDrop() -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 42
    x86.mov edx, 8
    x86.xor ebx, ebx
    x86.mov rcx, rdx
    x86.mov rdx, rbx
    x86.call mm_alloc
    x86.mov [rbp-16], eax
    x86.mov esi, [rbp-16]
    x86.mov edi, 42
    x86.mov [esi+0], edi
    x86.mov r8, [rbp-16]
    x86.mov eax, [r8+0]
    x86.mov r9, [rbp-8]
    x86.mov [rbp-24], eax
    x86.mov rcx, r9
    x86.call mm_scope_exit
    x86.mov eax, [rbp-24]
    x86.epilogue
    x86.ret
  }
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call memory-safety.createAndDrop
    x86.mov [rbp-16], eax
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
    x86.je memory-safety.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_20]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-24], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-24]
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
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_13
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.constant {value = 2 : i64}
    %4 = arith.constant {value = 16 : i64}
    %5 = arith.constant {value = 0 : i64}
    %6 = std.call_runtime @mm_alloc %4, %5
    memref.store %6, local
    %7 = memref.load local : i64
    memref.store_indirect %2, %7+0
    %8 = memref.load local : i64
    memref.store_indirect %3, %8+8
    %9 = memref.load local : i64
    %10 = memref.load __scope_13 : i64
    %11 = memref.load_indirect %10+8
    %12 = arith.constant {value = 0 : i64}
    std.call_runtime @mm_move %9, %11, %12
    %13 = memref.load __scope_13 : i64
    std.call_runtime @mm_scope_exit %13
    %14 = memref.load local : i64
    func.return %14
  }
  func @memory-safety.main() -> u32 {
  entry:
    %15 = arith.constant {value = 0 : i64}
    %16 = std.call_runtime @mm_scope_enter %15
    memref.store %16, __scope_18
    %17 = func.call @memory-safety.makeRef
    memref.store %17, p
    %19 = memref.load p : i64
    %20 = memref.load_indirect %19+0
    %21 = arith.constant {value = 21 : i64}
    %22 = arith.constant {value = 0 : i64}
    %23 = std.call_runtime @mm_alloc %21, %22
    memref.store %23, __tostr_buf_23
    %24 = std.call_runtime @maxon_i64_to_string %20, %23
    %25 = memref.load __tostr_buf_23 : i64
    %26 = arith.constant {value = 16 : i64}
    %27 = arith.constant {value = 0 : i64}
    %28 = std.call_runtime @mm_alloc %26, %27
    memref.store %28, __interptmp_22
    %29 = arith.constant {value = 32 : i64}
    %30 = arith.constant {value = 0 : i64}
    %31 = std.call_runtime @mm_alloc_in %29, %28, %30
    memref.store %31, __interp_managed_22
    %32 = arith.constant {value = 1 : i64}
    %33 = arith.addi %24, %32
    %34 = arith.constant {value = 0 : i64}
    %35 = std.call_runtime @mm_alloc_in %33, %31, %34
    %36 = arith.constant {value = 0 : i64}
    memref.store %36, __interp_offset_22
    memref.store %35, __interp_buf_22
    memref.store %24, __interp_totallen_22
    memref.store %25, __interp_partbuf_22_0
    memref.store %24, __interp_partlen_22_0
    %37 = memref.load __interp_buf_22 : i64
    %38 = memref.load __interp_offset_22 : i64
    %39 = arith.addi %37, %38
    %40 = memref.load __interp_partbuf_22_0 : i64
    %41 = memref.load __interp_partlen_22_0 : i64
    std.memcopy %40, %39, %41
    %45 = memref.load __interp_buf_22 : i64
    %46 = memref.load __interp_totallen_22 : i64
    %47 = arith.addi %45, %46
    %48 = arith.constant {value = 0 : i64}
    memref.store_indirect %48, %47+0
    %49 = memref.load __tostr_buf_23 : i64
    std.call_runtime @mm_free %49
    %50 = memref.load __interp_buf_22 : i64
    %51 = memref.load __interp_managed_22 : i64
    memref.store_indirect %50, %51+0
    %52 = memref.load __interp_totallen_22 : i64
    %53 = memref.load __interp_managed_22 : i64
    memref.store_indirect %52, %53+8
    %54 = memref.load __interp_managed_22 : i64
    memref.store_indirect %52, %54+16
    %55 = arith.constant {value = 1 : i64}
    %56 = memref.load __interp_managed_22 : i64
    memref.store_indirect %55, %56+24
    %57 = memref.load __interp_managed_22 : i64
    %58 = memref.load __interptmp_22 : i64
    memref.store_indirect %57, %58+0
    %59 = arith.constant {value = 0 : i64}
    %60 = memref.load __interptmp_22 : i64
    memref.store_indirect %59, %60+8
    %61 = memref.load __interptmp_22 : i64
    func.call @stdlib.Print.print %61
    %62 = arith.constant {value = 0 : i64}
    %63 = memref.load __scope_18 : i64
    std.call_runtime @mm_scope_exit %63
    func.return %62
  }
}
=== x86
module {
  func @memory-safety.makeRef() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 1
    x86.mov edx, 2
    x86.mov ebx, 16
    x86.xor esi, esi
    x86.mov rcx, rbx
    x86.mov rdx, rsi
    x86.call mm_alloc
    x86.mov [rbp-16], eax
    x86.mov edi, [rbp-16]
    x86.mov r8, 1
    x86.mov [edi+0], r8
    x86.mov r9, [rbp-16]
    x86.mov eax, 2
    x86.mov [r9+8], eax
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-8]
    x86.mov edx, [ecx+8]
    x86.xor ecx, ecx
    x86.mov r8, rcx
    x86.mov rcx, rax
    x86.call mm_move
    x86.mov rcx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, [rbp-16]
    x86.epilogue
    x86.ret
  }
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=112
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.call memory-safety.makeRef
    x86.mov [rbp-16], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, [ecx+0]
    x86.mov ebx, 21
    x86.xor esi, esi
    x86.mov [rbp-88], edx
    x86.mov rcx, rbx
    x86.mov rdx, rsi
    x86.call mm_alloc
    x86.mov [rbp-24], eax
    x86.mov rcx, [rbp-88]
    x86.mov rdx, rax
    x86.call maxon_i64_to_string
    x86.mov edi, [rbp-24]
    x86.mov r8, 16
    x86.xor r9, r9
    x86.mov [rbp-96], eax
    x86.mov [rbp-104], edi
    x86.mov rcx, r8
    x86.mov rdx, r9
    x86.call mm_alloc
    x86.mov [rbp-32], eax
    x86.mov ecx, 32
    x86.xor edx, edx
    x86.mov r8, rdx
    x86.mov rdx, rax
    x86.call mm_alloc_in
    x86.mov [rbp-40], eax
    x86.mov ecx, 1
    x86.mov edx, [rbp-96]
    x86.lea ebx, [edx + ecx]
    x86.xor ecx, ecx
    x86.mov rdx, rax
    x86.mov r8, rcx
    x86.mov rcx, rbx
    x86.call mm_alloc_in
    x86.xor ecx, ecx
    x86.mov [rbp-48], ecx
    x86.mov [rbp-56], eax
    x86.mov eax, [rbp-96]
    x86.mov [rbp-64], eax
    x86.mov ecx, [rbp-104]
    x86.mov [rbp-72], ecx
    x86.mov [rbp-80], eax
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
    x86.mov rcx, rax
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
    x86.mov rcx, rax
    x86.call stdlib.Print.print
    x86.xor eax, eax
    x86.mov ecx, [rbp-8]
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
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_13
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, result
    %3 = arith.constant {value = 1 : i1}
    cf.cond_br %3 [then: block_0, else: block_0.merge]
  block_0:
    %4 = arith.constant {value = 0 : i64}
    %5 = std.call_runtime @mm_scope_enter %4
    memref.store %5, __scope_16
    %6 = arith.constant {value = 10 : i64}
    %7 = arith.constant {value = 20 : i64}
    %8 = arith.constant {value = 16 : i64}
    %9 = arith.constant {value = 0 : i64}
    %10 = std.call_runtime @mm_alloc %8, %9
    memref.store %10, p
    %11 = memref.load p : i64
    memref.store_indirect %6, %11+0
    %12 = memref.load p : i64
    memref.store_indirect %7, %12+8
    %13 = memref.load p : i64
    %14 = memref.load_indirect %13+0
    memref.store %14, result
    %15 = memref.load __scope_16 : i64
    std.call_runtime @mm_scope_exit %15
    cf.br block_0.merge
  block_0.merge:
    %16 = memref.load result : i64
    memref.store %16, __range_val_1
    %17 = arith.constant {value = 0 : i64}
    %18 = arith.cmpi lt %16, %17
    %19 = arith.constant {value = 4294967295 : i64}
    %20 = arith.cmpi gt %16, %19
    %21 = arith.ori1 %18, %20
    cf.cond_br %21 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    %22 = memref.lea_symdata __panic_msg_28
    %23 = std.ptr_to_i64 %22
    std.call_runtime @maxon_panic %23
  __range_ok_1:
    %24 = memref.load __range_val_1 : i64
    %25 = memref.load __scope_13 : i64
    std.call_runtime @mm_scope_exit %25
    func.return %24
  }
}
=== x86
module {
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=48
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.mov edx, 1
    x86.test edx, edx
    x86.je memory-safety.main.block_0.merge
  block_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-24], eax
    x86.mov ecx, 10
    x86.mov edx, 20
    x86.mov ebx, 16
    x86.xor esi, esi
    x86.mov rcx, rbx
    x86.mov rdx, rsi
    x86.call mm_alloc
    x86.mov [rbp-32], eax
    x86.mov edi, [rbp-32]
    x86.mov r8, 10
    x86.mov [edi+0], r8
    x86.mov r9, [rbp-32]
    x86.mov eax, 20
    x86.mov [r9+8], eax
    x86.mov eax, [rbp-32]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-16], ecx
    x86.mov rcx, [rbp-24]
    x86.call mm_scope_exit
    x86.jmp memory-safety.main.block_0.merge
  block_0.merge:
    x86.mov eax, [rbp-16]
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
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-48], eax
    x86.call mm_scope_exit
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
    %21 = arith.constant {value = 0 : i64}
    std.call_runtime @mm_reparent %18, %20, %21
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
    %34 = arith.constant {value = 8 : i64}
    %35 = arith.constant {value = 0 : i64}
    %36 = std.call_runtime @mm_alloc %34, %35
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
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.xor edx, edx
    x86.xor ebx, ebx
    x86.xor esi, esi
    x86.mov edi, 8
    x86.mov r8, 32
    x86.xor r9, r9
    x86.mov rcx, r8
    x86.mov rdx, r9
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
    x86.mov eax, 16
    x86.xor ecx, ecx
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.call mm_alloc
    x86.mov [rbp-24], eax
    x86.mov eax, [rbp-24]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-24]
    x86.mov [ecx+8], eax
    x86.mov ecx, [rbp-24]
    x86.xor edx, edx
    x86.mov r8, rdx
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.call mm_reparent
    x86.mov eax, 7
    x86.mov ecx, 8
    x86.xor edx, edx
    x86.call mm_alloc
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-32]
    x86.mov ecx, 7
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-32]
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.call ItemArray.push
    x86.xor eax, eax
    x86.mov ecx, [rbp-24]
    x86.mov rdx, rax
    x86.call ItemArray.get
    x86.xor ecx, ecx
    x86.mov ebx, 8
    x86.xor esi, esi
    x86.mov [rbp-72], eax
    x86.mov [rbp-80], edx
    x86.mov rcx, rbx
    x86.mov rdx, rsi
    x86.call mm_alloc
    x86.mov [rbp-40], eax
    x86.mov eax, [rbp-40]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-72]
    x86.mov [rbp-48], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-80]
    x86.cmp ecx, eax
    x86.je memory-safety.main.otherwise_default_cleanup_4
  otherwise_default_error_2:
    x86.mov eax, [rbp-40]
    x86.mov [rbp-48], eax
    x86.jmp memory-safety.main.otherwise_default_continue_3
  otherwise_default_cleanup_4:
    x86.jmp memory-safety.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-48]
    x86.mov [rbp-56], eax
    x86.mov ecx, [rbp-56]
    x86.mov edx, [ecx+0]
    x86.mov [rbp-64], edx
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
    x86.mov eax, [rbp-64]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-72], eax
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
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_8
    %2 = arith.constant {value = 0 : i64}
    memref.store %2, result
    %3 = arith.constant {value = 0 : i64}
    memref.store %3, i
    cf.br loop_0.header
  loop_0.header:
    %4 = arith.constant {value = 3 : i64}
    %5 = memref.load i : i64
    %6 = arith.cmpi lt %5, %4
    cf.cond_br %6 [then: loop_0, else: loop_0.exit]
  loop_0:
    %7 = arith.constant {value = 0 : i64}
    %8 = std.call_runtime @mm_scope_enter %7
    memref.store %8, __scope_14
    %9 = memref.load i : i64
    %10 = arith.constant {value = 8 : i64}
    %11 = arith.constant {value = 0 : i64}
    %12 = std.call_runtime @mm_alloc %10, %11
    memref.store %12, c
    %13 = memref.load c : i64
    memref.store_indirect %9, %13+0
    %14 = memref.load c : i64
    %15 = memref.load_indirect %14+0
    %16 = arith.constant {value = 1 : i64}
    %17 = arith.cmpi eq %15, %16
    cf.cond_br %17 [then: check_1, else: check_1.after]
  check_1:
    %18 = arith.constant {value = 0 : i64}
    %19 = std.call_runtime @mm_scope_enter %18
    memref.store %19, __scope_21
    %20 = memref.load c : i64
    %21 = memref.load_indirect %20+0
    memref.store %21, result
    %22 = memref.load __scope_21 : i64
    std.call_runtime @mm_scope_exit %22
    cf.br loop_0.exit
  check_1.after:
    %23 = arith.constant {value = 1 : i64}
    %24 = memref.load i : i64
    %25 = arith.addi %24, %23
    memref.store %25, i
    %26 = memref.load __scope_14 : i64
    std.call_runtime @mm_scope_exit %26
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
    %33 = memref.lea_symdata __panic_msg_33
    %34 = std.ptr_to_i64 %33
    std.call_runtime @maxon_panic %34
  __range_ok_2:
    %35 = memref.load __range_val_2 : i64
    %36 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %36
    func.return %35
  }
}
=== x86
module {
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=64
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov [rbp-16], ecx
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.jmp memory-safety.main.loop_0.header
  loop_0.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jge memory-safety.main.loop_0.exit
  loop_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-32], eax
    x86.mov ecx, [rbp-24]
    x86.mov edx, 8
    x86.xor ebx, ebx
    x86.mov [rbp-64], ecx
    x86.mov rcx, rdx
    x86.mov rdx, rbx
    x86.call mm_alloc
    x86.mov [rbp-40], eax
    x86.mov esi, [rbp-40]
    x86.mov edi, [rbp-64]
    x86.mov [esi+0], edi
    x86.mov r8, [rbp-40]
    x86.mov r9, [r8+0]
    x86.mov eax, 1
    x86.cmp r9, eax
    x86.jne memory-safety.main.check_1.after
  check_1:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-48], eax
    x86.mov ecx, [rbp-40]
    x86.mov edx, [ecx+0]
    x86.mov [rbp-16], edx
    x86.mov rcx, [rbp-48]
    x86.call mm_scope_exit
    x86.jmp memory-safety.main.loop_0.exit
  check_1.after:
    x86.mov eax, 1
    x86.mov ecx, [rbp-24]
    x86.add ecx, eax
    x86.mov [rbp-24], ecx
    x86.mov edx, [rbp-32]
    x86.mov rcx, rdx
    x86.call mm_scope_exit
    x86.jmp memory-safety.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-16]
    x86.mov [rbp-56], eax
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
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-64], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-64]
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
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_8
    %2 = func.param flag : StdI64
    memref.store %2, flag
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.cmpi gt %2, %3
    cf.cond_br %4 [then: check_0, else: check_0.after]
  check_0:
    %5 = arith.constant {value = 0 : i64}
    %6 = std.call_runtime @mm_scope_enter %5
    memref.store %6, __scope_12
    %7 = memref.load flag : i64
    %8 = arith.constant {value = 8 : i64}
    %9 = arith.constant {value = 0 : i64}
    %10 = std.call_runtime @mm_alloc %8, %9
    memref.store %10, w
    %11 = memref.load w : i64
    memref.store_indirect %7, %11+0
    %12 = memref.load w : i64
    %13 = memref.load_indirect %12+0
    %14 = arith.constant {value = 1 : i64}
    %15 = arith.addi %13, %14
    %16 = memref.load __scope_12 : i64
    std.call_runtime @mm_scope_exit %16
    %17 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %17
    func.return %15
  check_0.after:
    %18 = arith.constant {value = 0 : i64}
    %19 = memref.load __scope_8 : i64
    std.call_runtime @mm_scope_exit %19
    func.return %18
  }
  func @memory-safety.main() -> u32 {
  entry:
    %20 = arith.constant {value = 0 : i64}
    %21 = std.call_runtime @mm_scope_enter %20
    memref.store %21, __scope_20
    %22 = arith.constant {value = 5 : i64}
    %23 = func.call @memory-safety.compute %22
    memref.store %23, __range_val_0
    %24 = arith.constant {value = 0 : i64}
    %25 = arith.cmpi lt %23, %24
    %26 = arith.constant {value = 4294967295 : i64}
    %27 = arith.cmpi gt %23, %26
    %28 = arith.ori1 %25, %27
    cf.cond_br %28 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %29 = memref.lea_symdata __panic_msg_28
    %30 = std.ptr_to_i64 %29
    std.call_runtime @maxon_panic %30
  __range_ok_0:
    %31 = memref.load __range_val_0 : i64
    %32 = memref.load __scope_20 : i64
    std.call_runtime @mm_scope_exit %32
    func.return %31
  }
}
=== x86
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    x86.prologue stack_size=48
    x86.mov [rbp-16], ecx
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.xor ecx, ecx
    x86.mov edx, [rbp-16]
    x86.cmp edx, ecx
    x86.jle memory-safety.compute.check_0.after
  check_0:
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-24], eax
    x86.mov ecx, [rbp-16]
    x86.mov edx, 8
    x86.xor ebx, ebx
    x86.mov [rbp-40], ecx
    x86.mov rcx, rdx
    x86.mov rdx, rbx
    x86.call mm_alloc
    x86.mov [rbp-32], eax
    x86.mov esi, [rbp-32]
    x86.mov edi, [rbp-40]
    x86.mov [esi+0], edi
    x86.mov r8, [rbp-32]
    x86.mov r9, [r8+0]
    x86.mov eax, 1
    x86.add r9, eax
    x86.mov eax, [rbp-24]
    x86.mov [rbp-48], r9
    x86.mov rcx, rax
    x86.call mm_scope_exit
    x86.mov rcx, [rbp-8]
    x86.call mm_scope_exit
    x86.mov eax, [rbp-48]
    x86.epilogue
    x86.ret
  check_0.after:
    x86.xor eax, eax
    x86.mov ecx, [rbp-8]
    x86.call mm_scope_exit
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=32
    x86.xor eax, eax
    x86.mov rcx, rax
    x86.call mm_scope_enter
    x86.mov [rbp-8], eax
    x86.mov ecx, 5
    x86.call memory-safety.compute
    x86.mov [rbp-16], eax
    x86.xor edx, edx
    x86.cmp eax, edx
    x86.setl ebx
    x86.movzx ebx, ebxb
    x86.mov rsi, 4294967295
    x86.cmp rax, rsi
    x86.setg edi
    x86.movzx edi, edib
    x86.or ebx, edi
    x86.test ebx, ebx
    x86.je memory-safety.main.__range_ok_0
  __range_panic_0:
    x86.lea_symdata rax, [__panic_msg_28]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-24], eax
    x86.call mm_scope_exit
    x86.mov eax, [rbp-24]
    x86.epilogue
    x86.ret
  }
}
```

