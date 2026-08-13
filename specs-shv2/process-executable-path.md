---
feature: process-executable-path
status: stable
keywords: [process, executablePath, introspection, __Builtins, intrinsics, FilePath]
category: stdlib
---

# `Process.executablePath()` — the running program's own path

## Documentation

`stdlib/Process.maxon` is introspection of the CURRENT process (for launching children, see
`stdlib/Subprocess.maxon`). It carries one function, and that function bottoms out in one compiler
intrinsic:

| Intrinsic | Meaning |
|---|---|
| `__Builtins.executablePath()` | the absolute path of the running executable, as a FRESH owned `__ManagedMemory` |

`Process.executablePath()` turns that buffer into a `String`, parses it as a `FilePath`, and
surfaces a parse failure as `ProcessIntrospectionError.pathUnavailable`.

### The path is MACHINE-SPECIFIC, so every case here asserts a PROPERTY

There is no literal to compare against: the path is wherever this checkout happens to live, and it
differs between the Windows lane and the Mac lane. A case that baked one in would be a case that
fails on the other host for a reason that has nothing to do with the mechanism. So the four
properties asserted below are all the machine cannot vary:

- it is NON-EMPTY (the canonical `/specs/command-line-args.md:executable-path` case);
- it is DRIVE-QUALIFIED, i.e. absolute rather than relative;
- it NAMES A FILE THAT EXISTS — `File.exists` on the answer is true, which is the strongest of the
  four because it is the OS agreeing that the string round-trips back to the same object;
- it is STABLE — two independent calls agree, which pins the retry loop and the copy-out rather
  than merely the first success.

### `ProcessIntrospectionError.pathUnavailable` has no reachable case here, and that is measured

`executablePath`'s only throwing edge is `try FilePath.from(path) otherwise throw
ProcessIntrospectionError.pathUnavailable`. Two things have to line up for it to fire, and on this
lane neither does:

- `GetModuleFileNameA(NULL, …)` cannot fail for the CURRENT module — the only documented failure is
  a buffer too small, which `__proc_exe_path` answers by doubling and retrying rather than by
  giving up. `/specs/builtins-type.md` describes the intrinsic's failure answer as an *"empty
  buffer"*;
- and `FilePath.from("")` does not throw. `FilePath.create` refuses only invalid CHARACTERS
  (`FilePathError.invalidCharacter`), and the empty string has none.

So the arm is unreachable from a program on this target rather than merely untested, and a case
that faked it — by shadowing `Process` or by hand-building an empty path — would pin the fake and
not the mechanism. It is stated here instead. ⚠ It is worth knowing that this makes the stdlib's
`pathUnavailable` DEAD on Windows: an empty buffer flows through as an empty `FilePath` rather than
as the error `/specs/builtins-type.md` says it becomes. That is a `stdlib/` fact and not this
rung's to change.

### The substrate is x64-windows only at this rung

`__Builtins.executablePath` lowers to `__proc_exe_path`, which reads `GetModuleFileNameA` through
the Win32 import table. It joins the file, directory and command-line families under
`SemanticCheck.calleeNeedsWin32Substrate`, so a program that reaches it on another target is
refused with `E3104` at the call's own span. The three platforms genuinely differ here
(`GetModuleFileNameA`, `_NSGetExecutablePath`, `/proc/self/exe`), which is why serving another one
is a rung rather than a lowering — the same argument `Runtime/CommandLineRuntime.maxon`'s header
makes for argv.

## Tests

<!-- test: process-executable-path.is-not-empty -->
<!-- targets: x64-windows -->
The canonical property, in the canonical spec's own shape
(`/specs/command-line-args.md:executable-path`): the call succeeds and answers something.
```maxon
function main() returns ExitCode
	let exe = try Process.executablePath() otherwise return 2
	if exe.path.byteLength() > 0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: process-executable-path.names-a-file-that-exists -->
<!-- targets: x64-windows -->
The strongest property available without a literal: hand the answer straight back to the OS. A
truncated path, a path with the NUL still in it, or a length taken from the buffer's CAPACITY
rather than from `GetModuleFileNameA`'s return would all name nothing.
```maxon
function main() returns ExitCode
	let exe = try Process.executablePath() otherwise return 2
	if File.exists(exe) 'found'
		return 7
	end 'found'
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: process-executable-path.is-absolute -->
<!-- targets: x64-windows -->
An absolute Windows path is drive-qualified, and the answer is longer than the drive prefix alone.
A relative path — which is what a lowering that answered `argv[0]` verbatim could produce — has no
colon in it.
```maxon
function main() returns ExitCode
	let exe = try Process.executablePath() otherwise return 2
	var score = 0
	if exe.path.byteLength() > 3 'longerThanADrive'
		score = score + 1
	end 'longerThanADrive'
	if exe.path.contains(":") 'driveQualified'
		score = score + 2
	end 'driveQualified'
	return score as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: process-executable-path.is-stable-across-calls -->
<!-- targets: x64-windows -->
Two independent calls agree. Each one allocates its own buffer, asks the OS again and copies the
answer out, so this pins the whole retry-and-copy path rather than a single lucky first call — a
buffer reused across calls, or a length left over from the previous one, shows up here as a
mismatch.
```maxon
function main() returns ExitCode
	let first = try Process.executablePath() otherwise return 2
	let second = try Process.executablePath() otherwise return 3
	if first.path == second.path 'agree'
		return 4
	end 'agree'
	return 1
end 'main'
```
```exitcode
4
```

<!-- test: process-executable-path.builtin-answers-a-non-empty-buffer -->
<!-- targets: x64-windows -->
The intrinsic under the module, driven directly, so a failure attributes to the runtime entry
rather than to `FilePath` parsing above it.
```maxon
function main() returns ExitCode
	let mm = __Builtins.executablePath()
	if mm.length() > 0 'gotAPath'
		return 5
	end 'gotAPath'
	return 1
end 'main'
```
```exitcode
5
```

<!-- test: process-executable-path.builtin-result-is-owned -->
<!-- targets: x64-windows -->
Each call answers a FRESH owned `__ManagedMemory`, dropped at the end of the statement that bound
it. Fifty of them in a loop is the leak gate's shape: a missing drop is a non-zero mm balance at
exit, which the runtime reports as exit 101 rather than as a wrong number. The lengths are summed
so every answer is read, not merely produced.
```maxon
function main() returns ExitCode
	var seen = 0
	var i = 0
	while i < 50 'reads'
		let mm = __Builtins.executablePath()
		if mm.length() > 0 'nonEmpty'
			seen = seen + 1
		end 'nonEmpty'
		i = i + 1
	end 'reads'
	if seen == 50 'allFifty'
		return 6
	end 'allFifty'
	return 1
end 'main'
```
```exitcode
6
```

<!-- test: process-executable-path.builtin-arity-checked -->
`executablePath` takes no arguments. An intrinsic has no signature for the ordinary arity check to
read, so it is refused by the same `builtinArity` check `trunc`/`sleep`/`commandLineCount` use.
```maxon
function main() returns ExitCode
	return __Builtins.executablePath(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.executablePath' takes exactly 0 argument, but 1 were given
```

<!-- test: process-executable-path.rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
The introspection substrate is x64-windows only at this rung. On any other target the call is
refused at its source span with `E3104`, naming the runtime entry that has no lowering there —
never a panic from inside the wasm backend.
```maxon
function main() returns ExitCode
	let mm = __Builtins.executablePath()
	return mm.length() as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:22: this construct is x64-windows only at this rung: it lowers to the runtime entry '__proc_exe_path', which has no wasm32-wasi implementation
```
