---
feature: harness-refusal-second-directive-on-a-claimed-line
---
# An unread directive beside a claimed one

`SpecParser.scanTestFromMarker` refuses it. The `procs:` arm claims the line, so the second comment on
it would otherwise be discarded without a word.

## Tests

<!-- test: first-case -->
<!-- procs: 1 --> <!-- bogus -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
