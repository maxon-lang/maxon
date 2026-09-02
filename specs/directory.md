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

### `Directory.exists` answers about the path it was GIVEN, at every length

Every `Directory` entry point hands the runtime a C string, and the conversion behind that
(`__managed_memory_to_cstring`) is allowed to skip the copy when the buffer is *already*
NUL-terminated. The byte it inspects to decide that — `buffer[length]` — is one PAST the content,
so it may only be read when the record can vouch for it. A record whose capacity equals its length
cannot: the byte belongs to whatever the allocator hands out next, and `maxon_directory_exists`
reaches `__io_submit_sync`, which allocates *before* the path is read. A conversion that trusted a
zero found there handed out a pointer whose terminator the very next allocation overwrote, and
`GetFileAttributesA` read on past the path's own end — answering **false** for a directory plainly
present.

The ladder below is the whole test. Every rung names the same directory through a different number
of no-op `./` prefixes, so all five must answer `true` and any `false` is the conversion reading a
byte it does not own.

**MEASURED 2026-09-02, x64-windows, against the unfixed conversion: a 48-byte path answered `false`
for a directory that was there. 16, 24, 32, 40, 56, 64, 72 and 80 all answered `true`.** So exactly
one rung of this ladder has ever fired, and it is 48.

**INFERRED from that, and not read off the allocator:** 48 is where a record comes out exactly full,
and it is the length that collides with the sync request the lookup itself allocates —
`__io_submit_sync` asks for a 40-byte `SyncRequest` (`SyncReqSize = 0x28`) *before* the path is read,
and 40 and 48 plausibly round into one size class, which would put that allocation on the very byte
the probe had just accepted as a terminator. The multiples of 16 are chosen on that reading. Treat
it as the working explanation for why 48 and nothing else, not as a fact about the allocator.

⚠ **Which rung fires is therefore a property of the ALLOCATOR rather than of the language, and 48 is
a dated measurement rather than a constant.** Change the size classes or `SyncReqSize` and a
different rung may become the live one — which is why five are pinned and not the one, and why a
green run here is not by itself evidence that the probe is safe. What the case ASSERTS is
host-independent and cannot go wrong either way: a directory that exists is reported to exist,
however its path is spelled.

<!-- test: directory-exists-at-every-path-length -->
```maxon
typealias PathByteCount = int(0 to 4096)

// The directory every spelling below names. Its length is EVEN, which is what lets a "./"-padded
// spelling of it land on any even total.
function probeDirName() returns String
	return "test_cstring_pin"
end 'probeDirName'

// A relative spelling of that directory exactly `target` bytes long. The padding is "./" repeated,
// which changes the path's LENGTH and nothing else about which directory it names.
function spellingOfLength(target PathByteCount) returns String
	let name = probeDirName()
	var pad = ""
	while pad.byteLength() + name.byteLength() < target 'padToLength'
		pad.append("./")
	end 'padToLength'

	// The interpolation is what mints the exactly-full record: `pad` grew by appending and has spare
	// capacity, while this result is allocated at the size it needs.
	let spelled = "{pad}{name}"
	if spelled.byteLength() != target 'wrongLength'
		panic("spellingOfLength: {target} is not reachable from a {name.byteLength()}-byte name in steps of 2")
	end 'wrongLength'

	return spelled
end 'spellingOfLength'

function seesTheDirectory(target PathByteCount) returns bool
	let path = try FilePath.from(spellingOfLength(target)) otherwise panic("spellingOfLength produced an invalid path")
	if Directory.exists(path) 'found'
		return true
	end 'found'

	print("Directory.exists said false for a {target}-byte spelling of an existing directory\n")
	return false
end 'seesTheDirectory'

function main() returns ExitCode
	if not Directory.create(FilePath from "test_cstring_pin") 'noProbeDir'
		return 1
	end 'noProbeDir'

	// Distinct codes, so a red run names the rung from its exit status alone.
	if not seesTheDirectory(16) 'at16'
		return 2
	end 'at16'
	if not seesTheDirectory(32) 'at32'
		return 3
	end 'at32'
	if not seesTheDirectory(48) 'at48'
		return 4
	end 'at48'
	if not seesTheDirectory(64) 'at64'
		return 5
	end 'at64'
	if not seesTheDirectory(80) 'at80'
		return 6
	end 'at80'

	return 42
end 'main'
```
```exitcode
42
```
