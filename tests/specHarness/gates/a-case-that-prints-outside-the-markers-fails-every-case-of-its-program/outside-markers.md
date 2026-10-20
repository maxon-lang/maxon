---
feature: harness-gate-a-case-that-prints-outside-the-markers-fails-every-case-of-its-program
---
# The a-case-that-prints-outside-the-markers-fails-every-case-of-its-program gate

The first case prints the batch marker prefix itself, which no batching check can see in its source as a case that runs code before `main`. Batched, the program's output carries text no case marker owns, so every case of that program FAILS with a message naming the cause; each then passes when compiled and run alone, and the failure says so.

## Tests

<!-- test: outside-markers.forges -->

```maxon
function main() returns ExitCode
	let left = "@@maxon-"
	let right = "spec@@"
	print("{left}{right}junk\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
@@maxon-spec@@junk
```

<!-- test: outside-markers.plain-first -->

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

<!-- test: outside-markers.plain-second -->

```maxon
function main() returns ExitCode
	print("plain second\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
plain second
```
