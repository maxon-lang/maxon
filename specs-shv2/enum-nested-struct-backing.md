---
feature: enum-nested-struct-backing
status: experimental
keywords: [enum, struct, backing, rawValue, nested, composition]
category: type-system
---

# Enum Nested Struct Backing

## Documentation

Struct-backed enum raw values support nested struct literals. This enables composition where a target-specific backing type embeds a shared metadata struct.

```text
type Inner
	export let value as int(0 to 100)
end 'Inner'

type Outer
	export let inner as Inner
	export let flag as bool
end 'Outer'

enum Op
	add = Outer{inner: Inner{value: 1}, flag: true}
	nop = Outer{inner: Inner{value: 0}, flag: false}
end 'Op'

let op = Op.add
let v = op.rawValue.inner.value  // 1
let f = op.rawValue.flag         // true
```

## Tests

### Basic nested struct field access

<!-- test: nested-struct-backing-basic -->
```maxon
typealias Latency = int(0 to 50)

type OpMeta
	export let latency as Latency

	static function create(latency Latency) returns Self
		return Self{latency: latency}
	end 'create'
end 'OpMeta'

type X64OpMeta
	export let meta as OpMeta
	export let setsFlags as bool

	static function create(meta OpMeta, setsFlags bool) returns Self
		return Self{meta: meta, setsFlags: setsFlags}
	end 'create'
end 'X64OpMeta'

enum X64Op
	add = X64OpMeta.create(OpMeta.create(1), setsFlags: true)
	mov = X64OpMeta.create(OpMeta.create(3), setsFlags: false)
end 'X64Op'

function main() returns ExitCode
	let op = X64Op.add
	return op.rawValue.meta.latency
end 'main'
```
```exitcode
1
```

### Access outer field alongside nested struct

<!-- test: nested-struct-backing-outer-field -->
```maxon
typealias Latency = int(0 to 50)

type OpMeta
	export let latency as Latency

	static function create(latency Latency) returns Self
		return Self{latency: latency}
	end 'create'
end 'OpMeta'

type TargetMeta
	export let meta as OpMeta
	export let setsFlags as bool

	static function create(meta OpMeta, setsFlags bool) returns Self
		return Self{meta: meta, setsFlags: setsFlags}
	end 'create'
end 'TargetMeta'

enum Op
	add = TargetMeta.create(OpMeta.create(1), setsFlags: true)
	mov = TargetMeta.create(OpMeta.create(2), setsFlags: false)
end 'Op'

function main() returns ExitCode
	let op = Op.add
	if op.rawValue.setsFlags 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Multiple fields in nested struct

<!-- test: nested-struct-backing-multi -->
```maxon
typealias Latency = int(0 to 50)

type OpMeta
	export let latency as Latency
	export let isMemory as bool
	export let isCall as bool

	static function create(latency Latency, isMemory bool, isCall bool) returns Self
		return Self{latency: latency, isMemory: isMemory, isCall: isCall}
	end 'create'
end 'OpMeta'

type X64OpMeta
	export let meta as OpMeta
	export let setsFlags as bool

	static function create(meta OpMeta, setsFlags bool) returns Self
		return Self{meta: meta, setsFlags: setsFlags}
	end 'create'
end 'X64OpMeta'

enum X64Op
	load = X64OpMeta.create(OpMeta.create(4, isMemory: true, isCall: false), setsFlags: false)
	add = X64OpMeta.create(OpMeta.create(1, isMemory: false, isCall: false), setsFlags: true)
	call = X64OpMeta.create(OpMeta.create(5, isMemory: true, isCall: true), setsFlags: false)
end 'X64Op'

function main() returns ExitCode
	let op = X64Op.load
	if op.rawValue.meta.isMemory 'check'
		return op.rawValue.meta.latency
	end 'check'
	return 0
end 'main'
```
```exitcode
4
```

### Match on nested struct-backed enum

<!-- test: nested-struct-backing-match -->
```maxon
typealias Latency = int(0 to 50)

type Inner
	export let latency as Latency

	static function create(latency Latency) returns Self
		return Self{latency: latency}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner
	export let fast as bool

	static function create(inner Inner, fast bool) returns Self
		return Self{inner: inner, fast: fast}
	end 'create'
end 'Outer'

enum Op
	add = Outer.create(Inner.create(1), fast: true)
	div = Outer.create(Inner.create(40), fast: false)
end 'Op'

function main() returns ExitCode
	let op = Op.div
	return match op 'dispatch'
		add gives 10
		div gives 20
	end 'dispatch'
end 'main'
```
```exitcode
20
```

### Nested struct LITERAL as a field value

The spelling the Documentation section above writes — `Outer{inner: Inner{value: 1}, flag: true}` — which means
exactly what the factory form means and is read by the same reader.

<!-- test: nested-struct-backing-literal-form -->
```maxon
typealias Latency = int(0 to 50)

type Inner
	export let value as Latency
end 'Inner'

type Outer
	export let inner as Inner
	export let flag as bool
end 'Outer'

enum Op
	add = Outer{inner: Inner{value: 1}, flag: true}
	nop = Outer{inner: Inner{value: 0}, flag: false}
end 'Op'

function main() returns ExitCode
	let op = Op.add
	if op.rawValue.flag 'flagged'
		return op.rawValue.inner.value
	end 'flagged'
	return 9
end 'main'
```
```exitcode
1
```

### Three levels deep, through a runtime receiver

Composition is not limited to one level, the two spellings mix freely at every level, and the receiver need
not be a constant: the chain selects one scalar leaf out of the per-case constants whichever case it holds.

<!-- test: nested-struct-backing-three-levels -->
```maxon
typealias Latency = int(0 to 50)

type Cost
	export let cycles as Latency

	static function create(cycles Latency) returns Self
		return Self{cycles: cycles}
	end 'create'
end 'Cost'

type OpMeta
	export let cost as Cost
	export let isCall as bool

	static function create(cost Cost, isCall bool) returns Self
		return Self{cost: cost, isCall: isCall}
	end 'create'
end 'OpMeta'

type X64OpMeta
	export let meta as OpMeta

	static function create(meta OpMeta) returns Self
		return Self{meta: meta}
	end 'create'
end 'X64OpMeta'

enum X64Op
	add = X64OpMeta.create(OpMeta.create(Cost.create(4), isCall: false))
	call = X64OpMeta.create(OpMeta{cost: Cost.create(9), isCall: true})
end 'X64Op'

function cyclesOf(op X64Op) returns Latency
	return op.rawValue.meta.cost.cycles
end 'cyclesOf'

function main() returns ExitCode
	return cyclesOf(X64Op.call) - cyclesOf(X64Op.add)
end 'main'
```
```exitcode
5
```

### Error: a chain that stops on a nested record

A nested field is descended THROUGH, never selected: handing back the record itself would mint an owned heap
value inside an expression, which is the refusal bare `.rawValue` on a struct-backed enum already carries.

<!-- test: error.nested-struct-backing-whole-record -->
```maxon
typealias Wide = int(0 to 100)

type Inner
	export let n as Wide
end 'Inner'

type Meta
	export let inner as Inner
end 'Meta'

enum Task
	quick = Meta{inner: Inner{n: 1}}
end 'Task'

function main() returns ExitCode
	let t = Task.quick
	return t.rawValue.inner
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-nested-struct-backing/error.nested-struct-backing-whole-record.test:18:20: Unsupported: field 'inner' of the `Meta` backing read as a whole value — it is declared `Inner`, a record, and shv2 selects one SCALAR field of a struct backing at a time (`.inner.<field>`) rather than materializing one. That is the refusal `rawValue` itself carries, one level in: a materialized record is an owned heap value minted inside an expression
```

### Error: a leaf of a nested record whose declared type holds no constant

The field-type rule reaches EVERY leaf, not just the outermost fields: a `String` one level in is refused
exactly as a `String` at the top level is, and the diagnostic names the struct that declares it.

<!-- test: error.nested-struct-backing-leaf-type -->
```maxon
typealias Wide = int(0 to 100)

type Inner
	export let n as Wide
	export let label as String
end 'Inner'

type Meta
	export let inner as Inner
end 'Meta'

enum Task
	quick = Meta{inner: Inner{n: 1, label: 2}}
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-nested-struct-backing/error.nested-struct-backing-leaf-type.test:14:2: Unsupported: field 'label' of the `Inner` backing is declared `String`, which has no constant a struct backing can hold — a backing field is an integer, a `bool`, a float, a PAYLOAD-FREE enum, or a declared `type` whose own fields are again those, because the select that reads one field produces a single i64 per case and a nested record is DESCENDED INTO rather than selected. A `String`, a `Character`, an array, an interface, a function, or a payload-bearing union is a heap value that i64 would only be an address of
```

### Error: a nested constant short of one of ITS fields

Every rule the outermost group is checked by applies at every depth.

<!-- test: error.nested-struct-backing-missing-nested-field -->
```maxon
typealias Wide = int(0 to 100)

type Inner
	export let n as Wide
	export let m as Wide
end 'Inner'

type Meta
	export let inner as Inner
end 'Meta'

enum Task
	quick = Meta{inner: Inner{n: 1}}
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-nested-struct-backing/error.nested-struct-backing-missing-nested-field.test:14:2: Unsupported: field 'm' of the `Inner` backing, which case 'quick' of `enum Task` writes no value for — a struct backing supplies one constant PER CASE for every field, defaults included, because reading one field selects it from every case
```

### Error: a nested constant of a type the field does not declare

<!-- test: error.nested-struct-backing-wrong-nested-type -->
```maxon
typealias Wide = int(0 to 100)

type Inner
	export let n as Wide
end 'Inner'

type Other
	export let n as Wide
end 'Other'

type Meta
	export let inner as Inner
end 'Meta'

enum Task
	quick = Meta{inner: Other{n: 1}}
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-nested-struct-backing/error.nested-struct-backing-wrong-nested-type.test:17:2: Unsupported: the constant written for field 'inner' of the `Meta` backing, which is declared `Inner` — a backing field takes an INT literal for an integer field, a `true`/`false` for a `bool` field, an int or float literal for a float field, `<ThatEnum>.<case>` of THAT enum for a field of a declared enum, and a nested constant of THAT type (a literal or a factory call) for a field of a declared `type`
```

### Error: a scalar written for a field declared as a record

<!-- test: error.nested-struct-backing-scalar-for-record -->
```maxon
typealias Wide = int(0 to 100)

type Inner
	export let n as Wide
end 'Inner'

type Meta
	export let inner as Inner
end 'Meta'

enum Task
	quick = Meta{inner: 4}
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-nested-struct-backing/error.nested-struct-backing-scalar-for-record.test:13:2: Unsupported: the constant written for field 'inner' of the `Meta` backing, which is declared `Inner` — a backing field takes an INT literal for an integer field, a `true`/`false` for a `bool` field, an int or float literal for a float field, `<ThatEnum>.<case>` of THAT enum for a field of a declared enum, and a nested constant of THAT type (a literal or a factory call) for a field of a declared `type`
```

### Error: a nested constant written for a scalar field, reached by a READ first

The declaration door refuses this case in full — but a file that READS the enum can be parsed before the one
that declares it, so the read's own walk must report rather than assert. Here the reading function is
declared above the `enum`, which is what puts the read first.

<!-- test: error.nested-struct-backing-record-for-scalar -->
```maxon
typealias Wide = int(0 to 100)

type Inner
	export let n as Wide
end 'Inner'

type Meta
	export let count as Wide
end 'Meta'

function readIt(t Task) returns Wide
	return t.rawValue.count
end 'readIt'

enum Task
	quick = Meta{count: Inner{n: 1}}
end 'Task'

function main() returns ExitCode
	return readIt(Task.quick)
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-nested-struct-backing/error.nested-struct-backing-record-for-scalar.test:13:20: Unsupported: a nested `Inner` constant written for field 'count' of the `Meta` backing, which this read selects as a single word — a nested constant fills a field whose OWN declared type is that `type`, and this field's is not, so there is no number for the select to take
```

### Error: a label naming no field of the NESTED struct

<!-- test: error.nested-struct-backing-unknown-nested-field -->
```maxon
typealias Wide = int(0 to 100)

type Inner
	export let n as Wide
end 'Inner'

type Meta
	export let inner as Inner
end 'Meta'

enum Task
	quick = Meta{inner: Inner{count: 1}}
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3018: specs/fragments/enum-nested-struct-backing/error.nested-struct-backing-unknown-nested-field.test:13:2: type 'Inner' has no field named 'count'
```

### Error: more arguments than the NESTED struct declares fields

The count in the sentence is the group's, not the position of the first argument past the end — a
three-argument call on a one-field struct says three.

<!-- test: error.nested-struct-backing-nested-arity -->
```maxon
typealias Wide = int(0 to 100)

type Inner
	export let n as Wide

	static function create(n Wide) returns Self
		return Self{n: n}
	end 'create'
end 'Inner'

type Meta
	export let inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Meta'

enum Task
	quick = Meta.create(Inner.create(1, 2, 3))
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-nested-struct-backing/error.nested-struct-backing-nested-arity.test:21:2: Unsupported: 3 argument(s) to the `Inner` constant, which declares 1 field(s) — a factory call in this position is read as the struct LITERAL its arguments fill, so an argument past the last field names nothing
```

### Error: a field typed by a TUPLE alias

A tuple is a synthesized record, so it reaches the nested-record classification and is refused by the
identity check rather than by the domain — the constant written for it can never name the tuple's own
compiler-minted type.

<!-- test: error.nested-struct-backing-tuple-field -->
```maxon
typealias Wide = int(0 to 100)
typealias Pair = (Wide, Wide)

type Meta
	export let pair as Pair
	export let n as Wide
end 'Meta'

enum Task
	quick = Meta{pair: 1, n: 2}
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-nested-struct-backing/error.nested-struct-backing-tuple-field.test:11:2: Unsupported: the constant written for field 'pair' of the `Meta` backing, which is declared `__Tuple2.int.int` — a backing field takes an INT literal for an integer field, a `true`/`false` for a `bool` field, an int or float literal for a float field, `<ThatEnum>.<case>` of THAT enum for a field of a declared enum, and a nested constant of THAT type (a literal or a factory call) for a field of a declared `type`
```

### Error: a field typed by a LISTED STDLIB record

⚠ **THIS CASE USED TO PIN THE OPPOSITE SENTENCE, AND W115 IS WHY.** It read *"a compiler-owned record whose
DECLARED name is not the name a program writes, so the identity check refuses it whichever way the constant
is spelled"* — true while `CharacterSet` named the compiler's own layout under the reserved spelling
`__CharacterSet`, which no written constant could ever match. `stdlib/CharacterSet.maxon` is listed now, so
the bare name IS a declared record, the identity check ADMITS it, and the descent goes one level further
before refusing — landing on exactly the sentence its `Array` sibling below gets, at the same line:column.
That is the rung's whole thesis stated as a diagnostic: a retired builtin stops having a second rule.

<!-- test: error.nested-struct-backing-characterset-field -->
```maxon
typealias Wide = int(0 to 100)

type Meta
	export let chars as CharacterSet
	export let n as Wide
end 'Meta'

enum Task
	quick = Meta{chars: CharacterSet.create(), n: 2}
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-nested-struct-backing/error.nested-struct-backing-characterset-field.test:10:2: Unsupported: field 'chars' of the `CharacterSet` backing, which case 'quick' of `enum Task` writes no value for — a struct backing supplies one constant PER CASE for every field, defaults included, because reading one field selects it from every case
```

### Error: a field typed by a BARE generic, whose name a constant can match

`Array` written without type arguments is a declared record whose name a written constant CAN match, so this
is the one candidate that is admitted by the identity check and actually descends. The worst a wrongly
admitted record can do is exactly this: the descent reaches a field no constant column can stand for and the
case is refused there. Nothing is ever selected at the nested-record domain, so there is no wrong answer to
be had.

<!-- test: error.nested-struct-backing-bare-generic-field -->
```maxon
typealias Wide = int(0 to 100)

type Meta
	export let items as Array
	export let n as Wide
end 'Meta'

enum Task
	quick = Meta{items: Array.create(), n: 2}
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-nested-struct-backing/error.nested-struct-backing-bare-generic-field.test:10:2: Unsupported: field 'managed' of the `Array` backing, which case 'quick' of `enum Task` writes no value for — a struct backing supplies one constant PER CASE for every field, defaults included, because reading one field selects it from every case
```
