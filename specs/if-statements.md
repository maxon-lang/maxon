---
feature: if-statements
status: stable
keywords: if, else, conditional, branching, control flow, then
category: control-flow
---

## Developer Notes

If statements provide conditional execution in Maxon.

**Implementation Details:**
- Keywords: `if`, `else`, `then` (lexer.cpp)
- Parser: `parseIf()` in parser.cpp
- AST node: `IfAST` (ast.h)
- Supports both single-line and multi-line syntax
- Multi-line requires block identifiers
- Single-line if uses `then` keyword

**Syntax Variants:**

**Single-line if:**
```
if <condition> 'identifier' then <statement>
```

**Single-line if-else:**
```
if <condition> 'identifier' then <statement>
else <statement>
```

**Multi-line:**
```
if <condition> 'identifier'
    <statements>
end 'identifier'
```

**Multi-line with else:**
```
if <condition> 'identifier'
    <statements>
else 'identifier'
    <statements>
end 'identifier'
```

**Mixed single-line if with multi-line else:**
```
if <condition> 'identifier' then <statement>
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
if <condition> 'identifier' then <statement>
```

**Single-Line If-Else:**

```maxon
if <condition> 'identifier' then <statement>
else <statement>
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
function main() int
    var x = 10
    if x > 5 then return 1
    return 0
end 'main'
```
```exitcode
1
```


**Example (single-line if-else):**

```maxon
function main() int
    var x = 3
    if x > 5 then return 1
    else return 0
end 'main'
```
```exitcode
0
```


**Example (multi-line with else):**

```maxon
function main() int
    var x = 5
    if x == 5 'check'
        return 1
    else 'check'
        return 0
    end 'check'
end 'main'
```
```exitcode
1
```


**Notes:**
- Single-line if requires `then` keyword before the statement
- Single-line else does NOT use `then`, just `else <statement>`
- Multi-line if requires block identifier matching at all keywords
- Block identifier must be a string literal
- Conditions can be any boolean expression
- Else clause is optional
- Can mix single-line if with multi-line else

## Tests

<!-- test: if-statements.else -->
```maxon
function main() int
    var x = 5
    if x == 5 'check'
        return 1
    else 'check'
        return 0
    end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: if-statements.elseif -->
```maxon
function main() int
    var x = 3
    if x == 1 'check'
        return 1
    else 'check'
        if x == 2 'inner'
            return 2
        else 'inner'
            return 3
        end 'inner'
    end 'check'
end 'main'
```
```exitcode
3
```

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
```exitcode
1
```

<!-- test: if-statements.single-line -->
```maxon
function main() int
    var x = 0
    if 5 > 3 then x = 1
    return x
end 'main'
```
```exitcode
1
```

<!-- test: if-statements.single-line-else -->
```maxon
function main() int
    var x = 3
    if x > 5 then return 1
    else return 0
end 'main'
```
```exitcode
0
```
