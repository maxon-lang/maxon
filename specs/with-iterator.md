---
feature: withIterator
status: stable
keywords: [withIterator, iterator, for-in, advance, current, index, retreat]
category: stdlib
---

# withIterator and iterator navigation

## Documentation

### Overview

`.withIterator()` wraps any `Iterable` and yields `(Iterator, Element)` tuples in a for-loop. The `iter` binding exposes navigation methods on the underlying iterator — `index()`, `advance()`, `retreat()`, `peek()` — so the loop body can introspect or steer iteration.

### Usage

```text
let arr = ["a", "b", "c"]
for (iter, item) in arr.withIterator() 'loop'
    print("{iter.index()}: {item}\n")
end 'loop'
// 0: a
// 1: b
// 2: c
```

### Protocol

The `Iterator` interface is `current()` (infallible) + `advance()` (throws `IterationError.exhausted` at end). A live iterator always points at a valid element; the constructor throws on empty collections. Manual navigation (`iter.advance()`, `iter.retreat()`) inside the body composes with the loop's own advance — the next iteration's header then advances on top of that, so one extra `advance()` in the body skips one element, and a `retreat()` re-visits the current element.

Iterators are first-class values. Pass a half-consumed iterator into a function and iterate with `for x in iter`: the loop resumes from wherever the iterator is positioned.

## Tests

<!-- test: withIterator.basic -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	for (iter, value) in arr.withIterator() 'loop'
		print("{iter.index()}:{value}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0:10
1:20
2:30
```

<!-- test: withIterator.bytes -->
Iterate over string bytes via the ByteView iterable.
```maxon
function main() returns ExitCode
	let s = "abc"
	var sum = 0
	for b in s.bytes() 'loop'
		sum = sum + b
	end 'loop'
	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
294
```

<!-- test: withIterator.empty-array -->
Empty array yields zero iterations, no error.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
function main() returns ExitCode
	let arr = IntArray.create()
	var total = 0
	for v in arr 'loop'
		total = total + v
	end 'loop'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: withIterator.body-advance-skips-element -->
Calling `iter.advance()` inside the for-loop body skips the next element. Body runs on 10, advances to 20, header advances again to 30; so 20 is skipped.
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30, 40]
	for (iter, value) in arr.withIterator() 'loop'
		print("{value}\n")
		try iter.advance() otherwise break
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
30
```

<!-- test: withIterator.body-retreat-revisits-element -->
Calling `iter.retreat()` inside the body causes the next iteration to re-visit the current element. Without the guard, this would loop forever — we stop after a fixed count.
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	var visits = 0
	var retreated = false
	for (iter, value) in arr.withIterator() 'loop'
		visits = visits + 1
		if visits >= 5 'stop'
			break
		end 'stop'
		print("{iter.index()}:{value}\n")
		if iter.index() == 1 and not retreated 'once'
			retreated = true
			try iter.retreat() otherwise break
		end 'once'
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0:10
1:20
1:20
2:30
```

<!-- test: withIterator.iterator-as-parameter -->
An iterator passed to a helper function is still iterable from its current position. The helper consumes the remaining elements.
```maxon
typealias TokenIter = ArrayIterator with String

function printRest(iter TokenIter)
	for t in iter 'rest'
		print("{t}\n")
	end 'rest'
end 'printRest'

function main() returns ExitCode
	let toks = ["a", "b", "c", "d"]
	var iter = try toks.createIterator() otherwise return 9
	try iter.advance() otherwise return 9
	try iter.advance() otherwise return 9
	printRest(iter)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
c
d
```

<!-- test: withIterator.awaits-every-promise-of-an-array -->
Each element a `withIterator` loop binds out of an array of promises is awaited exactly once in the body, so
every coroutine is consumed by the time the loop ends and the array drops nothing still owed an await.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with (Tally, WaitError)
typealias TallyPromiseArray = Array with TallyPromise

enum WaitError implements Error
	tooLong
end 'WaitError'

function slowTally(n Tally) returns Tally throws WaitError
	sleep(1)

	if n > 100 'big'
		throw WaitError.tooLong
	end 'big'

	return n
end 'slowTally'

function main() returns ExitCode
	var promises = TallyPromiseArray.create()
	promises.push(async slowTally(1))
	promises.push(async slowTally(2))

	for (position, promise) in promises.withIterator() 'each'
		let value = try await promise otherwise 0
		print("{position.index()}: {value}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0: 1
1: 2
```

<!-- test: withIterator.awaits-every-service-reply-of-an-array -->
The same loop over replies to a service's throwing message, whose promise type names the message's merged
error set through `Calc.divide.errors`.
```maxon
typealias Tally = int(0 to u64.max)
typealias ReplyPromise = Promise with (Tally, Calc.divide.errors)
typealias ReplyPromiseArray = Array with ReplyPromise

enum MathError implements Error
	divideByZero
end 'MathError'

type Calc
	var calls as Tally

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function divide(n Tally, by Tally) returns Tally throws MathError
		self.calls = self.calls + 1

		if by == 0 'zero'
			throw MathError.divideByZero
		end 'zero'

		return try (n / by) otherwise throw MathError.divideByZero
	end 'divide'
end 'Calc'

function main() returns ExitCode
	let calc = spawn Calc.create()
	var replies = ReplyPromiseArray.create()
	replies.push(calc.divide(10, by: 2))
	replies.push(calc.divide(10, by: 0))

	for (position, reply) in replies.withIterator() 'each'
		let value = try await reply otherwise 0
		print("{position.index()}: {value}\n")
	end 'each'

	calc.shutdown()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0: 5
1: 0
```

<!-- test: withIterator.a-promise-array-it-never-awaits-is-dropped-once -->
A loop that walks an array of promises and awaits none leaves every one owned by the array, so each coroutine is
cancelled once, when the array drops, and never a second time by the loop's own element binding.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally
typealias TallyPromiseArray = Array with TallyPromise

function slowTally(n Tally) returns Tally
	sleep(1)
	return n
end 'slowTally'

function main() returns ExitCode
	var promises = TallyPromiseArray.create()
	promises.push(async slowTally(1))
	promises.push(async slowTally(2))
	var seen = 0

	for (position, _) in promises.withIterator() 'each'
		seen = seen + 1 + position.index()
	end 'each'

	return seen as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: withIterator.awaits-only-some-promises-of-an-array -->
A named promise the body awaits on one iteration and not the others is the array's until it is awaited: the
awaited one empties its slot, and the array drops the rest once.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally
typealias TallyPromiseArray = Array with TallyPromise

function slowTally(n Tally) returns Tally
	sleep(1)
	return n
end 'slowTally'

function main() returns ExitCode
	var promises = TallyPromiseArray.create()
	promises.push(async slowTally(1))
	promises.push(async slowTally(2))
	promises.push(async slowTally(4))
	var total = 0

	for (position, promise) in promises.withIterator() 'each'
		if position.index() == 1 'second'
			total = total + (await promise)
		end 'second'
	end 'each'

	return total as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: withIterator.a-loop-that-breaks-before-awaiting-drops-every-promise-once -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally
typealias TallyPromiseArray = Array with TallyPromise

function slowTally(n Tally) returns Tally
	sleep(1)
	return n
end 'slowTally'

function main() returns ExitCode
	var promises = TallyPromiseArray.create()
	promises.push(async slowTally(1))
	promises.push(async slowTally(2))
	var total = 0

	for (position, promise) in promises.withIterator() 'each'
		if position.index() == 1 'second'
			break
		end 'second'

		total = total + (await promise)
	end 'each'

	return total as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: withIterator.nested-loops-over-two-promise-arrays -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally
typealias TallyPromiseArray = Array with TallyPromise

function slowTally(n Tally) returns Tally
	sleep(1)
	return n
end 'slowTally'

function main() returns ExitCode
	var outer = TallyPromiseArray.create()
	outer.push(async slowTally(1))
	outer.push(async slowTally(2))
	var inner = TallyPromiseArray.create()
	inner.push(async slowTally(10))
	inner.push(async slowTally(20))
	var total = 0

	for (_, o) in outer.withIterator() 'eachOuter'
		total = total + (await o)

		for (position, i) in inner.withIterator() 'eachInner'
			if position.index() == total - 1 'matching'
				total = total + (await i)
			end 'matching'
		end 'eachInner'
	end 'eachOuter'

	return total as ExitCode
end 'main'
```
```exitcode
13
```
