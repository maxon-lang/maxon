---
feature: static-variables
status: experimental
keywords: [static, var, global, mutable, module, type]
category: language
---
# Static Variables

## Documentation

### Top-Level `var` Declarations

Top-level `var` declarations define mutable module-level variables. Unlike `let` constants which are compile-time evaluated and stored in read-only memory, `var` declarations create mutable storage in the program's data section.

#### Syntax

```maxon
var counter = 0
export var globalState = false
```

#### Features

- **Runtime storage**: Variables are stored in the writable data section
- **Initialization**: Initializers are evaluated at program start before `main`
- **Type inference**: Type is inferred from the initializer
- **Export support**: Use `export var` to make variables available to other modules

#### Initializer Requirements

Top-level `var` initializers must be constant expressions (same rules as `let`):
- Literals: integers, floats, booleans, strings, bytes, characters
- Arithmetic and logical operations on constants
- References to other top-level constants
- Enum member access

Function calls and runtime expressions are not allowed in initializers.

### Static Fields in Types

Types can have static fields that are shared across all instances. Static fields use the `static` keyword before `var` or `let`.

#### Syntax

```maxon
typealias Score = int(i64.min to i64.max)

type Counter
	static var count = 0       // Mutable static field
	static let MAX = 100       // Compile-time static constant

	export var value as Score       // Instance field
end 'Counter'
```

#### Features

- **Shared storage**: One copy exists for the type, not per instance
- **Direct access**: Access via `TypeName.fieldName` syntax
- **Static let**: Compile-time constant (same as top-level `let`)
- **Static var**: Mutable storage (same as top-level `var`)

#### Access Patterns

```maxon
Counter.count = Counter.count + 1   // Access static field
var c = Counter.create(10)          // Create instance
c.value = 20                        // Access instance field
```

## Tests

<!-- test: top-level-var-basic -->
```maxon
var counter = 0

function main() returns ExitCode
	counter = 42
	return counter
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-increment -->
```maxon

typealias Integer = int(i64.min to i64.max)

var total = 10

function add(n Integer)
	total = total + n
end 'add'

function main() returns ExitCode
	add(5)
	add(27)
	return total
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-multiple -->
```maxon
var a = 1
var b = 2
var c = 3

function main() returns ExitCode
	a = a * 10
	b = b * 10
	c = c * 10
	return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: top-level-var-with-let -->
```maxon
let BASE = 40
var offset = 0

function main() returns ExitCode
	offset = 2
	return BASE + offset
end 'main'
```
```exitcode
42
```

<!-- test: static-var-basic -->
```maxon
type Counter
	static var count = 0
end 'Counter'

function main() returns ExitCode
	Counter.count = 42
	return Counter.count
end 'main'
```
```exitcode
42
```

<!-- test: static-var-increment -->
```maxon
type Counter
	static var count = 0

	static function increment()
		Counter.count = Counter.count + 1
	end 'increment'
end 'Counter'

function main() returns ExitCode
	Counter.increment()
	Counter.increment()
	Counter.increment()
	return Counter.count
end 'main'
```
```exitcode
3
```

<!-- test: static-let-basic -->
```maxon
type Config
	static let MAX_SIZE = 42
end 'Config'

function main() returns ExitCode
	return Config.MAX_SIZE
end 'main'
```
```exitcode
42
```

<!-- test: static-var-multiple-types -->
```maxon
type TypeA
	static var value = 10
end 'TypeA'

type TypeB
	static var value = 20
end 'TypeB'

function main() returns ExitCode
	TypeA.value = TypeA.value + 2
	TypeB.value = TypeB.value + 10
	return TypeA.value + TypeB.value
end 'main'
```
```exitcode
42
```

<!-- test: static-and-instance-fields -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Thing
	static var created = 0
	export var id as Integer

	static function make(n Integer) returns Thing
		Thing.created = Thing.created + 1
		return Thing{id: n}
	end 'make'
end 'Thing'

function main() returns ExitCode
	let a = Thing.make(10)
	let b = Thing.make(20)
	return Thing.created + a.id + b.id
end 'main'
```
```exitcode
32
```

<!-- test: static-var-bool -->
```maxon
var initialized = false

function init()
	initialized = true
end 'init'

function main() returns ExitCode
	if initialized 'check1'
		return 1
	end 'check1'
	init()
	if initialized 'check2'
		return 42
	end 'check2'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: static-var-bool-adjacent-globals -->
Bool global followed by non-zero global must not bleed adjacent data.

```maxon
var flag = false
var counter = 42

function main() returns ExitCode
	if flag 'checkFalse'
		print("flag should be false\n")
		return 1
	end 'checkFalse'
	if counter == 42 'checkCounter'
		return 0
	end 'checkCounter'
	print("counter wrong\n")
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: top-level-var-enum-initializer -->
```maxon
enum Color
		Red
		Green
		Blue
end 'Color'

var current = Color.Green

function main() returns ExitCode
	let isGreen = match current 'check'
		Green gives true
		Red gives false
		Blue gives false
	end 'check'
	if isGreen 'check'
		current = Color.Blue
		let isBlue = match current 'check2'
			Blue gives true
			Red gives false
			Green gives false
		end 'check2'
		if isBlue 'check2'
			return 42
		end 'check2'
	end 'check'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-enum-initializer-cross-file -->
Cross-file: enum defined in one file, top-level var initialized with it in another.
```maxon
// --- file: api/defs.maxon
export enum CpuArch
	x64
	arm64
	wasm32
end 'CpuArch'

// --- file: app/main.maxon
var currentCpu = CpuArch.x64

function main() returns ExitCode
	let result = match currentCpu 'check'
		x64 gives 42
		arm64 gives 1
		wasm32 gives 2
	end 'check'
	return result
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-const-expr -->
```maxon
let BASE = 20
var offset = BASE + 1

function main() returns ExitCode
	offset = offset * 2
	return offset
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-array-literal -->
```maxon
var items = [10, 20, 30]

function main() returns ExitCode
	try items.set(1, value: 12) otherwise panic("test invariant: set OOB")
	let a = try items.get(0) otherwise 0
	let b = try items.get(1) otherwise 0
	let c = try items.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
52
```

<!-- test: top-level-var-array-cross-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

var scores = [10, 20, 30]

function getTotal() returns Integer
	let a = try scores.get(0) otherwise 0
	let b = try scores.get(1) otherwise 0
	let c = try scores.get(2) otherwise 0
	return a + b + c
end 'getTotal'

function setScore(index Integer, value Integer)
	try scores.set(index, value: value) otherwise panic("test invariant: set OOB")
end 'setScore'

function main() returns ExitCode
	setScore(1, value: 12)
	return getTotal()
end 'main'
```
```exitcode
52
```

<!-- test: top-level-var-array-mutate-cross-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

var counters = [0, 0, 0]

function increment(index Integer)
	let current = try counters.get(index) otherwise 0
	try counters.set(index, value: current + 1) otherwise panic("test invariant: set OOB")
end 'increment'

function total() returns Integer
	let a = try counters.get(0) otherwise 0
	let b = try counters.get(1) otherwise 0
	let c = try counters.get(2) otherwise 0
	return a + b + c
end 'total'

function main() returns ExitCode
	increment(0)
	increment(0)
	increment(1)
	increment(2)
	increment(2)
	increment(2)
	return total()
end 'main'
```
```exitcode
6
```

<!-- test: top-level-var-string-literal -->
A top-level `var` string is valid and reassignable — it materializes once at startup, like an
array-literal global.
```maxon
var greeting = "hello"

function main() returns ExitCode
	print("{greeting} ")
	greeting = "world"
	print(greeting)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world
```

<!-- test: top-level-var-string-mutate-cross-function -->
A `var` string global mutated in place across a function boundary must NOT be shared as an
immortal static record: it mutates correctly and frees cleanly (no leaked copy-on-write buffer).
```maxon
var msg = "hi"

function bump()
	msg.append("!")
end 'bump'

function main() returns ExitCode
	bump()
	bump()
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi!!
```

<!-- test: top-level-let-string-literal -->
```maxon
let name = "Ada"

function main() returns ExitCode
	print(name)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Ada
```

<!-- test: data-section-bool-1byte -->
A single bool global occupies 1 byte in the .data section.

```maxon
var flag = true

function main() returns ExitCode
	if flag 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i8 1
```

<!-- test: data-section-i64-8byte -->
A single i64 global occupies 8 bytes in the .data section.

```maxon
var counter = 42

function main() returns ExitCode
	return counter - 42
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
```

<!-- test: data-section-f64-8byte -->
A single f64 global occupies 8 bytes in the .data section.

```maxon
var pi = 3.14

function main() returns ExitCode
	if pi > 3.0 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
f64 3.14
```

<!-- test: data-section-bool-then-i64-sorted -->
A bool and i64 global: sorted largest-first, no padding needed.

```maxon
var flag = false
var counter = 42

function main() returns ExitCode
	if flag 'read'
		return 1
	end 'read'
	return counter - 42
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i8 0
```

<!-- test: data-section-bool-true-then-i64 -->
A true bool and i64: sorted largest-first, no padding needed.

```maxon
var flag = true
var counter = 99

function main() returns ExitCode
	if flag 'read'
		return counter - 99
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i64 99
i8 1
```

<!-- test: data-section-i64-then-bool -->
An i64 followed by a bool: no padding needed since bool has 1-byte alignment.

```maxon
var counter = 7
var flag = true

function main() returns ExitCode
	if flag 'read'
		return counter - 7
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i64 7
i8 1
```

<!-- test: data-section-multiple-bools -->
Multiple consecutive bools occupy 1 byte each with no padding.

```maxon
var a = true
var b = false
var c = true

function main() returns ExitCode
	if a and c and (b == false) 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i8 1
i8 0
i8 1
```

<!-- test: data-section-mixed-types -->
Mixed bool, i64, f64 globals sorted largest-first, no padding.

```maxon
var flag = true
var count = 10
var ratio = 2.5

function main() returns ExitCode
	if flag and (count == 10) and (ratio > 2.0) 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i64 10
f64 2.5
i8 1
```

<!-- test: top-level-var-byte-ranged-type -->
Module-level var with a byte-sized ranged type.
```maxon
typealias SmallInt = int(0 to u8.max)

var counter = 42 as SmallInt

function main() returns ExitCode
		return counter
end 'main'
```
```exitcode
42
```

<!-- test: top-level-let-struct-reassign-error -->
Reassigning an immutable top-level `let` struct variable should error.
```maxon
typealias SmallInt = int(0 to u8.max)

type Point
		export var x as SmallInt
		export var y as SmallInt

		static function create(x SmallInt, y SmallInt) returns Self
			return Self{x: x, y: y}
		end 'create'
end 'Point'

let origin = Point.create(0, y: 0)

function main() returns ExitCode
		origin = Point.create(1, y: 1)
		return 0
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/static-variables/top-level-let-struct-reassign-error.test:16:3: cannot assign to immutable variable: 'origin'
```

<!-- test: top-level-var-function-call-error -->
Function calls are not allowed in module-level `var` initializers.
```maxon
typealias Integer = int(i64.min to i64.max)

function getDefault() returns Integer
	return 42
end 'getDefault'

var value = getDefault()

function main() returns ExitCode
	return value
end 'main'
```
```maxoncstderr
error E2045: specs/fragments/static-variables/top-level-var-function-call-error.test:8:13: Function calls are not allowed in global variable initializers; 'getDefault()' is not a constant expression
```

<!-- test: literal-in-a-struct-field-is-not-static -->
A literal stored into a **struct field** must not become a shared immortal record. Whoever holds the
struct can mutate the field in place — `h.name.append("!")` — which the per-function escape analysis
cannot see, exactly as it cannot see a mutable global mutated in another function.

Left eligible, the damage is **silent and not local**: literals are interned, so both `"fld"` below are
ONE static record; `append` finds `capacity == -2`, detaches, and writes the fresh buffer **into that
shared record**. `untouched` — which nothing ever touched — then reads `"fld!"`, and the buffer leaks
(exit 101) because an immortal record's destructor is 0. Both were real on `2122a9471`.

Nothing else catches this: the plan's `.rodata` safety net does not exist, because a data→data pointer
cannot be baked under ASLR, so static records live in **writable** `.data` and the write succeeds
quietly. **The escape analysis is the only guard.**
```maxon
type Holder
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create("fld")
	h.name.append("!")
	let untouched = "fld"
	print("h.name={h.name} untouched={untouched}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
h.name=fld! untouched=fld
```

<!-- test: top-level-let-duplicate-declaration-error -->
Declaring the same top-level `let` name twice in one file is a duplicate definition (E3006),
positioned at the LATER declaration — the top-level twin of the duplicate-FUNCTION check. Top-level
value storage is first-wins, so the first declaration keeps the name and the diagnostic names the
redeclaration to remove.
```maxon
let A = 1
let A = 2

function main() returns ExitCode
	return A
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/static-variables/top-level-let-duplicate-declaration-error.test:3:5: duplicate definition of 'A'
```

<!-- test: top-level-var-duplicate-declaration-error -->
The same rule applies to a mutable `var`: two top-level `var` declarations of one name in one file
collide, and the second is the duplicate.
```maxon
var counter = 0
var counter = 5

function main() returns ExitCode
	return counter
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/static-variables/top-level-var-duplicate-declaration-error.test:3:5: duplicate definition of 'counter'
```

<!-- test: top-level-var-let-duplicate-declaration-error -->
Top-level value storage is kind-independent, so a `var` and a `let` sharing one name in one file
collide the same way — the second declaration is the duplicate regardless of which keyword introduces
it.
```maxon
var counter = 0
let counter = 5

function main() returns ExitCode
	return counter
end 'main'
```
```maxoncstderr
error E3006: specs/fragments/static-variables/top-level-var-let-duplicate-declaration-error.test:3:5: duplicate definition of 'counter'
```

<!-- test: static-let-cross-file -->
A static member is reached by its qualified name from wherever the type is visible, so an
`export static let` constant and an `export static var` beside it must both be readable — and one of
them writable — from another file. Nothing in the read depends on which file declared the member,
only on the type owning it. A compiler that publishes only the statics whose initializer is a CALL
leaves this pair invisible to the reader, and the failure does not look like a visibility problem: the
qualified read stops resolving and the diagnostic blames `Config` for being a type. shv2 answers 42
here already, so this is a case another compiler in the tree has satisfied.
```maxon
// --- file: api/limits.maxon
export type Config
	export static let MAX_SIZE = 40
	export static var used = 2
end 'Config'

// --- file: app/main.maxon
function main() returns ExitCode
	Config.used = Config.used + Config.MAX_SIZE
	return Config.used
end 'main'
```
```exitcode
42
```

<!-- test: static-let-enum-constant -->
A `static let` carries the TYPE of the constant that initialized it, not merely its bits. Initialized
from a backed enum case, the member is still a `Color` where it is read, and `.rawValue` is the only
route back to 40 and 2 — a route that exists only while the enum type survives the member. A member
that arrives as a bare integer is the shape of a static published by value with its type discarded,
and it says so: `Primitive type 'int' has no method named 'rawValue'`.
```maxon
enum Color
	red = 40
	green = 2
end 'Color'

type Palette
	static let accent = Color.red
	static let fallback = Color.green
end 'Palette'

function main() returns ExitCode
	let a = Palette.accent
	let b = Palette.fallback
	return (a.rawValue + b.rawValue) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: static-let-reassign-error -->
A `static let` is immutable wherever it is reached, and the assignment is refused in the words a
file-scope `let` reassignment is refused in — one immutability rule, two spellings of the target. The
blame is the whole qualified name, so the anchor is the base token: the member alone would name a
fragment of the thing that cannot be written. `specs-shv2/static-variables.md`'s
`error.static-let-reassign` pins this sentence byte for byte, which is why both compilers say it.
```maxon
type Config
	static let MAX_SIZE = 40
end 'Config'

function main() returns ExitCode
	Config.MAX_SIZE = 7
	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/static-variables/static-let-reassign-error.test:7:2: cannot assign to immutable variable: 'Config.MAX_SIZE'
```

<!-- test: static-member-undeclared-error -->
A qualified read of a member the type never declared names the MEMBER. The base resolved, the type is
right there and its static roster is readable, so a sentence about `Config` not being usable as a value
describes a program nobody wrote and sends the author to the wrong token. shv2 pins the same subject
under `error.static-member-undeclared` in its own file; the two compilers keep their own house
spellings for E3018 — `Type 'Pair' has no field 'c'` against `type 'Pair' has no field named 'c'` —
exactly the arrangement `specs-shv2/static-variables.md` records around lines 374-386.
```maxon
type Config
	static let MAX_SIZE = 42
end 'Config'

function main() returns ExitCode
	return Config.MIN_SIZE
end 'main'
```
```maxoncstderr
error E3018: specs/fragments/static-variables/static-member-undeclared-error.test:7:16: Type 'Config' has no static member 'MIN_SIZE'
```

<!-- test: local-shadowing-a-type-name-keeps-a-qualified-store-local -->
A local binding in scope outranks a type of the same spelling, so `Config.MAX_SIZE` here is a field of
the local `Config` and never a static. The store and the read must agree on that: routing either one to
`Config`'s static slot writes a program nobody wrote, and because the static is a `let` the misrouted
store refuses a valid program as an immutable assignment.
```maxon
typealias Num = int(0 to 1000)

type Holder
	export var MAX_SIZE as Num

	export static function make() returns Holder
		return Holder{MAX_SIZE: 1}
	end 'make'
end 'Holder'

type Config
	static let MAX_SIZE = 5
end 'Config'

function main() returns ExitCode
	var Config = Holder.make()
	Config.MAX_SIZE = 7
	return Config.MAX_SIZE
end 'main'
```
```exitcode
7
```

<!-- test: local-shadowing-a-type-name-keeps-a-qualified-store-local-over-a-static-var -->
The same shadowing over a WRITABLE static, which is the half where a misroute is silent: the store
lands in `Config.MAX_SIZE`'s global slot and the read follows it there, so the program exits 5 with the
local never touched — and the only outward sign is the local reported as unused.
```maxon
typealias Num = int(0 to 1000)

type Holder
	export var MAX_SIZE as Num

	export static function make() returns Holder
		return Holder{MAX_SIZE: 1}
	end 'make'
end 'Holder'

type Config
	static var MAX_SIZE = 5
end 'Config'

function main() returns ExitCode
	var Config = Holder.make()
	Config.MAX_SIZE = 7
	return Config.MAX_SIZE
end 'main'
```
```exitcode
7
```

<!-- test: local-shadowing-a-type-name-keeps-a-qualified-call-local -->
Shadowing is ONE rule reached through four doors — a store, a read, a CALL and an enum case — and each
door decided the base for itself. This is the call: `Config.bump()` invokes the local's method, not the
static of the same name on `type Config`. A door that consults only the static roster calls the wrong
`bump` and returns 5, leaving the local unread.
```maxon
typealias Num = int(0 to 1000)

type Holder
	export var n as Num

	export static function make() returns Holder
		return Holder{n: 1}
	end 'make'

	export function bump() returns Num
		return 7
	end 'bump'
end 'Holder'

type Config
	export static function bump() returns Num
		return 5
	end 'bump'
end 'Config'

function main() returns ExitCode
	let Config = Holder.make()
	return Config.bump()
end 'main'
```
```exitcode
7
```

<!-- test: local-shadowing-a-type-name-keeps-an-enum-case-read-local -->
The fourth door. `Color.red` reads the local's FIELD; the enum case of the same spelling belongs to a
type the local shadows. The enum registry is consulted from its own arm, so a base that names a value in
scope has to be refused there too — otherwise this door alone keeps answering 5 while the other three
answer 7, and one rule spelled four times disagrees with itself.
```maxon
typealias Num = int(0 to 1000)

type Holder
	export var red as Num

	export static function make() returns Holder
		return Holder{red: 7}
	end 'make'
end 'Holder'

enum Color
	red = 5
end 'Color'

function main() returns ExitCode
	let Color = Holder.make()
	return Color.red
end 'main'
```
```exitcode
7
```
