---
feature: harness-refusal-two-directives-on-one-line
---
# Two known directives on one line

`SpecParser.scanTestFromMarker` refuses it. A line is read by one marker arm, so the `preempt: off`
beside `procs: 1` would be discarded without a word and the case would run with preemption on.

## Tests

<!-- test: first-case -->
<!-- procs: 1 --> <!-- preempt: off -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
