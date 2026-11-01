---
feature: harness-gate-target-ir-pin-mismatch-fails
---
# The target-ir-pin-mismatch-fails gate

The gate appends a wrong host-lane `TargetIr` pin to the case and a placeholder pin for each other lane beneath its marker, so the spec is a complete IR spec; the host-lane pin must then fail the case.

## Tests

<!-- test: pin-mismatch.pinned -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
