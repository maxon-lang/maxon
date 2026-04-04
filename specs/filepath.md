---
feature: filepath
status: experimental
keywords: [filepath, path, filesystem, file, directory]
category: stdlib
---

# FilePath Type

## Documentation

`FilePath` is a type-safe wrapper around `String` for filesystem paths. It provides platform-aware path manipulation with methods for joining, extracting components, and querying path properties.

### Construction

Create a `FilePath` from a string literal or via `FilePath.from()`:

```maxon
function main() returns ExitCode
	let p = FilePath from "C:\\Users\\test.txt"
	print("{p}\n")
	let q = try FilePath.from("hello.maxon") otherwise panic("bad path")
	print("{q}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\Users\test.txt
hello.maxon
```

### Path Components

Extract filename, extension, stem, and parent directory:

```maxon
function main() returns ExitCode
	let p = FilePath from "C:\\Users\\docs\\readme.md"
	print("{p.filename()}\n")
	print("{p.fileExtension()}\n")
	print("{p.stem()}\n")
	print("{p.parent()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
readme.md
.md
readme
C:\Users\docs
```

### Joining Paths

Use `join()` to append components with the platform separator:

```maxon
function main() returns ExitCode
	let base = FilePath from "C:\\Users"
	let full = base.join("docs").join("readme.md")
	print("{full}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\Users\docs\readme.md
```

## Tests

<!-- test: filepath-from-string -->
```maxon
function main() returns ExitCode
	let p = FilePath from "C:\\test.txt"
	print("{p.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\test.txt
```

<!-- test: filepath-from-method -->
```maxon
function main() returns ExitCode
	let p = try FilePath.from("hello.maxon") otherwise panic("bad path")
	print("{p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello.maxon
```

<!-- test: filepath-filename -->
```maxon
function main() returns ExitCode
	let p = FilePath from "C:\\Users\\test.txt"
	print("{p.filename()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
test.txt
```

<!-- test: filepath-filename-fwd -->
```maxon
function main() returns ExitCode
	let p = FilePath from "C:/Users/test.txt"
	print("{p.filename()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
test.txt
```

<!-- test: filepath-extension -->
```maxon
function main() returns ExitCode
	let p = FilePath from "file.txt"
	print("{p.fileExtension()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
.txt
```

<!-- test: filepath-extension-none -->
```maxon
function main() returns ExitCode
	let p = FilePath from "file"
	print("'{p.fileExtension()}'\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
''
```

<!-- test: filepath-extension-maxon -->
```maxon
function main() returns ExitCode
	let p = FilePath from "Compiler.maxon"
	print("{p.fileExtension()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
.maxon
```

<!-- test: filepath-stem -->
```maxon
function main() returns ExitCode
	let p = FilePath from "file.txt"
	print("{p.stem()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
file
```

<!-- test: filepath-hidden-file -->
```maxon
function main() returns ExitCode
	let p = FilePath from ".gitignore"
	print("ext:'{p.fileExtension()}'\n")
	print("stem:'{p.stem()}'\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ext:''
stem:'.gitignore'
```

<!-- test: filepath-parent -->
```maxon
function main() returns ExitCode
	let p = FilePath from "C:\\Users\\test.txt"
	print("{p.parent()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\Users
```

<!-- test: filepath-parent-fwd -->
```maxon
function main() returns ExitCode
	let p = FilePath from "C:/Users/test.txt"
	print("{p.parent()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\Users
```

<!-- test: filepath-parent-none -->
```maxon
function main() returns ExitCode
	let p = FilePath from "file.txt"
	print("'{p.parent()}'\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
''
```

<!-- test: filepath-join -->
```maxon
function main() returns ExitCode
	let base = FilePath from "C:\\Users"
	let full = base.join("test")
	print("{full}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\Users\test
```

<!-- test: filepath-join-trailing-sep -->
```maxon
function main() returns ExitCode
	let base = FilePath from "C:\\Users\\"
	let full = base.join("test")
	print("{full}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\Users\test
```

<!-- test: filepath-join-chain -->
```maxon
function main() returns ExitCode
	let base = FilePath from "C:\\Users"
	let full = base.join("docs").join("readme.md")
	print("{full}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\Users\docs\readme.md
```

<!-- test: filepath-is-absolute-drive -->
```maxon
function main() returns ExitCode
	let p = FilePath from "/absolute/path"
	if p.isAbsolute() 'abs'
		print("absolute\n")
	end 'abs'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
absolute
```

<!-- test: filepath-is-absolute-unc -->
```maxon
function main() returns ExitCode
	let p = FilePath from "\\\\server\\share"
	if p.isAbsolute() 'abs'
		print("absolute\n")
	end 'abs'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
absolute
```

<!-- test: filepath-is-relative -->
```maxon
function main() returns ExitCode
	let p = FilePath from "foo/bar"
	if p.isRelative() 'rel'
		print("relative\n")
	end 'rel'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
relative
```

<!-- test: filepath-change-extension -->
```maxon
function main() returns ExitCode
	let p = FilePath from "file.txt"
	let q = p.changeExtension(".md")
	print("{q}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
file.md
```

<!-- test: filepath-change-extension-add -->
```maxon
function main() returns ExitCode
	let p = FilePath from "file"
	let q = p.changeExtension(".exe")
	print("{q}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
file.exe
```

<!-- test: filepath-change-extension-with-parent -->
```maxon
function main() returns ExitCode
	let p = FilePath from "C:\\Users\\file.txt"
	let q = p.changeExtension(".md")
	print("{q}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\Users\file.md
```

<!-- test: filepath-normalize -->
```maxon
function main() returns ExitCode
	let p = FilePath from "C:/Users/test"
	let n = p.normalize()
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\Users\test
```

<!-- test: filepath-equality -->
```maxon
function main() returns ExitCode
	let a = FilePath from "C:\\test.txt"
	let b = FilePath from "C:\\test.txt"
	if a == b 'eq'
		print("equal\n")
	end 'eq'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
equal
```

<!-- test: filepath-inequality -->
```maxon
function main() returns ExitCode
	let a = FilePath from "C:\\a.txt"
	let b = FilePath from "C:\\b.txt"
	if a != b 'neq'
		print("not equal\n")
	end 'neq'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
not equal
```

<!-- test: filepath-stringable -->
```maxon
function main() returns ExitCode
	let p = FilePath from "hello.txt"
	print("{p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello.txt
```

<!-- test: filepath-empty -->
```maxon
function main() returns ExitCode
	let p = FilePath from ""
	if p.isEmpty() 'empty'
		print("empty\n")
	end 'empty'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
empty
```

<!-- test: filepath-separator -->
```maxon
function main() returns ExitCode
	print("{FilePath.separator()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
\
```

<!-- test: filepath-file-exists -->
```maxon
function main() returns ExitCode
	let p = FilePath from "nonexistent_file_12345.txt"
	if not p.fileExists() 'notExists'
		print("not found\n")
	end 'notExists'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
not found
```

<!-- test: filepath-directory-exists -->
```maxon
function main() returns ExitCode
	let p = FilePath from "nonexistent_dir_12345"
	if not p.directoryExists() 'notExists'
		print("not found\n")
	end 'notExists'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
not found
```

<!-- test: filepath-join-filepath -->
```maxon
function main() returns ExitCode
	let base = FilePath from "C:\\Users"
	let child = FilePath from "docs"
	let full = base.join(child)
	print("{full}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\Users\docs
```

<!-- test: filepath-join-empty-base -->
```maxon
function main() returns ExitCode
	let base = FilePath from ""
	let full = base.join("test.txt")
	print("{full}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
test.txt
```

<!-- test: filepath-from-throws-invalid -->
```maxon
function main() returns ExitCode
	let p = try FilePath.from("https://example.com/path") otherwise (e) 'err'
		print("caught error\n")
		return 0
	end 'err'
	print("{p}\n")
	return 1
end 'main'
```
```exitcode
0
```
```stdout
caught error
```

<!-- test: filepath-from-valid-ok -->
```maxon
function main() returns ExitCode
	let p = try FilePath.from("C:\\valid\\path.txt") otherwise panic("unexpected error")
	print("{p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\valid\path.txt
```

### File URL Support

`FilePath` transparently accepts `file://` URLs in both `init()` and `from()`, extracting the filesystem path. Non-file URL schemes cause a panic in `init()` or throw `FilePathError.notFileURL` in `from()`.

<!-- test: filepath-file-url-from -->
```maxon
function main() returns ExitCode
	let p = try FilePath.from("file:///tmp/test.txt") otherwise panic("bad path")
	print("{p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
\tmp\test.txt
```

<!-- test: filepath-file-url-init -->
```maxon
function main() returns ExitCode
	let p = FilePath from "file:///tmp/test.txt"
	print("{p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
\tmp\test.txt
```

<!-- test: filepath-file-url-unix-path -->
```maxon
function main() returns ExitCode
	let p = try FilePath.from("file:///home/user/file.txt") otherwise panic("bad path")
	print("{p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
\home\user\file.txt
```

<!-- test: filepath-not-file-url -->
```maxon
function main() returns ExitCode
	try FilePath.from("https://example.com/path") otherwise 'err'
		print("caught notFileURL\n")
		return 0
	end 'err'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
caught notFileURL
```

<!-- test: filepath-regular-string-unchanged -->
```maxon
function main() returns ExitCode
	let p = try FilePath.from("C:\\Users\\normal\\path.txt") otherwise panic("bad path")
	print("{p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
C:\Users\normal\path.txt
```

<!-- test: filepath-file-url-empty-path -->
```maxon
function main() returns ExitCode
	let p = try FilePath.from("file:///") otherwise panic("bad path")
	print("path='{p}'\n")
	print("empty={p.isEmpty()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
path='\'
empty=false
```
