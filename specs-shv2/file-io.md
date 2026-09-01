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
end 'FileReadError'

enum FileWriteError implements Error
	failed
end 'FileWriteError'

enum FileDeleteError implements Error
	notFound
end 'FileDeleteError'

enum FileRenameError implements Error
	failed
end 'FileRenameError'
```

### File.readText

Read the entire contents of a text file as a UTF-8 encoded string.

**Signature:** `static function readText(path FilePath) returns String throws FileReadError`

**Parameters:**
- `path`: File path

**Returns:** File contents as a string

**Throws:** `FileReadError.notFound` if file cannot be read

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

**Throws:** `FileWriteError.failed` on failure

### File.readBinary

Read the entire contents of a file as raw bytes.

**Signature:** `static function readBinary(path FilePath) returns ByteArray throws FileReadError`

where `type ByteArray implements Array with Byte`

**Parameters:**
- `path`: File path

**Returns:** File contents as a byte array

**Throws:** `FileReadError.notFound` if file cannot be read

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

**Throws:** `FileWriteError.failed` on failure

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

**Throws:** `FileDeleteError.notFound` if the file cannot be deleted

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

⭐ **THIS SECTION IS THE HOME of the `<!-- targets: x64-windows, arm64-macos, arm64-linux -->` marker every `File.*` case in
this file, in `file-info.md`, and the one filesystem case in `bytearray-element-size.md` carries.**
Those cases point HERE rather than restating it, so the reason exists once and cannot drift into
fourteen versions of itself. It is `async-scheduler.md`'s Targets section applied to a SECOND
substrate, and it is deliberately not folded into that one: the green-thread gate is about a
hand-written context switch, this one is about file descriptors, and a marker that named the wrong
reason would be worse than none.

**IT IS A RUNTIME-SUBSTRATE GATE, AND THE COMPILER — NOT THE MARKER — IS WHAT DECIDES IT.**
`File.readText` / `writeText` / `readBinary` / `writeBinary` / `exists` / `delete` / `rename` / `info`
lower to the runtime entries `__mf_open_read`, `__mf_open_write`, `__mf_exists`, `__mf_delete`,
`__mf_rename` and `__mf_stat`, which are implemented for **x64-windows, arm64-macOS and arm64-Linux**.
The second lane landed at MAC4, over `open`/`creat`/`read`/`write`/`close`/`fstat`/`unlink`/`rename`; the
third took the same shape over `openat`/`read`/`write`/`close`/`fstat`/`unlinkat`/`renameat` raw
syscalls. Both put the errno→Win32 translation inside their own runtime, so the errno classification
above them is one graph for all three. `SemanticCheck.requireTargetSupportsCallee` refuses every
reachable one on the REMAINING lanes with **E3104**, naming the entry and the target:

```
error E3104: ...: this construct is x64-windows only at this rung: 'File.writeText' lowers to
the runtime entry '__mf_open_write', which has no x64-linux implementation
```

A pass elsewhere is not a thing that could be had, and the marker only spares the runner a compile
whose answer is already known.

⚠ **NOBODY HAD EVER RUN THESE CASES OFF-WINDOWS, AND A SURVEY CALLED THEM "byte-identically
portable" (N2 review, MEASURED).** They were unreachable until the rung that first loaded
`stdlib/File.maxon` landed — so the claim had never been tested, and the first cross-target run after
that read **15 failures on x64-linux and 14 on wasm32-wasi**, every one of them this gate.
A portability claim about code nothing has executed is a guess.

⚠ **IT IS NOT A PER-TARGET OPT-IN, AND THE TEST OF THAT IS WHAT CARRIES NO MARKER.** Anything
decided BEFORE lowering is target-neutral and runs everywhere. `bytearray-element-size`'s
`a-byte-two-files-disagree-about-is-two-types` asserts an **E3005** and was rewritten to reach it
without touching the filesystem rather than given a marker — a marker there would have been hiding a
green lane, not describing a red one.

⚠ **UN-GATE THE MOMENT A SECOND `__mf_*` SUBSTRATE LANDS. A stale gate is indistinguishable from a
real one — and this paragraph was collected once already.** It read *"What unblocks every case in this
file is one thing: POSIX (`open`/`read`/`write`/`unlink`/`rename`/`stat`) and WASI Preview2
implementations of those six entries."* The POSIX half exists (MAC4, arm64-macOS), and every marker in
this file, in `file-info.md` and in `bytearray-element-size.md`'s filesystem cases widened with it. What
is still owed is the WASI half, and the two Linux lanes — which are raw static images with no libc, so
each of the six is a syscall table rather than a re-spelling of the macOS work.

## Tests

<!-- test: read-text-file -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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

<!-- test: write-and-read-binary -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
