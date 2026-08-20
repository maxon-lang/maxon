---
feature: cross-file-overload-set
status: stable
keywords: [overload, cross-file, module, visibility, duplicate, return-type]
category: type-system
---

# One Free-Function Name, Two Files

## Documentation

A free-function name declared by **two files of one directory** is an **overload set**, exactly as two
declarations of one name inside a single file are. Which one a call means is decided by its arguments and by
what the calling file can NAME, at the call.

```maxon
// --- Console.maxon
function stripTrailingCR(bytes ByteArray) returns ByteArray

// --- Subprocess.maxon
function stripTrailingCR(line String) returns String
```

Two modules of one library must each be able to carry a private helper of the same name. Before this rule
they could not: both registered the bare name and the program was refused.

### Why the FILE boundary used to decide this, and no longer does

A declaration's **registration name** is minted where the declaration is parsed, and a parser is a pure
function of its own file. So a later overload of a name the same file already claimed registers as
`pick#bool`, while a later overload of a name **another** file claimed had no way to know it was later at
all — both registered the bare name.

The whole-program declaration sweep is what closes it: it walks every `function` declaration in the program
before any file is parsed, so it — and only it — can say *"this name is declared by more than one FILE of
this directory"*. It records that, and each file's parse reads the answer back rather than re-deriving it.
It is the same construction `extension-overload-set.md` describes for a method two `extension` declarations
publish, one declaration kind over.

**More than one DIRECTORY is a different rule with a different answer** — each such declaration is
registered under its directory-qualified spelling, and `namespace-qualified-resolution.md` owns it.

### When the name is contested, NOBODY keeps the bare spelling

An uncontested declaration registers under its own name, as it always did. A **contested** one registers
under its parameter-type spelling — and so does the first of them:

- two declarations whose parameters differ mint **different** suffixes and are two live overloads;
- two declarations whose parameters are the **same** mint the **same** suffix and collide, which is the
  `E3006` a genuine redeclaration has always earned.

"The same parameters" means the same source SPELLING, which is the same thing every overload key in this
compiler means by it. The bootstrap flattens a typealias before comparing and this does not, so two
overloads written at two spellings of one underlying type are two registrations here and one there — both
compilers refuse such a program, at different places and with different codes. Measured, on two root-level
files each declaring `export function f(a Integer = 1)` / `f(a Count = 1)`: the bootstrap answers
`E3006 Duplicate function 'f'` at the declaration, and this compiler answers `E3007` at the call.

### A call reads the return type of the overload it MEANS

The reason this needed more than a registration rule is that **a call's result type is fixed while its own
file is parsed** — it decides the machine type of the result, which register file it travels in, and whether
a scope-exit drop is enrolled for it — while the overload is resolved a whole pass later. A whole-program
index that kept ONE return type per NAME therefore typed every call to an overloaded name from whichever
declaration it read last.

So the sweep publishes each declaration's **parameter-type spellings** beside its return type, and the parse
asks which member the call means before it types the result. Two boundaries remain, and both DECLINE loudly
rather than guessing: a parameter spelled as anything but a type NAME (a `with` instantiation, a tuple, a
function type) is not read, and two members that fit a call equally are not chosen between.

## Tests

<!-- test: two-files-of-one-directory-each-carry-a-private-helper-of-one-name -->
The headline case, and the one this rule exists for. Each file declares a file-private `pick` and calls its
own; nothing about either file is visible to the other. Before this rule the program was refused
**`E3005: a.maxon:6:2: Cannot return 'int' from function declared to return 'String'`** — blaming the file
that is CORRECT, because `main.maxon`'s `pick` was the last declaration the sweep read and so every call to
the name in the program, `a.maxon`'s included, was typed to return a `Cnt`. MEASURED against the bootstrap,
which forms the same overload set and answers the same thing: `hi` then `1`, exit 0.
```maxon
// --- file: a.maxon
function pick(s String) returns String
	return s
end 'pick'

export function useA(s String) returns String
	return pick(s)
end 'useA'

// --- file: main.maxon
typealias Cnt = int(0 to 100)

function pick(n Cnt) returns Cnt
	return n
end 'pick'

function main() returns ExitCode
	print("{useA("hi")}\n")
	print("{pick(1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi
1
```

<!-- test: two-private-helpers-of-one-name-returning-different-managed-types -->
The `stripTrailingCR` shape, which is what blocks `stdlib/Subprocess.maxon` from being listed beside
`stdlib/Console.maxon`: two files of one module, one private helper name, parameter types that differ AND
return types that differ — and both of them MANAGED, so the divergence is not one any later retype could
repair. A result typed from the wrong member here is a leak in one direction and a drop of a record the
callee never wrote in the other, which is why the pair is pinned by `exitcode` and not by `stdout` alone.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

function stripTrailingCR(bytes ByteArray) returns ByteArray
	return bytes
end 'stripTrailingCR'

export function byteCount(b ByteArray) returns Integer
	return stripTrailingCR(b).count()
end 'byteCount'

// --- file: main.maxon
function stripTrailingCR(line String) returns String
	return line
end 'stripTrailingCR'

function main() returns ExitCode
	print("{byteCount(b"ab\r")}\n")
	print("{stripTrailingCR("xy")}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
xy
```

<!-- test: two-exported-overloads-in-two-files-resolve-by-argument-type -->
Visibility is not the whole answer, and this is the case that says so: both declarations are `export`ed, so
the calling file can name BOTH, and only their PARAMETER TYPES separate them. The set disagrees about its
return type in the way that matters most — one member returns a ranged int and the other a `String` — so the
result of each call is typed from the member its own argument means. MEASURED against the bootstrap: `42`
then `ok!`.
```maxon
// --- file: a.maxon
typealias Cnt = int(0 to 100)

export function widen(n Cnt) returns Cnt
	return n + 1
end 'widen'

// --- file: b.maxon
export function widen(s String) returns String
	return "{s}!"
end 'widen'

// --- file: main.maxon
function main() returns ExitCode
	print("{widen(41)}\n")
	print("{widen("ok")}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
ok!
```

### A genuine redeclaration is still refused


<!-- test: error.one-signature-declared-by-two-files-of-one-directory -->
⛔ **THE NEGATIVE CONTROL.** Two files declaring one name with the SAME parameter spelling are not an
overload set: they render the same suffix, claim one registration name and collide at the merge — the
refusal falls out of the mint rather than out of a second check written beside it. MEASURED against the
bootstrap, which refuses the same program: `E3006: Duplicate function 'pick'`.

⚠ The name the message quotes is one **neither declaration wrote**, because a contested name is registered
under its suffix and never bare. That is the same shape a contested `extension` method has, and it earns the
same extra sentence: told only `'pick#String'`, an author would search for a string that appears nowhere in
their source.
```maxon
// --- file: a.maxon
function pick(s String) returns String
	return s
end 'pick'

export function useA(s String) returns String
	return pick(s)
end 'useA'

// --- file: main.maxon
function pick(s String) returns String
	return "x{s}"
end 'pick'

function main() returns ExitCode
	print("{useA("a")} {pick("b")}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:12:10: duplicate definition of function 'pick#String' — 'pick' is declared as a free function in more than one FILE of its directory, so every one of those declarations is registered under its parameter-type spelling, and two of them spell the same parameters. Give the overloads distinct parameter types, or distinct names
```

<!-- test: error.two-spellings-of-one-type-are-ambiguous-at-the-call -->
Two overloads written at two SPELLINGS of one underlying type both register — the suffix is the source
spelling, pre-resolution — so the program is refused at the CALL, which cannot tell them apart, rather than
at the declaration. The bootstrap flattens the alias and refuses the same program at the declaration
instead; both refuse it, and no canonical spec pins either site.

⛔ **AND IT MUST EARN EXACTLY ONE DIAGNOSTIC.** A contested set has no member under the bare name, so an op
left naming it reaches `SemanticCheck.validateCall` and was reported **`E3004: call to undefined function
'f'`** — about a name declared twice over — underneath the E3007 that had just explained the real fault.
The ambiguous arm now points the op at a declared member exactly as the no-match arm does.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export function f(a Integer) returns Integer
	return a
end 'f'

// --- file: b.maxon
typealias Count = int(i64.min to i64.max)

export function f(a Count) returns Count
	return a + 1
end 'f'

// --- file: main.maxon
function main() returns ExitCode
	return f(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3007: <fragment>:18:9: Ambiguous overload for 'f': multiple overloads match. Candidates: (a int), (a int)
```

### The parse-time decider's own boundary

<!-- test: error.a-set-the-parse-time-decider-cannot-settle -->
⛔ **WHAT THE DECIDER DECLINES, IT DECLINES LOUDLY.** It reads a parameter type only when the source spells
it as a single type NAME: a `with` instantiation, a tuple, a function type and — much the commonest — a
RANGED INT (`n int(0 to 100)`) are all compound. Reading one registers it whole-program, and every
registration the declaration sweep makes moves the instance ids of every later one, which decide mangled
instance names. So a set with a compound-typed member cannot be settled at the parse,
the result falls back to the one return type the index keeps per NAME — here the `String` of the last
declaration — and the call, which resolves to the void member, is refused rather than typed from it.

⚠ The declaration ORDER is load-bearing and is not incidental: with the two swapped, the by-name fallback
records the void member, the call is typed `void`, that is the answer this call actually gets, and the
program compiles. The refusal is about the fallback being WRONG for this call, never about the set.
```maxon
typealias Integer = int(i64.min to i64.max)

function pick(n Integer)
	print("n{n}\n")
end 'pick'

function pick(pair (Integer, Integer)) returns String
	return "p{pair.0}"
end 'pick'

function main() returns ExitCode
	pick(7)
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:2: the overloads of 'pick' do not agree on their return type ('String' and 'void'), and this call needed the one they disagree about. A call's result type is fixed while its file is parsed, from the parameter types the whole-program declaration sweep publishes — and they did not settle which overload this call means, so the result was typed from the single return type that index keeps per NAME. Only a difference between plain scalars can be corrected once the overload is known, a whole pass later. Make the overloads return the same type, or spell every overload's parameters as type NAMES that this call's arguments match in exactly one of them
```

### Two facts the decider reads, and what each cost before it read them right

<!-- test: two-exported-overloads-returning-different-aggregates-resolve-by-argument-type -->
⛔⛔ **A SET RETURNING TWO DIFFERENT AGGREGATES USED TO READ AS *AGREEING*, AND THAT WAS A SILENT WRONG
ANSWER.** Two struct returns are both `structRef` and two generic-instance returns are both
`genericInstance`, so a tag-only test declared the set agreeing, the decider declined, and
`returnTypeOf`'s last-wins answer typed the call from the OTHER member —
`SemanticCheck.requireOverloadResultTagAgrees` is blind on the same rule, so nothing reported it either.

`Box` and `Bag` declare the same two field NAMES in OPPOSITE orders, which is what makes the failure a
number rather than a diagnostic: read at the wrong type's offsets, `b.first` finds the other field.
MEASURED before the fix, on the same-file spelling of this program: **22**, compiled clean, no diagnostic
at any stage, where the bootstrap prints **11**. Both members are `export`ed on purpose — with them
file-private, visibility alone would pick the member and the case would never enter the quadrant it is
about. MEASURED against the bootstrap: `11` then `44`.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export type Box
	export var first as Integer
	export var second as Integer

	export static function of(first Integer, second Integer) returns Self
		return Self{first: first, second: second}
	end 'of'
end 'Box'

export function pick(n Integer) returns Box
	return Box.of(n + 10, second: 22)
end 'pick'

// --- file: b.maxon
typealias Count = int(i64.min to i64.max)

export type Bag
	export var second as Count
	export var first as Count

	export static function of(second Count, first Count) returns Self
		return Self{second: second, first: first}
	end 'of'
end 'Bag'

export function pick(s String) returns Bag
	return Bag.of(s.count() + 33, first: 44)
end 'pick'

// --- file: main.maxon
function main() returns ExitCode
	let b = pick(1)
	print("{b.first}\n")
	let g = pick("zz")
	print("{g.first}\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
11
44
```

<!-- test: a-contested-generic-alias-return-in-an-overload-set-resolves-in-the-callee-file -->
⛔⛔ **THE PER-DECLARATION RETURN TYPE IS A SECOND COPY OF A FACT THREE WHOLE-PROGRAM PASSES RE-DECIDE, AND
THIS IS THE ONE OF THEM THE READ DOOR CANNOT REPRODUCE.** `typealias-file-scope.md`'s
`contested-generic-alias-in-a-cross-file-return-type` is this program with `makeBag` declared ONCE: `Bag` is
spelled by two files over two different elements, so it is CONTESTED, and N3's rewrite resolves a recorded
return type in the file that declared the FUNCTION. Give `makeBag` an overload that disagrees with it about
what it returns and the parse-time decider takes over the typing of the call — from a copy the rewrite did
not reach, which resolves `Bag` in the CALLER's scope instead. That is exactly the bug N3's rewrite exists to
correct, reintroduced one table over.

`theirs.get(1)` is an `int` only if `Bag` meant `adef.maxon`'s `Array with Num`; had it resolved against
`cmain.maxon` the value would be a `String` and the arithmetic would not compile. The three rewrites now walk
`overloadedDecls` in the same act and under the same rule — and against each declaration's OWN file, which is
strictly better than the one `funcReturnDeclFiles` keeps per key, since that column is last-wins and an
overload set may span two files.
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

export function makeBag(tag String) returns String
	return "t{tag}"
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
