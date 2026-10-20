---
feature: harness-refusal-program-outside-any-case
---
# A program outside any case

`SpecParser.extractTests` refuses it. No test marker opens a case above these blocks, so the program
and its expectation would run nowhere, with nothing reported.

## Tests

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
