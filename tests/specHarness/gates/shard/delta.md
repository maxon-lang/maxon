---
feature: harness-gate-shard-delta
---
# The shard gate: delta

## Tests

<!-- test: delta.selected -->

```maxon
function main() returns ExitCode
	print("selected\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong
```
