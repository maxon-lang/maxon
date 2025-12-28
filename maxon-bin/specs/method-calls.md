---
feature: method-calls
status: stable
keywords: [method, call, type, struct, instance]
category: type-system
---

# Method Calls

## Developer Notes

Method calls allow invoking functions defined on types. The compiler generates IR that passes the instance as an implicit first argument.

**Method Resolution:**
1. Convert base expression to get its type
2. If struct type, look up `TypeName$methodName` in func_map
3. Generate call with base value as first argument (self)
4. Handle return type appropriately

**IR Generation:**
- Method `foo.bar(x)` becomes `call @Foo$bar(%foo_ptr, %x)`
- Instance methods receive implicit `self` pointer as first parameter
- Return values work identically to regular functions

**Name Mangling:**
- Instance methods: `TypeName$methodName`
- Static methods: `TypeName$static$methodName` (future)

**Supported Patterns:**
- Simple: `obj.method()`
- With args: `obj.method(arg1, arg2)`
- Returning values: `let x = obj.getValue()`
- Chained: `obj.method1().method2()` (when method1 returns compatible type)

## Documentation

### Calling Methods

Methods are called using dot notation on an instance:

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

function main() returns int
    var c = Counter{count: 0}
    c.increment()
    return c.get()
end 'main'
```

### Methods with Parameters

Methods can take parameters in addition to the implicit self:

```text
type Adder
    var value int

    function add(n int)
        value = value + n
    end 'add'
end 'Adder'
```

### Methods Returning Values

Methods can return values that can be used in expressions:

```text
type Box
    var value int

    function getValue() returns int
        return value
    end 'getValue'
end 'Box'

function main() returns int
    var b = Box{value: 42}
    return b.getValue() + 1  // 43
end 'main'
```

## Tests

<!-- test: method-call-void -->
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
    c.increment()
    c.increment()
    return c.get()
end 'main'
```
```exitcode
3
```

<!-- test: method-call-with-args -->
```maxon
type Adder
    var total int

    function add(n int)
        total = total + n
    end 'add'

    function get() returns int
        return total
    end 'get'
end 'Adder'

function main() returns int
    var a = Adder{total: 0}
    a.add(10)
    a.add(20)
    a.add(12)
    return a.get()
end 'main'
```
```exitcode
42
```

<!-- test: method-return-in-expr -->
```maxon
type Box
    var value int

    function getValue() returns int
        return value
    end 'getValue'
end 'Box'

function main() returns int
    var b = Box{value: 40}
    return b.getValue() + 2
end 'main'
```
```exitcode
42
```

<!-- test: method-multiple-args -->
```maxon
type Calculator
    var result int

    function addTwo(a int, b int)
        result = result + a + b
    end 'addTwo'

    function get() returns int
        return result
    end 'get'
end 'Calculator'

function main() returns int
    var calc = Calculator{result: 0}
    calc.addTwo(20, 22)
    return calc.get()
end 'main'
```
```exitcode
42
```

<!-- test: method-call-on-field-access -->
```maxon
type Inner
    var value int

    function get() returns int
        return value
    end 'get'
end 'Inner'

type Outer
    var inner Inner

    function getInnerValue() returns int
        return inner.get()
    end 'getInnerValue'
end 'Outer'

function main() returns int
    var o = Outer{inner: Inner{value: 42}}
    return o.getInnerValue()
end 'main'
```
```exitcode
42
```

<!-- test: method-modify-multiple-fields -->
```maxon
type Point
    var x int
    var y int

    function moveBy(dx int, dy int)
        x = x + dx
        y = y + dy
    end 'moveBy'

    function sum() returns int
        return x + y
    end 'sum'
end 'Point'

function main() returns int
    var p = Point{x: 10, y: 10}
    p.moveBy(10, 12)
    return p.sum()
end 'main'
```
```exitcode
42
```

<!-- test: method-return-comparison -->
```maxon
type Value
    var n int

    function isPositive() returns int
        if n > 0 'positive'
            return 1
        end 'positive'
        return 0
    end 'isPositive'
end 'Value'

function main() returns int
    var v = Value{n: 42}
    return v.isPositive()
end 'main'
```
```exitcode
1
```
