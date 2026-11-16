---
feature: harness-refusal-empty-required-functions-block
---
# A ```RequiredFunctions block that names nothing

`SpecParser.pushRequiredFunctionNames` refuses an empty block: the runner suppresses
`--emit-ir-function=` when the list is empty, so the compiler's own misspelling refusal never
runs and the rendered Target IR is byte-identical to a test that never asked.

## Tests

<!-- test: empty-required-functions-block -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredFunctions
```
