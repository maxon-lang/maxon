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
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
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

### Move Through Redundant Parentheses Drops Once (No Double-Free)

`let u = (t)` — the initializer is a bare local reference wrapped in redundant parentheses.
`parseParenthesizedExpression` returns its inner value UNCHANGED, so `(t)` aliases `t`'s owned box
exactly as a bare `t` would: it MOVES. A move gate that decided "bare local" by counting tokens saw
three tokens (`( t )`) and called this a consume, left `t` unpoisoned, and both bindings decref'd the
one box at scope exit — a double-free (exit 101). The gate now strips redundant parentheses, so `t` is
moved-from and skipped and the box drops exactly once.

<!-- test: paren-move -->
<!-- targets: x64-windows -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var t = build(1)
	let u = (t)
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

### Use After Move Through Parentheses

`let u = (t)` moves `t` through redundant parentheses; the following `print(t)` reads the moved-from
binding and is rejected at the use. The parens do not exempt the source from poisoning — the gate sees
through them to the bare local reference underneath.

<!-- test: paren-use-after-move -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	let t = build(1)
	let u = (t)
	print(t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:11:8: use of moved value 't': its ownership moved to another binding at an earlier bind or assignment
```

### Field Read After Move Is Use-After-Move

`let q = p` moves `p`'s box into `q`; `return p.x` then READS a field out of the moved-from `p`. The
use-after-move guard fires at every binding-use site, not only the bare read — reading a field through
a moved-from base is rejected at the base, before the field load is emitted. (Without the guard this
returned the moved struct's field: a latent use-after-free once the new owner drops first.)

<!-- test: field-read-after-move -->
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
	return p.x
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:9: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

### Field Store After Move Is Use-After-Move (Not a Revive)

`let q = p` moves `p`'s box into `q`; `p.x = 99` then WRITES a field through the moved-from `p`. A
field store on a moved-from binding is a USE, not a revive: `p.x = …` mutates the box `p` no longer
owns (the one `q` holds), so it is rejected at the base. Only a FULL reassignment `p = <expr>` revives
`p`. Without the guard this silently mutated `q`'s aliased box and `return q.x` returned **99** — an
observable wrong answer for a program shv2 must reject.

<!-- test: field-store-after-move -->
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
	p.x = 99
	return q.x
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:2: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

### Method Call After Move Is Use-After-Move

`let q = p` moves `p`'s box into `q`; `p.getX()` then calls an instance method with the moved-from `p`
as receiver. The receiver is a use of `p`, so it is rejected at the base before the call is emitted.

<!-- test: method-call-after-move -->
```maxon
type Point
	export var x as int

	static function create(x int) returns Point
		return Self{x: x}
	end 'create'

	function getX() returns int
		return x
	end 'getX'
end 'Point'

function main() returns ExitCode
	var p = Point.create(5)
	let q = p
	return p.getX()
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:9: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

### Full Reassignment Revives, Then a Field Store Is Legal

`let q = p` moves `p`'s box into `q`; `p = Point.create(3)` is a FULL reassignment that REVIVES `p` —
it owns a fresh box now — so the following `p.x = 5` writes that new box legally, and `return p.x`
reads it back as 5. `q` owns the box moved out of `p`, `p` owns the box from `create(3)`; each drops
exactly once (no leak). This pins that a field store reads the CURRENT moved-from state (post-revive),
not a stale one.

<!-- test: reassign-revives-then-field-store -->
```maxon
type Point
	export var x as int

	static function create(x int) returns Point
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(1)
	let q = p
	p = Point.create(3)
	p.x = 5
	return p.x
end 'main'
```
```exitcode
5
```
```stdout
```

### Field Access and Method Call on a Live Struct (Positive Control)

`p` is never moved, so both the field store `p.x = 0` and the method call `p.getX()` are legal — the
use-after-move guard fires ONLY when the base binding is moved-from. Reads back 0.

<!-- test: field-access-on-live-struct -->
```maxon
type Point
	export var x as int

	static function create(x int) returns Point
		return Self{x: x}
	end 'create'

	function getX() returns int
		return x
	end 'getX'
end 'Point'

function main() returns ExitCode
	var p = Point.create(4)
	p.x = 0
	return p.getX()
end 'main'
```
```exitcode
0
```
```stdout
```
