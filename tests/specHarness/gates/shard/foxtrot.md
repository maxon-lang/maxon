---
feature: harness-gate-shard-foxtrot
---
# The shard gate: foxtrot

## Tests

<!-- test: foxtrot.selected -->

```maxon
function main() returns ExitCode
	print("selected\n")
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong
```
