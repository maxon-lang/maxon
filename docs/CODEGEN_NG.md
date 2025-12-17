# Codegen NG (Next Generation Code Generator)

The `codegen_ng` module is a new, simplified MIR code generator that directly compiles Maxon AST to MIR without the complexity of the original codegen.

## Overview

Located in `maxon-bin/codegen_ng/`, this module provides a clean implementation for generating MIR (Mid-level Intermediate Representation) from the parsed AST.

## Architecture

```
AST (from parser)
       |
       v
  codegen_ng
       |
       v
  MIR (optimizable)
       |
       v
  x86-64 backend
       |
       v
  Native executable
```

## Files

| File | Purpose |
|------|---------|
| `codegen_ng.h` | Main header, `MIRCodeGenerator` class |
| `codegen_ng.cpp` | Entry point, module setup |
| `codegen_ng_function.cpp` | Function declaration and body generation |
| `codegen_ng_expr.cpp` | Expression code generation |
| `codegen_ng_stmt.cpp` | Statement code generation |

## Usage

```bash
# Compile and emit IR
maxon compile file.maxon --emit-ir

# Compile and run
maxon file.maxon
```

## Supported Constructs

### Statements

| Statement | Status | Notes |
|-----------|--------|-------|
| `var x = expr` | Supported | Mutable variable declaration |
| `let x = expr` | Supported | Immutable variable declaration |
| `x = expr` | Supported | Assignment |
| `return expr` | Supported | Return with value |
| `return` | Supported | Void return |
| `func(args)` | Supported | Expression statement (function call) |
| `if/else` | Not yet | Control flow |
| `while` | Not yet | Loops |
| `for` | Not yet | Iteration |

### Expressions

| Expression | Status | Notes |
|------------|--------|-------|
| Integer literals | Supported | `42`, `-1` |
| Variables | Supported | Load from alloca |
| `a + b` | Supported | Addition |
| `a - b` | Supported | Subtraction |
| `a * b` | Supported | Multiplication |
| `func(args)` | Supported | Function calls |
| Division | Not yet | Need `createDiv` |
| Comparisons | Not yet | `<`, `>`, `==`, etc. |
| Boolean ops | Not yet | `and`, `or`, `not` |

## Example

Input (`ng.maxon`):
```maxon
function main() returns int
    var a = 42
    var b = foo(a)
    a = a + 1
    bar(a)
    return b
end 'main'

function foo(z int) returns int
    return z + 4
end 'foo'

function bar(z int)
    z = z + 1
end 'bar'
```

Generated IR (`ng.ir`):
```llvm
define i64 @main() {
entry0:
  %0 = alloca i64
  store i64 42, ptr %0
  %1 = alloca i64
  %2 = load i64, ptr %0
  %3 = call i64 @foo(i64 %2)
  store i64 %3, ptr %1
  %4 = load i64, ptr %0
  %5 = add i64 %4, 1
  store i64 %5, ptr %0
  %6 = load i64, ptr %0
  call void @bar(i64 %6)
  %7 = load i64, ptr %1
  ret i64 %7
}

define i64 @foo(i64 %z) {
entry0:
  %0 = alloca i64
  store i64 %z, ptr %0
  %1 = load i64, ptr %0
  %2 = add i64 %1, 4
  ret i64 %2
}

define void @bar(i64 %z) {
entry0:
  %0 = alloca i64
  store i64 %z, ptr %0
  %1 = load i64, ptr %0
  %2 = add i64 %1, 1
  store i64 %2, ptr %0
  ret void
}
```

## Implementation Details

### Variable Handling

All variables (including parameters) use the alloca-store-load pattern:

1. `alloca` creates stack space
2. `store` writes values to the alloca
3. `load` reads values from the alloca

This simplifies the IR and allows the optimizer to promote allocas to registers (mem2reg).

### Function Parameters

Parameters are handled by:
1. Creating an alloca for each parameter in the entry block
2. Storing the incoming parameter value into the alloca
3. Tracking the alloca in `namedValues` map

```cpp
for (size_t i = 0; i < func->parameters.size(); i++) {
    mir::MIRValue *alloca = builder->createAlloca(paramType, param.name);
    mir::MIRValue *argVal = function->parameters[i];
    builder->createStore(argVal, alloca);
    namedValues[param.name] = alloca;
}
```

### Entry Point

The generator creates a `_start` function that:
1. Calls `main()`
2. Passes the return value to `exit()`

```llvm
define void @_start() {
entry0:
  %0 = call i64 @main()
  call void @exit(i64 %0)
  ret void
}
```

## Error Messages

Error messages include type information and source locations:

```
Error: Unsupported statement type: IfStmt at line 5, column 4
Error: Unsupported expression type: ArrayIndexExpr at line 10, column 8
Error: Unknown variable: x at line 3
Error: Unsupported binary operator: '/' at line 7
```

## Adding New Constructs

### Adding a Statement

1. Edit `codegen_ng_stmt.cpp`
2. Add a new `dynamic_cast` check in `generateStmt()`
3. Generate appropriate MIR instructions

```cpp
if (auto *ifStmt = dynamic_cast<IfStmtAST *>(stmt)) {
    // Generate condition
    // Create then/else blocks
    // Generate branch
    return;
}
```

### Adding an Expression

1. Edit `codegen_ng_expr.cpp`
2. Add a new `dynamic_cast` check in `generateExpr()`
3. Return the resulting `MIRValue*`

```cpp
if (auto *divExpr = dynamic_cast<DivExprAST *>(expr)) {
    mir::MIRValue *left = generateExpr(divExpr->left.get());
    mir::MIRValue *right = generateExpr(divExpr->right.get());
    return builder->createDiv(left, right);
}
```

## Relationship to Original Codegen

The original `codegen_mir/` module is more feature-complete but also more complex. `codegen_ng` is:

- Simpler and easier to understand
- A clean slate for new development
- Currently used for experimental features
- Will eventually replace the original codegen
