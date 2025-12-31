---
feature: type-methods
status: experimental
keywords: [type, method, function, self, Self, static, export]
category: type-system
---

# Type Methods

## Developer Notes

Type methods are functions declared inside a type definition that operate on instances of that type.

**Declaration:**
- Methods are declared with `function` keyword inside a type body
- Instance methods have implicit `self` parameter referring to the instance
- Static methods use `static function` and don't receive `self`
- Methods can be exported with `export` modifier
- Qualified names like `Interface.methodName` indicate interface method implementations

**AST:**
- `MethodDecl` struct with name, qualified_name, is_static, is_export, params, return_type, body
- `TypeDecl` extended with `methods: []MethodDecl`

**Lexer Keywords:**
- `self` - reference to current instance
- `Self` - type name of enclosing type
- `static` - method without self parameter
- `export` - publicly visible method

**Code Generation:**
- Methods generate IR functions with mangled names: `TypeName$methodName`
- Instance methods receive implicit `self` pointer as first parameter
- Method calls are converted to function calls with receiver as first argument

**Parser:**
- `parseMethodDecl()` handles method signatures inside type bodies
- Methods can appear after fields in type definition
- Method body is parsed same as function body

## Documentation

Types can contain methods - functions that operate on type instances.

### Instance Methods

Instance methods automatically receive `self` as the current instance:

```text
type Counter
    var count int

    function increment()
        count = count + 1
    end 'increment'

    function get() returns int
        return count
    end 'get'
end 'Counter'
```

Call methods using dot notation:
```text
var c = Counter{count: 0}
c.increment()
var value = c.get()
```

### Static Methods

Static methods don't have access to `self`:

```text
type Math
    static function square(x int) returns int
        return x * x
    end 'square'
end 'Math'

var result = Math.square(5)  // 25
```

### Export Modifier

Use `export` to make methods visible outside the module:

```text
type PublicAPI
    export function doSomething() returns int
        return 42
    end 'doSomething'
end 'PublicAPI'
```

### Field Access in Methods

Methods can access fields directly without `self.` prefix:

```text
type Point
    var x int
    var y int

    function magnitude() returns int
        return x * x + y * y
    end 'magnitude'
end 'Point'
```

## Tests

<!-- test: type-method-basic -->
```maxon
type Counter
    var count int

    function increment()
        count = count + 1
    end 'increment'

    function get() returns int
        return count
    end 'get'
end 'Counter'

function main() returns int
    var c = Counter{count: 0}
    c.increment()
    return c.get()
end 'main'
```
```exitcode
1
```

<!-- test: type-method-with-params -->
```maxon
type Adder
    var total int

    function add(value int)
        total = total + value
    end 'add'

    function getTotal() returns int
        return total
    end 'getTotal'
end 'Adder'

function main() returns int
    var a = Adder{total: 0}
    a.add(10)
    a.add(32)
    return a.getTotal()
end 'main'
```
```exitcode
42
```

<!-- test: type-method-returning-value -->
```maxon
type Calculator
    var value int

    function double() returns int
        return value * 2
    end 'double'
end 'Calculator'

function main() returns int
    var c = Calculator{value: 21}
    return c.double()
end 'main'
```
```exitcode
42
```

<!-- test: type-multiple-methods -->
```maxon
type Counter
    var count int

    function increment()
        count = count + 1
    end 'increment'

    function decrement()
        count = count - 1
    end 'decrement'

    function reset()
        count = 0
    end 'reset'

    function get() returns int
        return count
    end 'get'
end 'Counter'

function main() returns int
    var c = Counter{count: 10}
    c.increment()
    c.increment()
    c.decrement()
    return c.get()
end 'main'
```
```exitcode
11
```

<!-- test: type-method-chain -->
```maxon
type Value
    var n int

    function add(x int)
        n = n + x
    end 'add'

    function get() returns int
        return n
    end 'get'
end 'Value'

function main() returns int
    var v = Value{n: 0}
    v.add(10)
    v.add(20)
    v.add(12)
    return v.get()
end 'main'
```
```exitcode
42
```
