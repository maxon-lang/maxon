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
typealias Integer = int(i64.min to i64.max)

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
typealias Integer = int(i64.min to i64.max)

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
typealias Integer = int(i64.min to i64.max)

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
```maxon
typealias Integer = int(i64.min to i64.max)

union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	let m = Message.text("discard me, i am long enough to be a heap string")
	let code = match m 'check'
		silent gives 0
		text(_) gives 4
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
typealias Integer = int(i64.min to i64.max)

union Pair
	none
	both(a String, b String)
end 'Pair'

function main() returns ExitCode
	let p = Pair.both("the first heap string is long", "the second heap string is long too")
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
typealias Integer = int(i64.min to i64.max)

union Pair
	none
	both(a String, b String)
end 'Pair'

function main() returns ExitCode
	let p = Pair.both("bound first string long enough to heap", "discarded second string also heap")
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
typealias Integer = int(i64.min to i64.max)

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
typealias Integer = int(i64.min to i64.max)

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
