---
feature: duplicate-typealias
status: stable
keywords: [parser, typealias, non-exported, cross-file, duplicate]
category: parser-edge-cases
---

# Duplicate Non-Exported Typealiases

## Documentation

Files in different directories may independently define the same non-exported typealias
(e.g. `typealias MyInt = int(0 to 100)`). Because the aliases are not
exported, they are file-local and must not interfere with each other. Two files of one directory
may not: a directory declares a name once, whatever its visibility, and the second declaration is E3061.

An alias's identity is its DECLARATION. Two declarations of one name are two types whatever they
denote — the same range, or the same function, tuple or generic shape — and a value of one does not
flow into a slot declared with the other unless it is cast. The rule covers the FUNCTION form
(`typealias Step = function(…) returns …`) exactly as it covers the ranged one.

An alias is always quoted by the name its author wrote, and by its directory where the reader sees
both declarations (`alpha.Tallies`).

A file-private alias never reaches another file THROUGH A SIGNATURE: a function visible outside its own
file may only name types at least as visible as itself (E3167, `specs/signature-type-visibility.md`). And a
file that declares a private alias while it sees another file's EXPORTED declaration of the same name means
its own: a bare name resolves to the reader's own directory's declaration before any other directory's, so
the declaring file reads its private alias bare and reaches the other with its directory
(`beta.Step`).

## Tests

<!-- test: non-exported-same-name-crossfile -->
```maxon
// --- file: a/a.maxon
typealias MyInt = int(0 to 1000)

function doubleIt(x MyInt) returns MyInt
	return x + x
end 'doubleIt'

export function doubledFive() returns ExitCode
	return doubleIt(5) as ExitCode
end 'doubledFive'

// --- file: b/b.maxon
typealias MyInt = int(0 to 1000)

function tripleIt(x MyInt) returns MyInt
	return x + x + x
end 'tripleIt'

export function tripledThree() returns ExitCode
	return tripleIt(3) as ExitCode
end 'tripledThree'

// --- file: main.maxon
function main() returns ExitCode
	let a = doubledFive()
	let b = tripledThree()
	return a + b
end 'main'
```
```exitcode
19
```

<!-- test: non-exported-same-name-different-range -->
```maxon
// --- file: a/a.maxon
typealias Limit = int(0 to 500)

function clampA(x Limit) returns Limit
	return x
end 'clampA'

export function fromA() returns ExitCode
	return clampA(40) as ExitCode
end 'fromA'

// --- file: b/b.maxon
typealias Limit = int(0 to 2000)

function clampB(x Limit) returns Limit
	return x
end 'clampB'

export function fromB() returns ExitCode
	return clampB(60) as ExitCode
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	let a = fromA()
	let b = fromB()
	return a + b
end 'main'
```
```exitcode
100
```

<!-- test: non-exported-same-name-function-alias-different-shape -->
The headline case for the FUNCTION form: two files each declare a private `Step`, over shapes that have nothing in common. Each file's functions take their own file's `Step`, and neither call sees the other's shape.
```maxon
// --- file: a/a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally, Tally) returns Tally

function foldA(start Tally, step Step) returns Tally
	return step(start, 3)
end 'foldA'

export function runA() returns ExitCode
	return foldA(1, step: function(acc, more) gives acc + more) as ExitCode
end 'runA'

// --- file: b/b.maxon
typealias Step = function(String) returns String

function foldB(label String, step Step) returns String
	return step(label)
end 'foldB'

export function runB(label String) returns String
	return foldB(label, step: function(text) gives "{text}!")
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	let n = runA()
	let s = runB("go")
	print("{n} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 go!
```

<!-- test: non-exported-same-name-function-alias-same-shape -->
Two files that AGREE about the shape still declare two types, because an alias's identity is its declaration. Nothing crosses here — each file's door takes its own `Step` — so the program runs.
```maxon
// --- file: a/a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally, Tally) returns Tally

function foldA(start Tally, step Step) returns Tally
	return step(start, 3)
end 'foldA'

export function runA() returns ExitCode
	return foldA(1, step: function(acc, more) gives acc + more) as ExitCode
end 'runA'

// --- file: b/b.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally, Tally) returns Tally

function foldB(start Tally, step Step) returns Tally
	return step(start, 5)
end 'foldB'

export function runB() returns ExitCode
	return foldB(1, step: function(acc, more) gives acc * more) as ExitCode
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	let a = runA()
	let b = runB()
	return a + b
end 'main'
```
```exitcode
9
```

<!-- test: error.two-file-private-function-aliases-of-one-shape-are-two-types -->
The same two agreeing declarations, and this time a value would have to cross. `Holder.op` is `pkg/lib.maxon`'s `Step` because that is where the field is written, and an exported field cannot be typed by a file-private alias, so the field is refused (E3167) and the value read from it is refused at `app/main.maxon`'s own `Step` parameter (E3005), because the two are two types.
```maxon
// --- file: pkg/lib.maxon
export typealias Integer = int(i64.min to i64.max)
typealias Step = function(Integer) returns Integer

export type Holder
	export var op as Step

	export static function make() returns Holder
		return Holder{op: function(n Integer) gives n + 1}
	end 'make'
end 'Holder'

// --- file: app/main.maxon
typealias Step = function(Integer) returns Integer

function apply(step Step) returns Integer
	return step(4)
end 'apply'

function main() returns ExitCode
	return apply(Holder.make().op) as ExitCode
end 'main'
```
```maxoncstderr
error E3167: pkg/<fragment>:7:19: exported field 'pkg.Holder.op' names file-private typealias 'Step' in its type
error E3005: app/<fragment>:22:9: argument type mismatch for 'step': expected 'app.Step', got 'pkg.Step'
```

<!-- test: error.two-file-private-generic-aliases-of-one-instance-are-two-types -->
The GENERIC form: both files declare `Bag` over the same instance, `Array with Integer`. `Holder.items` is `pkg/lib.maxon`'s `Bag`, and an exported field cannot be typed by a file-private alias, so the field is refused (E3167) and the value read from it is refused at `app/main.maxon`'s own `Bag` parameter (E3005), because the two are two types.
```maxon
// --- file: pkg/lib.maxon
export typealias Integer = int(i64.min to i64.max)
typealias Bag = Array with Integer

export type Holder
	export var items as Bag

	export static function make() returns Holder
		var b = Bag.create()
		b.push(3)
		return Holder{items: b}
	end 'make'
end 'Holder'

// --- file: app/main.maxon
typealias Bag = Array with Integer

function size(b Bag) returns ExitCode
	return b.count() as ExitCode
end 'size'

function main() returns ExitCode
	return size(Holder.make().items)
end 'main'
```
```maxoncstderr
error E3167: pkg/<fragment>:7:22: exported field 'pkg.Holder.items' names file-private typealias 'Bag' in its type
error E3005: app/<fragment>:24:9: argument type mismatch for 'b': expected 'app.Bag', got 'pkg.Bag'
```

<!-- test: error.an-element-read-from-another-files-same-instance-alias-is-not-this-files -->
One level down: `Holder.rows` is a `Rows`, an `Array with Bag` of `pkg/lib.maxon`'s `Bag`. An exported field cannot be typed by a file-private alias, so the field is refused (E3167), and the value read from it is refused at `app/main.maxon`'s own `Bag` parameter (E3005).
```maxon
// --- file: pkg/lib.maxon
export typealias Integer = int(i64.min to i64.max)
typealias Bag = Array with Integer
typealias Rows = Array with Bag

export type Holder
	export var rows as Rows

	export static function make() returns Holder
		var b = Bag.create()
		b.push(3)
		var rows = Rows.create()
		rows.push(b)
		return Holder{rows: rows}
	end 'make'
end 'Holder'

// --- file: app/main.maxon
typealias Bag = Array with Integer

function size(b Bag) returns ExitCode
	return b.count() as ExitCode
end 'size'

function main() returns ExitCode
	let holder = Holder.make()
	let first = try holder.rows.get(0) otherwise panic("no row")
	return size(first)
end 'main'
```
```maxoncstderr
error E3167: pkg/<fragment>:8:21: exported field 'pkg.Holder.rows' names file-private typealias 'Rows' in its type
error E3005: app/<fragment>:29:9: argument type mismatch for 'b': expected 'app.Bag', got 'pkg.Bag'
```

<!-- test: error.two-file-private-exit-code-aliases-are-two-types -->
`ExitCode` is no exception: each file's own declaration is its own type, whatever the name, so an exported field typed by `pkg/lib.maxon`'s is refused (E3167) and its value is refused at `app/main.maxon`'s parameter (E3005).
```maxon
// --- file: pkg/lib.maxon
typealias ExitCode = int(0 to 125)

export type Holder
	export var code as ExitCode

	export static function make() returns Holder
		return Holder{code: 7}
	end 'make'
end 'Holder'

// --- file: app/main.maxon
typealias ExitCode = int(0 to 125)

function settle(c ExitCode) returns ExitCode
	return c
end 'settle'

function main() returns ExitCode
	return settle(Holder.make().code)
end 'main'
```
```maxoncstderr
error E3167: pkg/<fragment>:6:21: exported field 'pkg.Holder.code' names file-private typealias 'ExitCode' in its type
error E3005: app/<fragment>:21:9: argument type mismatch for 'c': expected 'app.ExitCode', got 'pkg.ExitCode' — cast the argument with 'as app.ExitCode'
```

<!-- test: error.two-same-directory-private-aliases-with-a-declared-in-note-are-not-offered-a-cast -->
```maxon
// --- file: a.maxon
typealias Small = int(0 to 100)

module function make() returns Small
	return 5
end 'make'

// --- file: b.maxon
typealias Small = int(0 to 100)

function take(s Small) returns Small
	return s
end 'take'

function main() returns ExitCode
	return take(make()) as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:10:11: Duplicate typealias 'Small'
error E3167: <fragment>:5:17: module function 'make' names file-private typealias 'Small' in its return type
error E3005: <fragment>:17:9: argument type mismatch for 's': expected 'Small' (declared in b.maxon), got 'Small' (declared in a.maxon)
```

<!-- test: non-exported-same-name-function-alias-inside-a-generic-instance -->
The contested `Step` reaches an `Array` ELEMENT. Each file's `Steps` is an array of that file's shape, so the two instances are distinct and neither array can be handed the other's closure.
```maxon
// --- file: a/a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally, Tally) returns Tally
typealias Steps = Array with Step

function runA(start Tally) returns Tally
	var steps = Steps.create()
	steps.push(function(acc Tally, more Tally) gives acc + more)
	let step = try steps.get(0) otherwise panic("no step")
	return step(start, 3)
end 'runA'

export function goA() returns ExitCode
	return runA(1) as ExitCode
end 'goA'

// --- file: b/b.maxon
typealias Step = function(String) returns String
typealias Steps = Array with Step

export function runB(label String) returns String
	var steps = Steps.create()
	steps.push(function(text String) gives "{text}!")
	let step = try steps.get(0) otherwise panic("no step")
	return step(label)
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	let n = goA()
	let s = runB("go")
	print("{n} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 go!
```

<!-- test: error.a-contested-function-alias-is-named-by-its-source-spelling -->
A refusal between two aliases quotes the names the AUTHOR wrote. `Step` is contested and so is carried internally under a per-file spelling, and none of that spelling may reach the message: a diagnostic naming a type the program does not contain is unsearchable.
```maxon
// --- file: a/a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally, Tally) returns Tally
typealias Other = function(Tally, Tally) returns Tally

function pick() returns Step
	return function(acc Tally, more Tally) gives acc + more
end 'pick'

function runA(start Tally, f Other) returns Tally
	return f(start, 3)
end 'runA'

export function goA() returns ExitCode
	let s = pick()
	return runA(1, f: s) as ExitCode
end 'goA'

// --- file: b/b.maxon
typealias Step = function(String) returns String

function runB(label String, step Step) returns String
	return step(label)
end 'runB'

export function goB(label String) returns String
	return runB(label, step: function(text) gives "{text}!")
end 'goB'

// --- file: main.maxon
function main() returns ExitCode
	let n = goA()
	let s = goB("go")
	print("{n} {s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: a/<fragment>:17:9: argument type mismatch for 'f': expected 'Other', got 'a.Step'
```

<!-- test: non-exported-same-name-function-alias-over-same-spelled-slots -->
Both files spell `Step` identically — `function(Tally) returns Tally` — over their own `Tally`, which ranges differently. Each file's `Step` is its own declaration, and each file's closure is checked against its own file's `Tally`.
```maxon
// --- file: a/a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally) returns Tally

function foldA(start Tally, step Step) returns Tally
	return step(start)
end 'foldA'

export function goA() returns ExitCode
	return foldA(7, step: function(n) gives n + 1) as ExitCode
end 'goA'

// --- file: b/b.maxon
typealias Tally = int(0 to 5)
typealias Step = function(Tally) returns Tally

function foldB(start Tally, step Step) returns Tally
	return step(start)
end 'foldB'

export function goB() returns ExitCode
	return foldB(2, step: function(n) gives n * 2) as ExitCode
end 'goB'

// --- file: main.maxon
function main() returns ExitCode
	let a = goA()
	let b = goB()
	print("{a} {b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 4
```

<!-- test: non-exported-same-name-function-alias-nested-in-another-alias -->
An alias in a SLOT of another: `Outer` takes a `Step`, and each file's `Outer` is built over that file's own `Step`, so neither file's `Outer` accepts the other file's closure.
```maxon
// --- file: a/a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally) returns Tally
typealias Outer = function(Step) returns Tally

function applyA(outer Outer) returns Tally
	return outer(function(n Tally) gives n + 1)
end 'applyA'

export function goA() returns ExitCode
	return applyA(function(step) gives step(7)) as ExitCode
end 'goA'

// --- file: b/b.maxon
typealias Tally = int(0 to 5)
typealias Step = function(Tally) returns Tally
typealias Outer = function(Step) returns Tally

function applyB(outer Outer) returns Tally
	return outer(function(n Tally) gives n * 2)
end 'applyB'

export function goB() returns ExitCode
	return applyB(function(step) gives step(2)) as ExitCode
end 'goB'

// --- file: main.maxon
function main() returns ExitCode
	let a = goA()
	let b = goB()
	print("{a} {b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 4
```

<!-- test: non-exported-same-name-function-alias-as-a-field-type -->
A struct FIELD declared with a contested alias is the fourth place a name is read, beside a parameter, a return type and a local annotation. `Holder.op` is `a.maxon`'s `Step` because `a.maxon` is where the field is written.
```maxon
// --- file: a/a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally) returns Tally

export type Holder
	var op as Step

	static function create(op Step) returns Self
		return Self{op: op}
	end 'create'

	export static function incrementing() returns Holder
		return Holder.create(function(n Tally) gives n + 1)
	end 'incrementing'

	export function run(start ExitCode) returns ExitCode
		return self.op(start as Tally) as ExitCode
	end 'run'
end 'Holder'

// --- file: b/b.maxon
typealias Step = function(String) returns String

function foldB(label String, step Step) returns String
	return step(label)
end 'foldB'

export function goB(label String) returns String
	return foldB(label, step: function(text) gives "{text}!")
end 'goB'

// --- file: main.maxon
function main() returns ExitCode
	let holder = Holder.incrementing()
	let n = holder.run(1)
	let s = goB("go")
	print("{n} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 go!
```

<!-- test: a-private-function-alias-beside-a-visible-export-is-its-own-files-bare-name -->
`alpha/a.maxon` declares a private `Step` and sees `beta/b.maxon`'s exported `Step`: the declaring file's own directory decides, so its bare `Step` is its private alias, over `Tally`. `main.maxon` reads neither bare; it hands `foldB` a closure over `beta`'s `Step`.
```maxon
// --- file: alpha/a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally) returns Tally

function pick() returns Step
	return function(n Tally) gives n + 1
end 'pick'

export function goA() returns ExitCode
	let step = pick()

	return step(6) as ExitCode
end 'goA'

// --- file: beta/b.maxon
export typealias Step = function(String) returns String

export function foldB(label String, step Step) returns String
	return step(label)
end 'foldB'

// --- file: main.maxon
function main() returns ExitCode
	print("{foldB("go", step: function(text) gives "{text}!")}\n")

	return goA()
end 'main'
```
```exitcode
7
```
```stdout
go!
```

<!-- test: non-exported-same-name-function-alias-over-a-tuple-slot -->
A TUPLE alias in the slot. The two `Pair`s differ in element ORDER, which survives canonicalisation, so the two `Step`s are two shapes and each closure destructures its own file's pair.
```maxon
// --- file: a/a.maxon
typealias Tally = int(0 to 1000)
typealias Pair = (Tally, String)
typealias Step = function(Pair) returns Tally

function foldA(step Step) returns Tally
	return step((700, "a"))
end 'foldA'

export function goA() returns String
	return "{foldA(function(pair) gives pair.0 + 1)}"
end 'goA'

// --- file: b/b.maxon
typealias Tally = int(0 to 5)
typealias Pair = (String, Tally)
typealias Step = function(Pair) returns Tally

function foldB(step Step) returns Tally
	return step(("b", 3))
end 'foldB'

export function goB() returns String
	return "{foldB(function(pair) gives pair.1 + 1)}"
end 'goB'

// --- file: main.maxon
function main() returns ExitCode
	print("{goA()} {goB()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
701 4
```

<!-- test: non-exported-same-name-function-alias-over-a-generic-instance-slot -->
A GENERIC-INSTANCE alias in the slot, contested one level down: both files write `Array with Tally` and mean two element ranges, so `Tallies` is two instances and `Step` is two shapes.
```maxon
// --- file: a/a.maxon
typealias Tally = int(0 to 1000)
typealias Tallies = Array with Tally
typealias Step = function(Tallies) returns Tally

function foldA(step Step) returns Tally
	var xs = Tallies.create()
	xs.push(700)
	return step(xs)
end 'foldA'

export function goA() returns String
	return "{foldA(function(xs) gives (try xs.get(0) otherwise 0) + 1)}"
end 'goA'

// --- file: b/b.maxon
typealias Tally = int(0 to 5)
typealias Tallies = Array with Tally
typealias Step = function(Tallies) returns Tally

function foldB(step Step) returns Tally
	var xs = Tallies.create()
	xs.push(3)
	return step(xs)
end 'foldB'

export function goB() returns String
	return "{foldB(function(xs) gives (try xs.get(0) otherwise 0) + 1)}"
end 'goB'

// --- file: main.maxon
function main() returns ExitCode
	print("{goA()} {goB()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
701 4
```

<!-- test: a-private-ranged-alias-beside-a-visible-export-is-its-own-files-bare-name -->
The RANGED form of the same rule. `alpha/a.maxon`'s private `Tally` and `beta/b.maxon`'s exported `Tally` are two declarations `alpha/a.maxon` sees, and its bare `Tally` is its own: `150` fits only that one, so a bare `Tally` read as `beta`'s would be E3005.
```maxon
// --- file: alpha/a.maxon
typealias Tally = int(0 to 1000)

function bump(n Tally) returns Tally
	return n + 1
end 'bump'

export function goA() returns ExitCode
	return bump(150) as ExitCode
end 'goA'

// --- file: beta/b.maxon
export typealias Tally = int(0 to 5)

export function capB(n Tally) returns Tally
	return n
end 'capB'

// --- file: main.maxon
function main() returns ExitCode
	return goA() + (capB(3) as ExitCode) - 112
end 'main'
```
```exitcode
42
```

<!-- test: non-exported-same-name-function-alias-over-generic-slots-of-two-bases -->
The two `Tallies` share an element and differ in their BASE — `Array` against `List` — so the shape key must carry the base name as well as the arguments.
```maxon
// --- file: a/a.maxon
typealias Integer = int(i64.min to i64.max)
typealias Tallies = Array with Integer
typealias Step = function(Tallies) returns Integer

function foldA(step Step) returns Integer
	var xs = Tallies.create()
	xs.push(700)
	return step(xs)
end 'foldA'

export function goA() returns String
	return "{foldA(function(xs) gives (try xs.get(0) otherwise 0) + 1)}"
end 'goA'

// --- file: b/b.maxon
typealias Integer = int(i64.min to i64.max)
typealias Tallies = List with Integer
typealias Step = function(Tallies) returns Integer

function foldB(step Step) returns Integer
	var xs = Tallies.create()
	xs.append(3)
	return step(xs)
end 'foldB'

export function goB() returns String
	return "{foldB(function(xs) gives (try xs.get(0) otherwise 0) + 1)}"
end 'goB'

// --- file: main.maxon
function main() returns ExitCode
	print("{goA()} {goB()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
701 4
```

<!-- test: a-private-generic-alias-beside-a-visible-export-is-its-own-files-bare-name -->
The GENERIC form of the same rule. `beta/b.maxon` keeps a private `Tallies` over `List` while it sees `alpha/a.maxon`'s exported `Tallies` over `Array`, and its bare `Tallies` is its own: `append` exists on a `List` only.
```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(i64.min to i64.max)
export typealias Tallies = Array with Integer

export function countA(xs Tallies) returns Integer
	return xs.count()
end 'countA'

// --- file: beta/b.maxon
typealias Tallies = List with Integer

export function goB() returns ExitCode
	var xs = Tallies.create()
	xs.append(3)

	return xs.count() as ExitCode
end 'goB'

// --- file: main.maxon
function main() returns ExitCode
	var xs = alpha.Tallies.create()
	xs.push(1)

	return goB() + (countA(xs) as ExitCode)
end 'main'
```
```exitcode
2
```

<!-- test: error.two-functions-over-a-contested-generic-alias-do-not-merge -->
A ternary whose two arms are functions over a contested `Tallies`. The two arms do not merge — agreement is decided on the resolved shapes — and each caption names its `Tallies` by its directory, `alpha.Tallies` and `beta.Tallies`, the spellings a reader that sees both must write.

⚠ Both files hand a function VALUE across, so both export their `Tallies` — two exported declarations of one name in two directories, which is legal, and still two instances. Each declaring file sees both, so it names its own through its directory as well; the element aliases and `Integer` have one declaration each.
```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(i64.min to i64.max)
export typealias Tally = int(0 to 1000)
export typealias Tallies = Array with Tally

export function sizeA(t alpha.Tallies) returns Integer
	return t.count()
end 'sizeA'

// --- file: beta/b.maxon
export typealias Tiny = int(0 to 5)
export typealias Tallies = Array with Tiny

export function sizeB(t beta.Tallies) returns Integer
	return t.count()
end 'sizeB'

// --- file: main.maxon
function main() returns ExitCode
	let pick = true
	let f = sizeA if pick else sizeB
	let _ = f
	return 0
end 'main'
```
```maxoncstderr
error E2028: <fragment>:22:16: ternary expression type mismatch: true branch is 'fn(alpha.Tallies) returns Integer' but false branch is 'fn(beta.Tallies) returns Integer'
```

<!-- test: error.non-exported-same-name-in-one-directory -->
Two files of ONE directory each declare a private `MyInt`. Neither can see the other's, and the second is still
refused: a directory declares a name once, whatever its visibility.
```maxon
// --- file: a.maxon
typealias MyInt = int(0 to 1000)

function doubleIt(x MyInt) returns MyInt
	return x + x
end 'doubleIt'

export function doubledFive() returns ExitCode
	return doubleIt(5) as ExitCode
end 'doubledFive'

// --- file: b.maxon
typealias MyInt = int(0 to 1000)

function tripleIt(x MyInt) returns MyInt
	return x + x + x
end 'tripleIt'

export function tripledThree() returns ExitCode
	return tripleIt(3) as ExitCode
end 'tripledThree'

// --- file: main.maxon
function main() returns ExitCode
	return doubledFive() + tripledThree()
end 'main'
```
```maxoncstderr
error E3061: <fragment>:14:11: Duplicate typealias 'MyInt'
```
