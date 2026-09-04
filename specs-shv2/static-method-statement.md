---
feature: static-method-statement
status: experimental
keywords: [static, statement, void, effect, call]
category: types
---

# A Static Method Called for Its Effect

## Documentation

A `static function` that declares no return type returns nothing, so the only position it can be
written in is a **statement** — there is no value to bind and nothing to discard. `Helper.shout()` on
a line of its own is that call.

A static that DOES return a value is refused too, but by the DISCARD rule and not by the statement
door: its result fell away, so it is E3064 when the callee is pure and E3065 when it is impure, exactly
as for every other call written on a line of its own. The refusal therefore names the callee and offers
the `_ = expr` cure where one exists, instead of saying the shape is unsupported — which it is not.
`discarded-results.md` carries all three verdicts.

⭐ These cases are shv2-authored rather than ported. `specs/static-methods.md` covers statics
thoroughly — three cases, all green here — but **every one of them returns a value**, so the corpus
had no case in this position at all. That is why the gap survived: shv2 accepted
`__Builtins.sleep(ms)` and `__ManagedFile.statFree(buf)` in statement position through a recognizer
listing compiler-owned callees, and refused the identical user-written shape as
`E2015: Unsupported: identifier statement`. The asymmetry was never a rule, only which callees
happened to have a recognizer; the oracle compiles and runs the user form.

## Tests

<!-- test: static-method-statement.void-static-for-effect -->
A user type's VOID static called as a bare statement. The predicate behind the statement door asks the
whole-program index whether the BASE names a declared type, rather than consulting a table of
compiler-owned names, so a static is a statement wherever one is written. Prints `hi` and exits 0 —
byte-for-byte what the bootstrap oracle does with the same program.
```maxon
type Helper
	export static function shout()
		print("hi")
	end 'shout'
end 'Helper'

function main() returns ExitCode
	Helper.shout()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi
```

<!-- test: static-method-statement.void-static-runs-for-its-side-effect -->
The call is not merely ACCEPTED, it RUNS — and it runs once per statement, which a compiles-clean case
would miss entirely. Two bare `Announce.line()` statements must produce two `tick`s (`print` appends no newline, so the
expected stdout is `ticktick` — and that spelling is what distinguishes ONE call from TWO). A statement
door that parsed a call and dropped it, or folded the pair into one, is caught here rather than passing
quietly.
```maxon
type Announce
	export static function line()
		print("tick")
	end 'line'
end 'Announce'

function main() returns ExitCode
	Announce.line()
	Announce.line()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ticktick
```

<!-- test: static-method-statement.error.value-returning-static-is-still-refused -->
The NEGATIVE half. A static whose result falls off the statement is still refused — but under the
DISCARD code, which names the callee and points at the member the author wrote. Accepting the shape
strands no box: a program carrying this diagnostic never reaches codegen. Measured identical on the
oracle.
```maxon
type Point
	export var x as ExitCode

	export static function create() returns Point
		return Self{x: 1}
	end 'create'
end 'Point'

function main() returns ExitCode
	Point.create()
	return 0
end 'main'
```
```maxoncstderr
error E3064: <fragment>:11:8: result of pure function 'Point.create' must be used
```

<!-- test: static-method-statement.error.unknown-static-names-the-callee -->
A TYPO in a static's name, in statement position. Until this rung it was
`E2015: Unsupported: identifier statement` — a message about the SHAPE, which became false the moment a
void static became a legal statement: the construct is supported, the name is wrong, and E2015 says
nothing about a name. The statement door claims a qualified call on the strength of its BASE alone, so a
member that resolves to nothing is refused by `parseCallNamed`, which names the callee.
⭐ This is the same answer TWO other positions already gave for the identical typo — expression position
(`let x = Helper.nope()`) and the compiler-owned half in THIS position (`__Builtins.nope()`), both
measured — so the rung removes a third spelling rather than adding one.
```maxon
type Helper
	export static function shout()
		print("hi")
	end 'shout'
end 'Helper'

function main() returns ExitCode
	Helper.nope()
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:9:9: call to undefined function 'Helper.nope'
```

<!-- test: static-method-statement.keyword-named-static-is-a-statement -->
⭐⭐ A static DECLARED UNDER A KEYWORD (D8), called as a bare statement. Found in review: the statement
door's shape check asked a raw `TokenKind.identifier` for the member, so `Helper.to()` was refused as
`E2015: Unsupported: identifier statement` — while EXPRESSION position compiled and ran the identical
name (`let x = Helper.to()`, measured), and so did the bootstrap oracle for this very program. That is
the same asymmetry this rung was written to remove, surviving one level down in the shape check standing
in front of the fix. The member half now asks `namesMemberAt`, the predicate `methodCallsAt`,
`memberCallFollows` and `fieldAssignsAt` already share, so a member-naming token is taught to all four
at once. Prints `mut`.
```maxon
type Helper
	export static function match()
		print("m")
	end 'match'

	export static function upto()
		print("u")
	end 'upto'

	export static function to()
		print("t")
	end 'to'
end 'Helper'

function main() returns ExitCode
	Helper.match()
	Helper.upto()
	Helper.to()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
mut
```

<!-- test: static-method-statement.error.unknown-keyword-named-static-names-the-callee -->
The typo half of the case above, and what keeps the widened shape check from swallowing a name. A
keyword-spelled member that no file declares is claimed by the same base-is-a-declared-struct arm an
identifier-spelled one is, so it earns the positioned E3004 naming the callee rather than the shape
message. Without this the widening would be provable only in its accepting direction.
```maxon
type Helper
	export static function shout()
		print("hi")
	end 'shout'
end 'Helper'

function main() returns ExitCode
	Helper.while()
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:9:9: call to undefined function 'Helper.while'
```

<!-- test: static-method-statement.void-static-through-an-inner-generic-alias -->
⭐⭐ **THE BASE IS AN INNER GENERIC ALIAS, AND THIS DOOR HAS TO READ IT UNDER THE SAME KEY THE EXPRESSION
DOOR READS (found in W7's review).** `typealias IntPair = Pair with ExitCode` declared INSIDE `Plain` is
keyed whole-program as `Plain.IntPair`, so a statement door asking the BARE member finds no registration,
mangles `IntPair.shout`, and refuses a program the expression door on the very next line resolves
perfectly.

⚠ **MEASURED AS AN ASYMMETRY INSIDE ONE BINARY, which is what makes it a defect rather than a missing
feature.** With the identical alias moved to FILE scope this program already compiled and printed `shout`;
declared inside the calling type it was `E2015: Unsupported: identifier statement` — one written spelling
answered two ways, which is exactly what `qualifiedStaticCallsAt`'s own header forbids and what W7's
`genericAliasKeyFor` exists to make impossible. The bootstrap oracle cannot arbitrate: it fails this
program with an internal `E9001 … Function 'IntPair.shout' not found in module`, its own spelling of the
same missing lookup.
```maxon
type Pair uses T
	export var a as ExitCode

	export static function create(a ExitCode) returns Self
		return Self{a: a}
	end 'create'

	export static function shout()
		print("hi")
	end 'shout'
end 'Pair'

type Plain
	typealias IntPair = Pair with ExitCode

	export static function make() returns ExitCode
		IntPair.shout()
		let p = IntPair.create(0)
		return p.a
	end 'make'
end 'Plain'

function main() returns ExitCode
	return Plain.make()
end 'main'
```
```exitcode
0
```
```stdout
hi
```

<!-- test: static-method-statement.error.unknown-static-through-an-inner-generic-alias -->
The typo half, and the half that says the base was genuinely RESOLVED rather than merely tolerated. An
unknown member on an inner generic alias is refused by NAME through the alias's BASE STRUCT — the same
callee `parseQualifiedCall` would have mangled — and not by the shape message a base this door failed to
resolve would have earned. `Pair` in the message, not `IntPair`, is the whole evidence: it is the name
only the whole-program key can produce.
```maxon
type Pair uses T
	export var a as ExitCode

	export static function create(a ExitCode) returns Self
		return Self{a: a}
	end 'create'
end 'Pair'

type Plain
	typealias IntPair = Pair with ExitCode

	export static function make() returns ExitCode
		IntPair.nope()
		return 0
	end 'make'
end 'Plain'

function main() returns ExitCode
	return Plain.make()
end 'main'
```
```maxoncstderr
error E3004: <fragment>:14:11: call to undefined function 'Pair.nope'
```
