---
feature: harness-refusal-preempt-marker-bad-value
---
# A preempt marker whose value nothing recognizes

`none` reads like a plausible way to withdraw preemption, so a reader that defaulted it would run the case under preemption in silence.

## Tests

<!-- test: preempt-marker-bad-value -->
<!-- preempt: none -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
