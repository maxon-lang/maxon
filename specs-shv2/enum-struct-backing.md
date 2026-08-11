---
feature: enum-struct-backing
status: experimental
keywords: [enum, struct, backing, rawValue, metadata]
category: type-system
---

# Enum Struct Backing

## Documentation

Enums can use struct literals as backing values. Each case carries compile-time constant struct metadata accessible via `.rawValue`. At runtime, the enum is stored as an ordinal (i64) — the struct is reconstructed from constant data on `.rawValue` access.

```text
type Meta
	export let latency as int(0 to 50)
end 'Meta'

enum Instruction
	add = Meta{latency: 1}
	mul = Meta{latency: 3}
end 'Instruction'

let op = Instruction.mul
let lat = op.rawValue.latency  // 3
```

All cases must use the same struct type. Struct field values must be compile-time integer, float, or boolean constants, or nested struct literals.

## Tests

### Basic rawValue field access

<!-- test: struct-backing-basic -->
```maxon
typealias Latency = int(0 to 50)

type Meta
	export let value as Latency

	static function create(value Latency) returns Self
		return Self{value: value}
	end 'create'
end 'Meta'

enum TestOp
	add = Meta.create(1)
	mul = Meta.create(3)
end 'TestOp'

function main() returns ExitCode
	let op = TestOp.mul
	return op.rawValue.value
end 'main'
```
```exitcode
3
```

### rawValue field access on a non-constant receiver

A `.rawValue.<field>` chain works even when the receiver is NOT a compile-time
constant case — e.g. a function parameter, a method-call result, or a field. The
parser can fold the access to a constant only when the receiver was bound to a
known case (`let op = TestOp.mul`); for a runtime value the backing struct is
reconstructed from the ordinal at runtime (a select-chain over the per-case
constants). This exercises the runtime path.

<!-- test: struct-backing-rawvalue-nonconst-receiver -->
```maxon
typealias Latency = int(0 to 50)

type Meta
	export let value as Latency
end 'Meta'

enum TestOp
	add = Meta{value: 1}
	mul = Meta{value: 3}
end 'TestOp'

// `op` is a parameter — not a compile-time-known case — so `op.rawValue.value`
// cannot be const-folded and must materialize the backing struct at runtime.
function latencyOf(op TestOp) returns Latency
	return op.rawValue.value
end 'latencyOf'

function main() returns ExitCode
	return latencyOf(TestOp.mul)
end 'main'
```
```exitcode
3
```

### Multiple struct fields

<!-- test: struct-backing-multi-field -->
```maxon
typealias Latency = int(0 to 100)
typealias Throughput = int(0 to 10)

type OpInfo
	export let latency as Latency
	export let throughput as Throughput

	static function create(latency Latency, throughput Throughput) returns Self
		return Self{latency: latency, throughput: throughput}
	end 'create'
end 'OpInfo'

enum Instruction
	add = OpInfo.create(1, throughput: 1)
	mul = OpInfo.create(3, throughput: 2)
	div = OpInfo.create(40, throughput: 1)
end 'Instruction'

function main() returns ExitCode
	let op = Instruction.div
	return op.rawValue.latency
end 'main'
```
```exitcode
40
```

### Ordinal access on struct-backed enum

<!-- test: struct-backing-ordinal -->
```maxon
typealias Weight = int(0 to 100)

type Info
	export let weight as Weight

	static function create(weight Weight) returns Self
		return Self{weight: weight}
	end 'create'
end 'Info'

enum Priority
	low = Info.create(1)
	medium = Info.create(5)
	high = Info.create(10)
end 'Priority'

function main() returns ExitCode
	let p = Priority.high
	return p.ordinal
end 'main'
```
```exitcode
2
```

### Name access on struct-backed enum

<!-- test: struct-backing-name -->
```maxon
typealias Cost = int(0 to 100)

type Metadata
	export let cost as Cost

	static function create(cost Cost) returns Self
		return Self{cost: cost}
	end 'create'
end 'Metadata'

enum Op
	read = Metadata.create(1)
	write = Metadata.create(5)
end 'Op'

function main() returns ExitCode
	let op = Op.write
	// `.name` on a struct-backed enum is still the case name; print it
	// (comparing the accessor is E3097).
	print("{op.name}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
write
```

### Match on struct-backed enum

<!-- test: struct-backing-match -->
```maxon
typealias Latency = int(0 to 50)

type Meta
	export let latency as Latency

	static function create(latency Latency) returns Self
		return Self{latency: latency}
	end 'create'
end 'Meta'

enum TestOp
	add = Meta.create(1)
	sub = Meta.create(1)
	mul = Meta.create(3)
end 'TestOp'

function main() returns ExitCode
	let op = TestOp.sub
	return match op 'dispatch'
		add gives 10
		sub gives 20
		mul gives 30
	end 'dispatch'
end 'main'
```
```exitcode
20
```

### Throughput from second field

<!-- test: struct-backing-second-field -->
```maxon
typealias Latency = int(0 to 100)
typealias Throughput = int(0 to 10)

type OpInfo
	export let latency as Latency
	export let throughput as Throughput

	static function create(latency Latency, throughput Throughput) returns Self
		return Self{latency: latency, throughput: throughput}
	end 'create'
end 'OpInfo'

enum Instruction
	add = OpInfo.create(1, throughput: 2)
	mul = OpInfo.create(3, throughput: 1)
end 'Instruction'

function main() returns ExitCode
	let op = Instruction.add
	return op.rawValue.throughput
end 'main'
```
```exitcode
2
```

### Enum member reference as struct field value

<!-- test: struct-backing-enum-field -->
```maxon
typealias Cost = int(0 to 100)

enum Priority
	low
	medium
	high
end 'Priority'

type TaskInfo
	export let priority as Priority
	export let cost as Cost
end 'TaskInfo'

enum Task
	quick = TaskInfo{priority: Priority.low, cost: 1}
	normal = TaskInfo{priority: Priority.medium, cost: 5}
	heavy = TaskInfo{priority: Priority.high, cost: 10}
end 'Task'

function main() returns ExitCode
	let t = Task.heavy
	return t.rawValue.priority.ordinal
end 'main'
```
```exitcode
2
```

### Enum member reference in factory call

<!-- test: struct-backing-enum-factory -->
```maxon
typealias Level = int(0 to 100)

enum Mode
	fast
	slow
end 'Mode'

type Config
	export let mode as Mode
	export let level as Level

	static function create(mode Mode, level Level) returns Self
		return Self{mode: mode, level: level}
	end 'create'
end 'Config'

enum Setting
	turbo = Config.create(Mode.fast, level: 10)
	eco = Config.create(Mode.slow, level: 3)
end 'Setting'

function main() returns ExitCode
	let s = Setting.turbo
	return s.rawValue.mode.ordinal
end 'main'
```
```exitcode
0
```

### Error: mixed backing types

<!-- test: error.struct-backing-mixed -->
```maxon
typealias Value = int(0 to 100)

type Meta
	export let value as Value

	static function create(value Value) returns Self
		return Self{value: value}
	end 'create'
end 'Meta'

enum Mixed
	a = Meta.create(1)
	b = 42
end 'Mixed'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/enum-struct-backing/error.struct-backing-mixed.test:14:2: raw value type mismatch: 'expected Meta, got int'
```

### Error: fromRawValue blocked

<!-- test: error.struct-backing-fromRawValue -->
```maxon
typealias Value = int(0 to 100)

type Meta
	export let value as Value

	static function create(value Value) returns Self
		return Self{value: value}
	end 'create'
end 'Meta'

enum TestOp
	add = Meta.create(1)
end 'TestOp'

function main() returns ExitCode
	let op = try TestOp.fromRawValue(1) otherwise TestOp.add
	return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/enum-struct-backing/error.struct-backing-fromRawValue.test:17:15: unknown enum case: 'fromRawValue'
```


<!-- test: struct-backing-keyword-enum-case-field -->
A struct-backing field value may reference a keyword-spelled enum case
(`Cat.bool`, `Cat.int`). The enum spec permits keyword tokens as case names, so
a const-expression `EnumName.case` member must accept an identifier-like token
after the dot, not only a strict identifier — otherwise the `.bool` mis-parses.
This mirrors the compiler's own `StdType` enum, whose struct backings carry
`CastCategory.bool` / `CastCategory.int`.
```maxon
enum Cat
	bool
	int
end 'Cat'

type Info
	export let cat as Cat
end 'Info'

enum Kind
	a = Info{cat: Cat.bool}
	b = Info{cat: Cat.int}
end 'Kind'

function classify(k Kind) returns ExitCode
	return match k 'm'
		a gives 4
		b gives 1
	end 'm'
end 'classify'

function main() returns ExitCode
	return classify(Kind.b)
end 'main'
```
```exitcode
1
```


## shv2 additions — reads the canonical file does not write

The two cases below are **shv2 additions**, not part of the canonical `/specs` file. Both pin spellings that
were REFUSED until `/specs/union-struct-backing.md` forced the type-qualified read to be built, and both are
served by the same one door as the spellings above — so an unpinned half here is exactly how such a fix
becomes regressible in one edit.

### A backing field read off a case named through its TYPE

`Preset.large.width` reads the metadata without ever binding a value, which is `p.width` with the receiver
written as `<Enum>.<case>` instead of as a local. It used to report `E2010 Expected '(' but got 'newline'`,
because the type-qualified door admitted only `name`/`ordinal`/`rawValue` while the binding door already
admitted a backing FIELD. The bootstrap compiles it and answers 9 (MEASURED).

<!-- test: struct-backing-type-qualified-field -->
```maxon
typealias W = int(0 to 50)

type Spec
	export let width as W
end 'Spec'

enum Preset
	small = Spec{width: 3}
	large = Spec{width: 9}
end 'Preset'

function main() returns ExitCode
	return Preset.large.width
end 'main'
```
```exitcode
9
```

### A field of a backing field's own backing

`t.size.width` continues the chain through a field that is ITSELF a struct-backed enum: `Theme.bold`'s
`size` is `Size.large`, whose backing declares `width`. The hop past a selected enum-typed field admitted
only the three accessors before this, so `.ordinal` worked and `.width` did not — one rule for two doors
closes that.

⛔ **WRITING THIS CASE TOOK THE C# BOOTSTRAP DOWN, and that defect is fixed in the same commit.** Every
spelling below crashed it with `E9001 Value cannot be null. (Parameter 'key')` in `ParseFieldAccessChain`
— including with a payload-free `Size`, so it was never about struct backing twice over. The SHORTHAND
`t.size` computed its result type name with a hand-rolled `is IrStructType ? .Name : null`, a one-arm copy
of `GetFieldStructName`, so an ENUM-typed backing field was minted as a `MaxonEnum` with a null `TypeName`.
The long spelling `t.rawValue.size.ordinal` routes elsewhere and is pinned by the canonical file, which is
why the suite never saw it. Both compilers now print `9 1 3`.

<!-- test: struct-backing-enum-field-chain -->
```maxon
typealias W = int(0 to 50)

type Spec
	export let width as W
end 'Spec'

enum Size
	small = Spec{width: 3}
	large = Spec{width: 9}
end 'Size'

type Style
	export let size as Size
end 'Style'

enum Theme
	plain = Style{size: Size.small}
	bold = Style{size: Size.large}
end 'Theme'

function main() returns ExitCode
	let t = Theme.bold
	print("{t.size.width} {t.rawValue.size.ordinal} {Theme.plain.size.width}")
	return 0
end 'main'
```
```stdout
9 1 3
```


## shv2 refusals

The cases below are **shv2 additions**, not part of the canonical `/specs` file. Each pins a refusal this
compiler's declaration door raises, and each exists because the behaviour it replaces was measured: a struct
backing whose field type or field list the constant column cannot represent used to compile CLEANLY and fail
at run time or crash the compiler. A green suite said nothing about any of them, so they are pinned here.

### Error: a field type no constant column can hold

The backing struct's fields become per-case CONSTANTS selected as one i64, so a field whose declared type is
a heap value has no constant to be. Before this refusal, `Meta{label: 1}` on a `String` field compiled and
ACCESS-VIOLATED at run time, dereferencing a `String` record address of 1.

<!-- test: error.struct-backing-field-type -->
```maxon
type Meta
	export let label as String
end 'Meta'

enum Tag
	first = Meta{label: 1}
end 'Tag'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-struct-backing/error.struct-backing-field-type.test:7:2: Unsupported: field 'label' of the `Meta` backing is declared `String`, which has no constant a struct backing can hold — a backing field is an integer, a `bool`, a float, a PAYLOAD-FREE enum, or a declared `type` whose own fields are again those, because the select that reads one field produces a single i64 per case and a nested record is DESCENDED INTO rather than selected. A `String`, a `Character`, an array, an interface, a function, or a payload-bearing union is a heap value that i64 would only be an address of
```

### Error: a payload-bearing union as a backing field

A PAYLOAD-FREE enum is a legal backing field — its value is its i64 tag. A payload-bearing union is not: it
is heap-boxed, and every case takes the boxed form, so its value is a box POINTER. Both live in one registry,
which is exactly how this was admitted: the program below compiled and exited with an access violation,
`match`ing a box pointer whose address was the tag 1.

<!-- test: error.struct-backing-boxed-union-field -->
```maxon
typealias Num = int(0 to 99)

union Shape
	circle(r Num)
	blank
end 'Shape'

type Meta
	export let s as Shape
end 'Meta'

enum Kind
	only = Meta{s: Shape.blank}
end 'Kind'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-struct-backing/error.struct-backing-boxed-union-field.test:14:2: Unsupported: field 's' of the `Meta` backing is declared `Shape`, which has no constant a struct backing can hold — a backing field is an integer, a `bool`, a float, a PAYLOAD-FREE enum, or a declared `type` whose own fields are again those, because the select that reads one field produces a single i64 per case and a nested record is DESCENDED INTO rather than selected. A `String`, a `Character`, an array, an interface, a function, or a payload-bearing union is a heap value that i64 would only be an address of
```

### Error: a case that omits a field

Reading one field selects that field's constant from EVERY case, so every case must supply one. A field
DEFAULT does not excuse it.

<!-- test: error.struct-backing-missing-field -->
```maxon
typealias Wide = int(0 to 100)

type Meta
	export let value as Wide
	export let cost as Wide
end 'Meta'

enum Task
	quick = Meta{value: 1}
	slow = Meta{value: 2, cost: 5}
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-struct-backing/error.struct-backing-missing-field.test:10:2: Unsupported: field 'cost' of the `Meta` backing, which case 'quick' of `enum Task` writes no value for — a struct backing supplies one constant PER CASE for every field, defaults included, because reading one field selects it from every case
```

### Error: a label naming no declared field

<!-- test: error.struct-backing-unknown-field -->
```maxon
typealias Wide = int(0 to 100)

type Meta
	export let value as Wide
end 'Meta'

enum Task
	quick = Meta{bogus: 1}
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3018: specs/fragments/enum-struct-backing/error.struct-backing-unknown-field.test:9:2: type 'Meta' has no field named 'bogus'
```

### Error: one field written twice

The second constant would be silently dropped, since the field's value is resolved by the first entry that
fills it.

<!-- test: error.struct-backing-duplicate-field -->
```maxon
typealias Wide = int(0 to 100)

type Meta
	export let value as Wide
end 'Meta'

enum Task
	quick = Meta{value: 1, value: 2}
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3018: specs/fragments/enum-struct-backing/error.struct-backing-duplicate-field.test:9:2: field 'value' of 'Meta' is initialized twice by this literal
```

### Error: more factory arguments than the struct has fields

A factory call in a raw-value position is read as the struct LITERAL its arguments fill, so an argument past
the last field names nothing.

<!-- test: error.struct-backing-arity -->
```maxon
typealias Wide = int(0 to 100)

type Meta
	export let value as Wide
end 'Meta'

enum Task
	quick = Meta.create(1, 2)
end 'Task'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-struct-backing/error.struct-backing-arity.test:9:2: Unsupported: 2 argument(s) to the `Meta` constant, which declares 1 field(s) — a factory call in this position is read as the struct LITERAL its arguments fill, so an argument past the last field names nothing
```

### Error: bare `.rawValue` on a struct-backed enum

shv2 selects one FIELD of the backing at a time and does not materialize the record, so `.rawValue` with no
field after it names a value this compiler does not build. Both reference compilers DO materialize it; the
refusal says so rather than implying the value cannot exist.

<!-- test: error.struct-backing-bare-rawvalue -->
```maxon
typealias Wide = int(0 to 100)

type Meta
	export let value as Wide
end 'Meta'

enum Task
	quick = Meta{value: 1}
end 'Task'

function main() returns ExitCode
	let t = Task.quick
	let r = t.rawValue
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/enum-struct-backing/error.struct-backing-bare-rawvalue.test:14:12: Unsupported: `rawValue` on `enum Task` read as a whole value — its raw value is a `Meta` record, which shv2 selects one FIELD of at a time (`.rawValue.<field>`) rather than materializing. Both reference compilers do materialize it; doing so here means minting an owned heap record inside an expression, and no case in `/specs/enum-struct-backing.md` asks for one
```
