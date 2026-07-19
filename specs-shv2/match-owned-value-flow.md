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
  unconditionally.

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
A constructed String-payload union used directly as the scrutinee, matched with a
discard arm that binds nothing: the box AND its String payload free once through the
cascade on whichever `return` runs.
```maxon
union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	match Message.text("a long enough heap string to really allocate") 'check'
		silent then return 0
		text(_) then return 5
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
```maxon
typealias Integer = int(i64.min to i64.max)

union Num
	zero
	val(n Integer)
end 'Num'

function main() returns ExitCode
	var result = 7
	while result > 0 'loop'
		match Num.val(1) 'check'
			zero then return 0
			val(n) then break
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
