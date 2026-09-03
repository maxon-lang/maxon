---
feature: managed-file
status: experimental
keywords: [file, managed, __ManagedFile, RAII, handle]
category: type-system
---

# __ManagedFile

## Documentation

### Overview

`__ManagedFile` is a compiler builtin type that wraps a Windows file HANDLE with automatic cleanup via a destructor when the last reference goes out of scope. It replaces the raw `__Builtins` file functions with a managed, RAII-based API.

### Type Structure

`__ManagedFile` has a single field:
- `_handle` (int) — The raw Windows file HANDLE

### Static Methods

- `__ManagedFile.openRead(managed)` — Opens a file for reading. Throws `__ManagedFileError` on failure (notFound / accessDenied / openFailed).
- `__ManagedFile.openWrite(managed)` — Opens a file for writing (creates or overwrites). Throws `__ManagedFileError` on failure.
- `__ManagedFile.openWriteExecutable(managed)` — As openWrite, with 0755 on Unix. Throws on failure.
- `__ManagedFile.exists(managed)` — Returns 1 if the file exists (and is not a directory), 0 otherwise. Does not throw.
- `__ManagedFile.delete(managed)` — Deletes a file. Throws `__ManagedFileError` on failure.
- `__ManagedFile.rename(oldPath, newPath)` — Atomically renames a file, replacing an existing destination. Throws `__ManagedFileError` on failure (`deleteFailed` is the catch-all — the enum has no `renameFailed`, in either reference).
- `__ManagedFile.stat(managed)` — Returns a raw stat buffer pointer. Throws on failure. **The caller OWNS that buffer and must hand it back to `statFree`**: it is a raw allocation the ownership model cannot see, so a `stat` whose buffer is never freed is a leak the exit-101 gate reports.
- `__ManagedFile.statField(buffer, index)` — Reads field `index` of a stat buffer: `0` size, `1` modified, `2` created, `3` accessed (all three Unix SECONDS), `4` isDirectory, `5` isReadOnly. The two attribute fields are **0 or 1**, never a raw attribute mask. Does not throw — a null buffer or an index outside `[0, 6)` (a negative one included) is a caller invariant violation and ABORTS.
- `__ManagedFile.statFree(buffer)` — Releases a stat buffer. Does not throw; aborts on a null buffer. Void and non-throwing, so it is written as a bare STATEMENT (`__ManagedFile.statFree(st)`), which is the only position it has.

### Instance Methods

Instance methods are called on variables declared with type `__ManagedFile`:

- `size()` — Returns the file size in bytes. Throws on failure.
- `read(managed, size)` — Reads up to `size` bytes from the file into managed memory. Throws `readFailed` if `size > managed.capacity` or on I/O error.
- `write(managed)` — Writes managed memory buffer contents to the file. Returns bytes written. Throws on failure.
- `close()` — Explicitly closes the file handle. Idempotent. Also called automatically via destructor. Does not throw.

### Usage Pattern

`__ManagedFile` is used as a struct field inside wrapper types (like `File`):

```text
type FileWrapper
  export var file as __ManagedFile

  static function open(path String) returns FileWrapper throws FileError
    let result = try __ManagedFile.openRead(path.toByteArray().managed) otherwise throw FileError.notFound
    return FileWrapper{_file: result}
  end

  function size() returns int throws FileError
    return try _file.size() otherwise throw FileError.notFound
  end
end
```

## Tests

<!-- test: managed-file.open-read-nonexistent -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
function main() returns ExitCode
	try __ManagedFile.openRead("nonexistent_file_xyz_98765.txt".toByteArray().managed) otherwise 'notFound'
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

<!-- test: managed-file.write-and-read -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
export enum TestFileError implements Error
	openFailed
end 'TestFileError'

type TestFile
	export var file as __ManagedFile

	export static function openWrite(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openWrite(path) otherwise 'fail'
			throw TestFileError.openFailed
		end 'fail'
		return TestFile{file: handle}
	end 'openWrite'

	export static function openRead(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openRead(path) otherwise 'fail'
			throw TestFileError.openFailed
		end 'fail'
		return TestFile{file: handle}
	end 'openRead'
end 'TestFile'

function main() returns ExitCode
	let path = "test_managed_file_rw.txt"
	// Write a file
	var wf = try TestFile.openWrite(path.toByteArray().managed) otherwise 'writeFail'
		print("write open failed")
		return 1
	end 'writeFail'
	let content = "Hello Managed"
	try wf.file.write(content.toByteArray().managed) otherwise 'wErr'
		wf.file.close()
		return 3
	end 'wErr'
	wf.file.close()

	// Read it back
	var rf = try TestFile.openRead(path.toByteArray().managed) otherwise 'readFail'
		print("read open failed")
		return 2
	end 'readFail'
	let size = try rf.file.size() otherwise 'sizeErr'
		return 8
	end 'sizeErr'
	var buffer = try __ManagedMemory.create(size + 1, 1) otherwise 'allocFail'
		return 5
	end 'allocFail'
	let bytesRead = try rf.file.read(buffer, size) otherwise 'rErr'
		rf.file.close()
		return 9
	end 'rErr'
	rf.file.close()
	try buffer.setLength(bytesRead) otherwise 'setLenFail'
		return 6
	end 'setLenFail'
	// Null-terminate
	try buffer.setLength(bytesRead + 1) otherwise 'setLenFail2'
		return 6
	end 'setLenFail2'
	try buffer.setByte(bytesRead, 0) otherwise 'setByteFail'
		return 7
	end 'setByteFail'
	try buffer.setLength(bytesRead) otherwise 'setLenFail3'
		return 6
	end 'setLenFail3'
	let readContent = String.init(buffer)
	print("{readContent}")

	// Clean up
	try __ManagedFile.delete(path.toByteArray().managed) otherwise 'delErr'
		return 4
	end 'delErr'

	return 42
end 'main'
```
```exitcode
42
```
```stdout
Hello Managed
```

<!-- test: managed-file.exists -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
export enum TestFileError implements Error
	openFailed
end 'TestFileError'

type TestFile
	export var file as __ManagedFile

	export static function openWrite(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openWrite(path) otherwise 'fail'
			throw TestFileError.openFailed
		end 'fail'
		return TestFile{file: handle}
	end 'openWrite'
end 'TestFile'

function createEmptyFile(path String) throws TestFileError
	var f = try TestFile.openWrite(path.toByteArray().managed)
	f.file.close()
end 'createEmptyFile'

function main() returns ExitCode
	// Non-existent file
	let e1 = __ManagedFile.exists("nonexistent_xyz_managed_12345.txt".toByteArray().managed)
	if e1 != 0 'check1'
		return 1
	end 'check1'

	// Create a file, check exists, delete it
	let path = "test_managed_exists.txt"
	try createEmptyFile(path) otherwise 'createFail'
		return 10
	end 'createFail'
	let e2 = __ManagedFile.exists(path.toByteArray().managed)
	if e2 != 1 'check2'
		return 2
	end 'check2'
	try __ManagedFile.delete(path.toByteArray().managed) otherwise 'delErr'
		return 4
	end 'delErr'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: managed-file.delete-nonexistent -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
function main() returns ExitCode
	try __ManagedFile.delete("nonexistent_delete_xyz.txt".toByteArray().managed) otherwise 'checkFail'
		print("delete failed as expected")
		return 42
	end 'checkFail'
	return 0
end 'main'
```
```exitcode
42
```
```stdout
delete failed as expected
```

<!-- test: managed-file.auto-close -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
export enum TestFileError implements Error
	openFailed
end 'TestFileError'

type TestFile
	export var file as __ManagedFile

	export static function openWrite(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openWrite(path) otherwise 'fail'
			throw TestFileError.openFailed
		end 'fail'
		return TestFile{file: handle}
	end 'openWrite'

	export static function openRead(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openRead(path) otherwise 'fail'
			throw TestFileError.openFailed
		end 'fail'
		return TestFile{file: handle}
	end 'openRead'
end 'TestFile'

function writeFile(path String)
	let wf = try TestFile.openWrite(path.toByteArray().managed) otherwise panic("write open failed")
	try wf.file.write("auto".toByteArray().managed) otherwise panic("write failed")
	// wf goes out of scope here, destructor closes handle
end 'writeFile'

function main() returns ExitCode
	let path = "test_managed_autoclose.txt"
	writeFile(path)

	// Verify we can read it (file was properly closed by destructor)
	var rf = try TestFile.openRead(path.toByteArray().managed) otherwise 'readFail'
		print("read failed")
		return 1
	end 'readFail'
	let size = try rf.file.size() otherwise 'sizeErr'
		return 3
	end 'sizeErr'
	rf.file.close()
	try __ManagedFile.delete(path.toByteArray().managed) otherwise 'delErr'
		return 2
	end 'delErr'
	if size == 4 'sizeOk'
		return 42
	end 'sizeOk'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: managed-file.open-read-not-found-variant -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->

The errno→variant mapping ensures that opening a path that does not exist
routes to the `notFound` arm (rather than the catch-all `openFailed`).
Backed by `gt->io_error_code` populated by the runtime sync worker
(Win32 ERROR_FILE_NOT_FOUND=2 / POSIX ENOENT=2).

```maxon
function main() returns ExitCode
	var result = 0
	try __ManagedFile.openRead("nonexistent_phaseB_open_xyz.txt".toByteArray().managed) otherwise (e) 'h'
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

<!-- test: managed-file.delete-not-found-variant -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
function main() returns ExitCode
	var result = 0
	try __ManagedFile.delete("nonexistent_phaseB_delete_xyz.txt".toByteArray().managed) otherwise (e) 'h'
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

<!-- test: managed-file.stat-not-found-variant -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
function main() returns ExitCode
	var result = 0
	try __ManagedFile.stat("nonexistent_phaseB_stat_xyz.txt".toByteArray().managed) otherwise (e) 'h'
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

<!-- test: managed-file.error-direct-construction -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
function main() returns ExitCode
	let f = __ManagedFile{_handle: 0}
	return 0
end 'main'
```
```maxoncstderr
error E3072: specs/fragments/managed-file/managed-file.error-direct-construction.test:3:24: '__ManagedFile' is a compiler builtin type and cannot be constructed directly
```

<!-- test: managed-file.unknown-instance-method-is-refused-by-name -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
shv2-authored, and it pins the DISPATCHER rather than a behaviour the oracle defines.

The instance surface is exactly four methods (`size`/`read`/`write`/`close`), so anything else is refused
BY NAME. Left to fall through it would mangle into a callee no file declares and surface as `E3004`
against a method the author did write — the same argument `parseStringStaticCall` makes for `String`'s
statics.

⚠ The probe is `seek`, and that choice is the point rather than an arbitrary misspelling: v1 has an
`lseek` primitive, but it belongs to a different v1 abstraction and NEITHER reference's `__ManagedFile`
method list carries one, so `seek` is a name the language genuinely does not have and will not acquire by
a later rung finishing this surface. (This case used to probe `size`, which R4.2 delivers — a marker that
went stale the moment the rung it was written against landed.)

⚠ It is a COMPILE-TIME case on purpose: it needs no file to exist in the runner's working directory. The
RUNTIME half of instance dispatch is pinned by `managed-file.write-and-read` and `managed-file.auto-close`,
which reach `write`/`size` through a struct FIELD — the spelling the documented usage pattern actually
uses.
```maxon
function main() returns ExitCode
	let f = try __ManagedFile.openRead("nonexistent_unknown_method_xyz.txt".toByteArray().managed) otherwise 'nf'
		return 1
	end 'nf'
	let n = f.seek(0)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:12: Unsupported: `__ManagedFile` method 'seek' — the type has exactly the four both references declare: `size()`, `read(managed, size)`, `write(managed)` and `close()`
```

<!-- test: managed-file.rename-round-trip -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
shv2-authored, like the dispatcher case above, and for a reason of the same kind: `rename` is the ONLY one
of the thirteen methods that NO canonical case reaches. Every `/specs` exercise of it goes through
`File.rename`, whose signature takes a `FilePath` — and `stdlib/FilePath.maxon` did not then load for shv2
(it stopped at `E2015 String method 'byteAtOrPanic'`, `:56`), so there was no ported case to enable. Shipping
the method untested is worse than authoring one.

⚠ **THAT REASON'S FIRST HALF HAS EXPIRED: all of `stdlib/` loads now.** Whether a canonical `File.rename`
exercise is portable today is a question for whoever ports it; the shv2-authored case below is unaffected
and stays, because what it pins is the builtin-level round trip and nothing about `FilePath`.

What it pins is the ROUND TRIP, at the builtin level: a file written and closed, renamed, and then observed
to have moved — `exists(old) == 0` AND `exists(new) == 1`. Either half alone would pass against a `rename`
that did nothing to one of the two names.

⚠ It also happens to be the one case where a `__ManagedFile` is bound DIRECTLY rather than through a
wrapper's field, so the two receiver spellings (`structRef` from the open, `named` from a field read) are
both covered by the file: this one and `managed-file.write-and-read`.
```maxon
function main() returns ExitCode
	let oldPath = "test_managed_rename_src.txt"
	let newPath = "test_managed_rename_dst.txt"
	var f = try __ManagedFile.openWrite(oldPath.toByteArray().managed) otherwise 'openFail'
		return 1
	end 'openFail'
	try f.write("rename me".toByteArray().managed) otherwise 'writeFail'
		f.close()
		return 2
	end 'writeFail'
	f.close()

	try __ManagedFile.rename(oldPath.toByteArray().managed, newPath.toByteArray().managed) otherwise 'renameFail'
		return 3
	end 'renameFail'
	if __ManagedFile.exists(oldPath.toByteArray().managed) != 0 'oldStillThere'
		return 4
	end 'oldStillThere'
	if __ManagedFile.exists(newPath.toByteArray().managed) != 1 'newMissing'
		return 5
	end 'newMissing'
	try __ManagedFile.delete(newPath.toByteArray().managed) otherwise 'deleteFail'
		return 6
	end 'deleteFail'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: managed-file.stat-round-trip -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
shv2-authored, and it is the case whose ABSENCE was the R4.2 review's first blocker: not one committed
case reached a SUCCESSFUL `stat`. Every canonical `stat` exercise goes through `File.info`, and
`stdlib/FilePath.maxon` did not then load for shv2 (all of `stdlib/` loads now), so
`stat-not-found-variant` — which throws before a
buffer exists — was the whole of the coverage. `statField` and `statFree` had none at all.

What that hid was total: `__ManagedFile.statFree(buffer)` is VOID and NON-THROWING, so a bare statement is
the only position it can be written in, and the statement parser did not route a compiler-owned static
there. The canonical spelling (`stdlib/File.maxon:186`, verbatim) died as
`E2015: Unsupported: identifier statement`, which made the only release for `stat`'s raw `__mm_alloc` block
UNREACHABLE — so every successful `stat` leaked 48 bytes and the program exited **101**. A rung that
delivers `stat` and cannot free its result has not delivered `stat`.

What the case pins, and why each half is here rather than one of them:
- **the buffer is FREED** — this runs under the leak gate, so the fix is checked by the exit code, not by
  reading the parser;
- **the six FIELDS carry the packing** — `[0]` is the size (5, from a 5-byte write), `[32]`/`[40]` are the
  two attribute bits published as **0/1** and never as the raw mask (`stdlib/File.maxon:183` compares
  `attrs == 1`, so a leaked `0x10` would be a silent wrong answer), and `[8]`/`[16]`/`[24]` are Unix
  SECONDS. A plausibility WINDOW is the strongest stable assertion available for a clock, and it is a real
  one: a FILETIME that skipped the epoch subtraction reads ~1.3e10 s, and one that skipped the ÷10,000,000
  reads ~1.7e16 — both far outside it;
- **a DIRECTORY answers `isDirectory == 1`** through the same buffer, which is the other half of the bit
  that `exists` reads as its whole answer.
```maxon
function main() returns ExitCode
	let path = "test_managed_stat_round_trip.txt"
	var f = try __ManagedFile.openWrite(path.toByteArray().managed) otherwise 'openFail'
		return 1
	end 'openFail'
	try f.write("abcde".toByteArray().managed) otherwise 'writeFail'
		f.close()
		return 2
	end 'writeFail'
	f.close()

	let st = try __ManagedFile.stat(path.toByteArray().managed) otherwise 'statFail'
		return 3
	end 'statFail'
	print("size={__ManagedFile.statField(st, 0)} isDirectory={__ManagedFile.statField(st, 4)} isReadOnly={__ManagedFile.statField(st, 5)}\n")
	var implausible = 0
	for i in 1 to 3 'stamps'
		let t = __ManagedFile.statField(st, i)
		if t < 1577836800 or t > 4102444800 'window'
			implausible = implausible + 1
		end 'window'
	end 'stamps'
	print("implausibleTimestamps={implausible}\n")
	__ManagedFile.statFree(st)

	let dst = try __ManagedFile.stat(".".toByteArray().managed) otherwise 'dirStatFail'
		return 4
	end 'dirStatFail'
	print("dirIsDirectory={__ManagedFile.statField(dst, 4)}\n")
	__ManagedFile.statFree(dst)

	try __ManagedFile.delete(path.toByteArray().managed) otherwise 'deleteFail'
		return 5
	end 'deleteFail'
	return 42
end 'main'
```
```exitcode
42
```
```stdout
size=5 isDirectory=0 isReadOnly=0
implausibleTimestamps=0
dirIsDirectory=1
```

<!-- test: managed-file.open-read-without-try -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->

⭐ **E3057 MUST NAME THE CONSTRUCT THE AUTHOR WROTE (D12).** `openRead` throws, so a bare call branches on
nothing and its error flag is discarded — the same defect `managed-directory.next-without-try` pins for the
sibling family, and it gets the same sentence, quoting the author's own spelling rather than the
`__mf_open_read` they have never heard of. Before D12 it read `throwing array accessor requires try: …`:
the right code, naming a construct this program does not contain.

⚠ Compile-time on purpose — it needs no file to exist in the runner's working directory.
```maxon
function main() returns ExitCode
	_ = __ManagedFile.openRead("nonexistent_open_read_without_try_xyz.txt".toByteArray().managed)
	return 0
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/managed-file/managed-file.open-read-without-try.test:3:6: throwing function requires try: 'openRead'
```

<!-- test: managed-file.open-write-executable-without-try -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->

⭐⭐ **THE CALLEE NAMES THE METHOD THAT WAS WRITTEN, AND THIS CASE RECORDED IN ADVANCE THE DAY IT WOULD
BECOME ABLE TO.** It used to read *"`openWriteExecutable` and `openWrite` are ONE call here … the callee is
therefore a LOSSY KEY and no map over it can be injective"*, and it closed with the exact condition for its
own change: *"This case is the one that would go quiet if a POSIX lane ever splits the two entry points — at
which point the callee stops being lossy, `ManagedFileRuntime.managedFileSourceMethod`'s open-write arm
collapses to an ordinary `found`, and THIS wording must change with it."*

MAC4 is that day. A POSIX lane makes the 0755 bit OBSERVABLE — an executable written without it exists,
holds the right bytes and cannot be run — so `openWriteExecutable` has an entry point of its own
(`__mf_open_write_exec`, and `StdOp.osFileOpenWriteExecutable` behind it), the map is injective again, and
the diagnostic names exactly the method the program contains. What the case still pins is that E3057 quotes
a SOURCE METHOD and never a runtime callee.
```maxon
function main() returns ExitCode
	_ = __ManagedFile.openWriteExecutable("nonexistent_open_write_exec_without_try_xyz.txt".toByteArray().managed)
	return 0
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/managed-file/managed-file.open-write-executable-without-try.test:3:6: throwing function requires try: 'openWriteExecutable'
```

<!-- test: managed-file.read-into-a-non-owned-buffer-is-refused -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->

⛔⛔ **A `read` IS A WRITE INTO THE CALLER'S BUFFER, AND A BUFFER THE RECORD DOES NOT OWN IS REFUSED —
NOT FILLED.** A zero-copy view, an `.rdata` byte-string blob, an inline String and an immortal record all
stamp a NEGATIVE sentinel in `capacity@16`, and `managed.capacity()` hands that value straight to user code:
so the documented bound *"throws `readFailed` if `size > managed.capacity`"* already refuses every
`size >= 0` for such a record, `0` included.

⚠ **THE EMITTED GUARD DID NOT DO THAT, AND THE `size` BOUND STRUCTURALLY COULD NOT.** The runtime compared
`size` against a BYTE EXTENT derived through a logical shift right, which turns `capacity = -1` into
`0x1FFFFFFFFFFFFFFF` — enormous and positive. MEASURED before the fix: this program answered `within=2
over=24 parent=90,90`, i.e. `ReadFile` wrote 24 bytes through a 4-byte window into an `Array` somebody else
owns, corrupting its live elements and 20 bytes past the end of its allocation.

⭐ **THE 2-BYTE READ IS WHAT MAKES THE CASE DISCRIMINATE.** Two bytes FIT the four-byte window, so no size
bound of any kind can refuse it and only the OWNERSHIP test can — a case that asked for 24 bytes alone would
be satisfied by a guard that merely got its arithmetic right. The parent is read back afterwards to prove
nothing was written through it.
```maxon
typealias Byte = int(0 to 255)
typealias Bytes = Array with Byte

export enum TestFileError implements Error
	openFailed
end 'TestFileError'

type TestFile
	export var file as __ManagedFile

	export static function openWrite(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openWrite(path) otherwise throw TestFileError.openFailed
		return TestFile{file: handle}
	end 'openWrite'

	export static function openRead(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openRead(path) otherwise throw TestFileError.openFailed
		return TestFile{file: handle}
	end 'openRead'
end 'TestFile'

function main() returns ExitCode
	let path = "test_mf_read_non_owned.txt"
	var wf = try TestFile.openWrite(path.toByteArray().managed) otherwise panic("openWrite: the runner's working directory is writable")
	try wf.file.write("ZZZZZZZZZZZZZZZZZZZZZZZZ".toByteArray().managed) otherwise panic("write: 24 bytes to a freshly opened file")
	wf.file.close()

	var arr = Bytes.create()
	arr.push(65)
	arr.push(66)
	arr.push(67)
	arr.push(68)
	let view = try arr.slice(0, endIndex: 4) otherwise panic("slice: 0..4 of a length-4 array")

	// Guard the PREMISE rather than the sentinel's value: if a slice ever stops being a zero-copy view this
	// case stops testing its subject, and it must go red then rather than quiet.
	if view.managed.capacity() >= 0 'sliceMustStillBeAView'
		print("a slice is no longer a non-owned view; this case no longer tests its subject")
		return 1
	end 'sliceMustStillBeAView'

	var rf = try TestFile.openRead(path.toByteArray().managed) otherwise panic("openRead: the file just written")
	let within = try rf.file.read(view.managed, 2) otherwise 999
	let over = try rf.file.read(view.managed, 24) otherwise 999
	rf.file.close()

	let p0 = try arr.get(0) otherwise panic("get: index 0 of a length-4 array")
	let p3 = try arr.get(3) otherwise panic("get: index 3 of a length-4 array")
	try __ManagedFile.delete(path.toByteArray().managed) otherwise panic("delete: the file this case created")

	print("within={within} over={over} parent={p0},{p3}")
	return 42
end 'main'
```
```exitcode
42
```
```stdout
within=999 over=999 parent=65,68
```

<!-- test: managed-file.read-bound-is-the-owned-capacity -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->

⭐⭐ **THE CONTROL THAT MAKES THE REFUSAL ABOVE MEAN SOMETHING.** "A read into a non-owned buffer throws" is
satisfied just as well by a guard that throws on EVERY buffer, so the owned path has to be pinned beside it:
a `size` at exactly the capacity succeeds and puts the file's bytes in the buffer, and one byte above it
THROWS rather than clamping — which is the contract's own wording (`specs/managed-file.md`: *"throws
`readFailed` if `size > managed.capacity`"*) and the bootstrap's stated choice, *"a silent clamp in the
runtime would hide a user contract violation"*.

⚠ The file holds 10 bytes and the buffer 4, so the at-capacity read is bounded by the BUFFER and not by the
file — `48` is `'0'`, the first byte written.
```maxon
export enum TestFileError implements Error
	openFailed
end 'TestFileError'

type TestFile
	export var file as __ManagedFile

	export static function openWrite(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openWrite(path) otherwise throw TestFileError.openFailed
		return TestFile{file: handle}
	end 'openWrite'

	export static function openRead(path __ManagedMemory) returns TestFile throws TestFileError
		let handle = try __ManagedFile.openRead(path) otherwise throw TestFileError.openFailed
		return TestFile{file: handle}
	end 'openRead'
end 'TestFile'

function main() returns ExitCode
	let path = "test_mf_read_owned_bound.txt"
	var wf = try TestFile.openWrite(path.toByteArray().managed) otherwise panic("openWrite: the runner's working directory is writable")
	try wf.file.write("0123456789".toByteArray().managed) otherwise panic("write: 10 bytes to a freshly opened file")
	wf.file.close()

	var rf = try TestFile.openRead(path.toByteArray().managed) otherwise panic("openRead: the file just written")
	var mm = try __ManagedMemory.create(4, elementSize: 1) otherwise panic("create: a 4-byte owned buffer")
	let atCapacity = try rf.file.read(mm, 4) otherwise 999
	let overCapacity = try rf.file.read(mm, 5) otherwise 999
	rf.file.close()

	try mm.setLength(4) otherwise panic("setLength: 4 bytes of a capacity-4 buffer")
	let first = try mm.byteAt(0) otherwise panic("byteAt: offset 0 of a 4-byte live range")
	try __ManagedFile.delete(path.toByteArray().managed) otherwise panic("delete: the file this case created")

	print("at={atCapacity} first={first} over={overCapacity}")
	return 42
end 'main'
```
```exitcode
42
```
```stdout
at=4 first=48 over=999
```
