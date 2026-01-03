---
feature: mem2reg-phi-placement
status: stable
keywords: [optimization, mem2reg, phi, ssa, register-promotion]
category: optimization
---

# Mem2Reg PHI Placement

## Documentation

The Mem2Reg optimization eliminates stack allocations by promoting local variables to SSA registers. For variables with multiple definitions across different control flow paths, PHI nodes are inserted to merge values.

### Single Definition

When a variable is only assigned in one block, the optimization is straightforward:

```maxon
function main() returns int
    var x = 10
    return x + 5
end 'main'
```
```exitcode
15
```

### Multi-Definition with If/Else

When a variable is assigned in different branches, a PHI node merges the values:

```maxon
function main() returns int
    var x = 0
    if 1 > 0 'branch'
        x = 10
    end 'branch' else 'else_branch'
        x = 20
    end 'else_branch'
    return x
end 'main'
```
```exitcode
10
```

### Loop Variables

Variables modified in loops are correctly promoted with PHI nodes at the loop header:

```maxon
function main() returns int
    var sum = 0
    var i = 0
    while i < 5 'loop'
        sum = sum + i
        i = i + 1
    end 'loop'
    return sum
end 'main'
```
```exitcode
10
```

## Tests

<!-- test: if-else-phi -->
```maxon
function main() returns int
    var x = 0
    if 5 > 3 'check'
        x = 42
    end 'check' else 'else_check'
        x = 100
    end 'else_check'
    return x
end 'main'
```
```exitcode
42
```

<!-- test: if-else-phi-else-path -->
```maxon
function main() returns int
    var x = 0
    if 1 > 5 'check'
        x = 42
    end 'check' else 'else_check'
        x = 100
    end 'else_check'
    return x
end 'main'
```
```exitcode
100
```

<!-- test: nested-if-phi -->
```maxon
function main() returns int
    var result = 0
    if 1 > 0 'outer'
        if 2 > 1 'inner'
            result = 10
        end 'inner' else 'else_inner'
            result = 20
        end 'else_inner'
    end 'outer' else 'else_outer'
        result = 30
    end 'else_outer'
    return result
end 'main'
```
```exitcode
10
```

<!-- test: loop-phi -->
```maxon
function main() returns int
    var sum = 0
    var i = 1
    while i <= 5 'loop'
        sum = sum + i
        i = i + 1
    end 'loop'
    return sum
end 'main'
```
```exitcode
15
```

<!-- test: nested-loop-phi -->
```maxon
function main() returns int
    var total = 0
    var i = 0
    while i < 3 'outer'
        var j = 0
        while j < 3 'inner'
            total = total + 1
            j = j + 1
        end 'inner'
        i = i + 1
    end 'outer'
    return total
end 'main'
```
```exitcode
9
```

<!-- test: multiple-vars-phi -->
```maxon
function main() returns int
    var a = 0
    var b = 0
    if 1 > 0 'branch'
        a = 10
        b = 20
    end 'branch' else 'else_branch'
        a = 100
        b = 200
    end 'else_branch'
    return a + b
end 'main'
```
```exitcode
30
```

<!-- test: phi-with-computation -->
```maxon
function main() returns int
    var x = 5
    if x > 3 'check'
        x = x * 2
    end 'check' else 'else_check'
        x = x + 10
    end 'else_check'
    return x
end 'main'
```
```exitcode
10
```

<!-- test: loop-counter -->
```maxon
function main() returns int
    var count = 0
    var i = 0
    while i < 10 'loop'
        if i mod 2 == 0 'even'
            count = count + 1
        end 'even'
        i = i + 1
    end 'loop'
    return count
end 'main'
```
```exitcode
5
```
