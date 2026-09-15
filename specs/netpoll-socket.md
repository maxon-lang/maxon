---
feature: netpoll-socket
status: experimental
keywords: [socket, netpoll, poller, deadline, park, concurrency, tcp, green-threads, scheduler, listener, accept]
category: system
---

# A socket operation parks its green thread on the poller

## Documentation

A socket is registered with the network poller and driven non-blocking, so `connect`, `recv` and `send`
PARK the green thread that issued them and hand the processor back. The property is that a green thread
waiting on a socket holds NO machine: it is suspended on the poller until the descriptor is readable or
writable, and never sits inside a kernel call holding the processor it was running on.

`__Builtins.schedNetpollBlockCount()` counts those parks — how many times a green thread has been
suspended on the network poller waiting for a socket to become ready. It joins the scheduler-state roster
beside `__Builtins.schedRetakeCount()` and `__Builtins.schedPreemptCount()`, which count the sysmon
rescues a blocking socket call provokes instead.

A parked socket read carries a DEADLINE: `setReadDeadline(milliseconds)` and
`setWriteDeadline(milliseconds)` on `TcpClient` (and on `__ManagedSocket` beneath it) end the wait with
`timedOut` when the peer stays silent; `0` clears the deadline.

The listening side parks the same way. `TcpListener.bind(host, port: p)` binds and listens, and `accept()`
suspends its green thread on the poller until a connection arrives. `p` of `0` asks the kernel for an
ephemeral port and `port()` reports the one it gave, which is how a program reaches a listener whose
address it did not choose — and how two of these cases running at once cannot collide.

## Tests

<!-- test: netpoll-socket.n-concurrent-reads-do-not-serialise -->
<!-- procs: 1 -->
**FOUR ECHO ROUND-TRIPS IN FLIGHT AT ONE PROCESSOR, AND THE MONITOR NEVER FIRES.** A blocking `recv` holds the
only machine there, so four round-trips cost four times one round-trip and sysmon retakes the processor out of
the kernel call; a reader parked on the poller holds nothing, so the four overlap and nothing is retaken.

⚠⚠ **ONE ROUND-TRIP RUNS BEFORE THE WINDOW OPENS, BECAUSE THE WINDOW MUST HOLD THE SOCKET BAND AND NOTHING
ELSE.** A retake is sysmon doing its job for a genuinely blocking call, and a lane's FIRST crossing carries
work that is not this band: `getaddrinfo` is `SyscallClass.blocking` and may consult a hosts file however
numeric the address, and Winsock's one-time startup sits behind the first socket of the process. Those are
real blocking calls and sysmon is right to retake them — they simply say nothing about what a parked `recv`
does to its processor. So the warm-up pays for all of it, and the four measured round-trips cross a window
that holds socket reads alone. MEASURED: without it, 3 of 25 runs on x64-windows report a retake or a
preemption; with it, 20 of 20 report neither.

⚠ **THE ASSERTION IS EXACTLY ZERO AND MUST STAY SO.** A tolerance would hide the regression this case exists
to catch — the whole reading is *"the monitor never had to rescue a machine"*, and *"rarely had to"* is the
symptom, not the cure.

⭐ **ITS RED IS BEHAVIOURAL — `stuck=true`, the monitor rescuing a machine out of a socket read — RATHER THAN
A MISSING INTRINSIC.** It asserts what a socket call does to the processor it runs on, and a file whose every
case died at the same `E3004` would witness nothing about that.
```maxon
typealias Tally = int(0 to u64.max)

// Four in flight at once plus the warm-up, and the peer is done when it has answered exactly that many.
let rounds = 5

function echoRounds(listener TcpListener) returns Tally
	var served = 0

	while served < rounds 'serving'
		let conn = try listener.accept() otherwise return served
		let heard = try conn.recv(1024) otherwise return served
		_ = try conn.send(heard) otherwise return served
		served = served + 1
	end 'serving'

	return served
end 'echoRounds'

function echoOnce(listener TcpListener, n Tally) returns Tally throws NetworkError
	let client = try TcpClient.connect("127.0.0.1", port: listener.port())
	let msg = "maxon line {n}\n"
	_ = try client.send(msg)
	let response = try client.recv(1024)

	if response == msg 'echoed'
		return 1
	end 'echoed'

	return 0
end 'echoOnce'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1

	let peer = async echoRounds(listener)
	_ = try echoOnce(listener, n: 0) otherwise 0

	let beforeRetake = __Builtins.schedRetakeCount()
	let beforePreempt = __Builtins.schedPreemptCount()

	let p1 = async echoOnce(listener, n: 1)
	let p2 = async echoOnce(listener, n: 2)
	let p3 = async echoOnce(listener, n: 3)
	let p4 = async echoOnce(listener, n: 4)

	let r1 = try await p1 otherwise 0
	let r2 = try await p2 otherwise 0
	let r3 = try await p3 otherwise 0
	let r4 = try await p4 otherwise 0
	let ok = r1 + r2 + r3 + r4

	let retaken = __Builtins.schedRetakeCount() - beforeRetake
	let preempted = __Builtins.schedPreemptCount() - beforePreempt
	let stuck = (retaken + preempted) > 0

	let served = await peer

	if served != rounds 'peer'
		panic("unreachable: every round-trip that returned was answered by this peer")
	end 'peer'

	print("ok={ok} stuck={stuck}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
ok=4 stuck=false
```
```exitcode
0
```

<!-- test: netpoll-socket.a-parked-reader-is-on-the-poller -->
<!-- procs: 1 -->
**THE `blocked` WITNESS ON ITS OWN: THE PARKS ARE COUNTED, NOT INFERRED FROM A MISSING SYMPTOM.** Its sibling's
`stuck=false` says only that the monitor never had to rescue a machine, and a reader busy-waiting on a
non-blocking socket satisfies that just as well as a reader parked on the poller. This case counts the parks
themselves: two concurrent round-trips must leave at least one poller park each behind them, and both must
still answer correctly — a counter that rises while the data is wrong is measuring the wrong thing.

⚠ **THE PEER RUNS ONLY WHEN A READER YIELDS, WHICH IS WHAT MAKES THE FLOOR A FLOOR.** It shares the readers'
strand, so a reader's `recv` is always issued before anything has been echoed to it and always finds the
socket empty. The peer's own parks can only add to the count above that floor.
```maxon
typealias Tally = int(0 to u64.max)

// One poller park per reader is the floor: a reader that held its machine never reached the poller at all.
let readers = 2

function echoRounds(listener TcpListener) returns Tally
	var served = 0

	while served < readers 'serving'
		let conn = try listener.accept() otherwise return served
		let heard = try conn.recv(1024) otherwise return served
		_ = try conn.send(heard) otherwise return served
		served = served + 1
	end 'serving'

	return served
end 'echoRounds'

function echoOnce(listener TcpListener, n Tally) returns Tally throws NetworkError
	let client = try TcpClient.connect("127.0.0.1", port: listener.port())
	let msg = "maxon line {n}\n"
	_ = try client.send(msg)
	let response = try client.recv(1024)

	if response == msg 'echoed'
		return 1
	end 'echoed'

	return 0
end 'echoOnce'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1

	let beforeParks = __Builtins.schedNetpollBlockCount()

	let peer = async echoRounds(listener)
	let p1 = async echoOnce(listener, n: 1)
	let p2 = async echoOnce(listener, n: 2)

	let r1 = try await p1 otherwise 0
	let r2 = try await p2 otherwise 0
	let ok = r1 + r2

	let parks = __Builtins.schedNetpollBlockCount() - beforeParks
	let blocked = parks >= readers

	let served = await peer

	if served != readers 'peer'
		panic("unreachable: every round-trip that returned was answered by this peer")
	end 'peer'

	print("ok={ok} blocked={blocked}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
ok=2 blocked=true
```
```exitcode
0
```

<!-- test: netpoll-socket.a-read-deadline-fires -->
**A READ DEADLINE ANSWERS `timedOut`, AND PROMPTLY.** The peer accepts the connection and sends nothing, so a
read with no deadline waits for the kernel's own TCP timeout — minutes. The 300 ms deadline must end it, and
`prompt` is what tells a deadline that fired from one the kernel eventually supplied.

⚠ **THE PEER OUTLIVES THE DEADLINE BECAUSE THE READER IS WHAT ENDS IT.** A peer that closed on a timer of its
own would race the deadline, and a closed connection answers `connectionClosed` — the wrong reading, reached
for the wrong reason. So the peer parks on a read of its own and is released by the reader's close, which can
only happen after the deadline has already fired.

⚠ The `default` arm says *unreachable*, and it has to say so with `panic`: a `default throws` in a match
whose error has nowhere to go is silently discarded, and with a payload-carrying error it leaks the box.
```maxon
// Far above the 300 ms deadline and far below any kernel-side idle-read timeout: an elapsed reading under
// this can only have come from the deadline itself.
let promptMs = 3000

function holdSilently(listener TcpListener) returns ExitCode
	let conn = try listener.accept() otherwise return 1
	try conn.recv(1024) otherwise ignore

	return 0
end 'holdSilently'

function readPastTheDeadline(listener TcpListener) returns ExitCode throws NetworkError
	let client = try TcpClient.connect("127.0.0.1", port: listener.port())
	try client.setReadDeadline(300)

	var fired = false
	let start = Clock.nowMs()

	try client.recv(1024) otherwise (e) 'readErr'
		match e 'check'
			timedOut then fired = true
			default panic("unreachable: nothing was sent, so only the read deadline can end this recv")
		end 'check'
	end 'readErr'

	let tookMs = Clock.elapsedMs(start)
	client.close()

	print("timedOut={fired} prompt={tookMs < promptMs}\n")
	return 0
end 'readPastTheDeadline'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1

	let peer = async holdSilently(listener)
	let result = try readPastTheDeadline(listener) otherwise 1
	let held = await peer

	if held != 0 'peer'
		panic("unreachable: the connection whose read ran out of time was accepted by this peer")
	end 'peer'

	return result
end 'main'
```
```stdout
timedOut=true prompt=true
```
```exitcode
0
```

<!-- test: netpoll-socket.drop-a-parked-reader -->
<!-- procs: 1 -->
**A PROMISE DROPPED WHILE ITS COROUTINE IS PARKED IN `recv` DOES NOT HANG THE EXIT.** `main` spawns a reader
that connects and then reads from a peer that never writes, sleeps long enough for the reader to reach the
read, and lets the promise drop at scope exit without ever awaiting it. The drop must unregister the
reader from the poller and RENOUNCE it: a reader still registered when its stack is gone is a use-after-free
the next readiness event walks into, and a reader the drop cannot reach at all leaves the exit's join waiting
forever — the case TIMES OUT rather than failing.

⚠ **THE LISTENER IS THE PEER, AND IT NEVER ACCEPTS.** The kernel completes the handshake into the backlog, so
the connection is ESTABLISHED and the reader's `recv` genuinely parks — while nothing on the other side can
ever write to it, or close it out from under the drop this case is about.

⚠ **THE STACK IS FREED BY THE THREAD'S OWN COMPLETION, NOT BY THE DROP.** Nothing can unwind a SUSPENDED
frame from outside, so a drop that freed the stack would strand every heap value the reader's locals own — the
socket, the receive buffer, the client. The drop therefore EXPIRES the wait and readies the thread: it resumes,
answers `timedOut`, unwinds its own frame, and its runner reclaims it. The exit code is the witness — a leaked
box reports 101.

⚠ **`parked > 0` IS WHAT MAKES THIS A POLLER CASE.** A reader that merely yielded at the I/O point ahead of
its kernel call is also "not complete" and is also reachable by the drop, so the `gtIsComplete` peek alone
reads GREEN against a runtime with no poller at all — MEASURED, by running exactly that program. Only the
counter separates a promise dropped off the POLLER from one dropped at a plain I/O yield.

⚠ **`reader` MUST BE BOUND.** `_ = async stall()` discards the promise at its own statement, before `main`
sleeps: `stall` never runs, never opens a socket, and the drop takes the never-ran arm. The peek pins that
the reader really was still parked when it was dropped — a completed thread reads `1`.
```maxon
function stall(listener TcpListener) returns ExitCode throws NetworkError
	let client = try TcpClient.connect("127.0.0.1", port: listener.port())
	_ = try client.recv(1024)

	return 0
end 'stall'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1

	let before = __Builtins.schedNetpollBlockCount()
	let reader = async stall(listener)
	sleep(200)
	let parked = __Builtins.schedNetpollBlockCount() - before
	let stillWaiting = __Builtins.gtIsComplete(reader.inner) == 0

	print("dropped={parked > 0 and stillWaiting}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
dropped=true
```
```exitcode
0
```

<!-- test: netpoll-socket.a-parked-reader-survives-its-owner-closing-the-socket -->
<!-- procs: 1 -->
**AN `async` ARGUMENT CO-OWNS THE SOCKET BOX, SO THE OWNER CAN CLOSE IT WHILE THE COROUTINE IS PARKED ON IT.**
A coroutine shares its spawner's strand — only one of the two RUNS at a time — but a PARKED coroutine and a
running owner are exactly the pair that can be alive at once, and the `async` door takes a reference on the box
rather than a copy of the descriptor (`Parser.coOwnConcreteRecordForSink`). So `client.close()` reaches
`__np_pd_release` on a descriptor with a published waiter, which is a state a program can build.

⭐ **THE RELEASE READIES THE WAITER, IT DOES NOT STRAND IT AND IT DOES NOT ABORT.** A direction word holding a
green thread is the only pointer anything has to it, so the release resumes that thread with every word of the
record back to its born state and the record's generation bumped: the resumed wait sees a record that has
moved on, answers `NetpollWaitStale`, and the operation reports its own failure — `recvFailed`, never
`timedOut` — having issued no syscall on a descriptor that may already name another socket. Dropping the
waiter would hang the exit with the deadlock detector disarmed, and aborting would kill a program that did
nothing wrong.

⚠ **THE LISTENER IS THE PEER, AND IT NEVER ACCEPTS.** The kernel completes the handshake into the backlog, so
the read parks on an ESTABLISHED connection that nothing on the far side can write to or close — the only
thing that ends it is the owner closing under it, which is the state under test.

⚠ **`parked > 0` IS WHAT MAKES THIS A POLLER CASE.** Without it, a reader that merely yielded ahead of its
kernel call — or one that never ran — satisfies the rest of the program just as well, and the case would read
green against a release that never met a waiter at all.
```maxon
// Exactly the four outcomes the match below can produce: 0 for a read that returned data, and one per
// `NetworkError` variant a closed-under-it read can raise.
typealias ReadOutcome = int(0 to 3)

// Nothing on the far side can ever write, so this read parks and stays parked until the owner closes under it.
function readUntilClosed(client TcpClient) returns ReadOutcome
	var code = 0

	try client.recv(1024) otherwise (e) 'readErr'
		match e 'which'
			recvFailed then code = 1
			connectionClosed then code = 2
			timedOut then code = 3
			default panic("unreachable: a read whose socket closes under it ends in one of the three above")
		end 'which'
	end 'readErr'

	return code
end 'readUntilClosed'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let client = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 1

	let before = __Builtins.schedNetpollBlockCount()
	let reader = async readUntilClosed(client)
	sleep(200)
	let parked = __Builtins.schedNetpollBlockCount() - before

	client.close()
	let code = await reader

	print("parked={parked > 0} code={code}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
parked=true code=1
```
```exitcode
0
```

<!-- test: netpoll-socket.a-readied-waiter-does-not-clobber-the-next-owner-of-its-descriptor -->
<!-- procs: 1 -->
<!-- unsupported-targets: x64-windows -->
**A RELEASE LEAVES ITS RECORD IN THE TABLE, SO THE NEXT SOCKET ON THAT DESCRIPTOR INHERITS IT — AND THE
THREAD THE RELEASE READIED IS STILL HOLDING A POINTER TO IT.** `__np_pd_release` readies the waiter and
returns the record's words to their born state, but the table slot goes on naming that record, so
`__np_pd_open` on a recycled descriptor hands the SAME record to the next socket. A resumed waiter that
writes its direction word without first asking whether the record is still its own writes over whatever the
new owner published there — and a direction word is the only pointer anything holds to a parked thread, so
the new owner is stranded with `__np_waiters` still counting it: a hang with the deadlock detector disarmed.

⭐ **THE DESCRIPTOR REUSE IS DETERMINISTIC, NOT HOPEFUL.** POSIX requires `socket()` to return the
LOWEST-NUMBERED descriptor not currently open by the process. Every other descriptor this program holds —
the listener, and the socket the peer accepted for the first connection — was opened BEFORE the one that is
closed here, and neither `close` nor the `connect` that follows it yields, so the peer cannot release its own
in between. The closed descriptor is therefore the lowest free one when the second connection is made, and
that connection takes it.

⚠ **THE PEER MUST OUTLAST A CONNECTION THAT DIES WITHOUT SPEAKING.** The first connection is closed under its
own reader without ever writing, so the peer's read of it ends in an error; the peer is finished only once it
has ECHOED, which is the second connection.

⚠ **THE WITNESS IS THAT THE SECOND CONNECTION'S READ COMPLETES AT ALL.** A stranded reader is not a wrong
answer, it is a thread nothing can ever wake, so the shape of the failure is the case never finishing —
the runner reports it as TIMED OUT rather than as a mismatch. The echo is asked for before it is read so that
a read on a HEALTHY socket has an answer waiting for it, which is what makes "never returned" mean the strand
and not the peer.

⚠⚠ **IT IS A GUARD OVER AN INVARIANT, NOT A REPRODUCTION.** On one strand the readied thread runs at the
owner’s very next yield, which is inside the second `connect` — before that socket publishes anything — so
this ordering does not reach the clobber even with the generation test removed, and the case passes either
way. What it holds is the descriptor reuse itself: nothing else in the corpus closes a socket and opens
another onto the same record, so without it the recycle path has no coverage at all.

⚠ **x64-windows IS MARKED BECAUSE A SOCKET HANDLE THERE CARRIES NO LOWEST-FREE PROMISE.** The reuse this
case rests on is POSIX's rule that `socket()` returns the lowest-numbered free descriptor; Winsock says
nothing of the kind, so closing one socket and opening another there need not land on the same record and
the state this case is about would not be built. Its subject is the generation test, which every polled
lane runs — the descriptor reuse is only how this file reaches it.

```maxon
typealias ReadOutcome = int(0 to 3)
typealias Answered = int(0 to 1)

// Nothing is ever sent on this connection, so this read parks and is still parked when the owner closes.
function readNothing(client TcpClient) returns ReadOutcome
	var code = 0

	try client.recv(1024) otherwise (e) 'readErr'
		match e 'which'
			recvFailed then code = 1
			connectionClosed then code = 2
			timedOut then code = 3
			default panic("unreachable: a read whose socket closes under it ends in one of the three above")
		end 'which'
	end 'readErr'

	return code
end 'readNothing'

function echoUntilAnswered(listener TcpListener) returns Answered
	var answered = 0

	while answered == 0 'serving'
		let conn = try listener.accept() otherwise return 0
		let heard = try conn.recv(1024) otherwise continue
		_ = try conn.send(heard) otherwise return 0
		answered = 1
	end 'serving'

	return answered
end 'echoUntilAnswered'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async echoUntilAnswered(listener)

	let first = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 1

	let stale = async readNothing(first)
	sleep(200)
	first.close()

	// The lowest free descriptor is the one just closed, so this connection inherits the released record.
	var second = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 2
	_ = try second.send("reuse\n") otherwise return 3
	let echo = try second.recv(1024) otherwise return 4

	let outcome = await stale
	let answered = await peer

	if answered != 1 'peer'
		panic("unreachable: the read that returned was answered by this peer")
	end 'peer'

	let echoed = echo == "reuse\n"
	print("echoed={echoed} outcome={outcome}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
echoed=true outcome=1
```
```exitcode
0
```

<!-- test: netpoll-socket.two-readers-on-one-socket-is-a-named-stop -->
<!-- procs: 1 -->
**TWO GREEN THREADS READING ONE SOCKET IS A PROGRAM ERROR, AND IT STOPS WITH A NAMED ABORT.** A direction
word holds ONE waiter, so a second wait on the same direction would overwrite the first's publication and
the first would never be woken again. `__np_pd_wait` refuses that outright rather than losing a thread
silently — `RuntimeAbort.netpollDoubleWait`, exit **107**.

⭐ **AND THE REFUSAL IS RIGHT HERE WHERE ITS NEIGHBOUR'S WAS NOT.** Two readers racing one byte stream have
no defined answer — whichever wakes first takes bytes the other was going to return — so naming the stop is
the service. That is the opposite of a release finding a parked waiter, which happens to a program that did
nothing wrong and is answered by readying the waiter rather than by stopping.

⚠ **THE LISTENER IS THE PEER, AND IT NEVER ACCEPTS.** The handshake completes into the backlog, so both reads
are issued on an ESTABLISHED connection that nothing can ever answer — the abort is what ends the program, not
a peer that hung up.

⚠ **THE ORDER IS DETERMINISTIC BECAUSE THE SLEEP IS THE HANDOVER.** The coroutine shares its spawner's
strand and runs when the spawner yields, so the `sleep` is what lets it reach its read and publish itself
on the direction word; the owner's own read then arrives second, every time.
```maxon
typealias ReadOutcome = int(0 to 3)

function readNothing(client TcpClient) returns ReadOutcome
	var code = 0

	try client.recv(1024) otherwise (e) 'readErr'
		match e 'which'
			recvFailed then code = 1
			connectionClosed then code = 2
			timedOut then code = 3
			default panic("unreachable: a read on a socket nothing writes to ends in one of the three above")
		end 'which'
	end 'readErr'

	return code
end 'readNothing'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let client = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 1

	let first = async readNothing(client)
	sleep(200)

	// The coroutine is published on this socket's READ direction, so this second read is the refusal.
	try client.recv(1024) otherwise ignore

	let unreachable = await first
	return unreachable as ExitCode
end 'main'
```
```exitcode
107
```

<!-- test: netpoll-socket.a-reader-arriving-before-an-ended-reader-resumes-is-the-same-named-stop -->
<!-- procs: 1 -->
**A WAIT HOLDS ITS DIRECTION UNTIL ITS OWN THREAD RESUMES, SO A SECOND READER ARRIVING AFTER THE FIRST WAS
ENDED BUT BEFORE IT RAN IS STILL TWO READERS, AND STOPS THE SAME WAY.** Whatever ends a wait — readiness, its
deadline, a cancelled promise — readies the thread, but that thread has not answered yet: it resumes, settles
the direction word and only then returns. A second wait on the same direction in that window meets the reader
that still holds it — `RuntimeAbort.netpollDoubleWait`, exit **107** — exactly as it would a moment earlier,
while the first was still published. A word that read *nothing waits* in the window would make the refusal a
matter of timing: the second reader would publish itself there, and the first reader's resume would erase it —
a thread nothing can wake, with `__np_waiters` still counting it, so the program hangs with the deadlock
detector disarmed.

⭐ **THE WINDOW IS BUILT, NOT HOPED FOR.** The first reader is parked on the socket's read direction, and the
coroutine that started it keeps itself runnable. `main` announces its read through `phase` and issues it, and
the read opens with a genuine suspension, during which the canceller is the one member the strand can run: it
cancels the first reader, which ends that wait and queues the reader, and yields. A yield fires what is due
before it places the yielder, so `main`'s own resumption is fired there — and a readied OWNER joins the front
of its strand's queue where a coroutine joins the back. `main` therefore finds the socket empty and reaches its
wait while the first reader's has ended and the reader has not yet run.

⚠ **A MACHINE WAITING IN THE POLLER CAN FIRE `main`'s RESUMPTION BEFORE THE CANCELLER RUNS**, since registering
it breaks that wait. `main` then meets the first reader still published and stops by the ordinary road — the
same exit, which is the property: which of the two roads a run takes is the scheduler's business, and both end
in the one named stop.

⚠ **THE WAIT HAS TO END BY SOMETHING THE STRAND DOES ITSELF.** Every socket operation opens with that
suspension, and it runs every readied member of the strand first. Readiness and a deadline are processed by
whichever machine is polling, at a time the program does not choose, and one landing before the second reader's
suspension is settled there, before its call is issued. A cancel is issued by the strand, at a point the program
picks.

⚠ **A RUNTIME THAT FREES THE WORD WHEN THE WAIT ENDS DOES NOT STOP.** `main` publishes itself on the word, the
cancelled reader's resume erases it, and `main` waits for ever — the runner reports the case TIMED OUT.
```maxon
typealias ReadOutcome = int(0 to 3)

// 0 until `main` is about to issue its own read.
var phase = 0

function readOutcome(client TcpClient) returns ReadOutcome
	var code = 0

	try client.recv(1024) otherwise (e) 'readErr'
		match e 'which'
			recvFailed then code = 1
			connectionClosed then code = 2
			timedOut then code = 3
			default panic("unreachable: a read on a socket nothing writes to ends in one of the three above")
		end 'which'
	end 'readErr'

	return code
end 'readOutcome'

// Yielding rather than sleeping keeps this coroutine runnable, so it is what runs while `main`'s read is
// suspended.
function cancelFirstReader(client TcpClient) returns ReadOutcome
	let first = async readOutcome(client)

	while phase == 0 'untilMainReads'
		Runtime.yield()
	end 'untilMainReads'

	first.cancel()
	Runtime.yield()
	return 0
end 'cancelFirstReader'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let client = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 1

	let canceller = async cancelFirstReader(client)
	sleep(200)

	// The first reader's wait is ended under this read's own suspension, before the reader runs.
	phase = 1
	let second = readOutcome(client)
	let unreachable = await canceller
	print("second={second} canceller={unreachable}\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
107
```

<!-- test: netpoll-socket.a-loopback-echo-service-accepts-and-answers -->
<!-- procs: 1 -->
**A LISTENER IN THIS PROCESS IS A PEER LIKE ANY OTHER: BIND, ACCEPT, ANSWER.** The service side of the poller
has to compose with the client side end to end — a bound listener, an `accept` that yields a socket of its
own, and a round-trip over that socket that comes back byte for byte. This is the floor every other listener
case builds on, and the one that says the two halves meet.

⚠ **THE PORT IS THE KERNEL'S TO CHOOSE.** `port: 0` takes an ephemeral one and `port()` reports it, so two
spec workers running this file at the same time cannot land on the same address — a fixed number would turn
a collision into a failure that looks like a defect.
```maxon
typealias Served = int(0 to 1)

function serveOne(listener TcpListener) returns Served
	let conn = try listener.accept() otherwise return 0
	let heard = try conn.recv(1024) otherwise return 0
	_ = try conn.send(heard) otherwise return 0

	return 1
end 'serveOne'

function roundTrip(listener TcpListener) returns bool
	let payload = "maxon loopback\n"

	let client = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return false
	_ = try client.send(payload) otherwise return false
	let answer = try client.recv(1024) otherwise return false

	return answer == payload
end 'roundTrip'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1

	let peer = async serveOne(listener)
	let echoed = roundTrip(listener)
	let served = await peer

	print("echoed={echoed} served={served}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
echoed=true served=1
```
```exitcode
0
```

<!-- test: netpoll-socket.a-parked-accept-is-on-the-poller -->
<!-- procs: 1 -->
**THE PARKS ARE COUNTED ON THE ACCEPTING SIDE TOO.** A listener that answers is not yet a listener that PARKS:
an `accept` spun on in a loop, or one left inside a blocking kernel call while sysmon rescues the machine,
answers its connection just as well. The counter is the only thing that separates an accept suspended on the
poller from either of those, and the delta across the accept is what this case reads.

⚠ **THE DIAL WAITS FIRST, SO THE ACCEPT IS MEASURED WHILE IT IS PARKED AND NOT MERELY ABOUT TO BE.** A
connection already sitting in the backlog is answered without the poller ever being involved, and the count
would then say nothing.
```maxon
typealias Served = int(0 to 1)

// One poller park is the floor: an accept that held its machine never reached the poller at all.
let acceptors = 1

// Long enough that the accept is already suspended when the connection arrives.
let settleMs = 200

function acceptOne(listener TcpListener) returns Served
	let conn = try listener.accept() otherwise return 0
	_ = try conn.recv(1024) otherwise return 0

	return 1
end 'acceptOne'

function dialAfterSettling(listener TcpListener) returns bool
	sleep(settleMs)

	let client = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return false
	_ = try client.send("knock\n") otherwise return false

	return true
end 'dialAfterSettling'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1

	let beforeParks = __Builtins.schedNetpollBlockCount()

	let acceptor = async acceptOne(listener)
	let dialer = async dialAfterSettling(listener)

	let served = await acceptor
	let dialled = await dialer

	let parks = __Builtins.schedNetpollBlockCount() - beforeParks
	let parked = parks >= acceptors

	print("served={served} dialled={dialled} parked={parked}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
served=1 dialled=true parked=true
```
```exitcode
0
```

<!-- test: netpoll-socket.a-listener-serves-two-connections-in-turn -->
<!-- procs: 1 -->
**EACH ACCEPTED SOCKET OWNS ITS OWN DESCRIPTOR, AND THE ANSWER COMES BACK ON THE ONE THE QUESTION ARRIVED ON.**
Both connections are accepted BEFORE either is answered, so the peer holds two live sockets at once — an
`accept` that handed back the listener's own descriptor, or the previous connection's, would cross the two
payloads, and each side checking for its OWN bytes is what catches that.

⚠ **NOTHING HERE DEPENDS ON WHICH CLIENT CONNECTS FIRST.** The peer echoes whatever arrives on each socket,
so the verdict is per-connection identity and not an ordering the scheduler happens to give.
```maxon
typealias Served = int(0 to 2)

function serveBoth(listener TcpListener) returns Served
	let earlier = try listener.accept() otherwise return 0
	let later = try listener.accept() otherwise return 0

	var served = 0

	let fromEarlier = try earlier.recv(1024) otherwise return served
	_ = try earlier.send(fromEarlier) otherwise return served
	served = served + 1

	let fromLater = try later.recv(1024) otherwise return served
	_ = try later.send(fromLater) otherwise return served
	served = served + 1

	return served
end 'serveBoth'

function roundTrip(listener TcpListener, payload String) returns bool
	let client = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return false
	_ = try client.send(payload) otherwise return false
	let answer = try client.recv(1024) otherwise return false

	return answer == payload
end 'roundTrip'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1

	let peer = async serveBoth(listener)
	let earlier = async roundTrip(listener, payload: "first payload\n")
	let later = async roundTrip(listener, payload: "second payload\n")

	let earlierOk = await earlier
	let laterOk = await later
	let served = await peer

	print("earlier={earlierOk} later={laterOk} served={served}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
earlier=true later=true served=2
```
```exitcode
0
```

<!-- test: netpoll-socket.drop-a-parked-accept -->
<!-- procs: 1 -->
**AN ACCEPT IS DROPPED FROM THE POLLER THE SAME WAY A READ IS.** A listener registers on the poller for
readability just as a socket does, so a coroutine suspended in `accept` is reachable by exactly the machinery
its reading sibling is — and a drop that knows about sockets but not about listeners strands a thread nothing
can ever wake, which the exit's join then waits on forever.

⚠ **THE LISTENER MUST SURVIVE ITS DROPPED ACCEPT.** The coroutine co-owns the listener box, so a drop that
took the box down with the wait would close a listener its owner is still holding — `bound` asks the listener
for its port after the drop and gets the same answer it gave before.

⚠ **THE EXIT CODE IS DISTINCTIVE ON PURPOSE.** It is produced at exactly one place, the end of `main`, so a
leaked box (101), a named abort, or a hang cannot be mistaken for the clean exit this case is about.

⚠ **`parked > 0` IS WHAT MAKES THIS A POLLER CASE.** An accept that merely yielded ahead of its kernel call is
also "not complete" and is also reachable by the drop, so the `gtIsComplete` peek alone reads green against a
runtime whose listener never reached the poller at all.
```maxon
let cleanExit = 42

function stall(listener TcpListener) returns ExitCode throws NetworkError
	_ = try listener.accept()

	return 0
end 'stall'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let port = listener.port()

	let before = __Builtins.schedNetpollBlockCount()
	let acceptor = async stall(listener)
	sleep(200)
	let parked = __Builtins.schedNetpollBlockCount() - before
	let stillWaiting = __Builtins.gtIsComplete(acceptor.inner) == 0
	let stillBound = listener.port() == port

	print("dropped={parked > 0 and stillWaiting} bound={stillBound}\n")
	return cleanExit as ExitCode
end 'main'
```
```stdout
dropped=true bound=true
```
```exitcode
42
```

<!-- test: netpoll-socket.two-accepts-on-one-listener-is-a-named-stop -->
<!-- procs: 1 -->
**A LISTENER IS A DESCRIPTOR LIKE ANY OTHER, SO TWO GREEN THREADS ACCEPTING ON ONE OF THEM STOPS WITH THE
SAME NAMED ABORT TWO READERS DO.** An accept waits on the listener's READ direction, and a direction word
holds ONE waiter, so a second accept would overwrite the first's publication and the first would never be
woken again — `RuntimeAbort.netpollDoubleWait`, exit **107**.

⭐ **THE STOP IS THE SERVICE HERE TOO.** Two acceptors racing one backlog have no defined answer — whichever
wakes first takes the connection the other was going to return — so a runtime that let the second wait
through would be trading a named refusal for a thread nobody can ever wake, and a park on a poller source
disarms the deadlock detector, so the exit's join would hang rather than report.

⚠ **NOTHING EVER DIALS THIS LISTENER.** The backlog stays empty, so both accepts are genuinely suspended and
the abort is what ends the program, not a connection that answered one of them.

⚠ **THE ORDER IS DETERMINISTIC BECAUSE THE SLEEP IS THE HANDOVER.** The coroutine shares its spawner's
strand and runs when the spawner yields, so the `sleep` is what lets it reach its accept and publish itself
on the direction word; the owner's own accept then arrives second, every time.
```maxon
typealias AcceptOutcome = int(0 to 1)

function acceptNobody(listener TcpListener) returns AcceptOutcome
	var code = 0

	try listener.accept() otherwise (e) 'acceptErr'
		match e 'which'
			acceptFailed then code = 1
			default panic("unreachable: an accept on a listener nothing dials ends only as an abandoned accept")
		end 'which'
	end 'acceptErr'

	return code
end 'acceptNobody'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1

	let first = async acceptNobody(listener)
	sleep(200)

	// The coroutine is published on this listener's READ direction, so this second accept is the refusal.
	try listener.accept() otherwise ignore

	let unreachable = await first
	return unreachable as ExitCode
end 'main'
```
```exitcode
107
```

<!-- test: netpoll-socket.a-second-bind-of-a-live-port-is-refused -->
<!-- procs: 1 -->
**A LIVE LISTENER OWNS ITS PORT, AND THE SECOND BIND SAYS SO BY NAME.** A `bind` that quietly succeeded onto a
port already being listened on would split the backlog between two listeners and make which one accepts a
given connection a matter of luck. The refusal is the contract, and it has to arrive as a `NetworkError` the
program can match on rather than as a listener that answers nothing.

⚠ **THE PORT IS THE ONE THE KERNEL GAVE, NOT ONE THIS CASE PICKED.** Binding `0` and reading `port()` back is
what makes "already in use" mean this program's own listener and not another worker's.

⚠ The `default` arm says *unreachable*, and it has to say so with `panic`: a `default throws` in a match whose
error has nowhere to go is silently discarded.
```maxon
function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let port = listener.port()

	var refused = false

	try TcpListener.bind("127.0.0.1", port: port) otherwise (e) 'again'
		match e 'which'
			bindFailed then refused = true
			default panic("unreachable: a port this process is already listening on can only refuse a bind")
		end 'which'
	end 'again'

	print("refused={refused}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
refused=true
```
```exitcode
0
```

