---
title: Memory Model
description: Reference-by-default semantics, cloning, auto-equatable, scope cleanup, and code generation.
sidebar:
  order: 15
---

### Reference-by-Default Assignment

Assigning a struct-typed variable to another variable copies the **heap pointer**, creating a reference (alias) to the same object:

```maxon
type Point
	export var x as int
	export var y as int
end 'Point'

var a = Point{x: 1, y: 2}
var b = a               // b is an alias for a -- both point to the same object
b.x = 99
print("{a.x}")          // 99 -- a and b share the same object
```

Field mutation through an alias affects the original, because both variables point to the same heap-allocated object. Reassignment, however, rebinds the variable to a new object without affecting the original:

```maxon
var a = Point{x: 1, y: 2}
var b = a               // alias
b = Point{x: 5, y: 6}  // rebinds b to a new object -- a is unaffected
print("{a.x}")          // 1 -- a still points to the original
```

All types in Maxon use reference semantics on assignment — the variable is rebound to a new value only when reassigned with an expression. The practical distinction is that struct field mutations are visible through aliases, whereas arithmetic on ranged integers and primitives always produces a new value and rebinds the variable, leaving the original unchanged.

### Stack Promotion

The compiler performs escape analysis to identify struct literals that can be safely stack-allocated instead of heap-allocated. A struct is promoted to the stack when all of the following are true:

- All fields are primitive types (no heap-allocated field types)
- Neither the variable nor any alias escapes the function (not returned, stored into a heap field, captured by a closure, or passed to a function that escapes it)
- The `@heap` directive is not used

Stack-promoted structs are freed automatically when the stack frame is reclaimed — no reference counting overhead is incurred. This optimization is transparent and preserves the same semantics as heap allocation.

The `@heap` annotation forces a struct to be heap-allocated, bypassing stack promotion:

```maxon
@heap var p = Point{x: 1, y: 2}  // always heap-allocated
```

`@heap` is only valid on `var` or `let` declarations with struct literal initializers.

### Explicit Cloning

To create an independent deep copy of a struct, use the `.clone()` method:

```maxon
var a = Point{x: 1, y: 2}
var b = a.clone()       // deep copy -- b is independent of a
b.x = 99
print("{a.x}")          // 1 -- a is unchanged
```

The type must implement the `Cloneable` interface to use `.clone()`. See [Cloneable Interface](#cloneable-interface) below.

### Cloneable Interface

The `Cloneable` interface is defined in the standard library:

```maxon
interface Cloneable
	function clone() returns Self
end 'Cloneable'
```

**Auto-conformance:** The compiler automatically generates `Cloneable` conformance for any struct whose fields are all Cloneable types. You do not need to write the conformance manually unless you need custom clone behavior.

**Built-in Cloneable types:**
- All primitives (`int`, `float`, `bool`, `byte`)
- `String`
- `Array` (when the element type is Cloneable)

**When auto-conformance fails:** If a struct contains a field whose type is not Cloneable (such as an enum with associated values), the compiler will not auto-generate conformance. You must implement `clone()` manually or restructure the type.

### Auto-Equatable

The compiler also auto-generates `Equatable` conformance for structs whose fields all implement `Equatable`. The synthesized `equals()` method compares each field using `==` (for primitives) or `.equals()` (for nested structs).

```maxon
type Point
	export var x as int
	export var y as int
end 'Point'

// Point auto-conforms to Equatable (all fields are primitive)
var a = Point{x: 1, y: 2}
var b = Point{x: 1, y: 2}
if a == b 'equal'           // true -- content equality
	print("equal")
end 'equal'
```

If a struct contains a field that doesn't implement `Equatable` (such as a function type), using `==` produces error E3069.

To compare reference identity (whether two variables point to the same heap object), use the `is` operator:

```maxon
var a = Point{x: 1, y: 2}
var b = a.clone()           // deep copy
var c = a                   // alias (reference to same object)
a is b                      // false -- different objects
a is c                      // true -- same object
```

### Scope Cleanup

When a struct variable goes out of scope, the compiler automatically releases its heap allocation. The runtime uses reference counting: each heap allocation has a refcount header. When a reference is created (via assignment), the refcount is incremented. When a variable goes out of scope, the runtime decrements the refcount and frees the memory if it reaches zero.

```maxon
function compute() returns int
	var a = Point{x: 10, y: 20}  // allocated on heap, refcount = 1
	var b = Point{x: 30, y: 40}  // allocated on heap, refcount = 1
	return a.x + b.y              // a and b released here (refcount -> 0 -> freed)
end 'compute'
```

**Return values:** When a struct is returned from a function, the returned variable is not released at scope exit — the caller takes responsibility for its lifetime.

```maxon
function makePoint() returns Point
	var p = Point{x: 1, y: 2}
	return p                      // p is NOT freed; caller is responsible
end 'makePoint'
```

**Container cleanup:** Containers with heap-allocated elements (e.g., `List with MyStruct`) perform deep cleanup when freed. Each element's refcount is decremented, and elements whose refcount reaches zero are freed recursively. For `List`, the compiler walks all managed list nodes and decrefs their stored values before freeing the managed list itself.

```maxon
typealias TokenList = List with Token

function example() returns int
	var list = TokenList.create()
	list.append(Token{id: 1})   // Token incref'd by the managed list node
	list.append(Token{id: 2})   // Token incref'd by the managed list node
	return 0                     // list freed: each Token decref'd (rc→0→freed),
															 // then managed list nodes freed, then managed list freed
end 'example'
```

### Stack Allocation
- Local variables (`var`, `let`)
- Function parameters
- Automatic lifetime (scope-based)

### Heap Allocation
- Arrays (all array types)
- Strings (when dynamically allocated)
- Automatically freed at end of scope
- No manual `free` or garbage collector needed

### Borrow Checking

The borrow checker prevents mutation of a collection while references to its elements are alive. When you obtain a reference from a mutable source (e.g., `var s = try arr.get(0) otherwise ""`), that source cannot be mutated until the reference is no longer used.

Borrows use non-lexical lifetimes (NLL): a borrow ends at the last use of the borrowing variable, not at the end of its scope.

```maxon
var arr = ["hello"]
var s = try arr.get(0) otherwise ""
arr.push("world")    // ERROR E3070: cannot mutate 'arr' via 'push' while it is borrowed by 's'
```

```maxon
var arr = ["hello"]
var s = try arr.get(0) otherwise ""
print("{s}\n")        // last use of s — borrow expires here
arr.push("world")    // OK: borrow has expired
```

The borrow checker also detects indirect mutation through helper functions:

```maxon
function clearList(list StringList)
	list.clear()
end 'clearList'

var val = try list.first() otherwise "none"
clearList(list)       // ERROR E3070: cannot mutate 'list' via 'clearList' while it is borrowed by 'val'
```

### Safety
- No bounds checking on arrays
- No null checks
- Use-after-move prevented at compile time (see Ownership System above)
- Mutation of borrowed collections prevented at compile time (see Borrow Checking above)

### Calling Convention
- Parameters that are only read are passed by value (simple types in registers, arrays as pointer)
- Parameters that are assigned to inside the callee are passed by reference (pointer to caller's storage)
- Return values passed by value (in register or stack)

---

## Code Generation

### Native Backends
- Maxon uses custom from-scratch backends (no LLVM dependency)
- Generates standalone native executables directly

### Optimizations
- Constant folding
- Dead code elimination

### Runtime Library
- Provides implementations for intrinsic functions
- Auto-linked with all programs
- No C runtime dependency
