---
feature: qualified-names
status: stable
keywords: [namespace, qualified, scope, suffix-matching]
category: namespaces
---

# Qualified Names

## Developer Notes

Qualified names use dot notation to call functions from specific namespaces: `namespace.function()`.

Implementation:
- Parsed in `Parser::parseFunctionCall()`
- Syntax: `identifier '.' identifier '(' args ')'`
- Functions stored with qualified names: `namespace.function`
- Unqualified calls use suffix matching (`.functionName`)
- Semantic analyzer validates ambiguous calls
- Works across file boundaries using suffix matching

The compiler allows both qualified (`math.add`) and unqualified (`add`) calls, using suffix matching to resolve unqualified names.

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
export function add(a int, b int) int
    return a + b
end 'add'

function main() int
    return add(10, 20)  // Unqualified call within same namespace
end 'main'
```
```exitcode
30
```


## Tests
