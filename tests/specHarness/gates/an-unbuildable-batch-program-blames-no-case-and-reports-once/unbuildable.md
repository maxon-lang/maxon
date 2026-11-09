---
feature: harness-gate-an-unbuildable-batch-program-blames-no-case-and-reports-once
---
# The an-unbuildable-batch-program-blames-no-case-and-reports-once gate

Three plain run cases that each pass alone. The gate plants a regular file where the harness stages the shared program's sources, so the harness cannot build that program for a reason that belongs to the machine and to no case. The run must blame no case for it, say once that the program could not be built and why, name the cases that ran alone instead, and let their own results decide them.

## Tests

<!-- test: unbuildable.first -->

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

<!-- test: unbuildable.second -->

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

<!-- test: unbuildable.third -->

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
