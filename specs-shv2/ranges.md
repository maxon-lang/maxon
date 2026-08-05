---
feature: ranges
status: experimental
keywords: [range, to, upto, iteration]
category: control-flow
---

## Documentation

# Ranges

Ranges create sequences of values that can be iterated over. They use the `to` (inclusive) and `upto` (exclusive upper bound) keywords.

**Inclusive range** — includes both endpoints:

```text
for i in 1 to 5 'loop'   // iterates: 1, 2, 3, 4, 5
    print("{i}")
end 'loop'
```

**Exclusive range** — excludes the upper bound:

```text
for i in 1 upto 5 'loop'   // iterates: 1, 2, 3, 4
    print("{i}")
end 'loop'
```

Range expressions are supported for `int` and `Character` values.

### Range Types

The standard library defines two range types:

- `Range uses Bound` — inclusive range (`start to end`), implements `Iterable with Bound`
- `OpenRange uses Bound` — exclusive upper bound (`start upto end`), implements `Iterable with Bound`

## Tests

<!-- test: ranges.basic-inclusive -->
```maxon
function main() returns ExitCode
		var sum = 0
		for i in 1 to 5 'loop'
				sum = sum + i
		end 'loop'
		return sum
end 'main'
```
```exitcode
15
```

<!-- test: ranges.basic-exclusive -->
```maxon
function main() returns ExitCode
		var sum = 0
		for i in 1 upto 5 'loop'
				sum = sum + i
		end 'loop'
		return sum
end 'main'
```
```exitcode
10
```

<!-- test: ranges.zero-start -->
```maxon
function main() returns ExitCode
		var sum = 0
		for i in 0 to 3 'loop'
				sum = sum + i
		end 'loop'
		return sum
end 'main'
```
```exitcode
6
```

<!-- test: ranges.single-element-inclusive -->
```maxon
function main() returns ExitCode
		var count = 0
		for _ in 5 to 5 'loop'
				count = count + 1
		end 'loop'
		return count
end 'main'
```
```exitcode
1
```

<!-- test: ranges.single-element-exclusive -->
```maxon
function main() returns ExitCode
		var count = 0
		for _ in 5 upto 6 'loop'
				count = count + 1
		end 'loop'
		return count
end 'main'
```
```exitcode
1
```

<!-- test: ranges.empty-inclusive -->
```maxon
function main() returns ExitCode
		var count = 0
		for _ in 5 to 3 'loop'
				count = count + 1
		end 'loop'
		return count
end 'main'
```
```exitcode
0
```

<!-- test: ranges.empty-exclusive -->
```maxon
function main() returns ExitCode
		var count = 0
		for _ in 5 upto 5 'loop'
				count = count + 1
		end 'loop'
		return count
end 'main'
```
```exitcode
0
```

<!-- test: ranges.variable-bounds -->
```maxon
function main() returns ExitCode
		let start = 2
		let finish = 4
		var sum = 0
		for i in start to finish 'loop'
				sum = sum + i
		end 'loop'
		return sum
end 'main'
```
```exitcode
9
```

<!-- test: ranges.expression-bounds -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getStart() returns Integer
		return 1
end 'getStart'

function getEnd() returns Integer
		return 4
end 'getEnd'

function main() returns ExitCode
		var sum = 0
		for i in getStart() to getEnd() 'loop'
				sum = sum + i
		end 'loop'
		return sum
end 'main'
```
```exitcode
10
```

<!-- test: ranges.negative-bounds -->
```maxon
function main() returns ExitCode
		var sum = 0
		for i in -2 to 2 'loop'
				sum = sum + i
		end 'loop'
		return sum
end 'main'
```
```exitcode
0
```

<!-- test: ranges.break-in-range -->
```maxon
function main() returns ExitCode
		var last = 0
		for i in 1 to 100 'loop'
				last = i
				if i == 5 'done'
						break
				end 'done'
		end 'loop'
		return last
end 'main'
```
```exitcode
5
```

<!-- test: ranges.continue-in-range -->
```maxon
function main() returns ExitCode
		var sum = 0
		for i in 1 to 10 'loop'
				if i mod 2 == 0 'skip'
						continue
				end 'skip'
				sum = sum + i
		end 'loop'
		return sum
end 'main'
```
```exitcode
25
```

<!-- test: ranges.nested-ranges -->
```maxon
function main() returns ExitCode
		var sum = 0
		for i in 1 to 3 'outer'
				for j in 1 to 3 'inner'
						sum = sum + i * j
				end 'inner'
		end 'outer'
		return sum
end 'main'
```
```exitcode
36
```

<!-- test: ranges.large-range -->
```maxon
function main() returns ExitCode
		var sum = 0
		for _ in 1 to 1000 'loop'
				sum = sum + 1
		end 'loop'
		print("{sum}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
1000
```

<!-- test: ranges.character-range -->
<!-- CHARACTER RANGES rung (re-attributed from P1.8b, 2026-07-28) — shv2 has no `Character` type (`Parser.maxon` says so in its own words); a char literal is an INT. ⚠ MEASURED: this case PASSES anyway, because it only COUNTS the iterations and 'a'..'z' as codepoints counts 26 the same way. Its twin `character-range-print` — which looks at the value — does not. Left disabled deliberately: enabling it would claim a mechanism that is not here (the P1.7 slice-1 lesson) -->
```maxon
function main() returns ExitCode
		var count = 0
		for _ in 'a' to 'z' 'loop'
				count = count + 1
		end 'loop'
		return count
end 'main'
```
```exitcode
26
```

<!-- test: ranges.character-range-print -->
<!-- CHARACTER RANGES rung (re-attributed from P1.8, 2026-07-28) — `'a' upto 'f'` is a range of INTEGERS in shv2: a single-byte character
     literal materializes as an integer literal (`decodeCharLiteral`'s `byte` arm), so the loop variable
     is an int and prints as one. ⚠ MEASURED at P1.8 Slice E, on the enabled case: `97 98 99 100 101`
     where the spec wants `a b c d e`. Its twin `ranges.character-range` passes over the identical
     construct because it only COUNTS the trips — see the note there. Unblocking needs a range whose
     ELEMENT TYPE is `Character`, which is a range question and not a String-method one -->
```maxon
function main() returns ExitCode
		for c in 'a' to 'e' 'loop'
				print("{c}\n")
		end 'loop'
		return 0
end 'main'
```
```exitcode
0
```
```stdout
a
b
c
d
e
```

<!-- disabled-test: ranges.create-iterator -->
<!-- NOT the range gap, which this case never reaches. MEASURED: `E2015 Unsupported: `try` must be applied to a call — `try f(…)`, `try obj.method(…)`, or `try await p` (got '(')` — a parser restriction on `try` over a parenthesised operand. The first-class `Range` VALUE + iterator protocol is the blocker BEHIND it, and stays unmeasured until `try` accepts this shape (its siblings below DO get the stated error) -->
A range used outside a for-in header is a first-class value with `createIterator()`.
```maxon
function main() returns ExitCode
		let it = try (1 upto 4).createIterator() otherwise return 99
		var sum = 0
		for v in it 'loop'
				sum = sum + v
		end 'loop'
		return sum
end 'main'
```
```exitcode
6
```

<!-- disabled-test: ranges.inclusive-create-iterator -->
<!-- TUPLES + `Map` rung — first-class `Range` VALUE + the iterator protocol. `withIterator()` yields `(Iterator, Element)` TUPLES and shv2 has no tuple type, so this is blocked on the same missing mechanism `Map` is; re-attributed from P1.8 when Slice E closed the rung (2026-07-28). -->
`to` produces an inclusive Range — `createIterator()` visits the endpoint.
```maxon
function main() returns ExitCode
		let it = try (1 to 4).createIterator() otherwise return 99
		var sum = 0
		for v in it 'loop'
				sum = sum + v
		end 'loop'
		return sum
end 'main'
```
```exitcode
10
```

<!-- disabled-test: ranges.with-iterator -->
<!-- TUPLES + `Map` rung — first-class `Range` VALUE + the iterator protocol. `withIterator()` yields `(Iterator, Element)` TUPLES and shv2 has no tuple type, so this is blocked on the same missing mechanism `Map` is; re-attributed from P1.8 when Slice E closed the rung (2026-07-28). -->
`(start upto end).withIterator()` exposes the underlying iterator inside the loop.
```maxon
function main() returns ExitCode
		for (iter, v) in (10 upto 13).withIterator() 'loop'
				print("{iter.index()}:{v}\n")
		end 'loop'
		return 0
end 'main'
```
```exitcode
0
```
```stdout
0:10
1:11
2:12
```

<!-- disabled-test: ranges.empty-create-iterator-throws -->
<!-- TUPLES + `Map` rung — first-class `Range` VALUE + the iterator protocol. `withIterator()` yields `(Iterator, Element)` TUPLES and shv2 has no tuple type, so this is blocked on the same missing mechanism `Map` is; re-attributed from P1.8 when Slice E closed the rung (2026-07-28). -->
An empty exclusive range fails to construct an iterator.
```maxon
function main() returns ExitCode
		let it = try (5 upto 5).createIterator() otherwise return 7
		return it.current()
end 'main'
```
```exitcode
7
```

<!-- disabled-test: ranges.empty-inclusive-create-iterator-throws -->
<!-- TUPLES + `Map` rung — first-class `Range` VALUE + the iterator protocol. `withIterator()` yields `(Iterator, Element)` TUPLES and shv2 has no tuple type, so this is blocked on the same missing mechanism `Map` is; re-attributed from P1.8 when Slice E closed the rung (2026-07-28). -->
An empty inclusive range (end < start) also throws.
```maxon
function main() returns ExitCode
		let it = try (5 to 3).createIterator() otherwise return 7
		return it.current()
end 'main'
```
```exitcode
7
```

<!-- disabled-test: ranges.let-binding -->
<!-- TUPLES + `Map` rung — first-class `Range` VALUE + the iterator protocol. `withIterator()` yields `(Iterator, Element)` TUPLES and shv2 has no tuple type, so this is blocked on the same missing mechanism `Map` is; re-attributed from P1.8 when Slice E closed the rung (2026-07-28). -->
A range can be bound to a variable and iterated via the standard for-in path.
```maxon
function main() returns ExitCode
		let r = 1 upto 5
		var sum = 0
		for x in r 'loop'
				sum = sum + x
		end 'loop'
		return sum
end 'main'
```
```exitcode
10
```

<!-- test: ranges.float-bounds-refused -->
A counted range's loop variable is a counter stepped by 1, so its bounds must be INTEGERS.
A `float` range is refused at the bound, not at the backend: before this was checked it reached
the x64 emitter and panicked (*"rax is in the gpr register file where the xmm file is required"*),
an internal error against a program the runnable oracle refuses cleanly.
```maxon
function main() returns ExitCode
	var trips = 0
	for x in 1.0 to 3.0 'l'
		trips = trips + 1
	end 'l'
	return trips as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/ranges/ranges.float-bounds-refused.test:4:11: Unsupported: a counted `for … in <lo> to|upto <hi>` range needs INTEGER bounds — got a 'float'. The loop variable is a counter stepped by 1, which no other domain has a meaning for; iterate a `float`/`String` by indexing an `Array` over it
```

<!-- test: ranges.bool-bounds-refused -->
The same for a `bool` range, which was SILENTLY ACCEPTED and ran two trips incrementing a bool.
Nothing in the lowering rejected it: the other bad domains happened to land on a comparison type
error, which is `emitCompare` refusing two operands and says nothing about the step.
```maxon
function main() returns ExitCode
	var trips = 0
	for b in false to true 'l'
		trips = trips + 1
	end 'l'
	return trips as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/ranges/ranges.bool-bounds-refused.test:4:11: Unsupported: a counted `for … in <lo> to|upto <hi>` range needs INTEGER bounds — got a 'bool'. The loop variable is a counter stepped by 1, which no other domain has a meaning for; iterate a `float`/`String` by indexing an `Array` over it
```

<!-- test: ranges.mixed-bounds-blames-the-float-half -->
Each bound is asked separately and anchored on its own half, so a mixed range names the side that
is wrong — here the END bound, at the `to`.
```maxon
function main() returns ExitCode
	var trips = 0
	for x in 1 to 3.5 'l'
		trips = trips + 1
	end 'l'
	return trips as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/ranges/ranges.mixed-bounds-blames-the-float-half.test:4:13: Unsupported: a counted `for … in <lo> to|upto <hi>` range needs INTEGER bounds — got a 'float'. The loop variable is a counter stepped by 1, which no other domain has a meaning for; iterate a `float`/`String` by indexing an `Array` over it
```

<!-- test: ranges.ranged-alias-parameter-bound-stays-legal -->
The check is `tagIsIntegral`, not `== integer`, and this is the program that pins the difference: a
ranged-alias bound arriving as a PARAMETER carries the `named` tag until TypeResolution collapses
it. The narrow spelling would refuse this legal program (the measured false refusal
`requireSetKeyMatchesType` records). A char literal is an integer codepoint here, so it passes too.
```maxon
typealias Row = int(0 to 63)

function sumTo(lastRow Row) returns Row
	var total = 0 as Row
	for r in 0 to lastRow 'l'
		total = total + r
	end 'l'
	return total
end 'sumTo'

function main() returns ExitCode
	var chars = 0
	for _ in 'a' to 'e' 'k'
		chars = chars + 1
	end 'k'
	return (sumTo(5) + chars) as ExitCode
end 'main'
```
```exitcode
20
```
