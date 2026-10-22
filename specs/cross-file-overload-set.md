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

"The same parameters" means the same parameter DECLARATIONS: a parameter's type is the declaration its
name resolves to, which is what every overload key in this compiler means by it. Two aliases of one
underlying type are two declarations, so overloads written over them are two registrations: two root-level
files each declaring `export function f(a Integer = 1)` / `f(a Count = 1)` are answered with `E3007` at the
call. One name written in two files over two file-private declarations is two types as well.

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
⛔ **THE NEGATIVE CONTROL.** Two files declaring one name with the SAME parameter types are not an
overload set: they render the same suffix, claim one registration name and collide at the merge — the
refusal falls out of the mint rather than out of a second check written beside it.

⚠ A contested name is registered under its parameter types, never bare. The message names the member in
source form, `'pick(String)'`, and earns the same extra sentence a contested `extension` method does, saying
where the collision comes from.
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
error E3006: <fragment>:12:10: duplicate definition of function 'pick(String)' — 'pick' is declared as a free function in more than one FILE of its directory, so every one of those declarations is registered under its parameter-type spelling, and two of them spell the same parameters. Give the overloads distinct parameter types, or distinct names
```

<!-- test: two-files-each-overload-on-their-own-private-alias-of-one-spelling -->
One spelling is not one type. Each file's `Limit` is its own file-private declaration, so the two `f`s take
two different parameter types: two overloads, each called from its own file.
```maxon
// --- file: a.maxon
typealias Limit = int(0 to 9)

function f(x Limit) returns Limit
	return x + 1
end 'f'

export function useA() returns ExitCode
	return f(2) as ExitCode
end 'useA'

// --- file: main.maxon
typealias Limit = int(0 to 9)

function f(x Limit) returns Limit
	return x * 2
end 'f'

function main() returns ExitCode
	print("{useA()} {f(4)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 8
```

<!-- test: error.two-spellings-of-one-type-are-ambiguous-at-the-call -->
Two overloads written at two ALIASES of one underlying type both register — each parameter is identified by
the alias declaration it names, not by the type underneath — so the program is refused at the CALL, which
cannot tell them apart, rather than at the declaration.

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
error E3008: <fragment>:17:2: function 'pick(Count)' is not exported
```

<!-- test: error.a-member-over-a-generic-instance-the-caller-cannot-name-is-refused-in-source-form -->
The same refusal when the hidden member's parameter is a generic instance over an alias the file contests
with the library's `Count`. The refusal names the member by the alias its declaration wrote.
```maxon
// --- file: a.maxon
typealias Count = int(0 to 100)
typealias Counts = Array with Count

function pick(c Counts)
	print("c{c.count()}\n")
end 'pick'

// --- file: main.maxon
typealias Integer = int(i64.min to i64.max)

function pick(pair (Integer, Integer)) returns String
	return "p{pair.0}"
end 'pick'

function main() returns ExitCode
	pick([7])
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:18:2: function 'pick(Counts)' is not exported
```

<!-- test: error.a-generic-member-the-caller-cannot-name-is-refused -->
The hidden member is itself generic, and the refusal names it as its declaration spells it.
```maxon
// --- file: a.maxon
function pick(n T) uses T
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
error E3008: <fragment>:15:2: function 'pick' is not exported
```

<!-- test: a-member-the-caller-cannot-name-does-not-refuse-a-call-a-nameable-member-serves -->
Visibility filters the set before scoring. `a.maxon`'s file-private `pick` takes exactly the argument's
`Count` and would outscore `main.maxon`'s `pick`, which takes it widened to `Integer` (`Count` implements
`Integer`), but `main.maxon` cannot name it, so its call means its own `pick`. It prints `1007 5`, exit 0.
```maxon
// --- file: types.maxon
export typealias Integer = int(0 to 100000)
export typealias Count = int(0 to 100) implements Integer

// --- file: a.maxon
function pick(n Count) returns Count
	return n
end 'pick'

export function viaA(n Count) returns Count
	return pick(n)
end 'viaA'

// --- file: main.maxon
function pick(n Integer) returns Integer
	return n + 1000
end 'pick'

function main() returns ExitCode
	let c = 7 as Count
	print("{pick(c)} {viaA(5)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1007 5
```

<!-- test: a-member-the-calling-file-cannot-name-is-not-bound-by-a-global-initializer -->
A global initializer's call is made by the compiler's `__module_init`, which no file wrote, so the files it may not name are judged against the file the initializer is written in. `main.maxon` cannot name `types.maxon`'s file-private `create(n Count)`, which would outscore the exported `create(n Integer)` for the `Count` it passes, so `var y = Rec.create(7 as Count)` means the exported member, as the same call in a function body does. It prints `1007 1005`, exit 0.
```maxon
// --- file: types.maxon
export typealias Integer = int(0 to 100000)
export typealias Count = int(0 to 100) implements Integer

export type Rec
	export var v as Integer

	static function create(n Count) returns Self
		return Self{v: n}
	end 'create'

	export static function create(n Integer) returns Self
		return Self{v: n + 1000}
	end 'create'

	export static function create(s String) returns Self
		return Self{v: s.byteLength() as Integer}
	end 'create'
end 'Rec'

// --- file: a.maxon
export function viaA(n Count) returns Integer
	return Rec.create(n).v
end 'viaA'

// --- file: main.maxon
var y = Rec.create(7 as Count)

function main() returns ExitCode
	print("{y.v} {viaA(5)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1007 1005
```

<!-- test: error.a-member-only-a-hidden-overload-serves-is-refused-in-a-global-initializer -->
`types.maxon`'s `create(n Count)` is file-private and its `create(s String)` is exported. `main.maxon` cannot
name the first, so the initializer's overload choice is narrowed to the second, which fits no `Count`; the choice
then widens to the whole set and binds the hidden member, and the initializer is refused for naming it, as the same
call in a function body is.
```maxon
// --- file: types.maxon
export typealias Integer = int(0 to 100000)
export typealias Count = int(0 to 100) implements Integer

export type Rec
	export var v as Integer

	static function create(n Count) returns Self
		return Self{v: n}
	end 'create'

	export static function create(s String) returns Self
		return Self{v: s.byteLength() as Integer}
	end 'create'
end 'Rec'

// --- file: main.maxon
var y = Rec.create(7 as Count)

function main() returns ExitCode
	print("{y.v}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:19:9: function 'Rec.create' is not exported
```

<!-- test: error.a-global-initializer-call-to-a-set-whose-every-member-is-hidden-is-refused-once -->
Every `create` of `Rec` is file-private to `types.maxon`, so `main.maxon` can name none of them and its
initializer's call is refused once, as the written name, not again as the member the choice binds.
```maxon
// --- file: types.maxon
export typealias Integer = int(0 to 100000)
export typealias Count = int(0 to 100) implements Integer

export type Rec
	export var v as Integer

	static function create(n Count) returns Self
		return Self{v: n}
	end 'create'

	static function create(n Integer) returns Self
		return Self{v: n + 1000}
	end 'create'
end 'Rec'

// --- file: main.maxon
var y = Rec.create(7 as Count)

function main() returns ExitCode
	print("{y.v}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:19:9: function 'Rec.create' is not exported
```

<!-- test: a-defaulted-parameter-of-a-factory-in-another-file-fills-in-a-global-initializer -->
`main.maxon`'s initializer omits `create`'s defaulted `n`, so the compiler calls the default's helper, which `types.maxon` declares. The helper is the compiler's call and no name `main.maxon` wrote, so the initializer is not refused for a file that cannot name it. It prints `6`.
```maxon
// --- file: types.maxon
export typealias Integer = int(0 to 100000)

export type Rec
	export var v as Integer

	export static function create(n Integer = 5) returns Self
		return Self{v: n + 1}
	end 'create'
end 'Rec'

// --- file: main.maxon
var r = Rec.create()

function main() returns ExitCode
	print("{r.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: a-field-default-built-by-a-helper-in-another-file-fills-in-a-global-initializer -->
`Bag.items` is defaulted by a call, which `types.maxon` declares as a helper. `main.maxon`'s initializer builds a `Bag` without naming that helper, so the compiler's call to it is not refused for a file that cannot name it. It prints `3 0`.
```maxon
// --- file: types.maxon
export typealias Integer = int(0 to 100000)
export typealias IntArray = Array with Integer

export type Bag
	export var items = IntArray.create()
	export var tag as Integer

	export static function create(tag Integer) returns Self
		return Self{tag: tag}
	end 'create'
end 'Bag'

// --- file: main.maxon
var bag = Bag.create(3)

function main() returns ExitCode
	print("{bag.tag} {bag.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 0
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
error E2015: <fragment>:17:2: Unsupported: the overloads of 'pick' do not agree on their return type ('String' and 'void'), and this call needed the one they disagree about. A call's result type is fixed while its file is parsed, from the parameter types the whole-program declaration sweep publishes — and they did not settle which overload this call means, so the result was typed from the single return type that index keeps per NAME. Only a difference between plain scalars can be corrected once the overload is known, a whole pass later. Make the overloads return the same type, or spell every overload's parameters as type NAMES that this call's arguments match in exactly one of them
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
THIS IS THE ONE OF THEM THE READ DOOR CANNOT REPRODUCE.** `ByteArray` means two things here: `adef.maxon`
declares none and reads the library's `Array with Byte`, while `cmain.maxon` declares its own over `String`,
which is what the bare name means in that file. The whole-program rewrite resolves a recorded return type in
the file that declared the FUNCTION. Give `makeBag` an overload that disagrees with it about what it returns
and the parse-time decider takes over the typing of the call — from a copy the rewrite must reach too, or
`ByteArray` resolves in the CALLER's scope: the bug the rewrite exists to correct, reintroduced one table
over.

`theirs.get(1)` is an `int` only if `ByteArray` meant the library's `Array with Byte`; had it resolved
against `cmain.maxon` the value would be a `String` and the cast would not compile. The three rewrites walk
`overloadedDecls` in the same act and under the same rule — and against each declaration's OWN file, which is
strictly better than the one `funcReturnDeclFiles` keeps per key, since that column is last-wins and an
overload set may span two files.
```maxon
// --- file: adef.maxon
export function makeBag() returns ByteArray
	var b = ByteArray.create()
	b.push(4)
	b.push(9)
	return b
end 'makeBag'

public function makeBag(tag String) returns String
	return "t{tag}"
end 'makeBag'

// --- file: cmain.maxon
typealias ByteArray = Array with String

function main() returns ExitCode
	var mine = ByteArray.create()
	mine.push("x")
	var theirs = makeBag()
	return ((try theirs.get(1) otherwise 0) as ExitCode) * 10 + (mine.count() as ExitCode)
end 'main'
```
```exitcode
91
```


<!-- test: a-function-value-of-a-file-private-overload-keeps-its-parameters -->
Two files of one directory each declare a file-private `bump`, with different parameters. Taken as a value in
`main.maxon`, `bump` is the one that file declares, with its by-reference parameter, so the call writes `v`.
Returns 16.
```maxon
// --- file: alias.maxon
public typealias Small = int(1 to 100)
public typealias Medium = int(1 to 200)

// --- file: main.maxon
function bump(n Small)
	n = 7
end 'bump'

function main() returns ExitCode
	var v = 3 as Small
	let f = bump
	f(v)
	return ((v as Medium) + other()) as ExitCode
end 'main'

// --- file: other.maxon
function bump(n Medium)
	n = 9
end 'bump'

export function other() returns Medium
	var w = 1 as Medium
	bump(w)
	return w
end 'other'
```
```exitcode
16
```


<!-- test: a-module-level-function-value-of-a-file-private-overload-keeps-its-parameters -->
The same overload set, with `bump` taken as a value at the top level of `main.maxon`: the file declares one
`bump`, and the module-level value is that one, as the value in a body is. Returns 16.
```maxon
// --- file: alias.maxon
public typealias Small = int(1 to 100)
public typealias Medium = int(1 to 200)

// --- file: main.maxon
let f = bump

function bump(n Small)
	n = 7
end 'bump'

function main() returns ExitCode
	var v = 3 as Small
	f(v)
	return ((v as Medium) + other()) as ExitCode
end 'main'

// --- file: other.maxon
function bump(n Medium)
	n = 9
end 'bump'

export function other() returns Medium
	var w = 1 as Medium
	bump(w)
	return w
end 'other'
```
```exitcode
16
```

<!-- test: a-rebound-function-value-of-a-file-private-overload-keeps-its-parameters -->
A `var` holding the value, rebound on one path, still names the one `bump` this file declares. Returns 16.
```maxon
// --- file: alias.maxon
public typealias Small = int(1 to 100)
public typealias Medium = int(1 to 200)

// --- file: main.maxon
function bump(n Small)
	n = 7
end 'bump'

function main() returns ExitCode
	var v = 3 as Small
	var f = bump

	if v > 0 'rebind'
		f = bump
	end 'rebind'

	f(v)
	return ((v as Medium) + other()) as ExitCode
end 'main'

// --- file: other.maxon
function bump(n Medium)
	n = 9
end 'bump'

export function other() returns Medium
	var w = 1 as Medium
	bump(w)
	return w
end 'other'
```
```exitcode
16
```

<!-- test: a-typed-function-value-of-a-file-private-overload-keeps-its-parameters -->
The value bound through a function typealias is the same `bump`. Returns 16.
```maxon
// --- file: alias.maxon
public typealias Small = int(1 to 100)
public typealias Medium = int(1 to 200)

// --- file: main.maxon
typealias BumpFn = function(n Small)

function bump(n Small)
	n = 7
end 'bump'

function main() returns ExitCode
	var v = 3 as Small
	let f = bump as BumpFn
	f(v)
	return ((v as Medium) + other()) as ExitCode
end 'main'

// --- file: other.maxon
function bump(n Medium)
	n = 9
end 'bump'

export function other() returns Medium
	var w = 1 as Medium
	bump(w)
	return w
end 'other'
```
```exitcode
16
```


<!-- test: a-function-value-of-a-file-private-overload-taking-a-byte-array-keeps-its-parameters -->
The visible `bump` takes a `ByteArray`, which the declaration sweep spells by its generic base; the value
still names that one declaration. Returns 3.
```maxon
// --- file: alias.maxon
public typealias Medium = int(1 to 200)

// --- file: main.maxon
function bump(b ByteArray) returns Medium
	return b.count() as Medium
end 'bump'

function main() returns ExitCode
	let f = bump
	let bytes = "abc".toByteArray()
	return (f(bytes) + other() - 9) as ExitCode
end 'main'

// --- file: other.maxon
function bump(n Medium) returns Medium
	return n + 8
end 'bump'

export function other() returns Medium
	return bump(1)
end 'other'
```
```exitcode
3
```

<!-- test: a-function-value-of-a-file-private-overload-taking-a-tuple-keeps-its-parameters -->
The visible `bump` takes a tuple of aliases, which the declaration sweep spells by its elements' source
names; the value still names that one declaration. Returns 7.
```maxon
// --- file: alias.maxon
public typealias Small = int(1 to 100)
public typealias Medium = int(1 to 200)

// --- file: main.maxon
function bump(p (Small, Small)) returns Medium
	return (p.0 + p.1) as Medium
end 'bump'

function main() returns ExitCode
	let f = bump
	return (f((3 as Small, 4 as Small)) + other() - 9) as ExitCode
end 'main'

// --- file: other.maxon
function bump(n Medium) returns Medium
	return n + 8
end 'bump'

export function other() returns Medium
	return bump(1)
end 'other'
```
```exitcode
7
```


<!-- test: error.a-function-value-of-a-visible-overload-returning-an-interface-is-refused -->
The `make` visible from `main.maxon` returns an interface, so a value of it is refused as a single
interface-returning function's is, although the file-private `make` in `other.maxon` returns an integer.
```maxon
// --- file: main.maxon
typealias Integer = int(i64.min to i64.max)

interface Producer
	function produce() returns Integer
end 'Producer'

type Widget implements Producer
	export var seed as Integer

	static function create(seed Integer) returns Widget
		return Widget{seed: seed}
	end 'create'

	function produce() returns Integer
		return self.seed
	end 'produce'
end 'Widget'

function make(seed Integer) returns Producer
	return Widget.create(seed)
end 'make'

function consume(p Producer) returns Integer
	return p.produce()
end 'consume'

function main() returns ExitCode
	let f = make
	return consume(f(42)) as ExitCode
end 'main'

// --- file: other.maxon
typealias Integer = int(i64.min to i64.max)

function make(seed Integer, extra Integer) returns Integer
	return seed + extra
end 'make'

export function other() returns ExitCode
	return make(1, extra: 2) as ExitCode
end 'other'
```
```maxoncstderr
error E2015: <fragment>:30:10: Unsupported: a function value's return type declared at the interface type 'Producer' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a call through a function value carries its whole signature on the call op (`callIndirect`), where there is room for ONE result — so the second register is never read and the witness half is dropped. Declare the return at a concrete type, or hand the interface back from a NAMED function called DIRECTLY, whose second return register carries the witness half
```


<!-- test: error.a-refused-initializer-call-to-a-name-only-hidden-cross-file-overloads-carry -->
Two files of the directory each declare a file-private `pick`, so `main.maxon` can name neither, and its
initializer is refused as a body call naming `pick` is.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

function pick(x Integer) returns Integer
	return x
end 'pick'

export function useA() returns Integer
	return pick(1)
end 'useA'

// --- file: b.maxon
function pick(s String) returns Integer
	return s.byteLength() as Integer
end 'pick'

export function useB() returns Integer
	return pick("ab")
end 'useB'

// --- file: main.maxon
let G = pick(3)

function main() returns ExitCode
	return (G + useA() + useB()) as ExitCode
end 'main'
```
```maxoncstderr
error E3008: <fragment>:23:9: function 'pick' is not exported
```

<!-- test: a-generic-function-named-like-a-math-builtin-is-the-one-called -->
A file that declares `abs` means its own: the math builtin is the compiler's only while no file declares the
name. Returns 7.
```maxon
typealias Integer = int(i64.min to i64.max)

function abs(x T) uses T returns T
	return x
end 'abs'

function main() returns ExitCode
	return (abs(0 - 3 as Integer) + 10) as ExitCode
end 'main'
```
```exitcode
7
```


<!-- test: a-function-value-of-a-label-distinguished-overload-member-names-the-visible-one -->
`a.maxon` declares two `pick`s with the same types and different labels, one of them file-private, and
`b.maxon` declares a third; from `main.maxon` the one visible `pick` is the exported one, and the value names
it. Returns 9.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

function pick(start Integer, endIndex Integer) returns Integer
	return endIndex - start
end 'pick'

export function pick(start Integer, length Integer) returns Integer
	return start + length
end 'pick'

export function useA() returns Integer
	return pick(1, endIndex: 2)
end 'useA'

// --- file: b.maxon
function pick(s String) returns Integer
	return s.byteLength() as Integer
end 'pick'

export function useB() returns Integer
	return pick("ab")
end 'useB'

// --- file: main.maxon
function main() returns ExitCode
	let f = pick
	return (f(3, 4) + useB() + useA() - 1) as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: error.a-throwing-member-in-a-subdirectory-is-spelled-with-its-namespace -->
The set is declared by an `extension` in `sub/` and by an `extension` in the root file. Each member is qualified by its own file: the member the call binds to lives in `sub/`.
```maxon
// --- file: sub/box.maxon
export enum PutError implements Error
	failed
end 'PutError'

export type Box
	export var n as Count

	export static function create() returns Box
		return Box{n: 0}
	end 'create'
end 'Box'

extension Box
	export function put(x Count) throws PutError
		if x == 0 'nothing'
			throw PutError.failed
		end 'nothing'
	end 'put'
end 'Box'

// --- file: main.maxon
extension sub.Box
	function put(x String) throws sub.PutError
		if x.isEmpty() 'nothing'
			throw sub.PutError.failed
		end 'nothing'
	end 'put'
end 'Box'

function main() returns ExitCode
	let b = sub.Box.create()
	b.put(1)
	return 0
end 'main'
```
```maxoncstderr
error E3057: <fragment>:34:4: throwing function requires try: 'sub.Box.put'
```

<!-- test: error.a-throwing-member-in-the-root-file-is-spelled-without-the-others-namespace -->
The same two members, and the call binds to the one in the root file, so the sentence does not wear the `sub.` namespace of the other member.
```maxon
// --- file: sub/box.maxon
export enum PutError implements Error
	failed
end 'PutError'

export type Box
	export var n as Count

	export static function create() returns Box
		return Box{n: 0}
	end 'create'
end 'Box'

extension Box
	export function put(x Count) throws PutError
		if x == 0 'nothing'
			throw PutError.failed
		end 'nothing'
	end 'put'
end 'Box'

// --- file: main.maxon
extension sub.Box
	function put(x String) throws sub.PutError
		if x.isEmpty() 'nothing'
			throw sub.PutError.failed
		end 'nothing'
	end 'put'
end 'Box'

function main() returns ExitCode
	let b = sub.Box.create()
	b.put("x")
	return 0
end 'main'
```
```maxoncstderr
error E3057: <fragment>:34:4: throwing function requires try: 'Box.put'
```
