---
feature: for-destructuring-pattern
keywords: [for, destructuring, tuple, pattern, map, discard, diagnostics]
category: statements
---

# The `for` header's destructuring pattern

## Documentation

`for (key, value) in m` and `let (x, y) = t` spell **one pattern**. The parser reads both through
`parseDestructuringNames` and checks both against `requireDestructurableTuple`, so `_` discards a
position in both and both refuse a one-name pattern with the same sentence — which is deliberate,
and is why the loop header needs no rules of its own.

**What is NOT shared is the NOUNS the refusals are composed from**, and that is the whole subject
of this file. A `let` binding has a *binding*, an *initializer* and a *right-hand side*. A `for`
header has **none of the three**: it has a loop pattern and a source ELEMENT. Held to one
hard-coded set of nouns, `for (a, b) in xs` over an int array reported

> a destructuring **binding** of a 'int' **initializer** (only a TUPLE can be destructured —
> `let (x, y) = …` needs a **right-hand side** that is a tuple)

— a sentence describing a construct the author did not write, on the refusal a `for` header
reaches FIRST. The nouns are now the caller's, exactly as `parseHashTableColumnArg` takes its
subject, and the cases below pin one sentence per construct so the two cannot collapse back into
one.

### Authored, not ported

`/specs/tuples.md` pins the `let` spellings of both sentences
(`error.destructure-of-a-non-tuple`, `error.destructure-arity-mismatch`) and they stay
byte-identical — that is what makes them the CONTROL for these. It has no case anywhere for the
`for` spelling of either, because until `Map` landed there was no source whose element was a
tuple, so the `for` arm of `requireDestructurableTuple` was unreachable and its wording was never
read by anyone.

## Tests

<!-- test: non-tuple-element -->
An `Array with int` yields an int per trip, and an int cannot be destructured. The refusal blames
the pattern and the source's ELEMENT — no binding, no initializer, no right-hand side — and it is
positioned on the `for` keyword, where the author decided how many names to write.
```maxon
function main() returns ExitCode
	let xs = [1, 2, 3]
	for (a, b) in xs 'each'
		print("{a} {b}")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:2: Unsupported: a destructuring `for` pattern of a 'int' element (only a TUPLE can be destructured — `for (k, v) in …` needs a source whose element is a tuple)
```

<!-- test: arity-mismatch -->
A map's element is a `(key, value)` pair, so three names have nothing to bind. Same construct
noun, same anchor; only the count clause differs, and that half is shared with `let` verbatim.
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 20]
	for (a, b, c) in m 'each'
		print("{a} {b} {c}")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:2: Unsupported: a destructuring `for` pattern of 3 names against a tuple of 2 elements (every element must be named)
```

<!-- test: one-name-in-parens -->
A one-name pattern is refused before the source is even parsed, so `for (x) in …` is read as the
malformed destructure it is rather than as a type error about whatever follows. **This sentence
is deliberately the `let` one for BOTH constructs** — it is the arity rule of the pattern itself,
which is one pattern, and a `for` header that wants one name simply omits the parens. It is
pinned here so that staying `let`-worded is a recorded decision rather than the next thing
someone "fixes" for consistency.
```maxon
function main() returns ExitCode
	let xs = [1, 2, 3]
	for (x) in xs 'each'
		print("{x}")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:2: Unsupported: a destructuring binding of 1 name(s) (a tuple has at least 2 elements, so `let (x) = …` names no destructurable shape — write `let x = …`)
```

<!-- test: discard-a-position -->
`_` discards a position in a `for` header exactly as it does in a `let`, and a discarded position
binds no name at all — so it is exempt from the unused-binding check that `v` must satisfy. The
sum proves the surviving position still reads the pair's VALUE and not its key.
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 20]
	var total = 0
	for (_, v) in m 'each'
		total = total + v
	end 'each'
	return total
end 'main'
```
```exitcode
30
```
