---
feature: while-loops
status: stable
keywords: while, loop, iteration, control flow
category: control-flow
---
# While Loops

## Documentation

Execute a block of code repeatedly while a condition is true.

**Syntax:**

```maxon
while <condition> 'identifier'
	<statements>
end 'identifier'
```
**Parameters:**
- `condition` - Boolean expression evaluated before each iteration
- `identifier` - String label for the loop block (must match at `end`)

**Example:**

```maxon
var x = 5
var i = 3
while i > 0 'loop'
	x = x + 2
	i = i - 1
end 'loop'
// x is now 11
```
**Notes:**
- Condition is evaluated before each iteration (pre-test loop)
- Block identifier is required and must match at `while` and `end`
- Use `break` to exit the loop early
- Use `continue` to skip to the next iteration
- Infinite loops possible with `while true 'loop'`

## Tests

<!-- test: while-loops.basic -->
```maxon
function main() returns ExitCode
var x = 5
var i = 3
while i > 0 'loop'
x = x + 2
i = i - 1
if i == 0 'check'
break
end 'check'
end 'loop'
return x
end 'main'
```
```exitcode
11
```

<!-- test: while-loops.break -->
```maxon
function main() returns ExitCode
	var x = 5
	while true 'loop'
		x = x + 2
		if x == 11 'check'
			break
		end 'check'
	end 'loop'
	return x
end 'main'
```
```exitcode
11
```

<!-- test: while-loops.zero-iterations -->
```maxon
function main() returns ExitCode
	var x = 10
	while x < 5 'loop'
		x = x + 1
	end 'loop'
	return x
end 'main'
```
```exitcode
10
```

<!-- test: nested-control -->
```maxon
function main() returns ExitCode
	var result = 0
	var i = 0
	while i < 3 'outer'
		var j = 0
		while j < 3 'inner'
			if (i + j) mod 2 == 0 'even'
				result = result + 1
			end 'even'
			j = j + 1
		end 'inner'
		i = i + 1
	end 'outer'
	return result
end 'main'
```
```exitcode
5
```

<!-- test: while-loops.continue -->
```maxon
function main() returns ExitCode
	var sum = 0
	var i = 0
	while i < 5 'loop'
		i = i + 1
		if i == 3 'skip'
			continue
		end 'skip'
		sum = sum + i
	end 'loop'
	return sum
end 'main'
```
```exitcode
12
```

<!-- test: while-loops.counter-survives-a-narrow-ranged-neighbour -->
A loop counter must survive a NARROW RANGED local allocated next to it.

Each variable gets a stack slot, and the slot has to hold the widest access made to it. A local whose
type is a narrow ranged int (`SlotTally` below is `int(0 to 16)`, and the arithmetic over it stays
narrow) once got a FOUR-byte slot while the stores and loads reaching it were EIGHT bytes wide — so the
overrun landed on whatever was allocated next to it. Here that neighbour is `pass`, the loop counter:
every iteration reset it, `pass < 2` never went false, and the loop ran forever.

The guard turns that into a distinguishable exit code rather than a hang, so the case fails loudly
instead of timing out. `99` means the counter was corrupted; `2` is the two passes the loop owes.
```maxon
typealias SlotTally = int(0 to 16)

enum Pair
	first
	second
end 'Pair'

function liveCount(both bool) returns SlotTally
	var n = 0
	for c in Pair.allCases 'walk'
		if c == Pair.first or both 'live'
			n = n + 1
		end 'live'
	end 'walk'
	return n
end 'liveCount'

function main() returns ExitCode
	let scaled = 20 * (liveCount(false) + 1)
	let seed = 100 + scaled

	var guard = 0
	var pass = 0
	while pass < 2 'passes'
		var running = seed
		for c in Pair.allCases 'inner'
			if c == Pair.first 'one'
				running = running + 1
			end 'one'
		end 'inner'
		// ⚠ `running` is NARROW-typed, so it takes a 4-byte store while this read is 8 bytes wide — the
		// high half is slot padding. Checking it here means that padding can never be depended on
		// SILENTLY: if it is ever non-zero this case says 98 instead of quietly computing the right
		// answer. (Measured: exactly one variable in the whole bootstrap suite has this store/load
		// width pair, and it is this one.)
		if running != 141 'paddingLeakedIn'
			return 98
		end 'paddingLeakedIn'

		guard = guard + running
		pass = pass + 1
		if guard > 2000 'runaway'
			return 99
		end 'runaway'
	end 'passes'

	return pass
end 'main'
```
```exitcode
2
```
