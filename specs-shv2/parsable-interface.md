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

<!-- test: parsable.type-implements-parsable -->
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

<!-- test: parsable.successful-parse -->
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

<!-- test: parsable.throws-on-invalid-input -->
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

<!-- test: parsable.multiple-error-conditions -->
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

<!-- test: parsable.otherwise-fallthrough -->
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

<!-- test: error.missing-throws -->
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

<!-- test: error.throws-non-error-type -->
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

<!-- test: parsable.throws-a-compiler-synthesized-error-enum -->
The POSITIVE control for the case above, and it is not a formality: an impl may narrow an abstract
`throws Error` to `ArrayError` — the error every throwing `Array` accessor throws, which four committed
cases already propagate. shv2 SYNTHESIZES that enum rather than reading it, because `stdlib/Array.maxon` is
not a listed module, and **a synthesized seed owes every clause the declaration it stands in for writes**:
`stdlib/Array.maxon:6` is `export enum ArrayError implements Error`, and the bootstrap seeds the same fact
explicitly for the family it synthesizes (`2-Parser.cs:1371`, `conformingInterfaces: ["Error"]`).

MEASURED against the oracle, which compiles this and exits 5. shv2 refused it with
`E3016: … throws 'ArrayError' which does not conform to Error` for as long as the conformance check read a
list the seed left empty — a legal program rejected by the very rule that was added to reject an illegal
one. `__DivisionByZeroError` is the same shape and is seeded the same way.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

type Value implements Parsable
	export var n as Integer

	static function fromString(input String) returns Self throws ArrayError
		var digits = IntegerArray.create()
		digits.push(input.byteLength() + 4)
		let first = try digits.get(0)
		return Value{n: first}
	end 'fromString'
end 'Value'

function main() returns ExitCode
	let v = try Value.fromString("x") otherwise panic("fromString cannot fail on a one-element array")
	return v.n as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: parsable.int-fromstring -->
```maxon
function main() returns ExitCode
	let n = try int.fromString("42") otherwise 0
	return n
end 'main'
```
```exitcode
42
```

<!-- test: parsable.int-fromstring-negative -->
```maxon
function main() returns ExitCode
	let n = try int.fromString("-7") otherwise 0
	return n + 10
end 'main'
```
```exitcode
3
```

<!-- test: parsable.int-fromstring-invalid -->
```maxon
function main() returns ExitCode
	let n = try int.fromString("abc") otherwise 99
	return n
end 'main'
```
```exitcode
99
```

<!-- test: parsable.float-fromstring -->
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

<!-- test: parsable.float-fromstring-negative -->
```maxon
function main() returns ExitCode
	let f = try float.fromString("-2.5") otherwise 0.0
	return trunc(f) + 10
end 'main'
```
```exitcode
8
```

`float.fromString` and `"{x}"` are the two ends of ONE conversion — `stdlib/Builtins.maxon`'s
`__float_bitsFromText`, read forwards and backwards. They used to be two: the printer was the exact
shortest-round-trip search while the reader was a naive `intPart + Σ digit/10^k` accumulation, so
print-then-parse was not guaranteed to return the value it started from. These two cases pin the
convergence, and each fails against the reader that was replaced.

<!-- test: parsable.float-fromstring-round-trips-interpolation -->
Print a float, parse the text back, print it again. The shortest-round-trip printer emits the
shortest decimal that reads back as the SAME double — a claim only an exact reader can honour, so
the two lines being identical and `a == b` being true is the whole convergence in one program.

⚠ **THE VALUE WAS CHOSEN BY MEASURING AGAINST THE REPLACED READER, NOT BY LOOKING HARD.** Most
17-digit decimals round-trip through a naive `Σ digit/10^k` too, so most of them pin nothing:
`3.14159`, `0.30000000000000004`, `0.12345678901234567`, `0.9999999999999999` and
`2.2250738585072014` were all tried and all stayed GREEN against the old reader.
`1.7976931348623157` — `f64.max`'s significand — is one that does not: the naive accumulation
lands on `1.7976931348623155`, one ULP low. The LITERAL is read by the compiler's own copy of the
exact reader either way, so `a` is fixed and `b` alone moves, which is what makes this a test of
the READER rather than of the printer.
```maxon
function main() returns ExitCode
	let a = 1.7976931348623157
	let text = "{a}"
	let b = try float.fromString(text) otherwise 0.0
	print("{text}\n")
	print("{b}\n")
	if a == b 'exact'
		return 0
	end 'exact'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
1.7976931348623157
1.7976931348623157
```

<!-- test: parsable.float-fromstring-past-the-i64-integer-part -->
An integer part wider than an i64. The replaced reader accumulated it in an `int` and WRAPPED —
`18446744073709551617` is 2^64 + 1, so its naive `intPart` came back as 1 and the answer was `1.0`.
The exact reader builds the magnitude in limbs and rounds it to the nearest double, 2^64.

⚠ The printed digits are `18446744073709552` followed by zeros, NOT 2^64's exact expansion
`…551616`. Both name the same double and the printer answers with the SHORTEST decimal that reads
back as it (17 significant digits here) — so this line also pins that the two ends agree about which
double is meant rather than about which digits spell it.
```maxon
function main() returns ExitCode
	let f = try float.fromString("18446744073709551617.0") otherwise 0.0
	print("{f}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709552000.0
```

<!-- test: parsable.bool-fromstring-true -->
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

<!-- test: parsable.bool-fromstring-false -->
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

<!-- test: parsable.byte-fromstring -->
**THE SECOND DOOR (A1s-prim), AND THE ONE NEITHER REFERENCE COMPILER HAS.** `byte` is not a keyword in
shv2 — there is no `TokenKind.byte` at all — so `byte.fromString(…)` never needed a `parsePrimary` arm and
has always flowed through the ordinary qualified-call path. What it got there was the mangled callee
`byte.fromString`, which no file declares: `E3004: call to undefined function 'byte.fromString'`, a WRONG
ANSWER rather than a missing feature. v1 has the identical hole and says so at `Parser.maxon:15975` (it
tests three TOKEN KINDS, and `byte` lost its keyword there); the bootstrap covers `byte` only because it
still has a `TokenType.Byte` to test. Recognizing the primitive TYPE NAME rather than its token kind is
what makes one rule serve both doors.
```maxon
function main() returns ExitCode
	let n = try byte.fromString("41") otherwise 0
	return n + 1
end 'main'
```
```exitcode
42
```

<!-- test: parsable.byte-user-type-outranks-primitive -->
**A USER DECLARATION OUTRANKS THE PRIMITIVE READING, and this is the case that makes the clause
load-bearing.** `type int` is refused at its own name (`E2010: Expected identifier but got 'int'`), so
`int.`/`float.`/`bool.` can never be contested — but `byte` is an ordinary identifier here, and a `type
byte` with its own `static function fromString` compiled and ran on this tree BEFORE the rewrite existed.
Minting `__byte_fromString` ahead of it would silently re-point a call that already worked at the stdlib
body: a wrong answer with no diagnostic, which no exit code in the rest of this file could see. The
precedence is asked as `declaresCallee` of the same mangled name the ordinary path builds, so the two
readings of one call site cannot come to disagree about what "the user declared it" means.
```maxon
type byte
	export let n as Integer

	export static function fromString(_ String) returns Integer
		return 7
	end 'fromString'
end 'byte'

function main() returns ExitCode
	return byte.fromString("41")
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: parsable.bound-keyword-outranks-primitive-static -->
**AND THE KEYWORD QUALIFIERS HAVE THE SAME CONTEST AFTER ALL — NOT THROUGH A `type`, THROUGH A BINDING
(A1s-prim review).** The case above turns on `byte` being an identifier, and the reason `int`/`float`/`bool`
were argued to be uncontestable is that no user TYPE may be declared under a keyword. That argument covers
declarations and not bindings: D8 admits a keyword-named PARAMETER, so `function f(float Box)` puts a VALUE
in scope under the name `float`, and `float.fromString(…)` is then a method call on that value — the
identical token shape `primitiveStaticCallAt` claims.

⚠ **THE ARM ORDER IS WHAT DECIDES IT, AND NOTHING ELSE DOES.** Both readings route to `parseDottedPrimary`,
which asks the scope BEFORE the type reading — so the binding wins, and the new arm changed no answer here.
That is a property of an ORDERING inside one routine, which is exactly the kind a green suite cannot see:
hoist the primitive test above the scope test and this call silently stops calling the user's method and
starts calling `stdlib/Builtins.maxon`'s parser, with no diagnostic anywhere. Pinned as an exit code so the
re-point is loud. (Either way of getting it wrong fails: the primitive reading of `float.fromString` is
THROWING, so it cannot even be written without `try`.)
```maxon
type Box
	export let n as Integer

	export static function create(n Integer) returns Box
		return Self{n: n}
	end 'create'

	export function fromString(_ String) returns Integer
		return self.n
	end 'fromString'
end 'Box'

function f(float Box) returns ExitCode
	return float.fromString("9")
end 'f'

function main() returns ExitCode
	return f(Box.create(7))
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: error.unknown-primitive-static -->
**AN UNKNOWN STATIC ON A PRIMITIVE NAMES WHAT THE AUTHOR WROTE — NEVER THE MANGLED SYMBOL.** Both
references fail this in the two available ways: the bootstrap rewrites unconditionally and reports
`E2004: Undefined function '__int_frobnicate'` (`2-Parser.cs:24399-24411`), naming a symbol the author never
typed; v1 reports nothing at all and PANICS in the backend (`Targets/Shared/StdOpHelpers.maxon:362`, which
its own `TypeResolution.maxon:10597-10616` admits). shv2 ruled on that class at D11c, so the check that
decides whether to rewrite is the same check that keeps the author's spelling in the message.

⚠ **IT IS THE PARSER'S REFUSAL AND IT HAS TO BE — MEASURED BY REMOVING IT, IN TWO STEPS.**
`isBuiltinConformanceImplName` declares every callee under `int.`/`float.`/`bool.` to be the compiler's own.
Remove this refusal and the RESERVED-CALLEE gate catches the call instead, telling an author who wrote
`int.frobnicate` that *"the `__` prefix names a compiler intrinsic"* — the right code, describing a program
they do not have. Narrow that gate to the `__` prefix as well and there is no diagnostic at all: the same
qualifier makes `int.frobnicate` an `isSignaturelessCompilerCallee`, `SemanticCheck.validateCall` returns
for exactly those before its E3004, and `let n = int.frobnicate("41")` reaches the backend —
`panic … resolveCallFixups: call to unknown function 'int.frobnicate'`. See the `byte` twin below for what
happens where none of that applies.
```maxon
function main() returns ExitCode
	let n = try int.frobnicate("41") otherwise 0
	return n
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:14: Unsupported: 'int' has no static method named 'frobnicate' — a primitive type's statics are the free functions `stdlib/Builtins.maxon` declares, and it declares none of that name
```

<!-- test: error.unknown-byte-static-is-an-ordinary-undefined-function -->
**THE `byte` TWIN, AND THE CONTROL ON THE ASYMMETRY ABOVE.** `byte.` is an ordinary identifier qualifier —
nothing declares it compiler-owned — so an unresolved member is left to the authority for "no such
function", `SemanticCheck`, reading the registry the real parse built. That is `parseQualifiedCall`'s own
rule (*"the sweep must never own a veto"*) obeyed wherever it can be, and it is also what keeps a user's
`type byte` answerable in its own terms rather than in the primitive's. This diagnostic is UNCHANGED by the
rewrite — it is what the tree already reported — and pinning it is what would catch the refusal above
being widened to a qualifier that does not need it.
```maxon
function main() returns ExitCode
	let n = try byte.frobnicate("41") otherwise 0
	return n
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:19: call to undefined function 'byte.frobnicate'
```

<!-- test: error.minted-callee-arity-names-the-source-spelling -->
**THE MINT MUST NOT REACH THE AUTHOR'S DIAGNOSTICS (A1s-prim, coordinator ruling).** `int.fromString` links
to `__int_fromString`, so every message that quotes a callee had to be taught the difference or it would
report the right error code about a program the reader does not have — the D11c class, which shv2 ruled on
and fixed in its own compiler. MEASURED before the fix: `'__int_fromString' expects 1 argument(s) but 0 were
provided`.

⚠⚠ **AND THE ANSWER IS THE CALL SITE'S, NEVER THE NAME'S.** `stdlib/Builtins.maxon` declares
`__int_fromString` AND CALLS it (`__byte_fromString`'s first line) having genuinely written those bytes, so
a name-keyed rewrite would rename the author's own diagnostic in their own file — D12's lossy key with the
arrow reversed. `MaxonOp.call`/`tryCall` therefore carry a `CalleeMint`, and `SemanticCheck`'s
`callDiagnosticNoun` reads it. MEASURED in the other direction, by breaking that stdlib call: it reports
`stdlib/Builtins.maxon:322:19: '__int_fromString' expects 1 argument(s) but 0 were provided` — the mangled
name, in the file whose author wrote it. That half CANNOT be pinned by a fragment (the exemption is an
identity compare against the real `<stdlibDir>/Builtins.maxon`, which no spec file can be), so it is
recorded here rather than tested.
```maxon
function main() returns ExitCode
	let n = try int.fromString() otherwise 0
	return n
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:18: 'int.fromString' expects 1 argument(s) but 0 were provided
```

<!-- test: error.minted-callee-missing-try-names-the-source-spelling -->
**E3057 IS THE SAME LEAK AT THE MISTAKE PEOPLE ACTUALLY MAKE** — forgetting the `try` on a throwing call —
so it is pinned beside the arity one rather than trusted to share its fix. Both nouns come from the single
`callDiagnosticNoun` door; before it, this said `throwing function requires try: '__int_fromString'`.

⚠ `byte.fromString` gets the identical treatment through the identical door — it is minted by the same
routine at the same site — so the two doors of this rung cannot diverge on the noun either.
```maxon
function main() returns ExitCode
	let n = int.fromString("42")
	return n
end 'main'
```
```maxoncstderr
error E3057: <fragment>:3:14: throwing function requires try: 'int.fromString'
```
