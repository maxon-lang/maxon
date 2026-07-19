---
feature: assignment
status: selfhosted
keywords: [assignment, equals, mutation, reassignment, var]
category: statements
milestone: M4b
---

# Assignment Statement

## Documentation

The assignment operator `=` updates the value of a mutable (`var`) binding:

```maxon
variable = expression
```

M4b adds reassignment on top of M2's `let`/`var` bindings. A `var` may be
reassigned any number of times; each write rebinds the variable's current value,
so a later reference sees the new value (`x = x + 2` reads the old `x`, then
rebinds it to the sum). A `let` binding is immutable — assigning to it is E2013.

Lowering: the value model is **on-the-fly SSA**. A reassignment is a rebinding of
the variable's current ValueId (no stack slot, no store op), so straight-line code
needs no phis. Where a `var` is reassigned across a control-flow merge — the header
and exit of a `while` loop — the parser inserts block-arg **phis**, which the
Std-tier phi-elimination pass resolves into coalesced values plus register moves
before the backend (see `specs-shv2/while-loops.md`).

## Tests

The M4b slice of `specs/assignment.md`: straight-line reassignment, chained
reassignment across two variables, and the canonical accumulator loop. All three
fit the placeholder register allocator's pool.

<!-- test: basic-assignment -->
<!-- targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	var x = 3
	x = x + 2
	return x
end 'main'
```
```exitcode
5
```

<!-- test: multiple-assignments -->
<!-- targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	var x = 10
	var y = 20
	x = y
	y = 30
	return x + y
end 'main'
```
```exitcode
50
```

<!-- test: assignment-in-loop -->
<!-- targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	var sum = 0
	var i = 1
	while i <= 5 'loop'
		sum = sum + i
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
15
```

<!-- test: assign-to-let-error -->
<!-- targets: wasm32-wasi -->
Assigning to a `let` binding is rejected — only `var` bindings are mutable.
```maxon
function main() returns ExitCode
	let x = 3
	x = 5
	return x
end 'main'
```
```maxoncstderr
error E2013: <fragment>:4:2: cannot assign to immutable variable: 'x'
```

<!-- test: error.retype-struct-to-other-struct-errors -->
<!-- targets: wasm32-wasi -->
Two different structs are two different types, even though both are "a struct". A kind alone cannot
tell them apart, so a bare `structRef == structRef` tag check passes this retype and the overwritten
box is later dropped under the binding's declared destructor — a wrong answer in the bootstrap
(E9001 in lowering) and a memory-safety hole once the field is managed (OPEN #54). Struct identity is
the interned name, exact. (Ported from `specs/assignment.md`; shv2's `assignTypeMismatch` wording.)
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Other
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Other'

function main() returns ExitCode
	var p = Point.create(1)
	p = Other.create(2)
	print("{p.x}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-struct-to-other-struct-errors.test:23:2: cannot assign 'Other' to variable 'p' of type 'Point'
```

<!-- test: error.reassign-wrong-struct -->
<!-- targets: wasm32-wasi -->
The same retype with MANAGED (String-field) structs — the case OPEN #54 was filed for. The scalar tag
check accepts it; the overwrite drops the old `BoxA` and the scope exit drops `a` under `BoxA`'s
destructor while it holds a `BoxB` box. Rejected at the reassignment, before either drop.
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var s as String

	static function create(x Integer) returns Self
		return Self{s: "v{x}"}
	end 'create'
end 'BoxA'

type BoxB
	export var t as String

	static function create(x Integer) returns Self
		return Self{t: "w{x}"}
	end 'create'
end 'BoxB'

function main() returns ExitCode
	var a = BoxA.create(1)
	a = BoxB.create(2)
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.reassign-wrong-struct.test:22:2: cannot assign 'BoxB' to variable 'a' of type 'BoxA'
```
