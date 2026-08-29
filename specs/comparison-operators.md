---
feature: comparison-operators
status: stable
keywords: [operators, comparison, equals, not-equals, greater, less]
category: operators
---

# Comparison Operators

## Documentation

Comparison operators compare two values and return `true` or `false`.

### Operators

- `==` - Equal to
- `!=` - Not equal to
- `<` - Less than
- `>` - Greater than
- `<=` - Less than or equal to
- `>=` - Greater than or equal to

### Example

```maxon
function main() returns ExitCode
	let x = 10
	let y = 20
	
	if x < y 'check'
		return 1
	end 'check'
	
	return 0
end 'main'
```
```exitcode
1
```


## Tests

<!-- test: equality -->
```maxon
function main() returns ExitCode
	let x = 42
	if x == 42 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```


<!-- test: not-equal -->
```maxon
function main() returns ExitCode
	let x = 10
	if x != 20 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```


<!-- test: greater-than -->
```maxon
function main() returns ExitCode
	if 5 > 3 'check'
		return 42
	end 'check'
	return 0
end 'main'
```
```exitcode
42
```


<!-- test: less-than-or-equal -->
```maxon
function main() returns ExitCode
	let a = 5
	let b = 10
	if a <= b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```


<!-- test: float-comparison -->
```maxon
function main() returns ExitCode
	let x = 3.5
	let y = 2.1
	if x > y 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```



<!-- test: wide-unsigned-ordering -->
Two operands whose DECLARED ranges reach past `i64.max` order by their VALUES, not by the sign bit
of their patterns. `u64.max` is `0xFFFF…FFFF`, which a signed compare reads as `-1` — so before the
ordering-comparison signedness rule this answered `three > big`, exactly backwards, in both
compilers. The two calls are what keep the operands out of the constant folder: the rule reads
DECLARED types, and this case is about a value only known at run time.

```maxon
typealias Wide = int(0 to u64.max)

function wide(n Wide) returns Wide
	return n
end 'wide'

function main() returns ExitCode
	let big = wide(u64.max)
	let three = wide(3 as Wide)

	if three < big 'ordered'
		return 42
	end 'ordered'
	return 1
end 'main'
```
```exitcode
42
```


<!-- test: wide-unsigned-ordering-against-a-narrow-range -->
One operand reaching past `i64.max` is enough to decide the pair: the other's `int(0 to 100)` admits
no negative, so every value either can hold orders the same way under the unsigned reading. This is
the half a ONE-SIDED optimal type would get wrong in the other direction — see
`a-plain-int-operand-keeps-the-signed-reading`.

```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 100)

function wide(n Wide) returns Wide
	return n
end 'wide'

function narrow(n Narrow) returns Narrow
	return n
end 'narrow'

function main() returns ExitCode
	let big = wide(u64.max)
	let small = narrow(3 as Narrow)

	if big > small 'ordered'
		return 42
	end 'ordered'
	return 1
end 'main'
```
```exitcode
42
```


<!-- test: compared-against-the-unsigned-extreme -->
`u64.max` written in EXPRESSION position is the largest value the domain has, so nothing exceeds it.
It rides as the wrapped `-1`, so a compiler that reads its PATTERN rather than what the source wrote
answers this `true` — which is the shape this case exists to refuse.

```maxon
typealias Wide = int(0 to u64.max)

function wide(n Wide) returns Wide
	return n
end 'wide'

function main() returns ExitCode
	let three = wide(3 as Wide)

	if three > u64.max 'impossible'
		return 1
	end 'impossible'
	return 42
end 'main'
```
```exitcode
42
```


<!-- test: a-negative-literal-is-not-the-unsigned-extreme -->
The negative control for the case above, and the reason `u64.max` is recognised by what the source
WROTE rather than by the value it folded to: a literal `-1` is the SAME 64 bits and is a genuinely
negative operand, so a non-negative value is above it. Read as `u64.max` this would answer `false`.

```maxon
typealias Wide = int(0 to u64.max)

function wide(n Wide) returns Wide
	return n
end 'wide'

function main() returns ExitCode
	let three = wide(3 as Wide)

	if three > 0 - 1 'aboveNegativeOne'
		return 42
	end 'aboveNegativeOne'
	return 1
end 'main'
```
```exitcode
42
```


<!-- test: a-plain-int-operand-keeps-the-signed-reading -->
An operand that admits negatives REFUSES the unsigned reading for the pair, whatever the other one
declares. A one-sided rule — "take the first ranged type either operand offers", which is what the
arithmetic family does — would let the non-negative side impose an unsigned reading here and answer
this backwards.

```maxon
typealias Wide = int(0 to u64.max)
typealias Signed = int(i64.min to i64.max)

function wide(n Wide) returns Wide
	return n
end 'wide'

function signed(n Signed) returns Signed
	return n
end 'signed'

function main() returns ExitCode
	let three = signed(3 as Signed)
	let minusOne = signed(0 - 1)

	if three > minusOne 'ordered'
		return 42
	end 'ordered'
	return 1
end 'main'
```
```exitcode
42
```


<!-- test: a-negative-sentinel-in-a-non-negative-type-still-decodes -->
⚠ **THE DELIBERATE HOLE IN THE ORDERING RULE, PINNED SO IT CANNOT BE CLOSED BY ACCIDENT.**

A declared range is not enforced at a parameter or a return in either compiler, and the standard
library does not merely tolerate that — it BUILDS on it. `Map.findSlot` returns `-(insertIndex + 1)`
to encode "not found" through a return type declared `TableSlotIndex = int(0 to u64.max)`, and
`if slotIndex >= 0` is what decodes it. Read unsigned that test is ALWAYS TRUE, every insert takes
the found path with a negative index, and `Array.set` panics: measured at **81 behaviour failures**
across the reference suite when the rule trusted the declaration.

So a FOLDED CONSTANT THAT FITS THE SIGNED DOMAIN is evidence for the signed reading, and `0` is the
constant every sentinel decode in the tree is written against. The price is named rather than
hidden: `wide > 100` for a `wide` above `i64.max` keeps the signed reading and is WRONG. Closing
that needs the declared ranges to become TRUE — enforced at the parameter, with a signed
`TableSlotIndex` — not a wider rule at the comparison.

```maxon
typealias Slot = int(0 to u64.max)

function findSlot(key Slot) returns Slot
	if key == 7 'found'
		return 3 as Slot
	end 'found'
	return 0 - 1
end 'findSlot'

function main() returns ExitCode
	let slot = findSlot(9 as Slot)

	if slot >= 0 'wronglyFound'
		return 1
	end 'wronglyFound'
	return 42
end 'main'
```
```exitcode
42
```
