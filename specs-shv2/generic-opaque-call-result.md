---
feature: generic-opaque-call-result
status: stable
keywords: [generics, type-parameter, ownership, layout-descriptor, drop, substitution, dictionary]
category: type-system
---

# What a `returns <type parameter>` call gives back INSIDE a shared body

## Documentation

A `returns T` hand-off is the callee's `+1` and the caller adopts it
(`specs-shv2/generic-opaque-owned-return.md`). Both halves of that sentence are about the CALLER when
the caller is itself a shared generic body, and each has its own obligation:

- **what the result IS.** A call whose receiver is a generic INSTANCE has its opaque return substituted
  through that instance — `c.get()` on a `Cell with Num` gives a `Num`. When the instance's argument at
  that position is the CALLER'S OWN type parameter, the substitution fixes nothing and the result must
  stay the type parameter it was. An instance over its own parameters is the identity substitution, and
  a substitution that loses that identity leaves a value the body can no longer prove is a `T` — so a
  `where Element is Equatable` comparison against a genuine `Element` is refused on a program in which
  both operands are `Element`.

- **what the result OWES.** The adopted `+1` is released at scope exit through the descriptor-gated
  `__drop_type_param`, so a body that adopts one needs a layout descriptor — the same reservation a
  move-out (`pop`/`remove`) and a borrowed-`T` return already carry.

## Tests

### An opaque result adopted from a nested instance is released

`Holder uses T` holds a `Cell with T`. `cell.get()` is a `returns U` hand-off substituted through an
instance whose argument is `T`, so the caller adopts a `+1` on the enclosing `T` and owes exactly one
`__drop_type_param` — read out of `Holder`'s own descriptor, which the method must therefore carry.

<!-- test: an-opaque-result-adopted-from-a-nested-instance-is-released -->
```maxon
type Cell uses U
	export var slot as U

	export static function make(slot U) returns Self
		return Self{slot: slot}
	end 'make'

	export function get() returns U
		return slot
	end 'get'
end 'Cell'

type Holder uses T where T is Equatable
	typealias TCell = Cell with T
	export var cell as TCell

	export static function make(cell TCell) returns Self
		return Self{cell: cell}
	end 'make'

	export function holds(other T) returns bool
		let mine = cell.get()
		return mine == other
	end 'holds'
end 'Holder'

typealias StrCell = Cell with String
typealias StrHolder = Holder with String

function main() returns ExitCode
	let h = StrHolder.make(StrCell.make("a held string long enough to force a heap allocation"))
	if not h.holds("a held string long enough to force a heap allocation") 'mismatch'
		return 1
	end 'mismatch'
	if h.holds("a different string long enough to force a heap allocation") 'falsePositive'
		return 2
	end 'falsePositive'
	print("held\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
held
```

### A trivial instantiation of the same shape is inert

The identical bodies at `Holder with Num`. The adopted result owes a `__drop_type_param` whose
`destroyFunc@40` is the zero word, so the release is a load and nothing else.

<!-- test: a-trivial-instantiation-of-an-adopted-opaque-result-is-inert -->
```maxon
typealias Num = int(0 to 1000)

type Cell uses U
	export var slot as U

	export static function make(slot U) returns Self
		return Self{slot: slot}
	end 'make'

	export function get() returns U
		return slot
	end 'get'
end 'Cell'

type Holder uses T where T is Equatable
	typealias TCell = Cell with T
	export var cell as TCell

	export static function make(cell TCell) returns Self
		return Self{cell: cell}
	end 'make'

	export function holds(other T) returns bool
		let mine = cell.get()
		return mine == other
	end 'holds'
end 'Holder'

typealias NumCell = Cell with Num
typealias NumHolder = Holder with Num

function main() returns ExitCode
	let h = NumHolder.make(NumCell.make(7 as Num))
	if not h.holds(7 as Num) 'mismatch'
		return 1
	end 'mismatch'
	if h.holds(8 as Num) 'falsePositive'
		return 2
	end 'falsePositive'
	return 0
end 'main'
```
```exitcode
0
```

### A record whose `Self` IS its own instance keeps its type parameter opaque

A declared `type Array` is its `__ManagedMemory`, so `Self` inside its body is the INSTANCE over the
declaration's own parameters rather than the base
(`specs-shv2/array-declared-record.md`). A sibling call declared `returns Element` therefore has its
result substituted through `Array with Element` — the identity — and must come back as `Element`.
Substituted to anything else, `mine == element` under `where Element is Equatable` is refused with
*"requires an argument of type 'Element' … not a concrete value"* on a program whose operands are both
`Element`.

The program's own answer comes through the compiler's `Array` surface, which is what a user-declared
`Array` member still falls through to; what this case pins is that the declared body COMPILES and the
record it describes is destroyed exactly once.

<!-- test: a-self-is-its-own-instance-record-keeps-its-parameter-opaque -->
```maxon
typealias Idx = int(0 to u64.max)

enum BagError implements Error
	oob
end 'BagError'

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function count() returns Idx
		return managed.length()
	end 'count'

	export function at(i Idx) returns Element throws BagError
		return try managed.get(i) otherwise throw BagError.oob
	end 'at'
end 'Array'

extension Array where Element is Equatable
	export function contains(element Element) returns bool
		let mine = try at(0) otherwise return false
		return mine == element
	end 'contains'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var b = StrArray.create()
	b.push("a stored string long enough to force a heap allocation")
	if not b.contains("a stored string long enough to force a heap allocation") 'missing'
		return 1
	end 'missing'
	print("found\n")
	return b.count() as ExitCode
end 'main'
```
```exitcode
1
```
```stdout
found
```
