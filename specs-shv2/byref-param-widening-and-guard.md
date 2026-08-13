---
feature: byref-param-widening-and-guard
keywords: [byref, param, reassign, overload, forwarding, range, typealias, guard, cell]
category: memory-safety
---

# By-Reference Parameters: How Far the Widening Reaches, and What the Entry Guard Reads

## Documentation

A parameter a function REASSIGNS is passed as the address of a cell rather than as a value, and both
ends of the call have to agree about that before either is parsed (`pass-by-reference.md` is the
feature). Deciding *which* functions take a parameter that way is a whole-program closure over
forwarding edges, and the closure runs against a declaration sweep that has resolved nothing: it
matches a call to a declaration by the BARE NAME the source wrote before the `(`.

That match has to be widened — a name may be worn by more than one declaration — but the widening
must not cross a boundary the SOURCE SYNTAX already draws, and there is exactly one such boundary a
token sweep can read: whether a receiver was written in front of the name.

- **`f(…)`** may name a free `f`, or — through implicit `self` — a sibling METHOD `f`. It widens
  over both.
- **`x.f(…)`, `Type.f(…)`** can name a METHOD `f`, or a free `f` reached by its directory route
  (`utils.f(…)`). It can never name a plain root-level free function, which has no qualified
  spelling to be written after a dot.
- **`self.f(…)`, `Self.f(…)`** narrows once more: the receiver's type IS the enclosing type, so the
  call names that one type's `f` and no other's.

Without the second half, any user program declaring a free function whose name a `stdlib` method
also wears — and reassigning one of its parameters — made that method by-reference, and the
by-reference-ness then travelled up every caller of it. Where it reached an OVERLOADED name it was
not a wasted cell but a hard refusal: `Array.swap` inherited a user `swap`, carried it through
`compareAndSwap` and `smallSortRange`, and E2015 refused *"overloading 'Array.sort'"* in a program
that never mentioned sorting.

The narrowing is not total, and the boundary is exactly the receiver a call writes. A user METHOD
whose name a `stdlib` method reaches through a NON-`self` dotted call still shares its node — the
receiver is a value whose type a token sweep cannot read — and is still refused. `Array.swap`'s own
`managed.swap(i, j: j)` is the one such carrier in the sort helpers, which is why a user method named
`swap` is refused while one named `compareAndSwap` is not.

Independently, a by-reference parameter's own ENTRY RANGE GUARD reads one dereference away from the
parameter slot. The slot holds the cell's ADDRESS, so guarding it compares a heap address against
the declared bounds; the value the caller actually passed is what the range describes. A full-range
alias hides this completely — it is not guarded at all — so the symptom is a NARROW alias panicking on
every call while the identical program with a wide one runs.

## Tests

<!-- test: free-function-may-share-a-stdlib-methods-name -->
A free `swap` that reassigns its parameter does not make `stdlib`'s `Array.swap` by-reference, so
the overloaded `Array.sort` above it stays declarable and sorting still works. Exits with the
reassigned tag (99) plus the sorted array's first element (1).
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
	export var tag as Integer

	static function create(t Integer) returns Self
		return Self{tag: t}
	end 'create'
end 'Node'

function swap(n Node) returns Integer
	n = Node.create(99)
	return n.tag
end 'swap'

function main() returns ExitCode
	var owned = Node.create(5)
	let bumped = swap(owned)
	var nums = [3, 1, 2]
	nums.sort()
	return bumped + (try nums.get(0) otherwise 0)
end 'main'
```
```exitcode
100
```

<!-- test: a-user-method-may-share-a-self-called-stdlib-methods-name -->
`stdlib`'s `Array.compareAndSwap` is only ever reached as `self.compareAndSwap(…)`, and a `self.`
receiver is the enclosing type — so a user method wearing that name is a different declaration and
does not make the sort helpers by-reference. Exits with the reassigned tag.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
	export var tag as Integer

	static function create(t Integer) returns Self
		return Self{tag: t}
	end 'create'
end 'Node'

type Holder
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'

	export function compareAndSwap(n Node) returns Integer
		n = Node.create(99)
		return n.tag
	end 'compareAndSwap'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(1)
	var owned = Node.create(5)
	return h.compareAndSwap(owned)
end 'main'
```
```exitcode
99
```

<!-- test: a-bare-call-still-widens-a-sibling-method -->
The other direction is NOT narrowed, and this is what says so. `outer` hands its parameter to
`inner` with a bare call, which implicit `self` resolves to the sibling method — so `outer`'s
parameter is by-reference too and `inner`'s write is visible through it. A rule that let a bare call
reach only free functions would return 7 here.
```maxon
typealias Integer = int(i64.min to i64.max)

type Runner
	export var seed as Integer

	static function create() returns Self
		return Self{seed: 0}
	end 'create'

	function inner(n Integer) returns Integer
		n = 42
		return n
	end 'inner'

	function outer(n Integer) returns Integer
		_ = inner(n)
		return n
	end 'outer'
end 'Runner'

function main() returns ExitCode
	var r = Runner.create()
	var start = 7
	return r.outer(start)
end 'main'
```
```exitcode
42
```

<!-- test: narrow-ranged-byref-param-guards-the-pointee -->
The entry guard of a by-reference parameter checks what the cell HOLDS. Both 5 (in) and 99 (out) sit
well inside `Int`, so nothing panics and the reassigned value comes back.
```maxon
typealias Int = int(0 to 1000000)

function bump(n Int) returns Int
	n = 99
	return n
end 'bump'

function main() returns ExitCode
	var v = 5
	return bump(v)
end 'main'
```
```exitcode
99
```

<!-- test: full-range-byref-param-needs-no-guard -->
The control that isolates the guard from everything else about the ABI: the identical program over a
FULL-range alias emits no entry guard at all, and behaved correctly even while the narrow one did
not.
```maxon
typealias Wide = int(i64.min to i64.max)

function bump(n Wide) returns Wide
	n = 99
	return n
end 'bump'

function main() returns ExitCode
	var v = 5
	return bump(v)
end 'main'
```
```exitcode
99
```

<!-- test: out-of-range-argument-to-a-byref-param-still-panics -->
<!-- targets: x64-windows, x64-linux -->
Reading the pointee is not the same as not reading anything: a runtime-computed 2000 handed to a
`Small` by-reference parameter is still refused, at the parameter's own declaration line.
```maxon
typealias Small = int(0 to 100)

function bump(n Small) returns Small
	n = 7
	return n
end 'bump'

function main() returns ExitCode
	var v = 40
	v = v * 50
	return bump(v)
end 'main'
```
```exitcode
1
```
```stderr
panic at out-of-range-argument-to-a-byref-param-still-panics.test:4: Range check failed: value outside typealias 'Small'
Stack trace:
  in bump
  in main
  in mrt_start
```

<!-- test: narrow-ranged-float-byref-param-guards-the-pointee -->
A ranged FLOAT parameter takes the same door and the same dereference, in f64 — the domain is
decided where the bounds are read, never where the site is recorded.
```maxon
typealias Ratio = float(0.0 to 10.0)

function scale(r Ratio) returns Ratio
	r = r * 2.0
	return r
end 'scale'

function main() returns ExitCode
	var v = 3.0
	let doubled = scale(v)
	if doubled == 6.0 'exact'
		return 6
	end 'exact'
	return 1
end 'main'
```
```exitcode
6
```
