---
feature: harness-gate-two-cases-that-break-the-batch-program-are-both-named-and-the-rest-pass
---
# The two-cases-that-break-the-batch-program-are-both-named-and-the-rest-pass gate

Five plain run cases, the second and fourth of which declare their `main` inside a conditional-compilation arm no target takes. Each fails alone with E3001 and, in the shared program, leaves its generated call undefined in the entry file, which names no case. Finding both takes more than one split of the five, and the three healthy cases must come out passing, built into one program that holds only them.

## Tests

<!-- test: two-no-mains.first -->

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

<!-- test: two-no-mains.declares-no-main-early -->

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

<!-- test: two-no-mains.third -->

```maxon
function main() returns ExitCode
	print("third\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
third
```

<!-- test: two-no-mains.declares-no-main-late -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	print("never built either\n")
	return 0
end 'main'
#endif
```

```exitcode
0
```

```stdout
never built either
```

<!-- test: two-no-mains.fifth -->

```maxon
function main() returns ExitCode
	print("fifth\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
fifth
```
