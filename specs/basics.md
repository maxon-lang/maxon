---
feature: basics
status: selfhosted
keywords: [main, return, semantic, validation]
category: basics
---

## Documentation

The compiler performs three semantic checks before lowering: every program must
declare a `main` function, `main` must return `ExitCode`, and `main` must take no
parameters.

### E3001: No main function

Every program must have a `main` function. If none is found:

```text
error E3001: No 'main' function found
```

### E3002: Main wrong return type

The `main` function must return `ExitCode`. With no return type (or a different
type):

```text
error E3002: Function 'main' must return ExitCode
```

### E3225: Main takes parameters

The `main` function is called with no arguments, so it must declare no parameters,
a parameter with a default value included. When `main` also does not return
`ExitCode`, both errors are reported:

```text
error E3225: Function 'main' must take no parameters
```

## Tests

These are the cases for the three semantic checks and the `return <int> → exit <int>`
case, written with little more than a function declaration, `return` and an integer literal.
The `no-main` case uses `ExitCode` rather than a `typealias`.

<!-- test: return-literal -->
```maxon
function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: no-main -->
```maxon
function notmain() returns ExitCode
	return 42
end 'notmain'
```
```maxoncstderr
error E3001: No 'main' function found
```

<!-- test: main-no-return-type -->
```maxon
function main()
	return
end 'main'
```
```maxoncstderr
error E3002: Function 'main' must return ExitCode
```

<!-- test: main-takes-a-parameter -->
```maxon
function main(code ExitCode) returns ExitCode
	return code
end 'main'
```
```maxoncstderr
error E3225: Function 'main' must take no parameters
```

<!-- test: main-takes-a-defaulted-parameter -->
```maxon
function main(code ExitCode = 0) returns ExitCode
	return code
end 'main'
```
```maxoncstderr
error E3225: Function 'main' must take no parameters
```

<!-- test: main-takes-a-parameter-and-returns-nothing -->
```maxon
function main(code ExitCode)
	print("{code}")
end 'main'
```
```maxoncstderr
error E3002: Function 'main' must return ExitCode
error E3225: Function 'main' must take no parameters
```
