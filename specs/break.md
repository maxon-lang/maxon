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
- Must be inside a loop (while or for)
- Exits only the innermost loop (or specified labeled loop)
- Labeled break allows breaking to a specific outer loop
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
break           // Break from innermost loop
break 'label'   // Break from loop with specified label
```
**Example:**

```maxon
var x = 5
while true 'loop'
    x = x + 2
    if x == 11 'check'
        break
    end 'check'
end 'loop'
// x is now 11
```

**Labeled Break Example:**

```maxon
while true 'outer'
    while true 'inner'
        break 'outer'  // Breaks out of outer loop
    end 'inner'
end 'outer'
```

**Labeled Continue:**

The same syntax works for `continue` to jump to a specific loop's next iteration:

```maxon
while x < 10 'outer'
    x = x + 1
    while y < 10 'inner'
        y = y + 1
        if y == 3 'check'
            continue 'outer'  // Skips to next iteration of outer loop
        end 'check'
    end 'inner'
end 'outer'
```

**Notes:**
- Exits the innermost `while` or `for` loop
- Control flow continues after the loop's `end` statement
- Must be inside a loop (compile error otherwise)
- Label must match an enclosing loop's label
- `continue 'label'` jumps to the next iteration of the labeled loop

## Tests

<!-- test: break.in-loop -->
```maxon
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
```exitcode
5
```

<!-- test: break.with-if -->
```maxon
function main() int
    var x = 0
    while true 'loop'
        x = x + 1
        if x == 10 'check'
            break
        end 'check'
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
        if count == 3 'check'
            break
        end 'check'
    end 'loop'
    return x
end 'main'
```
```exitcode
8
```

<!-- test: break.labeled-break-outer -->
```maxon
function main() int
    var x = 0
    while x < 10 'outer'
        x = x + 1
        while true 'inner'
            if x == 3 'check'
                break 'outer'
            end 'check'
            break 'inner'
        end 'inner'
    end 'outer'
    return x
end 'main'
```
```exitcode
3
```

<!-- test: break.labeled-break-inner -->
```maxon
function main() int
    var x = 0
    var y = 0
    while x < 5 'outer'
        x = x + 1
        while y < 10 'inner'
            y = y + 1
            if y == 3 'check'
                break 'inner'
            end 'check'
        end 'inner'
    end 'outer'
    return x
end 'main'
```
```exitcode
5
```

<!-- test: break.labeled-break-triple-nested -->
```maxon
function main() int
    var result = 0
    while true 'outer'
        while true 'middle'
            while true 'inner'
                result = result + 1
                if result == 5 'check'
                    break 'outer'
                end 'check'
            end 'inner'
        end 'middle'
    end 'outer'
    return result
end 'main'
```
```exitcode
5
```

<!-- test: break.labeled-continue-outer -->
```maxon
function main() int
    var x = 0
    var count = 0
    while x < 3 'outer'
        x = x + 1
        var y = 0
        while y < 5 'inner'
            y = y + 1
            count = count + 1
            if y == 2 'check'
                continue 'outer'
            end 'check'
        end 'inner'
    end 'outer'
    return count
end 'main'
```
```exitcode
6
```

<!-- test: break.labeled-continue-inner -->
```maxon
function main() int
    var sum = 0
    var x = 0
    while x < 3 'outer'
        x = x + 1
        var y = 0
        while y < 5 'inner'
            y = y + 1
            if y == 3 'check'
                continue 'inner'
            end 'check'
            sum = sum + 1
        end 'inner'
    end 'outer'
    return sum
end 'main'
```
```exitcode
12
```

<!-- test: break.labeled-continue-triple-nested -->
```maxon
function main() int
    var count = 0
    var a = 0
    while a < 2 'outer'
        a = a + 1
        var b = 0
        while b < 3 'middle'
            b = b + 1
            var c = 0
            while c < 4 'inner'
                c = c + 1
                count = count + 1
                if c == 2 'check'
                    continue 'middle'
                end 'check'
            end 'inner'
        end 'middle'
    end 'outer'
    return count
end 'main'
```
```exitcode
12
```
