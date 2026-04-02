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

- `__ManagedFile.openRead(managed)` — Opens a file for reading. Returns a `__ManagedFile` pointer or -1 on failure.
- `__ManagedFile.openWrite(managed)` — Opens a file for writing (creates or overwrites). Returns a `__ManagedFile` pointer or -1 on failure.
- `__ManagedFile.exists(managed)` — Returns 1 if the file exists (and is not a directory), 0 otherwise.
- `__ManagedFile.delete(managed)` — Deletes a file. Returns 0 on success, non-zero on failure.

### Instance Methods

Instance methods are called on variables declared with type `__ManagedFile`:

- `size()` — Returns the file size in bytes.
- `read(managed, size)` — Reads bytes from the file into managed memory, clamped to capacity. Returns bytes read.
- `write(managed)` — Writes managed memory buffer contents to the file. Returns bytes written or -1 on failure.
- `close()` — Explicitly closes the file handle. Idempotent. Also called automatically via destructor.

### Usage Pattern

`__ManagedFile` is used as a struct field inside wrapper types (like `File`):

```text
type FileWrapper
  export var file __ManagedFile

  static function open(path String) returns FileWrapper
    let result = __ManagedFile.openRead(path.managed)
    return FileWrapper{_file: result}
  end

  function size() returns int
    return _file.size()
  end
end
```

## Tests

<!-- test: managed-file.open-read-nonexistent -->
```maxon
function main() returns ExitCode
	let result = __ManagedFile.openRead("nonexistent_file_xyz_98765.txt".managed)
	if result == -1 'notFound'
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
type TestFile
	export var file __ManagedFile
end 'TestFile'

function main() returns ExitCode
	let path = "test_managed_file_rw.txt"
	// Write a file
	let writeResult = __ManagedFile.openWrite(path.managed)
	if writeResult == -1 'writeFail'
		print("write open failed")
		return 1
	end 'writeFail'
	var wf = TestFile{_file: writeResult}
	let content = "Hello Managed"
	let written = wf._file.write(content.managed)
	wf._file.close()
	if written < 0 'writeErr'
		return 3
	end 'writeErr'

	// Read it back
	let readResult = __ManagedFile.openRead(path.managed)
	if readResult == -1 'readFail'
		print("read open failed")
		return 2
	end 'readFail'
	var rf = TestFile{_file: readResult}
	let size = rf._file.size()
	var buffer = __ManagedMemory.create(size + 1, 1)
	let bytesRead = rf._file.read(buffer, size)
	rf._file.close()
	buffer.setLength(bytesRead)
	// Null-terminate
	buffer.setLength(bytesRead + 1)
	buffer.setByte(bytesRead, 0)
	buffer.setLength(bytesRead)
	let readContent = String{managed: buffer, _iterPos: 0}
	print("{readContent}")

	// Clean up
	let delResult = __ManagedFile.delete(path.managed)
	if delResult != 0 'delErr'
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
type TestFile
	export var file __ManagedFile
end 'TestFile'

function createEmptyFile(path String)
	let result = __ManagedFile.openWrite(path.managed)
	var f = TestFile{_file: result}
	f._file.close()
end 'createEmptyFile'

function main() returns ExitCode
	// Non-existent file
	let e1 = __ManagedFile.exists("nonexistent_xyz_managed_12345.txt".managed)
	if e1 != 0 'check1'
		return 1
	end 'check1'

	// Create a file, check exists, delete it
	let path = "test_managed_exists.txt"
	createEmptyFile(path)
	let e2 = __ManagedFile.exists(path.managed)
	if e2 != 1 'check2'
		return 2
	end 'check2'
	let delResult = __ManagedFile.delete(path.managed)
	if delResult != 0 'delErr'
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
	let result = __ManagedFile.delete("nonexistent_delete_xyz.txt".managed)
	if result != 0 'checkFail'
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
type TestFile
	export var file __ManagedFile
end 'TestFile'

function writeFile(path String)
	let result = __ManagedFile.openWrite(path.managed)
	var wf = TestFile{_file: result}
	let written = wf._file.write("auto".managed)
	if written < 0 'writeErr'
		panic("write failed")
	end 'writeErr'
	// wf goes out of scope here, destructor closes handle
end 'writeFile'

function main() returns ExitCode
	let path = "test_managed_autoclose.txt"
	writeFile(path)

	// Verify we can read it (file was properly closed by destructor)
	let readResult = __ManagedFile.openRead(path.managed)
	if readResult == -1 'readFail'
		print("read failed")
		return 1
	end 'readFail'
	var rf = TestFile{_file: readResult}
	let size = rf._file.size()
	rf._file.close()
	let delResult = __ManagedFile.delete(path.managed)
	if delResult != 0 'delErr'
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
