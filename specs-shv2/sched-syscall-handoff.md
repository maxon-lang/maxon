---
feature: sched-syscall-handoff
status: experimental
keywords: [scheduler, green-threads, syscall, blocking, handoff, overlapped, sysmon, file-io, subprocess, procs, MAXON_MAX_PROCS]
category: system
---

# A blocking syscall must not cost the scheduler a processor

## Documentation

⚖ **A GREEN THREAD THAT ENTERS A BLOCKING SYSCALL TODAY TAKES ITS M WITH IT.** The M is the OS thread the
scheduler runs a P on, so for as long as the call is inside the kernel that P schedules nothing — and a
program with more concurrent blocking calls than processors runs them **in batches of `MAXON_MAX_PROCS`,
serially**, however many green threads are ready. The cure has two halves and this rung builds both:
**overlapped IO**, so the call parks the green thread on a completion rather than parking the OS thread on
the kernel; and **Go-style P handoff plus a sysmon**, so a call that still blocks has its P taken away and
given to another M rather than idled.

⭐ **THE COST IS ALREADY MEASURABLE, AND IT IS A CLEAN SERIALIZATION RATHER THAN A STALL.** Eight services
each running `cmd /c exit 0` through `Subprocess.run`, all eight sends in flight before any reply is
awaited, same binary, one box:

| `MAXON_MAX_PROCS` | wall |
|---|---|
| 1 | **566 ms** |
| 2 | 107 ms |
| 4 | 108 ms |
| 16 | 95 ms |

Eight children at one processor cost eight times one child; at two or more they overlap. ⇒ **the wait
genuinely occupies an M** — that is the reading the rung exists for, and it is why the batch of eight
falls off a cliff between one processor and two.

⛔⛔ **BUT IT IS NOT A DEADLOCK, AND THIS FILE MUST NOT CLAIM THAT IT IS.** A file read and a child's exit
both complete on their own: the kernel returns, the M comes back, the P picks up the next green thread.
So a program of *N* independent blocking calls at *P* processors takes ⌈N/P⌉ batches and **finishes** —
it is slower than it should be and it is never wedged. **MEASURED before this file was written, on both
programs below at `MAXON_MAX_PROCS ∈ {1, 2, 4, 16}`: every run answered, every run exited 0.** A shape
that really does wedge needs a blocking call that only ANOTHER green thread can complete — a pipe whose
writer is itself a green thread on an occupied M — and that is a different program from either of these.

⇒ **what these two cases assert is COMPLETION AND THE ANSWER, not a timing.** Every reply arrives, the
sentinel that was sent last still replies, and the aggregate is a fixed number. That is deliberately a
claim a stopwatch cannot make and a flaky box cannot break: when the overlapped path lands, the shape
these programs describe is the one that can regress into a genuine stall (a completion never delivered, a
P handed off and never handed back, a sysmon that retakes a P from an M that was about to return), and
each of those is a **hang** here — which the harness reports as a clean per-test failure at 120 s
(`SpecTestRunner.maxon:739`), not as a slow pass.

### Targets

Both cases carry `<!-- targets: x64-windows -->` alone. That is a property of the SUBSTRATE and not of
the rule: overlapped file IO on this lane is `ReadFile` with an `OVERLAPPED` and an IOCP completion, and
the child wait is a `WaitForMultipleObjects` on process handles — neither has an arm64-macOS
implementation at this rung, where the equivalent is `kqueue` plus `posix_spawn`'s `waitpid`. **The
arm64-macOS equivalent rides the same rung** and these two cases widen to
`x64-windows, arm64-macos` when it lands; nothing about the assertions changes when they do.

## Tests

<!-- test: more-blocking-file-reads-than-processors-still-finish -->
<!-- targets: x64-windows -->
<!-- procs: 2 -->
**EIGHT CONCURRENT FILE ROUND-TRIPS ON TWO PROCESSORS.** Every send is posted before any reply is awaited,
so all eight reads are outstanding at once and there are four times as many of them as there are Ms to
carry them. Each service writes its own small file, reads it back and answers the byte count, so the
aggregate is `8 × 10` and a read that returned nothing subtracts exactly its own ten from it.

⚠ **THE SENTINEL IS SENT LAST AND AWAITED LAST, AND IT IS THE HALF THAT WOULD SEE A LOST P.** It does no
IO at all — one integer, immediately — so `last=1` says a green thread that needed nothing from the
kernel still got scheduled after eight that did. A P handed off to a blocking M and never handed back
takes this reply with it, and the case reads a 120 s timeout rather than a wrong number.

⚠ **THE FILES ARE NAMED PER SERVICE AND DELETED ON EVERY PATH**, including both error paths, because the
harness runs its cases in parallel out of one `temp/` directory.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReaderHandleArray = Array with Reader.handle
typealias ReplyPromise = Promise with (Integer, ServiceError)
typealias ReplyPromiseArray = Array with ReplyPromise

let readerCount = 8

// Ten bytes, so the aggregate is 80 and one lost round-trip is visible as 70 rather than as a rounding.
let payload = "0123456789"

type Reader
	var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'

	export function roundTrip() returns Integer
		let path = FilePath from "syscall_handoff_read_{self.id}.txt"
		try File.writeText(path, content: payload) otherwise 'w'
			return 0
		end 'w'
		let text = try File.readText(path) otherwise 'r'
			try File.delete(path) otherwise ignore
			return 0
		end 'r'
		try File.delete(path) otherwise ignore
		return text.byteLength()
	end 'roundTrip'
end 'Reader'

type Sentinel
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Sentinel'

function main() returns ExitCode
	var readers = ReaderHandleArray.create()

	var i = 0
	while i < readerCount 'spawnEach'
		readers.push(spawn Reader.create(i))
		i = i + 1
	end 'spawnEach'

	let last = spawn Sentinel.create()

	// Post every read before awaiting any of them — this is what makes eight reads CONCURRENT rather
	// than eight reads one after another, and it is the whole difference between this program and one
	// that could never see the subject.
	var replies = ReplyPromiseArray.create()
	var k = 0
	while k < readerCount 'sendEach'
		let r = try readers.get(k) otherwise panic("readers.get OOB at {k} — the loop is bounded by the count the pushes above filled")
		replies.push(r.roundTrip())
		k = k + 1
	end 'sendEach'

	let tailReply = last.ping()

	var aggregate = 0
	var n = 0
	while n < readerCount 'collect'
		let p = try replies.get(n) otherwise panic("replies.get OOB at {n} — the loop is bounded by the count the pushes above filled")
		aggregate = aggregate + (try await p otherwise 0)
		n = n + 1
	end 'collect'

	let tail = try await tailReply otherwise 0

	print("aggregate={aggregate} last={tail}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
aggregate=80 last=1
```
```exitcode
0
```

<!-- test: a-blocking-subprocess-wait-does-not-stall-a-sibling -->
<!-- targets: x64-windows -->
<!-- procs: 2 -->
**THE SAME SHAPE ON THE WAIT THAT COSTS THE MOST.** `Subprocess.run` is the surface behind the 566 ms /
107 ms table above: eight `cmd /c exit 0` children, all eight sends posted before any reply is awaited,
two processors. Each service answers `1` for a child that exited cleanly, so the aggregate is the count
and a child whose wait was lost subtracts its own one.

⚠ **A CHILD'S WAIT IS THE LONGEST BLOCKING CALL A SPEC CASE CAN CHEAPLY MAKE — ~70 ms against a file
read's microseconds** — which is why it, and not the file read above, is the case whose serialization is
visible in a wall clock at all. The assertion is still the ANSWER: `cmd /c exit 0` does no IO, writes
nothing and depends on nothing, so the eight are interchangeable and their sum is fixed however the
scheduler interleaves them.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RunnerHandleArray = Array with Runner.handle
typealias ReplyPromise = Promise with (Integer, ServiceError)
typealias ReplyPromiseArray = Array with ReplyPromise

let runnerCount = 8

type Runner
	var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'

	export function child() returns Integer
		let exe = Executable.name("cmd")
		var argv = StringArray.create()
		argv.push("/c")
		argv.push("exit")
		argv.push("0")
		let result = try Subprocess.run(exe, arguments: argv) otherwise 'e'
			return 0
		end 'e'
		if result.succeeded() 'ok'
			return 1
		end 'ok'
		return 0
	end 'child'
end 'Runner'

type Sentinel
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Sentinel'

function main() returns ExitCode
	var runners = RunnerHandleArray.create()

	var i = 0
	while i < runnerCount 'spawnEach'
		runners.push(spawn Runner.create(i))
		i = i + 1
	end 'spawnEach'

	let last = spawn Sentinel.create()

	var replies = ReplyPromiseArray.create()
	var k = 0
	while k < runnerCount 'sendEach'
		let r = try runners.get(k) otherwise panic("runners.get OOB at {k} — the loop is bounded by the count the pushes above filled")
		replies.push(r.child())
		k = k + 1
	end 'sendEach'

	let tailReply = last.ping()

	var aggregate = 0
	var n = 0
	while n < runnerCount 'collect'
		let p = try replies.get(n) otherwise panic("replies.get OOB at {n} — the loop is bounded by the count the pushes above filled")
		aggregate = aggregate + (try await p otherwise 0)
		n = n + 1
	end 'collect'

	let tail = try await tailReply otherwise 0

	print("aggregate={aggregate} last={tail}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
aggregate=8 last=1
```
```exitcode
0
```
