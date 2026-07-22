---
feature: match-expr-divergent-type
status: experimental
keywords: match, expression, gives, type-agreement, E3005
category: control-flow
---
# Match Expression Arms Must Give Compatible Types

## Documentation

Every `gives` arm of a match *expression* feeds one result slot, and that slot is minted with the
FIRST arm's type. A value's type is fixed at birth, so an arm that gives a value of another type is
wrong-typed through the slot with no diagnostic: `match c { 0 gives "hello"; default gives 5 }` types
the slot as `String`, then stores the integer `5`, which a later reader dereferences as a `String`
pointer — a segfault on a user program. Two different structs are worse: the slot is later dropped
under the first struct's destructor, a wild free.

Before this was guarded, such a program **crashed the compiler** with an internal error (`E9001`) once
the ill-typed result reached a downstream cast or lowering step, rather than a clean diagnostic. The
match expression now checks arm-type agreement at the merge — the same rule `ParseTernaryExpression`
already applies to a ternary's two branches — and rejects a genuine mismatch with **E3005**, anchored
at the `match` keyword, before the ill-typed value can flow anywhere.

**Compatible arms are untouched.** Arms of the same type (all `int`, all `String`, all the same
struct/union/enum) type and run exactly as before. Cross-register-class numeric arms — an `int` arm
and a `float` arm — are still UNIFIED by promoting the integer arm to `float`, not rejected; only a
genuinely incompatible pair (a `String` beside an `int`, a `bool` beside an `int`, two different
structs/unions) is an error. The mismatch is named by each side's type: a declared struct, union, or
enum by its concrete type name, and a scalar by its primitive kind name (`int`, `bool`, `float`, …).

## Tests

<!-- test: match-gives-same-type -->
Same-type arms are accepted exactly as before — the merge slot holds either arm's `int`.
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

<!-- test: int-and-float-arms -->
The arms cross register classes — an integer `5` and a float `7.5` — so the integer arm is promoted
to float and the result is uniformly float, not rejected. With `x` = 1 the integer arm is selected,
promoted to `5.0`; `trunc` reads it back as 5.
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
A `String` arm and an `int` arm share a general register but are different types; the slot would
deref the int as a String pointer.
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
error E3005: specs/fragments/match-expr-divergent-type/error.match-gives-string-and-int.test:4:10: match arms give incompatible types: 'int' vs 'String'
```

<!-- test: error.match-gives-string-and-int-cast -->
The original crash form: the ill-typed result is used in an `as` cast, which is where the unguarded
compiler threw `E9001`. The give-type check now rejects it at the `match` before the cast is reached.
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
error E3005: specs/fragments/match-expr-divergent-type/error.match-gives-string-and-int-cast.test:4:12: match arms give incompatible types: 'int' vs 'String'
```

<!-- test: error.match-gives-bool-and-int -->
A `bool` arm and an `int` arm are distinct scalar classes.
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
error E3005: specs/fragments/match-expr-divergent-type/error.match-gives-bool-and-int.test:4:10: match arms give incompatible types: 'bool' vs 'int'
```

<!-- test: error.match-gives-two-structs -->
Two different structs share the pointer class but name distinct types; the slot would drop the second
under the first's destructor — a wild free.
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
error E3005: specs/fragments/match-expr-divergent-type/error.match-gives-two-structs.test:22:10: match arms give incompatible types: 'Cup' vs 'Box'
```

<!-- test: error.match-gives-two-unions -->
Two different unions name distinct types.
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
error E3005: specs/fragments/match-expr-divergent-type/error.match-gives-two-unions.test:16:10: match arms give incompatible types: 'Toggle' vs 'Shape'
```

<!-- test: error.match-gives-union-and-int -->
A boxed union give beside a scalar `int` give — the aggregate and the scalar cannot share the slot.
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
error E3005: specs/fragments/match-expr-divergent-type/error.match-gives-union-and-int.test:19:10: match arms give incompatible types: 'int' vs 'Holder'
```
