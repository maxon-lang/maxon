---
feature: harness-refusal-unrenderable-required-function
---
# A ```RequiredFunctions name nothing emits

`TargetPrinter.requireEveryNameRendered` refuses it: a misspelling would add nothing to the
fragment and the case would pass for ever while pinning exactly what it pinned before.

## Tests

<!-- test: unrenderable-required-function -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredFunctions
__no_such_emitted_function
```
