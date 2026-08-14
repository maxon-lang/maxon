---
feature: async-try-await
status: stable
keywords: [async, await, try, otherwise, throws, green-threads, promise, E3057, E3059]
category: concurrency
---

# Async / Await — error-carrying `try await` (P1.5-B2b)

## Documentation

An `async` function that `throws` hands its error back through the SAME dual-register error ABI an
ordinary throwing call uses (the primary value in R8, the error flag in R10). The green-thread trampoline
captures both when the spawned thunk returns, so awaiting such a promise must be done with

```text
let r = try await p otherwise <handler>
```

`try await` lowers to `__gt_try_await`, the throwing twin of `__gt_await`: it drives the scheduler exactly
as `__gt_await` does, then returns the awaited thunk's `(result, errorFlag)` pair through the dual-register
exit. The `try … otherwise …` desugar then reads the flag exactly as it does for a throwing CALL — so
every `otherwise` shape (a fallback value, `ignore`, a bare propagate, a `panic`, and the `(e)` binding
that recovers the thrown error) works on a `try await` unchanged.

A plain `await p` of a promise from a **throwing** function is refused (**E3057**): a plain `await` lowers
to `__gt_await`, which yields only the result and DROPS the error flag — a throw would be delivered as a
wrong answer. The error type the promise carries also lets the propagate form check that a bare
`try await p` re-throws a type the enclosing function actually declares (**E3059** on a mismatch), and lets
`try await` on a NON-throwing promise be refused (**E3055**) exactly as `try` on a non-throwing call is.

This slice is **scalar-only** (like B1a): the awaited result and the async arguments are integer/bool
values, and the thrown error is a scalar enum or a union whose payload is scalar.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** Every case here awaits, so it reaches the driver. The `try`/`otherwise` VALUE FLOW
being pinned is target-neutral and is covered without a marker by `try-otherwise-value-flow`.

## Tests

<!-- test: async-try-await.otherwise-default -->
<!-- targets: x64-windows -->
Two throwing async calls: one succeeds (its result flows through) and one throws (its `otherwise` fallback
value stands in). The two `try await` sites use the ordinary `try` fallback-value desugar.
```maxon
enum WorkError implements Error
	failed
end 'WorkError'

function mayFail(succeed bool) returns int throws WorkError
	Runtime.yield()
	if succeed 'ok'
		return 42
	end 'ok'
	throw WorkError.failed
end 'mayFail'

function main() returns ExitCode
	let p1 = async mayFail(true)
	let r1 = try await p1 otherwise 0
	let p2 = async mayFail(false)
	let r2 = try await p2 otherwise 80
	return (r1 + r2) as ExitCode
end 'main'
```
```exitcode
122
```

<!-- test: async-try-await.propagate -->
<!-- targets: x64-windows -->
A bare `try await p` inside a throwing function propagates the awaited thunk's error to the caller — here
the thunk succeeds, so the propagate is not taken and the result flows through.
```maxon
enum WorkError implements Error
	failed
end 'WorkError'

function mayFail(succeed bool) returns int throws WorkError
	Runtime.yield()
	if succeed 'ok'
		return 10
	end 'ok'
	throw WorkError.failed
end 'mayFail'

function inner() returns int throws WorkError
	let p = async mayFail(true)
	let result = try await p
	return result
end 'inner'

function main() returns ExitCode
	let r = try inner() otherwise 0
	return r as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: async-try-await.otherwise-panic -->
<!-- targets: x64-windows -->
`otherwise panic(…)` on a succeeding `try await` — the panic is unreachable and the result flows through.
```maxon
enum WorkError implements Error
	failed
end 'WorkError'

function succeeds() returns int throws WorkError
	Runtime.yield()
	return 7
end 'succeeds'

function main() returns ExitCode
	let p = async succeeds()
	let r = try await p otherwise panic("should not fail")
	return r as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-try-await.void -->
<!-- targets: x64-windows -->
A void-returning throwing async function has no result — only an error flag. `try await p otherwise ignore`
awaits it for its side effect; the thunk succeeds and sets the global.
```maxon
enum WorkError implements Error
	failed
end 'WorkError'

var flag = 0

function maySetFlag(succeed bool) throws WorkError
	Runtime.yield()
	if succeed 'ok'
		flag = 1
		return
	end 'ok'
	throw WorkError.failed
end 'maySetFlag'

function main() returns ExitCode
	let p = async maySetFlag(true)
	try await p otherwise ignore
	return flag as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: async-try-await.otherwise-bind -->
<!-- targets: x64-windows -->
`otherwise (e)` binds the error the awaited thunk THREW, typed at the thunk's declared `throws` enum — the
promise carries its callee's error type — so `match e` dispatches on the thrown case.
```maxon
enum TaskError implements Error
	timedOut
	crashed
end 'TaskError'

function mayFail(mode int) returns int throws TaskError
	Runtime.yield()
	if mode == 0 'ok'
		return 1
	end 'ok'
	if mode == 1 'slow'
		throw TaskError.timedOut
	end 'slow'
	throw TaskError.crashed
end 'mayFail'

function main() returns ExitCode
	let p = async mayFail(2)
	let r = try await p otherwise (e) 'failed'
		return match e 'why'
			timedOut gives 7
			crashed gives 9
		end 'why'
	end 'failed'
	return r as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: async-try-await.otherwise-bind-assoc-value -->
<!-- targets: x64-windows -->
The thrown error is an associated-value union, so the error flag IS a heap pointer to the payload. The
`(e)` binding takes ownership and scope-end releases it exactly once — a leak (the payload never decref'd)
or a double-free would surface under the runtime's allocation accounting (exit 101). The payload is a
scalar `int`, which the B2b error channel carries.
```maxon
union WorkError implements Error
	refused(code int)
end 'WorkError'

function work(shouldFail bool) returns int throws WorkError
	if shouldFail 'fail'
		throw WorkError.refused(5)
	end 'fail'
	return 3
end 'work'

function main() returns ExitCode
	let p = async work(true)
	let r = try await p otherwise (e) 'failed'
		match e 'which'
			refused(code) then return code as ExitCode
		end 'which'
		return 0
	end 'failed'
	return r as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: async-try-await.otherwise-bind-cross-block -->
<!-- targets: x64-windows -->
The `async` and its `try await` sit in different basic blocks — an intervening `if` separates them — so the
promise (and its error type) must reach the await across the block boundary. In shv2 the promise is the
same SSA value throughout, so its error type is carried with no rebuild.
```maxon
enum WorkError implements Error
	failed
end 'WorkError'

function work(shouldFail bool) returns int throws WorkError
	Runtime.yield()
	if shouldFail 'fail'
		throw WorkError.failed
	end 'fail'
	return 3
end 'work'

function main() returns ExitCode
	let p = async work(true)
	var guard = 1
	if guard == 1 'sep'
		guard = 2
	end 'sep'
	let r = try await p otherwise (e) 'failed'
		return match e 'which'
			failed gives 0
		end 'which'
	end 'failed'
	return r as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: async-try-await.otherwise-bind-void -->
<!-- targets: x64-windows -->
A void-returning throwing async awaited with `try await … otherwise (e)`: there is no result, only the error
flag, and the `(e)` binding is still typed at the thunk's `throws` enum.
```maxon
enum TaskError implements Error
	timedOut
	crashed
end 'TaskError'

var flag = 0

function maySetFlag(succeed bool) throws TaskError
	Runtime.yield()
	if succeed 'ok'
		flag = 1
		return
	end 'ok'
	throw TaskError.crashed
end 'maySetFlag'

function main() returns ExitCode
	let p = async maySetFlag(false)
	try await p otherwise (e) 'failed'
		flag = match e 'which'
			timedOut gives 7
			crashed gives 9
		end 'which'
	end 'failed'
	return flag as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: async-try-await.error.propagate-type-mismatch -->
A bare `try await p` re-throws the awaited thunk's error through the enclosing function's error-return ABI.
If the two error types differ the caller would decode one enum's ordinals as another's tags — a silent
miscompile — so the mismatch is refused (E3059), exactly as it is for a `try` CALL.
```maxon
enum AError implements Error
	alpha
end 'AError'

enum BError implements Error
	beta
	gamma
end 'BError'

function throwsA() returns int throws AError
	throw AError.alpha
end 'throwsA'

function caller() returns int throws BError
	let p = async throwsA()
	let r = try await p
	return r
end 'caller'

function main() returns ExitCode
	let v = try caller() otherwise 0
	return v as ExitCode
end 'main'
```
```maxoncstderr
error E3059: <fragment>:17:10: try propagates 'AError' but enclosing function throws 'BError' — add 'otherwise' to convert
```

<!-- test: async-try-await.error.double-try-await -->
<!-- targets: x64-windows -->
`try await` is LINEAR exactly as plain `await` is — awaiting one throwing promise twice would release its
green thread twice. The linearity pass counts `tryAwait` sites, not only plain `await`, so the second
`try await` of the same promise is refused (E3100). This locks in the soundness that the exhaustive
op-match structurally protects.
```maxon
enum WorkError implements Error
	failed
end 'WorkError'

function compute() returns int throws WorkError
	Runtime.yield()
	return 42
end 'compute'

function main() returns ExitCode
	let p = async compute()
	let a = try await p otherwise 0
	let b = try await p otherwise 0
	return (a + b) as ExitCode
end 'main'
```
```maxoncstderr
error E3100: <fragment>:14:14: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```
