---
feature: file-io
status: experimental
keywords: [file, io, read, write, text, binary]
category: stdlib
---

# File I/O

## Developer Notes

File I/O functions for reading and writing text and binary files.

### Implementation

- **Runtime**: Windows API calls in `runtime_windows.mir` (`__read_file`, `__write_file`, `__write_file_binary`)
- **Compiler**: Intrinsics in `codegen_mir_intrinsics.cpp` (`intrinsic_read_file`, `intrinsic_write_file`, `intrinsic_write_file_binary`)
- **Declarations**: `intrinsics_defs.h` defines the function signatures
- **Error Handling**: Functions return boolean success indicators (0 = success, -1 = failure)

### Windows API Usage

- `CreateFileA` for file creation/opening
- `ReadFile` for reading file contents
- `WriteFile` for writing data
- `CloseHandle` for cleanup
- Files are opened with `GENERIC_READ`/`GENERIC_WRITE` and `CREATE_ALWAYS`/`OPEN_EXISTING` as appropriate

### String Handling

- Text files use UTF-8 encoding
- `readTextFile` returns file contents as a string
- `writeTextFile` accepts string content and converts to UTF-8 bytes
- `writeBinaryFile` accepts `[]byte` array for raw binary data

### Memory Management

- File contents are read into heap-allocated buffers
- String conversion handles null-termination for C API compatibility
- Memory tracking is supported for debugging allocations

## Documentation

File I/O functions provide basic file reading and writing capabilities.

### readTextFile

Read the entire contents of a text file as a UTF-8 encoded string.

**Signature:** `readTextFile(path string) string`

**Parameters:**
- `path`: File path as a string

**Returns:** File contents as a string, or empty string on error

**Example:**

```maxon
function main() int
    let content = readTextFile("example.txt")
    print("File content: " + content)
    return 0
end 'main'
```

### writeTextFile

Write a string to a text file using UTF-8 encoding.

**Signature:** `writeTextFile(path string, content string) bool`

**Parameters:**
- `path`: File path as a string
- `content`: Text content to write

**Returns:** `true` on success, `false` on failure

**Example:**

```maxon
function main() int
    let success = writeTextFile("output.txt", "Hello, World!")
    if success 'write_ok'
        print("File written successfully")
        else 'write_ok'
        print("Failed to write file")
    end 'write_ok'
    return 0
end 'main'
```

### writeBinaryFile

Write binary data to a file.

**Signature:** `writeBinaryFile(path string, data []byte) bool`

**Parameters:**
- `path`: File path as a string
- `data`: Binary data as a byte array

**Returns:** `true` on success, `false` on failure

**Example:**

```maxon
function main() int
    let data = [72b, 101b, 108b, 108b, 111b]  // "Hello" in bytes
    let success = writeBinaryFile("binary.dat", data)
    if success 'write_ok'
        print("Binary file written successfully")
        else 'write_ok'
        print("Failed to write binary file")
    end 'write_ok'
    return 0
end 'main'
```

## Tests

<!-- test: read-text-file -->
```maxon
function main() int
    let content = readTextFile("test.txt")
    print("Content: " + content)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
Content: Hello, World!
```

<!-- test: write-text-file -->
```maxon
function main() int
    let success = writeTextFile("output.txt", "Test content")
    return success as int
end 'main'
```
```exitcode
1
```

<!-- test: write-binary-file -->
```maxon
function main() int
    let data = [65b, 66b, 67b]  // "ABC" as bytes
    let success = writeBinaryFile("binary.bin", data)
    return success as int
end 'main'
```
```exitcode
1
```

<!-- test: read-nonexistent-file -->
```maxon
function main() int
    let content = readTextFile("nonexistent.txt")
    print("Content: '" + content + "'")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
Content: ''
```

<!-- test: write-to-readonly-path -->
```maxon
function main() int
    let success = writeTextFile("C:\\Windows\\test.txt", "Should fail")
    return success as int
end 'main'
```
```exitcode
0
```