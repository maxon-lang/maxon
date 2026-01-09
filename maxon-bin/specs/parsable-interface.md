---
feature: parsable-interface
status: experimental
keywords: [Parsable, interface, fromString, parsing, error, throws, static]
category: interfaces
---

# Parsable Interface

## Documentation

### Overview

The `Parsable` interface provides a standardized way for types to be constructed from string input. Types implementing `Parsable` provide a static `fromString` method that can throw parsing errors.

### Implementing Parsable

Types implement `Parsable` by providing a static `fromString` method that:
- Takes a `String` input
- Returns `Self` (the implementing type)
- Throws a specific error type on parse failure

```maxon
enum MoneyParseError is Error
    InvalidFormat = 1
    NegativeValue = 2
end 'MoneyParseError'

type Money is Parsable
    var cents int

    static function Parsable.fromString(input String) returns Self throws MoneyParseError
        if input.byteLength() == 0 'empty'
            throw MoneyParseError.InvalidFormat
        end 'empty'

        if input.startsWith("-") 'negative'
            throw MoneyParseError.NegativeValue
        end 'negative'

        return Money{cents: input.byteLength()}
    end 'fromString'
end 'Money'
```

### Using Parsable Types

Use `do-catch` blocks to handle parsing errors:

```maxon
do 'parse'
    var price = try Money.fromString("4299")
    // use price...
end 'parse' catch (e MoneyParseError) 'err'
    print("Failed to parse\n")
end 'err'
```

## Tests

<!-- test: parsable.interface-definition -->
```maxon
// Parsable interface can be defined
function main() returns int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: parsable.type-implements-parsable -->
```maxon
// Type can implement Parsable with throwing static method
enum ParseError is Error
    Invalid = 1
end 'ParseError'

type Value is Parsable
    var n int

    static function Parsable.fromString(input String) returns Self throws ParseError
        return Value{n: input.byteLength()}
    end 'fromString'
end 'Value'

function main() returns int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: parsable.successful-parse -->
```maxon
// Parsable.fromString returns struct on success
enum ParseError is Error
    Invalid = 1
end 'ParseError'

type Value is Parsable
    export var n int

    static function Parsable.fromString(input String) returns Self throws ParseError
        return Value{n: input.byteLength()}
    end 'fromString'
end 'Value'

function main() returns int
    do 'parse'
        var v = try Value.fromString("hello")
        return v.n
    end 'parse' catch (e ParseError) 'err'
        return 0
    end 'err'
end 'main'
```
```exitcode
5
```

<!-- test: parsable.throws-on-invalid-input -->
```maxon
// Parsable.fromString throws error on invalid input
enum ParseError is Error
    Empty = 1
end 'ParseError'

type Value is Parsable
    export var n int

    static function Parsable.fromString(input String) returns Self throws ParseError
        if input.byteLength() == 0 'check'
            throw ParseError.Empty
        end 'check'
        return Value{n: input.byteLength()}
    end 'fromString'
end 'Value'

function main() returns int
    do 'parse'
        var v = try Value.fromString("")
        return v.n
    end 'parse' catch (e ParseError) 'err'
        return 42
    end 'err'
end 'main'
```
```exitcode
42
```

<!-- test: parsable.multiple-error-conditions -->
```maxon
// Parsable can throw different errors for different conditions
enum MoneyParseError is Error
    InvalidFormat = 1
    NegativeValue = 2
end 'MoneyParseError'

type Money is Parsable
    export var cents int

    static function Parsable.fromString(input String) returns Self throws MoneyParseError
        if input.byteLength() == 0 'empty'
            throw MoneyParseError.InvalidFormat
        end 'empty'

        if input.startsWith("-") 'negative'
            throw MoneyParseError.NegativeValue
        end 'negative'

        return Money{cents: input.byteLength()}
    end 'fromString'
end 'Money'

function main() returns int
    do 'parse'
        var price = try Money.fromString("-50")
        return price.cents
    end 'parse' catch (e MoneyParseError) 'err'
        return 99
    end 'err'
end 'main'
```
```exitcode
99
```

<!-- test: parsable.do-catch-fallthrough -->
```maxon
// do-catch blocks fall through correctly without explicit return
enum ParseError is Error
    Invalid = 1
end 'ParseError'

type Value is Parsable
    export var n int

    static function Parsable.fromString(input String) returns Self throws ParseError
        if input.startsWith("x") 'check'
            throw ParseError.Invalid
        end 'check'
        return Value{n: input.byteLength()}
    end 'fromString'
end 'Value'

function main() returns int
    var result = 0

    do 'parse1'
        var v = try Value.fromString("hello")
        result = result + v.n
        v = v
    end 'parse1' catch (e ParseError) 'err1'
        result = result + 100
    end 'err1'

    do 'parse2'
        var v2 = try Value.fromString("xbad")
        result = result + v2.n
        v2 = v2
    end 'parse2' catch (e ParseError) 'err2'
        result = result + 10
    end 'err2'

    do 'parse3'
        var v3 = try Value.fromString("world")
        result = result + v3.n
        v3 = v3
    end 'parse3' catch (e ParseError) 'err3'
        result = result + 1000
    end 'err3'

    return result
end 'main'
```
```exitcode
20
```
```stdout
```

<!-- test: error.missing-throws -->
```maxon
// Implementation must throw if interface requires it
type Value is Parsable
    var n int

    static function Parsable.fromString(input String) returns Self
        return Value{n: input.byteLength()}
    end 'fromString'
end 'Value'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
error E015: specs/fragments/parsable-interface.error.missing-throws.1.test:1:1: Method 'Value.fromString' must throw 'Error' as required by interface 'Parsable'
```

<!-- test: error.throws-non-error-type -->
```maxon
// Implementation must throw a type that conforms to Error
enum NotAnError
    Bad = 1
end 'NotAnError'

type Value is Parsable
    var n int

    static function Parsable.fromString(input String) returns Self throws NotAnError
        return Value{n: input.byteLength()}
    end 'fromString'
end 'Value'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
error E015: specs/fragments/parsable-interface.error.throws-non-error-type.1.test:1:1: Method 'Value.fromString' throws 'NotAnError' which does not conform to Error
```
