---
feature: conditional-ownership-param
status: experimental
keywords: [ownership, borrow, param, var, reassign, retain, struct, union, conditional, leak, use-after-free]
category: memory
---

# Conditional Ownership of a Borrowed-Aggregate Parameter Var

## Documentation

A struct or union PARAMETER is a BORROWED managed value: the caller owns the box and lends it, so
the callee holds a pointer it must not drop. Binding one to a `var` — `var q = p` — aliases that
same box (`q.x = 100` shows through to the caller). The alias is the point: an aggregate has no
cheap owning copy, so `q` cannot be a fresh box the way a mutable `String` var is.

The hazard is a CONDITIONAL reassignment in a NESTED block:

```maxon
var q = p                 // q aliases the borrowed box p
if c > 0 'b'
    q = Point.create(9)   // q now owns a fresh box
end 'b'
return q.x                // read AFTER the block
```

Were `q` left borrowed at its declaration, the reassignment inside the inner block would be the
first thing to make `q` own a droppable value — and it would enrol `q` as owned at the INNER block's
depth. `q`'s fresh box would then drop at the inner block's `end`, while `q` (declared OUTER) lives
on and is read past the block: a use-after-free.

The fix makes `q` a CO-OWNER of the borrowed box from its DECLARATION, via a refcount retain
(`__mm_retain`), so it is enrolled as owned at ITS OWN scope and the reassignment is a uniform
owned→owned transition — never the wrong-depth enrolment. This mirrors the mutable-`String`
promotion exactly, with an incref in place of a copy (a struct is reference semantics, a String is
value semantics). The retain balances on every path:

- **then-path** — `q = Point.create(9)` decrefs the retained box (balancing the incref) and `q`
  owns the fresh box, dropped at scope exit.
- **else-path** — `q` keeps the retained box; its scope-exit drop decrefs it, so the box's count
  goes retain(+1) … decref(−1) = net 0 and the CALLER still owns it (no double free).

`q` gets a FRESH SSA value distinct from `p` (the retain RETURNS the pointer), so a later re-borrow
of `p` (`let r = p`) cannot poison `q`, and moving `q` (`let r = q`) poisons `q` and not `p`.

These are all USE-AFTER-FREE regressions under poison-on-free (`__mm_free` overwrites a freed box
with `0x3F`): before the fix the RED cases return `0x3F3F3F3F` (1061109567) or fault (`0xC0000005`).
Every expected exit code below is the value the BOOTSTRAP (`maxon-sharp`, the oracle) produces for
the same program.

## Tests

### Conditional Reassign in a Nested Block, Then Read (Struct)

`q` aliases the borrowed struct param, is reassigned an owned box on the taken branch, and read past
the block. Before the fix `q`'s fresh box dropped at the inner `end` and `return q.x` read poison
(1061109567). With the retain-promotion `q` owns from declaration, so the reassignment drops the
retained box and `q` owns `Point.create(9)` to scope exit.

<!-- test: struct-cond-reassign-then -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function pick(p Point, c Integer) returns Integer
	var q = p
	if c > 0 'b'
		q = Point.create(9)
	end 'b'
	return q.x
end 'pick'

function main() returns ExitCode
	let base = Point.create(5)
	return pick(base, c: 1)
end 'main'
```
```exitcode
9
```
```stdout
```

### Conditional Reassign Not Taken, Then Read (Struct, Else Path)

The reassignment's branch is NOT taken, so `q` keeps aliasing the caller's box and reads its value
(5). The retained box's scope-exit decref balances the declaration's incref, leaving the caller the
sole owner — no double free. This is the case a naive drop-at-declaration-scope fix would break.

<!-- test: struct-cond-reassign-else -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function pick(p Point, c Integer) returns Integer
	var q = p
	if c > 0 'b'
		q = Point.create(9)
	end 'b'
	return q.x
end 'pick'

function main() returns ExitCode
	let base = Point.create(5)
	return pick(base, c: 0)
end 'main'
```
```exitcode
5
```
```stdout
```

### Same-Depth Reassign Is Unaffected (Struct)

The reassignment is at the SAME block depth as the declaration, so even the pre-fix code enrolled
`q` at the right scope. It stays correct: `q` owns `Point.create(9)` at function scope and reads 9.

<!-- test: struct-same-depth-reassign -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function pick(p Point) returns Integer
	var q = p
	q = Point.create(9)
	return q.x
end 'pick'

function main() returns ExitCode
	let base = Point.create(5)
	return pick(base)
end 'main'
```
```exitcode
9
```
```stdout
```

### Conditional Reassign in a Nested Block, Then Match (Union)

The union twin of the struct then-path: `q` aliases a borrowed union param, is reassigned
`Num.val(7)` on the taken branch, and matched past the block. Before the fix the boxed union dropped
at the inner `end` and the match read freed memory — a fault (`0xC0000005`). The retain keeps `q`
owning from declaration.

<!-- test: union-cond-reassign-then -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Num
	none
	val(v Integer)
end 'Num'

function pick(u Num, c Integer) returns Integer
	var q = u
	if c > 0 'b'
		q = Num.val(7)
	end 'b'
	return match q 'm'
		none gives 0
		val(v) gives v
	end 'm'
end 'pick'

function main() returns ExitCode
	let base = Num.val(5)
	return pick(base, c: 1)
end 'main'
```
```exitcode
7
```
```stdout
```

### Conditional Reassign Not Taken, Then Match (Union, Else Path)

The union else-path: the reassignment is skipped, `q` matches the caller's `Num.val(5)`, and the
retained box's scope-exit decref balances the incref — the caller stays the sole owner.

<!-- test: union-cond-reassign-else -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Num
	none
	val(v Integer)
end 'Num'

function pick(u Num, c Integer) returns Integer
	var q = u
	if c > 0 'b'
		q = Num.val(7)
	end 'b'
	return match q 'm'
		none gives 0
		val(v) gives v
	end 'm'
end 'pick'

function main() returns ExitCode
	let base = Num.val(5)
	return pick(base, c: 0)
end 'main'
```
```exitcode
5
```
```stdout
```

### Reassign Then Return Out of the Block (Struct)

The reassignment and the read are BOTH inside the block, and the block leaves via `return` — so the
value is read before the scope-exit drop and even the pre-fix code returns the right value. With the
retain the ownership is uniform and the drops still balance on both the `return`-out and the
fall-through path.

<!-- test: struct-return-out-of-block -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function pick(p Point, c Integer) returns Integer
	var q = p
	if c > 0 'b'
		q = Point.create(9)
		return q.x
	end 'b'
	return q.x
end 'pick'

function main() returns ExitCode
	let base = Point.create(5)
	return pick(base, c: 1)
end 'main'
```
```exitcode
9
```
```stdout
```

### Reassign Inside a Loop, Break, Then Read After the Loop (Struct)

`q` is reassigned an owned box inside the loop body and the loop `break`s; the final value is read
after the loop. Before the fix the fresh box dropped at the loop-body `end` and the post-loop read
hit poison. The retain-promotion makes `q` a loop-carried owned var, reassigned owned→owned.

<!-- test: struct-break-after-reassign -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function pick(p Point, n Integer) returns Integer
	var q = p
	var i = 0
	while i < n 'l'
		q = Point.create(9)
		i = i + 1
		break
	end 'l'
	return q.x
end 'pick'

function main() returns ExitCode
	let base = Point.create(5)
	return pick(base, n: 1)
end 'main'
```
```exitcode
9
```
```stdout
```

### Reassign in a Nested If Then Continue, Read After the Loop (Struct)

`q` is reassigned inside a nested `if` in the loop body, which then `continue`s; the value is read
after the loop. Before the fix the box dropped at the inner block's `end` and the post-loop read hit
poison. The retain keeps `q` owned across every iteration.

<!-- test: struct-continue-after-reassign -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function pick(p Point, n Integer) returns Integer
	var q = p
	var i = 0
	while i < n 'l'
		if i > 0 'b'
			q = Point.create(9)
			i = i + 1
			continue
		end 'b'
		i = i + 1
	end 'l'
	return q.x
end 'pick'

function main() returns ExitCode
	let base = Point.create(5)
	return pick(base, n: 2)
end 'main'
```
```exitcode
9
```
```stdout
```

### Loop-Carried Reassign, Read After the Loop (Struct)

`q` is reassigned every iteration; the previous iteration's box (and, on the first, the retained
alias) is dropped exactly once at the reassignment, and the final value survives to be read after
the loop. No leak (every intermediate freed once) and no use-after-free (the final box lives to the
read). Ran three iterations, so the surviving value is `Point.create(2)`.

<!-- test: struct-loop-carried-reassign -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function pick(p Point, n Integer) returns Integer
	var q = p
	var i = 0
	while i < n 'l'
		q = Point.create(i)
		i = i + 1
	end 'l'
	return q.x
end 'pick'

function main() returns ExitCode
	let base = Point.create(5)
	return pick(base, n: 3)
end 'main'
```
```exitcode
2
```
```stdout
```

### Borrowed Var Never Reassigned Is Leak-Free (Struct)

`q` aliases the borrowed param and is never reassigned, just read. The retain at declaration and the
decref at scope exit cancel, so the box's count is unchanged and the caller stays the sole owner —
no leak, no double free. (The bootstrap lints a never-reassigned `var` as E3077; shv2 permits it,
and the value 5 is the else-path's oracle-verified twin, where the reassignment is likewise skipped.)

<!-- test: struct-borrowed-var-never-reassigned -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function pick(p Point) returns Integer
	var q = p
	return q.x
end 'pick'

function main() returns ExitCode
	let base = Point.create(5)
	return pick(base)
end 'main'
```
```exitcode
5
```
```stdout
```

### Re-Borrowing the Param Does Not Poison the Retained Var (Struct)

`q` retains the borrowed param, then `let r = p` re-borrows the SAME param. Because `q` holds a
FRESH SSA value (the retain's return), the re-borrow of `p` cannot match `q` in the move-source scan,
so reading `q` afterward is legal. `q.x` is 9 on the taken branch, `r.x` is the caller's 5 (a plain
borrow the reassignment never touches): 14.

<!-- test: struct-reborrow-param-not-poisoned -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function pick(p Point, c Integer) returns Integer
	var q = p
	let r = p
	if c > 0 'b'
		q = Point.create(9)
	end 'b'
	return q.x + r.x
end 'pick'

function main() returns ExitCode
	let base = Point.create(5)
	return pick(base, c: 1)
end 'main'
```
```exitcode
14
```
```stdout
```

### Moving the Retained Var Hands Its Box to the New Owner (Struct)

The retain makes `q` a genuine OWNER, so `let r = q` MOVES `q` into `r` (shv2 is move-only for
locals, exactly as `let u = a` moves an owned `String`). `r` reads the caller's value (5) and drops
the co-owned box once at scope exit — leak-free — while the caller stays an owner.

<!-- test: struct-move-retained-var -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function pick(p Point) returns Integer
	var q = p
	let r = q
	return r.x
end 'pick'

function main() returns ExitCode
	let base = Point.create(5)
	return pick(base)
end 'main'
```
```exitcode
5
```
```stdout
```

### Using the Retained Var After Moving It Is Use-After-Move (Struct)

Because the retain made `q` an owner, `let r = q` moves it — so reading `q` afterward is a genuine
use-after-move (E3102), poisoning `q` and not `p`. Before the fix `q` was a borrowed alias and
`let r = q` re-borrowed it, so `return q.x` returned 5; the retain makes the move real. (The
`<fragment>` line/column are into the compiled fragment, whose one-line header shifts the source
down by one.)

<!-- test: error.struct-use-after-move-retained-var -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function pick(p Point) returns Integer
	var q = p
	let r = q
	return q.x
end 'pick'

function main() returns ExitCode
	let base = Point.create(5)
	return pick(base)
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:9: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```
