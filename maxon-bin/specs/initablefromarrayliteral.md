---
feature: initablefromarrayliteral
status: stable
keywords: [array, literal, interface, InitableFromArrayLiteral, generic, type annotation]
category: type-system
---

# InitableFromArrayLiteral Interface

## Documentation

### Array Literals with Type Annotations

The stdlib `Array` type implements `InitableFromArrayLiteral`, allowing initialization from array literals:

```text
var arr = [1, 2, 3]
```

This creates an Array containing the elements 1, 2, and 3.

## Tests

<!-- test: array-from-literal -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    return arr.count()
end 'main'
```
```exitcode
3
```

<!-- test: array-from-literal-access -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    var val = arr.get(1)
    if let v = val 'check'
        return v
    end 'check'
    return 0
end 'main'
```
```exitcode
20
```

<!-- test: array-from-literal-first -->
```maxon
function main() returns int
    var arr = [42, 2, 3]
    var val = arr.first()
    if let v = val 'check'
        return v
    end 'check'
    return 0
end 'main'
```
```exitcode
42
```

<!-- test: array-from-literal-last -->
```maxon
function main() returns int
    var arr = [1, 2, 99]
    var val = arr.last()
    if let v = val 'check'
        return v
    end 'check'
    return 0
end 'main'
```
```exitcode
99
```
