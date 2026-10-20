---
feature: harness-gate-target-ir-pin-mismatch-fails
---
# The target-ir-pin-mismatch-fails gate

The gate appends a wrong host-lane `TargetIr` pin to the last case, which must then fail while the first case passes.

## Tests

<!-- test: pin-mismatch.unpinned -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```

<!-- test: pin-mismatch.pinned -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
