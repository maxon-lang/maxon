---
feature: builtins-type
status: stable
keywords: [builtins, __Builtins, compiler intrinsics, stdlib]
category: type-system
---

# __Builtins Type

## Documentation

### Overview

`__Builtins` is a compiler builtin type that provides static methods for low-level system operations. While accessible from any code, most users should prefer the stdlib wrappers: `print()`, `File`, `Directory`, `Process`, `CommandLine`.

All methods include safety checks to prevent crashes and memory corruption:
- Buffer reads are clamped to capacity
- Out-of-bounds argument indices return empty values
- Null/invalid handles are handled gracefully

### Available Static Methods

**I/O:**
- `__Builtins.writeStdout(managed)` returns int - Write managed buffer to stdout
- `__Builtins.writeStderr(managed)` returns int - Write managed buffer to stderr

**Command Line:**
- `__Builtins.commandLineCount()` returns int - Get argument count
- `__Builtins.commandLineArg(index)` returns __ManagedMemory - Get argument at index

**File I/O:**
- `__Builtins.fileOpenRead(managed)` returns int - Open file for reading
- `__Builtins.fileSize(handle)` returns int - Get file size
- `__Builtins.fileRead(handle, managed, size)` returns int - Read bytes from file
- `__Builtins.fileClose(handle)` - Close file handle
- `__Builtins.fileDelete(managed)` returns int - Delete a file
- `__Builtins.writeFile(pathManaged, contentManaged)` returns int - Write text file
- `__Builtins.writeFileBinary(pathManaged, array)` returns int - Write binary file

**Directory:**
- `__Builtins.findFirstFile(managed)` returns int - Start directory search
- `__Builtins.findFilename(handle)` returns __ManagedMemory - Get current filename
- `__Builtins.findNextFile(handle)` returns int - Advance to next file
- `__Builtins.findClose(handle)` - Close search handle
- `__Builtins.directoryExists(managed)` returns bool - Check directory exists
- `__Builtins.createDirectory(managed)` returns bool - Create directory
- `__Builtins.getCurrentDirectory()` returns __ManagedMemory - Get current directory

**Process:**
- `__Builtins.processCreate(cmdManaged, cwdManaged)` returns int - Create process
- `__Builtins.processWait(handle, timeoutMs)` returns int - Wait for process
- `__Builtins.processGetExitCode(handle)` returns int - Get exit code
- `__Builtins.processClose(handle)` - Close process handle
- `__Builtins.processCreateWithCapture(cmdManaged, cwdManaged)` returns int - Create with capture
- `__Builtins.processGetHandle(capturePtr)` returns int - Get process handle
- `__Builtins.processReadStdout(capturePtr)` returns __ManagedMemory - Read captured stdout
- `__Builtins.processReadStderr(capturePtr)` returns __ManagedMemory - Read captured stderr

**Primitive:**
- `__Builtins.floatToBits(value)` returns int - Bitcast float to int

## Tests

These tests verify the __Builtins type works both through stdlib wrappers and directly.

<!-- test: builtins-type.print-via-stdlib -->
```maxon
function main() returns ExitCode
  print("hello\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: builtins-type.command-line-count -->
<!-- Args: arg1 arg2 arg3 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  return args.count()
end 'main'
```
```exitcode
3
```

<!-- test: builtins-type.directory-exists -->
```maxon
function main() returns ExitCode
  let cwd = Directory.currentDirectory()
  if Directory.exists(cwd) 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: builtins-type.process-execute -->
```maxon
function main() returns ExitCode
  let result = Process.execute("cmd /c echo ok", timeoutMs: 5000)
  if result == 0 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: builtins-type.command-line-arg-out-of-bounds -->
<!-- Args: one -->
```maxon
function main() returns ExitCode
  let managed = __Builtins.commandLineArg(9999)
  let s = String{managed: managed, _iterPos: 0}
  if s == "" 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: builtins-type.find-filename-null-handle -->
```maxon
function main() returns ExitCode
  let managed = __Builtins.findFilename(0)
  let s = String{managed: managed, _iterPos: 0}
  if s == "" 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: builtins-type.find-next-null-handle -->
```maxon
function main() returns ExitCode
  let result = __Builtins.findNextFile(0)
  if result == 0 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: builtins-type.find-close-null-handle -->
```maxon
function main() returns ExitCode
  __Builtins.findClose(0)
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: builtins-type.float-to-bits -->
```maxon
function main() returns ExitCode
  let bits = __Builtins.floatToBits(1.0)
  // IEEE 754: 1.0 = 0x3FF0000000000000 = 4607182418800017408
  if bits == 4607182418800017408 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: builtins-type.direct-write-stdout -->
```maxon
function main() returns ExitCode
  let s = "direct\n"
  __Builtins.writeStdout(s.managed)
  return 0
end 'main'
```
```exitcode
0
```
```stdout
direct
```
