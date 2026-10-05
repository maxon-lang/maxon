---
feature: harness-gate-a-panic-mid-batch-is-attributed
---
# The a-panic-mid-batch-is-attributed gate

The middle case of three batched cases crashes unexpectedly, and only it may fail for that crash.

## Tests

<!-- test: panic-mid-batch.one -->

```maxon
function main() returns ExitCode
	print("one\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
one
```

<!-- test: panic-mid-batch.two -->

```maxon
function main() returns ExitCode
	print("two\n")
	panic("planted by the a-panic-mid-batch-is-attributed gate")
end 'main'
```

```exitcode
0
```

```stdout
two
```

<!-- test: panic-mid-batch.three -->

```maxon
function main() returns ExitCode
	print("three\n")
	return 4
end 'main'
```

```exitcode
4
```

```stdout
three
```
