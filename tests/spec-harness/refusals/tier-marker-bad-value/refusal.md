---
feature: harness-refusal-tier-marker-bad-value
---
# A tier marker whose value nothing recognizes

`SpecParser.parseTierValue` refuses it. The failure it prevents is silent and fails OPEN: an unrecognized
value read as "the author's code" compiles a case that said its subject is a library-tier surface as an
ordinary program, so the refusal that tier exists to lift is reported as the case's own result.

`user` is the value chosen here deliberately: it is the name of the tier a case without the marker already
has, and therefore the shape of mistake a fail-open reader would swallow in silence.

## Tests

<!-- test: tier-marker-bad-value -->
<!-- tier: user -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
