---
feature: static-instance-name-duplicates
status: experimental
keywords: [static, instance, method, duplicate, overload, mangling, diagnostics]
category: diagnostics
---

# A `static` member and an INSTANCE member of one name are two functions — and a duplicate of either still collides

## Documentation

`specs/same-name-methods.md` states the rule this file guards the edges of: one type may declare both a
`static function foo(…)` and an instance `function foo(…)`, and the call syntax picks between them —
`Type.foo()` is the static, `instance.foo()` the instance. With **identical user parameter lists** there
is nothing in the arguments to select on, so the two cannot be one overload set: shv2 gives them two
REGISTRATION NAMES instead, and the discriminator is the call's syntax rather than its arguments.

**The instance member keeps the source spelling; the static takes `<name>#__static`.** That spelling is
what these tests make a design constraint, and it is decided in exactly one place —
`ProgramSignatures.memberRegistrationKey`, which composes "is this name contested?" with the mint
`staticMemberRegistrationName`. Three sites read it (the whole-program sweep's own key, the registration
name a declaration claims and so the emitted symbol, and the callee a `Type.foo(…)` call carries) and all
three must produce the same bytes; a fourth spelling of the composition would be a callee whose
declaration facts read back from a key nothing wrote. The suffix lives in the `__` space no declaration
may enter (E2051), which is what keeps a legal overload set from ever rendering it.

The split is MINTED ON CONTEST ONLY: a static whose name no instance member shares registers under
exactly the bytes it always did, so no emitted symbol in a program without such a pair moves.

**What must still collide is a genuine redefinition, on either side of the split** — and that is what
this file exists for, because the two sides earn two different diagnostics:

- Two INSTANCE members of one name claim the SOURCE name, so the duplicate is reported against a string
  the author can grep for and earns canonical's wording, `Duplicate function 'X'`
  (`specs/duplicate-functions.md`).
- Two STATIC members of one name claim the MINTED name, which no declaration wrote. That earns the
  mangled register's own sentence, naming the spelling and saying where it came from — the discipline
  `ParseStaging.duplicateFunctionMessage` already applies to a contested free function and a contested
  `extension` method, whose names are synthesized for their own reasons.

⚠ **The second sentence was first gated on the CONTEST rather than on the property, and it fired on the
first case below** — telling an author whose statics are fine to *"give the statics distinct parameter
types"*, about `Box.getValue`, which is exactly what they wrote. Every sentence that names a synthesized
spelling now sits inside the "this name is not what a declaration wrote" band, so it cannot reach a name
the author can grep for. These cases are what hold that shut.

## Tests

<!-- test: error.a-duplicate-instance-member-keeps-the-canonical-sentence -->
### Two INSTANCE members claim the source name, so the author sees the name they wrote
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(v Integer) returns Box
		return Box{value: v}
	end 'create'

	static function getValue() returns Integer
		return 9
	end 'getValue'

	function getValue() returns Integer
		return value
	end 'getValue'

	function getValue() returns Integer
		return 8
	end 'getValue'
end 'Box'

function main() returns ExitCode
	let b = Box.create(3)
	return b.getValue() + Box.getValue()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:19:11: Duplicate function 'Box.getValue'
```

<!-- test: error.a-duplicate-static-member-names-the-minted-spelling -->
### Two STATIC members claim the minted name, which the sentence has to explain
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(v Integer) returns Box
		return Box{value: v}
	end 'create'

	static function getValue() returns Integer
		return 9
	end 'getValue'

	static function getValue() returns Integer
		return 8
	end 'getValue'

	function getValue() returns Integer
		return value
	end 'getValue'
end 'Box'

function main() returns ExitCode
	let b = Box.create(3)
	return b.getValue() + Box.getValue()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:15:18: duplicate definition of function 'Box.getValue#__static' — 'Box.getValue' names both a `static` member and an instance member, so the static is registered under a spelling of its own — and more than one `static` declaration of it claims that spelling. Give the statics distinct parameter types, or distinct names
```

<!-- test: error.a-duplicate-static-overload-names-the-suffixed-spelling -->
### The statics are an overload set of their own, so the collision is one suffix further down
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(v Integer) returns Box
		return Box{value: v}
	end 'create'

	static function getValue() returns Integer
		return 9
	end 'getValue'

	static function getValue(bump Integer) returns Integer
		return 20 + bump
	end 'getValue'

	static function getValue(bump Integer) returns Integer
		return 30 + bump
	end 'getValue'

	function getValue() returns Integer
		return value
	end 'getValue'
end 'Box'

function main() returns ExitCode
	let b = Box.create(3)
	return b.getValue()
end 'main'
```
```maxoncstderr
error E3006: <fragment>:19:18: duplicate definition of function 'Box.getValue#__static#Integer' — 'Box.getValue' names both a `static` member and an instance member, so the static is registered under a spelling of its own — and more than one `static` declaration of it claims that spelling. Give the statics distinct parameter types, or distinct names
```

<!-- test: both-members-run-and-their-answers-differ -->
### Both members are reachable, and the values tell which one ran
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var count as Integer

	static function create(count Integer) returns Counter
		return Counter{count: count}
	end 'create'

	static function reset() returns Counter
		return Counter{count: 7}
	end 'reset'

	function reset()
		count = 3
	end 'reset'
end 'Counter'

function main() returns ExitCode
	let c = Counter.reset()
	var c2 = Counter.create(42)
	c2.reset()
	return c.count + c2.count
end 'main'
```
```exitcode
10
```

<!-- test: the-instance-member-may-be-declared-first -->
### The split does not depend on declaration order
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(v Integer) returns Box
		return Box{value: v}
	end 'create'

	function getValue() returns Integer
		return value
	end 'getValue'

	static function getValue() returns Integer
		return 9
	end 'getValue'
end 'Box'

function main() returns ExitCode
	let b = Box.create(42)
	return b.getValue() + Box.getValue()
end 'main'
```
```exitcode
51
```

<!-- test: the-statics-may-themselves-be-an-overload-set -->
### An ordinary overload set on the static side, beside the instance member
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(v Integer) returns Box
		return Box{value: v}
	end 'create'

	static function getValue() returns Integer
		return 9
	end 'getValue'

	static function getValue(bump Integer) returns Integer
		return 20 + bump
	end 'getValue'

	function getValue() returns Integer
		return value
	end 'getValue'
end 'Box'

function main() returns ExitCode
	let b = Box.create(3)
	return b.getValue() + Box.getValue() + Box.getValue(10)
end 'main'
```
```exitcode
42
```
