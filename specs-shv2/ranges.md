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

<!-- test: ranges.create-iterator -->
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

<!-- test: ranges.inclusive-create-iterator -->
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

<!-- test: ranges.with-iterator -->
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

<!-- test: ranges.empty-create-iterator-throws -->
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

<!-- test: ranges.empty-inclusive-create-iterator-throws -->
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

<!-- test: ranges.let-binding -->
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

<!-- test: ranges.character-range-encodes-every-utf8-width -->
### A character range mints its element through all four UTF-8 encoder widths

⚠ **THE ENCODER HAS FOUR ARMS AND THE COMMITTED CORPUS EXERCISED ONE (BATCH23 review).** A character range
mints its per-trip element with `__char_from_cp` (`GraphemeRuntime.buildCharFromCodepoint`), which ENCODES a
codepoint into a fresh owned record — the dual of `__char_at`, which copies bytes that are already UTF-8.
Every other character-range case in this file and in `character-ownership.md` uses `'a'`…`'z'`, so only the
ASCII arm ever ran; the 2-, 3- and 4-byte arms — each with its own lead-byte floor, its own shift ladder and
its own continuation bytes — were emitted and never executed. They are correct (measured), and this is what
says so, because a lead-byte constant paired with the wrong shift is a wrong ANSWER that no equality walk
and no golden fragment would name.

It also pins the OWNERSHIP of the wide arms: each trip allocates a record `__str_decref` must reclaim, so a
missed drop on any arm is the runner's exit 101 rather than a wrong string.

```maxon

function main() returns ExitCode
	var two = ""
	for c in 'à' to 'å' 'twoByte'
		two.append("{c}")
	end 'twoByte'

	var three = ""
	for c in '一' to '五' 'threeByte'
		three.append("{c}")
	end 'threeByte'

	var four = ""
	for c in '😀' to '😃' 'fourByte'
		four.append("{c}")
	end 'fourByte'

	print("two={two}\nthree={three}\nfour={four}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
two=àáâãäå
three=一丁丂七丄丅丆万丈三上下丌不与丏丐丑丒专且丕世丗丘丙业丛东丝丞丟丠両丢丣两严並丧丨丩个丫丬中丮丯丰丱串丳临丵丶丷丸丹为主丼丽举丿乀乁乂乃乄久乆乇么义乊之乌乍乎乏乐乑乒乓乔乕乖乗乘乙乚乛乜九乞也习乡乢乣乤乥书乧乨乩乪乫乬乭乮乯买乱乲乳乴乵乶乷乸乹乺乻乼乽乾乿亀亁亂亃亄亅了亇予争亊事二亍于亏亐云互亓五
four=😀😁😂😃
```
