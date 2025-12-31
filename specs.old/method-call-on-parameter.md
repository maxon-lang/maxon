---
feature: method-call-on-parameter
status: stable
keywords: method, call, parameter, same type, sibling
category: expressions
---

## Developer Notes

When inside a method, calling a method on a parameter of the same type must work correctly.
The compiler must distinguish between:
1. Sibling method calls: `count()` inside `string` calls `self.count()`
2. Method calls on parameters: `other.count()` inside `string` calls `other.count()`

**The Bug:**
When we call `other.method()` where `other` is a parameter of the same type as `self`,
the semantic analyzer incorrectly treats it as a sibling method call. This causes:
- The compiler to skip parameter 0 (self) thinking it will be injected
- But the parser already added `other` as args[0]
- Result: "Too many positional arguments" error

**Fix:**
A sibling method call is ONLY when:
1. We're inside a method (currentReceiverType is set)
2. The called method is from the same type
3. The call has NO explicit receiver (args.size() < expected params - 1)

If args[0] already provides a value for `self`, it's NOT a sibling call.

## Documentation

# Method Calls on Parameters of the Same Type

When writing a method, you can call other methods on parameters that have the same type as `self`.

## Example

```maxon
type Counter
    var value int
    
    function getValue() returns int
        return value
    end 'getValue'
    
    function addFrom(other Counter) returns int
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
type Foo
    var x int
    
    function bar() returns int
        return x
    end 'bar'
    
    function callBarOn(other Foo) returns int
        return other.bar()
    end 'callBarOn'
end 'Foo'

function main() returns int
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
type Value
    var n int
    
    function get() returns int
        return n
    end 'get'
    
    function add(other Value) returns int
        return n + other.get()
    end 'add'
    
    function multiply(other Value) returns int
        return n * other.get()
    end 'multiply'
end 'Value'

function main() returns int
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
type Calculator
    var base int
    
    function double() returns int
        return base * 2
    end 'double'
    
    function quadruple() returns int
        // Sibling call - calls self.double()
        return double() * 2
    end 'quadruple'
end 'Calculator'

function main() returns int
    var calc = Calculator{base: 5}
    return calc.quadruple()
end 'main'
```
```exitcode
20
```

<!-- test: method-with-args-on-same-type-parameter -->
```maxon
type Adder
    var value int
    
    function addTo(n int) returns int
        return value + n
    end 'addTo'
    
    function delegateAdd(other Adder, n int) returns int
        return other.addTo(n)
    end 'delegateAdd'
end 'Adder'

function main() returns int
    var a = Adder{value: 100}
    var b = Adder{value: 50}
    return a.delegateAdd(b, 7)
end 'main'
```
```exitcode
57
```
