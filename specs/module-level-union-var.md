---
feature: module-level-union-var
status: stable
keywords: [module, var, union, global, refcount, module-init, heap]
category: declaration
---
# Module-Level Union Variables

## Documentation

A union with any payload-bearing case is a HEAP BOX: every value of it — including
its payload-free cases — is an `mm_alloc`'d record holding `[tag, payload...]`.
That is the same representation a struct, a `String` or an `Array` has, and a
module-level `var` of any of those already works: the initializer runs in
`__module_init` before `main`, the `.data` slot OWNS the occupant, and the
process-exit global cleanup releases it.

A boxed union was the one managed value that did not get that treatment. Its case
reference was folded as if it were a scalar enum's ordinal, so the `.data` slot was
initialized to the raw ordinal while every later assignment stored a pointer — two
irreconcilable representations for one slot. The mismatch was silent and severe:

- `var hold = Hold.unowned` + `hold = Hold.owned(5)` **compiled clean and returned 0
  instead of 5**. The store lowered to `std.global_store_i64 @hold %1`, where `%1`
  was `mm_alloc`'s SIZE argument — a Std-tier id that collided with the stale
  Maxon-tier id carried by the box's handle, not the box.
- A second function storing a different case had no such collision to hide behind,
  and the stale id reached the register allocator as
  `E9001: value %N has no register and no stack home`.
- Reading the global was broken the same way: the load produced a bare integer, so
  the tag read passed the POINTER through as the tag instead of loading `[ptr + 0]`.

A payload-FREE union (and a plain `enum`) is representationally a bare discriminant,
not a box, so its case reference stays a compile-time constant and its global stays a
`.data` ordinal. The split is on the representation, never on the syntax: `Hold.unowned`
and `Hold.owned(5)` are both boxes because `Hold` has a payload case.

The union type name has to reach lowering on the OP, not just in the module's
`GlobalVarInfos`, because a global load/store inside a generic method is rebuilt from
scratch when that method is monomorphized. A specialization that reconstructed the op
without the type name lowered the same slot as a bare integer *inside the clone only* —
the identical wrong answer, restricted to one specialization and therefore invisible to
a corpus with no generic reader of a union global.

## Tests

<!-- test: module-union-store-then-match -->
Assigning a payload-carrying case to a module-level union var, then matching it.
This is the silent wrong answer: it returned 0 (the `.data` ordinal) instead of 5.
```maxon
typealias Small = int(0 to 100)

union Hold
	unowned
	owned(v Small)
end 'Hold'

var hold = Hold.unowned

function seed()
	hold = Hold.owned(5)
end 'seed'

function main() returns ExitCode
	seed()
	return match hold 'm'
		unowned gives 0
		owned(v) gives v
	end 'm'
end 'main'
```
```exitcode
5
```

<!-- test: module-union-two-storing-functions -->
Two functions each storing a different case. The second store's stale value id had
no definition in its own function, which surfaced as `E9001` out of the register
allocator rather than as a wrong answer.
```maxon
typealias Small = int(0 to 100)

union Hold
	unowned
	owned(v Small)
end 'Hold'

var hold = Hold.unowned

function seed()
	hold = Hold.owned(5)
end 'seed'

function clear()
	hold = Hold.unowned
end 'clear'

function main() returns ExitCode
	clear()
	seed()
	return match hold 'm'
		unowned gives 0
		owned(v) gives v
	end 'm'
end 'main'
```
```exitcode
5
```

<!-- test: module-union-read-initializer -->
The declared initializer must survive to `main` unread-and-unwritten. The slot holds
a box built by `__module_init`, so the tag read finds the case that was declared.
```maxon
typealias Small = int(0 to 100)

union Hold
	unowned
	owned(v Small)
end 'Hold'

var hold = Hold.owned(9)

function main() returns ExitCode
	return match hold 'm'
		unowned gives 0
		owned(v) gives v
	end 'm'
end 'main'
```
```exitcode
9
```

<!-- test: module-union-payload-free-initializer -->
A payload-free case as the initializer is still a box, because the union has a
payload case elsewhere. Matching it must read the tag out of the record.
```maxon
typealias Small = int(0 to 100)

union Hold
	unowned
	owned(v Small)
end 'Hold'

var hold = Hold.unowned

function main() returns ExitCode
	return match hold 'm'
		unowned gives 4
		owned(v) gives v
	end 'm'
end 'main'
```
```exitcode
4
```

<!-- test: module-union-reassign-releases-old -->
Reassigning the global releases the previous occupant, so a loop of stores does not
leak. The suite fails the run on a leaked allocation, which is the assertion here.
```maxon
typealias Small = int(0 to 100)

union Hold
	unowned
	owned(v Small)
end 'Hold'

var hold = Hold.unowned

function main() returns ExitCode
	var i = 0
	while i < 10 'fill'
		hold = Hold.owned(i as Small)
		i = i + 1
	end 'fill'

	return match hold 'm'
		unowned gives 0
		owned(v) gives v
	end 'm'
end 'main'
```
```exitcode
9
```

<!-- test: module-union-payload-free-union-stays-scalar -->
A union whose cases ALL lack payloads is a bare discriminant, not a box, so its
global keeps the compile-time-constant `.data` ordinal it always had.
```maxon
union Mode
	off
	on
end 'Mode'

var mode = Mode.off

function seed()
	mode = Mode.on
end 'seed'

function main() returns ExitCode
	seed()
	return match mode 'm'
		off gives 0
		on gives 6
	end 'm'
end 'main'
```
```exitcode
6
```

<!-- test: module-enum-stays-scalar -->
A plain `enum` global is unaffected: it folds to its ordinal exactly as before.
```maxon
enum Color
	red
	green
end 'Color'

var c = Color.red

function seed()
	c = Color.green
end 'seed'

function main() returns ExitCode
	seed()
	return match c 'm'
		red gives 0
		green gives 7
	end 'm'
end 'main'
```
```exitcode
7
```

<!-- test: module-union-in-monomorphized-method -->
A generic type's methods store to and read from the union global. Monomorphizing them
rebuilds every op, so the slot's type name must travel with the rebuilt load and store.
Without it the specialization alone lowered the global as a bare integer and the tag
read dereferenced it: `panic: nil pointer or invalid memory access in Cell.readBack`.
```maxon
typealias Small = int(0 to 100)

union Hold
	unowned
	owned(v Small)
end 'Hold'

var hold = Hold.unowned

type Cell uses T
	export var item as T

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function stash(v Small)
		hold = Hold.owned(v)
	end 'stash'

	export function readBack() returns Small
		return match hold 'r'
			unowned gives 0
			owned(v) gives v
		end 'r'
	end 'readBack'
end 'Cell'

typealias SmallCell = Cell with Small

function main() returns ExitCode
	let c = SmallCell.create(1)
	c.stash(5)
	return c.readBack()
end 'main'
```
```exitcode
5
```
