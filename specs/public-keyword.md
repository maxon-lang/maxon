---
feature: public-keyword
status: stable
keywords: [public, export, module, visibility, api]
category: infrastructure
---

# Public Keyword

## Documentation

`public` is a visibility modifier, written where `export` and `module` are
written and mutually exclusive with both. It makes a declaration visible to
every file — exactly as `export` does — and additionally states that the
declaration is **API surface**.

```text
public function parse(text String) returns Config
	...
end 'parse'
```

### The four tiers

| modifier | visible to | audited by the unused-export family? |
|---|---|---|
| *(none)* | the declaring FILE | — |
| `module` | the declaring DIRECTORY and its subdirectories | yes (E3094) |
| `export` | every file | yes (E3092, E3093) |
| `public` | every file | **no** |

### `export` and `public` are the same visibility and a different claim

Anything that asks *"may this file name that symbol?"* must answer
identically for the two. They differ in one thing only, which is what the
author is saying about USE:

- `export` — other files may see this, **and I expect this program to use
  it**. If nothing outside the declaring file references it, that is a real
  finding (E3092).
- `public` — this is **API surface**; do not ask who calls it. A shared
  module may legitimately publish a symbol that this particular program
  never reaches, and "no caller in this compilation" is not the same fact
  as "dead".

### There is deliberately no `module public`

E3094 keeps its full force. A `module` declaration that wants exemption is
promoted to `public`, which changes its visibility and says so. A tier plus
a separate exemption flag would be two axes, and every reader would then
have to know it must ask about both.

### The modifiers are mutually exclusive

Any two of `export`, `module` and `public` on one declaration is **E2001**,
positioned at the second one — the modifier that cannot be there is the one
the message is about. The pair is named in a fixed order (`export`, then
`module`, then `public`) whichever order the author wrote them in, so one
illegal program has one diagnostic.

## Tests

<!-- test: public-function-is-visible-across-files -->
```maxon
// --- file: api/lib.maxon
typealias Integer = int(i64.min to i64.max)

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
typealias Integer = int(i64.min to i64.max)

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

<!-- test: public-constant-and-var-are-visible-across-files -->
```maxon
// --- file: api/limits.maxon
public let BASE = 30
public var counter = 5

// --- file: app/main.maxon
function main() returns ExitCode
	counter = counter + BASE
	return counter
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
typealias Integer = int(i64.min to i64.max)

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
typealias Integer = int(i64.min to i64.max)

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
error E3092: api/<fragment>:5:17: exported function 'api.neverCalledFromOutside' is never referenced outside its declaring file
```

## The modifiers are mutually exclusive

⚠ Each pair is tested in BOTH orders, and both orders must render the SAME sentence: the words are
named in a fixed order rather than the order they were written, so one illegal program has exactly one
diagnostic. Written the other way round, `module export` used to report `Expected function declaration,
got 'module'` at column 1 — the combination was never even recognised as one.

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

<!-- test: error.module-then-public-combined -->
```maxon
module public function bad() returns ExitCode
	return 0
end 'bad'

function main() returns ExitCode
	return bad()
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:8: 'module' and 'public' cannot be combined
```

<!-- test: error.public-then-module-combined -->
```maxon
public module function bad() returns ExitCode
	return 0
end 'bad'

function main() returns ExitCode
	return bad()
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:8: 'module' and 'public' cannot be combined
```

<!-- test: error.module-then-export-combined -->
The pre-existing pair, in the order that used to be unrecognised.
```maxon
module export function bad() returns ExitCode
	return 0
end 'bad'

function main() returns ExitCode
	return bad()
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:8: 'export' and 'module' cannot be combined
```

## `public` is a keyword, and a keyword may still be a declared name

D8's keyword-as-a-declared-name rule applies to `public` as it does to every other keyword: it may name
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
