---
feature: managed-field-store-order
status: stable
keywords: [struct, field, managed, refcount, store, order, alias, self-assign, use-after-free]
category: memory
---
# Managed Field Store Order — Acquire Before Release

## Documentation

A store into a managed struct field is three operations, not one: the field
acquires a reference to the value being stored, the field releases the value it
displaces, and the pointer is written. **The acquire must be emitted BEFORE the
release.**

The order is not a style choice. A struct in shv2 is a pointer to a shared heap
box, so two names can denote one record and the two operands of a single store
can turn out to be the same record at run time. When they are, release-first
takes the refcount to zero and frees, and the acquire then touches freed memory.
Acquire-first makes that same store a net no-op — the refcount goes 1 → 2 → 1 —
which is what a store of a value onto itself must be.

Both halves of "acquire" are affected, because both read the source record:

- a borrowed struct or array is CO-OWNED, so the acquire is an incref of a
  pointer that release-first has already freed;
- a borrowed `String` is PROMOTED to an owned copy, so the acquire *reads the
  bytes* of a record release-first has already freed.

No static check substitutes for the order. The lexical no-op `self.f = self.f`
is refused as E3067, and that refusal is precisely what hid this: it masks the
spelling while leaving the aliasing routes below untouched. Those routes are
genuine stores whose operands merely coincide at run time, which nothing
whole-program can decide in general. The store protocol itself has to be safe.

Every other durable-storage write in the compiler already states this rule in
its own header — the cell rebind, the module-global store and the local
binding rebind all settle the new value before releasing the old. The field
store was the one door that did not, and these cases exist so it cannot drift
back.

⛔ **Every case below pins `exitcode 0`, and that pin is what catches the OPPOSITE
error.** A store that acquires without ever releasing leaks, the program exits
101, and its `stdout` is still exactly what a correct run prints — so stdout
alone cannot tell the two apart. An UNPINNED exit code is not "expect 0": the
harness states outright that it "says nothing about it" and passes without
looking (`SpecTestRunner.checkRunExitCode`, whose `unpinned` arm is
`TestOutcome.pass`), and the suite-level `memoryLeak` flag reads the RUNNER
process's own exit code, not any test program's. **Delete the `exitcode` blocks
and these three cases go on passing while the release is gone.**

## Tests

<!-- test: field-store-aliasing-through-two-parameters -->
### One Record Passed As Both Parameters of a Field-Store Helper
`assign(a, src: a)` hands the same struct to both parameters, so `dst` and `src`
name one heap box and `dst.args = src.args` stores an array onto itself. The
array must survive the store with its element intact. Release-first frees it and
then increfs freed memory, which faulted with an access violation.
```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Blk
	export var args as NumArray

	export static function create() returns Blk
		return Self{args: NumArray.create()}
	end 'create'
end 'Blk'

function assign(dst Blk, src Blk)
	dst.args = src.args
end 'assign'

function main() returns ExitCode
	var a = Blk.create()
	a.args.push(7)
	assign(a, src: a)
	let n = a.args.count()
	let v = try a.args.get(0) otherwise 0
	print("n={n} v={v}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=1 v=7
```

<!-- test: self-field-store-aliasing-its-own-receiver -->
### A Self-Field Store Whose Argument Is the Receiver Itself
The same hazard through the OTHER parser door: `self.args = other.args` inside a
method, invoked as `a.absorb(a)` so the receiver and the argument are one box.
The two doors converge on one emission and must therefore agree on the order;
this case pins that they do.
```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Blk
	export var args as NumArray

	export static function create() returns Blk
		return Self{args: NumArray.create()}
	end 'create'

	export function absorb(other Blk)
		self.args = other.args
	end 'absorb'
end 'Blk'

function main() returns ExitCode
	var a = Blk.create()
	a.args.push(7)
	a.absorb(a)
	let n = a.args.count()
	let v = try a.args.get(0) otherwise 0
	print("n={n} v={v}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=1 v=7
```

<!-- test: string-field-store-aliasing-through-two-parameters -->
### A `String` Field Stored Onto Itself Must Acquire Before It Releases
The acquire is `retainBorrowedByteRecord` (`__str_retain`), not the `__mm_alloc` +
`__str_copy` this case was written against: since `ca5169e231` a borrowed `String` reaching
a DURABLE sink is CO-OWNED, for the reason the struct/union arm beside it already gave —
Maxon is single-ownership with reference semantics, so a `dst.name = src.name` that stored a
different record than the `src.name` it read would be a copy the author never wrote, and the
field's identity would diverge from the source's. The ORDER is what this case pins and the
new acquire does not move it: `__str_retain` reads the source record — its `capacity@16`, then
either an incref or a clone of its bytes — so a release-first store frees the field's only
reference and the acquire touches freed memory, the same defect with a different acquire. The
field must still read back its original text.
```maxon
type Rec
	export var name as String

	export static function create(n String) returns Rec
		return Self{name: n}
	end 'create'
end 'Rec'

function copyName(dst Rec, src Rec)
	dst.name = src.name
end 'copyName'

function main() returns ExitCode
	var r = Rec.create("hello")
	copyName(r, src: r)
	print("name={r.name}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
name=hello
```
