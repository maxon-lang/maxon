---
feature: duplicate-block-identifiers
status: stable
keywords: block, identifier, error, validation, duplicate
category: error-handling
---

## Developer Notes

Duplicate block identifier validation ensures that each block in nested structures has a unique identifier to prevent shadowing and improve code clarity.

**Implementation Details:**
- Validation in `SemanticAnalyzer` (semantic_analyzer.cpp and semantic_analyzer.h)
- Block ID tracking via `blockIdStack` (vector of sets)
- Validation occurs in `declareBlockId()` method
- Checked during analysis of `IfStmtAST`, `WhileStmtAST`, and `ForStmtAST` nodes

**Behavior:**
- Each block scope maintains a set of active block identifiers
- When a block is encountered, its identifier is checked against the current scope
- Error is raised if identifier already exists in the same scope
- Block scopes are managed independently per function

## Documentation

# Duplicate Block Identifiers

The compiler prevents using the same block identifier for multiple blocks within overlapping scopes.

**Valid Code - Different Identifiers:**

```maxon
function main() returns int
    var x = 0
    if true 'outer'
        x = 1
    end 'outer'
    
    if true 'inner'
        x = 2
    end 'inner'
    
    return x
end 'main'
```
```exitcode
0
```


**Valid Code - Shadowing at Different Nesting Levels:**

```maxon
function main() returns int
    var x = 0
    if true 'check'
        x = 1
        if true 'check'
            x = 2
        end 'check'
    end 'check'
    return x
end 'main'
```
```exitcode
0
```


**Invalid Code - Duplicate at Same Level:**

```maxon
function main() returns int
    var x = 0
    if true 'check'
        x = 1
    end 'check'
    while false 'check'
        x = 2
    end 'check'
    return x
end 'main'
```
```maxoncstderr
Semantic Error: line 7, column 5
Duplicate block identifier 'check' in nested blocks

  7 |     while false 'check'
    |     ^
```


**Notes:**
- Block identifiers must be unique within the same function scope
- Nested blocks can reuse identifiers from parent scopes
- Error message indicates the duplicate identifier and its location
- Applies to if, else, while, and for statements

## Tests

<!-- test: duplicate-block-identifiers.different-blocks -->
```maxon
function main() returns int
    var x = 0
    if true 'outer'
        x = 1
    end 'outer'
    
    if true 'inner'
        x = 2
    end 'inner'
    
    return x
end 'main'
```
```exitcode
2
```

<!-- test: duplicate-block-identifiers.nested-same-id -->
```maxon
function main() returns int
    var x = 0
    if true 'check'
        x = 1
        if true 'check'
            x = 2
        end 'check'
    end 'check'
    return x
end 'main'
```
```exitcode
2
```

<!-- test: duplicate-block-identifiers.multiple-nested -->
```maxon
function main() returns int
    var x = 0
    if true 'outer'
        x = 1
        while true 'inner'
            x = 2
            break
        end 'inner'
    end 'outer'
    return x
end 'main'
```
```exitcode
2
```

<!-- test: duplicate-block-identifiers.else-nested -->
```maxon
function main() returns int
    var x = 0
    if false 'check'
        x = 1
    end 'check' else 'else_check'
        if true 'nested'
            x = 2
        end 'nested'
    end 'else_check'
    return x
end 'main'
```
```exitcode
2
```
