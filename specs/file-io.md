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
