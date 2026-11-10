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
	accessDenied
	listFailed
end 'DirectoryListError'
```

### Directory.list

List files and directories in a path.

**Signature:** `static function list(path FilePath) returns FilePathArray throws DirectoryListError`

where `type FilePathArray implements Array with FilePath`

**Parameters:**
- `path`: Directory path

**Returns:** Array of FilePath entries (excluding `.` and `..`)

**Throws:** `DirectoryListError.notFound` if directory doesn't exist, `DirectoryListError.accessDenied` if the process may not list it, `DirectoryListError.listFailed` for any other failure

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

### Directory.removeTree

Remove a file, a directory with everything under it, or a directory link.

**Signature:** `static function removeTree(path FilePath) returns FilePathArray`

**Parameters:**
- `path`: The entry to remove

**Returns:** The paths that could not be removed; empty when everything is gone. A path nothing is at removes nothing and reports nothing.

A directory link (a symbolic link on POSIX, a junction or directory symbolic link on Windows) is removed as an entry: what it points at is never entered, listed or removed. An entry the process may not list or delete is reported and the walk goes on, so one stuck entry does not hide the rest; a directory holding a reported entry is itself reported only through that entry.

### Directory.removeContents

Remove everything inside a directory and keep the directory.

**Signature:** `static function removeContents(path FilePath) returns FilePathArray`

**Parameters:**
- `path`: The directory to empty

**Returns:** The paths that could not be removed, as `removeTree` reports them. A path that is not a directory removes nothing and reports nothing.

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
`__md_delete` removes an EMPTY directory and nothing else — `RemoveDirectoryW` on Windows, `rmdir` on
Darwin and `unlinkat(AT_FDCWD, path, AT_REMOVEDIR)` on Linux — so a directory holding anything at all
survives the call and the caller is told so. The reason
wasm32-wasi has none of it is written down in `file-io.md` and not repeated here; what would un-gate it is
the same WASI substrate that un-gates that file, plus its directory-enumeration twin — the one that cost
the most on every lane, because Win32 takes a GLOB where POSIX takes a directory: arm64-macOS emulates
`FindFirstFileW` over `opendir`/`readdir`/`fnmatch`, and both Linux lanes do it over
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
	let onlyFile = FilePath from "test_dots_one/only.txt"
	try File.writeText(onlyFile, content: "x") otherwise return 1

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
	try File.delete(onlyFile) otherwise return 6

	// An empty directory holds ONLY "." and "..". Filtering them must leave an
	// empty list — not a spurious entry read from an unwritten name buffer.
	let emptyDir = FilePath from "test_dots_empty"
	_ = Directory.create(emptyDir)
	let none = try Directory.list(emptyDir) otherwise return 7
	if none.count() != 0 'countZero'
		return 8
	end 'countZero'

	try Directory.delete(emptyDir) otherwise return 9
	try Directory.delete(oneFile) otherwise return 10

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

<!-- test: list-an-unreadable-directory-is-access-denied -->
<!-- unsupported-targets: wasm32-wasi -->
A directory the process may not list is `accessDenied`, never `notFound`: a caller that cannot tell a missing directory from
one it may not read cannot report the reason. The case denies itself the listing (a deny entry for Everyone on Windows,
mode 000 elsewhere), lists the directory, then restores access and removes what it made.
```maxon
let toolTimeoutMs = 30000
let directoryName = "directory-unreadable-listed"

function runTool(tool String, arguments StringArray) returns bool
	let result = try Subprocess.run(Executable.name(tool), arguments: arguments, timeoutMs: toolTimeoutMs) otherwise return false
	return result.succeeded()
end 'runTool'

function denyListing(directory FilePath) returns bool
	var arguments = StringArray.create(0)

	#if os(Windows)
	arguments.push(directory.toString())
	arguments.push("/deny")
	arguments.push("*S-1-1-0:(RD)")
	return runTool("icacls", arguments: arguments)
	#else
	arguments.push("000")
	arguments.push(directory.toString())
	return runTool("chmod", arguments: arguments)
	#endif
end 'denyListing'

function restoreListing(directory FilePath) returns bool
	var arguments = StringArray.create(0)

	#if os(Windows)
	arguments.push(directory.toString())
	arguments.push("/remove:d")
	arguments.push("*S-1-1-0")
	return runTool("icacls", arguments: arguments)
	#else
	arguments.push("755")
	arguments.push(directory.toString())
	return runTool("chmod", arguments: arguments)
	#endif
end 'restoreListing'

function main() returns ExitCode
	let directory = Directory.currentPath().join(directoryName)

	if not Directory.create(directory) 'madeIt'
		return 2
	end 'madeIt'

	if not denyListing(directory) 'denied'
		try Directory.delete(directory) otherwise ignore
		return 3
	end 'denied'

	var verdict = "listed"

	if let entries = try Directory.list(directory) 'listed'
		verdict = "listed {entries.count()} entries"
	end 'listed' else (e) 'refused'
		verdict = e.name
	end 'refused'

	if not restoreListing(directory) 'restored'
		return 4
	end 'restored'

	try Directory.delete(directory) otherwise return 5

	print("{verdict}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
accessDenied
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
file, and a name nothing created. These three cases run beside every other case in the suite, so each
names a directory nothing else here could reach and takes it away again.

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

### Removing a tree

`Directory.removeTree` and `Directory.removeContents` answer the paths they could not remove. Each case
below makes its own directories and takes them away again on its passing path.

<!-- test: remove-tree-removes-a-nested-tree -->
```maxon
function main() returns ExitCode
	let base = FilePath from "test_remove_tree_nested_9c4e"
	let deep = base.join("a").join("b")

	if not Directory.create(deep) 'couldNotCreate'
		return 1
	end 'couldNotCreate'

	try File.writeText(base.join("top.txt"), content: "x") otherwise return 2
	try File.writeText(base.join("a").join("mid.txt"), content: "x") otherwise return 3
	try File.writeText(deep.join("leaf.txt"), content: "x") otherwise return 4

	let unremoved = Directory.removeTree(base)

	if not unremoved.isEmpty() 'reportedAFailure'
		return 5
	end 'reportedAFailure'

	if Directory.exists(base) 'stillThere'
		return 6
	end 'stillThere'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: remove-tree-removes-a-file -->
```maxon
function main() returns ExitCode
	let file = FilePath from "test_remove_tree_file_1b3d.txt"
	try File.writeText(file, content: "x") otherwise return 1

	let unremoved = Directory.removeTree(file)

	if not unremoved.isEmpty() 'reportedAFailure'
		return 2
	end 'reportedAFailure'

	if File.exists(file) 'stillThere'
		return 3
	end 'stillThere'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: remove-tree-of-a-path-nothing-is-at-reports-nothing -->
```maxon
function main() returns ExitCode
	let missing = FilePath from "test_remove_tree_absent_6d02"

	if Directory.exists(missing) or File.exists(missing) 'somethingIsThere'
		return 1
	end 'somethingIsThere'

	if not Directory.removeTree(missing).isEmpty() 'reportedAFailure'
		return 2
	end 'reportedAFailure'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: remove-contents-empties-a-directory-and-keeps-it -->
```maxon
function main() returns ExitCode
	let base = FilePath from "test_remove_contents_c8f1"
	let nested = base.join("sub")

	if not Directory.create(nested) 'couldNotCreate'
		return 1
	end 'couldNotCreate'

	try File.writeText(base.join("top.txt"), content: "x") otherwise return 2
	try File.writeText(nested.join("leaf.txt"), content: "x") otherwise return 3

	let unremoved = Directory.removeContents(base)

	if not unremoved.isEmpty() 'reportedAFailure'
		return 4
	end 'reportedAFailure'

	if not Directory.exists(base) 'directoryWentToo'
		return 5
	end 'directoryWentToo'

	let left = try Directory.list(base) otherwise return 6

	if not left.isEmpty() 'somethingStays'
		return 7
	end 'somethingStays'

	try Directory.delete(base) otherwise return 8

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: remove-tree-removes-a-directory-link-and-not-what-it-points-at -->
<!-- unsupported-targets: wasm32-wasi -->
A directory link inside the tree is removed as an entry. The directory it points at, and what that holds, is
never entered. The link is a junction on Windows and a symbolic link elsewhere.
```maxon
let linkToolTimeoutMs = 30000

function plantDirectoryLink(link FilePath, target FilePath) returns bool
	var arguments = StringArray.create(0)

	#if os(Windows)
	arguments.push("/c")
	arguments.push("mklink")
	arguments.push("/J")
	arguments.push(link.toString())
	arguments.push(target.toString())
	let tool = "cmd"
	#else
	arguments.push("-s")
	arguments.push(target.toString())
	arguments.push(link.toString())
	let tool = "ln"
	#endif

	let result = try Subprocess.run(Executable.name(tool), arguments: arguments, timeoutMs: linkToolTimeoutMs) otherwise return false

	return result.succeeded()
end 'plantDirectoryLink'

function main() returns ExitCode
	let base = Directory.currentPath().join("test_remove_tree_link_base_2f7a")
	let target = Directory.currentPath().join("test_remove_tree_link_target_2f7a")
	let kept = target.join("kept.txt")

	if not Directory.create(base) or not Directory.create(target) 'couldNotCreate'
		return 1
	end 'couldNotCreate'

	try File.writeText(kept, content: "kept") otherwise return 2

	if not plantDirectoryLink(base.join("link"), target: target) 'couldNotLink'
		return 3
	end 'couldNotLink'

	let unremoved = Directory.removeTree(base)

	if not unremoved.isEmpty() 'reportedAFailure'
		return 4
	end 'reportedAFailure'

	if Directory.exists(base) 'stillThere'
		return 5
	end 'stillThere'

	if not File.exists(kept) 'followedTheLink'
		return 6
	end 'followedTheLink'

	try File.delete(kept) otherwise return 7
	try Directory.delete(target) otherwise return 8

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: remove-tree-reports-what-it-cannot-remove -->
<!-- unsupported-targets: wasm32-wasi -->
A directory the process may not list cannot be emptied. `removeTree` reports it, removes what it can around it,
and leaves the directories above it standing. The case denies itself the listing (a deny entry for Everyone on
Windows, mode 000 elsewhere), then restores access and removes what it made.
```maxon
let accessToolTimeoutMs = 30000

function runAccessTool(tool String, arguments StringArray) returns bool
	let result = try Subprocess.run(Executable.name(tool), arguments: arguments, timeoutMs: accessToolTimeoutMs) otherwise return false

	return result.succeeded()
end 'runAccessTool'

function denyListing(directory FilePath) returns bool
	var arguments = StringArray.create(0)

	#if os(Windows)
	arguments.push(directory.toString())
	arguments.push("/deny")
	arguments.push("*S-1-1-0:(RD)")
	return runAccessTool("icacls", arguments: arguments)
	#else
	arguments.push("000")
	arguments.push(directory.toString())
	return runAccessTool("chmod", arguments: arguments)
	#endif
end 'denyListing'

function restoreListing(directory FilePath) returns bool
	var arguments = StringArray.create(0)

	#if os(Windows)
	arguments.push(directory.toString())
	arguments.push("/remove:d")
	arguments.push("*S-1-1-0")
	return runAccessTool("icacls", arguments: arguments)
	#else
	arguments.push("755")
	arguments.push(directory.toString())
	return runAccessTool("chmod", arguments: arguments)
	#endif
end 'restoreListing'

function main() returns ExitCode
	let base = Directory.currentPath().join("test_remove_tree_refused_e5a9")
	let locked = base.join("locked")

	if not Directory.create(locked) 'couldNotCreate'
		return 1
	end 'couldNotCreate'

	try File.writeText(base.join("loose.txt"), content: "x") otherwise return 2
	try File.writeText(locked.join("held.txt"), content: "x") otherwise return 3

	if not denyListing(locked) 'denied'
		return 4
	end 'denied'

	let unremoved = Directory.removeTree(base)
	print("unremoved {unremoved.count()}\n")

	for stuck in unremoved 'eachStuck'
		print("{stuck.filename()}\n")
	end 'eachStuck'

	print("loose.txt gone {not File.exists(base.join("loose.txt"))}\n")
	print("base stands {Directory.exists(base)}\n")

	if not restoreListing(locked) 'restored'
		return 5
	end 'restored'

	if not Directory.removeTree(base).isEmpty() 'stillStuck'
		return 6
	end 'stillStuck'

	if Directory.exists(base) 'stillThere'
		return 7
	end 'stillThere'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
unremoved 1
locked
loose.txt gone true
base stands true
```

<!-- test: a-path-holding-a-nul-names-no-directory-not-its-prefix -->
A path holding a NUL names no directory on any lane — never the one named by the bytes before the NUL.
Creating one creates nothing, and listing one is refused even when the prefix is a directory that exists.
```maxon
function main() returns ExitCode
	let here = try FilePath.from(".") otherwise return 2
	let madePrefix = here.join("directory-nul-made")
	let listedPrefix = here.join("directory-nul-listed")

	print("create {Directory.create(here.join("directory-nul-made\0x"))}\n")
	print("prefix created {Directory.exists(madePrefix)}\n")

	if not Directory.create(listedPrefix) 'setup'
		return 3
	end 'setup'

	if let entries = try Directory.list(here.join("directory-nul-listed\0x")) 'listed'
		print("list answered {entries.count()} entries\n")
	end 'listed' else (e) 'listRefused'
		print("list refused {e.name}\n")
	end 'listRefused'

	try Directory.delete(madePrefix) otherwise ignore
	try Directory.delete(listedPrefix) otherwise ignore
	return 0
end 'main'
```
```stdout
create false
prefix created false
list refused notFound
```
```exitcode
0
```
