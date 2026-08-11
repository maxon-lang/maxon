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


<!-- test: error.unknown-union-case -->
A match arm naming a case the union does not have is E3034, worded "union" — the
reference compiler distinguishes a union from an enum here (`IsUnion ? "union" :
"enum"`), unlike the always-"enum" declaration diagnostics.
```maxon
union Shape
	circle
	square
end 'Shape'

function classify(s Shape) returns ExitCode
	match s 'm'
		circle then return 1
		square then return 2
		triangle then return 3
	end 'm'
end 'classify'

function main() returns ExitCode
	return classify(Shape.circle)
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/union-scalar-backing/error.unknown-union-case.test:11:3: unknown union case: 'triangle'
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
	// `.rawValue` (decoded string) and `.name` (case spelling) are observed by
	// printing; comparing either accessor directly is E3097.
	print("{m.rawValue}\n{m.name}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
mir.sub.i64
sub
```


## shv2 additions

<!-- test: payload-case-scalar-raw-value -->
A case's PAYLOAD LIST and its RAW VALUE are independent halves, so a case may write both. This is the shape
`/specs/union-struct-backing.md` forced — `add(dest ID, src ID) = OpMeta.create(1)` — read at the scalar
backing this file is about; for one rung a payload list ENDED the case and every such declaration died at
the `=` with `E2010 Expected 'an enum case name'`. Both references read the raw value after the optional
payload list, and the bootstrap prints the identical line (MEASURED).

⚠ It is also the first declaration that is **heap-boxed AND renumbered at once**: `op.rawValue` is the
written raw value `5` while `op.ordinal` is the declaration index `0`. Those are two independent questions
about one tag — where it LIVES (offset 0 of the box) and what it MEANS — and a compiler that fused them
answers one of them with the other.
```maxon
typealias ID = int(i64.min to i64.max)

union Instr
	add(dest ID, src ID) = 5
	nop = 7
end 'Instr'

function main() returns ExitCode
	let op = Instr.add(1, src: 2)
	var r = 0
	match op 'h'
		add(d, s) then r = d + s
		nop then r = 99
	end 'h'
	print("{r} {op.rawValue} {op.ordinal} {op.name}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 5 0 add
```
