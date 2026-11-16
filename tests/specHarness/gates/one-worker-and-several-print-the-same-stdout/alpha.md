---
feature: harness-gate-one-worker-and-several-print-the-same-stdout-alpha
---
# The one-worker-and-several gate: alpha

A run case that waits beside a run case with its own exit code.

## Tests

<!-- test: alpha.waits -->

```maxon
function main() returns ExitCode
	sleep(0)
	print("alpha waited\n")
	return 0
end 'main'
```

```stdout
alpha waited
```

<!-- test: alpha.exits-two -->

```maxon
function main() returns ExitCode
	print("alpha exits\n")
	return 2
end 'main'
```

```exitcode
2
```

```stdout
alpha exits
```
