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
    let result = try __ManagedFile.openRead(path.managed) otherwise throw FileError.notFound
    return FileWrapper{_file: result}
  end

  function size() returns int throws FileError
    return try _file.size() otherwise throw FileError.notFound
  end
end
```

## Tests

<!-- test: managed-file.open-read-nonexistent -->
```maxon
function main() returns ExitCode
	try __ManagedFile.openRead("nonexistent_file_xyz_98765.txt".managed) otherwise 'notFound'
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
	var wf = try TestFile.openWrite(path.managed) otherwise 'writeFail'
		print("write open failed")
		return 1
	end 'writeFail'
	let content = "Hello Managed"
	try wf.file.write(content.managed) otherwise 'wErr'
		wf.file.close()
		return 3
	end 'wErr'
	wf.file.close()

	// Read it back
	var rf = try TestFile.openRead(path.managed) otherwise 'readFail'
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
	try __ManagedFile.delete(path.managed) otherwise 'delErr'
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
	var f = try TestFile.openWrite(path.managed)
	f.file.close()
end 'createEmptyFile'

function main() returns ExitCode
	// Non-existent file
	let e1 = __ManagedFile.exists("nonexistent_xyz_managed_12345.txt".managed)
	if e1 != 0 'check1'
		return 1
	end 'check1'

	// Create a file, check exists, delete it
	let path = "test_managed_exists.txt"
	try createEmptyFile(path) otherwise 'createFail'
		return 10
	end 'createFail'
	let e2 = __ManagedFile.exists(path.managed)
	if e2 != 1 'check2'
		return 2
	end 'check2'
	try __ManagedFile.delete(path.managed) otherwise 'delErr'
		return 4
	end 'delErr'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: managed-file.delete-nonexistent -->
```maxon
function main() returns ExitCode
	try __ManagedFile.delete("nonexistent_delete_xyz.txt".managed) otherwise 'checkFail'
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
	let wf = try TestFile.openWrite(path.managed) otherwise panic("write open failed")
	try wf.file.write("auto".managed) otherwise panic("write failed")
	// wf goes out of scope here, destructor closes handle
end 'writeFile'

function main() returns ExitCode
	let path = "test_managed_autoclose.txt"
	writeFile(path)

	// Verify we can read it (file was properly closed by destructor)
	var rf = try TestFile.openRead(path.managed) otherwise 'readFail'
		print("read failed")
		return 1
	end 'readFail'
	let size = try rf.file.size() otherwise 'sizeErr'
		return 3
	end 'sizeErr'
	rf.file.close()
	try __ManagedFile.delete(path.managed) otherwise 'delErr'
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

The errno→variant mapping ensures that opening a path that does not exist
routes to the `notFound` arm (rather than the catch-all `openFailed`).
Backed by `gt->io_error_code` populated by the runtime sync worker
(Win32 ERROR_FILE_NOT_FOUND=2 / POSIX ENOENT=2).

```maxon
function main() returns ExitCode
	var result = 0
	try __ManagedFile.openRead("nonexistent_phaseB_open_xyz.txt".managed) otherwise (e) 'h'
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
```maxon
function main() returns ExitCode
	var result = 0
	try __ManagedFile.delete("nonexistent_phaseB_delete_xyz.txt".managed) otherwise (e) 'h'
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
```maxon
function main() returns ExitCode
	var result = 0
	try __ManagedFile.stat("nonexistent_phaseB_stat_xyz.txt".managed) otherwise (e) 'h'
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
```maxon
function main() returns ExitCode
	let f = __ManagedFile{_handle: 0}
	return 0
end 'main'
```
```maxoncstderr
error E3072: specs/fragments/managed-file/managed-file.error-direct-construction.test:3:24: '__ManagedFile' is a compiler builtin type and cannot be constructed directly
```
