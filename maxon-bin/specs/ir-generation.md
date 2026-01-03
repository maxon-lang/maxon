---
feature: ir-generation
status: stable
keywords: [ir, llvm, codegen, optimization]
category: compilation
---

# IR Generation

## Documentation

The compiler generates LLVM Intermediate Representation (IR) code.

### Optimization Modes

**Optimized Mode** (default):
- Maximum performance
- Smaller code size
- Harder to debug

**Debug Mode** (`-g` flag):
- Preserves source structure
- Easier debugging
- Slower execution

### Example

Source code:
```maxon
function main() returns int
    var sum = 0
    var i = 0
    while i < 10 'loop'
        sum = sum + i
        i = i + 1
    end 'loop'
    return sum
end 'main'
```
```exitcode
45
```


Optimized IR (simplified):
```llvm
define i32 @main() {
  ret i32 45  ; Loop fully computed at compile time
}
```

Debug IR would preserve the full loop structure with all variables and operations.

## Tests

<!-- test: optimized-loop -->
```maxon
function main() returns int
    var sum = 0
    var i = 0
    while i < 10 'loop'
        sum = sum + i
        i = i + 1
    end 'loop'
    return sum
end 'main'
```
```exitcode
45
```


<!-- test: optimization-verification -->
```maxon
function compute() returns int
    var result = 0
    var x = 1
    while x <= 5 'loop'
        result = result + x * 2
        x = x + 1
    end 'loop'
    return result
end 'compute'

function main() returns int
    return compute()
end 'main'
```
```exitcode
30
```

