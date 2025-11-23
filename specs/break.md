---
feature: break
status: stable
keywords: break, loop, control flow, exit
category: control-flow
---

## Developer Notes

Break statement exits the innermost enclosing loop.

**Implementation Details:**
- Keyword: `break` (lexer.cpp, TokenType::Break)
- Parser: `parseBreak()` in parser.cpp
- AST node: `BreakAST` (ast.h)
- Codegen: Generates unconditional branch to loop continuation block

**Semantic Rules:**
- Must be inside a loop (while)
- Exits only the innermost loop
- Cannot break from nested functions
- Valid in both single-line and multi-line contexts

**Code Generation:**
- Creates branch to the loop's continuation block
- Uses LLVM's `BranchInst` with loop exit as target
- Subsequent code in the same block becomes unreachable

## Documentation

# Break Statement

Exit from the innermost enclosing loop.

**Syntax:**

```maxon
break
```
**Example:**

```maxon
var x = 5
while true 'loop'
    x = x + 2
    if x = 11 'check'
        break
    end 'check'
end 'loop'
// x is now 11
```
**Notes:**
- Exits the innermost `while` loop
- Control flow continues after the loop's `end` statement
- Must be inside a loop (compile error otherwise)
- Can be used in single-line if: `if condition break`

## Tests

<!-- test: break.in-loop -->
```maxon
function main() int
    var x = 0
    while true 'loop'
        x = x + 1
        if x = 5 'check'
            break
        end 'check'
    end 'loop'
    return x
end 'main'
```
```exitcode
5
```

<!-- test: break.with-single-line-if -->
```maxon
function main() int
    var x = 0
    while true 'loop'
        x = x + 1
        if x = 10 break
    end 'loop'
    return x
end 'main'
```
```exitcode
10
```

<!-- test: break.multiple-conditions -->
```maxon
function main() int
    var x = 5
    var count = 0
    while x < 100 'loop'
        x = x + 1
        count = count + 1
        if count = 3 'check'
            break
        end 'check'
    end 'loop'
    return x
end 'main'
```
```exitcode
8
```
