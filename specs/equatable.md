---
feature: equatable
status: stable
keywords: [equatable, equals, equality, interface, conformance]
category: type-system
---

# Equatable

## Documentation

### Overview

`Equatable` is a standard library interface for types that can be compared for equality. Types conforming to `Equatable` implement an `equals` method that compares two instances of the same type.

**Interface definition:**

```text
export interface Equatable
  function equals(other Self) returns bool
end 'Equatable'
```

### Conformance

Declare conformance with `implements Equatable` and implement `equals`:

```text
type Point implements Equatable
  var x int
  var y int

  function equals(other Point) returns bool
    return x == other.x and y == other.y
  end 'equals'
end 'Point'
```

### Usage

Call `equals` on an instance, passing another instance of the same type:

```text
var a = Point{x: 1, y: 2}
var b = Point{x: 1, y: 2}
var same = a.equals(b)  // true
```

## Tests

<!-- test: basic-equal -->
```maxon
type Point implements Equatable
  export var x Integer
  export var y Integer

  function equals(other Point) returns bool
    return x == other.x and y == other.y
  end 'equals'
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = Point{x: 1, y: 2}
  if a.equals(b) 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: basic-not-equal -->
```maxon
type Point implements Equatable
  export var x Integer
  export var y Integer

  function equals(other Point) returns bool
    return x == other.x and y == other.y
  end 'equals'
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = Point{x: 3, y: 4}
  if a.equals(b) 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: single-field -->
```maxon
type Wrapper implements Equatable
  export var value Integer

  function equals(other Wrapper) returns bool
    return value == other.value
  end 'equals'
end 'Wrapper'

function main() returns ExitCode
  var a = Wrapper{value: 42}
  var b = Wrapper{value: 42}
  var c = Wrapper{value: 99}
  if a.equals(b) 'eq'
    if a.equals(c) 'neq'
      return 1
    end 'neq'
    return 0
  end 'eq'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: partial-field-match -->
```maxon
type Point implements Equatable
  export var x Integer
  export var y Integer

  function equals(other Point) returns bool
    return x == other.x and y == other.y
  end 'equals'
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = Point{x: 1, y: 99}
  if a.equals(b) 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: self-equality -->
```maxon
type Box implements Equatable
  export var value Integer

  function equals(other Box) returns bool
    return value == other.value
  end 'equals'
end 'Box'

function main() returns ExitCode
  var a = Box{value: 7}
  if a.equals(a) 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: equals-in-function -->
```maxon
type Id implements Equatable
  export var n Integer

  function equals(other Id) returns bool
    return n == other.n
  end 'equals'
end 'Id'

function areEqual(a Id, b Id) returns bool
  return a.equals(b)
end 'areEqual'

function main() returns ExitCode
  var x = Id{n: 5}
  var y = Id{n: 5}
  var z = Id{n: 6}
  if areEqual(x, b: y) 'eq'
    if areEqual(x, b: z) 'neq'
      return 1
    end 'neq'
    return 0
  end 'eq'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: equals-branching -->
```maxon
type Token implements Equatable
  export var id Integer

  function equals(other Token) returns bool
    return id == other.id
  end 'equals'
end 'Token'

function main() returns ExitCode
  var a = Token{id: 10}
  var b = Token{id: 10}
  var c = Token{id: 20}
  var result = 0
  if a.equals(b) 'first'
    result = result + 1
  end 'first'
  if a.equals(c) 'second'
    result = result + 10
  end 'second'
  if b.equals(c) 'third'
    result = result + 100
  end 'third'
  return result
end 'main'
```
```exitcode
1
```
