---
feature: harness-gate-a-case-that-breaks-the-batch-program-is-named-and-its-siblings-pass
---
# The a-case-that-breaks-the-batch-program-is-named-and-its-siblings-pass gate

Three plain run cases. The middle one declares its `main` inside a conditional-compilation arm no target takes, so the text the batching checks read holds exactly one `main` while the compiler sees none. Alone it fails with E3001, and in the shared program its generated call `__spec_case_<n>()` is undefined: the diagnostic lands in the generated entry file, which names no case. The two healthy cases compile and pass alone, so the run must fail the middle case only, say that it broke the batch program, and pass the other two.

## Tests

<!-- test: no-main.first -->

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

<!-- test: no-main.declares-no-main -->

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

<!-- test: no-main.second -->

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
