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
| `File.readText(path FilePath)` | `String` | `FileReadError` | The whole file as text, read to its end — bytes appended while it is read are included. |
| `File.readBinary(path FilePath)` | `ByteArray` | `FileReadError` | The whole file as bytes, read the same way. |
| `File.writeText(path FilePath, content String, mode FilePermission = .normal)` | — | `FileWriteError` | Create or truncate, then write. |
| `File.createText(path FilePath, content String)` | — | `FileWriteError` | Create a file that must not exist yet, then write. An existing file is `alreadyExists` and is left as it was. A failed write deletes the new file and throws the write's error; when that delete fails too, it throws `partialFileLeft`. |
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

Each operation throws its own error enum. They share these cases:

| Case | Thrown when |
|------|-------------|
| `notFound` | The file, or a directory on its path, does not exist |
| `accessDenied` | The operating system refuses access to the path |
| `busy` | Another process holds the file open in a way that excludes this operation |
| `failed` | Any other failure |

On every target a path holding a NUL byte is a missing file: each operation on it answers as it does for a
file that is not there, and the file named by the bytes before the NUL is left as it was.

| Error enum | Cases |
|------------|-------|
| `FileReadError` | `notFound`, `accessDenied`, `busy`, `failed` |
| `FileWriteError` | `failed`, `notFound`, `accessDenied`, `alreadyExists`, `busy`, `partialFileLeft` |
| `FileDeleteError` | `notFound`, `accessDenied`, `busy`, `failed` |
| `FileRenameError` | `failed`, `notFound`, `accessDenied`, `busy` |
| `FileInfoError` | `notFound`, `accessDenied`, `busy`, `failed` |

`alreadyExists` and `partialFileLeft` come only from `File.createText`. Every one of these enums has
`failure()`, which returns the cause as a `FileFailure` — `notFound`, `accessDenied`, `alreadyExists`, `busy`
or `failed` (`partialFileLeft` gives `failed`) — so one `match` handles the causes of any file operation.

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
| `FilePath.from(path String)` | `FilePath` | Throws `FilePathError.invalidCharacter` on Windows for a control character (NUL included) or one of `< > " \| ? *`, `notFileURL` for a URL whose scheme is other than `file`, or `malformedURL` for text holding `://` that `URL.parse` refuses. |
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
| `joinPath(component FilePath)` | `FilePath` | Append another path. |
| `changeExtension(newExt String)` | `FilePath` | Replace or add an extension; include the dot (`".exe"`). |
| `anchoredAt(base FilePath)` | `FilePath` | A relative path joined onto `base`, or an absolute path kept. Only what cannot change the file named is folded — `.` components, repeated separators and a trailing one — and every `..` is left for the filesystem, so through a symbolic link it names the file the spelling names. On Windows a drive-relative path (`C:foo`) is joined onto a `base` rooted on the same drive; against any other `base` it stays drive-relative, because it names that drive's current directory. |
| `resolve(base FilePath)` | `FilePath` | `anchoredAt(base)`, then folded as `normalize()` folds it — lexically, so a `..` after a symbolic link cancels the link's name. |
| `resolvedPhysically()` | `FilePath` | The path's one physical spelling, which the filesystem answers: every symbolic link along it is followed, every `..` is taken against the directory the walk has reached, and a relative path is taken against the working directory. Two spellings of one file answer the same path, so it is the path to key a cache or a lock by; the lexical `normalize()` gives each spelling a key of its own. On Windows the `\\?\` prefix is dropped and a network path comes back as `\\server\share\…`; a volume reachable only as `\\?\Volume{…}\` throws `failed`. Throws `PathResolveError.notFound` when the path names nothing, `accessDenied` when the process may not look it up, and `failed` for any other refusal, which on Linux includes an unmounted `/proc`. Refused on `wasm32-wasi` with E3104 ([Target support](/docs/stdlib/#target-support)). |
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
| `equals(other FilePath)`, `hash()` | | Host filesystem semantics: they compare `comparisonSpelling()`. |
| `comparisonSpelling()` | `String` | The path as the host compares paths: lower-cased on Windows, as written on macOS and Linux. `equals`, `hash` and `isInside` compare this spelling, so it is the one to key a map or a digest by. |

```maxon
enum FilePathError implements Error
	invalidCharacter
	notFileURL
	malformedURL
	noParent
end 'FilePathError'
```

```maxon
enum PathResolveError implements Error
	notFound
	accessDenied
	failed
end 'PathResolveError'
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
| `Directory.list(path FilePath)` | `FilePathArray` | `DirectoryListError` | The entries of a directory, each joined onto `path`. Throws `notFound` for a directory that does not exist, `accessDenied` for one the process may not list, and `listFailed` for any other failure. |
| `Directory.exists(path FilePath)` | `bool` | — | True when `path` is an existing directory. |
| `Directory.isDirectory(path FilePath)` | `bool` | — | The same as `exists`. |
| `Directory.create(path FilePath)` | `bool` | — | Create the directory and any missing parents. True when the directory exists afterwards. |
| `Directory.delete(path FilePath)` | — | `DirectoryDeleteError` | Remove an empty directory. A directory that still holds an entry is refused, not emptied. |
| `Directory.removeTree(path FilePath)` | `FilePathArray` | — | Remove `path` and everything below it, and answer the entries that could not be removed; an empty answer means nothing is left, and a path that does not exist answers empty. A symbolic link or junction is removed as one entry, and what it points at stays as it was. |
| `Directory.removeContents(path FilePath)` | `FilePathArray` | — | Remove everything inside the directory `path` and keep the directory, answering what could not be removed as `removeTree` does. |
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

On every target a path holding a NUL byte is a missing directory: `Directory.create` answers false, and the
other methods answer as they do for a directory that is not there.

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
| `Stdin.create()` | `Stdin` | — | A reader with an empty buffer; `Console.stdin()` returns one. |
| `Console.stdoutWantsColor()` | `bool` | — | True when standard output is a terminal, `NO_COLOR` is unset and `TERM` is other than `dumb` (compared case-insensitively): output may then carry ANSI escapes. `NO_COLOR` set to any value turns colour off; Windows reads an empty value as unset. `false` on `wasm32-wasi`. |
| `readLine()` | `String` | `ConsoleError.endOfFile` | The next line without its `\n`; a `\r` before it is removed too. A last line with no terminator is returned, and the call after it throws. |
| `readBytes(n BytePos)` | `ByteArray` | — | Up to `n` bytes, unchanged. Fewer than `n` only when input ends first; an empty array at the end of input. It reads from the same buffer as `readLine`. |

```maxon
function main() returns ExitCode
	let stdin = Console.stdin()
	var lines = 0 as Count

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
| `CommandLine.readOptionalIntArgument(position Count, otherwise Count)` | `Count` | `ParseError.invalidFormat` | The non-negative integer in the argument at `position` (0 is the program path, 1 the first argument after it), or `otherwise` when there is no argument at `position`. An argument present but not a non-negative integer, or beyond `i64.max`, throws. |

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

```maxon
function main() returns ExitCode
	let rounds = try CommandLine.readOptionalIntArgument(1, otherwise: 1000) otherwise return 2
	print("{rounds} rounds
")
	return 0
end 'main'
```

Run as `program 50` it prints `50 rounds`, run bare it prints `1000 rounds`, and run as `program -3` or `program many` it returns 2.

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
Below ERROR, a call sends its record to the handler and returns, and the handler writes it. When the
handler's mailbox is full, the call waits up to 10 seconds for room and then discards the record; a record
logged after the handler has stopped is discarded. At ERROR and above the call waits for room as long as it
takes. `Log.error` —
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
	public static function audit(message String, attrs LogAttrArray = LogAttrArray.create(0), file String = __file__, line SourceLineNumber = __line__)
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

A `LogHandler` is a service interface with one requirement, `handle(mutable self, record immutable LogRecord) returns bool`. A sink
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

A handler of your own implements `LogHandler`, declares its parameter `record immutable LogRecord`, and receives a frozen `LogRecord`:

| Member | Returns | Description |
|--------|---------|-------------|
| `time` | `UnixNanos` | When the record was made. |
| `level`, `message` | `LogLevel`, `String` | |
| `attrs` | `LogFieldArray` | The logger's bound fields, then the call's own. |
| `file`, `line` | `String`, `SourceLineNumber` | The caller's location. |
| `LogRecord.create(time, level:, message:, attrs:, file:, line:)` | `LogRecord` | A record. `attrs` is an `isolated LogFieldArray`: pass a fresh array or an `isolated` local. `freeze` the record to send it to a handler. |

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

A capture holds only the keys the program emitted itself. The capture lives in a recorder service, the
library's `default` for its capture control: a program that calls `TraceCapture` spawns that service before
`main` runs, and every thread and service traces into the one capture.

| Method | Returns | Description |
|--------|---------|-------------|
| `TraceCapture.trace(key String)` | — | Send `key` to the recorder when capturing. Use a stable dotted name. |
| `TraceCapture.startCapture()` | — | Clear the recorder, then switch capturing on. |
| `TraceCapture.stopCapture()` | `TraceKeyArray` | Switch capturing off, then await the recorder's keys, in the order it received them; the recorder starts empty again. |
| `TraceCapture.fired(capturedKeys TraceKeyArray, key String)` | `bool` | True when `key` was recorded at least once. |

`trace`, `startCapture` and `stopCapture` wait for room when the recorder's mailbox is full, as long as it
takes (`sendTimeout: ForeverMs`). In a cancelled coroutine the message is dropped, and `stopCapture` returns
an empty array.

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
| `Process.id()` | `Pid` | — | This process's id. |
| `Process.namespace()` | `String` | — | The pid space this process belongs to: the `/proc/self/ns/pid` link target on Linux, the boot-session UUID on macOS, the machine's `MachineGuid` on Windows; `""` when the host withholds it. Two processes may probe each other's ids when their namespaces are equal. |
| `Process.isAlive(pid Pid)` | `bool` | — | True while the process with id `pid` is running; `false` once it has ended, whether or not its parent has reaped it, and for an id no host hands out (0, or one past 32 bits). |
| `Process.endDescendants()` | `DescendantProcessArray` | — | End every process descended from this one that is still running, detached ones included, wait until each has ended, and answer them; a descendant that has already ended is left out. Panics when the host cannot list every descendant or one has not ended five seconds after its kill, and when the answer needs more than 64 KiB, which it reports after ending them all. `maxon test` and `spec-test` call it at the end of each test and case (see [Processes a test leaves](/docs/cli/#maxon-test)). A program that calls it tracks its descendants from startup, so the first call also finds the processes started before it. |
| `Process.enterBackgroundPriority()` | `BackgroundPriority` | — | Put this process at background scheduling priority and answer the priority the OS reports afterwards: a priority class on Windows (`BELOW_NORMAL_PRIORITY_CLASS`, 16384), a nice value under POSIX (10). Children spawned afterwards inherit it. `BackgroundPriority` is `int(i64.min to i64.max)`. |
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

`DescendantProcess` is one process `Process.endDescendants()` ended: `pid` (`Pid`) and `name` (`String`),
the name the host gives its executable with every byte outside printable ASCII shown as `?`.
`DescendantProcess.create(pid, name:)` makes one. `DescendantProcessArray` is `Array with DescendantProcess`.

## Subprocess

`Subprocess` starts child processes. `Subprocess.run` waits for the child and collects its output;
`Configuration` gives full control; `StreamingSubprocess` keeps the child's pipes open for line-by-line
conversation. A spawn from a green thread parks that green thread, so others keep running.

Not available on `wasm32-wasi`, which has no process spawning: every call is refused at compile time with
E3074.

```maxon
function main() returns ExitCode
	var args = StringArray.create(0)
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
| `Subprocess.run(executable Executable, arguments StringArray, workingDirectory FilePath = FilePath.empty(), timeoutMs DurationMs = 0)` | `CollectedOutput` | Inherit the environment, no stdin, collect stdout and stderr up to 16 MiB each. An empty `workingDirectory` inherits the parent's. A `timeoutMs` deadline kills the child's whole process tree ([SubprocessError](#subprocesserror)); `0` waits forever. |
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

On POSIX a `name` is searched for on `PATH` alone. On Windows the search follows `CreateProcessW`'s
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

Create one with `Configuration.create(executable)` (or `Configuration.create(executable, workingDirectory: dir)`;
an empty path inherits the parent's working directory), assign the fields you need, then run it.

| Field | Type | Default |
|-------|------|---------|
| `executable` | `Executable` | as given |
| `arguments` | `StringArray` | empty |
| `workingDirectory` | `FilePath` | empty, meaning the parent's |
| `environment` | `Environment` | `inherit` |
| `standardInput` | `InputSource` | `none` |
| `standardOutput` | `OutputDestination` | `collect` up to 16 MiB |
| `standardError` | `OutputDestination` | `collect` up to 16 MiB |
| `timeoutMs` | `DurationMs` | `0`, no deadline. Non-zero kills the child's whole process tree ([SubprocessError](#subprocesserror)). |
| `platformOptions` | `PlatformOptions` | `PlatformOptions.defaults()` |

| Method | Returns | Description |
|--------|---------|-------------|
| `run()` | `CollectedOutput` | Run and collect. Throws `SubprocessError`. |
| `runDetached()` | `Pid` | Start without waiting and return the process id. Standard streams are discarded. The process is outside the process tree of any deadline ([SubprocessError](#subprocesserror)). Throws `SubprocessError`: `detachRefused` on Windows when the job this program runs in does not allow breakaway, and `spawnFailed` on Linux when the child cannot start its own session. |

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
| `bytes(data)` | `data` whole, NUL bytes included, then end of input |
| `file(path)` | The file's contents |
| `hold` | A pipe that stays open and silent until the child exits, so a read blocks |

| OutputDestination | The stream |
|-------------------|------------|
| `discard` | Dropped |
| `inherit` | Passed through to this process's stream |
| `collect(limitBytes)` | Captured into `CollectedOutput`, truncated at the `limitBytes` limit |
| `file(path)` | Written to a file |

A run feeds `bytes` input while it drains the child's output, so a child that writes before it
reads still receives its whole input, and a `timeoutMs` deadline fires while input is still pending.

A child inherits only the three standard streams its own configuration gives it; the files this process
holds open and the pipes of other spawns in progress at the same moment stay with this process.

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
	treeNotEnded(elapsedMs DurationMs, stdout String, stderr String, osError OsErrorCode)
	detachRefused(osError OsErrorCode)
	inputTooLarge
	endOfStream
	statusLost(stdout String, stderr String)
end 'SubprocessError'
```

`executableNotFound` is thrown on every target when the executable does not exist: a bare name no search
finds, or an `Executable.path` naming a missing file. `spawnFailed` is any other refusal to start the child —
a `file` stream that cannot be opened and a `workingDirectory` that does not exist included, though either
fails with a not-found code; its reason carries the OS error number (`os error 5`). A spawn whose pipe
cannot be made throws it with the error of the call that failed, `os error 4` when the process has too many
files open.

A NUL byte in the program name or path, an argument, the working directory, an environment variable's name
or value, or a `file` stream's path throws `spawnFailed` before any child starts, with a reason naming that
operand.

`timeout` carries the output the child had already produced when the deadline killed it, since that partial
text is usually the only evidence of why it hung. Both fields are empty when the layer that threw was not
collecting output, which is `StreamingSubprocess.waitWithTimeout`.

A deadline ends the child's whole process tree, and so does [`StreamingSubprocess.endTree()`](#streamingsubprocess).
On Linux and macOS the tree is the child and every process
in the child's session descended from it by parent at the moment of the kill, however many there are: each
is stopped as it is found, the search repeats until a full pass finds no new process, and then the
descendants are killed, newest first, together with the child's process group. The child's process group is
ended even when the child itself has already exited. A process started with `runDetached()`, any other
process in another session, and everything such a process starts are outside the tree, as is a process
already reparented to init, as one that double-forked is. On Windows the tree is the child's job object,
which a process started with `runDetached()` leaves. When the tree cannot be fully ended (on Linux `/proc`
cannot be read or listed, on macOS `proc_listchildpids` fails, an allocation fails, or the kill of an exited
child's process group is refused), the deadline throws `treeNotEnded`, after the child itself is reaped. It
carries the output `timeout` carries on the same path and the error code of the step that failed as an
[`OsErrorCode`](/docs/stdlib/#names-a-library-signature-asks-for), and its reason is
`timed out after 5000ms, but the child's process tree could not be fully ended: os error <n>`. `deadlineFired()`
is true for it and `timedOut()` false.

`detachRefused` is what `runDetached()` throws on Windows when the job this program runs in does not allow
breakaway. It carries the `GetLastError` code, and its reason says to run the program outside that job or allow
breakaway on it.

`endOfStream` is what a `StreamingSubprocess` line reader throws when the stream it reads has ended.

`statusLost(stdout, stderr)` is what `Subprocess.run` and `StreamingSubprocess.wait()` / `waitWithTimeout()` throw for a
child that has ended while another process took its exit status, so the status is unavailable. It carries the
output `run` collected; `wait()` and `waitWithTimeout()` throw empty fields. Whether the status is available
depends on whether the child finished dying before the process holding it, such as a debugger's stub, let it go.

`displayReason()` renders any case as one line, such as
`timed out after 5000ms, and the kill was sent to the child's whole process tree`. `timedOut()` is true for
`timeout`, `deadlineFired()` for `timeout` and `treeNotEnded`, and `endedTheStream()` for `endOfStream`, so a
caller can test for any of them without a `match`.

`SpawnPreparation` — `none`, `standardStream`, `workingDirectory` — names the step of a spawn the runtime
records a failure at; a not-found code at `none` is what makes a failure `executableNotFound`.

### RunProcessError

```maxon
enum RunProcessError implements Error
	spawnFailed
end 'RunProcessError'
```

The error the library-tier intrinsic `__Builtins.runProcess` throws when its command names nothing the host
can start. A program starts processes through `Subprocess`, which reports its failures as `SubprocessError`.

### Spawn records

`SpawnEnvironment` and `StdioRuntimeTriple` are the operands `Subprocess` hands the runtime's spawn contract. A
program starts processes through `Subprocess` and builds neither.

| Member | Description |
|--------|-------------|
| `SpawnEnvironment.create(block String, source EnvSourceValue)` | The environment block and where the child's environment comes from, `EnvSource.parent` or `EnvSource.block`. One value because the two are one decision. Its fields are `block` and `source`. |
| `StdioRuntimeTriple.create(kind StdioKindValue, data String, limit ByteLimit)` | One stdio stream's kind, its payload and its byte limit. `data` is a path for a `file` stream and empty for a kind that has no payload; `limit` is read only for `collect`. Its fields are `kind`, `data` and `limit`. |

### StreamingSubprocess

A child whose standard streams stay open as pipes the caller drives, for a long-lived worker that answers
request after request. A read or a wait parks the calling green thread, so its siblings keep running.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `StreamingSubprocess.spawn(executable, arguments:)` | `StreamingSubprocess` | `SubprocessError` | Spawn in the parent's working directory. |
| `StreamingSubprocess.spawnWithCwd(executable, arguments:, workingDirectory:)` | `StreamingSubprocess` | `SubprocessError` | With a working directory. |
| `StreamingSubprocess.spawnWithEnvironment(executable, arguments:, workingDirectory:, environment Environment)` | `StreamingSubprocess` | `SubprocessError` | With a working directory (empty for the parent's) and an environment. |
| `StreamingSubprocess.spawnTraceable(executable, arguments:, workingDirectory:, environment Environment, traced bool)` | `StreamingSubprocess` | `SubprocessError` | As `spawnWithEnvironment`; with `traced`, the child is created for a debugger: on Windows and Linux this process becomes its debugger, and on macOS the child is created suspended, for a debugger to attach to by its `processId()`. |
| `processId()` | `Pid` | `SubprocessError` | The child's operating-system process id. Throws once the handle is released. |
| `writeStdinLine(line String)` | — | `SubprocessError` | Write `line` and a newline, NUL bytes included. Throws on a broken pipe. |
| `takeStdout()` | `StdoutReader` | `SubprocessError` | Take the child's stdout side out as a `StdoutReader`, which a coroutine can drain beside the child's owner. While the reader is out, the child's own stdout reads (`readStdoutLine`, `readStdoutLineCapped`, `readStdoutBytes`, `tryReadStdoutLine`) throw `ioFailed`. Throws `ioFailed` when the stdout side is already out, and once the handle is released. |
| `reclaimStdout()` | — | `SubprocessError` | Make the stdout side the child's again, once the coroutine that held the `StdoutReader` has ended. Throws `ioFailed` when the stdout side is not out. |
| `takeStderr()` | `StderrReader` | `SubprocessError` | As `takeStdout()`, for the child's stderr side: while the `StderrReader` is out, `readStderrLine`, `readStderrLineCapped` and `tryReadStderrLine` throw `ioFailed`. Throws `ioFailed` when the stderr side is already out, and once the handle is released. |
| `reclaimStderr()` | — | `SubprocessError` | Make the stderr side the child's again, once the coroutine that held the `StderrReader` has ended. Throws `ioFailed` when the stderr side is not out. |
| `readStdoutLine()` | `String` | `SubprocessError` | The next line without its terminator (CRLF or LF); `""` is a blank line. Throws `endOfStream` when the stream has ended. Lines over 1 MiB arrive in pieces. |
| `readStdoutLineCapped(maxBytes)` | `String` | `SubprocessError` | With an explicit per-call cap. |
| `readStdoutBytes(count)` | `String` | `SubprocessError` | Exactly `count` bytes, fewer only at end of stream; nothing is stripped. For length-framed protocols. Shares a buffer with the line readers. |
| `readStderrLine()` | `String` | `SubprocessError` | As `readStdoutLine`, for stderr. |
| `readStderrLineCapped(maxBytes)` | `String` | `SubprocessError` | With a cap. |
| `tryReadStdoutLine()` | `LinePoll` | `SubprocessError` | A line if one is already buffered; never blocks. Throws `ioFailed` while the stdout side is out with a `StdoutReader`. |
| `tryReadStderrLine()` | `LinePoll` | `SubprocessError` | As above, for stderr. Throws `ioFailed` while the stderr side is out with a `StderrReader`. |
| `pollExit()` | `ExitPoll` | — | Whether the child has exited, without blocking or killing it. A released handle answers `running`. On Linux and macOS a child a signal killed reports `exited(128 + signal)`. |
| `isReapedElsewhere()` | `bool` | — | Whether another process has taken the child's exit status, so that `pollExit()` answers `running` for ever. Whether a status is available depends on whether the child finished dying before the process holding it, such as a debugger's stub, let it go. `false` on Windows and for a released handle. Answers at once. |
| `pollTermination()` | `TerminationPoll` | — | As `pollExit()`, answering how the child ended: `signalled(signal)` for a child a signal killed on Linux and macOS, `exited(code)` otherwise. |
| `streamHasEnded(stream StdioStream)` | `bool` | — | Whether a read of the child's `stdout` or `stderr` has found the end of that stream, answering at once. A read latches the end, so a loop of `tryReadStdoutLine` / `tryReadStderrLine` calls moves this to `true`. A read that failed and a released handle answer `true`. |
| `outputHasEnded()` | `bool` | — | Whether reads have found the end of both `stdout` and `stderr`: `streamHasEnded` for each. |
| `closeStdin()` | — | — | Close the child's stdin so it sees end of input. Idempotent. |
| `wait()` | exit code | `SubprocessError` | Park the calling green thread until the child exits. |
| `waitWithTimeout(timeoutMs DurationMs)` | exit code | `SubprocessError` | Parks the calling green thread until the child exits. Throws `timeout` and kills the child's whole process tree ([SubprocessError](#subprocesserror)) when the deadline passes, or `treeNotEnded` when that tree cannot be fully ended; `0` waits forever. A child whose exit status went to another process throws `statusLost`. Both deadline errors carry empty output fields — a streaming child's bytes belong to the caller draining the streams. |
| `endTree()` | `TreeEnd` | — | Ends the child's whole process tree at once, the kill a `waitWithTimeout` deadline sends but with no wait to expire, and returns once every process it killed has ended, waiting at most five seconds for them. The child may already have exited: what it left running is the tree. The answer is `ended` when the tree is gone, `survived(osError)` when the child is gone but part of its tree could not be ended or had not ended within those five seconds, and `unconfirmed(reason)` when the kill was refused and the child had not ended within those five seconds, or the handle is released. |
| `release()` | — | — | Free the OS handle. Idempotent. Forgetting it leaks the handle and a process slot. Panics while the stdout side is out with a `StdoutReader` or the stderr side with a `StderrReader`: `reclaimStdout()` or `reclaimStderr()` first. |
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

union TreeEnd
	ended
	survived(osError OsErrorCode)
	unconfirmed(reason String)
end 'TreeEnd'

enum StdioStream
	stdout
	stderr
end 'StdioStream'
```

`LinePoll` exists because a blank line from the child and "nothing buffered" are both `""` once the
terminator is removed. `none` says nothing about whether the stream has ended; ask `pollExit()` for the
child, or `streamHasEnded(stream)` for the stream.

`TreeEnd.displayReason()` renders the answer as one line for a diagnostic.

### StdoutReader

The stdout side of a `StreamingSubprocess`, taken out with `takeStdout()`. It reads lines and owns nothing
else: no release and no wait. It is a clean input to `async`, so a coroutine can drain it while the child's
owner keeps writing stdin and waiting, and the promise of that coroutine may be stored and awaited later.
Once the coroutine has ended, `reclaimStdout()` makes the stdout side the child's again, and `release()` of
the child follows.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `readLine()` | `String` | `SubprocessError` | The next line without its terminator; the contract of `StreamingSubprocess.readStdoutLine`, including `endOfStream` when the stream has ended. |
| `readBytes(count ByteLimit)` | `String` | `SubprocessError` | Exactly `count` bytes, fewer only at end of stream; the contract of `StreamingSubprocess.readStdoutBytes`, for a length-framed protocol read in a coroutine. |

`specs/subprocess.md` pins the flow: `subprocess-take-stdout-moves-the-stdout-side-out-of-the-child`,
`subprocess-release-while-the-stdout-reader-is-out-is-refused` and
`subprocess-a-reader-whose-drain-ends-is-reclaimed-before-the-child-is-released`.

```maxon
function main() returns ExitCode
	var args = StringArray.create(0)
	args.push("--version")

	var child = try StreamingSubprocess.spawn(Executable.name("git"), arguments: args) otherwise (e) 'spawn'
		print("{e.displayReason()}\n")
		return 1
	end 'spawn'

	let line = try child.readStdoutLine() otherwise (e) 'read'
		print("{e.displayReason()}\n")
		return 1
	end 'read'

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

### StderrReader

The stderr side of a `StreamingSubprocess`, taken out with `takeStderr()`, on the terms `StdoutReader` has
for stdout: it reads lines and owns nothing else, a coroutine can drain it beside the child's owner, and
`reclaimStderr()` makes the stderr side the child's again once that coroutine has ended. Draining stderr
in its own coroutine keeps a child that writes a lot of it from blocking on a full pipe.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `readLine()` | `String` | `SubprocessError` | The next line without its terminator; the contract of `StreamingSubprocess.readStderrLine`, including `endOfStream` when the stream has ended. |

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
| `close()` | — | — | Unmap and release the section, and withdraw its name if this segment published it. Idempotent. A `readWord`, `writeWord` or `copyOut` afterwards throws `SharedMemoryError.closed`. |

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
	closed
end 'SharedMemoryError'
```

`createFailed`: the name is published by a section smaller than `bytes` (`create`) or is published at all
(`publish`), or the host refuses the name or the size.
`invalidName`: the name is empty, longer than 31 bytes, or holds a `/`, a `\` or a NUL byte. The rule is the
same on every target (31 bytes is macOS's limit), so a name that works on one host works on all of them.
`mapFailed`: no address space for the view. `outOfBounds`: a `readWord`, `writeWord` or `copyOut` would
reach past the end of the section. `closed`: a `readWord`, `writeWord` or `copyOut` on a segment after its
`close()`.

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

## Debuggee

The debugger surface: `DebugPort` drives a debuggee through the Win32 debug API on Windows, and
`ProcessTrace` drives one through `ptrace` on Linux. Both types compile on every target; an operation that
reaches the debug channel of the other host panics. Each owns a scratch region it reserves at `create()`
(`isReady()` reports whether it did) and releases at `close()`; an operation after `close()` panics.

A transfer through `readMemory` or `writeCode` moves at most `DebugTransferBytes` (512) bytes. A longer one
throws `unreadable` or `unwritable` before the debuggee is touched, and a transfer of 0 bytes returns at once.
`transferSizeOf(count TransferByteCount)` answers which of the three a count is: a `TransferSize` of
`nothing`, `withinTheWindow` or `pastTheWindow`.

```maxon
enum DebuggeeError implements Error
	unreadable
	unwritable
	outOfBounds
end 'DebuggeeError'
```

`unreadable`: memory or registers the OS refused to read, or a read past the window.
`unwritable`: a write the debuggee refused, a write past the window, or a register block of the wrong size.
`outOfBounds`: a `RegisterBlock` offset off a word boundary or past the end.

### RegisterBlock

Every register set a debuggee hands back is a `RegisterBlock`: a record of 64-bit words read at word-aligned
byte offsets.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `word(offset RegisterOffset)` | `RegisterWord` | `DebuggeeError.outOfBounds` | The word at byte `offset`, a multiple of 8 below the block's size. |
| `with(offset RegisterOffset, word RegisterWord)` | `RegisterBlock` | `DebuggeeError.outOfBounds` | A new block with that word replaced; the receiver keeps its words. |
| `programCounter()`, `stackPointer()`, `framePointer()` | `RegisterWord` | `DebuggeeError.outOfBounds` | `rip`, `rsp` and `rbp` of a Windows x64 `CONTEXT`. |
| `flags()` | `RegisterWord` | `DebuggeeError.outOfBounds` | `EFlags` of a Windows x64 `CONTEXT`. |
| `gpr(index Win64GprIndex)` | `RegisterWord` | `DebuggeeError.outOfBounds` | General-purpose register `index` of a `CONTEXT`, in its order `rax`, `rcx`, `rdx`, `rbx`, `rsp`, `rbp`, `rsi`, `rdi`, `r8` to `r15`. |
| `xmmLane(index Win64XmmIndex)` | `RegisterWord` | `DebuggeeError.outOfBounds` | The low 64 bits of `xmm<index>` of a `CONTEXT`. |
| `withProgramCounter(address RegisterWord)` | `RegisterBlock` | `DebuggeeError.outOfBounds` | A new `CONTEXT` with `rip` replaced. |
| `withFlags(flags RegisterWord)` | `RegisterBlock` | `DebuggeeError.outOfBounds` | A new `CONTEXT` with `EFlags` replaced. |

The `CONTEXT` accessors throw `outOfBounds` for a block of any other size.

### DebugPort

`DebugPort` holds the event the debuggee last reported; `continueEvent` and `eventThread` refer to it.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `DebugPort.create()` | `DebugPort` | — | A port with its scratch region reserved. |
| `isReady()` | `bool` | — | True while the scratch region is held. |
| `close()` | — | — | Release the scratch region. |
| `waitEvent(budgetMs DebuggeeWaitMs)` | `DebugPortEvent` | — | Wait up to `budgetMs` for the next debug event. |
| `eventThread()` | `DebuggeeThreadId` | — | The thread that reported the last event. |
| `continueEvent(disposition DebugDisposition)` | `bool` | — | Let the debuggee run on from the last event, `handled` or `unhandled`; true when the OS accepted it. |
| `readMemory(process DebugHandle, address DebuggeeAddress, count DebuggeeByteCount)` | `ByteArray` | `DebuggeeError.unreadable` | `count` bytes of the debuggee's memory; a short read throws. |
| `writeCode(process DebugHandle, address DebuggeeAddress, content ByteArray)` | — | `DebuggeeError.unwritable` | Write `content` into code: lift the page protection, write, restore it and flush the instruction cache. |
| `readContext(thread DebugHandle)` | `RegisterBlock` | `DebuggeeError.unreadable` | The thread's `CONTEXT`: control, integer and floating-point registers. |
| `writeContext(thread DebugHandle, context RegisterBlock)` | — | `DebuggeeError.unwritable` | Set the thread's `CONTEXT` from a block of `CONTEXT` size. |
| `suspend(thread DebugHandle)`, `resume(thread DebugHandle)` | `bool` | — | Suspend or resume the thread; true on success. |
| `breakIn(process DebugHandle)` | `bool` | — | Make the debuggee raise a breakpoint. |
| `detach(processId DebuggeeProcessId)` | `bool` | — | Stop debugging the process and let it run. |

`DebugHandle` is an OS handle a `DebugPortEvent` carries. `DebugDisposition` is `handled` or `unhandled`.

| `DebugPortEvent` case | Meaning |
|-----------------------|---------|
| `timedOut` | The budget passed with the debuggee silent. |
| `processCreated(processId, thread, process, threadHandle, imageBase)` | The debuggee started; `process` and `threadHandle` are its `DebugHandle`s. |
| `threadCreated(thread, threadHandle, startAddress)` | A thread started. |
| `threadExited(thread)` | A thread ended. |
| `processExited(thread, exitCode)` | The debuggee ended with `exitCode` (`DebuggeeExitCode`). |
| `libraryLoaded(thread)` | A DLL loaded. |
| `exception(thread, code, address)` | An exception, with its `DebuggeeFaultCode` and address. |
| `other(thread)` | Any other debug event. |

### ProcessTrace

`ProcessTrace` addresses the debuggee by process and thread id.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `ProcessTrace.create()` | `ProcessTrace` | — | A tracer with its scratch region reserved. |
| `isReady()` | `bool` | — | True while the scratch region is held. |
| `close()` | — | — | Release the scratch region. |
| `configure(process DebuggeeProcessId)` | `bool` | — | Trace new threads of `process` too, and kill it when the tracer exits. |
| `wait(budgetMs DebuggeeWaitMs, process DebuggeeProcessId)` | `TraceReport` | — | Wait up to `budgetMs` for a thread of `process` to report. |
| `resume(thread DebuggeeThreadId, signal DebuggeeSignal, stepping bool)` | `bool` | — | Continue the thread, or single-step it when `stepping`, delivering `signal`; `NoTraceSignal` (0) resumes it silently. |
| `detach(thread DebuggeeThreadId, signal DebuggeeSignal)` | `bool` | — | Stop tracing the thread, delivering `signal`. |
| `cloneChild(parent DebuggeeThreadId)` | `DebuggeeThreadId` | `DebuggeeError.unreadable` | The id of the thread `parent` started, read while `parent` is stopped at the `cloneEvent` that reported it. The kernel has traced that thread since its birth, before its own first stop is reported. |
| `threadState(process DebuggeeProcessId, thread DebuggeeThreadId)` | `TraceThreadState` | — | What `/proc` says of one thread of `process`. |
| `signalThread(process DebuggeeProcessId, thread DebuggeeThreadId, signal DebuggeeSignal)` | `bool` | — | Send `signal` to one thread. |
| `signalProcess(process DebuggeeProcessId, signal DebuggeeSignal)` | `bool` | — | Send `signal` to the process. |
| `signalWasSentByKill(thread DebuggeeThreadId)` | `bool` | — | True when the signal that stopped the thread came from `kill`. |
| `readMemory(process DebuggeeProcessId, address DebuggeeAddress, count DebuggeeByteCount)` | `ByteArray` | `DebuggeeError.unreadable` | `count` bytes of the debuggee's memory. |
| `writeCode(thread DebuggeeThreadId, address DebuggeeAddress, content ByteArray)` | — | `DebuggeeError.unwritable` | Write `content` a word at a time, keeping the bytes around it. |
| `registers(thread DebuggeeThreadId)` | `RegisterBlock` | `DebuggeeError.unreadable` | The thread's general registers, as the kernel lays them out for the architecture. |
| `setRegisters(thread DebuggeeThreadId, block RegisterBlock)` | — | `DebuggeeError.unwritable` | Set them from a block of that size. |
| `vectorRegisters(thread DebuggeeThreadId)` | `RegisterBlock` | `DebuggeeError.unreadable` | The thread's floating-point and vector registers. |

Three public constants name where the general-register block keeps the registers a frame walk starts from,
as byte offsets for the architecture the program is built for:

| Constant | arm64 | x64 |
|----------|-------|-----|
| `TraceFramePointerOffset` | 232 (`x29`) | 32 (`rbp`) |
| `TraceStackPointerOffset` | 248 (`sp`) | 152 (`rsp`) |
| `TraceProgramCounterOffset` | 256 (`pc`) | 128 (`rip`) |

| `TraceReport` case | Meaning |
|--------------------|---------|
| `nothing` | The budget passed with the debuggee silent. |
| `exited(thread, code)` | The thread exited with `code`. |
| `killed(thread, status)` | A signal ended the thread; `status` is the raw wait status. |
| `stopped(thread, signal, event)` | The thread stopped with `signal`; `event` is a `TraceEvent`: `signalStop`, `cloneEvent` (it started a thread) or `otherEvent`. |

`arrived()` is true for every case but `nothing`.

| `TraceThreadState` case | Meaning |
|-------------------------|---------|
| `absent` | `/proc` holds no record of the thread. |
| `present(code)` | The thread's record; `code` starts with its state letter. |

`hasLeft()` is true for `absent` and for a thread that is a zombie (`Z`) or dead (`X`).
`isRunning()` is true for state `R`, `isUninterruptible()` for state `D`, and `isTraceStopped()` for state `t`, a
thread stopped for its tracer.

| Type | Definition |
|------|------------|
| `DebuggeeAddress`, `DebuggeeThreadId`, `DebuggeeProcessId`, `TransferByteCount` | `int(0 to u64.max)` |
| `DebuggeeByteCount` | `int(0 to 0x40000000)` |
| `DebuggeeWaitMs` | `int(0 to 0xFFFFFFFF)` |
| `DebuggeeExitCode`, `RegisterWord` | `int(i64.min to i64.max)` |
| `DebuggeeFaultCode` | `int(0 to u32.max)` |
| `DebuggeeSignal` | `int(0 to 255)` |
| `RegisterOffset` | `int(0 to 0xFFFF)` |
| `Win64GprIndex`, `Win64XmmIndex` | `int(0 to 15)` |

## Sampler

A sampling profiler's view of another process, on every native target. `SampledProcess` stops a thread
briefly, copies its registers and the top of its stack, and lets it run on; `SampleTimer` paces the samples.
A thread is stopped through its thread handle on `x64-windows`, its Mach thread port on `arm64-macos`, and
`ptrace` on `arm64-linux` and `x64-linux`.

| `SampledProcess` member | Returns | Throws | Description |
|-------------------------|---------|--------|-------------|
| `SampledProcess.open(pid Pid)` | `SampledProcess` | `SamplerError.noSuchProcess`, `.noScratch`, `.accessDenied`, `.threadNotPinned` | Open process `pid` for sampling and reserve its scratch regions. `accessDenied`: macOS refused the process's task port, or Linux refused to trace it. `threadNotPinned`: on Linux the calling green thread could not be pinned to its machine, which `ptrace` needs because it answers only the OS thread that attached. On Linux, a main thread in uninterruptible sleep is waited on for up to 10 seconds and then taken as traceable. |
| `SampledProcess.cyclesAreNanoseconds()` | `bool` | — | True on macOS and Linux, where `cycles` and `suspendedCycles` are CPU nanoseconds; false on Windows, where they are the thread's cycle counter. |
| `SampledProcess.foreignCodeKeepsTheFrameChain()` | `bool` | — | True on macOS and Linux, where a frame-pointer walk may continue through code outside the sampled program; false on Windows. |
| `threads()` | `SampledThreadArray` | — | Open every thread the process has now. Each stays open until `release` or `close`. |
| `release(thread SampledThread)` | — | — | Close one thread this sampler opened. |
| `cycles(thread SampledThread)` | `SampleCycles` | `SamplerError.unknownThread` | CPU time the thread has consumed, in the unit `cyclesAreNanoseconds()` names. On macOS and Linux a running thread is stopped to read it, and the next `capture` of that thread answers from the same stop. |
| `capture(thread SampledThread)` | `SampleCapture` | `SamplerError.unknownThread`, `.callingThread` | Sample one thread. `unknownThread` refuses a thread `threads()` did not hand out or `release` let go; `callingThread` refuses the thread making the call, which would stop itself. On Linux a thread in uninterruptible sleep answers `unreadable`. |
| `modules()` | `SampledModuleArray` | `SamplerError.moduleListUnavailable` | Every module loaded in the process: the module snapshot on Windows and the dyld image list on macOS, each retried while the OS cannot answer yet. On Linux, whose images are static, an empty array while the process lives. |
| `close()` | — | — | Close every open thread, the process and the scratch regions. On Linux it detaches every thread it stopped, then unpins the calling green thread; call it on the green thread that opened the sampler. |
| `clone()` | — | — | Panics: a sampler owns one process's handles, so it is held in one place. |

On Linux a thread that cannot be detached within 10 seconds has its process killed rather than left
stopped, and stderr carries one line per process:
`sampler: thread <tid> of process <pid> could not be detached within 10000 ms, so the process was killed rather than left stopped`.

| `SampleCapture` case | Meaning |
|----------------------|---------|
| `threadGone` | The thread exited. |
| `unreadable(reservation, suspendedCycles)` | The thread was stopped, and its registers were unreadable. |
| `captured(stack)` | A `SampledStack`. |

A `SampledStack` has `programCounter`, `framePointer` and `stackPointer`; `reservation`, a
`StackReservation`; `suspendedCycles`, the thread's CPU time read while it was stopped; and `window`, a
`StackWindow` holding up to 64 KiB copied from the thread's stack. A `StackWindow` stays readable until the
sampler's next `capture`: `byteCount()` is the bytes copied, 0 once a later capture has replaced them, and
`word(offset StackWindowOffset)` is the word `offset` bytes in, throwing `SamplerError.outOfWindow` outside the
copy or after a later capture.

| `StackReservation` case | Meaning |
|-------------------------|---------|
| `base(address)` | The base of the reservation the stack lives in, unique to each live stack. Windows reports it. |
| `unreported` | The reservation is unknown, as on macOS and Linux. |

`SampledThread` has a public `id` (`SampledThreadId`). `SampledModule` has `base`, `limit`, `name` and
`fileType`. On Windows each UTF-16 unit above ASCII in the name reads as `?`, and `fileType` is 0; on macOS
the name is the last component of the image's path and `fileType` is its Mach-O header's file type (2 for
the executable).

| `SampleTimer` member | Returns | Throws | Description |
|----------------------|---------|--------|-------------|
| `SampleTimer.create(interval SampleIntervalNanos)` | `SampleTimer` | — | A periodic timer. On Windows its steps are rounded to 100 ns; elsewhere each wait sleeps to its deadline, rounded up to a microsecond. |
| `ready()` | `bool` | — | True when the host gave the timer `create` asked for; on Windows a refused timer reads false. |
| `wait()` | — | — | Sleep until the next tick: each deadline is the previous one plus the interval, so the samples keep their rate. Panics after `close()`, and on a timer that is not `ready()`. |
| `close()` | — | — | Release the timer. |

```maxon
enum SamplerError implements Error
	noSuchProcess
	noScratch
	unknownThread
	outOfWindow
	moduleListUnavailable
	callingThread
	accessDenied
	threadNotPinned
end 'SamplerError'
```

`SampleCycles`, `SampledThreadId` are `int(0 to u64.max)`; `SampleIntervalNanos` is `int(1 to u64.max)`;
`StackWindowOffset` is `int(0 to i64.max)`; `SampledThreadArray` and `SampledModuleArray` are arrays of
`SampledThread` and `SampledModule`. The module is declared under `#if not os(Wasi)`, so its names exist on
every native target.
