---
feature: harness-gate-shard-charlie
---
# The shard gate: charlie

## Tests

<!-- test: charlie.selected -->

```maxon
function main() returns ExitCode
	print("selected\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong
```
