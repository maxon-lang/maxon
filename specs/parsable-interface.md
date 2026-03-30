---
feature: parsable-interface
status: stable
keywords: [Parsable, interface, fromString, parsing, error, throws, static, int, float, bool, byte]
category: interfaces
---

# Parsable Interface

## Documentation

### Overview

The `Parsable` interface provides a standardized way for types to be constructed from string input. Types implementing `Parsable` provide a static `fromString` method that can throw parsing errors.

### Builtin Type Parsing

Builtin types (`int`, `float`, `bool`, `byte`) support `fromString` as static methods. These throw `ParseError.invalidFormat` on invalid input.

```maxon
var n = try int.fromString("42") otherwise 0
var f = try float.fromString("3.14") otherwise 0.0
var b = try bool.fromString("true") otherwise false
var y = try byte.fromString("255") otherwise 0
```

### Implementing Parsable

User-defined types implement `Parsable` by providing a static `fromString` method that:
- Takes a `String` input
- Returns `Self` (the implementing type)
- Throws a specific error type on parse failure

```maxon
typealias Amount = int(i64.min to i64.max)

enum MoneyParseError implements Error
	InvalidFormat = 1
	NegativeValue = 2
end 'MoneyParseError'

type Money implements Parsable
	var cents Amount

	static function fromString(input String) returns Self throws MoneyParseError
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
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: parsable.type-implements-parsable -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Type can implement Parsable with throwing static method
enum ParseError implements Error
	Invalid = 1
end 'ParseError'

type Value implements Parsable
	var n Integer

	static function fromString(input String) returns Self throws ParseError
		return Value{n: input.byteLength()}
	end 'fromString'
end 'Value'

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: parsable.successful-parse -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Parsable.fromString returns struct on success
enum ParseError implements Error
	Invalid = 1
end 'ParseError'

type Value implements Parsable
	export var n Integer

	static function fromString(input String) returns Self throws ParseError
		return Value{n: input.byteLength()}
	end 'fromString'
end 'Value'

function main() returns ExitCode
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

typealias Integer = int(i64.min to i64.max)

// Parsable.fromString throws error on invalid input
enum ParseError implements Error
	Empty = 1
end 'ParseError'

type Value implements Parsable
	export var n Integer

	static function fromString(input String) returns Self throws ParseError
		if input.byteLength() == 0 'check'
			throw ParseError.Empty
		end 'check'
		return Value{n: input.byteLength()}
	end 'fromString'
end 'Value'

function main() returns ExitCode
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

typealias Integer = int(i64.min to i64.max)

// Parsable can throw different errors for different conditions
enum MoneyParseError implements Error
	InvalidFormat = 1
	NegativeValue = 2
end 'MoneyParseError'

type Money implements Parsable
	export var cents Integer

	static function fromString(input String) returns Self throws MoneyParseError
		if input.byteLength() == 0 'empty'
			throw MoneyParseError.InvalidFormat
		end 'empty'

		if input.startsWith("-") 'negative'
			throw MoneyParseError.NegativeValue
		end 'negative'

		return Money{cents: input.byteLength()}
	end 'fromString'
end 'Money'

function main() returns ExitCode
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

typealias Integer = int(i64.min to i64.max)

// otherwise blocks execute code when error occurs, then continue execution
enum ParseError implements Error
	Invalid = 1
end 'ParseError'

type Value implements Parsable
	export var n Integer

	static function fromString(input String) returns Self throws ParseError
		if input.startsWith("x") 'check'
			throw ParseError.Invalid
		end 'check'
		return Value{n: input.byteLength()}
	end 'fromString'
end 'Value'

function main() returns ExitCode
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

typealias Integer = int(i64.min to i64.max)

// Implementation must throw if interface requires it
type Value implements Parsable
	var n Integer

	static function fromString(input String) returns Self
		return Value{n: input.byteLength()}
	end 'fromString'
end 'Value'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/parsable-interface/error.missing-throws.test:6:6: Method 'Value.fromString' must throw 'Error' as required by interface 'Parsable'
```

<!-- test: error.throws-non-error-type -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Implementation must throw a type that conforms to Error
enum NotAnError
	Bad = 1
end 'NotAnError'

type Value implements Parsable
	var n Integer

	static function fromString(input String) returns Self throws NotAnError
		return Value{n: input.byteLength()}
	end 'fromString'
end 'Value'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: specs/fragments/parsable-interface/error.throws-non-error-type.test:10:6: Method 'Value.fromString' throws 'NotAnError' which does not conform to Error
```

<!-- test: parsable.int-fromstring -->
```maxon
function main() returns ExitCode
	var n = try int.fromString("42") otherwise 0
	return n
end 'main'
```
```exitcode
42
```

<!-- test: parsable.int-fromstring-negative -->
```maxon
function main() returns ExitCode
	var n = try int.fromString("-7") otherwise 0
	return n + 10
end 'main'
```
```exitcode
3
```

<!-- test: parsable.int-fromstring-invalid -->
```maxon
function main() returns ExitCode
	var n = try int.fromString("abc") otherwise 99
	return n
end 'main'
```
```exitcode
99
```

<!-- test: parsable.float-fromstring -->
```maxon
function main() returns ExitCode
	var f = try float.fromString("3.14") otherwise 0.0
	var check = f * 100.0
	return trunc(check)
end 'main'
```
```exitcode
314
```

<!-- test: parsable.float-fromstring-negative -->
```maxon
function main() returns ExitCode
	var f = try float.fromString("-2.5") otherwise 0.0
	return trunc(f) + 10
end 'main'
```
```exitcode
8
```

<!-- test: parsable.bool-fromstring-true -->
```maxon
function main() returns ExitCode
	var b = try bool.fromString("true") otherwise false
	if b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: parsable.bool-fromstring-false -->
```maxon
function main() returns ExitCode
	var b = try bool.fromString("false") otherwise true
	if b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
