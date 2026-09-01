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

**Signature:** `static function list(path FilePath) returns FilePathArray throws DirectoryListError`

where `type FilePathArray implements Array with FilePath`

**Parameters:**
- `path`: Directory path

**Returns:** Array of FilePath entries (excluding `.` and `..`)

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

## Targets

⭐ Every case below carries `<!-- targets: x64-windows, arm64-macos -->`, for the ONE reason stated in
**`file-io.md`'s "Targets — the one statement of the FILESYSTEM gate"**: `Directory.list` / `exists` /
`isDirectory` / `create` / `currentPath` lower to the runtime entries `__md_open_search`, `__md_exists`,
`__md_create` and `__md_current_path`, and `list-filters-dot-entries` reaches `__mf_*` on top of those.
None has an x64-linux or wasm32-wasi implementation at this rung — arm64-macOS gained one at MAC4, which is why the markers name it — `E3104`, raised by
`SemanticCheck.requireTargetSupportsCallee`, not by the marker. MEASURED at the rung that first loaded
`stdlib/Directory.maxon`: **9 of 9 cases refused on wasm32-wasi**, each naming its own entry. The reason
is written down in `file-io.md` and not repeated here; what un-gates the REMAINING lanes is the same POSIX/WASI
substrate that un-gates that file, plus its directory-enumeration twin — which on arm64-macOS meant
emulating `FindFirstFileA`'s GLOB over `opendir`/`readdir`/`fnmatch`, because Win32 takes a pattern where
POSIX takes a directory.

## Tests

<!-- test: list-directory -->
<!-- targets: x64-windows, arm64-macos -->
```maxon
function main() returns ExitCode
	let files = try Directory.list(FilePath from "../bin") otherwise 'err'
		return 0
	end 'err'
	// bin directory should contain the maxon executable
	var foundMaxon = false
	for f in files 'loop'
		let name = f.filename()
		if name == "maxon.exe" or name == "maxon" 'check'
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
<!-- targets: x64-windows, arm64-macos -->
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

### Listing filters the `.` and `..` pseudo-entries

`Directory.list` never yields `.` or `..`. The runtime's find-next owns that filtering on
every target — the stdlib pushes whatever it is handed — so a leaked dot reaches callers as
a real name, and a leaked `..` walks *upward out of the tree*, which is how
`collectMaxonFilesUnder` recursed forever.

This pins both halves, because they have been traded against each other twice. Removing the
filter fixed empty directories and broke listing; adding it back to only some targets fixed
listing and left arm64-macOS unfiltered for a month. The existing tests above could not see
it: they assert membership and `count() >= 1`, which stay true while two bogus entries ride
along. **Only an exact count catches this.**

<!-- test: list-filters-dot-entries -->
<!-- targets: x64-windows, arm64-macos -->
```maxon
function main() returns ExitCode
	let oneFile = FilePath from "test_dots_one"
	_ = Directory.create(oneFile)
	var f = try __ManagedFile.openWrite("test_dots_one/only.txt".toByteArray().managed) otherwise return 1
	try f.write("x".toByteArray().managed) otherwise return 2
	f.close()

	let entries = try Directory.list(oneFile) otherwise return 3
	for e in entries 'each'
		let name = e.filename()
		if name == "." or name == ".." 'dot'
			return 4
		end 'dot'
	end 'each'
	if entries.count() != 1 'countOne'
		return 5
	end 'countOne'
	try __ManagedFile.delete("test_dots_one/only.txt".toByteArray().managed) otherwise return 6

	// An empty directory holds ONLY "." and "..". Filtering them must leave an
	// empty list — not a spurious entry read from an unwritten name buffer.
	let emptyDir = FilePath from "test_dots_empty"
	_ = Directory.create(emptyDir)
	let none = try Directory.list(emptyDir) otherwise return 7
	if none.count() != 0 'countZero'
		return 8
	end 'countZero'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: list-nonexistent-directory -->
<!-- targets: x64-windows, arm64-macos -->
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
<!-- targets: x64-windows, arm64-macos -->
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
<!-- targets: x64-windows, arm64-macos -->
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
<!-- targets: x64-windows, arm64-macos -->
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
<!-- targets: x64-windows, arm64-macos -->
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
<!-- targets: x64-windows, arm64-macos -->
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
