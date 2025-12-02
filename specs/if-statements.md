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
- Requires block identifiers for all if/else statements
- Enforces explicit block structure with `end` keyword

**Syntax:**

**If statement:**
```
if <condition> 'identifier'
    <statements>
end 'identifier'
```

**If-else statement:**
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

**Syntax:**

```maxon
if <condition> 'identifier'
    <statements>
end 'identifier'
```

**With Else:**

```maxon
if <condition> 'identifier'
    <statements>
else 'identifier'
    <statements>
end 'identifier'
```

**Example (simple if):**

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


**Example (if-else):**

```maxon
function main() int
    var x = 3
    if x > 5 'check'
        return 1
    else 'check'
        return 0
    end 'check'
end 'main'
```
```exitcode
0
```


**Example (nested if):**

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
- Block identifier required after `if` condition
- Block identifier must match on `else` and `end` keywords
- Block identifier must be a string literal
- Conditions can be any boolean expression
- Else clause is optional

## Tests

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

<!-- test: if-statements.nested -->
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
