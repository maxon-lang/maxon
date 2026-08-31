---
feature: managed-directory
status: experimental
keywords: [directory, managed, __ManagedDirectory, RAII, search]
category: type-system
---

# __ManagedDirectory

## Documentation

### Overview

`__ManagedDirectory` is a compiler builtin type that wraps a Windows FindFirstFile/FindNextFile search handle with automatic cleanup via a destructor when the last reference goes out of scope. It replaces the raw `__Builtins` directory functions with a managed, RAII-based API.

### Type Structure

`__ManagedDirectory` has a single field:
- `_block` (int) — Pointer to a heap-allocated block containing the search HANDLE and WIN32_FIND_DATAA

### Static Methods (throwing)

- `__ManagedDirectory.openSearch(managed)` — Opens a directory search with a pattern. Throws `__ManagedDirectoryError.openSearchFailed` if the path does not exist or access is denied.
- `__ManagedDirectory.create(managed)` — Creates a directory. Throws `__ManagedDirectoryError.createFailed` on failure.
- `__ManagedDirectory.currentPath()` — Returns the current working directory as `__ManagedMemory`. Throws `__ManagedDirectoryError.currentPathFailed` on failure.

### Static Methods (non-throwing)

- `__ManagedDirectory.exists(managed)` — Returns true if the path is an existing directory.

### ⚠ WHERE THE CASES BELOW PUT THE DIRECTORIES THEY MAKE, AND WHY THEY CANNOT REMOVE THEM

Six cases here CREATE a directory, and **shv2 has no directory-REMOVAL runtime entry at all** — the OS
operations it lowers are `CreateDirectoryA`, `GetFileAttributesA`, `FindFirstFileA`/`FindNextFileA`,
`GetCurrentDirectoryA` and `DeleteFileA`, and `DeleteFileA` refuses a directory. So a case can delete every
FILE it wrote and cannot delete the directory that held them: whatever it makes outlives the suite run.

⚠⚠ **UPDATED BY `R4.8` (2026-08-03): THE PREFIX BELOW IS NOW REDUNDANT, AND THE REASON IT WAS ADDED IS
NOW THE HARNESS'S JOB.** `R4.8` found that this runner spawned every test binary with **no working
directory at all**, so a case inherited the SUITE's cwd — the checkout root — which is exactly why
these six had to spell `temp/` themselves to stay out of it. The runner now spawns every test binary in
**`<checkoutRoot>/temp/`** (`SpecTestRunner.RunWorkingSubdirName`), matching the bootstrap's
long-standing contract, so a bare relative path already lands under `temp/`. ⇒ **These six now resolve
to `temp/temp/…`** — still gitignored, still passing, and harmless, but it is a second mechanism doing
one job. Dropping the prefix is safe once someone re-runs this file; it was left in place by `R4.8`
deliberately, because churning six PASSING cases buys no coverage and this file already diverges from
`/specs` by design (it carries nine shv2-authored cases beyond the canonical ten). **The paragraph
below is kept for its reasoning, which is still correct about WHY the root must stay clean — only its
claim that this prefix is what keeps it clean is now stale.**

⇒ Every one of them makes it under **`temp/`, which is gitignored**, rather than at the repo ROOT. That is
not cosmetic: the project-root resolver walks UP, so a stray directory at the root is inside the project
every later build resolves, and six of them were being left there on every suite run. Adding a
`__ManagedDirectory.remove` is the real cure and is its own rung; until it exists this is the whole of what
a spec can do.

⚠ **The scratch root is SHARED** — by these six cases and by every parallel spec worker — so its creation
must not race. Each case attempts the create and treats a LOSS as ordinary (`CreateDirectoryA` reports
`ERROR_ALREADY_EXISTS`); the `exists` probe in the handler is the only gate. The per-case directory below it
needs no such care: one case owns each name.

### Instance Methods (throwing)

- `next()` — Advance-first iteration. Advances the cursor to the next REAL entry, skipping the `.` and `..` pseudo-entries, and returns non-zero if one was found or 0 when the iteration is complete. `openSearch` does not pre-load an entry, so callers iterate `while next() != 0 { use filename() }`. Throws `__ManagedDirectoryError.nextFailed` on OS error.

### Instance Methods (non-throwing)

- `filename()` — Returns the current entry's filename as `__ManagedMemory`. Only valid after a `next()` call that returned non-zero (before the first `next()` there is no current entry). The runtime owns all dot-filtering, so `filename()` never returns `.` or `..`.
- `close()` — Explicitly closes the search handle. Idempotent. Also called automatically via destructor.

### Usage Pattern

`__ManagedDirectory` is used as a struct field inside wrapper types:

```text
type DirSearch
  var dir as __ManagedDirectory

  static function open(pattern String) returns DirSearch throws SearchError
    let result = try __ManagedDirectory.openSearch(pattern.toByteArray().managed) otherwise throw SearchError.notFound
    return DirSearch{dir: result}
  end

  function filename() returns __ManagedMemory
    return dir.filename()
  end
end
```

## Tests

<!-- test: managed-directory.exists -->
<!-- targets: x64-windows, arm64-macos -->
```maxon
function main() returns ExitCode
	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	let cwdStr = String.init(cwd)
	let exists = __ManagedDirectory.exists(cwdStr.toByteArray().managed)
	if exists 'check'
		return 42
	end 'check'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.not-exists -->
<!-- targets: x64-windows, arm64-macos -->
```maxon
function main() returns ExitCode
	let exists = __ManagedDirectory.exists("nonexistent_dir_xyz_99999".toByteArray().managed)
	if not exists 'check'
		return 42
	end 'check'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.current-path -->
<!-- targets: x64-windows, arm64-macos -->
```maxon
function main() returns ExitCode
	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	let cwdStr = String.init(cwd)
	if cwdStr.count() > 0 'hasPath'
		return 42
	end 'hasPath'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.open-search-nonexistent -->
<!-- targets: x64-windows, arm64-macos -->
```maxon
function main() returns ExitCode
	try __ManagedDirectory.openSearch("nonexistent_dir_xyz_88888/*".toByteArray().managed) otherwise 'notFound'
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

<!-- test: managed-directory.open-search-throws -->
<!-- targets: x64-windows, arm64-macos -->
```maxon
function main() returns ExitCode
	try __ManagedDirectory.openSearch("nonexistent_dir_xyz_88888_throws/*".toByteArray().managed) otherwise 'err'
		return 42
	end 'err'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.create-throws -->
<!-- targets: x64-windows, arm64-macos -->
```maxon
function main() returns ExitCode
	try __ManagedDirectory.create("nonexistent_parent_xyz_88888/child".toByteArray().managed) otherwise 'err'
		return 42
	end 'err'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.next-without-try -->
<!-- targets: x64-windows, arm64-macos -->
```maxon
function main() returns ExitCode
	let dir = try __ManagedDirectory.openSearch("./*".toByteArray().managed) otherwise return 1
	_ = dir.next()
	return 0
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/managed-directory/managed-directory.next-without-try.test:4:10: throwing function requires try: 'next'
```

<!-- test: managed-directory.search-and-list -->
<!-- targets: x64-windows, arm64-macos -->
```maxon
export enum TestFileError implements Error
	openFailed
end 'TestFileError'

type TestFile
	export var file as __ManagedFile

	export static function openWrite(path __ManagedMemory) returns TestFile throws TestFileError
		let wr = try __ManagedFile.openWrite(path) otherwise 'f'
			throw TestFileError.openFailed
		end 'f'
		return TestFile{file: wr}
	end 'openWrite'
end 'TestFile'

export enum TestDirError implements Error
	searchFailed
end 'TestDirError'

type TestDir
	export var dir as __ManagedDirectory

	export static function search(pattern __ManagedMemory) returns TestDir throws TestDirError
		let handle = try __ManagedDirectory.openSearch(pattern) otherwise 'fail'
			throw TestDirError.searchFailed
		end 'fail'
		return TestDir{dir: handle}
	end 'search'
end 'TestDir'

function createFile(path String, content String) throws TestFileError
	var f = try TestFile.openWrite(path.toByteArray().managed)
	try f.file.write(content.toByteArray().managed) otherwise 'err'
		f.file.close()
		panic("write failed")
	end 'err'
	f.file.close()
end 'createFile'

function main() returns ExitCode
	// Create a temp directory (may already exist from previous run)
	// The scratch root is shared with every other case here and with every parallel spec
	// worker, so losing the create race is ordinary — see this file's Documentation.
	let scratchRoot = "temp"
	try __ManagedDirectory.create(scratchRoot.toByteArray().managed) otherwise 'anotherWorkerMadeIt'
		if not __ManagedDirectory.exists(scratchRoot.toByteArray().managed) 'noScratchRoot'
			return 1
		end 'noScratchRoot'
	end 'anotherWorkerMadeIt'

	let dirPath = "temp/test_managed_dir_search"
	if not __ManagedDirectory.exists(dirPath.toByteArray().managed) 'needCreate'
		try __ManagedDirectory.create(dirPath.toByteArray().managed) otherwise 'createFail'
			print("create failed")
			return 1
		end 'createFail'
	end 'needCreate'

	try createFile("{dirPath}/file1.txt", content: "a") otherwise 'c1Err'
		return 5
	end 'c1Err'
	try createFile("{dirPath}/file2.txt", content: "b") otherwise 'c2Err'
		return 5
	end 'c2Err'

	// Search the directory
	var dir = try TestDir.search("{dirPath}/*".toByteArray().managed) otherwise 'searchFail'
		print("search failed")
		return 2
	end 'searchFail'

	var fileCount = 0
	while (try dir.dir.next() otherwise return 3) != 0 'loop'
		fileCount = fileCount + 1
	end 'loop'
	dir.dir.close()

	// Clean up
	try __ManagedFile.delete("{dirPath}/file1.txt".toByteArray().managed) otherwise 'del1Err'
		return 4
	end 'del1Err'
	try __ManagedFile.delete("{dirPath}/file2.txt".toByteArray().managed) otherwise 'del2Err'
		return 4
	end 'del2Err'

	if fileCount == 2 'checkCount'
		return 42
	end 'checkCount'
	print("unexpected count: {fileCount}")
	return 3
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.open-search-not-found-variant -->
<!-- targets: x64-windows, arm64-macos -->

The errno→variant mapping ensures that opening a search on a path that does
not exist routes to the `notFound` arm (rather than the catch-all
`openSearchFailed`). Backed by `gt->io_error_code` populated by the runtime
sync worker (Win32 ERROR_FILE_NOT_FOUND=2 / ERROR_PATH_NOT_FOUND=3, POSIX ENOENT=2).

```maxon
function main() returns ExitCode
	var result = 0
	try __ManagedDirectory.openSearch("nonexistent_phaseB_search_xyz/*".toByteArray().managed) otherwise (e) 'h'
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

<!-- test: managed-directory.error-direct-construction -->
<!-- targets: x64-windows, arm64-macos -->
```maxon
function main() returns ExitCode
	let d = __ManagedDirectory{_block: 0}
	return 0
end 'main'
```
```maxoncstderr
error E3072: specs/fragments/managed-directory/managed-directory.error-direct-construction.test:3:29: '__ManagedDirectory' is a compiler builtin type and cannot be constructed directly
```

### shv2's own cases

⚠ A `### ` heading, not a `## ` one: `SpecParser`'s active-test region runs from
`## Tests` to the NEXT `## ` heading, so a second-level heading here would shelve
every case below it silently.

The nine cases below are shv2's own, added by R4.3's adversarial probing (seven)
and by its independent review (the last two). Each pins something the ten ported
cases above do not reach — and every one of them was written because a probe
found the mechanism, not to restate a passing one.

<!-- test: managed-directory.next-does-not-skip-the-first-match -->
<!-- targets: x64-windows, arm64-macos -->

⭐ **The advance-first PENDING flag.** `FindFirstFileA` does not merely open a
search, it returns the FIRST entry — so a `next()` that fetches immediately
loses it. `search-and-list` above cannot catch that: a `dir/*` pattern's first
two entries are `.` and `..`, which the runtime's dot filter discards anyway.
This searches `*.txt` in a directory whose first match is a REAL file.
MEASURED by sabotage: with the flag cleared at the open, this returns 1.

```maxon
export enum ProbeError implements Error
	failed
end 'ProbeError'

function writeFile(path String) throws ProbeError
	var f = try __ManagedFile.openWrite(path.toByteArray().managed) otherwise 'openFail'
		throw ProbeError.failed
	end 'openFail'
	try f.write("x".toByteArray().managed) otherwise 'writeFail'
		f.close()
		throw ProbeError.failed
	end 'writeFail'
	f.close()
end 'writeFile'

function main() returns ExitCode
	// The scratch root is shared with every other case here and with every parallel spec
	// worker, so losing the create race is ordinary — see this file's Documentation.
	let scratchRoot = "temp"
	try __ManagedDirectory.create(scratchRoot.toByteArray().managed) otherwise 'anotherWorkerMadeIt'
		if not __ManagedDirectory.exists(scratchRoot.toByteArray().managed) 'noScratchRoot'
			return 1
		end 'noScratchRoot'
	end 'anotherWorkerMadeIt'

	let dirPath = "temp/test_md_first_match"
	if not __ManagedDirectory.exists(dirPath.toByteArray().managed) 'needCreate'
		try __ManagedDirectory.create(dirPath.toByteArray().managed) otherwise return 1
	end 'needCreate'
	try writeFile("{dirPath}/alpha.txt") otherwise return 2
	try writeFile("{dirPath}/beta.txt") otherwise return 2

	var dir = try __ManagedDirectory.openSearch("{dirPath}/*.txt".toByteArray().managed) otherwise return 3
	var count = 0
	while (try dir.next() otherwise return 4) != 0 'loop'
		count = count + 1
	end 'loop'
	dir.close()

	try __ManagedFile.delete("{dirPath}/alpha.txt".toByteArray().managed) otherwise return 5
	try __ManagedFile.delete("{dirPath}/beta.txt".toByteArray().managed) otherwise return 5
	if count == 2 'both'
		return 42
	end 'both'
	return count
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.filename-round-trip -->
<!-- targets: x64-windows, arm64-macos -->

⭐ **`filename()` — which NO ported case reaches.** It answers with a FRESH
owned `__ManagedMemory` copy rather than a pointer into the find block (the
bootstrap's shape, which the next `next()` invalidates), so the name survives
being read back into a `String`. The dot filter is checked here too: `.` and
`..` never reach the caller, so every name this sees is a real entry.

```maxon
export enum ProbeError implements Error
	failed
end 'ProbeError'

function writeFile(path String) throws ProbeError
	var f = try __ManagedFile.openWrite(path.toByteArray().managed) otherwise 'openFail'
		throw ProbeError.failed
	end 'openFail'
	try f.write("x".toByteArray().managed) otherwise 'writeFail'
		f.close()
		throw ProbeError.failed
	end 'writeFail'
	f.close()
end 'writeFile'

function main() returns ExitCode
	// The scratch root is shared with every other case here and with every parallel spec
	// worker, so losing the create race is ordinary — see this file's Documentation.
	let scratchRoot = "temp"
	try __ManagedDirectory.create(scratchRoot.toByteArray().managed) otherwise 'anotherWorkerMadeIt'
		if not __ManagedDirectory.exists(scratchRoot.toByteArray().managed) 'noScratchRoot'
			return 1
		end 'noScratchRoot'
	end 'anotherWorkerMadeIt'

	let dirPath = "temp/test_md_filename"
	if not __ManagedDirectory.exists(dirPath.toByteArray().managed) 'needCreate'
		try __ManagedDirectory.create(dirPath.toByteArray().managed) otherwise return 1
	end 'needCreate'
	try writeFile("{dirPath}/only.txt") otherwise return 2

	var dir = try __ManagedDirectory.openSearch("{dirPath}/*.txt".toByteArray().managed) otherwise return 3
	var found = 0
	while (try dir.next() otherwise return 4) != 0 'loop'
		let name = String.init(dir.filename())
		if name == "only.txt" 'isTheFile'
			found = found + 1
		end 'isTheFile'
	end 'loop'
	dir.close()

	try __ManagedFile.delete("{dirPath}/only.txt".toByteArray().managed) otherwise return 5
	if found == 1 'exactlyOne'
		return 42
	end 'exactlyOne'
	return 6
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.next-after-close-throws-closed -->
<!-- targets: x64-windows, arm64-macos -->

⚠ **shv2's ONE deliberate divergence from both references.** Neither throws
`closed` — and neither guards, so `close()` followed by `next()` reaches Win32
with a NULL handle while the pending flag still says an entry is waiting, and
the search's FIRST entry is replayed as if it were found. `__ManagedFile`'s
`size`/`read`/`write` already throw `__ManagedFileError.closed` from the
identical guard, so this is the family's established contract in shv2.

```maxon
function main() returns ExitCode
	var dir = try __ManagedDirectory.openSearch("./*".toByteArray().managed) otherwise return 1
	dir.close()
	try dir.next() otherwise (e) 'handler'
		match e 'kind'
			closed then return 42
			default panic("expected closed")
		end 'kind'
	end 'handler'
	return 2
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.exists-discriminates-file-from-directory -->
<!-- targets: x64-windows, arm64-macos -->

`exists()` asks whether the path is a DIRECTORY, so an existing plain file is
`false` — a `GetFileAttributesA` that merely succeeded is not the answer. The
`not-exists` case above only covers a path that names nothing.

```maxon
export enum ProbeError implements Error
	failed
end 'ProbeError'

function writeFile(path String) throws ProbeError
	var f = try __ManagedFile.openWrite(path.toByteArray().managed) otherwise 'openFail'
		throw ProbeError.failed
	end 'openFail'
	try f.write("x".toByteArray().managed) otherwise 'writeFail'
		f.close()
		throw ProbeError.failed
	end 'writeFail'
	f.close()
end 'writeFile'

function main() returns ExitCode
	let path = "test_md_plain_file.txt"
	try writeFile(path) otherwise return 1
	let isDir = __ManagedDirectory.exists(path.toByteArray().managed)
	try __ManagedFile.delete(path.toByteArray().managed) otherwise return 2
	if isDir 'aFileIsNotADirectory'
		return 3
	end 'aFileIsNotADirectory'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.create-existing-is-create-failed -->
<!-- targets: x64-windows, arm64-macos -->

`CreateDirectoryA` on a path that already exists reports
`ERROR_ALREADY_EXISTS`, which is neither of the two errno codes the shared
classification names — so it takes this operation's catch-all. The
`create-throws` case above reaches the same throw through a missing PARENT,
which is a different Win32 error, so only this one pins the catch-all.

```maxon
function main() returns ExitCode
	// The scratch root is shared with every other case here and with every parallel spec
	// worker, so losing the create race is ordinary — see this file's Documentation. It is
	// NOT the subject: the catch-all below is pinned on `dirPath`'s SECOND create.
	let scratchRoot = "temp"
	try __ManagedDirectory.create(scratchRoot.toByteArray().managed) otherwise 'anotherWorkerMadeIt'
		if not __ManagedDirectory.exists(scratchRoot.toByteArray().managed) 'noScratchRoot'
			return 1
		end 'noScratchRoot'
	end 'anotherWorkerMadeIt'

	let dirPath = "temp/test_md_already_there"
	if not __ManagedDirectory.exists(dirPath.toByteArray().managed) 'needCreate'
		try __ManagedDirectory.create(dirPath.toByteArray().managed) otherwise return 1
	end 'needCreate'
	try __ManagedDirectory.create(dirPath.toByteArray().managed) otherwise (e) 'handler'
		match e 'kind'
			createFailed then return 42
			default panic("expected createFailed")
		end 'kind'
	end 'handler'
	return 2
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.auto-close -->
<!-- targets: x64-windows, arm64-macos -->

The RAII half: a search opened and NEVER closed explicitly, reclaimed only by
the destructor at the scope exit. The exit code proves the memory half (a leaked
find block or box fails the exit-101 gate); the OS SEARCH HANDLE half is
structurally invisible to that gate and was measured separately, by process
handle count — 20,000 unclosed searches leave 66 handles live, and 20,060 with
the destructor's `close` removed.

```maxon
function main() returns ExitCode
	var i = 0
	while i < 200 'loop'
		var dir = try __ManagedDirectory.openSearch("./*".toByteArray().managed) otherwise return 1
		_ = try dir.next() otherwise return 2
		i = i + 1
	end 'loop'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.error-enum-name-is-reserved -->
<!-- targets: x64-windows, arm64-macos -->

⚠ **SEEDED IS NOT RESERVED** — the R4.4 trap, verified rather than assumed.
`__ManagedDirectoryError` is seeded into the enum registry unconditionally, and
a user declaration that could DISPLACE the seed would capture the runtime's
ordinals and reroute every handler arm with no diagnostic anywhere. The `__`
prefix is what makes that impossible here, and this is the case that says so.

```maxon
export enum __ManagedDirectoryError implements Error
	somethingElse
end '__ManagedDirectoryError'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2051: <fragment>:2:13: identifier '__ManagedDirectoryError' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: managed-directory.dotfiles-are-entries-not-dot-pseudo-entries -->
<!-- targets: x64-windows, arm64-macos -->

⭐ **THE DOT FILTER IS THREE BYTES, AND ALL SEVENTEEN CASES ABOVE PASS IF IT IS
ONE.** `.` is byte0 `.` + NUL, `..` is `.` `.` + NUL — so a filter that tested
only the leading byte would hide **every dotfile in the directory** while still
discarding exactly the two pseudo-entries the other cases care about. Nothing
above can see the difference: `search-and-list` and
`next-does-not-skip-the-first-match` list directories that contain no dotfile,
and `filename-round-trip` searches `*.txt`. This one lists a directory holding
`.gitignore` (byte1 is not NUL), `..config` (byte1 IS `.`, byte2 is not NUL) and
a plain file, and requires all three back. MEASURED: with `dot1` routed
unconditionally back to the fetch, this returns 1 and the other seventeen stay
green.

```maxon
export enum ProbeError implements Error
	failed
end 'ProbeError'

function writeFile(path String) throws ProbeError
	var f = try __ManagedFile.openWrite(path.toByteArray().managed) otherwise 'openFail'
		throw ProbeError.failed
	end 'openFail'
	try f.write("x".toByteArray().managed) otherwise 'writeFail'
		f.close()
		throw ProbeError.failed
	end 'writeFail'
	f.close()
end 'writeFile'

function main() returns ExitCode
	// The scratch root is shared with every other case here and with every parallel spec
	// worker, so losing the create race is ordinary — see this file's Documentation.
	let scratchRoot = "temp"
	try __ManagedDirectory.create(scratchRoot.toByteArray().managed) otherwise 'anotherWorkerMadeIt'
		if not __ManagedDirectory.exists(scratchRoot.toByteArray().managed) 'noScratchRoot'
			return 1
		end 'noScratchRoot'
	end 'anotherWorkerMadeIt'

	let dirPath = "temp/test_md_dotfiles"
	if not __ManagedDirectory.exists(dirPath.toByteArray().managed) 'needCreate'
		try __ManagedDirectory.create(dirPath.toByteArray().managed) otherwise return 1
	end 'needCreate'
	try writeFile("{dirPath}/.gitignore") otherwise return 2
	try writeFile("{dirPath}/..config") otherwise return 2
	try writeFile("{dirPath}/plain.txt") otherwise return 2

	var dir = try __ManagedDirectory.openSearch("{dirPath}/*".toByteArray().managed) otherwise return 3
	var seen = 0
	var dots = 0
	while (try dir.next() otherwise return 4) != 0 'loop'
		let name = String.init(dir.filename())
		if name == ".gitignore" 'hidden'
			seen = seen + 1
		end 'hidden'
		if name == "..config" 'doubleLeading'
			seen = seen + 1
		end 'doubleLeading'
		if name == "plain.txt" 'ordinary'
			seen = seen + 1
		end 'ordinary'
		if name == "." 'singleDot'
			dots = dots + 1
		end 'singleDot'
		if name == ".." 'doubleDot'
			dots = dots + 1
		end 'doubleDot'
	end 'loop'
	dir.close()

	try __ManagedFile.delete("{dirPath}/.gitignore".toByteArray().managed) otherwise return 5
	try __ManagedFile.delete("{dirPath}/..config".toByteArray().managed) otherwise return 5
	try __ManagedFile.delete("{dirPath}/plain.txt".toByteArray().managed) otherwise return 5

	if dots != 0 'pseudoEntryLeaked'
		return 6
	end 'pseudoEntryLeaked'
	if seen == 3 'allThree'
		return 42
	end 'allThree'
	return seen
end 'main'
```
```exitcode
42
```

<!-- test: managed-directory.eof-and-close-are-both-idempotent -->
<!-- targets: x64-windows, arm64-macos -->

Two contracts nothing above states. **EOF is a plain 0 and stays one**: an empty
directory's very first `next()` is the end of the iteration, and a `next()` past
it must answer 0 again rather than throw `nextFailed` or replay — `FindNextFileA`
keeps reporting `ERROR_NO_MORE_FILES`, and only the `GetLastError` arm makes that
an ordinary end. **`close()` is idempotent**: the spec says so, `__md_destruct`
depends on it (an explicit `close()` followed by the scope-exit drop calls it
twice), and `search-and-list` exercises close-then-drop but never close-then-close
in one program.

```maxon
function main() returns ExitCode
	// The scratch root is shared with every other case here and with every parallel spec
	// worker, so losing the create race is ordinary — see this file's Documentation.
	let scratchRoot = "temp"
	try __ManagedDirectory.create(scratchRoot.toByteArray().managed) otherwise 'anotherWorkerMadeIt'
		if not __ManagedDirectory.exists(scratchRoot.toByteArray().managed) 'noScratchRoot'
			return 1
		end 'noScratchRoot'
	end 'anotherWorkerMadeIt'

	let dirPath = "temp/test_md_empty_search"
	if not __ManagedDirectory.exists(dirPath.toByteArray().managed) 'needCreate'
		try __ManagedDirectory.create(dirPath.toByteArray().managed) otherwise return 1
	end 'needCreate'

	var dir = try __ManagedDirectory.openSearch("{dirPath}/*".toByteArray().managed) otherwise return 2
	let first = try dir.next() otherwise return 3
	if first != 0 'emptyDirectoryHasNoRealEntry'
		return 4
	end 'emptyDirectoryHasNoRealEntry'
	let second = try dir.next() otherwise return 5
	if second != 0 'eofStaysEof'
		return 6
	end 'eofStaysEof'

	dir.close()
	dir.close()
	return 42
end 'main'
```
```exitcode
42
```
