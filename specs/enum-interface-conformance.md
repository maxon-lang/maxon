---
feature: enum-interface-conformance
status: experimental
keywords: [enum, union, interface, implements, conformance, widening, existential]
category: type-system
---

# An `enum`/`union` `implements` clause

## Documentation

An `enum` or a `union` may name any interface in its `implements` clause, exactly as a `type` may. The
clause files the same conformance a `type`'s does: the enum satisfies each of the interface's
requirements with its own methods, and nothing about the requirement changes because the conformer is
a tag rather than a struct.

A value of a conforming enum therefore **widens into every interface position**: an interface-typed
parameter, an interface-typed return, and an interface-typed field. A payload-free enum travels as its
tag in the fat pointer's value half; a `union` carrying a payload travels as the pointer to its box,
and the widened value is released through its witness when it drops, exactly as a struct conformer is.

Two refusals apply to an enum's clause on the same terms they apply to a `type`'s:

- a requirement the enum supplies no matching method for is **E3016**, positioned at the enum's name;
- a clause naming an interface that does not exist is **E3015**, positioned at the enum's name.

A payload-free enum may also name `Hashable` or `Equatable` explicitly. The compiler grants both to
every payload-free enum already, and the granted implementation is what satisfies the declared
requirement — declaring it is a statement of intent, not a demand for a hand-written `hash()`.

### Example

```text
interface Greeter
  function greet() returns Integer
end 'Greeter'

enum Step implements Greeter
  one
  two

  function greet() returns Integer
    return match self 'which'
      one gives 41
      two gives 7
    end 'which'
  end 'greet'
end 'Step'
```

## Tests

<!-- test: an-enum-conformer-is-passed-as-the-interface -->
The ARGUMENT door, and the shape `basic-interface-dispatch` pins for a `type`. `Step.one` widens into a
`Greeter` parameter and dispatches to the enum's own method through the witness.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet() returns Integer
end 'Greeter'

enum Step implements Greeter
	one
	two

	function greet() returns Integer
		return match self 'which'
			one gives 41
			two gives 7
		end 'which'
	end 'greet'
end 'Step'

function callGreet(g Greeter) returns Integer
	return g.greet() + 1
end 'callGreet'

function main() returns ExitCode
	return callGreet(Step.one)
end 'main'
```
```exitcode
42
```

<!-- test: an-enum-conformer-is-returned-as-the-interface -->
The RETURN door. A non-throwing function declared `returns Greeter` hands back the fat pointer, and the
witness half is the enum's table.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet() returns Integer
end 'Greeter'

enum Step implements Greeter
	one
	two

	function greet() returns Integer
		return match self 'which'
			one gives 41
			two gives 7
		end 'which'
	end 'greet'
end 'Step'

function make() returns Greeter
	return Step.two
end 'make'

function callGreet(g Greeter) returns Integer
	return g.greet() + 1
end 'callGreet'

function main() returns ExitCode
	let g = make()
	return callGreet(g)
end 'main'
```
```exitcode
8
```

<!-- test: an-enum-conformer-is-stored-in-an-interface-field -->
The FIELD-STORE door: the enum value is widened at the `Self{...}` literal, held at the field's
interface type, and dispatched through `self`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet() returns Integer
end 'Greeter'

enum Step implements Greeter
	one
	two

	function greet() returns Integer
		return match self 'which'
			one gives 41
			two gives 7
		end 'which'
	end 'greet'
end 'Step'

type Holder
	let g as Greeter

	static function create(s Step) returns Self
		return Self{g: s}
	end 'create'

	function greetThrough() returns Integer
		return self.g.greet()
	end 'greetThrough'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(Step.one)
	return h.greetThrough()
end 'main'
```
```exitcode
41
```

<!-- test: a-payload-carrying-enum-conformer-widens-and-drops -->
A payload-carrying conformer travels as the pointer to its box, so the widening must not lose the
reference and the drop must release it exactly once. The payload is long enough to force a real heap
allocation, and the exit code is pinned so a leak (exit 101) reddens the case rather than passing on
stdout alone.
```maxon
interface Describer
	function describe() returns String
end 'Describer'

union Status implements Describer
	idle
	ready(detail String)

	function describe() returns String
		return match self 'k'
			idle gives "idle"
			ready(detail) gives "ready: {detail}"
		end 'k'
	end 'describe'
end 'Status'

function show(d Describer)
	print("{d.describe()}\n")
end 'show'

function main() returns ExitCode
	let s = Status.ready("a rather long detail string that forces a real heap allocation")
	show(s)
	return 0
end 'main'
```
```stdout
ready: a rather long detail string that forces a real heap allocation
```
```exitcode
0
```

<!-- test: a-union-conformer-is-passed-as-the-interface -->
The `union` twin of the first case: an `implements` clause on a `union` files the same conformance, and
a case carrying an associated value widens at the argument door like any other.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet() returns Integer
end 'Greeter'

union Signal implements Greeter
	low
	high(level Integer)

	function greet() returns Integer
		return match self 'which'
			low gives 5
			high(level) gives level
		end 'which'
	end 'greet'
end 'Signal'

function callGreet(g Greeter) returns Integer
	return g.greet() + 1
end 'callGreet'

function main() returns ExitCode
	return callGreet(Signal.high(9))
end 'main'
```
```exitcode
10
```

<!-- test: error.an-enum-conformer-missing-a-requirement -->
E3016 applies to an enum's clause on the same terms as a `type`'s, positioned at the enum's name. No
widening anywhere in the program: the clause alone is the claim, and an unsatisfied claim is refused
where it is written.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet() returns Integer
end 'Greeter'

enum Step implements Greeter
	one
	two
end 'Step'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:8:6: Partial interface implementation: type 'Step' is missing 1 method(s):
  - greet() returns Integer
```

<!-- test: error.an-enum-implements-an-unknown-interface -->
E3015: a typo'd interface name on an enum must not silently pass any more than it may on a `type`.
```maxon
enum Step implements Nonexistent
	one
	two
end 'Step'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3015: <fragment>:2:6: type 'Step' implements unknown interface 'Nonexistent'
```

<!-- test: an-enum-declaring-a-granted-protocol-is-satisfied-by-the-grant -->
The REGRESSION GUARD, and it is expected to pass today. A payload-free enum is granted `Hashable` by the
compiler, so naming it explicitly must be satisfied by the grant rather than demanding a hand-written
`hash()` — an `implements` clause that files a conformance must not un-file the one already there.
```maxon
enum Color implements Hashable
	red
	green
	blue
end 'Color'

typealias Tally = int(i64.min to i64.max)
typealias ColorMap = Map with (Color, Tally)

function main() returns ExitCode
	var m = ColorMap.create()
	try m.insert(Color.red, value: 10) otherwise ignore
	try m.insert(Color.blue, value: 32) otherwise ignore
	let a = try m.get(Color.red) otherwise 0
	let b = try m.get(Color.blue) otherwise 0
	return a + b
end 'main'
```
```exitcode
42
```

<!-- test: a-library-enum-conformer-is-credited-at-the-widening -->
The conformance is filed when the enum's OWN file is parsed, and the widening happens in another — so the
two files have no ordering and the credit cannot come from having just read the clause. The overlay puts
the interface and the conforming enum in `stdlib/Builtins.maxon`; the program names a case of it and
widens the tag, calling no function of the enum on the way in.
```maxon
// --- stdlib-overlay: Builtins.maxon
export typealias OverlayRank = int(i64.min to i64.max)

export interface OverlayRanker
	function rank() returns OverlayRank
end 'OverlayRanker'

export enum OverlayStep implements OverlayRanker
	first
	second

	export function rank() returns OverlayRank
		return match self 'which'
			first gives 3
			second gives 40
		end 'which'
	end 'rank'
end 'OverlayStep'
// --- file: main.maxon
function through(r OverlayRanker) returns OverlayRank
	return r.rank()
end 'through'

function main() returns ExitCode
	return through(OverlayStep.second)
end 'main'
```
```exitcode
40
```

<!-- test: error.an-enum-conformer-writing-a-let-argument-is-refused -->
⭐⭐ **THE MUTATION SUMMARY CLOSES OVER THE ENUM CONFORMER TOO.** `through(g Grower, dest IntArray)`
names no callee — the jump goes through the witness table — so what says it writes `dest` is the union
over `Grower`'s conformers (`SemanticCheck.witnessDispatchImpls`). `Step.grow` pushes into its `dest`,
therefore `through` writes its `dest`, therefore passing a `let`-bound array is **E3019**. The same
program with a `type` conformer is `where-clauses.md`'s `witness-mutation-of-let-argument-refused`; a
conformer roster that walked declared types only left this one compiling clean and silently pushing
into an immutable array.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

interface Grower
	function grow(dest IntArray)
end 'Grower'

enum Step implements Grower
	one
	two

	function grow(dest IntArray)
		dest.push(9)
	end 'grow'
end 'Step'

function through(g Grower, dest IntArray)
	g.grow(dest)
end 'through'

function main() returns ExitCode
	let a = IntArray.create()
	through(Step.one, dest: a)
	return try a.get(0) otherwise 55
end 'main'
```
```maxoncstderr
error E3019: <fragment>:24:2: cannot pass 'a' to function that mutates parameter 'dest' (in main)
```

<!-- test: an-enum-conformer-writing-a-var-argument-is-allowed -->
⭐ **THE CONTROL FOR THE REFUSAL ABOVE: THE SAME PROGRAM WITH `var a`.** E3019 is about the BINDING and
never about the route, so a refusal that fired on the enum dispatch rather than on the subject would
take this program with it and nothing else here would notice. Answers 9 — the push happened, through
the enum's witness table, into an array the program declared mutable.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

interface Grower
	function grow(dest IntArray)
end 'Grower'

enum Step implements Grower
	one
	two

	function grow(dest IntArray)
		dest.push(9)
	end 'grow'
end 'Step'

function through(g Grower, dest IntArray)
	g.grow(dest)
end 'through'

function main() returns ExitCode
	var a = IntArray.create()
	through(Step.one, dest: a)
	return try a.get(0) otherwise 55
end 'main'
```
```exitcode
9
```
