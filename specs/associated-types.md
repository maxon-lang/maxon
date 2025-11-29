---
feature: associated-types
status: experimental
keywords: [type, interface, associated, element, iterable]
category: type-system
---

# Associated Types

## Developer Notes

Associated types allow interfaces to declare abstract type placeholders that conforming structs must define with concrete types. This enables type-safe iteration where different iterators can yield different element types.

### Key Implementation Points

- **Lexer**: `type` keyword recognized inside interface and struct bodies
- **Parser**: 
  - `parseInterface()` parses `type Name` declarations, stores in `associatedTypes` vector
  - `parseStruct()` parses `type Name = ConcreteType` assignments, stores in `typeAssignments` map
- **AST**:
  - `InterfaceDefAST` gets `std::vector<std::string> associatedTypes`
  - `StructDefAST` gets `std::map<std::string, std::string> typeAssignments`
- **Semantic Analyzer**:
  - `InterfaceInfo` stores `std::vector<std::string> associatedTypes`
  - `StructInfo` stores `std::map<std::string, std::string> typeAssignments`
  - `checkInterfaceConformance()` validates all associated types are defined
  - Method signatures: associated type names (e.g., `Element`) resolved to concrete types
  - Both return types and parameter types can use associated types
- **Codegen**:
  - For-loop variable type resolved from iterator's `Element` type assignment
  - `getCurrent()` return type matched to resolved `Element` type

### Resolution Order

1. Parse interface: collect associated type names (e.g., `Element`)
2. Parse struct: collect type assignments (e.g., `Element = char`)
3. Conformance check: verify all required associated types are assigned
4. Method validation: substitute associated types with concrete types in signatures
5. For-loop codegen: look up `Element` from iterator struct's type assignments

### Type Substitution in Method Signatures

During conformance checking, both `Self` and associated types are substituted:

```text
Interface signature:  function getCurrent(self Self) Element
Struct (string):      function getCurrent(self string) char
Resolution:           Self -> string, Element -> char
```

## Documentation

Associated types allow interfaces to declare type placeholders that implementing structs must define. This enables generic interfaces where the concrete types vary by implementation.

### Declaring Associated Types in Interfaces

Use the `type` keyword inside an interface to declare an associated type:

```maxon
interface Container
    type Element
    function get(self Self, index int) Element
    function set(self Self, index int, value Element) Self
end 'Container'
```

Associated types can be used in:
- Return types (`Element`)
- Parameter types (`value Element`)
- Combined with `Self` in the same signature

### Implementing Associated Types in Structs

Structs assign concrete types to associated types using `type Name = ConcreteType`:

```maxon
struct IntArray is Container
    data [100]int
    len int

    type Element = int

    function get(self IntArray, index int) int
        return self.data[index]
    end 'get'

    function set(self IntArray, index int, value int) IntArray
        self.data[index] = value
        return self
    end 'set'
end 'IntArray'
```

The method signatures must use the concrete type (`int`) that matches the type assignment.

### The Iterable Interface

The standard library `Iterable` interface uses associated types:

```maxon
interface Iterable
    type Element
    function hasNext(self Self) int
    function getCurrent(self Self) Element
    function next(self Self) Self
end 'Iterable'
```

Different iterators define different element types:

- `Iterator` (for `range()`): `type Element = int`
- `string`: `type Element = char`
- `ByteView`: `type Element = byte`
- `UTF16View`: `type Element = int`

### For-Loop Type Inference

When iterating with `for`, the loop variable's type is inferred from the iterator's `Element` type:

```maxon
function main() int
    var s = "Hi"
    for ch in s 'chars'
        // ch has type 'char' (inferred from string's Element type)
        print(ch as int)
    end 'chars'
    return 0
end 'main'
```
```output
ExitCode: 0
Stdout: 72
105
```

### Conformance Requirements

A struct conforming to an interface with associated types must:

1. Define all associated types with `type Name = ConcreteType`
2. Implement all methods with signatures matching the resolved types
3. Use exact type matches (no implicit conversions)

```maxon
interface Summable
    type Element
    function sum(self Self) Element
end 'Summable'

struct IntPair is Summable
    a int
    b int

    type Element = int

    function sum(self IntPair) int
        return self.a + self.b
    end 'sum'
end 'IntPair'

function main() int
    var p = IntPair{a: 10, b: 32}
    return IntPair.sum(p)
end 'main'
```
```output
ExitCode: 42
```

### Error: Missing Type Assignment

If a struct doesn't define a required associated type:

```maxon
interface HasElement
    type Element
    function get(self Self) Element
end 'HasElement'

struct Broken is HasElement
    value int

    function get(self Broken) int
        return self.value
    end 'get'
end 'Broken'

function main() int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 7, column 1
Struct 'Broken' does not define required associated type 'Element' from interface 'HasElement'

  7 | struct Broken is HasElement
    | ^

Semantic Error: line 7, column 1
Method 'Broken.get' has return type 'int' but interface 'HasElement' requires 'Element'

  7 | struct Broken is HasElement
    | ^
```

### Error: Type Mismatch in Method

If a method's signature doesn't match the resolved associated type:

```maxon
interface Producer
    type Output
    function produce(self Self) Output
end 'Producer'

struct WrongReturn is Producer
    type Output = float
    value int

    function produce(self WrongReturn) int
        return self.value
    end 'produce'
end 'WrongReturn'

function main() int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 7, column 1
Method 'WrongReturn.produce' has return type 'int' but interface 'Producer' requires 'float'

  7 | struct WrongReturn is Producer
    | ^
```


## Tests

<!-- test: basic-associated-type -->
```maxon
interface Wrapper
    type Inner
    function unwrap(self Self) Inner
end 'Wrapper'

struct IntBox is Wrapper
    value int

    type Inner = int

    function unwrap(self IntBox) int
        return self.value
    end 'unwrap'
end 'IntBox'

function main() int
    var box = IntBox{value: 42}
    return IntBox.unwrap(box)
end 'main'
```
```exitcode
42
```


<!-- test: associated-type-in-param -->
```maxon
interface Accumulator
    type Item
    function add(self Self, item Item) Self
    function total(self Self) int
end 'Accumulator'

struct IntSum is Accumulator
    sum int

    type Item = int

    function add(self IntSum, item int) IntSum
        return IntSum{sum: self.sum + item}
    end 'add'

    function total(self IntSum) int
        return self.sum
    end 'total'
end 'IntSum'

function main() int
    var acc = IntSum{sum: 0}
    acc = IntSum.add(acc, 10)
    acc = IntSum.add(acc, 32)
    return IntSum.total(acc)
end 'main'
```
```exitcode
42
```


<!-- test: multiple-associated-types -->
```maxon
interface Pair
    type First
    type Second
    function getFirst(self Self) First
    function getSecond(self Self) Second
end 'Pair'

struct IntFloat is Pair
    a int
    b float

    type First = int
    type Second = float

    function getFirst(self IntFloat) int
        return self.a
    end 'getFirst'

    function getSecond(self IntFloat) float
        return self.b
    end 'getSecond'
end 'IntFloat'

function main() int
    var p = IntFloat{a: 40, b: 2.5}
    var x = IntFloat.getFirst(p)
    var y = trunc(IntFloat.getSecond(p))
    return x + y
end 'main'
```
```exitcode
42
```


<!-- test: char-element-type -->
```maxon
interface CharSource
    type Element
    function getChar(self Self) Element
end 'CharSource'

struct SingleChar is CharSource
    ch char

    type Element = char

    function getChar(self SingleChar) char
        return self.ch
    end 'getChar'
end 'SingleChar'

function main() int
    var s = SingleChar{ch: 'A'}
    var c = SingleChar.getChar(s)
    return c as int
end 'main'
```
```exitcode
65
```


<!-- test: byte-element-type -->
```maxon
interface ByteSource
    type Element
    function getByte(self Self) Element
end 'ByteSource'

struct SingleByte is ByteSource
    b byte

    type Element = byte

    function getByte(self SingleByte) byte
        return self.b
    end 'getByte'
end 'SingleByte'

function main() int
    var s = SingleByte{b: 42 as byte}
    var b = SingleByte.getByte(s)
    return b as int
end 'main'
```
```exitcode
42
```


<!-- test: missing-type-assignment-error -->
```maxon
interface NeedsElement
    type Element
    function get(self Self) Element
end 'NeedsElement'

struct Missing is NeedsElement
    value int

    function get(self Missing) int
        return self.value
    end 'get'
end 'Missing'

function main() int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 7, column 1
Struct 'Missing' does not define required associated type 'Element' from interface 'NeedsElement'

  7 | struct Missing is NeedsElement
    | ^

Semantic Error: line 7, column 1
Method 'Missing.get' has return type 'int' but interface 'NeedsElement' requires 'Element'

  7 | struct Missing is NeedsElement
    | ^
```


<!-- test: wrong-return-type-error -->
```maxon
interface Typed
    type Output
    function make(self Self) Output
end 'Typed'

struct WrongType is Typed
    type Output = float
    value int

    function make(self WrongType) int
        return self.value
    end 'make'
end 'WrongType'

function main() int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 7, column 1
Method 'WrongType.make' has return type 'int' but interface 'Typed' requires 'float'

  7 | struct WrongType is Typed
    | ^
```


<!-- test: wrong-param-type-error -->
```maxon
interface Acceptor
    type Input
    function accept(self Self, val Input) int
end 'Acceptor'

struct WrongParam is Acceptor
    type Input = float
    value int

    function accept(self WrongParam, val int) int
        return self.value + val
    end 'accept'
end 'WrongParam'

function main() int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 7, column 1
Method 'WrongParam.accept' parameter 2 has type 'int' but interface 'Acceptor' requires 'float'

  7 | struct WrongParam is Acceptor
    | ^
```

