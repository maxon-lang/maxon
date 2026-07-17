---
feature: moves
status: experimental
keywords: [ownership, move, use-after-move, single-owner, drop, reassign, string, struct]
category: memory
---

# Static Single-Owner Moves

## Documentation

Under shv2's static single-owner model an owned heap value (an owned `String`, a struct box) has
exactly ONE owner and is dropped exactly once, at that owner's scope exit. Binding or assigning a
value from a bare reference to an owned binding therefore MOVES ownership rather than aliasing it:

```maxon
let t = build(1)   // t owns the box
let u = t          // ownership MOVES to u; t is now moved-from
print(u)           // u still usable
// print(t)        // ERROR: use of moved value 't'
```

The source is left MOVED-FROM: reading it is a compile error (use-after-move), and its scope-exit
drop is SKIPPED — the value drops once, through its new owner. A fresh owned temporary (`build()`,
`"{x}"`) is owned by no binding, so binding it is a CONSUME, not a move — nothing is poisoned.

A WRITE to a moved-from `var` REVIVES it: the binding owns the new value and is usable again. So a
value moved on some-but-not-all paths of an `if`/`while` becomes a (conservative) use-after-move past
the merge — the flag is set unconditionally where the move is written, with no dataflow join.

## Tests

### Legal Move From a Var, Source Unused

`t` (a `var`) is bound then moved into `u`; `t` is never read again. The value drops exactly once —
through `u` at scope exit, with `t`'s drop skipped — so no leak and no double-free.

<!-- test: legal-move -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var t = build(1)
	let u = t
	print(u)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v1
```

### Use After Move-On-Bind

`let u = t` moves `t`; the following `print(t)` reads the moved-from binding and is rejected at the
use.

<!-- test: use-after-move-on-bind -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	let t = build(1)
	let u = t
	print(t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:11:8: use of moved value 't': its ownership moved to another binding at an earlier bind or assignment
```

### Owned Var Assigned From Owned Var (#41)

`s = t` overwrites `s`'s box (dropped at the assignment) and MOVES `t`'s box into `s`. Before Wave C
both bindings ended up owning `t`'s box and it was decref'd twice at scope exit — a double-free the
leak gate reported as exit 101. Now `t` is moved-from and skipped, so each box drops exactly once.

<!-- test: assign-owned-from-owned -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = build(1)
	var t = build(2)
	s = t
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v2
```

### Use After Move-On-Assign

`s = t` moves `t`; the following `print(t)` reads the moved-from binding and is rejected at the use.

<!-- test: use-after-move-on-assign -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = build(1)
	var t = build(2)
	s = t
	print(t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:12:8: use of moved value 't': its ownership moved to another binding at an earlier bind or assignment
```

### Reassign Revives a Moved-From Var

`a` is moved into `b`, then `a` is reassigned a fresh value. The write REVIVES `a` — it owns the new
box and is usable again — while `b` owns the box moved out of `a`. Both print, and the old box is NOT
double-dropped by the reassignment (it belongs to `b` now, not `a`).

<!-- test: reassign-revives -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var a = build(10)
	let b = a
	a = build(20)
	print(b)
	print(a)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v10v20
```

### Second Alias Of a Moved Value Is Use-After-Move

`let b = a` moves `a`; `let c = a` then reads the moved-from `a` and is rejected at the use.

<!-- test: multiple-alias -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	let a = build(1)
	let b = a
	let c = a
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:11:10: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

### Conditional Move Poisons Conservatively

`a` is moved inside the `if` body. `movedFrom` is a flag set unconditionally where the move is
written, so `a` is treated as moved on EVERY path past the merge — reading it after the `if` is a
use-after-move even though the non-taken branch never moved it. This over-rejection is the intended
sound minimum for this rung (a moved-on-all-paths dataflow join is a later rung).

<!-- test: conditional-poisoning -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	let a = build(1)
	let flag = 1
	if flag > 0 'b'
		let u = a
		print(u)
	end 'b'
	print(a)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:8: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

### Struct Move Drops Once (No Double-Free)

A struct box is owned by its type. `let q = p` moves `p`'s box into `q`; `p` is left moved-from and
its drop is skipped, so the box drops exactly once through `q`. Without the move both would decref it
and the leak gate would fire (exit 101).

<!-- test: struct-move-drop-skip -->
```maxon
type Point
	export var x as int

	static function create(x int) returns Point
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(7)
	let q = p
	return q.x
end 'main'
```
```exitcode
7
```
```stdout
```
