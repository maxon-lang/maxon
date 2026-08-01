---
feature: harness-refusal-runtime-name-parsed-from-source
---
# A ```RequiredRuntime name the fragment already shows

`printTargetModule` refuses it: naming a function parsed from source pins nothing new, and
reading the block as though it had would be a reader's mistake.

<!-- expect-refusal: is not a compiler-emitted runtime function -->

## Tests

<!-- test: runtime-name-parsed-from-source -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
main
```
