---
feature: harness-gate-filter-union-beta
---
# The filter-union gate: beta

## Tests

<!-- test: beta.selected -->

```maxon
function main() returns ExitCode
	print("selected\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong
```
