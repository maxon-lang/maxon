---
feature: command-line-args
status: stable
keywords: [args, argv, command-line, main, parameters]
category: functions
---

# Command Line Arguments

## Documentation

# Command Line Arguments

Access command line arguments in Maxon by adding `args Array of String` parameter to `main()`.

## Syntax

```text
function main(args Array of String) returns int
    // args[0] is the program name/path
    // args[1..n] are user-provided arguments
    return 0
end 'main'
```

## Properties

- `args[0]` - The program name or path (always present)
- `args[1]`, `args[2]`, etc. - User-provided arguments
- `args.count()` - Total number of arguments (including program name)

## Example

```text
function main(args Array of String) returns int
    print("Program: ")
    print(args[0])
    
    if args.count() > 1 'has-args'
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
function main(args Array of String) returns int
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
function main(args Array of String) returns int
    // Without extra args, count should be 1 (just program name)
    return args.count()
end 'main'
```
```exitcode
1
```

<!-- test: args-with-one-arg -->
<!-- Args: hello -->
```maxon
function main(args Array of String) returns int
    return args.count()
end 'main'
```
```exitcode
2
```

<!-- test: args-with-multiple-args -->
<!-- Args: one two three -->
```maxon
function main(args Array of String) returns int
    return args.count()
end 'main'
```
```exitcode
4
```

<!-- test: access-first-arg -->
<!-- Args: hello -->
```maxon
function main(args Array of String) returns int
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
function main(args Array of String) returns int
    print(args[1])
    print("\n")
    print(args[2])
    print("\n")
    print(args[3])
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
function main(args Array of String) returns int
    var i = 1
    while i < args.count() 'loop'
        print(args[i])
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
function main(args Array of String) returns int
    // Args are strings, just verify we can access them
    if args.count() == 2 'check'
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
function main(args Array of String) returns int
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
function main(args Array of String) returns int
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

