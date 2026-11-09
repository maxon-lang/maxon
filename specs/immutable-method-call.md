---
feature: immutable-method-call
status: stable
keywords: immutable, let, method, mutation
category: semantics
---
# Immutable Method Call

## Documentation

Calling a receiver-writing method on a `let` value is legal: `let` forbids only reassigning the name, so the method writes the record the name denotes. The receiver-writing methods of the builtin containers are, exactly: `append` on a `String`, and `push`, `set`, `insert`, `append`, `reserve`, `resize`, `clear`, `pop` and `remove` on an `Array`. A method of a declared type — `Set` among them, since `stdlib/Set.maxon` declares it — is receiver-writing when its body writes `self`: `let c = Counter.create(); c.bump()` runs for any user `type Counter` whose `bump` writes a field. Every method that only reads its receiver is legal on any binding.

A module-level `let` is read-only data. A receiver-writing method on it, or on a field chain rooted at it, is refused: `E3207` for an `Array` method, `E3159` where the method writes a `String` field. A `let` bound to a compile-time constant is immutable image data for the same reason.

A **parameter** is a borrowed reference to the caller's record, so `dest.append(src)` inside a helper is ordinary Maxon, and a caller may hand that parameter a `let` (`parameter-mutation.md`). A `let` that *aliases* a parameter names the same record.

## Tests

<!-- test: push-on-let-array-ok -->
Calling `push` on a `let` array writes the array the name denotes; the `let` only forbids reassigning `arr`.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let arr = IntArray.create(0)
	arr.push(42)
	return arr.count()
end 'main'
```
```exitcode
1
```

<!-- test: append-on-let-string-error -->
Calling `append` on a `let` string should produce a compile-time error.

```maxon

function main() returns ExitCode
	let s = "hello"
	s.append(" world")
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:5:4: 's' is a let and cannot be passed to the `mutable` parameter 'self'; bind it to a `var`
```

<!-- test: append-on-string-parameter-ok -->
A `String` PARAMETER may be appended to. It is not `mutable` — a parameter cannot be rebound — but the
record it denotes is the caller's and may be written, exactly as an array parameter's is.

```maxon
typealias Integer = int(i64.min to i64.max)

function grow(s mutable String) returns Integer
	s.append("XY")
	return s.byteLength()
end 'grow'

function main() returns ExitCode
	var t = "ab"
	return grow(t) as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: push-on-let-alias-of-parameter-ok -->
A `let` that ALIASES a parameter names the caller's record, so the push is seen through the caller's `v`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(p IntArray) returns Integer
	let a = p
	a.push(9)
	return a.count()
end 'grow'

function main() returns ExitCode
	var v = IntArray.create(0)
	return grow(v) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: insert-on-let-set-ok -->
A `Set` is a type the compiler COMPILES: `insert` is an ordinary declared method whose body writes `self`, and
it writes the record a `let` receiver names.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

function main() returns ExitCode
	let s = IntSet.create()
	s.insert(1)
	return s.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: insert-on-var-set-ok -->
The same call on a `var` Set writes it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(1)
	return s.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: remove-on-let-set-ok -->
The `remove` half of the same ruling: `remove` is a write the `let` receiver takes even when it answers false.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

function main() returns ExitCode
	let s = IntSet.create()
	let gone = s.remove(1)
	if gone 'g'
		return 1
	end 'g'
	return s.count() as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: remove-on-var-set-ok -->
The set is empty, so `remove` on a `var` answers false and the count is zero.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

function main() returns ExitCode
	var s = IntSet.create()
	let gone = s.remove(1)
	if gone 'g'
		return 1
	end 'g'
	return s.count() as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: contains-on-let-set-ok -->
A read-only `Set` method on a `let` receiver is fine.

⚠ The `Set` run cases COMPILE a set, so they stand or fall together on whatever a `Set` instance's
descriptor costs a target. None of them carries an `unsupported-targets:` marker.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

function main() returns ExitCode
	let s = IntSet.create()
	let has = s.contains(1)
	if has 'h'
		return 1
	end 'h'
	return s.count() as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: set-on-let-array-ok -->
Calling `set` on a `let` array writes the element.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let arr = IntArray.create(1)
	try arr.set(0, value: 99) otherwise panic("test invariant: set OOB")
	return try arr.get(0) otherwise -1
end 'main'
```
```exitcode
99
```

<!-- test: read-on-let-array-ok -->
Reading from a `let` array (non-mutating methods) should work fine.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let arr = IntArray.create(0)
	let n = arr.count()
	return n
end 'main'
```
```exitcode
0
```

<!-- test: push-on-var-array-ok -->
Calling `push` on a `var` array should work fine.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create(0)
	arr.push(42)
	let x = try arr.get(0) otherwise 0
	return x
end 'main'
```
```exitcode
42
```

<!-- test: read-on-var-self-field-array-ok -->
A bare self-field name used as a method RECEIVER must load the FIELD, not the receiver. A self-field
alias carries no SSA value (`VarInfo.boundValue` is left 0 — and ValueId 0 IS the receiver), so
dispatching on it would address the enclosing struct's box as if it were the array: `items.count()` would
read the Bag's second word and answer 0 for an array holding one element, with no diagnostic anywhere.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items as IntArray

	static function create() returns Bag
		return Self{items: IntArray.create(0)}
	end 'create'

	function size() returns Integer
		return items.count()
	end 'size'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push(1)
	return b.size() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: push-on-var-self-field-array-ok -->
A `var` FIELD's container may be written through its bare self-field name. The field's own `var`/`let` is
what decides — the same `layout.fieldIsMutable` column a self-field ASSIGNMENT asks — and not the
receiver binding, which is a parameter and therefore never `mutable`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items as IntArray

	static function create() returns Bag
		return Self{items: IntArray.create(0)}
	end 'create'

	function add(v Integer)
		items.push(v)
	end 'add'

	function size() returns Integer
		return items.count()
	end 'size'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.add(1)
	return b.size() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: append-on-var-self-field-string-ok -->
The `String` half of the same rule.

```maxon
typealias Integer = int(i64.min to i64.max)

type Buf
	export var s as String

	static function create() returns Buf
		return Self{s: "ab"}
	end 'create'

	function grow()
		s.append("XY")
	end 'grow'

	function size() returns Integer
		return s.byteLength()
	end 'size'
end 'Buf'

function main() returns ExitCode
	var b = Buf.create()
	b.grow()
	return b.size() as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: push-on-let-self-field-array-ok -->
A `let` FIELD forbids only reassigning the field, so the array it holds may be pushed onto.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export let items as IntArray

	static function create() returns Bag
		return Self{items: IntArray.create(0)}
	end 'create'

	function add(v Integer)
		items.push(v)
	end 'add'

	function size() returns Integer
		return items.count()
	end 'size'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.add(1)
	return b.size() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.push-through-a-field-chain-off-a-module-let -->
A chain through a module-level `let` reaches immutable image data: `SharedFacts.counts` is three tokens, so
the receiver of the push is the field's own record rather than the binding. The push would write a page mapped
read-only, which is a fault on x64 and, on wasm32-wasi, a silent rewrite of the constant.

```maxon
typealias Count = int(0 to 1000)
typealias CountArray = Array with Count

type Facts
	export var counts = CountArray.create(0)

	static function create() returns Facts
		return Self{}
	end 'create'
end 'Facts'

let SharedFacts = Facts.create()

function main() returns ExitCode
	SharedFacts.counts.push(1)

	return SharedFacts.counts.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3207: <fragment>:16:21: 'push' needs a writable receiver; 'counts' is immutable
```

<!-- test: push-through-a-field-chain-off-a-local-let-ok -->
The same chain off a local `let` writes the record the field holds, as off a `var`
(`push-through-a-field-chain-off-a-local-var-ok`). A `self`-rooted chain is a parameter's, so `self.items.push(v)`
inside the type's own method is legal on any receiver.

```maxon
typealias Count = int(0 to 1000)
typealias CountArray = Array with Count

type Facts
	export var counts = CountArray.create(0)

	static function create() returns Facts
		return Self{}
	end 'create'
end 'Facts'

function main() returns ExitCode
	let facts = Facts.create()
	facts.counts.push(1)

	return facts.counts.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: push-through-a-field-chain-off-a-local-var-ok -->
The same chain off a `var` writes the record the field holds.

```maxon
typealias Count = int(0 to 1000)
typealias CountArray = Array with Count

type Facts
	export var counts = CountArray.create(0)

	static function create() returns Facts
		return Self{}
	end 'create'
end 'Facts'

function main() returns ExitCode
	var facts = Facts.create()
	facts.counts.push(1)

	return facts.counts.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: let-record-written-through-a-returned-container -->
### Writing a `let` Array Through a Call That Returns It

The result of `pass(a)` is the record `a` names, so the push lands in it.


```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function pass(s IntArray) returns IntArray
	return s
end 'pass'

function main() returns ExitCode
	let a = IntArray.create(0)
	pass(a).push(9)
	return a.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: append-to-a-string-field-off-a-local-let -->
A `String` field of a `let` holder is storage of the `let`'s record, so appending to it writes the record.


```maxon
type Holder
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create("ab")
	h.name.append("XY")
	print("{h.name}\n")
	return 0
end 'main'
```
```stdout
abXY
```
```exitcode
0
```

<!-- test: error.append-to-a-string-field-off-a-module-let -->
```maxon
type Holder
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Holder'

let Shared = Holder.create("ab")

function main() returns ExitCode
	Shared.name.append("XY")
	print("{Shared.name}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:13:14: cannot pass 'Shared', which is read-only (a module-level `let`), to a function that writes parameter 'self' (in main); pass a clone()
```
