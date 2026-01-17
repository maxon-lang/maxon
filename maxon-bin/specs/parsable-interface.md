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

        return {cents: input.byteLength()}
    end 'fromString'
end 'Money'
```

### Using Parsable Types

Use `otherwise` to handle parsing errors:

```maxon
var price = try Money.fromString("4299") otherwise (e) 'err'
    print("Failed to parse\n")
    return  // must return or assign to price
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
        return {n: input.byteLength()}
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
        return {n: input.byteLength()}
    end 'fromString'
end 'Value'

function main() returns int
    var v = try Value.fromString("hello") otherwise 'err'
        return 0
    end 'err'
    return v.n
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
        return {n: input.byteLength()}
    end 'fromString'
end 'Value'

function main() returns int
    var v = try Value.fromString("") otherwise 'err'
        return 42
    end 'err'
    return v.n
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

        return {cents: input.byteLength()}
    end 'fromString'
end 'Money'

function main() returns int
    var price = try Money.fromString("-50") otherwise 'err'
        return 99
    end 'err'
    return price.cents
end 'main'
```
```exitcode
99
```

<!-- test: parsable.otherwise-fallthrough -->
```maxon
// otherwise blocks execute code when error occurs, then continue execution
enum ParseError is Error
    Invalid = 1
end 'ParseError'

type Value is Parsable
    export var n int

    static function Parsable.fromString(input String) returns Self throws ParseError
        if input.startsWith("x") 'check'
            throw ParseError.Invalid
        end 'check'
        return {n: input.byteLength()}
    end 'fromString'
end 'Value'

function main() returns int
    var result = 0

    // First call succeeds - handler not executed
    var v = try Value.fromString("hello") otherwise Value{n: 0}
    result = result + v.n  // adds 5

    // Second call fails - use default value
    var v2 = try Value.fromString("xbad") otherwise Value{n: 0}
    result = result + v2.n  // adds 0

    // Third call succeeds
    var v3 = try Value.fromString("world") otherwise Value{n: 0}
    result = result + v3.n  // adds 5

    return result
end 'main'
```
```exitcode
10
```

<!-- test: error.missing-throws -->
```maxon
// Implementation must throw if interface requires it
type Value is Parsable
    var n int

    static function Parsable.fromString(input String) returns Self
        return {n: input.byteLength()}
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
        return {n: input.byteLength()}
    end 'fromString'
end 'Value'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
error E015: specs/fragments/parsable-interface.error.throws-non-error-type.1.test:1:1: Method 'Value.fromString' throws 'NotAnError' which does not conform to Error
```
