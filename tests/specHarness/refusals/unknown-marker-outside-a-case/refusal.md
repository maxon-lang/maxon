---
feature: harness-refusal-unknown-marker-outside-a-case
---
# A comment line outside any case that no marker reads

`SpecParser.extractTests` refuses it. A case begins only at a marker the harness recognizes, so a
misspelt one opens no case at all: the program and expectation beneath it would read as prose and the
case would run nowhere, with nothing reported.

`tests:` is one character from `test:`.

## Tests

<!-- tests: first-case -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
