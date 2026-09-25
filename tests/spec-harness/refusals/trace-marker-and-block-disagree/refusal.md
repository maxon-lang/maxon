---
feature: harness-refusal-trace-marker-and-block-disagree
---
# A trace marker and a capture block naming different families

`SpecParser.scanTestFromMarker` refuses it. The marker asks for one family and the block pins another,
so one of the two would be discarded without a word.

## Tests

<!-- test: first-case -->
<!-- LogTrace -->

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
