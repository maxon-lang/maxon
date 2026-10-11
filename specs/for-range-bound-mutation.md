---
feature: for-range-bound-mutation
status: experimental
keywords: [for, range, to, upto, bounds, mutation, live-count]
category: control-flow
---

## Documentation

# Mutating a range bound's variable inside the loop

A counted `for i in <lo> to|upto <hi>` evaluates **both** bounds once, before the loop is
entered, and the loop variable is the counter itself. Assigning to a variable that supplied
either bound is therefore an ordinary write: it cannot change how many times the loop runs,
and the loop must not refuse it.

```text
var n = 0
for i in n upto 3 'l'      // runs 3 times whatever the body does to `n`
    n = n + 1
end 'l'
```

The ARRAY form differs: it re-reads the array's live count on every step, so a `push` in the
body extends the iteration and a `clear` ends it. The loop holds its own reference to the
record it started on, so rebinding the source variable leaves it walking that record to its
end, and the element the body reads is counted, so it stays valid after any mutation. The two
forms are pinned together here because they are two halves of one parser.

## Tests

<!-- test: range-start-bound-mutated-in-body -->
The START bound's variable is writable in the body — it was snapshotted into the preheader.
```maxon
function main() returns ExitCode
	var n = 0
	var total = 0
	for i in n upto 3 'l'
		n = n + 1
		total = total + i
	end 'l'
	return total
end 'main'
```
```exitcode
3
```

<!-- test: range-end-bound-mutated-in-body -->
The END bound's variable is writable too, and the loop still runs the snapshotted number of
times rather than chasing the growing bound.
```maxon
function main() returns ExitCode
	var n = 3
	var total = 0
	for i in 0 upto n 'l'
		n = n + 1
		total = total + i
	end 'l'
	return total
end 'main'
```
```exitcode
3
```

<!-- test: inclusive-range-start-bound-mutated-in-body -->
The same for `to`, whose upper bound is inclusive.
```maxon
function main() returns ExitCode
	var n = 1
	var total = 0
	for i in n to 3 'l'
		n = n + 1
		total = total + i
	end 'l'
	return total
end 'main'
```
```exitcode
6
```

<!-- test: range-bound-write-takes-effect -->
The write is not merely permitted, it happens: `n` is 1 at the loop's start (so the loop runs
1, 2, 3) and 31 after three trips.
```maxon
function main() returns ExitCode
	var n = 1
	var total = 0
	for i in n upto 4 'l'
		n = n + 10
		total = total + i
	end 'l'
	return (total + n) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: array-source-push-in-body-extends-the-iteration -->
The other half of the pin: an ARRAY source is writable in the body, and the loop re-reads its
live count, so each element the body pushes is visited. The element read before a push that
reallocates the array stays valid.
```maxon
typealias Labels = Array with String

function main() returns ExitCode
	var arr = Labels.create()
	arr.push("growing element number {40 + 2}")
	for s in arr 'l'
		if arr.count() < 3 'grow'
			arr.push("growing element number {arr.count() + 40}")
		end 'grow'

		print("{s} {s.byteLength()}\n")
	end 'l'
	print("count={arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
growing element number 42 25
growing element number 41 25
growing element number 42 25
count=3
```

<!-- test: array-shared-through-a-field-cleared-in-body-ends-the-iteration -->
A `Holder` stores the same record the loop walks, and grows then clears it from inside the body.
The element read before the call stays valid, and the next step sees the cleared count and ends.
```maxon
typealias Tally = int(0 to i64.max)
typealias Words = Array with String

type Holder
	var items as Words

	static function create(items Words) returns Self
		return Self{items: items}
	end 'create'

	function grow()
		for i in 0 upto 300 'g'
			items.push("holder-filler-{i}-pppppppppppppppp")
		end 'g'
		items.clear()
	end 'grow'
end 'Holder'

function main() returns ExitCode
	var arr = ["first-{1}", "second-{2}", "third-{3}"]
	var h = Holder.create(arr)
	var seen = 0 as Tally
	for x in arr 'each'
		h.grow()
		print("x.len={x.byteLength()}\n")
		print("x={x}\n")
		seen = seen + 1
	end 'each'
	print("done seen={seen} arrCount={arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
x.len=7
x=first-1
done seen=1 arrCount=0
```

<!-- test: module-array-cleared-by-a-callee-ends-the-iteration -->
A callee clears the module `var` array the loop walks. The element read before the call stays
valid, and the next step sees the cleared count and ends.
```maxon
typealias Lines = Array with String

var pending = Lines.create()

function wipe()
	pending.clear()
end 'wipe'

function main() returns ExitCode
	pending.push("hello world this is a long string for heap allocation {40 + 2}")
	for it in pending 'scan'
		wipe()
		print("{it.byteLength()} [{it}]\n")
	end 'scan'
	print("{pending.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
56 [hello world this is a long string for heap allocation 42]
0
```

<!-- test: module-array-rebound-in-body-walks-the-old-record -->
Rebinding the module `var` the loop walks leaves the loop on the record it started on, to that
record's end.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

var numbers = IntegerArray.create()

function main() returns ExitCode
	numbers.push(1)
	numbers.push(2)
	var t = 0 as Integer
	for n in numbers 'l'
		numbers = IntegerArray.create()
		t = t + n
	end 'l'
	print("{t} {numbers.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 0
```
