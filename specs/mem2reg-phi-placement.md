---
feature: mem2reg-phi-placement
status: stable
keywords: [optimization, mem2reg, phi, ssa, register-promotion]
category: optimization
---

# Mem2Reg PHI Placement

## Developer Notes

The Mem2Reg (Memory to Register) pass promotes stack-allocated variables to SSA registers. This is one of the most important optimizations for performance.

Implementation (optimizer.cpp):
- Located in `Mem2RegPass::promoteAlloca()`
- Uses the classic Cytron et al. algorithm for SSA construction
- Key steps:
  1. Identify promotable allocas (scalar types, only loads/stores)
  2. Find definition blocks (blocks containing stores to the alloca)
  3. Compute iterated dominance frontier for PHI placement
  4. Insert PHI nodes at dominance frontier blocks
  5. Rename variables using dominator tree traversal

For single-definition variables (stores only in one block):
- Simple direct replacement of loads with the stored value
- No PHI nodes needed

For multi-definition variables (stores in multiple blocks):
- PHI nodes inserted at dominance frontier locations
- Dominator tree traversal ensures correct reaching definitions
- Handles if/else branches, loops, and nested control flow

The algorithm correctly handles:
- Variables assigned in different if/else branches
- Variables modified in loops
- Nested control flow with multiple assignments
- Uses of variables after loop exit

## Documentation

The Mem2Reg optimization eliminates stack allocations by promoting local variables to SSA registers. For variables with multiple definitions across different control flow paths, PHI nodes are inserted to merge values.

### Single Definition

When a variable is only assigned in one block, the optimization is straightforward:

```maxon
function main() int
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
function main() int
    var x = 0
    if 1 > 0 'branch'
        x = 10
    else 'branch'
        x = 20
    end 'branch'
    return x
end 'main'
```
```exitcode
10
```

### Loop Variables

Variables modified in loops are correctly promoted with PHI nodes at the loop header:

```maxon
function main() int
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
function main() int
    var x = 0
    if 5 > 3 'check'
        x = 42
    else 'check'
        x = 100
    end 'check'
    return x
end 'main'
```
```exitcode
42
```

<!-- test: if-else-phi-else-path -->
```maxon
function main() int
    var x = 0
    if 1 > 5 'check'
        x = 42
    else 'check'
        x = 100
    end 'check'
    return x
end 'main'
```
```exitcode
100
```

<!-- test: nested-if-phi -->
```maxon
function main() int
    var result = 0
    if 1 > 0 'outer'
        if 2 > 1 'inner'
            result = 10
        else 'inner'
            result = 20
        end 'inner'
    else 'outer'
        result = 30
    end 'outer'
    return result
end 'main'
```
```exitcode
10
```

<!-- test: loop-phi -->
```maxon
function main() int
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
function main() int
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
function main() int
    var a = 0
    var b = 0
    if 1 > 0 'branch'
        a = 10
        b = 20
    else 'branch'
        a = 100
        b = 200
    end 'branch'
    return a + b
end 'main'
```
```exitcode
30
```

<!-- test: phi-with-computation -->
```maxon
function main() int
    var x = 5
    if x > 3 'check'
        x = x * 2
    else 'check'
        x = x + 10
    end 'check'
    return x
end 'main'
```
```exitcode
10
```

<!-- test: loop-counter -->
```maxon
function main() int
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
