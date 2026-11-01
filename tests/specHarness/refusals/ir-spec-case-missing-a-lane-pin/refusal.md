---
feature: harness-refusal-ir-spec-case-missing-a-lane-pin
---
# A case in an IR spec that carries no pin for a lane it runs on

`SpecParser.requireEveryLanePinned` refuses it, on any host and whatever lane the run is for. A spec that holds
a `TargetIr` block is an IR spec, and a lane whose block a case lacks is a lane nothing gates: a run on that lane
compares nothing and passes whatever the compiler emits there. The refusal names the case, the lanes it lacks and
the `--update-required --target=<lane>` run that mints each.

## Tests

<!-- test: ir-spec-case-missing-a-lane-pin -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
```TargetIr:x64-windows
func @main
```
