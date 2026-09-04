---
feature: match-expr-divergent-class
status: stable
keywords: match, expression, gives, float, register-class, promotion, E3005, type-agreement
category: control-flow
---
# A Match Expression Whose Arms Cross Register Classes Is PROMOTED, Not Crashed

## Documentation

Every `gives` arm of a match expression feeds ONE result phi, and a value's register file — general
(int/bool) vs XMM (float) — is fixed by its type at birth. So an integer give and a float give in the
same match hand the phi two values from different files. The register allocator cannot color a move
across files (`X64Backend.emitRegRegMove`); before this was guarded, such a program **crashed the
compiler** with `panic: crosses register files` — a backend panic on a user program, not a diagnostic.

The reference oracle UNIFIES such arms: it promotes the integer arms to float (`cvtsi2sd`) so the result
is uniformly float, and shv2 does the same (P1.5 #31). The integer arm's promotion is emitted into that
arm's OWN exit block — its `cvtsi2sd` on the conditional path, never hoisted before the branch — so only
the selected arm converts, and the merged result is a float value the surrounding context consumes like
any other (`trunc` reads it back to an int, returning it directly as an `ExitCode` would be a separate
`E3009`). This replaces the earlier `E2015` "different register classes" refusal, which was a placeholder
for exactly this promotion while floats were not yet a nameable type; now that they are, the refusal is
gone.

**Only INTEGER arms promote.** A float arm meeting a genuinely non-numeric arm — a `bool`, a `String`, a
struct, a union — is not a register-class crossing to unify but a real type disagreement, and it is
rejected as one (E3005, below), not promoted. **Same-class arms** are untouched by the promotion but must
still AGREE IN TYPE with each other, which is the same rule (below).

## Same-Class Arms Must Still Agree In Type (OPEN #54 Slice C)

The promotion above unifies a float arm with an INTEGER arm and rejects a float arm meeting a genuinely
non-numeric one. Neither reaches two arms that share a register class but are DIFFERENT TYPES — a `String`
give and an `int` give (both general registers), a `bool` give and an `int` give, or two different
structs/unions (both pointers). The result
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
- an AGGREGATE-NESS disagreement — a boxed struct/union give beside a SCALAR give (`match c { 0 gives
  Holder.holds(box); default gives 7 }`). The value tag `named` is OVERLOADED: a boxed union carries it,
  and so does a ranged-int alias — so `typesAgree(named, integer)` wrongly AGREES, and the phi
  dereferences the int `7` as a box pointer (a segfault). The tag cannot tell a real aggregate from a
  named scalar, but `aggregateNameOf` can — it names a declared struct/union and returns empty for every
  scalar (including a `named`-tagged ranged alias) — so the two gives disagree exactly when one has an
  aggregate name and the other does not.
- an AGGREGATE-NAME disagreement (two different structs, or two different unions) shares the
  `structRef`/`named` class — `typesAgree` passes it — but names distinct types, so it is caught by
  `namedAggregatesConflict`, the one name-agreement rule the whole trilogy shares.

Both render as **E3005**, positioned at the `match` keyword. Same-type arms (`0 gives 10  default gives
20`) are accepted exactly as before.

## Tests

<!-- test: int-and-float-arms -->
The arms cross register classes — an integer `5` and a float `7.5` — so the integer arm is promoted
to float (`cvtsi2sd`) and the result is uniformly float (P1.5 #31), exactly as the reference oracle
does. With `x` = 1 the integer arm is selected, promoted to `5.0`; `trunc` reads it back as 5.
```maxon
function main() returns ExitCode
	let x = 1
	let r = match x 'a'
		1 gives 5
		default gives 7.5
	end 'a'
	return trunc(r)
end 'main'
```
```exitcode
5
```

<!-- test: float-then-int-arms -->
The float-first mirror: a float `2.5` and an integer `9`. The integer arm is promoted to float and
the result is uniformly float. With `x` = 1 the float arm is selected, so `trunc(2.5)` is 2.
```maxon
function main() returns ExitCode
	let x = 1
	let r = match x 'a'
		1 gives 2.5
		default gives 9
	end 'a'
	return trunc(r)
end 'main'
```
```exitcode
2
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

<!-- test: error.match-gives-union-and-int -->
```maxon
typealias Num = int(i64.min to i64.max)

type Boxed
	export var v as Num

	export static function create(n Num) returns Boxed
		return Boxed{v: n}
	end 'create'
end 'Boxed'

union Holder
	nothing
	holds(b Boxed)
end 'Holder'

function main() returns ExitCode
	let c = 1
	let r = match c 'pick'
		0 gives Holder.holds(Boxed.create(1))
		default gives 7
	end 'pick'
	return 7
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/match-expr-divergent-class/error.match-gives-union-and-int.test:19:10: match arms give incompatible types: 'int' vs 'Holder'
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

<!-- test: error.match-gives-string-and-int-cast -->
The ill-typed result feeds an `as` cast, which is where an unguarded compiler reaches the cast with no
type to cast from. The give-type check rejects it at the `match`, before the cast is ever asked.
```maxon
function main() returns ExitCode
	let c = 1
	let out = match c 'pick'
		0 gives "hello"
		default gives 5
	end 'pick'
	return out as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/match-expr-divergent-class/error.match-gives-string-and-int-cast.test:4:12: match arms give incompatible types: 'int' vs 'String'
```
