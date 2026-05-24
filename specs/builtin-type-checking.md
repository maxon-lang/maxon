---
feature: builtin-type-checking
status: experimental
keywords: [builtin, type-checking, __ManagedFile, __ManagedMemory, __ManagedDirectory, __ManagedSocket]
category: type-system
---

# Builtin Type Checking

## Documentation

### Overview

Compiler builtin methods on `__ManagedFile`, `__ManagedSocket`, `__ManagedDirectory`, `__ManagedMemory`, `__ManagedList`, and `__ManagedListNode` validate argument types at compile time, just like regular function calls.

## Tests

<!-- test: builtin-type-checking.error-managed-file-open-read-int -->
```maxon
function main() returns ExitCode
	let result = __ManagedFile.openRead(0)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtin-type-checking/builtin-type-checking.error-managed-file-open-read-int.test:3:29: argument type mismatch for 'path': expected '__ManagedMemory', got 'int'
```

<!-- test: builtin-type-checking.error-managed-file-write-int -->
```maxon
export enum TestFileError implements Error
	openFailed
end 'TestFileError'

type TestFile
	export var file as __ManagedFile

	export static function open(path String) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openRead(path.managed) otherwise 'f'
			throw TestFileError.openFailed
		end 'f'
		return TestFile{file: handle}
	end 'open'
end 'TestFile'

function main() returns ExitCode throws TestFileError
	let f = try TestFile.open("test.txt")
	let written = try f.file.write(42)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtin-type-checking/builtin-type-checking.error-managed-file-write-int.test:19:27: argument type mismatch for 'managed': expected '__ManagedMemory', got 'int'
```

<!-- test: builtin-type-checking.error-managed-directory-open-search-int -->
```maxon
function main() returns ExitCode
	let result = __ManagedDirectory.openSearch(0)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtin-type-checking/builtin-type-checking.error-managed-directory-open-search-int.test:3:34: argument type mismatch for 'path': expected '__ManagedMemory', got 'int'
```

<!-- test: builtin-type-checking.error-managed-memory-set-length-string -->
```maxon
function main() returns ExitCode
	let managed = try __ManagedMemory.create(10, 8) otherwise panic("create failed")
	managed.setLength("hello")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtin-type-checking/builtin-type-checking.error-managed-memory-set-length-string.test:4:10: argument type mismatch for 'newLength': expected 'int', got 'String'
```

<!-- test: builtin-type-checking.error-managed-memory-append-int -->
```maxon
function main() returns ExitCode
	let managed = try __ManagedMemory.create(10, 8) otherwise panic("create failed")
	managed.append(42)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtin-type-checking/builtin-type-checking.error-managed-memory-append-int.test:4:10: argument type mismatch for 'other': expected '__ManagedMemory', got 'int'
```

<!-- test: builtin-type-checking.error-managed-socket-tcp-connect-int -->
```maxon
function main() returns ExitCode
	let result = __ManagedSocket.tcpConnect(0, 80)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtin-type-checking/builtin-type-checking.error-managed-socket-tcp-connect-int.test:3:31: argument type mismatch for 'host': expected '__ManagedMemory', got 'int'
```
