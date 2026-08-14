---
feature: immutable-method-call
status: stable
keywords: immutable, let, method, mutation
category: semantics
---
# Immutable Method Call

## Documentation

Calling a receiver-writing method on an immutable (`let`) binding is a compile-time error. The receiver-writing methods are, exactly: `append` on a `String`, and `push`, `set`, `insert`, `append`, `reserve`, `resize`, `clear`, `pop` and `remove` on an `Array`. Every other method only reads its receiver and is legal on a `let`.

⚠⚠ **THE RULE IS A BUILTIN-SURFACE ONE, AND A DECLARED TYPE IS EXEMPT (USER RULING 2026-08-14).** It lives in the parser's hand-written `arrayMethodMutatesReceiver` / `setMethodMutatesReceiver` rosters and is read only through the BUILTIN dispatchers, so a type the compiler compiles rather than synthesizes never reaches it — `let c = Counter.create(); c.bump()` compiles today for any user `type Counter`. `Set` used to be on this list and left it when `stdlib/Set.maxon` was listed (W90): the three cases below that once pinned E3019 on a `Set` are value-asserting `ok` cases now, so the drop is RECORDED rather than silently inherited. It was RULED, not drifted.

⚠ The `Array` and `String` cases are unaffected and stay green: both are still builtin-dispatched.

A **parameter** is exempt, and that is not a loophole: `mutable` asks whether the NAME may be rebound (a parameter's answer is no), while this rule asks whether the CONTAINER the name denotes may be written — and a parameter is a borrowed reference to the caller's record, so `dest.append(src)` inside a helper is ordinary Maxon. A `let` that merely *aliases* a parameter is still a `let`, and is still refused.

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
error E3019: specs/fragments/immutable-method-call/push-on-let-array-error.test:8:6: cannot pass 'arr' to function that mutates parameter 'self' (in main)
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
error E3019: specs/fragments/immutable-method-call/append-on-let-string-error.test:5:4: cannot pass 's' to function that mutates parameter 'self' (in main)
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

⚠ **shv2 NAMES THE BINDING** (user ruling). The bootstrap prints `immutable 'let' variable` here, but that
is its FALLBACK for having lost the name — `2-Parser.cs` keeps `_lastExprWasMutableVar` set while it clears
`_lastExprVarName` — and every other E3019 in the canonical corpus names the binding, this file's own
`push-on-let-array-error` and `append-on-let-string-error` included.
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
error E3019: specs/fragments/immutable-method-call/pass-let-string-through-inline-if-error.test:9:2: cannot pass 'g' to function that mutates parameter 's' (in main)
```

<!-- test: pass-let-string-through-try-otherwise-error -->
### Passing a `let` String Through `try … otherwise`

The merge doors are shared, so `try … otherwise` must refuse exactly where the inline `if` does. ⚠ Reaching
the borrowed merge takes care: `emitOwnedValueReturn` promotes at a `return`, so a String-returning throwing
function gives an OWNED try edge and `promoteBorrowedMergeEdge` then promotes the fallback to match — a
shape that was always safe. It takes a try edge that is itself a borrow, like the `Array with String`
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
error E3019: specs/fragments/immutable-method-call/pass-let-string-through-try-otherwise-error.test:11:2: cannot pass 'g' to function that mutates parameter 's' (in main)
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
error E3019: specs/fragments/immutable-method-call/push-on-let-alias-of-parameter-error.test:7:4: cannot pass 'a' to function that mutates parameter 'self' (in grow)
```

<!-- test: insert-on-let-set-ok -->
⭐⭐ **A `Set` RECEIVER NO LONGER OBEYS THIS RULE, AND THAT IS THE RULING RATHER THAN A REGRESSION (W90).**
This case pinned E3019 for as long as `Set` was a synthesized builtin. With `stdlib/Set.maxon` listed, `Set`
is a type the compiler COMPILES, `insert` is an ordinary declared method, and the parser's builtin
`setMethodMutatesReceiver` roster is never reached — exactly as it is never reached for any user type. It is
kept as a VALUE-asserting case rather than deleted, so the surface it used to refuse is still executed and
the day something re-refuses it, this goes red.

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

<!-- test: remove-on-let-set-ok -->
The `remove` half of the same ruling. The set is empty, so `remove` answers false and the count is zero.

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

<!-- test: contains-on-let-set-ok -->
A read-only `Set` method on a `let` receiver is fine.

⚠ **THE PARENTHETICAL THAT STOOD HERE WENT STALE AT W90 AND IS DELETED RATHER THAN REWORDED.** It read
*"the `let`-receiver ERROR cases above are refused before any of that, so they carry no restriction"* — and
those two cases COMPILE a set now (the ruling above), so all three of this file's `Set` cases stand or fall
together on whatever a `Set` instance's descriptor costs a target. None of them carries a `targets:` marker
and none ever did, so nothing in this file was ever encoding that restriction; saying so once here is more
honest than a sentence about a distinction that no longer exists.

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
error E3019: specs/fragments/immutable-method-call/set-on-let-array-error.test:8:10: cannot pass 'arr' to function that mutates parameter 'self' (in main)
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
dispatching on it addressed the enclosing struct's box as if it were the array: `items.count()` read the
Bag's second word and answered 0 for an array holding one element, with no diagnostic anywhere.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items as IntArray

	static function create() returns Bag
		return Self{items: IntArray.create()}
	end 'create'

	export function size() returns Integer
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

	export function add(v Integer)
		items.push(v)
	end 'add'

	export function size() returns Integer
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

	export function grow()
		s.append("XY")
	end 'grow'

	export function size() returns Integer
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
A `let` FIELD refuses the write, blaming the field's own name — byte-identical to the runnable oracle.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export let items as IntArray

	static function create() returns Bag
		return Self{items: IntArray.create()}
	end 'create'

	export function add(v Integer)
		items.push(v)
	end 'add'

	export function size() returns Integer
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
error E3019: specs/fragments/immutable-method-call/push-on-let-self-field-array-error.test:13:9: cannot pass 'items' to function that mutates parameter 'self' (in Bag.add)
```
