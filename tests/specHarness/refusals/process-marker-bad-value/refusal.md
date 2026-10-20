---
feature: harness-refusal-process-marker-bad-value
---
# A process marker whose value nothing recognizes

`SpecParser.parseProcessValue` refuses it. The failure it prevents is silent and fails OPEN: an
unrecognized value read as "may share a process" batches a case that said it needs a process of its own,
which is the one condition the marker exists to prevent.

`private` is the value chosen here deliberately: it is a plausible spelling of what the marker DOES, and
therefore the shape of typo a fail-open reader would swallow in silence.

## Tests

<!-- test: process-marker-bad-value -->
<!-- process: private -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
