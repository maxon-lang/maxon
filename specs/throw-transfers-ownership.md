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
	var pendingError OuterErr
	var hasPending bool

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
