---
feature: block-condition-temporaries
status: experimental
keywords: [memory, ownership, drop, leak, if, while, match, ternary, short-circuit, dominance]
category: memory-safety
---

# A Branching Construct Owns the Temporaries Its Condition Builds

## Documentation

A condition, a scrutinee, and a short-circuit's right-hand side are ordinary
expressions, so any of them may build an owned temporary:

```maxon
if [10, 20, 30].count() == 99 'x'
	flag = 1
end 'x'
```

That array is owned by nobody once `count()` has read it, so exactly one
`__mm_decref` must run for it — **on every path, and on no path twice.** Two
distinct ways to get that wrong meet here:

- **Drop on too few paths ⇒ a leak.** The array above is live on the true edge
  and the false edge alike. Releasing it inside the `if`'s body releases it only
  when the body runs.
- **Drop on a path that never built it ⇒ a release of a value that does not
  exist.** A short-circuit's right-hand side, a ternary's arms, and a `match`
  expression's arms each run on *one* path only. A drop placed after the merge
  sits on paths where the value was never created.

Both are the same rule seen from two sides, and the rule is dominance: **a
temporary is released in a block its definition dominates, once per path leaving
the region that built it.** Each branching construct therefore settles its own
temporaries at the point where control is still single-threaded:

| construct | where its temporaries are released |
|---|---|
| `if` / `else if` | the condition's exit block, before the two-way branch |
| `while` | the condition's exit block — so a re-evaluated condition releases **per iteration** |
| `match` (statement and expression) | after the scrutinee, before the first case test |
| `a if c else b` | the condition's exit block, before the two-way branch; each arm at its own end |
| `and` / `or` | the right-hand side's exit block, before it branches to the merge |

`while` is the case that must be released per iteration rather than once: its
condition runs again on every trip, so a release hoisted to the loop exit would
accumulate one live allocation per iteration.

## Tests

<!-- test: if-condition-temp-false-path -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var flag = 0 as Integer
	if [10, 20, 30].count() == 99 'x'
		flag = 1
	end 'x'
	return flag
end 'main'
```
```exitcode
0
```

<!-- test: if-condition-temp-true-path -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var flag = 0 as Integer
	if [10, 20, 30].count() == 3 'x'
		flag = 1
	end 'x'
	return flag
end 'main'
```
```exitcode
1
```

<!-- test: else-if-condition-temp -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var flag = 0 as Integer
	if [1].count() == 99 'a'
		flag = 1
	end 'a' else if [1, 2].count() == 99 'b'
		flag = 2
	end 'b' else 'c'
		flag = 3
	end 'c'
	return flag
end 'main'
```
```exitcode
3
```

<!-- test: while-condition-temp-per-iteration -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var i = 0 as Integer
	while i < [1, 2, 3].count() 'loop'
		i = i + 1
	end 'loop'
	return i
end 'main'
```
```exitcode
3
```

<!-- test: if-condition-temp-inside-a-loop -->

500 iterations, each building and releasing one array in the `if`'s condition.
A release that runs on only one edge leaks 500 allocations; the leak gate reports
that as exit 101 regardless of the returned value.

```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var n = 0 as Integer
	var i = 0 as Integer
	while i < 500 'loop'
		if [1, 2, 3].count() == 3 'x'
			n = n + 1
		end 'x'
		i = i + 1
	end 'loop'
	return n / 100
end 'main'
```
```exitcode
5
```

<!-- test: match-statement-scrutinee-temp -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var flag = 0 as Integer
	match [1, 2, 3].count() 'm'
		1 then flag = 1
		3 then flag = 7
		default panic("no")
	end 'm'
	return flag
end 'main'
```
```exitcode
7
```

<!-- test: match-expression-arm-temp -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let n = 2 as Integer
	let v = match n 'm'
		1 gives [1, 2].count()
		2 gives [3, 4, 5].count()
		default panic("no")
	end 'm'
	return v
end 'main'
```
```exitcode
3
```

<!-- test: ternary-arm-temp-false -->
```maxon
function main() returns ExitCode
	let c = false
	let v = [1, 2].count() if c else [3, 4, 5].count()
	return v
end 'main'
```
```exitcode
3
```

<!-- test: ternary-arm-temp-true -->
```maxon
function main() returns ExitCode
	let c = true
	let v = [1, 2].count() if c else [3, 4, 5].count()
	return v
end 'main'
```
```exitcode
2
```

<!-- test: short-circuit-or-skips-rhs-temp -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var flag = 0 as Integer
	if [1, 2, 3].count() == 3 or [4, 5].count() == 2 'x'
		flag = 1
	end 'x'
	return flag
end 'main'
```
```exitcode
1
```

<!-- test: short-circuit-and-skips-rhs-temp -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var flag = 0 as Integer
	if [1, 2, 3].count() == 99 and [4, 5].count() == 2 'x'
		flag = 1
	end 'x'
	return flag
end 'main'
```
```exitcode
0
```

<!-- test: if-inside-a-try-handler -->

The crossing of the two rules: the `try` fork's temporaries are released once per
edge, and the `if` nested inside the handler releases its own condition's
temporary without disturbing them.

```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var flag = 0 as Integer
	try [10, 20, 30].get(9) otherwise 'h'
		if [1, 2].count() == 2 'y'
			flag = 4
		end 'y'
	end 'h'
	return flag
end 'main'
```
```exitcode
4
```

<!-- test: try-in-a-while-condition -->

A `try` whose own fork temporaries are re-created on every trip round the loop,
inside a condition that is itself re-evaluated per iteration.

```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var i = 0 as Integer
	while (try [10, 20, 30].get(i) otherwise 0) > 0 'loop'
		i = i + 1
	end 'loop'
	return i
end 'main'
```
```exitcode
3
```

<!-- test: map-literal-temporary-in-value-position -->

⛔⛔ **EVERY CASE ABOVE BUILDS ITS TEMPORARY FROM AN *ARRAY* LITERAL, AND THAT GAP SHIPPED A DOUBLE FREE**
(found at the `W105` review). A `[k: v]` literal is the other container born through a bracket, and
`Parser.parseMapLiteralBody` tracked its create result as an owned temporary on top of the enrolment
`emitCall` had already made — one record, two `__destruct_Map_<K>_<V>` calls. The program below exited
**0xC0000005** against the bootstrap oracle's **3**.

⚠ **IT HID BEHIND THE *BOUND* FORM, WHICH IS WHY THE WHOLE SUITE WAS GREEN OVER IT.**
`removeFromPendingTemps` strips ALL occurrences of a value, so `let m = [1: 10]` cancels both enrolments
and answers correctly — and every map literal in `specs-shv2/map.md` is bound. Only a literal left as a
TEMPORARY reaches scope exit still holding two. The three cases here are that shape, one per construct
this spec's table covers.

```maxon
function main() returns ExitCode
	return [1: 10, 2: 20, 3: 30].count() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: map-literal-temporary-in-a-condition -->

The `if` row of this spec's table, over a map rather than an array: the literal is live on both edges of
the branch and must be released exactly once before it.

```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var flag = 0 as Integer
	if [1: 10, 2: 20].count() == 2 'x'
		flag = 5
	end 'x'
	return flag
end 'main'
```
```exitcode
5
```

<!-- test: map-literal-temporary-with-managed-columns-does-not-leak -->

⭐ **THE OTHER HALF OF THE SAME RULE, AND THE ONE A DOUBLE FREE CANNOT BE MISTAKEN FOR.** The fix must
release the record ONCE — not twice (the crash above) and not zero times. Both columns are heap `String`s
and the loop builds two hundred of them, so a missed release exits **101** and a second release faults;
answering `4` is the only outcome that is neither.

```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var total = 0 as Integer
	for i in 0 upto 200 'spin'
		total = total + i - i + ["alpha a fairly long heap string": "beta another long heap string", "gamma a third long heap string": "delta a fourth long heap string"].count()
	end 'spin'
	return (total / 100) as ExitCode
end 'main'
```
```exitcode
4
```
