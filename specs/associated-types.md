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
  function get(index Index) returns Element
  function set(index Index, value Element) returns Self
end 'Container'
```

Associated types can be used in:
- Return types (`Element`)
- Parameter types (`value Element`)
- Combined with `Self` in the same signature

### Implementing Associated Types

Types bind concrete types to associated types using `with` after the interface name. Interface methods use `function methodName(params)` syntax:

```maxon
typealias Score = int(i64.min to i64.max)

type ScoreArray implements Container with Score
  var data array of 100 Score
  var len Score

  function get(index Index) returns Score
    return data[index]
  end 'get'

  function set(index Index, value Score) returns ScoreArray
    data[index] = value
    return ScoreArray{data: data, len: len}
  end 'set'
end 'ScoreArray'
```

The `with` types map positionally to the interface's `uses` types. Method signatures use the concrete type (`int`) that was bound.

### Multiple Associated Types

For interfaces with multiple associated types, list them in order:

```maxon
typealias ID = int(i64.min to i64.max)
typealias Weight = float(f64.min to f64.max)

interface Pair uses First, Second
  function getFirst() returns First
  function getSecond() returns Second
end 'Pair'

type PersonRecord implements Pair with ID, Weight
  var a ID
  var b Weight

  function getFirst() returns ID
    return a
  end 'getFirst'

  function getSecond() returns Weight
    return b
  end 'getSecond'
end 'PersonRecord'
```

### The Iterable Interface

The standard library `Iterable` interface uses associated types:

```maxon
interface Iterable uses Element
  function next() returns Element throws IterationError
end 'Iterable'
```

Different iterators define different element types:

- `Iterator` (for `range()`): `implements Iterable with int`
- `string`: `implements Iterable with character` (grapheme cluster)
- `ByteView`: `implements Iterable with byte` (byte value)
- `UTF16View`: `implements Iterable with int` (UTF-16 code unit)
- `CodepointView`: `implements Iterable with int` (Unicode codepoint)

### For-Loop Type Inference

When iterating with `for`, the loop variable's type is inferred from the iterator's `Element` type:

```maxon
function main() returns ExitCode
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
typealias Score = int(i64.min to i64.max)

interface Summable uses Element
  function sum() returns Element
end 'Summable'

type ScorePair implements Summable with Score
  var a Score
  var b Score

  function sum() returns Score
    return a + b
  end 'sum'
end 'ScorePair'

function main() returns ExitCode
  var p = ScorePair{a: 10, b: 32}
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
typealias Score = int(i64.min to i64.max)

interface HasElement uses Element
  function get() returns Element
end 'HasElement'

type Broken implements HasElement
  var value Score

  function get() returns Score
    return value
  end 'get'
end 'Broken'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/docs-example-3.test:8:6: Type 'Broken' does not define required associated type 'Element' from interface 'HasElement'
```

### Error: Partial Implementation

If a type doesn't implement all interface methods:

```maxon
typealias Score = int(i64.min to i64.max)

interface TwoMethods uses Element
  function first() returns Element
  function second() returns Element
end 'TwoMethods'

type Partial implements TwoMethods with Score
  var value Score

  function first() returns Score
    return value
  end 'first'
end 'Partial'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/docs-example-4.test:9:6: Partial interface implementation: type 'Partial' is missing 1 method(s):
  - second() returns Score
```

### Error: Type Mismatch in Method

If a method's signature doesn't match the resolved associated type:

```maxon
typealias ID = int(i64.min to i64.max)
typealias Weight = float(f64.min to f64.max)

interface Producer uses Output
  function produce() returns Output
end 'Producer'

type WrongReturn implements Producer with Weight
  var value ID

  function produce() returns ID
    return value
  end 'produce'
end 'WrongReturn'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/docs-example-5.test:9:6: Partial interface implementation: type 'WrongReturn' has 1 method(s) with wrong signature:
  - produce() returns ID (expected produce() returns Weight)
```


## Tests

<!-- test: basic-associated-type -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Wrapper uses Inner
  function unwrap() returns Inner
end 'Wrapper'

type IntBox implements Wrapper with Integer
  var value Integer

  function unwrap() returns Integer
    return value
  end 'unwrap'
end 'IntBox'

function main() returns ExitCode
  var box = IntBox{value: 42}
  return box.unwrap()
end 'main'
```
```exitcode
42
```


<!-- test: associated-type-in-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Accumulator uses Item
  function add(item Item) returns Self
  function total() returns Integer
end 'Accumulator'

type IntSum implements Accumulator with Integer
  var sum Integer

  function add(item Integer) returns IntSum
    return IntSum{sum: sum + item}
  end 'add'

  function total() returns Integer
    return sum
  end 'total'
end 'IntSum'

function main() returns ExitCode
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

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Pair uses First, Second
  function getFirst() returns First
  function getSecond() returns Second
end 'Pair'

type IntFloat implements Pair with Integer, Float
  var a Integer
  var b Float

  function getFirst() returns Integer
    return a
  end 'getFirst'

  function getSecond() returns Float
    return b
  end 'getSecond'
end 'IntFloat'

function main() returns ExitCode
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

type SingleChar implements CharSource with Character
  var ch Character

  function getChar() returns Character
    return ch
  end 'getChar'
end 'SingleChar'

function main() returns ExitCode
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

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

interface ByteSource uses Element
  function getByte() returns Element
end 'ByteSource'

type SingleByte implements ByteSource with Byte
  var b Byte

  function getByte() returns Byte
    return b
  end 'getByte'
end 'SingleByte'

function main() returns ExitCode
  var s = SingleByte{b: 42 as Byte}
  var b = s.getByte()
  return b as Integer
end 'main'
```
```exitcode
42
```


<!-- test: missing-type-binding-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface NeedsElement uses Element
  function get() returns Element
end 'NeedsElement'

type Missing implements NeedsElement
  var value Integer

  function get() returns Integer
    return value
  end 'get'
end 'Missing'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/missing-type-binding-error.test:9:6: Type 'Missing' does not define required associated type 'Element' from interface 'NeedsElement'
```


<!-- test: partial-implementation-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface TwoMethods uses Element
  function first() returns Element
  function second() returns Element
end 'TwoMethods'

type Partial implements TwoMethods with Integer
  var value Integer

  function first() returns Integer
    return value
  end 'first'
end 'Partial'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/partial-implementation-error.test:10:6: Partial interface implementation: type 'Partial' is missing 1 method(s):
  - second() returns Integer
```


<!-- test: wrong-return-type-error -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Typed uses Output
  function make() returns Output
end 'Typed'

type WrongType implements Typed with Float
  var value Integer

  function make() returns Integer
    return value
  end 'make'
end 'WrongType'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/wrong-return-type-error.test:10:6: Partial interface implementation: type 'WrongType' has 1 method(s) with wrong signature:
  - make() returns Integer (expected make() returns Float)
```


<!-- test: wrong-param-type-error -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

interface Acceptor uses Input
  function accept(val Input) returns Integer
end 'Acceptor'

type WrongParam implements Acceptor with Float
  var value Integer

  function accept(val Integer) returns Integer
    return value + val
  end 'accept'
end 'WrongParam'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/associated-types/wrong-param-type-error.test:10:6: Partial interface implementation: type 'WrongParam' has 1 method(s) with wrong signature:
  - accept(val Integer) returns Integer (expected accept(val Float) returns Integer)
```


<!-- test: implicit-self-field-access -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Countable
  function getCount() returns Integer
end 'Countable'

type Counter implements Countable
  var count Integer

  function getCount() returns Integer
    return count
  end 'getCount'
end 'Counter'

function main() returns ExitCode
  var c = Counter{count: 42}
  return c.getCount()
end 'main'
```
```exitcode
42
```


<!-- test: method-call-syntax -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Addable
  function addOne() returns Integer
end 'Addable'

type Number implements Addable
  var value Integer

  function addOne() returns Integer
    return value + 1
  end 'addOne'
end 'Number'

function main() returns ExitCode
  var n = Number{value: 41}
  return n.addOne()
end 'main'
```
```exitcode
42
```


