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

⛔⛔ **THIS FILE USED TO CARRY A TABLE SHOWING THAT COST, AND THE TABLE WAS A COLD-CACHE ARTEFACT.** It read
**566 ms** at one processor against 107 / 108 / 95 ms at two, four and sixteen, and concluded that the batch
of eight *"falls off a cliff between one processor and two"*. **The 566 was a first-ever run of a
freshly-written binary** — a cold file cache, a cold `cmd.exe` image and a cold loader — and the three
numbers beside it were the same program measured once each, in ascending order, after it had warmed up.
Re-measured WARM and INTERLEAVED, the cliff is not there:

| `MAXON_MAX_PROCS` | 1 | 2 | 4 | 16 |
|---|---|---|---|---|
| **wall, median of 21** | **61 ms** | 61 ms | 61 ms | 62 ms |

⭐ **THE METHOD IS THE MEASUREMENT, SO IT IS RECORDED HERE RATHER THAN LEFT TO BE GUESSED AT.** One warm-up
run per cell, DISCARDED; then 21 rounds, each round running every processor count once, so that drift in the
machine's state lands on all four counts equally instead of on whichever was measured last. **And the two
compilers ran as EACH OTHER'S CONTROL** — the same program built by this tree and by its parent, interleaved
in the same rounds, 21 runs per cell per binary, 168 runs in all. Every one answered `aggregate=8 last=1` and
exited 0, and the two binaries agree cell for cell within 1 ms. The subject is
`a-blocking-subprocess-wait-does-not-stall-a-sibling` below, run as a standalone binary with
`MAXON_MAX_PROCS` set per run.

⛔⛔ **IT TOOK TWO CORRECTIONS TO GET HERE, AND THAT IS THE MOST USEFUL THING THIS PARAGRAPH CAN TELL YOU.**
The committed 566/107/108/95 was the first attempt. A SECOND reading — **143 / 128 / 128 / 127 ms**, taken
warm and interleaved, 9 runs per count — was offered as the correction, and it does not survive either: it
was still measured against no second binary and with a sample too small for a bimodal population, and the
answer it produced is more than twice the one 21 controlled rounds give. **A number that does not reproduce
under a warm interleaved control with a second binary is wrong, however carefully it was taken** — and this
one had to be taken three times before it stopped moving.

⚠⚠ **AND A MEDIAN IS THE WRONG STATISTIC TO QUOTE ALONE HERE, WHICH IS THE SECOND HALF OF WHY THE OLD TABLE
MISLED.** The distribution is BIMODAL: a fast mode at 45–62 ms and a slow mode at ~280–500 ms, with roughly
**one run in four** landing in the slow mode **at every processor count**. It is `cmd.exe` spawn cost, not
scheduling — the proportion does not move with the count, and it does not move between two different
compilers either. A sample of one, or of a handful taken in count order, will therefore produce a "curve" of
any shape you like; the 566/107/108/95 row is what that looks like.

⇒ **EIGHT CHILDREN AT ONE PROCESSOR DO *NOT* COST EIGHT TIMES ONE CHILD, AND THE MECHANISM IS NAMED RATHER
THAN GUESSED AT — BECAUSE "NO EFFECT HERE" OTHERWISE READS AS "THE EFFECT IS NOT REAL".**
`__gt_subp_wait_collect` (`SubprocessRuntime.maxon:1176`) **IS A POLL LOOP AND NEVER MAKES AN UNBOUNDED
KERNEL CALL.** Each pass asks the child's handle whether it has exited with a **ZERO-timeout**
`WaitForSingleObject`, peeks each pipe before committing to any read, and — on the one path where a pass
moved no bytes and the child is still running — sleeps **GREEN**, through `__gt_sleep(SubpPollMs)`. That
function's own header states it: *"it sleeps GREEN, so the whole scheduler runs while it waits"*.

⇒ **the wait PARKS the green thread and GIVES THE M BACK, so this program never occupied an M to begin
with.** The flat 61–62 ms is therefore **the correct answer for a program that does not exhibit the effect**,
not a failure to observe one — and eight of these at one processor were never serialised, which is exactly
why there is no cliff to find.

⭐⭐ **THE CALL THAT DOES HOLD ITS M IS `stdin`, AND THAT IS WHERE THIS FILE'S SUBJECT ACTUALLY LIVES.**
`Console.stdin().readLine()` reaches `__con_read_stdin` (`ConsoleRuntime.maxon:40`), which is a plain
synchronous `osReadFile` on the standard-input handle — **no overlapped read, no peek, no park**. A green
thread inside it is a green thread whose OS thread is in the kernel until the user presses return, holding
the processor with it. ⚠ **MEASURED BY THE PASS THAT IDENTIFIED THIS MECHANISM AND NOT RE-RUN HERE**: the
same two-service shape reads `reader-got | sibling` at `MAXON_MAX_PROCS=1` — the sibling cannot run until the
read returns — and flips to `sibling | reader-got` at two or more, where a second processor still has an M to
run it on. **That ordering, not a wall clock, is what a serialised M looks like when you can see it.**

⚠ **NONE OF THIS RETIRES THE RUNG, AND THE `stdin` READING IS WHY THAT IS A STATEMENT RATHER THAN A HOPE.**
The claim at the head of this file — a green thread inside a genuinely blocking kernel call takes its M with
it — is untouched, and now has a live example pointing at it. What has been withdrawn is one program's claim
to *demonstrate* it, and the reason that program never could is that its wait is green. Every call that
really does block (the standard-input read above, a synchronous read of a slow device, a `connect` to an
unreachable host) still costs its processor for the duration, and `handoffp` is still what gives that
processor to somebody else. **The
correct reading of this file is that it has TWO passing cases and no committed timing evidence**, which is
what the paragraph below already says the cases are for.

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
**THE SAME SHAPE ON THE WAIT THAT COSTS THE MOST.** `Subprocess.run` is the surface the timing paragraph at
the head of this file measures: eight `cmd /c exit 0` children, all eight sends posted before any reply is
awaited, two processors. Each service answers `1` for a child that exited cleanly, so the aggregate is the
count and a child whose wait was lost subtracts its own one.

⚠ **A CHILD'S WAIT IS THE LONGEST BLOCKING CALL A SPEC CASE CAN CHEAPLY MAKE — tens of milliseconds against
a file read's microseconds** — which is why it, and not the file read above, is the program a wall clock was
ever pointed at. ⛔ **That is a statement about which program is MEASURABLE, not about what the measurement
found**: warm and interleaved it shows no dependence on the processor count at all, and the paragraph at the
head of this file carries what was withdrawn and why. The assertion here is, and always was, the ANSWER:
`cmd /c exit 0` does no IO, writes nothing and depends on nothing, so the eight are interchangeable and
their sum is fixed however the scheduler interleaves them.
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

<!-- test: a-blocking-kernel-call-does-not-starve-an-unrelated-green-thread -->
<!-- targets: x64-windows -->
<!-- procs: 1 -->
<!-- stdin: delayed -->
⭐⭐ **THIS IS THE CASE THE FILE'S OPENING CLAIM WAS ALWAYS ABOUT, AND IT ASSERTS AN ORDER RATHER THAN A
CLOCK.** The reader blocks in `Console.stdin().readLine()` — `__con_read_stdin` (`ConsoleRuntime.maxon`),
a plain synchronous `osReadFile` with **no park and no overlapped read**, which is the one call in reach
of a spec case that genuinely holds its OS thread in the kernel. The harness's `stdin: delayed` marker
delivers one line after **≈1 s**, so the read *completes* and the program exits 0 either way. The
sentinel touches nothing the reader touches and answers in microseconds.

⇒ **`S` must print before `R`.** A green thread with no relationship to a kernel call must not wait a
full second behind it.

⚠ **MEASURED ON THE PARENT OF THE CHANGE THAT GREENS THIS, 5 RUNS OF 5, `MAXON_MAX_PROCS=1`:**
`R: read returned` then `S: sentinel ran`, `done sibling=1 read=5`, exit 0 — the sentinel waited out the
entire read. **The 1 s delay against a microsecond reply is a ~1000× margin**, which is why this is a
stable wrong ANSWER and not a timing flake; it was deliberately built that way after a `stdin: hold`
variant was rejected for being able to fail only by timing out.

⛔⛔ **A SECOND THING IS ALSO LOST, AND THE PREDICTION THAT THIS CASE NEEDED IT FIXED WAS WRONG.**
`emitGtRunOne` (`GtRuntime.maxon:5758-5768`) suspends the DRIVER inside `__gt_context_switch` and records
it only as `g.waiter`, published to no queue — so a `g` that neither parks nor completes strands its whole
driver chain, up to and including `main`. **That is real and measured**: with a read that never returns,
the sentinel runs to completion on another machine at FOUR processors and `main` still never resumes.

⇒ It was predicted here that the cure needed both halves — release the driver *and* retake the processor —
and that *"each alone leaves one of the two cases below red"*. **MEASURED, and it is false: the retake
alone greens both.** At one processor the sentinel's green thread is already sitting in P0's own ring when
the reader blocks, so retaking P0 and starting a machine on it runs the sentinel directly and no driver has
to move. The driver-release half was never built.

⚠ **So the captive driver above is a LIVE DEFECT that this file does not gate**, and the reason it is
tolerable is worth stating rather than leaving to be rediscovered: it can only bite a call that never
returns, and a program holding a green thread that never returns **cannot exit anyway** — shv2's exit path
waits for live green threads (measured: a green thread parked 60 s and never awaited keeps the process
alive after `main` returns). Curing it means `main` on a real green thread rather than the machine's own
`g0`, which is a different change from this one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Reader
	var id as Integer

	static function create() returns Self
		return Self{id: 0}
	end 'create'

	export function read() returns Integer
		let line = try Console.stdin().readLine() otherwise ""
		print("R: read returned\n")

		return line.count()
	end 'read'
end 'Reader'

type Sentinel
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		print("S: sentinel ran\n")

		return 1
	end 'ping'
end 'Sentinel'

function main() returns ExitCode
	let reader = spawn Reader.create()
	let sentinel = spawn Sentinel.create()

	let blocked = reader.read()
	let tailReply = sentinel.ping()

	let tail = try await tailReply otherwise 0
	let n = try await blocked otherwise 0
	print("done sibling={tail} read={n}\n")

	return 0 as ExitCode
end 'main'
```
```stdout
S: sentinel ran
R: read returned
done sibling=1 read=5
```
```exitcode
0
```

<!-- test: a-spare-processor-already-carries-the-sibling -->
<!-- targets: x64-windows -->
<!-- procs: 4 -->
<!-- stdin: delayed -->
⚠ **THIS CASE WAS ALREADY GREEN BEFORE THE CHANGE THAT GREENS THE ONE ABOVE, AND IT IS HERE TO SAY WHY
THAT ONE IS RED.** Same program, same expected output, one marker different. **MEASURED on the same
parent, `MAXON_MAX_PROCS=4`: `S` then `R`** — the required order already, because a second machine picks
the sentinel up while the first is in the kernel.

⇒ The pair states the defect exactly: **one processor cannot do what four can, and the reason is that a
processor is being spent on a thread that is asleep in the kernel.** As a gate this half is a regression
guard — it is the path that must not break while the other is being fixed — and it is deliberately not
counted as evidence for the cure.

⚠ **It pins an ORDER, not a count.** `MAXON_MAX_PROCS` is clamped to the host's processor count, so on a
one-core machine this case degenerates into the case above and would read `R` first. That is a real
limitation of the pair and not a flake to re-run; a host that cannot give the runtime two processors
cannot exhibit the difference the pair exists to show.
```maxon
typealias Integer = int(i64.min to i64.max)

type Reader
	var id as Integer

	static function create() returns Self
		return Self{id: 0}
	end 'create'

	export function read() returns Integer
		let line = try Console.stdin().readLine() otherwise ""
		print("R: read returned\n")

		return line.count()
	end 'read'
end 'Reader'

type Sentinel
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		print("S: sentinel ran\n")

		return 1
	end 'ping'
end 'Sentinel'

function main() returns ExitCode
	let reader = spawn Reader.create()
	let sentinel = spawn Sentinel.create()

	let blocked = reader.read()
	let tailReply = sentinel.ping()

	let tail = try await tailReply otherwise 0
	let n = try await blocked otherwise 0
	print("done sibling={tail} read={n}\n")

	return 0 as ExitCode
end 'main'
```
```stdout
S: sentinel ran
R: read returned
done sibling=1 read=5
```
```exitcode
0
```

<!-- test: sysmon-retakes-a-processor-from-a-blocking-call -->
<!-- targets: x64-windows -->
<!-- procs: 1 -->
<!-- stdin: delayed -->
⭐ **THIS CASE PINS THE MECHANISM, BECAUSE THE CASE ABOVE CAN BE GREENED BY THE WRONG CURE.** Routing
`__con_read_stdin` through the existing `__gt_io_park`/IOCP road would reorder those two lines without a
processor ever being retaken — a real improvement, and a **different** rung's. `__Builtins.schedRetakeCount()`
sums a per-P counter (the `schedStealCount` shape, so no `.data` word and no golden churn) and answers how
many times a `sysmon` actually took a processor away from a machine stuck in the kernel.

⇒ **`retaken=yes` is the claim that a processor was handed off, not merely that the output came out in a
better order.** It is a `> 0` test rather than a fixed count deliberately: how many retakes a run needs is
a scheduling detail, and pinning the number would make the case a hostage to the sysmon polling interval.
```maxon
typealias Integer = int(i64.min to i64.max)

type Reader
	var id as Integer

	static function create() returns Self
		return Self{id: 0}
	end 'create'

	export function read() returns Integer
		let line = try Console.stdin().readLine() otherwise ""
		print("R: read returned\n")

		return line.count()
	end 'read'
end 'Reader'

type Sentinel
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		print("S: sentinel ran\n")

		return 1
	end 'ping'
end 'Sentinel'

function main() returns ExitCode
	let reader = spawn Reader.create()
	let sentinel = spawn Sentinel.create()

	let blocked = reader.read()
	let tailReply = sentinel.ping()

	let tail = try await tailReply otherwise 0
	let n = try await blocked otherwise 0

	var mark = "no"
	if __Builtins.schedRetakeCount() > 0 'retaken'
		mark = "yes"
	end 'retaken'
	print("done sibling={tail} read={n} retaken={mark}\n")

	return 0 as ExitCode
end 'main'
```
```stdout
S: sentinel ran
R: read returned
done sibling=1 read=5 retaken=yes
```
```exitcode
0
```
