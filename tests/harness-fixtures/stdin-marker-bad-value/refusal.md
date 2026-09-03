---
feature: harness-refusal-stdin-marker-bad-value
---
# A stdin marker whose value nothing recognizes

`SpecParser.parseStdinValue` refuses it. The failure it prevents is silent and fails OPEN: an
unrecognized value read as "the default stdin" gives the case the null device, so every read answers at
once with EOF — and a case written to assert that a program BLOCKS would then report PASS without
anything ever having blocked.

`pipe` is the value chosen here deliberately: it is a plausible spelling of what the marker DOES, and
therefore the shape of typo a fail-open reader would swallow in silence.

<!-- expect-refusal: 'hold' and 'delayed' are the only values this marker takes -->

## Tests

<!-- test: stdin-marker-bad-value -->
<!-- stdin: pipe -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
