---
feature: harness-gate-target-ir-remint-past-a-compile-error-case
---
# The target-ir-remint-past-a-compile-error-case gate

The gate appends a wrong host-lane `TargetIr` pin to the last case, so the spec holds a pin; `--update-required` must re-mint it and leave the first case, which expects a compile error and renders no Target IR, unpinned and passing.

## Tests

<!-- test: remint-past-error.rejected -->

```maxon
function main() returns ExitCode
	return nosuchthing
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:9: Undefined variable 'nosuchthing'
```

<!-- test: remint-past-error.pinned -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
