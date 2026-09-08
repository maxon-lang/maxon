---
feature: typealias-managed-field-assignment
status: stable
keywords: [typealias, struct, field, managed, refcount, reassign, alias, leak]
category: memory
---
# Managed Field Assignment Through Typealiases

## Documentation

A struct field whose declared type is a typealias of a managed type (`typealias
NumArray = Array with Num` used as `var args as NumArray`, or the same alias
declared as a member typealias inside the type) must follow the exact same
ownership protocol as a plainly-typed managed field:

1. Storing a borrowed value into the field mints the field's own reference
   (store-incref-new).
2. Reassigning the field releases the displaced occupant (decref-old) — even
   when the store is the field's first write in the function (no prior load).
3. The struct's destructor releases the field.

The alias name must be resolved before ownership classification; a classifier
that consults the raw alias name would treat the field as unmanaged and skip
all three legs, leaking the occupant (or the stored value) on every
assignment.

These tests run under the suite's leak gate, so any skipped incref/decref
fails the test with a leak exit.

## Tests

<!-- test: member-typealias-cross-struct-field-assign -->
### Cross-Struct Field Assignment Through a Member Typealias
Assigning one struct's alias-typed managed field from another struct's field,
in a loop, must release every displaced array and count the aliased one. Any
missed decref-old (or missed store-incref) leaks once per iteration and trips
the leak gate.
```maxon
typealias Num = int(i64.min to i64.max)

type Blk
	typealias NumArray = Array with Num
	export var args as NumArray

	export static function create() returns Blk
		return Self{args: NumArray.create()}
	end 'create'
end 'Blk'

function main() returns ExitCode
	var i = 0

	while i < 3000 'l'
		var a = Blk.create()
		a.args.push(i)
		var b = Blk.create()
		b.args = a.args
		i = i + 1
	end 'l'

	print("done")
	return 0
end 'main'
```
```stdout
done
```

<!-- test: module-typealias-self-field-store -->
### Self-Field Store Through a Module Typealias Without a Prior Load
A `self.field = x` whose first touch of the field is the store (no dominating
load) must still release the displaced occupant. The setter runs in a loop so
a single missed release compounds into a leak-gate failure.
```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Blk
	export var args as NumArray

	export static function create() returns Blk
		return Self{args: NumArray.create()}
	end 'create'

	export function setArgs(x NumArray)
		self.args = x
	end 'setArgs'
end 'Blk'

function main() returns ExitCode
	var i = 0

	while i < 3000 'l'
		var a = Blk.create()
		a.args.push(i)
		var b = Blk.create()
		b.setArgs(a.args)
		i = i + 1
	end 'l'

	print("done")
	return 0
end 'main'
```
```stdout
done
```

<!-- test: alias-typed-field-cloned-both-live -->
### Cloning a Struct by Aliasing Its Alias-Typed Field, Both Copies Live
The `cloneIrBlock` shape: a helper builds a fresh struct and assigns its
alias-typed managed field directly from a borrowed parameter's field, then
returns the fresh struct. The source and the returned clone both outlive the
assignment, so the field must take its own reference (store-incref-new). A
missed incref shares one array between two struct lifetimes and double-frees it
at teardown; a spurious extra incref leaks it. Both fault the leak gate.
```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Blk
	export var args as NumArray

	export static function create() returns Blk
		return Self{args: NumArray.create()}
	end 'create'
end 'Blk'

function cloneBlk(src Blk) returns Blk
	var block = Blk.create()
	block.args = src.args
	return block
end 'cloneBlk'

function main() returns ExitCode
	var i = 0
	var total = 0

	while i < 3000 'l'
		var a = Blk.create()
		a.args.push(i)
		let c = cloneBlk(a)
		total = total + a.args.count() + c.args.count()
		i = i + 1
	end 'l'

	print("done {total}")
	return 0
end 'main'
```
```stdout
done 6000
```
