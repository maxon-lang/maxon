---
feature: typealias-file-scope
status: stable
keywords: [typealias, file-scope, ranged, stdlib, shadowing, cross-file]
category: diagnostics
---

# A typealias resolves in ITS OWN FILE first

## Documentation

A non-exported `typealias` is **file-local** (`specs/duplicate-typealias.md`), so two files may each
declare `Limit` with a different range and neither disturbs the other. Resolution is therefore
**scoped first, bare second** — the identical rule top-level `let`/`var` bindings already resolve by
(`ProgramSignatures.declFor`) — and it is what makes a file's own declaration authoritative for the
casts written in that file.

Before this rule existed, the alias registry was one whole-program map keyed by the bare name, so the
**last file merged won** and its range silently replaced everyone else's. That is a wrong ANSWER, not
a missing feature: a cast the declaring file's own range forbids compiled clean, and a cast that range
permits was rejected against a stranger's.

Two directions have to hold, and each catches the opposite failure:

- a **narrow** file's out-of-range cast is still **rejected** when another file's alias is wider
  (otherwise the wide one erases a guard the author wrote);
- a **wide** file's in-range cast is still **accepted** when another file's alias is narrower
  (otherwise the narrow one invents a guard the author never wrote).

`stdlib/` is the same rule with no special case: a listed stdlib module's typealias is another file's
declaration, so a user's own alias of that name wins for the user's own casts — and the user's does
not disturb the stdlib module either. `stdlib/Sleep.maxon` declares `Milliseconds`, which is what
makes it the case a user actually meets.

## The RANGE is per file. The UNDERLYING PRIMITIVE is not.

File scoping resolves the **range**, because the range is enforced where the declaring file is known
(`InsertRangeChecks`). The **underlying primitive** — `int` or `float` — is read by a second set of
readers that have no file to ask from: type resolution of a struct field (a `StructLayout` records no
declaring file), union payload classification reached from the emitted runtime's walk of the enum
registry, and generic type-argument and conformance-signature canonicalization. Those all resolve the
bare name against a registry holding one entry per name.

So a name whose declarations disagree about `int`-vs-`float` has **no answer that door can give**, and
it is refused at the second file's declaration with **E3105** rather than answered arbitrarily. Two
files declaring one name over different *ranges* stays legal and is the case above.

This is what makes the bare answer safe rather than lucky: because every declaration of a name shares
one underlying primitive in any program that compiles, the bare answer *is* the answer a scoped lookup
would give. Without the rule the parser resolved such a name file-scoped while those readers resolved
it last-wins, and the disagreement reached the backends — the x64 emitter panicked on an xmm value in
a gpr slot, wasm emitted a module its own validator rejected, and a struct field typed by the alias
compiled to the wrong width with no diagnostic at all.

## A THIRD file resolves to a declaration it MAY NAME

The rule above answers for the two files that declare the name. A **third** file — one that names
`Limit` and declares no `Limit` of its own — is the case neither of them covers, and the bare door
answered it *last-wins*: whichever declaration was merged last decided what the third file's parameter,
return type and cast meant.

That is not a tie between equals, because **a plain `typealias` is file-local and a third file may not
name it at all.** A declaration the reader is forbidden to write down cannot be the one the reader
meant. So the third file resolves to a declaration it MAY name — an `export`ed one, or one the
compiler supplies on the stdlib's behalf — and only falls back to the bare last-wins answer when *no*
declaration of the name is nameable from anywhere, which is the state the "declared, but hidden from
you" diagnostics (E2003 / E3011) are built on and which must keep its answer.

**MEASURED, and it was a wrong answer in the worst direction — a user's private alias decided what a
STDLIB function accepts.** A user file declaring `typealias Codepoint = int(0 to 100)` made
`stdlib/helpers/string/utf16.maxon`'s `utf16LeadSurrogate(codepoint Codepoint)` — whose `Codepoint` is
`stdlib/Character.maxon`'s exported `int(0 to 1114111)` — reject a perfectly legal `70000` with
`E3005 … outside the range of 'Codepoint' (int(0 to 100))`. `utf16.maxon` declares no `Codepoint`, so it
fell through to the bare door, and the user's file merged last. The range quoted at the user belonged to
the user's own alias; the function refusing it belonged to a file that had never heard of it.

**Out of scope**, and deliberately: `export` visibility as a *key* (an exported alias is still filed
under its bare name) and **E3063** ambiguity between two *nameable* aliases of one name in different
files — which is still last-wins, on the strictly smaller set of declarations the reader may name.
Both need cross-file name resolution; this rung is the file-scoped half only.

## Tests

<!-- test: user-alias-wins-over-stdlib -->
A user file declares `Milliseconds`, the name `stdlib/Sleep.maxon` also declares. The user's own
range governs the cast written in the user's file, so `500` is out of range and rejected. Before
file-scoped resolution the stdlib module merged last and its `int(0 to u64.max)` silently won: this
program compiled and returned 9.
```maxon
typealias Milliseconds = int(0 to 100)

function main() returns ExitCode
	let m = 500 as Milliseconds
	if m > 100 'chk'
		return 9
	end 'chk'
	return 3
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:14: Value 500 is outside the range of 'Milliseconds' (int(0 to 100))
```


<!-- test: narrow-file-cast-still-rejected -->
`a.maxon`'s `Limit` is `int(0 to 500)` and `b.maxon`'s is `int(0 to 2000)`. The cast in `a.maxon` is
checked against `a.maxon`'s range and rejected. This is the direction where the WIDER alias would
erase a guard the author wrote — the failure that returned 9 from this program.

The diagnostic is anchored in **`a.maxon`**, the file that wrote the cast — never in `b.maxon`, which
declares the same name over a different, wider range.
```maxon
// --- file: a.maxon
typealias Limit = int(0 to 500)

export function checkA() returns ExitCode
	let v = 600 as Limit
	return v
end 'checkA'

// --- file: b.maxon
typealias Limit = int(0 to 2000)

export function checkB() returns Limit
	return 0
end 'checkB'

// --- file: main.maxon
function main() returns ExitCode
	return checkA()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:14: Value 600 is outside the range of 'Limit' (int(0 to 500))
```


<!-- test: wide-file-cast-still-accepted -->
The opposite direction, and the shape of the collision `stdlib/helpers/string/` already contains:
`utf16.maxon` declares `Utf16UnitCount = int(1 to 2)` while `views.maxon` declares the same name as
`int(0 to u64.max)`. Each file's cast is checked against its OWN range, so the wide file's `40`
compiles even though a narrower alias of that name exists elsewhere. Under one whole-program registry
this program did not merely answer wrongly — whichever file merged last decided whether it compiled
at all, from the order the directory walk happened to return.
```maxon
// --- file: narrow.maxon
typealias Unit = int(1 to 2)

export function unitVal() returns ExitCode
	let u = 2 as Unit
	return u
end 'unitVal'

// --- file: wide.maxon
typealias Unit = int(0 to u64.max)

export function wideVal() returns ExitCode
	let w = 40 as Unit
	return w
end 'wideVal'

// --- file: main.maxon
function main() returns ExitCode
	return unitVal() + wideVal()
end 'main'
```
```exitcode
42
```


<!-- test: error.crossfile-alias-underlying-conflict -->
Two files declare `Measure`, one over `int` and one over `float`. Unlike two ranges, this pair has no
answer the file-less readers can be given, so it is refused at `b.maxon`'s declaration — the second
one, the newcomer — and never at `a.maxon`, which is the line that was fine.

Before the rule this program reached the x64 emitter, which panicked with
`xmm0 is in the xmm register file where the gpr file is required`: the parser had resolved `Measure`
to `int` inside `a.maxon` (file-scoped) while type resolution resolved it to `float` (bare,
last-wins). Two deciders, and nothing made them agree.
```maxon
// --- file: a.maxon
typealias Measure = int(0 to 100)

export function useInt(x Measure) returns Measure
	return x + 1
end 'useInt'

// --- file: b.maxon
typealias Measure = float(0.0 to 1.0)

export function useFloat(x Measure) returns Measure
	return x
end 'useFloat'

// --- file: main.maxon
function main() returns ExitCode
	return useInt(41) as ExitCode
end 'main'
```
```maxoncstderr
error E3105: <fragment>:10:11: Typealias 'Measure' is declared over 'float' here and over 'int' in another file — two files may declare one alias name over different RANGES, but not over different underlying types
```


<!-- test: crossfile-alias-same-underlying-different-range-still-legal -->
The guard the rule must not overreach into: two files, one name, two RANGES, one underlying `int`.
This is the shape `stdlib/` depends on — seven files privately declare `Byte = int(0 to u8.max)` —
and it stays legal. Each file's cast is checked against its own range, so both compile.
```maxon
// --- file: a.maxon
typealias Span = int(0 to 20)

export function fromA() returns ExitCode
	return 20 as Span
end 'fromA'

// --- file: b.maxon
typealias Span = int(0 to 4000)

export function fromB() returns ExitCode
	return 22 as Span
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	return fromA() + fromB()
end 'main'
```
```exitcode
42
```


<!-- test: third-file-resolves-to-the-nameable-declaration -->
A **third** file is what the two-file rule does not answer. `lib.maxon` names `Codepoint` and declares
none, so its parameter's range is neither of its own files' business — and the bare door used to hand it
whichever declaration merged last, which is `main.maxon`'s private `int(0 to 100)`. A legal `70000` was
then refused at the caller with a range that belongs to a file `lib.maxon` has never seen. The
declaration `lib.maxon` may actually NAME is the exported one, and that is the one it gets.
```maxon
// --- file: alias.maxon
export typealias Codepoint = int(0 to 1114111)

// --- file: lib.maxon
export function widen(c Codepoint) returns int
	return c / 1000
end 'widen'

// --- file: main.maxon
typealias Codepoint = int(0 to 100)

function narrow(c Codepoint) returns int
	return c
end 'narrow'

function main() returns ExitCode
	return (widen(70000) - narrow(28)) as ExitCode
end 'main'
```
```exitcode
42
```


<!-- test: third-file-runtime-guard-uses-the-nameable-declaration -->
The same collision through the door that emits CODE rather than a diagnostic: `big` is opaque, so
`widen`'s narrowed parameter is enforced by its ENTRY GUARD (A1f), and that guard reads its bounds
through the very lookup this rule fixes. Against the bare door it was built from `main.maxon`'s
`int(0 to 100)` and the program died `Range check failed: value outside typealias 'Codepoint'` on a
value the alias `lib.maxon` can name admits. A false panic is the runtime form of the false rejection
above, and it is the form the stdlib actually met.
```maxon
// --- file: alias.maxon
export typealias Codepoint = int(0 to 1114111)

// --- file: lib.maxon
export function widen(c Codepoint) returns Codepoint
	return c
end 'widen'

// --- file: main.maxon
typealias Codepoint = int(0 to 100)
typealias Integer = int(i64.min to i64.max)

function opaque(n Integer) returns Integer
	return n
end 'opaque'

function narrow(c Codepoint) returns int
	return c
end 'narrow'

function main() returns ExitCode
	let big = opaque(70000)
	return ((widen(big) / 1000) - narrow(28)) as ExitCode
end 'main'
```
```exitcode
42
```


<!-- test: error.file-private-alias-still-binds-in-its-own-file -->
The direction the fix must not overreach into, and the reason the scoped probe stays FIRST. `main.maxon`
declares `Codepoint` privately, so `narrow`'s parameter means `int(0 to 100)` **in `main.maxon`** — the
exported declaration elsewhere does not widen it. Only a file that declares none of them resolves to the
nameable one.
```maxon
// --- file: alias.maxon
export typealias Codepoint = int(0 to 1114111)

// --- file: lib.maxon
export function widen(c Codepoint) returns int
	return c
end 'widen'

// --- file: main.maxon
typealias Codepoint = int(0 to 100)

function narrow(c Codepoint) returns int
	return c
end 'narrow'

function main() returns ExitCode
	return (widen(70000) + narrow(150)) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:18:25: Value 150 is outside the range of 'Codepoint' (int(0 to 100))
```
