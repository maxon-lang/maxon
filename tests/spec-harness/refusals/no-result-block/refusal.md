---
feature: harness-refusal-no-result-block
---
# A case with a program and nothing to check

`SpecParser.pinsAnyResult` refuses it: running a case that pins nothing reports PASS for a
program nobody looked at.

## Tests

<!-- test: no-result-block -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
