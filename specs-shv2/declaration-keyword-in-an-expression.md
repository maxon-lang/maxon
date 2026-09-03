---
feature: declaration-keyword-in-an-expression
status: experimental
keywords: [parser, declaration, sweep, signature-index, keyword-case, token-scan]
category: parser-edge-cases
---

# A DECLARATION KEYWORD SPELLED INSIDE AN EXPRESSION DECLARES NOTHING

## Documentation

`enums-simple.md`'s `keyword-as-case-name` pins that an enum or union case may be **spelled with a
keyword**, and `keyword-named-case-members.md` pins **reading** such a case as a member (`Kw.end`,
`Kw.while`). This file pins the third face of the same fact: a case spelled with a keyword that **opens
a DECLARATION** — `function` — and read as a member in the middle of an expression.

shv2 reads a file's tokens **twice**. `Parser.foldDeclaredSignaturesInto` sweeps every file for the
declarations it makes and folds them into the whole-program `ProgramSignatures` index before any file is
parsed; the real parse runs afterwards. **The two must agree about what a declaration is**, and this
file's cases are that agreement written as programs.

A function declaration is `function` · `<name>` · `(`, and those three tokens are *also* what a
keyword-named case READ spells when a word-shaped operator follows it and the next operand is
parenthesized:

```
if m == Marker.function and (flag or not flag) 'both'
```

`function` is the case name, `and` stands in the name slot (a keyword is a legal declared name — see
`keyword-as-a-declared-name.md`), and the group's `(` follows it. Counted as a declaration, that line
opens a block that nothing ever closes: the sweep's block-depth counter never returns to zero, and every
**depth-gated** arm after it — `type`, `enum`, `union`, `interface`, `extension`, a top-level `let` or
`var` — stops firing for the rest of the file.

⚠ **THE SYMPTOM NAMES THE WRONG THING, WHICH IS WHY THIS FILE EXISTS.** The lost declaration is absent
from the index rather than malformed, so the diagnostic is about whatever *uses* it:

- a lost `union` made every `match` over it report **`E2015 Unsupported: a match pattern naming 'found'
  (only literal, range, and 'or' patterns over a scalar are supported; enum-case patterns arrive in a
  later wave)`** — an unimplemented-feature message for a feature that is implemented and works three
  lines further up the same file;
- a lost top-level binding took `ProgramSignatures.recordedDeclFor`'s drift guard down as a compiler
  **PANIC** (*"the declaration sweep never recorded top-level 'counter' … the sweep's block-depth gate
  and the parser's structure disagree about what is top level"*).

**That guard is why the binding case is pinned separately.** It covers `let`/`var` only, because only a
top-level BINDING resolves through `recordedDeclFor`; a lost `type`/`enum`/`union` has no such guard and
is silent. One shape, two failure modes, and the loud one covers a third of the shape.

⚠ **THE CURE IS POSITIONAL, AND IT HAS TO BE.** `functionDeclarationAt` asks
`declarationBeginsStatement`: a declaration keyword opens a declaration only where a declaration may
STAND — first on its line, past the `export` / `public` / `module` / `static` modifiers it may carry. It
is `ifBeginsStatement`'s rule one construct over. **The trigger is the three-token SEQUENCE and not the
word `and`**, so a cure that named that word, or that taught the sweep to skip parenthesized groups,
would leave the next spelling of the same mistake live: `or` reaches it, and so does the `if` of an
inline conditional (`Marker.function if (flag) else Marker.other`). Both are pinned below.

⚠ **AND THE POSITION TEST MUST NOT REFUSE A REAL DECLARATION**, which is the other half of the
agreement. `the-sweep-and-the-parse-agree-about-what-a-declaration-is` puts every modifier spelling —
`function`, `export function`, `export static function`, `module function` — *after* the trigger line
and reaches all of them: through a bare sibling call (which needs the type's member walk aligned) and
through free calls (which need the whole-program signature).

⚠ **ONE SHAPE OF THIS DEFECT IS NOT CURED HERE AND HAS NO CASE BELOW.** A keyword-named case standing as
a MATCH-ARM PATTERN whose body is parenthesized spells the same three tokens at a position where a
declaration genuinely may begin:

```
	return match m 'm'
		function gives (1 + 1)
		other gives 3
	end 'm'
```

Telling that from a real `function gives(x Idx)` — a function NAMED `gives`, which is legal and
compiles — needs the enclosing `match` body, which is exactly the context `keywordIsANameAt` takes from
`buildBlockExtentIndex`'s opener stack for the eight block-structure keywords. `functionDeclarationAt`
cannot ask it: `buildDeclaredNameIndex` reads `functionDeclarationAt` and `keywordIsAName` reads that
index, so the question would close a cycle between them. It is a separate rung, and a case added here
before it lands would be red.

## Tests

<!-- test: a-case-member-before-a-parenthesized-group-keeps-the-next-union -->
```maxon
typealias Idx = int(0 to 100)

enum Marker
	function
	other
end 'Marker'

function trips(m Marker, flag bool) returns bool
	if m == Marker.function and (flag or not flag) 'both'
		return true
	end 'both'
	return false
end 'trips'

union Victim
	found(index Idx)
	absent
end 'Victim'

function pick(v Victim) returns Idx
	match v 'v'
		found(index) then return index
		absent then return 0
	end 'v'
end 'pick'

function main() returns ExitCode
	if trips(Marker.function, flag: true) 'ok'
		return pick(Victim.found(42))
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: a-case-member-before-a-parenthesized-group-keeps-the-next-top-level-binding -->
```maxon
enum Marker
	function
	other
end 'Marker'

function trips(m Marker, flag bool) returns bool
	if m == Marker.function and (flag or not flag) 'both'
		return true
	end 'both'
	return false
end 'trips'

let counter = 42

function main() returns ExitCode
	if trips(Marker.function, flag: true) 'ok'
		return counter as ExitCode
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: the-or-spelling-of-the-same-sequence -->
```maxon
typealias Idx = int(0 to 100)

enum Marker
	function
	other
end 'Marker'

function trips(m Marker, flag bool) returns bool
	if m == Marker.function or (flag and not flag) 'both'
		return true
	end 'both'
	return false
end 'trips'

union Victim
	found(index Idx)
	absent
end 'Victim'

function pick(v Victim) returns Idx
	match v 'v'
		found(index) then return index
		absent then return 0
	end 'v'
end 'pick'

function main() returns ExitCode
	if trips(Marker.function, flag: false) 'ok'
		return pick(Victim.found(42))
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: the-inline-conditional-spelling-of-the-same-sequence -->
```maxon
typealias Idx = int(0 to 100)

enum Marker
	function
	other
end 'Marker'

function picked(flag bool) returns Marker
	return Marker.function if (flag) else Marker.other
end 'picked'

union Victim
	found(index Idx)
	absent
end 'Victim'

function pick(v Victim) returns Idx
	match v 'v'
		found(index) then return index
		absent then return 0
	end 'v'
end 'pick'

function main() returns ExitCode
	if picked(true) == Marker.function 'ok'
		return pick(Victim.found(42))
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: the-same-sequence-inside-a-type-method-body -->
```maxon
typealias Idx = int(0 to 100)

enum Marker
	function
	other
end 'Marker'

type Holder
	export var base as Idx

	export static function create() returns Holder
		return Self{base: 20}
	end 'create'

	export function trips(m Marker, flag bool) returns Idx
		if m == Marker.function and (flag or not flag) 'both'
			return self.base
		end 'both'
		return 0
	end 'trips'
end 'Holder'

union Victim
	found(index Idx)
	absent
end 'Victim'

function pick(v Victim) returns Idx
	match v 'v'
		found(index) then return index
		absent then return 0
	end 'v'
end 'pick'

function main() returns ExitCode
	let h = Holder.create()
	return (h.trips(Marker.function, flag: true) + pick(Victim.found(22))) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: the-sweep-and-the-parse-agree-about-what-a-declaration-is -->
```maxon
typealias Idx = int(0 to 100)

enum Marker
	function
	other
end 'Marker'

function trips(m Marker) returns bool
	return m == Marker.function and (true or false)
end 'trips'

type Holder
	export var base as Idx

	export static function create() returns Holder
		return Self{base: 12}
	end 'create'

	export function total() returns Idx
		return self.base + bonus()
	end 'total'

	export function bonus() returns Idx
		return 10
	end 'bonus'
end 'Holder'

export function exported() returns Idx
	let h = Holder.create()
	return h.total()
end 'exported'

module function scoped() returns Idx
	return 20
end 'scoped'

function main() returns ExitCode
	if trips(Marker.function) 'ok'
		return (exported() + scoped()) as ExitCode
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```
