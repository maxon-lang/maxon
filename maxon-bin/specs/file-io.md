---
feature: file-io
status: experimental
keywords: [file, io, read, write, text, binary]
category: stdlib
---

# File I/O

## Developer Notes

File I/O operations using the `File` type with static methods and `FileError` enum for error handling.

### Implementation

- **Runtime**: Windows API calls in `runtime_windows.mir` (`__read_file`, `__write_file`, `__write_file_binary`)
- **Compiler**: Intrinsics in `codegen_mir_intrinsics.cpp` (`intrinsic_read_file`, `intrinsic_write_file`, `intrinsic_write_file_binary`)
- **Declarations**: `intrinsics_defs.h` defines the function signatures
- **Error Handling**: Uses `FileError` enum with `throws` for proper error propagation

### Windows API Usage

- `CreateFileA` for file creation/opening
- `ReadFile` for reading file contents
- `WriteFile` for writing data
- `CloseHandle` for cleanup
- Files are opened with `GENERIC_READ`/`GENERIC_WRITE` and `CREATE_ALWAYS`/`OPEN_EXISTING` as appropriate

### String Handling

- Text files use UTF-8 encoding
- `File.readText` returns file contents as a string, or throws `FileError.notFound`
- `File.writeText` accepts string content and converts to UTF-8 bytes
- `File.writeBinary` accepts `Array of byte` for raw binary data

### Memory Management

- File contents are read into heap-allocated buffers
- String conversion handles null-termination for C API compatibility
- Memory tracking is supported for debugging allocations

## Documentation

File I/O operations using the `File` type.

### FileError

Error type for file operations:

```maxon
enum FileError is Error
    notFound
    permissionDenied
    alreadyExists
    diskFull
    invalidPath
    readError
    writeError
end 'FileError'
```

### File.readText

Read the entire contents of a text file as a UTF-8 encoded string.

**Signature:** `static function readText(path string) returns string throws FileError`

**Parameters:**
- `path`: File path as a string

**Returns:** File contents as a string

**Throws:** `FileError.notFound` if file cannot be read

**Example:**

```maxon
function main() returns int
    do 'read'
        let content = try File.readText("example.txt")
        print("File content: {content}\n")
        return 1
    catch e FileError 'err'
        print("Could not read file\n")
        return 0
    end 'read'
end 'main'
```
```exitcode
0
```
```stdout
Could not read file
```

### File.writeText

Write a string to a text file using UTF-8 encoding.

**Signature:** `static function writeText(path string, content string) throws FileError`

**Parameters:**
- `path`: File path as a string
- `content`: Text content to write

**Throws:** `FileError.writeError` on failure

### File.writeBinary

Write binary data to a file.

**Signature:** `static function writeBinary(path string, content Array of byte) throws FileError`

**Parameters:**
- `path`: File path as a string
- `content`: Binary data as a byte array

**Throws:** `FileError.writeError` on failure

### File.exists

Check if a file exists at the given path.

**Signature:** `static function exists(path string) returns bool`

**Parameters:**
- `path`: File path as a string

**Returns:** `true` if file exists, `false` otherwise

**Example:**

```maxon
function main() returns int
    if File.exists("temp/output.txt") 'check'
        print("File exists")
    end 'check' else 'nofile'
        print("File does not exist")
    end 'nofile'
    return 0
end 'main'
```

## Tests

<!-- test: read-text-file -->
```maxon
function main() returns int
    // Try to read a nonexistent file - this tests the error path
    do 'read'
        let content = try File.readText("nonexistent_file_xyz.txt")
        print("Content:{content}\n")
        return 0
    catch e FileError 'err'
        print("File not found")
        return 42
    end 'read'
end 'main'
```
```exitcode
42
```
```stdout
File not found
```

<!-- test: read-nonexistent-file -->
```maxon
function main() returns int
    do 'read'
        let content = try File.readText("nonexistent.txt")
        print("Unexpected: {content}\n")
        return 1
    catch e FileError 'err'
        print("File not found")
        return 0
    end 'read'
end 'main'
```
```exitcode
0
```
```stdout
File not found
```

<!-- test: file-exists -->
```maxon
function main() returns int
    // Test File.exists on a nonexistent file (returns false)
    if File.exists("nonexistent_xyz_12345.txt") 'check'
        return 1
    end 'check'
    return 42
end 'main'
```
```exitcode
42
```
