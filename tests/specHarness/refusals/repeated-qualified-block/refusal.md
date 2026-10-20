---
feature: harness-refusal-repeated-qualified-block
---
# A second block qualified by the same target

`SpecParser.scanTestFromMarker` refuses it. Two blocks for one target in one case are one expectation
written twice, and the later would silently replace the earlier.

## Tests

<!-- test: first-case -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```

```Stdout:x64-linux
first
```

```Stdout:x64-linux
second
```
