---
title: Files, Processes & I/O
description: Files, paths, directories, the console, command-line arguments, logging, processes, and shared memory.
sidebar:
  order: 4
---

## File

`File` reads, writes, renames, deletes and inspects files. Every method is static and takes a
[FilePath](#filepath).

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `File.readText(path FilePath)` | `String` | `FileReadError` | The whole file as text. |
| `File.readBinary(path FilePath)` | `ByteArray` | `FileReadError` | The whole file as bytes. |
| `File.writeText(path FilePath, content String, mode FilePermission = .normal)` | — | `FileWriteError` | Create or truncate, then write. |
| `File.writeBinary(path FilePath, content ByteArray, mode FilePermission = .normal)` | — | `FileWriteError` | Create or truncate, then write. |
| `File.exists(path FilePath)` | `bool` | — | True when a file exists at `path`. |
| `File.delete(path FilePath)` | — | `FileDeleteError` | Delete a file. |
| `File.rename(from FilePath, to FilePath)` | — | `FileRenameError` | Move a file, replacing any existing destination in one step, so a reader of `to` never sees a partly written file. |
| `File.info(path FilePath)` | `FileInfo` | `FileInfoError` | Size, timestamps and attributes, from one OS call. |

### Types

| Type | Definition |
|------|------------|
| `FileSize` | `int(0 to u64.max)` — bytes |
| `Timestamp` | `int(0 to u64.max)` — whole seconds since the Unix epoch |
| `Byte` | `int(0 to u8.max)` |
| `ByteArray` | `Array with Byte` |

`FileInfo` has read-only fields and a factory, `FileInfo.create(size, modifiedTime:, createdTime:,
accessedTime:, isDirectory:, isReadOnly:)`:

| Field | Type | Description |
|-------|------|-------------|
| `size` | `FileSize` | Size in bytes |
| `modifiedTime` | `Timestamp` | Last modification |
| `createdTime` | `Timestamp` | Creation |
| `accessedTime` | `Timestamp` | Last access |
| `isDirectory` | `bool` | The path is a directory |
| `isReadOnly` | `bool` | The file is read-only |

`FilePermission` is `normal` (0666) or `executable` (0755 on Unix).

| Error enum | Case | Thrown when |
|------------|------|-------------|
| `FileReadError` | `notFound` | The file cannot be opened or read |
| `FileWriteError` | `failed` | The file cannot be created or written |
| `FileDeleteError` | `notFound` | The file cannot be deleted |
| `FileRenameError` | `failed` | The rename fails |
| `FileInfoError` | `notFound` | The path does not exist |

```maxon
function main() returns ExitCode
	let dir = FilePath from "scratch"
	_ = Directory.create(dir)

	let notes = dir.join("notes.txt")
	try File.writeText(notes, content: "hello") otherwise panic("cannot write {notes}")

	let text = try File.readText(notes) otherwise ""
	let info = try File.info(notes) otherwise panic("just written")
	print("{text} {info.size} {info.isDirectory}\n")

	let moved = dir.join("moved.txt")
	try File.rename(notes, to: moved) otherwise panic("rename failed")
	print("{File.exists(notes)} {File.exists(moved)}\n")

	try File.delete(moved) otherwise panic("delete failed")
	let gone = try File.readText(moved) otherwise "no such file"
	print("{gone}\n")
	return 0
end 'main'
```

Output: `hello 5 false`, `false true`, `no such file`.

## FilePath

`FilePath` is a filesystem path. Construction normalizes separators to the host's (`\` on Windows, `/`
elsewhere) and accepts `file://` URLs, which are converted to paths with their percent escapes decoded
(`file:///tmp/a%20b` is `/tmp/a b`). It implements `Equatable`, `Hashable`,
`Stringable` and `InitableFromStringLiteral`. Equality and hashing follow the host filesystem:
case-insensitive on Windows, byte-exact elsewhere.

### Construction

| Member | Returns | Description |
|--------|---------|-------------|
| `FilePath from "a/b.txt"` | `FilePath` | From a literal; an invalid path panics. |
| `FilePath.from(path String)` | `FilePath` | Throws `FilePathError.invalidCharacter` on Windows for a control character or one of `< > " \| ? *`, `notFileURL` for a URL whose scheme is other than `file`, or `malformedURL` for text holding `://` that `URL.parse` refuses. |
| `FilePath.empty()` | `FilePath` | The empty path. |
| `FilePath.separator()` | `String` | The host separator. |
| `path` | field, `String` | The normalized text. |

### Components

| Member | Returns | Description |
|--------|---------|-------------|
| `filename()` | `String` | The last component (the whole path when there is no separator). |
| `fileExtension()` | `String` | The extension with its dot, or `""`. A leading dot is not an extension (`.gitignore` has none). |
| `stem()` | `String` | The filename without its extension. |
| `parent()` | `FilePath` | The containing directory. A path directly under a root gives that root, spelled as one (`/foo` gives `/`, `C:\foo` gives `C:\`), so walking upwards keeps answering absolute paths. Throws `FilePathError.noParent` for a root and for a relative path with no separator. |

### Building paths

| Member | Returns | Description |
|--------|---------|-------------|
| `join(component String)` | `FilePath` | Append a component with the host separator. |
| `join(component FilePath)` | `FilePath` | Append another path. |
| `changeExtension(newExt String)` | `FilePath` | Replace or add an extension; include the dot (`".exe"`). |
| `anchoredAt(base FilePath)` | `FilePath` | A relative path joined onto `base`, or an absolute path kept. Only what cannot change the file named is folded — `.` components, repeated separators and a trailing one — and every `..` is left for the filesystem, so through a symbolic link it names the file the spelling names. On Windows a drive-relative path (`C:foo`) is joined onto a `base` rooted on the same drive; against any other `base` it stays drive-relative, because it names that drive's current directory. |
| `resolve(base FilePath)` | `FilePath` | `anchoredAt(base)`, then folded as `normalize()` folds it — lexically, so a `..` after a symbolic link cancels the link's name. |
| `relativeTo(base FilePath)` | `FilePath` | The part after `base`. Throws `FilePathError.noParent` when the path is not inside `base`. |
| `normalize()` | `FilePath` | The path folded lexically, without asking the filesystem: `.` components removed, each `..` cancelling the component before it, repeated separators collapsed and a trailing one dropped. A `..` above an absolute path's root is dropped; a leading one in a relative path is kept. A relative path that folds away entirely is `.`. |
| `toString()` | `String` | The path text. |

### Queries

| Member | Returns | Description |
|--------|---------|-------------|
| `isEmpty()` | `bool` | The path is `""`. |
| `isAbsolute()` | `bool` | On Windows a drive path (`C:\`), a UNC path (`\\server`) or a rooted path (`\dir`); a leading `/` elsewhere. |
| `isRelative()` | `bool` | Not absolute. |
| `isInside(dir FilePath)` | `bool` | The path equals `dir` or lies beneath it, compared by whole components (`/foo/bar` is not inside `/foo/ba`). |
| `startsWith(prefix FilePath)` | `bool` | Component-wise prefix test. |
| `equals(other FilePath)`, `hash()` | | Host filesystem semantics. |

```maxon
enum FilePathError implements Error
	invalidCharacter
	notFileURL
	malformedURL
	noParent
end 'FilePathError'
```

```maxon
function main() returns ExitCode
	let base = FilePath from "project"
	let source = base.join("src").join("main.maxon")

	let relative = try source.relativeTo(base) otherwise FilePath.empty()
	let parent = try source.parent() otherwise FilePath.empty()
	let web = try FilePath.from("https://example.com/x") otherwise FilePath.empty()

	print("{source.filename()} {source.stem()} {source.fileExtension()} {parent.filename()}\n")
	print("{source.isInside(base)} {source.isRelative()} {relative.join("x").isEmpty()} {web.isEmpty()}\n")
	print("{source.changeExtension(".txt").filename()}\n")
	return 0
end 'main'
```

Output: `main.maxon main .maxon src`, `true true false true`, `main.txt`.

## Directory

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Directory.list(path FilePath)` | `Array with FilePath` | `DirectoryListError` | The entries of a directory, each joined onto `path`. |
| `Directory.exists(path FilePath)` | `bool` | — | True when `path` is an existing directory. |
| `Directory.isDirectory(path FilePath)` | `bool` | — | The same as `exists`. |
| `Directory.create(path FilePath)` | `bool` | — | Create the directory and any missing parents. True when the directory exists afterwards. |
| `Directory.delete(path FilePath)` | — | `DirectoryDeleteError` | Remove an empty directory. A directory that still holds an entry is refused, not emptied. |
| `Directory.currentPath()` | `FilePath` | — | The working directory. |

```maxon
enum DirectoryListError implements Error
	notFound
	accessDenied
	listFailed
end 'DirectoryListError'
```

```maxon
enum DirectoryDeleteError implements Error
	notFound
	notEmpty
	deleteFailed
end 'DirectoryDeleteError'
```

| Error enum | Case | Thrown when |
|------------|------|-------------|
| `DirectoryDeleteError` | `notFound` | Nothing exists at `path` |
| `DirectoryDeleteError` | `notEmpty` | The directory still holds an entry |
| `DirectoryDeleteError` | `deleteFailed` | The removal fails for any other reason |

```maxon
function main() returns ExitCode
	let dir = FilePath from "listing-demo"
	print("{Directory.create(dir.join("nested"))} {Directory.exists(dir)}\n")
	try File.writeText(dir.join("a.txt"), content: "a") otherwise panic("write")

	for entry in try Directory.list(dir) otherwise panic("list") 'each'
		print("{entry.filename()} {Directory.isDirectory(entry)}\n")
	end 'each'

	print("{Directory.currentPath().isAbsolute()}\n")
	return 0
end 'main'
```

It prints `true true`, then `a.txt false` and `nested true` in the order the OS lists them, then `true`.
`Directory.list` does not report `.` or `..`.

## Console

`Console.stdin()` returns a buffered reader over standard input. Keep one reader for the life of the
program: a new reader does not see input an earlier one already buffered.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Console.stdin()` | `Stdin` | — | The standard-input reader. |
| `readLine()` | `String` | `ConsoleError.endOfFile` | The next line without its `\n`; a `\r` before it is removed too. A last line with no terminator is returned, and the call after it throws. |

```maxon
function main() returns ExitCode
	let stdin = Console.stdin()
	var lines = 0

	while true 'read'
		let line = try stdin.readLine() otherwise break
		lines = lines + 1
		print("[{line}]\n")
	end 'read'

	print("{lines} lines\n")
	return 0
end 'main'
```

Given `one\r\ntwo\nthree` on stdin, it prints `[one]`, `[two]`, `[three]`, `3 lines`.

## CommandLine

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `CommandLine.args()` | `StringArray` | — | Every argument, the program path first. A fresh array each call. |
| `CommandLine.optionValue(arg String)` | `String` | `StringError.notFound` | Everything after the first `=` of `--key=value`, later `=` included. |
| `CommandLine.getOptionValue(name String)` | `String` | `StringError.notFound` | The value of the first `--name=value` argument. |

```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let mode = try CommandLine.getOptionValue("mode") otherwise "default"
	let note = try CommandLine.optionValue("--note=a=b") otherwise ""
	print("{args.count()} {mode} {note}\n")
	return 0
end 'main'
```

Run as `program --mode=fast`, it prints `2 fast a=b`.

## Log

`Log` is a structured logger modelled on Go's `log/slog`: a record is a level, a message and typed attributes,
and a handler service formats and writes it. A program logs with its first call:

```maxon
function main() returns ExitCode
	Log.info("started", attrs: [LogAttr.string("mode", value: "fast"), LogAttr.int("workers", value: 4)])
	return 0
end 'main'
```

writes one line to stderr:

```
time=2026-09-27T12:34:56.789Z level=INFO msg=started mode=fast workers=4
```

The standard library's default sink is a `TextLogHandler` on stderr with the time on (RFC 3339, UTC,
milliseconds) and a minimum level of INFO.

Only an enabled call builds a record: the level is checked at the call, before anything reaches the handler.
Below ERROR, a call sends its record to the handler and returns, and the handler writes it. `Log.error` —
and any call at a rank of ERROR or above — returns once the handler has written its record, and so every
record sent before it too, because the handler takes records in order; a line logged just before a `panic` is
on the stream. When the program exits normally, every record still queued is written first.

Logging runs on services, so it is refused on `wasm32-wasi`
([E3104](/docs/cli/error-codes/#e3104--targetunsupportedconstruct)), at the call.

### Writing records

Each writer takes `message String`, then `attrs LogAttrArray` (default empty), then `file String` and
`line SourceLineNumber`, which default to the caller's location and feed the handler's `addSource` output.

| Member | Returns | Description |
|--------|---------|-------------|
| `Log.trace`, `Log.debug`, `Log.info`, `Log.warn`, `Log.error` | — | Log `message` at that level through the default sink. |
| `Log.log(level LogLevel, message String, …)` | — | Log at any level, a custom one included. |
| `Log.enabled(level LogLevel)` | `bool` | True when the default sink's minimum admits `level` — a guard for attributes that are costly to compute. |
| `Log.default()` | `Logger` | A logger over the current default sink. |
| `Log.setDefault(sink LogSink)` | — | Make `sink` the default for every thread. |

A helper of your own forwards its caller's location by declaring the same two defaulted parameters and
passing them on:

```maxon
extension Log
	public static function audit(message String, attrs LogAttrArray = LogAttrArray.create(), file String = __file__, line SourceLineNumber = __line__)
		Log.log(LogLevel.named("AUDIT", rank: 2), message: message, attrs: attrs, file: file, line: line)
	end 'audit'
end 'Log'
```

### Levels

A `LogLevel` is a rank; a record is written when its rank is at or above the minimum.

| Member | Returns | Description |
|--------|---------|-------------|
| `LogLevel.trace`, `.debug`, `.info`, `.warn`, `.error` | `LogLevel` | Ranks -8, -4, 0, 4 and 8. |
| `LogLevel.create(rank LogRank)` | `LogLevel` | A custom level, named relative to a standard level near it: rank 2 prints `INFO+2`, rank -6 `DEBUG-2`, rank 12 `ERROR+4`. |
| `LogLevel.named(name String, rank LogRank)` | `LogLevel` | A custom level that prints `name`. |
| `rank` | `LogRank` | The rank. |
| `name()` | `String` | The printed name. |

### Attributes

| Member | Returns | Description |
|--------|---------|-------------|
| `LogAttr.string(key String, value String)` | `LogAttr` | A text attribute. |
| `LogAttr.int(key String, value LogInteger)` | `LogAttr` | An integer attribute. |
| `LogAttr.float(key String, value LogReal)` | `LogAttr` | A float attribute, printed in its shortest round-trip form. |
| `LogAttr.bool(key String, value bool)` | `LogAttr` | A boolean attribute. |
| `LogAttr.group(name String, attrs LogAttrArray)` | `LogAttr` | `attrs` qualified by `name`. |
| `fields` | `LogFieldArray` | The attribute flattened to fields. |

`LogAttrArray` is `Array with LogAttr`, written as an array literal at the call. Groups flatten: each attribute
becomes a `LogField`, which is what a handler receives.

| Member | Returns | Description |
|--------|---------|-------------|
| `LogField.create(key String, value LogValue, groups LogGroupPath)` | `LogField` | A field. |
| `groups` | `LogGroupPath` | The enclosing group names, outermost first. |
| `key`, `value` | `String`, `LogValue` | The key within its innermost group, and the value. |
| `dottedKey()` | `String` | The groups and the key joined with `.`: `req.method`. |

`LogValue` is a union with the cases `text(value String)`, `integer(value LogInteger)`, `real(value LogReal)`
and `boolean(value bool)`.

### Loggers

A `Logger` carries attributes and a group to every record it writes. It keeps the sink it was built on and
obeys that sink's level changes, including the ones made after it was built.

| Member | Returns | Description |
|--------|---------|-------------|
| `withAttrs(attrs LogAttrArray)` | `Logger` | A logger that adds `attrs` before each record's own, under the logger's current group. |
| `withGroup(name String)` | `Logger` | A logger whose later attributes, bound or passed, are qualified by `name`. |
| `trace`, `debug`, `info`, `warn`, `error`, `log` | — | As on `Log`, through this logger. |
| `enabled(level LogLevel)` | `bool` | True when this logger would write `level`, its group's override included. |
| `level()`, `setLevel`, `groupLevel`, `setGroupLevel`, `clearGroupLevel` | | As on `Log`, on this logger's sink. |

### Changing levels at run time

The levels belong to the sink. A change takes effect on the next call from any thread, for every `Logger`
on that sink; concurrent changes each land.

| Member | Returns | Description |
|--------|---------|-------------|
| `Log.level()` | `LogLevel` | The minimum. |
| `Log.setLevel(level LogLevel)` | — | Set the minimum. |
| `Log.setGroupLevel(group String, level LogLevel)` | — | Give loggers in `group` their own minimum. |
| `Log.clearGroupLevel(group String)` | — | Return `group` to the minimum. |
| `Log.groupLevel(group String)` | `LogLevel` | The level in effect for `group`: its override, else the minimum. |

A group is named by its path joined with `.`: a logger made by `withGroup("db").withGroup("pool")` obeys the
override for `db.pool`, else the one for `db`, else the minimum. `Log`'s own writers are outside every group.

`LogLevels` is the immutable value those calls replace: `LogLevels.minimum(level)`, then `level()`,
`groupLevel(group)`, `enables(level)`, and the copies `withLevel(level)`, `withGroupLevel(group, level:)` and
`withoutGroupLevel(group)`. `LogLevelVar` is `SharedValue with LogLevels` — the live cell a sink reads (see
[SharedValue](/docs/stdlib/runtime/#sharedvalue)).

### Handlers and sinks

A `LogHandler` is a service interface with one requirement, `handle(record LogRecord) returns bool`. A sink
pairs a handler with its levels:

| Member | Returns | Description |
|--------|---------|-------------|
| `LogSink.create(handler LogHandler.handle, levels LogLevelVar)` | `LogSink` | A sink; `handler` is a spawned service. |
| `handler`, `levels` | `LogHandler.handle`, `LogLevelVar` | Its parts. |
| `enabled`, `level`, `groupLevel`, `setLevel`, `setGroupLevel`, `clearGroupLevel` | | As on `Log`, on this sink. |

The default sink is a registry key: `Log.setDefault(sink)` registers `sink` for every later lookup on every
thread, and a program's own top-level `default LogSink = …` declaration takes precedence over the standard library's
([Program-Wide Defaults](/docs/language/async/#program-wide-defaults--default)).

The two handlers write each record with one write, so lines from different threads stay whole:

| Member | Returns | Description |
|--------|---------|-------------|
| `TextLogHandler.create(options LogHandlerOptions)` | `TextLogHandler` | `key=value` pairs in the order `time`, `level`, `source` (`file:line`), `msg`, then the attributes; the time is RFC 3339 with milliseconds. A text value is quoted with `Json.quote`'s escapes when it is empty or holds a space, a control byte, `"` or `=`. A group's attributes print with dotted keys. |
| `JsonLogHandler.create(options LogHandlerOptions)` | `JsonLogHandler` | One JSON object per line: `time` (RFC 3339 with nanoseconds), `level`, `source` as `{"file":…,"line":…}`, `msg`, then the attributes, a group as a nested object. A non-finite float is a string. |
| `LogHandlerOptions.create(stream LogStream, addTime bool, addSource bool)` | `LogHandlerOptions` | Where to write, and whether to include the time and the caller's location. |
| `LogHandlerOptions.standard()` | `LogHandlerOptions` | Stderr, time on, source off. |
| `stream`, `addTime`, `addSource` | | The options' fields. |

`LogStream` has the cases `standardError` and `standardOutput`.

A handler of your own implements `LogHandler` and receives a `LogRecord`:

| Member | Returns | Description |
|--------|---------|-------------|
| `time` | `UnixNanos` | When the record was made. |
| `level`, `message` | `LogLevel`, `String` | |
| `attrs` | `LogFieldArray` | The logger's bound fields, then the call's own. |
| `file`, `line` | `String`, `SourceLineNumber` | The caller's location. |
| `LogRecord.create(time, level:, message:, attrs:, file:, line:)` | `LogRecord` | A record. |

```maxon
function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.debug))
	Log.setDefault(LogSink.create(spawn JsonLogHandler.create(options), levels: levels))

	let requests = Log.default().withAttrs([LogAttr.string("service", value: "api")]).withGroup("req")
	requests.debug("served", attrs: [LogAttr.string("method", value: "GET"), LogAttr.int("status", value: 200)])
	Log.setGroupLevel("req", level: LogLevel.info)
	requests.debug("filtered")
	Log.error("failed", attrs: [LogAttr.bool("retry", value: false)])
	return 0
end 'main'
```

Output:

```
{"level":"DEBUG","msg":"served","service":"api","req":{"method":"GET","status":200}}
{"level":"ERROR","msg":"failed","retry":false}
```

## TraceCapture

`TraceCapture` records trace keys so a test can check which internal path ran. `trace` records only between
`startCapture` and `stopCapture`; for levelled output see [Log](#log).

A capture holds only the keys the program emitted itself. `TraceCapture` keeps its capture state in
module-level `var`s, so a call to any of these methods from a service message handler is
[E3143](/docs/cli/error-codes/#e3143--semanticsharedglobalaccessfromgreenthread).

| Method | Returns | Description |
|--------|---------|-------------|
| `TraceCapture.trace(key String)` | — | Record `key` when capturing. Use a stable dotted name. |
| `TraceCapture.startCapture()` | — | Start recording, discarding earlier keys. |
| `TraceCapture.stopCapture()` | `TraceKeyArray` | Stop, and return the keys in the order they were emitted. |
| `TraceCapture.fired(capturedKeys TraceKeyArray, key String)` | `bool` | True when `key` was recorded at least once. |

```maxon
function main() returns ExitCode
	TraceCapture.startCapture()
	TraceCapture.trace("cache.miss")
	let keys = TraceCapture.stopCapture()
	print("{TraceCapture.fired(keys, key: "cache.miss")} {TraceCapture.fired(keys, key: "cache.hit")}\n")
	return 0
end 'main'
```

Output: `true false`.

## Process

`Process` describes the running program. To start other programs see [Subprocess](#subprocess).

### ExitCode

`ExitCode` is the return type of `main`: `int(0 to u32.max)` on Windows and `int(0 to 255)` on Linux, macOS
and WASI, matching what each platform can report.

### Members

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Process.executablePath()` | `FilePath` | `ProcessIntrospectionError.pathUnavailable` | Absolute path of the running executable. |
| `Process.environmentVariable(name String)` | `String` | `ProcessIntrospectionError.variableUnset`, `.environmentUnreadable` | The variable's value. Names are case-insensitive on Windows (`Path` answers to `PATH`) and exact elsewhere. An unset variable throws; a variable set to `""` returns `""`. |
| `Process.currentEnvironmentEntries()` | `StringArray` | `ProcessIntrospectionError.environmentUnreadable` | Every `NAME=VALUE` entry, in the order the OS reports them. Throws when the OS fails to hand the environment over. |
| `Process.envEntryName(entry String)` | `String` | — | The text before the first `=` (searching from the second byte, so Windows' `=C:=C:\dir` entries keep their name). |
| `Process.envEntryValue(entry String)` | `String` | — | The text after that `=`, or `""`. |
| `Process.envNameKey(name String)` | `String` | — | The key a variable name is compared by: lower-cased on Windows, `name` as given elsewhere. Two names with equal keys are one variable. |
| `EnvNameValueSeparator` | `Byte` | — | The separator byte, `=` (61). |

```maxon
enum ProcessIntrospectionError implements Error
	pathUnavailable
	variableUnset
	environmentUnreadable
end 'ProcessIntrospectionError'
```

```maxon
function main() returns ExitCode
	let exe = try Process.executablePath() otherwise FilePath.empty()
	let unset = try Process.environmentVariable("SURELY_NOT_SET_ANYWHERE") otherwise "unset"
	print("{exe.isAbsolute()} {unset}\n")
	print("{Process.envEntryName("A=b=c")} {Process.envEntryValue("A=b=c")}\n")
	return 0
end 'main'
```

Output: `true unset` and `A b=c`.

## Subprocess

`Subprocess` starts child processes. `Subprocess.run` waits for the child and collects its output;
`Configuration` gives full control; `StreamingSubprocess` keeps the child's pipes open for line-by-line
conversation. A spawn from a green thread parks that green thread, so others keep running.

Not available on `wasm32-wasi`, which has no process spawning: every call is refused at compile time with
E3074.

```maxon
function main() returns ExitCode
	var args = StringArray.create()
	args.push("--version")

	let result = try Subprocess.run(Executable.name("git"), arguments: args) otherwise (e) 'failed'
		print("could not run git: {e.displayReason()}\n")
		return 1
	end 'failed'

	print("{result.succeeded()} {result.exitCode()} {result.stdout.startsWith("git version")}\n")
	return 0
end 'main'
```

With `git` on `PATH`, it prints `true 0 true`.

### Subprocess

| Method | Returns | Description |
|--------|---------|-------------|
| `Subprocess.run(executable Executable, arguments StringArray)` | `CollectedOutput` | Inherit the working directory and environment, no stdin, collect stdout and stderr up to 16 MiB each, no timeout. |
| `Subprocess.run(executable, arguments:, workingDirectory FilePath)` | `CollectedOutput` | With a working directory. |
| `Subprocess.run(executable, arguments:, workingDirectory:, timeoutMs DurationMs)` | `CollectedOutput` | With a deadline after which the child's whole process tree is killed; `0` waits forever. |
| `Subprocess.runConfiguration(config Configuration)` | `CollectedOutput` | Run a configuration; `config.run()` calls this. |
| `Subprocess.runDetachedConfiguration(config Configuration)` | `Pid` | Start detached; `config.runDetached()` calls this. |

All of them throw `SubprocessError`.

### Executable

```maxon
union Executable
	name(value String)
	path(value FilePath)
end 'Executable'
```

`name` is searched for when the child is spawned; `path` is used as given. A relative `path` is relative
to the child's `workingDirectory` on every OS, and to the parent's working directory when none is set. A
`name` that carries a directory part (`sub/tool`, `./tool`, or on Windows `C:tool`) is anchored the same
way and looked for in that one directory.

On POSIX a `name` is searched for on `PATH` alone. On Windows the search follows `CreateProcessA`'s
order: the directory the running program was loaded from, this program's working directory while
`NoDefaultCurrentDirectoryInExePath` is unset, the system directory, the 16-bit system directory, the
Windows directory, then `PATH`. On Windows a name with an extension is tried as given, and a name without
one is tried with each `PATHEXT` extension in turn.

| Member | Returns | Description |
|--------|---------|-------------|
| `resolve()` | `FilePath` | The arm as a path, without searching `PATH`. Throws `ExecutableError.notFound` only for a name that is not a valid path; it is not an "is this installed" check. |
| `displayName()` | `String` | A readable form for messages. |

`ExecutableError` has one case, `notFound`.

### Configuration

Create one with `Configuration.create(executable)`, assign the fields you need, then run it.

| Field | Type | Default |
|-------|------|---------|
| `executable` | `Executable` | as given |
| `arguments` | `StringArray` | empty |
| `workingDirectory` | `FilePath` | empty, meaning the parent's |
| `environment` | `Environment` | `inherit` |
| `standardInput` | `InputSource` | `none` |
| `standardOutput` | `OutputDestination` | `collect` up to 16 MiB |
| `standardError` | `OutputDestination` | `collect` up to 16 MiB |
| `timeoutMs` | `DurationMs` | `0`, no deadline. Non-zero kills the child's whole process tree. |
| `platformOptions` | `PlatformOptions` | `PlatformOptions.defaults()` |

| Method | Returns | Description |
|--------|---------|-------------|
| `run()` | `CollectedOutput` | Run and collect. Throws `SubprocessError`. |
| `runDetached()` | `Pid` | Start without waiting and return the process id. Standard streams are discarded. Throws `SubprocessError`. |

`PlatformOptions` has two `bool` fields, `windowsHideWindow` and `windowsCreateNewProcessGroup`;
`PlatformOptions.defaults()` sets both false.

### Environment, input and output

```maxon
union Environment
	inherit
	inheritUpdating(overrides EnvMap)
	custom(vars EnvMap)
end 'Environment'

union InputSource
	none
	inherit
	bytes(data String)
	file(path FilePath)
	hold
	delayed(data String)
end 'InputSource'

union OutputDestination
	discard
	inherit
	collect(limitBytes int(0 to u64.max))
	file(path FilePath)
end 'OutputDestination'
```

| Environment | The child sees |
|-------------|----------------|
| `inherit` | This process's environment |
| `inheritUpdating(overrides)` | This process's environment with `overrides` applied |
| `custom(vars)` | Exactly `vars` |

`EnvMap` is `Map with String, String`. On Windows variable names compare as `Process.envNameKey` folds
them, ignoring case: an override replaces the inherited variable of any spelling and the child sees the
override's spelling, and a map that names one variable in two spellings makes the spawn throw
`SubprocessError.spawnFailed`.

| InputSource | The child's stdin |
|-------------|-------------------|
| `none` | Closed; reads see end of input immediately |
| `inherit` | This process's stdin |
| `bytes(data)` | `data`, then end of input |
| `file(path)` | The file's contents |
| `hold` | A pipe that stays open and silent until the child exits, so a read blocks |
| `delayed(data)` | Like `hold` until about one second after the child's first stdout byte, then `data` and end of input. Requires `standardOutput` to be `collect`; any other pairing throws `spawnFailed` before spawning. |

| OutputDestination | The stream |
|-------------------|------------|
| `discard` | Dropped |
| `inherit` | Passed through to this process's stream |
| `collect(limitBytes)` | Captured into `CollectedOutput`, truncated at the `limitBytes` limit |
| `file(path)` | Written to a file |

### Results

`CollectedOutput` has public fields and a factory, `CollectedOutput.create(status, stdout:, stderr:, pid:,
durationMs:)`:

| Field / method | Type | Description |
|----------------|------|-------------|
| `status` | `TerminationStatus` | How the child ended |
| `stdout`, `stderr` | `String` | Collected output (empty unless collected) |
| `pid` | `int(0 to u64.max)` | The child's process id |
| `durationMs` | `DurationMs` | Wall time the run took |
| `succeeded()` | `bool` | Exited with code 0 |
| `exitCode()` | `int(0 to u32.max)` | The raw code |

```maxon
union TerminationStatus
	exited(code int(0 to u32.max))
	signalled(code int(0 to u32.max))
end 'TerminationStatus'
```

On Linux and macOS a child that a signal killed is `signalled(signal)`, carrying the signal number; every other
end is `exited(code)`. A Windows child that ended abnormally is `exited` with its NTSTATUS code. `isSuccess()`
is true for `exited(0)`; `code()` returns the number either way.

### SubprocessError

```maxon
union SubprocessError implements Error
	executableNotFound(name String)
	spawnFailed(reason String)
	ioFailed(reason String)
	timeout(elapsedMs DurationMs, stdout String, stderr String)
	inputTooLarge
end 'SubprocessError'
```

`executableNotFound` is thrown on every target when the executable does not exist: a bare name no search
finds, or an `Executable.path` naming a missing file. `spawnFailed` is any other refusal to start the child —
a `file` stream that cannot be opened and a `workingDirectory` that does not exist included, though either
fails with a not-found code; its reason carries the OS error number (`os error 5`).

`timeout` carries the output the child had already produced when the deadline killed it, since that partial
text is usually the only evidence of why it hung. Both fields are empty when the layer that threw was not
collecting output, which is `StreamingSubprocess.waitWithTimeout`.

`displayReason()` renders any case as one line, such as
`timed out after 5000ms, and the kill was sent to the child's whole process tree`.

### StreamingSubprocess

A child whose standard streams stay open as pipes the caller drives, for a long-lived worker that answers
request after request. A read parks the calling green thread until data arrives.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `StreamingSubprocess.spawn(executable, arguments:)` | `StreamingSubprocess` | `SubprocessError` | Spawn in the parent's working directory. |
| `StreamingSubprocess.spawnWithCwd(executable, arguments:, workingDirectory:)` | `StreamingSubprocess` | `SubprocessError` | With a working directory. |
| `StreamingSubprocess.spawnWithEnvironment(executable, arguments:, workingDirectory:, environment Environment)` | `StreamingSubprocess` | `SubprocessError` | With a working directory (empty for the parent's) and an environment. |
| `StreamingSubprocess.spawnTraceable(executable, arguments:, workingDirectory:, environment Environment, traced bool)` | `StreamingSubprocess` | `SubprocessError` | As `spawnWithEnvironment`; with `traced`, the child is created with this process as its debugger. |
| `writeStdinLine(line String)` | — | `SubprocessError` | Write `line` and a newline. Throws on a broken pipe. |
| `readStdoutLine()` | `String` | `SubprocessError` | The next line without its terminator (CRLF or LF). `""` means end of stream. Lines over 1 MiB arrive in pieces. |
| `readStdoutLineCapped(maxBytes)` | `String` | `SubprocessError` | With an explicit per-call cap. |
| `readStdoutBytes(count)` | `String` | `SubprocessError` | Exactly `count` bytes, fewer only at end of stream; nothing is stripped. For length-framed protocols. Shares a buffer with the line readers. |
| `readStderrLine()` | `String` | `SubprocessError` | As `readStdoutLine`, for stderr. |
| `readStderrLineCapped(maxBytes)` | `String` | `SubprocessError` | With a cap. |
| `tryReadStdoutLine()` | `LinePoll` | — | A line if one is already buffered; never blocks. |
| `tryReadStderrLine()` | `LinePoll` | — | As above, for stderr. |
| `pollExit()` | `ExitPoll` | — | Whether the child has exited, without blocking or killing it. A released handle answers `running`. On Linux and macOS a child a signal killed reports `exited(128 + signal)`. |
| `pollTermination()` | `TerminationPoll` | — | As `pollExit()`, answering how the child ended: `signalled(signal)` for a child a signal killed on Linux and macOS, `exited(code)` otherwise. |
| `closeStdin()` | — | — | Close the child's stdin so it sees end of input. Idempotent. |
| `wait()` | exit code | `SubprocessError` | Block until the child exits. |
| `waitWithTimeout(timeoutMs DurationMs)` | exit code | `SubprocessError` | Throws `timeout` and kills the child's whole process tree when the deadline passes; `0` waits forever. The thrown `timeout` carries empty output fields — a streaming child's bytes belong to the caller draining the streams. |
| `release()` | — | — | Free the OS handle. Idempotent. Forgetting it leaks the handle and a process slot. |
| `handle`, `released` | fields | — | The raw handle and whether it has been released. |

```maxon
union LinePoll
	line(text String)
	none
end 'LinePoll'

union ExitPoll
	running
	exited(code int(0 to u32.max))
end 'ExitPoll'

union TerminationPoll
	running
	ended(status TerminationStatus)
end 'TerminationPoll'
```

`LinePoll` exists because a blank line from the child and "nothing buffered" are both `""` once the
terminator is removed. `none` says nothing about whether the stream has ended; ask `pollExit()`.

```maxon
function main() returns ExitCode
	var args = StringArray.create()
	args.push("--version")

	var child = try StreamingSubprocess.spawn(Executable.name("git"), arguments: args) otherwise (e) 'spawn'
		print("{e.displayReason()}\n")
		return 1
	end 'spawn'

	let line = try child.readStdoutLine() otherwise ""
	let code = try child.wait() otherwise -1

	let state = match child.pollExit() 'poll'
		running gives "running"
		exited(c) gives "exited {c}"
	end 'poll'

	child.release()
	print("{line.startsWith("git version")} {code} {state}\n")
	return 0
end 'main'
```

With `git` on `PATH`, it prints `true 0 exited 0`.

## SharedMemory

A `SharedSegment` is a named block of memory that several processes map at once: one publishes it under a
name, another adopts the same name and sees the same bytes. Available on `x64-windows` (Win32 section
objects), `arm64-macos` (`shm_open` and `mmap`) and both Linux targets (a `/dev/shm` file and `mmap`);
refused at compile time elsewhere.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `SharedSegment.create(name String, bytes SegmentByteCount)` | `SharedSegment` | `SharedMemoryError` | Publish a section of `bytes` bytes under `name` and map it, adopting one already published under that name. |
| `SharedSegment.publish(name String, bytes SegmentByteCount)` | `SharedSegment` | `SharedMemoryError` | Publish a NEW section of `bytes` bytes under `name` and map it; a name already published is refused with `createFailed`. |
| `segmentName()` | `String` | — | The name another process maps it by. |
| `readWord(offset SegmentOffset)` | `SegmentWord` | `SharedMemoryError` | The 64-bit word `offset` bytes in. |
| `writeWord(offset SegmentOffset, value SegmentWord)` | — | `SharedMemoryError` | Write a 64-bit word `offset` bytes in. |
| `copyOut(offset SegmentOffset, byteCount SegmentByteCount)` | `ByteArray` | `SharedMemoryError` | An independent copy of a byte range. |
| `close()` | — | — | Unmap and release the section, and withdraw its name if this segment published it. Idempotent. |

`readWord` is an acquire load and `writeWord` a release store, so a word is how one process announces
bytes to another: once `readWord` returns a value another process wrote with `writeWord`, every write that
process made before it is visible, including a range read afterwards with `copyOut`.

Offsets are in bytes, not words. Every access is checked against the section's size: a word (8 bytes) or a
`copyOut` range (`offset + byteCount`) that would reach past the end throws `SharedMemoryError.outOfBounds`
and touches nothing.

**`create` adopts a name that is already published**, keeping the size that section already has; on every
target one smaller than `bytes` is refused with `SharedMemoryError.createFailed`. Two processes creating one
name at once both succeed, one publishing and one adopting. On Linux and macOS a section whose publisher exited
without `close()` is still published, and `create` adopts it. **`publish` only publishes**: a caller that needs
a fresh section gets one, or `createFailed` when the name is taken. **`close` withdraws the name only when this
segment published it**: an adopter's `close` leaves the name published for as long as the publisher holds it.

| Type | Definition |
|------|------------|
| `SegmentByteCount` | `int(1 to u32.max)` — never zero |
| `SegmentOffset` | `int(0 to u32.max)` |
| `SegmentWord` | `int(i64.min to i64.max)` |

```maxon
union SharedMemoryError implements Error
	createFailed
	mapFailed
	invalidName
	outOfBounds
end 'SharedMemoryError'
```

`createFailed`: the name is published by a section smaller than `bytes` (`create`) or is published at all
(`publish`), or the host refuses the name or the size.
`invalidName`: the name is empty, longer than 31 bytes, or holds a `/`, a `\` or a NUL byte. The rule is the
same on every target (31 bytes is macOS's limit), so a name that works on one host works on all of them.
`mapFailed`: no address space for the view. `outOfBounds`: a `readWord`, `writeWord` or `copyOut` would
reach past the end of the section.

Always `close()` a segment. A section stays alive while any view of it is mapped, and on Linux and macOS the
name outlives the process until it is withdrawn or the machine restarts.

```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("docs-demo-segment", bytes: 4096) otherwise (e) 'failed'
		print("no segment: {e}\n")
		return 1
	end 'failed'

	try segment.writeWord(8, value: 42) otherwise return 2
	let word = try segment.readWord(8) otherwise return 2
	let copied = try segment.copyOut(8, byteCount: 8) otherwise return 2
	print("{segment.segmentName()} {word} {copied.count()}\n")
	segment.close()
	return 0
end 'main'
```

Output: `docs-demo-segment 42 8`.
