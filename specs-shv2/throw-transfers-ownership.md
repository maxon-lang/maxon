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
<!-- ENABLED by #64 (P1.5, 2026-07-23): `run()` throws its own `pendingError` through a BORROWED `self`. The throw now transfers the box by RETAIN (`retainThrownField` increfs it, leaving the field slot intact) rather than the nulling MOVE an OWNED-LOCAL container uses — so the caller's box stays live and the refcount balances at one free, with no cross-call move tracking. Applies the #40 retain-promotion thesis to the throw error channel; see `boxed-union-field-throw-borrowed-self` below for the property that distinguishes it from a move (the caller re-reads the field after catching). Predecessors that had to land first: P1.4b wave 2c (the boxed-union struct FIELD — construct/reassign/scope-exit-drop/owned-local move-out) and #65 implicit-self (the bare `bump()` call in `run()`). -->
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
### Throwing a boxed-union field of an OWNED LOCAL struct transfers it out (P1.4b wave 2c)

A function holds an owned local struct with a boxed-union field and throws that
field. shv2 has no incref, so the throw MOVES the box out of the field (nulls the
slot); the function's own scope-exit drop of the struct then skips the nulled slot
(the cascade's null-guard), and the box reaches the caller alive. Caught and matched,
it yields the transferred payload once — no leak, no double-free.

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
way an OWNED LOCAL container does (see `boxed-union-field-throw-owned-local`): that would
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
