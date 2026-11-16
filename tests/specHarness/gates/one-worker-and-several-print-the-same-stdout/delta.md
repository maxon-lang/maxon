---
feature: harness-gate-one-worker-and-several-print-the-same-stdout-delta
---
# The one-worker-and-several gate: delta

A run case that waits beside one that runs in a process of its own.

## Tests

<!-- test: delta.waits -->

```maxon
function main() returns ExitCode
	sleep(0)
	print("delta waited\n")
	return 0
end 'main'
```

```stdout
delta waited
```

<!-- test: delta.alone -->
<!-- process: own -->

```maxon
function main() returns ExitCode
	print("delta alone\n")
	return 0
end 'main'
```

```stdout
delta alone
```
