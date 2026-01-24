---
feature: while-loops
status: stable
keywords: while, loop, iteration, control flow
category: control-flow
---
# While Loops

## Documentation

Execute a block of code repeatedly while a condition is true.

**Syntax:**

```maxon
while <condition> 'identifier'
    <statements>
end 'identifier'
```
**Parameters:**
- `condition` - Boolean expression evaluated before each iteration
- `identifier` - String label for the loop block (must match at `end`)

**Example:**

```maxon
var x = 5
var i = 3
while i > 0 'loop'
    x = x + 2
    i = i - 1
end 'loop'
// x is now 11
```
**Notes:**
- Condition is evaluated before each iteration (pre-test loop)
- Block identifier is required and must match at `while` and `end`
- Use `break` to exit the loop early
- Use `continue` to skip to the next iteration
- Infinite loops possible with `while true 'loop'`

## Tests

<!-- test: while-loops.basic -->
```maxon
function main() returns int
var x = 5
var i = 3
while i > 0 'loop'
x = x + 2
i = i - 1
if i == 0 'check'
break
end 'check'
end 'loop'
return x
end 'main'
```
```exitcode
11
```

<!-- test: while-loops.break -->
```maxon
function main() returns int
    var x = 5
    while true 'loop'
        x = x + 2
        if x == 11 'check'
            break
        end 'check'
    end 'loop'
    return x
end 'main'
```
```exitcode
11
```

<!-- test: while-loops.zero-iterations -->
```maxon
function main() returns int
    var x = 10
    while x < 5 'loop'
        x = x + 1
    end 'loop'
    return x
end 'main'
```
```exitcode
10
```

<!-- test: nested-control -->
```maxon
function main() returns int
    var result = 0
    var i = 0
    while i < 3 'outer'
        var j = 0
        while j < 3 'inner'
            if (i + j) mod 2 == 0 'even'
                result = result + 1
            end 'even'
            j = j + 1
        end 'inner'
        i = i + 1
    end 'outer'
    return result
end 'main'
```
```exitcode
5
```

<!-- test: while-loops.continue -->
```maxon
function main() returns int
    var sum = 0
    var i = 0
    while i < 5 'loop'
        i = i + 1
        if i == 3 'skip'
            continue
        end 'skip'
        sum = sum + i
    end 'loop'
    return sum
end 'main'
```
```exitcode
12
```
