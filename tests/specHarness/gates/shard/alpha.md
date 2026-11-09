---
feature: harness-gate-shard-alpha
---
# The shard gate: alpha

## Tests

<!-- test: alpha.selected -->

```maxon
function main() returns ExitCode
	print("selected\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong
```
