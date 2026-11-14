---
feature: stream-reader
status: experimental
keywords: [stream, StreamReader, ByteSource, readLine, readUntil, readBytes, readToEnd, LineOrEnd, StreamError, subprocess, tcp, tls, file, stdin]
category: stdlib
---

# `StreamReader` — one buffered reader over every byte source

## Documentation

`stdlib/Stream.maxon` declares `ByteSource`, whose one requirement `readSome(most)` answers up to `most` bytes
and an empty array only at the end of the stream, and `StreamReader uses Source where Source is ByteSource`,
which holds one source in its public field `source` and the bytes read from it but not yet handed out.

| Method | Answers |
|---|---|
| `StreamReader.over(source)` | a reader with nothing buffered |
| `readLine()` | `LineOrEnd.line(text)` without its LF or a CR before it, however long the line, a last line with no LF as `line` and then `ended` at the end of the stream |
| `readLineWithin(cap LineCap)` | `LineRead`: `line(text)` and `ended` as `readLine` answers them, and `pastTheCap` when no LF arrives within `cap` bytes |
| `readUntil(delimiter String, cap LineCap)` | `LineRead`, the text up to the delimiter, nothing stripped |
| `readBytes(count BytePos)` | exactly `count` bytes, fewer only at the end of the stream |
| `readToEnd()` | every byte left |

`pastTheCap` consumes nothing: the bytes stay buffered and the next read sees them. A source's own failures
reach the caller as `StreamError`: `timedOut`, `closed`, `failed(reason)` or `secure(error)` for a TLS failure.

The sources are a child process's output stream (`ChildOutput`, from `StreamingSubprocess.takeStdout()` and
`takeStderr()`), a file (`FileSource`, from `File.openRead`), `TcpClient`, `TlsStream` and standard input
(`StdinSource`, from `Console.stdin()`). Each has a public alias for its reader: `ChildOutputReader`,
`FileStreamReader`, `TcpStreamReader`, `TlsStreamReader` and `StdinReader`.

A read through a reader parks until bytes or the end arrive. An owner that must stay responsive hands each
reader to a service of its own, one per stream, and `awaitAny`s over `async` calls awaiting the services'
reads beside a timer coroutine; a deadline ends the child's process tree, which ends its streams, rather than
cancelling a parked read. A reader taken from a child that is then released throws `StreamError.closed`.

This file owns the reader and the sources' reads through it. `console-stdin.md` owns standard input,
`subprocess.md` a reader moved into a coroutine, and `subprocess-builtins.md` the intrinsic a child's output
stream reads through.

## Tests

<!-- test: a-child-stdout-is-read-line-by-line-then-ended -->
<!-- unsupported-targets: wasm32-wasi -->
A taken stdout reader answers each line without its terminator: a blank line is empty text, a CRLF terminator loses its CR too, and the read after the last line answers `ended` rather than throwing.
```maxon
function shown(read LineOrEnd) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
	end 'read'
end 'shown'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		print("one\n\nthree\r\n")
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	let argv = StringArray.create(0)
	argv.push("emit")
	let child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3
	child.closeStdin()
	let reader = try child.takeStdout() otherwise return 4

	let one = shown(try reader.readLine() otherwise return 5)
	let blank = shown(try reader.readLine() otherwise return 6)
	let three = shown(try reader.readLine() otherwise return 7)
	let ended = shown(try reader.readLine() otherwise return 8)

	_ = try child.wait() otherwise panic("the child ends once its input is closed")
	child.release()
	print("{one}|{blank.byteLength()}|{three}|{three.byteLength()}|{ended}\n")
	return 0
end 'main'
```
```stdout
one|0|three|5|ended
```
```exitcode
0
```

<!-- test: a-child-stderr-is-read-by-exact-count -->
<!-- unsupported-targets: wasm32-wasi -->
A taken stderr reader answers exactly the count asked while the child has that many bytes, the remainder when it has fewer, and an empty array after the last byte.
```maxon
function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		printError("abcdefghij")
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	let argv = StringArray.create(0)
	argv.push("emit")
	let child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3
	child.closeStdin()
	let reader = try child.takeStderr() otherwise return 4

	let first = try reader.readBytes(4) otherwise return 5
	let second = try reader.readBytes(4) otherwise return 6
	let third = try reader.readBytes(2) otherwise return 7
	let atEnd = try reader.readBytes(4) otherwise return 8

	_ = try child.wait() otherwise panic("the child ends once its input is closed")
	child.release()
	print("{String.from(first)}|{String.from(second)}|{String.from(third)}|{atEnd.count()}\n")
	return 0
end 'main'
```
```stdout
abcd|efgh|ij|0
```
```exitcode
0
```

<!-- test: a-line-longer-than-the-cap-is-refused-and-consumes-nothing -->
<!-- unsupported-targets: wasm32-wasi -->
A line read whose cap passes before an LF answers `pastTheCap` and leaves every byte buffered: the exact-count read after it gets the twenty bytes of the long line and its LF, and the line after it reads whole.
```maxon
function shown(read LineRead) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
		pastTheCap gives "pastTheCap"
	end 'read'
end 'shown'

function shownLine(read LineOrEnd) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
	end 'read'
end 'shownLine'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		print("xxxxxxxxxxxxxxxxxxxx\nshort\n")
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	let argv = StringArray.create(0)
	argv.push("emit")
	let child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3
	child.closeStdin()
	let reader = try child.takeStdout() otherwise return 4

	let refused = shown(try reader.readLineWithin(8) otherwise return 5)
	let taken = try reader.readBytes(21) otherwise return 6
	let short = shownLine(try reader.readLine() otherwise return 7)
	let ended = shownLine(try reader.readLine() otherwise return 8)

	_ = try child.wait() otherwise panic("the child ends once its input is closed")
	child.release()
	print("{refused}|{taken.count()}|{short}|{ended}\n")
	return 0
end 'main'
```
```stdout
pastTheCap|21|short|ended
```
```exitcode
0
```

<!-- test: a-line-read-without-a-cap-returns-a-line-longer-than-any-cap-whole -->
`readLine()` takes no cap: a line of 1,100,000 bytes, longer than a mebibyte, comes back whole, the line after it reads whole too, and the read after the last line answers `ended`.
```maxon
function shown(read LineOrEnd) returns String
	return match read 'read'
		line(text) gives "{text.byteLength()}"
		ended gives "ended"
	end 'read'
end 'shown'

function main() returns ExitCode
	let path = FilePath from "stream-reader-a-line-read-without-a-cap.txt"
	let chunk = "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789"
	var content = ""

	for _ in 0 upto 11000 'eachChunk'
		content.append(chunk)
	end 'eachChunk'

	content.append("\nshort\n")

	try File.writeText(path, content: content) otherwise return 1
	let reader = try File.openRead(path) otherwise return 2

	let long = shown(try reader.readLine() otherwise return 3)
	let short = shown(try reader.readLine() otherwise return 4)
	let ended = shown(try reader.readLine() otherwise return 5)

	try File.delete(path) otherwise return 6
	print("{long}|{short}|{ended}\n")
	return 0
end 'main'
```
```stdout
1100000|5|ended
```
```exitcode
0
```

<!-- test: a-line-read-within-a-cap-answers-pastTheCap-past-it-and-the-line-within-it -->
`readLineWithin(cap)` answers `pastTheCap` for a line of twelve bytes under a cap of eight and consumes nothing, answers that same line under a cap of twelve, reads the short line after it under the cap of eight, and answers `ended` after the last line.
```maxon
function shown(read LineRead) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
		pastTheCap gives "pastTheCap"
	end 'read'
end 'shown'

function main() returns ExitCode
	let path = FilePath from "stream-reader-a-line-read-within-a-cap.txt"

	try File.writeText(path, content: "twelve chars\nshort\n") otherwise return 1
	let reader = try File.openRead(path) otherwise return 2

	let refused = shown(try reader.readLineWithin(8) otherwise return 3)
	let exact = shown(try reader.readLineWithin(12) otherwise return 4)
	let short = shown(try reader.readLineWithin(8) otherwise return 5)
	let ended = shown(try reader.readLineWithin(8) otherwise return 6)

	try File.delete(path) otherwise return 7
	print("{refused}|{exact}|{short}|{ended}\n")
	return 0
end 'main'
```
```stdout
pastTheCap|twelve chars|short|ended
```
```exitcode
0
```

<!-- test: a-final-line-with-no-newline-is-a-line-and-then-the-stream-has-ended -->
A stream whose last line has no LF answers that text as a `line` and every read after it `ended`, from `readLine()` and from `readLineWithin(cap)` alike, as Go's `bufio.Reader.ReadString` answers the partial text beside its end-of-file.
```maxon
function shownWithin(read LineRead) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
		pastTheCap gives "pastTheCap"
	end 'read'
end 'shownWithin'

function shown(read LineOrEnd) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
	end 'read'
end 'shown'

function main() returns ExitCode
	let path = FilePath from "stream-reader-a-final-line-with-no-newline.txt"

	try File.writeText(path, content: "one\ntail") otherwise return 1

	let unbounded = try File.openRead(path) otherwise return 2
	let first = shown(try unbounded.readLine() otherwise return 3)
	let second = shown(try unbounded.readLine() otherwise return 4)
	let third = shown(try unbounded.readLine() otherwise return 5)
	let fourth = shown(try unbounded.readLine() otherwise return 6)

	let bounded = try File.openRead(path) otherwise return 7
	let boundedFirst = shownWithin(try bounded.readLineWithin(16) otherwise return 8)
	let boundedSecond = shownWithin(try bounded.readLineWithin(16) otherwise return 9)
	let boundedThird = shownWithin(try bounded.readLineWithin(16) otherwise return 10)
	let boundedFourth = shownWithin(try bounded.readLineWithin(16) otherwise return 11)

	try File.delete(path) otherwise return 12
	print("{first}|{second}|{third}|{fourth}\n")
	print("{boundedFirst}|{boundedSecond}|{boundedThird}|{boundedFourth}\n")
	return 0
end 'main'
```
```stdout
one|tail|ended|ended
one|tail|ended|ended
```
```exitcode
0
```

<!-- test: a-child-stdout-is-read-by-line-and-by-count-from-one-buffer -->
<!-- unsupported-targets: wasm32-wasi -->
Line reads and exact-count reads of one child stream share the reader's one buffer. The line read pulls more
than its line off the pipe, and the four-byte read after it answers the four bytes right after the line's LF
rather than waiting on a pipe that has nothing left to deliver; the next line read answers the rest of the
line.
```maxon
function shown(read LineOrEnd) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
	end 'read'
end 'shown'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		print("first\nabcdefghij\n")
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	let argv = StringArray.create(0)
	argv.push("emit")
	let child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3
	child.closeStdin()
	let reader = try child.takeStdout() otherwise return 4

	let first = shown(try reader.readLine() otherwise return 5)
	let counted = try reader.readBytes(4) otherwise return 6
	let rest = shown(try reader.readLine() otherwise return 7)
	let ended = shown(try reader.readLine() otherwise return 8)

	_ = try child.wait() otherwise panic("the child ends once its input is closed")
	child.release()
	print("{first}|{String.from(counted)}|{rest}|{ended}\n")
	return 0
end 'main'
```
```stdout
first|abcd|efghij|ended
```
```exitcode
0
```

<!-- test: a-child-output-read-after-release-answers-closed -->
<!-- unsupported-targets: wasm32-wasi -->
A reader taken from a child that is then released has no stream left to read: its next read throws
`StreamError.closed`, although the child wrote a line nothing read, and so does the read after that, so a
caller cannot mistake the released stream for one that ended.
```maxon
function afterRelease(reader ChildOutputReader) returns String
	let read = try reader.readLine() otherwise (e) 'refused'
		return e.name
	end 'refused'

	return match read 'read'
		line(text) gives "line {text}"
		ended gives "ended"
	end 'read'
end 'afterRelease'

function bytesAfterRelease(reader ChildOutputReader) returns String
	let bytes = try reader.readBytes(4) otherwise (e) 'refused'
		return e.name
	end 'refused'

	return "answered {bytes.count()} bytes"
end 'bytesAfterRelease'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		print("unread\n")
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	let argv = StringArray.create(0)
	argv.push("emit")
	let child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3
	child.closeStdin()
	let reader = try child.takeStdout() otherwise return 4

	_ = try child.wait() otherwise panic("the child ends once its input is closed")
	child.release()
	let line = afterRelease(reader)
	let bytes = bytesAfterRelease(reader)
	print("line={line} bytes={bytes}\n")
	return 0
end 'main'
```
```stdout
line=closed bytes=closed
```
```exitcode
0
```

<!-- test: releasing-a-child-whose-grandchild-holds-stdout-does-not-strand-the-reader -->
<!-- process: own -->
<!-- unsupported-targets: wasm32-wasi -->
A child that exits while a grandchild it started still holds its output pipes leaves the pump's reads parked: the
streams do not end when the child does. The program pumps the child's output until the child has exited, with
both streams still open and a read parked on each, then releases the child and stops the pump. That must return
at once and the program must end, rather than wait for the grandchild to let go of the pipes. The case ends the
grandchild itself before it returns, because a descendant a case leaves running fails it.
```maxon
#if os(Windows)
function holderExecutable() returns Executable
	return Executable.name("cmd")
end 'holderExecutable'

function holderArguments() returns StringArray
	let argv = StringArray.create(0)
	argv.push("/c")
	argv.push("start")
	argv.push("/b")
	argv.push("ping")
	argv.push("-n")
	argv.push("30")
	argv.push("127.0.0.1")
	argv.push("&")
	argv.push("exit")
	argv.push("0")
	return argv
end 'holderArguments'
#else
function holderExecutable() returns Executable
	return Executable.path(try FilePath.from("/bin/sh") otherwise panic("/bin/sh is a valid path"))
end 'holderExecutable'

function holderArguments() returns StringArray
	let argv = StringArray.create(0)
	argv.push("-c")
	argv.push("sleep 30 & exit 0")
	return argv
end 'holderArguments'
#endif

function hasExited(child StreamingSubprocess) returns bool
	return match child.pollExit() 'poll'
		running gives false
		reapedElsewhere or
		exited gives true
	end 'poll'
end 'hasExited'

function main() returns ExitCode
	let child = try StreamingSubprocess.spawn(holderExecutable(), arguments: holderArguments()) otherwise return 2
	child.closeStdin()
	let pump = try ChildPump.of(child) otherwise return 3

	var streamsClosed = 0 as Count

	while not hasExited(child) 'untilExit'
		let event = pump.next(50)

		match event 'event'
			said or
			silent then continue
			closed then streamsClosed = streamsClosed + 1
			broke(_, reason) then panic("a stream of the child broke: {reason}")
			exhausted then panic("the grandchild did not hold the child's output open")
		end 'event'
	end 'untilExit'

	child.release()
	pump.stop()
	_ = Process.endDescendants()

	print("child exited, streams closed before the release: {streamsClosed}\n")
	return 0
end 'main'
```
```stdout
child exited, streams closed before the release: 0
```
```exitcode
0
```

<!-- test: a-stdin-feed-whose-childs-stdin-the-owner-closed-settles-without-writing -->
<!-- unsupported-targets: wasm32-wasi -->
A `StdinFeed` waits for an anchor line on the child's stdout and then queues its own line for the child's stdin. The
owner may close that stdin first, which it is free to do at any time. The feed then has nothing it can deliver: the
anchor settles it as spent, nothing is written, and the program goes on.
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.path(try FilePath.from("C:/Windows/System32/cmd.exe") otherwise return 2)
	let argv = StringArray.create(0)
	argv.push("/c")
	argv.push("findstr")
	argv.push("x*")
	#else
	let exe = Executable.path(try FilePath.from("/bin/cat") otherwise return 2)
	let argv = StringArray.create(0)
	#endif

	let child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3
	let pump = try ChildPump.of(child) otherwise return 4
	let feed = StdinFeed.afterLine("anchor", line: "late")

	child.closeStdin()
	feed.offer(PumpEvent.said(StdioStream.stdout, text: "anchor"), child: child, pump: pump)

	_ = try child.wait() otherwise return 5
	pump.stop()
	child.release()

	print("feed settled\n")
	return 0
end 'main'
```
```stdout
feed settled
```
```exitcode
0
```

<!-- test: a-feed-larger-than-the-pipe-reaches-a-child-that-reads-late -->
<!-- unsupported-targets: wasm32-wasi -->
A feed larger than the pipe's buffer cannot be written at once, and the child here reads nothing for a second after its
anchor. The owner parks until the pipe is writable and then pushes on, so the whole feed arrives and the child counts
every byte of it. The child is this program, so the count is the same on every OS.
```maxon
function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		print("ready\n")
		sleep(1000)
		let received = try Console.stdin().readToEnd() otherwise return 9
		print("{received.count()}\n")
		return 0
	end 'childMode'

	let argv = StringArray.create(0)
	argv.push("count")

	let builder = StringBuilder.create()
	var filled = 0 as Count

	while filled < 200000 'fill'
		builder.append("x")
		filled = filled + 1
	end 'fill'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	let outcome = try runChildFedAfterLine(exe, argv: argv, workingDirectory: WorkingDirectory.inherit, environment: Environment.inherit, deadlineMs: 20000, endsWhen: RunEndsWhen.outputEnds, tailSilenceMs: 200, tailCapMs: 1000, anchor: "ready", feed: builder.build()) otherwise return 3

	match outcome 'outcome'
		ran(report) then print(report.stdout)
		outputBroken(_, reason, _) then print("output broke: {reason}\n")
	end 'outcome'

	return 0
end 'main'
```
```stdout
ready
200001
```
```exitcode
0
```

<!-- test: a-tail-reads-the-output-that-is-left-when-the-stdin-watch-finishes-first -->
<!-- unsupported-targets: wasm32-wasi -->
A feed the child never reads leaves the owner watching for the pipe to become writable. The watch finishes
long before the child, which exits without reading, writes its last line. The tail that follows must keep
reading until both streams end, so the line arrives and the tail reports `drained`: a finished watch is not a
quiet stream. The streams end when the child closes its pipes, which is before the child has ended, and the
tail releases the child without waiting for it, so the case polls the child's pid until the child has ended.
```maxon
let ChildEndsWithinMs = 30000
let ChildPollMs = 50

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		sleep(500)
		print("late\n")
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	let argv = StringArray.create(0)
	argv.push("emit")

	let builder = StringBuilder.create()
	var filled = 0 as Count

	while filled < 200000 'fill'
		builder.append("x")
		filled = filled + 1
	end 'fill'

	let child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3
	let pid = try child.processId() otherwise return 5
	let pump = try ChildPump.of(child) otherwise return 4
	let feed = StdinFeed.afterLine("anchor", line: builder.build())

	feed.offer(PumpEvent.said(StdioStream.stdout, text: "anchor"), child: child, pump: pump)

	let capture = StreamCapture.create()
	let tail = PumpTail.begin(2000, capMs: 10000).drainAndRelease(pump, child: child, into: capture)

	var waitedMs = 0 as DurationMs

	while Process.isAlive(pid) and waitedMs < ChildEndsWithinMs 'untilEnded'
		sleep(ChildPollMs)
		waitedMs = waitedMs + ChildPollMs
	end 'untilEnded'

	print("{tail.name} {capture.stdout}")
	return 0
end 'main'
```
```stdout
drained late
```
```exitcode
0
```

<!-- test: a-reader-in-a-service-answers-while-the-owner-waits-on-a-timer -->
<!-- unsupported-targets: wasm32-wasi -->
Each of a child's output streams is read by a service of its own, so a read that parks holds only its
service. The owner keeps an `async` call awaiting each service's next line beside a 200 ms timer in one array
and takes whichever arrives first. The child writes nothing on stderr and one stdout line after a second, then
sleeps, so the timer arrives first and the stdout line next, whatever the host is doing. The owner then asks
the stdout service for another line and ends the child's process tree, and both services answer `ended`.

⚠ **AN OWNER THAT A READ BLOCKED NEVER SEES ITS TIMER**, and a deadline that ended the child by cancelling a
read instead would leave the services' reads parked: the runner reports either as TIMED OUT. The two
remaining reads are awaited in array order, stderr's first, so the line does not depend on which end arrives
first.
```maxon
typealias ArrivalPromise = Promise with String
typealias Arrivals = Array with ArrivalPromise

function shown(read LineOrEnd) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
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

function nextLine(name String, service LineService.handle) returns String
	let text = try await service.next() otherwise panic("the {name} service stopped while its owner held its handle")

	return "{name}:{text}"
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

function nextInOrder(arrivals Arrivals) returns String
	let arrived = try arrivals.remove(0) otherwise panic("a read is left in the array")

	return await arrived
end 'nextInOrder'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		sleep(1000)
		print("late\n")
		sleep(60000)
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	let argv = StringArray.create(0)
	argv.push("emit")
	let child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3
	child.closeStdin()
	let outReader = try child.takeStdout() otherwise return 4
	let errReader = try child.takeStderr() otherwise return 5
	let out = spawn LineService.create(outReader)
	let err = spawn LineService.create(errReader)

	let arrivals = Arrivals.create(0)
	arrivals.push(async nextLine("out", service: out.clone()))
	arrivals.push(async nextLine("err", service: err))
	arrivals.push(async timerAfter(200))

	let first = firstOf(arrivals)
	let second = firstOf(arrivals)
	arrivals.push(async nextLine("out", service: out))

	let ending = child.endTree()
	let third = nextInOrder(arrivals)
	let fourth = nextInOrder(arrivals)
	child.release()

	print("{first} {second} {ending.displayReason()} {third} {fourth}\n")
	return 0
end 'main'
```
```stdout
timer out:late the process tree ended err:ended out:ended
```
```exitcode
0
```

<!-- test: a-blank-line-passes-through-the-child-pump -->
<!-- unsupported-targets: wasm32-wasi -->
The compiler forwards a child's output through `ChildPump`, one reader service per stream. A blank line the
child prints arrives as a `said` event with empty text, in order between its neighbours, and each stream
ends with `closed` before the pump answers `exhausted`.
```maxon
function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		print("one\n\nthree\n")
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	let argv = StringArray.create(0)
	argv.push("emit")
	let child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3
	child.closeStdin()
	let pump = try ChildPump.of(child) otherwise return 4

	var heard = ""
	var pumping = true

	while pumping 'pumping'
		let event = pump.next(1000)

		match event 'event'
			said(_, text) then heard = "{heard}[{text}]"
			closed or
			silent then continue
			broke(_, reason) then panic("a stream of the child broke: {reason}")
			exhausted then pumping = false
		end 'event'
	end 'pumping'

	pump.stop()
	_ = try child.wait() otherwise panic("the child ends once its input is closed")
	child.release()
	print("{heard}\n")
	return 0
end 'main'
```
```stdout
[one][][three]
```
```exitcode
0
```

<!-- test: a-tcp-connection-is-read-by-line-and-by-declared-length -->
<!-- procs: 1 -->
A reader over a `TcpClient` reads a header by line, the body it declares by count and the rest by line again, with no byte lost between them, and answers `ended` once the peer closes. The client sends its request through `reader.source`, so the source still sends while the reader buffers its reads, and the peer reads that request through a reader of its own.
```maxon
function shown(read LineOrEnd) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
	end 'read'
end 'shown'

function serve(listener TcpListener) returns String
	let conn = try listener.accept() otherwise return "accept-failed"
	let reader = StreamReader.over(conn)

	let request = try reader.readLine() otherwise (e) 'unheard'
		return "read threw {e.name}"
	end 'unheard'

	_ = try reader.source.send("Length: 5\r\nhelloworld\n") otherwise return "send-failed"
	reader.source.close()

	return shown(request)
end 'serve'

function heard(reader TcpStreamReader) returns String throws StreamError
	let header = shown(try reader.readLine())
	let body = String.from(try reader.readBytes(5))
	let rest = shown(try reader.readLine())
	let last = shown(try reader.readLine())

	return "{header}|{body}|{rest}|{last}"
end 'heard'

function exchangeOver(listener TcpListener) returns ExitCode
	let boundPort = listener.port()
	let peer = async serve(listener)

	let client = try TcpClient.connect("127.0.0.1", port: boundPort) otherwise 'refused'
		peer.cancel()
		return 2
	end 'refused'

	let reader = StreamReader.over(client)

	_ = try reader.source.send("GET /one\n") otherwise 'unsent'
		peer.cancel()
		return 3
	end 'unsent'

	let answer = try heard(reader) otherwise (e) 'unread'
		peer.cancel()
		print("read threw {e.name}\n")
		return 4
	end 'unread'

	let served = await peer
	print("client {answer}\n")
	print("server {served}\n")

	return 0
end 'exchangeOver'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	return exchangeOver(listener)
end 'main'
```
```stdout
client Length: 5|hello|world|ended
server GET /one
```
```exitcode
0
```

<!-- test: a-tls-stream-is-read-by-line-until-close-notify -->
A reader over a `TlsStream` answers `ended` once the peer's close_notify arrives, after every line sent before it. A peer that drops its stream without closing ends the TCP connection with no close_notify, and the read that meets it throws `StreamError.secure(truncated)` rather than answering `ended`.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	let bytes = ByteArray.create(0)
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function failure(error StreamError) returns String
	return match error 'error'
		secure(reason) gives "secure({describe(reason)})"
		failed(reason) gives "failed({reason})"
		timedOut or
		closed gives error.name
	end 'error'
end 'failure'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	let options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	let options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function sendsTwoLines(listener TlsListener, closes bool) returns String
	let stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	_ = try stream.send("first line\nsecond line\n") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	if closes 'closing'
		stream.close()
		return "closed"
	end 'closing'

	return "dropped"
end 'sendsTwoLines'

function readsLines(options TlsClientOptions, port NetworkPort) returns String
	let stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reader = StreamReader.over(stream)
	var heard = ""

	while true 'reading'
		let read = try reader.readLine() otherwise (error) 'failed'
			return "heard [{heard}] then {failure(error)}"
		end 'failed'

		match read 'read'
			line(text) then heard.append("{text};")
			ended then return "heard [{heard}] then ended"
		end 'read'
	end 'reading'

	panic("readsLines: the loop returns when the stream ends")
end 'readsLines'

function main() returns ExitCode
	let listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
	let client = trustedClient(rootHex)
	let boundPort = listener.port()

	let first = async sendsTwoLines(listener, closes: true)
	let closing = readsLines(client, port: boundPort)
	print("{await first}: client {closing}\n")

	let second = async sendsTwoLines(listener, closes: false)
	let dropping = readsLines(client, port: boundPort)
	print("{await second}: client {dropping}\n")

	listener.close()
	return 0
end 'main'
```
```stdout
closed: client heard [first line;second line;] then ended
dropped: client heard [first line;second line;] then secure(truncated)
```
```exitcode
0
```

<!-- test: a-file-is-read-line-by-line-across-chunks -->
A reader over a file reads every line of one larger than a single read, so lines that straddle two reads come back whole and the count and the last line are exact.
```maxon
function linesOf(path FilePath) returns String
	let reader = try File.openRead(path) otherwise (e) 'unopened'
		return "open threw {e.name}"
	end 'unopened'

	var lines = 0 as Count
	var last = ""

	while true 'reading'
		let read = try reader.readLine() otherwise (e) 'failed'
			return "read threw {e.name} after {lines}"
		end 'failed'

		match read 'read'
			line(text) then last = text
			ended then break 'reading'
		end 'read'

		lines = lines + 1
	end 'reading'

	return "lines={lines} last={last}"
end 'linesOf'

function main() returns ExitCode
	let path = FilePath from "stream-reader-a-file-is-read-line-by-line.txt"
	var content = ""

	for i in 0 upto 1000 'eachLine'
		content.append("line {i}\n")
	end 'eachLine'

	try File.writeText(path, content: content) otherwise return 1
	let outcome = linesOf(path)
	try File.delete(path) otherwise return 2
	print("{outcome}\n")
	return 0
end 'main'
```
```stdout
lines=1000 last=line 999
```
```exitcode
0
```

<!-- test: a-reader-named-only-as-an-element-type-reads-through-its-source -->
The user's program names `StreamReader with ZBytes` only as an array's element type: the readers come from a library factory and the source's `readSome` is reached only through `readLine` on each element. Each reader answers its one line, then `ended`.
```maxon
// --- stdlib-overlay: Stream.maxon
public type ZBytes implements ByteSource
	var left as Count

	static function create() returns ZBytes
		return Self{left: 1}
	end 'create'

	public function readSome(most ReadCapacity) returns isolated ByteArray throws StreamError
		let out = ByteArray.create(0) as isolated ByteArray

		if self.left == 0 'drained'
			return out
		end 'drained'

		self.left = 0
		out.push(104)
		out.push(105)
		out.push(10)
		return out
	end 'readSome'
end 'ZBytes'

public typealias ZBytesReader = StreamReader with ZBytes

public function zReader() returns ZBytesReader
	return StreamReader.over(ZBytes.create())
end 'zReader'
// --- file: main.maxon
typealias ZReaders = Array with (StreamReader with ZBytes)

function shown(read LineOrEnd) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
	end 'read'
end 'shown'

function main() returns ExitCode
	let readers = ZReaders.create(0)
	readers.push(zReader())
	readers.push(zReader())

	for reader in readers 'each'
		let first = shown(try reader.readLine() otherwise return 2)
		let second = shown(try reader.readLine() otherwise return 3)
		print("{first}|{second}\n")
	end 'each'

	return 0
end 'main'
```
```stdout
hi|ended
hi|ended
```
```exitcode
0
```

<!-- test: a-stream-capture-keeps-the-first-failure-it-was-told-of -->
A `StreamCapture` starts `intact`, and the first stream failure it is told of is the health it keeps: a second failure, on the same stream or the other, changes neither the stream nor the reason, and the lines heard before and after still land in their own stream's text.
```maxon
function shown(health OutputHealth) returns String
	return match health 'health'
		intact gives "intact"
		failed(stream, reason) gives "{stream.name}:{reason}"
	end 'health'
end 'shown'

function main() returns ExitCode
	let capture = StreamCapture.create()
	let before = shown(capture.health)

	capture.heard(StdioStream.stdout, text: "kept")
	capture.broken(StdioStream.stderr, reason: "first")
	capture.broken(StdioStream.stdout, reason: "second")
	capture.broken(StdioStream.stderr, reason: "third")
	capture.heard(StdioStream.stderr, text: "after")

	print("{before}|{shown(capture.health)}|{capture.stdout.byteLength()}|{capture.stderr.byteLength()}\n")
	return 0
end 'main'
```
```stdout
intact|stderr:first|5|6
```
```exitcode
0
```
