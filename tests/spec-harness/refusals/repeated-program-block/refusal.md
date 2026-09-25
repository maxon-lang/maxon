---
feature: harness-refusal-repeated-program-block
---
# A second program block in one case

`SpecParser.scanTestFromMarker` refuses it. The later block would silently replace the first, which is
how a case whose own marker is missing merges into the case above it.

## Tests

<!-- test: first-case -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```maxon
function main() returns ExitCode
	return 1
end 'main'
```

```exitcode
0
```
