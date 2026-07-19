---
feature: match-expr-divergent-class
status: stable
keywords: match, expression, gives, float, register-class, E2015, unsupported, E3005, type-agreement
category: control-flow
---
# A Match Expression Whose Arms Cross Register Classes Is REFUSED, Not Crashed

## Documentation

Every `gives` arm of a match expression feeds ONE result phi, and a value's register file — general
(int/bool) vs XMM (float) — is fixed by its type at birth. So an integer give and a float give in the
same match hand the phi two values from different files. The register allocator cannot color a move
across files (`X64Backend.emitRegRegMove`); before this was guarded, such a program **crashed the
compiler** with `panic: crosses register files` — a backend panic on a user program, not a diagnostic.

The reference oracle UNIFIES such arms: it promotes the integer arms to float (`cvtsi2sd`) so the result
is uniformly float, then the surrounding context decides (returning that float as an `ExitCode` is a
separate `E3009`). shv2 has both the instruction (`promoteToFloat`) and the lattice already — but the
*result* of a promoted match is a float **value**, and a float has no nameable type in shv2 yet
(`typealias F = float(…)` is itself still `E2015`). A promotion whose result nothing can hold or name is
the `mintPhi` trap: a mechanism with no consumer. So it is deferred to the float type-system rung, and
until then a cross-class match expression is refused **loudly and positioned**, exactly as every other
unbuilt scalar surface is — never allowed to reach the backend.

**Same-class arms are unaffected** by the cross-class refusal — but they must still AGREE IN TYPE with
each other, which is a separate rule (below). Only a genuine GP↔XMM crossing reaches the `unsupported`
refusal.

## Same-Class Arms Must Still Agree In Type (OPEN #54 Slice C)

The cross-class check above catches a float arm meeting a non-float arm. It does NOT catch two arms that
share a register class but are DIFFERENT TYPES — a `String` give and an `int` give (both general
registers), a `bool` give and an `int` give, or two different structs/unions (both pointers). The result
phi is minted with the FIRST arm's type, and a value's type is fixed at birth, so a later arm's value is
wrong-typed THROUGH that phi with no diagnostic: `match c { 0 gives "hello"; default gives 5 }` gives the
phi the `String` type, then reaches the merge carrying the integer `5`, which the phi's consumer
dereferences as a `String` pointer — a **segfault** on a user program. Two different structs are worse
still: the phi is later dropped under the FIRST struct's destructor — a wild free.

This is the match-merge site of the aggregate-type-identity trilogy (Slice A = return / reassign /
`otherwise`, Slice B = call argument, Slice C = here). Every reaching `gives` arm must yield the first
arm's type, checked BEFORE the phi is built:

- a SCALAR-class disagreement (`String` vs `int`, `bool` vs `int`) is caught by `typesAgree`, the
  symmetric form of the coercion rule the other sites ask one-sided. It is `typesAgree`, not a raw tag
  compare, so numeric SUBTYPES still agree: a ranged-int alias give and a plain `int` give, or an
  `ExitCode` give and an `int` give, share a register file and a phi holds either — they STAY valid.
- an AGGREGATE disagreement (two different structs, or two different unions) shares the
  `structRef`/`named` class — `typesAgree` passes it — but names distinct types, so it is caught by
  `namedAggregatesConflict`, the one name-agreement rule the whole trilogy shares.

Both render as **E3005**, positioned at the `match` keyword. Same-type arms (`0 gives 10  default gives
20`) are accepted exactly as before.

## Tests

<!-- test: error.int-and-float-arms -->
```maxon
function main() returns ExitCode
	let x = 1
	let r = match x 'a'
		1 gives 5
		default gives 7.5
	end 'a'
	return r
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/match-expr-divergent-class/error.int-and-float-arms.test:4:10: Unsupported: a match expression whose arms give values of different register classes (a float arm and a non-float arm) — unifying them promotes the integer arms to float, whose result has no nameable type until the float type system lands (a `float` typealias is itself not yet parsed), and it arrives with that rung
```

<!-- test: error.float-then-int-arms -->
```maxon
function main() returns ExitCode
	let x = 1
	let r = match x 'a'
		1 gives 2.5
		default gives 9
	end 'a'
	return r
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/match-expr-divergent-class/error.float-then-int-arms.test:4:10: Unsupported: a match expression whose arms give values of different register classes (a float arm and a non-float arm) — unifying them promotes the integer arms to float, whose result has no nameable type until the float type system lands (a `float` typealias is itself not yet parsed), and it arrives with that rung
```

<!-- test: error.match-gives-string-and-int -->
```maxon
function main() returns ExitCode
	let c = 1
	let r = match c 'pick'
		0 gives "hello"
		default gives 5
	end 'pick'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/match-expr-divergent-class/error.match-gives-string-and-int.test:4:10: match arms give incompatible types: 'int' vs 'String'
```

<!-- test: error.match-gives-bool-and-int -->
```maxon
function main() returns ExitCode
	let c = 1
	let r = match c 'pick'
		0 gives 1
		default gives true
	end 'pick'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/match-expr-divergent-class/error.match-gives-bool-and-int.test:4:10: match arms give incompatible types: 'bool' vs 'int'
```

<!-- test: error.match-gives-two-structs -->
```maxon
typealias Count = int(i64.min to i64.max)

type Box
	export var v as Count

	export static function make() returns Box
		return Box{v: 1}
	end 'make'
end 'Box'

type Cup
	export var w as Count

	export static function make() returns Cup
		return Cup{w: 2}
	end 'make'
end 'Cup'

function main() returns ExitCode
	let c = 1
	let r = match c 'pick'
		0 gives Box.make()
		default gives Cup.make()
	end 'pick'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/match-expr-divergent-class/error.match-gives-two-structs.test:22:10: match arms give incompatible types: 'Cup' vs 'Box'
```

<!-- test: error.match-gives-two-unions -->
```maxon
typealias Amt = int(0 to 100)

union Shape
	empty
	dot(x Amt)
end 'Shape'

union Toggle
	off
	on(y Amt)
end 'Toggle'

function main() returns ExitCode
	let c = 1
	let r = match c 'pick'
		0 gives Shape.empty
		default gives Toggle.off
	end 'pick'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/match-expr-divergent-class/error.match-gives-two-unions.test:16:10: match arms give incompatible types: 'Toggle' vs 'Shape'
```

<!-- test: match-gives-same-type -->
```maxon
function main() returns ExitCode
	let c = 0
	let r = match c 'pick'
		0 gives 10
		default gives 20
	end 'pick'
	return r
end 'main'
```
```exitcode
10
```
