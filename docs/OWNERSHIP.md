# Ownership System

Maxon implements a compile-time ownership system that tracks value ownership and prevents use-after-move errors without runtime overhead.

## Overview

Every variable in Maxon has ownership of its value. When a variable is passed to a function:

- **Borrow**: If the function only reads the parameter, ownership stays with the caller
- **Move**: If the function mutates the parameter, ownership transfers to the callee

After a move, the original variable cannot be used or reassigned - this is a compile-time error.

## Example

```maxon
function main() returns int
    var a = 42              // a owns the value 42

    var b = foo(a)          // borrow - foo only reads z
    a = a + 1               // OK - a still owns its value

    bar(a)                  // move - bar mutates z, ownership transfers

    // a = a + 1            // ERROR: Cannot assign after ownership transferred
    // return a             // ERROR: Cannot use after ownership transferred

    return b                // OK - b is still owned
end 'main'

function foo(z int) returns int
    return z + 4            // only reads z - borrows
end 'foo'

function bar(z int)
    z = z + 1               // mutates z - takes ownership
end 'bar'
```

## How It Works

### Mutation Analysis Pass

Before semantic analysis, the compiler runs a mutation analysis pass that scans each function to determine which parameters it mutates:

1. Direct assignment to a parameter (`z = ...`)
2. Array element assignment (`arr[i] = ...` where `arr` is a parameter)
3. Member assignment (`obj.field = ...` where `obj` is a parameter)
4. Passing to another function that mutates that parameter position

This information is stored per-function and used during semantic analysis.

### Ownership Tracking

During semantic analysis, each variable has an ownership state:

- `Owned` - Variable owns its value and can be used
- `Moved` - Ownership has been transferred; any use is an error

When a variable is passed to a function call:
1. Look up if the called function mutates that parameter position
2. If yes, mark the variable as `Moved` with location info
3. If no, the variable remains `Owned` (borrow)

### Error Detection

The compiler reports errors when:

- **Use after move**: Reading a moved variable
- **Assign after move**: Assigning to a moved variable

Error messages include where ownership was transferred:

```
Semantic Error: example.maxon:10:4
Cannot assign to variable 'a' after ownership was transferred
  Ownership transferred to function 'bar' at line 8
  Note: Once ownership is transferred, the variable cannot be used or reassigned
```

### Control Flow

If a variable is moved in any branch of a conditional, it's considered moved after the conditional:

```maxon
var a = 42
if condition 'c'
    bar(a)          // moves a in this branch
end 'c'
// a is now moved - might have been moved depending on condition
return a            // ERROR
```

## Design Decisions

### All Types Have Ownership

Unlike Rust which distinguishes between `Copy` and non-`Copy` types, Maxon applies ownership uniformly to all types including primitives (`int`, `float`, `bool`). This provides:

- Consistent semantics across all types
- No special cases to remember
- Clear data flow throughout the program

### Automatic Mutation Detection

The compiler automatically detects mutations rather than requiring explicit annotations (like Rust's `&mut`). This:

- Reduces syntactic overhead
- Makes refactoring easier (changing mutation behavior updates callers automatically)
- Matches Maxon's philosophy of minimal annotation

### Compile-Time Only

Ownership is purely a compile-time concept with zero runtime overhead. No reference counting, no garbage collection, no runtime checks.

## Implementation Files

| File | Purpose |
|------|---------|
| `semantic_analyzer.h` | `OwnershipState`, `MoveInfo`, `ParameterMutationInfo` types |
| `semantic_analyzer_mutation.cpp` | Mutation analysis pass implementation |
| `semantic_analyzer_expr.cpp` | Ownership tracking at call sites, use-after-move checks |
| `semantic_analyzer_stmt.cpp` | Assign-after-move checks, control flow merging |

## Future Work

- **Reborrowing**: Allow borrowing from a borrowed reference
- **Partial moves**: Track ownership of individual struct fields
- **Explicit annotations**: Optional `mut` keyword for documentation
