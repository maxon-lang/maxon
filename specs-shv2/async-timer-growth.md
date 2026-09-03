---
feature: async-timer-growth
status: stable
keywords: [async, sleep, green-threads, scheduler, timer, capacity, growth]
category: concurrency
---

# The timer store GROWS — an shv2 scheduler invariant

## Documentation

**This is an shv2-authored spec, not a port.** It pins an invariant of shv2's own green-thread scheduler that
no canonical spec describes, because the structure it is about — a flat timer store with a seed capacity — is
shv2's implementation choice rather than a property of the language.

Every green thread parked on a timer occupies one entry in the scheduler's timer store
(`GtRuntime.TimerHeapCapacity`, seeded at 256). **The seed is a starting size, not a limit:** when the store
fills, `__gt_timer_add` doubles it, copies the live entries across, and republishes the base — so the number
of threads that may wait on a timer at once is bounded by memory, not by a constant.

### What this pins, and what it caught

The store used to be a bare append with **no capacity check at all**, justified by a comment reasoning that
the concurrent-timer count was *"≤2 in the specs, 1 in a sequential loop, far below `TimerHeapCapacity`"*.
That is an argument about the test corpus, not about the code: nothing stopped a program parking a 257th
thread, and **MEASURED, 300 concurrent `sleep`s wrote 44 entries past the end of the 256-entry store** — plain
out-of-bounds writes into neighbouring slab memory — and the program still exited 0. A green suite could never
have found it, which is exactly why the false premise survived: it had been checked against the tests rather
than against the store.

The case below is therefore a **red-gate pin**, not a feature demonstration. It parks far more threads than
the seed capacity and checks that every one of them comes back with its own value.

## Tests

<!-- test: async-timer-growth.beyond-seed-capacity -->
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
Five thousand green threads park on a timer simultaneously — five doublings past the 256-entry seed — and each
returns its own index. Summing the results checks every entry individually: a copy that dropped an entry
strands that thread and the program hangs, and one that duplicated an entry double-counts and the sum is
wrong. Only a store that carried all 5000 across every doubling produces `12497500`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function sleeper(n Integer) returns Integer
	sleep(50)
	return n
end 'sleeper'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	var i = 0
	while i < 5000 'spawn'
		arr.push(async sleeper(i))
		i = i + 1
	end 'spawn'
	var sum = 0
	for p in arr 'each'
		sum = sum + await p
	end 'each'
	print("sum={sum}")
	return 0
end 'main'
```
```stdout
sum=12497500
```
```exitcode
0
```
