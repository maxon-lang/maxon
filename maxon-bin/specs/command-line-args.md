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
    let args = CommandLine.args()           // User arguments (excludes executable)
    let exe = CommandLine.executablePath()  // The executable path
    return 0
end 'main'
```

## API

- `CommandLine.args()` - Returns `Array of String` containing user arguments (excludes executable path)
- `CommandLine.executablePath()` - Returns `String` containing the executable path

## Properties

With `let args = CommandLine.args()`:
- `args[0]` - First user-provided argument
- `args[1]`, `args[2]`, etc. - Additional user-provided arguments
- `args.count()` - Number of user arguments (0 if no arguments provided)

## Example

```text
function main() returns int
    let exe = CommandLine.executablePath()
    print("Program: ")
    print(exe)
    
    let args = CommandLine.args()
    if args.count() > 0 'has-args'
        print("First argument: ")
        var first = try args.get(0) otherwise ""
        print(first)
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
- Each call to `args()` or `executablePath()` re-parses the command line
- Platform support: Windows (full), Linux (not yet implemented)

## Tests

<!-- test: args-length-no-extra -->
```maxon
function main() returns int
    // Without extra args, count should be 0 (no user arguments)
    let args = CommandLine.args()
    return args.count()
end 'main'
```
```exitcode
0
```

<!-- test: args-with-one-arg -->
<!-- Args: hello -->
```maxon
function main() returns int
    let args = CommandLine.args()
    return args.count()
end 'main'
```
```exitcode
1
```

<!-- test: args-with-multiple-args -->
<!-- Args: one two three -->
```maxon
function main() returns int
    let args = CommandLine.args()
    return args.count()
end 'main'
```
```exitcode
3
```

<!-- test: access-first-arg -->
<!-- Args: hello -->
```maxon
function main() returns int
    let args = CommandLine.args()
    var arg = try args.get(0) otherwise ""
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
<!-- TrackMemory: true -->
```maxon
function main() returns int
    let args = CommandLine.args()
    var arg1 = try args.get(0) otherwise ""
    var arg2 = try args.get(1) otherwise ""
    var arg3 = try args.get(2) otherwise ""
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
ALLOC #1: 4 bytes (command line arg)
MOVE: managed
ALLOC #2: 168 bytes (array grow)
INCREF: array grow -> rc=1
INCREF: <array_store> -> rc=2
FREE #1: 4 bytes (cstring release)
ALLOC #3: 4 bytes (command line arg)
MOVE: managed
INCREF: <array_store> -> rc=2
FREE #3: 4 bytes (cstring release)
ALLOC #4: 4 bytes (command line arg)
MOVE: managed
INCREF: <array_store> -> rc=2
FREE #4: 4 bytes (cstring release)
MOVE: result
INCREF: <array element return> -> rc=3
INCREF: <array element return> -> rc=3
INCREF: <array element return> -> rc=3
INCREF: <cstr> -> rc=4
fooDECREF: <cstr cleanup> -> rc=3
MOVE: managed

INCREF: <cstr> -> rc=4
barDECREF: <cstr cleanup> -> rc=3
MOVE: managed

INCREF: <cstr> -> rc=4
bazDECREF: <cstr cleanup> -> rc=3
MOVE: managed

DECREF: arg1 -> rc=2
DECREF: arg2 -> rc=2
DECREF: arg3 -> rc=2
DECREF: <array element> -> rc=1
DECREF: <array element> -> rc=1
DECREF: <array element> -> rc=1
DECREF: args -> rc=0
FREE #2: 168 bytes (array cleanup)

=== MEMORY STATS ===
Allocated: 180 bytes
Freed:     180 bytes
Leaked:    0 bytes
Moves:     7
Increfs:   10
Decrefs:   10
```

<!-- test: iterate-args -->
<!-- Args: a b c -->
```maxon
function main() returns int
    let args = CommandLine.args()
    var i = 0
    while i < args.count() 'loop'
        var arg = try args.get(i) otherwise ""
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
function main() returns int
    let args = CommandLine.args()
    // Args are strings, just verify we can access them
    if args.count() == 1 'check'
        var arg = try args.get(0) otherwise ""
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
function main() returns int
    let args = CommandLine.args()
    // Empty quoted arg
    var arg = try args.get(0) otherwise "x"
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
function main() returns int
    let args = CommandLine.args()
    var arg = try args.get(0) otherwise ""
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
function main() returns int
    let exe = CommandLine.executablePath()
    // Just verify it returns something (the actual path varies)
    if exe.byteLength() > 0 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```
