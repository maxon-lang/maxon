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
  var _dir __ManagedDirectory

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
	let cwdStr = String{managed: cwd, _iterPos: 0}
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
	let cwdStr = String{managed: cwd, _iterPos: 0}
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
type TestFile
	export var _file __ManagedFile
end 'TestFile'

type TestDir
	export var _dir __ManagedDirectory
end 'TestDir'

function createFile(path String, content String)
	let wr = __ManagedFile.openWrite(path.managed)
	var f = TestFile{_file: wr}
	let written = f._file.write(content.managed)
	f._file.close()
	if written < 0 'err'
		panic("write failed")
	end 'err'
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

	createFile("{dirPath}/file1.txt", content: "a")
	createFile("{dirPath}/file2.txt", content: "b")

	// Search the directory
	let searchResult = __ManagedDirectory.openSearch("{dirPath}/*".managed)
	if searchResult == 0 'searchFail'
		print("search failed")
		return 2
	end 'searchFail'
	var dir = TestDir{_dir: searchResult}

	var fileCount = 0
	var nameManaged = dir._dir.filename()
	var name = String{managed: nameManaged, _iterPos: 0}
	if name != "." and name != ".." 'notDot1'
		fileCount = fileCount + 1
	end 'notDot1'
	while dir._dir.next() != 0 'loop'
		nameManaged = dir._dir.filename()
		name = String{managed: nameManaged, _iterPos: 0}
		if name != "." and name != ".." 'notDot2'
			fileCount = fileCount + 1
		end 'notDot2'
	end 'loop'
	dir._dir.close()

	// Clean up
	let del1 = __ManagedFile.delete("{dirPath}/file1.txt".managed)
	let del2 = __ManagedFile.delete("{dirPath}/file2.txt".managed)
	if del1 != 0 or del2 != 0 'delErr'
		return 4
	end 'delErr'

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
