---
feature: directory
status: experimental
keywords: [directory, folder, list, filesystem]
category: stdlib
---

# Directory Operations

## Documentation

Directory operations using the `Directory` type.

### DirectoryError

Error type for directory operations:

```maxon
enum DirectoryError is Error
    notFound
    permissionDenied
    notDirectory
    alreadyExists
end 'DirectoryError'
```

### Directory.list

List files and directories in a path.

**Signature:** `static function list(path string) returns Array of string throws DirectoryError`

**Parameters:**
- `path`: Directory path as a string

**Returns:** Array of filenames (excluding `.` and `..`)

**Throws:** `DirectoryError.notFound` if directory doesn't exist

**Example:**

```maxon
function main() returns int
    do 'list'
        let files = try Directory.list("./")
        for f in files 'loop'
            print("{f}\n")
        end 'loop'
        return 0
    end 'list' catch (e DirectoryError) 'err'
        print("Failed to list directory")
        return 1
    end 'err'
end 'main'
```

### Directory.exists

Check if a path exists and is a directory.

**Signature:** `static function exists(path string) returns bool`

**Parameters:**
- `path`: Path to check

**Returns:** `true` if path exists and is a directory, `false` otherwise

**Example:**

```maxon
function main() returns int
    if Directory.exists("bin") 'check'
        print("bin is a directory")
    end 'check' else 'nodir'
        print("bin is not a directory")
    end 'nodir'
    return 0
end 'main'
```

### Directory.isDirectory

Check if a path is a directory. Alias for `exists`.

**Signature:** `static function isDirectory(path string) returns bool`

**Parameters:**
- `path`: Path to check

**Returns:** `true` if path is a directory, `false` otherwise

## Tests

<!-- test: list-directory -->
```maxon
function main() returns int
    do 'list'
        let files = try Directory.list("bin")
        return files.count()
    end 'list' catch (e DirectoryError) 'err'
        return 0
    end 'err'
end 'main'
```
```exitcode-gte
0
```

<!-- test: list-nonexistent-directory -->
```maxon
function main() returns int
    do 'list'
        var files = try Directory.list("nonexistent_dir_12345")
        files = files  // Use the variable to avoid unused warning
        print("Found files\n")
        return 1
    end 'list' catch (e DirectoryError) 'err'
        print("Directory not found")
        return 0
    end 'err'
end 'main'
```
```exitcode
0
```
```stdout
Directory not found
```

<!-- test: directory-exists -->
```maxon
function main() returns int
    if Directory.exists("bin") 'check'
        return 42
    end 'check'
    return 0
end 'main'
```
```exitcode
42
```

<!-- test: directory-is-directory -->
```maxon
function main() returns int
    if Directory.isDirectory("bin") 'check'
        return 42
    end 'check'
    return 0
end 'main'
```
```exitcode
42
```

<!-- test: file-is-not-directory -->
```maxon
function main() returns int
    // Test that a nonexistent path is not a directory
    if Directory.isDirectory("nonexistent_path_12345") 'check'
        return 1
    end 'check'
    return 42
end 'main'
```
```exitcode
42
```
