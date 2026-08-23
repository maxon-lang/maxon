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
- **The `gives` RESULT.** The phi ends up owning exactly ONE reference on every edge,
  and which of two ways an arm owes it is decided by whether anything else still owns
  the value after the merge:
  - An owned TEMPORARY — an owned interpolation/heap `String`, a struct box, or a
    managed payload a binding arm moved out (`text(s) gives s`) — is owned by nothing
    else, so ownership TRANSFERS and the value is dropped neither on the arm's edge
    nor in the post-match drain.
  - A value an IMMUTABLE binding declared outside the arm still owns is CO-OWNED
    (⚖ 2026-08-04): the arm's edge takes a SECOND reference and the binding keeps the
    one it already owed, so the source stays readable after the merge and each holder
    releases its own. A **mutable** source still moves — a `var` can be written through
    afterwards, so a second name for its value could watch it change (`moves.md`'s
    boundary).
  - A BORROWED give is promoted so the phi is uniformly owned: a `String` is copied,
    and a non-text aggregate — which has no copy — is increfed, so the merge is CORRECT
    on either arm rather than refused on both (see
    `match-borrowed-aggregate-give-co-owned`, and the ternary's twin at OPEN #14/S5).

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

<!-- test: gives-immutable-binding-is-co-owned -->
⭐⭐ **AN ARM GIVING AN IMMUTABLE BINDING'S OWN VALUE CO-OWNS IT, IT DOES NOT MOVE IT (⚖ 2026-08-04).**
The two constructs share one door (`Parser.settleArmGive`), so this is the ternary case's twin and the
reason the rule is written down once: `let u = t` from an immutable `t` ALIASES at a rebind, and a
`gives` arm reading the same `t` used to POISON it — one rule with two answers, decided by which
construct the read stood in. The phi still owns its value outright; it simply takes a SECOND reference
rather than stealing the binding's, which is what `try … otherwise <binding>` has always done
(`Parser.transferFallbackToPhi`). Both bindings are read after the merge, and each box is released
exactly once — a move here would double-free and an unbalanced incref would leak (exit 101).
```maxon
enum Pick
	first
	second
end 'Pick'

function choose(tag String, p Pick) returns String
	let a = "A {tag} padded out long enough to be a real heap allocation"
	let b = "B {tag} padded out long enough to be a real heap allocation"
	let chosen = match p 'k'
		first gives a
		second gives b
	end 'k'
	return "{chosen} | {a} | {b}"
end 'choose'

function main() returns ExitCode
	print(choose("x", p: Pick.first))
	print("\n")
	print(choose("y", p: Pick.second))
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
A x padded out long enough to be a real heap allocation | A x padded out long enough to be a real heap allocation | B x padded out long enough to be a real heap allocation
B y padded out long enough to be a real heap allocation | A y padded out long enough to be a real heap allocation | B y padded out long enough to be a real heap allocation
```

<!-- test: error.gives-mutable-binding-still-moves -->
The control for the case above, in this construct: a `var` source can be written through after the
merge, so a second name for its value could watch it change and the arm keeps the MOVE it always had
(⚖ 2026-08-04's own rationale, read the other way). Without it the co-owning arm could be widened to
every source and no case in this file would notice.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Pick
	first
	second
end 'Pick'

function build(x Integer) returns String
	return "v{x} padded out long enough to be a real heap allocation"
end 'build'

function main() returns ExitCode
	var t = build(1)
	let p = Pick.first
	let u = match p 'k'
		first gives t
		second gives build(2)
	end 'k'
	print(u)
	print(t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:8: use of moved value 't': its ownership moved to another binding at an earlier bind or assignment
```
