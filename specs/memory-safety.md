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
    %9 = memref.load p : i64
    %10 = memref.load_indirect %9+0
    %11 = arith.constant {value = 21 : i64}
    %12 = std.call_runtime @maxon_alloc %11
    memref.store %12, __tostr_buf_12
    %13 = std.call_runtime @maxon_i64_to_string %10, %12
    %14 = memref.load __tostr_buf_12 : i64
    %15 = arith.constant {value = 1 : i64}
    %16 = arith.addi %13, %15
    %17 = std.call_runtime @maxon_alloc %16
    %18 = arith.constant {value = 0 : i64}
    memref.store %18, __interp_offset_20
    memref.store %17, __interp_buf_20
    memref.store %13, __interp_totallen_20
    memref.store %14, __interp_partbuf_20_0
    memref.store %13, __interp_partlen_20_0
    %19 = memref.load __interp_buf_20 : i64
    %20 = memref.load __interp_offset_20 : i64
    %21 = arith.addi %19, %20
    %22 = memref.load __interp_partbuf_20_0 : i64
    %23 = memref.load __interp_partlen_20_0 : i64
    std.memcopy %22, %21, %23
    %27 = arith.constant {value = 32 : i64}
    %28 = std.call_runtime @maxon_alloc %27
    memref.store %28, __interp_managed_20
    %29 = memref.load __interp_buf_20 : i64
    %30 = memref.load __interp_managed_20 : i64
    memref.store_indirect %29, %30+0
    %31 = memref.load __interp_totallen_20 : i64
    %32 = memref.load __interp_managed_20 : i64
    memref.store_indirect %31, %32+8
    %33 = memref.load __interp_managed_20 : i64
    memref.store_indirect %31, %33+16
    %34 = arith.constant {value = 1 : i64}
    %35 = memref.load __interp_managed_20 : i64
    memref.store_indirect %34, %35+24
    %36 = arith.constant {value = 16 : i64}
    %37 = std.call_runtime @maxon_alloc %36
    memref.store %37, __interptmp_20
    %38 = memref.load __interp_managed_20 : i64
    %39 = memref.load __interptmp_20 : i64
    memref.store_indirect %38, %39+0
    %40 = arith.constant {value = 0 : i64}
    %41 = memref.load __interptmp_20 : i64
    memref.store_indirect %40, %41+8
    %42 = memref.load __interptmp_20 : i64
    func.call @stdlib.Print.print %42
    %43 = arith.constant {value = 0 : i64}
    %44 = memref.load p : i64
    std.call_runtime @maxon_release %44
    func.return %43
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

