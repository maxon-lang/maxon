---
feature: self-type-member-expression
status: experimental
keywords: [Self, static, enum, union, member, expression, type-system]
category: type-system
---

# `Self.<member>` in an EXPRESSION

## Documentation

`Self` names the type whose body encloses it. `specs-shv2/self-keyword.md` covers it in a **type**
position (`returns Self`, `other Self`) and every struct spec in the corpus covers `Self{…}`, the struct
literal. This file pins the third position — `Self` as the **base of a dotted expression** — which no
`/specs` file exercises at all: a search of the whole 284-file corpus for `Self.` finds **zero** hits,
in either the reference corpus or `specs-shv2`.

The rule is that there is no rule of its own: **`Self.<member>` means exactly what the enclosing type's
own NAME means there.** Inside `enum Toggle`, `Self.off` IS `Toggle.off`; inside `type Gate`,
`Self.make(v)` IS `Gate.make(v)`. So the two spellings take the same arm, mangle the same callee, and
report the same diagnostic on the same token — the parser resolves the keyword to the name it denotes at
the ONE place a type-named form reads its base (`Parser.typeBaseTokenAt`), and no arm downstream knows
the keyword exists.

⚠ **Before D9, `Self` in an expression could be exactly one thing — `Self{…}` — and `Self.` fell off
that single expectation in two directions.** In a `type` body it surfaced as `E2010 Expected '{' but got
'.'`; in an `enum` body it routed on into the struct literal's constructibility check and took the
compiler down with a **panic** that named an `enum` a `type` and blamed a declaration-sweep
disagreement that had not happened. Both programs are legal Maxon and the reference bootstrap runs both
(measured: each returns 42).

⚠ **A struct literal naming an `enum`/`union` is refused HERE rather than crashing**, and that is the
second half of the same door: `Self{…}` (and the `Toggle{…}` spelling of it) inside `enum Toggle`'s own
body is the only place the struct-literal path can be asked for a layout no `type` declared. **Both
reference compilers crash on it** — the bootstrap with an unhandled `IrEnumType`→`IrStructType` cast
inside `ParseStructLiteral`, reported as an internal `E9001` — so there is no oracle spelling to match
and the refusal names the cure instead.

⚠ **A static call in STATEMENT position stays refused, for `Self` exactly as for a type name.**
`Gate.make(1)` on a line of its own discards a box nothing would then free; the reference bootstrap
refuses the `Self` spelling too (`E2001 unexpected token: 'Self'`). Both spellings are pinned below so
that a rung which opens one cannot leave the other behind.

Every case below was a **hand probe first** (D9, 2026-07-30), and each was run against the reference
bootstrap as well as against shv2 — the accepting ones agree with it on the exit code, and the two
refusals it *also* refuses (`Self.nope` on an enum, `let Self = …`) agree with it character for
character. The ones that found nothing are here for the reason `enum-union-method-receiver.md` gives:
next rung, only a committed case still runs.

## Tests

<!-- test: enum-case-name-through-Self -->
### An enum CASE NAME through `Self`
The panicking half of D9. `Self.off` is `Toggle.off`, in a comparison and in a `return`.
```maxon
enum Toggle
	off
	on

	export function flipped() returns Toggle
		if self == Self.off 'isOff'
			return Self.on
		end 'isOff'
		return Self.off
	end 'flipped'
end 'Toggle'

function main() returns ExitCode
	let t = Toggle.off
	if t.flipped() == Toggle.on 'ok'
		return 42
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: static-call-through-Self -->
### A qualified STATIC CALL through `Self`
The `E2010` half of D9: `Self.make(self.n)` is `Gate.make(self.n)`.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export function twin() returns Gate
		return Self.make(self.n)
	end 'twin'
end 'Gate'

function main() returns ExitCode
	let g = Gate.make(21)
	return g.twin().n + 21
end 'main'
```
```exitcode
42
```

<!-- test: static-call-through-Self-with-labelled-arguments -->
### A static call through `Self` with a LABELLED argument
The argument list is slotted against the declaration by the same `slotCallArgs` the type-named spelling
uses, so a reordering label has to land in the same slot.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function two(a Num, b Num) returns Gate
		return Self{n: a + b}
	end 'two'

	export function twin() returns Num
		return Self.two(self.n, b: 1).n
	end 'twin'
end 'Gate'

function main() returns ExitCode
	return Gate.two(40, b: 1).twin()
end 'main'
```
```exitcode
42
```

<!-- test: static-call-through-Self-from-another-static -->
### A static call through `Self` from inside ANOTHER static
There is no receiver here at all, which is what makes it worth its own case: `Self` is resolved from the
enclosing DECLARATION, never from an instance.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export static function twice(v Num) returns Gate
		return Self.make(v + v)
	end 'twice'
end 'Gate'

function main() returns ExitCode
	return Gate.twice(21).n
end 'main'
```
```exitcode
42
```

<!-- test: union-payload-case-through-Self -->
### A UNION case through `Self` — payload-carrying and payload-free
`Self.ok(42)` constructs the box; `Self.none` is the bare case of the same boxed union.
```maxon
typealias Num = int(0 to 100)

union Res
	ok(v Num)
	none

	export function bump() returns Res
		return Self.ok(42)
	end 'bump'

	export function blank() returns Res
		return Self.none
	end 'blank'
end 'Res'

function main() returns ExitCode
	let r = Res.none
	let s = r.bump()
	let t = s.blank()
	return match t 'blanked'
		ok gives 1
		none gives match s 'bumped'
			ok(v) gives v
			none gives 2
		end 'bumped'
	end 'blanked'
end 'main'
```
```exitcode
42
```

<!-- test: throwing-static-through-Self-under-try -->
### A THROWING static through `Self` as a `try` target
A `try` target is its own dispatch position, and it admitted the type-named spelling only — so this
program was `E2015 try must be applied to a call … (got 'Self')` for a call that is right there.
```maxon
typealias Num = int(0 to 1000)

enum Err
	bad
end 'Err'

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export static function check(v Num) returns Num throws Err
		if v == 0 'zero'
			throw Err.bad
		end 'zero'
		return v
	end 'check'

	export function twin() returns Num
		return try Self.check(self.n) otherwise 3
	end 'twin'
end 'Gate'

function main() returns ExitCode
	return Gate.make(42).twin()
end 'main'
```
```exitcode
42
```

<!-- test: static-call-through-Self-inside-a-closure -->
### A static call through `Self` inside a CLOSURE body
The closure lifts to a top-level function, so the resolution has to have happened at parse time inside
the type's body — the lifted function has no enclosing type of its own.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export static function dbl(v Num) returns Num
		return v + v
	end 'dbl'

	export function viaClosure() returns Num
		let f = function(v Num) gives Self.dbl(v)
		return f(21)
	end 'viaClosure'
end 'Gate'

function main() returns ExitCode
	return Gate.make(1).viaClosure()
end 'main'
```
```exitcode
42
```

<!-- test: static-call-through-Self-in-a-generic-type -->
### A static call through `Self` in a GENERIC type's body
`Self` in a generic body is the base generic name, which is what the type-named spelling `Box.make(x)`
already resolves to — the instantiation is decided at the call site, not here.
```maxon
typealias Num = int(0 to 1000)

type Box uses T
	export var v as T

	export static function make(x T) returns Self
		return Self{v: x}
	end 'make'

	export static function twice(x T) returns Self
		return Self.make(x)
	end 'twice'
end 'Box'

typealias IntBox = Box with Num

function main() returns ExitCode
	return IntBox.twice(42).v
end 'main'
```
```exitcode
42
```

<!-- test: Self-as-a-type-and-as-an-expression-base-in-one-body -->
### `Self` as a RETURN type, a PARAMETER type and an expression base, in one declaration
The control for D9 and its widening in one program: the type positions must keep working unchanged
while the expression position starts to.
```maxon
enum Toggle
	off
	on

	export function same() returns Self
		return self
	end 'same'

	export function matches(other Self) returns bool
		return self == other
	end 'matches'

	export function isOff() returns bool
		return self.matches(Self.off)
	end 'isOff'
end 'Toggle'

function main() returns ExitCode
	let t = Toggle.off
	if t.isOff() 'ok'
		if t.same() == Toggle.off 'ok2'
			return 42
		end 'ok2'
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: error.struct-literal-naming-the-enclosing-enum-through-Self -->
### A struct literal through `Self` inside an `enum` body is refused
`Self{}` in an enum's own body is the one place the struct-literal path can be handed a name no `type`
declared. It took the compiler down with a panic that blamed the declaration sweep.
```maxon
enum Toggle
	off
	on

	export function bad() returns Toggle
		return Self{}
	end 'bad'
end 'Toggle'

function main() returns ExitCode
	let t = Toggle.off
	if t == Toggle.off 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:10: Unsupported: a struct literal naming `enum`/`union` `Toggle` — an enum/union declares no fields to write; a value of it is a CASE (`Toggle.<case>`, or `Self.<case>` from inside its own body)
```

<!-- test: error.struct-literal-naming-the-enclosing-enum-by-name -->
### The same refusal for the enum's OWN NAME
The two spellings reach one door, so they must report one thing. Pinned separately because only one of
them is `Self`, and a fix that taught the keyword alone would leave this one crashing.
```maxon
enum Toggle
	off
	on

	export function bad() returns Toggle
		return Toggle{}
	end 'bad'
end 'Toggle'

function main() returns ExitCode
	let t = Toggle.off
	if t == Toggle.off 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:10: Unsupported: a struct literal naming `enum`/`union` `Toggle` — an enum/union declares no fields to write; a value of it is a CASE (`Toggle.<case>`, or `Self.<case>` from inside its own body)
```

<!-- test: error.unknown-static-through-Self -->
### A static that does not exist, called through `Self`
The diagnostic names the type `Self` DENOTES and is anchored at the `Self` the author wrote — the same
`E3004` the `Gate.nope()` spelling reports, on the same column.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export function bad() returns Num
		return Self.nope()
	end 'bad'
end 'Gate'

function main() returns ExitCode
	return Gate.make(0).n
end 'main'
```
```maxoncstderr
error E3004: <fragment>:12:15: call to undefined function 'Gate.nope'
```

<!-- test: error.unknown-member-through-Self -->
### A `Self.<member>` that is neither a case nor a call
It falls to the type-qualified-bound arm, the same last resort `Gate.nope` falls to, and reports the
same thing on the same token rather than an `E2010` about the keyword.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export function bad() returns Num
		return Self.nope
	end 'bad'
end 'Gate'

function main() returns ExitCode
	return Gate.make(0).n
end 'main'
```
```maxoncstderr
error E2010: <fragment>:12:15: Expected 'min or max' but got 'nope'
```

<!-- test: error.Self-member-at-file-scope -->
### `Self.<member>` outside any type declaration
`Self` names nothing at file scope, and the expression position says so in the same words the type
position does — one reader answers both.
```maxon
function main() returns ExitCode
	let x = Self.foo
	return x
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:10: Unsupported: 'Self' outside a type declaration (`Self` names the type whose body encloses it, and there is none here)
```

<!-- test: error.Self-static-call-as-a-statement -->
### A static call through `Self` on a line of its own is refused
Deliberate parity with the type-named spelling below: a discarded factory result is a box nothing frees.
The reference bootstrap refuses this spelling too (`E2001 unexpected token: 'Self'`).
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export function bad() returns Num
		Self.make(1)
		return 1
	end 'bad'
end 'Gate'

function main() returns ExitCode
	return Gate.make(0).n
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:3: Unsupported: Self statement
```

<!-- test: error.static-call-as-a-statement -->
### …and for the type-named spelling
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export function bad() returns Num
		Gate.make(1)
		return 1
	end 'bad'
end 'Gate'

function main() returns ExitCode
	return Gate.make(0).n
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:3: Unsupported: identifier statement
```

<!-- test: keyword-named-enum-case-through-Self -->
### A KEYWORD-named enum case through `Self`
Where D9 crosses D8: a case may be spelled with a keyword, and `Self.while` has to read `while` as the
member name while `Self` is itself a keyword being read as a type name. Two keyword rewrites in one
three-token expression.
```maxon
enum Kw
	while
	end

	export function pick() returns Kw
		if self == Self.while 'w'
			return Self.end
		end 'w'
		return Self.while
	end 'pick'
end 'Kw'

function main() returns ExitCode
	let k = Kw.while
	if k.pick() == Kw.end 'ok'
		return 42
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: Self-case-in-every-arm-of-a-match -->
### `Self.<case>` in every arm of a `match`, payload-carrying and payload-free
Each arm rebuilds the union through `Self`, so the box the arm CONSTRUCTS and the box the arm
DESTRUCTURED are live at the same time — the shape a per-arm refcount error shows up in.
```maxon
typealias Num = int(0 to 100)

union R
	a(v Num)
	b(v Num)
	c

	export function norm() returns R
		return match self 'k'
			a(v) gives Self.b(v)
			b(v) gives Self.a(v)
			c gives Self.c
		end 'k'
	end 'norm'
end 'R'

function main() returns ExitCode
	let x = R.a(42)
	let y = x.norm()
	return match y 'k'
		a gives 1
		b(v) gives v
		c gives 2
	end 'k'
end 'main'
```
```exitcode
42
```

<!-- test: managed-payload-case-through-Self -->
### A MANAGED payload constructed through `Self`
The payload is a `String`, so the construct allocates and the box must be dropped exactly once — an
unbalanced refcount here is exit 101, not a wrong number.
```maxon
union Msg
	text(s String)
	silent

	export function shout() returns Msg
		return Self.text("hi")
	end 'shout'
end 'Msg'

function main() returns ExitCode
	let m = Msg.silent
	let n = m.shout()
	return match n 'k'
		text(s) gives s.byteLength() as ExitCode
		silent gives 1
	end 'k'
end 'main'
```
```exitcode
2
```

<!-- test: Self-static-result-as-a-call-argument -->
### A `Self.` static's result passed as a call ARGUMENT
Argument position is its own drop site — the box is an unbound owned temporary here, freed at statement
end rather than at a binding's scope exit.
```maxon
typealias Num = int(0 to 1000)

function take(g Num) returns Num
	return g
end 'take'

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export function arg() returns Num
		return take(Self.make(42).n)
	end 'arg'
end 'Gate'

function main() returns ExitCode
	return Gate.make(0).arg()
end 'main'
```
```exitcode
42
```

<!-- test: a-local-named-after-the-enclosing-type-does-not-capture-Self -->
### A local named after the enclosing TYPE does not capture `Self.`
The arm-order safety argument, and the only case that pins it: `Gate.` would resolve to the local
binding (a value outranks a type name), and `Self.` must not — it cannot mean a local under any
spelling. Resolving the base before the scope-based arms would send this to `parseMethodCall`.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export function twin() returns Num
		let Gate = 1
		return Self.make(41).n + Gate
	end 'twin'
end 'Gate'

function main() returns ExitCode
	return Gate.make(0).twin()
end 'main'
```
```exitcode
42
```

<!-- test: Self-static-called-from-an-instance-method-of-a-generic -->
### `Self.` in a generic type's INSTANCE method
The sibling of the static-to-static generic case above: the receiver exists here, and `Self` still comes
from the enclosing DECLARATION rather than from the instance's type argument.
```maxon
typealias Num = int(0 to 1000)

type Holder uses T
	export var v as T

	export static function make(x T) returns Self
		return Self{v: x}
	end 'make'

	export function twin() returns T
		return Self.make(self.v).v
	end 'twin'
end 'Holder'

typealias NumHolder = Holder with Num

function main() returns ExitCode
	return NumHolder.make(42).twin()
end 'main'
```
```exitcode
42
```

<!-- test: Self-static-across-a-file-boundary -->
### `Self.` in a type declared in ANOTHER file
The rewritten token's name is a slice of THIS file's source buffer and it feeds this file's own artifact
interner, so a `Self.` resolved in one file must not leak an id the other file's merge cannot resolve.
⚠ The reference bootstrap does not compile this program at all (`E4006 Unknown type 'Gate' in field
access chain`) — a bootstrap gap in directory projects, not a divergence shv2 owes anything to.
```maxon
// --- file: gate.maxon
typealias Num = int(0 to 1000)

export type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export static function twice(v Num) returns Gate
		return Self.make(v + v)
	end 'twice'
end 'Gate'

// --- file: main.maxon
function main() returns ExitCode
	return Gate.twice(21).n
end 'main'
```
```exitcode
42
```

<!-- test: error.unknown-case-through-Self-on-an-enum -->
### A case that does not exist, named through `Self`
The enum-side twin of the unknown-static refusal, and it is the reference bootstrap's diagnostic
character for character — the `Self` spelling reaches the same `E3034` on the same column the
`Toggle.nope` spelling does.
```maxon
enum Toggle
	off
	on

	export function bad() returns Toggle
		return Self.nope
	end 'bad'
end 'Toggle'

function main() returns ExitCode
	let t = Toggle.off
	if t == Toggle.off 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3034: <fragment>:7:10: unknown enum case: 'nope'
```

<!-- test: error.struct-literal-naming-the-enclosing-union -->
### A struct literal naming the enclosing `union`
The `union` half of the enum refusal — the same guard, reached through the same door, and pinned
separately because it is a different declaration keyword arriving at it.
```maxon
typealias Num = int(0 to 100)

union Res
	ok(v Num)
	none

	export function bad() returns Res
		return Self{}
	end 'bad'
end 'Res'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:10: Unsupported: a struct literal naming `enum`/`union` `Res` — an enum/union declares no fields to write; a value of it is a CASE (`Res.<case>`, or `Self.<case>` from inside its own body)
```

<!-- test: error.bare-Self-in-an-expression -->
### A bare `Self` in an expression, with neither `{` nor `.`
Now that `.` is handled, `{` genuinely IS the only continuation left — so the "Expected `{`" the D9 bug
used to report for `Self.` becomes an honest message here rather than a misleading one. (The reference
bootstrap reports `E3003 'Gate' is a type and cannot be used directly as a value` instead; both refuse,
and shv2's names the token it stopped at.)
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export function bad() returns Num
		return Self
	end 'bad'
end 'Gate'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:8:14: Expected '{' but got 'newline'
```

<!-- test: error.Self-as-a-binding-name -->
### `Self` cannot be bound by a `let`
A `let`/`var`, a `for … in` variable, a struct FIELD and a top-level binding each read their name with
the strict identifier reader, so none of them can be spelled `Self`. The reference bootstrap's own words,
character for character apart from its unquoted `identifier`.

⚠ **This is NOT what makes the arm order safe, and the D9 review had to correct that claim.** A function
or closure PARAMETER *can* be named `Self` — a parameter's name may be spelled with a keyword (D8) — so a
binding named `Self` genuinely can exist and the scope test genuinely can find it. What makes `Self.`
safe is the base position REFUSING to consult the value namespace about it
(`Parser.baseNamesAValueInScope`); the cases below pin that, and the ones above pinned only the local
named after the enclosing type, which is a different question.
```maxon
function main() returns ExitCode
	let Self = 5
	return Self
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:6: Expected identifier but got 'Self'
```

<!-- test: parameter-named-Self-does-not-capture-a-static-call -->
### A PARAMETER named `Self` does not capture `Self.` — and `Self{…}` agrees with it
⚠ **The D9 review's finding.** `Self` reached `parseDottedPrimary`'s value-based arms as a raw token, so a
parameter named `Self` — which D8's keyword-as-a-declared-name rule admits — was found by the scope test
and `Self.make(41)` was read as a METHOD CALL on that parameter: *"'int' has no method named 'make'"*. The
same body's `Self{n: v}` meant the TYPE all along, because `parsePrimary`'s `selfType` arm claims `{`
ahead of any scope test — so one function had `Self` meaning two different things three lines apart. The
reference bootstrap CANNOT COMPILE this program at all, and the reason is unrelated to `Self`: it refuses
`Gate{n: 1}.n` — a field access applied to a struct LITERAL — with `E2004 Cannot operate on int and struct`,
which reproduces with no `Self`-named parameter anywhere in the file. shv2 accepts that expression, so the
bootstrap is not an oracle for this case. (Its earlier claim here, that the bootstrap "resolves both to the
type and leaves the parameter merely unread", was measured FALSE and is corrected.) With the
struct-literal term removed the bootstrap does report `E3012 … unused variable: 'Self'`, and it reports
exactly that on the two sibling cases below, which is what this case's expectation rests on.

⭐ **IT PINS `E3012`, AND THE MISREAD IT WAS WRITTEN FOR IS STILL WHAT IT TESTS.** A parameter named `Self`
is never read — reading one is refused outright (`error.bare-Self-read-under-a-Self-named-parameter`) — so
the NAME is the subject and `_` would delete the case. An unread parameter is `E3012` (see
`unused-parameters`), a SEMANTIC error: reaching it proves both `Self.make(41)` and `Self{n: v}` resolved
to the TYPE, because the misread was a refusal this program would never have got past.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export function twin(Self Num) returns Num
		return Self.make(41).n + Self{n: 1}.n
	end 'twin'
end 'Gate'

function main() returns ExitCode
	return Gate.make(0).twin(7)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/self-type-member-expression/parameter-named-Self-does-not-capture-a-static-call.test:11:23: unused variable: 'Self'
```

<!-- test: parameter-named-Self-does-not-capture-an-enum-case -->
### A parameter named `Self` does not capture `Self.<case>` either
The enum-side face of the same finding, and it took a different arm: an enum case reference is not a call,
so it fell to the FIELD-ACCESS arm and reported *"a field access on 'Self', which is declared 'int' and
not a struct type"* for a case that is right there in the enum.

⭐ **IT NOW PINS `E3012`, AND THE MISREAD IT WAS WRITTEN FOR IS STILL WHAT IT TESTS.** A parameter named
`Self` is never read — reading one is refused outright
(`error.bare-Self-read-under-a-Self-named-parameter`) — so the NAME is the subject and `_` would delete the
case. An unread parameter is `E3012` (see `unused-parameters`), a SEMANTIC error, so reaching it proves
`Self.on` resolved to the enum case and not to the parameter: the old misread was a refusal this program
would never have got past. Measured on the bootstrap: the same `E3012 … unused variable: 'Self'` at the
same position.
```maxon
typealias Num = int(0 to 100)

enum Toggle
	off
	on

	export function pick(Self Num) returns Toggle
		return Self.on
	end 'pick'
end 'Toggle'

function main() returns ExitCode
	let t = Toggle.off
	if t.pick(1) == Toggle.on 'ok'
		return 42
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/self-type-member-expression/parameter-named-Self-does-not-capture-an-enum-case.test:8:23: unused variable: 'Self'
```

<!-- test: closure-parameter-named-Self-does-not-capture-Self -->
### A CLOSURE parameter named `Self` does not capture `Self.`
A third door onto the same scope test — the closure's own parameter scope rather than the method's — so a
guard placed on the method path alone would leave this one misreading.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export function twin() returns Num
		let f = function(Self Num) gives Self.make(41).n + 1
		return f(7)
	end 'twin'
end 'Gate'

function main() returns ExitCode
	return Gate.make(0).twin()
end 'main'
```
```exitcode
42
```

<!-- test: managed-payload-through-Self-under-a-Self-named-parameter -->
### A MANAGED payload through `Self` while a parameter shadows the name
The refcount half: the misread base built the box through a different arm, so the drop site has to be
confirmed under the shadow too.

⭐ **IT NOW PINS `E3012` RATHER THAN THE EXIT CODE, and the refcount half is what that costs.** A parameter
named `Self` is never read, so the name is the subject and `_` would delete the case; an unread parameter is
`E3012` (see `unused-parameters`), and a program that does not compile cannot be run. What SURVIVES is the
resolution under the shadow — E3012 is semantic, so reaching it proves `Self.text("hi")` bound the union
case rather than the parameter. What is LOST is the drop-site confirmation, which needed the program to
run: the managed-payload refcount under a `Self`-named parameter is now covered by no case at all.
Measured on the bootstrap: the same `E3012 … unused variable: 'Self'` at the same position.
```maxon
typealias Num = int(0 to 100)

union Msg
	text(s String)
	silent

	export function shout(Self Num) returns Msg
		return Self.text("hi")
	end 'shout'
end 'Msg'

function main() returns ExitCode
	let m = Msg.silent
	let n = m.shout(1)
	return match n 'k'
		text(s) gives s.byteLength() as ExitCode
		silent gives 1
	end 'k'
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/self-type-member-expression/managed-payload-through-Self-under-a-Self-named-parameter.test:8:24: unused variable: 'Self'
```

<!-- test: managed-payload-through-Self-runs-without-a-Self-named-parameter -->
### A MANAGED payload through `Self` — the RUNTIME half, with no shadowing parameter
⭐ **THIS CASE EXISTS BECAUSE THE ONE ABOVE STOPPED RUNNING.** `managed-payload-through-Self-under-a-Self-named-parameter`
now pins `E3012` (its parameter named `Self` is unread by construction, which is the whole subject), and a
program that does not compile cannot check a refcount. That flip would otherwise have removed the LAST
program reaching `Self.<case>(<managed payload>)` at run time — the exact way this repo has lost a
mechanism's whole coverage before. The construction and the drop site are identical; only the shadowing
parameter is gone, so an unbalanced refcount here is still exit 101 rather than a wrong number.
```maxon
typealias Num = int(0 to 100)

union Msg
	text(s String)
	silent

	export function shout(volume Num) returns Msg
		return Self.text("hi{volume}")
	end 'shout'
end 'Msg'

function main() returns ExitCode
	let m = Msg.silent
	let n = m.shout(1)
	return match n 'k'
		text(s) gives s.byteLength() as ExitCode
		silent gives 1
	end 'k'
end 'main'
```
```exitcode
3
```

<!-- test: error.bare-Self-read-under-a-Self-named-parameter -->
### A bare `Self` read is still refused when a parameter is named `Self`
The other half of the rule, and the one that makes the parameter harmless rather than merely
unreachable: `Self` alone is a TYPE in every expression position, so a parameter spelled that way can be
declared and never read. The reference bootstrap refuses it too (`E3003 'Gate' is a type and cannot be
used directly as a value`); shv2 names the token it stopped at.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export function bad(Self Num) returns Num
		return Self
	end 'bad'
end 'Gate'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:8:14: Expected '{' but got 'newline'
```

<!-- test: enum-case-named-Self-through-Self -->
### An enum case NAMED `Self`, referenced through `Self`
Where D8 and D9 cross at their sharpest: `Self.Self` reads the BASE as the enclosing type and the MEMBER
as a case whose own name is the same keyword. Two different rules about one word in three tokens. Found
nothing; committed because only a committed case still runs, and because a review that reserved `Self` at
`requireUnreservedName` would have silently refused this program (the bootstrap runs it, exit 42).
```maxon
enum Kw
	Self
	on

	export function pick() returns Kw
		return Self.Self
	end 'pick'
end 'Kw'

function main() returns ExitCode
	let k = Kw.on
	if k.pick() == Kw.Self 'ok'
		return 42
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: static-method-named-Self-through-Self -->
### A STATIC METHOD named `Self`, called through `Self`
The call-shaped twin of the case above: the base resolves to `Gate` and the callee mangles to
`Gate.Self`, so the member reader and the base reader disagree about the same word by design.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export static function Self(v Num) returns Num
		return v
	end 'Self'

	export function twin() returns Num
		return Self.Self(42)
	end 'twin'
end 'Gate'

function main() returns ExitCode
	return Gate.make(0).twin()
end 'main'
```
```exitcode
42
```

<!-- test: error.store-through-Self-is-not-captured-by-a-Self-named-parameter -->
### A STORE through `Self.` is refused the same way whether or not a parameter shadows the name
The third door onto the shadowing rule, and the last one that was still shadowable: a chain store
RESOLVES its base (it wants the binding's `VarInfo`, not a yes/no), so it carries its own copy of the
scope test. With `bad(Self Gate)` in scope it resolved to the PARAMETER and reported *"cannot assign to
immutable variable: 'Self'"*. No wrong answer was reachable through it — only a parameter may be named
`Self` and a parameter is immutable, so the store could never have succeeded — but the rule then held at
two doors of three. It now reports exactly what the `Gate.n = 5` spelling reports, on the same token.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Self{n: v}
	end 'make'

	export function bad(Self Gate) returns Num
		Self.n = 5
		return 1
	end 'bad'
end 'Gate'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:12:3: Undefined variable 'Self'
```

<!-- test: error.store-through-the-enclosing-type-name -->
### …and the type-named spelling it has to agree with
Pinned separately for the reason the enum struct-literal pair is: only one of the two is `Self`, and a
guard taught the keyword alone would let these two drift apart without a compile error anywhere.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export function bad() returns Num
		Gate.n = 5
		return 1
	end 'bad'
end 'Gate'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:8:3: Undefined variable 'Gate'
```
