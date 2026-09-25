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
	let parent = try p.parent() otherwise panic("no parent")
	print("{parent}\n")
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

### Normalizing Paths

`normalize()` folds a path lexically, without asking the filesystem: `.` components are removed, each `..`
cancels the component before it, repeated separators collapse and a trailing separator is dropped. A `..`
that would climb above an absolute path's root is dropped; one at the front of a relative path is kept. A
relative path that folds away entirely is `.`, and the empty path stays empty.

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
```Stdout:x64-windows
C:\test.txt
```
```stdout
C:/test.txt
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
	let parent = try p.parent() otherwise panic("no parent")
	print("{parent}\n")
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
C:\Users
```
```stdout
C:/Users
```

<!-- test: filepath-parent-fwd -->
```maxon
function main() returns ExitCode
	let p = FilePath from "C:/Users/test.txt"
	let parent = try p.parent() otherwise panic("no parent")
	print("{parent}\n")
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
C:\Users
```
```stdout
C:/Users
```

<!-- test: filepath-parent-none -->
```maxon
function main() returns ExitCode
	let p = FilePath from "file.txt"
	try p.parent() otherwise 'noParent'
		print("caught noParent\n")
		return 0
	end 'noParent'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
caught noParent
```

<!-- test: filepath-parent-under-root -->
```maxon
function main() returns ExitCode
	let p = FilePath from "/foo"
	let parent = try p.parent() otherwise panic("no parent")
	print("{parent}\n")
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
\
```
```stdout
/
```

<!-- test: filepath-parent-of-root-throws -->
```maxon
function main() returns ExitCode
	let p = FilePath from "/"
	try p.parent() otherwise 'noParent'
		print("caught noParent\n")
		return 0
	end 'noParent'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
caught noParent
```

<!-- test: filepath-parent-under-drive-root -->
```maxon
function main() returns ExitCode
	let p = FilePath from "C:\\foo"
	let parent = try p.parent() otherwise panic("no parent")
	print("{parent}\n")
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
C:\
```
```stdout
C:
```

<!-- test: filepath-parent-of-drive-root-throws -->
```maxon
function main() returns ExitCode
	let p = FilePath from "C:\\"
	if let parent = try p.parent() 'hasParent'
		print("{parent}\n")
	end 'hasParent' else 'noParent'
		print("caught noParent\n")
	end 'noParent'
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
caught noParent
```
```stdout
C:
```

<!-- test: filepath-parent-under-unc-root -->
```maxon
function main() returns ExitCode
	let p = FilePath from "\\\\server\\share\\foo"
	let parent = try p.parent() otherwise panic("no parent")
	print("{parent}\n")
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
\\server\share\
```
```stdout
//server/share
```

<!-- test: filepath-parent-drive-relative-throws -->
```maxon
function main() returns ExitCode
	let p = FilePath from "C:foo"
	try p.parent() otherwise 'noParent'
		print("caught noParent\n")
		return 0
	end 'noParent'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
caught noParent
```

<!-- test: filepath-parent-walk-up-terminates -->
```maxon
function main() returns ExitCode
	var level = FilePath from "/a/b/c"
	var steps = 0

	while steps < 10 'upwards'
		let above = try level.parent() otherwise 'atTheTop'
			print("top after {steps}\n")
			return 0
		end 'atTheTop'

		if above.isEmpty() 'empty'
			print("empty parent at {steps}\n")
			return 1
		end 'empty'

		print("{above}\n")
		level = above
		steps = steps + 1
	end 'upwards'

	print("did not terminate\n")
	return 1
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
\a\b
\a
\
top after 3
```
```stdout
/a/b
/a
/
top after 3
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
```Stdout:x64-windows
C:\Users\test
```
```stdout
C:/Users/test
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
```Stdout:x64-windows
C:\Users\test
```
```stdout
C:/Users/test
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
```Stdout:x64-windows
C:\Users\docs\readme.md
```
```stdout
C:/Users/docs/readme.md
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
```Stdout:x64-windows
C:\Users\file.md
```
```stdout
C:/Users/file.md
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
```Stdout:x64-windows
C:\Users\test
```
```stdout
C:/Users/test
```

<!-- test: filepath-normalize-folds-dot-segments -->
```maxon
function main() returns ExitCode
	for spelled in ["a/./b/../c", "a//b/", "../a/../../b", "a/..", "./", ""] 'eachPath'
		let p = try FilePath.from(spelled) otherwise panic("unspellable path '{spelled}'")
		print("[{p.normalize()}]\n")
	end 'eachPath'

	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
[a\c]
[a\b]
[..\..\b]
[.]
[.]
[]
```
```stdout
[a/c]
[a/b]
[../../b]
[.]
[.]
[]
```

<!-- test: filepath-normalize-stops-at-the-root -->
```maxon
function main() returns ExitCode
	#if os(Windows)
		let spellings = ["C:/x/../../y", "C:/..", "//server/share/x/../..", "C:a/../..", "/x/.."]
	#else
		let spellings = ["/x/../../y", "/..", "//a/./b", "/x/..", "/"]
	#endif

	for spelled in spellings 'eachPath'
		let p = try FilePath.from(spelled) otherwise panic("unspellable path '{spelled}'")
		print("[{p.normalize()}]\n")
	end 'eachPath'

	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
[C:\y]
[C:\]
[\\server\share\]
[C:..]
[\]
```
```stdout
[/y]
[/]
[/a/b]
[/]
[/]
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
```Stdout:x64-windows
\
```
```stdout
/
```

<!-- test: filepath-resolve-relative -->
```maxon
function main() returns ExitCode
	let base = FilePath from "C:\\Users"
	let rel = FilePath from "docs"
	let resolved = rel.resolve(base)
	print("{resolved}\n")
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
C:\Users\docs
```
```stdout
C:/Users/docs
```

<!-- test: filepath-resolve-absolute-unchanged -->
```maxon
function main() returns ExitCode
	let base = FilePath from "C:\\Other"
	let abs = FilePath from "C:\\Users\\file.txt"
	let resolved = abs.resolve(base)
	print("{resolved}\n")
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
C:\Users\file.txt
```
```stdout
C:/Other/C:/Users/file.txt
```

<!-- test: filepath-resolve-folds-dot-segments -->
```maxon
function main() returns ExitCode
	#if os(Windows)
		let base = FilePath from "C:/work/project"
		let absolute = FilePath from "C:/work/./runtime/../stdlib/Clock.maxon"
	#else
		let base = FilePath from "/work/project"
		let absolute = FilePath from "/work/./runtime/../stdlib/Clock.maxon"
	#endif

	for spelled in ["./runtime/Clock.maxon", "runtime/./Clock.maxon", "../../runtime/Clock.maxon", "."] 'eachPath'
		let p = try FilePath.from(spelled) otherwise panic("unspellable path '{spelled}'")
		print("[{p.resolve(base)}]\n")
	end 'eachPath'

	print("[{absolute.resolve(base)}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
[C:\work\project\runtime\Clock.maxon]
[C:\work\project\runtime\Clock.maxon]
[C:\runtime\Clock.maxon]
[C:\work\project]
[C:\work\stdlib\Clock.maxon]
```
```stdout
[/work/project/runtime/Clock.maxon]
[/work/project/runtime/Clock.maxon]
[/runtime/Clock.maxon]
[/work/project]
[/work/stdlib/Clock.maxon]
```

<!-- test: filepath-resolve-drive-relative -->
```maxon
function main() returns ExitCode
	#if os(Windows)
		let sameDrive = FilePath from "C:/work/project"
		let otherDrive = FilePath from "D:/elsewhere"
	#else
		let sameDrive = FilePath from "/work/project"
		let otherDrive = FilePath from "/elsewhere"
	#endif

	for spelled in ["C:src/main.maxon", "c:src/../lib", "C:"] 'eachPath'
		let p = try FilePath.from(spelled) otherwise panic("unspellable path '{spelled}'")
		let elsewhere = p.resolve(otherDrive)
		print("[{p.resolve(sameDrive)}] [{elsewhere}] {elsewhere.isAbsolute()}\n")
	end 'eachPath'

	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
[C:\work\project\src\main.maxon] [C:src\main.maxon] false
[C:\work\project\lib] [c:lib] false
[C:\work\project] [C:.] false
```
```stdout
[/work/project/C:src/main.maxon] [/elsewhere/C:src/main.maxon] true
[/work/project/lib] [/elsewhere/lib] true
[/work/project/C:] [/elsewhere/C:] true
```

<!-- test: filepath-anchored-at-keeps-parent-components -->
```maxon
function main() returns ExitCode
	#if os(Windows)
		let base = FilePath from "C:/work/project"
		let absolute = FilePath from "C:/work/./runtime/../stdlib//Clock.maxon/"
	#else
		let base = FilePath from "/work/project"
		let absolute = FilePath from "/work/./runtime/../stdlib//Clock.maxon/"
	#endif

	for spelled in ["./runtime/Clock.maxon", "runtime/./Clock.maxon", "a//b/", "../../runtime/Clock.maxon", "a/../b", ".", ""] 'eachPath'
		let p = try FilePath.from(spelled) otherwise panic("unspellable path '{spelled}'")
		print("[{p.anchoredAt(base)}]\n")
	end 'eachPath'

	print("[{absolute.anchoredAt(base)}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
[C:\work\project\runtime\Clock.maxon]
[C:\work\project\runtime\Clock.maxon]
[C:\work\project\a\b]
[C:\work\project\..\..\runtime\Clock.maxon]
[C:\work\project\a\..\b]
[C:\work\project]
[C:\work\project]
[C:\work\runtime\..\stdlib\Clock.maxon]
```
```stdout
[/work/project/runtime/Clock.maxon]
[/work/project/runtime/Clock.maxon]
[/work/project/a/b]
[/work/project/../../runtime/Clock.maxon]
[/work/project/a/../b]
[/work/project]
[/work/project]
[/work/runtime/../stdlib/Clock.maxon]
```

<!-- test: filepath-anchored-at-roots-and-drives -->
```maxon
function main() returns ExitCode
	#if os(Windows)
		let sameDrive = FilePath from "C:/work/project"
		let otherDrive = FilePath from "D:/elsewhere"
		let roots = ["C:/..", "C:/", "//server/share", "//server/share/x/..", "/x/./"]
	#else
		let sameDrive = FilePath from "/work/project"
		let otherDrive = FilePath from "/elsewhere"
		let roots = ["/..", "/", "//a/./b", "/x/./"]
	#endif

	for spelled in roots 'eachRoot'
		let p = try FilePath.from(spelled) otherwise panic("unspellable path '{spelled}'")
		print("[{p.anchoredAt(sameDrive)}]\n")
	end 'eachRoot'

	for spelled in ["C:src/main.maxon", "c:src/../lib", "C:", "C:./x"] 'eachPath'
		let p = try FilePath.from(spelled) otherwise panic("unspellable path '{spelled}'")
		let elsewhere = p.anchoredAt(otherDrive)
		print("[{p.anchoredAt(sameDrive)}] [{elsewhere}] {elsewhere.isAbsolute()}\n")
	end 'eachPath'

	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
[C:\..]
[C:\]
[\\server\share]
[\\server\share\x\..]
[\x]
[C:\work\project\src\main.maxon] [C:src\main.maxon] false
[C:\work\project\src\..\lib] [c:src\..\lib] false
[C:\work\project] [C:.] false
[C:\work\project\x] [C:x] false
```
```stdout
[/..]
[/]
[/a/b]
[/x]
[/work/project/C:src/main.maxon] [/elsewhere/C:src/main.maxon] true
[/work/project/c:src/../lib] [/elsewhere/c:src/../lib] true
[/work/project/C:] [/elsewhere/C:] true
[/work/project/C:./x] [/elsewhere/C:./x] true
```

<!-- test: filepath-path-immutable -->
```maxon
function main() returns ExitCode
	let p = FilePath from "hello.txt"
	print("{p.path}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello.txt
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
```Stdout:x64-windows
C:\Users\docs
```
```stdout
C:/Users/docs
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
	let p = try FilePath.from("https://example.com/path") otherwise 'err'
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
```Stdout:x64-windows
C:\valid\path.txt
```
```stdout
C:/valid/path.txt
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
```Stdout:x64-windows
\tmp\test.txt
```
```stdout
/tmp/test.txt
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
```Stdout:x64-windows
\tmp\test.txt
```
```stdout
/tmp/test.txt
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
```Stdout:x64-windows
\home\user\file.txt
```
```stdout
/home/user/file.txt
```

<!-- test: filepath-file-url-escapes-are-decoded -->
```maxon
function main() returns ExitCode
	let p = try FilePath.from("file:///tmp/a%20b/%C3%A9t%C3%A9.txt") otherwise panic("bad path")
	print("{p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
\tmp\a b\été.txt
```
```stdout
/tmp/a b/été.txt
```

<!-- test: filepath-file-url-escaped-drive-colon -->
```maxon
function main() returns ExitCode
	let p = try FilePath.from("file:///c%3A/Users/file.txt") otherwise panic("bad path")
	print("{p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```Stdout:x64-windows
c:\Users\file.txt
```
```stdout
/c:/Users/file.txt
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

<!-- test: filepath-malformed-url -->
```maxon
function main() returns ExitCode
	try FilePath.from("file://[unclosed/path.txt") otherwise (e) 'err'
		print("caught {e.name}\n")
		return 0
	end 'err'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
caught malformedURL
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
```Stdout:x64-windows
C:\Users\normal\path.txt
```
```stdout
C:/Users/normal/path.txt
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
```Stdout:x64-windows
path='\'
empty=false
```
```stdout
path='/'
empty=false
```
