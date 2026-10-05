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
Without a check that two of them have not claimed the same name, a program that declares one twice
compiles, and a **fixed resolution cascade** picks the winner rather than the author.

That is a wrong ANSWER, and its shape depends on which declaration the cascade happens to reach first:

- `type Box` and then `typealias Box = int(0 to 10)` would compile **completely clean**. The alias
  would be discarded without a word, and every `Box` in the program would mean the struct.
- The same two declarations in the opposite order would compile to the same thing, so the alias's range
  would never apply and a cast into it would be reported as `E3009: Cannot cast from int to struct` — a
  consequence of the collision, describing nothing the author could act on.
- Across files it is worse than order-dependent, it is unconditional: a `type Box` in **any** file
  would beat a `typealias Box` in the reader's **own** file, because the struct registry is consulted bare
  and first while the alias registry is the only one that is file-scoped.

**The rule: a type name is declared exactly once in any one scope.** A collision is reported at the **later**
declaration — its own file, its own name token — naming the kind the incumbent declared, which is the
half the author cannot see. The incumbent is the line that is fine.

Two typealiases of the same name are covered by their own code, **E3061**; everything else is
**E3006**, the code a name declared twice already carries for functions and for top-level
`let`/`var` (`specs/duplicate-functions.md`, `specs/static-variables.md` — where a `let` and a `var`
sharing one name collide *regardless of which keyword introduced them*, the same kind-independence
this rule states for types).

## Which pairs are in ONE scope

Two declarations of one name are a collision when some qualified or bare spelling could not tell them
apart. Every `typealias` form — ranged, function, generic-instance, tuple — is scoped by its file and
directory, so the scope of a declaration is decided by where it sits and how visible it is:

| the pair | outcome |
|---|---|
| two declarations in ONE file, any kinds | E3061 for two aliases, E3006 otherwise |
| two NAMEABLE declarations (`export`, `public`, `module`, or any nominal) in two files of ONE directory | E3061 for two aliases, E3006 otherwise — both would be spelled `dir.Name` |
| a FILE-PRIVATE alias and any declaration in another file | legal — the alias is visible in its own file alone |
| two aliases in two directories, of any forms | legal — each is `dir.Name` |
| a nominal declaration and a nameable alias in two directories | legal |
| two nominal declarations (`type` / `enum` / `union` / `interface`) of an author program | E3006, wherever they sit |
| an author declaration and a standard-library one | legal — each is named, `dir.Name` and `stdlib.Name` |

What a legal pair costs is paid at the READ: a file that declares the name means its own; a file that
declares none and sees two is E3063 and qualifies (`specs/typealias-collision.md`). Two declarations of one
name that denote different KINDS are kept apart the same way, so a file whose only `Handler` is
`typealias Handler = int(0 to 10)` casts `5 as Handler` against its own alias, whatever another directory's
`Handler` is.

A same-file duplicate is E3061 whether or not other files declare the name too: a newcomer is judged
against every declaration of its own file, and a cross-file pair that is legal never stands in for the
same-file one.

## The COMPILED name of a generic instantiation lives in the RESERVED namespace

A generic instantiation has no source-level name of its own, so the compiler builds one: `Box with
String` becomes **`Box_String`**, the base name joined to each argument's name. Every per-type symbol
the backend emits is derived from that string — `__destruct_Box_String`, `__layout_Box_String` — and a
declared `type Box_String` derives *its* symbols from the identical string. **Left as one namespace, two
producers would write into it with nothing comparing them.**

The consequence would not be a resolution ambiguity, which the author might at least see: both claimants
would be installed as functions of the same name, and the last one linked would win.

- **A LEAK.** `type Box_String` with two `String` fields, plus a `Box with String` anywhere in the
  program: the instance's one-field cascade would service both, the struct's second `String` would never
  be released, and the program would exit **101** with a clean build and not one diagnostic.
- **Worse, a SILENT SUCCESS.** When the two layouts happen to agree, the surviving destructor is
  *plausible* for the type it was never written for — a `Holder`-field struct destroyed through
  `__str_decref` — and the program returns the right answer. Nothing would ever be reported, and the
  type confusion would be invisible until a field moved.

**The rule: the two halves of that namespace are DISJOINT, so there is nothing to compare.** `__` is
already reserved — a declaration whose name starts with it is `E2051` — so when the string an
instantiation would compile to is already a declared `type` / `enum` / `union`, the instantiation is
minted **behind that prefix instead**: `Box with String` beside a `type Box_String` compiles to
`__Box_String`, and its cascade is `__destruct___Box_String`. Both types exist, both run, and neither
can reach the other's symbols.

The prefix is applied **on contest only** — a name nothing else claims is spelled bare — and it is applied where the name is BUILT rather than at a later check, because the
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

Rejecting the contest instead would be wrong in a way that reaches programs nobody would call unusual:
an `[Foo, Foo]` array literal interns `Array with Foo` without naming it, so a program whose only crime
is declaring `type Array_Foo` — a perfectly legal type name — would be refused with an error naming an
instantiation that appears **nowhere in its source**. The element type does not even have to match: the sweep cannot see a local's type, so a `[Bar, Bar]` literal interns
`Array with Foo` too, for every declared aggregate in the file.

⚠ **A `typealias` claims nothing, and neither does an `interface`.** An alias mints no symbol of its
own — a generic instance's methods are emitted under its BASE's name — so `typealias Box_Integer = Box
with Integer`, whose alias name is exactly what the instance it names compiles to, is legal and never
displaces anything. An `interface`'s only emitted artifact is `__witness_<conformer>.<interface>`,
whose head is the *conformer*, so it shares no symbol with an instance either. Only the declarations
that mint `__destruct_<name>` — `type`, `enum`, `union` — can contest.

That claim is about the DECLARED half of the namespace. The separator it is written with, `.`, carries
a second guarantee: a `.` occurs in no source identifier and therefore in no compiled instance name, so
a witness label cannot be spelled by a DIFFERENT conformer/interface pair either. An `interface` is
exactly where that matters — see *The WITNESS-TABLE label joins TWO names* below.

## Two INSTANTIATIONS that compile to one name are still E3006

A prefix cures a contest with a declaration, because there is a declaration to move out of the way.
Two instantiations that build the same string have no such asymmetry, and the join makes it possible on
its own: `_` is a legal character in a type name, so the join is **not injective** — `Pair with
(Box_Int, Str)` and `Pair with (Box, Int_Str)` both compile to `Pair_Box_Int_Str`. One
`__destruct_Pair_Box_Int_Str` survives and each pair's fields are released through the OTHER pair's
per-field callees. Without this rule: build exit 0, no diagnostic, **SIGSEGV**.

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

## The WITNESS-TABLE label joins TWO names, so it joins them with a character no name can hold

A type that conforms to an interface gets a `.rdata` **witness table** — the dictionary a constrained
generic body dispatches through — and that table is labelled from the pair that identifies it: the
CONFORMER and the INTERFACE. Joined with `_`, the label is **not injective**, for exactly the reason
the instantiation join above is not: `_` is a legal character in a type name, so `(A_B, C)` and
`(A, B_C)` both spell `__witness_A_B_C`.

**The consequence is worse than the instantiation pair's, because there is no second claimant to
diagnose.** Two instantiations are two interned declarations a front-end check can compare. A witness
table is minted during LOWERING, from a memo keyed on the LABEL, so the second pair does not *collide*
with the first — it silently *becomes* it: the mint finds its label already emitted, hands it back, and
the dispatch site takes the address of a table built for the other pair. Every method slot then
resolves to the other conformer's implementation, with the other conformer's `self`. Under a `_` join
the two-pair program below builds with exit 0 and no diagnostic, and returns **33** where the answer is
**43** — both dispatches reach `A_B.idc`.

Which pair wins is not which one is declared first; it is **which dispatch is lowered first**, so the
same two declarations return 33 or 44 depending only on the order two calls appear in an expression.

**The rule: the label joins with `.`** — `(A_B, C)` is `__witness_A_B.C` and `(A, B_C)` is
`__witness_A.B_C` — which makes it injective by CHARACTER CLASS rather than by an algorithm. A `.`
cannot occur in either half: the lexer admits only `[A-Za-z0-9_]` inside an identifier, and every name
that reaches the join is a source identifier, a compiled instance name (a `_`-join of source
identifiers behind an optional `__`), or one of the compiler's own conformer names (`int`, `String`,
`float`, `bool`). So the join has exactly one split and two pairs can never spell one label.

The alternatives are worse in ways worth writing down, because a later reader will reach for them:

- **Escaping by DOUBLING the separator does not even work.** With a one-character separator, a run of
  five underscores between two components admits three valid splits, so the decoder would need told
  the split it cannot derive.
- **A length prefix works, but it makes injectivity a property of a DECODING ALGORITHM** that every
  later reader has to get right. The character class makes it a property of the LEXER — the same
  construction the compiled-instance namespace two sections above rests on, where `__` is safe purely
  because `E2051` bars a declaration from it.

It is the same `.` a method's compiled name is joined with (`Point.create`), for the same reason, but
it is a SEPARATE decision and stays a separate constant: a method's separator is what the AUTHOR
writes at a call site, and a witness label is a name no source can spell.

## Tests

<!-- test: error.type-then-typealias -->
The silent case. `Box` is declared as a `type` and then as a ranged `typealias`; without the rule the
alias is discarded with no diagnostic at all and this program compiles and returns 7.
```maxon
typealias Small = int(0 to 100)

type Box
	export var v as Small
	static function make() returns Self
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
declaration here — never at the alias, which is fine on its own.
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


<!-- test: crossfile-type-and-file-private-typealias-coexist -->
Across files, with a FILE-PRIVATE alias — and this is the direction file scoping rescues. The struct
registry is bare, so if it were consulted first `b.maxon`'s own `typealias Box` would be unreachable
from `b.maxon` itself: the cast below would be rejected with `E3009: Cannot cast from int to struct`,
against a `type` declared in a file it never names, and an `E3006` would blame the pair as a duplicate.

**Neither declaration is a duplicate of anything.** A non-exported `typealias` is file-local, so
`a.maxon`'s `export type Box` cannot see it and it cannot see the struct — the two names never meet.
`b.maxon`'s cast resolves against `b.maxon`'s own alias, which is the reader-file rule the ranged
registry applies, applied to the CASCADE that picks which registry answers. The
exported pair that genuinely does collide is the next test.

`main.maxon` names `Box` too, and means the STRUCT — a's declaration is the only one it can see — while
`b.maxon` means its own alias by the same spelling in the same program. That is the whole claim, and it
is why main constructs one rather than merely calling into `b.maxon`: a case where nobody outside
`a.maxon` names `Box` would be answered by the compiler without ever deciding which declaration it meant.

⚠ `b.maxon`'s door returns `ExitCode` and its private `Box` is read inside `useIt`: a file-private type
may not be named in a signature another file calls (E3167).
```maxon
// --- file: a.maxon
export typealias Small = int(0 to 100)

export type Box
	export var v as Small

	export static function create(v Small) returns Box
		return Self{v: v}
	end 'create'
end 'Box'

// --- file: b.maxon
typealias Box = int(0 to 10)

function useIt() returns Box
	return 5
end 'useIt'

export function fromB() returns ExitCode
	return useIt() as ExitCode
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	let boxed = Box.create(3)

	return fromB() + (boxed.v as ExitCode)
end 'main'
```
```exitcode
8
```


<!-- test: error.crossfile-type-and-exported-typealias -->
The boundary the previous test does not cross: `b.maxon`'s alias is `export`ed, and it sits in the same
directory as `a.maxon`'s `type`, so both would be spelled `export.Score` — one name in one scope, and the
pair is a duplicate. File scoping rescues a pair that never meets; it does not merge two that do.
```maxon
// --- file: a.maxon
typealias Small = int(0 to 100)

export type Score
	export var v as Small
end 'Score'

// --- file: b.maxon
export typealias Score = int(0 to 10)

export function useIt() returns ExitCode
	return 5 as Score
end 'useIt'

// --- file: main.maxon
function main() returns ExitCode
	return useIt()
end 'main'
```
```maxoncstderr
error E3009: <fragment>:13:11: Cannot cast from int to struct
error E3006: <fragment>:10:18: duplicate definition of 'Score' — already declared as `type Score`
```


<!-- test: cross-directory-type-and-exported-typealias-coexist -->
A `type` and an exported `typealias` of one name in two DIRECTORIES coexist: each is named by its directory, so
no reader has to guess which one a spelling means.
```maxon
// --- file: a/score.maxon
export type Score
	export let v as ExitCode

	export static function make() returns Score
		return Score{v: 40}
	end 'make'
end 'Score'

// --- file: b/score.maxon
export typealias Score = int(0 to 10)

// --- file: app/main.maxon
function total(s a.Score, n b.Score) returns ExitCode
	return s.v + (n as ExitCode)
end 'total'

function main() returns ExitCode
	return total(a.Score.make(), n: 2)
end 'main'
```
```exitcode
42
```

<!-- test: a-type-and-another-directorys-generic-instance-alias-of-one-name-coexist -->
`a/` names `Pair with (Integer, Integer)` as `IntPair` while `b/` declares `type IntPair`; each file means its own.
```maxon
// --- file: a/a.maxon
typealias Integer = int(i64.min to i64.max)

type Pair uses A, B
	export var first as A
	export var second as B

	static function create(a A, b B) returns Self
		return Self{first: a, second: b}
	end 'create'
end 'Pair'

typealias IntPair = Pair with (Integer, Integer)

export function runA()
	let p = IntPair.create(3, b: 4)
	print("a {p.first} {p.second}\n")
end 'runA'

// --- file: b/b.maxon
typealias Integer = int(i64.min to i64.max)

type IntPair
	export var a as Integer
	export var b as Integer

	static function create(n Integer) returns Self
		return Self{a: n, b: n}
	end 'create'
end 'IntPair'

export function runB()
	let p = IntPair.create(7)
	print("b {p.a} {p.b}\n")
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	runA()
	runB()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a 3 4
b 7 7
```

<!-- test: error.duplicate-type-same-file -->
Two `type` declarations of one name in one file. Undiagnosed, the second layout would simply replace
the first in the registry, so a field the first declared would vanish.
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

<!-- test: error.duplicate-type-across-directories -->
The same two `type` declarations in two directories: a nominal name is whole-program, so a directory does not make room for it.
```maxon
// --- file: a/a.maxon
typealias Small = int(0 to 100)

export type Box
	export var v as Small
end 'Box'

// --- file: b/b.maxon
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
error E3006: b/<fragment>:12:13: duplicate definition of 'Box' — already declared as `type Box`
```

<!-- test: error.duplicate-private-type-across-directories -->
Two FILE-PRIVATE `type Box` declarations in two directories still collide: a nominal name is whole-program however visible it is.
```maxon
// --- file: a/a.maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

let G = Box.create()

export function runA()
	print("a {G.n}\n")
end 'runA'

// --- file: b/b.maxon
type Box
	export var label as String

	static function create() returns Self
		return Self{label: "b"}
	end 'create'
end 'Box'

let H = Box.create()

export function runB()
	print("b {H.label}\n")
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	runA()
	runB()
	return 0
end 'main'
```
```maxoncstderr
error E3006: b/<fragment>:20:6: duplicate definition of 'Box' — already declared as `type Box`
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
typealias Handler = function() returns Integer
typealias Handler = function() returns Integer

function main() returns ExitCode
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3061: <fragment>:3:11: Duplicate typealias 'Handler'
```


<!-- test: error.duplicate-alias-same-file-while-another-file-declares-it -->
The carve-out is per PAIR, and the pair that matters is the newcomer against the declaration that
most recently claimed the name — **never** against the first one. `a.maxon` declares `L` and `b.maxon`
declares it twice: `b.maxon`'s second `L` is a same-file duplicate and stays E3061, exactly as it
would if `a.maxon` did not exist. Against a registry that kept the FIRST declaration instead, each of
`b.maxon`'s two would be compared with `a.maxon`'s, found to be the legal cross-file case, and accepted —
the duplicate would compile in silence.
```maxon
// --- file: a.maxon
export typealias L = int(0 to 10)

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
The same hole for the GENERIC-INSTANCE form. The file-local carve-out must not hand a generic alias
a way to stay unchecked for duplication.
```maxon
// --- file: base.maxon

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
error E3061: <fragment>:15:11: Duplicate typealias 'G'
error E3062: <fragment>:10:11: unused typealias: 'G'
```


<!-- test: crossfile-ranged-and-function-alias-coexist -->
Two directories, one exported name, two alias FORMS — legal, because each is named by its own
directory (`alpha.Handler`, `beta.Handler`) and the forms do not decide it. Each declaring file means its
own: `alpha/a.maxon`'s `useA` returns its `int` alias and `beta/b.maxon`'s `useB` takes its function alias.
A bare, whole-program function-alias registry would answer for `alpha/a.maxon` too, rejecting its own
`Handler` with `Cannot cast from int to function` — a diagnostic in a file whose only `Handler` is an `int`
alias, naming a declaration it never mentions.
```maxon
// --- file: alpha/a.maxon
export typealias Handler = int(0 to 10)

export function useA() returns Handler
	return 5
end 'useA'

// --- file: beta/b.maxon
export typealias Handler = function() returns Integer

export function useB(h Handler) returns ExitCode
	return h() as ExitCode
end 'useB'

export typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
function zero() returns Integer
	return 0
end 'zero'

function main() returns ExitCode
	return useA() + useB(zero)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
5
```


<!-- test: crossfile-generic-alias-same-name-still-legal -->
The guard against overreach: two files each declaring one FILE-PRIVATE generic-instance alias is the
file-local case, the same as two ranged aliases, and it is legal.
```maxon
// --- file: base.maxon

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


<!-- test: crossfile-generic-alias-same-name-with-an-overloaded-parameter -->
The legal pair above with ONE thing added — the callee is OVERLOADED — and a program nothing is wrong
with, which a repair that names two files would falsely refuse. `a.maxon` and `b.maxon` each declare a
generic-instance alias `Thing` denoting a different instance, which the case above establishes is
allowed; `b.maxon` holds a value `a.maxon` made and hands it to `a.maxon`'s `take`, whose parameter is
declared with `a.maxon`'s `Thing`. An overload candidate's parameter type is the whole-program sweep's,
repaired at the call — and a repair that GATED on the candidate's declaring file while RESOLVING the
instance in the CALLING file would score `take`'s `Bx with Small` parameter as `b.maxon`'s
`Bx with String`. No candidate would fit, the overload would go unsettled, and the call's result would
be typed from the single return type the index keeps per NAME — the OTHER overload's:
**`E3005: Cannot return 'String' from function declared to return 'int'`**, naming a type this call
never meant.
```maxon
// --- file: base.maxon

export type Bx uses T
	export var value as T

	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	export function get() returns T
		return self.value
	end 'get'
end 'Bx'

// --- file: a.maxon
export typealias Small = int(0 to 100)
export typealias Thing = Bx with Small

export function makeA() returns Thing
	return Thing.create(7)
end 'makeA'

export function take(t Thing) returns ExitCode
	return t.get()
end 'take'

// --- file: d.maxon
export typealias Tag = int(0 to 50)

export function take(n Tag) returns String
	return "n{n}"
end 'take'

// --- file: b.maxon
typealias Thing = Bx with String

export function fromB() returns ExitCode
	let own = Thing.create("ab")
	print("{own.get()}\n")

	let t = makeA()

	return take(t)
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	print("{take(3)}\n")

	return fromB()
end 'main'
```
```exitcode
7
```
```stdout
n3
ab
```


<!-- test: crossfile-generic-alias-same-name-with-an-overloaded-parameter-on-an-int-argument -->
The same defect with an `int` type argument on both sides, because resolving an instance in the wrong
file is not a managed-type effect and a case that only ever showed it through a `String` would let a
repair that special-cases one look complete. Nothing here is a `String`: `a.maxon` means
`Bx with Small` and `b.maxon` means `Bx with Wide`, two ranged aliases over the same primitive. A
parameter scored in the calling file's scope would still fit nothing, and the same borrowed return type
would come back.
```maxon
// --- file: base.maxon

export type Bx uses T
	export var value as T

	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	export function get() returns T
		return self.value
	end 'get'
end 'Bx'

// --- file: a.maxon
export typealias Small = int(0 to 100)
export typealias Thing = Bx with Small

export function makeA() returns Thing
	return Thing.create(7)
end 'makeA'

export function take(t Thing) returns ExitCode
	return t.get()
end 'take'

// --- file: d.maxon
export typealias Tag = int(0 to 50)

export function take(n Tag) returns String
	return "n{n}"
end 'take'

// --- file: b.maxon
typealias Wide = int(0 to 1000)
typealias Thing = Bx with Wide

export function fromB() returns ExitCode
	let own = Thing.create(4)
	print("{own.get()}\n")

	let t = makeA()

	return take(t)
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	print("{take(3)}\n")

	return fromB()
end 'main'
```
```exitcode
7
```
```stdout
n3
4
```


<!-- test: crossfile-generic-alias-same-name-with-an-unoverloaded-parameter -->
The BOUND on that repair, and the condition it isolates. This is the same disagreement — two files,
one alias name, two different instances, a value crossing from the file that made it into the file
that means the other one — with the overload and nothing else removed. A callee's recorded RETURN type
is re-scoped into its DECLARING file before any file is parsed, so a crossing that goes through a
declared signature is right here and may not become wrong: `take` is resolved by name, its
parameter is checked against the signature it actually has, and 7 comes back. What the overload set
adds is a SWEPT parameter type read at the call site, in front of a repair that has to name one file
throughout; a repair that reached further than that would turn this case red.
```maxon
// --- file: base.maxon

export type Bx uses T
	export var value as T

	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	export function get() returns T
		return self.value
	end 'get'
end 'Bx'

// --- file: a.maxon
export typealias Small = int(0 to 100)
export typealias Thing = Bx with Small

export function makeA() returns Thing
	return Thing.create(7)
end 'makeA'

export function take(t Thing) returns ExitCode
	return t.get()
end 'take'

// --- file: b.maxon
typealias Thing = Bx with String

export function fromB() returns ExitCode
	let own = Thing.create("ab")
	print("{own.get()}\n")

	let t = makeA()

	return take(t)
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	return fromB()
end 'main'
```
```exitcode
7
```
```stdout
ab
```


<!-- test: error.crossfile-generic-and-ranged-alias-the-unused-ranged-one-is-reported -->
One name, two alias FORMS in two files, and only one of them is spelled. `a.maxon` names its own
generic-instance `Thing`; `b.maxon`'s ranged `Thing` is written nowhere, so E3062 is what that
declaration has earned. The cross-file credit a generic-instance alias gets is keyed on the bare
NAME, and a ranged alias sharing that name is a different declaration in a different file that the
spelling never reached.
```maxon
// --- file: base.maxon

export type Bx uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Bx'

// --- file: a.maxon
typealias Small = int(0 to 100)
typealias Thing = Bx with Small

export function fromA() returns ExitCode
	let b = Thing.create(7)
	return b.get()
end 'fromA'

// --- file: b.maxon
typealias Thing = int(0 to 10)

export function fromB() returns ExitCode
	return 2
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	return fromA() + fromB()
end 'main'
```
```maxoncstderr
error E3062: <fragment>:24:11: unused typealias: 'Thing'
```


<!-- test: error.crossfile-generic-and-ranged-alias-the-unused-generic-one-is-reported -->
The same pair the other way round, and it fails the same way. `b.maxon` spells `Thing` meaning its
OWN ranged alias; `a.maxon`'s generic-instance `Thing` is written nowhere. A spelling credits the
declaration it resolves to and no other, so the file that wrote the name decides what the name meant
there — the generic declaration it never referred to is unused.
```maxon
// --- file: base.maxon

export type Bx uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Bx'

// --- file: a.maxon
typealias Small = int(0 to 100)
typealias Thing = Bx with Small

export function fromA() returns ExitCode
	return 7
end 'fromA'

// --- file: b.maxon
typealias Thing = int(0 to 10)
typealias Held = Bx with Thing

export function fromB() returns ExitCode
	let b = Held.create(2)
	return b.get()
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	return fromA() + fromB()
end 'main'
```
```maxoncstderr
error E3062: <fragment>:16:11: unused typealias: 'Thing'
```


<!-- test: error.crossfile-generic-and-ranged-alias-a-third-file-spells-it -->
In both pairs above the file that spells `Thing` also declares one of the two forms, so the reader's
own declaration answers. Here `c.maxon` declares neither, and both declarations are file-private, so
no declaration of `Thing` is visible to it. A non-exported alias is unreachable from another file:
the spelling in `c.maxon` is refused with the hidden-name refusal, it credits neither declaration,
and both are reported unused.
```maxon
// --- file: base.maxon

export type Bx uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Bx'

// --- file: a.maxon
typealias Small = int(0 to 100)
typealias Thing = Bx with Small

export function fromA() returns ExitCode
	return 1
end 'fromA'

// --- file: b.maxon
typealias Thing = int(0 to 10)

export function fromB() returns ExitCode
	return 2
end 'fromB'

// --- file: c.maxon
export function fromC() returns ExitCode
	let t = Thing.create(4)
	return t.get()
end 'fromC'

// --- file: main.maxon
function main() returns ExitCode
	return fromA() + fromB() + fromC()
end 'main'
```
```maxoncstderr
error E3062: <fragment>:16:11: unused typealias: 'Thing'
error E3062: <fragment>:23:11: unused typealias: 'Thing'
error E3008: <fragment>:31:10: typealias 'Thing' is not exported
```


<!-- test: instantiation-compiles-onto-declared-type -->
The LEAK shape, legal. Without the reserved prefix `Box with String` would compile to `Box_String`, and
so does the `type Box_String` below — `installGenericInstanceDestructors` and `installStructDestructors`
would each emit a `__destruct_Box_String` and the later install would win. The struct's SECOND `String`
would then never be released: the build exits 0 with no diagnostic whatever and the program exits
**101**, the leak-check code. Under the reserved prefix the instance is `__Box_String` instead, so the two cascades are
different functions; both objects are built, both are dropped, and each answers for itself.
```maxon
typealias Num = int(0 to 100)

type Box uses T
	export var v as T
	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	function tag() returns Num
		return 1
	end 'tag'
end 'Box'

typealias SBox = Box with String

type Box_String
	export var a as String
	export var b as String
	static function make() returns Self
		return Self{a: "x", b: "y"}
	end 'make'
	function tag() returns Num
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
The SILENT SUCCESS, which is the more dangerous half. The two claimants have the SAME layout — one
pointer field — so a surviving shared cascade would be *plausible* for the type it was never written for:
the struct's `Holder` box would be released through the instance's `__str_decref`, which lands on a
refcount header that is really there. The program would return the right answer and leak nothing, and
nothing would be reported until a field moved. Disjointness does not look at layouts, so this
shape and the leaking one are cured identically — and the leak check still runs, so a cascade servicing
the wrong type would show up here as an exit **101** rather than as a plausible answer.
```maxon
typealias Num = int(0 to 100)

type Holder
	export var s as String
	static function make() returns Self
		return Self{s: "held"}
	end 'make'
end 'Holder'

type Box uses T
	export var v as T
	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	function tag() returns Num
		return 3
	end 'tag'
end 'Box'

typealias SBox = Box with String

type Box_String
	export var h as Holder
	static function make() returns Self
		return Self{h: Holder.make()}
	end 'make'
	function tag() returns Num
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
and each pair's two fields are released through the OTHER pair's per-field callees. Without the
rule: build exit 0, no diagnostic, **SIGSEGV**. It is reported at the `typealias`
that names the later instantiation, for the reason the whole file reports at the later declaration.
```maxon
type Str
	export var s as String
	static function make() returns Self
		return Self{s: "a"}
	end 'make'
end 'Str'

type Box
	export var s as String
	static function make() returns Self
		return Self{s: "b"}
	end 'make'
end 'Box'

type Box_Int
	export var s as String
	export var t as String
	export var u as String
	static function make() returns Self
		return Self{s: "c", t: "cc", u: "ccc"}
	end 'make'
end 'Box_Int'

type Int_Str
	export var s as String
	export var t as String
	export var u as String
	static function make() returns Self
		return Self{s: "d", t: "dd", u: "ddd"}
	end 'make'
end 'Int_Str'

type Pair uses A, B
	export var first as A
	export var second as B
	static function create(first A, second B) returns Self
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
	static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	function get() returns T
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
error E3062: <fragment>:30:11: unused typealias: 'W1'
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
error E3062: <fragment>:33:11: unused typealias: 'W1'
error E3062: <fragment>:34:11: unused typealias: 'H1'
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
error E3062: <fragment>:33:11: unused typealias: 'Q1'
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
error E3062: <fragment>:29:11: unused typealias: 'P1'
```


<!-- test: crossfile-instantiation-compiles-onto-declared-type -->
The compiled namespace is whole-program, like the declared one: the instantiation is written in
`a.maxon` and the contesting declaration is in `b.maxon`, and neither file names the other. That is why
the prefix is decided over the whole-program declaration sweep rather than per file — nothing `a.maxon`
can see tells it that `Box_String` is taken, and nothing `b.maxon` can see tells it that an
instantiation wants the name.
```maxon
// --- file: a.maxon
export typealias Num = int(0 to 100)

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
The shape that makes this a defect rather than a corner. Nothing here writes a generic instantiation at
all: `[Foo.create(1), Foo.create(2)]` is an array literal, and the declaration sweep interns
`Array with Foo` behind it because that is the only way the parser can name the literal's type. So
refusing the contest would refuse a program whose author wrote one perfectly ordinary type name —
`Array_Foo` — with an error naming `Array with Foo`, an instantiation that appears **nowhere in its
source**. The program builds and returns 9.
```maxon
typealias Num = int(0 to 100)

type Foo
	export var n as Num

	static function create(v Num) returns Self
		return Self{n: v}
	end 'create'
end 'Foo'

type Array_Foo
	export var tag as Num

	static function create(v Num) returns Self
		return Self{tag: v}
	end 'create'
end 'Array_Foo'

function main() returns ExitCode
	let xs = [Foo.create(1), Foo.create(2)]
	let m = Array_Foo.create(7)

	return ((xs.count() as Num) + m.tag) as ExitCode
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

	static function create(v String) returns Self
		return Self{s: v}
	end 'create'
end 'Foo'

type Array_Foo
	export var tag as Num

	static function create(v Num) returns Self
		return Self{tag: v}
	end 'create'
end 'Array_Foo'

function main() returns ExitCode
	let xs = [Foo.create("a"), Foo.create("b")]
	let m = Array_Foo.create(7)

	return ((xs.count() as Num) + m.tag) as ExitCode
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

	static function create(v Num) returns Self
		return Self{n: v}
	end 'create'
end 'Bar'

type Foo
	export var n as Num

	static function create(v Num) returns Self
		return Self{n: v}
	end 'create'
end 'Foo'

type Array_Foo
	export var tag as Num

	static function create(v Num) returns Self
		return Self{tag: v}
	end 'create'
end 'Array_Foo'

function main() returns ExitCode
	let xs = [Bar.create(1), Bar.create(2)]
	let m = Array_Foo.create(7)

	return ((xs.count() as Num) + m.tag) as ExitCode
end 'main'
```
```exitcode
9
```


<!-- test: declared-type-owning-a-string-named-like-an-array-instance -->
The declared type is the one that OWNS the `String` here, and that is a different question from the
case above: a managed struct puts `__destruct_Array_Foo` into the needed-destructor set, and that set is
keyed by NAME, so the name can pull the base-less `Array with Foo` instance in behind it and kill the
compiler — `panic ProgramSignatures.baseLayoutOf: base struct 'Array' is not declared`, a stack trace
with no diagnostic at all.

⚠ **THIS CASE PINS THE BUILTIN GUARD, NOT DISJOINTNESS**: the guard (`genericInstanceHasStringField` /
`genericInstanceHasManagedField` / `addNestedInstanceDestructors` returning early for an `Array`/`Set`
instance) is independent of the reserved prefix, and with `claimsCompiledTypeName` forced to answer
*no* — every other declaration-contest case in this file failing — **this one still passes.** Disjointness is the OUTER defence: the instance
is `__Array_Foo`, so the struct's cascade names only itself and the guard is never reached. Both are
real and neither is a substitute for the other; a case that credited the wrong one would go on passing
after the one doing the work was deleted.
```maxon
typealias Num = int(0 to 100)

type Foo
	export var n as Num

	static function create(v Num) returns Self
		return Self{n: v}
	end 'create'
end 'Foo'

type Array_Foo
	export var s as String
	export var tag as Num

	static function create(v Num) returns Self
		return Self{s: "held", tag: v}
	end 'create'
end 'Array_Foo'

function main() returns ExitCode
	let xs = [Foo.create(1), Foo.create(2)]
	let m = Array_Foo.create(7)

	return ((xs.count() as Num) + m.tag) as ExitCode
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
	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	function tag() returns Num
		return 5
	end 'tag'
end 'Box'

typealias SBox = Box with String

type Box_String
	export var a as String
	export var b as String
	export var c as String
	static function make() returns Self
		return Self{a: "aa", b: "bb", c: "cc"}
	end 'make'
	function tag() returns Num
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
	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	function tag() returns Num
		return 1
	end 'tag'
end 'Box'

typealias Inner = Box with String
typealias Outer = Box with (Box with String)

type Box_String
	export var a as String
	static function make() returns Self
		return Self{a: "aa"}
	end 'make'
	function tag() returns Num
		return 2
	end 'tag'
end 'Box_String'

type Box___Box_String
	export var a as String
	export var b as String
	static function make() returns Self
		return Self{a: "bb", b: "cc"}
	end 'make'
	function tag() returns Num
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
	static function create(n Num) returns Self
		return Self{s: "held", n: n}
	end 'create'
end 'Held'

type Box uses T
	export var v as T
	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	function tag() returns Num
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
some third path. The program builds and returns 9.
```maxon
typealias Num = int(0 to 100)

type Box uses T
	export var v as T
	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	function tag() returns Num
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
The other side of the roster: an `interface` is NOT a claimant. Its only emitted artifact is
`__witness_<conformer>.<interface>`, whose head is the CONFORMER, so it shares no symbol with an instance
and there is nothing to move out of the way — the instance keeps the bare `Box_String` and the interface
keeps its name. The program builds and returns 9.

The `.` separator makes the disjointness this case pins stronger: a `.` occurs in no compiled instance
name, so `__witness_Pen.Box_String` is unreachable from the instance side by the character class as well
as by its head.
```maxon
typealias Num = int(0 to 100)

type Box uses T
	export var v as T
	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	function tag() returns Num
		return 5
	end 'tag'
end 'Box'

typealias SBox = Box with String

interface Box_String
	function draw() returns Num
end 'Box_String'

type Pen implements Box_String
	export var ink as Num
	static function create(ink Num) returns Self
		return Self{ink: ink}
	end 'create'
	function draw() returns Num
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
being disjointness.
```maxon
typealias Num = int(0 to 100)

type __Array_Foo
	export var n as Num

	static function create(v Num) returns Self
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


<!-- test: witness-label-two-pairs-that-underscore-join-alike -->
The witness half of the same non-injective join, and the one with no diagnostic to fall back on.
Under a `_` join `(A_B, C)` and `(A, B_C)` both spell `__witness_A_B_C`, so the second pair's mint finds
the first pair's table already emitted and hands it back: `HoldB.go` dispatches through `HoldC`'s table
and reaches `A_B.idc` — build exit 0, no diagnostic, **33** — where `3 + 4 * 10` is 43.
```maxon
typealias Integer = int(0 to u32.max)

interface C
	function idc() returns Integer
end 'C'

interface B_C
	function idb() returns Integer
end 'B_C'

type A_B implements C
	export var x as Integer
	static function create() returns Self
		return Self{ x: 0 }
	end 'create'
	function idc() returns Integer
		return 3
	end 'idc'
end 'A_B'

type A implements B_C
	export var x as Integer
	static function create() returns Self
		return Self{ x: 0 }
	end 'create'
	function idb() returns Integer
		return 4
	end 'idb'
end 'A'

type HoldC uses T where T is C
	export var v as T
	static function create(v T) returns Self
		return Self{ v: v }
	end 'create'
	function go() returns Integer
		return self.v.idc()
	end 'go'
end 'HoldC'

type HoldB uses T where T is B_C
	export var v as T
	static function create(v T) returns Self
		return Self{ v: v }
	end 'create'
	function go() returns Integer
		return self.v.idb()
	end 'go'
end 'HoldB'

typealias HC = HoldC with A_B
typealias HB = HoldB with A

function main() returns ExitCode
	let h1 = HC.create(A_B.create())
	let h2 = HB.create(A.create())
	return h1.go() + h2.go() * 10
end 'main'
```
```exitcode
43
```


<!-- test: witness-label-the-other-pair-settles-the-label-first -->
The same two pairs with the OTHER one settling the shared label, which is what shows a `_` join's answer
is not merely wrong but arbitrary. The declarations are in the opposite order AND the two dispatches are
evaluated in the opposite order — and it is the DISPATCH order that decides, because the table is minted
where a call materializes its witness argument, not where a type is declared. Under a `_` join this
program answers **44**, both dispatches reaching `A.idb`, against 33 for the identical program with the
two calls swapped. The answer is 43 either way.
```maxon
typealias Integer = int(0 to u32.max)

interface B_C
	function idb() returns Integer
end 'B_C'

type A implements B_C
	export var x as Integer
	static function create() returns Self
		return Self{ x: 0 }
	end 'create'
	function idb() returns Integer
		return 4
	end 'idb'
end 'A'

interface C
	function idc() returns Integer
end 'C'

type A_B implements C
	export var x as Integer
	static function create() returns Self
		return Self{ x: 0 }
	end 'create'
	function idc() returns Integer
		return 3
	end 'idc'
end 'A_B'

type HoldB uses T where T is B_C
	export var v as T
	static function create(v T) returns Self
		return Self{ v: v }
	end 'create'
	function go() returns Integer
		return self.v.idb()
	end 'go'
end 'HoldB'

type HoldC uses T where T is C
	export var v as T
	static function create(v T) returns Self
		return Self{ v: v }
	end 'create'
	function go() returns Integer
		return self.v.idc()
	end 'go'
end 'HoldC'

typealias HB = HoldB with A
typealias HC = HoldC with A_B

function main() returns ExitCode
	let h1 = HC.create(A_B.create())
	let h2 = HB.create(A.create())
	return h2.go() * 10 + h1.go()
end 'main'
```
```exitcode
43
```


<!-- test: witness-label-three-pairs-that-underscore-join-alike -->
The witness twin of `error.three-instantiations-compile-to-one-name`: `_` splits `A_B_C_D` three ways, so
`(A_B_C, D)`, `(A_B, C_D)` and `(A, B_C_D)` would all spell one label. Unlike the instantiation trio —
which is reported twice and never compiles — under a `_` join this one builds clean and returns **111**:
three distinct interfaces, three distinct conformers, one table, every dispatch landing on `A_B_C.d`. Each of the three
must reach its own conformer, which the digits of 123 read off individually.
```maxon
typealias Small = int(0 to 100)

interface D
	function d() returns Small
end 'D'

interface C_D
	function cd() returns Small
end 'C_D'

interface B_C_D
	function bcd() returns Small
end 'B_C_D'

type A_B_C implements D
	export var v as Small
	static function create() returns Self
		return Self{ v: 0 }
	end 'create'
	function d() returns Small
		return 1
	end 'd'
end 'A_B_C'

type A_B implements C_D
	export var v as Small
	static function create() returns Self
		return Self{ v: 0 }
	end 'create'
	function cd() returns Small
		return 2
	end 'cd'
end 'A_B'

type A implements B_C_D
	export var v as Small
	static function create() returns Self
		return Self{ v: 0 }
	end 'create'
	function bcd() returns Small
		return 3
	end 'bcd'
end 'A'

type HoldD uses T where T is D
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function go() returns Small
		return self.item.d()
	end 'go'
end 'HoldD'

type HoldCD uses T where T is C_D
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function go() returns Small
		return self.item.cd()
	end 'go'
end 'HoldCD'

type HoldBCD uses T where T is B_C_D
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function go() returns Small
		return self.item.bcd()
	end 'go'
end 'HoldBCD'

typealias H1 = HoldD with A_B_C
typealias H2 = HoldCD with A_B
typealias H3 = HoldBCD with A

function main() returns ExitCode
	let h1 = H1.create(A_B_C.create())
	let h2 = H2.create(A_B.create())
	let h3 = H3.create(A.create())
	return h1.go() * 100 + h2.go() * 10 + h3.go()
end 'main'
```
```exitcode
123
```


<!-- test: witness-label-one-conformer-two-interfaces-and-a-third-pair -->
ONE conformer with TWO interfaces, which is the pair the head-is-the-conformer argument alone does not
separate. `A implements B, B_C` needs two tables — `__witness_A.B` and `__witness_A.B_C` — and they stay
distinct under either separator, because their heads agree and only the tails differ. The third pair is
what breaks: under a `_` join `(A_B, C)` spells the SAME `__witness_A_B_C` as `(A, B_C)`, so `HoldC.go`
dispatches `A_B.c` through `A`'s table and reaches `A.bc` — **144**, where the answer is 142 — the first
digit correct, which is why the two-interface half has to be in the program to prove it is not at risk.
```maxon
typealias Small = int(0 to 100)

interface B
	function b() returns Small
end 'B'

interface B_C
	function bc() returns Small
end 'B_C'

interface C
	function c() returns Small
end 'C'

type A implements B, B_C
	export var v as Small
	static function create() returns Self
		return Self{ v: 0 }
	end 'create'
	function b() returns Small
		return 1
	end 'b'
	function bc() returns Small
		return 4
	end 'bc'
end 'A'

type A_B implements C
	export var v as Small
	static function create() returns Self
		return Self{ v: 0 }
	end 'create'
	function c() returns Small
		return 2
	end 'c'
end 'A_B'

type HoldB uses T where T is B
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function go() returns Small
		return self.item.b()
	end 'go'
end 'HoldB'

type HoldBC uses T where T is B_C
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function go() returns Small
		return self.item.bc()
	end 'go'
end 'HoldBC'

type HoldC uses T where T is C
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function go() returns Small
		return self.item.c()
	end 'go'
end 'HoldC'

typealias HB = HoldB with A
typealias HBC = HoldBC with A
typealias HC = HoldC with A_B

function main() returns ExitCode
	let hb = HB.create(A.create())
	let hbc = HBC.create(A.create())
	let hc = HC.create(A_B.create())
	return hb.go() * 100 + hbc.go() * 10 + hc.go()
end 'main'
```
```exitcode
142
```


<!-- test: crossfile-return-type-is-the-declaring-files-meaning -->
⭐⭐ **A CALLEE'S RETURN TYPE IS A SLOT OF THE FILE THAT DECLARED IT, NEVER OF THE FILE CALLING IT** —
the shape neither of the coexistence cases above reaches, and the one where getting it wrong is SILENT.
`a.maxon` means a file-private `int(0 to 5)` by `Widget` and `b.maxon` an `export type` of the same name;
the two coexist because the alias is visible in `a.maxon` alone, where it wins. `main.maxon` declares NEITHER, and the
RECORD `fromB` hands it has to be read as `b.maxon`'s `Widget` — a name folded whole-program, or resolved
in the caller's scope, would give `main.maxon` the integer meaning and dereference a record through it.

Read with the caller's file it would be **`E3005: Cannot return 'struct' from function declared to return
'int'`**, and through the field read below it would dereference the integer as a record — **exit 139,
clean compile, no diagnostic.**

⚠ A file-private type may not be named in a signature another file calls (E3167), so the file whose meaning is private reads its own name INSIDE the body and hands the boundary an `ExitCode`. The contested pair is unchanged: one name, two files, two meanings.
```maxon
// --- file: a.maxon
typealias Widget = int(0 to 5)

export function fromA() returns ExitCode
	let w = 3 as Widget

	return w as ExitCode
end 'fromA'

// --- file: b.maxon
export typealias Slot = int(0 to 100)

export type Widget
	export var value as Slot

	export static function create(value Slot) returns Widget
		return Self{value: value}
	end 'create'
end 'Widget'

export function fromB() returns Widget
	return Widget.create(9)
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	let boxed = fromB()

	return fromA() + (boxed.value as ExitCode)
end 'main'
```
```exitcode
12
```


<!-- test: crossfile-return-type-through-two-alias-forms -->
The same rule through the arm that has no nominal declaration in it at all — a RANGED alias in one file
against a TUPLE alias in another. It is a different code path (a tuple alias resolves to the tuple's own
`structRef`, not to a declared `type`), so narrowing only the nominal side of the cascade leaves it open:
the tuple `fromB` returns would be read as `main.maxon`'s meaning of `Pair`, and a caller that folded the
name would bind an integer to a two-slot destructuring.

⚠ A file-private type may not be named in a signature another file calls (E3167), so the file whose meaning is private reads its own name INSIDE the body and hands the boundary an `ExitCode`. The contested pair is unchanged: one name, two files, two meanings.
```maxon
// --- file: a.maxon
typealias Pair = int(0 to 5)

export function fromA() returns ExitCode
	let n = 4 as Pair

	return n as ExitCode
end 'fromA'

// --- file: b.maxon
export typealias Pair = (ExitCode, ExitCode)

export function fromB() returns Pair
	return (7, 9)
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	let p = fromB()

	return fromA() + p.0 + p.1
end 'main'
```
```exitcode
20
```


<!-- test: error.crossfile-enum-member-is-not-reachable-through-an-alias -->
⛔⛔ **A MEMBER ACCESS ON A COEXISTING NAME MUST NOT REACH THE OTHER FILE'S DECLARATION, AND WHEN IT DID
THE RANGE AND THE VALUE CAME FROM DIFFERENT DECLARATIONS.** `a.maxon` means a ranged `int(0 to 5)` by
`Status`; `b.maxon` declares an `enum Status` whose `big` is 200. `Status.big` written inside `a.maxon`
resolved to `b.maxon`'s enum and stored **200** into a slot `a.maxon` declares as `int(0 to 5)` — the
range from one declaration, the value from the other, **compiling clean and exiting 201.**

`a.maxon` means the ALIAS by `Status`, and the only members a ranged alias has are its BOUNDS — so the
access is refused by the rule that already governs `Status.min` / `Status.max`, naming what this file's
declaration actually offers instead of silently reaching the other one.
```maxon
// --- file: a.maxon
typealias Status = int(0 to 5)

export function pick() returns ExitCode
	var s = 1 as Status
	s = Status.big
	return s as ExitCode
end 'pick'

// --- file: b.maxon
export enum Status
	small = 1
	big = 200
end 'Status'

// --- file: main.maxon
function main() returns ExitCode
	return pick()
end 'main'
```
```maxoncstderr
error E2010: <fragment>:7:13: Expected 'min or max' but got 'big'
```


<!-- test: crossfile-enum-member-keeps-its-own-files-value -->
The runnable half of the case above, and the one that asserts the VALUES rather than a diagnostic: each
file keeps its own declaration, so `b.maxon`'s `Status.big` is still 200 and `a.maxon`'s `Status` still
ranges 0 to 5. A cure that reached the enum from `a.maxon` would be caught by the error case; a cure that
lost `b.maxon`'s own enum would be caught here.
```maxon
// --- file: a.maxon
typealias Status = int(0 to 5)

export function clamped() returns ExitCode
	let s = 4 as Status
	return s
end 'clamped'

// --- file: b.maxon
enum Status
	small = 1
	big = 200
end 'Status'

export function widest() returns ExitCode
	return Status.big.rawValue as ExitCode
end 'widest'

// --- file: main.maxon
function main() returns ExitCode
	return widest() - clamped()
end 'main'
```
```exitcode
196
```


<!-- test: crossfile-return-type-that-is-an-enum-in-its-own-file -->
⭐ **A COEXISTING NAME WHOSE DECLARING FILE MEANS AN `enum`, IN A RETURN TYPE.** Nothing in the suite put
this shape in front of the code: an enum and a union are the two kinds that stay a bare `named` through
resolution (`resolveNamedStruct` normalizes a struct, `resolveNamedAlias` the function/generic/tuple forms,
`resolveFloatAliasType` a float alias — none of them touches an enum), so a repair that erases whatever is
still `named` at the crossing erases them too, **even when the reader and the declaring file agree.**
```maxon
// --- file: a.maxon
typealias Level = int(0 to 5)

export function fromA() returns ExitCode
	let n = 3 as Level

	return n as ExitCode
end 'fromA'

// --- file: b.maxon
export enum Level
	low = 1
	high = 9
end 'Level'

export function fromB() returns Level
	return Level.high
end 'fromB'

export function rankOf(l Level) returns ExitCode
	return l.rawValue
end 'rankOf'

// --- file: main.maxon
function main() returns ExitCode
	return fromA() + rankOf(fromB())
end 'main'
```
```exitcode
12
```


<!-- test: crossfile-return-type-that-is-a-boxed-union-in-its-own-file -->
⭐⭐ **THE SILENT ONE: a BOXED union crossing a file boundary as a returned TEMPORARY.** `holds(s String)`
gives the union a heap box, so the caller adopts the result and drops it once. Erase the type at the
crossing and the box is never enrolled — no diagnostic, no output, **exit 139** — which is why this case
returns a value that depends on the payload rather than merely compiling.
```maxon
// --- file: a.maxon
typealias Container = int(0 to 5)

export function fromA() returns ExitCode
	let n = 2 as Container

	return n as ExitCode
end 'fromA'

// --- file: b.maxon
export union Container
	empty
	holds(s String)
end 'Container'

export function makeB() returns Container
	return Container.holds("xyzz")
end 'makeB'

export function widthOf(c Container) returns ExitCode
	match c 'kind'
		empty then return 0
		holds(s) then return s.count() as ExitCode
	end 'kind'
end 'widthOf'

// --- file: main.maxon
function main() returns ExitCode
	return fromA() + widthOf(makeB())
end 'main'
```
```exitcode
6
```


<!-- test: crossfile-float-alias-against-a-nominal-declaration -->
A FLOAT ranged alias in the contest. `resolveFloatAliasType` reaches the ranged registry through
`aliasOf`, which is reader-aware about the RANGE and must be about the KIND too — otherwise `b.maxon`'s
own `returns Level` would be resolved against `a.maxon`'s float alias, and the refusal would land
**inside `b.maxon`, about a declaration its author never saw.**
```maxon
// --- file: a.maxon
typealias Level = float(0.0 to 5.0)

export function scaledA() returns ExitCode
	let v = 2.5 as Level

	return trunc(v * 2.0) as ExitCode
end 'scaledA'

// --- file: b.maxon
export enum Level
	low = 1
	high = 9
end 'Level'

export function fromB() returns Level
	return Level.high
end 'fromB'

export function rankOf(l Level) returns ExitCode
	return l.rawValue
end 'rankOf'

// --- file: main.maxon
function main() returns ExitCode
	return scaledA() + rankOf(fromB())
end 'main'
```
```exitcode
14
```


<!-- test: crossfile-indirect-call-through-a-contested-returning-alias -->
An INDIRECT call through a function-alias value whose return type is a coexisting name. The result's type
is a slot of the file that declared the ALIAS, not of the file making the call — and `c.maxon` below
declares neither `Token` nor `Cb`, so a call scoped by the caller resolved `Token` to `b.maxon`'s struct
and handed back the integer 3 wearing a record's type.
```maxon
// --- file: a.maxon
typealias Token = int(0 to 5)

export typealias Cb = function() returns Token

function three() returns Token
	return 3
end 'three'

export function goA() returns ExitCode
	return callIt(three)
end 'goA'

// --- file: b.maxon
export typealias Slot = int(0 to 100)

export type Token
	export var value as Slot

	export static function create(value Slot) returns Token
		return Self{value: value}
	end 'create'
end 'Token'

export function fromB() returns Token
	return Token.create(9)
end 'fromB'

// --- file: c.maxon
export function callIt(f Cb) returns ExitCode
	return f()
end 'callIt'

// --- file: main.maxon
function main() returns ExitCode
	let boxed = fromB()

	return goA() + (boxed.value as ExitCode)
end 'main'
```
```exitcode
12
```


<!-- test: crossfile-boxed-union-temporary-is-discarded-by-a-stranger -->
⭐⭐ **THE SILENT SHAPE, AND THE ONE THE LOUD UNION CASE ABOVE CANNOT REACH.** `main.maxon` declares
NEITHER claimant and mentions `Container` only in `relay`'s signature — a function nothing calls — so the
boxed union arrives as a DISCARDED owned temporary and every check that would have refused it is bypassed.

The fault this guards is not in `main.maxon` at all: were `a.maxon`'s `return 2 as Container` classified
by the whole-program enum registry, it would find `b.maxon`'s BOXED union, and the return path would emit
**`__mm_retain` on the integer 2** — `x64.movRegImm32 rcx, 2` / `x64.callDirect __mm_retain`, in a function
whose own file has no union in it. **Exit 139, clean compile, no output**, against a control that differs
only in the name.
```maxon
// --- file: a.maxon
typealias Container = int(0 to 5)

export function fromA() returns ExitCode
	return 2 as Container
end 'fromA'

// --- file: b.maxon
typealias CallTally = int(0 to 100)

export union Container
	empty
	holds(s String)
end 'Container'

var fromBCalls = 0 as CallTally

// `main.maxon` DISCARDS this function's result, which is the whole shape this case is about — so the
// callee must have an effect, or the discard is E3064 (`specs/discarded-results.md`) and the
// program never reaches the union it exists to exercise.
export function fromB() returns Container
	fromBCalls = fromBCalls + 1
	return Container.holds("hi")
end 'fromB'

export function valueOf(c Container) returns ExitCode
	match c 'pick'
		empty then return 0
		holds(s) then return 3 if s.equals("hi") else 0
	end 'pick'
end 'valueOf'

// --- file: main.maxon
function relay(c Container) returns ExitCode
	return valueOf(c)
end 'relay'

function main() returns ExitCode
	_ = fromB()

	return 5 + fromA()
end 'main'
```
```exitcode
7
```


<!-- test: crossfile-reassigned-parameter-of-a-contested-name -->
⭐⭐ **THE PURELY LOCAL SHAPE — nothing crosses a file, and that is why the eleven cases above could not
reach it.** Every other coexistence case puts the contested name at a crossing: a return type, a
parameter's declared type, an indirect call. Here `a.maxon` reassigns its OWN by-reference parameter, and
the fault is entirely inside `a.maxon` — the value never leaves it.

A reassigned parameter is a CELL, and the cell asks the memory-management tier whether its content is
managed. A tier with no reading file would answer off `b.maxon`'s declaration: `useA(c Container)`
doing `c = 4` would emit **`__mm_incref` on the literal 4** and `__mm_decref` on a header at address
**2** — exit **139** against a control of 7 — while the panic string two blocks up in the same function
reads *"outside typealias 'Container'"*: the compiler knowing it is a ranged alias while the tier
disagrees, inside one function body.
```maxon
// --- file: a.maxon
typealias Container = int(0 to 5)

function useA(c Container) returns ExitCode
	c = 4

	return c
end 'useA'

export function goA() returns ExitCode
	return useA(1)
end 'goA'

// --- file: b.maxon
union Container
	empty
	holds(s String)
end 'Container'

export function fromB() returns ExitCode
	let c = Container.holds("hi")

	match c 'pick'
		empty then return 0
		holds(s) then return 3 if s.equals("hi") else 0
	end 'pick'
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	return goA() + fromB()
end 'main'
```
```exitcode
7
```


<!-- test: crossfile-reassigned-parameter-against-a-struct-claimant -->
The same local shape with a `type` claimant rather than a union, because the two reach the managed
classifier through different arms of it.
```maxon
// --- file: a.maxon
typealias Widget = int(0 to 5)

function useA(c Widget) returns ExitCode
	c = 4

	return c
end 'useA'

export function goA() returns ExitCode
	return useA(2)
end 'goA'

// --- file: b.maxon
typealias Slot = int(0 to 100)

type Widget
	export var value as Slot

	static function create(value Slot) returns Widget
		return Self{value: value}
	end 'create'
end 'Widget'

export function fromB() returns ExitCode
	return Widget.create(3).value as ExitCode
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	return goA() + fromB()
end 'main'
```
```exitcode
7
```


<!-- test: error.crossfile-a-contested-name-does-not-credit-the-other-declaration -->
⭐ **THE UNUSED-EXPORT AUDIT MUST NOT COUNT ONE FILE'S USES OF ITS OWN ALIAS AS REFERENCES TO ANOTHER
FILE'S EXPORT.** `b.maxon`'s `export union Container` is named by nobody outside `b.maxon`, so it earns
E3092, whatever spelling `a.maxon`'s file-private `typealias Container` shares with it. A reference walk
crediting EVERY tracked declaration wearing the name would let `a.maxon`'s uses of its own alias silently
satisfy the export and **the diagnostic would vanish.** Not memory-unsafe, but it would make this audit's
answer depend on an unrelated file's choice of word.
```maxon
// --- file: a.maxon
typealias Container = int(0 to 5)

export function useA() returns ExitCode
	let c = 4 as Container
	return c
end 'useA'

// --- file: b.maxon
export union Container
	empty
	holds(s String)
end 'Container'

export function fromB() returns ExitCode
	let c = Container.holds("hi")

	match c 'pick'
		empty then return 0
		holds(s) then return 3 if s.equals("hi") else 0
	end 'pick'
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	return useA() + fromB()
end 'main'
```
```maxoncstderr
error E3092: <fragment>:11:14: exported type 'Container' is never referenced outside its declaring file
```


<!-- test: error.crossfile-a-service-throws-a-contested-name -->
<!-- unsupported-targets: wasm32-wasi -->
⭐ **A SERVICE MESSAGE IS A SIGNATURE, AND IT ANSWERS AT THE SAME DOOR.** `divide` is on `Calc`'s message
surface, so whoever spawns a `Calc` has to be able to name what the message takes, returns and throws —
and `b.maxon` writes all three over declarations of its own that no other file can see. Four positions,
four diagnostics, in signature order: the two parameters, the return type, then the throws clause.

⛔ **E3167 STANDS IN FRONT OF E3113 HERE, AND THE E3113 ROAD IS NOT REACHABLE FROM A LEGAL PROGRAM.**
`b.maxon`'s ranged `Fault` names no enum or union, while `a.maxon`'s `enum Fault` is a declaration
`b.maxon` cannot name, so E3113 would refuse the clause even though an `enum Fault` exists in the
program. That shape cannot be rebuilt around E3167 in one directory: an EXPORTED `typealias Fault` beside
an `enum Fault` there is a duplicate definition (E3006), so the throws clause cannot name a visible ranged
`Fault` while the enum exists.
`ServiceCompanions.mintServiceReplyErrorType` asks the same reader and is harmless only while a refusal
holds here; the refusal is E3167's.
```maxon
// --- file: a.maxon
export enum Fault implements Error
	broke
end 'Fault'

// --- file: b.maxon
typealias Fault = int(0 to 10)
typealias Integer = int(i64.min to i64.max)

export type Calc
	var count as Integer
	var seen as Fault

	export static function create() returns Self
		return Self{count: 0, seen: 3}
	end 'create'

	export function divide(n Integer, by Integer) returns Integer throws Fault
		return n + by + self.count + (self.seen as Integer)
	end 'divide'
end 'Calc'

// --- file: main.maxon
function main() returns ExitCode
	let h = spawn Calc.create()
	let v = try await h.divide(10, by: 2) otherwise return 70

	return v as ExitCode
end 'main'
```
```maxoncstderr
error E3167: <fragment>:19:18: exported function 'Calc.divide' names file-private typealias 'Integer' in the type of parameter 'n'
error E3167: <fragment>:19:18: exported function 'Calc.divide' names file-private typealias 'Integer' in the type of parameter 'by'
error E3167: <fragment>:19:18: exported function 'Calc.divide' names file-private typealias 'Integer' in its return type
error E3167: <fragment>:19:18: exported function 'Calc.divide' names file-private typealias 'Fault' in its throws clause
```

<!-- test: a-service-throws-an-enum-qualified-past-another-directorys-alias -->
An exported `typealias Fault` in `calc/` and an `enum Fault` in `faults/` coexist, because each is named by
its directory. `calc/`'s service message writes `throws faults.Fault`, which is the enum, so the reply
carries the enum's case back to the awaiting caller.
```maxon
// --- file: calc/alias.maxon
export typealias Fault = int(0 to 10)

// --- file: faults/fault.maxon
export enum Fault implements Error
	broke
end 'Fault'

// --- file: calc/calc.maxon
export typealias Count = int(i64.min to i64.max)

export type Calc
	var count as Count

	export static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function divide(n Count, by Count) returns Count throws faults.Fault
		if by == 0 'zero'
			throw faults.Fault.broke
		end 'zero'

		return n + by + self.count
	end 'divide'
end 'Calc'

// --- file: main.maxon
function main() returns ExitCode
	let h = spawn Calc.create()
	let v = try await h.divide(10, by: 2) otherwise return 70
	print("{v}\n")

	let w = try await h.divide(1, by: 0) otherwise return 3
	return w as ExitCode
end 'main'
```
```stdout
12
```
```exitcode
3
```

<!-- test: error.a-service-throws-the-alias-its-own-directory-declares -->
<!-- unsupported-targets: wasm32-wasi -->
The same pair, with the message's clause naming `calc.Fault` — the ranged alias. That names no enum or union,
so the clause is refused, whatever `faults/` declares under the same name.
```maxon
// --- file: calc/alias.maxon
export typealias Fault = int(0 to 10)

// --- file: faults/fault.maxon
export enum Fault implements Error
	broke
end 'Fault'

// --- file: calc/calc.maxon
export typealias Count = int(i64.min to i64.max)

export type Calc
	var count as Count

	export static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function divide(n Count, by Count) returns Count throws calc.Fault
		return n + by + self.count
	end 'divide'
end 'Calc'

// --- file: main.maxon
function main() returns ExitCode
	let h = spawn Calc.create()
	let v = try await h.divide(10, by: 2) otherwise return 70
	return v as ExitCode
end 'main'
```
```maxoncstderr
error E3113: calc/<fragment>:20:18: 'throws calc.Fault' names no declared enum or union. A caught error is decoded off the DECLARED clause, so the clause has to name the type whose cases it decodes into
```

<!-- test: error.a-service-throws-a-bare-name-two-directories-declare -->
The same pair, with the clause written bare in a file that declares neither: it sees two `Fault`s and must
say which.
```maxon
// --- file: calc/alias.maxon
export typealias Fault = int(0 to 10)

// --- file: faults/fault.maxon
export enum Fault implements Error
	broke
end 'Fault'

// --- file: calc/calc.maxon
export typealias Count = int(i64.min to i64.max)

export type Calc
	var count as Count

	export static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function divide(n Count, by Count) returns Count throws Fault
		return n + by + self.count
	end 'divide'
end 'Calc'

// --- file: main.maxon
function main() returns ExitCode
	let h = spawn Calc.create()
	let v = try await h.divide(10, by: 2) otherwise return 70
	return v as ExitCode
end 'main'
```
```maxoncstderr
error E3063: calc/<fragment>:20:65: Ambiguous type name 'Fault': more than one visible declaration matches it. Qualify it as one of: calc.Fault, faults.Fault
```

<!-- test: error.a-name-whose-declarations-coexist-in-part-is-blamed-in-path-order -->
Collision is not transitive. Here `d1/a.maxon`'s `enum Box` and `d2/b.maxon`'s `enum Box` collide as two
nominals, `d2/b.maxon` and `d2/c.maxon` collide as two nameable declarations in one directory, and
`d1/a.maxon`'s enum and `d2/c.maxon`'s alias coexist. Which one is refused must not depend on the order
the files arrive in: declarations are admitted path-earliest first, so `d1/a.maxon` is admitted, then
`d2/b.maxon` is refused against it, and `d2/c.maxon`, which collides with nothing admitted, stands. The
files are declared here in the reverse of that order.
```maxon
// --- file: d2/c.maxon
export typealias Box = int(0 to 9)

// --- file: d2/b.maxon
export enum Box
	large
end 'Box'

// --- file: d1/a.maxon
export enum Box
	small
end 'Box'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: d2/<fragment>:6:13: duplicate definition of 'Box' — already declared as `enum Box`
```


<!-- test: error.two-nominal-declarations-in-two-directories-collide -->
Two author NOMINAL declarations of one name collide wherever they sit: a `type`, `enum`, `union` or
`interface` name is whole-program, so a second directory does not make room for it. The refusal lands on
`shapes/box.maxon`'s declaration, because `crates/box.maxon` precedes it in path byte order.
```maxon
// --- file: shapes/box.maxon
export type Box
	export var side as ExitCode
end 'Box'

// --- file: crates/box.maxon
export enum Box
	open
	shut
end 'Box'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3006: shapes/<fragment>:3:13: duplicate definition of 'Box' — already declared as `enum Box`
```

<!-- test: error.extension-alias-pair-compiling-to-one-name -->
⭐ The same non-injective `_` join as `error.two-instantiations-compile-to-one-name`, reached through the
door that case cannot reach: a `typealias` declared INSIDE an `extension`, minted once per conforming
type rather than once where it is written. `Both = Pair with (K, V)` is one line of source, and
`One implements Duo with (A_B, C)` and `Two implements Duo with (A, B_C)` make it two instantiations
that both join to `Pair_A_B_C`. Unchecked, the second mint clobbers the first registration, so `One`'s
`Both` is laid out as `Pair with (A, B_C)` and `p.y.only` reads the wrong field — the program compiles
and prints `2 77` where it packs `20 77`.

The body reaches `Pair` through `Both.make(...)` rather than a struct literal because an inner alias
naming a USER generic keeps E3076 — see `specs/inner-alias-construction.md`. That refusal is
correct and unrelated to the join this case pins; a literal here would report it instead, and the
E3006 would never be reached.

```maxon
typealias Num = int(i64.min to i64.max)

type A_B
	export var only as Num

	static function create(only Num) returns Self
		return Self{only: only}
	end 'create'
end 'A_B'

type A
	export var pad as Num
	export var only as Num

	static function create(pad Num, only Num) returns Self
		return Self{pad: pad, only: only}
	end 'create'
end 'A'

type C
	export var pad as Num
	export var only as Num

	static function create(pad Num, only Num) returns Self
		return Self{pad: pad, only: only}
	end 'create'
end 'C'

type B_C
	export var only as Num

	static function create(only Num) returns Self
		return Self{only: only}
	end 'create'
end 'B_C'

type Pair uses X, Y
	export var x as X
	export var y as Y

	static function make(x X, y Y) returns Self
		return Pair{x: x, y: y}
	end 'make'
end 'Pair'

interface Duo uses K, V
	function first() returns K
	function second() returns V
end 'Duo'

extension Duo
	typealias Both = Pair with (K, V)

	function packedSecond() returns Num
		let p = Both.make(first(), y: second())
		return p.y.only
	end 'packedSecond'
end 'Duo'

type One implements Duo with (A_B, C)
	function first() returns A_B
		return A_B.create(1)
	end 'first'

	function second() returns C
		return C.create(2, only: 20)
	end 'second'

	static function create() returns Self
		return Self{}
	end 'create'
end 'One'

type Two implements Duo with (A, B_C)
	function first() returns A
		return A.create(3, only: 30)
	end 'first'

	function second() returns B_C
		return B_C.create(77)
	end 'second'

	static function create() returns Self
		return Self{}
	end 'create'
end 'Two'

function main() returns ExitCode
	print("{One.create().packedSecond()} {Two.create().packedSecond()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:53:12: duplicate definition of 'Pair_A_B_C' — the generic instantiations `Pair with (A_B, C)` and `Pair with (A, B_C)` compile to that same name
```

<!-- test: error.an-ambiguous-throws-clause-is-refused -->
A user `ParseError` exported beside the library's: a bare `throws ParseError` in a third file names an error
type two declarations hold.
```maxon
// --- file: lib/errors.maxon
export enum ParseError implements Error
	malformed
end 'ParseError'

// --- file: app/main.maxon
function check(n ExitCode) returns ExitCode throws ParseError
	return n
end 'check'

function main() returns ExitCode
	return try check(1) otherwise 2
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:8:52: Ambiguous type name 'ParseError': more than one visible declaration matches it. Qualify it as one of: lib.ParseError, stdlib.ParseError
```

<!-- test: error.a-throws-clause-naming-a-type-the-library-keeps-private-is-refused -->
`stdlib/FilePath.maxon`'s `ParentComponentRule` carries no `public`, so a `throws` clause cannot name it.
```maxon
function check(n ExitCode) returns ExitCode throws ParentComponentRule
	return n
end 'check'

function main() returns ExitCode
	return try check(1) otherwise 2
end 'main'
```
```maxoncstderr
error E3008: <fragment>:2:52: type 'ParentComponentRule' is not exported
```

<!-- test: error.an-ambiguous-implemented-interface-is-refused -->
A user `Parsable` exported beside the library's: a bare `implements Parsable` in a third file names an interface
two declarations hold.
```maxon
// --- file: lib/parsable.maxon
export interface Parsable
	function parse() returns bool
end 'Parsable'

// --- file: app/main.maxon
type Doc implements Parsable
	export function parse() returns bool
		return true
	end 'parse'
end 'Doc'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:8:21: Ambiguous type name 'Parsable': more than one visible declaration matches it. Qualify it as one of: lib.Parsable, stdlib.Parsable
```

<!-- test: error.an-ambiguous-constraint-interface-is-refused -->
The same pair named by a `where` constraint.
```maxon
// --- file: lib/parsable.maxon
export interface Parsable
	function parse() returns bool
end 'Parsable'

// --- file: app/main.maxon
function parsed(x T) uses T returns bool where T is Parsable
	return x.parse()
end 'parsed'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:8:53: Ambiguous type name 'Parsable': more than one visible declaration matches it. Qualify it as one of: lib.Parsable, stdlib.Parsable
```

<!-- test: error.an-ambiguous-parent-interface-is-refused -->
The same pair named by an `extends` clause.
```maxon
// --- file: lib/parsable.maxon
export interface Parsable
	function parse() returns bool
end 'Parsable'

// --- file: app/main.maxon
interface Fancy extends Parsable
	function shine() returns bool
end 'Fancy'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:8:25: Ambiguous type name 'Parsable': more than one visible declaration matches it. Qualify it as one of: lib.Parsable, stdlib.Parsable
```

<!-- test: error.an-interface-the-library-keeps-private-cannot-be-implemented -->
A library interface without `public` is hidden from author code, exactly as a library type is.
```maxon
// --- stdlib-overlay: helpers/string/hash.maxon
interface OverlayPrivateProtocol
	function poke() returns bool
end 'OverlayPrivateProtocol'
// --- file: main.maxon
type Doc implements OverlayPrivateProtocol
	export function poke() returns bool
		return true
	end 'poke'
end 'Doc'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:7:21: type 'OverlayPrivateProtocol' is not exported
```

<!-- test: error.a-module-scoped-library-interface-cannot-constrain-an-author-function -->
A `module` library interface is visible inside its own directory of the library and nowhere else.
```maxon
// --- stdlib-overlay: helpers/string/hash.maxon
module interface OverlayModuleProtocol
	function poke() returns bool
end 'OverlayModuleProtocol'
// --- file: main.maxon
function poked(x T) uses T returns bool where T is OverlayModuleProtocol
	return x.poke()
end 'poked'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:7:52: type 'OverlayModuleProtocol' is module-scoped and not visible from this directory
```

<!-- test: error.a-hidden-qualified-construction-head-is-refused-at-top-level -->
A file-private typealias named through its directory as the head of a top-level `from` construction: the qualified spelling reaches the declaration without making it visible.
```maxon
// --- file: lib/codes.maxon
typealias Codes = Array with Byte

export function probeCodes() returns ExitCode
	let held = Codes from [1]
	return 0 if held.isEmpty() else 1
end 'probeCodes'

// --- file: app/main.maxon
let C = lib.Codes from [1, 2]

function main() returns ExitCode
	return lib.probeCodes()
end 'main'
```
```maxoncstderr
error E3008: app/<fragment>:11:9: typealias 'lib.Codes' is not exported
```

<!-- test: error.an-ambiguous-literal-init-head-is-refused-at-top-level -->
A directory exports a `FilePath` that initializes from a string literal beside the library's; a top-level
`FilePath from "x"` in another directory names a type two declarations hold.
```maxon
// --- file: lib/path.maxon
export type FilePath implements InitableFromStringLiteral
	export let text as String

	export static function init(value String) returns FilePath
		return Self{text: value}
	end 'init'
end 'FilePath'

// --- file: app/main.maxon
let p = FilePath from "x"

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:12:9: Ambiguous type name 'FilePath': more than one visible declaration matches it. Qualify it as one of: lib.FilePath, stdlib.FilePath
```

<!-- test: error.a-hidden-literal-init-head-is-refused-at-top-level -->
A library type without `public` that initializes from a string literal, constructed at the top level of an
author's file.
```maxon
// --- stdlib-overlay: helpers/string/hash.maxon
type OverlayPrivateTag implements InitableFromStringLiteral
	export let text as String

	static function init(value String) returns OverlayPrivateTag
		return Self{text: value}
	end 'init'
end 'OverlayPrivateTag'
// --- file: main.maxon
let t = OverlayPrivateTag from "x"

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:11:9: type 'OverlayPrivateTag' is not exported
```

<!-- test: error.an-ambiguous-enum-case-is-refused-at-top-level -->
A directory exports a `ParseError` beside the library's; a top-level constant naming one of its cases in another
directory names a type two declarations hold.
```maxon
// --- file: lib/errors.maxon
export enum ParseError implements Error
	malformed
end 'ParseError'

// --- file: app/main.maxon
let e = ParseError.malformed

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:8:9: Ambiguous type name 'ParseError': more than one visible declaration matches it. Qualify it as one of: lib.ParseError, stdlib.ParseError
```

<!-- test: error.a-hidden-enum-case-is-refused-at-top-level -->
A library enum without `public`, its case named as a top-level constant in an author's file.
```maxon
// --- stdlib-overlay: helpers/string/hash.maxon
enum OverlayPrivateMode
	first
	second
end 'OverlayPrivateMode'
// --- file: main.maxon
let m = OverlayPrivateMode.first

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:8:9: type 'OverlayPrivateMode' is not exported
```

<!-- test: error.an-ambiguous-union-case-construction-is-refused-at-top-level -->
A directory exports a union `ParseError` beside the library's enum; a top-level construction of one of its cases
in another directory names a type two declarations hold.
```maxon
// --- file: lib/errors.maxon
export union ParseError
	malformed(at Byte)
	quiet
end 'ParseError'

// --- file: app/main.maxon
let h = ParseError.malformed(7)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:9:9: Ambiguous type name 'ParseError': more than one visible declaration matches it. Qualify it as one of: lib.ParseError, stdlib.ParseError
```

<!-- test: error.a-hidden-union-case-construction-is-refused-at-top-level -->
A library union without `public`, its payload case constructed by a top-level constant in an author's file.
```maxon
// --- stdlib-overlay: helpers/string/hash.maxon
union OverlayPrivateOp
	add(n Byte)
	nop
end 'OverlayPrivateOp'
// --- file: main.maxon
let h = OverlayPrivateOp.add(7)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:8:9: type 'OverlayPrivateOp' is not exported
```

<!-- test: error.an-ambiguous-payload-free-union-case-is-refused-at-top-level -->
The payload-free case of a payload-carrying union is a box rather than a tag, and its type name is judged like
every other position's.
```maxon
// --- file: lib/errors.maxon
export union ParseError
	malformed(at Byte)
	quiet
end 'ParseError'

// --- file: app/main.maxon
let u = ParseError.quiet

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:9:9: Ambiguous type name 'ParseError': more than one visible declaration matches it. Qualify it as one of: lib.ParseError, stdlib.ParseError
```

<!-- test: error.a-hidden-payload-free-union-case-is-refused-at-top-level -->
The payload-free case of a library union without `public`, named by a top-level constant in an author's file.
```maxon
// --- stdlib-overlay: helpers/string/hash.maxon
union OverlayPrivateOp
	add(n Byte)
	nop
end 'OverlayPrivateOp'
// --- file: main.maxon
let u = OverlayPrivateOp.nop

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:8:9: type 'OverlayPrivateOp' is not exported
```

<!-- test: error.an-ambiguous-factory-type-is-refused-at-top-level -->
A directory exports a `FilePath` with a `create()` factory beside the library's; a top-level `FilePath.create()`
in another directory names a type two declarations hold.
```maxon
// --- file: lib/path.maxon
export type FilePath
	export let size as ExitCode

	export static function create() returns FilePath
		return Self{size: 1}
	end 'create'
end 'FilePath'

// --- file: app/main.maxon
let g = FilePath.create()

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:12:9: Ambiguous type name 'FilePath': more than one visible declaration matches it. Qualify it as one of: lib.FilePath, stdlib.FilePath
```

<!-- test: error.a-hidden-factory-type-is-refused-at-top-level -->
A library type without `public` whose factory is `public`, called by a top-level constant in an author's file:
the type is named, and naming it is what its tier refuses.
```maxon
// --- stdlib-overlay: helpers/string/hash.maxon
type OverlayPrivateThing
	export let size as Byte

	public static function create() returns OverlayPrivateThing
		return Self{size: 1}
	end 'create'
end 'OverlayPrivateThing'
// --- file: main.maxon
let g = OverlayPrivateThing.create()

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:11:9: type 'OverlayPrivateThing' is not exported
```

<!-- test: error.a-module-type-named-through-its-directory-in-a-signature-is-refused -->
A `module` type written through its directory in a parameter and a return type, from outside that directory: the
qualified spelling reaches the declaration without making it visible.
```maxon
// --- file: b/thing.maxon
module type Thing
	export let size as ExitCode

	module static function create() returns Thing
		return Self{size: 1}
	end 'create'
end 'Thing'

// --- file: a/main.maxon
function keep(t b.Thing) returns b.Thing
	return t
end 'keep'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3088: a/<fragment>:12:17: type 'b.Thing' is module-scoped and not visible from this directory
error E3088: a/<fragment>:12:34: type 'b.Thing' is module-scoped and not visible from this directory
```

<!-- test: error.a-file-private-type-named-through-its-directory-as-a-call-base-is-refused -->
A file-private type written through its directory as the base of a static call, from another directory.
```maxon
// --- file: b/thing.maxon
type Thing
	export static function answer() returns ExitCode
		return 7
	end 'answer'
end 'Thing'

// --- file: a/main.maxon
function main() returns ExitCode
	return b.Thing.answer()
end 'main'
```
```maxoncstderr
error E3008: a/<fragment>:11:9: type 'b.Thing' is not exported
```

<!-- test: error.a-module-type-named-through-its-directory-is-refused-at-top-level -->
A `module` type written through its directory as a top-level factory head, from outside that directory.
```maxon
// --- file: b/thing.maxon
module type Thing
	export let size as ExitCode

	module static function create() returns Thing
		return Self{size: 1}
	end 'create'
end 'Thing'

// --- file: a/main.maxon
let t = b.Thing.create()

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3088: a/<fragment>:12:9: type 'b.Thing' is module-scoped and not visible from this directory
```

<!-- test: error.a-module-type-named-bare-outside-its-subtree-is-refused -->
The bare name is held to the same tier as the qualified one.
```maxon
// --- file: b/thing.maxon
module type Thing
	export let size as ExitCode

	module static function create() returns Thing
		return Self{size: 1}
	end 'create'
end 'Thing'

// --- file: a/main.maxon
function keep(t Thing) returns Thing
	return t
end 'keep'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3088: a/<fragment>:12:17: type 'Thing' is module-scoped and not visible from this directory
error E3088: a/<fragment>:12:32: type 'Thing' is module-scoped and not visible from this directory
```

<!-- test: a-module-type-named-through-its-directory-is-reachable-from-a-subdirectory -->
Inside the declaring directory's subtree the qualified spelling of a `module` type reaches it.
```maxon
// --- file: b/thing.maxon
module type Thing
	export let size as ExitCode

	module static function create() returns Thing
		return Self{size: 1}
	end 'create'
end 'Thing'

// --- file: b/inner/main.maxon
function main() returns ExitCode
	let t = b.Thing.create()
	print("{t.size}\n")
	return 0
end 'main'
```
```stdout
1
```

<!-- test: a-directory-qualified-alias-is-a-top-level-cast-target -->
Two directories export a `Score`; a top-level constant casts to each through its directory, which is the spelling
E3063 prescribes.
```maxon
// --- file: api/score.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/score.maxon
export typealias Score = int(0 to 50)

// --- file: app/main.maxon
let a = 70 as api.Score
let b = 20 as legacy.Score

function main() returns ExitCode
	print("{a} {b}\n")
	return 0
end 'main'
```
```stdout
70 20
```

<!-- test: error.a-top-level-cast-to-a-directory-qualified-alias-checks-its-range -->
The qualified cast target is that declaration's range, not the other directory's.
```maxon
// --- file: api/score.maxon
export typealias Score = int(0 to 100)

// --- file: legacy/score.maxon
export typealias Score = int(0 to 50)

// --- file: app/main.maxon
let a = 70 as api.Score
let b = 70 as legacy.Score

function main() returns ExitCode
	print("{a} {b}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: app/<fragment>:10:12: Value 70 is outside the range of 'legacy.Score' (int(0 to 50))
```

<!-- test: error.a-hidden-directory-qualified-alias-is-refused-as-a-top-level-cast-target -->
A file-private typealias named through its directory as a top-level cast target, from another directory.
```maxon
// --- file: lib/score.maxon
typealias Score = int(0 to 100)

export function probeScore() returns ExitCode
	let s = 5 as Score
	return s
end 'probeScore'

// --- file: app/main.maxon
let a = 5 as lib.Score

function main() returns ExitCode
	return lib.probeScore()
end 'main'
```
```maxoncstderr
error E3008: app/<fragment>:11:14: typealias 'lib.Score' is not exported
```

<!-- test: error.an-ambiguous-enum-case-beside-an-alias-is-refused-at-top-level -->
A directory exports a typealias `Ordering` beside the library's enum; a top-level constant naming one of the
enum's cases is ambiguous, whichever of the two declarations has the case.
```maxon
// --- file: lib/order.maxon
export typealias Ordering = int(0 to 9)

// --- file: app/main.maxon
let o = Ordering.lessThan

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:6:9: Ambiguous type name 'Ordering': more than one visible declaration matches it. Qualify it as one of: lib.Ordering, stdlib.Ordering
```

<!-- test: error.an-ambiguous-union-case-construction-beside-an-alias-is-refused-at-top-level -->
A directory exports a typealias `LogValue` beside the library's union; a top-level construction of a payload case
is ambiguous.
```maxon
// --- file: lib/value.maxon
export typealias LogValue = int(0 to 9)

// --- file: app/main.maxon
let v = LogValue.boolean(true)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:6:9: Ambiguous type name 'LogValue': more than one visible declaration matches it. Qualify it as one of: lib.LogValue, stdlib.LogValue
```

<!-- test: error.an-ambiguous-payload-free-union-case-beside-an-alias-is-refused-at-top-level -->
A directory exports a typealias `StatusCode` beside the library's payload-carrying union; a top-level constant
naming a payload-free case is ambiguous.
```maxon
// --- file: lib/status.maxon
export typealias StatusCode = int(0 to 9)

// --- file: app/main.maxon
let s = StatusCode.ok

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:6:9: Ambiguous type name 'StatusCode': more than one visible declaration matches it. Qualify it as one of: lib.StatusCode, stdlib.StatusCode
```

<!-- test: error.an-ambiguous-factory-type-beside-an-alias-is-refused-at-top-level -->
A directory exports a typealias `Console` beside the library's type; a top-level `Console.stdin()` is ambiguous.
```maxon
// --- file: lib/console.maxon
export typealias Console = int(0 to 9)

// --- file: app/main.maxon
let c = Console.stdin()

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3063: app/<fragment>:6:9: Ambiguous type name 'Console': more than one visible declaration matches it. Qualify it as one of: lib.Console, stdlib.Console
```
