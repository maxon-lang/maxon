---
feature: harness-gate-target-ir-mint-leaves-the-other-lanes-owed
---
# The target-ir-mint-leaves-the-other-lanes-owed gate

The gate appends a wrong host-lane `TargetIr` pin to the case, so the spec is an IR spec holding a pin for one lane. `--update-required` must mint the host lane without being refused for the lanes still owed, and a plain run after it must be refused naming each of them.

## Tests

<!-- test: mint-owes-lanes.fresh -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
