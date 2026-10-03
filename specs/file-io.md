---
feature: file-io
status: experimental
keywords: [file, io, read, write, text, binary]
category: stdlib
---

# File I/O

## Documentation

File I/O operations using the `File` type. All File methods take a `FilePath` parameter.

### Error Types

File operations use function-specific error types:

```maxon
enum FileReadError implements Error
	notFound
	accessDenied
	busy
	failed
end 'FileReadError'

enum FileWriteError implements Error
	failed
	notFound
	accessDenied
	alreadyExists
	busy
	partialFileLeft
end 'FileWriteError'

enum FileDeleteError implements Error
	notFound
	accessDenied
	busy
	failed
end 'FileDeleteError'

enum FileRenameError implements Error
	failed
	notFound
	accessDenied
	busy
end 'FileRenameError'

enum FileInfoError implements Error
	notFound
	accessDenied
	busy
	failed
end 'FileInfoError'

enum FileFailure
	notFound
	accessDenied
	alreadyExists
	busy
	failed
end 'FileFailure'
```

Each error type has a `failure()` method returning a `FileFailure`, so a caller can judge every `File`
error in one place. `busy` is a file another process holds open in a way that blocks the operation;
`partialFileLeft` is `File.createText`'s, and its `failure()` is `FileFailure.failed`.

### File.readText

Read the entire contents of a text file as a UTF-8 encoded string. It reads to the end of the file,
whatever size the file reports.

**Signature:** `static function readText(path FilePath) returns String throws FileReadError`

**Parameters:**
- `path`: File path

**Returns:** File contents as a string

**Throws:** `FileReadError` — `notFound`, `accessDenied`, `busy` or `failed`

**Example:**

```maxon
function main() returns ExitCode
	let content = try File.readText(FilePath from "example.txt") otherwise 'err'
		print("Could not read file\n")
		return 0
	end 'err'
	print("File content: {content}\n")
	return 1
end 'main'
```
```exitcode
0
```
```stdout
Could not read file
```

### File.writeText

Write a string to a text file using UTF-8 encoding.

**Signature:** `static function writeText(path FilePath, content String) throws FileWriteError`

**Parameters:**
- `path`: File path
- `content`: Text content to write

**Throws:** `FileWriteError` — `notFound`, `accessDenied`, `busy` or `failed`

### File.createText

Create a new file holding a string, UTF-8 encoded. The file must not exist.

**Signature:** `static function createText(path FilePath, content String) throws FileWriteError`

**Parameters:**
- `path`: File path
- `content`: Text content to write

**Throws:** `FileWriteError.alreadyExists` when the path already exists. A failed write deletes the file
it created and throws the write's error; when that delete fails too it throws
`FileWriteError.partialFileLeft`.

### File.readBinary

Read the entire contents of a file as raw bytes. It reads to the end of the file, whatever size the
file reports.

**Signature:** `static function readBinary(path FilePath) returns ByteArray throws FileReadError`

where `type ByteArray implements Array with Byte`

**Parameters:**
- `path`: File path

**Returns:** File contents as a byte array

**Throws:** `FileReadError` — `notFound`, `accessDenied`, `busy` or `failed`

**Example:**

```maxon
function main() returns ExitCode
	let bytes = try File.readBinary(FilePath from "data.bin") otherwise 'err'
		print("Could not read file\n")
		return 0
	end 'err'
	print("Read {bytes.count()} bytes\n")
	return 1
end 'main'
```

### File.writeBinary

Write binary data to a file.

**Signature:** `static function writeBinary(path FilePath, content ByteArray) throws FileWriteError`

where `type ByteArray implements Array with Byte`

**Parameters:**
- `path`: File path
- `content`: Binary data as a byte array

**Throws:** `FileWriteError` — `notFound`, `accessDenied`, `busy` or `failed`

### File.exists

Check if a file exists at the given path.

**Signature:** `static function exists(path FilePath) returns bool`

**Parameters:**
- `path`: File path

**Returns:** `true` if file exists, `false` otherwise

**Example:**

```maxon
function main() returns ExitCode
	if File.exists(FilePath from "temp/output.txt") 'check'
		print("File exists")
	end 'check' else 'nofile'
		print("File does not exist")
	end 'nofile'
	return 0
end 'main'
```

### File.delete

Delete a file at the given path.

**Signature:** `static function delete(path FilePath) throws FileDeleteError`

**Parameters:**
- `path`: File path

**Throws:** `FileDeleteError` — `notFound`, `accessDenied`, `busy` or `failed`

**Example:**

```maxon
function main() returns ExitCode
	try File.delete(FilePath from "temp/old_file.txt") otherwise 'err'
		print("Could not delete file")
		return 1
	end 'err'
	print("File deleted")
	return 0
end 'main'
```
```exitcode
1
```
```stdout
Could not delete file
```

## Targets — the one statement of the FILESYSTEM gate

<!-- unsupported-targets: wasm32-wasi -->
this file, in `file-info.md`, and the one filesystem case in `bytearray-element-size.md` carries.**
Those cases point HERE rather than restating it, so the reason exists once and cannot drift into
fourteen versions of itself. It is `async-scheduler.md`'s Targets section applied to a SECOND
substrate, and it is deliberately not folded into that one: the green-thread gate is about a
hand-written context switch, this one is about file descriptors, and a marker that named the wrong
reason would be worse than none.

**IT IS A RUNTIME-SUBSTRATE GATE, AND THE COMPILER — NOT THE MARKER — IS WHAT DECIDES IT.**
`File.readText` / `writeText` / `readBinary` / `writeBinary` / `exists` / `delete` / `rename` / `info`
lower to the runtime entries `__mf_open_read`, `__mf_open_write`, `__mf_exists`, `__mf_delete`,
`__mf_rename` and `__mf_stat`, which every lane but **wasm32-wasi** implements. The POSIX lanes go over
`open`/`creat`/`read`/`write`/`close`/`fstat`/`unlink`/`rename` — `openat`/`unlinkat`/`renameat` as raw
syscalls where there is no libc — and each puts the errno→Win32 translation inside its own runtime, so
the errno classification above them is one graph for all of them.
`SemanticCheck.requireTargetSupportsCallee` refuses every reachable one on wasm32-wasi with **E3104**,
naming the entry and the target:

```
error E3104: ...: 'File.writeText' lowers to the runtime entry '__mf_open_write', which has no
wasm32-wasi implementation
```

A pass elsewhere is not a thing that could be had, and the marker only spares the runner a compile
whose answer is already known.

⚠ **IT IS NOT A PER-TARGET OPT-IN, AND THE TEST OF THAT IS WHAT CARRIES NO MARKER.** Anything
decided BEFORE lowering is target-neutral and runs everywhere. `bytearray-element-size`'s
`a-byte-two-files-disagree-about-is-two-types` asserts an **E3005** and was rewritten to reach it
without touching the filesystem rather than given a marker — a marker there would have been hiding a
green lane, not describing a red one.

⚠ **UN-GATE THE MOMENT A WASI `__mf_*` SUBSTRATE LANDS. A stale gate is indistinguishable from a
real one.** What unblocks every case in this file on wasm32-wasi is one thing: WASI Preview2
implementations of the `__mf_*` entries.

## Tests

<!-- test: read-text-file -->
```maxon
function main() returns ExitCode
	// Try to read a nonexistent file - this tests the error path
	let content = try File.readText(FilePath from "nonexistent_file_xyz.txt") otherwise 'err'
		print("File not found")
		return 42
	end 'err'
	print("Content:{content}\n")
	return 0
end 'main'
```
```exitcode
42
```
```stdout
File not found
```

<!-- test: read-nonexistent-file -->
```maxon
function main() returns ExitCode
	let content = try File.readText(FilePath from "nonexistent.txt") otherwise 'err'
		print("File not found")
		return 0
	end 'err'
	print("Unexpected: {content}\n")
	return 1
end 'main'
```
```exitcode
0
```
```stdout
File not found
```

<!-- test: file-exists -->
```maxon
function main() returns ExitCode
	// Test File.exists on a nonexistent file (returns false)
	if File.exists(FilePath from "nonexistent_xyz_12345.txt") 'check'
		return 1
	end 'check'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: read-binary-nonexistent -->
```maxon
function main() returns ExitCode
	let bytes = try File.readBinary(FilePath from "nonexistent_binary_file.bin") otherwise 'err'
		print("File not found")
		return 42
	end 'err'
	print("Unexpected read: {bytes.count()} bytes")
	return 1
end 'main'
```
```exitcode
42
```
```stdout
File not found
```

<!-- test: write-and-read-text -->
```maxon
function main() returns ExitCode
	let path = FilePath from "test_readtext.txt"
	// Write a text file
	try File.writeText(path, content: "Hello World") otherwise 'write_err'
		print("Write failed")
		return 1
	end 'write_err'

	// Read it back with readText
	let content = try File.readText(path) otherwise 'read_err'
		print("Read failed")
		return 2
	end 'read_err'

	// Clean up
	try File.delete(path) otherwise 'del_err'
		print("Delete failed")
	end 'del_err'

	// Verify content
	print("{content}")
	if content.count() != 11 'len_check'
		print("\nWrong length: {content.count()}")
		return 3
	end 'len_check'
	return 42
end 'main'
```
```exitcode
42
```
```stdout
Hello World
```

<!-- test: create-text-of-an-existing-file-throws-already-exists -->
The refusal is `FileWriteError.alreadyExists`, which is what tells a caller that somebody else made the file
from every other reason a create can fail.
```maxon
function main() returns ExitCode
	let path = FilePath from "test_create_text_exists.txt"

	try File.createText(path, content: "first") otherwise 'createFailed'
		return 1
	end 'createFailed'

	var answer = 2 as ExitCode

	try File.createText(path, content: "second") otherwise (e) 'refused'
		answer = 42 if e == FileWriteError.alreadyExists else 3
	end 'refused'

	try File.delete(path) otherwise return 4
	return answer
end 'main'
```
```exitcode
42
```

<!-- test: write-text-over-the-running-executable-throws-busy -->
<!-- unsupported-targets: arm64-macos, wasm32-wasi -->
A file a running program's image keeps from being written — which Windows refuses with a sharing violation and
Linux with `ETXTBSY` — is `FileWriteError.busy`, distinct from a permission refusal. The program writes over a COPY
of itself that it keeps running, so a host that lets the write through truncates only the copy. macOS lets it
through, so it does not run there.
```maxon
#if os(Windows)
	let CopyDeleteAttempts = 40
#else
	let CopyDeleteAttempts = 1
#endif

let CopyDeleteRetryMs = 50

function main() returns ExitCode
	if CommandLine.args().count() > 1 'theRunningCopy'
		print("running\n")
		var input = Console.stdin()
		_ = try input.readLine() otherwise return 0
		return 0
	end 'theRunningCopy'

	let executable = try Process.executablePath() otherwise return 1
	let image = try File.readBinary(executable) otherwise return 2
	let copy = (FilePath from "test_write_over_a_running_copy.exe").resolve(Directory.currentPath())

	try File.writeBinary(copy, content: image, mode: FilePermission.executable) otherwise 'unwritten'
		return 3 if deleteTheCopy(copy) else 7
	end 'unwritten'

	let verdict = verdictOverARunningCopy(copy)

	if not deleteTheCopy(copy) 'copyLeftBehind'
		return 7
	end 'copyLeftBehind'

	return verdict
end 'main'

function verdictOverARunningCopy(copy FilePath) returns ExitCode
	var argv = StringArray.create()
	argv.push("hold")
	var running = try StreamingSubprocess.spawn(Executable.path(copy), arguments: argv) otherwise return 4
	let verdict = verdictWhileItRuns(running, copy: copy)
	running.closeStdin()
	let ended = endedCleanly(running)
	running.release()
	return verdict if ended else 6
end 'verdictOverARunningCopy'

function verdictWhileItRuns(running StreamingSubprocess, copy FilePath) returns ExitCode
	_ = try running.readStdoutLine() otherwise return 5

	try File.writeText(copy, content: "") otherwise (e) 'refused'
		return 42 if e == FileWriteError.busy else (10 + e.ordinal) as ExitCode
	end 'refused'

	return 2
end 'verdictWhileItRuns'

function endedCleanly(running StreamingSubprocess) returns bool
	_ = try running.wait() otherwise return false
	return true
end 'endedCleanly'

function deleteTheCopy(copy FilePath) returns bool
	for attempt in 1 to CopyDeleteAttempts 'eachAttempt'
		try File.delete(copy) otherwise (e) 'refused'
			if e == FileDeleteError.notFound 'nothingToDelete'
				return true
			end 'nothingToDelete'

			if attempt < CopyDeleteAttempts 'anotherAttemptFollows'
				sleep(CopyDeleteRetryMs)
			end 'anotherAttemptFollows'

			continue
		end 'refused'

		return true
	end 'eachAttempt'

	return false
end 'deleteTheCopy'
```
```exitcode
42
```

<!-- test: read-text-of-a-missing-file-throws-not-found -->
A file that is not there is `FileReadError.notFound`, distinct from a read that failed for another reason.
```maxon
function main() returns ExitCode
	let text = try File.readText(FilePath from "test_missing_read_text.txt") otherwise (e) 'unread'
		return 42 if e == FileReadError.notFound else 1
	end 'unread'

	return text.count() as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: create-text-refuses-an-existing-file -->
`File.createText` writes a file that does not exist yet, and refuses one that does without touching it.
```maxon
function main() returns ExitCode
	let path = FilePath from "test_create_text.txt"

	try File.createText(path, content: "first") otherwise 'createFailed'
		return 1
	end 'createFailed'

	var refused = false

	try File.createText(path, content: "second") otherwise 'secondRefused'
		refused = true
	end 'secondRefused'

	let content = try File.readText(path) otherwise 'readFailed'
		return 2
	end 'readFailed'

	try File.delete(path) otherwise 'deleteFailed'
		return 3
	end 'deleteFailed'

	print(content)

	if refused 'refusedTheSecond'
		return 42
	end 'refusedTheSecond'

	return 4
end 'main'
```
```exitcode
42
```
```stdout
first
```

<!-- test: read-text-of-a-file-that-reports-no-size -->
<!-- unsupported-targets: x64-windows, arm64-macos, wasm32-wasi -->
A Linux `/proc` file reports a size of 0 and still holds text, so `File.readText` reads to the end of the file
rather than stopping at the size it was told. `/proc/self/smaps` is longer than one 4096-byte read, so the
whole file arrives only when the read continues past the first chunk: its last line is the last mapping's
`VmFlags:` line.
```maxon
let OneReadChunkBytes = 4096

function main() returns ExitCode
	let path = FilePath from "/proc/self/smaps"
	let text = try File.readText(path) otherwise 'readFailed'
		return 1
	end 'readFailed'

	if text.byteLength() <= OneReadChunkBytes 'withinOneChunk'
		return 2
	end 'withinOneChunk'

	let lines = text.trimEnd().split("\n")
	let last = try lines.get(lines.count() - 1) otherwise return 3

	if last.startsWith("VmFlags:") 'theWholeFile'
		return 42
	end 'theWholeFile'

	return 4
end 'main'
```
```exitcode
42
```

<!-- test: write-and-read-binary -->
```maxon

function main() returns ExitCode
	let path = FilePath from "test_binary.bin"
	// Create a byte array with known values
	var data = ByteArray.create()
	data.push(65 as Byte)
	data.push(66 as Byte)
	data.push(67 as Byte)

	// Write binary file
	try File.writeBinary(path, content: data) otherwise 'write_err'
		print("Write failed")
		return 1
	end 'write_err'

	// Read it back
	let readData = try File.readBinary(path) otherwise 'read_err'
		print("Read failed")
		return 2
	end 'read_err'

	// Clean up the temp file
	try File.delete(path) otherwise 'del_err'
		print("Delete failed")
	end 'del_err'

	// Verify count
	if readData.count() != 3 'count_check'
		print("Wrong count: {readData.count()}")
		return 3
	end 'count_check'

	// Verify first value
	let b0 = try readData.get(0) otherwise 'e0'
		return 10
	end 'e0'

	if b0 != 65 as Byte 'check0'
		print("Wrong value")
		return 20
	end 'check0'

	print("Binary read/write OK")
	return 42
end 'main'
```
```exitcode
42
```
```stdout
Binary read/write OK
```

### File.rename

Atomically rename (move) a file, replacing the destination if it already
exists. Backed by `MoveFileEx` (Windows), `rename(2)` (POSIX), and
`descriptor.rename-at` (WASI).

**Signature:** `static function rename(from FilePath, to FilePath) throws FileRenameError`

<!-- test: write-rename-and-read -->
```maxon

function main() returns ExitCode
	let src = FilePath from "test_rename_src.bin"
	let dst = FilePath from "test_rename_dst.bin"

	var data = ByteArray.create()
	data.push(70 as Byte)
	data.push(71 as Byte)

	try File.writeBinary(src, content: data) otherwise 'write_err'
		print("Write failed")
		return 1
	end 'write_err'

	// Rename src -> dst.
	try File.rename(src, to: dst) otherwise 'rename_err'
		print("Rename failed")
		return 2
	end 'rename_err'

	// Source is gone, destination carries the bytes.
	if File.exists(src) 'src_remains'
		print("Source still exists")
		return 3
	end 'src_remains'

	let readData = try File.readBinary(dst) otherwise 'read_err'
		print("Read failed")
		return 4
	end 'read_err'

	if readData.count() != 2 'count_check'
		print("Wrong count: {readData.count()}")
		return 5
	end 'count_check'

	// Rename over an existing destination replaces it.
	try File.writeBinary(src, content: data) otherwise 'rewrite_err'
		print("Rewrite failed")
		return 6
	end 'rewrite_err'

	try File.rename(src, to: dst) otherwise 'replace_err'
		print("Replace rename failed")
		return 7
	end 'replace_err'

	try File.delete(dst) otherwise 'del_err'
		print("Delete failed")
	end 'del_err'

	print("Rename OK")
	return 42
end 'main'
```
```exitcode
42
```
```stdout
Rename OK
```

<!-- test: rename-of-a-missing-file-throws-not-found -->
A source that is not there is `FileRenameError.notFound`, distinct from a rename that failed for another reason.
```maxon
function main() returns ExitCode
	let src = FilePath from "test_rename_missing_src.bin"
	let dst = FilePath from "test_rename_missing_dst.bin"

	try File.rename(src, to: dst) otherwise (e) 'unmoved'
		return 42 if e == FileRenameError.notFound else (10 + e.ordinal) as ExitCode
	end 'unmoved'

	return 1
end 'main'
```
```exitcode
42
```
