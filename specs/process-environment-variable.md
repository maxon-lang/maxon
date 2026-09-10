---
feature: process-environment-variable
status: stable
keywords: [process, environmentVariable, environment, introspection, __Builtins, osEnvironmentEntry]
category: stdlib
---

# `Process.environmentVariable()` — one variable out of this process's environment

## Documentation

`stdlib/Process.maxon` is introspection of the CURRENT process. Beside `executablePath()` it answers
one variable of this process's own environment BY NAME:

```maxon
let home = try Process.environmentVariable("PATH") otherwise return 1
```

It walks the same entry list `stdlib/Subprocess.maxon` builds a child's environment from
(`__Builtins.osEnvironmentEntry(i)`, index 0 upward until an empty answer), so there is ONE reading of
this process's environment rather than two that could disagree. A name nothing in the environment
carries throws `ProcessIntrospectionError.variableUnset` — it does NOT answer the empty string, which
is a value a variable can genuinely hold.

### The name is matched case-insensitively on Windows and exactly under POSIX

Windows environment names are case-insensitive and the block spells the search path `Path`, not
`PATH`; POSIX names are case-sensitive and two names differing only in case are two variables. So the
comparison folds case on Windows and nowhere else, and the case below that reads `PATH` is the pin for
it: byte-exact matching would answer `variableUnset` on the Windows lane for a variable that is
plainly set.

### The VALUE is everything past the first `=`, and the search for it starts at byte 1

An entry is `NAME=VALUE`, and a value may itself contain `=`. The split therefore takes the FIRST `=`
and keeps the whole remainder. It starts at byte 1 rather than byte 0 because a Windows process's
environment opens with entries like `=C:=C:\dir` recording each drive's working directory, whose NAME
is `=C:`; splitting at byte 0 would give every one of them the empty name.

### Not on `wasm32-wasi`

The walk reaches the environment through `__gt_subp_env_entry`, which sits behind the subprocess
band's runtime prefix, so a program naming it is refused on that lane at compile time with **E3074**
(`subprocess-unsupported.md` pins that refusal). E3074 is NOT the target refusal the harness folds
into a counted skip — only E3104 is — so each case below carries an `unsupported-targets:` marker
excluding that lane alone, exactly as `subprocess.md`'s do.

## Tests

<!-- test: process-environment-variable.finds-a-variable-every-host-has -->
<!-- unsupported-targets: wasm32-wasi -->
Every host this compiler runs on sets a search path, so a non-empty answer for `PATH` is the one
value assertion that is true everywhere. On Windows it is ALSO the case-fold pin: the environment
block there spells the name `Path`, so a byte-exact comparison answers `variableUnset` here.
```maxon
function main() returns ExitCode
	let value = try Process.environmentVariable("PATH") otherwise return 2
	if value.isEmpty() 'empty'
		return 3
	end 'empty'

	return 7
end 'main'
```
```exitcode
7
```

<!-- test: process-environment-variable.unset-name-throws -->
<!-- unsupported-targets: wasm32-wasi -->
⭐ A name nothing carries THROWS rather than answering the empty string, because a variable set to the
empty string is a different fact from one that is not set at all, and a caller choosing a fallback has
to be able to tell them apart.
```maxon
function main() returns ExitCode
	let value = try Process.environmentVariable("MAXON_SPEC_NO_SUCH_VARIABLE_9F3A") otherwise 'unset'
		return 7
	end 'unset'

	print("answered '{value}' for a name nothing sets\n")
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: process-environment-variable.value-keeps-everything-past-the-first-separator -->
<!-- unsupported-targets: wasm32-wasi -->
⭐⭐ The split keeps the WHOLE remainder. This program spawns ITSELF with one variable whose value
contains an `=`, and the child echoes what it read back: a split that stopped at the first separator
would answer `a` where the whole value is `a=b`.
```maxon
typealias StringArray = Array with String

function child() returns ExitCode
	let value = try Process.environmentVariable("MAXON_SPEC_ENV_EQ") otherwise 'unset'
		print("unset\n")
		return 3
	end 'unset'

	print("{value}\n")
	return 0
end 'child'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	var argv = StringArray.create()
	argv.push("child")

	var config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_SPEC_ENV_EQ": "a=b"])
	let run = try Subprocess.runConfiguration(config) otherwise return 4

	print("child said {run.stdout.trim()}\n")
	return 7
end 'main'
```
```stdout
child said a=b
```
```exitcode
7
```
