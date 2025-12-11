---
feature: let-declaration
status: stable
keywords: let, immutable, constant, declaration
category: declaration
---

## Developer Notes

Variables declared with `let` are immutable and cannot be reassigned.

**Implementation Details:**
- Keyword: `let` (lexer.cpp, TokenType::Let)
- Parser: `parseVarDeclaration()` in parser.cpp (same as var)
- AST node: `VarDeclAST` with `isConst` flag (ast.h)
- Semantic analyzer enforces immutability
- Stored in LLVM alloca (same as var at runtime)

**Type System:**
- Type inference works same as `var`
- Explicit type annotation supported
- Immutability is compile-time only (no runtime difference)

**Semantic Analysis:**
- Assignment to `let` variable is a semantic error
- Checked during semantic analysis phase
- Error message indicates immutability violation

## Documentation

# Let Declaration (Immutable)

Declare an immutable variable that cannot be reassigned.

**Syntax:**

```maxon
let <name> = <initializer>
let <name> <type> = <initializer>
```
**Example:**

```maxon
let x = 42              // Type inferred as int
let y int = 10          // Explicit type
let result = x + y      // result is 52
```
**Immutability:**

```maxon
let x = 3
x = 5                   // ERROR: Cannot assign to immutable variable
```
**Notes:**
- Variables declared with `let` cannot be reassigned
- Immutability is enforced at compile time
- Useful for constants and values that shouldn't change
- Same runtime performance as `var`

## Tests

<!-- test: let-declaration.basic -->
```maxon
function main() returns int
    let x = 42
    let y = 10
    let result = x + y
    return result
end 'main'
```
```exitcode
52
```

<!-- test: let-declaration.immutable-error -->
```maxon
function main() returns int
    let x = 3
    x = 5
    return x
end 'main'
```
```maxoncstderr
Semantic Error: line 4, column 7
Cannot assign to read-only variable 'x'
  Variable declared with 'let' at line 3, column 5
  Note: Variables declared with 'let' are immutable (read-only). Use 'var' for mutable variables

  4 |     x = 5
    |       ^
```

