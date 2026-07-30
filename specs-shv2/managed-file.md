---
feature: managed-file
status: experimental
keywords: [file, managed, __ManagedFile, RAII, handle]
category: type-system
---

# __ManagedFile

## Documentation

### Overview

`__ManagedFile` is a compiler builtin type that wraps a Windows file HANDLE with automatic cleanup via a destructor when the last reference goes out of scope. It replaces the raw `__Builtins` file functions with a managed, RAII-based API.

### Type Structure

`__ManagedFile` has a single field:
- `_handle` (int) — The raw Windows file HANDLE

### Static Methods

- `__ManagedFile.openRead(managed)` — Opens a file for reading. Throws `__ManagedFileError` on failure (notFound / accessDenied / openFailed).
- `__ManagedFile.openWrite(managed)` — Opens a file for writing (creates or overwrites). Throws `__ManagedFileError` on failure.
- `__ManagedFile.openWriteExecutable(managed)` — As openWrite, with 0755 on Unix. Throws on failure.
- `__ManagedFile.exists(managed)` — Returns 1 if the file exists (and is not a directory), 0 otherwise. Does not throw.
- `__ManagedFile.delete(managed)` — Deletes a file. Throws `__ManagedFileError` on failure.
- `__ManagedFile.stat(managed)` — Returns a raw stat buffer pointer. Throws on failure.

### Instance Methods

Instance methods are called on variables declared with type `__ManagedFile`:

- `size()` — Returns the file size in bytes. Throws on failure.
- `read(managed, size)` — Reads up to `size` bytes from the file into managed memory. Throws `readFailed` if `size > managed.capacity` or on I/O error.
- `write(managed)` — Writes managed memory buffer contents to the file. Returns bytes written. Throws on failure.
- `close()` — Explicitly closes the file handle. Idempotent. Also called automatically via destructor. Does not throw.

### Usage Pattern

`__ManagedFile` is used as a struct field inside wrapper types (like `File`):

```text
type FileWrapper
  export var file as __ManagedFile

  static function open(path String) returns FileWrapper throws FileError
    let result = try __ManagedFile.openRead(path.toByteArray().managed) otherwise throw FileError.notFound
    return FileWrapper{_file: result}
  end

  function size() returns int throws FileError
    return try _file.size() otherwise throw FileError.notFound
  end
end
```

## Tests

<!-- test: managed-file.open-read-nonexistent -->
<!-- targets: x64-windows -->
```maxon
function main() returns ExitCode
	try __ManagedFile.openRead("nonexistent_file_xyz_98765.txt".toByteArray().managed) otherwise 'notFound'
		print("not found")
		return 42
	end 'notFound'
	return 0
end 'main'
```
```exitcode
42
```
```stdout
not found
```

<!-- test: managed-file.write-and-read -->
<!-- targets: x64-windows -->
```maxon
export enum TestFileError implements Error
	openFailed
end 'TestFileError'

type TestFile
	export var file as __ManagedFile

	export static function openWrite(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openWrite(path) otherwise 'fail'
			throw TestFileError.openFailed
		end 'fail'
		return TestFile{file: handle}
	end 'openWrite'

	export static function openRead(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openRead(path) otherwise 'fail'
			throw TestFileError.openFailed
		end 'fail'
		return TestFile{file: handle}
	end 'openRead'
end 'TestFile'

function main() returns ExitCode
	let path = "test_managed_file_rw.txt"
	// Write a file
	var wf = try TestFile.openWrite(path.toByteArray().managed) otherwise 'writeFail'
		print("write open failed")
		return 1
	end 'writeFail'
	let content = "Hello Managed"
	try wf.file.write(content.toByteArray().managed) otherwise 'wErr'
		wf.file.close()
		return 3
	end 'wErr'
	wf.file.close()

	// Read it back
	var rf = try TestFile.openRead(path.toByteArray().managed) otherwise 'readFail'
		print("read open failed")
		return 2
	end 'readFail'
	let size = try rf.file.size() otherwise 'sizeErr'
		return 8
	end 'sizeErr'
	var buffer = try __ManagedMemory.create(size + 1, 1) otherwise 'allocFail'
		return 5
	end 'allocFail'
	let bytesRead = try rf.file.read(buffer, size) otherwise 'rErr'
		rf.file.close()
		return 9
	end 'rErr'
	rf.file.close()
	try buffer.setLength(bytesRead) otherwise 'setLenFail'
		return 6
	end 'setLenFail'
	// Null-terminate
	try buffer.setLength(bytesRead + 1) otherwise 'setLenFail2'
		return 6
	end 'setLenFail2'
	try buffer.setByte(bytesRead, 0) otherwise 'setByteFail'
		return 7
	end 'setByteFail'
	try buffer.setLength(bytesRead) otherwise 'setLenFail3'
		return 6
	end 'setLenFail3'
	let readContent = String.init(buffer)
	print("{readContent}")

	// Clean up
	try __ManagedFile.delete(path.toByteArray().managed) otherwise 'delErr'
		return 4
	end 'delErr'

	return 42
end 'main'
```
```exitcode
42
```
```stdout
Hello Managed
```

<!-- test: managed-file.exists -->
<!-- targets: x64-windows -->
```maxon
export enum TestFileError implements Error
	openFailed
end 'TestFileError'

type TestFile
	export var file as __ManagedFile

	export static function openWrite(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openWrite(path) otherwise 'fail'
			throw TestFileError.openFailed
		end 'fail'
		return TestFile{file: handle}
	end 'openWrite'
end 'TestFile'

function createEmptyFile(path String) throws TestFileError
	var f = try TestFile.openWrite(path.toByteArray().managed)
	f.file.close()
end 'createEmptyFile'

function main() returns ExitCode
	// Non-existent file
	let e1 = __ManagedFile.exists("nonexistent_xyz_managed_12345.txt".toByteArray().managed)
	if e1 != 0 'check1'
		return 1
	end 'check1'

	// Create a file, check exists, delete it
	let path = "test_managed_exists.txt"
	try createEmptyFile(path) otherwise 'createFail'
		return 10
	end 'createFail'
	let e2 = __ManagedFile.exists(path.toByteArray().managed)
	if e2 != 1 'check2'
		return 2
	end 'check2'
	try __ManagedFile.delete(path.toByteArray().managed) otherwise 'delErr'
		return 4
	end 'delErr'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: managed-file.delete-nonexistent -->
<!-- targets: x64-windows -->
```maxon
function main() returns ExitCode
	try __ManagedFile.delete("nonexistent_delete_xyz.txt".toByteArray().managed) otherwise 'checkFail'
		print("delete failed as expected")
		return 42
	end 'checkFail'
	return 0
end 'main'
```
```exitcode
42
```
```stdout
delete failed as expected
```

<!-- test: managed-file.auto-close -->
<!-- targets: x64-windows -->
```maxon
export enum TestFileError implements Error
	openFailed
end 'TestFileError'

type TestFile
	export var file as __ManagedFile

	export static function openWrite(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openWrite(path) otherwise 'fail'
			throw TestFileError.openFailed
		end 'fail'
		return TestFile{file: handle}
	end 'openWrite'

	export static function openRead(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openRead(path) otherwise 'fail'
			throw TestFileError.openFailed
		end 'fail'
		return TestFile{file: handle}
	end 'openRead'
end 'TestFile'

function writeFile(path String)
	let wf = try TestFile.openWrite(path.toByteArray().managed) otherwise panic("write open failed")
	try wf.file.write("auto".toByteArray().managed) otherwise panic("write failed")
	// wf goes out of scope here, destructor closes handle
end 'writeFile'

function main() returns ExitCode
	let path = "test_managed_autoclose.txt"
	writeFile(path)

	// Verify we can read it (file was properly closed by destructor)
	var rf = try TestFile.openRead(path.toByteArray().managed) otherwise 'readFail'
		print("read failed")
		return 1
	end 'readFail'
	let size = try rf.file.size() otherwise 'sizeErr'
		return 3
	end 'sizeErr'
	rf.file.close()
	try __ManagedFile.delete(path.toByteArray().managed) otherwise 'delErr'
		return 2
	end 'delErr'
	if size == 4 'sizeOk'
		return 42
	end 'sizeOk'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: managed-file.open-read-not-found-variant -->
<!-- targets: x64-windows -->

The errno→variant mapping ensures that opening a path that does not exist
routes to the `notFound` arm (rather than the catch-all `openFailed`).
Backed by `gt->io_error_code` populated by the runtime sync worker
(Win32 ERROR_FILE_NOT_FOUND=2 / POSIX ENOENT=2).

```maxon
function main() returns ExitCode
	var result = 0
	try __ManagedFile.openRead("nonexistent_phaseB_open_xyz.txt".toByteArray().managed) otherwise (e) 'h'
		match e 'k'
			notFound then result = 42
			default panic("expected notFound")
		end 'k'
	end 'h'
	return result
end 'main'
```
```exitcode
42
```

<!-- test: managed-file.delete-not-found-variant -->
<!-- targets: x64-windows -->
```maxon
function main() returns ExitCode
	var result = 0
	try __ManagedFile.delete("nonexistent_phaseB_delete_xyz.txt".toByteArray().managed) otherwise (e) 'h'
		match e 'k'
			notFound then result = 42
			default panic("expected notFound")
		end 'k'
	end 'h'
	return result
end 'main'
```
```exitcode
42
```

<!-- test: managed-file.stat-not-found-variant -->
<!-- targets: x64-windows -->
```maxon
function main() returns ExitCode
	var result = 0
	try __ManagedFile.stat("nonexistent_phaseB_stat_xyz.txt".toByteArray().managed) otherwise (e) 'h'
		match e 'k'
			notFound then result = 42
			default panic("expected notFound")
		end 'k'
	end 'h'
	return result
end 'main'
```
```exitcode
42
```

<!-- test: managed-file.error-direct-construction -->
<!-- targets: x64-windows -->
```maxon
function main() returns ExitCode
	let f = __ManagedFile{_handle: 0}
	return 0
end 'main'
```
```maxoncstderr
error E3072: specs/fragments/managed-file/managed-file.error-direct-construction.test:3:24: '__ManagedFile' is a compiler builtin type and cannot be constructed directly
```

<!-- test: managed-file.unknown-instance-method-is-refused-by-name -->
<!-- targets: x64-windows -->
shv2-authored, and it pins the DISPATCHER rather than a behaviour the oracle defines.

The instance surface is exactly four methods (`size`/`read`/`write`/`close`), so anything else is refused
BY NAME. Left to fall through it would mangle into a callee no file declares and surface as `E3004`
against a method the author did write — the same argument `parseStringStaticCall` makes for `String`'s
statics.

⚠ The probe is `seek`, and that choice is the point rather than an arbitrary misspelling: v1 has an
`lseek` primitive, but it belongs to a different v1 abstraction and NEITHER reference's `__ManagedFile`
method list carries one, so `seek` is a name the language genuinely does not have and will not acquire by
a later rung finishing this surface. (This case used to probe `size`, which R4.2 delivers — a marker that
went stale the moment the rung it was written against landed.)

⚠ It is a COMPILE-TIME case on purpose: it needs no file to exist in the runner's working directory. The
RUNTIME half of instance dispatch is pinned by `managed-file.write-and-read` and `managed-file.auto-close`,
which reach `write`/`size` through a struct FIELD — the spelling the documented usage pattern actually
uses.
```maxon
function main() returns ExitCode
	let f = try __ManagedFile.openRead("nonexistent_unknown_method_xyz.txt".toByteArray().managed) otherwise 'nf'
		return 1
	end 'nf'
	let n = f.seek(0)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:12: Unsupported: `__ManagedFile` method 'seek' — the type has exactly the four both references declare: `size()`, `read(managed, size)`, `write(managed)` and `close()`
```
