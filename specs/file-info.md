---
feature: file-info
status: experimental
keywords: [file, info, metadata, size, timestamp, modified, created]
category: stdlib
---

# File Info

## Documentation

Retrieve metadata about a file using `File.info()`. Returns a `FileInfo` struct containing the file's size, timestamps, and attributes — all obtained with a single OS call.

### FileInfo Type

```text
type FileInfo
  export let size as FileSize              // file size in bytes
  export let modifiedTime as Timestamp     // last modification time (Unix epoch seconds)
  export let createdTime as Timestamp      // creation time (Unix epoch seconds)
  export let accessedTime as Timestamp     // last access time (Unix epoch seconds)
  export let isDirectory as bool           // true if path is a directory
  export let isReadOnly as bool            // true if file is read-only
end 'FileInfo'
```

### File.info

**Signature:** `static function info(path FilePath) returns FileInfo throws FileInfoError`

**Parameters:**
- `path`: File path to query

**Returns:** A `FileInfo` struct with file metadata

**Throws:** `FileInfoError.notFound` if the file does not exist

**Example:**

```text
let fi = try File.info(FilePath from "data.txt") otherwise 'e'
  print("file not found")
  return 1
end 'e'
print("size: {fi.size}, modified: {fi.modifiedTime}")
```

## Tests

<!-- test: file-info.basic-size -->
```maxon
function main() returns ExitCode
	let path = FilePath from "test_fi_basic.txt"
	try File.writeText(path, content: "ABCDE") otherwise 'w'
		return 1
	end 'w'
	let fi = try File.info(path) otherwise 'e'
		try File.delete(path) otherwise ignore
		return 2
	end 'e'
	try File.delete(path) otherwise ignore
	if fi.size != 5 'check'
		print("wrong size: {fi.size}")
		return 3
	end 'check'
	print("size={fi.size}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
size=5
```

<!-- test: file-info.timestamps -->
```maxon
function main() returns ExitCode
	let path = FilePath from "test_fi_times.txt"
	try File.writeText(path, content: "time test") otherwise 'w'
		return 1
	end 'w'
	let fi = try File.info(path) otherwise 'e'
		try File.delete(path) otherwise ignore
		return 2
	end 'e'
	try File.delete(path) otherwise ignore
	// Timestamps should be reasonable Unix epoch values (after year 2020 = 1577836800)
	if fi.modifiedTime < 1577836800 'mt'
		print("modifiedTime too small: {fi.modifiedTime}")
		return 3
	end 'mt'
	if fi.createdTime < 1577836800 'ct'
		print("createdTime too small: {fi.createdTime}")
		return 4
	end 'ct'
	if fi.accessedTime < 1577836800 'at'
		print("accessedTime too small: {fi.accessedTime}")
		return 5
	end 'at'
	print("timestamps ok")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
timestamps ok
```

<!-- test: file-info.not-found -->
```maxon
function main() returns ExitCode
	let fi = try File.info(FilePath from "nonexistent_fi_xyz.txt") otherwise 'e'
		print("not found")
		return 42
	end 'e'
	print("unexpected: size={fi.size}")
	return 0
end 'main'
```
```exitcode
42
```
```stdout
not found
```

<!-- test: file-info.directory -->
```maxon
function main() returns ExitCode
	let path = FilePath from "test_fi_dir"
	// Create directory (may already exist from prior run — ignore result)
	_ = Directory.create(path)
	let fi = try File.info(path) otherwise 'e'
		print("info failed")
		return 2
	end 'e'
	if fi.isDirectory == false 'dc'
		print("expected directory")
		return 3
	end 'dc'
	print("is directory")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
is directory
```

<!-- test: file-info.read-only -->
```maxon
function main() returns ExitCode
	let path = FilePath from "test_fi_rw.txt"
	try File.writeText(path, content: "rw test") otherwise 'w'
		return 1
	end 'w'
	let fi = try File.info(path) otherwise 'e'
		try File.delete(path) otherwise ignore
		return 2
	end 'e'
	try File.delete(path) otherwise ignore
	// A freshly written file should not be read-only
	if fi.isReadOnly 'ro'
		print("unexpected read-only")
		return 3
	end 'ro'
	print("not read-only")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
not read-only
```

<!-- test: file-info.empty-file -->
```maxon
function main() returns ExitCode
	let path = FilePath from "test_fi_empty.txt"
	try File.writeText(path, content: "") otherwise 'w'
		return 1
	end 'w'
	let fi = try File.info(path) otherwise 'e'
		try File.delete(path) otherwise ignore
		return 2
	end 'e'
	try File.delete(path) otherwise ignore
	if fi.size != 0 'check'
		print("wrong size: {fi.size}")
		return 3
	end 'check'
	print("empty size=0")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
empty size=0
```
