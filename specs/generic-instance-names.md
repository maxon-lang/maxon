---
feature: generic-instance-names
status: stable
keywords: [generics, typealias, monomorphization, symbol-names, mangling, collision]
category: parser-edge-cases
---

# The Name a Generic Instance Compiles To

## Documentation

A generic instance — `Array with String`, `Map with (Key, Value)` — is compiled under a single name, and
every method the instance needs is emitted beneath it. That name is normally one the program wrote down
(`typealias TokenIter = ArrayIterator with String` ⇒ `TokenIter.index`); where nothing names the
instance, the compiler synthesizes one from the source type and the arguments (`__ArrayIterator_String`).

Two rules govern it, and both are about a name being a FUNCTION of the program rather than of the
checkout:

1. **One instance, one name — chosen without reference to file order.** Several names may denote one
   instance: two files may each declare an alias for it, and the compiler may have synthesized one
   beside them. They all mean the same type, so which is used decides nothing but the symbols emitted —
   which is exactly why it must not be decided by which file the filesystem listed first. `stdlib`'s name
   outranks a project's; among equals the ordinal-smallest wins.

2. **Two instances may not compile to one name.** Synthesized names join the source type and its
   arguments with `_`, which is itself legal inside a type name, so `Map with (A_B, C)` and
   `Map with (A, B_C)` spell one name between them. That pair is refused (E3006) naming both
   instantiations, rather than the second instantiation being handed the first one's type.

## Tests

<!-- test: instance-name-does-not-depend-on-file-order -->
⭐ Two files each declare a name for ONE instance — `p_first.maxon` calls `ArrayIterator with String`
`ZIter`, `q_second.maxon` calls it `AIter` — and `main.maxon` uses that instance without naming it at
all. ⚠⚠ **THE EMITTED SYMBOL WAS A PROPERTY OF THE CHECKOUT.** Measured before the fix: `main`'s calls
read `ZIter.advance`/`ZIter.index` in natural source order and `AIter.advance`/`AIter.index` under
`MAXON_SOURCE_ORDER=reverse` — same program, same files, different binary — because the search for "does
anything already name this instance?" returned the FIRST entry an insertion-ordered dictionary handed
back, at four separate sites. The rule is now `stdlib` first, then ordinal-smallest, so `AIter` wins in
either order. ⚠ **THE VALUE THIS PRINTS IS THE LIVENESS HALF, NOT THE DISCRIMINATING ONE**: both names
denote the same type, so no run can tell them apart. The half that discriminates is the committed
FRAGMENT beside this case, which records the emitted call by name. ⚠⚠ THREE FILES ON PURPOSE: the defect
needs two competing declarations and a third party that names neither, and the file names are chosen so
that ordinal order and file order DISAGREE — written the other way round the case passes whatever the
compiler does.
```maxon
// --- file: p_first.maxon
typealias ZIter = ArrayIterator with String

export function viaZ(iter ZIter) returns ExitCode
	return iter.index() as ExitCode
end 'viaZ'

// --- file: q_second.maxon
typealias AIter = ArrayIterator with String

export function viaA(iter AIter) returns ExitCode
	return iter.index() as ExitCode
end 'viaA'

// --- file: main.maxon
function main() returns ExitCode
	let xs = ["a", "b", "c"]
	var it = try xs.createIterator() otherwise return 9
	try it.advance() otherwise return 9
	print("index={it.index()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
index=1
```


<!-- test: distinct-instantiations-of-one-generic-keep-their-own-fields -->
Positive control for the case below, and the reason that one is a REFUSAL rather than a rule against
instantiating a generic twice. Two `Map` instantiations whose type names share nothing awkward each keep
their own key and value types, and a value stored through one is read back through its own field table.
Prints `20 77`.
```maxon
typealias Num = int(i64.min to i64.max)

type Ka implements Hashable, Equatable
	var k as Num

	function hash() returns HashValue
		return k
	end 'hash'

	function equals(other Self) returns bool
		return k == other.k
	end 'equals'

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'
end 'Ka'

type Kb implements Hashable, Equatable
	var k as Num

	function hash() returns HashValue
		return k
	end 'hash'

	function equals(other Self) returns bool
		return k == other.k
	end 'equals'

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'
end 'Kb'

type Wide
	export var pad as Num
	export var only as Num

	static function create(pad Num, only Num) returns Self
		return Self{pad: pad, only: only}
	end 'create'
end 'Wide'

type Narrow
	export var only as Num

	static function create(only Num) returns Self
		return Self{only: only}
	end 'create'
end 'Narrow'

function main() returns ExitCode
	let first = [Ka.create(1): Wide.create(10, only: 20)]
	let second = [Kb.create(2): Narrow.create(77)]
	var a = 0
	var b = 0
	for (_, v) in first 'one'
		a = v.only
	end 'one'
	for (_, v) in second 'two'
		b = v.only
	end 'two'
	print("{a} {b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20 77
```


<!-- test: error.instantiation-pair-compiling-to-one-name -->
⭐ The same program with the type names chosen so the two instantiations SPELL THE SAME SYNTHESIZED NAME:
`Map with (A_B, C)` and `Map with (A, B_C)` both join to `__Map_A_B_C`. ⚠⚠ **THIS COMPILED CLEAN AND
PRINTED A WRONG ANSWER.** Measured before the fix: `20 0` where the program stores 77, because the
second literal found the name already taken, adopted the FIRST map's type without asking whether it
meant the same instance, and read a one-field `B_C` back through a two-field `C` — `only` at offset 8 of
a value whose only field is at 0. ⚠ Underscores are legal in user type names and `specs/tuples.md`
already carries a passing program declaring `A_B`, `A`, `C` and `B_C` together, so this is a shape the
corpus already writes. It is REFUSED rather than re-spelled: re-spelling means escaping the separator in
every synthesized name in the tree, `__ManagedMemory` included, to serve a pair nothing writes on
purpose — and `maxon-shv2` rules the same way for the same fact, with the same code and the same words.
```maxon
typealias Num = int(i64.min to i64.max)

type A_B implements Hashable, Equatable
	var k as Num

	function hash() returns HashValue
		return k
	end 'hash'

	function equals(other Self) returns bool
		return k == other.k
	end 'equals'

	static function create(k Num) returns Self
		return Self{k: k}
	end 'create'
end 'A_B'

type A implements Hashable, Equatable
	var k as Num

	function hash() returns HashValue
		return k
	end 'hash'

	function equals(other Self) returns bool
		return k == other.k
	end 'equals'

	static function create(k Num) returns Self
		return Self{k: k}
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

function main() returns ExitCode
	let first = [A_B.create(1): C.create(10, only: 20)]
	let second = [A.create(2): B_C.create(77)]
	var a = 0
	var b = 0
	for (_, v) in first 'one'
		a = v.only
	end 'one'
	for (_, v) in second 'two'
		b = v.only
	end 'two'
	print("{a} {b}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/generic-instance-names/error.instantiation-pair-compiling-to-one-name.test:55:15: duplicate definition of '__Map_A_B_C' - the generic instantiations `Map with (A_B, C)` and `Map with (A, B_C)` compile to that same name
```


<!-- test: error.extension-alias-pair-compiling-to-one-name -->
⭐ The SAME non-injective join, reached through the door the case above cannot reach: a `typealias`
declared INSIDE an `extension`, which is minted once per conforming type rather than once where it is
written. `Both = Pair with (K, V)` is one line of source; `One implements Duo with (A_B, C)` and
`Two implements Duo with (A, B_C)` make it two instantiations, and both join to `__Pair_A_B_C`.
⚠⚠ **THIS COMPILED CLEAN AND PRINTED A WRONG ANSWER**: measured before the fix, `2 77` where the
program packs `20 77` — the second mint silently CLOBBERED the first registration, so `One`'s `Both`
was laid out as `Pair with (A, B_C)` and `p.y.only` read the wrong field. ⚠ The diagnostic is
POSITIONLESS on purpose, and that is the one thing that differs from the case above: an extension
alias is minted by a loop that is no longer standing at any declaration the author wrote, so there is
no honest token to point at — the whole-program form is what `CompileError` carries for exactly that.
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

	export function packedSecond() returns Num
		let p = Both{x: first(), y: second()}
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
error E3006: duplicate definition of '__Pair_A_B_C' - the generic instantiations `Pair with (A_B, C)` and `Pair with (A, B_C)` compile to that same name
```
