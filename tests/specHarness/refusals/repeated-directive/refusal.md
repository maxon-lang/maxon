---
feature: harness-refusal-repeated-directive
---
# A directive written twice in one case

`SpecParser.scanTestFromMarker` refuses it. A case reads each directive once, so the later line would
silently overrule the earlier.

## Tests

<!-- test: first-case -->
<!-- procs: 1 -->
<!-- procs: 2 -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
