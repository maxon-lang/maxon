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

<!-- unsupported-targets: wasm32-wasi -->
**`file-io.md`'s "Targets — the one statement of the FILESYSTEM gate"**: `Directory.list` / `exists` /
`isDirectory` / `create` / `delete` / `currentPath` lower to the runtime entries `__md_open_search`,
`__md_exists`, `__md_create`, `__md_delete` and `__md_current_path`, and `list-filters-dot-entries`
reaches `__mf_*` on top of those. Every lane this compiler emits serves the family except wasm32-wasi,
which is why the marker names that one alone; the refusal is `E3104`, raised by
`SemanticCheck.requireTargetSupportsCallee`, not by the marker. **Every case in this file reaches one of
those entries, so every one of them is refused on a lane without the family**, each naming its own entry.
`__md_delete` removes an EMPTY directory and nothing else — `RemoveDirectoryA` on Windows, `rmdir` on
Darwin and `unlinkat(AT_FDCWD, path, AT_REMOVEDIR)` on Linux — so a directory holding anything at all
survives the call and the caller is told so. The reason
wasm32-wasi has none of it is written down in `file-io.md` and not repeated here; what would un-gate it is
the same WASI substrate that un-gates that file, plus its directory-enumeration twin — the one that cost
the most on every lane, because Win32 takes a GLOB where POSIX takes a directory: arm64-macOS emulates
`FindFirstFileA` over `opendir`/`readdir`/`fnmatch`, and both Linux lanes do it over
`openat(O_DIRECTORY)`/`getdents64` with the wildcard match HAND-WRITTEN, a raw static image linking no
`fnmatch` to call.

## Tests

<!-- test: list-directory -->
```maxon
function main() returns ExitCode
	let files = try Directory.list(FilePath from "../stdlib") otherwise 'err'
		return 0
	end 'err'
	// every checkout's stdlib carries String.maxon
	var foundModule = false
	for f in files 'loop'
		let name = f.filename()
		if name == "String.maxon" 'check'
			foundModule = true
		end 'check'
	end 'loop'
	if foundModule 'result'
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
	let files = try Directory.list(FilePath from "../stdlib") otherwise 'err'
		return 99
	end 'err'
	// the stdlib holds many modules
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
	if Directory.exists(FilePath from "../stdlib") 'check'
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
	if Directory.isDirectory(FilePath from "../stdlib") 'check'
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

### Removing a directory

`Directory.delete` removes an EMPTY directory and throws for anything else — a directory holding a
file, and a name nothing created. These three cases run with the working directory `temp/` and beside
every other case in the suite, so each names a directory nothing else here could reach and takes it
away again.

<!-- test: delete-removes-an-empty-directory -->
```maxon
function main() returns ExitCode
	let dir = FilePath from "test_delete_empty_a7c1"
	if not Directory.create(dir) 'couldNotCreate'
		return 1
	end 'couldNotCreate'

	try Directory.delete(dir) otherwise return 2

	if Directory.exists(dir) 'stillThere'
		return 3
	end 'stillThere'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: delete-refuses-a-directory-that-is-not-empty -->
```maxon
function main() returns ExitCode
	let dir = FilePath from "test_delete_nonempty_3e8d"
	if not Directory.create(dir) 'couldNotCreate'
		return 1
	end 'couldNotCreate'

	let occupant = dir.join("occupant.txt")
	try File.writeText(occupant, content: "x") otherwise return 2

	var refused = false

	try Directory.delete(dir) otherwise 'refusedNonEmpty'
		refused = true
	end 'refusedNonEmpty'

	// Read before the cleanup below takes the directory away for real.
	let survived = Directory.exists(dir)

	try File.delete(occupant) otherwise ignore
	try Directory.delete(dir) otherwise ignore

	if not refused 'removedADirectoryHoldingAFile'
		return 3
	end 'removedADirectoryHoldingAFile'

	if not survived 'removedWhatItRefused'
		return 4
	end 'removedWhatItRefused'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: delete-refuses-a-path-that-is-not-there -->
```maxon
function main() returns ExitCode
	let missing = FilePath from "test_delete_absent_5b20"
	if Directory.exists(missing) 'somethingIsThere'
		return 1
	end 'somethingIsThere'

	try Directory.delete(missing) otherwise return 42

	return 2
end 'main'
```
```exitcode
42
```
