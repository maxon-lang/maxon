---
feature: qualified-names
status: stable
keywords: [namespace, qualified, scope, suffix-matching]
category: namespaces
---

# Qualified Names

## Documentation

Call functions from other namespaces using dot notation, or use unqualified names when unambiguous.

### Syntax

```maxon
namespace.functionName(arguments)
// or
functionName(arguments)  // if unambiguous
```
### Example

```maxon
export function add(a Integer, b Integer) returns Integer
  return a + b
end 'add'

function main() returns ExitCode
  return add(10, b: 20)  // Unqualified call within same namespace
end 'main'
```
```exitcode
30
```


## Tests
