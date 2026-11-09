---
feature: harness-gate-a-case-dropped-by-name-from-the-batch-program-carries-the-break-note
---
# The a-case-dropped-by-name-from-the-batch-program-carries-the-break-note gate

Three plain run cases. The middle one holds an unused binding, which the compiler reports in the case's own file once the front end has accepted its declarations, so it is placed in the shared program and refused there with a diagnostic that names its staged file. The run must fail the middle case only, say that it broke the batch program, and pass the two healthy cases from a program built without it.

## Tests

<!-- test: named-error.first -->

```maxon
function main() returns ExitCode
	print("first\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
first
```

<!-- test: named-error.binds-nothing-it-reads -->

```maxon
function main() returns ExitCode
	let unused = 5
	return 0
end 'main'
```

```exitcode
0
```

<!-- test: named-error.second -->

```maxon
function main() returns ExitCode
	print("second\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
second
```
