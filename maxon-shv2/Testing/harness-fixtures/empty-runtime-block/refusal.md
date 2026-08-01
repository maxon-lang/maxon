---
feature: harness-refusal-empty-runtime-block
---
# A ```RequiredRuntime block that names nothing

`SpecParser.pushRuntimeNames` refuses an empty block: the runner suppresses
`--emit-ir-runtime=` when the list is empty, so the compiler's own misspelling refusal never
runs and the golden is byte-identical to a test that never asked.

<!-- expect-refusal: carries a ```RequiredRuntime block that pins NOTHING -->

## Tests

<!-- test: empty-runtime-block -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
```
