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
- **Error Handling**: `readTextFile` returns `nil` on error, other functions return boolean success indicators

### Windows API Usage

- `CreateFileA` for file creation/opening
- `ReadFile` for reading file contents
- `WriteFile` for writing data
- `CloseHandle` for cleanup
- Files are opened with `GENERIC_READ`/`GENERIC_WRITE` and `CREATE_ALWAYS`/`OPEN_EXISTING` as appropriate

### String Handling

- Text files use UTF-8 encoding
- `readTextFile` returns file contents as a string, or `nil` on error
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

**Signature:** `readTextFile(path string) string or nil`

**Parameters:**
- `path`: File path as a string

**Returns:** File contents as a string, or `nil` if file cannot be read

**Example:**

```maxon
function main() returns int
    let content = readTextFile("example.txt") or 'noFile'
        print("Could not read file")
        return 0
    end 'noFile'
    print("File content: {content}\n")
    return 1
end 'main'
```
```exitcode
0
```
```stdout
Could not read file
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
function main() returns int
    let success = writeTextFile("temp/output.txt", "Hello, World!")
    if success 'write_ok'
        print("File written successfully")
    end 'write_ok' else 'write_fail'
        print("Failed to write file")
    end 'write_fail'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
File written successfully
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
function main() returns int
    let data = [72 as byte, 101 as byte, 108 as byte, 108 as byte, 111 as byte]  // "Hello" in bytes
    let success = writeBinaryFile("temp/binary.dat", data)
    if success 'write_ok'
        print("Binary file written successfully")
    end 'write_ok' else 'write_fail'
        print("Failed to write binary file")
    end 'write_fail'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
Binary file written successfully
```

## Tests

<!-- test: read-text-file -->
```maxon
function main() returns int
    // Create a test file first
    writeTextFile("temp/read-test.txt", "Hello")
    let content = readTextFile("temp/read-test.txt") or 'noFile'
        print("File not found")
        return 1
    end 'noFile'
    print("Content:{content}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
Content:Hello
```

<!-- test: write-text-file -->
```maxon
function main() returns int
    let success = writeTextFile("temp/output.txt", "Testy content")
    return success as int
end 'main'
```
```exitcode
1
```

<!-- test: write-binary-file -->
```maxon
function main() returns int
    let data = [65 as byte, 66 as byte, 67 as byte]  // "ABC" as bytes
    let success = writeBinaryFile("temp/binary.bin", data)
    return success as int
end 'main'
```
```exitcode
1
```

<!-- test: read-nonexistent-file -->
```maxon
function main() returns int
    var result = readTextFile("nonexistent.txt")
    if let content = result 'hasContent'
        print("Unexpected: {content}\n")
        return 1
    end 'hasContent'
    print("File not found")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
File not found
```

<!-- test: write-to-readonly-path -->
```maxon
function main() returns int
    let success = writeTextFile("C:\\Windows\\test.txt", "Should fail")
    return success as int
end 'main'
```
```exitcode
0
```
