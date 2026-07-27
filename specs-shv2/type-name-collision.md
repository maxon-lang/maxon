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

## The COMPILED name of a generic instantiation lives in the RESERVED namespace

A generic instantiation has no source-level name of its own, so the compiler builds one: `Box with
String` becomes **`Box_String`**, the base name joined to each argument's name. Every per-type symbol
the backend emits is derived from that string — `__destruct_Box_String`, `__layout_Box_String` — and a
declared `type Box_String` derives *its* symbols from the identical string. **There was one namespace,
and two producers wrote into it with nothing comparing them.**

The consequence is not a resolution ambiguity, which the author might at least see: both claimants are
installed as functions of the same name, and the last one linked wins.

- **A LEAK.** `type Box_String` with two `String` fields, plus a `Box with String` anywhere in the
  program: the instance's one-field cascade services both, the struct's second `String` is never
  released, and the program exits **101** with a clean build and not one diagnostic.
- **Worse, a SILENT SUCCESS.** When the two layouts happen to agree, the surviving destructor is
  *plausible* for the type it was never written for — a `Holder`-field struct destroyed through
  `__str_decref` — and the program returns the right answer. Nothing is ever reported, and the
  type confusion is invisible until a field moves.

**The rule: the two halves of that namespace are DISJOINT, so there is nothing to compare.** `__` is
already reserved — a declaration whose name starts with it is `E2051` — so when the string an
instantiation would compile to is already a declared `type` / `enum` / `union`, the instantiation is
minted **behind that prefix instead**: `Box with String` beside a `type Box_String` compiles to
`__Box_String`, and its cascade is `__destruct___Box_String`. Both types exist, both run, and neither
can reach the other's symbols.

The prefix is applied **on contest only** — a name nothing else claims is spelled bare, exactly as it
always was — and it is applied where the name is BUILT rather than at a later check, because the
compiled name is baked into emitted code (a scope-exit drop calls `__destruct_<mangled>` directly).

Two properties make one re-mint enough, and they are different properties:

- **A minted name is free of the DECLARED half** because the mint re-probes until it is. That is a loop,
  not the prefix, and it would hold for any prefix; on a program that *compiles* it runs once, because a
  declared `__Box_String` is E2051 and the build is already failing.
- **A minted name is free of every OTHER INSTANCE's** because of the prefix, and nothing probes for it: a
  compiled name always BEGINS with its base name, and no base may start with `__`, so no *bare* compiled
  name ever does. Two minted names are therefore equal only when the bare names behind them are — which
  is the instantiation pair below, reported over the FINAL names. A prefix the language did **not**
  reserve would break exactly this half in silence: `XBox_String` is a name `XBox with String` compiles
  to on its own, and a legal program would earn a spurious E3006.

Rejecting the contest instead is what shv2 used to do, and it was wrong in a way that reached programs
nobody would call unusual: an `[Foo, Foo]` array literal interns `Array with Foo` without naming it, so
a program whose only crime was declaring `type Array_Foo` — a perfectly legal type name — was refused
with an error naming an instantiation that appears **nowhere in its source**. The element type did not
even have to match: the sweep cannot see a local's type, so a `[Bar, Bar]` literal interns
`Array with Foo` too, for every declared aggregate in the file.

⚠ **A `typealias` claims nothing, and neither does an `interface`.** An alias mints no symbol of its
own — a generic instance's methods are emitted under its BASE's name — so `typealias Box_Integer = Box
with Integer`, whose alias name is exactly what the instance it names compiles to, is legal and never
displaces anything. An `interface`'s only emitted artifact is `__witness_<conformer>_<interface>`,
whose head is the *conformer*, so it shares no symbol with an instance either. Only the declarations
that mint `__destruct_<name>` — `type`, `enum`, `union` — can contest.

## Two INSTANTIATIONS that compile to one name are still E3006

A prefix cures a contest with a declaration, because there is a declaration to move out of the way.
Two instantiations that build the same string have no such asymmetry, and the join makes it possible on
its own: `_` is a legal character in a type name, so the join is **not injective** — `Pair with
(Box_Int, Str)` and `Pair with (Box, Int_Str)` both compile to `Pair_Box_Int_Str`. One
`__destruct_Pair_Box_Int_Str` survives and each pair's fields are released through the OTHER pair's
per-field callees. Measured on the compiler before this rule: build exit 0, no diagnostic, **SIGSEGV**.

**That is E3006**, reported at the `typealias` that names the later of the two — the line the author
wrote last, the same choice every collision in this file makes. The claimant that SETTLES a name is the
first one, and a rejected claimant never displaces it, so a THIRD instantiation of one compiled name is
reported against the same incumbent the second was, at its own line, rather than against a claimant
that was itself refused.

Only a top-level `typealias` records a source anchor, so an instantiation NESTED inside another
(`Wrap with (Pair with …)`) has none: the report falls back to the other claimant's alias, and when
neither has one it is whole-program (`line == 0`, the anchorless form E3001 uses). Blaming the
enclosing alias instead would name a line whose own instantiation is fine.

It is decided in the FRONT END, over the interned instantiations, **not** at symbol-emission time: a
diagnostic raised where the symbol is minted has no user span to land on, and would blame a file the
author never opened.

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


<!-- test: instantiation-compiles-onto-declared-type -->
The LEAK, now legal. `Box with String` would compile to `Box_String`, and so does the `type Box_String`
below — `installGenericInstanceDestructors` and `installStructDestructors` each emitted a
`__destruct_Box_String` and the later install won. The struct's SECOND `String` was then never
released: the build exited 0 with no diagnostic whatever and the program exited **101**, the leak-check
code. Under the reserved prefix the instance is `__Box_String` instead, so the two cascades are
different functions; both objects are built, both are dropped, and each answers for itself.
```maxon
typealias Num = int(0 to 100)

type Box uses T
	export var v as T
	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	export function tag() returns Num
		return 1
	end 'tag'
end 'Box'

typealias SBox = Box with String

type Box_String
	export var a as String
	export var b as String
	export static function make() returns Self
		return Self{a: "x", b: "y"}
	end 'make'
	export function tag() returns Num
		return 2
	end 'tag'
end 'Box_String'

function main() returns ExitCode
	let s = SBox.create("hello")
	let t = Box_String.make()
	return s.tag() + t.tag()
end 'main'
```
```exitcode
3
```


<!-- test: instantiation-compiles-onto-declared-type-matching-layout -->
The SILENT SUCCESS, which was the more dangerous half. The two claimants have the SAME layout — one
pointer field — so the surviving cascade was *plausible* for the type it was never written for: the
struct's `Holder` box was released through the instance's `__str_decref`, which lands on a refcount
header that is really there. The program returned the right answer and leaked nothing, nothing was
reported, and nothing would be until a field moved. Disjointness does not look at layouts, so this
shape and the leaking one are cured identically — and the leak check still runs, so a cascade servicing
the wrong type would show up here as an exit **101** rather than as a plausible answer.
```maxon
typealias Num = int(0 to 100)

type Holder
	export var s as String
	export static function make() returns Self
		return Self{s: "held"}
	end 'make'
end 'Holder'

type Box uses T
	export var v as T
	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	export function tag() returns Num
		return 3
	end 'tag'
end 'Box'

typealias SBox = Box with String

type Box_String
	export var h as Holder
	export static function make() returns Self
		return Self{h: Holder.make()}
	end 'make'
	export function tag() returns Num
		return 4
	end 'tag'
end 'Box_String'

function main() returns ExitCode
	let s = SBox.create("hello")
	let t = Box_String.make()
	return s.tag() + t.tag()
end 'main'
```
```exitcode
7
```


<!-- test: error.two-instantiations-compile-to-one-name -->
No declared type is involved at all: the base name and the arguments are joined with `_`, and `_` is a
legal character in a type name, so the join is **not injective**. `Pair with (Box_Int, Str)` and `Pair
with (Box, Int_Str)` both compile to `Pair_Box_Int_Str`, one `__destruct_Pair_Box_Int_Str` survives,
and each pair's two fields are released through the OTHER pair's per-field callees. Measured on the
compiler before this rule: build exit 0, no diagnostic, **SIGSEGV**. It is reported at the `typealias`
that names the later instantiation, for the reason the whole file reports at the later declaration.
```maxon
type Str
	export var s as String
	export static function make() returns Self
		return Self{s: "a"}
	end 'make'
end 'Str'

type Box
	export var s as String
	export static function make() returns Self
		return Self{s: "b"}
	end 'make'
end 'Box'

type Box_Int
	export var s as String
	export var t as String
	export var u as String
	export static function make() returns Self
		return Self{s: "c", t: "cc", u: "ccc"}
	end 'make'
end 'Box_Int'

type Int_Str
	export var s as String
	export var t as String
	export var u as String
	export static function make() returns Self
		return Self{s: "d", t: "dd", u: "ddd"}
	end 'make'
end 'Int_Str'

type Pair uses A, B
	export var first as A
	export var second as B
	export static function create(first A, second B) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'Pair'

typealias P1 = Pair with (Box_Int, Str)
typealias P2 = Pair with (Box, Int_Str)

function main() returns ExitCode
	let a = P1.create(Box_Int.make(), second: Str.make())
	let b = P2.create(Box.make(), second: Int_Str.make())
	return 4
end 'main'
```
```maxoncstderr
error E3006: <fragment>:43:11: duplicate definition of 'Pair_Box_Int_Str' — the generic instantiations `Pair with (Box_Int, Str)` and `Pair with (Box, Int_Str)` compile to that same name
```


<!-- test: alias-named-like-its-own-compiled-name -->
The guard against overreach. A `typealias` claims no compiled name — a generic instance's methods are
emitted under its BASE's name, and nothing is named after the alias — so an alias spelled exactly like
what the instance it names compiles to is legal, and the rule must leave it alone. Only the three
keywords that MINT `__destruct_<name>` claim (`type`, `enum`, `union`) — the roster the section above
states and the two tests below pin.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Box'

typealias Box_Integer = Box with Integer

function main() returns ExitCode
	let b = Box_Integer.create(7)
	return b.get()
end 'main'
```
```exitcode
7
```


<!-- test: error.nested-instantiation-reported-at-the-aliased-one -->
Only a top-level `typealias X = Base with Args` records a source anchor; a NESTED instantiation is
interned as it is resolved and has none of its own. When the newcomer is the nested one the report
falls back to the INCUMBENT's alias — the line that names the other half of the pair — rather than at
the enclosing `Wrap` alias, whose own instantiation is fine.
```maxon
typealias Small = int(0 to 100)

type Box
	export var v as Small
end 'Box'

type Str
	export var v as Small
end 'Str'

type Box_Int
	export var v as Small
end 'Box_Int'

type Int_Str
	export var v as Small
end 'Int_Str'

type Pair uses X, Y
	export var first as X
	export var second as Y
end 'Pair'

type Wrap uses Z
	export var only as Z
end 'Wrap'

typealias P1 = Pair with (Box_Int, Str)
typealias W1 = Wrap with (Pair with (Box, Int_Str))

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:29:11: duplicate definition of 'Pair_Box_Int_Str' — the generic instantiations `Pair with (Box_Int, Str)` and `Pair with (Box, Int_Str)` compile to that same name
```


<!-- test: error.nested-instantiations-compile-to-one-name -->
The corner where NEITHER claimant is named by a `typealias`: both colliding instantiations are nested,
inside two DIFFERENT outer generics, so the outer pair does not collide and nothing carries an anchor.
The collision is real and is still reported — whole-program, the anchorless form E3001 uses — naming
both instantiations, because pointing at either enclosing alias would blame a line that is correct.
```maxon
typealias Small = int(0 to 100)

type Box
	export var v as Small
end 'Box'

type Str
	export var v as Small
end 'Str'

type Box_Int
	export var v as Small
end 'Box_Int'

type Int_Str
	export var v as Small
end 'Int_Str'

type Pair uses X, Y
	export var first as X
	export var second as Y
end 'Pair'

type Wrap uses Z
	export var only as Z
end 'Wrap'

type Hold uses Z
	export var only as Z
end 'Hold'

typealias W1 = Wrap with (Pair with (Box_Int, Str))
typealias H1 = Hold with (Pair with (Box, Int_Str))

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: duplicate definition of 'Pair_Box_Int_Str' — the generic instantiations `Pair with (Box_Int, Str)` and `Pair with (Box, Int_Str)` compile to that same name
```


<!-- test: error.three-instantiations-compile-to-one-name -->
The THIRD claimant, which is a different question from the second: each newcomer is measured against
the declaration that SETTLED the name, never against the one immediately before it, so a rejected
claimant does not become the incumbent. Both `Q2` and `Q3` are reported, each at its own line and each
naming `Q1` — `Pair_A_B_C_D` splits three ways because `_` is a legal name character.
```maxon
typealias Small = int(0 to 100)

type A
	export var v as Small
end 'A'

type B_C_D
	export var v as Small
end 'B_C_D'

type A_B
	export var v as Small
end 'A_B'

type C_D
	export var v as Small
end 'C_D'

type A_B_C
	export var v as Small
end 'A_B_C'

type D
	export var v as Small
end 'D'

type Pair uses X, Y
	export var first as X
	export var second as Y
end 'Pair'

typealias Q1 = Pair with (A_B_C, D)
typealias Q2 = Pair with (A_B, C_D)
typealias Q3 = Pair with (A, B_C_D)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:34:11: duplicate definition of 'Pair_A_B_C_D' — the generic instantiations `Pair with (A_B_C, D)` and `Pair with (A_B, C_D)` compile to that same name
error E3006: <fragment>:35:11: duplicate definition of 'Pair_A_B_C_D' — the generic instantiations `Pair with (A_B_C, D)` and `Pair with (A, B_C_D)` compile to that same name
```


<!-- test: error.two-instantiations-compile-to-one-name-a-declaration-also-holds -->
Both mechanisms at once, and the pair check has to survive the prefix. `type Pair_Box_Int_Str` is
declared, so BOTH instantiations are moved into the reserved space — and they land on the *same*
reserved name, because the prefix is a function of the string and the string is what was ambiguous.
The declaration is legal and stays legal; the pair still collides and is still reported, at the later
`typealias`. The name in the message is the name they actually compile to, `__`-prefix and all: saying
`Pair_Box_Int_Str` would name the declared struct, which is not what collided.
```maxon
typealias Small = int(0 to 100)

type Str
	export var v as Small
end 'Str'

type Box
	export var v as Small
end 'Box'

type Box_Int
	export var v as Small
end 'Box_Int'

type Int_Str
	export var v as Small
end 'Int_Str'

type Pair_Box_Int_Str
	export var v as Small
end 'Pair_Box_Int_Str'

type Pair uses X, Y
	export var first as X
	export var second as Y
end 'Pair'

typealias P1 = Pair with (Box_Int, Str)
typealias P2 = Pair with (Box, Int_Str)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:30:11: duplicate definition of '__Pair_Box_Int_Str' — the generic instantiations `Pair with (Box_Int, Str)` and `Pair with (Box, Int_Str)` compile to that same name
```


<!-- test: crossfile-instantiation-compiles-onto-declared-type -->
The compiled namespace is whole-program, like the declared one: the instantiation is written in
`a.maxon` and the contesting declaration is in `b.maxon`, and neither file names the other. That is why
the prefix is decided over the whole-program declaration sweep rather than per file — nothing `a.maxon`
can see tells it that `Box_String` is taken, and nothing `b.maxon` can see tells it that an
instantiation wants the name.
```maxon
// --- file: a.maxon
typealias Num = int(0 to 100)

export type Box uses T
	export var v as T
	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	export function tag() returns Num
		return 7
	end 'tag'
end 'Box'

export typealias SBox = Box with String

// --- file: b.maxon
typealias Num = int(0 to 100)

export type Box_String
	export var a as String
	export static function make() returns Self
		return Self{a: "x"}
	end 'make'
	export function tag() returns Num
		return 8
	end 'tag'
end 'Box_String'

// --- file: main.maxon
function main() returns ExitCode
	let s = SBox.create("hello")
	let t = Box_String.make()
	return s.tag() + t.tag()
end 'main'
```
```exitcode
15
```


<!-- test: array-literal-instance-onto-declared-type -->
The shape that made this a defect rather than a corner. Nothing here writes a generic instantiation at
all: `[Foo.create(1), Foo.create(2)]` is an array literal, and the declaration sweep interns
`Array with Foo` behind it because that is the only way the parser can name the literal's type. So a
program whose author wrote one perfectly ordinary type name — `Array_Foo` — was refused with an error
naming `Array with Foo`, an instantiation that appears **nowhere in its source**. The bootstrap accepts
it and returns 9, because its instance was `__Array_Foo` all along.
```maxon
typealias Num = int(0 to 100)

type Foo
	export var n as Num

	export static function create(v Num) returns Self
		return Self{n: v}
	end 'create'
end 'Foo'

type Array_Foo
	export var tag as Num

	export static function create(v Num) returns Self
		return Self{tag: v}
	end 'create'
end 'Array_Foo'

function main() returns ExitCode
	let xs = [Foo.create(1), Foo.create(2)]
	let m = Array_Foo.create(7)

	return (xs.count() + m.tag) as ExitCode
end 'main'
```
```exitcode
9
```


<!-- test: array-literal-managed-element-instance-onto-declared-type -->
The same shape with a MANAGED element, which is what reaches the destructor machinery: `Foo` owns a
`String`, so the array's elements are released through a real per-element walk rather than freed
wholesale. The declared `Array_Foo` is dropped in the same scope. If the two ever shared a symbol the
leak check would say so — this case exits 9 or it exits 101, and there is no third answer.
```maxon
typealias Num = int(0 to 100)

type Foo
	export var s as String

	export static function create(v String) returns Self
		return Self{s: v}
	end 'create'
end 'Foo'

type Array_Foo
	export var tag as Num

	export static function create(v Num) returns Self
		return Self{tag: v}
	end 'create'
end 'Array_Foo'

function main() returns ExitCode
	let xs = [Foo.create("a"), Foo.create("b")]
	let m = Array_Foo.create(7)

	return (xs.count() + m.tag) as ExitCode
end 'main'
```
```exitcode
9
```


<!-- test: array-literal-of-unrelated-type-onto-declared-type -->
The sharpest one: the literal is of `Bar`, and `Array with Foo` is interned anyway. A local's type is
invisible to the token sweep, so a `[<identifier>…]` literal anywhere in a file over-interns
`Array with T` for **every** declared struct and union in the program — the parser then reads back
whichever one the first element resolves to, and the rest are registry entries nothing ever emits. A
rule that rejected a declaration contest therefore rejected `type Array_Foo` on account of a literal
that has nothing to do with `Foo`. Under disjointness the over-interning is harmless, which is why it
stays: an unreferenced instance mints no symbol at all.
```maxon
typealias Num = int(0 to 100)

type Bar
	export var n as Num

	export static function create(v Num) returns Self
		return Self{n: v}
	end 'create'
end 'Bar'

type Foo
	export var n as Num

	export static function create(v Num) returns Self
		return Self{n: v}
	end 'create'
end 'Foo'

type Array_Foo
	export var tag as Num

	export static function create(v Num) returns Self
		return Self{tag: v}
	end 'create'
end 'Array_Foo'

function main() returns ExitCode
	let xs = [Bar.create(1), Bar.create(2)]
	let m = Array_Foo.create(7)

	return (xs.count() + m.tag) as ExitCode
end 'main'
```
```exitcode
9
```


<!-- test: declared-type-owning-a-string-named-like-an-array-instance -->
The declared type is the one that OWNS the `String` here, and that is a different question from the
case above: a managed struct puts `__destruct_Array_Foo` into the needed-destructor set, and that set is
keyed by NAME, so the name once pulled the base-less `Array with Foo` instance in behind it and the
compiler died — `panic ProgramSignatures.baseLayoutOf: base struct 'Array' is not declared`, a stack
trace with no diagnostic at all.

⚠ **THIS CASE PINS THE BUILTIN GUARD, NOT DISJOINTNESS**, and the difference is measured rather than
argued: the guard (`genericInstanceHasStringField` / `genericInstanceHasManagedField` /
`addNestedInstanceDestructors` returning early for an `Array`/`Set` instance) landed before the reserved
prefix did, and with `claimsCompiledTypeName` forced to answer *no* — every other declaration-contest
case in this file failing — **this one still passes.** Disjointness is the OUTER defence: the instance
is `__Array_Foo`, so the struct's cascade names only itself and the guard is never reached. Both are
real and neither is a substitute for the other; a case that credited the wrong one would go on passing
after the one doing the work was deleted.
```maxon
typealias Num = int(0 to 100)

type Foo
	export var n as Num

	export static function create(v Num) returns Self
		return Self{n: v}
	end 'create'
end 'Foo'

type Array_Foo
	export var s as String
	export var tag as Num

	export static function create(v Num) returns Self
		return Self{s: "held", tag: v}
	end 'create'
end 'Array_Foo'

function main() returns ExitCode
	let xs = [Foo.create(1), Foo.create(2)]
	let m = Array_Foo.create(7)

	return (xs.count() + m.tag) as ExitCode
end 'main'
```
```exitcode
9
```


<!-- test: declared-type-and-instance-both-cascade -->
Both cascades are REAL and both run. `type Box_String` owns three `String`s and the `Box with String`
instance owns one, so a build that emitted a single `__destruct_Box_String` for the two of them either
leaks two strings or frees one box twice depending on which install won. Two instances are constructed
so the instance cascade runs more than once, and the leak check fails the case on exit **101** if a
single `String` survives it.
```maxon
typealias Num = int(0 to 100)

type Box uses T
	export var v as T
	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	export function tag() returns Num
		return 5
	end 'tag'
end 'Box'

typealias SBox = Box with String

type Box_String
	export var a as String
	export var b as String
	export var c as String
	export static function make() returns Self
		return Self{a: "aa", b: "bb", c: "cc"}
	end 'make'
	export function tag() returns Num
		return 6
	end 'tag'
end 'Box_String'

function main() returns ExitCode
	let s = SBox.create("hello")
	let t = Box_String.make()
	let u = SBox.create("world")
	return s.tag() + t.tag() + u.tag()
end 'main'
```
```exitcode
16
```


<!-- test: nested-contest-mints-onto-a-second-contested-name -->
Both contests at once, one nested inside the other, and it is the case that pins why the probe never
asks the INSTANCE set. `Box with String` is contested and becomes `__Box_String`, so the enclosing
`Box with (Box with String)` compiles to `Box___Box_String` — a bare name that already CONTAINS the
prefix — and `type Box___Box_String` contests that in turn, sending it to `__Box___Box_String`. Four
managed cascades, four distinct symbols, every object dropped exactly once (a shared symbol is a leak at
**101** or a wild free). It works because a compiled name always BEGINS with its base name and no base
may start with `__`, so a minted name can never be some *other* instance's bare one — the property that
lets `claimsCompiledTypeName` probe declarations only.
```maxon
typealias Num = int(0 to 100)

type Box uses T
	export var v as T
	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	export function tag() returns Num
		return 1
	end 'tag'
end 'Box'

typealias Inner = Box with String
typealias Outer = Box with (Box with String)

type Box_String
	export var a as String
	export static function make() returns Self
		return Self{a: "aa"}
	end 'make'
	export function tag() returns Num
		return 2
	end 'tag'
end 'Box_String'

type Box___Box_String
	export var a as String
	export var b as String
	export static function make() returns Self
		return Self{a: "bb", b: "cc"}
	end 'make'
	export function tag() returns Num
		return 4
	end 'tag'
end 'Box___Box_String'

function main() returns ExitCode
	let i = Inner.create("hello")
	let o = Outer.create(Inner.create("world"))
	let d = Box_String.make()
	let e = Box___Box_String.make()
	return i.tag() + o.tag() + d.tag() + e.tag()
end 'main'
```
```exitcode
8
```


<!-- test: union-named-like-a-compiled-instance-name -->
The `union` half of the claimant roster, and the ONLY shape that makes it load-bearing: a managed-payload
union mints `__destruct_Box_String` from its bare name exactly as a `type` does, so without it in the
roster the instance would keep the bare name and the two cascades would be one symbol — a TAG-dispatching
union destructor reading the instance's `String` pointer as its tag, or a flat field cascade reading the
union's i64 tag as a `String`. Neither is a leak; both are a wild free. Both objects are built and both
are dropped, and the leak check fails the case on **101** if either cascade services the wrong one.
```maxon
typealias Num = int(0 to 100)

type Held
	export var s as String
	export var n as Num
	export static function create(n Num) returns Self
		return Self{s: "held", n: n}
	end 'create'
end 'Held'

type Box uses T
	export var v as T
	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	export function tag() returns Num
		return 5
	end 'tag'
end 'Box'

typealias SBox = Box with String

union Box_String
	silent
	held(h Held)
end 'Box_String'

function main() returns ExitCode
	let s = SBox.create("hello")
	let m = Box_String.held(Held.create(4))
	match m 'k'
		silent then return s.tag()
		held(h) then return s.tag() + h.n
	end 'k'
end 'main'
```
```exitcode
9
```


<!-- test: enum-named-like-a-compiled-instance-name -->
The `enum` half, written as a PAYLOAD-FREE enum on purpose: it mints no per-type symbol at all, so
nothing about this program would break if the roster dropped it — which is precisely why it needs a case
rather than an argument. The roster is one predicate over one registry (`enum` and `union` share it), and
this pins that the payload-free spelling reaches the same answer as the managed one above rather than
some third path. The bootstrap accepts it and returns 9.
```maxon
typealias Num = int(0 to 100)

type Box uses T
	export var v as T
	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	export function tag() returns Num
		return 5
	end 'tag'
end 'Box'

typealias SBox = Box with String

enum Box_String
	circle
	square
end 'Box_String'

function main() returns ExitCode
	let s = SBox.create("hello")
	let e = Box_String.square
	match e 'k'
		circle then return s.tag()
		square then return s.tag() + 4
	end 'k'
end 'main'
```
```exitcode
9
```


<!-- test: interface-named-like-a-compiled-instance-name -->
The other side of the roster: an `interface` is NOT a claimant, and it used to be one. Its only emitted
artifact is `__witness_<conformer>_<interface>`, whose head is the CONFORMER, so it shares no symbol with
an instance and there is nothing to move out of the way — the instance keeps the bare `Box_String` and
the interface keeps its name. shv2 rejected this program with E3006 before the roster shrank; the
bootstrap has always built it and returned 9.
```maxon
typealias Num = int(0 to 100)

type Box uses T
	export var v as T
	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	export function tag() returns Num
		return 5
	end 'tag'
end 'Box'

typealias SBox = Box with String

interface Box_String
	function draw() returns Num
end 'Box_String'

type Pen implements Box_String
	export var ink as Num
	export static function create(ink Num) returns Self
		return Self{ink: ink}
	end 'create'
	export function draw() returns Num
		return self.ink
	end 'draw'
end 'Pen'

function main() returns ExitCode
	let s = SBox.create("hello")
	let p = Pen.create(4)
	return s.tag() + p.draw()
end 'main'
```
```exitcode
9
```


<!-- test: error.declaring-a-reserved-instance-name -->
The guard on the reserved space itself, which is the whole reason the prefix is safe to mint into. A
declaration may not take a `__` name, so the space a contested instance is moved into is one no source
declaration can ever reach — and if that stopped being true, the disjointness above would quietly stop
being disjointness. The message is byte-identical to the bootstrap's.
```maxon
typealias Num = int(0 to 100)

type __Array_Foo
	export var n as Num

	export static function create(v Num) returns Self
		return Self{n: v}
	end 'create'
end '__Array_Foo'

function main() returns ExitCode
	let b = __Array_Foo.create(3)

	return b.n as ExitCode
end 'main'
```
```maxoncstderr
error E2051: <fragment>:4:6: identifier '__Array_Foo' is reserved: declarations starting with '__' are reserved for compiler internals
```
