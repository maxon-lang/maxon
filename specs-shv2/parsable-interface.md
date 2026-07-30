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
	var cents as Amount

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

<!-- disabled-test: parsable.type-implements-parsable -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Type can implement Parsable with throwing static method
enum ParseError implements Error
	Invalid = 1
end 'ParseError'

type Value implements Parsable
	var n as Integer

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

<!-- disabled-test: parsable.successful-parse -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Parsable.fromString returns struct on success
enum ParseError implements Error
	Invalid = 1
end 'ParseError'

type Value implements Parsable
	export var n as Integer

	static function fromString(input String) returns Self throws ParseError
		return Value{n: input.byteLength()}
	end 'fromString'
end 'Value'

function main() returns ExitCode
	let v = try Value.fromString("hello") otherwise 'err'
		return 0
	end 'err'
	return v.n
end 'main'
```
```exitcode
5
```

<!-- disabled-test: parsable.throws-on-invalid-input -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Parsable.fromString throws error on invalid input
enum ParseError implements Error
	Empty = 1
end 'ParseError'

type Value implements Parsable
	export var n as Integer

	static function fromString(input String) returns Self throws ParseError
		if input.byteLength() == 0 'check'
			throw ParseError.Empty
		end 'check'
		return Value{n: input.byteLength()}
	end 'fromString'
end 'Value'

function main() returns ExitCode
	let v = try Value.fromString("") otherwise 'err'
		return 42
	end 'err'
	return v.n
end 'main'
```
```exitcode
42
```

<!-- disabled-test: parsable.multiple-error-conditions -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Parsable can throw different errors for different conditions
enum MoneyParseError implements Error
	InvalidFormat = 1
	NegativeValue = 2
end 'MoneyParseError'

type Money implements Parsable
	export var cents as Integer

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
	let price = try Money.fromString("-50") otherwise 'err'
		return 99
	end 'err'
	return price.cents
end 'main'
```
```exitcode
99
```

<!-- disabled-test: parsable.otherwise-fallthrough -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. -->
```maxon

typealias Integer = int(i64.min to i64.max)

// otherwise blocks execute code when error occurs, then continue execution
enum ParseError implements Error
	Invalid = 1
end 'ParseError'

type Value implements Parsable
	export var n as Integer

	static function fromString(input String) returns Self throws ParseError
		if input.startsWith("x") 'check'
			throw ParseError.Invalid
		end 'check'
		return Value{n: input.byteLength()}
	end 'fromString'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Value'

function main() returns ExitCode
	var result = 0

	// First call succeeds - handler not executed
	let v = try Value.fromString("hello") otherwise Value.create(0)
	result = result + v.n  // adds 5

	// Second call fails - use default value
	let v2 = try Value.fromString("xbad") otherwise Value.create(0)
	result = result + v2.n  // adds 0

	// Third call succeeds
	let v3 = try Value.fromString("world") otherwise Value.create(0)
	result = result + v3.n  // adds 5

	return result
end 'main'
```
```exitcode
10
```

<!-- disabled-test: error.missing-throws -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Implementation must throw if interface requires it
type Value implements Parsable
	var n as Integer

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

<!-- disabled-test: error.throws-non-error-type -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Implementation must throw a type that conforms to Error
enum NotAnError
	Bad = 1
end 'NotAnError'

type Value implements Parsable
	var n as Integer

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

<!-- disabled-test: parsable.int-fromstring -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. And a SECOND thing is still missing for these: the `<prim>.<method>(args)` → `__<prim>_<method>` rewrite both references perform (v1 `Parser.maxon:15980-16020`, bootstrap `2-Parser.cs:24263-24276`). Today: `E2015: `try` must be applied to a call … (got 'int')`, because `int` is a KEYWORD TokenKind with no `parsePrimary` arm. -->
```maxon
function main() returns ExitCode
	let n = try int.fromString("42") otherwise 0
	return n
end 'main'
```
```exitcode
42
```

<!-- disabled-test: parsable.int-fromstring-negative -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. And a SECOND thing is still missing for these: the `<prim>.<method>(args)` → `__<prim>_<method>` rewrite both references perform (v1 `Parser.maxon:15980-16020`, bootstrap `2-Parser.cs:24263-24276`). Today: `E2015: `try` must be applied to a call … (got 'int')`, because `int` is a KEYWORD TokenKind with no `parsePrimary` arm. -->
```maxon
function main() returns ExitCode
	let n = try int.fromString("-7") otherwise 0
	return n + 10
end 'main'
```
```exitcode
3
```

<!-- disabled-test: parsable.int-fromstring-invalid -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. And a SECOND thing is still missing for these: the `<prim>.<method>(args)` → `__<prim>_<method>` rewrite both references perform (v1 `Parser.maxon:15980-16020`, bootstrap `2-Parser.cs:24263-24276`). Today: `E2015: `try` must be applied to a call … (got 'int')`, because `int` is a KEYWORD TokenKind with no `parsePrimary` arm. -->
```maxon
function main() returns ExitCode
	let n = try int.fromString("abc") otherwise 99
	return n
end 'main'
```
```exitcode
99
```

<!-- disabled-test: parsable.float-fromstring -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. And a SECOND thing is still missing for these: the `<prim>.<method>(args)` → `__<prim>_<method>` rewrite both references perform (v1 `Parser.maxon:15980-16020`, bootstrap `2-Parser.cs:24263-24276`). Today: `E2015: `try` must be applied to a call … (got 'int')`, because `int` is a KEYWORD TokenKind with no `parsePrimary` arm. -->
```maxon
function main() returns ExitCode
	let f = try float.fromString("3.14") otherwise 0.0
	let check = f * 100.0
	print("{trunc(check)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
314
```

<!-- disabled-test: parsable.float-fromstring-negative -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. And a SECOND thing is still missing for these: the `<prim>.<method>(args)` → `__<prim>_<method>` rewrite both references perform (v1 `Parser.maxon:15980-16020`, bootstrap `2-Parser.cs:24263-24276`). Today: `E2015: `try` must be applied to a call … (got 'int')`, because `int` is a KEYWORD TokenKind with no `parsePrimary` arm. -->
```maxon
function main() returns ExitCode
	let f = try float.fromString("-2.5") otherwise 0.0
	return trunc(f) + 10
end 'main'
```
```exitcode
8
```

<!-- disabled-test: parsable.bool-fromstring-true -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. And a SECOND thing is still missing for these: the `<prim>.<method>(args)` → `__<prim>_<method>` rewrite both references perform (v1 `Parser.maxon:15980-16020`, bootstrap `2-Parser.cs:24263-24276`). Today: `E2015: `try` must be applied to a call … (got 'int')`, because `int` is a KEYWORD TokenKind with no `parsePrimary` arm. -->
```maxon
function main() returns ExitCode
	let b = try bool.fromString("true") otherwise false
	if b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- disabled-test: parsable.bool-fromstring-false -->
<!-- BLOCKED ON `stdlib/Builtins.maxon` BEING LOADABLE, which is where `interface Parsable` and `__int_fromString` / `__float_fromString` / `__bool_fromString` are all declared. D6 cleared two of that module's three blockers (an interface ASSOCIATED-TYPE `typealias` at its line 56, and the reserved `__` declaration prefix its error enums and parse helpers carry). The one left is its line 285, `try (digit / fracDiv) otherwise panic(…)`: shv2's `/` is TOTAL BY DESIGN (`specs-shv2/division.md` — “no compiler-inserted guard”, a divide by zero is a hardware `#DE` for a future fault handler), so shv2 has no throwing division for a `try` to catch and rejects the form with `E2015: `try` must be applied to a call … (got '(')`. Unblocking it is the FALLIBLE-DIVISION rung, not this one: the oracle desugars a possibly-zero divide to a throwing `__checked_div` / `__checked_mod` builtin over a synthesized `__DivisionByZeroError` and requires `try` on it (E3057, E3103 — `maxon-sharp/Compiler/2-Parser.cs:23604-23641`), which would newly refuse the divides in `specs-shv2/arithmetic.md` and `arithmetic-operators.md`. And a SECOND thing is still missing for these: the `<prim>.<method>(args)` → `__<prim>_<method>` rewrite both references perform (v1 `Parser.maxon:15980-16020`, bootstrap `2-Parser.cs:24263-24276`). Today: `E2015: `try` must be applied to a call … (got 'int')`, because `int` is a KEYWORD TokenKind with no `parsePrimary` arm. -->
```maxon
function main() returns ExitCode
	let b = try bool.fromString("false") otherwise true
	if b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
