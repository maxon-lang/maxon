---
feature: harness-gate-result-json
---
# The result-json gate

## Tests

<!-- test: result-json.holds -->

```maxon
function main() returns ExitCode
	print("kept\n")
	return 0
end 'main'
```

```stdout
kept
```

<!-- test: result-json.breaks -->

```maxon
function main() returns ExitCode
	print("noise\n")
	print("FAIL ghost/phantom: not a verdict\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong
```
