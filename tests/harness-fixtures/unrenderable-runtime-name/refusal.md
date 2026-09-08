---
feature: harness-refusal-unrenderable-runtime-name
---
# A ```RequiredRuntime name nothing emits

`TargetPrinter.requireEveryNameRendered` refuses it: a misspelling would add nothing to the
fragment and the case would pass for ever while pinning exactly what it pinned before.

<!-- expect-refusal: so a ```RequiredRuntime block naming it would render nothing -->

## Tests

<!-- test: unrenderable-runtime-name -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
__no_such_emitted_function
```
