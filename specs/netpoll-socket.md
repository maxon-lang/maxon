---
feature: netpoll-socket
status: experimental
keywords: [socket, netpoll, poller, deadline, park, concurrency, tcp, green-threads, scheduler]
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

## Tests

<!-- test: netpoll-socket.n-concurrent-reads-do-not-serialise -->
<!-- procs: 1 -->
<!-- network: live -->
<!-- unsupported-targets: x64-windows -->
**FOUR ECHO ROUND-TRIPS IN FLIGHT AT ONE PROCESSOR, AND THE MONITOR NEVER FIRES.** A blocking `recv` holds the
only machine there, so four round-trips cost four times one round-trip and sysmon retakes the processor out of
the kernel call; a reader parked on the poller holds nothing, so the four overlap and nothing is retaken.

⚠⚠ **THE WINDOW COVERS THE SOCKET BAND AND NOT THE RESOLVER, WHICH IS WHAT THE WARM-UP IS FOR.** A retake is
sysmon doing its job for a genuinely blocking call, and `__ms_resolve4` makes four of them per connect that have
nothing to do with this case: it reads `/etc/hosts` and `/etc/resolv.conf`, and a file read IS
`SyscallClass.blocking`. MEASURED at one processor: with the socket band polled, a send/recv window reads zero
retakes in 6 of 6 runs while a connect window — the resolver's — reads one in roughly one run of eight. So the
witness opens AFTER a throwaway connect to the same host: the resolver's files are in the page cache and its
first-touch cost is spent outside the window, and what remains inside it is the socket band alone.

⚠ **THE ASSERTION IS EXACTLY ZERO AND MUST STAY SO.** A tolerance would hide the regression this case exists
to catch — the whole reading is *"the monitor never had to rescue a machine"*, and *"rarely had to"* is the
symptom, not the cure.

⚠ **x64-windows IS MARKED BECAUSE ITS SOCKETS REALLY ARE BLOCKING** and a `recv` there really does hold its
machine — overlapped I/O through the completion port is a mechanism of its own, and a rung of its own.

⭐ **THIS CASE NAMES NOTHING NEW AND MUST COMPILE AND RUN AGAINST ANY COMPILER IN THIS TREE.** It is the one
case in this file whose red is BEHAVIOURAL — `stuck=true`, the monitor rescuing a machine out of a socket
read — rather than a missing intrinsic. A file whose every case died at the same `E3004` would witness
nothing about what a socket call actually does to the processor it runs on.
```maxon
typealias Tally = int(0 to u64.max)

function echoOnce(n Tally) returns Tally throws NetworkError
	let client = try TcpClient.connect("tcpbin.com", port: 4242)
	let msg = "maxon line {n}\n"
	_ = try client.send(msg)
	let response = try client.recv(1024)

	if response == msg 'echoed'
		return 1
	end 'echoed'

	return 0
end 'echoOnce'

function main() returns ExitCode
	// The resolver's two files, read into the page cache before the witness opens — see the note above.
	_ = try echoOnce(0) otherwise 0

	let beforeRetake = __Builtins.schedRetakeCount()
	let beforePreempt = __Builtins.schedPreemptCount()

	let p1 = async echoOnce(1)
	let p2 = async echoOnce(2)
	let p3 = async echoOnce(3)
	let p4 = async echoOnce(4)

	let r1 = try await p1 otherwise 0
	let r2 = try await p2 otherwise 0
	let r3 = try await p3 otherwise 0
	let r4 = try await p4 otherwise 0
	let ok = r1 + r2 + r3 + r4

	let retaken = __Builtins.schedRetakeCount() - beforeRetake
	let preempted = __Builtins.schedPreemptCount() - beforePreempt
	let stuck = (retaken + preempted) > 0

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
<!-- network: live -->
<!-- unsupported-targets: x64-windows -->
**THE `blocked` WITNESS ON ITS OWN: THE PARKS ARE COUNTED, NOT INFERRED FROM A MISSING SYMPTOM.** Its sibling's
`stuck=false` says only that the monitor never had to rescue a machine, and a reader busy-waiting on a
non-blocking socket satisfies that just as well as a reader parked on the poller. This case counts the parks
themselves: two concurrent round-trips must leave at least one poller park each behind them, and both must
still answer correctly — a counter that rises while the data is wrong is measuring the wrong thing.

⚠ **x64-windows IS MARKED BECAUSE NO SOCKET IS REGISTERED WITH ITS POLLER**, so a blocking reader parks on
nothing and the count this case reads would be 0.
```maxon
typealias Tally = int(0 to u64.max)

// One poller park per reader is the floor: a reader that held its machine never reached the poller at all.
let readers = 2

function echoOnce(n Tally) returns Tally throws NetworkError
	let client = try TcpClient.connect("tcpbin.com", port: 4242)
	let msg = "maxon line {n}\n"
	_ = try client.send(msg)
	let response = try client.recv(1024)

	if response == msg 'echoed'
		return 1
	end 'echoed'

	return 0
end 'echoOnce'

function main() returns ExitCode
	let beforeParks = __Builtins.schedNetpollBlockCount()

	let p1 = async echoOnce(1)
	let p2 = async echoOnce(2)

	let r1 = try await p1 otherwise 0
	let r2 = try await p2 otherwise 0
	let ok = r1 + r2

	let parks = __Builtins.schedNetpollBlockCount() - beforeParks
	let blocked = parks >= readers

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
<!-- network: live -->
<!-- unsupported-targets: x64-windows -->
**A READ DEADLINE ANSWERS `timedOut`, AND PROMPTLY.** The connection sends nothing, so the echo peer sends
nothing back and a read with no deadline waits for the kernel's own TCP timeout — minutes. The 300 ms
deadline must end it, and `prompt` is what tells a deadline that fired from one the kernel eventually
supplied.

⚠ The `default` arm says *unreachable*, and it has to say so with `panic`: a `default throws` in a match
whose error has nowhere to go is silently discarded, and with a payload-carrying error it leaks the box.

⚠ **x64-windows IS MARKED BECAUSE A DEADLINE BOUNDS A POLLER WAIT**, and a reader inside a blocking `recv`
there has no such wait to bound.
```maxon
// Far above the 300 ms deadline and far below any kernel-side idle-read timeout: an elapsed reading under
// this can only have come from the deadline itself.
let promptMs = 3000

function readPastTheDeadline() returns ExitCode throws NetworkError
	let client = try TcpClient.connect("tcpbin.com", port: 4242)
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
	print("timedOut={fired} prompt={tookMs < promptMs}\n")
	return 0
end 'readPastTheDeadline'

function main() returns ExitCode
	let result = try readPastTheDeadline() otherwise 1
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
<!-- network: live -->
<!-- unsupported-targets: x64-windows -->
**A PROMISE DROPPED WHILE ITS COROUTINE IS PARKED IN `recv` DOES NOT HANG THE EXIT.** `main` spawns a reader
that connects and then reads from a peer it never wrote to, sleeps long enough for the reader to reach the
read, and lets the promise drop at scope exit without ever awaiting it. The drop must unregister the
reader from the poller and RENOUNCE it: a reader still registered when its stack is gone is a use-after-free
the next readiness event walks into, and a reader the drop cannot reach at all leaves the exit's join waiting
forever — the case TIMES OUT rather than failing.

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

⚠ **x64-windows IS MARKED BECAUSE ITS READER NEVER REACHES THE POLLER**, so the drop this case measures has
nothing there to deregister.
```maxon

function stall() returns ExitCode throws NetworkError
	let client = try TcpClient.connect("tcpbin.com", port: 4242)
	_ = try client.recv(1024)
	return 0
end 'stall'

function main() returns ExitCode
	let before = __Builtins.schedNetpollBlockCount()
	let reader = async stall()
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
<!-- network: live -->
<!-- unsupported-targets: x64-windows -->
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

⚠ **`parked > 0` IS WHAT MAKES THIS A POLLER CASE.** Without it, a reader that merely yielded ahead of its
kernel call — or one that never ran — satisfies the rest of the program just as well, and the case would read
green against a release that never met a waiter at all.

⚠ **x64-windows IS MARKED BECAUSE NO SOCKET IS REGISTERED WITH ITS POLLER**, so its reader parks on nothing,
`__np_pd_release` finds no record, and the state this case is about cannot be reached there.
```maxon
// Exactly the four outcomes the match below can produce: 0 for a read that returned data, and one per
// `NetworkError` variant a closed-under-it read can raise.
typealias ReadOutcome = int(0 to 3)

// The peer echoes and is sent nothing, so this read parks and stays parked until the owner closes under it.
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
	let client = try TcpClient.connect("tcpbin.com", port: 4242) otherwise return 1

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
<!-- network: live -->
<!-- unsupported-targets: x64-windows -->
**A RELEASE LEAVES ITS RECORD IN THE TABLE, SO THE NEXT SOCKET ON THAT DESCRIPTOR INHERITS IT — AND THE
THREAD THE RELEASE READIED IS STILL HOLDING A POINTER TO IT.** `__np_pd_release` readies the waiter and
returns the record's words to their born state, but the table slot goes on naming that record, so
`__np_pd_open` on a recycled descriptor hands the SAME record to the next socket. A resumed waiter that
writes its direction word without first asking whether the record is still its own writes over whatever the
new owner published there — and a direction word is the only pointer anything holds to a parked thread, so
the new owner is stranded with `__np_waiters` still counting it: a hang with the deadlock detector disarmed.

⭐ **THE DESCRIPTOR REUSE IS DETERMINISTIC, NOT HOPEFUL.** POSIX requires `socket()` to return the
LOWEST-NUMBERED descriptor not currently open by the process, so closing the only socket this program holds
and immediately connecting again returns that same number — there is no other free descriptor below it for
the kernel to choose. That is why the case closes exactly one socket and opens exactly one, in that order,
and holds nothing else open in between.

⚠ **THE WITNESS IS THAT THE SECOND CONNECTION'S READ COMPLETES AT ALL.** A stranded reader is not a wrong
answer, it is a thread nothing can ever wake, so the shape of the failure is the case never finishing —
the runner reports it as TIMED OUT rather than as a mismatch. The echo is sent first so that a read on a
HEALTHY socket has an answer waiting for it, which is what makes "never returned" mean the strand and not
the peer.

⚠⚠ **IT IS A GUARD OVER AN INVARIANT, NOT A REPRODUCTION.** On one strand the readied thread runs at the
owner’s very next yield, which is inside the second `connect` — before that socket publishes anything — so
this ordering does not reach the clobber even with the generation test removed, and the case passes either
way. What it holds is the descriptor reuse itself: nothing else in the corpus closes a socket and opens
another onto the same record, so without it the recycle path has no coverage at all.
```maxon
typealias ReadOutcome = int(0 to 3)

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

function main() returns ExitCode
	let first = try TcpClient.connect("tcpbin.com", port: 4242) otherwise return 1

	let stale = async readNothing(first)
	sleep(200)
	first.close()

	// The lowest free descriptor is the one just closed, so this connection inherits the released record.
	var second = try TcpClient.connect("tcpbin.com", port: 4242) otherwise return 2
	_ = try second.send("reuse\n") otherwise return 3
	let echo = try second.recv(1024) otherwise return 4

	let outcome = await stale
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
<!-- network: live -->
<!-- unsupported-targets: x64-windows -->
**TWO GREEN THREADS READING ONE SOCKET IS A PROGRAM ERROR, AND IT STOPS WITH A NAMED ABORT.** A direction
word holds ONE waiter, so a second wait on the same direction would overwrite the first's publication and
the first would never be woken again. `__np_pd_wait` refuses that outright rather than losing a thread
silently — `RuntimeAbort.netpollDoubleWait`, exit **107**.

⭐ **AND THE REFUSAL IS RIGHT HERE WHERE ITS NEIGHBOUR'S WAS NOT.** Two readers racing one byte stream have
no defined answer — whichever wakes first takes bytes the other was going to return — so naming the stop is
the service. That is the opposite of a release finding a parked waiter, which happens to a program that did
nothing wrong and is answered by readying the waiter rather than by stopping.

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
	let client = try TcpClient.connect("tcpbin.com", port: 4242) otherwise return 1

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
