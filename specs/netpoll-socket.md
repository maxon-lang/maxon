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
