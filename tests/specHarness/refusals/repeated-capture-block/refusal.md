---
feature: harness-refusal-repeated-capture-block
---
# Two capture blocks in one case

`SpecParser.scanTestFromMarker` refuses it. A case captures one trace, so an `mm-trace` block and a
`log-trace` block in one case would leave one of the two unread.

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

```mm-trace
```

```log-trace
```
