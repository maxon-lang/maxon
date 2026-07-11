---
feature: assignment
status: selfhosted
keywords: [assignment, equals, mutation, reassignment, var]
category: statements
milestone: M4b
---

# Assignment Statement

## Documentation

The assignment operator `=` updates the value of a mutable (`var`) binding:

```maxon
variable = expression
```

M4b adds reassignment on top of M2's `let`/`var` bindings. A `var` may be
reassigned any number of times; each write rebinds the variable's current value,
so a later reference sees the new value (`x = x + 2` reads the old `x`, then
rebinds it to the sum). A `let` binding is immutable — assigning to it is E2013.

Lowering: the value model is **on-the-fly SSA**. A reassignment is a rebinding of
the variable's current ValueId (no stack slot, no store op), so straight-line code
needs no phis. Where a `var` is reassigned across a control-flow merge — the header
and exit of a `while` loop — the parser inserts block-arg **phis**, which the
Std-tier phi-elimination pass resolves into coalesced values plus register moves
before the backend (see `specs-shv2/while-loops.md`).

## Tests

The M4b slice of `specs/assignment.md`: straight-line reassignment, chained
reassignment across two variables, and the canonical accumulator loop. All three
fit the placeholder register allocator's pool.

<!-- test: basic-assignment -->
```maxon
function main() returns ExitCode
	var x = 3
	x = x + 2
	return x
end 'main'
```
```exitcode
5
```

<!-- test: multiple-assignments -->
```maxon
function main() returns ExitCode
	var x = 10
	var y = 20
	x = y
	y = 30
	return x + y
end 'main'
```
```exitcode
50
```

<!-- test: assignment-in-loop -->
```maxon
function main() returns ExitCode
	var sum = 0
	var i = 1
	while i <= 5 'loop'
		sum = sum + i
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
15
```

<!-- test: assign-to-let-error -->
Assigning to a `let` binding is rejected — only `var` bindings are mutable.
```maxon
function main() returns ExitCode
	let x = 3
	x = 5
	return x
end 'main'
```
```maxoncstderr
error E2013: <fragment>:3:2: cannot assign to immutable variable: 'x'
```
