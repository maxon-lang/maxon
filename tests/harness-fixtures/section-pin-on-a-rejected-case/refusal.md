---
feature: harness-refusal-section-pin-on-a-rejected-case
---
# A section pin on a case that never links

`SpecParser.scanTestFromMarker` refuses it: `SpecExpectation.compilerError` has nowhere to
carry a pin, so the claim was dropped on the floor and the case passed on the diagnostic alone.

<!-- expect-refusal: expects a COMPILE ERROR (a ```maxoncstderr block) and also carries a -->

## Tests

<!-- test: section-pin-on-a-rejected-case -->

```maxon
function main() returns ExitCode
	return nosuchthing
end 'main'
```
```maxoncstderr
error E2004: <fragment>:3:9: Undefined variable 'nosuchthing'
```
```RequiredRdata
i64 999999
```
