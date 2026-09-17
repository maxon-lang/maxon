---
feature: harness-refusal-runs-marker-bad-value
---
# A runs marker whose value nothing recognizes

`SpecParser.parseRunsValue` refuses it. The failure it prevents is silent and fails OPEN: an
unrecognized value read as "shares the pool" runs the case beside every other worker, which is the one
condition the marker exists to keep it from.

`serial` is the value chosen here deliberately: it is a plausible spelling of what the marker DOES, and
therefore the shape of typo a fail-open reader would swallow in silence.

<!-- expect-refusal: 'alone' is the only value this marker takes -->

## Tests

<!-- test: runs-marker-bad-value -->
<!-- runs: serial -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
