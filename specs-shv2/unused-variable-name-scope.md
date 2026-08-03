---
feature: unused-variable-name-scope
status: stable
keywords: [variables, unused, diagnostics, errors, for, scope]
category: diagnostics
---

# The Scope of "Referenced" in the Unused-Variable Check

## Documentation

E3012 asks whether a NAME was mentioned in the enclosing function — not whether a
particular BINDING of that name was. Two sequential `for` loops over the same
variable name mint two distinct bindings, and a mention in the SECOND loop excuses
the FIRST: the author did name the variable, and the diagnostic's job is to find
names nothing in the body refers to.

### Why this file is AUTHORED rather than ported

The rule is not pinned by any case in `/specs`. It is pinned only INCIDENTALLY, by
two programs that were written to test something else and happen to depend on it:

- `specs/array-sort.md`'s `driftsort-large-sqrt-cross-check` — a
  `for i in 0 upto 5000 'fill'` whose body never reads `i`, legal only because a
  later `for i in 0 upto a.count() 'walk'` in the same function does `a.get(i)`.
- `specs/generic-module-merge-pattern.md`'s `dual-specialize-inner-array` — the
  same shape.

Neither file is ported to `specs-shv2`. Without the case below, a PER-BINDING
implementation of the check passes the whole suite and then refuses real corpus
programs the reference compilers accept. The second case is the negative control:
it holds the check alive, so the first case cannot pass by the diagnostic being
dead.

### A CAPTURE crosses the closure boundary; a closure's own PARAMETER does not

A closure body is parsed inline, under its own mention list — a closure's private
name may not excuse a declaration of that name in the enclosing function. What DOES
cross back is what the closure CAPTURED: reading an enclosing binding is a mention
of that name in the enclosing function, exactly as writing it inline would be.

The two cases below are that boundary from both sides, and they are a PAIR: the
first is red if the captured names are not carried back over the restore, the second
is red if the closure's list is carried back WHOLESALE instead. Neither direction is
reachable from the two loop cases above, because neither of those parses a closure at
all — the mechanism (a save, a CLEAR, a restore, and a re-push of the capture names)
is otherwise pinned by nothing in the suite.

## Tests

<!-- test: a-later-loop-of-the-same-name-excuses-an-earlier-one -->
```maxon
function main() returns ExitCode
	var total = 0
	for i in 0 upto 3 'a'
		total = total + 1
	end 'a'
	for i in 0 upto 3 'b'
		total = total + i
	end 'b'
	return total
end 'main'
```
```exitcode
6
```

<!-- test: one-loop-of-that-name-and-no-mention-is-still-refused -->
```maxon
function main() returns ExitCode
	var total = 0
	for i in 0 upto 3 'a'
		total = total + 1
	end 'a'
	return total
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variable-name-scope/one-loop-of-that-name-and-no-mention-is-still-refused.test:4:6: unused variable: 'i'
```

<!-- test: a-closure-capture-of-that-name-excuses-a-loop-binding -->
The only mention of `i` in `main` is the one INSIDE the closure, which reaches the
enclosing binding as a CAPTURE. `4 + 1 + 3 = 9`.
```maxon
function main() returns ExitCode
	let i = 5
	let f = function() gives i + 1
	var total = f()
	for i in 0 upto 3 'a'
		total = total + 1
	end 'a'
	return total
end 'main'
```
```exitcode
9
```

<!-- test: error.a-closure-parameter-of-that-name-does-not-excuse-it -->
The negative control for the case above, and the reason the closure's list is CLEARED
rather than shared: `i` here is the closure's OWN parameter, a name the enclosing
function never mentions, so the loop binding is still unused. Measured on the oracle,
which reports the same line and column.
```maxon
typealias Integer = int(i64.min to i64.max)
function main() returns ExitCode
	var total = 0
	for i in 0 upto 3 'a'
		total = total + 1
	end 'a'
	let f = function(i Integer) gives i + 1
	total = total + f(2)
	return total
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variable-name-scope/error.a-closure-parameter-of-that-name-does-not-excuse-it.test:5:6: unused variable: 'i'
```
