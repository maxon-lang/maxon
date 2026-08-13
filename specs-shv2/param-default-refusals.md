---
feature: param-default-refusals
status: selfhosted
keywords: [default, default-values, parameters, closure, interface, overload, duplicate]
category: core
---

# Where a Parameter Default Has No Name to Be Published Under

## Documentation

A parameter default is compiled as a synthesized nullary function, and a call site that omits the
argument emits a call to it. Everything therefore hangs off ONE thing: a name the call site can look the
declaration up by. Two declaration forms cannot supply one, and two more supply one that does not answer
for the declaration that wrote it — each is refused where the `=` is written, rather than mis-served.

- A CLOSURE literal is called indirectly, through a function value; the call site has no callee name.
- An INTERFACE requirement is in no signature registry; a witness dispatch slots against the interface's
  formals.
- A name whose DECLARATIONS DISAGREE about their defaults. A short call is filled while it is parsed, from
  a registry keyed by the name the source wrote, and the overload is resolved a whole pass later — so the
  fill has to be the same for every declaration wearing that name. Declarations that differ in their
  parameter list, in which positions default, or in whether they default anything at all have no one shape
  to be filled from. The same refusal catches a name DECLARED TWICE by accident, whose duplicate-definition
  diagnostic (E3006) is only reported once every file has parsed — after the losing declaration's own
  helper is built.
- A `static` MEMBER AND AN INSTANCE MEMBER of one type wearing one name. That pair is not an overload set:
  the two are told apart by the KEY each is registered under (`m` and `m#__static`), while the sweep files
  parameter defaults under the one name the source wrote. So a `Type.m()` call asks the defaults registry
  for the static key, misses, and fills nothing — the default is inert and the call is refused for an arity
  it never had.

⚠ **AN OVERLOAD SET WHOSE MEMBERS AGREE ON THAT SHAPE IS NOT REFUSED, AND USED TO BE (W74).** Members that
declare the same parameters and default the same positions are filled identically whichever one a call
resolves to, so there is nothing for a call to be told apart by — and each member's own default EXPRESSION
is supplied once the member is known. `function-overloads.md` carries that half.

These cases exist because a refusal nothing runs is a claim, not a door.

## Tests

<!-- test: error-default-on-closure-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let f = function(n Integer = 3) returns Integer
		gives n
	end 'f'
	return f(2)
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:29: Unsupported: a default value on closure parameter 'n' — a closure is called INDIRECTLY, through a function value, so no call site has a callee name to look the default up by. Give the closure's caller the value explicitly
```

<!-- test: error-default-on-interface-requirement -->
```maxon
typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet(times Integer = 2) returns Integer
end 'Greeter'

type Loud implements Greeter
	export function greet(times Integer) returns Integer
		return times
	end 'greet'
end 'Loud'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:31: Unsupported: a default value on interface requirement parameter 'times' — a witness dispatch slots its arguments against the INTERFACE's formals, which are in no signature registry, so no dispatch site can read a default from the requirement. Declare the default on each conforming type's method
```

<!-- test: error-default-on-overloaded-name -->
One member defaults its only parameter and the other defaults nothing, so the two disagree about the shape
of a short call: filled from the entry the sweep holds, `show()` would carry ONE argument and exclude the
`String` member on arity. MEASURED against the oracle on that shape (`g(a Num)` beside `g(a Num, b Num = 5)`,
called `g(1)`): the bootstrap selects the ONE-parameter member, so a fill-then-resolve that quietly took the
defaulted member would be a wrong ANSWER rather than a missing refusal. The sibling that defaults nothing is
seen because the sweep counts DECLARATIONS as well as defaulting ones — it reaches the defaults registry not
at all, so no test written over that registry alone could find it.
```maxon
typealias Integer = int(i64.min to i64.max)

function show(n Integer = 1) returns Integer
	return n
end 'show'

function show(s String) returns Integer
	return 2
end 'show'

function main() returns ExitCode
	return show()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:25: Unsupported: a default value on parameter 'n' of 'show' — that name is declared more than once in this program and the declarations do not agree about their defaults: they differ in their parameter list, in which positions default, or in whether they default anything at all. The whole-program declaration sweep publishes defaults under the name the source wrote and a short call is filled when it is PARSED, a whole pass before the overload is resolved, so a call that omits an argument cannot be told which declaration's shape to fill. Give every declaration of this name the same parameters and the same defaults, or drop the default
```

<!-- test: error-default-on-a-name-declared-twice -->
Both declarations default a parameter and the two shapes DIFFER — one parameter against two — so neither
is the name's sole answer for a short call. Without this refusal the losing declaration's drain reads the
winner's shape out of the by-name registry, finds no helper at its own parameter's position, and the
compiler PANICS with no source position at all — while the identical program with the defaults removed
answers a clean E3006.
```maxon
typealias Integer = int(i64.min to i64.max)

function f(a Integer = 1) returns Integer
	return a
end 'f'

function f(x Integer, y Integer = 2) returns Integer
	return x + y
end 'f'

function main() returns ExitCode
	return f()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:22: Unsupported: a default value on parameter 'a' of 'f' — that name is declared more than once in this program and the declarations do not agree about their defaults: they differ in their parameter list, in which positions default, or in whether they default anything at all. The whole-program declaration sweep publishes defaults under the name the source wrote and a short call is filled when it is PARSED, a whole pass before the overload is resolved, so a call that omits an argument cannot be told which declaration's shape to fill. Give every declaration of this name the same parameters and the same defaults, or drop the default
```

<!-- test: error-default-on-the-static-half-of-a-same-name-pair -->
A `static m` and an instance `m` are two members told apart by their registration KEY, and the defaults
registry has ONE key for the two of them. Before this refusal the static's default was simply inert:
`T.m()` asked the registry for `T.m#__static`, found nothing to fill, and the program was refused
`E3036: 'T.m#__static' expects 1 argument(s) but 0 were provided` — a symbol no source wrote, blamed on a
call the author got right.
```maxon
typealias Num = int(i64.min to i64.max)

type T
	export var v as Num

	export static function make(v Num) returns T
		return Self{v: v}
	end 'make'

	export function m(b Num) returns Num
		return self.v + b
	end 'm'

	export static function m(a Num = 7) returns Num
		return a
	end 'm'
end 'T'

function main() returns ExitCode
	let t = T.make(1)
	return (T.m() + t.m(2)) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:33: Unsupported: a default value on parameter 'a' of 'T.m' — a `static` member and an instance member of this type both wear that name, so the two are registered under DIFFERENT keys while the whole-program declaration sweep publishes parameter defaults under the one name the source wrote. A call that omits an argument cannot be told which of the two members' default to supply, and a call to the `static` half cannot find one at all. Give the two members distinct names, or drop the default
```

<!-- test: error-default-on-the-instance-half-of-a-same-name-pair -->
⭐ **THE SAME TWO MEMBERS AS THE CASE ABOVE, DECLARED IN THE OTHER ORDER, AND THE POINT IS THAT THE ANSWER
IS THE SAME.** It was not: the pair's two counts are tallied under different keys — declarations per
REGISTRATION key, defaulting declarations per SOURCE name — and the pair remaps exactly one of those, when
the second member arrives. So this order was refused by the count comparison while the other compiled and
answered 7. The refusal is now asked of `isStaticInstanceContest`, which is settled before any file is
parsed and cannot depend on which member was written first.
```maxon
typealias Num = int(i64.min to i64.max)

type T
	export var v as Num

	export static function make(v Num) returns T
		return Self{v: v}
	end 'make'

	export static function m(a Num) returns Num
		return a
	end 'm'

	export function m(b Num = 4) returns Num
		return self.v + b
	end 'm'
end 'T'

function main() returns ExitCode
	let t = T.make(1)
	return (T.m(2) + t.m()) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:26: Unsupported: a default value on parameter 'b' of 'T.m' — a `static` member and an instance member of this type both wear that name, so the two are registered under DIFFERENT keys while the whole-program declaration sweep publishes parameter defaults under the one name the source wrote. A call that omits an argument cannot be told which of the two members' default to supply, and a call to the `static` half cannot find one at all. Give the two members distinct names, or drop the default
```

<!-- test: two-files-agreeing-on-their-defaults-report-ONE-duplicate -->
Two files declaring one `f` with the SAME default shape are no longer refused at the `=` (W74) — they fall
through to the duplicate-definition diagnostic the program always deserved. ⚠ **AND THEY MUST EARN EXACTLY
ONE OF IT.** A default is compiled as a synthesized function named after the declaration that owns it, so
the duplicate `f` drags a duplicate `__paramDefault#f#0` behind it; reported, that second E3006 explains a
symbol absent from the source in a sentence about parameter-type spellings the author never used. MEASURED
before `FuncSignatureEntry.synthesized` existed, and again with the flag forced false: both E3006s printed.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export function f(a Integer = 1) returns Integer
	return a
end 'f'

// --- file: b.maxon
typealias Count = int(i64.min to i64.max)

export function f(a Count = 1) returns Count
	return a + 1
end 'f'

// --- file: main.maxon
function main() returns ExitCode
	return f() as ExitCode
end 'main'
```
```maxoncstderr
error E3006: <fragment>:12:17: Duplicate function 'f'
```

<!-- test: error-param-default-trailing-tokens -->
The capture walks to the `,` or `)` that ends the default, and the expression the drain parses out of
that region has to reach it. Anything left over is text the author wrote and the compiler was about to
ignore — `b Integer = 7 zzz` silently defaulted to 7. The bootstrap dropped it too, and now neither does.
```maxon
typealias Integer = int(i64.min to i64.max)

function f(a Integer, b Integer = 7 zzz) returns Integer
	return a + b
end 'f'

function main() returns ExitCode
	return f(1)
end 'main'
```
```maxoncstderr
error E2010: <fragment>:4:37: Expected 'end of default value' but got 'zzz'
```

<!-- test: error-param-default-exponent-without-point -->
`1e100` is the integer `1` followed by the identifier `e100` — a float literal must contain a decimal
point. Dropped, it made `b Real = 1e100` default to `1`: a hundred orders of magnitude, silently.
Write `1.0e100`.
```maxon
typealias Real = float(f64.min to f64.max)

function f(a Real, b Real = 1e100) returns Real
	return a + b
end 'f'

function main() returns ExitCode
	print("{f(1.0)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:4:30: Expected 'end of default value' but got 'e100'
```
