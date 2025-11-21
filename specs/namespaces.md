---
feature: namespaces
status: stable
keywords: [namespace, organization, scope]
category: organization
---

# Namespaces

## Developer Notes

Namespaces provide logical grouping of functions and prevent name collisions. Key implementation:

- Parsed in `Parser::parseNamespace()`
- Represented by `NamespaceDecl` AST node
- Namespaces can contain function declarations
- Names are mangled with namespace prefix for uniqueness
- Functions in a namespace can call other functions in the same namespace without qualification
- Calling from outside requires qualified name: `namespace::function()`
- Block identifiers work the same as for functions

The semantic analyzer handles name resolution, looking up symbols first in local scope, then in namespace scope, then globally.

## Documentation

Namespaces group related functions together and help organize code.

### Syntax

```maxon
namespace name 'name'
    // function declarations
end 'name'
```

### Example

```maxon
namespace math 'math'
    function add(a int, b int) int
        return a + b
    end 'add'
    
    function multiply(x int, y int) int
        return x * y
    end 'multiply'
end 'math'

function main() int
    return add(3, 4)  // Called from within namespace
end 'main'
```

## Tests

<!-- test: basic-namespace -->
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

<!-- test: multiple-functions -->
```maxon
namespace utils 'utils'
    function double(x int) int
        return x * 2
    end 'double'
    
    function triple(x int) int
        return x * 3
    end 'triple'
end 'utils'

function main() int
    return double(5) + triple(4)
end 'main'
```
```
ExitCode: 22
```

<!-- test: nested-calls-in-namespace -->
```maxon
namespace calc 'calc'
    function add(a int, b int) int
        return a + b
    end 'add'
    
    function sum_three(a int, b int, c int) int
        return add(add(a, b), c)
    end 'sum_three'
end 'calc'

function main() int
    return sum_three(1, 2, 3)
end 'main'
```
```
ExitCode: 6
```
