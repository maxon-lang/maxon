---
feature: unreachable-code
status: experimental
keywords: [unreachable, dead code, return, throw, panic, break, continue, E3071]
category: statements
---

# Unreachable Code After a Terminating Statement (E3071)

## Documentation

A statement in the **same straight-line block body** immediately after a `return`, `throw`,
`panic(...)`, `break` or `continue` can never be reached: control leaves the block at the
terminator and never comes back to the statement below it. The parser refuses it as **E3071**,
positioned at the first unreachable statement.

### It is a correctness rule in shv2, not only a lint

shv2 stores a block's terminator in a **slot** (`IrBlock.terminator`), not as the last op of the
op list, and `IrModule.setTerminator` **overwrites** that slot. So an accepted statement after a
terminator does not merely emit dead code — if that statement is one that terminates the block
itself (an `if`, a `while`, a `for`, a `match`), its terminator **replaces** the one already
there and the earlier `return`/`break` is silently discarded. MEASURED before this refusal
existed:

| program | answered | should answer |
|---|---|---|
| `return false` then an `if` then `return true` | `true` | `false` |
| `break` then an `if` inside a `while` | `105` | `1` |

Both are byte-identical in the emitted binary to the same program with the terminator deleted.
That is why `break`/`continue` carry the rule here even though the deprecated v1 self-hosted
compiler applied it only to `return`/`throw`/`panic`: in shv2 they are the same defect, and the
reference bootstrap already refuses all five (`specs/break.md` pins the two loop keywords).

### What it deliberately does NOT refuse

Control flow that creates **separate blocks** does not trigger it. A `return` inside an `if` is
the last statement of the `if`'s own block; the statements after the `if`'s `end` sit in the
continuation block and are reachable on the false path. Widening the rule to "this function has
already returned somewhere" would refuse the single most common shape in the language.

## Tests

<!-- test: return-then-if-is-refused -->
The W118 baseline, exactly: a straight-line `return` whose block is then re-terminated by an
`if`. Refused at the `if`, which is the first unreachable statement.
```maxon
function f(x ExitCode) returns ExitCode
	return 0
	if x > 0 'y'
		return 1
	end 'y'
	return 2
end 'f'

function main() returns ExitCode
	return f(1)
end 'main'
```
```maxoncstderr
error E3071: <fragment>:4:2: unreachable code after 'return'
```

<!-- test: break-then-if-is-refused -->
The same defect through `break`. Without the refusal this program printed `105` and returned 0;
the `break` never happened.
```maxon
function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 5 'loop'
		i = i + 1
		if i == 2 'skip'
			break
			if i > 0 'inner'
				total = total + 100
			end 'inner'
		end 'skip'
		total = total + 1
	end 'loop'
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3071: <fragment>:9:4: unreachable code after 'break'
```

<!-- test: unreachable-after-throw -->
`throw` leaves the function just as `return` does, and the message names the keyword the author
wrote.
```maxon
enum LoadError implements Error
	notFound
end 'LoadError'

function load() returns ExitCode throws LoadError
	throw LoadError.notFound
	return 1
end 'load'

function main() returns ExitCode
	return try load() otherwise 0
end 'main'
```
```maxoncstderr
error E3071: <fragment>:8:2: unreachable code after 'throw'
```

<!-- test: unreachable-after-panic -->
```maxon
function main() returns ExitCode
	panic("stop")
	return 1
end 'main'
```
```maxoncstderr
error E3071: <fragment>:4:2: unreachable code after 'panic'
```

<!-- test: terminator-ends-its-own-block -->
The legal half, and the one this rung had to prove still RUNS rather than merely still compiles:
a `return` that is the last statement of an `if` body with ordinary statements after the `if`,
and a `continue` that is the last statement of an `if` body inside a loop with an ordinary
statement after that `if`. Each terminator ends its OWN block; nothing follows it there. Both
exits of `classify` are executed, so neither can mask the other.
```maxon
function classify(x ExitCode) returns ExitCode
	if x > 5 'big'
		return 1
	end 'big'
	var seen = 0
	var i = 0
	while i < 4 'scan'
		i = i + 1
		if i == 2 'hit'
			continue
		end 'hit'
		seen = seen + 1
	end 'scan'
	return seen
end 'classify'

function main() returns ExitCode
	if classify(9) != 1 'earlyReturnLost'
		return 8
	end 'earlyReturnLost'

	return classify(0)
end 'main'
```
```exitcode
3
```
