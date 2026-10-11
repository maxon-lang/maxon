---
feature: immutable-method-call
status: stable
keywords: immutable, let, method, mutation
category: semantics
---
# Immutable Method Call

## Documentation

Calling a receiver-writing method on a read-only (`let`) value is a compile-time error, `E3159`. A `let` is deeply read-only: nothing reachable through it may be written. The receiver-writing methods of the builtin containers are, exactly: `append` on a `String`, and `push`, `set`, `insert`, `append`, `reserve`, `resize`, `clear`, `pop` and `remove` on an `Array`. A method of a declared type — `Set` among them, since `stdlib/Set.maxon` declares it — is receiver-writing when its body writes `self`, and is refused on a `let` the same way: `let c = Counter.create(); c.bump()` is `E3159` for any user `type Counter` whose `bump` writes a field. Every method that only reads its receiver is legal on a `let`.

A **parameter** is not a `let`: a parameter is a borrowed reference to the caller's record, so `dest.append(src)` inside a helper is ordinary Maxon, and the caller is refused at the call when it hands that parameter a `let` (`parameter-mutation.md`). A `let` that merely *aliases* a parameter is still a `let`, and is still refused.

## Tests

<!-- test: push-on-let-array-error -->
Calling `push` on a `let` array should produce a compile-time error.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let arr = IntArray.create()
	arr.push(42)
	return 0
end 'main'
```
```maxoncstderr
error E3159: specs/immutable-method-call/push-on-let-array-error.maxon:8:6: cannot write through 'arr': 'arr' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
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
error E3159: specs/immutable-method-call/append-on-let-string-error.maxon:5:4: cannot pass 's', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: append-on-string-parameter-ok -->
A `String` PARAMETER may be appended to. It is not `mutable` — a parameter cannot be rebound — but the
record it denotes is the caller's and may be written, exactly as an array parameter's is.

```maxon
typealias Integer = int(i64.min to i64.max)

function grow(s String) returns Integer
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

<!-- test: pass-let-string-through-inline-if-error -->
### Passing a `let` String Through an Inline `if`

⭐ **A LAUNDER IS NOT A LOOPHOLE.** `grow(g)` on a `let` is refused, so `grow(g if flag else g)` — the SAME
binding, reached through a merge that copies nothing — must be refused too. The blame name is keyed on the
argument being a single bare token, and an inline `if` is three, so the argument's own name says nothing;
the MERGE carries its arms' blame onto the result phi instead.

⚠ **THIS IS A MEMORY-SAFETY RULE, NOT A MESSAGE ONE**, which is why it is pinned here rather than left as a
nicety: accepted, the program writes into the literal's read-only `.rdata` record and takes an ACCESS
VIOLATION on x64, and on wasm32-wasi the same write succeeds into a record shared by every use of that
literal.

⚠ **The compiler NAMES THE BINDING** (⚖ user ruling): the merge does not lose the name, and every other
E3159 in the canonical corpus names the read-only root, this file's own `push-on-let-array-error` and
`append-on-let-string-error` included.
```maxon
function grow(s String)
	s.append("XY")
end 'grow'

function main() returns ExitCode
	let flag = true
	let g = "hello"
	grow(g if flag else g)
	return 0
end 'main'
```
```maxoncstderr
error E3159: specs/immutable-method-call/pass-let-string-through-inline-if-error.maxon:9:2: cannot pass 'g', which is read-only (a `let` binding), to a function that writes parameter 's' (in main); pass a clone()
```

<!-- test: pass-let-string-through-try-otherwise-error -->
### Passing a `let` String Through `try … otherwise`

The merge doors are shared, so `try … otherwise` must refuse exactly where the inline `if` does. ⚠ Reaching
the borrowed merge takes care: `emitOwnedValueReturn` promotes at a `return`, so a String-returning throwing
function gives an OWNED try edge and `promoteBorrowedMergeEdge` then promotes the fallback to match — a
shape that is safe. It takes a try edge that is itself a borrow, like the `Array with String`
element `get` hands back without copying, for the merge to stay borrowed all the way to the callee.
```maxon
typealias StringArray = Array with String

function grow(s String)
	s.append("XY")
end 'grow'

function main() returns ExitCode
	let g = "hello"
	var arr = StringArray.create()
	grow(try arr.get(0) otherwise g)
	return 0
end 'main'
```
```maxoncstderr
error E3159: specs/immutable-method-call/pass-let-string-through-try-otherwise-error.maxon:11:2: cannot pass 'g', which is read-only (a `let` binding), to a function that writes parameter 's' (in main); pass a clone()
```

<!-- test: push-on-let-alias-of-parameter-error -->
A `let` that merely ALIASES a parameter is still a `let`. The alias carries the parameter's own value,
so a rule derived from that value rather than from the binding would wrongly accept this.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(p IntArray) returns Integer
	let a = p
	a.push(9)
	return a.count()
end 'grow'

function main() returns ExitCode
	var v = IntArray.create()
	return grow(v) as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/immutable-method-call/push-on-let-alias-of-parameter-error.maxon:7:4: cannot write through 'a': 'a' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: insert-on-let-set-error -->
A `Set` is a type the compiler COMPILES: `insert` is an ordinary declared method whose body writes `self`, so
it writes the receiver, and a `let` receiver is refused exactly as it is for an `Array`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

function main() returns ExitCode
	let s = IntSet.create()
	s.insert(1)
	return s.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/immutable-method-call/insert-on-let-set-error.maxon:7:4: cannot pass 's', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
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

<!-- test: remove-on-let-set-error -->
The `remove` half of the same ruling: `remove` writes the set even when it answers false.

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
```maxoncstderr
error E3159: specs/immutable-method-call/remove-on-let-set-error.maxon:7:15: cannot pass 's', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
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

<!-- test: set-on-let-array-error -->
Calling `set` on a `let` array should produce a compile-time error.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let arr = IntArray.create()
	try arr.set(0, value: 99) otherwise panic("test invariant: set OOB")
	return 0
end 'main'
```
```maxoncstderr
error E3159: specs/immutable-method-call/set-on-let-array-error.maxon:8:10: cannot write through 'arr': 'arr' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: read-on-let-array-ok -->
Reading from a `let` array (non-mutating methods) should work fine.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let arr = IntArray.create()
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
	var arr = IntArray.create()
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
		return Self{items: IntArray.create()}
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
		return Self{items: IntArray.create()}
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

<!-- test: push-on-let-self-field-array-error -->
A `let` FIELD refuses the write, blaming the field's own name.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export let items as IntArray

	static function create() returns Bag
		return Self{items: IntArray.create()}
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
```maxoncstderr
error E3159: specs/immutable-method-call/push-on-let-self-field-array-error.maxon:13:9: cannot write through 'items': 'items' is read-only (a `let` field) and nothing reachable through it may be written; use 'var', or write a clone()
```

⭐⭐ **AND A CHAIN THROUGH A MODULE-LEVEL `let` REFUSES IT TOO, WHICH IS THE HALF NO DOOR COULD SEE.** The
case above is a `let` FIELD reached from inside the type; this is a `let` GLOBAL reached through a field
chain, where the receiver of the mutating call is the field's own record rather than the binding's.
`SharedFacts.counts` is three tokens, so the bare-name test says nothing about it, and a chain test that asked
only whether an enclosing `for` had locked the base would compile the write. When such a global's every field is
image data the record is `.rdata`: the push reaches `__mm_cow_detach` and writes a page mapped read-only,
which is a fault on x64 and, on wasm32-wasi, a silent rewrite of the constant.

⛔ **THE RULE IS ASKED OF EVERY ROOT, A LOCAL `let` INCLUDED.** A `let` is deeply read-only, so after
`let e = Facts.create()` both `e.counts.push(1)` and `e.counts.set(0, value: 9)` are writes through `e` and are
refused (`error.push-through-a-field-chain-off-a-local-let`); the same chain off a `var` is legal
(`push-through-a-field-chain-off-a-local-var-ok`).

⚠ **A `self`-ROOTED CHAIN IS UNTOUCHED** — `self` is a parameter, a borrowed reference to the caller's
record, so `self.items.push(v)` inside the type's own method stays legal; the CALL of that method on a `let`
is what is refused, which `parameter-mutation:let-struct-with-array-field-to-mutating-method-error` pins.

<!-- test: error.push-through-a-field-chain-off-a-module-let -->
```maxon
typealias Count = int(0 to 1000)
typealias CountArray = Array with Count

type Facts
	export var counts = CountArray.create()

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
error E3159: <fragment>:16:21: cannot write through 'SharedFacts.counts': 'SharedFacts' is read-only (a module-level `let`) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: error.push-through-a-field-chain-off-a-local-let -->
```maxon
typealias Count = int(0 to 1000)
typealias CountArray = Array with Count

type Facts
	export var counts = CountArray.create()

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
```maxoncstderr
error E3159: <fragment>:15:15: cannot write through 'facts.counts': 'facts' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: push-through-a-field-chain-off-a-local-var-ok -->
The same chain off a `var` writes the record the field holds.

```maxon
typealias Count = int(0 to 1000)
typealias CountArray = Array with Count

type Facts
	export var counts = CountArray.create()

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

<!-- test: returned-alias-to-a-mutating-parameter -->
### Passing a `let` String Through a Call That Returns It

A call is a launder too when it may hand back its argument: `pass(g)` may be `g`'s own record, so `grow`
would write it while `g` is still read.
```maxon
function grow(s String)
	s.append("XY")
end 'grow'

function pass(s String) returns String
	return s
end 'pass'

function main() returns ExitCode
	let n = 7
	let g = "hello{n}"
	grow(pass(g))
	print("{g}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: specs/immutable-method-call/returned-alias-to-a-mutating-parameter.maxon:13:2: cannot pass 'g', which is read-only (a `let` binding), to a function that writes parameter 's' (in main); pass a clone()
```

<!-- test: let-record-written-through-a-returned-container -->
### Writing a `let` Array Through a Call That Returns It

The receiver door asks of a call's result what it asks of the name the result may be.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function pass(s IntArray) returns IntArray
	return s
end 'pass'

function main() returns ExitCode
	let a = IntArray.create()
	pass(a).push(9)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/immutable-method-call/let-record-written-through-a-returned-container.maxon:11:10: cannot write through 'pass(a)': 'a' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: let-record-alias-of-a-parameter-passed-through-a-call -->
### A `let` Alias of a Parameter, Through a Call

The alias is blamed through a call exactly as `push-on-let-alias-of-parameter-error` blames it by name;
`q` is read after the call.
```maxon
function grow(s String)
	s.append("XY")
end 'grow'

function pass(s String) returns String
	return s
end 'pass'

function tagIt(p String)
	let q = p
	grow(pass(q))
	print("{q}\n")
end 'tagIt'

function main() returns ExitCode
	let n = 7
	var a = "lit{n}"
	tagIt(a)
	a.append("?")
	print("{a}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: specs/immutable-method-call/let-record-alias-of-a-parameter-passed-through-a-call.maxon:12:2: cannot pass 'q', which is read-only (a `let` binding), to a function that writes parameter 's' (in tagIt); pass a clone()
```
