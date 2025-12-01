---
feature: command-line-args
status: stable
keywords: [args, argv, command-line, main, parameters]
category: functions
---

# Command Line Arguments

## Developer Notes

Command line arguments are passed to `main()` as an array of strings when the signature includes `args []string`.

### Main Function Signatures

The `main()` function supports two signatures:

1. `function main() int` - No arguments
2. `function main(args []string) int` - Receives command line arguments

### Implementation

**Parser:**
- `parseMainFunction()` in `parser.cpp` detects the `args []string` parameter
- Sets `mainTakesArgs` flag when the signature includes args parameter

**Codegen (codegen_mir.cpp):**
- Checks `mainTakesArgs` to determine whether to pass arguments to `main()`
- On Windows: calls `__get_command_args()` which returns `{ ptr data, i32 length }`
- On Linux: TODO - currently passes null/zero (not yet implemented)

**Runtime (runtime_windows.mir):**
- `__get_command_args()` function implemented in `runtime_windows.mir`
- Uses Windows APIs: `GetCommandLineW()`, `CommandLineToArgvW()`, `WideCharToMultiByte()`
- Converts wide strings to UTF-8 encoded Maxon strings
- Returns array of string structs (16 bytes each for SSO layout)

### Memory Layout

The args array is a `[]string` (dynamic array of strings):
- Array struct: `{ ptr data, i32 length, i32 capacity }`
- Each string element: 16 bytes (SSO format - see string-type.md)

### Platform Support

- **Windows**: Fully implemented
- **Linux**: Not yet implemented (passes empty array)

## Documentation

# Command Line Arguments

Access command line arguments in Maxon by adding `args []string` parameter to `main()`.

## Syntax

```text
function main(args []string) int
    // args[0] is the program name/path
    // args[1..n] are user-provided arguments
    return 0
end 'main'
```

## Properties

- `args[0]` - The program name or path (always present)
- `args[1]`, `args[2]`, etc. - User-provided arguments
- `args.length` - Total number of arguments (including program name)

## Example

```text
function main(args []string) int
    print("Program: ")
    print(args[0])
    
    if args.length > 1 'has-args'
        print("First argument: ")
        print(args[1])
    end 'has-args'
    
    return 0
end 'main'
```

Running `myprogram.exe hello world` would output:
```
Program: myprogram.exe
First argument: hello
```

## Iterating Over Arguments

```text
function main(args []string) int
    for arg in args 'loop'
        print(arg)
    end 'loop'
    return 0
end 'main'
```

## Notes

- Arguments are UTF-8 encoded strings
- Arguments containing spaces must be quoted at the shell level
- The array is zero-indexed
- Platform support: Windows (full), Linux (not yet implemented)

## Tests

<!-- test: args-length-no-extra -->
```maxon
function main(args []string) int
    // Without extra args, length should be 1 (just program name)
    return args.length
end 'main'
```
```exitcode
1
```

<!-- test: args-with-one-arg -->
<!-- Args: hello -->
```maxon
function main(args []string) int
    return args.length
end 'main'
```
```exitcode
2
```

<!-- test: args-with-multiple-args -->
<!-- Args: one two three -->
```maxon
function main(args []string) int
    return args.length
end 'main'
```
```exitcode
4
```

<!-- test: access-first-arg -->
<!-- Args: hello -->
```maxon
function main(args []string) int
    print(args[1])
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
function main(args []string) int
    print(args[1])
    print(args[2])
    print(args[3])
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
function main(args []string) int
    var i = 1
    while i < args.length 'loop'
        print(args[i])
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
function main(args []string) int
    // Args are strings, just verify we can access them
    if args.length == 2 'check'
        print(args[1])
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
function main(args []string) int
    // Empty quoted arg
    if args[1] == "" 'check'
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
function main(args []string) int
    print(args[1])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
--key=value
```

