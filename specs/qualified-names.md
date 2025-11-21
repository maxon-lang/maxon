---
feature: qualified-names
status: stable
keywords: [namespace, qualified, scope, error]
category: namespaces
---

# Qualified Names

## Developer Notes

Qualified names use dot notation to call functions from a specific namespace: `namespace.function()`.

Implementation:
- Parsed in `Parser::parseFunctionCall()`
- Syntax: `identifier '.' identifier '(' args ')'`
- Semantic analyzer resolves namespace and function
- Error if qualified name used from within the same namespace (unnecessary qualification)
- Error if namespace doesn't exist or function not found in namespace

The compiler enforces that qualified names are only used when necessary (calling from outside the namespace).

## Documentation

Call functions from other namespaces using dot notation.

### Syntax

```maxon
namespace.functionName(arguments)
```

### Example

```maxon
namespace math 'math'
    function add(a int, b int) int
        return a + b
    end 'add'
end 'math'

function main() int
    return add(10, 20)
end 'main'
```
```output
ExitCode: 30
```

## Tests

<!-- test: qualified-call -->
```maxon
namespace math 'math'
    function add(a int, b int) int
        return a + b
    end 'add'
end 'math'

function main() int
    return add(10, 20)
end 'main'
```
```
ExitCode: 30
```

<!-- test: cross-namespace -->
```maxon
namespace utils 'utils'
    function double(x int) int
        return x * 2
    end 'double'
end 'utils'

namespace calc 'calc'
    function test() int
        return double(5)
    end 'test'
end 'calc'

function main() int
    return test()
end 'main'
```
```
ExitCode: 10
```

<!-- test: unnecessary-qualification -->
```maxon
namespace math 'math'
    function add(a int, b int) int
        return a + b
    end 'add'
end 'math'

function main() int
    return math.add(5, 3)
end 'main'
```
```
MaxoncStderr: Semantic Error: line 9, column 12
```
