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

## A GENERIC-INSTANCE typealias is file-local too — `typealias Slots = Array with Big`

Everything above is about the RANGED form. A **generic-instance** typealias (`typealias Slots = Array
with Big`) is a `typealias` like any other: not exported, it is file-local, and two files may each
declare `Slots` over a different instance without disturbing each other.

It was resolved through a second whole-program map keyed by the bare name, so it had the *original*
defect the ranged form was cured of — **the last file folded won, and every other file silently got
its instance.** The two forms this takes are both wrong answers and neither mentions the file that
caused it:

- a **compile-time refusal quoting a type the blamed file cannot name.** `main.maxon` declares
  `Slots = Array with Small` (`int(0 to 100)`) and `wide.maxon` declares `Slots = Array with Big`
  (`int(0 to 60000)`); `wide.maxon`'s own `push(50000)` was rejected as
  `E3005 wide.maxon:6:4: Value 50000 is outside the range of 'Small' (int(0 to 100))` — a range from a
  declaration `wide.maxon` does not contain and may not write down;
- a **runtime wrong answer**, which is the more dangerous shape and reaches a program that compiled
  clean. Where the two `Slots` differ only through a `Byte` the two files declare differently, the
  element identity is already correct (`bytearray-element-size.md`) and only the alias is shared, so
  the wide file's `push` of its own in-range `900` reaches the narrow file's element and panics
  `Range check failed: value outside typealias 'Byte'` — quoting the name of a declaration whose
  bounds permit the value.

⚠ **RESOLVING THE NAME IS NOT ENOUGH ON ITS OWN, AND THE MISSING HALF IS THE DROP ROUTER.** A struct
FIELD declared with a generic alias is recorded by the declaration sweep as a bare `named("Slots")` —
the alias is not interned yet — and the drop/clone cascade resolves that name with **no reader file to
ask from** (`managedFieldDropCallee`, `managedFieldCloneStrategy`, `fieldTypeIsArray`). While the
parser was last-wins too, those agreed with it and the program was merely refused. Scoping only the
parser makes them **disagree**: a field whose element is an `int` would be routed to the destructor of
an `Array with String` and its integers freed as pointers. So the field's recorded type is resolved
ONCE, against the file that declared it, at the moment the contest is known — and the file-less door
**refuses a contested name outright** rather than answering it arbitrarily, so a recorded spelling
that was missed is a loud compiler panic and never a wild free.

⚠ **AN AGREEING NAME IS NOT CONTESTED, AND THAT IS THE LOAD-BEARING HALF**, exactly as it is for a
contested `Byte`. Two files that both declare `typealias Counts = Array with Count` over one `Count`
name one instance between them: the name is not contested, every scoping door returns on its first
line, and no interned instance, mangled symbol or committed golden moves.

## Tests

<!-- test: user-alias-wins-over-stdlib -->
A user file declares `Milliseconds`, the name `stdlib/Sleep.maxon` also declares. The user's own
range governs the cast written in the user's file, so `500` is out of range and rejected. Before
file-scoped resolution the stdlib module merged last and ITS range silently won: this program
compiled and returned 9. (`stdlib/Sleep.maxon` declared `int(0 to u64.max)` when that was measured
and declares `int(0 to i64.max)` now; either admits `500`, so the observation is unchanged.)
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
`a.maxon`'s `Limit` is `int(0 to 200)` and `b.maxon`'s is `int(0 to 2000)`. The cast in `a.maxon` is
checked against `a.maxon`'s range and rejected. This is the direction where the WIDER alias would
erase a guard the author wrote — the failure that returned 9 from this program.

The diagnostic is anchored in **`a.maxon`**, the file that wrote the cast — never in `b.maxon`, which
declares the same name over a different, wider range.

⚠ **`a.maxon`'s BOUND IS INSIDE `ExitCode`'S NARROWEST PLATFORM RANGE, AND THAT IS DELIBERATE.** It was
`int(0 to 500)` until BATCH27 made `ExitCode` `int(0 to 255)` on Linux, macOS and WASI — at which point
`return v` stopped fitting and the program grew a SECOND E3005, naming `ExitCode`, on three of four
targets. That second error is not this case's subject: the subject is *which file's `Limit` the cast in
`a.maxon` is checked against*, and an incidental diagnostic about an unrelated builtin would sit in the
expectation masking it. Pinning it per-target would have written the noise down in two places instead of
removing it. `200` is under `255`, so `checkA`'s `return` is quiet on every target and the only
diagnostic left is the one the case exists for.
```maxon
// --- file: a.maxon
typealias Limit = int(0 to 200)

export function checkA() returns ExitCode
	let v = 600 as Limit
	return v
end 'checkA'

// --- file: b.maxon
typealias Limit = int(0 to 2000)

public function checkB() returns Limit
	return 0
end 'checkB'

// --- file: main.maxon
function main() returns ExitCode
	return checkA()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:14: Value 600 is outside the range of 'Limit' (int(0 to 200))
```


<!-- test: wide-file-cast-still-accepted -->
The opposite direction, and the shape of the collision `stdlib/helpers/string/` already contains:
`utf16.maxon` declares `Utf16UnitCount = int(1 to 2)` while `views.maxon` declares the same name as
`int(0 to i64.max)`. Each file's cast is checked against its OWN range, so the wide file's `40`
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
	return trunc(useInt(41))
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

### ⛔⛔ AN ARM-SERVED DOOR RESOLVES ITS ALIAS IN THE *CALLEE'S* FILE, AND THE ANSWER HAS TO TRAVEL

Every door above resolves the alias where it was WRITTEN, so the site's file and the alias's file are
one. `Array.get`/`set`/`resize` are the exception: shv2 serves them from an ARM rather than a call, so
there is no callee entry to guard and the bound is fetched from `stdlib/Array.maxon`'s declaration and
applied at the CALL (`Parser.recordArmServedIndexRangeCheck`). That resolution was correct from the
day it was written — and the emitted check still came out of a different declaration, because the site
recorded only the alias's NAME and `InsertRangeChecks` re-resolved it against the CALLING file.

⛔ **MEASURED 2026-08-30, and it is the defect the `third-file-*` cases above exist to close, arriving
through a door that never asked `lookup` which reader it meant: a user program's own FILE-PRIVATE
`typealias ElementIndex = int(0 to 3)` decided what `stdlib/Array.maxon`'s `set` accepts.** On a
20-element array, `a.set(9, value: 42)` was refused *"Value 9 is outside the range of 'ElementIndex'
(int(0 to 3))"*. `RangeCheckSite.aliasFilePath` carries the declaration now.

⚠ **THE STDLIB-INTERNAL HALF CANNOT BE WRITTEN AS A PROGRAM, so it is recorded here instead.**
`ElementIndex` and `ElementCount` are declared in BOTH `stdlib/Array.maxon` and `stdlib/Vector.maxon`,
and no source file can change either — the only probe is a sabotage of the library. Sabotage-verified
both ways: narrowing **`stdlib/Vector.maxon`**'s `ElementIndex` to `int(0 to 5)` refused an **`Array`**
index of 9, and narrowing **`stdlib/Array.maxon`**'s own had **no effect at all**. Both directions
reverse with the fix. Nothing was visibly wrong before it only because all four declarations carry the
same `int(0 to i64.max)`.

<!-- test: a-contested-element-index-does-not-govern-the-array-door -->
`main.maxon` and `lib.maxon` each declare `ElementIndex` over a range of their own, and neither may
reach `Array`'s. Index 9 is legal for `stdlib/Array.maxon`'s `int(0 to i64.max)` and illegal for both
user declarations, so a door reading either one refuses a legal program. Each file's own alias is
exercised beside it — `ownClamp(3)` and `libClamp(2)` — so the case cannot pass by the fix having
disabled file-scoped resolution instead of correcting it.
```maxon
// --- file: lib.maxon
typealias ElementIndex = int(0 to 2)

export function libClamp(i ElementIndex) returns ElementIndex
	return i
end 'libClamp'

// --- file: main.maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias ElementIndex = int(0 to 3)

function ownClamp(i ElementIndex) returns ElementIndex
	return i
end 'ownClamp'

function main() returns ExitCode
	var a = IntArray.create()
	a.resize(20)
	try a.set(9, value: 42) otherwise ignore
	print("{try a.get(9) otherwise 0} {ownClamp(3)} {libClamp(2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42 3 2
```

<!-- test: error.a-contested-element-index-still-governs-its-own-file -->
The direction the fix must not overreach into — `error.file-private-alias-still-binds-in-its-own-file`
one door over. `main.maxon`'s `ElementIndex` still means `int(0 to 3)` for `main.maxon`'s OWN cast,
even though the same name no longer reaches `Array.get`. If this case stops being an error, the cure
has stopped resolving by file rather than started resolving by the right one.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias ElementIndex = int(0 to 3)

function main() returns ExitCode
	var a = IntArray.create()
	a.resize(20)
	try a.set(9, value: 42) otherwise ignore
	// ⚠ NOT `return mine as ExitCode`: that spelling raises E3010 (`unneeded cast`) first and masks
	// the diagnostic this case is about.
	let mine = 9 as ElementIndex
	print("{mine}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:15: Value 9 is outside the range of 'ElementIndex' (int(0 to 3))
```


<!-- test: generic-alias-resolves-in-its-own-file -->
Each file declares `Slots` over its own element and pushes a value its own element permits. Under one
whole-program map the file folded second lost its declaration outright and its `push` was checked
against the other file's range: `E3005 … Value 50000 is outside the range of 'Small' (int(0 to 100))`,
reported inside `wide.maxon`, against a name that file never writes. The C# bootstrap answers 55.

⚠ **THE TWO RANGES ARE DISJOINT, AND THAT IS WHAT MAKES THIS A TEST.** Written with a NARROW and a
WIDE range it passed against the broken compiler for half the possible fold orders — whichever file
wins, the wide element accepts the narrow file's value too, so only one of the two directions is
observable and the directory walk decides which. `int(0 to 100)` and `int(1000 to 2000)` admit none of
each other's values, so a name resolved to the wrong file is refused whichever file won.
```maxon
// --- file: high.maxon
typealias High = int(1000 to 2000)
typealias Slots = Array with High

export function fromHigh() returns High
	var w = Slots.create()
	w.push(1500)
	return try w.get(0) otherwise 1000
end 'fromHigh'

// --- file: main.maxon
typealias Low = int(0 to 100)
typealias Slots = Array with Low

function main() returns ExitCode
	var w = Slots.create()
	w.push(40)
	return (fromHigh() / 100) + (try w.get(0) otherwise 0)
end 'main'
```
```exitcode
55
```


<!-- test: generic-alias-runtime-guard-uses-its-own-files-element -->
The RUNTIME half, and the dangerous one: this program COMPILED and then gave a wrong answer. The two
`Bytes` differ only through `Byte`, whose two declarations are already two element types
(`bytearray-element-size.md`), so the instances were distinct all along — only the alias name was
shared, and `wide.maxon` got `main.maxon`'s. Its own in-range `900` then met a guard for
`int(0 to u8.max)` and panicked `Range check failed: value outside typealias 'Byte'`, naming a
declaration whose bounds allow it. The C# bootstrap answers 209.

The two ranges are disjoint for the reason the case above states. The value reaching `push` here is a
PARAMETER and not a literal, so the guard it meets is the runtime one — which is what makes this the
form that compiles and then answers wrongly.
```maxon
// --- file: wide.maxon
typealias Byte = int(300 to 1000)
typealias Bytes = Array with Byte

export function wide(v Byte) returns Byte
	var b = Bytes.create()
	b.push(v)
	return try b.get(0) otherwise 300
end 'wide'

// --- file: main.maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var b = Bytes.create()
	let two = 200 as Byte
	b.push(two)
	return (try b.get(0) otherwise 0) + (wide(900) / 100)
end 'main'
```
```exitcode
209
```


<!-- test: generic-alias-field-drops-through-its-own-files-element -->
The half a scoped parser alone does not buy: `Bag` names an `Array with String` in one file and an
`Array with Num` in the other, and each is a struct FIELD. A field's declared type is recorded by the
sweep as a bare `named("Bag")`, and the drop and clone cascades resolve that name with no reader file,
so a parser scoped on its own would route `Nums.items` — a buffer of integers — to the destructor of
an array of Strings and free each element as a pointer. Both halves have to land together, and the
leak gate plus the exit code are what say they did.
```maxon
// --- file: words.maxon
typealias Bag = Array with String

type Words
	export var items as Bag

	export static function create() returns Words
		var b = Bag.create()
		b.push("hi")
		return Words{items: b}
	end 'create'
end 'Words'

export function wordCount() returns ElementCount
	let w = Words.create()
	return w.items.count()
end 'wordCount'

// --- file: main.maxon
typealias Num = int(0 to 1000)
typealias Bag = Array with Num

type Nums
	export var items as Bag

	export static function create() returns Nums
		var b = Bag.create()
		b.push(7)
		return Nums{items: b}
	end 'create'
end 'Nums'

function main() returns ExitCode
	let n = Nums.create()
	return (try n.items.get(0) otherwise 0) + wordCount()
end 'main'
```
```exitcode
8
```


<!-- test: agreeing-generic-alias-is-not-contested -->
The load-bearing negative, and it passes both before and after: two files declare `Counts` over one
`Count`, so both fold to ONE interned instance and the name is not contested at all. Nothing is
scoped, nothing is re-keyed, and the answer is the same answer the whole-program map already gave —
which is why the entire existing corpus, whose generic aliases are of exactly this shape, keeps every
instance and every emitted symbol it had.
```maxon
// --- file: a.maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

export function fromA() returns Count
	var c = Counts.create()
	c.push(40)
	return try c.get(0) otherwise 0
end 'fromA'

// --- file: main.maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

function main() returns ExitCode
	var c = Counts.create()
	c.push(2)
	return fromA() + (try c.get(0) otherwise 0)
end 'main'
```
```exitcode
42
```


<!-- test: generic-alias-nested-inside-another-generic-alias -->
An alias whose ARGUMENT is a contested alias. `Grid` is spelled identically in both files, so both
declarations interned to the one instance `Array with <the bare name "Slots">` and `Grid` looked like a
name they AGREE about — while its element denoted two different arrays. ⛔ MEASURED: the compiler
PANICKED, out of the element's drop-callee router (`arrayElementDropCallee` → the file-less door's
refusal), on a program both files' own declarations describe perfectly. Resolving a declaration's
arguments against its own file splits `Grid` in two, which is what makes `Grid` contested in turn — so
the contest is a fixpoint and not a single walk.
```maxon
// --- file: high.maxon
typealias High = int(1000 to 2000)
typealias Slots = Array with High
typealias Grid = Array with Slots

export function fromHigh() returns High
	var g = Grid.create()
	var row = Slots.create()
	row.push(1500)
	g.push(row)
	let back = try g.get(0) otherwise Slots.create()
	return try back.get(0) otherwise 1000
end 'fromHigh'

// --- file: main.maxon
typealias Low = int(0 to 100)
typealias Slots = Array with Low
typealias Grid = Array with Slots

function main() returns ExitCode
	var g = Grid.create()
	var row = Slots.create()
	row.push(40)
	g.push(row)
	let back = try g.get(0) otherwise Slots.create()
	return (fromHigh() / 100) + (try back.get(0) otherwise 0)
end 'main'
```
```exitcode
55
```


<!-- test: error.a-contested-alias-is-quoted-as-source-spells-it -->
A diagnostic names a declaration back at the author, and the compiler's contest mint (`Byte$300_1000`)
is a name NO SOURCE LINE HOLDS. ⛔ MEASURED while landing the generic form above: the narrowing E3005
stripped the mint and the `otherwise` E3005 did not, so one alias printed two ways depending only on
which of the two an out-of-range value happened to meet — with the real bounds spelled beside the
suffix that was supposed to carry them. Both sentences are now worded in one place and share the strip.
```maxon
// --- file: narrow.maxon
typealias Byte = int(0 to 255)

export function narrowByte(v Byte) returns Byte
	return v
end 'narrowByte'

// --- file: main.maxon
typealias Byte = int(300 to 1000)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var b = Bytes.create()
	let v = 500 as Byte
	b.push(v)
	return ((try b.get(0) otherwise 0) / 100) + narrowByte(5)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:11: otherwise value 0 is outside the range of 'Byte' (int(300 to 1000))
```


<!-- test: generic-alias-union-payload-drops-through-its-own-files-element -->
The union-payload twin of the field case: `Bag` names an `Array with String` in one file and an
`Array with Num` in the other, and each is a boxed union's PAYLOAD. A payload's declared type is
recorded by the same sweep, read by the same drop and deep-clone walks
(`unionPayloadsSupportDeepClone`), and so needs the same one-time resolution against its declaring
file. The payload is not BOUND here, and what this case is about is the DROP.

⭐ **THE `E3011 Unknown type 'Bag'` THIS PARAGRAPH USED TO CALL "an unrelated, pre-existing reason" WAS
NOT UNRELATED — IT WAS THE SAME FACT, ONE DOOR OVER, AND A4k CLOSED IT.** A generic-alias payload was
refused at a binding because `classifyUnionPayload` resolved a bare `named` through a cascade of its own
that had no generic-alias arm, while `declaredSlotType` — the door this case's own drop walk goes
through — did. `specs-shv2/generic-types.md` binds one now. What is deliberate here is only the SCOPE:
this case is the two-file one, and the reader-free walks are what it pins.

⚠ **AND THE EMITTED CODE FOR THIS CASE MOVED WHEN THAT LANDED, CORRECTLY.** Classified
`undeclaredName`, the payload was not managed: `WordBox.filled(b)` stored the array pointer WITHOUT
consuming `b`, so `b`'s own scope exit freed the buffer and the box held a stale pointer nothing read —
right answer, wrong owner. Classified as the instance it is, the construct MOVES the array into the box
and the union's `__destruct_<U>` cascade frees it. Both spellings exit 8 and neither leaks, which is
exactly why the split survived here.
```maxon
// --- file: words.maxon
typealias Bag = Array with String

union WordBox
	empty
	filled(items Bag)
end 'WordBox'

export function words() returns ExitCode
	var b = Bag.create()
	b.push("hi")
	let w = WordBox.filled(b)
	return match w 'm'
		empty gives 0
		filled gives 1
	end 'm'
end 'words'

// --- file: main.maxon
typealias Num = int(0 to 1000)
typealias Bag = Array with Num

union NumBox
	empty
	filled(items Bag)
end 'NumBox'

function main() returns ExitCode
	var b = Bag.create()
	b.push(7)
	let n = NumBox.filled(b)
	let mine = match n 'm'
		empty gives 0
		filled gives 7
	end 'm'
	return mine + words()
end 'main'
```
```exitcode
8
```


<!-- test: third-file-resolves-a-contested-generic-alias-to-the-nameable-one -->
The generic twin of `third-file-resolves-to-the-nameable-declaration`, and the case that pins the whole
visibility tier: `main.maxon` declares no `Slots`, so it may not mean `priv.maxon`'s file-private one and
must resolve to the `export`ed declaration — which is the only one it is allowed to write down. Under the
bare last-wins fallback alone, `main.maxon`'s `push(1500)` would meet `priv.maxon`'s `int(0 to 100)`,
which is the same "a declaration the reader is forbidden to name decided what the reader meant" wrong
answer the ranged form was cured of.
```maxon
// --- file: shared.maxon
export typealias Elem = int(1000 to 2000)
export typealias Slots = Array with Elem

// --- file: priv.maxon
typealias Elem = int(0 to 100)
typealias Slots = Array with Elem

export function fromPriv() returns Elem
	var w = Slots.create()
	w.push(40)
	return try w.get(0) otherwise 0
end 'fromPriv'

// --- file: main.maxon
function main() returns ExitCode
	var w = Slots.create()
	w.push(1500)
	return ((try w.get(0) otherwise 1000) / 100) + fromPriv()
end 'main'
```
```exitcode
55
```


<!-- test: error.cross-file-generic-alias-cycle-is-a-type-cycle -->
⛔ **A COMPILER PANIC, MEASURED WHILE LANDING THIS RUNG.** The mint a contested argument gets is derived
from that argument's instance, so each pass of the contest adds one nesting level — bounded on an acyclic
declaration graph by its depth, and unbounded on a cyclic one. `a.maxon` closes a cycle between its own
`P` and `Q`; the compiler aborted with `7 rounds over 6 declaration(s)`.

⚠ **AND THE CYCLE WALK COULD NOT HAVE CAUGHT IT EITHER.** `buildInstanceArgGraph` resolves a `named`
argument through the bare LAST-WINS map, where `Q` means `main.maxon`'s `Array with P` and `P` means its
`Array with High` — an ACYCLIC view of a program that cycles as soon as each file's declarations are read
as that file's own. So E3091 was owed and unreachable, by a walk that predates this rung. The
non-convergence IS the detection.
```maxon
// --- file: a.maxon
typealias P = Array with Q
typealias Q = Array with P

export function fromA() returns ExitCode
	return 1
end 'fromA'

// --- file: main.maxon
typealias High = int(1000 to 2000)
typealias P = Array with High
typealias Q = Array with P

function main() returns ExitCode
	var q = Q.create()
	return fromA() + q.count()
end 'main'
```
```maxoncstderr
error E3091: <fragment>:3:11: typealias 'P' forms a type cycle: its type arguments refer back to 'P'
```


<!-- test: per-instance-alias-on-a-contested-generic-alias -->
`W.Idx` is a PER-INSTANCE alias, whose identity is keyed on the instance-alias NAME — so on a `W` two
files declare differently it asks the very question the file-less door refuses. ⛔ MEASURED: the compiler
PANICKED out of `Parser.aggregateNameOf`, on a program each file's own declarations describe completely.
The prefix is a source spelling and every caller resolving one has a reading file, so it is resolved as
that file means it; the two callers that structurally cannot (the coercion authority, which is handed two
names, and the file-less type erasure) resolve as a stranger would.
```maxon
// --- file: wrap.maxon
export type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T) returns Self
		return Self{value: value, tag: 0}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

// --- file: a.maxon
typealias Elem = int(0 to 100)
typealias W = Wrapper with Elem

export function fromA() returns W.Idx
	let w = W.create(5)
	return w.getTag()
end 'fromA'

// --- file: main.maxon
typealias Elem = int(1000 to 2000)
typealias W = Wrapper with Elem

function takes(i W.Idx) returns ExitCode
	return i as ExitCode
end 'takes'

function main() returns ExitCode
	let w = W.create(1500)
	return takes(w.getTag()) + (fromA() as ExitCode)
end 'main'
```
```exitcode
0
```

<!-- test: error.contested-generic-alias-as-a-type-argument-is-not-a-panic -->
⛔ **A generic instance's TYPE ARGUMENT is not a declared SLOT, and `settleGenericAliasContest` rewrites
slots.** `Box with Bag` is interned once, by whichever pass first met it — the declaration SWEEP, which
runs before any contest can be known — so its argument stays a bare `named("Bag")`. The settle then
re-interns each declaration SCOPED, and the registry is append-only, so the pre-settle instance survives
beside its two scoped replacements and every whole-program walk over `instancesOfBase` still meets it.
⛔ MEASURED (A3k): once the consume boundary learned to resolve a generic alias, that spelling took
`isManagedOpaqueTypeParamField` AND `noteDestructorUsage`'s opaque-element drop rooting straight into the
file-less door's refusal — two unrelated walks — on a program whose single-file spelling compiles. A
superseded spelling classifies NOTHING (`genericAliasSpellingIsSuperseded`), so the verdict is the one
each file's own declarations earn: the co-owned-trivial reassign refusal, identical to the single-file
program's.
```maxon
// --- file: aboxdef.maxon
export type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function swap(v T)
		self.value = v
	end 'swap'
end 'Box'

// --- file: bhigh.maxon
typealias High = int(1000 to 2000)
typealias Bag = Array with High
typealias BB = Box with Bag

export function fromHigh() returns int
	var b = Bag.create()
	b.push(1500)
	let box = BB.create(b)
	return 1
end 'fromHigh'

// --- file: cmain.maxon
typealias Low = int(0 to 100)
typealias Bag = Array with Low
typealias BB = Box with Bag

function main() returns ExitCode
	var b = Bag.create()
	b.push(40)
	var box = BB.create(b)
	box.swap(Bag.create())
	return fromHigh() - 1
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:8: Unsupported: reassigning the type-parameter field 'value' of 'Box' in a shared generic body, where a trivial-struct instantiation co-owns the field — the box retains a co-owned trivial struct at construction and drops it once at destruction, but a shared-body reassignment cannot drop the old co-owned value (the descriptor-gated single-value drop for a trivial struct is a later slice); reassign the field on a concrete instance, or use a managed element type
```

<!-- test: error.contested-generic-alias-at-the-opaque-copy-gate -->
The twin of the case above at the OTHER reader-free classifier, and this one was reachable BEFORE the
consume boundary learned anything: `typeSupportsDeepClone` carries the identical
`isGenericAlias → genericAliasInstance` arm, and `requireOpaqueArrayCopyable` drives it over the same
`instancesOfBase` walk. ⛔ MEASURED: the compiler PANICKED out of the file-less door. A superseded
spelling refuses nothing, so the refusal that stands is the one the live instantiations earn — the
`Bag` whose element is an OS handle genuinely has no `copyFunc`, while the `int(0 to 100)` spelling in
`cmain.maxon` is a byte blit and earns nothing.

⛔ **THAT UNCOPYABLE ELEMENT WAS A `String` UNTIL G18, AND THE SUBJECT IS UNAFFECTED BY THE SWAP.** What this
case pins is the CONTESTED-ALIAS arm not panicking out of a file-less door; the refusal is only the
observable that proves the arm was walked. A managed-element array is deep-cloneable now (it has a
per-instance one-argument cloner thunk), so the observable had to move to the residue the gate will always
refuse — an OS handle, which cannot be deep-copied by anything.

⚠ **THE REFUSAL IS THE LIBRARY'S SINCE ARRH STRUCK `clone` FROM THE `Array` ROSTER, AND BLAME GIVES IT
THE USER'S SPAN BACK** — `arr.clone()` is the library's own declaration now, so this program is refused by the
OPAQUE copy gate inside that body rather than by the concrete gate at the call, and the sentence printed is
the opaque one. What the refusal is POSITIONED at is the user's own instantiation, with `stdlib/Array.maxon`'s
line kept as a `note:`; `specs-shv2/array-conditional-conformance-withheld.md` explains that relocation and
the blame edge once, for all four cases ARRH touched.
```maxon
// --- file: acontainer.maxon
export type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function duplicate() returns Self
		return Self{ items: self.items.clone() }
	end 'duplicate'
end 'Container'

// --- file: bhandles.maxon
type Handle
	export var f as __ManagedFile

	export static function create(f __ManagedFile) returns Self
		return Self{f: f}
	end 'create'
end 'Handle'

typealias Bag = Array with Handle
typealias NestedContainer = Container with Bag

export function useHandles() returns int
	var sa = Bag.create()
	sa.push(Handle.create(try __ManagedFile.openRead(b"DATA.BIN".managed) otherwise return 0))
	var nc = NestedContainer.create()
	nc.push(sa)
	return 1
end 'useHandles'

// --- file: cmain.maxon
typealias Low = int(0 to 100)
typealias Bag = Array with Low
typealias NestedContainer = Container with Bag

function main() returns ExitCode
	var sa = Bag.create()
	sa.push(7)
	var nc = NestedContainer.create()
	nc.push(sa)
	var dup = nc.duplicate()
	return useHandles() - 1
end 'main'
```
```maxoncstderr
error E2015: <fragment>:30:11: Unsupported: `slice` COPIES each element of an `Array with <type parameter>` field, but this generic type is instantiated with a type whose managed element cannot be deep-cloned — a compiler-owned aggregate or a base-struct-less generic instance with no runtime copy of its own (`__ManagedFile`, a `Vector`), a value held at an interface type, or a generic instance that owns one of those. String / struct / boxed-union / container (`Array with int`, `List with String`, `Array with (Array with String)`) / trivial instantiations, and a declared generic's instance whose own substituted fields are all deep-cloneable (`Box with String`), ARE supported (P1.7 slice 3b-vi-b, W162, W173, G18).
note: stdlib/Array.maxon:165:32: raised inside the library, on behalf of the construct above
```

<!-- test: contested-generic-alias-argument-that-owns-heap-is-not-co-owned-trivial -->
⛔⛔ **A SUPERSEDED SPELLING MUST CLASSIFY NOTHING AT *EVERY* ARGUMENT DOOR, AND THE THIRD DOOR'S
"NOTHING" IS NOT ITS OWN (A3k review).** `typeArgIsCoOwnedTrivial` is `typeIsManaged and not
typeArgIsOwned`; guarding only the OWNED half left the pre-settle `Box with named("N0")` orphan voting
MANAGED-AND-NOT-OWNED, i.e. CO-OWNED TRIVIAL — the manufactured kind this rung exists to abolish,
re-made one spelling over. Every LIVE instantiation of `Box` here OWNS a String, so every scoped
sibling in the by-base bucket votes not-co-owned; the orphan alone carried the refusal, and
`anyInstanceTypeArgHasKind` is an OR. ⛔ MEASURED: `v1.swap(…)` was refused **E2015** "a
trivial-struct instantiation co-owns the field", said of a box that owns a String, while the
byte-identical program whose second file spells its aliases `M0`/`M1` — nothing contested — runs to
exit 0, and so does the INLINE `Box with (Box with S0)` spelling of this very program. Two spellings
disagreeing again, in the direction this rung's thesis forbids.
```maxon
// --- file: adef.maxon
export type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'

export type S1
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S1'

export type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function swap(v T)
		self.value = v
	end 'swap'
end 'Box'

// --- file: bother.maxon
typealias N0 = Box with S1
typealias N1 = Box with N0

export function useOther() returns int
	var w1 = N1.create(N0.create(S1.make("other")))
	w1.swap(N0.create(S1.make("other2")))
	return 1
end 'useOther'

// --- file: cmain.maxon
typealias N0 = Box with S0
typealias N1 = Box with N0

function main() returns ExitCode
	var v0 = N0.create(S0.make("x"))
	var v1 = N1.create(v0)
	v1.swap(N0.create(S0.make("y")))
	return useOther() - 1
end 'main'
```
```exitcode
0
```

<!-- test: contested-generic-alias-argument-that-owns-heap-agrees-with-the-inline-spelling -->
The control that makes the case above a statement about AGREEMENT rather than about one spelling: the
same three files with the outer box spelled INLINE, so no `Box with named("N0")` is ever interned and
no orphan exists. It has always compiled — which is exactly why the alias spelling refusing was a
disagreement and not a policy.
```maxon
// --- file: adef.maxon
export type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'

export type S1
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S1'

export type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function swap(v T)
		self.value = v
	end 'swap'
end 'Box'

// --- file: bother.maxon
typealias N0 = Box with S1
typealias N1 = Box with (Box with S1)

export function useOther() returns int
	var w1 = N1.create(N0.create(S1.make("other")))
	w1.swap(N0.create(S1.make("other2")))
	return 1
end 'useOther'

// --- file: cmain.maxon
typealias N0 = Box with S0
typealias N1 = Box with (Box with S0)

function main() returns ExitCode
	var v1 = N1.create(N0.create(S0.make("x")))
	v1.swap(N0.create(S0.make("y")))
	return useOther() - 1
end 'main'
```
```exitcode
0
```

<!-- test: tuple-alias-over-a-contested-generic-alias -->
⛔⛔ **A USER-DECLARED TUPLE `typealias` HAS AN HONEST DECLARING FILE, AND IT USED TO THROW IT AWAY
(A3v).** A tuple's canonical name is keyed on its ELEMENT types, and `canonicalTupleElement` resolved
each of them through `declaredSlotType` under `CompilerOwnedDeclFilePath` — the STRANGER convention
(N2), which is right for a compiler-SYNTHESIZED tuple (it is declared in no file) and wrong for one
the source wrote a `typealias` for. So `adef.maxon`'s `Pair = (Bag, Num)` was interned over
`bother.maxon`'s `Bag`, and the declaring file could not construct its OWN field.

⛔ MEASURED before the fix: `error E3005: cannot assign '__Tuple2.Array_Num.int' to variable
'Keeper.p' of type '__Tuple2.Array_String.int'` — reported at line 10 of `adef.maxon`, the file that
declares every name in the sentence. Same shape as a contested generic alias in a RETURN type: the
LOSER's own legal program is the one refused.

⚠ The two files' `(Bag, Num)` are now two DIFFERENT tuple types, which is what the language already
says: `Bag` denotes different things in them. A tuple stays STRUCTURAL where the spellings agree —
per-file resolution then lands on identical element types and therefore on one canonical name.
```maxon
// --- file: adef.maxon
typealias Num = int(0 to 125)
typealias Bag = Array with Num
typealias Pair = (Bag, Num)

public type Keeper
	export var p as Pair
	export static function make() returns Self
		var b = Bag.create()
		b.push(7)
		return Self{p: (b, 1)}
	end 'make'
end 'Keeper'

export function useA() returns Num
	var k = Keeper.make()
	return try k.p.0.get(0) otherwise 0
end 'useA'

// --- file: bother.maxon
typealias Bag = Array with String

export function useB() returns int
	var b = Bag.create()
	b.push("x")
	return b.count() as int
end 'useB'

// --- file: cmain.maxon
function main() returns ExitCode
	return (useA() + useB()) as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: contested-generic-alias-in-a-cross-file-return-type -->
**A CROSS-FILE RETURN TYPE SPELLED WITH A CONTESTED GENERIC ALIAS RESOLVES IN THE CALLEE'S FILE, AND
THIS CASE EXISTS TO KEEP IT THAT WAY.** `makeBag` returns `Bag`, and the CALLER is itself a
contestant that means something else by that name — the shape that would bite hardest if the return
type were resolved against the reader's file. `ProgramSignatures.funcReturnDeclFiles` is the
per-callee declaring-file index that decides it, and `copyFreeFunctionSweepEntries` carries it across
a contest refile; nothing else pins either, so a regression in them would have been silent.

The element type is what discriminates: `theirs.get(1)` is an `int` only if `Bag` meant adef's
`Array with Num`. Had it resolved against `cmain.maxon`, the value would be a `String` and the `as
ExitCode` would not compile.
```maxon
// --- file: adef.maxon
typealias Num = int(0 to 125)
typealias Bag = Array with Num

export function makeBag() returns Bag
	var b = Bag.create()
	b.push(4)
	b.push(9)
	return b
end 'makeBag'

// --- file: cmain.maxon
typealias Bag = Array with String

function main() returns ExitCode
	var mine = Bag.create()
	mine.push("x")
	var theirs = makeBag()
	return ((try theirs.get(1) otherwise 0) * 10 + mine.count()) as ExitCode
end 'main'
```
```exitcode
91
```

<!-- test: tuple-alias-over-a-contested-generic-alias-either-order -->
⭐⭐ **THE CURE ABOVE IS A STATEMENT ABOUT THE PROGRAM, NOT ABOUT THE FILESYSTEM, AND THIS IS THE CASE
THAT SAYS SO (A3v review).** The tuple fix keys a SWEEP-minted spelling per (spelling, file), and the
first file to mint one keeps the unsuffixed key — so *which* file that is comes off the fold order,
which is `Directory.list` order, which is NTFS index order on Windows and APFS hash order on macOS
(defect-board row `A5a`). A cure whose answer moved with that would be the same wrong answer wearing
a different hat.

Both files here declare their OWN `Bag` and their OWN tuple over it, and each constructs its own
field — so whichever folds first, the other is the one that would fail. `zdef`/`abother` sort the
opposite way to `adef`/`bother` above, which is what makes the pair a two-order test rather than one
program written twice.

⛔ MEASURED against the merge-base compiler, BOTH orders red and each blaming the file that folded
SECOND: `E3005: cannot assign '__Tuple2.Array_Num.int' to variable 'Holder.q' of type
'__Tuple2.Array_String.int'` one way, and the same sentence with the two type names swapped the
other. On this tree both orders answer 72, and the emitted symbol sets are identical between them —
the ordinal never reaches a name anything renders or emits, because `sweepScopedTupleName` returns
early once `allFilesFolded` and every name crossing out of the index is canonicalized past it.
```maxon
// --- file: abother.maxon
typealias Bag = Array with String
typealias Pair = (Bag, int)

public type Holder
	export var q as Pair
	export static function make() returns Self
		var b = Bag.create()
		b.push("xy")
		return Self{q: (b, 1)}
	end 'make'
end 'Holder'

export function useB() returns int
	let h = Holder.make()
	return (try h.q.0.get(0) otherwise "").count() as int
end 'useB'

// --- file: cmain.maxon
function main() returns ExitCode
	return (useA() * 10 + useB()) as ExitCode
end 'main'

// --- file: zdef.maxon
typealias Num = int(0 to 125)
typealias Bag = Array with Num
typealias Pair = (Bag, int)

public type Keeper
	export var p as Pair
	export static function make() returns Self
		var b = Bag.create()
		b.push(7)
		return Self{p: (b, 1)}
	end 'make'
end 'Keeper'

export function useA() returns int
	var k = Keeper.make()
	return (try k.p.0.get(0) otherwise 0) as int
end 'useA'
```
```exitcode
72
```


<!-- test: a-sized-vector-keeps-its-element-count-when-its-element-is-contested -->
⭐ **A `Vector`'s SIZE is part of its type, and the per-file rescoping a contest causes must carry it.**
When two files disagree about a ranged alias, every generic instance over that element is re-interned
once per reading file (`SignatureIndex.fileScopedInstance`) so each file gets its own. That re-intern is
keyed on `(base, args, fixedSize)` — and it was called WITHOUT the third, so a `Vector with 8 W` came back
as a SIZELESS `Vector`: `create()` produced a zero-length vector and every index was out of bounds.
MEASURED on exactly this program before the fix — `panic at lib.maxon:11: a Vector with 8 has an index 7`
under shv2, against the bootstrap's `42 8 1779033703`.

⚠ **NO `Array` CASE CAN CATCH IT**, which is why this one is a `Vector`: every base but `Vector` is
unsized, so `fixedSize` is already `NoFixedSize` there and dropping it is a no-op. The contest cases in
`bytearray-element-size.md` are all `Array with Byte` and stayed green throughout.
```maxon
// --- file: lib.maxon
typealias W = int(i64.min to i64.max)
typealias WVec = Vector with 8 W

export function wideCount() returns W
	var v = WVec.create()
	return v.count()
end 'wideCount'

export function wideSlot() returns W
	var v = WVec.create()
	try v.set(7, value: 1779033703) otherwise panic("a Vector with 8 has an index 7")
	return try v.get(7) otherwise panic("a Vector with 8 has an index 7")
end 'wideSlot'

// --- file: main.maxon
typealias W = int(0 to 100)

function double(w W) returns W
	return w * 2
end 'double'

function main() returns ExitCode
	print("{double(21)} {wideCount()} {wideSlot()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42 8 1779033703
```
