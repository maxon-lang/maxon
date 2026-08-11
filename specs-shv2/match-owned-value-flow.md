---
feature: match-owned-value-flow
status: experimental
keywords: [match, union, ownership, scrutinee, gives, drop, decref, move]
category: ownership
---

# Match Owned-Value Flow (P1.3)

## Documentation

A `match` handles two owned heap values whose lifetime the arms decide:

- **The SCRUTINEE.** When the scrutinee is an owned heap TEMPORARY — a constructed
  union box, `match U.case(x) …`, bound to no name — it is enrolled as a
  match-scoped owned binding so it drops EXACTLY ONCE on every exit the match has:
  a `return`/`break`/`continue` arm, and a fall-through arm whose value converges at
  the merge. A managed box drops through its `__destruct_<U>` cascade, which
  null-guards any slot a binding arm moved out. An already-bound scrutinee
  (`let m = …; match m`) is dropped by its own binding, unchanged.
- **The `gives` RESULT.** When an expression arm yields an OWNED value — an owned
  interpolation/heap `String`, a struct box, or a managed payload a binding arm
  moved out (`text(s) gives s`) — ownership TRANSFERS to the result phi, which
  becomes the sole owner and drops once at its consumer's scope. The yielded value
  is therefore not dropped on the arm's edge nor in the post-match drain. When the
  arms disagree (one owned give, one borrowed literal) the borrowed give is promoted
  to an owned copy so the phi is uniformly owned and its consumer drops it
  unconditionally. A borrowed **non-text aggregate** give (a struct or boxed-union
  field read) has no such copy — the same boundary that refuses `return <borrowed
  aggregate>` — so a match that merges one into an owned result is refused at parse
  (E2015), exactly as the equivalent ternary is (OPEN #14).

Every case below is leak-free (a leak is exit 101) and crash-free (a double-free is
`0xC0000005`).

## Tests

<!-- test: temp-scalar-union-scrutinee -->
A constructed scalar-payload union used directly as the scrutinee is an owned
temporary; the box drops once on both `return` arms, whichever runs.
```maxon
typealias Integer = int(i64.min to i64.max)

union Num
	zero
	val(n Integer)
end 'Num'

function main() returns ExitCode
	match Num.val(5) 'check'
		zero then return 0
		val(n) then return n
	end 'check'
	return 0
end 'main'
```
```exitcode
5
```

<!-- test: temp-scalar-union-arm-order -->
The same, with the arms in the other order — the box drops once regardless of which
arm the parser sees first (the leak was arm-order-dependent).
```maxon
typealias Integer = int(i64.min to i64.max)

union Num
	zero
	val(n Integer)
end 'Num'

function main() returns ExitCode
	match Num.val(5) 'check'
		val(n) then return n
		zero then return 0
	end 'check'
	return 0
end 'main'
```
```exitcode
5
```

<!-- test: temp-managed-union-discard -->
A constructed String-payload union used directly as the scrutinee, matched with an arm
that DISCARDS the String slot: the box AND its String payload free once through the
cascade on whichever `return` runs.

⚠ The discard is PARTIAL (`text(_, code)`), and it has to be: an all-discard list
(`text(_)`) is `E3081` — the bare case name is the only spelling of "ignore every
payload" (`match-payload-discard-bindings.md`). A bare name would not test this at all:
it carries no binding list, so `declarePayloadBindings`'s discard branch — the code
that leaves a managed payload in the box for the cascade — would never run.
```maxon
typealias Integer = int(i64.min to i64.max)

union Message
	silent
	text(body String, code Integer)
end 'Message'

function main() returns ExitCode
	match Message.text("a long enough heap string to really allocate", code: 5) 'check'
		silent then return 0
		text(_, code) then return code
	end 'check'
	return 0
end 'main'
```
```exitcode
5
```

<!-- test: temp-scrutinee-break-arm -->
A temporary scrutinee inside a loop with a `break` arm: the box drops on the break
edge (down to the loop floor), not only on the `return` arm that the parser drained
into first.

⚠ The break is LABELLED, and it must be. An unlabelled `break` in a match arm exits
the MATCH, not the loop (`specs/match-statements.md`'s "## Break" section), so the
loop-floor drop this case exists to pin is only reached by naming the loop. Written
unlabelled — as it was, against a parser that had not yet implemented match targeting
and so read it as a loop exit — nothing in the body can ever change `result`, and the
program is an INFINITE LOOP by inspection: its expected exit of 7 was only reachable
via the bug. ⚠ The bootstrap is NOT an oracle for the unlabelled spelling and was not
consulted as one: it refuses this program outright with `E3012 unused variable: 'n'`,
a lint shv2 does not have. This case is `specs-shv2`-only; there is no canonical
`specs/match-owned-value-flow.md` for it to have diverged from.
```maxon
typealias Integer = int(i64.min to i64.max)

union Num
	zero
	val(n Integer)
end 'Num'

function main() returns ExitCode
	let result = 7
	while result > 0 'loop'
		match Num.val(1) 'check'
			zero then return 0
			val(n) then break 'loop'
		end 'check'
	end 'loop'
	return result
end 'main'
```
```exitcode
7
```

<!-- test: temp-scrutinee-fall-through -->
Both arms fall through to the merge; the temporary box drops once after the merge,
not per-arm.
```maxon
typealias Integer = int(i64.min to i64.max)

union Num
	zero
	val(n Integer)
end 'Num'

function main() returns ExitCode
	match Num.val(5) 'check'
		zero then print("z")
		val(n) then print("v")
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v
```

<!-- test: gives-owned-interpolation -->
Both `gives` arms yield an owned interpolation. Ownership transfers to the result
phi, which is dropped once at its binding's scope exit — neither arm's temporary is
double-freed by the post-match drain.
```maxon
enum C
	first
	second
end 'C'

function main() returns ExitCode
	let c = C.first
	let out = match c 'check'
		first gives "first arm value {1} padded to heap"
		second gives "second arm value {2} padded to heap"
	end 'check'
	print(out)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first arm value 1 padded to heap
```

<!-- test: gives-disagree-owned-borrowed -->
One arm gives an owned interpolation, the other a borrowed String literal. The
borrowed give is promoted to an owned copy so the phi is uniformly owned and its
consumer drops it unconditionally, whichever arm ran.
```maxon
enum C
	first
	second
end 'C'

function main() returns ExitCode
	let c = C.second
	let out = match c 'check'
		first gives "interpolated arm {1} padded to heap"
		second gives "a plain literal give also long enough"
	end 'check'
	print(out)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a plain literal give also long enough
```

<!-- test: gives-moved-out-managed-binding -->
A `gives` arm yields a managed payload it moved out of the scrutinee (`text(s) gives
s`): the payload is not dropped on the arm edge (it escapes to the phi) but at the
result's own scope exit, and the borrowed literal arm is promoted to match.
```maxon
union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	let m = Message.text("the moved payload string, long enough to be a real heap allocation")
	let out = match m 'check'
		silent gives "a fallback literal also long enough to be a real heap string"
		text(s) gives s
	end 'check'
	print(out)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the moved payload string, long enough to be a real heap allocation
```

<!-- test: gives-temp-managed-scrutinee -->
Both bugs at once: a TEMPORARY managed-union scrutinee whose match expression's arms
give owned values. The box drops once (cascade, null-guarding the moved slot) and the
result phi drops once.
```maxon
union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	let out = match Message.text("temporary scrutinee payload long enough to heap") 'check'
		silent gives "a fallback literal long enough to be a real heap string"
		text(s) gives s
	end 'check'
	print(out)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
temporary scrutinee payload long enough to heap
```

<!-- test: match-borrowed-aggregate-give-co-owned -->
A `match … gives` whose arms merge a BORROWED aggregate give (`e.kind`, a field read of a
borrowed struct parameter) with an OWNED one (`remapKind(e.kind)`, a fresh call result) is the
same merge the equivalent ternary makes, through the same shared code (OPEN #14): the owned
result phi would free the borrowed box while its real owner frees it too, so the borrowed arm is
INCREF'd on its own edge and the phi's drop releases that second reference. Exit `0` is the
assertion — this program leaked (exit 101) before the merge promoted the borrowed give, and it
was refused outright (E2015) between then and S5, on the false premise that shv2 had no incref.
```maxon
typealias Id = int(0 to 1000)

union Kind
	none
	value(inner Id)
end 'Kind'

type Entry
	export var kind as Kind

	static function create(kind Kind) returns Self
		return Self{kind: kind}
	end 'create'
end 'Entry'

function remapKind(k Kind) returns Kind
	return match k 'r'
		none gives Kind.none
		value(inner) gives Kind.value(inner + 1)
	end 'r'
end 'remapKind'

function chooseKind(e Entry, sel Id) returns Kind
	return match sel 'c'
		0 gives e.kind
		default gives remapKind(e.kind)
	end 'c'
end 'chooseKind'

function record(k Kind) returns Id
	return match k 'r'
		none gives 0
		value(inner) gives inner
	end 'r'
end 'record'

function main() returns ExitCode
	let e = Entry.create(Kind.value(3))
	let n = record(chooseKind(e, sel: 0))
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: gives-owned-string-arm-undeclared-call -->
An owned interpolation give in one arm makes the result phi owned, so the other arm is routed
through the borrowed-give promotion. Here that arm is an UNDECLARED call — an UNRESOLVED give that
is neither a String to promote nor an aggregate to refuse. The merge must DEFER it so semantic
analysis reports the real `E3004`, never a parser panic. (Twin of the ternary regression guard:
the shared `promoteBorrowedGive` must not crash on an unresolved give.)
```maxon
function pick(x int, c bool) returns String
	return match c 'm'
		true gives "{x}"
		default gives undefinedThing()
	end 'm'
end 'pick'

function main() returns ExitCode
	let s = pick(5, c: true)
	return 0
end 'main'
```
```maxoncstderr
error E3004: specs/fragments/match-owned-value-flow/gives-owned-string-arm-undeclared-call.test:5:17: call to undefined function 'undefinedThing'
```
