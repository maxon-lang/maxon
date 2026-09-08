---
feature: try-fork-pending-temporaries
status: experimental
keywords: [try, otherwise, memory, ownership, drop, leak, temporaries, struct-literal, fork]
category: memory-safety
---

# A `try` Forks in the Middle of an Expression

## Documentation

`try … otherwise …` is an EXPRESSION, so it can sit anywhere a value can — a field of
a struct literal, an argument of a call, an element of an array literal. When it does,
the enclosing expression is only half-built at the point the error flag is tested, and
the operands already evaluated are values that expression has **yet to consume**:

```maxon
return Conf{argv: StrArray.create(), n: try mayFail(n) otherwise panic("…")}
```

`StrArray.create()` runs first and owns its array. The `try` then splits control two
ways, and the array is stored into the struct several ops later, on whichever edge
reaches the store.

The rule is the one every branching construct in this compiler follows — *a temporary
is released in a block its definition dominates, once per path leaving the region that
built it* (`block-condition-temporaries.md`) — but a `try` fork answers it differently
from an `if` condition, because an `if` condition is FINISHED when it branches and this
expression is not. So the question each edge is asked is:

**does this edge go on to finish the enclosing expression?**

- the **ok** edge always does, so it releases nothing at the fork and still owes the
  temporaries at the end of its statement;
- the **error** edge does too whenever the handler yields a value (`otherwise 7`) or is
  a block that falls through — both merge into the continuation, where one release
  covers both paths;
- an **abandoning** path never reaches the rest of the expression, and releases them on
  its way out.

⚠ **"Abandoning" is a property of a PATH, not of the handler.** A block handler that falls
through at its `end` can still leave by `return` / `throw` / `break` / `continue` on an inner
path, so one classification per handler is a lie about the other half of it. The fork's
temporaries are therefore suspended as **anonymous owned bindings** for the duration of the
handler — the same enrolment `parseOtherwiseHandler` already makes for the caught error box —
so every exit releases them through the scope-drop machinery that already knows what a path
leaving early owes, and a `break` whose target lies INSIDE the handler releases nothing,
because that target's own floor is above them.

Releasing them on the ok edge instead is a **use-after-free**: the value is decref'd at
the head of `tryok` and read by the store that follows it. Releasing them on an error
edge that merges is the same fault one path over. Releasing them on neither is a leak.

## Tests

<!-- test: a-struct-literal-field-survives-a-diverging-try -->
The headline case. `argv` is built before the fork and stored after it, and the handler
`panic`s — so the error edge abandons the expression while the ok edge completes it.
Answers `1 + 3`.
```maxon
typealias StrArray = Array with String
typealias Integer = int(i64.min to i64.max)

enum Boom implements Error
	bad
end 'Boom'

function mayFail(n Integer) returns Integer throws Boom
	if n > 100 'big'
		throw Boom.bad
	end 'big'
	return n
end 'mayFail'

type Conf
	export var argv as StrArray
	export var n as Integer

	export static function create(n Integer) returns Conf
		return Conf{argv: StrArray.create(), n: try mayFail(n) otherwise panic("mayFail cannot fail for a small n")}
	end 'create'
end 'Conf'

function main() returns ExitCode
	var c = Conf.create(3)
	c.argv.push("a")
	return c.argv.count() + c.n as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: a-value-otherwise-finishes-the-expression-on-both-edges -->
`otherwise 7` merges, so the struct is built on the error path too — with the SAME
array the ok path would have used. Neither edge may release it at the fork. The ok
`Conf` answers `1 + 3`, the failing one `2 + 7`.
```maxon
typealias StrArray = Array with String
typealias Integer = int(i64.min to i64.max)

enum Boom implements Error
	bad
end 'Boom'

function mayFail(n Integer) returns Integer throws Boom
	if n > 100 'big'
		throw Boom.bad
	end 'big'
	return n
end 'mayFail'

type Conf
	export var argv as StrArray
	export var n as Integer

	export static function create(n Integer) returns Conf
		return Conf{argv: StrArray.create(), n: try mayFail(n) otherwise 7}
	end 'create'
end 'Conf'

function main() returns ExitCode
	var ok = Conf.create(3)
	ok.argv.push("a")
	var failed = Conf.create(900)
	failed.argv.push("b")
	failed.argv.push("c")
	return ((ok.argv.count() as Integer) + ok.n + (failed.argv.count() as Integer) + failed.n) as ExitCode
end 'main'
```
```exitcode
13
```

<!-- test: an-abandoning-handler-releases-them-once-on-its-own-edge -->
The error path TAKEN, with a handler that returns. The array built before the fork is
never stored, so the abandoned edge owes it exactly one release — the leak gate (exit
101) is what makes "none" fail, and a second release faults.
```maxon
typealias StrArray = Array with String
typealias Integer = int(i64.min to i64.max)

enum Boom implements Error
	bad
end 'Boom'

function mayFail(n Integer) returns Integer throws Boom
	if n > 100 'big'
		throw Boom.bad
	end 'big'
	return n
end 'mayFail'

type Conf
	export var argv as StrArray
	export var n as Integer

	export static function create(n Integer) returns Conf throws Boom
		return Conf{argv: StrArray.create(), n: try mayFail(n) otherwise return Conf{argv: StrArray.create(), n: 0}}
	end 'create'
end 'Conf'

function main() returns ExitCode
	let c = try Conf.create(900) otherwise return 9
	return (c.n + (c.argv.count() as Integer)) as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-call-argument-evaluated-before-the-fork -->
The same shape one construct over: an array literal is an argument, and the `try` that
follows it forks before the call consumes it. Both edges are exercised — `2 + 5` on the
error path, `1 + 2` on the ok path.
```maxon
typealias StrArray = Array with String
typealias Integer = int(i64.min to i64.max)

enum Boom implements Error
	bad
end 'Boom'

function mayFail(n Integer) returns Integer throws Boom
	if n > 100 'big'
		throw Boom.bad
	end 'big'
	return n
end 'mayFail'

function take(argv StrArray, n Integer) returns Integer
	return (argv.count() as Integer) + n
end 'take'

function main() returns ExitCode
	let a = take(["x", "y"], n: try mayFail(900) otherwise 5)
	let b = take(["z"], n: try mayFail(2) otherwise 5)
	return (a + b) as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: an-inner-return-in-a-fell-through-handler-releases-them -->
The polarity a per-handler classification cannot see: the handler FALLS THROUGH at its `end`
and still leaves by `return` on an inner path. `run(900)` takes the inner return, `run(200)`
takes the error edge and falls through to the merge, `run(1)` never errors — so one program
walks all three paths and the leak gate sees any of them released twice or not at all.
Answers `7 + 1 + 1`.
```maxon
typealias StrArray = Array with String
typealias Integer = int(i64.min to i64.max)

enum Boom implements Error
	bad
end 'Boom'

function consume(argv StrArray, n Integer) returns Integer throws Boom
	if n > 100 'big'
		throw Boom.bad
	end 'big'
	return (argv.count() as Integer) + n
end 'consume'

function run(n Integer) returns Integer
	try consume(["x", "y"], n: n) otherwise 'h'
		if n > 500 'deep'
			return 7
		end 'deep'
	end 'h'
	return 1
end 'run'

function main() returns ExitCode
	return (run(900) + run(200) + run(1)) as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: an-inner-throw-in-a-fell-through-handler-releases-them -->
The same shape leaving by `throw` rather than `return`, so the two hand-off exits are pinned
apart. Answers `3 + 1`.
```maxon
typealias StrArray = Array with String
typealias Integer = int(i64.min to i64.max)

enum Boom implements Error
	bad
end 'Boom'

function consume(argv StrArray, n Integer) returns Integer throws Boom
	if n > 100 'big'
		throw Boom.bad
	end 'big'
	return argv.count() + n
end 'consume'

function run(n Integer) returns Integer throws Boom
	try consume(["x", "y"], n: n) otherwise 'h'
		if n > 500 'deep'
			throw Boom.bad
		end 'deep'
	end 'h'
	return 1
end 'run'

function main() returns ExitCode
	let a = try run(900) otherwise 3
	let b = try run(200) otherwise 3
	return (a + b) as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: an-inner-break-in-a-fell-through-handler-releases-them -->
`break` leaves the enclosing expression too, and it is the exit whose destination decides the
answer: this one targets a loop OUTSIDE the `try`, so the fork's temporaries are below that
loop's floor and are released on the way out. A `break` to a loop opened INSIDE the handler
would be above it and must release nothing — the same floor answers both. Three iterations:
one clean, one erroring and falling through, one erroring and breaking, so `seen` is `2`.
```maxon
typealias StrArray = Array with String
typealias Integer = int(i64.min to i64.max)

enum Boom implements Error
	bad
end 'Boom'

function consume(argv StrArray, n Integer) returns Integer throws Boom
	if n > 100 'big'
		throw Boom.bad
	end 'big'
	return argv.count() + n
end 'consume'

function run(limit Integer) returns Integer
	var seen = 0 as Integer
	var i = 0 as Integer
	while i < limit 'loop'
		try consume(["x", "y"], n: i * 400) otherwise 'h'
			if i > 1 'deep'
				break
			end 'deep'
		end 'h'
		seen = seen + 1
		i = i + 1
	end 'loop'
	return seen
end 'run'

function main() returns ExitCode
	return run(9) as ExitCode
end 'main'
```
```exitcode
2
```
