---
feature: associated-types
status: experimental
keywords: [uses, with, interface, associated, element, iterable]
category: type-system
---

# Associated Types

## Developer Notes

Associated types allow interfaces to declare abstract type placeholders that conforming structs must define with concrete types. This enables type-safe iteration where different iterators can yield different element types.

### Syntax Overview

**Interface declaration with associated types:**
```maxon
interface Container uses Element
    function get(index int) returns Element
    function set(index int, value Element) returns Self
end 'Container'
```

**Struct conformance with type binding:**
```maxon
struct IntArray is Container with int
    var data array of 100 int
    
    function Container.get(index int) returns int
        return data[index]
    end 'get'
    
    function Container.set(index int, value int) returns IntArray
        data[index] = value
        return IntArray{data: data}
    end 'set'
end 'IntArray'
```

### Key Implementation Points

- **Lexer**: `uses` and `with` keywords recognized
- **Parser**: 
  - `parseInterface()` parses `interface Name uses Type1, Type2` header, stores in `associatedTypes` vector
  - `parseStruct()` parses `is Interface with Type1, Type2`, maps positionally to interface's associated types
  - Method signatures have **no explicit `self` parameter** - it is implicit
  - **Interface methods use `function InterfaceName.methodName(params)` syntax** to explicitly declare which interface a method implements
  - Non-interface methods use simple `function methodName(params)` format
- **AST**:
  - `InterfaceDefAST` gets `std::vector<std::string> associatedTypes` from `uses` clause
  - `StructDefAST` gets `std::map<std::string, std::string> typeAssignments` from `with` clause (positional mapping)
- **Semantic Analyzer**:
  - `InterfaceInfo` stores `std::vector<std::string> associatedTypes`
  - `StructInfo` stores `std::map<std::string, std::string> typeAssignments`
  - `checkInterfaceConformance()` validates:
    - ALL interface methods are implemented (error lists all missing methods)
    - Associated types are bound via `with` clause
    - Method signatures match after type substitution
  - **Implicit `self`**: In method bodies, bare identifiers resolve to struct fields first (implicit `self.`); parameters shadow fields
  - Both return types and parameter types can use associated types
- **Codegen**:
  - Methods receive implicit `self` parameter (auto-injected)
  - Bare field access in method bodies generates field access through implicit `self` pointer
  - For-loop variable type resolved from iterator's `Element` type assignment
  - `getCurrent()` return type matched to resolved `Element` type

### Resolution Order

1. Parse interface: collect associated type names from `uses` clause (e.g., `Element`)
2. Parse struct: map `with` types positionally to interface's associated types
3. Conformance check: verify ALL required methods are implemented (error if partial)
4. Method validation: substitute associated types with concrete types in signatures
5. For-loop codegen: look up `Element` from iterator struct's type assignments

### Type Substitution in Method Signatures

During conformance checking, both `Self` and associated types are substituted:

```text
Interface signature:  function getCurrent() returns Element
Struct (string):      function getCurrent() returns character
Resolution:           Self -> string, Element -> character (implicit self)
```

### Implicit `self` and Field Access

Methods have an implicit `self` parameter. Inside method bodies, bare identifiers resolve to struct fields:

```maxon
struct Counter
    var count int

    function increment() returns Counter
        return Counter{count: count + 1}  // 'count' resolves to 'self.count'
    end 'increment'
end 'Counter'
```

Parameters shadow fields - use explicit `self.fieldName` to access shadowed fields:

```maxon
struct Box
    var value int

    function setValue(value int) returns Box
        return Box{value: value}  // parameter 'value', not field
    end 'setValue'
end 'Box'
```

## Documentation

Associated types allow interfaces to declare type placeholders that implementing structs must define. This enables generic interfaces where the concrete types vary by implementation.

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

### Implementing Associated Types in Structs

Structs bind concrete types to associated types using `with` after the interface name. Interface methods use `function InterfaceName.methodName(params)` syntax:

```maxon
struct IntArray is Container with int
    var data array of 100 int
    var len int

    function Container.get(index int) returns int
        return data[index]
    end 'get'

    function Container.set(index int, value int) returns IntArray
        data[index] = value
        return IntArray{data: data, len: len}
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

struct IntFloat is Pair with int, float
    var a int
    var b float

    function Pair.getFirst() returns int
        return a
    end 'getFirst'
    
    function Pair.getSecond() returns float
        return b
    end 'getSecond'
end 'IntFloat'
```

### The Iterable Interface

The standard library `Iterable` interface uses associated types:

```maxon
interface Iterable uses Element
    function next() returns Element or nil
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
        var cps = ch.codepoints()
        if let cp = cps.next() 'get_cp'
            printInt(cp)
        end 'get_cp'
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

1. Bind all associated types with `with Type1, Type2` (positional order matches `uses`)
2. Implement **all** methods - partial implementation is an error
3. Use exact type matches in method signatures (no implicit conversions)

```maxon
interface Summable uses Element
    function sum() returns Element
end 'Summable'

struct IntPair is Summable with int
    var a int
    var b int

    function Summable.sum() returns int
        return a + b
    end 'sum'
end 'IntPair'

function main() returns int
    var p = IntPair{a: 10, b: 32}
    return p.sum()
end 'main'
```
```output
ExitCode: 42
```

### Calling Methods

Methods are called using the method call syntax:

```maxon
var p = IntPair{a: 10, b: 32}
var result = p.sum()    // Call sum() method on instance p
```

### Error: Missing Type Binding

If a struct doesn't bind required associated types:

```maxon
interface HasElement uses Element
    function get() returns Element
end 'HasElement'

struct Broken is HasElement
    var value int

    function HasElement.get() returns int
        return value
    end 'get'
end 'Broken'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 6, column 1
Struct 'Broken' does not define required associated type 'Element' from interface 'HasElement'

  6 | struct Broken is HasElement
    | ^

Semantic Error: line 6, column 1
Method 'Broken.get' has return type 'int' but interface 'HasElement' requires 'Element'

  6 | struct Broken is HasElement
    | ^
```

### Error: Partial Implementation

If a struct doesn't implement all interface methods:

```maxon
interface TwoMethods uses Element
    function first() returns Element
    function second() returns Element
end 'TwoMethods'

struct Partial is TwoMethods with int
    var value int

    function TwoMethods.first() returns int
        return value
    end 'first'
end 'Partial'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 7, column 1
Partial interface implementation: struct 'Partial' is missing 1 method(s):
  - second() returns int

  7 | struct Partial is TwoMethods with int
    | ^
```

### Error: Type Mismatch in Method

If a method's signature doesn't match the resolved associated type:

```maxon
interface Producer uses Output
    function produce() returns Output
end 'Producer'

struct WrongReturn is Producer with float
    var value int

    function Producer.produce() returns int
        return value
    end 'produce'
end 'WrongReturn'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 6, column 1
Method 'WrongReturn.produce' has return type 'int' but interface 'Producer' requires 'float'

  6 | struct WrongReturn is Producer with float
    | ^
```


## Tests

<!-- test: basic-associated-type -->
```maxon
interface Wrapper uses Inner
    function unwrap() returns Inner
end 'Wrapper'

struct IntBox is Wrapper with int
    var value int

    function Wrapper.unwrap() returns int
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

struct IntSum is Accumulator with int
    var sum int

    function Accumulator.add(item int) returns IntSum
        return IntSum{sum: sum + item}
    end 'add'

    function Accumulator.total() returns int
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

struct IntFloat is Pair with int, float
    var a int
    var b float

    function Pair.getFirst() returns int
        return a
    end 'getFirst'

    function Pair.getSecond() returns float
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
// character is a grapheme cluster struct, use codepoints() to access codepoint values
interface CharSource uses Element
    function getChar() returns Element
end 'CharSource'

struct SingleChar is CharSource with character
    var ch character

    function CharSource.getChar() returns character
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

struct SingleByte is ByteSource with byte
    var b byte

    function ByteSource.getByte() returns byte
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

struct Missing is NeedsElement
    var value int

    function NeedsElement.get() returns int
        return value
    end 'get'
end 'Missing'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 6, column 1
Struct 'Missing' does not define required associated type 'Element' from interface 'NeedsElement'

  6 | struct Missing is NeedsElement
    | ^

Semantic Error: line 6, column 1
Method 'Missing.get' has return type 'int' but interface 'NeedsElement' requires 'Element'

  6 | struct Missing is NeedsElement
    | ^
```


<!-- test: partial-implementation-error -->
```maxon
interface TwoMethods uses Element
    function first() returns Element
    function second() returns Element
end 'TwoMethods'

struct Partial is TwoMethods with int
    var value int

    function TwoMethods.first() returns int
        return value
    end 'first'
end 'Partial'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 7, column 1
Partial interface implementation: struct 'Partial' is missing 1 method(s):
  - second() returns int

  7 | struct Partial is TwoMethods with int
    | ^
```


<!-- test: wrong-return-type-error -->
```maxon
interface Typed uses Output
    function make() returns Output
end 'Typed'

struct WrongType is Typed with float
    var value int

    function Typed.make() returns int
        return value
    end 'make'
end 'WrongType'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 6, column 1
Method 'WrongType.make' has return type 'int' but interface 'Typed' requires 'float'

  6 | struct WrongType is Typed with float
    | ^
```


<!-- test: wrong-param-type-error -->
```maxon
interface Acceptor uses Input
    function accept(val Input) returns int
end 'Acceptor'

struct WrongParam is Acceptor with float
    var value int

    function Acceptor.accept(val int) returns int
        return value + val
    end 'accept'
end 'WrongParam'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 6, column 1
Method 'WrongParam.accept' parameter 1 has type 'int' but interface 'Acceptor' requires 'float'

  6 | struct WrongParam is Acceptor with float
    | ^
```


<!-- test: implicit-self-field-access -->
```maxon
interface Countable
    function getCount() returns int
end 'Countable'

struct Counter is Countable
    var count int

    function Countable.getCount() returns int
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

struct Number is Addable
    var value int

    function Addable.addOne() returns int
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


