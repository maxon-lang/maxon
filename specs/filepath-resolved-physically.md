---
feature: filepath-resolved-physically
status: experimental
keywords: [filepath, path, symlink, realpath, physical, canonical]
category: stdlib
---

# FilePath.resolvedPhysically

## Documentation

`resolvedPhysically()` asks the filesystem for the path's one physical spelling: every symbolic link along it is
followed, every `..` is taken against the directory the walk has reached (so after a link it climbs out of the
link's target, not out of the link's name), and a relative path is taken against the working directory. Two
spellings of one file answer the same path, which is what makes the result a key where `normalize()` — lexical,
and blind to links — is not.

The path has to exist. A path that names nothing throws `PathResolveError.notFound`; one the process may not
look up throws `accessDenied`; any other refusal (a link loop, a name too long for the host) is `failed`.

## Tests

<!-- test: filepath-resolved-physically-follows-links-and-dotdot -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
A link, a link whose target runs through another link, and a `..` taken after a link all resolve to the one
physical file. The directory holding the case is resolved first, so a working directory that is itself reached
through a link does not matter.
```maxon
function main() returns ExitCode
	let base = (FilePath from "temp/resolved-physically-links").resolve(Directory.currentPath())
	let inner = base.join("real").join("inner")

	if not Directory.create(inner) 'noDirectory'
		return 1
	end 'noDirectory'

	try File.writeText(inner.join("f.txt"), content: "x") otherwise return 2

	let shell = try FilePath.from("/bin/sh") otherwise return 3
	let script = "cd '{base.toString()}' && rm -f link nested && ln -s real link && ln -s link/inner nested"
	let made = try Subprocess.run(Executable.path(shell), arguments: ["-c", script]) otherwise return 4

	if not made.succeeded() 'noLinks'
		return 5
	end 'noLinks'

	let root = try base.resolvedPhysically() otherwise return 6
	let file = root.join("real").join("inner").join("f.txt")

	let viaLink = try base.join("link").join("inner").join("f.txt").resolvedPhysically() otherwise return 7
	let viaNested = try base.join("nested").join("f.txt").resolvedPhysically() otherwise return 8
	let viaDotDot = try base.join("nested").join("..").join("inner").join("f.txt").resolvedPhysically() otherwise return 9
	let aLinkItself = try base.join("link").resolvedPhysically() otherwise return 10

	print("{viaLink.equals(file)}\n")
	print("{viaNested.equals(file)}\n")
	print("{viaDotDot.equals(file)}\n")
	print("{aLinkItself.equals(root.join("real"))}\n")

	let cleaned = try Subprocess.run(Executable.path(shell), arguments: ["-c", "rm -rf '{base.toString()}'"]) otherwise return 11

	if not cleaned.succeeded() 'notCleaned'
		return 12
	end 'notCleaned'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
true
true
true
```

<!-- test: filepath-resolved-physically-follows-a-junction -->
<!-- unsupported-targets: arm64-macos, arm64-linux, x64-linux, wasm32-wasi -->
A Windows directory junction, and a file reached through one, resolve to the one physical file, spelled with
a drive letter and no `\\?\` prefix. The junction is made with PowerShell, which needs no elevation for one.
```maxon
function main() returns ExitCode
	let base = (FilePath from "temp\\resolved-physically-junction").resolve(Directory.currentPath())
	let inner = base.join("real").join("inner")

	if not Directory.create(inner) 'noDirectory'
		return 1
	end 'noDirectory'

	try File.writeText(inner.join("f.txt"), content: "x") otherwise return 2

	let script = "New-Item -ItemType Junction -Path '{base.join("link").toString()}' -Target '{base.join("real").toString()}' | Out-Null"
	let made = try Subprocess.run(Executable.name("powershell"), arguments: ["-NoProfile", "-Command", script]) otherwise return 3

	if not made.succeeded() 'noJunction'
		return 4
	end 'noJunction'

	let root = try base.resolvedPhysically() otherwise return 5
	let file = root.join("real").join("inner").join("f.txt")

	let viaJunction = try base.join("link").join("inner").join("f.txt").resolvedPhysically() otherwise return 6
	let theJunction = try base.join("link").resolvedPhysically() otherwise return 7

	print("{viaJunction.equals(file)}\n")
	print("{theJunction.equals(root.join("real"))}\n")
	print("{file.isAbsolute()}\n")
	print("{file.toString().contains("?")}\n")

	let cleaned = try Subprocess.run(Executable.name("powershell"), arguments: ["-NoProfile", "-Command", "Remove-Item -LiteralPath '{base.join("link").toString()}' -Force; Remove-Item -LiteralPath '{base.toString()}' -Recurse -Force"]) otherwise return 8

	if not cleaned.succeeded() 'notCleaned'
		return 9
	end 'notCleaned'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
true
true
false
```

<!-- test: filepath-resolved-physically-throws-for-a-missing-path -->
A path that names nothing throws `notFound`, whether its last component or an earlier one is missing.
```maxon
function main() returns ExitCode
	let here = Directory.currentPath()
	let missing = here.join("resolved-physically-no-such-entry")
	let beneath = missing.join("deeper")

	try missing.resolvedPhysically() otherwise (e) 'refused'
		print("{e.name}\n")
	end 'refused'

	try beneath.resolvedPhysically() otherwise (e) 'beneathRefused'
		print("{e.name}\n")
	end 'beneathRefused'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
notFound
notFound
```

<!-- test: filepath-resolved-physically-throws-not-found-below-a-regular-file -->
A path that runs through a regular file, as if it were a directory, names nothing: `notFound`, the same as a
missing name, so a walk that ascends on `notFound` alone treats both as absence.
```maxon
function main() returns ExitCode
	let file = Directory.currentPath().join("resolved-physically-a-regular-file.txt")

	try File.writeText(file, content: "x") otherwise return 1

	try file.join("beneath").resolvedPhysically() otherwise (e) 'refused'
		print("{e.name}\n")
	end 'refused'

	try File.delete(file) otherwise return 2

	return 0
end 'main'
```
```exitcode
0
```
```stdout
notFound
```

<!-- test: filepath-resolved-physically-answers-an-absolute-fixed-point -->
A relative path is resolved against the working directory, the answer is absolute, and resolving an answer
again changes nothing.
```maxon
function main() returns ExitCode
	let here = try (FilePath from ".").resolvedPhysically() otherwise return 1
	let again = try here.resolvedPhysically() otherwise return 2

	print("{here.isAbsolute()}\n")
	print("{here.equals(again)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
true
```
