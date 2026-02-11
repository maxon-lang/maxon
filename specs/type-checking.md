---
status: implemented
---

# Type Checking

## Notes
Type checking ensures argument types match parameter types at compile time.

## Docs
The compiler validates that function and method arguments match the expected parameter types. Type mismatches are reported as compile-time errors (E3005).

## Tests

<!-- test: method-call-wrong-self-type -->
```maxon
typealias StringArray = Array with String

function main() returns int
  var arr = StringArray{}
  arr.append("hello")
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/method-call-wrong-self-type.test:6:7: argument type mismatch for 'other': expected 'StringArray', got 'String'
```

<!-- test: method-call-wrong-element-type -->
```maxon
typealias IntArray = Array with int

function main() returns int
  var arr = IntArray{}
  arr.push("hello")
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/method-call-wrong-element-type.test:6:7: argument type mismatch for 'value': expected 'int', got 'String'
```

<!-- test: function-call-string-where-int-expected -->
```maxon
function takeInt(n int) returns int
  return n
end 'takeInt'

function main() returns int
  takeInt("hello")
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/function-call-string-where-int-expected.test:7:3: argument type mismatch for 'n': expected 'int', got 'String'
```

<!-- test: function-call-primitive-where-struct-expected -->
```maxon
typealias IntArray = Array with int

function takeArray(arr IntArray) returns int
  return arr.count()
end 'takeArray'

function main() returns int
  takeArray(42)
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/function-call-primitive-where-struct-expected.test:9:3: argument type mismatch for 'arr': expected 'IntArray', got 'int'
```

<!-- test: function-call-wrong-struct-type -->
```maxon
type Point
  export var x int
  export var y int
end 'Point'

type Size
  export var w int
  export var h int
end 'Size'

function takePoint(p Point) returns int
  return p.x
end 'takePoint'

function main() returns int
  var s = Size{}
  takePoint(s)
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/function-call-wrong-struct-type.test:18:3: argument type mismatch for 'p': expected 'Point', got 'Size'
```

<!-- test: stdlib-function-call-wrong-type -->
```maxon
function main() returns int
  print(42)
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/stdlib-function-call-wrong-type.test:3:3: argument type mismatch for 'value': expected 'String', got 'int'
```

<!-- test: implicit-method-call-wrong-type -->
```maxon
typealias IntArray = Array with int

type Container
  var items IntArray

  function addWrong(s String)
  items.push(s)
  end 'addWrong'
end 'Container'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/implicit-method-call-wrong-type.test:8:9: argument type mismatch for 'value': expected 'int', got 'String'
```

<!-- test: array-of-different-element-types -->
```maxon
typealias IntArray = Array with int
typealias StringArray = Array with String

function main() returns int
  var ints = IntArray{}
  var strings = StringArray{}
  ints.append(strings)
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/array-of-different-element-types.test:8:8: argument type mismatch for 'other': expected 'IntArray', got 'StringArray'
```

<!-- test: typealias-forward-reference -->
```maxon
typealias FooArray = Array with Foo

type Foo
  let value int
end 'Foo'

function main() returns int
  var arr = FooArray{}
  arr.push({value: 42})
  return arr.count() - 1
end 'main'
```
```exitcode
0
```

<!-- test: typealias-after-usage -->
A typealias defined after the type that uses it as a field should resolve correctly.
```maxon
type Container
  export var items ItemArray
end 'Container'

typealias ItemArray = Array with Item

type Item
  export let value int
end 'Item'

function main() returns int
  var c = Container{items: ItemArray{}}
  c.items.push({value: 7})
  return c.items.count()
end 'main'
```
```exitcode
1
```
