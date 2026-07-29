---
feature: union-managed-payload
status: experimental
keywords: [union, enum, payload, String, struct, ownership, move, drop, decref]
category: ownership
---

# Union Managed Payloads (P1.3 slice 2)

## Documentation

A `union`/`enum` case may carry a **managed** associated value — a `String` or a
`struct`. The union stays a heap box (`8 + maxArity*8`, i64 tag at offset 0,
payload slot `i` at `8 + i*8`); a managed payload slot holds an owned heap
pointer.

Ownership is static single-owner, exactly as for a `String` or a struct binding:

- **Construct is a MOVE.** `U.case(s)` transfers ownership of `s` into the box's
  payload slot — no incref, no copy. The source binding is moved-from (a later
  read is `E3102`). A borrowed String literal payload is promoted to an owned
  heap copy first, so the box always owns a droppable payload.
- **A match binding is a MOVE-OUT.** `match u { case(x) then … }` loads the
  managed field into `x` (which becomes an owned binding, dropped at its own
  scope exit) and clears the box slot. After the match `u` is moved-from (a later
  read is `E3102`). A discard `_`, an unbound tag-only arm, and a payload-free /
  scalar arm bind nothing and leave the box owned — `u` is dropped at scope exit.
- **Drop is a tag-conditional STATIC cascade.** When an owned managed-payload
  union is dropped, its `__destruct_<U>` loads the tag, and for the live case
  drops each still-present managed field through its own type's destructor (a
  `String` field via `__str_decref`, a `struct` field via `__mm_decref`), then
  frees the box. A moved-out slot is null and is skipped, so a payload is freed
  exactly once whether it was moved out, discarded, or left in place.

Passing a managed-payload union across a call boundary (as a parameter or a
return value), and binding a managed payload out of a *borrowed* union, are the
cross-call ownership ruling deferred to **P1.4**.

## Tests

<!-- test: struct-payload-drop-leak-free -->
An owned union with a struct payload, dropped at scope exit without being matched,
frees its struct payload through the cascade — no leak (a leak is exit 101).
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function main() returns ExitCode
	let s = Shape.solid(Body.create(5))
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: struct-payload-match-consume -->
Matching an owned struct-payload union binds the struct, reads its field, and
consumes the union — the struct is freed once (via the binding), the box once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function main() returns ExitCode
	let s = Shape.solid(Body.create(5))
	match s 'check'
		empty then return 0
		solid(b) then return b.mass
	end 'check'
end 'main'
```
```exitcode
5
```

<!-- test: string-payload-drop-leak-free -->
An owned union with a String payload, dropped at scope exit without being matched,
frees its String through `__str_decref` — no leak.
```maxon

union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	let m = Message.text("hello world this is a long enough string to be heap")
	return 9
end 'main'
```
```exitcode
9
```

<!-- test: string-payload-match-consume -->
Matching an owned String-payload union binds the String, prints it, and consumes
the union. The String is freed once (via the binding), the box once.
```maxon

union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	let m = Message.text("hi")
	match m 'check'
		silent then return 0
		text(s) then print(s)
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi
```

<!-- test: string-payload-interpolated -->
An interpolated String moved into a union payload, then matched back out and
printed. The interpolation temporary is owned; the move transfers it into the box.
```maxon

union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	let m = Message.text("v{41}")
	match m 'check'
		silent then return 0
		text(s) then print(s)
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v41
```

<!-- test: match-borrow-no-managed-binding -->
A match that binds no managed payload (a tag-only arm) borrows: the union is not
consumed and is dropped at scope exit, freeing its struct payload once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function main() returns ExitCode
	let s = Shape.solid(Body.create(3))
	let code = match s 'check'
		empty gives 1
		solid gives 2
	end 'check'
	return code
end 'main'
```
```exitcode
2
```

<!-- test: discard-managed-field -->
A `_` discard of a managed field binds nothing and does not consume: the union is
dropped at scope exit and the cascade frees the discarded String.

⚠ The discard is PARTIAL (`text(_, tag)`), and it has to be: an all-discard list
(`text(_)`) is `E3081` — the bare case name is the only spelling of "ignore every
payload" (`match-payload-discard-bindings.md`), and the bare form is already the case
directly above. It would also not test THIS: with no binding list there is no discard
for `declarePayloadBindings` to skip, so the slot that stays in the box for the cascade
would never be exercised.
```maxon
typealias Integer = int(i64.min to i64.max)

union Message
	silent
	text(body String, code Integer)
end 'Message'

function main() returns ExitCode
	let m = Message.text("discard me, i am long enough to be a heap string", code: 4)
	let code = match m 'check'
		silent gives 0
		text(_, tag) gives tag
	end 'check'
	return code
end 'main'
```
```exitcode
4
```

<!-- test: two-managed-fields-drop -->
A case with two String fields, dropped at scope exit, frees both.
```maxon

union Pair
	none
	both(a String, b String)
end 'Pair'

function main() returns ExitCode
	let p = Pair.both("the first heap string is long", b: "the second heap string is long too")
	return 6
end 'main'
```
```exitcode
6
```

<!-- test: two-managed-fields-bind-one-discard-one -->
A two-String case binds one field and discards the other. The bound one is freed
via its binding; the discarded one is freed by the cascade at scope exit.
```maxon

union Pair
	none
	both(a String, b String)
end 'Pair'

function main() returns ExitCode
	let p = Pair.both("bound first string long enough to heap", b: "discarded second string also heap")
	match p 'check'
		none then return 0
		both(a, _) then print(a)
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bound first string long enough to heap
```

<!-- test: two-binding-arms-fall-through -->
Two arms that each bind a managed payload AND fall through: each arm's binding is dropped on ITS OWN
exit edge, not accumulated for the continuation (where the other arm's value would be garbage). Both
paths are leak-free and crash-free.
```maxon

union U
	a(x String)
	b(y String)
end 'U'

function main() returns ExitCode
	let u = U.b("the taken arm b string, long enough to be a real heap allocation")
	match u 'check'
		a(s) then print(s)
		b(t) then print(t)
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the taken arm b string, long enough to be a real heap allocation
```

<!-- test: var-reassign-after-partial-move -->
A `var` union consumed by a binding-match is `partiallyMoved` (a re-read is E3102), but a REASSIGNMENT
revives it: the fresh value has no moved-out slots, so a later match is legal again. The old box (with
its nulled payload slot) is dropped at the reassignment; the new one at scope exit — both leak-free.
```maxon

union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	var m = Message.text("reassign first payload string long enough to be a real heap allocation")
	match m 'check'
		silent then return 0
		text(s) then print(s)
	end 'check'
	m = Message.text("reassign second payload string long enough to be a real heap allocation")
	match m 'again'
		silent then return 0
		text(t) then print(t)
	end 'again'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
reassign first payload string long enough to be a real heap allocationreassign second payload string long enough to be a real heap allocation
```

<!-- test: error.construct-moves-string-source -->
Moving a String binding into a union payload poisons it; a later read is E3102.
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	let msg = build(1)
	let m = Message.text(msg)
	print(msg)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:8: use of moved value 'msg': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.construct-moves-struct-source -->
Moving a struct binding into a union payload poisons it; a later read is E3102.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function main() returns ExitCode
	let b = Body.create(5)
	let s = Shape.solid(b)
	return b.mass
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:9: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.match-consume-then-use -->
A binding match consumes the union; a later read of the scrutinee is E3102.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function main() returns ExitCode
	let s = Shape.solid(Body.create(5))
	let first = match s 'check'
		empty gives 0
		solid(b) gives b.mass
	end 'check'
	let second = match s 'again'
		empty gives 0
		solid(b) gives b.mass
	end 'again'
	return first + second
end 'main'
```
```maxoncstderr
error E3102: <fragment>:23:21: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: union-struct-payload-ranged-alias -->
The `create` parameter is a RANGED-INT-ALIAS, which adds a name to this file's
interner and shifts its ids relative to the signatures interner's. The union's
`BoxA` payload type is minted in the signatures interner; classifying it against
the file interner without re-interning let the shift misread it as `int` — a
wrong `E3005` reject at construct, and (once the construct is allowed) a
misrouted scope-exit drop that leaks the payload. The payload type is now
ADOPTED into the file interner before it is classified (the `fieldTypeOf`/
`adoptType` door the struct side already used), and the drop callee is chosen
inside `ProgramSignatures` over its own interner, so id and interner always
agree (OPEN #52).
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var label as String

	static function create(x Integer) returns Self
		return Self{label: "v{x}"}
	end 'create'
end 'BoxA'

union UHold
	holds(inner BoxA)
end 'UHold'

function main() returns ExitCode
	let u = UHold.holds(BoxA.create(1))
	return 4
end 'main'
```
```exitcode
4
```

<!-- test: union-struct-payload-ranged-alias-match -->
The match-bind path shares the construct path's interner mismatch: with the
ranged-int alias present, the bound payload's `BoxA` type (a signatures id) was
classified against the file interner, so the managed payload could be misread as
a scalar and never moved out — a leak. Binding the payload now adopts its type
first, so the move-out and the scope-exit drop agree on what the payload is
(OPEN #52).
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var label as String

	static function create(x Integer) returns Self
		return Self{label: "v{x}"}
	end 'create'
end 'BoxA'

union UHold
	holds(inner BoxA)
end 'UHold'

function main() returns ExitCode
	let u = UHold.holds(BoxA.create(1))
	match u 'm'
		holds(inner) then return 6
	end 'm'
end 'main'
```
```exitcode
6
```

<!-- test: labeled-payload-args-two-scalar-fields -->
A two-field case constructed with a labelled second argument
(`pair(7, column: 13)`) follows the call-argument rule — the first argument is
positional, the second is named — and slots each value against the payload field
its label names. So `line` carries 7 and `column` carries 13, and the match sums
them to 20. The construct is layout-parallel, not source-parallel: the label is
the destination, exactly as a struct literal's field name is.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(7, column: 13)
	match e 'k'
		none then return 0
		pair(line, column) then return (line + column)
	end 'k'
end 'main'
```
```exitcode
20
```

<!-- test: error.payload-second-arg-positional -->
The payload argument list follows the call-argument rule: the second and later
arguments must be named. A bare second argument (`pair(7, 13)`) is rejected — the
same `consumeArgLabel` rule a call obeys.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(7, 13)
	return 0
end 'main'
```
```maxoncstderr
error E2053: <fragment>:10:25: the second and later arguments must be named ('name: value')
```

<!-- test: error.payload-first-arg-named -->
The FIRST payload argument must be positional. A labelled first argument
(`pair(line: 7, column: 13)`) is rejected — a case's payload is ordered by its
declaration, so the first slot needs no name to find its place.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(line: 7, column: 13)
	return 0
end 'main'
```
```maxoncstderr
error E2052: <fragment>:10:22: the first argument cannot be named; only the second and later arguments take 'name:' labels
```

<!-- test: error.payload-unknown-label -->
A named payload argument whose label names no payload field of the case
(`pair(7, bogus: 9)`) is rejected as an unknown field.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(7, bogus: 9)
	return 0
end 'main'
```
```maxoncstderr
error E3018: <fragment>:10:25: type 'LexErr' has no field named 'bogus'
```

<!-- test: error.payload-duplicate-label -->
Filling the same payload slot twice — here `column` positionally-then-by-name is
avoided, so a genuine repeat (`pair(7, column: 13, column: 14)`) is rejected as a
duplicate.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(7, column: 13, column: 14)
	return 0
end 'main'
```
```maxoncstderr
error E3018: <fragment>:10:37: field 'column' of 'LexErr' is initialized twice by this literal
```

<!-- test: error.payload-missing-arg -->
Every declared payload slot must be given a value; a union payload has no
defaults. Omitting `column` (`pair(7)`) is rejected as an uninitialized field.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(7)
	return 0
end 'main'
```
```maxoncstderr
error E3086: <fragment>:10:17: field 'column' of 'LexErr' is not initialized by this literal, and it has no default value
```

<!-- test: error.nested-union-payload -->
A union PAYLOAD that is itself a payload-bearing (boxed) union is refused. A boxed
union is now a managed field kind (a boxed-union STRUCT FIELD is constructible — see
`struct-managed-field.md`), but storing one in a union PAYLOAD slot needs the slot's
width derived the field way rather than from `payloadStorageOf` (which for a boxed
union is the un-lowerable `named` type). A clean reject at the construct site, not a
crash, until that follow-up slice.
```maxon
typealias N = int(0 to i64.max)

union Inner
	a(x N)
end 'Inner'

union Wrap
	wrap(inner Inner)
end 'Wrap'

function main() returns ExitCode
	let w = Wrap.wrap(Inner.a(5))
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:15: Unsupported: a payload field 'inner' of type 'union Inner' on `union Wrap` — a nested payload-bearing union payload needs its own destructor cascade, which arrives at a later rung (String and struct payloads are supported)
```
