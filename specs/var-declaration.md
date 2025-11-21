---
feature: var-declaration
status: stable
keywords: var, variable, declaration, mutable
category: declaration
---

## Developer Notes

Variables declared with `var` are mutable and can be reassigned.

**Implementation Details:**
- Keyword: `var` (lexer.cpp, TokenType::Var)
- Parser: `parseVarDeclaration()` in parser.cpp
- AST node: `VarDeclAST` (ast.h)
- Type inference from initializer expression
- Stored in LLVM alloca (stack variable)

**Type System:**
- Type can be explicitly specified: `var x int = 5`
- Type can be inferred: `var x = 5` (infers int)
- Type must match initializer if both provided
- All variables must be initialized

**Code Generation:**
- Creates alloca instruction for stack storage
- Stores initial value
- Variable remains mutable (can be reassigned)

## Documentation

# Variable Declaration (var)

Declare a mutable variable that can be reassigned.

**Syntax:**

```maxon
var <name> = <initializer>
var <name> <type> = <initializer>
```

**Example:**

```maxon
var x = 42              // Type inferred as int
var y int = 10          // Explicit type
var result = x + y      // result is 52
```

**Example with reassignment:**

```maxon
var x = 3
x = x + 2               // OK - var is mutable
// x is now 5
```

**Notes:**
- Variables declared with `var` are mutable
- Type can be inferred from initializer
- All variables must be initialized at declaration
- Scope is function-local

## Tests

<!-- test: var-declaration.basic -->
```maxon
function main() int
    var x = 42
    var y = 10
    var result = x + y
    return result
end 'main'
```
ExitCode: 52

<!-- test: var-declaration.reassignment -->
```maxon
function main() int
    var x = 3
    x = x + 2
    return x
end 'main'
```
ExitCode: 5

<!-- test: var-declaration.explicit-type -->
```maxon
function main() int
    var x int = 100
    return x
end 'main'
```
ExitCode: 100
