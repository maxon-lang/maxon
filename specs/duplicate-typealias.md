---
feature: duplicate-typealias
status: stable
keywords: [parser, typealias, non-exported, cross-file, duplicate]
category: parser-edge-cases
---

# Duplicate Non-Exported Typealiases

## Documentation

Multiple files may independently define the same non-exported typealias
(e.g. `typealias MyInt = int(0 to 100)`). Because the aliases are not
exported, they are file-local and must not interfere with each other.

The rule covers the FUNCTION form (`typealias Step = function(…) returns …`) too. Two files
declaring a private `Step` over different shapes have two types, and each file's declarations mean
its own — a value of one file's `Step` does not satisfy a door declared with the other's. What a
slot's spelling MEANS in the declaring file decides, not how it is spelled: two files that write
the identical `function(Tally) returns Tally` over two different `Tally` ranges still have two
shapes, and two files that agree share one.

A contested alias is always quoted by the name its author wrote. Where both sides of a refusal
print the same bare name, the message adds a note in parentheses: the shape where the two shapes
differ, otherwise the declaring files.

A file-private alias never reaches another file THROUGH A SIGNATURE: a function visible outside its own
file may only name types at least as visible as itself (E3167, `specs/signature-type-visibility.md`). So a
program that carries a contested name ACROSS a boundary exports the declaration the DOOR is written with,
and the file on the other side keeps its own private one — which is what the refusal cases below do. Every
runnable case keeps BOTH declarations private and reaches each file's own meaning through a door that names
neither.

## Tests

<!-- test: non-exported-same-name-crossfile -->
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
// --- file: a.maxon
typealias Limit = int(0 to 500)

function clampA(x Limit) returns Limit
	return x
end 'clampA'

export function fromA() returns ExitCode
	return clampA(40) as ExitCode
end 'fromA'

// --- file: b.maxon
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
// --- file: a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally, Tally) returns Tally

function foldA(start Tally, step Step) returns Tally
	return step(start, 3)
end 'foldA'

export function runA() returns ExitCode
	return foldA(1, step: function(acc, more) gives acc + more) as ExitCode
end 'runA'

// --- file: b.maxon
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
Two files that AGREE about the shape are not split. A name is scoped per declaring file only where the declarations disagree, so `Step` here is one type and one brand.
```maxon
// --- file: a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally, Tally) returns Tally

function foldA(start Tally, step Step) returns Tally
	return step(start, 3)
end 'foldA'

export function runA() returns ExitCode
	return foldA(1, step: function(acc, more) gives acc + more) as ExitCode
end 'runA'

// --- file: b.maxon
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

<!-- test: non-exported-same-name-function-alias-inside-a-generic-instance -->
The contested `Step` reaches an `Array` ELEMENT. Each file's `Steps` is an array of that file's shape, so the two instances are distinct and neither array can be handed the other's closure.
```maxon
// --- file: a.maxon
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

// --- file: b.maxon
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
// --- file: a.maxon
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

// --- file: b.maxon
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
error E3005: <fragment>:17:9: argument type mismatch for 'f': expected 'Other', got 'Step'
```

<!-- test: non-exported-same-name-function-alias-over-same-spelled-slots -->
Both files spell `Step` identically — `function(Tally) returns Tally` — and the shapes still differ, because each file's `Tally` is its own range. What a slot's spelling MEANS in the declaring file is what decides, never the characters.
```maxon
// --- file: a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally) returns Tally

function foldA(start Tally, step Step) returns Tally
	return step(start)
end 'foldA'

export function goA() returns ExitCode
	return foldA(7, step: function(n) gives n + 1) as ExitCode
end 'goA'

// --- file: b.maxon
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
A contested alias in a SLOT contests its container: `Outer` takes a `Step`, and the two files disagree about `Step`, so `Outer` is split as well. Splitting `Step` alone would leave one `Outer` accepting either file's closure.
```maxon
// --- file: a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally) returns Tally
typealias Outer = function(Step) returns Tally

function applyA(outer Outer) returns Tally
	return outer(function(n Tally) gives n + 1)
end 'applyA'

export function goA() returns ExitCode
	return applyA(function(step) gives step(7)) as ExitCode
end 'goA'

// --- file: b.maxon
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
// --- file: a.maxon
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

// --- file: b.maxon
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

<!-- test: error.two-shapes-of-one-contested-name-are-told-apart-by-their-files -->
When one file passes its own `Step` where another's is declared, both sides print the bare name `Step` — so the message carries a note. The SHAPES differ here, and the shape is what a reader needs, so the note is the shape.

⚠ The DOOR is `b.maxon`'s, so `b.maxon` exports its `Step` and `a.maxon` keeps its own private one: a door another file calls may not be written with a file-private type (E3167). The contest is the same one — two files, one name, two shapes — and the message is what this case is about.
```maxon
// --- file: a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally) returns Tally

function pick() returns Step
	return function(n Tally) gives n + 1
end 'pick'

export function goA() returns String
	return foldB("go", step: pick())
end 'goA'

// --- file: b.maxon
export typealias Step = function(String) returns String

export function foldB(label String, step Step) returns String
	return step(label)
end 'foldB'

// --- file: main.maxon
function main() returns ExitCode
	print("{goA()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:9: argument type mismatch for 'step': expected 'Step' (fn(String) returns String), got 'Step' (fn(int) returns int)
```

<!-- test: non-exported-same-name-function-alias-over-a-tuple-slot -->
A TUPLE alias in the slot. The two `Pair`s differ in element ORDER, which survives canonicalisation, so the two `Step`s are two shapes and each closure destructures its own file's pair.
```maxon
// --- file: a.maxon
typealias Tally = int(0 to 1000)
typealias Pair = (Tally, String)
typealias Step = function(Pair) returns Tally

function foldA(step Step) returns Tally
	return step((700, "a"))
end 'foldA'

export function goA() returns String
	return "{foldA(function(pair) gives pair.0 + 1)}"
end 'goA'

// --- file: b.maxon
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
// --- file: a.maxon
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

// --- file: b.maxon
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

<!-- test: error.two-ranges-of-one-contested-name-are-told-apart-by-their-files -->
The other tier of the same note. Here the two `Step`s render the SAME shape text — the difference is `Tally`'s range, which the rendering erases — so the note names the declaring files instead. The file is a provenance note beside the name and never part of it.

⚠ `b.maxon` owns the door and exports its `Tally` and `Step`; `a.maxon`'s pair stays private. Both declarations still render the same shape text, which is what makes the note name the files.
```maxon
// --- file: a.maxon
typealias Tally = int(0 to 1000)
typealias Step = function(Tally) returns Tally

function pick() returns Step
	return function(n Tally) gives n + 1
end 'pick'

export function goA() returns ExitCode
	return foldB(2, step: pick()) as ExitCode
end 'goA'

// --- file: b.maxon
export typealias Tally = int(0 to 5)
export typealias Step = function(Tally) returns Tally

export function foldB(start Tally, step Step) returns Tally
	return step(start)
end 'foldB'

// --- file: main.maxon
function main() returns ExitCode
	print("{goA()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:9: argument type mismatch for 'step': expected 'Step' (declared in b.maxon), got 'Step' (declared in a.maxon)
```

<!-- test: non-exported-same-name-function-alias-over-generic-slots-of-two-bases -->
The two `Tallies` share an element and differ in their BASE — `Array` against `List` — so the shape key must carry the base name as well as the arguments.
```maxon
// --- file: a.maxon
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

// --- file: b.maxon
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

<!-- test: error.two-bases-of-one-contested-generic-alias-are-told-apart-by-their-files -->
The provenance note is not a function-alias rule: a contested GENERIC alias earns it on the same terms. Both `Tallies` print bare, the two files sit in one namespace, so the declaring files are what separates them.

⚠ The door is `countA`, so `a.maxon` exports `Tallies` and its element; `b.maxon` keeps its own private `Tallies` over `List` and passes it. One name, two bases, two files.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)
export typealias Tallies = Array with Integer

export function countA(xs Tallies) returns Integer
	return xs.count()
end 'countA'

// --- file: b.maxon
typealias Integer = int(i64.min to i64.max)
typealias Tallies = List with Integer

export function goB() returns ExitCode
	var xs = Tallies.create()
	xs.append(3)
	return countA(xs) as ExitCode
end 'goB'

// --- file: main.maxon
function main() returns ExitCode
	print("{goB()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:9: argument type mismatch for 'xs': expected 'Tallies' (declared in a.maxon), got 'Tallies' (declared in b.maxon)
```

<!-- test: error.two-functions-over-a-contested-generic-alias-do-not-merge -->
A ternary whose two arms are functions over a contested `Tallies`. The two arms render the same `fn(…)` caption, and they still do not merge — agreement is decided on the resolved shapes, and the note is what tells the reader which `Tallies` each caption means.

⚠ Both files hand a function VALUE across, so both export their `Tallies` and its element — two exported declarations of one name over two ranges, which is legal (E3105 refuses only two underlying TYPES), and still two instances.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)
export typealias Tally = int(0 to 1000)
export typealias Tallies = Array with Tally

export function sizeA(t Tallies) returns Integer
	return t.count()
end 'sizeA'

// --- file: b.maxon
export typealias Integer = int(i64.min to i64.max)
export typealias Tally = int(0 to 5)
export typealias Tallies = Array with Tally

export function sizeB(t Tallies) returns Integer
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
error E2028: <fragment>:23:16: ternary expression type mismatch: true branch is 'fn(Tallies) returns Integer' (declared in a.maxon) but false branch is 'fn(Tallies) returns Integer' (declared in b.maxon)
```
