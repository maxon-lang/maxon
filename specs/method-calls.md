---
feature: method-calls
status: stable
keywords: [method, call, type, struct, instance]
category: type-system
---

# Method Calls

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

typealias Integer = int(i64.min to i64.max)

type Counter
  var count Integer

  function increment()
    count = count + 1
  end 'increment'

  function get() returns Integer
    return count
  end 'get'
end 'Counter'

function main() returns ExitCode
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

typealias Integer = int(i64.min to i64.max)

type Adder
  var total Integer

  function add(n Integer)
    total = total + n
  end 'add'

  function get() returns Integer
    return total
  end 'get'
end 'Adder'

function main() returns ExitCode
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

typealias Integer = int(i64.min to i64.max)

type Box
  var value Integer

  function getValue() returns Integer
    return value
  end 'getValue'
end 'Box'

function main() returns ExitCode
  var b = Box{value: 40}
  return b.getValue() + 2
end 'main'
```
```exitcode
42
```

<!-- test: method-multiple-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Calculator
  var result Integer

  function addTwo(a Integer, b Integer)
    result = result + a + b
  end 'addTwo'

  function get() returns Integer
    return result
  end 'get'
end 'Calculator'

function main() returns ExitCode
  var calc = Calculator{result: 0}
  calc.addTwo(20, b: 22)
  return calc.get()
end 'main'
```
```exitcode
42
```

<!-- test: method-call-on-field-access -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Inner
  var value Integer

  function get() returns Integer
    return value
  end 'get'
end 'Inner'

type Outer
  var inner Inner

  function getInnerValue() returns Integer
    return inner.get()
  end 'getInnerValue'
end 'Outer'

function main() returns ExitCode
  var o = Outer{inner: Inner{value: 42}}
  return o.getInnerValue()
end 'main'
```
```exitcode
42
```

<!-- test: method-modify-multiple-fields -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
  var x Integer
  var y Integer

  function moveBy(dx Integer, dy Integer)
    x = x + dx
    y = y + dy
  end 'moveBy'

  function sum() returns Integer
    return x + y
  end 'sum'
end 'Point'

function main() returns ExitCode
  var p = Point{x: 10, y: 10}
  p.moveBy(10, dy: 12)
  return p.sum()
end 'main'
```
```exitcode
42
```

<!-- test: method-return-comparison -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Value
  var n Integer

  function isPositive() returns Integer
    if n > 0 'positive'
      return 1
    end 'positive'
    return 0
  end 'isPositive'
end 'Value'

function main() returns ExitCode
  var v = Value{n: 42}
  return v.isPositive()
end 'main'
```
```exitcode
1
```

<!-- test: error-method-unnamed-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Adder
  var total Integer

  function addTwo(a Integer, b Integer)
    total = total + a + b
  end 'addTwo'
end 'Adder'

function main() returns ExitCode
  var x = Adder{total: 0}
  x.addTwo(10, 20)
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/method-calls/error-method-unnamed-args.test:15:5: Second and subsequent arguments must be named. Use 'name: value' syntax
```
```maxon

typealias Integer = int(i64.min to i64.max)

type Calculator
  var result Integer

  function compute(a Integer, b Integer, c Integer)
    result = a + b * c
  end 'compute'

  function get() returns Integer
    return result
  end 'get'
end 'Calculator'

function main() returns ExitCode
  var calc = Calculator{result: 0}
  calc.compute(10, c: 4, b: 8)
  return calc.get()
end 'main'
```
```exitcode
42
```

<!-- test: static-method-named-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Factory
  static function create(x Integer, y Integer) returns Integer
    return x * 10 + y
  end 'create'
end 'Factory'

function main() returns ExitCode
  return Factory.create(4, y: 2)
end 'main'
```
```exitcode
42
```

<!-- test: error-static-method-unnamed-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Factory
  static function create(x Integer, y Integer) returns Integer
    return x * 10 + y
  end 'create'
end 'Factory'

function main() returns ExitCode
  return Factory.create(4, 2)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/method-calls/error-static-method-unnamed-args.test:12:18: Second and subsequent arguments must be named. Use 'name: value' syntax
```
