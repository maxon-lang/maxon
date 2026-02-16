---
feature: method-call-on-parameter
status: stable
keywords: method, call, parameter, same type, sibling
category: expressions
---
# Method Calls on Parameters of the Same Type

## Documentation

When writing a method, you can call other methods on parameters that have the same type as `self`.

## Example

```maxon
typealias Score = i64

type Counter
  var value Score

  function getValue() returns Score
    return value
  end 'getValue'

  function addFrom(other Counter) returns Score
    // Call getValue() on 'other', not on 'self'
    return value + other.getValue()
  end 'addFrom'
end 'Counter'
```

The call `other.getValue()` correctly invokes `getValue` on the `other` parameter,
not on `self`.

## Tests

<!-- test: method-call-on-same-type-parameter -->
```maxon

typealias Integer = i64

type Foo
  var x Integer
  
  function bar() returns Integer
    return x
  end 'bar'
  
  function callBarOn(other Foo) returns Integer
    return other.bar()
  end 'callBarOn'
end 'Foo'

function main() returns ExitCode
  var f1 = Foo{x: 10}
  var f2 = Foo{x: 42}
  return f1.callBarOn(f2)
end 'main'
```
```exitcode
42
```

<!-- test: method-call-chain-same-type -->
```maxon

typealias Integer = i64

type Value
  var n Integer
  
  function get() returns Integer
    return n
  end 'get'
  
  function add(other Value) returns Integer
    return n + other.get()
  end 'add'
  
  function multiply(other Value) returns Integer
    return n * other.get()
  end 'multiply'
end 'Value'

function main() returns ExitCode
  var a = Value{n: 5}
  var b = Value{n: 3}
  var c = Value{n: 2}
  // a.add(b) = 5 + 3 = 8
  // a.multiply(c) = 5 * 2 = 10
  // total = 8 + 10 = 18
  return a.add(b) + a.multiply(c)
end 'main'
```
```exitcode
18
```

<!-- test: sibling-method-call-still-works -->
```maxon

typealias Integer = i64

type Calculator
  var base Integer
  
  function double() returns Integer
    return base * 2
  end 'double'
  
  function quadruple() returns Integer
    // Sibling call - calls self.double()
    return double() * 2
  end 'quadruple'
end 'Calculator'

function main() returns ExitCode
  var calc = Calculator{base: 5}
  return calc.quadruple()
end 'main'
```
```exitcode
20
```

<!-- test: method-with-args-on-same-type-parameter -->
```maxon

typealias Integer = i64

type Adder
  var value Integer
  
  function addTo(n Integer) returns Integer
    return value + n
  end 'addTo'
  
  function delegateAdd(other Adder, n Integer) returns Integer
    return other.addTo(n)
  end 'delegateAdd'
end 'Adder'

function main() returns ExitCode
  var a = Adder{value: 100}
  var b = Adder{value: 50}
  return a.delegateAdd(b, n: 7)
end 'main'
```
```exitcode
57
```
