---
feature: harness-gate-an-unremovable-entry-in-the-batch-source-root-is-a-harness-error-and-the-cases-run-alone
---
# The lockedbatch gate

Three plain run cases that each pass alone. The gate plants, in the directory where the harness stages the shared program's sources, a file the host refuses to remove (read-only on Windows, in an unwritable directory elsewhere). The harness empties that directory before it stages the program, cannot remove the file, and must refuse to stage the program there: it says once, naming the file, that the program could not be built, blames no case for it, and lets the cases' own runs alone decide them.

## Tests

<!-- test: lockedbatch.first -->

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

<!-- test: lockedbatch.second -->

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

<!-- test: lockedbatch.third -->

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
