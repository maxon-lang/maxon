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
			ok(v) gives v as ExitCode
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
error E3004: <fragment>:12:10: call to undefined function 'Gate.nope'
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
error E2010: <fragment>:12:15: Expected ''min' or 'max'' but got 'nope'
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
