---
feature: harness-gate-unbatchable-cases-run-alone
---
# The unbatchable-cases-run-alone gate

Two plain run cases batch together; the stderr case and the Args case run alone; the compile-error case runs nothing.

## Tests

<!-- test: unbatchable.plain-first -->

```maxon
function main() returns ExitCode
	print("plain first\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
plain first
```

<!-- test: unbatchable.plain-second -->

```maxon
function main() returns ExitCode
	print("plain second\n")
	return 2
end 'main'
```

```exitcode
2
```

```stdout
plain second
```

<!-- test: unbatchable.pins-stderr -->

```maxon
function main() returns ExitCode
	printError("to stderr\n")
	return 0
end 'main'
```

```exitcode
0
```

```stderr
to stderr
```

<!-- test: unbatchable.takes-args -->
<!-- Args: alpha beta -->

```maxon
function main() returns ExitCode
	return CommandLine.args().count()
end 'main'
```

```exitcode
3
```

<!-- test: unbatchable.refused -->

```maxon
function main() returns ExitCode
	let unread = 5
	return 0
end 'main'
```

```maxoncstderr
error E3012: <fragment>:3:6: unused variable: 'unread'
```
