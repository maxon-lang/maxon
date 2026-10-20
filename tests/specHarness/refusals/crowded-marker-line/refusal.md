---
feature: harness-refusal-crowded-marker-line
---
# A directive on the test marker line

`SpecParser.scanTestFromMarker` refuses it. The marker line is read for the case's name alone, so the
`procs: 1` beside it would be discarded without a word.

## Tests

<!-- test: a --> <!-- procs: 1 -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
