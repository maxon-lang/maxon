---
feature: await-any-waits
status: experimental
keywords: [awaitAny, select, wait, yield, message, modify, acquire, one-call open, HttpClient, E3221, E3205]
category: concurrency
---

# `awaitAny` is a wait

## Documentation

`promises.awaitAny()` parks the caller until some promise of the array has completed. The compiler cannot see which coroutine or service produced an array element, so a function that calls it waits on a party it cannot see, exactly as `await` of a promise of unknown origin does, and it yields. A message or spawn factory that reaches it is refused (E3221) because it would park while holding its own service. Inside a `modify` block, a one-call open or an `acquire` block it is refused (E3205), directly or through a callee. `main` and a plain coroutine hold nothing and may select.

A message body may make an `HttpClient` request: the client waits on no other party, so a request over `http` and one over `https` are both accepted there.

## Tests

<!-- test: await-any-waits.error.a-message-that-selects-over-replies-is-refused -->
`select` sends a request to `Peer`, then parks on the array of replies while holding `Probe`'s own service.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Replies = Array with Promise with Integer

type Peer
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Peer'

type Probe
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function select(p Peer.handle) returns Integer
		let ps = Replies.create(0)
		ps.push(try p.ping() otherwise return 0)
		let ready = ps.awaitAny()
		let winner = try ps.remove(ready) otherwise return 0
		winner.cancel()
		return ready as Integer
	end 'select'
end 'Probe'

function main() returns ExitCode
	let probe = spawn Probe.create()
	let peer = spawn Peer.create()
	_ = probe
	_ = peer
	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:24:18: the message 'select' may wait (it selects over promises whose coroutines it cannot see, at line 27) while holding its own service
```

<!-- test: await-any-waits.error.a-select-inside-a-modify-block-is-refused -->
Another coroutine could run while the select parks, so `batch` may not stay open across it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Tally = int(0 to u64.max)
typealias Jobs = Array with Promise with Integer

type Batch
	export var done as Tally

	static function create() returns Batch
		return Self{done: 0}
	end 'create'
end 'Batch'

function work(n Integer) returns Integer
	sleep(5)
	return n
end 'work'

function main() returns ExitCode
	let batch = Batch.create() as isolated Batch
	let ps = Jobs.create(0)
	ps.push(async work(1))
	modify batch 'edit'
		let ready = ps.awaitAny()
		batch.done = batch.done + (ready as Tally)
	end 'edit'
	let winner = try ps.remove(0) otherwise return 1
	_ = await winner
	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:24:18: cannot wait inside 'edit'; another coroutine could run while 'batch' is open. Await before or after the block
```

<!-- test: await-any-waits.error.a-one-call-open-around-a-method-that-selects-is-refused -->
`spin` selects over its helper coroutines, which could not run while `slot` is open for the call.

```maxon
typealias Tally = int(0 to u64.max)
typealias Jobs = Array with Promise with Tally

type Slot
	var count as Tally

	static function create() returns Slot
		return Self{count: 0}
	end 'create'

	function spin() returns Tally
		let jobs = Jobs.create(0)
		jobs.push(async delayed())
		let ready = jobs.awaitAny()
		let winner = try jobs.remove(ready) otherwise return 0
		let got = await winner
		self.count = got

		return got
	end 'spin'
end 'Slot'

function delayed() returns Tally
	sleep(10)

	return 7
end 'delayed'

function runSlot(slot isolated Slot) returns Tally
	return slot.spin()
end 'runSlot'

function main() returns ExitCode
	let slot = Slot.create() as isolated Slot
	let work = async runSlot(slot)
	let laps = await work
	print("laps={laps}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:31:14: cannot call 'spin' inside 'slot'; it may wait (it selects over promises whose coroutines it cannot see, at line 15). Call it before or after the block
```

<!-- test: await-any-waits.error.a-select-inside-an-acquire-block-is-refused -->
An `acquire` block holds its services, so it may not park on a select.

```maxon
typealias Tally = int(0 to u64.max)
typealias Jobs = Array with Promise with Tally

type Ledger
	var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function delayed() returns Tally
	sleep(10)

	return 7
end 'delayed'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		let jobs = Jobs.create(0)
		jobs.push(async delayed())
		let ready = jobs.awaitAny()
		let winner = try jobs.remove(ready) otherwise return 0

		return await winner
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let ledger = spawn Ledger.create(10)
	print("{work(ledger)}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:26:13: cannot wait inside 'held'; an acquire block holds its services and may not wait or acquire again. Await before or after the block
```

<!-- test: await-any-waits.a-select-over-coroutines-runs-in-main-and-in-a-plain-coroutine -->
`main` and `firstOf`, a coroutine of `main`, hold no service, so both may select. The index names the job that finished first, and awaiting every job afterwards drains the array.

```maxon
typealias Tally = int(0 to u64.max)
typealias Jobs = Array with Promise with Tally

function delayed(ms Tally, answer Tally) returns Tally
	sleep(ms as Milliseconds)

	return answer
end 'delayed'

function firstOf() returns Tally
	let jobs = Jobs.create(0)
	jobs.push(async delayed(200, answer: 1))
	jobs.push(async delayed(1, answer: 2))
	let ready = jobs.awaitAny()
	var sum = 0 as Tally

	for job in jobs 'eachJob'
		sum = sum + await job
	end 'eachJob'

	return ready as Tally * 100 + sum
end 'firstOf'

function main() returns ExitCode
	let jobs = Jobs.create(0)
	jobs.push(async delayed(200, answer: 3))
	jobs.push(async delayed(1, answer: 4))
	let ready = jobs.awaitAny()
	var sum = 0 as Tally

	for job in jobs 'eachJob'
		sum = sum + await job
	end 'eachJob'

	let inner = async firstOf()
	let innerAnswer = await inner

	print("main ready={ready} sum={sum} coroutine={innerAnswer}\n")

	return 0
end 'main'
```
```stdout
main ready=1 sum=7 coroutine=103
```
```exitcode
0
```

<!-- test: await-any-waits.a-message-body-may-make-an-http-request -->
`fetch` runs in `Fetcher`'s message body and makes an `HttpClient` request over `http` to a peer coroutine of `main`. The client reaches no wait, so the message is accepted.

```maxon
typealias Tally = int(0 to u64.max)

let RequestBytes = 4096

type Fetcher
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function fetch(url String) returns Tally
		let response = try HttpClient.get(url) otherwise return 0
		self.seen = self.seen + 1

		return response.body().byteLength() as Tally
	end 'fetch'
end 'Fetcher'

function answer(listener TcpListener) returns String
	let conn = try listener.accept() otherwise return "accept-failed"
	_ = try conn.readSome(RequestBytes) otherwise return "recv-failed"
	_ = try conn.send("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello") otherwise return "send-failed"

	return "served"
end 'answer'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let port = listener.port()
	let peer = async answer(listener)
	let fetcher = spawn Fetcher.create()
	let length = try await fetcher.fetch("http://127.0.0.1:{port}/") otherwise 'refused'
		peer.cancel()

		return 2
	end 'refused'

	print("fetched {length} bytes, peer {await peer}\n")

	return 0
end 'main'
```
```stdout
fetched 5 bytes, peer served
```
```exitcode
0
```
