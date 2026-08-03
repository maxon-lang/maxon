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
