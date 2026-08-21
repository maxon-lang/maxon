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

⚠ **THE EXEMPTION ITSELF IS NOT TESTED IN THIS FILE, and that is a property
of the compilers rather than of the feature.** E3092/E3093/E3094 are claimed
by `selfhosted` and `shv2` only — the C# bootstrap raises none of them — so
a case pinning the exemption would compile clean here and prove nothing. It
lives where it can discriminate: `specs-shv2/unused-export.md` carries the
matching pair (the same program under `export` reports E3092, under `public`
runs), and `specs-shv2/public-keyword.md` carries it again beside these
cases. What this file pins is everything about `public` that every compiler
must agree on: that it is a visibility modifier, that its reach is `export`'s,
and that combining it with either other modifier is refused.

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

<!-- test: public-type-with-public-members -->
```maxon
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

function main() returns ExitCode
	let p = Point.origin()
	return p.sum()
end 'main'
```
```exitcode
7
```

<!-- test: public-typealias-constant-and-var -->
```maxon
public typealias Score = int(0 to 100)

public let BASE = 30
public var counter = 5

function scoreOf(s Score) returns Score
	return s
end 'scoreOf'

function main() returns ExitCode
	counter = counter + BASE
	return counter + scoreOf(0)
end 'main'
```
```exitcode
35
```

<!-- test: public-enum -->
```maxon
public enum Color
	red
	green
	blue
end 'Color'

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

<!-- test: public-may-name-an-enum-case -->
D8's keyword-as-a-declared-name rule applies to `public` as it does to every
other keyword: it may name a declaration, an enum case, or a member after
`.`. What it may not be is a bare value in expression position.
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
