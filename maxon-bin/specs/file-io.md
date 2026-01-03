---
feature: file-io
status: experimental
keywords: [file, io, read, write, text, binary]
category: stdlib
---

# File I/O

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
    end 'read' catch (e FileError) 'err'
        print("Could not read file\n")
        return 0
    end 'err'
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
    end 'read' catch (e FileError) 'err'
        print("File not found")
        return 42
    end 'err'
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
    end 'read' catch (e FileError) 'err'
        print("File not found")
        return 0
    end 'err'
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
