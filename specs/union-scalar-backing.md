---
feature: union-scalar-backing
status: experimental
keywords: [union, backing, rawValue, int, string, char, float, enum-like]
category: type-system
---

# Union Scalar Backing

## Documentation

A `union` whose cases all omit associated-value payloads may give each case an
explicit scalar raw value — `int`, `float`, `String`, or `char` — exactly the
way an `enum` does. Such a union is representationally identical to an enum: a
bare integer carrying the case's raw value, never heap-boxed. The cases are
usable as bare values (`U.case`), expose `.rawValue` / `.name`, and match like
an enum. (The static `.fromRawValue` / `.fromName` lookup helpers remain an
`enum`-only surface — a bare `U.fromRawValue` reference on a union is an
unknown-case error, matching the C# bootstrap.)

```text
union ErrorCode
	lexerUnexpectedCharacter = 1001
	parserUnexpectedToken = 2001
	semanticTypeMismatch = 3001
end 'ErrorCode'

let code = ErrorCode.parserUnexpectedToken
let n = code.rawValue            // 2001
```

This is the same model the compiler's own `ErrorCode` union uses. A union with
*any* payload-bearing case keeps the heap-boxed representation (see
`union-cases` / `union-struct-backing`); scalar backing applies only to
all-bare unions.

The backing kind is fixed by the first explicitly-backed case; mixing kinds
(e.g. an `int` case and a `String` case in the same union) is rejected, and
duplicate raw values are rejected — identical to enum backing rules.

## Tests

<!-- test: int-backed-union-rawvalue -->
A bare reference to an int-backed union case exposes its raw value via
`.rawValue`.
```maxon
union ErrorCode
	lexerUnexpectedCharacter = 1001
	parserUnexpectedToken = 2001
	semanticTypeMismatch = 3001
end 'ErrorCode'

function main() returns ExitCode
	let code = ErrorCode.semanticTypeMismatch
	return (code.rawValue mod 100) as ExitCode
end 'main'
```
```exitcode
1
```


<!-- test: int-backed-union-match -->
A scalar-backed union matches like an enum — bare case names, exhaustive.
```maxon
union Status
	ok = 200
	notFound = 404
	serverError = 500
end 'Status'

function classify(s Status) returns ExitCode
	return match s 'm'
		ok gives 0
		notFound gives 4
		serverError gives 5
	end 'm'
end 'classify'

function main() returns ExitCode
	return classify(Status.notFound)
end 'main'
```
```exitcode
4
```


<!-- test: string-backed-union-name -->
A string-backed union keeps an integer runtime tag but exposes the decoded
string through `.rawValue`, and `.name` returns the case spelling.
```maxon
union Mnemonic
	add = "mir.add.i64"
	sub = "mir.sub.i64"
end 'Mnemonic'

function main() returns ExitCode
	let m = Mnemonic.sub
	if m.rawValue == "mir.sub.i64" and m.name == "sub" 'ok'
		return 3
	end 'ok'
	return 1
end 'main'
```
```exitcode
3
```
