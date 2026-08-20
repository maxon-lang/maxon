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
declaration up by. Two declaration forms cannot supply one, and one more supplies one that does not answer
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
✅ **A `static` MEMBER BESIDE AN INSTANCE MEMBER OF ONE NAME WAS A FOURTH, AND IS NOT ANY MORE (W75).** That
pair is not an overload set: the two are told apart by the KEY each is registered under (`m` and
`m#__static`), and the sweep used to file parameter defaults under the one name the source wrote — so a
`Type.m()` call asked the defaults registry for the static key, missed, and filled nothing, leaving the
default inert and the call refused for an arity it never had. The sweep's by-name folds now key by the
MEMBER each entry belongs to, and the synthesized helper is renamed with it, so each half of a pair carries
its own defaults. `same-name-methods.md` carries the pair's own rules; the cases below pin both halves and
both declaration orders.

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

<!-- test: a-default-on-the-static-half-of-a-same-name-pair -->
✅ **REFUSED UNTIL W75 (`error-default-on-the-static-half-of-a-same-name-pair`), AND THE REFUSAL'S OWN
SENTENCE NAMED THE CURE.** A `static m` and an instance `m` are two members told apart by their registration
KEY, and the defaults registry had ONE key for the two of them: `T.m()` asked it for `T.m#__static`, found
nothing to fill, and the program was refused `E3036: 'T.m#__static' expects 1 argument(s) but 0 were
provided` — a symbol no source wrote, blamed on a call the author got right. The by-name folds now key by the
member each entry belongs to, and the synthesized helper is renamed with it, so the static's default is the
static's. `T.m()` fills 7 and `t.m(2)` answers 3. ⚠ The oracle refuses this program on `E3007` — its rule
about what a pair IS, not about the default.
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
```exitcode
10
```

<!-- test: a-default-on-the-instance-half-of-a-same-name-pair -->
✅ **THE SAME TWO MEMBERS DECLARED IN THE OTHER ORDER, AND THE POINT IS THAT THE ANSWER IS THE SAME.** It
was not, twice over: before W74 the pair's two counts were tallied under different keys and this order was
refused by the count comparison while the other compiled; before W75 both orders were refused outright. The
sweep now files each member's defaults — and the tallies that judge them — under that member's own
registration key, which is settled by the second member to fold whichever one that is. `T.m(2)` answers 2
and `t.m()` fills 4 onto a `v` of 1. ⚠ The oracle refuses on `E3007`, its own rule about pairs.
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
```exitcode
7
```

<!-- test: error-default-on-an-overloaded-name-a-second-directory-contests -->
⛔⛔ **THE REFUSAL ABOVE, WITH ONE MORE DIRECTORY IN THE PROGRAM — AND UNTIL W78 THAT WAS ENOUGH TO TURN IT
INTO A WRONG ANSWER.** These are the same two declarations `error-default-on-overloaded-name` refuses:
`pick(a Num)` beside `pick(a Num, b Num = 5)`, called with one argument. `beta/` declares a `pick` of its
own, which makes the bare name contested — so the sweep registers `alpha/`'s two declarations under
`alpha.pick` while leaving the disagreement verdict and both declaration tallies on the bare `pick`. Every
gate keyed on the registration name then read maps nothing had written for that key, answered "these
declarations agree", and the short call was filled from the DEFAULTED member. MEASURED on `main`:
**shv2 answered 210** (`2*100 + 5 + 5`) where the oracle answers **2**, silently, with no diagnostic and a
green suite. ⚠ **Still narrower than the language**: the oracle compiles this program and selects the
one-parameter member. shv2 refuses it for the same reason it refuses the root-level shape one case above —
the fill happens while the call is parsed, a pass before the overload is resolved.
```maxon
// --- file: alpha/a.maxon
typealias Num = int(-1000 to 1000)

export function pick(a Num) returns Num
	return a
end 'pick'

export function pick(a Num, b Num = 5) returns Num
	return a * 100 + b + 5
end 'pick'

// --- file: beta/b.maxon
typealias Small = int(-1000 to 1000)

export function pick(a Small) returns Small
	return a + 50
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return alpha.pick(2) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: alpha/specs/fragments/param-default-refusals/error-default-on-an-overloaded-name-a-second-directory-contests.test:9:35: Unsupported: a default value on parameter 'b' of 'alpha.pick' — that name is declared more than once in this program and the declarations do not agree about their defaults: they differ in their parameter list, in which positions default, or in whether they default anything at all. The whole-program declaration sweep publishes defaults under the name the source wrote and a short call is filled when it is PARSED, a whole pass before the overload is resolved, so a call that omits an argument cannot be told which declaration's shape to fill. Give every declaration of this name the same parameters and the same defaults, or drop the default
```

<!-- test: error-default-on-an-overloaded-name-a-second-directory-contests-defaulted-first -->
⭐ **THE SAME TWO DECLARATIONS IN THE OTHER ORDER.** Last-wins keying means only the reversal can tell a
correct verdict from a lucky one: the by-name registries keep whichever declaration folded last, so a gate
that happens to read the defaulted member's entry answers correctly in one order and not in the other.
MEASURED before W78: **210 here too**, so the defect was not order-dependent — but a fix that only worked
in one order would have looked identical from the case above.
```maxon
// --- file: alpha/a.maxon
typealias Num = int(-1000 to 1000)

export function pick(a Num, b Num = 5) returns Num
	return a * 100 + b + 5
end 'pick'

export function pick(a Num) returns Num
	return a
end 'pick'

// --- file: beta/b.maxon
typealias Small = int(-1000 to 1000)

export function pick(a Small) returns Small
	return a + 50
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return alpha.pick(2) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: alpha/specs/fragments/param-default-refusals/error-default-on-an-overloaded-name-a-second-directory-contests-defaulted-first.test:5:35: Unsupported: a default value on parameter 'b' of 'alpha.pick' — that name is declared more than once in this program and the declarations do not agree about their defaults: they differ in their parameter list, in which positions default, or in whether they default anything at all. The whole-program declaration sweep publishes defaults under the name the source wrote and a short call is filled when it is PARSED, a whole pass before the overload is resolved, so a call that omits an argument cannot be told which declaration's shape to fill. Give every declaration of this name the same parameters and the same defaults, or drop the default
```

<!-- test: error-default-on-an-overloaded-name-the-contestant-declares-first -->
⭐ **THE THIRD ORDER, AND IT IS A DIFFERENT ONE: WHICH DIRECTORY THE SWEEP FOLDS FIRST.** Here the
single-declaration contestant is in `alpha/` and the overload set is in `beta/`, so the contest is already
known by the time the set's own file is folded — the opposite arrangement from the two cases above, where
the set folded first and had to be moved off the bare key afterwards. The sweep reaches the tally through
two different paths in the two arrangements, and only running both says whether they agree. MEASURED
before W78: **210** here as well.
```maxon
// --- file: alpha/a.maxon
typealias Small = int(-1000 to 1000)

export function pick(a Small) returns Small
	return a + 50
end 'pick'

// --- file: beta/b.maxon
typealias Num = int(-1000 to 1000)

export function pick(a Num) returns Num
	return a
end 'pick'

export function pick(a Num, b Num = 5) returns Num
	return a * 100 + b + 5
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return beta.pick(2) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: beta/specs/fragments/param-default-refusals/error-default-on-an-overloaded-name-the-contestant-declares-first.test:16:35: Unsupported: a default value on parameter 'b' of 'beta.pick' — that name is declared more than once in this program and the declarations do not agree about their defaults: they differ in their parameter list, in which positions default, or in whether they default anything at all. The whole-program declaration sweep publishes defaults under the name the source wrote and a short call is filled when it is PARSED, a whole pass before the overload is resolved, so a call that omits an argument cannot be told which declaration's shape to fill. Give every declaration of this name the same parameters and the same defaults, or drop the default
```

<!-- test: error-default-on-a-contested-name-whose-declarations-disagree-about-parameter-names -->
⛔⛔ **THE OTHER HALF OF W78's TALLY MOVE, AND NOTHING RAN IT UNTIL THIS CASE (found at review).** The
three cases above all disagree by a MISSING default — one member publishes a shape and the other publishes
nothing — which the sweep sees as two COUNTS that differ. This one disagrees the other way: both members
publish a default, so the counts match and the only thing that can refuse it is the disagreement VERDICT
`recordParamDefaults` files when two published shapes differ. `alpha/`'s members name their parameters
`(a, b)` and `(x, y)`, so a short call cannot be told which pair of labels it is filling.

⛔ **THE VERDICT HAS TO TRAVEL WITH THE ENTRIES, AND THAT MOVE HAD NO GATE.** `alpha/` folds first, so its
verdict is filed under the bare `pick` and only `ProgramSignatures.moveDeclarationTallies` carries it onto
`alpha.pick`. MEASURED by deleting that one line: this program **compiles and answers 65** while the
identical program with the two directories folded in the other order still refuses, and the uncontested
twin (`beta/` removed) still refuses — a silent, order-dependent accept that every committed case was
green over. ⚠ **Still narrower than the language**: the oracle carries defaults per declaration and
answers **67**.
```maxon
// --- file: alpha/a.maxon
typealias Num = int(-1000 to 1000)

export function pick(a Num, b Num = 5) returns Num
	return a + b
end 'pick'

export function pick(x bool, y Num = 5) returns Num
	return y if x else 0
end 'pick'

// --- file: beta/b.maxon
typealias Small = int(-1000 to 1000)

export function pick(a Small) returns Small
	return a + 50
end 'pick'

// --- file: app/main.maxon
function main() returns ExitCode
	return (alpha.pick(2) + alpha.pick(true) + beta.pick(3)) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: alpha/<fragment>:5:35: Unsupported: a default value on parameter 'b' of 'alpha.pick' — that name is declared more than once in this program and the declarations do not agree about their defaults: they differ in their parameter list, in which positions default, or in whether they default anything at all. The whole-program declaration sweep publishes defaults under the name the source wrote and a short call is filled when it is PARSED, a whole pass before the overload is resolved, so a call that omits an argument cannot be told which declaration's shape to fill. Give every declaration of this name the same parameters and the same defaults, or drop the default
```

<!-- test: two-files-agreeing-on-their-defaults-report-ONE-duplicate -->
Two files declaring one `f` with the SAME default shape are no longer refused at the `=` (W74) — they fall
through to the duplicate-definition diagnostic the program always deserved. ⚠ **AND THEY MUST EARN EXACTLY
ONE OF IT.** A default is compiled as a synthesized function named after the declaration that owns it, so
the duplicate `f` drags a duplicate `__paramDefault#f#0` behind it; reported, that second E3006 explains a
symbol absent from the source in a sentence about parameter-type spellings the author never used. MEASURED
before `FuncSignatureEntry.synthesized` existed, and again with the flag forced false: both E3006s printed.

⚠ **BOTH FILES SPELL THE PARAMETER `Integer`, AND THAT IS LOAD-BEARING NOW THAT TWO FILES OF ONE
DIRECTORY ARE AN OVERLOAD SET** (`cross-file-overload-set.md`). Written with two SPELLINGS of one underlying
type — `Integer` here and `Count` there, which is how this case read until then — the two declarations mint
two registration names, are two live overloads, and the program is `E3007` at the CALL instead: a real
verdict, and one that tests nothing about a synthesized helper's duplicate. It is pinned in its own right by
`cross-file-overload-set.md`'s `error.two-spellings-of-one-type-are-ambiguous-at-the-call`.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export function f(a Integer = 1) returns Integer
	return a
end 'f'

// --- file: b.maxon
typealias Integer = int(i64.min to i64.max)

export function f(a Integer = 1) returns Integer
	return a + 1
end 'f'

// --- file: main.maxon
function main() returns ExitCode
	return f() as ExitCode
end 'main'
```
```maxoncstderr
error E3006: <fragment>:12:17: duplicate definition of function 'f#Integer' — 'f' is declared as a free function in more than one FILE of its directory, so every one of those declarations is registered under its parameter-type spelling, and two of them spell the same parameters. Give the overloads distinct parameter types, or distinct names
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
