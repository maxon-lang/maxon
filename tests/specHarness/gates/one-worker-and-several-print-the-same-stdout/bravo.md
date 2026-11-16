---
feature: harness-gate-one-worker-and-several-print-the-same-stdout-bravo
---
# The one-worker-and-several gate: bravo

A run case that waits beside one that fails on a deliberately wrong expectation, so a failure's reason is part of the stdout compared.

## Tests

<!-- test: bravo.waits -->

```maxon
function main() returns ExitCode
	sleep(0)
	print("bravo waited\n")
	return 0
end 'main'
```

```stdout
bravo waited
```

<!-- test: bravo.fails-on-its-stdout -->

```maxon
function main() returns ExitCode
	print("bravo said this\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong
```
