---
title: Variables
description: Mutable (var) and immutable (let) bindings, including top-level variables.
sidebar:
  order: 6
---

### Mutable Variables (`var`)
```maxon
var x = 42              // Type inferred
x = x + 5               // Reassignment allowed
```

### Immutable Variables (`let`)
```maxon
let pi = 3.14159        // Cannot be reassigned
let name = "Maxon"
// pi = 3.14            // ERROR: Cannot assign to immutable variable
```

Calling a mutating method on a `let` variable is a compile error. Mutating methods include `push`, `pop`, `set`, `remove`, `clear`, `append`, `insert`, `resize`, `reserve`, `setLength`, `grow`, `upsert`, and similar methods that modify the receiver's state. Use `var` for variables that need mutation:

```maxon
let items = Array with int{}
// items.push(1)        // ERROR: cannot call mutating method 'push' on immutable variable
var items2 = Array with int{}
items2.push(1)           // OK — items2 is var
```

**Rules:**
- All variables must be initialized at declaration
- Type is always inferred from the initializer
- Scope is block-scoped
- Primitives are stack-allocated; structs with all-primitive fields that don't escape scope are stack-promoted automatically; `var` arrays use heap buffers (with automatic cleanup)
- Variables declared with `var` that are never reassigned produce an error (E3077). Use `let` instead if the variable is not mutated.
- For struct-typed variables, `var b = a` creates a reference (alias to the same object); use `var b = a.clone()` for an independent copy (see [Reference-by-Default Assignment](/docs/language/memory-model/#reference-by-default-assignment))
- Assigning an immutable (`let`) reference-type variable to a mutable (`var`) binding is an error (E3078). Value types (int, float, bool, byte) are always independent copies and are allowed. Use `let` instead of `var`, or call `.clone()` to create an independent mutable copy:
  ```maxon
  let a = Point.create(x: 1, y: 2)
  // var b = a              // ERROR E3078: cannot assign immutable variable 'a' to mutable binding 'b'
  let b = a                 // OK — b is immutable
  var c = a.clone()         // OK — c is an independent mutable copy
  ```
- All variables must be used; unused variables cause a compile error (E3012). This applies to `let`/`var` declarations, function parameters, for-loop variables, match pattern bindings, and closure parameters.
- The variable name `_` is a special discard identifier: it creates no binding and is exempt from unused variable checks. Only the exact name `_` is a discard -- names like `_x` are regular variables subject to normal unused checks. Multiple `_` discards are allowed in tuple destructuring (e.g., `for (_, _) in pairs`). In match patterns, `_` can discard individual bindings (e.g., `pair(_, second)`) but discarding all bindings is an error (E3081) — omit the parentheses instead: `pair then ...`.
- **Interface method exception**: methods that implement an interface contract are exempt from the unused-parameter check on their parameters. The implementer is forced to declare every parameter the interface names, even when a particular implementation does not need one of them. The check still applies to local `let`/`var` bindings inside the method, and to non-interface methods on the same type.

### Top-Level Variables

Variables can be declared at the top level of a module (outside any function):

```maxon
var globalCounter = 0
let MAX_SIZE = 1024

function main() returns ExitCode
		globalCounter = globalCounter + 1
		return globalCounter
end 'main'
```

**Top-Level Variable Rules:**
- `var` declares a mutable top-level variable (can be reassigned from any function)
- `let` declares an immutable top-level constant (compile-time evaluated when possible)
- Most initializers must be constant expressions (integer, float, bool, string literal, or enum member reference like `Color.red`)
- `Type from "literal"` expressions (e.g., `FilePath from "path"`) are also allowed as top-level `let` initializers; these are runtime-initialized before `main()` executes
- Static factory calls (e.g., `let shared = Counter.create()`) and array literals (e.g., `var items = [1, 2, 3]`) are also allowed; their initializers run in a per-file `__module_init` function before `main()`
- Initialized before `main()` executes
- Accessible from any function in the same file (use `export` for cross-file visibility)

**Use Cases:**
- Configuration constants (`let MAX_BUFFER_SIZE = 4096`)
- Counters and state shared across function calls (`var callCount = 0`)
- Program-wide settings

---
