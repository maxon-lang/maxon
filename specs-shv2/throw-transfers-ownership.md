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

<!-- disabled-test: propagate-throw-through-local-struct -->
<!-- P1.4b wave 2c DELIVERED the boxed-union struct FIELD this test needs — construct (`Self{pendingError: OuterErr…}`), reassign (`pendingError = …`), scope-exit drop, AND the throw-of-field MOVE-OUT `throw pendingError` requires (shv2 has no `__mm_incref`, so a thrown borrowed field is transferred by nulling its slot; see the boxed-union-field-throw-* cases below, which pin that mechanism on the SAME ownership shapes). This test stays disabled on a SEPARATE, later blocker the original note missed: the BARE method call `bump()` (implicit self) in `run()`. shv2 has no implicit-self method resolution — a bare `bump()` is a free call → E3004. The language needs `<EnclosingType>.<name>` method-name resolution with static-vs-instance receiver handling (oracle-verified: a bare instance `helper()` prepends self, a bare static `mk()` gets no receiver), which is a distinct feature/rung, NOT boxed-union fields. Every other construct here — bare self-field read/write, `throw <self-field>`, propagation via `return try lex.run()` — works today. -->
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
	var h = Holder.create()
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
### Throwing a boxed-union self-field through a BORROWED receiver, propagated

The exact ownership shape of the disabled `propagate-throw-through-local-struct`
(minus the implicit-self method call it also needs): an instance method throws its
own boxed-union self-field. `self` is BORROWED — its owner is the caller's `lex`
local — so the throw moves the box out by nulling the field through the borrow, and
the caller PROPAGATES (`return try lex.run()`), tearing `lex` down while the nulled
slot makes its destructor skip the transferred box. The error reaches `main`'s
handler once, no leak, no double-free.

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
