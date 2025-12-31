---
feature: static-methods
status: stable
keywords: [static, method, type, function]
category: types
---

# Static Methods

## Developer Notes

Static methods are functions defined inside type bodies that don't have an implicit `self` parameter. They belong to the type namespace but don't operate on instances.

### Implementation Details

- Parser: Checks for `static` keyword before `function` in method parsing
- AST: `FunctionAST.isStaticMethod` flag distinguishes static from instance methods
- Semantic Analyzer: Allows `Type.staticMethod()` calls for static methods
- Codegen: Doesn't set `currentReceiverType` for static methods, preventing implicit self access

### Key Files

- `ast.h`: Added `isStaticMethod` field to `FunctionAST`
- `parser/parser_decl.cpp`: Modified `parseMethodImpl` to handle `static` keyword
- `semantic_analyzer/semantic_analyzer_expr.cpp`: Allow qualified calls for static methods
- `codegen_mir/codegen_mir_function.cpp`: Skip self parameter handling for static methods

## Documentation

Static methods are functions that belong to a type but don't have access to instance data. They are called using `TypeName.methodName()` syntax.

### Declaring Static Methods

Use the `static` keyword before `function` inside a type body:

```text
type Point
    var x int
    var y int

    static function origin() returns Point
        return Point{x: 0, y: 0}
    end 'origin'

    static function create(x int, y int) returns Point
        return Point{x: x, y: y}
    end 'create'
end 'Point'
```

### Calling Static Methods

Static methods are called on the type name, not on instances:

```text
var p1 = Point.origin()
var p2 = Point.create(10, 20)
```

### Differences from Instance Methods

| Feature | Instance Method | Static Method |
|---------|----------------|---------------|
| Has `self` | Yes (implicit) | No |
| Can access fields | Yes | No |
| Call syntax | `instance.method()` | `Type.method()` |
| Keyword | `function` | `static function` |

### Common Use Cases

1. **Factory methods**: Create instances with specific configurations
2. **Utility functions**: Type-related operations that don't need instance data
3. **Constants**: Return predefined values associated with the type

## Tests

<!-- test: static-basic -->
```maxon
type Point
    var x int
    var y int

    static function create(x int, y int) returns Point
        return Point{x: x, y: y}
    end 'create'
end 'Point'

function main() returns int
    var p = Point.create(10, 20)
    print("{p.x},{p.y}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
10,20
```

<!-- test: static-no-params -->
```maxon
type Point
    var x int
    var y int

    static function origin() returns Point
        return Point{x: 0, y: 0}
    end 'origin'
end 'Point'

function main() returns int
    var p = Point.origin()
    print("{p.x},{p.y}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
0,0
```

<!-- test: static-returns-primitive -->
```maxon
type Config
    var value int

    static function defaultValue() returns int
        return 42
    end 'defaultValue'
end 'Config'

function main() returns int
    var v = Config.defaultValue()
    print("{v}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: static-with-instance-method -->
```maxon
type Counter
    var count int

    static function zero() returns Counter
        return Counter{count: 0}
    end 'zero'

    function increment()
        count = count + 1
    end 'increment'

    function getValue() returns int
        return count
    end 'getValue'
end 'Counter'

function main() returns int
    var c = Counter.zero()
    var i = 0
    while i < 15 'loop'
        c.increment()
        i = i + 1
    end 'loop'
    print("{c.getValue()}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
15
```

<!-- test: static-exported -->
```maxon
type Value
    var n int

    export static function hundred() returns Value
        return Value{n: 100}
    end 'hundred'
end 'Value'

function main() returns int
    var v = Value.hundred()
    print("{v.n}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
100
```

<!-- test: static-multiple -->
```maxon
type Triple
    var a int
    var b int
    var c int

    static function ones() returns Triple
        return Triple{a: 1, b: 1, c: 1}
    end 'ones'

    static function sequential() returns Triple
        return Triple{a: 1, b: 2, c: 3}
    end 'sequential'
end 'Triple'

function main() returns int
    var t = Triple.sequential()
    print("{t.a},{t.b},{t.c}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1,2,3
```
