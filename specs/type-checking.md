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

function main() returns ExitCode
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
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push("hello")
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/method-call-wrong-element-type.test:7:7: argument type mismatch for 'value': expected 'Int', got 'String'
```

<!-- test: function-call-string-where-int-expected -->
```maxon

typealias Integer = int(i64.min to i64.max)

function takeInt(n Integer) returns Integer
  return n
end 'takeInt'

function main() returns ExitCode
  takeInt("hello")
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/function-call-string-where-int-expected.test:10:3: argument type mismatch for 'n': expected 'Integer', got 'String'
```

<!-- test: function-call-primitive-where-struct-expected -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias IntegerArray = Array with Integer

function takeArray(arr IntegerArray) returns Integer
  return arr.count()
end 'takeArray'

function main() returns ExitCode
  takeArray(42)
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/function-call-primitive-where-struct-expected.test:12:3: argument type mismatch for 'arr': expected 'IntegerArray', got 'int'
```

<!-- test: function-call-wrong-struct-type -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

type Size
  export var w Integer
  export var h Integer
end 'Size'

function takePoint(p Point) returns Integer
  return p.x
end 'takePoint'

function main() returns ExitCode
  var s = Size{}
  takePoint(s)
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/function-call-wrong-struct-type.test:21:3: argument type mismatch for 'p': expected 'Point', got 'Size'
```

<!-- test: stdlib-function-call-wrong-type -->
```maxon
function main() returns ExitCode
  print(42)
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/stdlib-function-call-wrong-type.test:3:3: argument type mismatch for 'value': expected 'String', got 'int'
```

<!-- test: implicit-method-call-wrong-type -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Container
  var items IntArray

  function addWrong(s String)
  items.push(s)
  end 'addWrong'
end 'Container'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/implicit-method-call-wrong-type.test:9:9: argument type mismatch for 'value': expected 'Int', got 'String'
```

<!-- test: array-of-different-element-types -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int
typealias StringArray = Array with String

function main() returns ExitCode
  var ints = IntArray{}
  var strings = StringArray{}
  ints.append(strings)
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-checking/array-of-different-element-types.test:9:8: argument type mismatch for 'other': expected 'IntArray', got 'StringArray'
```

<!-- test: typealias-forward-reference -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FooArray = Array with Foo

type Foo
  let value Integer
end 'Foo'

function main() returns ExitCode
  var arr = FooArray{}
  arr.push(Foo{value: 42})
  return arr.count() - 1
end 'main'
```
```exitcode
0
```

<!-- test: typealias-after-usage -->
A typealias defined after the type that uses it as a field should resolve correctly.
```maxon

typealias Integer = int(i64.min to i64.max)

type Container
  export var items ItemArray
end 'Container'

typealias ItemArray = Array with Item

type Item
  export let value Integer
end 'Item'

function main() returns ExitCode
  var c = Container{items: ItemArray{}}
  c.items.push(Item{value: 7})
  return c.items.count()
end 'main'
```
```exitcode
1
```

<!-- test: error.circular-typealias-self-reference -->
A typealias that references itself should be a compile error.
```maxon
typealias A = Array with A

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E2012: specs/fragments/type-checking/error.circular-typealias-self-reference.test:2:26: Circular typealias dependency: A
```

<!-- test: error.circular-typealias-mutual -->
Two typealiases that reference each other should be a compile error.
```maxon
typealias A = Array with B
typealias B = Array with A

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E2012: specs/fragments/type-checking/error.circular-typealias-mutual.test:2:11: Circular typealias dependency: A -> B -> A
```
