---
feature: inline-allocation-under-preemption
status: experimental
keywords: [co-allocated, inline, slab, allocator, asynchronous preemption, services, MAXON_MAX_PROCS]
category: system
---

# A constant-capacity array create in a program with green threads survives being stopped

## Documentation

A constant-capacity `create` of an array is expanded in its caller as the allocator's hit path. A program with
green threads stops its user functions where they stand once they run long enough, so the expansion is an
async-unsafe range: a stop whose PC lands inside it is declined and retried, and no two creates are handed one
slot.

## Tests

<!-- test: eight-services-churning-constant-capacity-arrays-keep-their-sums -->
<!-- procs: 4 -->
Eight services each build and drop two constant-capacity arrays per round for eight million rounds, on four
processors, so the monitor stops each runner many times and a stopped runner resumes on another machine as often
as on its own. An allocation stopped half way and resumed after another service allocated from the same span
hands one slot to two arrays: a sum comes back wrong, the leak gate fires, or the allocator aborts.
```maxon
typealias Word = int(0 to u32.max)
typealias Total = int(0 to u64.max)
typealias WordArray = Array with Word
typealias ChurnHandleArray = Array with Churn.handle

let ServiceCount = 8
let Rounds = 8000000 as Word

type Churn
	var finished as Total

	static function create() returns Self
		return Self{finished: 0}
	end 'create'

	export function run(rounds Word, seed Word)
		var total = 0 as Total
		var round = 0 as Word

		while round < rounds 'eachRound'
			let a = WordArray.create(4, zeroed: false)
			a.push(round)
			a.push(seed)
			a.push(round)
			a.push(seed)

			let b = WordArray.create(6, zeroed: false)
			b.push(a.count() as Word)
			b.push(round)
			b.push(seed)
			b.push(round)
			b.push(seed)
			b.push(round)

			for word in a 'sumA'
				total = total + (word as Total)
			end 'sumA'

			for word in b 'sumB'
				total = total + (word as Total)
			end 'sumB'

			round = round + 1
		end 'eachRound'

		self.finished = self.finished + total
	end 'run'

	export function result() returns Total
		return self.finished
	end 'result'
end 'Churn'

function expected(rounds Word, seed Word) returns Total
	var total = 0 as Total
	var round = 0 as Word

	while round < rounds 'eachRound'
		total = total + (round as Total) * 5 + (seed as Total) * 4 + 4
		round = round + 1
	end 'eachRound'

	return total
end 'expected'

function main() returns ExitCode
	let handles = ChurnHandleArray.create(0)
	var index = 0 as ElementIndex

	while index < ServiceCount 'spawnEach'
		handles.push(spawn Churn.create())
		index = index + 1
	end 'spawnEach'

	index = 0

	while index < ServiceCount 'sendEach'
		let handle = try handles.get(index) otherwise panic("churn: handles.get OOB at {index}")
		try handle.run(Rounds, seed: (index + 1) as Word) otherwise panic("churn: a mailbox holds the one message")
		index = index + 1
	end 'sendEach'

	var wrong = 0 as Word
	index = 0

	while index < ServiceCount 'checkEach'
		let handle = try handles.get(index) otherwise panic("churn: handles.get OOB at {index}")
		let got = try await handle.result() otherwise 0
		let want = expected(Rounds, seed: (index + 1) as Word)

		if got != want 'mismatch'
			print("service {index}: got {got} want {want}\n")
			wrong = wrong + 1
		end 'mismatch'

		index = index + 1
	end 'checkEach'

	if wrong != 0 'anyWrong'
		return 3
	end 'anyWrong'

	print("churn ok\n")
	return 0
end 'main'
```
```stdout
churn ok
```
