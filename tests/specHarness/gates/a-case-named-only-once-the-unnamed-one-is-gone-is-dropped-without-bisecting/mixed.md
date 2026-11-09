---
feature: harness-gate-a-case-named-only-once-the-unnamed-one-is-gone-is-dropped-without-bisecting
---
# The a-case-named-only-once-the-unnamed-one-is-gone-is-dropped-without-bisecting gate

Four plain run cases. The second declares no `main` the compiler sees, so the shared program's compile reports only an error in the generated entry file, which names no case, and the compiler holds back the unused-binding error of the third case until that one is gone. The harness isolates the second case by trial builds; the trial that holds the third then names it in its own file, and it is dropped from that diagnostic. Both failing cases must say they broke the batch program, and the two healthy cases must pass from one program built without them.

## Tests

<!-- test: mixed.first -->

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

<!-- test: mixed.declares-no-main -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	print("never built\n")
	return 0
end 'main'
#endif
```

```exitcode
0
```

```stdout
never built
```

<!-- test: mixed.binds-nothing-it-reads -->

```maxon
function main() returns ExitCode
	let unused = 5
	return 0
end 'main'
```

```exitcode
0
```

<!-- test: mixed.second -->

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
