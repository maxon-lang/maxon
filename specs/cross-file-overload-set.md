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

Two modules of one library must each be able to carry a private helper of the same name.

### Why the FILE boundary cannot decide this alone

A declaration's **registration name** is minted where the declaration is parsed, and a parser is a pure
function of its own file. So a later overload of a name the same file already claimed registers as
`pick#bool`, while a later overload of a name **another** file claimed has no way to know it is later at
all — both would register the bare name.

The whole-program declaration sweep is what closes it: it walks every `function` declaration in the program
before any file is parsed, so it — and only it — can say *"this name is declared by more than one FILE of
this directory"*. It records that, and each file's parse reads the answer back rather than re-deriving it.
It is the same construction `extension-overload-set.md` describes for a method two `extension` declarations
publish, one declaration kind over.

**More than one DIRECTORY is a different rule with a different answer** — each such declaration is
registered under its directory-qualified spelling, and `namespace-qualified-resolution.md` owns it.

### When the name is contested, NOBODY keeps the bare spelling

An uncontested declaration registers under its own name. A **contested** one registers
under its parameter-type spelling — and so does the first of them:

- two declarations whose parameters differ mint **different** suffixes and are two live overloads;
- two declarations whose parameters are the **same** mint the **same** suffix and collide, which is the
  `E3006` a genuine redeclaration earns.

"The same parameters" means the same source SPELLING, which is the same thing every overload key in this
compiler means by it. Two overloads written at two spellings of one underlying type are two
registrations: two root-level files each declaring `export function f(a Integer = 1)` /
`f(a Count = 1)` are answered with `E3007` at the call.

### A call reads the return type of the overload it MEANS

The reason this needed more than a registration rule is that **a call's result type is fixed while its own
file is parsed** — it decides the machine type of the result, which register file it travels in, and whether
a scope-exit drop is enrolled for it — while the overload is resolved a whole pass later. A whole-program
index that kept ONE return type per NAME would type every call to an overloaded name from whichever
declaration it read last.

So the sweep publishes each declaration's **parameter-type spellings** beside its return type, and the parse
asks which member the call means before it types the result. It reads a parameter spelled as a type NAME, a
dotted `<Type>.<member>` name or a tuple. Two members that fit a call equally are not chosen between: the call
is ambiguous, `E3007`.

## Tests

<!-- test: two-files-of-one-directory-each-carry-a-private-helper-of-one-name -->
The headline case, and the one this rule exists for. Each file declares a file-private `pick` and calls its
own; nothing about either file is visible to the other. Typing every call to the name from the last
declaration the sweep read (`main.maxon`'s, returning a `Cnt`) would refuse `a.maxon` — the file that is
CORRECT — with an `E3005` on its `return`. The program prints `hi` then `1`, exit 0.
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
export typealias Integer = int(i64.min to i64.max)

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
result of each call is typed from the member its own argument means. It prints `42`
then `ok!`.
```maxon
// --- file: a.maxon
export typealias Cnt = int(0 to 100)

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

### A genuine redeclaration is refused


<!-- test: error.one-signature-declared-by-two-files-of-one-directory -->
⛔ **THE NEGATIVE CONTROL.** Two files declaring one name with the SAME parameter spelling are not an
overload set: they render the same suffix, claim one registration name and collide at the merge — the
refusal falls out of the mint rather than out of a second check written beside it.

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
at the declaration.

⛔ **AND IT MUST EARN EXACTLY ONE DIAGNOSTIC.** A contested set has no member under the bare name, so an op
left naming it would reach `SemanticCheck.validateCall` and be reported **`E3004: call to undefined function
'f'`** — about a name declared twice over — underneath the E3007 that explains the real fault. The
ambiguous arm points the op at a declared member exactly as the no-match arm does.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

export function f(a Integer) returns Integer
	return a
end 'f'

// --- file: b.maxon
export typealias Count = int(i64.min to i64.max)

export function f(a Count) returns Count
	return a + 1
end 'f'

// --- file: main.maxon
function main() returns ExitCode
	return f(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3007: <fragment>:18:9: Ambiguous overload for 'f': multiple overloads match. Candidates: (a Integer), (a Count)
```

### A tuple parameter is read

<!-- test: a-void-overload-beside-a-tuple-taking-one-resolves-by-argument-type -->
⛔ **THE DECLARATION ORDER IS THE TRAP.** The `String` member is declared LAST, so the one return type the
index keeps per NAME is `String`, and a call typed from it would hand the void member's call a result the
callee never writes. The decider reads the tuple parameter, finds that `7` cannot stand at it, and settles
the call on the void member before its result is typed. It prints `n7`, exit 0.
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
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n7
```

### A member the caller cannot name

<!-- test: error.a-void-member-the-caller-cannot-name-is-refused -->
The decider scores only the members the calling file may name, so a call whose one fitting member is
file-private to another file leaves it undecided, and the result is typed from the `String` of the last
declaration the sweep read. Resolution still binds the call to that void member, and the call is refused
for the one fault it has: the member is not exported. The result type the call was given is not reconciled
against a member the call may not name.
```maxon
// --- file: a.maxon
typealias Count = int(0 to 100)

function pick(n Count)
	print("n{n}\n")
end 'pick'

// --- file: main.maxon
typealias Integer = int(i64.min to i64.max)

function pick(pair (Integer, Integer)) returns String
	return "p{pair.0}"
end 'pick'

function main() returns ExitCode
	pick(7)
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:17:2: function 'pick#Count' is not exported
```

### A parameter type the decider does not read

<!-- test: error.a-set-with-a-member-the-decider-cannot-read-is-refused -->
The decider reads a qualified parameter type of one namespace segment and no deeper, so `lib.fmt.Score`
leaves the void member unscored and the whole set undecided. The result is typed from the `String` of the
last declaration the sweep read, resolution binds the call to the void member, and the call is refused
rather than handed a result its callee never writes.
```maxon
// --- file: lib/fmt/score.maxon
public typealias Score = int(0 to 100)

// --- file: main.maxon
typealias Integer = int(i64.min to i64.max)

function pick(n lib.fmt.Score)
	print("n{n}\n")
end 'pick'

function pick(pair (Integer, Integer)) returns String
	return "p{pair.0}"
end 'pick'

function main() returns ExitCode
	pick(7)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:17:2: the overloads of 'pick' do not agree on their return type ('String' and 'void'), and this call needed the one they disagree about. A call's result type is fixed while its file is parsed, from the parameter types the whole-program declaration sweep publishes — and they did not settle which overload this call means, so the result was typed from the single return type that index keeps per NAME. Only a difference between plain scalars can be corrected once the overload is known, a whole pass later. Make the overloads return the same type, or spell every overload's parameters as type NAMES that this call's arguments match in exactly one of them
```

### Two facts the decider reads

<!-- test: two-exported-overloads-returning-different-aggregates-resolve-by-argument-type -->
⛔⛔ **A SET RETURNING TWO DIFFERENT AGGREGATES MUST NOT READ AS *AGREEING* — THAT IS A SILENT WRONG
ANSWER.** Two struct returns are both `structRef` and two generic-instance returns are both
`genericInstance`, so a tag-only test would declare the set agreeing, the decider would decline, and
`returnTypeOf`'s last-wins answer would type the call from the OTHER member —
`SemanticCheck.requireOverloadResultTagAgrees` is blind on the same rule, so nothing would report it either.

`Box` and `Bag` declare the same two field NAMES in OPPOSITE orders, which is what makes the failure a
number rather than a diagnostic: read at the wrong type's offsets, `b.first` finds the other field and
prints **22**, with no diagnostic at any stage, where the answer is **11**. Both members are `export`ed on
purpose — with them
file-private, visibility alone would pick the member and the case would never enter the quadrant it is
about. It prints `11` then `44`.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

public type Box
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
export typealias Count = int(i64.min to i64.max)

public type Bag
	export var second as Count
	export var first as Count

	export static function of(second Count, first Count) returns Self
		return Self{second: second, first: first}
	end 'of'
end 'Bag'

export function pick(s String) returns Bag
	return Bag.of((s.count() as Count) + 33, first: 44)
end 'pick'

// --- file: main.maxon
function main() returns ExitCode
	let b = pick(1)
	print("{b.first}\n")
	let g = pick("zz")
	print("{g.first}\n")
	return 0
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
spelled by two files over two different elements, so it is CONTESTED, and the whole-program rewrite resolves
a recorded return type in the file that declared the FUNCTION. Give `makeBag` an overload that disagrees
with it about what it returns and the parse-time decider takes over the typing of the call — from a copy the
rewrite must reach too, or `Bag` resolves in the CALLER's scope: the bug the rewrite exists to correct,
reintroduced one table over.

`theirs.get(1)` is an `int` only if `Bag` meant `adef.maxon`'s `Array with Num`; had it resolved against
`cmain.maxon` the value would be a `String` and the arithmetic would not compile. The three rewrites walk
`overloadedDecls` in the same act and under the same rule — and against each declaration's OWN file, which is
strictly better than the one `funcReturnDeclFiles` keeps per key, since that column is last-wins and an
overload set may span two files.
```maxon
// --- file: adef.maxon
export typealias Num = int(0 to 125)
export typealias Bag = Array with Num

export function makeBag() returns Bag
	var b = Bag.create()
	b.push(4)
	b.push(9)
	return b
end 'makeBag'

public function makeBag(tag String) returns String
	return "t{tag}"
end 'makeBag'

// --- file: cmain.maxon
typealias Bag = Array with String

function main() returns ExitCode
	var mine = Bag.create()
	mine.push("x")
	var theirs = makeBag()
	return ((try theirs.get(1) otherwise 0) * 10 + (mine.count() as Num)) as ExitCode
end 'main'
```
```exitcode
91
```
