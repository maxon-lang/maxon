---
feature: break
status: selfhosted
keywords: [break, continue, loop, control flow, exit, label]
category: control-flow
milestone: M4b
---

# Break and Continue

## Documentation

`break` exits the innermost enclosing loop (control resumes after the loop's `end`);
`continue` jumps to the next iteration. Both accept an optional loop label to target
an OUTER loop (`break 'outer'`). Naming the innermost loop's own label is redundant
(E2048); a `break`/`continue` with no enclosing loop is E2047.

Lowering (M4b): `break` branches to the target loop's EXIT block, `continue` to its
HEADER (a back-edge). Both carry the current loop-carried variable values as block-arg
operands, so the exit's / header's phis see this path's values (see
`specs-shv2/while-loops.md`). The loop's header/exit block ids live on a loop-context
stack the parser threads, mirroring the block-scope stack.

## Tests

The `specs/break.md` cases whose prerequisites are all present: the own-label
diagnostic for a `continue` inside a comparison-condition loop, and the two
unreachable-code diagnostics (E3071, W118) — `break`/`continue` end a straight-line
block exactly as `return`/`throw`/`panic` do, so a statement after either in the same
block is dead code. Every PASSING `break`/`continue` case in `specs/break.md` is
DEFERRED — each either uses a bare boolean condition (`while true`, deferred past M4b)
or carries enough simultaneously-distinct SSA values (multiple loop-carried vars,
loop-exit phis) to exceed the placeholder register allocator's 6-register pool.
`continue`'s codegen IS exercised by `while-loops.continue` in
`specs-shv2/while-loops.md`; a single-variable comparison-condition `break` loop
compiles and runs correctly, but no such case exists verbatim in `specs/break.md`. All
deferred cases live under `## Deferred`.

<!-- test: break.error-continue-own-label -->
Labelling a `continue` with its own (innermost) loop's label is redundant.
```maxon
function main() returns ExitCode
	var x = 0
	while x < 10 'loop'
		x = x + 1
		continue 'loop'
	end 'loop'
	return x
end 'main'
```
```maxoncstderr
error E2048: <fragment>:6:12: 'continue' with label 'loop' targets its own loop; use 'continue' without a label, or 'continue' with the label of an outer loop
```

<!-- test: break.multiple-conditions -->
```maxon
function main() returns ExitCode
	var x = 5
	var count = 0
	while x < 100 'loop'
		x = x + 1
		count = count + 1
		if count == 3 'check'
			break
		end 'check'
	end 'loop'
	return x
end 'main'
```
```exitcode
8
```

<!-- test: break.error-unreachable-after-break -->
Error: `break` leaves the block unconditionally, so a statement after it in the
same block is unreachable — the same rule `return`/`throw`/`panic` already carry.
It is also what keeps the block well formed: `break` emits its branch and leaves
the parser positioned on the block it just terminated, so a statement accepted
here would append its ops AFTER a terminator, and the successor walk reads only
the last op.
```maxon
function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 5 'loop'
		i = i + 1
		if i == 2 'skip'
			break
			total = total + 100
		end 'skip'
		total = total + 1
	end 'loop'
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3071: specs/fragments/break/break.error-unreachable-after-break.test:9:4: unreachable code after 'break'
```

<!-- test: break.error-unreachable-after-continue -->
Error: the same for `continue`, which branches to the loop header.
```maxon
function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 5 'loop'
		i = i + 1
		if i == 2 'skip'
			continue
			total = total + 100
		end 'skip'
		total = total + 1
	end 'loop'
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3071: specs/fragments/break/break.error-unreachable-after-continue.test:9:4: unreachable code after 'continue'
```


## Deferred

Tests recorded for re-enablement at the milestone that unblocks them. They live in
this `## Deferred` section — NOT `## Tests` — so the spec-test parser (which scans
only `## Tests`, up to the next `## ` heading) never extracts them, and they carry
NO `<!-- test: … -->` marker. To re-enable: move the test up into `## Tests` and
prefix it with its `<!-- test: NAME -->` marker.

### break.in-loop / break.with-if / break.labeled-break-outer / break.labeled-break-triple-nested / break.error-break-own-label

Their LANGUAGE prerequisite — boolean-value conditions (`while true`) — has LANDED,
and the representative case below compiles and returns 5. What still blocks them is
this file: it is an ADAPTATION of `specs/break.md`, not a port of it, so it does not
carry their verbatim sources. Re-enable them by porting `specs/break.md` itself (the
corpus port), which is where those sources live. Representative case:

```maxon
function main() returns ExitCode
	var x = 0
	while true 'loop'
		x = x + 1
		if x == 5 'check'
			break
		end 'check'
	end 'loop'
	return x
end 'main'
```
```exitcode
5
```

### break.labeled-break-inner / break.labeled-continue-outer / break.labeled-continue-inner / break.labeled-continue-triple-nested

Their prerequisite — the liveness-based register allocator — has LANDED (M5), and the
representative case below compiles and returns 5. Like the group above, what still
blocks them is that this file is an ADAPTATION of `specs/break.md` rather than a port
of it, so it does not carry their verbatim sources. Re-enable them by porting
`specs/break.md` itself. Representative case:

```maxon
function main() returns ExitCode
	var x = 0
	var y = 0
	while x < 5 'outer'
		x = x + 1
		while y < 10 'inner'
			y = y + 1
			if y == 3 'check'
				break
			end 'check'
		end 'inner'
	end 'outer'
	return x
end 'main'
```
```exitcode
5
```
