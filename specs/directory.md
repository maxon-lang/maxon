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
mode 000 elsewhere), asks the host's own lister whether the denial binds this process, lists the directory, then restores
access and removes what it made. Whether a denial binds is the host's to say: a process holding the backup privilege lists
a directory whatever its ACL says, so `Directory.list` must answer what the host's lister answered, and a denial that
does not bind must be one the process's own token (the backup or restore privilege on Windows, uid 0 elsewhere) explains.
```maxon
let toolTimeoutMs = 30000
let directoryName = "directory-unreadable-listed"

#if os(Windows)
let listerRefusedExit = 5
#else
let listerRefusedExit = 1
#endif

function runTool(tool String, arguments StringArray) returns bool
	let result = try Subprocess.run(Executable.name(tool), arguments: arguments, deadline: WaitDeadline.within(toolTimeoutMs)) otherwise return false
	return result.succeeded()
end 'runTool'

function hostRefusesListing(directory FilePath) returns bool
	let arguments = StringArray.create(0)

	#if os(Windows)
	arguments.push("-NoProfile")
	arguments.push("-Command")
	arguments.push("try \{ [void][System.IO.Directory]::GetFileSystemEntries('{directory.toString()}'); exit 0 \} catch [System.UnauthorizedAccessException] \{ exit {listerRefusedExit} \}")
	let result = try Subprocess.run(Executable.name("powershell"), arguments: arguments, deadline: WaitDeadline.within(toolTimeoutMs)) otherwise panic("the host's lister did not run")
	#else
	arguments.push("-r")
	arguments.push(directory.toString())
	let result = try Subprocess.run(Executable.name("test"), arguments: arguments, deadline: WaitDeadline.within(toolTimeoutMs)) otherwise panic("the host's lister did not run")
	#endif

	if result.succeeded() 'listable'
		return false
	end 'listable'

	if result.exitCode() != listerRefusedExit 'unanswered'
		panic("the host's lister answered {result.exitCode()}, neither listing nor refusing")
	end 'unanswered'

	return true
end 'hostRefusesListing'

function tokenBypassesDenial() returns bool
	let arguments = StringArray.create(0)

	#if os(Windows)
	arguments.push("/priv")
	arguments.push("/fo")
	arguments.push("csv")
	arguments.push("/nh")
	let result = try Subprocess.run(Executable.name("whoami"), arguments: arguments, deadline: WaitDeadline.within(toolTimeoutMs)) otherwise panic("the host's privilege report did not run")

	return result.succeeded() and (result.stdout.contains("SeBackupPrivilege") or result.stdout.contains("SeRestorePrivilege"))
	#else
	arguments.push("-u")
	let result = try Subprocess.run(Executable.name("id"), arguments: arguments, deadline: WaitDeadline.within(toolTimeoutMs)) otherwise panic("the host's privilege report did not run")

	return result.succeeded() and result.stdout.trim() == "0"
	#endif
end 'tokenBypassesDenial'

function denyListing(directory FilePath) returns bool
	let arguments = StringArray.create(0)

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
	let arguments = StringArray.create(0)

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

	let hostRefuses = hostRefusesListing(directory)
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

	let expected = "accessDenied" if hostRefuses else "listed 0 entries"

	let denialExplained = hostRefuses or tokenBypassesDenial()

	print("list answered what the host's lister did {verdict == expected}\n")
	print("a denial that did not bind is a privileged token's {denialExplained}\n")

	if verdict != expected 'disagreed'
		print("the host refuses listing {hostRefuses}, Directory.list answered {verdict}\n")
	end 'disagreed'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
list answered what the host's lister did true
a denial that did not bind is a privileged token's true
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

<!-- test: delete-refuses-an-existing-file-as-failed-and-leaves-it -->
```maxon
function main() returns ExitCode
	let file = FilePath from "test_delete_file_8a4f.txt"
	try File.writeText(file, content: "x") otherwise return 1

	var answeredAsExpected = false

	try Directory.delete(file) otherwise (refusal) 'refused'
		if refusal == DirectoryDeleteError.deleteFailed 'asFailed'
			answeredAsExpected = true
		end 'asFailed'
	end 'refused'

	let survived = File.exists(file)

	try File.delete(file) otherwise ignore

	if not survived 'removedAFile'
		return 2
	end 'removedAFile'

	if not answeredAsExpected 'wrongAnswer'
		return 3
	end 'wrongAnswer'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: delete-answers-not-found-for-a-path-through-a-file -->
```maxon
function main() returns ExitCode
	let file = FilePath from "test_delete_through_file_c93b.txt"
	try File.writeText(file, content: "x") otherwise return 1

	var answeredAsExpected = false

	try Directory.delete(file.join("below")) otherwise (refusal) 'refused'
		if refusal == DirectoryDeleteError.notFound 'asNotFound'
			answeredAsExpected = true
		end 'asNotFound'
	end 'refused'

	try File.delete(file) otherwise ignore

	if not answeredAsExpected 'wrongAnswer'
		return 2
	end 'wrongAnswer'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: delete-refuses-a-dangling-link-as-failed-and-leaves-it -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
A symbolic link whose target is gone is still an entry at its path, so `Directory.delete` of it is `deleteFailed`
and the link stays: nothing resolves through it, and only a lookup that does not follow links finds it.
```maxon
let linkToolTimeoutMs = 30000

function plantLink(link FilePath, target FilePath) returns bool
	let arguments = StringArray.create(0)
	arguments.push("-s")
	arguments.push(target.toString())
	arguments.push(link.toString())

	let result = try Subprocess.run(Executable.name("ln"), arguments: arguments, deadline: WaitDeadline.within(linkToolTimeoutMs)) otherwise return false

	return result.succeeded()
end 'plantLink'

function main() returns ExitCode
	let base = Directory.currentPath().join("test_delete_dangling_link_6d2e")
	let link = base.join("dangling")

	if not Directory.create(base) 'couldNotCreate'
		return 1
	end 'couldNotCreate'

	if not plantLink(link, target: base.join("nothing-here")) 'couldNotLink'
		return 2
	end 'couldNotLink'

	var answeredAsExpected = false

	try Directory.delete(link) otherwise (refusal) 'refused'
		if refusal == DirectoryDeleteError.deleteFailed 'asFailed'
			answeredAsExpected = true
		end 'asFailed'
	end 'refused'

	let entries = try Directory.list(base) otherwise return 7
	let kept = entries.count()

	try File.delete(link) otherwise return 3
	try Directory.delete(base) otherwise return 4

	if not answeredAsExpected 'wrongAnswer'
		return 5
	end 'wrongAnswer'

	if kept != 1 'removedTheLink'
		return 6
	end 'removedTheLink'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: delete-refuses-a-differently-cased-spelling-of-an-existing-file -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
Whether a path names an entry is the file system's answer, not a comparison of spellings: on a volume that
folds case, a spelling of an existing file in other letters names that file, so `Directory.delete` of it is
`deleteFailed` and the file stays. On a volume that does not fold case the spelling names nothing, and the
answer is `notFound`.
```maxon
let testToolTimeoutMs = 30000

function hostFindsAnEntryAt(path FilePath) returns bool
	let arguments = StringArray.create(0)
	arguments.push("-e")
	arguments.push(path.toString())

	let result = try Subprocess.run(Executable.name("test"), arguments: arguments, deadline: WaitDeadline.within(testToolTimeoutMs)) otherwise panic("the host's existence test could not run")

	if result.succeeded() 'found'
		return true
	end 'found'

	if result.exitCode() != 1 'neither'
		panic("the host's existence test answered {result.exitCode()}, neither found nor absent")
	end 'neither'

	return false
end 'hostFindsAnEntryAt'

function main() returns ExitCode
	let base = Directory.currentPath().join("test_delete_other_case_3f7a")
	let file = base.join("entry.txt")
	let otherCase = base.join("ENTRY.TXT")

	if not Directory.create(base) 'couldNotCreate'
		return 1
	end 'couldNotCreate'

	try File.writeText(file, content: "x") otherwise return 2

	let foldsCase = hostFindsAnEntryAt(otherCase)
	let expected = DirectoryDeleteError.deleteFailed if foldsCase else DirectoryDeleteError.notFound
	var answeredAsExpected = false

	try Directory.delete(otherCase) otherwise (refusal) 'refused'
		answeredAsExpected = refusal == expected
	end 'refused'

	let survived = File.exists(file)

	try File.delete(file) otherwise return 3
	try Directory.delete(base) otherwise return 4

	print("the answer is the file system's {answeredAsExpected}\n")
	print("the file stays {survived}\n")

	return 0
end 'main'
```
```stdout
the answer is the file system's true
the file stays true
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
	let arguments = StringArray.create(0)

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

	let result = try Subprocess.run(Executable.name(tool), arguments: arguments, deadline: WaitDeadline.within(linkToolTimeoutMs)) otherwise return false

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
Windows, mode 000 elsewhere), asks the host's own lister whether the denial binds this process, then restores access
and removes what it made. Whether a denial binds is the host's to say: a process holding the backup privilege lists
and removes a directory whatever its ACL says, so `removeTree` must report exactly what the host's lister refused, and a
denial that does not bind must be one the process's own token (the backup or restore privilege on Windows, uid 0
elsewhere) explains.
```maxon
let accessToolTimeoutMs = 30000

#if os(Windows)
let listerRefusedExit = 5
#else
let listerRefusedExit = 1
#endif

function runAccessTool(tool String, arguments StringArray) returns bool
	let result = try Subprocess.run(Executable.name(tool), arguments: arguments, deadline: WaitDeadline.within(accessToolTimeoutMs)) otherwise return false

	return result.succeeded()
end 'runAccessTool'

function hostRefusesListing(directory FilePath) returns bool
	let arguments = StringArray.create(0)

	#if os(Windows)
	arguments.push("-NoProfile")
	arguments.push("-Command")
	arguments.push("try \{ [void][System.IO.Directory]::GetFileSystemEntries('{directory.toString()}'); exit 0 \} catch [System.UnauthorizedAccessException] \{ exit {listerRefusedExit} \}")
	let result = try Subprocess.run(Executable.name("powershell"), arguments: arguments, deadline: WaitDeadline.within(accessToolTimeoutMs)) otherwise panic("the host's lister did not run")
	#else
	arguments.push("-r")
	arguments.push(directory.toString())
	let result = try Subprocess.run(Executable.name("test"), arguments: arguments, deadline: WaitDeadline.within(accessToolTimeoutMs)) otherwise panic("the host's lister did not run")
	#endif

	if result.succeeded() 'listable'
		return false
	end 'listable'

	if result.exitCode() != listerRefusedExit 'unanswered'
		panic("the host's lister answered {result.exitCode()}, neither listing nor refusing")
	end 'unanswered'

	return true
end 'hostRefusesListing'

function tokenBypassesDenial() returns bool
	let arguments = StringArray.create(0)

	#if os(Windows)
	arguments.push("/priv")
	arguments.push("/fo")
	arguments.push("csv")
	arguments.push("/nh")
	let result = try Subprocess.run(Executable.name("whoami"), arguments: arguments, deadline: WaitDeadline.within(accessToolTimeoutMs)) otherwise panic("the host's privilege report did not run")

	return result.succeeded() and (result.stdout.contains("SeBackupPrivilege") or result.stdout.contains("SeRestorePrivilege"))
	#else
	arguments.push("-u")
	let result = try Subprocess.run(Executable.name("id"), arguments: arguments, deadline: WaitDeadline.within(accessToolTimeoutMs)) otherwise panic("the host's privilege report did not run")

	return result.succeeded() and result.stdout.trim() == "0"
	#endif
end 'tokenBypassesDenial'

function denyListing(directory FilePath) returns bool
	let arguments = StringArray.create(0)

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
	let arguments = StringArray.create(0)

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

	let hostRefuses = hostRefusesListing(locked)
	let unremoved = Directory.removeTree(base)
	var reportedLocked = false

	for stuck in unremoved 'eachStuck'
		reportedLocked = reportedLocked or stuck.filename() == "locked"
	end 'eachStuck'

	let looseGone = not File.exists(base.join("loose.txt"))
	let baseStands = Directory.exists(base)
	let expectedUnremoved = 1 if hostRefuses else 0
	let matches = unremoved.count() == expectedUnremoved and reportedLocked == hostRefuses and looseGone and baseStands == hostRefuses

	let denialExplained = hostRefuses or tokenBypassesDenial()

	print("removeTree reported what the host refused {matches}\n")
	print("a denial that did not bind is a privileged token's {denialExplained}\n")

	if not matches 'disagreed'
		print("the host refuses listing {hostRefuses}, unremoved {unremoved.count()}, locked reported {reportedLocked}, loose.txt gone {looseGone}, base stands {baseStands}\n")
	end 'disagreed'

	if Directory.exists(locked) 'stillDenied'
		if not restoreListing(locked) 'restored'
			return 5
		end 'restored'
	end 'stillDenied'

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
removeTree reported what the host refused true
a denial that did not bind is a privileged token's true
```

<!-- test: remove-tree-reports-a-dangling-link-it-cannot-remove -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
A symbolic link whose target is gone is an entry like any other: when the directory holding it denies the process
the right to remove entries, `removeTree` reports the link by its own path. It never reads the link as already gone
because nothing resolves through it. The case drops the directory's write permission, asks the host whether the
denial binds this process, then restores access and removes what it made. A denial that does not bind must be uid 0's.
```maxon
let accessToolTimeoutMs = 30000

function runAccessTool(tool String, arguments StringArray) returns bool
	let result = try Subprocess.run(Executable.name(tool), arguments: arguments, deadline: WaitDeadline.within(accessToolTimeoutMs)) otherwise return false

	return result.succeeded()
end 'runAccessTool'

function plantLink(link FilePath, target FilePath) returns bool
	let arguments = StringArray.create(0)
	arguments.push("-s")
	arguments.push(target.toString())
	arguments.push(link.toString())

	return runAccessTool("ln", arguments: arguments)
end 'plantLink'

function setMode(mode String, directory FilePath) returns bool
	let arguments = StringArray.create(0)
	arguments.push(mode)
	arguments.push(directory.toString())

	return runAccessTool("chmod", arguments: arguments)
end 'setMode'

function hostRefusesWriting(directory FilePath) returns bool
	let arguments = StringArray.create(0)
	arguments.push("-w")
	arguments.push(directory.toString())

	let result = try Subprocess.run(Executable.name("test"), arguments: arguments, deadline: WaitDeadline.within(accessToolTimeoutMs)) otherwise panic("the host's access test did not run")

	if result.succeeded() 'writable'
		return false
	end 'writable'

	if result.exitCode() != 1 'unanswered'
		panic("the host's access test answered {result.exitCode()}, neither writable nor refusing")
	end 'unanswered'

	return true
end 'hostRefusesWriting'

function isRoot() returns bool
	let arguments = StringArray.create(0)
	arguments.push("-u")

	let result = try Subprocess.run(Executable.name("id"), arguments: arguments, deadline: WaitDeadline.within(accessToolTimeoutMs)) otherwise panic("the host's identity report did not run")

	return result.succeeded() and result.stdout.trim() == "0"
end 'isRoot'

function main() returns ExitCode
	let base = Directory.currentPath().join("test_remove_tree_dangling_link_b41c")
	let held = base.join("held")
	let link = held.join("dangling")

	if not Directory.create(held) 'couldNotCreate'
		return 1
	end 'couldNotCreate'

	if not plantLink(link, target: base.join("nothing-here")) 'couldNotLink'
		return 2
	end 'couldNotLink'

	if not setMode("555", directory: held) 'denied'
		return 3
	end 'denied'

	let hostRefuses = hostRefusesWriting(held)
	let unremoved = Directory.removeTree(base)
	var reportedTheLink = false

	for stuck in unremoved 'eachStuck'
		reportedTheLink = reportedTheLink or stuck.toString() == link.toString()
	end 'eachStuck'

	let expectedUnremoved = 1 if hostRefuses else 0
	let matches = unremoved.count() == expectedUnremoved and reportedTheLink == hostRefuses and Directory.exists(base) == hostRefuses

	let denialExplained = hostRefuses or isRoot()

	print("removeTree reported what the host refused {matches}\n")
	print("a denial that did not bind is uid 0's {denialExplained}\n")

	if not matches 'disagreed'
		print("the host refuses writing {hostRefuses}, unremoved {unremoved.count()}, link reported {reportedTheLink}\n")
	end 'disagreed'

	if Directory.exists(held) 'stillDenied'
		if not setMode("755", directory: held) 'restored'
			return 4
		end 'restored'
	end 'stillDenied'

	if not Directory.removeTree(base).isEmpty() 'stillStuck'
		return 5
	end 'stillStuck'

	if Directory.exists(base) 'stillThere'
		return 6
	end 'stillThere'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
removeTree reported what the host refused true
a denial that did not bind is uid 0's true
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

### A path longer than the legacy Windows limit

Every `File` and `Directory` operation takes a path of any length. On Windows a relative path, and a path of 248 units or
more, is first resolved by the operating system; the runtime hands the OS the extended form (`\\?\`, and `\\?\UNC\` for a
network path) once the resolved path reaches 248 units, which is the longest a directory path may be before the OS refuses
it, so a valid layout never fails by length. Every spelling of an entry the OS resolves to the same place, `/`, `.`, `..`,
repeated and trailing separators included, names that entry at every length.

<!-- test: an-absolute-path-past-the-legacy-windows-limit-names-the-same-entries-as-a-short-one -->
```maxon
function main() returns ExitCode
	let rootName = "long-path-root"
	let root = Directory.currentPath().join(rootName)
	var deep = Directory.currentPath().join(rootName)

	for i in 0 upto 12 'segments'
		deep = deep.join("a-fairly-long-directory-segment-number-{i}")
	end 'segments'

	print("past the limit {deep.toString().byteLength() > 260}\n")
	print("create {Directory.create(deep)}\n")
	print("exists {Directory.exists(deep)}\n")

	let file = deep.join("file.txt")

	try File.writeText(file, content: "hello") otherwise (e) 'noWrite'
		print("write refused {e.name}\n")
		return 4
	end 'noWrite'

	let text = try File.readText(file) otherwise return 5
	print("read {text}\n")
	print("file exists {File.exists(file)}\n")

	let detour = deep.join("detour")
	print("detour {Directory.create(detour)}\n")

	let sep = FilePath.separator()
	let detoured = try FilePath.from("{deep.toString()}{sep}.{sep}detour{sep}..{sep}{sep}file.txt") otherwise return 6
	let again = try File.readText(detoured) otherwise return 7
	print("detoured read {again}\n")

	let listed = try Directory.list(deep) otherwise return 8
	print("listed {listed.count()}\n")

	let renamed = deep.join("renamed.txt")
	try File.rename(file, to: renamed) otherwise return 9
	let info = try File.info(renamed) otherwise return 10
	print("size {info.size}\n")
	print("old exists {File.exists(file)}\n")

	try File.delete(renamed) otherwise return 11
	print("unremoved {Directory.removeTree(root).count()}\n")
	print("root exists {Directory.exists(root)}\n")

	return 0
end 'main'
```
```stdout
past the limit true
create true
exists true
read hello
file exists true
detour true
detoured read hello
listed 2
size 5
old exists false
unremoved 0
root exists false
```
```exitcode
0
```

<!-- test: a-relative-path-past-the-legacy-windows-limit-names-the-same-entries-as-a-short-one -->
A relative path names its entries against the working directory, so its length is the working directory's plus its own.
```maxon
function main() returns ExitCode
	let rootName = "long-relative-root"
	let root = try FilePath.from(rootName) otherwise return 2
	var deep = root.join("a-fairly-long-directory-segment-number-first")

	for i in 0 upto 11 'segments'
		deep = deep.join("a-fairly-long-directory-segment-number-{i}")
	end 'segments'

	print("past the limit {deep.toString().byteLength() > 260}\n")
	print("create {Directory.create(deep)}\n")

	let file = deep.join("file.txt")

	try File.writeText(file, content: "hello") otherwise (e) 'noWrite'
		print("write refused {e.name}\n")
		return 4
	end 'noWrite'

	let text = try File.readText(file) otherwise return 5
	print("read {text}\n")
	print("file exists {File.exists(file)}\n")
	print("directory exists {Directory.exists(deep)}\n")

	let info = try File.info(file) otherwise return 6
	print("size {info.size}\n")

	try File.delete(file) otherwise return 7
	print("unremoved {Directory.removeTree(root).count()}\n")

	return 0
end 'main'
```
```stdout
past the limit true
create true
read hello
file exists true
directory exists true
size 5
unremoved 0
```
```exitcode
0
```

<!-- test: a-path-spelled-with-slashes-repeated-separators-and-a-trailing-separator-names-the-same-entry-at-both-lengths -->
The same file and directory are spelled with a forward slash for one separator, with all of them, with a repeated
separator, with a `.` segment, through a `..` that cancels a sibling, and with a trailing separator, once below the
legacy limit and once past it. Each spelling names the entry its plain form names.
```maxon
let rootName = "long-spelling-root"

function layout(root FilePath, segments Count) returns FilePath
	var deep = root

	for i in 0 upto segments 'segments'
		deep = deep.join("a-fairly-long-directory-segment-number-{i}")
	end 'segments'

	return deep
end 'layout'

function spell(text String) returns FilePath
	return try FilePath.from(text) otherwise panic("a spelling this case builds is not a path")
end 'spell'

function readOrNone(path FilePath) returns String
	return try File.readText(path) otherwise "unread"
end 'readOrNone'

function listedCount(dir FilePath) returns Count
	let entries = try Directory.list(dir) otherwise return 0

	return entries.count()
end 'listedCount'

function probe(label String, dir FilePath) returns bool
	let sep = FilePath.separator()
	let text = dir.toString()
	let forward = text.replace(sep, with: "/")

	print("{label} past the limit {text.byteLength() > 260}\n")
	print("{label} create {Directory.create(dir)}\n")
	print("{label} detour {Directory.create(dir.join("detour"))}\n")

	try File.writeText(dir.join("file.txt"), content: "hello") otherwise return false

	let mixed = spell("{text}/file.txt")
	let everySlash = spell("{forward}/file.txt")
	let doubled = spell("{text}{sep}{sep}file.txt")
	let dotted = spell("{text}{sep}.{sep}file.txt")
	let detoured = spell("{text}{sep}detour{sep}..{sep}file.txt")
	let closed = spell("{text}{sep}")

	print("{label} mixed {readOrNone(mixed)}\n")
	print("{label} every slash {readOrNone(everySlash)}\n")
	print("{label} doubled {readOrNone(doubled)}\n")
	print("{label} dotted {readOrNone(dotted)}\n")
	print("{label} detoured {readOrNone(detoured)}\n")
	print("{label} closed exists {Directory.exists(closed)}\n")
	print("{label} closed is a directory {Directory.isDirectory(closed)}\n")
	print("{label} closed lists {listedCount(closed)}\n")

	return true
end 'probe'

function main() returns ExitCode
	let root = Directory.currentPath().join(rootName)

	if not probe("short", dir: layout(root, segments: 1)) 'short'
		return 4
	end 'short'

	if not probe("long", dir: layout(root, segments: 12)) 'long'
		return 5
	end 'long'

	print("unremoved {Directory.removeTree(root).count()}\n")

	return 0
end 'main'
```
```stdout
short past the limit false
short create true
short detour true
short mixed hello
short every slash hello
short doubled hello
short dotted hello
short detoured hello
short closed exists true
short closed is a directory true
short closed lists 2
long past the limit true
long create true
long detour true
long mixed hello
long every slash hello
long doubled hello
long dotted hello
long detoured hello
long closed exists true
long closed is a directory true
long closed lists 2
unremoved 0
```
```exitcode
0
```

<!-- test: a-segment-ending-in-a-dot-or-a-space-names-the-same-entry-at-both-lengths -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
Windows drops the dots and spaces that end a name, so a file or directory spelled with them names the entry it
would without them, and the longer spelling must name it too. The POSIX lanes keep those characters in the name and
carry no such rule, hence the marker.
```maxon
let rootName = "long-dotted-root"
let leafName = "leaf-segment"

function layout(root FilePath, segments Count) returns FilePath
	var deep = root

	for i in 0 upto segments 'segments'
		deep = deep.join("a-fairly-long-directory-segment-number-{i}")
	end 'segments'

	return deep
end 'layout'

function spell(text String) returns FilePath
	return try FilePath.from(text) otherwise panic("a spelling this case builds is not a path")
end 'spell'

function readOrNone(path FilePath) returns String
	return try File.readText(path) otherwise "unread"
end 'readOrNone'

function probe(label String, parent FilePath) returns bool
	let sep = FilePath.separator()
	let dir = parent.join(leafName)
	let text = dir.toString()

	print("{label} past the limit {text.byteLength() > 260}\n")
	print("{label} create {Directory.create(dir)}\n")

	try File.writeText(dir.join("file.txt"), content: "hello") otherwise return false

	let dottedDirectory = spell("{text}.")
	let spacedDirectory = spell("{text} ")
	let dottedFile = spell("{text}{sep}file.txt.")
	let spacedFile = spell("{text}{sep}file.txt ")
	let dottedSegment = spell("{parent.toString()}{sep}{leafName}.{sep}file.txt")

	print("{label} directory with a trailing dot {Directory.exists(dottedDirectory)}\n")
	print("{label} directory with a trailing space {Directory.exists(spacedDirectory)}\n")
	print("{label} file with a trailing dot {readOrNone(dottedFile)}\n")
	print("{label} file with a trailing space {readOrNone(spacedFile)}\n")
	print("{label} segment with a trailing dot {readOrNone(dottedSegment)}\n")

	return true
end 'probe'

function main() returns ExitCode
	let root = Directory.currentPath().join(rootName)

	if not probe("short", parent: layout(root, segments: 1)) 'short'
		return 4
	end 'short'

	if not probe("long", parent: layout(root, segments: 11)) 'long'
		return 5
	end 'long'

	print("unremoved {Directory.removeTree(root).count()}\n")

	return 0
end 'main'
```
```stdout
short past the limit false
short create true
short directory with a trailing dot true
short directory with a trailing space true
short file with a trailing dot hello
short file with a trailing space hello
short segment with a trailing dot hello
long past the limit true
long create true
long directory with a trailing dot true
long directory with a trailing space true
long file with a trailing dot hello
long file with a trailing space hello
long segment with a trailing dot hello
unremoved 0
```
```exitcode
0
```

<!-- test: a-drive-relative-and-a-rooted-path-name-the-same-entry-at-both-lengths -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
`C:name` is relative to the working directory of drive `C`, and `\name` to the root of the working directory's drive.
Both spellings are Windows-only, so the POSIX lanes carry the marker. Each names the entry the working directory's
own absolute spelling names, once below the legacy limit and once past it.
```maxon
let rootName = "long-drive-relative-root"

function spell(text String) returns FilePath
	return try FilePath.from(text) otherwise panic("a spelling this case builds is not a path")
end 'spell'

function readOrNone(path FilePath) returns String
	return try File.readText(path) otherwise "unread"
end 'readOrNone'

function probe(label String, drive String, rooted String, segments Count) returns bool
	let sep = FilePath.separator()
	var relativeName = rootName

	for i in 0 upto segments 'segments'
		relativeName = "{relativeName}{sep}a-fairly-long-directory-segment-number-{i}"
	end 'segments'

	let relativeDirectory = spell(relativeName)
	let absoluteDirectory = Directory.currentPath().joinPath(relativeDirectory)

	print("{label} past the limit {absoluteDirectory.toString().byteLength() > 260}\n")
	print("{label} create {Directory.create(relativeDirectory)}\n")

	try File.writeText(relativeDirectory.join("file.txt"), content: "hello") otherwise return false

	let driveRelative = spell("{drive}:{relativeName}{sep}file.txt")
	let rootedFile = spell("{rooted}{sep}{relativeName}{sep}file.txt")

	print("{label} relative {readOrNone(relativeDirectory.join("file.txt"))}\n")
	print("{label} drive relative {readOrNone(driveRelative)}\n")
	print("{label} rooted {readOrNone(rootedFile)}\n")
	print("{label} absolute {readOrNone(absoluteDirectory.join("file.txt"))}\n")

	return true
end 'probe'

function main() returns ExitCode
	let parts = Directory.currentPath().toString().split(":")

	if parts.count() != 2 'notDriveRooted'
		return 3
	end 'notDriveRooted'

	let drive = try parts.get(0) otherwise return 3
	let rooted = try parts.get(1) otherwise return 3

	if not probe("short", drive: drive, rooted: rooted, segments: 1) 'short'
		return 4
	end 'short'

	if not probe("long", drive: drive, rooted: rooted, segments: 12) 'long'
		return 5
	end 'long'

	print("unremoved {Directory.removeTree(spell(rootName)).count()}\n")

	return 0
end 'main'
```
```stdout
short past the limit false
short create true
short relative hello
short drive relative hello
short rooted hello
short absolute hello
long past the limit true
long create true
long relative hello
long drive relative hello
long rooted hello
long absolute hello
unremoved 0
```
```exitcode
0
```

<!-- test: a-path-of-each-length-around-the-extension-threshold-is-created-found-and-read-both-ways -->
The runtime changes the form it hands the OS at 248 units, and the buffer it resolves into holds 260 before it
grows, so the lengths around both are where an off-by-one would show. The case pads the working directory out to
226 units and then names a directory of 8 to 50 more, so every resolved length from 235 to 277 is made, found, and
read through its absolute and its relative spelling, whatever the working directory is.
```maxon
typealias Units = int(0 to 100000)

let PaddedUnits = 226
let FirstFinal = 8
let LastFinal = 50

function repeated(piece String, count Units) returns String
	var text = ""

	for _ in 0 upto count 'each'
		text = "{text}{piece}"
	end 'each'

	return text
end 'repeated'

function readOrNone(path FilePath) returns String
	return try File.readText(path) otherwise "unread"
end 'readOrNone'

function main() returns ExitCode
	let working = Directory.currentPath()
	let workingUnits = working.toString().byteLength() as Units

	if workingUnits > PaddedUnits - 1 'tooDeep'
		return 3
	end 'tooDeep'

	let pad = repeated("p", count: PaddedUnits - 1 - workingUnits)
	let absoluteBase = working.join(pad)
	let relativeBase = try FilePath.from(pad) otherwise return 4

	var created = 0 as Units
	var found = 0 as Units
	var readAbsolute = 0 as Units
	var readRelative = 0 as Units
	var tried = 0 as Units

	for finalUnits in FirstFinal to LastFinal 'lengths'
		let name = repeated("f", count: finalUnits as Units)
		let absolute = absoluteBase.join(name)
		let relative = relativeBase.join(name)

		tried = tried + 1

		if Directory.create(absolute) 'made'
			created = created + 1
		end 'made'

		if Directory.exists(relative) and Directory.isDirectory(absolute) 'there'
			found = found + 1
		end 'there'

		try File.writeText(absolute.join("file.txt"), content: "hello") otherwise continue

		if readOrNone(absolute.join("file.txt")) == "hello" 'absoluteRead'
			readAbsolute = readAbsolute + 1
		end 'absoluteRead'

		if readOrNone(relative.join("file.txt")) == "hello" 'relativeRead'
			readRelative = readRelative + 1
		end 'relativeRead'
	end 'lengths'

	print("created {created} of {tried}\n")
	print("found {found} of {tried}\n")
	print("read through the absolute spelling {readAbsolute} of {tried}\n")
	print("read through the relative spelling {readRelative} of {tried}\n")
	print("unremoved {Directory.removeTree(absoluteBase).count()}\n")

	return 0
end 'main'
```
```stdout
created 43 of 43
found 43 of 43
read through the absolute spelling 43 of 43
read through the relative spelling 43 of 43
unremoved 0
```
```exitcode
0
```
