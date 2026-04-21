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

### Static Methods

- `__ManagedDirectory.openSearch(managed)` — Opens a directory search with a pattern. Returns a `__ManagedDirectory` pointer or 0 if no matches.
- `__ManagedDirectory.exists(managed)` — Returns true if the path is an existing directory.
- `__ManagedDirectory.create(managed)` — Creates a directory. Returns true on success.
- `__ManagedDirectory.currentPath()` — Returns the current working directory as `__ManagedMemory`.

### Instance Methods

Instance methods are called on variables declared with type `__ManagedDirectory`:

- `filename()` — Returns the current match's filename as `__ManagedMemory`.
- `next()` — Advances to the next match. Returns non-zero if found, 0 if done.
- `close()` — Explicitly closes the search handle. Idempotent. Also called automatically via destructor.

### Usage Pattern

`__ManagedDirectory` is used as a struct field inside wrapper types:

```text
type DirSearch
  var dir __ManagedDirectory

  static function open(pattern String) returns DirSearch
    let result = __ManagedDirectory.openSearch(pattern.managed)
    return DirSearch{_dir: result}
  end

  function filename() returns __ManagedMemory
    return _dir.filename()
  end
end
```

## Tests

<!-- test: managed-directory.exists -->
```maxon
function main() returns ExitCode
	let cwd = __ManagedDirectory.currentPath()
	let cwdStr = String.init(cwd)
	let exists = __ManagedDirectory.exists(cwdStr.managed)
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
```maxon
function main() returns ExitCode
	let exists = __ManagedDirectory.exists("nonexistent_dir_xyz_99999".managed)
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
```maxon
function main() returns ExitCode
	let cwd = __ManagedDirectory.currentPath()
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
```maxon
function main() returns ExitCode
	let dir = __ManagedDirectory.openSearch("nonexistent_dir_xyz_88888/*".managed)
	if dir == 0 'notFound'
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

<!-- test: managed-directory.search-and-list -->
```maxon
export enum TestFileError implements Error
	openFailed
end 'TestFileError'

type TestFile
	export var file __ManagedFile

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
	export var dir __ManagedDirectory

	export static function search(pattern __ManagedMemory) returns TestDir throws TestDirError
		let handle = __ManagedDirectory.openSearch(pattern)
		if handle == 0 'fail'
			throw TestDirError.searchFailed
		end 'fail'
		return TestDir{dir: handle}
	end 'search'
end 'TestDir'

function createFile(path String, content String) throws TestFileError
	var f = try TestFile.openWrite(path.managed)
	try f.file.write(content.managed) otherwise 'err'
		f.file.close()
		panic("write failed")
	end 'err'
	f.file.close()
end 'createFile'

function main() returns ExitCode
	// Create a temp directory (may already exist from previous run)
	let dirPath = "test_managed_dir_search"
	if not __ManagedDirectory.exists(dirPath.managed) 'needCreate'
		let created = __ManagedDirectory.create(dirPath.managed)
		if not created 'createFail'
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
	var dir = try TestDir.search("{dirPath}/*".managed) otherwise 'searchFail'
		print("search failed")
		return 2
	end 'searchFail'

	var fileCount = 0
	var nameManaged = dir.dir.filename()
	var name = String.init(nameManaged)
	if name != "." and name != ".." 'notDot1'
		fileCount = fileCount + 1
	end 'notDot1'
	while dir.dir.next() != 0 'loop'
		nameManaged = dir.dir.filename()
		name = String.init(nameManaged)
		if name != "." and name != ".." 'notDot2'
			fileCount = fileCount + 1
		end 'notDot2'
	end 'loop'
	dir.dir.close()

	// Clean up
	try __ManagedFile.delete("{dirPath}/file1.txt".managed) otherwise 'del1Err'
		return 4
	end 'del1Err'
	try __ManagedFile.delete("{dirPath}/file2.txt".managed) otherwise 'del2Err'
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

<!-- test: managed-directory.error-direct-construction -->
```maxon
function main() returns ExitCode
	let d = __ManagedDirectory{_block: 0}
	return 0
end 'main'
```
```maxoncstderr
error E3072: specs/fragments/managed-directory/managed-directory.error-direct-construction.test:3:29: '__ManagedDirectory' is a compiler builtin type and cannot be constructed directly
```
