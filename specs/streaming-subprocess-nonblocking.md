---
feature: streaming-subprocess-nonblocking
status: experimental
keywords: [subprocess, streaming, StreamingSubprocess, pollExit, ExitPoll, streamHasEnded, takeStdout, takeStderr, ChildOutputReader, LineRead, service, spawn, awaitAny, async, non-blocking, poll, stdio, pipe]
category: system
---

# The streaming child a parent can WATCH — `pollExit`, `streamHasEnded` and a read in a service

## Documentation

`StreamingSubprocess.wait()` parks until the child is gone, and a `ChildOutputReader`'s read parks until
bytes or the end of the stream arrive. A parent that must stay RESPONSIVE — doing other work while the
child still runs, and deciding for itself when to give up — gets its answers two ways:

- `pollExit()` returns `ExitPoll.running`, `ExitPoll.exited(code)`, or `ExitPoll.reapedElsewhere` when
  another process has taken the child's exit status. It does not block and it does not terminate.
- `streamHasEnded(stream)` answers `false` until a read of that stream has met its end, and `true` after.
- A read that may wait runs in a service of its own, one per stream: the reader `takeStdout()` or
  `takeStderr()` hands out moves into the service's factory through an `isolated` parameter, and the owner
  `awaitAny`s over `async` calls that await the service's next read, beside a timer coroutine, while it runs on.

⛔ **A BLANK LINE IS A LINE.** `echo.` writes a line whose text is `""`, and the read answers
`LineRead.line("")`; the end of the stream is `LineRead.ended`. A caller that keyed off emptiness would drop
every blank line the child writes. `a-blank-line-is-a-line-and-not-the-end` is the case that separates the
two.

⛔ **`wait(WaitDeadline.within(ms))` IS NOT A POLL, AND REACHING FOR IT AS ONE IS THE TRAP `pollExit` EXISTS TO CLOSE.**
`wait` takes a `WaitDeadline`: `WaitDeadline.within(ms)` carries a `WaitDeadlineMs`, at least 1, and
`wait()` is the wait with `WaitDeadline.none`, no deadline. A
caller who supplies a small deadline gets the contract's other half: the runtime **TERMINATES** the child
when the deadline fires, with exit code **1**. So both readings of "just poll it with a timeout" are wrong,
one by hanging (`wait()`) and one by killing the subject.
`poll-exit-leaves-the-child-alive` is the case that separates them: it drives the child to its OWN exit
code, and **a `1` there means the poll killed it.**

⚠ **A HANDLE `release()` HAS GIVEN BACK ANSWERS `running`**, because after `release()` there is no child
left to report an exit for, and a read through a reader taken before the release throws
`StreamError.closed`.

**Targets — x64-windows**, except the case marked POSIX: every child here is `cmd`.

**How these children are built.** Each argument is a separate `argv` token and the runtime joins them
with single spaces, quoting only a token that is empty or holds a space, a tab or a `"`. So `&` and
`>nul` travel as their own unquoted tokens and reach `cmd` as the separators and redirections they are.
⚠ Where a token is glued to the next command (`/p=abc<nul&`) that is LOAD-BEARING: `cmd`'s `echo` and
`set /p` preserve a space that precedes a `&`, so a separate `&` token would put one inside the child's
output and the asserted text would no longer be the text the case names.

## Tests

<!-- test: streaming-subprocess-nonblocking.poll-exit-running-then-exited -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
`pollExit()` answers `running` for a live child and `exited(code)` once it is gone, carrying the child's
REAL code. The child waits for a line on its standard input and then exits **42**, so the poll that follows
the spawn lands while it is unambiguously alive, whatever the host is doing: nothing but the parent's own
line can end it. The poll after `wait()` sees a signalled process handle and reports the code. 42 is
distinctive and non-zero, so an `exited` arm that answered a zero-by-default — an unwritten scratch word, a
masked-off upper half — is a different number rather than a plausible one.
```maxon
function describePoll(poll ExitPoll) returns String
	return match poll 'seen'
		running gives "running"
		reapedElsewhere gives "reapedElsewhere"
		exited(code) gives "exited {code}"
	end 'seen'
end 'describePoll'

function gatedChildArguments() returns StringArray
	let argv = StringArray.create(0)
	argv.push("/c")
	argv.push("set")
	argv.push("/p")
	argv.push("released=&")
	argv.push("exit")
	argv.push("/b")
	argv.push("42")
	return argv
end 'gatedChildArguments'

function main() returns ExitCode
	let child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: gatedChildArguments()) otherwise return 3
	let before = describePoll(child.pollExit())

	try child.writeStdinLine("go") otherwise return 5

	let code = try child.wait() otherwise return 4
	let after = describePoll(child.pollExit())
	child.release()

	print("before={before} after={after}\n")
	return code as ExitCode
end 'main'
```
```stdout
before=running after=exited 42
```
```exitcode
42
```

<!-- test: streaming-subprocess-nonblocking.poll-exit-leaves-the-child-alive -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⭐ **THE CASE THE DOOR EXISTS FOR: polling must not be a way of ending the thing being polled.** The same
child, which waits for a line on its standard input, is polled FIVE times — every answer must be `running`,
which is deterministic because the child cannot finish before the parent's own line — and is then allowed to
finish normally. It must report the exit code IT chose, **42**.

⛔ **A `1` HERE MEANS THE POLL KILLED THE CHILD.** `1` is the code the runtime stamps on a child it
terminates when a `wait` deadline fires, so a `pollExit` implemented on top of that path — the
obvious wrong implementation, and the reason this member is not a wrapper — turns this case red with
`code=1` rather than merely failing to answer. Against THIS child, `wait(WaitDeadline.within(50))` throws
`SubprocessError.timeout`, whose rendering opens `timed out after 50ms`, and a following `wait()` answers **1**, so the two outcomes really are
distinguishable by the number and not only in principle. `exitedSeen` staying `-1` is the second half of
the same claim: not one of the five polls claimed an exit that had not happened.
```maxon
typealias PollTally = int(0 to 1000)
typealias SeenCode = int(-1 to i64.max)

function gatedChildArguments() returns StringArray
	let argv = StringArray.create(0)
	argv.push("/c")
	argv.push("set")
	argv.push("/p")
	argv.push("released=&")
	argv.push("exit")
	argv.push("/b")
	argv.push("42")
	return argv
end 'gatedChildArguments'

function main() returns ExitCode
	let child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: gatedChildArguments()) otherwise return 3

	var runningSeen = 0 as PollTally
	var exitedSeen = -1 as SeenCode
	var polls = 0 as PollTally
	while polls < 5 'poll'
		match child.pollExit() 'answer'
			running then runningSeen = runningSeen + 1
			reapedElsewhere then return 6
			exited(code) then exitedSeen = code as SeenCode
		end 'answer'
		polls = polls + 1
	end 'poll'

	try child.writeStdinLine("go") otherwise return 5

	let code = try child.wait() otherwise return 4
	child.release()

	print("runningSeen={runningSeen} exitedSeen={exitedSeen}\n")
	return code as ExitCode
end 'main'
```
```stdout
runningSeen=5 exitedSeen=-1
```
```exitcode
42
```

<!-- test: streaming-subprocess-nonblocking.a-read-in-a-service-leaves-the-owner-running -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
A stdout reader handed to a service of its own parks there while the child has written nothing, and the owner
runs on: it waits for the first of the service's next line and a 50 ms timer, the timer arrives first, and only
then does the owner send the line the child waits for. The service's read then answers the child's line.

⚠ **THE TIMER WINS BY CONSTRUCTION, NOT BY LUCK.** The child writes nothing until it has read a line from its
standard input, and the parent sends that line only after the timer has arrived, so `first` is decided by the
order of the two rather than by how fast either runs. An owner that the read blocked would never see its timer,
and the runner reports the case TIMED OUT.
```maxon
typealias ArrivalPromise = Promise with String
typealias Arrivals = Array with ArrivalPromise

function shown(read LineRead) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
		pastTheCap gives "pastTheCap"
	end 'read'
end 'shown'

type LineService
	var reader as ChildOutputReader

	static function create(reader isolated ChildOutputReader) returns Self
		return Self{reader: merge reader}
	end 'create'

	export function next() returns String
		let read = try self.reader.readLine() otherwise (e) 'unread'
			return "threw {e.name}"
		end 'unread'

		return shown(read)
	end 'next'
end 'LineService'

function nextLine(service LineService.handle) returns String
	return try await service.next() otherwise panic("the line service stopped while its owner held its handle")
end 'nextLine'

function timerAfter(delay Milliseconds) returns String
	sleep(delay)

	return "timer"
end 'timerAfter'

function firstOf(arrivals Arrivals) returns String
	let ready = arrivals.awaitAny()
	let arrived = try arrivals.remove(ready) otherwise panic("awaitAny answered a slot of this array")

	return await arrived
end 'firstOf'

function gatedChildArguments() returns StringArray
	let argv = StringArray.create(0)
	argv.push("/c")
	argv.push("set")
	argv.push("/p")
	argv.push("released=&")
	argv.push("echo")
	argv.push("latehello")
	return argv
end 'gatedChildArguments'

function main() returns ExitCode
	let child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: gatedChildArguments()) otherwise return 3
	let reader = try child.takeStdout() otherwise return 4
	let service = spawn LineService.create(reader)

	let arrivals = Arrivals.create(0)
	arrivals.push(async nextLine(service))
	arrivals.push(async timerAfter(50))

	let first = firstOf(arrivals)
	try child.writeStdinLine("go") otherwise panic("the child reads its standard input before it exits")
	let line = firstOf(arrivals)

	let code = try child.wait() otherwise panic("the child ends once it has echoed its line")
	child.release()

	print("first={first} line={line} code={code}\n")
	return line.byteLength() as ExitCode
end 'main'
```
```stdout
first=timer line=latehello code=0
```
```exitcode
9
```

<!-- test: streaming-subprocess-nonblocking.a-parked-read-keeps-a-partial-line -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⭐ **A READ WAITING FOR THE REST OF A LINE MUST NOT LOSE WHAT IS ALREADY BUFFERED.** The child writes `abc`
with NO newline (`set /p=abc<nul`), then announces on stderr that it has, and `echo def` supplies the
terminator only after the child has read a line from its standard input. So once the parent has read the
announcement the partial is certainly in the pipe, and the newline certainly is not until the parent sends
its line, whatever the host is doing. The stdout reader runs in a service, whose read takes `abc` in and parks
for the rest; the owner's 50 ms timer arrives first, the owner sends its line, and the read answers the WHOLE
line, `abcdef`.

The two halves discriminate in opposite directions. An owner the read blocked would never see its timer, and
the runner reports the case TIMED OUT; a read that dropped the partial would come back with `def`, three bytes
and a different exit code, rather than a near miss.
```maxon
typealias ArrivalPromise = Promise with String
typealias Arrivals = Array with ArrivalPromise

function shown(read LineRead) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
		pastTheCap gives "pastTheCap"
	end 'read'
end 'shown'

type LineService
	var reader as ChildOutputReader

	static function create(reader isolated ChildOutputReader) returns Self
		return Self{reader: merge reader}
	end 'create'

	export function next() returns String
		let read = try self.reader.readLine() otherwise (e) 'unread'
			return "threw {e.name}"
		end 'unread'

		return shown(read)
	end 'next'
end 'LineService'

function nextLine(service LineService.handle) returns String
	return try await service.next() otherwise panic("the line service stopped while its owner held its handle")
end 'nextLine'

function timerAfter(delay Milliseconds) returns String
	sleep(delay)

	return "timer"
end 'timerAfter'

function firstOf(arrivals Arrivals) returns String
	let ready = arrivals.awaitAny()
	let arrived = try arrivals.remove(ready) otherwise panic("awaitAny answered a slot of this array")

	return await arrived
end 'firstOf'

function partialThenRestArguments() returns StringArray
	let argv = StringArray.create(0)
	argv.push("/c")
	argv.push("set")
	argv.push("/p=abc<nul&")
	argv.push("echo")
	argv.push("announced>&2&")
	argv.push("set")
	argv.push("/p")
	argv.push("released=&")
	argv.push("echo")
	argv.push("def")
	return argv
end 'partialThenRestArguments'

function main() returns ExitCode
	let child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: partialThenRestArguments()) otherwise return 3
	let announcements = try child.takeStderr() otherwise return 4
	_ = try announcements.readLine() otherwise return 5

	let reader = try child.takeStdout() otherwise return 6
	let service = spawn LineService.create(reader)

	let arrivals = Arrivals.create(0)
	arrivals.push(async nextLine(service))
	arrivals.push(async timerAfter(50))

	let first = firstOf(arrivals)
	try child.writeStdinLine("go") otherwise panic("the child reads its standard input before it exits")
	let line = firstOf(arrivals)

	let code = try child.wait() otherwise panic("the child ends once it has echoed its line")
	child.release()

	print("first={first} line={line} code={code}\n")
	return line.byteLength() as ExitCode
end 'main'
```
```stdout
first=timer line=abcdef code=0
```
```exitcode
6
```

<!-- test: streaming-subprocess-nonblocking.the-two-readers-do-not-cross -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
`takeStderr()`'s reader reads the child's STDERR, `takeStdout()`'s its stdout, and the two streams stay apart.
The child writes `errline` to stderr and `outline` to stdout — same length, different text, so a case that
read one pipe twice would still be reporting seven bytes and only the TEXT betrays it. Both are asserted by
name, and a second stdout read answers `ended`, so no stderr text ever landed in the stdout reader. The exit
code packs both lengths (`7*10 + 7`), so a stream that answered nothing shows up as 7 or 70.
```maxon
function shown(read LineRead) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
		pastTheCap gives "pastTheCap"
	end 'read'
end 'shown'

function bothStreamsArguments() returns StringArray
	let argv = StringArray.create(0)
	argv.push("/c")
	argv.push("echo")
	argv.push("errline>&2&")
	argv.push("echo")
	argv.push("outline")
	return argv
end 'bothStreamsArguments'

function main() returns ExitCode
	let child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: bothStreamsArguments()) otherwise return 3
	let out = try child.takeStdout() otherwise return 4
	let err = try child.takeStderr() otherwise return 5

	let outLine = shown(try out.readLine() otherwise return 6)
	let errLine = shown(try err.readLine() otherwise return 7)
	let extra = shown(try out.readLine() otherwise return 8)
	let code = try child.wait() otherwise return 9
	child.release()

	print("out={outLine} err={errLine} extra={extra} code={code}\n")
	return (outLine.byteLength() * 10 + errLine.byteLength()) as ExitCode
end 'main'
```
```stdout
out=outline err=errline extra=ended code=0
```
```exitcode
77
```

<!-- test: streaming-subprocess-nonblocking.after-release-poll-exit-answers-running-and-a-read-answers-closed -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
A handle `release()` has given back is answered, not faulted on: `pollExit` answers `running`, because there
is no child left to report an exit for, and a read through either reader taken before the release throws
`StreamError.closed`, because there is no pipe left to read.

The child runs to completion first, so `running` here is the released answer and not a live one — a
`pollExit` that consulted the freed handle would report `exited 0` (or fault), and either is a different
line rather than a near miss.
```maxon
function describeExit(poll ExitPoll) returns String
	return match poll 'seen'
		running gives "running"
		reapedElsewhere gives "reapedElsewhere"
		exited(code) gives "exited {code}"
	end 'seen'
end 'describeExit'

function shown(read LineRead) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
		pastTheCap gives "pastTheCap"
	end 'read'
end 'shown'

function afterRelease(reader ChildOutputReader) returns String
	let read = try reader.readLine() otherwise (e) 'refused'
		return e.name
	end 'refused'

	return shown(read)
end 'afterRelease'

function echoChildArguments() returns StringArray
	let argv = StringArray.create(0)
	argv.push("/c")
	argv.push("echo")
	argv.push("hello")
	return argv
end 'echoChildArguments'

function main() returns ExitCode
	let child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: echoChildArguments()) otherwise return 3
	let out = try child.takeStdout() otherwise return 4
	let err = try child.takeStderr() otherwise return 5
	let line = shown(try out.readLine() otherwise return 6)
	let code = try child.wait() otherwise return 7
	child.release()

	let afterPoll = describeExit(child.pollExit())
	let afterOut = afterRelease(out)
	let afterErr = afterRelease(err)

	print("line={line} code={code} afterPoll={afterPoll} afterOut={afterOut} afterErr={afterErr}\n")
	return line.byteLength() as ExitCode
end 'main'
```
```stdout
line=hello code=0 afterPoll=running afterOut=closed afterErr=closed
```
```exitcode
5
```

<!-- test: streaming-subprocess-nonblocking.a-blank-line-is-a-line-and-not-the-end -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⭐ **THE CASE `LineRead` EXISTS FOR.** The child writes `first`, then a BLANK line (`echo.`), then `last`,
and the four reads must answer `line[first]`, `line[]`, `line[last]` and `ended` in that order. The middle one
is a line whose text is the empty string; the end of the stream is a different answer, so the blank line is
reported rather than taken for the end.

⛔ **THE WRONG ANSWER HERE IS A DROPPED LINE, NOT A CRASH.** A `maxon monitor` keyed off emptiness
forwards a traced child's `line-one` and `line-three` and silently swallows the blank line between them —
an instrument quietly editing the output it exists to relay.

The exit code sums the three lines' descriptions (`11 + 6 + 10`), so a blank line that came back `ended`
(5 bytes) is **26** rather than 27 as well as a different stdout line.
```maxon
function shown(read LineRead) returns String
	return match read 'read'
		line(text) gives "line[{text}]"
		ended gives "ended"
		pastTheCap gives "pastTheCap"
	end 'read'
end 'shown'

function blankBetweenArguments() returns StringArray
	let argv = StringArray.create(0)
	argv.push("/c")
	argv.push("echo")
	argv.push("first&")
	argv.push("echo.&")
	argv.push("echo")
	argv.push("last")
	return argv
end 'blankBetweenArguments'

function main() returns ExitCode
	let child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: blankBetweenArguments()) otherwise return 3
	let reader = try child.takeStdout() otherwise return 4

	let first = shown(try reader.readLine() otherwise return 5)
	let blank = shown(try reader.readLine() otherwise return 6)
	let last = shown(try reader.readLine() otherwise return 7)
	let atEnd = shown(try reader.readLine() otherwise return 8)
	let code = try child.wait() otherwise return 9
	child.release()

	print("{first} {blank} {last} {atEnd} code={code}\n")
	return (first.byteLength() + blank.byteLength() + last.byteLength()) as ExitCode
end 'main'
```
```stdout
line[first] line[] line[last] ended code=0
```
```exitcode
27
```

<!-- test: streaming-subprocess-nonblocking.the-ends-of-both-streams-are-latched-by-the-readers -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⭐ **`streamHasEnded` ANSWERS WITHOUT A READ ONCE A READ HAS MET THE END.** `streamHasEnded(StdioStream.stdout)`
and `streamHasEnded(StdioStream.stderr)` answer `false` while a read of that stream may still produce bytes and
`true` once a read has found its end, so a parent can ask about a stream another coroutine reads.

The child writes `first`, `second` and a last line with no terminator, `tail`, and nothing on stderr. Before
any read both streams are open. Each reader reads its stream until `ended`: the unterminated `tail` is handed
over as a line when the end is found, so the text is the three lines in order, and then both streams report
their end.
```maxon
function linesArguments() returns StringArray
	let argv = StringArray.create(0)
	argv.push("/c")
	argv.push("echo")
	argv.push("first&")
	argv.push("echo")
	argv.push("second&")
	argv.push("set")
	argv.push("/p=tail<nul")
	return argv
end 'linesArguments'

function bothEnded(child StreamingSubprocess) returns bool
	return child.streamHasEnded(StdioStream.stdout) and child.streamHasEnded(StdioStream.stderr)
end 'bothEnded'

function drained(reader ChildOutputReader, marker String) returns String
	var seen = ""

	while true 'reading'
		let read = try reader.readLine() otherwise (e) 'failed'
			return "{seen}threw {e.name}|"
		end 'failed'

		match read 'read'
			line(text) then seen.append("{marker}{text}|")
			ended then return seen
			pastTheCap then return "{seen}pastTheCap|"
		end 'read'
	end 'reading'

	panic("drained: the loop returns when the stream ends")
end 'drained'

function main() returns ExitCode
	let child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: linesArguments()) otherwise return 3
	let openAtStart = not child.streamHasEnded(StdioStream.stdout) and not child.streamHasEnded(StdioStream.stderr)
	let out = try child.takeStdout() otherwise return 4
	let err = try child.takeStderr() otherwise return 5

	let fromOut = drained(out, marker: "")
	let fromErr = drained(err, marker: "err:")
	let endedAfter = bothEnded(child)

	_ = try child.wait() otherwise return 6
	child.release()

	print("openAtStart={openAtStart} seen={fromOut}{fromErr} endedAfter={endedAfter}\n")
	return 0
end 'main'
```
```stdout
openAtStart=true seen=first|second|tail| endedAfter=true
```
```exitcode
0
```

<!-- test: streaming-subprocess-nonblocking.posix-the-ends-of-both-streams-are-latched-by-the-readers -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
`the-ends-of-both-streams-are-latched-by-the-readers` on a POSIX lane, with the child a shell that writes
`first`, `second` and an unterminated `tail`.
```maxon
function linesArguments() returns StringArray
	let argv = StringArray.create(0)
	argv.push("-c")
	argv.push("echo first; echo second; printf tail")
	return argv
end 'linesArguments'

function bothEnded(child StreamingSubprocess) returns bool
	return child.streamHasEnded(StdioStream.stdout) and child.streamHasEnded(StdioStream.stderr)
end 'bothEnded'

function drained(reader ChildOutputReader, marker String) returns String
	var seen = ""

	while true 'reading'
		let read = try reader.readLine() otherwise (e) 'failed'
			return "{seen}threw {e.name}|"
		end 'failed'

		match read 'read'
			line(text) then seen.append("{marker}{text}|")
			ended then return seen
			pastTheCap then return "{seen}pastTheCap|"
		end 'read'
	end 'reading'

	panic("drained: the loop returns when the stream ends")
end 'drained'

function main() returns ExitCode
	let child = try StreamingSubprocess.spawn(Executable.name("sh"), arguments: linesArguments()) otherwise return 3
	let openAtStart = not child.streamHasEnded(StdioStream.stdout) and not child.streamHasEnded(StdioStream.stderr)
	let out = try child.takeStdout() otherwise return 4
	let err = try child.takeStderr() otherwise return 5

	let fromOut = drained(out, marker: "")
	let fromErr = drained(err, marker: "err:")
	let endedAfter = bothEnded(child)

	_ = try child.wait() otherwise return 6
	child.release()

	print("openAtStart={openAtStart} seen={fromOut}{fromErr} endedAfter={endedAfter}\n")
	return 0
end 'main'
```
```stdout
openAtStart=true seen=first|second|tail| endedAfter=true
```
```exitcode
0
```
