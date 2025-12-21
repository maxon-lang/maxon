---
feature: self-keyword
status: stable
keywords: [self, Self, method, type, instance]
category: type-system
---

# Self and self Keywords

## Developer Notes

The `self` and `Self` keywords enable instance method functionality within type definitions.

**`self` keyword:**
- References the current instance within a method body
- Implicitly available in all non-static methods
- Can be used explicitly (`self.field`) or fields can be accessed directly (just `field`)
- `self` is passed as an implicit first parameter to instance methods

**`Self` keyword:**
- Refers to the enclosing type name
- Useful for returning instances of the type or type annotations within methods
- `Self` in method signatures resolves to the concrete type name

**Parser Changes:**
- `self` token parses as a special identifier in method bodies
- `Self` token parses as a type name that resolves to the enclosing type
- Field access without `self.` prefix looks up fields in the current type context

**IR Generation:**
- Instance methods receive implicit `self` pointer as first parameter
- `self` identifier loads from the first parameter
- Field access `self.field` or bare `field` uses the self pointer + field offset
- Method calls are generated as: `TypeName$methodName(self, args...)`

**Method Name Mangling:**
- Instance methods: `TypeName$methodName`
- Static methods: `TypeName$static$methodName`

## Documentation

### The `self` Keyword

Inside instance methods, `self` refers to the current instance:

```text
type Counter
    var count int

    function increment()
        self.count = self.count + 1
    end 'increment'
end 'Counter'
```

Field access can omit `self.` when unambiguous:

```text
type Counter
    var count int

    function increment()
        count = count + 1
    end 'increment'
end 'Counter'
```

### The `Self` Type

`Self` refers to the enclosing type, useful for method signatures:

```text
type Point
    var x int
    var y int

    function origin() returns Self
        return Point{x: 0, y: 0}
    end 'origin'
end 'Point'
```

## Tests

<!-- test: self-explicit-access -->
```maxon
type Counter
    var count int

    function increment()
        self.count = self.count + 1
    end 'increment'

    function get() returns int
        return self.count
    end 'get'
end 'Counter'

function main() returns int
    var c = Counter{count: 0}
    c.increment()
    c.increment()
    return c.get()
end 'main'
```
```exitcode
2
```

<!-- test: self-implicit-access -->
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

<!-- test: self-with-params -->
```maxon
type Accumulator
    var total int

    function add(value int)
        self.total = self.total + value
    end 'add'

    function getTotal() returns int
        return self.total
    end 'getTotal'
end 'Accumulator'

function main() returns int
    var acc = Accumulator{total: 0}
    acc.add(10)
    acc.add(20)
    acc.add(12)
    return acc.getTotal()
end 'main'
```
```exitcode
42
```

<!-- test: self-multiple-fields -->
```maxon
type Point
    var x int
    var y int

    function sum() returns int
        return self.x + self.y
    end 'sum'

    function setX(newX int)
        self.x = newX
    end 'setX'
end 'Point'

function main() returns int
    var p = Point{x: 10, y: 32}
    return p.sum()
end 'main'
```
```exitcode
42
```

<!-- test: self-modify-and-return -->
```maxon
type Value
    var n int

    function double()
        self.n = self.n * 2
    end 'double'

    function get() returns int
        return self.n
    end 'get'
end 'Value'

function main() returns int
    var v = Value{n: 21}
    v.double()
    return v.get()
end 'main'
```
```exitcode
42
```

<!-- test: self-implicit-multiple-fields -->
```maxon
type Rectangle
    var width int
    var height int

    function area() returns int
        return width * height
    end 'area'
end 'Rectangle'

function main() returns int
    var r = Rectangle{width: 6, height: 7}
    return r.area()
end 'main'
```
```exitcode
42
```
