---
feature: harness-refusal-two-trace-markers-on-one-case
---
# Both trace markers on one case

`SpecParser.scanTestFromMarker` refuses it. A case captures one trace family, so one of the two
markers would be discarded without a word.

## Tests

<!-- test: first-case -->
<!-- MmTrace -->
<!-- LogTrace -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
