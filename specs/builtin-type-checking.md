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

The same type-checking applies to `__Builtins.*` runtime intrinsics: every
parameter is declared with a concrete type (`i64`, `cstring`,
`__ManagedMemory`), and the parser rejects arguments that don't match. This
catches the class of bug where a `__ManagedMemory` is passed to a runtime
helper that expects a NUL-terminated cstring — without the check, the
runtime would walk past the buffer end when the byte count fills the
allocated capacity.

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

### `cstring` parameters

`__Builtins.*` entries whose underlying runtime treats a buffer as a
NUL-terminated UTF-8 cstring (it strlens the pointer) declare the parameter
as `cstring`, not `__ManagedMemory`. The distinction matters because
`__ManagedMemory` is sized by its length header — its buffer is not
guaranteed to have a `\0` at `buffer[length]` when the byte count exactly
fills the allocated capacity. Passing a `__ManagedMemory` to a runtime that
strlens the pointer reads past the buffer end into adjacent heap.

Callers convert via `mm.toCString()`, which checks `buffer[length] == 0`
and COWs if not. The type check is what makes the conversion mandatory at
the source level — it is the gap that hid the original Subprocess `cwd`
NUL-termination bug.

<!-- test: builtin-type-checking.error-subprocess-resolve-on-path-managed -->
```maxon
function main() returns ExitCode
	let s = "ls"
	let result = __Builtins.subprocessResolveOnPath(s.managed)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtin-type-checking/builtin-type-checking.error-subprocess-resolve-on-path-managed.test:4:50: type mismatch: __Builtins.maxon_subprocess_resolve_on_path argument 0 expects 'cstring' but got 'ByteMemory'
```

<!-- test: builtin-type-checking.error-subprocess-resolve-on-path-int -->
```maxon
function main() returns ExitCode
	let result = __Builtins.subprocessResolveOnPath(42)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtin-type-checking/builtin-type-checking.error-subprocess-resolve-on-path-int.test:3:50: type mismatch: __Builtins.maxon_subprocess_resolve_on_path argument 0 expects 'cstring' but got 'int'
```

<!-- test: builtin-type-checking.error-subprocess-get-pid-cstring -->
```maxon
function main() returns ExitCode
	let s = "abc"
	let cs = s.managed.toCString()
	let result = __Builtins.subprocessGetPid(cs)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtin-type-checking/builtin-type-checking.error-subprocess-get-pid-cstring.test:5:43: type mismatch: __Builtins.maxon_subprocess_get_pid argument 0 expects 'i64' but got 'cstring'
```

<!-- test: builtin-type-checking.subprocess-resolve-on-path-cstring -->
```maxon
function main() returns ExitCode
	// Routing the path through `String.cstr()` satisfies the cstring type
	// check for `subprocessResolveOnPath`; the call itself may or may not
	// resolve a real binary depending on the host PATH, which is irrelevant
	// to this test.
	let s = "__nonexistent_binary_for_type_check__"
	let result = __Builtins.subprocessResolveOnPath(s.cstr())
	let isNull = __Builtins.managedIsNull(result)
	// Convert isNull (0 or 1) into 0 regardless — the test just verifies the
	// call compiles and runs once `.cstr()` is in the path.
	if isNull == 0 'zero'
		return 0
	end 'zero'
	return 0
end 'main'
```
```exitcode
0
```
