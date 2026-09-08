---
feature: for-range-bound-mutation
status: experimental
keywords: [for, range, to, upto, bounds, mutation, borrow]
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

That is **not** true of the ARRAY form, which hands the body a BORROWED element the array
still owns: a mutation that reallocates or clears the array leaves that borrow dangling, so
the iterated container is made unwritable for the body (E3019). The two forms are pinned
together here because they are two halves of one parser, and the lock that protects the
array form must not leak onto the range form.

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

<!-- test: array-source-stays-locked -->
The other half of the pin: an ARRAY source is still unwritable for the body, because its
element is a borrow the array owns.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(10)
	var total = 0
	for n in arr 'l'
		total = total + n
		arr.push(9)
	end 'l'
	return total
end 'main'
```
```maxoncstderr
error E3019: specs/fragments/for-range-bound-mutation/array-source-stays-locked.test:11:7: cannot pass 'arr' to function that mutates parameter 'self' (in main)
```
