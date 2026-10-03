---
feature: basics
status: selfhosted
keywords: [main, return, semantic, validation]
category: basics
---

## Documentation

The compiler performs two semantic checks before lowering: every program must
declare a `main` function, and `main` must return `ExitCode`.

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

## Tests

These are the two semantic-error cases and the `return <int> → exit <int>` case,
written with nothing but a function declaration, `return` and an integer literal.
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
