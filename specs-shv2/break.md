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

The only `specs/break.md` case whose prerequisites are all present at M4b: the
own-label diagnostic for a `continue` inside a comparison-condition loop. Every
PASSING `break`/`continue` case in `specs/break.md` is DEFERRED — each either uses a
bare boolean condition (`while true`, deferred past M4b) or carries enough
simultaneously-distinct SSA values (multiple loop-carried vars, loop-exit phis) to
exceed the placeholder register allocator's 6-register pool. `continue`'s codegen IS
exercised by `while-loops.continue` in `specs-shv2/while-loops.md`; a single-variable
comparison-condition `break` loop compiles and runs correctly, but no such case exists
verbatim in `specs/break.md`. All deferred cases live under `## Deferred`.

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
error E2048: <fragment>:5:12: 'continue' with label 'loop' targets its own loop; use 'continue' without a label, or 'continue' with the label of an outer loop
```

## Deferred

Tests recorded for re-enablement at the milestone that unblocks them. They live in
this `## Deferred` section — NOT `## Tests` — so the spec-test parser (which scans
only `## Tests`, up to the next `## ` heading) never extracts them, and they carry
NO `<!-- test: … -->` marker. To re-enable: move the test up into `## Tests` and
prefix it with its `<!-- test: NAME -->` marker.

### break.in-loop / break.with-if / break.labeled-break-outer / break.labeled-break-triple-nested / break.error-break-own-label

Re-enable once its prerequisites land: boolean-value conditions (`while true`),
deferred past M4b (M4b's `while` condition must be a comparison). Representative case:

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

### break.multiple-conditions

Re-enable once its prerequisites land: the liveness-based register allocator (M5).
Two loop-carried vars plus a `break` (needing a loop-exit phi) and several live
constants exceed the placeholder allocator's 6-register pool.

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

### break.labeled-break-inner / break.labeled-continue-outer / break.labeled-continue-inner / break.labeled-continue-triple-nested

Re-enable once its prerequisites land: the liveness-based register allocator (M5).
These nest two or three comparison-condition loops with multiple loop-carried vars
(and, for the continue cases, loop-body-local `var`s), well past the placeholder
allocator's 6-register pool. Representative case:

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
