---
feature: harness-gate-shard-echo
---
# The shard gate: echo

## Tests

<!-- test: echo.selected -->

```maxon
function main() returns ExitCode
	print("selected\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong
```
