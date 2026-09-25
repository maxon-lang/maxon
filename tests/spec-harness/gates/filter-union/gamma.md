---
feature: harness-gate-filter-union-gamma
---
# The filter-union gate: gamma

## Tests

<!-- test: gamma.selected -->

```maxon
function main() returns ExitCode
	print("selected\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong
```
