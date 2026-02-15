---
feature: interface-extensions
status: stable
keywords: [extension, interface, method]
category: type-system
---

# Interface Extensions

## Documentation

Extensions allow you to add methods to interfaces that are automatically available on all types conforming to that interface. Unlike regular interface methods that must be implemented by each conforming type, extension methods have a single implementation that works for all conformers.

### Extension Declaration

Extensions are declared with the `extension` keyword followed by an interface name:

```maxon
extension Iterable
  function count() returns Integer
    var n = 0
    for _ in self 'loop'
      n = n + 1
    end 'loop'
    return n
  end 'count'
end 'Iterable'
```

### How Extensions Work

When you define an extension method:
1. The method becomes available on all types that conform to the interface
2. The `self` keyword refers to the concrete type instance
3. Extension methods can call any method required by the interface
4. Associated types from the interface are resolved to the concrete type's bindings

### Using Associated Types

Extensions can use the interface's associated types. These are automatically substituted with the concrete type's associated type bindings:

```maxon
interface Container uses Element
  function get(index Integer) returns Element
end 'Container'

extension Container
  function first() returns Element
    return self.get(0)
  end 'first'
end 'Container'
```

When called on a type like `IntArray implements Container with int`, the return type `Element` becomes `int`.

### Extension Method Synthesis

When a type conforms to an interface that has extensions, the compiler synthesizes concrete methods for that type. For example, if `type IntArray implements Array with int` conforms to `Iterable`, calling `myArray.count()` invokes a method specialized for `IntArray`.

### Transitive Extensions

Extensions from interfaces are also applied transitively. If interface `B` extends interface `A`, extensions on `A` are available on types conforming to `B`.

### Example: Map on Iterable

```maxon
extension Iterable
  typealias ElementArray = Array with Element

  function map(transform (Element) returns Element) returns ElementArray
    var result = ElementArray{}
    for item in self 'loop'
      result.push(transform(item))
    end 'loop'
    return result
  end 'map'
end 'Iterable'
```

This `map` extension works on any `Iterable` type (Array, Set, Map, etc.) and returns a new array with transformed elements.

## Tests

<!-- test: basic-extension-on-array -->
```maxon
interface Countable
  function value() returns Integer
end 'Countable'

extension Countable
  function count() returns Integer
    return 42
  end 'count'
end 'Countable'

type IntList implements Countable
  var data Integer

  function value() returns Integer
    return data
  end 'value'
end 'IntList'

function main() returns ExitCode
  var list = IntList{data: 5}
  return list.count()
end 'main'
```
```exitcode
42
```


<!-- test: extension-with-self -->
```maxon
interface Summable
  function value() returns Integer
end 'Summable'

extension Summable
  function doubled() returns Integer
    return self.value() * 2
  end 'doubled'
end 'Summable'

type Number implements Summable
  var n Integer

  function value() returns Integer
    return n
  end 'value'
end 'Number'

function main() returns ExitCode
  var num = Number{n: 21}
  return num.doubled()
end 'main'
```
```exitcode
42
```


<!-- test: extension-multiple-types -->
```maxon
interface Valued
  function val() returns Integer
end 'Valued'

extension Valued
  function valPlusTen() returns Integer
    return self.val() + 10
  end 'valPlusTen'
end 'Valued'

type TypeA implements Valued
  var a Integer
  function val() returns Integer
    return a
  end 'val'
end 'TypeA'

type TypeB implements Valued
  var b Integer
  function val() returns Integer
    return b * 2
  end 'val'
end 'TypeB'

function main() returns ExitCode
  var ta = TypeA{a: 5}
  var tb = TypeB{b: 10}
  return ta.valPlusTen() + tb.valPlusTen()
end 'main'
```
```exitcode
45
```


<!-- test: extension-with-params -->
```maxon
interface Scalable
  function base() returns Integer
end 'Scalable'

extension Scalable
  function scale(factor Integer) returns Integer
    return self.base() * factor
  end 'scale'
end 'Scalable'

type Amount implements Scalable
  var amount Integer

  function base() returns Integer
    return amount
  end 'base'
end 'Amount'

function main() returns ExitCode
  var a = Amount{amount: 7}
  return a.scale(6)
end 'main'
```
```exitcode
42
```


<!-- test: extension-returns-struct -->
```maxon
interface Pointlike
  function getX() returns Integer
  function getY() returns Integer
end 'Pointlike'

type SimplePoint
  export var x Integer
  export var y Integer
end 'SimplePoint'

extension Pointlike
  function asSimple() returns SimplePoint
    return {x: self.getX(), y: self.getY()}
  end 'asSimple'
end 'Pointlike'

type Coord implements Pointlike
  var cx Integer
  var cy Integer

  function getX() returns Integer
    return cx
  end 'getX'

  function getY() returns Integer
    return cy
  end 'getY'
end 'Coord'

function main() returns ExitCode
  var c = Coord{cx: 10, cy: 32}
  var p = c.asSimple()
  return p.x + p.y
end 'main'
```
```exitcode
42
```


<!-- test: stdlib-map-on-array -->
```maxon
function main() returns ExitCode
  var nums = [1, 2, 3, 4, 5]
  var doubled = nums.map((x Integer) gives x * 2)
  
  var sum = 0
  for n in doubled 'loop'
    sum = sum + n
  end 'loop'
  
  // 2 + 4 + 6 + 8 + 10 = 30
  return sum
end 'main'
```
```exitcode
30
```


<!-- test: stdlib-map-on-set -->
```maxon
typealias IntSet = Set with int

function main() returns ExitCode
  var s = IntSet{}
  s.insert(10)
  s.insert(20)
  s.insert(30)
  var mapped = s.map((x Integer) gives x + 1)

  var sum = 0
  for n in mapped 'loop'
    sum = sum + n
  end 'loop'

  // 11 + 21 + 31 = 63 (order may vary but sum is same)
  return sum
end 'main'
```
```exitcode
63
```

<!-- test: stdlib-map-on-map -->
```maxon
function main() returns ExitCode
  var m = ["a": 1, "b": 2, "c": 3]

  var sum = 0
  for pair in m 'loop'
    sum = sum + pair.1 * 10
  end 'loop'

  // 10 + 20 + 30 = 60
  return sum
end 'main'
```
```exitcode
60
```

<!-- test: stdlib-map-on-map-with-function -->
```maxon
function main() returns ExitCode
  var m = ["a": 1, "b": 2, "c": 3]
  var mapped = m.map((p) gives p)

  var sum = 0
  for pair in mapped 'loop'
    sum = sum + pair.1
  end 'loop'

  // 1 + 2 + 3 = 6
  return sum
end 'main'
```
```exitcode
6
```
