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
typealias Score = i64

export function add(a Score, b Score) returns Score
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
