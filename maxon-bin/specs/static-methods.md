---
feature: static-methods
status: stable
keywords: [static, methods, type, constructor]
category: types
---

# Static Methods

## Developer Notes

Static methods are methods on a type that don't receive an implicit `self` parameter. They're useful for constructors, factory functions, and utility methods that don't need instance data.

**Implementation:**
- Parser: Checks for `static` keyword before `function` in type body
- AST: `MethodDecl.is_static` flag
- IR: Static methods don't get implicit `self` parameter
- Mangling: `TypeName$methodName` (same as instance methods)

**Key difference from instance methods:**
- No implicit `self` parameter
- Cannot access `self` or instance fields
- Called as `TypeName.method()` from outside the type

## Documentation

### Static Methods

Use the `static` keyword to declare a method that doesn't operate on an instance:

```text
type Counter
    var value int

    static function create() returns Counter
        return Counter{value: 0}
    end 'create'

    function increment()
        self.value = self.value + 1
    end 'increment'
end 'Counter'

var c = Counter.create()
c.increment()
```

Static methods are commonly used for:
- Constructors that return new instances
- Factory methods with custom initialization logic
- Utility functions related to the type

## Tests

<!-- test: static-method-basic -->
```maxon
type Box
    var value int

    static function make(v int) returns Box
        return Box{value: v}
    end 'make'
end 'Box'

function main() returns int
    var b = Box.make(42)
    return b.value
end 'main'
```
```exitcode
42
```

<!-- test: static-method-no-self -->
```maxon
type Math
    var unused int

    static function add(a int, b int) returns int
        return a + b
    end 'add'
end 'Math'

function main() returns int
    return Math.add(20, 22)
end 'main'
```
```exitcode
42
```

<!-- test: export-static-method -->
```maxon
type Factory
    var id int

    export static function create(val int) returns Factory
        return Factory{id: val}
    end 'create'

    export static function zero() returns Factory
        return Factory.create(0)
    end 'zero'
end 'Factory'

function main() returns int
    var f1 = Factory.create(100)
    var f2 = Factory.zero()
    return f1.id + f2.id
end 'main'
```
```exitcode
100
```
