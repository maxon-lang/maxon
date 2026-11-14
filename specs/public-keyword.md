---
feature: public-keyword
status: stable
keywords: [public, export, visibility, api]
category: infrastructure
---

# Public Keyword

## Documentation

`public` is a visibility modifier, written where `export` is written and
mutually exclusive with it. It makes a declaration visible to
every file — exactly as `export` does — and additionally states that the
declaration is **API surface**.

```text
public function parse(text String) returns Config
	...
end 'parse'
```

### The three tiers

| modifier | visible to | audited by the unused-declaration checks? |
|---|---|---|
| *(none)* | the declaring DIRECTORY and its subdirectories | yes (E3219) |
| `export` | every file | yes (E3092) |
| `public` | every file | **no** |

### `export` and `public` are the same visibility and a different claim

Anything that asks *"may this file name that symbol?"* must answer
identically for the two. They differ in one thing only, which is what the
author is saying about USE:

- `export` — other files may see this, **and I expect this program to use
  it**. If nothing outside the declaring directory's subtree references it, that is a
  real finding (E3092).
- `public` — this is **API surface**; do not ask who calls it. A shared
  library may legitimately publish a symbol that this particular program
  never reaches, and "no caller in this compilation" is not the same fact
  as "dead".

### The modifiers are mutually exclusive

`export` and `public` on one declaration is **E2001**, positioned at the second
one — the modifier that cannot be there is the one the message is about. The
pair is named in a fixed order (`export`, then `public`) whichever order the
author wrote them in, so one illegal program has one diagnostic.

## Tests

<!-- test: public-function-is-visible-across-files -->
```maxon
// --- file: api/lib.maxon
public typealias Integer = int(i64.min to i64.max)

public function answer() returns Integer
	return 42
end 'answer'

// --- file: app/main.maxon
function main() returns ExitCode
	return answer()
end 'main'
```
```exitcode
42
```

<!-- test: public-type-is-visible-across-files -->
```maxon
// --- file: api/shapes.maxon
public typealias Integer = int(i64.min to i64.max)

public type Point
	public var x as Integer
	public var y as Integer

	public static function origin() returns Point
		return Point{x: 3, y: 4}
	end 'origin'

	public function sum() returns Integer
		return x + y
	end 'sum'
end 'Point'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = Point.origin()
	return p.sum()
end 'main'
```
```exitcode
7
```

<!-- test: public-typealias-is-visible-across-files -->
```maxon
// --- file: api/types.maxon
public typealias Score = int(0 to 100)

// --- file: app/main.maxon
function scoreOf(s Score) returns Score
	return s
end 'scoreOf'

function main() returns ExitCode
	return scoreOf(11)
end 'main'
```
```exitcode
11
```

<!-- test: public-enum-is-visible-across-files -->
```maxon
// --- file: api/color.maxon
public enum Color
	red
	green
	blue
end 'Color'

// --- file: app/main.maxon
function main() returns ExitCode
	let c = Color.blue
	match c 'check'
		blue then return 7
		red then return 0
		green then return 0
	end 'check'
end 'main'
```
```exitcode
7
```

<!-- test: public-constants-are-visible-across-files -->
```maxon
// --- file: api/limits.maxon
public let BASE = 30
public let START = 5 as ExitCode

// --- file: app/main.maxon
function main() returns ExitCode
	return START + BASE
end 'main'
```
```exitcode
35
```

<!-- test: public-is-not-audited-where-export-is -->
⭐⭐ **THE DISCRIMINATING PAIR.** This program and the next are identical but for one word. Under
`public` the unreferenced declaration is API surface and nothing is reported; under `export` it is
E3092. If `public` were merely a spelling of `export`, this pair could not both hold.
```maxon
// --- file: api/lib.maxon
public typealias Integer = int(i64.min to i64.max)

public function neverCalledFromOutside() returns Integer
	return 5
end 'neverCalledFromOutside'

export function entry() returns Integer
	return neverCalledFromOutside()
end 'entry'

// --- file: app/main.maxon
function main() returns ExitCode
	return entry()
end 'main'
```
```exitcode
5
```

<!-- test: error.the-same-declaration-as-export-is-audited -->
```maxon
// --- file: api/lib.maxon
export typealias Integer = int(i64.min to i64.max)

export function neverCalledFromOutside() returns Integer
	return 5
end 'neverCalledFromOutside'

export function entry() returns Integer
	return neverCalledFromOutside()
end 'entry'

// --- file: app/main.maxon
function main() returns ExitCode
	return entry()
end 'main'
```
```maxoncstderr
error E3092: api/<fragment>:5:17: exported function 'api.neverCalledFromOutside' is never referenced outside its declaring directory
```

## The modifiers are mutually exclusive

⚠ Each pair is tested in BOTH orders, and both orders must render the SAME sentence: the words are
named in a fixed order rather than the order they were written, so one illegal program has exactly one
diagnostic.

<!-- test: error.export-then-public-combined -->
```maxon
export public function bad() returns ExitCode
	return 0
end 'bad'

function main() returns ExitCode
	return bad()
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:8: 'export' and 'public' cannot be combined
```

<!-- test: error.public-then-export-combined -->
```maxon
public export function bad() returns ExitCode
	return 0
end 'bad'

function main() returns ExitCode
	return bad()
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:8: 'export' and 'public' cannot be combined
```

<!-- test: error.module-after-public-is-a-stray-word -->
`module` is no modifier, so after `public` it is a stray word where a declaration keyword belongs.
```maxon
public module function bad() returns ExitCode
	return 0
end 'bad'

function main() returns ExitCode
	return bad()
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:8: Expected function declaration, got 'module'
error E3004: <fragment>:7:9: call to undefined function 'bad'
```

## `public` is a keyword, and a keyword may still be a declared name

The keyword-as-a-declared-name rule applies to `public` as it does to every other keyword: it may name
a declaration, an enum case, or a member after `.`. What it may not be is a bare value in expression
position — and nothing in the corpus wanted to be.

<!-- test: public-may-name-an-enum-case -->
```maxon
enum Access
	public
	private
end 'Access'

function main() returns ExitCode
	let a = Access.public
	match a 'which'
		public then return 3
		private then return 0
	end 'which'
end 'main'
```
```exitcode
3
```

## An error a `public` API throws is itself `public`

A `public` function's `throws` clause is part of its surface: a caller that cannot name the error type
cannot write a function that receives one, so it can only discard what went wrong. `JsonError`
(`Json.parse`), `JsonAccessError` (the `JsonDoc` accessors) and `StreamError` (`StreamReader.readLine`, the read `Console.stdin()` answers) are
therefore `public` like the APIs that throw them.

<!-- test: stdlib-error-enums-thrown-by-public-api-are-nameable -->
Each error type is named in a PARAMETER position, which a binding inferred from `otherwise (e)` never
does, so the case needs the name to be visible and not merely the value. `StreamError` is named without
reading anything, so the program compiles on every lane including the ones with no console substrate.
```maxon
function describeParse(e JsonError) returns String
	return match e 'kind'
		unexpectedEof gives "unexpectedEof"
		unexpectedChar gives "unexpectedChar"
		invalidEscape gives "invalidEscape"
		invalidNumber gives "invalidNumber"
		invalidSurrogate gives "invalidSurrogate"
		trailingContent gives "trailingContent"
	end 'kind'
end 'describeParse'

function describeAccess(e JsonAccessError) returns String
	return match e 'kind'
		wrongType gives "wrongType"
		notObject gives "notObject"
		notArray gives "notArray"
		missingKey gives "missingKey"
		outOfBounds gives "outOfBounds"
	end 'kind'
end 'describeAccess'

function describeStream(e StreamError) returns String
	return match e 'kind'
		timedOut gives "timedOut"
		closed gives "closed"
		failed(reason) gives "failed: {reason}"
		secure gives "secure"
	end 'kind'
end 'describeStream'

function parseVerdict(text String) returns String
	let doc = try Json.parse(text) otherwise (e) 'malformed'
		return describeParse(e)
	end 'malformed'

	return "parsed a document rooted at {doc.root}"
end 'parseVerdict'

function accessVerdict(text String) returns String
	let doc = try Json.parse(text) otherwise return "unparsed"
	let value = try doc.getString(doc.root, key: "a") otherwise (e) 'wrongShape'
		return describeAccess(e)
	end 'wrongShape'

	return "a is {value}"
end 'accessVerdict'

function main() returns ExitCode
	let parsed = parseVerdict("\{")
	let accessed = accessVerdict("\{\"a\": 1\}")
	let streamed = describeStream(StreamError.closed)
	print("parse: {parsed}\n")
	print("access: {accessed}\n")
	print("stream: {streamed}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
parse: unexpectedEof
access: wrongType
stream: closed
```
