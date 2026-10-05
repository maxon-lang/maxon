---
feature: harness-gate-target-ir-pin-remint
---
# The target-ir-pin-remint gate

The gate appends a wrong host-lane `TargetIr` pin to the last case; `--update-required` must replace it and add one to the first case.

## Tests

<!-- test: pin-remint.absent -->

```maxon
function main() returns ExitCode
	return 5
end 'main'
```

```exitcode
5
```

<!-- test: pin-remint.planted -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
