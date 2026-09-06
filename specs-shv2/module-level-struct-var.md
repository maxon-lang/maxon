---
feature: module-level-struct-var
status: stable
keywords: [module, var, struct, field, assignment, method]
category: declaration
---
# Module-Level Struct Variables

## Documentation

Module-level `var` declarations with struct types support field assignment and method calls on fields.

## Tests

<!-- test: module-struct-field-assign -->
Module-level struct var field assignment.
```maxon
typealias SmallInt = int(0 to u8.max)

type Counter
		export var value as SmallInt

		static function create(value SmallInt) returns Self
			return Self{value: value}
		end 'create'
end 'Counter'

var state = Counter.create(0)

function main() returns ExitCode
		state.value = 42
		return state.value
end 'main'
```
```exitcode
42
```

<!-- test: module-struct-nested-method-call -->
Module-level struct var nested field method call.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type State
		export var items as ByteArray

		static function create(items ByteArray) returns Self
			return Self{items: items}
		end 'create'
end 'State'

var state = State.create(ByteArray.create())

function main() returns ExitCode
		state.items.push(10)
		state.items.push(20)
		return state.items.count()
end 'main'
```
```exitcode
2
```

<!-- test: module-struct-nested-field-assign -->
Module-level struct var nested field assignment through chain.
```maxon
typealias SmallInt = int(0 to u8.max)

type Inner
		export var x as SmallInt

		static function create(x SmallInt) returns Self
			return Self{x: x}
		end 'create'
end 'Inner'

type Outer
		export var inner as Inner

		static function create(inner Inner) returns Self
			return Self{inner: inner}
		end 'create'
end 'Outer'

var state = Outer.create(Inner.create(0))

function main() returns ExitCode
		state.inner.x = 99
		return state.inner.x
end 'main'
```
```exitcode
99
```

<!-- test: module-struct-container-field-read -->
⭐ **A CONTAINER FIELD IS READ THROUGH A MODULE-LEVEL RECEIVER THE SAME WAY A SCALAR ONE IS.** The three
cases above reach scalar fields and nested structs; this reaches a field whose type is a generic instance,
which is a different road only because the receiver's type has to survive being carried from the global's
declaration to the field lookup.

⚠ **THE BINDING KEYWORD DOES NOT CHANGE THE ANSWER**, which is why both spellings are read here: a
module-level receiver resolves to the record it was declared from whether it is a `let` or a `var`, and a
`let` additionally may or may not be image data, so one spelling passing is no evidence for the other. The
control for both is the same read off a LOCAL receiver — `let f = Facts.create()` then `f.counts.count()` —
which reaches the field by a different road and must agree.
```maxon
typealias Count = int(0 to 1000)
typealias CountArray = Array with Count
typealias NameArray = Array with String

type Facts
	export var counts as CountArray = CountArray.create()
	export var names as NameArray = NameArray.create()
	export static function create() returns Facts
		return Self{}
	end 'create'
end 'Facts'

let SharedFacts = Facts.create()
var MutableFacts = Facts.create()

function main() returns ExitCode
	MutableFacts.counts.push(7)
	let shared = SharedFacts.counts.count() + SharedFacts.names.count()
	let mutable = MutableFacts.counts.count() + MutableFacts.names.count()
	return (shared + mutable) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: module-struct-receiver-is-the-record-not-its-first-field -->
⭐⭐ **THE RECEIVER IS THE RECORD, AND THIS PINS THE VALUE RATHER THAN A DIAGNOSTIC — WHICH IS THE HALF A
REFUSAL CANNOT REACH.** `module-struct-container-field-read` above holds that a container field resolves
through a module-level receiver; it goes red when the receiver's type is wrong AND the wrong type cannot
answer the member. That conjunction is luck. Here the wrong type CAN answer it: `Outer` delegates `value()`
to an `Inner` field that declares `value()` too, so a receiver mistyped as its own first field type-checks,
runs, exits 0, and prints a different number.

⚠ **`105` IS THE WHOLE ASSERTION.** `5` is `Inner.value()` — the answer a program gets when the global
silently IS its first field rather than the record that holds it. Nothing about that program is refusable:
the slot's declared type and the slot's contents agree with each other, and only the arithmetic disagrees
with the source. A same-named delegating member is an ordinary wrapper shape, not a contrived one, which is
what makes the value worth pinning beside the diagnostic.
```maxon
typealias Tag = int(0 to 1000)

type Inner
	export var n as Tag = 5
	export static function create() returns Inner
		return Self{}
	end 'create'
	export function value() returns Tag
		return self.n
	end 'value'
end 'Inner'

type Outer
	export var inner as Inner = Inner.create()
	export var extra as Tag = 100
	export static function create() returns Outer
		return Self{}
	end 'create'
	export function value() returns Tag
		return (self.inner.value() + self.extra) as Tag
	end 'value'
end 'Outer'

let G = Outer.create()

function main() returns ExitCode
	print("value={G.value()}")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
value=105
```
