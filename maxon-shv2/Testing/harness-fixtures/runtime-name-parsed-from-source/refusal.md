---
feature: harness-refusal-runtime-name-parsed-from-source
---
# A ```RequiredRuntime name the fragment already shows

`printTargetModule` refuses it: naming one of the PROGRAM's own functions pins nothing new, and
reading the block as though it had would be a reader's mistake. (A `stdlib/` body is NOT one of
these — the fragment withholds the library, so naming one renders something the golden did not
carry.)

<!-- expect-refusal: is the PROGRAM's own function -->

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
