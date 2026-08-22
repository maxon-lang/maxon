---
feature: diverging-function
status: experimental
keywords: [panic, diverges, never returns, otherwise, terminator, unreachable]
category: control-flow
---

# A Function That Never Returns

## Documentation

A function whose body can only ever end in `panic(…)` **never returns to its caller**. A CALL to
such a function is therefore a terminator exactly as a `panic` statement written in its place would
be: control does not continue past it, and whatever follows it in the same block is unreachable.

The compiler learns this from the DECLARATION SWEEP, so the fact is whole-program: a caller in
another file is told as readily as one three lines below. A declaration diverges when **all** of the
following hold, and the conditions are conservative by design — a shape not recognized here is
simply not marked, and the compiler behaves exactly as it did before:

| Condition | Why |
|---|---|
| its body's LAST statement is `panic(…)` | this is what rules out falling off the end, which is a return |
| its body spells no `return` and no `throw` | either is an exit path that is not the panic |
| it declares no `throws` clause | a `try f()` with no `otherwise` RETURNS the error to the caller, and it spells no `throw` of its own |
| its bare name is declared exactly once in the program | the fact is published under the name the source wrote, so a contested name cannot say which declaration a call reaches |

⚠ **The CALL must be a bare-name call statement.** A method call (`h.abort()`) and a namespace-qualified
one (`util.abort()`) are keyed differently and are not asked — they compile exactly as they did before.

⚠ **The condition is on the LAST statement, not on every path.** A body whose `if`/`else` arms all
panic is not recognized, and is not meant to be: the extra reach buys programs that can be written
with the panic at the tail instead, and every widening of this rule is a widening of the set of
call sites that stop emitting a fall-through edge.

### What a call to one does

A bare-name call STATEMENT to a diverging function terminates its block. Three consequences, and
each has a case below:

1. an `otherwise` handler whose last statement is such a call **terminates**, so a `try` used for
   its VALUE is satisfied — the error path produces no value because it never reaches the merge;
2. a function whose body ends in such a call needs no `return` of its own;
3. a statement written after such a call is UNREACHABLE. It is parsed for syntax into a dead block
   and refused nothing, exactly as a statement after an `if`/`else` whose branches all `return` is —
   the E3071 unreachable-code rule is a roster of KEYWORDS at a statement's first token, and a call
   is not one.

## Tests

<!-- test: handler-tail-diverging-call -->
An `otherwise` handler used for its VALUE whose last statement calls a diverging function.
The success path is taken, so the handler never runs.
```maxon
typealias Integer = int(i64.min to i64.max)

enum DrainError implements Error
	pipeDied
end 'DrainError'

function abortOnDeadWorker(worker Integer)
	print("worker {worker} died\n")
	panic("worker {worker} died while running")
end 'abortOnDeadWorker'

function drain(fail bool) returns Integer throws DrainError
	if fail 'broken'
		throw DrainError.pipeDied
	end 'broken'
	return 41
end 'drain'

function main() returns ExitCode
	let lines = try drain(false) otherwise (e) 'workerDied'
		abortOnDeadWorker(7)
	end 'workerDied'
	return lines + 1
end 'main'
```
```exitcode
42
```

<!-- test: handler-tail-diverging-call.error-path -->
The same shape with the error path taken: the handler runs, and the diverging call takes the
process down from inside its own frame.
```maxon
typealias Integer = int(i64.min to i64.max)

enum DrainError implements Error
	pipeDied
end 'DrainError'

function abortOnDeadWorker(worker Integer)
	print("worker {worker} died\n")
	panic("worker {worker} died while running")
end 'abortOnDeadWorker'

function drain(fail bool) returns Integer throws DrainError
	if fail 'broken'
		throw DrainError.pipeDied
	end 'broken'
	return 41
end 'drain'

function main() returns ExitCode
	let lines = try drain(true) otherwise (e) 'workerDied'
		abortOnDeadWorker(7)
	end 'workerDied'
	return lines + 1
end 'main'
```
```exitcode
1
```
```stdout
worker 7 died
```
```stderr
panic at handler-tail-diverging-call.error-path.test:10: worker 7 died while running
Stack trace:
  in abortOnDeadWorker
  in main
  in mrt_start
```

<!-- test: handler-binding-read-by-the-diverging-call -->
The caught error is BOUND and read by the diverging call's argument — the shape
`maxon-shv2/Testing/SpecWorkerPool.maxon` writes, where the handler interpolates
`e.displayReason()` into the abort message (spelled `{e}` here — the same rendering rule, on an error type this
file can declare). The interpolation's temporary is the handler's own and
must be released before the block terminates.
```maxon
typealias Integer = int(i64.min to i64.max)

enum DrainError implements Error
	pipeDied
end 'DrainError'

function abortOnDeadWorker(reason String)
	print("dead: {reason}\n")
	panic("worker died: {reason}")
end 'abortOnDeadWorker'

function drain(fail bool) returns Integer throws DrainError
	if fail 'broken'
		throw DrainError.pipeDied
	end 'broken'
	return 41
end 'drain'

function main() returns ExitCode
	let lines = try drain(true) otherwise (e) 'workerDied'
		abortOnDeadWorker("its stdout died: {e}")
	end 'workerDied'
	return lines + 1
end 'main'
```
```exitcode
1
```
```stdout
dead: its stdout died: pipeDied
```
```stderr
panic at handler-binding-read-by-the-diverging-call.test:10: worker died: its stdout died: pipeDied
Stack trace:
  in abortOnDeadWorker
  in main
  in mrt_start
```

<!-- test: missing-return-satisfied-by-a-diverging-tail-call -->
A value-returning function whose last statement is a diverging call needs no `return` of its own:
control cannot reach the end of the body. Without the fact this is `E3013 missing return statement`.
```maxon
function fatal(tag int)
	panic("fatal {tag}")
end 'fatal'

function pick(b bool) returns int
	if b 'y'
		return 6
	end 'y'
	fatal(9)
end 'pick'

function main() returns ExitCode
	return pick(true) * 7
end 'main'
```
```exitcode
42
```

<!-- test: a-statement-after-a-diverging-call-is-dead-not-refused -->
A statement after a diverging call is unreachable, and is accepted rather than refused — the same
answer a statement after an `if`/`else` whose branches all `return` gets. The `if` here is never
entered, so the dead tail is reached by nothing and `main` answers on the false edge.
⚠ This case is also the guard on the IMPLEMENTATION: the terminator is a SLOT, so a second one
would panic the compiler at `emitTerminator`, and `return 7` below is a second one unless the dead
block after the diverging call is a fresh block of its own.
```maxon
function fatal(tag int)
	panic("fatal {tag}")
end 'fatal'

function main() returns ExitCode
	if false 'never'
		fatal(1)
		return 7
	end 'never'
	return 3
end 'main'
```
```exitcode
3
```

<!-- test: a-body-that-panics-on-only-one-path-does-not-diverge -->
⛔ **THE OVER-MARKING GUARD.** `notFatal` panics on one path and falls off its end on the other, so
it RETURNS and a call to it is not a terminator. If it were marked, `main` would stop at the call
and the second line would never print.
```maxon
function notFatal(b bool)
	if b 'y'
		panic("boom")
	end 'y'
	print("survived\n")
end 'notFatal'

function main() returns ExitCode
	notFatal(false)
	print("back in main\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
survived
back in main
```

<!-- test: a-body-with-a-return-does-not-diverge -->
⛔ **THE OVER-MARKING GUARD, second condition.** `maybeFatal` ends in `panic` but spells a `return`
above it, so it returns on that path. Its result is discarded here — a bare-call statement is a
legal position for a value-returning function — and control must continue past it.
```maxon
function maybeFatal(b bool) returns int
	if b 'y'
		return 5
	end 'y'
	panic("no")
end 'maybeFatal'

function main() returns ExitCode
	maybeFatal(true)
	print("back in main\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
back in main
```

<!-- test: error.a-handler-tail-that-is-not-a-diverging-call-is-still-refused -->
⛔ **THE WIDENING'S BOUNDARY.** The handler's last statement is a call to a function that RETURNS,
so the error path reaches the merge with no value and the `try` cannot stand in a value position.
Unchanged by this feature.
```maxon
typealias Integer = int(i64.min to i64.max)

enum DrainError implements Error
	pipeDied
end 'DrainError'

function note(worker Integer)
	print("worker {worker} died\n")
end 'note'

function drain(fail bool) returns Integer throws DrainError
	if fail 'broken'
		throw DrainError.pipeDied
	end 'broken'
	return 41
end 'drain'

function main() returns ExitCode
	let lines = try drain(false) otherwise (e) 'workerDied'
		note(7)
	end 'workerDied'
	return lines + 1
end 'main'
```
```maxoncstderr
error E3059: <fragment>:20:14: type mismatch: 'a `try` used for its value needs a value on the error path too, but this `otherwise` handler catches the error without producing one (`otherwise ignore`, or a handler block that runs off its end) — give it a fallback value with `otherwise <expr>`, or make every path of the handler terminate (`return`/`throw`/`break`/`continue`)'
```

<!-- test: error.a-one-path-panic-in-a-handler-tail-is-still-refused -->
⛔ **THE OVER-MARKING GUARD, in the position the feature exists for.** The handler's last statement
calls a function that panics on ONE path only, so the error edge can still reach the merge.
```maxon
typealias Integer = int(i64.min to i64.max)

enum DrainError implements Error
	pipeDied
end 'DrainError'

function notFatal(b bool)
	if b 'y'
		panic("boom")
	end 'y'
	print("survived\n")
end 'notFatal'

function drain(fail bool) returns Integer throws DrainError
	if fail 'broken'
		throw DrainError.pipeDied
	end 'broken'
	return 41
end 'drain'

function main() returns ExitCode
	let lines = try drain(false) otherwise (e) 'workerDied'
		notFatal(false)
	end 'workerDied'
	return lines + 1
end 'main'
```
```maxoncstderr
error E3059: <fragment>:23:14: type mismatch: 'a `try` used for its value needs a value on the error path too, but this `otherwise` handler catches the error without producing one (`otherwise ignore`, or a handler block that runs off its end) — give it a fallback value with `otherwise <expr>`, or make every path of the handler terminate (`return`/`throw`/`break`/`continue`)'
```

<!-- test: a-self-recursive-diverging-function -->
The analysis asks nothing about a body's CALLEES, so a function that calls itself cannot send it
round a loop and cannot conclude divergence by circular reasoning. This one recurses to a bottom
that panics, and it is recognized on its own tail alone.
```maxon
function fatal(n int)
	if n > 0 'more'
		fatal(n - 1)
	end 'more'
	panic("bottom at {n}")
end 'fatal'

function main() returns ExitCode
	print("descending\n")
	fatal(2)
end 'main'
```
```exitcode
1
```
```stdout
descending
```
```stderr
panic at a-self-recursive-diverging-function.test:6: bottom at 0
Stack trace:
  in fatal
  in fatal
  in fatal
  in main
  in mrt_start
```
