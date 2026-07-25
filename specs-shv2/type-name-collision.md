---
feature: type-name-collision
status: stable
keywords: [type, typealias, enum, union, interface, duplicate, collision, diagnostics]
category: diagnostics
---

# One type NAME, one declaration

## Documentation

A program names a type through five declarations — `type`, `enum`, `union`, `interface`, and
`typealias` — and every one of them files the name in a **whole-program map keyed by the bare name**.
Nothing checked that two of them had not claimed the same name, so a program that declared one twice
compiled, and a **fixed resolution cascade** picked the winner rather than the author.

That is a wrong ANSWER, and its shape depends on which declaration the cascade happens to reach first:

- `type Box` and then `typealias Box = int(0 to 10)` compiled **completely clean**. The alias was
  discarded without a word, and every `Box` in the program meant the struct.
- The same two declarations in the opposite order compiled to the same thing, so the alias's range
  never applied and a cast into it was reported as `E3009: Cannot cast from int to struct` — a
  consequence of the collision, describing nothing the author could act on.
- Across files it is worse than order-dependent, it is unconditional: a `type Box` in **any** file
  beat a `typealias Box` in the reader's **own** file, because the struct registry is consulted bare
  and first while the alias registry is the only one that is file-scoped.

**The rule: a type name is declared exactly once.** A collision is reported at the **later**
declaration — its own file, its own name token — naming the kind the incumbent declared, which is the
half the author cannot see. The incumbent is the line that was fine.

Two typealiases of the same name are covered by their own code, **E3061**; everything else is
**E3006**, the code a name declared twice already carries for functions and for top-level
`let`/`var` (`specs/duplicate-functions.md`, `specs/static-variables.md` — where a `let` and a `var`
sharing one name collide *regardless of which keyword introduced them*, the same kind-independence
this rule states for types).

## The ONE exception: a typealias of the SAME FORM in ANOTHER file

A non-exported `typealias` is **file-local** (`specs/duplicate-typealias.md`), so two files may each
declare `Limit` and neither disturbs the other. That case is `specs-shv2/typealias-file-scope.md` and
it stays legal — `stdlib/` depends on it in two forms at once: seven files privately declare
`Byte = int(0 to u8.max)` (a **ranged** alias) and five declare `ByteArray = Array with Byte`
(a **generic-instance** alias).

The carve-out is per alias FORM, not per keyword, because the three forms of `typealias` denote three
different things and only one of them is file-scoped:

| declaration | denotes | registry |
|---|---|---|
| `typealias N = int(lo to hi)` | a **ranged** alias — erases to `int`/`float` | file-scoped (`RangedAliasRegistry`) |
| `typealias N = function(…)` | a **function** alias — mints a nominal `function` type | bare, whole-program |
| `typealias N = Base with Args` | a **generic-instance** alias — mints an instance | bare, whole-program |

So two *ranged* aliases in two files are legal, and two *generic-instance* aliases in two files are
legal, but a **ranged** alias in one file and a **function** alias in another are not: the bare
function-alias door wins from every file, including the ranged alias's own. Measured before this rule:
a file whose only `Handler` is `typealias Handler = int(0 to 10)` had its own `5 as Handler` rejected
with `Cannot cast from int to function`, against a declaration in a file it never mentions.

The exception is **cross-file and nothing more.** One file declaring the name twice is E3061 whether
or not other files declare it too, and that is a property of *which* declaration a newcomer is judged
against: the one that **most recently claimed the name**, never the first one. A registry keeping the
first would measure both of a file's two declarations against the far file, find each of them the
legal cross-file case, and accept the duplicate in silence — for every name in `stdlib/`'s shape.

**Out of scope**, and unchanged: two *same-form* aliases in two files that do not AGREE — two function
aliases with different signatures, or two generic-instance aliases over different instances — are
still resolved last-wins. Making those agree is cross-file name resolution, the rung that also owns
E3063; the ranged form's half of it is already enforced (E3105).

## Tests

<!-- test: error.type-then-typealias -->
The silent case. `Box` is declared as a `type` and then as a ranged `typealias`; the alias was
discarded with no diagnostic at all and this program compiled and returned 7.
```maxon
typealias Small = int(0 to 100)

type Box
	export var v as Small
	export static function make() returns Self
		return Self{v: 7}
	end 'make'
end 'Box'

typealias Box = int(0 to 10)

function main() returns ExitCode
	let b = Box.make()
	return b.v
end 'main'
```
```maxoncstderr
error E3006: <fragment>:11:11: duplicate definition of 'Box' — already declared as `type Box`
```


<!-- test: error.typealias-then-type -->
The same collision written the other way round. It is reported at the `type`, which is the later
declaration here — never at the alias, which was fine when it was written.
```maxon
typealias Small = int(0 to 100)
typealias Box = int(0 to 10)

type Box
	export var v as Small
end 'Box'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:5:6: duplicate definition of 'Box' — already declared as `typealias Box`
```


<!-- test: error.crossfile-type-and-typealias -->
Across files, and this is the direction file scoping does NOT rescue: `b.maxon`'s own `typealias Box`
is unreachable from `b.maxon` itself, because the struct registry is bare and is consulted first. The
cast below was rejected with `E3009: Cannot cast from int to struct` — against a `type` declared in a
file it never names — and that was the ONLY thing the program said.

**Both are reported now, and the E3009 comes first.** The cast error is raised by the parser, which
runs before the merge the collision is detected at; it is a true statement about the program as the
compiler must read it, and E3006 is the reason it reads that way. What matters is that the E3009 no
longer arrives ALONE: aborting `b.maxon`'s parse used to discard everything it had declared, so the
duplicate that caused the cast error was suppressed by the cast error
(`Parser.abortedParseArtifact`).
```maxon
// --- file: a.maxon
typealias Small = int(0 to 100)

export type Box
	export var v as Small
end 'Box'

// --- file: b.maxon
typealias Box = int(0 to 10)

export function useIt() returns ExitCode
	return 5 as Box
end 'useIt'

// --- file: main.maxon
function main() returns ExitCode
	return useIt()
end 'main'
```
```maxoncstderr
error E3009: <fragment>:13:11: Cannot cast from int to struct
error E3006: <fragment>:10:11: duplicate definition of 'Box' — already declared as `type Box`
```


<!-- test: error.duplicate-type-same-file -->
Two `type` declarations of one name in one file. Nothing diagnosed this either: the second layout
simply replaced the first in the registry, so a field the first declared vanished.
```maxon
typealias Small = int(0 to 100)

type Box
	export var v as Small
end 'Box'

type Box
	export var w as Small
end 'Box'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:8:6: duplicate definition of 'Box' — already declared as `type Box`
```


<!-- test: error.duplicate-type-crossfile -->
The same duplicate across two files. A `type` has no file-local form to fall back on — the registry
is bare and whole-program — so this is a collision wherever the two declarations sit.
```maxon
// --- file: a.maxon
typealias Small = int(0 to 100)

export type Box
	export var v as Small
end 'Box'

// --- file: b.maxon
typealias Small = int(0 to 100)

export type Box
	export var w as Small
end 'Box'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:12:13: duplicate definition of 'Box' — already declared as `type Box`
```


<!-- test: error.type-and-enum -->
The rule is kind-independent, so a `type` and an `enum` collide exactly as two `type`s do.
```maxon
typealias Small = int(0 to 100)

type Shape
	export var v as Small
end 'Shape'

enum Shape
	circle
	square
end 'Shape'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:8:6: duplicate definition of 'Shape' — already declared as `type Shape`
```


<!-- test: error.interface-and-type -->
An `interface` claims a type name too, and the diagnostic names the kind the incumbent declared —
the half the author cannot see from the line being reported.
```maxon
typealias Small = int(0 to 100)

interface Drawable
	function draw() returns Small
end 'Drawable'

type Drawable
	export var v as Small
end 'Drawable'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:8:6: duplicate definition of 'Drawable' — already declared as `interface Drawable`
```


<!-- test: error.duplicate-union-same-file -->
`union` is spelled as its own keyword in the message, for the reason every other diagnostic that
mentions one does: a declaration must not be called by a keyword its author did not write.
```maxon
union Tag
	first
	second
end 'Tag'

union Tag
	third
end 'Tag'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:7:7: duplicate definition of 'Tag' — already declared as `union Tag`
```


<!-- test: error.duplicate-function-alias-same-file -->
Two typealiases in one file are E3061 whichever FORM they take — the code already covers the ranged
pair (`specs/export-keyword.md`'s `error.duplicate-typealias-same-file`), and a function alias
declared twice is the same fact: no qualification could disambiguate two declarations in one file.
```maxon
typealias Handler = function() returns int
typealias Handler = function() returns int

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3061: <fragment>:3:11: Duplicate typealias 'Handler'
```


<!-- test: error.duplicate-alias-same-file-while-another-file-declares-it -->
The carve-out is per PAIR, and the pair that matters is the newcomer against the declaration that
most recently claimed the name — **never** against the first one. `a.maxon` declares `L` and `b.maxon`
declares it twice: `b.maxon`'s second `L` is a same-file duplicate and stays E3061, exactly as it
would if `a.maxon` did not exist. Measured against a registry that kept the FIRST declaration
instead: each of `b.maxon`'s two was compared with `a.maxon`'s, found to be the legal cross-file case,
and accepted — the duplicate compiled in silence. That is `stdlib/`'s own shape (seven files declare
`Byte`), so it would have been open for every name the rule exists to protect.
```maxon
// --- file: a.maxon
typealias L = int(0 to 10)

export function fromA() returns L
	return 1
end 'fromA'

// --- file: b.maxon
typealias L = int(0 to 20)
typealias L = int(0 to 30)

// --- file: main.maxon
function main() returns ExitCode
	return fromA()
end 'main'
```
```maxoncstderr
error E3061: <fragment>:11:11: Duplicate typealias 'L'
```


<!-- test: error.duplicate-generic-alias-same-file-while-another-file-declares-it -->
The same hole for the GENERIC-INSTANCE form, which is the half of the carve-out `stdlib/` exercises
with `ByteArray = Array with Byte` in five files. Nothing checked a generic alias for duplication
before this rule existed, so the file-local carve-out must not hand it a way to stay unchecked.
```maxon
// --- file: base.maxon
typealias Small = int(0 to 100)

export type Bx uses T
	export var value as T
end 'Bx'

// --- file: a.maxon
typealias Small = int(0 to 100)
typealias G = Bx with Small

// --- file: b.maxon
typealias Small = int(0 to 100)
typealias G = Bx with Small
typealias G = Bx with Small

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3061: <fragment>:16:11: Duplicate typealias 'G'
```


<!-- test: error.crossfile-ranged-and-function-alias -->
Two files, one name, two alias FORMS. This is the pair the file-local carve-out does not cover: the
function-alias registry is bare and whole-program, so it answers for `a.maxon` too, and `a.maxon`'s
own `5 as Handler` was rejected with `Cannot cast from int to function` — a diagnostic in a file
whose only `Handler` is an `int` alias, naming a declaration it never mentions, and the only thing
the program said. The E3061 that explains it now accompanies it (see the previous test on why the
consequence is printed first).
```maxon
// --- file: a.maxon
typealias Handler = int(0 to 10)

export function useA() returns ExitCode
	return 5 as Handler
end 'useA'

// --- file: b.maxon
typealias Handler = function() returns int

export function useB(h Handler) returns ExitCode
	return h() as ExitCode
end 'useB'

// --- file: main.maxon
function main() returns ExitCode
	return useA()
end 'main'
```
```maxoncstderr
error E3009: <fragment>:6:11: Cannot cast from int to function
error E3061: <fragment>:10:11: Duplicate typealias 'Handler'
```


<!-- test: crossfile-generic-alias-same-name-still-legal -->
The guard against overreach, and it is not hypothetical: this is exactly the shape `stdlib/` already
holds, where FIVE files each declare `typealias ByteArray = Array with Byte`. Two files declaring one
generic-instance alias is the file-local case, the same as two ranged aliases, and it stays legal.
```maxon
// --- file: base.maxon
typealias Integer = int(i64.min to i64.max)

export type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Box'

// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)
typealias IntBox = Box with Integer

export function fromA() returns ExitCode
	let b = IntBox.create(20)
	return b.get()
end 'fromA'

// --- file: b.maxon
typealias Integer = int(i64.min to i64.max)
typealias IntBox = Box with Integer

export function fromB() returns ExitCode
	let b = IntBox.create(22)
	return b.get()
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	return fromA() + fromB()
end 'main'
```
```exitcode
42
```
