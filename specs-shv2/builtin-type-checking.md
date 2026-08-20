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
		let handle = try __ManagedFile.openRead(path.toByteArray().managed) otherwise 'f'
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

<!-- disabled-test: builtin-type-checking.error-managed-socket-tcp-connect-int -->
<!-- No `__ManagedSocket` in shv2 — the type is on no rung of PLAN.md. -->
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

For a `String` the conversion has a name of its own: `s.cstr()`. It is the
whole of `mm.toCString()` with the buffer already in hand, and since Stage 4c
of the SSO plan it is also the spelling to reach for: `String` no longer
exports its raw-buffer field, so reaching through that field to call
`toCString()` on it no longer compiles.
Code that genuinely needs the bytes as a `__ManagedMemory` asks for
`s.toByteArray().managed`, which hands back an INDEPENDENT view — writing
through it cannot alter the string.

<!-- disabled-test: builtin-type-checking.error-subprocess-resolve-on-path-managed -->
<!-- shv2 resolves `cstring` to a machine word rather than to a `ValueTypeTag` of its own — `Parser.parseTypeReference`'s `cstringPointer` arm, whose comment names exactly this situation as the trigger for changing that. It DOES refuse this call (the argument is a `ByteArray`, not a word), but as `E3005 '__Builtins.subprocessResolveOnPath' requires a int, but its argument is ByteArray` at the CALLEE token, where the reference says `expects 'cstring' but got 'ByteBuffer'` at the argument. Reported to the coordinator as a candidate rung; on no rung of PLAN.md today. -->
```maxon
function main() returns ExitCode
	let s = "ls"
	// A `__ManagedMemory` reached the only way user code can reach a string's bytes: an independent
	// `toByteArray()` view. It is still the wrong TYPE here, which is the point of the test.
	let result = __Builtins.subprocessResolveOnPath(s.toByteArray().managed)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtin-type-checking/builtin-type-checking.error-subprocess-resolve-on-path-managed.test:6:50: type mismatch: __Builtins.maxon_subprocess_resolve_on_path argument 0 expects 'cstring' but got 'ByteBuffer'
```

<!-- disabled-test: builtin-type-checking.error-subprocess-resolve-on-path-int -->
<!-- Same missing mechanism, and here it is not a wording difference: with `cstring` erased to a machine word, `subprocessResolveOnPath(42)` COMPILES under shv2 (the only diagnostic left is `E3012 unused variable`). Refusing it is what `Parser.parseTypeReference`'s `cstringPointer` arm says would make `cstring` a tag of its own. -->
```maxon
function main() returns ExitCode
	let result = __Builtins.subprocessResolveOnPath(42)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtin-type-checking/builtin-type-checking.error-subprocess-resolve-on-path-int.test:3:50: type mismatch: __Builtins.maxon_subprocess_resolve_on_path argument 0 expects 'cstring' but got 'int'
```

<!-- disabled-test: builtin-type-checking.error-subprocess-get-pid-cstring -->
<!-- The same distinction in the other direction: `subprocessGetPid` declares an `i64` parameter and a `cstring` is a machine word under shv2, so passing one COMPILES (again only `E3012 unused variable` is left). Same missing mechanism as the two cases above. -->
```maxon
function main() returns ExitCode
	let s = "abc"
	let cs = s.cstr()
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
