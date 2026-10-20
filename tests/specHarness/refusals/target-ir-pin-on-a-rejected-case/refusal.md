---
feature: harness-refusal-target-ir-pin-on-a-rejected-case
---
# A Target IR pin on a case that never links

`SpecParser.scanTestFromMarker` refuses it: a case expecting a compile error renders no Target IR, so a
pin there could neither be compared nor re-minted.

## Tests

<!-- test: target-ir-pin-on-a-rejected-case -->

```maxon
function main() returns ExitCode
	return nosuchthing
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:9: Undefined variable 'nosuchthing'
```
```TargetIr:x64-linux
func @main
```
