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
    %8 = maxon.literal {value = 42 : i64}
    %9 = maxon.struct_literal @Resource
    maxon.assign %9 {var = r} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.struct_var_ref r
    %11 = maxon.field_access .id %10
    maxon.return %11
  }
  func @memory-safety.main() -> i64 {
  entry:
    %12 = maxon.call @memory-safety.createAndDrop
    maxon.assign %12 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.literal {value = 0 : i64}
    %14 = maxon.binop %12, %13 {op = lt}
    %15 = maxon.literal {value = 4294967295 : i64}
    %16 = maxon.binop %12, %15 {op = gt}
    %17 = maxon.binop %14, %16 {op = or}
    maxon.cond_br %17 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at scope-cleanup.test:14: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %19 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.return %19
  }
}
=== standard
module {
  func @memory-safety.createAndDrop() -> i64 {
  entry:
    %0 = arith.constant {value = 42 : i64}
    %1 = arith.constant {value = 8 : i64}
    %2 = std.call_runtime @maxon_alloc %1
    memref.store %2, r
    %3 = memref.load r : i64
    memref.store_indirect %0, %3+0
    %4 = memref.load r : i64
    %5 = memref.load_indirect %4+0
    %6 = memref.load r : i64
    std.call_runtime @maxon_release %6
    func.return %5
  }
  func @memory-safety.main() -> u32 {
  entry:
    %7 = func.call @memory-safety.createAndDrop
    memref.store %7, __range_val_0
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.cmpi lt %7, %8
    %10 = arith.constant {value = 4294967295 : i64}
    %11 = arith.cmpi gt %7, %10
    %12 = arith.ori1 %9, %11
    cf.cond_br %12 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %13 = memref.lea_symdata __panic_msg_18
    %14 = std.ptr_to_i64 %13
    std.call_runtime @maxon_panic %14
  __range_ok_0:
    %15 = memref.load __range_val_0 : i64
    func.return %15
  }
}
=== x86
module {
  func @memory-safety.createAndDrop() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 42
    x86.mov ecx, 8
    x86.call maxon_alloc
    x86.mov [rbp-8], eax
    x86.mov edx, [rbp-8]
    x86.mov ebx, 42
    x86.mov [edx+0], ebx
    x86.mov esi, [rbp-8]
    x86.mov eax, [esi+0]
    x86.mov edi, [rbp-8]
    x86.mov [rbp-16], eax
    x86.mov rcx, rdi
    x86.call maxon_release
    x86.mov eax, [rbp-16]
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
    x86.lea_symdata rax, [__panic_msg_18]
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
    %13 = maxon.literal {value = 1 : i64}
    %14 = maxon.literal {value = 2 : i64}
    %15 = maxon.struct_literal @Point
    maxon.assign %15 {var = local} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.struct_var_ref local
    maxon.return %16
  }
  func @memory-safety.main() -> i64 {
  entry:
    %17 = maxon.call @memory-safety.makeRef
    maxon.assign %17 {var = p} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.struct_var_ref p
    %19 = maxon.field_access .x %18
    %20 = maxon.string_interp
    maxon.call @stdlib.Print.print %20
    %21 = maxon.literal {value = 0 : i64}
    maxon.return %21
  }
}
=== standard
module {
  func @memory-safety.makeRef() -> i64 {
  entry:
    %0 = arith.constant {value = 1 : i64}
    %1 = arith.constant {value = 2 : i64}
    %2 = arith.constant {value = 16 : i64}
    %3 = std.call_runtime @maxon_alloc %2
    memref.store %3, local
    %4 = memref.load local : i64
    memref.store_indirect %0, %4+0
    %5 = memref.load local : i64
    memref.store_indirect %1, %5+8
    %6 = memref.load local : i64
    func.return %6
  }
  func @memory-safety.main() -> u32 {
  entry:
    %7 = func.call @memory-safety.makeRef
    memref.store %7, p
    %10 = memref.load p : i64
    %11 = memref.load_indirect %10+0
    %12 = arith.constant {value = 21 : i64}
    %13 = std.call_runtime @maxon_alloc %12
    memref.store %13, __tostr_buf_13
    %14 = std.call_runtime @maxon_i64_to_string %11, %13
    %15 = memref.load __tostr_buf_13 : i64
    %16 = arith.constant {value = 1 : i64}
    %17 = arith.addi %14, %16
    %18 = std.call_runtime @maxon_alloc %17
    %19 = arith.constant {value = 0 : i64}
    memref.store %19, __interp_offset_20
    memref.store %18, __interp_buf_20
    memref.store %14, __interp_totallen_20
    memref.store %15, __interp_partbuf_20_0
    memref.store %14, __interp_partlen_20_0
    %20 = memref.load __interp_buf_20 : i64
    %21 = memref.load __interp_offset_20 : i64
    %22 = arith.addi %20, %21
    %23 = memref.load __interp_partbuf_20_0 : i64
    %24 = memref.load __interp_partlen_20_0 : i64
    std.memcopy %23, %22, %24
    %28 = memref.load __interp_buf_20 : i64
    %29 = memref.load __interp_totallen_20 : i64
    %30 = arith.addi %28, %29
    %31 = arith.constant {value = 0 : i64}
    memref.store_indirect %31, %30+0
    %32 = memref.load __tostr_buf_13 : i64
    std.call_runtime @maxon_free %32
    %33 = arith.constant {value = 32 : i64}
    %34 = std.call_runtime @maxon_alloc %33
    memref.store %34, __interp_managed_20
    %35 = memref.load __interp_buf_20 : i64
    %36 = memref.load __interp_managed_20 : i64
    memref.store_indirect %35, %36+0
    %37 = memref.load __interp_totallen_20 : i64
    %38 = memref.load __interp_managed_20 : i64
    memref.store_indirect %37, %38+8
    %39 = memref.load __interp_managed_20 : i64
    memref.store_indirect %37, %39+16
    %40 = arith.constant {value = 1 : i64}
    %41 = memref.load __interp_managed_20 : i64
    memref.store_indirect %40, %41+24
    %42 = arith.constant {value = 16 : i64}
    %43 = std.call_runtime @maxon_alloc %42
    memref.store %43, __interptmp_20
    %44 = memref.load __interp_managed_20 : i64
    %45 = memref.load __interptmp_20 : i64
    memref.store_indirect %44, %45+0
    %46 = arith.constant {value = 0 : i64}
    %47 = memref.load __interptmp_20 : i64
    memref.store_indirect %46, %47+8
    %48 = memref.load __interptmp_20 : i64
    func.call @stdlib.Print.print %48
    %49 = memref.load __interptmp_20 : i64
    %50 = arith.constant {value = 0 : i64}
    std.call_runtime @maxon_release_with_managed %49, %50
    %51 = arith.constant {value = 0 : i64}
    %52 = memref.load p : i64
    std.call_runtime @maxon_release %52
    func.return %51
  }
}
=== x86
module {
  func @memory-safety.makeRef() -> i64 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 1
    x86.mov ecx, 2
    x86.mov edx, 16
    x86.mov rcx, rdx
    x86.call maxon_alloc
    x86.mov [rbp-8], eax
    x86.mov ebx, [rbp-8]
    x86.mov esi, 1
    x86.mov [ebx+0], esi
    x86.mov edi, [rbp-8]
    x86.mov r8, 2
    x86.mov [edi+8], r8
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=96
    x86.call memory-safety.makeRef
    x86.mov [rbp-8], eax
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+0]
    x86.mov edx, 21
    x86.mov [rbp-80], ecx
    x86.mov rcx, rdx
    x86.call maxon_alloc
    x86.mov [rbp-16], eax
    x86.mov rcx, [rbp-80]
    x86.mov rdx, rax
    x86.call maxon_i64_to_string
    x86.mov ebx, [rbp-16]
    x86.mov esi, 1
    x86.lea edi, [eax + esi]
    x86.mov [rbp-88], eax
    x86.mov [rbp-96], ebx
    x86.mov rcx, rdi
    x86.call maxon_alloc
    x86.xor r8, r8
    x86.mov [rbp-24], r8
    x86.mov [rbp-32], eax
    x86.mov r9, [rbp-88]
    x86.mov [rbp-40], r9
    x86.mov eax, [rbp-96]
    x86.mov [rbp-48], eax
    x86.mov [rbp-56], r9
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-24]
    x86.add eax, ecx
    x86.mov ecx, [rbp-48]
    x86.mov edx, [rbp-56]
    x86.mov rsi, ecx
    x86.mov rdi, eax
    x86.mov rcx, edx
    x86.rep_movsb
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-40]
    x86.add eax, ecx
    x86.xor ecx, ecx
    x86.mov byte ptr [eax+0], ecxb
    x86.mov eax, [rbp-16]
    x86.mov rcx, rax
    x86.call maxon_free
    x86.mov eax, 32
    x86.mov rcx, rax
    x86.call maxon_alloc
    x86.mov [rbp-64], eax
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-64]
    x86.mov [ecx+0], eax
    x86.mov eax, [rbp-40]
    x86.mov ecx, [rbp-64]
    x86.mov [ecx+8], eax
    x86.mov ecx, [rbp-64]
    x86.mov [ecx+16], eax
    x86.mov eax, 1
    x86.mov ecx, [rbp-64]
    x86.mov [ecx+24], eax
    x86.mov eax, 16
    x86.mov rcx, rax
    x86.call maxon_alloc
    x86.mov [rbp-72], eax
    x86.mov eax, [rbp-64]
    x86.mov ecx, [rbp-72]
    x86.mov [ecx+0], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-72]
    x86.mov [ecx+8], eax
    x86.mov eax, [rbp-72]
    x86.mov rcx, rax
    x86.call stdlib.Print.print
    x86.mov eax, [rbp-72]
    x86.xor ecx, ecx
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.call maxon_release_with_managed
    x86.xor eax, eax
    x86.mov ecx, [rbp-8]
    x86.call maxon_release
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
    %13 = maxon.literal {value = 0 : i64}
    maxon.assign %13 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %14 = maxon.literal {value = 1 : i1}
    maxon.cond_br %14 [then: block_0, else: block_0.merge]
  block_0:
    %15 = maxon.literal {value = 10 : i64}
    %16 = maxon.literal {value = 20 : i64}
    %17 = maxon.struct_literal @Point
    maxon.assign %17 {var = p} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.struct_var_ref p
    %19 = maxon.field_access .x %18
    maxon.assign %19 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.release {var = p} {type = Point}
    maxon.br block_0.merge
  block_0.merge:
    %20 = maxon.var_ref {var = result} {type = i64}
    maxon.assign %20 {var = __range_val_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %21 = maxon.literal {value = 0 : i64}
    %22 = maxon.binop %20, %21 {op = lt}
    %23 = maxon.literal {value = 4294967295 : i64}
    %24 = maxon.binop %20, %23 {op = gt}
    %25 = maxon.binop %22, %24 {op = or}
    maxon.cond_br %25 [then: __range_panic_1, else: __range_ok_1]
  __range_panic_1:
    maxon.panic "panic at block-scope-struct-release.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_1:
    %27 = maxon.var_ref {var = __range_val_1} {type = i64}
    maxon.return %27
  }
}
=== standard
module {
  func @memory-safety.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, p
    %1 = arith.constant {value = 0 : i64}
    memref.store %1, result
    %2 = arith.constant {value = 1 : i1}
    cf.cond_br %2 [then: block_0, else: block_0.merge]
  block_0:
    %3 = arith.constant {value = 10 : i64}
    %4 = arith.constant {value = 20 : i64}
    %5 = arith.constant {value = 16 : i64}
    %6 = std.call_runtime @maxon_alloc %5
    memref.store %6, p
    %7 = memref.load p : i64
    memref.store_indirect %3, %7+0
    %8 = memref.load p : i64
    memref.store_indirect %4, %8+8
    %9 = memref.load p : i64
    %10 = memref.load_indirect %9+0
    memref.store %10, result
    %11 = memref.load p : i64
    std.call_runtime @maxon_release %11
    %12 = arith.constant {value = 0 : i64}
    memref.store %12, p
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
    %19 = memref.lea_symdata __panic_msg_26
    %20 = std.ptr_to_i64 %19
    std.call_runtime @maxon_panic %20
  __range_ok_1:
    %21 = memref.load __range_val_1 : i64
    %22 = memref.load p : i64
    std.call_runtime @maxon_release %22
    func.return %21
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
    x86.mov edx, 1
    x86.test edx, edx
    x86.je memory-safety.main.block_0.merge
  block_0:
    x86.mov eax, 10
    x86.mov ecx, 20
    x86.mov edx, 16
    x86.mov rcx, rdx
    x86.call maxon_alloc
    x86.mov [rbp-8], eax
    x86.mov ebx, [rbp-8]
    x86.mov esi, 10
    x86.mov [ebx+0], esi
    x86.mov edi, [rbp-8]
    x86.mov r8, 20
    x86.mov [edi+8], r8
    x86.mov r9, [rbp-8]
    x86.mov eax, [r9+0]
    x86.mov [rbp-16], eax
    x86.mov eax, [rbp-8]
    x86.mov rcx, rax
    x86.call maxon_release
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.jmp memory-safety.main.block_0.merge
  block_0.merge:
    x86.mov eax, [rbp-16]
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
    x86.lea_symdata rax, [__panic_msg_26]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_1:
    x86.mov eax, [rbp-24]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-32], eax
    x86.call maxon_release
    x86.mov eax, [rbp-32]
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
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.literal {value = 0 : i64}
    %11 = maxon.literal {value = 0 : i64}
    %12 = maxon.literal {value = 8 : i64}
    %13 = maxon.struct_literal @__ManagedMemory
    %14 = maxon.struct_literal @ItemArray
    maxon.assign %14 {var = arr} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.literal {value = 7 : i64}
    %16 = maxon.struct_literal @Item
    maxon.assign %16 {var = item} {decl = 1 : i1} {mut = 1 : i1}
    %17 = maxon.struct_var_ref item
    maxon.call @ItemArray.push %14, %17
    %18 = maxon.struct_var_ref arr
    %19 = maxon.literal {value = 0 : i64}
    %22, %21 = maxon.try_call @ItemArray.get %18, %19
    %23 = maxon.literal {value = 0 : i64}
    %24 = maxon.struct_literal @Item
    maxon.assign %24 {var = __try_default_1} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %22 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    %25 = maxon.literal {value = 0 : i64}
    %26 = maxon.binop %21, %25 {op = ne}
    maxon.cond_br %26 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %27 = maxon.struct_var_ref __try_default_1
    maxon.assign %27 {var = __try_result_0} {mut = 1 : i1}
    maxon.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    maxon.release {var = __try_default_1} {type = Item}
    maxon.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %28 = maxon.struct_var_ref __try_result_0
    maxon.assign %28 {var = got} {decl = 1 : i1} {mut = 1 : i1}
    %29 = maxon.struct_var_ref got
    %30 = maxon.field_access .value %29
    maxon.assign %30 {var = __range_val_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %31 = maxon.literal {value = 0 : i64}
    %32 = maxon.binop %30, %31 {op = lt}
    %33 = maxon.literal {value = 4294967295 : i64}
    %34 = maxon.binop %30, %33 {op = gt}
    %35 = maxon.binop %32, %34 {op = or}
    maxon.cond_br %35 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    maxon.panic "panic at array-push-struct-incref.test:15: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_5:
    %37 = maxon.var_ref {var = __range_val_5} {type = i64}
    maxon.return %37
  }
}
=== standard
module {
  func @memory-safety.main() -> u32 {
  entry:
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.constant {value = 0 : i64}
    %5 = arith.constant {value = 8 : i64}
    %6 = arith.constant {value = 32 : i64}
    %7 = std.call_runtime @maxon_alloc %6
    memref.store %7, __struct_13
    %8 = memref.load __struct_13 : i64
    memref.store_indirect %2, %8+0
    %9 = memref.load __struct_13 : i64
    memref.store_indirect %3, %9+8
    %10 = memref.load __struct_13 : i64
    memref.store_indirect %4, %10+16
    %11 = memref.load __struct_13 : i64
    memref.store_indirect %5, %11+24
    %12 = arith.constant {value = 16 : i64}
    %13 = std.call_runtime @maxon_alloc %12
    memref.store %13, arr
    %14 = memref.load arr : i64
    memref.store_indirect %1, %14+0
    %15 = memref.load __struct_13 : i64
    %16 = memref.load arr : i64
    memref.store_indirect %15, %16+8
    %17 = arith.constant {value = 7 : i64}
    %18 = arith.constant {value = 8 : i64}
    %19 = std.call_runtime @maxon_alloc %18
    memref.store %19, item
    %20 = memref.load item : i64
    memref.store_indirect %17, %20+0
    %21 = memref.load arr : i64
    %22 = memref.load item : i64
    func.call @ItemArray.push %21, %22
    %23 = arith.constant {value = 0 : i64}
    %24 = memref.load arr : i64
    %25, %26 = func.try_call @ItemArray.get %24, %23
    %27 = arith.constant {value = 0 : i64}
    %28 = arith.constant {value = 8 : i64}
    %29 = std.call_runtime @maxon_alloc %28
    memref.store %29, __try_default_1
    %30 = memref.load __try_default_1 : i64
    memref.store_indirect %27, %30+0
    memref.store %25, __try_result_0
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.cmpi ne %26, %33
    cf.cond_br %34 [then: otherwise_default_error_2, else: otherwise_default_cleanup_4]
  otherwise_default_error_2:
    %35 = memref.load __try_result_0 : i64
    std.call_runtime @maxon_release %35
    %36 = memref.load __try_default_1 : i64
    memref.store %36, __try_result_0
    cf.br otherwise_default_continue_3
  otherwise_default_cleanup_4:
    %37 = memref.load __try_default_1 : i64
    std.call_runtime @maxon_release %37
    cf.br otherwise_default_continue_3
  otherwise_default_continue_3:
    %38 = memref.load __try_result_0 : i64
    memref.store %38, got
    %39 = memref.load got : i64
    %40 = memref.load_indirect %39+0
    memref.store %40, __range_val_5
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.cmpi lt %40, %41
    %43 = arith.constant {value = 4294967295 : i64}
    %44 = arith.cmpi gt %40, %43
    %45 = arith.ori1 %42, %44
    cf.cond_br %45 [then: __range_panic_5, else: __range_ok_5]
  __range_panic_5:
    %46 = memref.lea_symdata __panic_msg_36
    %47 = std.ptr_to_i64 %46
    std.call_runtime @maxon_panic %47
  __range_ok_5:
    %48 = memref.load __range_val_5 : i64
    %49 = memref.load arr : i64
    std.call_runtime @maxon_release_array_of_simple %49
    %50 = memref.load item : i64
    std.call_runtime @maxon_release %50
    %51 = memref.load got : i64
    std.call_runtime @maxon_release %51
    func.return %48
  }
}
=== x86
module {
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=80
    x86.xor eax, eax
    x86.xor ecx, ecx
    x86.xor edx, edx
    x86.xor ebx, ebx
    x86.mov esi, 8
    x86.mov edi, 32
    x86.mov rcx, rdi
    x86.call maxon_alloc
    x86.mov [rbp-8], eax
    x86.mov r8, [rbp-8]
    x86.xor r9, r9
    x86.mov [r8+0], r9
    x86.mov eax, [rbp-8]
    x86.xor ecx, ecx
    x86.mov [eax+8], ecx
    x86.mov eax, [rbp-8]
    x86.xor ecx, ecx
    x86.mov [eax+16], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, 8
    x86.mov [eax+24], ecx
    x86.mov eax, 16
    x86.mov rcx, rax
    x86.call maxon_alloc
    x86.mov [rbp-16], eax
    x86.mov eax, [rbp-16]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-8]
    x86.mov ecx, [rbp-16]
    x86.mov [ecx+8], eax
    x86.mov eax, 7
    x86.mov ecx, 8
    x86.call maxon_alloc
    x86.mov [rbp-24], eax
    x86.mov eax, [rbp-24]
    x86.mov ecx, 7
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-16]
    x86.mov ecx, [rbp-24]
    x86.mov rdx, rcx
    x86.mov rcx, rax
    x86.call ItemArray.push
    x86.xor eax, eax
    x86.mov ecx, [rbp-16]
    x86.mov rdx, rax
    x86.call ItemArray.get
    x86.xor ecx, ecx
    x86.mov ebx, 8
    x86.mov [rbp-64], eax
    x86.mov [rbp-72], edx
    x86.mov rcx, rbx
    x86.call maxon_alloc
    x86.mov [rbp-32], eax
    x86.mov eax, [rbp-32]
    x86.xor ecx, ecx
    x86.mov [eax+0], ecx
    x86.mov eax, [rbp-64]
    x86.mov [rbp-40], eax
    x86.xor eax, eax
    x86.mov ecx, [rbp-72]
    x86.cmp ecx, eax
    x86.je memory-safety.main.otherwise_default_cleanup_4
  otherwise_default_error_2:
    x86.mov rcx, [rbp-40]
    x86.call maxon_release
    x86.mov ecx, [rbp-32]
    x86.mov [rbp-40], ecx
    x86.jmp memory-safety.main.otherwise_default_continue_3
  otherwise_default_cleanup_4:
    x86.mov rcx, [rbp-32]
    x86.call maxon_release
    x86.jmp memory-safety.main.otherwise_default_continue_3
  otherwise_default_continue_3:
    x86.mov eax, [rbp-40]
    x86.mov [rbp-48], eax
    x86.mov ecx, [rbp-48]
    x86.mov edx, [ecx+0]
    x86.mov [rbp-56], edx
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
    x86.lea_symdata rax, [__panic_msg_36]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_5:
    x86.mov eax, [rbp-56]
    x86.mov ecx, [rbp-16]
    x86.mov [rbp-64], eax
    x86.call maxon_release_array_of_simple
    x86.mov rcx, [rbp-24]
    x86.call maxon_release
    x86.mov ecx, [rbp-48]
    x86.call maxon_release
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
    %8 = maxon.literal {value = 0 : i64}
    maxon.assign %8 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.literal {value = 0 : i64}
    maxon.assign %9 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %10 = maxon.literal {value = 3 : i64}
    %11 = maxon.var_ref {var = i} {type = i64}
    %12 = maxon.binop %11, %10 {op = lt}
    maxon.cond_br %12 [then: loop_0, else: loop_0.exit]
  loop_0:
    %13 = maxon.var_ref {var = i} {type = i64}
    %14 = maxon.struct_literal @Counter
    maxon.assign %14 {var = c} {decl = 1 : i1} {mut = 1 : i1}
    %15 = maxon.struct_var_ref c
    %16 = maxon.field_access .n %15
    %17 = maxon.literal {value = 1 : i64}
    %18 = maxon.binop %16, %17 {op = eq}
    maxon.cond_br %18 [then: check_1, else: check_1.after]
  check_1:
    %19 = maxon.struct_var_ref c
    %20 = maxon.field_access .n %19
    maxon.assign %20 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.release {var = c} {type = Counter}
    maxon.br loop_0.exit
  check_1.after:
    %21 = maxon.literal {value = 1 : i64}
    %22 = maxon.var_ref {var = i} {type = i64}
    %23 = maxon.binop %22, %21 {op = add}
    maxon.assign %23 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.release {var = c} {type = Counter}
    maxon.br loop_0.header
  loop_0.exit:
    %24 = maxon.var_ref {var = result} {type = i64}
    maxon.assign %24 {var = __range_val_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %25 = maxon.literal {value = 0 : i64}
    %26 = maxon.binop %24, %25 {op = lt}
    %27 = maxon.literal {value = 4294967295 : i64}
    %28 = maxon.binop %24, %27 {op = gt}
    %29 = maxon.binop %26, %28 {op = or}
    maxon.cond_br %29 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    maxon.panic "panic at release-before-break.test:19: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_2:
    %31 = maxon.var_ref {var = __range_val_2} {type = i64}
    maxon.return %31
  }
}
=== standard
module {
  func @memory-safety.main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, c
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
    %6 = memref.load i : i64
    %7 = arith.constant {value = 8 : i64}
    %8 = std.call_runtime @maxon_alloc %7
    memref.store %8, c
    %9 = memref.load c : i64
    memref.store_indirect %6, %9+0
    %10 = memref.load c : i64
    %11 = memref.load_indirect %10+0
    %12 = arith.constant {value = 1 : i64}
    %13 = arith.cmpi eq %11, %12
    cf.cond_br %13 [then: check_1, else: check_1.after]
  check_1:
    %14 = memref.load c : i64
    %15 = memref.load_indirect %14+0
    memref.store %15, result
    %16 = memref.load c : i64
    std.call_runtime @maxon_release %16
    %17 = arith.constant {value = 0 : i64}
    memref.store %17, c
    cf.br loop_0.exit
  check_1.after:
    %18 = arith.constant {value = 1 : i64}
    %19 = memref.load i : i64
    %20 = arith.addi %19, %18
    memref.store %20, i
    %21 = memref.load c : i64
    std.call_runtime @maxon_release %21
    %22 = arith.constant {value = 0 : i64}
    memref.store %22, c
    cf.br loop_0.header
  loop_0.exit:
    %23 = memref.load result : i64
    memref.store %23, __range_val_2
    %24 = arith.constant {value = 0 : i64}
    %25 = arith.cmpi lt %23, %24
    %26 = arith.constant {value = 4294967295 : i64}
    %27 = arith.cmpi gt %23, %26
    %28 = arith.ori1 %25, %27
    cf.cond_br %28 [then: __range_panic_2, else: __range_ok_2]
  __range_panic_2:
    %29 = memref.lea_symdata __panic_msg_30
    %30 = std.ptr_to_i64 %29
    std.call_runtime @maxon_panic %30
  __range_ok_2:
    %31 = memref.load __range_val_2 : i64
    %32 = memref.load c : i64
    std.call_runtime @maxon_release %32
    func.return %31
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
    x86.xor edx, edx
    x86.mov [rbp-24], edx
    x86.jmp memory-safety.main.loop_0.header
  loop_0.header:
    x86.mov eax, 3
    x86.mov ecx, [rbp-24]
    x86.cmp ecx, eax
    x86.jge memory-safety.main.loop_0.exit
  loop_0:
    x86.mov eax, [rbp-24]
    x86.mov ecx, 8
    x86.mov [rbp-40], eax
    x86.call maxon_alloc
    x86.mov [rbp-8], eax
    x86.mov edx, [rbp-8]
    x86.mov ebx, [rbp-40]
    x86.mov [edx+0], ebx
    x86.mov esi, [rbp-8]
    x86.mov edi, [esi+0]
    x86.mov r8, 1
    x86.cmp edi, r8
    x86.jne memory-safety.main.check_1.after
  check_1:
    x86.mov eax, [rbp-8]
    x86.mov ecx, [eax+0]
    x86.mov [rbp-16], ecx
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call maxon_release
    x86.xor ebx, ebx
    x86.mov [rbp-8], ebx
    x86.jmp memory-safety.main.loop_0.exit
  check_1.after:
    x86.mov eax, 1
    x86.mov ecx, [rbp-24]
    x86.add ecx, eax
    x86.mov [rbp-24], ecx
    x86.mov edx, [rbp-8]
    x86.mov rcx, rdx
    x86.call maxon_release
    x86.xor ebx, ebx
    x86.mov [rbp-8], ebx
    x86.jmp memory-safety.main.loop_0.header
  loop_0.exit:
    x86.mov eax, [rbp-16]
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
    x86.lea_symdata rax, [__panic_msg_30]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_2:
    x86.mov eax, [rbp-32]
    x86.mov ecx, [rbp-8]
    x86.mov [rbp-40], eax
    x86.call maxon_release
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
    %8 = maxon.param {index = 0 : i32} {name = flag} {type = i64}
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.binop %8, %9 {op = gt} {optimalType = i64}
    maxon.cond_br %10 [then: check_0, else: check_0.after]
  check_0:
    %11 = maxon.var_ref {var = flag} {type = i64}
    %12 = maxon.struct_literal @Wrapper
    maxon.assign %12 {var = w} {decl = 1 : i1} {mut = 1 : i1}
    %13 = maxon.struct_var_ref w
    %14 = maxon.field_access .val %13
    %15 = maxon.literal {value = 1 : i64}
    %16 = maxon.binop %14, %15 {op = add}
    maxon.release {var = w} {type = Wrapper}
    maxon.return %16
  check_0.after:
    %17 = maxon.literal {value = 0 : i64}
    maxon.return %17
  }
  func @memory-safety.main() -> i64 {
  entry:
    %18 = maxon.literal {value = 5 : i64}
    %19 = maxon.call @memory-safety.compute %18
    maxon.assign %19 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %19, %20 {op = lt}
    %22 = maxon.literal {value = 4294967295 : i64}
    %23 = maxon.binop %19, %22 {op = gt}
    %24 = maxon.binop %21, %23 {op = or}
    maxon.cond_br %24 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at release-before-return-in-block.test:17: Range check failed for type 'ExitCode': value outside int(0 to 4294967295)"
  __range_ok_0:
    %26 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.return %26
  }
}
=== standard
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    memref.store %0, w
    %1 = func.param flag : StdI64
    memref.store %1, flag
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.cmpi gt %1, %2
    cf.cond_br %3 [then: check_0, else: check_0.after]
  check_0:
    %4 = memref.load flag : i64
    %5 = arith.constant {value = 8 : i64}
    %6 = std.call_runtime @maxon_alloc %5
    memref.store %6, w
    %7 = memref.load w : i64
    memref.store_indirect %4, %7+0
    %8 = memref.load w : i64
    %9 = memref.load_indirect %8+0
    %10 = arith.constant {value = 1 : i64}
    %11 = arith.addi %9, %10
    %12 = memref.load w : i64
    std.call_runtime @maxon_release %12
    %13 = arith.constant {value = 0 : i64}
    memref.store %13, w
    %14 = memref.load w : i64
    std.call_runtime @maxon_release %14
    func.return %11
  check_0.after:
    %15 = arith.constant {value = 0 : i64}
    %16 = memref.load w : i64
    std.call_runtime @maxon_release %16
    func.return %15
  }
  func @memory-safety.main() -> u32 {
  entry:
    %17 = arith.constant {value = 5 : i64}
    %18 = func.call @memory-safety.compute %17
    memref.store %18, __range_val_0
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 4294967295 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %24 = memref.lea_symdata __panic_msg_25
    %25 = std.ptr_to_i64 %24
    std.call_runtime @maxon_panic %25
  __range_ok_0:
    %26 = memref.load __range_val_0 : i64
    func.return %26
  }
}
=== x86
module {
  func @memory-safety.compute(flag: i64) -> i64 {
  entry:
    x86.prologue stack_size=32
    x86.mov [rbp-16], ecx
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.xor eax, eax
    x86.cmp ecx, eax
    x86.jle memory-safety.compute.check_0.after
  check_0:
    x86.mov eax, [rbp-16]
    x86.mov ecx, 8
    x86.mov [rbp-24], eax
    x86.call maxon_alloc
    x86.mov [rbp-8], eax
    x86.mov edx, [rbp-8]
    x86.mov ebx, [rbp-24]
    x86.mov [edx+0], ebx
    x86.mov esi, [rbp-8]
    x86.mov edi, [esi+0]
    x86.mov r8, 1
    x86.add edi, r8
    x86.mov r9, [rbp-8]
    x86.mov [rbp-32], edi
    x86.mov rcx, r9
    x86.call maxon_release
    x86.xor eax, eax
    x86.mov [rbp-8], eax
    x86.mov rcx, [rbp-8]
    x86.call maxon_release
    x86.mov eax, [rbp-32]
    x86.epilogue
    x86.ret
  check_0.after:
    x86.xor eax, eax
    x86.mov ecx, [rbp-8]
    x86.call maxon_release
    x86.xor eax, eax
    x86.epilogue
    x86.ret
  }
  func @memory-safety.main() -> u32 {
  entry:
    x86.prologue stack_size=16
    x86.mov eax, 5
    x86.mov rcx, rax
    x86.call memory-safety.compute
    x86.mov [rbp-8], eax
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
    x86.lea_symdata rax, [__panic_msg_25]
    x86.mov rcx, rax
    x86.call maxon_panic
  __range_ok_0:
    x86.mov eax, [rbp-8]
    x86.epilogue
    x86.ret
  }
}
```

