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

A static that DOES return a value stays refused in statement position. The reason is not symmetry: a
constructor-shaped static hands back a box, and a bare statement would discard the only reference to
it, so nothing would ever free it. That argument is about the returned value and does not reach a void
static, which has none.

⭐ These cases are shv2-authored rather than ported. `specs/static-methods.md` covers statics
thoroughly — three cases, all green here — but **every one of them returns a value**, so the corpus
had no case in this position at all. That is why the gap survived: shv2 accepted
`__Builtins.sleep(ms)` and `__ManagedFile.statFree(buf)` in statement position through a recognizer
listing compiler-owned callees, and refused the identical user-written shape as
`E2015: Unsupported: identifier statement`. The asymmetry was never a rule, only which callees
happened to have a recognizer; the oracle compiles and runs the user form.

## Tests

<!-- test: static-method-statement.void-static-for-effect -->
A user type's VOID static called as a bare statement. The predicate behind the statement door now asks
the whole-program index for the callee's return type instead of consulting a table of compiler-owned
names, so a declared `void` static is accepted wherever one is written. Prints `hi` and exits 0 —
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
The NEGATIVE half, and the one that says the fix is a rule rather than a widening. A static that
returns a value is still refused in statement position, because a bare statement would discard the box
it hands back and nothing would then free it. Only `void` is accepted, so this case is what keeps the
door from being opened to every static.
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
error E2015: <fragment>:11:2: Unsupported: identifier statement
```

<!-- test: static-method-statement.error.unknown-static-names-the-callee -->
A TYPO in a static's name, in statement position. Until this rung it was
`E2015: Unsupported: identifier statement` — a message about the SHAPE, which became false the moment a
void static became a legal statement: the construct is supported, the name is wrong, and E2015 says
nothing about a name. The statement door now claims a qualified call whose base is a declared type even
when the member resolves to nothing, so the refusal comes from `parseCallNamed` and names the callee.
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
error E3004: <fragment>:9:2: call to undefined function 'Helper.nope'
```
