# Maxon Compiler - Missing Features Implementation Plan

## Executive Summary

Based on analysis of the old test fragments, the current Maxon compiler is missing several critical features. This document outlines a phased implementation plan to add these features and migrate all test fragments to the new format.

## Analysis of Missing Features

### Currently Implemented ✅
- Basic function declarations with return types
- Simple expressions (literals, binary operators: +, -, *, /)
- Operator precedence
- Function calls (no parameters yet)
- Block identifiers with `end 'identifier'` syntax

### Missing Features from Old Fragments ❌

#### 1. **Variable Declarations and Assignment**
- **Status**: Partially implemented (AST exists, but needs testing)
- **Old fragments**: `Let.test`, `Variable Assignment.test`
- **Features needed**:
  - `var` keyword for mutable variables
  - `let` keyword for immutable variables (read-only)
  - Variable assignment statements
  - Variable references in expressions

#### 2. **Function Parameters**
- **Status**: Not implemented
- **Old fragments**: `Function Call With Parameter.test`, `Expression Compound with Function.test`
- **Features needed**:
  - Function parameter declarations with types
  - Parameter passing in function calls
  - Parameter references in function body

#### 3. **Control Flow - If/Else**
- **Status**: Partially implemented (AST exists, but needs block syntax)
- **Old fragments**: `If Else.test`
- **Features needed**:
  - If statements with conditions
  - Else clauses
  - Block identifiers for if/else blocks
  - Comparison operators (==, !=, <, >, <=, >=)

#### 4. **Control Flow - While Loops**
- **Status**: Partially implemented (AST exists, but needs testing)
- **Old fragments**: `While Loop.test`
- **Features needed**:
  - While loops with conditions
  - `break` statement to exit loops
  - `continue` statement to skip iteration
  - Boolean literal `true` and `false`

#### 5. **Comparison Operators**
- **Status**: Partially implemented
- **Old fragments**: Used in `If Else.test`, `While Loop.test`
- **Features needed**:
  - `==` (equality)
  - `!=` (inequality)
  - `<`, `>`, `<=`, `>=` (relational)
  - Proper lexer token types (not just EQUAL/ASSIGN confusion)

#### 7. **Semantic Error Handling**
- **Status**: Not implemented
- **Old fragments**: `Semantic Error.test`, `Unknown Binary Operator Error.test`, `Unknown Keyword Error.test`
- **Features needed**:
  - Return statement validation (functions must return)
  - Type checking
  - Undefined variable detection
  - Error messages with line/column information

#### 8. **Code Optimization**
- **Status**: Relies on LLVM
- **Old fragments**: `OptimizeMath.test`
- **Features needed**:
  - Constant folding (already done by LLVM)
  - Dead code elimination (already done by LLVM)

---

## Implementation Phases

### Phase 1: Lexer Enhancements (Week 1)

#### Tasks:
1. **Add missing token types to `lexer.h`**:
   ```cpp
   LET,          // let keyword
   BREAK,        // break keyword
   CONTINUE,     // continue keyword
   TRUE,         // true keyword
   FALSE,        // false keyword
   EQUAL_EQUAL,  // == (equality comparison)
   NOT_EQUAL,    // != (not equal)
   ```

2. **Update lexer keyword map in `lexer.cpp`**:
   - Add `"let"` → `TokenType::LET`
   - Add `"break"` → `TokenType::BREAK`
   - Add `"continue"` → `TokenType::CONTINUE`
   - Add `"true"` → `TokenType::TRUE`
   - Add `"false"` → `TokenType::FALSE`

3. **Add multi-character operator handling**:
   - Detect `==` and emit `EQUAL_EQUAL`
   - Detect `!=` and emit `NOT_EQUAL`
   - Fix confusion between `=` (ASSIGN) and `==` (EQUAL_EQUAL)

4. **Test lexer changes**:
   - Create unit tests for new tokens
   - Verify operator precedence

---

### Phase 2: AST Extensions (Week 2)

#### Tasks:
1. **Add new AST node types in `ast.h`**:

   ```cpp
   // Boolean literal
   class BooleanExprAST : public ExprAST {
   public:
       bool value;
       
       BooleanExprAST(bool val) : value(val) {}
   };
   
   // Break statement
   class BreakStmtAST : public StmtAST {
   public:
       BreakStmtAST() = default;
   };
   
   // Continue statement
   class ContinueStmtAST : public StmtAST {
   public:
       ContinueStmtAST() = default;
   };
   
   // Let declaration (immutable variable)
   class LetDeclStmtAST : public StmtAST {
   public:
       std::string name;
       std::unique_ptr<ExprAST> initializer;
       
       LetDeclStmtAST(const std::string& n, std::unique_ptr<ExprAST> init)
           : name(n), initializer(std::move(init)) {}
   };
   ```

2. **Add function parameter support**:
   ```cpp
   struct FunctionParameter {
       std::string name;
       std::string type;
   };
   
   class FunctionAST {
   public:
       std::string name;
       std::vector<FunctionParameter> parameters;  // NEW
       std::string returnType;
       std::vector<std::unique_ptr<StmtAST>> body;
       
       FunctionAST(const std::string& n, 
                   std::vector<FunctionParameter> params,  // NEW
                   const std::string& ret,
                   std::vector<std::unique_ptr<StmtAST>> b)
           : name(n), parameters(std::move(params)), returnType(ret), body(std::move(b)) {}
   };
   ```

---

### Phase 3: Parser Enhancements (Week 3-4)

#### Tasks:

1. **Add `let` declaration parsing**:
   - Modify `parseStatement()` to handle `TokenType::LET`
   - Create `parseLetDecl()` method
   - Track immutable variables for semantic checks

2. **Add `break` and `continue` parsing**:
   - Modify `parseStatement()` to handle `TokenType::BREAK` and `TokenType::CONTINUE`
   - Track loop context to ensure break/continue are only used inside loops

3. **Add boolean literal parsing**:
   - Modify `parsePrimary()` to handle `TRUE` and `FALSE` tokens

4. **Fix comparison operator parsing**:
   - Update `parseExpression()` to properly handle `==`, `!=`, `<`, `>`, `<=`, `>=`
   - Ensure proper precedence (comparison lower than arithmetic)

5. **Add function parameter parsing**:
   - Modify `parseFunction()` to parse parameter list
   - Update function call parsing to accept arguments
   - Example syntax:
     ```maxon
     function add(a int, b int) int
         return a + b
     end 'add'
     ```

6. **Update block syntax to use curly braces (OLD fragments style)**:
   - **Decision needed**: Old fragments use `{ }` instead of block identifiers
   - If migrating old fragments as-is, need to support both syntaxes
   - Recommended: Convert old fragments to new block identifier syntax

---

### Phase 4: Code Generation (Week 5-6)

#### Tasks:

1. **Add codegen for function parameters**:
   - Generate LLVM function signatures with parameters
   - Allocate stack space for parameters
   - Handle parameter passing in function calls

2. **Add codegen for `let` declarations**:
   - Similar to `var`, but mark as immutable in symbol table
   - Reject assignments to `let` variables

3. **Add codegen for `break` and `continue`**:
   - Generate LLVM branch instructions
   - Track loop exit and loop continue labels
   - Emit proper jumps

4. **Add codegen for boolean literals**:
   - Generate i1 constants in LLVM

5. **Fix comparison operator codegen**:
   - Generate proper LLVM icmp instructions
   - Handle `==`, `!=`, `<`, `>`, `<=`, `>=`

---

### Phase 5: Semantic Analysis (Week 7)

#### Tasks:

1. **Add return statement validation**:
   - Ensure all non-void functions end with a return statement
   - Check all code paths return a value
   - Implement in `analyzer.cpp` for LSP

2. **Add type checking**:
   - Verify variable types match in assignments
   - Verify function return types
   - Verify function argument types

3. **Add variable scope checking**:
   - Track variable declarations
   - Detect undefined variable usage
   - Detect duplicate variable declarations

4. **Add immutability checking for `let`**:
   - Track which variables are immutable
   - Reject assignments to `let` variables
   - Provide clear error message (as in `Let.test`)

5. **Add loop context tracking**:
   - Ensure `break` and `continue` only appear inside loops
   - Provide clear error messages

---

### Phase 6: Test Migration (Week 8)

#### Tasks:

1. **Convert old fragments to new syntax**:
   - Replace `{ }` with block identifiers
   - Update `func` to `function`
   - Add explicit block identifiers after `end`
   
   Example conversion:
   ```maxon
   // OLD
   func main() int {
       return 5;
   }
   
   // NEW
   function main() int
       return 5
   end 'main'
   ```

2. **Migrate test files**:
   - `Expression Literal.test` → `expression-literal.test` ✅ (already covered)
   - `Expression Compound.test` → `expression-compound.test`
   - `Expression Compound with Function.test` → `expression-compound-with-function.test`
   - `Function Call With Parameter.test` → `function-call-with-parameter.test`
   - `Variable Assignment.test` → `variable-assignment.test`
   - `Let.test` → `let-declaration.test`
   - `If Else.test` → `if-else.test`
   - `While Loop.test` → `while-loop.test`
   - `OptimizeMath.test` → `optimize-math.test`
   - `Semantic Error.test` → `semantic-error-return.test`
   - `Unknown Binary Operator Error.test` → `error-unknown-binop.test`
   - `Unknown Keyword Error.test` → `error-unknown-keyword.test`

3. **Update test runner**:
   - Ensure `FragmentTests.cs` can handle all new test cases
   - Verify expected output matches LLVM IR format
   - Add semantic error test support

---

## Syntax Conversion Reference

### Old Syntax (C-style)
```maxon
func main() int {
    var x = 3;
    if (x == 3) {
        return 5;
    } else {
        return 2;
    }
}
```

### New Syntax (Block identifiers)
```maxon
function main() int
    var x = 3
    if x == 3 'check'
        return 5
    else 'check'
        return 2
    end 'check'
end 'main'
```

### Conversion Rules:
1. `func` → `function`
2. Remove `{ }` braces
3. Remove semicolons `;`
4. Add block identifiers after control structures
5. Add `end 'identifier'` to close blocks
6. Remove parentheses around conditions 

---

## Test Fragment Migration Details

### 1. Expression Literal
- **Status**: ✅ Already migrated (similar test exists)
- **Old**: `Expression Literal.test`
- **New**: Covered by `expression-binop.test`

### 2. Expression Compound
- **Status**: ❌ Needs migration
- **Old**: Tests `(2 + 3) * 5`
- **New file**: `expression-compound.test`
- **Changes needed**: Convert to block syntax

### 3. Expression Compound with Function
- **Status**: ❌ Needs migration + function parameters implementation
- **Old**: Tests `5 + add(x, 4)` with function parameters
- **New file**: `expression-compound-with-function.test`
- **Changes needed**:
  - Implement function parameters
  - Convert to block syntax

### 4. Function Call With Parameter
- **Status**: ❌ Needs function parameters implementation
- **Old**: Tests `add(x, 4)` with two parameters
- **New file**: `function-call-with-parameter.test`
- **Changes needed**:
  - Implement function parameters
  - Convert to block syntax

### 5. Variable Assignment
- **Status**: ❌ Needs migration + variable assignment testing
- **Old**: Tests `var x = 3; x = x + 2;`
- **New file**: `variable-assignment.test`
- **Changes needed**: Convert to block syntax

### 6. Let Declaration
- **Status**: ❌ Needs `let` keyword implementation
- **Old**: Tests immutable variable error
- **New file**: `let-declaration.test`
- **Changes needed**:
  - Implement `let` keyword
  - Add semantic error for reassignment
  - Convert to block syntax

### 7. If Else
- **Status**: ❌ Needs if/else + comparison operators
- **Old**: Tests if/else with `==` operator
- **New file**: `if-else.test`
- **Changes needed**:
  - Implement if/else blocks
  - Implement `==` comparison
  - Add block identifiers

### 8. While Loop
- **Status**: ❌ Needs while loop + break + boolean literal
- **Old**: Tests while loop with `break`
- **New file**: `while-loop.test`
- **Changes needed**:
  - Implement `break` statement
  - Implement `true` keyword
  - Add block identifiers

### 9. OptimizeMath
- **Status**: ✅ Should work with LLVM optimization
- **Old**: Tests constant folding
- **New file**: `optimize-math.test`
- **Changes needed**: Convert to block syntax

### 10. Semantic Error
- **Status**: ❌ Needs semantic analyzer
- **Old**: Tests missing return statement error
- **New file**: `semantic-error-return.test`
- **Changes needed**:
  - Implement return validation
  - Convert to block syntax

### 11. Unknown Binary Operator Error
- **Status**: ✅ Should already work
- **Old**: Tests `^` operator error
- **New file**: `error-unknown-binop.test`
- **Changes needed**: Convert to block syntax

### 12. Unknown Keyword Error
- **Status**: ✅ Should already work
- **Old**: Tests `foo` keyword error
- **New file**: `error-unknown-keyword.test`
- **Changes needed**: Convert to block syntax

---

## Priority Order

### High Priority (Phase 1-3):
1. ✅ Basic expressions (already done)
2. 🔴 Function parameters (blocks migration)
3. 🔴 Variable assignment
4. 🔴 Comparison operators (`==`, `!=`, etc.)
5. 🔴 If/else statements

### Medium Priority (Phase 4-5):
6. 🔴 While loops
7. 🔴 `break` and `continue`
8. 🔴 `let` keyword (immutable variables)

### Low Priority (Phase 6):
9. 🔴 Semantic error handling
10. 🔴 Code optimization testing
11. 🔴 Error message testing

---

## Success Criteria

### Milestone 1: All old fragments compile
- All 12 old fragment tests pass
- LLVM IR matches expected output
- Exit codes match expected values

### Milestone 2: Error handling works
- Semantic errors are caught
- Parser errors have clear messages
- Line and column numbers are accurate

### Milestone 3: LSP integration
- LSP server provides diagnostics
- Syntax highlighting works for new keywords
- Code completion includes new features

---

## Implementation Checklist

### Week 1: Lexer
- [ ] Add `LET`, `BREAK`, `CONTINUE`, `TRUE`, `FALSE` tokens
- [ ] Add `EQUAL_EQUAL`, `NOT_EQUAL` tokens
- [ ] Fix `=` vs `==` token handling
- [ ] Test lexer with new tokens

### Week 2: AST
- [ ] Add `BooleanExprAST`
- [ ] Add `BreakStmtAST` and `ContinueStmtAST`
- [ ] Add `LetDeclStmtAST`
- [ ] Add function parameter support to `FunctionAST`
- [ ] Update `CallExprAST` to support arguments

### Week 3-4: Parser
- [ ] Implement `parseLetDecl()`
- [ ] Implement `break` and `continue` parsing
- [ ] Implement boolean literal parsing
- [ ] Fix comparison operator parsing
- [ ] Implement function parameter parsing
- [ ] Implement function call argument parsing

### Week 5-6: Codegen
- [ ] Implement function parameter codegen
- [ ] Implement `let` declaration codegen
- [ ] Implement `break` and `continue` codegen
- [ ] Implement boolean literal codegen
- [ ] Fix comparison operator codegen

### Week 7: Semantic Analysis
- [ ] Implement return statement validation
- [ ] Implement type checking
- [ ] Implement variable scope checking
- [ ] Implement immutability checking for `let`
- [ ] Implement loop context tracking

### Week 8: Test Migration
- [ ] Convert all 12 old fragments to new syntax
- [ ] Move converted tests to `fragments/` directory
- [ ] Verify all tests pass
- [ ] Update documentation

---

## Notes

- **Block Identifier Syntax**: The new Maxon syntax requires explicit block identifiers. All old tests need conversion.
- **Semicolons**: The new syntax does NOT use semicolons. Remove them during migration.
- **Braces**: The new syntax does NOT use `{ }`. Use block identifiers instead.
- **Testing Strategy**: Implement features incrementally and test each phase before moving to the next.
- **LLVM Optimization**: Many optimizations (like constant folding) are handled by LLVM automatically, no special implementation needed.

---

## Expected Outcomes

After completing all phases:
1. ✅ All 12 old test fragments pass
2. ✅ New syntax is consistently used throughout
3. ✅ Compiler supports all basic Maxon language features
4. ✅ Error handling provides clear, helpful messages
5. ✅ LSP provides IDE support for all features
6. ✅ Code generation produces correct LLVM IR
7. ✅ Optimizations work as expected

---

## Appendix: Example Conversions

### Example 1: Function with Parameters
```maxon
// OLD
func add(a int, b int) int {
    return a + b;
}

// NEW
function add(a int, b int) int
    return a + b
end 'add'
```

### Example 2: If/Else
```maxon
// OLD
func main() int {
    var x = 5;
    if (x == 5) {
        return 1;
    } else {
        return 0;
    }
}

// NEW
function main() int
    var x = 5
    if x == 5 'check'
        return 1
    else 'check'
        return 0
    end 'check'
end 'main'
```

### Example 3: While Loop with Break
```maxon
// OLD
func main() int {
    var x = 0;
    while true {
        x = x + 1;
        if (x == 5) {
            break;
        }
    }
    return x;
}

// NEW
function main() int
    var x = 0
    while true 'loop'
        x = x + 1
        if x == 5 'check'
            break
        end 'check'
    end 'loop'
    return x
end 'main'
```

### Example 4: Let Declaration
```maxon
// OLD
func main() int {
    let x = 3;
    x = 5;  // ERROR: Cannot reassign read-only variable
    return x;
}

// NEW
function main() int
    let x = 3
    x = 5  // ERROR: Cannot reassign read-only variable
    return x
end 'main'
```
