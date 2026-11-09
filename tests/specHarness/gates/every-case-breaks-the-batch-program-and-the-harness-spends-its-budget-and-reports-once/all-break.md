---
feature: harness-gate-every-case-breaks-the-batch-program-and-the-harness-spends-its-budget-and-reports-once
---
# The every-case-breaks-the-batch-program-and-the-harness-spends-its-budget-and-reports-once gate

Sixteen plain run cases, each of which declares its `main` inside a conditional-compilation arm no target takes. Each fails alone with E3001 and, in the shared program, leaves its generated call undefined in the entry file, which names no case. Isolating all sixteen by trial builds costs more than the harness may spend, so the run must stop at its budget, fail no case as the culprit of the cases it did not reach, and say once, naming them, that it could not isolate them.

## Tests

<!-- test: all-break.case-01 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-02 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-03 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-04 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-05 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-06 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-07 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-08 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-09 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-10 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-11 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-12 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-13 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-14 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-15 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```

<!-- test: all-break.case-16 -->

```maxon
#if os(Windows) and os(Linux)
function main() returns ExitCode
	return 0
end 'main'
#endif
```

```exitcode
0
```
