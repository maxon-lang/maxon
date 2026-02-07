---
feature: associated-types
status: experimental
keywords: [uses, with, interface, associated, element, iterable]
category: type-system
---

# Associated Types

## Documentation

Associated types allow interfaces to declare type placeholders that implementing types must define. This enables generic interfaces where the concrete types vary by implementation.

### Declaring Associated Types in Interfaces

Use the `uses` keyword after the interface name to declare associated types:

```maxon
interface Container uses Element
  function get(index int) returns Element
  function set(index int, value Element) returns Self
end 'Container'
```

Associated types can be used in:
- Return types (`Element`)
- Parameter types (`value Element`)
- Combined with `Self` in the same signature

### Implementing Associated Types

Types bind concrete types to associated types using `with` after the interface name. Interface methods use `function methodName(params)` syntax:

```maxon
type IntArray is Container with int
  var data array of 100 int
  var len int

  function get(index int) returns int
    return data[index]
  end 'get'

  function set(index int, value int) returns IntArray
    data[index] = value
    return {data: data, len: len}
  end 'set'
end 'IntArray'
```

The `with` types map positionally to the interface's `uses` types. Method signatures use the concrete type (`int`) that was bound.

### Multiple Associated Types

For interfaces with multiple associated types, list them in order:

```maxon
interface Pair uses First, Second
  function getFirst() returns First
  function getSecond() returns Second
end 'Pair'

type IntFloat is Pair with int, float
  var a int
  var b float

  function getFirst() returns int
    return a
  end 'getFirst'
  
  function getSecond() returns float
    return b
  end 'getSecond'
end 'IntFloat'
```

### The Iterable Interface

The standard library `Iterable` interface uses associated types:

```maxon
interface Iterable uses Element
  function next() returns Element throws IterationError
end 'Iterable'
```

Different iterators define different element types:

- `Iterator` (for `range()`): `is Iterable with int`
- `string`: `is Iterable with character` (grapheme cluster)
- `ByteView`: `is Iterable with byte` (byte value)
- `UTF16View`: `is Iterable with int` (UTF-16 code unit)
- `CodepointView`: `is Iterable with int` (Unicode codepoint)

### For-Loop Type Inference

When iterating with `for`, the loop variable's type is inferred from the iterator's `Element` type:

```maxon
function main() returns int
  var s = "Hi"
  for ch in s 'chars'
    // ch has type 'character' (inferred from string's Element type - grapheme cluster)
    print("{ch}\n")
  end 'chars'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
H
i
```

### Conformance Requirements

A type conforming to an interface with associated types must:

1. Bind all associated types with `with Type1, Type2` (positional order matches `uses`)
2. Implement **all** methods - partial implementation is an error
3. Use exact type matches in method signatures (no implicit conversions)

```maxon
interface Summable uses Element
  function sum() returns Element
end 'Summable'

type IntPair is Summable with int
  var a int
  var b int

  function sum() returns int
    return a + b
  end 'sum'
end 'IntPair'

function main() returns int
  var p = IntPair{a: 10, b: 32}
  return p.sum()
end 'main'
```
```exitcode
42
```

### Calling Methods

Methods are called using the method call syntax:

```maxon
var p = IntPair{a: 10, b: 32}
var result = p.sum()    // Call sum() method on instance p
```

### Error: Missing Type Binding

If a type doesn't bind required associated types:

```maxon
interface HasElement uses Element
  function get() returns Element
end 'HasElement'

type Broken is HasElement
  var value int

  function get() returns int
    return value
  end 'get'
end 'Broken'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/docs-example-3.test:6:6: Type 'Broken' does not define required associated type 'Element' from interface 'HasElement'
```

### Error: Partial Implementation

If a type doesn't implement all interface methods:

```maxon
interface TwoMethods uses Element
  function first() returns Element
  function second() returns Element
end 'TwoMethods'

type Partial is TwoMethods with int
  var value int

  function first() returns int
    return value
  end 'first'
end 'Partial'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/docs-example-4.test:7:6: Partial interface implementation: type 'Partial' is missing 1 method(s):
  - second() returns int
```

### Error: Type Mismatch in Method

If a method's signature doesn't match the resolved associated type:

```maxon
interface Producer uses Output
  function produce() returns Output
end 'Producer'

type WrongReturn is Producer with float
  var value int

  function produce() returns int
    return value
  end 'produce'
end 'WrongReturn'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/docs-example-5.test:6:6: Partial interface implementation: type 'WrongReturn' has 1 method(s) with wrong signature:
  - produce() returns int (expected produce() returns float)
```


## Tests

<!-- test: basic-associated-type -->
```maxon
interface Wrapper uses Inner
  function unwrap() returns Inner
end 'Wrapper'

type IntBox is Wrapper with int
  var value int

  function unwrap() returns int
    return value
  end 'unwrap'
end 'IntBox'

function main() returns int
  var box = IntBox{value: 42}
  return box.unwrap()
end 'main'
```
```exitcode
42
```


<!-- test: associated-type-in-param -->
```maxon
interface Accumulator uses Item
  function add(item Item) returns Self
  function total() returns int
end 'Accumulator'

type IntSum is Accumulator with int
  var sum int

  function add(item int) returns IntSum
    return {sum: sum + item}
  end 'add'

  function total() returns int
    return sum
  end 'total'
end 'IntSum'

function main() returns int
  var acc = IntSum{sum: 0}
  acc = acc.add(10)
  acc = acc.add(32)
  return acc.total()
end 'main'
```
```exitcode
42
```


<!-- test: multiple-associated-types -->
```maxon
interface Pair uses First, Second
  function getFirst() returns First
  function getSecond() returns Second
end 'Pair'

type IntFloat is Pair with int, float
  var a int
  var b float

  function getFirst() returns int
    return a
  end 'getFirst'

  function getSecond() returns float
    return b
  end 'getSecond'
end 'IntFloat'

function main() returns int
  var p = IntFloat{a: 40, b: 2.5}
  var x = p.getFirst()
  var y = trunc(p.getSecond())
  return x + y
end 'main'
```
```exitcode
42
```


<!-- test: character-element-type -->
```maxon
// character is a grapheme cluster type, use codepoints() to access codepoint values
interface CharSource uses Element
  function getChar() returns Element
end 'CharSource'

type SingleChar is CharSource with Character
  var ch Character

  function getChar() returns Character
    return ch
  end 'getChar'
end 'SingleChar'

function main() returns int
  var s = SingleChar{ch: 'A'}
  var c = s.getChar()
  for cp in c.codepoints() 'loop'
    return cp
  end 'loop'
  return 0
end 'main'
```
```exitcode
65
```


<!-- test: byte-element-type -->
```maxon
interface ByteSource uses Element
  function getByte() returns Element
end 'ByteSource'

type SingleByte is ByteSource with byte
  var b byte

  function getByte() returns byte
    return b
  end 'getByte'
end 'SingleByte'

function main() returns int
  var s = SingleByte{b: 42 as byte}
  var b = s.getByte()
  return b as int
end 'main'
```
```exitcode
42
```


<!-- test: missing-type-binding-error -->
```maxon
interface NeedsElement uses Element
  function get() returns Element
end 'NeedsElement'

type Missing is NeedsElement
  var value int

  function get() returns int
    return value
  end 'get'
end 'Missing'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/missing-type-binding-error.test:6:6: Type 'Missing' does not define required associated type 'Element' from interface 'NeedsElement'
```


<!-- test: partial-implementation-error -->
```maxon
interface TwoMethods uses Element
  function first() returns Element
  function second() returns Element
end 'TwoMethods'

type Partial is TwoMethods with int
  var value int

  function first() returns int
    return value
  end 'first'
end 'Partial'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/partial-implementation-error.test:7:6: Partial interface implementation: type 'Partial' is missing 1 method(s):
  - second() returns int
```


<!-- test: wrong-return-type-error -->
```maxon
interface Typed uses Output
  function make() returns Output
end 'Typed'

type WrongType is Typed with float
  var value int

  function make() returns int
    return value
  end 'make'
end 'WrongType'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/wrong-return-type-error.test:6:6: Partial interface implementation: type 'WrongType' has 1 method(s) with wrong signature:
  - make() returns int (expected make() returns float)
```


<!-- test: wrong-param-type-error -->
```maxon
interface Acceptor uses Input
  function accept(val Input) returns int
end 'Acceptor'

type WrongParam is Acceptor with float
  var value int

  function accept(val int) returns int
    return value + val
  end 'accept'
end 'WrongParam'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/wrong-param-type-error.test:6:6: Partial interface implementation: type 'WrongParam' has 1 method(s) with wrong signature:
  - accept(val int) returns int (expected accept(val float) returns int)
```


<!-- test: implicit-self-field-access -->
```maxon
interface Countable
  function getCount() returns int
end 'Countable'

type Counter is Countable
  var count int

  function getCount() returns int
    return count
  end 'getCount'
end 'Counter'

function main() returns int
  var c = Counter{count: 42}
  return c.getCount()
end 'main'
```
```exitcode
42
```


<!-- test: method-call-syntax -->
```maxon
interface Addable
  function addOne() returns int
end 'Addable'

type Number is Addable
  var value int

  function addOne() returns int
    return value + 1
  end 'addOne'
end 'Number'

function main() returns int
  var n = Number{value: 41}
  return n.addOne()
end 'main'
```
```exitcode
42
```


