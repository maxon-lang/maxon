---
feature: default-interface-implementations
status: stable
keywords: [interface, default, implementation, method, synthesize]
category: type-system
---

# Default Interface Implementations

## Documentation

### Default Method Syntax

Interfaces can provide default implementations by including a function body. Methods without a body are abstract (must be implemented by conforming types). Methods with a body provide a default that conforming types inherit automatically:

```maxon
interface Collection uses Element extends Iterable
    function count() returns int                           // Abstract - must be implemented
    function get(index int) returns Element or nil         // Abstract - must be implemented
    function set(index int, value Element) returns Self    // Abstract - must be implemented

    // Default implementation - structs inherit this unless they override
    function map(transform (Element) Element) returns Self
        var i = 0
        while i < self.count() 'loop'
            if let elem = self.get(i) 'get'
                var transformed = transform(elem)
                self = self.set(i, transformed)
            end 'get'
            i = i + 1
        end 'loop'
        return self
    end 'map'
end 'Collection'
```

### Automatic Inheritance

When a type implements an interface with default methods:
- If the type provides its own implementation, that is used
- If the type does NOT provide an implementation, the default is automatically synthesized

```maxon
// This type gets map() automatically from Collection's default
type IntList is Collection with int
    var data __ManagedArray<int>

    function Collection.count() returns int
        return __managed_array_len(self.data)
    end 'count'

    function Collection.get(index int) returns int
        return __managed_array_get_at(self.data, index)
    end 'get'

    function Collection.set(index int, value int) IntList
        __managed_array_set_at(self.data, index, value)
        return self
    end 'set'

    // Note: No map() implementation - uses Collection's default
end 'IntList'
```

### Type Substitution

In default implementations:
- `Self` refers to the concrete type
- Associated types (like `Element`) resolve to their bound types
- Method calls on `self` use the type's implementations

### Benefits

1. **Code reuse**: Common method implementations don't need to be repeated
2. **API evolution**: New methods can be added to interfaces with defaults without breaking existing implementations
3. **Separation of concerns**: Core operations (count, get, set) are defined by implementers, derived operations (map) provided by interface

## Tests

<!-- test: default-map-on-array -->
```maxon
function double(x int) returns int
    return x * 2
end 'double'

function main() returns int
    var arr = [1, 2, 3]
    var result = arr.map(double)
    print("{result[0]}\n")
    print("{result[1]}\n")
    print("{result[2]}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
2
4
6
```

<!-- test: default-map-with-closure -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    var squared = arr.map((x int) gives x * x)
    print("{squared[0]}\n")
    print("{squared[1]}\n")
    print("{squared[2]}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
100
400
900
```
