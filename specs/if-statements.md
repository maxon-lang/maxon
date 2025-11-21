---
feature: if-statements
status: stable
keywords: if, else, conditional, branching, control flow
category: control-flow
---

## Developer Notes

If statements provide conditional execution in Maxon.

**Implementation Details:**
- Keywords: `if`, `else` (lexer.cpp)
- Parser: `parseIf()` in parser.cpp
- AST node: `IfAST` (ast.h)
- Supports both single-line and multi-line syntax
- Multi-line requires block identifiers

**Syntax Variants:**

**Single-line:**
```
if <condition> <statement>
```

**Multi-line:**
```
if <condition> 'identifier'
    <statements>
end 'identifier'
```

**With else:**
```
if <condition> 'identifier'
    <statements>
else 'identifier'
    <statements>
end 'identifier'
```

**Code Generation:**
- Creates basic blocks for then, else (optional), and continuation
- Uses conditional branch instruction for the condition
- Phi nodes for value merging when needed

## Documentation

# If Statements

Execute code conditionally based on a boolean expression.

**Single-Line Syntax:**

```maxon
if <condition> <statement>
```

**Multi-Line Syntax:**

```maxon
if <condition> 'identifier'
    <statements>
else 'identifier'
    <statements>
end 'identifier'
```

**Example (single-line):**

```maxon
var x = 10
if x > 5 return 1
```

**Example (multi-line with else):**

```maxon
var x = 5
if x = 5 'check'
    return 1
else 'check'
    return 0
end 'check'
```

**Notes:**
- Single-line if requires statement on same line
- Multi-line if requires block identifier matching at all keywords
- Block identifier must be a string literal
- Conditions can be any boolean expression
- Else clause is optional

## Tests

<!-- test: if-statements.else -->
```maxon
function main() int
    var x = 5
    if x = 5 'check'
        return 1
    else 'check'
        return 0
    end 'check'
end 'main'
```
ExitCode: 1

<!-- test: if-statements.elseif -->
```maxon
function main() int
    var x = 3
    if x = 1 'check'
        return 1
    else 'check'
        if x = 2 'inner'
            return 2
        else 'inner'
            return 3
        end 'inner'
    end 'check'
end 'main'
```
ExitCode: 3

<!-- test: if-statements.simple -->
```maxon
function main() int
    var x = 10
    if x > 5 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
ExitCode: 1
