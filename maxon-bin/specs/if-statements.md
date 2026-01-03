---
feature: if-statements
status: stable
keywords: if, else, conditional, branching, control flow
category: control-flow
---
## Documentation

# If Statements

Execute code conditionally based on a boolean expression.

**Simple If (no else):**

```maxon
if <condition> 'identifier'
    <statements>
end 'identifier'
```

**If-Else:**

The `else` keyword comes after the closing `end` of the if-block, on the same line:

```maxon
if <condition> 'if_id'
    <statements>
end 'if_id' else 'else_id'
    <statements>
end 'else_id'
```

**Else-If Chain:**

```maxon
if <condition1> 'case1'
    <statements>
end 'case1' else if <condition2> 'case2'
    <statements>
end 'case2' else 'default'
    <statements>
end 'default'
```

**Example (simple if):**

```maxon
function main() returns int
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
function main() returns int
    var x = 3
    if x > 5 'gt5'
        return 1
    end 'gt5' else 'not_gt5'
        return 0
    end 'not_gt5'
end 'main'
```
```exitcode
0
```


**Example (else-if chain):**

```maxon
function main() returns int
    var x = 2
    if x == 1 'case1'
        return 1
    end 'case1' else if x == 2 'case2'
        return 2
    end 'case2' else 'default'
        return 0
    end 'default'
end 'main'
```
```exitcode
2
```


**Notes:**
- Block identifier required after `if` condition
- Each branch has its own block identifier
- The `else` keyword appears on the same line as `end 'if_id'`
- Block identifiers must be string literals
- Conditions can be any boolean expression
- Else clause is optional

## Tests

<!-- test: if-statements.simple -->
```maxon
function main() returns int
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
function main() returns int
    var x = 5
    if x == 5 'is5'
        return 1
    end 'is5' else 'not5'
        return 0
    end 'not5'
end 'main'
```
```exitcode
1
```

<!-- test: if-statements.else-false -->
```maxon
function main() returns int
    var x = 3
    if x > 5 'gt5'
        return 1
    end 'gt5' else 'not_gt5'
        return 0
    end 'not_gt5'
end 'main'
```
```exitcode
0
```

<!-- test: if-statements.else-if-chain -->
```maxon
function main() returns int
    var x = 2
    if x == 1 'case1'
        return 1
    end 'case1' else if x == 2 'case2'
        return 2
    end 'case2' else 'default'
        return 0
    end 'default'
end 'main'
```
```exitcode
2
```

<!-- test: if-statements.nested -->
```maxon
function main() returns int
    var x = 3
    if x == 1 'outer'
        return 1
    end 'outer' else 'else_outer'
        if x == 2 'inner'
            return 2
        end 'inner' else 'else_inner'
            return 3
        end 'else_inner'
    end 'else_outer'
end 'main'
```
```exitcode
3
```
