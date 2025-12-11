---
feature: default-interface-implementations
status: stable
keywords: [interface, default, implementation, method, synthesize]
category: type-system
---

# Default Interface Implementations

## Developer Notes

Default interface implementations allow interfaces to provide a default body for methods that conforming types can inherit if they don't provide their own implementation.

### Implementation Details

**Parser** (`parser/parser_decl.cpp`):
- `parseInterface()` detects default implementations by checking if a function body follows the signature
- If next token after return type is not `function` or `end`, parses body using `parseStatementWithRecovery()`
- Stores body in `InterfaceMethodSignature::defaultBody`
- Requires matching `end 'methodName'` block identifier

**AST** (`ast.h`):
- `InterfaceMethodSignature` has `hasDefaultImplementation` bool and `defaultBody` vector
- `defaultBody` stores the AST statements of the default implementation

**Semantic Analyzer** (`semantic_analyzer.cpp`, `semantic_analyzer.h`):
- `InterfaceMethodInfo` stores `hasDefaultImplementation` and pointer to `defaultBody`
- `checkInterfaceConformance()` checks if method is missing from struct
- If method missing AND interface has default, synthesizes a `FunctionInfo` entry
- Synthesized entry has `isSynthesizedDefault = true`, `defaultBody` pointer, and `typeSubstitutions`
- Type substitutions map `Self` to concrete struct type and associated types to their bindings

**Code Generation** (`codegen_mir.cpp`, `codegen_mir_function.cpp`):
- `synthesizedMethods` vector stores synthesized method info from semantic analyzer
- During declaration pass, synthesized methods are declared as regular functions
- During body generation pass, `generateSynthesizedMethod()` generates code from the default body
- Uses `currentTypeBindings` for type substitution during codegen

### Type Substitution

When generating code for a synthesized default method:
1. `Self` is replaced with the concrete struct type
2. Associated types (e.g., `Element`) are replaced with their bound types
3. Method calls on `self` resolve to the struct's methods

### Override Behavior

If a struct provides its own implementation of a method, the default is NOT used. The struct's explicit implementation takes precedence.

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

When a struct implements an interface with default methods:
- If the struct provides its own implementation, that is used
- If the struct does NOT provide an implementation, the default is automatically synthesized

```maxon
// This struct gets map() automatically from Collection's default
struct IntList is Collection with int
    var data _ManagedArray<int>

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
- `Self` refers to the concrete struct type
- Associated types (like `Element`) resolve to their bound types
- Method calls on `self` use the struct's implementations

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
    printInt(result[0])
    printInt(result[1])
    printInt(result[2])
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
    printInt(squared[0])
    printInt(squared[1])
    printInt(squared[2])
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
