---
feature: command-line-args
status: stable
keywords: [args, argv, command-line, main, CommandLine]
category: functions
---

# Command Line Arguments

## Documentation

# Command Line Arguments

Access command line arguments in Maxon using the `CommandLine` stdlib type.

## Syntax

```text
function main() returns int
  let args = CommandLine.args()                                            // All arguments (including argv[0])
  let exe = try Process.executablePath() otherwise return 2                // The executable path (uses OS API; throws ProcessIntrospectionError)
  return 0
end 'main'
```

## API

- `CommandLine.args()` - Returns `StringArray` (where `type StringArray implements Array with String`) containing all command line arguments (including argv[0])
- `Process.executablePath()` - Returns `FilePath` containing the absolute executable path (uses GetModuleFileNameA on Windows, _NSGetExecutablePath on macOS, /proc/self/exe on Linux). Throws `ProcessIntrospectionError.pathUnavailable` if the OS lookup fails.

## Properties

With `let args = CommandLine.args()`:
- `args[0]` - First argument (typically the program name/path)
- `args[1]`, `args[2]`, etc. - Additional arguments
- `args.count()` - Total number of arguments

## Example

```text
function main() returns int
  let exe = try Process.executablePath() otherwise return 2
  print("Program: ")
  print(exe.path)
  
  let args = CommandLine.args()
  if args.count() > 1 'has-args'
    print("First user argument: ")
    var first = try args.get(1) otherwise ""
    print(first)
  end 'has-args'
  
  return 0
end 'main'
```

Running `myprogram.exe hello world` would output:
```
Program: C:\path\to\myprogram.exe
First user argument: hello
```

## Iterating Over Arguments

```text
function main() returns int
  let args = CommandLine.args()
  for arg in args 'loop'
    print(arg)
  end 'loop'
  return 0
end 'main'
```

## Notes

- Arguments are UTF-8 encoded strings
- Arguments containing spaces must be quoted at the shell level
- Each call to `args()` re-parses the command line
- `Process.executablePath()` returns the absolute path via OS-specific APIs, or throws `ProcessIntrospectionError.pathUnavailable` if the lookup fails
- Platform support: Windows (full), Linux (not yet implemented)

## Tests

<!-- test: args-length-no-extra -->
```maxon
function main() returns ExitCode
	// Without extra args, count should be 1 (just argv[0])
	let args = CommandLine.args()
	return args.count()
end 'main'
```
```exitcode
1
```

<!-- test: args-with-one-arg -->
<!-- Args: hello -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	return args.count()
end 'main'
```
```exitcode
2
```

<!-- test: args-with-multiple-args -->
<!-- Args: one two three -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	return args.count()
end 'main'
```
```exitcode
4
```

<!-- test: access-first-arg -->
<!-- Args: hello -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let arg = try args.get(1) otherwise ""
	print(arg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: access-multiple-args -->
<!-- Args: foo bar baz -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let arg1 = try args.get(1) otherwise ""
	let arg2 = try args.get(2) otherwise ""
	let arg3 = try args.get(3) otherwise ""
	print(arg1)
	print("\n")
	print(arg2)
	print("\n")
	print(arg3)
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
foo
bar
baz
```

<!-- test: iterate-args -->
<!-- Args: a b c -->
```maxon
function main() returns ExitCode
	// Skip argv[0] and print the rest
	let args = CommandLine.args()
	var i = 1
	while i < args.count() 'loop'
		let arg = try args.get(i) otherwise ""
		print(arg)
		print("\n")
		i = i + 1
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a
b
c
```

<!-- test: numeric-args -->
<!-- Args: 42 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	// Args include argv[0], so count == 2 with one user arg
	if args.count() == 2 'check'
		let arg = try args.get(1) otherwise ""
		print(arg)
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: empty-string-arg -->
<!-- Args: "" -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	// Empty quoted arg is at index 1 (index 0 is argv[0])
	let arg = try args.get(1) otherwise "x"
	if arg == "" 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: arg-with-equals -->
<!-- Args: --key=value -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let arg = try args.get(1) otherwise ""
	print(arg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
--key=value
```

<!-- test: executable-path -->
```maxon
function main() returns ExitCode
	let exe = try Process.executablePath() otherwise return 2
	// Just verify it returns something (the actual path varies)
	if exe.path.byteLength() > 0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
