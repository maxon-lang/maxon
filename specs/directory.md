---
feature: directory
status: stable
keywords: [directory, folder, list, filesystem]
category: stdlib
---

# Directory Operations

## Documentation

Directory operations using the `Directory` type. All Directory methods take a `FilePath` parameter.

### Error Types

Directory operations use function-specific error types:

```maxon
enum DirectoryListError implements Error
	notFound
end 'DirectoryListError'
```

### Directory.list

List files and directories in a path.

**Signature:** `static function list(path FilePath) returns StringArray throws DirectoryListError`

where `type StringArray implements Array with String`

**Parameters:**
- `path`: Directory path

**Returns:** Array of filenames (excluding `.` and `..`)

**Throws:** `DirectoryListError.notFound` if directory doesn't exist

**Example:**

```maxon
function main() returns ExitCode
	let files = try Directory.list(FilePath from "./") otherwise 'err'
		print("Failed to list directory")
		return 1
	end 'err'
	for f in files 'loop'
		print("{f}\n")
	end 'loop'
	return 0
end 'main'
```

### Directory.exists

Check if a path exists and is a directory.

**Signature:** `static function exists(path FilePath) returns bool`

**Parameters:**
- `path`: Path to check

**Returns:** `true` if path exists and is a directory, `false` otherwise

**Example:**

```maxon
function main() returns ExitCode
	if Directory.exists(FilePath from "bin") 'check'
		print("bin is a directory")
	end 'check' else 'nodir'
		print("bin is not a directory")
	end 'nodir'
	return 0
end 'main'
```

### Directory.isDirectory

Check if a path is a directory. Alias for `exists`.

**Signature:** `static function isDirectory(path FilePath) returns bool`

**Parameters:**
- `path`: Path to check

**Returns:** `true` if path is a directory, `false` otherwise

### Directory.currentPath

Get the current working directory as a FilePath.

**Signature:** `static function currentPath() returns FilePath`

**Returns:** The current working directory as a FilePath

**Example:**

```maxon
function main() returns ExitCode
	let cwd = Directory.currentPath()
	print("{cwd}\n")
	return 0
end 'main'
```

## Tests

<!-- test: list-directory -->
```maxon
function main() returns ExitCode
	let files = try Directory.list(FilePath from "../bin") otherwise 'err'
		return 0
	end 'err'
	// bin directory should contain the maxon executable
	var foundMaxon = false
	for f in files 'loop'
		if f == "maxon.exe" or f == "maxon" 'check'
			foundMaxon = true
		end 'check'
	end 'loop'
	if foundMaxon 'result'
		return 42
	end 'result'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: list-directory-count -->
```maxon
function main() returns ExitCode
	let files = try Directory.list(FilePath from "../bin") otherwise 'err'
		return 99
	end 'err'
	// bin directory has at least maxon.exe
	if files.count() >= 1 'ok'
		return 42
	end 'ok'
	return files.count()
end 'main'
```
```exitcode
42
```

<!-- test: list-nonexistent-directory -->
```maxon
function main() returns ExitCode
	let files = try Directory.list(FilePath from "nonexistent_dir_12345") otherwise 'err'
		print("Directory not found")
		return 0
	end 'err'
	print("Found {files.count()} files\n")
	return 1
end 'main'
```
```exitcode
0
```
```stdout
Directory not found
```

<!-- test: directory-exists -->
```maxon
function main() returns ExitCode
	if Directory.exists(FilePath from "../bin") 'check'
		return 42
	end 'check'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: directory-is-directory -->
```maxon
function main() returns ExitCode
	if Directory.isDirectory(FilePath from "../bin") 'check'
		return 42
	end 'check'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: file-is-not-directory -->
```maxon
function main() returns ExitCode
	// Test that a nonexistent path is not a directory
	if Directory.isDirectory(FilePath from "nonexistent_path_12345") 'check'
		return 1
	end 'check'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: current-directory-not-empty -->
```maxon
function main() returns ExitCode
	let cwd = Directory.currentPath()
	if cwd.toString().count() > 0 'ok'
		return 42
	end 'ok'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: current-directory-is-directory -->
```maxon
function main() returns ExitCode
	let cwd = Directory.currentPath()
	if Directory.exists(cwd) 'ok'
		return 42
	end 'ok'
	return 0
end 'main'
```
```exitcode
42
```
