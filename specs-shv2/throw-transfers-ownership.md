---
feature: throw-transfers-ownership
status: stable
keywords: [throw, propagate, transfer, refcount, memory, error-handling, codegen, union]
category: memory-management
---

# Throw Transfers Owned Reference For Heap-Allocated Errors

## Documentation

When a function throws an associated-value union (heap-allocated error), the
throw site transfers an owned reference (rc>=1) to the caller through the
error-return ABI. This prevents the thrown pointer from being freed by
caller-side scope cleanup of locals that may transitively own the same heap
object — for example:

- `throw self.field` inside an instance method, where the *outer* function
  holds the receiver as a local. After the throw unwinds, that outer local is
  decref'd by scope cleanup, and the local's destructor would decref the field
  the throw was carrying.
- `return try inner_throwing_function()` (propagation form) inside a function
  that holds a local struct; the propagated heap pointer travels through the
  outer function's scope cleanup of that local before reaching its caller.

The matching catch-side bookkeeping consumes the transferred reference instead
of incref'ing again: the binding-assign for `otherwise (e)` skips its incref,
and the no-binding `otherwise` form decrefs once to release the transfer.

## Tests

<!-- test: propagate-throw-through-local-struct -->
### Propagating a heap-allocated error through a function holding a local struct

A function creates a local struct, calls a method that throws an
associated-value union (loaded from a self field that an inner call had
rewritten), and propagates the throw via `return try`. The local struct's
destructor would otherwise free the field as part of the function's scope
cleanup; the throw must keep the pointer alive until the caller consumes it.

```maxon
typealias N = int(0 to i64.max)

union OuterErr implements Error
	unterminatedString(line N, column N)
	unexpectedEof(line N, column N)
end 'OuterErr'

enum InnerErr implements Error
	gone
end 'InnerErr'

type Lex
	var pendingError as OuterErr
	var hasPending as bool

	static function create() returns Lex
		return Self{pendingError: OuterErr.unexpectedEof(0, column: 0), hasPending: false}
	end 'create'

	function bump() throws InnerErr
		hasPending = true
		pendingError = OuterErr.unterminatedString(7, column: 13)
		throw InnerErr.gone
	end 'bump'

	function run() returns N throws OuterErr
		try bump() otherwise 'inner'
			if hasPending 'hp'
				throw pendingError
			end 'hp'
			throw OuterErr.unexpectedEof(0, column: 0)
		end 'inner'
		return 0
	end 'run'
end 'Lex'

function outer() returns N throws OuterErr
	var lex = Lex.create()
	return try lex.run()
end 'outer'

function main() returns ExitCode
	let v = try outer() otherwise (e) 'fail'
		match e 'kind'
			unterminatedString(line, column) then return (line + column)
			unexpectedEof(line, column) then return (line + column + 100)
		end 'kind'
	end 'fail'
	return (v + 200)
end 'main'
```
```exitcode
20
```

<!-- test: propagate-throw-otherwise-no-binding-decrefs -->
### A binding-less `otherwise` on a heap-allocated error path decrefs once

When a `try ... otherwise` block catches an associated-value error without
binding it to a name, the implicit cleanup must release the single transferred
reference (no incref/decref pair). The handler runs on error and the caller
sees no leak.

```maxon
typealias N = int(0 to i64.max)

union LexErr implements Error
	unterminatedString(line N, column N)
end 'LexErr'

function tokenize() returns N throws LexErr
	throw LexErr.unterminatedString(7, column: 13)
end 'tokenize'

function main() returns ExitCode
	var ran = false
	let v = try tokenize() otherwise 'noBinding'
		ran = true
		return 0
	end 'noBinding'
	if ran 'ranOk'
		return (v + 5)
	end 'ranOk'
	return 99
end 'main'
```
```exitcode
0
```

<!-- test: boxed-union-field-throw-owned-local -->
### Throwing a boxed-union field of a struct held in a local transfers it out (P1.4b wave 2c)

A function holds an owned local struct with a boxed-union field and throws that
field. The throw TRANSFERS an owned reference to the caller either way — by nulling
the field slot when this frame is the box's SOLE owner, or by `__mm_incref` when it
is not — and the caller's catch consumes exactly one reference, so the box reaches
the caller alive and is freed once. Caught and matched, it yields the transferred
payload once — no leak, no double-free.

⚠ **THE TRANSFER HERE IS THE RETAIN, AND THE PROSE SAID "shv2 has no incref, so the
throw MOVES the box out" — false twice over.** `retainThrownField` has existed since
#64 (the case below uses it), and this container is `let h = Holder.create()`: a CALL
RESULT, which shv2 must treat as a CO-OWNER because a `return` may hand back an
increfed borrow of something the callee still holds (`return h.ty`) and no signature
records which. Nulling the slot of a co-owned container is a segfault
(`union-managed-payload`'s co-owned family measures six of them), so the move-out now
demands sole ownership. What this case pins is that the TRANSFER stays balanced
whichever mechanism serves it; `boxed-union-field-throw-out-of-a-sole-owned-literal`
pins the move-out itself.

```maxon
typealias N = int(0 to i64.max)

union OuterErr implements Error
	unterminatedString(line N, column N)
	unexpectedEof(line N, column N)
end 'OuterErr'

type Holder
	export var pending as OuterErr

	static function create() returns Holder
		return Self{pending: OuterErr.unterminatedString(7, column: 13)}
	end 'create'
end 'Holder'

function process() returns N throws OuterErr
	let h = Holder.create()
	throw h.pending
end 'process'

function main() returns ExitCode
	let v = try process() otherwise (e) 'fail'
		match e 'kind'
			unterminatedString(line, column) then return (line + column)
			unexpectedEof(line, column) then return (line + column + 100)
		end 'kind'
	end 'fail'
	return (v + 200)
end 'main'
```
```exitcode
20
```

<!-- test: boxed-union-field-throw-borrowed-self -->
### Throwing a boxed-union self-field through a BORROWED receiver RETAINS the box (#64)

An instance method throws its own boxed-union `self`-field. `self` is BORROWED — its owner
is the caller's local — so the throw cannot MOVE the box out by nulling the field slot the
way a SOLELY-OWNED container does (see
`boxed-union-field-throw-out-of-a-sole-owned-literal`): that would
null the CALLER's box, a use-after-move if the caller CATCHES and re-reads the field.
Instead the throw RETAINS — `__mm_incref` makes the thrown reference a SECOND owner and
leaves the slot intact — so the caller's box stays live. This test proves exactly that:
after catching the thrown error, `main` RE-READS `lex.pendingError` and sees the same live
value (a move-out would have left a nulled slot, which under the always-on poison-free
`__mm_free` would fault or return `0x3F…`). The caller's container keeps its own reference,
the catch consumes the thrown one, and the refcount balances at one free — no leak, no
double-free. This applies the #40 retain-promotion thesis to the throw error channel.

```maxon
typealias N = int(0 to i64.max)

union OuterErr implements Error
	unterminatedString(line N, column N)
	unexpectedEof(line N, column N)
end 'OuterErr'

type Lex
	export var pendingError as OuterErr

	static function create() returns Lex
		return Self{pendingError: OuterErr.unterminatedString(7, column: 13)}
	end 'create'

	function run() returns N throws OuterErr
		throw pendingError
	end 'run'
end 'Lex'

function main() returns ExitCode
	var lex = Lex.create()
	var caught = 0
	try lex.run() otherwise (e) 'fail'
		match e 'k'
			unterminatedString(line, column) then caught = (line + column)
			unexpectedEof(line, column) then caught = 900
		end 'k'
	end 'fail'
	let again = lex.pendingError
	match again 'k2'
		unterminatedString(line, column) then return (caught + line + column)
		unexpectedEof(line, column) then return (caught + 500)
	end 'k2'
end 'main'
```
```exitcode
40
```

<!-- test: boxed-union-field-throw-out-of-a-sole-owned-literal -->
### Throwing a boxed-union field of a SOLELY-owned struct still MOVES it out

⭐⭐ **THE COVERAGE THE CASE ABOVE STOPPED PROVIDING, AND WITHOUT IT `moveOutThrownField` HAS NO TEST.**
Once a call result became a CO-OWNER, every `let h = Type.create(…)` container took the retain. What is left
is the PROPERTY rather than a list of spellings: **the frame must have CREATED the box and never shared its
pointer** — which is what `OwnedHeapExclusivity.sole` means and the only thing that licenses the null store.
The reachable spelling is a struct LITERAL, which the language admits only inside the declaring type's own
body (`struct-construction-restriction`), and that is this program: `let h = Self{…}` in a static of
`Holder`, straight out of `__mm_alloc`. The throw nulls `pending@0`, `__destruct_Holder`'s null-guard skips
it, and the box reaches the caller — so the emitted code carries the null store and NO `__mm_incref`, which
is what tells this case apart from its three siblings. `3 + 4 + 100` on the `unexpectedEof` arm; a double
free or a leak of the box would be exit 101 rather than a wrong number.

⚠ **THIS PROSE ONCE SAID "the ONLY container the frame can still prove SOLE is a struct LITERAL", AND THE
REVIEW FOUND A COUNTEREXAMPLE — a payload BINDING moved out of a sole box, which is `held(b) then throw b.e`
and was a 0xC0000005.** That hole is closed (a moved-out payload is co-owned; see the case below), so the
sentence would now be *accidentally* true — which is exactly why it is stated as the property instead. An
"only X" claim about which shapes reach a rule is an enumeration, and enumerations in this file rot; a phi
joining two struct literals is already a second sole container that no such list would have mentioned.

```maxon
typealias N = int(0 to i64.max)

union OuterErr implements Error
	unterminatedString(line N, column N)
	unexpectedEof(line N, column N)
end 'OuterErr'

type Holder
	export var pending as OuterErr

	static function boom() returns N throws OuterErr
		let h = Self{pending: OuterErr.unexpectedEof(3, column: 4)}
		throw h.pending
	end 'boom'
end 'Holder'

function main() returns ExitCode
	let v = try Holder.boom() otherwise (e) 'fail'
		match e 'kind'
			unterminatedString(line, column) then return (line + column)
			unexpectedEof(line, column) then return (line + column + 100)
		end 'kind'
	end 'fail'
	return (v + 200)
end 'main'
```
```exitcode
107
```

<!-- test: boxed-union-field-thrown-out-of-a-co-owned-in-payload -->
### Throwing a boxed-union field of a struct that was CO-OWNED into its box

⛔⛔ **A SOLELY-OWNED BOX DOES NOT MAKE ITS PAYLOAD SOLELY OWNED, AND THE THROW CHANNEL READS THE SAME
CONFLATION.** `Wrap.held(body)` over a BORROWED `body` co-owns the struct by `__mm_incref`
(`moveManagedValueInto`'s borrowed arm) rather than moving it in, so `w` is genuinely this frame's alone while
the `Body` record in its slot has two owners. The match's move-out then vacated `w`'s slot — which proves only
that the frame holds *that slot's* reference — and the payload binding `b` was stamped SOLE, so `throw b.e`
nulled `e@0` in a record the CALLER still reads. **This shape predates the co-ownership rule and is not a
nested-union case: a struct payload has been constructible since P1.4b.** With the payload co-owned the throw
takes `retainThrownField`, the caller's `body.e` stays live, and the refcount balances at one free (`first=7`
from the fallback, `second=52`).

```maxon
typealias Integer = int(i64.min to i64.max)

union Err
	bad(why String)
end 'Err'

type Body
	export var e as Err

	static function create(e Err) returns Self
		return Self{e: e}
	end 'create'
end 'Body'

union Wrap
	held(b Body)
end 'Wrap'

function probe(body Body) returns Integer throws Err
	let w = Wrap.held(body)
	match w 'o'
		held(b) then throw b.e
	end 'o'
	return 0
end 'probe'

function whyLen(e Err) returns Integer
	return match e 'l'
		bad(why) gives why.byteLength() as Integer
	end 'l'
end 'whyLen'

function main() returns ExitCode
	let body = Body.create(Err.bad("a struct payload thrown out of a sole box, heap-long"))
	let first = try probe(body) otherwise 7
	let second = whyLen(body.e)
	print("first={first} second={second}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first=7 second=52
```
