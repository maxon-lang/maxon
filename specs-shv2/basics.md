---
feature: basics
status: selfhosted
keywords: [main, return, semantic, validation]
category: basics
milestone: M1
---

## Documentation

`maxon-shv2` performs two semantic checks before lowering: every program must
declare a `main` function, and `main` must return `ExitCode`. The walking
skeleton (M1) compiles exactly the `return <int-literal>` slice.

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

These are the M1 slice of `specs/basics.md` — the two semantic-error cases and
the `return <int> → exit <int>` case — restricted to what the M1 parser accepts
(function declaration, `return`, integer literal). The `no-main` case uses
`ExitCode` (the one builtin type M1 resolves) rather than a `typealias`, and the
`return getValue()` / float / if-else cases from `specs/basics.md` are deferred
to their milestones (M3/M4).

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

<!-- disabled-test: disabled-marker-is-honored -->
```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
1
```
