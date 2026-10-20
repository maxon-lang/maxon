---
feature: harness-gate-no-fragments-written
---
# The no-fragments-written gate

A passing run case and a passing compile-error case, after which no `fragments/` directory may exist.

## Tests

<!-- test: no-fragments.runs -->

```maxon
function main() returns ExitCode
	print("ran\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
ran
```

<!-- test: no-fragments.refused -->

```maxon
function main() returns ExitCode
	let unread = 5
	return 0
end 'main'
```

```maxoncstderr
error E3012: <fragment>:3:6: unused variable: 'unread'
```
