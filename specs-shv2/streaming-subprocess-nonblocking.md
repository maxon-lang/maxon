---
feature: streaming-subprocess-nonblocking
status: experimental
keywords: [subprocess, streaming, StreamingSubprocess, pollExit, ExitPoll, tryReadStdoutLine, tryReadStderrLine, non-blocking, poll, drain, stdio, pipe]
category: system
---

# The streaming child a parent can WATCH — `pollExit` and the two `tryRead*Line` doors

## Documentation

`StreamingSubprocess`'s blocking surface answers every question by committing to it: `readStdoutLine`
parks until a line or end of stream arrives, and `wait()` parks until the child is gone. A parent that
must stay RESPONSIVE — draining output while the child still runs, and deciding for itself when to give
up — cannot ask any of them. These three members are the answers that change nothing and never wait.

- `pollExit()` returns `ExitPoll.running` or `ExitPoll.exited(code)`. It does not block and it does not
  terminate.
- `tryReadStdoutLine()` / `tryReadStderrLine()` return `LinePoll.line(text)` carrying a complete buffered
  line with its terminator stripped, or `LinePoll.none` when no complete line is buffered right now.
  `none` says nothing about end of stream; a caller that needs that distinction asks `pollExit`, because a
  child that has exited writes no more.

⛔ **THE LINE ANSWER IS A UNION BECAUSE A BLANK LINE IS A LINE.** `echo.` writes a line whose text is `""`,
which is also the only string a `""`-returning door could use for "nothing buffered" — so a caller keyed off
emptiness silently DROPS every blank line the child writes, and nothing downstream can tell that it did.
MEASURED: `maxon monitor`, whose whole job is forwarding a traced child's output verbatim, dropped both a
blank stdout line and a blank stderr line where the reference monitor forwarded them.
`a-blank-line-is-a-line-and-not-an-empty-pipe` is the case that separates the two.

⛔ **`waitWithTimeout` IS NOT A POLL, AND REACHING FOR IT AS ONE IS THE TRAP `pollExit` EXISTS TO CLOSE.**
`waitWithTimeout(0)` means *wait for ever* — 0 is the whole family's convention for no deadline — so it
is `wait()`. And a caller who supplies a small deadline instead gets the other half of that contract: the
runtime **TERMINATES** the child when the deadline fires, with exit code **1**. So both readings of
"just poll it with a timeout" are wrong, one by hanging and one by killing the subject.
`poll-exit-leaves-the-child-alive` is the case that separates them: it drives the child to its OWN exit
code, and **a `1` there means the poll killed it.**

⚠ **A HANDLE `release()` HAS GIVEN BACK ANSWERS `running`**, and `tryRead*Line` answers `none`, because
after `release()` there is no child left to report an exit for and no pipe left to read. Neither door
throws — unlike the blocking readers, which refuse a released handle through `requireLive`.

**Targets — x64-windows only**, the restriction the whole streaming-subprocess family carries: the
reader parks its green thread on the Windows completion driver, and every child here is `cmd`.

**How these children are built.** Each argument is a separate `argv` token and the runtime joins them
with single spaces, quoting only a token that is empty or holds a space, a tab or a `"`. So `&` and
`>nul` travel as their own unquoted tokens and reach `cmd` as the separators and redirections they are.
⚠ Where a token is glued to the next command (`/p=abc<nul&`) that is LOAD-BEARING: `cmd`'s `echo` and
`set /p` preserve a space that precedes a `&`, so a separate `&` token would put one inside the child's
output and the asserted text would no longer be the text the case names.

## Tests

<!-- test: streaming-subprocess-nonblocking.poll-exit-running-then-exited -->
<!-- targets: x64-windows -->
`pollExit()` answers `running` for a live child and `exited(code)` once it is gone, carrying the child's
REAL code. The child pings three times (~2 s) and then exits **42**, so the poll that follows the spawn
lands while it is unambiguously alive; the poll after `wait()` sees a signalled process handle and reports
the code. 42 is distinctive and non-zero, so an `exited` arm that answered a zero-by-default — an
unwritten scratch word, a masked-off upper half — is a different number rather than a plausible one.
```maxon
typealias StringArray = Array with String

function describePoll(poll ExitPoll) returns String
	return match poll 'seen'
		running gives "running"
		exited(code) gives "exited {code}"
	end 'seen'
end 'describePoll'

function slowChildArguments() returns StringArray
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("ping")
	argv.push("-n")
	argv.push("3")
	argv.push("127.0.0.1")
	argv.push(">nul")
	argv.push("&")
	argv.push("exit")
	argv.push("/b")
	argv.push("42")
	return argv
end 'slowChildArguments'

function main() returns ExitCode
	var child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: slowChildArguments()) otherwise return 3
	let before = describePoll(child.pollExit())
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
<!-- targets: x64-windows -->
⭐ **THE CASE THE DOOR EXISTS FOR: polling must not be a way of ending the thing being polled.** The same
~2 s child is polled FIVE times over 250 ms — every answer must be `running`, which is deterministic
because the child cannot finish inside a quarter of its own delay — and is then allowed to finish
normally. It must report the exit code IT chose, **42**.

⛔ **A `1` HERE MEANS THE POLL KILLED THE CHILD.** `1` is the code the runtime stamps on a child it
terminates when a `waitWithTimeout` deadline fires, so a `pollExit` implemented on top of that path — the
obvious wrong implementation, and the reason this member is not a wrapper — turns this case red with
`code=1` rather than merely failing to answer. MEASURED against THIS child: `waitWithTimeout(50)` throws
`timed out after 50ms` and a following `wait()` answers **1**, so the two outcomes really are
distinguishable by the number and not only in principle. `exitedSeen` staying `-1` is the second half of
the same claim: not one of the five polls claimed an exit that had not happened.
```maxon
typealias StringArray = Array with String
typealias PollTally = int(0 to 1000)
typealias SeenCode = int(-1 to i64.max)

function slowChildArguments() returns StringArray
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("ping")
	argv.push("-n")
	argv.push("3")
	argv.push("127.0.0.1")
	argv.push(">nul")
	argv.push("&")
	argv.push("exit")
	argv.push("/b")
	argv.push("42")
	return argv
end 'slowChildArguments'

function main() returns ExitCode
	var child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: slowChildArguments()) otherwise return 3

	var runningSeen = 0 as PollTally
	var exitedSeen = -1 as SeenCode
	var polls = 0 as PollTally
	while polls < 5 'poll'
		match child.pollExit() 'answer'
			running then runningSeen = runningSeen + 1
			exited(code) then exitedSeen = code as SeenCode
		end 'answer'
		sleep(50)
		polls = polls + 1
	end 'poll'

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

<!-- test: streaming-subprocess-nonblocking.try-read-stdout-answers-none-rather-than-blocking -->
<!-- targets: x64-windows -->
`tryReadStdoutLine()` answers `none` while the child has written nothing, and the line still arrives once
the child writes it. The parent spins, counting `none` answers, until one comes back a line.

⚠ **THE `none` ANSWER IS GUARANTEED BY CONSTRUCTION, NOT BY LUCK** — which is what keeps this case from
being flaky. The child's first byte cannot precede its `ping -n 3` (~2 s), and the first spin runs
microseconds after the spawn, so `sawNone` is decided by the child's shape rather than by a race. The
spin is bounded at 4000 turns of 5 ms so a reader that had begun BLOCKING would fail the case rather
than hang the suite: the line would never be read, and the exit code would be 0 instead of 9.
```maxon
typealias StringArray = Array with String
typealias SpinTally = int(0 to 100000)

function lateChildArguments() returns StringArray
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("ping")
	argv.push("-n")
	argv.push("3")
	argv.push("127.0.0.1")
	argv.push(">nul")
	argv.push("&")
	argv.push("echo")
	argv.push("latehello")
	return argv
end 'lateChildArguments'

function main() returns ExitCode
	var child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: lateChildArguments()) otherwise return 3

	var nones = 0 as SpinTally
	var seen = ""
	var spins = 0 as SpinTally
	while spins < 4000 'spin'
		match child.tryReadStdoutLine() 'polled'
			line(text) then seen = text
			none then nones = nones + 1
		end 'polled'

		if not seen.isEmpty() 'arrived'
			break
		end 'arrived'

		sleep(5)
		spins = spins + 1
	end 'spin'

	let code = try child.wait() otherwise return 4
	child.release()

	print("sawNone={nones > 0} line={seen} code={code}\n")
	return seen.byteLength() as ExitCode
end 'main'
```
```stdout
sawNone=true line=latehello code=0
```
```exitcode
9
```

<!-- test: streaming-subprocess-nonblocking.try-read-stdout-keeps-a-partial-line -->
<!-- targets: x64-windows -->
⭐ **A `none` ANSWER MUST NOT CONSUME WHAT IS ALREADY BUFFERED.** The child writes `abc` with NO newline
(`set /p=abc<nul`), waits ~2 s, then `echo def` supplies the terminator. After 400 ms the partial is
certainly in the parent's buffer and its newline certainly is not, so `tryReadStdoutLine()` must answer
`none` — and must leave `abc` where it is. The later read then returns the WHOLE line, `abcdef`.

The two halves discriminate in opposite directions. A reader that blocked on the incomplete line would
hang the first read past the 400 ms mark rather than answering `none`; a reader that answered `none` by
DRAINING the buffer would come back with `def`, three bytes and a different exit code, rather than a near
miss.
```maxon
typealias StringArray = Array with String
typealias SpinTally = int(0 to 100000)

function describePoll(poll LinePoll) returns String
	return match poll 'seen'
		line(text) gives "line[{text}]"
		none gives "none"
	end 'seen'
end 'describePoll'

function partialThenRestArguments() returns StringArray
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("set")
	argv.push("/p=abc<nul&")
	argv.push("ping")
	argv.push("-n")
	argv.push("3")
	argv.push("127.0.0.1")
	argv.push(">nul")
	argv.push("&")
	argv.push("echo")
	argv.push("def")
	return argv
end 'partialThenRestArguments'

function main() returns ExitCode
	var child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: partialThenRestArguments()) otherwise return 3

	sleep(400)
	let whileIncomplete = describePoll(child.tryReadStdoutLine())

	var seen = ""
	var spins = 0 as SpinTally
	while spins < 4000 'spin'
		match child.tryReadStdoutLine() 'polled'
			line(text) then seen = text
			none then break 'polled'
		end 'polled'

		if not seen.isEmpty() 'arrived'
			break
		end 'arrived'

		sleep(5)
		spins = spins + 1
	end 'spin'

	let code = try child.wait() otherwise return 4
	child.release()

	print("partial={whileIncomplete} line={seen} code={code}\n")
	return seen.byteLength() as ExitCode
end 'main'
```
```stdout
partial=none line=abcdef code=0
```
```exitcode
6
```

<!-- test: streaming-subprocess-nonblocking.try-read-stderr-and-the-two-streams-do-not-cross -->
<!-- targets: x64-windows -->
`tryReadStderrLine()` reads the child's STDERR, and the two streams stay apart. The child writes
`errline` to stderr and `outline` to stdout — same length, different text, so a case that read one pipe
twice would still be reporting seven bytes and only the TEXT betrays it. Both are asserted by name, and a
third stdout read afterwards answers 0 bytes, so no stderr text ever landed in the stdout buffer.

The spin is the same bounded shape `try-read-stdout-answers-none-rather-than-blocking` uses, shared by
both streams through one `fromStderr` flag rather than written twice: an unread line ends the spin at
4000 turns and fails the case rather than hanging the suite. The exit code packs both lengths
(`7*10 + 7`), so a stream that answered nothing shows up as 7 or 70.
```maxon
typealias StringArray = Array with String
typealias SpinTally = int(0 to 100000)

function describePoll(poll LinePoll) returns String
	return match poll 'seen'
		line(text) gives "line[{text}]"
		none gives "none"
	end 'seen'
end 'describePoll'

function bothStreamsArguments() returns StringArray
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("errline>&2&")
	argv.push("echo")
	argv.push("outline")
	return argv
end 'bothStreamsArguments'

function spinForLine(child StreamingSubprocess, fromStderr bool) returns String
	var spins = 0 as SpinTally
	while spins < 4000 'spin'
		let poll = child.tryReadStderrLine() if fromStderr else child.tryReadStdoutLine()
		match poll 'polled'
			line(text) then return text
			none then break 'polled'
		end 'polled'

		sleep(5)
		spins = spins + 1
	end 'spin'
	return ""
end 'spinForLine'

function main() returns ExitCode
	var child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: bothStreamsArguments()) otherwise return 3

	let outLine = spinForLine(child, fromStderr: false)
	let errLine = spinForLine(child, fromStderr: true)
	let extra = describePoll(child.tryReadStdoutLine())
	let code = try child.wait() otherwise return 4
	child.release()

	print("out={outLine} err={errLine} extra={extra} code={code}\n")
	return (outLine.byteLength() * 10 + errLine.byteLength()) as ExitCode
end 'main'
```
```stdout
out=outline err=errline extra=none code=0
```
```exitcode
77
```

<!-- test: streaming-subprocess-nonblocking.after-release-both-doors-answer-instead-of-throwing -->
<!-- targets: x64-windows -->
Neither non-blocking door refuses a handle `release()` has given back, and that asymmetry with the
blocking readers is deliberate: `readStdoutLine`, `wait` and `writeStdinLine` all throw
`SubprocessError.ioFailed` there through `requireLive`, because a released handle is a pointer to a freed
struct on some lanes and asking the runtime about it is already the bug. `pollExit` and the two
`tryRead*Line` doors answer from the released FLAG without reaching the runtime at all: `running`,
because there is no child left to report an exit for, and `none`, because there is no pipe left to read.

The child runs to completion first, so `running` here is the released answer and not a live one — a
`pollExit` that consulted the freed handle would report `exited 0` (or fault), and either is a different
line rather than a near miss.
```maxon
typealias StringArray = Array with String

function describeExit(poll ExitPoll) returns String
	return match poll 'seen'
		running gives "running"
		exited(code) gives "exited {code}"
	end 'seen'
end 'describeExit'

function describeLine(poll LinePoll) returns String
	return match poll 'seen'
		line(text) gives "line[{text}]"
		none gives "none"
	end 'seen'
end 'describeLine'

function echoChildArguments() returns StringArray
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("hello")
	return argv
end 'echoChildArguments'

function main() returns ExitCode
	var child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: echoChildArguments()) otherwise return 3
	let line = try child.readStdoutLine() otherwise return 4
	let code = try child.wait() otherwise return 5
	child.release()

	let afterPoll = describeExit(child.pollExit())
	let afterOut = describeLine(child.tryReadStdoutLine())
	let afterErr = describeLine(child.tryReadStderrLine())

	print("line={line} code={code} afterPoll={afterPoll} afterOut={afterOut} afterErr={afterErr}\n")
	return line.byteLength() as ExitCode
end 'main'
```
```stdout
line=hello code=0 afterPoll=running afterOut=none afterErr=none
```
```exitcode
5
```

<!-- test: streaming-subprocess-nonblocking.a-blank-line-is-a-line-and-not-an-empty-pipe -->
<!-- targets: x64-windows -->
⭐ **THE CASE THE UNION EXISTS FOR.** The child writes `first`, then a BLANK line (`echo.`), then `last`,
and the three polls must answer `line[first]`, `line[]`, `line[last]` in that order. The middle one is a
line whose text is the empty string, which is exactly the value a `""`-returning door would have to use
for *"nothing is buffered"* — so under such a door the blank line is not merely mis-labelled, it is
UNREPORTABLE, and every caller drops it.

⛔ **THE WRONG ANSWER HERE IS A DROPPED LINE, NOT A CRASH.** MEASURED before the union: `maxon monitor`
forwarded a traced child's `line-one` and `line-three` and silently swallowed the blank line between them,
on both stdout and stderr, where the reference monitor forwarded all three — an instrument quietly editing
the output it exists to relay.

The exit code sums the three descriptions' lengths (`11 + 6 + 10`), so a blank line that came back `none`
(4 bytes) is **25** rather than 27 as well as a different stdout line. The spin is bounded at 400 turns of
5 ms because this child has no delay in it — every line is there within milliseconds — so a poll that
never answers fails the case in about two seconds instead of hanging the suite.
```maxon
typealias StringArray = Array with String
typealias SpinTally = int(0 to 100000)

function blankBetweenArguments() returns StringArray
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("first&")
	argv.push("echo.&")
	argv.push("echo")
	argv.push("last")
	return argv
end 'blankBetweenArguments'

function nextLine(child StreamingSubprocess) returns String
	var spins = 0 as SpinTally
	while spins < 400 'spin'
		match child.tryReadStdoutLine() 'polled'
			line(text) then return "line[{text}]"
			none then break 'polled'
		end 'polled'

		sleep(5)
		spins = spins + 1
	end 'spin'

	return "none"
end 'nextLine'

function main() returns ExitCode
	var child = try StreamingSubprocess.spawn(Executable.name("cmd"), arguments: blankBetweenArguments()) otherwise return 3

	let first = nextLine(child)
	let blank = nextLine(child)
	let last = nextLine(child)
	let code = try child.wait() otherwise return 4
	child.release()

	print("{first} {blank} {last} code={code}\n")
	return (first.byteLength() + blank.byteLength() + last.byteLength()) as ExitCode
end 'main'
```
```stdout
line[first] line[] line[last] code=0
```
```exitcode
27
```
