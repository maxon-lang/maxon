---
feature: logger
status: experimental
keywords: [Log, Logger, LogLevel, LogAttr, LogSink, LogHandler, TextLogHandler, JsonLogHandler, slog, structured logging]
category: stdlib
---

# `Log` — structured logging, modelled on Go's `log/slog`

## Documentation

`Log.info("test")` needs no setup: the stdlib's `default LogSink` writes
`time=<RFC 3339 ms UTC> level=INFO msg=test` to stderr at level INFO. A `LogSink` pairs a `LogHandler`
service with its live `LogLevelVar`; `Log.setDefault(sink)` replaces both for every thread, and a `Logger`
keeps the sink it was built on and obeys that sink's level changes. A record below the level builds
nothing; `Log.error` waits until its record is written, and every record queued before it.

## Tests

<!-- test: log-info-needs-no-setup -->
<!-- unsupported-targets: wasm32-wasi -->
The program runs itself as a child whose only statement is `Log.info("test")`, and checks the SHAPE of the
child's stderr: the timestamp varies, so each digit is read as a digit and every other byte is pinned.
```maxon
typealias StringArray = Array with String

function child() returns ExitCode
	Log.info("test")
	return 0
end 'child'

function shapeOf(text String) returns String
	var shape = ""

	for c in text 'eachCharacter'
		let spelled = "{c}"

		if "0123456789".contains(spelled) 'digit'
			shape.append("D")
		end 'digit' else 'other'
			shape.append(spelled)
		end 'other'
	end 'eachCharacter'

	return shape
end 'shapeOf'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	var argv = StringArray.create()
	argv.push("child")

	var config = Configuration.create(Executable.path(me))
	config.arguments = argv
	let run = try Subprocess.runConfiguration(config) otherwise return 3

	if not run.succeeded() 'childFailed'
		return 4
	end 'childFailed'

	if shapeOf(run.stderr) != "time=DDDD-DD-DDTDD:DD:DD.DDDZ level=INFO msg=test\n" 'shape'
		print("unexpected stderr: {run.stderr}")
		return 1
	end 'shape'

	print("ok\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok
```

<!-- test: a-disabled-level-writes-nothing -->
```maxon
function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(options), levels: levels))
	Log.debug("hidden")
	Log.trace("hidden")
	Log.info("shown")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
level=INFO msg=shown
```

<!-- test: a-custom-level -->
An unnamed level prints relative to the named level at or below it, as slog does; a named level prints its
name. Filtering is by rank: at WARN, rank 2 is dropped and rank 6 is written.
```maxon
function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.trace))
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(options), levels: levels))
	let audit = LogLevel.named("AUDIT", rank: 2)

	Log.log(LogLevel.create(2), message: "two")
	Log.log(LogLevel.create(-6), message: "minusSix")
	Log.log(audit, message: "audited")
	Log.log(LogLevel.create(12), message: "twelve")

	Log.setLevel(LogLevel.warn)
	Log.log(LogLevel.create(2), message: "twoAgain")
	Log.log(audit, message: "auditedAgain")
	Log.log(LogLevel.create(6), message: "six")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
level=INFO+2 msg=two
level=DEBUG-2 msg=minusSix
level=AUDIT msg=audited
level=ERROR+4 msg=twelve
level=WARN+2 msg=six
```

<!-- test: typed-attrs-in-text -->
A text value is quoted when it holds a space, an `=`, a `"` or a control byte, with `Json.quote`'s escapes; a
float prints its shortest round-trip form; a group's attrs print with dotted keys.
```maxon
function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(options), levels: levels))
	Log.info("request", attrs: [
		LogAttr.string("path", value: "/a b"),
		LogAttr.int("status", value: 200),
		LogAttr.int("delta", value: -3),
		LogAttr.float("ratio", value: 0.5),
		LogAttr.bool("ok", value: true),
		LogAttr.group("req", attrs: [LogAttr.string("method", value: "GET"), LogAttr.int("bytes", value: 12)]),
		LogAttr.string("eq", value: "a=b"),
		LogAttr.string("said", value: "say \"hi\""),
		LogAttr.string("tabbed", value: "a\tb"),
		LogAttr.string("plain", value: "word")
	])
	return 0
end 'main'
```
```exitcode
0
```
```stdout
level=INFO msg=request path="/a b" status=200 delta=-3 ratio=0.5 ok=true req.method=GET req.bytes=12 eq="a=b" said="say \"hi\"" tabbed="a\tb" plain=word
```

<!-- test: typed-attrs-in-json -->
The same record through `JsonLogHandler`: one object per line, a group nested as an object.
```maxon
function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn JsonLogHandler.create(options), levels: levels))
	Log.info("request", attrs: [
		LogAttr.string("path", value: "/a b"),
		LogAttr.int("status", value: 200),
		LogAttr.int("delta", value: -3),
		LogAttr.float("ratio", value: 0.5),
		LogAttr.bool("ok", value: true),
		LogAttr.group("req", attrs: [LogAttr.string("method", value: "GET"), LogAttr.int("bytes", value: 12)]),
		LogAttr.string("eq", value: "a=b"),
		LogAttr.string("said", value: "say \"hi\""),
		LogAttr.string("tabbed", value: "a\tb"),
		LogAttr.string("plain", value: "word")
	])
	return 0
end 'main'
```
```exitcode
0
```
```stdout
{"level":"INFO","msg":"request","path":"/a b","status":200,"delta":-3,"ratio":0.5,"ok":true,"req":{"method":"GET","bytes":12},"eq":"a=b","said":"say \"hi\"","tabbed":"a\tb","plain":"word"}
```

<!-- test: with-attrs-and-with-group -->
`withAttrs` binds attrs before the record's own; after `withGroup("req")` every following attr, bound or
passed, is qualified by `req`.
```maxon
function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(options), levels: levels))
	let base = Log.default().withAttrs([LogAttr.string("service", value: "api")]).withGroup("req")
	base.info("served", attrs: [LogAttr.string("method", value: "GET"), LogAttr.int("status", value: 200)])
	base.withAttrs([LogAttr.int("id", value: 7)]).info("again")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
level=INFO msg=served service=api req.method=GET req.status=200
level=INFO msg=again service=api req.id=7
```

<!-- test: with-attrs-and-with-group-in-json -->
The same loggers through `JsonLogHandler`: the group nests the attrs that follow it.
```maxon
function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn JsonLogHandler.create(options), levels: levels))
	let base = Log.default().withAttrs([LogAttr.string("service", value: "api")]).withGroup("req")
	base.info("served", attrs: [LogAttr.string("method", value: "GET"), LogAttr.int("status", value: 200)])
	base.withAttrs([LogAttr.int("id", value: 7)]).info("again")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
{"level":"INFO","msg":"served","service":"api","req":{"method":"GET","status":200}}
{"level":"INFO","msg":"again","service":"api","req":{"id":7}}
```

<!-- test: add-source-names-the-callers-line -->
With `addSource`, the record names the file and line of the call that logged it, in text and in JSON.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: true)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(options), levels: levels))
	Log.info("first")

	Log.warn("second")
	Log.default().info("third")
	Log.error("fourth")

	let jsonOptions = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: true)
	Log.setDefault(LogSink.create(spawn JsonLogHandler.create(jsonOptions), levels: LogLevelVar.create(LogLevels.minimum(LogLevel.info))))
	Log.info("fifth")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
level=INFO source=main.maxon:5 msg=first
level=WARN source=main.maxon:7 msg=second
level=INFO source=main.maxon:8 msg=third
level=ERROR source=main.maxon:9 msg=fourth
{"level":"INFO","source":{"file":"main.maxon","line":13},"msg":"fifth"}
```

<!-- test: a-group-level-override -->
A group's own level overrides the minimum for loggers in that group and for no other.
```maxon
function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(options), levels: levels))
	Log.setGroupLevel("a", level: LogLevel.debug)
	let a = Log.default().withGroup("a")
	let b = Log.default().withGroup("b")
	a.debug("fromA")
	b.debug("fromB")
	b.info("infoFromB")
	Log.debug("ungrouped")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
level=DEBUG msg=fromA
level=INFO msg=infoFromB
```

<!-- test: set-level-takes-effect-on-the-next-call -->
The level is live: the next call on any thread obeys a change, whether `main` made it or a handler did.
```maxon
type Tuner
	var tuned as Integer

	static function create() returns Self
		return Self{tuned: 0}
	end 'create'

	export function lower() returns Integer
		Log.setLevel(LogLevel.debug)
		self.tuned = self.tuned + 1
		return self.tuned
	end 'lower'
end 'Tuner'

function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(options), levels: levels))
	Log.debug("before")
	Log.setLevel(LogLevel.debug)
	Log.debug("after")
	Log.setLevel(LogLevel.info)
	Log.debug("restored")

	let tuner = spawn Tuner.create()
	let tuned = try await tuner.lower() otherwise 0
	Log.debug("fromHandler")
	return tuned as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
1
```
```stdout
level=DEBUG msg=after
level=DEBUG msg=fromHandler
```

<!-- test: set-level-for-one-group-leaves-the-others -->
`Log.groupLevel(g)` is the level in effect for group `g`: its own override, else the minimum.
```maxon
function report()
	print("a={Log.groupLevel("a").name()} b={Log.groupLevel("b").name()} min={Log.level().name()}\n")
end 'report'

function main() returns ExitCode
	report()
	Log.setGroupLevel("a", level: LogLevel.debug)
	report()
	Log.setLevel(LogLevel.warn)
	report()
	Log.clearGroupLevel("a")
	report()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=INFO b=INFO min=INFO
a=DEBUG b=INFO min=INFO
a=DEBUG b=WARN min=WARN
a=WARN b=WARN min=WARN
```

<!-- test: concurrent-set-levels-lose-nothing -->
<!-- procs: 4 -->
Eight handlers each set a different group's level at once; every setter re-reads and retries until its
change lands, so no override is lost.
```maxon
type Setter
	let group as String
	let quiet as bool

	static function create(group String, quiet bool) returns Self
		return Self{group: group, quiet: quiet}
	end 'create'

	export function apply() returns Integer
		Log.setGroupLevel(self.group, level: LogLevel.warn if self.quiet else LogLevel.debug)
		return 1
	end 'apply'
end 'Setter'

typealias ApplyReplyArray = Array with Promise with (Integer, ServiceError)

function main() returns ExitCode
	var replies = ApplyReplyArray.create()

	for i in 0 upto 8 'spawnEach'
		let setter = spawn Setter.create("g{i}", quiet: i mod 2 == 1)
		replies.push(setter.apply())
	end 'spawnEach'

	var applied = 0

	for r in replies 'awaitEach'
		applied = applied + (try await r otherwise 0)
	end 'awaitEach'

	for i in 0 upto 8 'reportEach'
		print("g{i}={Log.groupLevel("g{i}").name()}\n")
	end 'reportEach'

	return applied as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
8
```
```stdout
g0=DEBUG
g1=WARN
g2=DEBUG
g3=WARN
g4=DEBUG
g5=WARN
g6=DEBUG
g7=WARN
```

<!-- test: levels-belong-to-the-handler -->
Each sink carries its own `LogLevelVar`, so `setLevel` through a logger on one sink changes nothing a logger
on the other writes. `la.error` waits for its line, which orders the two handlers' output.
```maxon
function main() returns ExitCode
	let optionsA = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(optionsA), levels: LogLevelVar.create(LogLevels.minimum(LogLevel.info))))
	let la = Log.default().withAttrs([LogAttr.string("via", value: "a")])

	let optionsB = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(optionsB), levels: LogLevelVar.create(LogLevels.minimum(LogLevel.info))))
	let lb = Log.default().withAttrs([LogAttr.string("via", value: "b")])

	lb.setLevel(LogLevel.debug)
	la.debug("hidden")
	la.error("fromA")
	lb.debug("shown")

	if la.enabled(LogLevel.debug) or not lb.enabled(LogLevel.debug) 'levels'
		return 1
	end 'levels'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
level=ERROR msg=fromA via=a
level=DEBUG msg=shown via=b
```

<!-- test: a-logger-built-before-set-level-obeys-it -->
```maxon
function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(options), levels: levels))
	let l = Log.default()
	l.debug("before")
	Log.setLevel(LogLevel.debug)
	l.debug("after")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
level=DEBUG msg=after
```

<!-- test: a-new-default-brings-its-own-levels -->
The new sink writes to stderr at DEBUG; a logger built on the old one keeps writing to stdout at INFO.
```maxon
function main() returns ExitCode
	let toStdout = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(toStdout), levels: LogLevelVar.create(LogLevels.minimum(LogLevel.info))))
	let old = Log.default().withAttrs([LogAttr.string("via", value: "old")])

	let toStderr = LogHandlerOptions.create(LogStream.standardError, addTime: false, addSource: false)
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(toStderr), levels: LogLevelVar.create(LogLevels.minimum(LogLevel.debug))))

	Log.debug("fresh")
	old.debug("stale")
	old.info("kept")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
level=INFO msg=kept via=old
```
```stderr
level=DEBUG msg=fresh
```

<!-- test: handlers-log-without-a-handle -->
A handler logs through `Log` with no handle passed to it. Worker `a` is awaited before `b` starts, so `a`'s
record is queued first.
```maxon
type Worker
	let name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'

	export function work() returns Integer
		Log.info("working", attrs: [LogAttr.string("worker", value: self.name)])
		return 1
	end 'work'
end 'Worker'

function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(options), levels: levels))

	let a = spawn Worker.create("a")
	let doneA = try await a.work() otherwise 0
	let b = spawn Worker.create("b")
	let doneB = try await b.work() otherwise 0
	return (doneA + doneB) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
2
```
```stdout
level=INFO msg=working worker=a
level=INFO msg=working worker=b
```

<!-- test: registering-a-sink-reroutes-every-thread -->
A user handler implements `LogHandler`; once its sink is the default, records from `main` and from a worker's
handler both reach it.
```maxon
type Collect implements LogHandler
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function handle(record LogRecord) returns bool
		self.count = self.count + 1
		print("collected {self.count}: {record.level.name()} {record.message}\n")
		return true
	end 'handle'
end 'Collect'

type Worker
	var runs as Integer

	static function create() returns Self
		return Self{runs: 0}
	end 'create'

	export function work() returns Integer
		self.runs = self.runs + 1
		Log.info("fromWorker")
		return self.runs
	end 'work'
end 'Worker'

function main() returns ExitCode
	Log.setDefault(LogSink.create(spawn Collect.create(), levels: LogLevelVar.create(LogLevels.minimum(LogLevel.info))))
	Log.info("fromMain")
	let worker = spawn Worker.create()
	let runs = try await worker.work() otherwise 0
	return runs as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
1
```
```stdout
collected 1: INFO fromMain
collected 2: INFO fromWorker
```

<!-- test: an-error-logged-before-a-panic-is-written -->
<!-- unsupported-targets: wasm32-wasi -->
The child logs three records, then `Log.error`, then panics. `Log.error` returns only once its record is
written, and the handler's mailbox is FIFO, so all four lines precede the panic on the child's stderr.
```maxon
typealias StringArray = Array with String

function child()
	Log.info("one")
	Log.info("two")
	Log.info("three")
	Log.error("boom")
	panic("after")
end 'child'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		child()
		return 0
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	var argv = StringArray.create()
	argv.push("child")

	var config = Configuration.create(Executable.path(me))
	config.arguments = argv
	let run = try Subprocess.runConfiguration(config) otherwise return 3

	let expected = [" level=INFO msg=one", " level=INFO msg=two", " level=INFO msg=three", " level=ERROR msg=boom"]
	let lines = run.stderr.split("\n")

	for (iter, suffix) in expected.withIterator() 'eachExpected'
		let line = try lines.get(iter.index()) otherwise 'short'
			print("stderr ended early: {run.stderr}")
			return 1
		end 'short'

		if not line.startsWith("time=") or not line.endsWith(suffix) 'mismatch'
			print("line {iter.index()} was: {line}\n")
			return 1
		end 'mismatch'
	end 'eachExpected'

	print("ok\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok
```

<!-- test: an-extension-adds-a-custom-level-helper -->
A program adds its own level helper to `Log` and `Logger` by extension, forwarding its caller's location, so
the record names the line that called the helper; the level filters it by rank.
```maxon
// --- file: main.maxon
let auditLevel = LogLevel.named("AUDIT", rank: 2)

extension Log
	public static function audit(message String, attrs LogAttrArray = LogAttrArray.create(), file String = __file__, line SourceLineNumber = __line__)
		Log.log(auditLevel, message: message, attrs: attrs, file: file, line: line)
	end 'audit'
end 'Log'

extension Logger
	public function audit(message String, attrs LogAttrArray = LogAttrArray.create(), file String = __file__, line SourceLineNumber = __line__)
		self.log(auditLevel, message: message, attrs: attrs, file: file, line: line)
	end 'audit'
end 'Logger'

function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: true)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(options), levels: levels))
	Log.audit("static")
	let logger = Log.default().withAttrs([LogAttr.string("via", value: "logger")])
	logger.audit("instance", attrs: [LogAttr.int("n", value: 1)])
	Log.setLevel(LogLevel.warn)
	Log.audit("dropped")
	logger.audit("droppedToo")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
level=AUDIT source=main.maxon:19 msg=static
level=AUDIT source=main.maxon:21 msg=instance via=logger n=1
```
